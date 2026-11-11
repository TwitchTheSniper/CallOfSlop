using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.Interiors;

/// <summary>A faction's interior look (Constructs/Interiors/Palettes.json, docs/sandbox/INTERIORS.md phase 12).</summary>
public sealed record InteriorPalette(string Style, string[] Wall, string Floor, string FloorColour, string CrewFloor, string ServiceFloor,
    string Frame, string Trim, string Accent, string Hazard, float Wear, string Lamp);

/// <summary>How a room type shifts its palette: a tone ("pale", "dark", "warm" or "") and whether its trim is the hazard colour.</summary>
public sealed record InteriorRoomLook(string Tone, bool Hazard);

/// <summary>The palettes by style, and the room types' shifts. A bad file is refused with a reason.</summary>
public sealed class InteriorPalettes
{
    public readonly Dictionary<string, InteriorPalette> ByStyle = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, InteriorRoomLook> Rooms = new(StringComparer.Ordinal);
    public string Default = "default";

    public static InteriorPalettes Load(string folder)
    {
        var path = Path.Combine(folder, "Palettes.json");
        if (!File.Exists(path))
            throw new InteriorDataException($"Palettes.json: missing (looked in {folder})");
        var p = new InteriorPalettes();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != InteriorData.Format)
                throw new InteriorDataException($"Palettes.json: format must be \"{InteriorData.Format}\"");
            if (root.TryGetProperty("default", out var d))
                p.Default = d.GetString() ?? "default";
            foreach (var e in root.GetProperty("palettes").EnumerateArray())
            {
                var style = Text(e, "style", "a palette");
                var where = $"Palettes.json \"{style}\"";
                var walls = e.GetProperty("wall").EnumerateArray().Select(w => Colour(w.GetString(), where + " wall")).ToArray();
                if (walls.Length == 0)
                    throw new InteriorDataException($"{where}: wall needs a colour");
                var wear = e.TryGetProperty("wear", out var we) ? we.GetSingle() : 0.5f;
                if (wear is < 0 or > 1)
                    throw new InteriorDataException($"{where}: wear is 0 to 1");
                p.ByStyle[style] = new InteriorPalette(style, walls, Material(e, "floor", where), Colour(Text(e, "floorColour", where), where), Material(e, "crewFloor", where),
                    Material(e, "serviceFloor", where), Colour(Text(e, "frame", where), where), Colour(Text(e, "trim", where), where), Colour(Text(e, "accent", where), where),
                    Colour(Text(e, "hazard", where), where), wear, Colour(Text(e, "lamp", where), where));
            }
            if (root.TryGetProperty("rooms", out var rooms))
                foreach (var r in rooms.EnumerateObject())
                {
                    var tone = r.Value.TryGetProperty("tone", out var t) ? t.GetString() ?? "" : "";
                    if (tone is not ("" or "pale" or "dark" or "warm"))
                        throw new InteriorDataException($"Palettes.json room \"{r.Name}\": tone is pale, dark or warm");
                    p.Rooms[r.Name] = new InteriorRoomLook(tone, r.Value.TryGetProperty("hazard", out var h) && h.ValueKind == JsonValueKind.True);
                }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InteriorDataException($"Palettes.json: {e.Message}");
        }
        if (!p.ByStyle.ContainsKey(p.Default))
            throw new InteriorDataException($"Palettes.json: no palette for the default \"{p.Default}\"");
        return p;
    }

    /// <summary>The palette for a style asked for by name (a FactionStyle's Name or Group), else for the base blueprint's group, else the default.</summary>
    public InteriorPalette? For(string? style, string group, out string why)
    {
        why = "";
        if (style != null)
        {
            if (Find(style) is { } asked)
                return asked;
            why = $"no interior palette for \"{style}\" (Palettes.json has {string.Join(", ", ByStyle.Keys)})";
            return null;
        }
        return Find(group) ?? ByStyle[Default];
    }

    private InteriorPalette? Find(string key)
    {
        if (ByStyle.TryGetValue(key, out var p))
            return p;
        var s = Styles.All.FirstOrDefault(x => x.Name.Equals(key, StringComparison.OrdinalIgnoreCase) || x.Group.Equals(key, StringComparison.OrdinalIgnoreCase));
        return s != null && ByStyle.TryGetValue(s.Name, out var byName) ? byName : null;
    }

    private static string Text(JsonElement e, string key, string where) =>
        e.TryGetProperty(key, out var v) && v.GetString() is { Length: > 0 } s ? s : throw new InteriorDataException($"{where}: \"{key}\" missing");

    private static string Colour(string? s, string where) =>
        ConstructPaint.TryParse(s, out _) ? s! : throw new InteriorDataException($"{where}: \"{s}\" isn't a colour like \"#c04020\"");

    private static string Material(JsonElement e, string key, string where)
    {
        var m = Text(e, key, where);
        return ConstructFinish.Of(m) != 0 ? m : throw new InteriorDataException($"{where}: {key} \"{m}\" isn't an offered material of Materials/Surfaces.json");
    }
}

public sealed partial class InteriorBuilder
{
    /// <summary>The look the last stage gives the fitted parts (null: none, every part keeps its own).</summary>
    public InteriorPalette? Palette { get; set; }
    public Dictionary<string, InteriorRoomLook> RoomLooks { get; set; } = new();
    public int LookPainted, LookFinished, LookLamps;

    private readonly List<(IReadOnlyList<PlanRoom> Rooms, int Lv)> _plansBuilt = new();

    private static readonly HashSet<string> CrewRooms = ["bridge", "quarters", "mess", "medical", "lockers", "gunnery"];
    private static readonly HashSet<string> ServiceRooms = ["engineering", "fuelbay", "fuel"];

