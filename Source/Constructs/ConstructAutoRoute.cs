using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>What the linking tool asks the auto-router for: a link so far (its start and nodes) and where it should go next.</summary>
public struct RouteAsk
{
    public ConstructWorld? World;
    public Construct C;
    public RouteType Type;
    public int From; // the output (when Parent is 255)
    public byte FromPort;
    public int Parent, ParentSeg; // a branch: the link and its segment (255: from a part)
    public Vector3 Junction;
    public CRouteNode[] Nodes; // the nodes so far (Count of them)
    public int Count;
    public int To; // the input it ends at (with ToPort), or -1: a waypoint at OpenAt, on part OpenHost
    public byte ToPort;
    public Vector3 OpenAt;
    public ushort OpenHost;
}

/// <summary>
/// Auto-routing for the linking tool (docs/sandbox/CONSTRUCTS.md, "Routed links", "Auto-routing"): given a link so far and an
/// input (or a waypoint), finds nodes that go round the parts and pass the one path rule (<see cref="ConstructRoutes.PathRule"/>).
/// Client only: it proposes nodes and the server checks them as it checks a manual route, so nothing new is trusted or sent.
///
/// Cables, pipes and chutes run on a lattice of the box faces' planes (a Hanan grid: the planes one link radius off each
/// box, plus the two ends', thinned to <see cref="MaxPoints"/> points), searched by A* over (point, direction) with a cost per
/// bend, a discount along surfaces, a bigger one one link-width beside another link or in a tray's lane, and a charge for sharing another link's run, so routes come out
/// square (every 90 degree bend suits the pipe's 15 and the chute's 45 degree steps). A shaft can't turn 90 degrees, so it
/// searches an even lattice with the 18 directions a 45 degree limit can step between, and gives up rather than bend more
/// than <see cref="ShaftMaxBends"/> times: that is what a gearbox is for.
///
/// A search is one state machine, stepped a few milliseconds a frame (<see cref="Poll"/>): the fixed tables are made once and
/// nothing allocates after that, a result is kept until the construct is rebuilt (<see cref="Construct.RebuildStamp"/> is in
/// the key), and a construct changed under a search restarts it. Each Step takes the construct afresh, and a result's host
/// parts are only used through the checks the caller runs on them (the stale index lesson).
/// </summary>
public sealed class RouteSearch
{
    public const int MaxPoints = 1 << 20; // lattice points (2 MB, about 100 planes an axis)
    public const int MaxStates = 1 << 18; // points with a direction reached (a search that needs more gives up)
    public const int MaxExpansions = 200_000; // states searched in one stage
    public const int ShaftMaxBends = 4; // a shaft that needs more bends wants a gearbox, not a worse route
    public const float BudgetMs = 2f; // the search's share of a frame
    public const float MaxReach = 24f; // the longest stretch it searches: a link has no limit, but further than this the player lays a node on the way and it carries on from there
    private const int Marks = 8192; // cm along an axis of the search box (82 m, for the MaxReach stretches)
    private const int PerAxis = 400, MaxPath = 2048;
    private const int HashSize = 1 << 19, HashMask = HashSize - 1, HeapSize = 1 << 19;
    private const int BlockBit = 1, HugBit = 8, SoftBit = 64, TargetBit = 512, ReachBit = 1024, BesideBit = 2048;
    private const float BendCost = 0.4f, HugFactor = 0.8f, BesideFactor = 0.5f, SoftFactor = 2.5f, HugGap = 0.03f, ShaftBendCost = 1.2f, SideCost = 0.6f;
    private const float StubMin = 0.1f; // a run at least this long between bends

    /// <summary>What a search came to: its state, the nodes it adds to the link and why it failed.</summary>
    public sealed class Result
    {
        public long Key;
        public RouteSearchState State;
        public string? Why;
        public readonly CRouteNode[] Nodes = new CRouteNode[Construct.MaxNodesPerRoute];
        public int Count;
        public float Length; // the whole link's length
        internal long Age;
    }

    private readonly Result[] _cache = new Result[8];
    private long _age;
    private readonly Result _busy = new() { State = RouteSearchState.Busy };

    // (made the first time the tool is used: a player who never links never pays for them)
    private ushort[] _grid = [];
    private int[] _hash = [], _hState = [];
    private int[] _sKey = [], _sParent = [];
    private float[] _sG = [], _sLen = [], _sRun = [];
    private byte[] _sBends = [];
    private float[] _heapF = [];
    private int[] _heapS = [];
    private int[] _parts = [], _stack = [];
    private byte[][] _marks = [];
    private float[][] _lat = [];
    private Vector3[] _path = [];
    private CRouteNode[] _chk = [];
    private readonly int[] _tgtPoint = new int[256];
    private readonly byte[] _tgtDir = new byte[256];
    private readonly float[] _tgtT = new float[256];

    private enum Phase : byte
    {
        Idle,
        Gather,
        Raster,
        Flood,
        Search,
    }

    // Each stage looks further round, on the construct's 5 cm grid; the last goes everywhere on a finer lattice, with links half sunk
    // into the surfaces they pass (the embedding the path rule forgives), for the narrow slots.
    private static readonly (float Margin, float Clear, int Gap)[] FlatStages = [(1f, 1f, 5), (4f, 1f, 5), (12f, 1f, 5), (12f, 0f, 2)];
    private static readonly (float Margin, float Step)[] ShaftStages = [(0.8f, 0.1f), (2.5f, 0.15f), (6f, 0.25f)];

    private Phase _phase;
    private RouteAsk _ask;
    private long _key;
    private int _stage, _partN, _partAt, _stackN, _heapN, _stateN, _expanded, _tgtN, _analytic;
    private float _radius, _clear, _maxLen, _alongSoFar;
    private float _hFactor = 0.92f; // the search's guess of the cost a metre: lower with runs to lie beside, or it would never look at the way round to them
    private Vector3 _s, _g, _sDir, _gDir, _min, _max;
    private bool _closed, _flat, _hasPrev, _longWay, _gaveUp;
    private int _frame, _startHost, _goalHost, _msS, _msG;
    private bool _half; // the goal is a suspension's Drive in
    private Vector3 _prevDir;
    private float _firstExit; // (with nodes: where the grace from the output has got to)
    private readonly int[] _n = new int[3], _stride = new int[3];
    private float _step;
    private readonly int[] _sAt = new int[3]; // (the even lattice: where the start is on each axis)
    private int _bestState = -1, _bestTarget = -1;
    private float _bestCost;
    private string? _lastWhy;

    private static readonly int[,] Dir18 = MakeDirs();
    private static readonly float[] Dir18Len = new float[18];
    private static readonly int[][] Turn18 = MakeTurns();

    private static int[,] MakeDirs()
    {
        var d = new int[18, 3];
        var n = 0;
        for (var a = 0; a < 3; a++)
            for (var s = -1; s <= 1; s += 2)
            {
                d[n, a] = s;
                n++;
            }
        for (var a = 0; a < 3; a++)
            for (var b = a + 1; b < 3; b++)
                for (var s = -1; s <= 1; s += 2)
                    for (var t = -1; t <= 1; t += 2)
                    {
                        d[n, a] = s;
                        d[n, b] = t;
                        n++;
                    }
        return d;
    }

    private static int[][] MakeTurns()
    {
        var turns = new int[18][];
        for (var i = 0; i < 18; i++)
        {
            Dir18Len[i] = Dir(i).Length();
            var list = new List<int>();
            for (var j = 0; j < 18; j++)
                if (j != i && Vector3.Dot(Vector3.Normalize(Dir(i)), Vector3.Normalize(Dir(j))) > MathF.Cos(46f * MathF.PI / 180f))
                    list.Add(j);
            turns[i] = list.ToArray();
        }
        return turns;
    }

