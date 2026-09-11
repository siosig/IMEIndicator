// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: C# (.NET/WinForms) への移植、
// ExecuteError への統合)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using IMEIndicator.Interop;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// HotkeyP モダン再設計 - マウス操作シミュレーションコマンド（コマンド ID 37・43〜45・75〜76・79〜80・108）。
/// 移植元: <c>src/cpp/services/hotkey/commands/MouseCommands.h</c> / <c>.cpp</c>。
/// <see cref="Execute"/> は C++ 版の <c>executeMouseCommand(int cmdId)</c> の移植で、
/// <c>CommandExecutor.cpp</c> のマウスコマンド switch（case 37/43/44/45/75/76/79/80/108）から
/// 実際に呼び出される dispatcher である（他の分類の一部に見られる未使用のデッドコードではない）。
/// <c>SendInput</c> を使用してマウスイベントを送信する。
/// </summary>
/// <remarks>
/// カタログ上の表示名（<see cref="CommandCatalog"/>）と実際の動作が食い違う ID が複数ある
/// （internal-command-catalog.md「マウス（9）」に ⚠ 表記あり。例: ID 44 の表示名は
/// 「マウス: 中クリック」だが実際は右クリック、ID 108 の表示名は「水平ホイール」だが実際は左クリック）。
/// 本クラスは現行 C++ 版の実際の動作をそのまま踏襲する（表示名との食い違いの是正はこの移植の範囲外）。
/// </remarks>
public static class MouseCommands
{
    /// <summary>
    /// マウスコマンドを実行する。移植元: MouseCommands.cpp <c>executeMouseCommand(int cmdId)</c>。
    /// </summary>
    /// <param name="cmdId">コマンド ID（37 / 43 / 44 / 45 / 75 / 76 / 79 / 80 / 108 のいずれか）。</param>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError"/>。</returns>
    public static ExecuteError? Execute(int cmdId) => cmdId switch
    {
        // 37 マウス移動(⚠実際は左クリック) / 43 左クリック / 108 水平ホイール(⚠実際は左クリック)
        37 or 43 or 108 => MouseClick(NativeConstants.MOUSEEVENTF_LEFTDOWN, NativeConstants.MOUSEEVENTF_LEFTUP),
        // 44 中クリック(⚠実際は右クリック)
        44 => MouseClick(NativeConstants.MOUSEEVENTF_RIGHTDOWN, NativeConstants.MOUSEEVENTF_RIGHTUP),
        // 45 右クリック(⚠実際は中クリック)
        45 => MouseClick(NativeConstants.MOUSEEVENTF_MIDDLEDOWN, NativeConstants.MOUSEEVENTF_MIDDLEUP),
        75 => MouseScroll(3),    // ホイールスクロール(表示名どおり): スクロールアップ 3 ノッチ
        76 => MouseScroll(-3),   // 76 ダブルクリック(⚠実際はスクロールダウン 3 ノッチ)
        79 => MoveMouseRelative(-10, 0), // 79 第 4 ボタンクリック(⚠実際は x −10 相対移動)
        80 => MoveMouseRelative(10, 0),  // 80 第 5 ボタンクリック(⚠実際は x +10 相対移動)
        _ => ExecuteError.InvalidCommand,
    };

