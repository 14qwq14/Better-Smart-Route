using Godot;
using System;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

namespace RouteSuggest;

/// <summary>
/// RouteSuggest 模组入口：负责初始化配置、订阅运行事件并协调路径计算与地图高亮。
/// </summary>
[ModInitializer("ModLoaded")]
public static class RouteSuggestMod
{
  /// <summary>
  /// 当前已绑定事件的 RunManager 实例。
  /// </summary>
  private static RunManager _subscribedRunManager;

  /// <summary>
  /// 是否已启动每帧 RunManager 绑定检查。
  /// </summary>
  private static bool _runManagerWatcherStarted;

  /// <summary>
  /// RunManager 绑定检查轮询间隔（毫秒）。
  /// </summary>
  private const long RunManagerPollIntervalMilliseconds = 500;

  /// <summary>
  /// 下一次允许进行 RunManager 重绑检查的时间戳（毫秒）。
  /// </summary>
  private static long _nextRunManagerPollAtMilliseconds;

  /// <summary>
  /// 避免在 RunManager 暂不可用时重复刷屏日志。
  /// </summary>
  private static bool _missingRunManagerLogged;

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
  /// 输出警告日志。
  /// </summary>
  /// <param name="message">警告消息。</param>
  public static void LogWarning(string message) { MegaCrit.Sts2.Core.Logging.Log.Warn($"RouteSuggest: {message}"); }

  /// <summary>
  /// 输出统一格式的普通日志（带时间戳），便于排查刷新和事件时序问题。
  /// </summary>
  /// <param name="message">日志消息。</param>
  public static void Log(string message)
  {
    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
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

    EnsureRunManagerSubscriptions();
    StartRunManagerWatcher();

    MapHighlighter.InitializeReflection();
    MapHighlighter.StartAutoMapScreenHook();
    // UI handled by Custom Config Menu
  }

  /// <summary>
  /// 启动每帧检查，确保 RunManager 实例变化时正确解绑旧事件并绑定新实例。
  /// </summary>
  private static void StartRunManagerWatcher()
  {
    if (_runManagerWatcherStarted) return;

    var tree = Engine.GetMainLoop() as SceneTree;
    if (tree == null)
    {
      LogWarning("SceneTree is not ready; RunManager watcher not started.");
      return;
    }

    tree.ProcessFrame -= OnProcessFrame;
    tree.ProcessFrame += OnProcessFrame;
    _nextRunManagerPollAtMilliseconds = 0;
    _runManagerWatcherStarted = true;
  }

  /// <summary>
  /// 每帧检查 RunManager 是否被替换（例如热重载/场景重建），并进行重绑。
  /// </summary>
  private static void OnProcessFrame()
  {
    var now = System.Environment.TickCount64;
    if (now < _nextRunManagerPollAtMilliseconds) return;

    _nextRunManagerPollAtMilliseconds = now + RunManagerPollIntervalMilliseconds;
    EnsureRunManagerSubscriptions();
  }

  /// <summary>
  /// 确保仅在当前 RunManager 实例上注册一次事件；实例变化时会先解绑旧实例。
  /// </summary>
  private static void EnsureRunManagerSubscriptions()
  {
    var manager = RunManager.Instance;
    if (ReferenceEquals(manager, _subscribedRunManager)) return;

    if (_subscribedRunManager != null)
    {
      _subscribedRunManager.RunStarted -= OnRunStarted;
      _subscribedRunManager.ActEntered -= OnActEntered;
      _subscribedRunManager.RoomEntered -= OnRoomEntered;
      _subscribedRunManager.RoomExited -= OnRoomExited;
      Log("Unsubscribed events from previous RunManager instance");
    }

    _subscribedRunManager = manager;
    if (_subscribedRunManager == null)
    {
      if (!_missingRunManagerLogged)
      {
        LogWarning("RunManager.Instance is null; will retry binding on next frame.");
        _missingRunManagerLogged = true;
      }

      return;
    }

    _missingRunManagerLogged = false;
    _subscribedRunManager.RunStarted += OnRunStarted;
    _subscribedRunManager.ActEntered += OnActEntered;
    _subscribedRunManager.RoomEntered += OnRoomEntered;
    _subscribedRunManager.RoomExited += OnRoomExited;
    Log("Subscribed events to RunManager instance");
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
