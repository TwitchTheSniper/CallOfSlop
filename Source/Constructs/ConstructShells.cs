using System.Numerics;

namespace CallOfSlop;

/// <summary>What a shell does where it lands (wire byte: new kinds go at the end).</summary>
public enum ShellKind : byte
{
    Explosive,
    Siege, // a deeper crater
    Incendiary, // lights what burns in the blast
    Smoke, // a cloud where it lands
}

/// <summary>One artillery shell in the air, or landed and waiting for its turn to go off. Fixed pool: <see cref="ConstructWorld.Shells"/>.</summary>
public struct CShell
{
    public ushort Id;
    public ShellKind Kind;
    public bool Landed;
    public byte By; // the gun's owner, for the story (255: nobody)
    public ushort Gun; // the construct that fired it (its shell passes it on the way out; the blast's credit)
    public ushort GunPart;
    public int Age; // ticks
    public Vector3 Pos, Vel, Prev;
    public Vector3 Tested; // where its last collision test ended
    public float Blast, Damage, Crater;
}

/// <summary>A landed shell's blast, walked a few parts a tick so that a big one on a big ship costs no one tick much.</summary>
public struct CShellBlast
{
    public bool Active, Incendiary;
    public Vector3 At;
    public float Radius, Damage;
    public byte By;
    public int Body; // the next index in the list of bodies
    public int Part; // the next part of that body, counted down (-1: start it)
    public int BodyCount;
}

/// <summary>
/// Artillery (docs/sandbox/CONSTRUCTS.md, "Artillery"): real shells on a parabola, as far from a hitscan <c>Round</c> as the game
/// gets. Each PC flies its own copy from the launch event with the same step; only the server tests for what they hit, and its
/// landing event ends them everywhere. Fixed pool, nothing allocates per tick, and the tick's cost is bounded three ways: at most
/// <see cref="ConstructWorld.MaxShells"/> in the air, each tested for what it hit one tick in <see cref="ConstructWorld.ShellTestEvery"/>
/// (over the whole stretch, so nothing is flown through), and one blast at a time, walked <see cref="ConstructWorld.ShellPartsPerTick"/>
/// parts a tick. A salvo's later shells wait their turn: a few ticks, and they have already been seen to land.
/// </summary>
public static class ConstructShells
{
    private const float FlatGravity = 18f; // (no gravity here: a muzzle speed still comes from a gun's range)

    private static ConstructWeaponParts.Rules R => ConstructWeaponParts.Current;

    /// <summary>Gravity's pull at a point, per second.</summary>
    public static Vector3 GravityAt(Vector3 p) => GravityFrames.Curved ? GravityFrames.GravityAt(p) : new Vector3(0, -World.Gravity * World.GravityScale, 0);

    /// <summary>The numbers of a shell gun, after a merge scaled it. A gun's damage is its blast's, and the blast's radius grows with the square root of it, so a merge's N-squared damage is N times the radius.</summary>
    public static void GunNumbers(ref CGun g, WeaponDef w, ShellKind kind)
    {
        var n = Math.Max(1, (int)g.MergeN);
        w.RangeM *= 1f + R.ShellRangePerStep * (n - 1);
        var pull = MathF.Max(1f, World.Gravity * World.GravityScale);
        g.MaxRange = w.RangeM;
        g.Speed = MathF.Sqrt(w.RangeM * (pull > 1f ? pull : FlatGravity)); // (range is the reach at 45 degrees)
        g.Shell = kind;
        g.BlastDamage = w.Damage;
        g.Blast = MathF.Min(R.ShellBlast * MathF.Sqrt(w.Damage / 320f), R.ShellBlastMax);
        g.Crater = kind == ShellKind.Smoke ? 0 : MathF.Min(g.Blast * R.ShellCrater * (kind == ShellKind.Siege ? R.ShellSiegeCrater : 1f), R.ShellCraterMax);
        g.Fx = GunFx.Cannon;
        g.Kick = R.ShellKick * n * n;
    }

