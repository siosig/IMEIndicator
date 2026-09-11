// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: C# port
// (014-port-to-csharp T054), namespace, testable macro parser intermediate representation)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

using IMEIndicator.Interop;
using IMEIndicator.Services.Hotkey;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// マクロ解析結果の 1 ステップ（SendInput の <c>INPUT</c> 1 件に対応する中間表現）。
/// <see cref="IMEIndicator.Interop"/> の <c>INPUT</c>/<c>KEYBDINPUT</c> は internal で
/// テストプロジェクト（別アセンブリ、InternalsVisibleTo 未設定）から参照できないため、
/// <see cref="TextCommands.ParseMacroToInputs"/> の戻り値としてこの public 型を用いる
/// （テスト容易性のための中間表現。TextCommands.T054 の実装方針どおり）。
/// </summary>
/// <param name="VirtualKey">仮想キーコード。<see cref="IsUnicode"/> が true の場合は無意味（0）。</param>
/// <param name="UnicodeChar">Unicode 文字送出時の文字（<c>KEYEVENTF_UNICODE</c> の wScan に対応）。
/// <see cref="IsUnicode"/> が false の場合は無意味（'\0'）。</param>
/// <param name="IsKeyUp">true = キーアップ（<c>KEYEVENTF_KEYUP</c>）、false = キーダウン。</param>
/// <param name="IsUnicode">true = Unicode 文字送出（<c>KEYEVENTF_UNICODE</c>）、false = 仮想キーコード送出。</param>
public readonly record struct MacroInputStep(ushort VirtualKey, char UnicodeChar, bool IsKeyUp, bool IsUnicode)
{
    /// <summary>仮想キーのキーダウン 1 件を表すステップを作る。</summary>
    public static MacroInputStep KeyDown(ushort vk) => new(vk, '\0', IsKeyUp: false, IsUnicode: false);

    /// <summary>仮想キーのキーアップ 1 件を表すステップを作る。</summary>
    public static MacroInputStep KeyUp(ushort vk) => new(vk, '\0', IsKeyUp: true, IsUnicode: false);

    /// <summary>Unicode 文字のキーダウン 1 件を表すステップを作る。</summary>
    public static MacroInputStep UnicodeDown(char ch) => new(0, ch, IsKeyUp: false, IsUnicode: true);

    /// <summary>Unicode 文字のキーアップ 1 件を表すステップを作る。</summary>
    public static MacroInputStep UnicodeUp(char ch) => new(0, ch, IsKeyUp: true, IsUnicode: true);
}

/// <summary>
/// <c>\sleep</c> で分割された後のマクロ本文 1 区間。
/// 移植元: <c>TextCommands.cpp</c> <c>executeMacro</c> 無名構造体 <c>Segment</c>。
/// </summary>
/// <param name="Text">この区間の生マクロ文字列（<see cref="TextCommands.ParseMacroToInputs"/> へ渡す）。</param>
/// <param name="SleepMilliseconds">この区間の送出後に待機するミリ秒数（0 なら待機なし）。</param>
public readonly record struct MacroSegment(string Text, int SleepMilliseconds);

/// <summary>
/// <c>\rep</c> / <c>\sleep</c> 解析結果全体。
/// 移植元: <c>TextCommands.cpp</c> <c>executeMacro</c> 冒頭のローカル変数群。
/// </summary>
/// <param name="RepeatCount">マクロ全体の繰り返し回数。値域 [1, 100]。既定 1。</param>
/// <param name="Segments"><c>\sleep</c> で分割された区間列。0 件なら（<c>\rep</c> のみ等で本文が
/// 空になった場合）マクロ全体が no-op で成功する（C++ 版と同じ仕様）。</param>
public readonly record struct MacroPlan(int RepeatCount, IReadOnlyList<MacroSegment> Segments);

