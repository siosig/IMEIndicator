#pragma once
/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - HotkeyManager
 ホットキーエントリの配列管理・登録/解除・カテゴリ判定
*/

#include <vector>
#include <span>
#include "../../models/hotkey/HotKeyEntry.h"


namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// ホットキー配列管理クラス
class HotkeyManager {
public:
    HotkeyManager() = default;
    ~HotkeyManager() = default;

    // コピー禁止（ホットキーは唯一のオブジェクト）
    HotkeyManager(const HotkeyManager&)            = delete;
    HotkeyManager& operator=(const HotkeyManager&) = delete;

    // ムーブは許可
    HotkeyManager(HotkeyManager&&)            = default;
    HotkeyManager& operator=(HotkeyManager&&) = default;

    // ホットキーを追加して配列インデックスを返す
    [[nodiscard]] int addHotkey(const HotKeyEntry& hk);
    [[nodiscard]] int addHotkey(HotKeyEntry&& hk);

    // インデックスでホットキーを取得（範囲外は nullptr）
    [[nodiscard]] HotKeyEntry*       getHotkey(int index) noexcept;
    [[nodiscard]] const HotKeyEntry* getHotkey(int index) const noexcept;

    // ホットキーを削除（index 以降をシフト）
    void removeHotkey(int index);

    // すべてのホットキーを削除
    void clear() noexcept;

    // エントリ数
    [[nodiscard]] size_t count() const noexcept { return m_hotkeys.size(); }

    // すべてのエントリにアクセス
    [[nodiscard]] std::span<const HotKeyEntry> hotkeys() const noexcept { return m_hotkeys; }
    [[nodiscard]] std::span<HotKeyEntry>       hotkeys()       noexcept { return m_hotkeys; }

    // キーバインドの重複チェック
    // vkey + modifiers の組み合わせが既存エントリと重複するか
    [[nodiscard]] bool hasDuplicate(UINT vkey, UINT modifiers) const noexcept;

    // vkey + modifiers でホットキーを検索（見つからない場合は nullptr）
    [[nodiscard]] const HotKeyEntry* findHotkeyByKey(UINT vkey, UINT modifiers) const noexcept;
    [[nodiscard]] HotKeyEntry*       findHotkeyByKey(UINT vkey, UINT modifiers) noexcept;

    // カテゴリでホットキーをフィルタリング
    // 自動カテゴリ判定を適用して返す
    [[nodiscard]] std::vector<const HotKeyEntry*>
        getHotkeysByCategory(Category category) const;

    // ホットキーを HTK データからロード（既存エントリを置き換え）
    void loadFromHtkData(std::vector<HotKeyEntry> hotkeys);

    // AppSettings.hotkeySettings 経由でのロード（IMEIndicator 統合用）。
    // 既存エントリを置き換え、addHotkey の重複チェックは行わない（信頼された永続化データから）。
    // 上限 256 件を超える分は無視される（FR-001 / FR-022）。
    void loadFromHotkeySettings(std::span<const HotKeyEntry> hotkeys);

private:
    std::vector<HotKeyEntry> m_hotkeys;
};

} // namespace imeindicator::services::hotkey
