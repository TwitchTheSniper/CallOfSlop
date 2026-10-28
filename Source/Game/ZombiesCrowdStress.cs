using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// A horde stress map (docs/zombies/MASS-HORDE.md, "Horde Stress"; the Zombies section's <c>"stress"</c>,
/// Dev/HordeStress.json). Each round's crowd, of the size the host picks in the lobby (<see cref="StressSize"/>), is
/// seeded in the mass tier on the map's staging areas, split evenly. It waits there for the countdown
/// (<see cref="StressPhase.Staging"/>: the crowd is held), then descends with the flow field leading to the rally point
/// (<see cref="StressPhase.Gather"/>: the crowd's goal), and once most of it is near the rally point, the hordes have
/// merged and it advances on the players (<see cref="StressPhase.Advance"/>: the goal cleared, the field leads to the
/// players as on every crowd map). Zombies are otherwise the mass horde's: promoted near the players, fighting, dying.
/// With <see cref="StressEndless"/> another wave of the same size comes from the staging areas every
/// <see cref="ZombiesStress.WaveSeconds"/> (straight at the players), up to <see cref="StressMax"/> at once.
/// <para>
/// Measurement: the server's tick time, the frame time, the tiers' counts, the drawn instances and the bytes per client
/// are kept per size bracket (the live horde's size against <see cref="StressSizeChoices"/>), and a summary goes to the
/// log at the end of each round and of the session ("[stress]" lines): where the PC started to suffer. The F9 window
/// shows the same counts live (Source/UI/UiPerf.cs). Nothing here runs on other maps; nothing allocates per tick.
/// </para>
/// </summary>
public sealed partial class Game
{
    /// <summary>The lobby's horde size presets (the host also can type a custom number, 1 to <see cref="StressMax"/>).</summary>
    public static readonly int[] StressSizeChoices = [1000, 2500, 5000, 10000, 25000, 50000, 100000];

    /// <summary>The most zombies a stress round may have at once (endless waves stop adding there).</summary>
    public const int StressMax = 200000;

    /// <summary>Host setting: the stress map's horde size (0: the map's "crowd").</summary>
    public int StressSize;

    /// <summary>Host setting: endless waves.</summary>
    public bool StressEndless;

    /// <summary>Self-test: the crowd walks this much faster (a short scripted run sees the whole descent).</summary>
    public float StressSpeedScale = 1f;

    public enum StressPhase : byte
    {
        Off,
        Staging,
        Gather,
        Advance,
    }

    /// <summary>Where this round's hordes are (server).</summary>
    public StressPhase StressState { get; private set; }

    /// <summary>When the phase started, the waves so far this round, the round's size (one wave), and the merge time (s from the descent; -1 not yet).</summary>
    public double StressPhaseAt { get; private set; }

    public int StressWave { get; private set; }

    public int StressWaveSize { get; private set; }

    public double StressMergeSeconds { get; private set; } = -1;

    /// <summary>Seconds in the current phase (as of the last tick).</summary>
    public double StressPhaseSeconds { get; private set; }

    /// <summary>The individuals alive (the perf window's count; the server's own).</summary>
    public int StressIndividuals => _zAlive;

    /// <summary>The share of the crowd near the rally point at the last check (self-test, perf window).</summary>
    public float StressNearShare { get; private set; }

    private double _stressNextWave, _stressDescentAt;

    /// <summary>Is this a stress map round on the server?</summary>
    public bool StressOn => IsServer && StressState != StressPhase.Off && ZMap is { Stress: not null };

    /// <summary>The horde size the next stress round will have on <paramref name="zm"/>.</summary>
    public int StressSizeFor(ZombiesMap zm) => Math.Clamp(StressSize > 0 ? StressSize : CrowdOverride > 0 ? CrowdOverride : zm.Crowd, 1, StressMax);

