// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models;

namespace IMEIndicator.Services;

/// <summary>
/// IME 開閉状態の最終値がどこから得られたかを表す。
/// 判定の優先順位（<see cref="ImeStateRules.Resolve"/>）で使う。
/// specs/017-fix-notepad-ime-display/data-model.md §2、research.md R-2。
/// </summary>
public enum ImeStateSource
{
    /// <summary>入力言語が日本語以外。常に OFF（既存規則。004-japanese-ime-only）。</summary>
    NotJapanese,

    /// <summary>フォーカスのウィンドウ（得られなければ前面ウィンドウ）の既定 IME ウィンドウから読んだ値。確実な判定。</summary>
    Imm32,

    /// <summary>タスクバー入力インジケーターのピクセル判定の値。IMM32 が読めないときだけ使う。確実な判定。</summary>
    Pixel,

    /// <summary>直前の IME 切替キー操作からの推定（追跡状態）。確実な判定ではない（FR-012）。</summary>
    KeyInference,
}

/// <summary>
/// 表示に使う最終的な IME 開閉値と、その出どころ。<see cref="ImeStateRules.Resolve"/> の戻り値。
/// specs/017-fix-notepad-ime-display/data-model.md §2。
/// </summary>
/// <param name="IsImeOn">表示に使う開閉値。</param>
/// <param name="Source">値の出どころ。</param>
public readonly record struct ImeResolution(bool IsImeOn, ImeStateSource Source)
{
    /// <summary>
    /// 追跡状態（<c>ImeMonitor._trackedImeState</c>）を再同期してよい確実な値かどうか。
    /// <see cref="Source"/> が <see cref="ImeStateSource.Imm32"/> または <see cref="ImeStateSource.Pixel"/> のとき
    /// <see langword="true"/>。research.md R-3（キー推定状態の再同期）。
    /// </summary>
    public bool ResyncsTrackedState => Source is ImeStateSource.Imm32 or ImeStateSource.Pixel;
}

/// <summary>
/// IME 切替キーからの推定結果。<see cref="ImeStateRules.InferFromKey"/> の戻り値。
/// specs/017-fix-notepad-ime-display/data-model.md §3。
/// </summary>
/// <param name="IsImeOn">キー操作から推定した開閉値。</param>
/// <param name="ImpliesJapaneseLanguage">ターミナル系プロセス用の追跡言語を日本語にするか。</param>
public readonly record struct ImeKeyInference(bool IsImeOn, bool ImpliesJapaneseLanguage);

/// <summary>
/// IME 状態の判定規則（問い合わせ先の選択・キー推定・判定結果の優先順位・通知判定）を集約する、
/// Win32 に依存しない純粋関数群。<see cref="ImeDetector"/> と <see cref="ImeMonitor"/> から呼ばれる。
/// </summary>
/// <remarks>
/// <para>
/// 017-fix-notepad-ime-display の根本原因（メモ帳は本文の編集コントロールがトップレベルウィンドウとは
/// 別スレッドで動くため、トップレベル基準の IME 状態問い合わせが常に OFF を返していた）への対応として、
/// 「どのウィンドウへ問い合わせるか」「どのキーをどう推定するか」「複数の判定結果からどれを採用するか」
/// 「通知すべきか」を、テスト可能な純粋関数として切り出したもの。
/// specs/017-fix-notepad-ime-display/contracts/ime-state-rules-contract.md が正の契約。
/// specs/017-fix-notepad-ime-display/research.md R-1〜R-6 に判断根拠を記載している。
/// </para>
/// <para>
/// テストプロジェクト（別アセンブリ）に <c>InternalsVisibleTo</c> が設定されていないため、
/// 単体テスト対象にする本クラスはすべて <see langword="public"/> にする
/// （<c>TextCommands</c> / <c>VolumeCommands</c> のコメントと同じ理由）。
/// </para>
/// </remarks>
public static class ImeStateRules
{
    // 移植元 KeyboardHook.cpp / ImeMonitor.cs と同一の仮想キー値。ImeMonitor 側の局所定数はここへ集約し、
    // 単一の情報源にする（research.md R-5）。
    /// <summary>IME ON（一部キーボードの専用キー）。</summary>
    public const int VkImeOn = 0x16;

