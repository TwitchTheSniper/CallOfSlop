using System.Globalization;
using System.Text.Json;

namespace CallOfSlop.Space;

/// <summary>A star system file that can't be used, with the reason (and the file and body in the message).</summary>
public sealed class StarSystemException(string message) : Exception(message);

/// <summary>
/// Star system files (<c>Systems/*.json</c>, docs/space/SPACE.md "System files"): the star first, then each body after
/// its parent. Unknown keys, impossible orbits and orbits that leave their parent's pull are refused with a reason.
/// </summary>
public static class StarSystemFile
{
    public const string FolderName = "Systems";

    /// <summary>The 1 AU used by "au" (m).</summary>
    public const double Au = 149_597_870_700;

    private static readonly string[] SystemKeys = ["name", "bodies", "description", "start", "galaxy"];
    private static readonly string[] GalaxyKeys = ["ly", "arrive"];
    private static readonly string[] ArriveKeys = ["a", "km", "au"];
    private static readonly string[] StartKeys = ["seconds", "hours", "days"];
    private static readonly string[] BodyKeys = ["name", "parent", "gm", "gravity", "radius", "map", "orbit", "rotation", "description", "color", "lumps"];
    private static readonly string[] OrbitKeys = ["a", "km", "au", "e", "i", "lan", "argp", "m0"];
    private static readonly string[] RotationKeys = ["period", "hours", "locked", "tilt", "angle"];