    /// <summary>Server, at a stress round's start (from <see cref="CrowdBeginRound"/>): the hordes on their staging areas, held.</summary>
    private void StressBeginRound(ZombiesMap zm, ZombiesStress st, int round, double now)
    {
        StressSummary("round over"); // (the previous round's, if it wasn't written yet)
        var n = StressSizeFor(zm);
        var tuning = ZRules.Crowd;
        if (StressSpeedScale != 1f)
        {
            tuning = tuning.Copy();
            tuning.Speed *= StressSpeedScale;
        }
        Crowd.Tuning = tuning;
        Crowd.Setup(World.Map, 0f, n);
        Crowd.Clear();
        _crowdRound = (_crowdRound + 1) & 0xFFFF; // (clients get a keyframe: ZombiesCrowdNet.cs)
        _zToSpawn = 0;
        Crowd.MassHP = ZRules.ZombieHealth(Math.Max(1, round));
        StressWave = 0;
        StressWaveSize = n;
        StressMergeSeconds = -1;
        StressNearShare = 0;
        StressSeedWave(st, n);
        _zRoundTotal = CrowdRoundTotal = Crowd.Total;
        CrowdRoundKills = 0;
        Crowd.SetGoal(true, st.Rally.X, st.Rally.Y);
        Crowd.Hold = st.Countdown > 0;
        StressState = st.Countdown > 0 ? StressPhase.Staging : StressPhase.Gather;
        StressPhaseAt = _stressDescentAt = now;
        _stressNextWave = now + st.Countdown + st.WaveSeconds;
        StressResetStats(round, n);
        Console.WriteLine($"[stress] round {round}: {Crowd.Total} zombies on {st.Staging.Count} staging areas ({Crowd.MassTotal} in the mass tier), "
                          + $"{(st.Countdown > 0 ? $"descending in {st.Countdown:0} s" : "descending now")} to the rally point ({st.Rally.X:0}, {st.Rally.Y:0}); "
                          + $"endless {(StressEndless ? $"on (a wave every {st.WaveSeconds:0} s)" : "off")}; {Crowd.FlowCells} flow cells, {Crowd.MassCols} x {Crowd.MassRows} mass cells, {Crowd.Bytes / 1024} KB");
        if (Crowd.Total < n)
            Console.WriteLine($"[stress] only {Crowd.Total} of {n} fitted on the staging areas (the mass cells hold {Crowd.Tuning.MassCapacity} each)");
    }

    /// <summary>One wave of <paramref name="n"/> into the mass on the staging areas (an even split, round blobs packed to the mass cells' capacity).</summary>
    private int StressSeedWave(ZombiesStress st, int n)
    {
        var k = st.Staging.Count;
        var cap = Math.Max(1, Crowd.Tuning.MassCapacity);
        var cell = Crowd.MassCell;
        var put = 0;
        for (var i = 0; i < k; i++)
        {
            var share = n / k + (i < n % k ? 1 : 0);
            var at = st.Staging[i];
            // Packed: share / (cap per cell) cells; a radius with room to spare for unwalkable cells, doubled if it filled.
            var radius = MathF.Sqrt(share / (float)cap * cell * cell / MathF.PI) * 1.3f + cell * 1.5f;
            var got = 0;
            for (var tries = 0; tries < 3 && got < share; tries++, radius *= 2)
                got += Crowd.SeedMass(at.X, at.Y, share - got, cap, radius);
            put += got;
        }
        StressWave++;
        return put;
    }

    /// <summary>Server, every tick of a stress round (from <see cref="CrowdTick"/>, before the crowd's tick): the phases and the waves.</summary>
    private void StressTick(ZombiesStress st, double now)
    {
        if (StressState == StressPhase.Off)
            return;
        StressPhaseSeconds = now - StressPhaseAt;
        switch (StressState)
        {
            case StressPhase.Staging when now - StressPhaseAt >= st.Countdown:
                Crowd.Hold = false;
                StressState = StressPhase.Gather;
                StressPhaseAt = _stressDescentAt = now;
                Console.WriteLine($"[stress] the hordes descend: {Crowd.Total} zombies");
                break;
            case StressPhase.Gather when ServerTick % 30 == 0:
                StressNearShare = StressShareNear(st.Rally);
                if (StressNearShare >= st.Gather || now - StressPhaseAt >= st.GatherSeconds)
                {
                    StressMergeSeconds = now - _stressDescentAt;
                    Crowd.SetGoal(false);
                    StressState = StressPhase.Advance;
                    StressPhaseAt = now;
                    Console.WriteLine($"[stress] {(StressNearShare >= st.Gather ? "merged" : "gave up waiting")} at the rally point after {StressMergeSeconds:0} s "
                                      + $"({StressNearShare * 100:0}% of {Crowd.Total} near it): advancing on the players");
                }
                break;
        }
        if (StressEndless && StressState != StressPhase.Staging && now >= _stressNextWave)
        {
            _stressNextWave = now + st.WaveSeconds;
            var live = Crowd.Total + _zAlive;
            var n = Math.Min(StressWaveSize, StressMax - live);
            if (n > 0)
            {
                var put = StressSeedWave(st, n);
                _zRoundTotal += put;
                CrowdRoundTotal += put;
                Console.WriteLine($"[stress] wave {StressWave}: {put} more from the staging areas ({Crowd.Total + _zAlive} live)");
            }
        }
    }

