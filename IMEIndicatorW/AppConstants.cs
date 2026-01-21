namespace IMEIndicatorClock;

/// <summary>
/// アプリケーション共通定数
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// アプリケーション名（アセンブリ名から取得）
    /// 設定ファイルやログのフォルダ名に使用
    /// </summary>
    public static readonly string AppName =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Name
        ?? "IMEIndicatorW";
}
