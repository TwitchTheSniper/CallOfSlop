using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// King of the Hill: two teams hold a hill zone (<see cref="MapObjectives.Hills"/>). The one team on it alone scores a
/// point a second; contested or empty, nobody does. The hill moves to the next zone every minute. First to 150.
/// </summary>
public sealed class KingOfTheHillModule : GameModeModule
{
    /// <summary>Points to win (1 point per second of holding the hill alone).</summary>
    public const int ScoreLimit = 150;

    /// <summary>The hill moves to the next zone this often.</summary>
    public const double MoveSeconds = 60;

    /// <summary>No clear trend with the player count in bot matches: the classic limit whatever the count.</summary>
    public static readonly ScoreLimits.Rule Rule = new(ScoreLimit, 0.00, 10, 150);

    private const float HaloHeight = 2.4f;

    public override GameMode Id => GameMode.KingOfTheHill;
    public override string Name => "King of the Hill";
    public override string Short => "KOTH";
    public override string Rules(int limit = 0) =>
        $"Two teams. Hold the hill alone for 1 point a second (contested: nobody scores). It moves every {MoveSeconds:0} s. First to {(limit > 0 ? limit : ScoreLimit)}.";
    public override bool TeamBased => true;
    public override ScoreLimits.Rule? ScoreRule => Rule;
    public override string ScoreUnit => "points";
    public override MapNeeds Needs => MapNeeds.TeamSpawns | MapNeeds.Hills;

    // ---- State (server-owned, replicated) ----------------------------------------------------------------

    public int HillIndex;
    public byte HillOwner = Game.NoTeam; // the one team on the hill, or NoTeam
    public bool HillContested;
    public double HillMoveIn = MoveSeconds; // seconds until the hill moves
    private double _hillMoveAt = -1, _hillAccum;

    /// <summary>The hill in play.</summary>
    public HillZone? ActiveHill(Game game) => game.Objectives.Hills.Count > 0 ? game.Objectives.Hills[HillIndex % game.Objectives.Hills.Count] : null;

    public static bool InHill(Vector3 feet, HillZone hill)
    {
        var d = new Vector2(feet.X - hill.Center.X, feet.Z - hill.Center.Z);
        // Height window measured from the ground, so a zone on a slope covers the whole slope (flat maps: ground = 0).
        var m = World.Map;
        var y = feet.Y - m.GroundHeight(feet.X, feet.Z) - (hill.Center.Y - m.GroundHeight(hill.Center.X, hill.Center.Z));
        return d.LengthSquared() <= hill.Radius * hill.Radius && y > -1f && y < 3f;
    }

    // ---- Lifecycle ----------------------------------------------------------------------------------------

    public override void OnReset(Game game)
    {
        HillIndex = 0;
        HillOwner = Game.NoTeam;
        HillContested = false;
        _hillMoveAt = -1;
        _hillAccum = 0;
        HillMoveIn = MoveSeconds;
    }

    public override void OnMapChanged(Game game)
    {
        HillIndex = 0;
        _hillMoveAt = -1;
    }

    public override void ServerTickPlaying(Game game, double now)
    {
        TickHill(game, now);
        TeamWinCheck(game, now);
    }

    private void TickHill(Game game, double now)
    {
        var hills = game.Objectives.Hills;
        if (hills.Count == 0)
            return;
        if (_hillMoveAt < 0)
            _hillMoveAt = now + MoveSeconds;
        if (now >= _hillMoveAt)
        {
            HillIndex = (HillIndex + 1) % hills.Count;
            _hillMoveAt = now + MoveSeconds;
            _hillAccum = 0;
            if (hills.Count > 1)
                game.ModeEvent("The hill has moved!", Sfx.ReconBeep);
        }
        HillMoveIn = Math.Max(0, _hillMoveAt - now);
        var hill = hills[HillIndex % hills.Count];

        bool blue = false, red = false;
        foreach (var p in game.Players.Values)
        {
            if (!p.Alive || p.Team > 1 || !InHill(p.Position, hill))
                continue;
            if (p.Team == 0)
                blue = true;
            else
                red = true;
        }
        HillContested = blue && red;
        var owner = blue == red ? Game.NoTeam : blue ? (byte)0 : (byte)1;
        if (owner != HillOwner && owner != Game.NoTeam)
            game.ModeEvent($"{GameModes.TeamNames[owner]} team has the hill", Sfx.HitTick);
        HillOwner = owner;
        if (owner == Game.NoTeam)
            return; // empty or contested: nobody scores
        _hillAccum += World.TickDt;
        while (_hillAccum >= 1)
        {
            _hillAccum -= 1;
            game.TeamScore[owner]++;
        }
    }

    // ---- Bots ---------------------------------------------------------------------------------------------

    /// <summary>Everyone heads for the hill, each to their own spot inside it (not stacked on the centre).</summary>
    public override Vector3? BotObjective(Game game, Player bot)
    {
        if (ActiveHill(game) is not { } hill)
            return null;
        var a = bot.Id * 2.4f;
        return hill.Center + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * hill.Radius * 0.45f;
    }

    // ---- Replicated state (7 bytes) --------------------------------------------------------------------------

    public override void WriteState(Game game, PacketWriter w) =>
        w.U8((byte)HillIndex).U8(HillOwner).U8(HillContested ? (byte)1 : (byte)0).F32((float)HillMoveIn);

    public override void ReadState(Game game, ref PacketReader r)
    {
        if (!r.Has(7))
            return;
        HillIndex = r.U8();
        HillOwner = r.U8();
        HillContested = r.U8() != 0;
        HillMoveIn = r.F32();
    }

    // ---- HUD ----------------------------------------------------------------------------------------------

    public override void HudLines(Game game, Player me, List<HudLine> lines, ref Vector3? pointAt)
    {
        if (ActiveHill(game) is not { } hill)
            return;
        lines.Add(HillContested ? new HudLine("HILL CONTESTED", HudOrange)
            : HillOwner < 2 ? new HudLine($"{GameModes.TeamNames[HillOwner].ToUpperInvariant()} HOLDS THE HILL", HudTeam(HillOwner))
            : new HudLine("HILL EMPTY", HudWhite));
        if (me.Alive && InHill(me.RenderPosition, hill))
            lines.Add(new HudLine("You're on the hill", HudGreen));
        else
            pointAt = hill.Center;
        var mv = Math.Max(0, (int)Math.Ceiling(HillMoveIn));
        lines.Add(new HudLine($"Hill moves in {mv / 60}:{mv % 60:00}", HudDim));
    }

    /// <summary>A glowing ring with a halo of light in the holder's colour (orange while contested) and a beam to find it from anywhere.</summary>
    public override void DrawObjectives(Renderer gfx, Game game, double now)
    {
        if (ActiveHill(game) is not { } hill)
            return;
        var color = HillContested
            ? new Vector3(1f, 0.55f, 0.15f) * (0.75f + 0.25f * MathF.Sin((float)now * 10f))
            : GameModes.TeamColor(HillOwner);
        ObjectiveRender.Ring(gfx, hill.Center, hill.Radius, color, 0.85f, 0.12f);
        ObjectiveRender.Halo(gfx, hill.Center, hill.Radius, HaloHeight, color, 0.42f);
        ObjectiveRender.Beam(gfx, hill.Center, 0.35f, 40f, color, 0.18f);
    }
}
