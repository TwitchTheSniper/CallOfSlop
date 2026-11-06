using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The running weather on this PC (docs/weather/WEATHER.md): one <see cref="WeatherSim"/> for the current map when it
/// has a "weather" section, stepped on its own worker thread so a frame never waits for it. The main thread moves the
/// target time on (<see cref="Speed"/> minutes of weather per real second), reads the worker's published snapshot
/// (storms, the cloud field under the player) and uploads the sky's cloud textures and the weather map picture.
///
/// Visual only, like the sky: every PC runs its own copy from the same seed (same storms at the same weather time),
/// nothing is sent over the network yet. No allocations per frame or per step once started.
/// </summary>
public static unsafe class Weather
{
    /// <summary>Snapshot of what is happening at the player (worker-computed after each step).</summary>
    public struct Local
    {
        public float WindU, WindV, CloudU, CloudV, Cover, Rain;
        public int NearestStorm;
        public float NearestKm;
    }

    // ---- Settings the UI changes (main thread) --------------------------------------------------------------------

    /// <summary>Minutes of weather per real second.</summary>
    public static float Speed = 60f;

    public static bool Paused;

    /// <summary>The weather map window is open: the worker draws its picture after each step.</summary>
    public static volatile bool MapWanted;

    /// <summary>Rain radar on the weather map instead of the satellite view.</summary>
    public static volatile bool Radar;

    /// <summary>Bumped when the weather map needs a fresh picture now (opened, radar toggled), even while paused.</summary>
    public static volatile int MapRequest;

    // ---- State ----------------------------------------------------------------------------------------------------

    private static WeatherSettings? _settings;
    private static WeatherSim? _sim; // touched by the worker only, once started
    private static Thread? _thread;
    private static readonly AutoResetEvent Wake = new(false);
    private static readonly object Lock = new();
    private static volatile bool _quit;

    // Written by the main thread under Lock, read by the worker.
    private static double _target;
    private static bool _cmdRestart;
    private static int _cmdSpawn; // 1: spawn a depression at (_spawnX, _spawnY)
    private static double _spawnX, _spawnY;
    private static double _probeX, _probeY;

    // Published by the worker under Lock.
    private static int _version;
    private static double _snapTime;
    private static int _n;
    private static double _size;
    private static float _lat;
    private static byte[] _skyFront = [], _skyBack = [], _mapFront = [], _mapBack = [];
    private static bool _mapFrontValid;
    private static readonly Storm[] StormsFront = new Storm[WeatherSim.MaxStorms];
    private static readonly float[] TracksFront = new float[WeatherSim.MaxStorms * WeatherSim.TrackLength * 3];
    private static Local _local;
    private static int _formed, _strongest;
    private static double _stepMs;

    // Main-thread copies and GL objects.
    private static int _seenVersion = -1, _mapSeenVersion = -1;
    private static uint _skyTexA, _skyTexB, _mapTex;
    private static bool _skyAIsCurrent;
    private static double _skyTime; // weather time of the current sky texture
    private static double _driftX, _driftZ; // the sky's cloud drift, metres (x east, z south)

    /// <summary>Texture units the sky samples the weather on (free of every other renderer's: 0-7, 9-14 are taken).</summary>
    public const uint SkyUnitA = 8, SkyUnitB = 15;

    /// <summary>Is weather running (the current map has a "weather" section)?</summary>
    public static bool Active => _settings != null;

    public static WeatherSettings? Settings => _settings;

    /// <summary>Weather time the main thread is aiming for, seconds since the start.</summary>
    public static double Time => _target;

    // ---- Main thread ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Once a frame: starts or stops the weather for <paramref name="map"/>, moves its time on by <paramref name="dt"/>
    /// real seconds, tells the worker where the camera is and uploads anything new. Cheap when nothing changed.
    /// </summary>
    public static void Frame(Map? map, double dt, Vector3 camera)
    {
        var want = map?.Weather;
        if (!ReferenceEquals(want, _settings))
        {
            Stop();
            if (want != null)
                Start(want);
        }
        if (_settings == null)
            return;
        dt = Math.Clamp(dt, 0, 0.25);
        Local local;
        double snapTime;
        lock (Lock)
        {
            if (!Paused)
                _target += dt * Speed * 60.0;
            // Never run more than three hours of weather behind: drop time rather than catch up for ever.
            if (_target - _snapTime > 3 * 3600)
                _target = _snapTime + 3 * 3600;
            _probeX = camera.X;
            _probeY = -camera.Z; // the weather's y is north, the world's -z
            local = _local;
            snapTime = _snapTime;
        }
        if (_target >= snapTime + WeatherSim.Dt)
            Wake.Set();

        // The sky's clouds drift with the wind aloft at the player, sped up with the weather (a time-lapse), capped
        // so a hurricane's 60 m/s doesn't turn them into a blur.
        var lapse = Paused ? 0 : Math.Clamp(Speed * 60.0 / 600.0, 1.0, 6.0);
        _driftX += local.CloudU * dt * lapse;
        _driftZ -= local.CloudV * dt * lapse;
        const double wrap = 1_000_000; // a whole number of noise tiles for any cloud scale in metres that divides it
        if (Math.Abs(_driftX) > wrap) _driftX -= Math.Sign(_driftX) * wrap;
        if (Math.Abs(_driftZ) > wrap) _driftZ -= Math.Sign(_driftZ) * wrap;

        Upload();
    }

