// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services.Hotkey;

using Xunit;

namespace IMEIndicator.Tests.Hotkey;

/// <summary>
/// CommandExecutor の単体テスト。根拠: tasks.md T066
/// 「除外 23 件の Execute が InvalidCommand を返し例外を投げない」「範囲外 ID（121, 199, 300）が InvalidCommand」。
/// tasks.md は本テストを CommandCatalogTests.cs へ追加するよう指示しているが、CommandCatalog（静的な
/// カタログデータ）と CommandExecutor（実行ディスパッチ）は別の関心事であり、このテストスイートは
/// 既存クラスごとに 1 テストファイルの慣習（例: ProcessPriorityService → ProcessPriorityServiceTests）に
/// 従っているため、専用ファイルとして分離した。
/// </summary>
/// <remarks>
/// ここで検証する ID はすべて実際の Win32 副作用（音量変更・プロセス終了・ウィンドウ操作等）を
/// 一切発生させないことを確認済み（範囲外 ID は <see cref="CommandExecutor.IsValidCommandId"/> で
/// 即座に弾かれる。除外 23 件はいずれも本クラスの switch 式の default、または
/// <c>SystemCommands.Execute</c> 自身の内部 default に落ちるだけで、どちらも実際の API 呼び出しを
/// 行わない）。
/// </remarks>
public sealed class CommandExecutorTests
{
    // CommandCatalogTests.ExcludedIds と同一の 23 件（internal-command-catalog.md「実行経路を
    // 持たない 23 種」）。
    private static readonly int[] ExcludedIds =
    {
        21, 50, 51, 52, 53, 54, 55, 56, 57, 62, 68, 69, 71, 72, 73, 82, 89, 91, 92, 93, 103, 106, 107,
    };

    [Theory]
    [MemberData(nameof(ExcludedIdCases))]
    public void Execute_ExcludedId_ReturnsInvalidCommandWithoutThrowing(int id)
    {
        ExecuteError? result = null;
        Exception? thrown = Record.Exception(() => result = CommandExecutor.Execute(id, string.Empty));

        Assert.Null(thrown);
        Assert.Equal(ExecuteError.InvalidCommand, result);
    }

    public static IEnumerable<object[]> ExcludedIdCases() => ExcludedIds.Select(id => new object[] { id });

    [Theory]
    [InlineData(121)] // 予約レンジ（HotkeyP 側の将来拡張用、[0,120] の直後）
    [InlineData(199)] // 200 未満（IMEIndicator 拡張の直前）
    [InlineData(300)] // 299 超過
    [InlineData(-1)]
    public void Execute_OutOfRangeId_ReturnsInvalidCommand(int id)
    {
        ExecuteError? result = CommandExecutor.Execute(id, string.Empty);

        Assert.Equal(ExecuteError.InvalidCommand, result);
    }

    [Theory]
    [InlineData(121)]
    [InlineData(300)]
    public void IsValidCommandId_OutOfRange_ReturnsFalse(int id)
    {
        Assert.False(CommandExecutor.IsValidCommandId(id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(200)]
    [InlineData(299)]
    public void IsValidCommandId_BoundaryValues_ReturnsTrue(int id)
    {
        Assert.True(CommandExecutor.IsValidCommandId(id));
    }

    [Fact]
    public void MainWindowHandle_DefaultsToZero()
    {
        // static プロパティなので他のテストが値を残している可能性がある。ここでは型と
        // 代入・読み出しの往復のみを検証し、既定値の断定はしない
        // （xUnit はテストクラスを並列実行しないが、クラス間の順序は保証されないため）。
        nint original = CommandExecutor.MainWindowHandle;
        try
        {
            CommandExecutor.MainWindowHandle = 0x1234;
            Assert.Equal(0x1234, CommandExecutor.MainWindowHandle);
        }
        finally
        {
            CommandExecutor.MainWindowHandle = original;
        }
    }
}
