// Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
// Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models.Hotkey;

namespace IMEIndicator.Services.Hotkey;

/// <summary>
/// ホットキーエントリの配列管理・検索・カテゴリ判定を行う。
/// 移植元: src/cpp/services/hotkey/HotkeyManager.h / HotkeyManager.cpp の
/// class HotkeyManager（1:1 移植。メンバー構成・挙動を変えていない）。
/// </summary>
/// <remarks>
/// スレッドセーフではない（移植元 C++ 版も <c>std::vector</c> を素のまま保持するだけで
/// 排他制御を持たない）。呼び出し側での直列化・排他は上位層
/// （将来実装の HotkeyService.cs、T068）の責務とする。
/// C++ 版はコピー禁止・ムーブ許可だが、C# の class は既定で参照型のため
/// この制約は不要（sealed class にするのみで同等の「唯一のオブジェクト」性を確保できる）。
/// </remarks>
public sealed class HotkeyManager
{
    private readonly List<HotKeyEntry> _hotkeys = [];

    /// <summary>登録済みエントリ数。移植元: HotkeyManager::count()。</summary>
    public int Count => _hotkeys.Count;

    /// <summary>
    /// すべてのエントリを読み取り専用ビューで返す。移植元: HotkeyManager::hotkeys()。
    /// 返される各要素（<see cref="HotKeyEntry"/>）自体は参照型のため、要素のプロパティを
    /// 書き換えることは引き続き可能（C++ 版の非 const オーバーロードに相当する挙動）。
    /// リストへの追加・削除はこのビュー経由ではできない。
    /// </summary>
    public IReadOnlyList<HotKeyEntry> Hotkeys => _hotkeys;

    /// <summary>
    /// ホットキーを追加し、追加後の配列インデックスを返す。
    /// 移植元: HotkeyManager::addHotkey()。重複チェックは行わない
    /// （C++ 版も同様。呼び出し側が事前に <see cref="HasDuplicate"/> を使うこと）。
    /// </summary>
    /// <param name="hotkey">追加するエントリ（null 不可）。</param>
    /// <returns>追加後の配列インデックス（末尾に追加されるため常に旧 <see cref="Count"/> と一致）。</returns>
    public int AddHotkey(HotKeyEntry hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        int index = _hotkeys.Count;
        _hotkeys.Add(hotkey);
        return index;
    }

    /// <summary>
    /// インデックスでホットキーを取得する。範囲外の場合は null。
    /// 移植元: HotkeyManager::getHotkey()。
    /// </summary>
    public HotKeyEntry? GetHotkey(int index)
    {
        if (index < 0 || index >= _hotkeys.Count)
        {
            return null;
        }

        return _hotkeys[index];
    }

    /// <summary>
    /// 指定インデックスのエントリを削除する（以降の要素は前方へシフトする）。
    /// 範囲外の場合は何もしない。移植元: HotkeyManager::removeHotkey()。
    /// </summary>
    public void RemoveHotkey(int index)
    {
        if (index < 0 || index >= _hotkeys.Count)
        {
            return;
        }

        _hotkeys.RemoveAt(index);
    }

    /// <summary>すべてのホットキーを削除する。移植元: HotkeyManager::clear()。</summary>
    public void Clear()
    {
        _hotkeys.Clear();
    }

    /// <summary>
    /// vkey + modifiers の組み合わせが既存エントリと重複するかどうかを調べる。
    /// 移植元: HotkeyManager::hasDuplicate()。
    /// </summary>
    public bool HasDuplicate(int vkey, int modifiers)
    {
        foreach (HotKeyEntry hk in _hotkeys)
        {
            if (hk.Vkey == vkey && hk.Modifiers == modifiers)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// vkey + modifiers の完全一致でホットキーを検索する（先頭からの線形探索）。
    /// 見つからない場合は null。移植元: HotkeyManager::findHotkeyByKey()。
    /// </summary>
    /// <remarks>
    /// C++ 版の実装は vkey と modifiers の単純な完全一致のみで判定しており、
    /// scanCode / distinguishLR / foregroundExcludeProcesses は一切参照しない。
    /// これらは HotKeyEntry / HotkeyGlobalOptions には永続化されているものの、
    /// 現行 C++ 版の実行時には（HotkeyManager 経由では）どこからも参照されない設定項目であるため、
    /// 本メソッドでも新たに参照するロジックを追加していない（C++ 版の実際の動作に忠実な移植）。
    /// </remarks>
    public HotKeyEntry? FindHotkeyByKey(int vkey, int modifiers)
    {
        foreach (HotKeyEntry hk in _hotkeys)
        {
            if (hk.Vkey == vkey && hk.Modifiers == modifiers)
            {
                return hk;
            }
        }

        return null;
    }

    /// <summary>
    /// 指定カテゴリに属するホットキー一覧を返す（<see cref="HotKeyEntry.AutoCategory"/> による
    /// 自動判定を適用する）。移植元: HotkeyManager::getHotkeysByCategory()。
    /// </summary>
    public IReadOnlyList<HotKeyEntry> GetHotkeysByCategory(HotkeyCategoryKind category)
    {
        var result = new List<HotKeyEntry>();
        foreach (HotKeyEntry hk in _hotkeys)
        {
            if (hk.AutoCategory() == category)
            {
                result.Add(hk);
            }
        }

        return result;
    }

    /// <summary>
    /// HTK データ由来のホットキー一覧で既存エントリを置き換える。
    /// 移植元: HotkeyManager::loadFromHtkData()。重複チェックは行わない。
    /// </summary>
    public void LoadFromHtkData(IEnumerable<HotKeyEntry> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        _hotkeys.Clear();
        _hotkeys.AddRange(hotkeys);
    }

    /// <summary>
    /// AppSettings.HotkeySettings.Hotkeys 由来のホットキー一覧で既存エントリを置き換える
    /// （IMEIndicator 統合用）。既存エントリを置き換え、<see cref="AddHotkey"/> のような
    /// 重複チェックは行わない（信頼された永続化データからのロードのため）。
    /// 上限 256 件を超える分は無視する（FR-001 / FR-022）。
    /// 移植元: HotkeyManager::loadFromHotkeySettings()。
    /// </summary>
    public void LoadFromHotkeySettings(IReadOnlyList<HotKeyEntry> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        const int maxHotkeys = 256; // FR-001 / FR-022

        _hotkeys.Clear();
        int limit = Math.Min(hotkeys.Count, maxHotkeys);
        _hotkeys.EnsureCapacity(limit);
        for (int i = 0; i < limit; i++)
        {
            _hotkeys.Add(hotkeys[i]);
        }
    }
}
