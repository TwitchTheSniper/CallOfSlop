using System.Numerics;
using Brutal.ImGuiApi;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The window tool's verbs for the command view and ship orders (docs/space/COMMAND.md "Trying it"):
/// <c>command [SPAN]</c> opens the view (SPAN: metres up the screen), <c>cmdpick N</c> picks the Nth craft on the map (0: the
/// nearest), <c>order orbit BODY KM</c>, <c>order goto BODY KM</c> and <c>order cancel</c> give the picked craft an order as the
/// menu would, <c>nav</c> prints every craft and its order, <c>hoist KM</c> (host) puts the picked craft that far over the
/// ground, at rest over it, <c>warp N</c> (host) asks for a time warp, and <c>rclick X,Y</c> is the right mouse button at a
/// window pixel (the order menu).
/// </summary>
public static unsafe partial class Program
{
    private static bool WsNavKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "command" or "cmdpick" or "order" or "nav" or "hoist" or "warp" or "cmdview" or "rclick" or "rclickup";

    private static bool WsNavVerb(Game g, Player me, string verb, string arg)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (verb)
        {
            case "command":
                if (!Ui.CommandOpen)
                    SetCommandOpen(true);
                if (!Ui.CommandOpen)
                {
                    WsFail("command: the view didn't open (no star system on this map?)");
                    return true;
                }
                if (words.Length > 0 && double.TryParse(words[0], inv, out var span))
                    Ui.CommandLook(span);
                _wsWait = 20;
                return true;
            case "cmdview": // "cmdview YAW PITCH" (degrees): the view's turn and tilt
                if (words.Length == 2 && float.TryParse(words[0], inv, out var yaw) && float.TryParse(words[1], inv, out var pitch))
                    Ui.CommandTurn(yaw * MathF.PI / 180, pitch * MathF.PI / 180);
                _wsWait = 3;
                return true;
            case "cmdpick":
            {
                var n = words.Length > 0 && int.TryParse(words[0], out var k) ? k : 0;
                ushort pick = 0;
                var seen = 0;
                foreach (var c in g.Constructs.Bodies)
                    if (Ui.CmdIsCraft(c) && ++seen == Math.Max(1, n))
                        pick = c.Id;
                if (pick == 0)
                {
                    WsFail($"cmdpick: no craft {n} ({seen} on the map)");
                    return true;
                }
                Ui.CommandLook(0, pick);
                WsSay($"[nav] picked construct {pick}");
                _wsWait = 3;
                return true;
            }
            case "order":
            {
                var kind = words.Length > 0 ? words[0].ToLowerInvariant() : "";
                var why = kind switch
                {
                    "cancel" => Ui.CommandOrder(g, NavKind.None, "", 0),
                    "orbit" or "goto" when words.Length == 3 && double.TryParse(words[2], inv, out var km) => Ui.CommandOrder(g, kind == "orbit" ? NavKind.Orbit : NavKind.GoTo, words[1], km),
                    _ => "order orbit BODY KM | order goto BODY KM | order cancel",
                };
                if (why != null)
                    WsFail("order: " + why);
                _wsWait = 10;
                return true;
            }
            case "nav":
            {
                if (g.Space is not { } s)
                {
                    WsSay("[nav] no star system here");
                    return true;
                }
                foreach (var c in g.Constructs.Bodies)
                {
                    if (!Ui.CmdIsCraft(c))
                        continue;
                    var o = g.NavOf(c.Id);
                    var height = World.Map.Planetoid is { } pl ? (float)pl.Altitude(c.WorldCom) : c.WorldCom.Y;
                    WsSay(string.Create(inv, $"[nav] construct {c.Id}: {(c.SpaceAway != 0 ? "parked" : c.SpaceShip != 0 ? "space" : "sky")}, {height:0} m up, {c.Vel.Length():0.0} m/s, mass {c.Mass / 1000:0.0} t, fuel {c.FuelLeft:0} L, thrust {c.ThrustShow:0.00}, nav throttle {c.NavThrottle:0.00}, takes orders: {Game.NavProblem(c) ?? "yes"}; order: {(o == null ? "none" : $"{Game.NavText(s.System, o)}, {o.Phase}, {o.Distance:0} m to go, {o.SpeedError:0.0} m/s off")}{(g.SpaceOrbitLine(c) is { } line ? "; " + line : "")}"));
                }
                return true;
            }
            case "hoist" when g.IsServer:
            {
                if (g.Constructs.Get(Ui.CommandPicked) is not { } c || GravityFrames.Point is not { } planet || World.Map.Planetoid is not { } pl
                    || words.Length < 1 || !float.TryParse(words[0], inv, out var km))
                {
                    WsFail("hoist KM: pick a craft first (command, cmdpick), on a planetoid");
                    return true;
                }
                var up = Vector3.Normalize(c.WorldCom - planet.Center);
                var to = planet.Center + up * ((float)pl.Radius + km * 1000f);
                c.Pos += to - c.WorldCom;
                c.PrevPos = c.Pos;
                c.Vel = Vector3.Zero;
                c.AngVel = Vector3.Zero;
                c.Wake();
                WsSay(string.Create(inv, $"[nav] construct {c.Id} hoisted to {km:0.0} km"));
                _wsWait = 10;
                return true;
            }
            case "rclick":
            {
                if (arg.Split(',') is not [var cx, var cy] || !float.TryParse(cx, inv, out var px) || !float.TryParse(cy, inv, out var py))
                {
                    WsFail($"rclick X,Y: \"{arg}\"");
                    return true;
                }
                Ui.ToolMouse = true;
                var io = Brutal.ImGuiApi.ImGui.GetIO();
                io.AddMousePosEvent(px, py);
                io.AddMouseButtonEvent(1, true);
                _wsQueue.EnqueueFront("rclickup");
                _wsWait = 3;
                return true;
            }
            case "rclickup":
                Brutal.ImGuiApi.ImGui.GetIO().AddMouseButtonEvent(1, false);
                _wsWait = 3;
                return true;
            case "warp" when g.IsServer:
                WsSay($"[nav] warp {arg}: {(int.TryParse(arg, out var w) && g.RequestSpaceWarp(w) ? "asked" : "refused")}");
                return true;
        }
        return false;
    }
}