    private static Vector3 Dir(int i) => new(Dir18[i, 0], Dir18[i, 1], Dir18[i, 2]);

    public RouteSearch()
    {
        for (var i = 0; i < _cache.Length; i++)
            _cache[i] = new Result();
    }

    private void MakeTables()
    {
        if (_grid.Length > 0)
            return;
        _grid = new ushort[MaxPoints];
        _hash = new int[HashSize];
        _hState = new int[HashSize];
        _sKey = new int[MaxStates];
        _sParent = new int[MaxStates];
        _sG = new float[MaxStates];
        _sLen = new float[MaxStates];
        _sRun = new float[MaxStates];
        _sBends = new byte[MaxStates];
        _heapF = new float[HeapSize];
        _heapS = new int[HeapSize];
        _parts = new int[Construct.MaxParts];
        _stack = new int[MaxPoints];
        _marks = [new byte[Marks], new byte[Marks], new byte[Marks]];
        _lat = [new float[PerAxis], new float[PerAxis], new float[PerAxis]];
        _path = new Vector3[MaxPath];
        _chk = new CRouteNode[Construct.MaxNodesPerRoute * 2];
    }

    /// <summary>Drops the search running (results stay until the construct changes).</summary>
    public void Cancel() => _phase = Phase.Idle;

    /// <summary>Forgets everything (a self-test's start).</summary>
    public void Clear()
    {
        Cancel();
        foreach (var r in _cache)
            r.Key = 0;
    }

    private static long Mix(long h, long v) => (h ^ v) * unchecked((long)0x9E3779B97F4A7C15L) + (h >>> 29);

    private static long Mix(long h, Vector3 v) => Mix(Mix(Mix(h, BitConverter.SingleToInt32Bits(v.X)), BitConverter.SingleToInt32Bits(v.Y)), BitConverter.SingleToInt32Bits(v.Z));

    private static long KeyOf(in RouteAsk a)
    {
        var h = Mix(Mix(Mix(1234567L, a.C.Id), a.C.RebuildStamp), (long)a.Type);
        h = Mix(Mix(Mix(Mix(h, a.From), a.FromPort), a.Parent), a.ParentSeg);
        h = Mix(Mix(Mix(Mix(h, a.Junction), a.Count), a.To), a.ToPort);
        h = Mix(Mix(Mix(h, a.OpenAt), a.OpenHost), a.C.RouteCount);
        for (var i = 0; i < a.Count; i++)
            h = Mix(Mix(h, a.Nodes[i].Pos), a.Nodes[i].Host);
        return h == 0 ? 1 : h;
    }

    /// <summary>
    /// The route for a link and a goal, as far as it has got: gets on with the search for at most <paramref name="budgetMs"/>,
    /// from where it stood if the ask is the same, from the start if not. A finished result is kept (a few of them, so aiming
    /// from one port to the next and back searches nothing) until the construct is rebuilt.
    /// </summary>
    public Result Poll(in RouteAsk ask, float budgetMs = BudgetMs)
    {
        var key = KeyOf(ask);
        foreach (var r in _cache)
            if (r.Key == key)
            {
                r.Age = ++_age;
                return r;
            }
        if (_phase == Phase.Idle || _key != key)
            Begin(ask, key);
        else
            _ask = ask;
        return _phase == Phase.Idle ? Stored(key) : Step(budgetMs) ?? _busy;
    }

    private Result Stored(long key)
    {
        foreach (var r in _cache)
            if (r.Key == key)
                return r;
        return _busy;
    }

    private Result Done(RouteSearchState state, string? why, int count = 0, float length = 0f)
    {
        _phase = Phase.Idle;
        var slot = _cache[0];
        foreach (var r in _cache)
            if (r.Age < slot.Age)
                slot = r;
        slot.Key = _key;
        slot.State = state;
        slot.Why = why;
        slot.Count = count;
        slot.Length = length;
        slot.Age = ++_age;
        for (var i = 0; i < count; i++)
            slot.Nodes[i] = _chk[_ask.Count + i];
        return slot;
    }

    // ---- The start: the ends, the frame, what can't work at all -------------------------------------------------------

    private void Begin(in RouteAsk a, long key)
    {
        MakeTables();
        _ask = a;
        _key = key;
        var c = a.C;
        _closed = a.To >= 0;
        _flat = a.Type != RouteType.Shaft;
        _radius = ConstructRoutes.Radius(a.Type);
        _startHost = a.Parent != 255 ? ConstructRoutes.HostOf(c, a.Parent, a.ParentSeg) : a.From;
        _goalHost = _closed ? a.To : a.OpenHost;
        _stage = 0;
        _lastWhy = null;
        _longWay = _gaveUp = false;
        if (_startHost < 0 || _startHost >= c.PartCount || _goalHost < 0 || _goalHost >= c.PartCount)
        {
            Done(RouteSearchState.Failed, "that part is gone");
            return;
        }
        // The frame the path rule will check in (board item fd01): one frame when every point shares it, else the body's.
        var seg0 = c.Parts[_startHost].Seg;
        var f = c.FreeformCount > 0 ? c.FrameOf(seg0) : 0;
        if (c.FreeformCount > 0)
        {
            var same = c.FrameOf(c.Parts[_goalHost].Seg) == f;
            for (var i = 0; i < a.Count && same; i++)
                same = a.Nodes[i].Host < c.PartCount && c.FrameOf(c.Parts[a.Nodes[i].Host].Seg) == f;
            if (!same)
                f = 0;
        }
        _frame = f;
        _msS = c.MoveSeg(seg0);
        _msG = c.MoveSeg(c.Parts[_goalHost].Seg);
        _half = !_flat && _closed && ConstructRoutes.IsHalfShaft(c, a.To); // (a shaft ending on a suspension: ConstructHalfShaft.cs)
        if (_half && ConstructRoutes.HalfShaftCross(c, _startHost, a.To) is { } crossWhy)
        {
            Done(RouteSearchState.Failed, crossWhy);
            return;
        }
        if (!_flat && !_half && _msS != _msG && c.Parts[_goalHost].D.Rotor == null) // (a tilting rotor's shaft ends over its joint: ConstructRotor.cs)
        {
            Done(RouteSearchState.Failed, "a drive shaft can't cross a joint: fit a U joint or CV joint there");
            return;
        }
        // The start and the way the link leaves it.
        var start = a.Parent != 255 ? a.Junction : ConstructRoutes.PortPoint(c, a.From, a.FromPort, a.Type);
        var at = ToWork(seg0, start);
        var first = at;
        var prev = at;
        var before = at;
        var along = 0f;
        for (var i = 0; i < a.Count; i++)
        {
            var p = ToWork(c.Parts[Math.Min((int)a.Nodes[i].Host, c.PartCount - 1)].Seg, a.Nodes[i].Pos);
            along += Vector3.Distance(prev, p);
            before = prev;
            prev = p;
        }
        _alongSoFar = along;
        _maxLen = _half ? ConstructRoutes.HalfShaftReach(c.DefOf(a.To)) - along : MaxReach;
        _s = prev;
        _hasPrev = a.Count > 0;
        _prevDir = a.Count > 0 && Vector3.DistanceSquared(prev, before) > 1e-6f ? Vector3.Normalize(prev - before) : Vector3.Zero;
        _sDir = a.Count == 0 && a.Parent == 255 ? PortDirWork(_startHost, a.FromPort) : Vector3.Zero;
        if (!_flat && a.Count == 0 && _sDir != Vector3.Zero)
            _prevDir = _sDir;
        _firstExit = 0f;
        if (a.Count > 0 && a.Parent == 255)
            _firstExit = ConstructRoutes.ExitAlongIn(c, _startHost, first, ToWork(c.Parts[Math.Min((int)a.Nodes[0].Host, c.PartCount - 1)].Seg, a.Nodes[0].Pos), _frame == 0 && c.FreeformCount > 0);
        _g = _closed ? ToWork(c.Parts[a.To].Seg, ConstructRoutes.PortPoint(c, a.To, a.ToPort, a.Type)) : ToWork(c.Parts[a.OpenHost].Seg, a.OpenAt);
        _gDir = _closed ? PortDirWork(a.To, a.ToPort) : Vector3.Zero;
        if (Vector3.Distance(_s, _g) > _maxLen)
        {
            Done(RouteSearchState.Failed, _half ? $"too far: half shaft reaches {ConstructRoutes.HalfShaftReach(c.DefOf(a.To)):0.#} m" : TooFarToSearch);
            return;
        }
        if (_closed && !_flat && _gDir == Vector3.Zero)
        {
            Done(RouteSearchState.Failed, "a drive shaft goes into a drive port");
            return;
        }
        BeginStage();
    }

