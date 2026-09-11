// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: C# ポート。
// namespace の変更、戻り値を IMEIndicator.Services.Hotkey.ExecuteError? へ統合)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using IMEIndicator.Interop;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// ディスプレイ制御コマンド（内部コマンド ID 18 / 23。contracts/internal-command-catalog.md
/// 「ディスプレイ（2）」）。移植元: src/cpp/services/hotkey/commands/DisplayCommands.h / .cpp。
/// EnumDisplaySettingsW でプライマリモニターの現在の DEVMODE を取得し、ChangeDisplaySettingsExW
/// （CDS_UPDATEREGISTRY）で画面の向きを回転させる。
/// </summary>
/// <remarks>
/// ID → メソッドの dispatch は本クラスの責務外（後続タスクの CommandExecutor.cs が担当する）。
/// C++ 版の <c>executeDisplayCommand(cmdId)</c> のような ID による switch はここには実装しない。
///
/// カタログ上の表示名（ID 18 = 「ディスプレイ: 解像度切替」、ID 23 = 「ランダム壁紙」）は
/// いずれも実際の動作（画面回転）と一致しない（⚠ internal-command-catalog.md 173〜174 行目
/// 参照）。表示名の是正は本フィーチャーの範囲外のため、本クラスは動作（<c>rotateDisplay</c>）
/// どおりに実装し、表示名は変更しない。
///
/// 内部コマンド ID 19（表示名は「電源: モニター電源オフ」）は CommandExecutor.cpp の電源コマンド
/// switch で先に処理され、<c>turnOffMonitor()</c>（C# 版 <see cref="PowerCommands.TurnOffMonitor"/>）
/// が呼ばれる。C++ 版 <c>executeDisplayCommand</c> の switch 文にも
/// <c>case 19: return rotateDisplay(270);</c> があるが、電源コマンド側で既に return 済みのため
/// 実行時には到達しない（到達不能コード）。<see cref="RotateDisplay"/> は
/// <c>rotateDisplay(DWORD degrees)</c> 自体の 1:1 移植として degrees=270 も正しく処理できるが、
/// 現時点でこの経路を呼び出す ID は存在しない。
///
/// C++ 版 DisplayCommands.h/.cpp が持つ <c>flipDisplayHorizontal()</c> は、Win32 API のみでは
/// 水平反転を実現できず常に no-op を返す実装であり、かつ <c>executeDisplayCommand</c> のどの
/// case からも呼ばれていない（C++ 版の時点で既に到達不能コード）。本クラスでは対応する
/// メソッドを設けない。
/// </remarks>
public static class DisplayCommands
{
    // ---- DEVMODEW.DmFields に立てるビットフラグ（wingdi.h）----
    // DEVMODEW 構造体（NativeTypes.cs）の同名フィールド（DmDisplayOrientation 等）と
    // PascalCase 変換後の綴りが一致してしまい紛らわしいため、このファイルに限り Win32 の
    // マクロ名をそのまま（SCREAMING_SNAKE_CASE）残す。
    // https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-devmodew
    private const uint DM_DISPLAYORIENTATION = 0x00000080;
    private const uint DM_PELSWIDTH = 0x00080000;
    private const uint DM_PELSHEIGHT = 0x00100000;

    // ---- ChangeDisplaySettingsExW（wingdi.h）----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexw
    private const uint CdsUpdateRegistry = 0x00000001;
    private const int DispChangeSuccessful = 0;

    // ---- EnumDisplaySettingsW（wingdi.h）----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw
    private const uint EnumCurrentSettings = unchecked((uint)-1);

