using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Massive blasts (docs/sandbox/CONSTRUCTS.md, "Massive blasts"; numbers in docs/perf/LIMITS.md): a charge whose radius is
/// <see cref="ConstructWorld.MegaRadius"/> or more (the Fission, Fusion and Antimatter Warheads, or a vast pile of plain filling) does not go off
/// in one tick. It is a shock front that grows from the centre at <see cref="FrontSpeed"/>: each tick the players it passes take their share
/// (the game, <see cref="IConstructTargets.MegaFront"/>), and the bodies it has reached, nearest first, are walked about a fixed number of parts a
/// tick (<see cref="ConstructWorld.MegaPartsPerTick"/>, shared by every massive blast at once; a body at a time, its broken parts out together). Its crater is dug in bands over ticks (Game.MegaDigTick).
/// Every number is arcade: chosen to look and play big, not modelled on anything. Nothing here allocates once a world's arrays are made.
/// </summary>
public static class MegaBlasts
{
    /// <summary>Looks (a wire byte in ShellLand, protocol 195): what the effect draws. New ones go at the end.</summary>
    public const byte Plain = 0, Fission = 1, Fusion = 2, Antimatter = 3;

    /// <summary>A payload section's look from its <c>ordnance.kind</c>.</summary>
    public static int LookOf(string kind) => kind switch { "fission" => Fission, "fusion" => Fusion, "antimatter" => Antimatter, _ => Plain };

    /// <summary>How fast the front grows (m/s): at least <see cref="ConstructWorld.MegaFrontSpeed"/>, and quick enough to reach its edge in <see cref="ConstructWorld.MegaFrontSeconds"/>.</summary>
    public static float FrontSpeed(float radius) => MathF.Max(ConstructWorld.MegaFrontSpeed, radius / ConstructWorld.MegaFrontSeconds);

    /// <summary>The bench's <c>Limit:</c> lines for a design whose blast is massive.</summary>
    public static void Limits(OrdFlightStats f, OrdStats st)
    {
        var r = MathF.Min(st.Blast, ConstructWorld.MegaMaxRadius);
        f.Limits.Add($"its {st.Blast:0} m blast is a shock front: it reaches its edge in {r / FrontSpeed(r):0.0} s, walking {ConstructWorld.MegaPartsPerTick} parts a tick over the nearest {ConstructWorld.MegaMaxBodies} bodies");
        if (st.Blast > ConstructWorld.MegaMaxRadius)
            f.Limits.Add($"the engine holds a blast to {ConstructWorld.MegaMaxRadius:0} m (and a host may set less)");
        if (st.Crater > 0)
            f.Limits.Add($"its crater is held to {MathF.Min(st.Crater, ConstructWorld.MegaCraterMax):0} m and dug in bands over a few ticks");
    }
}

/// <summary>A massive blast under way (<see cref="ConstructWorld.StepMegas"/>). Fixed pool: <see cref="ConstructWorld.MaxMegas"/>.</summary>
public struct CMegaBlast
{
    public bool Active;
    public byte Look, By;
    public Vector3 At;
    public float Radius, Damage, Front, Speed; // m, at the centre, how far the shock has gone, m/s
    public ushort Gun; // the body it came from (credit), and its rack
    public int GunPart;
    public int Ticks;
    public int Body, BodyCount; // the walk: the next body
    public int Debris; // loose debris it may still make (ConstructWorld.MegaDebrisMax)
    public ushort[] Bodies; // nearest first
    public float[] Reach; // ...how far the front must go to touch each (its centre's distance less its radius)
}

