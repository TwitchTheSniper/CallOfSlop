using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view (docs/space/COMMAND.md): the star system as a hologram over the dimmed game, from one ship's decks out
/// to every planet's orbit, on one wheel. It draws what the space clock and the constructs already say (bodies on their
/// rails, each craft where it is, the conic it is on), lets the player pick a craft and read it (UiCommandShip.cs), and
/// sends it orders (UiCommandOrders.cs; the host flies them, Game/GameNav.cs). Nothing here changes the game: it is a
/// picture and a few messages. Closed, it costs one bool check a frame.
/// <para>
/// The picture is an orthographic one: a focus (a ship, a body, or by itself whichever suits the zoom), a turn and a tilt
/// about the ecliptic's north, and metres to pixels. Left drag pans, right drag turns, the wheel zooms, a click picks, a
/// double click centres, a right click on a place gives the picked ship an order.
/// </para>
/// </summary>
public sealed partial class Ui
{
    public bool CommandOpen;

    // The hologram's colours.
    private static readonly Vector3 CmdCyan = new(0.42f, 0.90f, 1.00f), CmdDim = new(0.20f, 0.52f, 0.66f), CmdWhite = new(0.93f, 0.98f, 1f),
        CmdAmber = new(1f, 0.74f, 0.28f), CmdRed = new(1f, 0.36f, 0.30f), CmdGreen = new(0.45f, 1f, 0.72f), CmdBack = new(0.008f, 0.035f, 0.06f);

    /// <summary>The narrowest and widest the view goes (metres up the screen).</summary>
    public const double CommandMinSpan = 12;

    private double _cmdSpan = 400, _cmdSpanWant = 400;
    private float _cmdYaw = 0.55f, _cmdPitch = 0.92f;
    private bool _cmdFocusAuto = true;
    private int _cmdFocusBody = -1;
    private ushort _cmdFocusShip;
    private int _cmdFocusKey = int.MinValue;
    private SpacePos _cmdFocusPos;
    private Vec3d _cmdPan, _cmdGlide;
    private ushort _cmdSel;
    private int _cmdSelBody = -1;
    private double _cmdOpened, _cmdLast;
    private float2 _cmdDownL, _cmdDownR;
    private bool _cmdDragL, _cmdDragR, _cmdHeldL, _cmdHeldR;
    private Game? _cmdGame;

    // This frame's projection.
    private SpacePos _cvFocus;
    private Vec3d _cvRight, _cvUp, _cvDir;
    private double _cvScale;
    private float2 _cvCentre, _cvOrg, _cvSize;
    private SpaceTime _cvTime;
    private FramePose _cvPose;

    /// <summary>A craft the view lists: where it is this frame.</summary>
    private struct CmdCraft
    {
        public ushort Id;
        public SpacePos Pos;
        public Vec3d Vel;
        public int Primary;
        public float2 At;
        public float Px; // its radius on the screen
        public bool Orders; // it takes them
        public bool Space; // out of the bubble, a space ship (a client's: one of the host's ships pictures it)
    }

    private readonly CmdCraft[] _cmdCraft = new CmdCraft[128];
    private int _cmdCraftCount;
    private readonly float2[] _cmdPts = new float2[193];

    /// <summary>A line of text made again only when its key changes.</summary>
    private struct CmdLabel
    {
        public long Key;
        public string? Text;

        public bool Stale(long key)
        {
            if (Text != null && key == Key)
                return false;
            Key = key;
            return true;
        }
    }

    /// <summary>Opens or closes the view; opening picks the craft the player is in (or the nearest) and frames it.</summary>
    public void SetCommandOpen(bool open, Game? session, double now)
    {
        CommandOpen = open && session is { SandboxOn: true, Space: not null };
        _cmdOrderOpen = false;
        if (!CommandOpen)
            return;
        _cmdOpened = now;
        _cmdLast = now;
        _cmdGame = session;
        _cmdHeldL = _cmdHeldR = _cmdDragL = _cmdDragR = false;
        if (session!.Constructs.Get(_cmdSel) is not { } had || !CmdIsCraft(had))
            _cmdSel = CmdNearestCraft(session);
        _cmdFocusAuto = true;
        _cmdFocusBody = -1;
        _cmdFocusShip = 0;
        _cmdFocusKey = int.MinValue;
        _cmdPan = _cmdGlide = Vec3d.Zero;
        _cmdSpanWant = session.Constructs.Get(_cmdSel) is { } c ? Math.Max(40, c.Radius * 5.5) : 4000;
        _cmdSpan = _cmdSpanWant * 6; // (it comes in from further out)
    }

    /// <summary>Is this construct a craft the view shows: something that flies, or is in space?</summary>
    public static bool CmdIsCraft(Construct c) =>
        c.PartCount > 0 && !c.Wreck && c.CarriedBy == 0 && (c.SpaceShip != 0 || c.StabCount > 0 || c.ThrusterCount > 0 && c.DriveSeat >= 0);

