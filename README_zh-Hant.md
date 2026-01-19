# IMEIndicatorClockW

[English](README.md) | [日本語](README_ja.md) | [简体中文](README_zh-Hans.md) | [한국어](README_ko.md)

一款可視化顯示 IME（輸入法編輯器）狀態並提供可自訂桌面時鐘的 Windows 工具應用程式。

這是 macOS 版本 [IMEIndicatorClock](https://github.com/obott9/IMEIndicatorClock) 的 Windows 移植版。

## 螢幕截圖

### 桌面概覽
| 輸入法開啟（日文） | 輸入法關閉（英文） |
|:-----------------:|:-----------------:|
| ![IME ON](docs/images/desktop-ime-on.png) | ![IME OFF](docs/images/desktop-ime-off.png) |

### 設定介面
| 輸入法指示器 | 時鐘 | 滑鼠游標 | 版本 |
|:----------:|:----:|:-------:|:----:|
| ![Indicator](docs/images/settings-indicator.png) | ![Clock](docs/images/settings-clock.png) | ![Cursor](docs/images/settings-cursor.png) | ![Version](docs/images/settings-version.png) |

## 願景

**我們的目標是支援全世界的輸入法。**

我們致力於幫助使用輸入法的使用者，能夠一目了然地查看當前的輸入模式。

## 功能

### 輸入法指示器
- 在螢幕上可視化顯示當前輸入法狀態
- 日文輸入：紅色圓圈顯示「あ」
- 英文輸入：藍色圓圈顯示「A」
- 可自訂位置、大小和透明度
- 支援多螢幕

### 桌面時鐘
- 支援類比和數位模式的浮動時鐘
- 支援日期顯示（含日本年號）
- 根據輸入法狀態變更背景顏色
- 可完全自訂視窗大小、字體大小和顏色

### 滑鼠游標指示器
- 在滑鼠游標附近顯示輸入法狀態
- 方便文字輸入時使用

## 語言支援

### 完整支援（輸入法檢測 + UI）
| 語言 | 輸入法檢測 | UI 翻譯 |
|------|:----------:|:-------:|
| 日文 | ✅ | ✅ |
| 英文 | ✅ | ✅ |
| 簡體中文 | ✅ | ✅ |
| 繁體中文 | ✅ | ✅ |
| 韓文 | ✅ | ✅ |

### 輸入法檢測 + 基本 UI
| 語言 | 輸入法檢測 | UI 翻譯 |
|------|:----------:|:-------:|
| 泰文 | ✅ | ✅ |
| 越南文 | ✅ | ✅ |
| 阿拉伯文 | ✅ | ✅ |
| 希伯來文 | ✅ | ✅ |
| 印地文 | ✅ | ✅ |
| 俄文 | ✅ | ✅ |
| 希臘文 | ✅ | ✅ |
| 孟加拉文 | ✅ | ✅ |
| 泰米爾文 | ✅ | ✅ |
| 泰盧固文 | ✅ | ✅ |
| 尼泊爾文 | ✅ | ✅ |
| 僧伽羅文 | ✅ | ✅ |
| 緬甸文 | ✅ | ✅ |
| 高棉文 | ✅ | ✅ |
| 寮文 | ✅ | ✅ |
| 蒙古文 | ✅ | ✅ |
| 波斯文 | ✅ | ✅ |
| 烏克蘭文 | ✅ | ✅ |

*這些語言的 UI 翻譯為機器翻譯，可能需要改進。歡迎貢獻！*

## 系統需求

- Windows 10/11
- .NET 8.0 Runtime

## 安裝

1. 從 [Releases](https://github.com/obott9/IMEIndicatorClockW/releases) 下載最新版本
2. 解壓縮到任意資料夾
3. 執行 `IMEIndicatorW.exe`

## 從原始碼建置

### 需求
- Visual Studio 2022 或 VS Code
- .NET 8.0 SDK

### 建置步驟
```bash
git clone https://github.com/obott9/IMEIndicatorClockW.git
cd IMEIndicatorClockW
dotnet build
```

## 使用方法

1. 啟動應用程式後，系統匣會出現圖示
2. 右鍵點擊匣圖示可存取設定
3. 可拖曳時鐘或指示器到您喜歡的位置

## 安全性與隱私

### 關於鍵盤鉤子的使用

本應用程式使用**低階鍵盤鉤子**（`SetWindowsHookEx` API）來準確偵測輸入法狀態。

**為什麼需要鍵盤鉤子：**
- 僅使用 Windows 標準輸入法 API 無法在某些應用程式（終端機、遊戲等）中準確取得輸入法狀態
- 鍵盤鉤子可偵測輸入法切換鍵（半形/全形、變換鍵等），實現更準確的狀態顯示

**安全性：**
- **絕不記錄或傳送**按鍵輸入內容
- 僅偵測輸入法相關按鍵（半形/全形、Ctrl+Space 等）
- 沒有網路通訊功能
- 設定資料僅儲存在本機（`%AppData%\IMEIndicatorW`）
- 原始碼公開，可供檢視

**關於防毒軟體警告：**
使用鍵盤鉤子的應用程式可能會觸發防毒軟體警告。這是因為與鍵盤記錄器使用相同的技術，但本應用程式不會執行任何惡意操作。如有疑慮，請檢視原始碼。

## 開發

本專案與 Anthropic 的 [Claude AI](https://claude.ai/) 合作開發。

Claude 協助了：
- 架構設計和程式碼實作
- 多語言本地化
- 文件和 README 建立

## 支持

如果您覺得這個應用程式有用，請考慮請我喝杯咖啡！

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/obott9)

## 貢獻

歡迎貢獻！特別是：
- 其他語言的 UI 翻譯
- 支援更多輸入法類型
- 錯誤回報和功能請求

## 授權

MIT License - 詳情請參閱 [LICENSE](LICENSE) 檔案。
