// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using System.Diagnostics;

namespace IMEIndicator.Services;

/// <summary>
/// プロセス優先度操作の抽象インターフェース（テスト用フェイク差し替え対応）。
/// 移植元: src/cpp/services/IProcessPriorityService.h。
/// </summary>
/// <param name="ProcessId">プロセス ID。</param>
/// <param name="CurrentPriorityClass">現在の優先度クラス。</param>
public readonly record struct ProcessPriorityEntry(int ProcessId, ProcessPriorityClass CurrentPriorityClass);

public interface IProcessPriorityService
{
    /// <summary>
    /// processNameNoExt（拡張子なし、大小無視）に一致する全プロセスの (PID, 優先度クラス) を返す。
    /// 戻り値は呼び出し時点の独立したスナップショットでなければならない（移植元 C++ の
    /// <c>std::vector</c> 値返しと同じ意味）。実装・フェイクとも、後続の <see cref="SetPriority"/> 等で
    /// 内部状態が変わっても、既に返した一覧を書き換えてはならない。
    /// </summary>
    IReadOnlyList<ProcessPriorityEntry> GetProcessPriorities(string processNameNoExt);

    /// <summary>指定 PID の優先度クラスを設定する。成功時 true。</summary>
    bool SetPriority(int processId, ProcessPriorityClass priorityClass);

    /// <summary>指定 PID のアフィニティを取得する。失敗時 null。</summary>
    nint? GetAffinity(int processId);

    /// <summary>指定 PID のアフィニティを設定する。成功時 true。</summary>
    bool SetAffinity(int processId, nint affinityMask);

    /// <summary>
    /// 名前一致するプロセスのうち、制御可能（書き込み権限を取得できる）ものが 1 つでもあれば true。
    /// 一致プロセスが 0 件の場合も true（判定不能）。全て権限拒否で失敗した場合のみ false
    /// （管理者権限が必要）。設定ダイアログでの行ハイライト判定に使用する。
    /// </summary>
    bool IsAccessibleForControl(string processNameNoExt);
}
