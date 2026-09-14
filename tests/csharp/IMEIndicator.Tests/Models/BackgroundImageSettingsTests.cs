// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;

using Xunit;

namespace IMEIndicator.Tests.Models;

/// <summary>
/// BackgroundImageSettings の既定値、および AppSettings.Clamp() による値域丸めの単体テスト。
/// 契約: specs/015-split-appearance-settings/data-model.md §1・§3。
/// </summary>
public sealed class BackgroundImageSettingsTests
{
    [Fact]
    public void DefaultsMatchPreFeatureAppearance()
    {
        var s = new BackgroundImageSettings();

        // 導入前の見た目（AppConstants.BackgroundImageLogicalSize=128、Present の固定 255=不透明）
        // と一致すること（FR-009）。
        Assert.False(s.IsVisible);
        Assert.Equal(128.0, s.Size);
        Assert.Equal(1.0, s.Opacity);
    }

    [Fact]
    public void DefaultImagePathIsEmpty()
    {
        var s = new BackgroundImageSettings();
        Assert.Equal(string.Empty, s.ImagePath);
    }

    [Theory]
    [InlineData(0.0, 32.0)]
    [InlineData(10000.0, 512.0)]
    [InlineData(-1.0, 32.0)]
    [InlineData(300.0, 300.0)]
    public void ClampBoundsSizeTo32_512(double input, double expected)
    {
        var settings = new AppSettings();
        settings.BackgroundImage.Size = input;

        settings.Clamp();

        Assert.Equal(expected, settings.BackgroundImage.Size);
    }

    [Theory]
    [InlineData(0.0, 0.1)]
    [InlineData(2.0, 1.0)]
    [InlineData(-1.0, 0.1)]
    [InlineData(0.5, 0.5)]
    public void ClampBoundsOpacityTo010_100(double input, double expected)
    {
        var settings = new AppSettings();
        settings.BackgroundImage.Opacity = input;

        settings.Clamp();

        Assert.Equal(expected, settings.BackgroundImage.Opacity);
    }

    [Theory]
    [InlineData("  C:\\images\\custom.png  ", "C:\\images\\custom.png")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    public void ClampTrimsImagePath(string input, string expected)
    {
        var settings = new AppSettings();
        settings.BackgroundImage.ImagePath = input;

        settings.Clamp();

        Assert.Equal(expected, settings.BackgroundImage.ImagePath);
    }
}
