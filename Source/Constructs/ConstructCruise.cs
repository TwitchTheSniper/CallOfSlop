using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Ships cruising on a rail far from everyone (Sean, 2026-11-24: "a ship travelling between planets, not engaged in combat, with just NPCs
/// aboard, can have a simple predictive trajectory without needing as frequent updates"; "for very unengaged ships we can be checking only
/// 30 seconds kind of time"). Out of a planet's bubble ships already coast on Kepler rails (docs/space/SPACE.md); this is the same idea inside
/// one. An NPC crew's awake ship that is far (<see cref="ConstructFar"/>), or a player's ship flying with nobody aboard, past <see cref="Range"/>
/// from every person, not engaged (<see cref="Construct.NpcBusy"/>, not hit), flying steadily (its speed changed under <see cref="SteadyAccel"/>
/// since it was last looked at, turning under <see cref="SteadySpin"/>), with clear air under every place along the path a spell takes it and
/// nothing near, leaves the physics' list for up to <see cref="HoldTicks"/>: each tick it is moved at its speed along the planet's curve (the
/// speed and the hull turned with it, so a level flight stays level and a hover stays put), its spin held at none. Its logic, power, autopilot
/// and crew go on as a far body's do, and a spell's end takes the fuel and charge its physics would have burned (<see cref="Settle"/>). A spell
/// ends <see cref="GoalLead"/> before the ship would reach the point its order or autopilot flies it to (<see cref="SpellFor"/>); then the
/// physics has it for two far steps, so its crew's steering takes hold and its speed is measured again, and steady still it goes back on. It
/// comes off sooner (on the tick before its next far step) when it is hit, its crew sees something to fight, it is changed, held or coupled, a
/// person comes within <see cref="Range"/>, its fuel would run out, or anything could meet it (the rails' hash, <see cref="ConstructRails.Meets"/>,
/// each tick), and the physics takes it back when an active body comes near (ConstructPhysics.SimList). A far NPC body's spells line up with the
/// far LOD's step (<see cref="ConstructFar.Tick"/>): it goes on right after the tick its far step covers ends and comes off on the tick before
/// its next one, so no tick of its motion is stepped twice or skipped. Server only; nothing on the wire or in a save changes: its pose and speed
/// are true every tick, the poses go out as any far body's. Bounded per body (a few distances a tick, a ray down per 2.5 s of path when it goes
/// on), nothing allocated.
/// </summary>
public static class ConstructCruise
{
    /// <summary>Off: far NPC ships step on the far LOD's rate as before (a tool's switch, <c>--no-cruise</c>).</summary>
    public static bool On = true;

    /// <summary>Metres from the nearest person past which a ship may cruise (with <see cref="ConstructFar.Hysteresis"/> to come off): the far LOD's.</summary>
    public static float Range = 1500;

    /// <summary>Ticks of one cruise spell before the physics has it back for two far steps (its crew's steering, its speed measured): Sean,
    /// 2026-11-24, "for very unengaged ships we can be checking only 30 seconds kind of time". A ship bound for a point nearer than that ends
    /// its spell <see cref="GoalLead"/> before it would get there (<see cref="SpellFor"/>), so its crew slows it in time.</summary>
    public const int HoldTicks = 30 * World.TickRate;

    /// <summary>The shortest spell worth going on for (ticks): a ship nearer its goal than this flies on in the physics.</summary>
    public const int MinTicks = 3 * World.TickRate;

    /// <summary>Seconds before it would reach the point it flies to that a spell ends.</summary>
    public const float GoalLead = 6;

    /// <summary>Metres up or down a spell may take a climbing or sinking ship (its height hold would level it off).</summary>
    public const float ClimbMost = 10;

    /// <summary>Seconds between the places along a spell's path whose ground is looked at before it goes on.</summary>
    public const float PathStep = 2.5f;

    /// <summary>A candidate is looked at once every this many far steps (staggered by id), so the look costs a fraction of a body a tick.</summary>
    public const int LookEvery = 8;

    /// <summary>m/s2: the most its speed may have changed by on average since it was last looked at, to count as steady.</summary>
    public const float SteadyAccel = 3f;

    /// <summary>rad/s: the most it may be turning, to count as steady.</summary>
    public const float SteadySpin = 0.35f;

    /// <summary>Metres of clear air under its hull, here and where a spell takes it, that a ship needs to cruise.</summary>
    public const float Floor = 60;

    /// <summary>Ships cruising after the last step (the profiler's, a tool's).</summary>
    public static int Count { get; private set; }

    /// <summary>Spells started since a tool last cleared them, and ticks cruised.</summary>
    public static long Spells, Ticks;

    /// <summary>Server, each tick after the physics and the rails: cruising ships moved, candidates looked at, spells ended.</summary>
    public static void Step(ConstructWorld world, IWorldQuery query, float dt, long hashTick)
    {
        var every = ConstructFar.Every;
        if (!On || world.PeopleCount == 0 || every <= 1 || ConstructFar.Range <= 0)
        {
            if (Count > 0)
                foreach (var c in world.Bodies)
                    if (c.Cruise)
                    {
                        ConstructPhysics.CruiseStep(c, GravityFrames.Point, dt); // (this tick's move: the physics left it out)
                        Settle(c, ConstructFar.Tick);
                        c.Cruise = false; // (the far LOD is off too: every body is the physics' from the next tick)
                    }
            Count = 0;
            return;
        }
        var tick = ConstructFar.Tick;
        var last = tick % every == every - 1; // (the tick before a far step: a spell ends after this tick's move, or a candidate goes on)
        var planet = GravityFrames.Point;
        var ground = planet != null ? World.Map?.Planetoid : null;
        var n = 0;
        foreach (var c in world.Bodies)
        {
            if (c.Cruise)
            {
                // (a far NPC body comes off only on the tick before its far step; one the physics steps every tick, a player's ship or one the
                // far LOD made near again, on any tick: the physics has it from the next)
                var anyTick = c.FarRate <= 1;
                var off = !Still(world, c) || c.CruiseFuelRate > 0 && c.CruiseFuelRate * (tick + 1 - c.CruiseFrom) >= c.CruiseFuelAt; // (its fuel would run out: the physics has it before)
                Move(world, c, planet, dt);
                if (!off)
                {
                    ConstructRails.IndexOnce(world, hashTick);
                    off = ConstructRails.Meets(world, c, dt);
                }
                if ((last || anyTick) && (off || tick + 1 >= c.CruiseUntil))
                {
                    Settle(c, tick);
                    c.Cruise = false; // (the far step on the next tick takes it from here)
                    Sample(c, tick);
                    c.CruiseRecheck = off ? 0 : tick + 2u * (uint)every; // (a spell's end: looked at again after two far steps)
                }
                else
                {
                    n++;
                    Ticks++;
                }
                continue;
            }
            var far = c.FarRate > 1;
            if (c.OnRail || (far ? !last : c.NpcFaction == 0 && c.Owner == 255))
                continue; // (a far NPC body goes on only after the tick its far step covers ends; a player's ship stepped every tick, on any)
            var due = c.CruiseRecheck != 0 && tick >= c.CruiseRecheck || (far ? (tick / every + c.Id) % LookEvery == 0 : (tick + c.Id) % (LookEvery * every) == 0);
            if (!due)
                continue;
            c.CruiseRecheck = 0;
            var spell = Candidate(world, c, tick, every) && Steady(c, tick) ? SpellFor(c, planet) : 0;
            if (spell >= MinTicks && Clear(world, query, c, planet, ground, spell * dt))
            {
                ConstructRails.IndexOnce(world, hashTick);
                if (!ConstructRails.Meets(world, c, dt))
                {
                    var span = Math.Max(1u, tick - c.CruiseT0);
                    c.CruiseFuelRate = MathF.Max(0, (c.CruiseF0 - c.FuelLeft) / span);
                    c.CruiseChargeRate = MathF.Max(0, (c.CruiseC0 - c.Charge) / span);
                    (c.CruiseFuelAt, c.CruiseChargeAt, c.CruiseFrom) = (c.FuelLeft, c.Charge, tick + 1);
                    c.Cruise = true; // (its step covered this tick: the rail moves it from the next)
                    if (far)
                        c.Vel = (c.Vel + c.CruiseV0) * 0.5f; // (the steady speed: a coarse step's jitter halved)
                    c.AngVel = Vector3.Zero;
                    c.CruiseUntil = tick + 1 + (uint)spell;
                    Spells++;
                    n++;
                    continue;
                }
            }
            Sample(c, tick);
        }
        Count = n;
        if (n > 0)
            world.GridMoved(); // (they moved: the broadphase is built again when next asked)
    }

    /// <summary>The physics takes a cruising ship back before this tick's step (something came near it, it was changed): its spell ended with the last tick.</summary>
    internal static void End(Construct c)
    {
        if (!c.Cruise)
            return;
        Settle(c, ConstructFar.Tick - 1);
        c.Cruise = false;
    }

    /// <summary>What it has now, for the next look's measures (its speed's change, the physics' burn).</summary>
    private static void Sample(Construct c, uint tick)
    {
        c.CruiseV0 = c.Vel;
        c.CruiseT0 = tick;
        c.CruiseF0 = c.FuelLeft;
        c.CruiseC0 = c.Charge;
    }

    /// <summary>
    /// A spell ends after tick <paramref name="tick"/>: it burns what its physics would have (the rate measured before it, times the ticks cruised),
    /// less what its logic and power burned meanwhile (they ran on: their share is in that rate already).
    /// </summary>
    private static void Settle(Construct c, uint tick)
    {
        var ticks = tick + 1 - c.CruiseFrom;
        var fuel = c.CruiseFuelRate * ticks - (c.CruiseFuelAt - c.FuelLeft);
        if (fuel > 0)
            c.UseFuel(fuel);
        var charge = c.CruiseChargeRate * ticks - (c.CruiseChargeAt - c.Charge);
        if (charge > 0)
            c.UseCharge(charge);
    }

    /// <summary>Could this far NPC ship cruise at all: free, awake, alone, unengaged, nobody near?</summary>
    private static bool Candidate(ConstructWorld world, Construct c, uint tick, int every)
    {
        if (c.Asleep || c.Anchored || c.Dirty || c.CarriedBy != 0 || c.GrabbedBy != 255 || c.NudgeBy != 255 || c.SpaceShip != 0 || c.FramePending || c.Framed
            || c.Frame != 0 || c.HeldBy != 0 || c.HeldNear != 0 || c.LooseFrom != 0 || c.RecoilCount > 0 || c.Driver != 255 || c.Wreck)
            return false; // (a player in its seat is a person near it; an owned ship flies an order unseated)
        if (!Still(world, c))
            return false;
        for (var k = 0; k < world.CouplingCount; k++)
            if (world.Couplings[k].BodyA == c.Id || world.Couplings[k].BodyB == c.Id)
                return false;
        for (var k = 0; k < world.ConnectorCount; k++)
            if (world.Connectors[k].A.Body == c.Id || world.Connectors[k].B.Body == c.Id)
                return false; // (a rope or a spring on it)
        return true;
    }

    /// <summary>Can it stay on: unengaged, unchanged, untouched, nobody within <see cref="Range"/> (less the hysteresis once on)?</summary>
    private static bool Still(ConstructWorld world, Construct c)
    {
        if (c.NpcBusy || c.LastHitBy != 255 || c.LastHitBody != 0 || c.Dirty || c.GrabbedBy != 255 || c.NudgeBy != 255 || c.CarriedBy != 0 || c.Framed
            || c.FramePending || c.Frame != 0 || c.SpaceShip != 0 || c.Anchored || c.RecoilCount > 0)
            return false;
        var at = c.WorldCom;
        var reach = (c.Cruise ? Range - ConstructFar.Hysteresis : Range + ConstructFar.Hysteresis) + c.Radius;
        for (var k = 0; k < world.PeopleCount; k++)
            if (Vector3.DistanceSquared(world.People[k], at) < reach * reach)
                return false;
        return true;
    }

    /// <summary>Has its speed changed little since it was last looked at, and is it turning slowly?</summary>
    private static bool Steady(Construct c, uint tick)
    {
        var span = tick - c.CruiseT0;
        if (c.CruiseT0 == 0 || span < 2 || span > 64 * World.TickRate)
            return false; // (not looked at lately: this look is the first sample)
        var accel = (c.Vel - c.CruiseV0).Length() / (span * World.TickDt);
        return accel < SteadyAccel && c.AngVel.LengthSquared() < SteadySpin * SteadySpin;
    }

    /// <summary>
    /// How long a spell it may take (ticks): <see cref="HoldTicks"/>, less when the point its crew's order or its autopilot flies it to
    /// (<see cref="Construct.CruiseGoal"/>, the first stabilizer's held point) is nearer, along the ground, than that at its speed, so the
    /// physics has it <see cref="GoalLead"/> before it gets there. A ship hovering or flying away from its point takes a whole spell.
    /// </summary>
    private static int SpellFor(Construct c, PointGravity? planet)
    {
        var at = c.WorldCom;
        var up = planet != null ? Vector3.Normalize(at - planet.Center) : Vector3.UnitY;
        var spell = (float)HoldTicks;
        if (c.CruiseGoalOn)
            spell = MathF.Min(spell, TicksTo(c.CruiseGoal, at, up, c.Vel));
        if (c.StabCount > 0 && c.Stabs[0].ApWas)
            spell = MathF.Min(spell, TicksTo(c.Stabs[0].HoldPos, at, up, c.Vel));
        var climb = MathF.Abs(Vector3.Dot(c.Vel, up));
        if (climb > 0.05f)
            spell = MathF.Min(spell, ClimbMost / climb * World.TickRate); // (climbing or sinking: its height hold levels it off soon, so a spell takes it this far at most)
        return (int)MathF.Max(0, spell);
    }

    private static float TicksTo(Vector3 goal, Vector3 at, Vector3 up, Vector3 vel)
    {
        var way = goal - at;
        way -= up * Vector3.Dot(way, up); // (along the ground: its height is the autopilot's own)
        var d = way.Length();
        var closing = d > 1 ? Vector3.Dot(vel, way / d) : 0;
        return closing < 1 ? HoldTicks : (d / closing - GoalLead) * World.TickRate;
    }

    /// <summary>Where a spell's move puts its centre of mass after <paramref name="t"/> seconds (round the planet's centre, as <see cref="ConstructPhysics.CruiseStep"/>).</summary>
    private static Vector3 PathAt(Vector3 at, Vector3 vel, PointGravity? planet, float t)
    {
        if (planet == null)
            return at + vel * t;
        var rel = at - planet.Center;
        var r = rel.Length();
        var up = rel / MathF.Max(r, 1);
        var climb = Vector3.Dot(vel, up);
        var along = vel - up * climb;
        var axis = Vector3.Cross(up, along);
        var len = axis.Length();
        if (len > 1e-6f && r > 1)
            rel = Vector3.Transform(rel, Quaternion.CreateFromAxisAngle(axis / len, along.Length() * t / r));
        return planet.Center + rel + Vector3.Normalize(rel) * (climb * t);
    }

    /// <summary>Clear air under it, now and every <see cref="PathStep"/> along the path a spell of <paramref name="span"/> seconds takes it (the ground's smooth height on a planetoid, and a ray down for what stands on it).</summary>
    private static bool Clear(ConstructWorld world, IWorldQuery query, Construct c, PointGravity? planet, Planetoid? ground, float span)
    {
        var at = c.WorldCom;
        var need = Floor + c.Radius;
        if (ground != null && (at - planet!.Center).Length() < ground.Radius)
            return false; // (in a hollow world's cavity)
        var steps = (int)MathF.Ceiling(span / PathStep);
        for (var k = 0; k <= steps; k++)
        {
            var p = PathAt(at, c.Vel, planet, MathF.Min(span, k * PathStep));
            if (ground != null && ground.RoughAltitude(p) < need)
                return false;
            var down = planet != null ? -Vector3.Normalize(p - planet.Center) : -Vector3.UnitY;
            if (query.Raycast(p, down, need, QueryFilter.StaticOnly) < need)
                return false;
        }
        return true;
    }

    /// <summary>A tick on the rail: along the planet's curve at its speed (the speed and the hull turned with it), its climb kept; flat, straight on.</summary>
    private static void Move(ConstructWorld world, Construct c, PointGravity? planet, float dt) => ConstructPhysics.CruiseStep(c, planet, dt);
}

public static partial class ConstructPhysics
{
    /// <summary>A cruising ship's tick (ConstructCruise.cs): moved at its speed, round the planet's centre when there is one, no forces, no spin.</summary>
    internal static void CruiseStep(Construct c, PointGravity? planet, float dt)
    {
        c.PrevPos = c.Pos;
        c.PrevRot = c.Rot;
        var arm = Vector3.Transform(c.Com, c.Rot);
        var at = c.Pos + arm;
        Vector3 to;
        if (planet != null)
        {
            var rel = at - planet.Center;
            var r = rel.Length();
            var up = rel / MathF.Max(r, 1);
            var climb = Vector3.Dot(c.Vel, up);
            var along = c.Vel - up * climb;
            var axis = Vector3.Cross(up, along);
            var len = axis.Length();
            if (len > 1e-6f && r > 1)
            {
                var turn = Quaternion.CreateFromAxisAngle(axis / len, along.Length() * dt / r);
                rel = Vector3.Transform(rel, turn);
                c.Vel = Vector3.Transform(c.Vel, turn);
                c.Rot = Quaternion.Normalize(turn * c.Rot);
                up = Vector3.Normalize(rel);
            }
            to = planet.Center + rel + up * (climb * dt);
        }
        else
            to = at + c.Vel * dt;
        Place(c, to - at + arm - Vector3.Transform(c.Com, c.Rot));
    }
}
