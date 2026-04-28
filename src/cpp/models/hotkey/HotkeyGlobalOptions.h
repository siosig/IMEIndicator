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
#include <vector>

namespace imeindicator::models::hotkey {

// フックモード（永続化用文字列値）
// HotkeyP の useHook(int) と互換: 0=none / 1=low_level / 2=system / 3=auto
enum class HookMode : int {
    None         = 0,
    LowLevel     = 1,
    SystemHotKey = 2,
    Auto         = 3,
};

const char* hookModeToString(HookMode m) noexcept;
bool tryParseHookMode(std::string_view s, HookMode& out) noexcept;  // 大小無視

// HotkeySettings.globalOptions のネスト型。全エントリ共通の動作設定。
struct HotkeyGlobalOptions {
    HookMode                  hookMode                       = HookMode::Auto;
    bool                      distinguishLeftRightModifiers  = false;
    std::vector<std::wstring> foregroundExcludeProcesses;     // 0〜32 件、各 0〜256 文字
    int                       mouseDelayMs                   = 0;     // [0, 1000]
    bool                      playSoundOnExecution           = false;

    bool operator==(const HotkeyGlobalOptions&) const = default;
};

void to_json(nlohmann::json& j, const HotkeyGlobalOptions& o);
void from_json(const nlohmann::json& j, HotkeyGlobalOptions& o);

} // namespace imeindicator::models::hotkey
