using System.Numerics;

namespace CallOfSlop;

/// <summary>What the selector's frame reads from the keys (Program.SandboxSelect.cs fills it from the binds).</summary>
public struct SelectInput
{
    public bool FireDown, FirePressed, AimDown, AimPressed, Shift;
    public bool Move, Copy, Paint, Weld, Delete, Save, Reload, SnapSwitch;
    public TurnKey? Turn;
    public Vector3 CamRight, CamUp;
    public float Scroll;
    public long NowMs;
    public Vector2 Screen;
    public Matrix4x4 ViewProj;
    public uint Colour;
    public PaintMode PaintMode;
}

/// <summary>
/// The selector on this PC (docs/sandbox/CONSTRUCTS.md, "Build stations"). Selecting: LMB picks the part under the crosshair,
/// held and dragged it spans a box in the construct's frame, RMB held and dragged a rectangle on the screen, a double-click the
/// whole connected section; Shift adds to the selection, and on what is selected takes away; RMB clicked clears it. Acting: the
/// action keys go to the server with the selection; move and copy first take up a ghost (<see cref="Placing"/>) that follows the
/// crosshair, sits on what is aimed at, snaps to a free socket, and goes down on LMB. Tool-mode state changes on transitions.
/// </summary>
public sealed class SelectController
{
    public enum Placing : byte { None, Move, Copy, Stamp }

    public const long DoubleClickMs = 350;
    public const long ConfirmMs = 4000;
    public const float DragMetres = 0.15f;
    public const float MarqueePixels = 8f;
    public const float Slack = 0.02f; // what a box is grown by: a drag across a flat face has some depth

    public readonly PartSelection Selection = new();

    // Dragging.
    private Construct? _dragBody;
    private Vector3 _dragA, _dragB;
    private float _dragDist;
    private int _dragPart = -1;
    private bool _dragging, _dragBox, _dragRemoves;
    private bool _marquee, _marqueeOn, _marqueeRemoves;
    private Vector2 _mouse;
    private int _pendingMarquee = -1;
    private long _lastClick = long.MinValue;
    private int _lastPart = -1;
    private Construct? _lastBody;
    private long _deleteAt = -1;

    // A box started on open ground: on the world, in a level frame at its first corner (the ground is never part of the selection).
    public const float WorldDragMetres = 0.5f;
    public const float WorldBoxUp = 48f, WorldBoxDown = 2f; // how far it reaches above and below the drag
    public const int MaxBodies = 256; // constructs a selection spans
    private bool _worldDrag;
    private Vector3 _worldA, _worldB;
    private float _worldDist;
    private Quaternion _worldTurn = Quaternion.Identity;

    // Placing.
    public Placing Mode { get; private set; }
    public ConstructClip? Clip { get; private set; }
    private ushort[] _moveItems = [];
    private Construct? _moveBody;
    private byte _turn;
    private Quaternion _frame = Quaternion.Identity;
    private float _holdDist = 8f;
    private byte _spin;
    private int _clipSock;
    private (ushort Part, byte Socket, float Score)[] _clipSockets = [];
    private ClipPlace _place;
    private bool _placeOk;
    private string _placeWhy = "";
    private long _fitKey = long.MinValue;
    private bool _fitOk;
    private string _fitWhy = "";
    private bool[]? _skip;

    // A blueprint on its way.
    private BlueprintLoad? _load;
    private string _loadName = "";
    private long _keepUntil;

    // Survival's cost line.
    private int _costFrame, _missing;
    private readonly Dictionary<ushort, int> _loose = new();

    // Words.
    private long _hintKey = long.MinValue;
    private string _hint = "", _look = "";

    /// <summary>The Delete key's question is up.</summary>
    public bool Asking => _deleteAt >= 0;

    /// <summary>Asked to open the save window (the Ui reads and clears it).</summary>
    public ConstructClip? WantSave;

    /// <summary>The marquee on the screen for the ImGui pass (input only stores it: ImGui is called only between NewFrame and Render).</summary>
    public bool MarqueeShown => _marqueeOn;
    public Vector2 MarqueeA => _screenA;
    public Vector2 MarqueeB => _screenA + _mouse;
    private Vector2 _screenA;

