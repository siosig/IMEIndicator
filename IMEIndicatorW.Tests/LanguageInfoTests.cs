using IMEIndicatorClock.Services;
using Xunit;

namespace IMEIndicatorW.Tests;

/// <summary>
/// LanguageInfoレコードのユニットテスト
/// </summary>
public class LanguageInfoTests
{
    [Fact]
    public void LanguageInfo_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var info1 = new LanguageInfo(LanguageType.Japanese, true);
        var info2 = new LanguageInfo(LanguageType.Japanese, true);
        var info3 = new LanguageInfo(LanguageType.Japanese, false);
        var info4 = new LanguageInfo(LanguageType.Korean, true);

        // Assert
        Assert.Equal(info1, info2);
        Assert.NotEqual(info1, info3);
        Assert.NotEqual(info1, info4);
    }

    [Theory]
    [InlineData(LanguageType.Japanese, true)]
    [InlineData(LanguageType.Korean, false)]
    [InlineData(LanguageType.ChineseSimplified, true)]
    [InlineData(LanguageType.English, false)]
    public void LanguageInfo_Properties_AreCorrect(LanguageType language, bool isIMEOn)
    {
        // Arrange & Act
        var info = new LanguageInfo(language, isIMEOn);

        // Assert
        Assert.Equal(language, info.Language);
        Assert.Equal(isIMEOn, info.IsIMEOn);
    }

    [Fact]
    public void LanguageInfo_With_CreatesNewInstance()
    {
        // Arrange
        var original = new LanguageInfo(LanguageType.Japanese, false);

        // Act
        var modified = original with { IsIMEOn = true };

        // Assert
        Assert.False(original.IsIMEOn);
        Assert.True(modified.IsIMEOn);
        Assert.Equal(original.Language, modified.Language);
    }
}
