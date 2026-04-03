using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RouteSuggest;

public static class RouteCalculator
{
    public static Dictionary<string, List<List<MapPoint>>> CalculatedPaths { get; private set; } = new Dictionary<string, List<List<MapPoint>>>();
    private static MapPoint _lastStartPoint;

    public static void InvalidateCache()
    {
        _lastStartPoint = null;
    }

    public static void UpdateBestPath()
    {
        var runState = RouteSuggestMod.RunState;
        if (runState == null) return;

        var startPoint = runState.CurrentMapPoint ?? runState.Map?.StartingMapPoint;
        if (startPoint == null) return;

        // Simple caching: If we haven't moved and config wasn't saved, don't recalculate
        if (_lastStartPoint == startPoint) return;
        _lastStartPoint = startPoint;

        CalculatedPaths.Clear();
        foreach (var config in ConfigManager.PathConfigs)
        {
            if (!config.Enabled) continue;
            
            var paths = FindOptimalPaths(startPoint, config);
            if (paths.Count > 0)
            {
                CalculatedPaths[config.Name] = paths;
            }
        }
    }

    private class DpMemoState
    {
        public List<(MapPoint Node, string Key)> Predecessors = new List<(MapPoint, string)>();
    }

    private static List<List<MapPoint>> FindOptimalPaths(MapPoint startPoint, PathConfig config)
    {
        if (startPoint == null) return new List<List<MapPoint>>();

        var trackedTypes = config.TargetCounts.Keys.ToList();
        var dp = new Dictionary<MapPoint, Dictionary<string, DpMemoState>>();
        var queue = new Queue<MapPoint>();
        var inQueue = new HashSet<MapPoint>();

        queue.Enqueue(startPoint);
        inQueue.Add(startPoint);

        var startCounts = new int[trackedTypes.Count];
        int startIdx = trackedTypes.IndexOf(startPoint.PointType);
        if (startIdx >= 0) startCounts[startIdx] = 1;

        string startKey = trackedTypes.Count > 0 ? string.Join(",", startCounts) : "0";
        dp[startPoint] = new Dictionary<string, DpMemoState> { { startKey, new DpMemoState() } };

        var bossNodes = new List<MapPoint>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            inQueue.Remove(current);

            if (current.PointType == MapPointType.Boss && !bossNodes.Contains(current)) bossNodes.Add(current);
            if (current.Children == null || current.Children.Count == 0) continue;

            var currentStates = dp[current];
            foreach (var child in current.Children)
            {
                if (!dp.ContainsKey(child)) dp[child] = new Dictionary<string, DpMemoState>();
                int childIdx = trackedTypes.IndexOf(child.PointType);

                foreach (var parentKey in currentStates.Keys)
                {
                    var parentCounts = trackedTypes.Count > 0 ? parentKey.Split(',').Select(int.Parse).ToArray() : new int[0];
                    if (childIdx >= 0 && parentCounts.Length > 0) parentCounts[childIdx]++;
                    
                    string childKey = trackedTypes.Count > 0 ? string.Join(",", parentCounts) : "0";
                    if (!dp[child].ContainsKey(childKey)) dp[child][childKey] = new DpMemoState();

                    var pred = (current, parentKey);
                    if (!dp[child][childKey].Predecessors.Contains(pred)) dp[child][childKey].Predecessors.Add(pred);
                }

                if (!inQueue.Contains(child))
                {
                    queue.Enqueue(child);
                    inQueue.Add(child);
                }
            }
        }

        int bestScore = int.MinValue;
        var bestBossStates = new List<(MapPoint boss, string key)>();

        foreach (var boss in bossNodes)
        {
            if (dp.ContainsKey(boss))
            {
                foreach (var bossKey in dp[boss].Keys)
                {
                    var counts = trackedTypes.Count > 0 ? bossKey.Split(',').Select(int.Parse).ToArray() : new int[0];
                    int score = 0;
                    for (int i = 0; i < trackedTypes.Count; i++)
                    {
                        int target = config.TargetCounts[trackedTypes[i]];
                        int actual = counts[i];
                        if (actual < target)
                        {
                            int diff = target - actual;
                            score -= (diff * diff * 10);
                        }
                        else
                        {
                            score += (actual - target) * 2;
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestBossStates.Clear();
                        bestBossStates.Add((boss, bossKey));
                    }
                    else if (score == bestScore)
                    {
                        bestBossStates.Add((boss, bossKey));
                    }
                }
            }
        }

        var optimalPaths = new List<List<MapPoint>>();
        foreach (var bestBossState in bestBossStates)
        {
            var pathsToBuild = new List<List<MapPoint>>();
            BacktrackPaths(bestBossState.boss, bestBossState.key, dp, new List<MapPoint>(), pathsToBuild);
            foreach (var path in pathsToBuild)
            {
                path.Reverse();
                optimalPaths.Add(path);
            }
        }

        optimalPaths.Sort((a, b) =>
        {
            int minLen = Math.Min(a.Count, b.Count);
            for (int i = 0; i < minLen; i++)
            {
                int cmp = a[i].coord.CompareTo(b[i].coord);
                if (cmp != 0) return cmp;
            }
            return a.Count.CompareTo(b.Count);
        });

        if (ConfigManager.CurrentHighlightType == HighlightType.One && optimalPaths.Count > 1)
        {
            return new List<List<MapPoint>> { optimalPaths[0] };
        }

        return optimalPaths;
    }

    private static void BacktrackPaths(MapPoint current, string key, Dictionary<MapPoint, Dictionary<string, DpMemoState>> dp, List<MapPoint> currentPath, List<List<MapPoint>> allPaths)
    {
        currentPath.Add(current);
        var state = dp[current][key];
        if (state.Predecessors.Count == 0)
        {
            allPaths.Add(new List<MapPoint>(currentPath));
        }
        else
        {
            foreach (var pred in state.Predecessors) BacktrackPaths(pred.Node, pred.Key, dp, currentPath, allPaths);
        }
        currentPath.RemoveAt(currentPath.Count - 1);
    }
}
