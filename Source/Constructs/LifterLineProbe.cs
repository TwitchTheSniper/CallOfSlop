using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's lifter phase 4 verbs (Program.WindowShot.cs, under "lifter"): a test line on flat ground (an arm, an Assembler, a
/// Piece Rack and a Lay-down Pad round it, nothing linked), a piece put in a machine's out as if made, settings, the arms' mains,
/// and a printout of every arm's points and ports. Host only; a throwaway world's.
/// </summary>
public static partial class LifterProbe
{
    public static ushort LineArm, LineAsm, LineRack, LinePad;

    /// <summary>Host: the line round <paramref name="at"/> (ground): the arm, an Assembler east set to a recipe that uses Gear Sets, a rack west stocked, a pad north.</summary>
    public static string Line(ConstructWorld world, Vector3 at, string tier, out Construct? arm)
    {
        arm = null;
        var name = (tier.Length > 0 && tier != "Scrap" ? tier + " " : "") + "Loader Arm";
        if (ConstructParts.Find(name) is not { Lifter: { } ld } ad || ConstructParts.Find("Assembler") is not { } asm
            || ConstructParts.Find("Piece Rack") is not { } rack || ConstructParts.Find("Lay-down Pad") is not { } pad || ConstructParts.Find("Gear Set") is not { } gear)
            return "a part is missing (Loader Arm, Assembler, Piece Rack, Lay-down Pad, Gear Set)";
        float G(float x, float z) => World.Map.GroundHeight(x, z);
        var r = ld.ReachMax;
        arm = world.Get(BaseBody) is { } based && based.DefOf(0) == ad ? based : ConstructBuild.PlaceNew(world, ad, at with { Y = G(at.X, at.Z) }, 0); // (round the powered base's arm when "lifter base" made one)
        var ax = at + new Vector3(r * 0.55f + Half(asm).X, 0, 0);
        var am = ConstructBuild.PlaceNew(world, asm, ax with { Y = G(ax.X, ax.Z) }, MathF.PI / 2);
        var rx = at + new Vector3(-(r * 0.45f + Half(rack).X), 0, 0);
        var rk = ConstructBuild.PlaceNew(world, rack, rx with { Y = G(rx.X, rx.Z) }, 0);
        var px = at + new Vector3(0, 0, -(1.6f + Half(pad).Z));
        var pd = ConstructBuild.PlaceNew(world, pad, px with { Y = G(px.X, px.Z) }, 0);
        if (arm == null || am == null || rk == null || pd == null)
            return "no room for the line";
        foreach (var b in new[] { arm, am, rk, pd })
            b.Anchored = true;
        (LineArm, LineAsm, LineRack, LinePad) = (arm.Id, am.Id, rk.Id, pd.Id);
        // The Assembler's recipe: the first of its family that uses a Gear Set.
        var list = AssemblingTable.Current.ByMachine[asm.AsmMachine];
        var recipe = "";
        for (var k = 0; k < list.Length; k++)
            if (list[k].CountOf(gear.Id) > 0 && list[k].MinTier <= asm.Tier)
            {
                ConstructConfig.Put(ref am.Parts[0], asm, CfgKey.Recipe, k + 1);
                world.StructureDirty.Add(am.Id);
                recipe = list[k].Output.Name;
                break;
            }
        // The rack: Gear Sets (wanted by the Assembler) and something it does not use.
        var stocked = 0;
        for (var n = 0; n < 6; n++)
            stocked += world.RackPut(rk, 0, gear) ? 1 : 0;
        var other = ConstructParts.Find("Square Beam 2 m");
        for (var n = 0; n < 2 && other != null; n++)
            stocked += world.RackPut(rk, 0, other) ? 1 : 0;
        return $"line: {name} body {arm.Id}, Assembler body {am.Id} (recipe {(recipe.Length > 0 ? recipe : "none uses a Gear Set")}), Piece Rack body {rk.Id} ({stocked} stocked), Lay-down Pad body {pd.Id}";
    }

    private static Vector3 Half(ConstructPartDef d) => (d.BoundsMax - d.BoundsMin) * 0.5f;

