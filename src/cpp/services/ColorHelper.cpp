#include "ColorHelper.h"

#include <algorithm>
#include <cctype>
#include <charconv>
#include <cstdint>
#include <cstdio>
#include <system_error>

namespace imeindicator::services {

namespace {

constexpr D2D1_COLOR_F kGray{0.5f, 0.5f, 0.5f, 1.0f};

bool isAsciiHexDigit(char c) noexcept
{
    return (c >= '0' && c <= '9') ||
           (c >= 'a' && c <= 'f') ||
           (c >= 'A' && c <= 'F');
}

// 1 桁の hex を byte に変換（0xF→0xFF へ複製）
bool parseSingleHexNibble(char c, uint8_t& out) noexcept
{
    if (!isAsciiHexDigit(c)) return false;
    int v = 0;
    auto [ptr, ec] = std::from_chars(&c, &c + 1, v, 16);
    if (ec != std::errc{}) return false;
    out = static_cast<uint8_t>((v << 4) | v);
    return true;
}

// 2 桁の hex を byte に変換
bool parseHexByte(std::string_view two, uint8_t& out) noexcept
{
    if (two.size() != 2) return false;
    if (!isAsciiHexDigit(two[0]) || !isAsciiHexDigit(two[1])) return false;
    int v = 0;
    auto [ptr, ec] = std::from_chars(two.data(), two.data() + 2, v, 16);
    if (ec != std::errc{}) return false;
    out = static_cast<uint8_t>(v);
    return true;
}

uint8_t to255(float f) noexcept
{
    if (f <= 0.0f) return 0;
    if (f >= 1.0f) return 255;
    return static_cast<uint8_t>(f * 255.0f + 0.5f);
}

std::string_view trimAndStripHash(std::string_view s) noexcept
{
    while (!s.empty() && std::isspace(static_cast<unsigned char>(s.front()))) s.remove_prefix(1);
    while (!s.empty() && std::isspace(static_cast<unsigned char>(s.back()))) s.remove_suffix(1);
    if (!s.empty() && s.front() == '#') s.remove_prefix(1);
    return s;
}

} // namespace

D2D1_COLOR_F ColorHelper::parseColor(std::string_view hex) noexcept
{
    auto h = trimAndStripHash(hex);
    if (h.empty()) return kGray;

    uint8_t a = 255, r = 0, g = 0, b = 0;
    bool ok = false;

    switch (h.size()) {
        case 3:  // #RGB
            ok = parseSingleHexNibble(h[0], r) &&
                 parseSingleHexNibble(h[1], g) &&
                 parseSingleHexNibble(h[2], b);
            break;
        case 4:  // #RGBA（既存 C# 版と同じく alpha は末尾）
            ok = parseSingleHexNibble(h[0], r) &&
                 parseSingleHexNibble(h[1], g) &&
                 parseSingleHexNibble(h[2], b) &&
                 parseSingleHexNibble(h[3], a);
            break;
        case 6:  // #RRGGBB
            ok = parseHexByte(h.substr(0, 2), r) &&
                 parseHexByte(h.substr(2, 2), g) &&
                 parseHexByte(h.substr(4, 2), b);
            break;
        case 8:  // #AARRGGBB
            ok = parseHexByte(h.substr(0, 2), a) &&
                 parseHexByte(h.substr(2, 2), r) &&
                 parseHexByte(h.substr(4, 2), g) &&
                 parseHexByte(h.substr(6, 2), b);
            break;
        default:
            break;
    }
    if (!ok) return kGray;
    return {r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f};
}

std::string ColorHelper::toArgbHex(const D2D1_COLOR_F& color)
{
    char buf[16];
    std::snprintf(buf, sizeof(buf), "#%02X%02X%02X%02X",
                  to255(color.a), to255(color.r), to255(color.g), to255(color.b));
    return buf;
}

std::string ColorHelper::toRgbHex(const D2D1_COLOR_F& color)
{
    char buf[16];
    std::snprintf(buf, sizeof(buf), "#%02X%02X%02X",
                  to255(color.r), to255(color.g), to255(color.b));
    return buf;
}

bool ColorHelper::isValidHexColor(std::string_view hex) noexcept
{
    auto h = trimAndStripHash(hex);
    if (h.empty()) return false;
    if (h.size() != 3 && h.size() != 4 && h.size() != 6 && h.size() != 8) return false;
    return std::all_of(h.begin(), h.end(), [](char c) { return isAsciiHexDigit(c); });
}

} // namespace imeindicator::services
