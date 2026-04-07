using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Logging;

namespace RouteSuggest;

/// <summary>
/// 负责配置读写与默认配置初始化。
/// </summary>
public static class ConfigManager
{
  /// <summary>
  /// 当前配置文件 schema 版本号。
  /// </summary>
  private const int CurrentSchemaVersion = 4;

  /// <summary>
  /// 回退路径标记，表示当前路径来自用户目录回退。
  /// </summary>
  private const string FallbackConfigPathMarker = "__fallback__";

  /// <summary>
  /// 配置发生变化时触发（来源可能是运行时面板或配置文件改动）。
  /// </summary>
  public static event Action<string> ConfigurationChanged;

  /// <summary>
  /// 配置文件绝对路径缓存。
  /// </summary>
  private static string _configFilePath;

  /// <summary>
  /// 计算配置路径时使用的程序集路径缓存（用于检测运行期变化）。
  /// </summary>
  private static string _configPathAssemblyLocation;

  /// <summary>
  /// 是否已启动运行时配置监听。
  /// </summary>
  private static bool _changeWatcherStarted = false;

  /// <summary>
  /// 最近一次观测到的配置指纹。
  /// </summary>
  private static int? _lastObservedConfigFingerprint;

  /// <summary>
  /// 最近一次观测到的配置文件 UTC 写入时间戳（ticks）。
  /// </summary>
  private static long _lastObservedConfigWriteTimeTicks = -1;

  /// <summary>
  /// 在批量加载/重置配置时暂时抑制变更通知，避免误触发刷新。
  /// </summary>
  private static bool _suppressChangeNotifications = false;

  /// <summary>
  /// 当前生效的路径配置集合。
  /// </summary>
  public static List<PathConfig> PathConfigs { get; private set; } = new List<PathConfig>();

  /// <summary>
  /// 当前高亮模式（单路线 / 多路线）。
  /// </summary>
  public static HighlightType CurrentHighlightType { get; set; } = HighlightType.One;

  /// <summary>
  /// 内置默认路线配置集合。
  /// </summary>
  public static readonly List<PathConfig> DefaultPathConfigs = new List<PathConfig>
    {
        new PathConfig { Name = "Safe (Green) / 安全", Color = new Color(0f, 1f, 0f, 1f), Priority = 100, Enabled = true, TargetCounts = new Dictionary<MapPointType, TargetRange> { { MapPointType.Elite, new TargetRange(0, 0) } } },
        new PathConfig { Name = "Aggressive (Red) / 激进", Color = new Color(1f, 0f, 0f, 1f), Priority = 50, Enabled = false, TargetCounts = new Dictionary<MapPointType, TargetRange> { { MapPointType.Elite, new TargetRange(15, 15) } } },
        new PathConfig { Name = "Question marks (Yellow) / 未知", Color = new Color(1f, 1f, 0f, 1f), Priority = 75, Enabled = false, TargetCounts = new Dictionary<MapPointType, TargetRange> { { MapPointType.Unknown, new TargetRange(15, 15) } } }
    };

  /// <summary>
  /// 启动时先恢复默认，再尝试覆盖本地存档配置。
  /// </summary>
  public static void Initialize()
  {
    _suppressChangeNotifications = true;
    ResetToDefault();
    LoadConfig();
    _suppressChangeNotifications = false;

    _lastObservedConfigFingerprint = BuildConfigFingerprint();
    RefreshObservedConfigWriteTime();
    StartConfigChangeWatcher();
  }

