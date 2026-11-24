using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Free bodies on rails (Karl, 2026-11-22: far things as data, so a world can hold hundreds of wrecks and haulers). A body
/// falling free round a planet, touching nothing, with nothing running on it (a wreck, a lost crate, a bundle: see
/// <see cref="Eligible"/>), leaves the physics' list and the per-body systems (power, logic) and is moved instead by the
/// same step the physics would give it alone: gravity, the air's damping, the speed and spin limits, the turn. Its pose,
/// speed and spin stay true every tick, so the network, the saves, rays and the broadphase see an ordinary moving body.
/// It comes off the rail (back into the physics as it was) when anything comes within reach of it (the physics' own list
/// adds it, ConstructPhysics.SimList), when <see cref="Construct.Wake"/> is called (a hit, a tool, a weld),
/// when it drops near the ground, when a person comes within <see cref="PersonRange"/>, or when it stops qualifying.
/// Bodies go on in a slice each tick (<see cref="Every"/>), so the test costs a fraction of a body each.
/// Measured with <c>--stress-check</c>: docs/perf/LIMITS.md "Free bodies on rails".
/// </summary>
public static class ConstructRails
{
    /// <summary>Off: every body is stepped by the physics as before (a tool's switch, <c>--no-rails</c>).</summary>
    public static bool On = true;

    /// <summary>Ticks between a body's checks (it goes on, or a rail body looks round for what it could meet).</summary>
    public const int Every = 30;

    /// <summary>No rail within this many metres of a person (their tools, their feet, what they see up close).</summary>
    public const float PersonRange = 300;

    /// <summary>Metres of clear sky over the ground under it a body needs to go on a rail (half of it to stay on until its next check).</summary>
    public const float Floor = 300;

    /// <summary>Bodies on rails after the last step (the profiler's, a tool's).</summary>
    public static int Count { get; private set; }

    private static long _tick;

    /// <summary>
    /// Server, each tick right after the physics: the rail bodies stepped, a slice of the bodies checked. After it, so each body
    /// moves once a tick: one the physics took back this tick (something came near: SimList) is off the rail already and was
    /// stepped there; one that leaves here takes this tick's step on the rail first; one that goes on was stepped by the physics.
    /// </summary>
    public static void Step(ConstructWorld world, IWorldQuery query, float dt)
    {
        StepRails(world, query, dt);
        ConstructCruise.Step(world, query, dt, _tick); // (far NPC ships cruising: ConstructCruise.cs)
    }

    private static void StepRails(ConstructWorld world, IWorldQuery query, float dt)
    {
        _tick++;
        if (!On || !world.UseGrid || GravityFrames.Point is not { } planet || world.PeopleCount == 0) // (a small world steps every body: no rails there)
        {
            if (Count > 0)
                foreach (var c in world.Bodies)
                    c.OnRail = false;
            Count = 0;
            return;
        }
        var ground = World.Map?.Planetoid;
        IndexOnce(world, _tick);
        var n = 0;
        var slice = (int)(_tick % Every);
        foreach (var c in world.Bodies)
        {
            var check = (c.Id + slice) % Every == 0;
            if (c.OnRail)
            {
                ConstructPhysics.RailStep(c, dt);
                if (!StillOn(world, c, planet, ground, check) || Meets(world, c, dt))
                    c.OnRail = false; // (the physics' from the next tick, which steps it with what it meets)
                else
                    n++;
            }
            else if (check && !c.Asleep && Eligible(world, c) && Clear(world, c, planet, ground) && !Meets(world, c, dt))
            {
                c.OnRail = true; // (the physics stepped it this tick: the rail from the next)
                n++;
            }
        }
        Count = n;
        if (n > 0)
            world.GridMoved(); // (they moved: the broadphase is built again when next asked)
    }

    // ---- What a rail body could meet: a coarse hash of every body's centre, made once a tick (the broadphase's 8 m cells
    // made each rail body's look a hundred buckets, 3 ms a tick for 660 of them) ----------------------------------------------

    /// <summary>Metres a side of the hash's cells; a body wider than half of it is looked at by every rail body (<see cref="_big"/>).</summary>
    private const float HashCell = 64;

    private static readonly Dictionary<long, int> _head = new(1024);
    private static int[] _next = new int[256];
    private static Construct[] _items = new Construct[256];
    private static readonly List<Construct> _big = new(16);
    private static float _top;

