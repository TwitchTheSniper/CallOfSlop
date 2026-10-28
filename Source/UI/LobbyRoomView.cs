using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The lobby room's text, built once and kept until something it shows changes (PreMatchLobby.cs). The lobby screen
/// draws these strings every frame; <see cref="Refresh"/> only compares a signature of the game's state (no
/// allocations) and rebuilds when it moved: a join, a ready tick, a team change, a ping, a setting, the countdown's
/// second. The self-test checks that a frame without changes allocates nothing.
/// </summary>
public sealed class LobbyRoomView
{
    /// <summary>One line of the player list.</summary>
    public sealed class Row
    {
        public byte Id;
        public string Name = "";
        public string Rank = "";
        public string Ping = "";
        public byte Team = Game.NoTeam;
        public bool Picked, Ready, Bot, Host, You;
        public HatId Hat;
        public ulong SteamId; // the Steam avatar before the name, when both sides are on Steam (SteamAvatars.cs)
        public Vector3 Color;
        public int PingMs;
    }

    /// <summary>One map the host can pick, with its line and whether it fits the people here.</summary>
    public readonly record struct MapEntry(Map Map, string Label, bool Allowed, bool Current);

    public readonly List<Row> Rows = new();
    public readonly List<MapEntry> MapList = new();
    public readonly List<string> ChatText = new();
    public readonly int[] TeamCounts = new int[2];

    public string MapTitle = "", MapInfo = "", ModeLine = "", PackLine = "", SettingsLine = "", ReadyLine = "", CountdownLine = "", BotsLine = "";

    /// <summary>The settings tabs' text (UiLobbyRoom.cs): the mode's rules, the score limit's meaning, the gravity combo's
    /// label and the Rules line under the settings line (destruction, voice, dead chat, bot gear).</summary>
    public string ModeRules = "", ScoreLimitText = "", GravityLabel = "", RulesLine = "";
    public int Ready, Humans;

    /// <summary>Why the local player can't join Blue / Red right now (null: they can). <see cref="Game.TeamRefusal"/> (no cooldown in the lobby).</summary>
    public string? RefuseBlue, RefuseRed;

    /// <summary>How many times the text was rebuilt (the self-test checks a quiet lobby doesn't rebuild).</summary>
    public int Rebuilds { get; private set; }

    private long _signature = long.MinValue;
    private int _chatVersion = -1;
    private string _mapListKey = "";
    private int _mapListPack = -2, _mapListHumans = -1;
    private GameMode _mapListMode;
    private readonly List<Row> _pool = new();

    /// <summary>The pack whose maps <see cref="MapList"/> lists (-1: True Random's pool).</summary>
    public int MapListPack => _mapListPack;

    /// <summary>Brings the text up to date with <paramref name="game"/>. Cheap and allocation-free when nothing changed.</summary>
    public void Refresh(Game game, Progress? localProgress)
    {
        var sig = Signature(game, localProgress);
        if (sig != _signature)
        {
            _signature = sig;
            Rebuild(game, localProgress);
        }
        if (game.ChatVersion != _chatVersion)
        {
            _chatVersion = game.ChatVersion;
            ChatText.Clear();
            foreach (var c in game.Chat)
                ChatText.Add(ChatRules.Format(c)); // "[TEAM] Name: text", or a system line (LobbyChat.cs)
        }
    }

    /// <summary>Forget everything (a new session): the next <see cref="Refresh"/> rebuilds.</summary>
    public void Reset()
    {
        _signature = long.MinValue;
        _chatVersion = -1;
        _mapListPack = -2;
        _mapListKey = "";
    }

    private static long Mix(long h, long v) => unchecked((h ^ v) * 0x100000001B3L + (long)0x9E3779B97F4A7C15UL);

