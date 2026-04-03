<div align="center">
  <a href="README-EN.md">English</a> | <a href="README.md">简体中文</a>
</div>
# RouteSuggest - Slay the Spire 2 Mod

![](screenshot.png)

A mod for Slay the Spire 2 that suggests the optimal path through the map and highlights it on the map screen.

_Based on the original STS2RouteSuggest mod by Jiajie Chen @jiegec, modified to use a Dynamic Programming algorithm and new target-based route matching._

**Supported game versions:** v0.99.1 and v0.100.0 (public beta)

## Features

- **Advanced Pathing**: Uses a high-performance Dynamic Programming (DP) algorithm for optimal speed and matched closest path robustness.
- **Visual highlighting** with three default routes:
  - **Green**: Safe path (avoids Elites)
  - **Red**: Aggressive path (prioritizes Elites)
  - **Yellow**: Question marks path (prioritizes Unknown locations)
- **Smart scoring**: Configurable target-based weights for different playstyles.
- **GUI Configuration**: Full in-game configuration via ModConfig (optional)
- **Manual Configuration**: Direct JSON configuration for advanced users

## Installation

1. Download the latest release from [GitHub releases](https://github.com/14qwq14/Better-Smart-Route).
2. Extract the mod files to your Slay the Spire 2 mods folder (`mods` folder should reside in the same folder as the game executable):
   - **Windows**: `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\`
   - **macOS**: `~/Library/Application\ Support/Steam/steamapps/common/Slay\ the\ Spire\ 2/SlayTheSpire2.app/Contents/MacOS/mods/`
   - **Linux**: `~/.steam/steam/steamapps/common/Slay\ the\ Spire\ 2/mods`
3. Launch Slay the Spire 2 - the mod will load automatically

## Building from Source

### Prerequisites

- .NET 9.0 SDK or later
- Godot 4.5.1 with Mono support
- Slay the Spire 2 (for the sts2.dll reference)

### Build Steps

```bash
# Clone the repository
git clone https://github.com/14qwq14/Better-Smart-Route
cd Better-Smart-Route

# Build the mod
./build.ps1
```

RouteSuggest uses a Dynamic Programming (DP) algorithm to calculate optimal paths based on target values and scoring. This provides better performance and matched closest path robustness compared to basic search methods.

By default, the following three target-based routes are calculated:

### Safe (Green)

Minimizes tough encounters for a safer journey:

- **Elite**: 0 weight (avoids Elites)

### Aggressive (Red)

Prioritizes combat rewards and high-value targets:

- **Elite**: +15 weight

### Question marks (Yellow)

Prioritizes map exploration and random events:

- **Unknown**: +15 weight

When paths share an edge, they overlap on the map screen according to rendering priority, or blend into other colors (like bright gold).

## Configuration

### GUI Settings (Recommended)

RouteSuggest optionally integrates with [**ModConfig**](https://github.com/xhyrzldf/ModConfig-STS2). When ModConfig is installed, RouteSuggest appears in the game's **Settings > Mods** menu. If ModConfig is not installed, the mod still works normally, but you'll need to edit the JSON configuration file manually (see below).

With ModConfig GUI, you can:

- **General Settings**:
  - **Highlight Type**: Choose to highlight one optimal path or all paths with optimal score
    - **One**: Pick one path from among optimal paths
    - **All**: Highlight all paths tied for the best score
- **Configure each path**:
  - **Enabled**: Toggle to enable/disable this path (disabled paths are not calculated or shown)
  - **Name**: Identifier for the path
  - **Color**: Enter hex color code (e.g., `#FFD700` for gold, `#FF0000` for red)
  - **Priority**: Slider to set rendering priority (higher = renders on top when paths overlap)
  - **Scoring Weights**: Sliders for each room type
    - Positive = prefer this room type
    - Negative = avoid this room type
    - Zero = neutral
- **Add New Path**: Slider to add a new path (slide to 1)
- **Remove Path**: Each path has a slider to remove it (0=keep, 1=remove)
- **Reset to Defaults**: Slider to reset all paths to default configuration
- **Changes are saved automatically** to the config file location (see below for details)

### Manual JSON Configuration

Alternatively, you can customize the path types by manually editing `RouteSuggestConfig.json`:

- **Existing users**: If you already have a config file at `mods/RouteSuggestConfig.json`, it will continue to be used (no migration needed)
- **New users**: The config will be saved alongside `RouteSuggest.dll` (found recursively in the mods folder). If the DLL cannot be found, it falls back to `mods/RouteSuggestConfig.json`
- **Note**: Config is saved to the same location where it was read from. The mod will not automatically migrate the config file to a different location.

```json
{
	"schema_version": 3,
	"highlight_type": "One",
	"path_configs": [
		{
			"name": "Safe (Green)",
			"color": "#00FF00",
			"priority": 100,
			"enabled": true,
			"scoring_weights": {
				"Elite": 0
			}
		},
		{
			"name": "Aggressive (Red)",
			"color": "#FF0000",
			"priority": 50,
			"enabled": true,
			"scoring_weights": {
				"Elite": 15
			}
		},
		{
			"name": "Question marks (Yellow)",
			"color": "#FFFF00",
			"priority": 75,
			"enabled": true,
			"scoring_weights": {
				"Unknown": 15
			}
		}
	]
}
```

- **enabled**: Set to `false` to disable a path (disabled paths are not calculated or shown)
- **color**: Hex color code (e.g., `#FFD700` for gold, `#FF0000` for red)
- **priority**: Higher values render on top when paths overlap
- **scoring_weights**: Integer values for each room type (positive = preferred, negative = avoid)

| Field             | Type    | Description                                                                                         |
| :---------------- | :------ | :-------------------------------------------------------------------------------------------------- |
| `schema_version`  | Integer | Configuration schema version. Currently `3`.                                                        |
| `highlight_type`  | String  | Mode: `One` (highlights a single best route) or `All` (highlights all tied top routes).             |
| `path_configs`    | Array   | List of configured paths. If missing, default paths will be used.                                   |
| `enabled`         | Boolean | Calculates and displays this path when set to `true`.                                               |
| `name`            | String  | Name of the path (for ModConfig view).                                                              |
| `color`           | String  | Hex color string.                                                                                   |
| `priority`        | Integer | Higher values render on top of lower ones when paths overlap.                                       |
| `scoring_weights` | Object  | Room type weights. Positive values attract, negative values repel. Missing values are 0 by default. |

**Available room types (for `scoring_weights`):**

- `RestSite`
- `Treasure`
- `Shop`
- `Monster`
- `Elite`
- `Unknown`

If the config file is missing or invalid, default path configs are used.

