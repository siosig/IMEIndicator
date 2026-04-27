#include "UnicodeUtil.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <algorithm>
#include <cwctype>

namespace imeindicator::win32 {

std::wstring utf8ToWide(std::string_view utf8)
{
    if (utf8.empty()) return {};

    const int utf8Len = static_cast<int>(utf8.size());
    const int wideLen = ::MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, utf8.data(), utf8Len, nullptr, 0);
    if (wideLen <= 0) return {};

    std::wstring result(static_cast<size_t>(wideLen), L'\0');
    const int converted = ::MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, utf8.data(), utf8Len,
        result.data(), wideLen);
    if (converted <= 0) return {};
    return result;
}

std::string wideToUtf8(std::wstring_view wide)
{
    if (wide.empty()) return {};

    const int wideLen = static_cast<int>(wide.size());
    const int utf8Len = ::WideCharToMultiByte(
        CP_UTF8, 0, wide.data(), wideLen, nullptr, 0, nullptr, nullptr);
    if (utf8Len <= 0) return {};

    std::string result(static_cast<size_t>(utf8Len), '\0');
    const int converted = ::WideCharToMultiByte(
        CP_UTF8, 0, wide.data(), wideLen,
        result.data(), utf8Len, nullptr, nullptr);
    if (converted <= 0) return {};
    return result;
}

int compareIgnoreCase(std::wstring_view lhs, std::wstring_view rhs)
{
    const int r = ::CompareStringOrdinal(
        lhs.data(), static_cast<int>(lhs.size()),
        rhs.data(), static_cast<int>(rhs.size()),
        TRUE /*bIgnoreCase*/);
    // CompareStringOrdinal は 1/2/3 を返す。0 は失敗
    return r - 2;  // CSTR_LESS_THAN=1, CSTR_EQUAL=2, CSTR_GREATER_THAN=3
}

namespace {
constexpr bool isWhitespace(wchar_t c)
{
    return c == L' ' || c == L'\t' || c == L'\n' || c == L'\r' ||
           c == L'\v' || c == L'\f' || c == L'　' /* 全角空白 */;
}
} // namespace

std::wstring trim(std::wstring_view src)
{
    auto begin = src.begin();
    auto end = src.end();
    while (begin != end && isWhitespace(*begin)) ++begin;
    while (begin != end && isWhitespace(*(end - 1))) --end;
    return std::wstring(begin, end);
}

std::wstring stripExeAndTrim(std::wstring_view name)
{
    std::wstring trimmed = trim(name);
    constexpr std::wstring_view kExe = L".exe";
    if (trimmed.size() >= kExe.size()) {
        std::wstring_view tail(trimmed.data() + (trimmed.size() - kExe.size()), kExe.size());
        if (compareIgnoreCase(tail, kExe) == 0) {
            trimmed.resize(trimmed.size() - kExe.size());
            // 拡張子前の空白も除去
            while (!trimmed.empty() && isWhitespace(trimmed.back())) {
                trimmed.pop_back();
            }
        }
    }
    return trimmed;
}

} // namespace imeindicator::win32
