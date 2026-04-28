#include "HwndUtil.h"

#include <psapi.h>
#include <wil/resource.h>

#include <cwctype>
#include <chrono>
#include <mutex>
#include <string_view>
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

// ===== HotkeyP マージ (010-hotkeyp-merge) で追加されたヘルパー =====

std::wstring getForegroundExeName()
{
    HWND hw = GetForegroundWindow();
    if (!hw) return {};
    return getProcessNameByHwnd(hw);
}

namespace {

struct FindWindowContext {
    std::wstring targetExe;  // 比較対象（ファイル名のみ、小文字化済み）
    HWND found = nullptr;
};

std::wstring toLowerCopy(std::wstring_view s) {
    std::wstring r(s);
    for (auto& c : r) c = static_cast<wchar_t>(::towlower(c));
    return r;
}

BOOL CALLBACK enumWindowsForExe(HWND hwnd, LPARAM lParam)
{
    auto* ctx = reinterpret_cast<FindWindowContext*>(lParam);

    // 表示可能なトップレベルウィンドウのみ対象（HotkeyP 元 findWindow 相当）
    if (!IsWindowVisible(hwnd)) return TRUE;
    if (GetWindow(hwnd, GW_OWNER) != nullptr) return TRUE;  // 所有者ありはサブウィンドウ
    if (GetWindowTextLengthW(hwnd) == 0) return TRUE;        // タイトル空はダイアログ等

    auto name = getProcessNameByHwnd(hwnd);
    if (name.empty()) return TRUE;

    if (toLowerCopy(name) == ctx->targetExe) {
        ctx->found = hwnd;
        return FALSE;  // 列挙終了
    }
    return TRUE;
}

} // namespace

HWND findWindowByExeName(std::wstring_view exeFullPathOrName)
{
    if (exeFullPathOrName.empty()) return nullptr;

    // 末尾のファイル名部分のみ抽出
    auto fileName = extractFileNameFromPath(exeFullPathOrName);

    FindWindowContext ctx;
    ctx.targetExe = toLowerCopy(fileName);

    EnumWindows(enumWindowsForExe, reinterpret_cast<LPARAM>(&ctx));
    return ctx.found;
}

bool bringWindowToFront(HWND hwnd) noexcept
{
    if (!hwnd || !IsWindow(hwnd)) return false;

    // 最小化状態なら復元
    if (IsIconic(hwnd)) {
        ShowWindow(hwnd, SW_RESTORE);
    }

    // SetForegroundWindow は通常、フォアグラウンドロックの制約があり呼び出し失敗するケースあり
    // AttachThreadInput で一時的にスレッド入力をアタッチして強制前面化（HotkeyP 互換）
    DWORD foreThread = GetWindowThreadProcessId(GetForegroundWindow(), nullptr);
    DWORD targetThread = GetWindowThreadProcessId(hwnd, nullptr);
    DWORD currentThread = GetCurrentThreadId();

    if (foreThread != currentThread) {
        AttachThreadInput(currentThread, foreThread, TRUE);
    }
    if (targetThread != currentThread) {
        AttachThreadInput(currentThread, targetThread, TRUE);
    }

    BringWindowToTop(hwnd);
    BOOL result = SetForegroundWindow(hwnd);

    if (foreThread != currentThread) {
        AttachThreadInput(currentThread, foreThread, FALSE);
    }
    if (targetThread != currentThread) {
        AttachThreadInput(currentThread, targetThread, FALSE);
    }

    return result != FALSE;
}

} // namespace imeindicator::win32
