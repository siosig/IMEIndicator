using IMEIndicator.Services;
using Xunit;

namespace IMEIndicator.Tests;

/// <summary>
/// ECoreCpuInfo の基本動作テスト
/// </summary>
public class ECoreCpuInfoTests
{
    [Fact]
    public void Instance_DoesNotThrow()
    {
        // P/Invoke 呼び出しが例外を投げないことを確認
        var info = ECoreCpuInfo.Instance;
        Assert.NotNull(info);
    }

    [Fact]
    public void ECoreMask_IsNonNegative()
    {
        var info = ECoreCpuInfo.Instance;
        Assert.True(info.ECoreMask >= 0);
    }

    [Fact]
    public void ECoreCount_IsNonNegative()
    {
        var info = ECoreCpuInfo.Instance;
        Assert.True(info.ECoreCount >= 0);
    }

    [Fact]
    public void PCoreCount_IsNonNegative()
    {
        var info = ECoreCpuInfo.Instance;
        Assert.True(info.PCoreCount >= 0);
    }

    [Fact]
    public void HasECores_ConsistentWithCounts()
    {
        var info = ECoreCpuInfo.Instance;
        if (info.HasECores)
        {
            Assert.True(info.ECoreCount > 0);
            Assert.True(info.PCoreCount > 0);
            Assert.True(info.ECoreMask > 0);
        }
        else
        {
            Assert.Equal(0, info.ECoreMask);
        }
    }

    [Fact]
    public void Instance_ReturnsSameObject()
    {
        // Lazy<T> によるシングルトン動作を確認
        var a = ECoreCpuInfo.Instance;
        var b = ECoreCpuInfo.Instance;
        Assert.Same(a, b);
    }
}
