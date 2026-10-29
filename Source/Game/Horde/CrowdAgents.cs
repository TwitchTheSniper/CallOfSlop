using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// The far crowd's persistent visual zombies (docs/zombies/MASS-HORDE.md, "Rendering: persistent crowd agents"). The
/// crowd's far zombies are only counts per 4 m cell (a client's <see cref="CrowdPicture"/>, the host's mass tier); this
/// keeps a pool of visual zombies ("agents") standing in for them, each with its own position, heading, walk phase and
/// look kept from frame to frame, so nothing pops or teleports:
/// <list type="bullet">
/// <item>A visible cell holds <c>min(count, cap)</c> agents (<see cref="CapAt"/>: <see cref="DrawCap"/> near, falling to
/// <see cref="FarCap"/> far, with hysteresis). Agents walk along the cell's heading (smoothed; the still/moving switch
/// eased) and cross into the next cell when it has room (or is a sampled, flowing cell: the conveyor), never
/// wrapping. A cell they may not enter blocks them: they stop and spread out in their own cell.</item>
/// <item>When counts change, a cell with too many passes agents to a neighbour with too few (they walk across); what is
/// left over after a moment sinks into the ground, and a cell short of agents for a moment raises new ones out of it
/// (<see cref="FadeSeconds"/>). A cell coming into view (frustum, fog, terrain horizon) is filled at once (nobody saw
/// it empty); a cell leaving view drops its agents at once.</item>
/// <item>Tier changes: <see cref="Claim"/> (a zombie that became an individual near here: the nearest agent hides and
/// stands in for it until the count drops), <see cref="Hint"/> (one that joined the far crowd here: an agent appears
/// right there), <see cref="Release"/> (the host's mass let one out as a particle here: the nearest agent sinks while
/// the particle rises).</item>
/// </list>
/// Visual only: nothing here feeds back into the game or the network. The same on host and client (each from its own
/// counts). Deterministic for the same inputs and frame times (the per-agent pass runs in parallel stripes, but every
/// decision that touches another agent or a cell is taken afterwards in agent order). No allocation once the pool has
/// grown. Placement API for other systems (shots into the far crowd): <see cref="Count"/>, the agent arrays,
/// <see cref="Position"/>, <see cref="RayHit"/> and <see cref="Kill"/>.
/// </summary>
public sealed class CrowdAgents
{
    public enum Mode : byte
    {
        /// <summary>Standing in or walking with its cell.</summary>
        Live,

        /// <summary>Coming up out of the ground (a new agent in a cell that was short).</summary>
        Rising,

        /// <summary>Going down into the ground (no longer counted in any cell); removed when gone.</summary>
        Sinking,

        /// <summary>Walking across to the neighbouring cell it was passed to (already counted there).</summary>
        Moving,

        /// <summary>Hidden: an individual zombie stands in its place (still counted until the cell's count drops).</summary>
        Claimed,
    }

    // ---- Tuning ---------------------------------------------------------------------------------------

    /// <summary>Most agents a cell near the camera (within <see cref="FarFrom"/> m) and far (beyond <see cref="FarTo"/> m).</summary>
    public int DrawCap = 28, FarCap = 8;

    public float FarFrom = 60f, FarTo = 180f;

    /// <summary>The crowd's walking speed (m/s; each agent its own share of it).</summary>
    public float Speed = 1.5f;

    /// <summary>A cell this full is packed (the mass tier's capacity): agents don't flow into a packed cell from a packed one.</summary>
    public int JamCount = 40;

    /// <summary>A packed cell pressing on (a jam with a heading) shuffles on the spot at this share of the pace instead of standing (a horde held back still moves as a body).</summary>
    public const float JamPace = 0.35f;

    /// <summary>Cull cells behind the terrain's horizon (<see cref="CrowdHorizon"/>).</summary>
    public bool HorizonCull = true;

    /// <summary>Rigs by distance: near up to <see cref="NearRange"/> m, mid up to <see cref="MidRange"/>, far beyond; an agent changes after <see cref="LodSlack"/> m more.</summary>
    public float NearRange = 35f, MidRange = 80f, LodSlack = 2f;

    public const float FadeSeconds = 0.5f, SinkDepth = 1.9f, MoveSpeed = 2.4f, EaseSpeed = 0.35f, TurnRate = 2.5f;

    /// <summary>A cell with agents to spare for this long sinks them; one short for this long raises new ones.</summary>
    public const float SurplusSeconds = 0.6f, DeficitSeconds = 0.4f;

    /// <summary>A claimed agent (an individual took its place) is dropped after this long if its cell never lost the count.</summary>
    public const float ClaimSeconds = 1.5f;

    /// <summary>The furthest an agent moves in a frame of <c>dt</c> s (self-test): its fastest walk.</summary>
    public float MaxStep(float dt) => MathF.Max(MoveSpeed, Speed * 1.3f + EaseSpeed) * dt + 1e-3f;

    // ---- The grid ---------------------------------------------------------------------------------------

    public int Cols, Rows;
    public float Cell = 4f, MinX, MinZ, FloorY;
    public TerrainData? Ground;

    private int _n;
    private int[] _want = [], _target = [], _members = [], _visFrame = [], _occStamp = [];
    private byte[] _head = [], _cap = [], _vis = [], _occ = [], _fresh = [];
    private float[] _hx = [], _hz = [], _move = [], _flow = [], _yawC = [], _gy = [], _top = [], _surT = [], _defT = [];

    // ---- The agents (structure of arrays, indices 0..Count-1) ------------------------------------------

    public int Count;
    public float[] X = [], Z = [], Y = [], Yaw = [], Walk = [], Fade = [], SX = [], SZ = [], Pace = [], Timer = [];
    public Mode[] State = [];
    public int[] CellOf = [];

