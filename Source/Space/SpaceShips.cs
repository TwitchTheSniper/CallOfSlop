namespace CallOfSlop.Space;

/// <summary>How a ship is being moved this tick (docs/space/SPACE.md, "Ships").</summary>
public enum ShipMode : byte
{
    /// <summary>The symplectic step: an exact Kepler drift round the primary, kicks from everything else.</summary>
    Coast,
    /// <summary>On its own Kepler rail round the primary (perturbations too small to matter): exact, and as cheap at 100,000x as at 1x.</summary>
    Rail,
    /// <summary>The adaptive step (Dormand-Prince 5(4) with error control): burns, force hooks that matter, strong perturbers.</summary>
    Adaptive,
}

[Flags]
public enum ShipFlags : byte
{
    None = 0,
    /// <summary>Never put on rails.</summary>
    NoRails = 1,
    /// <summary>Always the adaptive step.</summary>
    ForceAdaptive = 2,
    /// <summary>Keep the primary it has (references and tests: integrate a whole flight in one frame).</summary>
    NoHandover = 4,
}

/// <summary>
/// A force hook (docs/space/SPACE.md): an extra acceleration on ships, added to the bodies' gravity in every step (gravity
/// technology, anomalies, repulsors, later light pressure). Called many times a tick: must not allocate and must be a pure
/// function of its inputs (the predictor calls it at future times).
/// </summary>
public interface ISpaceForce
{
    /// <summary>Acceleration in m/s^2, ecliptic axes, on a ship at an ecliptic position and velocity at a time.</summary>
    Vec3d Accel(SpacePos pos, Vec3d vel, SpaceTime t);
}

/// <summary>
/// A force hook that knows which ship it acts on (a gravity well on a ship pulls the others, never its own ship;
/// ConstructGravity.cs).
/// </summary>
public interface IShipForce : ISpaceForce
{
    /// <summary>As <see cref="ISpaceForce.Accel"/>, on ship index <paramref name="ship"/> (-1: none in particular).</summary>
    Vec3d AccelOn(int ship, SpacePos pos, Vec3d vel, SpaceTime t);
}

/// <summary>A fixed point mass (an anomaly, a gravity-tech beacon): the simplest force hook.</summary>
public sealed class PointMassForce(SpacePos at, double mu) : ISpaceForce
{
    public SpacePos At = at;
    public double Mu = mu;

    public Vec3d Accel(SpacePos pos, Vec3d vel, SpaceTime t)
    {
        var d = At - pos;
        var d2 = d.LengthSquared;
        return d2 > 0 ? d * (Mu / (d2 * Math.Sqrt(d2))) : Vec3d.Zero;
    }

    /// <summary>The potential energy per kg at a position (for energy checks).</summary>
    public double Potential(SpacePos pos) => -Mu / (At - pos).Length;
}

/// <summary>
/// Ships, wrecks and thrown things outside a planetoid's bubble (docs/space/SPACE.md, the architecture's phase 2):
/// massless test particles in the summed gravity of a <see cref="StarSystem"/>'s bodies on rails, plus thrust and force
/// hooks. Data-oriented arrays by ship index, stepped on the host once per tick to the space clock's time.
/// <para>
/// Each ship is held relative to its <b>primary</b>, the smallest body whose sphere of influence it is in, as a double
/// offset and velocity in that body's inertial frame (ecliptic axes, moving and accelerating with the body along its
/// rails). Crossing a sphere of influence hands it over to the parent or the child (exact fixed-point differences). Three
/// steps: <see cref="ShipMode.Coast"/> (Wisdom-Holman kick-drift-kick, the drift an exact universal Kepler step round the
/// primary, the kick everything else: symplectic, so energy doesn't drift over thousands of orbits),
/// <see cref="ShipMode.Adaptive"/> (Dormand-Prince 5(4) with error control, for burns and strong perturbers) and
/// <see cref="ShipMode.Rail"/> (patched conic: its own two-body orbit from an epoch, exact at any speed, used only while the
/// other bodies' pull is under <see cref="RailTolerance"/> of the primary's). Nothing allocates per step.
/// </para>
/// </summary>
public sealed class SpaceShips
{
    // ---- Settings ----------------------------------------------------------------------------------------------------

    /// <summary>The coasting step is at most this fraction of the local orbit (and of every perturber's time scale).</summary>
    public double StepsPerOrbit = 48;

    /// <summary>The coasting substeps a ship may need per game tick before it limits the speed (also the most in one chunk).</summary>
    public int MaxSubsteps = 64;

    /// <summary>
    /// The most substeps a chunk takes before step sizes are looked at again (a fraction of a local orbit or of a
    /// perturber's pass).
    /// </summary>
    public int ChunkSubsteps = 8;

    /// <summary>The perturbation (other bodies and hooks over the primary's pull) under which a coasting ship goes on rails.</summary>
    public double RailTolerance = 1e-6;

    /// <summary>
    /// High time speed (<see cref="WarpRails"/>): a coasting ship goes on rails under this perturbation instead of
    /// <see cref="RailTolerance"/>, as space games put ships on rails at high warp. Round Glacier the moonlet's tide is about
    /// 6 x 10^-6 of Glacier's pull at a low orbit, so the old tolerance kept those ships coasting, and their step budget held
    /// time at about 15,000x (2026-11-17).
    /// </summary>
    public double WarpRailTolerance = 0.02;

    /// <summary>
    /// Set by the flight while time runs faster than <see cref="SpaceTimeRule.WarpRailsAbove"/>: coasting ships with small
    /// perturbations take patched-conic rails (exact at any speed, and steady to draw), and come off again as it slows.
    /// </summary>
    public bool WarpRails;

    /// <summary>The most chunks a tick is cut into to look for rail ships crossing a sphere of influence (rails: one Kepler solve each).</summary>
    public int MaxRailChunks = 256;

    /// <summary>The shortest rail chunk (s): near a sphere's edge the time speed limit takes over instead.</summary>
    public const double RailStepLeast = 4;

    /// <summary>
    /// Per body, the radius under which a ship in space stops being one (a planetoid's sky's edge, m; 0: none, set by
    /// the game). A coasting ship whose orbit dips under it holds the time speed so it takes several ticks to get there,
    /// instead of jumping past the edge (or the ground) in one sped-up tick.
    /// </summary>
    public readonly double[] Sky;

    /// <summary>The perturbation above which a coasting ship takes the adaptive step (the Kepler drift assumes it small).</summary>
    public double AdaptiveRatio = 0.1;

