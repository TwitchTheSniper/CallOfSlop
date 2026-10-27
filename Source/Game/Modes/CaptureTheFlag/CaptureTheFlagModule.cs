using System.Numerics;

namespace CallOfSlop;

/// <summary>Capture the Flag: one team's flag.</summary>
public sealed class FlagState
{
    public Vector3 Position;
    public byte Carrier = byte.MaxValue; // player id, or byte.MaxValue
    public bool Home = true;
    public double ReturnIn; // seconds until a dropped flag goes home (replicated)
    internal double ReturnAt;

    public bool Carried => Carrier != byte.MaxValue;
    public bool Dropped => !Home && !Carried;
}

/// <summary>
/// Capture the Flag: two teams, a flag stand each (<see cref="MapObjectives.Flags"/>). Grab the enemy flag and bring it
/// to yours while yours is home. A dropped flag goes home on its own after 20 s, or when a teammate touches it. First to 3.
/// </summary>
public sealed class CaptureTheFlagModule : GameModeModule
{
    /// <summary>Captures to win.</summary>
    public const int CaptureLimit = 3;

    /// <summary>A dropped flag goes home on its own after this long.</summary>
    public const double FlagReturnSeconds = 20;

    /// <summary>How close (horizontally) you must get to touch a flag or your base.</summary>
    public const float FlagTouchRadius = 1.6f;

    /// <summary>No clear trend with the player count in bot matches: the classic limit whatever the count.</summary>
    public static readonly ScoreLimits.Rule Rule = new(CaptureLimit, 0.00, 1, 3);

    private const float HaloHeight = 1.6f;

    public override GameMode Id => GameMode.CaptureTheFlag;
    public override string Name => "Capture the Flag";
    public override string Short => "CTF";
    public override string Rules(int limit = 0) =>
        $"Two teams. Grab the enemy flag and bring it to yours while yours is home. First to {(limit > 0 ? limit : CaptureLimit)} captures.";
    public override bool TeamBased => true;
    public override ScoreLimits.Rule? ScoreRule => Rule;
    public override string ScoreUnit => "captures";
    public override MapNeeds Needs => MapNeeds.TeamSpawns | MapNeeds.Flags;

    // ---- State (server-owned, replicated) ----------------------------------------------------------------

    public readonly FlagState[] Flags = [new(), new()];

    /// <summary>Which flag (team index) this player is carrying, or -1.</summary>
    public int CarriedFlagOf(Player p)
    {
        for (var t = 0; t < 2; t++)
            if (Flags[t].Carrier == p.Id)
                return t;
        return -1;
    }

    private static bool Touches(Vector3 feet, Vector3 at)
    {
        if (GravityFrames.Curved)
        {
            var rel = GravityFrames.ToLocal(at, feet - at); // a planet: in the flag's own frame
            return rel.X * rel.X + rel.Z * rel.Z <= FlagTouchRadius * FlagTouchRadius && MathF.Abs(rel.Y) < 2f;
        }
        var d = new Vector2(feet.X - at.X, feet.Z - at.Z);
        return d.LengthSquared() <= FlagTouchRadius * FlagTouchRadius && MathF.Abs(feet.Y - at.Y) < 2f;
    }

    // ---- Lifecycle ----------------------------------------------------------------------------------------

    public override void OnReset(Game game) => ResetFlags(game);

    public override void OnMapChanged(Game game) => ResetFlags(game);

    private void ResetFlags(Game game)
    {
        for (var t = 0; t < 2; t++)
        {
            Flags[t].Position = game.Objectives.Flags[t];
            Flags[t].Carrier = byte.MaxValue;
            Flags[t].Home = true;
            Flags[t].ReturnIn = 0;
        }
    }

    public override void ServerTickPlaying(Game game, double now)
    {
        TickFlags(game, now);
        TeamWinCheck(game, now);
    }

    private void ReturnFlag(Game game, int team, string why)
    {
        var f = Flags[team];
        f.Position = game.Objectives.Flags[team];
        f.Carrier = byte.MaxValue;
        f.Home = true;
        f.ReturnIn = 0;
        game.ModeEvent($"{GameModes.TeamNames[team]} flag {why}", Sfx.Respawn);
    }

    private void TickFlags(Game game, double now)
    {
        var stands = game.Objectives.Flags;
        // Carriers carry; a carrier who died or left drops the flag where they were.
        for (var t = 0; t < 2; t++)
        {
            var f = Flags[t];
            if (f.Carried)
            {
                if (game.Players.TryGetValue(f.Carrier, out var c) && c.Alive)
                    f.Position = c.Position;
                else
                {
                    f.Carrier = byte.MaxValue;
                    f.ReturnAt = now + FlagReturnSeconds;
                    game.ModeEvent($"{GameModes.TeamNames[t]} flag dropped", Sfx.HitTick);
                }
            }
            if (f.Dropped && now >= f.ReturnAt)
                ReturnFlag(game, t, "returned to base");
            f.ReturnIn = f.Dropped ? Math.Max(0, f.ReturnAt - now) : 0;
        }

        foreach (var p in game.Players.Values)
        {
            if (!p.Alive || p.Team > 1)
                continue;
            var own = p.Team;
            var enemy = 1 - own;
            var carrying = CarriedFlagOf(p);
            // Pick up the enemy flag (home or dropped).
            if (carrying < 0 && !Flags[enemy].Carried && Touches(p.Position, Flags[enemy].Position))
            {
                Flags[enemy].Carrier = p.Id;
                Flags[enemy].Home = false;
                carrying = enemy;
                game.ModeEvent($"{p.Name} took the {GameModes.TeamNames[enemy]} flag!", Sfx.ReconBeep);
                game.LogicObjective(LogicEvent.FlagTaken, enemy, p); // map logic: match.flagTaken (GameLogic.cs)
            }
            // Touching your own dropped flag sends it home.
            if (Flags[own].Dropped && Touches(p.Position, Flags[own].Position))
                ReturnFlag(game, own, $"returned by {p.Name}");
            // Capture: bring the enemy flag to your own flag, while yours is at home.
            if (carrying == enemy && Flags[own].Home && Touches(p.Position, stands[own]))
            {
                game.TeamScore[own]++;
                Flags[enemy].Carrier = byte.MaxValue;
                Flags[enemy].Home = true;
                Flags[enemy].Position = stands[enemy];
                game.ModeEvent($"{p.Name} CAPTURED the {GameModes.TeamNames[enemy]} flag! ({GameModes.TeamNames[own]} {game.TeamScore[own]}-{game.TeamScore[enemy]})", Sfx.LevelUp);
                game.LogicObjective(LogicEvent.FlagCaptured, own, p); // map logic: match.flagCaptured, n = the team that scored
            }
        }
    }

