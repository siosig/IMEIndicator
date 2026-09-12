// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;
using IMEIndicator.App;

namespace IMEIndicator.Models;

/// <summary>
/// 画面右上に表示する IME ON 背景画像の表示設定。
/// settings.json の "backgroundImage" 子オブジェクトに対応する
/// （specs/015-split-appearance-settings/data-model.md §1）。
/// MouseCursorIndicatorSettings とは独立（片方の変更で他方は変わらない、FR-003）。
/// 値域チェック（Clamp）はこのクラスでは行わず、AppSettings.Clamp() でまとめて行う
/// （MouseCursorIndicatorSettings と同じ方針）。
/// </summary>
public sealed class BackgroundImageSettings
{
    /// <summary>背景画像の表示可否。</summary>
    [JsonPropertyName("isVisible")]
    [JsonPropertyOrder(0)]
    public bool IsVisible { get; set; }

    /// <summary>
    /// 背景画像の論理サイズ（px、一辺）。値域 32〜512（Clamp は AppSettings 側）。
    /// 既定値は本機能導入前の固定値（AppConstants.BackgroundImageLogicalSize）と同じにし、
    /// 既存ユーザーの見た目を変えない（FR-009）。
    /// </summary>
    [JsonPropertyName("size")]
    [JsonPropertyOrder(1)]
    public double Size { get; set; } = AppConstants.BackgroundImageLogicalSize;

    /// <summary>
    /// 不透明度。値域 0.10〜1.00（Clamp は AppSettings 側）。
    /// 既定値は本機能導入前の描画（Present に固定値 255 = 完全不透明）と同じ 1.0（FR-009）。
    /// </summary>
    [JsonPropertyName("opacity")]
    [JsonPropertyOrder(2)]
    public double Opacity { get; set; } = 1.0;
}
