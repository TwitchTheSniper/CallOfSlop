using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Far bodies' physics on a coarser rate (docs/sandbox/design/faction-controllers.md section 8, the user's answer 8: up to 1,000 faction ships as
/// bodies near players): an NPC crew's awake body further than <see cref="Range"/> from every person steps its physics once every <see cref="Every"/>
/// ticks, by that many ticks' time, staggered by id, in a pass of its own after the near bodies' (ConstructPhysics.Pass). Bodies with a player's hand
/// in them (owned, held, nudged, carried, coupled) are never far. The game gives the people each tick (<see cref="ConstructWorld.People"/>); none given: off, and
/// every body steps every tick as before. Bounded per body (a distance to each person), nothing allocated.
/// </summary>
public static class ConstructFar
{
    /// <summary>Metres from the nearest person past which an NPC body is far (0: off).</summary>
    public static float Range = 1500;

    /// <summary>A far body's physics rate: once every this many ticks.</summary>
    public static byte Every = 4;

    /// <summary>Far bodies at the last step (a tool's and the profiler's).</summary>
    public static int LastFar { get; private set; }

    /// <summary>Bodies the near and far passes stepped, summed since a tool last cleared them.</summary>
    public static long SimNear, SimFar;

    private static uint _tick;

    /// <summary>Marks the far bodies of <paramref name="world"/>, then steps its physics: once as before when none is far, else the near pass and the far pass.</summary>
    public static void Physics(ConstructWorld world, IWorldQuery query, float dt)
    {
        var far = LastFar;
        if (far == 0 || _tick % Every != 0)
        {
            if (far == 0)
            {
                ConstructPhysics.Step(world, query, dt);
                SimNear += ConstructPhysics.LastSimulated;
                return;
            }
            // (not the far bodies' tick: the near pass alone, the far ones met as fixed obstacles)
            try
            {
                (ConstructPhysics.PassTick, ConstructPhysics.Pass) = (_tick, 1);
                ConstructPhysics.Step(world, query, dt);
                SimNear += ConstructPhysics.LastSimulated;
            }
            finally
            {
                ConstructPhysics.Pass = 0;
            }
            return;
        }
        try
        {
            ConstructPhysics.PassTick = _tick;
            ConstructPhysics.Pass = 1;
            ConstructPhysics.Step(world, query, dt);
            SimNear += ConstructPhysics.LastSimulated;
            ConstructPhysics.Pass = 2;
            ConstructPhysics.Step(world, query, dt * Every);
            SimFar += ConstructPhysics.LastSimulated;
        }
        finally
        {
            ConstructPhysics.Pass = 0;
        }
    }

    /// <summary>Is a far body's logic and power due this tick (and so stepped by <see cref="Construct.FarLogic"/> ticks' time)?</summary>
    public static bool LogicDue(Construct c) => !c.OnRail && (c.FarLogic <= 1 || _tick % c.FarLogic == 0); // (one phase, as the physics)

    /// <summary>Ticks one logic step of <paramref name="c"/> stands for (a hold counted in ticks counts down by this).</summary>
    public static int StepTicks(Construct c) => c.FarLogic > 1 ? c.FarLogic : 1;

    /// <summary>Metres either side of <see cref="Range"/> a body must pass to change between near and far (so one at the line doesn't flicker).</summary>
    public const float Hysteresis = 100;

    /// <summary>
    /// At the start of a world's step: which NPC bodies are far (their logic and power on the coarse rate; the awake ones' physics too). Every far
    /// body steps on the same tick (one phase: they meet each other and their ropes' partners in one step). A body changes between near and far only
    /// on the tick after that step, so no tick is stepped twice or skipped: its last near step ends where its first far step's time begins, and its
    /// last far step covers every tick up to its first near one. A body that is taken in hand, coupled or roped to a near body is near at once.
    /// </summary>
    public static void Mark(ConstructWorld world)
    {
        _tick++;
        var n = 0;
        var on = Range > 0 && Every > 1 && world.PeopleCount > 0;
        var turn = _tick % Every == 1 % Every; // (the tick after the far step: the only one a body changes on)
        foreach (var c in world.Bodies)
        {
            if (!on || c.NpcFaction == 0 || c.Owner != 255 || c.CarriedBy != 0 || c.GrabbedBy != 255 || c.NudgeBy != 255 || c.SpaceShip != 0)
            {
                c.FarRate = c.FarLogic = 0;
                continue;
            }
            if (!turn)
            {
                n += c.FarRate > 1 ? 1 : 0;
                continue; // (between turns it keeps what it is)
            }
            var at = c.WorldCom;
            var reach = (c.FarLogic > 1 ? Range - Hysteresis : Range + Hysteresis) + c.Radius;
            c.FarRate = c.FarLogic = 0;
            var near = false;
            for (var k = 0; k < world.PeopleCount && !near; k++)
                near = Vector3.DistanceSquared(world.People[k], at) < reach * reach;
            if (near)
                continue;
            c.FarLogic = Every;
            if (c.Asleep || c.Anchored)
                continue;
            c.FarRate = Every;
            n++;
        }
        if (on)
            foreach (var c in world.Bodies)
            {
                // A loose body in a moving construct's frame steps at its frame's rate (ConstructFrames.cs): met as a fixed
                // obstacle by a far frame between its steps (or meeting one), it would be braked against a deck that moves on.
                if (c.Frame == 0 || c.GrabbedBy != 255 || c.NudgeBy != 255 || world.Get(c.Frame) is not { } f || f.FarRate == c.FarRate && f.FarLogic == c.FarLogic)
                    continue;
                if (!turn && (c.FarRate > 1 || f.FarRate > 1))
                    continue; // (rates change on the turn tick only: no tick stepped twice or skipped)
                var rate = c.Asleep || c.Anchored ? (byte)0 : f.FarRate;
                n += (rate > 1 ? 1 : 0) - (c.FarRate > 1 ? 1 : 0);
                (c.FarRate, c.FarLogic) = (rate, f.FarLogic);
            }
        for (var k = 0; k < world.CouplingCount && n > 0; k++)
        {
            ref var cp = ref world.Couplings[k];
            if (world.Get(cp.BodyA) is { FarRate: > 1 } a)
                (a.FarRate, a.FarLogic, n) = (0, 0, n - 1); // (coupled: stepped with its partner, every tick)
            if (world.Get(cp.BodyB) is { FarRate: > 1 } b)
                (b.FarRate, b.FarLogic, n) = (0, 0, n - 1);
        }
        // A rope or spring to a near body (or to the world) makes it near: the whole connected set steps at one rate (a few rounds for chains).
        for (var round = 0; round < 4 && n > 0; round++)
        {
            var changed = false;
            for (var k = 0; k < world.ConnectorCount; k++)
            {
                ref var cn = ref world.Connectors[k];
                var a = cn.A.Body != 0 ? world.Get(cn.A.Body) : null;
                var b = cn.B.Body != 0 ? world.Get(cn.B.Body) : null;
                var aFar = a is { FarLogic: > 1 };
                var bFar = b is { FarLogic: > 1 };
                if (aFar == bFar && (a != null && b != null))
                    continue;
                if (aFar)
                    (n, changed) = (n - (a!.FarRate > 1 ? 1 : 0), true);
                if (aFar)
                    (a!.FarRate, a.FarLogic) = (0, 0);
                if (bFar)
                    (n, changed) = (n - (b!.FarRate > 1 ? 1 : 0), true);
                if (bFar)
                    (b!.FarRate, b.FarLogic) = (0, 0);
            }
            if (!changed)
                break;
        }
        LastFar = n;
    }
}
