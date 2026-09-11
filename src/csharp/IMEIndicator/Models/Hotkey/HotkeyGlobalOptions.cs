// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;

namespace IMEIndicator.Models.Hotkey;

/// <summary>
/// キーボードフックモード。HotkeyP の useHook(int) と互換の意味を持つ。
/// JSON 上は文字列（"none"/"low_level"/"system"/"auto"）で表現するが、その相互変換を行う
/// JsonConverter は T014（app/Settings/JsonConverters.cs の HookModeConverter）で実装するため、
/// 本タスク（T012）では型定義のみを行う
/// （<see cref="Models.PriorityLevel"/> / T011 と同じ方針。ProcessPriorityRule.cs 参照）。
/// 数値は移植元 src/cpp/models/hotkey/HotkeyGlobalOptions.h の enum class HookMode と一致させている。
/// </summary>
public enum HookMode
{
    /// <summary>フックなし。</summary>
    None = 0,

    /// <summary>WH_KEYBOARD_LL 低レベルフック。</summary>
    LowLevel = 1,

    /// <summary>RegisterHotKey によるシステムホットキー。</summary>
    SystemHotKey = 2,

    /// <summary>状況に応じて自動選択（既定）。</summary>
    Auto = 3,
}

/// <summary>
/// HotkeySettings.GlobalOptions のネスト型。全ホットキーエントリ共通の動作設定。
/// 移植元: src/cpp/models/hotkey/HotkeyGlobalOptions.h の struct HotkeyGlobalOptions。
/// </summary>
public sealed class HotkeyGlobalOptions
{
    /// <summary>キーボードフックモード。既定 Auto。</summary>
    [JsonPropertyName("hookMode")]
    [JsonPropertyOrder(0)]
    public HookMode HookMode { get; set; } = HookMode.Auto;

    /// <summary>左右修飾キー（LCtrl/RCtrl 等）を区別するか。</summary>
    [JsonPropertyName("distinguishLeftRightModifiers")]
    [JsonPropertyOrder(1)]
    public bool DistinguishLeftRightModifiers { get; set; } = false;

    /// <summary>フォアグラウンド判定から除外するプロセス名。0〜32 件、各 0〜256 文字。</summary>
    [JsonPropertyName("foregroundExcludeProcesses")]
    [JsonPropertyOrder(2)]
    public List<string> ForegroundExcludeProcesses { get; set; } = new();

    /// <summary>マウス操作の遅延（ミリ秒）。値域 [0, 1000]。</summary>
    [JsonPropertyName("mouseDelayMs")]
    [JsonPropertyOrder(3)]
    public int MouseDelayMs { get; set; } = 0;

    /// <summary>ホットキー実行時にサウンドを再生するか。</summary>
    [JsonPropertyName("playSoundOnExecution")]
    [JsonPropertyOrder(4)]
    public bool PlaySoundOnExecution { get; set; } = false;
}
