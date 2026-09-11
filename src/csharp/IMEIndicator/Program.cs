// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.App;
using IMEIndicator.Services;

namespace IMEIndicator;

/// <summary>
/// エントリポイント。移植元: src/cpp/app/main.cpp（wWinMain）+ EntryPoint_PowerToggle.cpp。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // DPI awareness は最初に設定する（移植元 wWinMain も /powertoggle 分岐より前に設定）。
        // app.manifest からは意図的に外してある（T003: WinForms SDK アナライザー WFO0003 のため）。
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        if (args.Length >= 1 && string.Equals(args[0], "/powertoggle", StringComparison.OrdinalIgnoreCase))
        {
            RunPowerToggleEntry();
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, AppConstants.SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // 二重起動は静かに終了（移植元と同じ）。
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var app = new App.App();
        app.Initialize();
        Application.Run(app);
    }

    /// <summary>
    /// <c>/powertoggle</c> 引数の処理。移植元 <c>runPowerToggleEntry()</c> と同じ 2 段構成:
    /// (1) 常駐インスタンスへ Event でトグル要求を送る。(2) 送れなければ（常駐が無ければ）
    /// その場で電源モードをトグルする。<b>いずれの経路も通知（バルーン等）は行わない</b>
    /// （移植元コメントに反し実際には未配線のまま出荷されている挙動をそのまま踏襲する。
    /// tasks.md T042 の訂正記録を参照）。
    /// </summary>
    private static void RunPowerToggleEntry()
    {
        if (PowerToggleIpc.TrySignalExisting())
        {
            return;
        }

        PowerModeService.Toggle();
    }
}
