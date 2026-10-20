using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The simplest possible texturing: a handful of small greyscale textures generated in code at
/// startup (no image files), stacked into one GL texture array. The shader tints them with each
/// box's colour and maps them in world space (2 m per repeat), so the blocky look and the map
/// palettes stay the same and every surface just gets a bit of pattern.
/// </summary>
public enum Material : byte
{
    Grid, // prototype grid (Proto Box)
    Concrete, // speckled slabs
    Brick, // running-bond bricks
    Wood, // planks (crates, small props, steps)
    Metal, // riveted panels
    Tiles, // floor tiles (upper floors, roofs, walkways)
    Dirt, // noisy ground (sand, earth)
    Count,
}

public static class Textures
{
    public const int Size = 64; // texels per repeat; 2 m per repeat = 32 texels per metre

    /// <summary>All layers as RGBA8, layer after layer.</summary>
    public static byte[] Generate()
    {
        var layers = (int)Material.Count;
        var data = new byte[Size * Size * 4 * layers];
        for (var l = 0; l < layers; l++)
        {
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var v = Math.Clamp(Sample((Material)l, x, y), 0f, 1f);
                var b = (byte)(v * 255);
                var i = ((l * Size + y) * Size + x) * 4;
                data[i] = data[i + 1] = data[i + 2] = b;
                data[i + 3] = 255;
            }
        }
        return data;
    }

    // ---- Patterns (values average around 0.8 so tinting keeps colours close to the originals) ----

    private static float Sample(Material m, int x, int y)
    {
        var n = Noise(x, y, (int)m);
        switch (m)
        {
            case Material.Grid:
            {
                var fine = x % 32 == 0 || y % 32 == 0; // 1 m lines
                var coarse = x == 0 || y == 0 || x == 1 || y == 1; // 2 m lines, thicker
                return coarse ? 0.5f : fine ? 0.68f : 0.88f + n * 0.04f;
            }
            case Material.Concrete:
            {
                var seam = x == 0 || y == 0; // one slab per 2 m
                var speck = Hash(x, y, 77) > 0.93f ? -0.12f : 0;
                return seam ? 0.6f : 0.8f + n * 0.12f + speck + Blob(x, y, 5) * 0.08f;
            }
            case Material.Brick:
            {
                // 8 rows of 8 px; bricks 16 px long, every other row offset by half a brick.
                var row = y / 8;
                var bx = (x + (row % 2) * 8) % 16;
                var mortar = y % 8 == 0 || bx == 0;
                if (mortar)
                    return 0.55f + n * 0.05f;
                var brick = Hash((x + (row % 2) * 8) / 16, row, 13); // per-brick shade
                return 0.74f + brick * 0.16f + n * 0.08f;
            }
            case Material.Wood:
            {
                // 4 planks per repeat, grain running along x.
                var plank = y / 16;
                var gap = y % 16 == 0;
                if (gap)
                    return 0.45f;
                var shade = Hash(plank, 3, 21) * 0.14f;
                var grain = MathF.Sin((y + Noise(x / 4, plank, 9) * 6) * 1.7f) * 0.05f;
                var knot = Blob(x + plank * 17, y, 31) > 0.85f ? -0.1f : 0;
                return 0.74f + shade + grain + n * 0.05f + knot;
            }
            case Material.Metal:
            {
                // Two panels per repeat with a seam and rivets near the corners.
                var px = x % 32;
                var py = y % 32;
                if (px == 0 || py == 0)
                    return 0.5f;
                var rivet = (px is 3 or 28) && (py is 3 or 28);
                if (rivet)
                    return 1f;
                var brushed = Noise(x / 8, y, 41) * 0.06f;
                return 0.8f + brushed + n * 0.04f;
            }
            case Material.Tiles:
            {
                // 4x4 tiles per repeat (50 cm), slight per-tile shade.
                var grout = x % 16 == 0 || y % 16 == 0;
                if (grout)
                    return 0.6f;
                return 0.8f + Hash(x / 16, y / 16, 51) * 0.12f + n * 0.04f;
            }
            case Material.Dirt:
            {
                var pebble = Hash(x, y, 61) > 0.95f ? 0.12f : 0;
                return 0.76f + Blob(x, y, 8) * 0.14f + n * 0.12f + pebble;
            }
        }
        return 0.8f;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    /// <summary>Per-texel noise in -0.5..0.5.</summary>
    private static float Noise(int x, int y, int seed) => Hash(x, y, seed) - 0.5f;

    /// <summary>Smooth tileable blotches in 0..1 (value noise on an 8x8 grid that wraps).</summary>
    private static float Blob(int x, int y, int seed)
    {
        const int cell = 8;
        const int cells = Size / cell;
        var gx = x / cell;
        var gy = y / cell;
        var fx = (x % cell) / (float)cell;
        var fy = (y % cell) / (float)cell;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float H(int a, int b) => Hash(((a % cells) + cells) % cells, ((b % cells) + cells) % cells, seed);
        var top = H(gx, gy) + (H(gx + 1, gy) - H(gx, gy)) * fx;
        var bottom = H(gx, gy + 1) + (H(gx + 1, gy + 1) - H(gx, gy + 1)) * fx;
        return top + (bottom - top) * fy;
    }

    // ---- Picking a material for each map box ---------------------------------------------

    /// <summary>Guesses a material from a box's shape, height and colour.</summary>
    public static Material Pick(Map map, Box box, Vector3 color, bool isGround)
    {
        if (map.PrototypeGrid)
            return Material.Grid; // Proto Box stays a prototype

        var size = box.Half * 2;
        var bottom = box.Center.Y - box.Half.Y;
        var warm = color.X > color.Z + 0.08f; // reds, oranges, sand
        var sandy = color.X > color.Z + 0.12f && color.Y > color.Z + 0.04f;
        var bluish = color.Z >= color.X - 0.01f && color.Z > 0.3f;

        var greenish = color.Y > color.X + 0.04f && color.Y > color.Z + 0.04f;
        if (isGround)
            return sandy || greenish ? Material.Dirt : Material.Concrete;
        if (greenish && size.Y >= 0.4f)
            return Material.Dirt; // hills, hedges, tree canopies: leafy/earthy noise

        // Terraced sand/earth hills are stacks of wide thin slabs: keep them earthy, not tiled.
        if (sandy && size.Y <= 0.6f && MathF.Min(size.X, size.Z) > 4f)
            return Material.Dirt;

        // Floors, roofs, walkways, ledges: thin horizontal slabs off the ground.
        if (size.Y <= 0.6f && bottom > 0.4f)
            return Material.Tiles;

        // Crates, barrels, steps, small cover.
        if (size.X <= 3.2f && size.Z <= 3.2f && size.Y <= 3.2f)
            return bluish ? Material.Metal : Material.Wood;

        // Walls: thin in one direction and tall.
        var thin = MathF.Min(size.X, size.Z) <= 1.25f;
        if (thin && size.Y >= 1.5f)
            return warm && !sandy ? Material.Brick : Material.Concrete;

        // Shipping containers, machinery and other bluish/greyish big blocks.
        if (bluish)
            return Material.Metal;

        return warm && !sandy ? Material.Brick : Material.Concrete;
    }
}
