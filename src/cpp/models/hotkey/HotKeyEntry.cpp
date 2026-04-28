/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
#include "HotKeyEntry.h"

#include "../../win32/UnicodeUtil.h"

#include <algorithm>
#include <nlohmann/json.hpp>

namespace imeindicator::models::hotkey {

namespace {

class ValidationErrorCategory : public std::error_category {
public:
    const char* name() const noexcept override { return "imeindicator.hotkey.validation"; }
    std::string message(int ev) const override {
        switch (static_cast<ValidationError>(ev)) {
            case ValidationError::ExeAndCmdConflict:
                return "exe and cmd are mutually exclusive";
            case ValidationError::NoTrigger:
                return "neither vkey nor cmd is set (no trigger)";
            case ValidationError::InvalidCommandId:
                return "cmd is in reserved range (121-199) or undefined (>=300)";
            case ValidationError::InvalidCategory:
                return "category is out of [0, 39] range";
        }
        return "unknown validation error";
    }
};

const ValidationErrorCategory& validationErrorCategory() noexcept {
    static const ValidationErrorCategory inst;
    return inst;
}

} // namespace

std::error_code make_error_code(ValidationError e) noexcept {
    return {static_cast<int>(e), validationErrorCategory()};
}

std::expected<void, ValidationError> HotKeyEntry::validate() const noexcept {
    // exe と cmd は排他: exe 非空かつ cmd >= 0 はエラー
    if (!exe.empty() && cmd >= 0) {
        return std::unexpected(ValidationError::ExeAndCmdConflict);
    }

    // トリガーチェック: vkey == 0 かつ cmd < 0 はエラー（autoStart=true で起動時実行のみは許容）
    if (vkey == 0 && cmd < 0 && !autoStart) {
        return std::unexpected(ValidationError::NoTrigger);
    }

    // cmd 範囲チェック: -1（非コマンド）/ [0, 120]（HotkeyP）/ [200, 299]（IMEIndicator 拡張）
    if (cmd >= 0) {
        const bool inHotkeyPRange    = (cmd >= 0   && cmd <= 120);
        const bool inImeIndicatorExt = (cmd >= 200 && cmd <= 299);
        if (!inHotkeyPRange && !inImeIndicatorExt) {
            return std::unexpected(ValidationError::InvalidCommandId);
        }
    }

    // category 範囲チェック（クランプは呼び出し側で）
    if (category < 0 || category > 39) {
        return std::unexpected(ValidationError::InvalidCategory);
    }

    return {};
}

void to_json(nlohmann::json& j, const HotKeyEntry& e) {
    j = nlohmann::json{
        {"note",          win32::wideToUtf8(e.note)},
        {"icon",          e.icon},
        {"category",      e.category},
        {"exe",           win32::wideToUtf8(e.exe)},
        {"args",          win32::wideToUtf8(e.args)},
        {"dir",           win32::wideToUtf8(e.dir)},
        {"sound",         win32::wideToUtf8(e.sound)},
        {"modifiers",     e.modifiers},
        {"vkey",          e.vkey},
        {"scanCode",      e.scanCode},
        {"cmd",           e.cmd},
        {"cmdShow",       static_cast<int>(e.cmdShow)},
        {"opacity",       e.opacity},
        {"priority",      static_cast<int>(e.priority)},
        {"disable",       e.disable},
        {"multInst",      e.multInst},
        {"trayMenu",      e.trayMenu},
        {"autoStart",     e.autoStart},
        {"ask",           e.ask},
        {"delay",         e.delay},
        {"admin",         e.admin},
        {"distinguishLR", false},  // 互換用フィールド（HotKeyEntry に専用フラグなし、グローバル設定で代替）
    };
}

void from_json(const nlohmann::json& j, HotKeyEntry& e) {
    auto utf8FieldToWide = [&](const char* key) -> std::wstring {
        auto it = j.find(key);
        if (it != j.end() && it->is_string()) {
            return win32::utf8ToWide(it->get<std::string>());
        }
        return {};
    };

    e.note  = utf8FieldToWide("note");
    e.exe   = utf8FieldToWide("exe");
    e.args  = utf8FieldToWide("args");
    e.dir   = utf8FieldToWide("dir");
    e.sound = utf8FieldToWide("sound");

    e.icon      = j.value("icon", 0);
    e.category  = j.value("category", 0);
    e.modifiers = j.value("modifiers", 0u);
    e.vkey      = j.value("vkey", 0u);
    e.scanCode  = j.value("scanCode", 0ul);
    e.cmd       = j.value("cmd", -1);

    e.cmdShow = WindowShow::Normal;
    if (auto it = j.find("cmdShow"); it != j.end() && it->is_number_integer()) {
        const int v = it->get<int>();
        if (v >= 0 && v <= 2) e.cmdShow = static_cast<WindowShow>(v);
    }

    e.opacity = std::clamp(j.value("opacity", 0), 0, 255);

    e.priority = ProcessPriorityLevel::Normal;
    if (auto it = j.find("priority"); it != j.end() && it->is_number_integer()) {
        const int v = it->get<int>();
        if (v >= 0 && v <= 5) e.priority = static_cast<ProcessPriorityLevel>(v);
    }

    e.disable   = j.value("disable",   false);
    e.multInst  = j.value("multInst",  false);
    e.trayMenu  = j.value("trayMenu",  false);
    e.autoStart = j.value("autoStart", false);
    e.ask       = j.value("ask",       false);
    e.delay     = j.value("delay",     false);
    e.admin     = j.value("admin",     false);

    // category クランプ（[0, 39]）
    if (e.category < 0 || e.category > 39) e.category = 0;

    // note 切り詰め（256 文字）
    constexpr size_t MaxNote = 256;
    if (e.note.size() > MaxNote) e.note.resize(MaxNote);
}

} // namespace imeindicator::models::hotkey
