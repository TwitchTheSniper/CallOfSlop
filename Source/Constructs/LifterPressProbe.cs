using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's lifter phase 5 verbs (Program.WindowShot.cs, under "lifter"): "lifter press [TIER]" a press line (an arm, a Press
/// with a Button Die and its copper kit, a Piece Rack stocked with laminate plate blanks and a Lay-down Pad, nothing linked);
/// "lifter drop NAME|blank|button" a loose piece let fall over the line's pad (it takes it in); "lifter save" what the arms hold.
/// Host only; a throwaway world's.
/// </summary>
public static partial class LifterProbe
{
    public static ushort LinePress;

    /// <summary>The blank the probe's press takes: a laminate plate 30 x 30 cm, 50 cm long (the Button Die's, as "Press stock" uses).</summary>
    public static ConstructPartDef? Blank(int cm = 50) =>
        Extrusion.Def(new ExtrusionKey(Extrusion.Presets[1].Mask, 30, 30, (ushort)cm, RecyclingTable.Current.IdOf("laminate"), false));

    public static string Press(ConstructWorld world, Vector3 at, string tier)
    {
        var padName = tier == "largepad" ? "Large Lay-down Pad" : "Lay-down Pad"; // ("lifter press largepad": the Large pad, phase 7)
        if (tier == "largepad")
            tier = "";
        var name = (tier.Length > 0 && tier != "Scrap" ? tier + " " : "") + "Loader Arm";
        if (ConstructParts.Find(name) is not { Lifter: { } ld } ad || ConstructParts.Find("Press") is not { } press || ConstructParts.Find("Button Die") is not { } die
            || ConstructParts.Find("Piece Rack") is not { } rack || ConstructParts.Find(padName) is not { } pad || Blank() is not { } blank)
            return "press: a part is missing (Loader Arm, Press, Button Die, Piece Rack, Lay-down Pad, a laminate blank)";
        float G(float x, float z) => World.Map.GroundHeight(x, z);
        var r = ld.ReachMax;
        var arm = ConstructBuild.PlaceNew(world, ad, at with { Y = G(at.X, at.Z) }, 0);
        var px = at + new Vector3(r * 0.45f + Half(press).X, 0, 0); // (turned so its bays face south and its out north: both ends within the arm's reach from the side)
        var pr = ConstructBuild.PlaceNew(world, press, px with { Y = G(px.X, px.Z) }, 0);
        var rx = at + new Vector3(-(r * 0.45f + Half(rack).X), 0, 0);
        var rk = ConstructBuild.PlaceNew(world, rack, rx with { Y = G(rx.X, rx.Z) }, 0);
        var padMid = (pad.BoundsMin + pad.BoundsMax) * 0.5f;
        var pdx = at + new Vector3(-padMid.X, 0, -(1.6f + Half(pad).Z) - padMid.Z); // (its middle there: a part's origin is its corner cell)
        var pd = ConstructBuild.PlaceNew(world, pad, pdx with { Y = G(pdx.X, pdx.Z) }, 0);
        if (arm == null || pr == null || rk == null || pd == null)
            return "press: no room for the line";
        foreach (var b in new[] { arm, pr, rk, pd })
            b.Anchored = true;
        (LineArm, LinePress, LineRack, LinePad, LineAsm) = (arm.Id, pr.Id, rk.Id, pd.Id, pr.Id);
        var fitted = ConstructPress.FitDie(world, pr, 0, die);
        pr.Rebuild();
        var kit = ConstructPress.FeedKitMaterial(world, pr, 0, 40, RecyclingTable.Current.IdOf("copper"));
        var stocked = 0;
        for (var n = 0; n < 4; n++)
            stocked += world.RackPut(rk, 0, blank) ? 1 : 0;
        ConstructLifters.PointAt(pr, new LiftPoint { Body = pr.Id, Part = 0, Kind = LiftPointKind.Out }, out var outAt);
        world.StructureDirty.Add(pr.Id);
        return $"press: {name} body {arm.Id}, Press body {pr.Id} (out {Vector3.Distance(outAt, arm.Pos):0.0} m from the arm, reach {ld.ReachMin:0.0}-{ld.ReachMax:0.0}, up {ld.Up:0.0} down {ld.Down:0.0}; die {(fitted ? die.Name : "not fitted")}, kit {kit}), Piece Rack body {rk.Id} ({stocked} {blank.Name}), Lay-down Pad body {pd.Id}";
    }

    /// <summary>A loose piece let fall over the line's pad (it should be taken in within a second or two of coming to rest).</summary>
    public static string Drop(ConstructWorld world, string piece)
    {
        if (world.Get(LinePad) is not { } pd)
            return "drop: no pad (lifter line or lifter press first)";
        var loaded = piece.StartsWith("loaded ", StringComparison.Ordinal); // ("drop loaded NAME": a store part with 10 iron ore in it, phase 7)
        if (loaded)
            piece = piece[7..];
        var d =piece == "blank" ? Blank() : ConstructParts.Find(piece);
        if (d == null)
            return $"drop: no part \"{piece}\"";
        Freeform.PartWorld(pd, 0, out var pp, out var pr);
        var pdd = pd.DefOf(0);
        var mid = pp + Vector3.Transform(((pdd.RackHoldMin + pdd.RackHoldMax) * 0.5f) with { Y = pdd.RackHoldMin.Y + 1.2f }, pr);
        var loose = ConstructEdits.DropLoose(world, d, mid, Quaternion.Identity);
        if (loaded && loose != null && world.StoreOf(loose, 0) is { } ls)
            (ls.Amount, ls.Material) = (10, (byte)RecyclingTable.Current.IdOf("iron ore"));
        return $"drop: {d.Name} over the pad{(loose == null ? ": no room" : $" (body {loose.Id})")}";
    }
}

