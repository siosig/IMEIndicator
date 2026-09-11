// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services.Hotkey;
using IMEIndicator.Services.Hotkey.Commands;

using Xunit;

namespace IMEIndicator.Tests.Hotkey.Commands;

/// <summary>
/// VolumeCommands の単体テスト。実機の Core Audio API を実際に呼び出すと副作用（音量変更）が
/// 生じるため、AdjustVolume / ToggleMute / SetVolume 経由で COM 層まで到達する経路は対象外とする。
/// 対象は以下の 2 種類のみ:
///   1. TryParseLeadingInt（純粋関数。VolumeCommands.cpp の std::stoi 相当のパース）。
///   2. ExecuteVolumeCommand のうち、バリデーションで早期に ApiCallFailed を返す経路
///      （"M" や有効な数値を含む入力は AdjustVolume/ToggleMute/SetVolume に到達するため対象外）。
/// </summary>
public sealed class VolumeCommandsTests
{
    [Theory]
    [InlineData("5", 5)]
    [InlineData("+5", 5)]
    [InlineData("-3", -3)]
    [InlineData("50", 50)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("2147483647", int.MaxValue)] // int.MaxValue ちょうど
    public void TryParseLeadingInt_ParsesValidLeadingInteger(string input, int expected)
    {
        bool ok = VolumeCommands.TryParseLeadingInt(input, out int value);

        Assert.True(ok);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("50abc", 50)]   // std::stoi と同様、末尾の非数字は無視する
    [InlineData("+50abc", 50)]
    [InlineData("-3xyz", -3)]
    [InlineData("5.5", 5)]      // 小数点は数字ではないため、"5" までで打ち切り
    public void TryParseLeadingInt_IgnoresTrailingNonDigits(string input, int expected)
    {
        bool ok = VolumeCommands.TryParseLeadingInt(input, out int value);

        Assert.True(ok);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("V50")]         // 先頭が符号でも数字でもない
    [InlineData("99999999999")] // int の範囲を超える（std::stoi の out_of_range 相当）
    [InlineData("-99999999999")]
    public void TryParseLeadingInt_FailsForInvalidInput(string input)
    {
        bool ok = VolumeCommands.TryParseLeadingInt(input, out int value);

        Assert.False(ok);
        Assert.Equal(0, value);
    }

    [Theory]
    [InlineData("")]     // 空文字列
    [InlineData("X")]    // 'V' で始まらず、2 文字未満
    [InlineData("AB")]   // 'V' で始まらない
    [InlineData("V")]    // 'V' のみ（2 文字未満）
    [InlineData("Vx")]   // 絶対値指定だが数字が続かない
    [InlineData("V+")]   // 符号のみで数字が無い（3 文字未満）
    [InlineData("V+x")]  // 符号の後が数字でない
    [InlineData("V-")]
    [InlineData("V-x")]
    public void ExecuteVolumeCommand_ReturnsApiCallFailed_ForMalformedInputWithoutTouchingAudioApi(string param)
    {
        // これらの入力はすべてバリデーションで弾かれ、AdjustVolume/ToggleMute/SetVolume
        // （実機の Core Audio API 呼び出し）に到達する前に ApiCallFailed を返す経路であることを
        // TryParseLeadingInt 側のテストと合わせて確認済み。
        ExecuteError? result = VolumeCommands.ExecuteVolumeCommand(param);

        Assert.Equal(ExecuteError.ApiCallFailed, result);
    }
}
