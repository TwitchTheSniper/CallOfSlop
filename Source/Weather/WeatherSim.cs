namespace CallOfSlop;

/// <summary>A tropical cyclone in <see cref="WeatherSim"/>: an axisymmetric vortex with its own intensity budget.</summary>
public struct Storm
{
    public int Id;
    public bool Alive;

    /// <summary>Centre, metres from the domain's centre (x east, y north).</summary>
    public double X, Y;

    /// <summary>Maximum sustained wind (m/s), the radius it blows at (m) and the radius past which the vortex is gone (m).</summary>
    public float Vmax, Rmax, Router;

    /// <summary>Motion over the ground, m/s (steering flow plus beta drift).</summary>
    public float Vx, Vy;

    /// <summary>Hours since genesis; the strongest wind so far (m/s).</summary>
    public double AgeHours;
    public float PeakVmax;

    /// <summary>What it last saw under its centre: sea surface temperature (C), land share 0-1, deep-layer shear (m/s).</summary>
    public float Sst, Land, Shear;

    /// <summary>Points kept on its track (oldest dropped) and where the next one goes.</summary>
    public int TrackCount, TrackHead;

    public readonly float Knots => Vmax * 1.943844f;

    /// <summary>Central pressure, hPa, from the wind (Atkinson-Holliday style wind-pressure relation).</summary>
    public readonly float Pressure => 1010f - MathF.Pow(MathF.Max(Knots, 0f) / 6.3f, 1f / 0.644f);

    /// <summary>-1 depression, 0 tropical storm, 1-5 Saffir-Simpson category.</summary>
    public readonly int Category => WeatherSim.CategoryOf(Vmax);
}

/// <summary>
/// A regional weather simulation (docs/weather/WEATHER.md): a square grid of columns, thousands of kilometres across,
/// centred on the map. It is the CPU stand-in for the "Tier 0/1" atmosphere of the weather design: a synoptic steering
/// flow (trade easterlies, a subtropical ridge that wobbles, westerlies with travelling troughs, easterly waves),
/// column water vapour and cloud water carried by the wind (semi-Lagrangian), evaporation from a warm sea that the
/// storms cool, condensation where air converges or is lifted, rain-out, and tropical cyclones after the hurricane
/// branch of Cyclogenesis (Amador Herrera et al. 2024): axisymmetric vortices with Coriolis sign, a potential-intensity
/// budget from the sea under them (shear and dry air hold them back, land kills them), steered by the flow round them
/// with beta drift. Each storm's winds (inflow low down, outflow aloft) are added to the grid's, so its moisture
/// spirals in and its cloud shield spirals out, with a clear eye.
///
/// Deterministic for a seed and allocation-free per step. A step (10 minutes of weather) can be done whole
/// (<see cref="Step"/>) or a few rows at a time (<see cref="Advance"/>) with identical results.
/// </summary>
public sealed class WeatherSim
{
    /// <summary>Seconds of weather per step.</summary>
    public const double Dt = 600.0;

    public const int MaxStorms = 8;
    public const int TrackLength = 96;

    private const double Omega = 7.2921e-5;
    private const double MetresPerDegree = 111_000.0;

    public readonly WeatherSettings Settings;
    public readonly int N;

    /// <summary>Cell width (m) and domain width (m).</summary>
    public readonly double Cell, Size;

    /// <summary>Weather time since the start (s) and steps done.</summary>
    public double Time { get; private set; }
    public long Steps { get; private set; }

    // ---- Fields (index = j * N + i; i east, j north, j = 0 the south edge) -------------------------------------------
    /// <summary>Land share 0-1 (static).</summary>
    public readonly float[] Land;

    /// <summary>Sea surface temperature, climatology (C) and the storms' cold wake on it (C, negative).</summary>
    public readonly float[] Sst, SstAnom;

    /// <summary>Low-level wind (m/s) and cloud-level wind (m/s), this step.</summary>
    public readonly float[] U, V, Uc, Vc;

    /// <summary>Column water vapour and cloud water, mm (kg/m2); rain rate, mm/h.</summary>
    public float[] Q, C;
    public readonly float[] Rain;
    private float[] _q2, _c2;

    /// <summary>A divergence-free turbulence field (unit rms), periodic on the grid, for cloud texture.</summary>
    private readonly float[] _tu, _tv;

    public readonly Storm[] Storms = new Storm[MaxStorms];

    /// <summary>Track points per storm slot: x, y (m) and Vmax (m/s), <see cref="TrackLength"/> per slot.</summary>
    public readonly float[] Tracks = new float[MaxStorms * TrackLength * 3];

    /// <summary>Storms formed so far (natural and seeded) and the strongest category reached.</summary>
    public int StormsFormed { get; private set; }
    public int StrongestCategory { get; private set; } = -1;

    private ulong _rng;
    private int _nextId = 1;

    // Step state for Advance: phase 0 = storms, 1 = winds (rows), 2 = transport and physics (rows).
    private int _phase, _row;

    // Background flow parameters for the step being done.
    private double _ridgeLat, _waveShift, _troughShift, _turbX, _turbY;
    private readonly double _wavePhase, _troughPhase, _ridgePhase;

