/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - 音量制御コマンド実装
 Core Audio (IAudioEndpointVolume) のみ使用。WinMM Mixer API は廃止。
 WIL wil::com_ptr で COM ポインタを RAII 管理。
*/

#include "VolumeCommands.h"
#include <mmdeviceapi.h>
#include <algorithm>
#include <charconv>
#include <cwchar>
#include <string>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// --- エラーカテゴリ ---

namespace {

struct VolumeErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override {
        return "VolumeError";
    }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<VolumeError>(ev)) {
        case VolumeError::ComNotInitialized: return "COM not initialized";
        case VolumeError::DeviceNotFound:    return "Audio device not found";
        case VolumeError::ApiCallFailed:     return "Audio API call failed";
        }
        return "Unknown VolumeError";
    }
};

const VolumeErrorCategory& volumeErrorCategory() noexcept {
    static VolumeErrorCategory cat;
    return cat;
}

// デフォルト音量エンドポイントを取得
[[nodiscard]] std::expected<wil::com_ptr<IAudioEndpointVolume>, VolumeError>
getEndpointVolume() noexcept {
    wil::com_ptr<IMMDeviceEnumerator> enumerator;
    HRESULT hr = CoCreateInstance(
        __uuidof(MMDeviceEnumerator),
        nullptr,
        CLSCTX_ALL,
        IID_PPV_ARGS(enumerator.put())
    );
    if (FAILED(hr)) {
        return std::unexpected(
            hr == CO_E_NOTINITIALIZED
                ? VolumeError::ComNotInitialized
                : VolumeError::ApiCallFailed
        );
    }

    wil::com_ptr<IMMDevice> device;
    hr = enumerator->GetDefaultAudioEndpoint(eRender, eConsole, device.put());
    if (FAILED(hr)) {
        return std::unexpected(VolumeError::DeviceNotFound);
    }

    wil::com_ptr<IAudioEndpointVolume> vol;
    hr = device->Activate(
        __uuidof(IAudioEndpointVolume),
        CLSCTX_ALL,
        nullptr,
        reinterpret_cast<void**>(vol.put())
    );
    if (FAILED(hr)) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }

    return vol;
}

} // namespace

// --- エラーコード生成 ---

std::error_code make_error_code(VolumeError e) {
    return {static_cast<int>(e), volumeErrorCategory()};
}

// --- 公開 API ---

std::expected<void, VolumeError> adjustVolume(float delta) noexcept {
    auto result = getEndpointVolume();
    if (!result) return std::unexpected(result.error());

    float current = 0.0f;
    if (FAILED(result.value()->GetMasterVolumeLevelScalar(&current))) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }

    const float newLevel = std::clamp(current + delta, 0.0f, 1.0f);
    if (FAILED(result.value()->SetMasterVolumeLevelScalar(newLevel, nullptr))) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }
    return {};
}

std::expected<void, VolumeError> setVolume(float level) noexcept {
    auto result = getEndpointVolume();
    if (!result) return std::unexpected(result.error());

    const float clamped = std::clamp(level, 0.0f, 1.0f);
    if (FAILED(result.value()->SetMasterVolumeLevelScalar(clamped, nullptr))) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }
    return {};
}

std::expected<float, VolumeError> getVolume() noexcept {
    auto result = getEndpointVolume();
    if (!result) return std::unexpected(result.error());

    float level = 0.0f;
    if (FAILED(result.value()->GetMasterVolumeLevelScalar(&level))) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }
    return level;
}

std::expected<void, VolumeError> toggleMute() noexcept {
    auto result = getEndpointVolume();
    if (!result) return std::unexpected(result.error());

    BOOL muted = FALSE;
    if (FAILED(result.value()->GetMute(&muted))) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }
    if (FAILED(result.value()->SetMute(!muted, nullptr))) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }
    return {};
}

std::expected<void, VolumeError> setMute(bool mute) noexcept {
    auto result = getEndpointVolume();
    if (!result) return std::unexpected(result.error());

    if (FAILED(result.value()->SetMute(mute ? TRUE : FALSE, nullptr))) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }
    return {};
}

std::expected<bool, VolumeError> getMute() noexcept {
    auto result = getEndpointVolume();
    if (!result) return std::unexpected(result.error());

    BOOL muted = FALSE;
    if (FAILED(result.value()->GetMute(&muted))) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }
    return muted != FALSE;
}

std::expected<void, VolumeError>
executeVolumeCommand(std::wstring_view param) noexcept {
    // param 形式:
    //   "M"       → ミュート切替
    //   "V+NN"    → 音量を NN% 上げる
    //   "V-NN"    → 音量を NN% 下げる
    //   "VNN"     → 音量を NN% に設定 (0-100)

    if (param.empty()) {
        return std::unexpected(VolumeError::ApiCallFailed);
    }

    // ミュート切替
    if (param == L"M") {
        return toggleMute();
    }

    // 音量コマンド ("V" で始まる)
    if (param.size() < 2 || param[0] != L'V') {
        return std::unexpected(VolumeError::ApiCallFailed);
    }

    const wchar_t sign = param[1];

    if (sign == L'+' || sign == L'-') {
        // 相対変更: "V+5" / "V-3"
        if (param.size() < 3) {
            return std::unexpected(VolumeError::ApiCallFailed);
        }
        // wstring_view → narrow string で from_chars を使用
        const std::wstring numStr(param.substr(2));
        try {
            const int pct = std::stoi(numStr);
            const float delta = static_cast<float>(pct) / 100.0f;
            return adjustVolume(sign == L'+' ? delta : -delta);
        } catch (...) {
            return std::unexpected(VolumeError::ApiCallFailed);
        }
    } else {
        // 絶対設定: "V50" → 50%
        const std::wstring numStr(param.substr(1));
        try {
            const int pct = std::stoi(numStr);
            const float level = std::clamp(static_cast<float>(pct) / 100.0f, 0.0f, 1.0f);
            return setVolume(level);
        } catch (...) {
            return std::unexpected(VolumeError::ApiCallFailed);
        }
    }
}

} // namespace imeindicator::services::hotkey
