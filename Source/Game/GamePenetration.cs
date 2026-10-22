using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bullet penetration in the game (rules: <see cref="PenetrationTrace"/>, data: <see cref="MaterialPenetration"/>).
/// Server-authoritative: <see cref="ServerResolveShot"/> extends each pellet's ray through the thin or soft walls its
/// round is big enough for, against the same rewound player hitboxes as always; whatever it hits behind a wall takes
/// the reduced damage (<see cref="PenetrationTrace.FactorAt"/>; headshots still count). The shooter predicts the same
/// path (tracers and hit markers), and every client marks both sides of each wall from the shot's origin, sent after
/// the tracer ends when a shot went through something (<see cref="ShotPenetrated"/>; older clients ignore the extra
/// bytes). A map can switch it off with "penetration": false. Bots get no wallhacks: an Insane base of fire may put
/// suppressive fire through a thin wall only at a fresh last known spot (<see cref="BotSuppressThroughWall"/>).
/// </summary>
public sealed partial class Game
{
    /// <summary>Shot flag: some pellet went through a wall; the shot's origin (12 bytes) follows the tracer ends.</summary>
    public const byte ShotPenetrated = 64;

    /// <summary>Shot flag: something was hit through a wall (the shooter's hit marker shows it).</summary>
    public const byte ShotWallbang = 128;

    /// <summary>The last hit marker was for a hit through a wall (drawn hollow, see Program.cs).</summary>
    public bool HitMarkerWallbang;

    private readonly PenetrationTrace _pen = new();
    private readonly PenetrationTrace _botPen = new();
    private bool _shotPenetrated;
    private Vector3[]? _shotEnds;

    /// <summary>Is bullet penetration on for the map being played (default on; the map's "penetration": false turns it off)?</summary>
    public static bool PenetrationOn => World.Map.Penetration;

    /// <summary>
    /// After a pellet's first world hit at <paramref name="dist"/>: how far it really gets through what its round can pass
    /// (the same distance when it passes nothing). Fills <see cref="_pen"/> for <see cref="PenFactor"/>.
    /// </summary>
    private float PenetrateAfterHit(WeaponDef def, Vector3 o, Vector3 d, float dist)
    {
        var power = PenetrationOn && dist < World.Range - 0.01f ? MaterialPenetration.PowerOf(def) : 0f;
        if (power <= 0)
        {
            _pen.Clear();
            return dist;
        }
        var end = _pen.Run(World.Map, World.Physics, power, o, d, World.Range, MaterialPenetration.MaxSurfaces);
        if (_pen.Surfaces > 0)
            _shotPenetrated = true;
        return end;
    }

    /// <summary>The damage multiplier for a hit at <paramref name="dist"/> along the last pellet (1 unless it went through walls first).</summary>
    private float PenFactor(float dist) => _pen.FactorAt(dist);

    /// <summary>
    /// Breakable props and building doors/windows along the pellet's path: the original single check when it passed
    /// nothing; otherwise one per stretch between walls, each with the damage left on that stretch (a crate or door it
    /// went through is hit too, and glass it flies through after a wall breaks).
    /// </summary>
    private void PenetratedShotEffects(Player shooter, Vector3 o, Vector3 d, float dist, float damage, bool endOnWorld, double now)
    {
        var from = 0f;
        var factor = 1f;
        var piece = 0;
        for (var i = 0; i < _pen.Surfaces && _pen.SurfaceEnd[i] <= dist; i++)
        {
            while (piece < _pen.PieceCount && _pen.Pieces[piece].Surface < i)
                piece++;
            var front = piece < _pen.PieceCount ? _pen.Pieces[piece].In : _pen.SurfaceEnd[i];
            var start = o + d * from;
            ShotProps(shooter, start, d, MathF.Max(0, front - from), damage * factor, now); // the prop it went into
            ShotBuildings(shooter, start, d, MathF.Max(0, front - from), damage * factor, now); // the door it went through
            from = _pen.SurfaceEnd[i];
            factor = _pen.SurfaceFactor[i];
        }
        var o2 = o + d * from;
        if (endOnWorld)
            ShotProps(shooter, o2, d, MathF.Max(0, dist - from), damage * factor, now); // breakable props (GameProps.cs)
        ShotBuildings(shooter, o2, d, MathF.Max(0, dist - from), damage * factor, now); // building doors and window glass (GameBuildings.cs)
    }