    private Vector3 ToWork(int seg, Vector3 p) => _frame == 0 ? _ask.C.ToRest(seg, p) : p;

    private Vector3 PortDirWork(int part, byte port)
    {
        var d = _flat ? ConstructRoutes.PortDir(_ask.C, part, port, _ask.Type) : ConstructRoutes.PortDir(_ask.C, part, port);
        return _frame == 0 ? _ask.C.DirToRest(_ask.C.Parts[part].Seg, d) : d;
    }

    private void BeginStage()
    {
        var stages = _flat ? FlatStages.Length : ShaftStages.Length;
        if (_stage >= stages)
        {
            Done(RouteSearchState.Failed, FailReason());
            return;
        }
        var c = _ask.C;
        var margin = _flat ? FlatStages[_stage].Margin : ShaftStages[_stage].Margin;
        _clear = _flat ? FlatStages[_stage].Clear * _radius : _radius + 0.01f;
        _hFactor = c.RouteCount > 0 || c.TrayCount > 0 ? 0.6f : 0.92f;
        _min = ConstructWorld.Quantise(Vector3.Min(_s, _g) - new Vector3(margin)); // (on the 1 cm grid: so are the planes)
        _max = ConstructWorld.Quantise(Vector3.Max(_s, _g) + new Vector3(margin));
        _partAt = 0;
        _expanded = 0;
        _analytic = 0;
        if (_frame == 0)
        {
            _partN = c.PartsInBox(_min, _max, _parts);
        }
        else
        {
            _partN = 0;
            for (var i = 0; i < c.PartCount; i++)
                if (c.FrameOf(c.Parts[i].Seg) == _frame)
                    _parts[_partN++] = i;
        }
        foreach (var m in _marks)
            Array.Clear(m);
        if (_flat)
        {
            // The ends' own planes, and one a stub off each way, so a link can leave and arrive in open air.
            var stub = MathF.Max(StubMin * 1.5f, ConstructRoutes.Grace(_radius) * 0.9f);
            for (var a = 0; a < 3; a++)
            {
                Mark(a, _s[a], 2);
                Mark(a, _g[a], 2);
                Mark(a, _s[a] - stub, 2);
                Mark(a, _s[a] + stub, 2);
                Mark(a, _g[a] - stub, 2);
                Mark(a, _g[a] + stub, 2);
            }
            _phase = Phase.Gather;
        }
        else
        {
            MakeEvenLattice();
            _phase = Phase.Raster;
        }
    }

    private static readonly string TooFarToSearch = $"more than {MaxReach:0} m to search: place a node on the way";

    private string FailReason()
    {
        if (_lastWhy != null)
            return _lastWhy;
        if (!_flat)
            return ShaftNeed();
        if (_longWay)
            return TooFarToSearch;
        return _gaveUp ? "gave up: too many parts in the way" : "no way round the parts";
    }

    /// <summary>What a shaft needs when no straight run or gentle bends reach the input.</summary>
    private string ShaftNeed()
    {
        var into = -_gDir;
        var angle = _sDir != Vector3.Zero ? MathF.Acos(Math.Clamp(Vector3.Dot(_sDir, into), -1f, 1f)) * 180f / MathF.PI : 0f;
        return angle > 80f
            ? "no gentle shaft run: fit a right-angle gearbox at the corner"
            : "no clear shaft run: line the ports up, or fit a gearbox or a U joint on the way";
    }

    // ---- The lattice: where lines can run --------------------------------------------------------------------------

    private void Mark(int axis, float v, byte kind)
    {
        var cm = (int)MathF.Round((v - _min[axis]) * 100f);
        if (cm >= 0 && cm < Marks && v <= _max[axis]) // (no plane past the box: the parts out there aren't looked at)
            _marks[axis][cm] |= kind;
    }

    /// <summary>Each box's faces, a clearance off them, on the 1 cm grid away from the box.</summary>
    private void MarkBox(Vector3 lo, Vector3 hi)
    {
        for (var a = 0; a < 3; a++)
        {
            Mark(a, MathF.Floor(lo[a] * 100f) * 0.01f, 1);
            Mark(a, MathF.Ceiling(hi[a] * 100f) * 0.01f, 1);
        }
    }

    /// <summary>The marked planes as coordinates, thinned (the closest first, the ends' always stay) until the lattice fits.</summary>
    private bool MakeLattice()
    {
        for (var gap = FlatStages[_stage].Gap; gap < 4096; gap = gap * 3 / 2 + 1)
        {
            var total = 1L;
            var fits = true;
            for (var a = 0; a < 3; a++)
            {
                var n = 0;
                var last = -100000;
                var marks = _marks[a];
                for (var cm = 0; cm < Marks; cm++)
                {
                    if (marks[cm] == 0 || marks[cm] == 1 && cm - last < gap)
                        continue;
                    if (n < PerAxis)
                        _lat[a][n] = _min[a] + cm * 0.01f;
                    n++;
                    last = cm;
                }
                if (n > PerAxis)
                    fits = false;
                _n[a] = n;
                total *= Math.Max(n, 1);
            }
            if (fits && total <= MaxPoints)
            {
                _stride[0] = 1;
                _stride[1] = _n[0];
                _stride[2] = _n[0] * _n[1];
                Array.Clear(_grid, 0, (int)total);
                return true;
            }
        }
        return false;
    }

    private void MakeEvenLattice()
    {
        var size = _max - _min;
        _step = MathF.Max(ShaftStages[_stage].Step, MathF.Max(size.X, MathF.Max(size.Y, size.Z)) / 96f);
        var total = EvenAxes();
        if (total > MaxPoints)
        {
            _step *= MathF.Pow(total / (float)MaxPoints, 1f / 3f) * 1.05f;
            EvenAxes();
        }
        _stride[0] = 1;
        _stride[1] = _n[0];
        _stride[2] = _n[0] * _n[1];
        Array.Clear(_grid, 0, _n[0] * _n[1] * _n[2]);
    }