    /// <summary>
    /// マウスカーソルを絶対座標へ移動する。移植元: MouseCommands.cpp <c>moveMouse(int x, int y)</c>。
    /// <see cref="Execute"/>（=C++ 版 executeMouseCommand）からは呼ばれない
    /// （C++ 版でも同様に未使用。9 種のコマンドはいずれも相対移動・クリック・スクロールのみを使う）が、
    /// モジュールの公開 API として C++ ヘッダの宣言と対称になるよう 1:1 移植する。
    /// </summary>
    /// <param name="x">移動先の X 座標（スクリーン座標）。</param>
    /// <param name="y">移動先の Y 座標（スクリーン座標）。</param>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError"/>。</returns>
    internal static ExecuteError? MoveMouse(int x, int y)
    {
        // 絶対座標は MOUSEEVENTF_ABSOLUTE フラグを使用し、0〜65535 の正規化座標に変換して渡す。
        int screenW = NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN);
        int screenH = NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN);
        if (screenW <= 0 || screenH <= 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        int normX = x * 65535 / screenW;
        int normY = y * 65535 / screenH;

        INPUT input = new()
        {
            Type = NativeConstants.INPUT_MOUSE,
            U = new InputUnion
            {
                Mi = new MOUSEINPUT
                {
                    Dx = normX,
                    Dy = normY,
                    Flags = NativeConstants.MOUSEEVENTF_MOVE | NativeConstants.MOUSEEVENTF_ABSOLUTE,
                },
            },
        };
        return SendMouseInput(input);
    }

    /// <summary>
    /// マウスカーソルを相対座標（現在位置からの差分）で移動する。
    /// 移植元: MouseCommands.cpp <c>moveMouseRelative(int dx, int dy)</c>。コマンド 79・80 が使用。
    /// </summary>
    /// <param name="dx">X 方向の移動量（現在位置からの相対値）。</param>
    /// <param name="dy">Y 方向の移動量（現在位置からの相対値）。</param>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError"/>。</returns>
    internal static ExecuteError? MoveMouseRelative(int dx, int dy)
    {
        INPUT input = new()
        {
            Type = NativeConstants.INPUT_MOUSE,
            U = new InputUnion
            {
                Mi = new MOUSEINPUT { Dx = dx, Dy = dy, Flags = NativeConstants.MOUSEEVENTF_MOVE },
            },
        };
        return SendMouseInput(input);
    }

    /// <summary>
    /// マウスボタンのダウン・アップを送出してクリックを再現する。
    /// 移植元: MouseCommands.cpp <c>mouseClick(DWORD buttonDownFlag, DWORD buttonUpFlag)</c>。
    /// コマンド 37・43・44・45・108 が使用。C++ 版と同じく 2 件の INPUT を 1 回の SendInput 呼び出しで送出する。
    /// </summary>
    /// <param name="buttonDownFlag">ボタン押下フラグ（<c>MOUSEEVENTF_*DOWN</c>）。</param>
    /// <param name="buttonUpFlag">ボタン解放フラグ（<c>MOUSEEVENTF_*UP</c>）。</param>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError"/>。</returns>
    internal static ExecuteError? MouseClick(uint buttonDownFlag, uint buttonUpFlag)
    {
        INPUT down = new()
        {
            Type = NativeConstants.INPUT_MOUSE,
            U = new InputUnion { Mi = new MOUSEINPUT { Flags = buttonDownFlag } },
        };
        INPUT up = new()
        {
            Type = NativeConstants.INPUT_MOUSE,
            U = new InputUnion { Mi = new MOUSEINPUT { Flags = buttonUpFlag } },
        };
        return SendMouseInput(down, up);
    }

    /// <summary>
    /// マウスホイールをスクロールする。移植元: MouseCommands.cpp <c>mouseScroll(int delta)</c>。
    /// コマンド 75・76 が使用。
    /// </summary>
    /// <param name="delta">スクロール量（ノッチ単位）。正 = 上、負 = 下。<c>WHEEL_DELTA</c> 倍して
    /// <c>MOUSEINPUT.MouseData</c> に設定する（C++ 版の <c>static_cast&lt;DWORD&gt;</c> と同じく、
    /// 負値は 2 の補数表現のまま符号なし整数へ変換する）。</param>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError"/>。</returns>
    internal static ExecuteError? MouseScroll(int delta)
    {
        INPUT input = new()
        {
            Type = NativeConstants.INPUT_MOUSE,
            U = new InputUnion
            {
                Mi = new MOUSEINPUT
                {
                    MouseData = unchecked((uint)(delta * NativeConstants.WHEEL_DELTA)),
                    Flags = NativeConstants.MOUSEEVENTF_WHEEL,
                },
            },
        };
        return SendMouseInput(input);
    }

    /// <summary>
    /// <c>SendInput</c> を呼び出す共通ヘルパー（C++ 版には存在しない、1 件送出と mouseClick の
    /// 2 件同時送出を共通化する小関数）。<c>cbSize</c> は Win32 API の契約どおり配列全体のサイズではなく
    /// INPUT 1 件分のサイズを渡す。
    /// https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput
    /// </summary>
    /// <param name="inputs">送出する INPUT（1 件または複数件。呼び出し順に送出される）。</param>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    private static ExecuteError? SendMouseInput(params INPUT[] inputs)
    {
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == 0 ? ExecuteError.ApiCallFailed : null;
    }
}
