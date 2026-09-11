// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Diagnostics;

namespace IMEIndicator.Services.Hotkey;

/// <summary>
/// 内部コマンド 1 件分のカタログエントリ。
/// 根拠: specs/014-port-to-csharp/contracts/internal-command-catalog.md「実行経路を持つ 108 種」。
/// </summary>
/// <param name="Id">コマンド ID（0〜120 = HotkeyP 由来、200〜299 = IMEIndicator 拡張。現行 C++ 版と同一）。</param>
/// <param name="Category">分類。カタログ文書の見出し文字列をそのまま使う（C# 側 Commands/*.cs への実装振り分けに用いる。
/// UI 表示には使わない。現行 C++ 版の分類と表示名接頭辞は必ずしも一致しない。例: ID 6 は Category="システム"
/// （SystemCommands.cpp が実装）だが Label="電源: スクリーンセーバー起動"（HotkeyP 由来の表示上の接頭辞））。</param>
/// <param name="Label">設定画面の一覧に表示するラベル。カタログ文書の「表示名」列の値をそのまま使う。
/// 現行 C++ 版 kHotkeyCommandList と同じ、それ単体で完結した表示文字列（一部のみ独自の接頭辞を持つ。
/// <see cref="LabelFor"/> はこれをそのまま返し、Category を連結しない）。</param>
public readonly record struct CommandCatalogEntry(int Id, string Category, string Label);

