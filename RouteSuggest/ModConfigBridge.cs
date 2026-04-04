using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;

namespace RouteSuggest;

internal static class ModConfigBridge
{
    private static bool _available;
    private static bool _registered;
    private static Type _apiType;
    private static Type _entryType;
    private static Type _configTypeEnum;

    internal static bool IsAvailable => _available;

    internal static void DeferredRegister()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame += OnNextFrame;
    }

    private static void OnNextFrame()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame -= OnNextFrame;
        Detect();
        if (_available) Register();
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
        catch
        {
            _available = false;
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
                ["zhs"] = "路线推荐",
            };

            var registerMethod = _apiType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Register")
                .OrderByDescending(m => m.GetParameters().Length)
                .First();

            if (registerMethod.GetParameters().Length == 4)
            {
                registerMethod.Invoke(null, new object[] { "RouteSuggest", displayNames["en"], displayNames, entries });
            }
            else
            {
                registerMethod.Invoke(null, new object[] { "RouteSuggest", displayNames["en"], entries });
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[RouteSuggest] ModConfig registration failed: {e}");
        }
    }

    internal static T GetValue<T>(string key, T fallback)
    {
        if (!_available) return fallback;
        try
        {
            var result = _apiType.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static)
                ?.MakeGenericMethod(typeof(T))
                ?.Invoke(null, new object[] { "RouteSuggest", key });
            return result != null ? (T)result : fallback;
        }
        catch { return fallback; }
    }

    internal static void SetValue(string key, object value)
    {
        if (!_available) return;
        try
        {
            _apiType.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { "RouteSuggest", key, value });
        }
        catch { }
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
                if (Enum.TryParse<HighlightType>(Convert.ToString(v), out var t))
                {
                    ConfigManager.CurrentHighlightType = t;
                    ConfigManager.SaveConfiguration();
                    RouteCalculator.InvalidateCache();
                    RouteCalculator.UpdateBestPath();
                }
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
            var pathConfig = ConfigManager.PathConfigs[i];
            var ci = i; // capture index
            var prefix = $"Path_{ci}";

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
                    ConfigManager.PathConfigs[ci].Enabled = Convert.ToBoolean(v);
                    ConfigManager.SaveConfiguration();
                    RouteCalculator.InvalidateCache();
                    RouteCalculator.UpdateBestPath();
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
                    var c = ConfigManager.ParseColor(Convert.ToString(v));
                    ConfigManager.PathConfigs[ci].Color = c;
                    ConfigManager.SaveConfiguration();
                    RouteCalculator.InvalidateCache();
                    RouteCalculator.UpdateBestPath();
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
                    ConfigManager.PathConfigs[ci].Priority = (int)Math.Round(Convert.ToSingle(v));
                    ConfigManager.SaveConfiguration();
                    RouteCalculator.InvalidateCache();
                    RouteCalculator.UpdateBestPath();
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
                        int minVal = (int)Math.Round(Convert.ToSingle(v));
                        if (ConfigManager.PathConfigs[ci].TargetCounts == null)
                            ConfigManager.PathConfigs[ci].TargetCounts = new Dictionary<MapPointType, TargetRange>();

                        if (!ConfigManager.PathConfigs[ci].TargetCounts.TryGetValue(pt, out var old))
                            old = new TargetRange(minVal, 15);

                        ConfigManager.PathConfigs[ci].TargetCounts[pt] = new TargetRange(minVal, old.Max);

                        ConfigManager.SaveConfiguration();
                        RouteCalculator.InvalidateCache();
                        RouteCalculator.UpdateBestPath();
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
                        int maxVal = (int)Math.Round(Convert.ToSingle(v));
                        if (ConfigManager.PathConfigs[ci].TargetCounts == null)
                            ConfigManager.PathConfigs[ci].TargetCounts = new Dictionary<MapPointType, TargetRange>();

                        if (!ConfigManager.PathConfigs[ci].TargetCounts.TryGetValue(pt, out var old))
                            old = new TargetRange(0, maxVal);

                        ConfigManager.PathConfigs[ci].TargetCounts[pt] = new TargetRange(old.Min, maxVal);

                        ConfigManager.SaveConfiguration();
                        RouteCalculator.InvalidateCache();
                        RouteCalculator.UpdateBestPath();
                    }));
                }));
            }
        }

        var result = Array.CreateInstance(_entryType, list.Count);
        for (int i = 0; i < list.Count; i++)
            result.SetValue(list[i], i);
        return result;
    }

    private static object Entry(Action<object> configure)
    {
        var inst = Activator.CreateInstance(_entryType);
        configure(inst);
        return inst;
    }

    private static void Set(object obj, string name, object value)
        => obj.GetType().GetProperty(name)?.SetValue(obj, value);

    private static Dictionary<string, string> L(string en, string zhs)
        => new() { ["en"] = en, ["zhs"] = zhs };

    private static object EnumVal(string name)
        => Enum.Parse(_configTypeEnum, name);
}
