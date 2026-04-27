#include "services/PowerModeBackup.h"
#include "services/Logger.h"
#include "models/PowerMode.h"

#include <filesystem>
#include <fstream>

#include <gtest/gtest.h>

namespace fs = std::filesystem;
using namespace imeindicator;

namespace {

class TempDir {
public:
    TempDir()
    {
        path_ = fs::temp_directory_path() / fs::path(L"imeindicator_pwbk_") /
                fs::path(std::to_wstring(::GetCurrentProcessId()) + L"_" +
                          std::to_wstring(reinterpret_cast<uintptr_t>(this)));
        fs::create_directories(path_);
    }
    ~TempDir() { std::error_code ec; fs::remove_all(path_, ec); }
    const fs::path& path() const noexcept { return path_; }
private:
    fs::path path_;
};

class BackupFixture : public ::testing::Test {
protected:
    void SetUp() override
    {
        if (!services::Logger::isInitialized()) {
            services::Logger::init(tmp_.path() / L"logs", models::LogLevel::Critical);
        }
    }
    TempDir tmp_;
};

} // namespace

TEST_F(BackupFixture, RoundTripSaveAndLoad)
{
    services::PowerModeBackup b(tmp_.path());
    services::PowerModeBackupRecord rec;
    rec.previousMode = models::PowerMode::BestPerformance;
    rec.savedAtUnixMs = 1745740800000;
    rec.processId = 12345;
    EXPECT_TRUE(b.save(rec));

    auto loaded = b.tryLoad();
    ASSERT_TRUE(loaded.has_value());
    EXPECT_EQ(loaded->schemaVersion, 1);
    EXPECT_EQ(loaded->previousMode, models::PowerMode::BestPerformance);
    EXPECT_EQ(loaded->savedAtUnixMs, 1745740800000);
}

TEST_F(BackupFixture, FileMissingReturnsNullopt)
{
    services::PowerModeBackup b(tmp_.path());
    EXPECT_FALSE(b.tryLoad().has_value());
}

TEST_F(BackupFixture, UnsupportedSchemaVersionDeletesFile)
{
    services::PowerModeBackup b(tmp_.path());
    nlohmann::json j = {
        {"schemaVersion", 99},
        {"previousMode", "Balanced"},
        {"savedAtUnixMs", 1},
        {"processId", 1}
    };
    {
        std::ofstream ofs(b.filePath(), std::ios::binary);
        ofs << j.dump(2);
    }
    EXPECT_FALSE(b.tryLoad().has_value());
    EXPECT_FALSE(fs::exists(b.filePath()));
}

TEST_F(BackupFixture, InvalidPreviousModeDeletesFile)
{
    services::PowerModeBackup b(tmp_.path());
    nlohmann::json j = {
        {"schemaVersion", 1},
        {"previousMode", "NotAMode"},
        {"savedAtUnixMs", 1},
        {"processId", 1}
    };
    {
        std::ofstream ofs(b.filePath(), std::ios::binary);
        ofs << j.dump(2);
    }
    EXPECT_FALSE(b.tryLoad().has_value());
    EXPECT_FALSE(fs::exists(b.filePath()));
}

TEST_F(BackupFixture, RestoreIsIdempotentDeleteFile)
{
    services::PowerModeBackup b(tmp_.path());
    services::PowerModeBackupRecord rec;
    rec.previousMode = models::PowerMode::BestPowerEfficiency;
    EXPECT_TRUE(b.save(rec));
    b.deleteFile();
    EXPECT_FALSE(fs::exists(b.filePath()));
    // 二度呼んでも問題なし
    b.deleteFile();
    EXPECT_FALSE(fs::exists(b.filePath()));
}
