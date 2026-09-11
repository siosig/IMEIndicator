// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;

namespace IMEIndicator.Models;

/// <summary>
/// マウスカーソル追従インジケーターの表示設定。
/// settings.json の "mouseCursorIndicator" 子オブジェクトに対応する
/// （data-model.md §1、contracts/settings-compat-contract.md）。
/// 値域チェック（Clamp）はこのクラスでは行わず、AppSettings.Clamp() でまとめて行う。
/// 現行 C++ 版 src/cpp/models/MouseCursorIndicatorSettings.h と等価。
/// </summary>
public sealed class MouseCursorIndicatorSettings
{
    /// <summary>インジケーターの表示可否。</summary>
    [JsonPropertyName("isVisible")]
    [JsonPropertyOrder(0)]
    public bool IsVisible { get; set; } = true;

    /// <summary>インジケーターの論理サイズ（px）。値域 20〜100（Clamp は AppSettings 側）。</summary>
    [JsonPropertyName("size")]
    [JsonPropertyOrder(1)]
    public double Size { get; set; } = 34.0;

    /// <summary>不透明度。値域 0.10〜1.00（Clamp は AppSettings 側）。</summary>
    [JsonPropertyName("opacity")]
    [JsonPropertyOrder(2)]
    public double Opacity { get; set; } = 0.9;

    /// <summary>カーソル位置からの X オフセット（px、論理ピクセル）。</summary>
    [JsonPropertyName("offsetX")]
    [JsonPropertyOrder(3)]
    public double OffsetX { get; set; } = 15.0;

    /// <summary>カーソル位置からの Y オフセット（px、論理ピクセル）。</summary>
    [JsonPropertyName("offsetY")]
    [JsonPropertyOrder(4)]
    public double OffsetY { get; set; } = 15.0;
}
