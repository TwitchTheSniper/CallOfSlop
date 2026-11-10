using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Smooth tops for dug ground (docs/sandbox/TERRAIN-DIGGING.md, "Smooth tops"). The ground stays columns of spans
/// (<see cref="TerrainEdits"/>, <see cref="PlanetEdits"/>); only the open top of each column's highest span stops being a
/// level square. At every corner where four columns meet, each column's top takes a height worked out from all four, so
/// tops that belong together meet there and make one sloping surface, and a column's top is the two triangles between its
/// four corner heights.
/// <para>
/// The height is the ground as built at the corner plus a mean of how far the columns there have been dug or filled (top
/// now less top as built), each weighed by how close its change is to the column's own (<see cref="Weight"/>). So ground
/// nobody has changed is the map's own surface exactly, with no terraces in a cell a dig merely touched; a cut or a fill of
/// even depth is a copy of the slope it was made on; and where a column meets a cell that is still the map's own ground it
/// comes up (or down) to that ground's surface with no wall.
/// </para>
/// <para>
/// Columns changed alike (within <see cref="Walk"/>) weigh fully and share one height: a slope. Ones far apart
/// (<see cref="Far"/>) don't weigh at all: a wall, as before. In between the wall shrinks as the tops lean towards each
/// other, so nothing pops between slope and wall along a crater's rim. What was a walkable step is a walkable ramp or a
/// lower step, never a slope too steep to walk. Everything is worked out from the spans, the same on every peer, and is the
/// ground for drawing, movement, rays and physics alike.
/// </para>
/// </summary>
public static class GroundTops
{
    /// <summary>Columns whose changes differ by no more than this (metres) share one height at their corner: one sloping surface.</summary>
    public const float Near = 1f;

    /// <summary>Columns whose changes differ by this (metres) or more don't lean towards each other at all: a shaft, a trench's side, a cut bank.</summary>
    public const float Far = 2f;

    /// <summary>How much a column whose change differs by <paramref name="d"/> (metres, not negative) counts in another's corner height: 1 one surface, 0 a wall.</summary>
    public static float Weight(float d) => d <= Near ? 1f : d < Far ? (Far - d) / (Far - Near) : 0f;

    /// <summary>A column at a corner: none there (the map's edge), the map's own untouched ground, or a dug column.</summary>
    public const byte None = 0, Untouched = 1, Dug = 2;

    /// <summary>
    /// The heights of up to four columns' tops at the corner they share. Per column: its <paramref name="kind"/>, its top span
    /// (<paramref name="lo"/>, <paramref name="top"/>) and its <paramref name="delta"/> (top less top as built; an untouched
    /// column's is 0 and its span isn't read). <paramref name="baseAt"/> is the ground as built at the corner. Writes
    /// <paramref name="height"/> for the dug ones.
    /// </summary>
    public static void Corner(ReadOnlySpan<byte> kind, ReadOnlySpan<float> lo, ReadOnlySpan<float> top, ReadOnlySpan<float> delta, float baseAt, Span<float> height)
    {
        // The quick way out, and most corners: four dug columns nobody has changed, or all changed alike.
        if (kind[0] == Dug && kind[1] == Dug && kind[2] == Dug && kind[3] == Dug && delta[0] == delta[1] && delta[1] == delta[2] && delta[2] == delta[3])
        {
            var h = baseAt + delta[0];
            for (var k = 0; k < 4; k++)
                height[k] = MathF.Max(h, lo[k] + TerrainEdits.MinSpan);
            return;
        }
        for (var k = 0; k < 4; k++)
        {
            if (kind[k] != Dug)
                continue;
            float sum = delta[k], weights = 1f;
            var pin = 0f; // (how firmly the map's own ground there holds this top to its surface: it can't move)
            for (var m = 0; m < 4; m++)
            {
                if (m == k || kind[m] == None)
                    continue;
                if (kind[m] == Untouched)
                {
                    if (lo[k] < baseAt - TerrainEdits.MinSpan)
                        pin = MathF.Max(pin, Weight(MathF.Abs(delta[k])));
                    continue;
                }
                // Two dug columns: their top spans side by side (not one's ledge hanging over the other's floor).
                if (!(lo[k] < top[m] - TerrainEdits.MinSpan && lo[m] < top[k] - TerrainEdits.MinSpan))
                    continue;
                var w = Weight(MathF.Abs(delta[m] - delta[k]));
                sum += w * delta[m];
                weights += w;
            }
            var h = baseAt + sum / weights;
            h += (baseAt - h) * pin;
            height[k] = MathF.Max(h, lo[k] + TerrainEdits.MinSpan);
        }
    }

    /// <summary>
    /// A column's top at (u, v) across it (0..1 each way) from its four corner heights (0,0), (1,0), (0,1), (1,1): two
    /// triangles split along (0,0)-(1,1), the way the map's own cells are split.
    /// </summary>
    public static float At(ReadOnlySpan<float> t, float u, float v) =>
        u >= v ? t[0] + u * (t[1] - t[0]) + v * (t[3] - t[1]) : t[0] + v * (t[2] - t[0]) + u * (t[3] - t[2]);

    /// <summary>The ground under a point at height <paramref name="y"/> in a column whose top is <paramref name="top"/> there (as <see cref="TerrainEdits.GroundBelow(ReadOnlySpan{float}, float)"/>).</summary>
    public static float GroundBelow(ReadOnlySpan<float> s, float y, float top) => s.Length <= 2 || y >= s[^2] ? top : TerrainEdits.GroundBelow(s[..^2], y);

    /// <summary>Is height <paramref name="y"/> inside one of the spans, the highest ending at <paramref name="top"/>?</summary>
    public static bool Solid(ReadOnlySpan<float> s, float y, float top) => y >= s[^2] ? y <= top : s.Length > 2 && TerrainEdits.Solid(s[..^2], y);
}
