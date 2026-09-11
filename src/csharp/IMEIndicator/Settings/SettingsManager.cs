// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using IMEIndicator.Models;
using IMEIndicator.Services;

namespace IMEIndicator.Settings;

/// <summary>
/// 設定 I/O 統括。移植元: src/cpp/services/SettingsManager.h / .cpp の class SettingsManager。
/// 契約: specs/014-port-to-csharp/contracts/settings-compat-contract.md。
/// </summary>
public sealed class SettingsManager
{
#if DEBUG
    private const string SettingsFileNameConst = "settings-d.json";
#else
    private const string SettingsFileNameConst = "settings.json";
#endif

    private readonly JsonSerializerOptions _jsonOptions;
    private bool _loadedAsV1;
    private bool _loadedAsV2;
    private bool _loadedAsV3;

    /// <summary>本番用: %APPDATA%\IMEIndicator\ を使用する。</summary>
    public SettingsManager() : this(DefaultSettingsDirectory())
    {
    }

    /// <summary>テスト用: 設定ディレクトリを注入する。</summary>
    public SettingsManager(string settingsDirectory)
    {
        SettingsDirectory = settingsDirectory;
        SettingsFilePath = Path.Combine(settingsDirectory, SettingsFileNameConst);
        _jsonOptions = BuildJsonOptions();
    }

    /// <summary>現在保持している設定。<see cref="Load"/> / <see cref="Reset"/> で更新される。</summary>
    public AppSettings Settings { get; set; } = new();

    /// <summary>直近のエラーメッセージ。エラーが無ければ null。</summary>
    public string? LastError { get; private set; }

    public string SettingsFilePath { get; }

    public string SettingsDirectory { get; }

    private string TempFilePath => SettingsFilePath + ".tmp";
    private string V1BackupPath => SettingsFilePath + ".v1.bak";
    private string V2BackupPath => SettingsFilePath + ".v2.bak";
    private string V3BackupPath => SettingsFilePath + ".v3.bak";

    /// <summary>
    /// 設定読み込み。失敗時は既定値で構築して内部状態として保持する。
    /// 戻り値: 正常読み込み成功 = true、ファイル無し/破損で既定値にフォールバック = false。
    /// </summary>
    public bool Load()
    {
        LastError = null;
        CleanupStaleTempFile();

        if (!File.Exists(SettingsFilePath))
        {
            Settings = new AppSettings { IsFirstLaunch = true };
            _loadedAsV1 = _loadedAsV2 = _loadedAsV3 = false;
            Log.Settings.Information("settings file not found, using defaults");
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LastError = "failed to open settings file: " + ex.Message;
            Log.Settings.Error(ex, "{Error}", LastError);
            Settings = new AppSettings();
            return false;
        }

        // 段階 1: JSON 構文としての妥当性のみを検証する（移植元 `ifs >> j` に対応）。
        // ここで失敗した場合のみ破損ファイルとしてリネームする。
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            LastError = "settings parse error: " + ex.Message;
            Log.Settings.Error(ex, "{Error}", LastError);
            RenameBroken();
            Settings = new AppSettings();
            return false;
        }

