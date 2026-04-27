#include "SettingsManager.h"

#include "../app/AppConstants.h"
#include "../win32/UnicodeUtil.h"

#include <chrono>
#include <fstream>
#include <sstream>
#include <system_error>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <shlobj.h>

#include <nlohmann/json.hpp>
#include <spdlog/spdlog.h>
#include <wil/resource.h>

#pragma comment(lib, "shell32.lib")

namespace imeindicator::services {
namespace {

std::filesystem::path roamingAppDataDir()
{
    // SHGetKnownFolderPath(FOLDERID_RoamingAppData)
    PWSTR raw = nullptr;
    HRESULT hr = ::SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &raw);
    if (FAILED(hr) || !raw) {
        if (raw) ::CoTaskMemFree(raw);
        // フォールバック: %APPDATA% 環境変数
        wchar_t buf[MAX_PATH] = {};
        DWORD got = ::GetEnvironmentVariableW(L"APPDATA", buf, MAX_PATH);
        if (got > 0 && got < MAX_PATH) return std::filesystem::path(buf);
        return std::filesystem::current_path();
    }
    std::filesystem::path p(raw);
    ::CoTaskMemFree(raw);
    return p;
}

std::filesystem::path defaultSettingsDir()
{
    return roamingAppDataDir() /
           std::filesystem::path(app::AppConstants::AppName);
}

std::filesystem::path tempPathOf(const std::filesystem::path& target)
{
    auto p = target;
    p += L".tmp";
    return p;
}

std::wstring timestampWide()
{
    // UTC ISO-8601 風（コロンを含まないようにファイル名向け）
    SYSTEMTIME st{};
    ::GetLocalTime(&st);
    wchar_t buf[64] = {};
    ::swprintf_s(buf, L"%04u%02u%02u-%02u%02u%02u",
                 st.wYear, st.wMonth, st.wDay,
                 st.wHour, st.wMinute, st.wSecond);
    return buf;
}

} // namespace

SettingsManager::SettingsManager()
    : SettingsManager(defaultSettingsDir())
{}

SettingsManager::SettingsManager(std::filesystem::path settingsDirectory)
    : settingsDirectory_(std::move(settingsDirectory))
    , settingsFilePath_(settingsDirectory_ /
                        std::filesystem::path(app::AppConstants::SettingsFileName))
{
}

void SettingsManager::cleanupStaleTempFiles() noexcept
{
    std::error_code ec;
    std::filesystem::remove(tempPathOf(settingsFilePath_), ec);
}

void SettingsManager::createV1Backup() noexcept
{
    auto log = spdlog::get(std::string(app::AppConstants::LoggerSettings));

    auto bak = settingsFilePath_;
    bak += std::wstring(app::AppConstants::V1BackupSuffix);
    std::error_code ec;

    if (std::filesystem::exists(bak, ec)) {
        // 既にバックアップ存在 → 上書きしない（最古を保持）
        return;
    }
    std::filesystem::copy_file(settingsFilePath_, bak,
                               std::filesystem::copy_options::overwrite_existing,
                               ec);
    if (ec) {
        if (log) log->warn("settings v1 backup failed: {}", ec.message());
    } else {
        if (log) log->info("settings v1 backup created");
    }
}

void SettingsManager::renameBroken() noexcept
{
    auto log = spdlog::get(std::string(app::AppConstants::LoggerSettings));

    auto broken = settingsFilePath_;
    broken += std::wstring(app::AppConstants::BrokenSuffixPrefix);
    broken += timestampWide();

    std::error_code ec;
    std::filesystem::rename(settingsFilePath_, broken, ec);
    if (ec) {
        if (log) log->warn("settings broken rename failed: {}", ec.message());
    } else {
        if (log) log->warn("renamed broken settings file to {}",
                           win32::wideToUtf8(broken.wstring()));
    }
}

