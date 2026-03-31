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

## Support

If you find a bug, have a feature request, or something isn't working as expected, feel free to [open an issue](../../issues). I'll take a look when I can.

Custom plugins are available on request, potentially for a small fee depending on scope. Reach out via an issue or at access@shaedy.de.

> Note: These repos may not always be super active since most of my work happens in private repositories. But issues and requests are still welcome.

## Donate

If you want to support my work: [ko-fi.com/shaedy](https://ko-fi.com/shaedy)
