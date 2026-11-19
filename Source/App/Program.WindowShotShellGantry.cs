using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's Shell Gantry verbs (ConstructGantry.cs): "shellgantry setup [SHELL]" (host) stands a gantry 9 m ahead facing the
/// player, a Shell Parts Table behind its rear frame with a Plated Head and two Runner Legs on it, a shell (default the Robot Shell)
/// in its bay and a Shell Control Chair 3 m to the player's right; "shellgantry fit SLOT MODULE" asks the nearest gantry for a swap through the panel's own message (either peer);
/// "shellgantry state" says what this peer sees: the jobs, the bay's shell and its modules, the table, and this player's worn kit.
/// </summary>
public static partial class Program
{
    private static void WsShellGantry(Game g, Player me, string arg)
    {
        var w = g.Constructs;
        var words = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var sub = words.Length > 0 ? words[0].ToLowerInvariant() : "state";
        var rest = words.Length > 1 ? words[1] : "";
        switch (sub)
        {
            case "setup" when g.IsServer:
            {
                if (ConstructParts.Find("Shell Gantry") is not { } gd || ConstructParts.Find("Shell Parts Table") is not { } td
                    || ConstructParts.Find(rest.Length > 0 ? rest : "Robot Shell") is not { ShellBody: true } sd)
                {
                    WsFail("shellgantry setup: a part is missing (Shell Gantry, Shell Parts Table, the shell)");
                    return;
                }
                var yaw = me.Yaw + MathF.PI;
                var fwd = new Vector3(MathF.Sin(me.Yaw), 0, -MathF.Cos(me.Yaw));
                var at = me.Position + fwd * 9;
                at.Y = World.Map.GroundHeight(at.X, at.Z);
                if (ConstructBuild.PlaceNew(w, gd, at, yaw) is not { } gc)
                {
                    WsFail("shellgantry setup: the gantry wasn't placed");
                    return;
                }
                gc.Frozen = true;
                gc.Rebuild();
                Freeform.PartWorld(gc, 0, out var gp, out var gr);
                var tc = ConstructBuild.PlaceNew(w, td, new FreePlacement { Pos = gp + Vector3.Transform(new Vector3(0, 0.01f, 3.7f), gr), Rot = gr * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) });
                var sc = ConstructBuild.PlaceNew(w, sd, new FreePlacement { Pos = gp + Vector3.Transform(new Vector3(0, 0.09f, 0), gr), Rot = gr });
                foreach (var c in new[] { tc, sc })
                    if (c != null)
                    {
                        c.Frozen = true;
                        c.Rebuild();
                        w.StructureDirty.Add(c.Id);
                    }
                w.StructureDirty.Add(gc.Id);
                if (ConstructParts.Find("Shell Control Chair") is { } cd)
                {
                    var side = new Vector3(MathF.Cos(me.Yaw), 0, MathF.Sin(me.Yaw));
                    var ca = me.Position + side * 3;
                    ca.Y = World.Map.GroundHeight(ca.X, ca.Z);
                    if (ConstructBuild.PlaceNew(w, cd, ca, me.Yaw) is { } cc)
                    {
                        cc.Frozen = true;
                        cc.Rebuild();
                        w.StructureDirty.Add(cc.Id); // (a chair to wake in the shell from: "sit Shell Control Chair")
                    }
                }
                var put = 0;
                foreach (var n in new[] { "Shell Plated Head", "Shell Runner Leg", "Shell Runner Leg" })
                    if (tc != null && ConstructParts.Find(n) is { } md && w.RackPut(tc, 0, md))
                        put++;
                WsSay($"shellgantry setup: gantry {gc.Id}, table {tc?.Id ?? 0} ({put} modules on it), {sd.Name} {sc?.Id ?? 0}; table found by the gantry: {ConstructGantry.TableOf(w, gc, 0, out _) >= 0}; shell in the bay: {ConstructGantry.ShellIn(w, gc, 0)?.Id ?? 0}");
                return;
            }
            case "fit":
            {
                var p = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var slot = p.Length > 0 ? Array.FindIndex(ShellFrames.Slots, s => s.Equals(p[0], StringComparison.OrdinalIgnoreCase)) : -1;
                var mod = p.Length > 1 ? Array.FindIndex(ShellFrames.Modules, m => m.Name.Equals(p[1], StringComparison.OrdinalIgnoreCase)) : -1;
                if (slot < 0 || mod < 0 || Nearest(g, me) is not { } gc)
                {
                    WsFail($"shellgantry fit SLOT MODULE: \"{rest}\" (or no gantry)");
                    return;
                }
                g.SandboxGantry(gc.Id, gc.Gantries[0], slot, mod);
                WsSay($"shellgantry fit: asked gantry {gc.Id} for a {ShellFrames.Modules[mod].Name} in the {ShellFrames.Slots[slot]}");
                return;
            }
            default:
            {
                WsSay($"shellgantry state: {(g.IsServer ? "host" : "client")}, {w.GantryJobCount} job(s){(w.GantryJobCount > 0 ? $": arm {w.GantryJobs[0].Arm} slot {w.GantryJobs[0].Slot} {w.GantryJobs[0].Old}->{w.GantryJobs[0].New}, {ConstructConveyor.Clock - w.GantryJobs[0].Start:0.0} s in" : "")}");
                if (Nearest(g, me) is { } gc)
                {
                    var part = gc.Gantries[0];
                    if (ConstructGantry.ShellIn(w, gc, part) is { } sh)
                    {
                        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
                        ShellFrames.ReadKit(sh.Parts[0], sh.Parts[0].D, kit);
                        var f = ShellFrames.FrameOf(sh.Parts[0].D);
                        var names = new string[ShellFrames.SlotCount];
                        for (var s = 0; s < names.Length; s++)
                            names[s] = $"{ShellFrames.Slots[s]}={ShellFrames.ModuleAt(f, kit, s)?.Name}";
                        WsSay($"shellgantry state: bay shell {sh.Id} {sh.Parts[0].D.Name}: {string.Join(", ", names)} (kit {string.Join(".", kit.ToArray())})");
                    }
                    else
                        WsSay("shellgantry state: no shell in the bay");
                    if (ConstructGantry.TableOf(w, gc, part, out var tb) is var t and >= 0 && tb != null && w.StoreOf(tb, t) is { } st)
                        WsSay($"shellgantry state: table {tb.Id}: {string.Join(", ", ShellFrames.Modules.Where(m => m.Part.Length > 0).Select(m => $"{ConstructGantry.OnTable(w, tb, t, m)} {m.Part}"))} ({st.ItemCount} items)");
                    else
                        WsSay("shellgantry state: no table in reach");
                    WsSay($"shellgantry state: look line: {ConstructGantry.Look(w, gc, part)}");
                }
                else
                    WsSay("shellgantry state: no gantry near");
                var k = ShellFrames.Worn(me);
                WsSay(FormattableString.Invariant($"shellgantry state: me in shell {me.InShell}, frame {me.ShellFrame}, mods {string.Join(".", me.ShellMods)}, legs {k.Modules[4]?.Name}/{k.Modules[5]?.Name}, head {k.Modules[0]?.Name}, pace x{Game.ShellMoveMult(me):0.000}, hits x{k.Damage:0.00}"));
                return;
            }
        }
    }

    private static Construct? Nearest(Game g, Player me)
    {
        Construct? best = null;
        var near = float.MaxValue;
        foreach (var c in g.Constructs.Bodies)
            if (c.GantryCount > 0 && Vector3.DistanceSquared(c.WorldCom, me.Position) is var d && d < near)
                (best, near) = (c, d);
        return best;
    }
}
