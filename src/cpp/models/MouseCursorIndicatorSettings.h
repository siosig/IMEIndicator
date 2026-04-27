#pragma once

#include <algorithm>

#include <nlohmann/json.hpp>

namespace imeindicator::models {

// 既存 C# 版 AppSettings.MouseCursorIndicatorSettings と完全互換。
// settings-schema-v1.md / v2.md の "mouseCursorIndicator" 子オブジェクトに対応。
struct MouseCursorIndicatorSettings {
    bool   isVisible{true};
    double size{34.0};
    double opacity{0.9};
    double offsetX{15.0};
    double offsetY{15.0};

    // 値域 Clamp（spec data-model.md §バリデーションサマリ）
    void clamp()
    {
        size    = std::clamp(size,    20.0, 100.0);
        opacity = std::clamp(opacity,  0.1,   1.0);
        // offsetX / offsetY は spec 上は制限なし（描画時の境界補正で吸収）
    }

    bool operator==(const MouseCursorIndicatorSettings&) const = default;
};

// nlohmann::json アダプタ（camelCase 維持。空フィールドはデフォルト値で補完）
inline void to_json(nlohmann::json& j, const MouseCursorIndicatorSettings& s)
{
    j = nlohmann::json{
        {"isVisible", s.isVisible},
        {"size",      s.size},
        {"opacity",   s.opacity},
        {"offsetX",   s.offsetX},
        {"offsetY",   s.offsetY}
    };
}

inline void from_json(const nlohmann::json& j, MouseCursorIndicatorSettings& s)
{
    s.isVisible = j.value("isVisible", true);
    s.size      = j.value("size",      34.0);
    s.opacity   = j.value("opacity",   0.9);
    s.offsetX   = j.value("offsetX",   15.0);
    s.offsetY   = j.value("offsetY",   15.0);
}

} // namespace imeindicator::models
