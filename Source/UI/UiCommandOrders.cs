using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view's orders (UiCommand.cs): a right click on a world, on the ground of the world the picked ship flies
/// over, or on a place in space opens a small menu of what the ship can be told to do there; the choice goes to the host
/// (<see cref="Game.NavSend"/>), which flies it (Game/GameNav.cs).
/// </summary>
public sealed partial class Ui
{
    private bool _cmdOrderOpen, _cmdOrderAsk, _cmdOrderShut;
    private ushort _cmdOrderShip;
    private int _cmdOrderBody = -1; // the world to orbit (-1: none under the click)
    private int _cmdOrderPointBody = -1; // the body a point in space is measured from (-1: no point)
    private Vec3d _cmdOrderPoint, _cmdOrderGround, _cmdOrderLand;
    private bool _cmdOrderHasGround, _cmdOrderHasLand, _cmdOrderCome;
    private float _cmdOrderKm, _cmdOrderKmMin, _cmdOrderKmMax;
    private float2 _cmdOrderAt;
    private string _cmdOrderComeText = "", _cmdOrderTitle = "", _cmdOrderOrbit = "", _cmdOrderGo = "", _cmdOrderNote = "";
    private ushort _cmdOrderDock; // another craft under the click: dock with it (0: none)
    private string _cmdOrderDockText = "", _cmdOrderDockWhy = "";
    private double _cmdOrderNoteUntil;
    private bool _cmdOrderBoard, _cmdOrderBoardDoor; // a Board order (Game/GameNavBoard.cs): the ship's pod to a door or the hull of another
    private ushort _cmdOrderBoardShip;
    private int _cmdOrderBoardPart;
    private string _cmdOrderBoardText = "";
    private bool _cmdOrderBoardAny, _cmdOrderBoardDoors; // a pod docked but no spot under the click: Board buttons that pick one after (and does it have doors)