bool SettingsManager::load()
{
    lastError_.clear();
    cleanupStaleTempFiles();

    auto log = spdlog::get(std::string(app::AppConstants::LoggerSettings));

    std::error_code ec;
    if (!std::filesystem::exists(settingsFilePath_, ec)) {
        // ファイル無し → デフォルト設定で構築。初回起動扱い。
        settings_ = models::AppSettings{};
        settings_.isFirstLaunch = true;
        loadedAsV1_ = false;
        if (log) log->info("settings file not found, using defaults");
        return false;
    }

    std::ifstream ifs(settingsFilePath_, std::ios::binary);
    if (!ifs) {
        lastError_ = "failed to open settings file";
        if (log) log->error("{}", lastError_);
        settings_ = models::AppSettings{};
        return false;
    }

    nlohmann::json j;
    try {
        ifs >> j;
    } catch (const std::exception& ex) {
        lastError_ = std::string("settings parse error: ") + ex.what();
        if (log) log->error("{}", lastError_);
        // 破損ファイルをリネーム
        ifs.close();
        renameBroken();
        settings_ = models::AppSettings{};
        return false;
    }
    ifs.close();

    try {
        models::AppSettings parsed{};
        models::from_json(j, parsed);
        settings_ = std::move(parsed);
        loadedAsV1_ = (settings_.schemaVersion < 2);
        if (loadedAsV1_) {
            // 次回 save() 時に v2 へ昇格する。読み込み時点で内部値も 2 にしておく。
            settings_.schemaVersion = 2;
            createV1Backup();
            if (log) log->info("loaded v1 settings, will migrate to v2 on next save");
        } else {
            if (log) log->debug("loaded v2 settings");
        }
        return true;
    } catch (const std::exception& ex) {
        lastError_ = std::string("settings deserialize error: ") + ex.what();
        if (log) log->error("{}", lastError_);
        settings_ = models::AppSettings{};
        return false;
    }
}

bool SettingsManager::writeAtomic(const std::string& utf8Content) noexcept
{
    auto log = spdlog::get(std::string(app::AppConstants::LoggerSettings));
    std::error_code ec;
    std::filesystem::create_directories(settingsDirectory_, ec);

    const auto tmp = tempPathOf(settingsFilePath_);
    {
        std::ofstream ofs(tmp, std::ios::binary | std::ios::trunc);
        if (!ofs) {
            lastError_ = "failed to open tmp file for writing";
            if (log) log->error("{}", lastError_);
            return false;
        }
        ofs.write(utf8Content.data(),
                  static_cast<std::streamsize>(utf8Content.size()));
        if (!ofs) {
            lastError_ = "failed to write tmp file";
            if (log) log->error("{}", lastError_);
            return false;
        }
    }
    if (!::MoveFileExW(tmp.wstring().c_str(),
                        settingsFilePath_.wstring().c_str(),
                        MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        lastError_ = "MoveFileExW failed: " +
                     std::to_string(::GetLastError());
        if (log) log->error("{}", lastError_);
        return false;
    }
    return true;
}

bool SettingsManager::save()
{
    lastError_.clear();
    auto log = spdlog::get(std::string(app::AppConstants::LoggerSettings));

    settings_.schemaVersion = 2;  // 書き出し時は常に v2

    nlohmann::json j;
    try {
        models::to_json(j, settings_);
    } catch (const std::exception& ex) {
        lastError_ = std::string("serialize error: ") + ex.what();
        if (log) log->error("{}", lastError_);
        return false;
    }

    // 2 スペースインデント、改行 LF（dump は LF を使う）
    const std::string utf8Content = j.dump(2) + "\n";

    if (!writeAtomic(utf8Content)) {
        return false;
    }

    if (log) log->debug("settings saved ({} bytes)", utf8Content.size());
    loadedAsV1_ = false;
    return true;
}

bool SettingsManager::reset()
{
    settings_ = models::AppSettings{};
    return save();
}

} // namespace imeindicator::services
