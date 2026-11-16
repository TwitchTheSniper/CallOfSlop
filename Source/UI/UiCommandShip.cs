using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view's craft (UiCommand.cs): each one's mark on the map, the conic it is on, its order; the picked one's
/// hologram when the view is close (its hull as stacked outlines, cut from its parts' boxes) and its card: what a
/// commander wants to know of a ship without walking its decks. The card's numbers are read four times a second.
/// </summary>
public sealed partial class Ui
{
    // ---------------------------------------------------------------------------------------------------------------
    // Marks, paths, orders
    // ---------------------------------------------------------------------------------------------------------------

    private void CmdCrafts(ImDrawListPtr dl, Game g, SpaceSession s, float fade, double now)
    {
        var sys = s.System;
        var m = ImGui.GetMousePos();
        for (var k = 0; k < _cmdCraftCount; k++)
        {
            ref var cr = ref _cmdCraft[k];
            if (g.Constructs.Get(cr.Id) is not { } c)
                continue;
            var picked = cr.Id == _cmdSel;
            var order = g.NavOf(cr.Id);
            sys.StateOf(cr.Primary, _cvTime, out var bp, out var bv);
            var r = cr.Pos - bp;
            var v = cr.Vel - bv;
            var body = sys.Bodies[cr.Primary];

            // The path it is on, when it is on one: up in space, or high and fast.
            var free = c.SpaceShip != 0 || r.Length - body.Radius > 1500 && v.Length > 40;
            if (free && cr.Primary > 0 && v.LengthSquared / 2 - body.Mu / r.Length < 0 && Vec3d.Cross(r, v).Length > 1)
            {
                var o = KeplerOrbit.FromState(body.Mu, r, v, _cvTime);
                var sinks = o.Periapsis < body.Radius;
                CmdOrbit(dl, bp, o, o.M0, sinks ? CmdRed : picked ? CmdWhite : CmdCyan, (picked ? 0.85f : 0.4f) * fade, picked ? 1.5f : 1f, sinks);
                if (picked && o.A * _cvScale > 30)
                {
                    var low = CmdP(bp + o.P * o.Periapsis);
                    var high = CmdP(bp - o.P * o.Apoapsis);
                    ref var lt = ref _cmdApsis[0];
                    if (lt.Stale((long)(o.Periapsis - body.Radius)))
                        lt.Text = "LOW " + CmdDistance(o.Periapsis - body.Radius);
                    ref var ht = ref _cmdApsis[1];
                    if (ht.Stale((long)(o.Apoapsis - body.Radius)))
                        ht.Text = "HIGH " + CmdDistance(o.Apoapsis - body.Radius);
                    if (!sinks)
                    {
                        CmdRing(dl, low, 4 * _scale, CmdWhite, fade);
                        CmdText(dl, low + new float2(8 * _scale, 4 * _scale), 10.5f, CmdWhite, 0.85f * fade, lt.Text!);
                    }
                    if (o.E > 0.004)
                    {
                        CmdRing(dl, high, 4 * _scale, CmdWhite, fade);
                        CmdText(dl, high + new float2(8 * _scale, 4 * _scale), 10.5f, CmdWhite, 0.85f * fade, ht.Text!);
                    }
                }
            }

            if (order != null)
                CmdOrderMark(dl, g, s, c, order, cr, r, v, fade, now);

            if (!CmdOn(cr.At, cr.Px + 40))
                continue;
            var colour = picked ? CmdWhite : order != null ? CmdAmber : CmdCyan;
            var hover = !_cmdOverPanel && (m - cr.At).Length() < MathF.Max(14 * _scale, MathF.Min(cr.Px, 90 * _scale));
            var half = MathF.Max(13 * _scale, cr.Px * 1.08f);
            if (cr.Px > 20 * _scale)
                CmdHoloDraw(dl, g, c, cr, picked ? CmdCyan : CmdDim, fade * (picked ? 1f : 0.8f), now);
            else
            {
                // A dart along the way it goes (seen from its world); a diamond when it sits still.
                var way = new float2((float)Vec3d.Dot(v, _cvRight), -(float)Vec3d.Dot(v, _cvUp));
                var relative = c.SpaceShip != 0 ? v.Length : c.Vel.Length();
                var size = 7 * _scale;
                if (relative > 2 && way.Length() > 1e-3f)
                {
                    way /= way.Length();
                    var side = new float2(-way.Y, way.X);
                    var tip = cr.At + way * (size * 1.5f);
                    var a = cr.At - way * size + side * (size * 0.8f);
                    var b = cr.At - way * size - side * (size * 0.8f);
                    ImDrawListExtensions.AddTriangleFilled(dl, in tip, in a, in b, Hud.Colour(colour, 0.25f * fade));
                    CmdGlow(dl, tip, a, colour, fade);
                    CmdGlow(dl, tip, b, colour, fade);
                    CmdLine(dl, a, cr.At - way * (size * 0.4f), colour, fade);
                    CmdLine(dl, b, cr.At - way * (size * 0.4f), colour, fade);
                }
                else
                {
                    CmdGlow(dl, cr.At + new float2(0, -size), cr.At + new float2(size, 0), colour, fade);
                    CmdGlow(dl, cr.At + new float2(size, 0), cr.At + new float2(0, size), colour, fade);
                    CmdGlow(dl, cr.At + new float2(0, size), cr.At + new float2(-size, 0), colour, fade);
                    CmdGlow(dl, cr.At + new float2(-size, 0), cr.At + new float2(0, -size), colour, fade);
                }
            }
            if (picked)
            {
                CmdBrackets(dl, cr.At, half + 6 * _scale, CmdWhite, fade);
                CmdRing(dl, cr.At, half * 1.5f + 10 * _scale, CmdCyan, 0.55f * fade, 1f, 4, 0.35f, (float)(now * 0.6));
                CmdRing(dl, cr.At, half * 1.5f + 15 * _scale, CmdCyan, 0.25f * fade, 1f, 24, 0.3f, (float)(-now * 0.2));
            }
            else if (hover)
                CmdBrackets(dl, cr.At, half + 4 * _scale, CmdCyan, 0.8f * fade, 1f);
            // Its name on a leader line.
            var off = half + 8 * _scale;
            CmdLine(dl, cr.At + new float2(off * 0.7f, off * 0.7f), cr.At + new float2(off + 14 * _scale, off + 10 * _scale), colour, 0.6f * fade);
            CmdLine(dl, cr.At + new float2(off + 14 * _scale, off + 10 * _scale), cr.At + new float2(off + 30 * _scale, off + 10 * _scale), colour, 0.6f * fade);
            CmdText(dl, cr.At + new float2(off + 34 * _scale, off + 3 * _scale), picked ? 13.5f : 12, colour, fade, CmdName(c));
        }

        // The player, when they aren't aboard something on the map.
        if (g.Local is { Alive: true } me)
        {
            ref var h = ref g.SandboxHands.Hand(me.Id);
            if (!h.Seated)
            {
                var at = CmdP(_cvPose.ToEcliptic(Vec3d.From(me.Position)));
                if (CmdOn(at))
                {
                    ImDrawListExtensions.AddCircleFilled(dl, in at, 2.5f * _scale, Hud.Colour(CmdGreen, fade), 8);
                    CmdRing(dl, at, 6 * _scale, CmdGreen, 0.7f * fade);
                    CmdText(dl, at + new float2(9 * _scale, -16 * _scale), 10.5f, CmdGreen, 0.9f * fade, "YOU");
                }
            }
        }
    }