    /// <summary>Each agent's look (brightness, stride, yaw spread) and its own number (unique while it lives: self-test).</summary>
    public uint[] Look = [], Id = [];

    public byte[] Lod = [];
    private byte[] _dead = [];
    private uint _nextId = 1;

    // ---- This frame ---------------------------------------------------------------------------------------

    /// <summary>Frames updated.</summary>
    public long Frame;

    /// <summary>This frame: cells with agents in view, and cells left out by the frustum, the fog and the horizon.</summary>
    public int CellsDrawn, CellsCulled, CellsFogged, CellsHidden;

    /// <summary>Since the start (self-test, log): agents raised, filled in at once, sunk, dropped at once, passed to a neighbour, crossed, claimed.</summary>
    public long Raised, Filled, Sunk, Dropped, Passed, Crossed, Claims;

    public readonly CrowdHorizon Horizon = new();
    private readonly CrowdJobs _jobs;
    private Vector3 _cam;
    private float _dt, _fogEnd;
    private CrowdInstances? _out;
    private readonly Action<int> _cellPass, _flowPass, _agentPass, _horizonPass;
    private int _horizonBuild;
    private bool _fillAll = true;

    // Per stripe: the agents that want to cross into another cell this frame.
    private readonly int[][] _crossI;
    private readonly int[][] _crossC;
    private readonly float[][] _crossX, _crossZ;
    private readonly int[] _crossN;

    // Per cell, the agents in it (counting sort; rebuilt each frame and when asked).
    private int[] _start = [], _items = [];
    private long _listFrame = -1;
    private int _listCount = -1;

    // Tier changes waiting for this frame: kind 0 claim, 1 hint, 2 release.
    private readonly float[] _evX = new float[256], _evZ = new float[256];
    private readonly uint[] _evLook = new uint[256];
    private readonly byte[] _evKind = new byte[256];
    private int _evN;

    /// <summary>Unit directions for the 255 heading bytes (index 0: none).</summary>
    private static readonly float[] DirX = new float[256], DirZ = new float[256];

    static CrowdAgents()
    {
        for (var b = 1; b < 256; b++)
        {
            var f = World.FlatForward(HordeCrowd.HeadingYaw((byte)b));
            DirX[b] = f.X;
            DirZ[b] = f.Z;
        }
    }

    public CrowdAgents(CrowdJobs? jobs = null)
    {
        _jobs = jobs ?? CrowdJobs.Shared;
        var s = _jobs.Stripes;
        _crossI = new int[s][];
        _crossC = new int[s][];
        _crossX = new float[s][];
        _crossZ = new float[s][];
        _crossN = new int[s];
        for (var k = 0; k < s; k++)
        {
            _crossI[k] = new int[256];
            _crossC[k] = new int[256];
            _crossX[k] = new float[256];
            _crossZ[k] = new float[256];
        }
        _cellPass = CellPass;
        _flowPass = FlowPass;
        _agentPass = AgentPass;
        _horizonPass = HorizonPass;
        Grow(4096);
    }

    /// <summary>The grid's cell count.</summary>
    public int Cells => _n;

    /// <summary>The count the agents follow in cell <paramref name="c"/>, and how many agents stand in it now (self-test).</summary>
    public int WantOf(int c) => _want[c];

    public int MembersOf(int c) => _members[c];

    public int TargetOf(int c) => _target[c];

    public bool VisibleCell(int c) => _vis[c] != 0;

    // ---- Inputs ---------------------------------------------------------------------------------------------

    /// <summary>The grid (a new one drops every agent; the same one keeps them).</summary>
    public void Layout(int cols, int rows, float cell, float minX, float minZ, float floorY, TerrainData? ground)
    {
        if (cols == Cols && rows == Rows && cell == Cell && minX == MinX && minZ == MinZ && ReferenceEquals(ground, Ground) && floorY == FloorY && _n == cols * rows)
            return;
        (Cols, Rows, Cell, MinX, MinZ, FloorY, Ground) = (cols, rows, cell, minX, minZ, floorY, ground);
        _n = cols * rows;
        Count = 0;
        _want = new int[_n];
        _target = new int[_n];
        _members = new int[_n];
        _visFrame = new int[_n];
        _occStamp = new int[_n];
        _head = new byte[_n];
        _cap = new byte[_n];
        _vis = new byte[_n];
        _occ = new byte[_n];
        _fresh = new byte[_n];
        _hx = new float[_n];
        _hz = new float[_n];
        _move = new float[_n];
        _flow = new float[_n];
        _yawC = new float[_n];
        _gy = new float[_n];
        _top = new float[_n];
        _surT = new float[_n];
        _defT = new float[_n];
        _start = new int[_n + 1];
        Array.Fill(_visFrame, int.MinValue);
        for (var c = 0; c < _n; c++)
        {
            float x0 = MinX + c % Cols * Cell, z0 = MinZ + c / Cols * Cell;
            if (Ground == null)
            {
                _gy[c] = FloorY;
                _top[c] = FloorY + 2.2f;
                continue;
            }
            _gy[c] = Ground.Sample(x0 + Cell * 0.5f, z0 + Cell * 0.5f);
            var top = float.NegativeInfinity;
            for (var j = 0; j <= 4; j++)
                for (var i = 0; i <= 4; i++)
                    top = MathF.Max(top, Ground.Sample(x0 + Cell * i / 4f, z0 + Cell * j / 4f));
            _top[c] = top + 2.2f;
        }
        _horizonBuild++;
        _fillAll = true;
    }

