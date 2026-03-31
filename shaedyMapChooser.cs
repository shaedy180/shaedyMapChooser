using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json.Serialization;

namespace ShaedyMapChooser;

public class MapInfo
{
    [JsonPropertyName("ws")]
    public bool IsWorkshop { get; set; } = false;

    [JsonPropertyName("display")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("mapid")]
    public string? WorkshopId { get; set; }

    [JsonPropertyName("minplayers")]
    public int MinPlayers { get; set; } = 0;

    [JsonPropertyName("maxplayers")]
    public int MaxPlayers { get; set; } = 64;

    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 1;
}

public class ShaedyConfig : BasePluginConfig
{
    [JsonPropertyName("RtvPercentage")]
    public float RtvPercentage { get; set; } = 0.6f;

    [JsonPropertyName("VoteDuration")]
    public int VoteDuration { get; set; } = 25;

    [JsonPropertyName("VoteRoundsBeforeEnd")]
    public int VoteRoundsBeforeEnd { get; set; } = 2;

    [JsonPropertyName("EmptyMapRotationCooldown")]
    public int EmptyMapRotationCooldown { get; set; } = 30; // minutes the server must be empty before rotation

    [JsonPropertyName("Maps")]
    public Dictionary<string, MapInfo> Maps { get; set; } = new();

    [JsonPropertyName("MapPools")]
    public Dictionary<string, Dictionary<string, MapInfo>> MapPools { get; set; } = new();
}

public class ShaedyMapChooser : BasePlugin, IPluginConfig<ShaedyConfig>
{
    public override string ModuleName => "shaedy MapChooser";
    public override string ModuleVersion => "3.3.0";
    public override string ModuleAuthor => "shaedy";

    public ShaedyConfig Config { get; set; } = new();

    private HashSet<int> _rtvVoters = new();
    private bool _isVoteInProgress = false;
    private bool _autoVoteTriggered = false;
    private bool _rtvTriggered = false;
    private bool _mapChangeScheduled = false;
    private DateTime? _mapChangeScheduledAt = null;
    private const int MAP_CHANGE_TIMEOUT_SECONDS = 60;

    private DateTime? _serverEmptySince = null;
    private KeyValuePair<string, MapInfo>? _nextMap = null;
    private Dictionary<int, string> _playerVotes = new();
    private Dictionary<string, int> _currentVotes = new();

    private string _prefix => $"{ChatColors.White}[{ChatColors.Green}shaedy-MapChooser{ChatColors.White}]";

    public void OnConfigParsed(ShaedyConfig config)
    {
        Config = config;
        Console.WriteLine($"[ShaedyMapChooser] Config loaded with {config.Maps.Count} default maps and {config.MapPools.Count} mode pools.");
    }

