// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Runtime.InteropServices;
using System.Windows.Automation;

using IMEIndicator.Interop;
using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// Windows 入力インジケーター（タスクバーの「あ」/「A」表示）の描画内容をピクセル判定し、
/// IME の ON/OFF を検出する。移植元: src/cpp/services/PixelIMEDetector.h / .cpp（シングルトン）。
/// </summary>
/// <remarks>
/// <para>
/// 検出アルゴリズム（移植元と 1:1。対象・取得プロパティ・成否判定基準を変更していない）:
/// <list type="number">
/// <item>UI Automation で <c>Shell_TrayWnd</c> 配下の Name が「入力インジケーター」または
///     「Input indicator」の要素を検索し、<c>BoundingRectangle</c>（スクリーン座標の矩形）を取得する。
///     検索結果は 60 秒キャッシュする（移植元 kIndicatorSearchInterval）。</item>
/// <item>矩形が空、または幅・高さが 5px 未満なら検出不能として扱う（移植元と同じ下限）。</item>
/// <item><c>BitBlt</c> でその矩形のスクリーンピクセルをメモリ DC へコピーし、<c>GetDIBits</c> で
///     32bpp のピクセルデータを取得する。</item>
/// <item>矩形の左右端・下端の各 5px（上端 1 行は除く）を背景としてサンプリングし、輝度の平均を求める。</item>
/// <item>内側領域（上マージン 3px・下マージン 8px・左右マージン 5px を除いた範囲）で、背景輝度との差が
///     30 を超えるピクセルの比率を求め、8.5% を超えれば IME ON と判定する（移植元の実測値ベースの閾値）。</item>
/// </list>
/// </para>
/// <para>
/// <b>呼び出し方式（重要・当初設計から訂正済み）</b>: 移植元 C++ 版の <c>IMEMonitor::checkIMEState</c>
/// を実際に読んだところ、<c>PixelIMEDetector::detectIMEStateTimed</c>
/// （このクラスの <see cref="DetectImeStateTimed"/> に対応）は独立した周期タイマーではなく、
/// <c>IMEMonitor</c> のデバウンス処理から <b>同期的にオンデマンド</b>で呼ばれる（呼び出しのたびに
/// 毎回 UI Automation / BitBlt / GetDIBits を実行し、結果キャッシュは行わない。60 秒の矩形キャッシュのみ
/// 有効）。<c>AppSettings.PixelVerificationIntervalMs</c>（設定ファイルの
/// <c>pixelVerificationIntervalMs</c>、内部コマンド 201 が 0⇔2000 でトグルする値）は、C++ 版で
/// <c>App.cpp</c> → <c>IMEMonitor::setPixelVerificationIntervalMs</c> まで配線されているにもかかわらず、
/// <c>IMEMonitor.cpp</c> 内で一度も参照されていない（<c>pixelVerificationIntervalMs_</c> フィールドへの
/// 代入のみで読み取りが無い）ことをソースの全呼び出し箇所を確認して検証済み。つまり現行 C++ 版では
/// この設定はピクセル検出の実行有無に一切影響しない（内部コマンド 201 の実効果も無い）。
/// FR-001/FR-002（現行版と同じ振る舞い）に従い、この「設定はあるが読み取られない」という実態を
/// そのまま移植する（是正は本フィーチャーの範囲外。internal-command-catalog.md の 41 件の食い違いと
/// 同じ方針）。<c>ImeMonitor</c>（T027）は <c>SetPixelVerificationIntervalMs</c> 相当の setter を
/// 持つが値を保持するだけで、本クラスの動作には反映しない。
/// </para>
/// </remarks>
public sealed partial class PixelImeDetector
{
    private static readonly Lazy<PixelImeDetector> LazyInstance = new(() => new PixelImeDetector());

    /// <summary>プロセス内で共有するシングルトンインスタンス（GDI/UI Automation リソースを保持する）。</summary>
    public static PixelImeDetector Instance => LazyInstance.Value;

