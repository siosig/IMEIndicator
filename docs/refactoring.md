# リファクタリング計画書

## エグゼクティブサマリー

現状の「動くがモノリシックな構造」から、**Generic Hostによる堅牢なライフサイクル管理**、**SafeHandleによる厳格なリソース保護**、および**イベント駆動による超低負荷運用**を実現するモダンアーキテクチャへの刷新を目的とする。

## 主要な発見

- **アーキテクチャの負債**: `App.xaml.cs` が神オブジェクト（Service Locator）化しており、拡張性とテスト可能性が低い
- **リソース管理のリスク**: P/Invoke で扱うアンマネージドハンドル（DC, Hooks）が `SafeHandle` で保護されておらず、リークの潜在的リスクがある
- **パフォーマンスのボトルネック**: 16ms周期のポーリング（MouseTracker）が不要なCPUウェイクアップを発生させている
- **.NET 10の親和性**: `field` キーワード、`System.Threading.Lock`、`SearchValues` などの新機能活用余地がある

## 詳細分析

### 1. ライフサイクルとDIの統合

`Microsoft.Extensions.Hosting` を導入し、WPFアプリをHosted Serviceとして管理する。これにより、設定管理（IOptions）、ロギング（ILogger）、依存注入（DI）が標準パターンで利用可能になる。

現在の `App.xaml.cs` は以下の問題を抱えている：

- `App.Instance` を介した静的プロパティの公開は典型的な Service Locator アンチパターン
- `App` クラスが設定、ViewModel、監視サービス、UIのすべての生存期間を握っている
- 各コンポーネントが `App.Instance` に依存することで結合度が極めて高い

### 2. 安全性と堅牢性の向上

`IntPtr` を直接扱うのを止め、すべて `SafeHandle` 派生クラスにラップする。また、スレッド間通信の不備（Dispatcher未経由のUI更新）を ViewModel の setter レベルで解決する。

具体的な問題：

- `IMEMonitor` が `IDisposable` を実装しているが、`App.OnExit` で `Stop()` は呼んでいるものの `Dispose()` が呼ばれていない
- `IMEStateChanged += OnIMEStateChanged` のイベント購読が解除されていない
- `PixelIMEDetector` の GDI ハンドル（DC）が `SafeHandle` でラップされておらず、例外発生時に OS リソースがリークする
- `App.OnExit` 内の `catch (Exception) { }` が終了処理中の致命的なエラーを隠蔽

### 3. パフォーマンスの最適化

`MouseTracker` をポーリングから `SetWinEventHook` によるイベント駆動へ変更し、`PixelIMEDetector` では `stackalloc` と `Span<T>` を用いたゼロアロケーション判定を実装する。

---

## リファクタリング計画

### 【P0】基盤刷新とリソース安全性の確保（最優先）

#### P0-1: Generic Host導入

- **対象ファイル**: `Program.cs`（新規）, `App.xaml.cs`
- **具体的な変更**:
  - `Host.CreateApplicationBuilder` を使用し、DIコンテナで各サービスを管理
  - `App.Instance` 静的プロパティを廃止
  - `IMEMonitor` を `IHostedService` として登録
  - ViewModel や設定クラスはコンストラクタ注入（DI）で受け取るように変更
- **期待効果**: 結合度の低下、テスト容易性の向上
- **リスク**: 中（起動順序の変化、Win32フックのスレッド親和性維持が必要）
- **工数**: M

```csharp
// Program.cs (イメージ)
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<App>();
builder.Services.AddSingleton<MainWindow>();
builder.Services.AddHostedService<ImeWorker>();
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("Settings"));

using var host = builder.Build();
host.Run();
```

#### P0-2: SafeHandle実装

- **対象ファイル**: `NativeMethods.cs`, `PixelIMEDetector.cs`, `WinEventHookManager.cs`, `KeyboardHook.cs`
- **具体的な変更**:
  - GDIハンドル用の `SafeDCHandle` を実装
  - WinEventHookハンドル用の `WinEventHookHandle` を実装
  - KeyboardHookハンドル用の `SafeHookHandle` を実装
- **期待効果**: メモリ・リソースリークの完全排除
- **リスク**: 低
- **工数**: S

```csharp
// SafeHandle 実装例
public sealed class SafeDCHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeDCHandle() : base(true) { }

    public static SafeDCHandle GetScreenDC()
    {
        var handle = new SafeDCHandle();
        handle.SetHandle(GetDC(IntPtr.Zero));
        return handle;
    }

    protected override bool ReleaseHandle() => ReleaseDC(IntPtr.Zero, handle);
}

public sealed class WinEventHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public WinEventHookHandle() : base(true) { }
    protected override bool ReleaseHandle() => UnhookWinEvent(handle);
}
```

#### P0-3: スレッド安全なUI更新

- **対象ファイル**: 各 ViewModel
- **具体的な変更**:
  - ViewModelのプロパティ変更時に `Dispatcher.CheckAccess` を確認
  - 必要に応じ `BeginInvoke` でUIスレッドにマーシャリング
