using System.Numerics;

namespace CallOfSlop;

/// <summary>Axis-aligned box used for walls, floor, cover and hitboxes.</summary>
public readonly record struct Box(Vector3 Center, Vector3 Half)
{
    /// <summary>This box grown by <paramref name="r"/> on every side (a fat bullet's hit radius); itself for r <= 0.</summary>
    public Box Grown(float r) => r <= 0f ? this : new(Center, Half + new Vector3(r));

    public Vector3 Min => Center - Half;
    public Vector3 Max => Center + Half;

    /// <summary>Slab-method ray test. Returns distance along the (normalised) ray, or null.</summary>
    public float? Raycast(Vector3 origin, Vector3 dir, float maxDist)
    {
        float tMin = 0f, tMax = maxDist;
        for (var axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = dir[axis], lo = Min[axis], hi = Max[axis];
            if (MathF.Abs(d) < 1e-6f)
            {
                if (o < lo || o > hi)
                    return null;
                continue;
            }
            var inv = 1f / d;
            var t1 = (lo - o) * inv;
            var t2 = (hi - o) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax)
                return null;
        }
        return tMin;
    }
}

/// <summary>Buttons held during one simulation tick.</summary>
[Flags]
public enum Buttons : byte
{
    None = 0,
    Forward = 1,
    Back = 2,
    Left = 4,
    Right = 8,
    Jump = 16,
    Sprint = 32,
    Crouch = 64,
    Prone = 128,
}

public enum Stance : byte
{
    Stand,
    Crouch,
    Prone,
}

/// <summary>One tick of player input. The client sends these; the server replays them.</summary>
public struct InputCmd
{
    public uint Seq;
    public Buttons Buttons;
    public float Yaw;
    public float Pitch;
    public byte Weapon; // weapon id currently held
    public byte SpeedPct; // movement speed in % (weapon mobility, aiming); the server clamps it
    public uint Tick; // server tick this command is simulated at, for mover poses (0 = MoverPhysics.Clock; see Movers.cs)
}

/// <summary>Everything movement depends on. Client prediction and the server run the same code on it.</summary>
public struct MoveState
{
    public Vector3 Position; // feet
    public Vector3 Velocity;
    public bool Grounded;
    public Stance Stance;
    public byte MantleTicks; // > 0 while climbing over a ledge or through a window
    public Vector3 MantleTo; // where the climb ends (feet)
    public PerkId MovePerk; // the movement perk being carried (set by the server on spawn)
    public byte BoostTicks; // > 0 while Head Start / Afterburner speed is active
    public byte KnockTicks; // > 0 while staggered by a blast push: weak steering (Knockback.cs; not in snapshots, the client works it out)
}

/// <summary>Shared rules: tick rate, player physics, movement and ray casts against the current map.</summary>
public static partial class World
{
    // ---- Simulation clock ---------------------------------------------------
    public const int TickRate = 60; // fixed simulation ticks per second (client and server)
    public const float TickDt = 1f / TickRate;
    public const int SnapshotEvery = 2; // server sends a snapshot every 2 ticks (30 Hz)
    public const int MaxRewindTicks = 24; // lag compensation rewinds at most 400 ms

    // ---- Map -----------------------------------------------------------------
    /// <summary>The map being played. Client and server always load the same one (sent in the Welcome).</summary>
    public static Map Map { get; private set; } = Maps.All[0];

    public static void SetMap(Map map)
    {
        using var perf = Perf.Measure(PerfArea.Loading); // the perf window (Source/Perf)
        map.RestoreProps(); // a map always starts with its breakable props whole
        map.Destruction?.StartGraph(); // destructible buildings: their support graph, on a worker thread (MapDestruction.cs)
        Map = map;
        ApplyGravitySetting(); // the host's gravity setting, within this map's floor (GameGravity.cs)
        GravityFrames.Use(map.Gravity); // which way is up (Source/Physics/GravityFrames.cs): straight up except on planet and gravity-zone maps
        _physics = null;
        Ragdolls.Clear(); // bodies belong to the old map (Source/Render/Ragdolls.cs)
        Gibs.Clear(); // and so do the gibs (Source/Props/Gibs.cs)
    }

    /// <summary>A breakable prop's collider box changed (it broke): the physics world switches that box off.</summary>
    public static void SolidChanged(Map map, int index)
    {
        if (ReferenceEquals(map, Map) && _physics is { } p && p.IsFor(map))
            p.UpdateBox(index, map.Solid[index]);
        Ragdoll.WorldGeneration++; // ragdolls gather their colliders again
        LegacyCollision.SolidChanged(map, index);
        DecalEvents.SolidChanged(map, index); // impacts on it fade, map decals on it hide (Source/Decals)
    }