    /// <summary>A client: the counts and headings of its picture of the crowd (none before the first keyframe).</summary>
    public void FromPicture(CrowdPicture p, TerrainData? ground)
    {
        if (p.Cols == 0 || p.Rows == 0)
        {
            if (_n > 0)
            {
                Array.Clear(_want);
                Array.Clear(_head);
            }
            return;
        }
        Layout(p.Cols, p.Rows, p.Cell, p.MinX, p.MinZ, p.Y, ground);
        if (p.Round < 0 || p.Count.Length != _n)
        {
            Array.Clear(_want);
            Array.Clear(_head);
            return;
        }
        for (var c = 0; c < _n; c++)
        {
            _want[c] = p.Count[c];
            _head[c] = p.Head[c];
        }
    }

    /// <summary>The host: the mass tier's cells (its particles are drawn as themselves).</summary>
    public void FromMass(HordeCrowd crowd)
    {
        if (crowd.MassCols == 0)
            return;
        Layout(crowd.MassCols, crowd.MassRows, crowd.MassCell, crowd.MinX, crowd.MinZ, crowd.Y, crowd.Terrain);
        for (var c = 0; c < _n; c++)
        {
            var m = crowd.MassCount[c];
            _want[c] = m;
            if (m == 0)
            {
                _head[c] = 0;
                continue;
            }
            float vx = crowd.MassVX[c], vz = crowd.MassVZ[c];
            _head[c] = vx * vx + vz * vz > 1e-4f ? HordeCrowd.HeadingByte(MathF.Atan2(vx, -vz)) : (byte)0;
        }
    }

    /// <summary>An individual zombie appeared at (x, z) out of the crowd: the nearest agent hides and stands in for it.</summary>
    public void Claim(float x, float z) => Event(0, x, z, 0);

    /// <summary>A zombie joined the far crowd at (x, z) (an individual or particle left): an agent appears right there.</summary>
    public void Hint(float x, float z, uint look) => Event(1, x, z, look);

    /// <summary>The host's mass let a zombie out as a particle at (x, z): the nearest agent sinks while the particle rises.</summary>
    public void Release(float x, float z) => Event(2, x, z, 0);

    private void Event(byte kind, float x, float z, uint look)
    {
        if (_evN >= _evX.Length)
            return;
        _evKind[_evN] = kind;
        _evX[_evN] = x;
        _evZ[_evN] = z;
        _evLook[_evN] = look;
        _evN++;
    }

    // ---- The frame ----------------------------------------------------------------------------------------

    /// <summary>
    /// One frame: the cells' view and targets, every agent's step (writing its instance into <paramref name="output"/>,
    /// if given), then crossings, passes, sinking and raising. <paramref name="fogEnd"/>: cells past it are left out (0:
    /// no fog). Uses the view frustum <see cref="ZombieAnim.SetFrustum"/> set.
    /// </summary>
    public void Update(Vector3 cam, float dt, float fogEnd, CrowdInstances? output)
    {
        Frame++;
        CellsDrawn = CellsCulled = CellsFogged = CellsHidden = 0;
        if (_n == 0)
        {
            _evN = 0;
            return;
        }
        _cam = cam;
        _dt = Math.Clamp(dt, 0f, 0.1f);
        _fogEnd = fogEnd;
        _out = output;
        if (HorizonCull && Ground != null && Horizon.Stale(Ground, cam))
        {
            var range = MathF.Sqrt(Cols * Cols + Rows * Rows) * Cell + 10f;
            Horizon.Begin(Ground, cam, fogEnd > 0 ? MathF.Min(range, fogEnd + 20f) : range);
            _jobs.Run(_horizonPass);
            _horizonBuild++;
        }
        _jobs.Run(_cellPass);
        for (var s = 0; s < _jobs.Stripes; s++)
            _stats[s].AddTo(this);
        _jobs.Run(_flowPass);
        for (var s = 0; s < _jobs.Stripes; s++)
            _crossN[s] = 0;
        _jobs.Run(_agentPass);
        ResolveCrossings();
        RemoveDead();
        BuildLists();
        Events();
        Rebalance();
        _fillAll = false;
        _out = null;
    }

    private void HorizonPass(int s)
    {
        _jobs.Range(s, CrowdHorizon.Bins, out var from, out var to);
        Horizon.BuildBins(from, to);
    }

    private struct CellStats
    {
        public int Drawn, Culled, Fogged, Hidden;

        public void AddTo(CrowdAgents a)
        {
            a.CellsDrawn += Drawn;
            a.CellsCulled += Culled;
            a.CellsFogged += Fogged;
            a.CellsHidden += Hidden;
            this = default;
        }
    }

    private CellStats[] _stats = new CellStats[8];

    /// <summary>Is cell <paramref name="c"/> behind the terrain's horizon from the camera (cached until the horizon is built again)?</summary>
    public bool HiddenCell(int c)
    {
        if (!HorizonCull || Ground == null || Horizon.Steps == 0)
            return false;
        if (Volatile.Read(ref _occStamp[c]) == _horizonBuild)
            return _occ[c] != 0;
        float cx = MinX + (c % Cols + 0.5f) * Cell - Horizon.At.X, cz = MinZ + (c / Cols + 0.5f) * Cell - Horizon.At.Z;
        var d = MathF.Sqrt(cx * cx + cz * cz);
        var hidden = Horizon.Hidden(cx, cz, d, Cell * 0.7072f, _top[c]);
        _occ[c] = hidden ? (byte)1 : (byte)0;
        Volatile.Write(ref _occStamp[c], _horizonBuild);
        return hidden;
    }

    /// <summary>The cell (x, z) is in (-1: outside the grid).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CellAt(float x, float z)
    {
        var cx = (int)MathF.Floor((x - MinX) / Cell);
        var cz = (int)MathF.Floor((z - MinZ) / Cell);
        return (uint)cx < (uint)Cols && (uint)cz < (uint)Rows ? cz * Cols + cx : -1;
    }

