#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// プロセス優先度操作の純粋仮想インターフェース（テスト用モック対応）。
// 既存 [IProcessPriorityService.cs] と等価のシグネチャ。
struct ProcessPriorityEntry {
    DWORD processId;
    DWORD currentPriorityClass;  // Windows の PRIORITY_CLASS 定数（NORMAL_PRIORITY_CLASS など）
};

class IProcessPriorityService {
public:
    virtual ~IProcessPriorityService() = default;

    // processName（拡張子なし、大小無視）に一致する全プロセスの (PID, 優先度クラス) を返す。
    virtual std::vector<ProcessPriorityEntry>
        getProcessPriorities(const std::wstring& processNameNoExt) = 0;

    // 指定 PID の優先度クラスを設定。成功時 true。
    virtual bool setPriority(DWORD processId, DWORD priorityClass) = 0;

    // 指定 PID のアフィニティを取得。失敗時 std::nullopt。
    virtual std::optional<DWORD_PTR> getAffinity(DWORD processId) = 0;

    // 指定 PID のアフィニティを設定。成功時 true。
    virtual bool setAffinity(DWORD processId, DWORD_PTR affinityMask) = 0;
};

} // namespace imeindicator::services
