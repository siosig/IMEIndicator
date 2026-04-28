/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - HotkeyManager 実装
*/

#include "HotkeyManager.h"
#include <algorithm>

int HotkeyManager::addHotkey(const HotKeyEntry& hk)
{
    int index = static_cast<int>(m_hotkeys.size());
    m_hotkeys.push_back(hk);
    return index;
}

int HotkeyManager::addHotkey(HotKeyEntry&& hk)
{
    int index = static_cast<int>(m_hotkeys.size());
    m_hotkeys.push_back(std::move(hk));
    return index;
}

HotKeyEntry* HotkeyManager::getHotkey(int index) noexcept
{
    if (index < 0 || static_cast<size_t>(index) >= m_hotkeys.size()) return nullptr;
    return &m_hotkeys[static_cast<size_t>(index)];
}

const HotKeyEntry* HotkeyManager::getHotkey(int index) const noexcept
{
    if (index < 0 || static_cast<size_t>(index) >= m_hotkeys.size()) return nullptr;
    return &m_hotkeys[static_cast<size_t>(index)];
}

void HotkeyManager::removeHotkey(int index)
{
    if (index < 0 || static_cast<size_t>(index) >= m_hotkeys.size()) return;
    m_hotkeys.erase(m_hotkeys.begin() + index);
}

void HotkeyManager::clear() noexcept
{
    m_hotkeys.clear();
}

bool HotkeyManager::hasDuplicate(UINT vkey, UINT modifiers) const noexcept
{
    return std::any_of(m_hotkeys.begin(), m_hotkeys.end(),
        [vkey, modifiers](const HotKeyEntry& hk) {
            return hk.vkey == vkey && hk.modifiers == modifiers;
        });
}

const HotKeyEntry* HotkeyManager::findHotkeyByKey(UINT vkey, UINT modifiers) const noexcept
{
    for (const auto& hk : m_hotkeys) {
        if (hk.vkey == vkey && hk.modifiers == modifiers) return &hk;
    }
    return nullptr;
}

HotKeyEntry* HotkeyManager::findHotkeyByKey(UINT vkey, UINT modifiers) noexcept
{
    for (auto& hk : m_hotkeys) {
        if (hk.vkey == vkey && hk.modifiers == modifiers) return &hk;
    }
    return nullptr;
}

std::vector<const HotKeyEntry*>
HotkeyManager::getHotkeysByCategory(Category category) const
{
    std::vector<const HotKeyEntry*> result;
    for (const auto& hk : m_hotkeys) {
        if (hk.autoCategory() == category) {
            result.push_back(&hk);
        }
    }
    return result;
}

void HotkeyManager::loadFromHtkData(std::vector<HotKeyEntry> hotkeys)
{
    m_hotkeys = std::move(hotkeys);
}
