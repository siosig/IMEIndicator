namespace IMEIndicatorClock.Services;

/// <summary>
/// 言語タイプ
/// </summary>
public enum LanguageType
{
    English,
    Japanese,
    Korean,
    ChineseSimplified,
    ChineseTraditional,
    Vietnamese,
    Thai,
    Hindi,
    Bengali,
    Tamil,
    Telugu,
    Nepali,
    Sinhala,
    Myanmar,
    Khmer,
    Lao,
    Mongolian,
    Arabic,
    Persian,
    Hebrew,
    Ukrainian,
    Russian,
    Greek,
    Other
}

/// <summary>
/// 言語情報
/// </summary>
public record LanguageInfo(LanguageType Language, bool IsIMEOn);