/// <summary>
/// 内部コマンド分類「テキスト・マクロ」（ID 26, 27, 67, 74, 94）の実行ロジック。
/// 移植元: <c>src/cpp/services/hotkey/commands/TextCommands.h</c> / <c>.cpp</c>
/// （<c>pasteText</c> / <c>setClipboardText</c> / <c>parseMacroToInputs</c> / <c>executeMacro</c>）。
///
/// ID → メソッドの振り分けは行わない（CommandExecutor.cs の責務）。現行 C++ 版
/// <c>CommandExecutor.cpp</c> の dispatch 実態（<c>executeCommandById</c> の
/// 「テキスト/マクロコマンド」節）は次のとおりで、本クラスの 3 メソッドで表現できる：
///   - ID 27（Macro）             → <see cref="ExecuteMacro"/>
///   - ID 74（KeysToActiveWnd）   → <see cref="ExecuteMacro"/>（<c>executeMacro</c> へ直接 dispatch）
///   - ID 26（SendKeysToWindow）  → <see cref="ExecuteMacro"/>（同上）
///   - ID 67（PasteText）        → <see cref="PasteText"/>
///   - ID 94（MacroToActive）    → <see cref="SetClipboardText"/>
///     ※ ID 94 は表示名（CommandCatalog: 「アクティブウィンドウにマクロ送信」）と実装が
///     食い違う既知項目（CommandCatalog.cs のコメントにある「表示名と実際の動作が食い違う
///     41 件」の 1 つ）。<c>executeTextCommand</c> の <c>case 94</c> は
///     <c>setClipboardText(param)</c> を呼んでおり <c>executeMacro</c> ではない。本移植は
///     実装（動作）を正として <c>setClipboardText</c> 相当を割り当てる。
///
/// ### tasks.md との既知の齟齬（実装は実際の C++ ソースを正とした。詳細根拠は各メソッドのコメント）
/// - <b>プレースホルダ展開（%date% 等）は実装しない</b>: tasks.md T054 および
///   <c>specs/010-hotkeyp-merge/contracts/internal-command-catalog.md</c>（<c>expandPlaceholders</c>
///   関数への言及あり）はプレースホルダ展開を要求しているが、現行 <c>TextCommands.cpp</c> の
///   <c>pasteText</c> / <c>setClipboardText</c> / <c>executeMacro</c> のどこにもプレースホルダ
///   展開ロジックは存在しない（<c>%</c> を特別扱いするコードが一切無い）。根拠:
///   (1) <c>git log --all -S"expandPlaceholders" -- src/cpp</c> が全履歴を通じて 0 件
///   （この関数は一度も実装されたことがない）。
///   (2) <c>specs/010-hotkeyp-merge/tasks.md</c> T028 は「プレースホルダ展開は T021 として
///   後続で実施」と先送りしたが、T021 は「実装中の発見により方針変更」で
///   <c>parseMacroToInputs</c> の構文拡張（\sleep・\rep 等）に用途変更され、プレースホルダ
///   展開は最終的にどのタスクでも実施されなかった。
///   (3) プレースホルダ展開を検証するはずだった T064（<c>TextCommandsTests.cpp</c>）は
///   未完了（チェックボックス <c>[ ]</c>）かつファイル自体が存在しない。
/// - <b><c>\show</c> / <c>\rshift</c>,<c>\lshift</c>,<c>\rctrl</c>,<c>\lctrl</c>,<c>\ralt</c>,<c>\lalt</c>
///   は実装しない</b>: <c>specs/010-hotkeyp-merge/contracts/macro-syntax.md</c>
///   （設計時ドキュメント）には記載があるが、現行 <c>parseMacroToInputs</c> にはこれらの
///   エスケープコマンドの分岐が存在しない（対応する識別子チェックが無い）。
/// - <b><c>{WIN}</c> 単体はモディファイアではない</b>: macro-syntax.md は
///   <c>{WIN}&lt;x&gt;</c>（プラス無し）を「次要素への Win 修飾」として説明するが、実装は
///   <c>{WIN+}</c> / <c>{LWIN+}</c>（末尾にリテラル <c>+</c> が必須）のみを修飾子として認識する。
///   <c>{WIN}</c>（プラス無し）は controlKeyMap 経由で「VK_LWIN 単体キー押下」として扱われる。
/// - <b>不正な範囲の <c>\sleep</c> / <c>\rep</c> はエラーにせずクランプする</b>: macro-syntax.md
///   は範囲外を「エラー、解析失敗」としているが、実装は <c>std::clamp</c>
///   （<c>\sleep</c>: [0,60000]、<c>\rep</c>: [1,100]）で丸めるだけでエラーを返さない。
/// - <b>未知の <c>{KEYNAME}</c> / <c>\command</c> は警告ログを出さない</b>: 実装にロギング呼び出しは
///   一切無い。未知の <c>{TOKEN}</c> は無音でスキップ（かつ修飾フラグは維持されたまま次のトークンへ
///   持ち越される）。未知の <c>\xxx</c> はバックスラッシュ 1 文字だけをリテラル出力し、識別子部分は
///   消費せず次ループで通常文字として再処理される（macro-syntax.md の「バリデーション」表は未実装）。
/// - <b>INPUT 総数 4096 件の上限チェックは実装しない</b>: macro-syntax.md に記載があるが、
///   実装に該当するチェックは存在しない。
/// - <b>IME 状態の保護・別スレッド dispatch・多重実行制御は本クラスの範囲外</b>: macro-syntax.md
///   記載のこれらの機能は、仮に実装されるとしても呼び出し側（HotkeyService.cs 等、T068）の
///   責務であり、現行 <c>TextCommands.cpp</c> 自体には実装が無い。
/// </summary>
public static class TextCommands
{
    // ---- 仮想キーコード（WinUser.h の標準値） ----
    // NativeConstants（IMEIndicator.Interop.NativeTypes.cs）は Phase 6 の他タスク（並列実装中）も
    // 触る共有ファイルのため、このファイル専用の定数はここへローカルに持つ
    // （KeyboardHook.cs が同じ理由でローカル定数を持つのと同じ方針）。
    // INPUT_KEYBOARD・KEYEVENTF_KEYUP のみ、型（uint）がそのまま一致し既存流用に適するため
    // NativeConstants 側を直接参照する。
    private const ushort VK_BACK = 0x08;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_PAUSE = 0x13;
    private const ushort VK_CAPITAL = 0x14;
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_PRIOR = 0x21;
    private const ushort VK_NEXT = 0x22;
    private const ushort VK_END = 0x23;
    private const ushort VK_HOME = 0x24;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_SNAPSHOT = 0x2C;
    private const ushort VK_INSERT = 0x2D;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_APPS = 0x5D;
    private const ushort VK_F1 = 0x70;
    private const ushort VK_F2 = 0x71;
    private const ushort VK_F3 = 0x72;
    private const ushort VK_F4 = 0x73;
    private const ushort VK_F5 = 0x74;
    private const ushort VK_F6 = 0x75;
    private const ushort VK_F7 = 0x76;
    private const ushort VK_F8 = 0x77;
    private const ushort VK_F9 = 0x78;
    private const ushort VK_F10 = 0x79;
    private const ushort VK_F11 = 0x7A;
    private const ushort VK_F12 = 0x7B;
    private const ushort VK_F13 = 0x7C;
    private const ushort VK_F14 = 0x7D;
    private const ushort VK_F15 = 0x7E;
    private const ushort VK_F16 = 0x7F;
    private const ushort VK_F17 = 0x80;
    private const ushort VK_F18 = 0x81;
    private const ushort VK_F19 = 0x82;
    private const ushort VK_F20 = 0x83;
    private const ushort VK_F21 = 0x84;
    private const ushort VK_F22 = 0x85;
    private const ushort VK_F23 = 0x86;
    private const ushort VK_F24 = 0x87;
    private const ushort VK_NUMLOCK = 0x90;
    private const ushort VK_SCROLL = 0x91;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_VOLUME_MUTE = 0xAD;
    private const ushort VK_VOLUME_DOWN = 0xAE;
    private const ushort VK_VOLUME_UP = 0xAF;
    private const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
    private const ushort VK_MEDIA_PREV_TRACK = 0xB1;
    private const ushort VK_MEDIA_STOP = 0xB2;
    private const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const ushort VK_LAUNCH_MAIL = 0xB4;
    private const ushort VK_LAUNCH_MEDIA_SELECT = 0xB5;
    private const ushort VK_LAUNCH_APP1 = 0xB6;
    private const ushort VK_LAUNCH_APP2 = 0xB7;

