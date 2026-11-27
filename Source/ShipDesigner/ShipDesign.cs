using System.Globalization;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// A ship as the ship designer holds it (docs/sandbox/SHIP-DESIGNER.md): hull bodies lofted from stations, pieces on their skins, decks and the
/// rooms on them. Plain data with no game types, so the same files build into the game and into the designer's web page (tools/ship-designer,
/// C# compiled to WebAssembly). Coordinates as the Zero Day's generator: x across (0 the centreline), y up, z along the ship, the bow at the
/// smallest z; metres.
/// </summary>
public sealed class ShipDesign
{
    public const int Version = 1;
    public const int MaxHulls = 12, MaxStations = 24, MaxPieces = 200, MaxRooms = 120, MaxDecks = 24, MaxJoins = 40, MaxRisers = 40, MaxPassages = 40;

    public string Name = "New ship";

    /// <summary>
    /// How the decks lie. Along (false): floors level, decks along the length, as the Zero Day's (a deck's Floor is a height). Stacked
    /// (true): floors across the ship facing the bow, so thrust is down, as a tower ship's (a deck's Floor is a z, its rooms' up is towards the
    /// bow, and a room's X runs across and its Z0..Z1 hold its height range y).
    /// </summary>
    public bool Stacked;

    /// <summary>
    /// How the doors between rooms are placed (Karl, 2026-11-27). Automatic (false): every two rooms on a deck that share a wall get a
    /// doorway, where a placed door sits or else in the wall's middle. Manual (true): only the doors the player places are cut; a shared
    /// wall with none stays a wall, so the rooms behind it are not joined.
    /// </summary>
    public bool ManualDoors;

    /// <summary>A point on a deck to world: (u, v) on the floor (a room's X and Z), up above it.</summary>
    public double[] World(Deck dk, double u, double v, double up) => Stacked ? [u, v, dk.Floor - up] : [u, dk.Floor + up, v];

    /// <summary>A room's world box: x0, x1, y0, y1, z0, z1.</summary>
    /// <summary>A room's deck as the room sees it: the deck's floor, and the room's own height when it has one (a tall hold, a two-deck hall).</summary>
    /// <summary>A room's own floor and clear height: its deck's, or its own height (it can rise through the decks above), and
    /// for a room merged down through the deck below (Karl, 2026-11-24), its floor that much lower.</summary>
    public Deck DeckOf(Room r)
    {
        if (r.Height <= 0 && r.Below <= 0)
            return Decks[r.Deck];
        var dk = Decks[r.Deck];
        var h = r.Height > 0 ? r.Height : dk.Height;
        return new Deck { Floor = dk.Floor - (Stacked ? -r.Below : r.Below), Height = h + r.Below, Hull = dk.Hull };
    }

    /// <summary>Which of two decks is under the other (stacked decks: up is -z, so the one aft).</summary>
    public bool Under(int a, int b) => Stacked ? Decks[a].Floor > Decks[b].Floor : Decks[a].Floor < Decks[b].Floor;

    public double[] Box(Room r)
    {
        var dk = DeckOf(r);
        return Stacked ? [r.X0, r.X1, r.Z0, r.Z1, dk.Floor - dk.Height, dk.Floor] : [r.X0, r.X1, dk.Floor, dk.Ceiling, r.Z0, r.Z1];
    }

    /// <summary>Two decks one above the other with no deck between (a shaft can join them).</summary>
    public bool Neighbours(int a, int b)
    {
        if (a == b) return false;
        var (lo, hi) = Decks[a].Floor < Decks[b].Floor ? (Decks[a], Decks[b]) : (Decks[b], Decks[a]);
        return !Decks.Any(k => k.Floor > lo.Floor && k.Floor < hi.Floor);
    }
    public List<HullBody> Hulls = [];
    public List<Piece> Pieces = [];
    public List<Deck> Decks = [];
    public List<Room> Rooms = [];
    public List<HullJoin> Joins = [];
    public List<Riser> Risers = [];
    public List<RoomDoor> Doors = [];
    public List<HullWindow> Windows = [];
    public List<Passage> Passages = [];
    private int _next = 1;

    public int NewId() => _next++;

    public HullBody? Hull(int id) => Hulls.Find(h => h.Id == id);

    /// <summary>The body whose cladding h draws with: h's own, or the end of its chain of matched bodies.</summary>
    public HullBody CladSource(HullBody h)
    {
        var s = h;
        for (var i = 0; i < MaxHulls && s.CladFrom != 0; i++)
        {
            if (Hull(s.CladFrom) is not { } n || n == h)
                break;
            s = n;
        }
        return s;
    }

    /// <summary>Whether h may follow src's cladding: another body, whose own chain doesn't lead back to h.</summary>
    public bool CanMatchClad(HullBody h, HullBody src)
    {
        var s = src;
        for (var i = 0; i <= MaxHulls; i++)
        {
            if (s == h)
                return false;
            if (s.CladFrom == 0 || Hull(s.CladFrom) is not { } n)
                return true;
            s = n;
        }
        return false;
    }
    public Piece? PieceById(int id) => Pieces.Find(p => p.Id == id);
    public Room? RoomById(int id) => Rooms.Find(r => r.Id == id);
    public HullJoin? JoinById(int id) => Joins.Find(j => j.Id == id);

    // ---- JSON (System.Text.Json.Nodes only: no reflection, so it trims for the web page)

