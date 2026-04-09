﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RouteSuggest;

/// <summary>
/// 负责根据当前地图起点和策略配置，计算若干条最优路线。
/// </summary>
public static class RouteCalculator
{
    /// <summary>
    /// 对外暴露的只读计算结果：配置名 -> 路径列表。
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<IReadOnlyList<MapPoint>>> _calculatedPaths = new();

    /// <summary>
    /// 按“配置名 -> 候选路径列表”缓存本次计算结果，供高亮模块使用。
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<MapPoint>>> CalculatedPaths => _calculatedPaths;

    /// <summary>
    /// 每个终点状态单次回溯最多生成路径数。
    /// </summary>
    private const int MaxBacktrackPathsPerState = 3;

    /// <summary>
    /// 回溯允许的最大路径深度（防御性阈值）。
    /// </summary>
    private const int MaxBacktrackDepth = 64;

    /// <summary>
    /// 回溯步骤上限，防止在极端图上指数爆炸。
    /// </summary>
    private const int MaxBacktrackSteps = 100_000;

    /// <summary>
    /// 压缩状态可编码的最大房间类型槽位数（每槽 8 bit）。
    /// </summary>
    private const int MaxTrackedTypeSlots = 8;

    /// <summary>
    /// 用于避免同一起点重复计算。
    /// </summary>
    private static MapPoint _lastStartPoint;

    /// <summary>
    /// 配置指纹缓存：当配置发生变化时，即使起点不变也会触发重算。
    /// </summary>
    private static string _lastConfigFingerprint;

    /// <summary>
    /// RunManager 回退反射读取 RunState 的属性缓存。
    /// </summary>
    private static PropertyInfo _cachedRunStateProperty;

    /// <summary>
    /// 上次建立反射缓存时的 RunManager 运行时类型。
    /// </summary>
    private static Type _cachedRunManagerType;

    /// <summary>
    /// 避免在状态槽位溢出时刷屏日志。
    /// </summary>
    private static bool _stateSlotOverflowWarningLogged;

    /// <summary>
    /// 避免反射属性不存在时刷屏日志。
    /// </summary>
    private static bool _runStateReflectionMissingLogged;

    /// <summary>
    /// 失效内部缓存，强制下一次调用重新计算路径。
    /// </summary>
    public static void InvalidateCache()
    {
        _lastStartPoint = null;
        _lastConfigFingerprint = null;
    }

