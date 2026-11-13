using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "--check-planetoid FILE [--png OUT.png] [--at LAT,LON]...": for world makers (docs/planets/PLANETS.md "Making a
/// world"). Loads a planetoid map and prints what its ground is: the features' own bounds, the heights and slopes a
/// sampling finds, how much of it each surface rule colours, the time a height takes, a digest of the sampled heights
/// (the same on every machine) and the ground at any points asked for (a landing ground, a site). The picture is the
/// whole world unrolled (longitude across, north up), coloured and shaded as the game colours it.
/// </summary>
public static class PlanetCli
{
    public static int Run(string[] args)
    {
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }; // (Console goes to the log file)
        Console.SetOut(stdout);
        string? file = null, png = null;
        var at = new List<(double Lat, double Lon)>();
        var lines = new List<double[]>();
        for (var k = 0; k < args.Length; k++)
        {
            var v = k + 1 < args.Length ? args[k + 1] : null;
            if (args[k].Equals("--check-planetoid", StringComparison.OrdinalIgnoreCase))
                file = v;
            else if (args[k].Equals("--png", StringComparison.OrdinalIgnoreCase))
                png = v;
            else if (args[k].Equals("--at", StringComparison.OrdinalIgnoreCase) && v?.Split(',') is { Length: 2 } ll
                     && double.TryParse(ll[0], System.Globalization.CultureInfo.InvariantCulture, out var la)
                     && double.TryParse(ll[1], System.Globalization.CultureInfo.InvariantCulture, out var lonArg))
                at.Add((la, lonArg));
            else if (args[k].Equals("--line", StringComparison.OrdinalIgnoreCase) && v?.Split(',') is { Length: 4 or 5 } li)
            {
                var nums = li.Select(t => double.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
                if (!nums.Any(double.IsNaN))
                    lines.Add(nums);
            }
        }
        if (file == null || !File.Exists(file))
        {
            Console.WriteLine($"ERROR: no file {file}");
            return 1;
        }
        Map m;
        try
        {
            var full = Path.GetFullPath(file);
            var dir = Path.GetDirectoryName(full)!;
            var inPack = Path.GetFileName(Path.GetDirectoryName(dir) ?? "").Equals("Maps", StringComparison.OrdinalIgnoreCase);
            m = MapFile.Load(File.ReadAllText(file), inPack ? Path.GetFileName(dir) + "/" + Path.GetFileName(file) : Path.GetFileName(file), inPack ? dir : "");
        }
        catch (Exception e) when (e is MapFileException or IOException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"REFUSED: {e.Message}");
            return 1;
        }
        if (m.Planetoid is not { } p)
        {
            Console.WriteLine($"ERROR: '{m.Name}' has no \"planetoid\"");
            return 1;
        }
        var cell = p.Radius * Math.PI / 2 / p.N;
        Console.WriteLine($"OK: '{m.Name}': radius {p.Radius:0} m, {p.N} cells a face ({cell:0.00} m), gravity {p.Strength:0.#}, {p.Features.Count} features, "
                          + $"bounds {p.MinHeight:0} to {p.MaxHeight:0} m" + (p.Crust > 0 ? $", hollow (crust {p.Crust:0} m)" : "")
                          + $", sky edge {Math.Max(m.HalfX, m.HalfZ):0} m, {m.Spawns.Count} spawns"
                          + (p.LandingTonnes > 0 ? $", ships land up to {p.LandingTonnes:0} t ({p.LandingWhy})" : ""));

        // A sampling: W x W/2 points, evenly over the sphere's area down each column (sin of the latitude uniform).
        const int w = 720, hgt = 360;
        var hs = new double[w * hgt];
        var us = double.MaxValue;
        for (var pass = 0; pass < 3; pass++) // (the best of three: the first pass also pays for the JIT)
        {
            var sw = Stopwatch.StartNew();
            for (var j = 0; j < hgt; j++)
            {
                var lat = Math.Asin(1 - (j + 0.5) * 2.0 / hgt);
                for (var i = 0; i < w; i++)
                {
                    Dir(lat, (i + 0.5) * 2 * Math.PI / w - Math.PI, out var x, out var y, out var z);
                    hs[j * w + i] = p.HeightAt(x, y, z);
                }
            }
            us = Math.Min(us, sw.Elapsed.TotalMilliseconds * 1000 / hs.Length);
        }
        double lo = double.MaxValue, hi = double.MinValue, sum = 0;
        uint digest = 2166136261;
        foreach (var h in hs)
        {
            lo = Math.Min(lo, h);
            hi = Math.Max(hi, h);
            sum += h;
            digest = (digest ^ (uint)(long)Math.Round(h * 1024)) * 16777619;
        }
        Console.WriteLine($"heights: {lo:0.0} to {hi:0.0} m, mean {sum / hs.Length:0.0} m over {hs.Length} points; {us:0.00} us a height (a {Planetoid.Tile + 1} x {Planetoid.Tile + 1} tile about {us * (Planetoid.Tile + 1) * (Planetoid.Tile + 1) / 1000:0.00} ms); digest {digest:x8}");

        // Slopes and surface rules at the game's own cell size, at every 16th sampled point.
        var surf = p.Surface;
        int n = 0, rock = 0, dirt = 0, sand = 0, snow = 0, walk = 0;
        for (var j = 0; j < hgt; j += 4)
        for (var i = 0; i < w; i += 4)
        {
            var lat = Math.Asin(1 - (j + 0.5) * 2.0 / hgt);
            var slope = Slope(p, lat, (i + 0.5) * 2 * Math.PI / w - Math.PI, cell, out var h);
            n++;
            if (slope >= surf.RockSlope) rock++;
            else if (slope >= surf.DirtSlope) dirt++;
            else if (surf.SandBelow is { } sb && h <= sb) sand++;
            if (surf.SnowAbove is { } sa && h >= sa && slope < surf.RockSlope) snow++;
            if (slope < 40) walk++;
        }
        Console.WriteLine($"ground: {rock * 100.0 / n:0}% cliff, {dirt * 100.0 / n:0}% slope, {sand * 100.0 / n:0}% low ground, {snow * 100.0 / n:0}% above the snow line; {walk * 100.0 / n:0}% under 40 degrees");

        // Ore: what the world's ground holds (WORLDS.md "Ore"), and how much of the sampled ground lies over a deposit.
        if (m.Resources is { } ore)
        {
            var over = 0;
            for (var j = 0; j < hgt; j += 4)
            for (var i = 0; i < w; i += 4)
            {
                Dir(Math.Asin(1 - (j + 0.5) * 2.0 / hgt), (i + 0.5) * 2 * Math.PI / w - Math.PI, out var x, out var y, out var z);
                if (ore.KindAt(p.Center + new Vector3((float)x, (float)y, (float)z) * (float)p.Radius, false) != null)
                    over++;
            }
            Console.WriteLine($"ore: {ore.DepositCount} deposits ({ore.Summary()}); {over * 100.0 / n:0}% of the ground lies over one");
        }
        else
            Console.WriteLine("ore: none (the map has no \"sandbox\" section)");

        foreach (var cave in PlanetCaves.Of(p))
        {
            var plan = PlanetCaves.Dig(p, cave);
            var c = plan.Chamber - p.Center;
            var where = $"cave under {cave.Num("lat", 0)}, {cave.Num("lon", 0)}: chamber {c.Length() - p.Radius:0.0} m, tunnel {plan.Length:0} m, {plan.Ops.Count} scoops";
            if (plan.Mouth is { } mo)
            {
                var d = Vector3.Normalize(mo - p.Center);
                Console.WriteLine($"{where}; its mouth at {Math.Asin(d.Y) * 180 / Math.PI:0.000}, {Math.Atan2(d.X, d.Z) * 180 / Math.PI:0.000}, height {(mo - p.Center).Length() - p.Radius:0.0} m");
            }
            else
                Console.WriteLine($"{where}; SEALED (it never comes out within {PlanetCaves.MaxLength:0} m: dig to find it)");
        }
        foreach (var l in lines)
            Line(p, l);
        foreach (var (la, lon) in at)
        {
            double worst = 0, hMin = double.MaxValue, hMax = double.MinValue;
            for (var a = 0; a < 8; a++)
            for (var r = 0; r <= 2; r++)
            {
                // Points on rings 0, 20 and 40 m out: is there room to put something down here?
                var dl = r * 20 / p.Radius;
                var la2 = la * Math.PI / 180 + dl * Math.Cos(a * Math.PI / 4);
                var lo2 = lon * Math.PI / 180 + dl * Math.Sin(a * Math.PI / 4) / Math.Max(Math.Cos(la * Math.PI / 180), 0.05);
                worst = Math.Max(worst, Slope(p, Math.Clamp(la2, -Math.PI / 2, Math.PI / 2), lo2, cell, out var h));
                hMin = Math.Min(hMin, h);
                hMax = Math.Max(hMax, h);
            }
            double farMin = double.MaxValue, farMax = double.MinValue; // the country round it, 300 m out: does the place sit in it or stand off it?
            for (var a = 0; a < 16; a++)
            {
                var dl = 300 / p.Radius;
                Dir(Math.Clamp(la * Math.PI / 180 + dl * Math.Cos(a * Math.PI / 8), -Math.PI / 2, Math.PI / 2),
                    lon * Math.PI / 180 + dl * Math.Sin(a * Math.PI / 8) / Math.Max(Math.Cos(la * Math.PI / 180), 0.05), out var x, out var y, out var z);
                var h = p.HeightAt(x, y, z);
                farMin = Math.Min(farMin, h);
                farMax = Math.Max(farMax, h);
            }
            Console.WriteLine($"at {la:0.###}, {lon:0.###}: height {hMin:0.0} to {hMax:0.0} m within 40 m, steepest {worst:0.0} degrees; {farMin:0.0} to {farMax:0.0} m at 300 m");
        }

        // In a star system: where its sun stands over this ground (the point with the sun overhead), through one turn.
        if (Space.StarSystems.ForMap(m.Name, out var entry, out var body))
        {
            var sys = entry.System;
            var bubble = new Space.SpaceBubble(sys, body);
            var b = sys.Bodies[body];
            var turn = b.RotationPeriod > 0 ? b.RotationPeriod : 86400;
            var line = $"in {sys.Name}: a turn takes {turn / 3600:0.#} h; the sun is overhead at";
            for (var q = 0; q < 4; q++)
            {
                var d = bubble.SunDirection(Space.SpaceTime.FromSeconds(sys.Start.Seconds + turn * q / 4));
                line += $" {Math.Asin(Math.Clamp(d.Y, -1, 1)) * 180 / Math.PI:0}, {Math.Atan2(d.X, d.Z) * 180 / Math.PI:0}" + (q < 3 ? ";" : " (lat, lon at each quarter turn from the start)");
            }
            Console.WriteLine(line);
        }
        else
            Console.WriteLine("in no star system (no body in Systems/*.json names this map)");

        if (png != null)
        {
            var rgb = new byte[w * hgt * 3];
            var sun = Vector3.Normalize(new Vector3(-0.6f, 0.7f, 0.2f)); // in the picture's own frame: lit from the upper left
            for (var j = 0; j < hgt; j++)
            for (var i = 0; i < w; i++)
            {
                var h = (float)hs[j * w + i];
                // The picture's slope from its neighbours (in metres: a column is cos(lat) narrower on the ground).
                var lat = Math.Asin(1 - (j + 0.5) * 2.0 / hgt);
                var dx = 2 * Math.PI / w * p.Radius * Math.Cos(lat) * 2;
                var dy = p.Radius * Math.PI / hgt * 2;
                var gx = (float)((hs[j * w + (i + 1) % w] - hs[j * w + (i + w - 1) % w]) / Math.Max(dx, 1));
                var gy = (float)((hs[Math.Max(j - 1, 0) * w + i] - hs[Math.Min(j + 1, hgt - 1) * w + i]) / dy);
                var nrm = Vector3.Normalize(new Vector3(-gx, 1, -gy));
                var slope = MathF.Acos(nrm.Y) * (180f / MathF.PI);
                var rule = slope >= surf.RockSlope ? TerrainMaterial.Rock
                    : slope >= surf.DirtSlope ? TerrainMaterial.Dirt
                    : surf.SandBelow is { } sb && h <= sb ? TerrainMaterial.Sand
                    : TerrainMaterial.Grass;
                var (col, _) = TerrainData.ColourOf(surf, new Vector3(MathF.Sqrt(MathF.Max(0, 1 - nrm.Y * nrm.Y)), nrm.Y, 0), h, (byte)rule, i * 7f, j * 7f);
                var light = 0.35f + 0.75f * MathF.Max(Vector3.Dot(nrm, sun), 0);
                var o = (j * w + i) * 3;
                rgb[o] = (byte)Math.Clamp(col.X * light * 255, 0, 255);
                rgb[o + 1] = (byte)Math.Clamp(col.Y * light * 255, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp(col.Z * light * 255, 0, 255);
            }
            MapKit.PngWriter.Write(png, w, hgt, rgb);
            Console.WriteLine($"picture: {png} ({w} x {hgt}: longitude -180 to 180 across, north up, rows even in area)");
        }
        return 0;
    }

    /// <summary>
    /// <c>--line LAT,LON,LAT,LON[,N]</c>: the ground's height at N points (60 if not said) along the great circle between two
    /// places, as metres along and the height: a crevasse's walls, a ridge's pass, a cut through a site.
    /// </summary>
    private static void Line(Planetoid p, double[] l)
    {
        var n = l.Length > 4 ? Math.Clamp((int)l[4], 2, 2000) : 60;
        Dir(l[0] * Math.PI / 180, l[1] * Math.PI / 180, out var ax, out var ay, out var az);
        Dir(l[2] * Math.PI / 180, l[3] * Math.PI / 180, out var bx, out var by, out var bz);
        var ang = Math.Acos(Math.Clamp(ax * bx + ay * by + az * bz, -1, 1));
        var sb = new System.Text.StringBuilder($"line {l[0]}, {l[1]} to {l[2]}, {l[3]} ({ang * p.Radius:0} m):");
        for (var k = 0; k < n; k++)
        {
            var t = k / (double)(n - 1);
            double wa = ang < 1e-9 ? 1 - t : Math.Sin((1 - t) * ang) / Math.Sin(ang), wb = ang < 1e-9 ? t : Math.Sin(t * ang) / Math.Sin(ang);
            sb.Append($" {t * ang * p.Radius:0}:{p.HeightAt(ax * wa + bx * wb, ay * wa + by * wb, az * wa + bz * wb):0.0}");
        }
        Console.WriteLine(sb.ToString());
    }

    private static void Dir(double lat, double lon, out double x, out double y, out double z)
    {
        x = Math.Cos(lat) * Math.Sin(lon);
        y = Math.Sin(lat);
        z = Math.Cos(lat) * Math.Cos(lon);
    }

    /// <summary>The ground's slope (degrees) at a point, measured over one cell north-south and east-west.</summary>
    private static double Slope(Planetoid p, double lat, double lon, double cell, out double h)
    {
        var d = cell / p.Radius;
        Dir(lat, lon, out var x, out var y, out var z);
        h = p.HeightAt(x, y, z);
        Dir(Math.Min(lat + d, Math.PI / 2), lon, out x, out y, out z);
        var hn = p.HeightAt(x, y, z);
        Dir(lat, lon + d / Math.Max(Math.Cos(lat), 0.05), out x, out y, out z);
        var he = p.HeightAt(x, y, z);
        var g = Math.Sqrt((hn - h) * (hn - h) + (he - h) * (he - h)) / cell;
        return Math.Atan(g) * 180 / Math.PI;
    }
}
