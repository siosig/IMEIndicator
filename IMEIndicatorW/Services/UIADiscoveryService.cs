using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using IMEIndicatorClock.Models;

namespace IMEIndicatorClock.Services;

/// <summary>
/// UI Automation 発見ログを管理するサービス
/// </summary>
public class UIADiscoveryService
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IMEIndicatorW",
        "uia_discovery_log.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private UIADiscoveryLog _log = new();
    private readonly HashSet<string> _knownKeys = new();

    public static UIADiscoveryService Instance { get; } = new();

    private UIADiscoveryService()
    {
        Load();
    }

    /// <summary>
    /// ログファイルから読み込み
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var json = File.ReadAllText(LogFilePath);
                _log = JsonSerializer.Deserialize<UIADiscoveryLog>(json) ?? new UIADiscoveryLog();

                // 既知のキーをセットに追加
                _knownKeys.Clear();
                foreach (var elem in _log.Elements)
                {
                    _knownKeys.Add(elem.Key);
                }

                DbgLog.Log(3, $"UIADiscoveryService: {_log.Elements.Count} 件の記録を読み込みました");
            }
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "UIADiscoveryService Load");
            _log = new UIADiscoveryLog();
        }
    }

    /// <summary>
    /// ログファイルに保存
    /// </summary>
    public void Save()
    {
        try
        {
            System.Windows.MessageBox.Show($"保存開始\nElements: {_log.Elements.Count}\nPath: {LogFilePath}", "Debug");

            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _log.LastUpdated = DateTime.Now;
            var json = JsonSerializer.Serialize(_log, JsonOptions);
            File.WriteAllText(LogFilePath, json);

            System.Windows.MessageBox.Show("保存完了!", "Debug");
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "UIADiscoveryService Save");
        }
    }

    /// <summary>
    /// 要素を記録（新規または更新）- AutomationElement版
    /// </summary>
    /// <returns>true = 初めて見た要素</returns>
    public bool RecordElement(AutomationElement element, string parentName, string parentControlType, string activeWindowTitle)
    {
        try
        {
            var name = element.Current.Name ?? "";
            var className = element.Current.ClassName ?? "";
            var controlType = element.Current.ControlType;
            var localizedType = element.Current.LocalizedControlType ?? "";
            var rect = element.Current.BoundingRectangle;
            var rectStr = rect.IsEmpty ? "N/A" : $"l:{rect.Left:F0} t:{rect.Top:F0} r:{rect.Right:F0} b:{rect.Bottom:F0}";

            return RecordElementData(name, className, controlType.Id, controlType.ProgrammaticName,
                localizedType, rectStr, parentName, parentControlType, activeWindowTitle);
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "UIADiscoveryService RecordElement");
            return false;
        }
    }

    /// <summary>
    /// 要素を記録（新規または更新）- データ版（スレッドセーフ）
    /// </summary>
    /// <returns>true = 初めて見た要素</returns>
    public bool RecordElementData(string name, string className, int controlTypeId, string controlTypeName,
        string localizedType, string rectStr, string parentName, string parentControlType, string activeWindowTitle)
    {
        try
        {
            // キー生成: 親名|ControlTypeId|Name
            var key = $"{parentName}|0x{controlTypeId:X}|{name}";

            var isFirstSeen = !_knownKeys.Contains(key);

            if (isFirstSeen)
            {
                // 新規要素
                var newElement = new UIADiscoveredElement
                {
                    Key = key,
                    Name = name,
                    ClassName = className,
                    ControlTypeId = controlTypeId,
                    ControlTypeName = controlTypeName,
                    LocalizedControlType = localizedType,
                    ParentName = parentName,
                    ParentControlType = parentControlType,
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now,
                    SeenCount = 1,
                    ActiveWindowTitle = activeWindowTitle,
                    BoundingRectangle = rectStr
                };

                _log.Elements.Add(newElement);
                _knownKeys.Add(key);

                DbgLog.Log(2, $"[UIA NEW] {key}");
            }
            else
            {
                // 既存要素を更新
                var existing = _log.Elements.FirstOrDefault(e => e.Key == key);
                if (existing != null)
                {
                    existing.LastSeen = DateTime.Now;
                    existing.SeenCount++;
                    existing.BoundingRectangle = rectStr;
                }
            }

            return isFirstSeen;
        }
        catch (Exception ex)
        {
            DbgLog.Ex(ex, "UIADiscoveryService RecordElementData");
            return false;
        }
    }

    /// <summary>
    /// 既知の要素かどうか
    /// </summary>
    public bool IsKnown(string key)
    {
        return _knownKeys.Contains(key);
    }

    /// <summary>
    /// キーを生成
    /// </summary>
    public static string MakeKey(string parentName, int controlTypeId, string name)
    {
        return $"{parentName}|0x{controlTypeId:X}|{name}";
    }

    /// <summary>
    /// 全ての記録を取得
    /// </summary>
    public IReadOnlyList<UIADiscoveredElement> GetAllElements()
    {
        return _log.Elements.AsReadOnly();
    }

    /// <summary>
    /// IMEマーク関連の要素のみ取得
    /// </summary>
    public IEnumerable<UIADiscoveredElement> GetIMEMarkElements()
    {
        return _log.Elements.Where(e => e.IsIMEMark);
    }

    /// <summary>
    /// IME状態テキスト要素のみ取得
    /// </summary>
    public IEnumerable<UIADiscoveredElement> GetIMEStateTextElements()
    {
        return _log.Elements.Where(e => e.IsIMEStateText);
    }

    /// <summary>
    /// ログファイルのパス
    /// </summary>
    public string GetLogFilePath() => LogFilePath;

    /// <summary>
    /// 記録をクリア
    /// </summary>
    public void Clear()
    {
        _log.Elements.Clear();
        _knownKeys.Clear();
        Save();
    }
}
