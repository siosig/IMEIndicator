// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: C# ポート。
// namespace の変更、Windows SDK 側に対応する定数が無いもの（IME 拡張仮想キー、
// WM_HOTKEY_RAW_KBD/WM_HOTKEY_RAW_MOUSE、vkMouse 等の番兵値）をファイル内ローカル定義として移植)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;

namespace IMEIndicator.Services.Hotkey;

/// <summary>
/// 入力ソースの種別。
/// 移植元: src/cpp/services/hotkey/InputRouter.h の <c>enum class InputSource</c>。
/// </summary>
public enum InputSource
{
    /// <summary>キーボード（vkey &lt; <see cref="InputRouter.VkMouse"/> = 512）。</summary>
    Keyboard,

    /// <summary>マウス（vkey == <see cref="InputRouter.VkMouse"/> = 512）。</summary>
    Mouse,

    /// <summary>特殊入力（vkey == <see cref="InputRouter.VkDelete"/> = 513）。</summary>
    Special,

    /// <summary>WinLIRC リモコン（vkey == <see cref="InputRouter.VkLirc"/> = 514）。</summary>
    Remote,

    /// <summary>ジョイスティック（vkey == <see cref="InputRouter.VkJoy"/> = 515）。</summary>
    Joystick,
}

/// <summary>
/// 入力ルーティング ユーティリティ（静的関数のみ）。
/// 移植元: src/cpp/services/hotkey/InputRouter.h / InputRouter.cpp のクラス <c>InputRouter</c>（1:1 移植）。
/// C++ 版はコンストラクタを <c>delete</c> した「静的関数のみのクラス」のため、C# では static class が自然。
/// </summary>
/// <remarks>
/// <para>
/// 重要な設計上の注記: specs/014-port-to-csharp/tasks.md の T065 記述（登録変更時に UI スレッドで構築する
/// 照合テーブルを <c>volatile</c> 参照で差し替え、<c>TryMatch(in KeyEventData, out int index)</c> は
/// 割り当てなしの線形探索、<c>foregroundExcludeProcesses</c> を参照）は、実際の C++ ソースを調査した結果、
/// 事実と異なることが判明している。
/// 実際のホットキー照合（キー入力が登録済みホットキーのどれに一致するかの判定）は
/// <c>HotkeyManager.FindHotkeyByKey(vkey, modifiers)</c> が担う（vkey と modifiers の完全一致を linear scan、
/// scanCode は比較に使わない）。本クラスはそれとは独立した「入力種別判定・分類」のみを行うユーティリティである。
/// </para>
/// <para>
/// <c>HotkeyGlobalOptions.DistinguishLeftRightModifiers</c> と
/// <c>HotkeyGlobalOptions.ForegroundExcludeProcesses</c> は、モデルには永続化されているものの、
/// 現行 C++ 版のホットキー処理系（HotkeyManager / HotkeyService / InputRouter / HookEngine のいずれからも）
/// から一切参照されていない設定項目のため、本クラスでもこれらを参照する照合ロジックは追加しない。
/// </para>
/// <para>
/// 定数の出所について: <see cref="NativeConstants"/>（Interop/NativeTypes.cs）に同名の定数が既にある場合は
/// それを再利用し（<c>MOD_CONTROL</c> / <c>MOD_SHIFT</c> / <c>MOD_ALT</c> / <c>MOD_WIN</c> / <c>VK_KANJI</c> /
/// <c>VK_CONVERT</c> / <c>VK_NONCONVERT</c> / <c>VK_MENU</c> / <c>VK_LWIN</c> / <c>VK_RWIN</c> /
/// <c>VK_SNAPSHOT</c> / <c>VK_MEDIA_*</c> / <c>VK_VOLUME_*</c>）、無いものは C++ 版の
/// <c>VK_DBE_SBCSCHAR</c>/<c>VK_DBE_DBCSCHAR</c> ローカル定義（InputRouter.cpp 冒頭）や
/// <see cref="IMEIndicator.Services.KeyboardHook"/> の局所定数と同じ方針で、このファイル内に
/// private const として定義する（<see cref="VkMouse"/> 等の番兵値と
/// <c>WM_HOTKEY_RAW_KBD</c>/<c>WM_HOTKEY_RAW_MOUSE</c> は他クラスからの参照を想定し public / internal にする）。
/// </para>
/// </remarks>
public static class InputRouter
{
    // ---- HotkeyP 由来の仮想キー空間の拡張番兵値（src/cpp/models/hotkey/HotKeyEntry.h の
    //      vkMouse/vkDelete/vkLirc/vkJoy と同値）。C# 版 HotKeyEntry.cs（Models/Hotkey/）には
    //      定数化されておらずコメントでのみ言及されているため、唯一の消費者である本クラスで定義する。
    //      他クラス（HotkeyManager 等）からも参照できるよう public にする。 ----