    /// <summary>Even planes through the start (a shaft leaves its port exactly along a lattice line). Returns how many points that makes.</summary>
    private long EvenAxes()
    {
        var total = 1L;
        for (var a = 0; a < 3; a++)
        {
            var below = (int)MathF.Floor((_s[a] - _min[a]) / _step);
            var above = (int)MathF.Floor((_max[a] - _s[a]) / _step);
            var n = Math.Min(below + above + 1, PerAxis);
            for (var i = 0; i < n; i++)
                _lat[a][i] = _s[a] + (i - below) * _step;
            _n[a] = n;
            _sAt[a] = Math.Min(below, n - 1);
            total *= n;
        }
        return total;
    }

    // ---- Stepping ------------------------------------------------------------------------------------------------------

    private Result? Step(float budgetMs)
    {
        var c = _ask.C;
        var end = Stopwatch.GetTimestamp() + (long)(budgetMs * 0.001 * Stopwatch.Frequency);
        while (true)
        {
            switch (_phase)
            {
                case Phase.Gather:
                    if (!GatherSlice(c, end))
                        return null;
                    if (!MakeLattice())
                    {
                        _gaveUp = true;
                        _stage = 99;
                        BeginStage();
                        return Stored(_key);
                    }
                    _partAt = 0;
                    _phase = Phase.Raster;
                    break;
                case Phase.Raster:
                    if (!RasterSlice(c, end))
                        return null;
                    ResetStates();
                    if (_flat && StartFlood())
                        _phase = Phase.Flood;
                    else if (!_flat && StartShaft())
                        _phase = Phase.Search;
                    else
                        NextStage();
                    break;
                case Phase.Flood:
                    if (!FloodSlice(end))
                        return null;
                    if (StartFlat())
                        _phase = Phase.Search;
                    else
                        NextStage();
                    break;
                case Phase.Search:
                    var done = _flat ? FlatSlice(end) : ShaftSlice(end);
                    if (done == null)
                        return null;
                    if (done.Value && Finish())
                        return Stored(_key);
                    NextStage();
                    break;
                default:
                    return Stored(_key);
            }
            if (_phase == Phase.Idle)
                return Stored(_key);
            if (Stopwatch.GetTimestamp() >= end)
                return null;
        }
    }

    private void NextStage()
    {
        // (a box round the whole construct already: a wider one holds nothing new)
        var c = _ask.C;
        var pad = new Vector3(1f);
        if (_flat && _stage < FlatStages.Length - 2 && Vector3.Min(_min, c.LocalMin - pad) == _min && Vector3.Max(_max, c.LocalMax + pad) == _max)
            _stage = FlatStages.Length - 2;
        _stage++;
        BeginStage();
    }

    private bool BoxInWork(Construct c, int part, int b, out Vector3 lo, out Vector3 hi)
    {
        if (_frame == 0 && c.FreeformCount > 0 && c.FrameOf(c.Parts[part].Seg) != 0)
        {
            // (a piece welded at an angle: its bounds, a little more than it fills; the path rule doesn't look at it here at all)
            if (b != 0)
            {
                lo = hi = default;
                return false;
            }
            c.RestBounds(part, out lo, out hi);
            return true;
        }
        c.BoxOf(part, b, out var centre, out var half);
        lo = centre - half;
        hi = centre + half;
        return true;
    }

    private bool GatherSlice(Construct c, long end)
    {
        var clear = new Vector3(_clear);
        for (; _partAt < _partN; _partAt++)
        {
            if ((_partAt & 63) == 63 && Stopwatch.GetTimestamp() >= end)
                return false;
            var i = _parts[_partAt];
            if (i >= c.PartCount)
                continue;
            var d = c.DefOf(i);
            for (var b = 0; b < d.SolidBoxes; b++)
                if (BoxInWork(c, i, b, out var lo, out var hi) && Overlaps(lo - clear, hi + clear))
                    MarkBox(lo - clear, hi + clear);
        }
        // Next to the runs already here, so a new one can lie alongside them.
        if (_frame == 0)
            for (var k = 0; k < c.RouteCount; k++)
            {
                var a = ConstructRoutes.RestPoint(c, k, -1);
                for (var i = 0; i <= c.Routes[k].Count; i++)
                {
                    var b = ConstructRoutes.RestPoint(c, k, i);
                    if (RunBox(c, k, a, b, out var lo, out var hi))
                        MarkBox(lo, hi);
                    a = b;
                }
            }
        // ... and the lanes of the trays and clamps.
        if (_frame == 0)
            for (var t = 0; t < c.TrayCount; t++)
                for (var l = 0; l < c.DefOf(c.Trays![t]).TrayLanes; l++)
                    if (LaneBox(c, t, l, out var lo, out var hi))
                        MarkBox(lo, hi);
        return true;
    }

    /// <summary>A tray's lane as the line a centreline lies on, or false.</summary>
    private bool LaneBox(Construct c, int tray, int lane, out Vector3 lo, out Vector3 hi)
    {
        var part = c.Trays![tray];
        ConstructTrays.LaneEnds(c, part, lane, out var a, out var b);
        var seg = c.Parts[part].Seg;
        a = ConstructWorld.Quantise(c.ToRest(seg, a));
        b = ConstructWorld.Quantise(c.ToRest(seg, b));
        lo = Vector3.Min(a, b);
        hi = Vector3.Max(a, b);
        return Overlaps(lo, hi);
    }

    /// <summary>A square run of another link as a box a centreline has to stay out of to lie beside it, or false.</summary>
    private bool RunBox(Construct c, int k, Vector3 a, Vector3 b, out Vector3 lo, out Vector3 hi)
    {
        var d = Vector3.Abs(a - b);
        var straight = (d.X > 0.015f ? 1 : 0) + (d.Y > 0.015f ? 1 : 0) + (d.Z > 0.015f ? 1 : 0);
        var s = new Vector3(ConstructRoutes.Radius(c.Routes[k].Type) + _radius + ConstructRoutes.LoomGap); // (one link-width over)
        lo = Vector3.Min(a, b) - s;
        hi = Vector3.Max(a, b) + s;
        return straight == 1 && Overlaps(lo, hi);
    }

    private bool Overlaps(Vector3 lo, Vector3 hi) =>
        hi.X >= _min.X && lo.X <= _max.X && hi.Y >= _min.Y && lo.Y <= _max.Y && hi.Z >= _min.Z && lo.Z <= _max.Z;

    private bool RasterSlice(Construct c, long end)
    {
        var clear = new Vector3(_clear);
        var inset = new Vector3(0.001f);
        for (; _partAt < _partN; _partAt++)
        {
            if (Stopwatch.GetTimestamp() >= end)
                return false;
            var i = _parts[_partAt];
            if (i >= c.PartCount)
                continue;
            var d = c.DefOf(i);
            for (var b = 0; b < d.SolidBoxes; b++)
                if (BoxInWork(c, i, b, out var lo, out var hi) && Overlaps(lo - clear, hi + clear))
                {
                    if (_flat)
                        RasterBox(lo - clear + inset, hi + clear - inset, BlockBit);
                    else
                        RasterPoints(lo - clear, hi + clear);
                }
        }
        if (_flat && _frame == 0)
            for (var k = 0; k < c.RouteCount; k++)
            {
                var a = ConstructRoutes.RestPoint(c, k, -1);
                for (var i = 0; i <= c.Routes[k].Count; i++)
                {
                    var b = ConstructRoutes.RestPoint(c, k, i);
                    if (RunBox(c, k, a, b, out var lo, out var hi))
                        RasterBox(lo + inset, hi - inset, SoftBit, BesideBit);
                    a = b;
                }
            }
        if (_flat && _frame == 0)
            for (var t = 0; t < c.TrayCount; t++)
                for (var l = 0; l < c.DefOf(c.Trays![t]).TrayLanes; l++)
                    if (LaneBox(c, t, l, out var lo, out var hi))
                        RasterBox(lo, hi, SoftBit, BesideBit);
        return true;
    }

