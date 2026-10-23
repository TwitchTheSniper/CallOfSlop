using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>A minimal map file with <paramref name="skyKey"/> (e.g. "\"skybox\": \"dusk\",") in it, as the Sky Test map's folder.</summary>
    private static Map SkyProbe(string skyKey, string source = "Dev/SkyProbe.json") => MapFile.Load($$"""
        { "name": "Sky Probe", "halfX": 20, "halfZ": 20, {{skyKey}} "ops": [
          { "op": "Ground", "color": [0.4, 0.4, 0.4] },
          { "op": "Spawn", "x": 5, "z": 5 } ] }
        """, source);

    /// <summary>
    /// Skies (Source/Render/Sky): presets, the "skybox" section (round trip and readable errors), the sun from the time of
    /// day, fog matching the sky's horizon, old maps keeping their look and fingerprint, the image skies and their
    /// decoders (with the Brutal.Texture HDR canary), cloud coverage, and the shader sources.
    /// </summary>
    private static void CheckSky()
    {
        SkyNotes.Clear();
        // ---- Presets ---------------------------------------------------------------------------------------------
        foreach (var name in SkySettings.PresetNames)
        {
            var s = SkySettings.FromPreset(name);
            var l = s.Light();
            var json = SkyFile.ToJson(s);
            var back = SkyProbe($"\"skybox\": {json},").Skybox;
            var colours = new[] { s.Zenith, s.Horizon, s.Ground, s.SunColor, s.MoonColor, s.CloudColor, s.CloudShade, s.HazeColor };
            if (colours.Any(c => c.X is < 0 or > 1 || c.Y is < 0 or > 1 || c.Z is < 0 or > 1))
                Fail($"preset {name}: a colour is outside 0-1");
            if (MathF.Abs(s.SunDir.Length() - 1) > 1e-4f || !float.IsFinite(l.SunColor.X + l.AmbSky.Y + l.AmbGround.Z) || l.AmbSky.Y <= 0.1f)
                Fail($"preset {name}: bad light (sun {s.SunDir}, ambient {l.AmbSky})");
            if (back == null || SkyFile.ToJson(back) != json || back.Preset != s.Preset)
                Fail($"preset {name}: written as {json} but didn't read back the same");
            SkyLine($"preset {name,-8}: {s.Describe()}; key light {l.SunColor.X:0.00}/{l.SunColor.Y:0.00}/{l.SunColor.Z:0.00}, sky ambient {l.AmbSky.Y:0.00}; file: {json}");
        }
        foreach (var (alias, key) in new[] { ("clear day", "clear"), ("Desert Haze", "desert"), ("DUSK", "dusk"), ("sunset", "dusk") })
            if (SkySettings.PresetKey(alias) != key)
                Fail($"preset name \"{alias}\" should mean {key}");
        if (SkyProbe("\"skybox\": { \"preset\": \"desert haze\" },").Skybox?.Preset != "desert")
            Fail("\"preset\": \"desert haze\" didn't load the desert preset");
        var night = SkySettings.FromPreset("night");
        var nl = night.Light();
        if (night.SunDir.Y >= 0 || Vector3.Dot(nl.SunDir, night.MoonDir) < 0.99f || nl.SunColor.Length() > 0.5f || nl.SunColor.Length() < 0.05f)
            Fail($"night: the key light should be the dim moon (sun {night.SunDir}, key {nl.SunDir} {nl.SunColor})");
        if (Vector3.Distance(SkySettings.FromPreset("clear").SunDir, Lighting.DefaultSunDirection) > 1e-5f)
            Fail("the clear preset should keep the classic sun direction");

        // ---- Map JSON round trip -----------------------------------------------------------------------------------
        var custom = SkySettings.FromPreset("dusk");
        custom.SunDirection = null;
        custom.TimeOfDay = 7.25f;
        custom.Latitude = -33.5f;
        custom.DayOfYear = 300;
        custom.Heading = 15;
        custom.SunSize = 3.1f;
        custom.SunGlow = 1.2f;
        custom.SunDisc = false;
        custom.Moon = true;
        custom.MoonDirection = new Vector3(0.2f, 0.7f, -0.4f);
        custom.MoonSize = 4;
        custom.Stars = 0.6f;
        custom.CloudCoverage = 0.55f;
        custom.CloudHeading = 200;
        custom.CloudAltitude = 900;
        custom.CloudShade = new Vector3(0.3f, 0.25f, 0.4f);
        custom.HazeStrength = 1.1f;
        custom.FogFromSky = false;
        custom.FogMax = 0.7f;
        custom.Ambient = 0.75f;
        custom.Zenith = new Vector3(0.1f, 0.2f, 0.35f);
        var customJson = SkyFile.ToJson(custom);
        var reread = SkyProbe($"\"skybox\": {customJson},").Skybox!;
        var same = SkyFile.ToJson(reread) == customJson && reread.SunDirection == null && reread.TimeOfDay == 7.25f && reread.Latitude == -33.5f
                   && reread.Moon && reread.MoonDirection == custom.MoonDirection && reread.CloudCoverage == 0.55f && !reread.FogFromSky && !reread.SunDisc
                   && reread.Zenith == custom.Zenith && reread.Horizon == custom.Horizon && Vector3.Distance(reread.SunDir, custom.SunDir) < 1e-6f;
        if (!same)
            Fail($"a sky with every setting changed didn't survive a write and read: {customJson}");
        var mapsWithSky = Maps.All.Where(m => m.Skybox != null).ToList();
        foreach (var m in mapsWithSky)
        {
            var copy = MapFile.Load(MapFile.ToJson(m), m.SourceFile ?? m.Name);
            if (copy.Skybox == null || SkyFile.ToJson(copy.Skybox) != SkyFile.ToJson(m.Skybox!) || copy.Sky != m.Sky)
                Fail($"{m.Name}: its sky didn't survive writing the map out and reading it back ({SkyFile.ToJson(m.Skybox!)} became {(copy.Skybox is { } cs ? SkyFile.ToJson(cs) : "none")}, sky {m.Sky} / {copy.Sky})");
        }
        SkyLine($"round trip: a sky with every setting changed, and {mapsWithSky.Count} map(s) with a sky ({string.Join(", ", mapsWithSky.Select(m => $"{m.Name}: {m.Skybox!.Preset}"))})");
        if (Maps.Find("Sky Test") is not { Skybox: not null } skyTest || skyTest.Pack?.Name != "Dev")
            Fail("the Sky Test map (Maps/Dev/SkyTest.json) should load into the Dev pack with a sky");

        // ---- Bad files give readable errors ------------------------------------------------------------------------
        (string Sky, string Expect)[] bad =
        [
            ("\"skybox\": \"sunrise\",", "unknown preset \"sunrise\" (presets: clear, overcast"),
            ("\"skybox\": 5,", "expected a preset name"),
            ("\"skybox\": { \"preset\": \"dusk\", \"colour\": [1, 0, 0] },", "unknown key \"colour\" (known: preset, type"),
            ("\"skybox\": { \"clouds\": { \"coverage\": 1.5 } },", "clouds: \"coverage\" is a number from 0 to 1, got 1.5"),
            ("\"skybox\": { \"clouds\": { \"cover\": 0.5 } },", "clouds: unknown key \"cover\""),
            ("\"skybox\": { \"sun\": { \"time\": 25 } },", "\"time\" is a number from 0 to 24"),
            ("\"skybox\": { \"sun\": { \"time\": 12, \"direction\": [0, 1, 0] } },", "give the sun's position one way"),
            ("\"skybox\": { \"sun\": { \"elevation\": 20 } },", "\"elevation\" and \"azimuth\" go together"),
            ("\"skybox\": { \"sun\": { \"direction\": [0, 0, 0] } },", "can't be [0, 0, 0]"),
            ("\"skybox\": { \"zenith\": [1.2, 0.5, 0.5] },", "zenith: colours are [r, g, b] with values 0-1"),
            ("\"skybox\": { \"horizon\": \"blue\" },", "horizon: colours are [r, g, b]"),
            ("\"skybox\": { \"type\": \"sphere\" },", "\"type\" is \"procedural\", \"cubemap\" or \"equirect\""),
            ("\"skybox\": { \"type\": \"equirect\" },", "type \"equirect\" needs \"image\""),
            ("\"skybox\": { \"type\": \"cubemap\" },", "type \"cubemap\" needs \"faces\""),
            ("\"skybox\": { \"type\": \"procedural\", \"image\": \"Skies/sample.hdr\" },", "need type \"equirect\" or \"cubemap\""),
            ("\"skybox\": { \"image\": \"Skies/missing.hdr\" },", "image \"Skies/missing.hdr\" not found"),
            ("\"skybox\": { \"image\": \"Skies/sample.jpg\" },", "not found"),
            ("\"skybox\": { \"image\": \"../Legacy/pack.json\" },", "must be a path inside the Maps folder"),
            ("\"skybox\": { \"faces\": [\"a.png\", \"b.png\"] },", "\"faces\" is 6 file names"),
            ("\"skybox\": { \"faces\": \"Skies/sample_{face}.png\", \"image\": \"Skies/sample.hdr\" },", "a cubemap sky has \"faces\", not one \"image\""),
            ("\"skybox\": { \"fog\": { \"max\": 2 } },", "fog: \"max\" is a number from 0 to 1"),
            ("\"skybox\": { \"moon\": 3 },", "moon: expected true, false or an object"),
        ];
        var readable = 0;
        foreach (var (sky, expect) in bad)
        {
            try
            {
                SkyProbe(sky, "Dev/BadSky.json");
                Fail($"{sky} loaded without an error");
            }
            catch (MapFileException ex)
            {
                if (!ex.Message.Contains(expect) || !ex.Message.StartsWith("Dev/BadSky.json: skybox"))
                    Fail($"{sky} gave \"{ex.Message}\" (expected it to name the file, the skybox and say: {expect})");
                else
                    readable++;
            }
        }
        SkyLine($"{readable} of {bad.Length} broken skybox sections rejected with a readable message, e.g. \"{TryLoadError(bad[3].Sky)}\"");

        // ---- The sun from the time of day ------------------------------------------------------------------------
        (string What, Vector3 Got, Vector3 Want)[] suns =
        [
            ("equator, noon, equinox: overhead", SkySettings.SunFromTime(12, 0, 80, 0), Vector3.UnitY),
            ("45N, noon, equinox: 45 deg up in the south (+Z)", SkySettings.SunFromTime(12, 45, 80, 0), Vector3.Normalize(new Vector3(0, 1, 1))),
            ("45S, noon, equinox: 45 deg up in the north (-Z)", SkySettings.SunFromTime(12, -45, 80, 0), Vector3.Normalize(new Vector3(0, 1, -1))),
            ("equator, 06:00, equinox: rising due east (+X)", SkySettings.SunFromTime(6, 0, 80, 0), Vector3.UnitX),
            ("45N, 18:00, equinox: setting due west (-X)", SkySettings.SunFromTime(18, 45, 80, 0), -Vector3.UnitX),
            ("45N, midnight, equinox: 45 deg below the north horizon", SkySettings.SunFromTime(0, 45, 80, 0), Vector3.Normalize(new Vector3(0, -1, -1))),
            ("45N, noon, heading 90: south turns to -X", SkySettings.SunFromTime(12, 45, 80, 90), Vector3.Normalize(new Vector3(-1, 1, 0))),
        ];
        foreach (var (what, got, want) in suns)
            if (Vector3.Distance(got, want) > 0.012f) // the equinox declination isn't exactly 0 on day 80
                Fail($"sun {what}: got ({got.X:0.000}, {got.Y:0.000}, {got.Z:0.000})");
        var solstice = MathF.Asin(SkySettings.SunFromTime(12, 45, 172, 0).Y) * 180 / MathF.PI;
        if (MathF.Abs(solstice - 68.44f) > 0.3f)
            Fail($"sun at 45N, noon, June solstice: {solstice:0.00} deg up (expected 68.44)");
        var angles = SkyProbe("\"skybox\": { \"sun\": { \"elevation\": 30, \"azimuth\": 90 } },").Skybox!.SunDir;
        if (Vector3.Distance(angles, new Vector3(MathF.Cos(MathF.PI / 6), 0.5f, 0)) > 1e-4f)
            Fail($"sun elevation 30 azimuth 90 should be 30 deg up in the east, got {angles}");
        var dusk = SkySettings.FromPreset("dusk");
        SkyLine($"sun from the time of day: {suns.Length} positions and the June solstice ({solstice:0.00} deg at 45N) right; dusk preset sun {dusk.SunElevation:0.0} deg up in the west");
        if (dusk.SunElevation is < 1 or > 10 || dusk.SunDir.X > -0.9f)
            Fail("the dusk sun should be low in the west");

        // ---- Fog and the sky's horizon ---------------------------------------------------------------------------
        var worstSeam = 0f;
        foreach (var name in SkySettings.PresetNames)
        {
            var s = SkySettings.FromPreset(name);
            var sun = s.SunDir;
            var away = Vector3.Normalize(new Vector3(-sun.X, 0, -sun.Z) + new Vector3(1e-4f, 0, 0));
            var l = s.Light();
            if (Vector3.Distance(s.Gradient(away), s.Horizon) > 1e-5f || l.Horizon != s.Horizon || !l.SkyFog || l.Zenith != s.Zenith || l.Ground != s.Ground)
                Fail($"preset {name}: the fog colour at the horizon isn't the sky's horizon colour");
            foreach (var dir in new[] { away, Vector3.Normalize(new Vector3(sun.X, 0, sun.Z) + new Vector3(1e-4f, 0, 0)), Vector3.UnitX, -Vector3.UnitZ })
            {
                var up = s.Gradient(Vector3.Normalize(dir + new Vector3(0, 0.002f, 0)));
                var down = s.Gradient(Vector3.Normalize(dir - new Vector3(0, 0.002f, 0)));
                worstSeam = MathF.Max(worstSeam, Vector3.Distance(up, down));
            }
            if (Vector3.Distance(s.Gradient(Vector3.UnitY), s.Zenith) > 0.02f && s.HazeNow == 0)
                Fail($"preset {name}: straight up isn't the zenith colour");
        }
        if (worstSeam > 0.06f)
            Fail($"the sky gradient jumps {worstSeam:0.000} across the horizon (fog would show a seam)");
        if (Maps.Find("Sky Test") is { Skybox: { } st } stMap && stMap.Sky != st.Horizon)
            Fail("a map with a sky must use its horizon colour as its fog / background colour");
        // The GLSL gradient must be the same maths as SkySettings.Gradient.
        string[] gradientTerms = ["sqrt(min(y, 1.0))", "smoothstep(0.0, 0.3, -y)", "pow(s, 6.0)", "(1.0 - 0.6 * min(abs(y), 1.0))", "uSkyFog > 0.5 ? SkyGradient("];
        foreach (var term in gradientTerms)
            if (!Lighting.Glsl.Contains(term))
                Fail($"Lighting.Glsl lost \"{term}\" (it must match SkySettings.Gradient)");
        SkyLine($"fog meets every preset's sky at the horizon exactly; worst step across the horizon {worstSeam:0.0000}");

        // ---- Old maps keep their look and fingerprint --------------------------------------------------------------
        var d = Lighting.Default;
        if (d.SunDir != Vector3.Normalize(new Vector3(-0.4f, 0.8f, -0.3f)) || d.SunColor != new Vector3(1.0f, 0.95f, 0.86f) * 0.55f || d.AmbSky != new Vector3(0.50f, 0.52f, 0.56f)
            || d.AmbGround != new Vector3(0.40f, 0.38f, 0.34f) || d.FogMax != 0.85f || d.SkyFog)
            Fail("Lighting.Default must stay the classic light (maps without a sky must look as they always did)");
        var plain = 0;
        foreach (var m in Maps.All.Where(m => m.SourceFile != null && m.Skybox == null))
        {
            plain++;
            var text = File.ReadAllText(Path.Combine(Maps.Folder, m.SourceFile!));
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var want = doc.RootElement.TryGetProperty("sky"u8, out var se) ? MapFile.ToColour(se, "sky", m.Name) : Lighting.DefaultSky;
            if (m.Sky != want)
                Fail($"{m.Name}: no skybox, so its sky colour should be its \"sky\" {want}, got {m.Sky}");
            var json = MapFile.ToJson(m);
            if (json.Contains("\"skybox\"") || !json.Contains("\"sky\""))
                Fail($"{m.Name}: a map without a sky must write \"sky\" and no \"skybox\"");
        }
        // A sky never changes the fingerprint: the High Ground maps (Legacy 31-36) with their old flat colours instead hash the same.
        (string File, string OldSky)[] old =
        [
            ("Legacy/31-GravelPit.json", "#c8c3b4"), ("Legacy/32-Hillfort.json", "#a9b7c0"), ("Legacy/33-Riverbend.json", "#9fb8cc"),
            ("Legacy/34-AdobeMesa.json", "#e0c9a6"), ("Legacy/35-Ridgeline.json", "#a8bccb"), ("Legacy/36-AlpinePass.json", "#c3d3e2"),
        ];
        var hashesKept = 0;
        foreach (var (file, oldSky) in old)
        {
            var path = Path.Combine(Maps.Folder, file);
            if (!File.Exists(path))
                continue;
            var text = File.ReadAllText(path);
            var withSky = MapFile.Load(text, file);
            var flat = MapFile.Load(Regex.Replace(text, "\"skybox\": [^\\n]*\\n", $"\"sky\": \"{oldSky}\",\n"), file);
            if (flat.Skybox != null || withSky.ContentHash != flat.ContentHash)
                Fail($"{file}: adding a sky changed the map's fingerprint ({withSky.ContentHash:x8} vs {flat.ContentHash:x8})");
            else
                hashesKept++;
        }
        var flatProbe = SkyProbe("\"sky\": [0.2, 0.3, 0.4],");
        if (flatProbe.Skybox != null || flatProbe.Sky != new Vector3(0.2f, 0.3f, 0.4f) || flatProbe.ContentHash != SkyProbe("\"skybox\": \"night\",").ContentHash)
            Fail("a sky must not change the fingerprint, and \"sky\" alone must stay a flat colour");
        SkyLine($"{plain} maps without a sky keep their flat colour and the classic light; {hashesKept} High Ground maps hash the same with their sky as with their old colour");

        // ---- Image skies and decoders ------------------------------------------------------------------------------
        var stb = MeshTextures.StbAvailable;
        var hdrStb = SkyImages.HdrCanary;
        SkyLine($"canary Brutal.Texture/stb (PNG): {(stb ? "PASS" : "FALLBACK")} - {MeshTextures.CanaryReport}");
        SkyLine($"canary Brutal.Texture/stb (HDR): {(hdrStb ? "PASS" : "FALLBACK")} - {SkyImages.HdrCanaryReport}");
        var (tw, th, trgb) = SkyImages.DecodeHdr(SkyImages.TinyHdr());
        if (tw != 5 || th != 3 || MathF.Abs(trgb[0] - 40f / 1024f) > 1e-7f || MathF.Abs(trgb[1] - 200f / 1024f) > 1e-7f)
            Fail($"the built-in RGBE reader got the test image wrong ({tw}x{th}, first pixel {trgb[0]}, {trgb[1]})");
        try
        {
            var cube = SkyProbe("\"skybox\": { \"faces\": \"Skies/sample_{face}.png\" },").Skybox!;
            var pano = SkyProbe("\"skybox\": { \"image\": \"Skies/sample.hdr\", \"sun\": { \"time\": 17.3 } },").Skybox!;
            var ci = cube.Loaded!;
            var pi = pano.Loaded!;
            SkyLine($"sample skies: cubemap 6x {ci.Width}x{ci.Height} ({ci.Decoder}), panorama {pi.Width}x{pi.Height} HDR {pi.Hdr} ({pi.Decoder}); "
                 + $"measured horizon {Fmt(cube.Horizon)} / {Fmt(pano.Horizon)}, zenith {Fmt(cube.Zenith)} / {Fmt(pano.Zenith)}");
            if (cube.Type != SkyType.Cubemap || pano.Type != SkyType.Equirect || ci.Width != ci.Height || pi.Width != pi.Height * 2 || !pi.Hdr)
                Fail("the sample skies loaded as the wrong kind");
            // Both files hold the same sky, so both direction mappings must agree (orientation, face order, flips).
            var sampleSun = new Vector3(MathF.Cos(11 * MathF.PI / 180) * MathF.Sin(250 * MathF.PI / 180), MathF.Sin(11 * MathF.PI / 180), -MathF.Cos(11 * MathF.PI / 180) * MathF.Cos(250 * MathF.PI / 180));
            var diffs = new List<float>();
            var rng = new Random(5);
            for (var i = 0; i < 600; i++)
            {
                var dir = Vector3.Normalize(new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 1.6f - 0.3f, (float)rng.NextDouble() * 2 - 1));
                if (Vector3.Dot(dir, sampleSun) > 0.995f)
                    continue; // the sun disc is a few texels: skip it
                diffs.Add(Vector3.Distance(ci.Sample(dir), pi.Display(pi.Sample(dir), 1f)));
            }
            diffs.Sort();
            var p90 = diffs[(int)(diffs.Count * 0.9)];
            var mean = (Vector3.Distance(cube.Horizon, pano.Horizon) + Vector3.Distance(cube.Zenith, pano.Zenith)) / 2;
            SkyLine($"cubemap vs panorama in {diffs.Count} directions: median difference {diffs[diffs.Count / 2]:0.000}, 90th percentile {p90:0.000}, measured colours differ {mean:0.000}");
            if (p90 > 0.08f || mean > 0.05f)
                Fail("the cubemap and the panorama of the same sky disagree: a direction mapping is wrong");
            if (cube.Zenith.Z <= cube.Zenith.X || cube.Zenith.Z < 0.3f)
                Fail("the sample sky's measured zenith should be blue");
            if (hdrStb)
            {
                var raw = File.ReadAllBytes(Path.Combine(Maps.Folder, "Dev", "Skies", "sample.hdr"));
                var (bw, bh, brgb) = SkyImages.DecodeHdr(raw);
                var (sw, sh, srgb, _) = SkyImages.LoadHdr(raw, "sample.hdr");
                var diff = 0f;
                for (var i = 0; i < Math.Min(brgb.Length, srgb.Length); i++)
                    diff = MathF.Max(diff, MathF.Abs(brgb[i] - srgb[i]));
                if (bw != sw || bh != sh || diff > 1e-4f)
                    Fail($"stb and the built-in reader disagree on the run-length encoded sample.hdr (max difference {diff})");
            }
        }
        catch (MapFileException ex)
        {
            Fail($"the sample image skies didn't load: {ex.Message}");
        }

        // ---- Clouds -----------------------------------------------------------------------------------------------
        var noise = SkyRenderer.Noise;
        var cov = new[] { 0.2f, 0.5f, 0.8f }.Select(c => (c, SkyRenderer.MeasuredCoverage(noise, c))).ToArray();
        SkyLine("cloud coverage asked vs drawn: " + string.Join(", ", cov.Select(c => $"{c.c:0.0} -> {c.Item2:0.00}")));
        if (cov.Any(c => MathF.Abs(c.c - c.Item2) > 0.12f) || SkyRenderer.MeasuredCoverage(noise, 0) > 0)
            Fail("cloud coverage doesn't mean what it says (see SkyRenderer.CoverageThreshold): " + string.Join(", ", cov.Select(c => $"{c.c:0.0} -> {c.Item2:0.00}")));
        var w0 = SkyRenderer.WindOffsets(SkySettings.FromPreset("clear"), 1e6);
        if (!float.IsFinite(w0.X) || w0.X is < 0 or >= 1 || w0.W is < 0 or >= 1)
            Fail("cloud drift offsets must stay wrapped to 0-1 after a long session");

        // ---- Shader sources ----------------------------------------------------------------------------------------
        // CI has no GPU, so the GLSL can't be compiled here; check the sources are complete and agree with the C#.
        var fs = SkyRenderer.FragmentSource;
        var declared = Regex.Matches(fs, @"uniform\s+\w+\s+(\w+)\s*;").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(SkyRenderer.VertexSource, @"uniform\s+\w+\s+(\w+)\s*;").Select(m => m.Groups[1].Value)).ToHashSet();
        var expected = SkyRenderer.UniformNames.Concat(Lighting.UniformNames).ToHashSet();
        if (!declared.SetEquals(expected))
            Fail($"sky shader uniforms and the C# lists disagree: declared only {string.Join(",", declared.Except(expected))}; expected only {string.Join(",", expected.Except(declared))}");
        foreach (var name in SkyRenderer.UniformNames.Concat(Lighting.UniformNames))
            if (name != "uInvVP" && !Regex.IsMatch(fs[(fs.IndexOf("void main()", StringComparison.Ordinal) is var mi and >= 0 ? mi : 0)..], $@"\b{name}\b")
                && !Regex.IsMatch(Lighting.Glsl[Lighting.Glsl.IndexOf("vec3 ApplyLight", StringComparison.Ordinal)..], $@"\b{name}\b"))
                Fail($"sky uniform {name} is declared but never used");
        foreach (var (what, src) in new[] { ("sky vertex", SkyRenderer.VertexSource), ("sky fragment", fs) })
        {
            if (!src.StartsWith("#version 330 core") || !src.Contains("void main()") || src.Any(c => c > 126 || (c < 32 && c != '\n' && c != '\r' && c != '\t'))
                || src.Count(c => c == '{') != src.Count(c => c == '}') || src.Count(c => c == '(') != src.Count(c => c == ')'))
                Fail($"{what} shader source is malformed (version line, main, ASCII, balanced brackets)");
        }
        SkyLine($"sky shaders: {declared.Count} uniforms declared, all in the C# lists and used; brackets balanced (compiled with Mesa llvmpipe during development)");

        // ---- Settings ------------------------------------------------------------------------------------------------
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-sky-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(tmp, "fov=90\n");
            var oldSave = Settings.Load(tmp);
            var lowSet = new Settings { SkyQuality = SkyQuality.Low };
            lowSet.Save(tmp);
            if (oldSave.SkyQuality != SkyQuality.High || Settings.Load(tmp).SkyQuality != SkyQuality.Low)
                Fail("Sky quality: old settings files should default to High, and Low should save and load");
        }
        finally
        {
            File.Delete(tmp);
        }
        // The section's results as one GitHub Actions notice (the workflow prints selftest.log; "::notice" lines become annotations).
        Line("::notice title=Sky::" + string.Join("%0A", SkyNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    private static readonly List<string> SkyNotes = new();

    private static void SkyLine(string s)
    {
        Line(s);
        SkyNotes.Add(s);
    }

    private static string TryLoadError(string sky)
    {
        try
        {
            SkyProbe(sky, "Dev/BadSky.json");
            return "(no error)";
        }
        catch (MapFileException ex)
        {
            return ex.Message;
        }
    }

    private static string Fmt(Vector3 c) => $"({c.X:0.00}, {c.Y:0.00}, {c.Z:0.00})";
}
