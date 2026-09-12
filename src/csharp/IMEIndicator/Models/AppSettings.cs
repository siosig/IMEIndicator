// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json.Serialization;
using IMEIndicator.App;
using IMEIndicator.Models.Hotkey;

namespace IMEIndicator.Models;

/// <summary>
/// ログレベル。移植元 src/cpp/models/AppSettings.h の <c>enum class LogLevel</c> と同じ並び。
/// JSON 上は文字列（"trace"/"debug"/"info"/"warn"/"error"/"critical"）。相互変換は T014 の
/// LogLevelConverter が行う（本クラスでは型定義のみ）。
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Critical = 5,
}

/// <summary>
/// 設定ファイル（%APPDATA%\IMEIndicator\settings.json）のルートエンティティ。
/// キー順・既定値・値域は specs/014-port-to-csharp/contracts/settings-compat-contract.md（v4 まで）、
/// specs/015-split-appearance-settings/data-model.md §3（backgroundImage の値域、v5）。
/// 移植元は src/cpp/models/AppSettings.h / .cpp の struct AppSettings。
/// </summary>
/// <remarks>
/// <see cref="IJsonOnDeserializing"/> / <see cref="IJsonOnDeserialized"/> を実装し、移植元
/// <c>from_json</c>（AppSettings.cpp）の 2 つの挙動を再現する。
/// (1) <c>schemaVersion</c> が JSON に無い場合は 1 として扱う（<c>j.value("schemaVersion", 1)</c>）。
///     これは <c>new AppSettings()</c>（fresh install、既定値 4）とは異なる、デシリアライズ専用の既定値。
/// (2) デシリアライズ完了直後に必ず <see cref="Clamp"/> を呼ぶ（<c>from_json</c> 末尾の <c>s.clamp()</c>）。
/// </remarks>
public sealed class AppSettings : IJsonOnDeserializing, IJsonOnDeserialized
{
    /// <summary>
    /// スキーマバージョン。<c>new AppSettings()</c> の既定値は 5（fresh install。
    /// 015-split-appearance-settings で backgroundImage.size/opacity を追加し 4→5）。
    /// JSON からのデシリアライズでキーが無い場合は 1 として扱う（<see cref="OnDeserializing"/> 参照）。
    /// 読み込みは 1〜5 を受容し、書き出しは常に 5（<see cref="Settings.SettingsManager"/> が書き出し直前に上書きする）。
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; set; } = 5;

    [JsonPropertyName("mouseCursorIndicator")]
    [JsonPropertyOrder(1)]
    public MouseCursorIndicatorSettings MouseCursorIndicator { get; set; } = new();

    /// <summary>IME ON 時にインジケーターへ表示する文字。1〜8 文字、空は既定値。</summary>
    [JsonPropertyName("imeOnText")]
    [JsonPropertyOrder(2)]
    public string ImeOnText { get; set; } = "あ";

    /// <summary>IME OFF 時に想定される表示文字（現状は保持のみで描画には使わない）。1〜8 文字。</summary>
    [JsonPropertyName("imeOffText")]
    [JsonPropertyOrder(3)]
    public string ImeOffText { get; set; } = "A";

    [JsonPropertyName("isFirstLaunch")]
    [JsonPropertyOrder(4)]
    public bool IsFirstLaunch { get; set; } = true;

    /// <summary>プロセス優先度ルール。最大 30 件（超過は Clamp で切り捨て）。</summary>
    [JsonPropertyName("processPriorityRules")]
    [JsonPropertyOrder(5)]
    public List<ProcessPriorityRule> ProcessPriorityRules { get; set; } = new();

    /// <summary>プロセス優先度監視のポーリング間隔（秒）。値域 1〜1800。</summary>
    [JsonPropertyName("pollingIntervalSeconds")]
    [JsonPropertyOrder(6)]
    public int PollingIntervalSeconds { get; set; } = 1;

    [JsonPropertyName("logLevel")]
    [JsonPropertyOrder(7)]
    public LogLevel LogLevel { get; set; } = LogLevel.Warn;

    /// <summary>ピクセル検証の周期（ミリ秒）。値域 0〜60000（0 で無効）。</summary>
    [JsonPropertyName("pixelVerificationIntervalMs")]
    [JsonPropertyOrder(8)]
    public int PixelVerificationIntervalMs { get; set; } = 2000;

    [JsonPropertyName("hotkeySettings")]
    [JsonPropertyOrder(9)]
    public HotkeySettings HotkeySettings { get; set; } = new();

    [JsonPropertyName("backgroundImage")]
    [JsonPropertyOrder(10)]
    public BackgroundImageSettings BackgroundImage { get; set; } = new();

    /// <summary>
    /// デシリアライズ開始前に呼ばれる。schemaVersion を JSON 側の既定値 1 にリセットする
    /// （<c>new AppSettings()</c> の既定値 4 とは別。移植元 <c>j.value("schemaVersion", 1)</c>）。
    /// JSON に schemaVersion キーがあれば、この直後にプロパティセッターが上書きする。
    /// </summary>
    void IJsonOnDeserializing.OnDeserializing()
    {
        SchemaVersion = 1;
    }

    /// <summary>デシリアライズ完了直後に <see cref="Clamp"/> を呼ぶ（移植元 from_json 末尾の s.clamp()）。</summary>
    void IJsonOnDeserialized.OnDeserialized() => Clamp();

    /// <summary>
    /// 値域を丸め、上限を超えた要素を切り捨てる。読み込み直後に必ず呼ぶ
    /// （移植元: AppSettings::clamp()、from_json 末尾で呼ばれる）。
    /// </summary>
    public void Clamp()
    {
        MouseCursorIndicator.Size = Math.Clamp(MouseCursorIndicator.Size, 20.0, 100.0);
        MouseCursorIndicator.Opacity = Math.Clamp(MouseCursorIndicator.Opacity, 0.1, 1.0);
        // OffsetX / OffsetY は spec 上は制限なし（描画時の境界補正で吸収。移植元と同じ）

        ImeOnText = ClampIndicatorText(ImeOnText, "あ");
        ImeOffText = ClampIndicatorText(ImeOffText, "A");

        PollingIntervalSeconds = Math.Clamp(PollingIntervalSeconds, MinPollingIntervalSeconds, MaxPollingIntervalSeconds);

        // 31 件目以降を切り捨て（移植元: AppConstants::MaxProcessPriorityRules = 30）
        if (ProcessPriorityRules.Count > MaxProcessPriorityRules)
        {
            ProcessPriorityRules.RemoveRange(MaxProcessPriorityRules, ProcessPriorityRules.Count - MaxProcessPriorityRules);
        }
        foreach (ProcessPriorityRule rule in ProcessPriorityRules)
        {
            rule.MaxBackoffExponent = Math.Clamp(rule.MaxBackoffExponent, 0, 10);
        }

        PixelVerificationIntervalMs = Math.Clamp(PixelVerificationIntervalMs, 0, 60000);

        if ((int)LogLevel < 0 || (int)LogLevel > 5)
        {
            LogLevel = LogLevel.Warn;
        }

        // schemaVersion は 1/2/3/4/5 のみ受容。書き出しは SettingsManager.Save() が常に 5 にする。
        if (SchemaVersion is not (1 or 2 or 3 or 4 or 5))
        {
            SchemaVersion = 5;
        }

        HotkeySettings.Hotkeys ??= new List<HotKeyEntry>();
        HotkeySettings.Categories ??= new List<HotkeyCategory>();
        if (HotkeySettings.Hotkeys.Count > MaxHotkeys)
        {
            HotkeySettings.Hotkeys.RemoveRange(MaxHotkeys, HotkeySettings.Hotkeys.Count - MaxHotkeys);
        }
        if (HotkeySettings.Categories.Count > MaxHotkeyCategories)
        {
            HotkeySettings.Categories.RemoveRange(MaxHotkeyCategories, HotkeySettings.Categories.Count - MaxHotkeyCategories);
        }
        foreach (HotKeyEntry entry in HotkeySettings.Hotkeys)
        {
            if (entry.Category is < 0 or > 39)
            {
                entry.Category = 0;
            }
        }
        HotkeySettings.GlobalOptions ??= new HotkeyGlobalOptions();
        HotkeySettings.GlobalOptions.MouseDelayMs = Math.Clamp(HotkeySettings.GlobalOptions.MouseDelayMs, 0, 1000);
        HotkeySettings.GlobalOptions.ForegroundExcludeProcesses ??= new List<string>();
        if (HotkeySettings.GlobalOptions.ForegroundExcludeProcesses.Count > 32)
        {
            HotkeySettings.GlobalOptions.ForegroundExcludeProcesses.RemoveRange(
                32, HotkeySettings.GlobalOptions.ForegroundExcludeProcesses.Count - 32);
        }

        // 015-split-appearance-settings FR-001/FR-002/FR-010。
        BackgroundImage.Size = Math.Clamp(BackgroundImage.Size, AppConstants.BackgroundImageMinSize, AppConstants.BackgroundImageMaxSize);
        BackgroundImage.Opacity = Math.Clamp(BackgroundImage.Opacity, 0.1, 1.0);
    }

    // 移植元: AppSettings.cpp 内の無名名前空間 clampIndicatorText()。
    // トリム後に空なら既定値、MaxIndicatorTextChars（8）を超えたら切り詰める。
    private static string ClampIndicatorText(string src, string fallback)
    {
        string trimmed = CppTrim.Trim(src);
        if (trimmed.Length == 0)
        {
            return fallback;
        }
        if (trimmed.Length > MaxIndicatorTextChars)
        {
            trimmed = trimmed[..MaxIndicatorTextChars];
        }
        return trimmed;
    }

    // 移植元: src/cpp/app/AppConstants.h の制限値群
    private const int MinPollingIntervalSeconds = 1;
    private const int MaxPollingIntervalSeconds = 1800;
    private const int MaxIndicatorTextChars = 8;
    private const int MaxProcessPriorityRules = 30;
    private const int MaxHotkeys = 256;
    private const int MaxHotkeyCategories = 32;
}
