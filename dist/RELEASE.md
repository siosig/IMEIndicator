# IMEIndicatorClockW リリース手順

## ビルド環境

- Windows 10 / 11
- Visual Studio 2022 または .NET 8 SDK
- （macOSからはビルド不可）

## リリースビルド作成

### 1. Visual Studioでビルド

```
Build > Publish > Folder
```

構成：
- Configuration: Release
- Target Framework: net8.0-windows
- Deployment Mode: Self-contained（推奨）
- Target Runtime: win-x64

### 2. コマンドラインでビルド（代替）

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o build\publish
```

### 3. ZIPファイル作成

```powershell
# 作業ディレクトリを作成（.gitignoreに追加済み）
mkdir build\release\IMEIndicatorClockW

# ビルド成果物をコピー
Copy-Item -Recurse build\publish\* build\release\IMEIndicatorClockW\

# READMEをコピー
Copy-Item dist\README.txt build\release\IMEIndicatorClockW\
Copy-Item dist\README_EN.txt build\release\IMEIndicatorClockW\

# ZIPを作成
Compress-Archive -Path build\release\IMEIndicatorClockW -DestinationPath build\release\IMEIndicatorClockW_vX.X.X.zip
```

### 4. GitHub Releasesにアップロード

1. GitHubでタグを作成（例: `v1.0.0`）
2. Releases → Draft a new release
3. 作成したZIPファイルをアップロード
4. リリースノートを記載して公開

## ファイル構成

```
リポジトリ内（コミット対象）:
  dist/
    ├── RELEASE.md      # この手順書
    ├── README.txt      # ZIPに同梱（日本語）
    └── README_EN.txt   # ZIPに同梱（English）

作業用（.gitignoreで除外）:
  build/
    ├── publish/                        # ビルド出力
    └── release/
        └── IMEIndicatorClockW_vX.X.X.zip

GitHub Releases（最終配布先）:
  └── IMEIndicatorClockW_vX.X.X.zip
```

## ZIPファイルの内容

```
IMEIndicatorClockW_vX.X.X.zip
├── IMEIndicatorClockW.exe
├── IMEIndicatorClockW.dll
├── （その他の依存ファイル）
├── README.txt          # 日本語
└── README_EN.txt       # English
```

## README.txt 必須記載事項

同梱するREADMEには以下を必ず記載すること：

- **ソフトの概要** - アプリの利用目的・機能
- **作者への連絡先** - メールアドレス、GitHub等（作者に管理権限があること）
- **取り扱い種別** - フリーソフト/シェアウェア等
- **動作環境** - Windowsバージョン、必要なランタイム
- **インストール方法** - 手順を明記
- **アンインストール方法** - フォルダ削除のみでも必ず記載（「プログラムの追加と削除」に載らないため）

## リリースチェックリスト

- [ ] バージョン番号を更新（.csproj）
- [ ] Release構成でビルド
- [ ] Windows 10 で動作確認
- [ ] Windows 11 で動作確認
- [ ] dist/README.txt の内容が最新か確認
- [ ] GitHubにタグを作成
- [ ] GitHub Releasesに ZIPをアップロード

## 注意事項

- Self-contained でビルドすると .NET ランタイムが同梱され、ユーザー環境に依存しない
- Framework-dependent でビルドするとサイズは小さいが、ユーザーが .NET 8 をインストールする必要がある
