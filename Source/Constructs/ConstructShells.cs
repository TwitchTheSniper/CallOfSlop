using System.Numerics;

namespace CallOfSlop;

/// <summary>What a shell does where it lands (wire byte: new kinds go at the end).</summary>
public enum ShellKind : byte
{
    Explosive,
    Siege, // a deeper crater
    Incendiary, // lights what burns in the blast
    Smoke, // a cloud where it lands
    Bomb, // let go from a rack (ConstructOrdnance.cs)
    Flak, // bursts near what flies, or at its sight's time
    Rocket, // pushes itself for a while
    Missile, // and turns towards what it was locked on to
    Flare, // burns and sinks, and draws missiles
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
    public float Thrust, Turn, Prox; // a rocket's push (m/s2), a missile's turn (radians a second), how near a flying thing sets it off (m)
    public int Burn, Fuse, Delay; // ticks it pushes for, its age when it bursts (0: never), ticks it lies after it hit before going off
    public int Clear; // ticks it passes its own construct
    public ushort Target, Decoy; // what a missile steers for: a body, or (drawn away) a flare's shell id
    public Vector3 Size; // a bomb's size (what is drawn: its part type may be this machine's own)
    public ushort Stuck; // the body a bomb landed on, and where on it (it rides along until its fuse)
    public Vector3 StuckAt;

    // A designed bomb or missile (OrdnanceFlight.cs). Every PC flies these from the launch event and ShellSteer:
    public ShellFlags Flags;
    public float AirPush; // m/s2 of its push that breathes air (thinning with the air, none below MinSpeed)
    public float TurnAir, TurnBurn; // radians a second its fins give at speed in thick air, and its gimbal or vanes while it burns (Turn: any time)
    public float Drag, Lift, MinSpeed; // a share of its speed a second in thick air; the share of gravity its wings hold up at speed; an air motor's lowest speed
    public int Wait, Deploy, Life; // ticks before it lights; its age when chutes and wings open; ticks before it is given up on (0: ShellMaxAge)
    public Vector3 Aim; // the point it steers for (HasAim)
    // ...and only the server's:
    public ConstructPartDef? Design;
    public byte Stage, Charge, Seek; // the stage burning, the charge in a series that goes off next, how it finds its target (OrdSeek)
    public float Mass; // kg still flying (stages drop)
    public ushort Guide; // the body that guides it (a gunner's, a camera's, a waypoint's)
    public int GuidePart, Channel;
    public OrdFuse Fuses;
    public float FuseHeight, FuseDistance;

    // How it is drawn (every PC, from the launch event: ShellLooks.cs). Null: a plain box.
    public ConstructPartDef? Look; // the model it had on its rack (a designed weapon's run-time part, a bomb's part file, a rocket's stand-in)
    public Quaternion Rot, PrevRot; // its part's turn in the world, this tick and the last (nose, -Z, eased from its rack onto its way)
    public float Hurt; // the server's: damage it has taken in the air (ConstructCookOffs.cs: at its part's health it goes off)
}

