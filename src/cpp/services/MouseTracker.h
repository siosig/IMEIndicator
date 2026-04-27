#pragma once

#include <functional>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// マウスカーソル位置を一定間隔で監視し、移動時にコールバックを呼ぶ。
// 既存 [MouseTracker.cs] が WPF の CompositionTarget.Rendering（VBlank 同期）を
// 使っているのに対し、C++ 版は WPF を持たないため SetTimer(16ms ≒ 60Hz) ベースで
// 等価な滑らかさを得る。VBlank 完全同期は描画ウィンドウ側 IDirectComposition に委ねる。
class MouseTracker {
public:
    using Callback = std::function<void(int x, int y)>;

    MouseTracker() = default;
    ~MouseTracker();

    MouseTracker(const MouseTracker&) = delete;
    MouseTracker& operator=(const MouseTracker&) = delete;

    // コールバック設定（Start 前後どちらでも可）
    void setCallback(Callback cb) { callback_ = std::move(cb); }

    // 監視を開始。intervalMs はティック間隔（既定 16ms ≒ 60Hz）。
    bool start(unsigned intervalMs = 16);

    // 監視を停止（Dispose 兼用）。
    void stop() noexcept;

    bool isRunning() const noexcept { return timerWindow_ != nullptr; }

    // 内部用（friend にしたくないので public にしたが、外部から呼ばないこと）
    void onTimerTick();

private:
    HWND timerWindow_{nullptr};
    UINT_PTR timerId_{0};
    POINT lastPos_{};
    Callback callback_;
};

} // namespace imeindicator::services
