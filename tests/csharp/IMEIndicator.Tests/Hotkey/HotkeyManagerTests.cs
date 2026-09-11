// Copyright (C) 2026 IMEIndicator Project
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License v2 or later.
// See COPYING in the repository root for the full license text.

using IMEIndicator.Models.Hotkey;
using IMEIndicator.Services.Hotkey;

using Xunit;

namespace IMEIndicator.Tests.Hotkey;

/// <summary>
/// HotkeyManager の単体テスト。
/// 根拠: src/cpp/services/hotkey/HotkeyManager.h / HotkeyManager.cpp（1:1 移植元）。
/// </summary>
public sealed class HotkeyManagerTests
{
    // --- AddHotkey / Count / Hotkeys ---

    [Fact]
    public void AddHotkey_ReturnsSequentialIndex_AndIncreasesCount()
    {
        var manager = new HotkeyManager();

        int index0 = manager.AddHotkey(new HotKeyEntry { Note = "a" });
        int index1 = manager.AddHotkey(new HotKeyEntry { Note = "b" });

        Assert.Equal(0, index0);
        Assert.Equal(1, index1);
        Assert.Equal(2, manager.Count);
    }

    [Fact]
    public void AddHotkey_NullEntry_ThrowsArgumentNullException()
    {
        var manager = new HotkeyManager();

        Assert.Throws<ArgumentNullException>(() => manager.AddHotkey(null!));
    }

    [Fact]
    public void Hotkeys_ReflectsAddedEntriesInOrder()
    {
        var manager = new HotkeyManager();
        var a = new HotKeyEntry { Note = "a" };
        var b = new HotKeyEntry { Note = "b" };
        manager.AddHotkey(a);
        manager.AddHotkey(b);

        Assert.Equal(new[] { a, b }, manager.Hotkeys);
    }

    // --- GetHotkey ---

    [Fact]
    public void GetHotkey_ValidIndex_ReturnsSameInstance()
    {
        var manager = new HotkeyManager();
        var entry = new HotKeyEntry { Note = "a" };
        manager.AddHotkey(entry);

        Assert.Same(entry, manager.GetHotkey(0));
    }

