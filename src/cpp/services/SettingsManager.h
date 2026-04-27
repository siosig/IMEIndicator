#pragma once

#include "../models/AppSettings.h"

#include <filesystem>
#include <optional>
#include <string>

namespace imeindicator::services {

// 設定 I/O 統括。contracts/settings-schema-v1.md / v2.md に厳密準拠。
class SettingsManager {
public:
    // 本番用: %APPDATA%\IMEIndicator\ を使用
    SettingsManager();

    // テスト用: 設定ディレクトリを注入
    explicit SettingsManager(std::filesystem::path settingsDirectory);

    // 設定読み込み。失敗時は default で構築し、内部状態として保持する。
    // 戻り値: 正常読み込み成功 = true、ファイル無し/破損で default にフォールバック = false
    bool load();

    // 設定保存（アトミック書き込み）。常に v2 で書き出す。
    bool save();

    // デフォルトに戻して保存
    bool reset();

    const models::AppSettings& settings() const noexcept { return settings_; }
    models::AppSettings& mutableSettings() noexcept { return settings_; }
    void setSettings(models::AppSettings s) { settings_ = std::move(s); }

    // 直近のエラーメッセージ（UTF-8）
    const std::string& lastError() const noexcept { return lastError_; }

    std::filesystem::path settingsFilePath() const noexcept { return settingsFilePath_; }
    std::filesystem::path settingsDirectory() const noexcept { return settingsDirectory_; }

private:
    void cleanupStaleTempFiles() noexcept;
    bool writeAtomic(const std::string& utf8Content) noexcept;

    // v1（schemaVersion 欠落）として読み込んだ場合は、書き込む前に v1 バックアップを作成する
    void createV1Backup() noexcept;

    // 破損ファイルをタイムスタンプ付きでリネーム
    void renameBroken() noexcept;

    std::filesystem::path settingsDirectory_;
    std::filesystem::path settingsFilePath_;
    std::string lastError_;
    models::AppSettings settings_{};
    bool loadedAsV1_{false};
};

} // namespace imeindicator::services
