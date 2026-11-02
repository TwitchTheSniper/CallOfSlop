using System.Diagnostics;
using System.Globalization;

namespace CallOfSlop;

/// <summary>
/// <c>--weather-sim [--days N] [--seed S] [--cells N] [--latitude L]</c>: runs the weather simulation headless with the
/// Streamed Terrain map's settings (or the defaults) and prints every storm every 12 hours of weather, then a summary.
/// For tuning and for checking a "weather" section without starting the game.
/// </summary>
public static class WeatherCli
{
    public static int Run(string[] args)
    {
        var settings = new WeatherSettings();
        var days = 12.0;
        string? png = null;
        for (var i = 0; i + 1 < args.Length; i++)
        {
            var v = args[i + 1];
            switch (args[i].ToLowerInvariant())
            {
                case "--days": days = double.Parse(v, CultureInfo.InvariantCulture); break;
                case "--seed": settings.Seed = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--cells": settings.Cells = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--latitude": settings.Latitude = float.Parse(v, CultureInfo.InvariantCulture); break;
                case "--size": settings.SizeKm = float.Parse(v, CultureInfo.InvariantCulture); break;
                case "--png": png = v; break;
            }
        }
        var sw = Stopwatch.StartNew();
        var sim = new WeatherSim(settings);
        Console.WriteLine($"[weather] seed {settings.Seed}, {settings.Cells}^2 cells of {sim.Cell / 1000:0.0} km, latitude {settings.Latitude}, built in {sw.Elapsed.TotalMilliseconds:0} ms");
        Console.WriteLine($"[weather] {Describe(sim)}");
        var steps = (int)(days * 86400 / WeatherSim.Dt);
        sw.Restart();
        for (var n = 1; n <= steps; n++)
        {
            sim.Step();
            if (n % 72 == 0)
            {
                Console.WriteLine($"day {sim.Time / 86400:0.0}: {Describe(sim)}");
                if (png != null && n % 144 == 0)
                    WritePng(sim, png.Replace("{day}", $"{sim.Time / 86400:00}"));
                foreach (var s in sim.Storms)
                    if (s.Alive)
                        Console.WriteLine($"    {WeatherSim.NameOf(s.Id),-9} {WeatherSim.CategoryName(s.Category),-22} {s.Vmax,5:0.0} m/s {s.Pressure,6:0} hPa at ({s.X / 1000,6:0}, {s.Y / 1000,6:0}) km lat {sim.LatitudeAt(s.Y),5:0.0}  moving {s.Vx,5:0.0},{s.Vy,5:0.0} m/s  sst {s.Sst:0.0} shear {s.Shear:0} land {s.Land:0.00} rmax {s.Rmax / 1000:0} km");
            }
        }
        var ms = sw.Elapsed.TotalMilliseconds / steps;
        double t0 = 0, t1 = 0, t2 = 0;
        for (var n = 0; n < 100; n++)
        {
            var w = Stopwatch.StartNew();
            sim.Advance(1);
            t0 += w.Elapsed.TotalMilliseconds;
            w.Restart();
            sim.Advance(sim.N);
            t1 += w.Elapsed.TotalMilliseconds;
            w.Restart();
            sim.Advance(sim.N);
            t2 += w.Elapsed.TotalMilliseconds;
        }
        Console.WriteLine($"[weather] storms {t0 / 100:0.000} ms, winds {t1 / 100:0.000} ms, transport and physics {t2 / 100:0.000} ms a step");
        Console.WriteLine($"[weather] {steps} steps, {ms:0.00} ms a step; {sim.StormsFormed} storm(s) formed, strongest {WeatherSim.CategoryName(sim.StrongestCategory)}");
        return 0;
    }

    /// <summary>The weather map as a PNG (north up), storms ringed.</summary>
    public static void WritePng(WeatherSim sim, string path)
    {
        var n = sim.N;
        var rgba = new byte[n * n * 4];
        WeatherImage.FillMap(sim, rgba, false);
        var rgb = new byte[n * n * 3];
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var src = ((n - 1 - j) * n + i) * 4; // north up
                var dst = (j * n + i) * 3;
                rgb[dst] = rgba[src];
                rgb[dst + 1] = rgba[src + 1];
                rgb[dst + 2] = rgba[src + 2];
            }
        foreach (var s in sim.Storms)
        {
            if (!s.Alive)
                continue;
            var ci = (int)(s.X / sim.Cell + n * 0.5);
            var cj = n - 1 - (int)(s.Y / sim.Cell + n * 0.5);
            var rad = (int)(s.Rmax * 3 / sim.Cell) + 2;
            for (var a = 0; a < 64; a++)
            {
                var x = ci + (int)(Math.Cos(a * Math.PI / 32) * rad);
                var y = cj + (int)(Math.Sin(a * Math.PI / 32) * rad);
                if (x >= 0 && y >= 0 && x < n && y < n)
                {
                    rgb[(y * n + x) * 3] = 255;
                    rgb[(y * n + x) * 3 + 1] = 60;
                    rgb[(y * n + x) * 3 + 2] = 40;
                }
            }
        }
        MapKit.PngWriter.Write(path, n, n, rgb);
    }

    public static string Describe(WeatherSim sim)
    {
        double q = 0, c = 0, rain = 0, cloudy = 0, land = 0;
        var n = sim.Q.Length;
        for (var k = 0; k < n; k++)
        {
            q += sim.Q[k];
            c += sim.C[k];
            rain += sim.Rain[k];
            cloudy += sim.C[k] > 0.5f ? 1 : 0;
            land += sim.Land[k];
        }
        return $"vapour {q / n:0.0} mm, cloud {c / n:0.00} mm, cloudy {cloudy / n * 100:0}%, rain {rain / n:0.000} mm/h, land {land / n * 100:0}%";
    }
}
