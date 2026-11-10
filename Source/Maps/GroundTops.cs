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
/// Columns changed alike (within <see cref="Near"/>) weigh fully and share one height: a slope. Ones far apart
/// (<see cref="Far"/>) don't weigh at all: a wall, as before. In between the wall shrinks as the tops lean towards each
/// other, so nothing pops between slope and wall along a crater's rim. A column's middle stays at its span's top
/// (<see cref="Fans"/>), so the ground is where the spans say there and a heap or a pit keeps its size. On filled ground
/// a step a player could walk up stays walkable (<see cref="Ease"/>). Everything is worked out from the spans, the same
/// on every peer, and is the ground for drawing, movement, rays and physics alike.
/// </para>
/// </summary>
public static class GroundTops
{
    /// <summary>
    /// Columns whose changes differ by no more than this (metres) share one height at their corner: one sloping surface.
    /// It takes in the steepest step a blast leaves (a crater's side after several blasts in one place, the rim of a very
    /// big one): at 1 m those sides were bars and blocks.
    /// </summary>
    public const float Near = 4f;

    /// <summary>Columns whose changes differ by this (metres) or more don't lean towards each other at all: a deep shaft, a cutting's side.</summary>
    public const float Far = 8f;

    /// <summary>How much a column whose change differs by <paramref name="d"/> (metres, not negative) counts in another's corner height: 1 one surface, 0 a wall.</summary>
    public static float Weight(float d) => d <= Near ? 1f : d < Far ? (Far - d) / (Far - Near) : 0f;

    /// <summary>Half the step a player walks up (<see cref="World.StepHeight"/>): a corner this far off its column's own height stands for such a step.</summary>
    public const float HalfStep = 0.275f;

    /// <summary>Past this (metres off its column's own height) a corner is left where the mean put it.</summary>
    public const float EaseEnd = 0.31f;

    /// <summary>
    /// Keeps what was a walkable step walkable on ground that was filled in (a heap, a bank, a ramp: what players build to
    /// walk on). A corner <paramref name="off"/> metres from its column's own height makes a slope of that over half a
    /// column (<paramref name="size"/> metres wide); where that would be steeper than a player walks (40 degrees here, the
    /// limit is 42) and the step it stands for was one they could walk up, the corner stays at the walkable slope and the
    /// rest is a low riser between the two columns (13 cm at most). Past that it is back to the plain slope by
    /// <see cref="EaseEnd"/>. Dug ground (a crater's side, a hole) is left alone: its slopes stay unbroken.
    /// </summary>
    public static float Ease(float off, float size)
    {
        var d = MathF.Abs(off);
        var walk = size * 0.42f;
        if (d <= walk || walk >= HalfStep || d >= EaseEnd)
            return off;
        var f = d <= HalfStep ? walk : walk + (d - HalfStep) * (EaseEnd - walk) / (EaseEnd - HalfStep);
        return MathF.CopySign(f, off);
    }

    /// <summary>A column at a corner: none there (the map's edge), the map's own untouched ground, or a dug column.</summary>
    public const byte None = 0, Untouched = 1, Dug = 2;

