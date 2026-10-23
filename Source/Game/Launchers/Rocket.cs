using System.Numerics;

namespace CallOfSlop;

/// <summary>How a launcher projectile's flight ended (<see cref="Msg.RocketBoom"/>'s kind byte).</summary>
public enum RocketEnd : byte
{
    /// <summary>Still flying (or stuck and waiting).</summary>
    None,

    /// <summary>Armed and went off: blast, direct-hit bonus, cluster bomblets.</summary>
    Explode,

    /// <summary>Hit something before it armed: only the direct hit's damage, no blast.</summary>
    Dud,

    /// <summary>Left the map (or its owner is gone): removed with no effect.</summary>
    Gone,

    /// <summary>Not an end: the target it was locked on popped flares and pulled it off (a visual event at the target).</summary>
    Flares,
}

/// <summary>
/// One launcher projectile in flight (rocket, grenade or sticky charge). The server owns the real one; every client
/// flies its own copy from the spawn message with the same <see cref="RocketFlight.Step"/> (deterministic), guided and
/// stuck ones get a small correction with each snapshot, and the server's explode message ends it everywhere.
/// Pooled: nothing is allocated per rocket per frame.
/// </summary>
public sealed class Rocket
{
    public const int TrailLength = 14;

    public ushort Id;
    public byte Owner;

    /// <summary>The base weapon's id and the attachments on it (the flight numbers come from <see cref="Def"/>).</summary>
    public byte WeaponId;
    public uint Kit;
    public WeaponDef Def = null!;

    /// <summary>The shot's seed (the client's prediction is matched to the server's spawn by it).</summary>
    public uint Seed;

    public Vector3 Pos, Vel;

    /// <summary>Where it was at the start of the last tick (drawing blends between the two).</summary>
    public Vector3 PrevPos;

    /// <summary>Metres flown (arming distance).</summary>
    public float Travelled;

    /// <summary>Ticks since launch.</summary>
    public int Age;

    public int BouncesLeft;

    /// <summary>Lock-on: the streak entity it homes on (255 = none).</summary>
    public byte Target = byte.MaxValue;

    /// <summary>Wire guided: it steers to its owner's aim point.</summary>
    public bool Wire;

    /// <summary>Flares pulled it off its target: it flies at <see cref="Decoy"/> instead.</summary>
    public bool Decoyed;
    public Vector3 Decoy;

    /// <summary>Sticky charges: stuck to the world, or to player <see cref="StuckTo"/> at <see cref="StuckOffset"/> from their feet.</summary>
    public bool Stuck;
    public byte StuckTo = byte.MaxValue;
    public Vector3 StuckOffset;

    /// <summary>Armed when it stuck (sticky charges keep the arming of their flight).</summary>
    public bool Armed;

    // ---- Server: how it ended (set by the step, used by the blast) ----
    public RocketEnd End;
    public Vector3 EndPos, EndNormal;
    public byte HitPlayer = byte.MaxValue, HitEntity = byte.MaxValue;

    // ---- Client ----
    /// <summary>The local player's own prediction (not yet confirmed by the server).</summary>
    public bool Predicted;

    /// <summary>Client: it hit something here and waits for the server's explode message.</summary>
    public bool Hidden;

    public double Born;
    public Vector3 RenderPos;

    /// <summary>Visual-only offset that eases to zero (a prediction handing over to the server's copy, a sync correction).</summary>
    public Vector3 RenderOffset;

    public readonly Vector3[] Trail = new Vector3[TrailLength];
    public int TrailHead, TrailCount;
    public double NextTrail, NextMotorSound, LastSync;

    public bool IsArmed => Travelled >= Def.LauncherArmDistance;

    /// <summary>Clears it for reuse from the pool.</summary>
    public void Reset()
    {
        Id = 0;
        Owner = WeaponId = 0;
        Kit = Seed = 0;
        Pos = Vel = PrevPos = Decoy = StuckOffset = EndPos = EndNormal = RenderPos = RenderOffset = Vector3.Zero;
        Travelled = 0;
        Age = BouncesLeft = 0;
        Target = StuckTo = HitPlayer = HitEntity = byte.MaxValue;
        Wire = Decoyed = Stuck = Armed = Predicted = Hidden = false;
        End = RocketEnd.None;
        Born = NextTrail = NextMotorSound = LastSync = 0;
        TrailHead = TrailCount = 0;
    }

