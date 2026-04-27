#include "services/Logger.h"

#include <filesystem>
#include <fstream>
#include <thread>
#include <chrono>

#include <gtest/gtest.h>
#include <spdlog/spdlog.h>

namespace fs = std::filesystem;
using namespace imeindicator;

namespace {

// 一時ディレクトリ生成・破棄ヘルパー
class TempDir {
public:
    TempDir()
    {
        path_ = fs::temp_directory_path() / fs::path(L"imeindicator_test_") /
                fs::path(std::to_wstring(::GetCurrentProcessId()) + L"_" +
                          std::to_wstring(reinterpret_cast<uintptr_t>(this)));
        fs::create_directories(path_);
    }
    ~TempDir()
    {
        std::error_code ec;
        fs::remove_all(path_, ec);
    }
    const fs::path& path() const noexcept { return path_; }
private:
    fs::path path_;
};

class LoggerFixture : public ::testing::Test {
protected:
    void TearDown() override
    {
        // 各テスト間で Logger をクリーンに
        if (services::Logger::isInitialized()) {
            services::Logger::shutdown();
        }
        spdlog::drop_all();
    }
};

} // namespace

TEST_F(LoggerFixture, InitCreatesLogDirectoryAndAllCategories)
{
    TempDir tmp;
    services::Logger::init(tmp.path(), models::LogLevel::Info);
    EXPECT_TRUE(services::Logger::isInitialized());

    // 8 カテゴリすべてが register 済みか
    constexpr const char* kNames[] = {
        "app", "ime", "pixel", "priority", "power", "display", "tray", "settings"
    };
    for (auto name : kNames) {
        SCOPED_TRACE(name);
        auto logger = spdlog::get(name);
        ASSERT_NE(logger, nullptr);
        EXPECT_EQ(logger->level(), spdlog::level::info);
    }
}

TEST_F(LoggerFixture, SetGlobalLevelPropagatesToAllLoggers)
{
    TempDir tmp;
    services::Logger::init(tmp.path(), models::LogLevel::Warn);

    services::Logger::setGlobalLevel(models::LogLevel::Trace);

    auto log = spdlog::get("app");
    ASSERT_NE(log, nullptr);
    EXPECT_EQ(log->level(), spdlog::level::trace);
}

TEST_F(LoggerFixture, ShutdownAllowsReinit)
{
    TempDir tmp;
    services::Logger::init(tmp.path(), models::LogLevel::Info);
    services::Logger::shutdown();
    EXPECT_FALSE(services::Logger::isInitialized());

    // 再 init できる（少なくとも例外を投げない）
    services::Logger::init(tmp.path(), models::LogLevel::Debug);
    EXPECT_TRUE(services::Logger::isInitialized());
}

TEST_F(LoggerFixture, ToSpdlogLevelMappingIsCorrect)
{
    EXPECT_EQ(services::Logger::toSpdlogLevel(models::LogLevel::Trace),    spdlog::level::trace);
    EXPECT_EQ(services::Logger::toSpdlogLevel(models::LogLevel::Debug),    spdlog::level::debug);
    EXPECT_EQ(services::Logger::toSpdlogLevel(models::LogLevel::Info),     spdlog::level::info);
    EXPECT_EQ(services::Logger::toSpdlogLevel(models::LogLevel::Warn),     spdlog::level::warn);
    EXPECT_EQ(services::Logger::toSpdlogLevel(models::LogLevel::Error),    spdlog::level::err);
    EXPECT_EQ(services::Logger::toSpdlogLevel(models::LogLevel::Critical), spdlog::level::critical);
}
