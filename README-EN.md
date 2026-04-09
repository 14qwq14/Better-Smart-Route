<div align="center">
  <a href="README-EN.md">English</a> | <a href="README.md">简体中文</a>
</div>

# RouteSuggest (Slay the Spire 2 Route Suggestion Mod)

![RouteSuggest Screenshot](screenshot.png)

RouteSuggest computes optimal map routes based on your target room-count ranges (Elites, Unknowns, Shops, etc.) and highlights them directly on the map screen.

## Compatibility

- **Mod version**: `2.0`
- **Supported game versions**: `v0.99.1`, `v0.100.0` (public beta)
- **Development runtime**: Godot 4.5.1 (Mono) + .NET 9

## Feature Highlights

- **Dynamic Programming (DP) route calculation** for stable and efficient path selection.
- **Single/all optimal highlighting** (`One` / `All`).
- **Priority-based rendering** when routes overlap.
- **Manual JSON configuration** for advanced users.

## Default Strategies

> The table below lists common strategy examples editable via JSON.

| Route                          | Color     | Priority | Example target ranges |
| :----------------------------- | :-------- | :------: | :-------------------- |
| Safe (Green) / 安全            | `#00FF00` |   100    | `Elite: 0-0`          |
| Aggressive (Red) / 激进        | `#FF0000` |    50    | `Elite: 15-15`        |
| Question marks (Yellow) / 未知 | `#FFFF00` |    75    | `Unknown: 15-15`      |

## Installation

1. Download the latest release zip from GitHub Releases.
2. Extract and copy mod files into the game's `mods` folder.
3. Launch the game; the mod is loaded automatically.

Typical `mods` paths:

- **Windows**: `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\`
- **macOS**: `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/`
- **Linux**: `~/.steam/steam/steamapps/common/Slay the Spire 2/mods/`

## Configuration

### Edit JSON manually

Config file: `RouteSuggestConfig.json`

Lookup strategy:

1. Prefer the directory containing the mod DLL.
2. Fallback to `mods/RouteSuggestConfig.json` under user data.

Example:

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

Supported room types in `target_counts` (legacy `scoring_weights` is still accepted):

- `RestSite`
- `Treasure`
- `Shop`
- `Monster`
- `Elite`
- `Unknown`

## Build from Source

### Prerequisites

- .NET 9.0 SDK
- Godot 4.5.1 (Mono)
- Local Slay the Spire 2 install (for `sts2.dll`)

### Windows

Optional environment variables:

- `GODOT_PATH` (Godot executable)
- `STS2_PATH` (game directory)

Run `build.ps1`.

### Linux / macOS

Optional environment variables:

- `GODOT_PATH`
- `STS2_PATH`

Run `build.sh`.

Build scripts automatically:

1. Copy `sts2.dll`
2. Build Godot C# solution
3. Generate `dist/`
4. Package `RouteSuggest-v<version>.zip`

## Project Structure 

```text
RouteSuggest/
  ConfigManager.cs      # config read/write + defaults
  MapHighlighter.cs     # map highlight rendering + map event hooks
  PathConfig.cs         # strategy model and scoring
  RouteCalculator.cs    # DP route search
  RouteSuggestMod.cs    # mod entry + lifecycle events
build.ps1 / build.sh    # build scripts
install.sh              # install helper (Linux/macOS)
mod_manifest.json       # mod metadata
```

## Keep the GitHub Repository Clean

The following are generated/local artifacts and should not be committed:

- `.godot/`
- `dist/`
- `RouteSuggest-v*.zip`
- `sts2.dll`
- temporary test files (e.g. `Test.cs`, `test.py`, `tmp_script.ps1`)

`.gitignore` has been updated accordingly. Keep only source and reproducible build inputs in the repository.

## License

See `LICENSE` for license details.
