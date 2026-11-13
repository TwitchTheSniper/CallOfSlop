using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The city's life (docs/planets/CITY.md "Life"): trains on the elevated loop to a timetable, people walking round the
/// blocks and across the square, and people at the stations waiting, boarding and getting off. Everything is a function
/// of the shared clock (the server tick every PC poses movers by), so nothing is sent, saved or simulated: two PCs see the
/// same train at the same platform with the same people stepping off it.
/// </summary>
public sealed class CityLife
{
    /// <summary>A train's cruising speed (m/s), its acceleration and braking (m/s2), and how long it stands at a station (s).</summary>
    public const float Speed = 15f, Accel = 0.9f, Dwell = 24f;

    /// <summary>Cars a train, a car's length over its couplers (m), and the trains on each track.</summary>
    public const int Cars = 4, TrainsPerTrack = 2;

    public const float CarLength = 16f;

    /// <summary>The track's middle from the line's middle (m), and the deck's top over which the cars ride.</summary>
    public const float Track = 1.9f;

    public readonly CityPlan Plan;
    public readonly CityRail? Rail;

    /// <summary>Distance along the line at each of its points (m), and at each station's middle (sorted).</summary>
    public readonly float[] Cum = [];

    public readonly float[] StationAt = [];

    /// <summary>A whole round of the loop, stops included (s).</summary>
    public readonly float Period;

    private readonly float[] _legTime = [], _leg = [];

    /// <summary>People walking round blocks: (min x, min z, max x, max z) of their round on the plan, then speed (signed: the
    /// way round), the start of their round (m), a seed and 0. Grouped by chunk: <see cref="WalkerChunks"/>.</summary>
    public readonly Vector4[] Walkers = [];

    /// <summary>Each chunk of walkers: its first and count, and its middle on the plan.</summary>
    public readonly (int First, int Count, Vector2 Mid)[] WalkerChunks = [];

    public CityLife(CityPlan plan)
    {
        Plan = plan;
        Rail = plan.Rail;
        if (Rail is { Path.Length: > 2 } r)
        {
            var n = r.Path.Length;
            Cum = new float[n + 1];
            for (var i = 1; i <= n; i++)
                Cum[i] = Cum[i - 1] + Vector2.Distance(r.Path[i - 1], r.Path[i % n]);
            var st = new float[r.Stations.Length];
            for (var k = 0; k < st.Length; k++)
            {
                var best = 0;
                for (var i = 1; i < n; i++)
                    if (Vector2.DistanceSquared(r.Path[i], r.Stations[k]) < Vector2.DistanceSquared(r.Path[best], r.Stations[k]))
                        best = i;
                st[k] = Cum[best];
            }
            Array.Sort(st);
            StationAt = st;
            _leg = new float[st.Length];
            _legTime = new float[st.Length];
            for (var k = 0; k < st.Length; k++)
            {
                var d = (k + 1 < st.Length ? st[k + 1] : st[0] + Length) - st[k];
                _leg[k] = d;
                _legTime[k] = RunTime(d);
                Period += Dwell + _legTime[k];
            }
        }
        if (plan.Metro is { Stations.Length: > 0 } metro)
            MakeMetro(metro);
        (Walkers, WalkerChunks) = MakeWalkers(plan);
        StreetCars = MakeCars(plan);
    }

    /// <summary>The loop's length (m).</summary>
    public float Length => Cum.Length > 0 ? Cum[^1] : 0;

    private static float RunTime(float d)
    {
        var ramp = Speed * Speed / Accel; // (accelerating and braking together cover this)
        return d >= ramp ? d / Speed + Speed / Accel : 2 * MathF.Sqrt(d / Accel);
    }

    private static float RunDistance(float d, float t)
    {
        var total = RunTime(d);
        var top = MathF.Min(Speed, MathF.Sqrt(d * Accel));
        var tr = top / Accel;
        if (t <= tr)
            return 0.5f * Accel * t * t;
        if (t >= total - tr)
        {
            var left = total - t;
            return d - 0.5f * Accel * left * left;
        }
        return 0.5f * Accel * tr * tr + top * (t - tr);
    }

