// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IMEIndicator.Models;
using IMEIndicator.Models.Hotkey;

namespace IMEIndicator.Settings;

/// <summary>
/// <c>double</c> を、C++ 版が使う nlohmann::json の <c>dump()</c> と同じ「常に小数点を含む最短往復可能表現」で
/// 書き出す（契約: contracts/settings-compat-contract.md §バイト互換の要件）。
/// .NET Core 3.0 以降、<see cref="double.ToString(IFormatProvider)"/>（書式指定なし）は既定で
/// 最短往復可能表現を返す（IEEE 754-2008 準拠の浮動小数点書式更新）。ただし整数値ちょうどのとき
/// （例: 34.0）は小数点が付かず "34" になるため、その場合のみ ".0" を補う。
/// </summary>
public sealed class DoubleWithPointConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteRawValue(FormatDouble(value), skipInputValidation: true);

    /// <summary>
    /// 上記クラス概要の書式化ロジック本体（最短往復表現。整数値のときのみ ".0" を補う）。
    /// <see cref="BackgroundImagePositionConverter"/> など、double を同じ書式で書き出す必要がある
    /// 他のコンバーターと共有するために切り出す（ロジックの重複を避ける。契約:
    /// specs/018-draggable-background-image/contracts/settings-schema-contract.md §書き出し規則）。
    /// </summary>
    internal static string FormatDouble(double value)
    {
        string formatted = value.ToString(CultureInfo.InvariantCulture);
        if (!formatted.Contains('.') && !formatted.Contains('E') && !formatted.Contains('e'))
        {
            formatted += ".0";
        }
        return formatted;
    }
}

/// <summary>
/// <see cref="LogLevel"/> ↔ JSON 文字列（"trace"/"debug"/"info"/"warn"/"error"/"critical"）の変換。
/// 移植元: src/cpp/models/AppSettings.cpp の logLevelToString / tryParseLogLevel。
/// 読み込みは大小無視、"warning"→Warn・"err"→Error の別表記も受容する。文字列以外のトークンや
/// 未知の値は例外を投げず既定値 Warn にフォールバックする（移植元が常に Warn で事前初期化してから
/// 文字列トークンのときだけ上書きする挙動と同じ）。
/// </summary>
public sealed class LogLevelConverter : JsonConverter<LogLevel>
{
    public override LogLevel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && TryParse(reader.GetString(), out LogLevel level))
        {
            return level;
        }
        return LogLevel.Warn;
    }

    public override void Write(Utf8JsonWriter writer, LogLevel value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToText(value));

    /// <summary>大小無視でのパース。設定画面のドロップダウンや将来のコマンドライン解析からも使えるよう公開する。</summary>
    public static bool TryParse(string? s, out LogLevel level)
    {
        if (s is not null)
        {
            if (Eq(s, "trace")) { level = LogLevel.Trace; return true; }
            if (Eq(s, "debug")) { level = LogLevel.Debug; return true; }
            if (Eq(s, "info")) { level = LogLevel.Info; return true; }
            if (Eq(s, "warn")) { level = LogLevel.Warn; return true; }
            if (Eq(s, "warning")) { level = LogLevel.Warn; return true; }
            if (Eq(s, "error")) { level = LogLevel.Error; return true; }
            if (Eq(s, "err")) { level = LogLevel.Error; return true; }
            if (Eq(s, "critical")) { level = LogLevel.Critical; return true; }
        }
        level = LogLevel.Warn;
        return false;
    }

    public static string ToText(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Info => "info",
        LogLevel.Warn => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => "warn",
    };

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// <see cref="PriorityLevel"/> ↔ JSON の変換。移植元: src/cpp/models/ProcessPriorityRule.cpp の
/// priorityLevelToString / tryParsePriorityLevel。
/// <b>LogLevel/HookMode と異なり大小を区別する</b>（移植元 <c>s == "Idle"</c> の厳密比較のまま）。
/// 文字列が一致しない場合、数値トークンであれば 0〜5 をそのまま enum 値として採用するフォールバックがある
/// （移植元 from_json の itNum 分岐）。どちらにも該当しなければ既定値 Normal。
/// </summary>
public sealed class PriorityLevelConverter : JsonConverter<PriorityLevel>
{
    public override PriorityLevel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? s = reader.GetString();
            if (s == "Idle") return PriorityLevel.Idle;
            if (s == "BelowNormal") return PriorityLevel.BelowNormal;
            if (s == "Normal") return PriorityLevel.Normal;
            if (s == "AboveNormal") return PriorityLevel.AboveNormal;
            if (s == "High") return PriorityLevel.High;
            if (s == "Realtime") return PriorityLevel.Realtime;
            return PriorityLevel.Normal;
        }
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int v) && v is >= 0 and <= 5)
        {
            return (PriorityLevel)v;
        }
        return PriorityLevel.Normal;
    }

    public override void Write(Utf8JsonWriter writer, PriorityLevel value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToText(value));

    public static string ToText(PriorityLevel level) => level switch
    {
        PriorityLevel.Idle => "Idle",
        PriorityLevel.BelowNormal => "BelowNormal",
        PriorityLevel.Normal => "Normal",
        PriorityLevel.AboveNormal => "AboveNormal",
        PriorityLevel.High => "High",
        PriorityLevel.Realtime => "Realtime",
        _ => "Normal",
    };
}

