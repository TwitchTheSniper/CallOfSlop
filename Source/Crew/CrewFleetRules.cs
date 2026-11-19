using System.Text.Json;

namespace CallOfSlop;

/// <summary>What an admiral's group does when it loses its admiral (faction-controllers.md 6.4, the user's answer 5: by doctrine).</summary>
public enum FleetSuccession : byte
{
    Captain, // the senior captain on the net steps up after the doctrine's delay
    Break, // the group scatters: each ship runs, then its captain is on its own
    Flagship, // only a ship with a command fit (a part that gives access to the fleet net by radio) can carry an admiral; none left: the group goes home
}

/// <summary>A group's shape (faction-controllers.md 6.4).</summary>
public enum FleetFormation : byte
{
    Column,
    Line,
    Wedge,
    Screen,
}

/// <summary>How a power's admirals lead (Crew/Fleet.json "doctrines"; a kind or power names one with "doctrine", else the one named as its kind, else "Standard").</summary>
public sealed class FleetDoctrine
{
    public string Name = "Standard";
    public float Caution = 1f; // the strength ratio (its own over the enemy's) it wants to fight; it pulls back at half of it, in again past it
    public float Discipline = 0.5f; // 0-1: how far its captains' own Flee lines are held off while the admiral holds
    public float Skill = 0.5f; // 0-1: how well it picks a focus target (low: the nearest)
    public FleetFormation Formation = FleetFormation.Wedge;
    public float Spacing = 60; // m between ships in a formation
    public FleetSuccession Succession = FleetSuccession.Captain;
    public float Delay = 10; // s of disarray while command passes (no new orders)
    public float Resistance = 0.5f; // 0-1: how hard its officers are to interrogate (each officer varies about it)

    /// <summary>A copy a player's group may change (its shape and spacing: phase 6) without changing the power's.</summary>
    public FleetDoctrine Copy() => (FleetDoctrine)MemberwiseClone();
}

/// <summary>
/// The fleet command network and groups' numbers (Crew/Fleet.json, "call-of-slop-fleet/1"; built-in values when missing or refused, logged): which
/// parts give a ship access to its group's network by radio and how far each reaches, the cut grace, the admirals' think rates, ransoms and
/// interrogation, and the doctrines by name.
/// </summary>
public sealed class FleetRules
{
    public const string Format = "call-of-slop-fleet/1";

    /// <summary>Parts that put a ship on the net by radio, by name; any part with a radar ("radar": {"range"}) does too.</summary>
    public string[] NetParts = ["Wireless Sender", "Wireless Receiver", "Long Range Sensor", "Wi-Fi Mast", "Router Antenna"];
    public float RadioRange = 25000; // m: a ship with a whole net part reaches its group this far
    public float VoiceRange = 1500; // m: with none, its crew's hand sets (every crew has them) reach this far
    public float CutGraceSeconds = 30; // a ship off the net keeps its last order this long, then its captain is on its own
    public float ThinkNear = 1, ThinkFar = 5; // s between an admiral's thinks with a member awake (a person within 3 km) or none
    public int GroupsPerTick = 8; // group slots looked at a tick (the far layer's fixed cost: 256 groups go round in 32 ticks)
    public long RansomAdmiral = 1500, RansomCaptain = 400; // credits a power pays for its officer back
    public float InterrogateSeconds = 60; // an officer of resistance 0.5 breaks in about this long with an Interrogator at it (alone: InterrogateAlone times)
    public float InterrogateAlone = 3;
    public float IntelSeconds = 300; // positions given up stay shown this long
    public readonly Dictionary<string, FleetDoctrine> Doctrines = new(StringComparer.OrdinalIgnoreCase);

    private static FleetRules? _current;
    public static FleetRules Current => _current ??= Load(Path.Combine(AppFolders.Data, "Crew", "Fleet.json"));

