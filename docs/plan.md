---
description: "IMEIndicator の設計。技術スタック・プロジェクト構成・主要コンポーネント・Win32 相互運用・性能方針・ビルドと配布・バージョン管理を記す。実装の構造を調べるときやビルド・リリース手順を確かめるときに読む。"
updated: 2026-11-12
keywords: [設計, アーキテクチャ, .NET 11, WinForms, ReadyToRun, 単一ファイル, Smart App Control]
---
# IMEIndicator 設計

IMEIndicator の実装構造と、その選択の理由をまとめた文書です。機能の仕様は `docs/spec.md`、利用者向けの手順は [README.md](../README.md) に書いています。記述は .NET 11・バージョン 1.5.4 の状態です。

## 技術スタック

| 技術 | 版 | 採用の理由 |
|---|---|---|
| C# | `LangVersion` は `latest`（[Directory.Build.props](../Directory.Build.props)） | 常に SDK が対応する最新の言語機能を使えます。 |
| .NET | 11（`net11.0-windows`） | Windows 専用 API を使うため、Windows 向けのターゲットフレームワークを指定しています。 |
| WinForms | `UseWindowsForms` を有効化 | トレイアイコン・設定画面・メッセージループを標準部品で実装でき、UI フレームワークとして WPF は使いません。 |
| WPF サブセット | `Microsoft.WindowsDesktop.App.WPF` を `FrameworkReference` で追加 | UI Automation の `System.Windows.Automation` と `System.Windows.Rect` の解決に必要で、`UseWPF` は XAML のビルド処理まで有効にするため設定しません。 |
| Serilog | 4.2.0（`Serilog.Sinks.File` 6.0.0、`Serilog.Enrichers.Thread` 4.0.0） | ファイルへのログ出力とスレッド ID の付与を既存のライブラリで済ませるためです。 |
| xUnit | 2.9.2（`Microsoft.NET.Test.Sdk` 17.12.0） | .NET の標準的なテストフレームワークで、`dotnet test` から実行できます。 |

## プロジェクト構成

[IMEIndicator.slnx](../IMEIndicator.slnx) が、アプリ本体とテストの 2 つのプロジェクトをまとめています。

| パス | 役割 |
|---|---|
| [src/csharp/IMEIndicator/App](../src/csharp/IMEIndicator/App) | アプリ全体の統括（`App`）、定数（`AppConstants`）、`/powertoggle` の受け渡し（`PowerToggleIpc`） |
| [src/csharp/IMEIndicator/Interop](../src/csharp/IMEIndicator/Interop) | Win32 API の宣言（`NativeMethods.*.cs`）、構造体と定数（`NativeTypes.cs`）、Core Audio の COM 宣言（`CoreAudio.cs`） |
| [src/csharp/IMEIndicator/Models](../src/csharp/IMEIndicator/Models) | 設定とルールのデータ型（`AppSettings`、`ProcessPriorityRule`、`PowerMode`、`Hotkey/` 配下のホットキー設定など） |
| [src/csharp/IMEIndicator/Services](../src/csharp/IMEIndicator/Services) | IME 状態の検出と監視、電源モード、プロセス優先度、ログ、ホットキー（`Hotkey/` 配下） |
| [src/csharp/IMEIndicator/Settings](../src/csharp/IMEIndicator/Settings) | 設定ファイルの読み書き（`SettingsManager`）と JSON 変換（`JsonConverters`、`SettingsJsonContext`） |
| [src/csharp/IMEIndicator/Views](../src/csharp/IMEIndicator/Views) | インジケーターと背景画像のウィンドウ、トレイアイコン、設定画面と編集ダイアログ |
| [src/csharp/IMEIndicator/Resources](../src/csharp/IMEIndicator/Resources) | アプリアイコン（`app.ico`、デバッグ用の `app-debug.ico`）と既定の背景画像（`ime-on-background.png`、実行ファイルに埋め込み） |
| [tests/csharp/IMEIndicator.Tests](../tests/csharp/IMEIndicator.Tests) | xUnit のテスト。`Hotkey/`・`Models/`・`Services/`・`Settings/` が本体の構成に対応し、`fixtures/` に設定ファイルの見本 JSON を置きます。 |

## 主要コンポーネント

各機能の担当クラスは次のとおりです。

