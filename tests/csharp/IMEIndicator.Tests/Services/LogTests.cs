// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;
using IMEIndicator.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
// Serilog の静的グローバルロガー Serilog.Log と IMEIndicator.Services.Log が同名のため、
// using-alias で明示的にこちらを指す（CS0104 あいまい参照の回避。Log.cs 冒頭のコメント参照）。
using Log = IMEIndicator.Services.Log;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="Log"/> のテスト。現行 tests/cpp/unit/LoggerTests.cpp の移植。
/// T017 の指示どおり、<see cref="Log.Configure"/> でメモリ上のシンクへ差し替え、
/// ファイルシステムには一切書き込まない。
/// </summary>
/// <remarks>
/// <see cref="Log.LevelSwitch"/> と内部の実ロガーはクラス静的な状態のため、各テストは
/// 冒頭で必ず <see cref="Log.Configure"/> を呼んで自分専用のシンクへ切り替えてから検証する
/// （xUnit は既定で同一クラス内のテストを並列実行しないため、テストメソッド間の競合はない）。
/// </remarks>
public sealed class LogTests
{
    [Fact]
    public void SetLevel_SuppressesEventsBelowConfiguredMinimum()
    {
        var sink = new CapturingSink();
        Log.Configure(BuildTestLogger(sink));

        Log.SetLevel(LogLevel.Warn);
        Log.App.Information("info は warn 未満なので抑制される");
        Assert.Empty(sink.Events);

        Log.App.Warning("warn は閾値を満たすので記録される");
        Assert.Single(sink.Events);
    }

    [Fact]
    public void SetLevel_LoweringThreshold_AllowsPreviouslySuppressedLevel()
    {
        var sink = new CapturingSink();
        Log.Configure(BuildTestLogger(sink));

        Log.SetLevel(LogLevel.Warn);
        Log.App.Debug("抑制される");
        Assert.Empty(sink.Events);

        Log.SetLevel(LogLevel.Trace);
        Log.App.Debug("Trace まで開放したので記録される");
        Assert.Single(sink.Events);
    }

    [Theory]
    [InlineData(LogLevel.Trace, LogEventLevel.Verbose)]
    [InlineData(LogLevel.Debug, LogEventLevel.Debug)]
    [InlineData(LogLevel.Info, LogEventLevel.Information)]
    [InlineData(LogLevel.Warn, LogEventLevel.Warning)]
    [InlineData(LogLevel.Error, LogEventLevel.Error)]
    [InlineData(LogLevel.Critical, LogEventLevel.Fatal)]
    public void ToSerilogLevel_MapsEachLogLevelCorrectly(LogLevel level, LogEventLevel expected)
    {
        Assert.Equal(expected, Log.ToSerilogLevel(level));
    }

    [Fact]
    public void ToSerilogLevel_UnknownValue_FallsBackToWarning()
    {
        var unknown = (LogLevel)999;

        Assert.Equal(LogEventLevel.Warning, Log.ToSerilogLevel(unknown));
    }

    [Fact]
    public void CategoryLoggers_AttachExpectedCategoryProperty()
    {
        var sink = new CapturingSink();
        Log.Configure(BuildTestLogger(sink));
        Log.SetLevel(LogLevel.Trace);

        (ILogger Logger, string Expected)[] cases =
        [
            (Log.App, "app"),
            (Log.Ime, "ime"),
            (Log.Pixel, "pixel"),
            (Log.Priority, "priority"),
            (Log.Power, "power"),
            (Log.Display, "display"),
            (Log.Tray, "tray"),
            (Log.Settings, "settings"),
            (Log.Hotkey, "hotkey"),
            (Log.Hook, "hook"),
            (Log.Command, "command"),
            (Log.Macro, "macro"),
        ];

        foreach (var (logger, expected) in cases)
        {
            sink.Events.Clear();
            logger.Information("probe: {Category}", expected);

            var evt = Assert.Single(sink.Events);
            Assert.True(evt.Properties.TryGetValue("Category", out var property));
            var scalar = Assert.IsType<ScalarValue>(property);
            var categoryValue = Assert.IsType<string>(scalar.Value);
            Assert.Equal(expected, categoryValue);
        }
    }

    [Fact]
    public void Configure_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Log.Configure(null!));
    }

    // Log.LevelSwitch に連動させたテスト専用ロガーを組み立てる。ファイルへは書かず、
    // CapturingSink がメモリ上にイベントを保持するだけなのでテスト間で副作用を残さない。
    private static ILogger BuildTestLogger(CapturingSink sink) =>
        new LoggerConfiguration()
            .MinimumLevel.ControlledBy(Log.LevelSwitch)
            .WriteTo.Sink(sink)
            .CreateLogger();

    // Serilog.Core.ILogEventSink の最小実装。ファイルへ書かずにメモリ上でイベントを捕捉する。
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