    private readonly CmdLabel[] _cmdApsis = new CmdLabel[2];

    /// <summary>Where an order sends its ship: the circle it is to fly, or the point, and a line there.</summary>
    private void CmdOrderMark(ImDrawListPtr dl, Game g, SpaceSession s, Construct c, NavOrder o, in CmdCraft cr, Vec3d r, Vec3d v, float fade, double now)
    {
        var sys = s.System;
        if (o.Body >= sys.Count)
            return;
        var bp = sys.PositionOf(o.Body, _cvTime);
        var n = _cmdPts.Length - 1;
        if (o.Kind == NavKind.Orbit)
        {
            // The circle, in the plane the ship will fly it in (its own when it is round that body already).
            var from = cr.Pos - bp;
            var up = from.Length > 1 ? from.Normalized() : Vec3d.UnitX;
            var normal = o.Body == cr.Primary ? NavGuidance.OrbitNormal(up, r, v, sys.Bodies[o.Body].Mu) : Vec3d.UnitZ;
            var x = up - normal * Vec3d.Dot(up, normal);
            x = x.Length > 1e-6 ? x.Normalized() : Vec3d.UnitX;
            var y = Vec3d.Cross(normal, x);
            for (var i = 0; i <= n; i++)
            {
                var (sn, cs) = Math.SinCos(i * (Math.Tau / n) + now * 0.05);
                _cmdPts[i] = CmdP(bp + (x * cs + y * sn) * o.Radius);
            }
            var col = Hud.Colour(CmdAmber, (o.Phase == NavPhase.Orbiting ? 0.35f : 0.8f) * fade);
            for (var i = 0; i + 3 <= n; i += 6)
                ImDrawListExtensions.AddPolyline(dl, _cmdPts.AsSpan(i, 4), col, ImDrawFlags.None, 1.4f * _scale);
            return;
        }
        var to = o.Kind == NavKind.Ground ? _cvPose.ToEcliptic(o.Point) : bp + o.Point;
        var at = CmdP(to);
        var d = 7 * _scale;
        CmdGlow(dl, at + new float2(0, -d), at + new float2(d, 0), CmdAmber, fade);
        CmdGlow(dl, at + new float2(d, 0), at + new float2(0, d), CmdAmber, fade);
        CmdGlow(dl, at + new float2(0, d), at + new float2(-d, 0), CmdAmber, fade);
        CmdGlow(dl, at + new float2(-d, 0), at + new float2(0, -d), CmdAmber, fade);
        CmdRing(dl, at, d * 2.2f, CmdAmber, 0.6f * fade, 1f, 3, 0.5f, (float)(now * 1.2));
        // The way there, as dashes running towards it.
        var gap = at - cr.At;
        var len = gap.Length();
        if (len > 30 * _scale && len < 30000)
        {
            var dir = gap / len;
            var step = 14 * _scale;
            for (var t = (float)(now * 22 % step); t + step * 0.5f < len; t += step)
                CmdLine(dl, cr.At + dir * t, cr.At + dir * (t + step * 0.5f), CmdAmber, 0.6f * fade);
        }
    }

