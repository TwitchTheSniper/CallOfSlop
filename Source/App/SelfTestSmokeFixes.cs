using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Checks for findings from the real-GPU smoke run: shaped maps have ground (fogged into the horizon) beyond their
/// outline out to the far plane, maps without a boundary keep their old fog, the timestamp GPU timer the prop renderer
/// uses reads every pass back exactly once and only when ready, and the scene graph stats can tell build frames apart.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Map horizons: ground and fog beyond a boundary, older packs' fog unchanged.</summary>
    private static void CheckMapHorizons()
    {
        var shaped = 0;
        foreach (var m in Maps.All)
            if (MapHorizonCheck(m))
                shaped++;
        if (shaped == 0)
            Fail("no map has a boundary (the Cluttered pack should)");
        var carousel = Maps.All.FirstOrDefault(x => x.Name == "Carousel");
        if (carousel != null && (carousel.Size != MapSize.Small || !(carousel.FogEnd > carousel.FogStart)))
            Fail("Carousel: a small circle map without fog over its far ground (the smoke run's black void below the horizon)");
        Line($"{shaped} shaped maps have ground and fog past the outline; maps without a boundary keep their fog");
    }

    /// <summary>
    /// One map's horizon checks (also run by the "Map contract" section and --check-map): fog past a boundary, the far
    /// ground and its fade. Returns whether the map has a boundary.
    /// </summary>
    private static bool MapHorizonCheck(Map m)
    {
        if (m.Boundary is not { } b)
        {
            // No boundary (every Legacy map: the old Classic, Collision Point and High Ground packs): exactly the fog maps always had.
            var (start, end) = m.PointGravity != null ? (400f, 0f) : m.Size switch // (a planet: no fog in space, MapPlanet.cs)
            {
                MapSize.Large => (70f, m.FarPlane * 0.9f),
                MapSize.Medium => (90f, 260f),
                _ => (400f, 0f),
            };
            if (m.FogStart != start || m.FogEnd != end)
                Fail($"{m.Name}: fog {m.FogStart:0.#}-{m.FogEnd:0.#} m, was {start:0.#}-{end:0.#} m (a map without a boundary changed its look)");
            var old = m.Skybox is { } sky ? sky.Light() : Lighting.Default;
            if (m.OuterFog != Vector4.Zero || m.Light != old)
                Fail($"{m.Name}: light changed (outer fog {m.OuterFog}) on a map without a boundary");
            return false;
        }
        // Fog must reach its full strength by the far plane, where the far ground is cut off, so the cut never shows.
        if (!(m.FogEnd > m.FogStart) || m.FogEnd > m.FarPlane)
            Fail($"{m.Name}: shaped map with fog {m.FogStart:0.#}-{m.FogEnd:0.#} m (needs start < end <= far plane {m.FarPlane:0} m)");
        // Small shaped maps: nothing inside the outline is fogged.
        var across = 0f;
        foreach (var p in b.Outline)
        foreach (var q in b.Outline)
            across = MathF.Max(across, Vector2.Distance(p, q));
        if (m.Size == MapSize.Small && m.FogStart < across)
            Fail($"{m.Name}: fog starts at {m.FogStart:0} m, inside the {across:0} m wide playable area");
        if (m.HasTerrain)
            return true; // terrain maps: the land itself runs past the outline (shaded darker)
        if (m.FallKillY != null)
            return true; // void maps (Space pack): no ground on purpose, the sky image is the view below
        // Flat shaped maps: the floor plus a far ground covering the far plane's reach from anywhere inside the outline.
        var floor = m.MeshPlacements.FirstOrDefault(p => p.Source == "boundary floor");
        var far = m.MeshPlacements.FirstOrDefault(p => p.Source == "boundary far ground");
        if (floor == null || far?.Mesh == null)
        {
            Fail($"{m.Name}: shaped flat map without {(floor == null ? "a boundary floor" : "a far ground")} (the view past the edge would be empty)");
            return true;
        }
        var bb = far.Mesh.Bounds;
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var c = 0; c < 8; c++)
        {
            var corner = new Vector3((c & 1) != 0 ? bb.Max.X : bb.Min.X, (c & 2) != 0 ? bb.Max.Y : bb.Min.Y, (c & 4) != 0 ? bb.Max.Z : bb.Min.Z);
            var w = Vector3.Transform(corner, far.Transform);
            lo = Vector3.Min(lo, w);
            hi = Vector3.Max(hi, w);
        }
        var reach = m.FarPlane;
        if (lo.X > b.Min.X - reach || lo.Z > b.Min.Y - reach || hi.X < b.Max.X + reach || hi.Z < b.Max.Y + reach)
            Fail($"{m.Name}: far ground covers x {lo.X:0}..{hi.X:0}, z {lo.Z:0}..{hi.Z:0}; needs the outline plus the far plane ({reach:0} m) on every side");
        if (hi.Y > 0.001f || hi.Y < -1f)
            Fail($"{m.Name}: far ground top at y {hi.Y:0.00} (should sit just under the floor)");
        if (far.Color.X + far.Color.Y + far.Color.Z <= 0.01f)
            Fail($"{m.Name}: far ground is black");
        var worst = CheckFarGroundHorizon(m, b, far.Color, hi.Y);
        Line($"{m.Name}: {b.Spec.Shape} outline {across:0} m across ({m.Size}), far ground {hi.X - lo.X:0} x {hi.Z - lo.Z:0} m, "
             + $"fog {m.FogStart:0}-{m.FogEnd:0} m, horizon fade {m.OuterFog.Z:0}-{m.OuterFog.W:0} m from the centre, far plane {m.FarPlane:0} m, "
             + $"far ground vs sky horizon worst {worst:0.0000} over {SkySettings.PresetNames.Length} presets + flat sky");
        return true;
    }

    /// <summary>
    /// A flat shaped map's far ground and the sky below the horizon must meet the sky's horizon colour (no dark band, the
    /// second smoke run's Carousel overview): for every sky preset and the classic flat sky, from cameras low and high
    /// inside the outline, every visible far-ground point past the fade and every below-horizon sky direction must be
    /// within a small difference of the sky at the horizon in that direction; nothing inside the floor is touched.
    /// Returns the largest difference seen.
    /// </summary>
    private static float CheckFarGroundHorizon(Map m, MapBoundary b, Vector4 farColor, float farTop)
    {
        const float tolerance = 0.02f;
        var of = m.OuterFog;
        if (!(of.W > of.Z))
        {
            Fail($"{m.Name}: flat shaped map without a horizon fade past its outline");
            return 1f;
        }
        var c = new Vector2(of.X, of.Y);
        var radius = 0f;
        foreach (var p in b.Outline)
            radius = MathF.Max(radius, Vector2.Distance(p, c));
        if (of.Z < radius + MathF.Min(b.Spec.GroundMargin, 64f))
            Fail($"{m.Name}: horizon fade starts {of.Z:0.0} m from the centre, inside the outline plus floor margin ({radius + b.Spec.GroundMargin:0.0} m)");
        // Ground cut off by the far plane must already be fully faded, or the cut would show against the sky.
        if (of.W + radius > m.FarPlane)
            Fail($"{m.Name}: horizon fade ends {of.W:0} m from the centre, but the far plane ({m.FarPlane:0} m) cuts ground at {m.FarPlane - radius:0} m");
        var lights = new List<(string Name, LightSetup Light, Vector3 Flat)>();
        var starts = m.OuterFogStarts;
        foreach (var preset in SkySettings.PresetNames)
            lights.Add((preset, SkySettings.FromPreset(preset).Light() with { OuterFog = of, OuterStarts = starts }, m.Sky));
        lights.Add(("flat sky", Lighting.Default with { OuterFog = of, OuterStarts = starts }, m.Sky));
        if (m.Light.OuterFog != of || !ReferenceEquals(m.Light.OuterStarts, starts) || starts is not { Length: Map.OuterFogDirections })
            Fail($"{m.Name}: the map's light doesn't carry its horizon fade (and its {Map.OuterFogDirections} start radii)");
        else
            CheckOuterStarts(m, b, lights[^1].Light, c);
        var worst = 0f;
        var cams = new List<Vector3>();
        foreach (var h in new[] { 1.7f, 13f, 40f, 90f })
        {
            cams.Add(new Vector3(c.X, h, c.Y));
            for (var k = 0; k < 4; k++)
            {
                var a = k * MathF.PI / 2 + 0.4f;
                var p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                if (!b.Contains(p.X, p.Y))
                    p = c + (p - c) * 0.6f;
                cams.Add(new Vector3(p.X, h, p.Y));
            }
        }
        var albedo = new Vector3(farColor.X, farColor.Y, farColor.Z);
        foreach (var (name, light, flat) in lights)
        {
            var plain = light with { OuterFog = Vector4.Zero };
            var lit = albedo * (light.AmbSky + light.SunColor * MathF.Max(light.SunDir.Y, 0f)); // the far ground's top, roughly lit
            var bad = 0;
            foreach (var cam in cams)
            for (var i = 0; i < 24; i++)
            {
                var az = i * MathF.Tau / 24f;
                var flatDir = new Vector3(MathF.Cos(az), 0f, MathF.Sin(az));
                // The sky right at the horizon in this direction (what the far ground must meet).
                var horizon = light.SkyFog ? Lighting.SkyGradient(plain, Vector3.Normalize(flatDir + new Vector3(0, 1e-4f, 0))) : flat;
                foreach (var drop in new[] { 0.002f, 0.01f, 0.03f, 0.08f, 0.2f, 0.5f })
                {
                    var dir = Vector3.Normalize(flatDir - new Vector3(0, drop, 0));
                    // Below the horizon where no ground is drawn (past the far plane): the sky itself.
                    var skyBelow = light.SkyFog ? Lighting.SkyGradient(light, dir) : flat;
                    var d0 = Vector3.Distance(skyBelow, horizon);
                    // The far ground along this ray, when it's within the far plane and past the floor.
                    var t = (cam.Y - farTop) / -dir.Y;
                    var d1 = 0f;
                    if (t > 0 && t < m.FarPlane)
                    {
                        var hit = cam + dir * t;
                        var r = Vector2.Distance(new Vector2(hit.X, hit.Z), c);
                        if (r >= Lighting.OuterStart(light, new Vector2(hit.X, hit.Z) - c) + (of.W - of.Z)) // fully faded in this direction
                            d1 = Vector3.Distance(Lighting.ApplyFog(light, lit, hit, cam, flat, new Vector2(m.FogStart, m.FogEnd)), horizon);
                    }
                    var d = MathF.Max(d0, d1);
                    worst = MathF.Max(worst, d);
                    if (d > tolerance && bad++ == 0)
                        Fail($"{m.Name} under {name}: from ({cam.X:0},{cam.Y:0},{cam.Z:0}) looking {drop:0.###} below the horizon, "
                             + $"{(d1 > d0 ? "far ground" : "sky")} differs from the sky's horizon colour by {d:0.000} (max {tolerance})");
                }
            }
            // Nothing on the floor is touched by the fade (only by the map's usual fog).
            var inside = new Vector3(c.X + radius * 0.5f, 0f, c.Y);
            var cam0 = new Vector3(c.X, 1.7f, c.Y);
            var range = new Vector2(m.FogStart, m.FogEnd);
            if (Lighting.ApplyFog(light, lit, inside, cam0, flat, range) != Lighting.ApplyFog(plain, lit, inside, cam0, flat, range))
                Fail($"{m.Name} under {name}: the horizon fade changes the floor inside the outline");
        }
        return worst;
    }

    /// <summary>
    /// The per-direction horizon fade (<see cref="Map.OuterFogStarts"/>): it never touches the floor and it starts close
    /// past the floor on every side (Old Town, twice as wide as deep, had a band of dark far ground past its short sides).
    /// The rules live in <see cref="MapKit.MapChecks.HorizonFade"/>, so a generated map is checked the same way before
    /// it is committed.
    /// </summary>
    private static void CheckOuterStarts(Map m, MapBoundary b, in LightSetup light, Vector2 c)
    {
        var fade = MapKit.MapChecks.HorizonFade(m, light);
        const int n = Map.OuterFogDirections;
        Line($"{m.Name}: horizon fade starts {light.OuterStarts!.Min():0}-{light.OuterStarts!.Max():0} m from the centre, at most {fade.Worst:0.0} m past the floor "
             + $"(direction {fade.WorstAt}{(fade.Notched ? ", notched outline" : "")}); the farthest point's radius would be {light.OuterFog.Z:0} m");
        foreach (var f in fade.Failures)
            Fail($"{m.Name}: {f}");
        if (n % 4 != 0 || !Lighting.Glsl.Contains($"uniform vec4 uOuterR[{n / 4}];") || !Lighting.Glsl.Contains($"{n}.0 / 6.28318531") || !Lighting.Glsl.Contains($"% {n};"))
            Fail($"the shader's uOuterR array isn't {n} radii long (Map.OuterFogDirections)");
    }

    /// <summary>
    /// Gunsmith backdrop: the showcase pages have no ImGui window background (the 3D gun is drawn behind the UI), so the
    /// loadout window paints itself opaque around the 3D view (Ui.ShowcaseBackdrop) and the HUD's key help and footer-side
    /// panels can't show through. At the smoke window and the user's monitors, the four bands cover exactly the window
    /// minus the view, without covering the view.
    /// </summary>
    private static void CheckShowcaseBackdrop()
    {
        foreach (var (w, h) in new[] { (2679f, 1786f), (5120f, 1440f), (3440f, 1440f), (1920f, 1080f) })
        {
            var scale = Math.Clamp(h / 900f, 0.8f, 3f); // Ui.TargetScale at the default UI scale
            var size = new Brutal.Numerics.float2(MathF.Min(1150 * scale, w - 40), MathF.Min(760 * scale, h - 40));
            var min = new Brutal.Numerics.float2((w - size.X) / 2, (h - size.Y) / 2);
            var max = min + size;
            // The attachment showcase's view (UiGunsmith: 20-96% across, 17-78% down the body).
            var v0 = min + new Brutal.Numerics.float2(size.X * 0.2f, size.Y * 0.2f);
            var v1 = min + new Brutal.Numerics.float2(size.X * 0.96f, size.Y * 0.75f);
            var bands = Ui.ShowcaseBackdropRects(min, max, v0, v1);
            var area = 0f;
            foreach (var (a, b) in bands)
            {
                area += MathF.Max(0, b.X - a.X) * MathF.Max(0, b.Y - a.Y);
                var ox = MathF.Min(b.X, v1.X) - MathF.Max(a.X, v0.X);
                var oy = MathF.Min(b.Y, v1.Y) - MathF.Max(a.Y, v0.Y);
                if (ox > 0.01f && oy > 0.01f)
                    Fail($"gunsmith backdrop at {w}x{h}: a band ({a.X:0},{a.Y:0})-({b.X:0},{b.Y:0}) covers the 3D view");
            }
            var want = size.X * size.Y - (v1.X - v0.X) * (v1.Y - v0.Y);
            if (MathF.Abs(area - want) > want * 1e-4f)
                Fail($"gunsmith backdrop at {w}x{h}: the bands cover {area:0} px^2, the window minus the view is {want:0}");
            Line($"{w}x{h}: loadout window {size.X:0}x{size.Y:0} at ({min.X:0},{min.Y:0}), backdrop bands cover {area / want * 100:0.00}% of it outside the 3D view");
        }
    }

    /// <summary>A fake timestamp backend: stamps take the current fake time; results come back <see cref="Latency"/> frames later.</summary>
    private sealed class FakeStamps : IGpuStamps
    {
        public ulong Now;
        public int Frame, Latency = 2, Reads, EarlyReads;
        private readonly ulong[] _value;
        private readonly int[] _stampedAt;

        public FakeStamps(int count)
        {
            _value = new ulong[count];
            _stampedAt = new int[count];
            Array.Fill(_stampedAt, int.MaxValue);
        }

        public void Stamp(int index)
        {
            _value[index] = Now;
            _stampedAt[index] = Frame;
        }

        public bool Ready(int index) => Frame - _stampedAt[index] >= Latency;

        public ulong Read(int index)
        {
            Reads++;
            if (!Ready(index))
                EarlyReads++;
            return _value[index];
        }
    }

    /// <summary>The prop renderer's GPU timer: per-pass times from timestamps, read once, never early, skipped passes give no sample.</summary>
    private static void CheckGpuStampRing()
    {
        const int ring = 4;
        var fake = new FakeStamps(GpuStampRing.QueryCount(2, ring));
        double sum0 = 0, sum1 = 0;
        int n0 = 0, n1 = 0;
        var timer = new GpuStampRing(2, ring, fake, (pass, ms) =>
        {
            if (pass == 0)
            {
                sum0 += ms;
                n0++;
            }
            else
            {
                sum1 += ms;
                n1++;
            }
        });
        var recorded = 0;
        var animFrames = 0;
        void RunFrame(int f, ulong plainNs, ulong animNs, bool anim)
        {
            fake.Frame = f;
            fake.Now = (ulong)f * 4_000_000UL + 1_000_000_000UL;
            timer.BeginFrame();
            if (timer.Recording)
            {
                recorded++;
                if (anim)
                    animFrames++;
            }
            fake.Now += plainNs;
            timer.EndPass(0);
            if (anim)
            {
                fake.Now += animNs;
                timer.EndPass(1);
            }
            timer.EndFrame();
        }

        // 1. Results two frames late: every frame timed, plain 0.25 ms, animated 0.05 ms on every other frame.
        for (var f = 0; f < 200; f++)
            RunFrame(f, 250_000, 50_000, f % 2 == 0);
        for (var f = 200; f < 210; f++)
        {
            fake.Frame = f;
            timer.Collect();
        }
        if (fake.EarlyReads > 0)
            Fail($"GPU timer: {fake.EarlyReads} results read before they were ready (would stall)");
        if (n0 != recorded || n1 != animFrames)
            Fail($"GPU timer: {n0} plain / {n1} animated samples from {recorded} timed frames ({animFrames} with the animated pass)");
        var avg0 = n0 > 0 ? sum0 / n0 : -1;
        var avg1 = n1 > 0 ? sum1 / n1 : -1;
        if (Math.Abs(avg0 - 0.25) > 1e-9 || Math.Abs(avg1 - 0.05) > 1e-9)
            Fail($"GPU timer: plain {avg0:0.000000} ms (expected 0.25), animated {avg1:0.000000} ms (expected 0.05)");
        var firstRecorded = recorded;

        // 2. A slow GPU (results 6 frames late, more than the ring): frames are skipped, never double counted or read early.
        fake.Latency = 6;
        sum0 = sum1 = 0;
        n0 = n1 = 0;
        recorded = animFrames = 0;
        for (var f = 300; f < 400; f++)
            RunFrame(f, 400_000, 100_000, true);
        for (var f = 400; f < 410; f++)
        {
            fake.Frame = f;
            timer.Collect();
        }
        if (fake.EarlyReads > 0 || n0 != recorded || n1 != recorded || recorded == 0 || recorded >= 100)
            Fail($"GPU timer with a slow GPU: {recorded} of 100 frames timed, {n0}/{n1} samples, {fake.EarlyReads} early reads");
        else if (Math.Abs(sum0 / n0 - 0.4) > 1e-9 || Math.Abs(sum1 / n1 - 0.1) > 1e-9)
            Fail($"GPU timer with a slow GPU: plain {sum0 / n0:0.000000} ms, animated {sum1 / n1:0.000000} ms (expected 0.4 / 0.1)");
        Line($"GPU timer (timestamps): {firstRecorded} of 200 frames timed at 2 frames latency, plain 0.250 / animated 0.050 ms read back exactly; "
             + $"slow GPU: {recorded} of 100 frames timed, no early or double reads");

        // 3. Scene graph stats: a gun build is counted (build frames stay out of the steady-state worst).
        var rig = new GunRig("smoke test gun");
        var def = Weapons.DefaultPrimary;
        var before = GunRig.Builds;
        rig.Ensure(def);
        rig.Ensure(def);
        if (GunRig.Builds != before + 1)
            Fail($"scene graph: {GunRig.Builds - before} gun builds counted for one new weapon and one repeat (expected 1)");
        if (!ScenePresentation.Stats.Contains("build/GC frames", StringComparison.Ordinal))
            Fail("scene graph: the stats line doesn't report build/GC frames apart from the worst");
    }
}