    /// <summary>Boxes of the current map grew back (broken props restored): the physics world reads them again.</summary>
    public static void InvalidateGrid(Map map)
    {
        if (ReferenceEquals(map, Map) && _physics is { } p && p.IsFor(map))
            p.RefreshBoxes();
        Ragdoll.WorldGeneration++;
        LegacyCollision.Invalidate(map);
        DecalEvents.BoxesRestored(map); // map decals on restored boxes show again
    }

    private static PhysicsWorld? _physics;

    /// <summary>
    /// The current map's collision world (built on first use): one BVH over every static collider, the terrain and the
    /// movers. Every collision question goes through it (see <see cref="Query"/>).
    /// </summary>
    public static PhysicsWorld Physics => _navBuildPhysics ?? (_physics is { } p && p.IsFor(Map) ? p : _physics = BuildPhysics());

    private static PhysicsWorld BuildPhysics()
    {
        using var perf = Perf.Measure(PerfArea.Loading); // the perf window (Source/Perf)
        return new PhysicsWorld(Map);
    }

    /// <summary>
    /// While the nav graph builds a slice (this thread only): the map as at the start of a match
    /// (<see cref="PhysicsWorld.AsBuilt"/>), so props broken and doors opened meanwhile never change the graph. Null otherwise.
    /// </summary>
    [ThreadStatic]
    private static PhysicsWorld? _navBuildPhysics;

    /// <summary>Is a nav graph slice running on this thread against the map as built (<see cref="UseNavBuildPhysics"/>)?</summary>
    public static bool NavBuildAsBuilt => _navBuildPhysics != null;

    /// <summary>Points <see cref="Physics"/> at <paramref name="world"/> on this thread (null: back to the live world). Returns the previous one.</summary>
    public static PhysicsWorld? UseNavBuildPhysics(PhysicsWorld? world)
    {
        var previous = _navBuildPhysics;
        _navBuildPhysics = world;
        return previous;
    }

    /// <summary>The current map's world queries: rays, shape casts, overlaps (<see cref="IWorldQuery"/>).</summary>
    public static IWorldQuery Query => Physics;

    /// <summary>The movement code every player runs (classic or swept, from the server's physics setting).</summary>
    public static IMover Mover { get; set; } = ClassicMover.Instance;

    /// <summary>Which movement the server runs (and clients predict with). Set from Settings on the host, from the Welcome on clients.</summary>
    public static PhysicsMode PhysicsMode
    {
        get => Mover.Mode;
        set => Mover = value == PhysicsMode.Swept ? SweptMover.Instance : ClassicMover.Instance;
    }

    public const float StepHeight = 0.55f; // walk up anything this tall (stairs are 0.4 m)

    /// <summary>
    /// Boxes, oriented boxes and meshes steeper than this can't be walked up (the swept mover's slopes). Terrain has its own,
    /// smoother rules: <see cref="SlopeRules"/> (walkable to 45 degrees over the footprint, slower uphill, grip, capped slides).
    /// </summary>
    public const float MaxSlopeDeg = 42f;

    /// <summary>cos(<see cref="MaxSlopeDeg"/>): ground whose normal points up less than this is too steep.</summary>
    public const float MinWalkNormalY = 0.7431448f;

    // ---- Player ------------------------------------------------------------
    public const float PlayerRadius = 0.4f;
    public const float PlayerHeight = 1.8f; // standing
    public const float EyeHeight = 1.6f;
    public const float HeadStart = 1.4f; // standing: hits above this are headshots

    // ---- Stances -------------------------------------------------------------
    public static float HeightOf(Stance s) => s switch { Stance.Crouch => 1.2f, Stance.Prone => 0.6f, _ => PlayerHeight };
    public static float EyeOf(Stance s) => s switch { Stance.Crouch => 1.05f, Stance.Prone => 0.45f, _ => EyeHeight };
    public static float SpeedOf(Stance s) => s switch { Stance.Crouch => 0.5f, Stance.Prone => 0.22f, _ => 1f };
    public static float SpreadOf(Stance s) => s switch { Stance.Crouch => 0.75f, Stance.Prone => 0.5f, _ => 1f };
    public static float RecoilOf(Stance s) => s switch { Stance.Crouch => 0.85f, Stance.Prone => 0.6f, _ => 1f };
    public const float WalkSpeed = 6f;
    public const float SprintSpeed = 13.5f; // 2.25x walking (was 9 m/s before protocol 10)
    public const float JumpSpeed = 6f;
    public const float Gravity = 18f;

