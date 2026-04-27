#pragma once

#include <string>
#include <string_view>

namespace imeindicator::win32 {

// UTF-8 ↔ UTF-16 変換ヘルパー。MultiByteToWideChar / WideCharToMultiByte ベース。
// 失敗時は空文字列を返す（spec での要求が単純なテキスト変換のため、ICU 不採用：R-020）。

// UTF-8 (std::string_view) → UTF-16 (std::wstring)
std::wstring utf8ToWide(std::string_view utf8);

// UTF-16 (std::wstring_view) → UTF-8 (std::string)
std::string wideToUtf8(std::wstring_view wide);

// 大小区別なし比較（CompareStringOrdinal を使用）
// returns: < 0, 0, > 0（lhs < rhs / lhs == rhs / lhs > rhs）
int compareIgnoreCase(std::wstring_view lhs, std::wstring_view rhs);

// 末尾の `.exe`（大小無視）を除去し、両端の空白をトリムする
std::wstring stripExeAndTrim(std::wstring_view name);

// 両端の空白（スペース・タブ・全角空白等）をトリム
std::wstring trim(std::wstring_view src);

} // namespace imeindicator::win32
