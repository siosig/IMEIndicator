#pragma once

#include "../models/PowerMode.h"

#include <cstdint>
#include <filesystem>
#include <optional>

namespace imeindicator::services {

// 異常終了時の電源モード復元用バックアップ（FR-011）
// contracts/power-mode-backup-schema.md に厳密に従う。
struct PowerModeBackupRecord {
    int schemaVersion{1};               // 1 固定
    models::PowerMode previousMode{models::PowerMode::Balanced};
    int64_t savedAtUnixMs{0};
    uint32_t processId{0};
};

// %LOCALAPPDATA%\IMEIndicator\state\power-mode-backup.json への永続化。
// テスト時はカスタムパスを注入できるよう、ステートディレクトリを引数で受け取る。
class PowerModeBackup {
public:
    // ステートディレクトリ（power-mode-backup.json を置く先）
    explicit PowerModeBackup(std::filesystem::path stateDirectory);

    // 既存ファイルの読み込み。存在しない or 破損 or schemaVersion!=1 等は std::nullopt。
    // 不正なファイルは内部で削除される（次回起動でも再評価しない）。
    std::optional<PowerModeBackupRecord> tryLoad();

    // 書き出し（アトミック: .tmp + MoveFileExW REPLACE_EXISTING|WRITE_THROUGH）。
    // 失敗しても例外は投げず、bool で結果を返す。
    bool save(const PowerModeBackupRecord& record);

    // ファイル削除（正常終了時のクリーンアップ）。存在しなくてもエラーにしない。
    void deleteFile() noexcept;

    // 現在のバックアップファイルパス（テスト用）
    std::filesystem::path filePath() const noexcept { return filePath_; }

private:
    std::filesystem::path stateDirectory_;
    std::filesystem::path filePath_;
};

} // namespace imeindicator::services