    /// <summary>The adaptive step's error bounds per step: position (m), velocity (m/s), and relative to the state's size.</summary>
    public double PosTolerance = 1e-4, VelTolerance = 1e-7, RelTolerance = 1e-14;

    /// <summary>The adaptive steps one ship may take per game tick before it limits the speed.</summary>
    public int AdaptiveStepsPerTick = 16;

    /// <summary>The most substeps <see cref="MaxSubsteps"/> may be set to (the grid's size).</summary>
    public const int MaxGrid = 1 << 10;

    /// <summary>How far past a sphere of influence a ship goes before it's handed to the parent (so it can't flicker).</summary>
    public const double SoiHysteresis = 1.02;

    // ---- State ------------------------------------------------------------------------------------------------------

    public readonly StarSystem System;
    public readonly int Capacity;
    public int Count { get; private set; }
    public SpaceTime Time { get; private set; }

    /// <summary>The force hooks every ship feels (shared with the predictor).</summary>
    public readonly List<ISpaceForce> Forces;

    /// <summary>Thrust per ship (m/s^2, ecliptic axes), set by the host each tick; non-zero takes the adaptive step.</summary>
    public readonly Vec3d[] Thrust;

    /// <summary>
    /// Is any ship pushed this step? A burn is real time (the time speed rule): one tick at 100,000x is half an hour, and a
    /// burn the busy test hadn't seen yet (it reads the last tick's flames) put a ship 70 km/s off in one tick (2026-11-16).
    /// </summary>
    public bool AnyThrust()
    {
        for (var i = 0; i < Count; i++)
            if (Thrust[i] != Vec3d.Zero)
                return true;
        return false;
    }

    public readonly ShipFlags[] Flags;

    /// <summary>
    /// Each ship's id (1 to 65535, then round again), kept when <see cref="Remove"/> moves the last ship into a gap: what
    /// the wire names a ship by (phase 3, <c>Msg.Space</c>), since indices shift.
    /// </summary>
    public readonly ushort[] Ids;

    private ushort _nextId;

    private readonly int[] _primary;
    private readonly Vec3d[] _r, _v;
    private readonly ShipMode[] _mode;
    private readonly SpaceTime[] _railEpoch;
    private readonly Vec3d[] _railR, _railV;
    private readonly double[] _h, _hMax;

    // The coasting step's last perturbation (its time and primary): the next step's opening kick and the mode check reuse it.
    private readonly Vec3d[] _aEnd;
    private readonly long[] _aNs;
    private readonly int[] _sub;

    // Rail ships within one Advance: when each is next looked at (ns; between looks its state is left alone, exactly,
    // since a rail is a function of time), the step it was given at its last look (s), and whether it reached its
    // primary's sky in this tick and waits there for the rest of it.
    private readonly long[] _railNext;
    private readonly double[] _railStep;
    private readonly bool[] _skyHeld;

    // Bodies: GM, sphere of influence, children (flattened).
    private readonly int _nb;
    private readonly double[] _mu, _soi;

    private readonly int[] _childStart, _children;

    // Body states on the chunk's time grid (grid point g, body b at g * _nb + b): position, rail velocity, rail acceleration.
    private readonly SpacePos[] _gPos;
    private readonly Vec3d[] _gVel, _gAcc;
    private readonly long[] _gNs;

    // Bodies now (the end of the last step) and at one arbitrary time (the adaptive step's stages).
    private readonly SpacePos[] _cPos, _sPos;
    private readonly Vec3d[] _cVel, _cAcc, _sVel, _sAcc;

    private SpaceShips? _predictor;

    // Interpolating the bodies across a short chunk (the adaptive step's stages at 1x) instead of a Kepler solve each.
    private readonly double[] _aN4;
    private bool _interp;
    private int _gLast;

    /// <summary>The fastest speed every ship can follow accurately within its per-tick step budget (rails: no limit).</summary>
    public int MaxSpeed { get; private set; } = SpaceClock.MaxSpeed;

    /// <summary>Counters for tests and the log: hand-overs, rail entries, Kepler solves that failed (should stay 0).</summary>
    public long Handovers, RailEntries, DriftFailures, AdaptiveSteps, CoastSteps, SkyHolds;

    private long _advanceTo;

    public SpaceShips(StarSystem system, int capacity, SpaceTime start = default, List<ISpaceForce>? forces = null)
    {
        System = system;
        Capacity = capacity;
        Time = start;
        Forces = forces ?? [];
        Thrust = new Vec3d[capacity];
        Flags = new ShipFlags[capacity];
        Ids = new ushort[capacity];
        _primary = new int[capacity];
        _r = new Vec3d[capacity];
        _v = new Vec3d[capacity];
        _mode = new ShipMode[capacity];
        _railEpoch = new SpaceTime[capacity];
        _railR = new Vec3d[capacity];
        _railV = new Vec3d[capacity];
        _h = new double[capacity];
        _hMax = new double[capacity];
        _aEnd = new Vec3d[capacity];
        _aNs = new long[capacity];
        _sub = new int[capacity];
        _railNext = new long[capacity];
        _railStep = new double[capacity];
        _skyHeld = new bool[capacity];

        var bodies = system.Bodies;
        _nb = bodies.Length;
        if (_nb == 0 || bodies[0].Parent >= 0)
            throw new ArgumentException("a star system starts with its star");
        _mu = new double[_nb];
        _soi = new double[_nb];
        Sky = new double[_nb];
        _aN4 = new double[_nb];
        var counts = new int[_nb + 1];
        for (var b = 0; b < _nb; b++)
        {
            _mu[b] = bodies[b].Mu;
            if (b > 0)
            {
                if (bodies[b].Parent < 0 || bodies[b].Parent >= b)
                    throw new ArgumentException($"body {b} should come after its parent");
                counts[bodies[b].Parent]++;
            }
            _soi[b] = SphereOfInfluence(system, b);
            if (b > 0)
            {
                var o = bodies[b].Orbit;
                _aN4[b] = o.A * o.N * o.N * o.N * o.N; // a cubic's error over a step dt is about A (N dt)^4 / 384
            }
        }
        _childStart = new int[_nb + 1];
        for (var b = 0; b < _nb; b++)
            _childStart[b + 1] = _childStart[b] + counts[b];
        _children = new int[Math.Max(1, _nb - 1)];
        var fill = new int[_nb];
        for (var b = 1; b < _nb; b++)
        {
            var p = bodies[b].Parent;
            _children[_childStart[p] + fill[p]++] = b;
        }

        _gPos = new SpacePos[(MaxGrid + 1) * _nb];
        _gVel = new Vec3d[(MaxGrid + 1) * _nb];
        _gAcc = new Vec3d[(MaxGrid + 1) * _nb];
        _gNs = new long[MaxGrid + 1];
        _cPos = new SpacePos[_nb];
        _cVel = new Vec3d[_nb];
        _cAcc = new Vec3d[_nb];
        _sPos = new SpacePos[_nb];
        _sVel = new Vec3d[_nb];
        _sAcc = new Vec3d[_nb];
        EvalBodies(Time.Ns, _cPos, _cVel, _cAcc, 0);
    }

