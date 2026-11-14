using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's crane verbs (arms-and-cranes.md phase 6; Program.WindowShot.cs, under "lifter"; host only, a throwaway world's):
/// "lifter crane [Medium|Large] [N]" builds the phase 5 press line (a rack of blanks, a Press with a Button Die, a pad; its Loader Arm set to
/// Off) and over it a crane: two runs of N Crane Rail sections a span apart, their top <see cref="Height"/> m over the ground, and a Crane
/// Bridge on the near run, all one anchored body. "lifter cranecut" takes the far run's middle section out (the crane stops, its piece in
/// its hand); "lifter cranes N" stands N more cranes with nothing under them beside it (a cost probe); "lifter crane" alone again lists them.
/// </summary>
public static partial class LifterProbe
{
    public static ushort CraneBody, Pad2Body;
    private static bool _padCut;
    public const float Height = 6f;

    public static string Crane(ConstructWorld world, Vector3 at, string args)
    {
        var w = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cls = w.Length > 0 ? w[0] : "Medium";
        var line = Press(world, at, "");
        if (world.Get(LineArm) is not { } arm || world.Get(LinePress) is not { } pr || world.Get(LineRack) is not { } rk || world.Get(LinePad) is not { } pd)
            return "crane: " + line;
        ConstructConfig.Put(ref arm.Parts[0], arm.DefOf(0), CfgKey.LiftMode, 3); // (the arm stands still: the crane does the work)
        world.StructureDirty.Add(arm.Id);
        // The box the line stands in, to lay the rails round.
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var b in new[] { arm, pr, rk, pd })
            for (var i = 0; i < b.PartCount; i++)
            {
                var d = b.DefOf(i);
                Freeform.PartWorld(b, i, out var p, out var r);
                for (var k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) == 0 ? d.BoundsMin.X : d.BoundsMax.X, (k & 2) == 0 ? d.BoundsMin.Y : d.BoundsMax.Y, (k & 4) == 0 ? d.BoundsMin.Z : d.BoundsMax.Z);
                    var c = p + Vector3.Transform(corner, r);
                    lo = Vector3.Min(lo, c);
                    hi = Vector3.Max(hi, c);
                }
            }
        var n = w.Length > 1 && int.TryParse(w[1], out var nn) ? Math.Clamp(nn, 1, 64) : 0;
        var made = Build(world, cls, new Vector3((lo.X + hi.X) * 0.5f, 0, (lo.Z + hi.Z) * 0.5f), hi.X - lo.X, hi.Z - lo.Z, n, out var crane, out var why);
        if (crane == null)
            return "crane: " + why;
        CraneBody = crane.Id;
        return $"crane: {made}; the line: {line} (its arm set to Off)";
    }

    /// <summary>A crane centred over (<paramref name="mid"/>.X, Z) whose span crosses <paramref name="wide"/> m in x and whose runs cover <paramref name="deep"/> m in z.</summary>
    private static string Build(ConstructWorld world, string cls, Vector3 mid, float wide, float deep, int sections, out Construct? crane, out string why)
    {
        crane = null;
        why = "";
        if (ConstructParts.Find($"{cls} Crane Bridge") is not { Lifter: { } ld } bridge)
        {
            why = $"no part \"{cls} Crane Bridge\" (Medium, Large or Capital)";
            return "";
        }
        ConstructPartDef? rail = null;
        foreach (var d in ConstructParts.All)
            if (d is { CraneRail: true } && d.Class == bridge.Class)
                rail = d;
        if (rail == null)
        {
            why = "no Crane Rail of its class";
            return "";
        }
        float G(float x, float z) => World.Map.GroundHeight(x, z);
        var len = rail.BoundsMax.Z - rail.BoundsMin.Z;
        var n = sections > 0 ? sections : Math.Clamp((int)MathF.Ceiling((deep + 4f) / len), 2, 64);
        var top = G(mid.X, mid.Z) + Height;
        var nearX = mid.X - ld.Span * 0.5f; // (the near rail's middle; the span's middle over the line's)
        var z0 = mid.Z - n * len * 0.5f - rail.BoundsMin.Z;
        var y = top - rail.BoundsMax.Y;
        var bodies = new List<Construct>();
        for (var side = 0; side < 2; side++)
            for (var i = 0; i < n; i++)
            {
                var b = ConstructBuild.PlaceNew(world, rail, new Vector3(nearX + side * ld.Span, y + rail.BoundsMin.Y - 0.01f, z0 + i * len), 0); // (PlaceNew takes the bottom: the origin goes there plus its own depth)
                if (b == null)
                {
                    why = "no room for a rail section";
                    return "";
                }
                bodies.Add(b);
            }
        // The bridge on the near run's outer side: its Base (the near rail's top line) one cell in +x from its own origin.
        var br = ConstructBuild.PlaceNew(world, bridge, new Vector3(nearX - ld.Base.X, top - ld.Base.Y + bridge.BoundsMin.Y - 0.01f, mid.Z), 0);
        if (br == null)
        {
            why = "no room for the bridge";
            return "";
        }
        foreach (var b in bodies)
            if (!Into(world, br, b))
            {
                why = $"rail body {b.Id} would not join the bridge's";
                return "";
            }
        br.Anchored = true;
        br.Pinned = true; // (held where it is: a crane on no columns)
        br.Dirty = true;
        br.Rebuild();
        world.StructureDirty.Add(br.Id);
        crane = br;
        var cd = ConstructLifters.DefOf(br, 0)!;
        return $"{cls} Crane Bridge body {br.Id}: {2 * n} rail sections of {len:0} m, top {top:0.0} m; reach across {cd.AcrossMin:0.0}-{cd.AcrossMax:0.0}, along {cd.RunLo:0.0} to {cd.RunHi:0.0}, rails {(cd.RailWhy == 0 ? "ok" : cd.RailWhy == 1 ? "not on a rail" : "far end has no rail")} (the line is {wide:0.0} x {deep:0.0} m)";
    }

    /// <summary>"lifter cranecut": the far run's middle section out of the crane (its reach goes; a piece in its hand stays there).</summary>
    public static string CraneCut(ConstructWorld world, string arg = "")
    {
        FindCrane(world);
        if (world.Get(CraneBody) is not { } c)
            return "cranecut: no crane (lifter crane first)";
        if (arg == "pad")
        {
            // "lifter cranecut pad": once, when the crane carries a piece to the pad, the near run's first section (under the pad) goes.
            // (now: when it carries a piece, the near run's section over where the piece is going; the bridge is part 0 at the body's origin, unturned)
            if (ConstructLifters.Find(world, c.Id, 0) is not { State: LiftState.Carry } l || l.Age < 30 || _padCut)
                return _padCut ? "cranecut pad: done before" : "cranecut pad: not carrying yet";
            var under = -1;
            for (var i = 1; i < c.PartCount && under < 0; i++)
                if (c.DefOf(i).CraneRail && MathF.Abs(c.Parts[i].Pos.X - 1) < 0.2f && l.To.Z >= c.Parts[i].Pos.Z + c.DefOf(i).BoundsMin.Z && l.To.Z <= c.Parts[i].Pos.Z + c.DefOf(i).BoundsMax.Z)
                    under = i;
            if (under < 0)
                return "cranecut pad: no section over its put point";
            _padCut = true;
            world.RemovePart(c, under);
            c.Rebuild();
            world.StructureDirty.Add(c.Id);
            var pd = ConstructLifters.DefOf(c, 0);
            return $"cranecut pad: the near run's first section out mid-carry; rails now {(pd is { RailWhy: 0 } ? $"along {pd.RunLo:0.0} to {pd.RunHi:0.0}" : "none")}";
        }
        var rails = new List<int>();
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).CraneRail)
                rails.Add(i);
        if (rails.Count < 2)
            return "cranecut: no rails";
        var cut = rails[rails.Count * 3 / 4 - 1]; // (the far run's section under the line: z -6 to -2 with five a run)
        world.RemovePart(c, cut);
        c.Rebuild();
        world.StructureDirty.Add(c.Id);
        var cd = ConstructLifters.DefOf(c, 0);
        return $"cranecut: removed rail part {cut}; the crane's rails now: {(cd is { RailWhy: 0 } ? $"along {cd.RunLo:0.0} to {cd.RunHi:0.0}" : cd is { RailWhy: 1 } ? "not on a rail" : "its far end has no rail")}";
    }

    /// <summary>"lifter cranes N": N more Medium cranes in a row east of the player, nothing under them (the step's cost with several cranes).</summary>
    public static string Cranes(ConstructWorld world, Vector3 at, int count)
    {
        var s = "";
        for (var k = 0; k < count; k++)
        {
            var made = Build(world, "Medium", at + new Vector3(14 + 12 * k, 0, 0), 4, 8, 3, out var c, out var why);
            s += c == null ? $" {k}: {why};" : $" {c.Id};";
        }
        return $"cranes: {count} placed:{s}";
    }
}

