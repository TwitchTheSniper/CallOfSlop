using System.Numerics;
using Brutal.ImGuiApi;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The window tool's verbs for the command view and ship orders (docs/space/COMMAND.md "Trying it"):
/// <c>command [SPAN]</c> opens the view (SPAN: metres up the screen), <c>cmdpick N</c> picks the Nth craft on the map (0: the
/// nearest), <c>order orbit BODY KM</c>, <c>order goto BODY KM</c>, <c>order ground M</c>, <c>order land M</c>, <c>order come</c>, <c>order dock N</c> (dock with the Nth craft, as cmdpick counts), <c>order board N door|hull</c> and <c>order cancel</c> give the picked craft an order as the
/// menu would, <c>nav</c> prints every craft and its order, <c>hoist KM</c> (host) puts the picked craft that far over the
/// ground, at rest over it, <c>speed N</c> (host) asks for a time speed, <c>travel FUEL SHARE</c> (host) sets Settings > Gameplay's
/// Fuel use in space and Trip fuel for the run (percents), and <c>letgo [ID]</c> (host) has the port holding the picked craft (or construct ID) let it go, and <c>rclick X,Y</c> is the right mouse button at a
/// window pixel (the order menu).
/// </summary>
public static unsafe partial class Program
{
    private static bool WsNavKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "command" or "cmdpick" or "order" or "nav" or "hoist" or "speed" or "travel" or "cmdview" or "rclick" or "rclickup" or "spacewalk" or "missile" or "missiles" or "letgo" or "orbit";

    /// <summary>"order dock N": the picked craft docks with the Nth craft on the map (as cmdpick counts them).</summary>
    private static string? WsDockOrder(Game g, int n)
    {
        var seen = 0;
        foreach (var c in g.Constructs.Bodies)
            if (Ui.CmdIsCraft(c) && ++seen == Math.Max(1, n))
                return Ui.CommandDock(g, c.Id);
        return $"no craft {n} ({seen} on the map)";
    }

