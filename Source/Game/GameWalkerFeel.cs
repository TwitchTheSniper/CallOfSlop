using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A walker's footfalls on this peer (docs/sandbox/GIANTS.md, phase 4; ConstructWalkFeel.cs): when a leg comes down (the
/// server's feet on the host, the renderer's own on a client) its thump is heard there, deeper for bigger legs, and a player
/// riding the walker (standing on it, seated or driving) feels it as camera shake; one standing near the foot feels a little.
/// Stabilisers on the walker take the thump down with the rest of its ride (<see cref="Construct.WalkRough"/>). Nothing
/// allocates.
/// </summary>
public sealed partial class Game
{
    public const float ThumpShake = 0.55f; // the camera shake a bare titan's footfall gives its riders (0 to 1)

    /// <summary>The largest walker thump shake this peer has felt (probes).</summary>
    public float WalkerThumpFelt;

    private void UpdateWalkerFeel()
    {
        foreach (var c in Constructs.Bodies)
        {
            if (c.LegCount == 0)
                continue;
            var mask = 0UL;
            for (var k = 0; k < c.LegCount && k < 64; k++)
                if (c.Legs[k].Swinging)
                    mask |= 1UL << k;
            var landed = c.WalkFeelMask & ~mask;
            c.WalkFeelMask = mask;
            if (landed != 0)
                WalkerThump(c, landed);
        }
    }

    /// <summary>The legs in <paramref name="landed"/> came down: one thump where they did.</summary>
    private void WalkerThump(Construct c, ulong landed)
    {
        var at = Vector3.Zero;
        var n = 0;
        var reach = 0f;
        for (var k = 0; k < c.LegCount && k < 64; k++)
            if ((landed & (1UL << k)) != 0 && ConstructPhysics.LegLive(c, k))
            {
                at += c.Legs[k].Foot;
                reach = MathF.Max(reach, c.Legs[k].Reach);
                n++;
            }
        if (n == 0)
            return;
        at /= n;
        var size = Math.Clamp(reach / 48f, 0.05f, 1.5f); // (a titan leg is 1)
        var rough = c.WalkRough;
        Audio.Effect3DPitched(Sfx.LandThud, at, Math.Clamp(0.35f + 0.65f * size, 0.2f, 1f) * (0.5f + 0.5f * rough), Math.Clamp(1.1f - 0.6f * MathF.Min(1, size), 0.45f, 1.1f));
        if (Local is not { } me)
            return;
        var felt = 0f;
        if (me.Sim.RideBody == c.Id || c.Driver == me.Id || SitsOn(c, me.Id))
            felt = ThumpShake * MathF.Min(1, size) * rough; // (riding it)
        else
        {
            var near = reach * 2;
            var d = Vector3.Distance(me.RenderEye, at);
            if (d < near)
                felt = ThumpShake * 0.6f * MathF.Min(1, size) * (1 - d / near); // (the ground shakes near a foot)
        }
        if (felt > 0)
        {
            Shake = MathF.Max(Shake, felt);
            WalkerThumpFelt = MathF.Max(WalkerThumpFelt, felt);
        }
    }

    private static bool SitsOn(Construct c, int id)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].SitterPlus1 == id + 1)
                return true;
        return false;
    }
}