/// <summary>The world's massive blasts (see <see cref="MegaBlasts"/>).</summary>
public sealed partial class ConstructWorld
{
    public const float MegaRadius = 100f; // m: a blast this big or bigger is a shock front (the Small Nuke's 90 m, a prop's, stays a blast)
    public const float MegaMaxRadius = 2000f; // m: the engine's cap on any blast (a 20 m wide antimatter design would ask for kilometres more)
    public const float MegaFrontSpeed = 340f; // m/s the front grows at least (arcade: about sound's speed)...
    public const float MegaFrontSeconds = 4f; // ...and faster still for the biggest, so none takes longer than this to reach its edge
    public const float MegaCraterMax = 120f; // m: the biggest crater a blast digs (memory: about 180,000 columns of dug ground, 48 B each the first time a map digs them)
    public const float MegaShoveMax = 30f; // m/s the most a massive blast throws a body
    public const int MaxMegas = 4; // under way at once; a fifth waits as a plain blast (bounded work, never lost)
    public const int MegaMaxBodies = 2048; // bodies one massive blast reaches, the nearest
    public const int MegaPartsPerTick = 800; // parts walked a tick over every massive blast at once
    public const float MegaVaporiseShare = 0.5f; // a body wholly inside this share of its radius is gone at once
    public const int MegaDebrisMax = 96; // pieces one massive blast cuts off that fall as wrecks; past that they are gone too

    /// <summary>While a massive blast walks: the wrecks it may still cut off (-1: no blast walking, no limit; ConstructWorld.SplitOff).</summary>
    public int MegaDebrisLeft = -1;

    /// <summary>The host's cap on any blast's radius (Settings.SandboxMaxBlast, the lobby's "Biggest blast"; 0: only the engine's).</summary>
    public float BlastCap;

    /// <summary>A blast's radius as this world lets it be: the engine's cap, then the host's.</summary>
    public float CapRadius(float radius)
    {
        var r = MathF.Min(radius, MegaMaxRadius);
        return BlastCap > 0 ? MathF.Min(r, BlastCap) : r;
    }

    private CMegaBlast[] _megas = [];
    private readonly List<Construct> _megaNear = new(256);

    /// <summary>Massive blasts under way, and the parts they have walked (the perf window, the bench).</summary>
    public int MegaCount, MegaPartsWalked, MegasStarted, MegaBodiesGone;

    /// <summary>Is any part of a massive blast still to come (its front, its walk)?</summary>
    public bool MegaBusy => MegaCount > 0;

    /// <summary>
    /// Server: a massive blast at <paramref name="at"/> (its radius already capped): the crater is asked for at once and dug over ticks, the front
    /// and the walk go on in <see cref="StepMegas"/>. False when the pool is full (the caller goes off as a plain blast instead).
    /// </summary>
    public bool StartMega(Vector3 at, float radius, float damage, float crater, byte look, byte by, ushort gun, int gunPart)
    {
        if (_megas.Length == 0)
            _megas = new CMegaBlast[MaxMegas];
        var k = -1;
        for (var i = 0; i < _megas.Length; i++)
            if (!_megas[i].Active)
            {
                k = i;
                break;
            }
        if (k < 0)
        {
            Caps.Hit("massive blasts at once", MaxMegas);
            return false;
        }
        ref var m = ref _megas[k];
        var near = Near(at, radius + 1f, _megaNear);
        if (m.Bodies == null || m.Bodies.Length < near.Count)
        {
            var size = Math.Max(256, near.Count);
            m.Bodies = new ushort[size]; // (once per size: a world's first massive blast, or one over more bodies than any before)
            m.Reach = new float[size];
        }
        var n = 0;
        for (var i = 0; i < near.Count; i++)
        {
            var c = near[i];
            var reach = Vector3.Distance(c.WorldCom, at) - c.Radius;
            if (reach > radius)
                continue;
            m.Bodies[n] = c.Id;
            m.Reach[n++] = MathF.Max(0f, reach);
        }
        Array.Sort(m.Reach, m.Bodies, 0, n); // (nearest first: the front reaches them in this order)
        if (n > MegaMaxBodies)
        {
            Caps.Hit("bodies one massive blast reaches", MegaMaxBodies);
            n = MegaMaxBodies;
        }
        m.Active = true;
        m.Look = look;
        m.By = by;
        m.At = at;
        m.Radius = radius;
        m.Damage = damage;
        m.Front = 0;
        m.Speed = MegaBlasts.FrontSpeed(radius);
        Targets?.MegaStarted(at, radius, look, by);
        m.Gun = gun;
        m.GunPart = gunPart;
        m.Ticks = 0;
        m.Body = 0;
        m.BodyCount = n;
        m.Debris = MegaDebrisMax;
        MegaCount++;
        MegasStarted++;
        if (crater > 0)
            Targets?.MegaGround(at, MathF.Min(crater, MegaCraterMax));
        return true;
    }