    public override void Load(bool hotReload)
    {
        AddCommand("css_rtv", "Rock the Vote", OnRtvCommand);
        AddCommand("css_nextmap", "Show next map", OnNextMapCommand);

        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect);
        RegisterEventHandler<EventCsWinPanelMatch>(OnMatchEnd);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            Console.WriteLine($"[ShaedyMapChooser] OnMapStart -> {mapName}. Resetting vote state.");
            ResetVoteState();
        });

        AddTimer(30.0f, CheckEmptyAndRotate, TimerFlags.REPEAT);

        if (hotReload)
        {
            ResetVoteState();
        }

        Console.WriteLine("[ShaedyMapChooser] v3.2.0 Loaded successfully.");
    }

    private void ResetVoteState()
    {
        _isVoteInProgress = false;
        _autoVoteTriggered = false;
        _rtvTriggered = false;
        _mapChangeScheduled = false;
        _mapChangeScheduledAt = null;
        _rtvVoters.Clear();
        _currentVotes.Clear();
        _playerVotes.Clear();
        _nextMap = null;
        _serverEmptySince = null;
    }

    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNextMapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (_nextMap != null)
        {
            player.PrintToChat($"{_prefix} Next map: {ChatColors.Green}{GetDisplayName(_nextMap.Value.Key, _nextMap.Value.Value)}");
        }
        else
        {
            player.PrintToChat($"{_prefix} No next map set yet. Use {ChatColors.Green}!rtv{ChatColors.White} to start a vote.");
        }
    }

    private HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
        {
            _serverEmptySince = null;
        }
        return HookResult.Continue;
    }

    private void CheckEmptyAndRotate()
    {
        // Reset stuck state after timeout
        if (_mapChangeScheduled && _mapChangeScheduledAt.HasValue)
        {
            double stuckSeconds = (DateTime.UtcNow - _mapChangeScheduledAt.Value).TotalSeconds;
            if (stuckSeconds > MAP_CHANGE_TIMEOUT_SECONDS)
            {
                Console.WriteLine($"[ShaedyMapChooser] WARNING: _mapChangeScheduled stuck for {stuckSeconds:F0}s. Force-resetting state!");
                ResetVoteState();
            }
        }

        if (_isVoteInProgress || _mapChangeScheduled) return;

        int currentPlayers = GetRealPlayerCount();

        if (currentPlayers > 0)
        {
            _serverEmptySince = null;
            return;
        }

        if (_serverEmptySince == null)
        {
            _serverEmptySince = DateTime.UtcNow;
            Console.WriteLine($"[ShaedyMapChooser] Server is now empty. Starting {Config.EmptyMapRotationCooldown}min timer...");
        }

        double minutesEmpty = (DateTime.UtcNow - _serverEmptySince.Value).TotalMinutes;

        if (minutesEmpty >= Config.EmptyMapRotationCooldown)
        {
            Console.WriteLine($"[ShaedyMapChooser] Server empty for {minutesEmpty:F1}min (threshold: {Config.EmptyMapRotationCooldown}min). Rotating to random map...");

            var randomMap = GetRandomMapFromPool();
            if (randomMap.Key != null)
            {
                _nextMap = randomMap;
                _mapChangeScheduled = true;
                _mapChangeScheduledAt = DateTime.UtcNow;

                Console.WriteLine($"[ShaedyMapChooser] Changing to {randomMap.Key} in 5 seconds...");

                AddTimer(5.0f, () =>
                {
                    ForceChangeMap();
                });
            }
            else
            {
                Console.WriteLine("[ShaedyMapChooser] No valid maps in pool for empty rotation!");
            }
        }
    }

    private int GetRealPlayerCount()
    {
        try
        {
            return Utilities.GetPlayers().Count(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV && p.Connected == PlayerConnectedState.PlayerConnected);
        }
        catch
        {
            return 0;
        }
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.UserId.HasValue)
        {
            _rtvVoters.Remove(player.UserId.Value);
            _playerVotes.Remove(player.UserId.Value);
        }
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_isVoteInProgress || _nextMap != null || _autoVoteTriggered) return HookResult.Continue;

        try
        {
            var maxRoundsCvar = ConVar.Find("mp_maxrounds");
            int maxRounds = maxRoundsCvar?.GetPrimitiveValue<int>() ?? 24;
            if (maxRounds <= 0) return HookResult.Continue;

            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
            if (gameRules != null)
            {
                int roundsPlayed = gameRules.TotalRoundsPlayed;
                int roundsRemaining = maxRounds - roundsPlayed;

                if (roundsRemaining <= Config.VoteRoundsBeforeEnd && roundsRemaining > 0)
                {
                    Console.WriteLine($"[ShaedyMapChooser] Auto-vote triggered. Rounds remaining: {roundsRemaining}");
                    Server.PrintToChatAll($"{_prefix} Match ends in {ChatColors.Yellow}{roundsRemaining}{ChatColors.White} rounds. Starting Map Vote...");
                    _autoVoteTriggered = true;
                    _rtvTriggered = false;
                    StartVote();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ShaedyMapChooser] Error in OnRoundStart: {ex.Message}");
        }
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }

    private HookResult OnMatchEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        Console.WriteLine($"[ShaedyMapChooser] Match ended. NextMap set: {_nextMap != null}, AutoVote: {_autoVoteTriggered}");

        if (_mapChangeScheduled) return HookResult.Continue;

        if (_nextMap != null)
        {
            Server.PrintToChatAll($"{_prefix} Next map: {ChatColors.Green}{GetDisplayName(_nextMap.Value.Key, _nextMap.Value.Value)}");
            Server.PrintToChatAll($"{_prefix} Changing map in 5 seconds...");
            _mapChangeScheduled = true;
            _mapChangeScheduledAt = DateTime.UtcNow;
            AddTimer(5.0f, ForceChangeMap);
        }
        else if (!_isVoteInProgress)
        {
            Console.WriteLine("[ShaedyMapChooser] No next map set at match end, starting emergency vote.");
            _autoVoteTriggered = true;
            _rtvTriggered = false;
            StartVote();
        }
        return HookResult.Continue;
    }

    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRtvCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || !player.UserId.HasValue) return;

        if (_mapChangeScheduled)
        {
            if (_mapChangeScheduledAt.HasValue && (DateTime.UtcNow - _mapChangeScheduledAt.Value).TotalSeconds > MAP_CHANGE_TIMEOUT_SECONDS)
            {
                Console.WriteLine("[ShaedyMapChooser] RTV: _mapChangeScheduled was stuck, resetting.");
                ResetVoteState();
            }
            else
            {
                player.PrintToChat($"{_prefix} Map change is already in progress!");
                return;
            }
        }
        if (_isVoteInProgress)
        {
            player.PrintToChat($"{_prefix} A vote is already in progress!");
            return;
        }
        if (_nextMap != null)
        {
            player.PrintToChat($"{_prefix} Next map already set: {ChatColors.Green}{GetDisplayName(_nextMap.Value.Key, _nextMap.Value.Value)}");
            return;
        }
        if (_rtvVoters.Contains(player.UserId.Value))
        {
            player.PrintToChat($"{_prefix} You have already voted for RTV.");
            return;
        }

        _rtvVoters.Add(player.UserId.Value);
        int currentPlayers = GetRealPlayerCount();
        if (currentPlayers <= 0) currentPlayers = 1;
        int votesNeeded = Math.Max(1, (int)Math.Ceiling(currentPlayers * Config.RtvPercentage));

        Server.PrintToChatAll($"{_prefix} {ChatColors.Green}{player.PlayerName}{ChatColors.White} wants to RTV. ({ChatColors.Yellow}{_rtvVoters.Count}/{votesNeeded}{ChatColors.White})");

        if (_rtvVoters.Count >= votesNeeded)
        {
            Server.PrintToChatAll($"{_prefix} RTV Vote passed! Starting map vote...");
            _rtvTriggered = true;
            _autoVoteTriggered = false;
            StartVote();
        }
    }

    private void StartVote()
    {
        if (_isVoteInProgress)
        {
            Console.WriteLine("[ShaedyMapChooser] StartVote called but vote already in progress!");
            return;
        }

        _isVoteInProgress = true;
        _rtvVoters.Clear();
        _currentVotes.Clear();
        _playerVotes.Clear();

        Console.WriteLine($"[ShaedyMapChooser] Starting vote. RTV: {_rtvTriggered}, Auto: {_autoVoteTriggered}");

        int currentPlayers = GetRealPlayerCount();

        // Determine the active map pool based on current map prefix
        var activeMaps = GetActiveMapsForCurrentMode();
        var validMaps = activeMaps.Where(m => currentPlayers >= m.Value.MinPlayers && currentPlayers <= m.Value.MaxPlayers).ToList();
        if (!validMaps.Any()) validMaps = activeMaps.ToList();

        if (!validMaps.Any())
        {
            Console.WriteLine("[ShaedyMapChooser] No maps available for vote!");
            Server.PrintToChatAll($"{_prefix} {ChatColors.Red}No maps available for voting!");
            _isVoteInProgress = false;
            return;
        }

        // Weighted random selection for map pool
        var mapPool = validMaps.Select(map =>
        {
            double weight = map.Value.Weight <= 0 ? 0.01 : map.Value.Weight;
            return new { Map = map, Score = Math.Pow(Random.Shared.NextDouble(), 1.0 / weight) };
        }).OrderByDescending(x => x.Score).Take(5).Select(x => x.Map).ToList();

        var voteMenu = new ChatMenu($"{ChatColors.Green}Vote for the next Map!");

        foreach (var mapEntry in mapPool)
        {
            string mapKey = mapEntry.Key;
            MapInfo mapInfo = mapEntry.Value;
            string display = GetDisplayName(mapKey, mapInfo);

            voteMenu.AddMenuOption(display, (voter, option) =>
            {
                if (voter == null || !voter.IsValid || !voter.UserId.HasValue) return;

                int voterId = voter.UserId.Value;

                if (_playerVotes.ContainsKey(voterId))
                {
                    voter.PrintToChat($"{_prefix} You already voted for {ChatColors.Green}{GetDisplayName(_playerVotes[voterId], LookupMapInfo(_playerVotes[voterId]))}{ChatColors.White}!");
                    return;
                }

                _playerVotes[voterId] = mapKey;
                if (!_currentVotes.ContainsKey(mapKey)) _currentVotes[mapKey] = 0;
                _currentVotes[mapKey]++;

                voter.PrintToChat($"{_prefix} You voted for {ChatColors.Green}{display}{ChatColors.White}.");
                Server.PrintToChatAll($"{_prefix} {ChatColors.Green}{voter.PlayerName}{ChatColors.White} voted for {ChatColors.Yellow}{display}");
            });
        }

        var players = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV).ToList();
        foreach (var p in players)
        {
            try
            {
                MenuManager.OpenChatMenu(p, voteMenu);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShaedyMapChooser] Error opening menu for {p.PlayerName}: {ex.Message}");
            }
        }

        Server.PrintToChatAll($"{_prefix} {ChatColors.Yellow}Map Vote started! {ChatColors.White}You have {ChatColors.Green}{Config.VoteDuration}{ChatColors.White} seconds to vote.");
        AddTimer(Config.VoteDuration, FinishVote);
    }

    private KeyValuePair<string, MapInfo> GetRandomMapFromPool()
    {
        var validMaps = GetActiveMapsForCurrentMode().ToList();
        if (!validMaps.Any()) return default;

        int totalWeight = validMaps.Sum(x => x.Value.Weight > 0 ? x.Value.Weight : 1);
        if (totalWeight <= 0) return validMaps[Random.Shared.Next(validMaps.Count)];

        int randomValue = Random.Shared.Next(0, totalWeight);
        int currentSum = 0;
        foreach (var map in validMaps)
        {
            int w = map.Value.Weight > 0 ? map.Value.Weight : 1;
            currentSum += w;
            if (randomValue < currentSum) return map;
        }
        return validMaps.First();
    }

    /// <summary>
    /// Gets the appropriate map pool based on the current map's prefix.
    /// If the current map starts with "surf_", uses the "surf" pool.
    /// If it starts with "kz_", uses the "kz" pool.
    /// Otherwise falls back to the default Maps pool.
    /// </summary>
    private Dictionary<string, MapInfo> GetActiveMapsForCurrentMode()
    {
        string? currentMapName = Server.MapName;
        if (!string.IsNullOrEmpty(currentMapName))
        {
            foreach (var pool in Config.MapPools)
            {
                if (currentMapName.StartsWith(pool.Key + "_", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[ShaedyMapChooser] Using '{pool.Key}' map pool ({pool.Value.Count} maps) based on current map '{currentMapName}'");
                    return pool.Value;
                }
            }
        }
        return Config.Maps;
    }

    private string GetDisplayName(string key, MapInfo? info) =>
        info != null && !string.IsNullOrEmpty(info.DisplayName) ? info.DisplayName : key;

    /// <summary>
    /// Looks up a MapInfo by key across all pools and default Maps.
    /// </summary>
    private MapInfo LookupMapInfo(string mapKey)
    {
        if (Config.Maps.TryGetValue(mapKey, out var info))
            return info;
        foreach (var pool in Config.MapPools.Values)
        {
            if (pool.TryGetValue(mapKey, out var poolInfo))
                return poolInfo;
        }
        return new MapInfo();
    }

    private void FinishVote()
    {
        Console.WriteLine($"[ShaedyMapChooser] Vote finished. Votes received: {_currentVotes.Count}");

        _isVoteInProgress = false;

        if (_currentVotes.Count == 0 || !_currentVotes.Any(v => v.Value > 0))
        {
            if (Config.Maps.Count > 0)
            {
                _nextMap = GetRandomMapFromPool();
                Server.PrintToChatAll($"{_prefix} No votes received. Random map: {ChatColors.Green}{GetDisplayName(_nextMap.Value.Key, _nextMap.Value.Value)}");
                Console.WriteLine($"[ShaedyMapChooser] No votes, random map selected: {_nextMap.Value.Key}");
            }
            else
            {
                Server.PrintToChatAll($"{_prefix} {ChatColors.Red}No maps available!");
                Console.WriteLine("[ShaedyMapChooser] No maps in config!");
                _rtvTriggered = false;
                _autoVoteTriggered = false;
                return;
            }
        }
        else
        {
            var winner = _currentVotes.OrderByDescending(x => x.Value).First();
            var mapInfo = LookupMapInfo(winner.Key);
            _nextMap = new KeyValuePair<string, MapInfo>(winner.Key, mapInfo);

            Server.PrintToChatAll($"{_prefix} Vote Winner: {ChatColors.Green}{GetDisplayName(_nextMap.Value.Key, _nextMap.Value.Value)}{ChatColors.White} ({winner.Value} votes)");
            Console.WriteLine($"[ShaedyMapChooser] Vote winner: {_nextMap.Value.Key} with {winner.Value} votes");
        }

        _currentVotes.Clear();
        _playerVotes.Clear();

        // RTV = Sofort wechseln, Auto-Vote = Am Match-Ende wechseln
        if (_rtvTriggered)
        {
            Server.PrintToChatAll($"{_prefix} Changing map in {ChatColors.Yellow}5 seconds{ChatColors.White}...");
            _mapChangeScheduled = true;
            _mapChangeScheduledAt = DateTime.UtcNow;
            AddTimer(5.0f, ForceChangeMap);
        }
        else if (_autoVoteTriggered)
        {
            Server.PrintToChatAll($"{_prefix} Map will change at the {ChatColors.Yellow}end of the match{ChatColors.White}.");
        }
        else
        {
            // Fallback: Sofort wechseln wenn unklar
            Console.WriteLine("[ShaedyMapChooser] Unknown vote trigger, changing immediately.");
            Server.PrintToChatAll($"{_prefix} Changing map in {ChatColors.Yellow}5 seconds{ChatColors.White}...");
            _mapChangeScheduled = true;
            _mapChangeScheduledAt = DateTime.UtcNow;
            AddTimer(5.0f, ForceChangeMap);
        }
    }

    // NEU: Robustere Map-Wechsel Funktion mit Retry-Logik
    private void ForceChangeMap()
    {
        if (_nextMap == null)
        {
            Console.WriteLine("[ShaedyMapChooser] ForceChangeMap called but no next map set!");
            _mapChangeScheduled = false;
            return;
        }

        var key = _nextMap.Value.Key;
        var info = _nextMap.Value.Value;

        Console.WriteLine($"[ShaedyMapChooser] Executing map change to: {key}");

        _nextMap = null;
        _autoVoteTriggered = false;
        _rtvTriggered = false;
        _serverEmptySince = null;
        _rtvVoters.Clear();
        _currentVotes.Clear();
        _playerVotes.Clear();
        Server.NextFrame(() =>
        {
            try
            {
                if (info != null && info.IsWorkshop && !string.IsNullOrEmpty(info.WorkshopId))
                {
                    Console.WriteLine($"[ShaedyMapChooser] Executing: host_workshop_map {info.WorkshopId}");
                    Server.ExecuteCommand($"host_workshop_map {info.WorkshopId}");
                }
                else
                {
                    Console.WriteLine($"[ShaedyMapChooser] Executing: changelevel {key}");
                    Server.ExecuteCommand($"changelevel {key}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShaedyMapChooser] Error during map change: {ex.Message}");
                // Fallback: map command
                try
                {
                    Server.ExecuteCommand($"map {key}");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[ShaedyMapChooser] Fallback map command also failed: {ex2.Message}");
                }
            }
        });
    }
}