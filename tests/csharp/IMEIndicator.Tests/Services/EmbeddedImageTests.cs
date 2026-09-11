// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

using IMEIndicator.Services;

using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// 埋め込みリソース ime-on-background.png の読み込み・縮小の検証（T023）。
/// 現行 C++ 版 tests/cpp/unit/WicImageLoaderTests.cpp の置き換え。
/// </summary>
/// <remarks>
/// C++ 版との相違点（意図的な変更）:
/// <list type="bullet">
/// <item>C++ 版はファイルシステムから読み WIC（WICBitmapInterpolationModeHighQualityCubic）で
/// デコード・縮小し、四隅 alpha=0・中央 alpha=255 を厳密一致で検証する。C# 版はアセンブリの
/// 埋め込みリソースから読み GDI+（InterpolationMode.HighQualityBicubic）で縮小するため、
/// 実装（ライブラリ）が異なり数値が厳密には一致しない前提で、近似（許容誤差つき）で検証する。</item>
/// <item>リソース名は <c>Assembly.GetManifestResourceNames()</c> から末尾一致で解決する
/// （RootNamespace 前提のハードコード名に依存しない）。埋め込み元は本体アセンブリ
/// （IMEIndicator.csproj の EmbeddedResource）であり、このテストが属するテストアセンブリ自身
/// ではないため、<c>Assembly.GetExecutingAssembly()</c>（テストアセンブリを指してしまう）
/// ではなく <c>typeof(BackgroundImageLayout).Assembly</c>（本体アセンブリ）から取得する。</item>
/// </list>
/// </remarks>
public sealed class EmbeddedImageTests
{
    private const string ResourceFileName = "ime-on-background.png";
    private const int TargetSize = 128;

    // 「ごく小さい値」「ごく大きい値」の許容誤差（256 階調中 5 階調 ≒ 2%）。
    // WIC と GDI+ のリサンプリング実装差による数値の微差を吸収する。
    private const byte NearTransparentMaxAlpha = 5;
    private const byte NearOpaqueMinAlpha = 250;

    [Fact]
    public void EmbeddedResource_IsRegisteredInMainAssembly()
    {
        var names = typeof(BackgroundImageLayout).Assembly.GetManifestResourceNames();
        Assert.Contains(names, n => n.EndsWith(ResourceFileName, StringComparison.Ordinal));
    }

    [Fact]
    public void ResizedTo128_HasRequestedDimensions()
    {
        using var resized = LoadAndResizeTo128();

        Assert.Equal(TargetSize, resized.Width);
        Assert.Equal(TargetSize, resized.Height);
    }

    [Fact]
    public void ResizedTo128_CornersAreNearTransparent()
    {
        using var resized = LoadAndResizeTo128();

        AssertNearTransparent(resized.GetPixel(0, 0));
        AssertNearTransparent(resized.GetPixel(TargetSize - 1, 0));
        AssertNearTransparent(resized.GetPixel(0, TargetSize - 1));
        AssertNearTransparent(resized.GetPixel(TargetSize - 1, TargetSize - 1));
    }

    [Fact]
    public void ResizedTo128_CenterIsNearOpaque()
    {
        using var resized = LoadAndResizeTo128();

        var center = resized.GetPixel(TargetSize / 2, TargetSize / 2);
        Assert.True(center.A >= NearOpaqueMinAlpha, $"center alpha={center.A}, expected >= {NearOpaqueMinAlpha}");
    }

    private static void AssertNearTransparent(Color pixel) =>
        Assert.True(pixel.A <= NearTransparentMaxAlpha, $"corner alpha={pixel.A}, expected <= {NearTransparentMaxAlpha}");

    // 埋め込み PNG を本体アセンブリから読み込み、GDI+ の HighQualityBicubic で 128×128 へ縮小する。
    // 呼び出し側で using により破棄すること。
    private static Bitmap LoadAndResizeTo128()
    {
        var assembly = typeof(BackgroundImageLayout).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(ResourceFileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"埋め込みリソース '{ResourceFileName}' が本体アセンブリに見つからない。");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"埋め込みリソース '{resourceName}' のストリームを取得できない。");

        using var source = Image.FromStream(stream);

        var resized = new Bitmap(TargetSize, TargetSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, TargetSize, TargetSize));
        }
        return resized;
    }
}