    /// <summary>The world pose the ghost is drawn at (valid while <see cref="Placing"/>).</summary>
    public Vector3 GhostPos { get; private set; }
    public Quaternion GhostRot { get; private set; } = Quaternion.Identity;
    public bool GhostOk => _placeOk;
    public bool GhostOn { get; private set; }

    public void Reset()
    {
        Selection.Clear();
        Idle(null);
    }

    /// <summary>Esc, or anything that backs out: a ghost, or a question. True when there was one (the key is used up).</summary>
    public bool Cancel(Game? g)
    {
        if (Mode != Placing.None || _load != null)
        {
            EndPlacing();
            g?.SandboxSay("cancelled");
            return true;
        }
        if (_deleteAt >= 0)
        {
            _deleteAt = -1;
            return true;
        }
        return false;
    }

    /// <summary>The tool went away: the drag, the ghost and the question end; the selection stays.</summary>
    public void Idle(Game? g)
    {
        _dragging = _marquee = _marqueeOn = _worldDrag = false;
        _pendingMarquee = -1;
        _dragBody = null;
        _deleteAt = -1;
        EndPlacing();
        if (g != null)
            g.SandboxSelectHint = "";
    }

    private void EndPlacing()
    {
        Mode = Placing.None;
        Clip = null;
        _load = null;
        _moveBody = null;
        GhostOn = false;
        _skip = null;
    }

    /// <summary>The mouse while RMB is down with the tool out (the view stays still for the marquee). True: this frame's mouse is used.</summary>
    public bool Mouse(Vector2 delta, bool aimDown)
    {
        if (!aimDown || Mode != Placing.None)
            return false;
        _mouse += delta;
        return true;
    }

    // ---- Every frame, tool out or not ---------------------------------------------------------------------------

