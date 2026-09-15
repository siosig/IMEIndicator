// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json;
using IMEIndicator.Models;
using IMEIndicator.Settings;
using Xunit;

namespace IMEIndicator.Tests.Settings;

/// <summary>
/// <see cref="SettingsManager"/> のテスト。現行 tests/cpp/unit/SettingsManagerTests.cpp の移植。
/// </summary>
public sealed class SettingsManagerTests
{
    // 現行 tests/cpp/unit/SettingsManagerTests.cpp の TempDir 相当。テストごとに固有の一時ディレクトリを作り、破棄時に削除する。
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "imeindicator_settings_" + Environment.ProcessId + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // ベストエフォート。テスト後片付けの失敗でテスト自体は失敗させない。
            }
        }
    }

    private static string FixturesDir() =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static void CopyFixtureTo(string fixtureName, string dest) =>
        File.Copy(System.IO.Path.Combine(FixturesDir(), fixtureName), dest, overwrite: true);

    [Fact]
    public void FreshDirectoryYieldsDefaultsAndFirstLaunch()
    {
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);

        Assert.False(mgr.Load()); // ファイル無し → false 返り、デフォルトで構築
        Assert.True(mgr.Settings.IsFirstLaunch);
        Assert.Equal(1, mgr.Settings.PollingIntervalSeconds);
        Assert.Equal(34.0, mgr.Settings.MouseCursorIndicator.Size);
    }

    [Fact]
    public void LoadV1SampleAndMigrateOnSave()
    {
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        CopyFixtureTo("settings-v1-sample.json", mgr.SettingsFilePath);

        Assert.True(mgr.Load());
        Assert.Equal(40.0, mgr.Settings.MouseCursorIndicator.Size);
        Assert.Equal(2, mgr.Settings.PollingIntervalSeconds);
        ProcessPriorityRule rule = Assert.Single(mgr.Settings.ProcessPriorityRules);
        Assert.Equal(PriorityLevel.BelowNormal, rule.TargetPriority);
        // v1 として読まれたあと内部で v7 に昇格しているはず
        Assert.Equal(7, mgr.Settings.SchemaVersion);

        Assert.True(mgr.Save());
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(mgr.SettingsFilePath));
        Assert.Equal(7, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("logLevel", out _));
        Assert.True(doc.RootElement.TryGetProperty("pixelVerificationIntervalMs", out _));
    }

    [Fact]
    public void LoadV2SampleRoundTrip()
    {
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        CopyFixtureTo("settings-v2-sample.json", mgr.SettingsFilePath);

        Assert.True(mgr.Load());
        Assert.Equal(7, mgr.Settings.SchemaVersion);
        Assert.Equal(2, mgr.Settings.ProcessPriorityRules.Count);
        Assert.Equal(LogLevel.Warn, mgr.Settings.LogLevel);
        Assert.Equal(2000, mgr.Settings.PixelVerificationIntervalMs);

        // Save 後に再読み込みしても、主要な値は等価
        double originalSize = mgr.Settings.MouseCursorIndicator.Size;
        int originalRuleCount = mgr.Settings.ProcessPriorityRules.Count;
        Assert.True(mgr.Save());

        var mgr2 = new SettingsManager(tmp.Path);
        Assert.True(mgr2.Load());
        Assert.Equal(originalSize, mgr2.Settings.MouseCursorIndicator.Size);
        Assert.Equal(originalRuleCount, mgr2.Settings.ProcessPriorityRules.Count);
        Assert.Equal(7, mgr2.Settings.SchemaVersion);
    }

    [Fact]
    public void LoadV3SampleMigratesToV6AndCreatesBackup()
    {
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        CopyFixtureTo("settings-v3-sample.json", mgr.SettingsFilePath);

        Assert.True(mgr.Load());
        // v3 には backgroundImage が無い → 既定値（無効、サイズ/不透明度は導入前と同じ 128/1.0。FR-009）
        Assert.False(mgr.Settings.BackgroundImage.IsVisible);
        Assert.Equal(128.0, mgr.Settings.BackgroundImage.Size);
        Assert.Equal(1.0, mgr.Settings.BackgroundImage.Opacity);
        // v3 として読まれたあと内部で v7 に昇格
        Assert.Equal(7, mgr.Settings.SchemaVersion);
        Assert.Equal(2, mgr.Settings.ProcessPriorityRules.Count);
        Assert.Empty(mgr.Settings.HotkeySettings.Hotkeys);

        // v3 バックアップが作られる
        string bak = mgr.SettingsFilePath + ".v3.bak";
        Assert.True(File.Exists(bak));

        // 最古を保持: 既存の .v3.bak は 2 回目の load で上書きされない
        File.AppendAllText(bak, "\n// marker");
        var again = new SettingsManager(tmp.Path);
        Assert.True(again.Load());
        Assert.Contains("// marker", File.ReadAllText(bak));

        // Save すれば v7 で書き出され、backgroundImage が含まれる
        Assert.True(mgr.Save());
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(mgr.SettingsFilePath));
        Assert.Equal(7, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(doc.RootElement.GetProperty("backgroundImage").GetProperty("isVisible").GetBoolean());
    }

    [Fact]
    public void LoadV4SampleMigratesToV6AndCreatesBackup()
    {
        // 016-custom-background-image（contracts/settings-schema-contract.md「マイグレーション」節）:
        // v1/v2/v3 と同じパターンを v5→v6 にも延長したことの検証。
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        string v4Json = File.ReadAllText(System.IO.Path.Combine(FixturesDir(), "settings-v3-sample.json"))
            .Replace("\"schemaVersion\": 3", "\"schemaVersion\": 4");
        File.WriteAllText(mgr.SettingsFilePath, v4Json);

        Assert.True(mgr.Load());
        Assert.Equal(7, mgr.Settings.SchemaVersion);

        // v4 バックアップが作られる
        string bak = mgr.SettingsFilePath + ".v4.bak";
        Assert.True(File.Exists(bak));

        // 最古を保持: 既存の .v4.bak は 2 回目の load で上書きされない
        File.AppendAllText(bak, "\n// marker");
        var again = new SettingsManager(tmp.Path);
        Assert.True(again.Load());
        Assert.Contains("// marker", File.ReadAllText(bak));

        Assert.True(mgr.Save());
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(mgr.SettingsFilePath));
        Assert.Equal(7, doc.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void BackgroundImageVisibleRoundTrip()
    {
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        mgr.Load(); // ファイル無し → 既定値
        Assert.False(mgr.Settings.BackgroundImage.IsVisible);

        mgr.Settings.BackgroundImage.IsVisible = true;
        Assert.True(mgr.Save());

        var mgr2 = new SettingsManager(tmp.Path);
        Assert.True(mgr2.Load());
        Assert.True(mgr2.Settings.BackgroundImage.IsVisible);
        Assert.Equal(7, mgr2.Settings.SchemaVersion);
        // mouseCursorIndicator.isVisible とは独立
        Assert.True(mgr2.Settings.MouseCursorIndicator.IsVisible);
    }

    [Fact]
    public void LoadV5SampleRoundTrip()
    {
        // 015-split-appearance-settings: backgroundImage.size/opacity を持つファイルの読み書き検証。
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        CopyFixtureTo("settings-v5-sample.json", mgr.SettingsFilePath);

        Assert.True(mgr.Load());
        Assert.Equal(7, mgr.Settings.SchemaVersion);
        Assert.Equal(300.0, mgr.Settings.BackgroundImage.Size);
        Assert.Equal(0.3, mgr.Settings.BackgroundImage.Opacity);
        Assert.Equal(string.Empty, mgr.Settings.BackgroundImage.ImagePath);

        Assert.True(mgr.Save());
        var mgr2 = new SettingsManager(tmp.Path);
        Assert.True(mgr2.Load());
        Assert.Equal(300.0, mgr2.Settings.BackgroundImage.Size);
        Assert.Equal(0.3, mgr2.Settings.BackgroundImage.Opacity);
    }

    [Fact]
    public void LoadV5SampleMigratesToV6AndCreatesBackup()
    {
        // 016-custom-background-image(contracts/settings-schema-contract.md「マイグレーション」節):
        // v1〜v4 と同じパターンを v5→v6 にも延長したことの検証。
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        CopyFixtureTo("settings-v5-sample.json", mgr.SettingsFilePath);

        Assert.True(mgr.Load());
        Assert.Equal(7, mgr.Settings.SchemaVersion);
        // v5 には imagePath が無い → 既定値(未指定)
        Assert.Equal(string.Empty, mgr.Settings.BackgroundImage.ImagePath);

        // v5 バックアップが作られる
        string bak = mgr.SettingsFilePath + ".v5.bak";
        Assert.True(File.Exists(bak));

        // 最古を保持: 既存の .v5.bak は 2 回目の load で上書きされない
        File.AppendAllText(bak, "\n// marker");
        var again = new SettingsManager(tmp.Path);
        Assert.True(again.Load());
        Assert.Contains("// marker", File.ReadAllText(bak));

        Assert.True(mgr.Save());
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(mgr.SettingsFilePath));
        Assert.Equal(7, doc.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void LoadV6SampleMigratesToV7AndCreatesBackup()
    {
        // 018-draggable-background-image(contracts/settings-schema-contract.md「マイグレーション」節):
        // v1〜v5 と同じパターンを v6→v7 にも延長したことの検証。
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        CopyFixtureTo("settings-v6-sample.json", mgr.SettingsFilePath);

        Assert.True(mgr.Load());
        Assert.Equal(7, mgr.Settings.SchemaVersion);
        Assert.Equal(300.0, mgr.Settings.BackgroundImage.Size);
        Assert.Equal(0.3, mgr.Settings.BackgroundImage.Opacity);
        Assert.Equal(@"C:\Users\example\Pictures\ime-on.png", mgr.Settings.BackgroundImage.ImagePath);
        // v6 には position が無い → 既定値(未移動)
        Assert.Null(mgr.Settings.BackgroundImage.Position);

        // v6 バックアップが作られる
        string bak = mgr.SettingsFilePath + ".v6.bak";
        Assert.True(File.Exists(bak));

        // 最古を保持: 既存の .v6.bak は 2 回目の load で上書きされない
        File.AppendAllText(bak, "\n// marker");
        var again = new SettingsManager(tmp.Path);
        Assert.True(again.Load());
        Assert.Contains("// marker", File.ReadAllText(bak));

        Assert.True(mgr.Save());
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(mgr.SettingsFilePath));
        Assert.Equal(7, doc.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void LoadV7SampleRoundTrip()
    {
        // 018-draggable-background-image: backgroundImage.position を持つファイルの読み書き検証。
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        CopyFixtureTo("settings-v7-sample.json", mgr.SettingsFilePath);

        Assert.True(mgr.Load());
        Assert.Equal(7, mgr.Settings.SchemaVersion);
        BackgroundImagePosition? position = mgr.Settings.BackgroundImage.Position;
        Assert.NotNull(position);
        Assert.Equal(
            @"\\?\DISPLAY#GSM1388#4&125707d6&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
            position!.MonitorId);
        Assert.Equal(BackgroundImageAnchor.BottomRight, position.Anchor);
        Assert.Equal(24.0, position.OffsetX);
        Assert.Equal(16.0, position.OffsetY);

        Assert.True(mgr.Save());
        var mgr2 = new SettingsManager(tmp.Path);
        Assert.True(mgr2.Load());
        Assert.Equal(7, mgr2.Settings.SchemaVersion);
        BackgroundImagePosition? position2 = mgr2.Settings.BackgroundImage.Position;
        Assert.NotNull(position2);
        Assert.Equal(position!.MonitorId, position2!.MonitorId);
        Assert.Equal(position.Anchor, position2.Anchor);
        Assert.Equal(position.OffsetX, position2.OffsetX);
        Assert.Equal(position.OffsetY, position2.OffsetY);
    }

    [Fact]
    public void LoadInvalidPositionAnchor_DoesNotLoseOtherSettings()
    {
        // 018-draggable-background-image(contracts/settings-schema-contract.md「不正値の扱い」節):
        // position.anchor が未知の値でも BackgroundImagePositionConverter が null にフォールバックするだけで、
        // ホットキー設定・プロセス優先度ルールなど position 以外の設定は失われないことの検証。
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        string invalidJson = File.ReadAllText(System.IO.Path.Combine(FixturesDir(), "settings-v7-sample.json"))
            .Replace("\"anchor\": \"bottomRight\"", "\"anchor\": \"middle\"");
        File.WriteAllText(mgr.SettingsFilePath, invalidJson);

        Assert.True(mgr.Load());
        Assert.Null(mgr.Settings.BackgroundImage.Position);

        // position 以外の設定は失われない
        Assert.Empty(mgr.Settings.HotkeySettings.Hotkeys);
        ProcessPriorityRule rule = Assert.Single(mgr.Settings.ProcessPriorityRules);
        Assert.Equal("code.exe", rule.ProcessName);
        Assert.Equal(PriorityLevel.AboveNormal, rule.TargetPriority);
    }

    [Fact]
    public void BrokenFileFallsBackToDefaultsAndRenames()
    {
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);
        CopyFixtureTo("settings-broken.json", mgr.SettingsFilePath);

        Assert.False(mgr.Load()); // パース失敗 → false
        Assert.True(mgr.Settings.IsFirstLaunch); // デフォルト構築

        // 元ファイルは renameBroken で消えている
        Assert.False(File.Exists(mgr.SettingsFilePath));
        // .broken-* が存在する
        bool foundBroken = Directory.EnumerateFiles(tmp.Path).Any(p => System.IO.Path.GetFileName(p).Contains(".broken-"));
        Assert.True(foundBroken);
    }

    [Fact]
    public void ClampsOutOfRangeValuesOnLoad()
    {
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);

        const string json = """
        {
          "schemaVersion": 2,
          "mouseCursorIndicator": { "isVisible": true, "size": 9999.0, "opacity": 5.0, "offsetX": 0.0, "offsetY": 0.0 },
          "imeOnText": "あ",
          "imeOffText": "A",
          "isFirstLaunch": false,
          "processPriorityRules": [],
          "pollingIntervalSeconds": 99999,
          "logLevel": "warn",
          "pixelVerificationIntervalMs": 99999
        }
        """;
        File.WriteAllText(mgr.SettingsFilePath, json);

        Assert.True(mgr.Load());
        Assert.True(mgr.Settings.MouseCursorIndicator.Size <= 100.0);
        Assert.True(mgr.Settings.MouseCursorIndicator.Opacity <= 1.0);
        Assert.True(mgr.Settings.PollingIntervalSeconds <= 1800);
        Assert.True(mgr.Settings.PixelVerificationIntervalMs <= 60000);
    }

    [Fact]
    public void TruncatesProcessPriorityRulesAt30()
    {
        using TempDir tmp = new();
        var mgr = new SettingsManager(tmp.Path);

        var rules = Enumerable.Range(0, 50).Select(i => new
        {
            processName = $"p{i}",
            targetPriority = "Normal",
            maxBackoffExponent = 6,
            isEnabled = true,
            useECoreOnly = false,
        });
        var payload = new
        {
            schemaVersion = 2,
            mouseCursorIndicator = new { isVisible = true, size = 34.0, opacity = 0.9, offsetX = 15.0, offsetY = 15.0 },
            imeOnText = "あ",
            imeOffText = "A",
            isFirstLaunch = false,
            processPriorityRules = rules,
            pollingIntervalSeconds = 1,
            logLevel = "warn",
            pixelVerificationIntervalMs = 2000,
        };
        File.WriteAllText(mgr.SettingsFilePath, JsonSerializer.Serialize(payload));

        Assert.True(mgr.Load());
        Assert.Equal(30, mgr.Settings.ProcessPriorityRules.Count);
    }
}
