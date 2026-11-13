using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The Ministry of Slop's buildings round the penal colony (docs/story/OPENING.md "The Ministry's buildings"): the records
/// office, canteen, infirmary, requisitions store and gatehouse across a street in front of the colony, watchtowers at its
/// back corners and lamp posts down the street. Where each stands is data (Story/Opening/ColonyBuildings.json: a spot in
/// the colony's own frame and a turn); the blueprints are the part kit's (PartKit/Generators/BuildingParts.Ministry.cs).
/// The server builds them with the colony on a fresh story world, each on its spot when the ground there is clear and
/// level enough or the nearest such spot within the file's slack, and keeps which construct is which in the save. A
/// building someone takes down stays down; a world from before the buildings gets them when it is next loaded.
/// </summary>
public sealed partial class Game
{
    private readonly List<(string Name, ushort Body)> _cbBuilt = [];

    /// <summary>Both sides, a new match (from <see cref="OpeningReset"/>): no buildings until the server builds or loads them.</summary>
    private void ColonyBuildingsReset()
    {
        _cbBuilt.Clear();
        _csBench = false;
        _csLift = 0;
    }

    /// <summary>
    /// Server: the construct of a colony building by its blueprint's name (any case), or null when there is none. The
    /// colony itself and the guards' barracks answer to their blueprints' names too ("Penal Colony", "Colony Barracks").
    /// For the lore's items, the staff's posts and the like: local spots in the building are its blueprint's metres.
    /// </summary>
    internal Construct? ColonyBuilding(string blueprint)
    {
        if (blueprint.Equals(OpeningScript.Current.Colony, StringComparison.OrdinalIgnoreCase))
            return Constructs.Get(_opColony);
        if (blueprint.Equals("Colony Barracks", StringComparison.OrdinalIgnoreCase))
            return Constructs.Get(_opBarracks);
        foreach (var (name, body) in _cbBuilt)
            if (name.Equals(blueprint, StringComparison.OrdinalIgnoreCase))
                return Constructs.Get(body);
        return null;
    }

    /// <summary>Server, the colony just built (a fresh story world, or a world from before the buildings): every building on its spot.</summary>
    private void ColonyBuildingsBuild()
    {
        var script = ColonyBuildingsScript.Current;
        if (script.Problem.Length > 0)
        {
            Log($"[colony] {script.Problem}: no Ministry buildings");
            return;
        }
        if (Constructs.Get(_opColony) is not { } colony)
            return;
        var n = 0;
        var all = 0;
        foreach (var b in script.Buildings)
        {
            if (b.Site || b.Elsewhere)
                continue; // (the site's own pieces: ColonySiteFurnish; the barracks and the Remote Works: their own files, through ColonySiteStand)
            all++;
            if (ColonyBuildingPlace(colony, b) is not { } c)
            {
                Log($"[colony] no clear, level ground for the {b.Blueprint} within {script.Slack:0} m of its spot");
                continue;
            }
            _cbBuilt.Add((b.Blueprint, c.Id));
            Constructs.Events.Add($"opening: the {b.Blueprint} as construct {c.Id}");
            n++;
        }
        Log($"[colony] {n} of {all} Ministry buildings round the colony");
    }

    /// <summary>One building: built turned as the file says, then stood on its spot or the nearest clear, level one (rings 2 m apart).</summary>
    private Construct? ColonyBuildingPlace(Construct colony, ColonyBuildingsScript.Building b)
    {
        if (ConstructBuild.FindBlueprint(b.Blueprint) is not { } bp)
            return null;
        var spot = colony.ToWorld(new Vector3(b.At.X, 0, b.At.Y));
        var ground = ConstructWorld.Ground(World.Query, spot.X, spot.Z);
        var c = ConstructBuild.BuildOnGround(Constructs, bp, ground, b.Yaw * MathF.PI / 180f, out _);
        if (c == null)
            return null;
        var mid = c.ToWorld((c.LocalMin + c.LocalMax) * 0.5f);
        var slack = ColonyBuildingsScript.Current.Slack;
        for (var r = 0f; r <= slack; r += 2f)
            for (int k = 0, ks = r == 0 ? 1 : 12; k < ks; k++)
            {
                var a = k * MathF.Tau / ks;
                var at = spot + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * r;
                var move = new Vector3(at.X - mid.X, 0, at.Z - mid.Z);
                if (!ColonyBuildingSpotClear(c, move, b.Level))
                    continue;
                var g = ConstructWorld.Ground(World.Query, at.X, at.Z);
                c.Pos += move with { Y = g.Y - ground.Y };
                c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
                c.NetHistReset();
                if (r > 0)
                    Log($"[colony] the {b.Blueprint} stands {r:0} m off its spot (the spot is taken or not level)");
                return c;
            }
        Constructs.Remove(c);
        return null;
    }