    /// <summary>Server, each tick with the shells: every front grows, the players it passed are hit, and the bodies it reached are walked.</summary>
    private void StepMegas(float dt)
    {
        if (MegaCount == 0)
            return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var budget = MegaPartsPerTick;
        for (var k = 0; k < _megas.Length; k++)
        {
            ref var m = ref _megas[k];
            if (!m.Active)
                continue;
            var from = m.Ticks == 0 ? -1f : m.Front;
            m.Front = MathF.Min(m.Radius, m.Front + m.Speed * dt);
            m.Ticks++;
            Targets?.MegaFront(m.At, m.Radius, m.Damage, from, m.Front, Get(m.Gun), m.GunPart);
            WalkMega(ref m, ref budget);
            if (m.Front >= m.Radius && m.Body >= m.BodyCount)
            {
                m.Active = false;
                MegaCount--;
            }
        }
        MegaStepWorstMs = MathF.Max(MegaStepWorstMs, (float)System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    /// <summary>The longest tick of <see cref="StepMegas"/> so far (the bench and the perf window; reset by hand).</summary>
    public float MegaStepWorstMs;

    /// <summary>
    /// The bodies the front has reached, nearest first, about <paramref name="budget"/> parts a tick. A body is done in one go, whatever its size
    /// (a part a tick at most over the budget): every part in reach takes its damage, and the parts it breaks come out together through
    /// <see cref="RemoveParts"/> (one structure rebuild, not one a part: a break one by one costs a flood fill and a split each, 0.1 ms a part on a
    /// 60-part hull). Parts that stop working rather than fall off (engines, tanks, seats) take their damage the usual way, and an ammo box,
    /// a tank or a bomb the blast breaks still cooks off or goes off. A body wholly inside <see cref="MegaVaporiseShare"/> of the radius is gone at once.
    /// </summary>
    private void WalkMega(ref CMegaBlast m, ref int budget)
    {
        DamageBy = m.By;
        MegaDebrisLeft = m.Debris;
        while (budget > 0 && m.Body < m.BodyCount)
        {
            if (m.Reach[m.Body] > m.Front)
                break; // (the front hasn't got there yet: the rest are further)
            var c = Get(m.Bodies[m.Body++]);
            if (c == null)
                continue;
            budget -= 1 + c.PartCount;
            MegaPartsWalked += c.PartCount;
            if (m.Reach[m.Body - 1] + 2f * c.Radius < m.Radius * MegaVaporiseShare && !c.Anchored)
            {
                MegaBodiesGone++; // (wholly inside the middle: gone at once, with what it held; an anchored base is walked, its foundations may hold)
                Remove(c);
                continue;
            }
            MegaBody(c, in m);
            if (IsLive(c))
                MegaFinish(c, in m);
        }
        m.Debris = Math.Max(0, MegaDebrisLeft);
        MegaDebrisLeft = -1;
        DamageBy = 255;
    }

    /// <summary>One body's parts under a massive blast (see <see cref="WalkMega"/>).</summary>
    private void MegaBody(Construct c, in CMegaBlast m)
    {
        if (c.Dirty)
            c.Rebuild();
        var n = c.PartCount;
        if (_megaDoomed.Length < n)
            _megaDoomed = new bool[Math.Max(n, 1024)]; // (once per size)
        var doomed = 0;
        for (var i = 0; i < n; i++)
        {
            _megaDoomed[i] = false;
            var dist = PartDistance(c, i, m.At);
            if (dist >= m.Radius)
                continue;
            ref var p = ref c.Parts[i];
            if (p.Broken)
                continue;
            var d = p.D;
            var f = 1f - dist / m.Radius;
            var dmg = m.Damage * f * f;
            if (m.Look != MegaBlasts.Plain && f > 0.6f && CanBurn(c, i))
                Ignite(c, i); // (the heat near the middle lights what burns)
            BlastStored(c, i, dmg);
            if (dmg - d.Armour < p.Health || d.OnBreak == PartBreak.Disable)
            {
                DamagePart(c, i, dmg, out _); // (it holds, or it stops working where it is: no change to the structure)
                continue;
            }
            // It breaks and comes off: marked, with what its breaking sets off.
            p.Health = 0;
            if (d.Role is PartRole.AmmoBox or PartRole.FuelTank || d.IsTankVolume || d.AmmoStore)
                CookOff(c, i, ConstructRender.PartCentre(c, i));
            if (d.Explosive is { } bomb)
                Arm(c, i, bomb);
            _megaDoomed[i] = true;
            doomed++;
        }
        if (doomed == 0 || !IsLive(c) || c.PartCount != n)
            return;
        Disable(c);
        RemoveParts(c, new ReadOnlySpan<bool>(_megaDoomed, 0, n));
    }

    private bool[] _megaDoomed = [];

    /// <summary>A body the front has walked: its routed links and tracks, and the shove (held to <see cref="MegaShoveMax"/>).</summary>
    private void MegaFinish(Construct c, in CMegaBlast m)
    {
        if (c.RouteCount > 0)
            ConstructRoutes.Blast(this, c, m.At, m.Radius, m.Damage);
        if (c.TrackCount > 0 && IsLive(c))
            ConstructTracks.Blast(this, c, m.At, m.Radius, m.Damage);
        if (c.Anchored || !IsLive(c))
            return;
        var d = c.WorldCom - m.At;
        var dist = d.Length();
        if (dist > m.Radius + c.Radius * 0.5f || dist < 1e-3f)
            return;
        var k = 1f - MathF.Min(1, dist / (m.Radius + c.Radius * 0.5f));
        var dv = MathF.Min(MegaShoveMax, k * m.Damage * 2.4f / MathF.Max(c.Mass, 20f));
        c.Wake();
        c.Vel += Vector3.Normalize(d / dist + new Vector3(0, 0.5f, 0)) * dv;
    }

    /// <summary>
    /// Server: a massive warhead set off where it hangs or lies (shot, burnt, crushed: <see cref="ConstructWeapons"/>' cook-offs). Its shock front
    /// starts there, as a landed one's would, and every PC is told with a ShellLand of no shell. With every front in use it waits half a second.
    /// </summary>
    internal void MegaCookOff(in CCookOff at)
    {
        var bomb = at.Bomb!;
        var r = CapRadius(bomb.ExplodeRadius);
        var crater = bomb.ExplodeCrater * r / MathF.Max(1f, bomb.ExplodeRadius);
        var by = Get(at.Body)?.Owner ?? 255;
        if (!StartMega(at.At, r, bomb.ExplodeDamage, crater, at.Look, by, at.Body, -1))
        {
            if (CookOffCount < CookOffs.Length)
                CookOffs[CookOffCount++] = at with { Ticks = World.TickRate / 2 };
            return;
        }
        AddFire(new CFireEvent { Kind = CFireKind.ShellLand, Shell = 0, At = at.At, Radius = r, Muzzle = (byte)ShellKind.Bomb, Ext = at.Look });
    }

    private void ClearMegas()
    {
        for (var k = 0; k < _megas.Length; k++)
            _megas[k].Active = false;
        MegaCount = 0;
    }
}
