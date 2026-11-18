using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A kind of key site (a spaceport, a refinery, a mine, an outpost): how much control it gives, its garrison, what it earns.</summary>
public sealed class SiteKindDef
{
    public string Name = "";
    public float Weight = 1, Garrison = 3, Income = 5;
}

/// <summary>A key site on a planet (Crew/Territory.json): where it is (latitude and longitude on a planetoid, else x and z) and who holds it at the start.</summary>
public sealed class SiteDef
{
    public string Name = "", Planet = "", Holder = "";
    public int Kind;
    public Vector2 At;
}

/// <summary>
/// Territory and the far layer's numbers (Crew/Territory.json, "call-of-slop-npc-territory/1"; none when the file is missing: no planet is held, the far
/// layer has nothing to do). Karl's answer 1 (2026-11-17): a planet belongs to the power with the majority of its control; each key site gives control by
/// its kind's weight shared among the sites of that kind on that planet (one spaceport counts for all of a spaceport's weight, one of fifty for a fiftieth).
/// The far layer's numbers: how groups nobody sees move, fight and take sites, and when they become bodies near a person (section 8).
/// </summary>
public sealed class TerritoryRules
{
    public const string Format = "call-of-slop-npc-territory/1";

    public readonly List<SiteKindDef> Kinds = new();
    public readonly List<string> Planets = new();
    public readonly List<SiteDef> Sites = new();
    public float Majority = 0.5f; // a holder has more than this share of a planet's control; nobody has: contested

    // The far layer (section 8).
    public int GroupsPerTick = 8; // far groups stepped each tick, in turn
    public float MaterialiseRange = 5000, CollapseRange = 7000, CollapseSeconds = 60; // m from the nearest person; s nobody within the collapse range
    public float SpawnFromPeople = 600; // a materialised ship is set down at least this far from every person
    public float GroundSpeed = 12, SpaceSpeed = 3000; // m/s: a group on a planet, between planets (on the space clock)
    public float TransitSeconds = 600, LandSeconds = 90; // between planets when their distance isn't known; from landing to a site on a closed world
    public float SiteReach = 800; // m: a group this near a site is at it
    public float FightRate = 0.01f, FightCap = 0.02f; // strength lost a second for each of the enemy's; at most this share of its own a second
    public float TakeSeconds = 120; // a site with no garrison left and no defender is taken after this long
    public float RegenPerMinute = 0.1f; // a held site's garrison grows back by this share of its full one a minute, with no enemy at it
    public float TakeEveryMinutes = 3, HoldMinutes = 10, GatherMinutes = 3;
    public int TakeShips = 3, FarGroups = 3; // a power's Take group, and its far groups at most

    private static TerritoryRules? _current;
    public static TerritoryRules Current => _current ??= Load(Path.Combine(AppFolders.Data, "Crew", "Territory.json"));

    /// <summary>A tool: rules of its own, from now on in this run.</summary>
    public static void Use(TerritoryRules r) => _current = r;

    /// <summary>A copy whose numbers a test may change (the lists are shared).</summary>
    public TerritoryRules Copy() => (TerritoryRules)MemberwiseClone();

    public int KindOf(string name) => Kinds.FindIndex(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public int PlanetOf(string name) => Planets.FindIndex(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static TerritoryRules Load(string path)
    {
        var r = new TerritoryRules();
        if (!File.Exists(path))
            return r;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new InvalidDataException($"not {Format}");
            float F(JsonElement e, string key, float d) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : d;
            int I(JsonElement e, string key, int d) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;
            if (root.TryGetProperty("kinds", out var kinds))
                foreach (var kv in kinds.EnumerateObject())
                    r.Kinds.Add(new SiteKindDef { Name = kv.Name, Weight = MathF.Max(0.01f, F(kv.Value, "weight", 1)), Garrison = MathF.Max(0, F(kv.Value, "garrison", 3)), Income = MathF.Max(0, F(kv.Value, "income", 5)) });
            r.Majority = Math.Clamp(F(root, "majority", 0.5f), 0.3f, 0.95f);
            if (root.TryGetProperty("far", out var far))
            {
                r.GroupsPerTick = Math.Clamp(I(far, "groupsPerTick", r.GroupsPerTick), 1, 64);
                r.MaterialiseRange = F(far, "materialise", r.MaterialiseRange);
                r.CollapseRange = MathF.Max(r.MaterialiseRange + 500, F(far, "collapse", r.CollapseRange));
                r.CollapseSeconds = F(far, "collapseSeconds", r.CollapseSeconds);
                r.SpawnFromPeople = F(far, "spawnFromPeople", r.SpawnFromPeople);
                r.GroundSpeed = MathF.Max(1, F(far, "groundSpeed", r.GroundSpeed));
                r.SpaceSpeed = MathF.Max(1, F(far, "spaceSpeed", r.SpaceSpeed));
                r.TransitSeconds = F(far, "transitSeconds", r.TransitSeconds);
                r.LandSeconds = F(far, "landSeconds", r.LandSeconds);
                r.SiteReach = F(far, "siteReach", r.SiteReach);
                r.FightRate = Math.Clamp(F(far, "fightRate", r.FightRate), 0, 1);
                r.FightCap = Math.Clamp(F(far, "fightCap", r.FightCap), 0.001f, 1);
                r.TakeSeconds = F(far, "takeSeconds", r.TakeSeconds);
                r.RegenPerMinute = F(far, "regenPerMinute", r.RegenPerMinute);
                r.TakeEveryMinutes = F(far, "takeEveryMinutes", r.TakeEveryMinutes);
                r.HoldMinutes = F(far, "holdMinutes", r.HoldMinutes);
                r.GatherMinutes = F(far, "gatherMinutes", r.GatherMinutes);
                r.TakeShips = Math.Clamp(I(far, "takeShips", r.TakeShips), 1, CrewGroup.MaxShips);
                r.FarGroups = Math.Clamp(I(far, "farGroups", r.FarGroups), 0, 16);
            }
            if (root.TryGetProperty("planets", out var planets))
                foreach (var pl in planets.EnumerateObject())
                {
                    if (r.Planets.Count >= PowerControllers.MaxPlanets)
                    {
                        Console.WriteLine($"[territory] {path}: more than {PowerControllers.MaxPlanets} planets: {pl.Name} and after left out");
                        break;
                    }
                    r.Planets.Add(pl.Name);
                    foreach (var s in pl.Value.EnumerateArray())
                    {
                        var kind = r.KindOf(s.GetProperty("kind").GetString() ?? "");
                        var name = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (kind < 0 || r.Sites.Count >= PowerControllers.MaxSites)
                        {
                            Console.WriteLine($"[territory] {path}: site \"{name}\" on {pl.Name} refused: {(kind < 0 ? "no such kind" : $"more than {PowerControllers.MaxSites} sites")}");
                            continue;
                        }
                        var at = s.GetProperty("at");
                        r.Sites.Add(new SiteDef
                        {
                            Name = name.Length > 0 ? name : $"{pl.Name} {r.Kinds[kind].Name} {r.Sites.Count + 1}",
                            Planet = pl.Name,
                            Kind = kind,
                            At = new Vector2(at[0].GetSingle(), at[1].GetSingle()),
                            Holder = s.TryGetProperty("holder", out var h) ? h.GetString() ?? "" : "",
                        });
                    }
                }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException)
        {
            Console.WriteLine($"[territory] {path} refused ({ex.Message}): no planet is held");
            return new TerritoryRules();
        }
        return r;
    }
}
