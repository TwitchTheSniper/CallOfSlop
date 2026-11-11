using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The gait profiles (docs/sandbox/GIANTS.md, phase 3), set on a leg with the configure tool and kept in its settings (so
/// they ride blueprints, saves and the wire like any part setting). Simple (the default): no settings, it just walks.
/// Basic: the walker's stride, step height, body height and gait, read from its lead leg (the leg with the highest profile,
/// the first such). Advanced: also each leg's own pace, lift, spread and gait group, read from that leg. Server and client
/// read them alike each tick, so the client's feet follow the same gait. Nothing allocates.
/// </summary>
public sealed partial class Construct
{
    public byte WalkProfile; // 0 Simple, 1 Basic, 2 Advanced (the lead leg's)
    public float WalkStride = 1, WalkLift = 1, WalkStand = 1; // Basic: shares of the build's own
    public byte WalkGaitPick; // Basic: 0 Auto, 1 Tripod, 2 Wave
    public int WalkGaitKey = -1; // the gait settings when the gait was last planned (a change re-plans it)
    public float WalkSpeed = 1; // every profile: the top speed at full throttle, a share of the legs' own (0 to 2; WalkSpeedOf)
}

public static partial class ConstructPhysics
{
    public const int GaitAuto = 0, GaitTripod = 1, GaitWave = 2;

    /// <summary>Reads the walker's profile from its legs (each tick, server and client).</summary>
    public static void WalkTune(Construct c)
    {
        var lead = -1;
        var best = -1f;
        for (var k = 0; k < c.LegCount; k++)
        {
            ref var p = ref c.Parts[c.Legs[k].Part];
            var v = ConstructConfig.Get(p, p.D, CfgKey.WalkProfile, 0);
            if (v > best)
            {
                best = v;
                lead = k;
            }
        }
        c.WalkProfile = (byte)Math.Clamp(best, 0, 2);
        c.WalkSpeed = WalkSpeedOf(c, lead);
        if (lead < 0 || c.WalkProfile == 0)
        {
            c.WalkStride = c.WalkLift = c.WalkStand = 1;
            c.WalkGaitPick = GaitAuto;
        }
        else
        {
            ref var p = ref c.Parts[c.Legs[lead].Part];
            c.WalkStride = Math.Clamp(ConstructConfig.Get(p, p.D, CfgKey.Stride, 100) * 0.01f, 0.5f, 1.5f);
            c.WalkLift = Math.Clamp(ConstructConfig.Get(p, p.D, CfgKey.StepLift, 100) * 0.01f, 0.5f, 2.5f);
            c.WalkStand = Math.Clamp(ConstructConfig.Get(p, p.D, CfgKey.BodyHeight, 100) * 0.01f, 0.6f, 1.3f);
            c.WalkGaitPick = (byte)Math.Clamp(ConstructConfig.Get(p, p.D, CfgKey.Gait, 0), 0, 2);
        }
        var key = c.WalkGaitPick + 3 * c.WalkProfile;
        if (c.WalkProfile == 2)
            for (var k = 0; k < c.LegCount && k < 12; k++)
                key = key * 3 + LegGroupPick(c, k);
        if (key != c.WalkGaitKey)
        {
            c.WalkGaitKey = key;
            c.WalkLive = -1; // (planned again with these settings)
        }
    }

    public const float MaxWalkSpeed = 2; // the speed setting's top: twice the legs' own

    /// <summary>
    /// The walk speed setting (every profile, Simple too), a share of the legs' own top speed: the lead leg's
    /// (<paramref name="lead"/>; -1: found here, as WalkTune does), else the first leg whose setting was changed, else 1.
    /// Any peer (a client's HUD and footfalls read it from the legs' settings, which ride the wire as any setting).
    /// </summary>
    public static float WalkSpeedOf(Construct c, int lead = -1)
    {
        if (c.LegCount == 0)
            return 1;
        if (lead < 0)
        {
            var best = -1f;
            for (var k = 0; k < c.LegCount; k++)
            {
                ref var q = ref c.Parts[c.Legs[k].Part];
                var v = ConstructConfig.Get(q, q.D, CfgKey.WalkProfile, 0);
                if (v > best)
                {
                    best = v;
                    lead = k;
                }
            }
        }
        ref var p = ref c.Parts[c.Legs[lead].Part];
        var set = ConstructConfig.Get(p, p.D, CfgKey.WalkSpeed, 100);
        for (var k = 0; k < c.LegCount && set == 100; k++)
        {
            ref var q = ref c.Parts[c.Legs[k].Part];
            set = ConstructConfig.Get(q, q.D, CfgKey.WalkSpeed, 100);
        }
        return Math.Clamp(set * 0.01f, 0, MaxWalkSpeed);
    }

    /// <summary>Leg <paramref name="k"/>'s own share of a setting (Advanced only; else 1).</summary>
    private static float LegTune(Construct c, int k, CfgKey key, float lo, float hi)
    {
        if (c.WalkProfile != 2)
            return 1;
        ref var p = ref c.Parts[c.Legs[k].Part];
        return Math.Clamp(ConstructConfig.Get(p, p.D, key, 100) * 0.01f, lo, hi);
    }

    public static float LegPace(Construct c, int k) => LegTune(c, k, CfgKey.LegPace, 0.5f, 1.5f);
    public static float LegLift(Construct c, int k) => LegTune(c, k, CfgKey.LegLift, 0.5f, 2f);
    public static float LegSpread(Construct c, int k) => LegTune(c, k, CfgKey.LegSpread, 0.7f, 1.3f);

    /// <summary>Leg <paramref name="k"/>'s tripod half (Advanced): 0 auto, 1 first, 2 second.</summary>
    public static int LegGroupPick(Construct c, int k)
    {
        if (c.WalkProfile != 2)
            return 0;
        ref var p = ref c.Parts[c.Legs[k].Part];
        return (int)Math.Clamp(ConstructConfig.Get(p, p.D, CfgKey.LegGroup, 0), 0, 2);
    }

    /// <summary>Where leg <paramref name="k"/>'s foot rests (body frame): its own rest point, spread by its Advanced setting.</summary>
    public static Vector3 LegRest(Construct c, int k)
    {
        ref var leg = ref c.Legs[k];
        var s = LegSpread(c, k);
        if (s == 1)
            return leg.Rest;
        var r = leg.Rest - leg.Hip;
        return leg.Hip + new Vector3(r.X * s, r.Y, r.Z * s);
    }
}
