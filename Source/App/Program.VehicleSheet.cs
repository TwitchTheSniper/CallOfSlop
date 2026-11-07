using System.Numerics;
using Brutal.OpenGlApi;
using CallOfSlop.MapKit;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--vehicle-sheet "Bandit Box Truck" [--seeds N] [--from S] [--seed-list A,B] [--map NAME]</c>: a contact sheet for grading (docs/vehiclekit/VEHICLEKIT.md, "Quality tools"):
/// one PNG next to the log, vehicle_sheet_&lt;Name&gt;.png, a cell a seed with its side and front three-quarter views (the same pictures as --vehicle-shots), labelled with the seed and any grade
/// it has. A seed that doesn't generate gets a dark cell saying so. Default 12 seeds. Runs under a virtual X server, then the game quits. For the user to grade when they ask: never sent anywhere.
/// </summary>
public static unsafe partial class Program
{
    private static bool _vehicleSheet;

    private const int SheetTileW = 320, SheetTileH = 180, SheetLabel = 18, SheetPerRow = 3;

    private static void RunVehicleSheet(string[] args)
    {
        var map = args.SkipWhile(a => !a.Equals("--map", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } n ? Maps.Find(n) : Maps.Find("Construct Yard");
        World.SetMap(map ?? Maps.All[0]);
        map = World.Map;
        var name = args.SkipWhile(a => !a.Equals("--vehicle-sheet", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (name == null || VehicleRegistry.Find(name) is not { } g)
        {
            Console.WriteLine($"[vehiclesheet] no vehicle generator \"{name}\"");
            return;
        }
        var seeds = VehicleQualityCli.SeedsFrom(args, 12);
        var (style, shape) = VehicleQualityCli.Parts(g);
        var grades = VehicleQualityCli.GradesPath() is { } gp ? VehicleQualityCli.LoadGrades(gp).Where(x => x.Style == style && x.Shape == shape).ToDictionary(x => x.Seed, x => x.Value) : [];
        var cellW = SheetTileW * 2 + 6;
        var cellH = SheetTileH + SheetLabel + 6;
        var rows = (seeds.Count + SheetPerRow - 1) / SheetPerRow;
        var (width, height) = (cellW * Math.Min(SheetPerRow, Math.Max(1, seeds.Count)), cellH * Math.Max(1, rows) + SheetLabel + 4);
        var sheet = new byte[width * height * 3];
        Array.Fill(sheet, (byte)236);
        SheetText(sheet, width, 4, 3, $"{g.Name}  seeds {(seeds.Count > 1 && seeds[^1] - seeds[0] == seeds.Count - 1 ? $"{seeds[0]}-{seeds[^1]}" : string.Join(" ", seeds))}  shipped {string.Join(" ", g.Seeds)}", (20, 20, 20));
        for (var k = 0; k < seeds.Count && !_window.ShouldClose; k++)
        {
            var seed = seeds[k];
            var (x0, y0) = (k % SheetPerRow * cellW, SheetLabel + 4 + k / SheetPerRow * cellH);
            var label = $"seed {seed}" + (grades.TryGetValue(seed, out var gr) ? $"  grade {gr}" : "") + (g.Seeds.Contains(seed) ? "  shipped" : "");
            SheetText(sheet, width, x0 + 2, y0 + 3, label, (20, 20, 20));
            ConstructWorld w;
            Construct? c;
            try
            {
                var v = VehicleRegistry.Make(g, seed);
                var bp = ConstructBuild.ParseBlueprint(v.Text(), "sheet.json");
                w = new ConstructWorld();
                c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, Vector3.Zero), 0, out var why, VehicleChecks.TowOf(bp));
                if (c == null)
                    throw new VehicleKitException(why);
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                SheetFill(sheet, width, x0, y0 + SheetLabel, SheetTileW * 2 + 2, SheetTileH, (60, 40, 40));
                SheetText(sheet, width, x0 + 8, y0 + SheetLabel + 8, "does not generate", (240, 200, 200));
                Console.WriteLine($"[vehiclesheet] {g.Name} {seed}: {ex.Message}");
                continue;
            }
            var mid = c.Pos + Vector3.Transform((c.LocalMin + c.LocalMax) * 0.5f, c.Rot);
            var size = MathF.Max(c.LocalMax.Z - c.LocalMin.Z, 5);
            var views = new (Vector3 Eye, Vector3 Look)[]
            {
                (mid + new Vector3(-size * 1.2f, 0.3f, 0), mid),
                (mid + new Vector3(size * 0.8f, 1.5f, -size * 0.8f), mid),
            };
            for (var v = 0; v < views.Length; v++)
            {
                var tx = x0 + v * (SheetTileW + 2);
                ShootVehicleView(w, map, views[v].Eye, views[v].Look, 0.8f, () => SheetTile(sheet, width, tx, y0 + SheetLabel));
            }
            Console.WriteLine($"[vehiclesheet] {g.Name} {seed}");
        }
        var file = AppFolders.LogPath($"vehicle_sheet_{VehicleRegistry.Slug(g.Name)}.png");
        PngWriter.Write(file, width, height, sheet);
        Console.WriteLine($"[vehiclesheet] wrote {file} ({seeds.Count} seeds)");
    }

    /// <summary>Reads the back buffer and puts it, scaled down to a tile (a box filter), at (x0, y0) of the sheet, top row first.</summary>
    private static void SheetTile(byte[] sheet, int width, int x0, int y0)
    {
        var fb = _window.FramebufferSize;
        int w = fb.X, h = fb.Y;
        var pixels = new byte[w * h * 4];
        OpenGl.Native.glPixelStorei(OpenGl.GL_PACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            OpenGl.Native.glReadPixels(0, 0, w, h, OpenGl.GL_RGBA, OpenGl.GL_UNSIGNED_BYTE, p);
        var scale = MathF.Max(w / (float)SheetTileW, h / (float)SheetTileH);
        var (usedW, usedH) = ((int)(w / scale), (int)(h / scale));
        var (ox, oy) = ((SheetTileW - usedW) / 2, (SheetTileH - usedH) / 2);
        var step = Math.Max(1, (int)scale);
        for (var ty = 0; ty < usedH; ty++)
            for (var tx = 0; tx < usedW; tx++)
            {
                int r = 0, gg = 0, b = 0, count = 0;
                var (sx, sy) = ((int)(tx * scale), (int)(ty * scale));
                for (var dy = 0; dy < step && sy + dy < h; dy++)
                    for (var dx = 0; dx < step && sx + dx < w; dx++)
                    {
                        var at = ((h - 1 - (sy + dy)) * w + sx + dx) * 4; // (GL rows go bottom up)
                        (r, gg, b, count) = (r + pixels[at], gg + pixels[at + 1], b + pixels[at + 2], count + 1);
                    }
                var o = ((y0 + oy + ty) * width + x0 + ox + tx) * 3;
                if (o < 0 || o + 2 >= sheet.Length || count == 0)
                    continue;
                (sheet[o], sheet[o + 1], sheet[o + 2]) = ((byte)(r / count), (byte)(gg / count), (byte)(b / count));
            }
    }

    private static void SheetFill(byte[] sheet, int width, int x0, int y0, int w, int h, (byte R, byte G, byte B) c)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w && x < width; x++)
            {
                var o = (y * width + x) * 3;
                if (o >= 0 && o + 2 < sheet.Length)
                    (sheet[o], sheet[o + 1], sheet[o + 2]) = c;
            }
    }