    /// <summary>
    /// A body's sphere of influence (Laplace: a (m / M)^0.4, metres): inside it the body is the better frame to fly in.
    /// The star's is infinite.
    /// </summary>
    public static double SphereOfInfluence(StarSystem sys, int body)
    {
        var b = sys.Bodies[body];
        if (b.Parent < 0)
            return double.PositiveInfinity;
        return b.Orbit.A * Math.Pow(b.Mu / sys.Bodies[b.Parent].Mu, 0.4);
    }

    public double SoiOf(int body) => _soi[body];

    // ---- Ships ------------------------------------------------------------------------------------------------------

    /// <summary>Adds a ship at an ecliptic position and velocity at <see cref="Time"/>; returns its index (-1: full).</summary>
    public int Add(SpacePos pos, Vec3d vel, ShipFlags flags = ShipFlags.None)
    {
        if (Count >= Capacity)
            return -1;
        var i = Count++;
        Flags[i] = flags;
        Ids[i] = NextId();
        Thrust[i] = Vec3d.Zero;
        _h[i] = 0;
        _aNs[i] = long.MinValue;
        var p = PrimaryAt(pos);
        _primary[i] = p;
        _r[i] = pos - _cPos[p];
        _v[i] = vel - _cVel[p];
        _mode[i] = ShipMode.Coast;
        SelectMode(i);
        return i;
    }

    /// <summary>Adds a ship relative to a body (offset and velocity in its inertial frame) at <see cref="Time"/>.</summary>
    public int AddRelative(int body, Vec3d r, Vec3d v, ShipFlags flags = ShipFlags.None)
        => Add(_cPos[body] + r, _cVel[body] + v, flags);

    /// <summary>Removes a ship; the last ship takes its index.</summary>
    public void Remove(int i)
    {
        var last = --Count;
        if (i == last)
            return;
        Thrust[i] = Thrust[last];
        Flags[i] = Flags[last];
        Ids[i] = Ids[last];
        _primary[i] = _primary[last];
        _r[i] = _r[last];
        _v[i] = _v[last];
        _mode[i] = _mode[last];
        _railEpoch[i] = _railEpoch[last];
        _railR[i] = _railR[last];
        _railV[i] = _railV[last];
        _h[i] = _h[last];
        _hMax[i] = _hMax[last];
        _aEnd[i] = _aEnd[last];
        _aNs[i] = _aNs[last];
    }

    public void Clear() => Count = 0;

    /// <summary>The index of the ship with this id, or -1.</summary>
    public int IndexOfId(ushort id)
    {
        for (var i = 0; i < Count; i++)
            if (Ids[i] == id)
                return i;
        return -1;
    }

    /// <summary>
    /// Ids another flight already uses (phase 6: ships in several systems share one id space, so a construct, a packet and
    /// a gravity well name a ship by id alone). Null: only this flight's.
    /// </summary>
    public Func<ushort, bool>? IdTaken;

    /// <summary>Adds a ship with a given id (a jump from another system keeps its ship's id); -1 when full or the id is in use.</summary>
    public int AddWithId(ushort id, SpacePos pos, Vec3d vel, ShipFlags flags = ShipFlags.None)
    {
        if (id == 0 || IndexOfId(id) >= 0)
            return -1;
        var i = Add(pos, vel, flags);
        if (i >= 0)
            Ids[i] = id;
        return i;
    }

    private ushort NextId()
    {
        // Skip 0 and any id still in use (only matters after 65,535 adds).
        while (true)
        {
            if (++_nextId == 0)
                _nextId = 1;
            if (IndexOfId(_nextId) < 0 && IdTaken?.Invoke(_nextId) != true)
                return _nextId;
        }
    }

    /// <summary>Overwrites a ship's state (a host's snapshot on a client, a loaded save) in its primary's frame.</summary>
    public void SetState(int i, int primary, Vec3d r, Vec3d v)
    {
        _primary[i] = primary;
        _r[i] = r;
        _v[i] = v;
        _h[i] = 0;
        _aNs[i] = long.MinValue;
        _mode[i] = ShipMode.Coast;
        SelectMode(i);
    }

    public ShipMode ModeOf(int i) => _mode[i];
    public int PrimaryOf(int i) => _primary[i];

    /// <summary>The ship's offset and velocity in its primary's inertial frame (what goes in a snapshot).</summary>
    public void RelativeOf(int i, out int primary, out Vec3d r, out Vec3d v)
    {
        primary = _primary[i];
        r = _r[i];
        v = _v[i];
    }

    /// <summary>The ship's position in the ecliptic frame at <see cref="Time"/>.</summary>
    public SpacePos PositionOf(int i) => _cPos[_primary[i]] + _r[i];

    /// <summary>The ship's ecliptic (inertial) velocity at <see cref="Time"/>.</summary>
    public Vec3d VelocityOf(int i) => _cVel[_primary[i]] + _v[i];

    /// <summary>A body's position at <see cref="Time"/> (cached: no Kepler solve).</summary>
    public SpacePos BodyPosition(int body) => _cPos[body];
    public Vec3d BodyVelocity(int body) => _cVel[body];

    /// <summary>
    /// The specific orbital energy round the primary (v^2/2 - mu/r, J/kg). With only the primary it's constant; with
    /// perturbers it moves.
    /// </summary>
    public double KeplerEnergy(int i) => _v[i].LengthSquared / 2 - _mu[_primary[i]] / _r[i].Length;

    /// <summary>The smallest body whose sphere of influence holds an ecliptic position at <see cref="Time"/>.</summary>
    public int PrimaryAt(SpacePos pos)
    {
        var cur = 0;
        for (var guard = 0; guard < StarSystem.MaxDepth + 1; guard++)
        {
            var next = -1;
            for (var k = _childStart[cur]; k < _childStart[cur + 1]; k++)
            {
                var c = _children[k];
                if ((pos - _cPos[c]).Length < _soi[c])
                {
                    next = c;
                    break;
                }
            }
            if (next < 0)
                break;
            cur = next;
        }
        return cur;
    }

