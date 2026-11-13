using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Body styles phase 7b (docs/vehiclekit/BODY-STYLES.md, section 14): lamps on constructs light what is round them through the game's light system, as the
/// muzzle flash does (<see cref="LightGrid.AddDynamic"/>, the shader's <c>ApplyLightAt</c>): a lined bay's lamps (<see cref="ConstructPartDef.Lamps"/>) and a
/// Lamp logic part that is on. The nearest to the eye within <see cref="Reach"/> take whatever dynamic lights the frame has left (at most
/// <see cref="LightGrid.MaxDynamic"/> in all), each faded as it nears the next one left out and the reach, so lights hand over without popping. Nothing
/// allocates in a steady frame: a body's lamp parts are gathered once a rebuild.
/// </summary>
public static class ConstructLamps
{
    /// <summary>How far from the eye a lamp is lit, and how far its light reaches.</summary>
    public const float Reach = 16f, Range = 4.5f, Strength = 1.1f;

    /// <summary>
    /// In the dark (a storm's dusk: <see cref="ColdStorm.Dusk"/>) lamps are lit from further off and throw their light further
    /// and harder: pools of light on the ground. The same few lights as by day (<see cref="LightGrid.MaxDynamic"/>), so a
    /// pixel costs what it did; only their numbers change.
    /// </summary>
    public const float DarkReach = 60f, DarkRange = 10f, DarkStrength = 1.1f;

    /// <summary>A plain lamp's light in the dark: the orange of an old sodium lamp, against the dusk's blue.</summary>
    public static readonly Vector3 DarkColour = new(1f, 0.74f, 0.36f);

    /// <summary>How dark the scene is, 0 (day, or under a roof, where a lamp a metre from a wall would burn it white: the plain reach and range) to 1 (set each frame by the storm's glue, Program.Storm.cs).</summary>
    public static float Dark;

    private const int Most = LightGrid.MaxDynamic + 1;
    private static readonly float[] BestD = new float[Most];
    private static readonly Vector3[] BestPos = new Vector3[Most], BestCol = new Vector3[Most];

    /// <summary>Lamps lit this frame and lamps in reach (the F9 window and the smoke log).</summary>
    public static int LastLit, LastNear;

    public static void AddLights(ConstructWorld world, Vector3 eye, float alpha, bool blendTicks)
    {
        var free = LightGrid.DynamicFree;
        LastLit = LastNear = 0;
        if (free <= 0)
            return;
        var keep = Math.Min(free + 1, Most);
        var n = 0;
        var dark = Math.Clamp(Dark, 0f, 1f);
        float reach = Reach + (DarkReach - Reach) * dark, range = Range + (DarkRange - Range) * dark, strength = Strength + (DarkStrength - Strength) * dark;
        foreach (var c in world.Bodies)
        {
            if (c.SpaceAway != 0)
                continue;
            if (c.LampStamp != c.RebuildStamp)
                Gather(c);
            if (c.LampParts.Length == 0)
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            if (Vector3.DistanceSquared(pos, eye) > (reach + 30f) * (reach + 30f))
                continue; // (a body's parts are within 30 m of its origin)
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            foreach (var i in c.LampParts)
            {
                ref var p = ref c.Parts[i];
                if (p.Broken)
                    continue;
                var d = c.DefOf(i);
                if (d.Logic == CLogic.Lamp)
                {
                    if (d.Mains ? MainsLit(c, i) : p.Out >= 0.5f)
                        Offer(c, i, (d.BoundsMin + d.BoundsMax) * 0.5f, Vector3.Lerp(new Vector3(1f, 0.93f, 0.6f), DarkColour, dark), pos, rot, eye, keep, reach, ref n);
                    continue;
                }
                foreach (var (at, colour) in d.Lamps)
                    Offer(c, i, at, colour, pos, rot, eye, keep, reach, ref n);
            }
        }
        LastNear = n;
        var lit = Math.Min(n, free);
        for (var k = 0; k < lit; k++)
        {
            var fade = Math.Clamp((reach - BestD[k]) / 3f, 0f, 1f);
            if (n > free)
                fade *= Math.Clamp((BestD[free] - BestD[k]) / 1.5f, 0f, 1f); // (the next one left out: a lamp handing over dims rather than pops)
            if (fade > 0.01f)
            {
                LightGrid.AddDynamic(BestPos[k], BestCol[k] * (strength * fade), range);
                LastLit++;
            }
        }
    }