    /// <summary>The way to fire to land on <paramref name="target"/> from <paramref name="muzzle"/> at <paramref name="speed"/>: the low arc, or the high one to lob over something. False (45 degrees at it) when it is out of reach.</summary>
    public static bool Lead(Vector3 muzzle, Vector3 target, float speed, bool high, out Vector3 dir)
    {
        var pull = GravityAt(muzzle);
        var g = pull.Length();
        var up = g > 0.01f ? -pull / g : Vector3.UnitY;
        var to = target - muzzle;
        var y = Vector3.Dot(to, up);
        var flat = to - up * y;
        var x = flat.Length();
        if (g < 0.01f || x < 0.5f)
        {
            dir = to.LengthSquared() > 1e-4f ? Vector3.Normalize(to) : up;
            return g < 0.01f || x >= 0f;
        }
        flat /= x;
        var v2 = speed * speed;
        var disc = v2 * v2 - g * (g * x * x + 2 * y * v2);
        if (disc < 0)
        {
            dir = Vector3.Normalize(flat + up);
            return false;
        }
        var root = MathF.Sqrt(disc);
        var t = (v2 + (high ? root : -root)) / (g * x);
        dir = Vector3.Normalize(flat + up * t);
        return true;
    }

    /// <summary>Gun <paramref name="k"/> fires: a shell leaves its muzzle. False when the pool is full (it waits, the round not spent).</summary>
    public static void Launch(ConstructWorld world, Construct c, int k, Vector3 dir)
    {
        ref var g = ref c.Guns[k];
        ConstructWeapons.MuzzleWorld(c, k, out var muzzle, out _);
        var vel = dir * g.Speed + c.Vel;
        var s = world.AddShell(new CShell { Kind = g.Shell, By = c.Owner, Gun = c.Id, GunPart = g.Part, Pos = muzzle, Prev = muzzle, Tested = muzzle, Vel = vel, Blast = g.Blast, Damage = g.BlastDamage, Crater = g.Crater });
        if (s != 0)
            world.AddFire(new CFireEvent { Kind = CFireKind.ShellOut, Body = c.Id, Part = g.Part, Shell = s, At = muzzle, To = vel, Muzzle = (byte)g.Shell });
    }

    /// <summary>
    /// Where gun <paramref name="k"/> lands as it points now, in long straight steps against the map (a drop aid: what the
    /// gunner's marker shows, every frame on one PC). False when it doesn't come down in the time looked at.
    /// </summary>
    public static bool Predict(Construct c, int k, IWorldQuery query, out Vector3 land, out float seconds)
    {
        ref var g = ref c.Guns[k];
        ConstructWeapons.MuzzleWorld(c, k, out var p, out var fwd);
        var v = fwd * g.Speed;
        const float step = 0.5f;
        seconds = 0;
        for (var i = 0; i < 80; i++)
        {
            var a = GravityAt(p);
            var next = p + v * step + a * (0.5f * step * step);
            var d = next - p;
            var len = d.Length();
            if (len > 1e-3f && query.Raycast(p, d / len, len, QueryFilter.Shots) is var hit && hit < len - 1e-3f)
            {
                land = p + d / len * hit;
                seconds += step * hit / len;
                return true;
            }
            v += a * step;
            p = next;
            seconds += step;
        }
        land = p;
        return false;
    }
}

/// <summary>The world's shells (see <see cref="ConstructShells"/>).</summary>
public sealed partial class ConstructWorld
{
    public const int MaxShells = 64; // in the air at once: a gun holds its fire while it is full
    public const int ShellTestEvery = 3; // a shell looks for what it hit one tick in this many, over all of them
    public const int ShellPartsPerTick = 400; // parts of a blast walked a tick
    public const int ShellMaxBodies = 256; // bodies one blast reaches (the nearest the broadphase gives)
    public const int ShellMaxAge = 90 * World.TickRate; // ticks in the air before it is given up on
    public const int ShellMuzzleClear = 30; // ticks it passes its own construct