    /// <summary>A stamp starts: a saved blueprint's text is built here for its ghost and sent up for the server's own.</summary>
    public string? BeginStamp(Game g, string name)
    {
        string text;
        try
        {
            text = File.ReadAllText(PlayerBlueprints.PathOf(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
        EndPlacing();
        _load = new BlueprintLoad(text, name + ".json");
        _loadName = name;
        _keepUntil = Environment.TickCount64 + 2000; // (the tool's round trip: a pick made before it arrives is kept)
        return null;
    }

    /// <summary>Every frame in Sandbox: a blueprint on its way, and the tool going away.</summary>
    public void Tick(Game g, bool toolOut, long now)
    {
        if (_load is { } load && load.Step())
        {
            _load = null;
            if (load.Clip == null)
                g.SandboxSay($"{_loadName} can't be built: {load.Why}");
            else
            {
                g.SandboxClipUpload(ConstructEdits.Deflate(load.Text));
                StartPlacing(Placing.Stamp, load.Clip, Quaternion.Identity, null);
            }
        }
        if (!toolOut && now >= _keepUntil && (Mode != Placing.None || _dragging || _marquee || _deleteAt >= 0))
            Idle(g);
    }

    private void StartPlacing(Placing mode, ConstructClip clip, Quaternion frame, Construct? moving)
    {
        Mode = mode;
        Clip = clip;
        _frame = frame;
        _turn = 0;
        _spin = 0;
        _clipSock = 0;
        _holdDist = 8f;
        _moveBody = moving;
        _fitKey = long.MinValue;
        _costFrame = 0;
        _clipSockets = SocketsOf(clip);
    }

    /// <summary>The clip's free structural sockets, those on its outside first: the ones that make sense to attach by.</summary>
    private static (ushort, byte, float)[] SocketsOf(ConstructClip clip)
    {
        var c = clip.Body;
        var list = new List<(ushort, byte, float)>();
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Seg != 0)
                continue;
            var d = c.DefOf(i);
            for (var s = 0; s < d.Sockets.Length; s++)
            {
                if (d.Sockets[s].Kind != SocketKind.Struct || c.SocketUsed(i, s) || d.HasJoint && s == d.LeafIndex)
                    continue;
                c.SocketAt(i, s, out var at, out var dir);
                list.Add(((ushort)i, (byte)s, Vector3.Dot(at - clip.Origin, ConstructParts.DirVector(dir))));
            }
        }
        list.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        return list.ToArray();
    }

    // ---- The tool's frame ---------------------------------------------------------------------------------------

    /// <summary>One frame with the tool out and the mouse captured. The hit is the crosshair's own, out to <see cref="ConstructEdits.AidReach"/>.</summary>
    public void Frame(Game g, Player me, in SelectInput inp, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, bool surface, Vector3 point, Vector3 normal)
    {
        Selection.Check(g.Constructs);
        ConstructRender.PartSel = Selection;
        if (Mode != Placing.None && Clip != null)
        {
            PlaceFrame(g, me, in inp, eye, dir, aimed, in hit, surface, point, normal);
            return;
        }
        var off = g.SandboxAidOff(BuildAid.Select);
        if (off == null)
        {
            WorldDrag(g, me, in inp, eye, dir, aimed, in hit, surface, point);
            SelectFrame(g, in inp, eye, dir, aimed, in hit);
            ActionKeys(g, me, in inp);
        }
        Words(g, off, null);
    }

    /// <summary>A drag started on open ground: a box on the world, from the ground up, for many constructs at once.</summary>
    private void WorldDrag(Game g, Player me, in SelectInput inp, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, bool surface, Vector3 point)
    {
        if (inp.FirePressed && !aimed)
        {
            _worldDist = surface ? Vector3.Distance(eye, point) : ConstructEdits.AidReach * 0.5f;
            _worldDrag = true;
            _worldA = _worldB = surface ? point : eye + dir * _worldDist;
            _worldTurn = GravityFrames.Curved ? Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(GravityFrames.Basis(_worldA))) : Quaternion.Identity;
        }
        if (!_worldDrag)
            return;
        var inv = Quaternion.Conjugate(_worldTurn);
        if (inp.FireDown)
            _worldB = surface ? point : aimed ? hit.Point : eye + dir * _worldDist;
        var a = Vector3.Transform(_worldA, inv);
        var b = Vector3.Transform(_worldB, inv);
        var min = Vector3.Min(a, b) - new Vector3(0, WorldBoxDown, 0);
        var max = Vector3.Max(a, b) + new Vector3(0, WorldBoxUp, 0);
        var far = (a - b) with { Y = 0 };
        var dragged = far.LengthSquared() > WorldDragMetres * WorldDragMetres;
        if (inp.FireDown)
        {
            ConstructRender.SelWorldOn = dragged;
            ConstructRender.SelWorldTurn = _worldTurn;
            ConstructRender.SelBoxMin = min;
            ConstructRender.SelBoxMax = max;
            return;
        }
        _worldDrag = false;
        if (!dragged)
            return;
        var creative = g.SandboxInfiniteParts;
        var player = me.Id;
        var reachFrom = eye;
        var n = Selection.AddWorldBox(g.Constructs, _worldTurn, min, max, inp.Shift, c => Vector3.Distance(reachFrom, c.WorldCom) <= ConstructEdits.AidReach + c.Radius && ConstructEdits.RemoverRefusal(c, player, creative, null) == null, MaxBodies);
        if (n >= MaxBodies)
            g.SandboxSay($"at most {MaxBodies} constructs at once");
    }

