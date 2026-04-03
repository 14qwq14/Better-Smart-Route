using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RouteSuggest;

public static class ModConfigAdapter
{
    public static void DeferredRegisterModConfig()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        Action callback = null;
        callback = () =>
        {
            tree.ProcessFrame -= callback;
            RegisterModConfigViaReflection();
        };
        tree.ProcessFrame += callback;
    }

    private static void RegisterModConfigViaReflection()
    {
        try
        {
            var apiType = Type.GetType("ModConfig.ModConfigApi, ModConfig");
            var entryType = Type.GetType("ModConfig.ConfigEntry, ModConfig");
            var configType = Type.GetType("ModConfig.ConfigType, ModConfig");
            var managerType = Type.GetType("ModConfig.ModConfigManager, ModConfig");

            if (apiType == null || entryType == null || configType == null || managerType == null)
            {
                RouteSuggestMod.Log("ModConfig not found, skipping GUI registration");
                return;
            }

            var method = apiType.GetMethod("SetValue") ?? throw new Exception("SetValue method not found");
            
            method.Invoke(null, new object[] { "RouteSuggest", "__reset_default", false });
            method.Invoke(null, new object[] { "RouteSuggest", "highlight_type", ConfigManager.CurrentHighlightType.ToString() });
            method.Invoke(null, new object[] { "RouteSuggest", "__add_path", false });

            for (int i = 0; i < ConfigManager.PathConfigs.Count; i++)
            {
                var config = ConfigManager.PathConfigs[i];
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_remove", false });
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_enabled", config.Enabled });
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_name", config.Name });
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_color", $"#{config.Color.ToHtml(false)}" });
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_priority", (float)config.Priority });

                var roomTypes = new[] { MegaCrit.Sts2.Core.Map.MapPointType.RestSite, MegaCrit.Sts2.Core.Map.MapPointType.Treasure, MegaCrit.Sts2.Core.Map.MapPointType.Shop,
                    MegaCrit.Sts2.Core.Map.MapPointType.Monster, MegaCrit.Sts2.Core.Map.MapPointType.Elite, MegaCrit.Sts2.Core.Map.MapPointType.Unknown };
                
                foreach (var roomType in roomTypes)
                {
                    ConfigManager.PathConfigs[i].TargetCounts.TryGetValue(roomType, out var weight);
                    method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_target_{roomType}", (float)weight });
                }
            }

            var save = managerType.GetMethod("SaveValues", BindingFlags.NonPublic | BindingFlags.Static);
            save?.Invoke(null, new object[] { "RouteSuggest" });

            var entries = new List<object>();

            object MakeEntry(string key, string label, object type, object defaultValue = null, float min = 0, float max = 100, float step = 1,
                string format = "F0", string[] options = null, Dictionary<string, string> labels = null, Dictionary<string, string> descriptions = null,
                Action<object> onChanged = null)
            {
                var entry = Activator.CreateInstance(entryType);
                entryType.GetProperty("Key")?.SetValue(entry, key);
                entryType.GetProperty("Label")?.SetValue(entry, label);
                entryType.GetProperty("Type")?.SetValue(entry, type);
                if (defaultValue != null) entryType.GetProperty("DefaultValue")?.SetValue(entry, defaultValue);
                entryType.GetProperty("Min")?.SetValue(entry, min);
                entryType.GetProperty("Max")?.SetValue(entry, max);
                entryType.GetProperty("Step")?.SetValue(entry, step);
                entryType.GetProperty("Format")?.SetValue(entry, format);
                if (options != null) entryType.GetProperty("Options")?.SetValue(entry, options);
                if (labels != null) entryType.GetProperty("Labels")?.SetValue(entry, labels);
                if (descriptions != null) entryType.GetProperty("Descriptions")?.SetValue(entry, descriptions);
                if (onChanged != null) entryType.GetProperty("OnChanged")?.SetValue(entry, onChanged);
                return entry;
            }

            object GetConfigType(string name) => Enum.Parse(configType, name);

            entries.Add(MakeEntry("", "General", GetConfigType("Header"), labels: new() { { "zhs", "通用" } }));

            entries.Add(MakeEntry("__reset_default", "Reset to defaults", GetConfigType("Toggle"), defaultValue: true,
                labels: new() { { "zhs", "重置为默认" } }, descriptions: new() { { "en", "Toggle to reset all configurations to default" }, { "zhs", "点击以重置所有配置为默认" } },
                onChanged: (value) => { if ((bool)value) { ConfigManager.ResetToDefault(); SaveAndUpdatePath(); DeferredRegisterModConfig(); } }));

            entries.Add(MakeEntry("highlight_type", "Highlight Type", GetConfigType("Dropdown"), defaultValue: ConfigManager.CurrentHighlightType.ToString(), options: new[] { "One", "All" },
                labels: new() { { "zhs", "高亮类型" } }, descriptions: new() { { "en", "Pick one path from optimal paths (One) or highlight all optimal paths (All)" }, { "zhs", "选择一条最优路径 (One) 或高亮所有最优路径 (All)" } },
                onChanged: (value) => { if (Enum.TryParse<HighlightType>((string)value, out var newType)) { ConfigManager.CurrentHighlightType = newType; SaveAndUpdatePath(); } }));

            entries.Add(MakeEntry("", "", GetConfigType("Separator")));
            entries.Add(MakeEntry("", "Path Management", GetConfigType("Header"), labels: new() { { "zhs", "路径管理" } }));

            entries.Add(MakeEntry("__add_path", "Add New Path", GetConfigType("Toggle"), defaultValue: false,
                labels: new() { { "zhs", "添加新路径" } }, descriptions: new() { { "en", "Toggle to add a new path configuration" }, { "zhs", "点击以添加新的路径配置" } },
                onChanged: (value) => { if ((bool)value) { ConfigManager.PathConfigs.Add(new PathConfig { Name = $"Path{ConfigManager.PathConfigs.Count + 1}", Color = new Color(1f, 1f, 1f, 1f), Priority = 50, TargetCounts = new() }); SaveAndUpdatePath(); DeferredRegisterModConfig(); } }));

            entries.Add(MakeEntry("", "", GetConfigType("Separator")));

            for (int i = 0; i < ConfigManager.PathConfigs.Count; i++)
            {
                var config = ConfigManager.PathConfigs[i];
                var pathIndex = i;

                entries.Add(MakeEntry("", $"Path {i + 1}", GetConfigType("Header"), labels: new() { { "zhs", $"路径 {i + 1}" } }));

                entries.Add(MakeEntry($"path_{i}_remove", "Remove Path", GetConfigType("Toggle"), defaultValue: false, labels: new() { { "zhs", "删除路径" } },
                    descriptions: new() { { "en", "Toggle to remove this path configuration" }, { "zhs", "点击以删除此路径配置" } },
                    onChanged: (value) => { if ((bool)value) { ConfigManager.PathConfigs.RemoveAt(pathIndex); SaveAndUpdatePath(); DeferredRegisterModConfig(); } }));

                entries.Add(MakeEntry($"path_{i}_enabled", "Enabled", GetConfigType("Toggle"), defaultValue: config.Enabled, labels: new() { { "zhs", "是否启用" } },
                    descriptions: new() { { "en", "Enable or disable this path" }, { "zhs", "启用或禁用此路径" } }, onChanged: (value) => { config.Enabled = (bool)value; SaveAndUpdatePath(); }));

                entries.Add(MakeEntry($"path_{i}_name", "Name", GetConfigType("TextInput"), defaultValue: config.Name, labels: new() { { "zhs", "名称" } },
                    descriptions: new() { { "en", "The name of this path" }, { "zhs", "此路径的名称" } }, onChanged: (value) => { config.Name = (string)value; SaveAndUpdatePath(); }));

                entries.Add(MakeEntry($"path_{i}_color", "Color (hex, e.g., #FFD700)", GetConfigType("TextInput"), defaultValue: $"#{config.Color.ToHtml(false)}", labels: new() { { "zhs", "颜色" } },
                    descriptions: new() { { "en", "Hex color code for path highlighting" }, { "zhs", "用于路径高亮的十六进制颜色代码" } }, onChanged: (value) => { config.Color = Godot.Color.FromHtml((string)value); SaveAndUpdatePath(); }));

                entries.Add(MakeEntry($"path_{i}_priority", "Priority (higher = on top)", GetConfigType("Slider"), defaultValue: (float)config.Priority, min: 0, max: 200, step: 10, format: "F0",
                    labels: new() { { "zhs", "优先级" } }, descriptions: new() { { "en", "Higher priority paths are rendered on top of lower priority paths" }, { "zhs", "优先级高的路径会覆盖优先级低的" } },
                    onChanged: (value) => { config.Priority = (int)(float)value; SaveAndUpdatePath(); }));

                entries.Add(MakeEntry("", "Scoring Weights", GetConfigType("Header"), labels: new() { { "zhs", "目标次数" } }));

                var roomTypes = new[] { MegaCrit.Sts2.Core.Map.MapPointType.RestSite, MegaCrit.Sts2.Core.Map.MapPointType.Treasure, MegaCrit.Sts2.Core.Map.MapPointType.Shop,
                    MegaCrit.Sts2.Core.Map.MapPointType.Monster, MegaCrit.Sts2.Core.Map.MapPointType.Elite, MegaCrit.Sts2.Core.Map.MapPointType.Unknown };

                foreach (var roomType in roomTypes)
                {
                    config.TargetCounts.TryGetValue(roomType, out var weight);
                    var capturedRoomType = roomType;
                    var roomLabels = new Dictionary<string, string> { { "en", roomType.ToString() } };
                    var roomDescriptions = new Dictionary<string, string> { { "en", $"Target count for {roomType} rooms" } };

                    switch (roomType)
                    {
                        case MegaCrit.Sts2.Core.Map.MapPointType.RestSite: roomLabels["zhs"] = "休息处"; roomDescriptions["zhs"] = "休息处目标出现次数"; break;
                        case MegaCrit.Sts2.Core.Map.MapPointType.Treasure: roomLabels["zhs"] = "宝箱"; roomDescriptions["zhs"] = "宝箱目标出现次数"; break;
                        case MegaCrit.Sts2.Core.Map.MapPointType.Shop: roomLabels["zhs"] = "商店"; roomDescriptions["zhs"] = "商店目标出现次数"; break;
                        case MegaCrit.Sts2.Core.Map.MapPointType.Monster: roomLabels["zhs"] = "普通敌人"; roomDescriptions["zhs"] = "普通敌人目标出现次数"; break;
                        case MegaCrit.Sts2.Core.Map.MapPointType.Elite: roomLabels["zhs"] = "精英敌人"; roomDescriptions["zhs"] = "精英敌人目标出现次数"; break;
                        case MegaCrit.Sts2.Core.Map.MapPointType.Unknown: roomLabels["zhs"] = "未知"; roomDescriptions["zhs"] = "疑问号目标出现次数"; break;
                    }

                    entries.Add(MakeEntry($"path_{i}_target_{roomType}", roomType.ToString(), GetConfigType("Slider"), defaultValue: (float)weight, min: 0, max: 15, step: 1, format: "F0",
                        labels: roomLabels, descriptions: roomDescriptions, onChanged: (value) => { config.TargetCounts[capturedRoomType] = (int)(float)value; SaveAndUpdatePath(); }));
                }

                entries.Add(MakeEntry("", "", GetConfigType("Separator")));
            }

            var entriesArray = Array.CreateInstance(entryType, entries.Count);
            for (int i = 0; i < entries.Count; i++) entriesArray.SetValue(entries[i], i);

            var registerMethod = apiType.GetMethod("Register", new[] { typeof(string), typeof(string), entryType.MakeArrayType() });
            registerMethod?.Invoke(null, new object[] { "RouteSuggest", "RouteSuggest", entriesArray });
        }
        catch (Exception ex)
        {
            RouteSuggestMod.Log($"Failed to register with ModConfig: {ex.Message}");
        }
    }

    private static void SaveAndUpdatePath()
    {
        ConfigManager.SaveConfiguration();
        RouteCalculator.InvalidateCache();
        RouteCalculator.UpdateBestPath();
        MapHighlighter.RequestHighlightOnMapOpen();
    }
}
