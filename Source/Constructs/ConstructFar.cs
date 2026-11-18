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
        if (far == 0)
        {
            ConstructPhysics.Step(world, query, dt);
            SimNear += ConstructPhysics.LastSimulated;
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
    public static bool LogicDue(Construct c) => c.FarLogic <= 1 || (_tick + c.Id) % c.FarLogic == 0;

    /// <summary>At the start of a world's step: which NPC bodies are far (their logic and power on the coarse rate; the awake ones' physics too).</summary>
    public static void Mark(ConstructWorld world)
    {
        _tick++;
        var n = 0;
        var on = Range > 0 && Every > 1 && world.PeopleCount > 0;
        foreach (var c in world.Bodies)
        {
            c.FarRate = c.FarLogic = 0;
            if (!on || c.NpcFaction == 0 || c.Owner != 255 || c.CarriedBy != 0 || c.GrabbedBy != 255 || c.NudgeBy != 255 || c.SpaceShip != 0)
                continue;
            var at = c.WorldCom;
            var reach = Range + c.Radius;
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
        for (var k = 0; k < world.CouplingCount && n > 0; k++)
        {
            ref var cp = ref world.Couplings[k];
            if (world.Get(cp.BodyA) is { FarRate: > 1 } a)
                (a.FarRate, a.FarLogic, n) = (0, 0, n - 1); // (coupled: stepped with its partner, every tick)
            if (world.Get(cp.BodyB) is { FarRate: > 1 } b)
                (b.FarRate, b.FarLogic, n) = (0, 0, n - 1);
        }
        LastFar = n;
    }
}