    /// <summary>
    /// (phase 12) Paints and finishes every part the builder fitted by what it is (floor, wall, door frame, door, stair, trim, lamp) and the
    /// room it stands in, from <see cref="Palette"/> and the room type's shift; through the step's paint and finish, so a save, a blueprint
    /// and the wire carry it as they carry a player's paint. The vehicle's own parts keep theirs.
    /// </summary>
    private void Look()
    {
        if (Palette is not { } pal)
            return;
        var cells = new Dictionary<(int L, int X, int Z), PlanRoom>();
        foreach (var (rooms, lv) in _plansBuilt)
            foreach (var r in rooms)
                foreach (var (x, z) in r.Cells())
                    cells.TryAdd((r.Deck + lv, x, z), r);
        var own = new HashSet<int>(_carried);
        foreach (var v in _base)
            own.Add(v.Index);
        var floors = new HashSet<string> { Shell("floor"), Shell("stairFloor") };
        floors.UnionWith(_data.FloorPanels);
        var walls = new HashSet<string> { Shell("wall4"), Shell("wall2"), Shell("doorWall"), Shell("corner"), Shell("garageWall"), Shell("railing") };
        walls.UnionWith(_data.Panels);
        var doors = new HashSet<string> { Shell("door"), Shell("swingDoor"), Shell("bulkheadDoor"), Shell("doubleDoorL"), Shell("doubleDoorR") };
        var looks = new Dictionary<PlanRoom, (string Wall, string Trim, bool Crew, bool Service)>();
        var wallMat = pal.Wear < 0.34f ? "wall panelling" : pal.Wear < 0.67f ? "worn paint" : "rusty paint";
        var parts = Parts.ToList(); // (paint and config add no steps, but the list is the base's)
        foreach (var v in parts)
        {
            if (own.Contains(v.Index))
                continue;
            var name = v.Def.Name;
            C.PartBounds(v.Index, out var lo, out var hi);
            var mid = (lo + hi) * 0.5f;
            var key = ((int)MathF.Floor((lo.Y - _floorY + 0.5f) / 3), (int)MathF.Floor((mid.X - _corner.X) / 4), (int)MathF.Floor((mid.Z - _corner.Z) / 4));
            var room = cells.GetValueOrDefault(key);
            var (wall, trim, crew, service) = room == null ? (pal.Wall[0], pal.Trim, false, false) : LookOf(room, pal, looks);
            if (floors.Contains(name))
                LookPart(v, crew ? pal.CrewFloor : service ? pal.ServiceFloor : pal.Floor, pal.FloorColour);
            else if (walls.Contains(name))
                LookPart(v, wallMat, wall);
            else if (name == Shell("doorCorner"))
                LookPart(v, trim == pal.Hazard ? "hazard paint" : wallMat, trim);
            else if (doors.Contains(name))
                LookPart(v, null, pal.Accent);
            else if (name == Shell("stair"))
                LookPart(v, "deck plate", pal.Frame);
            else if (name == "Kick Plate")
                LookPart(v, pal.Wear >= 0.67f ? "rusty paint" : service && pal.Wear >= 0.5f ? "soot" : "deck plate", trim);
            else if (name == "Wall Rib")
                LookPart(v, service && pal.Wear >= 0.5f ? "soot" : pal.Wear >= 0.5f ? "dirty steel" : "steel", pal.Frame);
            else if (name == "Bay Marking")
                LookPart(v, null, pal.Hazard);
            else if (name is "Wall Light" or "Hall Floodlight")
            {
                Config(v, ("colour", pal.Lamp));
                LookLamps++;
            }
        }
        Notes.Add($"look: the {pal.Style} palette (wear {pal.Wear:0.##}, walls {wallMat}): {LookPainted} parts painted, {LookFinished} finished, {LookLamps} lamps coloured");
    }

    private void LookPart(VPart v, string? material, string colour)
    {
        if (material != null && ConstructFinish.Paintable(v.Def))
        {
            Finish(v, material);
            LookFinished++;
        }
        if (ConstructPaint.Paintable(v.Def))
        {
            Paint(v, colour);
            LookPainted++;
        }
    }

    private (string Wall, string Trim, bool Crew, bool Service) LookOf(PlanRoom r, InteriorPalette pal, Dictionary<PlanRoom, (string, string, bool, bool)> cache)
    {
        if (cache.TryGetValue(r, out var got))
            return got;
        var shift = RoomLooks.GetValueOrDefault(r.Type);
        var wall = Hex(pal.Wall[(r.Id & 0x7fffffff) % pal.Wall.Length]);
        wall = shift?.Tone switch
        {
            "pale" => Vector3.Lerp(wall, new Vector3(0.95f, 0.95f, 0.93f), 0.55f),
            "dark" => Vector3.Lerp(wall, new Vector3(0.1f, 0.11f, 0.12f), 0.45f),
            "warm" => Vector3.Lerp(wall, new Vector3(0.78f, 0.6f, 0.42f), 0.3f),
            _ => wall,
        };
        var look = (ToHex(wall), shift?.Hazard == true ? pal.Hazard : pal.Trim, CrewRooms.Contains(r.Type), ServiceRooms.Contains(r.Type));
        cache[r] = look;
        return look;
    }

    private static Vector3 Hex(string s)
    {
        var v = uint.Parse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Vector3((v >> 16) & 255, (v >> 8) & 255, v & 255) / 255f;
    }

    private static string ToHex(Vector3 c)
    {
        static int B(float f) => Math.Clamp((int)MathF.Round(f * 255f), 0, 255);
        return $"#{B(c.X):x2}{B(c.Y):x2}{B(c.Z):x2}";
    }
}