    // ---- Stepping ---------------------------------------------------------------------------------------------------

    /// <summary>Moves every ship (and <see cref="Time"/>) on to a later time. Spans needing many steps are split into chunks.</summary>
    public void Advance(SpaceTime to)
    {
        var total = to.Ns - Time.Ns;
        if (total <= 0)
            return;
        var dt = total / (double)SpaceTime.NsPerSecond;
        var need = 1.0;
        for (var i = 0; i < Count; i++)
            if (_mode[i] == ShipMode.Coast)
                need = Math.Max(need, dt / (_hMax[i] = MaxStep(i)));
        // A chunk covers at most ChunkSubsteps of each ship's steps as they are at its start, so the step sizes are
        // looked at again before a ship gets much closer to anything (a pass that tightens them mid-chunk).
        var chunks = (long)Math.Ceiling(need / Math.Clamp(Math.Min(ChunkSubsteps, MaxSubsteps), 1, MaxGrid));
        if (chunks < 1 || double.IsNaN(need))
            chunks = 1;
        // Ships on rails are looked at often enough not to pass through a sphere of influence (or under a sky) between
        // two looks: one tick at 1,000,000x is 4.6 hours, and a ship would fly through a moon's sphere unseen. Each is
        // looked at only when its own step is up (a look is a Kepler solve and the mode's check), so a ship near an
        // edge cuts the tick into many chunks without the others paying for them.
        var railNeed = 1.0;
        for (var i = 0; i < Count; i++)
        {
            _skyHeld[i] = false;
            if (_mode[i] != ShipMode.Rail)
                continue;
            _railStep[i] = RailStep(i);
            _railNext[i] = Time.Ns + (long)Math.Min(total, _railStep[i] * SpaceTime.NsPerSecond);
            railNeed = Math.Max(railNeed, dt / _railStep[i]);
        }
        if (railNeed > chunks)
            chunks = (long)Math.Min(Math.Ceiling(railNeed), Math.Max(chunks, MaxRailChunks));
        _advanceTo = to.Ns;
        var start = Time.Ns;
        var span = total;
        var recut = false;
        for (long c = 1; c <= chunks; c++)
        {
            var hv = Handovers;
            StepChunk(c == chunks ? to.Ns : start + (long)((Int128)span * c / chunks), c == 1);
            if (Handovers == hv || c == chunks || recut)
                continue;
            // A ship changed primary mid-tick (into a moon's sphere, say): what is left of the tick is cut again for
            // its new steps (its sky among them), once a tick.
            var rest = (to.Ns - Time.Ns) / (double)SpaceTime.NsPerSecond;
            var need2 = 1.0;
            for (var i = 0; i < Count; i++)
                if (_mode[i] == ShipMode.Rail)
                    need2 = Math.Max(need2, rest / _railStep[i]);
            if (need2 <= chunks - c)
                continue;
            recut = true;
            start = Time.Ns;
            span = to.Ns - Time.Ns;
            chunks = (long)Math.Min(Math.Ceiling(need2), Math.Max(chunks - c, MaxRailChunks));
            c = 0;
        }
        UpdateSpeedLimit();
    }

    /// <summary>Jumps to a time without moving the ships' relative states (a client taking a host's clock with fresh states).</summary>
    public void SetTime(SpaceTime t)
    {
        Time = t;
        EvalBodies(t.Ns, _cPos, _cVel, _cAcc, 0);
        for (var i = 0; i < Count; i++)
            if (_mode[i] == ShipMode.Rail)
                EnterRails(i);
    }

    private void StepChunk(long endNs, bool first)
    {
        var t0 = Time.Ns;
        var dtNs = endNs - t0;
        var dt = dtNs / (double)SpaceTime.NsPerSecond;

        // How many substeps each coasting ship needs (powers of two, so every ship's kicks fall on the shared grid).
        var nMax = 1;
        var cap = Math.Clamp(MaxSubsteps, 1, MaxGrid);
        for (var i = 0; i < Count; i++)
        {
            if (_mode[i] != ShipMode.Coast)
                continue;
            var want = dt / (first ? _hMax[i] : MaxStep(i));
            var n = 1;
            while (n < want && n < cap)
                n <<= 1;
            _sub[i] = n;
            if (n > nMax)
                nMax = n;
        }

        // The bodies on the grid: point 0 is now (already known), the rest one Kepler solve per body per point.
        Array.Copy(_cPos, 0, _gPos, 0, _nb);
        Array.Copy(_cVel, 0, _gVel, 0, _nb);
        Array.Copy(_cAcc, 0, _gAcc, 0, _nb);
        _gNs[0] = t0;
        for (var g = 1; g <= nMax; g++)
        {
            _gNs[g] = g == nMax ? endNs : t0 + (long)((Int128)dtNs * g / nMax);
            EvalBodies(_gNs[g], _gPos, _gVel, _gAcc, g * _nb);
        }

        _gLast = nMax;
        var worst = 0.0;
        for (var b = 1; b < _nb; b++)
            worst = Math.Max(worst, _aN4[b]);
        var dt2 = dt * dt;
        _interp = worst * dt2 * dt2 / 384 < 1e-6 && worst * dt2 / 8 < 1e-9;

        var final = endNs >= _advanceTo;
        for (var i = 0; i < Count; i++)
        {
            if (_skyHeld[i] || _mode[i] == ShipMode.Rail && Thrust[i] == Vec3d.Zero && !final && endNs < _railNext[i])
                continue; // (held at its sky for the rest of the tick, or a rail ship whose next look isn't due)
            if (_mode[i] != ShipMode.Adaptive && Thrust[i] != Vec3d.Zero)
            {
                _mode[i] = ShipMode.Adaptive; // thrust set since the last step (a rail ship's state is current)
                _h[i] = 0;
            }
            switch (_mode[i])
            {
                case ShipMode.Rail:
                    RailLook(i, t0, endNs);
                    break;
                case ShipMode.Coast:
                    StepCoast(i, nMax);
                    break;
                default:
                    StepAdaptive(i, t0, endNs);
                    break;
            }
        }

        Time = new SpaceTime(endNs);
        var last = nMax * _nb;
        Array.Copy(_gPos, last, _cPos, 0, _nb);
        Array.Copy(_gVel, last, _cVel, 0, _nb);
        Array.Copy(_gAcc, last, _cAcc, 0, _nb);
        for (var i = 0; i < Count; i++)
        {
            if (_skyHeld[i])
            {
                if (_mode[i] == ShipMode.Rail)
                    EnterRails(i); // (it waits where it reached the sky: its rail starts again from there)
                continue;
            }
            if (_mode[i] == ShipMode.Rail && !final && endNs < _railNext[i])
                continue;
            if (_mode[i] == ShipMode.Rail && Handover(i, _cPos, _cVel, 0))
                _mode[i] = ShipMode.Coast; // its rail was round the old primary
            SelectMode(i);
            if (_mode[i] == ShipMode.Rail)
            {
                _railStep[i] = RailStep(i);
                _railNext[i] = endNs + (long)Math.Min(long.MaxValue / 4, _railStep[i] * SpaceTime.NsPerSecond);
            }
        }
    }