    [Theory]
    [InlineData(-1)] // 負の添字
    [InlineData(1)]  // count（=1）以上の添字
    public void GetHotkey_OutOfRangeIndex_ReturnsNull(int index)
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Note = "a" }); // 有効な添字は 0 のみ

        Assert.Null(manager.GetHotkey(index));
    }

    [Fact]
    public void GetHotkey_EmptyManager_ReturnsNullForAnyIndex()
    {
        var manager = new HotkeyManager();

        Assert.Null(manager.GetHotkey(-1));
        Assert.Null(manager.GetHotkey(0));
        Assert.Null(manager.GetHotkey(10));
    }

    // --- RemoveHotkey ---

    [Fact]
    public void RemoveHotkey_ShiftsSubsequentEntriesForward()
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Note = "a" });
        manager.AddHotkey(new HotKeyEntry { Note = "b" });
        manager.AddHotkey(new HotKeyEntry { Note = "c" });

        manager.RemoveHotkey(0);

        Assert.Equal(2, manager.Count);
        Assert.Equal("b", manager.GetHotkey(0)!.Note);
        Assert.Equal("c", manager.GetHotkey(1)!.Note);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void RemoveHotkey_OutOfRangeIndex_DoesNothingAndDoesNotThrow(int index)
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Note = "a" });

        manager.RemoveHotkey(index);

        Assert.Equal(1, manager.Count);
    }

    // --- Clear ---

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Note = "a" });
        manager.AddHotkey(new HotKeyEntry { Note = "b" });

        manager.Clear();

        Assert.Equal(0, manager.Count);
        Assert.Empty(manager.Hotkeys);
    }

    // --- HasDuplicate ---

    [Fact]
    public void HasDuplicate_ReturnsTrueForMatchingVkeyAndModifiers()
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Vkey = 65, Modifiers = 2 });

        Assert.True(manager.HasDuplicate(65, 2));
    }

    [Fact]
    public void HasDuplicate_ReturnsFalseWhenVkeyOrModifiersDiffer()
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Vkey = 65, Modifiers = 2 });

        Assert.False(manager.HasDuplicate(65, 4));
        Assert.False(manager.HasDuplicate(66, 2));
    }

    [Fact]
    public void HasDuplicate_EmptyManager_ReturnsFalse()
    {
        var manager = new HotkeyManager();

        Assert.False(manager.HasDuplicate(0, 0));
    }

    // --- FindHotkeyByKey ---

    [Fact]
    public void FindHotkeyByKey_ReturnsMatchingEntry()
    {
        var manager = new HotkeyManager();
        var entry = new HotKeyEntry { Vkey = 65, Modifiers = 2, Note = "target" };
        manager.AddHotkey(entry);

        Assert.Same(entry, manager.FindHotkeyByKey(65, 2));
    }

    [Fact]
    public void FindHotkeyByKey_ReturnsNullWhenNotFound()
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Vkey = 65, Modifiers = 2 });

        Assert.Null(manager.FindHotkeyByKey(66, 2));
    }

    [Fact]
    public void FindHotkeyByKey_MatchesByVkeyAndModifiersOnly_ScanCodeIsIrrelevant()
    {
        // C++ 版 findHotkeyByKey() は vkey と modifiers の完全一致のみで判定し、scanCode は
        // 一切参照しない（HotkeyManager.cpp を実際に確認済み）。scanCode が大きく異なる
        // エントリでも vkey+modifiers さえ一致すれば見つかること、かつ線形探索の結果として
        // 先に登録した方（添字が小さい方）が返ることを確認する。
        var manager = new HotkeyManager();
        var first = new HotKeyEntry { Vkey = 65, Modifiers = 2, ScanCode = 0x001E, Note = "first" };
        var second = new HotKeyEntry { Vkey = 65, Modifiers = 2, ScanCode = 0x7FFF, Note = "second" };
        manager.AddHotkey(first);
        manager.AddHotkey(second);

        Assert.Same(first, manager.FindHotkeyByKey(65, 2));
    }

    // --- GetHotkeysByCategory ---

    [Fact]
    public void GetHotkeysByCategory_ReturnsOnlyMatchingEntriesInOriginalOrder()
    {
        var manager = new HotkeyManager();
        var program1 = new HotKeyEntry { Exe = @"C:\apps\tool.exe" };
        var document = new HotKeyEntry { Exe = @"C:\docs\readme.txt" };
        var program2 = new HotKeyEntry { Exe = @"C:\apps\another.EXE" };
        manager.AddHotkey(program1);
        manager.AddHotkey(document);
        manager.AddHotkey(program2);

        IReadOnlyList<HotKeyEntry> programs = manager.GetHotkeysByCategory(HotkeyCategoryKind.Programs);

        Assert.Equal(new[] { program1, program2 }, programs);
    }

    [Fact]
    public void GetHotkeysByCategory_NoMatch_ReturnsEmptyList()
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Exe = @"C:\docs\readme.txt" });

        Assert.Empty(manager.GetHotkeysByCategory(HotkeyCategoryKind.Programs));
    }

    // --- LoadFromHtkData ---

    [Fact]
    public void LoadFromHtkData_ReplacesExistingEntries()
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Note = "old" });

        manager.LoadFromHtkData(new List<HotKeyEntry>
        {
            new() { Note = "new1" },
            new() { Note = "new2" },
        });

        Assert.Equal(2, manager.Count);
        Assert.Equal("new1", manager.GetHotkey(0)!.Note);
        Assert.Equal("new2", manager.GetHotkey(1)!.Note);
    }

    [Fact]
    public void LoadFromHtkData_NullEntries_ThrowsArgumentNullException()
    {
        var manager = new HotkeyManager();

        Assert.Throws<ArgumentNullException>(() => manager.LoadFromHtkData(null!));
    }

    // --- LoadFromHotkeySettings ---

    [Fact]
    public void LoadFromHotkeySettings_ReplacesExistingEntries()
    {
        var manager = new HotkeyManager();
        manager.AddHotkey(new HotKeyEntry { Note = "old" });

        manager.LoadFromHotkeySettings(new List<HotKeyEntry> { new() { Note = "new" } });

        Assert.Equal(1, manager.Count);
        Assert.Equal("new", manager.GetHotkey(0)!.Note);
    }

    [Fact]
    public void LoadFromHotkeySettings_MoreThan256Entries_KeepsOnlyFirst256()
    {
        // FR-001 / FR-022: 上限 256 件を超える分は無視される
        // （HotkeyManager::loadFromHotkeySettings() の MaxHotkeys = 256 と break の挙動）。
        var manager = new HotkeyManager();
        var source = new List<HotKeyEntry>();
        for (int i = 0; i < 300; i++)
        {
            source.Add(new HotKeyEntry { Vkey = i, Note = $"hk{i}" });
        }

        manager.LoadFromHotkeySettings(source);

        Assert.Equal(256, manager.Count);
        Assert.Equal(0, manager.GetHotkey(0)!.Vkey);     // 先頭は保持される
        Assert.Equal(255, manager.GetHotkey(255)!.Vkey); // 256 件目（添字 255）まで保持される
        Assert.Null(manager.GetHotkey(256));             // 257 件目以降は破棄される
    }

    [Fact]
    public void LoadFromHotkeySettings_ExactlyMaxEntries_KeepsAll()
    {
        var manager = new HotkeyManager();
        var source = new List<HotKeyEntry>();
        for (int i = 0; i < 256; i++)
        {
            source.Add(new HotKeyEntry { Vkey = i });
        }

        manager.LoadFromHotkeySettings(source);

        Assert.Equal(256, manager.Count);
    }

    [Fact]
    public void LoadFromHotkeySettings_NullEntries_ThrowsArgumentNullException()
    {
        var manager = new HotkeyManager();

        Assert.Throws<ArgumentNullException>(() => manager.LoadFromHotkeySettings(null!));
    }
}

