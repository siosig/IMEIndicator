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
    // 観察モード: IME 切替キーを検出してコールバックを呼ぶだけで、キーは消費しない（CallNextHookEx）。
    // 010-hotkeyp-merge 統合後は HotkeyP 由来の HookEngine（services/hotkey/HookEngine）も
    // 同じ WH_KEYBOARD_LL に並行登録される。両者は責務分離されており衝突しない:
    //   - 本フック (KeyboardHook): IME 切替キー（VK_KANJI/VK_KANA/VK_DBE_DBCSCHAR/...）の観察のみ
    //   - HookEngine: 上記 IME キーは InputRouter::isImeSwitchKey() でフィルタアウトされ、
    //                 ホットキー対象から除外される（spec FR-021 / research.md R-003）
    // フックチェーンは LIFO（後に登録した方が先に呼ばれる）。本フックはどちらの順序でも
    // 必ず CallNextHookEx で次に渡すため、HookEngine の動作を阻害しない。
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
