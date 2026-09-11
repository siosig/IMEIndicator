// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.Services;

/// <summary>
/// 色のパース・変換ヘルパー。現行 C++ 版 src/cpp/services/ColorHelper.h / .cpp の移植
/// （電源モードのインジケーター色を HEX 文字列から解決する用途、T020）。
/// C++ 版は内部表現に Direct2D の D2D1_COLOR_F（0.0〜1.0 の float ARGB）を使うが、
/// C# 版は GDI+ 描画（System.Drawing）を使うため、内部表現は <see cref="Color"/>
/// （0〜255 の byte ARGB）とする。パース規則・既定色・HEX 書式は C++ 版と同一。
/// サポートする HEX 文字列フォーマット:
/// <list type="bullet">
/// <item>#RGB / #RGBA（短縮形、各桁を 2 回繰り返したのと等価）</item>
/// <item>#RRGGBB / #AARRGGBB（標準形、A は ALPHA）</item>
/// </list>
/// </summary>
public static class ColorHelper
{
    // 空文字・不正入力時のフォールバック色（現行 C++ 版 kGray と同じ: R=G=B=128, A=255）。
    private static readonly Color GrayFallback = Color.FromArgb(255, 128, 128, 128);

    /// <summary>
    /// HEX 文字列を <see cref="Color"/> に変換する。<c>null</c>・空文字・不正入力は灰色を返す。
    /// </summary>
    public static Color ParseColor(string? hex)
    {
        var h = TrimAndStripHash(hex);
        if (h.IsEmpty)
        {
            return GrayFallback;
        }

        byte a = 255, r = 0, g = 0, b = 0;
        bool ok;

        switch (h.Length)
        {
            case 3: // #RGB
                ok = TryParseSingleHexNibble(h[0], out r) &&
                     TryParseSingleHexNibble(h[1], out g) &&
                     TryParseSingleHexNibble(h[2], out b);
                break;
            case 4: // #RGBA（alpha は末尾。現行 C++ 版・既存 C# 版と同じ並び）
                ok = TryParseSingleHexNibble(h[0], out r) &&
                     TryParseSingleHexNibble(h[1], out g) &&
                     TryParseSingleHexNibble(h[2], out b) &&
                     TryParseSingleHexNibble(h[3], out a);
                break;
            case 6: // #RRGGBB
                ok = TryParseHexByte(h.Slice(0, 2), out r) &&
                     TryParseHexByte(h.Slice(2, 2), out g) &&
                     TryParseHexByte(h.Slice(4, 2), out b);
                break;
            case 8: // #AARRGGBB
                ok = TryParseHexByte(h.Slice(0, 2), out a) &&
                     TryParseHexByte(h.Slice(2, 2), out r) &&
                     TryParseHexByte(h.Slice(4, 2), out g) &&
                     TryParseHexByte(h.Slice(6, 2), out b);
                break;
            default:
                ok = false;
                break;
        }

        return ok ? Color.FromArgb(a, r, g, b) : GrayFallback;
    }

    /// <summary>
    /// <see cref="Color"/> を "#AARRGGBB"（大文字）へ変換する。
    /// </summary>
    public static string ToArgbHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// <see cref="Color"/> を "#RRGGBB"（大文字、alpha は破棄）へ変換する。
    /// </summary>
    public static string ToRgbHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// 有効な HEX カラー文字列か判定する（先頭 # は任意、3/4/6/8 桁の hex のみ許容）。
    /// </summary>
    public static bool IsValidHexColor(string? hex)
    {
        var h = TrimAndStripHash(hex);
        if (h.IsEmpty)
        {
            return false;
        }
        if (h.Length is not (3 or 4 or 6 or 8))
        {
            return false;
        }

        foreach (var c in h)
        {
            if (!IsAsciiHexDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    // 前後の空白を除去し、先頭が '#' なら取り除く（C++ 版 trimAndStripHash と同じ規則）。
    private static ReadOnlySpan<char> TrimAndStripHash(string? s)
    {
        var span = s is null ? ReadOnlySpan<char>.Empty : s.AsSpan().Trim();
        if (!span.IsEmpty && span[0] == '#')
        {
            span = span[1..];
        }
        return span;
    }

    private static bool IsAsciiHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    // 1 桁の hex を byte に変換する（0xF → 0xFF へ複製。C++ 版 parseSingleHexNibble と同じ）。
    private static bool TryParseSingleHexNibble(char c, out byte result)
    {
        if (!TryHexDigitValue(c, out var v))
        {
            result = 0;
            return false;
        }
        result = (byte)((v << 4) | v);
        return true;
    }

    // 2 桁の hex を byte に変換する（C++ 版 parseHexByte と同じ）。
    private static bool TryParseHexByte(ReadOnlySpan<char> two, out byte result)
    {
        result = 0;
        if (two.Length != 2 || !TryHexDigitValue(two[0], out var hi) || !TryHexDigitValue(two[1], out var lo))
        {
            return false;
        }
        result = (byte)((hi << 4) | lo);
        return true;
    }

    private static bool TryHexDigitValue(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
    }
}
