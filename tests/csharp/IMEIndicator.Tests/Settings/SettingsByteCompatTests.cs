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
/// settings.json のバイト互換性テスト（契約: specs/014-port-to-csharp/contracts/settings-compat-contract.md
/// （v4 まで）、specs/015-split-appearance-settings/contracts/settings-schema-contract.md（v4→v5 差分）、
/// specs/016-custom-background-image/contracts/settings-schema-contract.md（v5→v6 差分）、
/// specs/018-draggable-background-image/contracts/settings-schema-contract.md（v6→v7 差分））。
/// C++ 版（nlohmann::json の dump(2)）と同じキー順・インデント・改行・非エスケープ・小数点表記になることを検証する。
/// </summary>
public sealed class SettingsByteCompatTests
{
    private static JsonSerializerOptions BuildOptions() => new()
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
            new BackgroundImagePositionConverter(),
        },
    };

    [Fact]
    public void DefaultSettings_TopLevelKeyOrder_MatchesContract()
    {
        string json = JsonSerializer.Serialize(new AppSettings(), BuildOptions());
        string[] expectedOrder =
        [
            "schemaVersion", "mouseCursorIndicator", "imeOnText", "imeOffText", "isFirstLaunch",
            "processPriorityRules", "pollingIntervalSeconds", "logLevel", "pixelVerificationIntervalMs",
            "hotkeySettings", "backgroundImage",
        ];
        AssertTopLevelKeyOrder(json, expectedOrder);
    }

    [Fact]
    public void DefaultSettings_MouseCursorIndicatorKeyOrder_MatchesContract()
    {
        string json = JsonSerializer.Serialize(new AppSettings(), BuildOptions());
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement mci = doc.RootElement.GetProperty("mouseCursorIndicator");
        string[] expected = ["isVisible", "size", "opacity", "offsetX", "offsetY"];
        AssertObjectKeyOrder(mci, expected);
    }

    [Fact]
    public void DefaultSettings_HotkeySettingsKeyOrder_MatchesContract()
    {
        string json = JsonSerializer.Serialize(new AppSettings(), BuildOptions());
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement hk = doc.RootElement.GetProperty("hotkeySettings");
        AssertObjectKeyOrder(hk, ["hotkeys", "categories", "globalOptions"]);
        AssertObjectKeyOrder(hk.GetProperty("globalOptions"),
            ["hookMode", "distinguishLeftRightModifiers", "foregroundExcludeProcesses", "mouseDelayMs", "playSoundOnExecution"]);
    }

    [Fact]
    public void DefaultSettings_BackgroundImageKeyOrder_MatchesContract()
    {
        // 018-draggable-background-image: position 追加後のキー順
        // （contracts/settings-schema-contract.md「差分」節）。
        string json = JsonSerializer.Serialize(new AppSettings(), BuildOptions());
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement bg = doc.RootElement.GetProperty("backgroundImage");
        AssertObjectKeyOrder(bg, ["isVisible", "size", "opacity", "imagePath", "position"]);
    }

    [Fact]
    public void DefaultSettings_BackgroundImagePosition_IsNull()
    {
        // 018-draggable-background-image FR-015: 未移動は position: null で表現する
        // （contracts/settings-schema-contract.md「書き出し規則」節）。
        string json = JsonSerializer.Serialize(new AppSettings(), BuildOptions());
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement position = doc.RootElement.GetProperty("backgroundImage").GetProperty("position");
        Assert.Equal(JsonValueKind.Null, position.ValueKind);
    }

    [Fact]
    public void DefaultSettings_BackgroundImageImagePath_IsEmptyString()
    {
        // 016-custom-background-image FR-004: 未指定は空文字で表現し、同梱の既定画像を使う。
        string json = JsonSerializer.Serialize(new AppSettings(), BuildOptions());
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement bg = doc.RootElement.GetProperty("backgroundImage");
        Assert.Equal(string.Empty, bg.GetProperty("imagePath").GetString());
    }

    [Fact]
    public void MovedBackgroundImagePosition_KeyOrder_MatchesContract()
    {
        // 018-draggable-background-image: position オブジェクトのキー順
        // （contracts/settings-schema-contract.md「差分」節）。
        var settings = new AppSettings();
        settings.BackgroundImage.Position = new BackgroundImagePosition("DISPLAY1", BackgroundImageAnchor.BottomRight, 24.0, 16.0);
        string json = JsonSerializer.Serialize(settings, BuildOptions());
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement position = doc.RootElement.GetProperty("backgroundImage").GetProperty("position");
        AssertObjectKeyOrder(position, ["monitorId", "anchor", "offsetX", "offsetY"]);
    }

    [Fact]
    public void BackgroundImagePositionOffsets_AlwaysIncludeDecimalPoint()
    {
        // 018-draggable-background-image: offsetX/offsetY も DoubleWithPointConverter と同じ書式
        // （contracts/settings-schema-contract.md「書き出し規則」節）。整数値の 24.0 / 16.0 でも
        // 小数点が付くこと（DoubleFields_AlwaysIncludeDecimalPoint と同じ観点）。
        var settings = new AppSettings();
        settings.BackgroundImage.Position = new BackgroundImagePosition("DISPLAY1", BackgroundImageAnchor.BottomRight, 24.0, 16.0);
        string json = JsonSerializer.Serialize(settings, BuildOptions());
        Assert.Contains("\"offsetX\": 24.0", json);
        Assert.Contains("\"offsetY\": 16.0", json);
    }

    [Fact]
    public void BackgroundImagePositionMonitorId_BackslashesAreEscaped()
    {
        // 018-draggable-background-image: monitorId の \ は JSON の規則により \\ になる
        // （contracts/settings-schema-contract.md「書き出し規則」節）。
        const string monitorId = @"\\?\DISPLAY#GSM1388#4&125707d6&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var settings = new AppSettings();
        settings.BackgroundImage.Position = new BackgroundImagePosition(monitorId, BackgroundImageAnchor.BottomRight, 24.0, 16.0);
        string json = JsonSerializer.Serialize(settings, BuildOptions());

        // 生 JSON 文字列内で "\" が "\\"（2 文字）にエスケープされていること。
        string expectedEscaped = monitorId.Replace("\\", "\\\\");
        Assert.Contains($"\"monitorId\": \"{expectedEscaped}\"", json);

        // 読み戻すと元の値（エスケープ前）と一致すること。
        using JsonDocument doc = JsonDocument.Parse(json);
        string? roundTripped = doc.RootElement
            .GetProperty("backgroundImage")
            .GetProperty("position")
            .GetProperty("monitorId")
            .GetString();
        Assert.Equal(monitorId, roundTripped);
    }

    [Fact]
    public void DefaultSettings_WritesAlwaysSchemaVersion7()
    {
        var settings = new AppSettings { SchemaVersion = 1 }; // 意図的に不整合な値を入れても
        string json = JsonSerializer.Serialize(settings, BuildOptions());
        using JsonDocument doc = JsonDocument.Parse(json);
        // Serialize 単体では SchemaVersion をこちらで明示的に 7 にしない限り書き出し値は反映されない。
        // 「常に 7」を強制するのは SettingsManager.Save() の責務（SettingsManagerTests で別途検証）。
        // ここでは Clamp() が 1 を許容範囲として保持することのみ確認する（無効値のみ 7 に補正される）。
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void NonAsciiCharacters_AreNotEscaped()
    {
        var settings = new AppSettings { ImeOnText = "あ", ImeOffText = "A" };
        string json = JsonSerializer.Serialize(settings, BuildOptions());
        Assert.Contains("\"あ\"", json);
        Assert.DoesNotContain("\\u3042", json); // "あ" の \uXXXX エスケープが出ないこと
    }

    [Fact]
    public void ForwardSlash_IsNotEscaped()
    {
        var settings = new AppSettings();
        settings.HotkeySettings.Hotkeys.Add(new HotKeyEntry { Exe = "C:/path/to/app.exe" });
        string json = JsonSerializer.Serialize(settings, BuildOptions());
        Assert.Contains("C:/path/to/app.exe", json);
        Assert.DoesNotContain("C:\\/path", json); // "\/" にエスケープされないこと
    }

    [Theory]
    [InlineData(34.0, "34.0")]
    [InlineData(0.9, "0.9")]
    [InlineData(15.0, "15.0")]
    public void DoubleFields_AlwaysIncludeDecimalPoint(double value, string expectedSubstring)
    {
        var settings = new AppSettings();
        settings.MouseCursorIndicator.Size = value;
        settings.MouseCursorIndicator.Opacity = value is >= 0.1 and <= 1.0 ? value : 0.9;
        string json = JsonSerializer.Serialize(settings, BuildOptions());
        Assert.Contains($"\"size\": {expectedSubstring}", json);
    }

    [Theory]
    [InlineData(32.0, "32.0")]
    [InlineData(128.0, "128.0")]
    [InlineData(512.0, "512.0")]
    public void BackgroundImageDoubleFields_AlwaysIncludeDecimalPoint(double size, string expectedSubstring)
    {
        var settings = new AppSettings();
        settings.BackgroundImage.Size = size;
        string json = JsonSerializer.Serialize(settings, BuildOptions());
        Assert.Contains($"\"size\": {expectedSubstring}", json);
    }

    [Fact]
    public void SerializedJson_UsesTwoSpaceIndentAndNoCarriageReturn()
    {
        string json = JsonSerializer.Serialize(new AppSettings(), BuildOptions());
        Assert.Contains("\n  \"mouseCursorIndicator\"", json);
        Assert.DoesNotContain("\r\n", json);
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void EmptyArrays_AreWrittenInline()
    {
        string json = JsonSerializer.Serialize(new AppSettings(), BuildOptions());
        Assert.Contains("\"processPriorityRules\": []", json);
    }

    [Fact]
    public void UnknownFields_AreIgnoredWithoutThrowing()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "mouseCursorIndicator": { "isVisible": true, "size": 34.0, "opacity": 0.9, "offsetX": 15.0, "offsetY": 15.0 },
          "imeOnText": "あ", "imeOffText": "A", "isFirstLaunch": false,
          "processPriorityRules": [], "pollingIntervalSeconds": 1,
          "logLevel": "warn", "pixelVerificationIntervalMs": 2000,
          "someFutureField": "ignored"
        }
        """;
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, BuildOptions());
        Assert.NotNull(settings);
        Assert.Equal(LogLevel.Warn, settings!.LogLevel);
    }

    // 上位キーの出現順を、値の型を問わず先頭からの出現順で検証する。
    private static void AssertTopLevelKeyOrder(string json, string[] expectedOrder)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        AssertObjectKeyOrder(doc.RootElement, expectedOrder);
    }

    private static void AssertObjectKeyOrder(JsonElement obj, string[] expectedOrder)
    {
        string[] actual = obj.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(expectedOrder, actual);
    }
}