/// <summary>
/// 内部コマンドカタログ（実行経路を持つ 108 種の ID → 分類 → 表示名の対応表）。
///
/// 根拠:
/// - specs/014-port-to-csharp/contracts/internal-command-catalog.md「実行経路を持つ 108 種」
///   （電源8・音量7・メディア8・ウィンドウ18・画面端配置8・マウス9・プロセス8・
///   テキスト・マクロ5・ディスプレイ2・システム21・仮想デスクトップ4・IMEIndicator 拡張10 = 合計108）
/// - specs/014-port-to-csharp/data-model.md §4「内部コマンド」
///
/// 設定画面のコマンド一覧はこのカタログ（<see cref="All"/>）から生成する（C# 側の契約）。
/// ID の範囲・意味・表示名は現行 C++ 版と同一にし、移植では変更しない。表示名と実際の動作が
/// 食い違う項目（カタログ文書で ⚠ が付いている 41 件）があっても、本カタログの表示名は
/// カタログ文書の記載どおりとする（是正は本フィーチャーの範囲外）。
/// </summary>
public static class CommandCatalog
{
    /// <summary>
    /// 実行経路を持つ 108 件。順序はカタログ文書の分類・記載順。
    /// </summary>
    public static readonly IReadOnlyList<CommandCatalogEntry> All = new List<CommandCatalogEntry>
    {
        // === 電源（8） ===
        new(2, "電源", "電源: シャットダウン"),
        new(3, "電源", "電源: 再起動"),
        new(4, "電源", "電源: スリープ"),
        new(5, "電源", "電源: ログオフ"),
        new(19, "電源", "電源: モニター電源オフ"),
        new(38, "電源", "電源: シャットダウンダイアログ"),
        new(63, "電源", "電源: 画面ロック"),
        new(64, "電源", "電源: 休止状態"),

        // === 音量（7） ===
        new(13, "音量", "音量: +5%"),
        new(14, "音量", "音量: -5%"),
        new(15, "音量", "Wave 音量 +"),
        new(16, "音量", "Wave 音量 −"),
        new(17, "音量", "音量: ミュート切替"),
        new(58, "音量", "Wave ミュート"),
        new(78, "音量", "音量: 絶対値設定"),

        // === メディア（8） ===
        new(0, "メディア", "CD トレイを開く"),
        new(1, "メディア", "CD トレイを閉じる"),
        new(39, "メディア", "メディア: 再生（CD）"),
        new(40, "メディア", "メディア: 次のトラック"),
        new(41, "メディア", "メディア: 停止"),
        new(42, "メディア", "メディア: 前のトラック"),
        new(100, "メディア", "CD 読み込み速度"),
        new(120, "メディア", "メディア: 再生/一時停止"),

        // === ウィンドウ（18） ===
        new(7, "ウィンドウ", "ウィンドウ: 最大化"),
        new(8, "ウィンドウ", "ウィンドウ: 最小化"),
        new(9, "ウィンドウ", "ウィンドウ: 閉じる"),
        new(10, "ウィンドウ", "ウィンドウ: 常に手前に表示（トグル）"),
        new(25, "ウィンドウ", "ウィンドウ: 非表示"),
        new(28, "ウィンドウ", "ウィンドウ: 画面中央に配置"),
        new(65, "ウィンドウ", "ウィンドウ: 他のウィンドウを最小化"),
        new(77, "ウィンドウ", "ウィンドウ: 不透明度設定"),
        new(81, "ウィンドウ", "ウィンドウ: デスクトップ表示"),
        new(83, "ウィンドウ", "テキスト: 前のタスク（Alt+Shift+Tab）"),
        new(84, "ウィンドウ", "テキスト: 次のタスク（Alt+Tab）"),
        new(90, "ウィンドウ", "ディスプレイ: ウィンドウスクリーンショット"),
        new(101, "ウィンドウ", "アプリ非表示"),
        new(102, "ウィンドウ", "アプリをトレイへ最小化"),
        new(110, "ウィンドウ", "ウィンドウ: 不透明度 +"),
        new(111, "ウィンドウ", "ウィンドウ: 不透明度 -"),
        new(112, "ウィンドウ", "ウィンドウ: すべて最大化"),
        new(115, "ウィンドウ", "ウィンドウ: トレイに最小化"),

        // === 画面端配置（8） ===
        // カタログ文書の表示名列をそのまま使う（29 のみ「画面端に配置: 」の接頭辞付き、
        // 30〜36 は方向名のみ。文書の記載どおりで統一はしない）。
        new(29, "画面端配置", "画面端に配置: 左下"),
        new(30, "画面端配置", "下"),
        new(31, "画面端配置", "右下"),
        new(32, "画面端配置", "左"),
        new(33, "画面端配置", "右"),
        new(34, "画面端配置", "左上"),
        new(35, "画面端配置", "上"),
        new(36, "画面端配置", "右上"),

        // === マウス（9） ===
        new(37, "マウス", "マウス移動"),
        new(43, "マウス", "マウス: 左クリック"),
        new(44, "マウス", "マウス: 中クリック"),
        new(45, "マウス", "マウス: 右クリック"),
        new(75, "マウス", "マウス: ホイールスクロール"),
        new(76, "マウス", "マウス: ダブルクリック"),
        new(79, "マウス", "第 4 ボタンクリック"),
        new(80, "マウス", "第 5 ボタンクリック"),
        new(108, "マウス", "水平ホイール"),

        // === プロセス（8） ===
        new(11, "プロセス", "プロセス: フォアグラウンドプロセス強制終了"),
        new(20, "プロセス", "プロセス優先度設定"),
        new(46, "プロセス", "プロセス: 優先度 Idle"),
        new(47, "プロセス", "プロセス: 優先度 Normal"),
        new(48, "プロセス", "プロセス: 優先度 High"),
        new(49, "プロセス", "プロセス: 優先度 Realtime"),
        new(59, "プロセス", "プロセス: 優先度 BelowNormal"),
        new(60, "プロセス", "プロセス: 優先度 AboveNormal"),

        // === テキスト・マクロ（5） ===
        new(26, "テキスト・マクロ", "アクティブウィンドウにキー送信"),
        new(27, "テキスト・マクロ", "テキスト: マクロ実行"),
        new(67, "テキスト・マクロ", "テキスト: テキスト貼付け"),
        new(74, "テキスト・マクロ", "アクティブウィンドウにキー文字列送信"),
        new(94, "テキスト・マクロ", "アクティブウィンドウにマクロ送信"),

        // === ディスプレイ（2） ===
        new(18, "ディスプレイ", "ディスプレイ: 解像度切替"),
        new(23, "ディスプレイ", "ランダム壁紙"),

        // === システム（21） ===
        new(6, "システム", "電源: スクリーンセーバー起動"),
        new(12, "システム", "システム: コントロールパネル"),
        new(22, "システム", "システム: ごみ箱を空にする"),
        new(24, "システム", "ディスク空き容量表示"),
        new(61, "システム", "マルチコマンド"),
        new(66, "システム", "システム: ウィンドウ情報表示"),
        new(70, "システム", "内部コマンド一覧表示"),
        new(85, "システム", "テキスト表示ポップアップ"),
        new(86, "システム", "特定キーを無効化"),
        new(87, "システム", "テキスト: 全ホットキー無効化（トグル）"),
        new(88, "システム", "マウスショートカット無効化"),
        new(95, "システム", "ジョイスティック無効化"),
        new(96, "システム", "リモコン無効化"),
        new(97, "システム", "キーボードショートカット無効化"),
        new(98, "システム", "トレイアイコン非表示"),
        new(99, "システム", "トレイアイコン復元"),
        new(104, "システム", "システム: 最近のドキュメントをクリア"),
        new(105, "システム", "システム: 一時ファイル削除"),
        new(109, "システム", "ドライブを安全に取り外す"),
        new(113, "システム", "IMEIndicator 設定画面表示"),
        new(114, "システム", "フック再読み込み"),

        // === 仮想デスクトップ（4） ===
        new(116, "仮想デスクトップ", "仮想デスクトップ: 次へ"),
        new(117, "仮想デスクトップ", "仮想デスクトップ: 前へ"),
        new(118, "仮想デスクトップ", "仮想デスクトップ: 新規"),
        new(119, "仮想デスクトップ", "仮想デスクトップ: 閉じる"),

        // === IMEIndicator 拡張（10） ===
        new(200, "IMEIndicator 拡張", "[IMEIndicator] インジケーター表示切替"),
        new(201, "IMEIndicator 拡張", "[IMEIndicator] ピクセル検出 有効/無効"),
        new(202, "IMEIndicator 拡張", "[IMEIndicator] IME 設定リロード"),
        new(203, "IMEIndicator 拡張", "[IMEIndicator] 背景画像表示切替"),
        new(210, "IMEIndicator 拡張", "[IMEIndicator] 電源モード切替（バックアップ付き）"),
        new(211, "IMEIndicator 拡張", "[IMEIndicator] 高パフォーマンス電源プラン適用"),
        new(212, "IMEIndicator 拡張", "[IMEIndicator] 電源モードバックアップから復元"),
        new(220, "IMEIndicator 拡張", "[IMEIndicator] プロセス優先度ルール一時停止"),
        new(221, "IMEIndicator 拡張", "[IMEIndicator] プロセス優先度ルール再開"),
        new(222, "IMEIndicator 拡張", "[IMEIndicator] 全プロセス優先度ルール一時停止"),
    };