    /// <summary>Most agents for a cell <paramref name="d"/> m from the camera.</summary>
    public int CapAt(float d)
    {
        if (d <= FarFrom || FarTo <= FarFrom)
            return DrawCap;
        if (d >= FarTo)
            return Math.Min(FarCap, DrawCap);
        var t = (d - FarFrom) / (FarTo - FarFrom);
        return Math.Max(1, (int)MathF.Round(DrawCap + (Math.Min(FarCap, DrawCap) - DrawCap) * t));
    }

    private void CellPass(int s)
    {
        _jobs.Range(s, _n, out var from, out var to);
        var dt = _dt;
        var frame = (int)Frame;
        var r = Cell * 0.7072f + 1f;
        ref var st = ref _stats[s];
        var fog2 = _fogEnd > 0 ? (_fogEnd + r) * (_fogEnd + r) : float.MaxValue;
        for (var c = from; c < to; c++)
        {
            var want = _want[c];
            if (want == 0 && _members[c] == 0)
            {
                _target[c] = 0;
                _fresh[c] = 0;
                continue; // (its view is kept as it was: a cell emptied in view lets its agents sink in peace)
            }
            float cx = MinX + (c % Cols + 0.5f) * Cell, cz = MinZ + (c / Cols + 0.5f) * Cell;
            float dx = cx - _cam.X, dz = cz - _cam.Z;
            var d2 = dx * dx + dz * dz;
            bool vis;
            if (d2 > fog2)
            {
                vis = false;
                st.Fogged++;
            }
            else if (!ZombieAnim.Visible(new Vector3(cx, _gy[c] + 0.9f, cz), r + MathF.Max(0f, _top[c] - _gy[c] - 2.2f)))
            {
                vis = false;
                st.Culled++;
            }
            else if (HiddenCell(c))
            {
                vis = false;
                st.Hidden++;
            }
            else
            {
                vis = true;
                if (want > 0)
                    st.Drawn++;
            }
            // Newly in view: filled at once (it was evaluated last frame and out of view; an empty cell isn't evaluated).
            // (Or everything on the first frame of a new grid: nobody saw it empty.)
            _fresh[c] = vis && _vis[c] == 0 && (_visFrame[c] == frame - 1 || _fillAll) ? (byte)1 : (byte)0;
            _vis[c] = vis ? (byte)1 : (byte)0;
            _visFrame[c] = frame;
            // The cap by distance (hysteresis: it moves in steps of 3, or to either end).
            var cap = CapAt(MathF.Sqrt(d2));
            int cur = _cap[c];
            if (cur == 0 || Math.Abs(cap - cur) >= 3 || cap != cur && (cap == DrawCap || cap == Math.Min(FarCap, DrawCap)))
                _cap[c] = (byte)cap;
            _target[c] = vis ? Math.Min(want, _cap[c]) : 0;
            // The heading, smoothed, and the still/moving switch eased.
            var hb = _head[c];
            if (hb != 0)
            {
                float tx = DirX[hb], tz = DirZ[hb];
                float hx = _hx[c], hz = _hz[c];
                if (hx == 0 && hz == 0 || _fresh[c] != 0)
                    (hx, hz) = (tx, tz);
                else
                {
                    var k = MathF.Min(1f, dt * 2.5f);
                    hx += (tx - hx) * k;
                    hz += (tz - hz) * k;
                    var l = MathF.Sqrt(hx * hx + hz * hz);
                    if (l > 1e-4f)
                        (hx, hz) = (hx / l, hz / l);
                    else
                        (hx, hz) = (tx, tz);
                }
                _hx[c] = hx;
                _hz[c] = hz;
                _yawC[c] = MathF.Atan2(hx, -hz);
                _move[c] = _fresh[c] != 0 ? 1f : MathF.Min(1f, _move[c] + dt / 0.5f);
            }
            else
                _move[c] = _fresh[c] != 0 ? 0f : MathF.Max(0f, _move[c] - dt / 0.5f);
        }
    }

    /// <summary>The neighbouring cell along a heading (the eight directions; -1: off the grid or no heading).</summary>
    private int Downstream(int c, float hx, float hz)
    {
        int dc = MathF.Abs(hx) > 0.38f ? Math.Sign(hx) : 0, dr = MathF.Abs(hz) > 0.38f ? Math.Sign(hz) : 0;
        if (dc == 0 && dr == 0)
            return -1;
        int ux = c % Cols + dc, uz = c / Cols + dr;
        return (uint)ux < (uint)Cols && (uint)uz < (uint)Rows ? uz * Cols + ux : -1;
    }

    /// <summary>
    /// May an agent of cell <paramref name="c"/> walk into neighbouring cell <paramref name="u"/>? Only where the crowd
    /// has reached (a count there) and there is room for another agent (or out of view: it leaves the picture).
    /// </summary>
    private bool Accepts(int c, int u)
    {
        if (u < 0 || _want[u] == 0)
            return false; // (nothing there: the crowd hasn't reached it)
        if (_vis[u] == 0)
            return true; // (out of view: it leaves the picture)
        return _members[u] < _target[u];
    }