public static partial class LifterProbe
{
    /// <summary>The line's pad as the take-in sees it: its list entry, and each loose one-part body within 12 m in the pad's frame.</summary>
    public static string PadSeen(ConstructWorld world)
    {
        if (world.Get(LinePad) is not { } pd)
            return "";
        var s = $" [pads listed {world.PadBodies.Count}, this {world.PadBodies.Contains(pd.Id)}, stamp {pd.PadStamp}/{pd.RebuildStamp}";
        Freeform.PartWorld(pd, 0, out var pp, out var pr);
        var inv = Quaternion.Conjugate(pr);
        var d = pd.DefOf(0);
        foreach (var b in world.Bodies)
            if (b.PartCount == 1 && b != pd && Vector3.Distance(b.Pos, pd.Pos) < 12)
            {
                var local = Vector3.Transform(b.WorldCom - pp, inv);
                s += $"; body {b.Id} {b.DefOf(0).Name} local {local:0.00} hold {d.RackHoldMin:0.00}-{d.RackHoldMax:0.00} vel {b.Vel.Length():0.00} store {b.Parts[0].Store} health {b.Parts[0].Health:0}/{b.DefOf(0).Health:0} anchored {b.Anchored}"
                    + (world.StoreOf(pd, 0) is { } pst ? $" take-in: {ConstructLifters.TakeInWhy(world, pd, 0, pst, pp, inv, pd.Radius + Vector3.Distance(pd.Pos, pp) + 2f, b) ?? "yes"}" : "");
            }
        return s + "]";
    }
}

public static partial class LifterProbe
{
    /// <summary>
    /// Host: a Lay-down Pad behind the newest arm (away from the station's bay, its long side along the bay), stocked with what the
    /// station's job still needs: a pick pad for an arm that doesn't reach the hold (phase 5).
    /// </summary>
    public static string PickPad(ConstructWorld world, Construct sc, int t, bool stock = true)
    {
        if (ConstructParts.Find("Lay-down Pad") is not { } pad)
            return "pickpad: no Lay-down Pad part";
        Construct? arm = null;
        foreach (var b in world.Bodies)
            if (b.PartCount == 1 && b.DefOf(0).Lifter != null && (arm == null || b.Id > arm.Id))
                arm = b;
        if (arm == null || sc.Parts[t].Station is not { } j)
            return "pickpad: no arm, or the station has no job";
        Freeform.PartWorld(sc, t, out _, out var sr);
        var ad = arm.DefOf(0);
        var armMid = arm.Pos + Vector3.Transform((ad.BoundsMin + ad.BoundsMax) * 0.5f, arm.Rot);
        var mid = (pad.BoundsMin + pad.BoundsMax) * 0.5f;
        var back = Vector3.Transform(-Vector3.UnitZ, sr);
        var centre = armMid + back * ((ad.BoundsMax.Z - ad.BoundsMin.Z) * 0.5f + 0.4f + (pad.BoundsMax.X - pad.BoundsMin.X) * 0.5f);
        var turn = sr * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2); // (its long side, local z, along the bay's x)
        var pos = centre - Vector3.Transform(mid, turn);
        pos.Y = World.Map.GroundHeight(centre.X, centre.Z) - pad.BoundsMin.Y + 0.01f;
        if (ConstructBuild.PlaceNew(world, pad, new FreePlacement { Pos = pos, Rot = turn }) is not { } pd)
            return "pickpad: no room";
        pd.Anchored = true;
        LinePad = pd.Id;
        var n = 0;
        for (var i = 0; i < j.KindN && stock; i++)
            if (ConstructParts.Get(j.KindDef[i]) is { } d)
                for (var m = j.KindFit[i]; m < j.KindNeed[i]; m++)
                    n += world.RackPut(pd, 0, d) ? 1 : 0;
        return $"pickpad: Lay-down Pad body {pd.Id} behind arm body {arm.Id}, {n} pieces of the job's {j.KindN} kinds on it";
    }
}

public static partial class LifterProbe
{
    /// <summary>A cost counter (Stopwatch ticks): calls, total, worst.</summary>
    public struct CostCount
    {
        public long N, Total, Worst;
        public readonly string Text(string name) => N == 0 ? $"{name}: not run" : $"{name}: {N} calls, {Total * 1e6 / N / System.Diagnostics.Stopwatch.Frequency:0.0} us average, worst {Worst * 1e6 / System.Diagnostics.Stopwatch.Frequency:0} us";
    }

    public static CostCount TakeIn, PadCount; // server: the pads' take-in (once a second) and a station's pick pad count (2 Hz while building)
    public static CostCount ScanCost, LookCost, PutCost; // server: the lifter scan (once a second), an idle arm's look for a carry, a carry's put (phase 5b)

    public static void Cost(ref CostCount c, long ticks)
    {
        c.N++;
        c.Total += ticks;
        c.Worst = Math.Max(c.Worst, ticks);
    }
}
