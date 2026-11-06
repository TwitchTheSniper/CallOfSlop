using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>How a Modular Bomb Rack's stations are laid out.</summary>
public enum RackLayout : byte
{
    Row, // one line across the rack (side by side)
    Pairs, // two lines, fore and aft: pairs one behind the other
    Drum, // rings of three round an axis along the rack: the drum turns each to the bottom to let it go
}

/// <summary>A Modular Bomb Rack's station size: the old Bomb Rack's, Heavy Bomb Rack's and Giant Bomb Rack's ratings.</summary>
public enum RackSize : byte
{
    Light,
    Heavy,
    Giant,
}

/// <summary>What a Modular Bomb Rack is: its stations, their layout and their size. Stations 0: not a modular rack.</summary>
public readonly record struct RackShape(byte Stations, RackLayout Layout, RackSize Size);

/// <summary>
/// The Modular Bomb Rack (docs/sandbox/CONSTRUCTS.md, "Modular bomb racks"): one rack whose shape is its settings, 1 to 16 stations in a row, in
/// pairs or round a drum, of light, heavy or giant size. A shape is one part def: the presets are part files (a <c>"modularRack"</c> block that
/// <see cref="Expand"/> turns into the whole part), and a shape no file has is made at run time, one def a shape, in the extrusions' id space
/// (read it through <c>CPart.D</c>, <c>DefOf</c> or <c>ConstructParts.Def</c>, never <c>All[id]</c>). Its key is its name in saves and
/// blueprints ("Bomb Rack (8 heavy, drum)"), three bytes on the wire (<see cref="Write"/>). The configure tool's Stations, Layout and
/// Station size swap the part in place for the shape asked (<see cref="Reshape"/>, the Replacer's own plan), so its welds and wires stay.
/// </summary>
public static class ModularRack
{
    public const string Prefix = "Bomb Rack (";
    public const int MaxStations = 16;
    internal const byte WireKind = 247; // (after Extrusion.WireMark: a rack shape, three bytes)

    public static readonly string[] LayoutWords = ["row", "pairs", "drum"];
    public static readonly string[] SizeWords = ["light", "heavy", "giant"];
    private static readonly string[] LayoutOptions = ["single row", "double row", "drum"];

    /// <summary>A station size's numbers: the scale of every box, the pitch across and fore and aft (m), the load a station (kg), mass and health.</summary>
    private readonly record struct SizeRow(float Scale, float Pitch, float Depth, float Station, float BaseMass, float MassEach, float BaseHealth, float HealthEach, float Armour, float Cost);

    private static readonly SizeRow[] Sizes =
    [
        new(1f, 0.25f, 1.1f, 500f, 2f, 4f, 60f, 10f, 3f, 1f), // (the Light Bomb, the Aerial Bomb and the Small Nuke all hang on one)
        new(2.5f, 0.625f, 2.75f, 5000f, 15f, 35f, 200f, 30f, 6f, 8f), // (the Heavy Bomb Rack's rating)
        new(8f, 2f, 8.8f, 2_000_000f, 300f, 900f, 1000f, 150f, 12f, 120f), // (the Giant Bomb Rack's)
    ];

    private const float TotalShare = 0.75f; // past the first station, each adds this share of a station's load to the whole rack's rating
    private const string Dark = "#2a2c26", Brass = "#c8a838", Hub = "#3a3d40";

    public static float StationLoad(RackSize s) => Sizes[(int)s].Station;

    /// <summary>The whole rack's rating: a station's, and three quarters of one for each more (a rack can't take its heaviest on every hook).</summary>
    public static float TotalLoad(in RackShape s) => Sizes[(int)s.Size].Station * (1 + TotalShare * (s.Stations - 1));

    /// <summary>What a shape is worth, to the survival rule that the configure tool only makes a rack smaller.</summary>
    public static float Cost(in RackShape s) => s.Stations * Sizes[(int)s.Size].Cost * (s.Layout == RackLayout.Drum ? 1.25f : 1f);

    public static string Key(in RackShape s) => $"{Prefix}{s.Stations.ToString(CultureInfo.InvariantCulture)} {SizeWords[(int)s.Size]}, {LayoutWords[(int)s.Layout]})";