    /// <summary>Every system file in the game's Systems folder (bad files are skipped and printed).</summary>
    public static List<StarSystem> LoadAll(string? folder = null, List<string>? errors = null)
    {
        folder ??= Path.Combine(AppContext.BaseDirectory, FolderName);
        var list = new List<StarSystem>();
        if (!Directory.Exists(folder))
            return list;
        foreach (var f in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                list.Add(Load(File.ReadAllText(f), Path.GetFileName(f)));
            }
            catch (Exception ex) when (ex is StarSystemException or IOException)
            {
                errors?.Add(ex.Message);
                Console.WriteLine($"[space] system skipped: {ex.Message}");
            }
        }
        return list;
    }

    public static StarSystem Load(string json, string file)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new StarSystemException($"{file}: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new StarSystemException($"{file}: expected an object");
            Keys(root, SystemKeys, file);
            var sys = new StarSystem { File = file, Name = Str(root, "name", file) ?? Path.GetFileNameWithoutExtension(file) };
            if (!root.TryGetProperty("bodies", out var bodies) || bodies.ValueKind != JsonValueKind.Array || bodies.GetArrayLength() == 0)
                throw new StarSystemException($"{file}: \"bodies\" should be a list, the star first");
            var list = new List<SpaceBody>();
            foreach (var b in bodies.EnumerateArray())
                list.Add(ReadBody(b, list, file));
            sys.Bodies = list.ToArray();
            sys.Start = ReadStart(root, file);
            Check(sys);
            ReadGalaxy(root, sys, file);
            return sys;
        }
    }

    /// <summary>The optional <c>"start": { "days": N }</c> (or seconds, hours): a new game's space time, 0 to 100 years.</summary>
    private static SpaceTime ReadStart(JsonElement root, string file)
    {
        if (!root.TryGetProperty("start", out var st))
            return SpaceTime.Epoch;
        var where = file + " start";
        if (st.ValueKind != JsonValueKind.Object)
            throw new StarSystemException($"{where}: should be an object, e.g. {{ \"days\": 250 }}");
        Keys(st, StartKeys, where);
        var s = Num(st, "seconds", where);
        var h = Num(st, "hours", where);
        var d = Num(st, "days", where);
        if ((s != null ? 1 : 0) + (h != null ? 1 : 0) + (d != null ? 1 : 0) != 1)
            throw new StarSystemException($"{where}: give exactly one of \"seconds\", \"hours\" and \"days\"");
        var seconds = s ?? (h != null ? h.Value * 3600 : d!.Value * 86400);
        if (!(seconds >= 0) || seconds > 100 * 365.25 * 86400)
            throw new StarSystemException($"{where}: should be 0 to 100 years");
        return SpaceTime.FromSeconds(seconds);
    }

    /// <summary>
    /// The optional <c>"galaxy"</c> block (phase 6, docs/space/GALAXY.md): where the system sits in the galaxy
    /// (<c>"ly": [x, y, z]</c>, light-years along the ecliptic's axes, which every system shares) and how far from its star
    /// a jump arrives (<c>"arrive": { "au": N }</c>, or "km", "a"; default <see cref="Galaxy.DefaultArrival"/>).
    /// </summary>
    private static void ReadGalaxy(JsonElement root, StarSystem sys, string file)
    {
        sys.Arrival = Galaxy.DefaultArrival(sys);
        if (!root.TryGetProperty("galaxy", out var g))
            return;
        var where = file + " galaxy";
        if (g.ValueKind != JsonValueKind.Object)
            throw new StarSystemException($"{where}: should be an object, e.g. {{ \"ly\": [4.2, -1, 0.3] }}");
        Keys(g, GalaxyKeys, where);
        if (g.TryGetProperty("ly", out var ly))
        {
            if (ly.ValueKind != JsonValueKind.Array || ly.GetArrayLength() != 3
                || ly.EnumerateArray().Any(c => c.ValueKind != JsonValueKind.Number || !double.IsFinite(c.GetDouble()) || Math.Abs(c.GetDouble()) > Galaxy.MaxLy))
                throw new StarSystemException($"{where}: \"ly\" should be [x, y, z] in light-years, each within {Galaxy.MaxLy:0}");
            sys.GalaxyLy = new Vec3d(ly[0].GetDouble(), ly[1].GetDouble(), ly[2].GetDouble());
        }
        if (g.TryGetProperty("arrive", out var ar))
        {
            if (ar.ValueKind != JsonValueKind.Object)
                throw new StarSystemException($"{where}: \"arrive\" should be an object, e.g. {{ \"au\": 2 }}");
            Keys(ar, ArriveKeys, where + " arrive");
            var am = Num(ar, "a", where);
            var km = Num(ar, "km", where);
            var au = Num(ar, "au", where);
            if ((am != null ? 1 : 0) + (km != null ? 1 : 0) + (au != null ? 1 : 0) != 1)
                throw new StarSystemException($"{where} arrive: give the distance from the star once, as \"a\" (m), \"km\" or \"au\"");
            var d = am ?? (km != null ? km.Value * 1000 : au!.Value * Au);
            if (!(d > sys.Bodies[0].Radius * 2) || d > SpacePos.SafeReach * 0.5)
                throw new StarSystemException($"{where} arrive: should be past twice the star's radius ({Km(sys.Bodies[0].Radius * 2)}) and within {SpacePos.SafeReach * 0.5 / Au:0} AU");
            sys.Arrival = d;
        }
    }

    private static SpaceBody ReadBody(JsonElement e, List<SpaceBody> before, string file)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new StarSystemException($"{file}: body {before.Count + 1} should be an object");
        var name = Str(e, "name", file) ?? throw new StarSystemException($"{file}: body {before.Count + 1} needs a \"name\"");
        var where = $"{file}: {name}";
        Keys(e, BodyKeys, where);
        if (before.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new StarSystemException($"{where}: two bodies have this name");
        var body = new SpaceBody { Name = name, Map = Str(e, "map", where) };
        body.Radius = Num(e, "radius", where) ?? throw new StarSystemException($"{where}: needs a \"radius\" (m)");
        if (!(body.Radius > 0))
            throw new StarSystemException($"{where}: radius should be above 0");
        var gm = Num(e, "gm", where);
        var g = Num(e, "gravity", where);
        if ((gm == null) == (g == null))
            throw new StarSystemException($"{where}: give exactly one of \"gm\" (m^3/s^2) and \"gravity\" (at the surface, m/s^2)");
        body.Mu = gm ?? g!.Value * body.Radius * body.Radius;
        if (!(body.Mu > 0) || !double.IsFinite(body.Mu))
            throw new StarSystemException($"{where}: gm/gravity should be above 0");

        var parentName = Str(e, "parent", where);
        if (before.Count == 0)
        {
            if (parentName != null || e.TryGetProperty("orbit", out _))
                throw new StarSystemException($"{where}: the first body is the star: no parent or orbit");
        }
        else
        {
            if (parentName == null)
                throw new StarSystemException($"{where}: needs a \"parent\" (only the first body, the star, has none)");
            body.Parent = before.FindIndex(b => b.Name.Equals(parentName, StringComparison.OrdinalIgnoreCase));
            if (body.Parent < 0)
                throw new StarSystemException($"{where}: parent \"{parentName}\" isn't a body listed before it");
            if (!e.TryGetProperty("orbit", out var orbit) || orbit.ValueKind != JsonValueKind.Object)
                throw new StarSystemException($"{where}: needs an \"orbit\" object");
            body.Orbit = ReadOrbit(orbit, before[body.Parent].Mu, where + " orbit");
        }

        if (e.TryGetProperty("color", out var col))
        {
            if (col.ValueKind != JsonValueKind.Array || col.GetArrayLength() != 3
                || col.EnumerateArray().Any(c => c.ValueKind != JsonValueKind.Number || !(c.GetDouble() is >= 0 and <= 1)))
                throw new StarSystemException($"{where}: \"color\" should be [r, g, b], each 0 to 1");
            body.Color = new System.Numerics.Vector3((float)col[0].GetDouble(), (float)col[1].GetDouble(), (float)col[2].GetDouble());
        }

        if (e.TryGetProperty("lumps", out var lumps))
            body.Lumps = ReadLumps(lumps, body, before.Count == 0, where);

        if (e.TryGetProperty("rotation", out var rot))
        {
            if (rot.ValueKind != JsonValueKind.Object)
                throw new StarSystemException($"{where}: \"rotation\" should be an object");
            Keys(rot, RotationKeys, where + " rotation");
            var period = Num(rot, "period", where);
            var hours = Num(rot, "hours", where);
            var locked = rot.TryGetProperty("locked", out var l) && l.ValueKind == JsonValueKind.True;
            if ((period != null ? 1 : 0) + (hours != null ? 1 : 0) + (locked ? 1 : 0) > 1)
                throw new StarSystemException($"{where} rotation: give one of \"period\" (s), \"hours\" and \"locked\"");
            if (locked && body.Parent < 0)
                throw new StarSystemException($"{where} rotation: the star can't be locked to a parent");
            body.RotationPeriod = locked ? body.Orbit.Period : period ?? (hours ?? 0) * 3600;
            if (!(body.RotationPeriod >= 0) || !double.IsFinite(body.RotationPeriod))
                throw new StarSystemException($"{where} rotation: the period should be 0 (none) or more");
            var tilt = Num(rot, "tilt", where) ?? 0;
            if (!(tilt >= 0 && tilt <= 180))
                throw new StarSystemException($"{where} rotation: tilt is 0 to 180 degrees");
            body.Tilt = Deg(tilt);
            body.RotationAtEpoch = Deg(Num(rot, "angle", where) ?? 0);
        }
        return body;
    }

    /// <summary>The farthest a lump may reach from a body's centre, in its radii (past it, it's another body: give it an orbit).</summary>
    public const double LumpReach = 2;

    private static CallOfSlop.GravityModel ReadLumps(JsonElement e, SpaceBody body, bool star, string where)
    {
        where += " lumps";
        if (star)
            throw new StarSystemException($"{where}: the star can't be lumpy");
        if (e.ValueKind != JsonValueKind.Object)
            throw new StarSystemException($"{where}: should be an object, e.g. {{ \"sources\": [ {{ \"type\": \"mascons\", \"points\": [[0, 0, 0, 3], [400, 0, 0]], \"mu\": 1 }} ] }}");
        CallOfSlop.GravityModel model;
        try
        {
            model = CallOfSlop.MapFile.ReadBodyLumps(e, where, body.Mu);
        }
        catch (CallOfSlop.MapFileException ex)
        {
            throw new StarSystemException(ex.Message);
        }
        foreach (var s in model.Sources)
            if (s.Center.Length + s.Bound > body.Radius * LumpReach)
                throw new StarSystemException($"{where}: {s.Name} reaches {Km(s.Center.Length + s.Bound)} from the centre, past {LumpReach} radii ({Km(body.Radius * LumpReach)})");
        return model;
    }

    private static KeplerOrbit ReadOrbit(JsonElement o, double mu, string where)
    {
        Keys(o, OrbitKeys, where);
        var am = Num(o, "a", where);
        var km = Num(o, "km", where);
        var au = Num(o, "au", where);
        if ((am != null ? 1 : 0) + (km != null ? 1 : 0) + (au != null ? 1 : 0) != 1)
            throw new StarSystemException($"{where}: give the semi-major axis once, as \"a\" (m), \"km\" or \"au\"");
        var a = am ?? (km != null ? km.Value * 1000 : au!.Value * Au);
        if (!(a > 0) || !double.IsFinite(a))
            throw new StarSystemException($"{where}: the semi-major axis should be above 0");
        var e = Num(o, "e", where) ?? 0;
        if (!(e >= 0 && e < 1))
            throw new StarSystemException($"{where}: eccentricity is 0 to below 1 (closed orbits only)");
        var i = Num(o, "i", where) ?? 0;
        if (!(i >= 0 && i <= 180))
            throw new StarSystemException($"{where}: inclination is 0 to 180 degrees");
        return new KeplerOrbit(mu, a, e, Deg(i), Deg(Num(o, "lan", where) ?? 0), Deg(Num(o, "argp", where) ?? 0), Deg(Num(o, "m0", where) ?? 0));
    }

    /// <summary>Checks that need the whole system: depth, orbits clear of the parent, inside its pull, inside the position range.</summary>
    private static void Check(StarSystem sys)
    {
        for (var i = 1; i < sys.Count; i++)
        {
            var b = sys.Bodies[i];
            var where = $"{sys.File}: {b.Name}";
            var parent = sys.Bodies[b.Parent];
            if (sys.Depth(i) > StarSystem.MaxDepth)
                throw new StarSystemException($"{where}: more than {StarSystem.MaxDepth} parents deep");
            if (b.Orbit.Periapsis <= parent.Radius + b.Radius)
                throw new StarSystemException($"{where}: the orbit dips into {parent.Name} (periapsis {Km(b.Orbit.Periapsis)} from its centre, the two radii are {Km(parent.Radius + b.Radius)})");
            if (b.Mu >= parent.Mu)
                throw new StarSystemException($"{where}: heavier than its parent {parent.Name} (gm {b.Mu:g4} against {parent.Mu:g4})");
            if (parent.Parent >= 0)
            {
                var hill = sys.HillRadius(b.Parent);
                if (b.Orbit.Apoapsis >= hill * 0.5)
                    throw new StarSystemException($"{where}: the orbit goes past half {parent.Name}'s Hill radius ({Km(b.Orbit.Apoapsis)} against {Km(hill)}): {parent.Name} couldn't hold it");
            }
            var reach = 0.0;
            for (var c = i; sys.Bodies[c].Parent >= 0; c = sys.Bodies[c].Parent)
                reach += sys.Bodies[c].Orbit.Apoapsis;
            if (reach > SpacePos.SafeReach)
                throw new StarSystemException($"{where}: too far from the star ({reach / Au:0} AU; a system reaches {SpacePos.SafeReach / Au:0} AU)");
        }
        for (var i = 1; i < sys.Count; i++)
            for (var j = i + 1; j < sys.Count; j++)
            {
                var a = sys.Bodies[i];
                var b = sys.Bodies[j];
                if (a.Parent != b.Parent)
                    continue;
                // Siblings' orbits may not cross: rails don't collide.
                if (a.Orbit.Periapsis <= b.Orbit.Apoapsis + a.Radius + b.Radius && b.Orbit.Periapsis <= a.Orbit.Apoapsis + a.Radius + b.Radius)
                    throw new StarSystemException($"{sys.File}: {a.Name} and {b.Name} have orbits that overlap round {sys.Bodies[a.Parent].Name} ({Km(a.Orbit.Periapsis)}-{Km(a.Orbit.Apoapsis)} and {Km(b.Orbit.Periapsis)}-{Km(b.Orbit.Apoapsis)})");
            }
    }

    private static string Km(double m) => m >= 1e9 ? $"{m / Au:0.###} AU" : $"{m / 1000:0.#} km";

    private static double Deg(double d) => d * Math.PI / 180;

    private static void Keys(JsonElement e, string[] allowed, string where)
    {
        foreach (var p in e.EnumerateObject())
            if (!allowed.Contains(p.Name))
                throw new StarSystemException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", allowed)})");
    }

    private static string? Str(JsonElement e, string key, string where)
    {
        if (!e.TryGetProperty(key, out var v))
            return null;
        if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
            throw new StarSystemException($"{where}: \"{key}\" should be a name");
        return v.GetString()!.Trim();
    }

    private static double? Num(JsonElement e, string key, string where)
    {
        if (!e.TryGetProperty(key, out var v))
            return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d))
            throw new StarSystemException($"{where}: \"{key}\" should be a number");
        return d;
    }

    /// <summary>A short description of a system (for --space-sim and the log).</summary>
    public static string Describe(StarSystem sys)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{sys.Name} ({sys.File}): {sys.Count} bodies");
        for (var i = 0; i < sys.Count; i++)
        {
            var b = sys.Bodies[i];
            sb.Append('\n').Append(new string(' ', 2 + 2 * sys.Depth(i)));
            sb.Append(CultureInfo.InvariantCulture, $"{b.Name}: radius {Km(b.Radius)}, surface gravity {b.SurfaceGravity:0.##} m/s2");
            if (b.Parent >= 0)
                sb.Append(CultureInfo.InvariantCulture, $", orbit {Km(b.Orbit.Periapsis)} to {Km(b.Orbit.Apoapsis)}, period {Span(b.Orbit.Period)}");
            if (b.RotationPeriod > 0)
                sb.Append(CultureInfo.InvariantCulture, $", day {Span(b.RotationPeriod)}");
            if (b.Map != null)
                sb.Append(CultureInfo.InvariantCulture, $", map \"{b.Map}\"");
        }
        return sb.ToString();
    }

    public static string Span(double s) => s >= 86400 * 2 ? FormattableString.Invariant($"{s / 86400:0.##} days") : s >= 7200 ? FormattableString.Invariant($"{s / 3600:0.##} h") : FormattableString.Invariant($"{s / 60:0.#} min");
}