    /// <summary>Where a train is at time <paramref name="t"/> (s): its distance along the line (m) and, while it stands at a
    /// station, which (an index into <see cref="StationAt"/>) and since when (s; -1 when it is running).</summary>
    /// <param name="dir">+1: the track on the line's left, running the way the line goes; -1: the other.</param>
    public (float S, int Station, float Since) Train(double t, int dir, int k)
    {
        if (Period <= 0)
            return (0, -1, -1);
        var tau = (float)((t + k * Period / TrainsPerTrack + (dir < 0 ? Period * 0.27 : 0)) % Period);
        var n = StationAt.Length;
        for (var j = 0; j < n; j++)
        {
            // Going the other way the stations come in the reverse order, the legs too.
            var st = dir > 0 ? j : (n - j) % n;
            var leg = dir > 0 ? j : (n - j - 1 + n) % n;
            var at = StationAt[st];
            if (tau < Dwell)
                return (at, st, tau);
            tau -= Dwell;
            if (tau < _legTime[leg])
            {
                var run = RunDistance(_leg[leg], tau);
                var s = dir > 0 ? at + run : at - run;
                return (((s % Length) + Length) % Length, -1, -1);
            }
            tau -= _legTime[leg];
        }
        return (StationAt[0], 0, 0);
    }

    /// <summary>The plan point and the line's unit direction there, at distance <paramref name="s"/> along the line.</summary>
    public (Vector2 P, Vector2 Dir) At(float s)
    {
        var r = Rail!;
        var n = r.Path.Length;
        s = ((s % Length) + Length) % Length;
        var i = Array.BinarySearch(Cum, s);
        if (i < 0)
            i = ~i - 1;
        i = Math.Clamp(i, 0, n - 1);
        var a = r.Path[i];
        var b = r.Path[(i + 1) % n];
        var f = (s - Cum[i]) / MathF.Max(Cum[i + 1] - Cum[i], 1e-3f);
        return (Vector2.Lerp(a, b, f), Vector2.Normalize(b - a));
    }

    /// <summary>The previous departure and the next arrival of trains going <paramref name="dir"/> at station <paramref name="st"/>,
    /// and whether one stands there now (its arrival then is the last one).</summary>
    public (double LastLeave, double NextArrive, bool Standing, double Arrived) StationTimes(double t, int dir, int st)
    {
        double lastLeave = double.NegativeInfinity, nextArrive = double.PositiveInfinity, arrived = double.NegativeInfinity;
        var standing = false;
        // Each train's arrival here: once a period; look at this period and its neighbours.
        for (var k = 0; k < TrainsPerTrack; k++)
        {
            var arr = ArrivalOffset(dir, k, st);
            var baseT = Math.Floor((t - arr) / Period) * Period + arr;
            for (var m = -1; m <= 1; m++)
            {
                var a = baseT + m * Period;
                var leave = a + Dwell;
                if (a <= t && t < leave)
                {
                    standing = true;
                    arrived = Math.Max(arrived, a);
                }
                if (leave <= t)
                    lastLeave = Math.Max(lastLeave, leave);
                if (a > t)
                    nextArrive = Math.Min(nextArrive, a);
            }
        }
        return (lastLeave, nextArrive, standing, arrived);
    }

    /// <summary>When (mod the period) train k going <paramref name="dir"/> arrives at station <paramref name="st"/>.</summary>
    private double ArrivalOffset(int dir, int k, int st)
    {
        var n = StationAt.Length;
        double tau = 0;
        for (var j = 0; j < n; j++)
        {
            var s = dir > 0 ? j : (n - j) % n;
            var leg = dir > 0 ? j : (n - j - 1 + n) % n;
            if (s == st)
                break;
            tau += Dwell + _legTime[leg];
        }
        var shift = k * Period / TrainsPerTrack + (dir < 0 ? Period * 0.27 : 0);
        return ((tau - shift) % Period + Period) % Period;
    }

    // ---- The metro ------------------------------------------------------------------------------------------------------

    /// <summary>The metro's trains turn back at the tunnels' ends for this long (s), out of sight.</summary>
    public const float Turn = 20f;

    /// <summary>A metro train's run one way (s): from the tunnel's end through every station to the other end.</summary>
    public float MetroHalf { get; private set; }

    private float[] _metroArrive = [], _metroBack = [];

    private void MakeMetro(CityMetro m)
    {
        var st = m.Stations;
        _metroArrive = new float[st.Length];
        var t = RunTime(st[0] - m.Z0);
        for (var j = 0; j < st.Length; j++)
        {
            _metroArrive[j] = t;
            t += Dwell + (j + 1 < st.Length ? RunTime(st[j + 1] - st[j]) : RunTime(m.Z1 - st[j]));
        }
        MetroHalf = t + Turn;
        // Coming back: the same legs the other way.
        _metroBack = new float[st.Length];
        t = RunTime(m.Z1 - st[^1]);
        for (var j = st.Length - 1; j >= 0; j--)
        {
            _metroBack[j] = t;
            t += Dwell + (j > 0 ? RunTime(st[j] - st[j - 1]) : RunTime(st[0] - m.Z0));
        }
    }

