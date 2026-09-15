// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;

namespace IMEIndicator.Models;

/// <summary>
/// 背景画像の位置の基準となる、作業領域上の隅（data-model.md §1）。
/// OffsetX / OffsetY がどちらの辺から測った距離かを、この値が決める。
/// </summary>
public enum BackgroundImageAnchor
{
    /// <summary>左右の基準辺=作業領域の左端、上下の基準辺=上端。</summary>
    TopLeft,

    /// <summary>左右の基準辺=作業領域の右端、上下の基準辺=上端。</summary>
    TopRight,

    /// <summary>左右の基準辺=作業領域の左端、上下の基準辺=下端。</summary>
    BottomLeft,

    /// <summary>左右の基準辺=作業領域の右端、上下の基準辺=下端。</summary>
    BottomRight,
}

/// <summary>
/// ユーザーが最後にドロップした背景画像の位置（data-model.md §1）。
/// 不変の record。値を更新するときは丸ごと置き換える。
/// 値域チェック（Clamp）はこのクラスでは行わず、既存方針どおり AppSettings.Clamp() に集約する。
/// </summary>
/// <param name="MonitorId">置いたモニターの識別子。非 null（空文字は「一致するモニター無し」と同じ扱い）。</param>
/// <param name="Anchor">基準の隅。</param>
/// <param name="OffsetX">基準の隅に接する辺からの距離（論理 px）。値域 0〜100000。丸めは AppSettings.Clamp() が行う。</param>
/// <param name="OffsetY">基準の隅に接する辺からの距離（論理 px）。値域 0〜100000。丸めは AppSettings.Clamp() が行う。</param>
public sealed record BackgroundImagePosition(
    [property: JsonPropertyName("monitorId"), JsonPropertyOrder(0)]
    string MonitorId,
    [property: JsonPropertyName("anchor"), JsonPropertyOrder(1)]
    BackgroundImageAnchor Anchor,
    [property: JsonPropertyName("offsetX"), JsonPropertyOrder(2)]
    double OffsetX,
    [property: JsonPropertyName("offsetY"), JsonPropertyOrder(3)]
    double OffsetY);
