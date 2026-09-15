// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;
using IMEIndicator.Services;

using Xunit;

namespace IMEIndicator.Tests.Services;

/// <summary>
/// <see cref="ImeStateRules"/> の単体テスト。
/// specs/017-fix-notepad-ime-display/contracts/ime-state-rules-contract.md「テストケース」の
/// SR-01〜SR-08 に対応する。Win32 に依存しない純粋関数のみを対象とするため、
/// ディスプレイ構成・実際の IME 状態に依存せず常に実行できる。
/// </summary>
public sealed class ImeStateRulesTests
{
    // ---- SR-01: SelectQueryWindow ----

    // nint は xUnit の既定シリアライズ対象型（bool/byte/char/decimal/double/float/int/long/sbyte/short/string/
    // uint/ulong/DateTime/DateTimeOffset/Guid/BigInteger/TimeOnly/TimeSpan/Type/Uri/Version/enum とその配列・null）
    // に含まれないため、[InlineData] に直接渡すとテストローダーの内部メッセージ配信が
    // 「There is at least one object in this array that cannot be serialized」で壊れ、
    // 無関係な他テストの結果まで報告から欠落する（xunit.net 公式ドキュメント
    // https://xunit.net/docs/getting-started/v3/custom-serialization で確認済み）。
    // 本リポジトリの既存テスト（InputRouterTests.cs・HotkeyServiceTests.cs）と同じく、
    // シリアライズ可能な long で受けてメソッド本体で nint へキャストする。
    [Theory]
    // foregroundWindow, focusWindow, expected
    [InlineData(0x1000L, 0L, 0x1000L)]        // フォーカスなし → 前面ウィンドウ
    [InlineData(0x1000L, 0x1000L, 0x1000L)]   // フォーカス == 前面 → 前面ウィンドウ
    [InlineData(0x1000L, 0x2000L, 0x2000L)]   // フォーカスが別ウィンドウ → フォーカスのウィンドウ
    [InlineData(0L, 0L, 0L)]                  // 前面ウィンドウなし
    public void SelectQueryWindow_MatchesDecisionTable(long foregroundWindow, long focusWindow, long expected)
    {
        nint actual = ImeStateRules.SelectQueryWindow((nint)foregroundWindow, (nint)focusWindow);

        Assert.Equal((nint)expected, actual);
    }

    // ---- SR-02: InferFromKey ----

    [Fact]
    public void InferFromKey_VkKanji_WhenTrackedOff_InfersOn_WithoutImplyingLanguage()
    {
        ImeKeyInference? result = ImeStateRules.InferFromKey(ImeStateRules.VkKanji, currentTrackedImeState: false);

        Assert.NotNull(result);
        Assert.True(result!.Value.IsImeOn);
        Assert.False(result.Value.ImpliesJapaneseLanguage);
    }