    /// <summary>
    /// 主入口：根据当前运行状态和配置计算最佳路径集合。
    /// </summary>
    public static void UpdateBestPath()
    {
        var runState = RouteSuggestMod.RunState;
        var manager = RunManager.Instance;
        if (runState == null)
        {
            runState = ResolveRunStateFromManager(manager);
        }

        if (runState == null)
        {
            RouteSuggestMod.LogWarning("UpdateBestPath skipped: RunState is null.");
            return;
        }

        var startPoint = runState.CurrentMapPoint ?? runState.Map?.StartingMapPoint;
        if (startPoint == null)
        {
            _calculatedPaths.Clear();
            return;
        }

        var configFingerprint = BuildConfigFingerprint();
        if (_lastStartPoint == startPoint && _lastConfigFingerprint == configFingerprint) return;

        _lastStartPoint = startPoint;
        _lastConfigFingerprint = configFingerprint;

        MapHighlighter.ForceClearHighlighting();
        _calculatedPaths.Clear();

        var activeConfigs = ConfigManager.PathConfigs.Where(c => c.Enabled).ToList();
        if (activeConfigs.Count == 0) return;

        var results = FindAllOptimalPaths(startPoint, activeConfigs);
        foreach (var kvp in results)
        {
            _calculatedPaths[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// 当入口态缓存为空时，尝试通过 RunManager 反射读取运行态（带属性缓存）。
    /// </summary>
    private static RunState ResolveRunStateFromManager(RunManager manager)
    {
        if (manager == null) return null;

        var runtimeType = manager.GetType();
        if (_cachedRunStateProperty == null || _cachedRunManagerType != runtimeType)
        {
            _cachedRunManagerType = runtimeType;
            _cachedRunStateProperty = runtimeType.GetProperty("CurrentRun") ?? runtimeType.GetProperty("Run");

            if (_cachedRunStateProperty == null)
            {
                if (!_runStateReflectionMissingLogged)
                {
                    RouteSuggestMod.LogWarning($"UpdateBestPath fallback failed: RunManager has neither 'CurrentRun' nor 'Run' property. GameVersion={RouteSuggestMod.GetGameVersionSummary()}");
                    _runStateReflectionMissingLogged = true;
                }
            }
            else
            {
                _runStateReflectionMissingLogged = false;
            }
        }

        return _cachedRunStateProperty?.GetValue(manager) as RunState;
    }

    /// <summary>
    /// DP 状态节点，记录可回溯到当前状态的前驱信息。
    /// </summary>
    private sealed class DpMemoState
    {
        /// <summary>
        /// 记录可回溯到本状态的前驱节点与前驱状态 key。
        /// </summary>
        public HashSet<(MapPoint Node, ulong Key)> Predecessors { get; } = new();
    }

    /// <summary>
    /// 计算各配置下的最优路径集合。
    /// </summary>
    /// <param name="startPoint">路径起点。</param>
    /// <param name="configs">启用的路径配置集合。</param>
    /// <returns>按配置名分组的候选路径集合（只读）。</returns>
    private static Dictionary<string, IReadOnlyList<IReadOnlyList<MapPoint>>> FindAllOptimalPaths(MapPoint startPoint, List<PathConfig> configs)
    {
        var finalResult = new Dictionary<string, IReadOnlyList<IReadOnlyList<MapPoint>>>();
        var configKeyMap = ConfigSnapshotUtility.BuildResultKeyMap(configs);
        var trackedTypeIndex = BuildTrackedTypeIndex(configs);
        var maxPathsPerConfig = GetMaxPathsPerConfig();

        var reachableNodes = CollectReachableNodes(startPoint);
        var topoOrder = BuildTopologicalOrder(reachableNodes, out var hasCycle);
        if (hasCycle)
        {
            RouteSuggestMod.LogError("Detected cycle in map graph; route calculation skipped to avoid infinite traversal.");
            return finalResult;
        }

        var dp = new Dictionary<MapPoint, Dictionary<ulong, DpMemoState>>();
        var startStateKey = 0UL;
        if (TryGetTrackedTypeIndex(startPoint.PointType, trackedTypeIndex, out var startTypeIndex))
        {
            startStateKey = AddToState(startStateKey, startTypeIndex, 1);
        }

        dp[startPoint] = new Dictionary<ulong, DpMemoState> { [startStateKey] = new DpMemoState() };
        var bossNodes = new HashSet<MapPoint>();

        foreach (var current in topoOrder)
        {
            if (current.PointType == MapPointType.Boss) bossNodes.Add(current);
            if (!dp.TryGetValue(current, out var currentStates)) continue;
            if (current.Children == null || current.Children.Count == 0) continue;

            foreach (var child in current.Children)
            {
                if (child == null || !reachableNodes.Contains(child)) continue;

                if (!dp.TryGetValue(child, out var childStates))
                {
                    childStates = new Dictionary<ulong, DpMemoState>();
                    dp[child] = childStates;
                }

                var childTypeTracked = TryGetTrackedTypeIndex(child.PointType, trackedTypeIndex, out var childTypeIndex);
                foreach (var parentEntry in currentStates)
                {
                    var childKey = childTypeTracked ? AddToState(parentEntry.Key, childTypeIndex, 1) : parentEntry.Key;

                    if (!childStates.TryGetValue(childKey, out var childState))
                    {
                        childState = new DpMemoState();
                        childStates[childKey] = childState;
                    }

                    childState.Predecessors.Add((current, parentEntry.Key));
                }
            }
        }

        var endNodes = bossNodes.Count > 0
            ? bossNodes
            : reachableNodes.Where(n => n.Children == null || n.Children.Count == 0).ToHashSet();

        foreach (var config in configs)
        {
            var endStateScores = new List<(MapPoint Node, ulong StateKey, int Score)>();
            foreach (var endNode in endNodes)
            {
                if (!dp.TryGetValue(endNode, out var endStates)) continue;
                foreach (var stateEntry in endStates)
                {
                    var score = EvaluateState(stateEntry.Key, config, trackedTypeIndex);
                    endStateScores.Add((endNode, stateEntry.Key, score));
                }
            }

            endStateScores.Sort((a, b) => b.Score.CompareTo(a.Score));

            var uniquePaths = new List<(List<MapPoint> Path, int Score, string PathKey)>();
            var seenPathKeys = new HashSet<string>(StringComparer.Ordinal);
            var backtrackSteps = 0;

            foreach (var candidate in endStateScores)
            {
                if (uniquePaths.Count >= maxPathsPerConfig) break;

                var remaining = maxPathsPerConfig - uniquePaths.Count;
                var currentBatch = new List<List<MapPoint>>();
                BacktrackPaths(
                    candidate.Node,
                    candidate.StateKey,
                    dp,
                    new List<MapPoint>(),
                    currentBatch,
                    Math.Min(remaining, MaxBacktrackPathsPerState),
                    MaxBacktrackDepth,
                    ref backtrackSteps,
                    MaxBacktrackSteps);

                foreach (var path in currentBatch)
                {
                    path.Reverse();
                    var pathKey = BuildPathIdentity(path);
                    if (!seenPathKeys.Add(pathKey)) continue;

                    uniquePaths.Add((path, candidate.Score, pathKey));
                    if (uniquePaths.Count >= maxPathsPerConfig) break;
                }

                if (backtrackSteps >= MaxBacktrackSteps)
                {
                    RouteSuggestMod.LogWarning($"Backtracking reached step limit ({MaxBacktrackSteps}); candidate paths were truncated.");
                    break;
                }
            }

            var selectedPaths = uniquePaths
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.PathKey, StringComparer.Ordinal)
                .Select(entry => entry.Path)
                .Take(maxPathsPerConfig)
                .ToList();

            var exportPaths = ConfigManager.CurrentHighlightType == HighlightType.One && selectedPaths.Count > 1
                ? selectedPaths.Take(1).ToList()
                : selectedPaths;

            var configKey = configKeyMap.TryGetValue(config, out var resolvedKey)
                ? resolvedKey
                : (string.IsNullOrWhiteSpace(config.Name) ? "Unnamed Config" : config.Name);
            var readonlyPaths = new ReadOnlyCollection<IReadOnlyList<MapPoint>>(
                exportPaths
                    .Select(path => (IReadOnlyList<MapPoint>)new ReadOnlyCollection<MapPoint>(path))
                    .ToList());
            finalResult[configKey] = readonlyPaths;
        }

        return finalResult;
    }

    /// <summary>
    /// 根据当前启用配置动态构建房间类型状态索引。
    /// </summary>
    private static IReadOnlyDictionary<MapPointType, int> BuildTrackedTypeIndex(IReadOnlyList<PathConfig> configs)
    {
        var trackedTypes = new SortedSet<MapPointType>(Comparer<MapPointType>.Create((a, b) => ((int)a).CompareTo((int)b)));

        if (configs != null)
        {
            foreach (var config in configs)
            {
                if (config?.TargetCounts == null) continue;
                foreach (var pointType in config.TargetCounts.Keys)
                {
                    trackedTypes.Add(pointType);
                }
            }
        }

        var indexMap = new Dictionary<MapPointType, int>();
        var index = 0;
        foreach (var pointType in trackedTypes)
        {
            if (index >= MaxTrackedTypeSlots)
            {
                WarnStateSlotOverflowOnce($"pointType={pointType}");
                break;
            }

            indexMap[pointType] = index;
            index++;
        }

        return indexMap;
    }

    /// <summary>
    /// 收集从起点可达的所有节点。
    /// </summary>
    private static HashSet<MapPoint> CollectReachableNodes(MapPoint startPoint)
    {
        var visited = new HashSet<MapPoint>();
        var stack = new Stack<MapPoint>();
        stack.Push(startPoint);
        visited.Add(startPoint);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Children == null || node.Children.Count == 0) continue;

            foreach (var child in node.Children)
            {
                if (child == null || !visited.Add(child)) continue;
                stack.Push(child);
            }
        }

        return visited;
    }

