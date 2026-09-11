// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;
using IMEIndicator.Models.Hotkey;
using IMEIndicator.Services.Hotkey;

namespace IMEIndicator.Views;

/// <summary>
/// ホットキー編集ダイアログ（追加・編集共用のモーダルダイアログ）。
/// 移植元: <c>src/cpp/views/SettingsDialog.cpp</c> の <c>showHotkeyEditDialog</c>
/// （および同ファイル内の <c>vkeyToString</c> / <c>modifiersToString</c> / <c>describeHotkey</c> /
/// <c>describeAction</c> / <c>lookupCommandLabel</c> / <c>shortcutKeyEditProc</c>）。
/// 項目・順序は specs/014-port-to-csharp/contracts/ui-parity-contract.md §6 を正とする。
/// </summary>
/// <remarks>
/// <para>
/// C++ 版は EDIT コントロールを SUBCLASS して WM_KEYDOWN を直接捕捉し、押下したキーを vkey として
/// 記録すると同時に、その瞬間の Ctrl/Alt/Shift/Win 押下状態を修飾キーチェックボックスへ「補助的に」
/// 反映する（最終確定は常にチェックボックスの状態）。C# 版では <see cref="ShortcutTextBox"/>
/// （<see cref="Control.IsInputKey"/> を上書きした TextBox 派生）の <see cref="Control.KeyDown"/>
/// で同じ役割を果たす。Win キーは <see cref="KeyEventArgs"/> に含まれないため、
/// <c>Services/KeyboardHook.cs</c> の <c>IsWinKeyDown()</c> と同じ
/// <see cref="NativeMethods.GetAsyncKeyState"/> による検出をここでも行う。
/// </para>
/// <para>
/// C++ 版にはアクション種別（exe / 内部コマンド）ラジオボタンによるコントロール Enabled 切替が
/// 存在しない（常に両方編集可能で、OK 時にラジオの選択のみで exe/cmd を振り分ける）が、
/// C# 版では誤入力防止のため Enabled 切替を追加する（仕様上の操作手順を変えるものではない）。
/// </para>
/// </remarks>
public sealed class HotkeyEditForm : Form
{
    // HotKeyEntry.Vkey のコメント: 512=vkMouse（マウスボタンをトリガーにしたホットキー）。
    private const int VkMouse = 512;

    private readonly TextBox _noteTextBox = new();
    private readonly ShortcutTextBox _shortcutTextBox = new() { ReadOnly = true };
    private readonly CheckBox _ctrlCheckBox = new() { Text = "Ctrl", AutoSize = true };
    private readonly CheckBox _altCheckBox = new() { Text = "Alt", AutoSize = true };
    private readonly CheckBox _shiftCheckBox = new() { Text = "Shift", AutoSize = true };
    private readonly CheckBox _winCheckBox = new() { Text = "Win", AutoSize = true };
    private readonly RadioButton _useExeRadio = new() { Text = "アプリ・URL・フォルダ起動", AutoSize = true };
    private readonly RadioButton _useCmdRadio = new() { Text = "内部コマンド", AutoSize = true };
    private readonly TextBox _exeTextBox = new();
    private readonly Button _browseButton = new() { Text = "参照...", AutoSize = true };
    private readonly TextBox _argsTextBox = new();
    private readonly TextBox _dirTextBox = new();
    private readonly ComboBox _cmdComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _disableCheckBox = new() { Text = "無効化", AutoSize = true };
    private readonly CheckBox _multInstCheckBox = new() { Text = "複数起動許可", AutoSize = true };
    private readonly CheckBox _trayMenuCheckBox = new() { Text = "トレイメニュー表示", AutoSize = true };
    private readonly CheckBox _autoStartCheckBox = new() { Text = "起動時に自動実行", AutoSize = true };
    private readonly CheckBox _adminCheckBox = new() { Text = "管理者として実行", AutoSize = true };
    private readonly Button _okButton = new() { Text = "OK", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "キャンセル", AutoSize = true };

    // ショートカット欄で最後に記録した vkey。テキストボックスの表示文字列からは逆変換しない
    // （"VK_0xNN" 等の表示専用フォーマットを往復させないため、値そのものを別途保持する）。
    private int _shortcutVkey;