    private void FlowPass(int s)
    {
        _jobs.Range(s, _n, out var from, out var to);
        var k = MathF.Min(1f, _dt * 3f);
        for (var c = from; c < to; c++)
        {
            if (_members[c] == 0 && _target[c] == 0)
            {
                _flow[c] = 0;
                continue;
            }
            var flow = _move[c] > 0.01f && Accepts(c, Downstream(c, _hx[c], _hz[c])) ? 1f : 0f;
            _flow[c] = _fresh[c] != 0 ? flow : _flow[c] + (flow - _flow[c]) * k;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float GroundY(float x, float z) => Ground != null ? Ground.Sample(x, z) : FloorY;

    private void AgentPass(int s)
    {
        _jobs.Range(s, Count, out var from, out var to);
        var dt = _dt;
        var output = _out;
        var cam = _cam;
        var fade = dt / FadeSeconds;
        float near1 = NearRange - LodSlack, near2 = NearRange + LodSlack, mid1 = MidRange - LodSlack, mid2 = MidRange + LodSlack;
        near1 *= near1;
        near2 *= near2;
        mid1 *= mid1;
        mid2 *= mid2;
        var stripe = output?.StripeAt(s);
        for (var i = from; i < to; i++)
        {
            var c = CellOf[i];
            var st = State[i];
            if (_vis[c] == 0)
            {
                _dead[i] = 1; // (its cell left the view: dropped at once)
                continue;
            }
            _dead[i] = 0;
            switch (st)
            {
                case Mode.Claimed:
                    Timer[i] -= dt;
                    if (Timer[i] <= 0)
                        _dead[i] = 1;
                    continue; // (hidden)
                case Mode.Rising:
                    Fade[i] += fade;
                    if (Fade[i] >= 1f)
                    {
                        Fade[i] = 1f;
                        State[i] = st = Mode.Live;
                    }
                    break;
                case Mode.Sinking:
                    Fade[i] -= fade;
                    if (Fade[i] <= 0f)
                    {
                        _dead[i] = 1;
                        continue;
                    }
                    break;
            }
            float x = X[i], z = Z[i], vx, vz;
            var turnTo = float.NaN;
            if (st == Mode.Moving)
            {
                float ex = SX[i] - x, ez = SZ[i] - z;
                var el = MathF.Sqrt(ex * ex + ez * ez);
                if (el <= MoveSpeed * dt)
                {
                    (vx, vz) = (ex / MathF.Max(dt, 1e-4f), ez / MathF.Max(dt, 1e-4f));
                    State[i] = Mode.Live;
                }
                else
                {
                    (vx, vz) = (ex / el * MoveSpeed, ez / el * MoveSpeed);
                    turnTo = MathF.Atan2(vx, -vz);
                }
            }
            else
            {
                var go = _move[c] * _flow[c];
                var drift = Pace[i] * Speed * go;
                vx = _hx[c] * drift;
                vz = _hz[c] * drift;
                if (st != Mode.Sinking)
                {
                    if (go > 0.5f)
                    {
                        // Walking with the stream: where it would stop goes with it.
                        SX[i] = x + vx * dt;
                        SZ[i] = z + vz * dt;
                    }
                    else
                    {
                        float ex = SX[i] - x, ez = SZ[i] - z;
                        var el = MathF.Sqrt(ex * ex + ez * ez);
                        if (el > 0.15f)
                        {
                            var e = MathF.Min(EaseSpeed * (1f - go), el / MathF.Max(dt, 1e-4f));
                            vx += ex / el * e;
                            vz += ez / el * e;
                        }
                    }
                }
                if (_move[c] > 0.3f)
                    turnTo = _yawC[c] + ((Look[i] >> 8 & 255) / 255f - 0.5f) * 0.5f;
                // Pressing on in place: a moving cell with no room ahead (a stream sampled at the cap, or a queue) walks
                // on the spot; a packed one (a jam) shuffles, slower.
                Walk[i] += Pace[i] * Speed * _move[c] * (1f - _flow[c]) * dt * (_want[c] < JamCount ? 1f : JamPace);
            }
            float nx = x + vx * dt, nz = z + vz * dt;
            if (st != Mode.Moving && st != Mode.Sinking)
            {
                var nc = CellAt(nx, nz);
                if (nc != c)
                {
                    // Wants to cross: decided after this pass, in agent order (it waits here this frame).
                    var k = _crossN[s];
                    if (k < _crossI[s].Length)
                    {
                        _crossI[s][k] = i;
                        _crossC[s][k] = nc;
                        _crossX[s][k] = nx;
                        _crossZ[s][k] = nz;
                        _crossN[s] = k + 1;
                    }
                    (nx, nz) = (x, z);
                }
            }
            var mx = nx - x;
            var mz = nz - z;
            var moved = MathF.Sqrt(mx * mx + mz * mz);
            if (moved > 0)
            {
                X[i] = nx;
                Z[i] = nz;
                Walk[i] += moved;
                Y[i] = GroundY(nx, nz);
            }
            if (!float.IsNaN(turnTo))
            {
                var dy = turnTo - Yaw[i];
                dy -= MathF.Floor((dy + MathF.PI) / MathF.Tau) * MathF.Tau;
                var lim = TurnRate * dt;
                Yaw[i] += Math.Clamp(dy, -lim, lim);
            }
            // Level of detail, with a little slack either way.
            float lx = nx - cam.X, lz = nz - cam.Z;
            var d2 = lx * lx + lz * lz;
            int lod = Lod[i];
            if (lod == 0 && d2 > near2 || lod == 2 && d2 < mid1 || lod == 1 && (d2 < near1 || d2 > mid2))
                Lod[i] = (byte)(lod = d2 < NearRange * NearRange ? 0 : d2 < MidRange * MidRange ? 1 : 2);
            if (stripe != null && Fade[i] > 0f)
                stripe.Add(lod, nx, Y[i] - (1f - Fade[i]) * SinkDepth, nz, Yaw[i], Walk[i], output!.Bright(Look[i]), _move[c] < 0.5f);
        }
    }

    private void ResolveCrossings()
    {
        for (var s = 0; s < _jobs.Stripes; s++)
        {
            var n = _crossN[s];
            for (var k = 0; k < n; k++)
            {
                var i = _crossI[s][k];
                if (_dead[i] != 0)
                    continue;
                int c = CellOf[i], u = _crossC[s][k];
                var ok = u >= 0 && Math.Abs(u % Cols - c % Cols) <= 1 && Math.Abs(u / Cols - c / Cols) <= 1 && Accepts(c, u);
                if (!ok)
                {
                    // Blocked: it stops and finds itself a spot of its own in its cell.
                    Spot(c, Look[i] ^ (uint)Frame * 2654435761u, out SX[i], out SZ[i]);
                    continue;
                }
                _members[c]--;
                _members[u]++;
                CellOf[i] = u;
                X[i] = _crossX[s][k];
                Z[i] = _crossZ[s][k];
                Y[i] = GroundY(X[i], Z[i]);
                Crossed++;
                if (_vis[u] == 0)
                    _dead[i] = 1; // (walked out of the view)
            }
        }
    }

    /// <summary>A spot in cell <paramref name="c"/> from a hash (kept off the edges).</summary>
    private void Spot(int c, uint seed, out float x, out float z)
    {
        var h = HordeCrowd.Hash(seed ^ (uint)c * 0x9E3779B1u);
        x = MinX + (c % Cols + 0.08f + (h & 0xFFFF) / 65536f * 0.84f) * Cell;
        z = MinZ + (c / Cols + 0.08f + (h >> 16) / 65536f * 0.84f) * Cell;
    }

    private void RemoveDead()
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            if (_dead[i] == 0)
                continue;
            if (State[i] != Mode.Sinking)
            {
                _members[CellOf[i]]--;
                Dropped++;
            }
            RemoveAt(i);
        }
    }

