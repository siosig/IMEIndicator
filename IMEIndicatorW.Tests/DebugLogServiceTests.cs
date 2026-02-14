using IMEIndicatorClock.Services;
using Xunit;

namespace IMEIndicatorW.Tests;

/// <summary>
/// DebugLogService / DbgLog のユニットテスト
/// static クラスの状態をテスト終了時に復元
/// </summary>
public class DebugLogServiceTests : IDisposable
{
    private readonly int _originalDebugLevel;
    private readonly PathStyle _originalPathStyle;

    public DebugLogServiceTests()
    {
        _originalDebugLevel = DebugLogService.DebugLevel;
        _originalPathStyle = DebugLogService.PathStyle;
    }

    public void Dispose()
    {
        DebugLogService.DebugLevel = _originalDebugLevel;
        DebugLogService.PathStyle = _originalPathStyle;
    }

    // ============================================================
    // DebugLevel プロパティ
    // ============================================================

    [Fact]
    public void DebugLevel_SetAndGet()
    {
        DebugLogService.DebugLevel = 5;
        Assert.Equal(5, DebugLogService.DebugLevel);

        DebugLogService.DebugLevel = -3;
        Assert.Equal(-3, DebugLogService.DebugLevel);

        DebugLogService.DebugLevel = 0;
        Assert.Equal(0, DebugLogService.DebugLevel);
    }

    // ============================================================
    // IsFileOutputEnabled
    // ============================================================

    [Fact]
    public void IsFileOutputEnabled_NegativeLevel_ReturnsTrue()
    {
        DebugLogService.DebugLevel = -5;
        Assert.True(DebugLogService.IsFileOutputEnabled);
    }

    [Fact]
    public void IsFileOutputEnabled_PositiveLevel_ReturnsFalse()
    {
        DebugLogService.DebugLevel = 5;
        Assert.False(DebugLogService.IsFileOutputEnabled);
    }

    [Fact]
    public void IsFileOutputEnabled_Zero_ReturnsFalse()
    {
        DebugLogService.DebugLevel = 0;
        Assert.False(DebugLogService.IsFileOutputEnabled);
    }

    // ============================================================
    // PathStyle プロパティ
    // ============================================================

    [Theory]
    [InlineData(PathStyle.FileOnly)]
    [InlineData(PathStyle.Parent1)]
    [InlineData(PathStyle.Parent2)]
    [InlineData(PathStyle.FullPath)]
    public void PathStyle_SetAndGet(PathStyle style)
    {
        DebugLogService.PathStyle = style;
        Assert.Equal(style, DebugLogService.PathStyle);
    }

    // ============================================================
    // LogFilePath
    // ============================================================

    [Fact]
    public void LogFilePath_ReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(DebugLogService.LogFilePath));
    }

    [Fact]
    public void LogFilePath_ContainsAppName()
    {
        Assert.Contains("IMEIndicatorClock", DebugLogService.LogFilePath);
    }

    // ============================================================
    // Log — レベルフィルタリング（例外が出ないことの確認）
    // ============================================================

    [Fact]
    public void Log_LevelAboveThreshold_DoesNotThrow()
    {
        DebugLogService.DebugLevel = 3;
        // レベル5はしきい値3を超えるので出力されないはず
        var ex = Record.Exception(() => DebugLogService.Log(5, "このメッセージは出力されない"));
        Assert.Null(ex);
    }

    [Fact]
    public void Log_LevelBelowThreshold_DoesNotThrow()
    {
        DebugLogService.DebugLevel = 5;
        // レベル3はしきい値5以下なので出力される
        var ex = Record.Exception(() => DebugLogService.Log(3, "このメッセージは出力される"));
        Assert.Null(ex);
    }

    [Fact]
    public void Log_LevelZero_DoesNotThrow()
    {
        DebugLogService.DebugLevel = 0;
        var ex = Record.Exception(() => DebugLogService.Log(1, "DebugLevel=0 では何も出力しない"));
        Assert.Null(ex);
    }

    // ============================================================
    // Error / Exception — 例外が出ないことの確認
    // ============================================================

    [Fact]
    public void Error_DoesNotThrow()
    {
        DebugLogService.DebugLevel = 5;
        var ex = Record.Exception(() => DebugLogService.Error("テストエラー"));
        Assert.Null(ex);
    }

    [Fact]
    public void Exception_DoesNotThrow()
    {
        DebugLogService.DebugLevel = 5;
        var testEx = new InvalidOperationException("テスト例外");
        var ex = Record.Exception(() => DebugLogService.Exception(testEx, "追加メッセージ"));
        Assert.Null(ex);
    }

    // ============================================================
    // DbgLog エイリアス
    // ============================================================

    [Fact]
    public void DbgLog_Aliases_DoNotThrow()
    {
        DebugLogService.DebugLevel = 5;

        var ex = Record.Exception(() =>
        {
            DbgLog.Log(3, "テスト");
            DbgLog.I("情報");
            DbgLog.W("警告");
            DbgLog.E("エラー");
            DbgLog.Ex(new Exception("テスト"), "追加");
        });

        Assert.Null(ex);
    }
}
