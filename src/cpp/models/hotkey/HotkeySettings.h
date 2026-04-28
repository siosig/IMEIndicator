#pragma once
/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */

#include "HotKeyEntry.h"
#include "HotkeyCategory.h"
#include "HotkeyGlobalOptions.h"

#include <nlohmann/json.hpp>
#include <vector>

namespace imeindicator::models::hotkey {

// AppSettings.hotkeySettings の型。HotkeyP 由来のホットキー設定一式を保持する。
// contracts/settings-schema-v3.md §hotkeySettings サブスキーマ準拠。
struct HotkeySettings {
    std::vector<HotKeyEntry>     hotkeys;          // 0〜256 件（FR-001）
    std::vector<HotkeyCategory>  categories;       // 0〜32 件
    HotkeyGlobalOptions          globalOptions;

    // 上限超過分の切り捨て・各エントリの category クランプ
    void clamp();

    bool operator==(const HotkeySettings&) const = default;
};

void to_json(nlohmann::json& j, const HotkeySettings& s);
void from_json(const nlohmann::json& j, HotkeySettings& s);

} // namespace imeindicator::models::hotkey