    /// <summary>半角/全角（現在の追跡状態を反転させる）。</summary>
    public const int VkKanji = 0x19;

    /// <summary>IME OFF（一部キーボードの専用キー）。</summary>
    public const int VkImeOff = 0x1A;

    /// <summary>一部キーボードの IME OFF。</summary>
    public const int VkOemAuto = 0xF3;

    /// <summary>一部キーボードの IME ON。</summary>
    public const int VkOemEnlw = 0xF4;

    /// <summary>
    /// IME 開閉状態の問い合わせ先ウィンドウを選ぶ。フォーカスのウィンドウが得られていれば
    /// それを優先し（IME はフォーカスのあるスレッドの入力コンテキストに対して動くため）、
    /// 得られなければ前面ウィンドウにフォールバックする。
    /// research.md R-1（メモ帳ではフォーカスとトップレベルが別スレッドで、トップレベル基準の問い合わせが
    /// 常に OFF を返した実測結果）。
    /// </summary>
    /// <param name="foregroundWindow">前面ウィンドウ（<c>GetForegroundWindow</c>）。</param>
    /// <param name="focusWindow">
    /// <c>GetGUIThreadInfo</c> の <c>hwndFocus</c>。取得できない、または 0 の場合は 0 を渡すこと。
    /// </param>
    /// <returns>IME 開閉状態を問い合わせるべきウィンドウ。</returns>
    public static nint SelectQueryWindow(nint foregroundWindow, nint focusWindow) =>
        focusWindow != 0 ? focusWindow : foregroundWindow;

    /// <summary>
    /// IME 切替キーの押下から、開閉状態と追跡言語への影響を推定する。
    /// 推定できないキー（<c>VK_KANA</c> / <c>VK_CONVERT</c> / <c>VK_NONCONVERT</c> 等、IME のキー設定で
    /// 意味が変わりうるキー）は <see langword="null"/> を返す（research.md R-5・後続候補 F-2）。
    /// </summary>
    /// <param name="vkCode">検出された仮想キーコード。</param>
    /// <param name="currentTrackedImeState">
    /// 現在の追跡状態（<c>ImeMonitor._trackedImeState</c>）。<see cref="VkKanji"/> の反転推定に使う。
    /// </param>
    /// <returns>推定結果。推定対象外のキーなら <see langword="null"/>。</returns>
    public static ImeKeyInference? InferFromKey(int vkCode, bool currentTrackedImeState) => vkCode switch
    {
        VkKanji => new ImeKeyInference(IsImeOn: !currentTrackedImeState, ImpliesJapaneseLanguage: false),
        VkOemEnlw => new ImeKeyInference(IsImeOn: true, ImpliesJapaneseLanguage: true),
        VkImeOn => new ImeKeyInference(IsImeOn: true, ImpliesJapaneseLanguage: true),
        VkOemAuto => new ImeKeyInference(IsImeOn: false, ImpliesJapaneseLanguage: true),
        VkImeOff => new ImeKeyInference(IsImeOn: false, ImpliesJapaneseLanguage: true),
        _ => null,
    };

    /// <summary>
    /// ピクセル判定（<see cref="PixelImeDetector"/>）を呼ぶ必要があるかどうか。
    /// IMM32 の読み取りに成功していれば、既に確実な値があるためピクセル判定は呼ばない
    /// （research.md R-2、R-9: 判定 1 回あたりの UI Automation 検索を減らす）。
    /// </summary>
    /// <param name="language">問い合わせ先のキーボードレイアウトから判定した入力言語。</param>
    /// <param name="immReliable">IMM32 の読み取り（<see cref="ImeDetector.GetImeOpenStatusEx"/>）が成功したか。</param>
    /// <returns>ピクセル判定を呼ぶべきなら <see langword="true"/>。</returns>
    public static bool NeedsPixelFallback(LanguageType language, bool immReliable) =>
        language == LanguageType.Japanese && !immReliable;