    private static int First(float[] xs, int n, float v)
    {
        // The first index with xs[i] >= v.
        var lo = 0;
        var hi = n;
        while (lo < hi)
        {
            var m = (lo + hi) >> 1;
            if (xs[m] < v)
                lo = m + 1;
            else
                hi = m;
        }
        return lo;
    }

    /// <summary>
    /// Marks every lattice edge a box touches: along an axis, the edges in the other two axes' open range and overlapping
    /// this one's (<paramref name="bit"/>), and the wider ring round it (the discount for running beside it).
    /// </summary>
    private void RasterBox(Vector3 lo, Vector3 hi, int bit, int ring = HugBit)
    {
        Span<int> a0 = stackalloc int[3], a1 = stackalloc int[3], o0 = stackalloc int[3], o1 = stackalloc int[3];
        for (var a = 0; a < 3; a++)
        {
            var xs = _lat[a];
            var n = _n[a];
            // (strictly inside: on a plane a clearance off the box is fine)
            a0[a] = First(xs, n, lo[a]);
            a1[a] = First(xs, n, hi[a]); // (exclusive)
            o0[a] = First(xs, n, lo[a] - HugGap);
            o1[a] = First(xs, n, hi[a] + HugGap);
        }
        for (var axis = 0; axis < 3; axis++)
        {
            var u = (axis + 1) % 3;
            var v = (axis + 2) % 3;
            var xs = _lat[axis];
            // The edges overlapping the box along this axis: from the one before its first point to the last point in it.
            var e0 = Math.Max(0, a0[axis] - 1);
            var e1 = Math.Min(_n[axis] - 1, a1[axis]);
            while (e0 < e1 && xs[e0 + 1] <= lo[axis])
                e0++;
            while (e1 > e0 && xs[e1 - 1] >= hi[axis])
                e1--;
            var eo = _stride[axis];
            var uo = _stride[u];
            var vo = _stride[v];
            var hugBits = (ushort)(ring << axis);
            var hardBits = (ushort)(bit << axis);
            for (var i = o0[u]; i < o1[u]; i++)
                for (var j = o0[v]; j < o1[v]; j++)
                {
                    var hard = i >= a0[u] && i < a1[u] && j >= a0[v] && j < a1[v];
                    var at = i * uo + j * vo + e0 * eo;
                    if (hard)
                        for (var e = e0; e < e1; e++, at += eo)
                            _grid[at] |= hardBits;
                    else
                        for (var e = e0; e < e1; e++, at += eo)
                            _grid[at] |= hugBits;
                }
        }
    }

    /// <summary>The even lattice's points inside a box.</summary>
    private void RasterPoints(Vector3 lo, Vector3 hi)
    {
        var x0 = First(_lat[0], _n[0], lo.X);
        var x1 = First(_lat[0], _n[0], hi.X);
        var y0 = First(_lat[1], _n[1], lo.Y);
        var y1 = First(_lat[1], _n[1], hi.Y);
        var z0 = First(_lat[2], _n[2], lo.Z);
        var z1 = First(_lat[2], _n[2], hi.Z);
        for (var z = z0; z < z1; z++)
            for (var y = y0; y < y1; y++)
            {
                var at = x0 + y * _stride[1] + z * _stride[2];
                for (var x = x0; x < x1; x++, at++)
                    _grid[at] = BlockBit;
            }
    }

    // ---- A*: the states, the heap --------------------------------------------------------------------------------------

    private void ResetStates()
    {
        Array.Clear(_hash);
        _heapN = 0;
        _stateN = 0;
        _bestState = -1;
        _bestTarget = -1;
        _bestCost = float.MaxValue;
    }

    private int StateOf(int key, out bool fresh)
    {
        var h = (int)((uint)(key * -1640531527) >> 13) & HashMask;
        while (true)
        {
            var k = _hash[h];
            if (k == 0)
            {
                fresh = true;
                if (_stateN >= MaxStates)
                    return -1;
                _hash[h] = key + 1;
                _hState[h] = _stateN;
                _sKey[_stateN] = key;
                return _stateN++;
            }
            if (k == key + 1)
            {
                fresh = false;
                return _hState[h];
            }
            h = (h + 1) & HashMask;
        }
    }

    private void Push(float f, int state)
    {
        if (_heapN >= HeapSize)
            return;
        var i = _heapN++;
        while (i > 0)
        {
            var p = (i - 1) >> 1;
            if (_heapF[p] <= f)
                break;
            _heapF[i] = _heapF[p];
            _heapS[i] = _heapS[p];
            i = p;
        }
        _heapF[i] = f;
        _heapS[i] = state;
    }

    private int Pop(out float f)
    {
        var top = _heapS[0];
        f = _heapF[0];
        var n = --_heapN;
        var lastF = _heapF[n];
        var lastS = _heapS[n];
        var i = 0;
        while (true)
        {
            var c = i * 2 + 1;
            if (c >= n)
                break;
            if (c + 1 < n && _heapF[c + 1] < _heapF[c])
                c++;
            if (_heapF[c] >= lastF)
                break;
            _heapF[i] = _heapF[c];
            _heapS[i] = _heapS[c];
            i = c;
        }
        _heapF[i] = lastF;
        _heapS[i] = lastS;
        return top;
    }

    // ---- The square search (cables, pipes, chutes) -----------------------------------------------------------------------

    private static readonly Vector3[] Axis6 = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

    private int PointNear(Vector3 p)
    {
        var at = 0;
        for (var a = 0; a < 3; a++)
        {
            var i = First(_lat[a], _n[a], p[a] - 0.006f);
            if (i >= _n[a])
                return -1;
            at += i * _stride[a];
        }
        return at;
    }

    private static int Index(int axis, int point, int[] stride, int[] n) => (point / stride[axis]) % n[axis];

    /// <summary>
    /// The arrivals, and the flood back from them: every lattice point a link could reach them from, whatever its bends. A
    /// goal shut in behind parts is known from this alone, in one pass of the lattice, where A* would first try every point in
    /// every direction, and the search keeps out of pockets with no way on.
    /// </summary>
    private bool StartFlood()
    {
        var c = _ask.C;
        var gp = PointNear(_g);
        if (gp < 0 || PointNear(_s) < 0)
            return false;
        _tgtN = 0;
        // Arrivals: along the line into the input (the grace lets the end run through anything on its last stretch).
        if (_closed)
        {
            for (var d = 0; d < 6; d++)
            {
                var way = Axis6[d];
                var dot = Vector3.Dot(way, _gDir);
                if (_gDir != Vector3.Zero && dot < -0.3f)
                    continue;
                var reach = ConstructRoutes.Grace(_radius) + ConstructRoutes.ExitAlongIn(c, _goalHost, _g, _g + way * 4f, _frame == 0 && c.FreeformCount > 0);
                var extra = _gDir != Vector3.Zero && dot < 0.7f ? SideCost : 0f;
                var at = gp;
                var axis = d / 2;
                var sign = (d & 1) == 0 ? 1 : -1;
                var i = Index(axis, gp, _stride, _n);
                for (var k = i + sign; k >= 0 && k < _n[axis]; k += sign)
                {
                    at += sign * _stride[axis];
                    var t = MathF.Abs(_lat[axis][k] - _g[axis]);
                    if (t > reach || _tgtN >= _tgtPoint.Length)
                        break;
                    if (t < StubMin)
                        continue;
                    // (the way the link moves on arriving is against the way out of the port: d ^ 1)
                    _tgtPoint[_tgtN] = at;
                    _tgtDir[_tgtN] = (byte)(d ^ 1);
                    _tgtT[_tgtN++] = t + extra;
                    _grid[at] |= TargetBit;
                }
            }
        }
        else
        {
            for (var d = 0; d < 6; d++)
            {
                _tgtPoint[_tgtN] = gp;
                _tgtDir[_tgtN] = (byte)d;
                _tgtT[_tgtN++] = 0f;
            }
            _grid[gp] |= TargetBit;
        }
        _stackN = 0;
        for (var t = 0; t < _tgtN; t++)
            if ((_grid[_tgtPoint[t]] & ReachBit) == 0)
            {
                _grid[_tgtPoint[t]] |= ReachBit;
                _stack[_stackN++] = _tgtPoint[t];
            }
        return _stackN > 0;
    }

