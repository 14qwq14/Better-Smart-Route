using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace RouteSuggest;

/// <summary>
/// Configuration for a path type including scoring weights, color, and priority.
/// </summary>
public class PathConfig
{
    /// <summary>
    /// Name for this path configuration (e.g., "Safe", "Aggressive").
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Color used to highlight this path type on the map.
    /// </summary>
    public Color Color { get; set; }

    /// <summary>
    /// Higher priority paths are rendered on top of lower priority paths when they overlap.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether this path is enabled. Disabled paths are not calculated or shown.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Target counts for each room type. Positive values make the room more desirable,
    /// negative values make it less desirable. Only rooms with defined weights contribute to the score.
    /// </summary>
    public Dictionary<MapPointType, int> TargetCounts { get; set; } = new Dictionary<MapPointType, int>();

    /// <summary>
    /// Calculates the total score for a given path using this configuration's scoring weights.
    /// </summary>
    /// <param name="path">List of map points representing the path to score.</param>
    /// <returns>The total score, or 0 if path is null or empty.</returns>
    public int CalculateScore(List<MapPoint> path)
    {
        if (path == null) return -9999;

        // 统一计算路径中各类节点出现的次数
        var counts = new Dictionary<MapPointType, int>();
        foreach (var point in path)
        {
            if (!counts.ContainsKey(point.PointType)) counts[point.PointType] = 0;
            counts[point.PointType]++;
        }

        // 以误差平方和为代价计算负分。分数越高（最大为0）代表越符合配置参数期待�?
        int score = 0;
        foreach (var kvp in TargetCounts)
        {
            counts.TryGetValue(kvp.Key, out int actual);
            int diff = Math.Abs(kvp.Value - actual);
            score -= (diff * diff); // Negative penalty (差值平方作为惩罚，有效放大了单类节点数量严重不符的情况)
        }
        return score;
    }
}

/// <summary>
/// Enum for controlling how many paths to highlight: pick one from optimal paths, or highlight all optimal paths.
/// </summary>
public enum HighlightType
{
    /// <summary>Highlight one path from among optimal paths.</summary>
    One,
    /// <summary>Highlight all paths with the best score.</summary>
    All
}

/// <summary>
/// Main mod class for RouteSuggest. Provides optimal path suggestions on the map with multiple strategies.
/// </summary>
[ModInitializer("ModLoaded")]
public static class RouteSuggest
{
    /// <summary>
    /// Current run state, set when a run starts.
    /// </summary>
    public static RunState RunState { get; private set; }

    /// <summary>
    /// Dictionary of calculated paths for each configured path type.
    /// Key is the path config name, value is a list of calculated paths (one or multiple depending on HighlightType).
    /// </summary>
    public static Dictionary<string, List<List<MapPoint>>> CalculatedPaths { get; private set; } = new Dictionary<string, List<List<MapPoint>>>();

    /// <summary>
    /// List of configured path types. Can be modified at runtime or loaded from config file.
    /// Defaults contain Safe (gold) and Aggressive (red) path types.
    /// </summary>
    public static List<PathConfig> PathConfigs { get; private set; }

    /// <summary>
    /// Controls whether to highlight one optimal path (One) or all optimal paths (All).
    /// </summary>
    public static HighlightType CurrentHighlightType { get; set; } = HighlightType.One;

    /// <summary>
    /// Default path configurations used as fallback and for reset functionality.
    /// </summary>
    private static readonly List<PathConfig> DefaultPathConfigs = new List<PathConfig>
    {
        new PathConfig
        {
            Name = "Safe (Green) / 安全",
            Color = new Color(0f, 1f, 0f, 1f), // Green
            Priority = 100,
            TargetCounts = new Dictionary<MapPointType, int>
            {
                { MapPointType.Elite, 0 }
            }
        },
        new PathConfig
        {
            Name = "Aggressive (Red) / 激�?,
            Color = new Color(1f, 0f, 0f, 1f), // Red
            Priority = 50,
            TargetCounts = new Dictionary<MapPointType, int>
            {
                { MapPointType.Elite, 15 }
            }
        },
        new PathConfig
        {
            Name = "Question marks (Yellow) / 问号",
            Color = new Color(1f, 1f, 0f, 1f), // Yellow
            Priority = 75,
            TargetCounts = new Dictionary<MapPointType, int>
            {
                { MapPointType.Unknown, 15 }
            }
        },
        new PathConfig
        {
            Name = "Boss Rush (Purple) / 首领速�?,
            Color = new Color(0.5f, 0f, 0.5f, 1f), // Purple
            Priority = 120,
            Enabled = false,
            TargetCounts = new Dictionary<MapPointType, int>
            {
                { MapPointType.Elite, 15 },
                { MapPointType.RestSite, 0 },
                { MapPointType.Monster, 0 }
            }
        },
        new PathConfig
        {
            Name = "Max Rewards (Gold) / 最大收�?,
            Color = new Color(1f, 0.84f, 0f, 1f), // Gold
            Priority = 90,
            Enabled = false,
            TargetCounts = new Dictionary<MapPointType, int>
            {
                { MapPointType.Elite, 15 },
                { MapPointType.Treasure, 10 },
                { MapPointType.Shop, 5 }
            }
        }
    };

    /// <summary>
    /// Stores original colors and scales for each highlighted TextureRect to restore later.
    /// </summary>
    private static readonly Dictionary<TextureRect, (Color color, Vector2 scale)> OriginalTickProperties = new Dictionary<TextureRect, (Color, Vector2)>();

    /// <summary>
    /// Cached reflection field for accessing the map screen's internal paths dictionary.
    /// </summary>
    private static FieldInfo _pathsField;

    /// <summary>
    /// Whether reflection has been successfully initialized.
    /// </summary>
    private static bool _reflectionInitialized = false;

    /// <summary>
    /// Flag indicating if a highlight request is pending for when the map screen opens.
    /// </summary>
    private static bool _pendingHighlight = false;

    /// <summary>
    /// Stores the path to the config file that was used for loading.
    /// Used for saving configuration back to the same location.
    /// </summary>
    private static string _configFilePath = null;