    /// <summary>vkey がマウスであることを示す番兵値。移植元: HotKeyEntry.h の <c>vkMouse</c>。</summary>
    public const uint VkMouse = 512;

    /// <summary>vkey が特殊入力（Delete 系）であることを示す番兵値。移植元: HotKeyEntry.h の <c>vkDelete</c>。</summary>
    public const uint VkDelete = 513;

    /// <summary>vkey が WinLIRC リモコンであることを示す番兵値（現行版でも未使用）。移植元: HotKeyEntry.h の <c>vkLirc</c>。</summary>
    public const uint VkLirc = 514;

    /// <summary>vkey がジョイスティックであることを示す番兵値（現行版でも未使用）。移植元: HotKeyEntry.h の <c>vkJoy</c>。</summary>
    public const uint VkJoy = 515;

    // ---- LL フック → メインウィンドウ通知用カスタム WM メッセージ（src/cpp/services/hotkey/HookEngine.h と同値）。
    //      本タスク時点で HookEngine.cs は未実装のため、唯一の消費者である本クラスで定義する。
    //      HookEngine.cs 側に同名定義が現れて重複した場合の整理はオーケストレーター側で対応する。 ----

    /// <summary>
    /// WPARAM: vkCode、LPARAM: scanCode。移植元: HookEngine.h の <c>WM_HOTKEY_RAW_KBD</c>（= WM_APP(0x8000) + 0x100）。
    /// </summary>
    internal const uint WM_HOTKEY_RAW_KBD = 0x8100;

    /// <summary>
    /// WPARAM: WM_LBUTTONDOWN 等、LPARAM: mouseData。移植元: HookEngine.h の <c>WM_HOTKEY_RAW_MOUSE</c>
    /// （= WM_APP(0x8000) + 0x101）。
    /// </summary>
    internal const uint WM_HOTKEY_RAW_MOUSE = 0x8101;

    // ---- IME 拡張仮想キー・NativeConstants 未定義の VK_* 定数。
    //      IsImeSwitchKey 専用。VK_KANJI/VK_CONVERT/VK_NONCONVERT は NativeConstants に既存のため再利用する。
    //      値は Windows SDK winuser.h の標準マクロ、VK_DBE_* は imm.h 由来（環境によって未定義のことがあるため
    //      C++ 版 InputRouter.cpp 冒頭と同じく直接定数値で定義）。 ----
    private const int VK_KANA = 0x15;          // カナ。NativeConstants.VK_HANGEUL と同値だが、C++ 版の表記
                                                // （VK_KANA）に合わせて可読性のためこの名前で持つ。
    private const int VK_PROCESSKEY = 0xE5;    // IME 処理中
    private const int VK_DBE_SBCSCHAR = 0xF3;  // 全角/半角
    private const int VK_DBE_DBCSCHAR = 0xF4;  // 半角/全角

    // ---- 特殊キー定数。IsSpecialKey 専用。VK_SNAPSHOT は NativeConstants に既存のため再利用する。 ----
    private const int VK_CAPITAL = 0x14; // CapsLock
    private const int VK_NUMLOCK = 0x90; // NumLock
    private const int VK_SCROLL = 0x91;  // ScrollLock
    private const int VK_PAUSE = 0x13;   // Pause/Break

    // ---- マルチメディアキー定数。IsMultimediaKey 専用。
    //      VK_VOLUME_*/VK_MEDIA_* は NativeConstants に既存のため再利用する。 ----
    private const int VK_LAUNCH_MEDIA_SELECT = 0xB5;
    private const int VK_LAUNCH_MAIL = 0xB4;
    private const int VK_LAUNCH_APP1 = 0xB6;
    private const int VK_LAUNCH_APP2 = 0xB7;

