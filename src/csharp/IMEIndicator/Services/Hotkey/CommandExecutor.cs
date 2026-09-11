// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Diagnostics;
using IMEIndicator.Interop;
using IMEIndicator.Services.Hotkey.Commands;

namespace IMEIndicator.Services.Hotkey;

/// <summary>
/// 内部コマンド ID 0〜120（HotkeyP 由来）・200〜299（IMEIndicator 拡張）の実行ディスパッチ。
/// 移植元: src/cpp/services/hotkey/CommandExecutor.h / .cpp。
/// </summary>
/// <remarks>
/// <para>
/// C++ 版の <c>executeCommandById</c> は複数の switch 文を順に試す構造（後段の switch にある
/// case でも前段で既にマッチして return 済みなら実際には到達しない）だが、ID の範囲は実際には
/// 重複しないため、本クラスは 1 つの switch 式で ID → 各コマンド分類クラスのメソッドへ直接
/// 対応付ける（挙動は同じで、内部構造だけを単純化している）。対応表は
/// <c>CommandExecutor.cpp</c> の <c>executeCommandById</c> を実際に読み、以下の既知の罠を
/// 踏まえて構築した:
/// <list type="bullet">
/// <item>ID 19（表示名は「モニター電源オフ」）は電源コマンドとして <see cref="PowerCommands.TurnOffMonitor"/>
/// が処理する。<c>DisplayCommands.cpp</c> にも同じ ID の case があるが、C++ 版で既に電源側が
/// 先に return するため到達しないデッドコードであり、本クラスも <see cref="DisplayCommands"/> 側へは
/// dispatch しない。</item>
/// <item>ID 38（表示名は「シャットダウンダイアログ」）は ID 2 と同じ <see cref="PowerCommands.Shutdown"/>
/// を呼ぶ（ダイアログは出ない）。</item>
/// <item>ID 15/16/58 は「Wave」系の表示名だが実装は 13/14/17 と同じ Core Audio API を共用する。</item>
/// <item>ID 29〜36（画面端配置スナップ）は <c>WindowCommands.cpp</c>/<c>.h</c> に対応する関数が無く、
/// <c>CommandExecutor.cpp</c> 自身が直接処理する（<see cref="SnapToRegion"/> として同じロジックを移植）。</item>
/// <item>ID 65/102/115（表示名がそれぞれ異なる）はいずれも <see cref="WindowCommands.MinimizeToTray"/>
/// （フォアグラウンドウィンドウではなく <see cref="MainWindowHandle"/>、すなわち IMEIndicator 自身の
/// ウィンドウが対象）を呼ぶ。</item>
/// <item>ID 46〜49 は表示名（Idle→Realtime の順）と実際に設定される優先度クラスが逆転している
/// （46→Realtime、47→High、48→Normal、49→Idle）。59/60 は表示名が優先度設定だが実際は
/// <see cref="ProcessCommands.LaunchApp"/>（59 のみ管理者権限）。</item>
/// <item>ID 62（メディア）・92/93（プロセス）は外側の dispatch には存在するが、対応する分類クラスの
/// 内部实装に case が無く実質無効（<see cref="CommandCatalog"/> の除外 23 種に含まれる）。本クラスの
/// switch にも case を設けず、共通の default（<see cref="ExecuteError.InvalidCommand"/>）に委ねる
/// （C++ 版では分類によって最終的なエラー種別が ApiCallFailed/InvalidCommand とまちまちだが、
/// UI からはこれらの ID を選択できず観測可能な差が実質無いため、本移植では一律 InvalidCommand に
/// 統一する）。</item>
/// <item>ID 6・12・22・24・50〜57・61・66・70・85〜89・91・95〜99・103〜107・109・113・114
/// （「システム」分類全体）と ID 37・43〜45・75・76・79・80・108（「マウス」分類全体）は、
/// <see cref="SystemCommands.Execute"/> / <see cref="MouseCommands.Execute"/> が C++ 版の
/// <c>executeSystemCommand</c>/<c>executeMouseCommand</c>（実際に呼ばれる本物の dispatcher）の
/// 1:1 移植であるため、本クラスはそれぞれへ丸ごと委譲する（実行経路の無い ID の扱いも
/// それぞれの内部 default に委ねる）。</item>
/// </list>
/// </para>
/// </remarks>
public static class CommandExecutor
{
    /// <summary>
    /// 内部コマンド（ウィンドウ操作系）がホットキー押下元アプリではなく「自アプリ（IMEIndicator）
    /// のウィンドウ」を対象とする場合に使うハンドル（ID 65/102/115 の
    /// <see cref="WindowCommands.MinimizeToTray"/> 専用）。App.Initialize から設定する想定
    /// （移植元 <c>setMainWindowHandle</c>）。既定値 0（未設定）。
    /// </summary>
    public static nint MainWindowHandle { get; set; }

