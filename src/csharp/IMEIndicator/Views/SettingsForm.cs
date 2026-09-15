// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.ComponentModel;
using IMEIndicator.App;
using IMEIndicator.Interop;
using IMEIndicator.Models;
using IMEIndicator.Models.Hotkey;
using IMEIndicator.Services.Hotkey;
using IMEIndicator.Settings;

namespace IMEIndicator.Views;

/// <summary>
/// 設定画面本体。インジケーター設定・プロセス優先度ルール・ホットキーをまとめて編集する。
/// 移植元: <c>src/cpp/views/SettingsDialog.h</c> / <c>.cpp</c> の <c>class SettingsDialog</c>。
/// 項目・順序は specs/014-port-to-csharp/contracts/ui-parity-contract.md §4 を正とする。
/// </summary>
/// <remarks>
/// C++ 版はルール・ホットキーの編集内容を <c>workingRules_</c> / <c>workingHotkeys_</c> という
/// 作業コピーへ保持し、適用/OK 時にのみ <see cref="SettingsManager.Settings"/> へ反映する
/// （キャンセル時は作業コピーごと破棄され、実設定は変更されない）。本実装もこの設計を踏襲するが、
/// C++ 版が値型 <c>AppSettings</c> のコピーで実現している「未適用の変更を隔離する」効果は、
/// C# 版では参照型の <see cref="AppSettings"/> を直接書き換える代わりに、ルール・ホットキーの
/// リストだけを作業コピー（<see cref="_workingRules"/> / <see cref="_workingHotkeys"/>）として
/// 分離することで同等に実現している（他のフィールドは <see cref="ReadTo"/> が呼ばれるまで
/// 一切書き換えないため、結果的に C++ 版と同じ「未適用は破棄される」挙動になる）。
/// </remarks>
public sealed class SettingsForm : Form
{
    private const int MaxRules = 30;
    private const int MaxHotkeys = 256;

    private readonly SettingsManager _settingsManager;

