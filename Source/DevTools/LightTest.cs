using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// <c>--light-test DIR [--only "id,id"]</c> (Karl, 2026-11-24: a lighting test to walk round on his phone): the places in
/// <c>tools/preview/light-test.json</c> as one web page, <c>DIR/index.html</c> (<c>tools/preview/light-test.html</c>), and one
/// <c>DIR/models/ID.json</c> each: the construct's model as the Ship gallery writes it, plus what the game lights it with: every lamp
/// <see cref="ConstructLamps"/> would offer (a lined bay's lamps, ceiling strips, Lamp parts as if switched on: where, colour, range,
/// strength, a spot's cone), the map's sky light (<see cref="SkySettings.Light"/>: the star's direction and colour, the sky and ground
/// ambient) and the views to stand at. The page lights it the way <c>Lighting.Glsl</c>'s mode 5 does (hemisphere ambient scaled by the
/// dark interiors' eight diagonal maps, the star with a shadow map, lamps with <c>DynLight</c>'s falloff, their own shadows and the
/// soft knee). A design aid like the gallery: no materials' patterns, bloom, AO or fog.
/// </summary>
public static partial class ConstructPreview
{
    public static int LightTestCli(string[] args)
    {
        var dir = Flag(args, "--light-test")!;
        var only = Flag(args, "--only")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? page = null, config = null;
        for (var up = new DirectoryInfo(AppContext.BaseDirectory); up != null && page == null; up = up.Parent)
        {
            var at = Path.Combine(up.FullName, "tools", "preview");
            if (File.Exists(Path.Combine(at, "light-test.html")) && File.Exists(Path.Combine(at, "light-test.json")))
                (page, config) = (Path.Combine(at, "light-test.html"), Path.Combine(at, "light-test.json"));
        }
        if (page == null || config == null)
        {
            Console.WriteLine($"--light-test: no tools/preview/light-test.html and .json above {AppContext.BaseDirectory}");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(config), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        Directory.CreateDirectory(Path.Combine(dir, "models"));
        var index = new StringBuilder("{\"places\":[");
        var failed = 0;
        var n = 0;
        foreach (var place in doc.RootElement.GetProperty("places").EnumerateArray())
        {
            var id = place.GetProperty("id").GetString()!;
            var title = place.GetProperty("title").GetString()!;
            var bpName = place.GetProperty("blueprint").GetString()!;
            var mapName = place.GetProperty("map").GetString()!;
            var space = place.TryGetProperty("space", out var sp) && sp.GetBoolean();
            var note = place.TryGetProperty("note", out var nt) ? nt.GetString() ?? "" : "";
            index.Append(n++ > 0 ? "," : "").Append(JsonSerializer.Serialize(new { id, title, note, space, map = mapName }));
            if (only != null && !only.Contains(id))
                continue;
            if (ConstructBuild.FindBlueprint(bpName) is not { } bp)
            {
                Console.WriteLine($"REFUSED light test {id}: no blueprint \"{bpName}\"");
                failed++;
                continue;
            }
            var world = new ConstructWorld();
            var c = ConstructBuild.BuildOnGround(world, bp, Vector3.Zero, 0, out var why);
            if (c == null)
            {
                Console.WriteLine($"REFUSED light test {id}: {bp.Name} doesn't build: {why}");
                failed++;
                continue;
            }
            var map = Maps.Find(mapName);
            if (map == null)
                Console.WriteLine($"light test {id}: no map \"{mapName}\", the classic light instead");
            var model = Model(c, bp, null);
            var extra = new StringBuilder();
            var lamps = LampsOf(c, extra);
            extra.Append(",\"light\":").Append(LightOf(map, space));
            extra.Append(",\"body\":").Append(BodyOf(mapName, place));
            extra.Append(",\"views\":").Append(ViewsOf(c, world, place, id));
            var json = model[..model.LastIndexOf('}')] + extra + "}";
            var file = Path.Combine(dir, "models", id + ".json");
            File.WriteAllText(file, json);
            Console.WriteLine($"light test {id}: {bp.Name}, {c.PartCount} parts, {_tris} triangles, {lamps} lamps, {new FileInfo(file).Length / 1024} KB");
        }
        index.Append("]}");
        File.WriteAllText(Path.Combine(dir, "models", "index.json"), index.ToString());
        File.Copy(page, Path.Combine(dir, "index.html"), true);
        Console.WriteLine($"[light-test] {n} places -> {Path.GetFullPath(dir)}, {failed} refused");
        return failed;
    }

    private static string G(float f) => MathF.Round(f, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static string V(Vector3 v) => $"[{G(v.X)},{G(v.Y)},{G(v.Z)}]";

    /// <summary>
    /// Every lamp the game would offer on <paramref name="c"/> (<see cref="ConstructLamps"/>'s AddLights, at rest, Lamp parts lit) as
    /// <c>"lamps":[[x,y,z, r,g,b, range, strength, dx,dy,dz, cos, kind], ...]</c> in the model's frame: kind 0 a lined bay's lamp (always
    /// lit), 1 a ceiling strip (out with the ship's power), 2 a Lamp part (its switch or the mains); cos -2 all round.
    /// </summary>
    private static int LampsOf(Construct c, StringBuilder sb)
    {
        sb.Append(",\"lamps\":[");
        var count = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var p = c.Parts[i];
            var d = c.DefOf(i);
            c.RestPose(p.Seg, out var q, out var t);
            if (d.Logic == CLogic.Lamp)
            {
                var lc = ConstructLamps.LampColour(c, i, d);
                if (lc != Vector3.Zero)
                    Add(d.LightAt ?? (d.BoundsMin + d.BoundsMax) * 0.5f, lc, 2, d);
                continue;
            }
            foreach (var (at, colour) in d.Lamps)
                Add(at, colour, 0, null);
            if (d.Lamps.Length == 0)
                foreach (var (at, colour) in ConstructLamps.StripLamps(d))
                    Add(at, colour, 1, null);

            void Add(Vector3 local, Vector3 colour, int kind, ConstructPartDef? light)
            {
                var at = Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, local), q) + t;
                var range = light is { LightRange: > 0f } ? light.LightRange : ConstructLamps.Range;
                var strength = ConstructLamps.Strength * (light?.LightStrength ?? 1f);
                var dir = Vector3.Zero;
                var cos = -2f;
                if (light is { LightCone: > 0f })
                {
                    dir = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, light.LightDir), q));
                    cos = MathF.Cos(light.LightCone * (MathF.PI / 180f));
                }
                sb.Append(count++ > 0 ? "," : "").Append('[').Append(G(at.X)).Append(',').Append(G(at.Y)).Append(',').Append(G(at.Z)).Append(',')
                    .Append(G(colour.X)).Append(',').Append(G(colour.Y)).Append(',').Append(G(colour.Z)).Append(',').Append(G(range)).Append(',').Append(G(strength)).Append(',')
                    .Append(G(dir.X)).Append(',').Append(G(dir.Y)).Append(',').Append(G(dir.Z)).Append(',').Append(G(cos)).Append(',').Append(kind).Append(']');
            }
        }
        sb.Append(']');
        return count;
    }

    /// <summary>
    /// The day and night clock (Karl, 2026-11-24: "based on orbit, night and day cycle"): the body the place's map is played on,
    /// from the star system file (radius, GM, its solar day: 0 when locked to its star, so the ground's star never moves) and
    /// the place's <c>"altitude"</c> (m over the ground, in orbit) or <c>"latitude"</c> (degrees, on the ground; 35 when not given).
    /// </summary>
    private static string BodyOf(string mapName, JsonElement place)
    {
        foreach (var e in Space.StarSystems.All)
            foreach (var b in e.System.Bodies)
            {
                if (!string.Equals(b.Map, mapName, StringComparison.OrdinalIgnoreCase))
                    continue;
                // The solar day: the spin against the year (a moon's year is its parent's). Locked to the star: no day at all.
                var yearOf = b;
                while (yearOf.Parent > 0)
                    yearOf = e.System.Bodies[yearOf.Parent];
                var year = yearOf.Parent == 0 ? yearOf.Orbit.Period : 0;
                double solar = 0;
                if (b.RotationPeriod > 0 && year > 0 && Math.Abs(b.RotationPeriod - year) > 1)
                    solar = 1 / (1 / b.RotationPeriod - 1 / year);
                else if (b.RotationPeriod > 0 && year <= 0)
                    solar = b.RotationPeriod;
                var alt = place.TryGetProperty("altitude", out var a) ? a.GetDouble() : 0;
                var lat = place.TryGetProperty("latitude", out var l) ? l.GetDouble() : 35;
                return "{" + $"\"name\":{JsonSerializer.Serialize(b.Name)},\"radius\":{b.Radius.ToString("0", CultureInfo.InvariantCulture)},\"mu\":{b.Mu.ToString("0.####e0", CultureInfo.InvariantCulture)}," +
                    $"\"solarDay\":{Math.Abs(solar).ToString("0", CultureInfo.InvariantCulture)},\"tilt\":{(b.Tilt * 180 / Math.PI).ToString("0.##", CultureInfo.InvariantCulture)}," +
                    $"\"altitude\":{alt.ToString("0", CultureInfo.InvariantCulture)},\"latitude\":{lat.ToString("0.##", CultureInfo.InvariantCulture)}" + "}";
            }
        Console.WriteLine($"light test: no body in a star system is played on map \"{mapName}\": no day and night");
        return "null";
    }

    /// <summary>The map's sky light at its own sun (<see cref="Map.Light"/>), and what the page needs to aim the star elsewhere.</summary>
    private static string LightOf(Map? map, bool space)
    {
        var l = map?.Light ?? Lighting.Default;
        var sky = map?.Skybox;
        var key = sky != null ? sky.SunColor * (sky.SunIntensity * Lighting.DefaultSunStrength) : Lighting.DefaultSunColor * Lighting.DefaultSunStrength;
        var fog = map?.Sky ?? Lighting.DefaultSky;
        return "{" + $"\"sunDir\":{V(l.SunDir)},\"sunColor\":{V(l.SunColor)},\"key\":{V(key)},\"ambSky\":{V(l.AmbSky)},\"ambGround\":{V(l.AmbGround)}," +
            $"\"fog\":{V(fog)},\"skyTint\":{G(Lighting.SkyTint)},\"zenith\":{V(l.Zenith)},\"horizon\":{V(l.Horizon)},\"ground\":{V(l.Ground)}," +
            $"\"roomFloor\":{G(RoomShade.Floor)},\"space\":{(space ? "true" : "false")}" + "}";
    }

    /// <summary>
    /// Where a person would stand to look at the part whose middle is <paramref name="at"/>: the nearest spot round it (rings out to
    /// 3.5 m) with a floor under it within 3 m, a ceiling 1.9 to 6 m over that floor, head room and a clear line to the part's middle.
    /// The eye (1.65 m over the floor) in the model's frame, or null.
    /// </summary>
    private static Vector3? StandNear(Construct c, ConstructWorld world, Vector3 at)
    {
        Vector3 W(Vector3 v) => c.Pos + Vector3.Transform(v, c.Rot);
        Vector3 Wd(Vector3 v) => Vector3.Transform(v, c.Rot);
        float Ray(Vector3 from, Vector3 dir, float far) => world.Raycast(W(from), Wd(dir), far, out var h, only: c) && !h.Glass ? h.Distance : float.PositiveInfinity;
        for (var r = 0.6f; r <= 3.5f; r += 0.35f)
            for (var a = 0; a < 16; a++)
            {
                var ang = a * MathF.PI / 8f;
                var p = at + new Vector3(MathF.Sin(ang) * r, 0.4f, MathF.Cos(ang) * r);
                var down = Ray(p, -Vector3.UnitY, 3f);
                if (float.IsInfinity(down))
                    continue;
                var floor = p.Y - down;
                var eye = new Vector3(p.X, floor + 1.65f, p.Z);
                var up = Ray(new Vector3(p.X, floor + 0.3f, p.Z), Vector3.UnitY, 6f);
                if (up < 1.6f || float.IsInfinity(up))
                    continue;
                var look = at - eye;
                var dist = look.Length();
                if (dist > 0.3f && Ray(eye, look / dist, dist - 0.5f) < dist - 0.5f)
                    continue;
                return eye;
            }
        return null;
    }

    /// <summary>The place's views: <c>[[name, x, y, z, outside, lookX, lookY, lookZ], ...]</c>, a part's middle, a point or the middle of the whole (outside: 1).</summary>
    private static string ViewsOf(Construct c, ConstructWorld world, JsonElement place, string id)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        var centres = new List<(string Name, Vector3 At)>();
        for (var i = 0; i < c.PartCount; i++)
        {
            var p = c.Parts[i];
            var d = c.DefOf(i);
            c.RestPose(p.Seg, out var q, out var t);
            var a = Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, d.BoundsMin), q) + t;
            var b = Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, d.BoundsMax), q) + t;
            lo = Vector3.Min(lo, Vector3.Min(a, b));
            hi = Vector3.Max(hi, Vector3.Max(a, b));
            centres.Add((d.Name, (a + b) * 0.5f));
        }
        var sb = new StringBuilder("[");
        var k = 0;
        foreach (var v in place.GetProperty("views").EnumerateArray())
        {
            var name = v.GetProperty("name").GetString()!;
            Vector3 at;
            var outside = v.TryGetProperty("outside", out var o) && o.GetBoolean();
            if (outside || v.TryGetProperty("at", out var c0) && c0.ValueKind == JsonValueKind.String)
                at = (lo + hi) * 0.5f;
            else if (v.TryGetProperty("at", out var arr) && arr.ValueKind == JsonValueKind.Array)
                at = new Vector3(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle());
            else
            {
                var part = v.GetProperty("part").GetString()!;
                var nth = v.TryGetProperty("nth", out var nn) ? nn.GetInt32() : 0;
                var hits = centres.Where(x => x.Name.Contains(part, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0)
                {
                    Console.WriteLine($"light test {id}: view \"{name}\": no part named like \"{part}\", left out");
                    continue;
                }
                at = hits[Math.Min(nth, hits.Count - 1)].At;
                if (StandNear(c, world, at) is { } stand)
                {
                    sb.Append(k++ > 0 ? "," : "").Append('[').Append(JsonSerializer.Serialize(name)).Append(',').Append(G(stand.X)).Append(',').Append(G(stand.Y)).Append(',').Append(G(stand.Z))
                        .Append(",0,").Append(G(at.X)).Append(',').Append(G(at.Y)).Append(',').Append(G(at.Z)).Append(']');
                    continue;
                }
                Console.WriteLine($"light test {id}: view \"{name}\": no floor with a ceiling near {part}, standing at its middle");
            }
            sb.Append(k++ > 0 ? "," : "").Append('[').Append(JsonSerializer.Serialize(name)).Append(',').Append(G(at.X)).Append(',').Append(G(at.Y)).Append(',').Append(G(at.Z)).Append(',').Append(outside ? 1 : 0).Append(']');
        }
        return sb.Append(']').ToString();
    }
}