    // NativeConstants に無い SendInput 用フラグ（同ファイル内ローカル定数の方針は上記コメントと同じ）。
    private const uint KEYEVENTF_UNICODE = 0x0004;

    // 制御シーケンス名 → VK コードのマップ。移植元 TextCommands.cpp 無名名前空間 controlKeyMap()。
    // キーは大文字小文字を区別する（C++ 版が std::wstring の厳密一致で比較しているのと同じ。
    // 例えば "{ctrl}"（小文字）は修飾子にもこのマップにもヒットしない ―― 意図的な仕様として踏襲）。
    private static readonly Dictionary<string, ushort> ControlKeyMap = new(StringComparer.Ordinal)
    {
        ["ENTER"] = VK_RETURN,
        ["TAB"] = VK_TAB,
        ["ESC"] = VK_ESCAPE,
        ["ESCAPE"] = VK_ESCAPE,
        ["BACKSPACE"] = VK_BACK,
        ["DELETE"] = VK_DELETE,
        ["DEL"] = VK_DELETE,
        ["INSERT"] = VK_INSERT,
        ["HOME"] = VK_HOME,
        ["END"] = VK_END,
        ["PGUP"] = VK_PRIOR,
        ["PGDN"] = VK_NEXT,
        ["UP"] = VK_UP,
        ["DOWN"] = VK_DOWN,
        ["LEFT"] = VK_LEFT,
        ["RIGHT"] = VK_RIGHT,
        ["F1"] = VK_F1,
        ["F2"] = VK_F2,
        ["F3"] = VK_F3,
        ["F4"] = VK_F4,
        ["F5"] = VK_F5,
        ["F6"] = VK_F6,
        ["F7"] = VK_F7,
        ["F8"] = VK_F8,
        ["F9"] = VK_F9,
        ["F10"] = VK_F10,
        ["F11"] = VK_F11,
        ["F12"] = VK_F12,
        ["F13"] = VK_F13,
        ["F14"] = VK_F14,
        ["F15"] = VK_F15,
        ["F16"] = VK_F16,
        ["F17"] = VK_F17,
        ["F18"] = VK_F18,
        ["F19"] = VK_F19,
        ["F20"] = VK_F20,
        ["F21"] = VK_F21,
        ["F22"] = VK_F22,
        ["F23"] = VK_F23,
        ["F24"] = VK_F24,
        ["WIN"] = VK_LWIN,
        ["SPACE"] = VK_SPACE,
        ["APPS"] = VK_APPS,
        ["NUMLOCK"] = VK_NUMLOCK,
        ["CAPSLOCK"] = VK_CAPITAL,
        ["SCROLLLOCK"] = VK_SCROLL,
        ["PRINTSCREEN"] = VK_SNAPSHOT,
        ["PRTSC"] = VK_SNAPSHOT,
        ["PAUSE"] = VK_PAUSE,
        ["BREAK"] = VK_PAUSE,
        ["MEDIA_PLAY_PAUSE"] = VK_MEDIA_PLAY_PAUSE,
        ["MEDIA_NEXT"] = VK_MEDIA_NEXT_TRACK,
        ["MEDIA_PREV"] = VK_MEDIA_PREV_TRACK,
        ["MEDIA_STOP"] = VK_MEDIA_STOP,
        ["VOL_UP"] = VK_VOLUME_UP,
        ["VOL_DOWN"] = VK_VOLUME_DOWN,
        ["VOL_MUTE"] = VK_VOLUME_MUTE,
        ["LAUNCH_MAIL"] = VK_LAUNCH_MAIL,
        ["LAUNCH_APP1"] = VK_LAUNCH_APP1,
        ["LAUNCH_APP2"] = VK_LAUNCH_APP2,
        ["LAUNCH_MEDIA"] = VK_LAUNCH_MEDIA_SELECT,
    };

