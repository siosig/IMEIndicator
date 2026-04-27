#include "PowerModeBackup.h"

#include "../app/AppConstants.h"
#include "../win32/UnicodeUtil.h"

#include <chrono>
#include <fstream>
#include <system_error>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <nlohmann/json.hpp>
#include <spdlog/spdlog.h>

namespace imeindicator::services {
namespace {

std::filesystem::path tempPathOf(const std::filesystem::path& target)
{
    auto p = target;
    p += L".tmp";
    return p;
}

bool atomicWrite(const std::filesystem::path& target, const std::string& utf8Content)
{
    const auto tmp = tempPathOf(target);

    std::error_code ec;
    std::filesystem::create_directories(target.parent_path(), ec);

    {
        std::ofstream ofs(tmp, std::ios::binary | std::ios::trunc);
        if (!ofs) return false;
        ofs.write(utf8Content.data(), static_cast<std::streamsize>(utf8Content.size()));
        if (!ofs) return false;
        ofs.flush();
    }

    if (!::MoveFileExW(tmp.wstring().c_str(), target.wstring().c_str(),
                       MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        return false;
    }
    return true;
}

int64_t nowUnixMs()
{
    using namespace std::chrono;
    return duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
}

} // namespace

PowerModeBackup::PowerModeBackup(std::filesystem::path stateDirectory)
    : stateDirectory_(std::move(stateDirectory))
    , filePath_(stateDirectory_ /
                std::filesystem::path(app::AppConstants::PowerBackupFileName))
{
}

std::optional<PowerModeBackupRecord> PowerModeBackup::tryLoad()
{
    auto log = spdlog::get(std::string(app::AppConstants::LoggerPower));

    std::error_code ec;
    if (!std::filesystem::exists(filePath_, ec)) {
        return std::nullopt;
    }

    // ifstream は内側スコープで完結させ、deleteFile() 呼び出し前に必ず close させる。
    // Windows では open 中のファイルへ std::filesystem::remove を呼ぶと共有違反で
    // 失敗するため、open/close を厳密に分離することがファイル削除の前提条件。
    nlohmann::json j;
    {
        std::ifstream ifs(filePath_, std::ios::binary);
        if (!ifs) {
            if (log) log->warn("PowerModeBackup tryLoad: failed to open file");
            return std::nullopt;
        }
        try {
            ifs >> j;
        } catch (const std::exception& ex) {
            if (log) log->warn("PowerModeBackup tryLoad: parse error: {}", ex.what());
            // ifs は continue するスコープ脱出で close。deleteFile はスコープ外で安全に呼ぶ。
        }
    }

    if (j.is_null()) {
        // パース失敗のフラグ代わり：null のままなら tryLoad はパース失敗と判断
        // （内部 try でログ済み）。
        deleteFile();
        return std::nullopt;
    }

    PowerModeBackupRecord rec;
    rec.schemaVersion = j.value("schemaVersion", 0);
    if (rec.schemaVersion != 1) {
        if (log) log->warn("PowerModeBackup tryLoad: unsupported schemaVersion={}", rec.schemaVersion);
        deleteFile();
        return std::nullopt;
    }

    auto modeStr = j.value("previousMode", std::string{});
    if (!models::tryParsePowerMode(modeStr, rec.previousMode)) {
        if (log) log->warn("PowerModeBackup tryLoad: invalid previousMode={}", modeStr);
        deleteFile();
        return std::nullopt;
    }

    rec.savedAtUnixMs = j.value<int64_t>("savedAtUnixMs", 0);
    rec.processId     = j.value<uint32_t>("processId", 0);
    return rec;
}

bool PowerModeBackup::save(const PowerModeBackupRecord& record)
{
    auto log = spdlog::get(std::string(app::AppConstants::LoggerPower));

    PowerModeBackupRecord effective = record;
    effective.schemaVersion = 1;
    if (effective.savedAtUnixMs <= 0) effective.savedAtUnixMs = nowUnixMs();
    if (effective.processId == 0) effective.processId = ::GetCurrentProcessId();

    nlohmann::json j = {
        {"schemaVersion", effective.schemaVersion},
        {"previousMode",  models::powerModeToStableString(effective.previousMode)},
        {"savedAtUnixMs", effective.savedAtUnixMs},
        {"processId",     effective.processId}
    };

    const std::string content = j.dump(2) + "\n";
    if (!atomicWrite(filePath_, content)) {
        if (log) log->warn("PowerModeBackup save: atomic write failed");
        return false;
    }
    return true;
}

void PowerModeBackup::deleteFile() noexcept
{
    std::error_code ec;
    std::filesystem::remove(filePath_, ec);
    // tmp ファイルも残っていれば削除（クラッシュ後の掃除）
    auto tmp = tempPathOf(filePath_);
    std::filesystem::remove(tmp, ec);
}

} // namespace imeindicator::services