    public string ToJson()
    {
        var o = new JsonObject
        {
            ["format"] = "CallOfSlop ship design",
            ["version"] = Version,
            ["name"] = Name,
            ["decks orientation"] = Stacked ? "stacked" : "along",
            ["door placement"] = ManualDoors ? "manual" : "automatic",
            ["hulls"] = new JsonArray([.. Hulls.Select(h => { var j = h.ToJson(); j["id"] = h.Id; if (h.CladFrom != 0 && HullIndex(h.CladFrom) is var ci && ci >= 0) j["cladFrom"] = ci; return (JsonNode)j; })]),
            ["decks"] = new JsonArray([.. Decks.Select(d => { var j = new JsonObject { ["floor"] = R(d.Floor), ["height"] = R(d.Height) }; if (d.Hull != 0 && HullIndex(d.Hull) is var di && di >= 0) j["hull"] = di; return (JsonNode)j; })]),
            ["rooms"] = new JsonArray([.. Rooms.Select(r => { var j = r.ToJson(); j["id"] = r.Id; return (JsonNode)j; })]),
            ["pieces"] = new JsonArray([.. Pieces.Select(p => { var j = p.ToJson(); j["hull"] = HullIndex(p.Hull); j["id"] = p.Id; return (JsonNode)j; })]),
            ["doors"] = new JsonArray([.. Doors.Select(k => (JsonNode)new JsonObject { ["id"] = k.Id, ["a"] = Rooms.FindIndex(r => r.Id == k.A), ["b"] = Rooms.FindIndex(r => r.Id == k.B), ["t"] = R(k.T), ["kind"] = k.Kind, ["shape"] = k.Shape, ["w"] = R(k.W), ["h"] = R(k.H), ["sill"] = R(k.Sill), ["shape"] = k.Shape })]),
            ["windows"] = new JsonArray([.. Windows.Select(k => (JsonNode)new JsonObject { ["id"] = k.Id, ["room"] = Rooms.FindIndex(r => r.Id == k.Room), ["side"] = k.Side, ["t"] = R(k.T), ["w"] = R(k.W), ["h"] = R(k.H), ["sill"] = R(k.Sill), ["shape"] = k.Shape, ["gap"] = R(k.Gap), ["hole"] = R(k.Hole), ["depth"] = R(k.Depth), ["double"] = k.Double, ["frameOut"] = k.FrameOut, ["frameIn"] = k.FrameIn })]),
            ["risers"] = new JsonArray([.. Risers.Select(k => (JsonNode)k.ToJson())]),
            ["joins"] = new JsonArray([.. Joins.Select(k => { var j = k.ToJson(this); j["id"] = k.Id; return (JsonNode)j; })]),
            ["passages"] = new JsonArray([.. Passages.Select(k => (JsonNode)new JsonObject { ["id"] = k.Id, ["a"] = Rooms.FindIndex(r => r.Id == k.A), ["b"] = Rooms.FindIndex(r => r.Id == k.B), ["join"] = Joins.FindIndex(j => j.Id == k.Join), ["kind"] = k.Kind })]),
        };
        return o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads a design; throws <see cref="FormatException"/> with the reason when it can't.</summary>
    public static ShipDesign FromJson(string text)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(text); }
        catch (Exception e) { throw new FormatException("not JSON: " + e.Message); }
        if (root is not JsonObject o)
            throw new FormatException("not a ship design (no object)");
        var d = new ShipDesign { Name = (string?)o["name"] ?? "Ship", Stacked = (string?)o["decks orientation"] == "stacked", ManualDoors = (string?)o["door placement"] == "manual" };
        foreach (var h in o["hulls"]?.AsArray() ?? [])
            if (h is JsonObject ho && d.Hulls.Count < MaxHulls)
                d.Hulls.Add(HullBody.FromJson(ho, d.Take(ho)));
        // A body's matched cladding names the other by its place in the list; one that would close a loop is dropped.
        var hl = o["hulls"]?.AsArray().OfType<JsonObject>().Take(d.Hulls.Count).ToList() ?? [];
        for (var i = 0; i < hl.Count; i++)
            if ((int?)hl[i]["cladFrom"] is int ci && ci >= 0 && ci < d.Hulls.Count && d.CanMatchClad(d.Hulls[i], d.Hulls[ci]))
                d.Hulls[i].CladFrom = d.Hulls[ci].Id;
        foreach (var k in o["decks"]?.AsArray() ?? [])
            if (k is JsonObject ko && d.Decks.Count < MaxDecks)
                d.Decks.Add(new Deck { Floor = F(ko["floor"], 0.25), Height = Math.Clamp(F(ko["height"], 3), 2.2, 8),
                    Hull = (int?)ko["hull"] is int dh && dh >= 0 && dh < d.Hulls.Count ? d.Hulls[dh].Id : 0 });
        foreach (var r in o["rooms"]?.AsArray() ?? [])
            if (r is JsonObject ro && d.Rooms.Count < MaxRooms)
                d.Rooms.Add(Room.FromJson(ro, d.Take(ro)));
        foreach (var p in o["pieces"]?.AsArray() ?? [])
            if (p is JsonObject po && d.Pieces.Count < MaxPieces)
            {
                var pc = Piece.FromJson(po, d.Take(po));
                // A piece names its hull by its place in the list (ids are made fresh on reading).
                var hi = (int?)po["hull"] ?? 0;
                if (hi < 0 || hi >= d.Hulls.Count || PieceKinds.Find(pc.Kind) is null)
                    continue;
                pc.Hull = d.Hulls[hi].Id;
                d.Pieces.Add(pc);
            }
        foreach (var j in o["joins"]?.AsArray() ?? [])
            if (j is JsonObject jo && d.Joins.Count < MaxJoins && HullJoin.FromJson(d, jo, d.Take(jo)) is { } hj)
                d.Joins.Add(hj);
        foreach (var j in o["risers"]?.AsArray() ?? [])
            if (j is JsonObject ro && d.Risers.Count < MaxRisers)
                d.Risers.Add(Riser.FromJson(ro, d.Take(ro)));
        d.Risers.RemoveAll(k => k.From < 0 || k.To < 0 || k.From >= d.Decks.Count || k.To >= d.Decks.Count || k.From == k.To);
        d.Rooms.RemoveAll(r => r.Deck < 0 || r.Deck >= d.Decks.Count);
        foreach (var j in o["windows"]?.AsArray() ?? [])
            if (j is JsonObject wo && d.Windows.Count < MaxRooms * 2 && (int?)wo["room"] is int ri && ri >= 0 && ri < d.Rooms.Count)
                d.Windows.Add(new HullWindow { Id = d.Take(wo), Room = d.Rooms[ri].Id, Side = HullWindow.Sides.Contains((string?)wo["side"]) ? (string)wo["side"]! : "x0",
                    T = Math.Clamp(F(wo["t"], 0.5), 0, 1), W = Math.Clamp(F(wo["w"], 1.5), 0.3, 40), H = Math.Clamp(F(wo["h"], 1), 0.3, 6), Sill = Math.Clamp(F(wo["sill"], 0.9), 0, 6), Shape = HullWindow.Shapes.Contains((string?)wo["shape"]) ? (string)wo["shape"]! : "Rect",
                    Gap = Math.Clamp(F(wo["gap"], -1), -1, 2), Hole = Math.Clamp(F(wo["hole"], 1), 0.5, 2.5), Depth = Math.Clamp(F(wo["depth"], 0.2), 0.05, 1), Double = (bool?)wo["double"] ?? true,
                    FrameOut = (bool?)wo["frameOut"] ?? true, FrameIn = (bool?)wo["frameIn"] ?? true });
        // Doors name their rooms by place in the list (room ids are made fresh on reading).
        foreach (var j in o["doors"]?.AsArray() ?? [])
            if (j is JsonObject dob && d.Doors.Count < MaxRooms * 2)
            {
                var (a, b) = ((int?)dob["a"] ?? -1, (int?)dob["b"] ?? -1);
                if (a >= 0 && b >= 0 && a < d.Rooms.Count && b < d.Rooms.Count && a != b)
                    d.Doors.Add(new RoomDoor { Id = d.Take(dob), A = d.Rooms[a].Id, B = d.Rooms[b].Id, T = Math.Clamp(F(dob["t"], 0.5), 0, 1),
                        Kind = (string?)dob["kind"] == "Window" ? "Window" : "Door", Shape = RoomDoor.Shapes.Contains((string?)dob["shape"]) ? (string)dob["shape"]! : "Rect",
                        W = Math.Clamp(F(dob["w"], (string?)dob["kind"] == "Window" ? 1.2 : 0.9), 0.3, 6), H = Math.Clamp(F(dob["h"], (string?)dob["kind"] == "Window" ? 0.8 : 2.1), 0.3, 3), Sill = Math.Clamp(F(dob["sill"], 1), 0, 3) });
            }
        // Passages through an arm name their rooms and their arm by place in the list.
        foreach (var j in o["passages"]?.AsArray() ?? [])
            if (j is JsonObject po && d.Passages.Count < MaxPassages)
            {
                var (a, b, ji) = ((int?)po["a"] ?? -1, (int?)po["b"] ?? -1, (int?)po["join"] ?? -1);
                if (a >= 0 && b >= 0 && a < d.Rooms.Count && b < d.Rooms.Count && a != b)
                    d.Passages.Add(new Passage { Id = d.Take(po), A = d.Rooms[a].Id, B = d.Rooms[b].Id, Join = ji >= 0 && ji < d.Joins.Count ? d.Joins[ji].Id : 0, Kind = Passage.Kinds.Contains((string?)po["kind"]) ? (string)po["kind"]! : "Lift" });
            }
        if (d.Hulls.Count == 0)
            throw new FormatException("a ship design needs at least one hull");
        return d;
    }

    /// <summary>The id a saved thing had (kept, so the page's selection outlives an undo), or a new one.</summary>
    private int Take(JsonObject o)
    {
        var id = (int?)o["id"] ?? 0;
        if (id <= 0 || Hulls.Any(h => h.Id == id) || Rooms.Any(r => r.Id == id) || Pieces.Any(p => p.Id == id) || Joins.Any(j => j.Id == id) || Risers.Any(k => k.Id == id) || Doors.Any(k => k.Id == id) || Windows.Any(k => k.Id == id) || Passages.Any(k => k.Id == id))
            return NewId();
        _next = Math.Max(_next, id + 1);
        return id;
    }

    internal int HullIndex(int id) => Hulls.FindIndex(h => h.Id == id);

    internal static double F(JsonNode? n, double or) => n is null ? or : (double)n;
    internal static double R(double v) => Exact ? v : Math.Round(v, 3);
    internal static double R4(double v) => Exact ? v : Math.Round(v, 4);

    /// <summary>While set, ToJson writes every number whole (not rounded), for the page's cache keys: a value moved under a millimetre
    /// (or a design read back rounded, as an undo does) must not find the mesh made from the other.</summary>
    [ThreadStatic] internal static bool Exact;

    /// <summary>Text made with <see cref="Exact"/> set.</summary>
    internal static string Whole(Func<string> make)
    {
        var was = Exact;
        Exact = true;
        try { return make(); }
        finally { Exact = was; }
    }
    internal static string S(double v) => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);
}