    /// <summary>
    /// マクロ文字列を実行する（ID 27 Macro / 74 KeysToActiveWnd / 26 SendKeysToWindow 相当）。
    /// 移植元: <c>TextCommands.cpp</c> <c>executeMacro</c>。<c>\rep</c> によるマクロ全体の繰り返しと
    /// <c>\sleep</c> によるセグメント間ウェイトをサポートし、送出後（および送出失敗時）に押下中の
    /// 修飾キーを強制リリースする。
    /// </summary>
    /// <param name="macro">マクロ文字列。空文字列は <see cref="ExecuteError.InvalidCommand"/>。</param>
    /// <returns>成功時 null、失敗時 <see cref="ExecuteError"/>。</returns>
    public static ExecuteError? ExecuteMacro(string macro)
    {
        if (string.IsNullOrEmpty(macro))
        {
            // TextError::EmptyText 相当（CommandExecutor.cpp fromTextError: EmptyText → InvalidCommand）。
            return ExecuteError.InvalidCommand;
        }

        MacroPlan plan = ParseRepeatAndSleep(macro);

        if (plan.Segments.Count == 0)
        {
            // "\rep <n> " のみ等、\rep 除去後にマクロ本文が空になったケース。
            // C++ 版 executeMacro もここで forceReleaseModifiers を呼ばずに即座に成功を返す
            // （意図的な仕様。segments.empty() の分岐がそのまま return {} になっている）。
            return null;
        }

        for (int rep = 0; rep < plan.RepeatCount; rep++)
        {
            foreach (MacroSegment segment in plan.Segments)
            {
                if (segment.Text.Length > 0)
                {
                    IReadOnlyList<MacroInputStep> steps = ParseMacroToInputs(segment.Text);
                    if (steps.Count > 0)
                    {
                        INPUT[] inputs = ToNativeInputs(steps);
                        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
                        if (sent == 0)
                        {
                            ForceReleaseModifiers();
                            return ExecuteError.ApiCallFailed;
                        }
                    }
                }

                if (segment.SleepMilliseconds > 0)
                {
                    Thread.Sleep(segment.SleepMilliseconds);
                }
            }
        }

        ForceReleaseModifiers();
        return null;
    }

