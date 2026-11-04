using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>Helpers other sections still use; the section this file held was removed (user, 2026-11-03).</summary>
public static partial class SelfTest
{

    // ---- Loopback: chat and voice through RakNet ---------------------------------------------------------------

    private static void PumpEach(Game server, Game[] clients, double seconds, ref double now, Stopwatch clock, Action<double> each)
    {
        var until = clock.Elapsed.TotalSeconds + seconds;
        while (clock.Elapsed.TotalSeconds < until)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            each(now);
            server.Update(now, dt);
            foreach (var c in clients)
                c.Update(now, dt);
            Thread.Sleep(2);
        }
    }
}
