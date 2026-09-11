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
/// ImeIndicatorCommands の単体テスト。
/// 根拠: specs/014-port-to-csharp/contracts/internal-command-catalog.md「IMEIndicator 拡張（10）」、
/// tasks.md T064。
/// </summary>
/// <remarks>
/// <see cref="ImeIndicatorCommands"/> は static class で <c>Handlers</c> をプロセス全体で共有する
/// static フィールドに保持するため、各テストは実行順に依存しないよう、検証対象の状態
/// （Handlers 注入済み／未注入）を必ず自分自身の冒頭で明示的に設定する
/// （xUnit は既定で同一クラス内のテストを直列実行するが、それに依存しない書き方にする）。
/// </remarks>
public sealed class ImeIndicatorCommandsTests
{
    // Execute の委譲先を記録するテスト用スパイ。Handlers の各デリゲートフィールドをラップし、
    // どれが何回・どんな引数で呼ばれたかを記録する（App 本体には一切依存しない）。
    // [Theory] の DispatchCases が Action<HandlerSpy> を public パラメーターとして公開するため
    // （xUnit は [Theory] メソッド自体を public にする必要がある）、この入れ子クラスも public にする
    // （CS0051: アクセシビリティの一貫性エラー回避）。
    public sealed class HandlerSpy
    {
        public int ToggleIndicatorVisibleCount { get; private set; }
        public int TogglePixelDetectionCount { get; private set; }
        public int ReloadSettingsCount { get; private set; }
        public int ToggleBackgroundImageVisibleCount { get; private set; }
        public int TogglePowerModeAndNotifyCount { get; private set; }
        public int ApplyHighPerformancePowerCount { get; private set; }
        public int RestorePowerBackupCount { get; private set; }
        public int PauseAllRulesCount { get; private set; }
        public (string Param, bool Enabled)? SetRuleEnabledCall { get; private set; }

        // 想定外のフィールドが誤って呼ばれていないこと（ID→委譲先の取り違え）を検出するための合計。
        public int TotalInvocationCount =>
            ToggleIndicatorVisibleCount + TogglePixelDetectionCount + ReloadSettingsCount +
            ToggleBackgroundImageVisibleCount + TogglePowerModeAndNotifyCount +
            ApplyHighPerformancePowerCount + RestorePowerBackupCount + PauseAllRulesCount +
            (SetRuleEnabledCall.HasValue ? 1 : 0);

        public ImeIndicatorCommands.Handlers ToHandlers() => new(
            ToggleIndicatorVisible: () => ToggleIndicatorVisibleCount++,
            TogglePixelDetection: () => TogglePixelDetectionCount++,
            ReloadSettings: () => ReloadSettingsCount++,
            ToggleBackgroundImageVisible: () => ToggleBackgroundImageVisibleCount++,
            TogglePowerModeAndNotify: () => TogglePowerModeAndNotifyCount++,
            ApplyHighPerformancePower: () => ApplyHighPerformancePowerCount++,
            RestorePowerBackup: () => RestorePowerBackupCount++,
            SetRuleEnabled: (name, enabled) => SetRuleEnabledCall = (name, enabled),
            PauseAllRules: () => PauseAllRulesCount++);
    }

    // === IsImeIndicatorCommandId ===

    [Theory]
    [InlineData(200)]
    [InlineData(299)]
    [InlineData(250)]
    public void IsImeIndicatorCommandId_WithinRange_ReturnsTrue(int id)
    {
        Assert.True(ImeIndicatorCommands.IsImeIndicatorCommandId(id));
    }