    private void SelectFrame(Game g, in SelectInput inp, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit)
    {
        if (_deleteAt >= 0 && inp.NowMs - _deleteAt > ConfirmMs)
            _deleteAt = -1;
        // The press.
        if (inp.FirePressed)
        {
            _dragging = true;
            _dragBox = false;
            _dragBody = aimed ? hit.Body : null;
            _dragPart = aimed ? hit.Part : -1;
            _dragRemoves = aimed && inp.Shift && Selection.Body == hit.Body && Selection.Contains(hit.Part);
            if (aimed)
            {
                _dragA = _dragB = hit.Body.ToLocal(hit.Point);
                _dragDist = hit.Distance;
            }
        }
        if (_dragging && inp.FireDown && _dragBody is { } db)
        {
            _dragB = db.ToLocal(aimed && hit.Body == db ? hit.Point : eye + dir * _dragDist);
            if (Vector3.DistanceSquared(_dragA, _dragB) > DragMetres * DragMetres)
                _dragBox = true;
            ConstructRender.SelBoxBody = _dragBox ? db : null;
            ConstructRender.SelBoxMin = Vector3.Min(_dragA, _dragB) - new Vector3(Slack);
            ConstructRender.SelBoxMax = Vector3.Max(_dragA, _dragB) + new Vector3(Slack);
        }
        else
            ConstructRender.SelBoxBody = null;
        // The release.
        if (_dragging && !inp.FireDown)
        {
            _dragging = false;
            if (_dragBody is { } b && g.Constructs.IsLive(b))
            {
                if (_dragBox)
                    Selection.AddBox(b, Vector3.Min(_dragA, _dragB) - new Vector3(Slack), Vector3.Max(_dragA, _dragB) + new Vector3(Slack), inp.Shift && !_dragRemoves, _dragRemoves);
                else
                    Click(b, _dragPart, inp.Shift, inp.NowMs);
            }
            else if (!inp.Shift && _dragBody == null)
                _lastBody = null;
            _dragBody = null;
        }
        // RMB: held and dragged a marquee, clicked it clears.
        if (inp.AimPressed)
        {
            _marquee = true;
            _marqueeOn = false;
            _mouse = Vector2.Zero;
            _screenA = inp.Screen * 0.5f;
            _pendingMarquee = aimed ? hit.Body.Id : -1;
            _marqueeRemoves = inp.Shift && aimed && Selection.Body == hit.Body && Selection.Contains(hit.Part);
        }
        if (_marquee && inp.AimDown && _mouse.LengthSquared() > MarqueePixels * MarqueePixels)
            _marqueeOn = true;
        if (_marquee && !inp.AimDown)
        {
            _marquee = false;
            if (_marqueeOn)
                Marquee(g, in inp);
            else if (Selection.Count > 0)
            {
                Selection.Clear();
                g.SandboxSay("selection cleared");
            }
            _marqueeOn = false;
        }
    }

    private void Click(Construct c, int part, bool shift, long now)
    {
        if (part < 0 || part >= c.PartCount)
            return;
        var twice = now - _lastClick <= DoubleClickMs && _lastPart == part && ReferenceEquals(_lastBody, c);
        _lastClick = twice ? long.MinValue : now;
        _lastPart = part;
        _lastBody = c;
        if (twice)
        {
            if (!shift)
                Selection.Clear();
            Selection.AddSection(c, part, shift && Selection.Contains(part));
            return;
        }
        if (shift)
        {
            var into = Selection.For(c, true)!; // (Shift reaches other constructs too)
            if (!into.Remove(part))
                into.Add(c, part);
            return;
        }
        Selection.Clear();
        Selection.Add(c, part);
    }

    private void Marquee(Game g, in SelectInput inp)
    {
        var a = MarqueeA;
        var b = MarqueeB;
        Construct? best = _pendingMarquee > 0 ? g.Constructs.Get((ushort)_pendingMarquee) : Selection.Body;
        if (best == null)
        {
            // Nothing under the crosshair at the press: the construct with the most parts in the rectangle.
            var most = 0;
            foreach (var c in g.Constructs.Bodies)
            {
                if (Vector3.Distance(c.WorldCom, g.Local?.Eye ?? c.WorldCom) > ConstructEdits.AidReach + c.Radius)
                    continue;
                var n = Selection.AddScreen(c, inp.ViewProj, inp.Screen, a, b, false, false, countOnly: true);
                if (n > most)
                {
                    most = n;
                    best = c;
                }
            }
        }
        if (best != null)
            Selection.AddScreen(best, inp.ViewProj, inp.Screen, a, b, inp.Shift && !_marqueeRemoves, _marqueeRemoves);
    }