    /// <summary>
    /// A rail ship's look at the end of a chunk: its rail carried there. A ship that went under its primary's sky in the
    /// chunk is put back where it crossed (found along its rail) and waits there for the rest of the tick, so the game
    /// sees it at the sky's edge (to land, or to come back into the bubble) instead of past it or under the ground.
    /// </summary>
    private void RailLook(int i, long t0, long endNs)
    {
        var mu = _mu[_primary[i]];
        var sky = Sky[_primary[i]];
        var wasOut = sky > 0 && _r[i].LengthSquared >= sky * sky;
        if (!UniversalKepler.PropagateNs(mu, _railR[i], _railV[i], endNs - _railEpoch[i].Ns, out _r[i], out _v[i]))
            DriftFailures++;
        if (!wasOut || _r[i].LengthSquared >= sky * sky)
            return;
        long lo = Math.Max(t0, _railEpoch[i].Ns), hi = endNs;
        for (var k = 0; k < 40 && hi - lo > 1000; k++)
        {
            var mid = lo + (hi - lo) / 2;
            if (UniversalKepler.PropagateNs(mu, _railR[i], _railV[i], mid - _railEpoch[i].Ns, out var rm, out _) && rm.LengthSquared < sky * sky)
                hi = mid;
            else
                lo = mid;
        }
        UniversalKepler.PropagateNs(mu, _railR[i], _railV[i], hi - _railEpoch[i].Ns, out _r[i], out _v[i]);
        _skyHeld[i] = true;
        SkyHolds++;
    }

    /// <summary>Wisdom-Holman kick-drift-kick on the grid.</summary>
    private void StepCoast(int i, int nMax)
    {
        var n = _sub[i];
        var stride = nMax / n;
        var r = _r[i];
        var v = _v[i];
        var p = _primary[i];
        var a = _aNs[i] == _gNs[0] ? _aEnd[i] : Perturbation(i, p, r, v, _gPos, _gVel, _gAcc, 0, _gNs[0]);
        for (var k = 0; k < n; k++)
        {
            var ga = k * stride;
            var gb = ga + stride;
            var h = (_gNs[gb] - _gNs[ga]) / (double)SpaceTime.NsPerSecond;
            v += a * (h / 2);
            if (!UniversalKepler.Propagate(_mu[p], r, v, h, out r, out v))
                DriftFailures++;
            // The closing half-kick in the frame the step began in, then the hand-over (if any), then the next step's
            // opening kick in the new frame: a kick split across two frames would lose half a step of the new
            // primary's pull.
            a = Perturbation(i, p, r, v, _gPos, _gVel, _gAcc, gb * _nb, _gNs[gb]);
            v += a * (h / 2);
            _r[i] = r;
            _v[i] = v;
            _primary[i] = p;
            if (Handover(i, _gPos, _gVel, gb * _nb))
            {
                p = _primary[i];
                r = _r[i];
                v = _v[i];
                a = Perturbation(i, p, r, v, _gPos, _gVel, _gAcc, gb * _nb, _gNs[gb]);
            }
            CoastSteps++;
        }
        _r[i] = r;
        _v[i] = v;
        _aEnd[i] = a;
        _aNs[i] = _gNs[nMax];
    }

    // Dormand-Prince 5(4) coefficients.
    private const double A21 = 1.0 / 5;
    private const double A31 = 3.0 / 40, A32 = 9.0 / 40;
    private const double A41 = 44.0 / 45, A42 = -56.0 / 15, A43 = 32.0 / 9;
    private const double A51 = 19372.0 / 6561, A52 = -25360.0 / 2187, A53 = 64448.0 / 6561, A54 = -212.0 / 729;
    private const double A61 = 9017.0 / 3168, A62 = -355.0 / 33, A63 = 46732.0 / 5247, A64 = 49.0 / 176, A65 = -5103.0 / 18656;
    private const double B1 = 35.0 / 384, B3 = 500.0 / 1113, B4 = 125.0 / 192, B5 = -2187.0 / 6784, B6 = 11.0 / 84;
    private const double E1 = 71.0 / 57600, E3 = -71.0 / 16695, E4 = 71.0 / 1920, E5 = -17253.0 / 339200, E6 = 22.0 / 525, E7 = -1.0 / 40;

