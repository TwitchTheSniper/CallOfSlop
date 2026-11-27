using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Ship Designer's yard input: the orbit camera (the page's OrbitControls: left or middle drag turns, right drag pans, the
/// wheel zooms), the move arrows on a picked body (TransformControls, 25 cm snap), and the page's pointer handlers: picking,
/// dragging station rings, pieces, rooms (move, size, height), doors; drawing rooms; the palette; mounting sockets from a room.
/// </summary>
public sealed partial class Ui
{
    private Vector3 _camPos = new(38, 22, -12), _camTarget = new(0, 3, 22);
    private bool _sdFramePending, _sdOrbiting, _sdPanning;
    private float2 _yardMin, _yardMax;
    private Matrix4x4 _sdView, _sdProj;

    private sealed class SdDrag
    {
        public string What = "";
        public int Id, Hull, I;
        public float X0, Y0;
        public bool Moved, Off;
        public double Rx0, Rx1, Rz0, Rz1, H0;
        public (double X, double Z)? Start, End;
        public (bool Any, bool X0, bool X1, bool Z0, bool Z1) Edges;
        public Vector3 At;
        public Vector3 S0, S1;
    }

    private SdDrag? _drag;

    // The page's tools: the connection being placed, mounting a socket from a room, merging, ladders, doors, palette.
    private sealed class ConnState
    {
        public string Step = "a";
        public JsonObject? A, B;
        public int JoinId, HullId;
        public bool NewBody;
    }

    private ConnState? _conn;

    private sealed class MountState
    {
        public string Kind = "";
        public int Room, Pid;
        public JsonObject? Pending, Last;
    }

    private MountState? _mounting;
    private double _pieceSize = 1, _pieceRecess;
    private string _extShape = "Box", _extUp = "Vault";
    private int _mergeFrom;
    private (int A, int B)? _mergeAcross;
    private (string Kind, int Dir, int Room)? _placingRiser;
    private int _doorFrom;
    private string _doorKind = "Door";
    private string _roomKind = "Cabin";
    private bool _drawing = true;
    private (string Kind, int Id, bool Cut)? _clip;
    private int _pickRoomFor;
    private bool _pMirror = true, _pPair = true, _allStations;

    private sealed class PaletteDrag
    {
        public string Kind = "";
        public int Id;
        public bool Off;
        public bool Carry; // taken from the quick wheel: it follows the pointer until a click (UiShipDesignerWheel.cs)
    }

    private PaletteDrag? _paletteDrag;

    // The move arrows (TransformControls) on a picked body.
    private int _gizmoHull;
    private Vector3 _gizmoPos;
    private int _gizmoAxis = -1, _gizmoHover = -1;

    // The pointer's shape over the yard, as the page's cursor: set by Hover, kept while its drag runs.
    private ImGuiMouseCursor _sdCursor = ImGuiMouseCursor.Arrow;
    private Vector3 _gizmoStart, _gizmoGrab;

    private const float Fov = 45 * MathF.PI / 180;

    private (Vector3 O, Vector3 D) RayAt(float2 mouse)
    {
        var w = MathF.Max(1, _yardMax.X - _yardMin.X);
        var h = MathF.Max(1, _yardMax.Y - _yardMin.Y);
        var nx = (mouse.X - _yardMin.X) / w * 2 - 1;
        var ny = -((mouse.Y - _yardMin.Y) / h * 2 - 1);
        Matrix4x4.Invert(_sdView * _sdProj, out var inv);
        var a = Vector4.Transform(new Vector4(nx, ny, 0, 1), inv);
        var b = Vector4.Transform(new Vector4(nx, ny, 1, 1), inv);
        var p0 = new Vector3(a.X, a.Y, a.Z) / a.W;
        var p1 = new Vector3(b.X, b.Y, b.Z) / b.W;
        return (_camPos, Vector3.Normalize(p1 - p0));
    }

    private float2? ToScreen(Vector3 p)
    {
        var c = Vector4.Transform(new Vector4(p, 1), _sdView * _sdProj);
        if (c.W <= 1e-4f)
            return null;
        return new float2(_yardMin.X + (c.X / c.W * 0.5f + 0.5f) * (_yardMax.X - _yardMin.X), _yardMin.Y + (0.5f - c.Y / c.W * 0.5f) * (_yardMax.Y - _yardMin.Y));
    }

    private List<Hit> CastAt(float2 m, IEnumerable<DesignObj>? list = null)
    {
        var (o, d) = RayAt(m);
        return Cast(o, d, list);
    }

    private Hit? HullHit(float2 m) => CastAt(m, _sdPickables.Where(o => o.Pick?.Type == "hull")).Cast<Hit?>().FirstOrDefault();

