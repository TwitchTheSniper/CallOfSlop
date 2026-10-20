using System.Numerics;

namespace CallOfSlop;

/// <summary>Extra building blocks for the medium and large maps (terrain, trees, towers, hangars...).</summary>
public sealed partial class Map
{
    /// <summary>
    /// A terraced hill: stacked 0.5 m layers, each inset by <paramref name="step"/> on every side,
    /// so you can walk straight up it (the step height is 0.55 m). Alternate layers are shaded
    /// slightly differently, which reads like contour lines.
    /// </summary>
    /// <returns>The height of the top terrace actually built (the hill stops early if it runs out of room).</returns>
    [MapOp]
    public float Hill(float cx, float cz, float w, float d, float height, Vector3 color, float step = 1.4f)
    {
        using var __ = Op([cx, cz, w, d, height, color, step]);
        var layers = (int)MathF.Round(height / 0.5f);
        var top = 0f;
        for (var i = 0; i < layers; i++)
        {
            var inset = i * step;
            if (w - 2 * inset < 1.5f || d - 2 * inset < 1.5f)
                break;
            var shade = i % 2 == 0 ? 1f : 0.93f;
            Block(cx - w / 2 + inset, i * 0.5f, cz - d / 2 + inset, cx + w / 2 - inset, (i + 1) * 0.5f, cz + d / 2 - inset, color * shade);
            top = (i + 1) * 0.5f;
        }
        return top;
    }

    /// <summary>A tree, but only where there's open ground around it (so woods don't grow through buildings).</summary>
    [MapOp]
    public void TreeIfClear(float x, float z, float h = 6f)
    {
        using var __ = Op([x, z, h]);
        foreach (var (ox, oz) in new[] { (0f, 0f), (2f, 0f), (-2f, 0f), (0f, 2f), (0f, -2f) })
            if (IsBlocked(new Vector3(x + ox, GroundHeight(x + ox, z + oz), z + oz)))
                return;
        Tree(x, z, h);
    }

    /// <summary>A long ramp of 0.5 m steps along X or Z, up to a platform at <paramref name="height"/>.</summary>
    [MapOp]
    public void Ramp(float x, float z, float width, float height, bool alongX, int dir, Vector3 color, float y0 = 0)
    {
        using var __ = Op([x, z, width, height, alongX, dir, color, y0]);
        var n = (int)MathF.Ceiling(height / 0.5f);
        var rise = height / n;
        for (var k = 1; k <= n; k++)
        {
            float a = (k - 1) * 1.0f * dir, b = k * 1.0f * dir;
            if (alongX)
                Block(x + a, y0, z - width / 2, x + b, y0 + k * rise, z + width / 2, color);
            else
                Block(x - width / 2, y0, z + a, x + width / 2, y0 + k * rise, z + b, color);
        }
    }

    public static readonly Vector3 TrunkBrown = new(0.36f, 0.26f, 0.17f);
    public static readonly Vector3 Leaf = new(0.25f, 0.48f, 0.22f);

    /// <summary>Blocky tree: trunk plus two canopy blocks starting above head height (you can walk under the edges).</summary>
    [MapOp]
    public void Tree(float x, float z, float h = 6f, float y = 0)
    {
        using var __ = Op([x, z, h, y]);
        Prop(x, z, 0.6f, h * 0.6f, 0.6f, TrunkBrown, y);
        var c = 2.8f + h * 0.15f;
        Prop(x, z, c, h * 0.35f, c, Leaf, y + h * 0.4f);
        Prop(x, z, c * 0.6f, h * 0.25f, c * 0.6f, Leaf * 1.08f, y + h * 0.75f);
    }

    /// <summary>A loose cluster of boxy boulders.</summary>
    [MapOp]
    public void Rocks(float x, float z, float size, Vector3 color, int seed)
    {
        using var __ = Op([x, z, size, color, seed]);
        var rng = new Random(seed);
        var count = 2 + rng.Next(3);
        for (var i = 0; i < count; i++)
        {
            var ox = ((float)rng.NextDouble() - 0.5f) * size;
            var oz = ((float)rng.NextDouble() - 0.5f) * size;
            var w = size * (0.35f + (float)rng.NextDouble() * 0.4f);
            var d = size * (0.35f + (float)rng.NextDouble() * 0.4f);
            var h = size * (0.25f + (float)rng.NextDouble() * 0.45f);
            Prop(x + ox, z + oz, w, h, d, color * (0.9f + (float)rng.NextDouble() * 0.2f));
        }
    }

