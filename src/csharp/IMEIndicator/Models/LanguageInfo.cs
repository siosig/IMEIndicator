// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.Models;

/// <summary>
/// IME が想定する入力言語の種別。
/// 現行 C++ 版 src/cpp/models/LanguageInfo.h の LanguageType と等価（English, Japanese の 2 値）。
/// data-model.md 上は Other を含む 3 値の想定だが、現行ロジックは 2 値のみ参照しているため、
/// C++ 版と同様に Other は「Japanese 以外」として暗黙に扱う（拡張時に値を追加可能）。
/// </summary>
public enum LanguageType
{
    /// <summary>英語（半角英数）入力。</summary>
    English = 0,

    /// <summary>日本語入力。</summary>
    Japanese = 1,
}

/// <summary>
/// IME 状態を表す不変の値オブジェクト（現在の入力言語と IME の ON/OFF）。
/// 現行 C++ 版 src/cpp/models/LanguageInfo.h の LanguageInfo 構造体と等価。
/// record struct の値等価（Equals / GetHashCode / == / !=）を状態変化検出に利用する。
/// フックコールバックやポーリングの比較対象になり得るため、ヒープ割り当てのない
/// readonly record struct（不変の値型）として定義する。
/// </summary>
/// <param name="Language">現在の入力言語。</param>
/// <param name="IsImeOn">IME が ON かどうか。</param>
public readonly record struct LanguageInfo(LanguageType Language, bool IsImeOn);
