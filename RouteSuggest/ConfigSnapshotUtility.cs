using System;
using System.Collections.Generic;
using System.Text;
using MegaCrit.Sts2.Core.Map;

namespace RouteSuggest;

/// <summary>
/// 配置快照工具：提供稳定指纹构建与配置结果键生成，避免跨模块重复实现。
/// </summary>
internal static class ConfigSnapshotUtility
{
  /// <summary>
  /// 使用稳定顺序遍历目标类型，避免 Dictionary 枚举顺序导致的指纹抖动。
  /// </summary>
  private static readonly MapPointType[] StableTargetTypeOrder =
    (MapPointType[])Enum.GetValues(typeof(MapPointType));

  /// <summary>
  /// 构建配置快照指纹字符串（零哈希碰撞风险，直接字符串相等比较）。
  /// </summary>
  /// <param name="highlightType">高亮模式。</param>
  /// <param name="pathConfigs">路径配置列表。</param>
  /// <returns>可直接比较相等性的稳定指纹。</returns>
  public static string BuildFingerprint(HighlightType highlightType, IReadOnlyList<PathConfig> pathConfigs)
  {
    pathConfigs ??= Array.Empty<PathConfig>();

    var sb = new StringBuilder(pathConfigs.Count * 96 + 32);
    sb.Append("h=").Append((int)highlightType)
      .Append(";count=").Append(pathConfigs.Count);

    for (int i = 0; i < pathConfigs.Count; i++)
    {
      var config = pathConfigs[i];
      sb.Append("|cfg#").Append(i).Append('{');

      if (config == null)
      {
        sb.Append("null");
      }
      else
      {
        AppendStringPart(sb, config.Name);
        sb.Append(";en=").Append(config.Enabled ? 1 : 0)
          .Append(";pri=").Append(config.Priority)
          .Append(";rgba=")
          .Append(BitConverter.SingleToInt32Bits(config.Color.R)).Append(',')
          .Append(BitConverter.SingleToInt32Bits(config.Color.G)).Append(',')
          .Append(BitConverter.SingleToInt32Bits(config.Color.B)).Append(',')
          .Append(BitConverter.SingleToInt32Bits(config.Color.A));

        AppendTargetCounts(sb, config.TargetCounts);
      }

      sb.Append('}');
    }

    return sb.ToString();
  }

  /// <summary>
  /// 基于配置列表构建“配置对象 -> 结果键”映射，重名时自动附加序号。
  /// </summary>
  /// <param name="configs">配置集合。</param>
  /// <returns>每个配置对象对应的唯一结果键。</returns>
  public static Dictionary<PathConfig, string> BuildResultKeyMap(IEnumerable<PathConfig> configs)
  {
    var result = new Dictionary<PathConfig, string>();
    var usage = new Dictionary<string, int>(StringComparer.Ordinal);

    if (configs == null) return result;

    foreach (var config in configs)
    {
      if (config == null) continue;
      result[config] = BuildConfigResultKey(config.Name, usage);
    }

    return result;
  }

  /// <summary>
  /// 构建单个配置结果键（重名追加 " (2)", " (3)"...）。
  /// </summary>
  /// <param name="name">配置名。</param>
  /// <param name="usage">重名计数器。</param>
  /// <returns>唯一结果键。</returns>
  public static string BuildConfigResultKey(string name, Dictionary<string, int> usage)
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
  /// 将字符串按“长度+内容”方式写入指纹，避免分隔符冲突。
  /// </summary>
  private static void AppendStringPart(StringBuilder sb, string value)
  {
    value ??= string.Empty;
    sb.Append(";name=").Append(value.Length).Append(':').Append(value);
  }

  /// <summary>
  /// 以稳定顺序追加目标区间。
  /// </summary>
  private static void AppendTargetCounts(StringBuilder sb, Dictionary<MapPointType, TargetRange> targetCounts)
  {
    if (targetCounts == null || targetCounts.Count == 0)
    {
      sb.Append(";targets=0");
      return;
    }

    var written = 0;
    sb.Append(";targets=");
    foreach (var pointType in StableTargetTypeOrder)
    {
      if (!targetCounts.TryGetValue(pointType, out var range)) continue;

      sb.Append('[')
        .Append((int)pointType).Append(',')
        .Append(range.Min).Append(',')
        .Append(range.Max).Append(']');
      written++;
    }

    sb.Append("#").Append(written);
  }
}