    private void ActionKeys(Game g, Player me, in SelectInput inp)
    {
        if (Selection.Extra.Count > 0)
        {
            ManyKeys(g, in inp);
            return;
        }
        if (Selection.Body is not { } c || Selection.Count == 0)
        {
            if (inp.Move || inp.Copy || inp.Paint || inp.Weld || inp.Delete || inp.Save)
                g.SandboxSay("nothing selected");
            return;
        }
        if (inp.Delete)
        {
            if (_deleteAt < 0)
            {
                _deleteAt = inp.NowMs;
                return;
            }
            _deleteAt = -1;
            Send(g, c);
            g.SandboxSelectDo(SelAct.Delete);
            Selection.Clear();
            return;
        }
        if (inp.Paint)
        {
            Send(g, c);
            g.SandboxSelectDo(SelAct.Paint, inp.PaintMode, inp.Colour);
        }
        else if (inp.Weld)
        {
            Send(g, c);
            g.SandboxSelectDo(SelAct.Weld);
        }
        else if (inp.Copy || inp.Move)
        {
            var clip = ConstructClip.Capture(c, Selection.Items, out var why);
            if (clip == null)
            {
                g.SandboxSay(why);
                return;
            }
            if (inp.Copy)
            {
                Send(g, c);
                g.SandboxSelectDo(SelAct.Copy);
            }
            else
            {
                _moveItems = Selection.Items.ToArray();
                _skip = new bool[c.PartCount];
                foreach (var i in _moveItems)
                    _skip[i] = true;
            }
            StartPlacing(inp.Copy ? Placing.Copy : Placing.Move, clip, c.Rot, inp.Move ? c : null);
        }
        else if (inp.Save)
        {
            if (g.SandboxAidOff(BuildAid.Blueprint) is { } off)
            {
                g.SandboxSay(off);
                return;
            }
            var clip = ConstructClip.Capture(c, Selection.Items, out var why);
            if (clip == null)
                g.SandboxSay(why);
            else
                WantSave = clip;
        }
    }

    private void Send(Game g, Construct c) => g.SandboxSelectSend(c, Selection.Items);

    /// <summary>A selection over many constructs: paint, weld and delete go to each in turn; a group to move, copy or save is one construct's.</summary>
    private void ManyKeys(Game g, in SelectInput inp)
    {
        if (inp.Move || inp.Copy || inp.Save)
        {
            g.SandboxSay("Move, copy and save work on one construct: this selection is on several");
            return;
        }
        if (inp.Delete && _deleteAt < 0)
        {
            _deleteAt = inp.NowMs;
            return;
        }
        if (!inp.Delete && !inp.Paint && !inp.Weld)
            return;
        var delete = inp.Delete;
        _deleteAt = -1;
        void One(PartSelection s, in SelectInput keys)
        {
            g.SandboxSelectSend(s.Body!, s.Items);
            if (delete)
                g.SandboxSelectDo(SelAct.Delete);
            else if (keys.Paint)
                g.SandboxSelectDo(SelAct.Paint, keys.PaintMode, keys.Colour);
            else
                g.SandboxSelectDo(SelAct.Weld);
        }
        One(Selection, in inp);
        foreach (var e in Selection.Extra)
            One(e, in inp);
        if (delete)
            Selection.Clear();
    }

    // ---- Placing ------------------------------------------------------------------------------------------------

    private void PlaceFrame(Game g, Player me, in SelectInput inp, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, bool surface, Vector3 point, Vector3 normal)
    {
        var clip = Clip!;
        if (Mode == Placing.Move && (_moveBody == null || !g.Constructs.IsLive(_moveBody) || Selection.Count != _moveItems.Length))
        {
            g.SandboxSay("the group changed: select again");
            EndPlacing();
            return;
        }
        var aid = Mode == Placing.Stamp ? BuildAid.Blueprint : BuildAid.Select;
        var off = g.SandboxAidOff(aid);
        if (inp.AimPressed)
        {
            EndPlacing();
            return;
        }
        if (inp.SnapSwitch && _clipSockets.Length > 0)
        {
            _clipSock = (_clipSock + 1) % _clipSockets.Length;
            _fitKey = long.MinValue;
        }
        if (inp.Reload)
        {
            _spin = (byte)((_spin + 1) & 3);
            _fitKey = long.MinValue;
        }
        if (inp.Scroll != 0)
            _holdDist = Math.Clamp(_holdDist + inp.Scroll * MathF.Max(0.25f, _holdDist * 0.12f), 2f, ConstructEdits.AidReach - 1f);
        var attach = aimed && TryAttach(g, in hit);
        if (!attach)
        {
            if (inp.Reload)
                _turn = Rot24.Compose(GhostTurn.Delta(TurnKey.Left, _frame, inp.CamRight, inp.CamUp), _turn);
            if (inp.Turn is { } key)
                _turn = Rot24.Compose(GhostTurn.Delta(key, _frame, inp.CamRight, inp.CamUp), _turn);
            FreePose(clip, eye, dir, surface || aimed, surface ? point : hit.Point, surface ? normal : hit.Normal);
            _placeOk = true;
            _placeWhy = "";
        }
        GhostOn = true;
        if (off != null)
        {
            _placeOk = false;
            _placeWhy = off;
        }
        else if (inp.FirePressed && _placeOk)
            Commit(g);
        Cost(g, me, clip, eye, inp.NowMs);
        Words(g, off, _placeOk ? null : _placeWhy);
    }