    private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);

    private static int CellOf(float v) => (int)MathF.Floor(v / HashCell);

    private static long _indexed = -1;

    /// <summary>The hash for this tick, made once (the rails' step makes it; the cruise's when the rails didn't run: ConstructCruise.cs).</summary>
    internal static void IndexOnce(ConstructWorld world, long tick)
    {
        if (_indexed == tick)
            return;
        Index(world);
        _indexed = tick;
    }

    private static void Index(ConstructWorld world)
    {
        _indexed = -1;
        _head.Clear();
        _big.Clear();
        var bodies = world.Bodies;
        if (_items.Length < bodies.Count)
        {
            _items = new Construct[bodies.Count * 2]; // (only when the world holds more bodies than ever before)
            _next = new int[bodies.Count * 2];
        }
        var top = 0f;
        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            top = MathF.Max(top, c.Vel.LengthSquared());
            _items[i] = c;
            if (c.Radius > HashCell * 0.5f)
            {
                _big.Add(c);
                _next[i] = -2;
                continue;
            }
            var at = c.WorldCom;
            var key = Key(CellOf(at.X), CellOf(at.Y), CellOf(at.Z));
            _next[i] = _head.TryGetValue(key, out var first) ? first : -1;
            _head[key] = i;
        }
        _top = MathF.Sqrt(top);
    }

    /// <summary>
    /// Could anything meet it before the next tick is done? Each tick, so a rail body never closes on another rail body, a
    /// station or a ship unseen: what is within its sphere, a tick and a half of its speed and the fastest body's, and a tick
    /// more for the hash's centres (made before this tick's rail steps). The physics' own list takes it back too (SimList).
    /// </summary>
    internal static bool Meets(ConstructWorld world, Construct c, float dt)
    {
        var at = c.WorldCom;
        var ahead = 1.5f * dt; // (a tick of closing, and half again: what it finds is the physics' from the next tick, which meets it with its own speed margin)
        var look = c.Radius + (c.Vel.Length() + _top) * ahead + _top * dt + ConstructWorld.GridMargin;
        foreach (var o in _big)
            if (o != c && Close(c, o, at, ahead))
                return true;
        var q = look + HashCell * 0.5f; // (the others' own radius: at most half a cell)
        int x0 = CellOf(at.X - q), x1 = CellOf(at.X + q), y0 = CellOf(at.Y - q), y1 = CellOf(at.Y + q), z0 = CellOf(at.Z - q), z1 = CellOf(at.Z + q);
        for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++)
                for (var z = z0; z <= z1; z++)
                {
                    if (!_head.TryGetValue(Key(x, y, z), out var i))
                        continue;
                    for (; i >= 0; i = _next[i])
                        if (_items[i] != c && Close(c, _items[i], at, ahead))
                            return true;
                }
        return false;
    }

    private static bool Close(Construct c, Construct o, Vector3 at, float ahead)
    {
        if (o.CarriedBy == c.Id || o.Frame == c.Id && o.Frame != 0)
            return false; // (what it carries or has aboard moves with it: ConstructCruise.cs; a rail body carries nothing)
        var reach = c.Radius + o.Radius + (c.Vel - o.Vel).Length() * ahead + _top * World.TickDt + ConstructWorld.GridMargin;
        return Vector3.DistanceSquared(o.WorldCom, at) < reach * reach;
    }

    /// <summary>Can this body ride a rail: free, alone, with nothing on it that runs?</summary>
    private static bool Eligible(ConstructWorld world, Construct c)
    {
        if (c.Asleep || c.Anchored || c.Dirty || c.CarriedBy != 0 || c.CargoCount > 0 || c.GrabbedBy != 255 || c.NudgeBy != 255 || c.SpaceShip != 0 || c.FramePending || c.Framed || c.Frame != 0
            || c.HeldBy != 0 || c.HeldNear != 0 || c.LooseFrom != 0 || c.RecoilCount > 0 || c.AeroFlying)
            return false;
        if (c.JointCount > 0 || c.WheelCount + c.SpringCount + c.CellCount + c.SurfaceCount + c.RotorCount > 0)
            return false; // (anything it steps for itself)
        if (c.Driven || c.EngineRunning || c.ThrustShow > 0 || c.HoverOn || c.ActiveSignal || c.StabLive || c.RotorLive || c.NpcFaction != 0)
            return false;
        if (!c.Wreck && (c.HasLogic || c.HasBattery || c.Engine >= 0 || c.XWireIn > 0))
            return false; // (a live craft's logic and power keep running; a wreck's are dead)
        for (var k = 0; k < world.CouplingCount; k++)
            if (world.Couplings[k].BodyA == c.Id || world.Couplings[k].BodyB == c.Id)
                return false;
        for (var k = 0; k < world.ConnectorCount; k++)
            if (world.Connectors[k].A.Body == c.Id || world.Connectors[k].B.Body == c.Id)
                return false; // (a rope or a spring on it)
        return true;
    }

    /// <summary>High enough over the ground, and no person near.</summary>
    private static bool Clear(ConstructWorld world, Construct c, PointGravity planet, Planetoid? ground)
    {
        var at = c.WorldCom;
        var span = Every * World.TickDt;
        // The ground under it (the features' smooth height: no tiles), and how far it can sink before its next check.
        var rel = at - planet.Center;
        var d = rel.Length();
        var alt = ground != null ? ground.RoughAltitude(at) : d;
        var sink = MathF.Max(0, -Vector3.Dot(c.Vel, rel) / MathF.Max(d, 1)) * span;
        if (alt - c.Radius - sink < Floor || ground != null && d < ground.Radius) // (or in a hollow world's cavity)
            return false;
        c.RailLow = d - alt + Floor * 0.5f; // (the ground's radius here and half the margin: what the ground ahead rises by meets the rest)
        for (var k = 0; k < world.PeopleCount; k++)
            if (Vector3.DistanceSquared(world.People[k], at) < (PersonRange + c.Radius) * (PersonRange + c.Radius))
                return false;
        return true;
    }

    private static bool StillOn(ConstructWorld world, Construct c, PointGravity planet, Planetoid? ground, bool check)
    {
        if (c.Asleep || c.Anchored || c.Dirty || c.GrabbedBy != 255 || c.NudgeBy != 255 || c.CarriedBy != 0 || c.Framed || c.FramePending || c.RecoilCount > 0)
            return false;
        if ((c.WorldCom - planet.Center).Length() - c.Radius < c.RailLow)
            return false;
        return !check || Eligible(world, c) && Clear(world, c, planet, ground);
    }
}