    private bool FloodSlice(long end)
    {
        var count = 0;
        while (_stackN > 0)
        {
            if ((++count & 4095) == 0 && Stopwatch.GetTimestamp() >= end)
                return false;
            var p = _stack[--_stackN];
            for (var axis = 0; axis < 3; axis++)
            {
                var stride = _stride[axis];
                var i = Index(axis, p, _stride, _n);
                if (i + 1 < _n[axis] && (_grid[p] & (BlockBit << axis)) == 0 && (_grid[p + stride] & ReachBit) == 0)
                {
                    _grid[p + stride] |= ReachBit;
                    _stack[_stackN++] = p + stride;
                }
                if (i > 0 && (_grid[p - stride] & (BlockBit << axis)) == 0 && (_grid[p - stride] & ReachBit) == 0)
                {
                    _grid[p - stride] |= ReachBit;
                    _stack[_stackN++] = p - stride;
                }
            }
        }
        return true;
    }

    private bool StartFlat()
    {
        var c = _ask.C;
        var sp = PointNear(_s);
        // Departures: along each way the rule allows, through whatever the grace forgives.
        var any = false;
        var legal = _prevDir == Vector3.Zero;
        for (var d = 0; d < 6; d++)
        {
            var way = Axis6[d];
            var extra = 0f;
            if (_sDir != Vector3.Zero)
            {
                var dot = Vector3.Dot(way, _sDir);
                if (dot < -0.3f)
                    continue;
                if (dot < 0.7f)
                    extra = SideCost;
            }
            if (_prevDir != Vector3.Zero)
            {
                var bend = MathF.Acos(Math.Clamp(Vector3.Dot(_prevDir, way), -1f, 1f)) * 180f / MathF.PI;
                if (bend > 150f || ConstructRoutes.BendRule(_ask.Type, bend) != null)
                    continue;
                legal = true;
                if (bend > 1f)
                    extra += BendCost;
            }
            float reach;
            if (_hasPrev)
                reach = MathF.Max(ConstructRoutes.Grace(_radius) + (_ask.Parent == 255 ? _firstExit : 0f) - _alongSoFar, 0.1f);
            else
                reach = ConstructRoutes.Grace(_radius) + (_ask.Parent != 255 ? 0f : ConstructRoutes.ExitAlongIn(c, _startHost, _s, _s + way * 4f, _frame == 0 && c.FreeformCount > 0));
            var at = sp;
            var axis = d / 2;
            var sign = (d & 1) == 0 ? 1 : -1;
            var i = Index(axis, sp, _stride, _n);
            for (var k = i + sign; k >= 0 && k < _n[axis]; k += sign)
            {
                at += sign * _stride[axis];
                var t = MathF.Abs(_lat[axis][k] - _s[axis]);
                if (t > reach)
                    break;
                if (t < MathF.Min(StubMin, reach * 0.5f) || (_grid[at] & ReachBit) == 0)
                    continue;
                var key = at * 6 + d;
                var st = StateOf(key, out var fresh);
                if (st < 0)
                    return false;
                var g = t + extra;
                if (!fresh && _sG[st] <= g)
                    continue;
                _sG[st] = g;
                _sLen[st] = t;
                _sRun[st] = t;
                _sParent[st] = -1;
                Push(g + Heur(at), st);
                any = true;
            }
            // (nothing within the grace but a clear run: a node with a surface beside it)
            if (_hasPrev || reach <= 0f)
                any |= SeedAlong(sp, d, extra);
        }
        if (!legal)
            _lastWhy = $"the last node leaves at an angle no square run can follow: take it off, or carry on by hand ({ConstructRoutes.Name(_ask.Type)}s bend in steps)";
        return any;
    }

    /// <summary>A start on the plain lattice line, for a tail with no grace left: the first step that is clear.</summary>
    private bool SeedAlong(int sp, int d, float extra)
    {
        var axis = d / 2;
        var sign = (d & 1) == 0 ? 1 : -1;
        var i = Index(axis, sp, _stride, _n);
        var k = i + sign;
        if (k < 0 || k >= _n[axis])
            return false;
        var at = sp + sign * _stride[axis];
        var edge = sign > 0 ? sp : at;
        if ((_grid[edge] & (BlockBit << axis)) != 0 || (_grid[at] & ReachBit) == 0)
            return false;
        var t = MathF.Abs(_lat[axis][k] - _s[axis]);
        var st = StateOf(at * 6 + d, out var fresh);
        if (st < 0 || !fresh && _sG[st] <= t + extra)
            return false;
        _sG[st] = t + extra;
        _sLen[st] = t;
        _sRun[st] = t;
        _sParent[st] = -1;
        Push(t + extra + Heur(at), st);
        return true;
    }

    private float Heur(int point)
    {
        var x = point % _n[0];
        var rest = point / _n[0];
        var y = rest % _n[1];
        var z = rest / _n[1];
        var dx = MathF.Abs(_lat[0][x] - _g.X);
        var dy = MathF.Abs(_lat[1][y] - _g.Y);
        var dz = MathF.Abs(_lat[2][z] - _g.Z);
        var bends = (dx > 0.01f ? 1 : 0) + (dy > 0.01f ? 1 : 0) + (dz > 0.01f ? 1 : 0) - 1;
        return (dx + dy + dz) * _hFactor + MathF.Max(bends, 0) * BendCost;
    }