    /// <summary>Would <paramref name="self"/> moved by <paramref name="move"/> stand in the map, clear of every other body's
    /// footprint (0.75 m between), on ground whose corners and middle vary under <paramref name="level"/> m?</summary>
    private bool ColonyBuildingSpotClear(Construct self, Vector3 move, float level)
    {
        var (lo, hi) = ColonyFootprint(self);
        lo += new Vector2(move.X, move.Z);
        hi += new Vector2(move.X, move.Z);
        var map = World.Map;
        if (lo.X < -map.HalfX + 2 || hi.X > map.HalfX - 2 || lo.Y < -map.HalfZ + 2 || hi.Y > map.HalfZ - 2)
            return false;
        const float Gap = 0.75f;
        foreach (var o in Constructs.Bodies)
        {
            if (o == self)
                continue;
            var (olo, ohi) = ColonyFootprint(o);
            if (olo.X < hi.X + Gap && ohi.X > lo.X - Gap && olo.Y < hi.Y + Gap && ohi.Y > lo.Y - Gap)
                return false;
        }
        var low = float.MaxValue;
        var high = float.MinValue;
        Span<Vector2> probes = [(lo + hi) * 0.5f, lo, hi, new(lo.X, hi.Y), new(hi.X, lo.Y)];
        foreach (var p in probes)
        {
            var y = ConstructWorld.Ground(World.Query, p.X, p.Y).Y;
            low = MathF.Min(low, y);
            high = MathF.Max(high, y);
        }
        return high - low < level;
    }

    /// <summary>A body's footprint on the ground (world x and z), from its rest bounds' corners.</summary>
    private static (Vector2 Lo, Vector2 Hi) ColonyFootprint(Construct c)
    {
        var lo = new Vector2(float.MaxValue);
        var hi = new Vector2(float.MinValue);
        for (var k = 0; k < 4; k++)
        {
            var w = c.ToWorld(new Vector3((k & 1) == 0 ? c.LocalMin.X : c.LocalMax.X, (c.LocalMin.Y + c.LocalMax.Y) * 0.5f, (k & 2) == 0 ? c.LocalMin.Z : c.LocalMax.Z));
            lo = Vector2.Min(lo, new Vector2(w.X, w.Z));
            hi = Vector2.Max(hi, new Vector2(w.X, w.Z));
        }
        return (lo, hi);
    }

    /// <summary>Server, taking a save: which construct is which building.</summary>
    private (string, ushort)[] ColonyBuildingsCapture() => [.. _cbBuilt];

    /// <summary>Server, loading a world with a colony: its buildings (one taken down stays down), or new ones round it when
    /// the save is from before them (<paramref name="listed"/> false).</summary>
    private void ColonyBuildingsLoad((string Name, ushort Body)[] saved, bool listed)
    {
        _cbBuilt.Clear();
        foreach (var (name, body) in saved)
            if (Constructs.Get(body) != null || body == 0 && name == SpaceportMark) // (the mark that the spaceport was built: GameSpaceport.cs)
                _cbBuilt.Add((name, body));
        if (!listed && _opColony != 0)
            ColonyBuildingsBuild();
    }
}

/// <summary>Story/Opening/ColonyBuildings.json: the Ministry's buildings round the colony. A bad file is refused with a reason and none are built.</summary>
public sealed class ColonyBuildingsScript
{
    public const string Format = "call-of-slop-colony-buildings/1";
    public const int MaxBuildings = 64;

    /// <summary>A building: its blueprint, where its middle stands in the colony's frame (x, z m), its turn (degrees) and how level its ground must be (m).
    /// <see cref="Site"/>: one of the site's own pieces, laid at exactly its spot in the colony's plane on a graded bench,
    /// <see cref="Sink"/> m into it (Game/GameColonySite.cs). <see cref="Elsewhere"/>: a building another file builds, whose
    /// spot the plan fixes (the Colony Barracks, the Remote Works).</summary>
    public readonly record struct Building(string Blueprint, Vector2 At, float Yaw, float Level, bool Site = false, float Sink = 0, bool Elsewhere = false);

    /// <summary>The bench: the rectangle of the colony's frame (x, z m) graded level on a fresh world; empty (max under min): none.</summary>
    public Vector2 BenchMin, BenchMax;

    /// <summary>The most the land may stand over or under the colony's level anywhere under the bench (m): past it nothing is graded.</summary>
    public float BenchMost = 6;

