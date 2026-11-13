using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A city's metro dug into fresh ground (docs/planets/CITY.md "The metro"): each station's hall, the tunnel stubs at its
/// ends and the shafts of its flights of stairs, as the ground drill's scoops, before anything stands on the world (like
/// the caves: GameSandboxCaves.cs). They save and reach a joiner with the rest of the dug ground; a world from before the
/// metro keeps its ground, and its stairs lead down into rock.
/// </summary>
public sealed partial class Game
{
    private void CityMetroDig()
    {
        if (!IsServer || World.Map.City is not { Metro: { } m } plan || DigGround == null)
            return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var ops = 0;
        void Scoop(Vector2 p, float y, float r)
        {
            Dig(new DigOp(DigKind.Scoop, plan.Ground(p, y), r));
            ops++;
        }
        float top = -m.Depth, bed = -m.Depth - CityMetro.Bed, ceil = -m.Depth + CityMetro.Ceiling;
        const float R = 4.4f, Step = 4.8f; // (balls this far apart fill the box between their centres; the hall's walls hide the scallops)
        foreach (var z in m.Stations)
        {
            // The hall: two layers of balls over its whole floor plan and a little beyond, from under the track bed to the
            // ceiling, kept a couple of metres under the street.
            int nx = (int)MathF.Ceiling(2 * (CityMetro.HallX - 1) / Step), nz = (int)MathF.Ceiling(2 * (CityMetro.HallZ - 1) / Step);
            for (var i = 0; i <= nx; i++)
                for (var j = 0; j <= nz; j++)
                {
                    float x = -CityMetro.HallX + 1 + 2 * (CityMetro.HallX - 1) * i / nx, zz = z - CityMetro.HallZ + 1 + 2 * (CityMetro.HallZ - 1) * j / nz;
                    Scoop(new Vector2(m.X + x, zz), bed + 1.6f, R);
                    Scoop(new Vector2(m.X + x, zz), MathF.Min(ceil - 1.6f, -R - 1.5f), R);
                }
            // The tunnel stubs: one line of balls over the tracks.
            foreach (var sz in new[] { -1f, 1f })
                for (var d = CityMetro.HallZ; d <= CityMetro.HallZ + CityMetro.Stub; d += 3.5f)
                    Scoop(new Vector2(m.X, z + sz * d), bed + 2.4f, 3.9f);
            // Each flight's shaft: balls down its slope, tall enough to walk under and no wider than the opening in the
            // paving (the ground is the street's floor: a wider ball left a pit beside the railings), the first a metre
            // in from the stair's head so nothing is dug behind it.
            foreach (var (stairTop, down) in m.Flights(z))
                for (var d = 1.0f; d <= m.StairRun + 0.01f; d += 1.0f)
                {
                    var y = CityGeometry.GroundTop - d * MathF.Tan(33f * MathF.PI / 180);
                    Scoop(stairTop + new Vector2(0, down * d), y + 0.9f, 1.3f);
                }
        }
        Log($"[city] the metro dug under {World.Map.Name}: {m.Stations.Length} stations, {ops} ground ops, {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms");
    }
}
