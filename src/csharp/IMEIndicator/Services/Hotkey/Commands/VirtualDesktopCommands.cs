// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: C# ポート
// （VirtualDesktopManager → VirtualDesktopCommands 静的クラス）。戻り値を
// IMEIndicator.Services.Hotkey.ExecuteError? へ統合。ID による dispatch（execute()）は
// 本クラスの責務外とし実装しない（CommandExecutor.cs へ移管）。未使用の
// IVirtualDesktopManager COM に関する記述は含めない)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using IMEIndicator.Interop;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// 仮想デスクトップ操作の内部コマンド実装（内部コマンド ID 116〜119。
/// contracts/internal-command-catalog.md「仮想デスクトップ（4）」）。
/// 移植元: src/cpp/services/hotkey/platform/VirtualDesktop.h / .cpp の VirtualDesktopManager。
/// </summary>
/// <remarks>
/// <para>
/// <b>重要（移植元の実装調査結果）</b>: 移植元 <c>VirtualDesktop.h</c> のコメントには
/// 「Windows 11 では IVirtualDesktopManager COM インターフェースを使う」という記載があるが、
/// これは実態と異なる。<c>VirtualDesktop.cpp</c> の <c>VirtualDesktopManager::execute()</c> を
/// 確認すると、Windows 11 以上かどうかで分岐しているのは <c>isWindows11OrLater()</c> による
/// 非対応ガード（<see cref="ExecuteError.PlatformNotSupported"/> 相当）のみで、実際に仮想
/// デスクトップを操作する経路は Windows 10 以下と全く同じ <c>switch*Fallback()</c>
/// （<c>SendInput</c> による Win+Ctrl+矢印キー等のキーシーケンス送出）1 本のみである
/// （IVirtualDesktopManager 等の COM インターフェースは非公式 API のため未実装のまま）。
/// 本クラスもこの「実際の動作」だけを移植し、COM 経由のパスは実装しない。
/// </para>
/// <para>
/// ID→メソッドの dispatch は本クラスの責務外（後続タスクの CommandExecutor.cs が担当する）。
/// C++ 版の <c>VirtualDesktopManager::execute(VirtualDesktopCommand cmd)</c> のような
/// ID による switch はここには実装せず、Windows 11 未満のガード（C++ 版では execute() 内で
/// 一括して行っていた）は本クラスの各メソッドが個別に行う。
/// </para>
/// </remarks>
public static class VirtualDesktopCommands
{
    // ---- 仮想キーコード（winuser.h）----
    // https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes
    // VK_CONTROL / VK_LEFT / VK_RIGHT / VK_F4 は NativeConstants（Interop/NativeTypes.cs）に
    // まだ定義が無いためここに private const として追加する（Win キーは既存の
    // NativeConstants.VK_LWIN を再利用）。他分類のホットキーコマンドが同じキーコードを
    // 必要とする場合は NativeConstants 側への集約を検討してよいが、本タスクの担当範囲は
    // 本ファイルのみのため、ここではファイルローカルな定義に留める。
    private const ushort VkControl = 0x11;
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;
    private const ushort VkF4 = 0x73;

    // 'D' キーには VK_ 定数が存在せず、ASCII コードがそのまま仮想キーコードとして使える
    // （0x30-0x39 = '0'-'9'、0x41-0x5A = 'A'-'Z'。移植元 C++ 版も sendKeySequence({..., 'D'}) と
    // 文字リテラルをそのまま渡している）。
    private const ushort VkD = (ushort)'D';

    /// <summary>
    /// 現在の OS が Windows 11 (ビルド 22000) 以降かどうかを判定する。
    /// 移植元: <c>VirtualDesktopManager::isWindows11OrLater()</c>
    /// （<c>VerifyVersionInfoW</c> + <c>VerSetConditionMask(VER_GREATER_EQUAL)</c> による判定）。
    /// </summary>
    /// <remarks>
    /// C# 版は Win32 の GetVersionExW/VerifyVersionInfoW を P/Invoke せず、
    /// <see cref="Environment.OSVersion"/> を使う。.NET ランタイム自身の実装
    /// （dotnet/runtime の src/libraries/System.Private.CoreLib/src/System/Environment.Windows.cs
    /// にある内部メソッド GetOSVersion()。2026-09 時点の main ブランチで実装を確認済み:
    /// https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Environment.Windows.cs）
    /// は ntdll.dll の RtlGetVersion 系 API（Interop.NtDll.RtlGetVersionEx）を直接呼び出して
    /// おり、GetVersionExW/VerifyVersionInfoW に適用される「アプリケーションマニフェストの
    /// &lt;compatibility&gt;&lt;supportedOS&gt; 宣言によるバージョン詐称シム」の対象外である。
    /// そのため本プロジェクトの app.manifest（Windows 10/11 の GUID
    /// {8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a} を宣言済み）の内容に関わらず、
    /// <c>Environment.OSVersion.Version</c> は常に実際の OS ビルド番号を返す。
    /// よって懸念されていた RtlGetVersion の直接 P/Invoke は不要と判断した。
    /// </remarks>
    public static bool IsWindows11OrLater() => IsWindows11OrLater(Environment.OSVersion.Version);

