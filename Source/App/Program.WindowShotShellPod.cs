using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's shell pod verbs (ConstructShellPod.cs; docs/sandbox/design/shell-pod.md): "shellpod setup" (host) puts four pods in a
/// row ahead (1 a Small pod bolted on the floor, 2 a Medium flush in the floor, 3 a Small bolted under a ceiling, 4 a Medium flush in a
/// ceiling: those two hang frozen in the air, their faces down; 5 and 6 Medium pods bolted on the floor, with bins), a Robot Shell (channel 1) and a Mining Shell (channel 2) to the left
/// and two Shell Control Chairs (channels 1 and 2) to the right; "shellpod go N [PLAYER]" (host, a test state) stands a player in pod
/// N's frame; "shellpod dock" presses the button of the pod this player stands in (the use key's own message); "shellpod state" says
/// what this peer sees.
/// </summary>
public static partial class Program
{
    private static readonly List<ushort> _wsPods = [];

    private static void WsShellPod(Game g, Player me, string arg)
    {
        var w = g.Constructs;
        var words = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var sub = words.Length > 0 ? words[0].ToLowerInvariant() : "state";
        var rest = words.Length > 1 ? words[1] : "";
        var fwd = new Vector3(MathF.Sin(me.Yaw), 0, -MathF.Cos(me.Yaw));
        var side = new Vector3(MathF.Cos(me.Yaw), 0, MathF.Sin(me.Yaw));
        Vector3 Ground(Vector3 at) => at with { Y = World.Map.GroundHeight(at.X, at.Z) };
        void Fix(Construct? c)
        {
            if (c == null)
                return;
            c.Frozen = true;
            c.Rebuild();
            w.StructureDirty.Add(c.Id);
        }
        switch (sub)
        {
            case "setup" when g.IsServer:
            {
                _wsPods.Clear();
                var yaw = me.Yaw + MathF.PI;
                var up = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw);
                var down = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI), up);
                (string Name, float Ahead, bool Ceiling, float Height)[] pods =
                    [("Small Shell Pod", 5, false, 0), ("Medium Flush Shell Pod", 10, false, 0), ("Small Shell Pod", 15, true, 2.95f), ("Medium Flush Shell Pod", 20, true, 2.8f),
                     ("Medium Shell Pod", 25, false, 0), ("Medium Shell Pod", 30, false, 0)];
                foreach (var (name, ahead, ceiling, height) in pods)
                {
                    if (ConstructParts.Find(name) is not { } pd)
                    {
                        WsFail($"shellpod setup: no part {name}");
                        return;
                    }
                    var at = Ground(me.Position + fwd * ahead);
                    var c = ceiling ? ConstructBuild.PlaceNew(w, pd, new FreePlacement { Pos = at + Vector3.UnitY * height, Rot = down }) : ConstructBuild.PlaceNew(w, pd, at, yaw);
                    Fix(c);
                    if (c != null)
                        _wsPods.Add(c.Id);
                }
                var made = new List<string>();
                foreach (var (name, ch, left) in new[] { ("Robot Shell", 1, 3f), ("Mining Shell", 2, 6f) })
                    if (ConstructParts.Find(name) is { } sd && ConstructBuild.PlaceNew(w, sd, Ground(me.Position - side * left + fwd * 2), yaw) is { } sc)
                    {
                        ConstructConfig.Put(ref sc.Parts[0], sd, CfgKey.Channel, ch);
                        Fix(sc);
                        made.Add($"{name} {sc.Id} ch {ch}");
                    }
                foreach (var (ch, right) in new[] { (1, 3f), (2, 5f) })
                    if (ConstructParts.Find("Shell Control Chair") is { } cd && ConstructBuild.PlaceNew(w, cd, Ground(me.Position + side * right), me.Yaw) is { } cc)
                    {
                        ConstructConfig.Put(ref cc.Parts[0], cd, CfgKey.Channel, ch);
                        Fix(cc);
                        made.Add($"chair {cc.Id} ch {ch}");
                    }
                WsSay($"shellpod setup: pods {string.Join(", ", _wsPods)}; {string.Join(", ", made)}");
                return;
            }
            case "go" when g.IsServer:
            {
                var p = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var n = p.Length > 0 && int.TryParse(p[0], out var v) ? v - 1 : -1;
                var who = me;
                if (p.Length > 1 && p[1] == "other")
                    who = g.Players.Values.FirstOrDefault(o => o != me && o.InShell) ?? me;
                if ((uint)n >= (uint)_wsPods.Count || w.Get(_wsPods[n]) is not { } c || !who.InShell)
                {
                    WsFail($"shellpod go N [other]: no pod {n + 1}, or that player isn't in a shell");
                    return;
                }
                var feet = ConstructShellPod.StandFeet(c, 0, who.ShellFrame, out var f, out _);
                who.Teleport(feet + Vector3.UnitY * 0.05f);
                who.Yaw = ConstructShellPod.YawOf(f);
                WsSay($"shellpod go: player {who.Id} to pod {n + 1} (body {c.Id}) at {feet.X:0.0},{feet.Y:0.00},{feet.Z:0.0}");
                return;
            }
            case "dock":
            {
                var (c, part) = ShellPodFrame(g, me, false, default);
                if (c == null)
                {
                    WsFail($"shellpod dock: not in an empty pod's frame (in shell {me.InShell}, at {me.Position.X:0.0},{me.Position.Y:0.00},{me.Position.Z:0.0})");
                    return;
                }
                g.SandboxSend(CActKind.Use, body: c.Id, part: (ushort)part);
                WsSay($"shellpod dock: pressed the button of pod body {c.Id}");
                return;
            }
            default:
            {
                WsSay($"shellpod state: {(g.IsServer ? "host" : "client")}, {w.PodJobCount} job(s){(w.PodJobCount > 0 ? $": body {w.PodJobs[0].Body} phase {w.PodJobs[0].Phase} frame {w.PodJobs[0].Frame}, {ConstructConveyor.Clock - w.PodJobs[0].Start:0.0} s in" : "")}");
                foreach (var c in w.Bodies)
                    for (var q = 0; q < c.PodCount; q++)
                    {
                        int i = c.Pods[q];
                        ref var pp = ref c.Parts[i];
                        ShellCond wear = default;
                        ShellWear.Read(pp, pp.D, ref wear);
                        ConstructShellPod.Lid(c, i, pp.D.ShellPod!, out var lid, out _, out var mount, out _, out _);
                        WsSay($"shellpod state: body {c.Id} {pp.D.Name} ({(mount == 0 ? "floor" : mount == 1 ? "wall" : "ceiling")}, lid at {lid.Y:0.00}): frame {ConstructShellPod.FrameIn(pp)}, ch {ConstructShellPod.ChannelIn(pp)}, owner {ConstructShellPod.OwnerIn(pp)}, power cell wear {wear[(int)ShellWork.Power]}, ore {ConstructConfig.Get(pp, pp.D, CfgKey.PodOreUnits, 0)}, bin {w.StoreOf(c, i)?.Amount ?? -1}; {ConstructShellPod.Look(w, c, i, me.InShell)}");
                    }
                var shells = w.Bodies.Where(b => b.PartCount == 1 && b.Parts[0].D.ShellBody).Select(b => $"{b.Parts[0].D.Name} {b.Id} bin {w.StoreOf(b, 0)?.Amount ?? -1}");
                WsSay($"shellpod state: standing shells: {string.Join(", ", shells)}; me in shell {me.InShell} frame {me.ShellFrame} hopper {me.HopperAmount} at {me.Position.X:0.0},{me.Position.Y:0.00},{me.Position.Z:0.0}; look: {g.SandboxLookText}");
                return;
            }
        }
    }
}
