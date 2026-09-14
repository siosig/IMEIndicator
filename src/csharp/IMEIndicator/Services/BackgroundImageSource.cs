// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IMEIndicator.Services;

/// <summary>
/// 背景画像ウィンドウに表示するビットマップの読込・フォールバック・contain フィット描画を担う、
/// ウィンドウ・ハンドル・Win32 に一切依存しない静的サービス。表示矩形の位置計算を担う
/// <see cref="BackgroundImageLayout"/>（値オブジェクト・純関数）と対になるクラスで、こちらは
/// 表示する画素そのもの（デコード・合成）を担当する。
/// specs/016-custom-background-image/contracts/background-image-source-contract.md の実装
/// （根拠: research.md R-2〜R-6、data-model.md §2）。
/// </summary>
/// <remarks>
/// フォールバック連鎖（契約 §フォールバック連鎖）:
/// <code>
/// 1. ユーザー指定ファイル: imagePath が空でなければ File.ReadAllBytes → MemoryStream →
///    Image.FromStream でデコードする（Image.FromFile は使わない。ファイルハンドルを
///    保持し続けないため）
/// 2. 同梱の既定画像: 手順 1 が未指定または失敗した場合、埋め込みリソース
///    "ime-on-background.png" を読み込む（解決方法は <c>BackgroundImageWindow</c> の
///    private メソッド <c>TryLoadScaledBitmap</c> と同じ: GetManifestResourceNames() の末尾一致）
/// 3. どちらも失敗: null を返す（例外を上位へ伝播させない）
/// </code>
/// 失敗の理由はすべて <see cref="Log.Display"/> へ警告ログとして記録したうえで、次の
/// フォールバック段階へ進む。本クラスはキャッシュを持たないステートレスな純関数として実装し、
/// 呼び出しのたびに毎回読込・デコードを行う。再読込が必要かどうかの判断（サイズ変化・
/// imagePath 変化の検知）は呼び出し側 <c>BackgroundImageWindow.Relayout()</c> の責務とする
/// （契約 §再読込トリガー）。
/// </remarks>
public static class BackgroundImageSource
{
    // BackgroundImageWindow.ResourceFileName と同じ値（契約: 同梱既定画像の埋め込みリソース名）。
    // T010 で BackgroundImageWindow 側が本クラスへ委譲するまでの間、意図的に重複させている。
    private const string ResourceFileName = "ime-on-background.png";

    /// <summary>
    /// 背景画像のビットマップを読み込む。ユーザー指定 PNG（<paramref name="imagePath"/>）を優先し、
    /// 未指定、またはその読込・デコードに失敗した場合は同梱の既定画像へフォールバックする。
    /// デコードした元画像は縦横比を保ったまま（contain フィット）中央へ配置し、
    /// <paramref name="width"/> × <paramref name="height"/> の出力ビットマップへ描画する。
    /// </summary>
    /// <param name="imagePath">
    /// ユーザー指定 PNG のファイルパス。空文字・空白のみの場合は「未指定」として扱い、
    /// 既定画像の読込へ直行する（null は渡されない前提。呼び出し側の
    /// <c>BackgroundImageSettings.ImagePath</c> は非 null で <c>Clamp()</c> 済み）。
    /// </param>
    /// <param name="width">出力ビットマップの幅（物理 px）。0 以下の場合は <see langword="null"/> を返す。</param>
    /// <param name="height">出力ビットマップの高さ（物理 px）。0 以下の場合は <see langword="null"/> を返す。</param>
    /// <returns>
    /// 新規生成された <see cref="Bitmap"/>（<see cref="PixelFormat.Format32bppPArgb"/>）。所有権は
    /// 呼び出し側に移り、呼び出し側が Dispose する（本メソッド内部で使った中間リソースは
    /// 呼び出し内で完結し、外部にハンドルを残さない）。<paramref name="width"/>・
    /// <paramref name="height"/> が 0 以下、またはユーザー指定・既定画像のいずれの読込・デコードにも
    /// 失敗した場合は <see langword="null"/>（例外は投げない）。
    /// </returns>
    public static Bitmap? Load(string imagePath, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return TryLoadUserImage(imagePath, width, height) ?? TryLoadEmbeddedDefaultImage(width, height);
    }

