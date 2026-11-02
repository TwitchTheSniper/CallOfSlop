namespace CallOfSlop;

/// <summary>
/// Pictures of a <see cref="WeatherSim"/>: the F11 weather map (satellite-style clouds over sea and land, or a rain
/// radar) and the two-channel cloud texture the sky reads (Source/Render/Sky/SkyRenderer.cs). Rows go south to north.
/// No allocations.
/// </summary>
public static class WeatherImage
{
    /// <summary>How much of the sky cloud water (mm) covers: thin cloud is patchy, a few mm is overcast.</summary>
    public static float Cover(float cloud) => 1f - MathF.Exp(-cloud / 1.6f);

    /// <summary>The weather map, RGBA, N x N (row 0 the south edge). <paramref name="radar"/>: rain colours instead of cloud.</summary>
    public static void FillMap(WeatherSim sim, byte[] rgba, bool radar)
    {
        var n = sim.N;
        for (var j = 0; j < n; j++)
        {
            var lat = MathF.Abs((float)sim.LatitudeAt(sim.CellY(j)));
            for (var i = 0; i < n; i++)
            {
                var k = j * n + i;
                var land = sim.Land[k];
                // Sea by temperature (warm enough for hurricanes is lighter and greener), land greener in the tropics.
                var sst = sim.Sst[k] + sim.SstAnom[k];
                var warm = Math.Clamp((sst - 25f) / 4.5f, 0f, 1f);
                float r = 0.05f + 0.06f * warm, g = 0.16f + 0.16f * warm, b = 0.32f + 0.1f * warm;
                var dry = Math.Clamp((lat - 18f) / 14f, 0f, 1f);
                float lr = 0.20f + 0.25f * dry, lg = 0.34f + 0.06f * dry, lb = 0.16f + 0.08f * dry;
                r += (lr - r) * land;
                g += (lg - g) * land;
                b += (lb - b) * land;
                if (radar)
                {
                    var rain = sim.Rain[k];
                    if (rain > 0.2f)
                    {
                        // Green, yellow, red, magenta as the rain gets heavier (mm/h).
                        var t = Math.Clamp(MathF.Log2(rain / 0.2f) / 7f, 0f, 1f);
                        float cr, cg, cb;
                        if (t < 0.33f) { cr = 0.1f; cg = 0.75f; cb = 0.2f; }
                        else if (t < 0.6f) { cr = 0.95f; cg = 0.9f; cb = 0.15f; }
                        else if (t < 0.85f) { cr = 0.95f; cg = 0.25f; cb = 0.1f; }
                        else { cr = 0.9f; cg = 0.2f; cb = 0.85f; }
                        var a = Math.Clamp(rain / 0.6f, 0.35f, 0.9f);
                        r += (cr - r) * a;
                        g += (cg - g) * a;
                        b += (cb - b) * a;
                    }
                }
                else
                {
                    var c = sim.C[k];
                    var cover = Cover(c);
                    var shade = 1f - Math.Clamp((c - 6f) / 40f, 0f, 0.12f); // the deepest towers a touch greyer
                    r += (0.95f * shade - r) * cover;
                    g += (0.96f * shade - g) * cover;
                    b += (0.98f * shade - b) * cover;
                }
                var o = k * 4;
                rgba[o] = (byte)(Math.Clamp(r, 0f, 1f) * 255f);
                rgba[o + 1] = (byte)(Math.Clamp(g, 0f, 1f) * 255f);
                rgba[o + 2] = (byte)(Math.Clamp(b, 0f, 1f) * 255f);
                rgba[o + 3] = 255;
            }
        }
    }

    /// <summary>
    /// The sky's cloud texture, two bytes a cell: the noise threshold for the cell's coverage (as
    /// <see cref="SkyRenderer.CoverageThreshold"/> gives it) and how thick and dark the cloud is (0-1).
    /// </summary>
    public static void FillSky(WeatherSim sim, byte[] rg)
    {
        for (var k = 0; k < sim.C.Length; k++)
        {
            var c = sim.C[k];
            rg[k * 2] = (byte)(Math.Clamp(SkyRenderer.CoverageThreshold(Cover(c)), 0f, 1f) * 255f + 0.5f);
            rg[k * 2 + 1] = (byte)(Math.Clamp(c / 10f, 0f, 1f) * 255f + 0.5f);
        }
    }
}
