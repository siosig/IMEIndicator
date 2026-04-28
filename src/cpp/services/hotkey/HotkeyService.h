#pragma once
/*
 * Copyright (C) 2026 IMEIndicator Project
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyService: HookEngine + HotkeyManager + CommandExecutor のオーケストレーション層。
 App から start/stop/reload で操作し、メッセージループ経由で WM_HOTKEY_RAW_KBD/MOUSE を
 受け取って登録済みホットキーを実行する。

 設計方針（research.md R-002 / R-003 / plan.md Project Structure）:
 - HookEngine は HotkeyService が単独所有
 - HotkeyManager も同じく単独所有（AppSettings.hotkeySettings から loadFromHotkeySettings）
 - CommandExecutor は free 関数群（メインウィンドウハンドルを setMainWindowHandle で渡す）
 - App から呼ばれる reload() で HotkeyManager に新しい hotkeys を流し込み、HookEngine を再起動
*/

#include "HookEngine.h"
#include "HotkeyManager.h"
#include "../../models/hotkey/HotkeySettings.h"

#include <windows.h>

namespace imeindicator::services::hotkey {

class HotkeyService {
public:
    HotkeyService();
    ~HotkeyService();

    HotkeyService(const HotkeyService&) = delete;
    HotkeyService& operator=(const HotkeyService&) = delete;
    HotkeyService(HotkeyService&&) = delete;
    HotkeyService& operator=(HotkeyService&&) = delete;

    // メインウィンドウハンドルを保存し、CommandExecutor へ伝播。HookEngine を起動する。
    // settings は初期ロード対象。空でも可（後から reload で投入可能）。
    // 戻り値: 起動成功 = true。失敗時はログにエラー出力済み。
    bool start(HWND mainHwnd,
               const models::hotkey::HotkeySettings& settings,
               models::hotkey::HookMode mode = models::hotkey::HookMode::Auto);

    // フックスレッド停止 + ホットキー登録解除。デストラクタからも自動呼出し。
    void stop() noexcept;

    // 設定変更時に呼ぶ: HookEngine を一旦停止し、HotkeyManager を新しい settings で再ロード、
    // HookEngine を再起動する。設定ダイアログでの保存後に App から呼ばれる想定。
    bool reload(const models::hotkey::HotkeySettings& settings);

    // App::messageWndProc で WM_HOTKEY_RAW_KBD / WM_HOTKEY_RAW_MOUSE を受け取った際に呼ぶ。
    // 処理した場合は true を返す。
    [[nodiscard]] bool handleRawHookMessage(UINT msg, WPARAM wParam, LPARAM lParam);

    [[nodiscard]] bool isRunning() const noexcept { return running_; }

    // HotkeyManager への参照（テスト・UI 連携用）
    HotkeyManager& manager() noexcept { return manager_; }
    const HotkeyManager& manager() const noexcept { return manager_; }

private:
    // ホットキーが検出されたときの内部ハンドラ。HotkeyManager から該当エントリを取得し
    // CommandExecutor::executeCommand に dispatch する。
    void onHotkeyDetected(UINT vkey, DWORD scanCode, UINT modifiers);

    // autoStart=true のエントリを起動順に実行（FR-018）
    void executeAutoStartEntries();

    HotkeyManager manager_;
    HookEngine    engine_;
    HWND          mainHwnd_{nullptr};
    bool          running_{false};
    models::hotkey::HookMode currentMode_{models::hotkey::HookMode::Auto};
};

} // namespace imeindicator::services::hotkey