/// <summary>A deck: a floor's top over the whole ship and the clear height above it.</summary>
public sealed class Deck
{
    public double Floor = 0.25, Height = 3.0;
    /// <summary>The body it is in (Karl, 2026-11-26: decks and rooms in a side pod or a nacelle): its rooms clip to that body's skin; 0, whichever body a room's floor is in.</summary>
    public int Hull;
    public double Ceiling => Floor + Height;
}

/// <summary>A room: a box on a deck (world x and z). Its kind says what it is for; a cargo hold is a room of kind Hold.</summary>
public sealed class Room
{
    public int Id;
    public int Deck;
    public double X0, X1, Z0, Z1;
    public string Kind = "Cabin";
    public string Label = "";
    public int Group; // segments of one room share a group (not 0): open to each other, no door between (Karl, 2026-11-24)
    public double Below; // metres its floor goes down through the deck under it (a room merged across decks, owned by the upper)
    public double Height; // its own clear height (0: its deck's), so it can rise through the decks above (Karl, 2026-11-24)
    public double[]? Want; // the plan it was drawn at, while a deck move has shrunk it to fit (it grows back to this when it can)
    public List<RoomExt> Exts = []; // the room grown out of a wall or up through its ceiling, in a shape (Karl, 2026-11-24)
    public bool RunAft; // the same at its aft end, into the stern (Karl, 2026-11-24: an option for the rear as well)
    public bool RunOn; // with ToHull: its open ends run on into the nose or the stern while there is floor (Karl, 2026-11-24), a player's choice
    public double WallThick = 0.1; // its walls' shell, inside its box (Karl, 2026-11-25; outward before)
    public const double WallThickMax = 0.3;
    // Its interior cladding (the user's corridor references, 2026-11-24): a style, the ribs' spacing, two colours and its lights.
    public static readonly string[] InteriorStyles = ["Off", "Clean", "Industrial"];
    public string Interior = "Off";
    public double RibSpacing = 2;
    public const double RibSpacingMin = 1, RibSpacingMax = 4;
    public string PanelColour = "#d9dee3";
    public string TrimColour = "#e0a526";
    public bool Lights = true;
    /// <summary>The floor's tiles: the plate layer's Plates or its Hex honeycomb.</summary>
    public static readonly string[] FloorKinds = ["Plates", "Hex", "Plain", "Diamond", "Grating", "Tiles", "Rubber", "Wood", "Hazard"];
    /// <summary>Its walls' relief in Room view when it has no interior cladding (Karl, 2026-11-25).</summary>
    public static readonly string[] WallStyles = ["Plain", "Panels", "Ribbed", "Padded", "Grate", "Conduit", "Hex", "Industrial", "Corridor", "Zero Day"];
    public string WallStyle = "Plain";
    /// <summary>A corridor's own lining and grating (Karl, 2026-11-25): a room of kind Corridor whose walls and floor are still the plain defaults takes them. Styles the user set are kept.</summary>
    public void KindDefaults()
    {
        if (Kind != "Corridor")
            return;
        if (WallStyle == "Plain") WallStyle = "Corridor";
        if (Floor == "Plates") Floor = "Grating";
    }
    public string WallColour = ""; // empty: its kind's colour, darkened
    public int Seed; // varies the random parts of its walls and floor
    public const int SeedMax = 999;
    public string Floor = "Plates";
    public static string Colour(string? v, string or) => v is { Length: 7 } && v[0] == '#' && v.Skip(1).All(Uri.IsHexDigit) ? v.ToLowerInvariant() : or;
    public bool ToHull; // its sides go out to the skin (lined 6 cm in) where no room is beyond them, so it follows the hull's shape

