﻿using Godot;
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Map;

namespace RouteSuggest;

/// <summary>
/// 路径高亮显示方式：仅展示一条最佳路线或展示所有候选路线。
/// </summary>
public enum HighlightType
{
  One,
  All
}

/// <summary>
/// 每种房间类型的目标数量范围。
/// </summary>
public readonly struct TargetRange
{
  /// <summary>
  /// 目标下限。
  /// </summary>
  public int Min { get; }

  /// <summary>
  /// 目标上限。
  /// </summary>
  public int Max { get; }

  /// <summary>
  /// 初始化目标区间。
  /// </summary>
  /// <param name="min">下限。</param>
  /// <param name="max">上限。</param>
  public TargetRange(int min, int max) { Min = min; Max = max; }
}

/// <summary>
/// 单条路线策略配置（颜色、优先级、开关、目标范围等）。
/// </summary>
public class PathConfig
{
  /// <summary>
  /// 空路径评分惩罚值。
  /// </summary>
  internal const int NullPathPenalty = -10_000;

  /// <summary>
  /// 配置名称。
  /// </summary>
  public string Name { get; set; }

  /// <summary>
  /// 路线显示颜色。
  /// </summary>
  public Color Color { get; set; }

  /// <summary>
  /// 路线优先级（数值越大优先级越高）。
  /// </summary>
  public int Priority { get; set; }

  /// <summary>
  /// 是否启用该路线配置。
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// 各房间类型的目标数量区间。
  /// </summary>
  public Dictionary<MapPointType, TargetRange> TargetCounts { get; set; } = new Dictionary<MapPointType, TargetRange>();

  /// <summary>
  /// 单个房间类型计数对总分的贡献计算：落在区间奖励，偏离区间按平方惩罚。
  /// </summary>
  /// <param name="actual">实际计数。</param>
  /// <param name="target">目标区间。</param>
  /// <returns>该项分值。</returns>
  internal static int EvaluateTargetRangeScore(int actual, TargetRange target)
  {
    if (actual < target.Min)
    {
      int diff = target.Min - actual;
      return -(diff * diff * 50);
    }

    if (actual > target.Max)
    {
      int diff = actual - target.Max;
      return -(diff * diff * 50);
    }

    double mid = (target.Min + target.Max) / 2.0;
    return (int)(10 - Math.Abs(actual - mid) * 2);
  }

  /// <summary>
  /// 对一条路径打分：越贴近各房间类型目标区间分数越高。
  /// </summary>
  /// <param name="path">待评分路径。</param>
  /// <returns>路径分值。</returns>
  public int CalculateScore(List<MapPoint> path)
  {
    if (path == null) return NullPathPenalty;

    int score = 0;
    foreach (var kvp in TargetCounts)
    {
      int actual = 0;
      for (int i = 0; i < path.Count; i++)
      {
        var point = path[i];
        if (point != null && point.PointType == kvp.Key)
        {
          actual++;
        }
      }

      score += EvaluateTargetRangeScore(actual, kvp.Value);
    }

    return score;
  }
}

