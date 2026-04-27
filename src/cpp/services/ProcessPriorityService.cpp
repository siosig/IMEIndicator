#include "ProcessPriorityService.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cwctype>

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

} // namespace imeindicator::services