    /// <summary>Host: a piece of a kind put in a machine's Piece out as if it had made it (the machine is the line's Assembler, or a body id).</summary>
    public static string Hold(ConstructWorld world, ushort body, string piece)
    {
        if (world.Get(body) is not { } b || ConstructParts.Find(piece) is not { } d)
            return $"hold: no body {body} or no part \"{piece}\"";
        var r = ConstructLifters.HoldOut(world, b, 0, d);
        return $"hold: {d.Name} at body {b.Id}'s out: {(r == 1 ? "held for a lifter" : r == 2 ? "its out is full" : "no lifter serves its out")}";
    }

    /// <summary>Every arm's points, ports (Busy, Alarm, Moved), settings, mains and its body's power draw while busy.</summary>
    public static string Points(ConstructWorld world)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var lb in world.LiftBodies)
        {
            if (world.Get(lb.Body) is not { } c)
                continue;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l.Part >= c.PartCount)
                    continue;
                ref readonly var p = ref c.Parts[l.Part];
                var d = c.DefOf(l.Part);
                sb.Append($" [body {c.Id} {d.Name}: {l.State} {l.Why} rise {l.Rise:0.00}; Busy {p.Out:0} Alarm {p.Out1:0} Moved {p.Mem:0}; Works {ConstructConfig.Get(p, d, CfgKey.LiftMode, 0):0} Carry height {ConstructConfig.Get(p, d, CfgKey.CarryHeight, ConstructLifters.CarryRise):0.0}; mains {(c.MainsOff ? "off" : "on")}, Run held {ConstructExtruder.RunHeld(c, l.Part)}, powered {ConstructRecycler.Powered(world, c, l.Part)}, body draws {c.PowerUse:0} W (arm {d.PowerDraw:0} W while busy);");
                for (var k = 0; k < l.PointN; k++)
                    sb.Append($" {l.Points![k].Kind}@{l.Points[k].Body}.{l.Points[k].Part}{(l.Points[k].Kind == LiftPointKind.PressBay ? "/" + (l.Points[k].Bay + 1) : "")}");
                sb.Append(']');
            }
        }
        foreach (var o in world.LiftOuts)
            sb.Append($" [out body {o.Body}.{o.Part}: {o.Count} {ConstructParts.Get(o.Def)?.Name}]");
        if (world.Get(LineRack) is { } rk && world.StoreOf(rk, 0) is { } rs)
            sb.Append($" [rack {rs.ItemCount}]");
        if (world.Get(LinePad) is { } pdb && world.StoreOf(pdb, 0) is { } ps)
            sb.Append($" [pad {ps.ItemCount}{Kinds(ps)}]");
        if (world.Get(LineAsm) is { } ab && world.StoreOf(ab, 0) is { } ast)
            sb.Append($" [assembler inputs {ast.ItemCount}{Kinds(ast)}]");
        return $"{Carries} carries begun (host){sb}";
    }

    private static string Kinds(CStore st)
    {
        if (st.KindDef == null)
            return "";
        var s = "";
        for (var k = 0; k < st.KindN; k++)
            s += $" {st.KindCount![k]} {ConstructParts.Get(st.KindDef[k])?.Name}";
        return s.Length > 0 ? ":" + s : "";
    }

    /// <summary>Host: a setting on every arm (liftMode 0-3, carryHeight m), as the configure tool sets it.</summary>
    public static string Set(ConstructWorld world, string key, float v)
    {
        var k = key == "carryHeight" ? CfgKey.CarryHeight : CfgKey.LiftMode;
        var n = 0;
        foreach (var lb in world.LiftBodies)
            if (world.Get(lb.Body) is { } c)
                for (var i = 0; i < lb.N; i++)
                    if (lb.Lifts[i].Part < c.PartCount)
                    {
                        ConstructConfig.Put(ref c.Parts[lb.Lifts[i].Part], c.DefOf(lb.Lifts[i].Part), k, v);
                        world.StructureDirty.Add(c.Id);
                        n++;
                    }
        return $"set {key} = {v} on {n} arm(s)";
    }

    public static ushort BaseBody;
    public static int BaseMains = -1, BaseRun = -1;

    /// <summary>
    /// Host: a powered base round an arm (phase 3's leftover): the arm, with a Battery and two Switches welded on its plinth's sockets as the
    /// attach tool fits them; one Switch wired into the Battery (the mains), the other into the arm's Run. Both start on.
    /// </summary>
    public static string Base(ConstructWorld world, Vector3 at, string tier)
    {
        var name = (tier.Length > 0 && tier != "Scrap" ? tier + " " : "") + "Loader Arm";
        if (ConstructParts.Find(name) is not { Lifter: not null } ad || ConstructParts.Find("Battery") is not { } bat || ConstructParts.Find("Switch") is not { } sw)
            return "base: a part is missing (Loader Arm, Battery, Switch)";
        if (ConstructBuild.PlaceNew(world, ad, at with { Y = World.Map.GroundHeight(at.X, at.Z) }, 0) is not { } arm)
            return "base: no room";
        arm.Anchored = true;
        var gs = new ConstructGhost[64]; // (a probe: once)
        int Fit(ConstructPartDef d)
        {
            var n = ConstructBuild.Ghosts(arm, d, gs, Vector3.Zero, 8);
            for (var k = 0; k < n; k++)
                if (ConstructBuild.Attach(world, arm, d, gs[k]) is var i and >= 0)
                    return i;
            return -1;
        }
        var b = Fit(bat);
        BaseMains = Fit(sw);
        BaseRun = Fit(sw);
        if (b < 0 || BaseMains < 0 || BaseRun < 0)
            return $"base: the plinth took battery {b}, switches {BaseMains}, {BaseRun}";
        arm.Parts[BaseMains].State |= 1;
        arm.Parts[BaseRun].State |= 1;
        var w1 = ConstructRoutes.Add(world, arm, RouteType.Data, BaseMains, 255, 255, 0, default, default, b, 255);
        var w2 = ConstructRoutes.Add(world, arm, RouteType.Data, BaseRun, 255, 255, 0, default, default, 0, 255);
        arm.Dirty = true;
        arm.Rebuild();
        world.StructureDirty.Add(arm.Id);
        BaseBody = arm.Id;
        return $"base: {name} body {arm.Id} with a Battery (part {b}), a mains Switch (part {BaseMains}, wire {w1}) and a Run Switch (part {BaseRun}, wire {w2})";
    }

    /// <summary>Host: one of the base's switches flipped, as a press flips it ("mains" or "run").</summary>
    public static string Flip(ConstructWorld world, string which)
    {
        var part = which == "run" ? BaseRun : BaseMains;
        if (world.Get(BaseBody) is not { } c || part < 0 || part >= c.PartCount)
            return "flip: no base";
        c.Parts[part].State ^= 1;
        c.Wake();
        return $"flip: the {which} switch is {((c.Parts[part].State & 1) != 0 ? "on" : "off")}";
    }

    /// <summary>Host: every arm's body's mains switch (the Switch's own effect: Construct.MainsOff).</summary>
    public static string Mains(ConstructWorld world, bool off)
    {
        var n = 0;
        foreach (var lb in world.LiftBodies)
            if (world.Get(lb.Body) is { } c)
            {
                c.MainsOff = off;
                c.Wake();
                n++;
            }
        return $"mains {(off ? "off" : "on")} on {n} arm bod(ies)";
    }
}

public static partial class LifterProbe
{
    /// <summary>"TIER [N]": N lines (a tick cost with several arms working), 25 m apart east of <paramref name="at"/>, each stocked; the last's ids kept.</summary>
    public static string Lines(ConstructWorld world, Vector3 at, string args)
    {
        var w = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var n = w.Length > 1 && int.TryParse(w[1], out var k) ? Math.Clamp(k, 1, 8) : 1;
        var s = "";
        for (var i = 0; i < n; i++)
        {
            if (i > 0)
                BaseBody = 0;
            s = Line(world, at + new Vector3(25f * i, 0, 0), w.Length > 0 ? w[0] : "", out _);
        }
        return n > 1 ? $"{n} lines; the last: {s}" : s;
    }
}

public static partial class LifterProbe
{
    public static string Reset()
    {
        (StepTicks, StepTotal, StepWorst) = (0, 0, 0);
        return "the lifter step's cost counters start again";
    }
}
