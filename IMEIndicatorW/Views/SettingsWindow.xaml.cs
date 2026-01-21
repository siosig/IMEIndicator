using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;
using IMEIndicatorClock.ViewModels;

namespace IMEIndicatorClock.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isInitializing = true;
    private readonly Dictionary<string, LanguageControlSet> _languageControls = new();
    private readonly List<string> _imeFontNames = new();

    private static readonly (string key, string displayName)[] LanguageList = new[]
    {
        ("English", "英語"),
        ("Japanese", "日本語"),
        ("Korean", "韓国語"),
        ("ChineseSimplified", "中国語(簡体)"),
        ("ChineseTraditional", "中国語(繁体)"),
        ("Vietnamese", "ベトナム語"),
        ("Thai", "タイ語"),
        ("Hindi", "ヒンディー語"),
        ("Bengali", "ベンガル語"),
        ("Tamil", "タミル語"),
        ("Telugu", "テルグ語"),
        ("Nepali", "ネパール語"),
        ("Sinhala", "シンハラ語"),
        ("Myanmar", "ミャンマー語"),
        ("Khmer", "クメール語"),
        ("Lao", "ラオ語"),
        ("Mongolian", "モンゴル語"),
        ("Arabic", "アラビア語"),
        ("Persian", "ペルシャ語"),
        ("Hebrew", "ヘブライ語"),
        ("Ukrainian", "ウクライナ語"),
        ("Russian", "ロシア語"),
        ("Greek", "ギリシャ語"),
        ("Other", "その他")
    };

    private class LanguageControlSet
    {
        public ComboBox FontCombo { get; set; } = null!;
        public ColorPickerButton ColorPicker { get; set; } = null!;
        public TextBox TextBox { get; set; } = null!;
    }

    private static readonly string[] CommonFonts = new[]
    {
        "Segoe UI",
        "Yu Gothic UI",
        "Meiryo UI",
        "MS Gothic",
        "MS Mincho",
        "Arial",
        "Times New Roman",
        "Consolas",
        "Courier New"
    };

    // フォントが指定された文字を表示できるかチェック
    private static bool CanFontDisplayCharacter(string fontName, string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        try
        {
            var typeface = new Typeface(new FontFamily(fontName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                return false;

            foreach (var c in text)
            {
                if (!glyphTypeface.CharacterToGlyphMap.ContainsKey(c))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 日付フォーマットサンプル
    private static readonly string[] DateFormatSamples = new[]
    {
        "yyyy/MM/dd",           // 2026/01/13
        "yyyy-MM-dd",           // 2026-01-13
        "yyyy年M月d日",         // 2026年1月13日
        "M/d",                  // 1/13
        "MM月dd日(ddd)",        // 01月13日(火)
        "yyyy/MM/dd(dddd)",     // 2026/01/13(火曜日)
        "gg y年M月d日",         // 令和 8年1月13日
        "ggy/MM/dd",            // 令和8/01/13
        "yy.MM.dd ddd",         // 26.01.13 火
        "d日(ddd)"              // 13日(火)
    };

    // 時刻フォーマットサンプル
    private static readonly string[] TimeFormatSamples = new[]
    {
        "HH:mm:ss",             // 14:30:45
        "HH:mm",                // 14:30
        "H:mm",                 // 14:30
        "H時mm分ss秒",          // 14時30分45秒
        "hh:mm tt",             // 02:30 午後
        "h:mm tt",              // 2:30 午後
        "tt h時mm分",           // 午後 2時30分
        "HH:mm:ss.fff",         // 14:30:45.123
        "H:mm:ss",              // 14:30:45
        "hh:mm:ss tt"           // 02:30:45 午後
    };

    public Color ClockColorOff { get; set; }
    public Color ClockColorOn { get; set; }
    public Color AnalogClockColor { get; set; }
    public bool ShowSeconds { get; set; }

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 設定ウィンドウが開いているフラグを設定
        App.Instance.IsSettingsWindowOpen = true;
        Closed += OnWindowClosed;

        // ViewModelのプロパティ変更を購読（ディスプレイ選択の自動更新用）
        _viewModel.IMEIndicatorViewModel.PropertyChanged += OnIMEIndicatorViewModelPropertyChanged;
        _viewModel.ClockViewModel.PropertyChanged += OnClockViewModelPropertyChanged;

        ApplyLocalization();
        LoadFonts();
        LoadDisplays();
        LoadClockSettings();
        LoadLanguageSettings();
        LoadVisibilitySettings();
        LoadIMELanguageColors();
        DebugModeCheck.IsChecked = IMEMonitor.DebugMode;

        // デバッグ版のみ表示するパネルの制御
        UpdateDebugOnlyPanelVisibility();

        // セカンダリディスプレイに表示
        PositionOnSecondaryDisplay();

        _isInitializing = false;
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        // ウィンドウタイトルとヘッダー
        Title = loc.GetString("SettingsTitle");
        TitleText.Text = loc.GetString("SettingsTitle");

        // タブヘッダー
        TabIMEIndicator.Header = loc.GetString("TabIMEIndicator");
        TabClock.Header = loc.GetString("TabClock");
        TabMouse.Header = loc.GetString("TabMouse");
        TabAbout.Header = loc.GetString("TabAbout");

        // IMEインジケータータブ
        IMEIndicatorVisibleCheck.Content = loc.GetString("LabelVisible");
        LblIMESize.Text = loc.GetString("LabelSize");
        LblIMEOpacity.Text = loc.GetString("LabelOpacity");
        LblIMEPositionPreset.Text = loc.GetString("LabelPositionPreset");
        BtnIMETopLeft.Content = loc.GetString("BtnTopLeft");
        BtnIMETopRight.Content = loc.GetString("BtnTopRight");
        BtnIMEBottomLeft.Content = loc.GetString("BtnBottomLeft");
        BtnIMEBottomRight.Content = loc.GetString("BtnBottomRight");
        LblIMEPositionManual.Text = loc.GetString("LabelPositionManual");
        LblIMEDisplay.Text = loc.GetString("LabelDisplay");
        LblLanguageColors.Text = loc.GetString("LabelLanguageColors");
        LblGlobalFont.Text = loc.GetString("GlobalFont");
        LblGlobalFontSize.Text = loc.GetString("FontSize");
        LblColLang.Text = loc.GetString("Language");
        LblColFont.Text = loc.GetString("Font");
        LblColColor.Text = loc.GetString("Color");
        LblColText.Text = loc.GetString("Text");
        LblPixelVerification.Text = loc.GetString("LabelPixelVerification");
        LblPixelVerificationHint.Text = loc.GetString("LabelPixelVerificationHint");

        // 時計タブ
        ClockVisibleCheck.Content = loc.GetString("LabelVisible");
        LblClockStyle.Text = loc.GetString("LabelStyle");
        StyleDigitalItem.Content = loc.GetString("StyleDigital");
        StyleAnalogItem.Content = loc.GetString("StyleAnalog");
        LblClockWindowSettings.Text = loc.GetString("LabelWindowSettings");
        LblClockWindowSize.Text = loc.GetString("LabelWidth");
        LblClockHeight.Text = loc.GetString("LabelHeight");
        LblAnalogClockSize.Text = loc.GetString("LabelAnalogClockSize");
        LblClockDisplaySettings.Text = loc.GetString("LabelDisplaySettings");
        LblClockColorSettings.Text = loc.GetString("LabelColorSettings");
        LblClockFont.Text = loc.GetString("LabelFont");
        LblClockFontSize.Text = loc.GetString("LabelFontSize");
        LblClockOpacity.Text = loc.GetString("LabelOpacity");
        LblClockBackgroundColor.Text = loc.GetString("LabelBackgroundColor");
        UseIMEIndicatorColorsCheck.Content = loc.GetString("LabelUseIMEIndicatorColors");
        LblClockIMEOff.Text = loc.GetString("LabelIMEOff");
        LblClockIMEOn.Text = loc.GetString("LabelIMEOn");
        LblAnalogClockColor.Text = loc.GetString("LabelAnalogClockColor");
        LblClockTextColor.Text = loc.GetString("LabelClockTextColor");
        ShowSecondsCheck.Content = loc.GetString("LabelShowSeconds");
        LblDateFormat.Text = loc.GetString("LabelDateFormat");
        DateFormatFullItem.Content = loc.GetString("FormatFull");
        DateFormatNormalItem.Content = loc.GetString("FormatNormal");
        DateFormatShortItem.Content = loc.GetString("FormatShort");
        DateFormatCustom1Item.Content = loc.GetString("FormatCustom1");
        DateFormatCustom2Item.Content = loc.GetString("FormatCustom2");
        LblDateSelectFromSamples.Text = loc.GetString("LabelSelectFromSamples");
        LblDateOrInputDirectly.Text = loc.GetString("LabelOrInputDirectly");
        LblTimeFormat.Text = loc.GetString("LabelTimeFormat");
        TimeFormatFullItem.Content = loc.GetString("FormatFull");
        TimeFormatNormalItem.Content = loc.GetString("FormatNormal");
        TimeFormatShortItem.Content = loc.GetString("FormatShort");
        TimeFormatCustom1Item.Content = loc.GetString("FormatCustom1");
        TimeFormatCustom2Item.Content = loc.GetString("FormatCustom2");
        LblTimeSelectFromSamples.Text = loc.GetString("LabelSelectFromSamples");
        LblTimeOrInputDirectly.Text = loc.GetString("LabelOrInputDirectly");
        LblLayout.Text = loc.GetString("LabelLayout");
        LayoutVerticalDateFirstItem.Content = loc.GetString("LayoutVerticalDateFirst");
        LayoutVerticalTimeFirstItem.Content = loc.GetString("LayoutVerticalTimeFirst");
        LayoutHorizontalDateFirstItem.Content = loc.GetString("LayoutHorizontalDateFirst");
        LayoutHorizontalTimeFirstItem.Content = loc.GetString("LayoutHorizontalTimeFirst");
        LayoutDateOnlyItem.Content = loc.GetString("LayoutDateOnly");
        LayoutTimeOnlyItem.Content = loc.GetString("LayoutTimeOnly");
        LblClockDisplay.Text = loc.GetString("LabelDisplay");
        LblClockPositionPreset.Text = loc.GetString("LabelPositionPreset");
        BtnClockTopLeft.Content = loc.GetString("BtnTopLeft");
        BtnClockTopRight.Content = loc.GetString("BtnTopRight");
        BtnClockBottomLeft.Content = loc.GetString("BtnBottomLeft");
        BtnClockBottomRight.Content = loc.GetString("BtnBottomRight");
        LblClockPositionManual.Text = loc.GetString("LabelPositionManual");

        // マウスタブ
        MouseIndicatorVisibleCheck.Content = loc.GetString("LabelVisible");
        LblMouseDescription.Text = loc.GetString("LabelMouseDescription");
        LblMouseSize.Text = loc.GetString("LabelSize");
        LblMouseOpacity.Text = loc.GetString("LabelOpacity");
        LblMouseOffset.Text = loc.GetString("LabelPosition");

        // バージョン情報タブ
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        LblVersion.Text = $"{loc.GetString("LabelVersion")}: {version?.Major}.{version?.Minor}.{version?.Build}";
        LblAppDescription.Text = loc.GetString("AppDescription");
        LblSupportedLanguages.Text = loc.GetString("LabelSupportedLanguages");
        LblCustomize.Text = loc.GetString("LabelCustomize");
        BtnGoToIMEIndicator.Text = loc.GetString("BtnGoToIMEIndicator");
        BtnGoToClock.Text = loc.GetString("BtnGoToClock");
        BtnGoToMouse.Text = loc.GetString("BtnGoToMouse");
        BtnResetSettings.Content = loc.GetString("BtnResetSettings");
        DebugModeCheck.Content = loc.GetString("LabelDebugMode");

        // 対応言語パネルを構築
        BuildSupportedLanguagesPanel();

        // 閉じるボタン
        BtnClose.Content = loc.GetString("BtnClose");
    }

    private void LoadLanguageSettings()
    {
        // サポートされる言語をComboBoxに設定
        LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages;

        // 現在の言語を選択
        var currentLang = SettingsManagerRef?.Settings.Language ?? "";
        if (string.IsNullOrEmpty(currentLang))
        {
            // システム言語に基づいて選択
            currentLang = LocalizationService.Instance.CurrentLanguageCode;
        }

        for (int i = 0; i < LocalizationService.SupportedLanguages.Length; i++)
        {
            if (LocalizationService.SupportedLanguages[i].Code == currentLang ||
                LocalizationService.SupportedLanguages[i].Code.Split('-')[0] == currentLang.Split('-')[0])
            {
                LanguageCombo.SelectedIndex = i;
                break;
            }
        }

        // 選択されていない場合は英語（最初）を選択
        if (LanguageCombo.SelectedIndex < 0)
        {
            LanguageCombo.SelectedIndex = 0;
        }
    }

    private void LoadDisplays()
    {
        var loc = LocalizationService.Instance;
        var screens = System.Windows.Forms.Screen.AllScreens;
        var displayNames = new List<string>();
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var isPrimary = screen.Primary ? " " + loc.GetString("DisplayPrimary") : "";
            var displayName = loc.GetString("DisplayFormat", i + 1, isPrimary, screen.Bounds.Width, screen.Bounds.Height);
            displayNames.Add(displayName);
        }

        IMEIndicatorDisplayCombo.ItemsSource = displayNames;
        ClockDisplayCombo.ItemsSource = displayNames;

        // 現在の設定を反映
        var imeDisplayIndex = _viewModel.IMEIndicatorViewModel.DisplayIndex;
        var clockDisplayIndex = _viewModel.ClockViewModel.DisplayIndex;

        IMEIndicatorDisplayCombo.SelectedIndex = imeDisplayIndex < screens.Length ? imeDisplayIndex : 0;
        ClockDisplayCombo.SelectedIndex = clockDisplayIndex < screens.Length ? clockDisplayIndex : 0;
    }

    private void PositionOnSecondaryDisplay()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length > 1)
        {
            // セカンダリディスプレイ（ディスプレイ2）の中央に配置
            var secondary = screens[1];
            Left = secondary.WorkingArea.Left + (secondary.WorkingArea.Width - Width) / 2;
            Top = secondary.WorkingArea.Top + (secondary.WorkingArea.Height - Height) / 2;
        }
        else
        {
            // シングルディスプレイの場合は中央
            var primary = screens[0];
            Left = primary.WorkingArea.Left + (primary.WorkingArea.Width - Width) / 2;
            Top = primary.WorkingArea.Top + (primary.WorkingArea.Height - Height) / 2;
        }
    }

    private void LoadFonts()
    {
        var systemFonts = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(f => f)
            .ToList();

        // よく使うフォントを先頭に追加
        var fontList = CommonFonts
            .Where(f => systemFonts.Contains(f))
            .Concat(systemFonts.Where(f => !CommonFonts.Contains(f)))
            .ToList();

        ClockFontCombo.ItemsSource = fontList;

        // 現在のフォントを選択
        var clockFont = _viewModel.ClockViewModel.FontFamily.Source;
        ClockFontCombo.SelectedItem = fontList.Contains(clockFont) ? clockFont : fontList.FirstOrDefault();
    }

    private void LoadClockSettings()
    {
        var settings = _viewModel.ClockViewModel;

        // 背景色を読み込み
        ClockColorOff = ParseColor(settings.BackgroundColorIMEOff);
        ClockColorOn = ParseColor(settings.BackgroundColorIMEOn);
        ShowSeconds = settings.ShowSeconds;

        // スライダーの初期値設定（バインディングを使わずに直接設定）
        ClockWidthSlider.Value = settings.Width;
        ClockHeightSlider.Value = settings.Height;

        // ComboBoxの初期値設定
        ClockStyleCombo.SelectedIndex = settings.ClockStyle == ClockStyle.Digital ? 0 : 1;
        ShowSecondsCheck.IsChecked = ShowSeconds;

        // サンプルフォーマットのComboBox初期化
        LoadFormatSamples();

        // フォーマットプリセットとレイアウト
        DateFormatPresetCombo.SelectedIndex = (int)settings.DateFormatPreset;
        TimeFormatPresetCombo.SelectedIndex = (int)settings.TimeFormatPreset;
        LayoutCombo.SelectedIndex = (int)settings.Layout;
        UpdateCustomFormatVisibility();

        // カスタムフォーマット設定
        DateFormatBox.Text = GetCustomDateFormat(settings.DateFormatPreset);
        TimeFormatBox.Text = GetCustomTimeFormat(settings.TimeFormatPreset);

        // カラーピッカーの初期値
        ClockColorOffPicker.SelectedColor = ClockColorOff;
        ClockColorOnPicker.SelectedColor = ClockColorOn;

        // アナログ時計色を読み込み
        AnalogClockColor = ParseColor(settings.AnalogClockColorHex);
        AnalogClockColorPicker.SelectedColor = AnalogClockColor;

        // 背景色グリッドの有効/無効状態を更新
        UpdateClockBackgroundColorGridEnabled();
    }

    private void LoadFormatSamples()
    {
        var now = DateTime.Now;

        // 日付サンプル（フォーマット + 結果例）
        var dateSampleItems = DateFormatSamples.Select(f =>
        {
            try
            {
                var example = FormatWithJapaneseCalendar(now, f);
                return $"{f}  →  {example}";
            }
            catch
            {
                return f;
            }
        }).ToList();
        DateFormatSampleCombo.ItemsSource = dateSampleItems;

        // 時刻サンプル（フォーマット + 結果例）
        var timeSampleItems = TimeFormatSamples.Select(f =>
        {
            try
            {
                var example = now.ToString(f);
                return $"{f}  →  {example}";
            }
            catch
            {
                return f;
            }
        }).ToList();
        TimeFormatSampleCombo.ItemsSource = timeSampleItems;
    }

    private static string FormatWithJapaneseCalendar(DateTime dateTime, string format)
    {
        // 和暦指定子(g)が含まれている場合は和暦を使用
        if (format.Contains('g'))
        {
            var culture = new System.Globalization.CultureInfo("ja-JP");
            culture.DateTimeFormat.Calendar = new System.Globalization.JapaneseCalendar();
            return dateTime.ToString(format, culture);
        }
        return dateTime.ToString(format);
    }

    private void UpdateCustomFormatVisibility()
    {
        var datePreset = (DateTimeFormatPreset)DateFormatPresetCombo.SelectedIndex;
        var timePreset = (DateTimeFormatPreset)TimeFormatPresetCombo.SelectedIndex;

        CustomDateFormatPanel.Visibility = (datePreset == DateTimeFormatPreset.Custom1 || datePreset == DateTimeFormatPreset.Custom2)
            ? Visibility.Visible
            : Visibility.Collapsed;

        CustomTimeFormatPanel.Visibility = (timePreset == DateTimeFormatPreset.Custom1 || timePreset == DateTimeFormatPreset.Custom2)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string GetCustomDateFormat(DateTimeFormatPreset preset)
    {
        var settings = _viewModel.ClockViewModel;
        return preset switch
        {
            DateTimeFormatPreset.Custom1 => SettingsManagerRef?.Settings.Clock.CustomDateFormat1 ?? "M/d",
            DateTimeFormatPreset.Custom2 => SettingsManagerRef?.Settings.Clock.CustomDateFormat2 ?? "MM-dd",
            _ => ""
        };
    }

    private string GetCustomTimeFormat(DateTimeFormatPreset preset)
    {
        return preset switch
        {
            DateTimeFormatPreset.Custom1 => SettingsManagerRef?.Settings.Clock.CustomTimeFormat1 ?? "H:mm",
            DateTimeFormatPreset.Custom2 => SettingsManagerRef?.Settings.Clock.CustomTimeFormat2 ?? "HH:mm:ss",
            _ => ""
        };
    }

    private SettingsManager? SettingsManagerRef => _viewModel?.SettingsManager;

    private static Color ParseColor(string hex)
    {
        if (hex.StartsWith("#")) hex = hex[1..];
        if (hex.Length == 6)
        {
            return Color.FromRgb(
                Convert.ToByte(hex[0..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }
        return Colors.Blue;
    }

    private static string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        App.Instance.IsSettingsWindowOpen = false;

        // PropertyChanged購読解除
        _viewModel.IMEIndicatorViewModel.PropertyChanged -= OnIMEIndicatorViewModelPropertyChanged;
        _viewModel.ClockViewModel.PropertyChanged -= OnClockViewModelPropertyChanged;

        // 設定を自動保存
        _viewModel.SaveSettings();
    }

    private void OnIMEIndicatorViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IMEIndicatorViewModel.DisplayIndex))
        {
            // UIスレッドで更新
            Dispatcher.BeginInvoke(() =>
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var index = _viewModel.IMEIndicatorViewModel.DisplayIndex;
                if (index >= 0 && index < screens.Length && IMEIndicatorDisplayCombo.SelectedIndex != index)
                {
                    _isInitializing = true;
                    IMEIndicatorDisplayCombo.SelectedIndex = index;
                    _isInitializing = false;
                    DbgLog.Log(4, $"SettingsWindow: IMEIndicatorDisplayCombo更新 → {index}");
                }
            });
        }
    }

    private void OnClockViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClockViewModel.DisplayIndex))
        {
            // UIスレッドで更新
            Dispatcher.BeginInvoke(() =>
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var index = _viewModel.ClockViewModel.DisplayIndex;
                if (index >= 0 && index < screens.Length && ClockDisplayCombo.SelectedIndex != index)
                {
                    _isInitializing = true;
                    ClockDisplayCombo.SelectedIndex = index;
                    _isInitializing = false;
                    DbgLog.Log(4, $"SettingsWindow: ClockDisplayCombo更新 → {index}");
                }
            });
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // IMEインジケーター位置プリセット
    private void IMEIndicator_TopLeft_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetIMEIndicatorPosition(PositionPreset.TopLeft);
    }

    private void IMEIndicator_TopRight_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetIMEIndicatorPosition(PositionPreset.TopRight);
    }

    private void IMEIndicator_BottomLeft_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetIMEIndicatorPosition(PositionPreset.BottomLeft);
    }

    private void IMEIndicator_BottomRight_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetIMEIndicatorPosition(PositionPreset.BottomRight);
    }

    // 時計位置プリセット
    private void Clock_TopLeft_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetClockPosition(PositionPreset.TopLeft);
    }

    private void Clock_TopRight_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetClockPosition(PositionPreset.TopRight);
    }

    private void Clock_BottomLeft_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetClockPosition(PositionPreset.BottomLeft);
    }

    private void Clock_BottomRight_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetClockPosition(PositionPreset.BottomRight);
    }

    // 時計スタイル変更
    private void ClockStyle_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_viewModel?.ClockViewModel != null)
        {
            _viewModel.ClockViewModel.ClockStyle = ClockStyleCombo.SelectedIndex == 0
                ? ClockStyle.Digital
                : ClockStyle.Analog;
        }
    }

    // 時計幅変更
    private void ClockWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || _viewModel?.ClockViewModel == null) return;
        _viewModel.ClockViewModel.Width = e.NewValue;
    }

    // 時計高さ変更
    private void ClockHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing || _viewModel?.ClockViewModel == null) return;
        _viewModel.ClockViewModel.Height = e.NewValue;
    }

    // 秒表示切替
    private void ShowSeconds_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.ClockViewModel != null)
        {
            ShowSeconds = ShowSecondsCheck.IsChecked ?? true;
            _viewModel.ClockViewModel.ShowSeconds = ShowSeconds;
        }
    }

    // IME言語別色設定の初期化
    private void LoadIMELanguageColors()
    {
        // フォント一覧を取得
        _imeFontNames.Clear();
        _imeFontNames.Add(""); // 空 = グローバル設定を使用
        foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
        {
            _imeFontNames.Add(font.Source);
        }

        // グローバルフォントコンボを設定
        GlobalFontCombo.Items.Clear();
        foreach (var fontName in _imeFontNames.Where(f => !string.IsNullOrEmpty(f)))
        {
            GlobalFontCombo.Items.Add(new ComboBoxItem
            {
                Content = fontName,
                FontFamily = new FontFamily(fontName)
            });
        }

        // グローバル設定を読み込み
        var globalFontName = _viewModel.IMEIndicatorViewModel.FontName;
        var globalFontSizeRatio = _viewModel.IMEIndicatorViewModel.FontSizeRatio;

        // グローバルフォント選択
        for (int i = 0; i < GlobalFontCombo.Items.Count; i++)
        {
            if (GlobalFontCombo.Items[i] is ComboBoxItem item && item.Content?.ToString() == globalFontName)
            {
                GlobalFontCombo.SelectedIndex = i;
                break;
            }
        }

        // グローバルフォントサイズ選択
        foreach (ComboBoxItem item in GlobalFontSizeCombo.Items)
        {
            if (item.Tag is string tagStr && double.TryParse(tagStr, out var tag))
            {
                if (Math.Abs(tag - globalFontSizeRatio) < 0.01)
                {
                    GlobalFontSizeCombo.SelectedItem = item;
                    break;
                }
            }
        }

        // 言語別コントロールを作成
        CreateLanguageColorControls();
    }

    private void CreateLanguageColorControls()
    {
        var settings = _viewModel.IMEIndicatorViewModel.GetLanguageColors();
        LanguageColorsList.Children.Clear();
        _languageControls.Clear();

        foreach (var (key, displayName) in LanguageList)
        {
            // この言語の表示テキストを取得
            var displayText = settings.TryGetValue(key, out var langSettings) ? langSettings.DisplayText ?? "?" : "?";

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(0, 2, 0, 4)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            // 言語名
            var label = new TextBlock
            {
                Text = displayName,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            // フォント選択（表示可能なフォントのみ）
            var fontCombo = new ComboBox { FontSize = 10 };
            fontCombo.Items.Add(new ComboBoxItem { Content = "(共通)", Tag = "" });
            foreach (var fontName in _imeFontNames.Where(f => !string.IsNullOrEmpty(f)))
            {
                // この文字を表示できるフォントのみ追加
                if (CanFontDisplayCharacter(fontName, displayText))
                {
                    fontCombo.Items.Add(new ComboBoxItem
                    {
                        Content = fontName.Length > 15 ? fontName[..12] + "..." : fontName,
                        Tag = fontName,
                        FontFamily = new FontFamily(fontName),
                        ToolTip = fontName
                    });
                }
            }
            fontCombo.SelectionChanged += LanguageFont_SelectionChanged;
            Grid.SetColumn(fontCombo, 1);
            grid.Children.Add(fontCombo);

            // 色選択
            var colorPicker = new ColorPickerButton { Margin = new Thickness(5, 0, 0, 0) };
            if (settings.TryGetValue(key, out var colorSettings))
            {
                colorPicker.SelectedColor = ParseColor(colorSettings.Color ?? "#3B82F6");
            }
            colorPicker.ColorChanged += (s, c) => LanguageColor_Changed(key, c);
            Grid.SetColumn(colorPicker, 2);
            grid.Children.Add(colorPicker);

            // 表示テキスト
            var textBox = new TextBox
            {
                Text = displayText,
                Margin = new Thickness(5, 0, 0, 0),
                TextAlignment = TextAlignment.Center,
                MaxLength = 2,
                Width = 45,
                FontSize = 11
            };
            textBox.TextChanged += (s, e) => LanguageText_Changed(key, textBox.Text);
            Grid.SetColumn(textBox, 3);
            grid.Children.Add(textBox);

            // 既存設定を反映
            if (settings.TryGetValue(key, out var existingSettings))
            {
                // フォント
                if (!string.IsNullOrEmpty(existingSettings.FontName))
                {
                    for (int i = 0; i < fontCombo.Items.Count; i++)
                    {
                        if (fontCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == existingSettings.FontName)
                        {
                            fontCombo.SelectedIndex = i;
                            break;
                        }
                    }
                }
                else
                {
                    fontCombo.SelectedIndex = 0;
                }
            }
            else
            {
                fontCombo.SelectedIndex = 0;
            }

            _languageControls[key] = new LanguageControlSet
            {
                FontCombo = fontCombo,
                ColorPicker = colorPicker,
                TextBox = textBox
            };

            border.Child = grid;
            LanguageColorsList.Children.Add(border);
        }
    }

    // グローバルフォントサイズ変更
    private void GlobalFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (GlobalFontSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
        {
            if (double.TryParse(tagStr, out var ratio))
            {
                _viewModel.IMEIndicatorViewModel.FontSizeRatio = ratio;
                _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
            }
        }
    }

    // 言語別フォント変更
    private void LanguageFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        SaveLanguageColorSettings();
    }

    // 言語別色変更
    private void LanguageColor_Changed(string languageKey, Color color)
    {
        if (_isInitializing) return;
        SaveLanguageColorSettings();
    }

    // 言語別テキスト変更
    private void LanguageText_Changed(string languageKey, string text)
    {
        if (_isInitializing) return;
        SaveLanguageColorSettings();
    }

    // 言語別色設定を保存
    private void SaveLanguageColorSettings()
    {
        // グローバル設定を保存
        if (GlobalFontCombo.SelectedItem is ComboBoxItem globalFontItem)
        {
            var globalFontName = globalFontItem.Content?.ToString() ?? "";
            _viewModel.IMEIndicatorViewModel.FontName = globalFontName;
        }

        // 言語別設定を保存
        foreach (var (key, controls) in _languageControls)
        {
            var color = controls.ColorPicker.SelectedColor;
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            var displayText = controls.TextBox.Text;

            var fontName = "";
            if (controls.FontCombo.SelectedItem is ComboBoxItem fontItem)
            {
                fontName = fontItem.Tag?.ToString() ?? "";
            }

            _viewModel.IMEIndicatorViewModel.SetLanguageColorFull(key, hex, displayText, fontName, 0);
        }

        // マウスカーソルインジケーターにも設定を反映
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();
    }

    // 設定をリセット
    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;

        var result = MessageBox.Show(
            loc.GetString("MsgResetConfirm"),
            loc.GetString("MsgResetTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.ResetToDefaults();
            LoadFonts();
            LoadClockSettings();
            MessageBox.Show(loc.GetString("MsgResetComplete"), loc.GetString("MsgComplete"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // 時計のフォント変更
    private void ClockFont_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ClockFontCombo.SelectedItem is string fontName && _viewModel?.ClockViewModel != null)
        {
            _viewModel.ClockViewModel.FontFamily = new FontFamily(fontName);
        }
    }

    // 時刻フォーマット変更
    private void TimeFormat_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (_viewModel?.ClockViewModel != null && !string.IsNullOrWhiteSpace(TimeFormatBox.Text) && SettingsManagerRef != null)
        {
            var preset = _viewModel.ClockViewModel.TimeFormatPreset;
            if (preset == DateTimeFormatPreset.Custom1)
            {
                SettingsManagerRef!.Settings.Clock.CustomTimeFormat1 = TimeFormatBox.Text;
            }
            else if (preset == DateTimeFormatPreset.Custom2)
            {
                SettingsManagerRef!.Settings.Clock.CustomTimeFormat2 = TimeFormatBox.Text;
            }
        }
    }

    // 日付フォーマット変更
    private void DateFormat_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (_viewModel?.ClockViewModel != null && !string.IsNullOrWhiteSpace(DateFormatBox.Text) && SettingsManagerRef != null)
        {
            var preset = _viewModel.ClockViewModel.DateFormatPreset;
            if (preset == DateTimeFormatPreset.Custom1)
            {
                SettingsManagerRef!.Settings.Clock.CustomDateFormat1 = DateFormatBox.Text;
            }
            else if (preset == DateTimeFormatPreset.Custom2)
            {
                SettingsManagerRef!.Settings.Clock.CustomDateFormat2 = DateFormatBox.Text;
            }
        }
    }

    // デバッグモード切替
    private void DebugMode_Changed(object sender, RoutedEventArgs e)
    {
        IMEMonitor.DebugMode = DebugModeCheck.IsChecked ?? false;
    }

    // デバッグ版のみ表示するパネルの表示制御
    private void UpdateDebugOnlyPanelVisibility()
    {
#if DEBUG
        DebugOnlyPanel.Visibility = Visibility.Visible;
#else
        DebugOnlyPanel.Visibility = Visibility.Collapsed;
#endif
    }

    // 時計背景色（IME OFF）変更
    // IMEインジケータ言語別色使用切替
    private void UseIMEIndicatorColors_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        UpdateClockBackgroundColorGridEnabled();
    }

    private void UpdateClockBackgroundColorGridEnabled()
    {
        var isEnabled = !(_viewModel?.ClockViewModel?.UseIMEIndicatorColors ?? false);
        ClockBackgroundColorGrid.IsEnabled = isEnabled;
        ClockBackgroundColorGrid.Opacity = isEnabled ? 1.0 : 0.5;
    }

    // 時計背景色（IME OFF）変更
    private void ClockColorOff_Changed(object? sender, Color e)
    {
        ClockColorOff = e;
        if (!_isInitializing && _viewModel?.ClockViewModel != null)
        {
            _viewModel.ClockViewModel.BackgroundColorIMEOff = ColorToHex(e);
            _viewModel.ClockViewModel.ReloadSettings();
        }
    }

    // 時計背景色（IME ON）変更
    private void ClockColorOn_Changed(object? sender, Color e)
    {
        ClockColorOn = e;
        if (!_isInitializing && _viewModel?.ClockViewModel != null)
        {
            _viewModel.ClockViewModel.BackgroundColorIMEOn = ColorToHex(e);
            _viewModel.ClockViewModel.ReloadSettings();
        }
    }

    // アナログ時計色変更
    private void AnalogClockColor_Changed(object? sender, Color e)
    {
        AnalogClockColor = e;
        if (!_isInitializing && _viewModel?.ClockViewModel != null)
        {
            _viewModel.ClockViewModel.AnalogClockColorHex = ColorToHex(e);
        }
    }

    // 時計テキスト色変更
    private void ClockTextColor_Changed(object? sender, Color e)
    {
        if (!_isInitializing && _viewModel?.ClockViewModel != null)
        {
            _viewModel.ClockViewModel.TextColorHex = ColorToHex(e);
        }
    }

    // IMEインジケーター表示ディスプレイ変更
    private void IMEIndicatorDisplay_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (_viewModel?.IMEIndicatorViewModel != null && IMEIndicatorDisplayCombo.SelectedIndex >= 0)
        {
            var newIndex = IMEIndicatorDisplayCombo.SelectedIndex;
            var currentIndex = _viewModel.IMEIndicatorViewModel.DisplayIndex;

            // 現在と違うディスプレイが選択された場合、そのディスプレイに移動（デフォルト: 左上）
            if (newIndex != currentIndex)
            {
                _viewModel.MoveIMEIndicatorToDisplay(newIndex);
            }
        }
    }

    // 時計表示ディスプレイ変更
    private void ClockDisplay_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (_viewModel?.ClockViewModel != null && ClockDisplayCombo.SelectedIndex >= 0)
        {
            var newIndex = ClockDisplayCombo.SelectedIndex;
            var currentIndex = _viewModel.ClockViewModel.DisplayIndex;

            // 現在と違うディスプレイが選択された場合、そのディスプレイに移動（デフォルト: 右上）
            if (newIndex != currentIndex)
            {
                _viewModel.MoveClockToDisplay(newIndex);
            }
        }
    }

    // 日付フォーマットプリセット変更
    private void DateFormatPreset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (_viewModel?.ClockViewModel != null && DateFormatPresetCombo.SelectedIndex >= 0)
        {
            var preset = (DateTimeFormatPreset)DateFormatPresetCombo.SelectedIndex;
            _viewModel.ClockViewModel.DateFormatPreset = preset;
            DateFormatBox.Text = GetCustomDateFormat(preset);
            UpdateCustomFormatVisibility();
        }
    }

    // 時刻フォーマットプリセット変更
    private void TimeFormatPreset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (_viewModel?.ClockViewModel != null && TimeFormatPresetCombo.SelectedIndex >= 0)
        {
            var preset = (DateTimeFormatPreset)TimeFormatPresetCombo.SelectedIndex;
            _viewModel.ClockViewModel.TimeFormatPreset = preset;
            TimeFormatBox.Text = GetCustomTimeFormat(preset);
            UpdateCustomFormatVisibility();
        }
    }

    // レイアウト変更
    private void Layout_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_viewModel?.ClockViewModel != null && LayoutCombo.SelectedIndex >= 0)
        {
            _viewModel.ClockViewModel.Layout = (DateTimeLayout)LayoutCombo.SelectedIndex;
        }
    }

    // 日付フォーマットサンプル選択
    private void DateFormatSample_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (DateFormatSampleCombo.SelectedIndex >= 0 && DateFormatSampleCombo.SelectedIndex < DateFormatSamples.Length)
        {
            var format = DateFormatSamples[DateFormatSampleCombo.SelectedIndex];
            DateFormatBox.Text = format;
        }
    }

    // 時刻フォーマットサンプル選択
    private void TimeFormatSample_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (TimeFormatSampleCombo.SelectedIndex >= 0 && TimeFormatSampleCombo.SelectedIndex < TimeFormatSamples.Length)
        {
            var format = TimeFormatSamples[TimeFormatSampleCombo.SelectedIndex];
            TimeFormatBox.Text = format;
        }
    }

    // 言語選択変更
    private void Language_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (LanguageCombo.SelectedItem is UILanguageInfo langInfo && SettingsManagerRef != null)
        {
            SettingsManagerRef.Settings.Language = langInfo.Code;
            LocalizationService.Instance.SetLanguage(langInfo.Code);
            ApplyLocalization();
            LoadDisplays();
        }
    }

    // 表示設定の読み込み
    private void LoadVisibilitySettings()
    {
        var settings = App.Instance.SettingsManager?.Settings;
        if (settings == null) return;

        IMEIndicatorVisibleCheck.IsChecked = settings.IMEIndicator.IsVisible;
        ClockVisibleCheck.IsChecked = settings.Clock.IsVisible;
        MouseIndicatorVisibleCheck.IsChecked = settings.MouseCursorIndicator.IsVisible;

        // 定期ピクセル検証間隔
        var intervalMs = settings.IMEIndicator.PixelVerificationIntervalMs;
        for (int i = 0; i < PixelVerificationCombo.Items.Count; i++)
        {
            if (PixelVerificationCombo.Items[i] is ComboBoxItem item && item.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out var tag) && tag == intervalMs)
                {
                    PixelVerificationCombo.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    // IMEインジケーター表示切替
    private void IMEIndicatorVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        App.Instance.SetIMEIndicatorVisible(IMEIndicatorVisibleCheck.IsChecked ?? false);
    }

    // 定期ピクセル検証間隔変更
    private void PixelVerification_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (PixelVerificationCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
        {
            if (int.TryParse(tagStr, out var intervalMs) && SettingsManagerRef != null)
            {
                SettingsManagerRef.Settings.IMEIndicator.PixelVerificationIntervalMs = intervalMs;
                // IMEMonitorに即時反映
                IMEMonitor.SetPixelVerificationInterval(intervalMs);
            }
        }
    }

    // 時計表示切替
    private void ClockVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        App.Instance.SetClockVisible(ClockVisibleCheck.IsChecked ?? false);
    }

    // マウスインジケーター表示切替
    private void MouseIndicatorVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        App.Instance.SetMouseIndicatorVisible(MouseIndicatorVisibleCheck.IsChecked ?? false);
    }

    // UI Automation テストウィンドウを開く
    private void BtnUIAutomationTest_Click(object sender, RoutedEventArgs e)
    {
        var testWindow = new UIAutomationTestWindow();
        testWindow.Show();
    }

    // 対応言語パネルを構築
    private void BuildSupportedLanguagesPanel()
    {
        SupportedLanguagesPanel.Children.Clear();

        var languageColors = _viewModel.IMEIndicatorViewModel.GetLanguageColors();
        var loc = LocalizationService.Instance;

        foreach (var (key, displayName) in LanguageList)
        {
            if (!languageColors.TryGetValue(key, out var settings)) continue;

            var panel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(5, 2, 5, 2)
            };

            // 色付きの丸（インジケーター風）
            var indicator = new System.Windows.Shapes.Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(ParseColor(settings.Color ?? "#3B82F6"))
            };

            // 表示テキスト
            var textInCircle = new TextBlock
            {
                Text = settings.DisplayText ?? "?",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            // 丸の中にテキストを配置
            var grid = new Grid
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0)
            };
            grid.Children.Add(indicator);
            grid.Children.Add(textInCircle);

            // 言語名
            var langName = new TextBlock
            {
                Text = loc.GetString($"Lang{key}"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = 11
            };

            panel.Children.Add(grid);
            panel.Children.Add(langName);
            SupportedLanguagesPanel.Children.Add(panel);
        }
    }

}
