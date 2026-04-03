<div align="center">
  <a href="README-EN.md">English</a> | <a href="README.md">��������</a>
</div>
# RouteSuggest - Slay the Spire 2 Mod

![](screenshot.png)

A mod for Slay the Spire 2 that suggests the optimal path through the map and highlights it on the map screen.

_Based on the original STS2RouteSuggest mod by Jiajie Chen @jiegec, modified to use a Dynamic Programming algorithm and new target-based route matching._

**Supported game versions:** v0.99.1 and v0.100.0 (public beta)

## Features

- **Advanced Pathing**: Uses a high-performance Dynamic Programming (DP) algorithm for optimal speed and matched closest path robustness.
- **Visual highlighting** with five default routes:
  - **Green**: Safe path (avoids Elites)
  - **Red**: Aggressive path (prioritizes Elites)
  - **Yellow**: Question marks path (prioritizes Unknown locations)
  - **Magenta**: Boss Rush (avoids Elites, RestSites, and Monsters)
  - **Cyan**: Max Rewards (prioritizes Elites, Treasures, and Shops)
- **Smart scoring**: Configurable target-based ranges (Min/Max limits) for different playstyles.
- **Improved Config System**: Auto-generation of default JSON configuration and better error handling.
- **GUI Configuration**: Full in-game configuration by pressing F10.
- **Manual Configuration**: Direct JSON configuration for advanced users

## Demo & Usage

If you have installed the F10 menu, you can access the "Mod Settings" menu in-game to:

- Enable/disable specific paths (such as the default disabled "Boss Rush" and "Max Rewards" paths)
- Tweak path highlighting colors and priorities
- Adjust target ranges (Min/Max) for each room type

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

By default, the following five target-based routes are calculated:

### Safe (Green)

Minimizes tough encounters for a safer journey:

- **Elite**: Target range 0 - 0 (avoids Elites)

### Aggressive (Red)

Prioritizes combat rewards and high-value targets:

- **Elite**: Target range 15 - 15

### Question marks (Yellow)

Prioritizes map exploration and random events:

- **Unknown**: Target range 15 - 15

### Boss Rush (Magenta)

Minimize detours, aiming for the boss:

- **Elite**: Target range 0 - 0
- **RestSite**: Target range 0 - 0
- **Monster**: Target range 0 - 0

### Max Rewards (Cyan)

Maximize resources along the path:

- **Elite**: Target range 15 - 15
- **Treasure**: Target range 10 - 10
- **Shop**: Target range 5 - 5

When paths share an edge, they overlap on the map screen according to rendering priority, or blend into other colors (like bright gold).

## Configuration

### GUI Settings (Recommended)

With the F10 Menu GUI, you can:

- **General Settings**:
  - **Highlight Type**: Choose to highlight one optimal path or all paths with optimal score
    - **One**: Pick one path from among optimal paths
    - **All**: Highlight all paths tied for the best score
