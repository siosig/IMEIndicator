# IMEIndicatorClockW リリース手順

## ビルド環境

- Windows 10 / 11
- .NET 8 SDK
- gh CLI（GitHub Releases作成用）

## ディレクトリ構成

```
IMEIndicatorClockW/
├── IMEIndicatorW/              # ソースコード
│   └── IMEIndicatorW.csproj    # バージョン番号はここ
├── dist/                       # リリース関連ファイル
│   ├── RELEASE.md              # この手順書
│   ├── README.txt              # ZIPに同梱（日本語）
│   ├── README_EN.txt           # ZIPに同梱（English）
│   ├── README_ko.txt           # ZIPに同梱（韓国語）
│   ├── README_zh-CN.txt        # ZIPに同梱（中国語簡体字）
│   ├── README_zh-TW.txt        # ZIPに同梱（中国語繁体字）
│   └── IMEIndicatorClockW_vX.X.X.zip  # ← リリースZIP出力先（.gitignore済み）
├── publish/                    # ビルド出力先（.gitignore済み）
│   └── IMEIndicatorClockW.exe
└── .gitignore                  # publish/, dist/*.zip を除外
```

## リリース手順

### 1. バージョン番号を更新

`IMEIndicatorW/IMEIndicatorW.csproj` の `<Version>` を更新してコミット。

### 2. ビルド

```bash
dotnet publish IMEIndicatorW/IMEIndicatorW.csproj -c Release -p:SelfContained=true -o publish
```

- Self-contained: .NETランタイム同梱（約73MB EXE）
- 出力先: `publish/IMEIndicatorClockW.exe`

### 3. ZIPファイル作成

`dist/` ディレクトリにZIPを作成する。

```bash
# ステージングディレクトリに必要ファイルを集める
mkdir -p /tmp/ime_zip
cp publish/IMEIndicatorClockW.exe /tmp/ime_zip/
cp dist/README*.txt /tmp/ime_zip/

# ZIP作成（PowerShell経由）
powershell.exe -Command "Compress-Archive -Force -Path 'C:\...\ime_zip\*' -DestinationPath 'C:\...\dist\IMEIndicatorClockW_vX.X.X.zip'"

# またはzipコマンド
cd /tmp/ime_zip && zip -9 ../../dist/IMEIndicatorClockW_vX.X.X.zip *
```

### 4. GitHub Releasesに公開

```bash
git push
gh release create vX.X.X dist/IMEIndicatorClockW_vX.X.X.zip --title "vX.X.X" --notes "リリースノート"
```

## ZIPファイルの内容

```
IMEIndicatorClockW_vX.X.X.zip
├── IMEIndicatorClockW.exe   # 単一EXE（Self-contained）
├── README.txt               # 日本語
├── README_EN.txt            # English
├── README_ko.txt            # 韓国語
├── README_zh-CN.txt         # 中国語（簡体字）
└── README_zh-TW.txt         # 中国語（繁体字）
```

## リリースノートのルール

末尾に必ずスポンサーセクションを含める：

```markdown
---

## ❤️ Support this project / このプロジェクトを支援する

If you find this useful, please consider sponsoring!
このツールが役に立ったら、スポンサーをご検討ください！

[![Sponsor](https://img.shields.io/badge/Sponsor-%E2%9D%A4-red?logo=github)](https://github.com/sponsors/obott9)
```

## リリースチェックリスト

- [ ] バージョン番号を更新（.csproj）→ コミット
- [ ] Release構成でビルド
- [ ] ZIPに README 5言語分が含まれているか確認
- [ ] Windows 10 / 11 で動作確認
- [ ] dist/README.txt の内容が最新か確認
- [ ] git push
- [ ] gh release create でタグ作成 + ZIPアップロード

## 注意事項

- Self-contained でビルドすると .NET ランタイムが同梱され、ユーザー環境に依存しない
- Framework-dependent でビルドするとサイズは小さいが、ユーザーが .NET 8 をインストールする必要がある
- アセンブリ名は `IMEIndicatorClockW`（開発版 `IMEIndicatorW` と異なる）
