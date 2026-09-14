// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Encodings.Web;
using System.Text.Json;
using IMEIndicator.Models;
using IMEIndicator.Models.Hotkey;
using IMEIndicator.Settings;
using Xunit;

namespace IMEIndicator.Tests.Settings;

/// <summary>
/// <see cref="DoubleWithPointConverter"/> / <see cref="LogLevelConverter"/> /
/// <see cref="PriorityLevelConverter"/> / <see cref="HookModeConverter"/> の単体テスト。
/// 契約: specs/014-port-to-csharp/contracts/settings-compat-contract.md。
/// </summary>
/// <remarks>
/// 2026-09-11 時点、このマシンでは Smart App Control が dotnet test の実行をブロックしているため
/// （specs/014-port-to-csharp/quickstart.md 検証記録参照）、本ファイルは未実行のまま作成した。
/// SAC 解除後、最初に実行して結果を確認すること。
/// </remarks>
public sealed class JsonConvertersTests
{
    private static JsonSerializerOptions BuildOptions() => new()
    {
        Converters =
        {
            new DoubleWithPointConverter(),
            new LogLevelConverter(),
            new PriorityLevelConverter(),
            new HookModeConverter(),
        },
    };

    [Theory]
    [InlineData(34.0, "34.0")]
    [InlineData(0.9, "0.9")]
    [InlineData(100.0, "100.0")]
    [InlineData(15.0, "15.0")]
    [InlineData(0.1, "0.1")]
    [InlineData(0.0, "0.0")]
    [InlineData(20.5, "20.5")]
    public void DoubleWithPointConverter_AlwaysWritesDecimalPoint(double value, string expected)
    {
        string json = JsonSerializer.Serialize(value, BuildOptions());
        Assert.Equal(expected, json);
    }

