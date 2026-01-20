using System.Runtime.InteropServices;
using System.Windows.Automation;
using AutomationCondition = System.Windows.Automation.Condition;
using DrawingColor = System.Drawing.Color;

namespace IMEIndicatorClock.Services;

/// <summary>
/// ピクセル判定によるIME ON/OFF状態検出サービス
/// Windows入力インジケーターの描画内容からIME状態を判定
/// </summary>
public partial class PixelIMEDetector : IDisposable
{
    #region Win32 API

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        IntPtr lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        // bmiColors は使用しない（32bppなので）
    }

    private const uint SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    #endregion

    private static PixelIMEDetector? _instance;
    private static readonly object _lock = new();
    private bool _disposed;

#if DEBUG
    /// <summary>
    /// デバッグ用画像保存の有効/無効（UIAutomationTestWindowから制御）
    /// </summary>
    public static bool EnableDebugImageSave { get; set; } = false;
#endif

    // 入力インジケーター位置のキャッシュ
    private System.Windows.Rect _cachedIndicatorRect = System.Windows.Rect.Empty;
    private DateTime _lastIndicatorSearch = DateTime.MinValue;
    private const int IndicatorCacheSeconds = 10; // 10秒間キャッシュ

    // 判定結果のキャッシュ
    private DateTime _lastPixelCheck = DateTime.MinValue;
    private bool? _lastPixelResult = null;
    private const int PixelCheckIntervalMs = 200; // 200ms間隔で判定

    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static PixelIMEDetector Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new PixelIMEDetector();
                }
            }
            return _instance;
        }
    }

    private PixelIMEDetector()
    {
    }

    /// <summary>
    /// ピクセル判定でIME ON/OFF状態を検出
    /// </summary>
    /// <param name="language">言語タイプ</param>
    /// <returns>IME ON状態かどうか（判定不能な場合はnull）</returns>
    public bool? DetectIMEState(LanguageType language)
    {
        // 日本語、韓国語、中国語のみ対応
        if (language != LanguageType.Japanese &&
            language != LanguageType.Korean &&
            language != LanguageType.ChineseSimplified &&
            language != LanguageType.ChineseTraditional)
        {
            return null;
        }

        // キャッシュチェック
        var now = DateTime.Now;
        if (_lastPixelResult.HasValue &&
            (now - _lastPixelCheck).TotalMilliseconds < PixelCheckIntervalMs)
        {
            return _lastPixelResult;
        }

        try
        {
            // 入力インジケーターの位置を取得
            var rect = GetIndicatorRect();
            if (rect.IsEmpty || rect.Width < 5 || rect.Height < 5)
            {
                DbgLog.Log(5, "PixelIME: インジケーター位置取得失敗");
                return null;
            }

            // BitBltでインジケーター領域をキャプチャして分析
            bool isOn = AnalyzeIndicatorWithBitBlt(rect, language);

            _lastPixelResult = isOn;
            _lastPixelCheck = now;

            DbgLog.Log(5, $"PixelIME: {language} -> {(isOn ? "ON" : "OFF")}");
            return isOn;
        }
        catch (Exception ex)
        {
            DbgLog.Log(5, $"PixelIME: 例外 - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// BitBltでキャプチャしてピクセルを分析（GetDIBits使用）
    /// </summary>
    private bool AnalyzeIndicatorWithBitBlt(System.Windows.Rect rect, LanguageType language)
    {
        int left = (int)rect.Left;
        int top = (int)rect.Top;
        int width = (int)rect.Width;
        int height = (int)rect.Height;

        DbgLog.Log(5, $"PixelIME: rect=({left},{top},{width}x{height})");

        IntPtr screenDC = IntPtr.Zero;
        IntPtr memDC = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            // スクリーンDCを取得
            screenDC = GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero)
            {
                DbgLog.Log(5, "PixelIME: スクリーンDC取得失敗");
                return false;
            }

            // メモリDCとビットマップを作成
            memDC = CreateCompatibleDC(screenDC);
            hBitmap = CreateCompatibleBitmap(screenDC, width, height);
            oldBitmap = SelectObject(memDC, hBitmap);

            // BitBltでスクリーンからメモリDCにコピー
            if (!BitBlt(memDC, 0, 0, width, height, screenDC, left, top, SRCCOPY))
            {
                DbgLog.Log(5, "PixelIME: BitBlt失敗");
                return false;
            }

            // GetDIBitsでピクセルデータを直接取得（GDI+を経由しない）
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // 負の値でトップダウン（上から下）
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB
                }
            };

            // ピクセルデータ用バッファ（BGRA形式、4バイト/ピクセル）
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            IntPtr pPixels = Marshal.AllocHGlobal(pixels.Length);
            try
            {
                int result = GetDIBits(memDC, hBitmap, 0, (uint)height, pPixels, ref bmi, DIB_RGB_COLORS);
                if (result == 0)
                {
                    DbgLog.Log(5, "PixelIME: GetDIBits失敗");
                    return false;
                }

                Marshal.Copy(pPixels, pixels, 0, pixels.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(pPixels);
            }

#if DEBUG
            // デバッグ: キャプチャ画像を保存（設定で有効時のみ）
            if (EnableDebugImageSave)
            {
                SaveDebugBitmapFromPixels(pixels, width, height, language);
            }
#endif

            return AnalyzePixelData(pixels, width, height, stride, language);
        }
        finally
        {
            // リソース解放
            if (oldBitmap != IntPtr.Zero && memDC != IntPtr.Zero)
                SelectObject(memDC, oldBitmap);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (memDC != IntPtr.Zero)
                DeleteDC(memDC);
            if (screenDC != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    /// <summary>
    /// ピクセルデータを分析してIME状態を判定（GetDIBits結果を直接使用）
    /// </summary>
    private bool AnalyzePixelData(byte[] pixels, int width, int height, int stride, LanguageType language)
    {
        DbgLog.Log(5, $"PixelIME: size={width}x{height}, stride={stride}");

        // 全体をサンプリング（マージン最小）
        int marginX = 1;
        int marginY = 1;

        int sampleLeft = marginX;
        int sampleTop = marginY;
        int sampleWidth = width - marginX * 2;
        int sampleHeight = height - marginY * 2;

        if (sampleWidth < 3 || sampleHeight < 3)
        {
            DbgLog.Log(5, "PixelIME: サンプル領域が小さすぎる");
            return false;
        }

        // 全ピクセルスキャンで暗いピクセル（文字）を探す
        int totalPixels = 0;
        int darkPixels = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = y * stride + x * 4;
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];
                totalPixels++;
                // 暗いピクセル（RGB各成分が200未満）をカウント
                if (r < 200 || g < 200 || b < 200)
                {
                    darkPixels++;
                }
            }
        }

        double darkRatio = (double)darkPixels / totalPixels;
        DbgLog.Log(5, $"PixelIME: darkPixels={darkPixels}/{totalPixels} ({darkRatio:P1})");

        // 暗いピクセルの割合で判定
        // 実測値:
        //   日本語: あ=9.0%, A=6.5% → darkRatio > 7.5% で ON
        //   韓国語: 가=5.3%, A=6.5% → darkRatio < 6% で ON
        //   中国語: 中=5.3%, 英=7.5% → darkRatio < 6% で ON
        bool result = language switch
        {
            LanguageType.Japanese => darkRatio > 0.075,   // 7.5%超でON（あ=9%, A=6.5%）
            LanguageType.Korean => darkRatio < 0.06,      // 6%未満でON
            LanguageType.ChineseSimplified => darkRatio < 0.06,  // 6%未満でON
            LanguageType.ChineseTraditional => darkRatio < 0.06, // 6%未満でON
            _ => false
        };

        DbgLog.Log(5, $"PixelIME: {language} darkRatio={darkRatio:P1} -> {(result ? "ON" : "OFF")}");
        return result;
    }

    /// <summary>
    /// 入力インジケーターの位置を取得（キャッシュ付き）
    /// </summary>
    private System.Windows.Rect GetIndicatorRect()
    {
        var now = DateTime.Now;

        // キャッシュが有効ならそれを返す
        if (!_cachedIndicatorRect.IsEmpty &&
            (now - _lastIndicatorSearch).TotalSeconds < IndicatorCacheSeconds)
        {
            return _cachedIndicatorRect;
        }

        // UI Automationで検索
        var indicator = FindWindowsInputIndicator();
        if (indicator == null)
        {
            _cachedIndicatorRect = System.Windows.Rect.Empty;
            _lastIndicatorSearch = now;
            return System.Windows.Rect.Empty;
        }

        try
        {
            _cachedIndicatorRect = indicator.Current.BoundingRectangle;
            _lastIndicatorSearch = now;
            DbgLog.Log(6, $"PixelIME: インジケーター位置更新 {_cachedIndicatorRect}");
            return _cachedIndicatorRect;
        }
        catch
        {
            _cachedIndicatorRect = System.Windows.Rect.Empty;
            _lastIndicatorSearch = now;
            return System.Windows.Rect.Empty;
        }
    }

    /// <summary>
    /// Windowsシステムの入力インジケーターを探す（FindAll + フィルタ方式で高速化）
    /// </summary>
    private static AutomationElement? FindWindowsInputIndicator()
    {
        try
        {
            var rootElement = AutomationElement.RootElement;

            // FindAll で全子要素を一括取得（FindFirst より速い）
            var rootChildren = rootElement.FindAll(TreeScope.Children, AutomationCondition.TrueCondition);

            // メモリ上でフィルタリング
            foreach (AutomationElement child in rootChildren)
            {
                try
                {
                    var className = child.Current.ClassName ?? "";

                    if (className == "Shell_TrayWnd")
                    {
                        // 子孫から入力関連の要素を探す
                        var descendants = child.FindAll(TreeScope.Descendants, AutomationCondition.TrueCondition);

                        foreach (AutomationElement desc in descendants)
                        {
                            try
                            {
                                var name = desc.Current.Name ?? "";

                                // 「トレイ入力インジケーター」または「Input indicator」を含む要素
                                if (name.Contains("入力インジケーター") || name.Contains("Input indicator"))
                                {
                                    return desc;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            DbgLog.Log(5, $"PixelIME: FindIndicator例外 - {ex.Message}");
        }

        return null;
    }

#if DEBUG
    // デバッグ用: 最後に保存した時刻（連続保存を防止）
    private DateTime _lastDebugSave = DateTime.MinValue;

    /// <summary>
    /// デバッグ用: GetDIBitsで取得したピクセルデータから画像を保存
    /// </summary>
    private void SaveDebugBitmapFromPixels(byte[] pixels, int width, int height, LanguageType language)
    {
        try
        {
            // 5秒に1回のみ保存（ディスク負荷軽減）
            var now = DateTime.Now;
            if ((now - _lastDebugSave).TotalSeconds < 5) return;
            _lastDebugSave = now;

            var saveDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                "debug");

            if (!System.IO.Directory.Exists(saveDir))
                System.IO.Directory.CreateDirectory(saveDir);

            var timestamp = now.ToString("HHmmss");
            var filePath = System.IO.Path.Combine(saveDir, $"dib_{language}_{timestamp}.png");

            // byte配列からBitmapを作成
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rect = new System.Drawing.Rectangle(0, 0, width, height);
            var bmpData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            // ピクセルデータをコピー
            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            bitmap.UnlockBits(bmpData);

            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            DbgLog.Log(5, $"PixelIME: DIB画像保存: {filePath}");
        }
        catch (Exception ex)
        {
            DbgLog.Log(5, $"PixelIME: デバッグ画像保存エラー: {ex.Message}");
        }
    }
#endif

    /// <summary>
    /// キャッシュをクリア（テスト用）
    /// </summary>
    public void ClearCache()
    {
        _cachedIndicatorRect = System.Windows.Rect.Empty;
        _lastIndicatorSearch = DateTime.MinValue;
        _lastPixelResult = null;
        _lastPixelCheck = DateTime.MinValue;
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースを解放（保護されたメソッド）
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // マネージドリソースのクリーンアップ
            ClearCache();
        }

        // 静的インスタンスをクリア
        lock (_lock)
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        _disposed = true;
        DbgLog.Log(5, "PixelIMEDetector: Disposed");
    }

    /// <summary>
    /// ファイナライザ
    /// </summary>
    ~PixelIMEDetector()
    {
        Dispose(false);
    }

    /// <summary>
    /// シングルトンインスタンスを明示的に破棄
    /// </summary>
    public static void DisposeInstance()
    {
        lock (_lock)
        {
            if (_instance != null)
            {
                _instance.Dispose();
                _instance = null;
            }
        }
    }
}