    /// <summary>
    /// 对可达子图进行拓扑排序；若存在环则返回 hasCycle=true。
    /// </summary>
    private static List<MapPoint> BuildTopologicalOrder(HashSet<MapPoint> reachableNodes, out bool hasCycle)
    {
        var indegree = reachableNodes.ToDictionary(node => node, _ => 0);

        foreach (var node in reachableNodes)
        {
            if (node.Children == null || node.Children.Count == 0) continue;
            foreach (var child in node.Children)
            {
                if (child == null || !reachableNodes.Contains(child)) continue;
                indegree[child]++;
            }
        }

        var queue = new Queue<MapPoint>(indegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var order = new List<MapPoint>(reachableNodes.Count);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            order.Add(node);

            if (node.Children == null || node.Children.Count == 0) continue;
            foreach (var child in node.Children)
            {
                if (child == null || !reachableNodes.Contains(child)) continue;
                indegree[child]--;
                if (indegree[child] == 0) queue.Enqueue(child);
            }
        }

        hasCycle = order.Count != reachableNodes.Count;
        return order;
    }

    /// <summary>
    /// 基于当前配置生成稳定指纹，用于检测是否需要重新计算。
    /// </summary>
    /// <returns>配置指纹字符串。</returns>
    private static string BuildConfigFingerprint()
    {
        return ConfigSnapshotUtility.BuildFingerprint(ConfigManager.CurrentHighlightType, ConfigManager.PathConfigs)
            + ";max_paths=" + GetMaxPathsPerConfig();
    }