    /// <summary>Remembers a trail point (the smoke behind it).</summary>
    public void PushTrail(Vector3 p)
    {
        Trail[TrailHead] = p;
        TrailHead = (TrailHead + 1) % TrailLength;
        if (TrailCount < TrailLength)
            TrailCount++;
    }

    /// <summary>The i-th most recent trail point (0 = newest).</summary>
    public Vector3 TrailAt(int i) => Trail[(TrailHead - 1 - i + TrailLength * 2) % TrailLength];
}

/// <summary>What one flight step ran into.</summary>
public enum RocketContact : byte
{
    None,

    /// <summary>The map (a wall, the floor, the terrain): <c>hit</c> says where.</summary>
    World,

    /// <summary>A grenade bounced off the map (and flies on).</summary>
    Bounce,

    /// <summary>Flew out of the map.</summary>
    OutOfBounds,
}

/// <summary>
/// The launcher projectiles' flight, pure and deterministic (no randomness, no clocks): the server and every client run
/// the same steps from the same spawn and get the same path. Rockets: a motor up to top speed, a little gravity, optional
/// steering; grenades: a ballistic arc with optional bounces. Two substeps per 60 Hz tick, each a ray cast against the
/// map (so a 100 m/s rocket can't tunnel through a thin wall).
/// </summary>
public static class RocketFlight
{
    public const float Radius = 0.08f;
    public const int Substeps = 2;
    public const float Restitution = 0.4f;
    private const float Deg = MathF.PI / 180f;

    /// <summary>Launch direction inside the weapon's cone for this shot (the same seed gives the same direction everywhere).</summary>
    public static Vector3 LaunchDirection(Vector3 dir, float spread, uint seed) => Weapons.SpreadDirection(dir, spread, seed, 0);

    /// <summary>Launch velocity along a (unit) direction.</summary>
    public static Vector3 LaunchVelocity(WeaponDef d, Vector3 dir) => dir * d.LauncherSpeed;

    /// <summary>Where a projectile starts: just in front of the eye (pulled back if that's inside a wall).</summary>
    public static Vector3 LaunchPoint(Vector3 eye, Vector3 dir)
    {
        const float ahead = 0.55f;
        var d = World.Query.Raycast(eye, dir, ahead + Radius, QueryFilter.Shots);
        return eye + dir * MathF.Max(0f, MathF.Min(ahead, d - Radius * 2));
    }

