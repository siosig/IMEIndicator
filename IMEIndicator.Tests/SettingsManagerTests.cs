using System.IO;
using IMEIndicator.Models;
using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests;

/// <summary>
/// SettingsManagerのユニットテスト
/// tempディレクトリを使用してファイルI/Oをテスト
/// </summary>
public class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"IMEIndicatorW_Test_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); }
            catch { /* テストクリーンアップ失敗は無視 */ }
        }
    }

    [Fact]
    public void Load_NonexistentFile_ReturnsDefaults()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);

        // Act
        var result = manager.Load();

        // Assert
        Assert.True(result);
        Assert.NotNull(manager.Settings);
        Assert.NotNull(manager.Settings.MouseCursorIndicator);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesSettings()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        manager.Settings.ImeOnColor = "#FF0000";
        manager.Settings.ImeOnText = "日";
        manager.Settings.ImeOffColor = "#0000FF";
        manager.Settings.ImeOffText = "E";
        manager.Settings.MouseCursorIndicator.Size = 50;
        manager.Settings.MouseCursorIndicator.Opacity = 0.5;

        // Act
        var saveResult = manager.Save();
        var manager2 = new SettingsManager(_tempDir);
        var loadResult = manager2.Load();

        // Assert
        Assert.True(saveResult);
        Assert.True(loadResult);
        Assert.Equal("#FF0000", manager2.Settings.ImeOnColor);
        Assert.Equal("日", manager2.Settings.ImeOnText);
        Assert.Equal("#0000FF", manager2.Settings.ImeOffColor);
        Assert.Equal("E", manager2.Settings.ImeOffText);
        Assert.Equal(50, manager2.Settings.MouseCursorIndicator.Size);
        Assert.Equal(0.5, manager2.Settings.MouseCursorIndicator.Opacity);
    }

    [Fact]
    public void Load_CorruptJSON_FallsBackToDefaults()
    {
        // Arrange
        Directory.CreateDirectory(_tempDir);
        var manager = new SettingsManager(_tempDir);
        var filePath = manager.GetSettingsFilePath();
        File.WriteAllText(filePath, "{ invalid json !!!");

        // Act
        var result = manager.Load();

        // Assert
        Assert.False(result);
        Assert.NotNull(manager.LastError);
        // デフォルト設定にフォールバック
        Assert.NotNull(manager.Settings);
        Assert.Equal(new AppSettings().ImeOnColor, manager.Settings.ImeOnColor);
    }

    [Fact]
    public void Load_MissingNestedProperties_UsesDefaults()
    {
        // Arrange — MouseCursorIndicator プロパティが存在しないJSON
        Directory.CreateDirectory(_tempDir);
        var manager = new SettingsManager(_tempDir);
        var filePath = manager.GetSettingsFilePath();
        File.WriteAllText(filePath, """{"imeOnColor": "#FF0000"}""");

        // Act
        var result = manager.Load();

        // Assert
        Assert.True(result);
        Assert.NotNull(manager.Settings.MouseCursorIndicator);
    }

    [Fact]
    public void ValidateAndClamp_OutOfRange_Clamped()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        manager.Settings.MouseCursorIndicator.Opacity = 2.0;
        manager.Settings.MouseCursorIndicator.Size = 10; // 下限20未満
                                                         // 下限50未満

        // Act
        manager.ValidateAndClampSettings();

        // Assert
        Assert.Equal(1.0, manager.Settings.MouseCursorIndicator.Opacity);
        Assert.Equal(20, manager.Settings.MouseCursorIndicator.Size);
    }

    [Fact]
    public void Save_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        Assert.False(Directory.Exists(_tempDir));

        // Act
        var result = manager.Save();

        // Assert
        Assert.True(result);
        Assert.True(Directory.Exists(_tempDir));
        Assert.True(File.Exists(manager.GetSettingsFilePath()));
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        manager.Settings.ImeOnColor = "#999999";
        manager.Settings.MouseCursorIndicator.Size = 80;
        manager.Save();

        // Act
        manager.Reset();

        // Assert
        var defaults = new AppSettings();
        Assert.Equal(defaults.ImeOnColor, manager.Settings.ImeOnColor);
        Assert.Equal(defaults.MouseCursorIndicator.Size, manager.Settings.MouseCursorIndicator.Size);
    }

    [Fact]
    public void HideWhenImeOff_DefaultIsTrue()
    {
        // デフォルト値が true であることを確認
        var settings = new MouseCursorIndicatorSettings();
        Assert.True(settings.HideWhenImeOff);
    }

    [Fact]
    public void HideWhenImeOff_SaveAndLoad_Preserved()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        manager.Settings.MouseCursorIndicator.HideWhenImeOff = false;

        // Act
        manager.Save();
        var manager2 = new SettingsManager(_tempDir);
        manager2.Load();

        // Assert
        Assert.False(manager2.Settings.MouseCursorIndicator.HideWhenImeOff);
    }

    [Fact]
    public void HideWhenImeOff_MissingInJson_FallsBackToDefault()
    {
        // settings.json に hideWhenImeOff がない場合、デフォルト true が使用される
        Directory.CreateDirectory(_tempDir);
        var manager = new SettingsManager(_tempDir);
        File.WriteAllText(manager.GetSettingsFilePath(), """{"mouseCursorIndicator": {"isVisible": true}}""");

        manager.Load();

        Assert.True(manager.Settings.MouseCursorIndicator.HideWhenImeOff);
    }

    [Fact]
    public void Save_Atomic_NoTmpFileRemains()
    {
        // アトミック保存後に .tmp ファイルが残らないことを確認
        var manager = new SettingsManager(_tempDir);
        manager.Settings.ImeOnColor = "#AABBCC";

        var result = manager.Save();

        Assert.True(result);
        Assert.False(File.Exists(manager.GetSettingsFilePath() + ".tmp"));
        Assert.True(File.Exists(manager.GetSettingsFilePath()));
    }

    [Fact]
    public void Save_Atomic_ProducesValidJson()
    {
        // アトミック保存結果が有効な JSON であることを確認
        var manager = new SettingsManager(_tempDir);
        manager.Settings.ImeOnColor = "#112233";
        manager.Settings.MouseCursorIndicator.Size = 42;
        manager.Save();

        var json = File.ReadAllText(manager.GetSettingsFilePath());
        var parsed = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(parsed);
        Assert.Equal("#112233", parsed.ImeOnColor);
        Assert.Equal(42, parsed.MouseCursorIndicator.Size);
    }

    [Fact]
    public void Load_CleansStaleTmpFile()
    {
        // Load 時に残存 .tmp ファイルがクリーンアップされることを確認
        Directory.CreateDirectory(_tempDir);
        var manager = new SettingsManager(_tempDir);
        var tmpPath = manager.GetSettingsFilePath() + ".tmp";
        File.WriteAllText(tmpPath, "stale data");

        manager.Load();

        Assert.False(File.Exists(tmpPath));
    }

    [Fact]
    public void ProcessPriorityRules_SaveAndLoad_RoundTrip()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        manager.Settings.PollingIntervalSeconds = 5;
        manager.Settings.ProcessPriorityRules =
        [
            new() { ProcessName = "nextcloud.exe", TargetPriority = IMEIndicator.Models.PriorityLevel.Idle, MaxBackoffExponent = 6, IsEnabled = true },
            new() { ProcessName = "chrome", TargetPriority = IMEIndicator.Models.PriorityLevel.BelowNormal, MaxBackoffExponent = 4, IsEnabled = false }
        ];

        // Act
        manager.Save();
        var manager2 = new SettingsManager(_tempDir);
        manager2.Load();

        // Assert
        Assert.Equal(5, manager2.Settings.PollingIntervalSeconds);
        Assert.Equal(2, manager2.Settings.ProcessPriorityRules.Count);

        var rule1 = manager2.Settings.ProcessPriorityRules[0];
        Assert.Equal("nextcloud.exe", rule1.ProcessName);
        Assert.Equal(IMEIndicator.Models.PriorityLevel.Idle, rule1.TargetPriority);
        Assert.Equal(6, rule1.MaxBackoffExponent);
        Assert.True(rule1.IsEnabled);

        var rule2 = manager2.Settings.ProcessPriorityRules[1];
        Assert.Equal("chrome", rule2.ProcessName);
        Assert.Equal(IMEIndicator.Models.PriorityLevel.BelowNormal, rule2.TargetPriority);
        Assert.False(rule2.IsEnabled);
    }

    [Fact]
    public void ProcessPriorityRules_Validation_ClampsValues()
    {
        var manager = new SettingsManager(_tempDir);
        manager.Settings.ProcessPriorityRules =
        [
            new() { ProcessName = "test", MaxBackoffExponent = 15 }
        ];

        manager.ValidateAndClampSettings();

        var rule = manager.Settings.ProcessPriorityRules[0];
        Assert.Equal(10, rule.MaxBackoffExponent); // 最大10
    }

    [Fact]
    public void PollingIntervalSeconds_DefaultIsOne()
    {
        var manager = new SettingsManager(_tempDir);
        manager.Load();
        Assert.Equal(1, manager.Settings.PollingIntervalSeconds);
    }

    [Fact]
    public void PollingIntervalSeconds_Validation_Clamps()
    {
        var manager = new SettingsManager(_tempDir);
        manager.Settings.PollingIntervalSeconds = 0;
        manager.ValidateAndClampSettings();
        Assert.Equal(1, manager.Settings.PollingIntervalSeconds);

        manager.Settings.PollingIntervalSeconds = 2000;
        manager.ValidateAndClampSettings();
        Assert.Equal(1800, manager.Settings.PollingIntervalSeconds);
    }

    [Fact]
    public void ProcessPriorityRules_EmptyByDefault()
    {
        var manager = new SettingsManager(_tempDir);
        manager.Load();
        Assert.NotNull(manager.Settings.ProcessPriorityRules);
        Assert.Empty(manager.Settings.ProcessPriorityRules);
    }

    [Fact]
    public void ProcessPriorityRules_EnumSerializedAsString()
    {
        // PriorityLevel が文字列として JSON に保存されることを確認
        var manager = new SettingsManager(_tempDir);
        manager.Settings.ProcessPriorityRules =
        [
            new() { ProcessName = "test", TargetPriority = IMEIndicator.Models.PriorityLevel.High }
        ];
        manager.Save();

        var json = File.ReadAllText(manager.GetSettingsFilePath());
        Assert.Contains("\"High\"", json); // 数値ではなく文字列
        Assert.DoesNotContain("\"4\"", json); // High の enum 数値
    }
}
