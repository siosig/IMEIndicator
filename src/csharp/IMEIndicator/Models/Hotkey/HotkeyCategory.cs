// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;

namespace IMEIndicator.Models.Hotkey;

/// <summary>
/// ユーザー定義のホットキーカテゴリ。
/// 固定カテゴリ（id 0〜11、HotkeyP 由来の固定名）は永続化されず動的判定のため、
/// ここで扱うのは id 12〜39 のユーザー定義カテゴリのみ。
/// 移植元: src/cpp/models/hotkey/HotkeyCategory.h の struct HotkeyCategory。
/// </summary>
public sealed class HotkeyCategory
{
    /// <summary>カテゴリ ID。値域 [12, 39] でユニーク。</summary>
    [JsonPropertyName("id")]
    [JsonPropertyOrder(0)]
    public int Id { get; set; } = 12;

    /// <summary>カテゴリ名。1〜32 文字、空白のみは禁止。</summary>
    [JsonPropertyName("name")]
    [JsonPropertyOrder(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>表示順。値域 [0, 999]。</summary>
    [JsonPropertyName("displayOrder")]
    [JsonPropertyOrder(2)]
    public int DisplayOrder { get; set; } = 0;

    /// <summary>カテゴリの表示色。"#RRGGBB" 形式、または空文字。</summary>
    [JsonPropertyName("colorLabel")]
    [JsonPropertyOrder(3)]
    public string ColorLabel { get; set; } = string.Empty;
}
