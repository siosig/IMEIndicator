// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services.Hotkey.Commands;

using Xunit;

namespace IMEIndicator.Tests.Hotkey.Commands;

/// <summary>
/// VirtualDesktopCommands.IsWindows11OrLater(Version) の単体テスト。
/// 根拠: src/cpp/services/hotkey/platform/VirtualDesktop.cpp の
/// isWindows11OrLater()（VerifyVersionInfoW によるビルド番号 22000 以上判定）の移植先。
/// 実行環境の実際の OS バージョンに依存させないため、Version を直接注入できる
/// オーバーロードを対象にする（無引数版 IsWindows11OrLater() は Environment.OSVersion に
/// 委譲するだけの薄いラッパーのため、ここでは検証しない）。
/// </summary>
public sealed class VirtualDesktopCommandsTests
{
    [Theory]
    [InlineData(10, 0, 22000)]  // Windows 11 21H2 最小ビルド（境界値ちょうど）
    [InlineData(10, 0, 22621)]  // Windows 11 22H2
    [InlineData(10, 0, 26100)]  // Windows 11 24H2
    [InlineData(11, 0, 22000)]  // メジャーが将来 11 に上がった場合でもビルドが条件を満たせば true
    public void ReturnsTrueForWindows11OrLaterBuilds(int major, int minor, int build)
    {
        var version = new Version(major, minor, build, 0);
        Assert.True(VirtualDesktopCommands.IsWindows11OrLater(version));
    }

    [Theory]
    [InlineData(10, 0, 19045)]  // Windows 10 22H2（Windows 11 未満）
    [InlineData(10, 0, 19044)]
    [InlineData(6, 3, 9600)]    // Windows 8.1
    [InlineData(10, 0, 21999)]  // 境界値の 1 つ手前
    public void ReturnsFalseForOlderThanWindows11Builds(int major, int minor, int build)
    {
        var version = new Version(major, minor, build, 0);
        Assert.False(VirtualDesktopCommands.IsWindows11OrLater(version));
    }

    [Fact]
    public void BoundaryBuildNumberIsInclusive()
    {
        Assert.True(VirtualDesktopCommands.IsWindows11OrLater(new Version(10, 0, 22000, 0)));
        Assert.False(VirtualDesktopCommands.IsWindows11OrLater(new Version(10, 0, 21999, 0)));
    }

    [Fact]
    public void MajorVersionBelow10IsNeverWindows11OrLater()
    {
        // ビルド番号だけが大きくてもメジャーが 10 未満なら false
        // （AND 条件のメジャー側が独立して効いていることの確認。実在しない組み合わせ）。
        Assert.False(VirtualDesktopCommands.IsWindows11OrLater(new Version(9, 0, 99999, 0)));
    }

    [Fact]
    public void HighMajorVersionAloneIsNotSufficientWithoutBuildNumber()
    {
        // メジャーが 10 以上でもビルドが 22000 未満なら false
        // （メジャーが上がれば無条件に true になる「OR」判定ではなく、
        // VerifyVersionInfoW と同じ「各フィールド独立の AND」判定であることの確認。
        // 実在しない組み合わせ）。
        Assert.False(VirtualDesktopCommands.IsWindows11OrLater(new Version(11, 0, 21999, 0)));
    }
}
