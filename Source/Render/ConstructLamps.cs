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
    public const float Reach = 16f, Range = 4.5f, Strength = 1.4f; // (the strengths go through the shader's soft knee on an unlit map: Lighting.cs ApplyLightAt)

    /// <summary>
    /// In the dark (a storm's dusk: <see cref="ColdStorm.Dusk"/>) lamps are lit from further off and throw their light further
    /// and harder: pools of light on the ground. The same few lights as by day (<see cref="LightGrid.MaxDynamic"/>), so a
    /// pixel costs what it did; only their numbers change.
    /// </summary>
    public const float DarkReach = 60f, DarkRange = 15f, DarkStrength = 2.6f;

    /// <summary>A plain lamp's light in the dark: the orange of an old sodium lamp, against the dusk's blue.</summary>
    public static readonly Vector3 DarkColour = new(1f, 0.74f, 0.36f);

    /// <summary>How dark the scene is, 0 (day, or under a roof, where a lamp a metre from a wall would burn it white: the plain reach and range) to 1 (set each frame by the storm's glue, Program.Storm.cs).</summary>
    public static float Dark;

    // (twice the lights as candidates: the nearest are then put in order by whether the eye sees them, Seen)
    private const int Most = LightGrid.MaxDynamic * 2 + 1;

    /// <summary>
    /// How much farther a lamp counts when a wall, a floor or a deck stands between it and the eye (m). Karl, 2026-11-21: a Zero
    /// Day room went dark at one end or the other by where you stood, its deck and the one under it holding 30 to 60 lamps in reach
    /// and only the nearest lit; the lamps of the room you are in now come first, and a lamp behind a wall still beats a far one in
    /// the room (its light may come round a door).
    /// </summary>
    public const float HiddenPenalty = 8f;

    private static readonly float[] BestD = new float[Most], BestKey = new float[Most];
    private static readonly int[] Order = new int[Most];
    private static readonly Vector3[] BestPos = new Vector3[Most], BestCol = new Vector3[Most];
    // (who each kept lamp is, for its shadow: LampShadows.cs; sub -1: on a moving segment, unshadowed)
    private static readonly Construct?[] BestBody = new Construct?[Most];
    private static readonly int[] BestPart = new int[Most], BestSub = new int[Most];
    private static readonly Vector3[] BestLocal = new Vector3[Most], BestBodyPos = new Vector3[Most];
    private static readonly Quaternion[] BestRot = new Quaternion[Most];

    /// <summary>Lamps lit this frame and lamps in reach (the F9 window and the smoke log).</summary>
    public static int LastLit, LastNear;

    public static void AddLights(ConstructWorld world, Vector3 eye, float alpha, bool blendTicks)
    {
        var free = LightGrid.DynamicFree;
        LastLit = LastNear = 0;
        if (free <= 0)
            return;
        var keep = Math.Min(free * 2 + 1, Most);
        var n = 0;
        var dark = Math.Clamp(Dark, 0f, 1f);
        float reach = Reach + (DarkReach - Reach) * dark, range = Range + (DarkRange - Range) * dark, strength = Strength + (DarkStrength - Strength) * dark;
        foreach (var c in world.Bodies)
        {
            if (c.SpaceAway != 0)
                continue;
            if (c.LampStamp != c.StructStamp)
                Gather(c);
            if (c.LampParts.Length == 0)
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            if (Vector3.DistanceSquared(c.WorldCom, eye) > (reach + c.Radius) * (reach + c.Radius))
                continue; // (every part is within Radius of the centre of mass: a 200 m works is lit at its far end too)
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
                        Offer(c, i, 0, (d.BoundsMin + d.BoundsMax) * 0.5f, Vector3.Lerp(new Vector3(1f, 0.93f, 0.6f), DarkColour, dark), pos, rot, eye, keep, reach, ref n);
                    continue;
                }
                var sub = 0;
                foreach (var (at, colour) in d.Lamps)
                    Offer(c, i, sub++, at, colour, pos, rot, eye, keep, reach, ref n);
                // (a ship's light strips go out with its power; a hull with no battery keeps them, as they glow; a lined bay's lamps already light its panels)
                if (d.Lamps.Length == 0 && (!c.HasBattery || c.PowerOn))
                    foreach (var (at, colour) in StripLamps(d))
                        Offer(c, i, sub++, at, colour, pos, rot, eye, keep, reach, ref n);
            }
        }
        LastNear = n;
        var cand = Math.Min(n, keep);
        _frame++;
        var rays = 0;
        // The candidates in order of distance, a lamp the eye can't see counting HiddenPenalty farther (insertion sort: few, mostly in order).
        for (var k = 0; k < cand; k++)
        {
            BestKey[k] = BestD[k] + (SeenCached(world, eye, k, ref rays) ? 0f : HiddenPenalty);
            var at = k;
            while (at > 0 && BestKey[Order[at - 1]] > BestKey[k])
            {
                Order[at] = Order[at - 1];
                at--;
            }
            Order[at] = k;
        }
        var lit = Math.Min(cand, free);
        for (var j = 0; j < lit; j++)
        {
            var k = Order[j];
            var fade = Math.Clamp((reach - BestD[k]) / 3f, 0f, 1f);
            if (cand > free)
                fade *= Math.Clamp((BestKey[Order[free]] - BestKey[k]) / 1.5f, 0f, 1f); // (the next one left out: a lamp handing over dims rather than pops)
            if (fade > 0.01f)
            {
                var light = LightGrid.MaxDynamic - LightGrid.DynamicFree;
                LightGrid.AddDynamic(BestPos[k], BestCol[k] * (strength * fade), range);
                if (BestSub[k] >= 0 && BestBody[k] is { } body)
                    LampShadows.Offer(light, body, BestPart[k], BestSub[k], BestLocal[k], BestBodyPos[k], BestRot[k]); // (its walls keep its light in: LampShadows.cs)
                LastLit++;
            }
        }
        for (var k = 0; k < Most; k++)
            BestBody[k] = null; // (no references kept past the frame)
    }

    /// <summary>Rays a frame for <see cref="Seen"/>, nearest lamps first, and how many frames an answer is kept.</summary>
    private const int RaysPerFrame = 8, SeenKeep = 12;

    private struct SeenEntry
    {
        public int Body, Part, Sub, Frame;
        public bool Seen;
    }

    private static readonly SeenEntry[] SeenCache = new SeenEntry[256]; // (open slots keyed by lamp; a clash takes the slot)
    private static int _frame;

    /// <summary>
    /// <see cref="Seen"/> kept a few frames per lamp: a ship deck has 60 lamps in reach, and a ray each a frame cost 3 ms on the
    /// cloud box. A lamp not tested yet counts as seen (by distance alone, as before) until its ray comes round.
    /// </summary>
    private static bool SeenCached(ConstructWorld world, Vector3 eye, int k, ref int rays)
    {
        if (BestSub[k] < 0 || BestBody[k] is not { } c)
            return true;
        var slot = (int)((uint)(c.Id * 73856093 ^ BestPart[k] * 19349663 ^ BestSub[k] * 83492791) % (uint)SeenCache.Length);
        ref var e = ref SeenCache[slot];
        var mine = e.Frame != 0 && e.Body == c.Id && e.Part == BestPart[k] && e.Sub == BestSub[k];
        if (mine && _frame - e.Frame < SeenKeep)
            return e.Seen;
        if (rays >= RaysPerFrame)
            return !mine || e.Seen;
        rays++;
        e = new SeenEntry { Body = c.Id, Part = BestPart[k], Sub = BestSub[k], Frame = _frame, Seen = Seen(world, eye, k) };
        return e.Seen;
    }

    /// <summary>
    /// Whether the eye sees candidate <paramref name="k"/>: a ray from the eye to the lamp through the construct world, which holds
    /// the bodies at their tick pose, so both ends are moved there from the pose the lamp's body is drawn at (a ship at speed is
    /// metres from its tick pose between ticks). A lamp on a moving segment, or within 0.6 m, counts as seen; glass doesn't hide one.
    /// </summary>
    private static bool Seen(ConstructWorld world, Vector3 eye, int k)
    {
        if (BestSub[k] < 0 || BestBody[k] is not { } c)
            return true;
        var inv = Quaternion.Conjugate(BestRot[k]);
        var from = c.Pos + Vector3.Transform(Vector3.Transform(eye - BestBodyPos[k], inv), c.Rot);
        var to = c.Pos + Vector3.Transform(BestLocal[k], c.Rot);
        var dir = to - from;
        var d = dir.Length();
        if (d < 0.6f)
            return true;
        return !world.Raycast(from, dir / d, d - 0.35f, out var hit) || hit.Glass; // (0.35 m short: the lamp's own fitting)
    }

    /// <summary>Lamp <paramref name="sub"/> at <paramref name="local"/> on part i: kept among the nearest <paramref name="keep"/> (sorted by distance).</summary>
    private static void Offer(Construct c, int i, int sub, Vector3 local, Vector3 colour, Vector3 pos, Quaternion rot, Vector3 eye, int keep, float reach, ref int n)
    {
        ref var p = ref c.Parts[i];
        var rest = p.Pos + Rot24.Apply(p.Rot, local);
        var still = c.FrameOf(p.Seg) == 0;
        var world = still ? pos + Vector3.Transform(rest, rot) : c.SegToWorld(p.Seg, rest);
        var dist = Vector3.Distance(world, eye);
        if (dist > reach)
            return;
        var at = Math.Min(n, keep);
        while (at > 0 && BestD[at - 1] > dist)
            at--;
        if (at >= keep)
            return;
        for (var k = Math.Min(n, keep - 1); k > at; k--)
        {
            (BestD[k], BestPos[k], BestCol[k]) = (BestD[k - 1], BestPos[k - 1], BestCol[k - 1]);
            (BestBody[k], BestPart[k], BestSub[k], BestLocal[k], BestBodyPos[k], BestRot[k]) = (BestBody[k - 1], BestPart[k - 1], BestSub[k - 1], BestLocal[k - 1], BestBodyPos[k - 1], BestRot[k - 1]);
        }
        (BestD[at], BestPos[at], BestCol[at]) = (dist, world, colour);
        (BestBody[at], BestPart[at], BestSub[at], BestLocal[at], BestBodyPos[at], BestRot[at]) = (c, i, still ? sub : -1, rest, pos, rot);
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
        if (c.LampStamp != c.StructStamp)
            Gather(c);
        var f = i < c.MainsFeed.Length ? c.MainsFeed[i] : -1;
        return f < 0 || f < c.PartCount && !c.Parts[f].Broken && c.Parts[f].Out >= 0.5f;
    }

    /// <summary>A body's lamp parts, gathered again after a rebuild (the only allocation: a new list when the parts change).</summary>
    private static void Gather(Construct c)
    {
        c.LampStamp = c.StructStamp;
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
            if (c.DefOf(i) is var d && (d.Lamps.Length > 0 || d.Logic == CLogic.Lamp || StripLamps(d).Length > 0))
                count++; // (StripLamps: once per part type, kept only while the type lives)
        if (count == 0)
        {
            c.LampParts = [];
            return;
        }
        var list = new int[count];
        count = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i) is var d && (d.Lamps.Length > 0 || d.Logic == CLogic.Lamp || StripLamps(d).Length > 0))
                list[count++] = i;
        c.LampParts = list;
    }

    /// <summary>A light strip's lamps: one every this far along it (m), this far under it.</summary>
    public const float StripSpacing = 1.8f, StripDrop = 0.2f;

    private sealed class StripSet(  (Vector3 At, Vector3 Colour)[] lamps) { public readonly (Vector3 At, Vector3 Colour)[] Lamps = lamps; }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConstructPartDef, StripSet> Strips = new(); // (weak: a run-time part's type is let go)

    /// <summary>
    /// Karl, 2026-11-21: a ship's ceiling light strips glowed white but lit nothing round them (only its wall lamps were lights), so a
    /// corridor under a bright bar stayed dark. A part's glowing boxes that are pale, flat and facing down (thin in the part's y, at
    /// least 0.12 m2 and 0.6 m long: a ceiling strip, not a screen or a coloured stripe) light the room as a row of lamps, one every
    /// <see cref="StripSpacing"/> along the strip, <see cref="StripDrop"/> under it, in its colour. Worked out once per part type.
    /// </summary>
    public static (Vector3 At, Vector3 Colour)[] StripLamps(ConstructPartDef d)
    {
        if (Strips.TryGetValue(d, out var have))
            return have.Lamps;
        List<(Vector3, Vector3)>? found = null;
        foreach (var b in d.Boxes)
        {
            if (!b.Glow || b.Hidden || b.Glass > 0f)
                continue;
            var col = b.Color;
            var lo = MathF.Min(col.X, MathF.Min(col.Y, col.Z));
            var hi = MathF.Max(col.X, MathF.Max(col.Y, col.Z));
            if (lo < 0.7f || hi - lo > 0.3f || b.Half.Y > 0.03f || b.Half.Y > MathF.Min(b.Half.X, b.Half.Z))
                continue; // (only pale, flat, ceiling-wise strips)
            float sx = b.Half.X * 2f, sz = b.Half.Z * 2f;
            if (sx * sz < 0.12f || MathF.Max(sx, sz) < 0.6f)
                continue;
            var along = sx >= sz ? Vector3.UnitX : Vector3.UnitZ;
            var len = MathF.Max(sx, sz);
            var count = Math.Max(1, (int)MathF.Round(len / StripSpacing));
            var colour = col / hi * 0.9f;
            found ??= new List<(Vector3, Vector3)>();
            for (var k = 0; k < count; k++)
            {
                var t = ((k + 0.5f) / count - 0.5f) * len;
                found.Add((b.Center + along * t - Vector3.UnitY * (b.Half.Y + StripDrop), colour));
            }
        }
        var r = found?.ToArray() ?? [];
        Strips.AddOrUpdate(d, new StripSet(r));
        return r;
    }
}