    public double Area => (X1 - X0) * (Z1 - Z0);

    public JsonObject ToJson() => new()
    {
        ["deck"] = Deck, ["x0"] = ShipDesign.R(X0), ["x1"] = ShipDesign.R(X1), ["z0"] = ShipDesign.R(Z0), ["z1"] = ShipDesign.R(Z1),
        ["kind"] = Kind, ["label"] = Label, ["toHull"] = ToHull, ["runOn"] = RunOn, ["runAft"] = RunAft, ["height"] = ShipDesign.R(Height), ["below"] = ShipDesign.R(Below), ["group"] = Group,
        ["exts"] = new JsonArray([.. Exts.Select(e => (JsonNode)new JsonObject { ["side"] = e.Side, ["shape"] = e.Shape, ["depth"] = ShipDesign.R(e.Depth), ["width"] = ShipDesign.R(e.Width), ["t"] = ShipDesign.R(e.T) })]),
        ["wallThick"] = ShipDesign.R(WallThick),
        ["interior"] = Interior, ["ribSpacing"] = ShipDesign.R(RibSpacing), ["panelColour"] = PanelColour, ["trimColour"] = TrimColour, ["lights"] = Lights, ["floor"] = Floor, ["wallStyle"] = WallStyle, ["wallColour"] = WallColour, ["seed"] = Seed,
    };

