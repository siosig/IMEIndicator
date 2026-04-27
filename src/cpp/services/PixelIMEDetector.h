#pragma once

#include "../models/LanguageInfo.h"

#include <chrono>
#include <cstdint>
#include <mutex>
#include <optional>
#include <vector>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// Windows 入力インジケーターの描画内容からピクセル判定で IME ON/OFF を検出する。
// 既存 [PixelIMEDetector.cs] を C++ 化したもの。シングルトンで GDI/COM リソースを保持する。
//
// アルゴリズム概要:
//   1) UI Automation で Shell_TrayWnd 配下の「入力インジケーター」/「Input indicator」を検索
//      （矩形を 60 秒キャッシュ）
//   2) BitBlt + GetDIBits でその矩形のピクセルを取得
//   3) 周囲 5px をサンプリングして背景輝度を算出
//   4) 内側領域でテキストピクセル比率を計算し、8.5% 超で IME ON と判定
class PixelIMEDetector {
public:
    struct Timing {
        std::optional<bool> isOn;
        double getRectMs{0.0};
        double analyzeMs{0.0};
        double totalMs{0.0};
    };

    static PixelIMEDetector& instance();

    // キャッシュ付き検出（200ms キャッシュ）。日本語のみ対応。
    std::optional<bool> detectIMEState(models::LanguageType language);

    // キャッシュなしの検出（時間計測付き）。各呼び出しで UI Automation/GDI を実行する。
    Timing detectIMEStateTimed(models::LanguageType language);

    // 矩形・結果キャッシュをクリア（テスト用）。
    void clearCache();

    ~PixelIMEDetector();

private:
    PixelIMEDetector() = default;
    PixelIMEDetector(const PixelIMEDetector&) = delete;
    PixelIMEDetector& operator=(const PixelIMEDetector&) = delete;

    RECT findIndicatorRect();
    RECT searchAndCacheIndicatorRect();
    bool analyzeIndicatorWithBitBlt(const RECT& rect, models::LanguageType language);
    bool analyzePixelData(const std::vector<uint8_t>& pixels,
                          int width, int height, int stride,
                          models::LanguageType language);
    bool ensureGdiResources(int width, int height);
    void releaseGdiResources();

    // 状態同期用ミューテックス（外部呼び出しは複数スレッドの可能性あり）。
    std::mutex stateMutex_;

    // 入力インジケーター矩形のキャッシュ
    RECT cachedIndicatorRect_{};
    bool hasCachedRect_{false};
    std::chrono::steady_clock::time_point lastIndicatorSearch_{};

    // 直近のピクセル判定結果のキャッシュ
    std::optional<bool> lastPixelResult_;
    std::chrono::steady_clock::time_point lastPixelCheck_{};

    // GDI リソース（ensureGdiResources で確保し、release で解放）
    HDC screenDC_{nullptr};
    HDC memDC_{nullptr};
    HBITMAP bitmap_{nullptr};
    HGDIOBJ oldBitmap_{nullptr};
    int gdiWidth_{0};
    int gdiHeight_{0};
    std::vector<uint8_t> pixelBuffer_;
};

} // namespace imeindicator::services