    /// <summary>The craft the local player sits in, else the nearest to them (0: none).</summary>
    private static ushort CmdNearestCraft(Game g)
    {
        if (g.Local is not { } me)
            return 0;
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (h.Seated && g.Constructs.Get(h.SeatBody) is { } seat && CmdIsCraft(seat))
            return seat.Id;
        ushort best = 0;
        var bestD = float.MaxValue;
        foreach (var c in g.Constructs.Bodies)
        {
            if (!CmdIsCraft(c) || c.SpaceAway != 0)
                continue;
            var d = Vector3.DistanceSquared(c.WorldCom, me.Position) - c.Radius * c.Radius;
            if (d < bestD)
            {
                bestD = d;
                best = c.Id;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The frame
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The view, when open. <paramref name="keyName"/>: what closes it, for the hint.</summary>
    public void DrawCommand(Game? session, double now, string keyName)
    {
        if (!CommandOpen)
            return;
        if (session is not { SandboxOn: true, Space: { } s })
        {
            CommandOpen = false;
            return;
        }
        _cmdGame = session;
        var dt = (float)Math.Clamp(now - _cmdLast, 0, 0.1);
        _cmdLast = now;
        var vp = ImGui.GetMainViewport();
        _cvOrg = vp.Pos;
        _cvSize = vp.Size;
        ImGui.SetNextWindowPos(vp.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(vp.Size, ImGuiCond.Always);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoNav
                                       | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoScrollWithMouse;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0, 0));
        var shown = ImGui.Begin("##commandview"u8, flags);
        ImGui.PopStyleVar();
        if (shown)
        {
            var dl = ImGui.GetWindowDrawList();
            var fade = (float)Math.Clamp((now - _cmdOpened) / 0.35, 0, 1);
            CmdFrame(session, s, dt, now);
            CmdBackdrop(dl, fade, now);
            CmdGrid(dl, s, fade);
            CmdBodies(dl, session, s, fade, now);
            CmdCrafts(dl, session, s, fade, now);
            CmdChrome(dl, session, s, fade, now, keyName);
            CmdRoster(dl, session, s, fade);
            CmdShipCard(dl, session, s, fade, now);
            CmdInput(session, s);
            CmdOrderPopup(session, s);
        }
        ImGui.End();
    }

    /// <summary>The clock, the craft list and the projection for this frame.</summary>
    private void CmdFrame(Game g, SpaceSession s, float dt, double now)
    {
        _cvTime = g.SpaceRenderTime;
        _cvPose = s.Bubble.PoseAt(_cvTime);
        var sys = s.System;

        // The craft, where they are.
        _cmdCraftCount = 0;
        foreach (var c in g.Constructs.Bodies)
        {
            if (_cmdCraftCount >= _cmdCraft.Length || !CmdIsCraft(c) || !g.NavState(c, _cvTime, out var pos, out var vel))
                continue;
            ref var k = ref _cmdCraft[_cmdCraftCount++];
            k.Id = c.Id;
            k.Pos = pos;
            k.Vel = vel;
            k.Primary = NavGuidance.PrimaryAt(sys, pos, _cvTime);
            k.Orders = Game.NavProblem(c) == null;
            k.Space = g.SpaceShipIdOf(c) != 0;
        }
        if (_cmdSel != 0 && CmdFind(_cmdSel) < 0)
            _cmdSel = 0;

        // The zoom eases towards what the wheel asked.
        var widest = 3.0 * CmdSystemReach(sys);
        _cmdSpanWant = Math.Clamp(_cmdSpanWant, CommandMinSpan, widest);
        _cmdSpan = Math.Exp(Math.Log(_cmdSpan) + (Math.Log(_cmdSpanWant) - Math.Log(_cmdSpan)) * (1 - Math.Exp(-dt * 9)));
        _cmdSpan = Math.Clamp(_cmdSpan, CommandMinSpan * 0.5, widest * 8);

        // The focus: pinned, or whatever suits the zoom round the picked craft (or the player).
        SpacePos anchor;
        var anchorShip = CmdFind(_cmdSel);
        if (anchorShip >= 0)
            anchor = _cmdCraft[anchorShip].Pos;
        else
            anchor = _cvPose.ToEcliptic(Vec3d.From(g.Local?.Position ?? default));
        int key;
        SpacePos at;
        if (!_cmdFocusAuto && _cmdFocusShip != 0 && CmdFind(_cmdFocusShip) is var fs and >= 0)
        {
            key = 1000 + _cmdFocusShip;
            at = _cmdCraft[fs].Pos;
        }
        else if (!_cmdFocusAuto && _cmdFocusBody >= 0 && _cmdFocusBody < sys.Count)
        {
            key = _cmdFocusBody;
            at = sys.PositionOf(_cmdFocusBody, _cvTime);
        }
        else
        {
            _cmdFocusAuto = true;
            var prim = NavGuidance.PrimaryAt(sys, anchor, _cvTime);
            var planet = prim;
            while (planet > 0 && sys.Bodies[planet].Parent > 0)
                planet = sys.Bodies[planet].Parent;
            var body = -1;
            if (prim == 0 || _cmdSpan > 0.05 * sys.Bodies[planet].Orbit.A)
                body = prim == 0 && _cmdSpan < 0.05 * CmdSystemReach(sys) ? -1 : 0;
            else if (prim != planet && _cmdSpan > 3 * sys.Bodies[prim].Orbit.A)
                body = planet;
            else if (_cmdSpan > 5 * sys.Bodies[prim].Radius)
                body = prim;
            key = body >= 0 ? body : anchorShip >= 0 ? 1000 + _cmdSel : -2;
            at = body >= 0 ? sys.PositionOf(body, _cvTime) : anchor;
        }
        if (key != _cmdFocusKey)
        {
            if (_cmdFocusKey != int.MinValue)
                _cmdGlide = _cmdGlide + _cmdPan + (_cmdFocusPos - at); // (the picture stays where it is, then slides over)
            _cmdPan = Vec3d.Zero;
            _cmdFocusKey = key;
        }
        _cmdFocusPos = at;
        _cmdGlide = _cmdGlide * Math.Exp(-dt * 6);
        if (_cmdGlide.Length * (_cvSize.Y / _cmdSpan) < 0.3)
            _cmdGlide = Vec3d.Zero;
        _cvFocus = at + _cmdPan + _cmdGlide;

        var (sy, cy) = Math.SinCos(_cmdYaw);
        var (sp, cp) = Math.SinCos(_cmdPitch);
        var forward = new Vec3d(-sy, cy, 0);
        _cvRight = new Vec3d(cy, sy, 0);
        _cvDir = forward * cp - Vec3d.UnitZ * sp;
        _cvUp = forward * sp + Vec3d.UnitZ * cp;
        _cvScale = _cvSize.Y / _cmdSpan;
        _cvCentre = new float2(_cvOrg.X + _cvSize.X * 0.5f, _cvOrg.Y + _cvSize.Y * 0.52f);

        for (var k = 0; k < _cmdCraftCount; k++)
        {
            ref var cr = ref _cmdCraft[k];
            cr.At = CmdP(cr.Pos);
            cr.Px = g.Constructs.Get(cr.Id) is { } c ? (float)(c.Radius * _cvScale) : 0;
        }
    }

    private int CmdFind(ushort id)
    {
        if (id != 0)
            for (var k = 0; k < _cmdCraftCount; k++)
                if (_cmdCraft[k].Id == id)
                    return k;
        return -1;
    }

    /// <summary>The far end of the outermost body's orbit (m from the star).</summary>
    private static double CmdSystemReach(StarSystem sys)
    {
        var reach = 1e7;
        for (var b = 1; b < sys.Count; b++)
            if (sys.Bodies[b].Parent == 0)
                reach = Math.Max(reach, sys.Bodies[b].Orbit.Apoapsis);
        return reach;
    }

    private float2 CmdP(SpacePos p) => CmdV(p - _cvFocus);

    private float2 CmdV(Vec3d d) => new(_cvCentre.X + (float)(Vec3d.Dot(d, _cvRight) * _cvScale), _cvCentre.Y - (float)(Vec3d.Dot(d, _cvUp) * _cvScale));

    /// <summary>A screen point back into space, on the plane through <paramref name="through"/> square to <paramref name="normal"/>. False when the view lies along the plane.</summary>
    private bool CmdUnproject(float2 screen, SpacePos through, Vec3d normal, out Vec3d fromThrough)
    {
        fromThrough = default;
        var x = (screen.X - _cvCentre.X) / _cvScale;
        var y = -(screen.Y - _cvCentre.Y) / _cvScale;
        var origin = (_cvFocus - through) + _cvRight * x + _cvUp * y; // (a point of the ray, from the plane's point)
        var along = Vec3d.Dot(_cvDir, normal);
        if (Math.Abs(along) < 0.12)
            return false;
        fromThrough = origin - _cvDir * (Vec3d.Dot(origin, normal) / along);
        return true;
    }

    /// <summary>Where a screen point's ray meets a ball round <paramref name="centre"/> (the near side), from the centre. False when it misses.</summary>
    private bool CmdOnBall(float2 screen, SpacePos centre, double radius, out Vec3d fromCentre)
    {
        fromCentre = default;
        var x = (screen.X - _cvCentre.X) / _cvScale;
        var y = -(screen.Y - _cvCentre.Y) / _cvScale;
        var o = (_cvFocus - centre) + _cvRight * x + _cvUp * y;
        var across = o - _cvDir * Vec3d.Dot(o, _cvDir);
        var d2 = radius * radius - across.LengthSquared;
        if (d2 < 0)
            return false;
        fromCentre = across - _cvDir * Math.Sqrt(d2);
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Drawing helpers
    // ---------------------------------------------------------------------------------------------------------------

    private bool CmdOn(float2 p, float margin = 60) =>
        p.X > _cvOrg.X - margin && p.Y > _cvOrg.Y - margin && p.X < _cvOrg.X + _cvSize.X + margin && p.Y < _cvOrg.Y + _cvSize.Y + margin;

    private void CmdLine(ImDrawListPtr dl, float2 a, float2 b, Vector3 c, float alpha, float w = 1f)
    {
        if (alpha > 0.004f && float.IsFinite(a.X + a.Y + b.X + b.Y))
            dl.AddLine(a, b, Hud.Colour(c, alpha), w * _scale);
    }

    /// <summary>A line with light round it.</summary>
    private void CmdGlow(ImDrawListPtr dl, float2 a, float2 b, Vector3 c, float alpha, float w = 1.2f)
    {
        CmdLine(dl, a, b, c, alpha * 0.16f, w * 4.5f);
        CmdLine(dl, a, b, c, alpha, w);
    }

    /// <summary>A circle on the screen: whole, or <paramref name="dashes"/> arcs turning with <paramref name="phase"/>.</summary>
    private void CmdRing(ImDrawListPtr dl, float2 centre, float r, Vector3 c, float alpha, float w = 1f, int dashes = 0, float fill = 0.6f, float phase = 0)
    {
        if (alpha <= 0.004f || !(r > 0.5f) || !float.IsFinite(centre.X + centre.Y + r) || r > 60000f)
            return;
        var col = Hud.Colour(c, alpha);
        if (dashes <= 0)
        {
            ImDrawListExtensions.AddCircle(dl, in centre, r, col, r > 300 ? 128 : r > 40 ? 64 : 32, w * _scale);
            return;
        }
        var step = MathF.Tau / dashes;
        var segs = Math.Max(2, (int)(r * step * fill / 6f));
        for (var k = 0; k < dashes; k++)
        {
            var a0 = phase + k * step;
            ImDrawListExtensions.PathClear(dl);
            ImDrawListExtensions.PathArcTo(dl, in centre, r, a0, a0 + step * fill, Math.Min(segs, 48));
            ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, w * _scale);
        }
    }

    /// <summary>Text <paramref name="px"/> tall (before the UI scale). <paramref name="align"/>: 0 from the left, 1 centred, 2 to the right.</summary>
    private float CmdText(ImDrawListPtr dl, float2 at, float px, Vector3 c, float alpha, string text, int align = 0, bool shadow = true)
    {
        if (alpha <= 0.004f || text.Length == 0 || !float.IsFinite(at.X + at.Y))
            return 0;
        var size = px * _scale;
        var w = Hud.TextWidth(text, size);
        if (align == 1)
            at.X -= w * 0.5f;
        else if (align == 2)
            at.X -= w;
        at = new float2(MathF.Round(at.X), MathF.Round(at.Y));
        if (shadow)
            dl.AddText(ImGui.GetFont(), size, at + new float2(1, 1), Hud.Colour(CmdBack, alpha * 0.9f), text);
        dl.AddText(ImGui.GetFont(), size, at, Hud.Colour(c, alpha), text);
        return w;
    }

    /// <summary>The corners of a box, as a bracket (the hologram's frame for anything picked or pointed at).</summary>
    private void CmdBrackets(ImDrawListPtr dl, float2 centre, float half, Vector3 c, float alpha, float w = 1.4f)
    {
        var arm = half * 0.38f;
        for (var k = 0; k < 4; k++)
        {
            var sx = (k & 1) == 0 ? -1f : 1f;
            var sy = (k & 2) == 0 ? -1f : 1f;
            var corner = centre + new float2(sx * half, sy * half);
            CmdGlow(dl, corner, corner - new float2(sx * arm, 0), c, alpha, w);
            CmdGlow(dl, corner, corner - new float2(0, sy * arm), c, alpha, w);
        }
    }

    /// <summary>A panel's plate: dark glass, a bright top edge with a notch, thin sides.</summary>
    private void CmdPlate(ImDrawListPtr dl, float2 p0, float2 p1, float alpha, Vector3 edge)
    {
        dl.AddRectFilled(p0, p1, Hud.Colour(CmdBack, alpha * 0.78f));
        dl.AddRectFilledMultiColor(p0, new float2(p1.X, p0.Y + 46 * _scale), Hud.Colour(edge, alpha * 0.10f), Hud.Colour(edge, alpha * 0.10f), Hud.Colour(edge, 0), Hud.Colour(edge, 0));
        var notch = 14 * _scale;
        CmdGlow(dl, p0, new float2(p1.X - notch, p0.Y), edge, alpha * 0.95f);
        CmdGlow(dl, new float2(p1.X - notch, p0.Y), new float2(p1.X, p0.Y + notch), edge, alpha * 0.95f);
        CmdLine(dl, new float2(p1.X, p0.Y + notch), p1, edge, alpha * 0.35f);
        CmdLine(dl, p0, new float2(p0.X, p1.Y), edge, alpha * 0.35f);
        CmdLine(dl, new float2(p0.X, p1.Y), p1, edge, alpha * 0.22f);
        CmdLine(dl, new float2(p0.X, p0.Y + 4 * _scale), new float2(p0.X, p0.Y + 30 * _scale), edge, alpha, 3f);
    }

    /// <summary>A flat button drawn by hand. True on the click.</summary>
    private bool CmdButton(ImDrawListPtr dl, float2 p0, float2 p1, string label, Vector3 c, float alpha, bool on = false, float px = 13)
    {
        var m = ImGui.GetMousePos();
        var hover = !_cmdOrderOpen && m.X >= p0.X && m.X < p1.X && m.Y >= p0.Y && m.Y < p1.Y;
        dl.AddRectFilled(p0, p1, Hud.Colour(c, alpha * (hover ? 0.30f : on ? 0.20f : 0.07f)));
        dl.AddRect(p0, p1, Hud.Colour(c, alpha * (hover || on ? 0.95f : 0.45f)), 0, 0, _scale);
        CmdText(dl, new float2((p0.X + p1.X) * 0.5f, (p0.Y + p1.Y) * 0.5f - px * _scale * 0.5f), px, hover || on ? CmdWhite : c, alpha, label, 1, false);
        if (hover)
            _cmdOverPanel = true;
        return hover && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    private bool _cmdOverPanel;

    /// <summary>Marks a panel's rectangle: the map under it takes no clicks this frame.</summary>
    private void CmdPanelArea(float2 p0, float2 p1)
    {
        var m = ImGui.GetMousePos();
        if (m.X >= p0.X && m.X < p1.X && m.Y >= p0.Y && m.Y < p1.Y)
            _cmdOverPanel = true;
    }

    /// <summary>A distance in words: metres, kilometres, thousands of them, AU.</summary>
    public static string CmdDistance(double m)
    {
        var a = Math.Abs(m);
        return a < 995 ? FormattableString.Invariant($"{m:0} m")
            : a < 99_950 ? FormattableString.Invariant($"{m / 1000:0.0} km")
            : a < 1e9 ? FormattableString.Invariant($"{m / 1000:#,0} km")
            : a < 1.5e10 ? FormattableString.Invariant($"{m / 1e9:0.00} Gm")
            : FormattableString.Invariant($"{m / 1.495978707e11:0.00} AU");
    }

    /// <summary>A time in words.</summary>
    public static string CmdTime(double s) =>
        !double.IsFinite(s) || s < 0 ? "-"
        : s < 90 ? FormattableString.Invariant($"{s:0} s")
        : s < 5400 ? FormattableString.Invariant($"{(int)(s / 60)}m {(int)(s % 60):00}s")
        : s < 172_800 ? FormattableString.Invariant($"{(int)(s / 3600)}h {(int)(s % 3600 / 60):00}m")
        : FormattableString.Invariant($"{s / 86400:0.0} d");

    // ---------------------------------------------------------------------------------------------------------------
    // The backdrop and the grid
    // ---------------------------------------------------------------------------------------------------------------

    private void CmdBackdrop(ImDrawListPtr dl, float fade, double now)
    {
        var p0 = _cvOrg;
        var p1 = _cvOrg + _cvSize;
        dl.AddRectFilled(p0, p1, Hud.Colour(CmdBack, 0.86f * fade));
        // Light from the middle, dark at the edges: a projector's cone.
        var mid = new float2(_cvCentre.X, _cvCentre.Y);
        var glow = Hud.Colour(new Vector3(0.05f, 0.22f, 0.32f), 0.20f * fade);
        var none = Hud.Colour(new Vector3(0.05f, 0.22f, 0.32f), 0);
        dl.AddRectFilledMultiColor(p0, new float2(mid.X, mid.Y), none, none, glow, none);
        dl.AddRectFilledMultiColor(new float2(mid.X, p0.Y), new float2(p1.X, mid.Y), none, none, none, glow);
        dl.AddRectFilledMultiColor(new float2(p0.X, mid.Y), new float2(mid.X, p1.Y), none, glow, none, none);
        dl.AddRectFilledMultiColor(mid, p1, glow, none, none, none);
        // Scan lines, and one bright one sweeping down.
        var line = Hud.Colour(CmdCyan, 0.022f * fade);
        var step = MathF.Max(3f, 4f * _scale);
        for (var y = p0.Y; y < p1.Y; y += step)
            dl.AddLine(new float2(p0.X, y), new float2(p1.X, y), line, 1f);
        var sweep = p0.Y + (float)(now * 0.11 % 1.0) * _cvSize.Y;
        dl.AddRectFilledMultiColor(new float2(p0.X, sweep - 60 * _scale), new float2(p1.X, sweep), none, none, Hud.Colour(CmdCyan, 0.05f * fade), Hud.Colour(CmdCyan, 0.05f * fade));
    }

    /// <summary>Range rings on the ecliptic plane through the focus, at round distances, with their names; spokes every 30 degrees.</summary>
    private void CmdGrid(ImDrawListPtr dl, SpaceSession s, float fade)
    {
        // The ring spacing: 1, 2 or 5 times a power of ten, about a sixth of the view.
        var want = _cmdSpan / 5.5;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(want)));
        var lead = want / pow;
        var ring = pow * (lead < 1.5 ? 1 : lead < 3.5 ? 2 : lead < 7.5 ? 5 : 10);
        var centre = _cvFocus - (_cmdPan + _cmdGlide); // (the rings stay on what is focused while the view pans)
        var n = _cmdPts.Length - 1;
        for (var k = 1; k <= 6; k++)
        {
            var r = ring * k;
            for (var i = 0; i <= n; i++)
            {
                var (sn, cs) = Math.SinCos(i * (Math.Tau / n));
                _cmdPts[i] = CmdP(centre + new Vec3d(cs * r, sn * r, 0));
            }
            var a = (k % 2 == 0 ? 0.20f : 0.11f) * fade;
            ImDrawListExtensions.AddPolyline(dl, _cmdPts.AsSpan(0, n + 1), Hud.Colour(CmdDim, a), ImDrawFlags.None, _scale);
            ref var label = ref _cmdGridLabels[k];
            if (label.Stale((long)(r * 10)))
                label.Text = CmdDistance(r);
            CmdText(dl, CmdP(centre + new Vec3d(r, 0, 0)) + new float2(5 * _scale, -14 * _scale), 11, CmdDim, 0.75f * fade, label.Text!);
        }
        for (var k = 0; k < 12; k++)
        {
            var (sn, cs) = Math.SinCos(k * (Math.Tau / 12));
            var dir = new Vec3d(cs, sn, 0);
            CmdLine(dl, CmdP(centre + dir * (ring * 0.25)), CmdP(centre + dir * (ring * 6.25)), CmdDim, (k % 3 == 0 ? 0.13f : 0.06f) * fade);
        }
    }

    private readonly CmdLabel[] _cmdGridLabels = new CmdLabel[8];

    // ---------------------------------------------------------------------------------------------------------------
    // Bodies
    // ---------------------------------------------------------------------------------------------------------------

    private void CmdBodies(ImDrawListPtr dl, Game g, SpaceSession s, float fade, double now)
    {
        var sys = s.System;
        // Orbits first (under everything).
        for (var b = 1; b < sys.Count; b++)
        {
            var body = sys.Bodies[b];
            var parent = sys.PositionOf(body.Parent, _cvTime);
            var px = body.Orbit.A * _cvScale;
            if (px < 10)
                continue;
            var a = (float)Math.Clamp((px - 10) / 30, 0, 1) * fade;
            CmdOrbit(dl, parent, body.Orbit, body.Orbit.MeanAnomalyAt(_cvTime), b == _cmdSelBody ? CmdCyan : CmdDim, (b == _cmdSelBody ? 0.75f : 0.42f) * a, 1.1f, false);
        }
        for (var b = 0; b < sys.Count; b++)
        {
            var body = sys.Bodies[b];
            var pos = sys.PositionOf(b, _cvTime);
            var at = CmdP(pos);
            var r = (float)(body.Radius * _cvScale);
            // A moon hides in its planet's dot until its orbit opens up.
            var open = b == 0 || body.Parent == 0 ? 1f : (float)Math.Clamp((body.Orbit.A * _cvScale - 14) / 20, 0, 1);
            if (open <= 0 || !CmdOn(at, r + 80))
                continue;
            var tint = body.Color is { } col ? Vector3.Lerp(col, CmdCyan, 0.45f) : CmdCyan;
            var picked = b == _cmdSelBody;
            if (r >= 5)
                CmdGlobe(dl, g, s, b, pos, at, r, tint, fade * open, now);
            else
            {
                var dot = b == 0 ? 5f : body.Parent == 0 ? 3.5f : 2.5f;
                ImDrawListExtensions.AddCircleFilled(dl, in at, dot * _scale, Hud.Colour(tint, 0.95f * fade * open), 16);
                CmdRing(dl, at, (dot + 4) * _scale, tint, 0.5f * fade * open);
            }
            if (picked)
                CmdRing(dl, at, MathF.Max(r, 6 * _scale) + 9 * _scale, CmdWhite, 0.9f * fade, 1.5f, 3, 0.7f, (float)(now * 0.8));
            var tag = MathF.Max(r, 5 * _scale) * 0.72f + 6 * _scale;
            var name = _cmdBodyNames.Length > b ? _cmdBodyNames[b] : body.Name;
            if (b == 0 && r < _cvSize.Y * 0.8f)
            {
                // (the star's name hangs down and to the left: its inner worlds' names stand up and to the right of it)
                CmdLine(dl, at + new float2(-tag * 0.72f, tag * 0.72f), at + new float2(-tag - 16 * _scale, tag + 6 * _scale), tint, 0.6f * fade);
                CmdText(dl, at + new float2(-tag - 20 * _scale, tag), 15, picked ? CmdWhite : tint, fade, name, 2);
            }
            else if (r < _cvSize.Y * 0.8f)
            {
                CmdLine(dl, at + new float2(tag * 0.72f, -tag * 0.72f), at + new float2(tag + 16 * _scale, -tag - 6 * _scale), tint, 0.6f * fade * open);
                CmdText(dl, at + new float2(tag + 20 * _scale, -tag - 15 * _scale), body.Parent == 0 ? 15 : 12.5f, picked ? CmdWhite : tint, fade * open, name);
            }
        }
    }

    private string[] _cmdBodyNames = [];
    private StarSystem? _cmdNamesFor;

    private void CmdNames(StarSystem sys)
    {
        if (ReferenceEquals(_cmdNamesFor, sys))
            return;
        _cmdNamesFor = sys;
        _cmdBodyNames = new string[sys.Count];
        for (var b = 0; b < sys.Count; b++)
            _cmdBodyNames[b] = sys.Bodies[b].Name.ToUpperInvariant();
    }

    /// <summary>
    /// A conic round <paramref name="centre"/>: the whole ellipse when it fits the view, else the stretch near
    /// <paramref name="meanNow"/> (where the thing on it is), so a planet's orbit stays a smooth curve when it is a
    /// thousand screens across. <paramref name="dashed"/>: every other piece.
    /// </summary>
    private void CmdOrbit(ImDrawListPtr dl, SpacePos centre, in KeplerOrbit o, double meanNow, Vector3 c, float alpha, float w, bool dashed)
    {
        if (alpha <= 0.004f || !(o.A > 0) || !(o.E < 1))
            return;
        var px = o.A * _cvScale;
        var n = _cmdPts.Length - 1;
        var e0 = 0.0;
        var sweep = Math.Tau;
        if (px > 6 * _cvSize.Y)
        {
            // Only the arc on the screen: a few screens either side of where it is now.
            sweep = Math.Min(Math.Tau, 6 * _cvSize.Y / px);
            e0 = KeplerOrbit.EccentricAnomaly(meanNow, o.E) - sweep * 0.5;
        }
        var b = o.A * Math.Sqrt(1 - o.E * o.E);
        for (var i = 0; i <= n; i++)
        {
            var (sn, cs) = Math.SinCos(e0 + sweep * i / n);
            _cmdPts[i] = CmdP(centre + o.P * (o.A * (cs - o.E)) + o.Q * (b * sn));
        }
        var col = Hud.Colour(c, alpha);
        if (!dashed)
        {
            ImDrawListExtensions.AddPolyline(dl, _cmdPts.AsSpan(0, n + 1), col, ImDrawFlags.None, w * _scale);
            return;
        }
        for (var i = 0; i + 2 <= n; i += 4)
            ImDrawListExtensions.AddPolyline(dl, _cmdPts.AsSpan(i, 3), col, ImDrawFlags.None, w * _scale);
    }

    /// <summary>A body big enough to be a ball: dark glass, a lit limb, lines of latitude and longitude turning with its ground, its sky's edge.</summary>
    private void CmdGlobe(ImDrawListPtr dl, Game g, SpaceSession s, int b, SpacePos pos, float2 at, float r, Vector3 tint, float fade, double now)
    {
        var sys = s.System;
        var body = sys.Bodies[b];
        var huge = r > _cvSize.Y * 6;
        if (!huge)
        {
            ImDrawListExtensions.AddCircleFilled(dl, in at, r, Hud.Colour(Vector3.Lerp(CmdBack, tint, 0.10f), 0.92f * fade), r > 200 ? 128 : 64);
            CmdRing(dl, at, r, tint, 0.16f * fade, 5f);
            CmdRing(dl, at, r, tint, 0.95f * fade, 1.4f);
        }
        if (r >= 26 && !huge)
        {
            // The graticule, front side only, in the body's own turning frame.
            var spin = sys.SpinRotation(b, _cvTime);
            var x = spin.Rotate(Vec3d.UnitX);
            var y = spin.Rotate(Vec3d.UnitY);
            var z = spin.Rotate(Vec3d.UnitZ);
            var segs = r > 220 ? 72 : 36;
            var rr = body.Radius;
            var line = Hud.Colour(tint, 0.26f * fade);
            for (var lat = -60; lat <= 60; lat += 30)
            {
                var (sl, cl) = Math.SinCos(lat * Math.PI / 180);
                CmdGlobeArc(dl, pos, x * (rr * cl), y * (rr * cl), z * (rr * sl), segs, lat == 0 ? Hud.Colour(tint, 0.5f * fade) : line);
            }
            for (var lon = 0; lon < 180; lon += 30)
            {
                var (sn, cs) = Math.SinCos(lon * Math.PI / 180);
                var east = x * cs + y * sn;
                CmdGlobeArc(dl, pos, east * rr, z * rr, Vec3d.Zero, segs, lon == 0 ? Hud.Colour(tint, 0.5f * fade) : line);
            }
            // The poles' axis.
            CmdLine(dl, CmdP(pos + z * (rr * 1.0)), CmdP(pos + z * (rr * 1.18)), tint, 0.7f * fade);
            CmdLine(dl, CmdP(pos - z * (rr * 1.0)), CmdP(pos - z * (rr * 1.18)), tint, 0.35f * fade);
        }
        // The sky's edge: inside it a ship flies on the autopilot, outside it is in space.
        if (body.Map != null && r >= 18)
        {
            var edge = (float)((Game.NavMinOrbit(sys, b) - Game.NavEdgeMargin) * _cvScale);
            CmdRing(dl, at, edge, CmdCyan, 0.42f * fade, 1f, Math.Clamp((int)(edge / 9f), 24, 120), 0.5f, (float)(now * 0.03));
            if (edge > 70 * _scale && edge < _cvSize.Y)
                CmdText(dl, at + new float2(0, -edge - 16 * _scale), 10.5f, CmdCyan, 0.6f * fade, "SKY EDGE", 1);
        }
    }

    /// <summary>A circle on a ball (centre + a cos t + b sin t + off), only where it faces the viewer.</summary>
    private void CmdGlobeArc(ImDrawListPtr dl, SpacePos centre, Vec3d a, Vec3d b, Vec3d off, int segs, ImColor8 col)
    {
        var have = false;
        var last = default(float2);
        for (var i = 0; i <= segs; i++)
        {
            var (sn, cs) = Math.SinCos(i * (Math.Tau / segs));
            var p = a * cs + b * sn + off;
            var front = Vec3d.Dot(p, _cvDir) < 0;
            var sp = CmdV((centre - _cvFocus) + p);
            if (front && have)
                dl.AddLine(last, sp, col, _scale);
            have = front;
            last = sp;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The top bar, the hints
    // ---------------------------------------------------------------------------------------------------------------

    private CmdLabel _cmdClock, _cmdScale;
    private static readonly int[] CmdSpeeds = [0, 1, 10, 100, 1000, 10_000, 100_000];
    private static readonly string[] CmdSpeedLabels = ["II", "1x", "10x", "100x", "1K", "10K", "100K"];
    private static readonly string[] CmdHeldText = ["", "   (A SHIP IS BURNING)", "   (SLOWED FOR A BURN)", "   (SHIPS' LIMIT)", "   (SETTINGS' LIMIT)"];

    /// <summary>Why the time runs slower than the host asked (an index into <see cref="CmdHeldText"/>; 0: it doesn't, or this is a client).</summary>
    private static int CmdSpeedHeld(Game g, SpaceSession s)
    {
        if (s.Flight is not { } f || f.RequestedSpeed <= f.Clock.Speed || f.Clock.Speed == 0)
            return 0;
        if (f.AnyoneBusy)
            return 1;
        if (g.NavSpeedCap <= f.Clock.Speed)
            return 2;
        return g.TimeSpeedMost <= f.Clock.Speed ? 4 : 3;
    }
    private static readonly string[] CmdPresets = ["SHIP", "WORLD", "MOONS", "SYSTEM"];

    private void CmdChrome(ImDrawListPtr dl, Game g, SpaceSession s, float fade, double now, string keyName)
    {
        _cmdOverPanel = false;
        var sys = s.System;
        CmdNames(sys);
        var pad = 22 * _scale;
        var p0 = _cvOrg + new float2(pad, pad);
        // Title, top left.
        CmdText(dl, p0, 11, CmdDim, fade, "NAV COMPUTER  //  COMMAND VIEW");
        var title = _cmdSysName ??= sys.Name.ToUpperInvariant();
        if (!ReferenceEquals(_cmdSysFor, sys))
        {
            _cmdSysFor = sys;
            title = _cmdSysName = sys.Name.ToUpperInvariant();
        }
        CmdText(dl, p0 + new float2(0, 15 * _scale), 26, CmdWhite, fade, title);
        var t = _cvTime.Seconds;
        var held = CmdSpeedHeld(g, s);
        if (_cmdClock.Stale(((long)t * 1000 + s.Speed) * 8 + held))
            _cmdClock.Text = FormattableString.Invariant($"DAY {(int)(t / 86400) + 1}  {(int)(t % 86400 / 3600):00}:{(int)(t % 3600 / 60):00}:{(int)(t % 60):00}   TIME {(s.Speed == 0 ? "PAUSED" : $"{s.Speed}x")}{CmdHeldText[held]}");
        CmdText(dl, p0 + new float2(0, 48 * _scale), 12.5f, s.Speed == 1 ? CmdCyan : CmdAmber, fade, _cmdClock.Text!);
        CmdGlow(dl, p0 + new float2(0, 68 * _scale), p0 + new float2(250 * _scale, 68 * _scale), CmdCyan, 0.8f * fade);
        CmdLine(dl, p0 + new float2(250 * _scale, 68 * _scale), p0 + new float2(262 * _scale, 80 * _scale), CmdCyan, 0.8f * fade);

        // Time speed, under the clock (the host's: Karl, 2026-11-16: ships coast between planets in real time, and the
        // command view speeds time up; a burn holds it at 1x and an ordered ship slows it before its next burn).
        if (g.IsServer && s.Flight is { } hf)
        {
            var tw = 34 * _scale;
            for (var k = 0; k < CmdSpeeds.Length; k++)
            {
                var q0 = p0 + new float2(k * (tw + 3 * _scale), 76 * _scale);
                if (CmdButton(dl, q0, q0 + new float2(tw, 19 * _scale), CmdSpeedLabels[k], k == 0 ? CmdAmber : CmdCyan, fade, hf.RequestedSpeed == CmdSpeeds[k], 11))
                    g.RequestTimeSpeed(Math.Min(CmdSpeeds[k], Math.Max(1, g.TimeSpeedMost)));
            }
        }

        // Zoom presets, top middle.
        var bw = 92 * _scale;
        var bh = 26 * _scale;
        var bx = _cvCentre.X - (bw * 4 + 18 * _scale) * 0.5f;
        var by = _cvOrg.Y + pad;
        var level = CmdLevel(sys);
        for (var k = 0; k < 4; k++)
        {
            var q0 = new float2(bx + k * (bw + 6 * _scale), by);
            if (CmdButton(dl, q0, q0 + new float2(bw, bh), CmdPresets[k], CmdCyan, fade, level == k))
                CmdPreset(g, sys, k);
        }
        if (_cmdScale.Stale((long)(_cmdSpan * 100)))
            _cmdScale.Text = "VIEW  " + CmdDistance(_cmdSpan) + "  ACROSS" + (_cmdFocusAuto ? "" : "   //   FOCUS PINNED");
        CmdText(dl, new float2(_cvCentre.X, by + bh + 7 * _scale), 11, CmdDim, fade, _cmdScale.Text!, 1);

        // Hints, bottom middle.
        var hint = _cmdHint ??= $"WHEEL zoom    LEFT DRAG pan    RIGHT DRAG turn    CLICK pick    DOUBLE CLICK centre    RIGHT CLICK order    {keyName.ToUpperInvariant()} / ESC close";
        CmdText(dl, new float2(_cvCentre.X, _cvOrg.Y + _cvSize.Y - pad - 12 * _scale), 11, CmdDim, 0.9f * fade, hint, 1);

        // A scale bar, bottom left of the middle.
        var want = _cmdSpan / 6;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(want)));
        var lead = want / pow;
        var bar = pow * (lead < 1.5 ? 1 : lead < 3.5 ? 2 : lead < 7.5 ? 5 : 10);
        var len = (float)(bar * _cvScale);
        var s0 = new float2(_cvCentre.X - len * 0.5f, _cvOrg.Y + _cvSize.Y - pad - 34 * _scale);
        CmdGlow(dl, s0, s0 + new float2(len, 0), CmdCyan, 0.9f * fade);
        CmdLine(dl, s0 + new float2(0, -5 * _scale), s0 + new float2(0, 5 * _scale), CmdCyan, fade);
        CmdLine(dl, s0 + new float2(len, -5 * _scale), s0 + new float2(len, 5 * _scale), CmdCyan, fade);
        ref var bl = ref _cmdGridLabels[0];
        if (bl.Stale((long)(bar * 10)))
            bl.Text = CmdDistance(bar);
        CmdText(dl, s0 + new float2(len * 0.5f, -18 * _scale), 11.5f, CmdCyan, fade, bl.Text!, 1);

        // The screen's corners.
        var c0 = _cvOrg + new float2(10 * _scale, 10 * _scale);
        var c1 = _cvOrg + _cvSize - new float2(10 * _scale, 10 * _scale);
        var arm = 26 * _scale;
        CmdLine(dl, c0, c0 + new float2(arm, 0), CmdCyan, 0.7f * fade, 1.5f);
        CmdLine(dl, c0, c0 + new float2(0, arm), CmdCyan, 0.7f * fade, 1.5f);
        CmdLine(dl, c1, c1 - new float2(arm, 0), CmdCyan, 0.7f * fade, 1.5f);
        CmdLine(dl, c1, c1 - new float2(0, arm), CmdCyan, 0.7f * fade, 1.5f);
        CmdLine(dl, new float2(c1.X, c0.Y), new float2(c1.X - arm, c0.Y), CmdCyan, 0.7f * fade, 1.5f);
        CmdLine(dl, new float2(c1.X, c0.Y), new float2(c1.X, c0.Y + arm), CmdCyan, 0.7f * fade, 1.5f);
        CmdLine(dl, new float2(c0.X, c1.Y), new float2(c0.X + arm, c1.Y), CmdCyan, 0.7f * fade, 1.5f);
        CmdLine(dl, new float2(c0.X, c1.Y), new float2(c0.X, c1.Y - arm), CmdCyan, 0.7f * fade, 1.5f);
    }

    private string? _cmdHint, _cmdSysName;
    private StarSystem? _cmdSysFor;

    /// <summary>Which preset the view is nearest: 0 a ship, 1 its world, 2 the world's moons, 3 the system.</summary>
    private int CmdLevel(StarSystem sys)
    {
        var k = CmdFind(_cmdSel);
        var prim = k >= 0 ? _cmdCraft[k].Primary : NavGuidance.PrimaryAt(sys, _cvFocus, _cvTime);
        if (prim == 0)
            return _cmdSpan < 1e6 ? 0 : 3;
        var planet = prim;
        while (sys.Bodies[planet].Parent > 0)
            planet = sys.Bodies[planet].Parent;
        return _cmdSpan < 3 * sys.Bodies[prim].Radius ? 0 : _cmdSpan < 14 * sys.Bodies[prim].Radius ? 1 : _cmdSpan < 0.05 * sys.Bodies[planet].Orbit.A ? 2 : 3;
    }

    private void CmdPreset(Game g, StarSystem sys, int level)
    {
        var k = CmdFind(_cmdSel);
        var prim = k >= 0 ? _cmdCraft[k].Primary : NavGuidance.PrimaryAt(sys, _cvFocus, _cvTime);
        var planet = prim;
        while (planet > 0 && sys.Bodies[planet].Parent > 0)
            planet = sys.Bodies[planet].Parent;
        _cmdFocusAuto = true;
        _cmdFocusBody = -1;
        _cmdFocusShip = 0;
        _cmdPan = Vec3d.Zero;
        switch (level)
        {
            case 0:
                _cmdSpanWant = k >= 0 && g.Constructs.Get(_cmdSel) is { } c ? Math.Max(40, c.Radius * 5.5) : 400;
                break;
            case 1:
                _cmdSpanWant = prim > 0 ? Math.Max(5.2 * sys.Bodies[prim].Radius, 3.4 * Game.NavMinOrbit(sys, prim)) : 0.02 * CmdSystemReach(sys);
                break;
            case 2:
            {
                var far = 0.0;
                for (var b = 1; b < sys.Count; b++)
                    if (sys.Bodies[b].Parent == planet && planet > 0)
                        far = Math.Max(far, sys.Bodies[b].Orbit.Apoapsis);
                _cmdSpanWant = far > 0 ? 2.9 * far : planet > 0 ? 30 * sys.Bodies[planet].Radius : 0.02 * CmdSystemReach(sys);
                if (planet > 0 && planet != prim)
                {
                    _cmdFocusAuto = false;
                    _cmdFocusBody = planet;
                }
                break;
            }
            default:
                _cmdSpanWant = 2.5 * CmdSystemReach(sys);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------------------------------------

    private void CmdInput(Game g, SpaceSession s)
    {
        var io = ImGui.GetIO();
        var m = io.MousePos;
        var free = !_cmdOverPanel && !_cmdOrderOpen && ImGui.IsWindowHovered();
        if (free && io.MouseWheel != 0)
            _cmdSpanWant *= Math.Pow(1.32, -io.MouseWheel);

        // Left: a click picks, a drag pans.
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && free)
        {
            _cmdHeldL = true;
            _cmdDragL = false;
            _cmdDownL = m;
        }
        if (_cmdHeldL)
        {
            if (!_cmdDragL && (m - _cmdDownL).Length() > 5 * _scale)
                _cmdDragL = true;
            if (_cmdDragL)
                _cmdPan = _cmdPan - _cvRight * (io.MouseDelta.X / _cvScale) + _cvUp * (io.MouseDelta.Y / _cvScale);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _cmdHeldL = false;
                if (!_cmdDragL)
                    CmdPick(g, s, m, ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left));
            }
        }
        if (free && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            CmdPick(g, s, m, true);

        // Right: a click orders, a drag turns the view.
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && free)
        {
            _cmdHeldR = true;
            _cmdDragR = false;
            _cmdDownR = m;
        }
        if (_cmdHeldR)
        {
            if (!_cmdDragR && (m - _cmdDownR).Length() > 5 * _scale)
                _cmdDragR = true;
            if (_cmdDragR)
            {
                _cmdYaw -= io.MouseDelta.X * 0.006f;
                _cmdPitch = Math.Clamp(_cmdPitch + io.MouseDelta.Y * 0.005f, 0.18f, MathF.PI / 2);
            }
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                _cmdHeldR = false;
                if (!_cmdDragR)
                    CmdOrderAt(g, s, m);
            }
        }
    }

    /// <summary>What is under a screen point: a craft (its index), else a body. -1: nothing.</summary>
    private int CmdHit(SpaceSession s, float2 m, out int body)
    {
        body = -1;
        var best = -1;
        var bestD = float.MaxValue;
        for (var k = 0; k < _cmdCraftCount; k++)
        {
            var d = (m - _cmdCraft[k].At).Length() - MathF.Max(14 * _scale, MathF.Min(_cmdCraft[k].Px, 90 * _scale));
            if (d < 0 && d < bestD)
            {
                bestD = d;
                best = k;
            }
        }
        if (best >= 0)
            return best;
        var sys = s.System;
        var bodyD = float.MaxValue;
        for (var b = 0; b < sys.Count; b++)
        {
            if (b > 0 && sys.Bodies[b].Parent > 0 && sys.Bodies[b].Orbit.A * _cvScale < 14)
                continue; // (a moon still hidden in its planet's dot)
            var at = CmdP(sys.PositionOf(b, _cvTime));
            var r = MathF.Max((float)(sys.Bodies[b].Radius * _cvScale), 13 * _scale);
            var d = (m - at).Length() - r;
            if (d < 0 && r < bodyD)
            {
                bodyD = r; // (the smaller thing wins: a moon in front of its planet)
                body = b;
            }
        }
        return -1;
    }

    private void CmdPick(Game g, SpaceSession s, float2 m, bool centre)
    {
        var k = CmdHit(s, m, out var body);
        if (k >= 0)
        {
            _cmdSel = _cmdCraft[k].Id;
            _cmdSelBody = -1;
            if (centre)
            {
                _cmdFocusAuto = false;
                _cmdFocusShip = _cmdSel;
                _cmdFocusBody = -1;
                _cmdPan = Vec3d.Zero;
            }
            return;
        }
        if (body >= 0)
        {
            _cmdSelBody = body;
            if (centre)
            {
                _cmdFocusAuto = false;
                _cmdFocusBody = body;
                _cmdFocusShip = 0;
                _cmdPan = Vec3d.Zero;
            }
            return;
        }
        if (!centre)
            _cmdSelBody = -1;
    }
}