    /// <summary>
    /// 读取并归一化“每配置最多路径数”。
    /// </summary>
    private static int GetMaxPathsPerConfig()
    {
        return Math.Max(1, ConfigManager.MaxPathsPerConfig);
    }

    /// <summary>
    /// 将某一房间类型计数累加到压缩状态值中。
    /// </summary>
    /// <param name="state">当前压缩状态。</param>
    /// <param name="typeIndex">房间类型索引。</param>
    /// <param name="amount">累加数量。</param>
    /// <returns>累加后的压缩状态。</returns>
    private static ulong AddToState(ulong state, int typeIndex, int amount)
    {
        if (typeIndex < 0 || typeIndex >= MaxTrackedTypeSlots)
        {
            WarnStateSlotOverflowOnce($"typeIndex={typeIndex}");
            return state;
        }

        var shift = typeIndex * 8;
        var currentVal = (state >> shift) & 0xFF;
        currentVal = Math.Min(255, currentVal + (uint)amount);

        state &= ~(0xFFUL << shift);
        state |= (currentVal << shift);
        return state;
    }

    /// <summary>
    /// 从压缩状态中读取某一房间类型计数。
    /// </summary>
    /// <param name="state">压缩状态。</param>
    /// <param name="typeIndex">房间类型索引。</param>
    /// <returns>该房间类型计数。</returns>
    private static int GetFromState(ulong state, int typeIndex)
    {
        if (typeIndex < 0 || typeIndex >= MaxTrackedTypeSlots) return 0;
        return (int)((state >> (typeIndex * 8)) & 0xFF);
    }

