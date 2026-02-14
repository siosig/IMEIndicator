using IMEIndicatorClock.Services;
using Xunit;

namespace IMEIndicatorW.Tests;

/// <summary>
/// LocalizationServiceのユニットテスト
/// シングルトンの状態をテスト終了時に復元
/// </summary>
public class LocalizationServiceTests : IDisposable
{
    private readonly string _originalLanguage;

    public LocalizationServiceTests()
    {
        _originalLanguage = LocalizationService.Instance.CurrentLanguageCode;
    }

    public void Dispose()
    {
        // テスト後に元の言語に戻す
        LocalizationService.Instance.SetLanguage(_originalLanguage);
    }

    // ============================================================
    // SupportedLanguages
    // ============================================================

    [Fact]
    public void SupportedLanguages_Contains21Languages()
    {
        Assert.Equal(21, LocalizationService.SupportedLanguages.Length);
    }

    [Fact]
    public void SupportedLanguages_ContainsEnglish()
    {
        Assert.Contains(LocalizationService.SupportedLanguages,
            lang => lang.Code == "en");
    }

    [Fact]
    public void SupportedLanguages_ContainsJapanese()
    {
        Assert.Contains(LocalizationService.SupportedLanguages,
            lang => lang.Code == "ja");
    }

    // ============================================================
    // SetLanguage
    // ============================================================

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void SetLanguage_SupportedLanguage_ChangesLanguage(string code)
    {
        LocalizationService.Instance.SetLanguage(code);

        Assert.Equal(code, LocalizationService.Instance.CurrentLanguageCode);
    }

    [Fact]
    public void SetLanguage_UnsupportedLanguage_FallsBackToEnglish()
    {
        LocalizationService.Instance.SetLanguage("xx-YY");

        Assert.Equal("en", LocalizationService.Instance.CurrentLanguageCode);
    }

    // ============================================================
    // GetString
    // ============================================================

    [Fact]
    public void GetString_AppTitle_ReturnsNonEmpty()
    {
        LocalizationService.Instance.SetLanguage("en");
        var title = LocalizationService.Instance.GetString("AppTitle");

        Assert.False(string.IsNullOrEmpty(title));
    }

    [Fact]
    public void GetString_NonExistentKey_ReturnsKey()
    {
        var key = "ThisKeyDoesNotExist_12345";
        var result = LocalizationService.Instance.GetString(key);

        Assert.Equal(key, result);
    }

    // ============================================================
    // インデクサー
    // ============================================================

    [Fact]
    public void Indexer_ReturnsNonEmpty()
    {
        LocalizationService.Instance.SetLanguage("en");
        var title = LocalizationService.Instance["AppTitle"];

        Assert.False(string.IsNullOrEmpty(title));
    }

    // ============================================================
    // LanguageChanged イベント
    // ============================================================

    [Fact]
    public void LanguageChanged_Event_Fires()
    {
        // 初期状態を英語にしておく
        LocalizationService.Instance.SetLanguage("en");

        bool fired = false;
        void Handler(object? sender, EventArgs e) => fired = true;

        LocalizationService.Instance.LanguageChanged += Handler;
        try
        {
            LocalizationService.Instance.SetLanguage("ja");
            Assert.True(fired);
        }
        finally
        {
            LocalizationService.Instance.LanguageChanged -= Handler;
        }
    }

    [Fact]
    public void SetLanguage_SameLanguage_DoesNotFireEvent()
    {
        LocalizationService.Instance.SetLanguage("en");

        bool fired = false;
        void Handler(object? sender, EventArgs e) => fired = true;

        LocalizationService.Instance.LanguageChanged += Handler;
        try
        {
            LocalizationService.Instance.SetLanguage("en");
            Assert.False(fired);
        }
        finally
        {
            LocalizationService.Instance.LanguageChanged -= Handler;
        }
    }
}
