#include "services/SettingsManager.h"
#include "services/Logger.h"
#include "models/AppSettings.h"
#include "app/AppConstants.h"

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
        path_ = fs::temp_directory_path() / fs::path(L"imeindicator_settings_") /
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

void copyFixtureTo(const fs::path& src, const fs::path& dst)
{
    fs::copy_file(src, dst, fs::copy_options::overwrite_existing);
}

fs::path fixturesDir()
{
    // tests/cpp/CMakeLists.txt の copy_directory で fixtures/ がビルド出力にコピーされる
    return fs::current_path() / L"fixtures";
}

class SettingsFixture : public ::testing::Test {
protected:
    void SetUp() override
    {
        // ロガーを silent な temp ディレクトリで初期化（テストでログ書き込み副作用を避ける）
        if (!services::Logger::isInitialized()) {
            services::Logger::init(tmp_.path() / L"logs", models::LogLevel::Critical);
        }
    }
    TempDir tmp_;
};

} // namespace

TEST_F(SettingsFixture, FreshDirectoryYieldsDefaultsAndFirstLaunch)
{
    services::SettingsManager mgr(tmp_.path());
    EXPECT_FALSE(mgr.load());  // ファイル無し → false 返り、デフォルトで構築
    EXPECT_TRUE(mgr.settings().isFirstLaunch);
    EXPECT_EQ(mgr.settings().pollingIntervalSeconds, 1);
    EXPECT_EQ(mgr.settings().mouseCursorIndicator.size, 34.0);
}

TEST_F(SettingsFixture, LoadV1SampleAndMigrateOnSave)
{
    auto fxV1 = fixturesDir() / L"settings-v1-sample.json";
    if (!fs::exists(fxV1)) GTEST_SKIP() << "fixture not found: " << fxV1;

    services::SettingsManager mgr(tmp_.path());
    copyFixtureTo(fxV1, mgr.settingsFilePath());

    EXPECT_TRUE(mgr.load());
    EXPECT_EQ(mgr.settings().mouseCursorIndicator.size, 40);
    EXPECT_EQ(mgr.settings().pollingIntervalSeconds, 2);
    ASSERT_EQ(mgr.settings().processPriorityRules.size(), 1u);
    EXPECT_EQ(mgr.settings().processPriorityRules[0].targetPriority,
              models::PriorityLevel::BelowNormal);
    // v1 として読まれたあと内部で v4 に昇格しているはず（013-ime-corner-image）
    EXPECT_EQ(mgr.settings().schemaVersion, 4);

    // Save すれば v4 で書き出される
    EXPECT_TRUE(mgr.save());
    std::ifstream ifs(mgr.settingsFilePath());
    nlohmann::json j; ifs >> j;
    EXPECT_EQ(j.value("schemaVersion", 0), 4);
    EXPECT_TRUE(j.contains("logLevel"));
    EXPECT_TRUE(j.contains("pixelVerificationIntervalMs"));
}

TEST_F(SettingsFixture, LoadV2SampleRoundTrip)
{
    auto fxV2 = fixturesDir() / L"settings-v2-sample.json";
    if (!fs::exists(fxV2)) GTEST_SKIP() << "fixture not found: " << fxV2;

    services::SettingsManager mgr(tmp_.path());
    copyFixtureTo(fxV2, mgr.settingsFilePath());

    EXPECT_TRUE(mgr.load());
    EXPECT_EQ(mgr.settings().schemaVersion, 4);
    EXPECT_EQ(mgr.settings().processPriorityRules.size(), 2u);
    EXPECT_EQ(mgr.settings().logLevel, models::LogLevel::Warn);
    EXPECT_EQ(mgr.settings().pixelVerificationIntervalMs, 2000);

    // Save 後に再読み込みしても等価
    auto original = mgr.settings();
    ASSERT_TRUE(mgr.save());
    services::SettingsManager mgr2(tmp_.path());
    ASSERT_TRUE(mgr2.load());
    EXPECT_EQ(mgr2.settings(), original);
}

// v3 → v4 互換（013-ime-corner-image / contracts/settings-schema-v4.md）:
// backgroundImage 欠落の v3 サンプルを読み、既定値（無効）で補完・v4 へ昇格・.v3.bak 生成を検証。
TEST_F(SettingsFixture, LoadV3SampleMigratesToV4AndCreatesBackup)
{
    auto fxV3 = fixturesDir() / L"settings-v3-sample.json";
    if (!fs::exists(fxV3)) GTEST_SKIP() << "fixture not found: " << fxV3;

    services::SettingsManager mgr(tmp_.path());
    copyFixtureTo(fxV3, mgr.settingsFilePath());

    EXPECT_TRUE(mgr.load());
    // v3 には backgroundImage が無い → 既定値（無効）
    EXPECT_FALSE(mgr.settings().backgroundImage.isVisible);
    // v3 として読まれたあと内部で v4 に昇格
    EXPECT_EQ(mgr.settings().schemaVersion, 4);
    EXPECT_EQ(mgr.settings().processPriorityRules.size(), 2u);
    EXPECT_EQ(mgr.settings().hotkeySettings.hotkeys.size(), 0u);

    // v3 バックアップが作られる
    auto bak = mgr.settingsFilePath();
    bak += std::wstring(app::AppConstants::V3BackupSuffix);
    ASSERT_TRUE(fs::exists(bak));

    // 最古を保持: 既存の .v3.bak は 2 回目の load で上書きされない
    {
        std::ofstream ofs(bak, std::ios::binary | std::ios::app);
        ofs << "\n// marker";
    }
    services::SettingsManager again(tmp_.path());
    EXPECT_TRUE(again.load());
    {
        std::ifstream ifs(bak, std::ios::binary);
        std::string content((std::istreambuf_iterator<char>(ifs)), std::istreambuf_iterator<char>());
        EXPECT_NE(content.find("// marker"), std::string::npos);
    }

    // Save すれば v4 で書き出され、backgroundImage が含まれる
    EXPECT_TRUE(mgr.save());
    std::ifstream ifs(mgr.settingsFilePath());
    nlohmann::json j; ifs >> j;
    EXPECT_EQ(j.value("schemaVersion", 0), 4);
    ASSERT_TRUE(j.contains("backgroundImage"));
    EXPECT_FALSE(j["backgroundImage"].value("isVisible", true));
}

