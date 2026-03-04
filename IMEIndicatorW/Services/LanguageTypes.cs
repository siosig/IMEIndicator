namespace IMEIndicatorClock.Services;

/// <summary>
/// 言語タイプ（日本語IME専用に簡素化）
/// </summary>
public enum LanguageType
{
    English,
    Japanese,
    Other
}

/// <summary>
/// 言語情報
/// </summary>
public record LanguageInfo(LanguageType Language, bool IsIMEOn);