    /// <summary>A power's doctrine: its own, its kind's, the one named as its kind, else Standard.</summary>
    public FleetDoctrine DoctrineOf(byte power)
    {
        var p = CrewPowers.Of(power);
        if (p != null && p.Doctrine.Length > 0 && Doctrines.TryGetValue(p.Doctrine, out var d))
            return d;
        if (p != null && p.Kind.Doctrine.Length > 0 && Doctrines.TryGetValue(p.Kind.Doctrine, out d))
            return d;
        if (p != null && Doctrines.TryGetValue(p.Kind.Name, out d))
            return d;
        return Doctrines.TryGetValue("Standard", out d) ? d : Standard;
    }

    private static readonly FleetDoctrine Standard = new();

    public static FleetRules Load(string path)
    {
        var r = new FleetRules();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[fleet] no {Path.GetFileName(path)}: groups use the built-in numbers and the Standard doctrine");
            return r;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new InvalidDataException($"its format isn't \"{Format}\"");
            if (root.TryGetProperty("net", out var net))
            {
                if (net.TryGetProperty("parts", out var parts))
                    r.NetParts = parts.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray();
                r.RadioRange = Num(net, "radioRange", r.RadioRange, 100, 1e7f);
                r.VoiceRange = Num(net, "voiceRange", r.VoiceRange, 0, 1e6f);
                r.CutGraceSeconds = Num(net, "cutGraceSeconds", r.CutGraceSeconds, 0, 3600);
            }
            if (root.TryGetProperty("think", out var th))
            {
                r.ThinkNear = Num(th, "nearSeconds", r.ThinkNear, 0.2f, 60);
                r.ThinkFar = Num(th, "farSeconds", r.ThinkFar, 0.2f, 600);
                r.GroupsPerTick = (int)Num(th, "groupsPerTick", r.GroupsPerTick, 1, CrewFleet.MaxGroups);
            }
            if (root.TryGetProperty("prisoners", out var pr))
            {
                r.RansomAdmiral = (long)Num(pr, "ransomAdmiral", r.RansomAdmiral, 0, 1e9f);
                r.RansomCaptain = (long)Num(pr, "ransomCaptain", r.RansomCaptain, 0, 1e9f);
                r.InterrogateSeconds = Num(pr, "interrogateSeconds", r.InterrogateSeconds, 1, 3600);
                r.InterrogateAlone = Num(pr, "aloneTimes", r.InterrogateAlone, 1, 100);
                r.IntelSeconds = Num(pr, "intelSeconds", r.IntelSeconds, 1, 36000);
            }
            if (root.TryGetProperty("doctrines", out var ds))
                foreach (var e in ds.EnumerateObject())
                {
                    var d = new FleetDoctrine { Name = e.Name };
                    var v = e.Value;
                    d.Caution = Num(v, "caution", d.Caution, 0.2f, 5);
                    d.Discipline = Num(v, "discipline", d.Discipline, 0, 1);
                    d.Skill = Num(v, "skill", d.Skill, 0, 1);
                    d.Spacing = Num(v, "spacing", d.Spacing, 5, 2000);
                    d.Delay = Num(v, "delaySeconds", d.Delay, 0, 600);
                    d.Resistance = Num(v, "resistance", d.Resistance, 0, 1);
                    if (v.TryGetProperty("formation", out var fo))
                        d.Formation = Enum.TryParse<FleetFormation>(fo.GetString(), true, out var ff) ? ff : throw new InvalidDataException($"doctrine {e.Name}: no formation \"{fo.GetString()}\" (column, line, wedge, screen)");
                    if (v.TryGetProperty("succession", out var su))
                        d.Succession = Enum.TryParse<FleetSuccession>(su.GetString(), true, out var sf) ? sf : throw new InvalidDataException($"doctrine {e.Name}: no succession \"{su.GetString()}\" (captain, break, flagship)");
                    r.Doctrines[e.Name] = d;
                }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            Console.WriteLine($"[fleet] REFUSED {path}: {ex.Message}; groups use the built-in numbers");
            return new FleetRules();
        }
        return r;
    }

    private static float Num(JsonElement e, string key, float def, float min, float max)
    {
        if (!e.TryGetProperty(key, out var v))
            return def;
        var x = v.GetSingle();
        if (x < min || x > max)
            throw new InvalidDataException($"\"{key}\" {x} is outside {min} to {max}");
        return x;
    }
}