    // フォールバック連鎖 手順 1: ユーザー指定ファイルを読み込み、contain フィットで合成する。
    // Image.FromStream は「Image の生存期間中ストリームを開いたままにする」ことを要求するため
    // （公式ドキュメントの既知の制約）、MemoryStream と デコード元 Image は ComposeContainFit
    // （実体は Graphics.DrawImage）が完了するまで同一スコープの using で保持する。
    private static Bitmap? TryLoadUserImage(string imagePath, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            // 「未指定」は失敗ではないため、警告ログは出さずに次の段階（既定画像）へ委ねる。
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(imagePath);
            using var stream = new MemoryStream(bytes);
            using Image source = Image.FromStream(stream);
            return ComposeContainFit(source, width, height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                        or ArgumentException or InvalidOperationException
                                        or ExternalException or OutOfMemoryException)
        {
            // GDI+ は不正な画像データも慣例的に OutOfMemoryException で報告するため捕捉に含める
            // （Image.FromStream の既知の挙動。BackgroundImageWindow.TryLoadScaledBitmap と同じ扱い）。
            Log.Display.Warning(
                ex,
                "BackgroundImageSource: ユーザー指定画像 '{ImagePath}' の読込・デコードに失敗しました。既定画像へフォールバックします。",
                imagePath);
            return null;
        }
    }

    // フォールバック連鎖 手順 2: 同梱の既定画像（埋め込みリソース）を読み込み、contain フィットで合成する。
    // 解決方法・例外の捕捉パターンは BackgroundImageWindow.TryLoadScaledBitmap と同一にする
    // （契約 §フォールバック連鎖 手順 2）。Assembly.GetExecutingAssembly() ではなく
    // typeof(BackgroundImageSource).Assembly を使い、本体アセンブリを指すことを保証する。
    private static Bitmap? TryLoadEmbeddedDefaultImage(int width, int height)
    {
        try
        {
            Assembly assembly = typeof(BackgroundImageSource).Assembly;
            string? resourceName = assembly.GetManifestResourceNames()
                .SingleOrDefault(n => n.EndsWith(ResourceFileName, StringComparison.Ordinal));
            if (resourceName is null)
            {
                Log.Display.Warning("BackgroundImageSource: 埋め込みリソース '{ResourceFileName}' が見つかりません。", ResourceFileName);
                return null;
            }

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                Log.Display.Warning("BackgroundImageSource: 埋め込みリソース '{ResourceName}' のストリームを取得できません。", resourceName);
                return null;
            }

            using Image source = Image.FromStream(stream);
            return ComposeContainFit(source, width, height);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException
                                        or ExternalException or OutOfMemoryException)
        {
            // GDI+ は不正な画像データも慣例的に OutOfMemoryException で報告するため捕捉に含める
            // （Image.FromStream の既知の挙動）。
            Log.Display.Warning(ex, "BackgroundImageSource: 同梱既定画像のデコードに失敗しました。");
            return null;
        }
    }

    // デコード済みの元画像（source）を、縦横比を保ったまま（contain フィット）width×height の
    // 出力ビットマップの中央へ描画する（契約 §Contain フィット（縦横比保持））。
    // 表示枠に対して余る領域は Clear(Transparent) 済みのため透明のまま残る（追加処理不要）。
    private static Bitmap ComposeContainFit(Image source, int width, int height)
    {
        double scale = Math.Min((double)width / source.Width, (double)height / source.Height);
        int destWidth = (int)Math.Round(source.Width * scale);
        int destHeight = (int)Math.Round(source.Height * scale);
        int destX = (width - destWidth) / 2;
        int destY = (height - destHeight) / 2;

        // UpdateLayeredWindow(AC_SRC_ALPHA) は premultiplied alpha を要求するため、Format32bppPArgb で
        // 描画する（LayeredWindow.Present の注意事項、BackgroundImageWindow.TryLoadScaledBitmap と同じ理由）。
        var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(result))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(destX, destY, destWidth, destHeight));
        }
        return result;
    }
}