    /// <summary>
    /// The host's gravity setting as a multiplier (<see cref="Game.GravityPercent"/>: 0.25 to 2, default 1): every field of
    /// the map (straight down, a planet, every zone) and everything that falls (players, grenades, launcher rounds, ragdolls,
    /// gibs, debris) is scaled by it. It multiplies the end of every gravity expression, so at 1 every number is exactly what
    /// it was. Bots' nav graphs are built at 1 (NavGraph.MoverWalk), the same for every setting.
    /// </summary>
    public static float GravityScale = 1f;

    /// <summary>The host's gravity setting in percent (<see cref="Game.GravityPercent"/>); <see cref="GravityScale"/> follows it and the map.</summary>
    public static int GravitySetting = 100;

    /// <summary>
    /// Sets <see cref="GravityScale"/> from the host's setting and the current map: never under the map's own floor
    /// (<see cref="Map.MinGravityPercent"/>: on a small planet too little pull would let a sprint jump fly off it). Every PC
    /// works it out the same way from the setting and the map file.
    /// </summary>
    public static void ApplyGravitySetting()
    {
        var percent = Math.Max(GravitySetting, Map.MinGravityPercent);
        GravityScale = percent == 100 ? 1f : percent / 100f;
    }
    /// <summary>
    /// Full health for players and bots (was 100; raised to 120 so fights last a little longer). It rides in the
    /// snapshot as one byte, so keep it at 255 or less. Zombies have their own health (ZombiesRules).
    /// </summary>
    public const int MaxHealth = 120;

    /// <summary>
    /// One-shot blasts tuned against the old 100 health (explosive props and mines, kill-streak strikes, the Hunter
    /// Drone and gunship rounds) do this much more to players, so their kill radius stays the same at
    /// <see cref="MaxHealth"/>. Machines, props, doors and zombies still take the tuned damage. (Frag and Impact grenades
    /// have their own player and object damage in Throwables.)
    /// </summary>
    public const float BlastPlayerScale = MaxHealth / 100f;

    /// <summary>
    /// <paramref name="percent"/>% of <see cref="MaxHealth"/>, rounded. Health thresholds (bots retreating, the HUD's
    /// low-health colour) are written as percentages so they follow <see cref="MaxHealth"/>.
    /// </summary>
    public static int HealthAt(int percent) => (percent * MaxHealth + 50) / 100;

    public const float RespawnDelay = 3f;

    // ---- Shooting (per-weapon stats live in Weapons.cs) -------------------------
    public const float Range = 200f;
    public const float AdsSpeedMult = 0.7f;

