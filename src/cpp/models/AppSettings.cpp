#include "AppSettings.h"

#include "../app/AppConstants.h"
#include "../win32/UnicodeUtil.h"

#include <algorithm>
#include <cctype>

namespace imeindicator::models {

const char* logLevelToString(LogLevel level) noexcept
{
    switch (level) {
        case LogLevel::Trace:    return "trace";
        case LogLevel::Debug:    return "debug";
        case LogLevel::Info:     return "info";
        case LogLevel::Warn:     return "warn";
        case LogLevel::Error:    return "error";
        case LogLevel::Critical: return "critical";
    }
    return "warn";
}

bool tryParseLogLevel(std::string_view s, LogLevel& out) noexcept
{
    // 大小無視で比較
    auto eq = [](std::string_view a, std::string_view b) {
        if (a.size() != b.size()) return false;
        for (size_t i = 0; i < a.size(); ++i) {
            if (std::tolower(static_cast<unsigned char>(a[i])) !=
                std::tolower(static_cast<unsigned char>(b[i]))) return false;
        }
        return true;
    };
    if (eq(s, "trace"))    { out = LogLevel::Trace;    return true; }
    if (eq(s, "debug"))    { out = LogLevel::Debug;    return true; }
    if (eq(s, "info"))     { out = LogLevel::Info;     return true; }
    if (eq(s, "warn"))     { out = LogLevel::Warn;     return true; }
    if (eq(s, "warning"))  { out = LogLevel::Warn;     return true; }  // 別表記受容
    if (eq(s, "error"))    { out = LogLevel::Error;    return true; }
    if (eq(s, "err"))      { out = LogLevel::Error;    return true; }
    if (eq(s, "critical")) { out = LogLevel::Critical; return true; }
    return false;
}

namespace {

// 文字数カウント（コードポイント単位ではなく UTF-16 のコードユニット数で簡易対応）
// spec の "1〜8 文字" 制約は表示する文字数の意図であり、サロゲートペアまで厳密にチェックする
// 必要は薄いため、wchar_t 単位の長さで判定する。
std::wstring clampIndicatorText(std::wstring_view src, std::wstring_view fallback)
{
    std::wstring trimmed = win32::trim(src);
    if (trimmed.empty()) {
        return std::wstring(fallback);
    }
    if (trimmed.size() > static_cast<size_t>(app::AppConstants::MaxIndicatorTextChars)) {
        trimmed.resize(static_cast<size_t>(app::AppConstants::MaxIndicatorTextChars));
    }
    return trimmed;
}

} // namespace

void AppSettings::clamp()
{
    mouseCursorIndicator.clamp();

    imeOnText  = clampIndicatorText(imeOnText,  L"あ");
    imeOffText = clampIndicatorText(imeOffText, L"A");

    pollingIntervalSeconds = std::clamp(
        pollingIntervalSeconds,
        app::AppConstants::MinPollingIntervalSeconds,
        app::AppConstants::MaxPollingIntervalSeconds);

    // 31 件目以降を切り捨て
    if (processPriorityRules.size() >
        static_cast<size_t>(app::AppConstants::MaxProcessPriorityRules)) {
        processPriorityRules.resize(
            static_cast<size_t>(app::AppConstants::MaxProcessPriorityRules));
    }
    for (auto& rule : processPriorityRules) {
        rule.maxBackoffExponent = std::clamp(rule.maxBackoffExponent, 0, 10);
    }

    pixelVerificationIntervalMs = std::clamp(pixelVerificationIntervalMs, 0, 60000);

    // logLevel は from_json/tryParse 段階で範囲チェック済みのため再 Clamp 不要
    if (static_cast<int>(logLevel) < 0 || static_cast<int>(logLevel) > 5) {
        logLevel = LogLevel::Warn;
    }

    // schemaVersion は 1 / 2 / 3 のみ受容、書き出し時には to_json で 3 を強制する
    if (schemaVersion != 1 && schemaVersion != 2 && schemaVersion != 3) {
        schemaVersion = 3;
    }

    // v3 新規セクションのクランプ
    hotkeySettings.clamp();
}

void to_json(nlohmann::json& j, const AppSettings& s)
{
    nlohmann::json rules = nlohmann::json::array();
    for (const auto& r : s.processPriorityRules) {
        nlohmann::json rj;
        to_json(rj, r);
        rules.push_back(std::move(rj));
    }

    nlohmann::json mci;
    to_json(mci, s.mouseCursorIndicator);

    nlohmann::json hk;
    to_json(hk, s.hotkeySettings);

    // 書き出し時は常に schemaVersion: 3（contracts/settings-schema-v3.md §書き出し時の挙動）
    j = nlohmann::json{
        {"schemaVersion",                 3},
        {"mouseCursorIndicator",          std::move(mci)},
        {"imeOnText",                     win32::wideToUtf8(s.imeOnText)},
        {"imeOffText",                    win32::wideToUtf8(s.imeOffText)},
        {"isFirstLaunch",                 s.isFirstLaunch},
        {"processPriorityRules",          std::move(rules)},
        {"pollingIntervalSeconds",        s.pollingIntervalSeconds},
        {"logLevel",                      logLevelToString(s.logLevel)},
        {"pixelVerificationIntervalMs",   s.pixelVerificationIntervalMs},
        {"hotkeySettings",                std::move(hk)}
    };
}

void from_json(const nlohmann::json& j, AppSettings& s)
{
    // schemaVersion: 未指定 → 1（v1 として読み込む）
    s.schemaVersion = j.value("schemaVersion", 1);

    if (auto it = j.find("mouseCursorIndicator");
        it != j.end() && it->is_object()) {
        from_json(*it, s.mouseCursorIndicator);
    } else {
        s.mouseCursorIndicator = MouseCursorIndicatorSettings{};
    }

    if (auto it = j.find("imeOnText"); it != j.end() && it->is_string()) {
        s.imeOnText = win32::utf8ToWide(it->get<std::string>());
    }
    if (auto it = j.find("imeOffText"); it != j.end() && it->is_string()) {
        s.imeOffText = win32::utf8ToWide(it->get<std::string>());
    }

    s.isFirstLaunch = j.value("isFirstLaunch", true);

    s.processPriorityRules.clear();
    if (auto it = j.find("processPriorityRules");
        it != j.end() && it->is_array()) {
        for (const auto& elem : *it) {
            if (!elem.is_object()) continue;
            ProcessPriorityRule r;
            from_json(elem, r);
            s.processPriorityRules.push_back(std::move(r));
        }
    }

    s.pollingIntervalSeconds = j.value("pollingIntervalSeconds", 1);

    // logLevel: v1 では未指定 → Warn
    s.logLevel = LogLevel::Warn;
    if (auto it = j.find("logLevel"); it != j.end() && it->is_string()) {
        LogLevel lv = LogLevel::Warn;
        if (tryParseLogLevel(it->get<std::string>(), lv)) {
            s.logLevel = lv;
        }
    }

    s.pixelVerificationIntervalMs = j.value("pixelVerificationIntervalMs", 2000);

    // v3 新規: hotkeySettings（v2 以前は未定義 → 既定値で構築）
    s.hotkeySettings = hotkey::HotkeySettings{};
    if (auto it = j.find("hotkeySettings"); it != j.end() && it->is_object()) {
        from_json(*it, s.hotkeySettings);
    }

    // 読み込み直後にクランプ
    s.clamp();
}

} // namespace imeindicator::models