- **Configure each path**:
  - **Enabled**: Toggle to enable/disable this path (disabled paths are not calculated or shown)
  - **Name**: Identifier for the path
  - **Color**: Enter hex color code (e.g., `#FFD700` for gold, `#FF0000` for red)
  - **Priority**: Slider to set rendering priority (higher = renders on top when paths overlap)
  - **Scoring Ranges**: Min/Max bounds for each room type target
    - Penalizes paths that fall outside the Target Range
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
				"Elite": { "min": 0, "max": 0 }
			}
		},
		{
			"name": "Aggressive (Red)",
			"color": "#FF0000",
			"priority": 50,
			"enabled": true,
			"scoring_weights": {
				"Elite": { "min": 15, "max": 15 }
			}
		},
		{
			"name": "Question marks (Yellow)",
			"color": "#FFFF00",
			"priority": 75,
			"enabled": true,
			"scoring_weights": {
				"Unknown": { "min": 15, "max": 15 }
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
| `name`            | String  | Name of the path (for F10 view).                                                                    |
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

## Detailed Configuration Instructions

You can modify the mod configuration via the in-game F10 Menu or by editing `RouteSuggestConfig.json`. If the file is missing or invalid, the mod will automatically generate a default configuration on startup.

### Configuration Options:

- **highlight_type**: `One` (highlight a single best route) or `All` (highlight all best routes with the same score).
- **path_configs**: List of path configuration items for different strategies.

Each path config contains:

- **name**: Path name shown in logs and UI.
- **color**: Hex color string (e.g. `#FF0000`).
- **priority**: Render priority, higher numbers draw on top.
- **enabled**: Whether to display this path.
- **scoring_weights**: (Optional) Target count ranges for each room type. E.g. `"Elite": {"min": 15, "max": 15}` means combat as many elites as possible. The algorithm calculates penalty based on the squared deviation from target counts, finding the best route matching your preference.

**Example config:**

```json
{
	"schema_version": 3,
	"highlight_type": "One",
	"path_configs": [
		{
			"name": "Safe (Green) / Safe",
			"color": "#00FF00",
			"priority": 100,
			"enabled": true,
			"scoring_weights": {
				"Elite": { "min": 0, "max": 0 }
			}
		}
	]
}
```

## Demo Video / GIF

![Demo Video or GIF](https://raw.githubusercontent.com/14qwq14/Better-Smart-Route/master/screenshot.png) (Pending GIF update)

## Detailed Configuration Instructions

You can modify the mod configuration via the in-game F10 Menu or by editing `RouteSuggestConfig.json`. If the file is missing or invalid, the mod will automatically generate a default configuration on startup.

### Configuration Options:

- **highlight_type**: `One` (highlight a single best route) or `All` (highlight all best routes with the same score).
- **path_configs**: List of path configuration items for different strategies.

Each path config contains:

- **name**: Path name shown in logs and UI.
- **color**: Hex color string (e.g. `#FF0000`).
- **priority**: Render priority, higher numbers draw on top.
- **enabled**: Whether to display this path.
- **scoring_weights**: (Optional) Target count ranges for each room type. E.g. `"Elite": {"min": 15, "max": 15}` means combat as many elites as possible. The algorithm calculates penalty based on the squared deviation from target counts, finding the best route matching your preference.

**Example config:**

```json
{
	"schema_version": 3,
	"highlight_type": "One",
	"path_configs": [
		{
			"name": "Safe (Green) / Safe",
			"color": "#00FF00",
			"priority": 100,
			"enabled": true,
			"scoring_weights": {
				"Elite": { "min": 0, "max": 0 }
			}
		}
	]
}
```

## Demo Video / GIF

![Demo Video or GIF](https://raw.githubusercontent.com/14qwq14/Better-Smart-Route/master/screenshot.png) (Pending GIF update)

## Detailed Configuration Instructions

You can modify the mod configuration via the in-game F10 Menu or by editing `RouteSuggestConfig.json`. If the file is missing or invalid, the mod will automatically generate a default configuration on startup.

### Configuration Options:

- **highlight_type**: `One` (highlight a single best route) or `All` (highlight all best routes with the same score).
- **path_configs**: List of path configuration items for different strategies.

Each path config contains:

- **name**: Path name shown in logs and UI.
- **color**: Hex color string (e.g. `#FF0000`).
- **priority**: Render priority, higher numbers draw on top.
- **enabled**: Whether to display this path.
- **scoring_weights**: (Optional) Target count ranges for each room type. E.g. `"Elite": {"min": 15, "max": 15}` means combat as many elites as possible. The algorithm calculates penalty based on the squared deviation from target counts, finding the best route matching your preference.

**Example config:**

```json
{
	"schema_version": 3,
	"highlight_type": "One",
	"path_configs": [
		{
			"name": "Safe (Green) / Safe",
			"color": "#00FF00",
			"priority": 100,
			"enabled": true,
			"scoring_weights": {
				"Elite": { "min": 0, "max": 0 }
			}
		}
	]
}
```
