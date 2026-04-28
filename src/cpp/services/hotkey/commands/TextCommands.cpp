/*
 * Copyright (C) Petr Lastovicka (HotkeyP 4.11, https://hotkeyp.sourceforge.net/)
 * Copyright (C) 2026 IMEIndicator Project (Modified for IMEIndicator integration: namespace, AppSettings binding, ImeIndicatorCommands extension)
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License v2 or later.
 * See COPYING in the repository root for the full license text.
 */
/*
 HotkeyP モダン再設計 - テキスト/マクロコマンド実装
 parseMacro: 制御シーケンスを解析して SendInput で送信
*/

#include "TextCommands.h"
#include <cwctype>
#include <map>


#include "../../../models/hotkey/HotKeyEntry.h"

namespace imeindicator::services::hotkey {

// HotkeyP コア由来の型を短く参照するための using ディレクティブ（HotKeyEntry / Command / Category 等）
using namespace ::imeindicator::models::hotkey;

namespace {

struct TextErrorCategory : std::error_category {
    [[nodiscard]] const char* name() const noexcept override { return "TextError"; }
    [[nodiscard]] std::string message(int ev) const override {
        switch (static_cast<TextError>(ev)) {
        case TextError::EmptyText:       return "Empty text";
        case TextError::ClipboardFailed: return "Clipboard operation failed";
        case TextError::ApiCallFailed:   return "Text API call failed";
        }
        return "Unknown TextError";
    }
};

const TextErrorCategory& textErrorCategory() noexcept {
    static TextErrorCategory cat;
    return cat;
}

// 制御シーケンス名 → VK コードのマップ
const std::map<std::wstring, WORD>& controlKeyMap() {
    static const std::map<std::wstring, WORD> m = {
        {L"ENTER",     VK_RETURN},
        {L"TAB",       VK_TAB},
        {L"ESC",       VK_ESCAPE},
        {L"ESCAPE",    VK_ESCAPE},
        {L"BACKSPACE", VK_BACK},
        {L"DELETE",    VK_DELETE},
        {L"DEL",       VK_DELETE},
        {L"INSERT",    VK_INSERT},
        {L"HOME",      VK_HOME},
        {L"END",       VK_END},
        {L"PGUP",      VK_PRIOR},
        {L"PGDN",      VK_NEXT},
        {L"UP",        VK_UP},
        {L"DOWN",      VK_DOWN},
        {L"LEFT",      VK_LEFT},
        {L"RIGHT",     VK_RIGHT},
        {L"F1",        VK_F1},  {L"F2",  VK_F2},  {L"F3",  VK_F3},
        {L"F4",        VK_F4},  {L"F5",  VK_F5},  {L"F6",  VK_F6},
        {L"F7",        VK_F7},  {L"F8",  VK_F8},  {L"F9",  VK_F9},
        {L"F10",       VK_F10}, {L"F11", VK_F11}, {L"F12", VK_F12},
        {L"F13",       VK_F13}, {L"F14", VK_F14}, {L"F15", VK_F15},
        {L"F16",       VK_F16}, {L"F17", VK_F17}, {L"F18", VK_F18},
        {L"F19",       VK_F19}, {L"F20", VK_F20}, {L"F21", VK_F21},
        {L"F22",       VK_F22}, {L"F23", VK_F23}, {L"F24", VK_F24},
        {L"WIN",       VK_LWIN},
        {L"SPACE",     VK_SPACE},
        {L"APPS",      VK_APPS},
        // Phase 4 / US2 (T021) で追加された特殊キー
        {L"NUMLOCK",   VK_NUMLOCK},
        {L"CAPSLOCK",  VK_CAPITAL},
        {L"SCROLLLOCK",VK_SCROLL},
        {L"PRINTSCREEN", VK_SNAPSHOT},
        {L"PRTSC",     VK_SNAPSHOT},
        {L"PAUSE",     VK_PAUSE},
        {L"BREAK",     VK_PAUSE},
        // メディアキー（{KEYNAME} 形式での記述もサポート）
        {L"MEDIA_PLAY_PAUSE", VK_MEDIA_PLAY_PAUSE},
        {L"MEDIA_NEXT",       VK_MEDIA_NEXT_TRACK},
        {L"MEDIA_PREV",       VK_MEDIA_PREV_TRACK},
        {L"MEDIA_STOP",       VK_MEDIA_STOP},
        {L"VOL_UP",           VK_VOLUME_UP},
        {L"VOL_DOWN",         VK_VOLUME_DOWN},
        {L"VOL_MUTE",         VK_VOLUME_MUTE},
        {L"LAUNCH_MAIL",      VK_LAUNCH_MAIL},
        {L"LAUNCH_APP1",      VK_LAUNCH_APP1},
        {L"LAUNCH_APP2",      VK_LAUNCH_APP2},
        {L"LAUNCH_MEDIA",     VK_LAUNCH_MEDIA_SELECT},
    };
    return m;
}

void addKeyDownUp(std::vector<INPUT>& inputs, WORD vk) {
    INPUT inp{};
    inp.type       = INPUT_KEYBOARD;
    inp.ki.wVk     = vk;
    inp.ki.dwFlags = 0;
    inputs.push_back(inp);
    inp.ki.dwFlags = KEYEVENTF_KEYUP;
    inputs.push_back(inp);
}

void addKeyDown(std::vector<INPUT>& inputs, WORD vk) {
    INPUT inp{};
    inp.type       = INPUT_KEYBOARD;
    inp.ki.wVk     = vk;
    inp.ki.dwFlags = 0;
    inputs.push_back(inp);
}

void addKeyUp(std::vector<INPUT>& inputs, WORD vk) {
    INPUT inp{};
    inp.type       = INPUT_KEYBOARD;
    inp.ki.wVk     = vk;
    inp.ki.dwFlags = KEYEVENTF_KEYUP;
    inputs.push_back(inp);
}

void addUnicodeChar(std::vector<INPUT>& inputs, wchar_t ch) {
    INPUT inp{};
    inp.type         = INPUT_KEYBOARD;
    inp.ki.wScan     = static_cast<WORD>(ch);
    inp.ki.dwFlags   = KEYEVENTF_UNICODE;
    inputs.push_back(inp);
    inp.ki.dwFlags   = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
    inputs.push_back(inp);
}

} // namespace

std::error_code make_error_code(TextError e) {
    return {static_cast<int>(e), textErrorCategory()};
}

std::vector<INPUT> parseMacroToInputs(std::wstring_view macro) {
    std::vector<INPUT> inputs;
    inputs.reserve(macro.size() * 2);

    bool ctrl  = false;
    bool alt   = false;
    bool shift = false;
    bool win   = false;  // Phase 4 / US2 で追加

    size_t i = 0;
    while (i < macro.size()) {
        // Phase 4 / US2 (T021): エスケープシーケンス \\ \{ \} \n \t \media_*
        if (macro[i] == L'\\' && i + 1 < macro.size()) {
            const wchar_t next = macro[i + 1];
            if (next == L'\\' || next == L'{' || next == L'}') {
                addUnicodeChar(inputs, next);
                i += 2;
                continue;
            }
            if (next == L'n') { addKeyDownUp(inputs, VK_RETURN); i += 2; continue; }
            if (next == L't') { addKeyDownUp(inputs, VK_TAB);    i += 2; continue; }

            // \media_play_pause / \media_next / \media_prev / \media_stop
            // \launch_app1 / \launch_app2 / \launch_mail / \launch_media
            // 末尾までスキャンして識別子を抽出
            size_t cmdStart = i + 1;
            size_t cmdEnd   = cmdStart;
            while (cmdEnd < macro.size() &&
                   (iswalnum(macro[cmdEnd]) || macro[cmdEnd] == L'_')) {
                ++cmdEnd;
            }
            const std::wstring cmdName(macro.substr(cmdStart, cmdEnd - cmdStart));
            WORD vk = 0;
            if      (cmdName == L"media_play_pause") vk = VK_MEDIA_PLAY_PAUSE;
            else if (cmdName == L"media_next")       vk = VK_MEDIA_NEXT_TRACK;
            else if (cmdName == L"media_prev")       vk = VK_MEDIA_PREV_TRACK;
            else if (cmdName == L"media_stop")       vk = VK_MEDIA_STOP;
            else if (cmdName == L"launch_mail")      vk = VK_LAUNCH_MAIL;
            else if (cmdName == L"launch_app1")      vk = VK_LAUNCH_APP1;
            else if (cmdName == L"launch_app2")      vk = VK_LAUNCH_APP2;
            else if (cmdName == L"launch_media")     vk = VK_LAUNCH_MEDIA_SELECT;
            if (vk) {
                addKeyDownUp(inputs, vk);
                i = cmdEnd;
                continue;
            }
            // 認識できないエスケープ: バックスラッシュ自身として出力
            addUnicodeChar(inputs, L'\\');
            ++i;
            continue;
        }

        if (macro[i] == L'{') {
            // 閉じ括弧を探す
            size_t end = macro.find(L'}', i + 1);
            if (end == std::wstring_view::npos) {
                // 不正なシーケンス: そのまま文字として出力
                addUnicodeChar(inputs, macro[i]);
                ++i;
                continue;
            }
            const std::wstring token(macro.substr(i + 1, end - i - 1));

            // 修飾キー: 次のキーに適用
            if (token == L"CTRL" || token == L"CONTROL") {
                ctrl = true; i = end + 1; continue;
            }
            if (token == L"ALT") {
                alt  = true; i = end + 1; continue;
            }
            if (token == L"SHIFT") {
                shift = true; i = end + 1; continue;
            }
            // Phase 4 / US2 (T021): {WIN} 修飾子サポート
            if (token == L"WIN+" || token == L"LWIN+") {
                win = true; i = end + 1; continue;
            }

            // 通常の制御キー
            const auto& km = controlKeyMap();
            auto it = km.find(token);
            if (it != km.end()) {
                if (ctrl)  addKeyDown(inputs, VK_CONTROL);
                if (alt)   addKeyDown(inputs, VK_MENU);
                if (shift) addKeyDown(inputs, VK_SHIFT);
                if (win)   addKeyDown(inputs, VK_LWIN);

                addKeyDownUp(inputs, it->second);

                if (win)   addKeyUp(inputs, VK_LWIN);
                if (shift) addKeyUp(inputs, VK_SHIFT);
                if (alt)   addKeyUp(inputs, VK_MENU);
                if (ctrl)  addKeyUp(inputs, VK_CONTROL);
                ctrl = alt = shift = win = false;
            }
            i = end + 1;
        } else {
            // 通常文字
            const wchar_t ch = macro[i];
            if (ctrl)  addKeyDown(inputs, VK_CONTROL);
            if (alt)   addKeyDown(inputs, VK_MENU);
            if (shift) addKeyDown(inputs, VK_SHIFT);
            if (win)   addKeyDown(inputs, VK_LWIN);

            addUnicodeChar(inputs, ch);

            if (win)   addKeyUp(inputs, VK_LWIN);
            if (shift) addKeyUp(inputs, VK_SHIFT);
            if (alt)   addKeyUp(inputs, VK_MENU);
            if (ctrl)  addKeyUp(inputs, VK_CONTROL);
            ctrl = alt = shift = win = false;
            ++i;
        }
    }
    return inputs;
}

std::expected<void, TextError> executeMacro(std::wstring_view macro) noexcept {
    if (macro.empty()) return std::unexpected(TextError::EmptyText);

    auto inputs = parseMacroToInputs(macro);
    if (inputs.empty()) return {};

    const UINT sent = SendInput(
        static_cast<UINT>(inputs.size()),
        inputs.data(),
        sizeof(INPUT)
    );
    if (sent == 0) return std::unexpected(TextError::ApiCallFailed);
    return {};
}

std::expected<void, TextError> setClipboardText(std::wstring_view text) noexcept {
    if (!OpenClipboard(nullptr)) {
        return std::unexpected(TextError::ClipboardFailed);
    }
    EmptyClipboard();

    const size_t bytes = (text.size() + 1) * sizeof(wchar_t);
    HGLOBAL hMem = GlobalAlloc(GMEM_MOVEABLE, bytes);
    if (!hMem) {
        CloseClipboard();
        return std::unexpected(TextError::ApiCallFailed);
    }

    auto* dst = static_cast<wchar_t*>(GlobalLock(hMem));
    if (!dst) {
        GlobalFree(hMem);
        CloseClipboard();
        return std::unexpected(TextError::ApiCallFailed);
    }
    wmemcpy(dst, text.data(), text.size());
    dst[text.size()] = L'\0';
    GlobalUnlock(hMem);

    if (!SetClipboardData(CF_UNICODETEXT, hMem)) {
        GlobalFree(hMem);
        CloseClipboard();
        return std::unexpected(TextError::ClipboardFailed);
    }
    CloseClipboard();
    return {};
}

std::expected<void, TextError> pasteText(std::wstring_view text) noexcept {
    if (text.empty()) return std::unexpected(TextError::EmptyText);

    // クリップボードにセットして Ctrl+V で貼り付け
    auto result = setClipboardText(text);
    if (!result) return result;

    INPUT inputs[4]{};
    inputs[0].type = INPUT_KEYBOARD;
    inputs[0].ki.wVk = VK_CONTROL;
    inputs[1].type = INPUT_KEYBOARD;
    inputs[1].ki.wVk = 'V';
    inputs[2].type = INPUT_KEYBOARD;
    inputs[2].ki.wVk = 'V';
    inputs[2].ki.dwFlags = KEYEVENTF_KEYUP;
    inputs[3].type = INPUT_KEYBOARD;
    inputs[3].ki.wVk = VK_CONTROL;
    inputs[3].ki.dwFlags = KEYEVENTF_KEYUP;

    SendInput(4, inputs, sizeof(INPUT));
    return {};
}

std::expected<void, TextError>
executeTextCommand(int cmdId, std::wstring_view param) noexcept {
    switch (cmdId) {
    case 27: return executeMacro(param);
    case 67: return pasteText(param);
    case 94: return setClipboardText(param);
    default:
        return std::unexpected(TextError::ApiCallFailed);
    }
}

} // namespace imeindicator::services::hotkey