    private static long Signature(Game game, Progress? prog)
    {
        long h = 1469598103934665603L;
        h = Mix(h, game.LobbyVersion);
        h = Mix(h, (long)game.Phase);
        h = Mix(h, game.LocalId);
        h = Mix(h, (long)Math.Ceiling(game.LobbyCountdown));
        h = Mix(h, (long)Math.Ceiling(game.LobbyAutoStartIn));
        h = Mix(h, (long)game.Mode);
        h = Mix(h, game.ScoreLimit);
        h = Mix(h, game.MaxTeamDiff * 4 + (int)game.TeamSelection + ((int)game.TeamBalance << 8) + (game.HumansTeam << 12));
        h = Mix(h, World.Map.Name.GetHashCode());
        h = Mix(h, game.GravityPercent | (game.MatchMinutes << 8) | ((int)game.BotSkill << 16) | ((int)game.BotGearMode << 20) | ((int)game.ScoreLimitMode << 24));
        h = Mix(h, game.WantedBots | (game.AutoStartSeconds << 8) | ((game.DestructionEnabled ? 1 : 0) << 24) | ((game.VoiceAllowed ? 1 : 0) << 25)
                   | ((game.DeadChatPrivate ? 1 : 0) << 26) | ((game.ReturnToLobby ? 1 : 0) << 27) | ((game.FrenzyBuffsEnabled ? 1 : 0) << 28));
        h = Mix(h, game.ZStartRound | ((game.ZTestKit ? 1 : 0) << 8));
        h = Mix(h, game.Players.Count);
        foreach (var p in game.Players.Values)
        {
            h = Mix(h, p.Id | (p.Team << 8) | (p.TeamChoice << 16) | ((p.Ready ? 1 : 0) << 24) | ((p.Bot != null ? 1 : 0) << 25));
            h = Mix(h, p.Name.GetHashCode());
            h = Mix(h, p.Prestige | (p.Level << 8) | ((int)p.Hat << 16));
            h = Mix(h, p.SteamId.GetHashCode());
            h = Mix(h, p.Id < game.PingMs.Length ? game.PingMs[p.Id] / 5 : 0); // 5 ms steps: no rebuild for 1 ms jitter
        }
        if (prog != null)
            h = Mix(h, prog.Prestige | (prog.Level << 8));
        return h;
    }

