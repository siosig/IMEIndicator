// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;

namespace IMEIndicator.Models.Hotkey;

/// <summary>
/// AppSettings.HotkeySettings の型。HotkeyP 由来のホットキー設定一式を保持する。
/// 移植元: src/cpp/models/hotkey/HotkeySettings.h の struct HotkeySettings。
/// 上限（Hotkeys 最大 256 件、Categories 最大 32 件）はこのクラスでは保持・適用せず、
/// SettingsManager / AppSettings.Clamp()（T013 以降）でまとめて行う
/// （MouseCursorIndicatorSettings 等、他の永続化モデルと同じ方針）。
/// </summary>
public sealed class HotkeySettings
{
    /// <summary>登録済みホットキー一覧。最大 256 件（上限適用は Clamp 側）。</summary>
    [JsonPropertyName("hotkeys")]
    [JsonPropertyOrder(0)]
    public List<HotKeyEntry> Hotkeys { get; set; } = new();

    /// <summary>ユーザー定義カテゴリ一覧。最大 32 件（上限適用は Clamp 側）。</summary>
    [JsonPropertyName("categories")]
    [JsonPropertyOrder(1)]
    public List<HotkeyCategory> Categories { get; set; } = new();

    /// <summary>全ホットキーエントリ共通の動作設定。</summary>
    [JsonPropertyName("globalOptions")]
    [JsonPropertyOrder(2)]
    public HotkeyGlobalOptions GlobalOptions { get; set; } = new();
}