    /// <summary>A craft's call sign: what its flight computer is called, and its number.</summary>
    private string CmdName(Construct c)
    {
        var key = c.Id | (long)(c.StabCount > 0 && c.Stabs[0].Part < c.PartCount ? c.Parts[c.Stabs[0].Part].Def : 0) << 16;
        if (!_cmdNames.TryGetValue(c.Id, out var name) || name.Key != key)
        {
            var kind = "CRAFT";
            if (c.StabCount > 0 && c.Stabs[0].Part < c.PartCount)
            {
                var part = c.Parts[c.Stabs[0].Part].D.Name;
                var cut = part.IndexOf(" Flight", StringComparison.OrdinalIgnoreCase);
                if (cut > 0 && !part.StartsWith("Small", StringComparison.OrdinalIgnoreCase) && !part.StartsWith("Large", StringComparison.OrdinalIgnoreCase))
                    kind = part[..cut].ToUpperInvariant();
            }
            else if (c.SpaceShip != 0)
                kind = "HULL";
            _cmdNames[c.Id] = name = (key, kind + " " + c.Id);
        }
        return name.Text;
    }

    private readonly Dictionary<ushort, (long Key, string Text)> _cmdNames = new(64);

    // ---------------------------------------------------------------------------------------------------------------
    // The hologram
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>A hull as lines: each level's outline, in the construct's rest frame.</summary>
    private sealed class CmdHolo
    {
        public ushort Id;
        public int Parts;
        public Vector3[] A = [], B = [];
        public float[] Level = []; // 0 at the keel .. 1 at the top
        public int Count;
        public double Used;
    }

    private readonly CmdHolo[] _cmdHolos = [new(), new(), new(), new()];
    private byte[] _cmdGrid = [];
    private const int CmdHoloCells = 72, CmdHoloLevels = 12, CmdHoloLines = 6000;

    private CmdHolo CmdHoloOf(Construct c, double now)
    {
        CmdHolo? free = null;
        foreach (var h in _cmdHolos)
        {
            if (h.Id == c.Id && h.Parts == c.PartCount)
            {
                h.Used = now;
                return h;
            }
            if (free == null || h.Used < free.Used)
                free = h;
        }
        CmdHoloBuild(free!, c);
        free!.Used = now;
        return free;
    }