    public static Room FromJson(JsonObject o, int id)
    {
        var r = new Room
        {
            Id = id, Deck = (int?)o["deck"] ?? 0, X0 = ShipDesign.F(o["x0"], -1), X1 = ShipDesign.F(o["x1"], 1), Z0 = ShipDesign.F(o["z0"], 0),
            Z1 = ShipDesign.F(o["z1"], 2), Kind = (string?)o["kind"] ?? "Cabin", Label = (string?)o["label"] ?? "", ToHull = (bool?)o["toHull"] ?? false, RunOn = (bool?)o["runOn"] ?? false, RunAft = (bool?)o["runAft"] ?? false,
            Height = Math.Clamp(ShipDesign.F(o["height"], 0), 0, 40), Below = Math.Clamp(ShipDesign.F(o["below"], 0), 0, 40), Group = Math.Max(0, (int?)o["group"] ?? 0),
        };
        if (RoomKinds.Find(r.Kind) is null)
            r.Kind = "Cabin";
        foreach (var j in o["exts"]?.AsArray() ?? [])
            if (j is JsonObject eo && r.Exts.Count < RoomExt.Max && RoomExt.Sides.Contains((string?)eo["side"]))
            {
                var e = new RoomExt { Side = (string)eo["side"]!, Shape = (string?)eo["shape"] ?? "Box", Depth = Math.Clamp(ShipDesign.F(eo["depth"], 1.5), 0.25, 12),
                    Width = Math.Clamp(ShipDesign.F(eo["width"], 2), 0.5, 40), T = Math.Clamp(ShipDesign.F(eo["t"], 0.5), 0, 1) };
                if (!RoomExt.ShapesFor(e.Side).Contains(e.Shape))
                    e.Shape = RoomExt.ShapesFor(e.Side)[0];
                r.Exts.Add(e);
            }
        r.WallThick = Math.Clamp(ShipDesign.F(o["wallThick"], 0.1), 0, WallThickMax);
        r.Interior = InteriorStyles.FirstOrDefault(k => string.Equals(k, (string?)o["interior"], StringComparison.OrdinalIgnoreCase)) ?? "Off";
        r.RibSpacing = Math.Clamp(ShipDesign.F(o["ribSpacing"], 2), RibSpacingMin, RibSpacingMax);
        r.PanelColour = Colour((string?)o["panelColour"], "#d9dee3");
        r.TrimColour = Colour((string?)o["trimColour"], "#e0a526");
        r.Lights = (bool?)o["lights"] ?? true;
        r.Floor = FloorKinds.FirstOrDefault(k => string.Equals(k, (string?)o["floor"], StringComparison.OrdinalIgnoreCase)) ?? "Plates";
        r.WallStyle = WallStyles.FirstOrDefault(k => string.Equals(k, (string?)o["wallStyle"], StringComparison.OrdinalIgnoreCase)) ?? "Plain";
        r.WallColour = (string?)o["wallColour"] is { Length: > 0 } wc ? Colour(wc, "") : "";
        r.Seed = Math.Clamp((int?)o["seed"] ?? 0, 0, SeedMax);
        // A saved design that set its styles keeps them; only an older one with neither key takes the kind's.
        if (o["wallStyle"] is null && o["floor"] is null)
            r.KindDefaults();
        r.Normalise();
        return r;
    }

    /// <summary>Orders the corners and keeps the room at least a metre each way.</summary>
    public void Normalise()
    {
        if (X1 < X0) (X0, X1) = (X1, X0);
        if (Z1 < Z0) (Z0, Z1) = (Z1, Z0);
        if (X1 - X0 < 1) X1 = X0 + 1;
        if (Z1 - Z0 < 1) Z1 = Z0 + 1;
    }
}