    private static float Bulk(Construct c, int i)
    {
        c.PartBounds(i, out var mn, out var mx);
        var d = mx - mn;
        return d.X * d.Y * d.Z;
    }

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
            case "missiles": // "missiles [on|off]": the command view's missiles layer, and what it drew last frame
            {
                if (words.Length > 0)
                    Ui.CommandMissilesOn = words[0] != "off";
                var (shown, homing) = Ui.CommandMissileCounts;
                WsSay($"[nav] missiles layer {(Ui.CommandMissilesOn ? "on" : "off")}: {shown} shown, {homing} homing");
                _wsWait = 3;
                return true;
            }
            case "missile" when g.IsServer: // "missile M [SPEED]" (host): a test missile M metres off the picked craft, locked on it (no blast)
            {
                if (g.Constructs.Get(Ui.CommandPicked) is not { } tc || words.Length == 0 || !float.TryParse(words[0], inv, out var off))
                {
                    WsFail("missile: pick a craft first (cmdpick N), then missile M [SPEED]");
                    return true;
                }
                var mv = words.Length > 1 && float.TryParse(words[1], inv, out var sp) ? sp : 120f;
                var side = Vector3.Normalize(Vector3.Cross(tc.Vel.LengthSquared() > 1 ? Vector3.Normalize(tc.Vel) : Vector3.UnitZ, Vector3.UnitY) + new Vector3(0, 0.15f, 0));
                if (!float.IsFinite(side.X))
                    side = Vector3.UnitX;
                var from = tc.WorldCom + side * off;
                var way = Vector3.Normalize(Vector3.Cross(side, Vector3.UnitY) - side * 0.3f); // (it starts off across, then turns in)
                var id = g.Constructs.AddShell(new CShell { Kind = ShellKind.Missile, Pos = from, Prev = from, Tested = from, Vel = tc.Vel + way * mv, Thrust = 25f, Turn = 0.6f, Burn = 30 * World.TickRate, Target = tc.Id, Clear = 0, By = 255 });
                WsSay($"[nav] test missile {id} {off} m off construct {tc.Id}");
                _wsWait = 3;
                return true;
            }
            case "letgo" when g.IsServer: // "letgo [ID]" (host): the picked craft (or construct ID) let go by the port that holds it, as its pilot's key does
            {
                var which = words.Length > 0 && ushort.TryParse(words[0], out var lid) ? lid : Ui.CommandPicked;
                if (g.Constructs.Get(which) is not { CarriedBy: not 0 } held)
                {
                    WsFail("letgo: pick a craft a port holds first (cmdpick N)");
                    return true;
                }
                ConstructCarrier.LetGo(g.Constructs, held);
                WsSay($"[nav] construct {held.Id} let go");
                _wsWait = 3;
                return true;
            }
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
                if (kind is "ground" or "land" && words.Length == 2 && float.TryParse(words[1], inv, out var metres) && g.Space is { } gs && g.Constructs.Get(Ui.CommandPicked) is { } gc && GravityFrames.Point is { } ball)
                {
                    // "order ground M": a spot M metres along the ground from the picked craft, at its own height, as the menu's ground order gives.
                    // "order land M": the same spot on the ball's surface, to set down on (from space too: then along from under it).
                    var from = gc.SpaceShip != 0 ? ball.Center + Vector3.Normalize(gc.WorldCom - ball.Center) * (float)gs.System.Bodies[gs.Bubble.Body].Radius : gc.WorldCom;
                    var up = Vector3.Normalize(from - ball.Center);
                    var along = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                    var high = kind == "land" ? (float)gs.System.Bodies[gs.Bubble.Body].Radius : (from - ball.Center).Length();
                    var spot = ball.Center + Vector3.Normalize(from + along * metres - ball.Center) * high;
                    g.NavSend(gc.Id, kind == "land" ? NavKind.Land : NavKind.Ground, gs.Bubble.Body, Vec3d.From(spot), 0);
                    _wsWait = 10;
                    return true;
                }
                if (kind == "board" && words.Length == 3 && int.TryParse(words[1], out var bnth))
                {
                    // "order board N door|hull": the picked craft's pod to the Nth craft on the map (cmdpick's count), by its first
                    // event door, or through its biggest part (a hull piece) (GameNavBoard.cs).
                    Construct? bt = null;
                    var bseen = 0;
                    foreach (var c in g.Constructs.Bodies)
                        if (Ui.CmdIsCraft(c) && ++bseen == Math.Max(1, bnth))
                            bt = c;
                    if (bt == null || g.Constructs.Get(Ui.CommandPicked) is not { } bc)
                    {
                        WsFail($"order board: no craft {bnth}, or none picked");
                        return true;
                    }
                    var door = words[2].ToLowerInvariant() == "door";
                    var bpart = -1;
                    for (var i = 0; i < bt.PartCount; i++)
                        if (door ? Game.BoardIsDoor(bt, i) : bpart < 0 || Bulk(bt, i) > Bulk(bt, bpart))
                        {
                            bpart = i;
                            if (door)
                                break;
                        }
                    if (bpart < 0)
                    {
                        WsFail($"order board: construct {bt.Id} has no {(door ? "event door" : "part")}");
                        return true;
                    }
                    WsSay($"[nav] board construct {bt.Id} by its {bt.DefOf(bpart).Name} (part {bpart})");
                    g.NavSend(bc.Id, NavKind.Board, 0, new Vec3d(bt.Id, bpart, door ? 1 : 0), 0);
                    _wsWait = 10;
                    return true;
                }
                var why = kind switch
                {
                    "cancel" => Ui.CommandOrder(g, NavKind.None, "", 0),
                    "come" => Ui.CommandOrder(g, NavKind.Come, "", 0),
                    "dock" when words.Length == 2 && int.TryParse(words[1], out var nth) => WsDockOrder(g, nth),
                    "orbit" or "goto" when words.Length == 3 && double.TryParse(words[2], inv, out var km) => Ui.CommandOrder(g, kind == "orbit" ? NavKind.Orbit : NavKind.GoTo, words[1], km),
                    _ => "order orbit BODY KM | order goto BODY KM | order ground M | order land M | order come | order dock N | order board N door|hull | order cancel",
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
                    if (!Ui.CmdIsCraft(c) && !(c.CarriedBy != 0 && c.StabCount > 0)) // (a docked craft too, to see it ride)
                        continue;
                    var o = g.NavOf(c.Id);
                    var height = World.Map.Planetoid is { } pl ? (float)pl.Altitude(c.WorldCom) : c.WorldCom.Y;
                    var push = ConstructPhysics.NavDrive(c, out _, out _, out var burn);
                    var accel = c.Mass > 0 ? push / c.Mass : 0;
                    c.WorldFrame(0, out var cq, out _);
                    var tilt = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Transform(Vector3.UnitY, cq), ConstructPhysics.HoldUp(c.WorldCom)), -1, 1)) * 180 / MathF.PI; // (its deck against the ground's up)
                    WsSay(string.Create(inv, $"[nav] construct {c.Id} drive: {accel:0.00} m/s2, {(burn > 0 ? $"{c.FuelLeft / burn:0} s at full, dV {accel * c.FuelLeft / burn:0} m/s" : "no fuel burnt")}"));
                    WsSay(string.Create(inv, $"[nav] construct {c.Id}: {(c.SpaceAway != 0 ? "parked" : c.SpaceShip != 0 ? "space" : "sky")}, {height:0} m up, tilt {tilt:0} deg, {c.Vel.Length():0.0} m/s, spin {c.AngVel.Length():0.00} rad/s{(c.CarriedBy != 0 ? $", held by {c.CarriedBy}" : "")}, mass {c.Mass / 1000:0.0} t, fuel {c.FuelLeft:0} L, thrust {c.ThrustShow:0.00}, nav throttle {c.NavThrottle:0.00}, takes orders: {Game.NavProblem(c) ?? "yes"}; order: {(o == null ? "none" : $"{Game.NavText(s.System, o)}, {o.Phase}, {o.Distance:0} m to go, {o.SpeedError:0.0} m/s off{(o.ArriveNs != 0 ? $", trip leaves day {o.DepartNs / 864e11:0.0}, arrives day {o.ArriveNs / 864e11:0.0}, {o.DvNeed / 1000:0.00} km/s" : "")}")}; space day {s.Now.Seconds / 86400:0.00} at {s.Speed}x{((c.SpaceAway != 0 ? g.SpaceAwayLine(c) : g.SpaceOrbitLine(c)) is { } line ? "; " + line : "")}{(g.Local is { Alive: true } you && c.SpaceShip == 0 ? $"; {Vector3.Distance(you.Position, c.WorldCom):0} m from you" : "")}"));
                }
                return true;
            }
            case "spacewalk" when g.IsServer:
            {
                // "spacewalk KM [SIDE]": this player out on a spacewalk KM km over the ground under the picked craft, SIDE m (default 150) to one side of it, at rest.
                if (g.Constructs.Get(Ui.CommandPicked) is not { } c || GravityFrames.Point is not { } planet || World.Map.Planetoid is not { } pl
                    || words.Length < 1 || !float.TryParse(words[0], inv, out var km))
                {
                    WsFail("spacewalk KM [SIDE]: pick a craft first (command, cmdpick), on a planetoid");
                    return true;
                }
                var side = words.Length > 1 && float.TryParse(words[1], inv, out var sm) ? sm : 150f;
                var up = Vector3.Normalize(c.WorldCom - planet.Center);
                var across = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                me.Sim.RideBody = 0;
                me.Sim.Position = planet.Center + up * ((float)pl.Radius + km * 1000f) + across * side;
                me.Sim.Velocity = Vector3.Zero;
                WsSay(string.Create(inv, $"[nav] you are out at {km:0.0} km, {side:0} m beside construct {c.Id}'s line"));
                _wsWait = 10;
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
            case "travel" when g.IsServer: // "travel FUEL SHARE": Settings > Gameplay's Fuel use in space and Trip fuel (percent) for this run, not saved
                if (words.Length == 2 && int.TryParse(words[0], out var use) && int.TryParse(words[1], out var share))
                {
                    _settings.SpaceFuelPercent = Math.Clamp(use, Game.SpaceFuelLeast, 100);
                    _settings.TripFuelPercent = Math.Clamp(share, Game.TripFuelLeast, Game.TripFuelMost);
                    WsSay($"[nav] travel: fuel use in space {_settings.SpaceFuelPercent}%, trip fuel {_settings.TripFuelPercent}%");
                }
                else
                    WsFail("travel FUEL SHARE (percents)");
                return true;
            case "orbit": // "orbit": the picked craft's drawn conic this frame, to the metre (a frame between ticks: steady while it coasts)
            {
                if (g.Space is not { } s || g.Constructs.Get(Ui.CommandPicked) is not { } c)
                {
                    WsFail("orbit: pick a craft first (command, cmdpick)");
                    return true;
                }
                var t = g.SpaceRenderTime;
                if (!g.NavState(c, t, out var pos, out var vel))
                {
                    WsSay("[nav] orbit: no state");
                    return true;
                }
                var prim = NavGuidance.PrimaryAt(s.System, pos, t);
                s.System.StateOf(prim, t, out var bp, out var bv);
                var rr = pos - bp;
                var vv = vel - bv;
                if (vv.LengthSquared / 2 - s.System.Bodies[prim].Mu / rr.Length >= 0 || Vec3d.Cross(rr, vv).Length <= 1)
                {
                    WsSay(string.Create(inv, $"[nav] orbit round {s.System.Bodies[prim].Name} at day {t.Seconds / 86400:0.0000} ({s.Speed}x): open, {rr.Length:0} m out at {vv.Length:0.0} m/s"));
                    return true;
                }
                var ko = KeplerOrbit.FromState(s.System.Bodies[prim].Mu, pos - bp, vel - bv, t);
                WsSay(string.Create(inv, $"[nav] orbit round {s.System.Bodies[prim].Name} at day {t.Seconds / 86400:0.0000} ({s.Speed}x): low {ko.Periapsis:0} m, high {ko.Apoapsis:0} m, tilt {Math.Acos(Math.Clamp(Vec3d.Cross(ko.P, ko.Q).Z, -1, 1)) * 180 / Math.PI:0.000} deg, periapsis towards {ko.P.X:0.0000},{ko.P.Y:0.0000},{ko.P.Z:0.0000}"));
                return true;
            }
            case "speed" when g.IsServer:
                WsSay($"[nav] speed {arg}: {(int.TryParse(arg, out var w) && g.RequestTimeSpeed(w) ? "asked" : "refused")}");
                return true;
        }
        return false;
    }
}
