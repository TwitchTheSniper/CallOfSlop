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
    private double _cmdOrderNoteUntil;

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
        if (hit >= 0 && !come)
            return; // (another craft: nothing to tell it yet)
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
                }
                if (send != NavKind.None)
                    ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor(8);
        _cmdOrderShut = false;
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