    /// <summary>
    /// <see cref="IsWindows11OrLater()"/> のテスト用オーバーロード。任意の <see cref="Version"/> を
    /// 判定できるようにし、実行環境の実際の OS バージョンに依存せず境界値をテストできるようにする。
    /// </summary>
    /// <remarks>
    /// 移植元の <c>VerSetConditionMask(..., VER_GREATER_EQUAL)</c> をメジャー・マイナー・ビルドの
    /// 3 フィールドへ個別に適用したうえで <c>VerifyVersionInfoW</c> に渡す構成は、「メジャー・
    /// マイナー・ビルドをタプルとして辞書式に比較」するのではなく「各フィールドを独立に
    /// &gt;= で比較し、すべてを満たすか（AND）」を判定する
    /// （https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-verifyversioninfow）。
    /// 実在する Windows のバージョンではメジャー・マイナーは変わらず（Windows 10 と 11 は
    /// いずれも Major=10, Minor=0）ビルド番号のみが上がるため、この AND 条件は事実上
    /// 「メジャーが 10 以上、かつビルドが 22000 以上」と同値になる（マイナーは DWORD の
    /// 非負値なので Minor &gt;= 0 は常に真であり判定に寄与しない。移植元と同じ 3 フィールド
    /// 構成を律儀に再現するより、意味のある 2 条件だけを書くほうが可読性が高いと判断し
    /// マイナーの比較は省略した）。
    /// </remarks>
    public static bool IsWindows11OrLater(Version osVersion) =>
        osVersion.Major >= 10 && osVersion.Build >= 22000;

    /// <summary>
    /// 次の仮想デスクトップへ切り替える（Win+Ctrl+→）。内部コマンド ID 116。
    /// 移植元: <c>VirtualDesktopManager::switchNextFallback()</c>。
    /// </summary>
    /// <returns>
    /// 成功時は null。Windows 11 未満の場合は <see cref="ExecuteError.PlatformNotSupported"/>。
    /// SendInput が失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? SwitchNext()
    {
        if (!IsWindows11OrLater())
        {
            return ExecuteError.PlatformNotSupported;
        }

        return SendKeySequence(NativeConstants.VK_LWIN, VkControl, VkRight);
    }

    /// <summary>
    /// 前の仮想デスクトップへ切り替える（Win+Ctrl+←）。内部コマンド ID 117。
    /// 移植元: <c>VirtualDesktopManager::switchPrevFallback()</c>。
    /// </summary>
    /// <returns>
    /// 成功時は null。Windows 11 未満の場合は <see cref="ExecuteError.PlatformNotSupported"/>。
    /// SendInput が失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? SwitchPrevious()
    {
        if (!IsWindows11OrLater())
        {
            return ExecuteError.PlatformNotSupported;
        }

        return SendKeySequence(NativeConstants.VK_LWIN, VkControl, VkLeft);
    }

    /// <summary>
    /// 新しい仮想デスクトップを作成する（Win+Ctrl+D）。内部コマンド ID 118。
    /// 移植元: <c>VirtualDesktopManager::createNewFallback()</c>。
    /// </summary>
    /// <returns>
    /// 成功時は null。Windows 11 未満の場合は <see cref="ExecuteError.PlatformNotSupported"/>。
    /// SendInput が失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? CreateNew()
    {
        if (!IsWindows11OrLater())
        {
            return ExecuteError.PlatformNotSupported;
        }

        return SendKeySequence(NativeConstants.VK_LWIN, VkControl, VkD);
    }

    /// <summary>
    /// 現在の仮想デスクトップを閉じる（Win+Ctrl+F4）。内部コマンド ID 119。
    /// 移植元: <c>VirtualDesktopManager::closeCurrentFallback()</c>。
    /// </summary>
    /// <returns>
    /// 成功時は null。Windows 11 未満の場合は <see cref="ExecuteError.PlatformNotSupported"/>。
    /// SendInput が失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? CloseCurrent()
    {
        if (!IsWindows11OrLater())
        {
            return ExecuteError.PlatformNotSupported;
        }

        return SendKeySequence(NativeConstants.VK_LWIN, VkControl, VkF4);
    }

    /// <summary>
    /// 複数の仮想キーを「先頭から順にキーダウン→末尾から逆順にキーアップ」の 1 回の
    /// SendInput 呼び出しで送出する。移植元: 匿名名前空間内の <c>sendKeySequence()</c>。
    /// </summary>
    /// <remarks>
    /// 移植元 C++ 版は SendInput の戻り値（実際に挿入されたイベント数）を確認せず常に成功として
    /// 扱う（<c>sendKeySequence</c> の戻り値は void、呼び出し元の <c>*Fallback()</c> はそのまま
    /// <c>return {};</c> で成功を返している）。本 C# 版は、同じく SendInput を使う
    /// <c>MediaCommands.SendMediaKey</c>（移植元 MediaCommands.cpp の sendMediaKey も同様に
    /// 戻り値を確認し ApiCallFailed を返す設計）と挙動を揃え、戻り値 0（1 件も挿入できなかった。
    /// 代表的な原因は UIPI によるブロック）を <see cref="ExecuteError.ApiCallFailed"/> として
    /// 呼び出し元に伝える。これは移植元からの意図的な差分であり、忠実な 1:1 移植ではない
    /// （呼び出し元にはこの差分を報告済み）。
    /// https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput
    /// </remarks>
    private static ExecuteError? SendKeySequence(params ushort[] keys)
    {
        var inputs = new INPUT[keys.Length * 2];

        int i = 0;
        foreach (ushort vk in keys)
        {
            inputs[i++] = new INPUT
            {
                Type = NativeConstants.INPUT_KEYBOARD,
                U = new InputUnion { Ki = new KEYBDINPUT { Vk = vk } },
            };
        }

        // 押下と逆順で離す（移植元 sendKeySequence() と同じ。修飾キーを正しく chording するため）。
        for (int j = keys.Length - 1; j >= 0; j--)
        {
            inputs[i++] = new INPUT
            {
                Type = NativeConstants.INPUT_KEYBOARD,
                U = new InputUnion { Ki = new KEYBDINPUT { Vk = keys[j], Flags = NativeConstants.KEYEVENTF_KEYUP } },
            };
        }

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == 0 ? ExecuteError.ApiCallFailed : null;
    }
}
