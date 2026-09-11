// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// 異常終了時の電源モード復元用バックアップ（FR-011）。
/// 移植元: src/cpp/services/PowerModeBackup.h / .cpp の class PowerModeBackup。
/// 保存先パス・JSON フィールド（schemaVersion / previousMode / savedAtUnixMs / processId）は
/// 現行 C++ 版と完全互換: %LOCALAPPDATA%\IMEIndicator\state\power-mode-backup.json
/// （src/cpp/app/App.cpp の localAppDataDir_ + AppConstants::StateSubDir、
/// src/cpp/app/AppConstants.h の PowerBackupFileName から実際のパスを確認済み）。
/// </summary>
/// <remarks>
/// C++ 版の save/tryLoad は savedAtUnixMs・processId を呼び出し側が明示指定できる汎用 API だが、
/// 実際の呼び出し元（App.cpp の backupCurrentPowerMode() / 起動時リストア）はいずれも
/// previousMode のみを指定し、時刻・PID は保存時に自動採番させている。C# 版はこの実利用形態に
/// 合わせて公開 API を <see cref="Save"/>(PowerMode) / <see cref="TryLoad"/>(out PowerMode) に
/// 単純化する（schemaVersion・savedAtUnixMs・processId はワイヤーフォーマットとしては維持しつつ、
/// 内部の DTO（<see cref="BackupRecord"/>）に閉じ込める）。
/// </remarks>
public sealed class PowerModeBackup
{
    private const string BackupFileName = "power-mode-backup.json";
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>本番用: %LOCALAPPDATA%\IMEIndicator\state を使用する。</summary>
    public PowerModeBackup() : this(DefaultStateDirectory())
    {
    }

    /// <summary>テスト用: ステートディレクトリを注入する。</summary>
    public PowerModeBackup(string stateDirectory)
    {
        StateDirectory = stateDirectory;
        FilePath = Path.Combine(stateDirectory, BackupFileName);
    }

    /// <summary>バックアップファイルを置くディレクトリ。</summary>
    public string StateDirectory { get; }

    /// <summary>バックアップファイルの絶対パス（テスト用に公開。移植元 filePath() に対応）。</summary>
    public string FilePath { get; }

    /// <summary>
    /// 現在の電源モードをバックアップとして保存する（アトミック書き込み: .tmp へ書いてから
    /// File.Move で置き換える。SettingsManager.WriteAtomic と同じ方針）。
    /// savedAtUnixMs・processId は呼び出し時点の値を自動採番する。失敗しても例外は投げず false を返す。
    /// </summary>
    public bool Save(PowerMode previousMode)
    {
        var record = new BackupRecord
        {
            SchemaVersion = CurrentSchemaVersion,
            PreviousMode = ToStableString(previousMode),
            SavedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ProcessId = (uint)Environment.ProcessId,
        };

        string content;
        try
        {
            content = JsonSerializer.Serialize(record, JsonOptions) + "\n";
        }
        catch (Exception ex)
        {
            Log.Power.Warning(ex, "PowerModeBackup save: serialize failed");
            return false;
        }

        return WriteAtomic(content);
    }

    /// <summary>
    /// 既存ファイルを読み込む。存在しない・破損・schemaVersion 不一致・previousMode 不正のいずれかの
    /// 場合は false を返す。不正なファイルは内部で削除し、次回以降も再評価しない
    /// （移植元 tryLoad() の deleteFile() 呼び出しと同じ方針）。
    /// </summary>
    public bool TryLoad(out PowerMode previousMode)
    {
        previousMode = PowerMode.Balanced;

        if (!File.Exists(FilePath))
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(FilePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Power.Warning(ex, "PowerModeBackup tryLoad: failed to open file");
            return false;
        }

        BackupRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<BackupRecord>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Power.Warning(ex, "PowerModeBackup tryLoad: parse error");
            DeleteFileCore();
            return false;
        }

        if (record is null)
        {
            Log.Power.Warning("PowerModeBackup tryLoad: parse error: null document");
            DeleteFileCore();
            return false;
        }

        if (record.SchemaVersion != CurrentSchemaVersion)
        {
            Log.Power.Warning("PowerModeBackup tryLoad: unsupported schemaVersion={SchemaVersion}", record.SchemaVersion);
            DeleteFileCore();
            return false;
        }

        if (!TryParseStableString(record.PreviousMode, out PowerMode parsedMode))
        {
            Log.Power.Warning("PowerModeBackup tryLoad: invalid previousMode={PreviousMode}", record.PreviousMode);
            DeleteFileCore();
            return false;
        }

        previousMode = parsedMode;
        return true;
    }

    /// <summary>バックアップファイルを削除する（正常終了時のクリーンアップ）。存在しなくてもエラーにしない。</summary>
    public void Delete() => DeleteFileCore();

    private static string DefaultStateDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IMEIndicator",
        "state");

    private bool WriteAtomic(string content)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            string tempPath = FilePath + ".tmp";
            // BOM なし UTF-8（SettingsManager.WriteAtomic と同じ方針）
            File.WriteAllText(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Power.Warning(ex, "PowerModeBackup save: atomic write failed");
            return false;
        }
    }

    private void DeleteFileCore()
    {
        TryDeleteFile(FilePath);
        // .tmp ファイルも残っていれば削除する（クラッシュ後の掃除。移植元 deleteFile() と同じ）。
        TryDeleteFile(FilePath + ".tmp");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ベストエフォート。削除失敗は無視する（移植元 deleteFile() の error_code 方針と同じ）。
        }
    }

    private static string ToStableString(PowerMode mode) => mode switch
    {
        PowerMode.BestPowerEfficiency => "BestPowerEfficiency",
        PowerMode.Balanced => "Balanced",
        PowerMode.BestPerformance => "BestPerformance",
        _ => "Balanced",
    };

    private static bool TryParseStableString(string value, out PowerMode mode)
    {
        switch (value)
        {
            case "BestPowerEfficiency":
                mode = PowerMode.BestPowerEfficiency;
                return true;
            case "Balanced":
                mode = PowerMode.Balanced;
                return true;
            case "BestPerformance":
                mode = PowerMode.BestPerformance;
                return true;
            default:
                mode = PowerMode.Balanced;
                return false;
        }
    }

    // JSON ワイヤーフォーマット専用の DTO。移植元 PowerModeBackup.cpp の nlohmann::json 構築
    // （schemaVersion / previousMode / savedAtUnixMs / processId）とフィールド名・型を合わせている。
    private sealed class BackupRecord
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("previousMode")]
        public string PreviousMode { get; set; } = string.Empty;

        [JsonPropertyName("savedAtUnixMs")]
        public long SavedAtUnixMs { get; set; }

        [JsonPropertyName("processId")]
        public uint ProcessId { get; set; }
    }
}
