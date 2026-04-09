<div align="center">
  <a href="README-EN.md">English</a> | <a href="README.md">简体中文</a>
</div>

# RouteSuggest（杀戮尖塔2 路线推荐模组）

![RouteSuggest Screenshot](screenshot.png)

根据你的目标（精英、问号、商店等房间数量区间）自动计算地图最优路线，并在地图界面高亮显示。

## 兼容性

- **模组版本**：`2.0`
- **支持游戏版本**：`v0.99.1`、`v0.100.0`（公开测试版）
- **开发运行时**：Godot 4.5.1（Mono） + .NET 9

## 功能概览

- **动态规划 (DP) 路线计算**：按目标区间匹配最优路线，性能稳定。
- **多路线高亮**：支持按配置高亮单条或多条最优路线。
- **优先级渲染**：路线重叠时按 `priority` 叠加显示（高优先级在上层）。
- **手动 JSON 配置**：高级用户可直接编辑配置文件。

## 默认策略（开箱即用）

> 说明：以下为常用策略示例，可在 JSON 中修改。

| 路线名                         | 颜色      | 优先级 | 目标区间示例     |
| :----------------------------- | :-------- | :----: | :--------------- |
| Safe (Green) / 安全            | `#00FF00` |  100   | `Elite: 0-0`     |
| Aggressive (Red) / 激进        | `#FF0000` |   50   | `Elite: 15-15`   |
| Question marks (Yellow) / 未知 | `#FFFF00` |   75   | `Unknown: 15-15` |

## 安装（玩家）

1. 从 GitHub Releases 下载发布包（zip）。
2. 解压后将模组文件放入游戏 `mods` 目录。
3. 启动游戏，模组会自动加载。

常见 `mods` 路径：

- **Windows**：`C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\`
- **macOS**：`~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/`
- **Linux**：`~/.steam/steam/steamapps/common/Slay the Spire 2/mods/`

## 配置说明

### 手动编辑 JSON

配置文件名：`RouteSuggestConfig.json`

路径策略：

1. 优先放在模组 DLL 同目录。
2. 若获取失败，回退到用户目录 `mods/RouteSuggestConfig.json`。

示例：

```json
{
	"schema_version": 5,
	"highlight_type": "One",
	"max_paths_per_config": 3,
	"path_configs": [
		{
			"name": "Safe (Green) / 安全",
			"color": "#00FF00",
			"priority": 100,
			"enabled": true,
			"target_counts": {
				"Elite": { "min": 0, "max": 0 }
			}
		}
	]
}
```

`target_counts`（兼容旧字段 `scoring_weights`）支持的房间类型：

- `RestSite`
- `Treasure`
- `Shop`
- `Monster`
- `Elite`
- `Unknown`

## 从源码构建（开发者）

### 环境要求

- .NET 9.0 SDK
- Godot 4.5.1（Mono）
- 本地《杀戮尖塔2》安装（用于读取 `sts2.dll`）

### Windows

- 可设置环境变量：
  - `GODOT_PATH`（Godot 可执行文件）
  - `STS2_PATH`（游戏目录）
- 运行 `build.ps1`

### Linux / macOS

- 可设置环境变量：
  - `GODOT_PATH`
  - `STS2_PATH`
- 运行 `build.sh`

构建脚本会自动：

1. 复制 `sts2.dll`
2. 调用 Godot 编译 C# 方案
3. 生成 `dist/`
4. 打包 `RouteSuggest-v<version>.zip`

## 项目结构（精简后）

```text
RouteSuggest/
  ConfigManager.cs      # 配置读写、默认配置
  MapHighlighter.cs     # 地图高亮渲染与地图事件绑定
  PathConfig.cs         # 路径配置与评分模型
  RouteCalculator.cs    # DP 路径搜索
  RouteSuggestMod.cs    # 模组入口与事件调度
build.ps1 / build.sh    # 构建脚本
install.sh              # 安装脚本（Linux/macOS）
mod_manifest.json       # 模组元数据
```

## 许可证

本项目使用 `LICENSE` 中声明的许可证。
