using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The underground metro (docs/planets/CITY.md "The metro"): one line under the city's central avenue, along the plan's z
/// axis at x = <see cref="X"/>, with stations at <see cref="Stations"/> (their middles' z). A station is a hall dug under
/// the street (<see cref="Depth"/> m down to the platforms' tops): two side platforms, the tracks between, tiled walls, a
/// lit ceiling, a tunnel mouth at each end, and four flights of stairs from the pavements.
/// </summary>
public sealed class CityMetro
{
    public float X;
    public float[] Stations = [];

    /// <summary>Where the line's tunnels end (z): the trains turn back there, out of sight.</summary>
    public float Z0, Z1;

    /// <summary>The platforms' tops under the street (m).</summary>
    public float Depth = 10f;

    /// <summary>A hall's half width (x) and half length (z), its ceiling's height over the platforms, and how far the tunnel
    /// stubs run on beyond its ends (m).</summary>
    public const float HallX = 12.5f, HallZ = 45f, Ceiling = 5.5f, Stub = 30f;

    /// <summary>The track bed under the platforms' tops, the tracks' middles (x from the line) and the platforms' inner edge.</summary>
    public const float Bed = 1.0f, Track = 1.9f, Edge = 3.8f;

    /// <summary>The flights of stairs: their middle's x from the line, their width, where they leave the street (z from the
    /// hall's middle, going down towards it), and how long the opening in the pavement over the top of each is.</summary>
    public const float StairX = 9.8f, StairW = 2.2f, StairTop = 42f, Opening = 5.5f;

    /// <summary>A flight's run along the ground: it falls from the pavement to the platform at 33 degrees.</summary>
    public float StairRun => (Depth + CityGeometry.RoadLift + CityGeometry.Kerb) / MathF.Tan(33f * MathF.PI / 180);

    /// <summary>The four flights of a station at <paramref name="z"/>: each top's plan point and the way down (+1 or -1 along z).</summary>
    public IEnumerable<(Vector2 Top, float Down)> Flights(float z)
    {
        foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                yield return (new Vector2(X + sx * StairX, z + sz * StairTop), -sz);
    }

    /// <summary>The holes the flights leave in the street: the pavement over each flight's top.</summary>
    public IEnumerable<(Vector2 Min, Vector2 Max)> Holes()
    {
        foreach (var z in Stations)
            foreach (var (top, down) in Flights(z))
            {
                var a = top + new Vector2(-StairW * 0.5f - 0.15f, 0);
                var b = top + new Vector2(StairW * 0.5f + 0.15f, down * Opening);
                yield return (Vector2.Min(a, b), Vector2.Max(a, b));
            }
    }
}
