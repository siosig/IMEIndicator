using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Automation;
using IMEIndicatorClock.Services;
using AutomationCondition = System.Windows.Automation.Condition;

namespace IMEIndicatorClock.Views;

/// <summary>
/// UI Automation を使用してシステムトレイの IME Indicator を探索するテストウィンドウ
/// </summary>
public partial class UIAutomationTestWindow : Window
{
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetWindowTextInternal(IntPtr hWnd, char* lpString, int nMaxCount);

    private static unsafe int GetWindowText(IntPtr hWnd, Span<char> buffer)
    {
        fixed (char* ptr = buffer)
        {
            return GetWindowTextInternal(hWnd, ptr, buffer.Length);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    // スクリーンキャプチャ用API
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    private const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private DispatcherTimer? _autoRefreshTimer;
    private readonly List<string> _newElementsThisSession = new();
    private int _newCountThisSearch = 0;

    // ハンドル監視用
    private DispatcherTimer? _handleMonitorTimer;
    private bool _isMonitoring;
    private IntPtr _lastActiveWindowHandle;

    public UIAutomationTestWindow()
    {
        InitializeComponent();
        LoadRecords();
        UpdateActiveWindowInfo();
    }

    private void LoadRecords()
    {
        var records = UIADiscoveryService.Instance.GetAllElements();
        GridRecords.ItemsSource = records;
        TxtRecordCount.Text = $"記録済み: {records.Count} 件";
    }

    private static string GetActiveWindowTitle()
    {
        var hwnd = GetForegroundWindow();
        Span<char> buffer = stackalloc char[256];
        int len = GetWindowText(hwnd, buffer);
        return len > 0 ? new string(buffer[..len]) : "";
    }

    private void UpdateActiveWindowInfo()
    {
        var hwnd = GetForegroundWindow();
        Span<char> buffer = stackalloc char[256];
        int len = GetWindowText(hwnd, buffer);
        var title = len > 0 ? new string(buffer[..len]) : "";
        _ = GetWindowThreadProcessId(hwnd, out var processId);

        string processName = "";
        try
        {
            var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch { }

        TxtActiveWindow.Text = $"HWND: 0x{hwnd:X}  PID: {processId}  Process: {processName}\nTitle: {title}";
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchIMEIndicator(false);
    }

    private async void BtnSearchIMEMark_Click(object sender, RoutedEventArgs e)
    {
        await SearchAllButtonsAsync();
    }

    /// <summary>
    /// ボタン情報を格納するクラス
    /// </summary>
    private class ButtonInfo
    {
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";
        public int ControlTypeId { get; set; } = 0xC350; // Button
        public string ControlTypeName { get; set; } = "ControlType.Button";
        public string LocalizedControlType { get; set; } = "ボタン";
        public string RectStr { get; set; } = "N/A";
        public string ParentName { get; set; } = "";
        public string ParentType { get; set; } = "";
        public bool IsIMERelated { get; set; }
        public int Index { get; set; }
    }

    /// <summary>
    /// テキスト検索結果用の要素情報クラス
    /// </summary>
    private class ElementInfo
    {
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string ControlTypeName { get; set; } = "";
        public string LocalizedControlType { get; set; } = "";
        public string ParentName { get; set; } = "";
        public string RectStr { get; set; } = "N/A";
    }

    /// <summary>
    /// デスクトップ全体からButtonControlTypeを探索（非同期版）
    /// 全ての処理を別スレッドで実行し、例外はすべて内部で処理
    /// </summary>
    private async Task SearchAllButtonsAsync()
    {
        BtnSearchIMEMark.IsEnabled = false;
        var activeWindowTitle = GetActiveWindowTitle();

        try
        {
            TxtStatus.Text = "探索中...";
            _newCountThisSearch = 0;
            TreeResult.Items.Clear();
            ListIMEMarks.Items.Clear();
            TxtLog.Clear();
            UpdateActiveWindowInfo();

            Log("Button全検索開始");

            // ログファイルパスを準備
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IMEIndicatorW");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, $"button_search_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            // 進捗報告用
            var progress = new Progress<string>(msg =>
            {
                TxtStatus.Text = msg;
                Log(msg);
            });

            // 全処理を別スレッドで実行
            var result = await Task.Run(() => SearchButtonsWorker(logPath, activeWindowTitle, progress));

            // 結果を表示
            Log($"検索完了: {result.buttonInfoList.Count}個, IME関連: {result.imeCount}個");

            var sb = new StringBuilder();
            sb.AppendLine($"=== ButtonControlType 全検索結果: {result.buttonInfoList.Count} 個 ===\n");

            foreach (var info in result.buttonInfoList)
            {
                // UIスレッドでRecordElementDataを呼ぶ
                UIADiscoveryService.Instance.RecordElementData(
                    info.Name, info.ClassName, info.ControlTypeId, info.ControlTypeName,
                    info.LocalizedControlType, info.RectStr, info.ParentName, info.ParentType, activeWindowTitle);

                if (info.IsIMERelated)
                {
                    var displayText = $"[Button] Name=\"{info.Name}\" (len={info.Name.Length})\n  Rect: {info.RectStr}\n  Parent: {info.ParentName}";
                    ListIMEMarks.Items.Add(displayText);
                    sb.AppendLine(displayText);
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"\n=== 合計: 全Button {result.buttonInfoList.Count}個中、IME関連 {result.imeCount}個 ===");
            sb.AppendLine($"全結果は {logPath} に保存");

            TxtOutput.Text = sb.ToString();
            TxtStatus.Text = $"完了 - IME関連 {result.imeCount}個 / 全{result.buttonInfoList.Count}個 ({DateTime.Now:HH:mm:ss})";

            TabIMEMark.IsSelected = true;
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}");
            TxtStatus.Text = $"エラー: {ex.Message}";
        }
        finally
        {
            BtnSearchIMEMark.IsEnabled = true;
        }
    }

    /// <summary>
    /// 別スレッドで実行されるワーカーメソッド
    /// UI Automationの全操作をここで行い、例外はすべて内部で処理
    /// </summary>
    private (List<ButtonInfo> buttonInfoList, int imeCount) SearchButtonsWorker(
        string logPath, string activeWindowTitle, IProgress<string> progress)
    {
        var buttonInfoList = new List<ButtonInfo>();
        var processedRuntimeIds = new HashSet<string>(); // 重複防止用
        int imeCount = 0;

        try
        {
            using var writer = new StreamWriter(logPath, false, System.Text.Encoding.UTF8);
            writer.WriteLine($"=== ButtonControlType 検索結果 ===");
            writer.WriteLine($"検索日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"アクティブウィンドウ: {activeWindowTitle}");
            writer.WriteLine();
            writer.Flush();

            var rootElement = AutomationElement.RootElement;
            var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            var allButtons = new List<AutomationElement>();

            // ========== Phase 1: システムトレイ関連ウィンドウを探索 ==========
            progress.Report("Phase 1: システムトレイ関連を探索中...");
            writer.WriteLine("=== Phase 1: システムトレイ関連ウィンドウ ===");
            writer.Flush();

            try
            {
                var rootChildren = rootElement.FindAll(TreeScope.Children, AutomationCondition.TrueCondition);
                writer.WriteLine($"デスクトップ直下: {rootChildren.Count} 要素");
                writer.Flush();

                foreach (AutomationElement child in rootChildren)
                {
                    try
                    {
                        string childName = "";
                        string childClass = "";
                        try { childName = child.Current.Name ?? ""; } catch { }
                        try { childClass = child.Current.ClassName ?? ""; } catch { }

                        // システムトレイ/IME関連の要素かチェック
                        bool isRelevant = childName.Contains("IME") || childName.Contains("Indicator") ||
                                          childName.Contains("インジケーター") || childName.Contains("入力") ||
                                          childName.Contains("トレイ") ||
                                          childClass.Contains("Shell") || childClass.Contains("Tray") ||
                                          childClass.Contains("NotifyIcon") || childClass.Contains("Taskbar") ||
                                          childClass.Contains("InputIndicator");

                        if (isRelevant)
                        {
                            writer.WriteLine($"  探索対象: \"{childName}\" [{childClass}]");
                            writer.Flush();

                            try
                            {
                                var buttons = child.FindAll(TreeScope.Descendants, buttonCondition);
                                writer.WriteLine($"    -> Button {buttons.Count}個発見");
                                writer.Flush();

                                foreach (AutomationElement btn in buttons)
                                {
                                    allButtons.Add(btn);
                                }
                            }
                            catch (Exception ex)
                            {
                                writer.WriteLine($"    -> FindAllエラー: {ex.Message}");
                                writer.Flush();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Phase 1 エラー: {ex.Message}");
                writer.Flush();
            }

            writer.WriteLine($"Phase 1 完了: {allButtons.Count}個");
            writer.WriteLine();
            writer.Flush();

            // ========== Phase 2: デスクトップ全体検索 ==========
            progress.Report("Phase 2: デスクトップ全体を検索中...");
            writer.WriteLine("=== Phase 2: デスクトップ全体検索 ===");
            writer.Flush();

            try
            {
                var buttons = rootElement.FindAll(TreeScope.Descendants, buttonCondition);
                writer.WriteLine($"デスクトップ全体: Button {buttons.Count}個発見");
                writer.Flush();

                foreach (AutomationElement btn in buttons)
                {
                    allButtons.Add(btn);
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Phase 2 エラー: {ex.Message}");
                writer.Flush();
            }

            writer.WriteLine($"Phase 2 完了: 合計 {allButtons.Count}個（重複含む）");
            writer.WriteLine();
            writer.Flush();

            progress.Report($"Button {allButtons.Count}個を処理中...");

            int idx = 0;
            int totalButtons = allButtons.Count;
            int duplicateCount = 0;

            foreach (AutomationElement btn in allButtons)
            {
                try
                {
                    if (idx % 20 == 0)
                    {
                        progress.Report($"処理中... {idx}/{totalButtons}");
                    }

                    // 重複チェック用のキー生成（RuntimeId または Name+Rect）
                    string duplicateKey = "";
                    try
                    {
                        var runtimeId = btn.GetRuntimeId();
                        if (runtimeId != null && runtimeId.Length > 0)
                        {
                            duplicateKey = string.Join("-", runtimeId);
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(duplicateKey))
                    {
                        // RuntimeIdが取れない場合はName+Rectで代用
                        try
                        {
                            var name = btn.Current.Name ?? "";
                            var rect = btn.Current.BoundingRectangle;
                            duplicateKey = $"{name}|{rect.Left:F0},{rect.Top:F0}";
                        }
                        catch { duplicateKey = $"unknown_{idx}"; }
                    }

                    if (processedRuntimeIds.Contains(duplicateKey))
                    {
                        duplicateCount++;
                        idx++;
                        continue; // 重複スキップ
                    }
                    processedRuntimeIds.Add(duplicateKey);

                    var info = new ButtonInfo { Index = idx };

                    // Name取得
                    try { info.Name = btn.Current.Name ?? ""; }
                    catch { info.Name = ""; }

                    // ClassName取得
                    try { info.ClassName = btn.Current.ClassName ?? ""; }
                    catch { info.ClassName = ""; }

                    // LocalizedControlType取得
                    try { info.LocalizedControlType = btn.Current.LocalizedControlType ?? "ボタン"; }
                    catch { info.LocalizedControlType = "ボタン"; }

                    // Rect取得
                    try
                    {
                        var rect = btn.Current.BoundingRectangle;
                        info.RectStr = rect.IsEmpty ? "N/A" : $"l:{rect.Left:F0} t:{rect.Top:F0} r:{rect.Right:F0} b:{rect.Bottom:F0}";
                    }
                    catch { info.RectStr = "N/A"; }

                    // Parent取得
                    try
                    {
                        var parent = TreeWalker.RawViewWalker.GetParent(btn);
                        if (parent != null)
                        {
                            try { info.ParentName = parent.Current.Name ?? ""; } catch { }
                            try { info.ParentType = parent.Current.ControlType?.ProgrammaticName ?? ""; } catch { }
                        }
                    }
                    catch { }

                    // IME関連チェック
                    info.IsIMERelated = info.Name.Contains("IME") || info.Name.Contains("インジケーター") ||
                                        info.Name.Contains("入力") || info.Name.Contains("トレイ") ||
                                        info.Name.Contains("Indicator") || info.Name.Contains("言語") ||
                                        info.ParentName.Contains("IME") || info.ParentName.Contains("Indicator");

                    if (info.IsIMERelated) imeCount++;

                    // ログ出力
                    writer.WriteLine($"[{idx + 1}] Name=\"{info.Name}\"");
                    writer.WriteLine($"  ClassName: {info.ClassName}");
                    writer.WriteLine($"  Rect: {info.RectStr}");
                    writer.WriteLine($"  Parent: {info.ParentName}");
                    if (info.IsIMERelated) writer.WriteLine("  *** IME関連 ***");
                    writer.WriteLine();
                    writer.Flush();

                    // バックグラウンドスレッドではRecordElementを呼ばない
                    // 結果処理時にUIスレッドで呼ぶ

                    buttonInfoList.Add(info);
                }
                catch
                {
                    // 要素全体の処理で例外が発生した場合
                    writer.WriteLine($"[{idx + 1}] 要素取得エラー");
                    writer.Flush();
                }

                idx++;
            }

            writer.WriteLine($"=== 処理完了: ユニーク{buttonInfoList.Count}個 (重複{duplicateCount}個スキップ), IME関連: {imeCount}個 ===");
            writer.Flush();
        }
        catch (Exception ex)
        {
            // ワーカー全体の例外
            try
            {
                using var writer = new StreamWriter(logPath, true, System.Text.Encoding.UTF8);
                writer.WriteLine($"致命的エラー: {ex.Message}");
                writer.Flush();
            }
            catch { }
        }

        return (buttonInfoList, imeCount);
    }

    private async void BtnSearchText_Click(object sender, RoutedEventArgs e)
    {
        var searchText = TxtSearchText.Text.Trim();
        if (string.IsNullOrEmpty(searchText))
        {
            MessageBox.Show("検索テキストを入力してください（「*」で全要素出力）", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await SearchTextAsync(searchText);
    }

    /// <summary>
    /// BMP取得ボタンクリック - 3秒後にWindows入力インジケーターの領域をキャプチャ
    /// </summary>
    private async void BtnCaptureBmp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnCaptureBmp.IsEnabled = false;

            // 3秒カウントダウン（IME状態を設定する時間を与える）
            for (int i = 3; i > 0; i--)
            {
                TxtStatus.Text = $"キャプチャまで {i} 秒...";
                Log($"キャプチャまで {i} 秒...");
                await Task.Delay(1000);
            }

            Log("BMP取得開始...");

            // Windowsシステムの入力インジケーターを探す
            var result = FindWindowsInputIndicator();

            if (result == null)
            {
                Log("Windows入力インジケーターが見つかりません");
                MessageBox.Show("Windows入力インジケーターが見つかりません", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (_, _, rect) = result.Value;

            // キーボードレイアウトから言語を判別（UI Automationではテキスト取得不可のため）
            var language = DetectLanguageFromKeyboardLayout();
            Log($"検出言語: {language}");

            // BoundingRectangleから位置を取得
            if (rect.IsEmpty)
            {
                Log("入力インジケーターの位置を取得できません");
                MessageBox.Show("入力インジケーターの位置を取得できません", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 実際のサイズを使用（DPI等で変動するため固定値は使わない）
            int captureWidth = (int)rect.Width;
            int captureHeight = (int)rect.Height;
            Log($"キャプチャ領域: left={rect.Left:F0}, top={rect.Top:F0}, width={captureWidth}, height={captureHeight}");

            CaptureFromScreen((int)rect.Left, (int)rect.Top, captureWidth, captureHeight, language);
        }
        catch (Exception ex)
        {
            Log($"BMP取得エラー: {ex.Message}");
            MessageBox.Show($"BMP取得エラー:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnCaptureBmp.IsEnabled = true;
            TxtStatus.Text = "";
        }
    }

    /// <summary>
    /// Windowsシステムの入力インジケーターを探す（FindAll + フィルタ方式で高速化）
    /// </summary>
    /// <returns>hWnd, name, rect のタプル。見つからない場合はnull</returns>
    private (IntPtr hWnd, string name, System.Windows.Rect rect)? FindWindowsInputIndicator()
    {
        var rootElement = AutomationElement.RootElement;

        Log("デスクトップ直下を一括取得...");

        // FindAll で全子要素を一括取得（FindFirst より速い）
        var rootChildren = rootElement.FindAll(TreeScope.Children, AutomationCondition.TrueCondition);
        Log($"  直下要素: {rootChildren.Count}個");

        // メモリ上でフィルタリング
        foreach (AutomationElement child in rootChildren)
        {
            try
            {
                var className = child.Current.ClassName ?? "";

                if (className == "Shell_TrayWnd")
                {
                    Log("  Shell_TrayWnd 発見");

                    // 子孫から入力関連の要素を探す
                    var descendants = child.FindAll(TreeScope.Descendants, AutomationCondition.TrueCondition);
                    Log($"  子孫要素: {descendants.Count}個");

                    foreach (AutomationElement desc in descendants)
                    {
                        try
                        {
                            var name = desc.Current.Name ?? "";

                            // 「トレイ入力インジケーター」または「Input indicator」を含む要素
                            if (name.Contains("入力インジケーター") || name.Contains("Input indicator"))
                            {
                                var descClassName = desc.Current.ClassName ?? "";
                                var hWnd = new IntPtr(desc.Current.NativeWindowHandle);
                                var rect = desc.Current.BoundingRectangle;
                                Log($"  発見: Name=\"{name}\" ClassName=\"{descClassName}\" hWnd=0x{hWnd:X}");
                                return (hWnd, name, rect);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        Log("入力インジケーターが見つかりませんでした");
        return null;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetKeyboardLayout(uint idThread);

    /// <summary>
    /// キーボードレイアウトから言語を判別
    /// </summary>
    private string DetectLanguageFromKeyboardLayout()
    {
        var layout = GetKeyboardLayout(0);
        var langId = (int)layout & 0xFFFF;

        Log($"KeyboardLayout: 0x{langId:X4}");

        return langId switch
        {
            // 日本語
            0x0411 => "ja",

            // 韓国語
            0x0412 => "ko",

            // 中国語
            0x0804 => "zh_CN",    // 簡体字（中国）
            0x0404 => "zh_TW",    // 繁体字（台湾）
            0x0C04 => "zh_HK",    // 繁体字（香港）
            0x1004 => "zh_SG",    // 簡体字（シンガポール）

            // 東南アジア
            0x042A => "vi",       // ベトナム語
            0x041E => "th",       // タイ語
            0x0455 => "my",       // ミャンマー語
            0x0453 => "km",       // クメール語
            0x0454 => "lo",       // ラオス語

            // 南アジア
            0x0439 => "hi",       // ヒンディー語
            0x0445 => "bn",       // ベンガル語
            0x0449 => "ta",       // タミル語
            0x044A => "te",       // テルグ語
            0x0461 => "ne",       // ネパール語
            0x045B => "si",       // シンハラ語
            0x0420 => "ur",       // ウルドゥー語（パキスタン）
            0x0820 => "ur_IN",    // ウルドゥー語（インド）
            0x044F => "sa",       // サンスクリット語

            // 中央アジア・モンゴル
            0x0450 => "mn",       // モンゴル語

            // 東欧・ロシア
            0x0419 => "ru",       // ロシア語
            0x0422 => "uk",       // ウクライナ語

            // 中東
            0x0401 => "ar_SA",    // アラビア語（サウジアラビア）
            0x0801 => "ar_IQ",    // アラビア語（イラク）
            0x0C01 => "ar_EG",    // アラビア語（エジプト）
            0x1001 => "ar_LY",    // アラビア語（リビア）
            0x1401 => "ar_DZ",    // アラビア語（アルジェリア）
            0x1801 => "ar_MA",    // アラビア語（モロッコ）
            0x0429 => "fa",       // ペルシャ語

            // 北アフリカ
            0x2000 => "zgh",      // 標準モロッコ タマジット語

            // ヨーロッパ
            0x0408 => "el",       // ギリシャ語
            0x0417 => "rm",       // ロマンシュ語
            0x082E => "dsb",      // 下ソルブ語
            0x042E => "hsb",      // 上ソルブ語

            // 英語
            0x0409 => "en_US",    // 英語（米国）
            0x0809 => "en_GB",    // 英語（英国）
            0x0C09 => "en_AU",    // 英語（オーストラリア）
            0x1009 => "en_CA",    // 英語（カナダ）
            0x1409 => "en_NZ",    // 英語（ニュージーランド）
            0x1809 => "en_IE",    // 英語（アイルランド）
            0x1C09 => "en_ZA",    // 英語（南アフリカ）
            0x2009 => "en_JM",    // 英語（ジャマイカ）
            0x2409 => "en_CB",    // 英語（カリブ）
            0x2809 => "en_BZ",    // 英語（ベリーズ）
            0x2C09 => "en_TT",    // 英語（トリニダード）
            0x3009 => "en_ZW",    // 英語（ジンバブエ）
            0x3409 => "en_PH",    // 英語（フィリピン）
            0x4009 => "en_IN",    // 英語（インド）
            0x4409 => "en_MY",    // 英語（マレーシア）
            0x4809 => "en_SG",    // 英語（シンガポール）

            _ => $"lang_0x{langId:X4}"
        };
    }

    /// <summary>
    /// IME Indicatorの子要素から言語を判別
    /// プロジェクト対応言語: ja, ko, zh-CN, zh-TW, vi, th, ru, ar, hi, uk, fa, bn, ta, te, ne, si, my, km, lo, mn, en
    /// </summary>
    private string DetectLanguageFromChildren(AutomationElement imeIndicator)
    {
        try
        {
            // まずIME Indicator自体のNameもチェック
            var detectedTexts = new List<string>();
            try
            {
                var imeIndicatorName = imeIndicator.Current.Name ?? "";
                Log($"IME Indicator Name: \"{imeIndicatorName}\"");
            }
            catch (Exception ex)
            {
                Log($"IME Indicator Name取得エラー: {ex.Message}");
            }

            // 子要素を取得（TreeScope.Subtree で自身も含む）
            var children = imeIndicator.FindAll(TreeScope.Subtree, AutomationCondition.TrueCondition);
            Log($"FindAll結果: {children.Count}個の要素");

            foreach (AutomationElement child in children)
            {
                try
                {
                    var name = child.Current.Name ?? "";
                    var controlType = child.Current.ControlType;
                    var rect = child.Current.BoundingRectangle;

                    // TextControlTypeのみログ詳細出力
                    if (controlType.Id == 0xC364) // Text
                    {
                        Log($"  [Text] Name=\"{name}\" Rect={{l:{rect.Left:F0} t:{rect.Top:F0} r:{rect.Right:F0} b:{rect.Bottom:F0}}}");
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        detectedTexts.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    Log($"  要素取得エラー: {ex.Message}");
                }
            }

            // 検出テキストをログ出力
            Log($"検出テキスト({detectedTexts.Count}個): {string.Join(", ", detectedTexts.Select(t => $"\"{t}\""))}");

            // 全テキストを結合して判別
            var allText = string.Join(" ", detectedTexts);

            // 言語判別（IMEインジケーター表示文字とキーワードで判別）
            // 日本語
            if (detectedTexts.Any(t => t == "あ" || t == "ア"))
                return "ja_kana";
            if (detectedTexts.Any(t => t == "A") && allText.Contains("日本語"))
                return "ja_alpha";

            // 韓国語
            if (detectedTexts.Any(t => t == "가" || t == "한"))
                return "ko_hangul";
            if (detectedTexts.Any(t => t == "A") && (allText.Contains("한국어") || allText.Contains("Korean")))
                return "ko_alpha";

            // 中国語（簡体字）
            if (detectedTexts.Any(t => t == "中") && allText.Contains("简体"))
                return "zh_CN_chinese";
            if (detectedTexts.Any(t => t == "英") && allText.Contains("简体"))
                return "zh_CN_alpha";

            // 中国語（繁体字）
            if (detectedTexts.Any(t => t == "中") && allText.Contains("繁體"))
                return "zh_TW_chinese";
            if (detectedTexts.Any(t => t == "英") && allText.Contains("繁體"))
                return "zh_TW_alpha";

            // 中国語（共通）
            if (detectedTexts.Any(t => t == "中"))
                return "zh_chinese";
            if (detectedTexts.Any(t => t == "英"))
                return "zh_alpha";

            // ベトナム語 (Telex/VNI入力)
            if (allText.Contains("Tiếng Việt") || allText.Contains("Vietnamese"))
                return "vi";

            // タイ語
            if (detectedTexts.Any(t => ContainsThaiChar(t)))
                return "th";
            if (allText.Contains("ไทย") || allText.Contains("Thai"))
                return "th";

            // ロシア語
            if (detectedTexts.Any(t => ContainsCyrillicChar(t)))
                return "ru";
            if (allText.Contains("Русский") || allText.Contains("Russian"))
                return "ru";

            // ウクライナ語
            if (allText.Contains("Українська") || allText.Contains("Ukrainian"))
                return "uk";

            // アラビア語
            if (detectedTexts.Any(t => ContainsArabicChar(t)))
                return "ar";
            if (allText.Contains("العربية") || allText.Contains("Arabic"))
                return "ar";

            // ペルシャ語
            if (allText.Contains("فارسی") || allText.Contains("Persian") || allText.Contains("Farsi"))
                return "fa";

            // ヒンディー語
            if (detectedTexts.Any(t => ContainsDevanagariChar(t)))
                return "hi";
            if (allText.Contains("हिन्दी") || allText.Contains("Hindi"))
                return "hi";

            // ベンガル語
            if (detectedTexts.Any(t => ContainsBengaliChar(t)))
                return "bn";
            if (allText.Contains("বাংলা") || allText.Contains("Bengali") || allText.Contains("Bangla"))
                return "bn";

            // タミル語
            if (detectedTexts.Any(t => ContainsTamilChar(t)))
                return "ta";
            if (allText.Contains("தமிழ்") || allText.Contains("Tamil"))
                return "ta";

            // テルグ語
            if (detectedTexts.Any(t => ContainsTeluguChar(t)))
                return "te";
            if (allText.Contains("తెలుగు") || allText.Contains("Telugu"))
                return "te";

            // ネパール語
            if (allText.Contains("नेपाली") || allText.Contains("Nepali"))
                return "ne";

            // シンハラ語
            if (detectedTexts.Any(t => ContainsSinhalaChar(t)))
                return "si";
            if (allText.Contains("සිංහල") || allText.Contains("Sinhala"))
                return "si";

            // ミャンマー語
            if (detectedTexts.Any(t => ContainsMyanmarChar(t)))
                return "my";
            if (allText.Contains("မြန်မာ") || allText.Contains("Myanmar") || allText.Contains("Burmese"))
                return "my";

            // クメール語（カンボジア語）
            if (detectedTexts.Any(t => ContainsKhmerChar(t)))
                return "km";
            if (allText.Contains("ខ្មែរ") || allText.Contains("Khmer"))
                return "km";

            // ラオス語
            if (detectedTexts.Any(t => ContainsLaoChar(t)))
                return "lo";
            if (allText.Contains("ລາວ") || allText.Contains("Lao"))
                return "lo";

            // モンゴル語
            if (detectedTexts.Any(t => ContainsMongolianChar(t)))
                return "mn";
            if (allText.Contains("Монгол") || allText.Contains("Mongolian"))
                return "mn";

            // 英語（デフォルト/その他）
            if (detectedTexts.Any(t => t == "A" || t == "ENG" || t == "EN"))
                return "en";

            return "unknown";
        }
        catch (Exception ex)
        {
            Log($"言語判別エラー: {ex.Message}");
            return "unknown";
        }
    }

    // Unicode範囲による文字種判別ヘルパーメソッド
    private static bool ContainsThaiChar(string s) =>
        s.Any(c => c >= 0x0E00 && c <= 0x0E7F);

    private static bool ContainsCyrillicChar(string s) =>
        s.Any(c => c >= 0x0400 && c <= 0x04FF);

    private static bool ContainsArabicChar(string s) =>
        s.Any(c => (c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F));

    private static bool ContainsDevanagariChar(string s) =>
        s.Any(c => c >= 0x0900 && c <= 0x097F);

    private static bool ContainsBengaliChar(string s) =>
        s.Any(c => c >= 0x0980 && c <= 0x09FF);

    private static bool ContainsTamilChar(string s) =>
        s.Any(c => c >= 0x0B80 && c <= 0x0BFF);

    private static bool ContainsTeluguChar(string s) =>
        s.Any(c => c >= 0x0C00 && c <= 0x0C7F);

    private static bool ContainsSinhalaChar(string s) =>
        s.Any(c => c >= 0x0D80 && c <= 0x0DFF);

    private static bool ContainsMyanmarChar(string s) =>
        s.Any(c => c >= 0x1000 && c <= 0x109F);

    private static bool ContainsKhmerChar(string s) =>
        s.Any(c => c >= 0x1780 && c <= 0x17FF);

    private static bool ContainsLaoChar(string s) =>
        s.Any(c => c >= 0x0E80 && c <= 0x0EFF);

    private static bool ContainsMongolianChar(string s) =>
        s.Any(c => (c >= 0x1800 && c <= 0x18AF) || (c >= 0x0400 && c <= 0x04FF)); // キリルも含む

    /// <summary>
    /// スクリーンDCからキャプチャ（絶対座標）
    /// </summary>
    private void CaptureFromScreen(int x, int y, int width, int height, string language)
    {
        var screenDC = GetDC(IntPtr.Zero);
        if (screenDC == IntPtr.Zero)
        {
            Log("スクリーンDC取得失敗");
            return;
        }

        try
        {
            var memDC = CreateCompatibleDC(screenDC);
            var hBitmap = CreateCompatibleBitmap(screenDC, width, height);
            var oldBitmap = SelectObject(memDC, hBitmap);

            // BitBltでコピー
            if (!BitBlt(memDC, 0, 0, width, height, screenDC, x, y, SRCCOPY))
            {
                Log("BitBlt失敗");
                DeleteObject(hBitmap);
                DeleteDC(memDC);
                return;
            }

            SelectObject(memDC, oldBitmap);

            // Bitmapに変換
            using var bitmap = Bitmap.FromHbitmap(hBitmap);

            // 正しい色で新しいBitmapを作成（Graphics.DrawImageで自動変換）
            using var correctedBitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(correctedBitmap))
            {
                g.DrawImage(bitmap, 0, 0);
            }

            SaveBitmapAsPng(correctedBitmap, language);

            DeleteObject(hBitmap);
            DeleteDC(memDC);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    /// <summary>
    /// ウィンドウDCからキャプチャ
    /// </summary>
    private void CaptureFromWindow(IntPtr hwnd, string language)
    {
        if (!GetWindowRect(hwnd, out RECT rect))
        {
            Log("ウィンドウ位置取得失敗");
            return;
        }

        int width = 50;
        int height = rect.Bottom - rect.Top;

        Log($"キャプチャ領域: left={rect.Left}, top={rect.Top}, width={width}, height={height}");

        // スクリーンDCからキャプチャ（より確実）
        CaptureFromScreen(rect.Left, rect.Top, width, height, language);
    }

    /// <summary>
    /// BitmapをPNGとして保存（ナンバリング対応）
    /// </summary>
    private void SaveBitmapAsPng(Bitmap bitmap, string language)
    {
        var saveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IMEIndicatorW",
            "captures");

        if (!Directory.Exists(saveDir))
            Directory.CreateDirectory(saveDir);

        // ファイル名決定（ナンバリング）
        string baseName = $"ime_{language}";
        string fileName;
        int number = 1;

        // 同じ言語のファイルが存在するかチェック
        var existingFiles = Directory.GetFiles(saveDir, $"{baseName}*.png");
        if (existingFiles.Length == 0)
        {
            fileName = $"{baseName}.png";
        }
        else
        {
            // 最大番号を取得
            foreach (var file in existingFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name == baseName)
                {
                    number = Math.Max(number, 2);
                }
                else if (name.StartsWith(baseName + "_"))
                {
                    var numPart = name.Substring(baseName.Length + 1);
                    if (int.TryParse(numPart, out int n))
                    {
                        number = Math.Max(number, n + 1);
                    }
                }
            }
            fileName = $"{baseName}_{number}.png";
        }

        var filePath = Path.Combine(saveDir, fileName);
        bitmap.Save(filePath, ImageFormat.Png);

        Log($"保存完了: {filePath}");
        TxtStatus.Text = $"保存: {fileName}";
    }

    /// <summary>
    /// テキスト検索（全ControlTypeのNameを検索）
    /// </summary>
    private async Task SearchTextAsync(string searchText)
    {
        BtnSearchText.IsEnabled = false;

        try
        {
            TxtStatus.Text = $"「{searchText}」を検索中...";
            TxtLog.Clear();
            GridTextSearchResults.ItemsSource = null; // 前回結果クリア
            TxtTextSearchInfo.Text = "";
            Log($"テキスト検索開始: \"{searchText}\"");

            // ログファイルパスを準備
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IMEIndicatorW");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, $"text_search_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            // 進捗報告用
            var progress = new Progress<string>(msg =>
            {
                TxtStatus.Text = msg;
                Log(msg);
            });

            // 全処理を別スレッドで実行
            var result = await Task.Run(() => SearchTextWorker(logPath, searchText, progress));

            // 結果を表示
            Log($"検索完了: {result.Count}個ヒット");
            TxtTextSearchInfo.Text = $"検索: \"{searchText}\" → {result.Count}個ヒット (ログ: {logPath})";
            GridTextSearchResults.ItemsSource = result;
            TxtStatus.Text = $"完了 - {result.Count}個ヒット ({DateTime.Now:HH:mm:ss})";
            TabTextSearch.IsSelected = true;
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}");
            TxtStatus.Text = $"エラー: {ex.Message}";
        }
        finally
        {
            BtnSearchText.IsEnabled = true;
        }
    }

    /// <summary>
    /// テキスト検索ワーカー（別スレッドで実行）
    /// </summary>
    private List<ElementInfo> SearchTextWorker(string logPath, string searchText, IProgress<string> progress)
    {
        var resultList = new List<ElementInfo>();
        var processedRuntimeIds = new HashSet<string>();

        try
        {
            using var writer = new StreamWriter(logPath, false, System.Text.Encoding.UTF8);
            writer.WriteLine($"=== テキスト検索結果 ===");
            writer.WriteLine($"検索日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"検索テキスト: \"{searchText}\"");
            writer.WriteLine();
            writer.Flush();

            var rootElement = AutomationElement.RootElement;
            var allElements = new List<AutomationElement>();

            // Phase 1: システムトレイ関連ウィンドウを探索
            progress.Report("Phase 1: システムトレイ関連を探索中...");
            writer.WriteLine("=== Phase 1: システムトレイ関連ウィンドウ ===");
            writer.Flush();

            try
            {
                var rootChildren = rootElement.FindAll(TreeScope.Children, AutomationCondition.TrueCondition);

                foreach (AutomationElement child in rootChildren)
                {
                    try
                    {
                        string childName = "";
                        string childClass = "";
                        try { childName = child.Current.Name ?? ""; } catch { }
                        try { childClass = child.Current.ClassName ?? ""; } catch { }

                        bool isRelevant = childName.Contains("IME") || childName.Contains("Indicator") ||
                                          childName.Contains("インジケーター") || childName.Contains("入力") ||
                                          childName.Contains("トレイ") ||
                                          childClass.Contains("Shell") || childClass.Contains("Tray") ||
                                          childClass.Contains("NotifyIcon") || childClass.Contains("Taskbar") ||
                                          childClass.Contains("InputIndicator");

                        if (isRelevant)
                        {
                            writer.WriteLine($"  探索対象: \"{childName}\" [{childClass}]");
                            writer.Flush();

                            try
                            {
                                var elements = child.FindAll(TreeScope.Descendants, AutomationCondition.TrueCondition);
                                writer.WriteLine($"    -> 要素 {elements.Count}個発見");
                                writer.Flush();

                                foreach (AutomationElement elem in elements)
                                {
                                    allElements.Add(elem);
                                }
                            }
                            catch (Exception ex)
                            {
                                writer.WriteLine($"    -> FindAllエラー: {ex.Message}");
                                writer.Flush();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Phase 1 エラー: {ex.Message}");
                writer.Flush();
            }

            writer.WriteLine($"Phase 1 完了: {allElements.Count}個");
            writer.WriteLine();
            writer.Flush();

            // Phase 2: デスクトップ全体検索 - TreeWalker方式（「*」の場合はスキップ）
            if (searchText != "*")
            {
                progress.Report("Phase 2: TreeWalkerでデスクトップ全体を検索中...");
                writer.WriteLine("=== Phase 2: デスクトップ全体検索 (TreeWalker) ===");
                writer.Flush();

                int phase2Count = 0;
                try
                {
                    var walker = TreeWalker.RawViewWalker;
                    var phase2Elements = new List<AutomationElement>();

                    // デスクトップ直下の子要素を取得
                    var child = walker.GetFirstChild(rootElement);
                    while (child != null)
                    {
                        // 各子要素を再帰的に探索
                        TreeWalkerCollectAll(walker, child, phase2Elements, writer, progress, ref phase2Count);
                        child = walker.GetNextSibling(child);
                    }

                    writer.WriteLine($"TreeWalker: 要素 {phase2Count}個発見");
                    writer.Flush();

                    foreach (var elem in phase2Elements)
                    {
                        allElements.Add(elem);
                    }
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"Phase 2 エラー: {ex.Message}");
                    writer.Flush();
                }

                writer.WriteLine($"Phase 2 完了: 合計 {allElements.Count}個（重複含む）");
                writer.WriteLine();
                writer.Flush();
            }
            else
            {
                writer.WriteLine("=== Phase 2: スキップ（*検索のため） ===");
                writer.WriteLine();
                writer.Flush();
            }

            // 要素を処理してNameでフィルタリング
            progress.Report($"要素 {allElements.Count}個を検索中...");
            int idx = 0;
            int hitCount = 0;

            writer.WriteLine($"=== 検索結果: \"{searchText}\" を含む要素 ===");
            writer.Flush();

            foreach (AutomationElement elem in allElements)
            {
                try
                {
                    if (idx % 100 == 0)
                    {
                        progress.Report($"検索中... {idx}/{allElements.Count}");
                    }

                    // 重複チェック
                    string duplicateKey = "";
                    try
                    {
                        var runtimeId = elem.GetRuntimeId();
                        if (runtimeId != null && runtimeId.Length > 0)
                        {
                            duplicateKey = string.Join("-", runtimeId);
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(duplicateKey))
                    {
                        try
                        {
                            var tempName = elem.Current.Name ?? "";
                            var rect = elem.Current.BoundingRectangle;
                            duplicateKey = $"{tempName}|{rect.Left:F0},{rect.Top:F0}";
                        }
                        catch { duplicateKey = $"unknown_{idx}"; }
                    }

                    if (processedRuntimeIds.Contains(duplicateKey))
                    {
                        idx++;
                        continue;
                    }
                    processedRuntimeIds.Add(duplicateKey);

                    // Name取得
                    string name = "";
                    try { name = elem.Current.Name ?? ""; } catch { }

                    // 検索テキストにマッチするかチェック（「*」は全要素、それ以外は完全一致）
                    bool isMatch = searchText == "*" || (!string.IsNullOrEmpty(name) && name == searchText);
                    if (isMatch)
                    {
                        var info = new ElementInfo { Name = name };

                        try { info.ClassName = elem.Current.ClassName ?? ""; } catch { }
                        try { info.ControlTypeName = elem.Current.ControlType?.ProgrammaticName ?? ""; } catch { }
                        try { info.LocalizedControlType = elem.Current.LocalizedControlType ?? ""; } catch { }
                        try
                        {
                            var parent = TreeWalker.RawViewWalker.GetParent(elem);
                            if (parent != null)
                            {
                                try { info.ParentName = parent.Current.Name ?? ""; } catch { }
                            }
                        }
                        catch { }
                        try
                        {
                            var rect = elem.Current.BoundingRectangle;
                            info.RectStr = rect.IsEmpty ? "N/A" : $"l:{rect.Left:F0} t:{rect.Top:F0} r:{rect.Right:F0} b:{rect.Bottom:F0}";
                        }
                        catch { }

                        resultList.Add(info);
                        hitCount++;

                        writer.WriteLine($"[{hitCount}] Name=\"{info.Name}\"");
                        writer.WriteLine($"  ClassName: {info.ClassName}");
                        writer.WriteLine($"  ControlType: {info.ControlTypeName}");
                        writer.WriteLine($"  LocalizedControlType: {info.LocalizedControlType}");
                        writer.WriteLine($"  Parent: {info.ParentName}");
                        writer.WriteLine($"  Rect: {info.RectStr}");
                        writer.WriteLine();
                        writer.Flush();
                    }
                }
                catch { }

                idx++;
            }

            writer.WriteLine($"=== 検索完了: {hitCount}個ヒット ===");
            writer.Flush();
        }
        catch (Exception ex)
        {
            try
            {
                using var writer = new StreamWriter(logPath, true, System.Text.Encoding.UTF8);
                writer.WriteLine($"致命的エラー: {ex.Message}");
                writer.Flush();
            }
            catch { }
        }

        return resultList;
    }

    /// <summary>
    /// TreeWalkerで再帰的に全要素を収集
    /// </summary>
    private void TreeWalkerCollectAll(TreeWalker walker, AutomationElement element,
        List<AutomationElement> elements, StreamWriter writer, IProgress<string> progress, ref int count)
    {
        try
        {
            elements.Add(element);
            count++;

            // 進捗報告（1000要素ごと）
            if (count % 1000 == 0)
            {
                progress.Report($"TreeWalker探索中... {count}個");
                writer.WriteLine($"  ... {count}個探索中");
                writer.Flush();
            }

            // 子要素を再帰的に探索
            var child = walker.GetFirstChild(element);
            while (child != null)
            {
                TreeWalkerCollectAll(walker, child, elements, writer, progress, ref count);
                child = walker.GetNextSibling(child);
            }
        }
        catch
        {
            // 要素へのアクセスに失敗した場合は無視して続行
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UIADiscoveryService.Instance.Save();
            var path = UIADiscoveryService.Instance.GetLogFilePath();
            Log($"ログを保存しました: {path}");
            LoadRecords();
            MessageBox.Show($"保存しました:\n{path}", "保存完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存エラー:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        TreeResult.Items.Clear();
        TxtOutput.Text = "";
        TxtLog.Text = "";
        ListIMEMarks.Items.Clear();
        ListNewElements.Items.Clear();
        _newElementsThisSession.Clear();
        _newCountThisSearch = 0;
        TxtNewCount.Text = "";
        TxtStatus.Text = "";
    }

    private void BtnOpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        var path = UIADiscoveryService.Instance.GetLogFilePath();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\""
            });
        }
        catch (Exception ex)
        {
            Log($"ファイルを開けませんでした: {ex.Message}");
        }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("記録済みの全データをクリアしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            UIADiscoveryService.Instance.Clear();
            LoadRecords();
            Log("記録をクリアしました");
        }
    }

    private void ChkAutoRefresh_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkAutoRefresh.IsChecked == true)
        {
            // ComboBoxから間隔を取得
            int intervalMs = 1000;
            if (CmbInterval.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
            {
                if (!int.TryParse(tagStr, out intervalMs))
                {
                    intervalMs = 1000; // パース失敗時のデフォルト値
                }
            }

            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs)
            };
            _autoRefreshTimer.Tick += (s, args) =>
            {
                UpdateActiveWindowInfo();
                SearchIMEIndicator(false);
            };
            _autoRefreshTimer.Start();
            Log($"自動更新を開始 ({intervalMs}ms)");
        }
        else
        {
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer = null;
            Log("自動更新を停止");
        }
    }

#if DEBUG
    private void ChkDebugImageSave_Changed(object sender, RoutedEventArgs e)
    {
        Services.PixelIMEDetector.EnableDebugImageSave = ChkDebugImageSave.IsChecked == true;
        Log($"調整用取得画像保存: {(ChkDebugImageSave.IsChecked == true ? "有効" : "無効")}");
    }
#else
    private void ChkDebugImageSave_Changed(object sender, RoutedEventArgs e)
    {
        // Releaseビルドでは何もしない
        ChkDebugImageSave.IsChecked = false;
        ChkDebugImageSave.IsEnabled = false;
    }
#endif

    /// <summary>
    /// ハンドル監視ボタンクリック - DetectIMEState2の時間計測
    /// </summary>
    private void BtnMonitorHandle_Click(object sender, RoutedEventArgs e)
    {
        if (_isMonitoring)
        {
            // 監視停止
            _handleMonitorTimer?.Stop();
            _handleMonitorTimer = null;
            _isMonitoring = false;
            BtnMonitorHandle.Content = "ハンドル監視";
            BtnMonitorHandle.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xCC, 0x66, 0xCC));
            Log("ハンドル監視を停止しました");
            TxtStatus.Text = "監視停止";
        }
        else
        {
            // タイマー開始（500ms間隔）
            _handleMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _handleMonitorTimer.Tick += HandleMonitorTimer_Tick;
            _handleMonitorTimer.Start();

            _isMonitoring = true;
            BtnMonitorHandle.Content = "監視停止";
            BtnMonitorHandle.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0x66, 0x66));
            Log("ハンドル監視を開始しました（500ms間隔）");
            TxtStatus.Text = "監視中...";
        }
    }

    /// <summary>
    /// ハンドル監視タイマーのTick処理 - DetectIMEState2の時間計測
    /// </summary>
    private void HandleMonitorTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // アクティブウィンドウの変化検出
            var currentActiveWindow = GetForegroundWindow();
            if (currentActiveWindow != _lastActiveWindowHandle)
            {
                _lastActiveWindowHandle = currentActiveWindow;
                Span<char> buffer = stackalloc char[256];
                int len = GetWindowText(currentActiveWindow, buffer);
                var title = len > 0 ? new string(buffer[..len]) : "(no title)";
                _ = GetWindowThreadProcessId(currentActiveWindow, out var processId);
                string processName = "";
                try
                {
                    var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch { }
                Log($"★ Window変化: {processName} - {title}");
            }

            var result = Services.PixelIMEDetector.Instance.DetectIMEState2(Services.LanguageType.Japanese);

            var state = result.IsOn.HasValue ? (result.IsOn.Value ? "ON" : "OFF") : "N/A";
            Log($"Total={result.TotalTimeMs:F2}ms (GetRect={result.GetRectTimeMs:F2}ms, Analyze={result.AnalyzeTimeMs:F2}ms) [{state}]");
        }
        catch (Exception ex)
        {
            Log($"監視エラー: {ex.Message}");
        }
    }

    private void SearchIMEIndicator(bool focusIMEMark)
    {
        // 砂時計カーソル
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            TxtStatus.Text = "探索中...";
            _newCountThisSearch = 0;
            var sb = new StringBuilder();
            TreeResult.Items.Clear();
            ListIMEMarks.Items.Clear();

            var activeWindowTitle = GetActiveWindowTitle();
            UpdateActiveWindowInfo();

            Log("探索開始...");

            // ルート要素（デスクトップ）を取得
            var rootElement = AutomationElement.RootElement;
            Log($"ルート要素取得: {rootElement.Current.Name}");
            sb.AppendLine($"=== ルート: {rootElement.Current.Name} ===");
            sb.AppendLine($"=== アクティブウィンドウ: {activeWindowTitle} ===\n");

            // 「IME Indicator」を直接名前で検索（高速）
            Log("IME Indicator を名前で検索中...");
            var nameCondition = new PropertyCondition(AutomationElement.NameProperty, "IME Indicator");
            var imeIndicatorDirect = rootElement.FindFirst(TreeScope.Children, nameCondition);

            if (imeIndicatorDirect != null)
            {
                Log($"IME Indicator を直接発見!");
                sb.AppendLine($"\n=== IME Indicator (直接検索) ===\n");
                var treeItem = new TreeViewItem { Header = "IME Indicator", IsExpanded = true };
                ExploreElement(imeIndicatorDirect, sb, treeItem, 0, "IME Indicator", "", activeWindowTitle, focusIMEMark);
                TreeResult.Items.Add(treeItem);

                TxtOutput.Text = sb.ToString();
                TxtStatus.Text = $"完了 ({DateTime.Now:HH:mm:ss})";

                if (_newCountThisSearch > 0)
                    TxtNewCount.Text = $"NEW: {_newCountThisSearch} 件";
                else
                    TxtNewCount.Text = "";

                if (focusIMEMark && ListIMEMarks.Items.Count > 0)
                    TabIMEMark.IsSelected = true;
                return;
            }

            Log("IME Indicator 未検出。デスクトップ全体を探索...");
            sb.AppendLine("\n*** IME Indicator が見つかりませんでした - デスクトップ全体を探索 ***\n");

            // デスクトップ直下の全要素を探索
            var rootChildren = rootElement.FindAll(TreeScope.Children, AutomationCondition.TrueCondition);
            Log($"デスクトップ直下: {rootChildren.Count} 要素");

            foreach (AutomationElement child in rootChildren)
            {
                try
                {
                    var childName = child.Current.Name ?? "";
                    var childClass = child.Current.ClassName ?? "";

                    // IME関連またはシステムトレイ関連を探索
                    if (childName.Contains("IME") || childName.Contains("Indicator") ||
                        childName.Contains("インジケーター") || childName.Contains("入力") ||
                        childClass.Contains("Shell") || childClass.Contains("Tray") ||
                        childClass.Contains("NotifyIcon") || childClass.Contains("InputIndicator"))
                    {
                        sb.AppendLine($">>> 探索: {childName} [{childClass}]");
                        var treeItem = new TreeViewItem { Header = $"{childName} [{childClass}]", IsExpanded = true };
                        ExploreElement(child, sb, treeItem, 0, childName, "", activeWindowTitle, focusIMEMark);
                        TreeResult.Items.Add(treeItem);
                    }
                }
                catch { }
            }

            TxtOutput.Text = sb.ToString();
            TxtStatus.Text = $"完了 ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            Log($"探索エラー: {ex.Message}");
            TxtStatus.Text = "エラー";
            MessageBox.Show($"探索エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // カーソルを元に戻す
            Mouse.OverrideCursor = null;
        }
    }

    private void ExploreElement(AutomationElement element, StringBuilder sb, TreeViewItem parentItem, int depth,
        string parentName, string parentControlType, string activeWindowTitle, bool focusIMEMark)
    {
        const int maxDepth = 6;
        if (depth > maxDepth) return;

        var indent = new string(' ', depth * 2);

        try
        {
            var name = element.Current.Name ?? "";
            var className = element.Current.ClassName ?? "";
            var controlType = element.Current.ControlType;
            var localizedType = element.Current.LocalizedControlType ?? "";

            // BoundingRectangle
            var rect = element.Current.BoundingRectangle;
            var rectStr = rect.IsEmpty ? "N/A" : $"{{l:{rect.Left:F0} t:{rect.Top:F0} r:{rect.Right:F0} b:{rect.Bottom:F0}}}";

            sb.AppendLine($"{indent}Name: \"{name}\"");
            sb.AppendLine($"{indent}  ClassName: \"{className}\"");
            sb.AppendLine($"{indent}  ControlType: {controlType.ProgrammaticName} (0x{controlType.Id:X})");
            sb.AppendLine($"{indent}  LocalizedControlType: \"{localizedType}\"");
            sb.AppendLine($"{indent}  BoundingRectangle: {rectStr}");

            // 発見を記録
            var isNew = UIADiscoveryService.Instance.RecordElement(element, parentName, parentControlType, activeWindowTitle);
            var newMarker = isNew ? " [NEW]" : "";

            if (isNew)
            {
                _newCountThisSearch++;
                var newEntry = $"[{DateTime.Now:HH:mm:ss}] {parentName} > \"{name}\" ({localizedType})";
                _newElementsThisSession.Add(newEntry);
                ListNewElements.Items.Insert(0, newEntry);
                sb.AppendLine($"{indent}  >>> NEW ELEMENT <<<");
            }

            // ツリーにも追加
            var itemHeader = $"{name} [{controlType.ProgrammaticName}]{newMarker}";
            if (!string.IsNullOrEmpty(name))
            {
                itemHeader = $"\"{name}\" [{localizedType}]{newMarker}";
            }

            // ButtonControlType と TextControlType を強調
            bool isButton = controlType == ControlType.Button;
            bool isText = controlType == ControlType.Text;

            if (isButton)
                itemHeader = $"[BTN] {itemHeader}";
            if (isText)
                itemHeader = $"[TXT] {itemHeader}";

            var treeItem = new TreeViewItem
            {
                Header = itemHeader,
                IsExpanded = depth < 3 || isButton || isText,
                Foreground = isNew ? System.Windows.Media.Brushes.Red :
                             isButton ? System.Windows.Media.Brushes.Blue :
                             isText ? System.Windows.Media.Brushes.Green :
                             System.Windows.Media.Brushes.Black,
                FontWeight = (isButton || isText) ? FontWeights.Bold : FontWeights.Normal
            };

            // 全ての ButtonControlType を記録（IMEマーク候補）→別ファイルにログ出力
            if (controlType == ControlType.Button)
            {
                // ListIMEMarksには追加しない（Textのみ表示）
                sb.AppendLine($"{indent}  >>> BUTTON (0xC350) <<<");
            }

            // 全ての TextControlType を記録（IME状態テキスト候補）
            if (controlType == ControlType.Text)
            {
                var textInfo = $"[Text] Name=\"{name}\" (len={name?.Length ?? 0})\n  Rect: {rectStr}\n  Parent: {parentName}";
                ListIMEMarks.Items.Add(textInfo);
            }

            // 子要素を探索
            var children = element.FindAll(TreeScope.Children, AutomationCondition.TrueCondition);
            if (children.Count > 0)
            {
                sb.AppendLine($"{indent}  Children: {children.Count}");
                sb.AppendLine();

                foreach (AutomationElement child in children)
                {
                    var childItem = new TreeViewItem { IsExpanded = depth < 2 };
                    // 名前が空の場合は親の名前を引き継ぐ
                    var childParentName = string.IsNullOrEmpty(name) ? parentName : name;
                    ExploreElement(child, sb, childItem, depth + 1, childParentName, controlType.ProgrammaticName, activeWindowTitle, focusIMEMark);
                    if (childItem.Header != null)
                    {
                        treeItem.Items.Add(childItem);
                    }
                }
            }
            else
            {
                sb.AppendLine();
            }

            parentItem.Items.Add(treeItem);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{indent}[Error: {ex.Message}]");
        }
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] {message}";
        TxtLog.Text += logMessage + "\n";
        TxtLog.ScrollToEnd();
        // VS出力ウィンドウにも出力
        System.Diagnostics.Debug.WriteLine($"[UIATest] {logMessage}");
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoRefreshTimer?.Stop();
        _handleMonitorTimer?.Stop();

        // 終了時に常に保存
        try
        {
            UIADiscoveryService.Instance.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        base.OnClosed(e);
    }
}
