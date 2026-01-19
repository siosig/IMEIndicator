# IMEIndicatorW

Windows用 IME状態インジケーター＆デスクトップ時計アプリケーション

## 概要

IMEIndicatorWは、現在のIME（入力メソッド）の状態をデスクトップ上に視覚的に表示するWindows用アプリケーションです。macOS版 [IMEIndicatorClock](https://github.com/obott9/IMEIndicatorClock) のWindows移植版です。

日本語入力のON/OFFが分かりにくいという問題を解決し、常に入力状態を確認できます。

## 機能

### IMEインジケーター
- IMEのON/OFF状態を色分けして表示
- 24言語対応（日本語、英語、韓国語、中国語、ベトナム語、タイ語など）
- 言語別のカスタムカラー・表示テキスト設定
- サイズ・透明度の調整
- マルチディスプレイ対応

### デスクトップ時計
- デジタル/アナログ表示の切り替え
- 日付・時刻フォーマットのカスタマイズ（和暦対応）
- レイアウト選択（縦/横並び、日付のみ、時刻のみ）
- IME状態による背景色の変更
- マルチディスプレイ対応

### マウスカーソルインジケーター
- カーソル位置にIME状態を表示
- オフセット調整可能

## スクリーンショット

（準備中）

## 動作環境

- Windows 10/11
- .NET 8.0 Runtime

## インストール

1. [Releases](https://github.com/obott9/IMEIndicatorW/releases) から最新版をダウンロード
2. 任意のフォルダに展開
3. `IMEIndicatorW.exe` を実行

## ビルド

### 必要環境
- Visual Studio 2022 または VS Code
- .NET 8.0 SDK

### ビルド手順
```bash
git clone https://github.com/obott9/IMEIndicatorW.git
cd IMEIndicatorW
dotnet build
```

## 使用方法

1. アプリケーションを起動すると、システムトレイにアイコンが表示されます
2. トレイアイコンを右クリックでメニューを表示
3. 「設定...」から各種設定を変更できます

### 設定項目

| カテゴリ | 設定項目 |
|---------|---------|
| IMEインジケーター | サイズ、透明度、位置、フォント、言語別色設定、ピクセル検証間隔 |
| 時計 | スタイル、サイズ、フォント、フォーマット、レイアウト、背景色 |
| マウス | サイズ、透明度、オフセット |

## セキュリティ・プライバシーについて

### キーボードフックの使用について

本アプリケーションは、IME状態の正確な検出のために**低レベルキーボードフック**（`SetWindowsHookEx` API）を使用しています。

**なぜキーボードフックが必要か：**
- Windows標準のIME APIだけでは、一部のアプリケーション（ターミナル、ゲームなど）でIME状態を正確に取得できません
- キーボードフックにより、IME切り替えキー（半角/全角、変換キーなど）の押下を検出し、より正確な状態表示を実現しています

**安全性について：**
- キー入力の内容は**一切記録・送信しません**
- 検出するのはIME関連のキー（半角/全角、Ctrl+Space等）のみです
- インターネット通信機能はありません
- 設定データはローカル（`%AppData%\IMEIndicatorW`）にのみ保存されます
- ソースコードは公開されており、動作を確認できます

**ウイルス対策ソフトの警告について：**
キーボードフックを使用するアプリケーションは、ウイルス対策ソフトから警告を受ける場合があります。これはキーロガー等と同じ技術を使用しているためですが、本アプリケーションは悪意のある動作を行いません。ご心配な場合はソースコードをご確認ください。

## 技術スタック

- C# / .NET 8.0
- WPF (Windows Presentation Foundation)
- MVVM パターン (CommunityToolkit.Mvvm)
- Windows IME API (IMM32, TSF)
- 低レベルキーボードフック (SetWindowsHookEx)
- ピクセルベースIME検出（タスクバーのIMEアイコン解析）

## 対応言語

英語、日本語、韓国語、中国語（簡体/繁体）、ベトナム語、タイ語、ヒンディー語、ベンガル語、タミル語、テルグ語、ネパール語、シンハラ語、ミャンマー語、クメール語、ラオ語、モンゴル語、アラビア語、ペルシャ語、ヘブライ語、ウクライナ語、ロシア語、ギリシャ語

## ライセンス

MIT License

## 作者

[obott9](https://github.com/obott9)

## 謝辞

- [imel](https://github.com/na0k106ata/imel) - IME状態検出の参考
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon)

