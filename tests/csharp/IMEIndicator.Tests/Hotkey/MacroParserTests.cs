// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services.Hotkey;
using IMEIndicator.Services.Hotkey.Commands;

using Xunit;

namespace IMEIndicator.Tests.Hotkey;

/// <summary>
/// <see cref="TextCommands.ParseMacroToInputs"/> と <see cref="TextCommands.ParseRepeatAndSleep"/>
/// の単体テスト（マクロ構文解析）。
///
/// 【スコープについて】このテストファイルは純粋な解析ロジック（副作用なし）のみを対象とする。
/// <see cref="TextCommands.ExecuteMacro"/> / <see cref="TextCommands.PasteText"/> /
/// <see cref="TextCommands.SetClipboardText"/> は内部で実際に <c>SendInput</c>
/// （キーストローク合成）や <c>Clipboard</c>（クリップボード書き換え）を呼び出すため、
/// 非空の入力で呼ぶと実行環境（フォーカス中のウィンドウ・クリップボード内容）を実際に変更してしまう
/// うえ、CI 等の対話デスクトップの無い環境では <c>Clipboard</c> が STA スレッド要求で例外を投げる
/// 可能性もある。そのためここでは「空文字列 → 副作用なしで即座にエラーを返す」early-return 経路のみ
/// を対象とし、SendInput/Clipboard を実際に呼ぶ経路はテストしない。
///
/// 【プレースホルダ展開テストが無いことについて】tasks.md T054 は
/// <c>%date% %time% %computername% %username% %clipboard% %CR% %LF% %TAB% %|</c> の
/// プレースホルダ展開テストを要求しているが、実際の <c>TextCommands.cpp</c>
/// （現行 C++ 版、移植元）にはプレースホルダ展開ロジックが存在しない（TextCommands.cs の
/// クラス doc コメントに詳細根拠を記載）。実装が存在しない以上、プレースホルダ展開のテストは
/// 追加していない（存在しない機能を「実装した」と偽ることになるため）。
/// </summary>
public sealed class MacroParserTests
{
    // ---- ParseMacroToInputs: リテラル文字 ----

    [Fact]
    public void PlainText_EachCharBecomesUnicodeDownUpPair()
    {
        var steps = TextCommands.ParseMacroToInputs("ab");

        Assert.Equal(
            new[]
            {
                MacroInputStep.UnicodeDown('a'),
                MacroInputStep.UnicodeUp('a'),
                MacroInputStep.UnicodeDown('b'),
                MacroInputStep.UnicodeUp('b'),
            },
            steps);
    }

    [Fact]
    public void EmptyMacro_ProducesNoSteps()
    {
        var steps = TextCommands.ParseMacroToInputs(string.Empty);

        Assert.Empty(steps);
    }