    public static Vector3 Forward(float yaw, float pitch) =>
        new(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), -MathF.Cos(pitch) * MathF.Cos(yaw));

    public static Vector3 FlatForward(float yaw) => new(MathF.Sin(yaw), 0, -MathF.Cos(yaw));

    public static Vector3 FlatRight(float yaw) => new(MathF.Cos(yaw), 0, MathF.Sin(yaw));

    /// <summary>
    /// Advances one player by exactly one tick of input, with the current <see cref="Mover"/>. This must stay
    /// deterministic: the client predicts with it and replays it during reconciliation, and the server runs it authoritatively.
    /// </summary>
    public static void Simulate(ref MoveState s, in InputCmd cmd) => (GravityFrames.Curved ? SweptMover.Instance : Mover).Simulate(ref s, cmd); // a planet always uses the capsule mover (any "up")

    public const float MantleReach = 1.7f; // highest ledge you can climb, above your feet

    public static Box Hitbox(Vector3 feet) => Hitbox(feet, Stance.Stand, 0);

    /// <summary>Damage box for a player. Prone players are a long, low box stretched along their facing.</summary>
    public static Box Hitbox(Vector3 feet, Stance stance, float yaw)
    {
        if (stance != Stance.Prone)
        {
            var h = HeightOf(stance);
            return new(feet + new Vector3(0, h / 2, 0), new Vector3(PlayerRadius, h / 2, PlayerRadius));
        }
        var f = FlatForward(yaw);
        // Matches the drawn body: from 0.8 m behind the feet to 1.0 m in front (head and gun).
        var half = new Vector3(MathF.Abs(f.X) * 0.9f + MathF.Abs(f.Z) * 0.3f, 0.3f, MathF.Abs(f.Z) * 0.9f + MathF.Abs(f.X) * 0.3f);
        return new(feet + f * 0.1f + new Vector3(0, 0.3f, 0), half);
    }

    /// <summary>
    /// Where a ray first hits a player's hit box (<see cref="Hitbox(Vector3, Stance, float)"/>), or null. On a planet the box
    /// stands along the local up, so the ray is turned into the body's frame first; elsewhere this is exactly the box test.
    /// </summary>
    public static float? RayHitsBody(Vector3 feet, Stance stance, float yaw, Vector3 origin, Vector3 dir, float maxDist)
    {
        GravityFrames.RayToLocal(feet, ref origin, ref dir);
        return Hitbox(feet, stance, yaw).Raycast(origin, dir, maxDist);
    }

    /// <summary>
    /// A world box round a player's body: their hit box, or on a planet the axis-aligned box round the hit box standing
    /// along the local up (a little bigger; for HUD tags and effects, never for damage).
    /// </summary>
    public static Box BodyBounds(Vector3 feet, Stance stance, float yaw)
    {
        var hb = Hitbox(feet, stance, yaw);
        if (!GravityFrames.Curved)
            return hb;
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var corner = hb.Center + new Vector3((i & 1) != 0 ? hb.Half.X : -hb.Half.X, (i & 2) != 0 ? hb.Half.Y : -hb.Half.Y, (i & 4) != 0 ? hb.Half.Z : -hb.Half.Z);
            var w = GravityFrames.PointToWorld(feet, corner);
            lo = Vector3.Min(lo, w);
            hi = Vector3.Max(hi, w);
        }
        return new Box((lo + hi) * 0.5f, (hi - lo) * 0.5f);
    }

    /// <summary>Did a hit at <paramref name="point"/> land on the head?</summary>
    public static bool IsHeadshot(Vector3 feet, Stance stance, float yaw, Vector3 point)
    {
        point = GravityFrames.PointToLocal(feet, point); // (a planet: in the body's upright frame)
        var rel = point - feet;
        return stance switch
        {
            Stance.Crouch => rel.Y >= 0.85f,
            Stance.Prone => rel.Y >= 0.2f && Vector3.Dot(new Vector3(rel.X, 0, rel.Z), FlatForward(yaw)) >= 0.55f, // head is out front
            _ => rel.Y >= HeadStart,
        };
    }

    /// <summary>Nearest distance at which a ray hits static geometry, boxes, terrain or a mover at its current pose (or maxDist if nothing). Shorthand for <see cref="Query"/>.</summary>
    public static float RaycastWorld(Vector3 origin, Vector3 dir, float maxDist) => Physics.Raycast(origin, dir, maxDist, QueryFilter.All);

    /// <summary>
    /// Ray cast for bot navigation: static geometry, with each mover's whole sweep as a solid pillar (so nav graphs
    /// never route through moving platforms and don't depend on where they happen to be).
    /// </summary>
    public static float RaycastNav(Vector3 origin, Vector3 dir, float maxDist) => Physics.Raycast(origin, dir, maxDist, QueryFilter.Navigation);

    /// <summary>Nearest distance at which a ray hits a box (terrain ignored), or maxDist.</summary>
    public static float RaycastBoxes(Vector3 origin, Vector3 dir, float maxDist) => Physics.Raycast(origin, dir, maxDist, QueryFilter.BoxesOnly);

    /// <summary>
    /// Could a player walk in a straight line from <paramref name="from"/> to <paramref name="to"/> (feet positions)?
    /// No box in the way at knee (0.5 m) or chest (1.3 m) height and, on terrain, no stretch players can't walk by
    /// <see cref="SlopeRules"/> (steeper than its limit up or down, so the answer is the same both ways, apart from a
    /// single lip or narrow steep patch the movers' grip gets over) and no hill in between.
    /// Meant for bot navigation (waypoint picking, nav graphs). Pure geometry: smoke etc. is up to the caller.
    /// </summary>
    public static bool CanWalkStraight(Vector3 from, Vector3 to)
    {
        if (GravityFrames.Curved)
        {
            if (GravityFrames.Point != null)
                return CanWalkCurved(from, to); // a planet: along the surface (NavSphere.cs)
            if (!GravityFrames.PlainUpAt(from) || !GravityFrames.PlainUpAt(to))
                return CanWalkField(from, to); // a gravity zone with its own up: along the ground there (NavSphere.cs)
            if (!GravityFrames.PlainUpAlong(from, to))
                return false; // the line crosses a zone where down is another way: no walking through it
        }
        var knee = new Vector3(0, 0.5f, 0);
        var chest = new Vector3(0, 1.3f, 0);
        var terrain = Map.TerrainCollision?.Layer(from.Y); // in a cave: its floor
        if (Map.Movers.Count > 0 && !ClearOfMovers(from + knee, to + knee))
            return false; // bots keep out of moving platforms' paths
        if (terrain == null)
            return Clear(from + knee, to + knee) && Clear(from + chest, to + chest);
        // Off the ground (on a roof, a bridge): straight lines only, terrain included in the ray casts.
        if (from.Y - terrain.HeightAt(from.X, from.Z) > 0.6f || to.Y - terrain.HeightAt(to.X, to.Z) > 0.6f)
            return ClearAll(from + knee, to + knee) && ClearAll(from + chest, to + chest);

        var flat = new Vector2(to.X - from.X, to.Z - from.Z);
        var len = flat.Length();
        var step = MathF.Min(0.5f, terrain.Data.Cell * 0.5f);
        var n = Math.Max(1, (int)MathF.Ceiling(len / step));
        var run = len / n;
        var maxRise = run * SlopeRules.NavTanLimit;
        var prev = new Vector3(from.X, terrain.HeightAt(from.X, from.Z), from.Z);
        var segStart = prev;
        var steep = 0;
        var gripSamples = Math.Max(1, (int)(0.5f / MathF.Max(run, 1e-3f))); // a steep stretch up to half a metre (the movers' grip)
        for (var k = 1; k <= n; k++)
        {
            var t = k / (float)n;
            var x = from.X + flat.X * t;
            var z = from.Z + flat.Y * t;
            var p = new Vector3(x, terrain.HeightAt(x, z), z);
            var rise = MathF.Abs(p.Y - prev.Y);
            if (rise > maxRise || !SlopeRules.NavWalkable(terrain, x, z))
            {
                if (rise > StepHeight || ++steep > gripSamples)
                    return false; // a cliff, a drop or a slope too steep to walk
            }
            else
                steep = 0;
            // Boxes: check the path in pieces of about 4 m that follow the ground.
            if (k % 8 == 0 || k == n)
            {
                if (!Clear(segStart + knee, p + knee) || !Clear(segStart + chest, p + chest))
                    return false;
                segStart = p;
            }
            prev = p;
        }
        return true;

        static bool Clear(Vector3 a, Vector3 b)
        {
            var d = b - a;
            var dist = d.Length();
            return dist < 0.01f || RaycastBoxes(a, d / dist, dist) >= dist - 0.05f;
        }

        static bool ClearOfMovers(Vector3 a, Vector3 b)
        {
            var d = b - a;
            var dist = d.Length();
            return dist < 0.01f || Physics.Raycast(a, d / dist, dist, new QueryFilter(QueryLayers.MoverSweeps)) >= dist;
        }

        static bool ClearAll(Vector3 a, Vector3 b)
        {
            var d = b - a;
            var dist = d.Length();
            return dist < 0.01f || RaycastWorld(a, d / dist, dist) >= dist - 0.05f;
        }
    }

    /// <summary>Would a player standing at <paramref name="feet"/> overlap any box, or be under the terrain? (The classic body check.)</summary>
    public static bool Blocked(Vector3 feet, float height = PlayerHeight) => Physics.Blocked(feet, height);

    /// <summary>Would a player standing at <paramref name="feet"/> overlap any box (terrain ignored)?</summary>
    public static bool BlockedByBoxes(Vector3 feet, float height = PlayerHeight) => Physics.BlockedByBoxes(feet, height);

    public static Vector3 ClampToArena(Vector3 feet)
    {
        if (GravityFrames.Point is { } planet)
            return ClampToSky(feet, planet);
        feet.X = Math.Clamp(feet.X, -Map.HalfX + PlayerRadius, Map.HalfX - PlayerRadius);
        feet.Z = Math.Clamp(feet.Z, -Map.HalfZ + PlayerRadius, Map.HalfZ - PlayerRadius);
        if (Map.Boundary is { } boundary)
            feet = boundary.ClampFeet(feet, PlayerRadius); // a shaped map: held inside its outline (MapEdges.cs)
        if (Map.TerrainCollision is { } terrain)
        {
            // Never below the ground: nobody falls out of a terrain map.
            feet.Y = Math.Clamp(feet.Y, terrain.Data.MinHeight - 5, terrain.Data.MaxHeight + 60);
            feet.Y = MathF.Max(feet.Y, terrain.GroundBelow(feet.X, feet.Z, feet.Y)); // (in a cave: its floor)
        }
        else
            feet.Y = Math.Clamp(feet.Y, -5, 60);
        return feet;
    }

    /// <summary>
    /// On a planet: never further from the centre than the map's half size (the sky above the planet ends there). The
    /// ground keeps players up; nothing else to clamp.
    /// </summary>
    public static Vector3 ClampToSky(Vector3 feet, PointGravity planet)
    {
        var d = feet - planet.Center;
        var len = d.Length();
        var max = MathF.Max(Map.HalfX, Map.HalfZ);
        if (len > max)
            feet = planet.Center + d / len * max;
        return feet;
    }

    public static float WrapAngle(float a)
    {
        // Far outside one turn, step down with a remainder first: subtracting 2 pi from 1e34 changes nothing, so the loops
        // below would never end (a client sending such a yaw froze the host). Normal angles take the same path as before.
        if (MathF.Abs(a) > 1000f)
            a = float.IsFinite(a) ? MathF.IEEERemainder(a, MathF.Tau) : 0f;
        while (a > MathF.PI)
            a -= MathF.Tau;
        while (a < -MathF.PI)
            a += MathF.Tau;
        return a;
    }

    public static float LerpAngle(float a, float b, float t) => a + WrapAngle(b - a) * t;
}

