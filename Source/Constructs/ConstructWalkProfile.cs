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
