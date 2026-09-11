// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using Xunit;

// IMEIndicator.Services.Log はプロセス全体で共有する static なロガー（本番のシングルプロセス
// 常駐アプリとしては正しい設計）だが、そのせいで Log.Configure / Log.SetLevel を使って
// キャプチャ用シンクを差し替え、記録されたイベント数を検証する LogTests は、xUnit が既定で
// 並列実行する他のどのテストクラスとも衝突しうる（Services/Hotkey/Commands 配下の各種コマンド
// 実装はいずれも内部で Log.XXX(...) を呼ぶため、対象は LogTests 自身が明示的に触れているクラスに
// 限らない）。実際に「Collection was modified; enumeration operation may not execute.」という
// レースを観測したため、テストアセンブリ全体の並列実行を無効化する（テスト総数はごく少なく
// 実行時間への影響は無視できる）。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