  /// <summary>
  /// 重置到内置默认配置。
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
        TargetCounts = new Dictionary<MapPointType, TargetRange>(defaultConfig.TargetCounts)
      };
      PathConfigs.Add(config);
    }
    RouteSuggestMod.Log("Reset to default path configurations");
  }

  /// <summary>
  /// 计算配置文件路径，优先使用程序集目录，其次回退到用户目录。
  /// </summary>
  /// <returns>配置文件绝对路径。</returns>
  public static string GetConfigFilePath()
  {
    string assemblyPath = null;
    try
    {
      assemblyPath = Assembly.GetExecutingAssembly().Location;
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogWarning($"Failed to resolve assembly location for config path: {ex.Message}");
    }

    if (!string.IsNullOrEmpty(assemblyPath))
    {
      if (_configFilePath != null && string.Equals(_configPathAssemblyLocation, assemblyPath, StringComparison.OrdinalIgnoreCase))
      {
        return _configFilePath;
      }

      _configPathAssemblyLocation = assemblyPath;
      var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
      _configFilePath = Path.Combine(assemblyDirectory ?? string.Empty, "RouteSuggestConfig.json");
      return _configFilePath;
    }

    if (_configFilePath != null && _configPathAssemblyLocation == FallbackConfigPathMarker)
    {
      return _configFilePath;
    }

    _configPathAssemblyLocation = FallbackConfigPathMarker;
    _configFilePath = Path.Combine(OS.GetUserDataDir(), "mods", "RouteSuggestConfig.json");
    return _configFilePath;
  }

  /// <summary>
  /// 保存当前配置到 JSON 文件。
  /// </summary>
  public static void SaveConfiguration()
  {
    try
    {
      string configPath = GetConfigFilePath();
      var dir = Path.GetDirectoryName(configPath);
      if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

      var configData = new ConfigFile
      {
        SchemaVersion = CurrentSchemaVersion,
        HighlightType = CurrentHighlightType.ToString(),
        PathConfigs = PathConfigs.Select(config => new PathConfigEntry
        {
          Name = config.Name,
          Color = $"#{config.Color.ToHtml(false)}",
          Priority = config.Priority,
          Enabled = config.Enabled,
          TargetCounts = (config.TargetCounts ?? new Dictionary<MapPointType, TargetRange>()).ToDictionary(
            kvp => kvp.Key.ToString(),
            kvp => new ScoreWeight { Min = kvp.Value.Min, Max = kvp.Value.Max })
        }).ToList()
      };

      var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
      File.WriteAllText(configPath, JsonSerializer.Serialize(configData, options));
      RefreshObservedConfigWriteTime();

      if (!_suppressChangeNotifications)
      {
        var currentFingerprint = BuildConfigFingerprint();
        var changed = _lastObservedConfigFingerprint != currentFingerprint;
        _lastObservedConfigFingerprint = currentFingerprint;
        if (changed) RaiseConfigurationChanged("save");
      }

      RouteSuggestMod.Log($"Config saved to {configPath}");
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"Failed to save config: {ex.Message}");
    }
  }

  /// <summary>
  /// 加载 JSON 配置；如果文件不存在则先生成一份默认配置。
  /// </summary>
  private static void LoadConfig()
  {
    try
    {
      string configPath = GetConfigFilePath();
      if (!File.Exists(configPath))
      {
        SaveConfiguration();
        return;
      }

      var configData = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(configPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
      if (configData == null)
      {
        RouteSuggestMod.LogWarning("Config deserialization returned null; keeping in-memory defaults.");
        return;
      }

      int originalSchemaVersion = configData.SchemaVersion;
      bool migrated = MigrateConfigIfNeeded(configData);

      if (configData.SchemaVersion > CurrentSchemaVersion)
      {
        RouteSuggestMod.LogWarning($"Config schema v{configData.SchemaVersion} is newer than supported v{CurrentSchemaVersion}; trying best-effort load.");
      }

      if (Enum.TryParse<HighlightType>(configData.HighlightType, out var loadedType))
        CurrentHighlightType = loadedType;
      else if (!string.IsNullOrWhiteSpace(configData.HighlightType))
        RouteSuggestMod.LogWarning($"Unknown highlight_type '{configData.HighlightType}', keeping current value '{CurrentHighlightType}'.");

      if (configData.PathConfigs != null && configData.PathConfigs.Count > 0)
      {
        PathConfigs.Clear();
        foreach (var entry in configData.PathConfigs)
        {
          PathConfigs.Add(new PathConfig
          {
            Name = string.IsNullOrWhiteSpace(entry.Name) ? "Unnamed Config" : entry.Name,
            Priority = entry.Priority,
            Color = ParseColor(entry.Color),
            Enabled = entry.Enabled,
            TargetCounts = ParseTargetCounts(entry.TargetCounts)
          });
        }
      }
      else
      {
        RouteSuggestMod.LogWarning("Config file contains no path_configs; keeping in-memory defaults.");
      }

      RefreshObservedConfigWriteTime();

      if (migrated)
      {
        RouteSuggestMod.Log($"Config migrated from schema v{originalSchemaVersion} to v{CurrentSchemaVersion}.");
        SaveConfiguration();
      }
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"Error loading config: {ex.Message}. Keeping in-memory defaults.");
    }
  }

  /// <summary>
  /// 当读取到旧版本配置时执行最小迁移，并返回是否发生了迁移。
  /// </summary>
  private static bool MigrateConfigIfNeeded(ConfigFile configData)
  {
    if (configData == null) return false;

    if (configData.SchemaVersion <= 0)
    {
      RouteSuggestMod.LogWarning("Config schema_version missing or invalid; assuming legacy schema v1.");
      configData.SchemaVersion = 1;
    }

    if (configData.SchemaVersion >= CurrentSchemaVersion) return false;

    // v4: 将字段语义统一为 target_counts，同时继续兼容 legacy scoring_weights 读取。
    if (configData.PathConfigs != null)
    {
      foreach (var path in configData.PathConfigs)
      {
        path.TargetCounts ??= new Dictionary<string, ScoreWeight>();
      }
    }

    configData.SchemaVersion = CurrentSchemaVersion;
    return true;
  }

  /// <summary>
  /// 解析 <c>#RRGGBB</c> / <c>#RRGGBBAA</c> 格式颜色。
  /// </summary>
  /// <param name="colorStr">颜色字符串。</param>
  /// <returns>解析后的颜色。</returns>
  public static Color ParseColor(string colorStr)
  {
    var fallback = new Color(1f, 1f, 1f, 1f);
    if (string.IsNullOrWhiteSpace(colorStr)) return fallback;

    var raw = colorStr.Trim();
    var hex = raw.StartsWith("#", StringComparison.Ordinal) ? raw.Substring(1) : raw;
    if (hex.Length != 6 && hex.Length != 8)
    {
      RouteSuggestMod.LogWarning($"Invalid color format '{colorStr}', fallback to white.");
      return fallback;
    }

    if (!TryParseHexByte(hex, 0, out var r) ||
        !TryParseHexByte(hex, 2, out var g) ||
        !TryParseHexByte(hex, 4, out var b))
    {
      RouteSuggestMod.LogWarning($"Invalid color hex '{colorStr}', fallback to white.");
      return fallback;
    }

    byte a = 255;
    if (hex.Length == 8 && !TryParseHexByte(hex, 6, out a))
    {
      RouteSuggestMod.LogWarning($"Invalid alpha hex in color '{colorStr}', fallback to white.");
      return fallback;
    }

    return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
  }

  /// <summary>
  /// 解析 2 位十六进制字节字符串。
  /// </summary>
  private static bool TryParseHexByte(string hex, int startIndex, out byte value)
  {
    value = 0;
    if (hex == null || startIndex < 0 || startIndex + 2 > hex.Length) return false;

    return byte.TryParse(
      hex.AsSpan(startIndex, 2),
      NumberStyles.HexNumber,
      CultureInfo.InvariantCulture,
      out value);
  }

  /// <summary>
  /// 将配置文件中的字符串键转换为 <see cref="MapPointType"/> 枚举。
  /// </summary>
  /// <param name="dict">源字典。</param>
  /// <returns>转换后的目标区间字典。</returns>
  private static Dictionary<MapPointType, TargetRange> ParseTargetCounts(Dictionary<string, ScoreWeight> dict)
  {
    var result = new Dictionary<MapPointType, TargetRange>();
    if (dict == null) return result;
    foreach (var kvp in dict)
    {
      if (kvp.Value == null) continue;
      if (Enum.TryParse<MapPointType>(kvp.Key, out var pt))
      {
        result[pt] = new TargetRange(kvp.Value.Min, kvp.Value.Max);
      }
    }
    return result;
  }

  /// <summary>
  /// 启动每帧配置监听，自动检测运行时改值和外部配置文件改动。
  /// </summary>
  private static void StartConfigChangeWatcher()
  {
    if (_changeWatcherStarted) return;

    var tree = Engine.GetMainLoop() as SceneTree;
    if (tree == null)
    {
      RouteSuggestMod.LogError("Failed to start config watcher: SceneTree is not ready");
      return;
    }

    tree.ProcessFrame -= OnProcessFrame;
    tree.ProcessFrame += OnProcessFrame;
    _changeWatcherStarted = true;
  }

  /// <summary>
  /// 每帧检测配置变化并触发变更通知。
  /// </summary>
  private static void OnProcessFrame()
  {
    if (_suppressChangeNotifications) return;

    if (TryReloadConfigIfFileChanged()) return;

    var currentFingerprint = BuildConfigFingerprint();
    if (_lastObservedConfigFingerprint == null)
    {
      _lastObservedConfigFingerprint = currentFingerprint;
      return;
    }

    if (_lastObservedConfigFingerprint != currentFingerprint)
    {
      _lastObservedConfigFingerprint = currentFingerprint;
      RaiseConfigurationChanged("runtime");
    }
  }

  /// <summary>
  /// 若配置文件被外部修改，则重新加载并触发刷新。
  /// </summary>
  /// <returns>若已处理文件变更并触发刷新，返回 true。</returns>
  private static bool TryReloadConfigIfFileChanged()
  {
    try
    {
      var configPath = GetConfigFilePath();
      if (!File.Exists(configPath)) return false;

      var currentWriteTicks = File.GetLastWriteTimeUtc(configPath).Ticks;
      if (currentWriteTicks == _lastObservedConfigWriteTimeTicks) return false;

      _lastObservedConfigWriteTimeTicks = currentWriteTicks;

      var beforeFingerprint = BuildConfigFingerprint();

      _suppressChangeNotifications = true;
      try
      {
        LoadConfig();
      }
      finally
      {
        _suppressChangeNotifications = false;
      }

      var afterFingerprint = BuildConfigFingerprint();
      _lastObservedConfigFingerprint = afterFingerprint;

      if (beforeFingerprint != afterFingerprint)
      {
        RouteSuggestMod.Log("Detected external config file update");
        RaiseConfigurationChanged("file");
        return true;
      }
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"Error watching config file: {ex.Message}");
    }

    return false;
  }

  /// <summary>
  /// 刷新最近一次观测到的配置文件写入时间。
  /// </summary>
  private static void RefreshObservedConfigWriteTime()
  {
    try
    {
      var configPath = GetConfigFilePath();
      _lastObservedConfigWriteTimeTicks = File.Exists(configPath)
        ? File.GetLastWriteTimeUtc(configPath).Ticks
        : -1;
    }
    catch (Exception ex)
    {
      _lastObservedConfigWriteTimeTicks = -1;
      RouteSuggestMod.LogWarning($"Failed to refresh config file write time: {ex.Message}");
    }
  }

  /// <summary>
  /// 计算当前配置的稳定指纹（用于检测是否发生实际变更）。
  /// </summary>
  /// <returns>配置指纹哈希值。</returns>
  private static int BuildConfigFingerprint()
  {
    var hash = new HashCode();

    hash.Add(CurrentHighlightType);
    foreach (var config in PathConfigs.OrderBy(c => c.Name))
    {
      hash.Add(config.Name ?? string.Empty);
      hash.Add(config.Enabled);
      hash.Add(config.Priority);
      hash.Add(config.Color.R);
      hash.Add(config.Color.G);
      hash.Add(config.Color.B);
      hash.Add(config.Color.A);

      if (config.TargetCounts == null) continue;
      foreach (var kvp in config.TargetCounts.OrderBy(kvp => kvp.Key))
      {
        hash.Add((int)kvp.Key);
        hash.Add(kvp.Value.Min);
        hash.Add(kvp.Value.Max);
      }
    }

    return hash.ToHashCode();
  }

  /// <summary>
  /// 安全触发配置变更通知。
  /// </summary>
  /// <param name="source">变更来源标记。</param>
  private static void RaiseConfigurationChanged(string source)
  {
    if (_suppressChangeNotifications) return;

    try
    {
      ConfigurationChanged?.Invoke(source);
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"Error notifying config change ({source}): {ex.Message}");
    }
  }

  private class ConfigFile
  {
    /// <summary>
    /// 配置文件版本号，后续用于迁移。
    /// </summary>
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("highlight_type")] public string HighlightType { get; set; }
    [JsonPropertyName("path_configs")] public List<PathConfigEntry> PathConfigs { get; set; }
  }

  private class ScoreWeight
  {
    /// <summary>
    /// 房间类型目标数量下限。
    /// </summary>
    [JsonPropertyName("min")] public int Min { get; set; }

    /// <summary>
    /// 房间类型目标数量上限。
    /// </summary>
    [JsonPropertyName("max")] public int Max { get; set; }
  }

  private class PathConfigEntry
  {
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("color")] public string Color { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    /// <summary>
    /// 新字段：各房间类型目标区间。
    /// </summary>
    [JsonPropertyName("target_counts")]
    public Dictionary<string, ScoreWeight> TargetCounts { get; set; }

    /// <summary>
    /// 旧字段兼容：历史版本使用 scoring_weights。
    /// </summary>
    [JsonPropertyName("scoring_weights")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ScoreWeight> LegacyScoringWeights
    {
      get => null;
      set
      {
        if (value == null || value.Count == 0) return;
        TargetCounts ??= new Dictionary<string, ScoreWeight>();

        foreach (var kvp in value)
        {
          if (!TargetCounts.ContainsKey(kvp.Key))
          {
            TargetCounts[kvp.Key] = kvp.Value;
          }
        }
      }
    }
  }

}