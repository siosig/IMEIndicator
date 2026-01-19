using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMEIndicatorClock.Models;
using IMEIndicatorClock.Services;
using IMEIndicatorClock.ViewModels;

namespace IMEIndicatorClock.Views;

public partial class LanguageColorsDialog : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<string, LanguageControlSet> _controls = new();
    private readonly List<string> _fontNames = new();

    private static readonly (string key, string displayName)[] Languages = new[]
    {
        ("English", "英語"),
        ("Japanese", "日本語"),
        ("Korean", "韓国語"),
        ("ChineseSimplified", "中国語(簡体)"),
        ("ChineseTraditional", "中国語(繁体)"),
        ("Thai", "タイ語"),
        ("Vietnamese", "ベトナム語"),
        ("Arabic", "アラビア語"),
        ("Hebrew", "ヘブライ語"),
        ("Hindi", "ヒンディー語"),
        ("Russian", "ロシア語"),
        ("Greek", "ギリシャ語"),
        ("Other", "その他")
    };

    private class LanguageControlSet
    {
        public ComboBox FontCombo { get; set; } = null!;
        public ComboBox SizeCombo { get; set; } = null!;
        public ColorPickerButton ColorPicker { get; set; } = null!;
        public TextBox TextBox { get; set; } = null!;
    }

    public LanguageColorsDialog(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        
        InitializeFontList();
        ApplyLocalization();
        LoadGlobalSettings();
        CreateLanguageControls();
    }

    private void InitializeFontList()
    {
        // フォント一覧を取得
        _fontNames.Add(""); // 空 = グローバル設定を使用
        foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
        {
            _fontNames.Add(font.Source);
        }

        // グローバルフォントコンボを設定
        GlobalFontCombo.Items.Clear();
        foreach (var fontName in _fontNames.Where(f => !string.IsNullOrEmpty(f)))
        {
            GlobalFontCombo.Items.Add(new ComboBoxItem 
            { 
                Content = fontName, 
                FontFamily = new FontFamily(fontName) 
            });
        }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        
        // タイトルと説明
        Title = loc.GetString("LanguageColors");
        LblDialogDescription.Text = loc.GetString("LanguageColorsDescription");
        
        // グローバル設定ラベル
        LblGlobalFont.Text = loc.GetString("GlobalFont");
        LblGlobalFontSize.Text = loc.GetString("FontSize");
        
        // 列ヘッダー
        LblColLang.Text = loc.GetString("Language");
        LblColFont.Text = loc.GetString("Font");
        LblColSize.Text = loc.GetString("Size");
        LblColColor.Text = loc.GetString("Color");
        LblColText.Text = loc.GetString("Text");
        
        // ボタン
        BtnOK.Content = loc.GetString("BtnOK");
        BtnCancel.Content = loc.GetString("BtnCancel");
    }

    private void LoadGlobalSettings()
    {
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
    }

    private void CreateLanguageControls()
    {
        var settings = _viewModel.IMEIndicatorViewModel.GetLanguageColors();

        foreach (var (key, displayName) in Languages)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(0, 3, 0, 5)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            // 言語名
            var label = new TextBlock
            {
                Text = displayName,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            // フォント選択
            var fontCombo = new ComboBox { FontSize = 11 };
            fontCombo.Items.Add(new ComboBoxItem { Content = "(共通)", Tag = "" });
            foreach (var fontName in _fontNames.Where(f => !string.IsNullOrEmpty(f)))
            {
                fontCombo.Items.Add(new ComboBoxItem 
                { 
                    Content = fontName.Length > 20 ? fontName[..17] + "..." : fontName, 
                    Tag = fontName,
                    FontFamily = new FontFamily(fontName),
                    ToolTip = fontName
                });
            }
            Grid.SetColumn(fontCombo, 1);
            grid.Children.Add(fontCombo);

            // フォントサイズ選択
            var sizeCombo = new ComboBox { FontSize = 11 };
            sizeCombo.Items.Add(new ComboBoxItem { Content = "(共通)", Tag = "0" });
            sizeCombo.Items.Add(new ComboBoxItem { Content = "20%", Tag = "0.2" });
            sizeCombo.Items.Add(new ComboBoxItem { Content = "30%", Tag = "0.3" });
            sizeCombo.Items.Add(new ComboBoxItem { Content = "40%", Tag = "0.4" });
            sizeCombo.Items.Add(new ComboBoxItem { Content = "50%", Tag = "0.5" });
            sizeCombo.Items.Add(new ComboBoxItem { Content = "60%", Tag = "0.6" });
            sizeCombo.Items.Add(new ComboBoxItem { Content = "70%", Tag = "0.7" });
            sizeCombo.Items.Add(new ComboBoxItem { Content = "80%", Tag = "0.8" });
            Grid.SetColumn(sizeCombo, 2);
            grid.Children.Add(sizeCombo);

            // 色選択
            var colorPicker = new ColorPickerButton { Margin = new Thickness(10, 0, 0, 0) };
            if (settings.TryGetValue(key, out var colorSettings))
            {
                colorPicker.SelectedColor = ParseColor(colorSettings.Color);
            }
            Grid.SetColumn(colorPicker, 3);
            grid.Children.Add(colorPicker);

            // 表示テキスト
            var textBox = new TextBox
            {
                Text = settings.TryGetValue(key, out var textSettings) ? textSettings.DisplayText : "?",
                Margin = new Thickness(10, 0, 0, 0),
                TextAlignment = TextAlignment.Center,
                MaxLength = 2,
                Width = 50
            };
            Grid.SetColumn(textBox, 4);
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
                    fontCombo.SelectedIndex = 0; // (共通)
                }

                // サイズ
                if (existingSettings.FontSizeRatio > 0)
                {
                    foreach (ComboBoxItem item in sizeCombo.Items)
                    {
                        if (item.Tag is string tagStr && double.TryParse(tagStr, out var tag))
                        {
                            if (Math.Abs(tag - existingSettings.FontSizeRatio) < 0.01)
                            {
                                sizeCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    sizeCombo.SelectedIndex = 0; // (共通)
                }
            }
            else
            {
                fontCombo.SelectedIndex = 0;
                sizeCombo.SelectedIndex = 0;
            }

            _controls[key] = new LanguageControlSet
            {
                FontCombo = fontCombo,
                SizeCombo = sizeCombo,
                ColorPicker = colorPicker,
                TextBox = textBox
            };
            
            border.Child = grid;
            LanguageList.Children.Add(border);
        }
    }

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
        return Colors.Gray;
    }

    private void GlobalFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // OKボタン押下時にまとめて適用するため、ここでは何もしない
    }

    private void GlobalFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // OKボタン押下時にまとめて適用するため、ここでは何もしない
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        // グローバル設定を保存
        if (GlobalFontCombo.SelectedItem is ComboBoxItem globalFontItem)
        {
            var globalFontName = globalFontItem.Content?.ToString() ?? "";
            _viewModel.IMEIndicatorViewModel.FontName = globalFontName;
        }
        if (GlobalFontSizeCombo.SelectedItem is ComboBoxItem globalSizeItem && globalSizeItem.Tag is string globalSizeTagStr)
        {
            if (double.TryParse(globalSizeTagStr, out var globalRatio))
            {
                _viewModel.IMEIndicatorViewModel.FontSizeRatio = globalRatio;
            }
        }

        // 言語別設定を保存
        foreach (var (key, controls) in _controls)
        {
            var color = controls.ColorPicker.SelectedColor;
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            var displayText = controls.TextBox.Text;
            
            // フォント設定
            var fontName = "";
            if (controls.FontCombo.SelectedItem is ComboBoxItem fontItem)
            {
                fontName = fontItem.Tag?.ToString() ?? "";
            }
            
            // サイズ設定
            double fontSizeRatio = 0;
            if (controls.SizeCombo.SelectedItem is ComboBoxItem sizeItem && sizeItem.Tag is string tagStr)
            {
                double.TryParse(tagStr, out fontSizeRatio);
            }

            _viewModel.IMEIndicatorViewModel.SetLanguageColorFull(key, hex, displayText, fontName, fontSizeRatio);
        }

        // マウスカーソルインジケーターにも設定を反映
        _viewModel.MouseCursorIndicatorViewModel.ReloadSettings();

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