public static partial class LifterProbe
{
    /// <summary>"lifter pad2" (phase 7): a second Lay-down Pad north of the line's arm, under the crane: somewhere for a hand order to put a pad's piece.</summary>
    public static string Pad2(ConstructWorld world)
    {
        if (world.Get(LineArm) is not { } arm || ConstructParts.Find("Lay-down Pad") is not { } pad)
            return "pad2: no line (lifter press or lifter crane first)";
        var padMid = (pad.BoundsMin + pad.BoundsMax) * 0.5f;
        var at = arm.Pos + new Vector3(-padMid.X, 0, 1.6f + Half(pad).Z - padMid.Z);
        var pd = ConstructBuild.PlaceNew(world, pad, at with { Y = World.Map.GroundHeight(at.X, at.Z) }, 0);
        if (pd == null)
            return "pad2: no room";
        pd.Anchored = true;
        Pad2Body = pd.Id;
        world.StructureDirty.Add(pd.Id);
        return $"pad2: Lay-down Pad body {pd.Id}";
    }
}

public static partial class LifterProbe
{
    /// <summary>
    /// "lifter stationcrane" (phase 7): a Medium crane over a Building Station's bay (a body of its own, pinned, as "lifter crane" builds),
    /// every Loader Arm on the station's body set to Off, so the station's fits that a lifter makes are the crane's.
    /// </summary>
    public static string StationCrane(ConstructWorld world, Construct sc, int t)
    {
        ConstructStation.Bay(sc, t, out var floor, out _, out var bay);
        var arms = 0;
        for (var i = 0; i < sc.PartCount; i++)
            if (sc.DefOf(i).Lifter is { Bridge: false })
            {
                ConstructConfig.Put(ref sc.Parts[i], sc.DefOf(i), CfgKey.LiftMode, 3);
                arms++;
            }
        world.StructureDirty.Add(sc.Id);
        Freeform.PartWorld(sc, t, out var hold, out _);
        var mid = (floor + hold) * 0.5f;
        var made = Build(world, "Medium", mid with { Y = 0 }, MathF.Abs(floor.X - hold.X) + bay.X, MathF.Abs(floor.Z - hold.Z) + bay.Z, 0, out var crane, out var why);
        if (crane == null)
            return "stationcrane: " + why;
        StationCraneBody = crane.Id;
        return $"stationcrane: {made}; {arms} arm(s) set to Off; bay floor {floor:0.0}, the station at {hold:0.0}";
    }

    public static ushort StationCraneBody;
}

public static partial class LifterProbe
{
    /// <summary>No rig built: the first body with a crane on it (the Production Showcase's) is the crane the verbs work on.</summary>
    public static void FindCrane(ConstructWorld world)
    {
        if (world.Get(CraneBody) != null)
            return;
        foreach (var lb in world.LiftBodies)
            for (var li = 0; li < lb.N; li++)
                if (lb.Body != StationCraneBody && world.Get(lb.Body) is { } lc && lb.Lifts[li].Part < lc.PartCount && ConstructLifters.DefOf(lc, lb.Lifts[li].Part) is { Bridge: true })
                    CraneBody = lc.Id;
    }
}