    [Fact]
    public void DoubleWithPointConverter_RoundTripsExactValue()
    {
        JsonSerializerOptions options = BuildOptions();
        const double original = 34.0;
        string json = JsonSerializer.Serialize(original, options);
        double parsed = JsonSerializer.Deserialize<double>(json, options);
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("warn", LogLevel.Warn)]
    [InlineData("WARN", LogLevel.Warn)]
    [InlineData("Warning", LogLevel.Warn)]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("Critical", LogLevel.Critical)]
    [InlineData("err", LogLevel.Error)]
    [InlineData("ERROR", LogLevel.Error)]
    public void LogLevelConverter_ParsesKnownValuesCaseInsensitively(string input, LogLevel expected)
    {
        LogLevel actual = JsonSerializer.Deserialize<LogLevel>($"\"{input}\"", BuildOptions());
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("notalevel")]
    [InlineData("")]
    public void LogLevelConverter_UnknownString_FallsBackToWarnWithoutThrowing(string input)
    {
        LogLevel actual = JsonSerializer.Deserialize<LogLevel>($"\"{input}\"", BuildOptions());
        Assert.Equal(LogLevel.Warn, actual);
    }

    [Fact]
    public void LogLevelConverter_NonStringToken_FallsBackToWarnWithoutThrowing()
    {
        LogLevel actual = JsonSerializer.Deserialize<LogLevel>("123", BuildOptions());
        Assert.Equal(LogLevel.Warn, actual);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "trace")]
    [InlineData(LogLevel.Warn, "warn")]
    [InlineData(LogLevel.Critical, "critical")]
    public void LogLevelConverter_WritesCanonicalLowercase(LogLevel level, string expected)
    {
        string json = JsonSerializer.Serialize(level, BuildOptions());
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData("Idle", PriorityLevel.Idle)]
    [InlineData("BelowNormal", PriorityLevel.BelowNormal)]
    [InlineData("Realtime", PriorityLevel.Realtime)]
    public void PriorityLevelConverter_ParsesExactCaseMatch(string input, PriorityLevel expected)
    {
        PriorityLevel actual = JsonSerializer.Deserialize<PriorityLevel>($"\"{input}\"", BuildOptions());
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PriorityLevelConverter_IsCaseSensitive_WrongCaseFallsBackToNormal()
    {
        // 移植元 C++ の tryParsePriorityLevel は大小無視しない（LogLevel/HookMode と異なる）。
        PriorityLevel actual = JsonSerializer.Deserialize<PriorityLevel>("\"idle\"", BuildOptions());
        Assert.Equal(PriorityLevel.Normal, actual);
    }

    [Theory]
    [InlineData(0, PriorityLevel.Idle)]
    [InlineData(5, PriorityLevel.Realtime)]
    public void PriorityLevelConverter_NumericFallback_AcceptsRawEnumValue(int input, PriorityLevel expected)
    {
        PriorityLevel actual = JsonSerializer.Deserialize<PriorityLevel>(input.ToString(), BuildOptions());
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PriorityLevelConverter_OutOfRangeNumber_FallsBackToNormal()
    {
        PriorityLevel actual = JsonSerializer.Deserialize<PriorityLevel>("99", BuildOptions());
        Assert.Equal(PriorityLevel.Normal, actual);
    }

    [Theory]
    [InlineData(PriorityLevel.Idle, "Idle")]
    [InlineData(PriorityLevel.AboveNormal, "AboveNormal")]
    public void PriorityLevelConverter_WritesPascalCase(PriorityLevel level, string expected)
    {
        string json = JsonSerializer.Serialize(level, BuildOptions());
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData("none", HookMode.None)]
    [InlineData("low_level", HookMode.LowLevel)]
    [InlineData("lowlevel", HookMode.LowLevel)]
    [InlineData("LOW_LEVEL", HookMode.LowLevel)]
    [InlineData("system", HookMode.SystemHotKey)]
    [InlineData("auto", HookMode.Auto)]
    [InlineData("AUTO", HookMode.Auto)]
    public void HookModeConverter_ParsesKnownValuesCaseInsensitively(string input, HookMode expected)
    {
        HookMode actual = JsonSerializer.Deserialize<HookMode>($"\"{input}\"", BuildOptions());
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void HookModeConverter_UnknownValue_FallsBackToAutoWithoutThrowing()
    {
        HookMode actual = JsonSerializer.Deserialize<HookMode>("\"garbage\"", BuildOptions());
        Assert.Equal(HookMode.Auto, actual);
    }

    [Theory]
    [InlineData(HookMode.None, "none")]
    [InlineData(HookMode.LowLevel, "low_level")]
    [InlineData(HookMode.SystemHotKey, "system")]
    [InlineData(HookMode.Auto, "auto")]
    public void HookModeConverter_WritesCanonicalLowercase(HookMode mode, string expected)
    {
        string json = JsonSerializer.Serialize(mode, BuildOptions());
        Assert.Equal($"\"{expected}\"", json);
    }

    [Fact]
    public void SettingsJsonContext_SerializesDefaultAppSettings_WithoutThrowing()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = SettingsJsonContext.Default,
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters =
            {
                new DoubleWithPointConverter(),
                new LogLevelConverter(),
                new PriorityLevelConverter(),
                new HookModeConverter(),
            },
        };

        var settings = new AppSettings();
        string json = JsonSerializer.Serialize(settings, options);

        Assert.Contains("\"schemaVersion\": 6", json);
        Assert.Contains("\"size\": 34.0", json);
        // 015-split-appearance-settings: backgroundImage.size/opacity も DoubleWithPointConverter で
        // 末尾 .0 付きの表記になること（contracts/settings-schema-contract.md）。
        Assert.Contains("\"size\": 128.0", json);
        Assert.Contains("\"opacity\": 1.0", json);
        Assert.Contains("あ", json); // 非 ASCII が \uXXXX にエスケープされないこと
        Assert.DoesNotContain("\\u", json);
    }
}
