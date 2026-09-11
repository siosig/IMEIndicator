// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace IMEIndicator.Services;

/// <summary>
/// ログ出力を統括する静的クラス。現行 C++ 版 <c>services::Logger</c>
/// （src/cpp/services/Logger.h / Logger.cpp）の移植（specs/014-port-to-csharp/research.md R-12）。
/// 出力先パス・行フォーマット・カテゴリ一覧・保持世代数はそちらを正として踏襲する。
/// </summary>
/// <remarks>
/// Serilog の静的グローバルロガー <c>global::Serilog.Log</c> とクラス名が衝突するため、
/// このクラスは <c>global::Serilog.Log</c> の静的メソッドを一切使わない。代わりに内部で
/// 保持する <see cref="ILogger"/> インスタンス（<c>_rootLogger</c>）から <c>ForContext</c> して
/// カテゴリ別ロガーを作る。
/// </remarks>
public static class Log
{
    private const string CategoryPropertyName = "Category";

    // 現行 Logger.cpp の kMaxLogFiles（ローテーション 5 世代）を踏襲する（research.md R-12）。
    // C++ 版はサイズ超過ローテーション（5MB × 5 世代）だが、移植版は T017 の指示どおり
    // 日次ローテーション（RollingInterval.Day）へ変更し、保持世代数のみ現行と揃える。
    private const int RetainedFileCountLimit = 5;

    // 現行 spdlog パターン "[%Y-%m-%d %H:%M:%S.%e] [%^%l%$] [%t] [%n] %v" に対応する Serilog 版
    // （contracts/log-file-contract.md 相当の行フォーマット）。
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:w}] [{ThreadId}] [{Category}] {Message:lj}{NewLine}{Exception}";

    // 既定はシンク無し（no-op）ロガー。Initialize / Configure が呼ばれる前に
    // カテゴリ別プロパティへアクセスされても例外を投げないための安全策。
    private static ILogger _rootLogger = new LoggerConfiguration().CreateLogger();

    /// <summary>
    /// 実行時にログレベルを切り替えるためのスイッチ。既定値は Warn
    /// （AppSettings.logLevel の既定値と同じ）。<see cref="Initialize"/> / <see cref="SetLevel"/> で更新する。
    /// </summary>
    public static LoggingLevelSwitch LevelSwitch { get; } = new(ToSerilogLevel(LogLevel.Warn));

    public static ILogger App => _rootLogger.ForContext(CategoryPropertyName, "app");
    public static ILogger Ime => _rootLogger.ForContext(CategoryPropertyName, "ime");
    public static ILogger Pixel => _rootLogger.ForContext(CategoryPropertyName, "pixel");
    public static ILogger Priority => _rootLogger.ForContext(CategoryPropertyName, "priority");
    public static ILogger Power => _rootLogger.ForContext(CategoryPropertyName, "power");
    public static ILogger Display => _rootLogger.ForContext(CategoryPropertyName, "display");
    public static ILogger Tray => _rootLogger.ForContext(CategoryPropertyName, "tray");
    public static ILogger Settings => _rootLogger.ForContext(CategoryPropertyName, "settings");
    public static ILogger Hotkey => _rootLogger.ForContext(CategoryPropertyName, "hotkey");
    public static ILogger Hook => _rootLogger.ForContext(CategoryPropertyName, "hook");
    public static ILogger Command => _rootLogger.ForContext(CategoryPropertyName, "command");
    public static ILogger Macro => _rootLogger.ForContext(CategoryPropertyName, "macro");

    /// <summary>
    /// 起動時に呼び出し、ファイルシンク付きの実ロガーを構築する。
    /// 出力先は <c>%LOCALAPPDATA%\IMEIndicator\logs\imeindicator.log</c>（現行版と同一パス）。
    /// </summary>
    /// <param name="initialLevel">起動直後に適用するログレベル（通常は設定ファイルの logLevel）。</param>
    public static void Initialize(LogLevel initialLevel)
    {
        LevelSwitch.MinimumLevel = ToSerilogLevel(initialLevel);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IMEIndicator",
            "logs");
        Directory.CreateDirectory(logDirectory);
        var logFilePath = Path.Combine(logDirectory, "imeindicator.log");

        _rootLogger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.WithThreadId()
            .WriteTo.File(
                logFilePath,
                outputTemplate: OutputTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCountLimit)
            .CreateLogger();
    }

    /// <summary>
    /// テスト用に任意の <see cref="ILogger"/> 実装へ差し替える。
    /// ファイル書き込みを避けたいテストでは、<see cref="LevelSwitch"/> に連動する
    /// <see cref="LoggerConfiguration"/> を組み立てて渡すこと。
    /// </summary>
    public static void Configure(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _rootLogger = logger;
    }

    /// <summary>
    /// ログレベルを切り替える。<see cref="LevelSwitch"/> を介して全カテゴリへ即時反映される。
    /// </summary>
    public static void SetLevel(LogLevel level) => LevelSwitch.MinimumLevel = ToSerilogLevel(level);

    /// <summary>
    /// <see cref="LogLevel"/> を Serilog の <see cref="LogEventLevel"/> へ変換する
    /// （現行 <c>Logger::toSpdlogLevel</c> の移植）。未知の値は Warning にフォールバックする。
    /// </summary>
    public static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Info => LogEventLevel.Information,
        LogLevel.Warn => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Warning,
    };
}
