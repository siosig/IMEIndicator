using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace IMEIndicatorClock.Services;

/// <summary>
/// ローカライゼーション（多言語対応）サービス
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    private static readonly object _lock = new();

    private ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    /// <summary>
    /// サポートされる言語リスト
    /// </summary>
    public static readonly UILanguageInfo[] SupportedLanguages = new[]
    {
        new UILanguageInfo("en", "English"),
        new UILanguageInfo("ja", "日本語"),
        new UILanguageInfo("zh-CN", "简体中文"),
        new UILanguageInfo("zh-TW", "繁體中文"),
        new UILanguageInfo("ko", "한국어"),
        new UILanguageInfo("vi", "Tiếng Việt"),
        new UILanguageInfo("th", "ไทย"),
        new UILanguageInfo("hi", "हिन्दी"),
        new UILanguageInfo("bn", "বাংলা"),
        new UILanguageInfo("ta", "தமிழ்"),
        new UILanguageInfo("te", "తెలుగు"),
        new UILanguageInfo("ne", "नेपाली"),
        new UILanguageInfo("si", "සිංහල"),
        new UILanguageInfo("my", "မြန်မာ"),
        new UILanguageInfo("km", "ភាសាខ្មែរ"),
        new UILanguageInfo("lo", "ລາວ"),
        new UILanguageInfo("mn", "Монгол"),
        new UILanguageInfo("ar", "العربية"),
        new UILanguageInfo("fa", "فارسی"),
        new UILanguageInfo("uk", "Українська"),
        new UILanguageInfo("ru", "Русский")
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static LocalizationService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new LocalizationService();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 現在の言語コード
    /// </summary>
    public string CurrentLanguageCode => _currentCulture.Name;

    private LocalizationService()
    {
        _resourceManager = new ResourceManager(
            "IMEIndicatorClock.Resources.Strings.Strings",
            typeof(LocalizationService).Assembly);

        // デフォルトはシステム言語、サポートされていない場合は英語
        var systemCulture = CultureInfo.CurrentUICulture;
        _currentCulture = GetSupportedCulture(systemCulture.Name) ?? CultureInfo.GetCultureInfo("en");

        DbgLog.I($"LocalizationService 初期化: システム言語={systemCulture.Name}, 使用言語={_currentCulture.Name}");
    }

    /// <summary>
    /// 言語を設定する
    /// </summary>
    public void SetLanguage(string cultureCode)
    {
        try
        {
            var culture = GetSupportedCulture(cultureCode) ?? CultureInfo.GetCultureInfo("en");
            if (_currentCulture.Name != culture.Name)
            {
                var oldCulture = _currentCulture.Name;
                _currentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                OnPropertyChanged(nameof(CurrentLanguageCode));
                OnLanguageChanged();

                DbgLog.I($"言語を変更: {oldCulture} → {culture.Name}");
            }
        }
        catch (Exception ex)
        {
            DbgLog.W($"言語設定に失敗: {cultureCode} - {ex.Message}");
        }
    }

    /// <summary>
    /// 文字列リソースを取得する
    /// </summary>
    public string GetString(string key)
    {
        try
        {
            return _resourceManager.GetString(key, _currentCulture) ?? key;
        }
        catch (Exception ex)
        {
            DbgLog.Log(5, $"リソース取得失敗: key={key}, error={ex.Message}");
            return key;
        }
    }

    /// <summary>
    /// フォーマット付き文字列リソースを取得する
    /// </summary>
    public string GetString(string key, params object[] args)
    {
        try
        {
            var format = _resourceManager.GetString(key, _currentCulture) ?? key;
            return string.Format(format, args);
        }
        catch (Exception ex)
        {
            DbgLog.Log(5, $"リソース取得失敗（フォーマット）: key={key}, error={ex.Message}");
            return key;
        }
    }

    /// <summary>
    /// インデクサーによるアクセス（XAML Binding用）
    /// </summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// 言語変更イベント
    /// </summary>
    public event EventHandler? LanguageChanged;

    private void OnLanguageChanged()
    {
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// サポートされている言語のCultureInfoを取得
    /// </summary>
    private static CultureInfo? GetSupportedCulture(string cultureCode)
    {
        // 完全一致
        foreach (var lang in SupportedLanguages)
        {
            if (string.Equals(lang.Code, cultureCode, StringComparison.OrdinalIgnoreCase))
            {
                return CultureInfo.GetCultureInfo(lang.Code);
            }
        }

        // 言語部分のみで一致（例: ja-JP → ja）
        var languagePart = cultureCode.Split('-')[0];
        foreach (var lang in SupportedLanguages)
        {
            if (string.Equals(lang.Code.Split('-')[0], languagePart, StringComparison.OrdinalIgnoreCase))
            {
                return CultureInfo.GetCultureInfo(lang.Code);
            }
        }

        return null;
    }
}

/// <summary>
/// 言語情報
/// </summary>
public class UILanguageInfo
{
    public string Code { get; }
    public string DisplayName { get; }

    public UILanguageInfo(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}