    /// <summary>
    /// Logs a message with a timestamp prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    private static void LogWithTimestamp(string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Log.Warn($"[{timestamp}] RouteSuggest: {message}");
    }

    /// <summary>
    /// Called when the mod is loaded. Initializes configuration, subscribes to game events,
    /// and sets up reflection for map highlighting.
    /// </summary>
    public static void ModLoaded()
    {
        LogWithTimestamp("Mod loaded");

        // Initialize PathConfigs from defaults
        PathConfigs = new List<PathConfig>();
        ResetToDefault();

        // Load configuration from file if available
        LoadConfig();

        // Pretty print the current path configurations
        PrintPathConfigs();

        // Subscribe to game events
        var manager = RunManager.Instance;
        manager.RunStarted += OnRunStarted;
        manager.ActEntered += OnActEntered;
        manager.RoomEntered += OnRoomEntered;
        manager.RoomExited += OnRoomExited;

        // Initialize reflection for path highlighting
        InitializeReflection();

        // Register with ModConfig for GUI settings (via reflection)
        DeferredRegisterModConfig();
    }

    /// <summary>
    /// Deferred registration with ModConfig to ensure all mods are loaded first.
    /// Uses reflection for zero-dependency integration.
    /// </summary>
    static void DeferredRegisterModConfig()
    {
        // Wait one frame for all mods to load
        var tree = (SceneTree)Engine.GetMainLoop();
        Action callback = null;
        callback = () =>
        {
            tree.ProcessFrame -= callback;
            RegisterModConfigViaReflection();
        };
        tree.ProcessFrame += callback;
    }

    /// <summary>
    /// Registers path configurations with ModConfig using reflection.
    /// This allows ModConfig integration without a hard DLL dependency.
    /// </summary>
    static void RegisterModConfigViaReflection()
    {
        try
        {
            // Check if ModConfig is available
            var apiType = Type.GetType("ModConfig.ModConfigApi, ModConfig");
            var entryType = Type.GetType("ModConfig.ConfigEntry, ModConfig");
            var configType = Type.GetType("ModConfig.ConfigType, ModConfig");
            var managerType = Type.GetType("ModConfig.ModConfigManager, ModConfig");

            if (apiType == null || entryType == null || configType == null || managerType == null)
            {
                LogWithTimestamp("ModConfig not found, skipping GUI registration");
                return;
            }

            // Synchronize current config to ModConfig
            var method = apiType.GetMethod("SetValue")!;
            method.Invoke(null, new object[] { "RouteSuggest", "__reset_default", false });
            method.Invoke(null, new object[] { "RouteSuggest", "highlight_type", CurrentHighlightType.ToString() });
            method.Invoke(null, new object[] { "RouteSuggest", "__add_path", false });
            for (int i = 0; i < PathConfigs.Count; i++)
            {
                var config = PathConfigs[i];
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_remove", false });
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_enabled", config.Enabled });
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_name", config.Name });
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_color", $"#{config.Color.ToHtml(false)}" });
                method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_priority", (float)config.Priority });

                var roomTypes = new[] { MapPointType.RestSite, MapPointType.Treasure, MapPointType.Shop,
                    MapPointType.Monster, MapPointType.Elite, MapPointType.Unknown };
                foreach (var roomType in roomTypes)
                {
                    var weight = 0;
                    PathConfigs[i].TargetCounts.TryGetValue(roomType, out weight);
                    method.Invoke(null, new object[] { "RouteSuggest", $"path_{i}_target_{roomType}", (float)weight });
                }
            }

            // Ensure our configurations are saved immediately
            var save = managerType.GetMethod("SaveValues", BindingFlags.NonPublic | BindingFlags.Static)!;
            save.Invoke(null, new object[] { "RouteSuggest" });

            var entries = new List<object>();

            // Helper to create ConfigEntry via reflection
            object MakeEntry(string key, string label, object type,
                object defaultValue = null, float min = 0, float max = 100, float step = 1,
                string format = "F0", string[] options = null,
                Dictionary<string, string> labels = null,
                Dictionary<string, string> descriptions = null,
                Action<object> onChanged = null)
            {
                var entry = Activator.CreateInstance(entryType);
                entryType.GetProperty("Key")?.SetValue(entry, key);
                entryType.GetProperty("Label")?.SetValue(entry, label);
                entryType.GetProperty("Type")?.SetValue(entry, type);
                if (defaultValue != null)
                    entryType.GetProperty("DefaultValue")?.SetValue(entry, defaultValue);
                entryType.GetProperty("Min")?.SetValue(entry, min);
                entryType.GetProperty("Max")?.SetValue(entry, max);
                entryType.GetProperty("Step")?.SetValue(entry, step);
                entryType.GetProperty("Format")?.SetValue(entry, format);
                if (options != null)
                    entryType.GetProperty("Options")?.SetValue(entry, options);
                if (labels != null)
                    entryType.GetProperty("Labels")?.SetValue(entry, labels);
                if (descriptions != null)
                    entryType.GetProperty("Descriptions")?.SetValue(entry, descriptions);
                if (onChanged != null)
                    entryType.GetProperty("OnChanged")?.SetValue(entry, onChanged);
                return entry;
            }

            // Helper to get ConfigType enum value
            object GetConfigType(string name) => Enum.Parse(configType, name);

            entries.Add(MakeEntry("", "General", GetConfigType("Header"),
                labels: new() { { "zhs", "通用" } }));

            // Reset to defaults logic
            entries.Add(MakeEntry("__reset_default", "Reset to defaults",
                GetConfigType("Toggle"),
                defaultValue: true,
                labels: new() { { "zhs", "重置为默认�? } },
                descriptions: new() { { "en", "Toggle to reset all configurations to default" }, { "zhs", "点击以重置所有配置为默认�? } },
                onChanged: (value) =>
                {
                    if ((bool)value)
                    {
                        ResetToDefault();
                        SaveAndUpdatePath();

                        // Re-register to refresh UI
                        DeferredRegisterModConfig();
                    }
                }));

            // Highlight type selector
            entries.Add(MakeEntry("highlight_type", "Highlight Type",
                GetConfigType("Dropdown"),
                defaultValue: HighlightType.One.ToString(),
                options: new[] { "One", "All" },
                labels: new() { { "zhs", "高亮类型" } },
                descriptions: new() { { "en", "Pick one path from optimal paths (One) or highlight all optimal paths (All)" }, { "zhs", "从最优路径中选择一�?(One) 或高亮所有最优路�?(All)" } },
                onChanged: (value) =>
                {
                    if (Enum.TryParse<HighlightType>((string)value, out var newType))
                    {
                        CurrentHighlightType = newType;
                        SaveAndUpdatePath();
                    }
                }));


            entries.Add(MakeEntry("", "", GetConfigType("Separator")));

            // Path Management section at the top
            entries.Add(MakeEntry("", "Path Management", GetConfigType("Header"),
                labels: new() { { "zhs", "路径管理" } }));

            // Toggle to add new path
            entries.Add(MakeEntry("__add_path", "Add New Path",
                GetConfigType("Toggle"),
                defaultValue: false,
                labels: new() { { "zhs", "添加新路�? } },
                descriptions: new() { { "en", "Toggle to add a new path configuration" }, { "zhs", "点击以添加新的路径配�? } },
                onChanged: (value) =>
                {
                    if ((bool)value)
                    {
                        var newConfig = new PathConfig
                        {
                            Name = $"Path{PathConfigs.Count + 1}",
                            Color = new Color(1f, 1f, 1f, 1f),
                            Priority = 50,
                            TargetCounts = new Dictionary<MapPointType, int>()
                        };
                        PathConfigs.Add(newConfig);
                        SaveAndUpdatePath();

                        // Re-register to refresh UI
                        DeferredRegisterModConfig();
                    }
                }));

            entries.Add(MakeEntry("", "", GetConfigType("Separator")));

            // Add configuration for each path
            for (int i = 0; i < PathConfigs.Count; i++)
            {
                var config = PathConfigs[i];
                var pathIndex = i;

                // Path header with remove toggle
                entries.Add(MakeEntry("", $"Path {i + 1}",
                    GetConfigType("Header"),
                    labels: new() { { "zhs", $"路径 {i + 1}" } }
                ));

                // Remove this path toggle
                entries.Add(MakeEntry($"path_{i}_remove", "Remove Path",
                    GetConfigType("Toggle"),
                    defaultValue: false,
                    labels: new() { { "zhs", "删除路径" } },
                    descriptions: new() { { "en", "Toggle to remove this path configuration" }, { "zhs", "点击以删除此路径配置" } },
                    onChanged: (value) =>
                    {
                        if ((bool)value)
                        {
                            PathConfigs.RemoveAt(pathIndex);
                            SaveAndUpdatePath();

                            // Re-register to refresh UI
                            DeferredRegisterModConfig();
                        }
                    }));

                // Enable/disable this path
                entries.Add(MakeEntry($"path_{i}_enabled", "Enabled",
                    GetConfigType("Toggle"),
                    defaultValue: true,
                    labels: new() { { "zhs", "是否启用" } },
                    descriptions: new() { { "en", "Enable or disable this path" }, { "zhs", "启用或禁用此路径" } },
                    onChanged: (value) =>
                    {
                        config.Enabled = (bool)value;
                        SaveAndUpdatePath();
                    }));


                // Name
                var defaultName = $"Path{i + 1}";
                if (i < DefaultPathConfigs.Count)
                {
                    defaultName = DefaultPathConfigs[i].Name;
                }
                entries.Add(MakeEntry($"path_{i}_name", "Name", GetConfigType("TextInput"),
                    defaultValue: defaultName,
                    labels: new() { { "zhs", "名称" } },
                    descriptions: new() { { "en", "The name of this path" }, { "zhs", "此路径的名称" } },
                    onChanged: (value) =>
                    {
                        config.Name = (string)value;
                        SaveAndUpdatePath();
                    }));

                // Color (hex input)
                var defaultColor = new Color(1f, 1f, 1f, 1f);
                if (i < DefaultPathConfigs.Count)
                {
                    defaultColor = DefaultPathConfigs[i].Color;
                }
                entries.Add(MakeEntry($"path_{i}_color", "Color (hex, e.g., #FFD700)",
                    GetConfigType("TextInput"),
                    defaultValue: $"#{defaultColor.ToHtml(false)}",
                    labels: new() { { "zhs", "颜色 (十六进制, �?#FFD700)" } },
                    descriptions: new() { { "en", "Hex color code for path highlighting" }, { "zhs", "用于路径高亮的十六进制颜色代�? } },
                    onChanged: (value) =>
                    {
                        config.Color = ParseColor((string)value);
                        SaveAndUpdatePath();
                    }));

                // Priority
                var defaultPriority = 50;
                if (i < DefaultPathConfigs.Count)
                {
                    defaultPriority = DefaultPathConfigs[i].Priority;
                }
                entries.Add(MakeEntry($"path_{i}_priority", "Priority (higher = on top)",
                    GetConfigType("Slider"),
                    defaultValue: (float)defaultPriority,
                    min: 0, max: 200, step: 10, format: "F0",
                    labels: new() { { "zhs", "优先�?(越高越靠�?" } },
                    descriptions: new() { { "en", "Higher priority paths are rendered on top of lower priority paths" }, { "zhs", "优先级高的路径会覆盖优先级低的路�? } },
                    onChanged: (value) =>
                    {
                        config.Priority = (int)(float)value;
                        SaveAndUpdatePath();
                    }));

                // Target counts section header
                entries.Add(MakeEntry("", "Scoring Weights", GetConfigType("Header"),
                    labels: new() { { "zhs", "目标次数" } }));

                // Add weights for each room type
                var roomTypes = new[] { MapPointType.RestSite, MapPointType.Treasure, MapPointType.Shop,
                    MapPointType.Monster, MapPointType.Elite, MapPointType.Unknown };
                foreach (var roomType in roomTypes)
                {
                    var weight = 0;
                    if (i < DefaultPathConfigs.Count)
                    {
                        DefaultPathConfigs[i].TargetCounts.TryGetValue(roomType, out weight);
                    }

                    var capturedRoomType = roomType;
                    var roomLabels = new Dictionary<string, string>
                    {
                        { "en", roomType.ToString() }
                    };
                    var roomDescriptions = new Dictionary<string, string>
                    {
                        { "en", $"Target count for {roomType} rooms" }
                    };

                    // Add Chinese translations for room types
                    switch (roomType)
                    {
                        case MapPointType.RestSite:
                            roomLabels["zhs"] = "休息�?;
                            roomDescriptions["zhs"] = "休息处房间的目标次数";
                            break;
                        case MapPointType.Treasure:
                            roomLabels["zhs"] = "宝箱";
                            roomDescriptions["zhs"] = "宝箱房间的目标次�?;
                            break;
                        case MapPointType.Shop:
                            roomLabels["zhs"] = "商店";
                            roomDescriptions["zhs"] = "商店房间的目标次�?;
                            break;
                        case MapPointType.Monster:
                            roomLabels["zhs"] = "普通敌�?;
                            roomDescriptions["zhs"] = "普通敌人房间的目标次数";
                            break;
                        case MapPointType.Elite:
                            roomLabels["zhs"] = "精英敌人";
                            roomDescriptions["zhs"] = "精英敌人房间的目标次�?;
                            break;
                        case MapPointType.Unknown:
                            roomLabels["zhs"] = "未知";
                            roomDescriptions["zhs"] = "未知房间的目标次�?;
                            break;
                        case MapPointType.Boss:
                            roomLabels["zhs"] = "Boss";
                            roomDescriptions["zhs"] = "Boss 房间的目标次�?;
                            break;
                    }

                    entries.Add(MakeEntry($"path_{i}_target_{roomType}", roomType.ToString(),
                        GetConfigType("Slider"),
                        defaultValue: (float)weight,
                        min: 0, max: 15, step: 1, format: "F0",
                        labels: roomLabels,
                        descriptions: roomDescriptions,
                        onChanged: (value) =>
                        {
                            config.TargetCounts[capturedRoomType] = (int)(float)value;
                            SaveAndUpdatePath();
                        }));
                }

                entries.Add(MakeEntry("", "", GetConfigType("Separator")));
            }

            // convert to entryType[]
            var entriesArray = Array.CreateInstance(entryType, entries.Count());
            for (int i = 0; i < entries.Count(); i++)
            {
                entriesArray.SetValue(entries[i], i);
            }

            // Register with ModConfig via reflection
            var registerMethod = apiType.GetMethod("Register",
                new[] { typeof(string), typeof(string), entryType.MakeArrayType() });
            registerMethod!.Invoke(null, new object[] { "RouteSuggest", "RouteSuggest", entriesArray });

            LogWithTimestamp($"Registered {entries.Count()} entries with ModConfig (via reflection)");
        }
        catch (Exception ex)
        {
            LogWithTimestamp($"Failed to register with ModConfig: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines the config file path to use.
    /// Priority: 1) mods/RouteSuggestConfig.json if exists, 2) same directory as RouteSuggest.dll (recursive search), 3) mods/RouteSuggestConfig.json as fallback
    /// </summary>
    /// <returns>The determined config file path.</returns>
    static string GetConfigFilePath()
    {
        string executablePath = OS.GetExecutablePath();
        string directoryName = Path.GetDirectoryName(executablePath);
        string modsPath = Path.Combine(directoryName, "mods");
        string modsConfigPath = Path.Combine(modsPath, "RouteSuggestConfig.json");

        LogWithTimestamp($"Determining config file path. Mods directory: {modsPath}");

        // Priority 1: Check if mods/RouteSuggestConfig.json exists
        LogWithTimestamp($"Priority 1: Checking for config at {modsConfigPath}");
        if (File.Exists(modsConfigPath))
        {
            LogWithTimestamp($"Found config at {modsConfigPath} (Priority 1)");
            return modsConfigPath;
        }
        LogWithTimestamp($"Config not found at {modsConfigPath}");

        // Priority 2: Find RouteSuggest.dll recursively and use its directory
        if (Directory.Exists(modsPath))
        {
            LogWithTimestamp("Priority 2: Searching for RouteSuggest.dll recursively in mods folder");
            try
            {
                string[] dllFiles = Directory.GetFiles(modsPath, "RouteSuggest.dll", SearchOption.AllDirectories);
                if (dllFiles.Length > 0)
                {
                    string dllPath = dllFiles[0];
                    string dllDirectory = Path.GetDirectoryName(dllPath);
                    string dllConfigPath = Path.Combine(dllDirectory, "RouteSuggestConfig.json");
                    LogWithTimestamp($"Found RouteSuggest.dll at {dllPath}");
                    LogWithTimestamp($"Using config path: {dllConfigPath} (Priority 2)");
                    return dllConfigPath;
                }
                else
                {
                    LogWithTimestamp("RouteSuggest.dll not found in mods folder or subdirectories");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                LogWithTimestamp($"Permission denied while searching for RouteSuggest.dll: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"Error searching for RouteSuggest.dll: {ex.Message}");
            }
        }
        else
        {
            LogWithTimestamp($"Mods directory does not exist: {modsPath}");
        }

        // Priority 3: Fall back to mods/RouteSuggestConfig.json
        LogWithTimestamp($"Priority 3: Falling back to default path {modsConfigPath}");
        return modsConfigPath;
    }

    /// <summary>
    /// Saves the current PathConfigs to RouteSuggestConfig.json.
    /// Called automatically when settings are changed via ModConfig.
    /// Uses the path determined during LoadConfig.
    /// </summary>
    static void SaveConfiguration()
    {
        try
        {
            // Use remembered path
            string configPath = _configFilePath;

            var configData = new ConfigFile
            {
                SchemaVersion = 3,
                HighlightType = CurrentHighlightType.ToString(),
                PathConfigs = new List<PathConfigEntry>()
            };

            foreach (var config in PathConfigs)
            {
                var entry = new PathConfigEntry
                {
                    Name = config.Name,
                    Color = $"#{config.Color.ToHtml(false)}",
                    Priority = config.Priority,
                    Enabled = config.Enabled,
                    TargetCounts = new Dictionary<string, int>()
                };

                foreach (var weight in config.TargetCounts)
                {
                    entry.TargetCounts[weight.Key.ToString()] = weight.Value;
                }

                configData.PathConfigs.Add(entry);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            string json = JsonSerializer.Serialize(configData, options);
            File.WriteAllText(configPath, json);

            LogWithTimestamp($"Configuration saved to {configPath}");
        }
        catch (Exception ex)
        {
            LogWithTimestamp($"Failed to save configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Resets to the default configuration.
    /// Creates deep copies of default configs to avoid modifying the originals.
    /// </summary>
    public static void ResetToDefault()
    {
        CurrentHighlightType = HighlightType.One;

        PathConfigs.Clear();
        foreach (var defaultConfig in DefaultPathConfigs)
        {
            var config = new PathConfig
            {
                Name = defaultConfig.Name,
                Color = defaultConfig.Color,
                Priority = defaultConfig.Priority,
                Enabled = defaultConfig.Enabled,
                TargetCounts = new Dictionary<MapPointType, int>(defaultConfig.TargetCounts)
            };
            PathConfigs.Add(config);
        }
        LogWithTimestamp("Reset to default path configurations");
    }

    /// <summary>
    /// Loads path configurations from RouteSuggestConfig.json if it exists.
    /// Falls back to default PathConfigs if file is missing or invalid.
    /// Remembers the config path for subsequent saves.
    /// </summary>
    static void LoadConfig()
    {
        try
        {
            string configPath = GetConfigFilePath();

            // Remember this path for saving
            _configFilePath = configPath;

            if (!File.Exists(configPath))
            {
                LogWithTimestamp($"Config file not found at {configPath}, generating default config.");
                try
                {
                    string dir = Path.GetDirectoryName(configPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    SaveConfiguration();
                }
                catch (Exception ex)
                {
                    LogWithTimestamp($"Failed to generate default config: {ex.Message}");
                }
                return;
            }

            string json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var configData = JsonSerializer.Deserialize<ConfigFile>(json, options);

            if (configData?.SchemaVersion != 1 && configData?.SchemaVersion != 2 && configData?.SchemaVersion != 3)
            {
                LogWithTimestamp($"Unsupported schema version {configData?.SchemaVersion}, using defaults");
                return;
            }

            // Load highlight type if present
            if (!string.IsNullOrEmpty(configData.HighlightType))
            {
                if (Enum.TryParse<HighlightType>(configData.HighlightType, out var loadedType))
                {
                    CurrentHighlightType = loadedType;
                    LogWithTimestamp($"Loaded highlight type: {loadedType}");
                }
            }

            if (configData.PathConfigs == null)
            {
                LogWithTimestamp("No path configs found in config file, using defaults");
                return;
            }

            // Clear and replace with loaded configs
            PathConfigs.Clear();
            foreach (var configEntry in configData.PathConfigs)
            {
                var config = new PathConfig
                {
                    Name = configEntry.Name,
                    Priority = configEntry.Priority,
                    Color = ParseColor(configEntry.Color),
                    Enabled = configEntry.Enabled,
                    TargetCounts = ParseTargetCounts(configEntry.TargetCounts)
                };
                PathConfigs.Add(config);
                LogWithTimestamp($"Loaded path config '{config.Name}' (enabled: {config.Enabled}) from file");
            }

            LogWithTimestamp($"Successfully loaded {PathConfigs.Count} path configs from {configPath}");
        }
        catch (JsonException jsonEx)
        {
            LogWithTimestamp($"JSON format error in config file: {jsonEx.Message}. Please check for syntax errors. Using defaults.");
        }
        catch (Exception ex)
        {
            LogWithTimestamp($"Failed to load config file: {ex.Message}, using defaults");
        }
    }

    /// <summary>
    /// Pretty prints all current path configurations for debugging.
    /// </summary>
    static void PrintPathConfigs()
    {
        LogWithTimestamp("Current Path Configurations:");
        LogWithTimestamp("==========================================");

        foreach (var config in PathConfigs)
        {
            LogWithTimestamp($"  Path: {config.Name} (Enabled: {config.Enabled})");
            LogWithTimestamp($"    Priority: {config.Priority}");
            LogWithTimestamp($"    Color: R={config.Color.R:F2}, G={config.Color.G:F2}, B={config.Color.B:F2}, A={config.Color.A:F2}");
            LogWithTimestamp($"    Scoring Weights:");

            foreach (var weight in config.TargetCounts.OrderBy(w => w.Key.ToString()))
            {
                LogWithTimestamp($"      {weight.Key}: {weight.Value:+0;-0;0}");
            }

            LogWithTimestamp("");
        }

        LogWithTimestamp($"Total path types configured: {PathConfigs.Count}");
        LogWithTimestamp("==========================================");
    }

    /// <summary>
    /// Parses a color string in hex format (#RGB or #RGBA) into a Godot Color.
    /// </summary>
    /// <param name="colorStr">Hex color string, e.g., "#FFD700" or "#FF0000FF"</param>
    /// <returns>Parsed Color, or white if parsing fails.</returns>
    static Color ParseColor(string colorStr)
    {
        if (string.IsNullOrEmpty(colorStr))
            return new Color(1f, 1f, 1f, 1f);

        // Support hex format like "#FFD700" or "#FFD700FF"
        if (colorStr.StartsWith("#"))
        {
            colorStr = colorStr.Substring(1);
            if (colorStr.Length == 6)
            {
                // RGB format
                float r = Convert.ToInt32(colorStr.Substring(0, 2), 16) / 255f;
                float g = Convert.ToInt32(colorStr.Substring(2, 2), 16) / 255f;
                float b = Convert.ToInt32(colorStr.Substring(4, 2), 16) / 255f;
                return new Color(r, g, b, 1f);
            }
            else if (colorStr.Length == 8)
            {
                // RGBA format
                float r = Convert.ToInt32(colorStr.Substring(0, 2), 16) / 255f;
                float g = Convert.ToInt32(colorStr.Substring(2, 2), 16) / 255f;
                float b = Convert.ToInt32(colorStr.Substring(4, 2), 16) / 255f;
                float a = Convert.ToInt32(colorStr.Substring(6, 2), 16) / 255f;
                return new Color(r, g, b, a);
            }
        }

        // Fallback: return white
        return new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>
    /// Parses scoring weights from the config file's string-keyed dictionary to MapPointType keys.
    /// </summary>
    /// <param name="weightsDict">Dictionary with string keys from JSON.</param>
    /// <returns>Dictionary with MapPointType keys, containing valid entries only.</returns>
    static Dictionary<MapPointType, int> ParseTargetCounts(Dictionary<string, int> weightsDict)
    {
        var result = new Dictionary<MapPointType, int>();
        if (weightsDict == null) return result;

        foreach (var kvp in weightsDict)
        {
            if (Enum.TryParse<MapPointType>(kvp.Key, out var pointType))
            {
                result[pointType] = kvp.Value;
            }
            else
            {
                LogWithTimestamp($"Unknown MapPointType '{kvp.Key}' in config");
            }
        }
        return result;
    }

    /// <summary>
    /// Represents the root structure of the RouteSuggestConfig.json file.
    /// </summary>
    private class ConfigFile
    {
        /// <summary>
        /// Schema version for config file compatibility. Supported versions: 1, 2, and 3.
        /// Version 2 adds highlight_type support.
        /// Version 3 adds enabled field to path configs.
        /// </summary>
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        /// <summary>
        /// Controls whether to highlight one optimal path (One) or all optimal paths (All).
        /// </summary>
        [JsonPropertyName("highlight_type")]
        public string HighlightType { get; set; }

        /// <summary>
        /// List of path configurations to load.
        /// </summary>
        [JsonPropertyName("path_configs")]
        public List<PathConfigEntry> PathConfigs { get; set; }
    }

    /// <summary>
    /// Represents a single path configuration entry in the JSON config file.
    /// </summary>
    private class PathConfigEntry
    {
        /// <summary>
        /// Name identifier for the path.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Hex color string (e.g., "#FFD700").
        /// </summary>
        [JsonPropertyName("color")]
        public string Color { get; set; }

        /// <summary>
        /// Priority value - higher renders on top.
        /// </summary>
        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        /// <summary>
        /// Whether this path is enabled. Disabled paths are not calculated or shown.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Target counts as string-keyed dictionary (keys are MapPointType names).
        /// </summary>
        [JsonPropertyName("scoring_weights")]
        public Dictionary<string, int> TargetCounts { get; set; }
    }

    /// <summary>
    /// Initializes reflection to access the map screen's internal _paths field.
    /// Required for highlighting path segments on the UI.
    /// </summary>
    static void InitializeReflection()
    {
        try
        {
            var mapScreenType = typeof(NMapScreen);
            _pathsField = mapScreenType.GetField("_paths", BindingFlags.NonPublic | BindingFlags.Instance);
            _reflectionInitialized = _pathsField != null;
            if (_reflectionInitialized)
            {
                LogWithTimestamp("Reflection initialized successfully");
            }
            else
            {
                LogWithTimestamp("Failed to initialize reflection - _paths field not found");
            }
        }
        catch (Exception ex)
        {
            LogWithTimestamp($"Error initializing reflection: {ex.Message}");
            _reflectionInitialized = false;
        }
    }

    /// <summary>
    /// Called when a new run starts. Stores the run state and calculates initial paths.
    /// </summary>
    static void OnRunStarted(RunState runState)
    {
        LogWithTimestamp("Run started");
        RouteSuggest.RunState = runState;
        UpdateBestPath();
    }

    /// <summary>
    /// Called when entering a new act. Recalculates paths and requests map highlighting.
    /// </summary>
    static void OnActEntered()
    {
        LogWithTimestamp("Act entered");
        UpdateBestPath();
        RequestHighlightOnMapOpen();
    }

    /// <summary>
    /// Called when entering a room. Recalculates paths and requests map highlighting.
    /// </summary>
    static void OnRoomEntered()
    {
        LogWithTimestamp("Room entered");
        UpdateBestPath();
        RequestHighlightOnMapOpen();
    }

    /// <summary>
    /// Requests path highlighting. If map screen is already open, highlights immediately.
    /// Otherwise, subscribes to the Opened event to highlight when it opens.
    /// </summary>
    static void RequestHighlightOnMapOpen()
    {
        LogWithTimestamp("Requesting highlight when map opens");

        // Check if map screen is already open
        var mapScreen = NMapScreen.Instance;
        if (mapScreen != null && mapScreen.IsOpen)
        {
            LogWithTimestamp("Map screen is already open, highlighting immediately");
            HighlightBestPath();
            return;
        }

        _pendingHighlight = true;

        // Subscribe to map screen opened event
        if (mapScreen != null)
        {
            mapScreen.Opened += OnMapScreenOpened;
            LogWithTimestamp("Subscribed to map screen Opened event");
        }
        else
        {
            LogWithTimestamp("NMapScreen.Instance is null, will retry on next act/room");
        }
    }

    /// <summary>
    /// Convenience function that saves configuration, updates best path, and requests map highlight.
    /// Wraps the three common operations that are frequently called together.
    /// </summary>
    static void SaveAndUpdatePath()
    {
        SaveConfiguration();
        UpdateBestPath();
        RequestHighlightOnMapOpen();
    }

    /// <summary>
    /// Event handler for when the map screen opens. Applies pending highlights.
    /// </summary>
    static void OnMapScreenOpened()
    {
        LogWithTimestamp("Map screen opened event triggered");

        if (!_pendingHighlight)
        {
            LogWithTimestamp("No pending highlight, ignoring");
            return;
        }

        _pendingHighlight = false;

        // Unsubscribe from the event
        var mapScreen = NMapScreen.Instance;
        if (mapScreen != null)
        {
            mapScreen.Opened -= OnMapScreenOpened;
            LogWithTimestamp("Unsubscribed from map screen Opened event");
        }

        // Apply the highlight
        HighlightBestPath();
    }

    /// <summary>
    /// Called when exiting a room. Recalculates paths for the new position.
    /// </summary>
    static void OnRoomExited()
    {
        LogWithTimestamp("Room exited");
        UpdateBestPath();
    }

    /// <summary>
    /// Calculates optimal paths for all configured path types from current position.
    /// Uses CurrentMapPoint if available, otherwise falls back to StartingMapPoint.
    /// </summary>
    static void UpdateBestPath()
    {
        var runState = RouteSuggest.RunState;
        if (runState == null)
        {
            return;
        }

        LogWithTimestamp($"Current act index {runState.CurrentActIndex}");
        LogWithTimestamp($"Floor {runState.ActFloor}/{runState.TotalFloor}");

        // Get current position, fallback to starting point if not set
        var startPoint = runState.CurrentMapPoint ?? runState.Map?.StartingMapPoint;

        if (startPoint != null)
        {
            LogWithTimestamp($"At map point {startPoint.coord}");

            // Calculate paths for all enabled path types
            CalculatedPaths.Clear();
            foreach (var config in PathConfigs)
            {
                if (!config.Enabled)
                {
                    LogWithTimestamp($"{config.Name}: skipped (disabled)");
                    continue;
                }
                var paths = FindOptimalPaths(startPoint, config);
                if (paths.Count > 0)
                {
                    int score = config.CalculateScore(paths[0]);
                    LogWithTimestamp($"{config.Name}: {paths.Count} optimal path(s) found with score {score}");
                    CalculatedPaths[config.Name] = paths;
                }
            }
        }
    }

    class DpMemoState
    {
        public List<(MapPoint Node, string Key)> Predecessors = new List<(MapPoint, string)>();
    }

    /// <summary>
    /// Finds optimal paths from startPoint to the Boss using the given configuration's scoring.
    /// Returns either one path (HighlightType.One) or all paths tied for best score (HighlightType.All).
    /// </summary>
    /// <param name="startPoint">Starting map point.</param>
    /// <param name="config">Path configuration with scoring weights.</param>
    /// <returns>List of optimal paths. Empty list if no paths found.</returns>
    static List<List<MapPoint>> FindOptimalPaths(MapPoint startPoint, PathConfig config)
    {
        if (startPoint == null) return new List<List<MapPoint>>();

        // 获取需要统计关注的房间类型（对于权重为0即未关注的房间，我们不需要记录在状态中以优化性能�?
        var trackedTypes = config.TargetCounts.Keys.ToList();

        // 动态规划状�?(DP State): 节点 (Node) -> 状态标�?(Key, 用逗号分隔的各类房间数�? -> 状态对�?(包含前驱节点)
        // 这样可以将在相同节点上、拥有相同已探索房间类型数量的路径合并，避免指数级爆�?(State space: O(V * C^k))
        var dp = new Dictionary<MapPoint, Dictionary<string, DpMemoState>>();

        var queue = new Queue<MapPoint>();
        var inQueue = new HashSet<MapPoint>();

        // 初始化起点状�?
        queue.Enqueue(startPoint);
        inQueue.Add(startPoint);

        var startCounts = new int[trackedTypes.Count];
        int startIdx = trackedTypes.IndexOf(startPoint.PointType);
        if (startIdx >= 0) startCounts[startIdx] = 1; // 如果起点是我们关注的房间类型，则数量 +1

        // 将初始房间计数序列化为字符串标识作为当前状态的 Key
        string startKey = trackedTypes.Count > 0 ? string.Join(",", startCounts) : "0";
        dp[startPoint] = new Dictionary<string, DpMemoState>();
        dp[startPoint][startKey] = new DpMemoState(); // 根节点没有前�?(Root has no predecessors)

        var bossNodes = new List<MapPoint>();

        // 使用 BFS 进行层级遍历进行状态转移。由于地图总是向右推进(DAG)，BFS保证无后效�?
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            inQueue.Remove(current);

            // 记录遇到�?Boss 节点作为最终终�?
            if (current.PointType == MapPointType.Boss && !bossNodes.Contains(current))
            {
                bossNodes.Add(current);
            }

            if (current.Children == null || current.Children.Count == 0) continue;

            var currentStates = dp[current];

            // 对所有相连的子节点进行状态转�?
            foreach (var child in current.Children)
            {
                if (!dp.ContainsKey(child))
                {
                    dp[child] = new Dictionary<string, DpMemoState>();
                }

                int childIdx = trackedTypes.IndexOf(child.PointType);

                // 遍历当前节点积累下来的所有有效状态历�?
                foreach (var parentKey in currentStates.Keys)
                {
                    // 反序列化父节点的状态标识（即各类房间的累积经过数量�?
                    var parentCounts = trackedTypes.Count > 0 ? parentKey.Split(',').Select(int.Parse).ToArray() : new int[0];
                    // 如果前往的子节点是受到关注统计的房间，则该项计数�?+1
                    if (childIdx >= 0 && parentCounts.Length > 0) parentCounts[childIdx]++;

                    // 重新序列化得到前驱在子节点上的新状态标�?
                    string childKey = trackedTypes.Count > 0 ? string.Join(",", parentCounts) : "0";

                    if (!dp[child].ContainsKey(childKey))
                    {
                        dp[child][childKey] = new DpMemoState();
                    }

                    // 记录从哪个父节点 (current)、带着什么状�?(parentKey) 转移而来的，用以后续回溯构建完整路径
                    var pred = (current, parentKey);
                    if (!dp[child][childKey].Predecessors.Contains(pred))
                    {
                        dp[child][childKey].Predecessors.Add(pred);
                    }
                }

                // 将未入队的子节点入队，等待下一轮转�?
                if (!inQueue.Contains(child))
                {
                    queue.Enqueue(child);
                    inQueue.Add(child);
                }
            }
        }

        // 遍历所�?Boss 节点的所有积累状态计算最终评分，找出最高分
        int bestScore = int.MinValue;
        var bestBossStates = new List<(MapPoint boss, string key)>();

        foreach (var boss in bossNodes)
        {
            if (dp.ContainsKey(boss))
            {
                foreach (var bossKey in dp[boss].Keys)
                {
                    // 对抵�?Boss 节点时的特定状态，计算其所有关卡总数与配置目标计数的曼哈顿误差平方和
                    var counts = trackedTypes.Count > 0 ? bossKey.Split(',').Select(int.Parse).ToArray() : new int[0];
                    int score = 0;
                    for (int i = 0; i < trackedTypes.Count; i++)
                    {
                        int target = config.TargetCounts[trackedTypes[i]];
                        int actual = counts[i];
                        int diff = target - actual;
                        // 这是最核心的评分函数映射。误差越大，惩罚呈现平方级增长，这意味着多目标权重的倾向会促使解越平滑地符合所有的数值要�?
                        score -= (diff * diff);
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestBossStates.Clear();
                        bestBossStates.Add((boss, bossKey));
                    }
                    else if (score == bestScore)
                    {
                        bestBossStates.Add((boss, bossKey));
                    }
                }
            }
        }

        LogWithTimestamp($"Found optimal score {bestScore} at Boss using DP");

        // Backtrack to build paths
        var optimalPaths = new List<List<MapPoint>>();
        foreach (var bestBossState in bestBossStates)
        {
            var pathsToBuild = new List<List<MapPoint>>();
            BacktrackPaths(bestBossState.boss, bestBossState.key, dp, new List<MapPoint>(), pathsToBuild);

            foreach (var path in pathsToBuild)
            {
                path.Reverse();
                optimalPaths.Add(path);
            }
        }

        // Sort paths by their coordinate sequence for reproducibility
        optimalPaths.Sort((a, b) =>
        {
            int minLen = Math.Min(a.Count, b.Count);
            for (int i = 0; i < minLen; i++)
            {
                int cmp = a[i].coord.CompareTo(b[i].coord);
                if (cmp != 0) return cmp;
            }
            return a.Count.CompareTo(b.Count);
        });

        LogWithTimestamp($"Found {optimalPaths.Count} optimal path(s) tied for best score");

        if (CurrentHighlightType == HighlightType.One && optimalPaths.Count > 1)
        {
            return new List<List<MapPoint>> { optimalPaths[0] };
        }

        return optimalPaths;
    }

    static void BacktrackPaths(MapPoint current, string key, Dictionary<MapPoint, Dictionary<string, DpMemoState>> dp, List<MapPoint> currentPath, List<List<MapPoint>> allPaths)
    {
        currentPath.Add(current);

        var state = dp[current][key];
        if (state.Predecessors.Count == 0)
        {
            // Reach start point
            allPaths.Add(new List<MapPoint>(currentPath));
        }
        else
        {
            foreach (var pred in state.Predecessors)
            {
                BacktrackPaths(pred.Node, pred.Key, dp, currentPath, allPaths);
            }
        }

        currentPath.RemoveAt(currentPath.Count - 1);
    }

    /// <summary>
    /// Highlights the calculated paths on the map screen. Higher priority paths render on top.
    /// Uses reflection to access the map's internal path rendering data.
    /// </summary>
    static void HighlightBestPath()
    {
        if (!_reflectionInitialized)
        {
            LogWithTimestamp("Highlighting skipped - reflection not initialized");
            return;
        }
        if (CalculatedPaths.Count == 0)
        {
            LogWithTimestamp("Highlighting skipped - no paths calculated");
            return;
        }

        try
        {
            // Clear any previous highlighting first
            ClearPathHighlighting();

            // Get the map screen instance
            var mapScreen = NMapScreen.Instance;
            if (mapScreen == null)
            {
                LogWithTimestamp("Highlighting skipped - NMapScreen.Instance is null");
                return;
            }
            if (!mapScreen.IsOpen)
            {
                LogWithTimestamp("Highlighting skipped - Map screen is not open");
                return;
            }

            // Get the _paths dictionary using reflection
            var paths = _pathsField.GetValue(mapScreen) as System.Collections.IDictionary;
            if (paths == null)
            {
                LogWithTimestamp("Failed to get paths dictionary");
                return;
            }

            // Build segment sets for each path type
            var pathSegments = new Dictionary<string, HashSet<(MapCoord, MapCoord)>>();
            foreach (var kvp in CalculatedPaths)
            {
                var pathList = kvp.Value;
                if (pathList != null && pathList.Count > 0)
                {
                    var segments = new HashSet<(MapCoord, MapCoord)>();
                    foreach (var path in pathList)
                    {
                        if (path != null && path.Count >= 2)
                        {
                            for (int i = 0; i < path.Count - 1; i++)
                            {
                                segments.Add((path[i].coord, path[i + 1].coord));
                            }
                        }
                    }
                    if (segments.Count > 0)
                    {
                        pathSegments[kvp.Key] = segments;
                    }
                }
            }

            // Collect colors for each segment to allow blending
            var segmentConfigs = new Dictionary<(MapCoord, MapCoord), List<Color>>();
            // Sort by priority ascending so that highest priority color is blended last
            var sortedConfigs = PathConfigs.Where(c => c.Enabled).OrderBy(c => c.Priority).ToList();

            foreach (var config in sortedConfigs)
            {
                if (!pathSegments.TryGetValue(config.Name, out var segments))
                    continue;

                foreach (var segment in segments)
                {
                    // Normalize segment direction for deduplication
                    var normalizedKey = segment.Item1.CompareTo(segment.Item2) <= 0
                        ? segment
                        : (segment.Item2, segment.Item1);

                    if (!segmentConfigs.ContainsKey(normalizedKey))
                    {
                        segmentConfigs[normalizedKey] = new List<Color>();
                    }
                    segmentConfigs[normalizedKey].Add(config.Color);
                }
            }

            var segmentColors = new Dictionary<(MapCoord, MapCoord), Color>();
            foreach (var kvp in segmentConfigs)
            {
                var colors = kvp.Value;
                if (colors.Count == 1)
                {
                    segmentColors[kvp.Key] = colors[0];
                }
                else
                {
                    // Blend colors for overlapping paths
                    // Since configs are sorted by priority (ascending), the highest priority
                    // color is blended last and has the strongest presence.
                    Color mix = colors[0];
                    for (int i = 1; i < colors.Count; i++)
                    {
                        mix = mix.Lerp(colors[i], 0.5f);
                    }
                    // Boost the brightness and alpha slightly to make the mixed path stand out
                    mix.R = Math.Min(mix.R * 1.2f, 1f);
                    mix.G = Math.Min(mix.G * 1.2f, 1f);
                    mix.B = Math.Min(mix.B * 1.2f, 1f);
                    mix.A = 1f;

                    segmentColors[kvp.Key] = mix;
                }
            }

            // Apply highlighting to all unique segments
            foreach (var kvp in segmentColors)
            {
                var segment = kvp.Key;
                var color = kvp.Value;

                // Try both directions (from->to and to->from)
                var key1 = segment;
                var key2 = (segment.Item2, segment.Item1);

                object pathTicks = null;
                if (paths.Contains(key1))
                {
                    pathTicks = paths[key1];
                }
                else if (paths.Contains(key2))
                {
                    pathTicks = paths[key2];
                }

                if (pathTicks != null)
                {
                    var ticks = pathTicks as IReadOnlyList<TextureRect>;
                    if (ticks != null)
                    {
                        foreach (var tick in ticks)
                        {
                            if (tick != null && GodotObject.IsInstanceValid(tick))
                            {
                                // Save original color and scale if not already saved
                                if (!OriginalTickProperties.ContainsKey(tick))
                                {
                                    OriginalTickProperties[tick] = (tick.Modulate, tick.Scale);
                                }
                                tick.Modulate = color;
                                tick.Scale = new Vector2(1.4f, 1.4f);
                            }
                        }
                    }
                }
            }

            LogWithTimestamp($"Highlighted {segmentColors.Count} unique path segments");
        }
        catch (Exception ex)
        {
            LogWithTimestamp($"Error highlighting path: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all path highlighting by restoring original colors and scales.
    /// Also cleans up invalid TextureRect entries from the cache.
    /// </summary>
    static void ClearPathHighlighting()
    {
        try
        {
            // Reset highlighted path ticks to their original color and scale
            var ticksToRemove = new List<TextureRect>();
            foreach (var kvp in OriginalTickProperties)
            {
                var tick = kvp.Key;
                var (originalColor, originalScale) = kvp.Value;

                if (tick != null && GodotObject.IsInstanceValid(tick))
                {
                    tick.Modulate = originalColor;
                    tick.Scale = originalScale;
                }
                else
                {
                    // Mark invalid ticks for removal
                    ticksToRemove.Add(tick);
                }
            }

            // Clean up invalid entries
            foreach (var tick in ticksToRemove)
            {
                OriginalTickProperties.Remove(tick);
            }
        }
        catch (Exception ex)
        {
            LogWithTimestamp($"Error clearing path highlighting: {ex.Message}");
        }
    }
}