    /// <summary>Watchtower: four legs, a platform with railings, and stairs up one side.</summary>
    [MapOp]
    public void Watchtower(float x, float z, float height, Vector3 color, int stairDir = 1)
    {
        using var __ = Op([x, z, height, color, stairDir]);
        const float s = 3f;
        foreach (var (lx, lz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            Prop(x + lx * (s / 2 - 0.2f), z + lz * (s / 2 - 0.2f), 0.4f, height, 0.4f, color);
        Block(x - s / 2, height, z - s / 2, x + s / 2, height + 0.25f, z + s / 2, color);
        // Railings (1 m, mantle-able), open on the stair side
        Block(x - s / 2, height + 0.25f, z - s / 2, x - s / 2 + 0.15f, height + 1.25f, z + s / 2, color);
        Block(x + s / 2 - 0.15f, height + 0.25f, z - s / 2, x + s / 2, height + 1.25f, z + s / 2, color);
        if (stairDir > 0)
            Block(x - s / 2, height + 0.25f, z - s / 2, x + s / 2, height + 1.25f, z - s / 2 + 0.15f, color);
        else
            Block(x - s / 2, height + 0.25f, z + s / 2 - 0.15f, x + s / 2, height + 1.25f, z + s / 2, color);
        // Stairs leading up to the open side
        var start = stairDir > 0 ? z + s / 2 + (height + 0.25f) / StepRise * StepRun : z - s / 2 - (height + 0.25f) / StepRise * StepRun;
        StairsZ(x - 0.6f, x + 0.6f, start, -stairDir, 0, height + 0.25f, color * 0.85f);
        Prop(x, z, s + 0.4f, 0.2f, s + 0.4f, color * 0.7f, height + 2.6f); // little roof
        Prop(x - s / 2, z - s / 2, 0.15f, 1.35f, 0.15f, color, height + 1.25f);
        Prop(x + s / 2, z - s / 2, 0.15f, 1.35f, 0.15f, color, height + 1.25f);
        Prop(x - s / 2, z + s / 2, 0.15f, 1.35f, 0.15f, color, height + 1.25f);
        Prop(x + s / 2, z + s / 2, 0.15f, 1.35f, 0.15f, color, height + 1.25f);
    }

    /// <summary>Big open shed: walls with a wide door on two opposite ends and a roof.</summary>
    [MapOp]
    public void Hangar(float cx, float cz, float w, float d, float h, Vector3 wall, Vector3 roof, bool doorsOnX)
    {
        using var __ = Op([cx, cz, w, d, h, wall, roof, doorsOnX]);
        const float t = 0.4f;
        float x0 = cx - w / 2, x1 = cx + w / 2, z0 = cz - d / 2, z1 = cz + d / 2;
        if (doorsOnX)
        {
            // Doors in the west and east walls
            WallZ(z0, z1, x0, t, 0, h, wall, new Hole(cz, d * 0.55f, 0, h * 0.75f));
            WallZ(z0, z1, x1 - t, t, 0, h, wall, new Hole(cz, d * 0.55f, 0, h * 0.75f));
            WallX(x0 + t, x1 - t, z0, t, 0, h, wall, new Hole(cx - w / 4, 1.5f, 1.0f, 2.3f), new Hole(cx + w / 4, 1.5f, 0, 2.5f));
            WallX(x0 + t, x1 - t, z1 - t, t, 0, h, wall, new Hole(cx - w / 4, 1.5f, 0, 2.5f), new Hole(cx + w / 4, 1.5f, 1.0f, 2.3f));
        }
        else
        {
            WallX(x0, x1, z0, t, 0, h, wall, new Hole(cx, w * 0.55f, 0, h * 0.75f));
            WallX(x0, x1, z1 - t, t, 0, h, wall, new Hole(cx, w * 0.55f, 0, h * 0.75f));
            WallZ(z0 + t, z1 - t, x0, t, 0, h, wall, new Hole(cz - d / 4, 1.5f, 1.0f, 2.3f), new Hole(cz + d / 4, 1.5f, 0, 2.5f));
            WallZ(z0 + t, z1 - t, x1 - t, t, 0, h, wall, new Hole(cz - d / 4, 1.5f, 0, 2.5f), new Hole(cz + d / 4, 1.5f, 1.0f, 2.3f));
        }
        Block(x0 - 0.3f, h, z0 - 0.3f, x1 + 0.3f, h + 0.4f, z1 + 0.3f, roof);
    }

    /// <summary>A plain block building you can't enter (city filler), with a darker roof cap.</summary>
    [MapOp]
    public void Tower(float cx, float cz, float w, float d, float h, Vector3 wall, Vector3 roof)
    {
        using var __ = Op([cx, cz, w, d, h, wall, roof]);
        Block(cx - w / 2, 0, cz - d / 2, cx + w / 2, h, cz + d / 2, wall);
        Block(cx - w / 2 - 0.2f, h, cz - d / 2 - 0.2f, cx + w / 2 + 0.2f, h + 0.5f, cz + d / 2 + 0.2f, roof);
    }

    /// <summary>A line of low cover (hedge, wall, sandbags) from (x0,z0) to (x1,z1), axis-aligned, with gaps.</summary>
    [MapOp]
    public void CoverLine(float x0, float z0, float x1, float z1, float height, float thick, Vector3 color, float segment = 6f, float gap = 2f)
    {
        using var __ = Op([x0, z0, x1, z1, height, thick, color, segment, gap]);
        var alongX = MathF.Abs(x1 - x0) >= MathF.Abs(z1 - z0);
        var a = alongX ? MathF.Min(x0, x1) : MathF.Min(z0, z1);
        var b = alongX ? MathF.Max(x0, x1) : MathF.Max(z0, z1);
        for (var p = a; p < b; p += segment + gap)
        {
            var e = MathF.Min(p + segment, b);
            if (alongX)
                Block(p, 0, z0 - thick / 2, e, height, z0 + thick / 2, color);
            else
                Block(x0 - thick / 2, 0, p, x0 + thick / 2, height, e, color);
        }
    }

    /// <summary>A ruined wall: a run of wall chunks at random heights, some missing.</summary>
    [MapOp]
    public void RuinWall(float x0, float z0, float x1, float z1, float maxH, Vector3 color, int seed)
    {
        using var __ = Op([x0, z0, x1, z1, maxH, color, seed]);
        var rng = new Random(seed);
        var alongX = MathF.Abs(x1 - x0) >= MathF.Abs(z1 - z0);
        var a = alongX ? MathF.Min(x0, x1) : MathF.Min(z0, z1);
        var b = alongX ? MathF.Max(x0, x1) : MathF.Max(z0, z1);
        for (var p = a; p < b; p += 1.5f)
        {
            if (rng.NextDouble() < 0.22)
                continue; // gap
            var h = 0.8f + (float)rng.NextDouble() * (maxH - 0.8f);
            var e = MathF.Min(p + 1.5f, b);
            if (alongX)
                Block(p, 0, z0 - 0.35f, e, h, z0 + 0.35f, color);
            else
                Block(x0 - 0.35f, 0, p, x0 + 0.35f, h, e, color);
        }
    }

    /// <summary>Train car / truck trailer: a long box on a darker base, optionally open-topped (a gondola you can stand in).</summary>
    [MapOp]
    public void TrainCar(float x, float z, bool alongX, Vector3 color, bool open = false)
    {
        using var __ = Op([x, z, alongX, color, open]);
        float len = 12f, wid = 3f, h = 3f;
        float hx = (alongX ? len : wid) / 2, hz = (alongX ? wid : len) / 2;
        Block(x - hx + 0.3f, 0, z - hz + 0.3f, x + hx - 0.3f, 0.9f, z + hz - 0.3f, new Vector3(0.15f, 0.15f, 0.16f)); // wheels/frame
        if (!open)
        {
            Block(x - hx, 0.9f, z - hz, x + hx, 0.9f + h, z + hz, color);
            return;
        }
        Block(x - hx, 0.9f, z - hz, x + hx, 1.2f, z + hz, color); // floor
        const float t = 0.15f;
        Block(x - hx, 1.2f, z - hz, x + hx, 2.4f, z - hz + t, color);
        Block(x - hx, 1.2f, z + hz - t, x + hx, 2.4f, z + hz, color);
        Block(x - hx, 1.2f, z - hz + t, x - hx + t, 2.4f, z + hz - t, color);
        Block(x + hx - t, 1.2f, z - hz + t, x + hx, 2.4f, z + hz - t, color);
    }

    /// <summary>Upright cylinder-ish tank (stacked boxes), e.g. fuel tank or silo.</summary>
    [MapOp]
    public void Tank(float x, float z, float r, float h, Vector3 color)
    {
        using var __ = Op([x, z, r, h, color]);
        Prop(x, z, r * 2, h, r * 1.2f, color);
        Prop(x, z, r * 1.2f, h, r * 2, color);
        Prop(x, z, r * 1.6f, h + 0.4f, r * 1.6f, color * 0.9f);
    }

    /// <summary>Simple blocky aircraft for the airfield.</summary>
    [MapOp]
    public void Plane(float x, float z, bool alongX, Vector3 color)
    {
        using var __ = Op([x, z, alongX, color]);
        var f = alongX ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
        var sx = alongX ? 14f : 2.4f;
        var sz = alongX ? 2.4f : 14f;
        Prop(x, z, sx, 2.4f, sz, color, 1.2f); // fuselage (you can crouch under it)
        Prop(x, z, alongX ? 3f : 16f, 0.4f, alongX ? 16f : 3f, color * 0.9f, 2.2f); // wings
        var tail = new Vector3(x, 0, z) - f * 6f;
        Prop(tail.X, tail.Z, alongX ? 2f : 6f, 0.3f, alongX ? 6f : 2f, color * 0.9f, 3.2f);
        Prop(tail.X, tail.Z, alongX ? 2f : 0.3f, 2.5f, alongX ? 0.3f : 2f, color * 0.85f, 3.5f);
        // landing gear
        Prop(x, z, 0.5f, 1.2f, 0.5f, new Vector3(0.15f, 0.15f, 0.15f));
        Prop(tail.X, tail.Z, 0.4f, 1.2f, 0.4f, new Vector3(0.15f, 0.15f, 0.15f));
    }

    /// <summary>Elevated walkway / bridge deck on pillars, from (x0,z0) to (x1,z1), at height h, with side rails.</summary>
    [MapOp]
    public void Bridge(float x0, float z0, float x1, float z1, float h, Vector3 color, bool rails = true)
    {
        using var __ = Op([x0, z0, x1, z1, h, color, rails]);
        Block(x0, h - 0.4f, z0, x1, h, z1, color);
        var alongX = MathF.Abs(x1 - x0) >= MathF.Abs(z1 - z0);
        if (rails)
        {
            if (alongX)
            {
                Block(x0, h, MathF.Min(z0, z1), x1, h + 1f, MathF.Min(z0, z1) + 0.15f, color * 0.85f);
                Block(x0, h, MathF.Max(z0, z1) - 0.15f, x1, h + 1f, MathF.Max(z0, z1), color * 0.85f);
            }
            else
            {
                Block(MathF.Min(x0, x1), h, z0, MathF.Min(x0, x1) + 0.15f, h + 1f, z1, color * 0.85f);
                Block(MathF.Max(x0, x1) - 0.15f, h, z0, MathF.Max(x0, x1), h + 1f, z1, color * 0.85f);
            }
        }
        var len = alongX ? MathF.Abs(x1 - x0) : MathF.Abs(z1 - z0);
        var n = Math.Max(2, (int)(len / 14f) + 1);
        for (var i = 0; i < n; i++)
        {
            var t = n == 1 ? 0.5f : i / (float)(n - 1);
            var px = alongX ? float.Lerp(MathF.Min(x0, x1) + 0.6f, MathF.Max(x0, x1) - 0.6f, t) : (x0 + x1) / 2;
            var pz = alongX ? (z0 + z1) / 2 : float.Lerp(MathF.Min(z0, z1) + 0.6f, MathF.Max(z0, z1) - 0.6f, t);
            Prop(px, pz, 0.8f, h - 0.4f, 0.8f, color * 0.75f);
        }
    }

    /// <summary>Spawn points spread around an ellipse at a fraction of the map size (Validate moves any that land in geometry).</summary>
    [MapOp]
    public void RingSpawns(int count, float frac, float phase = 0f)
    {
        using var __ = Op([count, frac, phase]);
        for (var i = 0; i < count; i++)
        {
            var a = phase + i * MathF.Tau / count;
            Spawn(MathF.Cos(a) * HalfX * frac, MathF.Sin(a) * HalfZ * frac);
        }
    }

    /// <summary>Bot waypoints on a grid over every free patch of ground.</summary>
    [MapOp]
    public void AutoWaypoints(float spacing)
    {
        using var __ = Op([spacing]);
        for (var x = -HalfX + spacing / 2; x < HalfX; x += spacing)
        for (var z = -HalfZ + spacing / 2; z < HalfZ; z += spacing)
        {
            var p = new Vector3(x, GroundHeight(x, z), z); // terrain maps: on the ground, and not on slopes too steep to stand on
            if (!BadSpot(p))
                Waypoints.Add(p);
        }
    }
}
