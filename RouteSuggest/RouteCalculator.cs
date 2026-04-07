﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    /// 评分时会跟踪的房间类型。
    /// </summary>
    private static readonly MapPointType[] TrackedTypes =
    {
        MapPointType.RestSite,
        MapPointType.Treasure,
        MapPointType.Shop,
        MapPointType.Monster,
        MapPointType.Elite,
        MapPointType.Unknown
    };

    /// <summary>
    /// 房间类型到压缩状态索引的映射，避免高频 IndexOf。
    /// </summary>
    private static readonly IReadOnlyDictionary<MapPointType, int> TrackedTypeIndex =
        TrackedTypes.Select((type, index) => new { type, index }).ToDictionary(x => x.type, x => x.index);

    /// <summary>
    /// 每个配置最多保留的候选路径数。
    /// </summary>
    private const int MaxPathsPerConfig = 3;

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
    /// 用于避免同一起点重复计算。
    /// </summary>
    private static MapPoint _lastStartPoint;

    /// <summary>
    /// 配置指纹缓存：当配置发生变化时，即使起点不变也会触发重算。
    /// </summary>
    private static int? _lastConfigFingerprint;

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
        if (runState == null && manager != null)
        {
            var prop = manager.GetType().GetProperty("CurrentRun") ?? manager.GetType().GetProperty("Run");
            if (prop != null)
            {
                runState = prop.GetValue(manager) as RunState;
            }
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

        var reachableNodes = CollectReachableNodes(startPoint);
        var topoOrder = BuildTopologicalOrder(reachableNodes, out var hasCycle);
        if (hasCycle)
        {
            RouteSuggestMod.LogError("Detected cycle in map graph; route calculation skipped to avoid infinite traversal.");
            return finalResult;
        }

        var dp = new Dictionary<MapPoint, Dictionary<ulong, DpMemoState>>();
        var startStateKey = 0UL;
        if (TryGetTrackedTypeIndex(startPoint.PointType, out var startTypeIndex))
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

                var childTypeTracked = TryGetTrackedTypeIndex(child.PointType, out var childTypeIndex);
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

        var configNameCounter = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var config in configs)
        {
            var endStateScores = new List<(MapPoint Node, ulong StateKey, int Score)>();
            foreach (var endNode in endNodes)
            {
                if (!dp.TryGetValue(endNode, out var endStates)) continue;
                foreach (var stateEntry in endStates)
                {
                    var score = EvaluateState(stateEntry.Key, config);
                    endStateScores.Add((endNode, stateEntry.Key, score));
                }
            }

            endStateScores.Sort((a, b) => b.Score.CompareTo(a.Score));

            var uniquePaths = new List<List<MapPoint>>();
            var seenPathKeys = new HashSet<string>(StringComparer.Ordinal);
            var backtrackSteps = 0;

            foreach (var candidate in endStateScores)
            {
                if (uniquePaths.Count >= MaxPathsPerConfig) break;

                var remaining = MaxPathsPerConfig - uniquePaths.Count;
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

                    uniquePaths.Add(path);
                    if (uniquePaths.Count >= MaxPathsPerConfig) break;
                }

                if (backtrackSteps >= MaxBacktrackSteps)
                {
                    RouteSuggestMod.LogWarning($"Backtracking reached step limit ({MaxBacktrackSteps}); candidate paths were truncated.");
                    break;
                }
            }

            var sortedBest = uniquePaths
                .OrderByDescending(path => config.CalculateScore(path))
                .ThenBy(path => path.Count)
                .ThenBy(BuildPathIdentity, StringComparer.Ordinal)
                .Take(MaxPathsPerConfig)
                .ToList();

            var exportPaths = ConfigManager.CurrentHighlightType == HighlightType.One && sortedBest.Count > 1
                ? sortedBest.Take(1).ToList()
                : sortedBest;

            var configKey = BuildConfigResultKey(config.Name, configNameCounter);
            var readonlyPaths = new ReadOnlyCollection<IReadOnlyList<MapPoint>>(
                exportPaths
                    .Select(path => (IReadOnlyList<MapPoint>)new ReadOnlyCollection<MapPoint>(path))
                    .ToList());
            finalResult[configKey] = readonlyPaths;
        }

        return finalResult;
    }

    /// <summary>
    /// 生成配置输出键，避免重名配置覆盖结果。
    /// </summary>
    private static string BuildConfigResultKey(string name, Dictionary<string, int> usage)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "Unnamed Config" : name;
        if (!usage.TryGetValue(baseName, out var count))
        {
            usage[baseName] = 1;
            return baseName;
        }

        count++;
        usage[baseName] = count;
        return $"{baseName} ({count})";
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
    /// <returns>配置指纹哈希值。</returns>
    private static int BuildConfigFingerprint()
    {
        var hash = new HashCode();

        hash.Add(ConfigManager.CurrentHighlightType);
        foreach (var config in ConfigManager.PathConfigs.OrderBy(c => c.Name))
        {
            hash.Add(config.Name ?? string.Empty);
            hash.Add(config.Enabled);
            hash.Add(config.Priority);
            hash.Add(config.Color.R);
            hash.Add(config.Color.G);
            hash.Add(config.Color.B);
            hash.Add(config.Color.A);

            if (config.TargetCounts == null) continue;
            foreach (var kvp in config.TargetCounts.OrderBy(k => k.Key))
            {
                hash.Add((int)kvp.Key);
                hash.Add(kvp.Value.Min);
                hash.Add(kvp.Value.Max);
            }
        }

        return hash.ToHashCode();
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
        if (typeIndex < 0 || typeIndex >= 8) return state;

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
        if (typeIndex < 0 || typeIndex >= 8) return 0;
        return (int)((state >> (typeIndex * 8)) & 0xFF);
    }

    /// <summary>
    /// 尝试获取房间类型在压缩状态中的索引。
    /// </summary>
    private static bool TryGetTrackedTypeIndex(MapPointType pointType, out int index)
    {
        return TrackedTypeIndex.TryGetValue(pointType, out index);
    }

    /// <summary>
    /// 对状态进行评分：落在区间内奖励，偏离区间按平方惩罚。
    /// </summary>
    /// <param name="stateKey">压缩状态。</param>
    /// <param name="config">评分配置。</param>
    /// <returns>状态分值。</returns>
    private static int EvaluateState(ulong stateKey, PathConfig config)
    {
        if (config.TargetCounts == null || config.TargetCounts.Count == 0) return 0;

        var score = 0;
        foreach (var kvp in config.TargetCounts)
        {
            var actual = 0;
            if (TryGetTrackedTypeIndex(kvp.Key, out var typeIndex))
            {
                actual = GetFromState(stateKey, typeIndex);
            }

            var range = kvp.Value;
            if (actual < range.Min)
            {
                var diff = range.Min - actual;
                score -= diff * diff * 50;
            }
            else if (actual > range.Max)
            {
                var diff = actual - range.Max;
                score -= diff * diff * 50;
            }
            else
            {
                var mid = (range.Min + range.Max) / 2.0;
                score += (int)(10 - Math.Abs(actual - mid) * 2);
            }
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
        return string.Join("->", path.Select(point => point?.coord.ToString() ?? "null"));
    }
}