| 機能 | 担当クラス | 役割 |
|---|---|---|
| 全体の統括 | [App.cs](../src/csharp/IMEIndicator/App/App.cs) の `App` | 各サービスとウィンドウを生成・接続し、表示の可否を `ApplyWindowVisibility` に集約します。 |
| IME 状態の検出 | [ImeDetector.cs](../src/csharp/IMEIndicator/Services/ImeDetector.cs) の `ImeDetector`、[PixelImeDetector.cs](../src/csharp/IMEIndicator/Services/PixelImeDetector.cs) の `PixelImeDetector` | `ImeDetector` が IMM32 経由で状態を判定します。`PixelImeDetector` は UI Automation と画面のピクセルで検証します。 |
| IME 状態の監視 | [ImeMonitor.cs](../src/csharp/IMEIndicator/Services/ImeMonitor.cs) の `ImeMonitor` | `KeyboardHook`、`WinEventHook`、`MouseTracker` からの通知を受け、30 ms のデバウンスを経て `ImeDetector` と `PixelImeDetector` で判定します。 |
| インジケーター描画 | [CursorIndicatorWindow.cs](../src/csharp/IMEIndicator/Views/CursorIndicatorWindow.cs) の `CursorIndicatorWindow`、[BackgroundImageWindow.cs](../src/csharp/IMEIndicator/Views/BackgroundImageWindow.cs) の `BackgroundImageWindow` | どちらも `LayeredWindow`（`NativeWindow` 派生の透過ウィンドウ）を継承します。マウス移動時は `SetWindowPos` だけで位置を更新し、再描画は設定・電源モード・文字・DPI の変化時に限ります。 |
| トレイ | [TrayIcon.cs](../src/csharp/IMEIndicator/Views/TrayIcon.cs) の `TrayIcon` | トレイアイコンとメニューを構築します。 |
| 設定 | [SettingsManager.cs](../src/csharp/IMEIndicator/Settings/SettingsManager.cs) の `SettingsManager` | 設定ファイルの読み書きを担い、JSON のシリアライズ設定は `SettingsJsonContext` と `JsonConverters` が定めます。編集画面は `SettingsForm`・`RuleEditForm`・`HotkeyEditForm` です。 |
| ホットキー | [HotkeyService.cs](../src/csharp/IMEIndicator/Services/Hotkey/HotkeyService.cs) の `HotkeyService` | `HookEngine`（低レベルフック）、`HotkeyManager`（エントリの管理と検索）、`CommandExecutor`（コマンドの実行）をまとめます。 |
| プロセス優先度 | [ProcessPriorityMonitor.cs](../src/csharp/IMEIndicator/Services/ProcessPriorityMonitor.cs) の `ProcessPriorityMonitor` | `System.Threading.Timer` で定期的にルールを評価し、`ProcessPriorityService` が優先度とアフィニティを変更します。前回の評価が終わる前の多重起動は `_tickInProgress` で防ぎます。E-Core の判定は `ECoreCpuInfo` が行います。 |
| 電源モード | [PowerModeService.cs](../src/csharp/IMEIndicator/Services/PowerModeService.cs) の `PowerModeService`、[PowerModeBackup.cs](../src/csharp/IMEIndicator/Services/PowerModeBackup.cs) の `PowerModeBackup` | 電源モードの取得・設定・トグルを行います。 |
| `/powertoggle` | [Program.cs](../src/csharp/IMEIndicator/Program.cs)、[PowerToggleIpc.cs](../src/csharp/IMEIndicator/App/PowerToggleIpc.cs) の `PowerToggleIpc` | 常駐中のインスタンスへ名前付きイベントでトグル要求を送り、常駐していなければその場で電源モードを切り替えます。 |

処理の流れは次のとおりです。`Program.Main` が名前付き Mutex で単一インスタンスを保証し、`App` を初期化します。`ImeMonitor` が IME 状態の変化を通知すると、`App` が `ApplyWindowVisibility` で `CursorIndicatorWindow` と `BackgroundImageWindow` の表示を切り替えます。ホットキーは `HotkeyService` が受け、`CommandExecutor` が実行します。

## Win32 相互運用