    private void RemoveAt(int i)
    {
        var last = --Count;
        if (i != last)
        {
            X[i] = X[last];
            Z[i] = Z[last];
            Y[i] = Y[last];
            Yaw[i] = Yaw[last];
            Walk[i] = Walk[last];
            Fade[i] = Fade[last];
            SX[i] = SX[last];
            SZ[i] = SZ[last];
            Pace[i] = Pace[last];
            Timer[i] = Timer[last];
            State[i] = State[last];
            CellOf[i] = CellOf[last];
            Look[i] = Look[last];
            Id[i] = Id[last];
            Lod[i] = Lod[last];
        }
        _dead[i] = _dead[last];
        _dead[last] = 0;
    }

    private void Grow(int n)
    {
        if (X.Length >= n)
            return;
        n = Math.Max(n, X.Length * 2);
        Array.Resize(ref X, n);
        Array.Resize(ref Z, n);
        Array.Resize(ref Y, n);
        Array.Resize(ref Yaw, n);
        Array.Resize(ref Walk, n);
        Array.Resize(ref Fade, n);
        Array.Resize(ref SX, n);
        Array.Resize(ref SZ, n);
        Array.Resize(ref Pace, n);
        Array.Resize(ref Timer, n);
        Array.Resize(ref State, n);
        Array.Resize(ref CellOf, n);
        Array.Resize(ref Look, n);
        Array.Resize(ref Id, n);
        Array.Resize(ref Lod, n);
        Array.Resize(ref _dead, n);
        _items = new int[n];
        _listFrame = -1;
    }

    /// <summary>A new agent in cell <paramref name="c"/> at (x, z).</summary>
    private int Spawn(int c, float x, float z, Mode mode, float fade, uint look)
    {
        if (Count >= X.Length)
            Grow(Count + 1);
        var i = Count++;
        var id = _nextId++;
        if (look == 0)
            look = HordeCrowd.Hash(id * 2654435761u ^ 0xC0FFEEu) | 1;
        X[i] = SX[i] = x;
        Z[i] = SZ[i] = z;
        Y[i] = GroundY(x, z);
        Look[i] = look;
        Id[i] = id;
        Yaw[i] = _move[c] > 0.3f || _head[c] != 0 ? _yawC[c] + ((look >> 8 & 255) / 255f - 0.5f) * 0.5f : (look >> 3 & 1023) / 1023f * MathF.Tau;
        Walk[i] = (look >> 10 & 255) / 64f;
        Fade[i] = fade;
        Pace[i] = 0.8f + 0.4f * ((look >> 4 & 255) / 255f);
        Timer[i] = 0;
        State[i] = mode;
        CellOf[i] = c;
        float lx = x - _cam.X, lz = z - _cam.Z;
        var d2 = lx * lx + lz * lz;
        Lod[i] = (byte)(d2 < NearRange * NearRange ? 0 : d2 < MidRange * MidRange ? 1 : 2);
        _dead[i] = 0;
        if (mode != Mode.Sinking)
            _members[c]++;
        return i;
    }

    /// <summary>Per cell, its agents that count there (live, rising, moving there, claimed): <see cref="_start"/>/<see cref="_items"/>.</summary>
    private void BuildLists()
    {
        Array.Clear(_start);
        for (var i = 0; i < Count; i++)
            if (State[i] != Mode.Sinking)
                _start[CellOf[i] + 1]++;
        for (var c = 0; c < _n; c++)
            _start[c + 1] += _start[c];
        // (fill using _start as cursors, then shift back)
        for (var i = 0; i < Count; i++)
            if (State[i] != Mode.Sinking)
                _items[_start[CellOf[i]]++] = i;
        for (var c = _n; c > 0; c--)
            _start[c] = _start[c - 1];
        _start[0] = 0;
        _listFrame = Frame;
        _listCount = Count;
    }

    private void Events()
    {
        for (var e = 0; e < _evN; e++)
        {
            float x = _evX[e], z = _evZ[e];
            var c = CellAt(x, z);
            if (c < 0 || _vis[c] == 0)
                continue;
            switch (_evKind[e])
            {
                case 0: // claim: the nearest counted agent near there hides
                {
                    var i = Nearest(c, x, z, 3f, claimable: true);
                    if (i >= 0)
                    {
                        State[i] = Mode.Claimed;
                        Timer[i] = ClaimSeconds;
                        Claims++;
                    }
                    break;
                }
                case 1: // hint: one appears right there (counted; if the count never comes, it sinks after a moment)
                    Spawn(c, x, z, Mode.Live, 1f, _evLook[e]);
                    Filled++;
                    break;
                default: // release: the nearest sinks
                {
                    var i = Nearest(c, x, z, Cell * 1.5f, claimable: true);
                    if (i >= 0)
                        StartSink(i);
                    break;
                }
            }
        }
        _evN = 0;
    }

