#include "ECoreCpuInfo.h"

#include <algorithm>
#include <set>
#include <vector>

namespace imeindicator::services {

namespace {

// SYSTEM_CPU_SET_INFORMATION のレイアウト（既存 C# 版と同じ）。
// kernel32.dll の GetSystemCpuSetInformation は Win10 以降。
#pragma pack(push, 1)
struct SystemCpuSetInfo {
    uint32_t Size;
    int32_t  Type;
    uint32_t Id;
    uint16_t Group;
    uint8_t  LogicalProcessorIndex;
    uint8_t  CoreIndex;
    uint8_t  LastLevelCacheIndex;
    uint8_t  NumaNodeIndex;
    uint8_t  EfficiencyClass;
    uint8_t  AllFlags;
    uint32_t SchedulingClass;
    uint64_t AllocationTag;
};
#pragma pack(pop)

using PFN_GetSystemCpuSetInformation = BOOL(WINAPI*)(
    PSYSTEM_CPU_SET_INFORMATION, ULONG, PULONG, HANDLE, ULONG);

PFN_GetSystemCpuSetInformation resolveApi() noexcept
{
    HMODULE h = ::GetModuleHandleW(L"kernel32.dll");
    if (!h) return nullptr;
    return reinterpret_cast<PFN_GetSystemCpuSetInformation>(
        ::GetProcAddress(h, "GetSystemCpuSetInformation"));
}

} // namespace

const ECoreCpuInfo& ECoreCpuInfo::instance() noexcept
{
    static ECoreCpuInfo s;
    return s;
}

ECoreCpuInfo::ECoreCpuInfo()
{
    detect();
}

void ECoreCpuInfo::detect() noexcept
{
    auto api = resolveApi();
    if (!api) return;

    ULONG bufferSize = 0;
    api(nullptr, 0, &bufferSize, nullptr, 0);
    if (bufferSize == 0) return;

    std::vector<uint8_t> buffer(bufferSize);
    if (!api(reinterpret_cast<PSYSTEM_CPU_SET_INFORMATION>(buffer.data()),
             bufferSize, &bufferSize, nullptr, 0)) {
        return;
    }

    std::vector<SystemCpuSetInfo> entries;
    size_t offset = 0;
    while (offset + sizeof(uint32_t) <= bufferSize) {
        const auto* info = reinterpret_cast<const SystemCpuSetInfo*>(buffer.data() + offset);
        if (info->Size == 0 || offset + info->Size > bufferSize) break;
        if (info->Type == 0) {  // CpuSetInformation
            entries.push_back(*info);
        }
        offset += info->Size;
    }

    if (entries.empty()) return;

    std::set<uint8_t> classes;
    for (const auto& e : entries) classes.insert(e.EfficiencyClass);
    if (classes.size() <= 1) return;  // 単一クラス → ハイブリッドではない

    const uint8_t minClass = *classes.begin();
    DWORD_PTR mask = 0;
    int eCnt = 0, pCnt = 0;
    for (const auto& e : entries) {
        if (e.EfficiencyClass == minClass) {
            mask |= (DWORD_PTR{1} << e.LogicalProcessorIndex);
            ++eCnt;
        } else {
            ++pCnt;
        }
    }

    if (eCnt > 0 && pCnt > 0) {
        hasECores_ = true;
        eCoreMask_ = mask;
        eCoreCount_ = eCnt;
        pCoreCount_ = pCnt;
    }
}

} // namespace imeindicator::services