| 方式 | 対象 | 使い分け |
|---|---|---|
| P/Invoke（`LibraryImport`） | `user32`・`gdi32`・`imm32`・`kernel32`・`advapi32`・`powrprof`・`shcore`・`winmm`・`ole32` | 単純な構造体だけを渡す API です。ソース生成により、実行時のマーシャリングコードの生成を避けられます。宣言は [Interop](../src/csharp/IMEIndicator/Interop) の `NativeMethods.*.cs` に DLL ごとに分けています。ただし `ole32` の宣言は、それを使う [VolumeCommands.cs](../src/csharp/IMEIndicator/Services/Hotkey/Commands/VolumeCommands.cs) の中にあります。 |
| 実行時解決（`GetProcAddress`） | `GetSystemCpuSetInformation` | 古い環境でこの API が無いとき、型ロードの失敗が他の機能へ及ばないよう、使うクラスの中に閉じ込めます（`ECoreCpuInfo`）。 |
| ソース生成 COM 相互運用（`GeneratedComInterface`） | Core Audio の `IMMDeviceEnumerator`・`IMMDevice`・`IAudioEndpointVolume` | 音量操作に使います。宣言は [CoreAudio.cs](../src/csharp/IMEIndicator/Interop/CoreAudio.cs) にあり、vtable のスロットはメソッドの宣言順で決まります。 |
| UI Automation（`System.Windows.Automation`） | フォーカス中の要素の矩形 | `PixelImeDetector` がピクセル検証の対象範囲を得るために使います。 |

## 性能方針

常駐するアプリのため、メモリと CPU の使用を抑え、他のプロセスに影響しない設定にしています。

| 設定 | 値 | 理由 |
|---|---|---|
| GC | Workstation Concurrent GC（`ServerGarbageCollection` を `false`、`ConcurrentGarbageCollection` を `true`） | デスクトップ向けの既定の GC で、サーバー GC のようにコアとメモリを多く占有しません。 |
| ReadyToRun | `PublishReadyToRun` を `true`（win-x64） | ネイティブコードを事前に生成して起動を速めます。CPU 命令セット（AVX2 など）は JIT が実行時に検出するため、ReadyToRun でも変わらず、短縮されるのは起動時間だけです。 |
| トリミング | しない（`PublishTrimmed` を `false`） | WinForms のトリミングは COM のマーシャリングに依存する部分が非対応のためです。 |
| Native AOT | 採用しない | 同じ理由で、WinForms が Native AOT のトリミングに対応していないためです。 |
| 位置更新 | `SetWindowPos` だけ | マウス移動のたびの再描画を避けます（`CursorIndicatorWindow`）。 |
| 優先度の評価 | 再入防止付きの `Timer` | 評価が重なって他のプロセスへ負荷をかけないようにします（`ProcessPriorityMonitor`）。 |

## ビルド・テスト・配布

前提は .NET 11 SDK です。リポジトリのルートで次のコマンドを実行します。

```powershell
dotnet build IMEIndicator.slnx -c Release
dotnet test IMEIndicator.slnx -c Release
dotnet publish src/csharp/IMEIndicator/IMEIndicator.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -o dist/csharp
```

| 項目 | 内容 |
|---|---|
| 配布形態 | 自己完結（`SelfContained`）・単一ファイル（`PublishSingleFile`）・圧縮（`EnableCompressionInSingleFile`）・`win-x64`。ネイティブライブラリも単一ファイルへ含めます（`IncludeNativeLibrariesForSelfExtract`）。.NET ランタイムの事前導入は不要です。 |
| 出力先 | `dist/csharp/IMEIndicator.exe` |
| CI | [.github/workflows/build.yml](../.github/workflows/build.yml)（GitHub Actions）の `dotnet-build` ジョブが、`windows-2022` で .NET SDK `11.0.x` を使い、ビルド・テスト・公開を順に実行して `IMEIndicator.exe` を成果物として保存します。 |
| リリース | `v` で始まるタグの push で `release` ジョブが動き、`dotnet-build` の成果物を GitHub のリリースへ添付します。配布するバイナリは、このワークフローの成果物に限ります。 |
| 署名 | コード署名はしていません。Windows 11 の Smart App Control や SmartScreen が、未署名の実行ファイルの起動をブロックすることがあります。利用者向けの対処は [README.md](../README.md) の「入手」と「配布」の節にあります。 |

## バージョン管理

バージョンは [Directory.Build.props](../Directory.Build.props) の `<Version>` の 1 箇所だけで定義します。プロジェクトファイルからは参照せず、暗黙的に継承されます。`AssemblyVersion` と `FileVersion` も明示せず、SDK が `Version` から導出します。

形式は `X.Y.Z` の 3 分割です。リリースビルドのたびに Z を 1 つ増やします。リリースの自動採番が 3 分割のタグだけを認識するため、`v` を付けた git タグ（例: `v1.5.4`）と `<Version>` を一致させます。