/// <summary>Where a player was (and how they stood) at one server tick.</summary>
public struct HistoryEntry
{
    public Vector3 Position;
    public Stance Stance;
    public float Yaw;

    /// <summary>
    /// The server tick this entry was recorded at (0: not a recorded tick, e.g. a spawn or teleport fill). The ring is
    /// reused every <see cref="Player.HistorySize"/> ticks, so a slot whose tick doesn't match the one asked for is stale.
    /// </summary>
    public uint Tick;
}

/// <summary>A remote player's state at one server tick, buffered on clients for interpolation.</summary>
public struct Snap
{
    public Stance Stance;
    public uint Tick;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw;
    public float Pitch;
    public bool Grounded, Mantling; // for the leg animation (Source/Render/LegAnimator.cs)
    public ushort SeatBody; // Sandbox: in this construct's seat (0: not seated); Yaw is then relative to the seat's facing
    public byte SeatPart;
}

/// <summary>State for one player.</summary>
public sealed class Player
{
    public byte Id;
    public string Name = "";
    public BotBrain? Bot; // server-side AI, null for people
    public MoveState Sim; // authoritative on the server, predicted for the local player on a client
    public float Yaw;
    public float Pitch;
    public int Health = World.MaxHealth;
    public uint Blood; // blood marks: hit parts, generation, last hit direction (BloodMarks, BodyBlood.cs); server-owned, replicated
    public bool Alive = true;
    public ushort Kills;
    public ushort Deaths;
    public byte Team = Game.NoTeam; // team modes: 0 (Blue) or 1 (Red); replicated in the mode state
    public byte TeamChoice = Game.NoTeam; // team modes: the team this player picked (NoTeam: Auto, autobalance may move them); replicated (TeamPick.cs)
    public double TeamSwitchAt = -100; // when this player last changed team by choice (the switch cooldown, TeamPick.cs)
    public byte GunLevel; // Gun Game: step on the weapon ladder (replicated)
    public bool Ready; // warmup: ready for the match (replicated; bots count as ready)
    public byte ShieldCharges; // Last Stand: bullet hits the shield still blocks (server-owned, replicated in the snapshot)
    public bool Regenerating; // health is coming back right now (server-owned, replicated in the snapshot)
    public byte FrenzyLevel; // kill frenzy 0-6 (server-owned, replicated in the snapshot's slow fields; Source/Game/Frenzy/FrenzyServer.cs)
    public WeaponClass FrenzyClass; // the class of the weapon that got this player's most recent kill (server-owned, replicated)
    public bool FrenzyBuffsOn; // the host's "Frenzy buffs" setting, replicated with FrenzyClass (Source/Game/Frenzy/FrenzyBuffs.cs)
    public FrenzyMeterCore? FrenzyHeat; // server only: this player's heat model (FrenzyServer.cs)
    public double ShieldFlashTime = -100; // client: when the shield last blocked a hit (the shimmer)