    /// <summary>OK 確定後のホットキーエントリ。呼び出し側は <c>DialogResult == OK</c> のときのみ使う。</summary>
    public HotKeyEntry Result { get; private set; }

    /// <param name="existing">編集対象の既存エントリ。<see langword="null"/> なら新規追加として空のエントリから始める。
    /// 渡されたインスタンス自体は変更しない（キャンセル時に呼び出し側の値を汚さないよう複製して扱う）。</param>
    public HotkeyEditForm(HotKeyEntry? existing = null)
    {
        Result = existing is null ? new HotKeyEntry() : CloneEntry(existing);
        _shortcutVkey = Result.Vkey;

        Text = "ホットキー編集";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(480, 520);

        BuildLayout();
        LoadFromEntry();
        WireEvents();

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    // ------------------------------------------------------------------
    // レイアウト構築
    // ------------------------------------------------------------------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddRow(root, "メモ", _noteTextBox);
        AddRow(root, "ショートカット", _shortcutTextBox);
        AddRow(root, "修飾キー", CreateFlow(_ctrlCheckBox, _altCheckBox, _shiftCheckBox, _winCheckBox));
        AddRow(root, "アクション種別", CreateFlow(_useExeRadio, _useCmdRadio));
        AddRow(root, "exe・URL・パス", CreateExeRow());
        AddRow(root, "引数", _argsTextBox);
        AddRow(root, "作業ディレクトリ", _dirTextBox);
        AddRow(root, "内部コマンド", _cmdComboBox);
        AddRow(root, "フラグ", CreateFlow(
            _disableCheckBox, _multInstCheckBox, _trayMenuCheckBox, _autoStartCheckBox, _adminCheckBox));

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(12),
        };
        buttonRow.Controls.Add(_okButton);
        buttonRow.Controls.Add(_cancelButton);

