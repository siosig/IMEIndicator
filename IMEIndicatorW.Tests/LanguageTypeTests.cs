using IMEIndicatorClock.Services;
using Xunit;

namespace IMEIndicatorW.Tests;

/// <summary>
/// 言語タイプ判定のユニットテスト
/// </summary>
public class LanguageTypeTests
{
    [Theory]
    [InlineData(0x0411, LanguageType.Japanese)]       // 日本語
    [InlineData(0x0412, LanguageType.Korean)]         // 韓国語
    [InlineData(0x0804, LanguageType.ChineseSimplified)]  // 簡体字中国語
    [InlineData(0x0404, LanguageType.ChineseTraditional)] // 繁体字中国語
    [InlineData(0x0C04, LanguageType.ChineseTraditional)] // 繁体字中国語（香港）
    [InlineData(0x0409, LanguageType.English)]        // 英語（US）
    [InlineData(0x0809, LanguageType.English)]        // 英語（UK）
    [InlineData(0x041E, LanguageType.Thai)]           // タイ語
    [InlineData(0x042A, LanguageType.Vietnamese)]     // ベトナム語
    public void GetLanguageType_ReturnsCorrectLanguage(int langId, LanguageType expected)
    {
        // Act
        var result = IMEDetector_Common.GetLanguageType(langId);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0x0412)]  // 韓国語
    [InlineData(0x0812)]  // 韓国語バリエーション
    public void GetLanguageType_KoreanVariants_ReturnKorean(int langId)
    {
        // Act
        var result = IMEDetector_Common.GetLanguageType(langId);

        // Assert
        Assert.Equal(LanguageType.Korean, result);
    }

    [Fact]
    public void GetLanguageType_UnknownLangId_ReturnsOther()
    {
        // Arrange
        int unknownLangId = 0x9999;

        // Act
        var result = IMEDetector_Common.GetLanguageType(unknownLangId);

        // Assert
        Assert.Equal(LanguageType.Other, result);
    }
}
