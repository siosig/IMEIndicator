// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services;

namespace IMEIndicator.Services.Hotkey.Commands;

/// <summary>
/// IMEIndicator 拡張コマンド（内部コマンド ID 200〜299）。
/// 移植元: src/cpp/services/hotkey/commands/ImeIndicatorCommands.h / .cpp（Phase 6 / US4 本実装）。
/// 根拠: specs/014-port-to-csharp/contracts/internal-command-catalog.md「IMEIndicator 拡張（10）」。
/// </summary>
/// <remarks>
/// HotkeyP 由来の他分類（PowerCommands 等）と異なり、このカテゴリは Win32 API を直接叩かず、
/// 既に実装済みの <c>IMEIndicator.App.App</c> の公開メソッドへ委譲する薄いラッパーである。
/// 依存方向は「App → Services.Hotkey.Commands.ImeIndicatorCommands」であるべきで、
/// Services.Hotkey 側から App 型を直接参照すると循環参照になる。そのため C++ 版のサービス注入 setter
/// （setIndicatorWindow / setImeMonitor / setSettingsManager / setProcessPriorityMonitor /
/// setPowerModeToggleHandler / setBackgroundImageToggleHandler）に相当する仕組みとして、
/// 個々の Win32 サービスへの参照ではなく <see cref="Handlers"/>（App の公開メソッドへのデリゲート束）を
/// <see cref="SetHandlers"/> で注入する設計にした（配線は T069 で App.Initialize から行う想定）。
/// </remarks>
public static class ImeIndicatorCommands
{
    /// <summary>
    /// cmd 200〜299 の実処理を担うハンドラ束。すべて <c>IMEIndicator.App.App</c> の公開メソッドへの
    /// 委譲を想定する（ID → フィールドの対応は <see cref="Execute"/> のコメント参照）。
    /// 既定値（全フィールド null）は「未注入」を表し、<see cref="Execute"/> はこれを検出して
    /// <see cref="ExecuteError.ApiCallFailed"/> を返す。
    /// </summary>
    public readonly record struct Handlers(
        Action ToggleIndicatorVisible,
        Action TogglePixelDetection,
        Action ReloadSettings,
        Action ToggleBackgroundImageVisible,
        Action TogglePowerModeAndNotify,
        Action ApplyHighPerformancePower,
        Action RestorePowerBackup,
        Action<string, bool> SetRuleEnabled,
        Action PauseAllRules);

    // 既定値（全フィールド null）は「未注入」を表す。SetHandlers が一度も呼ばれていない場合、
    // または明示的に default を渡された場合はこの状態のままになる（record struct の値比較で検出する）。
    private static Handlers _handlers;

    /// <summary>
    /// App から公開メソッド群を注入する（T069、App.Initialize から 1 回呼ぶ想定）。
    /// 未注入（既定値のまま）の状態で <see cref="Execute"/> を呼ぶと <see cref="ExecuteError.ApiCallFailed"/> を返す。
    /// </summary>
    public static void SetHandlers(Handlers handlers) => _handlers = handlers;

    /// <summary>コマンド ID が IMEIndicator 拡張の範囲（200〜299）かどうか。移植元 isImeIndicatorCommandId。</summary>
    public static bool IsImeIndicatorCommandId(int id) => id is >= 200 and <= 299;

    /// <summary>
    /// 表示名取得（設定画面・ログ用）。移植元 getImeIndicatorCommandName。
    /// 対応する ID が無い場合は空文字列を返す（移植元は空文字列 L"" を返す。null は返さない）。
    /// </summary>
    public static string GetName(int cmdId) => cmdId switch
    {
        // インジケーター制御（200〜209）
        200 => "IME インジケーター表示切替",
        201 => "ピクセル検出有効/無効",
        202 => "IME 設定リロード",
        203 => "背景画像表示切替",
        // 電源モード（210〜219）
        210 => "電源モード切替（バックアップ付き）",
        211 => "高パフォーマンス電源プラン適用",
        212 => "電源モードバックアップから復元",
        // プロセス優先度（220〜229）
        220 => "指定プロセス優先度ルールを一時停止",
        221 => "指定プロセス優先度ルールを再開",
        222 => "全プロセス優先度ルール一時停止",
        _ => string.Empty,
    };

