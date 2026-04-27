#pragma once

#include "../models/LanguageInfo.h"

#include <atomic>
#include <chrono>
#include <functional>
#include <mutex>
#include <optional>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <threadpoolapiset.h>

namespace imeindicator::services {

class MouseTracker;

// IME 状態を統合的に監視するサービス。
// KeyboardHook + WinEventHook + MouseTracker を統合し、IMEDetector + PixelIMEDetector で
// 状態を判定する。30ms デバウンス、200ms 楽観的更新検証窓を備える。
//
// 既存 [IMEMonitor.cs] / [IMEMonitor.KeyboardEvents.cs] と等価動作。
class IMEMonitor {
public:
    using IMEStateCallback = std::function<void(const models::LanguageInfo&)>;
    using CursorPositionCallback = std::function<void(int x, int y)>;

    IMEMonitor();
    ~IMEMonitor();

    IMEMonitor(const IMEMonitor&) = delete;
    IMEMonitor& operator=(const IMEMonitor&) = delete;

    void setIMEStateCallback(IMEStateCallback cb) { imeStateCallback_ = std::move(cb); }
    void setCursorPositionCallback(CursorPositionCallback cb) { cursorPositionCallback_ = std::move(cb); }

    // 監視開始。フック類とトラッカーを起動し、初回チェックを実行する。
    bool start();

    // 監視停止。フック解除とリソース解放を行う。Dispose 兼用。
    void stop() noexcept;

    // ピクセル検証の周期（ミリ秒、0 で無効化）。AppSettings から渡す。
    void setPixelVerificationIntervalMs(int ms) noexcept;

    // 現在保持している最新の IME 状態（コールバック発火後の値）。
    models::LanguageInfo currentState() const noexcept;

private:
    // フックコールバック → デバウンスタイマーをアーム。
    void onTriggerFired();

    // デバウンス満了時にスレッドプールから呼ばれる。
    static void CALLBACK debounceTimerCallback(PTP_CALLBACK_INSTANCE,
                                               PVOID context,
                                               PTP_TIMER);
    void onDebounceFired();

    // 実際の IME チェック処理（多重実行防止つき）。
    void checkIMEState(bool forceUpdate);

    // KeyboardHook のコールバック先。
    void onIMEKeyPressed(int vkCode);
    void onLanguageSwitchDetected();

    // === 状態 ===
    std::atomic<bool> running_{false};
    std::atomic<int> isChecking_{0};

    mutable std::mutex stateMutex_;
    models::LanguageInfo lastState_{models::LanguageType::English, false};
    HWND lastForegroundWindow_{nullptr};
    bool trackedIMEState_{false};
    std::optional<models::LanguageType> trackedLanguageForTerminal_;

    int pixelVerificationIntervalMs_{2000};

    // 楽観的更新タイムスタンプ（GetTickCount64 ベース）
    std::atomic<uint64_t> optimisticUpdateTimestamp_{0};

    // デバウンスタイマー
    PTP_TIMER debounceTimer_{nullptr};
    static constexpr int kDebounceDelayMs = 30;
    static constexpr int kVerificationDelayMs = 200;

    IMEStateCallback imeStateCallback_;
    CursorPositionCallback cursorPositionCallback_;

    // MouseTracker は所有
    std::unique_ptr<MouseTracker> mouseTracker_;
};

} // namespace imeindicator::services

#include <memory>