    /// <summary>The nearest counted agent to (x, z) within <paramref name="within"/> m, in cell <paramref name="c"/> and its neighbours (-1: none).</summary>
    private int Nearest(int c, float x, float z, float within, bool claimable)
    {
        var best = -1;
        var bd = within * within;
        int cc = c % Cols, cr = c / Cols;
        for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
            {
                int ux = cc + dc, uz = cr + dr;
                if ((uint)ux >= (uint)Cols || (uint)uz >= (uint)Rows)
                    continue;
                var u = uz * Cols + ux;
                for (var k = _start[u]; k < _start[u + 1]; k++)
                {
                    var i = _items[k];
                    if (i >= Count || CellOf[i] != u || claimable && State[i] is Mode.Claimed or Mode.Sinking)
                        continue;
                    var d = (X[i] - x) * (X[i] - x) + (Z[i] - z) * (Z[i] - z);
                    if (d < bd)
                        (bd, best) = (d, i);
                }
            }
        return best;
    }

    private void StartSink(int i)
    {
        if (State[i] == Mode.Sinking)
            return;
        _members[CellOf[i]]--;
        if (State[i] == Mode.Claimed)
        {
            // (hidden already: gone at once)
            State[i] = Mode.Sinking;
            Fade[i] = 0f;
            return;
        }
        State[i] = Mode.Sinking;
        Sunk++;
    }

    private void Rebalance()
    {
        var dt = _dt;
        for (var c = 0; c < _n; c++)
        {
            var diff = _members[c] - _target[c];
            if (diff == 0)
            {
                _surT[c] = _defT[c] = 0;
                continue;
            }
            if (diff > 0)
            {
                _defT[c] = 0;
                // Hidden ones first (an individual took their place): gone at once.
                for (var k = _start[c]; k < _start[c + 1] && diff > 0; k++)
                {
                    var i = _items[k];
                    if (i < Count && CellOf[i] == c && State[i] == Mode.Claimed)
                    {
                        StartSink(i);
                        diff--;
                    }
                }
                if (_vis[c] == 0)
                    continue;
                // Passed to a neighbour that is short (downstream first): they walk across.
                var passes = 0;
                var down = Downstream(c, _hx[c], _hz[c]);
                while (diff > 0 && passes < 4)
                {
                    var u = down >= 0 && _vis[down] != 0 && _members[down] < _target[down] ? down : ShortNeighbour(c);
                    if (u < 0)
                        break;
                    if (!PassTo(c, u))
                        break;
                    diff--;
                    passes++;
                }
                if (diff <= 0)
                {
                    _surT[c] = 0;
                    continue;
                }
                _surT[c] += dt;
                if (_surT[c] < SurplusSeconds)
                    continue;
                _surT[c] = 0;
                // The rest sink (the last counted first: rising ones before those that have stood longest).
                for (var k = _start[c + 1] - 1; k >= _start[c] && diff > 0; k--)
                {
                    var i = _items[k];
                    if (i < Count && CellOf[i] == c && State[i] is Mode.Live or Mode.Rising)
                    {
                        StartSink(i);
                        diff--;
                    }
                }
            }
            else
            {
                _surT[c] = 0;
                if (_vis[c] == 0)
                    continue;
                var fresh = _fresh[c] != 0;
                if (!fresh)
                {
                    _defT[c] += dt;
                    if (_defT[c] < DeficitSeconds)
                        continue;
                }
                _defT[c] = 0;
                for (; diff < 0; diff++)
                {
                    Spot(c, _nextId * 0x85EBCA77u, out var x, out var z);
                    Spawn(c, x, z, fresh ? Mode.Live : Mode.Rising, fresh ? 1f : 0f, 0);
                    if (fresh)
                        Filled++;
                    else
                        Raised++;
                }
            }
        }
    }

    /// <summary>A neighbouring cell in view with fewer agents than it should have (-1: none).</summary>
    private int ShortNeighbour(int c)
    {
        int cc = c % Cols, cr = c / Cols;
        var best = -1;
        var most = 0;
        for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
            {
                int ux = cc + dc, uz = cr + dr;
                if ((dc | dr) == 0 || (uint)ux >= (uint)Cols || (uint)uz >= (uint)Rows)
                    continue;
                var u = uz * Cols + ux;
                var shortBy = _target[u] - _members[u];
                if (_vis[u] != 0 && shortBy > most)
                    (most, best) = (shortBy, u);
            }
        return best;
    }

    /// <summary>Cell <paramref name="c"/> passes its agent nearest cell <paramref name="u"/> across to it.</summary>
    private bool PassTo(int c, int u)
    {
        float ux = MinX + (u % Cols + 0.5f) * Cell, uz = MinZ + (u / Cols + 0.5f) * Cell;
        var best = -1;
        var bd = float.MaxValue;
        for (var k = _start[c]; k < _start[c + 1]; k++)
        {
            var i = _items[k];
            if (i >= Count || CellOf[i] != c || State[i] != Mode.Live)
                continue;
            var d = (X[i] - ux) * (X[i] - ux) + (Z[i] - uz) * (Z[i] - uz);
            if (d < bd)
                (bd, best) = (d, i);
        }
        if (best < 0)
            return false;
        // Its spot: just inside the new cell, the nearest point of it.
        float x0 = MinX + u % Cols * Cell, z0 = MinZ + u / Cols * Cell;
        var m = Cell * 0.12f;
        SX[best] = Math.Clamp(X[best], x0 + m, x0 + Cell - m);
        SZ[best] = Math.Clamp(Z[best], z0 + m, z0 + Cell - m);
        State[best] = Mode.Moving;
        CellOf[best] = u;
        _members[c]--;
        _members[u]++;
        Passed++;
        return true;
    }