    /// <summary>The share of the crowd (particles and mass) within the merge radius of the rally point: a radius the whole crowd would pack into loosely.</summary>
    private float StressShareNear(Vector2 rally)
    {
        var total = Crowd.Total;
        if (total == 0)
            return 1f;
        var density = Math.Max(1, Crowd.Tuning.MassCapacity) / (Crowd.MassCell * Crowd.MassCell) * 0.6f; // (zombies a square metre, loosely packed)
        var r = MathF.Max(30f, MathF.Sqrt(total / density / MathF.PI));
        var r2 = r * r;
        var near = 0;
        for (var i = 0; i < Crowd.Count; i++)
        {
            var dx = Crowd.X[i] - rally.X;
            var dz = Crowd.Z[i] - rally.Y;
            if (dx * dx + dz * dz <= r2)
                near++;
        }
        var cols = Crowd.MassCols;
        var size = Crowd.MassCell;
        for (var c = 0; c < Crowd.MassCount.Length; c++)
        {
            var m = Crowd.MassCount[c];
            if (m == 0)
                continue;
            var dx = Crowd.MinX + (c % cols + 0.5f) * size - rally.X;
            var dz = Crowd.MinZ + (c / cols + 0.5f) * size - rally.Y;
            if (dx * dx + dz * dz <= r2)
                near += m;
        }
        return near / (float)total;
    }

    // ---- Measurement ------------------------------------------------------------------------------------------------

    /// <summary>One size bracket's figures (the live horde between two presets).</summary>
    private sealed class StressBucket
    {
        public long Ticks, Frames;
        public double TickMs, TickWorst, FrameMs, FrameWorst, ClientSeconds;
        public long NetBytes, CrowdBytes;
        public int PeakLive, PeakParticles, PeakMass, PeakIndividuals, PeakDrawn, SlowFrames;

        public void Clear()
        {
            Ticks = Frames = NetBytes = CrowdBytes = 0;
            TickMs = TickWorst = FrameMs = FrameWorst = ClientSeconds = 0;
            PeakLive = PeakParticles = PeakMass = PeakIndividuals = PeakDrawn = SlowFrames = 0;
        }
    }

    // Bracket 0: under the first preset; bracket k: at least StressSizeChoices[k - 1].
    private readonly StressBucket[] _stressBuckets = Enumerable.Range(0, StressSizeChoices.Length + 1).Select(_ => new StressBucket()).ToArray();
    private int _stressStatsRound, _stressStatsSize;
    private bool _stressStatsOpen;
    private long _stressNet0, _stressCrowd0;

    /// <summary>Program: the crowd instances drawn this frame (the host's renderer; the summary's peak).</summary>
    public int StressDrawn;

    /// <summary>The live horde right now: the crowd (particles and mass) and the individuals.</summary>
    public int StressLive => Crowd.Total + _zAlive;

    private static int StressBracket(int live)
    {
        var b = 0;
        while (b < StressSizeChoices.Length && live >= StressSizeChoices[b])
            b++;
        return b;
    }

    private void StressResetStats(int round, int size)
    {
        foreach (var b in _stressBuckets)
            b.Clear();
        _stressStatsRound = round;
        _stressStatsSize = size;
        _stressStatsOpen = true;
        _stressNet0 = Net.BytesSent;
        _stressCrowd0 = ZCrowdBytesSent;
    }

    /// <summary>Server, after each tick of a stress round: its time and the counts, into the live size's bracket.</summary>
    private void StressNoteTick(double ms)
    {
        if (!_stressStatsOpen)
            return;
        var live = StressLive;
        var b = _stressBuckets[StressBracket(live)];
        b.Ticks++;
        b.TickMs += ms;
        b.TickWorst = Math.Max(b.TickWorst, ms);
        b.PeakLive = Math.Max(b.PeakLive, live);
        b.PeakParticles = Math.Max(b.PeakParticles, Crowd.Count);
        b.PeakMass = Math.Max(b.PeakMass, Crowd.MassTotal);
        b.PeakIndividuals = Math.Max(b.PeakIndividuals, _zAlive);
        b.ClientSeconds += _clients.Count * World.TickDt;
        var net = Net.BytesSent;
        b.NetBytes += net - _stressNet0;
        _stressNet0 = net;
        var crowd = ZCrowdBytesSent;
        b.CrowdBytes += crowd - _stressCrowd0;
        _stressCrowd0 = crowd;
    }

