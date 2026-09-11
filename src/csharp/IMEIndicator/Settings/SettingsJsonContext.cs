// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json;
using System.Text.Json.Serialization;
using IMEIndicator.Models;

namespace IMEIndicator.Settings;

/// <summary>
/// <see cref="AppSettings"/> のソース生成 JSON シリアライズコンテキスト（契約:
/// specs/014-port-to-csharp/contracts/settings-compat-contract.md §シリアライザ設定）。
/// リフレクションを使わないため起動が速く、AOT/トリミングにも対応する。
/// </summary>
/// <remarks>
/// このコンテキストは <see cref="AppSettings"/> を起点に、そこから到達可能な型
/// （<see cref="MouseCursorIndicatorSettings"/>、<see cref="BackgroundImageSettings"/>、
/// <see cref="ProcessPriorityRule"/> とそのリスト、<c>Hotkey.*</c> 一式）のメタデータを
/// ソースジェネレーターが自動的に生成する（System.Text.Json のソース生成は、指定したルート型の
/// プロパティグラフを辿って到達可能な型を自動的に含める。個別に <c>[JsonSerializable]</c> を
/// 列挙する必要はない）。
///
/// <see cref="JavaScriptEncoder"/>（非 ASCII 文字を \uXXXX にエスケープしない設定）は
/// この属性では指定できない型のため、実際に使う <see cref="JsonSerializerOptions"/> は
/// <c>SettingsManager</c>（T015）側で <c>TypeInfoResolver = SettingsJsonContext.Default</c> を
/// 土台にしつつ、<c>Encoder</c> と <see cref="JsonConverters"/> の各コンバータを追加して構築する。
/// メタデータベース生成（既定の <see cref="JsonSourceGenerationMode.Metadata"/>）はこの追加を
/// 尊重するため、実行時に渡したコンバータがプロパティごとの既定変換より優先される。
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    NumberHandling = JsonNumberHandling.Strict,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AppSettings))]
public partial class SettingsJsonContext : JsonSerializerContext
{
}
