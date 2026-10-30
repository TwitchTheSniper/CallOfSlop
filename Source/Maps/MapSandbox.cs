using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A construct a sandbox map starts with: a blueprint (Constructs/Blueprints) standing at a point, facing a way.</summary>
public sealed class SandboxSpawn
{
    public string Blueprint = "";
    public float X, Z, Yaw; // yaw in degrees in the file, radians here
}

/// <summary>A loose part lying on a sandbox map at the start.</summary>
public sealed class SandboxLoose
{
    public string Part = "";
    public float X, Z, Yaw;
}

/// <summary>
/// A map's "sandbox" section (the Sandbox mode, docs/sandbox/CONSTRUCTS.md): the parts rack (where players take parts),
/// the parts it hands out, the constructs the map starts with and the loose parts lying about. Only the server reads it
/// (clients get every construct from the server), so it is not part of the map's fingerprint.
/// </summary>
public sealed class SandboxMap
{
    public float RackX, RackZ, RackYaw;
    public List<string> RackParts = new();
    public List<SandboxSpawn> Constructs = new();
    public List<SandboxLoose> Loose = new();

    /// <summary>
    /// The debug parts menu (the inventory key in Sandbox: any part into your hands, any blueprint spawned in front of
    /// you; UiSandboxParts.cs) is allowed here. On by default; <c>"partsMenu": false</c> turns it off (a survival map).
    /// </summary>
    public bool PartsMenu = true;

    /// <summary>
    /// Endless parts (milestone 3): the rack and the debug parts menu hand out parts from nothing, as before. Off
    /// (<c>"infiniteParts": false</c>), they hand out only what the player's inventory holds, and taking a part off your own
    /// construct puts it back there. On by default (Construct Yard).
    /// </summary>
    public bool InfiniteParts = true;

    /// <summary>Wrecks and broken-off parts are cleared away after this many seconds with nobody near or salvaging them (0: never).</summary>
    public float DebrisSeconds = DefaultDebrisSeconds;

    /// <summary>How near (metres) a player keeps a wreck from being cleared away.</summary>
    public float DebrisNear = DefaultDebrisNear;

    public const float DefaultDebrisSeconds = 180f;
    public const float DefaultDebrisNear = 12f;

    public Vector3 Rack => new(RackX, 0, RackZ);

    /// <summary>Reads the section. Part and blueprint names must exist (a missing one is a refusal with the reason).</summary>
    public static SandboxMap Read(JsonElement e, string source)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{source}: \"sandbox\" must be an object");
        var s = new SandboxMap();
        foreach (var p in e.EnumerateObject())
            switch (p.Name.ToLowerInvariant())
            {
                case "rack":
                    (s.RackX, s.RackZ, s.RackYaw) = Point(p.Value, source, "sandbox.rack");
                    break;
                case "rackparts":
                    foreach (var n in p.Value.EnumerateArray())
                    {
                        var name = n.GetString() ?? "";
                        if (ConstructParts.Find(name) == null)
                            throw new MapFileException($"{source}: sandbox.rackParts: no construct part named \"{name}\" ({string.Join(", ", ConstructParts.All.Select(d => d.Name))})");
                        s.RackParts.Add(name);
                    }
                    break;
                case "constructs":
                    foreach (var c in p.Value.EnumerateArray())
                    {
                        var name = c.TryGetProperty("blueprint", out var bp) ? bp.GetString() ?? "" : "";
                        if (ConstructBuild.FindBlueprint(name) == null)
                            throw new MapFileException($"{source}: sandbox.constructs: no blueprint named \"{name}\" ({string.Join(", ", ConstructBuild.Blueprints.Select(b => b.Name))})");
                        var (x, z, yaw) = Point(c.GetProperty("at"), source, "sandbox.constructs.at", c);
                        s.Constructs.Add(new SandboxSpawn { Blueprint = name, X = x, Z = z, Yaw = yaw });
                    }
                    break;
                case "loose":
                    foreach (var c in p.Value.EnumerateArray())
                    {
                        var name = c.TryGetProperty("part", out var pt) ? pt.GetString() ?? "" : "";
                        if (ConstructParts.Find(name) == null)
                            throw new MapFileException($"{source}: sandbox.loose: no construct part named \"{name}\"");
                        var (x, z, yaw) = Point(c.GetProperty("at"), source, "sandbox.loose.at", c);
                        s.Loose.Add(new SandboxLoose { Part = name, X = x, Z = z, Yaw = yaw });
                    }
                    break;
                case "partsmenu":
                    if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new MapFileException($"{source}: sandbox.partsMenu: expected true or false");
                    s.PartsMenu = p.Value.GetBoolean();
                    break;
                case "infiniteparts":
                    if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new MapFileException($"{source}: sandbox.infiniteParts: expected true or false");
                    s.InfiniteParts = p.Value.GetBoolean();
                    break;
                case "debrisseconds":
                    s.DebrisSeconds = Number(p.Value, source, "sandbox.debrisSeconds", 0, 3600);
                    break;
                case "debrisnear":
                    s.DebrisNear = Number(p.Value, source, "sandbox.debrisNear", 0, 200);
                    break;
                default:
                    throw new MapFileException($"{source}: sandbox: unknown key \"{p.Name}\" (rack, rackParts, constructs, loose, partsMenu, infiniteParts, debrisSeconds, debrisNear)");
            }
        if (s.RackParts.Count == 0)
            s.RackParts.AddRange(ConstructParts.All.Select(d => d.Name));
        return s;
    }

    private static float Number(JsonElement e, string source, string where, float lo, float hi)
    {
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetSingle(out var v) || !float.IsFinite(v) || v < lo || v > hi)
            throw new MapFileException($"{source}: {where}: expected a number from {lo} to {hi}");
        return v;
    }

    private static (float X, float Z, float Yaw) Point(JsonElement e, string source, string where, JsonElement? owner = null)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 2)
            throw new MapFileException($"{source}: {where}: expected [x, z] or [x, z, yaw]");
        var yaw = e.GetArrayLength() > 2 ? e[2].GetSingle() : owner is { } o && o.TryGetProperty("yaw", out var y) ? y.GetSingle() : 0f;
        return (e[0].GetSingle(), e[1].GetSingle(), yaw * MathF.PI / 180f);
    }
}

public sealed partial class Map
{
    /// <summary>The "sandbox" section (Sandbox mode), or null.</summary>
    public SandboxMap? Sandbox;
}