    public CShell[] Shells = []; // (made with the first shell: a clip's little world costs nothing)
    public int ShellCount;
    public int ShellBlastsWaiting; // landed, not yet gone off

    /// <summary>Shells fired, landed and blast parts walked (the perf window and hand checks).</summary>
    public int ShellsFired, ShellsLanded, ShellPartsWalked;

    private ushort _nextShell = 1;
    private CShellBlast _blast;
    private readonly ushort[] _blastBodies = new ushort[ShellMaxBodies];

    public bool ShellRoom => ShellCount < MaxShells;

    /// <summary>Adds a shell. Its id, or 0 when the pool is full.</summary>
    public ushort AddShell(CShell s)
    {
        if (ShellCount >= MaxShells)
            return 0;
        if (Shells.Length == 0)
            Shells = new CShell[MaxShells];
        s.Id = _nextShell++;
        if (_nextShell == 0)
            _nextShell = 1;
        Shells[ShellCount++] = s;
        ShellsFired++;
        return s.Id;
    }

    /// <summary>Client: a shell from the launch event.</summary>
    public void ClientAddShell(ushort id, ShellKind kind, Vector3 at, Vector3 vel)
    {
        if (ShellCount >= MaxShells)
            return;
        if (Shells.Length == 0)
            Shells = new CShell[MaxShells];
        Shells[ShellCount++] = new CShell { Id = id, Kind = kind, Pos = at, Prev = at, Vel = vel };
    }

    /// <summary>Client: its shell ended (the landing event).</summary>
    public void ClientDropShell(ushort id)
    {
        for (var k = 0; k < ShellCount; k++)
            if (Shells[k].Id == id)
            {
                Shells[k] = Shells[--ShellCount];
                return;
            }
    }

    /// <summary>Every PC, each tick: the shells fly. A client's end only when the server says so, or when one has flown too long.</summary>
    public void FlyShells(float dt)
    {
        for (var k = ShellCount - 1; k >= 0; k--)
        {
            ref var s = ref Shells[k];
            if (s.Landed)
                continue;
            s.Prev = s.Pos;
            s.Vel += ConstructShells.GravityAt(s.Pos) * dt;
            s.Pos += s.Vel * dt;
            if (++s.Age > ShellMaxAge)
                Shells[k] = Shells[--ShellCount];
        }
    }

    /// <summary>Server, each tick with the guns: shells fly, a third of them look for what they hit, and the next blast goes on.</summary>
    public void StepShells(IWorldQuery query, float dt)
    {
        if (ShellCount == 0 && !_blast.Active)
            return;
        FlyShells(dt);
        for (var k = 0; k < ShellCount; k++)
        {
            ref var s = ref Shells[k];
            if (s.Landed || (s.Age + s.Id) % ShellTestEvery != 0)
                continue;
            var d = s.Pos - s.Tested;
            var len = d.Length();
            if (len < 1e-4f)
                continue;
            d /= len;
            var best = query.Raycast(s.Tested, d, len, QueryFilter.Shots);
            var met = best < len - 1e-3f;
            var skip = s.Age < ShellMuzzleClear ? Get(s.Gun) : null;
            if (Raycast(s.Tested, d, best, out var hit, skip) && hit.Distance < best)
            {
                best = hit.Distance;
                met = true;
            }
            if (Targets != null && Targets.RayTarget(s.Tested, d, best, s.Age < ShellMuzzleClear ? s.Gun : (ushort)0, out var td, out _) && td < best)
            {
                best = td;
                met = true;
            }
            if (met)
            {
                s.Pos = s.Tested + d * best;
                s.Landed = true;
                ShellBlastsWaiting++;
            }
            else
                s.Tested = s.Pos;
        }
        WalkBlast();
        if (!_blast.Active && ShellBlastsWaiting > 0)
            for (var k = 0; k < ShellCount; k++)
                if (Shells[k].Landed)
                {
                    StartBlast(k);
                    break;
                }
    }