        Controls.Add(root);
        Controls.Add(buttonRow);
    }

    /// <summary>ラベル + コントロールの 1 行を末尾に追加する。</summary>
    private static void AddRow(TableLayoutPanel table, string labelText, Control control)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 3),
        };
        control.Dock = DockStyle.Fill;
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
            c.Margin = new Padding(0, 3, 16, 3);
            flow.Controls.Add(c);
        }
        return flow;
    }

    private Control CreateExeRow()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _exeTextBox.Dock = DockStyle.Fill;
        _exeTextBox.Margin = new Padding(0, 0, 6, 0);
        _browseButton.Margin = new Padding(0);
        panel.Controls.Add(_exeTextBox, 0, 0);
        panel.Controls.Add(_browseButton, 1, 0);
        return panel;
    }

    // ------------------------------------------------------------------
    // 初期値反映
    // ------------------------------------------------------------------

    private void LoadFromEntry()
    {
        _noteTextBox.Text = Result.Note;
        _shortcutTextBox.Text = VkeyToDisplayString(_shortcutVkey);

        _ctrlCheckBox.Checked = (Result.Modifiers & (int)NativeConstants.MOD_CONTROL) != 0;
        _altCheckBox.Checked = (Result.Modifiers & (int)NativeConstants.MOD_ALT) != 0;
        _shiftCheckBox.Checked = (Result.Modifiers & (int)NativeConstants.MOD_SHIFT) != 0;
        _winCheckBox.Checked = (Result.Modifiers & (int)NativeConstants.MOD_WIN) != 0;

        bool useCmd = Result.Cmd >= 0;
        _useCmdRadio.Checked = useCmd;
        _useExeRadio.Checked = !useCmd;

        _exeTextBox.Text = Result.Exe;
        _argsTextBox.Text = Result.Args;
        _dirTextBox.Text = Result.Dir;

        PopulateCommandComboBox();

        _disableCheckBox.Checked = Result.Disable;
        _multInstCheckBox.Checked = Result.MultInst;
        _trayMenuCheckBox.Checked = Result.TrayMenu;
        _autoStartCheckBox.Checked = Result.AutoStart;
        _adminCheckBox.Checked = Result.Admin;

        UpdateActionModeEnabled();
    }

    /// <summary>
    /// 内部コマンド ComboBox を <see cref="CommandCatalog.All"/>（108 件、表示は
    /// "分類: 表示名"）で埋める。既存エントリが実行経路の無い ID を保持している場合は
    /// ui-parity-contract.md §6 のとおり「(未実装: ID)」項目を追加してその値を保持したまま選択する。
    /// </summary>
    private void PopulateCommandComboBox()
    {
        _cmdComboBox.Items.Clear();
        foreach (CommandCatalogEntry entry in CommandCatalog.All)
        {
            _cmdComboBox.Items.Add(new CommandComboItem(entry.Id, $"{entry.Category}: {entry.Label}"));
        }

        int cmd = Result.Cmd;
        if (cmd >= 0 && !CommandCatalog.IsImplemented(cmd))
        {
            _cmdComboBox.Items.Add(new CommandComboItem(cmd, CommandCatalog.LabelFor(cmd)));
        }

        int selectId = cmd >= 0 ? cmd : (CommandCatalog.All.Count > 0 ? CommandCatalog.All[0].Id : -1);
        SelectCommandComboBoxItem(selectId);
    }

    private void SelectCommandComboBoxItem(int id)
    {
        foreach (object item in _cmdComboBox.Items)
        {
            if (item is CommandComboItem candidate && candidate.Id == id)
            {
                _cmdComboBox.SelectedItem = item;
                return;
            }
        }
        if (_cmdComboBox.Items.Count > 0)
        {
            _cmdComboBox.SelectedIndex = 0;
        }
    }

    // ------------------------------------------------------------------
    // イベント配線
    // ------------------------------------------------------------------

    private void WireEvents()
    {
        _shortcutTextBox.KeyDown += ShortcutTextBox_KeyDown;
        _useExeRadio.CheckedChanged += (_, _) => UpdateActionModeEnabled();
        _browseButton.Click += BrowseButton_Click;
        _okButton.Click += OkButton_Click;
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
    }

    /// <summary>アクション種別ラジオの選択に応じて exe 系 / 内部コマンド系コントロールの編集可否を切り替える。</summary>
    private void UpdateActionModeEnabled()
    {
        bool useExe = _useExeRadio.Checked;
        _exeTextBox.Enabled = useExe;
        _browseButton.Enabled = useExe;
        _argsTextBox.Enabled = useExe;
        _dirTextBox.Enabled = useExe;
        _cmdComboBox.Enabled = !useExe;
    }

    private void ShortcutTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true; // システム（他アプリ・IME 等）へのキー伝播を防ぐ。

        Keys keyCode = e.KeyCode;
        if (IsModifierKey(keyCode))
        {
            // 修飾キー単独の押下は無視する（確定は Ctrl/Alt/Shift/Win チェックボックスで行う）。
            return;
        }

        _shortcutVkey = (int)keyCode;
        _shortcutTextBox.Text = VkeyToDisplayString(_shortcutVkey);

        // 押下時点の修飾キー状態をチェックボックスへ自動反映する補助機能
        // （移植元: SettingsDialog.cpp shortcutKeyEditProc の WM_KEYDOWN 処理と同趣旨）。
        _ctrlCheckBox.Checked = e.Control;
        _altCheckBox.Checked = e.Alt;
        _shiftCheckBox.Checked = e.Shift;
        _winCheckBox.Checked = IsWinKeyDown();
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "実行ファイル (*.exe;*.com;*.bat)|*.exe;*.com;*.bat|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _exeTextBox.Text = dialog.FileName;
        }
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        Result.Note = _noteTextBox.Text;
        Result.Exe = _exeTextBox.Text;
        Result.Args = _argsTextBox.Text;
        Result.Dir = _dirTextBox.Text;
        Result.Vkey = _shortcutVkey;

        uint mods = 0;
        if (_ctrlCheckBox.Checked) mods |= NativeConstants.MOD_CONTROL;
        if (_altCheckBox.Checked) mods |= NativeConstants.MOD_ALT;
        if (_shiftCheckBox.Checked) mods |= NativeConstants.MOD_SHIFT;
        if (_winCheckBox.Checked) mods |= NativeConstants.MOD_WIN;
        Result.Modifiers = (int)mods;

        bool useCmd = _useCmdRadio.Checked;
        if (useCmd)
        {
            Result.Cmd = _cmdComboBox.SelectedItem is CommandComboItem selected ? selected.Id : -1;
            Result.Exe = string.Empty;
        }
        else
        {
            Result.Cmd = -1;
        }

        // 最低限の防御的チェック（C# 版に HotKeyEntry.Validate() 相当は存在しないため、
        // ここでのみ「アクション種別の選択」と「実データ」の不整合を弾く）。
        if (!useCmd && string.IsNullOrWhiteSpace(Result.Exe))
        {
            MessageBox.Show(this, "「exe・URL・パス」を指定してください。",
                "IME Indicator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (useCmd && Result.Cmd < 0)
        {
            MessageBox.Show(this, "内部コマンドを選択してください。",
                "IME Indicator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result.Disable = _disableCheckBox.Checked;
        Result.MultInst = _multInstCheckBox.Checked;
        Result.TrayMenu = _trayMenuCheckBox.Checked;
        Result.AutoStart = _autoStartCheckBox.Checked;
        Result.Admin = _adminCheckBox.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }

    // ------------------------------------------------------------------
    // ヘルパー
    // ------------------------------------------------------------------

    private static HotKeyEntry CloneEntry(HotKeyEntry source) => new()
    {
        Note = source.Note,
        Icon = source.Icon,
        Category = source.Category,
        Exe = source.Exe,
        Args = source.Args,
        Dir = source.Dir,
        Sound = source.Sound,
        Modifiers = source.Modifiers,
        Vkey = source.Vkey,
        ScanCode = source.ScanCode,
        Cmd = source.Cmd,
        CmdShow = source.CmdShow,
        Opacity = source.Opacity,
        Priority = source.Priority,
        Disable = source.Disable,
        MultInst = source.MultInst,
        TrayMenu = source.TrayMenu,
        AutoStart = source.AutoStart,
        Ask = source.Ask,
        Delay = source.Delay,
        Admin = source.Admin,
    };

    private static bool IsModifierKey(Keys keyCode) => keyCode is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;

    /// <summary>
    /// Win キーの押下状態を検出する。<see cref="KeyEventArgs"/> の Modifiers には含まれないため
    /// （移植元コメント参照）、<see cref="NativeMethods.GetAsyncKeyState"/> を直接呼ぶ。
    /// 同じ判定ロジックが <c>Services/KeyboardHook.cs</c> の private <c>IsWinKeyDown()</c> にも
    /// 存在するが、可視性の都合上ここでは同一ロジックを再実装している。
    /// </summary>
    private static bool IsWinKeyDown() =>
        (NativeMethods.GetAsyncKeyState(NativeConstants.VK_LWIN) & 0x8000) != 0 ||
        (NativeMethods.GetAsyncKeyState(NativeConstants.VK_RWIN) & 0x8000) != 0;

    /// <summary>
    /// vkey を人間に読みやすい表示へ変換する。
    /// 移植元: src/cpp/views/SettingsDialog.cpp 内 <c>vkeyToString</c>（無名 namespace）。
    /// </summary>
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
            VkMouse => "(マウス)",
            _ => $"VK_0x{vkey:X}",
        };
    }

    /// <summary>内部コマンド ComboBox の 1 項目。表示文字列と Id を対にして保持する。</summary>
    private sealed record CommandComboItem(int Id, string Display)
    {
        public override string ToString() => Display;
    }

    /// <summary>
    /// ショートカット記録用の TextBox。標準の TextBox は矢印キー・Tab 等を内部で消費し
    /// <see cref="Control.KeyDown"/> が発火しないため、<see cref="IsInputKey"/> を上書きして
    /// 幅広いキーを KeyDown として拾えるようにする
    /// （移植元 C++ 版は EDIT を SUBCLASS して WM_KEYDOWN を直接捕捉している）。
    /// ただし Tab と Escape はダイアログ内のフォーカス移動・キャンセル操作を優先し、
    /// ショートカットの記録対象からは除外する。
    /// </summary>
    private sealed class ShortcutTextBox : TextBox
    {
        protected override bool IsInputKey(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            return keyCode != Keys.Tab && keyCode != Keys.Escape;
        }
    }
}
