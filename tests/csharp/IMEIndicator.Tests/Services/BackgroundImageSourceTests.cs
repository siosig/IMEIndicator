// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Drawing.Imaging;

using IMEIndicator.Services;

using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="BackgroundImageSource.Load"/> のうち「ユーザー指定ファイルが正常に読める場合」の単体テスト（T009）。
/// specs/016-custom-background-image/contracts/background-image-source-contract.md §検証方法、
/// および §Contain フィット（縦横比保持） の計算式を検証する。
/// </summary>
/// <remarks>
/// <see cref="EmbeddedImageTests"/>（同梱の既定画像・埋め込みリソースからの読込）とは異なり、
/// 本クラスは一時ディレクトリへ実際に PNG ファイルを書き出し、そのファイルパスを
/// <see cref="BackgroundImageSource.Load"/> に渡す（契約 §フォールバック連鎖 手順 1:
/// File.ReadAllBytes → MemoryStream → Image.FromStream の経路を通す）。
/// 許容誤差（<see cref="NearTransparentMaxAlpha"/>/<see cref="NearOpaqueMinAlpha"/>）の考え方は
/// <see cref="EmbeddedImageTests"/> と同じ: GDI+ の HighQualityBicubic 補間によるエッジのにじみを
/// 吸収するための、ごく小さい/大きい値の閾値であり、厳密な 0/255 一致は要求しない。
/// </remarks>
public sealed class BackgroundImageSourceTests
{
    // EmbeddedImageTests と同じ許容誤差（256 階調中 5 階調 ≒ 2%）。
    private const byte NearTransparentMaxAlpha = 5;
    private const byte NearOpaqueMinAlpha = 250;

    // PowerModeBackupTests.TempDir と同じ方針: テストごとに固有の一時ファイルを作り、
    // 破棄時に削除する（実ファイルシステムへ副作用を残さない）。
    private sealed class TempPngFile : IDisposable
    {
        public string Path { get; }