    /// <summary>The adaptive step from t0 to t1 (ns), in the primary's frame, handing over between accepted steps.</summary>
    private void StepAdaptive(int i, long t0, long t1)
    {
        var total = (t1 - t0) / (double)SpaceTime.NsPerSecond;
        var p = _primary[i];
        var r = _r[i];
        var v = _v[i];
        var h = _h[i] > 0 ? _h[i] : Math.Max(1e-3, MaxStep(i) / 64);
        var t = 0.0;
        var k1v = Accel(i, p, r, v, t0);
        for (var guard = 0; guard < 200_000 && t < total; guard++)
        {
            var last = h >= total - t;
            var hs = last ? total - t : h;
            long Ns(double c) => t0 + (long)Math.Round((t + c * hs) * SpaceTime.NsPerSecond);
            var k1r = v;
            var r2 = r + k1r * (hs * A21); var v2 = v + k1v * (hs * A21);
            var k2r = v2; var k2v = Accel(i, p, r2, v2, Ns(1.0 / 5));
            var r3 = r + (k1r * A31 + k2r * A32) * hs; var v3 = v + (k1v * A31 + k2v * A32) * hs;
            var k3r = v3; var k3v = Accel(i, p, r3, v3, Ns(3.0 / 10));
            var r4 = r + (k1r * A41 + k2r * A42 + k3r * A43) * hs; var v4 = v + (k1v * A41 + k2v * A42 + k3v * A43) * hs;
            var k4r = v4; var k4v = Accel(i, p, r4, v4, Ns(4.0 / 5));
            var r5 = r + (k1r * A51 + k2r * A52 + k3r * A53 + k4r * A54) * hs; var v5 = v + (k1v * A51 + k2v * A52 + k3v * A53 + k4v * A54) * hs;
            var k5r = v5; var k5v = Accel(i, p, r5, v5, Ns(8.0 / 9));
            var r6 = r + (k1r * A61 + k2r * A62 + k3r * A63 + k4r * A64 + k5r * A65) * hs;
            var v6 = v + (k1v * A61 + k2v * A62 + k3v * A63 + k4v * A64 + k5v * A65) * hs;
            var k6r = v6; var endNs = last ? t1 : Ns(1); var k6v = Accel(i, p, r6, v6, endNs);
            var rn = r + (k1r * B1 + k3r * B3 + k4r * B4 + k5r * B5 + k6r * B6) * hs;
            var vn = v + (k1v * B1 + k3v * B3 + k4v * B4 + k5v * B5 + k6v * B6) * hs;
            var k7r = vn; var k7v = Accel(i, p, rn, vn, endNs);
            var er = (k1r * E1 + k3r * E3 + k4r * E4 + k5r * E5 + k6r * E6 + k7r * E7) * hs;
            var ev = (k1v * E1 + k3v * E3 + k4v * E4 + k5v * E5 + k6v * E6 + k7v * E7) * hs;
            var err = Math.Max(er.Length / (PosTolerance + RelTolerance * rn.Length), ev.Length / (VelTolerance + RelTolerance * vn.Length));
            if (!double.IsFinite(err))
                err = 1e9;
            if (err <= 1)
            {
                t = last ? total : t + hs;
                r = rn;
                v = vn;
                k1v = k7v;
                AdaptiveSteps++;
                // The scratch bodies hold the step's end (k7's evaluation): hand over there.
                _r[i] = r;
                _v[i] = v;
                _primary[i] = p;
                if (Handover(i, _sPos, _sVel, 0))
                {
                    p = _primary[i];
                    r = _r[i];
                    v = _v[i];
                    k1v = Accel(i, p, r, v, endNs);
                }
            }
            var grow = err > 0 ? 0.9 * Math.Pow(err, -0.2) : 5;
            var nh = hs * Math.Clamp(grow, 0.2, 5);
            if (!(last && err <= 1))
                h = nh;
            else if (nh < h)
                h = nh;
        }
        if (t < total)
            DriftFailures++; // gave up (shouldn't happen): the ship is left where it got to
        _r[i] = r;
        _v[i] = v;
        _primary[i] = p;
        _h[i] = h;
    }

    /// <summary>The full acceleration in the primary's frame at an arbitrary time (bodies into the scratch arrays).</summary>
    private Vec3d Accel(int i, int p, Vec3d r, Vec3d v, long ns)
    {
        BodiesAt(ns);
        var r2 = r.LengthSquared;
        return r * (-_mu[p] / (r2 * Math.Sqrt(r2))) + Perturbation(i, p, r, v, _sPos, _sVel, _sAcc, 0, ns);
    }

    /// <summary>
    /// Everything but the primary's own pull, in the primary's frame: the other bodies' gravity, less the primary frame's
    /// own acceleration along its rails, plus thrust and the force hooks.
    /// </summary>
    private Vec3d Perturbation(int i, int p, Vec3d r, Vec3d v, SpacePos[] pos, Vec3d[] vel, Vec3d[] acc, int off, long ns)
    {
        var pp = pos[off + p];
        var a = Thrust[i] - acc[off + p];
        for (var j = 0; j < _nb; j++)
        {
            if (j == p)
                continue;
            var d = r - (pos[off + j] - pp);
            var d2 = d.LengthSquared;
            a -= d * (_mu[j] / (d2 * Math.Sqrt(d2)));
        }
        if (p != 0)
        {
            // The primary's moons pull it as well as the ship, but its rails leave that out (a body moves only round its
            // parent), so its frame takes their pull on it as though it did: what is left of a moon's pull is the tide.
            // Without it Slopolis (13% of Glacier's mass, 140 km out) pulled ships round Glacier while Glacier stayed on
            // its path, and a 4 km orbit wandered by 1.4 km within hours (2026-11-16). The star is left fixed, as its rails are.
            for (var k = _childStart[p]; k < _childStart[p + 1]; k++)
            {
                var j = _children[k];
                var q = pp - pos[off + j];
                var q2 = q.LengthSquared;
                a += q * (_mu[j] / (q2 * Math.Sqrt(q2)));
            }
        }
        if (Forces.Count > 0)
        {
            var e = pp + r;
            var ev = vel[off + p] + v;
            var t = new SpaceTime(ns);
            for (var f = 0; f < Forces.Count; f++)
                a += Forces[f] is IShipForce sf ? sf.AccelOn(i, e, ev, t) : Forces[f].Accel(e, ev, t);
        }
        return a;
    }

    /// <summary>Moves ship i to its parent's or a child's frame if it crossed a sphere of influence. True if it did.</summary>
    private bool Handover(int i, SpacePos[] pos, Vec3d[] vel, int off)
    {
        if ((Flags[i] & ShipFlags.NoHandover) != 0)
            return false;
        var moved = false;
        for (var guard = 0; guard < 2 * StarSystem.MaxDepth + 2; guard++)
        {
            var p = _primary[i];
            int to;
            if (p != 0 && _r[i].Length > _soi[p] * SoiHysteresis)
                to = System.Bodies[p].Parent;
            else
            {
                to = -1;
                for (var k = _childStart[p]; k < _childStart[p + 1]; k++)
                {
                    var c = _children[k];
                    if ((_r[i] - (pos[off + c] - pos[off + p])).Length < _soi[c])
                    {
                        to = c;
                        break;
                    }
                }
                if (to < 0)
                    break;
            }
            _r[i] += pos[off + p] - pos[off + to];
            _v[i] += vel[off + p] - vel[off + to];
            _primary[i] = to;
            _h[i] = 0;
            moved = true;
            Handovers++;
        }
        return moved;
    }