    /// <summary>
    /// Cuts the hull into levels up its own height and keeps each level's outline: the parts' boxes fill a grid a level,
    /// and every edge between a filled cell and an empty one is a line (runs joined). Once a hull, when first shown.
    /// </summary>
    private void CmdHoloBuild(CmdHolo h, Construct c)
    {
        h.Id = c.Id;
        h.Parts = c.PartCount;
        h.Count = 0;
        var min = c.LocalMin;
        var size = Vector3.Max(c.LocalMax - min, new Vector3(0.1f));
        var cell = MathF.Max(0.2f, MathF.Max(size.X, size.Z) / CmdHoloCells);
        var nx = Math.Clamp((int)MathF.Ceiling(size.X / cell) + 2, 3, CmdHoloCells + 2);
        var nz = Math.Clamp((int)MathF.Ceiling(size.Z / cell) + 2, 3, CmdHoloCells + 2);
        var ny = Math.Clamp((int)MathF.Ceiling(size.Y / MathF.Max(1.25f, size.Y / CmdHoloLevels)), 1, CmdHoloLevels);
        var high = size.Y / ny;
        if (_cmdGrid.Length < nx * nz * ny)
            _cmdGrid = new byte[nx * nz * ny];
        Array.Clear(_cmdGrid, 0, nx * nz * ny);
        for (var i = 0; i < c.PartCount; i++)
        {
            c.PartBounds(i, out var lo, out var hi);
            var x0 = Math.Clamp((int)MathF.Floor((lo.X - min.X) / cell + 0.25f) + 1, 1, nx - 2);
            var x1 = Math.Clamp((int)MathF.Ceiling((hi.X - min.X) / cell - 0.25f), x0, nx - 2);
            var z0 = Math.Clamp((int)MathF.Floor((lo.Z - min.Z) / cell + 0.25f) + 1, 1, nz - 2);
            var z1 = Math.Clamp((int)MathF.Ceiling((hi.Z - min.Z) / cell - 0.25f), z0, nz - 2);
            var y0 = Math.Clamp((int)MathF.Floor((lo.Y - min.Y) / high + 0.2f), 0, ny - 1);
            var y1 = Math.Clamp((int)MathF.Ceiling((hi.Y - min.Y) / high - 0.2f) - 1, y0, ny - 1);
            for (var y = y0; y <= y1; y++)
                for (var z = z0; z <= z1; z++)
                    Array.Fill(_cmdGrid, (byte)1, (y * nz + z) * nx + x0, x1 - x0 + 1);
        }
        var want = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                want = Math.Min(want, CmdHoloLines);
                if (h.A.Length < want)
                {
                    h.A = new Vector3[want];
                    h.B = new Vector3[want];
                    h.Level = new float[want];
                }
            }
            var n = 0;
            for (var y = 0; y < ny; y++)
            {
                var wy = min.Y + (y + 0.5f) * high;
                var level = ny > 1 ? y / (float)(ny - 1) : 1f;
                // Edges along x: between row z - 1 and row z.
                for (var z = 1; z < nz; z++)
                {
                    var start = -1;
                    for (var x = 0; x <= nx; x++)
                    {
                        var edge = x < nx && _cmdGrid[(y * nz + z) * nx + x] != _cmdGrid[(y * nz + z - 1) * nx + x];
                        if (edge && start < 0)
                            start = x;
                        else if (!edge && start >= 0)
                        {
                            if (pass == 1 && n < want)
                            {
                                h.A[n] = new Vector3(min.X + (start - 1) * cell, wy, min.Z + (z - 1) * cell);
                                h.B[n] = new Vector3(min.X + (x - 1) * cell, wy, min.Z + (z - 1) * cell);
                                h.Level[n] = level;
                            }
                            n++;
                            start = -1;
                        }
                    }
                }
                // Edges along z: between column x - 1 and column x.
                for (var x = 1; x < nx; x++)
                {
                    var start = -1;
                    for (var z = 0; z <= nz; z++)
                    {
                        var edge = z < nz && _cmdGrid[(y * nz + z) * nx + x] != _cmdGrid[(y * nz + z) * nx + x - 1];
                        if (edge && start < 0)
                            start = z;
                        else if (!edge && start >= 0)
                        {
                            if (pass == 1 && n < want)
                            {
                                h.A[n] = new Vector3(min.X + (x - 1) * cell, wy, min.Z + (start - 1) * cell);
                                h.B[n] = new Vector3(min.X + (x - 1) * cell, wy, min.Z + (z - 1) * cell);
                                h.Level[n] = level;
                            }
                            n++;
                            start = -1;
                        }
                    }
                }
            }
            if (pass == 0)
                want = n;
            else
                h.Count = Math.Min(n, want);
        }
    }

    /// <summary>The picked craft as a hologram where it is, turned as it is, its thrusters lit when they push.</summary>
    private void CmdHoloDraw(ImDrawListPtr dl, Game g, Construct c, in CmdCraft cr, Vector3 colour, float fade, double now)
    {
        var h = CmdHoloOf(c, now);
        // The body's axes on the screen (pixels a metre): through the World's turn into the ecliptic, then the view's.
        var turn = c.SpaceAway != 0 ? Game.AwayRot : _cvPose.Rot;
        Span<float2> ax = stackalloc float2[3];
        for (var k = 0; k < 3; k++)
        {
            var e = turn.Rotate(Vec3d.From(Vector3.Transform(k == 0 ? Vector3.UnitX : k == 1 ? Vector3.UnitY : Vector3.UnitZ, c.Rot)));
            ax[k] = new float2((float)(Vec3d.Dot(e, _cvRight) * _cvScale), -(float)(Vec3d.Dot(e, _cvUp) * _cvScale));
        }
        var com = c.Com;
        var at = cr.At;
        var flicker = 0.93f + 0.07f * MathF.Sin((float)now * 31f);
        // Fewer lines when it is small on the screen.
        var skip = cr.Px < 60 * _scale ? 3 : cr.Px < 140 * _scale ? 2 : 1;
        for (var i = 0; i < h.Count; i += skip)
        {
            var a = h.A[i] - com;
            var b = h.B[i] - com;
            var pa = at + ax[0] * a.X + ax[1] * a.Y + ax[2] * a.Z;
            var pb = at + ax[0] * b.X + ax[1] * b.Y + ax[2] * b.Z;
            dl.AddLine(pa, pb, Hud.Colour(colour, (0.28f + 0.6f * h.Level[i]) * fade * flicker), _scale);
        }
        // Its drive: a flare behind each thruster that pushes.
        var push = c.ThrustShow;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var t = ref c.Thrusters[k];
            if (t.Seg != 0)
                continue;
            var p = t.At - com;
            var tail = p - t.Dir * (1.2f + 5f * push);
            var pa = at + ax[0] * p.X + ax[1] * p.Y + ax[2] * p.Z;
            var pb = at + ax[0] * tail.X + ax[1] * tail.Y + ax[2] * tail.Z;
            if (push > 0.02f)
                CmdGlow(dl, pa, pb, CmdAmber, fade * (0.5f + 0.5f * push), 2f);
            ImDrawListExtensions.AddCircleFilled(dl, in pa, 2.2f * _scale, Hud.Colour(push > 0.02f ? CmdAmber : colour, fade), 6);
        }
        // Where the flight computer sits.
        if (c.StabCount > 0 && c.Stabs[0].Part < c.PartCount)
        {
            var p = c.Parts[c.Stabs[0].Part].Pos - com;
            var pa = at + ax[0] * p.X + ax[1] * p.Y + ax[2] * p.Z;
            CmdRing(dl, pa, 5 * _scale, CmdGreen, fade, 1.2f);
            CmdRing(dl, pa, 9 * _scale, CmdGreen, 0.5f * fade, 1f, 2, 0.6f, (float)(now * 2));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The roster (left)
    // ---------------------------------------------------------------------------------------------------------------

    private void CmdRoster(ImDrawListPtr dl, Game g, SpaceSession s, float fade)
    {
        var pad = 22 * _scale;
        var w = 250 * _scale;
        var row = 24 * _scale;
        var rows = Math.Min(_cmdCraftCount, 14);
        var p0 = _cvOrg + new float2(pad, pad + 100 * _scale);
        var p1 = p0 + new float2(w, 34 * _scale + Math.Max(rows, 1) * row + 8 * _scale);
        CmdPlate(dl, p0, p1, fade, CmdCyan);
        CmdPanelArea(p0, p1);
        ref var head = ref _cmdRosterHead;
        if (head.Stale(_cmdCraftCount))
            head.Text = $"CRAFT IN RANGE  [{_cmdCraftCount}]";
        CmdText(dl, p0 + new float2(12 * _scale, 9 * _scale), 12, CmdCyan, fade, head.Text!);
        if (_cmdCraftCount == 0)
        {
            CmdText(dl, p0 + new float2(12 * _scale, 38 * _scale), 11.5f, CmdDim, fade, "NOTHING THAT FLIES IS ON THE MAP");
            return;
        }
        var m = ImGui.GetMousePos();
        for (var k = 0; k < rows; k++)
        {
            ref var cr = ref _cmdCraft[k];
            if (g.Constructs.Get(cr.Id) is not { } c)
                continue;
            var q0 = p0 + new float2(4 * _scale, 32 * _scale + k * row);
            var q1 = q0 + new float2(w - 8 * _scale, row - 2 * _scale);
            var hover = !_cmdOrderOpen && m.X >= q0.X && m.X < q1.X && m.Y >= q0.Y && m.Y < q1.Y;
            var picked = cr.Id == _cmdSel;
            var order = g.NavOf(cr.Id);
            if (picked || hover)
                dl.AddRectFilled(q0, q1, Hud.Colour(CmdCyan, (picked ? 0.20f : 0.10f) * fade));
            if (picked)
                CmdLine(dl, q0, new float2(q0.X, q1.Y), CmdWhite, fade, 2.5f);
            var colour = picked ? CmdWhite : order != null ? CmdAmber : cr.Orders ? CmdCyan : CmdDim;
            CmdText(dl, q0 + new float2(10 * _scale, 4 * _scale), 12, colour, fade, CmdName(c), 0, false);
            var where = c.SpaceShip != 0 ? "SPACE" : !c.HoverOn && c.Vel.LengthSquared() < 1 ? "DOWN" : "SKY";
            CmdText(dl, new float2(q1.X - 8 * _scale, q0.Y + 5 * _scale), 10.5f, order != null ? CmdAmber : CmdDim, fade, order != null ? "ORDER" : where, 2, false);
            if (hover && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _cmdSel = cr.Id;
                _cmdSelBody = -1;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _cmdFocusAuto = false;
                    _cmdFocusShip = cr.Id;
                    _cmdFocusBody = -1;
                    _cmdPan = Vec3d.Zero;
                    _cmdSpanWant = Math.Max(40, c.Radius * 5.5);
                }
            }
        }
    }

    private CmdLabel _cmdRosterHead;

    // ---------------------------------------------------------------------------------------------------------------
    // The picked craft's card (right)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>What the card shows of a craft, read at intervals.</summary>
    private struct CmdCard
    {
        public ushort Id;
        public double Read;
        public string Name, Class, Status, Flight, Orbit, Hull, Fuel, Power, Drive, Cargo, Crew, Systems, Order, Problem;
        public float HullShare, FuelShare, PowerShare, OrderDone;
        public bool HasFuel, HasPower, HasOrder, Orders, Thrusting;
    }

    private CmdCard _cmdCard;

    private void CmdCardRead(Game g, SpaceSession s, Construct c, in CmdCraft cr, double now)
    {
        ref var k = ref _cmdCard;
        k.Id = c.Id;
        k.Read = now;
        var sys = s.System;
        var body = sys.Bodies[cr.Primary];
        sys.StateOf(cr.Primary, _cvTime, out var bp, out var bv);
        var r = cr.Pos - bp;
        var v = cr.Vel - bv;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        k.Name = CmdName(c);
        var size = c.LocalMax - c.LocalMin;
        var owner = c.Owner != 255 && g.Players.TryGetValue(c.Owner, out var op) ? op.Name.ToUpperInvariant() : "NOBODY";
        k.Class = string.Create(inv, $"{c.Mass / 1000:0.0} T    {MathF.Max(size.X, MathF.Max(size.Y, size.Z)):0} M LONG    {c.PartCount} PARTS    OWNER {owner}");

        var name = _cmdBodyNames.Length > cr.Primary ? _cmdBodyNames[cr.Primary] : body.Name.ToUpperInvariant();
        var height = r.Length - body.Radius;
        var ground = c.SpaceShip == 0 ? c.Vel.Length() : v.Length;
        k.Status = c.SpaceAway != 0 ? "DEEP SPACE, ROUND " + name
            : c.SpaceShip != 0 ? "IN SPACE, ROUND " + name
            : !c.HoverOn && ground < 1 ? "DOWN ON " + name
            : ground < 2 ? "HOVERING OVER " + name
            : "FLYING OVER " + name;
        k.Flight = string.Create(inv, $"{CmdDistance(height).ToUpperInvariant()} UP    {ground:0} M/S");
        k.Orbit = "";
        if (c.SpaceShip != 0 && cr.Primary > 0)
        {
            var energy = v.LengthSquared / 2 - body.Mu / r.Length;
            if (energy >= 0)
                k.Orbit = "ESCAPING " + name;
            else if (Vec3d.Cross(r, v).Length > 1)
            {
                var o = KeplerOrbit.FromState(body.Mu, r, v, _cvTime);
                k.Orbit = o.Periapsis < body.Radius
                    ? "FALLING BACK: IT MEETS THE GROUND"
                    : $"LOW {CmdDistance(o.Periapsis - body.Radius).ToUpperInvariant()}    HIGH {CmdDistance(o.Apoapsis - body.Radius).ToUpperInvariant()}    ROUND IN {CmdTime(o.Period).ToUpperInvariant()}";
            }
        }

        // The hull: how much of its parts' health is left, and how many are wrecked.
        double have = 0, full = 0;
        var broken = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var max = p.D.Health;
            full += max;
            have += Math.Clamp(p.Health, 0, max);
            if (p.Broken)
                broken++;
        }
        k.HullShare = full > 0 ? (float)(have / full) : 1;
        k.Hull = string.Create(inv, $"{k.HullShare * 100:0}%{(broken > 0 ? $"    {broken} PART{(broken == 1 ? "" : "S")} WRECKED" : "")}");

        var tank = c.FuelCapacity();
        k.HasFuel = tank > 0 || c.FuelLeft > 0;
        k.FuelShare = tank > 0 ? Math.Clamp(c.FuelLeft / tank, 0, 1) : c.FuelLeft > 0 ? 1 : 0;
        k.Fuel = k.HasFuel ? string.Create(inv, $"{c.FuelLeft:0} L{(tank > 0 ? $" OF {tank:0}" : "")}") : "NO TANKS";
        k.HasPower = c.ChargeCapacity > 0;
        k.PowerShare = k.HasPower ? Math.Clamp(c.Charge / c.ChargeCapacity, 0, 1) : 0;
        k.Power = k.HasPower ? string.Create(inv, $"{c.Charge / 1000:0.0} MJ OF {c.ChargeCapacity / 1000:0.0}{(c.PowerOn ? "" : "    DEAD")}") : "NO BATTERIES";

        var force = ConstructPhysics.NavDrive(c, out _, out _, out var burn);
        k.Thrusting = c.ThrustShow > 0.02f;
        if (force > 0 && c.Mass > 0)
        {
            var a = force / c.Mass;
            var seconds = burn > 0 ? c.FuelLeft / burn : double.PositiveInfinity;
            k.Drive = string.Create(inv, $"{a:0.0} M/S2 ({a / 9.81f:0.00} G){(double.IsFinite(seconds) ? $"    {CmdTime(seconds).ToUpperInvariant()} OF BURN    DV {a * seconds / 1000:0.00} KM/S" : "    ON POWER")}");
        }
        else
            k.Drive = c.ThrusterCount > 0 ? "NOTHING CAN PUSH NOW (FUEL, POWER OR DAMAGE)" : "NO THRUSTERS";

        var cargo = c.LoadMass + c.CargoMass;
        k.Cargo = cargo > 0.5f ? string.Create(inv, $"{cargo / 1000:0.00} T ABOARD") : "EMPTY";

        // Who is aboard: in its seats, and standing within its hull.
        _cmdCrew.Clear();
        var standing = 0;
        foreach (var (id, p) in g.Players)
        {
            if (!p.Alive)
                continue;
            ref var hand = ref g.SandboxHands.Hand(id);
            if (hand.Seated && hand.SeatBody == c.Id)
            {
                if (_cmdCrew.Length > 0)
                    _cmdCrew.Append(", ");
                _cmdCrew.Append(p.Name.ToUpperInvariant());
                if (c.Driver == id)
                    _cmdCrew.Append(" (PILOT)");
            }
            else if (c.SpaceAway == 0 && Vector3.DistanceSquared(p.Position, c.WorldCom) < c.Radius * c.Radius * 0.6f)
                standing++;
        }
        if (standing > 0)
            _cmdCrew.Append(_cmdCrew.Length > 0 ? ", " : "").Append(inv, $"{standing} ON DECK");
        k.Crew = _cmdCrew.Length > 0 ? _cmdCrew.ToString() : "NOBODY ABOARD";

        _cmdCrew.Clear();
        _cmdCrew.Append(c.StabCount == 0 ? "NO FLIGHT COMPUTER" : Game.NavProblem(c) == null ? "FLIGHT COMPUTER" : "FLIGHT COMPUTER DOWN");
        if (c.StabCount > 0 && ConstructFlight.AutopilotOn(c))
            _cmdCrew.Append(" (AUTOPILOT ON)");
        if (c.JumpCount > 0)
            _cmdCrew.Append("    JUMP DRIVE");
        if (c.GunCount > 0)
            _cmdCrew.Append(inv, $"    {c.GunCount} GUN{(c.GunCount == 1 ? "" : "S")}");
        if (c.ThrusterCount > 0)
            _cmdCrew.Append(inv, $"    {c.ThrusterCount} THRUSTER{(c.ThrusterCount == 1 ? "" : "S")}");
        k.Systems = _cmdCrew.ToString();

        k.Problem = Game.NavProblem(c) is { } why ? "TAKES NO ORDERS: " + why.ToUpperInvariant() : "";
        k.Orders = k.Problem.Length == 0;
        var o2 = g.NavOf(c.Id);
        k.HasOrder = o2 != null;
        k.Order = "";
        k.OrderDone = 0;
        if (o2 != null && o2.Body < sys.Count)
        {
            var phase = o2.Phase switch
            {
                NavPhase.Climb => "CLIMBING OUT OF THE SKY",
                NavPhase.Transfer => "ON ITS WAY",
                NavPhase.Insert => "MATCHING THE ORBIT",
                NavPhase.Orbiting => "IN ORBIT, DRIVE OFF",
                NavPhase.Hold => "HOLDING AT THE POINT",
                NavPhase.Flying => "FLYING THERE",
                NavPhase.Arrived => "OVER THE WAYPOINT",
                NavPhase.NoDrive => "NO DRIVE: IT CAN'T FLY THIS",
                _ => "STARTING",
            };
            k.Order = Game.NavText(sys, o2).ToUpperInvariant() + "    //    " + phase;
        }
    }

    private readonly System.Text.StringBuilder _cmdCrew = new(128);

    private void CmdShipCard(ImDrawListPtr dl, Game g, SpaceSession s, float fade, double now)
    {
        var at = CmdFind(_cmdSel);
        if (at < 0 || g.Constructs.Get(_cmdSel) is not { } c)
        {
            if (_cmdSelBody >= 0)
                CmdBodyCard(dl, g, s, fade);
            return;
        }
        ref var cr = ref _cmdCraft[at];
        if (_cmdCard.Id != c.Id || now - _cmdCard.Read > 0.25 || _cmdCard.Name == null)
            CmdCardRead(g, s, c, cr, now);
        ref var k = ref _cmdCard;
        var pad = 22 * _scale;
        var w = 400 * _scale;
        var line = 19 * _scale;
        var lines = 13 + (k.Orbit.Length > 0 ? 1 : 0) + (k.HasOrder ? 2 : 0) + (k.Problem.Length > 0 ? 1 : 0);
        var p0 = new float2(_cvOrg.X + _cvSize.X - pad - w, _cvOrg.Y + pad);
        var p1 = p0 + new float2(w, 64 * _scale + lines * line + 50 * _scale);
        var edge = k.HasOrder ? CmdAmber : CmdCyan;
        CmdPlate(dl, p0, p1, fade, edge);
        CmdPanelArea(p0, p1);
        var x = p0.X + 16 * _scale;
        var y = p0.Y + 10 * _scale;
        CmdText(dl, new float2(x, y), 22, CmdWhite, fade, k.Name, 0, false);
        if (k.Thrusting)
            CmdText(dl, new float2(p1.X - 26 * _scale, y + 6 * _scale), 11, CmdAmber, fade * (0.6f + 0.4f * MathF.Sin((float)now * 9)), "BURNING", 2, false);
        y += 30 * _scale;
        CmdText(dl, new float2(x, y), 10.5f, CmdDim, fade, k.Class, 0, false);
        y += 22 * _scale;
        CmdLine(dl, new float2(x, y), new float2(p1.X - 16 * _scale, y), edge, 0.4f * fade);
        y += 8 * _scale;
        var col = 92 * _scale;

        void Row(ImDrawListPtr d, string head, string text, Vector3 colour, ref float yy)
        {
            CmdText(d, new float2(x, yy), 10.5f, CmdDim, fade, head, 0, false);
            CmdText(d, new float2(x + col, yy - 1 * _scale), 12, colour, fade, text, 0, false);
            yy += line;
        }

        void Bar(ImDrawListPtr d, string head, float share, string text, bool have, ref float yy)
        {
            CmdText(d, new float2(x, yy), 10.5f, CmdDim, fade, head, 0, false);
            var b0 = new float2(x + col, yy + 2 * _scale);
            var cells = 20;
            var cw = 6.5f * _scale;
            var colour = !have ? CmdDim : share < 0.15f ? CmdRed : share < 0.35f ? CmdAmber : CmdCyan;
            for (var i = 0; i < cells; i++)
            {
                var c0 = b0 + new float2(i * cw, 0);
                var lit = have && (i + 0.5f) / cells <= share + 0.001f;
                d.AddRectFilled(c0, c0 + new float2(cw - 2 * _scale, 10 * _scale), Hud.Colour(colour, (lit ? 0.9f : 0.14f) * fade));
            }
            CmdText(d, new float2(b0.X + cells * cw + 8 * _scale, yy - 1 * _scale), 11.5f, colour, fade, text, 0, false);
            yy += line;
        }

        Row(dl, "STATUS", k.Status, CmdWhite, ref y);
        Row(dl, "FLIGHT", k.Flight, CmdCyan, ref y);
        if (k.Orbit.Length > 0)
            Row(dl, "ORBIT", k.Orbit, k.Orbit.StartsWith("FALLING", StringComparison.Ordinal) ? CmdRed : CmdCyan, ref y);
        y += 6 * _scale;
        Bar(dl, "HULL", k.HullShare, k.Hull, true, ref y);
        Bar(dl, "FUEL", k.FuelShare, k.Fuel, k.HasFuel, ref y);
        Bar(dl, "POWER", k.PowerShare, k.Power, k.HasPower, ref y);
        y += 6 * _scale;
        Row(dl, "DRIVE", k.Drive, k.Drive.StartsWith('N') ? CmdAmber : CmdCyan, ref y);
        Row(dl, "CARGO", k.Cargo, CmdCyan, ref y);
        Row(dl, "CREW", k.Crew, CmdCyan, ref y);
        Row(dl, "SYSTEMS", k.Systems, CmdCyan, ref y);
        y += 6 * _scale;
        CmdLine(dl, new float2(x, y), new float2(p1.X - 16 * _scale, y), edge, 0.4f * fade);
        y += 8 * _scale;
        if (k.Problem.Length > 0)
            Row(dl, "ORDERS", k.Problem, CmdAmber, ref y);
        else if (k.HasOrder)
        {
            Row(dl, "ORDER", k.Order, CmdAmber, ref y);
            y += 4 * _scale;
            if (CmdButton(dl, new float2(x + col, y), new float2(x + col + 150 * _scale, y + 24 * _scale), "CANCEL ORDER", CmdAmber, fade))
                g.NavSend(c.Id, NavKind.None, 0, Vec3d.Zero, 0);
            y += 30 * _scale;
        }
        else
        {
            Row(dl, "ORDER", "NONE. RIGHT CLICK A WORLD OR A PLACE", CmdDim, ref y);
            y += 4 * _scale;
            // The quick ones: a low orbit round the world it is at.
            if (cr.Primary > 0)
            {
                var label = _cmdQuick.Stale(cr.Primary) ? _cmdQuick.Text = "ORBIT " + (_cmdBodyNames.Length > cr.Primary ? _cmdBodyNames[cr.Primary] : "IT") + " LOW" : _cmdQuick.Text!;
                if (CmdButton(dl, new float2(x + col, y), new float2(x + col + 190 * _scale, y + 24 * _scale), label, CmdCyan, fade))
                    g.NavSend(c.Id, NavKind.Orbit, cr.Primary, Vec3d.Zero, Game.NavMinOrbit(s.System, cr.Primary) + 300);
            }
            y += 30 * _scale;
        }
        // Centre it, bottom right of the card.
        if (CmdButton(dl, new float2(p1.X - 96 * _scale, p1.Y - 32 * _scale), new float2(p1.X - 12 * _scale, p1.Y - 10 * _scale), "CENTRE", CmdCyan, fade, !_cmdFocusAuto && _cmdFocusShip == c.Id, 11))
        {
            _cmdFocusAuto = false;
            _cmdFocusShip = c.Id;
            _cmdFocusBody = -1;
            _cmdPan = Vec3d.Zero;
        }
    }

    private CmdLabel _cmdQuick;
    private CmdLabel _cmdBodyCardKey;
    private string[] _cmdBodyLines = ["", "", "", ""];

    /// <summary>A picked body's card: what it is, how big, what pulls there, whether it can be landed on.</summary>
    private void CmdBodyCard(ImDrawListPtr dl, Game g, SpaceSession s, float fade)
    {
        var sys = s.System;
        if (_cmdSelBody >= sys.Count)
            return;
        var b = sys.Bodies[_cmdSelBody];
        if (_cmdBodyCardKey.Stale(_cmdSelBody * 7919L + (long)(_cvTime.Seconds / 2)))
        {
            _cmdBodyCardKey.Text = "";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _cmdBodyLines[0] = _cmdSelBody == 0 ? "THE STAR" : b.Parent == 0 ? "A PLANET OF " + sys.Bodies[0].Name.ToUpperInvariant() : "A MOON OF " + sys.Bodies[b.Parent].Name.ToUpperInvariant();
            _cmdBodyLines[1] = string.Create(inv, $"{CmdDistance(b.Radius).ToUpperInvariant()} RADIUS    {b.SurfaceGravity:0.0} M/S2 AT THE GROUND");
            _cmdBodyLines[2] = _cmdSelBody == 0 ? "" : string.Create(inv, $"{CmdDistance(b.Orbit.A).ToUpperInvariant()} OUT    ROUND IN {CmdTime(b.Orbit.Period).ToUpperInvariant()}");
            _cmdBodyLines[3] = b.Map != null ? (_cmdSelBody == s.Bubble.Body ? "YOU ARE HERE" : "A WORLD YOU CAN LAND ON") : _cmdSelBody == 0 ? "" : "NO GROUND TO LAND ON YET";
        }
        var pad = 22 * _scale;
        var w = 400 * _scale;
        var p0 = new float2(_cvOrg.X + _cvSize.X - pad - w, _cvOrg.Y + pad);
        var p1 = p0 + new float2(w, 148 * _scale);
        CmdPlate(dl, p0, p1, fade, CmdCyan);
        CmdPanelArea(p0, p1);
        var x = p0.X + 16 * _scale;
        var y = p0.Y + 10 * _scale;
        CmdText(dl, new float2(x, y), 22, CmdWhite, fade, _cmdBodyNames.Length > _cmdSelBody ? _cmdBodyNames[_cmdSelBody] : b.Name, 0, false);
        y += 34 * _scale;
        for (var i = 0; i < 4; i++)
        {
            if (_cmdBodyLines[i].Length == 0)
                continue;
            CmdText(dl, new float2(x, y), 12, i == 3 ? CmdGreen : CmdCyan, fade, _cmdBodyLines[i], 0, false);
            y += 20 * _scale;
        }
        if (CmdButton(dl, new float2(p1.X - 96 * _scale, p1.Y - 32 * _scale), new float2(p1.X - 12 * _scale, p1.Y - 10 * _scale), "CENTRE", CmdCyan, fade, !_cmdFocusAuto && _cmdFocusBody == _cmdSelBody, 11))
        {
            _cmdFocusAuto = false;
            _cmdFocusBody = _cmdSelBody;
            _cmdFocusShip = 0;
            _cmdPan = Vec3d.Zero;
        }
    }
}
