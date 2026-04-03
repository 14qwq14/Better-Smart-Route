using Godot;
using System;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;

namespace RouteSuggest;

[ModInitializer("ModLoaded")]
public static class RouteSuggestMod
{
  public static RunState RunState { get; private set; }

  public static void LogInfo(string message) { Log(message); }
  public static void LogError(string message) { MegaCrit.Sts2.Core.Logging.Log.Error($"RouteSuggest: {message}"); }
  public static void Log(string message)
  {
    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    MegaCrit.Sts2.Core.Logging.Log.Info($"[{timestamp}] RouteSuggest: {message}");
  }

  public static void ModLoaded()
  {
    Log("Mod loaded");

    ConfigManager.Initialize();

    try
    {
      var tree = (SceneTree)Engine.GetMainLoop();
      if (tree != null)
      {
        var ui = new ConfigMenu();
        tree.Root.CallDeferred("add_child", ui);
      }
    }
    catch { }

    var manager = RunManager.Instance;
    manager.RunStarted += OnRunStarted;
    manager.ActEntered += OnActEntered;
    manager.RoomEntered += OnRoomEntered;
    manager.RoomExited += OnRoomExited;

    MapHighlighter.InitializeReflection();
    // UI handled by Custom Config Menu
  }

  private static void OnRunStarted(RunState runState)
  {
    Log("Run started");
    MapHighlighter.ForceClearHighlighting();
    RunState = runState;
    RouteCalculator.InvalidateCache();
    RouteCalculator.UpdateBestPath();
  }

  private static void OnActEntered()
  {
    Log("Act entered");
    RouteCalculator.InvalidateCache();
    RouteCalculator.UpdateBestPath();
    MapHighlighter.RequestHighlightOnMapOpen();
  }

  private static void OnRoomEntered()
  {
    Log("Room entered");
    RouteCalculator.UpdateBestPath();
    MapHighlighter.RequestHighlightOnMapOpen();
  }

  private static void OnRoomExited()
  {
    Log("Room exited");
    RouteCalculator.UpdateBestPath();
  }
}