    /// <summary>A landed shell goes off: everything that happens at once (the event, the players, the ground), and the list of bodies its blast will walk.</summary>
    private void StartBlast(int k)
    {
        var s = Shells[k];
        Shells[k] = Shells[--ShellCount];
        ShellBlastsWaiting--;
        ShellsLanded++;
        AddFire(new CFireEvent { Kind = CFireKind.ShellLand, Shell = s.Id, At = s.Pos, Radius = s.Blast, Muzzle = (byte)s.Kind });
        var by = Get(s.Gun);
        Targets?.ShellBlast(s.Pos, s.Blast, s.Damage, s.Crater, s.Kind == ShellKind.Incendiary, by, s.GunPart);
        var near = Near(s.Pos, s.Blast + 1f, _blastNear);
        var n = 0;
        for (var i = 0; i < near.Count && n < ShellMaxBodies; i++)
            _blastBodies[n++] = near[i].Id;
        _blast = new CShellBlast { Active = n > 0, Incendiary = s.Kind == ShellKind.Incendiary, At = s.Pos, Radius = s.Blast, Damage = s.Damage, By = s.By, BodyCount = n, Part = -1 };
    }

    /// <summary>The blast under way, <see cref="ShellPartsPerTick"/> parts on. Walks each body's parts from the last, so one that breaks off never moves those still to come.</summary>
    private void WalkBlast()
    {
        if (!_blast.Active)
            return;
        ref var b = ref _blast;
        var budget = ShellPartsPerTick;
        DamageBy = b.By;
        while (budget > 0 && b.Body < b.BodyCount)
        {
            if (Get(_blastBodies[b.Body]) is not { } c)
            {
                b.Body++;
                b.Part = -1;
                continue;
            }
            if (b.Part < 0 || b.Part >= c.PartCount)
            {
                if (b.Part == -1 && Vector3.Distance(c.WorldCom, b.At) > b.Radius + c.Radius)
                {
                    b.Body++;
                    continue;
                }
                b.Part = c.PartCount; // (from the end)
            }
            while (budget > 0 && --b.Part >= 0)
            {
                budget--;
                ShellPartsWalked++;
                var dist = PartDistance(c, b.Part, b.At);
                if (dist >= b.Radius)
                    continue;
                var f = 1f - dist / b.Radius;
                var dmg = b.Damage * f * f;
                if (b.Incendiary)
                    Ignite(c, b.Part);
                BlastStored(c, b.Part, dmg);
                DamagePart(c, b.Part, dmg, out _);
                if (!IsLive(c))
                    break;
            }
            if (!IsLive(c))
            {
                b.Body++;
                b.Part = -1;
                continue;
            }
            if (b.Part > 0)
                break; // (the budget ran out in this body: on next tick)
            FinishBlastOn(c, b.At, b.Radius, b.Damage);
            b.Body++;
            b.Part = -1;
        }
        DamageBy = 255;
        if (b.Body >= b.BodyCount)
            b.Active = false;
    }

    /// <summary>A body the blast has walked: its routed links and tracks, and the shove.</summary>
    private void FinishBlastOn(Construct c, Vector3 at, float radius, float damage)
    {
        if (c.RouteCount > 0)
            ConstructRoutes.Blast(this, c, at, radius, damage);
        if (c.TrackCount > 0 && IsLive(c))
            ConstructTracks.Blast(this, c, at, radius, damage);
        if (c.Anchored || !IsLive(c))
            return;
        var d = c.WorldCom - at;
        var dist = d.Length();
        if (dist > radius + c.Radius * 0.5f || dist < 1e-3f)
            return;
        var k = 1f - MathF.Min(1, dist / (radius + c.Radius * 0.5f));
        c.Wake();
        c.Vel += (d / dist + new Vector3(0, 0.5f, 0)) * k * damage * 0.4f / MathF.Max(c.Mass, 20f) * 6f;
    }

    private void ClearShells()
    {
        ShellCount = 0;
        ShellBlastsWaiting = 0;
        _blast = default;
    }
}
