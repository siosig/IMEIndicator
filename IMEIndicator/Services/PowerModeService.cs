namespace IMEIndicator.Services;

/// <summary>
/// Windows 11 電源モード（Overlay）の取得・設定を行うサービス
/// </summary>
public enum PowerMode
{
    BestPowerEfficiency,
    Balanced,
    BestPerformance
}

public static class PowerModeService
{
    private static readonly Guid GuidBestPowerEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid GuidBalanced = Guid.Empty;
    private static readonly Guid GuidBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    /// <summary>
    /// 電源モードの表示名を返す
    /// </summary>
    public static string GetDisplayName(PowerMode mode) => mode switch
    {
        PowerMode.BestPowerEfficiency => "最適な電力効率",
        PowerMode.Balanced => "バランス",
        PowerMode.BestPerformance => "最適なパフォーマンス",
        _ => "不明"
    };

    /// <summary>
    /// 現在の電源モードを取得する
    /// </summary>
    public static PowerMode GetCurrentMode()
    {
        uint result = NativeMethods.PowerGetActualOverlayScheme(out Guid current);
        if (result != 0) return PowerMode.Balanced;

        return current == GuidBestPowerEfficiency ? PowerMode.BestPowerEfficiency
             : current == GuidBestPerformance ? PowerMode.BestPerformance
             : PowerMode.Balanced;
    }

    /// <summary>
    /// 電源モードを設定する
    /// </summary>
    public static bool SetMode(PowerMode mode)
    {
        Guid target = mode switch
        {
            PowerMode.BestPowerEfficiency => GuidBestPowerEfficiency,
            PowerMode.BestPerformance => GuidBestPerformance,
            _ => GuidBalanced
        };

        return NativeMethods.PowerSetActiveOverlayScheme(target) == 0;
    }

    /// <summary>
    /// 電源モードに対応するインジケーター色（HEX）を返す
    /// </summary>
    public static string GetIndicatorColor(PowerMode mode) => mode switch
    {
        PowerMode.BestPowerEfficiency => "#3B82F6",
        PowerMode.Balanced => "#EF4444",
        PowerMode.BestPerformance => "#EAB308",
        _ => "#EF4444"
    };

    /// <summary>
    /// 電源モードをトグルする（最適な電力効率 ↔ バランス）
    /// 最適なパフォーマンスからの場合はバランスへ遷移
    /// </summary>
    public static PowerMode ToggleMode()
    {
        var current = GetCurrentMode();
        var next = current == PowerMode.BestPowerEfficiency
            ? PowerMode.Balanced
            : PowerMode.BestPowerEfficiency;

        // 最適なパフォーマンスからの場合はバランスへ
        if (current == PowerMode.BestPerformance)
            next = PowerMode.Balanced;

        SetMode(next);
        return next;
    }
}
