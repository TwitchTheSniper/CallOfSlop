using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// One line in the log for a construct physics tick over <see cref="LogMs"/> (2026-11-23: Sean's F9 recording of the Zero Day works
/// on Caledon had two 3 s freezes of 900 ms ticks billed to "Construct physics", with Material Bundles at 250 m/s, and nothing said
/// which part of the step took the time). It splits the tick as the physics does (<see cref="ConstructPhysics.PartMs"/>: the list
/// and rebuilds, each body's forces and ground, the contacts between bodies, the solver, the rest; then the rails), names the
/// slowest body rebuild and collider build, and the fastest awake bodies with what they are near. At most one line a second.
/// Nothing allocated on a tick under the limit. Server only.
/// </summary>
public static class ConstructSlowTick
{
    public const double LogMs = 50;
    private static readonly double[] _parts0 = new double[5];
    private static double _rebuildMs, _buildMs;
    private static int _rebuildBody, _rebuildParts, _buildBody, _buildParts, _builds, _rebuilds;
    private static long _lastLog;

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    internal static void Begin()
    {
        Array.Copy(ConstructPhysics.PartMs, _parts0, 5);
        (_rebuildMs, _buildMs, _builds, _rebuilds) = (0, 0, 0, 0);
    }

    internal static void Rebuilt(Construct c, long t0)
    {
        var ms = Ms(t0, Stopwatch.GetTimestamp());
        _rebuilds++;
        if (ms > _rebuildMs)
            (_rebuildMs, _rebuildBody, _rebuildParts) = (ms, c.Id, c.PartCount);
    }

    internal static void Built(Construct c, long t0)
    {
        var ms = Ms(t0, Stopwatch.GetTimestamp());
        _builds++;
        if (ms > _buildMs)
            (_buildMs, _buildBody, _buildParts) = (ms, c.Id, c.PartCount);
    }

    internal static void End(ConstructWorld w, long p0, long rails0, long p1)
    {
        var total = Ms(p0, p1);
        if (total < LogMs || p1 - _lastLog < Stopwatch.Frequency)
            return;
        _lastLog = p1;
        LogLine(w, total, Ms(rails0, p1));
    }

    private static void LogLine(ConstructWorld w, double total, double rails)
    {
        var pm = ConstructPhysics.PartMs;
        var fast = w.Bodies.Where(b => !b.Asleep && b.PartCount > 0).OrderByDescending(b => b.Vel.LengthSquared()).Take(5)
            .Select(b => $"#{b.Id} {b.DefOf(0).Name}{(b.PartCount > 1 ? $" +{b.PartCount - 1}" : "")} {b.Vel.Length():0.0} m/s (frame #{b.Frame}) near #{Nearest(w, b)}");
        Console.WriteLine($"[slow tick] construct physics {total:0.0} ms: list and rebuilds {pm[0] - _parts0[0]:0.0}, forces and ground {pm[1] - _parts0[1]:0.0}, "
            + $"contacts {pm[2] - _parts0[2]:0.0}, solve {pm[3] - _parts0[3]:0.0}, rest {pm[4] - _parts0[4]:0.0}, rails {rails:0.0}; "
            + $"{ConstructPhysics.LastSimulated} simulated, {ConstructPhysics.LastContacts} contacts; {_rebuilds} rebuilds (worst {_rebuildMs:0.0} ms, body #{_rebuildBody}, {_rebuildParts} parts), "
            + $"{_builds} colliders built (worst {_buildMs:0.0} ms, body #{_buildBody}, {_buildParts} parts); fastest awake: {string.Join("; ", fast)}");
    }

    private static int Nearest(ConstructWorld w, Construct b)
    {
        var best = -1;
        var bd = float.MaxValue;
        foreach (var o in w.Bodies)
            if (o != b && o.PartCount > 0)
            {
                var d = System.Numerics.Vector3.Distance(o.WorldCom, b.WorldCom) - o.Radius;
                if (d < bd)
                    (bd, best) = (d, o.Id);
            }
        return best;
    }
}