    /// <summary>A right click on the map: what could the picked ship do there?</summary>
    private void CmdOrderAt(Game g, SpaceSession s, float2 m)
    {
        var k = CmdFind(_cmdSel);
        if (k < 0 || g.Constructs.Get(_cmdSel) is not { } c)
        {
            CmdNote("PICK A SHIP FIRST (LEFT CLICK ONE)");
            return;
        }
        if (Game.NavProblem(c) is { } why)
        {
            CmdNote(CmdName(c) + " TAKES NO ORDERS: " + why.ToUpperInvariant());
            return;
        }
        var sys = s.System;
        ref var cr = ref _cmdCraft[k];
        var hit = CmdHit(s, m, out var body);
        // On your own marker: it can come to you (a craft beside you is under the click too).
        var come = CmdCanCome(g, c) && g.Local is { } me && (m - CmdP(_cvPose.ToEcliptic(Vec3d.From(me.Position)))).Length() < 14 * _scale;
        _cmdOrderDock = 0;
        _cmdOrderBoard = false;
        if (hit >= 0 && !come)
        {
            // Another craft: dock with it, at a free docking port on it (GameNavDock.cs), or say there is none; with a boarding pod
            // docked, also board it by the door or hull panel under the click (GameNavBoard.cs).
            ref var other = ref _cmdCraft[hit];
            if (other.Id == c.Id || g.Constructs.Get(other.Id) is not { } t)
                return; // (itself: nothing to tell it)
            _cmdOrderBoardAny = _cmdOrderBoardDoors = false;
            if (Game.BoardPodOf(g.Constructs, c) != null)
            {
                if (other.Px < 40 * _scale || !CmdBoardPick(t, other, m, out var part, out var door))
                {
                    _cmdOrderBoardAny = true; // (zoomed out, or off its hull: pick the spot after, the view flies to it: UiCommandBoard.cs)
                    _cmdOrderBoardShip = t.Id;
                    for (var i = 0; i < t.PartCount && !_cmdOrderBoardDoors; i++)
                        _cmdOrderBoardDoors = Game.BoardIsDoor(t, i);
                }
                else
                {
                    _cmdOrderBoard = true;
                    _cmdOrderBoardShip = t.Id;
                    _cmdOrderBoardPart = part;
                    _cmdOrderBoardDoor = door;
                    _cmdOrderBoardText = (door ? "BOARD: SEAL ON THIS DOOR (" : "BOARD: CUT IN HERE (") + t.DefOf(part).Name.ToUpperInvariant() + ")";
                }
            }
            _cmdOrderShip = c.Id;
            _cmdOrderBody = -1;
            _cmdOrderPointBody = -1;
            _cmdOrderHasGround = _cmdOrderHasLand = _cmdOrderCome = false;
            _cmdOrderDock = t.Id;
            _cmdOrderDockText = "DOCK WITH " + CmdName(t);
            _cmdOrderDockWhy = c.SpaceAway != 0 || t.SpaceAway != 0 ? "TOO FAR APART TO DOCK"
                : cr.Space && !other.Space ? "IT IS IN SPACE: BRING IT DOWN INTO THE SKY FIRST"
                : Game.NavDockFor(g.Constructs, t, c) < 0 ? "NO PORT AVAILABLE"
                : "";
            _cmdOrderTitle = "ORDER FOR " + CmdName(c);
            _cmdOrderAt = m;
            _cmdOrderAsk = true;
            return;
        }
        _cmdOrderShip = c.Id;
        _cmdOrderBody = -1;
        _cmdOrderPointBody = -1;
        _cmdOrderHasGround = _cmdOrderHasLand = false;
        _cmdOrderCome = come;
        _cmdOrderComeText = come && g.Local is { } who && CmdOutInSpace(g, who) ? "COME TO ME AND HOLD BESIDE ME" : "COME TO ME AND LAND";
        if (come)
            body = s.Bubble.Body;
        if (body > 0)
        {
            _cmdOrderBody = body;
            var b = sys.Bodies[body];
            var least = Game.NavMinOrbit(sys, body);
            _cmdOrderKmMin = (float)((least - b.Radius) / 1000);
            _cmdOrderKmMax = (float)Math.Max(_cmdOrderKmMin * 4, Math.Min(0.5 * SpaceShips.SphereOfInfluence(sys, body), 60 * b.Radius) / 1000);
            _cmdOrderKm = MathF.Round(_cmdOrderKmMin + 0.3f, 1);
            _cmdOrderOrbit = "ORBIT " + _cmdBodyNames[body];
            // The ground under the click: a spot to land on, from the sky or from space, and in its sky a point to fly over.
            if (body == s.Bubble.Body && !come && c.SpaceAway == 0 && CmdOnBall(m, sys.PositionOf(body, _cvTime), b.Radius, out var onBall))
            {
                var world = _cvPose.FromEcliptic(sys.PositionOf(body, _cvTime) + onBall);
                var centre = s.Bubble.GameCentre;
                var up = (world - centre).Normalized();
                _cmdOrderLand = centre + up * b.Radius;
                _cmdOrderHasLand = CmdCanLand(c);
                // (at the ship's own height over the ground: the autopilot holds that)
                _cmdOrderGround = centre + up * (Vec3d.From(c.WorldCom) - centre).Length;
                _cmdOrderHasGround = c.SpaceShip == 0;
            }
        }
        else if (body < 0)
        {
            // A place in space: on the ecliptic plane through the ship's world, measured from whichever body rules there.
            var primary = cr.Primary;
            var pp = sys.PositionOf(primary, _cvTime);
            if (!CmdUnproject(m, pp, Vec3d.UnitZ, out var from))
            {
                CmdNote("TILT THE VIEW DOWN TO PICK A PLACE (RIGHT DRAG)");
                return;
            }
            var place = pp + from;
            var rules = NavGuidance.PrimaryAt(sys, place, _cvTime);
            _cmdOrderPointBody = rules;
            _cmdOrderPoint = place - sys.PositionOf(rules, _cvTime);
            // (told as the card tells heights: from the body's surface)
            _cmdOrderGo = "GO TO THIS PLACE, " + CmdDistance(Math.Max(0, _cmdOrderPoint.Length - sys.Bodies[rules].Radius)).ToUpperInvariant() + " FROM " + _cmdBodyNames[rules];
        }
        else
            return; // (the star: nothing to do there)
        _cmdOrderTitle = "ORDER FOR " + CmdName(c);
        _cmdOrderAt = m;
        _cmdOrderAsk = true;
    }

    private void CmdNote(string text)
    {
        _cmdOrderNote = text;
        _cmdOrderNoteUntil = _cmdLast + 3;
    }

