using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a power's ship is for (faction-controllers.md 3.2; the role lists name placeholder blueprints, Karl's answer 4).</summary>
public enum ShipRole : byte
{
    Cargo, // carries a convoy's cargo: the convoy's flagship
    Escort, // guards a convoy
    Raider, // raids
    Warship, // patrols, defends, answers calls
    Count,
}

/// <summary>A place a power holds or uses on the open world (a depot, a yard, a camp): from its plan, or picked by its controller (phase 3's waypoints).</summary>
public struct PlaceSpec
{
    public string Name;
    public string World; // the open world's key ("" or "*": any)
    public Vector3 At;
}

/// <summary>
/// A power's plan (Crew/Controllers.json "kinds", or a power file's "plan"): its stock to start, income, caps, its fleet (blueprints by role), which
/// missions it runs and with how many ships, its places. Read once.
/// </summary>
public sealed class PowerPlan
{
    public float Start = 3000, Income = 20; // units; units a minute for each place it still holds
    public int Ships = 8, Groups = 3, Places = 1; // caps; places it wants on an open world (picked when the plan names none there)
    public bool CanFall = true; // the user's answer 5: with no ship, no place and no stock for a ship it is gone for good
    public readonly string[][] Fleet = [[], [], [], []];
    public bool Convoy, Raid, Defend, Patrol;
    public int ConvoyCargo = 1, ConvoyEscorts = 2, RaidShips = 3, DefendShips = 2, PatrolShips = 2;
    public PlaceSpec[] PlaceList = [];

    public bool Fields => Array.Exists(Fleet, f => f.Length > 0);

    public string[] Of(ShipRole r) => Fleet[(int)r];
}

/// <summary>
/// The controllers' numbers (Crew/Controllers.json, "call-of-slop-npc-controllers/1"; built-in when missing or refused, logged): how often a controller
/// thinks, the world's caps on its bodies, where it lays its places, what ships cost, the missions' numbers, and a plan per kind (a power file's
/// "plan" overrides its kind's key by key). A ship's cost is its blueprint's real bill: every part's Assembling recipe, its materials and the parts it
/// takes, down to the raw units (the user's answer 6 asks for real factories later; until then the bill is paid from the power's stock).
/// </summary>
public sealed class ControllerRules
{
    public const string Format = "call-of-slop-npc-controllers/1";

    public float ThinkSeconds = 5;
    public int MaxBodies = 24; // the world's cap on controllers' ship bodies at once (the user's 1,000 is the LOD phase's: notes)
    public float SpawnFromPeople = 600, DockFromPeople = 400, DockRange = 150; // m
    public float PlaceNear = 1200, PlaceFar = 2200; // m from the first person: a power's places on an open world its plan names none on
    public float CostPerUnit = 1, BuildSecondsScale = 0.05f, MinBuildSeconds = 8, RepairShare = 1;
    public float ConvoyEveryMinutes = 4, CargoValue = 900, RaidSight = 3000, RaidLoot = 0.5f, CallRange = 5000, DefendMinutes = 4, QuietSeconds = 40;
    public float GatherMinutes = 4, RunMinutes = 12, PatrolMinutes = 8, RazeSeconds = 180, RazeRange = 300, DwellSeconds = 8;
    public readonly Dictionary<string, PowerPlan> Kinds = new(StringComparer.OrdinalIgnoreCase);

    private static ControllerRules? _current;
    public static ControllerRules Current => _current ??= Load(Path.Combine(AppFolders.Data, "Crew", "Controllers.json"));

    /// <summary>A tool: rules from another file, from now on in this run.</summary>
    public static void Use(ControllerRules r) => _current = r;

    private readonly Dictionary<byte, PowerPlan> _plans = new();

    /// <summary>Power <paramref name="id"/>'s plan: its file's "plan" over its kind's (made once; a plan that doesn't read is its kind's, logged).</summary>
    public PowerPlan PlanOf(byte id)
    {
        if (_plans.TryGetValue(id, out var p))
            return p;
        var def = CrewPowers.Of(id);
        var kind = def != null && Kinds.TryGetValue(def.Kind.Name, out var k) ? k : new PowerPlan { Start = 0, Income = 0 }; // (a kind with no plan has no economy: it fields nothing, trades nothing)
        p = kind;
        if (def != null && def.PlanText.Length > 0)
            try
            {
                using var doc = JsonDocument.Parse(def.PlanText);
                p = ReadPlan(doc.RootElement, kind);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or FormatException)
            {
                Console.WriteLine($"[powers] power {def.Name}: its plan doesn't read ({ex.Message}): its kind's");
            }
        _plans[id] = p;
        return p;
    }

    // ---- What a ship costs: its blueprint's bill -----------------------------------------------------------------------------------

    public readonly record struct Bill(float Units, float Seconds, int Parts);

    private readonly Dictionary<string, Bill> _bills = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, (float Units, float Seconds)> _partBills = new();

