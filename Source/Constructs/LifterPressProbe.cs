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
        var name = (tier.Length > 0 && tier != "Scrap" ? tier + " " : "") + "Loader Arm";
        if (ConstructParts.Find(name) is not { Lifter: { } ld } ad || ConstructParts.Find("Press") is not { } press || ConstructParts.Find("Button Die") is not { } die
            || ConstructParts.Find("Piece Rack") is not { } rack || ConstructParts.Find("Lay-down Pad") is not { } pad || Blank() is not { } blank)
            return "press: a part is missing (Loader Arm, Press, Button Die, Piece Rack, Lay-down Pad, a laminate blank)";
        float G(float x, float z) => World.Map.GroundHeight(x, z);
        var r = ld.ReachMax;
        var arm = ConstructBuild.PlaceNew(world, ad, at with { Y = G(at.X, at.Z) }, 0);
        var px = at + new Vector3(r * 0.5f + Half(press).Z, 0, 0);
        var pr = ConstructBuild.PlaceNew(world, press, px with { Y = G(px.X, px.Z) }, MathF.PI / 2);
        var rx = at + new Vector3(-(r * 0.45f + Half(rack).X), 0, 0);
        var rk = ConstructBuild.PlaceNew(world, rack, rx with { Y = G(rx.X, rx.Z) }, 0);
        var pdx = at + new Vector3(0, 0, -(1.6f + Half(pad).Z));
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
        world.StructureDirty.Add(pr.Id);
        return $"press: {name} body {arm.Id}, Press body {pr.Id} (die {(fitted ? die.Name : "not fitted")}, kit {kit}), Piece Rack body {rk.Id} ({stocked} {blank.Name}), Lay-down Pad body {pd.Id}";
    }

    /// <summary>A loose piece let fall over the line's pad (it should be taken in within a second or two of coming to rest).</summary>
    public static string Drop(ConstructWorld world, string piece)
    {
        if (world.Get(LinePad) is not { } pd)
            return "drop: no pad (lifter line or lifter press first)";
        var d = piece == "blank" ? Blank() : ConstructParts.Find(piece);
        if (d == null)
            return $"drop: no part \"{piece}\"";
        Freeform.PartWorld(pd, 0, out var pp, out var pr);
        var pdd = pd.DefOf(0);
        var mid = pp + Vector3.Transform(((pdd.RackHoldMin + pdd.RackHoldMax) * 0.5f) with { Y = pdd.RackHoldMin.Y + 1.2f }, pr);
        var loose = ConstructEdits.DropLoose(world, d, mid, Quaternion.Identity);
        return $"drop: {d.Name} over the pad{(loose == null ? ": no room" : $" (body {loose.Id})")}";
    }
}
