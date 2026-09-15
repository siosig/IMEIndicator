// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;

namespace IMEIndicator.Services;

/// <summary>
/// 低レベルキーボードフック（WH_KEYBOARD_LL）で IME 切替キーと Win+Space を検出する。
/// 移植元: src/cpp/services/KeyboardHook.h / .cpp と等価動作。LowLevelKeyboardProc は
/// 引数で context（ユーザーデータ）を受け取れないため、プロセス内で 1 つのフックハンドルを
/// 共有するシングルトンとして実装する
/// （契約: specs/014-port-to-csharp/contracts/win32-interop-contract.md §コールバックの寿命と割り当て禁止）。
/// </summary>
public sealed class KeyboardHook
{
    // 移植元 KeyboardHook.cpp 無名名前空間の VK 定数と同一値。WinUser.h の標準マクロ名を使う。
    // NativeConstants（Interop/NativeTypes.cs）には未定義のため、C++ 版と同じくこのファイル内の
    // 局所定数として持つ（KeyboardHook 専用のため共通化しない）。
    private const uint VK_KANA = 0x15;       // カナ/かな
    private const uint VK_IME_ON = 0x16;     // IME ON
    private const uint VK_KANJI = 0x19;      // 半角/全角
    private const uint VK_IME_OFF = 0x1A;    // IME OFF
    private const uint VK_CONVERT = 0x1C;    // 変換
    private const uint VK_NONCONVERT = 0x1D; // 無変換
    private const uint VK_OEM_AUTO = 0xF3;   // 一部キーボードの IME OFF
    private const uint VK_OEM_ENLW = 0xF4;   // 一部キーボードの IME ON
    private const uint VK_SPACE = 0x20;

    // WH_KEYBOARD_LL の wParam に載るメッセージ ID（NativeConstants 未定義のため局所定数）。
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    /// <summary>プロセス内で共有するシングルトンインスタンス。</summary>
    public static KeyboardHook Instance { get; } = new();

    // LowLevelKeyboardProc delegate は static readonly フィールドに保持し GC 回収を防ぐ
    // （契約: win32-interop-contract.md §コールバックの寿命と割り当て禁止）。
    private static readonly LowLevelKeyboardProc CallbackDelegate = HookProc;

    // コールバック本体（別スレッドで呼ばれる SetWindowsHookExW のフックチェーン）から読む購読先。
    // 登録変更（Set*Callback）は通常 UI スレッドから行われるため、volatile 参照で安全に差し替える
    // （契約: win32-interop-contract.md §コールバックの寿命と割り当て禁止）。
    private volatile Action<int>? _imeKeyCallback;
    private volatile Action? _languageSwitchCallback;

    // Ctrl キー（左右・汎用いずれのコードでも）の押下状態を追跡する状態機械と、その遷移を
    // 通知するコールバック。背景画像のドラッグ開始・終了判定に使う（018-draggable-background-image）。
    private readonly ControlKeyTracker _controlKeyTracker = new();
    private volatile Action<bool>? _controlKeyCallback;

    private nint _hook;

    private KeyboardHook()
    {
    }

    /// <summary>フックが有効かどうか。</summary>
    public bool IsRunning => _hook != 0;

    /// <summary>
    /// IME 切替キー（半角/全角・変換・無変換・カナ・IME ON/OFF 等）検出時に呼ぶコールバックを登録する。
    /// 引数は検出された vkCode。null を渡すと登録解除。
    /// </summary>
    public void SetImeKeyCallback(Action<int>? callback) => _imeKeyCallback = callback;

    /// <summary>Win+Space（言語切替キー）検出時に呼ぶコールバックを登録する。null を渡すと登録解除。</summary>
    public void SetLanguageSwitchCallback(Action? callback) => _languageSwitchCallback = callback;

    /// <summary>
    /// Ctrl キーの押下状態（左右どちらか、または汎用コード）が変化したときに呼ぶコールバックを登録する。
    /// null を渡すと登録解除。018-draggable-background-image FR-005: このコールバックの追加によって
    /// キー入力を消費・遅延させてはならない（CallNextHookEx は必ず呼ぶ）。
    /// </summary>
    public void SetControlKeyCallback(Action<bool>? callback) => _controlKeyCallback = callback;