    /// <summary>
    /// コマンド ID の有効性チェック（HotkeyP 由来 [0, 120] + IMEIndicator 拡張 [200, 299]）。
    /// 121〜199 は予約レンジで無効扱い。300〜 は未定義。移植元 <c>isValidCommandId</c>。
    /// </summary>
    public static bool IsValidCommandId(int id) => (id >= 0 && id <= 120) || (id >= 200 && id <= 299);

    /// <summary>
    /// 内部コマンドを実行する。移植元 <c>executeCommandById</c>。例外は一切外へ投げない
    /// （ホットキー検出コールバックの延長で呼ばれるため、想定外の例外で常駐プロセス全体を
    /// 落とさないための最終防衛線。個々のコマンド分類クラスは基本的に自分自身で例外を処理するが、
    /// ここでも二重に保険をかける）。
    /// </summary>
    /// <param name="id">内部コマンド ID。</param>
    /// <param name="param">コマンドパラメータ文字列（コマンドによって意味が異なる）。</param>
    /// <returns>成功時 null、失敗時 <see cref="ExecuteError"/>。</returns>
    public static ExecuteError? Execute(int id, string param)
    {
        try
        {
            return ExecuteCore(id, param ?? string.Empty);
        }
        catch (Exception ex)
        {
            Log.Command.Error(ex, "CommandExecutor.Execute: 予期しない例外: id={Id}", id);
            return ExecuteError.ApiCallFailed;
        }
    }

