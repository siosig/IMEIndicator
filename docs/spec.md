---
description: "IMEIndicator 1.5.4 の機能仕様（インジケーター表示・タスクトレイ・電源モード・プロセス優先度・グローバルホットキー・コマンドライン引数・設定ファイル・単一インスタンス）。挙動や設定キーの値域を確かめるときに読む。"
updated: 2026-11-12
keywords: [IME, 日本語入力, 背景画像, E-Core, /powertoggle, settings.json, Mutex]
---
# IMEIndicator 機能仕様

IMEIndicator 1.5.4 の利用者から見える挙動と設定の仕様をまとめる。日本語 IME が ON の間だけマウスカーソル付近にインジケーターを表示する常駐アプリで、実装の構成は `docs/plan.md` に記す。利用手順は [README.md](../README.md) に任せる。

## 対象環境

- OS は Windows 11 の 64 ビット版。
- 配布物は .NET 11 の自己完結・単一ファイルの `IMEIndicator.exe`（`win-x64`）で、.NET ランタイムの事前導入は不要。
- 実行権限は `asInvoker`。管理者権限は要求しない（[`app.manifest`](../src/csharp/IMEIndicator/app.manifest)）。管理者権限が必要なプロセスの優先度を変える場合だけ、管理者として起動する。
- DPI 対応は Per-Monitor V2。起動時に [`Program.cs`](../src/csharp/IMEIndicator/Program.cs) が設定する。

## インジケーター表示

インジケーターは、マウスカーソルの近くに出る小さな表示である。背景画像は、画面隅に出す別の画像である。両者は独立していて、片方の設定を変えても他方は変わらない（[`App.cs`](../src/csharp/IMEIndicator/App/App.cs) の `ApplyWindowVisibility`）。

### 表示条件

| 対象 | 表示する条件 |
|---|---|
| インジケーター | 入力言語が日本語で IME が ON、かつ `mouseCursorIndicator.isVisible` が `true` |
| 背景画像 | 入力言語が日本語で IME が ON、かつ `backgroundImage.isVisible` が `true` |

- IME が OFF のとき、または日本語以外の入力状態のときは、どちらも非表示になる。
- 表示する文字は `imeOnText`（既定は「あ」、1〜8 文字）。`imeOffText`（既定は「A」）は設定として保持するだけで、描画には使わない。

### インジケーターの位置とサイズ

| 項目 | 内容 | 既定値 | 値域 |
|---|---|---|---|
| 位置 | カーソル位置からの X・Y オフセット（論理 px） | 15・15 | 制限なし。画面外にはみ出す場合は描画時に補正する |
| `size` | 一辺（論理 px） | 34 | 20〜100 |
| `opacity` | 不透明度 | 0.9 | 0.10〜1.00 |

### 電源モードによる色

インジケーターの背景色は、Windows 11 の電源モードに連動する（[`PowerMode.cs`](../src/csharp/IMEIndicator/Models/PowerMode.cs)）。電源モードの取得に失敗したときは「バランス」として扱う。

| 電源モード | 色 |
|---|---|
| 最適な電力効率 | 青（`#3B82F6`） |
| バランス | 赤（`#EF4444`） |
| 最適なパフォーマンス | 黄（`#EAB308`） |

### 背景画像

| 項目 | 内容 | 既定値 | 値域 |
|---|---|---|---|
| `size` | 一辺（論理 px）。表示先モニターの DPI に応じて拡縮する | 128 | 32〜512 |
| `opacity` | 不透明度 | 1.0 | 0.10〜1.00 |
| `imagePath` | 使用する PNG ファイルの絶対パス。前後の空白は除去する | 空（同梱の既定画像を使う） | なし |

- 画像は縦横比を保ったまま正方形に収まるように描画する。
- `imagePath` が空のとき、またはファイルを読み込めないときは、同梱の既定画像に切り替える。既定画像も読み込めなければ何も表示しない。失敗の理由はログに警告として残す。
- 未移動の場合の位置は、プライマリモニターの作業領域の右上である。右端と上端から 16 論理 px の余白を空ける（[`BackgroundImageLayout.cs`](../src/csharp/IMEIndicator/Services/BackgroundImageLayout.cs)）。
- 設定画面の「参照...」ボタンで PNG を選び、「クリア」ボタンで指定を空に戻せる。

### 背景画像のドラッグ移動

1. Ctrl キーを押している間だけ、背景画像がマウス操作を受け付ける。通常時はマウス操作を下のウィンドウへ通す。
2. Ctrl を押しながら左ボタンでドラッグすると、画像を移動できる。
3. ボタンを離した位置で確定する。モニターの作業領域からはみ出す場合は、作業領域の内側へ寄せる。
4. 確定した位置は `backgroundImage.position` に保存する。モニター識別子（`monitorId`）、基準にする隅（`anchor`。`TopLeft`・`TopRight`・`BottomLeft`・`BottomRight`）、その隅からの距離（`offsetX`・`offsetY`、論理 px、0〜100000）の組である。