    /// <summary>
    /// 画面の向きを回転する（<c>rotateDisplay(DWORD degrees)</c> の 1:1 移植）。
    /// 内部コマンド ID 18（<c>RotateDisplay(90)</c>）・23（<c>RotateDisplay(180)</c>）。
    /// EnumDisplaySettingsW でプライマリモニターの現在の DEVMODE を取得し、
    /// dmDisplayOrientation を回転方向へ進めた DEVMODE を ChangeDisplaySettingsExW
    /// （CDS_UPDATEREGISTRY）で適用する。
    /// </summary>
    /// <param name="degrees">
    /// 回転量。90 / 180 / 270 のいずれか。C++ 版と同じく、これら以外の値（0 を含む）は
    /// 「向きが変わらない」として扱い、エラーにはせず何もせず成功を返す（C++ 版
    /// <c>rotateDisplay</c> の if / else if チェーンがいずれにもマッチしない場合と同じ挙動）。
    /// </param>
    /// <returns>
    /// 成功時（向きが変わらない no-op を含む）は null。プライマリモニターの DEVMODE 取得に
    /// 失敗した場合、および ChangeDisplaySettingsExW が DISP_CHANGE_SUCCESSFUL 以外を返した
    /// 場合は <see cref="ExecuteError.ApiCallFailed"/>。C++ 版は前者を
    /// <c>DisplayError::NoDisplay</c>、後者を <c>DisplayError::ApiCallFailed</c> として区別するが、
    /// <see cref="ExecuteError"/> にはディスプレイ未検出専用の値が無いため、C# 版ではどちらも
    /// この値にまとめる。
    /// </returns>
    public static ExecuteError? RotateDisplay(int degrees)
    {
        if (!TryGetCurrentDevMode(out DEVMODEW dm))
        {
            return ExecuteError.ApiCallFailed;
        }

        // 現在の向きから新しい向きを計算する（DMDO_DEFAULT=0 / DMDO_90=1 / DMDO_180=2 / DMDO_270=3）。
        uint current = dm.DmDisplayOrientation;
        uint next = degrees switch
        {
            90 => (current + 1) % 4,
            270 => (current + 3) % 4,
            180 => (current + 2) % 4,
            _ => current,
        };

        if (next == current)
        {
            return null;
        }

        // 現在の向きと次の向きで偶奇が変わる回転（90°・270° 方向）でのみ縦横を入れ替える。
        // degrees の値だけでは判定できない（例: 90° から 180° 回転すると 270° になるが、
        // どちらも「縦」向きのため入れ替え不要）。current/next の偶奇比較が C++ 版と同じ
        // 唯一の正しい判定方法。
        if ((current % 2) != (next % 2))
        {
            (dm.DmPelsWidth, dm.DmPelsHeight) = (dm.DmPelsHeight, dm.DmPelsWidth);
        }

        dm.DmDisplayOrientation = next;
        dm.DmFields = DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;

        int result = NativeMethods.ChangeDisplaySettingsExW(null, ref dm, 0, CdsUpdateRegistry, 0);
        return result == DispChangeSuccessful ? null : ExecuteError.ApiCallFailed;
    }

    /// <summary>
    /// プライマリモニターの現在の DEVMODE を取得する（C++ 版の匿名名前空間関数
    /// <c>getCurrentDevMode(DEVMODEW&amp;)</c> 相当）。
    /// </summary>
    /// <remarks>
    /// DmDeviceName / DmFormName（固定長文字列フィールド）は <c>default</c> のままだと null になり
    /// non-nullable な <see cref="string"/> フィールドとして扱っている NativeTypes.cs の宣言と
    /// 食い違うため、<see cref="MONITORINFOEXW.Create"/> と同じ方針で明示的に空文字列を設定する
    /// （このメソッドではどちらのフィールドも読み取らないため、動作上は C++ 版の
    /// <c>dm = {}; dm.dmSize = sizeof(DEVMODEW);</c> と等価）。
    /// </remarks>
    private static bool TryGetCurrentDevMode(out DEVMODEW dm)
    {
        dm = new DEVMODEW
        {
            DmDeviceName = string.Empty,
            DmFormName = string.Empty,
            DmSize = (ushort)Marshal.SizeOf<DEVMODEW>(),
        };
        return NativeMethods.EnumDisplaySettingsW(null, EnumCurrentSettings, ref dm);
    }
}