    private static ExecuteError? ExecuteCore(int id, string param)
    {
        if (!IsValidCommandId(id))
        {
            Log.Command.Warning("CommandExecutor.Execute: 範囲外の ID: {Id}", id);
            return ExecuteError.InvalidCommand;
        }

        // IMEIndicator 拡張コマンド（200〜299）はまるごと委譲する（C++ 版と同じ構造）。
        if (id is >= 200 and <= 299)
        {
            return ImeIndicatorCommands.Execute(id, param);
        }

        // ウィンドウスナップ（29〜36）: WindowCommands.h/.cpp に対応関数が無く、
        // C++ 版 CommandExecutor.cpp 自身が直接処理する共通ロジック。
        if (id is >= 29 and <= 36)
        {
            return SnapToRegion(id);
        }

        return id switch
        {
            // ---- 音量 ----
            13 => VolumeCommands.AdjustVolume(0.05f),
            14 => VolumeCommands.AdjustVolume(-0.05f),
            15 => VolumeCommands.AdjustVolume(0.05f), // 表示名「Wave 音量 +」だが実装は 13 と同一
            16 => VolumeCommands.AdjustVolume(-0.05f), // 表示名「Wave 音量 -」だが実装は 14 と同一
            17 => VolumeCommands.ToggleMute(),
            58 => VolumeCommands.ToggleMute(), // 表示名「Wave ミュート」だが実装は 17 と同一
            78 => VolumeCommands.ExecuteVolumeCommand(param),

            // ---- 電源（ID 19 はここで処理。DisplayCommands 側の同一 ID は到達しないデッドコード） ----
            2 => PowerCommands.Shutdown(),
            3 => PowerCommands.Restart(),
            4 => PowerCommands.Sleep(),
            5 => PowerCommands.LogOff(),
            19 => PowerCommands.TurnOffMonitor(),
            38 => PowerCommands.Shutdown(), // 表示名「シャットダウンダイアログ」だがダイアログは出ない
            63 => PowerCommands.LockWorkstation(),
            64 => PowerCommands.Hibernate(),

            // ---- ウィンドウ ----
            7 => WindowCommands.MaximizeActiveWindow(),
            8 => WindowCommands.MinimizeActiveWindow(),
            9 => WindowCommands.CloseActiveWindow(),
            10 => WindowCommands.ToggleAlwaysOnTop(),
            25 => WindowCommands.ToggleAlwaysOnTop(), // 表示名「非表示」だが実装は 10 と同一
            28 => WindowCommands.CenterWindow(),
            65 => WindowCommands.MinimizeToTray(MainWindowHandle), // 表示名「他のウィンドウを最小化」だが自アプリが対象
            77 => WindowCommands.SetOpacity(ParseOpacityParam(param)),
            81 => WindowCommands.ShowDesktop(),
            83 => WindowCommands.SwitchToNextWindow(), // 表示名「前のタスク」だが実装は 84 と同一
            84 => WindowCommands.SwitchToNextWindow(),
            90 => WindowCommands.CenterWindow(), // 表示名「ウィンドウスクリーンショット」だが実装は 28 と同一
            101 => WindowCommands.MinimizeActiveWindow(), // 表示名「アプリ非表示」だが実装は単なる最小化
            102 => WindowCommands.MinimizeToTray(MainWindowHandle),
            110 => WindowCommands.AdjustForegroundOpacityBy(-10), // 表示名「不透明度 +」だが alpha は減る
            111 => WindowCommands.AdjustForegroundOpacityBy(10), // 表示名「不透明度 -」だが alpha は増える
            112 => WindowCommands.MaximizeAll(),
            115 => WindowCommands.MinimizeToTray(MainWindowHandle),

            // ---- マウス（executeMouseCommand の 1:1 移植へ丸ごと委譲） ----
            37 or 43 or 44 or 45 or 75 or 76 or 79 or 80 or 108 => MouseCommands.Execute(id),

            // ---- メディア（62 は除外 23 種の 1 つ。case を設けず default へ） ----
            0 => MediaCommands.EjectCD(param),
            1 => MediaCommands.CloseCD(param),
            39 => MediaCommands.PlayPauseCd(),
            40 => MediaCommands.NextTrack(),
            41 => MediaCommands.Stop(),
            42 => MediaCommands.PrevTrack(),
            100 => MediaCommands.EjectCD(param), // 表示名「CD 読み込み速度」だが実装は 0 と同一
            120 => MediaCommands.PlayPause(),

            // ---- テキスト・マクロ ----
            26 => TextCommands.ExecuteMacro(param),
            27 => TextCommands.ExecuteMacro(param),
            67 => TextCommands.PasteText(param),
            74 => TextCommands.ExecuteMacro(param),
            94 => TextCommands.SetClipboardText(param), // 表示名「マクロ送信」だが実装はクリップボード設定のみ

            // ---- プロセス（46〜49 は表示名と優先度クラスが逆転。59/60 は表示名が優先度設定だが実際はアプリ起動） ----
            11 => ProcessCommands.KillForegroundProcess(),
            20 => ProcessCommands.LaunchApp(param, string.Empty, string.Empty, asAdmin: false),
            46 => ProcessCommands.SetForegroundProcessPriority(ProcessPriorityClass.RealTime),
            47 => ProcessCommands.SetForegroundProcessPriority(ProcessPriorityClass.High),
            48 => ProcessCommands.SetForegroundProcessPriority(ProcessPriorityClass.Normal),
            49 => ProcessCommands.SetForegroundProcessPriority(ProcessPriorityClass.Idle),
            59 => ProcessCommands.LaunchApp(param, string.Empty, string.Empty, asAdmin: true),
            60 => ProcessCommands.LaunchApp(param, string.Empty, string.Empty, asAdmin: false),

            // ---- ディスプレイ（19 は電源側で処理済みのためここには含めない） ----
            18 => DisplayCommands.RotateDisplay(90),
            23 => DisplayCommands.RotateDisplay(180),

            // ---- その他システム（executeSystemCommand の 1:1 移植へ丸ごと委譲。除外 ID も内部で吸収） ----
            6 or 12 or 22 or 24 or 50 or 51 or 52 or 53 or 54 or 55 or 56 or 57 or 61 or 66 or 70
                or 85 or 86 or 87 or 88 or 89 or 91 or 95 or 96 or 97 or 98 or 99 or 103 or 104
                or 105 or 106 or 107 or 109 or 113 or 114 => SystemCommands.Execute(id, param),

            // ---- Windows 11 仮想デスクトップ ----
            116 => VirtualDesktopCommands.SwitchNext(),
            117 => VirtualDesktopCommands.SwitchPrevious(),
            118 => VirtualDesktopCommands.CreateNew(),
            119 => VirtualDesktopCommands.CloseCurrent(),

            // 実行経路の無い ID（92/93 等）、および予約レンジ内の未使用 ID。
            _ => LogAndReturnInvalid(id),
        };
    }