    /// <summary>
    /// 複数の判定結果から、表示に使う最終的な IME 開閉値を決める。優先順位（research.md R-2）:
    /// (1) 日本語以外 → OFF。(2) IMM32 が読めた → その値（ピクセル判定より優先）。
    /// (3) IMM32 が読めずピクセル判定が値を返した → その値。(4) どちらも得られない → キー推定状態（FR-012）。
    /// </summary>
    /// <param name="language">入力言語。</param>
    /// <param name="immIsOpen">IMM32 から読んだ開閉値（<paramref name="immReliable"/> が <see langword="false"/> のときは無視される）。</param>
    /// <param name="immReliable">IMM32 の読み取りが成功したか。</param>
    /// <param name="pixelIsOn">ピクセル判定の値。呼んでいない、または判定不能なら <see langword="null"/>。</param>
    /// <param name="trackedImeState">直前の IME 切替キー操作からの追跡状態（キー推定）。</param>
    /// <returns>採用する開閉値と、その出どころ。</returns>
    public static ImeResolution Resolve(
        LanguageType language, bool immIsOpen, bool immReliable, bool? pixelIsOn, bool trackedImeState)
    {
        if (language != LanguageType.Japanese)
        {
            return new ImeResolution(IsImeOn: false, ImeStateSource.NotJapanese);
        }

        if (immReliable)
        {
            return new ImeResolution(immIsOpen, ImeStateSource.Imm32);
        }

        if (pixelIsOn.HasValue)
        {
            return new ImeResolution(pixelIsOn.Value, ImeStateSource.Pixel);
        }

        return new ImeResolution(trackedImeState, ImeStateSource.KeyInference);
    }

    /// <summary>
    /// 判定結果を <c>ImeStateChanged</c> として通知すべきかどうかを決める。
    /// 移植元 <c>ImeMonitor.CheckImeState</c> の条件（状態変化または強制更新で、かつ楽観的更新の検証窓内での
    /// 状態一致では発火しない）と等価な真理値を返しつつ、<paramref name="stale"/>（research.md R-4:
    /// 判定の実行中に押されたキーより古い結果）のときは必ず発火しない。
    /// </summary>
    /// <param name="stale">
    /// 判定の開始時から完了時までの間に、推定対象キーが押されて追跡状態が更新されたか
    /// （<c>ImeMonitor._keyGeneration</c> が判定開始時から変化していれば <see langword="true"/>）。
    /// </param>
    /// <param name="stateChanged">最終値が直前に通知した状態（<c>ImeMonitor._lastState</c>）と異なるか。</param>
    /// <param name="forceUpdate">前面切替・Win+Space 等による強制更新要求か。</param>
    /// <param name="elapsedSinceOptimisticUpdateMs">直前の楽観的更新からの経過ミリ秒。</param>
    /// <param name="verificationWindowMs">楽観的更新の検証窓（ミリ秒。既定 200ms）。</param>
    /// <returns>通知すべきなら <see langword="true"/>。</returns>
    public static bool ShouldFire(
        bool stale, bool stateChanged, bool forceUpdate, long elapsedSinceOptimisticUpdateMs, long verificationWindowMs)
    {
        if (stale)
        {
            return false;
        }

        if (!stateChanged && !forceUpdate)
        {
            return false;
        }

        bool withinOptimisticWindow = elapsedSinceOptimisticUpdateMs < verificationWindowMs;
        if (withinOptimisticWindow && !stateChanged)
        {
            // 楽観的更新の検証窓内で、かつ状態が一致 → 何もしない（既存ロジックと同じ）。
            return false;
        }

        return true;
    }
}
