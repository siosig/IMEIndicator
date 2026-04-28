/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - CD/メディアコマンド実装
 MCI (mciSendStringW) でCDトレイ操作。メディアキーは VK_MEDIA_* で送信。
*/

#include "MediaCommands.h"
#include <mmsystem.h>
#include <string>

#pragma comment(lib, "winmm.lib")

namespace {

struct MediaErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override { return "MediaError"; }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<MediaError>(ev)) {
        case MediaError::ApiCallFailed:  return "Media API call failed";
        case MediaError::DeviceNotFound: return "Media device not found";
        }
        return "Unknown MediaError";
    }
};

const MediaErrorCategory& mediaErrorCategory() noexcept {
    static MediaErrorCategory cat;
    return cat;
}

} // namespace

std::error_code make_error_code(MediaError e) {
    return {static_cast<int>(e), mediaErrorCategory()};
}

std::expected<void, MediaError> ejectCD(std::wstring_view driveLetter) noexcept {
    // MCI でCDトレイを開く
    std::wstring openCmd = L"open ";
    if (!driveLetter.empty()) {
        openCmd += driveLetter;
        if (openCmd.back() != L':') openCmd += L':';
    } else {
        openCmd += L"cdaudio";
    }
    openCmd += L" type cdaudio alias cdDrive";

    mciSendStringW(openCmd.c_str(), nullptr, 0, nullptr);

    const MCIERROR err = mciSendStringW(
        L"set cdDrive door open", nullptr, 0, nullptr);
    mciSendStringW(L"close cdDrive", nullptr, 0, nullptr);

    if (err != 0) return std::unexpected(MediaError::DeviceNotFound);
    return {};
}

std::expected<void, MediaError> closeCD(std::wstring_view driveLetter) noexcept {
    std::wstring openCmd = L"open ";
    if (!driveLetter.empty()) {
        openCmd += driveLetter;
        if (openCmd.back() != L':') openCmd += L':';
    } else {
        openCmd += L"cdaudio";
    }
    openCmd += L" type cdaudio alias cdDrive";

    mciSendStringW(openCmd.c_str(), nullptr, 0, nullptr);

    const MCIERROR err = mciSendStringW(
        L"set cdDrive door closed", nullptr, 0, nullptr);
    mciSendStringW(L"close cdDrive", nullptr, 0, nullptr);

    if (err != 0) return std::unexpected(MediaError::DeviceNotFound);
    return {};
}

std::expected<void, MediaError> sendMediaKey(WORD vk) noexcept {
    INPUT inputs[2]{};
    inputs[0].type   = INPUT_KEYBOARD;
    inputs[0].ki.wVk = vk;
    inputs[1].type   = INPUT_KEYBOARD;
    inputs[1].ki.wVk = vk;
    inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;

    if (SendInput(2, inputs, sizeof(INPUT)) == 0) {
        return std::unexpected(MediaError::ApiCallFailed);
    }
    return {};
}

std::expected<void, MediaError>
executeMediaCommand(int cmdId, std::wstring_view param) noexcept {
    switch (cmdId) {
    case 0:  return ejectCD(param);
    case 1:  return closeCD(param);
    case 39: return sendMediaKey(VK_MEDIA_PLAY_PAUSE);
    case 40: return sendMediaKey(VK_MEDIA_STOP);
    case 41: return sendMediaKey(VK_MEDIA_NEXT_TRACK);
    case 42: return sendMediaKey(VK_MEDIA_PREV_TRACK);
    case 100:return ejectCD(param);
    default:
        return std::unexpected(MediaError::ApiCallFailed);
    }
}
