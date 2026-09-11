// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

namespace IMEIndicator.Services.Hotkey;

/// <summary>
/// 内部コマンド実行エラー。移植元: <c>src/cpp/services/hotkey/CommandExecutor.h</c> の
/// <c>enum class ExecuteError</c>。値の意味は C++ 版と同一で、値そのものは C# 版で新設した
/// （C++ 版はカテゴリごとに個別のエラー enum（<c>PowerError</c> / <c>WindowError</c> 等）を持ち
/// <c>CommandExecutor.cpp</c> でこの型へ変換するが、C# 版は各コマンド分類クラスが直接この型を
/// 返す設計とし、中間の変換層を省略している。ユーザーから見える最終的なエラー種別は同じ）。
/// </summary>
public enum ExecuteError
{
    /// <summary>コマンド ID が範囲外、または実行経路の無い ID（後方互換のため引数としては受理するが処理しない）。</summary>
    InvalidCommand,

    /// <summary>対応していない Windows バージョン・環境でコマンドを実行しようとした。</summary>
    PlatformNotSupported,

    /// <summary>管理者権限が必要。</summary>
    AccessDenied,

    /// <summary>プロセス起動に失敗した。</summary>
    ProcessLaunchFailed,

    /// <summary>Win32 API 呼び出しが失敗した。</summary>
    ApiCallFailed,
}