    /// <summary>
    /// テキストをクリップボードへ設定して Ctrl+V を送出する（ID 67 PasteText 相当）。
    /// 移植元: <c>TextCommands.cpp</c> <c>pasteText</c>。
    ///
    /// 【tasks.md との既知の齟齬】tasks.md T054 はこのメソッドに
    /// <c>%date% %time% %computername% %username% %clipboard% %CR% %LF% %TAB% %|</c> の
    /// プレースホルダ展開を要求しているが、現行 <c>pasteText</c>（および <c>setClipboardText</c>）
    /// には <c>%</c> を解釈するコードが存在しない。テキストはそのままクリップボードへ設定される。
    /// 詳細根拠はクラス doc コメント参照。実際の C++ ソースを正としてプレースホルダ展開は実装しない。
    /// </summary>
    /// <param name="text">貼り付けるテキスト。空文字列は <see cref="ExecuteError.InvalidCommand"/>。</param>
    /// <returns>成功時 null、失敗時 <see cref="ExecuteError"/>。</returns>
    public static ExecuteError? PasteText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            // TextError::EmptyText 相当。
            return ExecuteError.InvalidCommand;
        }

        ExecuteError? clipboardResult = SetClipboardText(text);
        if (clipboardResult is not null)
        {
            return clipboardResult;
        }

        // Ctrl+V 送出。現行 pasteText（TextCommands.cpp）は SendInput の戻り値を確認せず、
        // 失敗時も executeMacro のような forceReleaseModifiers 相当の後処理を行わない
        // （executeMacro とは非対称な既存仕様）。本移植ではその挙動を意図的にそのまま踏襲する。
        var inputs = new[]
        {
            CreateKeyInput(VK_CONTROL, 0),
            CreateKeyInput((ushort)'V', 0),
            CreateKeyInput((ushort)'V', NativeConstants.KEYEVENTF_KEYUP),
            CreateKeyInput(VK_CONTROL, NativeConstants.KEYEVENTF_KEYUP),
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return null;
    }

    /// <summary>
    /// テキストをクリップボードへ設定する（ID 94 MacroToActive 相当。詳細はクラス doc コメント参照）。
    /// 移植元: <c>TextCommands.cpp</c> <c>setClipboardText</c>。
    /// </summary>
    /// <param name="text">クリップボードへ設定するテキスト。空文字列も許容する（C++ 版と同じ。
    /// <c>pasteText</c> と異なり空チェックを行わない）。</param>
    /// <returns>成功時 null、失敗時 <see cref="ExecuteError"/>。</returns>
    public static ExecuteError? SetClipboardText(string text)
    {
        try
        {
            if (text.Length == 0)
            {
                // 現行 setClipboardText（TextCommands.cpp）は空文字列でも OpenClipboard →
                // EmptyClipboard → GlobalAlloc（NUL 終端のみ 2 バイト）→ SetClipboardData を行い
                // 成功する（空文字チェックが無い）。Clipboard.SetText はテキストが空文字列だと
                // ArgumentException を送出するため、Clipboard.Clear() で「クリップボードが
                // 空文字列を保持している」のと観測可能な結果が同じ状態を再現する。
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
            }

            return null;
        }
        catch (ExternalException)
        {
            // OpenClipboard 失敗相当（他プロセスがクリップボードを開いている等）。
            // TextError::ClipboardFailed も fromTextError で最終的に ApiCallFailed になるため、
            // 中間の TextError 相当は経由せず直接 ApiCallFailed を返す。
            return ExecuteError.ApiCallFailed;
        }
        catch (ThreadStateException)
        {
            // System.Windows.Forms.Clipboard は STA スレッドからの呼び出しを要求する。
            // 呼び出し元スレッドが MTA の場合にここへ来る。C++ 版に直接対応する分岐は無いが、
            // 「クリップボード操作に失敗した」という結果は同じなので ApiCallFailed に丸める。
            return ExecuteError.ApiCallFailed;
        }
    }

    /// <summary>
    /// マクロ文字列冒頭の <c>\rep &lt;count&gt;</c> と、本文中の <c>\sleep &lt;ms&gt;</c> を解析する。
    /// 移植元: <c>TextCommands.cpp</c> <c>executeMacro</c> 冒頭のローカルロジック（そのものを
    /// 直接テストできるよう独立関数として切り出したもの。<see cref="ExecuteMacro"/> はこの結果を
    /// 使って SendInput を行うだけの薄いオーケストレーションになる）。
    ///
    /// 仕様（C++ 版と同一。tasks.md 側の記述ではなく実装を正とした点は下記コメントおよび
    /// クラス doc コメント参照）:
    /// <list type="bullet">
    /// <item><c>\rep &lt;count&gt;</c> は文字列が丁度その形式で始まる場合のみ解釈し、[1,100] に
    /// クランプする。数字が 1 桁も無い、または桁あふれ等でパースに失敗した場合は
    /// <c>"\rep "</c> をリテラルとして残す（<paramref name="macro"/> は変更せず、RepeatCount は
    /// 既定値 1 のまま）。</item>
    /// <item><c>\sleep &lt;ms&gt;</c> は本文中に現れるたびに区間を分割し、[0,60000] にクランプする。
    /// 数字が続かない場合でも（0 として）<c>"\sleep "</c> タグ自体は必ず本文から取り除かれる
    /// （<c>\rep</c> と異なり、パース失敗時でも常にタグを消費する非対称な既存仕様）。</item>
    /// </list>
    /// </summary>
    public static MacroPlan ParseRepeatAndSleep(string macro)
    {
        string src = macro;

        // \rep <count> パース（冒頭のみ）。パース成功時のみプレフィックスを除去する。
        int repeatCount = 1;
        const string repTag = "\\rep ";
        if (src.StartsWith(repTag, StringComparison.Ordinal))
        {
            int cmdStart = repTag.Length;
            int cmdEnd = cmdStart;
            while (cmdEnd < src.Length && char.IsAsciiDigit(src[cmdEnd]))
            {
                cmdEnd++;
            }

            if (cmdEnd > cmdStart && int.TryParse(src.AsSpan(cmdStart, cmdEnd - cmdStart), out int parsedRepeat))
            {
                repeatCount = Math.Clamp(parsedRepeat, 1, 100);

                // 後続の空白を 1 つだけスキップ
                if (cmdEnd < src.Length && src[cmdEnd] == ' ')
                {
                    cmdEnd++;
                }

                src = src[cmdEnd..];
            }

            // cmdEnd == cmdStart（数字なし）、またはパース失敗（桁あふれ等）の場合は何もしない
            // （"\rep " をリテラルとして扱う。src・repeatCount とも変更しない）。
        }

        // \sleep <ms> でセグメント分割
        var segments = new List<MacroSegment>();
        int pos = 0;
        while (pos < src.Length)
        {
            int sleepPos = src.IndexOf("\\sleep ", pos, StringComparison.Ordinal);
            if (sleepPos < 0)
            {
                segments.Add(new MacroSegment(src[pos..], 0));
                break;
            }

            int numStart = sleepPos + 7; // "\sleep " の後
            int numEnd = numStart;
            while (numEnd < src.Length && char.IsAsciiDigit(src[numEnd]))
            {
                numEnd++;
            }

            int ms = 0;
            if (numEnd > numStart)
            {
                ms = int.TryParse(src.AsSpan(numStart, numEnd - numStart), out int parsedMs)
                    ? Math.Clamp(parsedMs, 0, 60000)
                    : 0;
            }

            segments.Add(new MacroSegment(src[pos..sleepPos], ms));

            // 末尾の空白を 1 つだけスキップ
            if (numEnd < src.Length && src[numEnd] == ' ')
            {
                numEnd++;
            }

            pos = numEnd;
        }

        return new MacroPlan(repeatCount, segments);
    }

    /// <summary>
    /// マクロ文字列（1 区間分）を解析して <see cref="MacroInputStep"/> 列を生成する。
    /// 移植元: <c>TextCommands.cpp</c> <c>parseMacroToInputs</c>（無名名前空間の
    /// addKeyDownUp/addKeyDown/addKeyUp/addUnicodeChar 群を含む）を 1:1 移植。
    ///
    /// サポートする制御シーケンス:
    /// <list type="bullet">
    /// <item>特殊キー: <c>{ENTER}</c> <c>{TAB}</c> <c>{ESC}</c>/<c>{ESCAPE}</c> <c>{BACKSPACE}</c>
    /// <c>{DELETE}</c>/<c>{DEL}</c> <c>{INSERT}</c> <c>{HOME}</c> <c>{END}</c> <c>{PGUP}</c>
    /// <c>{PGDN}</c> <c>{UP}</c> <c>{DOWN}</c> <c>{LEFT}</c> <c>{RIGHT}</c> <c>{F1}</c>〜<c>{F24}</c>
    /// <c>{WIN}</c>（単体は VK_LWIN の単独押下） <c>{SPACE}</c> <c>{APPS}</c> <c>{NUMLOCK}</c>
    /// <c>{CAPSLOCK}</c> <c>{SCROLLLOCK}</c> <c>{PRINTSCREEN}</c>/<c>{PRTSC}</c> <c>{PAUSE}</c>/
    /// <c>{BREAK}</c>、メディア/起動キー（<c>{MEDIA_PLAY_PAUSE}</c> 等）。トークンは大文字小文字を
    /// 区別する。</item>
    /// <item>修飾子: <c>{CTRL}</c>/<c>{CONTROL}</c> <c>{ALT}</c> <c>{SHIFT}</c> <c>{WIN+}</c>/
    /// <c>{LWIN+}</c>（末尾の <c>+</c> が必須。次の 1 要素にのみ適用され、要素処理後に自動解除）。</item>
    /// <item>エスケープ: <c>\\</c> <c>\{</c> <c>\}</c>（リテラル） <c>\n</c>（Enter エイリアス）
    /// <c>\t</c>（Tab エイリアス） <c>\media_play_pause</c> <c>\media_next</c> <c>\media_prev</c>
    /// <c>\media_stop</c> <c>\launch_mail</c> <c>\launch_app1</c> <c>\launch_app2</c>
    /// <c>\launch_media</c>（小文字固定）。</item>
    /// <item>未知の <c>{TOKEN}</c> は無音でスキップし、修飾フラグ（ctrl/alt/shift/win）は
    /// リセットされず次のトークンへ持ち越される（C++ 版と同じ挙動）。</item>
    /// <item>未知の <c>\xxx</c> はバックスラッシュ 1 文字だけをリテラル出力し、続く識別子文字列は
    /// 消費しない（次ループで通常文字として個別に再処理される）。</item>
    /// <item>閉じ括弧の無い <c>{</c> は <c>{</c> 自体をリテラル文字として出力する。</item>
    /// </list>
    /// </summary>
    /// <param name="macro">解析対象のマクロ文字列（1 区間分）。</param>
    /// <returns>SendInput 送出順に並んだステップ列。</returns>
    public static IReadOnlyList<MacroInputStep> ParseMacroToInputs(string macro)
    {
        var steps = new List<MacroInputStep>(macro.Length * 2);

        bool ctrl = false;
        bool alt = false;
        bool shift = false;
        bool win = false;

        int i = 0;
        while (i < macro.Length)
        {
            // エスケープシーケンス: \\ \{ \} \n \t \media_* \launch_*
            if (macro[i] == '\\' && i + 1 < macro.Length)
            {
                char next = macro[i + 1];
                if (next is '\\' or '{' or '}')
                {
                    AddUnicodeChar(steps, next);
                    i += 2;
                    continue;
                }

                if (next == 'n')
                {
                    AddKeyDownUp(steps, VK_RETURN);
                    i += 2;
                    continue;
                }

                if (next == 't')
                {
                    AddKeyDownUp(steps, VK_TAB);
                    i += 2;
                    continue;
                }

                // \media_play_pause / \media_next / \media_prev / \media_stop
                // \launch_app1 / \launch_app2 / \launch_mail / \launch_media
                // 末尾までスキャンして識別子を抽出
                int cmdStart = i + 1;
                int cmdEnd = cmdStart;
                while (cmdEnd < macro.Length && (char.IsAsciiLetterOrDigit(macro[cmdEnd]) || macro[cmdEnd] == '_'))
                {
                    cmdEnd++;
                }

                string cmdName = macro[cmdStart..cmdEnd];
                ushort vk = cmdName switch
                {
                    "media_play_pause" => VK_MEDIA_PLAY_PAUSE,
                    "media_next" => VK_MEDIA_NEXT_TRACK,
                    "media_prev" => VK_MEDIA_PREV_TRACK,
                    "media_stop" => VK_MEDIA_STOP,
                    "launch_mail" => VK_LAUNCH_MAIL,
                    "launch_app1" => VK_LAUNCH_APP1,
                    "launch_app2" => VK_LAUNCH_APP2,
                    "launch_media" => VK_LAUNCH_MEDIA_SELECT,
                    _ => (ushort)0,
                };

                if (vk != 0)
                {
                    AddKeyDownUp(steps, vk);
                    i = cmdEnd;
                    continue;
                }

                // 認識できないエスケープ: バックスラッシュ自身を文字として出力し、1 文字だけ進める
                // （識別子として読んだ文字列は消費しない。次のループで通常文字として再処理される）。
                AddUnicodeChar(steps, '\\');
                i += 1;
                continue;
            }

            if (macro[i] == '{')
            {
                int end = macro.IndexOf('}', i + 1);
                if (end < 0)
                {
                    // 閉じ括弧が無い不正なシーケンス: '{' をそのまま文字として出力
                    AddUnicodeChar(steps, macro[i]);
                    i += 1;
                    continue;
                }

                string token = macro[(i + 1)..end];

                // 修飾キー: 次のキーに適用
                if (token is "CTRL" or "CONTROL")
                {
                    ctrl = true;
                    i = end + 1;
                    continue;
                }

                if (token == "ALT")
                {
                    alt = true;
                    i = end + 1;
                    continue;
                }

                if (token == "SHIFT")
                {
                    shift = true;
                    i = end + 1;
                    continue;
                }

                if (token is "WIN+" or "LWIN+")
                {
                    win = true;
                    i = end + 1;
                    continue;
                }

                // 通常の制御キー。未知のトークンの場合は何も出力せず、修飾フラグも変更しない
                // （C++ 版と同じ: ctrl/alt/shift/win はリセットされず次のトークンへ持ち越される）。
                if (ControlKeyMap.TryGetValue(token, out ushort mappedVk))
                {
                    if (ctrl)
                    {
                        AddKeyDown(steps, VK_CONTROL);
                    }

                    if (alt)
                    {
                        AddKeyDown(steps, VK_MENU);
                    }

                    if (shift)
                    {
                        AddKeyDown(steps, VK_SHIFT);
                    }

                    if (win)
                    {
                        AddKeyDown(steps, VK_LWIN);
                    }

                    AddKeyDownUp(steps, mappedVk);

                    if (win)
                    {
                        AddKeyUp(steps, VK_LWIN);
                    }

                    if (shift)
                    {
                        AddKeyUp(steps, VK_SHIFT);
                    }

                    if (alt)
                    {
                        AddKeyUp(steps, VK_MENU);
                    }

                    if (ctrl)
                    {
                        AddKeyUp(steps, VK_CONTROL);
                    }

                    ctrl = alt = shift = win = false;
                }

                i = end + 1;
                continue;
            }

            // 通常文字
            {
                char ch = macro[i];
                if (ctrl)
                {
                    AddKeyDown(steps, VK_CONTROL);
                }

                if (alt)
                {
                    AddKeyDown(steps, VK_MENU);
                }

                if (shift)
                {
                    AddKeyDown(steps, VK_SHIFT);
                }

                if (win)
                {
                    AddKeyDown(steps, VK_LWIN);
                }

                AddUnicodeChar(steps, ch);

                if (win)
                {
                    AddKeyUp(steps, VK_LWIN);
                }

                if (shift)
                {
                    AddKeyUp(steps, VK_SHIFT);
                }

                if (alt)
                {
                    AddKeyUp(steps, VK_MENU);
                }

                if (ctrl)
                {
                    AddKeyUp(steps, VK_CONTROL);
                }

                ctrl = alt = shift = win = false;
                i += 1;
            }
        }

        return steps;
    }

    private static void AddKeyDownUp(List<MacroInputStep> steps, ushort vk)
    {
        steps.Add(MacroInputStep.KeyDown(vk));
        steps.Add(MacroInputStep.KeyUp(vk));
    }

    private static void AddKeyDown(List<MacroInputStep> steps, ushort vk) => steps.Add(MacroInputStep.KeyDown(vk));

    private static void AddKeyUp(List<MacroInputStep> steps, ushort vk) => steps.Add(MacroInputStep.KeyUp(vk));

    private static void AddUnicodeChar(List<MacroInputStep> steps, char ch)
    {
        steps.Add(MacroInputStep.UnicodeDown(ch));
        steps.Add(MacroInputStep.UnicodeUp(ch));
    }

    // MacroInputStep 列を実際に SendInput へ渡せる INPUT[] へ変換する。
    // ExecuteMacro からのみ使用（テストは ParseMacroToInputs の戻り値を直接検証するため、
    // ここを経由しない＝internal 型 INPUT がテストプロジェクトから見えなくても問題にならない）。
    private static INPUT[] ToNativeInputs(IReadOnlyList<MacroInputStep> steps)
    {
        var inputs = new INPUT[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            MacroInputStep step = steps[i];
            if (step.IsUnicode)
            {
                uint flags = KEYEVENTF_UNICODE | (step.IsKeyUp ? NativeConstants.KEYEVENTF_KEYUP : 0);
                inputs[i] = CreateKeyInput(vk: 0, flags: flags, scan: step.UnicodeChar);
            }
            else
            {
                uint flags = step.IsKeyUp ? NativeConstants.KEYEVENTF_KEYUP : 0;
                inputs[i] = CreateKeyInput(step.VirtualKey, flags);
            }
        }

        return inputs;
    }

    private static INPUT CreateKeyInput(ushort vk, uint flags, ushort scan = 0) => new()
    {
        Type = NativeConstants.INPUT_KEYBOARD,
        U = new InputUnion
        {
            Ki = new KEYBDINPUT
            {
                Vk = vk,
                Scan = scan,
                Flags = flags,
                Time = 0,
                ExtraInfo = 0,
            },
        },
    };

    /// <summary>
    /// マクロ実行終了時に押下中の修飾キーを強制リリースする。
    /// 移植元: <c>TextCommands.cpp</c> 無名名前空間 <c>forceReleaseModifiers</c>
    /// （spec Edge Case「マクロのキーリリース漏れ」対策）。
    /// </summary>
    private static void ForceReleaseModifiers()
    {
        ReadOnlySpan<ushort> modifierVks =
        [
            VK_CONTROL, VK_LCONTROL, VK_RCONTROL,
            VK_SHIFT, VK_LSHIFT, VK_RSHIFT,
            VK_MENU, VK_LMENU, VK_RMENU,
            VK_LWIN, VK_RWIN,
        ];

        List<INPUT>? releases = null;
        foreach (ushort vk in modifierVks)
        {
            short state = NativeMethods.GetAsyncKeyState(vk);
            if ((state & 0x8000) != 0)
            {
                releases ??= new List<INPUT>(modifierVks.Length);
                releases.Add(CreateKeyInput(vk, NativeConstants.KEYEVENTF_KEYUP));
            }
        }

        if (releases is { Count: > 0 })
        {
            NativeMethods.SendInput((uint)releases.Count, releases.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }
}