    /// <summary>
    /// <see cref="All"/> の ID 逆引き用インデックス。<see cref="IsImplemented"/> /
    /// <see cref="LabelFor"/> を O(1) にするために保持する（108 件を毎回線形探索しない）。
    /// <c>CommandCatalogEntry</c> は readonly record struct のため既定値の Id は 0 だが
    /// （ID 0 = 「CD トレイを開く」は実在する有効な ID）、Dictionary の TryGetValue は
    /// 「見つからない」ことと「既定値」を混同しないので、この衝突は問題にならない。
    /// </summary>
    private static readonly IReadOnlyDictionary<int, CommandCatalogEntry> ById =
        All.ToDictionary(e => e.Id, e => e);

    /// <summary>
    /// 実行経路を持たない 23 種（dispatch 未登録 7 件 + dispatch 先で未処理 16 件）。
    /// 既存設定にこれらの ID のエントリがあっても設定は保持し、一覧では
    /// <see cref="LabelFor"/> が「(未実装: ID)」を返す。実行時は CommandExecutor が
    /// InvalidCommand を返し warn ログのみで、アプリは落とさない
    /// （internal-command-catalog.md「実行経路を持たない 23 種」）。
    /// </summary>
    private static readonly IReadOnlySet<int> ExcludedIds = new HashSet<int>
    {
        21, 50, 51, 52, 53, 54, 55, 56, 57, 62, 68, 69, 71, 72, 73, 82, 89, 91, 92, 93, 103, 106, 107,
    };

    /// <summary>
    /// 型初期化時の自己整合性チェック（Debug ビルドのみ。<see cref="Debug.Assert"/> は
    /// Release では呼び出し自体が除去されるため実行時コストは無い）。
    /// 108 件ちょうど・ID 重複なし・除外 23 件との重複なし、を保証する。
    /// 同等の検証は CommandCatalogTests でも行うが、実装ミスを型初期化の時点で
    /// 早期検知できるようにする（デバッグ原則: 恒久対応は他機能に影響しない疎結合な形で）。
    /// </summary>
    static CommandCatalog()
    {
        Debug.Assert(All.Count == 108, "CommandCatalog.All は 108 件である必要がある。");
        Debug.Assert(All.Select(e => e.Id).Distinct().Count() == All.Count, "CommandCatalog.All に ID の重複がある。");
        Debug.Assert(!All.Any(e => ExcludedIds.Contains(e.Id)), "CommandCatalog.All に実行経路を持たない ID が含まれている。");
    }

    /// <summary>指定 ID が実行経路を持つ 108 種に含まれるかどうか。</summary>
    public static bool IsImplemented(int id) => ById.ContainsKey(id);

    /// <summary>
    /// 設定画面の一覧に表示するラベルを返す。カタログに存在しない ID
    /// （実行経路を持たない 23 種、または未知の ID）には「(未実装: ID)」を返す。
    /// <see cref="CommandCatalogEntry.Label"/> は現行 C++ 版と同じくそれ単体で完結した表示文字列
    /// （例:「電源: シャットダウン」「第 4 ボタンクリック」）なので、ここでは
    /// <see cref="CommandCatalogEntry.Category"/> を連結しない（連結すると
    /// 「電源: 電源: シャットダウン」のような二重接頭辞になってしまう）。
    /// </summary>
    public static string LabelFor(int id) =>
        ById.TryGetValue(id, out CommandCatalogEntry entry)
            ? entry.Label
            : $"(未実装: {id})";
}