    /// <summary>A slice of the square search: true found, false nothing there, null out of time.</summary>
    private bool? FlatSlice(long end)
    {
        var minRun = MathF.Max(StubMin, _radius * 1.5f);
        while (true)
        {
            if ((_expanded & 63) == 63 && Stopwatch.GetTimestamp() >= end)
            {
                _expanded++;
                return null;
            }
            if (_heapN == 0)
                return _bestState >= 0;
            var s = Pop(out var f);
            if (f >= _bestCost)
                return true;
            var key = _sKey[s];
            var p = key / 6;
            var dir = key % 6;
            var g = _sG[s];
            if (f - Heur(p) > g + 1e-3f)
                continue; // (a stale heap entry)
            _expanded++;
            if (_expanded > MaxExpansions)
            {
                _gaveUp = true;
                return _bestState >= 0;
            }
            var cell = _grid[p];
            if ((cell & TargetBit) != 0)
                for (var t = 0; t < _tgtN; t++)
                {
                    if (_tgtPoint[t] != p)
                        continue;
                    var bend = _tgtDir[t] != dir;
                    if (bend && (_sRun[s] < minRun || _tgtDir[t] == (dir ^ 1)))
                        continue;
                    var total = g + _tgtT[t] + (bend ? BendCost : 0f);
                    if (total < _bestCost && _sLen[s] + _tgtT[t] <= _maxLen)
                    {
                        _bestCost = total;
                        _bestState = s;
                        _bestTarget = t;
                    }
                }
            for (var d = 0; d < 6; d++)
            {
                if (d == (dir ^ 1))
                    continue;
                var turn = d != dir;
                if (turn && _sRun[s] < minRun)
                    continue;
                var axis = d / 2;
                var stride = _stride[axis];
                var i = Index(axis, p, _stride, _n);
                int q, edge;
                float len;
                if ((d & 1) == 0)
                {
                    if (i + 1 >= _n[axis])
                        continue;
                    q = p + stride;
                    edge = p;
                    len = _lat[axis][i + 1] - _lat[axis][i];
                }
                else
                {
                    if (i == 0)
                        continue;
                    q = p - stride;
                    edge = q;
                    len = _lat[axis][i] - _lat[axis][i - 1];
                }
                var bits = _grid[edge];
                if ((bits & (BlockBit << axis)) != 0 || (_grid[q] & ReachBit) == 0)
                    continue;
                var total = _sLen[s] + len;
                if (total > _maxLen)
                {
                    _longWay = true;
                    continue;
                }
                var cost = len * ((bits & (SoftBit << axis)) != 0 ? SoftFactor : (bits & (BesideBit << axis)) != 0 ? BesideFactor : (bits & (HugBit << axis)) != 0 ? HugFactor : 1f);
                var ng = g + cost + (turn ? BendCost : 0f);
                var st = StateOf(q * 6 + d, out var fresh);
                if (st < 0)
                {
                    _gaveUp = true;
                    return _bestState >= 0;
                }
                if (!fresh && _sG[st] <= ng)
                    continue;
                _sG[st] = ng;
                _sLen[st] = total;
                _sRun[st] = turn ? len : _sRun[s] + len;
                _sParent[st] = s;
                Push(ng + Heur(q), st);
            }
        }
    }

    private Vector3 PointAt(int p)
    {
        var x = p % _n[0];
        var rest = p / _n[0];
        return new Vector3(_lat[0][x], _lat[1][rest % _n[1]], _lat[2][rest / _n[1]]);
    }

    // ---- The shaft search ---------------------------------------------------------------------------------------------

    private bool StartShaft()
    {
        var sp = _sAt[0] + _sAt[1] * _stride[1] + _sAt[2] * _stride[2];
        var any = false;
        var way = _prevDir != Vector3.Zero ? _prevDir : _sDir;
        for (var d = 0; d < 18; d++)
        {
            var v = Dir(d) / Dir18Len[d];
            var off = way == Vector3.Zero ? 0f : MathF.Acos(Math.Clamp(Vector3.Dot(way, v), -1f, 1f)) * 180f / MathF.PI;
            if (off > ConstructRoutes.ShaftMaxBendDeg)
                continue;
            var st = StateOf(sp * 18 + d, out _);
            if (st < 0)
                return false;
            var bent = off > 1f;
            _sG[st] = bent ? ShaftBendCost : 0f;
            _sLen[st] = 0f;
            _sRun[st] = 1f; // (the port's own stub: it may bend at once)
            _sBends[st] = (byte)(bent ? 1 : 0);
            _sParent[st] = -1;
            Push(_sG[st] + Vector3.Distance(_s, _g), st);
            any = true;
        }
        return any;
    }

    private bool? ShaftSlice(long end)
    {
        var graceS = ConstructRoutes.Grace(_radius) + (_ask.Count == 0 ? ConstructRoutes.ExitAlongIn(_ask.C, _startHost, _s, _s + _sDir * 4f, _frame == 0 && _ask.C.FreeformCount > 0) : 0f);
        var graceG = _half ? ConstructRoutes.HalfShaftGrace(_ask.C, _goalHost, _ask.ToPort, _g, _g + _gDir * 4f, _frame == 0 && _ask.C.FreeformCount > 0)
            : ConstructRoutes.Grace(_radius) + ConstructRoutes.ExitAlongIn(_ask.C, _goalHost, _g, _g + _gDir * 4f, _frame == 0 && _ask.C.FreeformCount > 0);
        var tries = _analytic;
        while (true)
        {
            if (((_expanded & 31) == 31 || _analytic != tries) && Stopwatch.GetTimestamp() >= end)
            {
                _expanded++;
                return null;
            }
            tries = _analytic;
            if (_heapN == 0)
                return false;
            var s = Pop(out var f);
            var key = _sKey[s];
            var p = key / 18;
            var dir = key % 18;
            var g = _sG[s];
            var at = PointAt(p);
            if (f - Vector3.Distance(at, _g) > g + 1e-3f)
                continue; // (a stale heap entry)
            _expanded++;
            if (_expanded > MaxExpansions)
            {
                _gaveUp = true;
                return false;
            }
            if (ShaftClose(s, at, dir))
                return true;
            for (var pick = -1; pick < Turn18[dir].Length; pick++)
            {
                var d = pick < 0 ? dir : Turn18[dir][pick];
                var turn = pick >= 0;
                if (turn && (_sRun[s] < 0.2f || _sBends[s] >= ShaftMaxBends))
                    continue;
                var x = p % _n[0] + Dir18[d, 0];
                var rest = p / _n[0];
                var y = rest % _n[1] + Dir18[d, 1];
                var z = rest / _n[1] + Dir18[d, 2];
                if (x < 0 || y < 0 || z < 0 || x >= _n[0] || y >= _n[1] || z >= _n[2])
                    continue;
                var q = x + y * _stride[1] + z * _stride[2];
                var len = Dir18Len[d] * _step;
                var total = _sLen[s] + len;
                if (total > _maxLen)
                {
                    _longWay = true;
                    continue;
                }
                if ((_grid[q] & BlockBit) != 0)
                {
                    var qp = PointAt(q);
                    if (total > graceS && Vector3.Distance(qp, _g) > graceG)
                        continue;
                }
                var ng = g + len + (turn ? ShaftBendCost : 0f);
                var st = StateOf(q * 18 + d, out var fresh);
                if (st < 0)
                {
                    _gaveUp = true;
                    return false;
                }
                if (!fresh && _sG[st] <= ng)
                    continue;
                _sG[st] = ng;
                _sLen[st] = total;
                _sRun[st] = turn ? len : _sRun[s] + len;
                _sBends[st] = (byte)(_sBends[s] + (turn ? 1 : 0));
                _sParent[st] = s;
                Push(ng + Vector3.Distance(PointAt(q), _g), st);
            }
        }
    }

    /// <summary>From this state can a straight run (straight into the port, or with one stub out of it) finish it? Checked with the path rule.</summary>
    private bool ShaftClose(int s, Vector3 at, int dir)
    {
        var way = Dir(dir) / Dir18Len[dir];
        var into = -_gDir;
        var cos = MathF.Cos(ConstructRoutes.ShaftMaxBendDeg * MathF.PI / 180f) + 0.001f;
        var cosIn = _half ? MathF.Cos(ConstructRoutes.HalfShaftMaxDeg * MathF.PI / 180f) + 0.001f : cos; // (a half shaft swings with its hub: it comes in straighter)
        Span<Vector3> ends = [_g, _g + _gDir * 0.3f, _g + _gDir * 0.6f];
        for (var e = 0; e < (_closed ? ends.Length : 1); e++)
        {
            var to = ends[e];
            var v = to - at;
            var len = v.Length();
            if (len < ConstructRoutes.MinSegment)
                continue;
            v /= len;
            if (Vector3.Dot(way, v) < cos || _closed && Vector3.Dot(v, into) < (e == 0 ? cosIn : cos))
                continue;
            if (_sBends[s] + (Vector3.Dot(way, v) < 0.999f ? 1 : 0) + (e > 0 ? 1 : 0) > ShaftMaxBends || len > ConstructRoutes.MaxSegment && !_half)
                continue;
            if (_analytic >= 600)
                return false;
            _analytic++;
            if (ShaftBuild(s, at, e > 0 ? to : (Vector3?)null))
                return true;
        }
        return false;
    }