/// <summary>
/// HotKeyEntry.DisplayName / HotKeyEntry.AutoCategory() の単体テスト。
/// この 2 つは T053（本タスク）で HotKeyEntry.cs に追加したため（HotkeyModelsTests.cs / T012 は
/// 「バリデーション・自動カテゴリ判定ロジックは対象外」と明記して意図的に除外している）、
/// HotkeyManager と同じ T053 の範囲としてここに置く。
/// 根拠: src/cpp/models/hotkey/HotKeyEntry.h の displayName() / autoCategory()。
/// </summary>
public sealed class HotKeyEntryDisplayNameAndAutoCategoryTests
{
    // --- DisplayName ---

    [Fact]
    public void DisplayName_ReturnsNote_WhenNoteNonEmpty()
    {
        var entry = new HotKeyEntry { Note = "メモ", Exe = @"C:\apps\tool.exe" };

        Assert.Equal("メモ", entry.DisplayName);
    }

    [Fact]
    public void DisplayName_ReturnsExe_WhenNoteEmpty()
    {
        var entry = new HotKeyEntry { Note = "", Exe = @"C:\apps\tool.exe" };

        Assert.Equal(@"C:\apps\tool.exe", entry.DisplayName);
    }

    [Fact]
    public void DisplayName_ReturnsEmptyString_WhenBothNoteAndExeAreEmpty()
    {
        // tasks.md T053 の記述（"note 空なら (無題)"）は実際の C++ displayName() の挙動とは異なる。
        // displayName() 自体は note が空なら exe を返すだけで、exe も空ならそのまま空文字列になる。
        // "(無題)" への変換は src/cpp/views/TrayIcon.cpp（トレイメニュー構築、呼び出し側）の
        // 責務であり、HotKeyEntry.DisplayName 自体には含めない
        // （詳細は HotKeyEntry.cs の DisplayName の remarks を参照）。
        var entry = new HotKeyEntry { Note = "", Exe = "", Cmd = 200 };

        Assert.Equal(string.Empty, entry.DisplayName);
    }

    // --- AutoCategory ---

    [Theory]
    [InlineData(0, "", HotkeyCategoryKind.Commands)]
    [InlineData(512, "", HotkeyCategoryKind.Mouse)]
    [InlineData(515, "", HotkeyCategoryKind.Joystick)]
    [InlineData(514, "", HotkeyCategoryKind.Remote)]
    [InlineData(0, "http://example.com", HotkeyCategoryKind.WebLinks)]
    [InlineData(0, "https://example.com", HotkeyCategoryKind.WebLinks)]
    [InlineData(0, "ftp://example.com/file", HotkeyCategoryKind.WebLinks)]
    [InlineData(0, "shell:startup", HotkeyCategoryKind.WebLinks)]
    [InlineData(0, @"C:\apps\tool.exe", HotkeyCategoryKind.Programs)]
    [InlineData(0, @"C:\apps\tool.COM", HotkeyCategoryKind.Programs)]
    [InlineData(0, @"C:\apps\tool.bat", HotkeyCategoryKind.Programs)]
    [InlineData(0, @"C:\docs\readme.txt", HotkeyCategoryKind.Documents)]
    [InlineData(0, @"C:\docs\folder", HotkeyCategoryKind.Documents)]
    public void AutoCategory_ReturnsExpectedCategory(int vkey, string exe, HotkeyCategoryKind expected)
    {
        var entry = new HotKeyEntry { Vkey = vkey, Exe = exe };

        Assert.Equal(expected, entry.AutoCategory());
    }

    [Fact]
    public void AutoCategory_AutoStart_TakesPriorityOverTrayMenuAndVkey()
    {
        // C++ 版の if / else-if チェーンで autoStart が最優先。trayMenu や vkey が
        // 他カテゴリを示していても Autorun になることを確認する。
        var entry = new HotKeyEntry { AutoStart = true, TrayMenu = true, Vkey = 512 };

        Assert.Equal(HotkeyCategoryKind.Autorun, entry.AutoCategory());
    }

    [Fact]
    public void AutoCategory_TrayMenu_TakesPriorityOverVkeyChecks()
    {
        var entry = new HotKeyEntry { AutoStart = false, TrayMenu = true, Vkey = 512 };

        Assert.Equal(HotkeyCategoryKind.TrayMenu, entry.AutoCategory());
    }

    [Fact]
    public void AutoCategory_ExeExactlyFourChars_ComparesWholeStringAsExtension()
    {
        // C++ 版 substr の挙動: exe.size() が 4 文字ちょうどのときは、末尾 4 文字ではなく
        // 文字列全体を ext として比較する（exe.size() > 4 が false になるため substr(0) = 全体）。
        var entry = new HotKeyEntry { Exe = ".exe" };

        Assert.Equal(HotkeyCategoryKind.Programs, entry.AutoCategory());
    }

    [Fact]
    public void AutoCategory_ExeShorterThanFourChars_NeverMatchesProgramsExtension()
    {
        // exe.size() >= 4 の外側ガードが false になるため、拡張子判定自体が行われない。
        var entry = new HotKeyEntry { Exe = ".ex" };

        Assert.Equal(HotkeyCategoryKind.Documents, entry.AutoCategory());
    }
}