    /// <summary>A metro train at time t: its middle's z on the plan, which way it runs (+1: towards +z), and the station it
    /// stands at (-1 running). Two trains, half a round apart, each going out on one track and back on the other.</summary>
    public (float Z, int Dir, int Station) MetroTrain(double t, int k)
    {
        var m = Plan.Metro!;
        var round = 2 * MetroHalf;
        var tau = (float)((t + k * MetroHalf) % round);
        var dir = tau < MetroHalf ? 1 : -1;
        if (dir < 0)
            tau -= MetroHalf;
        // Going back the stations come in the other order: mirror the line.
        var st = m.Stations;
        float Z(float z) => dir > 0 ? z : m.Z0 + m.Z1 - z;
        float SZ(int j) => dir > 0 ? st[j] : m.Z0 + m.Z1 - st[st.Length - 1 - j];
        var prev = m.Z0;
        var clock = 0f;
        for (var j = 0; j < st.Length; j++)
        {
            var at = SZ(j);
            var run = RunTime(at - prev);
            if (tau < clock + run)
                return (Z(prev + RunDistance(at - prev, tau - clock)), dir, -1);
            clock += run;
            if (tau < clock + Dwell)
                return (Z(at), dir, dir > 0 ? j : st.Length - 1 - j);
            clock += Dwell;
            prev = at;
        }
        var last = RunTime(m.Z1 - prev);
        return (Z(prev + RunDistance(m.Z1 - prev, MathF.Min(tau - clock, last))), dir, -1);
    }

    /// <summary>At metro station j for trains going <paramref name="dir"/>: the last departure, the next arrival, whether one
    /// stands there now and when it came (as <see cref="StationTimes"/>).</summary>
    public (double LastLeave, double NextArrive, bool Standing, double Arrived) MetroTimes(double t, int dir, int j)
    {
        // A train going +z reaches station j at _metroArrive[j] into its round; one going -z reaches it at the mirror's.
        var offset = dir > 0 ? _metroArrive[j] : MetroHalf + _metroBack[j];
        var head = MetroHalf; // (two trains half a round apart: one each way every half round)
        var a = Math.Floor((t - offset) / head) * head + offset;
        var standing = t < a + Dwell;
        return (standing ? a - head + Dwell : a + Dwell, a + head, standing, a);
    }

    // ---- Air taxis -----------------------------------------------------------------------------------------------------

    /// <summary>A taxi's hop from one rooftop pad to another (s): it stands a while, climbs, crosses the city high over the
    /// towers and comes down.</summary>
    public const float Hop = 80f, Stand = 14f, Climb = 16f;

    /// <summary>Taxis in the sky: two for each pad, at most twelve.</summary>
    public int Taxis => Math.Min(Plan.Pads.Length * 2, 12);

    private static uint Mix(uint a, uint b)
    {
        var x = a * 0x9E3779B1u ^ b * 0x85EBCA77u;
        x ^= x >> 15;
        x *= 0x2C1B3C6Du;
        x ^= x >> 12;
        return x;
    }

    /// <summary>Where taxi k is at time t: its plan point and height over the ground, and the way it faces on the plan.</summary>
    public (Vector2 P, float H, Vector2 Dir) Taxi(double t, int k)
    {
        var pads = Plan.Pads;
        var tt = t + k * 37.0;
        var leg = (long)Math.Floor(tt / Hop);
        var tau = (float)(tt - leg * Hop);
        var a = pads[(int)(Mix((uint)k, (uint)leg) % (uint)pads.Length)];
        var b = pads[(int)(Mix((uint)k, (uint)(leg + 1)) % (uint)pads.Length)];
        var cruise = Plan.Spec.Tallest * 1.3f;
        var dir = b.At - a.At;
        dir = dir.LengthSquared() > 1 ? Vector2.Normalize(dir) : new Vector2(MathF.Cos(k), MathF.Sin(k));
        if (tau < Stand)
            return (a.At, a.Height + 0.6f, dir);
        tau -= Stand;
        if (tau < Climb)
        {
            var f = tau / Climb;
            return (a.At, a.Height + 0.6f + (cruise - a.Height) * f * f * (3 - 2 * f), dir);
        }
        tau -= Climb;
        var cross = Hop - Stand - 2 * Climb;
        if (tau < cross)
        {
            var f = tau / cross;
            f = f * f * (3 - 2 * f);
            return (Vector2.Lerp(a.At, b.At, f), cruise, dir);
        }
        tau -= cross;
        var g = tau / Climb;
        return (b.At, cruise + (b.Height + 0.6f - cruise) * g * g * (3 - 2 * g), dir);
    }

