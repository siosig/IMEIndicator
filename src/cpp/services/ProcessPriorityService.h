#pragma once

#include "IProcessPriorityService.h"

#include <atomic>

namespace imeindicator::services {

// IProcessPriorityService の Win32 実装。
// CreateToolhelp32Snapshot / OpenProcess / GetPriorityClass / SetPriorityClass /
// GetProcessAffinityMask / SetProcessAffinityMask を使用。
//
// 管理者権限の有無は accessDeniedCount() で参照可能（ProcessPrioritySettingsTab で
// 「管理者権限が必要」表示の判定に使う）。
class ProcessPriorityService : public IProcessPriorityService {
public:
    std::vector<ProcessPriorityEntry>
        getProcessPriorities(const std::wstring& processNameNoExt) override;
    bool setPriority(DWORD processId, DWORD priorityClass) override;
    std::optional<DWORD_PTR> getAffinity(DWORD processId) override;
    bool setAffinity(DWORD processId, DWORD_PTR affinityMask) override;

    // 管理者権限不足での失敗回数（累積）。0 でない場合は管理者権限が必要な可能性あり。
    int accessDeniedCount() const noexcept { return accessDeniedCount_.load(); }

private:
    std::atomic<int> accessDeniedCount_{0};
};

} // namespace imeindicator::services