    private bool ShaftBuild(int s, Vector3 at, Vector3? last)
    {
        var n = 0;
        _path[n++] = _s;
        var chain = n;
        // Back along the states, then the right way round.
        var back = 0;
        for (var i = s; i >= 0 && back < MaxPath - 8; i = _sParent[i])
            back++;
        if (back >= MaxPath - 8)
            return false;
        var w = chain + back;
        for (var i = s; i >= 0; i = _sParent[i])
            _path[--w] = PointAt(_sKey[i] / 18);
        n = chain + back;
        if (last is { } l)
            _path[n++] = l;
        _path[n++] = _g;
        return Nodes(n, false);
    }

    // ---- From a path to nodes ----------------------------------------------------------------------------------------

    private bool Finish()
    {
        if (!_flat)
            return true; // (the shaft search has checked its own, ShaftClose)
        if (_bestState < 0)
            return false;
        // The whole path, start to goal, on the lattice.
        var n = 0;
        _path[n++] = PointAt(PointNear(_s)); // (on the lattice: a corner comes out of a real turn, not the ends' millimetres off it)
        var back = 0;
        for (var i = _bestState; i >= 0 && back < MaxPath - 8; i = _sParent[i])
            back++;
        if (back >= MaxPath - 8)
            return false;
        var w = n + back;
        for (var i = _bestState; i >= 0; i = _sParent[i])
            _path[--w] = PointAt(_sKey[i] / 6);
        n += back;
        if (_bestTarget >= 0 && _closed)
            _path[n++] = PointAt(PointNear(_g));
        else
            _path[n++] = _g;
        return Nodes(n, true);
    }

    /// <summary>The path's corners as nodes, on the parts they run past, checked with the server's rule.</summary>
    private bool Nodes(int n, bool final)
    {
        // Corners only: straight on stays one segment (split where a segment would be longer than a link allows).
        var w = 1;
        for (var i = 1; i < n; i++)
        {
            if (w >= 2)
            {
                var a = Vector3.Normalize(_path[w - 1] - _path[w - 2]);
                var d = _path[i] - _path[w - 1];
                if (d.LengthSquared() < 1e-8f)
                    continue;
                if (Vector3.Dot(a, Vector3.Normalize(d)) > 0.9999f)
                {
                    _path[w - 1] = _path[i];
                    continue;
                }
            }
            else if (Vector3.DistanceSquared(_path[0], _path[i]) < 1e-8f)
                continue;
            _path[w++] = _path[i];
        }
        n = w;
        var have = _ask.Count;
        Array.Copy(_ask.Nodes, _chk, have);
        var total = have;
        var length = _alongSoFar;
        var lastIndex = _closed ? n - 1 : n; // (an open goal's last point is a node, a closed one's is the port)
        for (var i = 1; i < lastIndex + (_closed ? 1 : 0); i++)
        {
            var seg = Vector3.Distance(_path[i - 1], _path[i]);
            length += seg;
            var pieces = (int)MathF.Ceiling(seg / (ConstructRoutes.MaxSegment - 0.01f));
            for (var k = 1; k < pieces; k++)
            {
                if (total >= Construct.MaxNodesPerRoute)
                    return Refuse($"it needs more than {Construct.MaxNodesPerRoute} nodes");
                _chk[total++] = new CRouteNode { Pos = Vector3.Lerp(_path[i - 1], _path[i], k / (float)pieces) };
            }
            if (i < lastIndex)
            {
                if (total >= Construct.MaxNodesPerRoute)
                    return Refuse($"it needs more than {Construct.MaxNodesPerRoute} nodes");
                _chk[total++] = new CRouteNode { Pos = _path[i] };
            }
        }
        if (_half && length > ConstructRoutes.HalfShaftReach(_ask.C.DefOf(_ask.To)))
            return Refuse($"too far: half shaft reaches {ConstructRoutes.HalfShaftReach(_ask.C.DefOf(_ask.To)):0.#} m");
        Hosts(have, total);
        var c = _ask.C;
        string? why;
        if (_closed)
            why = ConstructRoutes.Check(_ask.World, c, _ask.Type, _ask.From, _ask.FromPort, _ask.Parent, _ask.ParentSeg, _ask.Junction, _chk.AsSpan(0, total), _ask.To, _ask.ToPort, checkFrom: have); // (what the search added: the rest was checked as it was laid)
        else
        {
            var tail = _chk[total - 1];
            why = ConstructRoutes.OpenPath(c, _ask.Type, _ask.From, _ask.FromPort, _ask.Parent, _ask.ParentSeg, _ask.Junction, _chk.AsSpan(0, total - 1), tail.Pos, tail.Host);
        }
        if (why != null)
            return final && Refuse(why);
        Done(RouteSearchState.Found, null, total - have, length);
        return true;
    }

    private bool Refuse(string why)
    {
        _lastWhy = why;
        return false;
    }

    /// <summary>
    /// The part each new node goes on: the nearest one on the side of the link it is on (a link across a moving joint crosses
    /// it once: a flexible section of a chute, a cable's slack), in the frame the path is in. Positions go from the
    /// work frame into the host's own.
    /// </summary>
    private void Hosts(int from, int to)
    {
        var c = _ask.C;
        var two = _msS != _msG && !_half; // (every node of a half shaft stays on the output's side: only its end moves)
        var crossed = false;
        for (var i = from; i < to; i++)
        {
            if (!_closed && i == to - 1)
            {
                _chk[i] = new CRouteNode { Pos = _ask.OpenAt, Host = _ask.OpenHost }; // (the waypoint itself)
                continue;
            }
            var p = _chk[i].Pos;
            var rest = _frame == 0 ? p : c.ToRest(_frame, p);
            var host = Nearest(rest, _msS, out var near);
            var side = _startHost;
            if (two)
            {
                var other = Nearest(rest, _msG, out var nearOther);
                crossed |= nearOther < near;
                if (crossed)
                {
                    host = other;
                    side = _goalHost;
                }
            }
            if (host < 0)
                host = side;
            _chk[i].Host = (ushort)host;
            _chk[i].Pos = ConstructWorld.Quantise(_frame == 0 ? ConstructRoutes.FromRest(c, c.Parts[host].Seg, p) : p);
        }
    }

    private int Nearest(Vector3 rest, int moveSeg, out float dist)
    {
        var c = _ask.C;
        var reach = new Vector3(0.75f);
        var n = c.PartsInBox(rest - reach, rest + reach, Construct.PartScratch);
        var best = -1;
        dist = float.MaxValue;
        for (var k = 0; k < n; k++)
        {
            var i = Construct.PartScratch[k];
            var seg = c.Parts[i].Seg;
            if (c.MoveSeg(seg) != moveSeg || c.FreeformCount > 0 && c.FrameOf(seg) != _frame)
                continue;
            c.RestBounds(i, out var lo, out var hi);
            var d = Vector3.DistanceSquared(rest, Vector3.Clamp(rest, lo, hi));
            if (d < dist)
            {
                dist = d;
                best = i;
            }
        }
        return best;
    }
}

/// <summary>Where an auto-route has got to.</summary>
public enum RouteSearchState : byte
{
    Idle,
    Busy,
    Found,
    Failed,
}