    /// <summary>Picks the step for the next tick from the ship's state now.</summary>
    private void SelectMode(int i)
    {
        var flags = Flags[i];
        var was = _mode[i];
        ShipMode mode;
        if ((flags & ShipFlags.ForceAdaptive) != 0 || Thrust[i] != Vec3d.Zero)
            mode = ShipMode.Adaptive;
        else
        {
            var p = _primary[i];
            var r = _r[i];
            var pert = _mode[i] == ShipMode.Coast && _aNs[i] == Time.Ns ? _aEnd[i] : Perturbation(i, p, r, _v[i], _cPos, _cVel, _cAcc, 0, Time.Ns);
            var ratio = pert.Length * r.LengthSquared / _mu[p];
            if (ratio > (was == ShipMode.Adaptive ? AdaptiveRatio / 2 : AdaptiveRatio))
                mode = ShipMode.Adaptive;
            else if ((flags & ShipFlags.NoRails) == 0 && ratio < (was == ShipMode.Rail ? 4 : 1) * (WarpRails ? Math.Max(RailTolerance, WarpRailTolerance) : RailTolerance))
                mode = ShipMode.Rail;
            else
                mode = ShipMode.Coast;
        }
        if (mode == ShipMode.Rail && was != ShipMode.Rail)
            EnterRails(i);
        if (mode != ShipMode.Adaptive)
            _h[i] = 0;
        if (mode != ShipMode.Coast)
            _aNs[i] = long.MinValue;
        else if (was != ShipMode.Coast)
            _hMax[i] = 0;
        _mode[i] = mode;
    }

    private void EnterRails(int i)
    {
        _railEpoch[i] = Time;
        _railR[i] = _r[i];
        _railV[i] = _v[i];
        RailEntries++;
    }

    /// <summary>The longest coasting step for ship i now: a fraction of its orbit's and every perturber's time scale.</summary>
    private double MaxStep(int i)
    {
        var p = _primary[i];
        var r = _r[i];
        var v = _v[i];
        // A bound orbit's step comes from its period (steady from step to step, which keeps the step symplectic); an
        // escape's from its distance.
        var energy = v.LengthSquared / 2 - _mu[p] / r.Length;
        var h = energy < 0 ? Dynamical(-_mu[p] / (2 * energy), _mu[p]) : Dynamical(r.Length, _mu[p]);
        var pp = _cPos[p];
        for (var j = 0; j < _nb; j++)
        {
            if (j == p)
                continue;
            var d = (r - (_cPos[j] - pp)).Length;
            h = Math.Min(h, Dynamical(d, _mu[j]));
            var vr = (v - (_cVel[j] - _cVel[p])).Length;
            if (vr > 0)
                h = Math.Min(h, d / vr); // passing a perturber: its pull turns on the time scale d / v
        }
        // Rounded down to a power of two: the step then changes in rare factor-of-two jumps, not a little every chunk
        // (steps that keep changing size lose the symplectic step's cancelling errors).
        h /= StepsPerOrbit;
        return h > 0 && double.IsFinite(h) ? Math.ScaleB(1, Math.ILogB(h)) : h;
    }

    private static double Dynamical(double r, double mu) => mu > 0 ? 2 * Math.PI * Math.Sqrt(r * r * r / mu) : double.PositiveInfinity;

    private void UpdateSpeedLimit()
    {
        const double tick = 1.0 / 60;
        var speed = (double)SpaceClock.MaxSpeed;
        for (var i = 0; i < Count; i++)
        {
            switch (_mode[i])
            {
                case ShipMode.Coast:
                    speed = Math.Min(speed, MaxSubsteps * (_hMax[i] > 0 ? _hMax[i] : MaxStep(i)) / tick);
                    break;
                case ShipMode.Adaptive:
                    var h = _h[i] > 0 ? _h[i] : MaxStep(i) / 64;
                    speed = Math.Min(speed, AdaptiveStepsPerTick * h / tick);
                    break;
                default:
                    speed = Math.Min(speed, MaxRailChunks * RailStep(i) / tick);
                    break;
            }
            // A ship whose orbit dips under its primary's sky takes at least four ticks to get there.
            var p = _primary[i];
            var sky = Sky[p];
            if (sky > 0 && _mode[i] != ShipMode.Adaptive)
            {
                var r = _r[i].Length;
                if (r > sky && Periapsis(_mu[p], _r[i], _v[i]) < sky)
                    speed = Math.Min(speed, (r - sky) / Math.Max(1, _v[i].Length) / (4 * tick));
                else if (r <= sky && Vec3d.Dot(_r[i], _v[i]) < 0)
                {
                    // Under the sky (held at its edge, or waiting to come back into the bubble), falling: four ticks to the ground.
                    var down = -Vec3d.Dot(_r[i], _v[i]) / r;
                    speed = Math.Min(speed, Math.Max(0, r - System.Bodies[p].Radius) / Math.Max(1, down) / (4 * tick));
                }
            }
        }
        MaxSpeed = (int)Math.Clamp(Math.Floor(speed), 1, SpaceClock.MaxSpeed);
    }

