#include "ProcessPriorityRule.h"

#include "../win32/UnicodeUtil.h"

#include <algorithm>

namespace imeindicator::models {

const char* priorityLevelToString(PriorityLevel level) noexcept
{
    switch (level) {
        case PriorityLevel::Idle:        return "Idle";
        case PriorityLevel::BelowNormal: return "BelowNormal";
        case PriorityLevel::Normal:      return "Normal";
        case PriorityLevel::AboveNormal: return "AboveNormal";
        case PriorityLevel::High:        return "High";
        case PriorityLevel::Realtime:    return "Realtime";
    }
    return "Normal";
}

bool tryParsePriorityLevel(std::string_view s, PriorityLevel& out) noexcept
{
    if (s == "Idle")        { out = PriorityLevel::Idle;        return true; }
    if (s == "BelowNormal") { out = PriorityLevel::BelowNormal; return true; }
    if (s == "Normal")      { out = PriorityLevel::Normal;      return true; }
    if (s == "AboveNormal") { out = PriorityLevel::AboveNormal; return true; }
    if (s == "High")        { out = PriorityLevel::High;        return true; }
    if (s == "Realtime")    { out = PriorityLevel::Realtime;    return true; }
    return false;
}

DWORD priorityLevelToProcessPriorityClass(PriorityLevel level) noexcept
{
    switch (level) {
        case PriorityLevel::Idle:        return IDLE_PRIORITY_CLASS;
        case PriorityLevel::BelowNormal: return BELOW_NORMAL_PRIORITY_CLASS;
        case PriorityLevel::Normal:      return NORMAL_PRIORITY_CLASS;
        case PriorityLevel::AboveNormal: return ABOVE_NORMAL_PRIORITY_CLASS;
        case PriorityLevel::High:        return HIGH_PRIORITY_CLASS;
        case PriorityLevel::Realtime:    return REALTIME_PRIORITY_CLASS;
    }
    return NORMAL_PRIORITY_CLASS;
}

std::wstring ProcessPriorityRule::normalizedProcessName() const
{
    return win32::stripExeAndTrim(processName);
}

bool ProcessPriorityRule::isValid() const
{
    return !win32::trim(processName).empty();
}

int ProcessPriorityRule::validatedMaxBackoffExponent() const
{
    return std::clamp(maxBackoffExponent, 0, 10);
}

void to_json(nlohmann::json& j, const ProcessPriorityRule& r)
{
    j = nlohmann::json{
        {"processName",        win32::wideToUtf8(r.processName)},
        {"targetPriority",     priorityLevelToString(r.targetPriority)},
        {"maxBackoffExponent", r.maxBackoffExponent},
        {"isEnabled",          r.isEnabled},
        {"useECoreOnly",       r.useECoreOnly}
    };
}

void from_json(const nlohmann::json& j, ProcessPriorityRule& r)
{
    if (auto it = j.find("processName"); it != j.end() && it->is_string()) {
        r.processName = win32::utf8ToWide(it->get<std::string>());
    } else {
        r.processName.clear();
    }

    PriorityLevel parsedLevel = PriorityLevel::Normal;
    if (auto itStr = j.find("targetPriority");
        itStr != j.end() && itStr->is_string()) {
        if (!tryParsePriorityLevel(itStr->get<std::string>(), parsedLevel)) {
            parsedLevel = PriorityLevel::Normal;
        }
    } else if (auto itNum = j.find("targetPriority");
               itNum != j.end() && itNum->is_number_integer()) {
        // 数値で書かれていた場合のフォールバック
        const int v = itNum->get<int>();
        if (v >= 0 && v <= 5) parsedLevel = static_cast<PriorityLevel>(v);
    }
    r.targetPriority = parsedLevel;

    r.maxBackoffExponent = std::clamp(j.value("maxBackoffExponent", 6), 0, 10);
    r.isEnabled    = j.value("isEnabled",    true);
    r.useECoreOnly = j.value("useECoreOnly", false);
}

} // namespace imeindicator::models
