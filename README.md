# IMEIndicator

IMEIndicator は、Windows 11 上で日本語 IME の ON 状態をマウスカーソル付近に表示する常駐アプリです。通常はタスクトレイに常駐し、設定画面から表示サイズやオフセット、プロセス優先度ルールを変更できます。

> [!IMPORTANT]
> v1.3.0 から **ネイティブ C++ 版が主実装** になりました。.NET ランタイム不要の単一 EXE（約 0.8 MB）として配布されます。旧 C#/WPF 版は `IMEIndicator/` ディレクトリ配下に参照用として残しています（[レガシー C#/WPF 版](#レガシー-cwpf-版) を参照）。

## できること

- 日本語 IME が ON のときだけ、カーソル付近にインジケーターを表示する
- 電源モードに応じてインジケーター色を切り替える
- タスクトレイから設定画面、表示切替、電源モード切替、終了を行う
- 指定したプロセスの優先度を自動で変更する
- E-Core 搭載 CPU では、対象プロセスを E-Core のみに固定する

## 対応環境

- Windows 11 64-bit
- ランタイム不要（C++ 版はスタティック CRT で単一 EXE）

> [!NOTE]
> C++ 版は .NET / WPF / WinForms ランタイム依存ゼロです。配布された `IMEIndicator.exe` を任意のフォルダに置くだけで動作します（管理者権限不要）。プロセス優先度ルールでシステムプロセスを操作する場合のみ管理者権限を推奨。

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

## ビルド（C++ 版・本実装）

### 前提

- Visual Studio 2022 (17.8 以降) もしくは Visual Studio 2026
- C++ デスクトップ開発ワークロード + Windows 11 SDK
- CMake 3.27 以降（VS 同梱で OK）
- Git submodule で vcpkg を取得（初回のみ）

### 初回セットアップ

```powershell
git submodule update --init --recursive
external\vcpkg\bootstrap-vcpkg.bat -disableMetrics
```

### Release ビルド

```powershell
cmake --preset windows-x64-release
cmake --build --preset windows-x64-release
```

成果物: `build/release/bin/Release/IMEIndicator.exe`（約 0.8 MB）

### Debug ビルド

```powershell
cmake --build --preset windows-x64-debug
```

### テスト

```powershell
ctest --preset windows-x64-release
```

GoogleTest による単体テスト 69 件が実行されます。

### バージョン管理

リリースビルドごとに [src/cpp/resources/version.h](src/cpp/resources/version.h) と
[src/cpp/CMakeLists.txt](src/cpp/CMakeLists.txt) の `IMEINDICATOR_VERSION_BUILD` を 1 ずつ
インクリメントしてください（FR-009）。

### 配布

`build/release/bin/Release/IMEIndicator.exe` をそのまま配布できます。OS 同梱 DLL のみに依存し、VC ランタイム再頒布パッケージは不要です。署名手順は [自己署名.md](自己署名.md) を参照。

## 補足

- 設定ウィンドウはタスクバー直上に表示されます
- 設定ウィンドウを開けるのは 1 つだけです
- アプリはシングルインスタンス動作です（名前付き Mutex `IMEIndicator_SingleInstance`）

## レガシー C#/WPF 版

旧実装は `IMEIndicator/` 以下に保存されており、`IMEIndicator.sln` で開けます。新規開発は C++ 版で行うため、C#/WPF 版は **メンテナンス停止** 扱いです。

```powershell
# レガシー版のビルド（参照のみ）
dotnet build IMEIndicator.sln
dotnet test IMEIndicator.sln
```

## ライセンス

本プロジェクトは **GNU General Public License v2 (or later)** で配布されます。完全なライセンス本文は [COPYING](COPYING) を参照してください。

> **注**: 本リポジトリは現在、作者個人による私的利用を目的としており、OSS としての公開・再配布は予定していません。GPL は HotkeyP（後述）由来コードを取り込んだ結果としての必然的な選択です。将来公開・再配布する場合は、依存ライブラリのライセンス互換性を再確認する必要があります。

## 謝辞 (Acknowledgments)

- **HotkeyP** by Petr Lastovicka — グローバルホットキー機能のコア実装は HotkeyP 4.11（GPL v2）から派生しています。原典: <https://hotkeyp.sourceforge.net/> / <https://github.com/plastovicka/HotkeyP>。HotkeyP 由来のソースは `src/cpp/services/hotkey/` および `src/cpp/models/hotkey/HotKeyEntry.h` に配置されており、各ファイル先頭に著作権表記とライセンスヘッダを保持しています。