/// <summary>
/// A piece on a hull's skin: where on the skin (a station position <see cref="Z"/> along the hull, <see cref="T"/> round the half profile from
/// the keel's middle, 0, to the spine's, 1, on side <see cref="Side"/>), or on an end (<see cref="Surface"/> Fore or Aft, at <see cref="U"/>,
/// <see cref="V"/> across it). Mirror puts a twin on the other side.
/// </summary>
public sealed class Piece
{
    public int Id;
    public string Kind = "";
    public int Hull;
    public int Copy; // which copy of a mirrored hull it is on (0 or 1)
    public string Surface = "skin"; // skin, fore, aft
    public double Z, T, U, V;
    public int Side = 1;
    public bool Mirror; // a twin across its own hull copy's centreline
    public bool Pair = true; // on a mirrored pair of bodies, the same piece on the other body too, mirrored across the ship (Karl, 2026-11-24)
    public double Recess; // metres the piece sits back into the wall, behind its hole (Karl, 2026-11-24)
    public bool RecessClad, CladHull = true, RevealClad; // its recess's walls clad, in its hull's cladding colour; a window's reveal lined (DesignRecess, Karl 2026-11-27)
    public double Clad; // metres of cladding plate round a socket's frame on the skin (Karl, 2026-11-24: sized with the window)
    public double Size = 1; // its scale: a socket's opening and the piece drawn grow with it (Karl, 2026-11-24)
    public double Turn; // degrees about the skin's normal
    public int Room; // the room a socket opens into, picked by the player (0: the nearest of the kind it needs)
    public double Gap = -1; // metres the hull cladding keeps clear of its cutout; below 0: worked out from the plates' depth (Karl, 2026-11-25)
    public double Hole = 1; // the cutout (skin hole, frame, ring) scaled across the skin (Karl, 2026-11-25)
    public double Depth = 0.2; // a window's: how far in its inner pane sits behind the outer one (Karl, 2026-11-25)
    public bool Double = true; // a window's: double glazed (an outer and an inner pane, a reveal between)
    // An airlock's chamber (Karl, 2026-11-26; DesignDocking.cs): its width (along), height (across the hatch) and depth in, its doors' width and type.
    public double AirW = PieceDocking.AirW, AirH = PieceDocking.AirH, AirD = PieceDocking.AirD, DoorW = PieceDocking.DoorW;
    public string Door = "sliding";
    public int Feed; // an ammo socket's gun (a piece id; 0: the nearest weapon)
    // Hangar pieces' own (DesignHangar, Karl 2026-11-26): a bay's door size and type; a lift's or ramp deck's size and travel; a fitting's bay and spot in it.
    public double BayW = 20, BayH = 8, BayD = 20; // a bay's own volume across, tall and deep (Karl 2026-11-27; DesignHangar.Of)
    public double BayDoorW = 5, BayDoorH = 3, W = 5, L = 5, Travel = 3, Along, Across;
    public string BayDoorType = DesignHangar.DefDoor;
    public int ShutterRollers = 1, ShutterSlats = 4; // a shutter bay door's rollers (1 or 2) and slats on each (DesignHangar.Slats)
    public string ShutterSide = "Fore"; // the side its one roller is at, or the axis two split across
    public bool BayMatch; // a bay as wide and tall as its door opening (DesignHangar.Of), Karl 2026-11-27
    public string BayFloor = "Fixed"; // a bay's floor: Fixed or Lift-out (DesignHangar.FloorBox)
    public int Link;
    public string Fit = ""; // a weapon mount's weapon (DesignWeaponMounts.cs; "": empty)
    // A floodlight's (DesignFittings, Karl 2026-11-26): its aim off the skin's normal and about it (degrees), and its beam's colour.
    public double Pitch = DesignFittings.DefPitch, Yaw = DesignFittings.DefYaw;
    public string Beam = DesignFittings.DefBeam;
    public string Role = DesignSeats.DefRole; // a seat's (DesignSeats, Karl 2026-11-26): Helm, Captain, Gunner and the rest
    public double Extend = DesignRamp.DefExtend; // a boarding ramp's extension's run, metres (DesignRamp, Karl 2026-11-27)
    // A light bar's (DesignLightBars, Karl 2026-11-27): its length (m) and brightness (0 to 1; its colour is Beam); a room bar's mount and its height over the floor (m).
    public double BarLen = DesignLightBars.DefLen, Bright = DesignLightBars.DefBright, BarUp = DesignLightBars.DefUp;
    public string BarClad = DesignLightBars.DefClad; // a hull bar: how it meets the cladding (Cut, Flush, Over: DesignLightBars.Lift, Cuts)
    public string BarOn = "Wall";
    public double RampW = DesignRamp.DefWidth, RampL = DesignRamp.DefLength; // a boarding ramp's hatch across and along, metres (DesignRamp)
    public int Segments = DesignRamp.DefSegments; // a boarding ramp's telescoping sections (DesignRamp)
    public double RampAngle; // a boarding ramp's set angle, degrees; 0: the automatic one (DesignRamp.Angle)
    // A stand-off armour plate's (DesignArmour, Karl 2026-11-27): width along, height round, thickness and stand-off (m); its outer face's cladding and panel size.
    public double ArmW = DesignArmour.DefW, ArmH = DesignArmour.DefH, ArmT = DesignArmour.DefT, ArmOff = DesignArmour.DefOff, ArmCladSize = DesignArmour.DefCladSize;
    public string ArmClad = DesignArmour.DefClad, ArmStruts = DesignArmour.DefStruts;
    public double[] ArmTop = new double[DesignArmour.EdgePoints], ArmBot = new double[DesignArmour.EdgePoints]; // its outline's top and bottom edges (DesignArmour.EdgeAt)
    public double ArmFollow = 1; // 1: it follows the hull's shape; 0: flat
    public int ArmButton; // the utilities console button that works it (1-8; 0: none, F on the plate only)

    public JsonObject ToJson() => DesignArmour.Write(this, DesignRecess.Write(this, DesignLightBars.Write(this, DesignRamp.Write(this, DesignSeats.Write(this, DesignFittings.Write(this, DesignHangar.Write(this, new()
    {
        ["kind"] = Kind, ["surface"] = Surface, ["z"] = ShipDesign.R(Z), ["t"] = ShipDesign.R4(T), ["u"] = ShipDesign.R(U), ["v"] = ShipDesign.R(V),
        ["side"] = Side, ["copy"] = Copy, ["mirror"] = Mirror, ["pair"] = Pair, ["size"] = ShipDesign.R(Size), ["recess"] = ShipDesign.R(Recess), ["clad"] = ShipDesign.R(Clad), ["turn"] = ShipDesign.R(Turn), ["room"] = Room,
        ["gap"] = ShipDesign.R(Gap), ["hole"] = ShipDesign.R(Hole), ["depth"] = ShipDesign.R(Depth), ["double"] = Double,
        ["airW"] = ShipDesign.R(AirW), ["airH"] = ShipDesign.R(AirH), ["airD"] = ShipDesign.R(AirD), ["doorW"] = ShipDesign.R(DoorW), ["door"] = Door, ["feed"] = Feed, ["fit"] = Fit,
    })))))));

    /// <summary>Reads a piece (its hull is set by the caller).</summary>
    public static Piece FromJson(JsonObject o, int id)
    {
        var p = DesignArmour.Read(o, DesignRecess.Read(o, DesignLightBars.Read(o, DesignRamp.Read(o, DesignSeats.Read(o, DesignFittings.Read(o, DesignHangar.Read(o, Read(o, id))))))));
        if ((string?)o["fit"] is { } fit && PieceWeapons.Fits(p.Kind, fit))
            p.Fit = fit;
        PieceWeapons.Init(p);
        return p;
    }

