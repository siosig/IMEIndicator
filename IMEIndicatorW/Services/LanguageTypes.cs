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
/// 言語情報（値型によりヒープアロケーションを回避）
/// </summary>
public readonly record struct LanguageInfo(LanguageType Language, bool IsIMEOn);
