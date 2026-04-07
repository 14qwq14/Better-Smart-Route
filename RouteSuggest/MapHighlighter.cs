using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RouteSuggest;

/// <summary>
/// 负责地图路径高亮：监听地图界面生命周期并把最佳路径渲染到路径节点上。
/// </summary>
public static class MapHighlighter
{
  /// <summary>
  /// 记录每个 tick 的原始颜色和缩放，方便取消高亮时还原。
  /// </summary>
  private static readonly Dictionary<TextureRect, (Color color, Vector2 scale)> OriginalTickProperties = new();

  /// <summary>
  /// 反射获取 <c>NMapScreen</c> 私有字段 <c>_paths</c>（地图连线 -> tick 列表）。
  /// </summary>
  private static FieldInfo _pathsField;
  private static bool _reflectionInitialized = false;
  private static bool _reflectionFailureNotified = false;

  /// <summary>
  /// 自动重绑地图实例相关状态。
  /// </summary>
  private static bool _autoHookStarted = false;
  private static NMapScreen _hookedMapScreen = null;

  /// <summary>
  /// 当请求刷新时地图实例暂不可用，先挂起，等地图实例出现后再补绘。
  /// </summary>
  private static bool _pendingHighlightRequest = false;

  /// <summary>
  /// 初始化反射缓存，避免每次高亮都走反射查找。
  /// </summary>
  public static void InitializeReflection()
  {
    try
    {
      var mapScreenType = typeof(NMapScreen);
      _pathsField = mapScreenType.GetField("_paths", BindingFlags.NonPublic | BindingFlags.Instance);
      _reflectionInitialized = _pathsField != null;
      if (_reflectionInitialized)
      {
        _reflectionFailureNotified = false;
        RouteSuggestMod.Log("Reflection initialized successfully");
      }
      else
      {
        RouteSuggestMod.LogWarning("Reflection initialization failed: field '_paths' not found.");
      }
    }
    catch (Exception ex)
    {
      _reflectionInitialized = false;
      RouteSuggestMod.LogError($"Error initializing reflection: {ex.Message}");
    }
  }

  /// <summary>
  /// 启动每帧检查：用于在地图实例变化时自动重绑 <c>Opened</c> 事件。
  /// </summary>
  public static void StartAutoMapScreenHook()
  {
    if (_autoHookStarted) return;

    var tree = Engine.GetMainLoop() as SceneTree;
    if (tree == null)
    {
      RouteSuggestMod.LogError("Failed to start map auto-hook: SceneTree is not ready");
      return;
    }

    tree.ProcessFrame += OnProcessFrame;
    _autoHookStarted = true;
  }

  /// <summary>
  /// 每帧尝试检测地图实例是否变化，并处理挂起的重绘请求。
  /// </summary>
  private static void OnProcessFrame()
  {
    TryHookMapScreenInstance();

    if (!_pendingHighlightRequest) return;

    var mapScreen = NMapScreen.Instance;
    if (mapScreen == null) return;

    _pendingHighlightRequest = false;
    RouteCalculator.UpdateBestPath();
    HighlightBestPath();
  }

  /// <summary>
  /// 若出现新的地图界面实例，解除旧绑定并绑定到新实例。
  /// </summary>
  private static void TryHookMapScreenInstance()
  {
    var mapScreen = NMapScreen.Instance;
    if (mapScreen == null || mapScreen == _hookedMapScreen) return;

    if (_hookedMapScreen != null)
    {
      try { _hookedMapScreen.Opened -= OnMapScreenOpened; }
      catch (Exception ex)
      {
        RouteSuggestMod.LogWarning($"Failed to unhook previous map screen Opened event: {ex.Message}");
      }
    }

    mapScreen.Opened -= OnMapScreenOpened;
    mapScreen.Opened += OnMapScreenOpened;
    _hookedMapScreen = mapScreen;
    RouteSuggestMod.Log("Hooked map screen Opened event");
  }

  /// <summary>
  /// 外部请求刷新入口（配置变更、房间切换等场景都会调用）。
  /// </summary>
  public static void RequestHighlightOnMapOpen()
  {
    if (!_autoHookStarted) StartAutoMapScreenHook();

    TryHookMapScreenInstance();
    var mapScreen = NMapScreen.Instance;

    // 此时如果已经拿到了 mapScreen 的实例（即使从界面外调用的），尝试直接执行一次渲染高亮
    // 这个方法也是我们在设置面板中调节颜色或数值时，能使设置“实时生效”肉眼可见的关键代码
    if (mapScreen != null)
    {
      _pendingHighlightRequest = false;
      RouteCalculator.UpdateBestPath();
      HighlightBestPath();
    }
    else
    {
      _pendingHighlightRequest = true;
    }
  }

  /// <summary>
  /// 地图屏幕打开（<c>Opened</c> 事件）时重绘，保证开关地图后高亮仍存在。
  /// </summary>
  private static void OnMapScreenOpened()
  {
    _pendingHighlightRequest = false;
    RouteCalculator.UpdateBestPath();
    HighlightBestPath();
  }

  /// <summary>
  /// 强制清空所有已应用高亮，并清除原始属性缓存。
  /// </summary>
  public static void ForceClearHighlighting()
  {
    ClearPathHighlighting();
    OriginalTickProperties.Clear();
  }

  /// <summary>
  /// 根据当前最佳路径结果对地图路径进行高亮渲染。
  /// </summary>
  public static void HighlightBestPath()


  {


    if (!_reflectionInitialized)
    {
      InitializeReflection();
      if (!_reflectionInitialized)
      {
        if (!_reflectionFailureNotified)
        {
          RouteSuggestMod.LogWarning("Path highlighting is temporarily unavailable because reflection metadata is not ready.");
          _reflectionFailureNotified = true;
        }

        return;
      }
    }


    ClearPathHighlighting();

    if (RouteCalculator.CalculatedPaths.Count == 0) return;

    try
    {

      var mapScreen = NMapScreen.Instance;
      if (mapScreen == null) return;

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

      var sortedConfigs = ConfigManager.PathConfigs.Where(c => c.Enabled).OrderBy(c => c.Priority).ToList();

      var segmentColors = new Dictionary<(MapCoord, MapCoord), Color>();

      // 如果多条路线共享边，直接显示优先级最高的路线颜色。
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
        object pathTicks = paths.Contains(segment) ? paths[segment] : null;

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

  /// <summary>
  /// 将所有被修改过的 tick 还原到原始颜色与缩放。
  /// </summary>
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
