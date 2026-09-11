// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;

namespace IMEIndicator.Models.Hotkey;

/// <summary>
/// ホットキーの固定カテゴリ（インデックス 0〜11）+ ユーザー定義の起点（12）。
/// 移植元: src/cpp/models/hotkey/HotKeyEntry.h の <c>enum class Category</c>。
/// <see cref="HotKeyEntry.AutoCategory"/> の戻り値として使う自動判定用の分類であり、
/// ユーザーが名前・表示順・色を設定できる永続化カテゴリ（<see cref="HotkeyCategory"/>、
/// id 12〜39）とは別の概念（<see cref="UserDefined"/>（12）はユーザー定義カテゴリの
/// 開始位置を表す番兵値であり、<see cref="HotKeyEntry.AutoCategory"/> 自身がこの値を
/// 返すことはない）。
/// </summary>
/// <remarks>
/// 型名は C++ 版そのままの "Category" ではなく "HotkeyCategoryKind" とした。
/// 同名の <see cref="HotKeyEntry.Category"/> プロパティ（int、永続化用）が
/// <see cref="HotKeyEntry"/> に既に存在するため、もし型名も "Category" にすると、
/// <see cref="HotKeyEntry"/> 内で "Category.Autorun" のような式を書いたときに
/// C# の名前解決規則（同一クラス内では単純名はまずインスタンスメンバーとして解決される）
/// により "Category" がプロパティ（int）側に解決されてしまい、
/// ".Autorun" の参照でコンパイルエラーになる。この衝突を避けるため型名を変えている。
/// 値・判定順序は C++ 版と同一。
/// </remarks>
public enum HotkeyCategoryKind
{
    /// <summary>既定（未使用カテゴリ）。</summary>
    Default = 0,

    /// <summary>すべて（フィルタ用の特殊値）。</summary>
    All = 1,

    /// <summary>キーボード。</summary>
    Keyboard = 2,

    /// <summary>マウス。</summary>
    Mouse = 3,

    /// <summary>ジョイスティック（未使用）。</summary>
    Joystick = 4,

    /// <summary>WinLIRC リモコン（未使用）。</summary>
    Remote = 5,

    /// <summary>内部コマンド。</summary>
    Commands = 6,

    /// <summary>実行ファイル（.exe / .com / .bat）。</summary>
    Programs = 7,

    /// <summary>ドキュメント・フォルダ。</summary>
    Documents = 8,

    /// <summary>URL / シェルリンク。</summary>
    WebLinks = 9,

    /// <summary>IMEIndicator 起動時に自動実行。</summary>
    Autorun = 10,

    /// <summary>システムトレイメニューに表示。</summary>
    TrayMenu = 11,

    /// <summary>ユーザー定義カテゴリの開始位置（番兵値。12 以降が実際の ID）。</summary>
    UserDefined = 12,
}

/// <summary>
/// ホットキーエントリ 1 件分の永続化モデル。
/// フィールド名・型・既定値・JSON キー順は
/// specs/010-hotkeyp-merge/contracts/hotkey-entry-schema.md を正とする。
/// 移植元: src/cpp/models/hotkey/HotKeyEntry.h の struct HotKeyEntry
/// （実行時専用メンバー item / lirc / isDown / lock / processId / process はスキーマ
/// 文書に存在せず永続化対象外のため、ここには含めない）。
/// </summary>
/// <remarks>
/// DistinguishLR について: 現行 C++ 版の to_json（HotKeyEntry.cpp）は常に <c>false</c> を書き出し、
/// from_json 側はこのキーを一切読まない、純粋な互換用プレースホルダである
/// （struct HotKeyEntry 自体に対応するメンバーがなく、左右修飾キー区別の実体は
/// <see cref="HotkeyGlobalOptions.DistinguishLeftRightModifiers"/> が担っている）。
/// バイト互換（settings-compat-contract.md）を保つため、<see cref="DistinguishLR"/> は
/// 下記のとおり get が常に false・set が no-op の擬似プロパティとした
/// （System.Text.Json のソース生成は実際のプロパティの get/set を介してシリアライズするため、
/// 別途コンバータを書かずにこの挙動を実現できる）。
/// </remarks>
public sealed class HotKeyEntry
{
    /// <summary>表示コメント。空の場合は Exe のファイル名部分を代用。0〜256 文字。</summary>
    [JsonPropertyName("note")]
    [JsonPropertyOrder(0)]
    public string Note { get; set; } = string.Empty;

    /// <summary>アイコンリスト内のインデックス。値域 [0, 99]。</summary>
    [JsonPropertyName("icon")]
    [JsonPropertyOrder(1)]
    public int Icon { get; set; } = 0;

    /// <summary>カテゴリ番号。値域 [0, 39]。0〜11 は固定カテゴリ、12〜39 はユーザー定義。</summary>
    [JsonPropertyName("category")]
    [JsonPropertyOrder(2)]
    public int Category { get; set; } = 0;