    /// <summary>A blueprint's bill (made once a blueprint, never in a tick: the controllers make every plan's bills when they start).</summary>
    public Bill BillOf(ConstructBuild.Blueprint bp)
    {
        if (_bills.TryGetValue(bp.Name, out var b))
            return b;
        float units = 0, secs = 0;
        foreach (var step in bp.Steps)
        {
            var d = ConstructParts.Find(step.Part);
            var (u, s) = d != null ? PartBill(d, 0) : (2f, 1f);
            units += u;
            secs += s;
        }
        b = new Bill(MathF.Max(1, units * CostPerUnit), MathF.Max(MinBuildSeconds, secs * BuildSecondsScale), bp.Steps.Count);
        _bills[bp.Name] = b;
        return b;
    }

    private (float, float) PartBill(ConstructPartDef d, int depth)
    {
        if (_partBills.TryGetValue(d.Id, out var had))
            return had;
        float units = 2, secs = 1; // (no recipe: raw stock and a little work)
        if (AssemblingTable.Current.Of((ushort)d.Id) is { } r && depth < 4)
        {
            units = 0;
            secs = r.Seconds / Math.Max(1, r.Count);
            foreach (var (_, n) in r.Materials)
                units += n / (float)Math.Max(1, r.Count);
            foreach (var (part, n) in r.Parts)
            {
                var (u, s) = PartBill(part, depth + 1);
                units += u * n / Math.Max(1, r.Count);
                secs += s * n / Math.Max(1, r.Count);
            }
        }
        _partBills[d.Id] = (units, secs);
        return (units, secs);
    }

    // ---- Reading ----------------------------------------------------------------------------------------------------------------------

