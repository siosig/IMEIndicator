/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
#include "HotkeyCategory.h"

#include "../../win32/UnicodeUtil.h"

namespace imeindicator::models::hotkey {

void to_json(nlohmann::json& j, const HotkeyCategory& c) {
    j = nlohmann::json{
        {"id",           c.id},
        {"name",         win32::wideToUtf8(c.name)},
        {"displayOrder", c.displayOrder},
        {"colorLabel",   win32::wideToUtf8(c.colorLabel)},
    };
}

void from_json(const nlohmann::json& j, HotkeyCategory& c) {
    c.id           = j.value("id", 12);
    c.displayOrder = j.value("displayOrder", 0);
    if (auto it = j.find("name"); it != j.end() && it->is_string()) {
        c.name = win32::utf8ToWide(it->get<std::string>());
    }
    if (auto it = j.find("colorLabel"); it != j.end() && it->is_string()) {
        c.colorLabel = win32::utf8ToWide(it->get<std::string>());
    }
}

} // namespace imeindicator::models::hotkey
