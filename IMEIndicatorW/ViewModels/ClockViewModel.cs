using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;

namespace IMEIndicatorClock.ViewModels;

/// <summary>
/// 時計のViewModel
/// </summary>
public partial class ClockViewModel : ObservableObject, IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    // 和暦用CultureInfo（遅延初期化）
    private static readonly Lazy<CultureInfo> JapaneseCulture = new(() =>
    {
        var culture = new CultureInfo("ja-JP");
        culture.DateTimeFormat.Calendar = new JapaneseCalendar();
        return culture;
    });

    private ClockSettings Settings => _settingsManager?.Settings?.Clock ?? new ClockSettings();

    [ObservableProperty]
    private string _timeText = "";

    [ObservableProperty]
    private string _dateText = "";

    [ObservableProperty]
    private double _width = 200;

    [ObservableProperty]
    private double _height = 100;

    [ObservableProperty]
    private double _opacity = 0.9;

    [ObservableProperty]
    private Brush _backgroundColor = new SolidColorBrush(Color.FromRgb(59, 130, 246));

    [ObservableProperty]
    private Brush _textColor = new SolidColorBrush(Colors.White);

    [ObservableProperty]
    private double _fontSize = 24;

    [ObservableProperty]
    private FontFamily _fontFamily = new("Segoe UI");

    [ObservableProperty]
    private double _positionX = 200;

    [ObservableProperty]
    private double _positionY = 100;

    [ObservableProperty]
    private ClockDisplayMode _displayMode = ClockDisplayMode.TimeOnly;

    [ObservableProperty]
    private ClockStyle _clockStyle = ClockStyle.Digital;

    /// <summary>
    /// アナログ時計の秒針表示
    /// </summary>
    [ObservableProperty]
    private bool _showSeconds = true;

    [ObservableProperty]
    private string _timeFormat = "HH:mm:ss";

    [ObservableProperty]
    private string _dateFormat = "yyyy/MM/dd";

    [ObservableProperty]
    private bool _isIMEOn;

    [ObservableProperty]
    private int _displayIndex;

    [ObservableProperty]
    private bool _useIMEIndicatorColors;

    private LanguageType _currentLanguage = LanguageType.English;

    [ObservableProperty]
    private DateTimeLayout _layout = DateTimeLayout.VerticalTimeFirst;

    [ObservableProperty]
    private DateTimeFormatPreset _dateFormatPreset = DateTimeFormatPreset.Normal;

    [ObservableProperty]
    private DateTimeFormatPreset _timeFormatPreset = DateTimeFormatPreset.Normal;

    public string BackgroundColorIMEOff
    {
        get => Settings.BackgroundColorIMEOff;
        set
        {
            Settings.BackgroundColorIMEOff = value;
            UpdateBackgroundColor();
        }
    }

    public string BackgroundColorIMEOn
    {
        get => Settings.BackgroundColorIMEOn;
        set
        {
            Settings.BackgroundColorIMEOn = value;
            UpdateBackgroundColor();
        }
    }

    // アナログ時計の設定
    [ObservableProperty]
    private double _analogClockSize = 200;

    [ObservableProperty]
    private Brush _analogClockColor = null!;

    [ObservableProperty]
    private Brush _analogTextColor = null!;

    public string AnalogClockColorHex
    {
        get
        {
            try { return Settings.AnalogClockColor ?? "#FFFF00"; }
            catch { return "#FFFF00"; }
        }
        set
        {
            try
            {
                DbgLog.Log(1, $"AnalogClockColorHex setter: {value}");
                Settings.AnalogClockColor = value;
                AnalogClockColor = new SolidColorBrush(ColorHelper.ParseColor(value));
                OnPropertyChanged(nameof(AnalogClockColor));
            }
            catch (Exception ex)
            {
                DbgLog.W($"AnalogClockColorHex setter error: {ex.Message}");
            }
        }
    }

    public string AnalogTextColorHex
    {
        get
        {
            try { return Settings.AnalogTextColor ?? "#FFFFFF"; }
            catch { return "#FFFFFF"; }
        }
        set
        {
            try
            {
                DbgLog.Log(1, $"AnalogTextColorHex setter: {value}");
                Settings.AnalogTextColor = value;
                AnalogTextColor = new SolidColorBrush(ColorHelper.ParseColor(value));
                OnPropertyChanged(nameof(AnalogTextColor));
            }
            catch (Exception ex)
            {
                DbgLog.W($"AnalogTextColorHex setter error: {ex.Message}");
            }
        }
    }

    public string TextColorHex
    {
        get => Settings.TextColor ?? "#FFFFFF";
        set
        {
            Settings.TextColor = value;
            TextColor = new SolidColorBrush(ColorHelper.ParseColor(value));
            OnPropertyChanged(nameof(TextColor));
        }
    }

    // アナログ時計用
    [ObservableProperty]
    private double _hourAngle;

    [ObservableProperty]
    private double _minuteAngle;

    [ObservableProperty]
    private double _secondAngle;

    public ClockViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        // デフォルト値を先に設定
        _analogClockColor = new SolidColorBrush(Color.FromRgb(255, 255, 0));  // 黄色
        _analogTextColor = new SolidColorBrush(Colors.White);

        LoadSettings();

        _timer = new DispatcherTimer();
        UpdateTimerInterval();
        _timer.Tick += OnTimerTick;
        _timer.Start();

        UpdateTime();
    }

    private void LoadSettings()
    {
        try
        {
            // Settings参照をキャッシュ（途中でnullになる問題を防ぐ）
            var settings = _settingsManager?.Settings?.Clock;
            if (settings == null)
            {
                DbgLog.W("ClockVM.LoadSettings: Settings.Clock is null");
                return;
            }

            DbgLog.Log(3, $"ClockVM.LoadSettings: settings.PositionX={settings.PositionX}, settings.PositionY={settings.PositionY}");

            // 位置設定を最初に読み込む（他の設定で例外が発生しても位置は復元される）
            Width = settings.Width;
            Height = settings.Height;

            if (settings.PositionX == -1 || settings.PositionY == -1)
            {
                // 初回起動時
                DisplayIndex = settings.DisplayIndex;
                InitializeDefaultPosition();
            }
            else
            {
                // 座標とディスプレイの検証
                var (validX, validY, validDisplay) = DisplayHelper.GetValidPosition(
                    settings.PositionX, settings.PositionY,
                    Width, Height,
                    settings.DisplayIndex,
                    useTopRight: true);

                PositionX = validX;
                PositionY = validY;
                DisplayIndex = validDisplay;

                // 設定も更新（モニター構成変更時）
                if (validDisplay != settings.DisplayIndex)
                {
                    settings.DisplayIndex = validDisplay;
                    DbgLog.Log(3, $"ClockVM.LoadSettings: DisplayIndex補正 {settings.DisplayIndex} → {validDisplay}");
                }

                DbgLog.Log(3, $"ClockVM.LoadSettings: After load - PositionX={PositionX}, PositionY={PositionY}, DisplayIndex={DisplayIndex}");
            }

            // その他の設定
            Opacity = settings.Opacity;
            FontSize = settings.FontSize;
            FontFamily = new FontFamily(settings.FontName ?? "Segoe UI");
            TextColor = new SolidColorBrush(ColorHelper.ParseColor(settings.TextColor ?? "#FFFFFF"));
            DisplayMode = settings.DisplayMode;
            ClockStyle = settings.Style;
            ShowSeconds = settings.ShowSeconds;
            TimeFormat = settings.TimeFormat ?? "HH:mm:ss";
            DateFormat = settings.DateFormat ?? "yyyy/MM/dd";
            Layout = settings.Layout;
            DateFormatPreset = settings.DateFormatPreset;
            TimeFormatPreset = settings.TimeFormatPreset;
            UseIMEIndicatorColors = settings.UseIMEIndicatorColors;

            // アナログ時計設定の読み込み
            AnalogClockSize = settings.AnalogClockSize;
            var analogClockColorHex = settings.AnalogClockColor ?? "#FFFF00";
            var analogTextColorHex = settings.AnalogTextColor ?? "#FFFFFF";
            DbgLog.Log(1, $"LoadSettings: AnalogClockSize={AnalogClockSize}, AnalogClockColor={analogClockColorHex}, AnalogTextColor={analogTextColorHex}");

            AnalogClockColor = new SolidColorBrush(ColorHelper.ParseColor(analogClockColorHex));
            AnalogTextColor = new SolidColorBrush(ColorHelper.ParseColor(analogTextColorHex));

            // 明示的にプロパティ変更を通知
            OnPropertyChanged(nameof(AnalogClockColor));
            OnPropertyChanged(nameof(AnalogTextColor));

            UpdateBackgroundColor();
        }
        catch (Exception ex)
        {
            // デフォルト値を使用
            DbgLog.W($"LoadSettingsでエラー: {ex.Message}");
            AnalogClockColor = new SolidColorBrush(Color.FromRgb(255, 255, 0));
            AnalogTextColor = new SolidColorBrush(Colors.White);
        }
    }

    /// <summary>
    /// タイマー間隔を更新（ミリ秒・秒表示の場合は短く）
    /// </summary>
    private void UpdateTimerInterval()
    {
        var timeFormat = GetTimeFormat();
        
        // ミリ秒フォーマット（f, ff, fff）が含まれているか
        if (ContainsMilliseconds(timeFormat))
        {
            _timer.Interval = TimeSpan.FromMilliseconds(50);  // 20fps
        }
        else if (ContainsSeconds(timeFormat))
        {
            _timer.Interval = TimeSpan.FromMilliseconds(200); // 秒表示あり
        }
        else
        {
            _timer.Interval = TimeSpan.FromSeconds(1);        // 分表示のみ
        }
    }

    /// <summary>
    /// フォーマットにミリ秒指定子（f, ff, fff等）が含まれているか
    /// </summary>
    private static bool ContainsMilliseconds(string format)
    {
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '\'' || c == '"')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote && (c == 'f' || c == 'F'))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// フォーマットに秒指定子（s, ss）が含まれているか
    /// </summary>
    private static bool ContainsSeconds(string format)
    {
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '\'' || c == '"')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote && c == 's')
            {
                return true;
            }
        }
        return false;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateTime();
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;

        // フォーマットプリセットに基づいてフォーマットを取得
        var timeFormat = GetTimeFormat();
        var dateFormat = GetDateFormat();

        // デジタル表示用（和暦対応）
        TimeText = FormatDateTime(now, timeFormat);
        DateText = FormatDateTime(now, dateFormat);

        // アナログ時計用
        HourAngle = (now.Hour % 12 + now.Minute / 60.0) * 30;
        MinuteAngle = (now.Minute + now.Second / 60.0) * 6;
        SecondAngle = now.Second * 6;
    }

    /// <summary>
    /// 日時をフォーマット（和暦対応）
    /// フォーマット文字列に g または gg が含まれている場合は和暦を使用
    /// </summary>
    private static string FormatDateTime(DateTime dateTime, string format)
    {
        // フォーマットに 'g' が含まれているか確認（ただし引用符内は除く）
        if (ContainsEraSpecifier(format))
        {
            return dateTime.ToString(format, JapaneseCulture.Value);
        }
        return dateTime.ToString(format);
    }

    /// <summary>
    /// フォーマット文字列に元号指定子(g/gg)が含まれているかチェック
    /// </summary>
    private static bool ContainsEraSpecifier(string format)
    {
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '\'' || c == '"')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote && c == 'g')
            {
                return true;
            }
        }
        return false;
    }

    private string GetTimeFormat()
    {
        return TimeFormatPreset switch
        {
            DateTimeFormatPreset.Full => "H時mm分ss秒",
            DateTimeFormatPreset.Normal => "HH:mm:ss",
            DateTimeFormatPreset.Short => "H:mm",
            DateTimeFormatPreset.Custom1 => Settings.CustomTimeFormat1 ?? "H:mm",
            DateTimeFormatPreset.Custom2 => Settings.CustomTimeFormat2 ?? "HH:mm:ss",
            _ => "HH:mm:ss"
        };
    }

    private string GetDateFormat()
    {
        return DateFormatPreset switch
        {
            DateTimeFormatPreset.Full => "yyyy年M月d日 dddd",
            DateTimeFormatPreset.Normal => "yyyy/MM/dd",
            DateTimeFormatPreset.Short => "M/d",
            DateTimeFormatPreset.Custom1 => Settings.CustomDateFormat1 ?? "M/d",
            DateTimeFormatPreset.Custom2 => Settings.CustomDateFormat2 ?? "MM-dd",
            _ => "yyyy/MM/dd"
        };
    }

    /// <summary>
    /// IME状態を更新する
    /// </summary>
    public void UpdateIMEState(LanguageInfo languageInfo)
    {
        IsIMEOn = languageInfo.IsIMEOn;
        _currentLanguage = languageInfo.Language;
        UpdateBackgroundColor();
    }

    private void UpdateBackgroundColor()
    {
        string colorHex = "#3B82F6";  // デフォルト値

        try
        {
            if (UseIMEIndicatorColors)
            {
                // IMEインジケータの言語別色設定を使用
                var imeSettings = _settingsManager?.Settings?.IMEIndicator;
                var languageColors = imeSettings?.LanguageColors;

                if (languageColors != null)
                {
                    var languageKey = _currentLanguage.ToString();

                    // IME OFFの場合は英語表示
                    if (!IsIMEOn && _currentLanguage != LanguageType.English)
                    {
                        languageKey = "English";
                    }

                    if (languageColors.TryGetValue(languageKey, out var langSettings))
                    {
                        colorHex = langSettings.Color ?? "#3B82F6";
                    }
                }
            }
            else
            {
                // 従来のIME ON/OFF色を使用
                colorHex = IsIMEOn ? Settings.BackgroundColorIMEOn : Settings.BackgroundColorIMEOff;
            }
        }
        catch (Exception ex)
        {
            DbgLog.W($"UpdateBackgroundColor エラー: {ex.Message}");
        }

        BackgroundColor = new SolidColorBrush(ColorHelper.ParseColor(colorHex));
    }

    /// <summary>
    /// ウィンドウ位置が画面内に収まるように調整
    /// </summary>
    private void EnsurePositionWithinBounds()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;

        // 現在のディスプレイを取得
        int displayIndex = DisplayIndex;
        if (displayIndex >= screens.Length) displayIndex = 0;
        var screen = screens[displayIndex].WorkingArea;

        const double margin = 10;

        var oldX = PositionX;
        var oldY = PositionY;

        // X座標の境界チェック
        double maxX = screen.Right - Width - margin;
        double minX = screen.Left + margin;
        if (PositionX > maxX) PositionX = maxX;
        if (PositionX < minX) PositionX = minX;

        // Y座標の境界チェック
        double maxY = screen.Bottom - Height - margin;
        double minY = screen.Top + margin;
        if (PositionY > maxY) PositionY = maxY;
        if (PositionY < minY) PositionY = minY;

        if (oldX != PositionX || oldY != PositionY)
        {
            DbgLog.Log(3, $"EnsurePositionWithinBounds: 調整 ({oldX},{oldY}) -> ({PositionX},{PositionY}), screen={displayIndex}, bounds=({minX},{minY})-({maxX},{maxY})");
        }
    }

    /// <summary>
    /// 初回起動時のデフォルト位置（画面右上）を設定
    /// </summary>
    private void InitializeDefaultPosition()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;

        int displayIndex = DisplayIndex;
        if (displayIndex >= screens.Length) displayIndex = 0;
        var screen = screens[displayIndex].WorkingArea;

        const double offset = 10;
        PositionX = screen.Right - Width - offset;
        PositionY = screen.Top + offset;

        // 設定に保存
        Settings.PositionX = PositionX;
        Settings.PositionY = PositionY;
        DbgLog.Log(3, $"ClockVM.InitializeDefaultPosition: PositionX={PositionX}, PositionY={PositionY}");
    }

    partial void OnWidthChanged(double value) { if (_settingsManager != null) Settings.Width = value; }
    partial void OnHeightChanged(double value) { if (_settingsManager != null) Settings.Height = value; }
    partial void OnOpacityChanged(double value) { if (_settingsManager != null) Settings.Opacity = value; }
    partial void OnPositionXChanged(double value)
    {
        if (_settingsManager != null)
        {
            Settings.PositionX = value;
            DbgLog.Log(4, $"ClockVM.OnPositionXChanged: {value} -> Settings.PositionX={Settings.PositionX}");
        }
    }
    partial void OnPositionYChanged(double value)
    {
        if (_settingsManager != null)
        {
            Settings.PositionY = value;
            DbgLog.Log(4, $"ClockVM.OnPositionYChanged: {value} -> Settings.PositionY={Settings.PositionY}");
        }
    }
    partial void OnDisplayModeChanged(ClockDisplayMode value) { if (_settingsManager != null) Settings.DisplayMode = value; }
    partial void OnFontFamilyChanged(FontFamily value) { if (_settingsManager != null) Settings.FontName = value.Source; }
    partial void OnFontSizeChanged(double value) { if (_settingsManager != null) Settings.FontSize = value; }
    partial void OnClockStyleChanged(ClockStyle value) { if (_settingsManager != null) Settings.Style = value; }
    partial void OnShowSecondsChanged(bool value) { if (_settingsManager != null) Settings.ShowSeconds = value; }
    partial void OnTimeFormatChanged(string value) { if (_settingsManager != null) Settings.TimeFormat = value; }
    partial void OnDateFormatChanged(string value) { if (_settingsManager != null) Settings.DateFormat = value; }
    partial void OnDisplayIndexChanged(int value) { if (_settingsManager != null) Settings.DisplayIndex = value; }
    partial void OnLayoutChanged(DateTimeLayout value) { if (_settingsManager != null) Settings.Layout = value; }
    partial void OnAnalogClockSizeChanged(double value) { if (_settingsManager != null) Settings.AnalogClockSize = value; }
    partial void OnUseIMEIndicatorColorsChanged(bool value)
    {
        if (_settingsManager != null)
        {
            Settings.UseIMEIndicatorColors = value;
            UpdateBackgroundColor();
        }
    }
    partial void OnDateFormatPresetChanged(DateTimeFormatPreset value)
    {
        if (_settingsManager == null) return;
        Settings.DateFormatPreset = value;
        UpdateTime();
    }
    partial void OnTimeFormatPresetChanged(DateTimeFormatPreset value)
    {
        if (_settingsManager == null) return;
        Settings.TimeFormatPreset = value;
        UpdateTimerInterval();
        UpdateTime();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer.Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 設定を再読み込みする
    /// </summary>
    public void ReloadSettings()
    {
        LoadSettings();
        UpdateTimerInterval();
    }

    /// <summary>
    /// 現在の位置からディスプレイインデックスを自動検出・更新
    /// </summary>
    public void UpdateDisplayFromPosition()
    {
        int detectedDisplay = DisplayHelper.GetDisplayIndexFromPosition(
            PositionX, PositionY, Width, Height);

        if (detectedDisplay >= 0 && detectedDisplay != DisplayIndex)
        {
            DbgLog.Log(3, $"ClockVM: ディスプレイ自動検出 {DisplayIndex} → {detectedDisplay}");
            DisplayIndex = detectedDisplay;
        }
    }
}
