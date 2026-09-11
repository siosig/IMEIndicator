// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Services.Hotkey;

using Xunit;

namespace IMEIndicator.Tests.Hotkey;

/// <summary>
/// CommandCatalog の単体テスト。
/// 根拠: specs/014-port-to-csharp/contracts/internal-command-catalog.md
/// 「実行経路を持つ 108 種」「実行経路を持たない 23 種」、data-model.md §4。
/// </summary>
public sealed class CommandCatalogTests
{
    // internal-command-catalog.md「実行経路を持たない 23 種」の ID 一覧
    // （dispatch 未登録 7 件: 21,68,69,71,72,73,82 + dispatch 先で未処理 16 件:
    // 62,92,93,50-57,89,91,103,106,107）。
    private static readonly int[] ExcludedIds =
    {
        21, 50, 51, 52, 53, 54, 55, 56, 57, 62, 68, 69, 71, 72, 73, 82, 89, 91, 92, 93, 103, 106, 107,
    };

    [Fact]
    public void AllContainsExactly108Entries()
    {
        Assert.Equal(108, CommandCatalog.All.Count);
    }

    [Fact]
    public void AllHasNoDuplicateIds()
    {
        int distinctCount = CommandCatalog.All.Select(e => e.Id).Distinct().Count();
        Assert.Equal(CommandCatalog.All.Count, distinctCount);
    }

    [Fact]
    public void ExcludedIdsAreNotInAll()
    {
        // 除外リスト自体が 23 件であることも確認する（転記ミスの検出）。
        Assert.Equal(23, ExcludedIds.Length);

        foreach (int id in ExcludedIds)
        {
            Assert.DoesNotContain(CommandCatalog.All, e => e.Id == id);
        }
    }

    [Fact]
    public void LabelForExcludedIdReturnsUnimplementedMarker()
    {
        Assert.Equal("(未実装: 71)", CommandCatalog.LabelFor(71));

        // 除外 23 件すべてで「未実装」表示になり、IsImplemented が false になることを確認する。
        foreach (int id in ExcludedIds)
        {
            Assert.Equal($"(未実装: {id})", CommandCatalog.LabelFor(id));
            Assert.False(CommandCatalog.IsImplemented(id));
        }
    }

    [Theory]
    [InlineData(200)] // [IMEIndicator] インジケーター表示切替
    [InlineData(13)]  // 音量: +5%
    [InlineData(7)]   // ウィンドウ: 最大化
    [InlineData(0)]   // CD トレイを開く。readonly record struct の既定値（Id==0）との衝突が
                       // 無いこと（ById.TryGetValue による実装）の確認を兼ねる。
    public void IsImplementedReturnsTrueForKnownIds(int id)
    {
        Assert.True(CommandCatalog.IsImplemented(id));
    }

    // internal-command-catalog.md「集計」表（分類ごとの件数）との整合性。
    // カテゴリの取り違え（別分類への誤転記）を検出するための追加検証。
    [Theory]
    [InlineData("電源", 8)]
    [InlineData("音量", 7)]
    [InlineData("メディア", 8)]
    [InlineData("ウィンドウ", 18)]
    [InlineData("画面端配置", 8)]
    [InlineData("マウス", 9)]
    [InlineData("プロセス", 8)]
    [InlineData("テキスト・マクロ", 5)]
    [InlineData("ディスプレイ", 2)]
    [InlineData("システム", 21)]
    [InlineData("仮想デスクトップ", 4)]
    [InlineData("IMEIndicator 拡張", 10)]
    public void CategoryCountsMatchCatalogSummary(string category, int expectedCount)
    {
        int actualCount = CommandCatalog.All.Count(e => e.Category == category);
        Assert.Equal(expectedCount, actualCount);
    }

    [Fact]
    public void EveryEntryBelongsToAKnownCategory()
    {
        var knownCategories = new HashSet<string>
        {
            "電源", "音量", "メディア", "ウィンドウ", "画面端配置", "マウス",
            "プロセス", "テキスト・マクロ", "ディスプレイ", "システム",
            "仮想デスクトップ", "IMEIndicator 拡張",
        };

        Assert.All(CommandCatalog.All, e => Assert.Contains(e.Category, knownCategories));
    }
}
