// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;
using IMEIndicator.Services;

namespace IMEIndicator.Views;

/// <summary>
/// プロセス優先度ルールの追加・編集ダイアログ（コード配置、デザイナー不使用）。
/// 移植元: <c>src/cpp/views/SettingsDialog.cpp</c> の <c>showRuleEditDialog()</c>
/// （ファイル内 static 関数 <c>RuleEditContext</c> / <c>ruleEditWndProc</c>）。
/// 項目・順序は specs/014-port-to-csharp/contracts/ui-parity-contract.md §5 を正とする
/// （プロセス名 / 優先度 / 有効 / E-Core 固定 / バックオフ上限 / OK / キャンセル）。
/// </summary>
/// <remarks>
/// <para>
/// 移植時の意図的な差異（T048 指示による仕様）: C++ 版はダイアログの OK 押下時点では検証を行わず、
/// 呼び出し元（<c>SettingsDialog::onAddRule</c> / <c>onEditRule</c>）が <c>rule.isValid()</c> を見て
/// 無効なら黙って破棄する。本実装は OK ボタン押下時にダイアログ内で <see cref="ProcessPriorityRule.IsValid"/>
/// を検証し、空白のみのプロセス名なら <see cref="MessageBox"/> で警告して確定させない（サイレント破棄より
/// 利用者にわかりやすいため）。
/// </para>
/// <para>
/// WinForms 特有の注意点（C++ 版の Win32 直叩きと異なる箇所）:
/// <list type="bullet">
/// <item>C++ 版は初期値設定を <c>SetWindowTextW</c> で行っており、これは <c>CBN_EDITCHANGE</c> を
/// 発火しないため、編集モードでダイアログを開いた瞬間に候補ドロップダウンが開くことはない。
/// 一方 WinForms の <see cref="ComboBox.Text"/> はプログラムからの代入でも
/// <see cref="ComboBox.TextChanged"/> を発火するため、本実装では <see cref="LoadFrom"/> で初期値を
/// 設定した後に <see cref="ComboBox.TextChanged"/> を購読する順序にして、同じ「初期表示時は候補を
/// 開かない」挙動を再現している。</item>
/// <item>C++ 版は候補再構築の際に <c>CB_RESETCONTENT</c>（全消去）ではなく <c>CB_DELETESTRING</c>
/// を 1 件ずつ使う。<c>CB_RESETCONTENT</c> は <c>CBS_DROPDOWN</c> コンボの編集テキストまで
/// 消してしまうためで、これはネイティブ Win32 ComboBox の挙動としてそのまま WinForms の
/// <see cref="ComboBox"/>（同じネイティブコントロールの薄いラッパー）にも当てはまりうる。
/// 本実装でも <see cref="ComboBox.ObjectCollection.Clear"/>（<c>CB_RESETCONTENT</c> 相当）は使わず、
/// <see cref="ComboBox.ObjectCollection.RemoveAt"/> を 1 件ずつ呼ぶことで、入力中のテキストを
/// 消してしまうリスクを避けている。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class RuleEditForm : Form
{
    /// <summary>プロセス名候補の最大表示件数（ui-parity-contract.md §5）。</summary>
    private const int MaxProcessNameSuggestions = 20;

    /// <summary>
    /// 実行中プロセス名のキャッシュ。フォーム構築時に 1 回だけ取得し、以降は
    /// <see cref="OnNameTextChanged"/> で前方一致フィルタのみを行う（パフォーマンス優先。
    /// C++ 版は <c>CBN_EDITCHANGE</c> の都度 <c>enumerateDistinctProcessNames()</c> を呼ぶが、
    /// C# 版ではキャッシュ方式に変更している）。
    /// </summary>
    private readonly IReadOnlyList<string> _processNameCache;

    private readonly ComboBox _nameComboBox;
    private readonly ComboBox _priorityComboBox;
    private readonly CheckBox _enabledCheckBox;
    private readonly CheckBox _eCoreCheckBox;
    private readonly NumericUpDown _backoffNumericUpDown;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    /// <summary>
    /// OK で確定したルール。<see cref="Form.DialogResult"/> が <see cref="DialogResult.OK"/> の
    /// ときのみ有効な値を持つ。
    /// </summary>
    public ProcessPriorityRule Result { get; private set; } = new();

    /// <summary>
    /// ダイアログを構築する。
    /// </summary>
    /// <param name="existing">
    /// 編集対象の既存ルール。<see langword="null"/> の場合は新規追加モードとして
    /// <see cref="ProcessPriorityRule"/> の既定値（Normal / バックオフ 6 / 有効）で初期化する
    /// （移植元 <c>SettingsDialog::onAddRule</c> の初期値と同じ）。
    /// </param>
    public RuleEditForm(ProcessPriorityRule? existing = null)
    {
        _processNameCache = ProcessPriorityService.EnumerateDistinctProcessNames();

        Text = "プロセス優先度ルール";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 260);
        Padding = new Padding(12);

        // ----- プロセス名 / 優先度 / 有効 / E-Core 固定 / バックオフ上限 -----
        var fieldsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
        };
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
        fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < fieldsLayout.RowCount; i++)
        {
            fieldsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        _nameComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 3, 3, 10),
        };
        AddFieldRow(fieldsLayout, 0, "プロセス名", _nameComboBox);

        _priorityComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 3, 3, 10),
        };
        foreach (PriorityLevel level in Enum.GetValues<PriorityLevel>())
        {
            // PriorityLevel の列挙名がそのまま表示名になる（移植元 priorityLabel() と同じ文字列）。
            _priorityComboBox.Items.Add(level);
        }
        AddFieldRow(fieldsLayout, 1, "優先度", _priorityComboBox);

        _enabledCheckBox = new CheckBox
        {
            Text = "有効",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 6),
        };
        fieldsLayout.Controls.Add(_enabledCheckBox, 0, 2);
        fieldsLayout.SetColumnSpan(_enabledCheckBox, 2);

        _eCoreCheckBox = new CheckBox
        {
            Text = "E-Core 限定（ハイブリッド CPU のみ）",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 10),
        };
        fieldsLayout.Controls.Add(_eCoreCheckBox, 0, 3);
        fieldsLayout.SetColumnSpan(_eCoreCheckBox, 2);

        _backoffNumericUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 10,
            Width = 80,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 3),
        };
        AddFieldRow(fieldsLayout, 4, "最大バックオフ指数", _backoffNumericUpDown);

        // ----- OK / キャンセル -----
        // OK は検証を挟んでから閉じる必要があるため、Button.DialogResult には割り当てず
        // Click ハンドラで明示的に Form.DialogResult を設定して Close() する。
        _okButton = new Button
        {
            Text = "OK",
            AutoSize = true,
            Margin = new Padding(4, 4, 0, 0),
        };
        _cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = new Padding(4, 4, 0, 0),
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        // RightToLeft のため、先に追加した方が右端に来る。「OK キャンセル」の並び（移植元と同じ）
        // にするには、キャンセルを先に追加して OK を後から追加する。
        buttonsPanel.Controls.Add(_cancelButton);
        buttonsPanel.Controls.Add(_okButton);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.Controls.Add(fieldsLayout, 0, 0);
        rootLayout.Controls.Add(buttonsPanel, 0, 1);

        Controls.Add(rootLayout);

        LoadFrom(existing ?? new ProcessPriorityRule());

        // 初期値設定（LoadFrom）の完了後にイベントを購読する。理由はクラス doc コメント参照。
        _nameComboBox.TextChanged += OnNameTextChanged;
        _okButton.Click += OnOkButtonClick;

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    /// <summary>ラベル＋入力コントロールの 1 行を <paramref name="layout"/> に追加する。</summary>
    private static void AddFieldRow(TableLayoutPanel layout, int row, string labelText, Control control)
    {
        layout.Controls.Add(
            new Label
            {
                Text = labelText,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 9, 3, 3),
            },
            0, row);
        layout.Controls.Add(control, 1, row);
    }

    /// <summary>
    /// 各コントロールへルールの値を反映する。移植元 <c>showRuleEditDialog</c> の初期値設定部分
    /// （<c>ctx.hName</c> 〜 <c>ctx.hBackoff</c> への <c>SetWindowTextW</c> / <c>CB_SETCURSEL</c> /
    /// <c>BM_SETCHECK</c>）に対応する。
    /// </summary>
    private void LoadFrom(ProcessPriorityRule source)
    {
        _nameComboBox.Text = source.ProcessName;
        _priorityComboBox.SelectedItem = source.TargetPriority;
        _enabledCheckBox.Checked = source.IsEnabled;
        _eCoreCheckBox.Checked = source.UseECoreOnly;
        _backoffNumericUpDown.Value = source.ValidatedMaxBackoffExponent();
    }

    /// <summary>
    /// プロセス名入力時に前方一致候補を再構築する（大小無視、最大 <see cref="MaxProcessNameSuggestions"/>
    /// 件）。移植元 <c>ruleEditWndProc</c> の <c>CBN_EDITCHANGE</c> ハンドラに対応する。
    /// </summary>
    private void OnNameTextChanged(object? sender, EventArgs e)
    {
        // Items.Clear()（ネイティブ CB_RESETCONTENT 相当）は編集中テキストまで消しうるため使わず、
        // RemoveAt（CB_DELETESTRING 相当）で 1 件ずつ削除する。クラス doc コメント参照。
        while (_nameComboBox.Items.Count > 0)
        {
            _nameComboBox.Items.RemoveAt(_nameComboBox.Items.Count - 1);
        }

        string prefix = _nameComboBox.Text;
        if (prefix.Length == 0)
        {
            return;
        }

        int count = 0;
        foreach (string name in _processNameCache)
        {
            if (count >= MaxProcessNameSuggestions)
            {
                break;
            }
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                _nameComboBox.Items.Add(name);
                count++;
            }
        }

        if (count > 0)
        {
            _nameComboBox.DroppedDown = true;
        }
    }

    /// <summary>
    /// OK 押下時。プロセス名が空白のみなら警告して確定させない。有効なら <see cref="Result"/> を
    /// 構築して <see cref="DialogResult.OK"/> で閉じる。
    /// </summary>
    private void OnOkButtonClick(object? sender, EventArgs e)
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = _nameComboBox.Text,
            TargetPriority = _priorityComboBox.SelectedItem is PriorityLevel level ? level : PriorityLevel.Normal,
            IsEnabled = _enabledCheckBox.Checked,
            UseECoreOnly = _eCoreCheckBox.Checked,
            MaxBackoffExponent = (int)_backoffNumericUpDown.Value,
        };

        if (!rule.IsValid())
        {
            MessageBox.Show(
                this,
                "プロセス名を入力してください。",
                "IME Indicator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _nameComboBox.Focus();
            return;
        }

        Result = rule;
        DialogResult = DialogResult.OK;
        Close();
    }
}
