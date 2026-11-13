using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The break-out's fight at the Ministry Spaceport (docs/story/BREAKOUT.md "The spaceport"): the first time someone who
/// escaped comes near the landing, the spaceport's guards turn out at their posts round the terminal and go for whoever
/// escaped (only them: <see cref="BreakoutGuardFights"/>). The convicts who fled fight back, and the guards can't hurt
/// them, apart from the file's scripted deaths: at its time (or once enough guards are down) a convict is hit, says
/// their last words to the nearest escapee and dies, and every escapee near gains the lead those words give (a Lead
/// the Logbook lists until the record it points to is found: Story/Fragments "leads"). The fight is over when every
/// guard is down: each escapee there gains <c>Stormed</c>, and the guards never come again for them. A world loaded
/// mid-fight turns them out again (the dead stay dead). Host only, with the break-out's ten looks a second; the
/// guards are bots of the hostile side, like the colony's riot squad (GameColonyLockdown.cs). No wire change.
/// </summary>
public sealed partial class Game
{
    public const float BreakoutFightFar = 160f; // m from the landing: an escapee further off isn't fought (they ran)
    public const double BreakoutWordGap = 3.2; // s between a dying convict's lines

    private readonly Player?[] _bfGuard = new Player?[256]; // by player id: a spaceport guard
    private readonly Vector3[] _bfPost = new Vector3[256];
    private bool _bfOn;
    private double _bfSince;
    private int _bfTurned, _bfDeath, _bfLine, _bfQStormed = -1;
    private int _bfDying = -1; // the convict saying their last words (a story entity), -1: none
    private double _bfLineAt;
    private Player? _bfKill; // the one body the guards may hurt, for the moment of a scripted death

    /// <summary>Both sides, a new match: no fight.</summary>
    private void BreakoutFightReset()
    {
        Array.Clear(_bfGuard);
        _bfOn = false;
        _bfTurned = _bfDeath = _bfLine = 0;
        _bfDying = -1;
        _bfKill = null;
        _bfQStormed = -1;
    }

    public bool BreakoutGuardOf(Player p) => _bfGuard[p.Id] == p;

    /// <summary>The guards' side (SandboxEnemies): a guard attacks whoever escaped and is still at the spaceport, and a convict only to kill them.</summary>
    private bool BreakoutGuardFights(Player target) =>
        target == _bfKill || target.Bot == null && target.Id < MaxPlayers && _bkWho[target.Id] == target && _bk[target.Id].Stage == BreakoutSpaceport
        && _bk[target.Id].RideFrom == 0 && target.Alive && BreakoutLanding() is { } l && Flat2(target.Position - l) < BreakoutFightFar * BreakoutFightFar;

    /// <summary>The convicts' side (SandboxEnemies): a convict who fled fights the guards.</summary>
    private bool BreakoutCrewFights(Player convict, Player guard)
    {
        if (!_bfOn || _bfGuard[guard.Id] != guard || _bkCrewOf == null || _bkQFled < 0)
            return false;
        for (var i = 0; i < _bkCrew.Length; i++)
            if (_bkCrew[i] >= 0 && StoryBodyOf(_bkCrew[i]) == convict)
                return _bkCrewOf.World.Quality(_bkCrew[i], _bkQFled) > 0 && _bkCrew[i] != _bfDying;
        return false;
    }

