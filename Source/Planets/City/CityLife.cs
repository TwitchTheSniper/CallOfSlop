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
        (Walkers, WalkerChunks) = MakeWalkers(plan);
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
                continue;
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
        // The square: people strolling round rectangles of their own inside it, clear of the column and the kiosks.
        Vector2 smin = plan.SquareMin + new Vector2(12), smax = plan.SquareMax - new Vector2(12);
        var mz = (plan.SquareMin.Y + plan.SquareMax.Y) * 0.5f;
        (Vector2 Min, Vector2 Max)[] keepOut =
        [
            (new Vector2(-8), new Vector2(8)),
            (new Vector2(plan.SquareMin.X + 18, mz + 15), new Vector2(plan.SquareMin.X + 26, mz + 21)),
            (new Vector2(plan.SquareMax.X - 26, mz - 21), new Vector2(plan.SquareMax.X - 18, mz - 15)),
        ];
        for (var i = 0; i < 160; i++)
        {
            var a = new Vector2(d.Range(smin.X, smax.X), d.Range(smin.Y, smax.Y));
            var b = new Vector2(d.Range(smin.X, smax.X), d.Range(smin.Y, smax.Y));
            Vector2 lo = Vector2.Min(a, b), hi = Vector2.Max(a, b);
            if (hi.X - lo.X < 8 || hi.Y - lo.Y < 8 || keepOut.Any(k => Crosses(lo, hi, k.Min, k.Max)))
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
