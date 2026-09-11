// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Text.Json;
using IMEIndicator.Models;
using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="PowerModeBackup"/> のテスト。現行 tests/cpp/unit/PowerModeBackupTests.cpp の移植。
/// C# 版は公開 API を Save(PowerMode) / TryLoad(out PowerMode) に単純化しているため
/// （PowerModeBackup.cs の remarks 参照）、C++ 版のように savedAtUnixMs を直接注入するテストは
/// 実施できない。代わりに <see cref="Save_WritesSchemaVersion1WithTimestampAndProcessId"/> で
/// 実際に書き出された JSON を検証し、ワイヤーフォーマットの互換性を確認する。
/// </summary>
public sealed class PowerModeBackupTests
{
    // 現行 tests/cpp/unit/PowerModeBackupTests.cpp の TempDir、および
    // SettingsManagerTests.cs の TempDir と同じ方針: テストごとに固有の一時ディレクトリを作り、
    // 破棄時に削除する（実ファイルシステムへ副作用を残さない）。
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "imeindicator_pwbk_" + Environment.ProcessId + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // ベストエフォート。テスト後片付けの失敗でテスト自体は失敗させない。
            }
        }
    }

    [Fact]
    public void RoundTripSaveAndLoad_ReturnsSameMode()
    {
        using TempDir tmp = new();
        PowerModeBackup backup = new(tmp.Path);

        Assert.True(backup.Save(PowerMode.BestPerformance));

        Assert.True(backup.TryLoad(out PowerMode loaded));
        Assert.Equal(PowerMode.BestPerformance, loaded);
    }

    [Fact]
    public void Save_WritesSchemaVersion1WithTimestampAndProcessId()
    {
        using TempDir tmp = new();
        PowerModeBackup backup = new(tmp.Path);
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.True(backup.Save(PowerMode.BestPowerEfficiency));
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(backup.FilePath));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("BestPowerEfficiency", doc.RootElement.GetProperty("previousMode").GetString());
        long savedAt = doc.RootElement.GetProperty("savedAtUnixMs").GetInt64();
        Assert.InRange(savedAt, before, after);
        Assert.Equal((uint)Environment.ProcessId, doc.RootElement.GetProperty("processId").GetUInt32());
    }

    [Fact]
    public void TryLoad_FileMissing_ReturnsFalse()
    {
        using TempDir tmp = new();
        PowerModeBackup backup = new(tmp.Path);

        Assert.False(backup.TryLoad(out _));
    }

    [Fact]
    public void TryLoad_UnsupportedSchemaVersion_DeletesFileAndReturnsFalse()
    {
        using TempDir tmp = new();
        PowerModeBackup backup = new(tmp.Path);
        WriteRawJson(backup.FilePath, """{"schemaVersion":99,"previousMode":"Balanced","savedAtUnixMs":1,"processId":1}""");

        Assert.False(backup.TryLoad(out _));
        Assert.False(File.Exists(backup.FilePath));
    }

    [Fact]
    public void TryLoad_InvalidPreviousMode_DeletesFileAndReturnsFalse()
    {
        using TempDir tmp = new();
        PowerModeBackup backup = new(tmp.Path);
        WriteRawJson(backup.FilePath, """{"schemaVersion":1,"previousMode":"NotAMode","savedAtUnixMs":1,"processId":1}""");

        Assert.False(backup.TryLoad(out _));
        Assert.False(File.Exists(backup.FilePath));
    }

    [Fact]
    public void Delete_IsIdempotent()
    {
        using TempDir tmp = new();
        PowerModeBackup backup = new(tmp.Path);
        Assert.True(backup.Save(PowerMode.BestPowerEfficiency));

        backup.Delete();
        Assert.False(File.Exists(backup.FilePath));

        // 2 回目の呼び出しも例外にならない（移植元 RestoreIsIdempotentDeleteFile と同じ）
        backup.Delete();
        Assert.False(File.Exists(backup.FilePath));
    }

    private static void WriteRawJson(string path, string json)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
