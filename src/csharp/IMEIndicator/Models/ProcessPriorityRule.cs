// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;

namespace IMEIndicator.Models;

/// <summary>
/// Windows プロセス優先度（タスクマネージャー準拠の 6 段階）。
/// JSON 上は文字列（"Idle"/"BelowNormal"/...）で表現するが、その相互変換を行う
/// <c>JsonConverter</c> は T014（app/Settings/JsonConverters.cs の PriorityLevelConverter）で
/// 実装するため、本タスク（T011）では型定義のみを行う。
/// 数値（列挙順）は移植元 src/cpp/models/ProcessPriorityRule.h の
/// <c>enum class PriorityLevel</c> と一致させている。
/// </summary>
public enum PriorityLevel
{
    Idle = 0,
    BelowNormal = 1,
    Normal = 2,
    AboveNormal = 3,
    High = 4,
    Realtime = 5
}

/// <summary>
/// プロセス優先度制御ルール（永続化モデル。specs/014-port-to-csharp/data-model.md §1）。
/// 移植元: src/cpp/models/ProcessPriorityRule.h / .cpp の struct ProcessPriorityRule。
/// </summary>
public sealed class ProcessPriorityRule
{
    /// <summary>対象プロセス名。".exe" 拡張子付きでの指定も許容する（内部で除去）。</summary>
    [JsonPropertyName("processName")]
    [JsonPropertyOrder(0)]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>このルールが一致したプロセスへ適用する優先度。</summary>
    [JsonPropertyName("targetPriority")]
    [JsonPropertyOrder(1)]
    public PriorityLevel TargetPriority { get; set; } = PriorityLevel.Normal;

    /// <summary>優先度の適用に失敗し続けた場合の指数バックオフの上限（値域 0〜10）。</summary>
    [JsonPropertyName("maxBackoffExponent")]
    [JsonPropertyOrder(2)]
    public int MaxBackoffExponent { get; set; } = 6;

    /// <summary>このルールを有効にするかどうか。</summary>
    [JsonPropertyName("isEnabled")]
    [JsonPropertyOrder(3)]
    public bool IsEnabled { get; set; } = true;

    /// <summary>true の場合、対象プロセスのアフィニティを Efficiency コアのみへ固定する。</summary>
    [JsonPropertyName("useECoreOnly")]
    [JsonPropertyOrder(4)]
    public bool UseECoreOnly { get; set; } = false;

    /// <summary>
    /// 末尾の ".exe"（大小無視）と両端の空白を除去した名前を返す。
    /// 移植元: ProcessPriorityRule::normalizedProcessName()（win32::stripExeAndTrim() 相当）。
    /// </summary>
    public string NormalizedProcessName() => CppTrim.StripExeAndTrim(ProcessName);

    /// <summary>
    /// バリデーション: 空白をトリムした結果が非空であること。
    /// 移植元: ProcessPriorityRule::isValid()。
    /// </summary>
    public bool IsValid() => CppTrim.Trim(ProcessName).Length > 0;

    /// <summary>
    /// バリデーション後の MaxBackoffExponent（[0, 10] でクランプ）。
    /// 移植元: ProcessPriorityRule::validatedMaxBackoffExponent()。
    /// </summary>
    public int ValidatedMaxBackoffExponent() => Math.Clamp(MaxBackoffExponent, 0, 10);
}
