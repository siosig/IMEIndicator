// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.Models;

/// <summary>
/// 移植元 src/cpp/win32/UnicodeUtil.cpp の <c>isWhitespace</c> / <c>trim</c> / <c>stripExeAndTrim</c>
/// と同じ空白判定・トリム挙動を提供する。半角空白 / タブ / LF / CR / 垂直タブ(\v) / 改頁(\f) /
/// 全角空白（U+3000）のみを空白とみなす。.NET の <see cref="string.Trim()"/> が対象とする
/// Unicode 空白文字集合（例: U+00A0 も含む）とは範囲が異なるため、C++ 版と挙動を一致させるために
/// 独自実装する。<see cref="Models.ProcessPriorityRule"/> と <see cref="AppSettings"/> の双方から
/// 使う共通処理のためここに集約する（重複実装による定義ズレを防ぐ）。
/// </summary>
internal static class CppTrim
{
    public static bool IsWhitespace(char c) =>
        c is ' ' or '\t' or '\n' or '\r' or '\v' or '\f' or '　';

    /// <summary>両端から <see cref="IsWhitespace"/> に一致する文字を取り除く。</summary>
    public static string Trim(string src)
    {
        int begin = 0;
        int end = src.Length;
        while (begin < end && IsWhitespace(src[begin]))
        {
            begin++;
        }
        while (begin < end && IsWhitespace(src[end - 1]))
        {
            end--;
        }
        return src[begin..end];
    }

    /// <summary>
    /// 末尾の ".exe"（大小無視）と両端の空白を除去する。移植元: win32::stripExeAndTrim()。
    /// </summary>
    public static string StripExeAndTrim(string name)
    {
        string trimmed = Trim(name);
        const string exeSuffix = ".exe";
        if (trimmed.Length >= exeSuffix.Length)
        {
            string tail = trimmed[^exeSuffix.Length..];
            if (string.Equals(tail, exeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^exeSuffix.Length];

                // 拡張子前の空白も除去（末尾側のみ。移植元の while ループと同じ挙動）
                int end = trimmed.Length;
                while (end > 0 && IsWhitespace(trimmed[end - 1]))
                {
                    end--;
                }
                trimmed = trimmed[..end];
            }
        }
        return trimmed;
    }
}
