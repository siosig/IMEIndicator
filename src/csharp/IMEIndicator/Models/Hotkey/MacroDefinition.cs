// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Interop;

namespace IMEIndicator.Models.Hotkey;

/// <summary>
/// マクロ実行時の中間表現。永続化されない（HotKeyEntry.Args の文字列がソース）。
/// 将来実装予定の MacroParser（マクロ文字列 → 本型）と CommandExecutor（本型 → SendInput 実行）
/// で生成・消費される想定。移植元: src/cpp/models/hotkey/MacroDefinition.h の struct MacroDefinition。
/// JSON 永続化の対象外のため、他の Hotkey モデルと異なり JsonPropertyName 等は付与しない。
/// </summary>
public sealed class MacroDefinition
{
    /// <summary>元のマクロ文字列。</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// SendInput 用の入力列。
    /// 要素型 <see cref="INPUT"/>（IMEIndicator.Interop、NativeTypes.cs 定義）が internal のため、
    /// このプロパティも internal とする（MacroDefinition クラス自体は他の Hotkey モデルと同様 public）。
    /// NativeTypes.cs は本タスク（T012）の対象外ファイルのため可視性は変更しない。
    /// </summary>
    internal List<INPUT> Inputs { get; set; } = new();

    /// <summary>推定実行時間（\sleep の合計）。</summary>
    public TimeSpan TotalDuration { get; set; } = TimeSpan.Zero;

    /// <summary>\rep &lt;count&gt; の指定があるか。</summary>
    public bool Repeat { get; set; } = false;

    /// <summary>繰り返し回数。値域 [1, 100]。既定 1。</summary>
    public int RepeatCount { get; set; } = 1;
}
