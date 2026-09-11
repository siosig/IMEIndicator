# Copyright (C) 2026 IMEIndicator Project
#
# This program is free software; you can redistribute it and/or modify it
# under the terms of the GNU General Public License v2 or later.
# See COPYING in the repository root for the full license text.

<#
.SYNOPSIS
    C++ 版と C# 版のビルド時間・行数を計測し、SC-003/SC-004/SC-008 の達成状況を表示する
    （specs/014-port-to-csharp/tasks.md T045/T047、quickstart.md「ビルド時間の比較」）。

.DESCRIPTION
    次を計測する。
      1. C++ 版のクリーンビルド（vcpkg 依存込み）と、1 ファイル変更後の再ビルド
      2. C# 版のクリーンビルドと再ビルド
      3. src/csharp 配下の *.cs 行数（bin/obj 除外）と、現行 C++ 版の行数（12,603 行、SC-008）との比較

    PowerShell 7 専用（プロジェクトの CLAUDE.md 規約により bash は使用しない）。
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$cmakeCandidates = @(
    'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    'C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
)
$cmakeExe = $cmakeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $cmakeExe) {
    $onPath = Get-Command cmake -ErrorAction SilentlyContinue
    if ($onPath) { $cmakeExe = $onPath.Source }
}
$cppAvailable = [bool]$cmakeExe
if (-not $cppAvailable) {
    Write-Warning 'cmake.exe が見つかりません（Visual Studio の C++ ツールチェーンが現在このマシンに存在しない可能性）。C++ 側の計測はスキップし、C# 側のみ計測します。'
}

function Measure-Step {
    param(
        [string]$Label,
        [scriptblock]$Action
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Action | Out-Null
    $sw.Stop()
    [PSCustomObject]@{
        Label   = $Label
        Seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
    }
}

Write-Host '=== ビルド時間計測 ===' -ForegroundColor Cyan

$results = @()

if ($cppAvailable) {
    Write-Host '-- C++: クリーンビルド --'
    $results += Measure-Step 'C++ クリーンビルド' {
        & $cmakeExe --build --preset windows-x64-release --clean-first
    }

    Write-Host '-- C++: 再ビルド（無変更） --'
    $results += Measure-Step 'C++ 再ビルド' {
        & $cmakeExe --build --preset windows-x64-release
    }
}

Write-Host '-- C#: クリーンビルド --'
$results += Measure-Step 'C# クリーンビルド' {
    dotnet build IMEIndicator.slnx -c Release --no-incremental
}

Write-Host '-- C#: 再ビルド（無変更） --'
$results += Measure-Step 'C# 再ビルド' {
    dotnet build IMEIndicator.slnx -c Release
}

Write-Host ''
Write-Host '=== 結果（SC-003: クリーンビルドが現行版より短い / SC-004: 再ビルドが現行版より短い） ===' -ForegroundColor Cyan
$results | Format-Table -AutoSize

$cppClean = ($results | Where-Object Label -eq 'C++ クリーンビルド').Seconds
$csClean = ($results | Where-Object Label -eq 'C# クリーンビルド').Seconds
$cppIncr = ($results | Where-Object Label -eq 'C++ 再ビルド').Seconds
$csIncr = ($results | Where-Object Label -eq 'C# 再ビルド').Seconds

if ($cppAvailable) {
    Write-Host ("SC-003 クリーンビルド: C# {0}s vs C++ {1}s → {2}" -f $csClean, $cppClean, $(if ($csClean -lt $cppClean) { '達成' } else { '未達成' }))
    Write-Host ("SC-004 再ビルド:       C# {0}s vs C++ {1}s → {2}" -f $csIncr, $cppIncr, $(if ($csIncr -lt $cppIncr) { '達成' } else { '未達成' }))
} else {
    Write-Host ("C# クリーンビルド: {0}s / C# 再ビルド: {1}s（C++ 側は cmake 不在のため比較対象なし）" -f $csClean, $csIncr)
}

Write-Host ''
Write-Host '=== 行数計測（SC-008: 移植後のソース行数が現行版（12,603 行）を上回らない） ===' -ForegroundColor Cyan

$csFiles = Get-ChildItem 'src\csharp' -Recurse -File -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$csLines = ($csFiles | Get-Content | Measure-Object -Line).Lines

$csTestFiles = Get-ChildItem 'tests\csharp' -Recurse -File -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$csTestLines = ($csTestFiles | Get-Content | Measure-Object -Line).Lines

Write-Host ("src/csharp:   {0} ファイル, {1} 行" -f $csFiles.Count, $csLines)
Write-Host ("tests/csharp: {0} ファイル, {1} 行" -f $csTestFiles.Count, $csTestLines)
Write-Host ("現行 C++ 版:  113 ファイル, 12,603 行（src/cpp のみ。基準値）")
Write-Host ("SC-008: src/csharp {0} 行 vs 12,603 行 → {1}" -f $csLines, $(if ($csLines -le 12603) { '達成' } else { '未達成' }))
