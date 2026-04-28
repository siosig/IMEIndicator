#pragma once
/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */

#include <chrono>
#include <string>
#include <vector>
#include <windows.h>

namespace imeindicator::models::hotkey {

// マクロ実行時の中間表現。永続化されない（HotKeyEntry.args の文字列がソース）。
// MacroParser::parseMacro で生成され、CommandExecutor::executeMacro で消費される。
struct MacroDefinition {
    std::wstring        source;                       // 元のマクロ文字列
    std::vector<INPUT>  inputs;                       // SendInput 用の入力列
    std::chrono::milliseconds totalDuration{0};       // 推定実行時間（\sleep の合計）
    bool                repeat       = false;         // \rep <count> の指定あり
    int                 repeatCount  = 1;             // [1, 100]
};

} // namespace imeindicator::models::hotkey
