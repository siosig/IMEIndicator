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
 HotkeyP モダン再設計 - テキスト/マクロコマンド
 コマンド 27・67・94
 parseMacro / PasteTextData ロジックを std::wstring に統一
*/

#include <windows.h>
#include <expected>
#include <string>
#include <string_view>
#include <system_error>
#include <vector>

enum class TextError {
    EmptyText,
    ClipboardFailed,
    ApiCallFailed,
};

std::error_code make_error_code(TextError e);

// テキストをクリップボードに貼り付け（SendInput で入力）
[[nodiscard]] std::expected<void, TextError>
pasteText(std::wstring_view text) noexcept;

// テキストをクリップボードに設定
[[nodiscard]] std::expected<void, TextError>
setClipboardText(std::wstring_view text) noexcept;

// マクロ文字列を解析して INPUT 列を生成
// サポートする制御シーケンス:
//   {ENTER} {TAB} {ESC} {BACKSPACE} {DELETE}
//   {UP} {DOWN} {LEFT} {RIGHT}
//   {F1}〜{F24}
//   {CTRL} {ALT} {SHIFT} + 任意キー（例: {CTRL}a）
[[nodiscard]] std::vector<INPUT>
parseMacroToInputs(std::wstring_view macro);

// マクロを実行（SendInput）
[[nodiscard]] std::expected<void, TextError>
executeMacro(std::wstring_view macro) noexcept;

// テキストコマンドを実行
[[nodiscard]] std::expected<void, TextError>
executeTextCommand(int cmdId, std::wstring_view param) noexcept;