- **期待効果**: スレッド間アクセス例外の防止
- **リスク**: 低
- **工数**: S

### 【P1】モダン化と信頼性の向上

#### P1-1: イベント駆動型マウス追従

- **対象ファイル**: `MouseTracker.cs`
- **具体的な変更**:
  - 16ms `PeriodicTimer` ポーリングを廃止
  - `SetWinEventHook` で `EVENT_OBJECT_LOCATIONCHANGE` を監視するイベント駆動へ移行
- **期待効果**: CPU使用率の大幅削減（アイドル時0.1%以下）
- **リスク**: 中（OS挙動への依存、`EVENT_OBJECT_LOCATIONCHANGE` の発火頻度検証が必要）
- **工数**: M

#### P1-2: アトミック設定保存

- **対象ファイル**: `SettingsManager.cs`
- **具体的な変更**:
  - 一時ファイルへ書き込み + `File.Move(overwrite: true)` による破損防止
  - `IOptionsMonitor` でリアルタイム反映（Generic Host導入後）
- **期待効果**: 設定ファイル破損リスクの解消
- **リスク**: 低
- **工数**: S

```csharp
// アトミック保存の実装例
public void Save(AppSettings settings)
{
    string tempPath = _filePath + ".tmp";
    using (var stream = File.Create(tempPath))
    {
        JsonSerializer.Serialize(stream, settings);
    }
    File.Move(tempPath, _filePath, overwrite: true);
}
```

#### P1-3: C# 14 構文適用

- **対象ファイル**: 全域
- **具体的な変更**:
  - `field` キーワードによるプロパティ簡略化（Semi-auto properties）
  - `System.Threading.Lock` オブジェクトによる排他制御（既に一部使用済み）
- **期待効果**: コード量の削減、同期処理の高速化
- **リスク**: 低
- **工数**: S

```csharp
// C# 14 field キーワードの活用例
public partial class MouseCursorIndicatorViewModel : ObservableObject
{
    public bool IsVisible
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }
}
```

### 【P2】パフォーマンス極限最適化

#### P2-1: ゼロアロケーションピクセル判定

- **対象ファイル**: `PixelIMEDetector.cs`
- **具体的な変更**:
  - `GetDIBits` の転送先を `stackalloc` した `Span<byte>` に変更
  - `SearchValues` でピクセルパターン検索（SIMD活用）
  - `NativeMemory.Alloc` によるアンマネージドバッファで GC 走査対象から除外
- **期待効果**: GC発生頻度の抑制、判定処理の高速化
- **リスク**: 中（メモリ破壊注意、unsafe コードの安全性確認が必要）
- **工数**: M

#### P2-2: NativeAOT検証

- **対象ファイル**: プロジェクトファイル
- **具体的な変更**:
  - WPFの NativeAOT コンパイルを適用し、バイナリのネイティブ化を試行
  - `Hardcodet.NotifyIcon.Wpf` や CommunityToolkit の互換性を検証
- **期待効果**: 起動時間の短縮（500ms以下）、メモリ削減
- **リスク**: 高（ライブラリ互換性、トリミング設定の調整が必要）
- **工数**: L

---

## パフォーマンス改善効果の見積もり

| 項目 | 現状（推定） | 最適化後（見積もり） | 改善率 |
|:---|:---|:---|:---|
| 起動時間 | 1.2s - 2.0s | 0.4s - 0.6s | ~70% 削減 |
| メモリ使用量 (Working Set) | 60MB - 100MB | 15MB - 25MB | ~75% 削減 |
| CPU使用率 (マウス移動時) | 1.0% - 3.0% | 0.1% - 0.3% | ~90% 削減 |
| GC発生頻度 (Gen0) | 10秒に1回 | ほぼ発生しない | ~99% 削減 |

## リスクと注意点

1. **NativeAOTの互換性**: `Hardcodet.NotifyIcon.Wpf` や CommunityToolkit が NativeAOT 完全互換でない場合、トリミング設定の調整や代替手段の検討が必要
2. **Win32フックの再入性**: `SetWindowsHookEx` や `SetWinEventHook` はメッセージループに依存する。Hosted Service（バックグラウンドスレッド）で初期化する場合、適切な `Dispatcher` スレッドとの紐付けを維持すること
3. **OSアップデートの影響**: TSF (Text Services Framework) や UI Automation の内部構造は Windows のアップデートで微細に変わる可能性があるため、PixelIMEDetector は常にフォールバック手段として維持すべき
4. **IDisposable の修正**: `App.OnExit` で `IMEMonitor.Dispose()` を呼ぶよう修正し、イベント購読の解除も確実に行うこと
5. **シングルトンの再検討**: `PixelIMEDetector` のシングルトンはマルチモニタ環境での DPI 変更やリモートデスクトップ接続時のデバイスコンテキスト無効化に対応できない可能性がある