    private static double Periapsis(double mu, Vec3d r, Vec3d v)
    {
        var energy = v.LengthSquared / 2 - mu / r.Length;
        var h2 = Vec3d.Cross(r, v).LengthSquared;
        var e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h2 / (mu * mu)));
        return h2 / mu / (1 + e);
    }

    /// <summary>
    /// The longest a ship on rails may go between looks: half the time it would take, at its speed relative to each,
    /// to reach the edge of its primary's sphere of influence or of a child's whose orbit its own can reach.
    /// </summary>
    private double RailStep(int i)
    {
        var p = _primary[i];
        var r = _r[i];
        var v = _v[i];
        var rl = r.Length;
        var mu = _mu[p];
        var energy = v.LengthSquared / 2 - mu / rl;
        var h2 = Vec3d.Cross(r, v).LengthSquared;
        var e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h2 / (mu * mu)));
        var peri = h2 / mu / (1 + e);
        var apo = energy < 0 && e < 1 ? h2 / mu / (1 - e) : double.PositiveInfinity;
        var step = double.PositiveInfinity;
        if (p != 0 && apo > _soi[p])
            step = Math.Max(0, _soi[p] * SoiHysteresis - rl) / Math.Max(1e-3, v.Length);
        var bodies = System.Bodies;
        for (var k = _childStart[p]; k < _childStart[p + 1]; k++)
        {
            var c = _children[k];
            var o = bodies[c].Orbit;
            if (apo < o.A * (1 - o.E) - _soi[c] || peri > o.A * (1 + o.E) + _soi[c])
                continue; // (their paths never come within its sphere)
            var gap = (r - (_cPos[c] - _cPos[p])).Length - _soi[c];
            var vr = (v - (_cVel[c] - _cVel[p])).Length;
            step = Math.Min(step, Math.Max(0, gap) / Math.Max(1e-3, vr));
        }
        var sky = Sky[p];
        if (sky > 0 && rl > sky && peri < sky)
            step = Math.Min(step, (rl - sky) / Math.Max(1e-3, v.Length)); // (and its sky's edge, when it dips under it)
        return Math.Max(RailStepLeast, step * 0.5);
    }

    /// <summary>
    /// A ship's ecliptic position and velocity at a time near <see cref="Time"/> (a frame drawn between two ticks): its
    /// two-body orbit round its primary carried from the last step (from its rail's epoch when on one), its primary
    /// where its rails have it then. The drawn path and mark then move smoothly with the drawn bodies at any time speed.
    /// </summary>
    public void StateAt(int i, SpaceTime t, out SpacePos pos, out Vec3d vel)
    {
        var p = _primary[i];
        Vec3d r, v;
        var ok = _mode[i] == ShipMode.Rail
            ? UniversalKepler.PropagateNs(_mu[p], _railR[i], _railV[i], t.Ns - _railEpoch[i].Ns, out r, out v)
            : UniversalKepler.PropagateNs(_mu[p], _r[i], _v[i], t.Ns - Time.Ns, out r, out v);
        if (!ok)
        {
            r = _r[i];
            v = _v[i];
        }
        System.StateOf(p, t, out var bp, out var bv);
        pos = bp + r;
        vel = bv + v;
    }

    /// <summary>
    /// The bodies at a time inside the current chunk, into the scratch arrays: a cubic Hermite between the chunk's ends
    /// when the chunk is short enough for it to be exact to well under a millimetre, otherwise a Kepler solve per body.
    /// </summary>
    private void BodiesAt(long ns)
    {
        var t0 = _gNs[0];
        var t1 = _gNs[_gLast];
        if (!_interp || t1 <= t0)
        {
            EvalBodies(ns, _sPos, _sVel, _sAcc, 0);
            return;
        }
        var span = (t1 - t0) / (double)SpaceTime.NsPerSecond;
        var u = (ns - t0) / (double)(t1 - t0);
        var u2 = u * u;
        var u3 = u2 * u;
        var h10 = (u3 - 2 * u2 + u) * span;
        var h01 = -2 * u3 + 3 * u2;
        var h11 = (u3 - u2) * span;
        var d10 = 3 * u2 - 4 * u + 1;
        var d01 = (-6 * u2 + 6 * u) / span;
        var d11 = 3 * u2 - 2 * u;
        var last = _gLast * _nb;
        for (var b = 0; b < _nb; b++)
        {
            var p0 = _gPos[b];
            var d = _gPos[last + b] - p0;
            var v0 = _gVel[b];
            var v1 = _gVel[last + b];
            _sPos[b] = p0 + (v0 * h10 + d * h01 + v1 * h11);
            _sVel[b] = v0 * d10 + d * d01 + v1 * d11;
            _sAcc[b] = _gAcc[b] + (_gAcc[last + b] - _gAcc[b]) * u;
        }
    }

    /// <summary>Every body's position, rail velocity and rail acceleration at a time, into the arrays at an offset.</summary>
    private void EvalBodies(long ns, SpacePos[] pos, Vec3d[] vel, Vec3d[] acc, int off)
    {
        var t = new SpaceTime(ns);
        var bodies = System.Bodies;
        pos[off] = SpacePos.Origin;
        vel[off] = Vec3d.Zero;
        acc[off] = Vec3d.Zero;
        for (var b = 1; b < _nb; b++)
        {
            var body = bodies[b];
            var q = off + body.Parent;
            body.Orbit.StateAt(t, out var rp, out var vp);
            var r2 = rp.LengthSquared;
            pos[off + b] = pos[q] + rp;
            vel[off + b] = vel[q] + vp;
            acc[off + b] = acc[q] + rp * (-body.Orbit.Mu / (r2 * Math.Sqrt(r2)));
        }
    }

    // ---- Prediction -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The trajectory predictor (a map view's path): where ship i will be every <paramref name="every"/> seconds from now if
    /// it coasts (no thrust), with the same steps the host uses. Writes ecliptic positions into <paramref name="ecl"/>
    /// (the first point is now), and, if <paramref name="inFrame"/> isn't empty, the same points in <paramref name="frame"/>
    /// (a body's inertial frame shows a path round it; its body-fixed frame shows it over the ground). Returns the points
    /// written. No allocation after the first call.
    /// </summary>
    public int Predict(int i, double every, Span<SpacePos> ecl, FrameRef frame, Span<Vec3d> inFrame)
    {
        var sc = _predictor ??= new SpaceShips(System, 1, Time, Forces);
        sc.CopySettings(this);
        sc.Clear();
        sc.SetTime(Time);
        sc.Add(PositionOf(i), VelocityOf(i), Flags[i] & ~ShipFlags.ForceAdaptive);
        sc.SetState(0, _primary[i], _r[i], _v[i]);
        var stepNs = (long)Math.Round(every * SpaceTime.NsPerSecond);
        for (var k = 0; k < ecl.Length; k++)
        {
            if (k > 0)
                sc.Advance(new SpaceTime(Time.Ns + stepNs * k));
            ecl[k] = sc.PositionOf(0);
            if (k < inFrame.Length)
                inFrame[k] = System.PoseOf(frame, sc.Time).FromEcliptic(ecl[k]);
        }
        return ecl.Length;
    }

    private void CopySettings(SpaceShips o)
    {
        StepsPerOrbit = o.StepsPerOrbit;
        MaxSubsteps = o.MaxSubsteps;
        ChunkSubsteps = o.ChunkSubsteps;
        RailTolerance = o.RailTolerance;
        AdaptiveRatio = o.AdaptiveRatio;
        PosTolerance = o.PosTolerance;
        VelTolerance = o.VelTolerance;
        RelTolerance = o.RelTolerance;
        AdaptiveStepsPerTick = o.AdaptiveStepsPerTick;
        WarpRailTolerance = o.WarpRailTolerance;
        WarpRails = o.WarpRails;
        MaxRailChunks = o.MaxRailChunks;
        Array.Copy(o.Sky, Sky, Sky.Length);
    }
}