    /// <summary>Every agent gone (a new map or session).</summary>
    public void Clear()
    {
        Count = 0;
        if (_n > 0)
        {
            Array.Clear(_members);
            Array.Clear(_vis);
            Array.Fill(_visFrame, int.MinValue);
            Array.Clear(_hx);
            Array.Clear(_hz);
            Array.Clear(_cap);
        }
        _evN = 0;
        _listFrame = -1;
        _fillAll = true;
    }

    // ---- Placement API (shots into the far crowd and the like) ---------------------------------------------

    /// <summary>Where agent <paramref name="i"/> stands (its feet; sunk by its fade).</summary>
    public Vector3 Position(int i) => new(X[i], Y[i] - (1f - Fade[i]) * SinkDepth, Z[i]);

    /// <summary>Is agent <paramref name="i"/> one that stands for a crowd zombie in its cell (not hidden, not going)?</summary>
    public bool Standing(int i) => State[i] is Mode.Live or Mode.Rising or Mode.Moving;

    /// <summary>
    /// The first standing agent a ray from <paramref name="origin"/> along <paramref name="dir"/> (unit) hits within
    /// <paramref name="maxDist"/> m (each a vertical capsule of radius <paramref name="radius"/>, 1.8 m tall); -1 if none.
    /// Walks the grid's cells along the ray. <paramref name="t"/>: the distance along the ray.
    /// </summary>
    public int RayHit(Vector3 origin, Vector3 dir, float maxDist, out float t, float radius = 0.35f)
    {
        t = maxDist;
        if (_n == 0 || Count == 0)
            return -1;
        if (_listFrame != Frame || _listCount != Count)
            BuildLists();
        var best = -1;
        // 2D DDA over the cells the ray's flat shadow crosses, widened by a cell either side for the capsules' radius.
        var flat = new Vector2(dir.X, dir.Z);
        var fl = flat.Length();
        var steps = (int)(maxDist * fl / (Cell * 0.5f)) + 2;
        var lastCell = -1;
        for (var k = 0; k <= steps; k++)
        {
            var s = Math.Min(maxDist, k * Cell * 0.5f / MathF.Max(fl, 1e-4f));
            if (fl < 1e-4f && k > 0)
                break;
            var p = origin + dir * s;
            var c = CellAt(p.X, p.Z);
            if (c == lastCell)
                continue;
            lastCell = c;
            if (s > t + Cell * 2)
                break;
            if (c < 0)
                continue;
            int cc = c % Cols, cr = c / Cols;
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                {
                    int ux = cc + dc, uz = cr + dr;
                    if ((uint)ux >= (uint)Cols || (uint)uz >= (uint)Rows)
                        continue;
                    var u = uz * Cols + ux;
                    for (var j = _start[u]; j < _start[u + 1]; j++)
                    {
                        var i = _items[j];
                        if (i >= Count || !Standing(i))
                            continue;
                        if (RayCapsule(origin, dir, Position(i), radius, 1.8f, out var hit) && hit < t)
                            (t, best) = (hit, i);
                    }
                }
        }
        return best;
    }

    private static bool RayCapsule(Vector3 o, Vector3 d, Vector3 foot, float r, float h, out float t)
    {
        // A vertical cylinder test (ends flat), good enough at crowd distances.
        t = 0;
        float ox = o.X - foot.X, oz = o.Z - foot.Z;
        float a = d.X * d.X + d.Z * d.Z, b = 2 * (ox * d.X + oz * d.Z), cc = ox * ox + oz * oz - r * r;
        if (a < 1e-8f)
            return false;
        var disc = b * b - 4 * a * cc;
        if (disc < 0)
            return false;
        var sq = MathF.Sqrt(disc);
        var t0 = (-b - sq) / (2 * a);
        var t1 = (-b + sq) / (2 * a);
        if (t1 < 0)
            return false;
        var tt = t0 >= 0 ? t0 : t1;
        var y = o.Y + d.Y * tt - foot.Y;
        if (y < 0 || y > h)
            return false;
        t = tt;
        return true;
    }

    /// <summary>The nearest standing agent to (x, z) within <paramref name="within"/> m (at most a cell), -1 if none.</summary>
    public int NearestStanding(float x, float z, float within)
    {
        if (_n == 0 || Count == 0)
            return -1;
        var c = CellAt(x, z);
        if (c < 0)
            return -1;
        if (_listFrame != Frame || _listCount != Count)
            BuildLists();
        return Nearest(c, x, z, MathF.Min(within, Cell), claimable: true);
    }

    /// <summary>The agent with number <paramref name="id"/> (<see cref="Id"/>), -1 if it is gone.</summary>
    public int IndexOfId(uint id)
    {
        for (var i = 0; i < Count; i++)
            if (Id[i] == id)
                return i;
        return -1;
    }

    /// <summary>The count the agents follow at (x, z) (0 off the grid).</summary>
    public int WantAt(float x, float z)
    {
        var c = CellAt(x, z);
        return c < 0 ? 0 : _want[c];
    }

    /// <summary>The ground under (x, z) as the agents stand on it.</summary>
    public float GroundAt(float x, float z) => GroundY(x, z);

    /// <summary>Agent <paramref name="i"/> is killed (it sinks; its cell raises another if the count says so).</summary>
    public void Kill(int i)
    {
        if ((uint)i < (uint)Count)
            StartSink(i);
    }
}
