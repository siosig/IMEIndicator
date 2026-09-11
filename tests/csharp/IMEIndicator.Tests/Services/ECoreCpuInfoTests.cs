// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="ECoreCpuInfo"/> のテスト。現行 tests/cpp/unit/ECoreCpuInfoTests.cpp の移植。
/// ホストの CPU 構成に依存するため、E-Core 有無に応じた最低限のチェックのみ行う
/// （現行 C++ 版と同じ方針。実行環境を断定せず、例外を投げずに完了することを確認する）。
/// </summary>
public sealed class ECoreCpuInfoTests
{
    [Fact]
    public void Instance_ReturnsSameInstanceEachTime()
    {
        ECoreCpuInfo a = ECoreCpuInfo.Instance;
        ECoreCpuInfo b = ECoreCpuInfo.Instance;

        Assert.Same(a, b);
    }

    [Fact]
    public void Detect_CompletesWithoutThrowing()
    {
        Exception? exception = Record.Exception(() => _ = ECoreCpuInfo.Instance);

        Assert.Null(exception);
    }

    [Fact]
    public void NonHybridCpu_HasZeroMaskAndCount()
    {
        ECoreCpuInfo info = ECoreCpuInfo.Instance;
        if (!info.HasECores)
        {
            Assert.Equal((nint)0, info.ECoreMask);
            Assert.Equal(0, info.ECoreCount);
        }
    }

    [Fact]
    public void HybridCpu_ExposesNonZeroMaskAndCounts()
    {
        ECoreCpuInfo info = ECoreCpuInfo.Instance;
        if (info.HasECores)
        {
            Assert.NotEqual((nint)0, info.ECoreMask);
            Assert.True(info.ECoreCount > 0);
            Assert.True(info.PCoreCount > 0);
        }
    }
}
