#include "models/AppSettings.h"

#include <gtest/gtest.h>

using namespace imeindicator::models;

// v1 → v2 互換: schemaVersion 欠落の v1 サンプルを読み、
// 新規フィールド (logLevel, pixelVerificationIntervalMs) のデフォルト値が補完されることを検証。
TEST(JsonSchemaCompatTests, V1WithoutSchemaVersionFieldsDefaulted)
{
    nlohmann::json j = {
        {"mouseCursorIndicator", {
            {"isVisible", true}, {"size", 34.0}, {"opacity", 0.9},
            {"offsetX", 15.0}, {"offsetY", 15.0}
        }},
        {"imeOnText", "あ"},
        {"imeOffText", "A"},
        {"isFirstLaunch", false},
        {"processPriorityRules", nlohmann::json::array()},
        {"pollingIntervalSeconds", 1}
        // logLevel と pixelVerificationIntervalMs は欠落
    };

    AppSettings s;
    from_json(j, s);
    // schemaVersion は v1 として読まれる（コンスト 1）
    // ただし AppSettings.cpp の clamp() はそれを 2 に固定する仕様だが、
    // SettingsManager 側で v1→v2 昇格を判定するために from_json では生値を保持する必要がある。
    // 本テストでは新規フィールドのデフォルト供給だけを確認。
    EXPECT_EQ(s.logLevel, LogLevel::Warn);
    EXPECT_EQ(s.pixelVerificationIntervalMs, 2000);
}

TEST(JsonSchemaCompatTests, UnknownFieldsAreIgnored)
{
    nlohmann::json j = {
        {"schemaVersion", 2},
        {"mouseCursorIndicator", {
            {"isVisible", true}, {"size", 34.0}, {"opacity", 0.9},
            {"offsetX", 15.0}, {"offsetY", 15.0}
        }},
        {"imeOnText", "あ"},
        {"imeOffText", "A"},
        {"isFirstLaunch", false},
        {"processPriorityRules", nlohmann::json::array()},
        {"pollingIntervalSeconds", 1},
        {"logLevel", "warn"},
        {"pixelVerificationIntervalMs", 2000},
        {"someFutureField", "ignored"}
    };

    AppSettings s;
    EXPECT_NO_THROW(from_json(j, s));
    EXPECT_EQ(s.logLevel, LogLevel::Warn);
}

TEST(JsonSchemaCompatTests, V2WriteHasCamelCaseKeysAndIndent)
{
    AppSettings s;
    s.imeOnText = L"テ";
    s.imeOffText = L"E";

    nlohmann::json j;
    to_json(j, s);

    // 主要キーは camelCase
    EXPECT_TRUE(j.contains("schemaVersion"));
    EXPECT_TRUE(j.contains("mouseCursorIndicator"));
    EXPECT_TRUE(j.contains("imeOnText"));
    EXPECT_TRUE(j.contains("imeOffText"));
    EXPECT_TRUE(j.contains("isFirstLaunch"));
    EXPECT_TRUE(j.contains("processPriorityRules"));
    EXPECT_TRUE(j.contains("pollingIntervalSeconds"));
    EXPECT_TRUE(j.contains("logLevel"));
    EXPECT_TRUE(j.contains("pixelVerificationIntervalMs"));

    // 書き出しは常に schemaVersion: 2
    EXPECT_EQ(j["schemaVersion"], 2);

    // dump(2) は 2 スペースインデント・LF 改行
    auto dumped = j.dump(2);
    EXPECT_NE(dumped.find("\n  \"mouseCursorIndicator\""), std::string::npos);
    // CRLF は含まれない
    EXPECT_EQ(dumped.find("\r\n"), std::string::npos);
}

TEST(JsonSchemaCompatTests, LogLevelParsingIsCaseInsensitive)
{
    LogLevel out;
    EXPECT_TRUE(tryParseLogLevel("warn",     out)); EXPECT_EQ(out, LogLevel::Warn);
    EXPECT_TRUE(tryParseLogLevel("WARN",     out)); EXPECT_EQ(out, LogLevel::Warn);
    EXPECT_TRUE(tryParseLogLevel("Critical", out)); EXPECT_EQ(out, LogLevel::Critical);
    EXPECT_TRUE(tryParseLogLevel("trace",    out)); EXPECT_EQ(out, LogLevel::Trace);
    EXPECT_FALSE(tryParseLogLevel("notalevel", out));
}

TEST(JsonSchemaCompatTests, OutOfRangeLogLevelFallsBackToWarn)
{
    nlohmann::json j = {
        {"schemaVersion", 2},
        {"logLevel", "garbage"},
        {"mouseCursorIndicator", {
            {"isVisible", true}, {"size", 34.0}, {"opacity", 0.9},
            {"offsetX", 15.0}, {"offsetY", 15.0}
        }},
        {"imeOnText", "あ"}, {"imeOffText", "A"},
        {"isFirstLaunch", false},
        {"processPriorityRules", nlohmann::json::array()},
        {"pollingIntervalSeconds", 1},
        {"pixelVerificationIntervalMs", 2000}
    };
    AppSettings s;
    from_json(j, s);
    EXPECT_EQ(s.logLevel, LogLevel::Warn);
}