    /// <summary>The order menu (and the note under the presets when a click could give no order).</summary>
    private void CmdOrderPopup(Game g, SpaceSession s)
    {
        if (_cmdLast < _cmdOrderNoteUntil)
            CmdText(ImGui.GetWindowDrawList(), new float2(_cvCentre.X, _cvOrg.Y + 92 * _scale), 13, CmdAmber, (float)Math.Min(1, _cmdOrderNoteUntil - _cmdLast), _cmdOrderNote, 1);
        if (_cmdOrderAsk)
        {
            _cmdOrderAsk = false;
            ImGui.SetNextWindowPos(_cmdOrderAt + new float2(14 * _scale, 6 * _scale), ImGuiCond.Always);
            ImGui.OpenPopup("##cmdorder"u8);
        }
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new float4(0.01f, 0.05f, 0.08f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, new float4(CmdCyan.X, CmdCyan.Y, CmdCyan.Z, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.10f, 0.30f, 0.38f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new float4(0.18f, 0.48f, 0.60f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new float4(0.26f, 0.62f, 0.76f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new float4(0.05f, 0.16f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, new float4(CmdCyan.X, CmdCyan.Y, CmdCyan.Z, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new float4(CmdWhite.X, CmdWhite.Y, CmdWhite.Z, 1f));
        _cmdOrderOpen = ImGui.BeginPopup("##cmdorder"u8);
        if (_cmdOrderOpen)
        {
            var sys = s.System;
            var send = NavKind.None;
            var board = 0; // (1: pick a hull spot to cut into, 2: a door to seal on)
            ImGuiSafe.TextColored(new float4(CmdAmber.X, CmdAmber.Y, CmdAmber.Z, 1f), _cmdOrderTitle);
            ImGui.Separator();
            if (_cmdOrderShut || g.Constructs.Get(_cmdOrderShip) is not { } c || Game.NavProblem(c) != null)
                ImGui.CloseCurrentPopup();
            else
            {
                if (_cmdOrderBody > 0)
                {
                    ImGui.SetNextItemWidth(220 * _scale);
                    ImGui.SliderFloat("km up##cmdorbit"u8, ref _cmdOrderKm, _cmdOrderKmMin, _cmdOrderKmMax, "%.1f"u8, ImGuiSliderFlags.Logarithmic);
                    if (ImGui.Button(_cmdOrderOrbit, new float2(300 * _scale, 0)))
                        send = NavKind.Orbit;
                    if (_cmdOrderCome && ImGui.Button(_cmdOrderComeText, new float2(300 * _scale, 0)))
                        send = NavKind.Come;
                    if (_cmdOrderHasLand && ImGui.Button("LAND HERE"u8, new float2(300 * _scale, 0)))
                        send = NavKind.Land;
                    if (_cmdOrderHasGround && ImGui.Button("FLY TO THIS SPOT OVER THE GROUND"u8, new float2(300 * _scale, 0)))
                        send = NavKind.Ground;
                }
                if (_cmdOrderDock != 0)
                {
                    if (_cmdOrderDockWhy.Length > 0)
                        ImGuiSafe.TextColored(new float4(CmdAmber.X, CmdAmber.Y, CmdAmber.Z, 1f), _cmdOrderDockWhy);
                    else if (ImGui.Button(_cmdOrderDockText, new float2(MathF.Max(300 * _scale, ImGui.CalcTextSize(_cmdOrderDockText).X + 20 * _scale), 0)))
                        send = NavKind.Dock;
                }
                if (_cmdOrderBoard)
                {
                    var bw = MathF.Max(300 * _scale, ImGui.CalcTextSize(_cmdOrderBoardText).X + 20 * _scale);
                    if (ImGui.Button(_cmdOrderBoardText, new float2(bw, 0)))
                    {
                        g.NavSend(_cmdOrderShip, NavKind.Board, 0, new Vec3d(_cmdOrderBoardShip, _cmdOrderBoardPart, _cmdOrderBoardDoor ? 1 : 0), 0);
                        ImGui.CloseCurrentPopup();
                    }
                }
                else if (_cmdOrderBoardAny)
                {
                    if (ImGui.Button("BOARD: CUT INTO ITS HULL..."u8, new float2(300 * _scale, 0)))
                        board = 1;
                    if (_cmdOrderBoardDoors && ImGui.Button("BOARD: SEAL ON A DOOR..."u8, new float2(300 * _scale, 0)))
                        board = 2;
                }
                var wide = _cmdOrderPointBody >= 0 ? MathF.Max(300 * _scale, ImGui.CalcTextSize(_cmdOrderGo).X + 20 * _scale) : 300 * _scale;
                if (_cmdOrderPointBody >= 0 && ImGui.Button(_cmdOrderGo, new float2(wide, 0)))
                    send = NavKind.GoTo;
                if (g.NavOf(_cmdOrderShip) != null && ImGui.Button("CANCEL ITS ORDER"u8, new float2(wide, 0)))
                {
                    g.NavSend(_cmdOrderShip, NavKind.None, 0, Vec3d.Zero, 0);
                    ImGui.CloseCurrentPopup();
                }
                switch (send)
                {
                    case NavKind.Orbit:
                        g.NavSend(_cmdOrderShip, NavKind.Orbit, _cmdOrderBody, Vec3d.Zero, sys.Bodies[_cmdOrderBody].Radius + _cmdOrderKm * 1000.0);
                        break;
                    case NavKind.Ground:
                        g.NavSend(_cmdOrderShip, NavKind.Ground, _cmdOrderBody, _cmdOrderGround, 0);
                        break;
                    case NavKind.GoTo:
                        g.NavSend(_cmdOrderShip, NavKind.GoTo, _cmdOrderPointBody, _cmdOrderPoint, 0);
                        break;
                    case NavKind.Land:
                        g.NavSend(_cmdOrderShip, NavKind.Land, _cmdOrderBody, _cmdOrderLand, 0);
                        break;
                    case NavKind.Come:
                        g.NavSend(_cmdOrderShip, NavKind.Come, _cmdOrderBody, Vec3d.Zero, 0); // (the host works out the spot beside you)
                        break;
                    case NavKind.Dock:
                        g.NavSend(_cmdOrderShip, NavKind.Dock, s.Bubble.Body, Vec3d.Zero, 0, _cmdOrderDock); // (the host picks the port, and says when there is none)
                        break;
                }
                if (board != 0 && g.Constructs.Get(_cmdOrderBoardShip) is { } bt && g.Constructs.Get(_cmdOrderShip) is { } carrier)
                    CmdBoardStart(carrier, bt, board == 2); // (the view flies to it: UiCommandBoard.cs)
                if (send != NavKind.None || board != 0)
                    ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor(8);
        _cmdOrderShut = false;
    }

    /// <summary>
    /// The part of craft <paramref name="t"/> under the cursor, as its hologram is drawn (UiCommandShip.cs): of the parts whose
    /// middles lie within a few pixels, the one nearest the eye, or a door among them not far behind it (with <paramref name="doors"/>).
    /// False: none there.
    /// </summary>
    private bool CmdBoardPick(Construct t, in CmdCraft cr, float2 m, out int part, out bool door, bool doors = true)
    {
        part = -1;
        door = false;
        var turn = t.SpaceAway != 0 ? Game.AwayRot : _cvPose.Rot;
        Span<float2> ax = stackalloc float2[3];
        Span<float> deep = stackalloc float[3];
        for (var k = 0; k < 3; k++)
        {
            var e = turn.Rotate(Vec3d.From(Vector3.Transform(k == 0 ? Vector3.UnitX : k == 1 ? Vector3.UnitY : Vector3.UnitZ, t.Rot)));
            ax[k] = new float2((float)(Vec3d.Dot(e, _cvRight) * _cvScale), -(float)(Vec3d.Dot(e, _cvUp) * _cvScale));
            deep[k] = (float)Vec3d.Dot(e, _cvDir);
        }
        var com = t.Com;
        var reach = 12 * _scale;
        float bestDeep = float.MaxValue, doorDeep = float.MaxValue;
        var doorPart = -1;
        for (var i = 0; i < t.PartCount; i++)
        {
            var p = t.Parts[i].Pos - com;
            var at = cr.At + ax[0] * p.X + ax[1] * p.Y + ax[2] * p.Z;
            var isDoor = doors && Game.BoardIsDoor(t, i);
            if ((at - m).Length() > (isDoor ? reach * 1.5f : reach))
                continue;
            var d = deep[0] * p.X + deep[1] * p.Y + deep[2] * p.Z;
            if (d < bestDeep)
            {
                bestDeep = d;
                part = i;
            }
            if (isDoor && d < doorDeep)
            {
                doorDeep = d;
                doorPart = i;
            }
        }
        if (doorPart >= 0 && doorDeep < bestDeep + 1.5f)
        {
            part = doorPart;
            door = true;
        }
        return part >= 0;
    }

    /// <summary>Can this ship be told to come to the local player: they are on foot on the bubble's world, and it can land?</summary>
    private static bool CmdCanCome(Game g, Construct c) =>
        g.Local is { Alive: true } me && !g.SandboxHands.Hand(me.Id).Seated && me.Sim.RideBody != c.Id && c.SpaceAway == 0 && (CmdCanLand(c) || CmdOutInSpace(g, me));

    /// <summary>Is a player out in space (over the sky's edge)? A ship told to come to them holds beside them there (Game/GameNavLand.cs).</summary>
    private static bool CmdOutInSpace(Game g, Player p) => g.SpaceEdge > 0 && ConstructPhysics.HoldHeight(p.Position) > g.SpaceEdge;

    /// <summary>Can it set down on a spot (not a plane or a rocket, which land on their own)?</summary>
    private static bool CmdCanLand(Construct c)
    {
        if (c.StabCount == 0 || c.Stabs[0].Part >= c.PartCount)
            return false;
        ref var p = ref c.Parts[c.Stabs[0].Part];
        return ConstructFlight.KeysOf(c, p, p.D) is not (FlightPattern.Plane or FlightPattern.Rocket);
    }

    /// <summary>Esc in the view: an open order menu closes first. False: nothing was open, the view itself may close.</summary>
    public bool CommandEsc()
    {
        if (!_cmdOrderOpen && _cmdBoardShip != 0)
        {
            CmdBoardEnd(); // (a Board pick: the view goes back, no order)
            return true;
        }
        if (!_cmdOrderOpen)
            return false;
        _cmdOrderShut = true;
        return true;
    }

    /// <summary>The script and the checks: the order menu's choice without the mouse. Null, or why not.</summary>
    public string? CommandOrder(Game g, NavKind kind, string bodyName, double km)
    {
        if (g.Space is not { } s)
            return "no star system here";
        if (g.Constructs.Get(_cmdSel) is not { } c)
            return "no ship is picked";
        if (kind == NavKind.None)
        {
            g.NavSend(c.Id, NavKind.None, 0, Vec3d.Zero, 0);
            return null;
        }
        if (kind == NavKind.Come)
        {
            g.NavSend(c.Id, kind, s.Bubble.Body, Vec3d.Zero, 0);
            return null;
        }
        var body = s.System.IndexOf(bodyName);
        if (body <= 0)
            return "no such world: " + bodyName;
        if (kind == NavKind.Orbit)
            g.NavSend(c.Id, kind, body, Vec3d.Zero, s.System.Bodies[body].Radius + km * 1000);
        else
            g.NavSend(c.Id, NavKind.GoTo, body, new Vec3d(s.System.Bodies[body].Radius + km * 1000, 0, 0), 0);
        return null;
    }

    /// <summary>The script and the checks: the picked craft is told to dock with <paramref name="target"/>, as the menu's DOCK WITH would. Null, or why not (as the menu says it).</summary>
    public string? CommandDock(Game g, ushort target)
    {
        if (g.Space is not { } s)
            return "no star system here";
        if (g.Constructs.Get(_cmdSel) is not { } c)
            return "no ship is picked";
        if (g.Constructs.Get(target) is not { } t || t == c)
            return "no such ship to dock with";
        if (Game.NavDockFor(g.Constructs, t, c) < 0)
            return "NO PORT AVAILABLE";
        g.NavSend(c.Id, NavKind.Dock, s.Bubble.Body, Vec3d.Zero, 0, target);
        return null;
    }

    /// <summary>The script: the view's zoom (metres up the screen) and which craft is picked.</summary>
    public void CommandLook(double span, ushort pick = 0)
    {
        if (span > 0)
            _cmdSpan = _cmdSpanWant = span;
        if (pick != 0)
            _cmdSel = pick;
    }

    /// <summary>The script: the view's turn about the ecliptic's north and its tilt (radians; a quarter turn looks straight down).</summary>
    public void CommandTurn(float yaw, float pitch)
    {
        _cmdYaw = yaw;
        _cmdPitch = Math.Clamp(pitch, 0.18f, MathF.PI / 2);
    }

    /// <summary>The craft picked in the view (0: none).</summary>
    public ushort CommandPicked => _cmdSel;
}
