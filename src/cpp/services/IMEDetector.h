#pragma once

#include "../models/LanguageInfo.h"

#include <optional>
#include <string>
#include <unordered_set>
#include <utility>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace imeindicator::services {

// IME 検出の結果。state は最新の言語/ON-OFF、reliableStatus は API 取得が成功したか。
// 既存 [IMEDetector_Common.cs] の戻り値タプル相当。
struct IMEDetectionResult {
    models::LanguageInfo state;
    bool reliableStatus{false};
};

// IME 状態検出（IMM32 経由 + フォアグラウンドウィンドウ追跡）。
// PixelIMEDetector との二段検出フローは IMEMonitor 側で組み合わせる。
//
// 既存 [IMEDetector_Common.cs] / [IMEDetector_Japanese.cs] と等価動作。
struct IMEDetector {
    // IME 状態取得が信頼できないターミナル系プロセス名（拡張子なし、大小無視）。
    // powershell / pwsh / cmd / WindowsTerminal / conhost / wezterm-gui / alacritty / mintty
    static bool isTerminalProcess(const std::wstring& processNameNoExt) noexcept;

    // 言語 ID（HKL 下位 16 ビット）→ LanguageType。0x0411=日本語、それ以外は英語。
    static models::LanguageType getLanguageType(int langId) noexcept;

    // IMM32 経由の二段判定で IME ON/OFF を取得する。
    //   1) ImmGetDefaultIMEWnd → SendMessageTimeout(WM_IME_CONTROL, IMC_GETOPENSTATUS)
    //   2) ImmGetContext → ImmGetOpenStatus（ハンドルは ImmReleaseContext で解放）
    //   3) hwndFocus でも試行
    // 戻り値: { isOpen, success }
    static std::pair<bool, bool> getIMEOpenStatusEx(HWND hwndFocus, HWND hwndForeground);

    // 現在の IME 状態（フォアグラウンドウィンドウのキーボードレイアウト + IME ON/OFF）。
    // ターミナルプロセスの場合、trackedLanguageForTerminal が指定されればその言語を採用。
    static IMEDetectionResult getCurrentIMEStateEx(
        std::optional<models::LanguageType> trackedLanguageForTerminal);
};

} // namespace imeindicator::services