    /// <summary>Server, from <see cref="BreakoutTick"/>'s look: turn out, steer the scripted deaths, and stand down.</summary>
    private void BreakoutFightTick(StoryEngine s, double now)
    {
        var fight = BreakoutScript.Current.Fight;
        if (fight.Guards.Length == 0 || BreakoutLanding() is not { } landing)
            return;
        if (_bfQStormed == -1)
            _bfQStormed = s.Rules.Quality("Stormed")?.Id ?? -2;
        if (!_bfOn)
        {
            if (BreakoutFightWanted(s, landing, fight.Wake) is { } first)
                BreakoutTurnOut(s, first, now);
            return;
        }
        var alive = 0;
        for (var id = 1; id < MaxPlayers; id++)
        {
            if (_bfGuard[id] is not { } g)
                continue;
            if (!Players.TryGetValue((byte)id, out var p) || p != g)
                _bfGuard[id] = null; // (killed and taken away)
            else if (g.Alive)
                alive++;
        }
        BreakoutDeaths(s, fight, alive, now);
        if (alive > 0 || _bfDying >= 0)
            return;
        // Every guard down: the spaceport is theirs.
        _bfOn = false;
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Id < MaxPlayers && BreakoutGuardFights(p) && StoryWho(p) is var who and >= 0 && _bfQStormed >= 0)
                s.World.SetQuality(who, _bfQStormed, 1);
        if (fight.Won.Length > 0)
            ServerSystemChat(fight.Won, now);
        Log("[breakout] the spaceport's guards are down");
    }

    /// <summary>An escapee within <paramref name="wake"/> of the landing who hasn't stormed the spaceport yet, or null.</summary>
    private Player? BreakoutFightWanted(StoryEngine s, Vector3 landing, float wake)
    {
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers || !p.Alive || _bkWho[p.Id] != p || _bk[p.Id].Stage != BreakoutSpaceport || _bk[p.Id].RideFrom != 0)
                continue;
            if (Flat2(p.Position - landing) > wake * wake || StoryWho(p) is not (var who and >= 0))
                continue;
            if (_bfQStormed >= 0 && s.World.Quality(who, _bfQStormed) > 0)
                continue;
            return p;
        }
        return null;
    }

    /// <summary>The guards out at their posts round the terminal, the first of them shouting.</summary>
    private void BreakoutTurnOut(StoryEngine s, Player at, double now)
    {
        var fight = BreakoutScript.Current.Fight;
        if (!SpaceportFrame(out var origin, out var rot))
            return;
        _bfOn = true;
        _bfSince = now;
        _bfDeath = _bfLine = 0;
        _bfDying = -1;
        Player? shouter = null;
        for (var n = 0; n < fight.Guards.Length; n++)
        {
            var id = FreeNpcId();
            if (id == 0)
                break;
            var (name, post) = fight.Guards[n];
            var w = origin + Vector3.Transform(new Vector3(post.X, 0, post.Y), rot);
            var feet = StandInPath.Ground(w + StandInPath.Up(w) * 30f) + StandInPath.Up(w) * 0.6f;
            var bot = new Player
            {
                Id = id,
                Name = name,
                Bot = new BotBrain(BotDifficulty.Normal, n % 2 == 0 ? BotPersonality.Tactician : BotPersonality.Rusher, _rng.Next()),
                Team = SbHostileTeam, // (but it fights only the escapees: SandboxEnemies)
                SbSpawned = true, // (never respawns: killed, it goes)
            };
            Players[id] = bot;
            Spawn(bot, ServerTick, Brutal.GlfwApi.Glfw.Time);
            bot.Teleport(feet);
            bot.SbHome = feet;
            var d = at.Position - feet;
            bot.Yaw = bot.RenderYaw = MathF.Atan2(d.X, -d.Z);
            for (var i = 0; i < Player.HistorySize; i++)
                bot.History[i] = new HistoryEntry { Position = feet, Yaw = bot.Yaw };
            SandboxBotGun(bot, SbLoadKind.Class, (byte)(n % 2 == 0 ? WeaponClass.AssaultRifle : WeaponClass.SMG));
            _bfGuard[id] = bot;
            _bfPost[id] = feet;
            _bfTurned++;
            shouter ??= bot;
        }
        BroadcastRoster();
        if (shouter != null && fight.Shout.Length > 0)
            StorySpeak(shouter, fight.Shout, now);
        Log($"[breakout] the spaceport's guards turn out for {at.Name}");
    }

    /// <summary>
    /// Server, before the bot tick (GameSandboxSpawns.cs): a guard holds their post until an escapee is in the fight, then
    /// goes for the nearest (the bot's own fighting does the rest). False: not a guard.
    /// </summary>
    internal bool BreakoutGuardBot(Player p, BotBrain b, double now)
    {
        if (_bfGuard[p.Id] != p)
            return false;
        b.HoldUntil = 0;
        Player? best = null;
        var bestD = float.MaxValue;
        foreach (var q in Players.Values)
            if (q.Bot == null && BreakoutGuardFights(q))
            {
                var d2 = Vector3.DistanceSquared(q.Position, p.Position);
                if (d2 < bestD)
                    (best, bestD) = (q, d2);
            }
        b.ForcedGoal = best != null && bestD > 18f * 18f ? best.Position : _bfPost[p.Id];
        return true;
    }

    /// <summary>The file's deaths, one at a time: due by time or by guards down, the convict's words one by one, then the shot.</summary>
    private void BreakoutDeaths(StoryEngine s, BreakoutScript.FightPlan fight, int alive, double now)
    {
        if (_bfDying < 0)
        {
            while (_bfDeath < fight.Deaths.Length)
            {
                var death = fight.Deaths[_bfDeath];
                var down = _bfTurned - alive;
                if (now - _bfSince < death.After && (death.Down <= 0 || down < death.Down))
                    return;
                var e = s.World.Find(death.Who);
                if (e < 0 || s.Agents.Of(e) is not { Dead: false } || _bkQFled < 0 || s.World.Quality(e, _bkQFled) == 0 || StoryBodyOf(e) is not { Alive: true })
                {
                    _bfDeath++; // (already dead, or not here: their words are lost)
                    continue;
                }
                _bfDying = e;
                _bfLine = 0;
                _bfLineAt = now;
                Log($"[breakout] {death.Who} is hit");
                break;
            }
            if (_bfDying < 0)
                return;
        }
        if (now < _bfLineAt)
            return;
        var d = fight.Deaths[_bfDeath];
        var body = StoryBodyOf(_bfDying);
        if (body is not { Alive: true })
        {
            BreakoutDeathDone(s, d, now); // (killed some other way meanwhile)
            return;
        }
        if (_bfLine < d.Words.Length)
        {
            StorySpeak(body, d.Words[_bfLine], now);
            _bfLine++;
            _bfLineAt = now + BreakoutWordGap;
            return;
        }
        // The last word said: the shot. The nearest guard alive takes the blame (or nobody's, if none is).
        Player? by = null;
        var bestD = float.MaxValue;
        for (var id = 1; id < MaxPlayers; id++)
            if (_bfGuard[id] is { Alive: true } g && Vector3.DistanceSquared(g.Position, body.Position) is var d2 && d2 < bestD)
                (by, bestD) = (g, d2);
        _bfKill = body;
        DamagePlayer(by ?? body, body, 1000, true, by?.Weapon ?? MeleeKillId, now);
        _bfKill = null;
        BreakoutDeathDone(s, d, now);
    }

    /// <summary>A death over: the lead to every escapee near, and on to the next.</summary>
    private void BreakoutDeathDone(StoryEngine s, BreakoutScript.Death d, double now)
    {
        if (d.Lead.Length > 0 && _bkRelLead >= 0 && BreakoutLanding() is { } l)
        {
            var thing = s.World.Find(d.Lead);
            if (thing < 0)
                thing = s.World.Create(StoryKind.Thing, d.Lead, 0, s.Time);
            foreach (var p in Players.Values)
                if (p.Bot == null && p.Id < MaxPlayers && _bkWho[p.Id] == p && _bk[p.Id].Stage == BreakoutSpaceport
                    && Flat2(p.Position - l) < BreakoutFightFar * BreakoutFightFar && StoryWho(p) is var who and >= 0 && s.World.Get(who, _bkRelLead, thing) == 0)
                {
                    s.World.Set(who, _bkRelLead, thing, 1);
                    SandboxHands.Notices.Add((p.Id, $"In your Logbook (F1): {d.Lead}."));
                }
        }
        Log($"[breakout] {d.Who} is dead; the lead: {d.Lead}");
        _bfDying = -1;
        _bfDeath++;
    }
}