    // Loadout and the weapon in hand (replicated so others see and hear the right gun)
    public byte Primary = Weapons.DefaultPrimary.Id;
    public byte Secondary = Weapons.DefaultSecondary.Id;
    public byte Weapon = Weapons.DefaultPrimary.Id;
    public uint PrimaryKit, SecondaryKit; // attachments on each gun (replicated)
    public PerkSet Perks = PerkSet.Empty; // replicated

    // Cosmetics (prestige + hats, Source/Cosmetics/Cosmetics.cs): never used for gameplay. Bots: always 0 / None.
    public byte Prestige, Level; // as the player reported them in their Hello (untrusted, only shown and used to validate the hat); in the roster
    public HatId Hat; // the hat they wear (set from the loadout once Cosmetics.IsUnlocked(hat, Prestige) and human); in the roster
    public PlayerLook Look; // the look picked on the APPEARANCE screen (Source/Cosmetics/Appearance.cs; bots: Auto); in the roster
    public ulong SteamId; // the Steam ID they reported (0: not on Steam); cosmetic (Steam name and avatar in the menus), never trusted (GameSteam.cs)

    // Throwables (server-authoritative counts, refilled on spawn)
    public ThrowableKind Grenade, Equipment, WantGrenade, WantEquipment;
    public int GrenadesLeft, EquipmentLeft;
    public double NextThrow;
    public WeaponDef WeaponDef => DefFor(Weapon);

