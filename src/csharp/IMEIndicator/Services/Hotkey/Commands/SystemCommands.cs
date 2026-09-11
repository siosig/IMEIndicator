// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: C# ポート。
// namespace の変更、戻り値を IMEIndicator.Services.Hotkey.ExecuteError? へ統合)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using IMEIndicator.Interop;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// 「その他システム」系の内部コマンド実装（内部コマンド ID 6・12・22・24・61・66・70・85〜88・
/// 95〜99・104〜105・109・113〜114、全 21 種。contracts/internal-command-catalog.md「システム（21）」）。
/// 移植元: src/cpp/services/hotkey/commands/SystemCommands.h / .cpp。
/// </summary>
/// <remarks>
/// <para>
/// 【表示名との食い違いに関する重要な注意】このカテゴリは HotkeyP 由来の表示名
/// （<see cref="CommandCatalog"/> の Label）と実際の動作が 21 件中 20 件（ID 12 以外の全て）で
/// 食い違っている（internal-command-catalog.md「システム（21）」に ⚠ 表記あり。同文書の集計
/// セクションは「システム 17」と記載しているが、実際の表の ⚠ 件数は 20 件であり、本コメントは
/// 実際に SystemCommands.cpp を読んで確認した件数を記載する）。
/// 例: ID 6 の表示名は「電源: スクリーンセーバー起動」だが実際は <see cref="OpenTaskManager"/>
/// （タスクマネージャー起動）、ID 95 の表示名は「ジョイスティック無効化」だが実際は
/// <c>explorer.exe</c> の起動のみ。本クラスは表示名を一切参照せず、現行 C++ 版
/// <c>SystemCommands.cpp</c> の実装をそのまま踏襲する（是正はこの移植の範囲外。
/// これは実装のバグではなく、現在ユーザーが実際に使っている C++ 版の実際の動作である）。
/// </para>
/// <para>
/// <see cref="Execute"/> は C++ 版の <c>executeSystemCommand(int cmdId, std::wstring_view param)</c>
/// の 1:1 移植で、<c>CommandExecutor.cpp</c>「システムコマンド」switch
/// （case 6・12・22・24・50〜57・61・66・70・85〜89・91・95〜99・103〜107・109・113・114）から
/// 実際に呼び出される dispatcher である（他分類の一部に見られる未使用のデッドコードではない）。
/// このうち 50〜57・89・91・103・106・107 は dispatch はされるが <c>executeSystemCommand</c> 内に
/// 対応する case が無いため、C++ 版・C# 版いずれも default 分岐（<see cref="Execute"/> の
/// <c>ExecuteError.InvalidCommand</c>）に落ちる（internal-command-catalog.md
/// 「実行経路を持たない 23 種」参照。CommandCatalog.All にもこれらの ID は含まれない）。
/// ID→メソッドの実際の振り分け（CommandExecutor.cs から本クラスの個別メソッドを直接呼ぶか、
/// 本メソッドを 1 箇所だけ呼ぶか）は後続タスクの CommandExecutor.cs の設計による。
/// </para>
/// </remarks>
public static class SystemCommands
{
    // ---- MessageBeep（winuser.h）----
    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-messagebeep
    private const uint MbOk = 0x00000000;

    /// <summary>
    /// タスクマネージャーを開く（<c>openTaskManager()</c> 相当）。内部コマンド ID 6。
    /// ⚠ 表示名は「電源: スクリーンセーバー起動」だが、実装はタスクマネージャー起動
    /// （internal-command-catalog.md 182 行目）。
    /// </summary>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? OpenTaskManager() => ShellOpen("taskmgr.exe");

    /// <summary>
    /// コントロールパネルを開く（<c>openControlPanel()</c> 相当）。内部コマンド ID 12。
    /// このカテゴリでは唯一、表示名（「システム: コントロールパネル」）と実装が一致する ID。
    /// </summary>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? OpenControlPanel() => ShellOpen("control.exe");

