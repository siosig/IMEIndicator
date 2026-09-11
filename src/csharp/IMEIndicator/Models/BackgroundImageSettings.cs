// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;

namespace IMEIndicator.Models;

/// <summary>
/// 画面右上に表示する IME ON 背景画像の表示設定。
/// settings.json の "backgroundImage" 子オブジェクトに対応する（data-model.md §1）。
/// MouseCursorIndicatorSettings とは独立（片方の変更で他方は変わらない）。
/// 現行 C++ 版 src/cpp/models/BackgroundImageSettings.h と等価。
/// 値域チェック（Clamp）はこのクラスでは行わない（bool のみのため対象がなく、
/// 将来 size / margin 等を追加した際は AppSettings.Clamp() 側で行う）。
/// </summary>
public sealed class BackgroundImageSettings
{
    /// <summary>背景画像の表示可否。</summary>
    [JsonPropertyName("isVisible")]
    [JsonPropertyOrder(0)]
    public bool IsVisible { get; set; }
}
