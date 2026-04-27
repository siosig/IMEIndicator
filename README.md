# IMEIndicator

IMEIndicator は、Windows 11 上で日本語 IME の ON 状態をマウスカーソル付近に表示する常駐アプリです。通常はタスクトレイに常駐し、設定画面から表示サイズやオフセット、プロセス優先度ルールを変更できます。

## できること

- 日本語 IME が ON のときだけ、カーソル付近にインジケーターを表示する
- 電源モードに応じてインジケーター色を切り替える
- タスクトレイから設定画面、表示切替、電源モード切替、終了を行う
- 指定したプロセスの優先度を自動で変更する
- E-Core 搭載 CPU では、対象プロセスを E-Core のみに固定する

## 対応環境

- Windows 11
- .NET 10

> [!IMPORTANT]
> 配布済みの単一 EXE が Self-contained でない場合は .NET 10 ランタイムが必要です。Self-contained 版を publish した EXE ならランタイムは不要です。

## クイックスタート

1. IMEIndicator.exe を起動します。
2. 初回起動時は設定ウィンドウが自動で開きます。
3. 日本語 IME を ON にすると、マウスカーソルの近くにインジケーターが表示されます。
4. 設定ウィンドウを閉じてもアプリは終了せず、タスクトレイに常駐し続けます。
5. 終了するときは、タスクトレイアイコンを右クリックして **終了** を選択します。

## 基本的な使い方

### 通常起動

IMEIndicator.exe を通常起動すると、以下の動作になります。

- 設定ファイルを読み込む
- IME 状態の監視を開始する
- マウスカーソル追従ウィンドウを初期化する
- タスクトレイアイコンを表示する
- 初回起動時だけ設定ウィンドウを表示する

### タスクトレイ操作

タスクトレイアイコンの操作は次の通りです。

| 操作 | 動作 |
|------|------|
| 左クリック | 設定ウィンドウを開く |
| 右クリック | コンテキストメニューを開く |

右クリックメニューでは以下を実行できます。

- **表示切替**: カーソル付近のインジケーター表示を ON/OFF する
- **電源モード**: 最適な電力効率 / バランス / 最適なパフォーマンス を切り替える
- **設定**: 設定ウィンドウを開く
- **終了**: アプリを終了する

### インジケーター表示ルール

- 日本語 IME が ON のときだけ表示されます
- IME が OFF のときは非表示になります
- 日本語以外の入力状態では表示されません
- 表示文字は既定で **あ** です

### 電源モードと色

インジケーター背景色は Windows 11 の電源モードに連動します。

| 電源モード | 色 |
|------|------|
| 最適な電力効率 | 青 |
| バランス | 赤 |
| 最適なパフォーマンス | 黄 |

## 設定ウィンドウ

設定ウィンドウには 2 つの主要タブがあります。

### インジケーター

変更できる項目は次の通りです。

- 表示の ON/OFF
- サイズ
- カーソルからの X オフセット
- カーソルからの Y オフセット
- 不透明度

### プロセス優先度

指定したプロセスに対して、自動的に優先度を適用できます。

- 監視間隔は 1 秒から 1800 秒まで設定できます
- ルールは最大 30 件まで追加できます
- プロセス名は入力補完に対応します
- 優先度は Windows の 6 段階に対応します
- E-Core 搭載 CPU では E-Core 固定を有効にできます

優先度として選べる値は次の通りです。

- Idle
- BelowNormal
- Normal
- AboveNormal
- High
- Realtime

## コマンドライン引数

### /powertoggle

次のコマンドを実行すると、Windows 11 の電源モードをトグルします。

```powershell
IMEIndicator.exe /powertoggle
```

挙動は次の通りです。

- 起動中の IMEIndicator があれば、その常駐インスタンスに切替要求を送る
- 起動中のインスタンスがなければ、その場で電源モードを切り替えて通知だけ表示して終了する
- トグル対象は **最適な電力効率** と **バランス**
- 現在が **最適なパフォーマンス** の場合は **バランス** に切り替わる

## 設定ファイル

設定ファイルは次の場所に保存されます。

```text
%APPDATA%\IMEIndicator\settings.json
```

設定は JSON 形式で保存され、アプリ終了時や設定変更時に書き込まれます。

主な項目は次の通りです。

- mouseCursorIndicator.isVisible
- mouseCursorIndicator.size
- mouseCursorIndicator.opacity
- mouseCursorIndicator.offsetX
- mouseCursorIndicator.offsetY
- imeOnText
- imeOffText
- processPriorityRules
- pollingIntervalSeconds

## ビルド

リポジトリルートで実行します。

```powershell
dotnet build IMEIndicator.sln
```

## テスト

```powershell
dotnet test IMEIndicator.sln
```

## 単一 EXE の発行例

Framework-dependent の単一 EXE:

```powershell
dotnet publish IMEIndicator/IMEIndicator.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o release
```

Self-contained の単一 EXE:

```powershell
dotnet publish IMEIndicator/IMEIndicator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release-self-contained
```

## 補足

- 設定ウィンドウはタスクバー右下付近に表示されます
- 設定ウィンドウを開けるのは 1 つだけです
- アプリはシングルインスタンス動作です
