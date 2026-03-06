using System.Runtime.InteropServices;
using System.Windows.Automation;
using AutomationCondition = System.Windows.Automation.Condition;

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

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

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
    private static readonly System.Threading.Lock _lock = new();
    private bool _disposed;

#if DEBUG
    /// <summary>
    /// デバッグ用画像保存の有効/無効（UIAutomationTestWindowから制御）
    /// </summary>
    public static bool EnableDebugImageSave { get; set; } = false;
#endif

    // 入力インジケーターのキャッシュ
    private System.Windows.Rect _cachedIndicatorRect = System.Windows.Rect.Empty;
    private DateTime _lastIndicatorSearch = DateTime.MinValue;
    private const int IndicatorSearchIntervalMs = 10000; // 10秒間隔で再検索

    // 判定結果のキャッシュ
    private DateTime _lastPixelCheck = DateTime.MinValue;
    private bool? _lastPixelResult = null;
    private const int PixelCheckIntervalMs = 200; // 200ms間隔で判定

    // GDIリソースのキャッシュ（SafeHandleで安全に管理）
    private SafeDCHandle? _cachedScreenDC;
    private SafeMemDCHandle? _cachedMemDC;
    private SafeGdiObjectHandle? _cachedBitmap;
    private IntPtr _cachedOldBitmap = IntPtr.Zero;
    private int _cachedWidth;
    private int _cachedHeight;

    // ピクセルバッファの事前確保（ArrayPool/AllocHGlobalの毎回確保を排除）
    private byte[]? _pixelBuffer;
    private IntPtr _pinnedPixelBuffer = IntPtr.Zero;

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
        // 日本語のみ対応
        if (language != LanguageType.Japanese)
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
                return null;
            }

            // BitBltでインジケーター領域をキャプチャして分析
            bool isOn = AnalyzeIndicatorWithBitBlt(rect, language);

            _lastPixelResult = isOn;
            _lastPixelCheck = now;
            return isOn;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PixelIMEDetector] DetectIMEState failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// DetectIMEState2の計測結果
    /// </summary>
    public record DetectIMEState2Result(
        bool? IsOn,
        double GetRectTimeMs,
        double AnalyzeTimeMs,
        double TotalTimeMs
    );

    /// <summary>
    /// ピクセル判定でIME ON/OFF状態を検出（キャッシュなし版・時間計測付き）
    /// </summary>
    /// <param name="language">言語タイプ</param>
    /// <returns>IME状態と各処理の計測時間</returns>
    public DetectIMEState2Result DetectIMEState2(LanguageType language)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        double getRectTime = 0;
        double analyzeTime = 0;

        // 日本語のみ対応
        if (language != LanguageType.Japanese)
        {
            swTotal.Stop();
            return new DetectIMEState2Result(null, 0, 0, swTotal.Elapsed.TotalMilliseconds);
        }

        try
        {
            // 入力インジケーターの位置を取得（時間計測）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rect = GetIndicatorRect();
            sw.Stop();
            getRectTime = sw.Elapsed.TotalMilliseconds;

            if (rect.IsEmpty || rect.Width < 5 || rect.Height < 5)
            {
                swTotal.Stop();
                return new DetectIMEState2Result(null, getRectTime, 0, swTotal.Elapsed.TotalMilliseconds);
            }

            // BitBltでインジケーター領域をキャプチャして分析（時間計測）
            sw.Restart();
            bool isOn = AnalyzeIndicatorWithBitBlt(rect, language);
            sw.Stop();
            analyzeTime = sw.Elapsed.TotalMilliseconds;

            swTotal.Stop();
            return new DetectIMEState2Result(isOn, getRectTime, analyzeTime, swTotal.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PixelIMEDetector] DetectIMEState2 failed: {ex.Message}");
            swTotal.Stop();
            return new DetectIMEState2Result(null, getRectTime, analyzeTime, swTotal.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// GDIリソースを確保（初回またはサイズ変更時のみ）
    /// </summary>
    private bool EnsureGdiResources(int width, int height)
    {
        if (_cachedScreenDC is { IsInvalid: false } && _cachedWidth == width && _cachedHeight == height)
            return true;

        // 既存リソースを解放
        ReleaseGdiResources();

        var screenDC = GetDC(IntPtr.Zero);
        if (screenDC == IntPtr.Zero) return false;
        _cachedScreenDC = new SafeDCHandle(screenDC);

        var memDC = CreateCompatibleDC(screenDC);
        if (memDC == IntPtr.Zero) return false;
        _cachedMemDC = new SafeMemDCHandle(memDC);

        var bitmap = CreateCompatibleBitmap(screenDC, width, height);
        if (bitmap == IntPtr.Zero) return false;
        _cachedBitmap = new SafeGdiObjectHandle(bitmap);

        _cachedOldBitmap = SelectObject(memDC, bitmap);
        _cachedWidth = width;
        _cachedHeight = height;

        // ピクセルバッファも事前確保
        int bufferSize = width * height * 4;
        _pixelBuffer = new byte[bufferSize];
        _pinnedPixelBuffer = Marshal.AllocHGlobal(bufferSize);

        return true;
    }

    /// <summary>
    /// キャッシュ済みGDIリソースを解放
    /// </summary>
    private void ReleaseGdiResources()
    {
        if (_pinnedPixelBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_pinnedPixelBuffer);
            _pinnedPixelBuffer = IntPtr.Zero;
        }
        _pixelBuffer = null;

        // OldBitmapを復元してからSafeHandleをDispose
        if (_cachedOldBitmap != IntPtr.Zero && _cachedMemDC is { IsInvalid: false })
        {
            SelectObject(_cachedMemDC.DangerousGetHandle(), _cachedOldBitmap);
            _cachedOldBitmap = IntPtr.Zero;
        }

        _cachedBitmap?.Dispose();
        _cachedBitmap = null;

        _cachedMemDC?.Dispose();
        _cachedMemDC = null;

        _cachedScreenDC?.Dispose();
        _cachedScreenDC = null;

        _cachedWidth = 0;
        _cachedHeight = 0;
    }

    /// <summary>
    /// BitBltでキャプチャしてピクセルを分析（キャッシュ済みGDIリソース使用）
    /// </summary>
    private bool AnalyzeIndicatorWithBitBlt(System.Windows.Rect rect, LanguageType language)
    {
        int left = (int)rect.Left;
        int top = (int)rect.Top;
        int width = (int)rect.Width;
        int height = (int)rect.Height;

        if (!EnsureGdiResources(width, height))
            return false;

        var memDCHandle = _cachedMemDC!.DangerousGetHandle();
        var screenDCHandle = _cachedScreenDC!.DangerousGetHandle();
        var bitmapHandle = _cachedBitmap!.DangerousGetHandle();

        // BitBltでスクリーンからメモリDCにコピー
        if (!BitBlt(memDCHandle, 0, 0, width, height, screenDCHandle, left, top, SRCCOPY))
            return false;

        // GetDIBitsの前にビットマップをDCから解除（API要件）
        SelectObject(memDCHandle, _cachedOldBitmap);

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB
            }
        };

        int stride = width * 4;
        int length = stride * height;

        // 事前確保済みバッファでGetDIBits実行（アロケーションゼロ）
        int result = GetDIBits(screenDCHandle, bitmapHandle, 0, (uint)height, _pinnedPixelBuffer, ref bmi, DIB_RGB_COLORS);

        // ビットマップをDCに再選択
        _cachedOldBitmap = SelectObject(memDCHandle, bitmapHandle);

        if (result == 0) return false;

        Marshal.Copy(_pinnedPixelBuffer, _pixelBuffer!, 0, length);