    /// <summary>
    /// Ctrl の解放を取りこぼした（画面ロック中に離した等）形跡を検知したとき、App から呼ばれる。
    /// フック側の状態を「何も押されていない」に戻す。
    /// </summary>
    public void ResetControlKeyState() => _controlKeyTracker.Reset();

    /// <summary>フックを開始する。</summary>
    public bool Start()
    {
        if (_hook != 0)
        {
            return true;
        }

        // 現行 C++ 版 ::GetModuleHandleW(nullptr) と同じ（NULL = 自プロセスの実行ファイルモジュール）。
        nint hModule = NativeMethods.GetModuleHandleW(0);
        _hook = NativeMethods.SetWindowsHookExW(NativeConstants.WH_KEYBOARD_LL, CallbackDelegate, hModule, 0);

        bool success = _hook != 0;
        Log.Hook.Information("KeyboardHook.Start: {Success}", success);
        return success;
    }

    /// <summary>フックを解除する。</summary>
    public void Stop()
    {
        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
            Log.Hook.Information("KeyboardHook.Stop");
        }
    }

    // WH_KEYBOARD_LL 本体。観察モード: IME 切替キーを検出してコールバックを呼ぶだけで、
    // キーは消費しない（必ず CallNextHookEx で次へ渡す）。契約により new / ボックス化 / LINQ /
    // 文字列連結 / ログ出力を行わない。KBDLLHOOKSTRUCT は unsafe ポインタで直接読む。
    //
    // 010-hotkeyp-merge 統合後は HotkeyP 由来の HookEngine（Services/Hotkey 配下、本タスクでは未移植）も
    // 同じ WH_KEYBOARD_LL に並行登録され得る。両者は責務分離されており衝突しない
    // （移植元 KeyboardHook.cpp の同コメント参照）。フックチェーンは LIFO だが、本フックは
    // どちらの順序でも必ず CallNextHookEx で次に渡すため、他方の動作を阻害しない。
    private static unsafe nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && lParam != 0)
        {
            bool keyDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool keyUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;
            if (keyDown || keyUp)
            {
                var kb = (KBDLLHOOKSTRUCT*)lParam;
                uint vkCode = kb->VkCode;
                if (keyDown)
                {
                    Instance.HandleKeyDown(vkCode);
                }
                Instance.HandleControlKeyTransition(vkCode, keyUp);
            }
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private void HandleKeyDown(uint vkCode)
    {
        if (IsImeKey(vkCode))
        {
            _imeKeyCallback?.Invoke((int)vkCode);
        }

        if (vkCode == VK_SPACE && IsWinKeyDown())
        {
            _languageSwitchCallback?.Invoke();
        }
    }

    // Ctrl 系の仮想キー（VK_LCONTROL/VK_RCONTROL/VK_CONTROL）の押下/解放だけを ControlKeyTracker へ通す。
    // 契約: フック内の処理は真偽の比較とコールバック呼び出しだけに限る（research.md R-2、
    // LowLevelHooksTimeout を超えると Windows 7 以降は通知なくフックが削除されるため）。
    private void HandleControlKeyTransition(uint vkCode, bool isKeyUp)
    {
        if (_controlKeyTracker.OnKey(vkCode, isKeyUp))
        {
            _controlKeyCallback?.Invoke(_controlKeyTracker.IsDown);
        }
    }

    private static bool IsImeKey(uint vk) =>
        vk == VK_KANA || vk == VK_KANJI || vk == VK_CONVERT || vk == VK_NONCONVERT ||
        vk == VK_IME_ON || vk == VK_IME_OFF || vk == VK_OEM_AUTO || vk == VK_OEM_ENLW;

    private static bool IsWinKeyDown() =>
        (NativeMethods.GetAsyncKeyState(NativeConstants.VK_LWIN) & 0x8000) != 0 ||
        (NativeMethods.GetAsyncKeyState(NativeConstants.VK_RWIN) & 0x8000) != 0;
}