    // ---- Bots ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A carrier runs home; your dropped flag gets picked up; an enemy carrier gets chased; otherwise even ids attack
    /// the enemy flag and odd ids guard their own (or escort a teammate carrying).
    /// </summary>
    public override Vector3? BotObjective(Game game, Player bot)
    {
        if (bot.Team > 1)
            return null;
        var own = bot.Team;
        var enemy = 1 - own;
        var home = game.Objectives.Flags[own];
        if (CarriedFlagOf(bot) >= 0)
            return home; // run it home (it scores once our flag is back)
        var mine = Flags[own];
        if (mine.Dropped)
            return mine.Position; // return it
        if (mine.Carried && game.Players.TryGetValue(mine.Carrier, out var thief))
            return thief.Position; // chase the thief
        var theirs = Flags[enemy];
        if (theirs.Carried && game.Players.TryGetValue(theirs.Carrier, out var buddy) && buddy != bot)
            return bot.Id % 2 == 0 ? buddy.Position : home; // escort or guard
        return bot.Id % 2 == 0 ? theirs.Position : home + new Vector3(MathF.Cos(bot.Id), 0, MathF.Sin(bot.Id)) * 3f;
    }

    // ---- Replicated state (2 x 18 bytes) ---------------------------------------------------------------------

    public override void WriteState(Game game, PacketWriter w)
    {
        foreach (var f in Flags)
            w.Vec(f.Position).U8(f.Carrier).U8(f.Home ? (byte)1 : (byte)0).F32((float)f.ReturnIn);
    }

    public override void ReadState(Game game, ref PacketReader r)
    {
        foreach (var f in Flags)
        {
            if (!r.Has(18))
                break;
            f.Position = r.Vec();
            f.Carrier = r.U8();
            f.Home = r.U8() != 0;
            f.ReturnIn = r.F32();
        }
    }

    // ---- HUD ----------------------------------------------------------------------------------------------

    public override void HudLines(Game game, Player me, List<HudLine> lines, ref Vector3? pointAt)
    {
        for (byte t = 0; t < 2; t++)
        {
            var f = Flags[t];
            var who = f.Carried ? (f.Carrier == game.LocalId ? "YOU" : game.NameOf(f.Carrier)) : "";
            var text = f.Home ? "at base" : f.Carried ? $"taken by {who}" : $"dropped - returns in {Math.Ceiling(f.ReturnIn):0} s";
            lines.Add(new HudLine($"{GameModes.TeamNames[t].ToUpperInvariant()} FLAG: {text}", HudTeam(t)));
        }
        var carrying = CarriedFlagOf(me);
        if (carrying >= 0 && me.Team < 2)
        {
            lines.Add(new HudLine(Flags[me.Team].Home ? "You have the flag - take it to your base!" : "You have the flag - your flag must be home to score", HudOrange));
            pointAt = game.Objectives.Flags[me.Team];
        }
        else if (me.Team < 2 && Flags[me.Team].Dropped)
            pointAt = Flags[me.Team].Position;
        else if (me.Team < 2 && !Flags[1 - me.Team].Carried)
            pointAt = Flags[1 - me.Team].Position;
    }

    public override string ScoreboardTag(Game game, Player p) => CarriedFlagOf(p) >= 0 ? " [FLAG]" : "";

    /// <summary>Flags with their base rings and halos, and a beam (seen through walls) over a flag that's carried or dropped.</summary>
    public override void DrawObjectives(Renderer gfx, Game game, double now)
    {
        for (byte t = 0; t < 2; t++)
        {
            var c = GameModes.TeamColors[t];
            var home = game.Objectives.Flags[t];
            ObjectiveRender.Ring(gfx, home, FlagTouchRadius, c, 0.6f, 0.08f);
            ObjectiveRender.Halo(gfx, home, FlagTouchRadius, HaloHeight, c, 0.38f);
            var f = Flags[t];
            var at = f.Position;
            if (f.Carried && game.Players.TryGetValue(f.Carrier, out var carrier))
            {
                at = carrier.RenderPosition + new Vector3(0, 0.9f, 0); // on the carrier's back
                if (carrier.Id == game.LocalId && carrier.Alive)
                    continue; // you know you've got it (the HUD says so)
            }
            ObjectiveRender.Flag(gfx, at, c, now);
            if (!f.Home)
            {
                // Seen through walls: carriers and dropped flags can't hide.
                gfx.SetDepthTest(false);
                ObjectiveRender.Beam(gfx, at, 0.18f, 30f, c, f.Carried ? 0.35f : 0.22f);
                gfx.SetDepthTest(true);
            }
        }
    }
}