#if DEBUG
        if (EnableDebugImageSave)
        {
            SaveDebugBitmapFromPixels(_pixelBuffer!, width, height, language);
        }
#endif

        return AnalyzePixelData(_pixelBuffer!, width, height, stride, language);
    }

    /// <summary>
    /// ピクセルデータを分析してIME状態を判定（GetDIBits結果を直接使用）
    /// </summary>
    private bool AnalyzePixelData(byte[] pixels, int width, int height, int stride, LanguageType language)
    {

        // 全体をサンプリング（マージン最小）
        // 周囲ピクセルから背景色の輝度を取得（上端1行を除く）
        // 左端・右端・下端の各5ドットをサンプリング
        const int borderSize = 5;
        long bgBrightnessSum = 0;
        int bgPixelCount = 0;

        for (int y = 1; y < height; y++) // 上端1行をスキップ
        {
            for (int x = 0; x < width; x++)
            {
                // 左端・右端・下端のborderSizeドット以内のみ
                bool isBorder = x < borderSize || x >= width - borderSize
                    || y >= height - borderSize;
                if (!isBorder) continue;

                int offset = y * stride + x * 4;
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];
                bgBrightnessSum += (r * 299 + g * 587 + b * 114) / 1000;
                bgPixelCount++;
            }
        }

        if (bgPixelCount == 0) return false;
        int bgBrightness = (int)(bgBrightnessSum / bgPixelCount);

        // 文字領域（内側のみ）でテキストピクセルをカウント
        // 上下マージンを広めに取り、文字が集中する中央部に絞って比率差を拡大
        const int topMargin = 3;
        const int bottomMargin = 8;
        const int diffThreshold = 30; // 背景色との輝度差がこれ以上ならテキスト
        int textPixels = 0;
        int innerPixels = 0;

        for (int y = topMargin; y < height - bottomMargin; y++)
        {
            for (int x = borderSize; x < width - borderSize; x++)
            {
                innerPixels++;
                int offset = y * stride + x * 4;
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];
                int brightness = (r * 299 + g * 587 + b * 114) / 1000;
                if (Math.Abs(brightness - bgBrightness) > diffThreshold)
                {
                    textPixels++;
                }
            }
        }

        if (innerPixels == 0) return false;
        double textRatio = (double)textPixels / innerPixels;

        // 実測値（内側領域 topMargin=3, bottomMargin=8）:
        //   日本語: A=7.1%, あ=11.3% → 閾値 8.5%
        bool result = textRatio > 0.085;   // 8.5%超でON（日本語専用）
        return result;
    }

    /// <summary>
    /// 入力インジケーターの位置を取得（時間ベースキャッシュ）
    /// </summary>
    private System.Windows.Rect GetIndicatorRect()
    {
        // キャッシュが有効期間内ならそのまま返す
        var now = DateTime.Now;
        if (!_cachedIndicatorRect.IsEmpty &&
            (now - _lastIndicatorSearch).TotalMilliseconds < IndicatorSearchIntervalMs)
        {
            return _cachedIndicatorRect;
        }

        // 再検索
        return SearchAndCacheIndicator();
    }

    /// <summary>
    /// UI Automationで検索してキャッシュに保存
    /// </summary>
    private System.Windows.Rect SearchAndCacheIndicator()
    {
        _lastIndicatorSearch = DateTime.Now;
        var result = FindWindowsInputIndicator();
        if (result == null)
        {
            _cachedIndicatorRect = System.Windows.Rect.Empty;
            return System.Windows.Rect.Empty;
        }

        var (name, rect) = result.Value;
        _cachedIndicatorRect = rect;
        return rect;
    }

    /// <summary>
    /// Windowsシステムの入力インジケーターを探す（FindWindow + FromHandle方式で高速化）
    /// </summary>
    /// <returns>name, rect のタプル。見つからない場合はnull</returns>
    private static (string name, System.Windows.Rect rect)? FindWindowsInputIndicator()
    {
        try
        {
            // FindWindowでShell_TrayWndを直接取得（UI Automationより速い）
            var trayHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (trayHwnd == IntPtr.Zero)
            {
                return null;
            }

            // hWndからAutomationElementを取得
            var trayElement = AutomationElement.FromHandle(trayHwnd);
            if (trayElement == null)
            {
                return null;
            }

            // UI Automationで直接条件指定して検索（高速化）
            var conditionJa = new PropertyCondition(AutomationElement.NameProperty, "入力インジケーター");
            var conditionEn = new PropertyCondition(AutomationElement.NameProperty, "Input indicator");
            var orCondition = new OrCondition(conditionJa, conditionEn);

            var indicator = trayElement.FindFirst(TreeScope.Descendants, orCondition);
            if (indicator != null)
            {
                var rect = indicator.Current.BoundingRectangle;
                return (indicator.Current.Name ?? "", rect);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PixelIMEDetector] FindWindowsInputIndicator failed: {ex.Message}");
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PixelIMEDetector] SaveDebugBitmap failed: {ex.Message}");
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

        // GDIリソースの解放（アンマネージドリソース）
        ReleaseGdiResources();

        // 静的インスタンスをクリア
        lock (_lock)
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        _disposed = true;
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
