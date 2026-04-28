/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
#include "HotkeyGlobalOptions.h"

#include "../../win32/UnicodeUtil.h"

#include <algorithm>
#include <cctype>

namespace imeindicator::models::hotkey {

const char* hookModeToString(HookMode m) noexcept {
    switch (m) {
        case HookMode::None:         return "none";
        case HookMode::LowLevel:     return "low_level";
        case HookMode::SystemHotKey: return "system";
        case HookMode::Auto:         return "auto";
    }
    return "auto";
}

bool tryParseHookMode(std::string_view s, HookMode& out) noexcept {
    auto eq = [](std::string_view a, std::string_view b) {
        if (a.size() != b.size()) return false;
        for (size_t i = 0; i < a.size(); ++i) {
            if (std::tolower(static_cast<unsigned char>(a[i])) !=
                std::tolower(static_cast<unsigned char>(b[i]))) return false;
        }
        return true;
    };
    if (eq(s, "none"))      { out = HookMode::None;         return true; }
    if (eq(s, "low_level")) { out = HookMode::LowLevel;     return true; }
    if (eq(s, "lowlevel"))  { out = HookMode::LowLevel;     return true; }  // 別表記
    if (eq(s, "system"))    { out = HookMode::SystemHotKey; return true; }
    if (eq(s, "auto"))      { out = HookMode::Auto;         return true; }
    return false;
}

void to_json(nlohmann::json& j, const HotkeyGlobalOptions& o) {
    nlohmann::json excludes = nlohmann::json::array();
    for (const auto& p : o.foregroundExcludeProcesses) {
        excludes.push_back(win32::wideToUtf8(p));
    }
    j = nlohmann::json{
        {"hookMode",                       hookModeToString(o.hookMode)},
        {"distinguishLeftRightModifiers",  o.distinguishLeftRightModifiers},
        {"foregroundExcludeProcesses",     std::move(excludes)},
        {"mouseDelayMs",                   o.mouseDelayMs},
        {"playSoundOnExecution",           o.playSoundOnExecution},
    };
}

void from_json(const nlohmann::json& j, HotkeyGlobalOptions& o) {
    o.hookMode = HookMode::Auto;
    if (auto it = j.find("hookMode"); it != j.end() && it->is_string()) {
        HookMode m = HookMode::Auto;
        if (tryParseHookMode(it->get<std::string>(), m)) {
            o.hookMode = m;
        }
    }

    o.distinguishLeftRightModifiers = j.value("distinguishLeftRightModifiers", false);

    o.foregroundExcludeProcesses.clear();
    if (auto it = j.find("foregroundExcludeProcesses");
        it != j.end() && it->is_array()) {
        for (const auto& elem : *it) {
            if (elem.is_string()) {
                o.foregroundExcludeProcesses.push_back(
                    win32::utf8ToWide(elem.get<std::string>()));
            }
        }
        // 上限 32 件
        if (o.foregroundExcludeProcesses.size() > 32) {
            o.foregroundExcludeProcesses.resize(32);
        }
    }

    o.mouseDelayMs = j.value("mouseDelayMs", 0);
    o.mouseDelayMs = std::clamp(o.mouseDelayMs, 0, 1000);

    o.playSoundOnExecution = j.value("playSoundOnExecution", false);
}

} // namespace imeindicator::models::hotkey