    /// <summary>実行ファイルパス / URL / ドキュメント / フォルダ。Cmd &gt;= 0 のときは空。0〜1024 文字。</summary>
    [JsonPropertyName("exe")]
    [JsonPropertyOrder(3)]
    public string Exe { get; set; } = string.Empty;

    /// <summary>コマンドライン引数。0〜65535 文字。</summary>
    [JsonPropertyName("args")]
    [JsonPropertyOrder(4)]
    public string Args { get; set; } = string.Empty;

    /// <summary>作業ディレクトリ。0〜1024 文字。</summary>
    [JsonPropertyName("dir")]
    [JsonPropertyOrder(5)]
    public string Dir { get; set; } = string.Empty;

    /// <summary>実行時に再生する WAV ファイルパス。0〜1024 文字。</summary>
    [JsonPropertyName("sound")]
    [JsonPropertyOrder(6)]
    public string Sound { get; set; } = string.Empty;

    /// <summary>
    /// 修飾キーのビットマスク。MOD_ALT(1) / MOD_CONTROL(2) / MOD_SHIFT(4) / MOD_WIN(8) の OR。
    /// </summary>
    [JsonPropertyName("modifiers")]
    [JsonPropertyOrder(7)]
    public int Modifiers { get; set; } = 0;

    /// <summary>
    /// 仮想キーコード。値域 [0, 1023]。
    /// 512=vkMouse / 513=vkDelete / 514=vkLirc（未使用）/ 515=vkJoy（未使用）。
    /// </summary>
    [JsonPropertyName("vkey")]
    [JsonPropertyOrder(8)]
    public int Vkey { get; set; } = 0;

    /// <summary>
    /// スキャンコード、またはマウスホイール識別子（scanWheelUp = 0x40000000 等）。
    /// </summary>
    [JsonPropertyName("scanCode")]
    [JsonPropertyOrder(9)]
    public int ScanCode { get; set; } = 0;

    /// <summary>
    /// 内部コマンド ID。既定 -1（非コマンド）。有効値は -1、[0, 120]、[200, 299] のいずれか。
    /// Exe が空のときのみ有効。
    /// </summary>
    [JsonPropertyName("cmd")]
    [JsonPropertyOrder(10)]
    public int Cmd { get; set; } = -1;

    /// <summary>ウィンドウ表示状態。0=Normal, 1=Maximized, 2=Minimized。</summary>
    [JsonPropertyName("cmdShow")]
    [JsonPropertyOrder(11)]
    public int CmdShow { get; set; } = 0;

    /// <summary>起動アプリの不透明度。0=設定なし、1〜255=設定値。値域 [0, 255]。</summary>
    [JsonPropertyName("opacity")]
    [JsonPropertyOrder(12)]
    public int Opacity { get; set; } = 0;

    /// <summary>
    /// プロセス優先度。0=Idle, 1=Normal, 2=High, 3=Realtime, 4=BelowNormal, 5=AboveNormal。
    /// 値域 [0, 5]。既定 1（Normal）。
    /// </summary>
    [JsonPropertyName("priority")]
    [JsonPropertyOrder(13)]
    public int Priority { get; set; } = 1;

    /// <summary>ホットキー無効化。true の場合、リスト上には残るが反応しない。</summary>
    [JsonPropertyName("disable")]
    [JsonPropertyOrder(14)]
    public bool Disable { get; set; } = false;

    /// <summary>複数インスタンス許可。false の場合、起動済みなら前面化する。</summary>
    [JsonPropertyName("multInst")]
    [JsonPropertyOrder(15)]
    public bool MultInst { get; set; } = false;

    /// <summary>システムトレイメニューに表示するか。</summary>
    [JsonPropertyName("trayMenu")]
    [JsonPropertyOrder(16)]
    public bool TrayMenu { get; set; } = false;

    /// <summary>IMEIndicator 起動時に自動実行するか。</summary>
    [JsonPropertyName("autoStart")]
    [JsonPropertyOrder(17)]
    public bool AutoStart { get; set; } = false;

    /// <summary>実行前に確認ダイアログを表示するか。</summary>
    [JsonPropertyName("ask")]
    [JsonPropertyOrder(18)]
    public bool Ask { get; set; } = false;

    /// <summary>実行前にカウントダウンを表示するか（ユーザーがキャンセル可能）。</summary>
    [JsonPropertyName("delay")]
    [JsonPropertyOrder(19)]
    public bool Delay { get; set; } = false;

    /// <summary>管理者権限で実行するか（UAC プロンプト表示）。</summary>
    [JsonPropertyName("admin")]
    [JsonPropertyOrder(20)]
    public bool Admin { get; set; } = false;

    /// <summary>
    /// 常に false（現行 C++ 版と同じ互換用プレースホルダ）。詳細はクラスの remarks を参照。
    /// 実際の左右修飾キー区別は <see cref="HotkeyGlobalOptions.DistinguishLeftRightModifiers"/> を使う。
    /// </summary>
    [JsonPropertyName("distinguishLR")]
    [JsonPropertyOrder(21)]
    public bool DistinguishLR
    {
        get => false;
        set { /* 互換用。C++ 版 from_json もこのキーを読まないため、読み込んだ値は捨てる。 */ }
    }

