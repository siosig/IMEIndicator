// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.ComponentModel;
using System.Diagnostics;
using IMEIndicator.Interop;
using IMEIndicator.Services;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// プロセス管理系の内部コマンド実装（内部コマンド ID 11 / 20 / 46〜49 / 59 / 60。
/// contracts/internal-command-catalog.md「プロセス（8）」）。
/// 移植元: src/cpp/services/hotkey/commands/ProcessCommands.h / .cpp の
/// killForegroundProcess() / launchApp() / setForegroundProcessPriority()。
///
/// ID→メソッドの dispatch は本クラスの責務外（後続タスクの CommandExecutor.cs が担当する）。
/// C++ 版の <c>executeProcessCommand(cmdId, param)</c> のような ID による switch はここには実装しない。
///
/// 表示名と実装が食い違う既知のケース（internal-command-catalog.md「プロセス（8）」の表 ⚠ 参照。
/// 是正は本フィーチャーの範囲外、C++ 版と 1:1 のまま移植する）:
/// <list type="bullet">
/// <item>ID 46〜49 は表示名（Idle→Normal→High→Realtime の順）と実際に設定される優先度クラスが
/// 逆転している。46「優先度 Idle」→ 実際は <see cref="ProcessPriorityClass.RealTime"/>、
/// 47「優先度 Normal」→ <see cref="ProcessPriorityClass.High"/>、
/// 48「優先度 High」→ <see cref="ProcessPriorityClass.Normal"/>、
/// 49「優先度 Realtime」→ <see cref="ProcessPriorityClass.Idle"/> を設定する。
/// どの ID にどの優先度クラスを割り当てるかは <see cref="SetForegroundProcessPriority"/> の
/// 呼び出し元（CommandExecutor.cs）が決める。本クラスは ID を扱わない。</item>
/// <item>ID 59「優先度 BelowNormal」・60「優先度 AboveNormal」は表示名に反し、実際は
/// <see cref="LaunchApp"/>（引数で渡されたパスのアプリ起動）。59 は管理者権限
/// （<c>asAdmin: true</c>）付き、60 は ID 20 と同じ非管理者起動。</item>
/// </list>
///
/// 内部コマンド 92「サービス停止」・93「サービス開始」は CommandExecutor.cpp の呼び出し元 switch
/// には列挙されているが、<c>executeProcessCommand</c> 内の switch には対応する case が無く、
/// default（常に <c>ProcessError::InvalidParam</c>）にしか到達しない実質無効な ID
/// （CommandCatalog.cs の ExcludedIds に含まれる）。本クラスには対応するメソッドを設けない。
/// </summary>
/// <remarks>
/// 実装方針: contracts/win32-interop-contract.md「管理 API で置き換えるもの」
/// （<c>OpenProcess</c> + <c>SetPriorityClass</c> → <see cref="Process.PriorityClass"/> 等）の
/// 方針に従い、可能な限り <see cref="System.Diagnostics.Process"/> を使う（新規の P/Invoke 宣言は
/// 追加していない。フォアグラウンドウィンドウ取得には既存の
/// <see cref="NativeMethods.GetForegroundWindow"/> / <see cref="NativeMethods.GetWindowThreadProcessId"/>
/// をそのまま使う）。
/// <list type="bullet">
/// <item><see cref="KillForegroundProcess"/>: プロセス終了は <see cref="Process.Kill()"/>
/// （内部的に TerminateProcess 相当）を使う。C++ 版は <c>OpenProcess</c> 失敗を常に
/// <c>ProcessError::AccessDenied</c> として扱うが、本メソッドは実際の例外種別で
/// 「権限不足」と「対象プロセスが既に存在しない（フォアグラウンド取得直後に終了した等の
/// 競合）」を区別する（後者を AccessDenied 扱いにすると、権限とは無関係な状況で利用者に
/// 誤った案内をしてしまうため）。この 1 点のみ、C++ 版の大雑把な分類より高精度にしている
/// （動作そのもの＝「対象プロセスを強制終了する」は 1:1）。</item>
/// <item><see cref="SetForegroundProcessPriority"/>: 優先度変更ロジックは
/// <see cref="ProcessPriorityService.SetPriority"/> を再利用する。同メソッドは成否を
/// <c>bool</c> でしか返さず「アクセス拒否」と「その他の失敗」を区別できないが、呼び出し直前に
/// 生成した <see cref="ProcessPriorityService"/> インスタンスの
/// <see cref="ProcessPriorityService.AccessDeniedCount"/>（呼び出し前は必ず 0）を失敗直後に
/// 確認することで、C++ 版 <c>fromProcessError</c>（ProcessError::AccessDenied →
/// ExecuteError.AccessDenied、それ以外 → ExecuteError.ApiCallFailed）と同じ区別を、
/// 既存ロジックを変更・複製せずに再現する。</item>
/// <item><see cref="LaunchApp"/>: C++ 版は <c>ShellExecuteExW</c> を直接呼ぶが、C# 版では
/// <see cref="Process.Start(ProcessStartInfo)"/>（<c>UseShellExecute = true</c>）を使う。
/// これは .NET が公式に提供する ShellExecuteEx の管理ラッパーであり、"runas" verb による昇格も
/// 同様にサポートする（tasks.md T062 の SystemCommands.ShellOpen でも同じ対応付けが前提と
/// されている）。UAC ダイアログでの拒否は Win32Exception(1223 = ERROR_CANCELLED) として現れる
/// （C++ 版の ERROR_ACCESS_DENIED(5) / ERROR_ELEVATION_REQUIRED(740) 判定と役割は同じだが
/// エラーコード体系が異なるため、C# 側では 5 / 740 / 1223 をまとめて
/// <see cref="ExecuteError.AccessDenied"/> として扱う）。</item>
/// </list>
/// </remarks>
public static class ProcessCommands
{
    /// <summary>
    /// フォアグラウンドウィンドウのプロセスを強制終了する（<c>killForegroundProcess()</c> 相当）。
    /// 内部コマンド ID 11。
    /// </summary>
    /// <returns>
    /// 成功時は null。フォアグラウンドウィンドウが無い、または対象プロセスが見つからない場合は
    /// <see cref="ExecuteError.ApiCallFailed"/>（C++ 版 ProcessError::ProcessNotFound 相当）。
    /// 権限不足の場合は <see cref="ExecuteError.AccessDenied"/>。
    /// </returns>
    public static ExecuteError? KillForegroundProcess()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        try
        {
            using Process process = Process.GetProcessById((int)pid);
            process.Kill();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5) // ERROR_ACCESS_DENIED
        {
            return ExecuteError.AccessDenied;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // ArgumentException: 指定 PID のプロセスがローカルに存在しない
            // （フォアグラウンド取得直後にプロセスが終了した等の競合）。
            // InvalidOperationException: Kill() 呼び出し時点で対象が既に終了している。
            // いずれも C++ 版の ProcessError::ProcessNotFound 相当として扱う。
            return ExecuteError.ApiCallFailed;
        }
    }

    /// <summary>
    /// アプリケーションを起動する（<c>launchApp()</c> 相当）。内部コマンド ID 20・60（通常起動）・
    /// 59（管理者として起動）、および <c>HotkeyService</c>（T068）のホットキー由来 exe 起動・
    /// autoStart 実行から使う。
    /// </summary>
    /// <param name="exe">起動する実行ファイルのパス。空の場合は <see cref="ExecuteError.InvalidCommand"/>。</param>
    /// <param name="args">コマンドライン引数。不要なら空文字列。</param>
    /// <param name="workDir">作業ディレクトリ。空文字列ならカレントディレクトリを使う
    /// （C++ 版が空文字列時に <c>lpDirectory</c> へ null を渡すのと同じ挙動）。</param>
    /// <param name="asAdmin">true の場合、"runas" verb で管理者として起動する（ID 59 用）。</param>
    /// <param name="cmdShow">
    /// ウィンドウ表示状態。<see cref="IMEIndicator.Models.Hotkey.HotKeyEntry.CmdShow"/> と同じ符号化
    /// （0=Normal、1=Maximized、2=Minimized）。内部コマンド ID 20/59/60 は対応する
    /// HotKeyEntry を持たないため既定値 0（Normal、C++ 版 executeProcessCommand も
    /// nShow を渡していない箇所と同じ）。範囲外の値も Normal 扱いにする。
    /// </param>
    /// <returns>
    /// 成功時は null。<paramref name="exe"/> が空の場合は <see cref="ExecuteError.InvalidCommand"/>。
    /// UAC ダイアログでの拒否・権限不足の場合は <see cref="ExecuteError.AccessDenied"/>。
    /// それ以外の起動失敗（実行ファイルが見つからない等）は <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? LaunchApp(string exe, string args, string workDir, bool asAdmin = false, int cmdShow = 0)
    {
        if (string.IsNullOrEmpty(exe))
        {
            return ExecuteError.InvalidCommand;
        }

        var startInfo = new ProcessStartInfo(exe)
        {
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = true,
            Verb = asAdmin ? "runas" : string.Empty,
            WindowStyle = cmdShow switch
            {
                1 => ProcessWindowStyle.Maximized,
                2 => ProcessWindowStyle.Minimized,
                _ => ProcessWindowStyle.Normal,
            },
        };

        try
        {
            // 起動した子プロセスは追跡しない（C++ 版も SEE_MASK_NOCLOSEPROCESS で受け取った
            // ハンドルを即 CloseHandle するだけで、以降の子プロセスの生死には関与しない）。
            Process.Start(startInfo)?.Dispose();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 5 or 740 or 1223)
        {
            // 5 = ERROR_ACCESS_DENIED, 740 = ERROR_ELEVATION_REQUIRED,
            // 1223 = ERROR_CANCELLED（"runas" の UAC ダイアログをユーザーが拒否した場合）。
            return ExecuteError.AccessDenied;
        }
        catch (Win32Exception)
        {
            // 実行ファイルが見つからない等、権限以外の理由での起動失敗。
            return ExecuteError.ApiCallFailed;
        }
    }

    /// <summary>
    /// フォアグラウンドウィンドウのプロセスの優先度クラスを設定する
    /// （<c>setForegroundProcessPriority()</c> 相当）。内部コマンド ID 46〜49。
    /// どの ID にどの <see cref="ProcessPriorityClass"/> を渡すかは呼び出し元
    /// （CommandExecutor.cs）の責務。クラス doc の ⚠ のとおり、ID の表示名と実際に設定される
    /// 優先度クラスは対応していない。
    /// </summary>
    /// <param name="priorityClass">設定する優先度クラス。</param>
    /// <returns>
    /// 成功時は null。フォアグラウンドウィンドウが無い場合は <see cref="ExecuteError.ApiCallFailed"/>。
    /// 権限不足の場合は <see cref="ExecuteError.AccessDenied"/>。
    /// </returns>
    public static ExecuteError? SetForegroundProcessPriority(ProcessPriorityClass priorityClass)
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        // ProcessPriorityService.SetPriority は bool しか返さないため、直前に生成した
        // インスタンスの AccessDeniedCount（呼び出し前は必ず 0）で失敗理由を判別する。
        var priorityService = new ProcessPriorityService();
        if (priorityService.SetPriority((int)pid, priorityClass))
        {
            return null;
        }

        return priorityService.AccessDeniedCount > 0 ? ExecuteError.AccessDenied : ExecuteError.ApiCallFailed;
    }
}