- 保存した `monitorId` に一致するモニターが無いときは、プライマリモニターに表示する。このとき保存済みの位置は書き換えない。
- 設定画面の「既定の位置に戻す」ボタンで `position` を未移動（`null`）に戻せる。
- 位置の計算は [`BackgroundImagePlacement.cs`](../src/csharp/IMEIndicator/Services/BackgroundImagePlacement.cs)、ドラッグ操作は [`BackgroundImageWindow.cs`](../src/csharp/IMEIndicator/Views/BackgroundImageWindow.cs) が担当する。

## タスクトレイ

トレイアイコンの表示名は「IME Indicator」である（[`TrayIcon.cs`](../src/csharp/IMEIndicator/Views/TrayIcon.cs)）。

| 操作 | 動作 |
|---|---|
| 左クリック・左ダブルクリック | 設定ウィンドウを開く |
| 右クリック | メニューを開く |

右クリックメニューの項目は、上から次の順に並ぶ。

| 項目 | 動作 |
|---|---|
| 表示切替 | インジケーターの表示を ON/OFF する。現在の状態をチェックマークで示す |
| 背景画像表示切替 | 背景画像の表示を ON/OFF する。現在の状態をチェックマークで示す |
| 電源モード | 「最適な電力効率」「バランス」「最適なパフォーマンス」を選んで切り替える。現在のモードをチェックマークで示す |
| ホットキー | `trayMenu` を有効にしたホットキーの一覧を出し、選んだエントリを実行する。該当するエントリが 0 件のときは項目自体を隠す |
| 設定 | 設定ウィンドウを開く |
| 終了 | アプリを終了する |

- メニューから電源モードを切り替えると、「電源モード: <モード名>」のバルーン通知を出す。
- 設定ウィンドウは、初回起動時（`isFirstLaunch` が `true` のとき）に自動で開く。開いた後は `isFirstLaunch` を `false` にして保存する。
- 設定ウィンドウを閉じてもアプリは終了せず、トレイに常駐し続ける。

## 電源モード

- 対象は Windows 11 の電源モード（Power Overlay Scheme）で、「最適な電力効率」「バランス」「最適なパフォーマンス」の 3 種類（[`PowerModeService.cs`](../src/csharp/IMEIndicator/Services/PowerModeService.cs)）。
- トグル操作では「最適な電力効率」と「バランス」を交互に切り替える。現在が「最適なパフォーマンス」なら「バランス」にする。
- 切り替えの前に、現在のモードをバックアップファイルへ保存する。次の起動時にバックアップが残っていれば、そのモードへ戻して削除する。異常終了で電源モードが変わったままになるのを防ぐ。
- 正常終了時は、バックアップを削除する。
- ホットキーの内部コマンドに、電源モードのトグル（ID 210。バックアップ付き）と「最適なパフォーマンス」の適用（ID 211）がある。

## プロセス優先度

指定したプロセスの優先度を、一定間隔で監視して自動的に変更する（[`ProcessPriorityMonitor.cs`](../src/csharp/IMEIndicator/Services/ProcessPriorityMonitor.cs)）。

| 項目 | 内容 | 既定値 | 値域 |
|---|---|---|---|
| ルール数 | `processPriorityRules` の件数 | なし | 最大 30 件。超過分は読み込み時に切り捨てる |
| `pollingIntervalSeconds` | 監視間隔（秒） | 1 | 1〜1800 |
| `processName` | 対象のプロセス名。末尾の `.exe` は除いて扱う | なし | 空でないこと |
| `targetPriority` | 適用する優先度 | `Normal` | `Idle`・`BelowNormal`・`Normal`・`AboveNormal`・`High`・`Realtime` の 6 段階 |
| `isEnabled` | ルールを有効にするか | `true` | なし |
| `useECoreOnly` | 対象プロセスのアフィニティを E-Core のみに固定するか | `false` | なし |
| `maxBackoffExponent` | 適用に失敗し続けたときの指数バックオフの上限 | 6 | 0〜10 |

- E-Core の判定は、起動時に 1 回だけ CPU の効率クラスを取得して行う（[`ECoreCpuInfo.cs`](../src/csharp/IMEIndicator/Services/ECoreCpuInfo.cs)）。最も低い効率クラスの論理プロセッサを E-Core とみなす。Intel の第 12 世代以降のハイブリッド構成が対象で、E-Core が無い CPU では固定の対象にならない。
- 管理者権限が足りずに操作できないプロセスのルールは、設定ウィンドウのルール一覧で色を変えて示し、拒否回数も表示する。

## グローバルホットキー

任意のキーの組み合わせに、アプリ・URL・フォルダの起動、または内部コマンドの実行を割り当てられる。HotkeyP 4.11 由来のコアを移植したもので、設定ウィンドウの「ホットキー（最大 256 件）」グループで編集する。

### 割り当てられる動作

| 種類 | 条件 | 内容 |
|---|---|---|
| アプリ・URL・フォルダ起動 | `exe` が空でない | 実行ファイル、URL、フォルダを開く。`args`（引数）と `dir`（作業ディレクトリ）を指定できる |
| 内部コマンド | `exe` が空で、`cmd` が 0〜120 または 200〜299 | 音量、電源、ウィンドウ操作、テキスト貼り付けなどのコマンドを実行する |