    /// <summary>Client: reads a Shot packet's tracer ends and, if flagged, the shot's origin after them (no allocation).</summary>
    private ReadOnlySpan<Vector3> ReadShotEnds(ref PacketReader r, int n, byte flags, out Vector3? through)
    {
        var ends = _shotEnds ??= new Vector3[256];
        var count = 0;
        for (; count < n && count < ends.Length && r.Has(12); count++)
            ends[count] = r.Vec();
        through = (flags & ShotPenetrated) != 0 && r.Has(12) ? r.Vec() : null;
        if (through is { } t && !float.IsFinite(t.X + t.Y + t.Z))
            through = null;
        return ends.AsSpan(0, count);
    }

    /// <summary>A tracer that went through walls: impact marks on both sides of each (every client, from the same shot origin).</summary>
    private static void PenetrationMarks(Vector3? through, Vector3 to, WeaponDef def, byte owner, double now)
    {
        if (through is { } from)
            PenetrationTrace.OnPenetratedTracer(World.Map, from, to, owner, now, def.Pellets > 1, ImpactDecals.CalibreScale(def));
    }

    /// <summary>
    /// Bots: would a round from this bot's gun reach <paramref name="spot"/> (through whatever thin walls are in the way),
    /// with at least a quarter of its damage left? Only map geometry is looked at (no players), so it reveals nothing.
    /// </summary>
    internal bool BotWallbangLine(Player p, Vector3 spot)
    {
        if (!PenetrationOn)
            return false;
        var def = p.WeaponDef;
        var power = MaterialPenetration.PowerOf(def);
        var d = spot - p.Eye;
        var dist = d.Length();
        if (power <= 0 || dist < 0.5f || dist > World.Range)
            return false;
        d /= dist;
        var end = _botPen.Run(World.Map, World.Physics, power, p.Eye, d, dist, MaterialPenetration.MaxSurfaces);
        return end >= dist - 0.01f && _botPen.Surfaces > 0 && _botPen.FactorAt(dist) >= 0.25f;
    }

    /// <summary>
    /// An Insane base of fire with a Suppress order may shoot at the target's last known spot through a thin wall its gun
    /// goes through, but only while the contact is fresh (the order's Fire flag: a recent sighting or report). No known
    /// spot, no shots: bots never fire at someone they can't see without one.
    /// </summary>
    internal bool BotSuppressThroughWall(Player p, in SquadOrder o) =>
        o.Kind == SquadOrderKind.Suppress && o.Fire && BotWallbangLine(p, o.Look);

    // ---- Self-test hooks ----

    /// <summary>The trace of the last pellet the server (or the shooter's prediction) resolved.</summary>
    public PenetrationTrace SelfTestLastPen => _pen;

    /// <summary>Fires <paramref name="def"/> from <paramref name="origin"/> along <paramref name="dir"/> (no spread) as <paramref name="shooter"/>, at a given view tick.</summary>
    public void SelfTestPenShot(Player shooter, WeaponDef def, Vector3 origin, Vector3 dir, double viewTick, double now)
    {
        shooter.ShotClock = 0;
        ServerResolveShot(shooter, def, origin, dir, 0, 1, viewTick, now);
    }

    /// <inheritdoc cref="BotWallbangLine"/>
    public bool SelfTestBotWallbang(Player p, Vector3 spot) => BotWallbangLine(p, spot);

    /// <inheritdoc cref="BotSuppressThroughWall"/>
    public bool SelfTestBotSuppressThroughWall(Player p, SquadOrder o) => BotSuppressThroughWall(p, o);

    /// <summary>The bytes of the last Shot packet the server wrote (the shared writer's buffer), for the protocol check.</summary>
    public byte[] SelfTestLastPacket => _w.Span.ToArray();
}

public sealed partial class Map
{
    /// <summary>Bullets go through thin walls on this map (the map file's top-level "penetration"; default true).</summary>
    public bool Penetration = true;
}