    /// <summary>The yard: the view toggles, the 3D (drawn by Program behind the UI), the tip and the toast; then the mouse.</summary>
    private void Yard(float2 min, float2 max)
    {
        _yardMin = min;
        _yardMax = max;
        var aspect = MathF.Max(1, max.X - min.X) / MathF.Max(1, max.Y - min.Y);
        _sdView = Matrix4x4.CreateLookAt(_camPos, _camTarget, Vector3.UnitY);
        _sdProj = Matrix4x4.CreatePerspectiveFieldOfView(Fov, aspect, 0.1f, 2000f);
        if (_sdFramePending && _sdLoaded)
        {
            _sdFramePending = false;
            FrameShip();
        }
        var io = ImGui.GetIO();
        var mouse = io.MousePos;

        // The view toggles over the yard's corner.
        ImGui.SetCursorScreenPos(min + new float2(10 * _scale, 10 * _scale));
        if (ImGui.Button("Frame ship##sdv"u8))
            FrameShip();
        ImGui.SameLine();
        void Toggle(string label, ref bool v)
        {
            if (OnButton(label, v))
            {
                v = !v;
                Rebuild();
            }
            ImGui.SameLine();
        }
        Toggle("X-ray hull##sdv", ref _vXray);
        Toggle("Rooms##sdv", ref _vRooms);
        Toggle("Socket links##sdv", ref _vLinks);
        Toggle("Person##sdv", ref _vPerson);
        Toggle("Hull shown##sdv", ref _vHull);
        Toggle("Parts shown##sdv", ref _vParts);
        ImGui.NewLine();

        var dl = ImGui.GetWindowDrawList();
        if (_sdTip.Length > 0)
        {
            var tp = new float2(min.X + 12 * _scale, max.Y - 30 * _scale);
            var sz = ImGui.CalcTextSize(_sdTip);
            var tp1 = tp + sz + new float2(12 * _scale, 8 * _scale);
            var tp0 = tp - new float2(6 * _scale, 4 * _scale);
            ImDrawListExtensions.AddRectFilled(dl, in tp0, in tp1, new ImColor8(14, 19, 23, 200), 4 * _scale, default);
            ImDrawListExtensions.AddText(dl, in tp, new ImColor8(200, 210, 216, 255), _sdTip);
        }
        if (_sdToast.Length > 0 && _sdNow < _sdToastUntil)
        {
            var sz = ImGui.CalcTextSize(_sdToast);
            var tp = new float2((min.X + max.X) / 2 - sz.X / 2, min.Y + 52 * _scale);
            var t0 = tp - new float2(12 * _scale, 8 * _scale);
            var t1 = tp + sz + new float2(12 * _scale, 8 * _scale);
            ImDrawListExtensions.AddRectFilled(dl, in t0, in t1, new ImColor8(38, 50, 58, 240), 4 * _scale, default);
            var e0 = t0;
            var e1 = new float2(t0.X + 3 * _scale, t1.Y);
            ImDrawListExtensions.AddRectFilled(dl, in e0, in e1, _sdToastInfo ? Hex8(0x5cd07a) : Hex8(0xff6a50), 0, default);
            ImDrawListExtensions.AddText(dl, in tp, new ImColor8(242, 245, 247, 255), _sdToast);
        }

        // The mouse in the yard (not over a button, the file dialog or a panel).
        var inYard = mouse.X >= min.X && mouse.X < max.X && mouse.Y >= min.Y && mouse.Y < max.Y && !_sdFileOpen;
        var free = inYard && !ImGui.IsAnyItemHovered() && !ImGui.IsAnyItemActive();
        GizmoFrame(mouse, free);
        var wheel = SdWheelFrame(io, mouse); // (the quick wheel holds the mouse while open)
        if (free && _gizmoAxis < 0 && !wheel)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _paletteDrag == null)
            {
                if (_gizmoHover >= 0)
                    GizmoGrab(mouse);
                else
                {
                    PointerDown(mouse, io.KeyShift);
                    if (_drag == null && _gizmoAxis < 0)
                        _sdOrbiting = true;
                }
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
                SdMiddleDown(mouse); // held still: the quick wheel; moved: orbit, as before

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                _sdPanning = true;
            if (io.MouseWheel != 0)
            {
                var d = _camPos - _camTarget;
                _camPos = _camTarget + d * MathF.Pow(0.95f, io.MouseWheel * 2);
            }
            if (_drag == null && _paletteDrag == null && _gizmoAxis < 0 && !_sdOrbiting && !_sdPanning)
            {
                _sdCursor = ImGuiMouseCursor.Arrow;
                Hover(mouse);
                if (_gizmoHover >= 0)
                    _sdCursor = ImGuiMouseCursor.ResizeAll;
            }
            if (_sdCursor != ImGuiMouseCursor.Arrow && !_sdOrbiting && !_sdPanning)
                ImGui.SetMouseCursor(_sdCursor);
        }
        var h = MathF.Max(1, max.Y - min.Y);
        if (_sdOrbiting)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                var dlt = io.MouseDelta;
                var off = _camPos - _camTarget;
                var r = off.Length();
                var theta = MathF.Atan2(off.X, off.Z) - MathF.Tau * dlt.X / h;
                var phi = Math.Clamp(MathF.Acos(Math.Clamp(off.Y / r, -1, 1)) - MathF.Tau * dlt.Y / h, 0.0001f, MathF.PI - 0.0001f);
                _camPos = _camTarget + r * new Vector3(MathF.Sin(phi) * MathF.Sin(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Cos(theta));
            }
            else
                _sdOrbiting = false;
        }
        if (_sdPanning)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                var dlt = io.MouseDelta;
                var off = _camPos - _camTarget;
                var per = 2 * off.Length() * MathF.Tan(Fov / 2) / h;
                var fwd = Vector3.Normalize(-off);
                var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
                var up = Vector3.Cross(right, fwd);
                var mv = (-dlt.X * right + dlt.Y * up) * per;
                _camPos += mv;
                _camTarget += mv;
            }
            else
                _sdPanning = false;
        }
        if (_paletteDrag != null)
        {
            if (_paletteDrag.Carry ? !(free && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) : ImGui.IsMouseDown(ImGuiMouseButton.Left))
                PaletteMove(mouse, inYard);
            else
                PaletteDrop();
        }
        if (_drag != null)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                PointerMove(mouse);
            else
                PointerUp();
        }
        if (_mounting != null && _drag == null && inYard)
        {
            var op = MountAim(mouse);
            if (op != null)
                _mounting.Pending = op;
        }
        if (!io.WantTextInput)
            Keys(io);

        _sdView = Matrix4x4.CreateLookAt(_camPos, _camTarget, Vector3.UnitY);
        var objs = new List<DesignObj>(_sdObjs.Count + _sdPreview.Count + 8);
        objs.AddRange(_sdObjs);
        objs.AddRange(_sdPreview);
        objs.AddRange(GizmoObjs());
        ShipYardFrame = new ShipDesignerFrame(min, max, objs, _sdView, _sdProj, _camPos);
        SdWheelDraw();
    }

    // ---------- the move arrows

    private void PlaceGizmo()
    {
        if (_gizmoAxis >= 0)
            return;
        _gizmoHull = 0;
        if (_sdMode == "hull" && _selKind == "hull" && _selStation < 0)
        {
            var h = ById(Arr(S, "hulls"), _selId);
            if (!Found(h))
                return;
            _gizmoHull = I(h, "id");
            _gizmoPos = new Vector3((float)D(h, "x"), (float)D(h, "y"), (float)D(h, "z"));
        }
    }

    private float GizmoLen => Vector3.Distance(_camPos, _gizmoPos) * 0.14f;

    private static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

    private void GizmoFrame(float2 mouse, bool free)
    {
        _gizmoHover = -1;
        if (_gizmoHull == 0)
            return;
        if (_gizmoAxis >= 0)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                // Released: the last light move, then the whole body cut where it lands.
                _gizmoAxis = -1;
                Flush();
                if (!_dragFirst)
                    Cmd(new JsonObject { ["op"] = "moveHull", ["hull"] = _gizmoHull, ["x"] = _gizmoPos.X, ["y"] = _gizmoPos.Y, ["z"] = _gizmoPos.Z }, true);
                return;
            }
            var (ro, rd) = RayAt(mouse);
            var t = AxisParam(_gizmoStart, Axes[_gizmoAxis], ro, rd);
            if (t is { } tt)
            {
                var v = _gizmoStart + Axes[_gizmoAxis] * (tt - _gizmoGrab.X);
                v = new Vector3(Snap(v.X), Snap(v.Y), Snap(v.Z));
                if (v != _gizmoPos)
                {
                    _gizmoPos = v;
                    DragCmd(new JsonObject { ["op"] = "moveHull", ["light"] = true, ["preview"] = true, ["hull"] = _gizmoHull, ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z });
                }
            }
            return;
        }
        if (!free)
            return;
        var o = ToScreen(_gizmoPos);
        if (o == null)
            return;
        var best = 10 * _scale;
        for (var k = 0; k < 3; k++)
        {
            var e = ToScreen(_gizmoPos + Axes[k] * GizmoLen);
            if (e == null)
                continue;
            var dd = SegDist(mouse, o.Value, e.Value);
            if (dd < best)
            {
                best = dd;
                _gizmoHover = k;
            }
        }
    }

    private static float Snap(float v) => MathF.Round(v / 0.25f) * 0.25f;

    private void GizmoGrab(float2 mouse)
    {
        var (ro, rd) = RayAt(mouse);
        var t = AxisParam(_gizmoPos, Axes[_gizmoHover], ro, rd);
        if (t == null)
            return;
        _gizmoAxis = _gizmoHover;
        _gizmoStart = _gizmoPos;
        _gizmoGrab = new Vector3(t.Value, 0, 0);
        _dragFirst = true;
    }

    /// <summary>How far along the line (p, u) the point nearest the ray is.</summary>
    private static float? AxisParam(Vector3 p, Vector3 u, Vector3 o, Vector3 d)
    {
        var w = o - p;
        float b = Vector3.Dot(u, d), dd = Vector3.Dot(u, w), e = Vector3.Dot(d, w), den = 1 - b * b;
        if (MathF.Abs(den) < 1e-4f)
            return null;
        return (dd - b * e) / den;
    }

    private static float SegDist(float2 p, float2 a, float2 b)
    {
        var ab = b - a;
        var l2 = ab.X * ab.X + ab.Y * ab.Y;
        var t = l2 < 1e-6f ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / l2, 0, 1);
        var q = a + ab * t;
        return MathF.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
    }

    private readonly List<DesignGeo> _gizmoGeos = [];

    private IEnumerable<DesignObj> GizmoObjs()
    {
        foreach (var g in _gizmoGeos)
            ShipDesignerRenderer.Free(g);
        _gizmoGeos.Clear();
        if (_gizmoHull == 0)
            yield break;
        var len = GizmoLen;
        uint[] cols = [0xff3653, 0x8adb00, 0x2c8fff];
        for (var k = 0; k < 3; k++)
        {
            var b = new GB();
            var lit = k == _gizmoAxis || (k == _gizmoHover && _gizmoAxis < 0);
            var ax = Axes[k];
            var m = FromTo(Vector3.UnitY, ax) * Matrix4x4.CreateTranslation(_gizmoPos);
            b.M = Matrix4x4.CreateTranslation(0, len * 0.42f, 0) * m;
            b.Cylinder(len * 0.012f, len * 0.84f, 6);
            b.M = Matrix4x4.CreateTranslation(0, len * 0.92f, 0) * m;
            b.Box(len * 0.07f, len * 0.16f, len * 0.07f, 0, 0, 0);
            var g = b.Geo();
            _gizmoGeos.Add(g);
            yield return new DesignObj { Geo = g, Tint = Lin(lit ? 0xffff40u : cols[k]), Unlit = true, DepthTest = false, Transparent = true, Order = 9 };
        }
    }

    // ---------- the pointer (the page's pointerdown, pointermove, pointerup)

    private (double X, double Z)? DeckPoint(float2 m)
    {
        var decks = Arr(S, "decks").ToList();
        if (_sdDeck >= decks.Count)
            return null;
        var f = (float)D(decks[_sdDeck], "floor");
        var (o, d) = RayAt(m);
        // On a stacked deck the floor faces the bow: its plane is z = floor, and a room's second axis (z0..z1) is the height y.
        if (Stacked)
        {
            if (MathF.Abs(d.Z) < 1e-6f)
                return null;
            var t = (f - o.Z) / d.Z;
            if (t < 0)
                return null;
            var p = o + d * t;
            return (Math.Round(p.X * 4) / 4, Math.Round(p.Y * 4) / 4);
        }
        if (MathF.Abs(d.Y) < 1e-6f)
            return null;
        var ty = (f - o.Y) / d.Y;
        if (ty < 0)
            return null;
        var q = o + d * ty;
        return (Math.Round(q.X * 4) / 4, Math.Round(q.Z * 4) / 4);
    }

    private int OtherDeck(int i, int dir = 1)
    {
        var order = Arr(S, "decks").Select((d, j) => (F: D(d, "floor"), J: j)).OrderBy(p => p.F).Select(p => p.J).ToList();
        var at = order.IndexOf(i);
        if (dir < 0)
            return at > 0 ? order[at - 1] : -1;
        return at >= 0 && at + 1 < order.Count ? order[at + 1] : -1;
    }

    private static JsonObject At(Hit h) => new()
    {
        ["hull"] = h.O.Pick!.Hull, ["copy"] = h.O.Pick.Copy, ["x"] = h.Point.X, ["y"] = h.Point.Y, ["z"] = h.Point.Z,
        ["face"] = h.O.Pick.Surface.Length > 0 ? h.O.Pick.Surface : "skin",
    };

    private void PointerDown(float2 m, bool shift)
    {
        var hits = CastAt(m);
        Hit? top = hits.Count > 0 ? hits[0] : null;
        Hit? By(params string[] t) => hits.Cast<Hit?>().FirstOrDefault(x => t.Contains(x!.Value.O.Pick?.Type));
        if (_sdMode == "hull")
        {
            var st = By("station");
            if (st is { } s && _conn == null)
            {
                SelectStation(s.O.Pick!.Hull, s.O.Pick.I);
                _drag = new SdDrag { What = "station", Hull = s.O.Pick.Hull, I = s.O.Pick.I, X0 = m.X, Y0 = m.Y };
                _dragFirst = true;
                return;
            }
            var hh = By("hull");
            if (hh is { } hv && _conn != null)
            {
                var at = At(hv);
                if (_conn.Step == "a")
                {
                    _conn = new ConnState { Step = "b", A = at };
                    _sdTip = "Set the connection, then click the second body where it meets, or add a new body.";
                    return;
                }
                if (hv.O.Pick!.Hull == _conn.HullId)
                    _conn.HullId = 0; // (connecting to the body this tool made keeps it)
                _conn.B = at;
                _conn.NewBody = false;
                PlaceJoin();
                return;
            }
            var jn = By("join");
            if (jn is { } j && (hh == null || j.T <= hh.Value.T))
            {
                Select("join", j.O.Pick!.Id);
                return;
            }
            if (hh is { } hs)
                Select("hull", hs.O.Pick!.Hull);
            return;
        }
        if (_sdMode == "pieces" && _pickRoomFor != 0)
        {
            // Picking the room a socket opens into: a click on a room joins them; anywhere else stops picking.
            var r = By("room");
            var id = _pickRoomFor;
            _pickRoomFor = 0;
            if (r is { } rr)
            {
                Cmd(new JsonObject { ["op"] = "setPiece", ["id"] = id, ["room"] = rr.O.Pick!.Id });
                Toast("Joined to that room.", true);
            }
            Select("piece", id);
            _sdTip = SdTips[_sdMode];
            return;
        }
        if (_sdMode == "pieces")
        {
            var p = By("piece");
            if (p is { } pv && (top == null || top.Value.O == pv.O || top.Value.T + 0.01f >= pv.T))
            {
                Select("piece", pv.O.Pick!.Id);
                _drag = new SdDrag { What = "piece", Id = pv.O.Pick.Id, X0 = m.X, Y0 = m.Y };
                _dragFirst = true;
                return;
            }
            if (By("hull") == null)
                Select(null, 0);
            return;
        }
        if (_sdMode == "interior" && _mounting != null)
        {
            var op = MountAim(m) ?? _mounting.Last;
            if (op == null)
            {
                Toast("Click a wall, the floor or the ceiling of the room.");
                return;
            }
            var c = (JsonObject)op.DeepClone();
            c["preview"] = false;
            c["light"] = false;
            c["replace"] = _mounting.Pid;
            var a = Cmd(c, _mounting.Pid != 0);
            if (Str(a, "error").Length > 0)
            {
                Toast(Str(a, "error"));
                return;
            }
            var kind = _mounting.Kind;
            _mounting.Pid = 0;
            StopMount(true);
            Toast($"{kind} fitted: it cuts the hull there and opens into the room.", true);
            return;
        }
        if (Inside && _placingRiser is { } pr)
        {
            var rm = ById(Arr(S, "rooms"), pr.Room);
            _placingRiser = null;
            var at = DeckPoint(m);
            var from = Found(rm) ? I(rm, "deck") : _sdDeck;
            var to = OtherDeck(from, pr.Dir);
            if (Found(rm) && (at == null || at.Value.X < D(rm, "x0") || at.Value.X > D(rm, "x1") || at.Value.Z < D(rm, "z0") || at.Value.Z > D(rm, "z1")))
                at = ((D(rm, "x0") + D(rm, "x1")) / 2, (D(rm, "z0") + D(rm, "z1")) / 2); // off the room: its middle
            if (at == null)
            {
                Toast("Click the deck.");
                return;
            }
            if (to < 0)
            {
                Toast(pr.Dir < 0 ? "There is no deck below this one." : "There is no deck above this one.");
                return;
            }
            var a = Cmd(new JsonObject { ["op"] = "addRiser", ["kind"] = pr.Kind, ["x"] = at.Value.X, ["z"] = at.Value.Z, ["from"] = from, ["to"] = to });
            if (Str(a, "error").Length > 0)
            {
                Toast(Str(a, "error"));
                return;
            }
            Select("riser", I(a, "id"));
            Toast($"{pr.Kind} from deck {from + 1} to deck {to + 1}{(Found(rm) ? ", from " + RoomName(rm) : "")}. Pick its decks on the right to run it farther.", true);
            return;
        }
        if (_sdMode == "connectors")
        {
            // Doors and ladders win over the rooms round them (a room's walls are in front of its doors from most angles).
            var h = By("door") ?? By("riser") ?? By("room");
            if (_doorFrom < 0)
            {
                // The quick wheel's Door with no room picked: this click picks the room it starts from.
                if (h is { } h0 && h0.O.Pick!.Type == "room")
                {
                    _doorFrom = h0.O.Pick.Id;
                    Select("room", _doorFrom);
                    _sdTip = "Click the room the door opens into.";
                }
                else
                {
                    _doorFrom = 0;
                    Toast("Stopped placing the door.");
                }
                return;
            }
            if (_doorFrom != 0)
            {
                var from = _doorFrom;
                _doorFrom = 0;
                if (h is { } hr && hr.O.Pick!.Type == "room" && hr.O.Pick.Id != from)
                {
                    var a = Cmd(new JsonObject { ["op"] = "addDoor", ["a"] = from, ["b"] = hr.O.Pick.Id, ["kind"] = _doorKind });
                    if (Str(a, "error").Length > 0)
                    {
                        Toast(Str(a, "error"));
                        return;
                    }
                    var placed = ById(Arr(S, "placedDoors"), I(a, "id"));
                    if (Found(placed))
                    {
                        Select("door", I(placed, "id"));
                        Toast($"{_doorKind} placed. Drag it along the wall.", true);
                    }
                    else
                    {
                        Select(null, 0);
                        Toast($"Those rooms share too little wall on one deck, so the {_doorKind.ToLowerInvariant()} has nowhere to go: it is kept, and the checks say so.");
                    }
                }
                else
                    Toast("Stopped placing the door.");
                return;
            }
            if (h is { } hd && hd.O.Pick!.Type == "door")
            {
                Select("door", hd.O.Pick.Id);
                var p = ById(Arr(S, "placedDoors"), hd.O.Pick.Id);
                _drag = new SdDrag { What = "door", Id = I(p, "id"), S0 = V3(Get(p, "s0")), S1 = V3(Get(p, "s1")), X0 = m.X, Y0 = m.Y };
                _dragFirst = true;
                return;
            }
            if (h is { } hx)
                Select(hx.O.Pick!.Type, hx.O.Pick.Id);
            else
                Select(null, 0);
            return;
        }
        if (_sdMode == "interior" && _linking is { } lk)
        {
            var lh = By("room");
            if (lh is not { } lr)
            {
                _linking = null;
                _sdTip = SdTips[_sdMode];
                Rebuild();
                Toast("Stopped linking.");
                return;
            }
            var lid = lr.O.Pick!.Id;
            if (lk.From == 0)
            {
                lk.From = lid;
                Select("room", lid);
                _sdTip = "Now click a room in the body at the arm's other end.";
                return;
            }
            if (lid == lk.From)
                return;
            _linking = null;
            _sdTip = SdTips[_sdMode];
            var la = Cmd(C("addPassage", ("a", lk.From), ("b", lid), ("kind", lk.Kind)));
            if (Str(la, "error").Length > 0)
            {
                Toast(Str(la, "error"));
                Rebuild();
                return;
            }
            var pid = I(la, "id");
            Select("passage", pid);
            var made = ById(Arr(S, "passages"), pid);
            Toast(Found(made) && !B(made, "ok", true) ? "Laid, but the arm is too thin: see the checks." : "Linked through the arm.", true);
            return;
        }
        if (_sdMode == "interior" && _mergeFrom != 0)
        {
            var h = By("room");
            var from = _mergeFrom;
            _mergeFrom = 0;
            var rooms = Arr(S, "rooms").ToList();
            var other = h is { } hh2 ? ById(rooms, hh2.O.Pick!.Id) : default;
            var mine = ById(rooms, from);
            if (Found(other) && Found(mine) && I(other, "deck") != I(mine, "deck"))
            {
                // Across decks: the player picks the deck that owns the tall room.
                _mergeAcross = (from, I(other, "id"));
                Select("room", from);
                _sdTip = "Pick which deck owns the merged room, on the right.";
                return;
            }
            if (h is { } hm && hm.O.Pick!.Id != from)
            {
                var a = Cmd(new JsonObject { ["op"] = "mergeRoom", ["a"] = from, ["b"] = hm.O.Pick.Id });
                if (Str(a, "error").Length > 0)
                    Toast(Str(a, "error"));
                else
                {
                    Select("room", from);
                    Toast("Merged. Undo splits them again.", true);
                }
            }
            else
            {
                Rebuild();
                Toast("Stopped merging.");
            }
            return;
        }
        if (Inside || _sdMode == "pieces")
        {
            var hw = By("hullwin", "room", "piece");
            if (hw is { } w && w.O.Pick!.Type == "hullwin" && _placingRiser == null && _mergeFrom == 0)
            {
                Select("hullwin", w.O.Pick.Id);
                return;
            }
        }
        if (_sdMode == "interior")
        {
            var rz = By("riser", "passage");
            if (rz is { } rzv && By("riser", "passage", "room") is { } first && first.O == rzv.O)
            {
                Select(rzv.O.Pick!.Type, rzv.O.Pick.Id);
                return;
            }
            var r = By("room");
            // In Room view a click on a connected room makes it the room shown (Karl, 2026-11-25): the drop-down and its neighbours follow.
            if (r is { } rv && _roomView is { } shown && rv.O.Pick!.Id != shown.Room)
            {
                EnterRoomView(rv.O.Pick.Id);
                return;
            }
            if (r is { } rh)
            {
                Select("room", rh.O.Pick!.Id);
                var room = ById(Arr(S, "rooms"), rh.O.Pick.Id);
                var at = DeckPoint(m);
                var hp = rh.Point;
                (double X, double Z) onPlan = Stacked ? (hp.X, hp.Y) : (hp.X, hp.Z);
                // Its top grabbed: drag up or down to set its height (it can rise through the decks above).
                var bx = Nums(Get(room, "box"));
                var onTop = Stacked ? Math.Abs(hp.Z - bx[4]) < 0.12 : Math.Abs(hp.Y - bx[3]) < 0.12;
                if (onTop)
                {
                    _drag = new SdDrag { What = "roomTop", Id = I(room, "id"), H0 = D(room, "height"), At = hp, X0 = m.X, Y0 = m.Y };
                    _dragFirst = true;
                    return;
                }
                _drag = new SdDrag
                {
                    What = "room", Id = I(room, "id"), Rx0 = D(room, "x0"), Rx1 = D(room, "x1"), Rz0 = D(room, "z0"), Rz1 = D(room, "z1"),
                    Start = at, X0 = m.X, Y0 = m.Y, Edges = EdgesNear(room, onPlan, rh),
                };
                _dragFirst = true;
                return;
            }
            if (shift || _drawing)
            {
                var at = DeckPoint(m);
                if (at == null)
                    return;
                _drag = new SdDrag { What = "draw", Start = at, X0 = m.X, Y0 = m.Y };
                return;
            }
            Select(null, 0);
        }
    }

    /// <summary>
    /// Which edges of a room a plan point is grabbing: only inside its box, within 45 cm (less on a small room), and only from its
    /// floor or top; a wall grabbed in its middle moves the room, near its foot (0.6 m) or its top (0.3 m) sizes it.
    /// </summary>
    private (bool Any, bool X0, bool X1, bool Z0, bool Z1) EdgesNear(JsonElement r, (double X, double Z)? at, Hit? hit)
    {
        if (at == null)
            return default;
        var b = Nums(Get(r, "box"));
        if (hit is { } h && Math.Abs(Stacked ? h.N.Z : h.N.Y) < 0.7f)
        {
            double up = Stacked ? b[5] - h.Point.Z : h.Point.Y - b[2], ht = Stacked ? b[5] - b[4] : b[3] - b[2];
            if (up > 0.6 && up < ht - 0.3)
                return default;
        }
        // Its edges as drawn: a room out to the hull is drawn (and grabbed) out to the skin, past its plan.
        double X0 = Math.Min(D(r, "x0"), b[0]), X1 = Math.Max(D(r, "x1"), b[1]);
        double Z0 = Stacked ? Math.Min(D(r, "z0"), b[2]) : Math.Min(D(r, "z0"), b[4]), Z1 = Stacked ? Math.Max(D(r, "z1"), b[3]) : Math.Max(D(r, "z1"), b[5]);
        double mx = Math.Min(0.45, (X1 - X0) * 0.2), mz = Math.Min(0.45, (Z1 - Z0) * 0.2), t = 0.05;
        var (ax, az) = at.Value;
        if (ax < X0 - t || ax > X1 + t || az < Z0 - t || az > Z1 + t)
            return default;
        bool e0 = ax - X0 < mx, e1 = X1 - ax < mx, e2 = az - Z0 < mz, e3 = Z1 - az < mz;
        return (e0 || e1 || e2 || e3, e0, e1, e2, e3);
    }

    private void PointerMove(float2 m)
    {
        var d = _drag!;
        if (MathF.Sqrt((m.X - d.X0) * (m.X - d.X0) + (m.Y - d.Y0) * (m.Y - d.Y0)) > 4)
            d.Moved = true;
        if (!d.Moved)
            return;
        switch (d.What)
        {
            case "station":
            {
                var h = ById(Arr(S, "hulls"), d.Hull);
                var (o, dir) = RayAt(m);
                var z = AxisParam(new Vector3((float)D(h, "x"), (float)D(h, "y") + 2, 0), Vector3.UnitZ, o, dir);
                if (z is { } zz)
                    DragCmd(new JsonObject { ["op"] = "moveStation", ["hull"] = d.Hull, ["i"] = d.I, ["z"] = zz - D(h, "z") });
                break;
            }
            case "piece":
            {
                var hit = HullHit(m);
                d.Off = hit == null;
                if (hit is { } hv)
                    DragCmd(new JsonObject
                    {
                        ["op"] = "movePiece", ["preview"] = true, ["light"] = true, ["id"] = d.Id, ["hull"] = hv.O.Pick!.Hull, ["copy"] = hv.O.Pick.Copy,
                        ["surface"] = hv.O.Pick.Surface, ["x"] = hv.Point.X, ["y"] = hv.Point.Y, ["z"] = hv.Point.Z,
                    });
                _sdTip = hit != null ? "Release to drop it here." : "Release here to take it off the ship.";
                break;
            }
            case "room":
            {
                var at = DeckPoint(m);
                if (at == null || d.Start == null)
                    return;
                double dx = Math.Round((at.Value.X - d.Start.Value.X) * 4) / 4, dz = Math.Round((at.Value.Z - d.Start.Value.Z) * 4) / 4;
                var e = d.Edges;
                if (e.Any)
                {
                    // Grab an edge or a corner to size the room; the far side stays put, and it keeps at least a metre.
                    double x0 = d.Rx0, x1 = d.Rx1, z0 = d.Rz0, z1 = d.Rz1;
                    if (e.X0) x0 = Math.Min(d.Rx0 + dx, d.Rx1 - 1);
                    if (e.X1) x1 = Math.Max(d.Rx1 + dx, d.Rx0 + 1);
                    if (e.Z0) z0 = Math.Min(d.Rz0 + dz, d.Rz1 - 1);
                    if (e.Z1) z1 = Math.Max(d.Rz1 + dz, d.Rz0 + 1);
                    DragCmd(new JsonObject { ["op"] = "setRoom", ["light"] = true, ["id"] = d.Id, ["x0"] = x0, ["x1"] = x1, ["z0"] = z0, ["z1"] = z1 });
                    _sdTip = $"{Fmt(x1 - x0)} x {Fmt(z1 - z0)} m";
                }
                else
                    DragCmd(new JsonObject { ["op"] = "setRoom", ["light"] = true, ["id"] = d.Id, ["x0"] = d.Rx0 + dx, ["x1"] = d.Rx1 + dx, ["z0"] = d.Rz0 + dz, ["z1"] = d.Rz1 + dz });
                break;
            }
            case "door":
            {
                // Along the shared wall: the deck point projected onto it, as 0 to 1 between its ends.
                var at = DeckPoint(m);
                if (at == null)
                    return;
                double ax = d.S0.X, az = Stacked ? d.S0.Y : d.S0.Z, bx = d.S1.X, bz = Stacked ? d.S1.Y : d.S1.Z;
                double ddx = bx - ax, ddz = bz - az, l2 = ddx * ddx + ddz * ddz;
                var t = l2 < 1e-6 ? 0.5 : Math.Clamp(((at.Value.X - ax) * ddx + (at.Value.Z - az) * ddz) / l2, 0, 1);
                DragCmd(new JsonObject { ["op"] = "setDoor", ["light"] = true, ["id"] = d.Id, ["t"] = Math.Round(t * 100) / 100 });
                break;
            }
            case "roomTop":
            {
                // The point on the up line through the grabbed spot nearest the pointer's ray.
                var (o, dir) = RayAt(m);
                var up = Stacked ? -Vector3.UnitZ : Vector3.UnitY;
                var w0 = d.At - o;
                float b = Vector3.Dot(up, dir), dd = Vector3.Dot(up, w0), e = Vector3.Dot(dir, w0), den = 1 - b * b;
                if (den < 1e-4f)
                    return;
                var t = (b * e - dd) / den;
                var h = Math.Max(2.2, Math.Round((d.H0 + t) * 4) / 4);
                DragCmd(new JsonObject { ["op"] = "setRoom", ["light"] = true, ["id"] = d.Id, ["height"] = h });
                _sdTip = $"{Fmt(h)} m high";
                break;
            }
            case "draw":
            {
                var at = DeckPoint(m);
                if (at == null)
                    return;
                d.End = at;
                DrawPreview(d.Start!.Value, at.Value);
                break;
            }
        }
    }

    private void PointerUp()
    {
        var d = _drag!;
        _drag = null;
        if (d.What == "piece" && d.Moved && d.Off)
        {
            _sdPending = null;
            Cmd(new JsonObject { ["op"] = "delPiece", ["id"] = d.Id }, !_dragFirst);
            Select(null, 0);
            Toast("Taken off the ship. Undo puts it back.", true);
        }
        else if (d.What == "draw")
        {
            ClearPreview();
            if (d.End is { } e && d.Start is { } s)
            {
                double x0 = Math.Min(s.X, e.X), x1 = Math.Max(s.X, e.X), z0 = Math.Min(s.Z, e.Z), z1 = Math.Max(s.Z, e.Z);
                if (x1 - x0 >= 0.9 && z1 - z0 >= 0.9)
                {
                    var a = Cmd(new JsonObject { ["op"] = "addRoom", ["deck"] = _sdDeck, ["kind"] = _roomKind, ["x0"] = x0, ["x1"] = x1, ["z0"] = z0, ["z1"] = z1 });
                    if (Str(a, "error").Length == 0)
                        Select("room", I(a, "id"));
                }
            }
        }
        else
        {
            Flush();
            if (d.What is "piece" or "room")
                Take(_sd!.Scene());
        }
        _sdTip = SdTips[_sdMode];
    }

    private void DrawPreview((double X, double Z) a, (double X, double Z) b)
    {
        ClearPreview();
        var decks = Arr(S, "decks").ToList();
        if (_sdDeck >= decks.Count)
            return;
        var dk = decks[_sdDeck];
        float w = (float)Math.Max(0.1, Math.Abs(b.X - a.X)), l = (float)Math.Max(0.1, Math.Abs(b.Z - a.Z)), hh = (float)D(dk, "height"), f = (float)D(dk, "floor");
        var g = new GB();
        if (Stacked)
            g.Box(w, l, hh, (float)(a.X + b.X) / 2, (float)(a.Z + b.Z) / 2, f - hh / 2);
        else
            g.Box(w, hh, l, (float)(a.X + b.X) / 2, f + hh / 2, (float)(a.Z + b.Z) / 2);
        var kind = Arr(CAT, "rooms").FirstOrDefault(k => Str(k, "name") == _roomKind);
        var geo = g.Geo();
        _sdPreview.Add(new DesignObj { Geo = geo, Tint = Lin(HexOf(Str(kind, "colour", "#888888"))), Alpha = 0.35f, Transparent = true, DepthWrite = false, Unlit = true });
        _sdTip = $"{Fmt(w)} x {Fmt(l)} m {_roomKind}";
    }

    private void ClearPreview()
    {
        foreach (var o in _sdPreview)
            ShipDesignerRenderer.Free(o.Geo);
        _sdPreview.Clear();
    }

    private void Hover(float2 m)
    {
        var hits = CastAt(m);
        if (_sdMode == "hull" && hits.Any(h => h.O.Pick?.Type == "station"))
            _sdCursor = ImGuiMouseCursor.ResizeEW;
        else if (_sdMode == "pieces" && hits.FirstOrDefault().O?.Pick?.Type == "piece")
            _sdCursor = ImGuiMouseCursor.Hand;
        else if (_sdMode == "connectors" && hits.Any(h => h.O.Pick?.Type == "door"))
        {
            _sdCursor = ImGuiMouseCursor.Hand;
            _sdTip = "Click to pick the door; drag it along the wall.";
        }
        else if (_sdMode == "interior" && hits.FirstOrDefault(h => h.O.Pick?.Type == "room") is { O: not null } hit && _mounting == null)
        {
            var r = ById(Arr(S, "rooms"), hit.O.Pick!.Id);
            var bx = Nums(Get(r, "box"));
            if (bx.Length < 6)
                return;
            var hp = hit.Point;
            var onTop = Stacked ? Math.Abs(hp.Z - bx[4]) < 0.12 : Math.Abs(hp.Y - bx[3]) < 0.12;
            var e = onTop ? default : EdgesNear(r, Stacked ? (hp.X, hp.Y) : (hp.X, hp.Z), hit);
            _sdCursor = onTop ? ImGuiMouseCursor.ResizeNS : !e.Any ? ImGuiMouseCursor.ResizeAll
                : (e.X0 || e.X1) && (e.Z0 || e.Z1) ? ImGuiMouseCursor.ResizeNWSE : (e.X0 || e.X1) ? ImGuiMouseCursor.ResizeEW : ImGuiMouseCursor.ResizeNS;
            _sdTip = onTop ? "Drag up or down to set the room's height." : e.Any ? "Drag to size the room." : "Drag to move the room.";
        }
    }

    // ---------- mounting a socket from a room: hover the room's wall, floor or ceiling and a holo of the piece shows where the line out
    // from that face leaves the hull; a click fits it there, opening into the room. Esc stops.

    private void StartMount(string kind, int room)
    {
        StopMount();
        _mounting = new MountState { Kind = kind, Room = room };
        _sdTip = $"Move over a wall of the room (its floor or ceiling for a hatch). The {kind} shows where it would cut the hull; click to fit it.";
    }

    private void StopMount(bool keep = false)
    {
        if (_mounting == null)
            return;
        if (!keep && _mounting.Pid != 0)
            Cmd(new JsonObject { ["op"] = "delPiece", ["id"] = _mounting.Pid }, true);
        _mounting = null;
        _sdTip = SdTips.GetValueOrDefault(_sdMode, "");
    }

    private JsonObject? MountAim(float2 m)
    {
        var mt = _mounting!;
        var hit = CastAt(m).FirstOrDefault(h => h.O.Pick?.Type == "room" && h.O.Pick.Id == mt.Room);
        if (hit.O == null)
            return null;
        var up = Stacked ? -Vector3.UnitZ : Vector3.UnitY;
        var n = hit.O.Pick!.Floor ? -up : hit.N;
        return new JsonObject
        {
            ["op"] = "mount", ["kind"] = mt.Kind, ["room"] = mt.Room, ["x"] = hit.Point.X, ["y"] = hit.Point.Y, ["z"] = hit.Point.Z,
            ["nx"] = n.X, ["ny"] = n.Y, ["nz"] = n.Z, ["mirror"] = false, ["pair"] = true, ["size"] = _pieceSize, ["recess"] = _pieceRecess,
        };
    }

    private void MountTick()
    {
        if (_mounting?.Pending is not { } pend)
            return;
        var op = (JsonObject)pend.DeepClone();
        op["replace"] = _mounting.Pid;
        op["preview"] = true;
        op["light"] = true;
        _mounting.Pending = null;
        var a = Cmd(op, _mounting.Pid != 0);
        if (Str(a, "error").Length > 0)
        {
            _sdTip = Str(a, "error");
            _mounting.Pid = 0;
            return;
        }
        _mounting.Pid = I(a, "id");
        _mounting.Last = pend;
    }

    // ---------- the palette: drag a piece from the left panel onto the hull

    private void PaletteStart(string kind)
    {
        SdCarryCancel(); // (a piece the quick wheel put on the pointer goes first)
        _paletteDrag = new PaletteDrag { Kind = kind };
        _dragFirst = true;
        _sdTip = $"Drag the {kind} onto the hull.";
    }

    private void PaletteMove(float2 m, bool inYard)
    {
        var p = _paletteDrag!;
        if (!inYard)
            return;
        var hit = HullHit(m);
        if (hit is not { } hv)
        {
            if (p.Id != 0)
                _sdTip = "Release here to leave it off.";
            p.Off = true;
            return;
        }
        p.Off = false;
        var where = Str(Arr(CAT, "pieces").FirstOrDefault(k => Str(k, "name") == p.Kind), "where");
        if (where is "aft" or "fore" && hv.O.Pick!.Surface == "skin")
        {
            _sdTip = $"A {p.Kind} goes on a hull's end face.";
            return;
        }
        var at = new JsonObject { ["hull"] = hv.O.Pick!.Hull, ["copy"] = hv.O.Pick.Copy, ["surface"] = hv.O.Pick.Surface, ["x"] = hv.Point.X, ["y"] = hv.Point.Y, ["z"] = hv.Point.Z };
        if (p.Id == 0)
        {
            var op = new JsonObject { ["op"] = "addPiece", ["preview"] = true, ["light"] = true, ["kind"] = p.Kind, ["mirror"] = _pMirror, ["pair"] = _pPair, ["size"] = _pieceSize, ["recess"] = _pieceRecess };
            foreach (var kv in at)
                op[kv.Key] = kv.Value?.DeepClone();
            var a = Cmd(op);
            if (Str(a, "error").Length > 0)
            {
                _paletteDrag = null;
                return;
            }
            p.Id = I(a, "id");
            _dragFirst = false;
            Select("piece", p.Id, false);
        }
        else
        {
            var op = new JsonObject { ["op"] = "movePiece", ["preview"] = true, ["light"] = true, ["id"] = p.Id };
            foreach (var kv in at)
                op[kv.Key] = kv.Value?.DeepClone();
            DragCmd(op);
        }
    }

    private void PaletteDrop()
    {
        var p = _paletteDrag!;
        _paletteDrag = null;
        if (p.Id != 0 && p.Off)
        {
            _sdPending = null;
            Cmd(new JsonObject { ["op"] = "delPiece", ["id"] = p.Id }, true);
            Select(null, 0);
        }
        else
        {
            Flush();
            if (p.Id != 0)
            {
                Take(_sd!.Scene()); // now cut its opening
                // An airlock comes with its chamber: an Airlock room behind it, in the same undo step.
                if (Str(Arr(CAT, "pieces").FirstOrDefault(k => Str(k, "name") == p.Kind), "socket") == "airlock")
                {
                    var n = Arr(S, "rooms").Count();
                    var a = Cmd(new JsonObject { ["op"] = "autoRoom", ["id"] = p.Id }, true);
                    if (Str(a, "error").Length == 0)
                        Toast(Arr(S, "rooms").Count() > n ? "Airlock placed, with an airlock room behind it." : "Airlock placed: it opens into the room already there.", true);
                }
                Select("piece", p.Id);
            }
        }
        _sdTip = SdTips[_sdMode];
    }

    // ---------- keys (the page's keydown)

    /// <summary>Esc: stops mounting, else drops the pick, else closes the designer.</summary>
    public void ShipDesignerEscape()
    {
        if (_sdFileOpen)
            _sdFileOpen = false;
        else if (SdWheelOpen || SdCarrying)
            SdWheelReset();
        else if (_doorFrom != 0 || _placingRiser != null)
        {
            _doorFrom = 0;
            _placingRiser = null;
            _sdTip = SdTips[_sdMode];
            Rebuild();
        }
        else if (_linking != null)
        {
            _linking = null;
            _sdTip = SdTips[_sdMode];
            Rebuild();
        }
        else if (_mounting != null)
            StopMount();
        else if (_roomView != null)
            ExitRoomView();
        else if (_selKind != null)
            Select(null, 0);
        else
            CloseShipDesigner();
    }

    private void Keys(ImGuiIOPtr io)
    {
        var ctrl = io.KeyCtrl || io.KeySuper;
        bool P(ImGuiKey k) => ImGui.IsKeyPressed(k, false);
        if (ctrl && P(ImGuiKey.Z) && !io.KeyShift)
        {
            if (_canUndo)
                Take(_sd!.Undo());
        }
        else if (ctrl && (P(ImGuiKey.Y) || (P(ImGuiKey.Z) && io.KeyShift)))
        {
            if (_canRedo)
                Take(_sd!.Redo());
        }
        else if (ctrl && (P(ImGuiKey.C) || P(ImGuiKey.X)))
        {
            if (_selKind == null)
                return;
            var cut = P(ImGuiKey.X);
            _clip = (_selKind, _selId, false);
            if (cut)
            {
                if (_selKind == "piece")
                    Cmd(new JsonObject { ["op"] = "delPiece", ["id"] = _selId });
                else if (_selKind == "room")
                    Cmd(new JsonObject { ["op"] = "delRoom", ["id"] = _selId });
                else if (_selKind == "hull")
                    Cmd(new JsonObject { ["op"] = "delHull", ["hull"] = _selId });
                _clip = (_selKind, _selId, true);
                Select(null, 0);
            }
            else
                Toast("Copied. Ctrl+V pastes a copy beside it.", true);
        }
        else if (ctrl && P(ImGuiKey.V))
            Paste();
        else if (ctrl && P(ImGuiKey.D) && _selKind != null)
        {
            _clip = (_selKind, _selId, false);
            Paste();
        }
        else if (P(ImGuiKey.Delete) || P(ImGuiKey.Backspace))
        {
            var op = _selKind switch
            {
                "piece" => new JsonObject { ["op"] = "delPiece", ["id"] = _selId },
                "room" => new JsonObject { ["op"] = "delRoom", ["id"] = _selId },
                "join" => new JsonObject { ["op"] = "delJoin", ["id"] = _selId },
                "riser" => new JsonObject { ["op"] = "delRiser", ["id"] = _selId },
                "passage" => new JsonObject { ["op"] = "delPassage", ["id"] = _selId },
                "door" => new JsonObject { ["op"] = "delDoor", ["id"] = _selId },
                "hullwin" => new JsonObject { ["op"] = "delHullWindow", ["id"] = _selId },
                "hull" => new JsonObject { ["op"] = "delHull", ["hull"] = _selId },
                _ => null,
            };
            if (op != null && Str(Cmd(op), "error").Length == 0)
                Select(null, 0);
        }
    }

    private void Paste()
    {
        if (_clip is not { } c)
            return;
        if (c.Cut)
        {
            if (_canUndo)
                Take(_sd!.Undo());
            _clip = (c.Kind, c.Id, false);
            Toast("Pasted back.", true); // (a cut thing keeps its place)
            return;
        }
        var a = c.Kind == "piece" ? Cmd(new JsonObject { ["op"] = "pastePiece", ["id"] = c.Id, ["dz"] = 2 })
            : c.Kind == "room" ? Cmd(new JsonObject { ["op"] = "pasteRoom", ["id"] = c.Id })
            : Cmd(new JsonObject { ["op"] = "copyHull", ["hull"] = c.Id });
        if (Str(a, "error").Length > 0)
            return;
        if (c.Kind == "hull")
            Select("hull", I(Arr(S, "hulls").Last(), "id"));
        else
        {
            Select(c.Kind, I(a, "id"));
            _clip = (c.Kind, I(a, "id"), false);
        }
    }
}