    /// <summary>
    /// The heights of up to four columns' tops at the corner they share. Per column: its <paramref name="kind"/>, its top span
    /// (<paramref name="lo"/>, <paramref name="top"/>) and its <paramref name="delta"/> (top less top as built; an untouched
    /// column's is 0 and its span isn't read). <paramref name="baseAt"/> is the ground as built at the corner. Writes
    /// <paramref name="height"/> for the dug ones.
    /// </summary>
    public static void Corner(ReadOnlySpan<byte> kind, ReadOnlySpan<float> lo, ReadOnlySpan<float> top, ReadOnlySpan<float> delta, float baseAt, float size, Span<float> height)
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
            var own = baseAt + delta[k];
            if (delta[k] > 0)
                h = own + Ease(h - own, size); // (ground a player put there: a heap, a bank, a ramp)
            height[k] = MathF.Max(h, lo[k] + TerrainEdits.MinSpan);
        }
    }

    /// <summary>How far (metres) a column's middle may stand over or under the mean of its corners: half a column, a 45 degree point at most.</summary>
    public const float MiddleSlack = 0.25f;

    /// <summary>
    /// A column's middle, given its corners (the first four of <paramref name="t"/>), its span's top and the lowest its top
    /// may go: the span's top, but no more than <see cref="MiddleSlack"/> from the mean of its corners. So one column left
    /// standing in a crater (or one bored down alone) is a low bump or a dimple and not a spike, and a column on a steep
    /// side is a plain ramp and not a tooth.
    /// </summary>
    public static float Middle(ReadOnlySpan<float> t, float top, float floor)
    {
        var mean = (t[0] + t[1] + t[2] + t[3]) * 0.25f;
        return MathF.Max(Math.Clamp(top, mean - MiddleSlack, mean + MiddleSlack), floor);
    }

    /// <summary>Floats a column's top takes: its four corner heights (0,0), (1,0), (0,1), (1,1), then its middle's (the top of its highest span).</summary>
    public const int Stride = 5;

    /// <summary>
    /// Is a column's top a fan of four triangles round its middle (<see cref="Middle"/>: the top of its span wherever
    /// that is no spike)? Where the middle already
    /// lies on the line between corners (0,0) and (1,1) (within a centimetre: ground as built, a level pad, an even slope)
    /// two triangles split along that line do, the way the map's own cells are split.
    /// </summary>
    public static bool Fans(ReadOnlySpan<float> t) => MathF.Abs(t[4] - (t[0] + t[3]) * 0.5f) > 0.01f;

    /// <summary>A column's top at (u, v) across it (0..1 each way), from its corner heights and its middle (<see cref="Stride"/> floats).</summary>
    public static float At(ReadOnlySpan<float> t, float u, float v)
    {
        if (!Fans(t))
            return u >= v ? t[0] + u * (t[1] - t[0]) + v * (t[3] - t[1]) : t[0] + v * (t[2] - t[0]) + u * (t[3] - t[2]);
        float a = MathF.Abs(u - 0.5f), b = MathF.Abs(v - 0.5f);
        if (b >= a)
            return v <= 0.5f ? 2 * v * t[4] + t[0] * (1 - v - u) + t[1] * (u - v) : 2 * (1 - v) * t[4] + t[2] * (v - u) + t[3] * (u + v - 1);
        return u <= 0.5f ? 2 * u * t[4] + t[0] * (1 - u - v) + t[2] * (v - u) : 2 * (1 - u) * t[4] + t[1] * (u - v) + t[3] * (u + v - 1);
    }

    /// <summary>How a column's top rises across it at (u, v): the height gained over the whole column along u and along v, on the triangle there.</summary>
    public static void Slope(ReadOnlySpan<float> t, float u, float v, out float du, out float dv)
    {
        if (!Fans(t))
        {
            (du, dv) = u >= v ? (t[1] - t[0], t[3] - t[1]) : (t[3] - t[2], t[2] - t[0]);
            return;
        }
        float a = MathF.Abs(u - 0.5f), b = MathF.Abs(v - 0.5f);
        if (b >= a)
            (du, dv) = v <= 0.5f ? (t[1] - t[0], 2 * t[4] - t[0] - t[1]) : (t[3] - t[2], t[2] + t[3] - 2 * t[4]);
        else
            (du, dv) = u <= 0.5f ? (2 * t[4] - t[0] - t[2], t[2] - t[0]) : (t[1] + t[3] - 2 * t[4], t[3] - t[1]);
    }

    /// <summary>The ground under a point at height <paramref name="y"/> in a column whose top is <paramref name="top"/> there (as <see cref="TerrainEdits.GroundBelow(ReadOnlySpan{float}, float)"/>).</summary>
    public static float GroundBelow(ReadOnlySpan<float> s, float y, float top) => s.Length <= 2 || y >= s[^2] ? top : TerrainEdits.GroundBelow(s[..^2], y);

    /// <summary>Is height <paramref name="y"/> inside one of the spans, the highest ending at <paramref name="top"/>?</summary>
    public static bool Solid(ReadOnlySpan<float> s, float y, float top) => y >= s[^2] ? y <= top : s.Length > 2 && TerrainEdits.Solid(s[..^2], y);
}