    /// <summary>A weapon's stats with this player's attachments on it.</summary>
    public WeaponDef DefFor(byte weaponId) =>
        weaponId == Primary && BuiltPrimary != null ? BuiltPrimary
        : weaponId == Secondary && BuiltSecondary != null ? BuiltSecondary
        : ExtraDef(weaponId) is { } extra ? extra
        : Attachments.Apply(Weapons.Get(weaponId), weaponId == Primary ? PrimaryKit : weaponId == Secondary ? SecondaryKit : 0);

    /// <summary>Workbench Range (Zombies preview): built weapons used in place of the loadout's guns. Host-local, never replicated.</summary>
    public WeaponDef? BuiltPrimary, BuiltSecondary;

    /// <summary>Workbench Range / Zombies: built weapons for slots 3 and 4 (used once <see cref="ExtraSlots"/> is unlocked). Host-local.</summary>
    public readonly WeaponDef?[] BuiltExtra = new WeaponDef?[2];

    /// <summary>Zombies weapon-slot upgrade (2 -> 4 slots), bought on the map once per run (server-owned, sent in <see cref="Msg.RunState"/>).</summary>
    public bool ExtraSlots;

    /// <summary>Workbench Range / Zombies: the built melee weapon used on the melee key (null = the standard knife/butt strike). Host-local.</summary>
    public MeleeDef? MeleeBuild;

    /// <summary>Zombies: the grenade built in the grenade slot (null = the rules' stock grenade). Every client knows everyone's (Msg.ZBuild slot 5), so built grenades fly the same everywhere.</summary>
    public GrenadeDef? GrenadeBuild;

    /// <summary>Stunned by a melee build until then (bots stand still; zombies later).</summary>
    public double StunnedUntil;

    /// <summary>Server: a burn or bleed from special ammo (AmmoEffectsPlayers.cs): until when, the next tick, damage a tick, who lit it and with what.</summary>
    public double BurnUntil, BurnNext;

    public float BurnPerTick;
    public byte BurnBy = byte.MaxValue, BurnKillId;

    /// <summary>Sprinting right now, and ticks left of the after-sprint spread recovery (Source/Game/SprintSpread.cs). Server and local prediction.</summary>
    public bool Sprinting;
    public int SprintRecoverTicks;

    /// <summary>The built weapon in slot 3 or 4 with this id, if the slots are unlocked.</summary>
    public WeaponDef? ExtraDef(byte weaponId) =>
        !ExtraSlots ? null : BuiltExtra[0]?.Id == weaponId ? BuiltExtra[0] : BuiltExtra[1]?.Id == weaponId ? BuiltExtra[1] : null;

    public bool Owns(byte weaponId) => weaponId == Primary || weaponId == Secondary || ExtraDef(weaponId) != null;

    public Vector3 Position
    {
        get => Sim.Position;
        set => Sim.Position = value;
    }

    /// <summary>Sim position one tick ago, so rendering can blend between ticks at any frame rate.</summary>
    public Vector3 PrevPosition;

    // What actually gets drawn this frame (tick-blended, interpolated or error-smoothed).
    public Vector3 RenderPosition;
    public float RenderYaw;
    public float RenderPitch;

    /// <summary>Velocity being drawn (interpolated for remote players), plus on-ground / mantling: drives the legs (LegAnimator).</summary>
    public Vector3 RenderVelocity;
    public bool RenderGrounded = true, RenderMantling;

    // Sandbox: sitting in a construct's seat (GameSandbox.cs, every frame on every machine). The body is drawn in the
    // seat's frame (RenderSeatRot at RenderPosition, the seat's "at" point) in the seated pose for the seat type; the
    // upper body and head turn by RenderLookYaw (the look relative to the seat's facing); RenderYaw is the look in the world.
    public SeatKind RenderSeat;
    public SeatKind RenderSeatWas; // the seat type while last alive (dying seated: the ragdoll starts seated, limp; Ragdolls.cs)
    public Quaternion RenderSeatRot = Quaternion.Identity;
    public float RenderLookYaw;
    public Vector3 RenderWheelR, RenderWheelL; // a driver's hands on the steering wheel (world), and the rim's turn
    public Quaternion RenderWheelRot = Quaternion.Identity;

    /// <summary>Client, a remote player: the seat the interpolated snapshots put them in (0: none), whose yaw is then relative to it.</summary>
    public ushort SnapSeatBody;

    public byte SnapSeatPart;

    // ---- Server only --------------------------------------------------------
    /// <summary>Most commands <see cref="InputQueue"/> holds (Game.cs drops the oldest past it).</summary>
    public const int MaxQueuedInputs = 60;

