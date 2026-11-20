using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CallOfSlop.PartKit;
using CallOfSlop.ProcShips;

namespace CallOfSlop;

/// <summary>
/// <c>--procship-gallery DIR [--seeds "F17,C3d"] [--per N] [--add]</c>: the Procedural gallery (docs/procships/PROCSHIPS.md). Each
/// generated ship is built by the game's own rules and written as a preview model (as the Ship gallery's) with its plan laid over it
/// (<c>"proc"</c>: its traits, rooms, doors, airlocks, hatches, ladders and module sockets, in the model's frame), one
/// DIR/models/CODE.json each, listed in DIR/models/index.json with the ones that couldn't be made and why; DIR/index.html is
/// <c>tools/preview/procedural.html</c>. With no seeds, seeds 1..N of each class; <c>--add</c> keeps the ships already listed.
/// </summary>
public static partial class ConstructPreview
{
    internal static int ProcGallery(string dir, string[] args)
    {
        var models = Path.Combine(dir, "models");
        Directory.CreateDirectory(models);
        var per = int.TryParse(Flag(args, "--per"), out var n) ? n : 4;
        var specs = Flag(args, "--seeds") is { } seeds
            ? seeds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(t => ProcShipSpec.Parse(t) ?? throw new ArgumentException($"--seeds: \"{t}\" names no ship")).ToList()
            : Enum.GetValues<ProcClass>().SelectMany(c => Enumerable.Range(1, per).Select(s => new ProcShipSpec(s, c))).ToList();
        var listPath = Path.Combine(models, "index.json");
        var ships = new List<JsonObject>();
        var refused = new List<JsonObject>();
        if (args.Any(a => a.Equals("--add", StringComparison.OrdinalIgnoreCase)) && File.Exists(listPath) && JsonNode.Parse(File.ReadAllText(listPath)) is JsonObject old)
        {
            ships.AddRange((old["ships"] as JsonArray ?? []).OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()));
            refused.AddRange((old["refused"] as JsonArray ?? []).OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()));
        }
        var changed = new List<string>();
        foreach (var spec in specs)
        {
            ships.RemoveAll(o => (string?)o["code"] == spec.Code);
            refused.RemoveAll(o => (string?)o["code"] == spec.Code);
            ProcShip ship;
            string? json;
            int parts;
            string? why;
            try
            {
                ship = ProcShip.Get(spec);
                json = ProcModel(ship, out parts, out why);
            }
            catch (ProcShipException e)
            {
                refused.Add(new JsonObject { ["code"] = spec.Code, ["title"] = spec.Title, ["why"] = e.Message });
                Console.WriteLine($"REFUSED {spec.Title} ({spec.Code}): {e.Message}");
                continue;
            }
            if (json == null)
            {
                refused.Add(new JsonObject { ["code"] = spec.Code, ["title"] = spec.Title, ["why"] = "it doesn't build: " + why });
                Console.WriteLine($"REFUSED {spec.Title} ({spec.Code}): it doesn't build: {why}");
                continue;
            }
            var file = Slug(spec.Code) + ".json";
            var path = Path.Combine(models, file);
            if (!File.Exists(path) || File.ReadAllText(path) != json)
            {
                File.WriteAllText(path, json);
                changed.Add("models/" + file);
            }
            var traits = new JsonArray();
            foreach (var (k, v) in ship.Traits)
                traits.Add(new JsonArray(k, v));
            ships.Add(new JsonObject
            {
                ["code"] = spec.Code, ["title"] = spec.Title, ["seed"] = spec.Seed, ["class"] = spec.Class.ToString().ToLowerInvariant(),
                ["hull"] = ship.Hull.Style.ToString().ToLowerInvariant(), ["bow"] = ship.Hull.Bow.ToString().ToLowerInvariant(),
                ["role"] = ship.Hull.Role.ToString().ToLowerInvariant(), ["nacelles"] = ship.Hull.Nacelles.Count > 0,
                ["decks"] = ship.Hull.Decks, ["length"] = Math.Round(ship.Hull.Length, 1), ["fit"] = FitJson(ship.Plan.Kit),
                ["file"] = "models/" + file, ["kb"] = json.Length / 1024, ["parts"] = parts, ["steps"] = ship.Steps, ["tris"] = _tris, ["traits"] = traits,
            });
            Console.WriteLine($"{spec.Title} ({spec.Code}): {parts} parts, {_tris} triangles, {json.Length / 1024} KB");
        }
        int Order(JsonObject o) => (int)Enum.Parse<ProcClass>((string)o["class"]!, true) * 1000000 + (int)o["seed"]!;
        ships.Sort((a, b) => Order(a) != Order(b) ? Order(a).CompareTo(Order(b)) : string.CompareOrdinal((string)a["code"]!, (string)b["code"]!));
        var list = new JsonObject
        {
            ["generator"] = ProcShipSpec.Version,
            ["ships"] = new JsonArray([.. ships]),
            ["refused"] = new JsonArray([.. refused]),
        }.ToJsonString();
        if (!File.Exists(listPath) || File.ReadAllText(listPath) != list)
        {
            File.WriteAllText(listPath, list);
            changed.Add("models/index.json");
        }
        if (ProcTemplate() is { } page)
        {
            var pagePath = Path.Combine(dir, "index.html");
            if (!File.Exists(pagePath) || File.ReadAllText(pagePath) != page)
            {
                File.WriteAllText(pagePath, page);
                changed.Add("index.html");
            }
        }
        else
            Console.WriteLine($"no tools/preview/procedural.html above {AppContext.BaseDirectory}: wrote the models only");
        Console.WriteLine($"procedural gallery {Path.GetFullPath(dir)}: {ships.Count} ships, {refused.Count} refused; changed: {(changed.Count == 0 ? "nothing" : string.Join(" ", changed))}");
        return refused.Count;
    }

    private static string? ProcTemplate()
    {
        for (var up = new DirectoryInfo(AppContext.BaseDirectory); up != null; up = up.Parent)
            if (File.Exists(Path.Combine(up.FullName, "tools", "preview", "procedural.html")))
                return File.ReadAllText(Path.Combine(up.FullName, "tools", "preview", "procedural.html"));
        return null;
    }

    /// <summary>A procedural ship built by the game's rules and written as a preview model with its plan laid over it; null (and why) when it doesn't build.</summary>
    internal static string? ProcModel(ProcShip ship, out int parts, out string? why)
    {
        parts = 0;
        ConstructBuild.Blueprint bp;
        try
        {
            bp = ConstructBuild.ParseBlueprint(PyJson.Dumps(ship.Blueprint), ship.Name);
        }
        catch (Exception ex) when (ex is ConstructPartException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            why = ex.Message;
            return null;
        }
        var c = ConstructBuild.BuildOnGround(new ConstructWorld(), bp, Vector3.Zero, 0, out var buildWhy);
        if (c == null)
        {
            why = buildWhy;
            return null;
        }
        parts = c.PartCount;
        why = null;
        _partFrames = [];
        _animWrite = () => ProcOverlay(ship, _partFrames!.Count > 0 ? _partFrames[0] : Matrix4x4.Identity);
        try
        {
            return Model(c, bp);
        }
        finally
        {
            _partFrames = null;
            _animWrite = null;
        }
    }

    /// <summary>
    /// The plan in the model's frame (",\"proc\":{...}"): the first step's piece stands at the body's origin unturned, so a point in
    /// ship metres is that piece's frame moved by half a cell. Boxes are [x0, y0, z0, x1, y1, z1] after the turn.
    /// </summary>
    private static string ProcOverlay(ProcShip ship, Matrix4x4 frame)
    {
        var h = ship.Hull;
        var p = ship.Plan;
        const double H = ProcShipDraw.H;
        Vector3 M(double x, double y, double z) => Vector3.Transform(new Vector3((float)(x - H), (float)(y - H), (float)(z - H)), frame);
        JsonArray Pt(double x, double y, double z)
        {
            var v = M(x, y, z);
            return new JsonArray(R(v.X), R(v.Y), R(v.Z));
        }
        JsonArray Bx(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            Vector3 a = M(x0, y0, z0), b = M(x1, y1, z1);
            var lo = Vector3.Min(a, b);
            var hi = Vector3.Max(a, b);
            return new JsonArray(R(lo.X), R(lo.Y), R(lo.Z), R(hi.X), R(hi.Y), R(hi.Z));
        }
        var rooms = new JsonArray();
        foreach (var r in p.Rooms)
        {
            var room = new JsonObject { ["deck"] = r.Deck, ["name"] = r.Name, ["kind"] = r.Kind, ["box"] = Bx(r.X0, ProcHullShape.FloorTop(r.Deck), r.Z0, r.X1, ProcHullShape.Ceil(r.Top), r.Z1) };
            if (r.Top > r.Deck)
                room["top"] = r.Top;
            rooms.Add(room);
        }
        var doors = new JsonArray();
        foreach (var d in p.Doors)
        {
            double mid = d.A + ProcShipPlan.DoorW / 2, y = d.Floor + ProcShipPlan.DoorH / 2;
            doors.Add(new JsonObject { ["deck"] = d.Deck, ["kind"] = d.Kind, ["at"] = d.AlongX ? Pt(mid, y, d.C) : Pt(d.C, y, mid), ["acrossX"] = !d.AlongX });
        }
        foreach (var l in p.Locks)
        {
            var ft = ProcHullShape.FloorTop(l.Deck);
            doors.Add(new JsonObject { ["deck"] = l.Deck, ["kind"] = "lock outer", ["at"] = Pt(l.Side * (l.XOut - 0.125), ft + ProcShipPlan.DoorH / 2, l.A + ProcShipPlan.DoorW / 2), ["acrossX"] = true });
        }
        var locks = new JsonArray();
        foreach (var l in p.Locks)
        {
            var ft = ProcHullShape.FloorTop(l.Deck);
            locks.Add(new JsonObject { ["deck"] = l.Deck, ["side"] = l.Side < 0 ? "port" : "starboard", ["box"] = Bx(l.Side * l.XIn, ft, l.A - 0.05, l.Side * l.XOut, ft + 2.4, l.A + 1.55) });
        }
        var hatches = new JsonArray();
        foreach (var t in p.Hatches)
            hatches.Add(new JsonObject
            {
                ["kind"] = t.Kind, ["deck"] = t.Deck,
                ["box"] = t.Kind == "belly" ? Bx(t.X0, t.SillTop, t.Z0, t.X1, ProcHullShape.FloorTop(0), t.Z1) : Bx(t.X0, ProcHullShape.FloorTop(t.Deck), t.Z0, t.X1, h.TopY, t.Z1),
            });
        var ladders = new JsonArray();
        foreach (var l in p.Ladders)
            ladders.Add(new JsonObject { ["deck"] = l.Deck, ["why"] = l.Why, ["foot"] = Pt(l.X, ProcHullShape.FloorTop(l.Deck), l.Z), ["top"] = Pt(l.X, ProcHullShape.FloorTop(l.Deck) + l.Rise, l.Z) });
        var sockets = new JsonArray();
        foreach (var s in p.Sockets)
            sockets.Add(new JsonObject
            {
                ["name"] = s.Name, ["where"] = s.Where, ["kind"] = s.Use, ["size"] = $"{s.W}x{s.L}", ["keyed"] = s.Keyed,
                ["box"] = Bx(s.Min[0], s.Min[1], s.Min[2], s.Max[0], s.Max[1], s.Max[2]),
            });
        var decks = new JsonArray();
        for (var d = 0; d < h.Decks; d++)
            decks.Add(new JsonObject { ["name"] = $"Deck {d + 1}", ["floor"] = R(M(0, ProcHullShape.FloorTop(d), 0).Y), ["ceiling"] = R(M(0, ProcHullShape.Ceil(d), 0).Y) });
        var traits = new JsonArray();
        foreach (var (k, v) in ship.Traits)
            traits.Add(new JsonArray(k, v));
        var roles = new JsonObject();
        foreach (var (name, (role, _, _)) in ship.Roles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            roles[name] = role;
        var o = new JsonObject
        {
            ["code"] = ship.Spec.Code, ["title"] = ship.Spec.Title, ["generator"] = ProcShipSpec.Version, ["accent"] = p.Accent, ["fit"] = FitJson(p.Kit),
            ["traits"] = traits, ["decks"] = decks, ["rooms"] = rooms, ["doors"] = doors, ["locks"] = locks, ["hatches"] = hatches,
            ["ladders"] = ladders, ["sockets"] = sockets, ["roles"] = roles,
        };
        return ",\"proc\":" + o.ToJsonString();

        static double R(float v) => Math.Round(v, 3);
    }

    /// <summary>What a ship spent its points on (ProcShipFit.cs): the class's points, the reactors' and the drive's costs, the payload left.</summary>
    private static JsonObject FitJson(ProcFit f) => new()
    {
        ["points"] = f.Points, ["reactorSize"] = f.ReactorSize, ["reactors"] = f.Reactors, ["reactorCost"] = f.ReactorCost,
        ["drive"] = f.Drive.ToString().ToLowerInvariant(), ["driveCost"] = ProcFit.DriveCost(f.Drive), ["payload"] = f.Payload,
    };
}
