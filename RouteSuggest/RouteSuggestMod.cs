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

  public static void Log(string message)
  {
    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    MegaCrit.Sts2.Core.Logging.Log.Warn($"[{timestamp}] RouteSuggest: {message}");
  }

  public static void ModLoaded()
  {
    Log("Mod loaded");

    ConfigManager.Initialize();

    var manager = RunManager.Instance;
    manager.RunStarted += OnRunStarted;
    manager.ActEntered += OnActEntered;
    manager.RoomEntered += OnRoomEntered;
    manager.RoomExited += OnRoomExited;
    manager.RunEnded += OnRunEnded;

    MapHighlighter.InitializeReflection();
    ModConfigAdapter.DeferredRegisterModConfig();
  }

  private static void OnRunStarted(RunState runState)
  {
    Log("Run started");
    RunState = runState;
    RouteCalculator.InvalidateCache();
    RouteCalculator.UpdateBestPath();
  }

  private static void OnRunEnded(RunState obj)
  {
    MapHighlighter.ForceClearHighlighting();
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
