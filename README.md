# shaedy MapChooser

A CounterStrikeSharp plugin that handles map voting, Rock the Vote, and automatic map rotation.

## Features

- Rock the Vote (RTV) system with configurable vote percentage
- Timed vote with map pool selection
- Per-map player count limits (min/max players)
- Workshop map support via map IDs
- Weight-based random map selection
- Automatic map rotation on empty server
- Prevents the same map from appearing in consecutive votes

## Commands

| Command | Description |
|---------|-------------|
| `!rtv` | Rock the Vote to start a map vote |
| `!nextmap` | Shows what the next map will be |

## Installation

Drop the plugin folder into your CounterStrikeSharp `plugins` directory.

## Configuration

The config is auto-generated on first run. Key settings:

| Field | Description | Default |
|-------|-------------|---------|
| `rtv_percentage` | Percentage of players needed to trigger RTV | `0.6` |
| `vote_duration` | How long the vote lasts (seconds) | `25` |
| `vote_rounds_before_end` | Rounds before match end to trigger a vote | `3` |
| `empty_map_rotation_cooldown` | Seconds before rotating maps on empty server | `120` |
| `maps` | Array of map entries with `name`, `workshop_id`, `min_players`, `max_players`, `weight` | - |