    [Fact]
    public void InferFromKey_VkKanji_WhenTrackedOn_InfersOff_WithoutImplyingLanguage()
    {
        ImeKeyInference? result = ImeStateRules.InferFromKey(ImeStateRules.VkKanji, currentTrackedImeState: true);

        Assert.NotNull(result);
        Assert.False(result!.Value.IsImeOn);
        Assert.False(result.Value.ImpliesJapaneseLanguage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferFromKey_VkOemEnlw_AlwaysInfersOn_AndImpliesJapanese(bool currentTrackedImeState)
    {
        ImeKeyInference? result = ImeStateRules.InferFromKey(ImeStateRules.VkOemEnlw, currentTrackedImeState);

        Assert.NotNull(result);
        Assert.True(result!.Value.IsImeOn);
        Assert.True(result.Value.ImpliesJapaneseLanguage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferFromKey_VkImeOn_AlwaysInfersOn_AndImpliesJapanese(bool currentTrackedImeState)
    {
        ImeKeyInference? result = ImeStateRules.InferFromKey(ImeStateRules.VkImeOn, currentTrackedImeState);

        Assert.NotNull(result);
        Assert.True(result!.Value.IsImeOn);
        Assert.True(result.Value.ImpliesJapaneseLanguage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferFromKey_VkOemAuto_AlwaysInfersOff_AndImpliesJapanese(bool currentTrackedImeState)
    {
        ImeKeyInference? result = ImeStateRules.InferFromKey(ImeStateRules.VkOemAuto, currentTrackedImeState);

        Assert.NotNull(result);
        Assert.False(result!.Value.IsImeOn);
        Assert.True(result.Value.ImpliesJapaneseLanguage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferFromKey_VkImeOff_AlwaysInfersOff_AndImpliesJapanese(bool currentTrackedImeState)
    {
        ImeKeyInference? result = ImeStateRules.InferFromKey(ImeStateRules.VkImeOff, currentTrackedImeState);

        Assert.NotNull(result);
        Assert.False(result!.Value.IsImeOn);
        Assert.True(result.Value.ImpliesJapaneseLanguage);
    }

    [Theory]
    [InlineData(0x15)] // VK_KANA
    [InlineData(0x1C)] // VK_CONVERT
    [InlineData(0x1D)] // VK_NONCONVERT
    [InlineData(0x41)] // 'A' - 無関係なキー
    public void InferFromKey_NonInferenceKeys_ReturnsNull(int vkCode)
    {
        ImeKeyInference? result = ImeStateRules.InferFromKey(vkCode, currentTrackedImeState: false);

        Assert.Null(result);
    }

    // ---- SR-03: NeedsPixelFallback ----

    [Fact]
    public void NeedsPixelFallback_Japanese_Reliable_ReturnsFalse()
    {
        Assert.False(ImeStateRules.NeedsPixelFallback(LanguageType.Japanese, immReliable: true));
    }

    [Fact]
    public void NeedsPixelFallback_Japanese_Unreliable_ReturnsTrue()
    {
        Assert.True(ImeStateRules.NeedsPixelFallback(LanguageType.Japanese, immReliable: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NeedsPixelFallback_NonJapanese_AlwaysReturnsFalse(bool immReliable)
    {
        Assert.False(ImeStateRules.NeedsPixelFallback(LanguageType.English, immReliable));
    }

    // ---- SR-04: Resolve ----

    [Fact]
    public void Resolve_NonJapaneseLanguage_ReturnsOff_WithNotJapaneseSource()
    {
        ImeResolution result = ImeStateRules.Resolve(
            LanguageType.English, immIsOpen: true, immReliable: true, pixelIsOn: true, trackedImeState: true);

        Assert.False(result.IsImeOn);
        Assert.Equal(ImeStateSource.NotJapanese, result.Source);
    }

    [Fact]
    public void Resolve_ImmReliable_PrefersImmValue_EvenWhenPixelDisagrees()
    {
        // R2: pixelIsOn が IMM32 と逆でも IMM32 を採用する（既存の「ピクセルが IMM32 を上書きする」設計からの変更点）。
        ImeResolution result = ImeStateRules.Resolve(
            LanguageType.Japanese, immIsOpen: true, immReliable: true, pixelIsOn: false, trackedImeState: false);

        Assert.True(result.IsImeOn);
        Assert.Equal(ImeStateSource.Imm32, result.Source);
    }

    [Fact]
    public void Resolve_ImmUnreliable_PixelAvailable_UsesPixelValue()
    {
        ImeResolution result = ImeStateRules.Resolve(
            LanguageType.Japanese, immIsOpen: false, immReliable: false, pixelIsOn: true, trackedImeState: false);

        Assert.True(result.IsImeOn);
        Assert.Equal(ImeStateSource.Pixel, result.Source);
    }

    [Fact]
    public void Resolve_ImmUnreliable_PixelNull_FallsBackToTrackedState()
    {
        // FR-012: 判定不能時はキー推定（追跡状態）を維持する。
        ImeResolution result = ImeStateRules.Resolve(
            LanguageType.Japanese, immIsOpen: false, immReliable: false, pixelIsOn: null, trackedImeState: true);

        Assert.True(result.IsImeOn);
        Assert.Equal(ImeStateSource.KeyInference, result.Source);
    }

    // ---- SR-05: ImeResolution.ResyncsTrackedState ----

    [Theory]
    [InlineData(ImeStateSource.Imm32, true)]
    [InlineData(ImeStateSource.Pixel, true)]
    [InlineData(ImeStateSource.KeyInference, false)]
    [InlineData(ImeStateSource.NotJapanese, false)]
    public void ResyncsTrackedState_MatchesSourceReliability(ImeStateSource source, bool expected)
    {
        var resolution = new ImeResolution(IsImeOn: true, Source: source);

        Assert.Equal(expected, resolution.ResyncsTrackedState);
    }

    // ---- SR-06: ShouldFire ----

    [Fact]
    public void ShouldFire_Stale_AlwaysReturnsFalse()
    {
        // F1: 判定中に押されたキーより古い結果は、他の条件によらず捨てる。
        Assert.False(ImeStateRules.ShouldFire(stale: true, stateChanged: true, forceUpdate: true, elapsedSinceOptimisticUpdateMs: 999, verificationWindowMs: 200));
    }

    [Fact]
    public void ShouldFire_NotStale_StateChanged_ReturnsTrue()
    {
        // F2
        Assert.True(ImeStateRules.ShouldFire(stale: false, stateChanged: true, forceUpdate: false, elapsedSinceOptimisticUpdateMs: 0, verificationWindowMs: 200));
    }

    [Fact]
    public void ShouldFire_NotStale_NoChangeNoForce_ReturnsFalse()
    {
        // F3
        Assert.False(ImeStateRules.ShouldFire(stale: false, stateChanged: false, forceUpdate: false, elapsedSinceOptimisticUpdateMs: 999, verificationWindowMs: 200));
    }

    [Fact]
    public void ShouldFire_NotStale_ForceUpdate_WithinOptimisticWindow_ReturnsFalse()
    {
        // F4: 楽観的更新の検証窓内（経過 < 窓）かつ状態一致なら発火しない。
        Assert.False(ImeStateRules.ShouldFire(stale: false, stateChanged: false, forceUpdate: true, elapsedSinceOptimisticUpdateMs: 100, verificationWindowMs: 200));
    }

    [Fact]
    public void ShouldFire_NotStale_ForceUpdate_OutsideOptimisticWindow_ReturnsTrue()
    {
        // F5: 検証窓を過ぎていれば forceUpdate で発火する。
        Assert.True(ImeStateRules.ShouldFire(stale: false, stateChanged: false, forceUpdate: true, elapsedSinceOptimisticUpdateMs: 200, verificationWindowMs: 200));
    }

    // ---- SR-07: シナリオ - VK_OEM_ENLW の推定が検証で維持される ----

    [Fact]
    public void Scenario_OemEnlwInference_ConfirmedByReliableImm32_KeepsDisplayShown()
    {
        // 追跡状態 OFF で VK_OEM_ENLW → 推定 ON。
        ImeKeyInference? inference = ImeStateRules.InferFromKey(ImeStateRules.VkOemEnlw, currentTrackedImeState: false);
        Assert.NotNull(inference);
        Assert.True(inference!.Value.IsImeOn);

        // 200ms 後の検証で IMM32 が同じ ON を確実に読めた場合。
        ImeResolution resolution = ImeStateRules.Resolve(
            LanguageType.Japanese, immIsOpen: true, immReliable: true, pixelIsOn: null, trackedImeState: true);
        Assert.Equal(ImeStateSource.Imm32, resolution.Source);
        Assert.True(resolution.IsImeOn);

        // 状態は変化していない（表示を維持したまま、通知は発生しない）。
        bool fire = ImeStateRules.ShouldFire(stale: false, stateChanged: false, forceUpdate: false, elapsedSinceOptimisticUpdateMs: 200, verificationWindowMs: 200);
        Assert.False(fire);
    }

    // ---- SR-08: シナリオ - VK_IME_ON が判定不能でも表示を消さない ----

    [Fact]
    public void Scenario_ImeOnInference_UndeterminedVerification_KeepsInferredDisplay()
    {
        // 追跡状態 OFF で VK_IME_ON → 推定 ON。
        ImeKeyInference? inference = ImeStateRules.InferFromKey(ImeStateRules.VkImeOn, currentTrackedImeState: false);
        Assert.NotNull(inference);
        Assert.True(inference!.Value.IsImeOn);
        Assert.True(inference.Value.ImpliesJapaneseLanguage);

        // 200ms 後の検証で IMM32・ピクセルとも判定不能。
        ImeResolution resolution = ImeStateRules.Resolve(
            LanguageType.Japanese, immIsOpen: false, immReliable: false, pixelIsOn: null, trackedImeState: true);
        Assert.Equal(ImeStateSource.KeyInference, resolution.Source);
        Assert.True(resolution.IsImeOn);

        // 判定不能で状態も変わっていないので、表示を消す通知は発生しない（FR-012）。
        bool fire = ImeStateRules.ShouldFire(stale: false, stateChanged: false, forceUpdate: false, elapsedSinceOptimisticUpdateMs: 200, verificationWindowMs: 200);
        Assert.False(fire);
    }
}
