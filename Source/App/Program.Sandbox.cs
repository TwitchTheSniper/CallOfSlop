using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox mode on this PC (docs/sandbox/CONSTRUCTS.md): the keys (3 wrench, 4 welder, 1/2 back to the guns; with a
/// tool out and a part in hand, hold the fire button to fit it on the ghost socket under the crosshair, R turns it; hold
/// aim to take the part you look at off; F takes from the rack, sits, opens doors, picks up; T drops), the look target and
/// ghost sockets, and the constructs, ghosts and tool drawn in the world. The server decides everything (GameSandbox.cs).
/// </summary>
public static unsafe partial class Program
{
    private static int _sbVariantShift; // R presses: which way round the chosen part goes
    private static int _sbLastGhostKey = -1;
    private static bool _sbFiring, _sbRemoving;
    private static long _sbLookKey = -1;
    private static string _sbLookText = "", _sbGroundText = "";
    private static int _sbGroundKey = -1;
    private static ushort _sbWireBody; // the linking tool: the output a wire starts from (0: none)
    private static int _sbWirePart = -1;
    private static int _sbSeatKeys = -1;
    private static long _sbWireKey = -1;

    /// <summary>Sandbox with a tool out (or in a seat): the trigger and aim belong to the tools, not the gun.</summary>
    private static bool SandboxHoldsTrigger =>
        _game is { SandboxOn: true } g && g.Local is { Alive: true } me && (g.SandboxHands.Hand(me.Id).Tool != ConstructTool.None || g.SandboxHands.Hand(me.Id).Seated);

    /// <summary>The Sandbox keys, every frame while the mouse is captured. Returns true when the mode is on (the normal Interact, slot 3/4 and reload keys are then its own).</summary>
    private static bool SandboxInput(double now)
    {
        ConstructRender.ClearAim();
        if (_game is not { SandboxOn: true } g)
            return false;
        g.SandboxLookText = "";
        if (g.Local is not { Alive: true } me || !_mouseCaptured || MenusOpen)
        {
            _sbFiring = _sbRemoving = false;
            return true;
        }
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (Pressed(GameAction.Weapon3))
            g.SandboxSend(CActKind.Tool, part: (byte)(h.Tool == ConstructTool.Wrench ? ConstructTool.None : ConstructTool.Wrench));
        // (4 and 5 are the kill-streak keys by default, and Sandbox has no streaks: the welder and the linking tool take them)
        if (Pressed(GameAction.Weapon4) || Pressed(GameAction.Streak1))
            g.SandboxSend(CActKind.Tool, part: (byte)(h.Tool == ConstructTool.Welder ? ConstructTool.None : ConstructTool.Welder));
        if (Pressed(GameAction.Streak2))
            g.SandboxSend(CActKind.Tool, part: (byte)(h.Tool == ConstructTool.Linker ? ConstructTool.None : ConstructTool.Linker));
        if ((Pressed(GameAction.PrimaryWeapon) || Pressed(GameAction.SecondaryWeapon)) && h.Tool != ConstructTool.None)
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        if (h.Tool != ConstructTool.Linker)
        {
            _sbWireBody = 0;
            _sbWirePart = -1;
            g.SandboxWireFrom = "";
        }
        ConstructRender.ShowWires = h.Tool == ConstructTool.Linker;
        if (h.Seated)
        {
            if (Pressed(GameAction.Interact))
                g.SandboxSend(CActKind.Stand);
            // The keys a seat's input readers can read besides the movement keys: fire, aim, reload.
            var keys = (Down(GameAction.Fire) ? 1 : 0) | (Down(GameAction.Aim) ? 2 : 0) | (Down(GameAction.Reload) ? 4 : 0);
            if (keys != _sbSeatKeys)
            {
                _sbSeatKeys = keys;
                g.SandboxSend(CActKind.SeatKeys, part: (byte)keys);
            }
            return true;
        }
        if (_sbSeatKeys > 0)
        {
            _sbSeatKeys = 0;
            g.SandboxSend(CActKind.SeatKeys, part: 0);
        }
        if (Pressed(GameAction.DropItem) && h.Held != ConstructParts.None)
            g.SandboxSend(CActKind.Drop);

        // What the crosshair is on.
        var eye = me.RenderEye;
        var dir = g.LookDir(g.LookYaw, g.LookPitch);
        var wall = World.RaycastWorld(eye, dir, ConstructBuild.Reach + 1);
        var aimed = g.Constructs.Raycast(eye, dir, MathF.Min(wall, ConstructBuild.Reach + 1), out var hit);
        if (aimed)
        {
            ConstructRender.AimBody = hit.Body;
            ConstructRender.AimPart = hit.Part;
            var d = hit.Body.DefOf(hit.Part);
            ref var part = ref hit.Body.Parts[hit.Part];
            // (rebuilt only when what it shows changes: the look runs every frame)
            var near = hit.Distance <= ConstructBuild.UseReach;
            var fits = h.Tool != ConstructTool.None && ConstructBuild.ToolFits(h.Tool, d);
            var key = ((long)hit.Body.Id << 40) | ((long)hit.Part << 32) | ((long)(part.Broken ? 1 : 0) << 31) | ((long)(near ? 1 : 0) << 30) | ((long)(fits ? 1 : 0) << 29)
                      | ((long)(hit.Body.Driver == 255 ? 1 : 0) << 28) | ((long)(hit.Body.Loose ? 1 : 0) << 27) | (long)Math.Clamp((int)part.Health, 0, 1 << 26)
                      ^ ((long)(byte)ConstructLogic.Quantise(part.Out) << 17) ^ ((long)part.Setting << 12) ^ ((long)part.SitterPlus1 << 4) ^ (h.Tool == ConstructTool.Linker ? 1L << 62 : 0);
            if (key != _sbLookKey)
            {
                _sbLookKey = key;
                var setting = d.Settings.Length > 0 ? $" ({d.SettingName} {d.SettingLabels[Math.Min(part.Setting, (byte)(d.SettingLabels.Length - 1))]})" : "";
                var signal = d.IsSignal ? $" [{(d.Logic == CLogic.Speed ? part.Out.ToString("0.0") : part.Out >= 0.5f ? "on" : part.Out <= -0.5f ? "back" : "off")}]" : "";
                _sbLookText = d.Role switch
                {
                    _ when h.Tool == ConstructTool.Linker => $"{d.Name}{setting}{signal}{(d.HasOutput ? " - output" : "")}{(d.MaxInputs > 0 ? $" - takes {d.MaxInputs} input{(d.MaxInputs == 1 ? "" : "s")}" : "")}",
                    PartRole.Seat when near => part.SitterPlus1 != 0 || hit.Body.Driver != 255 && hit.Part == hit.Body.DriveSeat ? "Seat: taken" : hit.Part == hit.Body.DriveSeat ? "Seat: F to drive" : "Seat: F to ride",
                    PartRole.Logic when near && d.Logic is CLogic.Button or CLogic.Switch or CLogic.Lever => $"{d.Name}{signal}: F to {(d.Logic == CLogic.Button ? "press" : "flip")}",
                    PartRole.Door or PartRole.Hinge or PartRole.Joint when near => $"{d.Name}: F to open or shut",
                    PartRole.Logic => $"{d.Name}{setting}{signal}",
                    _ when hit.Body.Loose && near => $"{d.Name}: F to pick up",
                    _ => $"{d.Name} {(part.Broken ? "(wrecked)" : $"{part.Health:0}/{d.Health:0}")}{(fits ? " - hold aim to take it off" : "")}",
                };
            }
            g.SandboxLookText = _sbLookText;
        }
        var feet = me.Position;
        var nearRack = World.Map.Sandbox is { } sb && Vector3.Distance(feet with { Y = 0 }, sb.Rack) <= 4.5f;
        if (nearRack && !aimed)
            g.SandboxLookText = "Parts rack: F to take a part (again for the next kind)";
        if (Pressed(GameAction.Interact))
        {
            if (aimed && hit.Distance <= ConstructBuild.UseReach && hit.Body.Loose && h.Held == ConstructParts.None)
                g.SandboxSend(CActKind.PickUp, body: hit.Body.Id);
            else if (aimed && hit.Distance <= ConstructBuild.UseReach)
                g.SandboxSend(CActKind.Use, body: hit.Body.Id, part: (byte)hit.Part);
            else if (nearRack)
                g.SandboxSend(CActKind.Rack);
        }

        // The linking tool: fire on an output, then fire on an input to wire them; aim cuts a part's wires; R its setting.
        if (h.Tool == ConstructTool.Linker)
        {
            ConstructRender.WireFromBody = _sbWireBody;
            ConstructRender.WireFromPart = _sbWirePart;
            ConstructRender.WireEye = eye;
            ConstructRender.WireAim = aimed ? hit.Point : eye + dir * 3f;
            if (_sbWireBody != 0 && g.Constructs.Get(_sbWireBody) is null)
            {
                _sbWireBody = 0;
                _sbWirePart = -1;
            }
            if (aimed && Pressed(GameAction.Fire) && !_swallowClick)
            {
                var d = hit.Body.DefOf(hit.Part);
                if (_sbWireBody == 0)
                {
                    if (!d.HasOutput)
                        g.SandboxSay($"a {d.Name} has no output (start a wire at a seat, a control, a sensor or a gate)");
                    else
                    {
                        _sbWireBody = hit.Body.Id;
                        _sbWirePart = hit.Part;
                    }
                }
                else if (_sbWireBody != hit.Body.Id)
                {
                    g.SandboxSay("wires only join parts of the same construct");
                    _sbWireBody = 0;
                    _sbWirePart = -1;
                }
                else
                {
                    g.SandboxSend(CActKind.Wire, body: hit.Body.Id, part: (byte)_sbWirePart, socket: (byte)hit.Part);
                    _sbWireBody = 0;
                    _sbWirePart = -1;
                }
            }
            if (Pressed(GameAction.Aim))
            {
                if (_sbWireBody != 0)
                {
                    _sbWireBody = 0; // (aim lets go of a wire in progress)
                    _sbWirePart = -1;
                }
                else if (aimed)
                    g.SandboxSend(CActKind.Unwire, body: hit.Body.Id, part: (byte)hit.Part);
            }
            if (aimed && Pressed(GameAction.Reload))
                g.SandboxSend(CActKind.Setting, body: hit.Body.Id, part: (byte)hit.Part);
            var wk = _sbWireBody == 0 ? -1 : _sbWireBody * 256L + _sbWirePart;
            if (wk != _sbWireKey)
            {
                _sbWireKey = wk;
                g.SandboxWireFrom = _sbWireBody != 0 && g.Constructs.Get(_sbWireBody) is { } from && _sbWirePart < from.PartCount
                    ? $"Wire from the {from.DefOf(_sbWirePart).Name}: LMB on an input to join it (RMB lets go)" : "";
            }
            return true;
        }

        // Ghost sockets for the held part (a tool out only: bare hands never edit).
        var held = ConstructParts.Get(h.Held);
        if (h.Tool != ConstructTool.None && held != null)
        {
            ConstructRender.GhostDef = held;
            if (aimed && !hit.Body.Loose)
            {
                var c = hit.Body;
                c.WorldFrame(c.Parts[hit.Part].Seg, out var q, out var t);
                var near = Vector3.Transform(hit.Point - t, Quaternion.Conjugate(q));
                var n = ConstructBuild.Ghosts(c, held, ConstructRender.Ghosts, near, 2.5f);
                ConstructRender.GhostBody = c;
                ConstructRender.GhostCount = n;
                // The one nearest the crosshair.
                var best = -1;
                var bestD = 0.8f;
                for (var k = 0; k < n; k++)
                {
                    c.SocketAt(ConstructRender.Ghosts[k].Part, ConstructRender.Ghosts[k].Socket, out var at, out _);
                    c.SegPose(c.Parts[ConstructRender.Ghosts[k].Part].Seg, out var sq, out var st);
                    var world = c.ToWorld(st + Vector3.Transform(at, sq));
                    var along = Vector3.Dot(world - eye, dir);
                    var off = Vector3.Distance(world, eye + dir * along);
                    if (along > 0 && off < bestD)
                    {
                        bestD = off;
                        best = k;
                    }
                }
                ConstructRender.GhostSelected = best;
                if (best >= 0)
                {
                    ref var gh = ref ConstructRender.Ghosts[best];
                    var key = gh.Part * 64 + gh.Socket + c.Id * 4096;
                    if (key != _sbLastGhostKey)
                    {
                        _sbLastGhostKey = key;
                        _sbVariantShift = 0;
                    }
                    if (Pressed(GameAction.Reload) && ConstructBuild.NextVariant(c, held, gh.Part, gh.Socket, (gh.Variant + _sbVariantShift) % (held.Sockets.Length * 4)) is var next and >= 0)
                        _sbVariantShift = (next - gh.Variant + held.Sockets.Length * 4) % (held.Sockets.Length * 4);
                    var v = (gh.Variant + _sbVariantShift) % (held.Sockets.Length * 4);
                    ConstructRender.GhostValid = ConstructBuild.Place(c, gh.Part, gh.Socket, held, v / 4, v % 4, out var shown) == null;
                    if (ConstructRender.GhostValid)
                        gh = shown with { Variant = v };
                    if (Pressed(GameAction.Fire) && !_swallowClick)
                    {
                        g.SandboxSend(CActKind.Attach, body: c.Id, part: (byte)gh.Part, socket: (byte)gh.Socket, variant: (byte)v);
                        _sbFiring = true;
                    }
                }
            }
            else if (!aimed && wall <= ConstructBuild.Reach)
            {
                // The ground: a new construct of this part.
                var at = eye + dir * wall;
                if (World.Query.Raycast(at + new Vector3(0, 0.3f, 0), -Vector3.UnitY, 0.6f, QueryFilter.All) < 0.6f)
                {
                    ConstructRender.PlaceAt = at;
                    ConstructRender.PlaceYaw = g.LookYaw;
                    if (_sbGroundKey != held.Id)
                    {
                        _sbGroundKey = held.Id;
                        _sbGroundText = $"Ground: hold fire to set down the {held.Name} as a new construct";
                    }
                    g.SandboxLookText = _sbGroundText;
                    if (Pressed(GameAction.Fire) && !_swallowClick)
                    {
                        g.SandboxSend(CActKind.PlaceNew, at: at, yaw: g.LookYaw);
                        _sbFiring = true;
                    }
                }
            }
        }
        // Taking a part off: hold aim on it with the tool it needs.
        if (h.Tool != ConstructTool.None && aimed && Pressed(GameAction.Aim) && !hit.Body.Loose)
        {
            g.SandboxSend(CActKind.Remove, body: hit.Body.Id, part: (byte)hit.Part);
            _sbRemoving = true;
        }
        // Let go before it's done: the edit stops.
        if ((_sbFiring && !Down(GameAction.Fire)) || (_sbRemoving && !Down(GameAction.Aim)))
        {
            if (h.Edit != EditKind.None)
                g.SandboxSend(CActKind.Cancel);
            _sbFiring = _sbRemoving = false;
        }
        return true;
    }

    /// <summary>The constructs, ghosts and tool (called from the world pass; nothing outside Sandbox).</summary>
    private static void DrawSandbox(double now)
    {
        if (_game is not { } g || (g.Constructs.Bodies.Count == 0 && !g.SandboxOn))
            return;
        g.SandboxNow = g.IsServer ? g.SandboxNow : now;
        if (!g.IsServer)
        {
            ConstructNet.Blend(g.Constructs, now, (float)FrameDt);
            ConstructRender.FitWheels(g.Constructs, World.Query);
        }
        ConstructRender.Draw(Gfx, g.Constructs, g.TickAlpha, blendTicks: g.IsServer);
        g.SandboxSounds(now); // horns (GameSandbox.cs)
    }

    /// <summary>The tool in your hands, instead of the gun (Sandbox only).</summary>
    private static bool DrawSandboxTool(double now, Vector3 eye, Vector3 fwd)
    {
        if (_game is not { SandboxOn: true } g || g.Local is not { Alive: true } me)
            return false;
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (h.Seated)
            return true; // hands on the wheel: no gun
        if (h.Tool == ConstructTool.None)
            return false;
        ConstructRender.DrawTool(Gfx, h.Tool, eye, fwd, now, h.Progress(g.SandboxNow));
        return true;
    }
}
