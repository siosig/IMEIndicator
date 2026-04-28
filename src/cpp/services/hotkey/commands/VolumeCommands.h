#pragma once
/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - 音量制御コマンド（Core Audio のみ）
 IAudioEndpointVolume 使用。WinMM Mixer API は廃止。
 WIL wil::com_ptr で COM ポインタを RAII 管理。
*/

#include <windows.h>
#include <endpointvolume.h>
#include <wil/com.h>
#include <expected>
#include <string_view>
#include <system_error>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

// 音量制御エラー
enum class VolumeError {
    ComNotInitialized,
    DeviceNotFound,
    ApiCallFailed,
};

std::error_code make_error_code(VolumeError e);

// 音量を相対的に変更（delta: -1.0〜+1.0、0.0 なら変化なし）
[[nodiscard]] std::expected<void, VolumeError>
adjustVolume(float delta) noexcept;

// 音量をパーセンテージで設定（0.0〜1.0）
[[nodiscard]] std::expected<void, VolumeError>
setVolume(float level) noexcept;

// 現在の音量を取得（0.0〜1.0）
[[nodiscard]] std::expected<float, VolumeError>
getVolume() noexcept;

// ミュート状態を切り替え
[[nodiscard]] std::expected<void, VolumeError>
toggleMute() noexcept;

// ミュート状態を設定
[[nodiscard]] std::expected<void, VolumeError>
setMute(bool mute) noexcept;

// ミュート状態を取得
[[nodiscard]] std::expected<bool, VolumeError>
getMute() noexcept;

// 音量コマンドをパラメータ文字列から実行
// param 形式: "V" + delta (例: "V+5", "V-3", "V50")
[[nodiscard]] std::expected<void, VolumeError>
executeVolumeCommand(std::wstring_view param) noexcept;

} // namespace imeindicator::services::hotkey
