#include "ProcessPriorityService.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cwctype>
#include <set>

#pragma comment(lib, "kernel32.lib")
#pragma comment(lib, "psapi.lib")

namespace imeindicator::services {

namespace {

bool equalsIgnoreCase(std::wstring_view a, std::wstring_view b) noexcept
{
    if (a.size() != b.size()) return false;
    return ::CompareStringOrdinal(a.data(), static_cast<int>(a.size()),
                                   b.data(), static_cast<int>(b.size()),
                                   TRUE) == CSTR_EQUAL;
}

std::wstring stripExeExtension(std::wstring_view name)
{
    constexpr std::wstring_view kExt = L".exe";
    if (name.size() < kExt.size()) return std::wstring(name);
    auto suffix = name.substr(name.size() - kExt.size());
    if (equalsIgnoreCase(suffix, kExt)) {
        return std::wstring(name.substr(0, name.size() - kExt.size()));
    }
    return std::wstring(name);
}

bool isAccessDeniedError() noexcept
{
    const DWORD e = ::GetLastError();
    return e == ERROR_ACCESS_DENIED || e == 5;
}

} // namespace

std::vector<ProcessPriorityEntry>
ProcessPriorityService::getProcessPriorities(const std::wstring& processNameNoExt)
{
    std::vector<ProcessPriorityEntry> out;

    HANDLE snap = ::CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return out;

    PROCESSENTRY32W pe{};
    pe.dwSize = sizeof(pe);
    if (!::Process32FirstW(snap, &pe)) {
        ::CloseHandle(snap);
        return out;
    }

    do {
        const std::wstring exeNoExt = stripExeExtension(pe.szExeFile);
        if (!equalsIgnoreCase(exeNoExt, processNameNoExt)) continue;

        HANDLE proc = ::OpenProcess(
            PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pe.th32ProcessID);
        if (!proc) {
            if (isAccessDeniedError()) accessDeniedCount_.fetch_add(1);
            continue;
        }

        DWORD pc = ::GetPriorityClass(proc);
        ::CloseHandle(proc);
        if (pc == 0) continue;

        out.push_back({pe.th32ProcessID, pc});
    } while (::Process32NextW(snap, &pe));

    ::CloseHandle(snap);
    return out;
}

bool ProcessPriorityService::setPriority(DWORD processId, DWORD priorityClass)
{
    HANDLE proc = ::OpenProcess(PROCESS_SET_INFORMATION, FALSE, processId);
    if (!proc) {
        if (isAccessDeniedError()) accessDeniedCount_.fetch_add(1);
        return false;
    }
    BOOL ok = ::SetPriorityClass(proc, priorityClass);
    if (!ok && isAccessDeniedError()) accessDeniedCount_.fetch_add(1);
    ::CloseHandle(proc);
    return ok != FALSE;
}

std::optional<DWORD_PTR> ProcessPriorityService::getAffinity(DWORD processId)
{
    HANDLE proc = ::OpenProcess(
        PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId);
    if (!proc) {
        if (isAccessDeniedError()) accessDeniedCount_.fetch_add(1);
        return std::nullopt;
    }
    DWORD_PTR procMask = 0, sysMask = 0;
    BOOL ok = ::GetProcessAffinityMask(proc, &procMask, &sysMask);
    ::CloseHandle(proc);
    if (!ok) return std::nullopt;
    return procMask;
}

bool ProcessPriorityService::setAffinity(DWORD processId, DWORD_PTR affinityMask)
{
    HANDLE proc = ::OpenProcess(PROCESS_SET_INFORMATION, FALSE, processId);
    if (!proc) {
        if (isAccessDeniedError()) accessDeniedCount_.fetch_add(1);
        return false;
    }
    BOOL ok = ::SetProcessAffinityMask(proc, affinityMask);
    if (!ok && isAccessDeniedError()) accessDeniedCount_.fetch_add(1);
    ::CloseHandle(proc);
    return ok != FALSE;
}

bool ProcessPriorityService::isAccessibleForControl(const std::wstring& processNameNoExt)
{
    HANDLE snap = ::CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return true;  // 判定不能 → 警告色を出さない

    PROCESSENTRY32W pe{};
    pe.dwSize = sizeof(pe);
    if (!::Process32FirstW(snap, &pe)) {
        ::CloseHandle(snap);
        return true;
    }

    bool sawAny = false;
    bool anyAccessible = false;
    do {
        const std::wstring exeNoExt = stripExeExtension(pe.szExeFile);
        if (!equalsIgnoreCase(exeNoExt, processNameNoExt)) continue;

        sawAny = true;
        // 実際の制御権限（PROCESS_SET_INFORMATION）を試す。
        HANDLE proc = ::OpenProcess(PROCESS_SET_INFORMATION, FALSE, pe.th32ProcessID);
        if (proc) {
            ::CloseHandle(proc);
            anyAccessible = true;
            break;  // 1 つでも制御可能なら以降の探索は不要
        }
        // OpenProcess 失敗時は GetLastError を見て ACCESS_DENIED 以外なら判定不能とする。
        if (!isAccessDeniedError()) {
            anyAccessible = true;  // 不明なエラーは保守的に「判定不能」扱い
            break;
        }
    } while (::Process32NextW(snap, &pe));

    ::CloseHandle(snap);

    // 一致プロセスが 0 件なら true（実行されていないので判定不能）
    if (!sawAny) return true;
    return anyAccessible;
}

std::vector<std::wstring> ProcessPriorityService::enumerateDistinctProcessNames()
{
    // 大文字小文字を区別しない比較で重複排除するための set コンパレータ
    struct IcmpLess {
        bool operator()(const std::wstring& a, const std::wstring& b) const noexcept
        {
            return ::_wcsicmp(a.c_str(), b.c_str()) < 0;
        }
    };
    std::set<std::wstring, IcmpLess> seen;

    HANDLE snap = ::CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return {};

    PROCESSENTRY32W pe{};
    pe.dwSize = sizeof(pe);
    if (::Process32FirstW(snap, &pe)) {
        do {
            seen.emplace(pe.szExeFile);
        } while (::Process32NextW(snap, &pe));
    }
    ::CloseHandle(snap);

    // set は IcmpLess 順なのでそのまま vector に変換
    return std::vector<std::wstring>(seen.begin(), seen.end());
}

} // namespace imeindicator::services