    // ---- 修飾キー単体判定（RouteKeyboardEvent）・GetCurrentModifiers 専用。
    //      VK_MENU/VK_LWIN/VK_RWIN は NativeConstants に既存のため再利用する。 ----
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    /// <summary>
    /// vkey と scanCode から入力ソースを判定する。移植元: InputRouter.cpp の <c>classifyInput</c>。
    /// </summary>
    /// <param name="vkey">仮想キーコード。</param>
    /// <param name="scanCode">スキャンコード。C++ 版と同じく判定には使わない（vkey のみで判定する）。</param>
    public static InputSource ClassifyInput(uint vkey, uint scanCode)
    {
        if (vkey == VkMouse) return InputSource.Mouse;
        if (vkey == VkDelete) return InputSource.Special;
        if (vkey == VkLirc) return InputSource.Remote;
        if (vkey == VkJoy) return InputSource.Joystick;
        return InputSource.Keyboard;
    }

    /// <summary>
    /// マルチメディアキー（音量・メディア再生コントロール）かどうか。移植元: InputRouter.cpp の <c>isMultimediaKey</c>。
    /// </summary>
    public static bool IsMultimediaKey(uint vkey)
    {
        switch (vkey)
        {
            case NativeConstants.VK_VOLUME_UP:
            case NativeConstants.VK_VOLUME_DOWN:
            case NativeConstants.VK_VOLUME_MUTE:
            case NativeConstants.VK_MEDIA_PLAY_PAUSE:
            case NativeConstants.VK_MEDIA_NEXT_TRACK:
            case NativeConstants.VK_MEDIA_PREV_TRACK:
            case NativeConstants.VK_MEDIA_STOP:
            case VK_LAUNCH_MEDIA_SELECT:
            case VK_LAUNCH_MAIL:
            case VK_LAUNCH_APP1:
            case VK_LAUNCH_APP2:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 特殊キー（CapsLock, NumLock など）かどうか。移植元: InputRouter.cpp の <c>isSpecialKey</c>。
    /// </summary>
    public static bool IsSpecialKey(uint vkey)
    {
        switch (vkey)
        {
            case VK_CAPITAL:    // CapsLock
            case VK_NUMLOCK:    // NumLock
            case VK_SCROLL:     // ScrollLock
            case NativeConstants.VK_SNAPSHOT: // PrintScreen
            case VK_PAUSE:      // Pause/Break
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// IME 切替キー（VK_KANJI / VK_DBE_DBCSCHAR / VK_DBE_SBCSCHAR / VK_KANA / VK_PROCESSKEY 等）かどうか。
    /// これらは IMEIndicator の既存 <see cref="IMEIndicator.Services.KeyboardHook"/> が観察するキーで、
    /// HookEngine はホットキーとして処理しない
    /// （specs/010-hotkeyp-merge/spec.md FR-021 / specs/010-hotkeyp-merge/research.md R-003）。
    /// 移植元: InputRouter.cpp の <c>isImeSwitchKey</c>。
    /// </summary>
    public static bool IsImeSwitchKey(uint vkey)
    {
        // IMEIndicator の既存 KeyboardHook が IME 状態検出のために観察するキー。
        // HotkeyP の HookEngine はこれらをホットキーとして横取りせず、IME 動作を尊重する。
        switch (vkey)
        {
            case NativeConstants.VK_KANJI:    // 0x19  漢字キー
            case VK_KANA:                     // 0x15  かな (= VK_HANGUL)
            case VK_DBE_DBCSCHAR:              // 0xF3  全角/半角
            case VK_DBE_SBCSCHAR:              // 0xF4  半角/全角
            case VK_PROCESSKEY:                // 0xE5  IME 処理中
            case NativeConstants.VK_CONVERT:   // 0x1C  変換
            case NativeConstants.VK_NONCONVERT: // 0x1D  無変換
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// WH_KEYBOARD_LL が必要かどうか（RegisterHotKey では処理できないキー）。
    /// IME 切替キーは常に false を返す（HotkeyP 機能の対象外）。
    /// 移植元: InputRouter.cpp の <c>requiresLowLevelHook</c>。
    /// </summary>
    public static bool RequiresLowLevelHook(uint vkey, uint modifiers)
    {
        // IME 切替キーは IMEIndicator KeyboardHook の責務 → HookEngine は観察対象外（spec FR-021）
        if (IsImeSwitchKey(vkey)) return false;

        // Win キーを含む → システムが先に処理するため LL フック必要
        if ((modifiers & NativeConstants.MOD_WIN) != 0) return true;

        // マルチメディアキーは RegisterHotKey では登録できない → LL フック必要
        if (IsMultimediaKey(vkey)) return true;

        // 特殊キーも LL フック必要
        if (IsSpecialKey(vkey)) return true;

        // マウス・ジョイスティック・WinLIRC はキーボードフック不要（別スレッド）
        if (vkey >= VkMouse) return false;

        return false;
    }

    /// <summary>
    /// LL フックからのキーボードイベントをルーティングする。vkey と scanCode を受け取り、登録済み
    /// ホットキーとの照合・実行は、呼び出し元が注入する <paramref name="handler"/> に委譲する
    /// （HotkeyManager への直接参照を持たない疎結合設計）。修飾キー単体の押下は無視し、
    /// <paramref name="handler"/> を呼ばない。移植元: InputRouter.cpp の <c>routeKeyboardEvent</c>。
    /// </summary>
    /// <param name="vkey">仮想キーコード。</param>
    /// <param name="scanCode">スキャンコード。</param>
    /// <param name="currentModifiers">現在の修飾キー状態（MOD_CONTROL 等の OR）。</param>
    /// <param name="handler">
    /// vkey・scanCode・modifiers を受け取るコールバック。null の場合は何もしない。
    /// </param>
    public static void RouteKeyboardEvent(uint vkey, uint scanCode, uint currentModifiers, Action<uint, uint, uint>? handler)
    {
        if (handler is null) return;

        // 修飾キー単体は無視
        switch (vkey)
        {
            case VK_CONTROL:
            case VK_LCONTROL:
            case VK_RCONTROL:
            case VK_SHIFT:
            case VK_LSHIFT:
            case VK_RSHIFT:
            case NativeConstants.VK_MENU:
            case VK_LMENU:
            case VK_RMENU:
            case NativeConstants.VK_LWIN:
            case NativeConstants.VK_RWIN:
                return;
            default:
                break;
        }

        handler(vkey, scanCode, currentModifiers);
    }

    /// <summary>
    /// 現在押されている修飾キーを <see cref="NativeMethods.GetAsyncKeyState"/> で取得する。
    /// 移植元: InputRouter.cpp の無名 namespace の <c>getCurrentModifiers</c>。
    /// </summary>
    private static uint GetCurrentModifiers()
    {
        uint mods = 0;
        if ((NativeMethods.GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) mods |= NativeConstants.MOD_CONTROL;
        if ((NativeMethods.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) mods |= NativeConstants.MOD_SHIFT;
        if ((NativeMethods.GetAsyncKeyState(NativeConstants.VK_MENU) & 0x8000) != 0) mods |= NativeConstants.MOD_ALT;
        if (((NativeMethods.GetAsyncKeyState(NativeConstants.VK_LWIN) | NativeMethods.GetAsyncKeyState(NativeConstants.VK_RWIN)) & 0x8000) != 0)
        {
            mods |= NativeConstants.MOD_WIN;
        }

        return mods;
    }

    /// <summary>
    /// WM_HOTKEY_RAW_KBD / WM_HOTKEY_RAW_MOUSE メッセージを処理して <paramref name="handler"/> に転送する。
    /// メインウィンドウのメッセージループ（WndProc 相当）から呼ぶ。処理した場合は true を返す
    /// （呼び出し元で既定のメッセージ処理をスキップするため）。移植元: InputRouter.cpp の <c>handleRawHookMessage</c>。
    /// </summary>
    /// <param name="msg">ウィンドウメッセージ ID。</param>
    /// <param name="wParam">WPARAM。</param>
    /// <param name="lParam">LPARAM。</param>
    /// <param name="handler">vkey・scanCode・modifiers を受け取るコールバック。null の場合は転送しない。</param>
    /// <returns>msg が WM_HOTKEY_RAW_KBD / WM_HOTKEY_RAW_MOUSE のいずれかで処理した場合は true。</returns>
    public static bool HandleRawHookMessage(uint msg, nint wParam, nint lParam, Action<uint, uint, uint>? handler)
    {
        if (msg == WM_HOTKEY_RAW_KBD)
        {
            uint vkey = (uint)wParam;
            uint scanCode = (uint)lParam;
            uint mods = GetCurrentModifiers();
            RouteKeyboardEvent(vkey, scanCode, mods, handler);
            return true;
        }

        if (msg == WM_HOTKEY_RAW_MOUSE)
        {
            // マウスイベント: WPARAM = WM_LBUTTONDOWN 等, LPARAM = mouseData
            // VkMouse (512) を仮想キーとして使用
            handler?.Invoke(VkMouse, 0, (uint)wParam);
            return true;
        }

        return false;
    }
}
