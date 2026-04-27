#include "IMEDetector.h"

#include "../win32/HwndUtil.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <imm.h>

#include <algorithm>
#include <array>

#pragma comment(lib, "imm32.lib")

namespace imeindicator::services {

namespace {

constexpr UINT WM_IME_CONTROL_LOCAL = 0x0283;
constexpr LPARAM IMC_GETOPENSTATUS_LOCAL = 0x0005;

// 既存 C# 版 IMEDetector_Common.TerminalProcesses と同一集合。
// 拡張子は除いた名前（例: powershell.exe → "powershell"）。
const std::array<std::wstring_view, 8> kTerminalProcesses = {
    L"powershell",
    L"pwsh",
    L"cmd",
    L"WindowsTerminal",
    L"conhost",
    L"wezterm-gui",
    L"alacritty",
    L"mintty",
};

// 大小無視で等価か比較
bool equalsIgnoreCase(std::wstring_view a, std::wstring_view b) noexcept
{
    if (a.size() != b.size()) return false;
    return ::CompareStringOrdinal(a.data(), static_cast<int>(a.size()),
                                   b.data(), static_cast<int>(b.size()),
                                   TRUE) == CSTR_EQUAL;
}

// 拡張子 .exe を取り除く（大小無視）
std::wstring stripExeExtension(const std::wstring& name)
{
    constexpr std::wstring_view kExt = L".exe";
    if (name.size() < kExt.size()) return name;
    auto suffix = std::wstring_view{name}.substr(name.size() - kExt.size());
    if (equalsIgnoreCase(suffix, kExt)) {
        return name.substr(0, name.size() - kExt.size());
    }
    return name;
}

} // namespace

bool IMEDetector::isTerminalProcess(const std::wstring& processNameNoExt) noexcept
{
    for (auto p : kTerminalProcesses) {
        if (equalsIgnoreCase(processNameNoExt, p)) return true;
    }
    return false;
}

models::LanguageType IMEDetector::getLanguageType(int langId) noexcept
{
    constexpr int kLangJapanese = 0x0411;
    return langId == kLangJapanese
               ? models::LanguageType::Japanese
               : models::LanguageType::English;
}

std::pair<bool, bool> IMEDetector::getIMEOpenStatusEx(HWND hwndFocus, HWND hwndForeground)
{
    // 方法 1: DefaultIMEWnd に WM_IME_CONTROL(IMC_GETOPENSTATUS) を投げる。
    // SendMessageTimeout(SMTO_ABORTIFHUNG, 100ms) で応答無しウィンドウを避ける。
    if (HWND imeWnd = ::ImmGetDefaultIMEWnd(hwndForeground); imeWnd) {
        DWORD_PTR result = 0;
        LRESULT sendResult = ::SendMessageTimeoutW(
            imeWnd,
            WM_IME_CONTROL_LOCAL,
            static_cast<WPARAM>(IMC_GETOPENSTATUS_LOCAL),
            0,
            SMTO_ABORTIFHUNG,
            100,
            &result);
        if (sendResult != 0) {
            return {result != 0, true};
        }
    }

    // 方法 2: ImmGetContext → ImmGetOpenStatus（hwndForeground）
    if (HIMC hImc = ::ImmGetContext(hwndForeground); hImc) {
        const bool isOpen = ::ImmGetOpenStatus(hImc) != FALSE;
        ::ImmReleaseContext(hwndForeground, hImc);
        return {isOpen, true};
    }

    // 方法 3: hwndFocus でも試行
    if (hwndFocus && hwndFocus != hwndForeground) {
        if (HIMC hImc = ::ImmGetContext(hwndFocus); hImc) {
            const bool isOpen = ::ImmGetOpenStatus(hImc) != FALSE;
            ::ImmReleaseContext(hwndFocus, hImc);
            return {isOpen, true};
        }
    }

    return {false, false};
}

IMEDetectionResult IMEDetector::getCurrentIMEStateEx(
    std::optional<models::LanguageType> trackedLanguageForTerminal)
{
    HWND hwndForeground = ::GetForegroundWindow();
    if (!hwndForeground) {
        return {{models::LanguageType::English, false}, false};
    }

    const auto processName = stripExeExtension(
        win32::getProcessNameByHwnd(hwndForeground));

    DWORD pid = 0;
    DWORD threadId = ::GetWindowThreadProcessId(hwndForeground, &pid);

    HWND hwndTarget = hwndForeground;
    DWORD focusThreadId = threadId;

    GUITHREADINFO guiInfo{};
    guiInfo.cbSize = sizeof(guiInfo);
    if (::GetGUIThreadInfo(threadId, &guiInfo)) {
        if (guiInfo.hwndFocus && guiInfo.hwndFocus != hwndForeground) {
            hwndTarget = guiInfo.hwndFocus;
            focusThreadId = ::GetWindowThreadProcessId(hwndTarget, nullptr);
        }
    }

    HKL hkl = ::GetKeyboardLayout(focusThreadId);
    const int langId = static_cast<int>(reinterpret_cast<INT_PTR>(hkl)) & 0xFFFF;

    auto [imeOpen, imeSuccess] = getIMEOpenStatusEx(hwndTarget, hwndForeground);

    auto language = getLanguageType(langId);
    if (isTerminalProcess(processName) && trackedLanguageForTerminal.has_value()) {
        language = trackedLanguageForTerminal.value();
    }

    // 日本語以外は IME OFF と同じ表示にする（既存 C# 版互換）。
    if (language != models::LanguageType::Japanese) {
        imeOpen = false;
    }

    return {{language, imeOpen}, imeSuccess};
}

} // namespace imeindicator::services
