#pragma once

#include <cstdint>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// E-Core / P-Core 情報をプロセス起動時に 1 回取得してキャッシュする。
// Intel 第 12 世代以降のハイブリッドアーキテクチャ対応（既存 [ECoreCpuInfo.cs] と等価）。
//
// 内部で GetSystemCpuSetInformation を使用し、EfficiencyClass の最小値に該当する
// 論理プロセッサを E-Core と判定する。
class ECoreCpuInfo {
public:
    static const ECoreCpuInfo& instance() noexcept;

    bool       hasECores() const noexcept   { return hasECores_; }
    DWORD_PTR  eCoreMask() const noexcept   { return eCoreMask_; }
    int        eCoreCount() const noexcept  { return eCoreCount_; }
    int        pCoreCount() const noexcept  { return pCoreCount_; }

private:
    ECoreCpuInfo();
    ECoreCpuInfo(const ECoreCpuInfo&) = delete;
    ECoreCpuInfo& operator=(const ECoreCpuInfo&) = delete;

    void detect() noexcept;

    bool hasECores_{false};
    DWORD_PTR eCoreMask_{0};
    int eCoreCount_{0};
    int pCoreCount_{0};
};

} // namespace imeindicator::services