/// <summary>What a designed shell is and does (a wire byte).</summary>
[Flags]
public enum ShellFlags : byte
{
    None = 0,
    Designed = 1, // off an Ordnance Bench: OrdnanceFlight.Fly flies it
    AirMotor = 2, // some of its push breathes air
    HasAim = 4, // it steers for Aim
    Mine = 8, // landed, it waits for something to move near it
    Kinetic = 16, // its damage is its speed's when it hits
    FlareProne = 32, // a heat seeker: flares draw it
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
    public int Debris; // wrecks it may still leave (ConstructWorld.BlastDebris): past them a part it breaks off, or a piece it cuts off, is gone
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
        w.RangeM *= (1f + R.ShellRangePerStep * (n - 1)) * w.ChargeV; // (BuildStats took the range by the charge once: reach goes with the speed squared)
        // (not World.GravityScale before the maps are up: reading it starts World, which loads the maps, and a map's blueprints build guns: a shell gun in a committed blueprint
        // hung the start-up while maps loaded in parallel, or threw in a serial load)
        var pull = MathF.Max(1f, World.Gravity * (Maps.Loaded ? World.GravityScale : 1f));
        g.MaxRange = w.RangeM;
        g.Speed = MathF.Sqrt(w.RangeM * (pull > 1f ? pull : FlatGravity)); // (range is the reach at 45 degrees)
        g.Shell = kind;
        w.Damage = (int)MathF.Round(w.Damage / w.ChargeDamage); // (a shell's blast is its filling: the charge moves it, nothing more)
        g.BlastDamage = w.Damage;
        (g.Blast, g.Crater) = ShellSize(w.Damage, kind, n);
        g.Fx = GunFx.Cannon;
        g.Kick = R.ShellKick * n * n;
    }

    /// <summary>
    /// A shell's blast and crater (m) for its filling's <paramref name="damage"/>, fired from an <paramref name="n"/> x <paramref name="n"/> merged gun: 9 m x sqrt(damage / 320), the crater
    /// 0.4 of that (a siege shell's 1.5 times more). The caps (45 m and 14 m in WeaponParts.json) are a single gun's and grow with sqrt(N) (docs/sandbox/VEHICLE-WEAPONS.md,
    /// "Blasts by shell size"): a 4x4 Howitzer's 8192-damage shell blasts 46 m and digs 18 m, a 6x6's 18432 blasts 68 m and digs 27 m, still a plain blast
    /// (<see cref="ConstructWorld.MegaRadius"/> is 100 m) walked <see cref="ConstructWorld.ShellPartsPerTick"/> parts a tick.
    /// </summary>
    public static (float Blast, float Crater) ShellSize(float damage, ShellKind kind, int n = 1)
    {
        var grow = MathF.Sqrt(Math.Max(1, n));
        var blast = MathF.Min(R.ShellBlast * MathF.Sqrt(MathF.Max(0, damage) / 320f), R.ShellBlastMax * grow);
        var crater = kind == ShellKind.Smoke ? 0 : MathF.Min(blast * R.ShellCrater * (kind == ShellKind.Siege ? R.ShellSiegeCrater : 1f), R.ShellCraterMax * grow);
        return (blast, crater);
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
    public static void Launch(ConstructWorld world, Construct c, int k, Vector3 dir, IWorldQuery query)
    {
        ref var g = ref c.Guns[k];
        ConstructWeapons.MuzzleWorld(c, k, out var muzzle, out _);
        var vel = dir * g.Speed + (g.Shell >= ShellKind.Flak ? c.PointVel(muzzle) : c.Vel);
        var d = c.DefOf(g.Part);
        var target = g.Shell == ShellKind.Missile ? ConstructOrdnance.Lock(world, c, ref g, muzzle, dir, query) : (ushort)0;
        var burn = (int)(d.GunBurn * World.TickRate);
        var fuse = g.Shell switch
        {
            ShellKind.Flare => burn,
            ShellKind.Missile => burn + (int)(ConstructOrdnance.MissileCoast * World.TickRate),
            _ => g.FuseTicks,
        };
        var s = world.AddShell(new CShell
        {
            Kind = g.Shell, By = c.Owner, Gun = c.Id, GunPart = g.Part, Pos = muzzle, Prev = muzzle, Tested = muzzle, Vel = vel, Blast = g.Blast, Damage = g.BlastDamage, Crater = g.Crater,
            Thrust = d.GunThrust, Turn = d.GunTurn * MathF.PI / 180f, Prox = d.GunProx, Burn = burn, Fuse = fuse, Clear = ConstructWorld.ShellMuzzleClear, Target = target,
        });
        if (s != 0)
            world.AddFire(new CFireEvent { Kind = CFireKind.ShellOut, Body = c.Id, Part = g.Part, Shell = s, At = muzzle, To = vel, Muzzle = (byte)g.Shell, Thrust = d.GunThrust, Turn = d.GunTurn * MathF.PI / 180f, Burn = (ushort)Math.Min(burn, ushort.MaxValue), Target = target });
    }

    /// <summary>
    /// Where gun <paramref name="k"/> lands as it points now, in long straight steps against the map (a drop aid: what the
    /// gunner's marker shows, every frame on one PC). False when it doesn't come down in the time looked at.
    /// </summary>
    public static bool Predict(Construct c, int k, IWorldQuery query, out Vector3 land, out float seconds)
    {
        ref var g = ref c.Guns[k];
        ConstructWeapons.MuzzleWorld(c, k, out var p, out var fwd);
        var v = fwd * g.RoundV;
        var push = (g.Effects & GunEffect.Shell) != 0 ? c.DefOf(g.Part).GunThrust : 0f; // (a rocket's arc is its push's, for as long as it burns)
        var burn = push > 0 ? c.DefOf(g.Part).GunBurn : 0f;
        var step = MathF.Max(0.02f, g.MaxRange / MathF.Max(1f, g.RoundV) / 10f);
        seconds = 0;
        for (var i = 0; i < 40; i++)
        {
            var a = GravityAt(p);
            if (seconds < burn && v.LengthSquared() > 1f)
                a += Vector3.Normalize(v) * push;
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

    /// <summary>Where a thing let go at <paramref name="from"/> with velocity <paramref name="vel"/> lands (the bomb-aimer's cross): as <see cref="Predict"/>, against the map.</summary>
    public static bool Fall(Vector3 from, Vector3 vel, IWorldQuery query, out Vector3 land, out float seconds)
    {
        var p = from;
        var v = vel;
        const float step = 0.25f;
        seconds = 0;
        for (var i = 0; i < 120; i++)
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
    public const int MaxShells = 1024; // in the air at once: a gun holds its fire while it is full (designed weapons' bomblets share it: OrdnanceFlight.cs). Was 256: a full Ordnance Rack's salvo is 247. The tick's cost doesn't grow with it (ShellTestsPerTick), a crowded sky is looked at less often (docs/perf/LIMITS.md)
    public const int ShellTestEvery = 3; // a shell looks for what it hit one tick in this many, over all of them (more with more in the air: ShellTestsPerTick)
    public const int ShellTestsPerTick = 22; // ...so that no tick tests many more than this
    public const int ShellPartsPerTick = 400; // parts of a blast walked a tick
    public const int ShellMaxBodies = 256; // bodies one blast reaches (the nearest the broadphase gives)
    public const int BlastDebrisMin = 8, BlastDebrisMax = 32; // wrecks one blast leaves (parts broken off, pieces cut off): 8, and one more each 2 m of its radius, to 32; past that they are gone.
                                                                // (4 Howitzer shells left 110 one-part wrecks of a 2.5 t truck: their first ticks of contacts took 30 to 89 ms)

    /// <summary>The wrecks a blast of <paramref name="radius"/> m may leave (<see cref="BlastDebrisMin"/> to <see cref="BlastDebrisMax"/>).</summary>
    public static int BlastDebris(float radius) => Math.Clamp(BlastDebrisMin + (int)(radius * 0.5f), BlastDebrisMin, BlastDebrisMax);
    public const int ShellMaxAge = 90 * World.TickRate; // ticks in the air before it is given up on
    public const int ShellMuzzleClear = 30; // ticks it passes its own construct

    public CShell[] Shells = []; // (made with the first shell: a clip's little world costs nothing)
    public int ShellCount;
    public int ShellBlastsWaiting; // landed, not yet gone off

    /// <summary>Shells fired, landed and blast parts walked (the perf window and hand checks).</summary>
    public int ShellsFired, ShellsLanded, ShellPartsWalked;

    private ushort _nextShell = 1;
    private int _testEvery = ShellTestEvery, _shellTick;
    public readonly List<OrdnanceFlight.StageDrop> StageDrops = new(16); // spent stages to drop after the shells' step
    private CShellBlast _blast;
    private readonly ushort[] _blastBodies = new ushort[ShellMaxBodies];

    public bool ShellRoom => ShellCount < MaxShells;

    /// <summary>Adds a shell. Its id, or 0 when the pool is full.</summary>
    public ushort AddShell(CShell s)
    {
        if (ShellCount >= MaxShells)
        {
            Caps.Hit("shells in the air", MaxShells); // (guns and racks hold their fire until one lands)
            return 0;
        }
        if (Shells.Length == 0)
            Shells = new CShell[MaxShells];
        s.Id = _nextShell++;
        if (_nextShell == 0)
            _nextShell = 1;
        Shells[ShellCount++] = s;
        ShellsFired++;
        if (s.Kind == ShellKind.Flare)
            FlareCount++;
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
        if (kind == ShellKind.Flare)
            FlareCount++;
    }

    /// <summary>Client: its shell ended (the landing event).</summary>
    public void ClientDropShell(ushort id)
    {
        for (var k = 0; k < ShellCount; k++)
            if (Shells[k].Id == id)
            {
                if (Shells[k].Kind == ShellKind.Flare)
                    FlareCount--;
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
            if (s.Kind >= ShellKind.Rocket || (s.Flags & ShellFlags.Designed) != 0)
                ConstructOrdnance.Fly(this, ref s, dt); // (a rocket's push, a missile's turn, a flare's drag, a designed weapon's flight)
            s.Pos += s.Vel * dt;
            if (s.Look != null)
                ShellLooks.Turn(ref s); // (its nose onto its way: drawn only, the same on every PC)
            if (++s.Age > (s.Life > 0 ? s.Life : ShellMaxAge))
            {
                if (s.Kind == ShellKind.Flare)
                    FlareCount--;
                Shells[k] = Shells[--ShellCount];
            }
        }
    }

    /// <summary>Server, each tick with the guns: shells fly, a third of them look for what they hit, and the next blast goes on.</summary>
    public void StepShells(IWorldQuery query, float dt)
    {
        StepMegas(dt); // (massive blasts' fronts and walks: ConstructMegaBlast.cs)
        if (ShellCount == 0 && !_blast.Active)
            return;
        FlyShells(dt);
        _shellTick++;
        _testEvery = Math.Max(ShellTestEvery, (ShellCount + ShellTestsPerTick - 1) / ShellTestsPerTick); // (a crowded sky is looked at less often, each look over a longer stretch)
        for (var k = 0; k < ShellCount; k++)
        {
            ref var s = ref Shells[k];
            if (s.Landed)
            {
                if (s.Delay <= 0)
                    continue;
                s.Delay--; // (a bomb's fuse, after it hit; a mine's life)
                if (s.Stuck != 0 && Get(s.Stuck) is { } on)
                    s.Pos = on.ToWorld(s.StuckAt);
                if ((s.Flags & ShellFlags.Mine) != 0 && (_shellTick + s.Id) % _testEvery == 0 && OrdnanceFlight.MineTripped(this, ref s))
                    s.Delay = 0;
                continue;
            }
            if (s.Design != null)
                OrdnanceFlight.Step(this, ref s, query); // (stages drop, guidance moves its aim)
            if (s.Fuse > 0 && s.Age >= s.Fuse)
            {
                Burst(ref s); // (its time)
                continue;
            }
            if ((s.Age + s.Id) % _testEvery != 0)
                continue;
            if (s.Prox > 0 && s.Age > ConstructOrdnance.ProxArm && ConstructOrdnance.Near(this, ref s))
            {
                Burst(ref s); // (near enough)
                continue;
            }
            if (s.Fuses != 0 && OrdnanceFlight.FuseCheck(this, ref s, query))
            {
                Burst(ref s); // (its altitude, its distance from its aim, its radio channel)
                continue;
            }
            if (s.Kind == ShellKind.Missile && ((s.Flags & ShellFlags.Designed) == 0 || (s.Flags & ShellFlags.FlareProne) != 0) && s.Age % ConstructOrdnance.FlareLook < _testEvery)
                ConstructOrdnance.LookForFlares(this, ref s);
            var d = s.Pos - s.Tested;
            var len = d.Length();
            if (len < 1e-4f)
                continue;
            d /= len;
            var best = query.Raycast(s.Tested, d, len, QueryFilter.Shots);
            var met = best < len - 1e-3f;
            var skip = s.Age < s.Clear ? Get(s.Gun) : null;
            Construct? struck = null;
            if (Raycast(s.Tested, d, best, out var hit, skip) && hit.Distance < best)
            {
                best = hit.Distance;
                met = true;
                struck = hit.Body;
            }
            if (Targets != null && Targets.RayTarget(s.Tested, d, best, s.Age < s.Clear ? s.Gun : (ushort)0, out var td, out _) && td < best)
            {
                best = td;
                met = true;
                struck = null;
            }
            if (met)
            {
                s.Pos = s.Tested + d * best;
                s.Landed = true;
                ShellBlastsWaiting++;
                if ((s.Flags & ShellFlags.Kinetic) != 0)
                    OrdnanceFlight.Strike(ref s); // (a rod: its speed is its damage)
                if (s.Delay > 0)
                {
                    if (struck != null)
                    {
                        s.Stuck = struck.Id;
                        s.StuckAt = struck.ToLocal(s.Pos);
                    }
                    AddFire(new CFireEvent { Kind = CFireKind.ShellStop, Shell = s.Id, At = s.Pos });
                }
            }
            else
                s.Tested = s.Pos;
        }
        // The blasts: the one under way, then as many more as the tick's parts budget reaches (a cluster's bomblets go off together, a big blast takes ticks).
        var budget = ShellPartsPerTick;
        WalkBlast(ref budget);
        for (var round = 0; round < MaxBlastsPerTick && !_blast.Active && ShellBlastsWaiting > 0 && budget > 0; round++)
        {
            var started = false;
            for (var k = 0; k < ShellCount; k++)
                if (Shells[k].Landed && Shells[k].Delay <= 0)
                {
                    StartBlast(k);
                    started = true;
                    break;
                }
            if (!started)
                break;
            WalkBlast(ref budget);
        }
        OrdnanceFlight.DropStages(this);
    }

    private const int MaxBlastsPerTick = 12; // (each sends its own event and walks the broadphase once)

    /// <summary>A shell goes off in the air: in the blast's turn, and it digs nothing.</summary>
    private void Burst(ref CShell s)
    {
        s.Landed = true;
        s.Crater = 0;
        s.Delay = 0;
        ShellBlastsWaiting++;
    }

    /// <summary>A landed shell goes off: everything that happens at once (the event, the players, the ground), and the list of bodies its blast will walk.</summary>
    private void StartBlast(int k)
    {
        var s = Shells[k];
        Shells[k] = Shells[--ShellCount];
        ShellBlastsWaiting--;
        ShellsLanded++;
        var look = s.Design?.OrdStats?.Flight.Charges is { } charges && s.Charge < charges.Count ? charges[s.Charge].Look : MegaBlasts.Plain;
        if (s.Design != null)
            OrdnanceFlight.GoOff(this, ref s); // (the next charge of a series, bomblets, mines, warheads, rods)
        if (s.Blast > 0 && CapRadius(s.Blast) is var capped && capped < s.Blast)
        {
            s.Crater *= capped / s.Blast; // (the engine's cap and the host's: the crater shrinks with it, the damage at the middle stays)
            s.Blast = capped;
        }
        var mega = s.Blast >= MegaRadius && s.Damage > 0 && s.Kind != ShellKind.Flare && StartMega(s.Pos, s.Blast, s.Damage, s.Crater, look, s.By, s.Gun, s.GunPart);
        AddFire(new CFireEvent { Kind = CFireKind.ShellLand, Shell = s.Id, At = s.Pos, Radius = s.Blast, Muzzle = (byte)s.Kind, Ext = look });
        if (s.Kind == ShellKind.Flare)
        {
            FlareCount--;
            return; // (it burnt out)
        }
        if (s.Damage <= 0 || mega)
            return; // (a dispenser's pop; a massive blast goes on in StepMegas)
        BlastShells(s.Pos, s.Blast, s.Damage); // (ordnance in the air near it: it may go off too, in its turn)
        var by = Get(s.Gun);
        Targets?.ShellBlast(s.Pos, s.Blast, s.Damage, s.Crater, s.Kind == ShellKind.Incendiary, by, s.GunPart);
        var near = Near(s.Pos, s.Blast + 1f, _blastNear);
        var n = 0;
        for (var i = 0; i < near.Count && n < ShellMaxBodies; i++)
            _blastBodies[n++] = near[i].Id;
        _blast = new CShellBlast { Active = n > 0, Incendiary = s.Kind == ShellKind.Incendiary, At = s.Pos, Radius = s.Blast, Damage = s.Damage, By = s.By, BodyCount = n, Part = -1, Debris = BlastDebris(s.Blast) };
    }

    /// <summary>The blast under way, <see cref="ShellPartsPerTick"/> parts on. Walks each body's parts from the last, so one that breaks off never moves those still to come.</summary>
    private void WalkBlast(ref int budget)
    {
        if (!_blast.Active)
            return;
        ref var b = ref _blast;
        DamageBy = b.By;
        var ownDebris = MegaDebrisLeft < 0; // (inside a massive blast's walk its own count rules)
        if (ownDebris)
            MegaDebrisLeft = b.Debris;
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
            WarheadsBegin(c);
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
            WarheadsGone(); // (the bombs it set off on this body, out together: what is still to walk is below them, so keeps its place)
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
        if (ownDebris)
        {
            b.Debris = Math.Max(0, MegaDebrisLeft);
            MegaDebrisLeft = -1;
        }
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
        RoundCount = 0;
        ShellCount = 0;
        ShellBlastsWaiting = 0;
        FlareCount = 0;
        Drops.Clear();
        StageDrops.Clear();
        _blast = default;
        ClearMegas();
    }
}
