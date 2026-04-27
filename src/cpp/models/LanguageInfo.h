#pragma once

#include <cstddef>
#include <functional>

namespace imeindicator::models {

// 既存 C# 版 LanguageTypes.cs を踏襲（English, Japanese の 2 値）
// data-model.md では Other を含む 3 値だが、現行ロジックは 2 値のみ参照しているため
// Other は Japanese 以外として暗黙に扱う（拡張時に LanguageType::Other を追加可能）。
enum class LanguageType : int {
    English  = 0,
    Japanese = 1
};

// IME 状態を表す不変の値オブジェクト。
// 既存 C# 版の `readonly record struct LanguageInfo` と同等。
struct LanguageInfo {
    LanguageType language{LanguageType::English};
    bool isImeOn{false};

    constexpr bool operator==(const LanguageInfo& other) const noexcept
    {
        return language == other.language && isImeOn == other.isImeOn;
    }
    constexpr bool operator!=(const LanguageInfo& other) const noexcept
    {
        return !(*this == other);
    }
};

} // namespace imeindicator::models

// 状態変化検出のためのハッシュ
namespace std {
template <>
struct hash<imeindicator::models::LanguageInfo> {
    size_t operator()(const imeindicator::models::LanguageInfo& v) const noexcept
    {
        return (static_cast<size_t>(v.language) << 1) | (v.isImeOn ? 1u : 0u);
    }
};
} // namespace std
