using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>Helpers other sections still use; the section this file held was removed (user, 2026-11-03).</summary>
public static partial class SelfTest
{

    private static void Tick(Game game, ref double now, double seconds, Func<bool>? stop = null) // stop: checked after every tick
    {
        for (var t = 0.0; t < seconds; t += World.TickDt)
        {
            now += World.TickDt;
            game.Update(now, World.TickDt);
            if (stop?.Invoke() == true)
                return;
        }
    }

    private static Vector3 FlatOf(Vector3 v) => new(v.X, 0, v.Z);
}
