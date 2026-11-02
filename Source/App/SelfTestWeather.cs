using System.Diagnostics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The weather (Source/Weather, docs/weather/WEATHER.md): the "weather" map section (round trip, readable errors),
    /// Streamed Terrain having it, and the simulation on a 128-cell copy of that map's settings: deterministic whether
    /// stepped whole or a few rows at a time, no allocations per step, trade winds blowing west, a storm forming by
    /// itself within six days, a seeded depression over warm sea becoming a hurricane that turns anticlockwise, has a
    /// clear eye and moves west, a hurricane put over land dying, and the sky's cloud texture following the clouds.
    /// </summary>
    private static void CheckWeather()
    {
        // ---- The map section ------------------------------------------------------------------------------------------
        var probe = SkyProbe("\"weather\": { \"seed\": 9, \"latitude\": -18, \"cells\": 64, \"speed\": 30 },");
        if (probe.Weather is not { Seed: 9, Latitude: -18, Cells: 64, Speed: 30 } w)
            Fail($"weather section: didn't read seed 9, latitude -18, 64 cells, speed 30 ({probe.Weather?.ToJson() ?? "none"})");
        else
        {
            var back = MapFile.Load(MapFile.ToJson(probe), "Dev/WeatherProbe.json").Weather;
            if (back == null || !back.SameAs(w))
                Fail($"weather section: written as {w.ToJson()} but read back as {back?.ToJson() ?? "none"}");
        }
        if (SkyProbe("\"weather\": true,").Weather is not { } defaults || !defaults.SameAs(new WeatherSettings()) || defaults.ToJson() != "true")
            Fail("weather section: true should give the defaults (and write back as true)");
        if (SkyProbe("").Weather != null || SkyProbe("\"weather\": false,").Weather != null)
            Fail("weather section: a map without one (or false) should have no weather");
        foreach (var (bad, why) in new[] { ("{ \"sed\": 1 }", "unknown key"), ("{ \"cells\": 100 }", "multiple of 16"), ("{ \"latitude\": 2 }", "5 to 60"), ("\"yes\"", "expected true") })
        {
            try
            {
                SkyProbe($"\"weather\": {bad},");
                Fail($"weather section: {bad} should be refused ({why})");
            }
            catch (MapFileException ex) when (ex.Message.Contains(why))
            {
            }
            catch (Exception ex)
            {
                Fail($"weather section: {bad} gave \"{ex.Message}\", not a reason with \"{why}\"");
            }
        }
        var file = Path.Combine(AppContext.BaseDirectory, "Maps", "Dev", "StreamedTerrain.json");
        var streamed = File.Exists(file) ? MapFile.Load(File.ReadAllText(file), "Dev/StreamedTerrain.json") : null; // (not Maps.All: this section stays quick alone)
        if (streamed?.Weather == null || streamed.Skybox == null)
            Fail("Streamed Terrain should have weather and a procedural sky");
        if (Keybinds.Defaults[(int)GameAction.WeatherMap] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F11))
            Fail("the weather map should default to F11");

        var settings = (streamed?.Weather ?? new WeatherSettings { Seed = 7 }).Clone();
        settings.Cells = 128;

        // ---- Determinism: whole steps and row-sliced steps agree ------------------------------------------------------
        var a = new WeatherSim(settings);
        var b = new WeatherSim(settings);
        a.SpawnStorm(600_000, -700_000, 30f);
        b.SpawnStorm(600_000, -700_000, 30f);
        for (var n = 0; n < 24; n++)
            a.Step();
        while (b.Steps < 24)
            b.Advance(37);
        if (a.Digest() != b.Digest())
            Fail($"weather: 24 whole steps and 24 steps done 37 rows at a time differ ({a.Digest():X} vs {b.Digest():X})");

        // ---- No allocations per step --------------------------------------------------------------------------------
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var n = 0; n < 12; n++)
            a.Step();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes != 0)
            Fail($"weather: 12 steps allocated {bytes} B (should be none)");

        // ---- Trade winds: storm-free flow blows west in the deep tropics ---------------------------------------------
        a.Background(0, (15 - settings.Latitude) * 111_000, out var tradeU, out _);
        if (tradeU >= -2f)
            Fail($"weather: the trade wind at 15 N should blow from the east (u {tradeU:0.0} m/s)");

        // ---- A storm forms by itself ----------------------------------------------------------------------------------
        var sw = Stopwatch.StartNew();
        var natural = new WeatherSim(settings);
        var formedAt = -1.0;
        for (var n = 0; n < 6 * 144 && formedAt < 0; n++)
        {
            natural.Step();
            if (natural.StormsFormed > 0)
                formedAt = natural.Time / 86400;
        }
        var naturalMs = sw.Elapsed.TotalMilliseconds / Math.Max(natural.Steps, 1);
        if (formedAt < 0)
            Fail($"weather: no storm formed by itself in 6 days (seed {settings.Seed}; {WeatherCli.Describe(natural)})");

        // ---- A seeded depression over warm sea becomes a hurricane --------------------------------------------------
        var sim = new WeatherSim(settings);
        double sx = 0, sy = 0;
        var found = false;
        for (var j = 0; j < sim.N && !found; j++)
            for (var i = sim.N - 1; i >= sim.N / 2 && !found; i--)
            {
                var lat = sim.LatitudeAt(sim.CellY(j));
                var k = j * sim.N + i;
                if (lat is > 13 and < 17 && sim.Land[k] == 0 && sim.Sst[k] > 28.5f)
                {
                    (sx, sy) = (sim.CellX(i) - 400_000, sim.CellY(j));
                    found = sim.FieldAt(sim.Land, sx, sy) == 0;
                }
            }
        if (!found)
        {
            Fail("weather: no warm open sea at 13-17 N in the east of the domain to start a storm on");
            return;
        }
        var slot = sim.SpawnStorm(sx, sy);
        float peak = 0;
        var eyeSeen = false;
        var turning = 0f;
        for (var n = 0; n < 4 * 144 && sim.Storms[slot].Alive; n++)
        {
            if (peak >= 33 && eyeSeen && sim.Storms[slot].X - sx < -600_000)
                break; // (seen everything it should show)
            sim.Step();
            ref readonly var st = ref sim.Storms[slot];
            peak = MathF.Max(peak, st.Vmax);
            if (st.Vmax > 40 && st.Land < 0.1f && !eyeSeen)
            {
                // Anticlockwise (north): the low-level wind round a ring at 2 Rmax turns positive.
                float circ = 0;
                for (var q = 0; q < 16; q++)
                {
                    var ang = q * MathF.PI / 8;
                    float cx = MathF.Cos(ang), cy = MathF.Sin(ang);
                    var r = 2 * st.Rmax;
                    sim.WindAt(st.X + cx * r, st.Y + cy * r, false, out var u, out var v);
                    circ += -cy * u + cx * v;
                }
                turning = circ / 16 * sim.Hemisphere;
                // The eye: clearer than the eyewall round it.
                var eye = sim.FieldAt(sim.C, st.X, st.Y);
                float wall = 0;
                for (var q = 0; q < 8; q++)
                    wall += sim.FieldAt(sim.C, st.X + MathF.Cos(q * MathF.PI / 4) * st.Rmax, st.Y + MathF.Sin(q * MathF.PI / 4) * st.Rmax);
                wall /= 8;
                eyeSeen = eye < wall * 0.7f;
                Line($"weather: hurricane at day {sim.Time / 86400:0.0}: {st.Vmax:0} m/s, {st.Pressure:0} hPa, rmax {st.Rmax / 1000:0} km, mean tangential wind at 2 rmax {turning:0.0} m/s, cloud {eye:0.0} mm in the eye vs {wall:0.0} mm at the eyewall");
            }
        }
        ref readonly var end = ref sim.Storms[slot];
        if (peak < 33)
            Fail($"weather: a depression over {sim.FieldAt(sim.Sst, sx, sy):0.0} C sea only reached {peak:0} m/s in 4 days (a hurricane is 33)");
        if (turning <= 5f)
            Fail($"weather: the hurricane should turn anticlockwise round its centre (mean tangential wind {turning:0.0} m/s at 2 rmax)");
        if (!eyeSeen)
            Fail("weather: the hurricane has no clear eye");
        var moved = end.X - sx;
        if (moved > -500_000)
            Fail($"weather: the storm should move west with the trades (moved {moved / 1000:0} km east-west in {end.AgeHours:0} h)");

        // ---- Landfall: a hurricane put over land dies down ---------------------------------------------------------
        var land = new WeatherSim(settings);
        var ls = land.SpawnStorm(0, 0, 55f); // the map's own spot is always land
        for (var n = 0; n < 144 && land.Storms[ls].Alive; n++)
            land.Step();
        if (land.Storms[ls].Alive && land.Storms[ls].Vmax > 30)
            Fail($"weather: a 55 m/s hurricane over land still had {land.Storms[ls].Vmax:0} m/s a day later");

        // ---- The sky's texture follows the clouds -------------------------------------------------------------------
        var rg = new byte[sim.N * sim.N * 2];
        WeatherImage.FillSky(sim, rg);
        int clearK = -1, cloudyK = -1;
        for (var k = 0; k < sim.C.Length; k++)
        {
            if (sim.C[k] < 0.05f && clearK < 0)
                clearK = k;
            if (sim.C[k] > 6f && cloudyK < 0)
                cloudyK = k;
        }
        if (clearK < 0 || cloudyK < 0)
            Fail($"weather: expected both clear and thick-cloud cells ({WeatherCli.Describe(sim)})");
        else if (rg[clearK * 2] <= rg[cloudyK * 2] || rg[cloudyK * 2 + 1] <= rg[clearK * 2 + 1])
            Fail($"weather: sky texture: a clear cell should have a higher threshold and less thickness than a cloudy one ({rg[clearK * 2]}/{rg[clearK * 2 + 1]} vs {rg[cloudyK * 2]}/{rg[cloudyK * 2 + 1]})");

        Line($"weather: first storm formed by itself on day {formedAt:0.0}; seeded storm peaked at {peak:0} m/s ({WeatherSim.CategoryName(WeatherSim.CategoryOf(peak))}), moved {moved / 1000:0} km east-west; "
             + $"{settings.Cells}^2 cells, {naturalMs:0.00} ms a step; {WeatherCli.Describe(sim)}");
    }
}
