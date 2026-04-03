import re

# Read both files
with open('README.md', 'r', encoding='utf-8') as f:
    readme = f.read()

with open('README-EN.md', 'r', encoding='utf-8') as f:
    readme_en = f.read()

# --- Fixing README.md ---

# Fix top-level features list (3 to 5 default routes, weighting => ranges)
readme = re.sub(
    r'- \*\*视觉高亮\*\*：默认提供三条路线高亮：.*?- \*\*智能评分\*\*：可以为不同的游玩风格配置基于目标的权重。',
    r'- **视觉高亮**：默认提供五条路线高亮：\n  - **绿色**：安全路线 (避开精英)\n  - **红色**：激进路线 (优先挑战精英)\n  - **黄色**：问号路线 (优先前往未知地点)\n  - **洋红色**：首领速通 (避开精英、火堆和怪物)\n  - **青色**：最大收益 (尽可能多地获取精英、宝箱和商店)\n- **智能评分**：可以为不同的游玩风格配置特定的各类型房间目标数量范围 (Min/Max)。',
    readme, flags=re.DOTALL
)

readme = readme.replace('设置各房间类型的目标计分权重', '设置各房间类型的目标数量范围 (Min/Max)')

# Replace the 3 default path examples with 5 default path examples
readme = re.sub(
    r'默认情况下，会计算以下三条基于目标的路线：.*?(?=\n\n当路线共享同一条边时，它们会)',
    r'默认情况下，会计算以下五条基于目标的路线：\n\n### 安全 (绿色)\n\n尽可能减少艰难的战斗，让旅程更安全：\n\n- **精英 (Elite)**: 目标范围 0 - 0 (完全避开精英)\n\n### 激进 (红色)\n\n优先考虑战斗奖励和高价值目标：\n\n- **精英 (Elite)**: 目标范围 15 - 15 (尽可能多打精英)\n\n### 问号 (黄色)\n\n优先考虑地图探索和随机事件：\n\n- **未知 (Unknown)**: 目标范围 15 - 15 (尽可能多踩问号)\n\n### 首领速通 (洋红色)\n\n直奔首领，尽可能避开精英、火堆和怪物：\n\n- **精英 (Elite)**: 目标范围 0 - 0\n- **火堆 (RestSite)**: 目标范围 0 - 0\n- **怪物 (Monster)**: 目标范围 0 - 0\n\n### 最大收益 (青色)\n\n追求资源最大化，尽可能多拿精英、宝箱和商店：\n\n- **精英 (Elite)**: 目标范围 15 - 15\n- **宝箱 (Treasure)**: 目标范围 10 - 10\n- **商店 (Shop)**: 目标范围 5 - 5',
    readme, flags=re.DOTALL
)

# Fix Control Panel GUI text
readme = re.sub(
    r'- \*\*评分权重 \(Scoring Weights\)\*\*: 设置每种房间类型权重的滑块。.*?- 0 = 中立',
    r'- **目标范围 (Scoring Ranges)**: 调整每种房间类型的目标数量上限 (Max) 和下限 (Min)。\n    - 偏离设定范围时路线会受到评分惩罚',
    readme, flags=re.DOTALL
)
readme = readme.replace('**评分权重 (Scoring Weights)**: 设置每种房间类型权重的滑块', '**目标范围 (Scoring Ranges)**: 设置各类型房间上下限范围')
readme = re.sub(
    r'- 正数 = 偏好该房间类型.*? 0 = 中立',
    r'- 偏离目标范围上下限会受到惩罚得分',
    readme, flags=re.DOTALL
)

# Replace old scoring weight JSON example
if '"scoring_weights": {' in readme and '"Elite": 0' in readme:
    readme = re.sub(
        r'"scoring_weights": \{\s*"Elite": 0\s*\}.*?"scoring_weights": \{\s*"Unknown": 15\s*\}',
        r'"scoring_weights": {\n                                "Elite": {"min": 0, "max": 0}\n                        }\n                },\n                {\n                        "name": "Aggressive (Red)",\n                        "color": "#FF0000",\n                        "priority": 50,\n                        "enabled": true,\n                        "scoring_weights": {\n                                "Elite": {"min": 15, "max": 15}\n                        }\n                },\n                {\n                        "name": "Question marks (Yellow)",\n                        "color": "#FFFF00",\n                        "priority": 75,\n                        "enabled": true,\n                        "scoring_weights": {\n                                "Unknown": {"min": 15, "max": 15}\n                        }',
        readme, flags=re.DOTALL
    )