    private static Piece Read(JsonObject o, int id) => new()
    {
        Id = id, Kind = (string?)o["kind"] ?? "", Surface = (string?)o["surface"] ?? "skin", Z = ShipDesign.F(o["z"], 0), T = Math.Clamp(ShipDesign.F(o["t"], 0.5), 0, 1),
        U = ShipDesign.F(o["u"], 0), V = ShipDesign.F(o["v"], 0), Side = (int?)o["side"] is -1 ? -1 : 1, Copy = (int?)o["copy"] is 1 ? 1 : 0,
        Mirror = (bool?)o["mirror"] ?? false, Pair = (bool?)o["pair"] ?? true, Size = Math.Clamp(ShipDesign.F(o["size"], 1), 0.25, 4), Recess = Math.Clamp(ShipDesign.F(o["recess"], 0), 0, 2), Clad = Math.Clamp(ShipDesign.F(o["clad"], 0), 0, 2), Turn = ShipDesign.F(o["turn"], 0), Room = (int?)o["room"] ?? 0,
        Gap = Math.Clamp(ShipDesign.F(o["gap"], -1), -1, 2), Hole = Math.Clamp(ShipDesign.F(o["hole"], 1), 0.5, 2.5), Depth = Math.Clamp(ShipDesign.F(o["depth"], 0.2), 0.05, 1), Double = (bool?)o["double"] ?? true,
        AirW = Math.Clamp(ShipDesign.F(o["airW"], PieceDocking.AirW), 0.8, 6), AirH = Math.Clamp(ShipDesign.F(o["airH"], PieceDocking.AirH), 1.2, 6),
        AirD = Math.Clamp(ShipDesign.F(o["airD"], PieceDocking.AirD), 0.6, 6), DoorW = Math.Clamp(ShipDesign.F(o["doorW"], PieceDocking.DoorW), 0.6, 6),
        Door = Array.IndexOf(PieceDocking.Doors, (string?)o["door"]) >= 0 ? (string)o["door"]! : "sliding", Feed = (int?)o["feed"] ?? 0,
    };
}


/// <summary>A ladder or a lift between decks (Karl, 2026-11-24): at a plan point (x, z), from one deck to another, through
/// every deck between. It joins the rooms it stands in on each deck it stops at.</summary>
public sealed class Riser
{
    public int Id;
    public string Kind = "Ladder"; // Ladder or Lift
    public double X, Z;
    public int From, To;
    public int W = 2, L = 2; // a lift's car, whole metres across and along (Karl, 2026-11-24: sizes in steps of 1 m)

    /// <summary>Its footprint (half sizes across and along): a ladder's well, a lift's car.</summary>
    public (double Hx, double Hz) Half => Kind == "Lift" ? (W / 2.0, L / 2.0) : (0.4, 0.35);

    public JsonObject ToJson() => new()
    {
        ["id"] = Id, ["kind"] = Kind, ["x"] = ShipDesign.R(X), ["z"] = ShipDesign.R(Z), ["from"] = From, ["to"] = To, ["w"] = W, ["l"] = L,
    };

    public static Riser FromJson(JsonObject o, int id) => new()
    {
        Id = id, Kind = (string?)o["kind"] == "Lift" ? "Lift" : "Ladder", X = ShipDesign.F(o["x"], 0), Z = ShipDesign.F(o["z"], 0),
        From = (int?)o["from"] ?? 0, To = (int?)o["to"] ?? 1, W = Math.Clamp((int?)o["w"] ?? 2, 1, 8), L = Math.Clamp((int?)o["l"] ?? 2, 1, 8),
    };
}


/// <summary>A door placed between two rooms (Karl, 2026-11-24): where along the wall they share (T, 0 to 1). Rooms that
/// share a wall get a door in its middle without one; a placed door moves it.</summary>
public sealed class RoomDoor
{
    public int Id, A, B;
    public double T = 0.5;
    public string Kind = "Door"; // Door, or Window (Karl, 2026-11-24: windows between rooms, sized, in shapes)
    public string Shape = "Rect"; // a window's: Rect, Round, Oval, Arch
    public double W = 1.2, H = 0.8, Sill = 1.0;
    public static readonly string[] Shapes = ["Rect", "Round", "Oval", "Arch"];
    public bool Joins(Room a, Room b) => (A == a.Id && B == b.Id) || (A == b.Id && B == a.Id);
}


/// <summary>A window in the hull (Karl, 2026-11-24): on one wall of a room (Side: x0, x1, z0 or z1 of its plan) that is within
/// MaxGap of the skin, the glass laid on the skin itself so it follows the ship's contour. T is where along the wall its middle
/// sits (0 to 1, the window kept on the wall), W its width along the wall, H its height and Sill its foot over the floor.</summary>
public sealed class HullWindow
{
    public const double MaxGap = 1.0;
    public static readonly string[] Sides = ["x0", "x1", "z0", "z1"];
    public int Id, Room;
    public string Side = "x0";
    public double T = 0.5, W = 1.5, H = 1.0, Sill = 0.9;
    public string Shape = "Rect"; // its outline (Karl, 2026-11-24): Rect, Round, Oval, Arch or Hex
    public double Gap = -1, Hole = 1, Depth = 0.2; // cladding clearance (below 0: auto), the cut and boarding's scale, the inner pane's depth (Karl, 2026-11-25)
    public bool Double = true; // double glazed: an outer pane on the skin, an inner one Depth in, a reveal between
    // Its frames (Karl, 2026-11-27): the boarding round the glass on the skin (outside), and the lining and architrave round its wall's
    // opening (inside). Without one, the cladding on that side runs up to the glass's edge.
    public bool FrameOut = true, FrameIn = true;
    public static readonly string[] Shapes = ["Rect", "Round", "Oval", "Arch", "Hex"];