- `cmd` が 0〜120 のコマンドは HotkeyP 由来、200〜299 は IMEIndicator 独自の拡張である。一覧は設定ウィンドウのホットキー編集画面で選べる（[`CommandCatalog.cs`](../src/csharp/IMEIndicator/Services/Hotkey/CommandCatalog.cs)）。
- IMEIndicator 独自のコマンドは、200（インジケーター表示切替）、201（ピクセル検出の有効・無効）、202（IME 設定のリロード）、203（背景画像表示切替）、210（電源モード切替）、211（高パフォーマンス電源プラン適用）である。

### エントリのフラグ

| キー | 内容 |
|---|---|
| `disable` | エントリを無効にする |
| `multInst` | 複数インスタンスの起動を許可する |
| `trayMenu` | タスクトレイのメニューにも表示する |
| `autoStart` | IMEIndicator の起動時に自動実行する |
| `admin` | 管理者として実行する |

### 全体のオプション

`hotkeySettings.globalOptions` に次の項目がある（[`HotkeyGlobalOptions.cs`](../src/csharp/IMEIndicator/Models/Hotkey/HotkeyGlobalOptions.cs)）。

| キー | 内容 | 値域 |
|---|---|---|
| `hookMode` | キーボードフックの方式。`None`（フックなし）、`LowLevel`（低レベルフック）、`SystemHotKey`（システムのホットキー登録）、`Auto`（自動選択。既定） | 上記の 4 種 |
| `distinguishLeftRightModifiers` | 左右の修飾キーを区別する | なし |
| `foregroundExcludeProcesses` | 前面ウィンドウの判定から除外するプロセス名 | 最大 32 件 |
| `mouseDelayMs` | マウス操作の遅延（ミリ秒） | 0〜1000 |
| `playSoundOnExecution` | 実行時に音を鳴らす | なし |

- ホットキーのエントリは最大 256 件、カテゴリは最大 32 件。超過分は読み込み時に切り捨てる。

## コマンドライン引数

引数は、先頭の 1 つだけを解釈する。解釈できる引数は `/powertoggle` で、大文字・小文字を区別しない。

```powershell
IMEIndicator.exe /powertoggle
```

| 状況 | 動作 |
|---|---|
| 常駐インスタンスがある | 名前付きイベント `IMEIndicator_PowerToggle` に切り替え要求を送って終了する。常駐側が電源モードを切り替え、バルーン通知を出す |
| 常駐インスタンスがない | その場で電源モードを切り替えて終了する。通知は出さない |

- 切り替えの規則は「電源モード」の節に従う。
- `/powertoggle` は、単一インスタンスの判定より前に処理するので、常駐中でも実行できる。

## 設定ファイル

保存場所は `%APPDATA%\IMEIndicator\settings.json` である（[`SettingsManager.cs`](../src/csharp/IMEIndicator/Settings/SettingsManager.cs)）。

- 形式は BOM なしの UTF-8 の JSON。保存は一時ファイル（`settings.json.tmp`）へ書いてから置き換える。
- `schemaVersion` の現行値は 7。1〜7 を読み込めて、書き出しは常に 7 にする。キーが無いときは 1 として読み込む。
- 旧版（1〜6）の設定を読み込んだときは、移行前の内容を `settings.json.v<N>.bak`（`v1`〜`v6`）として残す。
- 読み込み時に値域を丸める。値域は、各機能の節の表に示した。

主なキーは次のとおり。

| キー | 内容 | 既定値 |
|---|---|---|
| `mouseCursorIndicator.isVisible`・`size`・`opacity`・`offsetX`・`offsetY` | インジケーターの表示設定 | `true`・34・0.9・15・15 |
| `imeOnText`・`imeOffText` | IME ON・OFF の表示文字（1〜8 文字。空は既定値に戻す） | 「あ」・「A」 |
| `isFirstLaunch` | 初回起動か | `true` |
| `processPriorityRules` | プロセス優先度ルールの配列 | 空 |
| `pollingIntervalSeconds` | 優先度の監視間隔（秒） | 1 |
| `logLevel` | `trace`・`debug`・`info`・`warn`・`error`・`critical` | `warn` |
| `pixelVerificationIntervalMs` | ピクセルによる IME 状態の検証周期（ミリ秒。0 で無効） | 2000（値域 0〜60000） |
| `hotkeySettings` | ホットキーの配列（`hotkeys`）、カテゴリ（`categories`）、全体のオプション（`globalOptions`） | 空 |
| `backgroundImage.isVisible`・`size`・`opacity`・`imagePath`・`position` | 背景画像の設定 | `false`・128・1.0・空・`null` |

## 単一インスタンス

- 起動時に、名前付き Mutex `IMEIndicator_SingleInstance` を取得する。
- すでに別のインスタンスが常駐しているときは、2 つ目のインスタンスは何も表示せずに終了する。
- 設定ウィンドウも 1 つしか開けない。開いている間に再度開こうとすると、開いているウィンドウを前面に出す。
- `/powertoggle` で起動したプロセスは、Mutex を取得しないので、常駐インスタンスと同時に動いても多重起動の扱いにならない。