# parameter descriptions near very bottom
readme = readme.replace('各节点种类的目标数量（目前使用平方惩罚误差，使搜索尽量贴近目标个数）', '各节点种类的目标数量上下限（使用 min 和 max 字段）')
readme = re.sub(
    r'"Elite": 15',
    r'"Elite": {"min": 15, "max": 15}',
    readme
)

# --- Fixing README-EN.md ---

# Fix top-level features list
readme_en = re.sub(
    r'- \*\*Visual highlighting\*\* with three default routes:.*?- \*\*Smart scoring\*\*: Configurable target-based weights for different playstyles.',
    r'- **Visual highlighting** with five default routes:\n  - **Green**: Safe path (avoids Elites)\n  - **Red**: Aggressive path (prioritizes Elites)\n  - **Yellow**: Question marks path (prioritizes Unknown locations)\n  - **Magenta**: Boss Rush (avoids Elites, RestSites, and Monsters)\n  - **Cyan**: Max Rewards (prioritizes Elites, Treasures, and Shops)\n- **Smart scoring**: Configurable target-based ranges (Min/Max limits) for different playstyles.',
    readme_en, flags=re.DOTALL
)

# text changes
readme_en = readme_en.replace('Adjust target counts for each room type', 'Adjust target ranges (Min/Max) for each room type')

# Replace the 3 default paths with 5 default paths
readme_en = re.sub(
    r'By default, the following three target-based routes are calculated:.*?(?=\n\nWhen paths share an edge)',
    r'By default, the following five target-based routes are calculated:\n\n### Safe (Green)\n\nMinimizes tough encounters for a safer journey:\n\n- **Elite**: Target range 0 - 0 (avoids Elites)\n\n### Aggressive (Red)\n\nPrioritizes combat rewards and high-value targets:\n\n- **Elite**: Target range 15 - 15\n\n### Question marks (Yellow)\n\nPrioritizes map exploration and random events:\n\n- **Unknown**: Target range 15 - 15\n\n### Boss Rush (Magenta)\n\nMinimize detours, aiming for the boss:\n\n- **Elite**: Target range 0 - 0\n- **RestSite**: Target range 0 - 0\n- **Monster**: Target range 0 - 0\n\n### Max Rewards (Cyan)\n\nMaximize resources along the path:\n\n- **Elite**: Target range 15 - 15\n- **Treasure**: Target range 10 - 10\n- **Shop**: Target range 5 - 5',
    readme_en, flags=re.DOTALL
)

# Replace UI explanation
readme_en = re.sub(
    r'- \*\*Scoring Weights\*\*: Sliders for each room type\s+- Positive = prefer this room type\s+- Negative = avoid this room type\s+- Zero = neutral',
    r'- **Scoring Ranges**: Min/Max bounds for each room type target\n    - Penalizes paths that fall outside the Target Range',
    readme_en, flags=re.DOTALL
)

# Replace exact JSON format string
if '"scoring_weights": {' in readme_en and '"Elite": 0' in readme_en:
    readme_en = re.sub(
        r'"scoring_weights": \{\s*"Elite": 0\s*\}.*?"scoring_weights": \{\s*"Unknown": 15\s*\}',
        r'"scoring_weights": {\n                                "Elite": {"min": 0, "max": 0}\n                        }\n                },\n                {\n                        "name": "Aggressive (Red)",\n                        "color": "#FF0000",\n                        "priority": 50,\n                        "enabled": true,\n                        "scoring_weights": {\n                                "Elite": {"min": 15, "max": 15}\n                        }\n                },\n                {\n                        "name": "Question marks (Yellow)",\n                        "color": "#FFFF00",\n                        "priority": 75,\n                        "enabled": true,\n                        "scoring_weights": {\n                                "Unknown": {"min": 15, "max": 15}\n                        }',
        readme_en, flags=re.DOTALL
    )

# descriptions
readme_en = readme_en.replace('Target counts for each node type', 'Target count range for each node type (uses min and max)')
readme_en = readme_en.replace('"Elite": 15', '"Elite": {"min": 15, "max": 15}')

with open('README.md', 'w', encoding='utf-8') as f:
    f.write(readme)

with open('README-EN.md', 'w', encoding='utf-8') as f:
    f.write(readme_en)

print("Updates completed successfully.")