    /// <summary>A 3 by 5 pixel font, doubled, for labels: digits, capitals (text is upper-cased) and - : . / +.</summary>
    private static readonly Dictionary<char, string> SheetGlyphs = new()
    {
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111", ['4'] = "101101111001001",
        ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001001001001", ['8'] = "111101111101111", ['9'] = "111101111001111",
        ['A'] = "010101111101101", ['B'] = "110101110101110", ['C'] = "011100100100011", ['D'] = "110101101101110", ['E'] = "111100110100111",
        ['F'] = "111100110100100", ['G'] = "011100101101011", ['H'] = "101101111101101", ['I'] = "111010010010111", ['J'] = "001001001101010",
        ['K'] = "101101110101101", ['L'] = "100100100100111", ['M'] = "101111111101101", ['N'] = "110101101101101", ['O'] = "010101101101010",
        ['P'] = "110101110100100", ['Q'] = "010101101110011", ['R'] = "110101110101101", ['S'] = "011100010001110", ['T'] = "111010010010010",
        ['U'] = "101101101101111", ['V'] = "101101101101010", ['W'] = "101101111111101", ['X'] = "101101010101101", ['Y'] = "101101010010010",
        ['Z'] = "111001010100111", ['-'] = "000000111000000", [':'] = "000010000010000", ['.'] = "000000000000010", ['/'] = "001001010100100",
        ['+'] = "000010111010000",
    };

    private static void SheetText(byte[] sheet, int width, int x0, int y0, string text, (byte R, byte G, byte B) c)
    {
        const int px = 2;
        var x = x0;
        foreach (var ch in text.ToUpperInvariant())
        {
            if (SheetGlyphs.TryGetValue(ch, out var bits))
                for (var k = 0; k < 15; k++)
                    if (bits[k] == '1')
                        SheetFill(sheet, width, x + k % 3 * px, y0 + k / 3 * px, px, px, c);
            x += 4 * px;
            if (x > width - 4 * px)
                return;
        }
    }
}