    /// <summary>
    /// Windows の設定アプリを指定ページで開く（<c>openSettings()</c> 相当）。
    /// 内部コマンド ID 70・85・86・87・88・104・105・113・114 が使用する。
    /// ⚠ これらはいずれも表示名と実装が食い違う（internal-command-catalog.md 188〜202 行目）。
    /// 例: ID 85 の表示名は「テキスト表示ポップアップ」だが実装は <c>ms-settings:display</c> を開く。
    /// </summary>
    /// <param name="page"><c>ms-settings:</c> URI スキームのページ名（例: <c>"display"</c>）。
    /// 空文字なら設定アプリのトップページ（<c>ms-settings:</c> のみ）を開く
    /// （C++ 版 openSettings() と同じく、空文字でも InvalidParam にはしない）。</param>
    /// <returns>成功時 <c>null</c>、失敗時 <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? OpenSettings(string page)
    {
        string uri = "ms-settings:" + page;
        return ShellOpen(uri);
    }

    /// <summary>
    /// サウンドを再生する（<c>playSoundFile()</c> 相当）。内部コマンド ID 22。
    /// ⚠ 表示名は「システム: ごみ箱を空にする」だが、実装はサウンド再生
    /// （internal-command-catalog.md 184 行目）。
    /// </summary>
    /// <param name="path">再生する WAV ファイルのパス。空文字なら既定のビープ音
    /// （<c>MessageBeep(MB_OK)</c>）を鳴らす。</param>
    /// <returns>
    /// 成功時 <c>null</c>（空パスの場合は <c>MessageBeep</c> の戻り値を確認せず常に成功。
    /// C++ 版 playSoundFile() と同じ）。ファイル再生に失敗した場合は
    /// <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? PlaySoundFile(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            // C++ 版: MessageBeep(MB_OK) を呼ぶのみで戻り値は確認しない（常に成功扱い）。
            NativeMethods.MessageBeep(MbOk);
            return null;
        }

        try
        {
            // PlaySoundW(path, nullptr, SND_FILENAME | SND_ASYNC | SND_NODEFAULT) 相当。
            // Load() でファイルを同期的に検証してから（不正パス・不正な WAV 形式はここで例外になる。
            // PlaySoundW もファイルを開けない場合は非同期再生を開始する前に同期的に FALSE を返すため、
            // 「検証は同期・再生は非同期」という挙動を Load()+Play() の 2 段階で再現する）、
            // Play() で非同期再生する（SND_ASYNC と同じく呼び出し元をブロックしない）。
            using var player = new SoundPlayer(path);
            player.Load();
            player.Play();
            return null;
        }
        catch (Exception)
        {
            // SoundPlayer.Load/Play が投げうる例外（ファイル不在・不正な WAV 形式・共有違反等）を
            // 種別を問わず ApiCallFailed として扱う。C++ 版 PlaySoundW は失敗理由を問わず
            // bool（成功/失敗）のみを返す契約であり、本メソッドもその契約に合わせて例外を外へ
            // 漏らさない（CommandExecutor.cpp の fromSystemError: SystemCmdError::ApiCallFailed
            // → ExecuteError::ApiCallFailed と同じ最終エラー種別）。
            return ExecuteError.ApiCallFailed;
        }
    }

    /// <summary>
    /// URL を既定のブラウザで開く（<c>openUrl()</c> 相当）。内部コマンド ID 24。
    /// ⚠ 表示名は「ディスク空き容量表示」だが、実装は URL オープン
    /// （internal-command-catalog.md 185 行目）。
    /// </summary>
    /// <param name="url">開く URL。</param>
    /// <returns>
    /// 成功時 <c>null</c>。<paramref name="url"/> が空文字の場合は
    /// <see cref="ExecuteError.InvalidCommand"/>（C++ 版 <c>SystemCmdError::InvalidParam</c> の
    /// 変換先。CommandExecutor.cpp の <c>fromSystemError</c> 参照）。起動に失敗した場合は
    /// <see cref="ExecuteError.ApiCallFailed"/>。
    /// </returns>
    public static ExecuteError? OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return ExecuteError.InvalidCommand;
        }

        return ShellOpen(url);
    }

    /// <summary>
    /// クリップボードを空にする（<c>clearClipboard()</c> 相当）。内部コマンド ID 61。
    /// ⚠ 表示名は「マルチコマンド」だが、実装はクリップボードクリア
    /// （internal-command-catalog.md 186 行目）。
    /// </summary>
    /// <returns>成功時 <c>null</c>、<c>OpenClipboard</c> に失敗した場合は
    /// <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? ClearClipboard()
    {
        if (!NativeMethods.OpenClipboard(nint.Zero))
        {
            return ExecuteError.ApiCallFailed;
        }

        // C++ 版と同じく EmptyClipboard/CloseClipboard の戻り値は確認しない。
        NativeMethods.EmptyClipboard();
        NativeMethods.CloseClipboard();
        return null;
    }

    /// <summary>
    /// スクリーンショットをクリップボードにコピーする（<c>captureScreen()</c> 相当）。
    /// 内部コマンド ID 66。⚠ 表示名は「システム: ウィンドウ情報表示」だが、実装は
    /// スクリーンショット取得（internal-command-catalog.md 187 行目）。
    /// </summary>
    /// <remarks>
    /// C++ 版はデスクトップを GDI で直接キャプチャするのではなく、PrintScreen キー
    /// （<c>VK_SNAPSHOT</c>）を <c>SendInput</c> で 2 件（キーダウン→キーアップ）送出し、
    /// OS 標準の「画面全体をクリップボードへコピーする」動作に委ねている
    /// （SystemCommands.cpp captureScreen() 参照）。C# 版も同じ方式を採用し、
    /// <c>GetDC</c>/<c>BitBlt</c> や <see cref="System.Drawing.Graphics.CopyFromScreen"/> による
    /// 独自の GDI キャプチャは使わない。理由: (1) 本移植は現行 C++ 版との動作 1:1 が最優先方針
    /// （internal-command-catalog.md「概要」）であり、実装の「結果」ではなく「手段」を合わせる
    /// ことで挙動の差異（マルチモニタ・DPI・保護コンテンツの扱い等）を避けられる。
    /// (2) Windows 10 1809 以降の「PrintScreen キーで画面キャプチャ（Snipping）を開く」設定
    /// （設定 &gt; アクセシビリティ &gt; キーボード）が有効な環境では、PrintScreen 送出により
    /// Snipping Tool のオーバーレイが開き即座にはクリップボードへコピーされないことがあるが、
    /// これは現行ユーザーが実際に使っている C++ 版と同一の体験になるため許容する。
    /// </remarks>
    /// <returns>成功時 <c>null</c>、<c>SendInput</c> に失敗した場合は
    /// <see cref="ExecuteError.ApiCallFailed"/>。</returns>
    public static ExecuteError? CaptureScreen()
    {
        ushort snapshotKey = (ushort)NativeConstants.VK_SNAPSHOT;

        INPUT[] inputs = new INPUT[2];
        inputs[0] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            U = new InputUnion { Ki = new KEYBDINPUT { Vk = snapshotKey } },
        };
        inputs[1] = new INPUT
        {
            Type = NativeConstants.INPUT_KEYBOARD,
            U = new InputUnion { Ki = new KEYBDINPUT { Vk = snapshotKey, Flags = NativeConstants.KEYEVENTF_KEYUP } },
        };

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == 0 ? ExecuteError.ApiCallFailed : null;
    }

    /// <summary>
    /// 指定したターゲット（実行ファイル名・フルパス・URL・URI スキームなど）をシェルで開く。
    /// C++ 版では <c>SystemCommands.cpp</c> 内の無名名前空間に閉じたヘルパー関数
    /// <c>shellOpen(target, params)</c>（= <c>ShellExecuteW(nullptr, L"open", target, params,
    /// nullptr, SW_SHOWNORMAL)</c>）だが、C# 版では本クラスの他メソッド（<see cref="OpenTaskManager"/>
    /// 等）からも使う共通プリミティブとして、設計方針どおり公開メソッドに昇格させている。
    /// 内部コマンド ID 95〜99・109 はこのメソッドを直接使用する。
    /// </summary>
    /// <param name="target">開く対象。実行ファイル名のみ（PATH 解決）・フルパス・URL・
    /// <c>ms-settings:</c> のような URI スキームのいずれでも可
    /// （<see cref="ProcessStartInfo.UseShellExecute"/> = <c>true</c> により ShellExecute 相当の
    /// 解決を行う）。</param>
    /// <returns>
    /// 成功時 <c>null</c>、起動に失敗した場合は <see cref="ExecuteError.ApiCallFailed"/>
    /// （C++ 版 <c>SystemCmdError::ApiCallFailed</c> → <c>ExecuteError::ApiCallFailed</c> と同じ
    /// 最終エラー種別。<see cref="ExecuteError.ProcessLaunchFailed"/> ではないことに注意。
    /// CommandExecutor.cpp の <c>fromSystemError</c> 参照）。
    /// </returns>
    public static ExecuteError? ShellOpen(string target)
    {
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            Verb = "open",
        };

        try
        {
            // UseShellExecute = true では、既存プロセスを再利用するケース（既定ブラウザが URL を
            // 既存タブで開く等）で戻り値が null になることがあるが、これは失敗ではない。
            // 例外が発生しなければ起動要求自体は成功しているとみなす（本メソッドは起動した
            // プロセスの終了を待つ必要がないため、直後に破棄してハンドルを解放する）。
            // https://learn.microsoft.com/dotnet/api/system.diagnostics.process.start
            using var process = Process.Start(startInfo);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Win32Exception: ShellExecute 相当の起動に失敗（ファイルが見つからない、
            // 関連付けアプリが無い等）。InvalidOperationException: target が空文字などで
            // ファイル名が指定されていない場合。C++ 版 shellOpen() の ShellExecuteW 失敗
            // （戻り値 <= 32）と同じ扱いにする。
            return ExecuteError.ApiCallFailed;
        }
    }

    /// <summary>
    /// システムコマンドを ID で実行する。C++ 版 <c>SystemCommands.cpp</c>
    /// <c>executeSystemCommand(int cmdId, std::wstring_view param)</c> の 1:1 移植であり、
    /// <c>CommandExecutor.cpp</c>「システムコマンド」switch から実際に呼び出される dispatcher
    /// である（<see cref="MouseCommands.Execute"/> と同様、他分類に見られる未使用のデッドコードではない）。
    /// </summary>
    /// <param name="cmdId">コマンド ID。</param>
    /// <param name="param">コマンド引数。引数を取らないコマンド（例: ID 6・12・61・66 等）では無視される。</param>
    /// <returns>成功時 <c>null</c>、失敗時または本カテゴリで未対応の ID の場合は <see cref="ExecuteError"/>
    /// （未対応 ID は C++ 版 <c>executeSystemCommand</c> の <c>default</c> 分岐と同じく
    /// <see cref="ExecuteError.InvalidCommand"/>）。</returns>
    public static ExecuteError? Execute(int cmdId, string param) => cmdId switch
    {
        6 => OpenTaskManager(),
        12 => OpenControlPanel(),
        22 => PlaySoundFile(param),
        24 => OpenUrl(param),
        61 => ClearClipboard(),
        66 => CaptureScreen(),
        70 => OpenSettings(param),
        85 => OpenSettings("display"),
        86 => OpenSettings("sound"),
        87 => OpenSettings("personalization"),
        88 => OpenSettings("network-status"),
        95 => ShellOpen("explorer.exe"),
        96 => ShellOpen("notepad.exe"),
        97 => ShellOpen("calc.exe"),
        98 => ShellOpen("mspaint.exe"),
        99 => ShellOpen("cmd.exe"),
        104 => OpenSettings("windowsupdate"),
        105 => OpenSettings("appsfeatures"),
        // ID 109: param が空なら既定で explorer.exe を開く（C++ 版 executeSystemCommand の
        // case 109 と同じ分岐。ShellOpen 自体には空文字フォールバックを持たせない設計）。
        109 => ShellOpen(string.IsNullOrEmpty(param) ? "explorer.exe" : param),
        113 => OpenSettings("printers"),
        114 => OpenSettings("bluetooth"),
        _ => ExecuteError.InvalidCommand,
    };
}