        // 段階 2: AppSettings への変換（移植元 `models::from_json(j, parsed)` に対応）。
        // 構文は正しいが型が不正な場合はここで失敗する。この場合はリネームしない
        // （移植元も renameBroken() を呼ばず、既定値へフォールバックするだけ）。
        using (document)
        {
            try
            {
                AppSettings? parsed = JsonSerializer.Deserialize<AppSettings>(document.RootElement, _jsonOptions);
                if (parsed is null)
                {
                    throw new JsonException("deserialized settings is null");
                }

                Settings = parsed;
                int loadedVersion = Settings.SchemaVersion;
                _loadedAsV1 = loadedVersion < 2;
                _loadedAsV2 = loadedVersion == 2;
                _loadedAsV3 = loadedVersion == 3;

                if (_loadedAsV1)
                {
                    Settings.SchemaVersion = 4;
                    CreateBackupIfAbsent(V1BackupPath, "v1");
                    Log.Settings.Information("loaded v1 settings, will migrate to v4 on next save");
                }
                else if (_loadedAsV2)
                {
                    Settings.SchemaVersion = 4;
                    CreateBackupIfAbsent(V2BackupPath, "v2");
                    Log.Settings.Information("loaded v2 settings, will migrate to v4 on next save");
                }
                else if (_loadedAsV3)
                {
                    Settings.SchemaVersion = 4;
                    CreateBackupIfAbsent(V3BackupPath, "v3");
                    Log.Settings.Information("loaded v3 settings, will migrate to v4 on next save");
                }
                else
                {
                    Log.Settings.Debug("loaded v4 settings");
                }

                return true;
            }
            catch (Exception ex)
            {
                LastError = "settings deserialize error: " + ex.Message;
                Log.Settings.Error(ex, "{Error}", LastError);
                Settings = new AppSettings();
                return false;
            }
        }
    }

    /// <summary>設定保存（アトミック書き込み）。常に v4 で書き出す。</summary>
    public bool Save()
    {
        LastError = null;
        Settings.SchemaVersion = 4;

        string content;
        try
        {
            content = JsonSerializer.Serialize(Settings, _jsonOptions) + "\n";
        }
        catch (Exception ex)
        {
            LastError = "serialize error: " + ex.Message;
            Log.Settings.Error(ex, "{Error}", LastError);
            return false;
        }

        if (!WriteAtomic(content))
        {
            return false;
        }

        Log.Settings.Debug("settings saved ({Bytes} bytes)", Encoding.UTF8.GetByteCount(content));
        _loadedAsV1 = _loadedAsV2 = _loadedAsV3 = false;
        return true;
    }

    /// <summary>既定値に戻して保存する。</summary>
    public bool Reset()
    {
        Settings = new AppSettings();
        return Save();
    }

    private static string DefaultSettingsDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IMEIndicator");

    // TypeInfoResolver + Encoder + 各種コンバータの組み合わせは JsonSourceGenerationOptions
    // 属性だけでは表現できないため、実行時にここで組み立てる（SettingsJsonContext.cs の remarks 参照）。
    private static JsonSerializerOptions BuildJsonOptions() => new()
    {
        TypeInfoResolver = SettingsJsonContext.Default,
        WriteIndented = true,
        NewLine = "\n", // 既定は Environment.NewLine（Windows では \r\n）。契約は LF 固定のため明示する。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new DoubleWithPointConverter(),
            new LogLevelConverter(),
            new PriorityLevelConverter(),
            new HookModeConverter(),
        },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private void CleanupStaleTempFile()
    {
        try
        {
            if (File.Exists(TempFilePath))
            {
                File.Delete(TempFilePath);
            }
        }
        catch
        {
            // 起動時のベストエフォートなクリーンアップ。失敗しても続行する。
        }
    }

    // 既にバックアップが存在する場合は上書きしない（最古を保持。移植元 createV1/V2/V3Backup と同じ）。
    private void CreateBackupIfAbsent(string backupPath, string label)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                return;
            }
            File.Copy(SettingsFilePath, backupPath, overwrite: true);
            Log.Settings.Information("settings {Label} backup created", label);
        }
        catch (Exception ex)
        {
            Log.Settings.Warning(ex, "settings {Label} backup failed", label);
        }
    }

    // 破損ファイルをローカル時刻のタイムスタンプ付きでリネームする（移植元 renameBroken、GetLocalTime 使用）。
    private void RenameBroken()
    {
        string broken = SettingsFilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        try
        {
            File.Move(SettingsFilePath, broken);
            Log.Settings.Warning("renamed broken settings file to {Path}", broken);
        }
        catch (Exception ex)
        {
            Log.Settings.Warning(ex, "settings broken rename failed");
        }
    }

    private bool WriteAtomic(string content)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            // BOM なし UTF-8（契約: settings-compat-contract.md §バイト互換の要件）
            File.WriteAllText(TempFilePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(TempFilePath, SettingsFilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LastError = "write failed: " + ex.Message;
            Log.Settings.Error(ex, "{Error}", LastError);
            return false;
        }
    }
}
