using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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

  /// <summary>
  /// 反射属性缓存，避免对同类型重复 GetProperty。
  /// </summary>
  private static readonly Dictionary<(Type type, string name), PropertyInfo> PropertyCache = new();

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
      _apiType = null;
      _entryType = null;
      _configTypeEnum = null;

      var assemblies = AppDomain.CurrentDomain.GetAssemblies();
      var preferredAssemblies = assemblies
        .Where(IsLikelyModConfigAssembly)
        .ToArray();

      if (preferredAssemblies.Length > 0)
      {
        TryResolveTypesFromAssemblies(preferredAssemblies);
      }

      if (_apiType == null || _entryType == null || _configTypeEnum == null)
      {
        // 兜底：若按名称筛选失败，再回退扫描所有已加载程序集。
        TryResolveTypesFromAssemblies(assemblies);
      }

      _available = _apiType != null && _entryType != null && _configTypeEnum != null;
    }
    catch (Exception ex)
    {
      _available = false;
      RouteSuggestMod.LogError($"ModConfigBridge detect failed: {ex.Message}");
    }
  }

  /// <summary>
  /// 判断程序集是否可能包含 ModConfig API。
  /// </summary>
  private static bool IsLikelyModConfigAssembly(Assembly assembly)
  {
    var name = assembly?.GetName()?.Name;
    return !string.IsNullOrWhiteSpace(name) && name.Contains("ModConfig", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// 在指定程序集集合中按全名直接定位目标类型，避免全量 GetTypes() 扫描。
  /// </summary>
  private static void TryResolveTypesFromAssemblies(IEnumerable<Assembly> assemblies)
  {
    foreach (var assembly in assemblies)
    {
      _apiType ??= assembly.GetType("ModConfig.ModConfigApi", throwOnError: false, ignoreCase: false);
      _entryType ??= assembly.GetType("ModConfig.ConfigEntry", throwOnError: false, ignoreCase: false);
      _configTypeEnum ??= assembly.GetType("ModConfig.ConfigType", throwOnError: false, ignoreCase: false);

      if (_apiType != null && _entryType != null && _configTypeEnum != null)
      {
        return;
      }
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

      var registerMethod = ResolveRegisterMethod();

      if (registerMethod == null)
      {
        RouteSuggestMod.LogError("ModConfigBridge: Register method not found");
        return;
      }

      var paramCount = registerMethod.GetParameters().Length;
      if (paramCount == 4)
      {
        registerMethod.Invoke(null, new object[] { "RouteSuggest", displayNames["en"], displayNames, entries });
      }
      else if (paramCount == 3)
      {
        registerMethod.Invoke(null, new object[] { "RouteSuggest", displayNames["en"], entries });
      }
      else
      {
        RouteSuggestMod.LogError($"ModConfigBridge: Unsupported Register overload with {paramCount} parameters.");
      }
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"ModConfigBridge register failed: {ex}");
    }
  }

  /// <summary>
  /// 优先按签名解析 Register 重载，避免“参数最多即最新”带来的误判。
  /// </summary>
  private static MethodInfo ResolveRegisterMethod()
  {
    var methods = _apiType
      .GetMethods(BindingFlags.Public | BindingFlags.Static)
      .Where(m => m.Name == "Register")
      .ToList();

    // 优先 4 参：Register(string id, string displayName, IDictionary<string,string> labels, ConfigEntry[] entries)
    foreach (var method in methods)
    {
      var ps = method.GetParameters();
      if (ps.Length != 4) continue;
      if (ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string)) continue;
      if (!typeof(IDictionary<string, string>).IsAssignableFrom(ps[2].ParameterType)) continue;
      if (!IsCompatibleEntryCollectionParameter(ps[3].ParameterType)) continue;

      return method;
    }

    // 其次 3 参：Register(string id, string displayName, ConfigEntry[] entries)
    foreach (var method in methods)
    {
      var ps = method.GetParameters();
      if (ps.Length != 3) continue;
      if (ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string)) continue;
      if (!IsCompatibleEntryCollectionParameter(ps[2].ParameterType)) continue;

      return method;
    }

    // 最后兜底，保持向后兼容。
    return methods
      .OrderByDescending(m => m.GetParameters().Length)
      .FirstOrDefault();
  }

  /// <summary>
  /// 判断参数类型是否可接收构建出的 entries 数组。
  /// </summary>
  private static bool IsCompatibleEntryCollectionParameter(Type parameterType)
  {
    if (parameterType == null || _entryType == null) return false;

    if (parameterType.IsArray)
    {
      var elementType = parameterType.GetElementType();
      return elementType != null && elementType.IsAssignableFrom(_entryType);
    }

    return parameterType.IsAssignableFrom(_entryType.MakeArrayType());
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

            var normalizedMax = Math.Max(oldRange.Max, minVal);
            cfgRef.TargetCounts[pt] = new TargetRange(minVal, normalizedMax);
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

            var normalizedMin = Math.Min(oldRange.Min, maxVal);
            cfgRef.TargetCounts[pt] = new TargetRange(normalizedMin, maxVal);
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

    var builder = new StringBuilder(input.Length);
    bool lastUnderscore = false;

    foreach (var ch in input.Trim())
    {
      if (char.IsLetterOrDigit(ch))
      {
        builder.Append(char.ToLowerInvariant(ch));
        lastUnderscore = false;
      }
      else if (!lastUnderscore)
      {
        builder.Append('_');
        lastUnderscore = true;
      }
    }

    return builder.ToString().Trim('_');
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
    if (obj == null || string.IsNullOrWhiteSpace(name)) return;

    var key = (obj.GetType(), name);
    if (!PropertyCache.TryGetValue(key, out var property))
    {
      property = key.Item1.GetProperty(name);
      PropertyCache[key] = property;
    }

    property?.SetValue(obj, value);
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