    // ---- Traffic ------------------------------------------------------------------------------------------------------

    /// <summary>Cars on the streets: (start x, z, end x, z) of the lane they drive along on the plan, then speed, where
    /// they start (m), a seed and 2 (the figure shader's kind for a car). A car that reaches the run's end comes in again
    /// at its start: the runs end at the city's edge or at the square.</summary>
    public readonly Vector4[] StreetCars = [];

    private static Vector4[] MakeCars(CityPlan plan)
    {
        var d = new CityPlan.Dice((uint)plan.Spec.Seed * 6007u + 13);
        var cars = new List<Vector4>();
        // Runs of street: neighbouring segments on one line, joined across their crossings.
        foreach (var alongX in new[] { true, false })
        {
            var lines = plan.Streets.Where(st => st.AlongX == alongX).GroupBy(st => alongX ? st.A.Y : st.A.X);
            foreach (var line in lines)
            {
                var segs = line.OrderBy(st => alongX ? st.A.X : st.A.Y).ToList();
                var k = 0;
                while (k < segs.Count)
                {
                    var j = k;
                    while (j + 1 < segs.Count && (alongX ? segs[j + 1].A.X - segs[j].B.X : segs[j + 1].A.Y - segs[j].B.Y) < 32)
                        j++;
                    Vector2 a = segs[k].A, b = segs[j].B;
                    var car = segs[k].Carriage;
                    var len = Vector2.Distance(a, b);
                    if (len > 60)
                    {
                        var dir = Vector2.Normalize(b - a);
                        var left = new Vector2(-dir.Y, dir.X);
                        // Lanes: one each way on a street, two on an avenue; keep to the right.
                        float[] lanes = segs[k].Avenue ? [car * 0.25f, car * 0.72f] : [car * 0.5f];
                        foreach (var lane in lanes)
                            foreach (var way in new[] { 1, -1 })
                            {
                                var off = -left * lane * way;
                                Vector2 from = way > 0 ? a + off : b + off, to = way > 0 ? b + off : a + off;
                                var n = Math.Max(1, (int)(len / d.Range(35, 60)));
                                for (var i = 0; i < n; i++)
                                {
                                    cars.Add(new Vector4(from.X, from.Y, to.X, to.Y));
                                    cars.Add(new Vector4(d.Range(8, 14), len * (i + d.Range(0, 0.6f)) / n, d.Next() & 0xffff, 2));
                                }
                            }
                    }
                    k = j + 1;
                }
            }
        }
        return cars.ToArray();
    }

    // ---- Walkers ------------------------------------------------------------------------------------------------------