public static partial class ConstructPhysics
{
    /// <summary>
    /// A rail body's tick (ConstructRails.cs): what <see cref="Step"/> does to a free body that touches nothing, in the same
    /// order (gravity, then <see cref="Integrate"/>'s damping, limits, turn and move), so on and off the rail it moves alike.
    /// </summary>
    internal static void RailStep(Construct c, float dt)
    {
        c.PrevPos = c.Pos;
        c.PrevRot = c.Rot;
        var weight = GravityFrames.Curved || GravityFrames.HookCount > 0 ? ConstructGravity.GravityFor(c, c.WorldCom) : new Vector3(0, -World.Gravity, 0) * World.GravityScale;
        c.Vel += weight * ((1 - c.AntiShare) * dt);
        var air = GravityFrames.Point != null ? AirAt(c.WorldCom) : 1f;
        c.Vel *= air >= 1 ? 0.999f : 1 - 0.001f * air;
        c.AngVel *= 0.985f;
        var top = TopAt(air);
        var v2 = c.Vel.LengthSquared();
        if (c.SpeedKept > top)
        {
            var v = MathF.Sqrt(v2); // (faster than the limit since it came into the world: it keeps that speed, as Integrate)
            if (v > c.SpeedKept)
                c.Vel *= c.SpeedKept / v;
            c.SpeedKept = v > top ? MathF.Min(c.SpeedKept, v) : 0f;
        }
        else if (v2 > top * top)
            c.Vel *= top / MathF.Sqrt(v2);
        var arm = Vector3.Transform(c.Com, c.Rot);
        var w = c.AngVel;
        if (w.LengthSquared() > 1e-12f)
        {
            var spin = new Quaternion(w.X, w.Y, w.Z, 0) * c.Rot;
            c.Rot = Quaternion.Normalize(new Quaternion(c.Rot.X + 0.5f * dt * spin.X, c.Rot.Y + 0.5f * dt * spin.Y, c.Rot.Z + 0.5f * dt * spin.Z, c.Rot.W + 0.5f * dt * spin.W));
        }
        Place(c, c.Vel * dt + arm - Vector3.Transform(c.Com, c.Rot));
    }
}