    /// <summary>A key back into a shape, or null.</summary>
    public static RackShape? ParseKey(string key)
    {
        if (!key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !key.EndsWith(')'))
            return null;
        var body = key[Prefix.Length..^1].Split(',', StringSplitOptions.TrimEntries);
        if (body.Length != 2)
            return null;
        var head = body[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (head.Length != 2 || !int.TryParse(head[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n is < 1 or > MaxStations)
            return null;
        var size = Array.FindIndex(SizeWords, w => w.Equals(head[1], StringComparison.OrdinalIgnoreCase));
        var layout = Array.FindIndex(LayoutWords, w => w.Equals(body[1], StringComparison.OrdinalIgnoreCase));
        return size < 0 || layout < 0 ? null : new RackShape((byte)n, (RackLayout)layout, (RackSize)size);
    }

    /// <summary>A part file's or a logic block's <c>"modular"</c> shape: <c>{"stations": 8, "layout": "drum", "size": "heavy"}</c>.</summary>
    public static RackShape ShapeOf(JsonElement e, string file)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: a rack's shape is {{\"stations\": 1 to {MaxStations}, \"layout\": row, pairs or drum, \"size\": light, heavy or giant}}");
        ConstructParts.Known(e, ["stations", "layout", "size"], $"{file}: the rack's shape");
        var n = ConstructParts.Num(ConstructParts.Need(e, "stations", file), file, "stations");
        if (n != MathF.Floor(n) || n < 1 || n > MaxStations)
            throw new ConstructPartException($"{file}: a rack has 1 to {MaxStations} stations, not {n.ToString(CultureInfo.InvariantCulture)}");
        var layout = Array.IndexOf(LayoutWords, ConstructParts.Str(ConstructParts.Need(e, "layout", file), file, "layout").ToLowerInvariant());
        var size = Array.IndexOf(SizeWords, ConstructParts.Str(ConstructParts.Need(e, "size", file), file, "size").ToLowerInvariant());
        if (layout < 0 || size < 0)
            throw new ConstructPartException($"{file}: a rack's layout is row, pairs or drum and its size light, heavy or giant");
        return new RackShape((byte)n, (RackLayout)layout, (RackSize)size);
    }

    /// <summary>A preset file (name, description, <c>"modularRack"</c>) as the whole part's text, for <see cref="ConstructParts.Parse"/>.</summary>
    public static string Expand(JsonElement root, JsonElement shape, string file)
    {
        ConstructParts.Known(root, ["format", "name", "description", "modularRack"], file);
        var name = ConstructParts.Str(ConstructParts.Need(root, "name", file), file, "name");
        var about = ConstructParts.Opt(root, "description") is { } d ? ConstructParts.Str(d, file, "description") : "";
        return Json(name, about, ShapeOf(shape, file), runtime: false);
    }

    // ---- The shape's part -------------------------------------------------------------------------------------------

    private static JsonArray V(float x, float y, float z) => [MathF.Round(x, 4), MathF.Round(y, 4), MathF.Round(z, 4)];

    private static JsonObject Box(float x0, float y0, float z0, float x1, float y1, float z1, string? colour = null, bool detail = false, bool hidden = false, string? shape = null)
    {
        var b = new JsonObject { ["min"] = V(x0, y0, z0), ["max"] = V(x1, y1, z1) };
        if (colour != null)
            b["color"] = colour;
        if (detail)
            b["detail"] = true;
        if (hidden)
            b["hidden"] = true;
        if (shape != null)
        {
            b["shape"] = shape;
            b["segments"] = 20;
        }
        return b;
    }

    private static JsonObject Sock(string name, float x, float y, float z, string dir) => new() { ["name"] = name, ["at"] = V(x, y, z), ["dir"] = dir };

    /// <summary>Where station <paramref name="k"/> is, and which way its hook faces (the part's frame).</summary>
    public static (Vector3 At, string Dir) Station(in RackShape s, int k)
    {
        var z = Sizes[(int)s.Size];
        var sc = z.Scale;
        switch (s.Layout)
        {
            case RackLayout.Pairs:
            {
                var cols = (s.Stations + 1) / 2;
                var w = z.Pitch * cols;
                return (new Vector3(-w / 2 + z.Pitch * (k / 2 + 0.5f), -0.04f * sc, (k % 2 == 0 ? -0.5f : 0.5f) * z.Depth), "-y");
            }
            case RackLayout.Drum:
            {
                var rings = (s.Stations + 2) / 3;
                var len = z.Depth * rings;
                var at = -len / 2 + z.Depth * (k / 3 + 0.5f);
                var r = DrumRadius(s);
                return (k % 3) switch
                {
                    0 => (new Vector3(0, -r, at), "-y"),
                    1 => (new Vector3(-r, 0, at), "-x"),
                    _ => (new Vector3(r, 0, at), "+x"),
                };
            }
            default:
            {
                var w = z.Pitch * s.Stations;
                return (new Vector3(-w / 2 + z.Pitch * (k + 0.5f), -0.04f * sc, 0), "-y");
            }
        }
    }

    public static float DrumRadius(in RackShape s) => 0.12f * Sizes[(int)s.Size].Scale;

    /// <summary>The part's whole text: boxes, sockets, numbers and settings from the shape. Every box that meets another overlaps it, so no two faces share a plane.</summary>
    public static string Json(string name, string description, in RackShape s, bool runtime)
    {
        var z = Sizes[(int)s.Size];
        var sc = z.Scale;
        var n = s.Stations;
        var boxes = new JsonArray();
        var sockets = new JsonArray();
        for (var k = 0; k < n; k++)
        {
            var (at, dir) = Station(s, k);
            sockets.Add(Sock($"lug{k}", at.X, at.Y, at.Z, dir));
        }
        float mass = z.BaseMass + z.MassEach * n, health = z.BaseHealth + z.HealthEach * n;
        if (s.Layout == RackLayout.Drum)
        {
            var rings = (n + 2) / 3;
            var half = z.Depth * rings / 2;
            var r = DrumRadius(s);
            var cap = 0.03f * sc;
            boxes.Add(Box(-r, -r, -half, r, r, half, hidden: true)); // (the drum's collider)
            boxes.Add(Box(-r, -r, -half - cap * 0.5f, r, r, half + cap * 0.5f, Hub, detail: true, shape: "cylinder")); // (its ends inside the end plates)
            boxes.Add(Box(-r * 1.15f, -r * 1.15f, -half - cap, r * 1.15f, r * 1.15f, -half, Dark));
            boxes.Add(Box(-r * 1.15f, -r * 1.15f, half, r * 1.15f, r * 1.15f, half + cap, Dark));
            boxes.Add(Box(-0.05f * sc, r * 0.8f, -half - cap * 0.5f, 0.05f * sc, r + 0.08f * sc, half + cap * 0.5f, Dark)); // (the hanger spine along its top)
            for (var k = 0; k < n; k++)
            {
                var (at, _) = Station(s, k);
                float lo = r + 0.04f * sc, hi = r - 0.02f * sc, wide = 0.05f * sc, deep = 0.07f * sc;
                boxes.Add((k % 3) switch
                {
                    0 => Box(-wide, -lo, at.Z - deep, wide, -hi, at.Z + deep, Brass, detail: true),
                    1 => Box(-lo, -wide, at.Z - deep, -hi, wide, at.Z + deep, Brass, detail: true),
                    _ => Box(hi, -wide, at.Z - deep, lo, wide, at.Z + deep, Brass, detail: true),
                });
            }
            sockets.Add(Sock("top", 0, r + 0.08f * sc, 0, "+y"));
            sockets.Add(Sock("back", 0, 0, half + cap, "+z"));
            sockets.Add(Sock("front", 0, 0, -half - cap, "-z"));
            mass *= 1.25f; // (the drum's turning gear)
        }
        else
        {
            var cols = s.Layout == RackLayout.Pairs ? (n + 1) / 2 : n;
            var w = z.Pitch * cols / 2;
            var d = (s.Layout == RackLayout.Pairs ? z.Depth / 2 : 0) + 0.2f * sc;
            boxes.Add(Box(-w, -0.04f * sc, -d, w, 0.08f * sc, d));
            for (var k = 0; k < n; k++)
            {
                var (at, _) = Station(s, k);
                boxes.Add(Box(at.X - 0.05f * sc, -0.08f * sc, at.Z - 0.07f * sc, at.X + 0.05f * sc, -0.035f * sc, at.Z + 0.07f * sc, Brass, detail: true)); // (its top inside the beam)
            }
            sockets.Add(Sock("top", 0, 0.08f * sc, 0, "+y"));
            sockets.Add(Sock("back", 0, 0, d, "+z"));
            sockets.Add(Sock("front", 0, 0, -d, "-z"));
            sockets.Add(Sock("right", w, 0, 0, "+x"));
            sockets.Add(Sock("left", -w, 0, 0, "-x"));
        }
        var station = z.Station;
        var total = TotalLoad(s);
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = (description.Length > 0 ? description + " " : "") + Describe(s),
            ["group"] = "Weapons",
            ["section"] = "Bomb racks",
            ["role"] = "logic",
            ["tool"] = "welder",
            ["mass"] = MathF.Round(mass, 1),
            ["health"] = MathF.Round(MathF.Min(health, ConstructParts.MaxPartHealth)),
            ["armour"] = z.Armour,
            ["material"] = "steel",
            ["color"] = Dark,
            ["attachSeconds"] = MathF.Min(ConstructParts.MaxAttachSeconds, 1.5f + 0.1f * n * (s.Size == RackSize.Light ? 1 : 2)),
            ["logic"] = new JsonObject
            {
                ["kind"] = "bombrack",
                ["load"] = total,
                ["station"] = station,
                ["modular"] = new JsonObject { ["stations"] = n, ["layout"] = LayoutWords[(int)s.Layout], ["size"] = SizeWords[(int)s.Size] },
            },
            ["boxes"] = boxes,
            ["sockets"] = sockets,
            ["config"] = new JsonArray
            {
                new JsonObject { ["key"] = "stations", ["label"] = "Stations", ["group"] = "Rack", ["min"] = 1, ["max"] = MaxStations, ["step"] = 1, ["default"] = n,
                    ["tooltip"] = "How many hooks. The rack's size follows. Change it with nothing on its hooks; in survival a rack only gets smaller here." },
                new JsonObject { ["key"] = "rackLayout", ["type"] = "choice", ["label"] = "Layout", ["group"] = "Rack", ["options"] = new JsonArray(LayoutOptions.Select(o => (JsonNode)o).ToArray()), ["default"] = (int)s.Layout,
                    ["tooltip"] = "Single row: side by side. Double row: pairs, one behind the other. Drum: rings of three round its axis, each turned to the bottom to let go." },
                new JsonObject { ["key"] = "rackSize", ["type"] = "choice", ["label"] = "Station size", ["group"] = "Rack", ["options"] = new JsonArray(SizeWords.Select(o => (JsonNode)o).ToArray()), ["default"] = (int)s.Size,
                    ["tooltip"] = "Light: 500 kg a station. Heavy: 5000 kg. Giant: 2000 t. The rack grows with it." },
                new JsonObject { ["key"] = "fireOrder", ["type"] = "choice", ["label"] = "Fire order", ["group"] = "Release", ["options"] = new JsonArray("one at a time", "salvo"), ["default"] = 0,
                    ["tooltip"] = "One at a time: one each interval while its input is on. Salvo: one press lets everything go, a tick apart. A drum always turns one to the bottom each interval." },
                new JsonObject { ["key"] = "rackInterval", ["label"] = "Interval", ["group"] = "Release", ["unit"] = "s", ["min"] = 0.05f, ["max"] = 3, ["step"] = 0.05f, ["default"] = 0.35f,
                    ["tooltip"] = "Seconds between drops one at a time, and a drum's turn." },
            },
        };
        if (runtime)
            part["stage"] = true; // (never offered: the configure tool makes it from a preset)
        return part.ToJsonString();
    }

    /// <summary>The shape in words, for the description and the look line.</summary>
    public static string Describe(in RackShape s)
    {
        var size = SizeWords[(int)s.Size];
        var where = s.Layout switch
        {
            RackLayout.Pairs => "in pairs, one behind the other",
            RackLayout.Drum => "round a drum that turns each to the bottom to let it go",
            _ => "side by side",
        };
        return $"{s.Stations} {size} station{(s.Stations == 1 ? "" : "s")} {where}. Rated for {StationLoad(s.Size):0} kg a station, {TotalLoad(s):0} kg in all.";
    }

    // ---- One def a shape --------------------------------------------------------------------------------------------

    private static readonly Dictionary<RackShape, ConstructPartDef> Interned = new();
    private static readonly Dictionary<RackShape, ConstructPartDef> Presets = new();
    private static IReadOnlyList<ConstructPartDef>? _presetsOf;

    /// <summary>The part for a shape: a preset file's when one has it, else one made the first time. Null: the run-time table is full.</summary>
    public static ConstructPartDef? Def(RackShape s)
    {
        if (s.Stations is < 1 or > MaxStations || (int)s.Layout > 2 || (int)s.Size > 2)
            return null;
        var all = ConstructParts.All;
        lock (Interned)
        {
            if (!ReferenceEquals(_presetsOf, all))
            {
                Presets.Clear();
                foreach (var d in all)
                    if (d.IsModularRack && !d.IsRackRuntime)
                        Presets.TryAdd(d.Rack, d);
                _presetsOf = all;
            }
            if (Presets.TryGetValue(s, out var preset))
                return preset;
            if (Interned.TryGetValue(s, out var have))
                return have;
            var key = Key(s);
            ConstructPartDef def;
            try
            {
                def = ConstructParts.Parse(Json(key, "", s, runtime: true), key);
            }
            catch (ConstructPartException)
            {
                return null; // (a shape the rules refuse: can't happen with the numbers above)
            }
            def.IsRackRuntime = true;
            def.ExHash = Fnv(key);
            if (!Extrusion.Adopt(def))
                return null;
            Interned[s] = def;
            return def;
        }
    }

    /// <summary>A rack's key (a blueprint's or a save's name) as its part.</summary>
    public static ConstructPartDef? Parse(string key) => ParseKey(key) is { } s ? Def(s) : null;

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    /// <summary>After <see cref="Extrusion.WireMark"/>: the shape, three bytes.</summary>
    public static void Write(PacketWriter w, ConstructPartDef d) => w.U8(d.Rack.Stations).U8((byte)d.Rack.Layout).U8((byte)d.Rack.Size);

    /// <summary>The shape from the wire, as this machine's part id. A stand-in, never a dropped packet, if the table is full.</summary>
    public static ushort Read(ref PacketReader r)
    {
        var s = new RackShape(r.U8(), (RackLayout)r.U8(), (RackSize)r.U8());
        if (s.Stations is < 1 or > MaxStations || (int)s.Layout > 2 || (int)s.Size > 2)
            throw new MalformedPacketException($"a bomb rack of {s.Stations} stations, layout {(int)s.Layout}, size {(int)s.Size}");
        if (Def(s) is { } d)
            return d.Id;
        return ConstructParts.Need("Bomb Rack").Id;
    }

    // ---- The configure tool -----------------------------------------------------------------------------------------

    /// <summary>The shape a rack's settings ask for.</summary>
    public static RackShape Asked(in CPart p, ConstructPartDef d) => new(
        (byte)Math.Clamp((int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.Stations, d.Rack.Stations)), 1, MaxStations),
        (RackLayout)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.RackLayout, (int)d.Rack.Layout), 0, 2),
        (RackSize)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.RackSize, (int)d.Rack.Size), 0, 2));

    /// <summary>A shape with one of its settings changed (a blueprint's refusal names the part it means).</summary>
    public static RackShape WithSetting(RackShape s, CfgKey k, float v) => k switch
    {
        CfgKey.Stations => s with { Stations = (byte)Math.Clamp((int)MathF.Round(v), 1, MaxStations) },
        CfgKey.RackLayout => s with { Layout = (RackLayout)Math.Clamp((int)v, 0, 2) },
        CfgKey.RackSize => s with { Size = (RackSize)Math.Clamp((int)v, 0, 2) },
        _ => s,
    };

    /// <summary>Is anything on one of its hooks (or welded to it and held)?</summary>
    public static bool Loaded(Construct c, int part)
    {
        var d = c.DefOf(part);
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
        {
            var mine = (e & 1) == 0 ? c.Links[e >> 1].SA : c.Links[e >> 1].SB;
            if (mine < d.Sockets.Length && d.Sockets[mine].Name.StartsWith("lug", StringComparison.Ordinal) || c.DefOf(c.OtherEnd(e)).Explosive != null)
                return true;
        }
        Span<ushort> held = stackalloc ushort[ConstructOrdnance.MaxHeld];
        return ConstructOrdnance.Held(c, part, held) > 0;
    }

    /// <summary>
    /// Server, from the configure tool: the rack becomes the shape its settings ask for, in place, keeping its welds and wires (the Replacer's plan,
    /// so its mounting socket stays where it is and it grows from there). Null, or why not; the caller puts its settings back on a refusal.
    /// </summary>
    public static string? Reshape(ConstructWorld world, Construct c, int part, bool creative)
    {
        var d = c.DefOf(part);
        var want = Asked(c.Parts[part], d);
        if (want == d.Rack)
            return null;
        if (Loaded(c, part))
            return "take everything off its hooks first";
        if (!creative && Cost(want) > Cost(d.Rack))
            return "in survival the configure tool only makes a rack smaller: build a bigger one";
        if (Def(want) is not { } e)
            return "no room for another rack shape: restart the game";
        if (ConstructReplace.Plan(world, c, part, e, out var swap) is { } why)
            return why;
        ConstructReplace.Apply(world, c, part, e, swap);
        return null;
    }
}
