#pragma once
/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */

#include <nlohmann/json.hpp>
#include <string>

namespace imeindicator::models::hotkey {

// ユーザー定義カテゴリ。
// 固定カテゴリ（id 0〜11、HotKeyEntry::Category enum 由来）は永続化されず動的判定。
// ここで管理するのは id 12〜39 のユーザー定義のみ。
struct HotkeyCategory {
    int          id           = 12;        // [12, 39] ユニーク
    std::wstring name;                     // 1〜32 文字、空白のみ禁止
    int          displayOrder = 0;         // [0, 999]
    std::wstring colorLabel;               // "#RRGGBB" 形式または空

    bool operator==(const HotkeyCategory&) const = default;
};

void to_json(nlohmann::json& j, const HotkeyCategory& c);
void from_json(const nlohmann::json& j, HotkeyCategory& c);

} // namespace imeindicator::models::hotkey