    private void Commit(Game g)
    {
        if (Mode == Placing.Move)
        {
            g.SandboxSelectSend(_moveBody!, _moveItems);
            g.SandboxSelectDo(SelAct.Move, place: in _place);
            EndPlacing();
            Selection.Clear();
            return;
        }
        g.SandboxPaste(in _place); // (the ghost stays: place another, or RMB to stop)
    }

    private void FreePose(ConstructClip clip, Vector3 eye, Vector3 dir, bool onSurface, Vector3 at, Vector3 normal)
    {
        if (Mode == Placing.Stamp && GravityFrames.Curved)
            _frame = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(GravityFrames.Basis(onSurface ? at : eye))); // (a planet: upright where it goes)
        var rot = Quaternion.Normalize(_frame * Rot24.Quat(_turn));
        var half = (clip.Max - clip.Min) * 0.5f;
        Vector3 pos;
        if (onSurface && Vector3.Distance(eye, at) <= ConstructEdits.AidReach - 1f)
        {
            // Resting on the surface aimed at: its extent along the normal, a hair clear.
            var inv = Vector3.Transform(normal, Quaternion.Conjugate(rot));
            pos = at + normal * (MathF.Abs(inv.X) * half.X + MathF.Abs(inv.Y) * half.Y + MathF.Abs(inv.Z) * half.Z + 0.02f);
        }
        else
            pos = eye + dir * _holdDist;
        _place = new ClipPlace { Pos = pos, Rot = rot };
        GhostRot = rot;
        GhostPos = ConstructClip.BodyPos(clip.Origin, pos, rot);
    }

    /// <summary>The aimed part's free socket nearest the crosshair, with the clip's current socket onto it. Checked once per target (it walks every part).</summary>
    private bool TryAttach(Game g, in ConstructHit hit)
    {
        var clip = Clip!;
        var t = hit.Body;
        if (hit.Part < 0 || hit.Part >= t.PartCount || t.Parts[hit.Part].Seg != 0 || _clipSockets.Length == 0 || ReferenceEquals(t, _moveBody) && Selection.Contains(hit.Part))
            return false;
        var d = t.DefOf(hit.Part);
        var best = -1;
        var bestD = float.MaxValue;
        var local = t.ToLocal(hit.Point);
        for (var s = 0; s < d.Sockets.Length; s++)
        {
            if (d.Sockets[s].Kind != SocketKind.Struct || t.SocketUsed(hit.Part, s) || d.HasJoint && s == d.LeafIndex)
                continue;
            t.SocketAt(hit.Part, s, out var at, out _);
            var dist = Vector3.DistanceSquared(at, local);
            if (dist < bestD)
            {
                bestD = dist;
                best = s;
            }
        }
        if (best < 0)
            return false;
        // The clip's socket: from the current one on, the first of the same class.
        var cls = d.Sockets[best].Class;
        var pick = -1;
        for (var k = 0; k < _clipSockets.Length && pick < 0; k++)
        {
            var at = (_clipSock + k) % _clipSockets.Length;
            var (cp, cs, _) = _clipSockets[at];
            if (clip.Body.DefOf(cp).Sockets[cs].Class == cls)
                pick = at;
        }
        if (pick < 0)
            return false;
        var (part, socket, _) = _clipSockets[pick];
        if (clip.AttachPose(t, hit.Part, best, part, socket, _spin, out var turn, out var offset) is not null)
            return false;
        var key = ((long)t.Id << 40) ^ ((long)hit.Part << 24) ^ ((long)best << 16) ^ ((long)pick << 3) ^ _spin ^ ((long)t.PartCount << 48);
        if (key != _fitKey)
        {
            _fitKey = key;
            _fitOk = !clip.Overlaps(t, turn, offset, ReferenceEquals(t, _moveBody) ? _skip : null);
            _fitWhy = _fitOk ? "" : "something is in the way";
        }
        _place = new ClipPlace { Attach = true, Body = t.Id, Part = (ushort)hit.Part, Socket = (byte)best, ClipPart = part, ClipSocket = socket, Spin = _spin };
        _placeOk = _fitOk;
        _placeWhy = _fitWhy;
        GhostRot = Quaternion.Normalize(t.Rot * Rot24.Quat(turn));
        GhostPos = t.Pos + Vector3.Transform(offset, t.Rot);
        return true;
    }

    /// <summary>Survival: how many of the clip's parts are out of reach, looked at five times a second.</summary>
    private void Cost(Game g, Player me, ConstructClip clip, Vector3 eye, long now)
    {
        if (g.SandboxInfiniteParts || Mode == Placing.Move)
        {
            _missing = 0;
            return;
        }
        if (--_costFrame > 0)
            return;
        _costFrame = 12;
        var view = g.SandboxBeaconOn ? g.SandboxBeacon(me.Id) : null;
        var inv = g.SandboxInv.Of(me.Id);
        _loose.Clear();
        foreach (var b in g.Constructs.Bodies)
            if (ConstructEdits.LooseFor(b, me.Id, eye, out var d))
                _loose[d.Id] = _loose.GetValueOrDefault(d.Id) + 1;
        _missing = 0;
        foreach (var (id, n) in clip.Cost())
            if (ConstructParts.Get(id) is { } d)
                _missing += Math.Max(0, n - ConstructEdits.ClipHave(d, view, inv, _loose.GetValueOrDefault(id)));
        _ = now;
    }

    // ---- Words --------------------------------------------------------------------------------------------------

    private void Words(Game g, string? off, string? problem)
    {
        var key = HashCode.Combine(Selection.Total * 300 + Selection.Extra.Count, (int)Mode, _missing, off?.GetHashCode() ?? 0, problem?.GetHashCode() ?? 0, _deleteAt >= 0, SandboxKeys.Stamp, _dragBox && _dragging);
        if (key != _hintKey)
        {
            _hintKey = key;
            var parts = Clip?.Parts ?? Selection.Total;
            var what = $"{parts} part{(parts == 1 ? "" : "s")}";
            if (Mode != Placing.None)
            {
                var name = Mode == Placing.Move ? "Move" : Mode == Placing.Copy ? "Copy" : "Stamp";
                var cost = _missing > 0 ? ConstructEdits.MissingLine(_missing, parts) : null;
                _look = SandboxKeys.Line(name, what, off ?? problem ?? cost);
                _hint = $"{SandboxKeys.Fire}: place, {SandboxKeys.Reload}: turn, arrows: tip, {SandboxKeys.Switch}: next socket, wheel: distance, {SandboxKeys.Aim}: cancel";
            }
            else if (off != null)
            {
                _look = SandboxKeys.Line("Selector", "off", off);
                _hint = "";
            }
            else if (Selection.Extra.Count > 0)
            {
                var bodies = $"{what} on {Selection.Extra.Count + 1} constructs";
                _look = _deleteAt >= 0 ? SandboxKeys.Line("Selector", bodies, null, $"{SandboxKeys.Drop} again: delete them") : SandboxKeys.Line("Selector", bodies + " selected");
                _hint = $"{SandboxKeys.SelPaint}: paint, {SandboxKeys.SelWeld}: weld, {SandboxKeys.Drop}: delete, {SandboxKeys.Aim}: clear";
            }
            else if (Selection.Count == 0)
            {
                _look = "";
                _hint = $"{SandboxKeys.Fire}: pick, drag: box (from the ground: many constructs), double-click: section, {SandboxKeys.Aim} drag: marquee, Shift: add or take away";
            }
            else
            {
                _look = _deleteAt >= 0 ? SandboxKeys.Line("Selector", what, null, $"{SandboxKeys.Drop} again: delete them") : SandboxKeys.Line("Selector", what + " selected");
                _hint = $"{SandboxKeys.SelMove}: move, {SandboxKeys.SelCopy}: copy, {SandboxKeys.SelPaint}: paint, {SandboxKeys.SelWeld}: attach, {SandboxKeys.Drop}: delete, {SandboxKeys.SelSave}: save, {SandboxKeys.Aim}: clear";
            }
        }
        if (_look.Length > 0)
            g.SandboxLookText = _look;
        g.SandboxSelectHint = _hint;
    }
}