    public static ControllerRules Load(string path)
    {
        var r = new ControllerRules();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[powers] no {Path.GetFileName(path)}: no power fields ships (controllers think, but have no plan)");
            return r;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new InvalidDataException($"its format isn't \"{Format}\"");
            if (root.TryGetProperty("think", out var th))
                r.ThinkSeconds = Num(th, "seconds", r.ThinkSeconds, 0.5f, 600);
            if (root.TryGetProperty("world", out var w))
            {
                r.MaxBodies = (int)Num(w, "maxBodies", r.MaxBodies, 0, 1000);
                r.SpawnFromPeople = Num(w, "spawnFromPeople", r.SpawnFromPeople, 0, 1e5f);
                r.DockFromPeople = Num(w, "dockFromPeople", r.DockFromPeople, 0, 1e5f);
                r.DockRange = Num(w, "dockRange", r.DockRange, 10, 1e4f);
            }
            if (root.TryGetProperty("places", out var pl))
            {
                r.PlaceNear = Num(pl, "near", r.PlaceNear, 50, 1e6f);
                r.PlaceFar = Num(pl, "far", r.PlaceFar, r.PlaceNear, 1e6f);
                r.RazeSeconds = Num(pl, "razeSeconds", r.RazeSeconds, 1, 36000);
                r.RazeRange = Num(pl, "razeRange", r.RazeRange, 10, 1e5f);
            }
            if (root.TryGetProperty("cost", out var c))
            {
                r.CostPerUnit = Num(c, "perUnit", r.CostPerUnit, 0.001f, 1000);
                r.BuildSecondsScale = Num(c, "buildSecondsScale", r.BuildSecondsScale, 0, 100);
                r.MinBuildSeconds = Num(c, "minBuildSeconds", r.MinBuildSeconds, 0, 36000);
                r.RepairShare = Num(c, "repairShare", r.RepairShare, 0, 10);
            }
            if (root.TryGetProperty("missions", out var m))
            {
                r.ConvoyEveryMinutes = Num(m, "convoyEveryMinutes", r.ConvoyEveryMinutes, 0, 1440);
                r.CargoValue = Num(m, "cargoValue", r.CargoValue, 0, 1e7f);
                r.RaidSight = Num(m, "raidSight", r.RaidSight, 0, 1e6f);
                r.RaidLoot = Num(m, "raidLoot", r.RaidLoot, 0, 1);
                r.CallRange = Num(m, "callRange", r.CallRange, 0, 1e7f);
                r.DefendMinutes = Num(m, "defendMinutes", r.DefendMinutes, 0.1f, 1440);
                r.QuietSeconds = Num(m, "quietSeconds", r.QuietSeconds, 1, 3600);
                r.GatherMinutes = Num(m, "gatherMinutes", r.GatherMinutes, 0.1f, 1440);
                r.RunMinutes = Num(m, "runMinutes", r.RunMinutes, 0.1f, 1440);
                r.PatrolMinutes = Num(m, "patrolMinutes", r.PatrolMinutes, 0.1f, 1440);
                r.DwellSeconds = Num(m, "dwellSeconds", r.DwellSeconds, 0, 600);
            }
            if (root.TryGetProperty("kinds", out var ks))
                foreach (var k in ks.EnumerateObject())
                {
                    if (CrewFactions.Find(k.Name) == 0)
                        Console.WriteLine($"[powers] {path}: \"{k.Name}\" is no kind in Crew/Factions: read, never used");
                    r.Kinds[k.Name] = ReadPlan(k.Value, new PowerPlan());
                }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            Console.WriteLine($"[powers] REFUSED {path}: {ex.Message}; no power fields ships");
            return new ControllerRules();
        }
        return r;
    }

    private static readonly string[] RoleKeys = ["cargo", "escort", "raider", "warship"];

    /// <summary>A plan over <paramref name="b"/>: each key it names replaces b's.</summary>
    public static PowerPlan ReadPlan(JsonElement e, PowerPlan b)
    {
        var p = new PowerPlan
        {
            Start = Num(e, "start", b.Start, 0, 1e9f),
            Income = Num(e, "income", b.Income, 0, 1e7f),
            Ships = (int)Num(e, "ships", b.Ships, 0, PowerControllers.MaxShips),
            Groups = (int)Num(e, "groups", b.Groups, 0, PowerControllers.MaxMissions),
            Places = (int)Num(e, "places", b.Places, 0, PowerControllers.MaxPlaces),
            CanFall = e.TryGetProperty("canFall", out var cf) ? cf.GetBoolean() : b.CanFall,
            ConvoyCargo = b.ConvoyCargo,
            ConvoyEscorts = b.ConvoyEscorts,
            RaidShips = b.RaidShips,
            DefendShips = b.DefendShips,
            PatrolShips = b.PatrolShips,
            Convoy = b.Convoy,
            Raid = b.Raid,
            Defend = b.Defend,
            Patrol = b.Patrol,
            PlaceList = b.PlaceList,
        };
        for (var i = 0; i < p.Fleet.Length; i++)
            p.Fleet[i] = b.Fleet[i];
        if (e.TryGetProperty("fleet", out var fl))
            foreach (var kv in fl.EnumerateObject())
            {
                var i = Array.IndexOf(RoleKeys, kv.Name.ToLowerInvariant());
                if (i < 0)
                    throw new InvalidDataException($"fleet: \"{kv.Name}\" is no role (cargo, escort, raider, warship)");
                var names = kv.Value.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
                foreach (var n in names)
                    if (ConstructBuild.FindBlueprint(n) == null)
                        Console.WriteLine($"[powers] fleet {kv.Name}: no blueprint \"{n}\" (it is skipped when ships are made)");
                p.Fleet[i] = names;
            }
        if (e.TryGetProperty("missions", out var ms))
        {
            p.Convoy = p.Raid = p.Defend = p.Patrol = false;
            foreach (var x in ms.EnumerateObject())
            {
                var on = x.Value.ValueKind != JsonValueKind.False;
                var n = x.Value.ValueKind == JsonValueKind.Object ? x.Value : default;
                switch (x.Name.ToLowerInvariant())
                {
                    case "convoy":
                        p.Convoy = on;
                        if (n.ValueKind == JsonValueKind.Object)
                        {
                            p.ConvoyCargo = (int)Num(n, "cargo", p.ConvoyCargo, 1, CrewGroup.MaxShips);
                            p.ConvoyEscorts = (int)Num(n, "escorts", p.ConvoyEscorts, 0, CrewGroup.MaxShips - 1);
                        }
                        break;
                    case "raid":
                        p.Raid = on;
                        if (n.ValueKind == JsonValueKind.Object)
                            p.RaidShips = (int)Num(n, "ships", p.RaidShips, 1, CrewGroup.MaxShips);
                        break;
                    case "defend":
                        p.Defend = on;
                        if (n.ValueKind == JsonValueKind.Object)
                            p.DefendShips = (int)Num(n, "ships", p.DefendShips, 1, CrewGroup.MaxShips);
                        break;
                    case "patrol":
                        p.Patrol = on;
                        if (n.ValueKind == JsonValueKind.Object)
                            p.PatrolShips = (int)Num(n, "ships", p.PatrolShips, 1, CrewGroup.MaxShips);
                        break;
                    default:
                        throw new InvalidDataException($"missions: \"{x.Name}\" is no mission a controller runs (convoy, raid, defend, patrol)");
                }
            }
        }
        if (e.TryGetProperty("placeList", out var pls))
            p.PlaceList = pls.EnumerateArray().Select(x =>
            {
                var at = x.GetProperty("at");
                return new PlaceSpec
                {
                    Name = x.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                    World = x.TryGetProperty("world", out var wd) ? wd.GetString() ?? "" : "",
                    At = new Vector3(at[0].GetSingle(), at[1].GetSingle(), at[2].GetSingle()),
                };
            }).Take(PowerControllers.MaxPlaces).ToArray();
        return p;
    }

    private static float Num(JsonElement e, string key, float def, float min, float max)
    {
        if (!e.TryGetProperty(key, out var v))
            return def;
        var x = v.GetSingle();
        if (!float.IsFinite(x) || x < min || x > max)
            throw new InvalidDataException($"\"{key}\" {x} is outside {min} to {max}");
        return x;
    }
}
