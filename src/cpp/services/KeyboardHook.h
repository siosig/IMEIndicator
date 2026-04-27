#pragma once

#include <functional>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// 低レベルキーボードフック（WH_KEYBOARD_LL）で IME 切替キーと Win+Space を検出する。
// 既存 [KeyboardHook.cs] と等価。LowLevelKeyboardProc は引数で context を受け取れないため、
// プロセス内で 1 つのフックハンドルを共有するシングルトンとして実装する。
class KeyboardHook {
public:
    using ImeKeyCallback = std::function<void(int vkCode)>;
    using LanguageSwitchCallback = std::function<void()>;

    static KeyboardHook& instance() noexcept;

    void setImeKeyCallback(ImeKeyCallback cb) { imeKeyCallback_ = std::move(cb); }
    void setLanguageSwitchCallback(LanguageSwitchCallback cb) { languageSwitchCallback_ = std::move(cb); }

    bool start();
    void stop() noexcept;

    bool isRunning() const noexcept { return hook_ != nullptr; }

private:
    KeyboardHook() = default;
    ~KeyboardHook();
    KeyboardHook(const KeyboardHook&) = delete;
    KeyboardHook& operator=(const KeyboardHook&) = delete;

    static LRESULT CALLBACK hookProc(int nCode, WPARAM wParam, LPARAM lParam);
    void handleKeyDown(int vkCode);

    HHOOK hook_{nullptr};
    ImeKeyCallback imeKeyCallback_;
    LanguageSwitchCallback languageSwitchCallback_;
};

} // namespace imeindicator::services