    /// <summary>A lamp at <paramref name="local"/> on part i: kept among the nearest <paramref name="keep"/> (sorted by distance).</summary>
    private static void Offer(Construct c, int i, Vector3 local, Vector3 colour, Vector3 pos, Quaternion rot, Vector3 eye, int keep, float reach, ref int n)
    {
        ref var p = ref c.Parts[i];
        var rest = p.Pos + Rot24.Apply(p.Rot, local);
        var world = c.FrameOf(p.Seg) == 0 ? pos + Vector3.Transform(rest, rot) : c.SegToWorld(p.Seg, rest);
        var dist = Vector3.Distance(world, eye);
        if (dist > reach)
            return;
        var at = Math.Min(n, keep);
        while (at > 0 && BestD[at - 1] > dist)
            at--;
        if (at >= keep)
            return;
        for (var k = Math.Min(n, keep - 1); k > at; k--)
            (BestD[k], BestPos[k], BestCol[k]) = (BestD[k - 1], BestPos[k - 1], BestCol[k - 1]);
        (BestD[at], BestPos[at], BestCol[at]) = (dist, world, colour);
        n++;
    }

    /// <summary>
    /// Whether a lamp part is lit (interiors phase 11). A plain lamp: while its signal is on. A mains lamp (<see cref="ConstructPartDef.Mains"/>):
    /// while the construct has a battery or plant, its power is on (the pose's flags) and the circuit reaches the lamp (<see cref="CPart.Powered"/>,
    /// from the structure on every peer), and, when a wire switches it, while that wire's source is on (a switch's signal, which poses carry).
    /// So a client needs nothing a lamp: mains lamps are left out of the poses' signals. No allocation but the feed list after a rebuild.
    /// </summary>
    public static bool Lit(Construct c, int i)
    {
        if (i < 0 || i >= c.PartCount)
            return false;
        var d = c.DefOf(i);
        if (d.Logic != CLogic.Lamp || c.Parts[i].Broken)
            return false;
        return d.Mains ? MainsLit(c, i) : c.Parts[i].Out >= 0.5f;
    }

    private static bool MainsLit(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        if (p.Broken || !c.HasBattery || !c.PowerOn || !p.Powered)
            return false;
        if (c.LampStamp != c.RebuildStamp)
            Gather(c);
        var f = i < c.MainsFeed.Length ? c.MainsFeed[i] : -1;
        return f < 0 || f < c.PartCount && !c.Parts[f].Broken && c.Parts[f].Out >= 0.5f;
    }

    /// <summary>A body's lamp parts, gathered again after a rebuild (the only allocation: a new list when the parts change).</summary>
    private static void Gather(Construct c)
    {
        c.LampStamp = c.RebuildStamp;
        // (mains lamps' switch wires: the first wire into each, from a part of this body)
        var mains = false;
        for (var i = 0; i < c.PartCount && !mains; i++)
            mains = c.DefOf(i).Mains;
        if (!mains)
            c.MainsFeed = [];
        else
        {
            if (c.MainsFeed.Length < c.PartCount)
                c.MainsFeed = new int[c.PartCount];
            Array.Fill(c.MainsFeed, -1);
            for (var k = 0; k < c.WireCount; k++)
            {
                ref var w = ref c.Wires[k];
                if (w.To < c.PartCount && w.From < c.PartCount && w.ToPort == 0 && c.MainsFeed[w.To] < 0 && c.DefOf(w.To).Mains)
                    c.MainsFeed[w.To] = w.From;
            }
        }
        var count = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i) is var d && (d.Lamps.Length > 0 || d.Logic == CLogic.Lamp))
                count++;
        if (count == 0)
        {
            c.LampParts = [];
            return;
        }
        var list = new int[count];
        count = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i) is var d && (d.Lamps.Length > 0 || d.Logic == CLogic.Lamp))
                list[count++] = i;
        c.LampParts = list;
    }
}
