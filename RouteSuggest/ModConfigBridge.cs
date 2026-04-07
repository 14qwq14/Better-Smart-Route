using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;

namespace RouteSuggest;

/// <summary>
/// 通过反射对接 ModConfig API，把配置面板的变更回调接入本模组刷新链路。
/// </summary>
internal static class ModConfigBridge
{
  private static bool _available;
  private static bool _registered;
  private static Type _apiType;
  private static Type _entryType;
  private static Type _configTypeEnum;

  internal static bool IsAvailable => _available;

  /// <summary>
  /// 延迟一帧注册，避免在 Mod 刚加载时目标 API 尚未准备就绪。
  /// </summary>
  internal static void DeferredRegister()
  {
    var tree = Engine.GetMainLoop() as SceneTree;
    if (tree == null)
    {
      RouteSuggestMod.LogError("ModConfigBridge: SceneTree is not ready");
      return;
    }

    tree.ProcessFrame -= OnNextFrame;
    tree.ProcessFrame += OnNextFrame;
  }

  private static void OnNextFrame()
  {
    var tree = Engine.GetMainLoop() as SceneTree;
    if (tree != null) tree.ProcessFrame -= OnNextFrame;

    Detect();
    if (_available)
    {
      Register();
      RouteSuggestMod.Log("ModConfigBridge: registration finished");
    }
    else
    {
      RouteSuggestMod.Log("ModConfigBridge: ModConfig API not found, skip registration");
    }
  }