// backgroundImage.isVisible の永続化往復（spec FR-007）
TEST_F(SettingsFixture, BackgroundImageVisibleRoundTrip)
{
    services::SettingsManager mgr(tmp_.path());
    mgr.load();  // ファイル無し → 既定値
    EXPECT_FALSE(mgr.settings().backgroundImage.isVisible);

    auto s = mgr.settings();
    s.backgroundImage.isVisible = true;
    mgr.setSettings(std::move(s));
    ASSERT_TRUE(mgr.save());

    services::SettingsManager mgr2(tmp_.path());
    ASSERT_TRUE(mgr2.load());
    EXPECT_TRUE(mgr2.settings().backgroundImage.isVisible);
    EXPECT_EQ(mgr2.settings().schemaVersion, 4);
    // mouseCursorIndicator.isVisible とは独立（spec FR-009）
    EXPECT_TRUE(mgr2.settings().mouseCursorIndicator.isVisible);
}

TEST_F(SettingsFixture, BrokenFileFallsBackToDefaultsAndRenames)
{
    auto fxBroken = fixturesDir() / L"settings-broken.json";
    if (!fs::exists(fxBroken)) GTEST_SKIP() << "fixture not found: " << fxBroken;

    services::SettingsManager mgr(tmp_.path());
    copyFixtureTo(fxBroken, mgr.settingsFilePath());

    EXPECT_FALSE(mgr.load());  // パース失敗 → false
    EXPECT_TRUE(mgr.settings().isFirstLaunch);  // デフォルト構築

    // 元ファイルは renameBroken で消えている
    EXPECT_FALSE(fs::exists(mgr.settingsFilePath()));
    // .broken-* がいる
    bool foundBroken = false;
    for (const auto& e : fs::directory_iterator(tmp_.path())) {
        const auto name = e.path().filename().wstring();
        if (name.find(L".broken-") != std::wstring::npos) {
            foundBroken = true; break;
        }
    }
    EXPECT_TRUE(foundBroken);
}

TEST_F(SettingsFixture, ClampsOutOfRangeValuesOnLoad)
{
    services::SettingsManager mgr(tmp_.path());
    nlohmann::json j = {
        {"schemaVersion", 2},
        {"mouseCursorIndicator", {
            {"isVisible", true}, {"size", 9999.0}, {"opacity", 5.0},
            {"offsetX", 0.0}, {"offsetY", 0.0}
        }},
        {"imeOnText", "あ"},
        {"imeOffText", "A"},
        {"isFirstLaunch", false},
        {"processPriorityRules", nlohmann::json::array()},
        {"pollingIntervalSeconds", 99999},
        {"logLevel", "warn"},
        {"pixelVerificationIntervalMs", 99999}
    };
    {
        std::ofstream ofs(mgr.settingsFilePath(), std::ios::binary);
        ofs << j.dump(2);
    }
    EXPECT_TRUE(mgr.load());
    EXPECT_LE(mgr.settings().mouseCursorIndicator.size, 100.0);
    EXPECT_LE(mgr.settings().mouseCursorIndicator.opacity, 1.0);
    EXPECT_LE(mgr.settings().pollingIntervalSeconds, 1800);
    EXPECT_LE(mgr.settings().pixelVerificationIntervalMs, 60000);
}

TEST_F(SettingsFixture, TruncatesProcessPriorityRulesAt30)
{
    services::SettingsManager mgr(tmp_.path());

    nlohmann::json arr = nlohmann::json::array();
    for (int i = 0; i < 50; ++i) {
        arr.push_back({
            {"processName", std::string("p") + std::to_string(i)},
            {"targetPriority", "Normal"},
            {"maxBackoffExponent", 6},
            {"isEnabled", true},
            {"useECoreOnly", false}
        });
    }
    nlohmann::json j = {
        {"schemaVersion", 2},
        {"mouseCursorIndicator", {{"isVisible", true}, {"size", 34.0},
            {"opacity", 0.9}, {"offsetX", 15.0}, {"offsetY", 15.0}}},
        {"imeOnText", "あ"}, {"imeOffText", "A"},
        {"isFirstLaunch", false},
        {"processPriorityRules", arr},
        {"pollingIntervalSeconds", 1},
        {"logLevel", "warn"},
        {"pixelVerificationIntervalMs", 2000}
    };
    {
        std::ofstream ofs(mgr.settingsFilePath(), std::ios::binary);
        ofs << j.dump(2);
    }
    EXPECT_TRUE(mgr.load());
    EXPECT_EQ(mgr.settings().processPriorityRules.size(), 30u);
}
