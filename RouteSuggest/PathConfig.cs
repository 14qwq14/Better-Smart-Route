using Godot;
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Map;

namespace RouteSuggest;

public enum HighlightType
{
    One,
    All
}

public class PathConfig
{
    public string Name { get; set; }
    public Color Color { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<MapPointType, int> TargetCounts { get; set; } = new Dictionary<MapPointType, int>();

    public int CalculateScore(List<MapPoint> path)
    {
        if (path == null) return -10000;

        var counts = new Dictionary<MapPointType, int>();
        foreach (var point in path)
        {
            if (!counts.ContainsKey(point.PointType)) counts[point.PointType] = 0;
            counts[point.PointType]++;
        }

        int score = 0;
        foreach (var kvp in TargetCounts)
        {
            counts.TryGetValue(kvp.Key, out int actual);
            int target = kvp.Value;
            
            if (actual < target)
            {
                // Penalize heavily for missing required nodes
                int diff = target - actual;
                score -= (diff * diff * 10);
            }
            else
            {
                // Slightly reward getting more than the minimum target so it prefers paths with more
                score += (actual - target) * 2;
            }
        }
        
        // Add a small tie-breaker for path length (longer paths slightly preferred if equal targets met)
        score += path.Count;
        
        return score;
    }
}