  private static void Detect()
  {
    try
    {
      var allTypes = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a =>
        {
          try { return a.GetTypes(); }
          catch { return Type.EmptyTypes; }
        })
        .ToArray();

      _apiType = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ModConfigApi");
      _entryType = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ConfigEntry");
      _configTypeEnum = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ConfigType");
      _available = _apiType != null && _entryType != null && _configTypeEnum != null;
    }
    catch (Exception ex)
    {
      _available = false;
      RouteSuggestMod.LogError($"ModConfigBridge detect failed: {ex.Message}");
    }
  }

  private static void Register()
  {
    if (_registered) return;
    _registered = true;

    try
    {
      var entries = BuildEntries();
      var displayNames = new Dictionary<string, string>
      {
        ["en"] = "Route Suggest",
        ["zhs"] = "路线推荐"
      };

      var registerMethod = _apiType.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.Name == "Register")
        .OrderByDescending(m => m.GetParameters().Length)
        .FirstOrDefault();

      if (registerMethod == null)
      {
        RouteSuggestMod.LogError("ModConfigBridge: Register method not found");
        return;
      }

      if (registerMethod.GetParameters().Length == 4)
      {
        registerMethod.Invoke(null, new object[] { "RouteSuggest", displayNames["en"], displayNames, entries });
      }
      else
      {
        registerMethod.Invoke(null, new object[] { "RouteSuggest", displayNames["en"], entries });
      }
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"ModConfigBridge register failed: {ex}");
    }
  }

  private static readonly MapPointType[] TrackedTypes = new[]
  {
    MapPointType.Elite,
    MapPointType.RestSite,
    MapPointType.Monster,
    MapPointType.Unknown,
    MapPointType.Treasure,
    MapPointType.Shop
  };

  private static string GetTranslatedTypeName(MapPointType type)
  {
    return type switch
    {
      MapPointType.Elite => "精英",
      MapPointType.RestSite => "营地",
      MapPointType.Monster => "怪物",
      MapPointType.Unknown => "问号",
      MapPointType.Treasure => "宝箱",
      MapPointType.Shop => "商店",
      _ => type.ToString()
    };
  }

  private static Array BuildEntries()
  {
    var list = new List<object>();
    var keyUsage = new Dictionary<string, int>(StringComparer.Ordinal);

    list.Add(Entry(cfg =>
    {
      Set(cfg, "Label", "General Settings");
      Set(cfg, "Labels", L("General Settings", "常规设置"));
      Set(cfg, "Type", EnumVal("Header"));
    }));

    list.Add(Entry(cfg =>
    {
      Set(cfg, "Key", "HighlightType");
      Set(cfg, "Label", "Highlight Style");
      Set(cfg, "Labels", L("Highlight Style", "高亮样式"));
      Set(cfg, "Type", EnumVal("Dropdown"));
      Set(cfg, "DefaultValue", (object)ConfigManager.CurrentHighlightType.ToString());
      Set(cfg, "Options", Enum.GetNames(typeof(HighlightType)));
      Set(cfg, "OnChanged", new Action<object>(v =>
      {
        if (!Enum.TryParse<HighlightType>(Convert.ToString(v), out var t)) return;

        ConfigManager.CurrentHighlightType = t;
        PersistConfigAndRefresh("HighlightType");
      }));
    }));

    list.Add(Entry(cfg =>
    {
      Set(cfg, "Label", "Path Configurations");
      Set(cfg, "Labels", L("Path Configurations", "路径配置"));
      Set(cfg, "Type", EnumVal("Header"));
    }));

    for (int i = 0; i < ConfigManager.PathConfigs.Count; i++)
    {
      var ci = i;
      if (!TryGetConfig(ci, out var pathConfig)) continue;
      var prefix = BuildStablePathEntryPrefix(pathConfig, ci, keyUsage);

      list.Add(Entry(cfg => Set(cfg, "Type", EnumVal("Separator"))));

      list.Add(Entry(cfg =>
      {
        Set(cfg, "Label", pathConfig.Name);
        Set(cfg, "Type", EnumVal("Header"));
      }));

      list.Add(Entry(cfg =>
      {
        Set(cfg, "Key", $"{prefix}_Enabled");
        Set(cfg, "Label", "Enabled");
        Set(cfg, "Labels", L("Enabled", "开启"));
        Set(cfg, "Type", EnumVal("Toggle"));
        Set(cfg, "DefaultValue", (object)pathConfig.Enabled);
        Set(cfg, "OnChanged", new Action<object>(v =>
        {
          if (!TryGetConfig(ci, out var cfgRef)) return;
          cfgRef.Enabled = Convert.ToBoolean(v);
          PersistConfigAndRefresh($"{cfgRef.Name}.Enabled");
        }));
      }));

      list.Add(Entry(cfg =>
      {
        Set(cfg, "Key", $"{prefix}_Color");
        Set(cfg, "Label", "Color");
        Set(cfg, "Labels", L("Color", "颜色"));
        Set(cfg, "Type", EnumVal("ColorPicker"));
        Set(cfg, "DefaultValue", (object)$"#{pathConfig.Color.ToHtml(false)}");
        Set(cfg, "OnChanged", new Action<object>(v =>
        {
          if (!TryGetConfig(ci, out var cfgRef)) return;
          cfgRef.Color = ConfigManager.ParseColor(Convert.ToString(v));
          PersistConfigAndRefresh($"{cfgRef.Name}.Color");
        }));
      }));

      list.Add(Entry(cfg =>
      {
        Set(cfg, "Key", $"{prefix}_Priority");
        Set(cfg, "Label", "Priority");
        Set(cfg, "Labels", L("Priority", "优先级"));
        Set(cfg, "Type", EnumVal("Slider"));
        Set(cfg, "DefaultValue", (object)(float)pathConfig.Priority);
        Set(cfg, "Min", 0f);
        Set(cfg, "Max", 200f);
        Set(cfg, "Step", 1f);
        Set(cfg, "Format", "F0");
        Set(cfg, "OnChanged", new Action<object>(v =>
        {
          if (!TryGetConfig(ci, out var cfgRef)) return;
          cfgRef.Priority = (int)Math.Round(Convert.ToSingle(v));
          PersistConfigAndRefresh($"{cfgRef.Name}.Priority");
        }));
      }));

      foreach (var pt in TrackedTypes)
      {
        var ptName = pt.ToString();
        var zhsName = GetTranslatedTypeName(pt);
        int currentMin = pathConfig.TargetCounts != null && pathConfig.TargetCounts.TryGetValue(pt, out var tr) ? tr.Min : 0;
        int currentMax = pathConfig.TargetCounts != null && pathConfig.TargetCounts.TryGetValue(pt, out var trMax) ? trMax.Max : 15;

        list.Add(Entry(cfg =>
        {
          Set(cfg, "Key", $"{prefix}_Min_{ptName}");
          Set(cfg, "Label", $"  » {ptName} (Min)");
          Set(cfg, "Labels", L($"  » {ptName} Min", $"  » {zhsName} (最小)"));
          Set(cfg, "Type", EnumVal("Slider"));
          Set(cfg, "DefaultValue", (object)(float)currentMin);
          Set(cfg, "Min", 0f);
          Set(cfg, "Max", 15f);
          Set(cfg, "Step", 1f);
          Set(cfg, "Format", "F0");
          Set(cfg, "OnChanged", new Action<object>(v =>
          {
            if (!TryGetConfig(ci, out var cfgRef)) return;

            int minVal = (int)Math.Round(Convert.ToSingle(v));
            cfgRef.TargetCounts ??= new Dictionary<MapPointType, TargetRange>();
            if (!cfgRef.TargetCounts.TryGetValue(pt, out var oldRange)) oldRange = new TargetRange(minVal, 15);

            cfgRef.TargetCounts[pt] = new TargetRange(minVal, oldRange.Max);
            PersistConfigAndRefresh($"{cfgRef.Name}.{ptName}.Min");
          }));
        }));

        list.Add(Entry(cfg =>
        {
          Set(cfg, "Key", $"{prefix}_Max_{ptName}");
          Set(cfg, "Label", $"  » {ptName} (Max)");
          Set(cfg, "Labels", L($"  » {ptName} Max", $"  » {zhsName} (最大)"));
          Set(cfg, "Type", EnumVal("Slider"));
          Set(cfg, "DefaultValue", (object)(float)currentMax);
          Set(cfg, "Min", 0f);
          Set(cfg, "Max", 15f);
          Set(cfg, "Step", 1f);
          Set(cfg, "Format", "F0");
          Set(cfg, "OnChanged", new Action<object>(v =>
          {
            if (!TryGetConfig(ci, out var cfgRef)) return;

            int maxVal = (int)Math.Round(Convert.ToSingle(v));
            cfgRef.TargetCounts ??= new Dictionary<MapPointType, TargetRange>();
            if (!cfgRef.TargetCounts.TryGetValue(pt, out var oldRange)) oldRange = new TargetRange(0, maxVal);

            cfgRef.TargetCounts[pt] = new TargetRange(oldRange.Min, maxVal);
            PersistConfigAndRefresh($"{cfgRef.Name}.{ptName}.Max");
          }));
        }));
      }
    }

    var result = Array.CreateInstance(_entryType, list.Count);
    for (int i = 0; i < list.Count; i++) result.SetValue(list[i], i);
    return result;
  }

  /// <summary>
  /// 为路径配置构建稳定的键前缀，优先使用配置名，重名时自动追加序号。
  /// </summary>
  private static string BuildStablePathEntryPrefix(PathConfig pathConfig, int fallbackIndex, Dictionary<string, int> usage)
  {
    var baseKey = NormalizeKey(pathConfig?.Name);
    if (string.IsNullOrEmpty(baseKey)) baseKey = $"path_{fallbackIndex}";

    if (!usage.TryGetValue(baseKey, out var count))
    {
      usage[baseKey] = 1;
      return $"Path_{baseKey}";
    }

    count++;
    usage[baseKey] = count;
    return $"Path_{baseKey}_{count}";
  }

  /// <summary>
  /// 规范化配置名为可用于配置键的字符串。
  /// </summary>
  private static string NormalizeKey(string input)
  {
    if (string.IsNullOrWhiteSpace(input)) return string.Empty;

    var chars = input
      .Trim()
      .ToLowerInvariant()
      .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
      .ToArray();

    var normalized = new string(chars);
    while (normalized.Contains("__", StringComparison.Ordinal))
    {
      normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
    }

    return normalized.Trim('_');
  }

  /// <summary>
  /// 将配置改动持久化并立刻触发地图重绘。
  /// </summary>
  private static void PersistConfigAndRefresh(string reason)
  {
    try
    {
      ConfigManager.SaveConfiguration();

      // 双保险：即使事件链断开，也确保改配置后马上重算+重绘。
      RouteCalculator.InvalidateCache();
      RouteCalculator.UpdateBestPath();
      MapHighlighter.RequestHighlightOnMapOpen();

      RouteSuggestMod.Log($"ModConfig changed and refreshed: {reason}");
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"ModConfig change apply failed ({reason}): {ex.Message}");
    }
  }

  private static bool TryGetConfig(int index, out PathConfig config)
  {
    config = null;
    if (index < 0 || index >= ConfigManager.PathConfigs.Count) return false;

    config = ConfigManager.PathConfigs[index];
    return config != null;
  }

  private static object Entry(Action<object> configure)
  {
    var inst = Activator.CreateInstance(_entryType);
    configure(inst);
    return inst;
  }

  private static void Set(object obj, string name, object value)
  {
    obj.GetType().GetProperty(name)?.SetValue(obj, value);
  }

  private static Dictionary<string, string> L(string en, string zhs)
  {
    return new Dictionary<string, string> { ["en"] = en, ["zhs"] = zhs };
  }

  private static object EnumVal(string name)
  {
    return Enum.Parse(_configTypeEnum, name);
  }
}
