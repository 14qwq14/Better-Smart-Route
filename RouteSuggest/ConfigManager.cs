using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Logging;

namespace RouteSuggest;

public static class ConfigManager
{
  private static string _configFilePath;

  public static List<PathConfig> PathConfigs { get; private set; } = new List<PathConfig>();
  public static HighlightType CurrentHighlightType { get; set; } = HighlightType.One;

  public static readonly List<PathConfig> DefaultPathConfigs = new List<PathConfig>
    {
        new PathConfig { Name = "Safe (Green) / 安全", Color = new Color(0f, 1f, 0f, 1f), Priority = 100, Enabled = true, TargetCounts = new() { { MapPointType.Elite, new TargetRange(0, 0) } } },
        new PathConfig { Name = "Aggressive (Red) / 激进", Color = new Color(1f, 0f, 0f, 1f), Priority = 50, Enabled = true, TargetCounts = new() { { MapPointType.Elite, new TargetRange(15, 15) } } },
        new PathConfig { Name = "Question marks (Yellow) / 问号", Color = new Color(1f, 1f, 0f, 1f), Priority = 75, Enabled = true, TargetCounts = new() { { MapPointType.Unknown, new TargetRange(15, 15) } } },
        new PathConfig { Name = "Boss Rush (Magenta) / 首领速通", Color = new Color(1f, 0f, 1f, 1f), Priority = 120, Enabled = true, TargetCounts = new() { { MapPointType.Elite, new TargetRange(15, 15) }, { MapPointType.RestSite, new TargetRange(0, 0) }, { MapPointType.Monster, new TargetRange(0, 0) } } },
        new PathConfig { Name = "Max Rewards (Cyan) / 最大收益", Color = new Color(0f, 1f, 1f, 1f), Priority = 90, Enabled = true, TargetCounts = new() { { MapPointType.Elite, new TargetRange(15, 15) }, { MapPointType.Treasure, new TargetRange(10, 10) }, { MapPointType.Shop, new TargetRange(5, 5) } } }
    };

  public static void Initialize()
  {
    ResetToDefault();
    LoadConfig();
  }

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

  public static string GetConfigFilePath()
  {
    if (_configFilePath != null) return _configFilePath;

    try
    {
      string assemblyPath = Assembly.GetExecutingAssembly().Location;
      if (!string.IsNullOrEmpty(assemblyPath))
      {
        _configFilePath = Path.Combine(Path.GetDirectoryName(assemblyPath), "RouteSuggestConfig.json");
        return _configFilePath;
      }
    }
    catch { }

    // Fallback for weird runtimes
    _configFilePath = Path.Combine(OS.GetUserDataDir(), "mods", "RouteSuggestConfig.json");
    return _configFilePath;
  }

  public static void SaveConfiguration()
  {
    try
    {
      string configPath = GetConfigFilePath();
      var dir = Path.GetDirectoryName(configPath);
      if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

      var configData = new ConfigFile
      {
        SchemaVersion = 3,
        HighlightType = CurrentHighlightType.ToString(),
        PathConfigs = PathConfigs.Select(config => new PathConfigEntry
        {
          Name = config.Name,
          Color = $"#{config.Color.ToHtml(false)}",
          Priority = config.Priority,
          Enabled = config.Enabled,
          TargetCounts = config.TargetCounts.ToDictionary(kvp => kvp.Key.ToString(), kvp => new ScoreWeight { Min = kvp.Value.Min, Max = kvp.Value.Max })
        }).ToList()
      };

      var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
      File.WriteAllText(configPath, JsonSerializer.Serialize(configData, options));
      RouteSuggestMod.Log($"Config saved to {configPath}");
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"Failed to save config: {ex.Message}");
    }
  }

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
      if (configData == null) return;

      if (Enum.TryParse<HighlightType>(configData.HighlightType, out var loadedType))
        CurrentHighlightType = loadedType;

      if (configData.PathConfigs != null)
      {
        PathConfigs.Clear();
        foreach (var entry in configData.PathConfigs)
        {
          PathConfigs.Add(new PathConfig
          {
            Name = entry.Name,
            Priority = entry.Priority,
            Color = ParseColor(entry.Color),
            Enabled = entry.Enabled,
            TargetCounts = ParseTargetCounts(entry.TargetCounts)
          });
        }
      }
    }
    catch (Exception ex)
    {
      RouteSuggestMod.Log($"Error loading config: {ex.Message}, reverting to default.");
      SaveConfiguration();
    }
  }

  private static Color ParseColor(string colorStr)
  {
    if (string.IsNullOrEmpty(colorStr)) return new Color(1f, 1f, 1f, 1f);
    if (colorStr.StartsWith("#")) colorStr = colorStr.Substring(1);

    if (colorStr.Length == 6)
    {
      return new Color(Convert.ToInt32(colorStr.Substring(0, 2), 16) / 255f,
                       Convert.ToInt32(colorStr.Substring(2, 2), 16) / 255f,
                       Convert.ToInt32(colorStr.Substring(4, 2), 16) / 255f, 1f);
    }
    if (colorStr.Length == 8)
    {
      return new Color(Convert.ToInt32(colorStr.Substring(0, 2), 16) / 255f,
                       Convert.ToInt32(colorStr.Substring(2, 2), 16) / 255f,
                       Convert.ToInt32(colorStr.Substring(4, 2), 16) / 255f,
                       Convert.ToInt32(colorStr.Substring(6, 2), 16) / 255f);
    }
    return new Color(1f, 1f, 1f, 1f);
  }

    private static Dictionary<MapPointType, TargetRange> ParseTargetCounts(Dictionary<string, ScoreWeight> dict)
  {
    var result = new Dictionary<MapPointType, TargetRange>();
    if (dict == null) return result;
    foreach (var kvp in dict) {
      if (Enum.TryParse<MapPointType>(kvp.Key, out var pt)) {
         result[pt] = new TargetRange(kvp.Value.Min, kvp.Value.Max);
      }
    }
    return result;
  }

  private class ConfigFile
  {
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("highlight_type")] public string HighlightType { get; set; }
    [JsonPropertyName("path_configs")] public List<PathConfigEntry> PathConfigs { get; set; }
  }

    private class ScoreWeight
  {
    [JsonPropertyName("min")] public int Min { get; set; }
    [JsonPropertyName("max")] public int Max { get; set; }
  }

  private class PathConfigEntry
  {
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("color")] public string Color { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;     
    [JsonPropertyName("scoring_weights")] public Dictionary<string, ScoreWeight> TargetCounts { get; set; }
  }

}