    // 移植元 kIndicatorSearchInterval（入力インジケーター矩形のキャッシュ間隔）。
    private static readonly TimeSpan IndicatorSearchInterval = TimeSpan.FromSeconds(60);

    // 移植元 analyzePixelData の定数群（実測値ベース、変更しない）。
    private const int BorderSize = 5;
    private const int TopMargin = 3;
    private const int BottomMargin = 8;
    private const int DiffThreshold = 30;
    private const double TextRatioThreshold = 0.085;

    private readonly object _lock = new();

    // === 入力インジケーター矩形のキャッシュ（_lock 配下でのみ読み書きする） ===
    private RECT _cachedRect;
    private bool _hasCachedRect;
    private TimeSpan _lastIndicatorSearch;

    // === GDI リソース（_lock 配下でのみ読み書きする） ===
    private nint _screenDc;
    private nint _memDc;
    private nint _bitmap;
    private nint _oldBitmap;
    private int _gdiWidth;
    private int _gdiHeight;
    private byte[] _pixelBuffer = [];

    private PixelImeDetector()
    {
    }

    /// <summary>
    /// キャッシュなしの検出。呼び出しのたびに UI Automation（60 秒キャッシュ付き矩形検索を除く）/
    /// BitBlt / GetDIBits を実行する。日本語以外は null を返す。移植元: detectIMEStateTimed。
    /// </summary>
    public bool? DetectImeStateTimed(LanguageType language)
    {
        if (language != LanguageType.Japanese)
        {
            return null;
        }

        lock (_lock)
        {
            RECT rect = FindIndicatorRectLocked();
            if (RectIsEmpty(rect) || rect.Width < 5 || rect.Height < 5)
            {
                return null;
            }

            return AnalyzeIndicatorWithBitBltLocked(rect);
        }
    }

