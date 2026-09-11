#pragma once

#include <nlohmann/json.hpp>

namespace imeindicator::models {

// 013-ime-corner-image: contracts/settings-schema-v4.md の "backgroundImage" サブオブジェクトに対応。
// 画面右上に表示する IME ON 背景画像の表示可否を永続化する（data-model.md §1）。
// MouseCursorIndicatorSettings とは独立（片方の変更で他方は変わらない）。
struct BackgroundImageSettings {
    bool isVisible{false};

    // 値域 Clamp（data-model.md §バリデーションサマリ）
    // 現状は bool のみのため no-op。将来 size / margin を追加した際の値域制約の置き場。
    void clamp() {}

    bool operator==(const BackgroundImageSettings&) const = default;
};

// nlohmann::json アダプタ（camelCase 維持）
inline void to_json(nlohmann::json& j, const BackgroundImageSettings& s)
{
    j = nlohmann::json{
        {"isVisible", s.isVisible}
    };
}

// "isVisible" が bool のときのみ採用。欠落・型不正（"yes" / 1 / null など）は既定値 false。
// j.value() は型不正で type_error.302 を投げるため使わず、is_boolean() で判定する。
// 未知キー（将来予約の size / margin / imagePath を含む）は無視する。
inline void from_json(const nlohmann::json& j, BackgroundImageSettings& s)
{
    s.isVisible = false;
    if (auto it = j.find("isVisible"); it != j.end() && it->is_boolean()) {
        s.isVisible = it->get<bool>();
    }
}

} // namespace imeindicator::models