    private static ExecuteError? LogAndReturnInvalid(int id)
    {
        Log.Command.Warning("CommandExecutor.Execute: 実行経路の無い ID: {Id}", id);
        return ExecuteError.InvalidCommand;
    }

    // ID 77（ウィンドウ不透明度設定）の param 解析。移植元 CommandExecutor.cpp case 77:
    // 「param を整数として解釈、空または解析失敗時は既定値 128」。VolumeCommands.TryParseLeadingInt
    // と同じ std::stoi 互換の「先頭の整数」解析（末尾の非数字は無視）を再利用する。
    private static int ParseOpacityParam(string param)
    {
        if (string.IsNullOrEmpty(param))
        {
            return 128;
        }

        return VolumeCommands.TryParseLeadingInt(param, out int value) ? value : 128;
    }

    // ウィンドウスナップ（ID 29〜36）。移植元 CommandExecutor.cpp executeCommandById 内、
    // WindowCommands.h/.cpp に対応関数を持たないインライン実装をそのまま移植。
    private static ExecuteError? SnapToRegion(int id)
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return ExecuteError.ApiCallFailed;
        }

        nint hmon = NativeMethods.MonitorFromWindow(hwnd, NativeConstants.MONITOR_DEFAULTTONEAREST);
        MONITORINFOEXW mi = MONITORINFOEXW.Create();
        if (!NativeMethods.GetMonitorInfoW(hmon, ref mi))
        {
            return ExecuteError.ApiCallFailed;
        }

        int w = mi.RcWork.Width;
        int h = mi.RcWork.Height;
        int halfW = w / 2;
        int halfH = h / 2;
        int left = mi.RcWork.Left;
        int top = mi.RcWork.Top;

        int x = left;
        int y = top;
        int sw = w;
        int sh = h;

        switch (id)
        {
            case 29: x = left; y = top + halfH; sw = halfW; sh = halfH; break; // 左下
            case 30: x = left; y = top + halfH; sw = w; sh = halfH; break; // 下
            case 31: x = left + halfW; y = top + halfH; sw = halfW; sh = halfH; break; // 右下
            case 32: x = left; y = top; sw = halfW; sh = h; break; // 左
            case 33: x = left + halfW; y = top; sw = halfW; sh = h; break; // 右
            case 34: x = left; y = top; sw = halfW; sh = halfH; break; // 左上
            case 35: x = left; y = top; sw = w; sh = halfH; break; // 上
            case 36: x = left + halfW; y = top; sw = halfW; sh = halfH; break; // 右上
        }

        // SetWindowPos の戻り値は確認しない（C++ 版と同じ）。
        NativeMethods.SetWindowPos(hwnd, 0, x, y, sw, sh, NativeConstants.SWP_NOZORDER);
        return null;
    }
}
