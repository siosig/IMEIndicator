/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
#include "HotkeySettings.h"

namespace imeindicator::models::hotkey {

namespace {
constexpr size_t MaxHotkeys    = 256;   // FR-001
constexpr size_t MaxCategories = 32;
} // namespace

void HotkeySettings::clamp() {
    // hotkeys 上限
    if (hotkeys.size() > MaxHotkeys) {
        hotkeys.resize(MaxHotkeys);
    }
    // categories 上限
    if (categories.size() > MaxCategories) {
        categories.resize(MaxCategories);
    }
    // 各エントリの category クランプは from_json 内で実施済み
    // globalOptions の mouseDelayMs / foregroundExcludeProcesses も from_json でクランプ
}

void to_json(nlohmann::json& j, const HotkeySettings& s) {
    nlohmann::json hotkeysJson = nlohmann::json::array();
    for (const auto& h : s.hotkeys) {
        nlohmann::json hj;
        to_json(hj, h);
        hotkeysJson.push_back(std::move(hj));
    }

    nlohmann::json categoriesJson = nlohmann::json::array();
    for (const auto& c : s.categories) {
        nlohmann::json cj;
        to_json(cj, c);
        categoriesJson.push_back(std::move(cj));
    }

    nlohmann::json globalJson;
    to_json(globalJson, s.globalOptions);

    j = nlohmann::json{
        {"hotkeys",       std::move(hotkeysJson)},
        {"categories",    std::move(categoriesJson)},
        {"globalOptions", std::move(globalJson)},
    };
}

void from_json(const nlohmann::json& j, HotkeySettings& s) {
    s.hotkeys.clear();
    if (auto it = j.find("hotkeys"); it != j.end() && it->is_array()) {
        for (const auto& elem : *it) {
            if (!elem.is_object()) continue;
            HotKeyEntry e;
            from_json(elem, e);
            s.hotkeys.push_back(std::move(e));
            if (s.hotkeys.size() >= MaxHotkeys) break;  // 上限に達したら以降を無視
        }
    }

    s.categories.clear();
    if (auto it = j.find("categories"); it != j.end() && it->is_array()) {
        for (const auto& elem : *it) {
            if (!elem.is_object()) continue;
            HotkeyCategory c;
            from_json(elem, c);
            s.categories.push_back(std::move(c));
            if (s.categories.size() >= MaxCategories) break;
        }
    }

    s.globalOptions = HotkeyGlobalOptions{};
    if (auto it = j.find("globalOptions"); it != j.end() && it->is_object()) {
        from_json(*it, s.globalOptions);
    }
}

} // namespace imeindicator::models::hotkey