    // The background flow split into per-row (latitude) and per-column (longitude) parts, refreshed every step, and the
    // turbulence field's whole-cell shift and bilinear weights (the same for every cell in a step).
    private readonly float[] _rowU, _rowTrades, _rowWest, _rowRidge, _colWave, _colTrough, _colRidge;
    private int _turbI, _turbJ;
    private float _turbFx, _turbFy;

    public WeatherSim(WeatherSettings settings)
    {
        Settings = settings.Clone();
        N = Settings.Cells;
        Size = Settings.SizeKm * 1000.0;
        Cell = Size / N;
        var n2 = N * N;
        Land = new float[n2];
        Sst = new float[n2];
        SstAnom = new float[n2];
        U = new float[n2];
        V = new float[n2];
        Uc = new float[n2];
        Vc = new float[n2];
        Q = new float[n2];
        C = new float[n2];
        _q2 = new float[n2];
        _c2 = new float[n2];
        Rain = new float[n2];
        _tu = new float[n2];
        _tv = new float[n2];
        _rowU = new float[N];
        _rowTrades = new float[N];
        _rowWest = new float[N];
        _rowRidge = new float[N];
        _colWave = new float[N];
        _colTrough = new float[N];
        _colRidge = new float[N];
        _rng = (ulong)(uint)Settings.Seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
        _wavePhase = NextDouble() * Math.PI * 2;
        _troughPhase = NextDouble() * Math.PI * 2;
        _ridgePhase = NextDouble() * Math.PI * 2;
        BuildStatic();
        SetBackground(0);
        for (var j = 0; j < N; j++)
            WindRow(j);
        // Start near saturation balance: humid over warm sea, drier over land; a little cloud from the noise.
        for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                var k = j * N + i;
                var ws = Saturation(AirTemp(k, j));
                Q[k] = ws * (0.72f - 0.22f * Land[k]);
                C[k] = MathF.Max(0f, _tu[k] * 0.3f);
            }
    }

    // ---- Geometry helpers -----------------------------------------------------------------------------------------

    /// <summary>The x (east) or y (north) of a cell centre, metres from the domain centre.</summary>
    public double CellX(int i) => (i + 0.5) * Cell - Size * 0.5;

    public double CellY(int j) => (j + 0.5) * Cell - Size * 0.5;

    public double LatitudeAt(double y) => Settings.Latitude + y / MetresPerDegree;

    private float LatOfRow(int j) => (float)LatitudeAt(CellY(j));

    /// <summary>+1 north of the equator (cyclones turn anticlockwise), -1 south.</summary>
    public float Hemisphere => Settings.Latitude >= 0 ? 1f : -1f;

    public static int CategoryOf(float vmax) => vmax switch
    {
        < 17.5f => -1,
        < 33f => 0,
        < 43f => 1,
        < 50f => 2,
        < 58f => 3,
        < 70f => 4,
        _ => 5,
    };

    public static string CategoryName(int cat) => cat switch
    {
        -1 => "Tropical depression",
        0 => "Tropical storm",
        _ => $"Hurricane (Cat {cat})",
    };

    // ---- Static fields --------------------------------------------------------------------------------------------

    private void BuildStatic()
    {
        var seed = (uint)Settings.Seed;
        // Turbulence: the curl of a periodic fractal stream function (so the flow is divergence-free, swirls rather than
        // streaks, and tiles the grid), eddies of about 3 to 24 cells.
        var psi = new float[N * N];
        for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                double sum = 0, amp = 1;
                for (var o = 0; o < 4; o++)
                {
                    var period = Math.Max(N / 8 >> o, 2); // lattice cells across the grid: 32, 64, 128, 256 for N = 256 (lattice of 8, 4, 2, 1 cells)
                    var lattice = N / period;
                    sum += PeriodicNoise(i / (double)lattice, j / (double)lattice, period, seed + 17u + (uint)o * 31u) * amp;
                    amp *= 0.55;
                }
                psi[j * N + i] = (float)sum;
            }
        double sumSq = 0;
        for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                int ip = (i + 1) % N, im = (i + N - 1) % N, jp = (j + 1) % N, jm = (j + N - 1) % N;
                double u = (psi[jp * N + i] - psi[jm * N + i]) * 0.5; // u = d(psi)/dy, v = -d(psi)/dx
                double v = -(psi[j * N + ip] - psi[j * N + im]) * 0.5;
                _tu[j * N + i] = (float)u;
                _tv[j * N + i] = (float)v;
                sumSq += u * u + v * v;
            }
        var norm = (float)(1.0 / Math.Sqrt(sumSq / (N * N) * 0.5 + 1e-12));
        for (var k = 0; k < N * N; k++)
        {
            _tu[k] *= norm;
            _tv[k] *= norm;
        }

        // Land: fractal noise with a continent to the west and north and open sea to the east and south (where the
        // storms are born), and always land under the map itself.
        for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                double x = CellX(i), y = CellY(j);
                var fx = x / Size;
                var fy = y / Size * Hemisphere; // "poleward" is +
                var r = Math.Sqrt(x * x + y * y);
                var s = Fbm(x / 420_000.0, y / 420_000.0, seed, 5) * 1.1
                        - 0.15
                        - 1.25 * fx
                        + 0.55 * fy
                        + 1.6 * Math.Exp(-(r / 160_000.0) * (r / 160_000.0));
                Land[j * N + i] = (float)Math.Clamp(0.5 + s * 6.0, 0.0, 1.0);
                var lat = Math.Abs(LatitudeAt(y));
                var sst = 29.6 - Math.Max(0.0, lat - 19.0) * 0.36 - Math.Max(0.0, 9.0 - lat) * 0.1
                          + (Fbm(x / 600_000.0 + 31.7, y / 600_000.0 - 12.9, seed + 7, 3) - 0.0) * 0.9;
                Sst[j * N + i] = (float)sst;
            }
    }

    // ---- Physics helpers ------------------------------------------------------------------------------------------

    /// <summary>Saturation column water (mm) for an air temperature (C): about 7% more per degree.</summary>
    public static float Saturation(float t) => 56f * MathF.Exp(0.068f * (t - 28f));

    // Saturation from a table (-20 to 44 C in 0.0625 C steps, linear): two exponentials a cell were most of a step.
    private const float SatMin = -20f, SatStep = 0.0625f;
    private static readonly float[] SatTable = BuildSatTable();

    private static float[] BuildSatTable()
    {
        var t = new float[1026];
        for (var i = 0; i < t.Length; i++)
            t[i] = Saturation(SatMin + i * SatStep);
        return t;
    }

    private static float SaturationFast(float t)
    {
        var g = Math.Clamp((t - SatMin) / SatStep, 0f, 1024f);
        var i = (int)g;
        return SatTable[i] + (SatTable[i + 1] - SatTable[i]) * (g - i);
    }

    // Each storm's tangential wind against radius (RadialSamples + 1 points from 0 to its outer radius), refreshed with
    // the storm every step, so the grid passes read a table instead of a power law.
    private const int RadialSamples = 128;
    private readonly float[] _profile = new float[MaxStorms * (RadialSamples + 1)];

    private float ProfileAt(int slot, float r, float router)
    {
        var g = r / router * RadialSamples;
        var i = (int)g;
        if (i >= RadialSamples)
            return 0f;
        var b = slot * (RadialSamples + 1) + i;
        return _profile[b] + (_profile[b + 1] - _profile[b]) * (g - i);
    }

    private float AirTemp(int k, int j) => AirTemp(k, LandTemp(j));

    private float AirTemp(int k, float landTemp)
    {
        var sea = Sst[k] + SstAnom[k] - 0.8f;
        return sea + (landTemp - sea) * Land[k];
    }

    private float LandTemp(int j) => 27.5f - MathF.Max(0f, MathF.Abs(LatOfRow(j)) - 17f) * 0.62f;

    /// <summary>Deep-layer (850-200 hPa) shear, m/s, from where the ridge is: low in the deep tropics, high in the westerlies.</summary>
    public float ShearAt(double y)
    {
        var lat = Math.Abs(LatitudeAt(y)) * 1.0;
        var t = Math.Clamp((lat - (_ridgeLat - 3.0)) / 10.0, 0.0, 1.0);
        t = t * t * (3 - 2 * t);
        return (float)(5.0 + 21.0 * t + 2.5 * Math.Sin(Time / 86400.0 * 0.9 + _troughPhase + CellYToPhase(y)));
    }

    private static double CellYToPhase(double y) => y / 700_000.0;

    private void SetBackground(double time)
    {
        var day = time / 86400.0;
        _ridgeLat = Math.Abs(Settings.Latitude) + 5.5 + 3.0 * Math.Sin(2 * Math.PI * day / 6.5 + _ridgePhase);
        _waveShift = -7.0 * time; // easterly waves drift west at 7 m/s
        _troughShift = 9.0 * time; // westerly troughs drift east
        _turbX = -4.0 * time;
        _turbY = 0.7 * time;
        for (var j = 0; j < N; j++)
        {
            var lat = Math.Abs(LatitudeAt(CellY(j)));
            BackgroundRow(lat, out var ub, out var trades, out var west, out var ridge);
            _rowU[j] = (float)ub;
            _rowTrades[j] = (float)trades;
            _rowWest[j] = (float)west;
            _rowRidge[j] = (float)ridge;
        }
        for (var i = 0; i < N; i++)
        {
            var x = CellX(i);
            BackgroundColumn(x, out var wave, out var trough, out var ridge);
            _colWave[i] = (float)wave;
            _colTrough[i] = (float)trough;
            _colRidge[i] = (float)ridge;
        }
        // Turbulence sampled at (x - turbX, y - turbY): grid position i - turbX / Cell (periodic).
        var gx = -_turbX / Cell;
        var gy = -_turbY / Cell;
        var fx = Math.Floor(gx);
        var fy = Math.Floor(gy);
        _turbFx = (float)(gx - fx);
        _turbFy = (float)(gy - fy);
        _turbI = (int)(((long)fx % N + N) % N);
        _turbJ = (int)(((long)fy % N + N) % N);
    }

    private void BackgroundRow(double lat, out double ub, out double trades, out double west, out double ridge)
    {
        var s = Math.Tanh((lat - _ridgeLat) / 4.0); // -1 in the trades, +1 in the westerlies
        ub = -5.0 * (1 - s) * 0.5 + 8.5 * (1 + s) * 0.5;
        var dt = (lat - (_ridgeLat - 13.0)) / 7.0;
        trades = Math.Exp(-dt * dt);
        west = (1 + s) * 0.5;
        var dr = (lat - _ridgeLat) / 6.0;
        ridge = Math.Exp(-dr * dr);
    }

    private void BackgroundColumn(double x, out double wave, out double trough, out double ridge)
    {
        wave = 3.2 * Math.Sin(2 * Math.PI * (x - _waveShift) / 2_400_000.0 + _wavePhase); // easterly waves
        trough = 6.0 * Math.Sin(2 * Math.PI * (x - _troughShift) / 3_800_000.0 + _troughPhase); // westerly troughs
        ridge = 1.2 * Math.Sin(2 * Math.PI * x / Size); // round the ridge: equatorward east of it, poleward west
    }

    /// <summary>The synoptic (storm-free) low-level wind at a point, m/s.</summary>
    public void Background(double x, double y, out float u, out float v)
    {
        BackgroundRow(Math.Abs(LatitudeAt(y)), out var ub, out var trades, out var west, out var ridgeRow);
        BackgroundColumn(x, out var wave, out var trough, out var ridgeCol);
        u = (float)ub;
        v = (float)((wave * trades + trough * west - ridgeCol * ridgeRow) * Hemisphere);
    }

    /// <summary>The turbulence field at cell (i, j) this step (periodic bilinear with the step's shared weights).</summary>
    private float Turb(float[] f, int i, int j)
    {
        var i0 = i + _turbI;
        if (i0 >= N) i0 -= N;
        var i1 = i0 + 1 == N ? 0 : i0 + 1;
        var j0 = j + _turbJ;
        if (j0 >= N) j0 -= N;
        var j1 = j0 + 1 == N ? 0 : j0 + 1;
        var a = f[j0 * N + i0] + (f[j0 * N + i1] - f[j0 * N + i0]) * _turbFx;
        var b = f[j1 * N + i0] + (f[j1 * N + i1] - f[j1 * N + i0]) * _turbFx;
        return a + (b - a) * _turbFy;
    }

    private int Wrap(int i)
    {
        i %= N;
        return i < 0 ? i + N : i;
    }

    /// <summary>A storm's wind at a point (m/s): low level (inflow) and cloud level (outflow); false past its outer radius.</summary>
    public bool StormWind(int slot, double x, double y, out float u, out float v, out float uc, out float vc)
    {
        u = v = uc = vc = 0;
        ref readonly var s = ref Storms[slot];
        var dx = x - s.X;
        var dy = y - s.Y;
        var r2 = dx * dx + dy * dy;
        if (r2 >= (double)s.Router * s.Router || r2 < 1)
            return false;
        var r = (float)Math.Sqrt(r2);
        var vt = ProfileAt(slot, r, s.Router);
        float rx = (float)(dx / r), ry = (float)(dy / r);
        var h = Hemisphere;
        float tx = -ry * h, ty = rx * h; // anticlockwise in the north
        var drift = vt / MathF.Max(s.Vmax, 1f) * 0.45f; // the motion shows in the wind (stronger on the right of the track)
        float mx = s.Vx * drift, my = s.Vy * drift;
        // Low level: about 20 degrees of inflow (more over land), none in the eye.
        var radial = -(0.36f + 0.34f * s.Land) * vt * Smooth(0.25f * s.Rmax, 0.9f * s.Rmax, r);
        u = tx * vt + rx * radial + mx;
        v = ty * vt + ry * radial + my;
        // Cloud level: weaker turning further out, and the outflow spreading the cloud shield.
        var tangential = 0.75f * vt * (1f - Smooth(1.5f * s.Rmax, 5f * s.Rmax, r) * 0.6f);
        radial = 0.4f * vt * Smooth(0.8f * s.Rmax, 1.6f * s.Rmax, r);
        uc = tx * tangential + rx * radial + mx;
        vc = ty * tangential + ry * radial + my;
        return true;
    }

    /// <summary>Tangential wind at radius r (m): solid-body rotation inside Rmax, a decaying power law outside, tapered to 0 at the outer radius.</summary>
    public static float VortexProfile(in Storm s, float r)
    {
        var vt = r < s.Rmax ? s.Vmax * r / s.Rmax : s.Vmax * MathF.Pow(s.Rmax / r, 0.55f);
        return vt * (1f - Smooth(0.55f * s.Router, s.Router, r));
    }

    private static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    // ---- Stepping -------------------------------------------------------------------------------------------------

    /// <summary>One whole step (10 minutes of weather).</summary>
    public void Step()
    {
        while (!Advance(int.MaxValue))
        {
        }
    }

    /// <summary>
    /// Works on the current step for up to <paramref name="rowBudget"/> rows (each phase-2 row costs one, a wind row one,
    /// the storm phase one); true when that finished a step. Results are the same however the work is split.
    /// </summary>
    public bool Advance(int rowBudget)
    {
        while (rowBudget > 0)
        {
            switch (_phase)
            {
                case 0:
                    SetBackground(Time);
                    UpdateStorms();
                    _phase = 1;
                    _row = 0;
                    rowBudget--;
                    break;
                case 1:
                    WindRow(_row++);
                    rowBudget--;
                    if (_row == N)
                    {
                        _phase = 2;
                        _row = 0;
                    }
                    break;
                default:
                    PhysicsRow(_row++);
                    rowBudget--;
                    if (_row == N)
                    {
                        (Q, _q2) = (_q2, Q);
                        (C, _c2) = (_c2, C);
                        Time += Dt;
                        Steps++;
                        _phase = 0;
                        _row = 0;
                        return true;
                    }
                    break;
            }
        }
        return false;
    }

    /// <summary>How much of the current step is done, 0-1.</summary>
    public float StepProgress => _phase == 0 ? 0f : (_phase - 1 + _row / (float)N) * 0.5f;

    private void WindRow(int j)
    {
        var y = CellY(j);
        float ru = _rowU[j], rt = _rowTrades[j], rw = _rowWest[j], rr = _rowRidge[j], h = Hemisphere;
        for (var i = 0; i < N; i++)
        {
            var x = CellX(i);
            var u = ru;
            var v = (_colWave[i] * rt + _colTrough[i] * rw - _colRidge[i] * rr) * h;
            var tu = Turb(_tu, i, j) * 1.8f;
            var tv = Turb(_tv, i, j) * 1.8f;
            float lu = u + tu, lv = v + tv;
            float cu = u * 1.25f + 1.5f + tu * 1.3f, cv = v * 1.1f + tv * 1.3f;
            for (var s = 0; s < MaxStorms; s++)
            {
                ref readonly var st = ref Storms[s];
                if (!st.Alive || Math.Abs(x - st.X) > st.Router || Math.Abs(y - st.Y) > st.Router)
                    continue;
                if (!StormWind(s, x, y, out var su, out var sv, out var scu, out var scv))
                    continue;
                lu += su;
                lv += sv;
                cu += scu;
                cv += scv;
            }
            var k = j * N + i;
            U[k] = lu;
            V[k] = lv;
            Uc[k] = cu;
            Vc[k] = cv;
        }
    }

    private float Sample(float[] f, double gx, double gy) => Sample(f, (float)gx, (float)gy);

    private float Sample(float[] f, float gx, float gy)
    {
        // Bilinear, clamped at the edges (air coming in from outside looks like the edge).
        var hi = N - 1.001f;
        gx = gx < 0f ? 0f : gx > hi ? hi : gx;
        gy = gy < 0f ? 0f : gy > hi ? hi : gy;
        int i0 = (int)gx, j0 = (int)gy;
        float fx = gx - i0, fy = gy - j0;
        var k = j0 * N + i0;
        var a = f[k] + (f[k + 1] - f[k]) * fx;
        var b = f[k + N] + (f[k + N + 1] - f[k + N]) * fx;
        return a + (b - a) * fy;
    }

    private void PhysicsRow(int j)
    {
        var dtc = (float)(Dt / Cell);
        var hours = (float)(Dt / 3600.0);
        var evapRate = (float)(Dt / (14.0 * 86400.0));
        var inv2Cell = (float)(0.5 / Cell);
        var h = Hemisphere;
        var y = CellY(j);
        var landTemp = LandTemp(j);
        // The storms that reach this row at all.
        var rowStorms = 0;
        for (var s = 0; s < MaxStorms; s++)
            if (Storms[s].Alive && Math.Abs(y - Storms[s].Y) < 0.9 * Storms[s].Router)
                rowStorms |= 1 << s;
        int jd = Math.Max(j - 1, 0), ju = Math.Min(j + 1, N - 1);
        var vScale = (float)(1.0 / ((ju - jd) * Cell));
        for (var i = 0; i < N; i++)
        {
            var k = j * N + i;
            // Semi-Lagrangian transport: vapour with the low-level wind, cloud with the cloud-level wind.
            var q = Sample(Q, i - U[k] * dtc, j - V[k] * dtc);
            var c = Sample(C, i - Uc[k] * dtc, j - Vc[k] * dtc);

            var t = AirTemp(k, landTemp);
            var ws = SaturationFast(t);
            var land = Land[k];
            var speed = MathF.Sqrt(U[k] * U[k] + V[k] * V[k]);

            // Evaporation from the sea, faster in strong wind (the storms' fuel); little from land.
            var seaWs = SaturationFast(Sst[k] + SstAnom[k] - 0.8f);
            var evap = (speed + 4f) * MathF.Max(0f, seaWs - q) * evapRate * (1f - 0.8f * land);
            q += evap;

            // Lift: low-level convergence (and the storms' bands) brings the condensation threshold down.
            int il = Math.Max(i - 1, 0), ir = Math.Min(i + 1, N - 1);
            var div = (U[j * N + ir] - U[j * N + il]) * (ir - il == 2 ? inv2Cell : inv2Cell * 2f) + (V[ju * N + i] - V[jd * N + i]) * vScale;
            var lift = Math.Clamp(-div * 9000f, -0.12f, 0.16f);
            for (var s = 0; rowStorms >> s != 0; s++)
            {
                if ((rowStorms & (1 << s)) == 0)
                    continue;
                ref readonly var st = ref Storms[s];
                var x = CellX(i);
                var dx = (float)(x - st.X);
                var dy = (float)(y - st.Y);
                var reach = 0.9f * st.Router;
                if (dx > reach || dx < -reach || dy > reach || dy < -reach)
                    continue;
                var r = MathF.Sqrt(dx * dx + dy * dy);
                if (r > reach)
                    continue;
                var strength = Math.Clamp((st.Vmax - 10f) / 40f, 0f, 1.2f);
                // Eyewall: a ring of deep convection round Rmax.
                var e = (r - st.Rmax) / (0.55f * st.Rmax);
                var ring = e < 3.5f ? MathF.Exp(-e * e) : 0f;
                // Rain bands: two logarithmic spirals curling in towards the eyewall.
                var bands = 0f;
                var gap = 0f;
                if (r > 1.2f * st.Rmax)
                {
                    var theta = MathF.Atan2(dy, dx) * h;
                    var band = 0.5f + 0.5f * MathF.Cos(2f * theta + 1.5f * MathF.Log(r / st.Rmax));
                    var reachBands = Smooth(1.2f * st.Rmax, 2.2f * st.Rmax, r) * (1f - Smooth(0.6f * st.Router, reach, r));
                    bands = band * band * reachBands;
                    // Between the bands, past the central dense overcast, the air sinks and the cloud thins.
                    gap = (1f - band) * Smooth(1.8f * st.Rmax, 2.8f * st.Rmax, r) * (1f - Smooth(0.4f * st.Router, 0.8f * st.Router, r));
                }
                lift += strength * (0.45f * ring + 0.32f * bands - 0.45f * gap);
                c *= 1f - 0.35f * gap * Math.Min(strength, 1f);
                // The eye: sinking air, clear once the storm is a hurricane.
                if (st.Vmax > 30f && r < 0.6f * st.Rmax)
                {
                    var eye = (1f - Smooth(0.25f * st.Rmax, 0.6f * st.Rmax, r)) * Math.Clamp((st.Vmax - 30f) / 15f, 0f, 1f);
                    lift -= 0.5f * eye;
                    c *= 1f - 0.45f * eye;
                }
            }

            var threshold = ws * (0.92f + 0.04f * land) * (1f - lift);
            if (q > threshold)
            {
                var d = (q - threshold) * 0.4f;
                q -= d;
                c += d;
            }
            else if (c > 0f)
            {
                // Cloud evaporates into air below its threshold, faster the drier it is.
                var dry = Math.Clamp((threshold - q) / MathF.Max(threshold, 1f), 0f, 1f);
                var ev = c * MathF.Min(1f, 0.1f + dry * 0.9f);
                c -= ev;
                q += ev;
            }
            // Rain-out (warm rain, Kessler-like: heavier clouds rain much harder).
            var rain = c * c / (c + 3f) * 0.2f * hours;
            c -= rain;
            Rain[k] = rain / hours;

            // The storms' cold wake: strong wind mixes cool water up; the sea recovers over about ten days.
            var anom = SstAnom[k];
            anom -= MathF.Max(0f, speed - 22f) * 0.0009f * hours * (1f - land);
            anom *= 1f - (float)(Dt / (10.0 * 86400.0));
            SstAnom[k] = MathF.Max(anom, -4f);

            _q2[k] = Math.Clamp(q, 0f, 120f);
            _c2[k] = Math.Clamp(c, 0f, 40f);
        }
    }

    // ---- Storms ---------------------------------------------------------------------------------------------------

    private void UpdateStorms()
    {
        var dt = (float)Dt;
        var hours = (float)(Dt / 3600.0);
        for (var s = 0; s < MaxStorms; s++)
        {
            ref var st = ref Storms[s];
            if (!st.Alive)
                continue;
            // Environment under and round the centre.
            var sst = FieldAt(Sst, st.X, st.Y) + FieldAt(SstAnom, st.X, st.Y);
            var land = FieldAt(Land, st.X, st.Y);
            var shear = ShearAt(st.Y);
            float moist = 0;
            for (var a = 0; a < 6; a++)
            {
                var ang = a * Math.PI / 3;
                double px = st.X + Math.Cos(ang) * 260_000, py = st.Y + Math.Sin(ang) * 260_000;
                var qv = FieldAt(Q, px, py) + FieldAt(C, px, py);
                var t = FieldAt(Sst, px, py) - 0.8f;
                moist += qv / Saturation(t);
            }
            moist /= 6f;
            st.Sst = sst;
            st.Land = land;
            st.Shear = shear;

            // Intensity: grow towards the potential intensity the sea allows, cut by shear and dry air; land kills it.
            var vpot = Math.Clamp(18f + 13.5f * (sst - 26f), 0f, 85f);
            var fShear = Math.Clamp(1f - (shear - 7f) / 16f, 0f, 1f);
            var fMoist = Math.Clamp((moist - 0.55f) / 0.3f, 0f, 1f);
            var target = vpot * fShear * (0.45f + 0.55f * fMoist) * (1f - land);
            var v = st.Vmax;
            if (land > 0.5f)
                v -= (v - 8f) * hours / 11f; // inland decay, e-folding in about 11 hours
            else if (v < target)
                v += v * (1f - v / target) * hours / 13f;
            else
                v -= (v - target) * hours / 20f;
            v += ((float)NextDouble() - 0.5f) * 0.25f;
            st.Vmax = v;
            st.PeakVmax = MathF.Max(st.PeakVmax, v);
            StrongestCategory = Math.Max(StrongestCategory, CategoryOf(v));
            st.Rmax = Math.Clamp(110_000f * MathF.Exp(-0.024f * (v - 15f)), 14_000f, 120_000f);
            st.Router = Math.Clamp(260_000f + 6_000f * v, 300_000f, 720_000f);
            FillProfile(s);

            // Motion: the storm-free flow averaged over a ring round it (deep-layer mean), plus beta drift poleward-west.
            float su = 0, sv = 0;
            for (var a = 0; a < 8; a++)
            {
                var ang = a * Math.PI / 4;
                Background(st.X + Math.Cos(ang) * 400_000, st.Y + Math.Sin(ang) * 400_000, out var bu, out var bv);
                su += bu + 0.8f;
                sv += bv;
            }
            var beta = 1.4f + 1.2f * MathF.Sqrt(MathF.Max(v, 0f) / 40f);
            st.Vx = su / 8f - beta * 0.75f;
            st.Vy = sv / 8f + beta * Hemisphere;
            st.X += st.Vx * dt;
            st.Y += st.Vy * dt;
            st.AgeHours += hours;

            // Track point every 3 hours.
            if (Steps % 18 == 0)
            {
                var b = (s * TrackLength + st.TrackHead) * 3;
                Tracks[b] = (float)st.X;
                Tracks[b + 1] = (float)st.Y;
                Tracks[b + 2] = v;
                st.TrackHead = (st.TrackHead + 1) % TrackLength;
                st.TrackCount = Math.Min(st.TrackCount + 1, TrackLength);
            }

            var edge = Size * 0.5 - 60_000;
            if (v < 9f && st.AgeHours > 12 || Math.Abs(st.X) > edge || Math.Abs(st.Y) > edge)
                st.Alive = false;
        }
        Genesis();
    }

    private void FillProfile(int slot)
    {
        ref readonly var st = ref Storms[slot];
        for (var n = 0; n <= RadialSamples; n++)
            _profile[slot * (RadialSamples + 1) + n] = VortexProfile(st, st.Router * n / RadialSamples);
    }

    /// <summary>Bilinear sample of a field at metres x, y (clamped to the domain).</summary>
    public float FieldAt(float[] f, double x, double y) => Sample(f, x / Cell + N * 0.5 - 0.5, y / Cell + N * 0.5 - 0.5);

    /// <summary>Total low-level or cloud-level wind at a point, m/s.</summary>
    public void WindAt(double x, double y, bool aloft, out float u, out float v)
    {
        u = FieldAt(aloft ? Uc : U, x, y);
        v = FieldAt(aloft ? Vc : V, x, y);
    }

    private void Genesis()
    {
        // Look over blocks of 16 x 16 cells for a disturbance that could become a depression: warm open sea, cloud
        // clustered, low shear, cyclonic turning in the flow, away from other storms and not too near the equator.
        var block = 16;
        var blocks = N / block;
        var alive = 0;
        for (var s = 0; s < MaxStorms; s++)
            alive += Storms[s].Alive ? 1 : 0;
        if (alive >= 4)
            return;
        var minSep = 900_000.0;
        for (var bj = 0; bj < blocks; bj++)
            for (var bi = 0; bi < blocks; bi++)
            {
                double x = CellX(bi * block + block / 2), y = CellY(bj * block + block / 2);
                var lat = Math.Abs(LatitudeAt(y));
                var edge = Size * 0.5 - 250_000;
                if (lat < 7 || Math.Abs(x) > edge || Math.Abs(y) > edge)
                    continue;
                var land = FieldAt(Land, x, y);
                var sst = FieldAt(Sst, x, y) + FieldAt(SstAnom, x, y);
                if (land > 0.05f || sst < 26.8f)
                    continue;
                var shear = ShearAt(y);
                if (shear > 13f)
                    continue;
                float cloud = 0;
                for (var dj = 0; dj < block; dj += 4)
                    for (var di = 0; di < block; di += 4)
                        cloud += C[(bj * block + dj) * N + bi * block + di];
                cloud /= (block / 4) * (block / 4);
                // Relative vorticity of the storm-free flow (cyclonic > 0 after the hemisphere sign).
                const double e = 150_000;
                Background(x + e, y, out _, out var vE);
                Background(x - e, y, out _, out var vW);
                Background(x, y + e, out var uN, out _);
                Background(x, y - e, out var uS, out _);
                var vort = ((vE - vW) - (uN - uS)) / (2 * e) * Hemisphere;
                if (vort < 2e-6 || cloud < 1.2f)
                    continue;
                var near = false;
                for (var s = 0; s < MaxStorms && !near; s++)
                    near = Storms[s].Alive && Math.Abs(Storms[s].X - x) < minSep && Math.Abs(Storms[s].Y - y) < minSep;
                if (near)
                    continue;
                var gpi = Math.Min(cloud / 2.5, 2.0) * Math.Min((sst - 26.5) / 2.0, 1.5) * Math.Min(vort / 1e-5, 2.0) * (1 - shear / 13.0);
                if (NextDouble() < gpi * 0.006)
                {
                    SpawnStorm(x, y, 13.5f);
                    return;
                }
            }
    }

    /// <summary>Starts a tropical depression at (x, y) metres from the domain centre; returns its slot or -1 if full.</summary>
    public int SpawnStorm(double x, double y, float vmax = 14f)
    {
        for (var s = 0; s < MaxStorms; s++)
        {
            if (Storms[s].Alive)
                continue;
            Storms[s] = new Storm
            {
                Id = _nextId++, Alive = true, X = x, Y = y, Vmax = vmax, PeakVmax = vmax,
                Rmax = 100_000f, Router = 340_000f,
            };
            FillProfile(s);
            StormsFormed++;
            StrongestCategory = Math.Max(StrongestCategory, CategoryOf(vmax));
            // Moisten the column round it a little, as the disturbance that made it would have.
            int ci = (int)(x / Cell + N * 0.5), cj = (int)(y / Cell + N * 0.5);
            var rad = (int)(150_000 / Cell) + 1;
            for (var j = Math.Max(cj - rad, 0); j <= Math.Min(cj + rad, N - 1); j++)
                for (var i = Math.Max(ci - rad, 0); i <= Math.Min(ci + rad, N - 1); i++)
                {
                    var k = j * N + i;
                    C[k] = MathF.Max(C[k], 2.5f);
                }
            return s;
        }
        return -1;
    }

    /// <summary>The storm name for an id (invented names, A to Z, then numbered).</summary>
    public static string NameOf(int id)
    {
        var i = id - 1;
        return i < Names.Length ? Names[i] : $"{Names[i % Names.Length]} {i / Names.Length + 1}";
    }

    private static readonly string[] Names =
    [
        "Ansel", "Brisa", "Corvin", "Dahlia", "Emeric", "Fenna", "Gideon", "Halle", "Ivo", "Juno", "Kasimir", "Liora",
        "Marten", "Nerys", "Osric", "Pella", "Quill", "Rosalind", "Soren", "Tamsin", "Ulric", "Vesna", "Wendel", "Xanthe",
        "Yorick", "Zelda",
    ];

    /// <summary>A fingerprint of the whole state (fields and storms), for determinism checks.</summary>
    public ulong Digest()
    {
        ulong h = 1469598103934665603UL;
        static ulong Mix(ulong h, uint v) => (h ^ v) * 1099511628211UL;
        for (var k = 0; k < Q.Length; k++)
        {
            h = Mix(h, BitConverter.SingleToUInt32Bits(Q[k]));
            h = Mix(h, BitConverter.SingleToUInt32Bits(C[k]));
        }
        foreach (ref readonly var s in Storms.AsSpan())
        {
            h = Mix(h, s.Alive ? 1u : 0u);
            h = Mix(h, BitConverter.SingleToUInt32Bits(s.Vmax));
            h = Mix(h, (uint)BitConverter.DoubleToInt64Bits(s.X));
            h = Mix(h, (uint)BitConverter.DoubleToInt64Bits(s.Y));
        }
        return h;
    }

    // ---- Noise and random numbers ---------------------------------------------------------------------------------

    private double NextDouble()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 11) * (1.0 / (1UL << 53));
    }

    private static double Hash(int x, int y, uint seed)
    {
        var h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return h / (double)uint.MaxValue * 2.0 - 1.0;
    }

    private static double ValueNoise(double x, double y, uint seed)
    {
        var x0 = Math.Floor(x);
        var y0 = Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        int ix = (int)x0, iy = (int)y0;
        var a = Hash(ix, iy, seed) + (Hash(ix + 1, iy, seed) - Hash(ix, iy, seed)) * fx;
        var b = Hash(ix, iy + 1, seed) + (Hash(ix + 1, iy + 1, seed) - Hash(ix, iy + 1, seed)) * fx;
        return a + (b - a) * fy;
    }

    /// <summary>Value noise that repeats every <paramref name="period"/> lattice cells in x and y.</summary>
    private static double PeriodicNoise(double x, double y, int period, uint seed)
    {
        var x0 = Math.Floor(x);
        var y0 = Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        int ix = ((int)x0 % period + period) % period, iy = ((int)y0 % period + period) % period;
        int ix1 = (ix + 1) % period, iy1 = (iy + 1) % period;
        var a = Hash(ix, iy, seed) + (Hash(ix1, iy, seed) - Hash(ix, iy, seed)) * fx;
        var b = Hash(ix, iy1, seed) + (Hash(ix1, iy1, seed) - Hash(ix, iy1, seed)) * fx;
        return a + (b - a) * fy;
    }

    private static double Fbm(double x, double y, uint seed, int octaves)
    {
        double sum = 0, amp = 0.5, f = 1;
        for (var o = 0; o < octaves; o++)
        {
            sum += ValueNoise(x * f, y * f, seed + (uint)o * 101u) * amp;
            amp *= 0.5;
            f *= 2.03;
        }
        return sum;
    }
}
