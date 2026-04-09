using Godot;
using System;

namespace RouteSuggest;

/// <summary>
/// 全局帧观察器：统一订阅一次 <see cref="SceneTree.ProcessFrame"/>，并分发到各模块回调。
/// </summary>
internal static class GlobalFrameWatcher
{
  /// <summary>
  /// 是否已完成全局帧事件订阅。
  /// </summary>
  private static bool _started;

  /// <summary>
  /// 共享帧回调事件；各模块可订阅并执行轻量轮询逻辑。
  /// </summary>
  internal static event Action FrameTick;

  /// <summary>
  /// 确保全局帧观察器已启动。
  /// </summary>
  /// <returns>启动成功返回 true；SceneTree 尚不可用时返回 false。</returns>
  internal static bool EnsureStarted()
  {
    if (_started) return true;

    var tree = Engine.GetMainLoop() as SceneTree;
    if (tree == null)
    {
      RouteSuggestMod.LogWarning("GlobalFrameWatcher: SceneTree is not ready.");
      return false;
    }

    tree.ProcessFrame -= OnProcessFrame;
    tree.ProcessFrame += OnProcessFrame;
    _started = true;
    RouteSuggestMod.Log("GlobalFrameWatcher started");
    return true;
  }

  /// <summary>
  /// Godot 每帧回调：分发给所有订阅者并隔离异常。
  /// </summary>
  private static void OnProcessFrame()
  {
    var handlers = FrameTick;
    if (handlers == null) return;

    foreach (Action handler in handlers.GetInvocationList())
    {
      try
      {
        handler();
      }
      catch (Exception ex)
      {
        RouteSuggestMod.LogError($"GlobalFrameWatcher subscriber failed: {ex.Message}");
      }
    }
  }
}