    private static (Vector4[], (int, int, Vector2)[]) MakeWalkers(CityPlan plan)
    {
        var d = new CityPlan.Dice((uint)plan.Spec.Seed * 7177u + 3);
        var byChunk = new Dictionary<(int, int), List<Vector4>>();
        void Add(Vector2 min, Vector2 max, float speed, float start, uint seed)
        {
            var mid = (min + max) * 0.5f;
            var key = ((int)MathF.Floor(mid.X / CityGeometry.ChunkSize), (int)MathF.Floor(mid.Y / CityGeometry.ChunkSize));
            if (!byChunk.TryGetValue(key, out var l))
                byChunk[key] = l = new List<Vector4>();
            l.Add(new Vector4(min.X, min.Y, max.X, max.Y));
            l.Add(new Vector4(speed, start, seed & 0xffff, 0));
        }
        foreach (var b in plan.Blocks)
        {
            if (b.Kind == 1)
                continue; // (the square has its own strollers; the market's pavements are walked like any block's)
            var size = b.Max - b.Min;
            var perimeter = 2 * (size.X + size.Y);
            // About one person every 9 m of the block's round, in two lanes (near the shop windows and out by the lamps),
            // half going each way round.
            var count = (int)(perimeter / 9f);
            for (var i = 0; i < count; i++)
            {
                var lane = d.Range(0.8f, 1.9f);
                var dir = d.Chance(0.5f) ? 1f : -1f;
                Add(b.Min - new Vector2(lane), b.Max + new Vector2(lane), dir * d.Range(1.1f, 1.6f), d.Range(0, perimeter + 8 * lane), d.Next());
            }
        }
        // The market: shoppers going round between two aisles, up one and back down the next.
        foreach (var b in plan.Blocks)
        {
            if (b.Kind != 2)
                continue;
            float x0 = b.Min.X + CityGeometry.MarketEnd, x1 = b.Max.X - CityGeometry.MarketEnd;
            var aisles = new List<float>();
            for (var z = b.Min.Y + CityGeometry.MarketFirst; z < b.Max.Y - 1; z += CityGeometry.MarketStep)
                aisles.Add(z);
            for (var k = 0; k + 1 < aisles.Count; k++)
                for (var i = 0; i < 14; i++)
                {
                    var lane = d.Range(-0.6f, 0.6f);
                    Add(new Vector2(x0 + lane, aisles[k] + lane), new Vector2(x1 - lane, aisles[k + 1] - lane), (d.Chance(0.5f) ? 1 : -1) * d.Range(0.7f, 1.1f), d.Range(0, 300), d.Next());
                }
        }
        // The square: people strolling round rectangles of their own inside it, clear of the column and the kiosks.
        Vector2 smin = plan.SquareMin + new Vector2(12), smax = plan.SquareMax - new Vector2(12);
        var mz = (plan.SquareMin.Y + plan.SquareMax.Y) * 0.5f;
        (Vector2 Min, Vector2 Max)[] keepOut =
        [
            (new Vector2(-11.5f), new Vector2(11.5f)),
            (new Vector2(plan.SquareMin.X + 18, mz + 15), new Vector2(plan.SquareMin.X + 26, mz + 21)),
            (new Vector2(plan.SquareMax.X - 26, mz - 21), new Vector2(plan.SquareMax.X - 18, mz - 15)),
        ];
        var beds = new List<(Vector2 Min, Vector2 Max)>(keepOut);
        foreach (var (qx, qz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var centre = new Vector2(qx * MathF.Min(36f, (plan.SquareMax.X - plan.SquareMin.X) * 0.25f), qz * MathF.Min(28f, (plan.SquareMax.Y - plan.SquareMin.Y) * 0.22f));
            beds.Add((centre - new Vector2(9.5f, 6.5f), centre + new Vector2(9.5f, 6.5f)));
        }
        for (var i = 0; i < 10; i++)
        {
            var la = i * MathF.Tau / 10 + 0.3f;
            if (i < 10)
                beds.Add((new Vector2(MathF.Cos(la), MathF.Sin(la)) * 21f - new Vector2(0.8f), new Vector2(MathF.Cos(la), MathF.Sin(la)) * 21f + new Vector2(0.8f)));
        }
        keepOut = beds.ToArray();
        for (var i = 0; i < 160; i++)
        {
            var a = new Vector2(d.Range(smin.X, smax.X), d.Range(smin.Y, smax.Y));
            var b = new Vector2(d.Range(smin.X, smax.X), d.Range(smin.Y, smax.Y));
            Vector2 lo = Vector2.Min(a, b), hi = Vector2.Max(a, b);
            if (hi.X - lo.X < 8 || hi.Y - lo.Y < 8 || keepOut.Any(k => Crosses(lo, hi, k.Min, k.Max))
                || (plan.Metro is { } m && m.Holes().Any(h => Crosses(lo, hi, h.Min - new Vector2(0.6f), h.Max + new Vector2(0.6f)))))
                continue;
            Add(lo, hi, (d.Chance(0.5f) ? 1 : -1) * d.Range(0.8f, 1.3f), d.Range(0, 400), d.Next());
        }
        var all = new List<Vector4>();
        var chunks = new List<(int, int, Vector2)>();
        // (chunks in rows, so the near ones lie in a few runs the renderer draws in one call each)
        foreach (var (key, l) in byChunk.OrderBy(kv => kv.Key.Item2).ThenBy(kv => kv.Key.Item1))
        {
            chunks.Add((all.Count / 2, l.Count / 2, new Vector2((key.Item1 + 0.5f) * CityGeometry.ChunkSize, (key.Item2 + 0.5f) * CityGeometry.ChunkSize)));
            all.AddRange(l);
        }
        return (all.ToArray(), chunks.ToArray());

        // Does a rectangle's round (its four sides) pass through a box?
        static bool Crosses(Vector2 lo, Vector2 hi, Vector2 bmin, Vector2 bmax)
        {
            bool Seg(float x0, float z0, float x1, float z1) => MathF.Max(x0, x1) > bmin.X && MathF.Min(x0, x1) < bmax.X && MathF.Max(z0, z1) > bmin.Y && MathF.Min(z0, z1) < bmax.Y;
            return Seg(lo.X, lo.Y, hi.X, lo.Y) || Seg(lo.X, hi.Y, hi.X, hi.Y) || Seg(lo.X, lo.Y, lo.X, hi.Y) || Seg(hi.X, lo.Y, hi.X, hi.Y);
        }
    }
}