    /// <summary>矩形・結果キャッシュをクリアする（テスト用）。移植元: clearCache。</summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _hasCachedRect = false;
            _cachedRect = default;
            _lastIndicatorSearch = default;
        }
    }

    /// <summary>GDI リソースを解放する。アプリ終了時に呼ぶ（移植元デストラクタ相当）。</summary>
    public void ReleaseResources()
    {
        lock (_lock)
        {
            ReleaseGdiResourcesLocked();
        }
    }

    private static bool RectIsEmpty(RECT rc) => rc.Width <= 0 || rc.Height <= 0;

    // 移植元 findIndicatorRect（60 秒キャッシュ）。_lock 配下から呼ぶこと。
    private RECT FindIndicatorRectLocked()
    {
        TimeSpan now = MonotonicNow();
        if (_hasCachedRect && (now - _lastIndicatorSearch) < IndicatorSearchInterval)
        {
            return _cachedRect;
        }

        RECT foundRect = FindInputIndicatorByUia();
        _lastIndicatorSearch = now;
        _cachedRect = foundRect;
        _hasCachedRect = !RectIsEmpty(foundRect);
        return foundRect;
    }

    // Environment.TickCount64（システム起動からの単調増加ミリ秒）ベースの時刻。
    // RuleRuntimeState.Clock と同じ考え方（NTP 補正・夏時間の影響を受けない）。
    private static TimeSpan MonotonicNow() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// UI Automation で <c>Shell_TrayWnd</c> 配下の「入力インジケーター」/「Input indicator」を検索し、
    /// 見つかれば <c>BoundingRectangle</c> を返す。移植元 findInputIndicatorByUIA（無名名前空間）と同じ。
    /// </summary>
    private static RECT FindInputIndicatorByUia()
    {
        nint tray = NativeMethods.FindWindowW("Shell_TrayWnd", null);
        if (tray == 0)
        {
            return default;
        }

        AutomationElement? trayElement = AutomationElement.FromHandle(tray);
        if (trayElement is null)
        {
            return default;
        }

        // Name == "入力インジケーター" OR "Input indicator"（移植元 makeNameCondition + CreateOrCondition）。
        Condition condition = new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "入力インジケーター"),
            new PropertyCondition(AutomationElement.NameProperty, "Input indicator"));

        AutomationElement? indicator = trayElement.FindFirst(TreeScope.Descendants, condition);
        if (indicator is null)
        {
            return default;
        }

        System.Windows.Rect bounds = indicator.Current.BoundingRectangle;
        if (bounds.IsEmpty)
        {
            return default;
        }

        return new RECT
        {
            Left = (int)Math.Round(bounds.Left),
            Top = (int)Math.Round(bounds.Top),
            Right = (int)Math.Round(bounds.Right),
            Bottom = (int)Math.Round(bounds.Bottom),
        };
    }

    // 移植元 ensureGdiResources。幅・高さが変わらなければ既存のリソースを再利用する。_lock 配下から呼ぶこと。
    private bool EnsureGdiResourcesLocked(int width, int height)
    {
        if (_screenDc != 0 && _memDc != 0 && _bitmap != 0 && _gdiWidth == width && _gdiHeight == height)
        {
            return true;
        }

        ReleaseGdiResourcesLocked();

        _screenDc = NativeMethods.GetDC(0);
        if (_screenDc == 0)
        {
            return false;
        }

        _memDc = NativeMethods.CreateCompatibleDC(_screenDc);
        if (_memDc == 0)
        {
            ReleaseGdiResourcesLocked();
            return false;
        }

        _bitmap = PixelGdiNativeMethods.CreateCompatibleBitmap(_screenDc, width, height);
        if (_bitmap == 0)
        {
            ReleaseGdiResourcesLocked();
            return false;
        }

        _oldBitmap = NativeMethods.SelectObject(_memDc, _bitmap);
        _gdiWidth = width;
        _gdiHeight = height;
        _pixelBuffer = new byte[width * height * 4];
        return true;
    }

    // 移植元 releaseGdiResources。_lock 配下から呼ぶこと。
    private void ReleaseGdiResourcesLocked()
    {
        if (_memDc != 0 && _oldBitmap != 0)
        {
            NativeMethods.SelectObject(_memDc, _oldBitmap);
            _oldBitmap = 0;
        }

        if (_bitmap != 0)
        {
            NativeMethods.DeleteObject(_bitmap);
            _bitmap = 0;
        }

        if (_memDc != 0)
        {
            NativeMethods.DeleteDC(_memDc);
            _memDc = 0;
        }

        if (_screenDc != 0)
        {
            NativeMethods.ReleaseDC(0, _screenDc);
            _screenDc = 0;
        }

        _gdiWidth = 0;
        _gdiHeight = 0;
        _pixelBuffer = [];
    }

    // 移植元 analyzeIndicatorWithBitBlt。BitBlt でスクリーンをキャプチャし、GetDIBits でピクセルを取得する。
    // _lock 配下から呼ぶこと。
    private bool AnalyzeIndicatorWithBitBltLocked(RECT rect)
    {
        int left = rect.Left;
        int top = rect.Top;
        int width = rect.Width;
        int height = rect.Height;

        if (!EnsureGdiResourcesLocked(width, height))
        {
            return false;
        }

        if (!PixelGdiNativeMethods.BitBlt(_memDc, 0, 0, width, height, _screenDc, left, top, PixelGdiNativeMethods.SRCCOPY))
        {
            return false;
        }

        // GetDIBits の前にビットマップを DC から外す（API 要件。移植元と同じ手順）。
        NativeMethods.SelectObject(_memDc, _oldBitmap);

        BITMAPINFOHEADER bmi = default;
        bmi.BiSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.BiWidth = width;
        bmi.BiHeight = height;
        bmi.BiPlanes = 1;
        bmi.BiBitCount = 32;
        bmi.BiCompression = 0; // BI_RGB

        int stride = width * 4;
        int scanLinesCopied = PixelGdiNativeMethods.GetDIBits(
            _screenDc, _bitmap, 0, (uint)height, _pixelBuffer, ref bmi, 0 /* DIB_RGB_COLORS */);

        // ビットマップを DC に戻す（次回の BitBlt に備える。移植元と同じ）。
        _oldBitmap = NativeMethods.SelectObject(_memDc, _bitmap);

        if (scanLinesCopied == 0)
        {
            return false;
        }

        return AnalyzePixelData(_pixelBuffer, width, height, stride);
    }

    /// <summary>
    /// 移植元 analyzePixelData。周囲の背景輝度と内側領域の輝度差からテキスト（「あ」等の字形）の
    /// 有無を判定する。ピクセルは GetDIBits が返す 32bpp BGRX（下位バイトから B, G, R, 未使用の順）。
    /// </summary>
    private static bool AnalyzePixelData(byte[] pixels, int width, int height, int stride)
    {
        long bgBrightnessSum = 0;
        int bgPixelCount = 0;

        // 背景輝度サンプリング（左端・右端・下端の各 5px、上端 1 行は除外。移植元と同じ範囲）。
        for (int y = 1; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isBorder = x < BorderSize || x >= width - BorderSize || y >= height - BorderSize;
                if (!isBorder)
                {
                    continue;
                }

                int offset = (y * stride) + (x * 4);
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];
                bgBrightnessSum += ((r * 299) + (g * 587) + (b * 114)) / 1000;
                bgPixelCount++;
            }
        }

        if (bgPixelCount == 0)
        {
            return false;
        }

        int bgBrightness = (int)(bgBrightnessSum / bgPixelCount);

        int textPixels = 0;
        int innerPixels = 0;
        for (int y = TopMargin; y < height - BottomMargin; y++)
        {
            for (int x = BorderSize; x < width - BorderSize; x++)
            {
                innerPixels++;
                int offset = (y * stride) + (x * 4);
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];
                int brightness = ((r * 299) + (g * 587) + (b * 114)) / 1000;
                if (Math.Abs(brightness - bgBrightness) > DiffThreshold)
                {
                    textPixels++;
                }
            }
        }

        if (innerPixels == 0)
        {
            return false;
        }

        double textRatio = (double)textPixels / innerPixels;
        // 既存実測値: 日本語 A=7.1%, あ=11.3% → 閾値 8.5%（移植元コメントと同じ）。
        return textRatio > TextRatioThreshold;
    }

    // ========================================================================
    // このクラス専用の P/Invoke 宣言（private ネスト型）。BitBlt / CreateCompatibleBitmap /
    // GetDIBits は PixelImeDetector 以外で使わないため、共有の Interop/NativeMethods.Gdi32.cs には
    // 追加せず、このファイル内に閉じ込める。file スコープ型は LibraryImport ソースジェネレーターの
    // 生成物（obj 配下の別ファイル LibraryImports.g.cs）と組み合わせられない（CS0759/CS0234）ため、
    // private ネスト型として実装する（T028 の元実装で実機確認済みの制約）。
    // ========================================================================

    // https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader
    // BITMAPINFO の bmiColors（パレット）は 32bpp/BI_RGB では参照されないため、移植元 C++ 版
    // （bmi.bmiColors に触れず biSize = sizeof(BITMAPINFOHEADER) のまま渡す）と同じくヘッダのみ定義する。
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint BiSize;
        public int BiWidth;
        public int BiHeight;
        public ushort BiPlanes;
        public ushort BiBitCount;
        public uint BiCompression;
        public uint BiSizeImage;
        public int BiXPelsPerMeter;
        public int BiYPelsPerMeter;
        public uint BiClrUsed;
        public uint BiClrImportant;
    }

    private static partial class PixelGdiNativeMethods
    {
        public const uint SRCCOPY = 0x00CC0020;

        // https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-createcompatiblebitmap
        [LibraryImport("gdi32.dll")]
        public static partial nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

        // https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-bitblt
        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);

        // https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-getdibits
        [LibraryImport("gdi32.dll")]
        public static partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);
    }
}
