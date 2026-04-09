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

  /// <summary>
  /// 新增路线时的默认名称。
  /// </summary>
  private const string DefaultNewRouteName = "New Route";

  /// <summary>
  /// 新增路线输入框的暂存名称。
  /// </summary>
  private static string _pendingNewRouteName = DefaultNewRouteName;

  /// <summary>
  /// 目标计数范围输入下限。
  /// </summary>
  private const int TargetCountMinValue = 0;

  /// <summary>
  /// 目标计数范围输入上限（与地图层数一致）。
  /// </summary>
  private const int TargetCountMaxValue = 15;

  /// <summary>
  /// 新增路线时的建议颜色轮换。
  /// </summary>
  private static readonly Color[] SuggestedRouteColors =
  {
    new Color(0.25f, 0.75f, 1f, 1f),
    new Color(1f, 0.55f, 0.25f, 1f),
    new Color(0.7f, 0.45f, 1f, 1f),
    new Color(0.25f, 0.9f, 0.5f, 1f),
    new Color(1f, 0.8f, 0.2f, 1f),
    new Color(1f, 0.4f, 0.6f, 1f)
  };

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

  private static readonly MapPointType[] DefaultTrackedTypes = new[]
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
    var editableTargetTypes = BuildEditableTargetTypes();
    var rangeEditorType = ResolveRangeEditorType(out var useCompactRangeEditor);
    var routeNameInputType = ResolveRouteNameInputType(out var routeNameInputSupported);
    var actionType = ResolveActionType(out var actionUsesToggleFallback);

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
      Set(cfg, "Key", "MaxPathsPerConfig");
      Set(cfg, "Label", "Max Routes Per Config");
      Set(cfg, "Labels", L("Max Routes Per Config", "每条策略最多路线数"));
      Set(cfg, "Type", EnumVal("Slider"));
      Set(cfg, "DefaultValue", (object)(float)ConfigManager.MaxPathsPerConfig);
      Set(cfg, "Min", 1f);
      Set(cfg, "Max", 12f);
      Set(cfg, "Step", 1f);
      Set(cfg, "Format", "F0");
      Set(cfg, "OnChanged", new Action<object>(v =>
      {
        ConfigManager.MaxPathsPerConfig = (int)Math.Round(Convert.ToSingle(v));
        PersistConfigAndRefresh("MaxPathsPerConfig");
      }));
    }));

    list.Add(Entry(cfg =>
    {
      Set(cfg, "Label", "Path Configurations");
      Set(cfg, "Labels", L("Path Configurations", "路径配置"));
      Set(cfg, "Type", EnumVal("Header"));
    }));

    list.Add(Entry(cfg =>
    {
      Set(cfg, "Label", "Path Management");
      Set(cfg, "Labels", L("Path Management", "路线管理"));
      Set(cfg, "Type", EnumVal("Header"));
    }));

    if (routeNameInputSupported)
    {
      list.Add(Entry(cfg =>
      {
        Set(cfg, "Key", "PathAddName");
        Set(cfg, "Label", "New Path Name");
        Set(cfg, "Labels", L("New Path Name", "新增路线名称"));
        Set(cfg, "Type", routeNameInputType);
        Set(cfg, "DefaultValue", (object)_pendingNewRouteName);
        Set(cfg, "OnChanged", new Action<object>(v =>
        {
          var candidate = Convert.ToString(v)?.Trim();
          _pendingNewRouteName = string.IsNullOrWhiteSpace(candidate) ? DefaultNewRouteName : candidate;
        }));
      }));
    }

    list.Add(Entry(cfg =>
    {
      Set(cfg, "Key", "PathAddAction");
      Set(cfg, "Label", "Add Path");
      Set(cfg, "Labels", L("Add Path", "新增路线"));
      Set(cfg, "Type", actionType);
      if (actionUsesToggleFallback) Set(cfg, "DefaultValue", (object)false);
      Set(cfg, "OnChanged", new Action<object>(v =>
      {
        if (actionUsesToggleFallback && !Convert.ToBoolean(v)) return;

        AddPathConfiguration(_pendingNewRouteName);
      }));
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
        Set(cfg, "Key", $"{prefix}_Remove");
        Set(cfg, "Label", "Remove This Path");
        Set(cfg, "Labels", L("Remove This Path", "删除此路线"));
        Set(cfg, "Type", actionType);
        if (actionUsesToggleFallback) Set(cfg, "DefaultValue", (object)false);
        Set(cfg, "OnChanged", new Action<object>(v =>
        {
          if (actionUsesToggleFallback && !Convert.ToBoolean(v)) return;
          RemovePathConfiguration(ci);
        }));
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

      foreach (var pt in editableTargetTypes)
      {
        var ptName = pt.ToString();
        var zhsName = GetTranslatedTypeName(pt);
        var currentRange = GetCurrentTargetRange(pathConfig, pt);

        if (useCompactRangeEditor)
        {
          list.Add(Entry(cfg =>
          {
            Set(cfg, "Key", $"{prefix}_Range_{ptName}");
            Set(cfg, "Label", $"  » {ptName} (Range)");
            Set(cfg, "Labels", L($"  » {ptName} Range", $"  » {zhsName} (范围)"));
            Set(cfg, "Type", rangeEditorType);
            Set(cfg, "DefaultValue", (object)$"{currentRange.Min}-{currentRange.Max}");
            Set(cfg, "OnChanged", new Action<object>(v =>
            {
              if (!TryGetConfig(ci, out var cfgRef)) return;
              if (!TryParseTargetRange(v, out var parsedRange))
              {
                RouteSuggestMod.LogWarning($"Invalid range input for {cfgRef.Name}.{ptName}: '{Convert.ToString(v)}'. Expected formats like '3-6' or '4'.");
                return;
              }

              cfgRef.TargetCounts ??= new Dictionary<MapPointType, TargetRange>();
              cfgRef.TargetCounts[pt] = parsedRange;
              PersistConfigAndRefresh($"{cfgRef.Name}.{ptName}.Range");
            }));
          }));
        }
        else
        {
          list.Add(Entry(cfg =>
          {
            Set(cfg, "Key", $"{prefix}_Min_{ptName}");
            Set(cfg, "Label", $"  » {ptName} (Min)");
            Set(cfg, "Labels", L($"  » {ptName} Min", $"  » {zhsName} (最小)"));
            Set(cfg, "Type", EnumVal("Slider"));
            Set(cfg, "DefaultValue", (object)(float)currentRange.Min);
            Set(cfg, "Min", (float)TargetCountMinValue);
            Set(cfg, "Max", (float)TargetCountMaxValue);
            Set(cfg, "Step", 1f);
            Set(cfg, "Format", "F0");
            Set(cfg, "OnChanged", new Action<object>(v =>
            {
              if (!TryGetConfig(ci, out var cfgRef)) return;

              int minVal = ClampTargetCount((int)Math.Round(Convert.ToSingle(v)));
              cfgRef.TargetCounts ??= new Dictionary<MapPointType, TargetRange>();
              if (!cfgRef.TargetCounts.TryGetValue(pt, out var oldRange)) oldRange = new TargetRange(minVal, TargetCountMaxValue);

              var normalizedRange = NormalizeTargetRange(minVal, oldRange.Max);
              cfgRef.TargetCounts[pt] = normalizedRange;
              PersistConfigAndRefresh($"{cfgRef.Name}.{ptName}.Min");
            }));
          }));

          list.Add(Entry(cfg =>
          {
            Set(cfg, "Key", $"{prefix}_Max_{ptName}");
            Set(cfg, "Label", $"  » {ptName} (Max)");
            Set(cfg, "Labels", L($"  » {ptName} Max", $"  » {zhsName} (最大)"));
            Set(cfg, "Type", EnumVal("Slider"));
            Set(cfg, "DefaultValue", (object)(float)currentRange.Max);
            Set(cfg, "Min", (float)TargetCountMinValue);
            Set(cfg, "Max", (float)TargetCountMaxValue);
            Set(cfg, "Step", 1f);
            Set(cfg, "Format", "F0");
            Set(cfg, "OnChanged", new Action<object>(v =>
            {
              if (!TryGetConfig(ci, out var cfgRef)) return;

              int maxVal = ClampTargetCount((int)Math.Round(Convert.ToSingle(v)));
              cfgRef.TargetCounts ??= new Dictionary<MapPointType, TargetRange>();
              if (!cfgRef.TargetCounts.TryGetValue(pt, out var oldRange)) oldRange = new TargetRange(TargetCountMinValue, maxVal);

              var normalizedRange = NormalizeTargetRange(oldRange.Min, maxVal);
              cfgRef.TargetCounts[pt] = normalizedRange;
              PersistConfigAndRefresh($"{cfgRef.Name}.{ptName}.Max");
            }));
          }));
        }
      }
    }

    var result = Array.CreateInstance(_entryType, list.Count);
    for (int i = 0; i < list.Count; i++) result.SetValue(list[i], i);
    return result;
  }

  /// <summary>
  /// 动态构建可编辑的房间类型集合：默认类型 + 当前配置中出现过的类型。
  /// </summary>
  private static IReadOnlyList<MapPointType> BuildEditableTargetTypes()
  {
    var set = new SortedSet<MapPointType>(Comparer<MapPointType>.Create((a, b) => ((int)a).CompareTo((int)b)));

    foreach (var pointType in DefaultTrackedTypes)
    {
      set.Add(pointType);
    }

    foreach (var config in ConfigManager.PathConfigs)
    {
      if (config?.TargetCounts == null) continue;

      foreach (var pointType in config.TargetCounts.Keys)
      {
        set.Add(pointType);
      }
    }

    return set.ToList();
  }

  /// <summary>
  /// 读取当前配置中某房间类型的范围（不存在时回退默认范围）。
  /// </summary>
  private static TargetRange GetCurrentTargetRange(PathConfig pathConfig, MapPointType pointType)
  {
    if (pathConfig?.TargetCounts != null && pathConfig.TargetCounts.TryGetValue(pointType, out var existingRange))
    {
      return NormalizeTargetRange(existingRange.Min, existingRange.Max);
    }

    return new TargetRange(TargetCountMinValue, TargetCountMaxValue);
  }

  /// <summary>
  /// 规范化范围，自动排序并夹取到合法区间。
  /// </summary>
  private static TargetRange NormalizeTargetRange(int min, int max)
  {
    var normalizedMin = ClampTargetCount(Math.Min(min, max));
    var normalizedMax = ClampTargetCount(Math.Max(min, max));
    return new TargetRange(normalizedMin, normalizedMax);
  }

  /// <summary>
  /// 对目标计数做上下界夹取。
  /// </summary>
  private static int ClampTargetCount(int value)
  {
    return Math.Clamp(value, TargetCountMinValue, TargetCountMaxValue);
  }

  /// <summary>
  /// 解析文本范围输入：支持 "3-6"、"3:6"、"3,6"、"4"。
  /// </summary>
  private static bool TryParseTargetRange(object rawValue, out TargetRange range)
  {
    range = new TargetRange(TargetCountMinValue, TargetCountMaxValue);

    var text = Convert.ToString(rawValue)?.Trim();
    if (string.IsNullOrWhiteSpace(text)) return false;

    var normalized = text
      .Replace("，", ",", StringComparison.Ordinal)
      .Replace("：", ":", StringComparison.Ordinal)
      .Replace("～", "-", StringComparison.Ordinal)
      .Replace("—", "-", StringComparison.Ordinal)
      .Replace("至", "-", StringComparison.Ordinal)
      .Replace(" ", string.Empty, StringComparison.Ordinal);

    var parts = normalized.Split(new[] { '-', '~', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 1 && int.TryParse(parts[0], out var single))
    {
      var value = ClampTargetCount(single);
      range = new TargetRange(value, value);
      return true;
    }

    if (parts.Length >= 2 && int.TryParse(parts[0], out var min) && int.TryParse(parts[1], out var max))
    {
      range = NormalizeTargetRange(min, max);
      return true;
    }

    return false;
  }

  /// <summary>
  /// 解析“路线名称输入控件”类型；若 API 不支持文本输入则返回占位类型并标记不可用。
  /// </summary>
  private static object ResolveRouteNameInputType(out bool supported)
  {
    supported = false;
    foreach (var candidate in new[] { "TextInput", "Input", "String", "Text" })
    {
      if (!TryEnumVal(candidate, out var value)) continue;

      supported = true;
      return value;
    }

    return EnumVal("Slider");
  }

  /// <summary>
  /// 解析范围编辑控件：优先文本输入，缺失时回退到双滑块。
  /// </summary>
  private static object ResolveRangeEditorType(out bool useTextInput)
  {
    useTextInput = false;
    foreach (var candidate in new[] { "TextInput", "Input", "String", "Text" })
    {
      if (!TryEnumVal(candidate, out var value)) continue;

      useTextInput = true;
      return value;
    }

    return EnumVal("Slider");
  }

  /// <summary>
  /// 解析动作控件：优先按钮，不支持时回退 Toggle。
  /// </summary>
  private static object ResolveActionType(out bool usesToggleFallback)
  {
    if (TryEnumVal("Button", out var buttonType))
    {
      usesToggleFallback = false;
      return buttonType;
    }

    usesToggleFallback = true;
    return EnumVal("Toggle");
  }

  /// <summary>
  /// 新增一路配置，并持久化。
  /// </summary>
  private static void AddPathConfiguration(string requestedName)
  {
    var resolvedName = BuildUniquePathName(requestedName);
    var suggestedPriority = ConfigManager.PathConfigs.Count == 0
      ? 100
      : ConfigManager.PathConfigs.Max(cfg => cfg?.Priority ?? 0) + 10;
    var clampedPriority = Math.Clamp(suggestedPriority, 0, 999);
    var color = SuggestedRouteColors[ConfigManager.PathConfigs.Count % SuggestedRouteColors.Length];

    ConfigManager.PathConfigs.Add(new PathConfig
    {
      Name = resolvedName,
      Color = color,
      Priority = clampedPriority,
      Enabled = true,
      TargetCounts = new Dictionary<MapPointType, TargetRange>()
    });

    _pendingNewRouteName = resolvedName;
    PersistConfigAndRefresh($"PathAdded:{resolvedName}");
    RouteSuggestMod.Log("Path config added. Reopen ModConfig panel to see the new entry immediately.");
  }

  /// <summary>
  /// 删除指定路线配置，至少保留 1 条。
  /// </summary>
  private static void RemovePathConfiguration(int index)
  {
    if (index < 0 || index >= ConfigManager.PathConfigs.Count) return;

    if (ConfigManager.PathConfigs.Count <= 1)
    {
      RouteSuggestMod.LogWarning("Cannot remove path config: at least one path config must remain.");
      return;
    }

    var removedName = ConfigManager.PathConfigs[index]?.Name ?? $"Path#{index}";
    ConfigManager.PathConfigs.RemoveAt(index);
    PersistConfigAndRefresh($"PathRemoved:{removedName}");
    RouteSuggestMod.Log("Path config removed. Reopen ModConfig panel to refresh entry layout.");
  }

  /// <summary>
  /// 生成不重复的路线名称。
  /// </summary>
  private static string BuildUniquePathName(string requestedName)
  {
    var baseName = string.IsNullOrWhiteSpace(requestedName) ? DefaultNewRouteName : requestedName.Trim();
    var existingNames = new HashSet<string>(
      ConfigManager.PathConfigs
        .Where(cfg => cfg != null && !string.IsNullOrWhiteSpace(cfg.Name))
        .Select(cfg => cfg.Name),
      StringComparer.OrdinalIgnoreCase);

    if (!existingNames.Contains(baseName)) return baseName;

    var suffix = 2;
    while (true)
    {
      var candidate = $"{baseName} ({suffix})";
      if (!existingNames.Contains(candidate)) return candidate;
      suffix++;
    }
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

  private static bool TryEnumVal(string name, out object value)
  {
    value = null;
    if (_configTypeEnum == null || string.IsNullOrWhiteSpace(name)) return false;

    try
    {
      value = Enum.Parse(_configTypeEnum, name, ignoreCase: true);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static object EnumVal(string name)
  {
    if (TryEnumVal(name, out var value)) return value;

    var fallbackName = Enum.GetNames(_configTypeEnum).FirstOrDefault();
    if (fallbackName == null)
    {
      throw new InvalidOperationException("ModConfigBridge: ConfigType enum has no values.");
    }

    RouteSuggestMod.LogWarning($"ModConfigBridge: ConfigType '{name}' not found, fallback to '{fallbackName}'.");
    return Enum.Parse(_configTypeEnum, fallbackName, ignoreCase: true);
  }
}