    /// <summary>Stops the worker and drops the weather (map change, quit).</summary>
    public static void Stop()
    {
        if (_thread != null)
        {
            _quit = true;
            Wake.Set();
            _thread.Join();
            _thread = null;
        }
        _settings = null;
        _sim = null;
    }

    private static void Start(WeatherSettings settings)
    {
        _settings = settings;
        Speed = settings.Speed;
        Paused = false;
        _quit = false;
        _driftX = _driftZ = 0;
        _seenVersion = _mapSeenVersion = -1;
        lock (Lock)
        {
            _target = 0;
            _snapTime = 0;
            _version = 0;
            _cmdRestart = true; // the worker builds the simulation (about 0.1 s), not the frame
            _cmdSpawn = 0;
            _local = default;
            _formed = 0;
            _strongest = -1;
            _n = settings.Cells;
            _size = settings.SizeKm * 1000.0;
            _lat = settings.Latitude;
            var n2 = _n * _n;
            if (_skyFront.Length != n2 * 2)
            {
                _skyFront = new byte[n2 * 2];
                _skyBack = new byte[n2 * 2];
                _mapFront = new byte[n2 * 4];
                _mapBack = new byte[n2 * 4];
            }
            _mapFrontValid = false;
            Array.Clear(StormsFront);
        }
        _thread = new Thread(Worker) { IsBackground = true, Name = "Weather", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
        Console.WriteLine($"[weather] started: seed {settings.Seed}, {settings.Cells}^2 cells over {settings.SizeKm:0} km at latitude {settings.Latitude}, {settings.Speed:0} min a second");
    }

    /// <summary>Starts the weather again from the beginning (new storms only if the seed changed: it is deterministic).</summary>
    public static void Restart()
    {
        if (_settings == null)
            return;
        lock (Lock)
        {
            _cmdRestart = true;
            _target = 0;
        }
        _driftX = _driftZ = 0;
        Wake.Set();
    }

    /// <summary>Starts a tropical depression at a point (metres from the domain centre, x east, y north).</summary>
    public static void SpawnStorm(double x, double y)
    {
        lock (Lock)
        {
            _cmdSpawn = 1;
            _spawnX = x;
            _spawnY = y;
        }
        Wake.Set();
    }

    /// <summary>Asks the worker for a fresh weather map picture now (window opened, radar toggled).</summary>
    public static void RequestMap()
    {
        MapRequest++;
        Wake.Set();
    }

    /// <summary>The newest storms, tracks and local weather (copies into the caller's arrays; main thread).</summary>
    public static void ReadStorms(Storm[] storms, float[] tracks, out Local local, out double time, out int formed, out int strongest, out double stepMs)
    {
        lock (Lock)
        {
            Array.Copy(StormsFront, storms, StormsFront.Length);
            Array.Copy(TracksFront, tracks, TracksFront.Length);
            local = _local;
            time = _snapTime;
            formed = _formed;
            strongest = _strongest;
            stepMs = _stepMs;
        }
    }

    /// <summary>The wind at the player, low and at cloud height (m/s, u east, v north): what flight reads (ConstructAir.cs). Called a few times a second.</summary>
    public static void ReadWind(out Vector2 low, out Vector2 high)
    {
        lock (Lock)
        {
            low = new Vector2(_local.WindU, _local.WindV);
            high = new Vector2(_local.CloudU, _local.CloudV);
        }
    }

    /// <summary>Domain width (m), cells across and latitude of the running weather.</summary>
    public static (double Size, int Cells, float Latitude) Domain => (_size, _n, _lat);

    /// <summary>The weather map picture (GL texture, rows south to north) or 0 before the first one.</summary>
    public static uint MapTexture => _mapSeenVersion >= 0 ? _mapTex : 0;

    /// <summary>
    /// What the sky needs (Source/Render/Sky/SkyRenderer.cs): the previous and current cloud textures, how far between
    /// them to draw, the domain width and the drift of the cloud pattern (metres). False when there is no weather yet.
    /// </summary>
    public static bool Sky(out uint prev, out uint cur, out float blend, out float invSize, out Vector2 drift)
    {
        prev = cur = 0;
        blend = 0;
        invSize = 0;
        drift = default;
        if (_settings == null || _seenVersion < 0)
            return false;
        cur = _skyAIsCurrent ? _skyTexA : _skyTexB;
        prev = _seenVersion > 0 ? (_skyAIsCurrent ? _skyTexB : _skyTexA) : cur;
        blend = (float)Math.Clamp((_target - _skyTime) / WeatherSim.Dt, 0, 1);
        invSize = (float)(1.0 / _size);
        drift = new Vector2((float)_driftX, (float)_driftZ);
        return true;
    }

    private static void Upload()
    {
        if (_settings == null)
            return;
        lock (Lock)
        {
            if (_version == _seenVersion)
                return;
            if (_skyTexA == 0)
            {
                _skyTexA = MakeTexture(_n, GL_RG8, GL_RG, GL_CLAMP_TO_EDGE);
                _skyTexB = MakeTexture(_n, GL_RG8, GL_RG, GL_CLAMP_TO_EDGE);
                _mapTex = MakeTexture(_n, GL_RGBA8, GL_RGBA, GL_CLAMP_TO_EDGE);
            }
            // The new sky goes into the older texture; the other becomes "previous".
            _skyAIsCurrent = !_skyAIsCurrent;
            Fill(_skyAIsCurrent ? _skyTexA : _skyTexB, _skyFront, GL_RG);
            if (_seenVersion < 0)
                Fill(_skyAIsCurrent ? _skyTexB : _skyTexA, _skyFront, GL_RG);
            _skyTime = _snapTime;
            _seenVersion = _version;
            if (_mapFrontValid && MapWanted)
            {
                Fill(_mapTex, _mapFront, GL_RGBA);
                _mapSeenVersion = _version;
            }
        }
    }

    private static uint MakeTexture(int n, uint internalFormat, uint format, uint wrap)
    {
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)internalFormat, n, n, 0, format, GL_UNSIGNED_BYTE, null);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)wrap);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)wrap);
        Native.glBindTexture(GL_TEXTURE_2D, 0);
        return tex.Id;
    }

    private static void Fill(uint tex, byte[] data, uint format)
    {
        Native.glBindTexture(GL_TEXTURE_2D, tex);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = data)
            Native.glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, _n, _n, format, GL_UNSIGNED_BYTE, p);
        Native.glBindTexture(GL_TEXTURE_2D, 0);
    }

    // ---- Worker ---------------------------------------------------------------------------------------------------

    private static void Worker()
    {
        var sw = new Stopwatch();
        var settings = _settings!;
        WeatherSim? sim = null;
        var mapRequest = -1;
        while (!_quit)
        {
            Wake.WaitOne(500);
            if (_quit)
                break;
            bool restart;
            int spawn;
            double target, sx, sy;
            lock (Lock)
            {
                restart = _cmdRestart;
                _cmdRestart = false;
                spawn = _cmdSpawn;
                _cmdSpawn = 0;
                (sx, sy) = (_spawnX, _spawnY);
                target = _target;
            }
            if (restart || sim == null)
            {
                sim = new WeatherSim(settings);
                _sim = sim;
                Publish(sim, 0);
            }
            if (MapWanted && MapRequest != mapRequest)
            {
                mapRequest = MapRequest;
                Publish(sim, 0);
            }
            if (spawn != 0)
            {
                sim.SpawnStorm(sx, sy);
                Publish(sim, sw.Elapsed.TotalMilliseconds);
            }
            // Catch up a bounded number of steps per wake (a slow PC falls behind and the main thread drops time).
            for (var n = 0; n < 24 && sim.Time + WeatherSim.Dt <= target && !_quit; n++)
            {
                sw.Restart();
                sim.Step();
                var ms = sw.Elapsed.TotalMilliseconds;
                Publish(sim, ms);
                lock (Lock)
                {
                    target = _target;
                    if (_cmdRestart || _cmdSpawn != 0)
                        break;
                }
            }
        }
    }

    private static void Publish(WeatherSim sim, double stepMs)
    {
        WeatherImage.FillSky(sim, _skyBack);
        var map = MapWanted;
        if (map)
            WeatherImage.FillMap(sim, _mapBack, Radar);
        double px, py;
        lock (Lock)
            (px, py) = (_probeX, _probeY);
        var local = new Local();
        sim.WindAt(px, py, false, out local.WindU, out local.WindV);
        sim.WindAt(px, py, true, out local.CloudU, out local.CloudV);
        local.Cover = WeatherImage.Cover(sim.FieldAt(sim.C, px, py));
        local.Rain = sim.FieldAt(sim.Rain, px, py);
        local.NearestStorm = -1;
        local.NearestKm = float.MaxValue;
        for (var s = 0; s < WeatherSim.MaxStorms; s++)
        {
            ref readonly var st = ref sim.Storms[s];
            if (!st.Alive)
                continue;
            var d = (float)(Math.Sqrt((st.X - px) * (st.X - px) + (st.Y - py) * (st.Y - py)) / 1000.0);
            if (d < local.NearestKm)
            {
                local.NearestKm = d;
                local.NearestStorm = s;
            }
        }
        lock (Lock)
        {
            (_skyFront, _skyBack) = (_skyBack, _skyFront);
            if (map)
            {
                (_mapFront, _mapBack) = (_mapBack, _mapFront);
                _mapFrontValid = true;
            }
            Array.Copy(sim.Storms, StormsFront, StormsFront.Length);
            Array.Copy(sim.Tracks, TracksFront, TracksFront.Length);
            _local = local;
            _snapTime = sim.Time;
            _formed = sim.StormsFormed;
            _strongest = sim.StrongestCategory;
            if (stepMs > 0)
                _stepMs = _stepMs <= 0 ? stepMs : _stepMs * 0.95 + stepMs * 0.05;
            _version++;
        }
    }
}
