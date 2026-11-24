using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's verb for Bake Ovens (ConstructBakeOven.cs): <c>oven</c> prints every oven as this peer has it (its tunnel,
/// clear height, way out, phase, why it waits and what is on its bed); <c>oven put NAME</c> (host) sets a new body of one part NAME (or the
/// blueprint NAME) down in the middle of the first oven's hearth, as a crane would leave it.
/// </summary>
public static unsafe partial class Program
{
    private static bool WsOvenVerb(Game g, string verb, string arg)
    {
        if (verb != "oven")
            return false;
        var w = g.Constructs;
        var sub = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        var n = 0;
        foreach (var c in w.Bodies)
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (!c.Parts[asm.Core].D.IsBakeOven || w.StoreOf(c, asm.Core) is not { } st)
                    continue;
                var bed = new ConstructBakeOven.Bed(c, asm);
                var why = ConstructBakeOven.Check(w, c, asm.Core, asm, bed, out var group, out var parts);
                if (sub[0] == "mode" && g.IsServer && n == 0)
                {
                    // oven mode bake|unbake: the Core's Mode setting, as the configure tool sets it.
                    var cd = c.Parts[asm.Core].D;
                    ConstructConfig.Set(ref c.Parts[asm.Core], cd, cd.CfgSlot[(int)CfgKey.Mode], sub.Length > 1 && sub[1] == "unbake" ? 1 : 0);
                    w.StructureDirty.Add(c.Id);
                    WsSay($"oven: mode {(ConstructBakeOven.Unbaking(c.Parts[asm.Core], cd) ? "Un-bake" : "Bake")}");
                    return true;
                }
                if (sub[0] == "put" && g.IsServer && n == 0)
                {
                    // The hearth's middle, on the rollers.
                    var local = bed.Mid(bed.Along * 0.5f) + new Vector3(0, 0.02f, 0);
                    var at = Vector3.Transform(local, ConstructBakeOven.CoreFrame(c, asm.Core));
                    Construct? made = null;
                    var name = sub.Length > 1 ? sub[1] : "";
                    if (name.Contains('+'))
                        made = WsOvenGroup(w, name.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), at, ConstructBakeOven.CoreFrame(c, asm.Core));
                    else if (ConstructParts.Find(name) is { } pd)
                        made = ConstructBuild.PlaceNew(w, pd, at, 0);
                    else if (ConstructBuild.FindBlueprint(name) is { } bp)
                        made = ConstructBuild.Build(w, bp, at, Quaternion.Identity, out _);
                    WsSay($"oven: put {name} at {at:0.00}: {(made == null ? "refused" : $"body {made.Id}, {made.PartCount} parts")}");
                    _wsWait = 3;
                    return true;
                }
                WsSay($"oven #{n++}: body {c.Id} part {asm.Core}: {MachineBlocks.Describe(c, asm)}, tunnel {bed.Across:0} x {bed.Along:0} m, clear {bed.Clear:0.0} m, way out {(ConstructRollOut.OutRunOf(c, asm.Core) is var k and >= 0 ? $"run {k}" : "none")}, phase {(OvenPhase)st.Blocked}, busy {st.Busy}, stall {(OvenStall)st.Refused}, progress {st.Progress}%; now: {why}{(group != 0 ? $" (body {group}, {parts} parts)" : "")}");
            }
        if (n == 0)
            WsSay("oven: no Bake Oven");
        foreach (var c in w.Bodies)
            for (var i = 0; i < c.PartCount; i++)
                if (BakedParts.IsBaked(c.DefOf(i)))
                    WsSay($"oven: body {c.Id} part {i}: {c.DefOf(i).Name}, {c.DefOf(i).Boxes.Length} boxes, {c.DefOf(i).Mass:0} kg, health {c.Parts[i].Health:0}/{c.DefOf(i).Health:0}, design {(BakedParts.DesignOf(c.DefOf(i)) is { } bd ? $"{bd.Subs.Length} subparts" : "unknown")}");
        return true;
    }

    /// <summary>(oven put A+B+C) A new body of those parts in a row along the bed's x, each turned a quarter more about y, their feet at <paramref name="at"/>.</summary>
    private static Construct? WsOvenGroup(ConstructWorld w, string[] names, Vector3 at, Matrix4x4 frame)
    {
        var defs = new List<ConstructPartDef>();
        foreach (var name in names)
            if (ConstructParts.Find(name) is { } d)
                defs.Add(d);
            else
                WsSay($"oven: no part {name}");
        if (defs.Count == 0)
            return null;
        var rot = Quaternion.CreateFromRotationMatrix(frame with { M41 = 0, M42 = 0, M43 = 0 });
        if (w.Create(at, rot) is not { } c)
            return null;
        float width = 0;
        foreach (var d in defs)
            width += MathF.Max(d.BoundsMax.X - d.BoundsMin.X, d.BoundsMax.Z - d.BoundsMin.Z) + 0.02f;
        var x = -width / 2;
        byte turn = 0;
        foreach (var d in defs)
        {
            var span = MathF.Max(d.BoundsMax.X - d.BoundsMin.X, d.BoundsMax.Z - d.BoundsMin.Z) + 0.02f;
            var min = Vector3.Min(Rot24.Apply(turn, d.BoundsMin), Rot24.Apply(turn, d.BoundsMax));
            var mid = (Rot24.Apply(turn, d.BoundsMin) + Rot24.Apply(turn, d.BoundsMax)) / 2;
            w.AddPart(c, d, new Vector3(x + span / 2 - mid.X, 0.01f - min.Y, -mid.Z), turn);
            x += span;
            turn = Rot24.Compose(Rot24.Nearest(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), out _), turn);
        }
        c.Rebuild();
        return c;
    }
}
