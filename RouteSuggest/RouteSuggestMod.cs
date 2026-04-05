using Godot;
using System;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;

namespace RouteSuggest;

/// <summary>
/// RouteSuggest 模组入口：负责初始化配置、订阅运行事件并协调路径计算与地图高亮。
/// </summary>
[ModInitializer("ModLoaded")]
public static class RouteSuggestMod
{
  /// <summary>
  /// 当前 Run 的运行态缓存，供路径计算器读取。
  /// </summary>
  public static RunState RunState { get; private set; }

  /// <summary>
  /// 兼容旧调用名的日志包装。
  /// </summary>
  /// <param name="message">日志消息。</param>
  public static void LogInfo(string message) { Log(message); }

  /// <summary>
  /// 输出错误日志。
  /// </summary>
  /// <param name="message">错误消息。</param>
  public static void LogError(string message) { MegaCrit.Sts2.Core.Logging.Log.Error($"RouteSuggest: {message}"); }

  /// <summary>
  /// 输出统一格式的普通日志（带时间戳），便于排查刷新和事件时序问题。
  /// </summary>
  /// <param name="message">日志消息。</param>
  public static void Log(string message)
  {
    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    MegaCrit.Sts2.Core.Logging.Log.Info($"[{timestamp}] RouteSuggest: {message}");
  }

  /// <summary>
  /// Mod 入口：初始化配置、订阅运行事件，并启动地图高亮模块。
  /// </summary>
  public static void ModLoaded()
  {
    Log("Mod loaded");

    ConfigManager.Initialize();
    ModConfigBridge.DeferredRegister();
    ConfigManager.ConfigurationChanged -= OnConfigurationChanged;
    ConfigManager.ConfigurationChanged += OnConfigurationChanged;

    var manager = RunManager.Instance;
    manager.RunStarted += OnRunStarted;
    manager.ActEntered += OnActEntered;
    manager.RoomEntered += OnRoomEntered;
    manager.RoomExited += OnRoomExited;

    MapHighlighter.InitializeReflection();
    MapHighlighter.StartAutoMapScreenHook();
    // UI handled by Custom Config Menu
  }

  /// <summary>
  /// 新开局回调：清空旧高亮并重算路线。
  /// </summary>
  /// <param name="runState">新开局的运行状态。</param>
  private static void OnRunStarted(RunState runState)
  {
    Log("Run started");
    MapHighlighter.ForceClearHighlighting();
    RunState = runState;
    RefreshRoutePreview("Run started");
  }

  /// <summary>
  /// 进入新章节回调：地图结构可能变化，先失效缓存再计算。
  /// </summary>
  private static void OnActEntered()
  {
    Log("Act entered");
    RefreshRoutePreview("Act entered");
  }

  /// <summary>
  /// 进入房间回调：刷新路线并请求地图打开时重绘。
  /// </summary>
  private static void OnRoomEntered()
  {
    Log("Room entered");
    RefreshRoutePreview("Room entered");
  }

  /// <summary>
  /// 离开房间回调：位置变化后更新一次候选路线。
  /// </summary>
  private static void OnRoomExited()
  {
    Log("Room exited");
    RouteCalculator.UpdateBestPath();
  }

  /// <summary>
  /// 配置发生变化时，统一触发路线重算与地图重绘请求。
  /// </summary>
  /// <param name="source">配置变更来源。</param>
  private static void OnConfigurationChanged(string source)
  {
    RefreshRoutePreview($"Config changed ({source})");
  }

  /// <summary>
  /// 统一刷新路线预览链路：失效缓存 -> 重算路径 -> 请求地图高亮。
  /// </summary>
  /// <param name="reason">触发刷新原因。</param>
  private static void RefreshRoutePreview(string reason)
  {
    Log($"Refresh route preview: {reason}");
    RouteCalculator.InvalidateCache();
    RouteCalculator.UpdateBestPath();
    MapHighlighter.RequestHighlightOnMapOpen();
  }
}