    [Theory]
    [InlineData(199)]
    [InlineData(300)]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(119)]
    public void IsImeIndicatorCommandId_OutOfRange_ReturnsFalse(int id)
    {
        Assert.False(ImeIndicatorCommands.IsImeIndicatorCommandId(id));
    }

    // === GetName ===

    [Theory]
    [InlineData(200, "IME インジケーター表示切替")]
    [InlineData(201, "ピクセル検出有効/無効")]
    [InlineData(202, "IME 設定リロード")]
    [InlineData(203, "背景画像表示切替")]
    [InlineData(210, "電源モード切替（バックアップ付き）")]
    [InlineData(211, "高パフォーマンス電源プラン適用")]
    [InlineData(212, "電源モードバックアップから復元")]
    [InlineData(220, "指定プロセス優先度ルールを一時停止")]
    [InlineData(221, "指定プロセス優先度ルールを再開")]
    [InlineData(222, "全プロセス優先度ルール一時停止")]
    public void GetName_KnownId_ReturnsCppEquivalentJapaneseName(int id, string expected)
    {
        Assert.Equal(expected, ImeIndicatorCommands.GetName(id));
    }

    [Theory]
    [InlineData(205)]  // 範囲内・未対応
    [InlineData(50)]   // 範囲外（HotkeyP 由来のシステムコマンド ID）
    public void GetName_UnknownId_ReturnsEmptyString(int id)
    {
        Assert.Equal(string.Empty, ImeIndicatorCommands.GetName(id));
    }

    // === Execute: Handlers 未注入 ===

    [Fact]
    public void Execute_HandlersNotInjected_ReturnsApiCallFailed()
    {
        ImeIndicatorCommands.SetHandlers(default);

        ExecuteError? result = ImeIndicatorCommands.Execute(200, string.Empty);

        Assert.Equal(ExecuteError.ApiCallFailed, result);
    }

    // === Execute: 範囲外 / 範囲内未対応 ID ===

    [Theory]
    [InlineData(121)]  // HotkeyP 側の範囲（0〜120）
    [InlineData(199)]  // 200 未満
    [InlineData(300)]  // 299 超過
    public void Execute_OutOfRangeId_ReturnsInvalidCommand_EvenWithHandlersInjected(int id)
    {
        // Handlers を注入済みにしておくことで、返り値が「未注入だから失敗」ではなく
        // 純粋に「ID が範囲外だから失敗」であることを切り分ける。
        var spy = new HandlerSpy();
        ImeIndicatorCommands.SetHandlers(spy.ToHandlers());

        ExecuteError? result = ImeIndicatorCommands.Execute(id, string.Empty);

        Assert.Equal(ExecuteError.InvalidCommand, result);
        Assert.Equal(0, spy.TotalInvocationCount);
    }

    [Theory]
    [InlineData(205)]  // 200〜299 の範囲内だが対応表に無い ID
    [InlineData(298)]  // 同上
    public void Execute_InRangeButUnmappedId_ReturnsPlatformNotSupported(int id)
    {
        // C++ 版 executeImeIndicatorCommand は範囲内・未対応を ImeIndicatorCmdError::NotImplemented
        // とし、CommandExecutor.cpp が ExecuteError::PlatformNotSupported に変換する。範囲外
        // （InvalidCommand）とは区別されるべき、C++ 版に合わせた挙動。
        var spy = new HandlerSpy();
        ImeIndicatorCommands.SetHandlers(spy.ToHandlers());

        ExecuteError? result = ImeIndicatorCommands.Execute(id, string.Empty);

        Assert.Equal(ExecuteError.PlatformNotSupported, result);
        Assert.Equal(0, spy.TotalInvocationCount);
    }

    [Fact]
    public void Execute_UnsupportedId_ReturnsInvalidCommand_EvenWithoutHandlers()
    {
        // Handlers 未注入でも、ID 検証が先に走るため ApiCallFailed ではなく InvalidCommand になる。
        ImeIndicatorCommands.SetHandlers(default);

        ExecuteError? result = ImeIndicatorCommands.Execute(300, string.Empty);

        Assert.Equal(ExecuteError.InvalidCommand, result);
    }

    // === Execute: 正常な委譲（ID → Handlers フィールドの対応表） ===

    public static IEnumerable<object[]> DispatchCases()
    {
        yield return new object[] { 200, (Action<HandlerSpy>)(s => Assert.Equal(1, s.ToggleIndicatorVisibleCount)) };
        yield return new object[] { 201, (Action<HandlerSpy>)(s => Assert.Equal(1, s.TogglePixelDetectionCount)) };
        yield return new object[] { 202, (Action<HandlerSpy>)(s => Assert.Equal(1, s.ReloadSettingsCount)) };
        yield return new object[] { 203, (Action<HandlerSpy>)(s => Assert.Equal(1, s.ToggleBackgroundImageVisibleCount)) };
        yield return new object[] { 210, (Action<HandlerSpy>)(s => Assert.Equal(1, s.TogglePowerModeAndNotifyCount)) };
        yield return new object[] { 211, (Action<HandlerSpy>)(s => Assert.Equal(1, s.ApplyHighPerformancePowerCount)) };
        yield return new object[] { 212, (Action<HandlerSpy>)(s => Assert.Equal(1, s.RestorePowerBackupCount)) };
        yield return new object[] { 220, (Action<HandlerSpy>)(s => Assert.Equal(("notepad.exe", false), s.SetRuleEnabledCall)) };
        yield return new object[] { 221, (Action<HandlerSpy>)(s => Assert.Equal(("notepad.exe", true), s.SetRuleEnabledCall)) };
        yield return new object[] { 222, (Action<HandlerSpy>)(s => Assert.Equal(1, s.PauseAllRulesCount)) };
    }

    [Theory]
    [MemberData(nameof(DispatchCases))]
    public void Execute_KnownId_InvokesExactlyTheMappedHandlerAndReturnsNull(int id, Action<HandlerSpy> verify)
    {
        var spy = new HandlerSpy();
        ImeIndicatorCommands.SetHandlers(spy.ToHandlers());

        ExecuteError? result = ImeIndicatorCommands.Execute(id, "notepad.exe");

        Assert.Null(result);
        Assert.Equal(1, spy.TotalInvocationCount); // 対応表の取り違え（他フィールドの誤発火）が無いこと
        verify(spy);
    }

    [Fact]
    public void Execute_Cmd220And221_PassDifferentEnabledFlagWithSameProcessName()
    {
        // 220 = 一時停止（enabled: false）、221 = 再開（enabled: true）であることを、
        // 同一プロセス名で 2 回に分けて明示的に確認する（DispatchCases だけでは
        // 「たまたま両方 false/true が入れ替わっていても気づけない」ケースを潰す）。
        var pauseSpy = new HandlerSpy();
        ImeIndicatorCommands.SetHandlers(pauseSpy.ToHandlers());
        ImeIndicatorCommands.Execute(220, "chrome.exe");
        Assert.Equal(("chrome.exe", false), pauseSpy.SetRuleEnabledCall);

        var resumeSpy = new HandlerSpy();
        ImeIndicatorCommands.SetHandlers(resumeSpy.ToHandlers());
        ImeIndicatorCommands.Execute(221, "chrome.exe");
        Assert.Equal(("chrome.exe", true), resumeSpy.SetRuleEnabledCall);
    }

    [Theory]
    [InlineData(220)]
    [InlineData(221)]
    public void Execute_Cmd220Or221_EmptyParam_ReturnsApiCallFailedWithoutInvokingHandler(int id)
    {
        // C++ 版 cmdSetRuleEnabled: processName.empty() の場合はハンドラを呼ばず ApiCallFailed を返す。
        var spy = new HandlerSpy();
        ImeIndicatorCommands.SetHandlers(spy.ToHandlers());

        ExecuteError? result = ImeIndicatorCommands.Execute(id, string.Empty);

        Assert.Equal(ExecuteError.ApiCallFailed, result);
        Assert.Equal(0, spy.TotalInvocationCount);
    }
}
