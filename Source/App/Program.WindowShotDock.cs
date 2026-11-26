using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window-shot tool's actions for the Ship Docking Station (Program.SandboxDock.cs, GameSandboxDock.cs): <c>dock ship NAME</c> (host: a
/// ship design spawned 20 m beside the first docking station, set down as the parts menu's big blueprints are), <c>dock open</c> (the use
/// key's own call on the first docking station), <c>dock cmd JSON</c> (a designer command, as its panels send it), <c>dock apply</c> (the
/// APPLY TO SHIP button) and <c>dock info</c> (each design body as this peer has it: its id, design, parts, place, owner, and the riders).
/// </summary>
public static unsafe partial class Program
{
    private static bool WsDockKnown(string action) => action.Split(' ', 2)[0].Equals("dock", StringComparison.OrdinalIgnoreCase);

    private static (Construct Body, int Part)? WsDockStation(Game g)
    {
        foreach (var b in g.Constructs.Bodies)
            for (var i = 0; i < b.PartCount; i++)
                if (b.DefOf(i).ShipDockRange > 0)
                    return (b, i);
        return null;
    }

    private static bool WsDockVerb(Game g, string verb, string arg)
    {
        if (verb != "dock")
            return false;
        var sp = arg.IndexOf(' ');
        var (sub, rest) = sp < 0 ? (arg, "") : (arg[..sp], arg[(sp + 1)..].Trim());
        _wsWait = 3;
        switch (sub)
        {
            case "ship":
            {
                if (!g.IsServer || WsDockStation(g) is not { } st)
                {
                    WsFail("dock ship: the host, with a docking station in the world");
                    return true;
                }
                var listed = ShipDesignStore.List().FirstOrDefault(x => x.Name.Equals(rest, StringComparison.OrdinalIgnoreCase));
                if (listed.Json == null || DesignParts.Canon(listed.Json) is not { } text || DesignParts.Register(text) is not { } hash)
                {
                    WsFail($"dock ship: no ship design \"{rest}\" ({DesignParts.LastRefusal})");
                    return true;
                }
                g.Constructs.KeepShipDesign(hash);
                var at = Game.DockPad(st.Body, st.Part) + new Vector3(30, 0, 0);
                var ground = ConstructWorld.Ground(World.Query, at + new Vector3(0, 20, 0));
                var why = g.VehicleSpawn.RequestBlueprint(DesignParts.BlueprintOf(hash, listed.Name), ground, 0, g.LocalId, 255, null);
                WsSay(why == null ? $"dock ship: {listed.Name} ({hash}) asked for at {ground:0.0}" : $"dock ship: refused: {why}");
                _wsWait = 60;
                return true;
            }
            case "open":
                if (WsDockStation(g) is not { } os)
                {
                    WsFail("dock open: no docking station");
                    return true;
                }
                OpenShipDock(g, os.Body, os.Part);
                WsSay($"dock open: designer {(Ui.ShipDesignerDocked ? "open" : "NOT open")} on ship {Ui.ShipDesignerDock?.Ship}; notice \"{g.SandboxNotice}\"");
                return true;
            case "cmd":
                Ui.ShipDesignerTestCmd(rest);
                WsSay($"dock cmd: {rest}");
                return true;
            case "apply":
                Ui.ShipDesignerTestApply();
                WsSay($"dock apply: designer {(Ui.ShipDesignerOpen ? "still open" : "closed")}, mouse {(_mouseCaptured ? "captured" : "free")}");
                _wsWait = 10;
                return true;
            case "info":
                foreach (var b in g.Constructs.Bodies)
                    if (b.PartCount > 0 && DesignParts.EntryOf(b.DefOf(0)) is { } e)
                        WsSay($"dock info: body {b.Id} \"{e.Name}\" {e.Hash}, {b.PartCount} parts, at {b.Pos:0.00}, speed {b.Vel.Length():0.00}, owner {b.Owner}");
                foreach (var p in g.Players.Values)
                    WsSay($"dock info: player {p.Id} at {p.Position:0.00}, riding {p.Sim.RideBody}");
                WsSay($"dock info: notice \"{g.SandboxNotice}\"");
                return true;
        }
        WsFail("dock ship NAME | open | cmd JSON | apply | info");
        return true;
    }
}
