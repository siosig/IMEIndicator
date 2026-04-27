#include "HwndUtil.h"

#include <psapi.h>
#include <wil/resource.h>

#include <chrono>
#include <mutex>
#include <unordered_map>

#pragma comment(lib, "psapi.lib")

namespace imeindicator::win32 {
namespace {

struct CacheEntry {
    std::wstring name;
    std::chrono::steady_clock::time_point expiresAt;
};

std::mutex g_cacheMutex;
std::unordered_map<DWORD, CacheEntry> g_processNameCache;
constexpr auto kCacheTtl = std::chrono::seconds(1);

std::wstring extractFileNameFromPath(std::wstring_view path)
{
    auto slash = path.find_last_of(L"\\/");
    if (slash == std::wstring_view::npos) {
        return std::wstring(path);
    }
    return std::wstring(path.substr(slash + 1));
}

} // namespace

std::wstring getProcessNameByPid(DWORD pid)
{
    if (pid == 0) return {};

    {
        std::lock_guard lk(g_cacheMutex);
        auto it = g_processNameCache.find(pid);
        if (it != g_processNameCache.end()) {
            if (std::chrono::steady_clock::now() < it->second.expiresAt) {
                return it->second.name;
            }
            g_processNameCache.erase(it);
        }
    }

    wil::unique_handle hProcess(::OpenProcess(
        PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid));
    if (!hProcess) {
        return {};
    }

    wchar_t buffer[MAX_PATH] = {};
    DWORD size = MAX_PATH;
    if (!::QueryFullProcessImageNameW(hProcess.get(), 0, buffer, &size) || size == 0) {
        return {};
    }

    std::wstring name = extractFileNameFromPath(std::wstring_view(buffer, size));

    {
        std::lock_guard lk(g_cacheMutex);
        g_processNameCache[pid] = CacheEntry{
            name,
            std::chrono::steady_clock::now() + kCacheTtl
        };
    }
    return name;
}

std::wstring getProcessNameByHwnd(HWND hwnd)
{
    if (!hwnd) return {};
    DWORD pid = 0;
    ::GetWindowThreadProcessId(hwnd, &pid);
    return getProcessNameByPid(pid);
}

std::wstring getWindowTitle(HWND hwnd)
{
    if (!hwnd) return {};
    const int len = ::GetWindowTextLengthW(hwnd);
    if (len <= 0) return {};
    std::wstring buf(static_cast<size_t>(len) + 1, L'\0');
    const int got = ::GetWindowTextW(hwnd, buf.data(), len + 1);
    if (got <= 0) return {};
    buf.resize(static_cast<size_t>(got));
    return buf;
}

std::wstring getWindowClassName(HWND hwnd)
{
    if (!hwnd) return {};
    wchar_t buf[256] = {};
    const int got = ::GetClassNameW(hwnd, buf, static_cast<int>(std::size(buf)));
    if (got <= 0) return {};
    return std::wstring(buf, static_cast<size_t>(got));
}

HKL getKeyboardLayoutOfHwnd(HWND hwnd)
{
    if (!hwnd) return ::GetKeyboardLayout(0);
    DWORD pid = 0;
    DWORD tid = ::GetWindowThreadProcessId(hwnd, &pid);
    return ::GetKeyboardLayout(tid);
}

bool isJapaneseHkl(HKL hkl)
{
    // HKL の下位 16 bit が言語 ID。0x0411 = 日本語(ja-JP)。
    constexpr WORD kJapaneseLangId = 0x0411;
    return LOWORD(reinterpret_cast<uintptr_t>(hkl)) == kJapaneseLangId;
}

} // namespace imeindicator::win32
