#Requires -RunAsAdministrator
<#
.SYNOPSIS
    IMEIndicatorClockW セットアップスクリプト

.DESCRIPTION
    タスクスケジューラーに登録し、Windowsログイン時に管理者権限で
    自動起動するよう設定します。UACプロンプトなしで起動されます。

.PARAMETER Uninstall
    タスクスケジューラーからの登録解除

.EXAMPLE
    # 登録（管理者PowerShellで実行）
    .\setup.ps1

    # 解除
    .\setup.ps1 -Uninstall
#>

param(
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$TaskName = "IMEIndicatorClockW"
$ExeName  = "IMEIndicatorClockW.exe"

# EXEのパスをスクリプトのあるディレクトリから解決
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExePath   = Join-Path $ScriptDir $ExeName

# publish/ フォルダも探す
if (-not (Test-Path $ExePath)) {
    $ExePath = Join-Path $ScriptDir "publish\$ExeName"
}

# ==================== 解除 ====================
if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "タスクスケジューラーから '$TaskName' を解除しました。" -ForegroundColor Green
    } else {
        Write-Host "タスク '$TaskName' は登録されていません。" -ForegroundColor Yellow
    }
    exit 0
}

# ==================== 登録 ====================
if (-not (Test-Path $ExePath)) {
    Write-Error "EXEが見つかりません: $ExePath`n先にリリースビルドを行ってください。"
    exit 1
}

Write-Host "EXEパス: $ExePath"

$Action    = New-ScheduledTaskAction -Execute $ExePath
$Trigger   = New-ScheduledTaskTrigger -AtLogOn
$Settings  = New-ScheduledTaskSettingsSet `
                 -AllowStartIfOnBatteries `
                 -DontStopIfGoingOnBatteries `
                 -ExecutionTimeLimit 0 `
                 -RestartCount 3 `
                 -RestartInterval (New-TimeSpan -Minutes 1)
$Principal = New-ScheduledTaskPrincipal `
                 -UserId $env:USERNAME `
                 -LogonType Interactive `
                 -RunLevel Highest

Register-ScheduledTask `
    -TaskName  $TaskName `
    -Action    $Action `
    -Trigger   $Trigger `
    -Settings  $Settings `
    -Principal $Principal `
    -Force | Out-Null

Write-Host ""
Write-Host "登録完了: '$TaskName'" -ForegroundColor Green
Write-Host "次回ログイン時から管理者権限・UACなしで自動起動します。"
Write-Host ""
Write-Host "今すぐ起動する場合:"
Write-Host "  Start-ScheduledTask -TaskName '$TaskName'" -ForegroundColor Cyan
Write-Host ""
Write-Host "解除する場合:"
Write-Host "  .\setup.ps1 -Uninstall" -ForegroundColor Cyan
