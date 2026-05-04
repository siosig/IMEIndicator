#pragma once

// FR-009: バージョン末尾をリリースビルドごとにインクリメント。
// 1.3.0.0 = C++ ポーティング初期版（009-port-to-cpp）。
// 1.4.0.0 = HotkeyP 機能マージ初期版（010-hotkeyp-merge、設定ファイル手書きで動作）。
// 1.4.0.1 = ホットキー設定 UI 追加（Phase 3 完成）。
// 1.4.0.2 = 設定ダイアログのプロセス優先度ルール一覧で、
//           管理者権限不足で制御不能な行を薄ピンク表示。
// 1.4.0.3 = ホットキー cmd 210（電源モード切替）が /toggle と同じ通知付き処理
//           （バックアップ・色更新・トレイバルーン）を実行するよう修正。
// 1.4.0.4 = 設定ダイアログでホットキー変更後、HotkeyService::reload を呼ぶよう修正
//           （設定保存後に新ホットキーが即時有効になる）。
// 1.4.0.5 = ロガー flush 設定を warn → info に下げ、HotkeyService にマッチ成功の
//           info ログを追加。cmd 2 Shutdown 等で強制終了されてもホットキー実行ログが
//           失われないようにする（デバッグ性向上）。
// 1.4.0.6 = HotKeyEntry::isCommand() の判定範囲を 0-120 から 0-120 + 200-299 に拡張。
//           IMEIndicator 拡張コマンド (200-299) のホットキーが、findHotkeyByKey で
//           マッチしても isCommand() が false で実行されない致命バグを修正。
//           これにより cmd 210（電源モード切替）等のホットキーが正しく発火する。
// 1.4.0.7 = Release ビルドの最適化を強化。/Ob3 (積極インライン) /Gw (global data
//           COMDAT) /arch:AVX2 を追加。配布対象 CPU が全て AVX2 対応であることが
//           前提（AVX2 非対応 CPU では起動不可）。
// 1.4.0.8 = リリースビルド。
// 1.4.0.9 = プロセス優先度ルール編集ダイアログのプロセス名フィールドに
//           前方一致オートコンプリートを追加（012-process-name-autocomplete）。
//           CBS_DROPDOWN ComboBox + CBN_EDITCHANGE で最大 20 件の候補表示。
// 以降は最終要素（BUILD）を 1 ずつ上げる。
#define IMEINDICATOR_VERSION_MAJOR 1
#define IMEINDICATOR_VERSION_MINOR 4
#define IMEINDICATOR_VERSION_PATCH 0
#define IMEINDICATOR_VERSION_BUILD 9

#define IMEINDICATOR_VERSION_NUM  IMEINDICATOR_VERSION_MAJOR, \
                                  IMEINDICATOR_VERSION_MINOR, \
                                  IMEINDICATOR_VERSION_PATCH, \
                                  IMEINDICATOR_VERSION_BUILD

#define IMEINDICATOR_STRINGIFY_INNER(x) #x
#define IMEINDICATOR_STRINGIFY(x) IMEINDICATOR_STRINGIFY_INNER(x)

#define IMEINDICATOR_VERSION_STR \
    IMEINDICATOR_STRINGIFY(IMEINDICATOR_VERSION_MAJOR) "." \
    IMEINDICATOR_STRINGIFY(IMEINDICATOR_VERSION_MINOR) "." \
    IMEINDICATOR_STRINGIFY(IMEINDICATOR_VERSION_PATCH) "." \
    IMEINDICATOR_STRINGIFY(IMEINDICATOR_VERSION_BUILD)