    // At its full size from the start: growing it inside a match (a backlog after a hitch) allocated in Net receive
    // (the "Frame allocations" failures on Windows CI, 2026-10-30).
    public readonly List<InputCmd> InputQueue = new(MaxQueuedInputs + 1);
    public uint LastProcessedSeq;
    public uint HighestQueuedSeq;
    public InputCmd LastInput;
    public int StarvedTicks;
    public bool Buffering = true;
    public double RespawnAt;
    public double LastShotTime = -10;
    public double ShotClock; // fire-rate budget (see Game.ServerResolveShot)
    public readonly double[] GroupShotClock = new double[ReceiverFire.Max]; // per receiver, for builds that fire them at their own rates (GameFireGroups.cs)
    public double LastCombatTime = -100; // last time they shot or were shot (for instant loadout swaps)
    public byte WantPrimary = Weapons.DefaultPrimary.Id; // requested loadout, applied on (re)spawn
    public byte WantSecondary = Weapons.DefaultSecondary.Id;
    public uint WantPrimaryKit, WantSecondaryKit;
    // The guns this person picked for the multiplayer modes (server). Zombies replaces the Want* guns with its built
    // ones; a match in another mode after a Zombies run (BACK TO LOBBY, a mode change) puts these back (ZombiesMode.cs).
    public byte MpPrimary = Weapons.DefaultPrimary.Id, MpSecondary = Weapons.DefaultSecondary.Id;
    public uint MpPrimaryKit, MpSecondaryKit;
    public PerkSet WantPerks = PerkSet.Empty;
    public bool LastStandUsed; // Last Stand saves you once per life
    public uint AfterburnerReadyTick; // server tick from which a kill can fire Afterburner again (Perks.AfterburnerCooldownTicks)
    public double ShieldUntil; // Last Stand: when the shield runs out even if it still has charges
    public double LastHurtTime = -100; // last time they took or dealt damage (health regen waits for this)
    public float RegenCarry; // health regen: fraction of a point owed
    public double ImmuneUntil; // can't be hurt at all until then (smoke test only)
    public double NextMelee;
    public bool WantsRespawn; // people respawn when they click Respawn; bots respawn on their own
    public uint SpawnTick;
    public StreakState Streak = new(); // kill streaks (server-owned; a client gets its own copy)
    public const int HistorySize = 128;
    public readonly HistoryEntry[] History = new HistoryEntry[HistorySize]; // per tick, for lag compensation

    // ---- Client only (remote players) ---------------------------------------
    /// <summary>Most snapshots <see cref="Snaps"/> holds (GameSnapshots.cs drops the oldest past it).</summary>
    public const int MaxSnaps = 64;

    // At its full size from the start, like InputQueue: a burst of snapshots after a stall mustn't grow it in Net receive.
    public readonly List<Snap> Snaps = new(MaxSnaps + 1);

    public Stance RenderStance; // stance being drawn (interpolated for remote players)
    public float RenderEyeHeight = World.EyeHeight; // smoothed so crouching doesn't snap the camera

    public Vector3 Eye => GravityFrames.Above(Position, World.EyeOf(Sim.Stance)); // (along "up": straight up except on a planet)
    public Vector3 RenderEye => RenderSeat != SeatKind.None ? RenderPosition + Vector3.Transform(new Vector3(0, RenderEyeHeight, 0), RenderSeatRot) : GravityFrames.Above(RenderPosition, RenderEyeHeight);
    public Vector3 Chest => GravityFrames.Above(Position, World.HeightOf(Sim.Stance) * 0.65f);

    public void Teleport(Vector3 feet)
    {
        Sim = new MoveState { Position = feet, Grounded = true };
        PrevPosition = feet;
        RenderPosition = feet;
        RenderStance = Stance.Stand;
        RenderEyeHeight = World.EyeHeight;
    }

    public static readonly Vector3[] Palette =
    [
        new(0.20f, 0.55f, 1.00f), // blue
        new(1.00f, 0.25f, 0.25f), // red
        new(0.25f, 0.90f, 0.35f), // green
        new(1.00f, 0.85f, 0.15f), // yellow
        new(0.75f, 0.35f, 1.00f), // purple
        new(0.10f, 0.90f, 0.90f), // cyan
        new(1.00f, 0.55f, 0.10f), // orange
        new(1.00f, 1.00f, 1.00f), // white
    ];

    /// <summary>
    /// The outfit colour: the team colour in team modes (team readability always wins over the chosen look), otherwise (no
    /// teams: free-for-all, co-op, Zombies) the outfit colour picked on the APPEARANCE screen, or the player slot's palette
    /// colour on Auto (and for bots).
    /// </summary>
    public Vector3 Color => Team < 2 ? GameModes.TeamColors[Team] : Look.OutfitColor(Palette[Id % Palette.Length]);
}