    /// <summary>Server, once a frame on a stress round: the frame's time (and the instances the renderer drew).</summary>
    private void StressNoteFrame(double frameDt)
    {
        if (!_stressStatsOpen || frameDt <= 0 || frameDt > 1)
            return;
        var ms = frameDt * 1000;
        var b = _stressBuckets[StressBracket(StressLive)];
        b.Frames++;
        b.FrameMs += ms;
        b.FrameWorst = Math.Max(b.FrameWorst, ms);
        if (ms > 33.4)
            b.SlowFrames++;
        b.PeakDrawn = Math.Max(b.PeakDrawn, StressDrawn);
    }

    /// <summary>
    /// Writes the round's figures per size bracket to the log ("[stress]" lines) and closes them (the end of a round, a
    /// new round, the session's end). Allocates (once, at the end).
    /// </summary>
    public void StressSummary(string why)
    {
        if (!_stressStatsOpen)
            return;
        _stressStatsOpen = false;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"[stress] summary ({why}): round {_stressStatsRound}, size {_stressStatsSize}{(StressEndless ? ", endless" : "")}, {StressWave} wave(s), merged {(StressMergeSeconds >= 0 ? $"after {StressMergeSeconds:0} s" : "no")}, "
                       + $"killed {CrowdRoundKills} of {CrowdRoundTotal}");
        Console.WriteLine(sb.ToString());
        for (var k = 0; k < _stressBuckets.Length; k++)
        {
            var b = _stressBuckets[k];
            if (b.Ticks == 0 && b.Frames == 0)
                continue;
            var from = k == 0 ? "under " + StressSizeChoices[0] : StressSizeChoices[k - 1] + "+";
            var perClient = b.ClientSeconds > 0 ? $"{b.NetBytes / b.ClientSeconds / 1024:0.0} KB/s per client ({b.CrowdBytes / b.ClientSeconds / 1024:0.0} KB/s of it the crowd)" : "no clients";
            Console.WriteLine(string.Create(inv, $"[stress]   {from} live (peak {b.PeakLive}): server tick avg {(b.Ticks > 0 ? b.TickMs / b.Ticks : 0):0.00} ms, worst {b.TickWorst:0.00} ms over {b.Ticks} ticks; "
                                                 + $"frames {(b.Frames > 0 ? 1000.0 * b.Frames / b.FrameMs : 0):0} fps avg, worst {b.FrameWorst:0.0} ms, {b.SlowFrames} under 30 fps; "
                                                 + $"peak particles {b.PeakParticles}, mass {b.PeakMass}, individuals {b.PeakIndividuals}, drawn {b.PeakDrawn}; {perClient}"));
        }
    }

    // ---- Live figures for the F9 window (any crowd map, server) ------------------------------------------------------

    /// <summary>Over the last second: the crowd's tick (mean and worst ms), the crowd's bytes per client a second, the clients.</summary>
    public float CrowdTickAvgMs, CrowdTickWorstMs, CrowdBytesPerClient;

    public int CrowdClients;

    private double _crowdPerfMs0, _crowdPerfWorst;
    private long _crowdPerfTicks0, _crowdPerfBytes0, _crowdPerfAt;

    /// <summary>Server, after each crowd tick: the window's one-second figures.</summary>
    private void CrowdPerfSample()
    {
        var ms = Crowd.TickMs - _crowdPerfMs0;
        var ticks = Crowd.Ticks - _crowdPerfTicks0;
        _crowdPerfWorst = Math.Max(_crowdPerfWorst, _crowdLastTickMs);
        if (ServerTick - _crowdPerfAt < World.TickRate)
            return;
        CrowdTickAvgMs = ticks > 0 ? (float)(ms / ticks) : 0;
        CrowdTickWorstMs = (float)_crowdPerfWorst;
        CrowdClients = _clients.Count;
        var secs = (ServerTick - _crowdPerfAt) * World.TickDt;
        CrowdBytesPerClient = _clients.Count > 0 && secs > 0 ? (float)((ZCrowdBytesSent - _crowdPerfBytes0) / secs / _clients.Count) : 0;
        _crowdPerfMs0 = Crowd.TickMs;
        _crowdPerfTicks0 = Crowd.Ticks;
        _crowdPerfBytes0 = ZCrowdBytesSent;
        _crowdPerfAt = ServerTick;
        _crowdPerfWorst = 0;
    }

    private double _crowdLastTickMs;

    /// <summary>Self-test: the stress round's phase machine without the clock (a tick of the stress logic now).</summary>
    public void SelfTestStressTick(double now)
    {
        if (ZMap is { Stress: { } st })
            StressTick(st, now);
    }

    /// <summary>A timestamp for <see cref="StressEndTick"/> (0 off a stress round: no clock read).</summary>
    private long StressBeginTick() => StressOn ? Stopwatch.GetTimestamp() : 0;

    private void StressEndTick(long t0)
    {
        if (t0 != 0)
            StressNoteTick(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }
}
