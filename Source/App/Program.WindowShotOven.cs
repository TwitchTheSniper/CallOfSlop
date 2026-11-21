using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's verb for Bake Ovens (ConstructBakeOven.cs): <c>oven</c> prints every oven as this peer has it (its bed, masts,
/// clear height, phase, why it waits and what is on its bed); <c>oven put NAME</c> (host) sets a new body of one part NAME (or the
/// blueprint NAME) down in the middle of the first oven's clear bed, as a crane would leave it.
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
                if (sub[0] == "put" && g.IsServer && n == 0)
                {
                    // The clear cells' middle: the mean of every free cell's middle, on the bed's top.
                    var d = c.Parts[asm.Core].D;
                    var sum = Vector3.Zero;
                    var k = 0;
                    for (var x = bed.X0; x < bed.X0 + bed.W; x++)
                        for (var z = bed.Z0; z < bed.Z0 + bed.L; z++)
                            if (bed.Free(c, asm, x, z))
                            {
                                sum += bed.CellMid(d, x, z);
                                k++;
                            }
                    var local = (k > 0 ? sum / k : Vector3.Zero) with { Y = bed.Top + 0.02f };
                    var at = Vector3.Transform(local, ConstructBakeOven.CoreFrame(c, asm.Core));
                    Construct? made = null;
                    var name = sub.Length > 1 ? sub[1] : "";
                    if (ConstructParts.Find(name) is { } pd)
                        made = ConstructBuild.PlaceNew(w, pd, at, 0);
                    else if (ConstructBuild.FindBlueprint(name) is { } bp)
                        made = ConstructBuild.Build(w, bp, at, Quaternion.Identity, out _);
                    WsSay($"oven: put {name} at {at:0.00}: {(made == null ? "refused" : $"body {made.Id}, {made.PartCount} parts")}");
                    _wsWait = 3;
                    return true;
                }
                WsSay($"oven #{n++}: body {c.Id} part {asm.Core}: {MachineBlocks.Describe(c, asm)}, corner masts {bed.Columns}, clear {bed.Clear:0.0} m, phase {(OvenPhase)st.Blocked}, busy {st.Busy}, stall {(OvenStall)st.Refused}, progress {st.Progress}%; now: {why}{(group != 0 ? $" (body {group}, {parts} parts)" : "")}");
            }
        if (n == 0)
            WsSay("oven: no Bake Oven");
        return true;
    }
}
