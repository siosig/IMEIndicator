#include "KeyboardHook.h"

namespace imeindicator::services {
namespace {

// 既存 [KeyboardHook.cs] と同一の VK 集合を使う。
constexpr int kVkKana       = 0x15;  // カナ/かな
constexpr int kVkKanji      = 0x19;  // 半角/全角
constexpr int kVkConvert    = 0x1C;  // 変換
constexpr int kVkNonConvert = 0x1D;  // 無変換
constexpr int kVkImeOn      = 0x16;  // IME ON
constexpr int kVkImeOff     = 0x1A;  // IME OFF
constexpr int kVkOemAuto    = 0xF3;  // 一部キーボードの IME OFF
constexpr int kVkOemEnlw    = 0xF4;  // 一部キーボードの IME ON

bool isImeKey(int vk) noexcept
{
    return vk == kVkKana    || vk == kVkKanji      ||
           vk == kVkConvert || vk == kVkNonConvert ||
           vk == kVkImeOn   || vk == kVkImeOff     ||
           vk == kVkOemAuto || vk == kVkOemEnlw;
}

bool isWinKeyDown() noexcept
{
    return (::GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
           (::GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
}

} // namespace

KeyboardHook& KeyboardHook::instance() noexcept
{
    static KeyboardHook s;
    return s;
}

KeyboardHook::~KeyboardHook()
{
    stop();
}

bool KeyboardHook::start()
{
    if (hook_) return true;
    hook_ = ::SetWindowsHookExW(WH_KEYBOARD_LL,
                                &KeyboardHook::hookProc,
                                ::GetModuleHandleW(nullptr),
                                0);
    return hook_ != nullptr;
}

void KeyboardHook::stop() noexcept
{
    if (hook_) {
        ::UnhookWindowsHookEx(hook_);
        hook_ = nullptr;
    }
}

LRESULT CALLBACK KeyboardHook::hookProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0) {
        const auto* kb = reinterpret_cast<const KBDLLHOOKSTRUCT*>(lParam);
        const bool keyDown = (wParam == WM_KEYDOWN) || (wParam == WM_SYSKEYDOWN);
        if (kb && keyDown) {
            instance().handleKeyDown(static_cast<int>(kb->vkCode));
        }
    }
    return ::CallNextHookEx(nullptr, nCode, wParam, lParam);
}

void KeyboardHook::handleKeyDown(int vkCode)
{
    if (isImeKey(vkCode)) {
        if (imeKeyCallback_) imeKeyCallback_(vkCode);
    }
    if (vkCode == VK_SPACE && isWinKeyDown()) {
        if (languageSwitchCallback_) languageSwitchCallback_();
    }
}

} // namespace imeindicator::services