    // ---- インジケーター ----
    private readonly CheckBox _visibleCheckBox = new() { Text = "インジケーター表示", AutoSize = true };
    private readonly NumericUpDown _sizeUpDown = new() { Minimum = 20, Maximum = 100, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly NumericUpDown _opacityUpDown = new() { Minimum = 0.10m, Maximum = 1.00m, DecimalPlaces = 2, Increment = 0.05m, Dock = DockStyle.Fill };
    private readonly NumericUpDown _offsetXUpDown = new() { Minimum = -1_000_000, Maximum = 1_000_000, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly NumericUpDown _offsetYUpDown = new() { Minimum = -1_000_000, Maximum = 1_000_000, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly TextBox _imeOnTextBox = new() { MaxLength = 8, Dock = DockStyle.Fill };
    private readonly TextBox _imeOffTextBox = new() { MaxLength = 8, Dock = DockStyle.Fill };

    // ---- 背景画像（015-split-appearance-settings。インジケーターとは独立、FR-003） ----
    private readonly CheckBox _backgroundImageCheckBox = new() { Text = "背景画像表示", AutoSize = true };
    private readonly NumericUpDown _backgroundImageSizeUpDown = new() { Minimum = AppConstants.BackgroundImageMinSize, Maximum = AppConstants.BackgroundImageMaxSize, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly NumericUpDown _backgroundImageOpacityUpDown = new() { Minimum = 0.10m, Maximum = 1.00m, DecimalPlaces = 2, Increment = 0.05m, Dock = DockStyle.Fill };
    private readonly TextBox _backgroundImagePathTextBox = new();
    private readonly Button _backgroundImageBrowseButton = new() { Text = "参照...", AutoSize = true };
    private readonly Button _backgroundImageClearButton = new() { Text = "クリア", AutoSize = true };

    // 018-draggable-background-image: 背景画像の位置。Ctrl+ドラッグの操作説明と、
    // ドラッグで確定した位置を既定へ戻すボタン（US5）。保留の扱いは
    // contracts/settings-ui-contract.md「既定の位置に戻す（保留の扱い）」のとおり。
    private readonly Button _backgroundImageResetPositionButton = new() { Text = "既定の位置に戻す", AutoSize = true };
    private readonly Label _backgroundImagePositionHintLabel = new()
    {
        Text = "Ctrl キーを押しながら画像をドラッグすると、表示位置を移動できます",
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        ForeColor = SystemColors.GrayText,
    };

    // 「既定の位置に戻す」ボタンが押されてから ReadTo() で実際に Position = null が
    // 書き込まれるまでの保留フラグ（FR-016: ドラッグで確定した位置を設定画面の操作で
    // 書き換えない。OnApply() で一度 false に戻すため、適用後のドラッグは巻き戻らない）。
    private bool _resetPositionPending;

    // ---- 全般 ----
    private readonly ComboBox _logLevelComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };

    // ---- プロセス優先度ルール ----
    private readonly ListView _rulesListView = new()
    {
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        MultiSelect = false,
        HideSelection = false,
        Size = new Size(420, 200),
    };
    private readonly Button _addRuleButton = new() { Text = "追加", AutoSize = true };
    private readonly Button _editRuleButton = new() { Text = "編集", AutoSize = true };
    private readonly Button _deleteRuleButton = new() { Text = "削除", AutoSize = true };
    private readonly NumericUpDown _pollingUpDown = new() { Minimum = 1, Maximum = 1800, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly Label _adminStatusLabel = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(420, 0) };

    // ---- ホットキー ----
    private readonly ListView _hotkeyListView = new()
    {
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        MultiSelect = false,
        HideSelection = false,
        Size = new Size(420, 150),
    };
    private readonly Button _addHotkeyButton = new() { Text = "追加", AutoSize = true };
    private readonly Button _editHotkeyButton = new() { Text = "編集", AutoSize = true };
    private readonly Button _deleteHotkeyButton = new() { Text = "削除", AutoSize = true };

    private readonly Button _okButton = new() { Text = "OK", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "キャンセル", AutoSize = true };
    private readonly Button _applyButton = new() { Text = "適用", AutoSize = true };

    private List<ProcessPriorityRule> _workingRules = [];
    private List<HotKeyEntry> _workingHotkeys = [];

    /// <summary>適用/OK で保存が完了した直後に呼ばれる。引数は保存後の設定。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<AppSettings>? AppliedCallback { get; set; }

    /// <summary>管理者権限不足カウンタの取得元（移植元 spec T095 相当）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<int>? AccessDeniedCountCallback { get; set; }

    /// <summary>
    /// 各ルールが管理者権限不足で制御不能かどうかを判定する。戻り値は引数と同じ件数・順序。
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<IReadOnlyList<ProcessPriorityRule>, IReadOnlyList<bool>>? AccessibilityProbeCallback { get; set; }

    public SettingsForm(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        Text = AppConstants.SettingsTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScroll = true;
        Padding = new Padding(10);

        TableLayoutPanel root = BuildLayout();
        WireEvents();
        LoadFrom(_settingsManager.Settings);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        ApplyContentHeight(root);
    }

    // 幅は 480 固定のまま、ルール・ホットキー件数等で変動する内容に必要な高さを実測して
    // ClientSize に反映する。以前は ClientSize を 900px 固定にしていたため、内容がそれを
    // 超えると root（Dock = Fill）が超過分を内部でクリップし、OK/キャンセル/適用ボタンが
    // 画面上どこにも表示・到達できなくなる不具合があった。画面の作業領域より高くなる場合は
    // 上限でクランプし、AutoScroll（コンストラクタで設定済み）で残りを閲覧できるようにする。
    private void ApplyContentHeight(Control root)
    {
        const int width = 480;
        int availableWidth = width - Padding.Horizontal;
        int contentHeight = root.GetPreferredSize(new Size(availableWidth, 0)).Height + Padding.Vertical;

        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int maxHeight = Math.Max(300, work.Height - 40);

        ClientSize = new Size(width, Math.Min(contentHeight, maxHeight));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PositionAboveTaskbar();
    }

    // 移植元 SettingsDialog::show() の作業領域計算（SPI_GETWORKAREA を用いた
    // 「横中央・下端はタスクバー直上」の位置決め）と同じ式を WinForms の作業領域で再現する。
    private void PositionAboveTaskbar()
    {
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int x = work.Left + Math.Max(0, (work.Width - Width) / 2);
        int y = Math.Max(work.Top + 10, work.Bottom - Height - 10);
        Location = new Point(x, y);
    }

    // ------------------------------------------------------------------
    // レイアウト構築
    // ------------------------------------------------------------------

    private TableLayoutPanel BuildLayout()
    {
        // Dock = Top + AutoSize（Fill ではない）にすることで、内容の実際の高さが
        // ApplyContentHeight から GetPreferredSize 経由で正しく測定できるようにする。
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 表示順は contracts/settings-ui-contract.md 「表示順」節のとおり
        // （インジケーター→背景画像→全般→プロセス優先度ルール→ホットキー→ボタン）。
        root.Controls.Add(BuildIndicatorGroup(), 0, 0);
        root.Controls.Add(BuildBackgroundImageGroup(), 0, 1);
        root.Controls.Add(BuildGeneralGroup(), 0, 2);
        root.Controls.Add(BuildRulesGroup(), 0, 3);
        root.Controls.Add(BuildHotkeysGroup(), 0, 4);
        root.Controls.Add(BuildButtonRow(), 0, 5);

        Controls.Add(root);
        return root;
    }

    private GroupBox BuildIndicatorGroup()
    {
        var group = new GroupBox { Text = "インジケーター", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(6) };

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddRow(table, "表示", _visibleCheckBox);
        AddRow(table, "サイズ (20-100)", _sizeUpDown);
        AddRow(table, "不透明度 (0.10-1.00)", _opacityUpDown);
        AddRow(table, "オフセット X", _offsetXUpDown);
        AddRow(table, "オフセット Y", _offsetYUpDown);
        AddRow(table, "IME ON テキスト", _imeOnTextBox);
        AddRow(table, "IME OFF テキスト", _imeOffTextBox);

        group.Controls.Add(table);
        return group;
    }

    // 015-split-appearance-settings US1/US2: インジケーターとは別グループ・別値域（FR-001/002/003/004）。
    private GroupBox BuildBackgroundImageGroup()
    {
        var group = new GroupBox { Text = "背景画像", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(6) };

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddRow(table, "表示", _backgroundImageCheckBox);
        AddRow(table, "サイズ (32-512)", _backgroundImageSizeUpDown);
        AddRow(table, "不透明度 (0.10-1.00)", _backgroundImageOpacityUpDown);
        AddRow(table, "画像ファイル", CreateBackgroundImagePathRow());
        AddRow(table, "位置", CreateBackgroundImagePositionRow());

        group.Controls.Add(table);
        return group;
    }

    // 015-split-appearance-settings FR-013: どちらの表示対象にも属さない全体設定。
    private GroupBox BuildGeneralGroup()
    {
        var group = new GroupBox { Text = "全般", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(6) };

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddRow(table, "ログレベル", _logLevelComboBox);
        foreach (string level in new[] { "trace", "debug", "info", "warn", "error", "critical" })
        {
            _logLevelComboBox.Items.Add(level);
        }

        group.Controls.Add(table);
        return group;
    }

    private GroupBox BuildRulesGroup()
    {
        var group = new GroupBox { Text = "プロセス優先度ルール（最大 30 件）", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(6) };

        _rulesListView.Columns.Add("プロセス名", 140);
        _rulesListView.Columns.Add("優先度", 100);
        _rulesListView.Columns.Add("E-Core", 60);
        _rulesListView.Columns.Add("有効", 60);

        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Top,
        };
        content.Controls.Add(_rulesListView);
        content.Controls.Add(CreateFlow(_addRuleButton, _editRuleButton, _deleteRuleButton));

        var pollingRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
        pollingRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pollingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _pollingUpDown.Width = 100;
        AddRow(pollingRow, "ポーリング間隔（秒、1-1800）", _pollingUpDown);
        content.Controls.Add(pollingRow);

        content.Controls.Add(_adminStatusLabel);

        group.Controls.Add(content);
        return group;
    }

    private GroupBox BuildHotkeysGroup()
    {
        var group = new GroupBox { Text = "ホットキー（最大 256 件）", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(6) };

        _hotkeyListView.Columns.Add("キー", 130);
        _hotkeyListView.Columns.Add("動作", 200);
        _hotkeyListView.Columns.Add("コメント", 60);

        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Top,
        };
        content.Controls.Add(_hotkeyListView);
        content.Controls.Add(CreateFlow(_addHotkeyButton, _editHotkeyButton, _deleteHotkeyButton));

        group.Controls.Add(content);
        return group;
    }

    private Control BuildButtonRow()
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 0),
        };
        // RightToLeft のため先に追加した方が右端に来る。[適用][キャンセル][OK]（OK が既定操作で最も右）の並びにする。
        row.Controls.Add(_okButton);
        row.Controls.Add(_cancelButton);
        row.Controls.Add(_applyButton);
        return row;
    }

    /// <summary>ラベル＋コントロールの 1 行を末尾に追加する。</summary>
    private static void AddRow(TableLayoutPanel table, string labelText, Control control)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 3),
        };
        control.Margin = new Padding(3, 3, 3, 6);

        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static FlowLayoutPanel CreateFlow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Margin = new Padding(0),
        };
        foreach (Control c in controls)
        {
            c.Margin = new Padding(0, 3, 12, 3);
            flow.Controls.Add(c);
        }
        return flow;
    }

    // 016-custom-background-image: contracts/settings-ui-contract.md「レイアウト」節のとおり、
    // HotkeyEditForm.CreateExeRow() と同型（テキストボックス＋ボタン群）の 3 カラム版。
    private Control CreateBackgroundImagePathRow()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _backgroundImagePathTextBox.Dock = DockStyle.Fill;
        _backgroundImagePathTextBox.Margin = new Padding(0, 0, 6, 0);
        _backgroundImageBrowseButton.Margin = new Padding(0, 0, 6, 0);
        _backgroundImageClearButton.Margin = new Padding(0);
        panel.Controls.Add(_backgroundImagePathTextBox, 0, 0);
        panel.Controls.Add(_backgroundImageBrowseButton, 1, 0);
        panel.Controls.Add(_backgroundImageClearButton, 2, 0);
        return panel;
    }

    // 018-draggable-background-image: contracts/settings-ui-contract.md「レイアウト」節のとおり。
    private Control CreateBackgroundImagePositionRow()
    {
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0),
        };
        flow.Controls.Add(_backgroundImageResetPositionButton);
        flow.Controls.Add(_backgroundImagePositionHintLabel);
        return flow;
    }

    // ------------------------------------------------------------------
    // イベント配線
    // ------------------------------------------------------------------

    private void WireEvents()
    {
        _backgroundImageBrowseButton.Click += BackgroundImageBrowseButton_Click;
        _backgroundImageClearButton.Click += BackgroundImageClearButton_Click;
        _backgroundImageResetPositionButton.Click += BackgroundImageResetPositionButton_Click;

        _addRuleButton.Click += (_, _) => OnAddRule();
        _editRuleButton.Click += (_, _) => OnEditRule();
        _deleteRuleButton.Click += (_, _) => OnDeleteRule();
        _rulesListView.DoubleClick += (_, _) => OnEditRule();

        _addHotkeyButton.Click += (_, _) => OnAddHotkey();
        _editHotkeyButton.Click += (_, _) => OnEditHotkey();
        _deleteHotkeyButton.Click += (_, _) => OnDeleteHotkey();
        _hotkeyListView.DoubleClick += (_, _) => OnEditHotkey();

        // OK は既定 DialogResult を割り当て、Click 内で先に適用処理を実行してから自動的に閉じる
        // （移植元 onOk() = onApply() + ダイアログを閉じる、と同じ順序）。
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Click += (_, _) => OnApply();
        _cancelButton.DialogResult = DialogResult.Cancel;
        _applyButton.Click += (_, _) => OnApply();
    }

    // 016-custom-background-image: contracts/settings-ui-contract.md「参照ダイアログ」節のとおり、
    // HotkeyEditForm.BrowseButton_Click と同型だが PNG 単一フィルター（FR-002）。
    private void BackgroundImageBrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PNG ファイル (*.png)|*.png",
            CheckFileExists = true,
            CheckPathExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _backgroundImagePathTextBox.Text = dialog.FileName;
        }
    }

    // 016-custom-background-image: contracts/settings-ui-contract.md「クリア操作」節のとおり。
    private void BackgroundImageClearButton_Click(object? sender, EventArgs e)
    {
        _backgroundImagePathTextBox.Text = string.Empty;
    }

    // 018-draggable-background-image: contracts/settings-ui-contract.md「既定の位置に戻す（保留の扱い）」。
    // ボタンは保留フラグを立てるだけで、実際に Position を null にするのは適用時（ReadTo）。
    private void BackgroundImageResetPositionButton_Click(object? sender, EventArgs e)
    {
        _resetPositionPending = true;
        _backgroundImageResetPositionButton.Enabled = false;
    }

    // ------------------------------------------------------------------
    // 設定 ⇔ コントロール
    // ------------------------------------------------------------------

    /// <summary>設定値を各コントロールへ反映する。移植元 <c>SettingsDialog::loadFromSettings</c>。</summary>
    private void LoadFrom(AppSettings settings)
    {
        _visibleCheckBox.Checked = settings.MouseCursorIndicator.IsVisible;
        _sizeUpDown.Value = ClampToRange(settings.MouseCursorIndicator.Size, _sizeUpDown);
        _opacityUpDown.Value = ClampToRange(settings.MouseCursorIndicator.Opacity, _opacityUpDown);
        _offsetXUpDown.Value = ClampToRange(settings.MouseCursorIndicator.OffsetX, _offsetXUpDown);
        _offsetYUpDown.Value = ClampToRange(settings.MouseCursorIndicator.OffsetY, _offsetYUpDown);
        _imeOnTextBox.Text = settings.ImeOnText;
        _imeOffTextBox.Text = settings.ImeOffText;

        _backgroundImageCheckBox.Checked = settings.BackgroundImage.IsVisible;
        _backgroundImageSizeUpDown.Value = ClampToRange(settings.BackgroundImage.Size, _backgroundImageSizeUpDown);
        _backgroundImageOpacityUpDown.Value = ClampToRange(settings.BackgroundImage.Opacity, _backgroundImageOpacityUpDown);
        _backgroundImagePathTextBox.Text = settings.BackgroundImage.ImagePath;

        _logLevelComboBox.SelectedIndex = (int)settings.LogLevel;

        _pollingUpDown.Value = Math.Clamp(settings.PollingIntervalSeconds, (int)_pollingUpDown.Minimum, (int)_pollingUpDown.Maximum);

        _workingRules = [.. settings.ProcessPriorityRules];
        _workingHotkeys = [.. settings.HotkeySettings.Hotkeys];
        RefreshRuleListView();
        RefreshHotkeyListView();
        UpdateAdminStatusLabel();

        _resetPositionPending = false;
        _backgroundImageResetPositionButton.Enabled = true;
    }

    private static decimal ClampToRange(double value, NumericUpDown control) =>
        (decimal)Math.Clamp(value, (double)control.Minimum, (double)control.Maximum);

    /// <summary>
    /// コントロールの値を <paramref name="settings"/> へ書き戻す。移植元
    /// <c>SettingsDialog::readControlsToSettings</c>。<paramref name="settings"/> は呼び出し元が
    /// 保持している既存インスタンスをそのまま渡す前提で、この画面が扱わないフィールド
    /// （<c>PixelVerificationIntervalMs</c> 等）は一切変更しない（＝現在値を維持する）ことで、
    /// C++ 版の「<c>out = mgr_.settings()</c> をベースに UI 項目だけ上書きする」と同じ結果になる。
    /// </summary>
    private void ReadTo(AppSettings settings)
    {
        settings.MouseCursorIndicator.IsVisible = _visibleCheckBox.Checked;
        settings.MouseCursorIndicator.Size = (double)_sizeUpDown.Value;
        settings.MouseCursorIndicator.Opacity = (double)_opacityUpDown.Value;
        settings.MouseCursorIndicator.OffsetX = (double)_offsetXUpDown.Value;
        settings.MouseCursorIndicator.OffsetY = (double)_offsetYUpDown.Value;

        settings.BackgroundImage.IsVisible = _backgroundImageCheckBox.Checked;
        settings.BackgroundImage.Size = (double)_backgroundImageSizeUpDown.Value;
        settings.BackgroundImage.Opacity = (double)_backgroundImageOpacityUpDown.Value;
        settings.BackgroundImage.ImagePath = _backgroundImagePathTextBox.Text;
        if (_resetPositionPending)
        {
            settings.BackgroundImage.Position = null;
        }

        // 空欄は「変更なし」を意味する（移植元: !on.empty() のときのみ上書き）。
        string on = _imeOnTextBox.Text;
        string off = _imeOffTextBox.Text;
        if (!string.IsNullOrEmpty(on))
        {
            settings.ImeOnText = on;
        }
        if (!string.IsNullOrEmpty(off))
        {
            settings.ImeOffText = off;
        }

        if (_logLevelComboBox.SelectedIndex >= 0)
        {
            settings.LogLevel = (LogLevel)_logLevelComboBox.SelectedIndex;
        }

        settings.ProcessPriorityRules = _workingRules;
        settings.PollingIntervalSeconds = (int)_pollingUpDown.Value;

        settings.HotkeySettings.Hotkeys = _workingHotkeys;

        settings.Clamp();
    }

    // ------------------------------------------------------------------
    // OK / キャンセル / 適用
    // ------------------------------------------------------------------

    /// <summary>移植元 <c>SettingsDialog::onApply</c>。保存・各サービスへの反映・一覧再描画を行う。</summary>
    private void OnApply()
    {
        ReadTo(_settingsManager.Settings);
        _settingsManager.Save();

        _workingRules = [.. _settingsManager.Settings.ProcessPriorityRules];
        _workingHotkeys = [.. _settingsManager.Settings.HotkeySettings.Hotkeys];
        RefreshRuleListView();
        RefreshHotkeyListView();
        UpdateAdminStatusLabel();

        AppliedCallback?.Invoke(_settingsManager.Settings);

        _resetPositionPending = false;
        _backgroundImageResetPositionButton.Enabled = true;
    }

    // ------------------------------------------------------------------
    // プロセス優先度ルール一覧
    // ------------------------------------------------------------------

    private void RefreshRuleListView()
    {
        IReadOnlyList<bool> blocked = AccessibilityProbeCallback?.Invoke(_workingRules) ?? [];
        bool hasBlockedInfo = blocked.Count == _workingRules.Count;

        _rulesListView.BeginUpdate();
        _rulesListView.Items.Clear();
        for (int i = 0; i < _workingRules.Count; i++)
        {
            ProcessPriorityRule rule = _workingRules[i];
            var item = new ListViewItem(rule.ProcessName);
            item.SubItems.Add(rule.TargetPriority.ToString());
            item.SubItems.Add(rule.UseECoreOnly ? "○" : string.Empty);
            item.SubItems.Add(rule.IsEnabled ? "○" : string.Empty);
            if (hasBlockedInfo && blocked[i])
            {
                // 管理者権限不足で制御不能な行を薄ピンクで表示する（移植元 NM_CUSTOMDRAW と同色 FFE0E8）。
                item.BackColor = Color.FromArgb(255, 224, 232);
            }
            _rulesListView.Items.Add(item);
        }
        _rulesListView.EndUpdate();
    }

    private int SelectedRuleIndex() => _rulesListView.SelectedIndices.Count > 0 ? _rulesListView.SelectedIndices[0] : -1;

    private void SelectRuleRow(int index) => SelectListViewRow(_rulesListView, index);

    private void OnAddRule()
    {
        if (_workingRules.Count >= MaxRules)
        {
            MessageBox.Show(this, "プロセス優先度ルールは最大 30 件までです。",
                "IME Indicator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new RuleEditForm();
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _workingRules.Add(form.Result);
            RefreshRuleListView();
            SelectRuleRow(_workingRules.Count - 1);
        }
    }

    private void OnEditRule()
    {
        int idx = SelectedRuleIndex();
        if (idx < 0)
        {
            return;
        }

        using var form = new RuleEditForm(_workingRules[idx]);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _workingRules[idx] = form.Result;
            RefreshRuleListView();
            SelectRuleRow(idx);
        }
    }

    private void OnDeleteRule()
    {
        int idx = SelectedRuleIndex();
        if (idx < 0)
        {
            return;
        }

        _workingRules.RemoveAt(idx);
        RefreshRuleListView();
    }

    private void UpdateAdminStatusLabel()
    {
        int count = AccessDeniedCountCallback?.Invoke() ?? 0;
        _adminStatusLabel.Text = count > 0
            ? $"⚠ 一部プロセスに管理者権限が必要です（拒否回数: {count}）"
            : string.Empty;
    }

    // ------------------------------------------------------------------
    // ホットキー一覧
    // ------------------------------------------------------------------

    private void RefreshHotkeyListView()
    {
        _hotkeyListView.BeginUpdate();
        _hotkeyListView.Items.Clear();
        foreach (HotKeyEntry entry in _workingHotkeys)
        {
            string shortcut = DescribeHotkey(entry);
            var item = new ListViewItem(shortcut.Length == 0 ? "-" : shortcut);
            item.SubItems.Add(DescribeAction(entry));
            item.SubItems.Add(entry.Note);
            _hotkeyListView.Items.Add(item);
        }
        _hotkeyListView.EndUpdate();
    }

    private int SelectedHotkeyIndex() => _hotkeyListView.SelectedIndices.Count > 0 ? _hotkeyListView.SelectedIndices[0] : -1;

    private void SelectHotkeyRow(int index) => SelectListViewRow(_hotkeyListView, index);

    private void OnAddHotkey()
    {
        if (_workingHotkeys.Count >= MaxHotkeys)
        {
            MessageBox.Show(this, "ホットキーは最大 256 件までです。",
                "IME Indicator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new HotkeyEditForm();
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _workingHotkeys.Add(form.Result);
            RefreshHotkeyListView();
            SelectHotkeyRow(_workingHotkeys.Count - 1);
        }
    }

    private void OnEditHotkey()
    {
        int idx = SelectedHotkeyIndex();
        if (idx < 0)
        {
            return;
        }

        using var form = new HotkeyEditForm(_workingHotkeys[idx]);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _workingHotkeys[idx] = form.Result;
            RefreshHotkeyListView();
            SelectHotkeyRow(idx);
        }
    }

    private void OnDeleteHotkey()
    {
        int idx = SelectedHotkeyIndex();
        if (idx < 0)
        {
            return;
        }

        _workingHotkeys.RemoveAt(idx);
        RefreshHotkeyListView();
    }

    private static void SelectListViewRow(ListView listView, int index)
    {
        if (index < 0 || index >= listView.Items.Count)
        {
            return;
        }

        listView.Items[index].Selected = true;
        listView.Items[index].Focused = true;
        listView.EnsureVisible(index);
    }

    // 移植元 SettingsDialog.cpp 無名 namespace の modifiersToString / vkeyToString / describeHotkey /
    // describeAction。CommandCatalog（108 種の実行経路を持つコマンドのみを正とする、014 移植で新設した
    // カタログ）を使う点が、C++ 版がこのファイル内に別途持つ独自リスト kHotkeyCommandList /
    // lookupCommandLabel と異なる（ui-parity-contract.md §6 の「108 種のみ」「(未実装: ID)」表示は
    // CommandCatalog を正として定義されており、HotkeyEditForm も同じカタログを使っているため、
    // 一覧表示側もこれに揃える）。

    private static string DescribeHotkey(HotKeyEntry entry)
    {
        string mods = ModifiersToString(entry.Modifiers);
        string key = VkeyToDisplayString(entry.Vkey);
        return mods + key;
    }

    private static string ModifiersToString(int modifiers)
    {
        string s = string.Empty;
        if ((modifiers & (int)NativeConstants.MOD_CONTROL) != 0) s += "Ctrl+";
        if ((modifiers & (int)NativeConstants.MOD_ALT) != 0) s += "Alt+";
        if ((modifiers & (int)NativeConstants.MOD_SHIFT) != 0) s += "Shift+";
        if ((modifiers & (int)NativeConstants.MOD_WIN) != 0) s += "Win+";
        return s;
    }

    private static string DescribeAction(HotKeyEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Exe))
        {
            int p = entry.Exe.LastIndexOfAny(['\\', '/']);
            return p < 0 ? entry.Exe : entry.Exe[(p + 1)..];
        }
        if (entry.Cmd >= 0)
        {
            return CommandCatalog.LabelFor(entry.Cmd);
        }
        return "(未設定)";
    }

    private static string VkeyToDisplayString(int vkey)
    {
        if (vkey == 0) return "(未設定)";
        if (vkey is >= 'A' and <= 'Z') return ((char)vkey).ToString();
        if (vkey is >= '0' and <= '9') return ((char)vkey).ToString();
        if (vkey is >= (int)Keys.F1 and <= (int)Keys.F24) return $"F{vkey - (int)Keys.F1 + 1}";

        return vkey switch
        {
            (int)Keys.Return => "Enter",
            (int)Keys.Escape => "Esc",
            (int)Keys.Tab => "Tab",
            (int)Keys.Back => "Backspace",
            (int)Keys.Delete => "Delete",
            (int)Keys.Insert => "Insert",
            (int)Keys.Home => "Home",
            (int)Keys.End => "End",
            (int)Keys.Prior => "PgUp",
            (int)Keys.Next => "PgDn",
            (int)Keys.Up => "↑",
            (int)Keys.Down => "↓",
            (int)Keys.Left => "←",
            (int)Keys.Right => "→",
            (int)Keys.Space => "Space",
            (int)Keys.PrintScreen => "PrintScreen",
            (int)Keys.Pause => "Pause",
            (int)Keys.CapsLock => "CapsLock",
            (int)Keys.NumLock => "NumLock",
            (int)Keys.Scroll => "ScrollLock",
            512 => "(マウス)", // HotKeyEntry.Vkey コメント: 512=vkMouse
            _ => $"VK_0x{vkey:X}",
        };
    }
}
