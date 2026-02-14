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
        Assert.NotNull(manager.Settings.Clock);
        Assert.NotNull(manager.Settings.IMEIndicator);
        Assert.NotNull(manager.Settings.MouseCursorIndicator);
        Assert.NotNull(manager.Settings.Debug);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesSettings()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        manager.Settings.Clock.Width = 300;
        manager.Settings.Clock.Height = 150;
        manager.Settings.IMEIndicator.Opacity = 0.5;
        manager.Settings.Language = "ja";

        // Act
        var saveResult = manager.Save();
        var manager2 = new SettingsManager(_tempDir);
        var loadResult = manager2.Load();

        // Assert
        Assert.True(saveResult);
        Assert.True(loadResult);
        Assert.Equal(300, manager2.Settings.Clock.Width);
        Assert.Equal(150, manager2.Settings.Clock.Height);
        Assert.Equal(0.5, manager2.Settings.IMEIndicator.Opacity);
        Assert.Equal("ja", manager2.Settings.Language);
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
        Assert.Equal(new AppSettings().Clock.Width, manager.Settings.Clock.Width);
    }

    [Fact]
    public void Load_MissingNestedProperties_UsesDefaults()
    {
        // Arrange — Clock プロパティが存在しないJSON
        Directory.CreateDirectory(_tempDir);
        var manager = new SettingsManager(_tempDir);
        var filePath = manager.GetSettingsFilePath();
        File.WriteAllText(filePath, """{"language": "en"}""");

        // Act
        var result = manager.Load();

        // Assert
        Assert.True(result);
        Assert.NotNull(manager.Settings.Clock);
        Assert.NotNull(manager.Settings.IMEIndicator);
    }

    [Fact]
    public void ValidateAndClamp_OpacityOutOfRange_Clamped()
    {
        // Arrange
        var manager = new SettingsManager(_tempDir);
        manager.Settings.IMEIndicator.Opacity = 1.5;
        manager.Settings.Clock.Opacity = -0.1;
        manager.Settings.MouseCursorIndicator.Opacity = 2.0;
        manager.Settings.IMEIndicator.Size = 1;   // 下限16未満
        manager.Settings.Clock.FontSize = 500;     // 上限200超
        manager.Settings.Debug.PollingInterval = 10; // 下限50未満

        // Act
        manager.ValidateAndClampSettings();

        // Assert
        Assert.Equal(1.0, manager.Settings.IMEIndicator.Opacity);
        Assert.Equal(0.0, manager.Settings.Clock.Opacity);
        Assert.Equal(1.0, manager.Settings.MouseCursorIndicator.Opacity);
        Assert.Equal(16, manager.Settings.IMEIndicator.Size);
        Assert.Equal(200, manager.Settings.Clock.FontSize);
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
        manager.Settings.Clock.Width = 999;
        manager.Settings.Language = "ko";
        manager.Save();

        // Act
        manager.Reset();

        // Assert
        var defaults = new AppSettings();
        Assert.Equal(defaults.Clock.Width, manager.Settings.Clock.Width);
        Assert.Equal(defaults.Language, manager.Settings.Language);
    }
}