    // --- 派生プロパティ・ヘルパー（永続化対象外。HotKeyEntry.h のメンバー関数相当）---
    // note.empty() ? exe : note と同じ判定に使う vkey の特殊値（HotKeyEntry.h の
    // vkMouse(512) / vkLirc(514) / vkJoy(515) 相当）。AutoCategory() 内でのみ使用する。
    private const int VkMouse = 512;
    private const int VkLirc = 514;
    private const int VkJoy = 515;

    /// <summary>
    /// このエントリが内部コマンド（exe 起動ではない）かどうか。移植元:
    /// src/cpp/models/hotkey/HotKeyEntry.h の isCommand()。有効な cmd の範囲は
    /// <see cref="IMEIndicator.Services.Hotkey.CommandExecutor.IsValidCommandId"/> と同一の
    /// [0,120]∪[200,299] だが、Models 層から Services 層へ依存させないためロジックはここに
    /// 重複させて持つ（値そのものは仕様上固定であり、二重管理による乖離リスクは小さい）。
    /// </summary>
    public bool IsCommand() => Exe.Length == 0 && Cmd >= 0 && (Cmd <= 120 || (Cmd >= 200 && Cmd <= 299));

    /// <summary>
    /// 表示名を取得する。<see cref="Note"/> が空の場合は <see cref="Exe"/> を返す
    /// （<see cref="Exe"/> も空の場合は空文字列のまま。内部コマンドエントリ等で起こりうる）。
    /// 移植元: src/cpp/models/hotkey/HotKeyEntry.h の displayName()。
    /// </summary>
    /// <remarks>
    /// tasks.md の T053 の説明には「note 空なら (無題)」とあるが、これは実際の C++ 実装とは
    /// 異なる。displayName() 自体は note が空なら exe を返すだけであり、"(無題)" という
    /// 固定文字列へのフォールバックは呼び出し側の src/cpp/views/TrayIcon.cpp
    /// （トレイのホットキーサブメニュー構築処理）が displayName() の戻り値が
    /// 「それでもなお空」だった場合にのみ追加で行っている、別レイヤーの責務である。
    /// そのため本プロパティでは "(無題)" への変換は行わない（その処理は、対応する
    /// トレイメニュー実装タスクで DisplayName の呼び出し側が行うべきもの）。
    /// JSON 化対象外（<see cref="JsonIgnoreAttribute"/>）: 導出値であり
    /// contracts/hotkey-entry-schema.md にも存在しないフィールドのため、
    /// バイト互換のシリアライズ結果に含めてはならない。
    /// </remarks>
    [JsonIgnore]
    public string DisplayName => Note.Length == 0 ? Exe : Note;

    /// <summary>
    /// カテゴリを自動判定する。移植元: src/cpp/models/hotkey/HotKeyEntry.h の autoCategory()。
    /// 判定順序・条件は C++ 版と同一（autoStart → trayMenu → vkey 特殊値 → exe 空 →
    /// URL 接頭辞 → 拡張子 → それ以外は Documents）。
    /// </summary>
    public HotkeyCategoryKind AutoCategory()
    {
        if (AutoStart)
        {
            return HotkeyCategoryKind.Autorun;
        }
        if (TrayMenu)
        {
            return HotkeyCategoryKind.TrayMenu;
        }
        if (Vkey == VkMouse)
        {
            return HotkeyCategoryKind.Mouse;
        }
        if (Vkey == VkJoy)
        {
            return HotkeyCategoryKind.Joystick;
        }
        if (Vkey == VkLirc)
        {
            return HotkeyCategoryKind.Remote;
        }
        if (Exe.Length == 0)
        {
            return HotkeyCategoryKind.Commands;
        }

        // URL 判定（簡易）。C++ 版 exe.starts_with(...) と同じ、大小文字を区別する比較。
        if (Exe.StartsWith("http://", StringComparison.Ordinal) ||
            Exe.StartsWith("https://", StringComparison.Ordinal) ||
            Exe.StartsWith("ftp://", StringComparison.Ordinal) ||
            Exe.StartsWith("shell:", StringComparison.Ordinal))
        {
            return HotkeyCategoryKind.WebLinks;
        }

        // 実行ファイル判定。C++ 版の _wcsicmp(ext, L".exe"/".com"/".bat") と同じ、
        // 大小文字を区別しない比較。exe.size() > 4 なら末尾 4 文字、そうでなければ
        // 文字列全体を ext とする C++ 版 substr の挙動（4 文字ちょうどのときは全体を比較）
        // をそのまま維持する。
        string ext = Exe.Length > 4 ? Exe.Substring(Exe.Length - 4) : Exe;
        if (Exe.Length >= 4 &&
            (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(ext, ".com", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase)))
        {
            return HotkeyCategoryKind.Programs;
        }

        return HotkeyCategoryKind.Documents;
    }
}