    /// <summary>
    /// 尝试获取房间类型在压缩状态中的索引。
    /// </summary>
    private static bool TryGetTrackedTypeIndex(MapPointType pointType, IReadOnlyDictionary<MapPointType, int> trackedTypeIndex, out int index)
    {
        index = -1;
        if (trackedTypeIndex == null) return false;
        if (!trackedTypeIndex.TryGetValue(pointType, out index)) return false;
        if (index < MaxTrackedTypeSlots) return true;

        WarnStateSlotOverflowOnce($"pointType={pointType}, index={index}");
        index = -1;
        return false;
    }

    /// <summary>
    /// 当状态槽位不足时仅告警一次，避免刷屏。
    /// </summary>
    private static void WarnStateSlotOverflowOnce(string details)
    {
        if (_stateSlotOverflowWarningLogged) return;

        _stateSlotOverflowWarningLogged = true;
        RouteSuggestMod.LogWarning($"State encoding supports up to {MaxTrackedTypeSlots} tracked map-point types; extra tracked type is ignored ({details}).");
    }

    /// <summary>
    /// 对状态进行评分：落在区间内奖励，偏离区间按平方惩罚。
    /// </summary>
    /// <param name="stateKey">压缩状态。</param>
    /// <param name="config">评分配置。</param>
    /// <returns>状态分值。</returns>
    private static int EvaluateState(ulong stateKey, PathConfig config, IReadOnlyDictionary<MapPointType, int> trackedTypeIndex)
    {
        if (config.TargetCounts == null || config.TargetCounts.Count == 0) return 0;

        var score = 0;
        foreach (var kvp in config.TargetCounts)
        {
            var actual = 0;
            if (TryGetTrackedTypeIndex(kvp.Key, trackedTypeIndex, out var typeIndex))
            {
                actual = GetFromState(stateKey, typeIndex);
            }

            score += PathConfig.EvaluateTargetRangeScore(actual, kvp.Value);
        }

        return score;
    }

    /// <summary>
    /// 从终点状态回溯构建具体路径（带上限保护）。
    /// </summary>
    private static void BacktrackPaths(
        MapPoint current,
        ulong key,
        Dictionary<MapPoint, Dictionary<ulong, DpMemoState>> dp,
        List<MapPoint> currentPath,
        List<List<MapPoint>> allPaths,
        int maxPaths,
        int maxDepth,
        ref int stepCounter,
        int maxSteps)
    {
        if (allPaths.Count >= maxPaths || stepCounter >= maxSteps) return;
        if (currentPath.Count >= maxDepth) return;

        if (!dp.TryGetValue(current, out var stateMap) || !stateMap.TryGetValue(key, out var state)) return;

        stepCounter++;
        currentPath.Add(current);

        if (state.Predecessors.Count == 0)
        {
            allPaths.Add(new List<MapPoint>(currentPath));
            currentPath.RemoveAt(currentPath.Count - 1);
            return;
        }

        foreach (var pred in state.Predecessors)
        {
            BacktrackPaths(pred.Node, pred.Key, dp, currentPath, allPaths, maxPaths, maxDepth, ref stepCounter, maxSteps);
            if (allPaths.Count >= maxPaths || stepCounter >= maxSteps) break;
        }

        currentPath.RemoveAt(currentPath.Count - 1);
    }

    /// <summary>
    /// 构造路径稳定标识，用于去重与稳定排序。
    /// </summary>
    private static string BuildPathIdentity(IReadOnlyList<MapPoint> path)
    {
        if (path == null || path.Count == 0) return "len=0";

        var sb = new System.Text.StringBuilder(path.Count * 24 + 8);
        sb.Append("len=").Append(path.Count).Append(';');

        foreach (var point in path)
        {
            if (point == null)
            {
                sb.Append("n;");
                continue;
            }

            var coordText = point.coord.ToString() ?? "null";
            sb.Append(coordText.Length)
              .Append(':')
              .Append(coordText)
              .Append(';');
        }

        return sb.ToString();
    }
}

