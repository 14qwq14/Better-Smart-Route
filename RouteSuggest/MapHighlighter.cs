using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RouteSuggest;

public static class MapHighlighter
{
  private static readonly Dictionary<TextureRect, (Color color, Vector2 scale)> OriginalTickProperties = new();
  private static FieldInfo _pathsField;
  private static bool _reflectionInitialized = false;
  private static bool _pendingHighlight = false;

  public static void InitializeReflection()
  {
    try
    {
      var mapScreenType = typeof(NMapScreen);
      _pathsField = mapScreenType.GetField("_paths", BindingFlags.NonPublic | BindingFlags.Instance);
      _reflectionInitialized = _pathsField != null;
      if (_reflectionInitialized) RouteSuggestMod.Log("Reflection initialized successfully");
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"Error initializing reflection: {ex.Message}");
    }
  }

  public static void RequestHighlightOnMapOpen()
  {
    var mapScreen = NMapScreen.Instance;
    if (mapScreen != null && mapScreen.IsOpen)
    {
      HighlightBestPath();
      return;
    }

    _pendingHighlight = true;
    if (mapScreen != null)
    {
      mapScreen.Opened += OnMapScreenOpened;
    }
  }

  private static void OnMapScreenOpened()
  {
    if (!_pendingHighlight) return;
    _pendingHighlight = false;

    var mapScreen = NMapScreen.Instance;
    if (mapScreen != null) mapScreen.Opened -= OnMapScreenOpened;

    HighlightBestPath();
  }

  public static void ForceClearHighlighting()
  {
    ClearPathHighlighting();
    OriginalTickProperties.Clear();
  }

  public static void HighlightBestPath()
  {
    if (!_reflectionInitialized) return;
    if (RouteCalculator.CalculatedPaths.Count == 0) return;

    try
    {
      ClearPathHighlighting();

      var mapScreen = NMapScreen.Instance;
      if (mapScreen == null || !mapScreen.IsOpen) return;

      var paths = _pathsField?.GetValue(mapScreen) as System.Collections.IDictionary;
      if (paths == null) return;

      var pathSegments = new Dictionary<string, HashSet<(MapCoord, MapCoord)>>();
      foreach (var kvp in RouteCalculator.CalculatedPaths)
      {
        var segments = new HashSet<(MapCoord, MapCoord)>();
        foreach (var path in kvp.Value)
        {
          if (path != null && path.Count >= 2)
          {
            for (int i = 0; i < path.Count - 1; i++) segments.Add((path[i].coord, path[i + 1].coord));
          }
        }
        if (segments.Count > 0) pathSegments[kvp.Key] = segments;
      }

      var segmentConfigs = new Dictionary<(MapCoord, MapCoord), List<Color>>();
      var sortedConfigs = ConfigManager.PathConfigs.Where(c => c.Enabled).OrderBy(c => c.Priority).ToList();

      foreach (var config in sortedConfigs)
      {
        if (!pathSegments.TryGetValue(config.Name, out var segments)) continue;
        foreach (var segment in segments)
        {
          var normalizedKey = segment.Item1.CompareTo(segment.Item2) <= 0 ? segment : (segment.Item2, segment.Item1);
          if (!segmentConfigs.ContainsKey(normalizedKey)) segmentConfigs[normalizedKey] = new List<Color>();
          segmentConfigs[normalizedKey].Add(config.Color);
        }
      }

      var segmentColors = new Dictionary<(MapCoord, MapCoord), Color>();

      // 颜色不混合：如果多条路线共享边，直接显示优先级最高的路线颜色
      var segmentTopPriority = new Dictionary<(MapCoord, MapCoord), int>();
      foreach (var config in sortedConfigs)
      {
        if (!pathSegments.TryGetValue(config.Name, out var segments)) continue;
        foreach (var segment in segments)
        {
          var normalizedKey = segment.Item1.CompareTo(segment.Item2) <= 0 ? segment : (segment.Item2, segment.Item1);
          if (!segmentTopPriority.TryGetValue(normalizedKey, out var existingPriority) || config.Priority > existingPriority)
          {
            var color = config.Color;
            color.A = 1f;
            segmentColors[normalizedKey] = color;
            segmentTopPriority[normalizedKey] = config.Priority;
          }
        }
      }

      foreach (var kvp in segmentColors)
      {
        var segment = kvp.Key;
        object pathTicks = paths.Contains(segment) ? paths[segment] : (paths.Contains((segment.Item2, segment.Item1)) ? paths[(segment.Item2, segment.Item1)] : null);

        if (pathTicks is IReadOnlyList<TextureRect> ticks)
        {
          foreach (var tick in ticks)
          {
            if (tick != null && GodotObject.IsInstanceValid(tick))
            {
              if (!OriginalTickProperties.ContainsKey(tick)) OriginalTickProperties[tick] = (tick.Modulate, tick.Scale);
              tick.Modulate = kvp.Value;
              tick.Scale = new Vector2(1.4f, 1.4f);
            }
          }
        }
      }
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"Error highlighting path: {ex.Message}");
    }
  }

  private static void ClearPathHighlighting()
  {
    try
    {
      var ticksToRemove = new List<TextureRect>();
      foreach (var kvp in OriginalTickProperties)
      {
        var tick = kvp.Key;
        if (tick != null && GodotObject.IsInstanceValid(tick))
        {
          tick.Modulate = kvp.Value.color;
          tick.Scale = kvp.Value.scale;
        }
        else
        {
          ticksToRemove.Add(tick);
        }
      }
      foreach (var tick in ticksToRemove) OriginalTickProperties.Remove(tick);
    }
    catch (Exception ex)
    {
      RouteSuggestMod.LogError($"Error clearing path highlighting: {ex.Message}");
    }
  }
}
