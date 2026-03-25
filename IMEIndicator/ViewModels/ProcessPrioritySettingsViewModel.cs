using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IMEIndicator.Models;
using IMEIndicator.Services;
using System.Diagnostics;

namespace IMEIndicator.ViewModels;

/// <summary>
/// プロセス優先度ルール管理のViewModel
/// </summary>
public partial class ProcessPrioritySettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private readonly ProcessPriorityMonitor _monitor;

    public ObservableCollection<ProcessPriorityRule> Rules { get; } = [];

    /// <summary>
    /// PriorityLevel の選択肢一覧（ComboBox バインド用）
    /// </summary>
    public static IReadOnlyList<PriorityLevel> PriorityLevels { get; } =
        Enum.GetValues<PriorityLevel>();

    /// <summary>
    /// このCPUにE-Coreが存在するかどうか
    /// </summary>
    public static bool HasECores => ECoreCpuInfo.Instance.HasECores;

    /// <summary>
    /// システム全体のポーリング間隔（秒）
    /// </summary>
    [ObservableProperty]
    private int _pollingIntervalSeconds;

    public ProcessPrioritySettingsViewModel(SettingsManager settingsManager, ProcessPriorityMonitor monitor)
    {
        _settingsManager = settingsManager;
        _monitor = monitor;
        _pollingIntervalSeconds = settingsManager.Settings.PollingIntervalSeconds;
        LoadRules();
    }

    /// <summary>
    /// 設定からルールを読み込む
    /// </summary>
    public void LoadRules()
    {
        Rules.Clear();
        foreach (var rule in _settingsManager.Settings.ProcessPriorityRules)
            Rules.Add(rule);
    }

    /// <summary>
    /// 新規ルールを追加する
    /// </summary>
    [RelayCommand]
    private void AddRule()
    {
        var rule = new ProcessPriorityRule
        {
            ProcessName = string.Empty,
            TargetPriority = PriorityLevel.Idle,
            MaxBackoffExponent = 6,
            IsEnabled = true
        };
        Rules.Add(rule);
        SaveAndSync();
    }

    /// <summary>
    /// ルールを削除する
    /// </summary>
    [RelayCommand]
    private void RemoveRule(ProcessPriorityRule? rule)
    {
        if (rule == null) return;
        Rules.Remove(rule);
        SaveAndSync();
    }

    /// <summary>
    /// 変更を保存しモニターに同期する
    /// </summary>
    public void SaveAndSync()
    {
        _settingsManager.Settings.ProcessPriorityRules = [.. Rules];
        _settingsManager.Settings.PollingIntervalSeconds = PollingIntervalSeconds;
        _settingsManager.Save();
        _monitor.UpdateRules(_settingsManager.Settings.ProcessPriorityRules);
        _monitor.UpdatePollingInterval(PollingIntervalSeconds);
    }
}
