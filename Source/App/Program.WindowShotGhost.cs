using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window-shot tool's actions for the blueprint ghost (Program.SandboxBlueprintGhost.cs), kept apart from the others:
/// <c>ghost NAME</c> (the parts menu's own Spawn of a shipped blueprint), <c>ghostveh STYLE|SHAPE</c> (the vehicle tab's, made on the host),
/// <c>waitghost</c> (until the ghost is out, at most about 20 s), <c>look YAW PITCH</c> (degrees), <c>swheel N</c> (Shift + wheel),
/// <c>esc</c>, and <c>ghostinfo</c> (this peer's ghost, the host's pending spawns, the newest bodies and their poses).
/// </summary>
public static unsafe partial class Program
{
    private static int _wsGhostTries;

    private static bool WsGhostKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "ghost" or "ghostveh" or "waitghost" or "look" or "swheel" or "esc" or "ghostinfo";

    private static bool WsGhostVerb(Game g, Player me, string verb, string arg)
    {
        switch (verb)
        {
            case "ghost":
            {
                var all = ConstructBuild.Blueprints;
                var index = -1;
                for (var i = 0; i < all.Count; i++)
                    if (all[i].Name.Equals(arg, StringComparison.OrdinalIgnoreCase))
                        index = i;
                if (index < 0)
                {
                    WsFail($"no blueprint \"{arg}\"");
                    return true;
                }
                Ui.SandboxPartsSpawn(g, index);
                _wsWait = 3;
                return true;
            }
            case "ghostveh":
            {
                var bar = arg.IndexOf('|');
                if (bar < 0)
                {
                    WsFail("ghostveh STYLE|SHAPE");
                    return true;
                }
                g.SandboxSpawnVehicle(arg[..bar].Trim(), arg[(bar + 1)..].Trim(), 0, ghost: true);
                _wsWait = 3;
                return true;
            }
            case "waitghost":
                if (g.GhostState == BlueprintGhostState.Carrying)
                {
                    WsSay($"ghost out: {g.GhostName} after {_wsGhostTries} waits");
                    _wsGhostTries = 0;
                    _wsWait = 3;
                    return true;
                }
                if (++_wsGhostTries > 120)
                {
                    WsFail($"no ghost (state {g.GhostState})");
                    return true;
                }
                _wsQueue.EnqueueFront("waitghost");
                _wsWait = 10;
                return true;
            case "look":
            {
                var bits = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (bits.Length < 2 || !float.TryParse(bits[0], ci, out var yaw) || !float.TryParse(bits[1], ci, out var pitch))
                {
                    WsFail("look YAW PITCH");
                    return true;
                }
                g.LookYaw = yaw * MathF.PI / 180f;
                g.LookPitch = pitch * MathF.PI / 180f;
                _wsWait = 3;
                return true;
            }
            case "swheel":
                TestShift = true;
                ScrollInput(int.TryParse(arg, out var by) ? by : 1);
                TestShift = false;
                _wsWait = 2;
                return true;
            case "esc":
                WsSay(BlueprintGhostEsc() ? "esc: the ghost went" : "esc: no ghost");
                _wsWait = 3;
                return true;
            case "ghostinfo":
            {
                WsSay($"ghostinfo: state {g.GhostState}, {g.GhostName}, token {g.GhostToken}, {g.GhostCount} boxes, bounds {g.GhostMax - g.GhostMin}; look \"{g.SandboxLookText}\"; notice \"{g.SandboxNotice}\"");
                if (g.IsServer)
                    WsSay($"ghostinfo: host pending spawns {g.SpawnPendingCount}");
                var bodies = g.Constructs.Bodies;
                WsSay($"ghostinfo: {bodies.Count} bodies");
                for (var i = Math.Max(0, bodies.Count - 3); i < bodies.Count; i++)
                {
                    var c = bodies[i];
                    var f = Vector3.Transform(Vector3.UnitZ, c.Rot);
                    WsSay($"ghostinfo: body {c.Id} owner {c.Owner} parts {c.PartCount} pos {c.Pos.X:0.00},{c.Pos.Y:0.00},{c.Pos.Z:0.00} heading {MathF.Atan2(f.X, f.Z) * 180 / MathF.PI:0.0} deg");
                }
                return true;
            }
        }
        return false;
    }
}