    /// <summary>Unit vector <paramref name="from"/> turned towards unit vector <paramref name="to"/> by at most <paramref name="maxAngle"/> radians.</summary>
    public static Vector3 Turn(Vector3 from, Vector3 to, float maxAngle)
    {
        var cos = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        var angle = MathF.Acos(cos);
        if (angle <= maxAngle || angle < 1e-5f)
            return to;
        var sin = MathF.Sin(angle);
        if (sin < 1e-4f)
        {
            // Straight behind: turn about any axis at right angles.
            var axis = Vector3.Normalize(Vector3.Cross(from, MathF.Abs(from.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            return Vector3.Normalize(Vector3.Transform(from, Quaternion.CreateFromAxisAngle(axis, maxAngle)));
        }
        var t = maxAngle / angle;
        return Vector3.Normalize(from * (MathF.Sin((1 - t) * angle) / sin) + to * (MathF.Sin(t * angle) / sin));
    }

    /// <summary>
    /// One 60 Hz tick of flight. <paramref name="steerAt"/>: the point a guided projectile turns towards this tick (null =
    /// unguided). Returns what it ran into; on <see cref="RocketContact.World"/> the projectile sits at the contact point.
    /// </summary>
    public static RocketContact Step(Rocket r, Vector3? steerAt, out RayHit hit)
    {
        hit = default;
        var d = r.Def;
        var h = World.TickDt / Substeps;
        var bounced = false;
        for (var i = 0; i < Substeps; i++)
        {
            var speed = r.Vel.Length();
            if (d.LauncherProjectile == LauncherProjectile.Rocket && d.LauncherThrust > 0 && speed < d.LauncherMaxSpeed && speed > 1e-3f)
            {
                var ns = MathF.Min(d.LauncherMaxSpeed, speed + d.LauncherThrust * h);
                r.Vel *= ns / speed;
                speed = ns;
            }
            if (steerAt is { } at && d.LauncherTurnRate > 0 && speed > 1e-3f)
            {
                var want = at - r.Pos;
                var wl = want.Length();
                if (wl > 0.3f)
                    r.Vel = Turn(r.Vel / speed, want / wl, d.LauncherTurnRate * Deg * h) * speed;
            }
            r.Vel.Y -= World.Gravity * d.LauncherGravity * h;
            if (d.LauncherDrag > 0)
                r.Vel *= MathF.Exp(-d.LauncherDrag * h);

            var move = r.Vel * h;
            var len = move.Length();
            if (len < 1e-6f)
                continue;
            var dir = move / len;
            if (World.Query.Raycast(r.Pos, dir, len + Radius, QueryFilter.Shots, out var rh))
            {
                var travel = MathF.Max(0f, rh.Distance - Radius);
                r.Pos += dir * travel;
                r.Travelled += travel;
                if (d.LauncherProjectile == LauncherProjectile.Grenade && r.BouncesLeft > 0)
                {
                    r.BouncesLeft--;
                    var n = rh.Normal;
                    r.Vel = (r.Vel - 2f * Vector3.Dot(r.Vel, n) * n) * Restitution;
                    r.Pos += n * 0.02f;
                    bounced = true;
                    continue;
                }
                hit = rh;
                return RocketContact.World;
            }
            r.Pos += move;
            r.Travelled += len;
            if (OutOfMap(r.Pos))
            {
                hit.Point = r.Pos;
                hit.Normal = -dir;
                return RocketContact.OutOfBounds;
            }
        }
        return bounced ? RocketContact.Bounce : RocketContact.None;
    }

    /// <summary>Past the map's edge (with a little room), far below the floor or far up in the sky.</summary>
    public static bool OutOfMap(Vector3 p)
    {
        var map = World.Map;
        if (MathF.Abs(p.X) > map.HalfX + 3f || MathF.Abs(p.Z) > map.HalfZ + 3f || p.Y < -30f || p.Y > 400f)
            return true;
        return map.Boundary is { } b && !b.Inside(p.X, p.Z) && b.SignedDistance(p.X, p.Z) > 3f; // a few metres past a shaped map's outline
    }

    /// <summary>The blast damage at <paramref name="dist"/> metres from the centre of a blast of radius <paramref name="radius"/>.</summary>
    public static float BlastDamage(WeaponDef d, float dist, float radius) =>
        dist >= radius ? 0f : d.Damage * MathF.Pow(1f - dist / radius, d.LauncherFalloff);

    /// <summary>The blast damage a player takes <paramref name="dist"/> metres from the centre in the open (the player radius and "playerMult"; before prone, Flak Jacket and walls).</summary>
    public static float PlayerBlastDamage(WeaponDef d, float dist) => BlastDamage(d, dist, d.PlayerBlastRadius) * d.LauncherPlayerMult;

    /// <summary>How far from the centre (m) the blast alone takes <paramref name="health"/> off a player in the open (0 if not even the centre does).</summary>
    public static float PlayerLethalRadius(WeaponDef d, float health)
    {
        var centre = d.Damage * d.LauncherPlayerMult;
        return centre < health ? 0f : d.PlayerBlastRadius * (1f - MathF.Pow(health / centre, 1f / d.LauncherFalloff));
    }

    /// <summary>
    /// Where cluster bomblet <paramref name="i"/> of a blast lands: scattered round the centre (deterministic from the
    /// seed, the same on the server and every client), dropped to the first surface below.
    /// </summary>
    public static Vector3 ClusterPoint(Vector3 center, float radius, uint seed, int i, int count)
    {
        var a = (i + Hash01(seed, i * 2) * 0.6f) / Math.Max(1, count) * MathF.Tau;
        var r = radius * (0.55f + 0.6f * Hash01(seed, i * 2 + 1));
        return KillStreaks.GroundAt(center.X + MathF.Cos(a) * r, center.Z + MathF.Sin(a) * r, center.Y + 1f);
    }

    /// <summary>Seconds after the main blast that bomblet <paramref name="i"/> goes off.</summary>
    public static float ClusterDelay(uint seed, int i) => 0.22f + 0.07f * i + 0.12f * Hash01(seed ^ 0x5bd1e995u, i);

    /// <summary>Cluster bomblets: a smaller blast (fraction of the radius and of the damage).</summary>
    public const float ClusterRadiusMult = 0.45f, ClusterDamageMult = 0.4f;

    private static float Hash01(uint seed, int i)
    {
        var x = seed * 0x9E3779B1u ^ (uint)i * 0x85EBCA77u;
        x ^= x >> 15;
        x *= 0x2C1B3C6Du;
        x ^= x >> 12;
        return (x & 0xFFFFFF) / 16777216f;
    }
}
