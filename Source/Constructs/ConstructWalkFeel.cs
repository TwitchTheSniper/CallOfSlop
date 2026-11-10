using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A walker's ride feel (docs/sandbox/GIANTS.md, phase 4). A bare walker rides rough: each group of feet that comes down
/// thumps the body (it dips and tips towards the feet that landed), the body sways towards its planted feet while a group is
/// in the air, and loose bodies on its deck are bounced about at each footfall. Stabiliser parts (a part file's
/// <c>"stabiliser"</c> block, from tools/constructs/stabiliser_parts.py) each steady so many kg of it: the more and bigger,
/// the less of all of it (<see cref="Construct.WalkRough"/>). The body's motion is real (riders feel it through the ride
/// frame, cargo through its contacts); the thump's sound and the riders' camera shake are each peer's own
/// (Game.UpdateWalkerFeel). Nothing allocates per tick.
/// </summary>
public sealed partial class Construct
{
    public float WalkSteady; // kg its working stabilisers steady (at a rebuild)
    public float WalkRough = 1; // 1: a bare walker's full thump, shake and sway; down to MinRough with enough stabilisers
    public bool WalkLanded; // a group of feet came down this tick (the server's WalkFeet; read by Walk)
    public ulong WalkFeelMask; // the legs in the air when this peer last looked (Game.UpdateWalkerFeel)

    /// <summary>The stabilisers (on every rebuild, with the legs): how rough the ride is.</summary>
    internal void BuildSteady()
    {
        var steady = 0f;
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Steady > 0 && Parts[i].Working)
                steady += Parts[i].D.Steady;
        WalkSteady = steady;
        WalkRough = ConstructPhysics.RoughOf(steady, Mass);
    }
}

public static partial class ConstructPhysics
{
    public const float MinRough = 0.12f; // the steadiest a walker rides, with stabilisers for all its weight
    public const float ThumpDip = 0.35f; // m/s the body drops at a footfall, bare
    public const float ThumpTip = 0.03f; // rad/s it tips towards the feet that came down, bare
    public const float SwayLean = 0.035f; // how far it leans towards its planted feet while a group is in the air (tan of the angle), bare
    public const float CargoPop = 1.6f; // m/s a loose body on the deck is thrown up at a footfall, bare (and a third of it sideways)

    /// <summary>A part file's <c>"stabiliser"</c> block: <c>steady</c>, kg of walker it steadies.</summary>
    public static void ParseStabiliser(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "stabiliser") is not { } s)
            return;
        ConstructParts.Known(s, ["steady"], $"{file}, stabiliser");
        d.Steady = ConstructParts.Num(ConstructParts.Need(s, "steady", $"{file}, stabiliser"), file, "stabiliser.steady");
        ConstructParts.Range(d.Steady, 1, 1e10f, file, "stabiliser.steady");
    }

    /// <summary>How rough a walker of <paramref name="mass"/> kg rides with <paramref name="steady"/> kg of stabilisers: 1 bare, down to <see cref="MinRough"/>.</summary>
    public static float RoughOf(float steady, float mass) => mass > 0 ? Math.Clamp(1 - steady / mass, MinRough, 1) : 1;

    /// <summary>
    /// Server, the tick a group of feet comes down: the body dips and tips towards them, and loose bodies resting on its
    /// deck are thrown up a little (by how rough it rides).
    /// </summary>
    private static void Thump(ConstructWorld world, Construct c)
    {
        var rough = c.WalkRough;
        var at = Vector3.Zero;
        var n = 0;
        for (var k = 0; k < c.LegCount; k++)
            if (c.Legs[k].Group == c.WalkGroup && LegLive(c, k))
            {
                at += c.Legs[k].Foot;
                n++;
            }
        if (n == 0)
            return;
        at /= n;
        var vel = c.Vel;
        vel.Y -= ThumpDip * rough;
        c.Vel = vel;
        var r = at - c.WorldCom;
        r.Y = 0;
        if (r.LengthSquared() > 1)
            c.AngVel += Vector3.Normalize(Vector3.Cross(r, -Vector3.UnitY)) * (ThumpTip * rough); // (the side that took the step goes down)
        ThrowCargo(world, c, rough);
    }

    private static readonly List<Construct> _thumpNear = new(64);

    /// <summary>Loose bodies on the walker's deck (light, over its plan, above its centre of mass) hop at a footfall.</summary>
    private static void ThrowCargo(ConstructWorld world, Construct c, float rough)
    {
        var lo = c.ToWorld(c.LocalMin);
        var hi = c.ToWorld(c.LocalMax);
        var min = Vector3.Min(lo, hi);
        var max = Vector3.Max(lo, hi) + Vector3.UnitY * 4;
        var near = world.Near(min, max, _thumpNear);
        var tick = (int)(c.WalkClock * 977) + c.WalkGroup * 31;
        for (var b = 0; b < near.Count; b++)
        {
            var o = near[b];
            if (o == c || o.Anchored || o.Frozen || o.CarriedBy != 0 || o.Mass > c.Mass * 0.01f)
                continue;
            var local = c.ToLocal(o.WorldCom);
            if (local.X < c.LocalMin.X || local.X > c.LocalMax.X || local.Z < c.LocalMin.Z || local.Z > c.LocalMax.Z || local.Y < c.Com.Y)
                continue;
            // (a jiggle sideways, the same on every run: a hash of the body and the step, no random numbers)
            var h = (o.Id * 7919 + tick) & 1023;
            var side = new Vector3(MathF.Cos(h * 0.00614f), 0, MathF.Sin(h * 0.00614f));
            o.Vel += (Vector3.UnitY + side * 0.33f) * (CargoPop * rough);
            o.Wake();
        }
    }
}
