using System.IO;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;
using Xunit;

namespace IMEIndicatorW.Tests;

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
        Assert.NotNull(manager.Settings.Debug);
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
        Assert.NotNull(manager.Settings.Debug);
    }

    [Fact]
    public void ValidateAndClamp_OutOfRange_Clamped()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        manager.Settings.MouseCursorIndicator.Opacity = 2.0;
        manager.Settings.MouseCursorIndicator.Size = 10; // 下限20未満
        manager.Settings.Debug.PollingInterval = 10;      // 下限50未満

        // Act
        manager.ValidateAndClampSettings();

        // Assert
        Assert.Equal(1.0, manager.Settings.MouseCursorIndicator.Opacity);
        Assert.Equal(20, manager.Settings.MouseCursorIndicator.Size);
        Assert.Equal(50, manager.Settings.Debug.PollingInterval);
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
}