    private void Rebuild(Game game, Progress? prog)
    {
        Rebuilds++;
        foreach (var r in Rows)
            _pool.Add(r);
        Rows.Clear();
        TeamCounts[0] = TeamCounts[1] = 0;
        Ready = Humans = 0;
        foreach (var p in game.Players.Values)
        {
            var r = _pool.Count > 0 ? _pool[^1] : new Row();
            if (_pool.Count > 0)
                _pool.RemoveAt(_pool.Count - 1);
            r.Id = p.Id;
            r.You = p.Id == game.LocalId;
            r.Bot = p.Bot != null;
            r.Host = p.Id == Game.HostId && !r.Bot;
            r.Name = r.You ? $"{game.NameOf(p.Id)} (you)" : game.NameOf(p.Id);
            var (prestige, level) = r.You && prog != null ? ((int)prog.Prestige, (int)prog.Level) : ((int)p.Prestige, (int)p.Level);
            r.Rank = r.Bot ? "BOT" : level > 0 ? Progress.RankLabel(prestige, level) : "";
            r.Hat = p.Hat;
            r.SteamId = r.Bot ? 0 : p.SteamId;
            r.Team = game.IsTeamMode ? p.Team : Game.NoTeam;
            r.Picked = !r.Bot && p.TeamChoice < 2;
            r.Ready = r.Bot || p.Ready;
            r.Color = game.IsTeamMode && p.Team < 2 ? GameModes.TeamColor(p.Team) : p.Color;
            r.PingMs = p.Id < game.PingMs.Length ? game.PingMs[p.Id] : 0;
            r.Ping = r.Bot ? "-" : r.Host ? "host" : $"{r.PingMs} ms";
            if (r.Team < 2)
                TeamCounts[r.Team]++;
            if (!r.Bot)
            {
                Humans++;
                if (p.Ready)
                    Ready++;
            }
            Rows.Add(r);
        }
        Rows.Sort(static (a, b) =>
        {
            var c = a.Team.CompareTo(b.Team);
            if (c != 0)
                return c;
            c = a.Bot.CompareTo(b.Bot);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });

        RefuseBlue = RefuseRed = null;
        if (game.IsTeamMode && game.Local is { } me)
        {
            var at = Brutal.GlfwApi.Glfw.Time;
            RefuseBlue = game.TeamRefusal(me, 0, at);
            RefuseRed = game.TeamRefusal(me, 1, at);
        }

        var map = World.Map;
        var limit = game.MapMaxPlayers;
        MapTitle = map.Name;
        MapInfo = $"{map.Size} map, {map.SizeLabel}, up to {limit} players{(map.Description.Length > 0 ? ". " + map.Description : "")}";
        var mode = game.Module;
        ModeLine = $"{mode.Name}: {game.ModeGoalText}";
        PackLine = game.TrueRandom ? $"Pack: {MapPack.TrueRandomName} (the vote offers maps from every pack)"
            : map.Pack is { } pack ? $"Pack: {pack.Name} (the vote offers its other maps)" : "Pack: none";
        var limitText = ScoreLimits.HasLimit(game.Mode) ? $"score limit {game.ScoreLimit} ({(game.ScoreLimitMode == ScoreLimitMode.Fixed ? "fixed" : "auto")})" : "no score limit";
        var auto = game.AutoStartSeconds > 0 ? $"auto-start {game.AutoStartSeconds} s" : "host starts";
        SettingsLine = $"{limitText}, {game.MatchMinutes} min, {auto}, {(game.ReturnToLobby ? "back to the lobby after each match" : "next match straight after the vote")}";
        if (game.EffectiveGravityPercent != 100)
            SettingsLine += ", " + Game.GravityLabel(game.EffectiveGravityPercent) + (game.EffectiveGravityPercent != game.GravityPercent ? $" (the lowest this map plays at; set {game.GravityPercent}%)" : "");
        if (game.Mode == GameMode.Zombies && game.ZStartRound > 1)
            SettingsLine += $", starts at round {game.ZStartRound}";
        if (game.Mode == GameMode.Zombies && game.TestKitOn)
            SettingsLine += ", test kit (every perk, 4 slots, strong guns)";
        if (game.IsTeamMode && game.TeamBalance != TeamBalance.Auto)
            SettingsLine += game.HumansVsBots ? $", Humans vs Bots (people on {GameModes.TeamNames[game.HumansTeam]})" : ", team balancing off";
        ModeRules = mode.Rules();
        ScoreLimitText = ScoreLimits.Describe(game.Mode, game.ScoreLimitMode);
        GravityLabel = game.GravityPercent == 100 ? "100% (the map's own)" : $"{game.GravityPercent}%";
        var destruction = map.Destruction == null ? "no destructible buildings on this map"
            : !Game.DestructionAllowedIn(game.Mode) ? "buildings stay whole in this mode"
            : game.DestructionEnabled ? "destruction on" : "destruction off";
        RulesLine = $"Frenzy buffs {(game.FrenzyBuffsEnabled ? "on" : "off")}, {destruction}, voice chat {(game.VoiceAllowed ? "allowed" : "off")}, "
                    + $"{(game.DeadChatPrivate ? "dead players' chat reaches only the dead" : "dead players' chat reaches everyone")}"
                    + (game.WantedBots > 0 ? $", bot gear: {BotGear.Name(game.BotGearMode)}" : "");
        BotsLine = game.WantedBots > 0 ? $"{game.BotCount} bot{(game.BotCount == 1 ? "" : "s")} ({game.BotSkill}) fill the empty places" : "No bots";
        ReadyLine = $"{Ready}/{Humans} ready";
        var cd = game.LobbyCountdown;
        CountdownLine = cd > 0 ? $"STARTING IN {Math.Ceiling(cd):0}" + (game.LobbyForced ? " (forced)" : "")
            : game.LobbyAutoStartIn > 0 ? $"Auto-start in {Math.Ceiling(game.LobbyAutoStartIn):0} s"
            : Ready == Humans ? "Everyone is ready" : "Waiting for everyone to ready up";
    }

    /// <summary>
    /// The host's map list for one pack (<paramref name="packIndex"/> into <see cref="Maps.Packs"/>, -1 True Random):
    /// every map with its size and limit, greyed out when it doesn't fit the people here. Rebuilt on changes only.
    /// </summary>
    public void RefreshMapList(Game game, int packIndex)
    {
        var humans = 0;
        foreach (var p in game.Players.Values) // (not HumanCount's LINQ: this runs every frame)
            if (p.Bot == null)
                humans++;
        var key = World.Map.Name;
        if (packIndex == _mapListPack && humans == _mapListHumans && game.Mode == _mapListMode && ReferenceEquals(key, _mapListKey))
            return;
        _mapListPack = packIndex;
        _mapListHumans = humans;
        _mapListMode = game.Mode;
        _mapListKey = key;
        MapList.Clear();
        var packs = Maps.Packs;
        IReadOnlyList<Map> pool = packIndex >= 0 && packIndex < packs.Length ? packs[packIndex].Maps : MapPack.TrueRandomPool(packs, Maps.All);
        foreach (var m in pool)
        {
            var allowed = game.LobbyMapAllowed(m);
            var lim = game.PlayerLimit(m);
            var why = allowed ? "" : !game.Module.Fits(m) ? " - not for this mode" : $" - too small for {humans}";
            MapList.Add(new MapEntry(m, $"{m.Name}  ({m.Size}, up to {lim}){why}", allowed, m.Name == key));
        }
    }
}