    /// <summary>
    /// IMEIndicator 拡張コマンドを実行する。移植元 executeImeIndicatorCommand。
    ///
    /// ID → 委譲先の対応（すべて <see cref="Handlers"/> 経由で App の公開メソッドを呼ぶ）:
    /// <list type="bullet">
    /// <item>200: ToggleIndicatorVisible → App.ToggleIndicatorVisible()</item>
    /// <item>201: TogglePixelDetection → App.TogglePixelDetection()</item>
    /// <item>202: ReloadSettings → App.ReloadSettings()</item>
    /// <item>203: ToggleBackgroundImageVisible → App.ToggleBackgroundImageVisible()</item>
    /// <item>210: TogglePowerModeAndNotify → App.TogglePowerModeAndNotify()</item>
    /// <item>211: ApplyHighPerformancePower → App.ApplyHighPerformancePower()</item>
    /// <item>212: RestorePowerBackup → App.RestorePowerBackup()</item>
    /// <item>220: SetRuleEnabled(param, enabled: false) → App.SetRuleEnabled(param, false)（一時停止）</item>
    /// <item>221: SetRuleEnabled(param, enabled: true) → App.SetRuleEnabled(param, true)（再開）</item>
    /// <item>222: PauseAllRules → App.PauseAllRules()</item>
    /// </list>
    ///
    /// なお C++ 版 cmd 200（cmdToggleIndicator）は「非表示にする時だけ即座に反映し、表示にする時は
    /// 次の IME 状態変化イベントに任せる」という非対称な実装だが、C# 版の App.SetIndicatorVisible
    /// （ToggleIndicatorVisible が内部で呼ぶ）はトグル後に常に ApplyWindowVisibility(現在の IME 状態) を
    /// 呼んで即座に表示反映するため、「次のイベント任せ」という遅延メカニズムを再現する必要はない
    /// （「IME が OFF のときはインジケーターを表示しない」という不変条件は、より直接的な方法で満たされる）。
    ///
    /// 範囲外 ID（200〜299 の外）は <see cref="ExecuteError.InvalidCommand"/>、範囲内だが対応表に無い
    /// 未対応 ID は <see cref="ExecuteError.PlatformNotSupported"/> を返す。これは C++ 版
    /// executeImeIndicatorCommand の isImeIndicatorCommandId 判定（範囲外 → InvalidCommand）と、
    /// 範囲内だが switch の default に落ちるケース（ImeIndicatorCmdError::NotImplemented →
    /// CommandExecutor.cpp の fromXxxError 相当のマッピングで ExecuteError::PlatformNotSupported）
    /// をそれぞれ忠実に再現したもの（レビューで発見した訂正: 当初はどちらも一律 InvalidCommand と
    /// していたが、C++ 版は 2 つを区別しているため合わせた）。
    ///
    /// 220/221 は <paramref name="param"/>（プロセス名）が空の場合、C++ 版 cmdSetRuleEnabled と同じく
    /// ハンドラを呼ばず <see cref="ExecuteError.ApiCallFailed"/> を返す（レビューで発見した訂正:
    /// 当初は空文字列でも無条件にハンドラへ委譲していた）。
    /// </summary>
    /// <param name="cmdId">内部コマンド ID。</param>
    /// <param name="param">コマンド引数。220/221 ではプロセス名として使う（それ以外のコマンドでは無視される）。</param>
    /// <returns>成功時は null。失敗時は <see cref="ExecuteError"/>。</returns>
    public static ExecuteError? Execute(int cmdId, string param)
    {
        if (!IsImeIndicatorCommandId(cmdId))
        {
            Log.Command.Warning("ImeIndicatorCommands.Execute: 範囲外の ID: {CmdId}", cmdId);
            return ExecuteError.InvalidCommand;
        }

        if (cmdId is not (200 or 201 or 202 or 203 or 210 or 211 or 212 or 220 or 221 or 222))
        {
            Log.Command.Warning("ImeIndicatorCommands.Execute: 未対応の ID: {CmdId}", cmdId);
            return ExecuteError.PlatformNotSupported;
        }

        // Handlers 未注入（SetHandlers が一度も呼ばれていない、または既定値を渡された）場合。
        // C++ 版の ImeIndicatorCmdError::ServiceNotInjected 相当。C# 版 ExecuteError にはこの区別が
        // 無いため ApiCallFailed に丸める（タスク仕様どおり）。
        if (_handlers == default)
        {
            Log.Command.Warning("ImeIndicatorCommands.Execute: Handlers 未注入のため実行できません: id={CmdId}", cmdId);
            return ExecuteError.ApiCallFailed;
        }

        if (cmdId is 220 or 221 && string.IsNullOrEmpty(param))
        {
            // C++ 版 cmdSetRuleEnabled: processName.empty() の場合はハンドラを呼ばず ApiCallFailed。
            Log.Command.Warning("ImeIndicatorCommands.Execute: id={CmdId} はプロセス名（param）が必要です", cmdId);
            return ExecuteError.ApiCallFailed;
        }

        switch (cmdId)
        {
            case 200: _handlers.ToggleIndicatorVisible(); break;
            case 201: _handlers.TogglePixelDetection(); break;
            case 202: _handlers.ReloadSettings(); break;
            case 203: _handlers.ToggleBackgroundImageVisible(); break;
            case 210: _handlers.TogglePowerModeAndNotify(); break;
            case 211: _handlers.ApplyHighPerformancePower(); break;
            case 212: _handlers.RestorePowerBackup(); break;
            case 220: _handlers.SetRuleEnabled(param, false); break; // 一時停止
            case 221: _handlers.SetRuleEnabled(param, true); break;  // 再開
            case 222: _handlers.PauseAllRules(); break;
        }

        Log.Command.Information("ImeIndicatorCommands.Execute: id={CmdId} name=\"{Name}\"", cmdId, GetName(cmdId));
        return null;
    }
}