    /// <summary>The bench's ramps to the land: from a spot on its edge to one out on the land (x, z m in the colony's frame).</summary>
    public (Vector2 From, Vector2 To)[] Ramps = [];

    /// <summary>The works yard: rectangles of the colony's frame (x, z m) where a survival world's start kit stands, inside the fence.</summary>
    public (Vector2 Min, Vector2 Max)[] Kit = [];

    public Building[] Buildings = [];
    public float Slack = 12;
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Story", "Opening", "ColonyBuildings.json");

    private static ColonyBuildingsScript? _current;
    public static ColonyBuildingsScript Current => _current ??= Load(FilePath);

    public static ColonyBuildingsScript Load(string path)
    {
        var t = new ColonyBuildingsScript();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            if (root.TryGetProperty("slack", out var sl))
                t.Slack = Math.Clamp(sl.GetSingle(), 0, 60);
            var list = new List<Building>();
            foreach (var e in root.GetProperty("buildings").EnumerateArray())
            {
                var name = e.GetProperty("blueprint").GetString() ?? "";
                if (ConstructBuild.FindBlueprint(name) == null)
                    throw new FormatException($"no blueprint named \"{name}\"");
                var at = e.GetProperty("at");
                if (at.GetArrayLength() != 2)
                    throw new FormatException($"{name}: \"at\" is [x, z] in metres");
                float x = at[0].GetSingle(), z = at[1].GetSingle();
                if (!float.IsFinite(x + z) || MathF.Abs(x) > 400 || MathF.Abs(z) > 400)
                    throw new FormatException($"{name}: \"at\" within 400 m of the colony");
                var yaw = e.TryGetProperty("yaw", out var y) ? y.GetSingle() : 0;
                var level = e.TryGetProperty("level", out var lv) ? Math.Clamp(lv.GetSingle(), 0.2f, 4f) : 1.2f;
                var site = e.TryGetProperty("site", out var st) && st.GetBoolean();
                var sink = e.TryGetProperty("sink", out var sk) ? Math.Clamp(sk.GetSingle(), -2f, 2f) : 0;
                var elsewhere = e.TryGetProperty("elsewhere", out var ew) && ew.GetBoolean();
                list.Add(new Building(name, new Vector2(x, z), float.IsFinite(yaw) ? yaw : 0, level, site, sink, elsewhere));
            }
            if (list.Count > MaxBuildings)
                throw new FormatException($"{list.Count} buildings (at most {MaxBuildings})");
            t.Buildings = [.. list];
            if (root.TryGetProperty("bench", out var bench))
            {
                static Vector2 V(JsonElement a) => a.GetArrayLength() == 2 && float.IsFinite(a[0].GetSingle() + a[1].GetSingle()) && MathF.Abs(a[0].GetSingle()) <= 400 && MathF.Abs(a[1].GetSingle()) <= 400
                    ? new Vector2(a[0].GetSingle(), a[1].GetSingle()) : throw new FormatException("a bench spot is [x, z] within 400 m of the colony");
                t.BenchMin = V(bench.GetProperty("min"));
                t.BenchMax = V(bench.GetProperty("max"));
                if (t.BenchMax.X - t.BenchMin.X is < 16 or > 160 || t.BenchMax.Y - t.BenchMin.Y is < 16 or > 160)
                    throw new FormatException("the bench is 16 to 160 m each way");
                if (bench.TryGetProperty("most", out var most))
                    t.BenchMost = Math.Clamp(most.GetSingle(), 0.5f, 10f);
                if (bench.TryGetProperty("kit", out var kit))
                {
                    t.Kit = [.. kit.EnumerateArray().Select(r => (V(r.GetProperty("min")), V(r.GetProperty("max"))))];
                    if (t.Kit.Length > 8 || t.Kit.Any(r => r.Max.X <= r.Min.X || r.Max.Y <= r.Min.Y))
                        throw new FormatException("at most 8 kit rectangles, each with its max over its min");
                }
                if (bench.TryGetProperty("ramps", out var ramps))
                {
                    t.Ramps = [.. ramps.EnumerateArray().Select(r => (V(r.GetProperty("from")), V(r.GetProperty("to"))))];
                    if (t.Ramps.Length > 8 || t.Ramps.Any(r => Vector2.Distance(r.From, r.To) > GroundShape.MaxLength))
                        throw new FormatException($"at most 8 ramps, each {GroundShape.MaxLength:0} m long at most");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            t.Problem = $"{Path.GetFileName(path)}: {ex.Message}";
        }
        return t;
    }
}