    /// <summary>A shaped window's outline round its middle, counter-clockwise, as (across, up) offsets, in RimPoints points evenly
    /// spaced along it. Every shape is convex, so a hole cut from it and a fan of glass from its middle both work.</summary>
    public const int RimPoints = 48;
    public static List<(double U, double V)> Rim(string shape, double hx, double hz)
    {
        var poly = new List<(double U, double V)>();
        switch (shape)
        {
            case "Round":
                for (var i = 0; i < RimPoints; i++) poly.Add((Math.Min(hx, hz) * Math.Cos(2 * Math.PI * i / RimPoints), Math.Min(hx, hz) * Math.Sin(2 * Math.PI * i / RimPoints)));
                return poly;
            case "Oval":
                for (var i = 0; i < RimPoints; i++) poly.Add((hx * Math.Cos(2 * Math.PI * i / RimPoints), hz * Math.Sin(2 * Math.PI * i / RimPoints)));
                return poly;
            case "Arch":
            {
                var rv = Math.Min(hx, 2 * hz); // the arch's rise
                poly.Add((-hx, -hz)); poly.Add((hx, -hz));
                for (var i = 0; i <= 16; i++) poly.Add((hx * Math.Cos(Math.PI * i / 16), hz - rv + rv * Math.Sin(Math.PI * i / 16)));
                break;
            }
            case "Hex":
                poly.AddRange([(hx, 0), (hx / 2, hz), (-hx / 2, hz), (-hx, 0), (-hx / 2, -hz), (hx / 2, -hz)]);
                break;
            default:
                poly.AddRange([(-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz)]);
                break;
        }
        // Evenly along the perimeter, so the glass fan's spokes are even.
        var len = new double[poly.Count + 1];
        for (var i = 0; i < poly.Count; i++) { var (a, b) = (poly[i], poly[(i + 1) % poly.Count]); len[i + 1] = len[i] + Math.Sqrt((b.U - a.U) * (b.U - a.U) + (b.V - a.V) * (b.V - a.V)); }
        var outp = new List<(double U, double V)>();
        for (int k = 0, e = 0; k < RimPoints; k++)
        {
            var t = len[^1] * k / RimPoints;
            while (e < poly.Count - 1 && len[e + 1] < t) e++;
            var (a, b) = (poly[e], poly[(e + 1) % poly.Count]);
            var f = len[e + 1] > len[e] ? (t - len[e]) / (len[e + 1] - len[e]) : 0;
            outp.Add((a.U + (b.U - a.U) * f, a.V + (b.V - a.V) * f));
        }
        return outp;
    }
}


/// <summary>A room grown out of one of its walls, or up through its ceiling (Karl, 2026-11-24: "expanding the room a certain
/// direction with more choices"). Out of a wall (Side x0, x1, z0, z1): an outline on plan, Width along the wall (its middle at T)
/// and Depth out, as high as the room: Box, Bay (angled sides), Hex (half a hexagon) or Round (an apse). Up ("up"): the ceiling
/// raised Depth in a profile across the room's narrower side, all along it: Vault (a barrel), Gable (pitched) or Hex (chamfered).</summary>
public sealed class RoomExt
{
    public const int Max = 6;
    public static readonly string[] Sides = ["x0", "x1", "z0", "z1", "up"];
    public static string[] ShapesFor(string side) => side == "up" ? ["Vault", "Gable", "Hex"] : ["Box", "Bay", "Hex", "Round"];
    public string Side = "z0", Shape = "Box";
    public double Depth = 1.5, Width = 2, T = 0.5;

    /// <summary>Its outline: (across, out) for a wall, from one end of its base round to the other; the base is the room's wall
    /// (or, up, its ceiling) and is not listed. Convex, so a fan from its middle fills it.</summary>
    public List<(double A, double B)> Outline(double w)
    {
        double h = w / 2, d = Depth;
        switch (Shape)
        {
            case "Bay": case "Gable" when Side == "up":
                return Shape == "Gable" ? [(-h, 0), (0, d), (h, 0)] : [(-h, 0), (-h / 2, d), (h / 2, d), (h, 0)];
            case "Hex":
                return [(-h, 0), (-h, d * 0.5), (-h / 2, d), (h / 2, d), (h, d * 0.5), (h, 0)];
            case "Round": case "Vault":
            {
                var o = new List<(double, double)>();
                for (var i = 0; i <= 12; i++)
                {
                    var t = Math.PI * i / 12;
                    o.Add((-h * Math.Cos(t), d * Math.Sin(t)));
                }
                return o;
            }
            default:
                return [(-h, 0), (-h, d), (h, d), (h, 0)];
        }
    }
}


/// <summary>A way between rooms in two bodies through the arm that joins them (Karl, 2026-11-26): a lift car on a track, or a walkway,
/// along the strut's curved spine from a door in one room's wall to a door in the other's. Join names the arm (0: the one between
/// the rooms' bodies).</summary>
public sealed class Passage
{
    public static readonly string[] Kinds = ["Lift", "Walkway"];
    /// <summary>The clear width a person needs inside the arm.</summary>
    public const double MinClear = 1.2;
    public int Id, A, B, Join;
    public string Kind = "Lift";
    public bool Joins(int a, int b) => (A == a && B == b) || (A == b && B == a);
}