    [Fact]
    public void JapaneseText_UsesUnicodeEvents()
    {
        // 日本語などのマルチバイト文字は KEYEVENTF_UNICODE（IsUnicode）で送出される。
        var steps = TextCommands.ParseMacroToInputs("あ");

        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.True(s.IsUnicode));
        Assert.Equal('あ', steps[0].UnicodeChar);
    }

    // ---- ParseMacroToInputs: 特殊キー {KEYNAME} ----

    [Theory]
    [InlineData("{ENTER}", (ushort)0x0D)]
    [InlineData("{TAB}", (ushort)0x09)]
    [InlineData("{ESC}", (ushort)0x1B)]
    [InlineData("{ESCAPE}", (ushort)0x1B)]
    [InlineData("{BACKSPACE}", (ushort)0x08)]
    [InlineData("{DELETE}", (ushort)0x2E)]
    [InlineData("{DEL}", (ushort)0x2E)]
    [InlineData("{F1}", (ushort)0x70)]
    [InlineData("{F24}", (ushort)0x87)]
    [InlineData("{PRTSC}", (ushort)0x2C)]
    [InlineData("{PRINTSCREEN}", (ushort)0x2C)]
    public void SpecialKey_ProducesKeyDownUpPairWithMappedVk(string macro, ushort expectedVk)
    {
        var steps = TextCommands.ParseMacroToInputs(macro);

        Assert.Equal(
            new[] { MacroInputStep.KeyDown(expectedVk), MacroInputStep.KeyUp(expectedVk) },
            steps);
    }

    [Fact]
    public void BareWin_IsRegularKeyPress_NotAModifier()
    {
        // {WIN}（末尾に + が無い）は controlKeyMap 経由の「VK_LWIN 単体キー押下」であり、
        // 修飾子としては働かない（macro-syntax.md の記述と実装が食い違う既知の点）。
        var steps = TextCommands.ParseMacroToInputs("{WIN}");

        Assert.Equal(
            new[] { MacroInputStep.KeyDown(0x5B), MacroInputStep.KeyUp(0x5B) },
            steps);
    }

    [Fact]
    public void UnknownToken_ProducesNoStepsAndDoesNotResetModifiers()
    {
        // {FOOBAR} は controlKeyMap にも修飾子名にも一致しないため無音でスキップされる。
        // かつ、直前の {CTRL} で立てた ctrl フラグはリセットされず次の 'a' に引き継がれる。
        var steps = TextCommands.ParseMacroToInputs("{CTRL}{FOOBAR}a");

        Assert.Equal(
            new[]
            {
                MacroInputStep.KeyDown(0x11), // VK_CONTROL
                MacroInputStep.UnicodeDown('a'),
                MacroInputStep.UnicodeUp('a'),
                MacroInputStep.KeyUp(0x11),
            },
            steps);
    }

    [Fact]
    public void UnclosedBrace_IsEmittedAsLiteralCharacter()
    {
        // 閉じ括弧の無い '{' はそのままリテラル文字として出力され、続く文字は通常文字として処理される。
        var steps = TextCommands.ParseMacroToInputs("{ENTER");

        Assert.Equal(
            new[]
            {
                MacroInputStep.UnicodeDown('{'),
                MacroInputStep.UnicodeUp('{'),
                MacroInputStep.UnicodeDown('E'),
                MacroInputStep.UnicodeUp('E'),
                MacroInputStep.UnicodeDown('N'),
                MacroInputStep.UnicodeUp('N'),
                MacroInputStep.UnicodeDown('T'),
                MacroInputStep.UnicodeUp('T'),
                MacroInputStep.UnicodeDown('E'),
                MacroInputStep.UnicodeUp('E'),
                MacroInputStep.UnicodeDown('R'),
                MacroInputStep.UnicodeUp('R'),
            },
            steps);
    }

    [Fact]
    public void LowercaseToken_IsCaseSensitiveAndNotRecognized()
    {
        // 現行 C++ 版 controlKeyMap / 修飾子判定は大文字小文字を区別する（std::wstring の厳密一致）。
        // "{ctrl}"（小文字）は修飾子にもcontrolKeyMapにもヒットしない。
        var steps = TextCommands.ParseMacroToInputs("{ctrl}a");

        Assert.Equal(
            new[] { MacroInputStep.UnicodeDown('a'), MacroInputStep.UnicodeUp('a') },
            steps);
    }

    // ---- ParseMacroToInputs: 修飾子 {CTRL} {ALT} {SHIFT} {WIN+} ----

    [Fact]
    public void CtrlModifier_WrapsNextCharacter()
    {
        var steps = TextCommands.ParseMacroToInputs("{CTRL}a");

        Assert.Equal(
            new[]
            {
                MacroInputStep.KeyDown(0x11), // VK_CONTROL down
                MacroInputStep.UnicodeDown('a'),
                MacroInputStep.UnicodeUp('a'),
                MacroInputStep.KeyUp(0x11), // VK_CONTROL up
            },
            steps);
    }

    [Fact]
    public void CtrlAltModifiers_NestInDownUpSymmetricOrder()
    {
        // 押下順: Ctrl → Alt →(対象キー)→ Alt解除 → Ctrl解除（宣言順にネストし、逆順で解除）。
        var steps = TextCommands.ParseMacroToInputs("{CTRL}{ALT}{DELETE}");

        Assert.Equal(
            new[]
            {
                MacroInputStep.KeyDown(0x11), // VK_CONTROL
                MacroInputStep.KeyDown(0x12), // VK_MENU (Alt)
                MacroInputStep.KeyDown(0x2E), // VK_DELETE
                MacroInputStep.KeyUp(0x2E),
                MacroInputStep.KeyUp(0x12),
                MacroInputStep.KeyUp(0x11),
            },
            steps);
    }

    [Fact]
    public void ModifierAppliesOnlyToNextElement_ThenAutoReleases()
    {
        // {CTRL}a の後、修飾フラグは解除されているため続く 'b' は無修飾。
        var steps = TextCommands.ParseMacroToInputs("{CTRL}ab");

        Assert.Equal(
            new[]
            {
                MacroInputStep.KeyDown(0x11),
                MacroInputStep.UnicodeDown('a'),
                MacroInputStep.UnicodeUp('a'),
                MacroInputStep.KeyUp(0x11),
                MacroInputStep.UnicodeDown('b'),
                MacroInputStep.UnicodeUp('b'),
            },
            steps);
    }

    [Fact]
    public void WinPlusModifier_RequiresTrailingPlusSign()
    {
        // {WIN+} / {LWIN+}（末尾に + が必須）のみが Win 修飾子として機能する。
        var steps = TextCommands.ParseMacroToInputs("{WIN+}a");

        Assert.Equal(
            new[]
            {
                MacroInputStep.KeyDown(0x5B), // VK_LWIN down
                MacroInputStep.UnicodeDown('a'),
                MacroInputStep.UnicodeUp('a'),
                MacroInputStep.KeyUp(0x5B), // VK_LWIN up
            },
            steps);
    }

    // ---- ParseMacroToInputs: エスケープシーケンス ----

    [Fact]
    public void NewlineEscape_IsEnterAlias()
    {
        var steps = TextCommands.ParseMacroToInputs(@"a\nb");

        // "a\nb"（バックスラッシュ n。改行文字ではない）→ a, Enter, b
        Assert.Equal(
            new[]
            {
                MacroInputStep.UnicodeDown('a'),
                MacroInputStep.UnicodeUp('a'),
                MacroInputStep.KeyDown(0x0D),
                MacroInputStep.KeyUp(0x0D),
                MacroInputStep.UnicodeDown('b'),
                MacroInputStep.UnicodeUp('b'),
            },
            steps);
    }

    [Fact]
    public void TabEscape_IsTabAlias()
    {
        var steps = TextCommands.ParseMacroToInputs(@"a\tb");

        Assert.Equal(
            new[]
            {
                MacroInputStep.UnicodeDown('a'),
                MacroInputStep.UnicodeUp('a'),
                MacroInputStep.KeyDown(0x09),
                MacroInputStep.KeyUp(0x09),
                MacroInputStep.UnicodeDown('b'),
                MacroInputStep.UnicodeUp('b'),
            },
            steps);
    }

    [Theory]
    [InlineData(@"\\", '\\')]
    [InlineData(@"\{", '{')]
    [InlineData(@"\}", '}')]
    public void EscapedLiteral_ProducesLiteralCharacter(string macro, char expectedChar)
    {
        var steps = TextCommands.ParseMacroToInputs(macro);

        Assert.Equal(
            new[] { MacroInputStep.UnicodeDown(expectedChar), MacroInputStep.UnicodeUp(expectedChar) },
            steps);
    }

    [Theory]
    [InlineData(@"\media_play_pause", (ushort)0xB3)]
    [InlineData(@"\media_next", (ushort)0xB0)]
    [InlineData(@"\media_prev", (ushort)0xB1)]
    [InlineData(@"\media_stop", (ushort)0xB2)]
    [InlineData(@"\launch_mail", (ushort)0xB4)]
    [InlineData(@"\launch_app1", (ushort)0xB6)]
    [InlineData(@"\launch_app2", (ushort)0xB7)]
    [InlineData(@"\launch_media", (ushort)0xB5)]
    public void MediaOrLaunchEscape_ProducesKeyDownUpPair(string macro, ushort expectedVk)
    {
        var steps = TextCommands.ParseMacroToInputs(macro);

        Assert.Equal(
            new[] { MacroInputStep.KeyDown(expectedVk), MacroInputStep.KeyUp(expectedVk) },
            steps);
    }

    [Fact]
    public void UnrecognizedEscape_EmitsLiteralBackslashThenReprocessesIdentifierAsLiteralChars()
    {
        // \xyz: 識別子 "xyz" はどの \command にも一致しないため、バックスラッシュ 1 文字だけを
        // リテラル出力し、"xyz" はその後 3 文字の通常文字として個別に再処理される
        // （識別子として読んだ範囲は消費されない＝現行 TextCommands.cpp の実装どおり）。
        var steps = TextCommands.ParseMacroToInputs(@"\xyz");

        Assert.Equal(
            new[]
            {
                MacroInputStep.UnicodeDown('\\'),
                MacroInputStep.UnicodeUp('\\'),
                MacroInputStep.UnicodeDown('x'),
                MacroInputStep.UnicodeUp('x'),
                MacroInputStep.UnicodeDown('y'),
                MacroInputStep.UnicodeUp('y'),
                MacroInputStep.UnicodeDown('z'),
                MacroInputStep.UnicodeUp('z'),
            },
            steps);
    }

    [Fact]
    public void TrailingBackslash_IsTreatedAsLiteralCharacter()
    {
        // 末尾の '\' は i+1 が範囲外のためエスケープ判定に入らず、通常文字として処理される。
        var steps = TextCommands.ParseMacroToInputs(@"a\");

        Assert.Equal(
            new[]
            {
                MacroInputStep.UnicodeDown('a'),
                MacroInputStep.UnicodeUp('a'),
                MacroInputStep.UnicodeDown('\\'),
                MacroInputStep.UnicodeUp('\\'),
            },
            steps);
    }

    // ---- ParseRepeatAndSleep: \rep ----

    [Fact]
    public void Rep_ParsesCountAndStripsPrefix()
    {
        MacroPlan plan = TextCommands.ParseRepeatAndSleep(@"\rep 3 abc");

        Assert.Equal(3, plan.RepeatCount);
        Assert.Equal(new[] { new MacroSegment("abc", 0) }, plan.Segments);
    }

    [Theory]
    [InlineData(@"\rep 0 a", 1)]   // 下限未満 → 1 にクランプ
    [InlineData(@"\rep 1 a", 1)]
    [InlineData(@"\rep 100 a", 100)]
    [InlineData(@"\rep 999 a", 100)] // 上限超過 → 100 にクランプ
    public void Rep_CountIsClampedTo1To100(string macro, int expectedRepeatCount)
    {
        MacroPlan plan = TextCommands.ParseRepeatAndSleep(macro);

        Assert.Equal(expectedRepeatCount, plan.RepeatCount);
    }

    [Fact]
    public void Rep_WithNoDigits_IsKeptAsLiteralPrefix()
    {
        // "\rep " の直後に数字が無い場合、パースを試みず "\rep " をリテラルとして残す
        // （RepeatCount は既定値 1 のまま）。
        MacroPlan plan = TextCommands.ParseRepeatAndSleep(@"\rep abc");

        Assert.Equal(1, plan.RepeatCount);
        Assert.Equal(new[] { new MacroSegment(@"\rep abc", 0) }, plan.Segments);
    }

    [Fact]
    public void Rep_WithOverflowingCount_IsKeptAsLiteralPrefix()
    {
        // int の範囲を超える桁数 → stoi 相当のパース失敗として "\rep " ごとリテラルに残す
        // （C++ 版 catch(...) と同じく repeatCount・src とも変更しない）。
        const string macro = @"\rep 999999999999999 x";

        MacroPlan plan = TextCommands.ParseRepeatAndSleep(macro);

        Assert.Equal(1, plan.RepeatCount);
        Assert.Equal(new[] { new MacroSegment(macro, 0) }, plan.Segments);
    }

    [Fact]
    public void Rep_OnlyWithNothingFollowing_ResultsInEmptySegments()
    {
        // "\rep 5 " のみ（本文なし）→ \rep 除去後に空文字列となり Segments は空になる。
        // ExecuteMacro はこの場合 SendInput を一切呼ばず成功として扱う（C++ 版と同じ仕様）。
        MacroPlan plan = TextCommands.ParseRepeatAndSleep(@"\rep 5 ");

        Assert.Equal(5, plan.RepeatCount);
        Assert.Empty(plan.Segments);
    }

    // ---- ParseRepeatAndSleep: \sleep ----

    [Fact]
    public void Sleep_SplitsIntoSegmentsWithClampedDelay()
    {
        MacroPlan plan = TextCommands.ParseRepeatAndSleep(@"a\sleep 100b");

        Assert.Equal(1, plan.RepeatCount);
        Assert.Equal(
            new[] { new MacroSegment("a", 100), new MacroSegment("b", 0) },
            plan.Segments);
    }

    [Theory]
    [InlineData(@"a\sleep 0b", 0)]
    [InlineData(@"a\sleep 60000b", 60000)]
    [InlineData(@"a\sleep 999999b", 60000)] // 上限超過 → 60000 にクランプ
    public void Sleep_DelayIsClampedTo0To60000(string macro, int expectedFirstSegmentSleep)
    {
        MacroPlan plan = TextCommands.ParseRepeatAndSleep(macro);

        Assert.Equal(expectedFirstSegmentSleep, plan.Segments[0].SleepMilliseconds);
    }

    [Fact]
    public void Sleep_TagIsAlwaysConsumedEvenWithoutValidNumber()
    {
        // \rep と異なり、"\sleep " タグ自体は後続に数字が無くても必ず本文から取り除かれる
        // （ms は 0 扱い）。この非対称性は C++ 版 executeMacro の既存仕様どおり。
        MacroPlan plan = TextCommands.ParseRepeatAndSleep(@"a\sleep xb");

        Assert.Equal(
            new[] { new MacroSegment("a", 0), new MacroSegment("xb", 0) },
            plan.Segments);
    }

    [Fact]
    public void RepAndSleep_CombineCorrectly()
    {
        MacroPlan plan = TextCommands.ParseRepeatAndSleep(@"\rep 2 a\sleep 50b");

        Assert.Equal(2, plan.RepeatCount);
        Assert.Equal(
            new[] { new MacroSegment("a", 50), new MacroSegment("b", 0) },
            plan.Segments);
    }

    [Fact]
    public void NoRepNoSleep_ProducesSingleSegmentWithDefaultRepeatCount()
    {
        MacroPlan plan = TextCommands.ParseRepeatAndSleep("hello");

        Assert.Equal(1, plan.RepeatCount);
        Assert.Equal(new[] { new MacroSegment("hello", 0) }, plan.Segments);
    }

    // ---- ExecuteMacro / PasteText: 副作用の無い early-return 経路のみ ----

    [Fact]
    public void ExecuteMacro_EmptyString_ReturnsInvalidCommandWithoutSendInput()
    {
        ExecuteError? result = TextCommands.ExecuteMacro(string.Empty);

        Assert.Equal(ExecuteError.InvalidCommand, result);
    }

    [Fact]
    public void PasteText_EmptyString_ReturnsInvalidCommandWithoutTouchingClipboard()
    {
        ExecuteError? result = TextCommands.PasteText(string.Empty);

        Assert.Equal(ExecuteError.InvalidCommand, result);
    }
}