        public TempPngFile()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "imeindicator_bgimgsrc_" + Environment.ProcessId + "_" + Guid.NewGuid().ToString("N") + ".png");
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch
            {
                // ベストエフォート。テスト後片付けの失敗でテスト自体は失敗させない。
            }
        }
    }

    [Fact]
    public void Load_SquareUserPng_ReturnsRequestedSizeWithOpaqueCenter()
    {
        using var temp = new TempPngFile();
        CreateSolidColorPng(temp.Path, 64, 64, Color.Red);

        Bitmap? loaded = BackgroundImageSource.Load(temp.Path, 128, 128);

        Assert.NotNull(loaded);
        using Bitmap result = loaded!;

        Assert.Equal(128, result.Width);
        Assert.Equal(128, result.Height);

        Color center = result.GetPixel(64, 64);
        Assert.True(center.A >= NearOpaqueMinAlpha, $"center alpha={center.A}, expected >= {NearOpaqueMinAlpha}");
    }

    [Fact]
    public void Load_NonSquareUserPng_ContainFitsWithTransparentMarginsAndNoDistortion()
    {
        // 400x200 の横長画像 → 128x128 の正方形表示枠。contain フィットの計算
        // （契約 §Contain フィット（縦横比保持））:
        //   scale  = min(128/400, 128/200) = 0.32
        //   destW  = round(400 * 0.32) = 128, destH = round(200 * 0.32) = 64
        //   destX  = (128 - 128) / 2 = 0,      destY  = (128 - 64) / 2 = 32
        // つまり画像本体は y=32〜95 の帯にのみ描画され、上下 y=0〜31 / y=96〜127 は
        // Graphics.Clear(Transparent) のまま透明で残るはず。境界付近（y=31/32, y=95/96）は
        // HighQualityBicubic 補間のにじみが乗りうるため、境界から離れた y=10 / y=120 でサンプルする。
        using var temp = new TempPngFile();
        CreateSolidColorPng(temp.Path, 400, 200, Color.Blue);

        Bitmap? loaded = BackgroundImageSource.Load(temp.Path, 128, 128);

        Assert.NotNull(loaded);
        using Bitmap result = loaded!;

        Assert.Equal(128, result.Width);
        Assert.Equal(128, result.Height);

        Color center = result.GetPixel(64, 64);
        Assert.True(center.A >= NearOpaqueMinAlpha, $"center alpha={center.A}, expected >= {NearOpaqueMinAlpha}");

        // 歪み（引き伸ばして 128x128 全面を不透明にする描画）が起きていないことの検証:
        // 縦横比を保った contain フィットであれば、上下の余白が実際に透明として残るはず。
        Color topMargin = result.GetPixel(64, 10);
        Color bottomMargin = result.GetPixel(64, 120);
        Assert.True(topMargin.A <= NearTransparentMaxAlpha, $"top margin alpha={topMargin.A}, expected <= {NearTransparentMaxAlpha}");
        Assert.True(bottomMargin.A <= NearTransparentMaxAlpha, $"bottom margin alpha={bottomMargin.A}, expected <= {NearTransparentMaxAlpha}");
    }

    [Fact]
    public void Load_EmptyImagePath_FallsBackToEmbeddedDefaultImage()
    {
        // 016-custom-background-image US2: 未指定（空文字）は同梱の既定画像を使う（FR-004）。
        // 既定画像は正方形のため、EmbeddedImageTests と同じ観点（中心近似不透明・四隅近似透明）で検証する。
        Bitmap? loaded = BackgroundImageSource.Load(string.Empty, 128, 128);

        Assert.NotNull(loaded);
        using Bitmap result = loaded!;

        Assert.Equal(128, result.Width);
        Assert.Equal(128, result.Height);

        Color center = result.GetPixel(64, 64);
        Assert.True(center.A >= NearOpaqueMinAlpha, $"center alpha={center.A}, expected >= {NearOpaqueMinAlpha}");

        Color corner = result.GetPixel(0, 0);
        Assert.True(corner.A <= NearTransparentMaxAlpha, $"corner alpha={corner.A}, expected <= {NearTransparentMaxAlpha}");
    }

    [Fact]
    public void Load_WhitespaceOnlyImagePath_FallsBackToEmbeddedDefaultImage()
    {
        // 016-custom-background-image: AppSettings.Clamp() がトリムするため実運用では空白のみの
        // 値は保持されないが、Load() 自体も空白のみを「未指定」として扱う契約（IsNullOrWhiteSpace）
        // であることを直接検証する。
        Bitmap? loaded = BackgroundImageSource.Load("   ", 128, 128);

        Assert.NotNull(loaded);
        using Bitmap result = loaded!;
        Assert.Equal(128, result.Width);
        Assert.Equal(128, result.Height);
    }

    [Fact]
    public void Load_NonexistentFilePath_FallsBackToEmbeddedDefaultImageWithoutThrowing()
    {
        // 016-custom-background-image US3 FR-006: 存在しないファイルを指定しても異常終了せず
        // 既定画像へフォールバックする。
        string nonexistentPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "imeindicator_bgimgsrc_missing_" + Guid.NewGuid().ToString("N") + ".png");

        Bitmap? loaded = BackgroundImageSource.Load(nonexistentPath, 128, 128);

        Assert.NotNull(loaded);
        using Bitmap result = loaded!;
        Assert.Equal(128, result.Width);
        Assert.Equal(128, result.Height);
        Color center = result.GetPixel(64, 64);
        Assert.True(center.A >= NearOpaqueMinAlpha, $"center alpha={center.A}, expected >= {NearOpaqueMinAlpha}");
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToEmbeddedDefaultImageWithoutThrowing()
    {
        // 016-custom-background-image US3 FR-006: PNG として読めないファイル（拡張子は .png だが
        // 実体は無関係なバイト列）を指定しても異常終了せず既定画像へフォールバックする。
        using var temp = new TempPngFile();
        File.WriteAllBytes(temp.Path, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        Bitmap? loaded = BackgroundImageSource.Load(temp.Path, 128, 128);

        Assert.NotNull(loaded);
        using Bitmap result = loaded!;
        Assert.Equal(128, result.Width);
        Assert.Equal(128, result.Height);
    }

    [Theory]
    [InlineData(0, 128)]
    [InlineData(128, 0)]
    [InlineData(-1, 128)]
    public void Load_NonPositiveWidthOrHeight_ReturnsNull(int width, int height)
    {
        // 016-custom-background-image contracts/background-image-source-contract.md §公開API。
        Bitmap? loaded = BackgroundImageSource.Load(string.Empty, width, height);
        Assert.Null(loaded);
    }

    // BackgroundImageSource.Load へ渡すための単色・不透明 PNG を一時ファイルへ書き出す。
    private static void CreateSolidColorPng(string path, int width, int height, Color color)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(color);
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