/// <summary>
/// <see cref="HookMode"/> ↔ JSON 文字列（"none"/"low_level"/"system"/"auto"）の変換。
/// 移植元: src/cpp/models/hotkey/HotkeyGlobalOptions.cpp の hookModeToString / tryParseHookMode。
/// 読み込みは大小無視、"lowlevel"（アンダースコアなし）も LowLevel として受容する。
/// 文字列以外・未知の値は既定値 Auto にフォールバックする（例外は投げない）。
/// </summary>
public sealed class HookModeConverter : JsonConverter<HookMode>
{
    public override HookMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? s = reader.GetString();
            if (Eq(s, "none")) return HookMode.None;
            if (Eq(s, "low_level")) return HookMode.LowLevel;
            if (Eq(s, "lowlevel")) return HookMode.LowLevel;
            if (Eq(s, "system")) return HookMode.SystemHotKey;
            if (Eq(s, "auto")) return HookMode.Auto;
        }
        return HookMode.Auto;
    }

    public override void Write(Utf8JsonWriter writer, HookMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToText(value));

    public static string ToText(HookMode mode) => mode switch
    {
        HookMode.None => "none",
        HookMode.LowLevel => "low_level",
        HookMode.SystemHotKey => "system",
        HookMode.Auto => "auto",
        _ => "auto",
    };

    private static bool Eq(string? a, string b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// <see cref="BackgroundImagePosition"/> ↔ JSON オブジェクトの変換（018-draggable-background-image、契約:
/// specs/018-draggable-background-image/contracts/settings-schema-contract.md §不正値の扱い・§書き出し規則）。
/// 読み込みは例外を一切投げない: オブジェクト以外のトークン、monitorId の欠落・非文字列、未知の anchor、
/// offsetX/offsetY の欠落・非数値は、いずれも null（未移動）にフォールバックする。
/// <c>JsonDocument.ParseValue(ref reader)</c> で値全体を読み切ってから
/// <c>TryGetProperty</c> 等の非例外系 API だけで検証するため、例外を投げずにリーダーを値の終端まで
/// 進められ、後続のプロパティを通常どおり読み続けられる（SettingsManager.Load() 段階2が型変換の例外で
/// 設定全体を既定値に戻すのを防ぐ、FR-017）。
/// </summary>
public sealed class BackgroundImagePositionConverter : JsonConverter<BackgroundImagePosition?>
{
    public override BackgroundImagePosition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Nullable 参照型の JsonConverter<T?> では、null トークンに対し既定ではコンバーターの Read は
        // 呼ばれない（シリアライザーが直接 null を割り当てる）が、念のため安全側に倒しておく。
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement;

        string? monitorId = null;
        if (element.TryGetProperty("monitorId", out JsonElement monitorIdProp) && monitorIdProp.ValueKind == JsonValueKind.String)
        {
            monitorId = monitorIdProp.GetString();
        }

        string? anchorText = null;
        if (element.TryGetProperty("anchor", out JsonElement anchorProp) && anchorProp.ValueKind == JsonValueKind.String)
        {
            anchorText = anchorProp.GetString();
        }
        bool anchorOk = TryParseAnchor(anchorText, out BackgroundImageAnchor anchor);

        double offsetX = 0.0;
        bool offsetXOk = element.TryGetProperty("offsetX", out JsonElement offsetXProp)
            && offsetXProp.ValueKind == JsonValueKind.Number
            && offsetXProp.TryGetDouble(out offsetX);

        double offsetY = 0.0;
        bool offsetYOk = element.TryGetProperty("offsetY", out JsonElement offsetYProp)
            && offsetYProp.ValueKind == JsonValueKind.Number
            && offsetYProp.TryGetDouble(out offsetY);

        if (monitorId is null || !anchorOk || !offsetXOk || !offsetYOk)
        {
            return null;
        }

        return new BackgroundImagePosition(monitorId, anchor, offsetX, offsetY);
    }

    public override void Write(Utf8JsonWriter writer, BackgroundImagePosition? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("monitorId", value.MonitorId);
        writer.WriteString("anchor", AnchorToText(value.Anchor));
        WriteDoubleWithPoint(writer, "offsetX", value.OffsetX);
        WriteDoubleWithPoint(writer, "offsetY", value.OffsetY);
        writer.WriteEndObject();
    }

    /// <summary>大小無視でのパース。設定画面など、他の箇所からも使えるよう公開する。</summary>
    public static bool TryParseAnchor(string? s, out BackgroundImageAnchor anchor)
    {
        if (s is not null)
        {
            if (Eq(s, "topLeft")) { anchor = BackgroundImageAnchor.TopLeft; return true; }
            if (Eq(s, "topRight")) { anchor = BackgroundImageAnchor.TopRight; return true; }
            if (Eq(s, "bottomLeft")) { anchor = BackgroundImageAnchor.BottomLeft; return true; }
            if (Eq(s, "bottomRight")) { anchor = BackgroundImageAnchor.BottomRight; return true; }
        }
        anchor = BackgroundImageAnchor.TopLeft;
        return false;
    }

    public static string AnchorToText(BackgroundImageAnchor anchor) => anchor switch
    {
        BackgroundImageAnchor.TopLeft => "topLeft",
        BackgroundImageAnchor.TopRight => "topRight",
        BackgroundImageAnchor.BottomLeft => "bottomLeft",
        BackgroundImageAnchor.BottomRight => "bottomRight",
        _ => "topLeft",
    };

    private static void WriteDoubleWithPoint(Utf8JsonWriter writer, string propertyName, double value)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteRawValue(DoubleWithPointConverter.FormatDouble(value), skipInputValidation: true);
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
