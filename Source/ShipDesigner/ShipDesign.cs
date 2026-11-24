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
    public const int MaxHulls = 12, MaxStations = 24, MaxPieces = 200, MaxRooms = 120, MaxDecks = 24, MaxJoins = 40, MaxRisers = 40;

    public string Name = "New ship";

    /// <summary>
    /// How the decks lie. Along (false): floors level, decks along the length, as the Zero Day's (a deck's Floor is a height). Stacked
    /// (true): floors across the ship facing the bow, so thrust is down, as a tower ship's (a deck's Floor is a z, its rooms' up is towards the
    /// bow, and a room's X runs across and its Z0..Z1 hold its height range y).
    /// </summary>
    public bool Stacked;

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
        return new Deck { Floor = dk.Floor - (Stacked ? -r.Below : r.Below), Height = h + r.Below };
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
    private int _next = 1;

    public int NewId() => _next++;

    public HullBody? Hull(int id) => Hulls.Find(h => h.Id == id);
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
            ["hulls"] = new JsonArray([.. Hulls.Select(h => { var j = h.ToJson(); j["id"] = h.Id; return (JsonNode)j; })]),
            ["decks"] = new JsonArray([.. Decks.Select(d => (JsonNode)new JsonObject { ["floor"] = R(d.Floor), ["height"] = R(d.Height) })]),
            ["rooms"] = new JsonArray([.. Rooms.Select(r => { var j = r.ToJson(); j["id"] = r.Id; return (JsonNode)j; })]),
            ["pieces"] = new JsonArray([.. Pieces.Select(p => { var j = p.ToJson(); j["hull"] = HullIndex(p.Hull); j["id"] = p.Id; return (JsonNode)j; })]),
            ["doors"] = new JsonArray([.. Doors.Select(k => (JsonNode)new JsonObject { ["id"] = k.Id, ["a"] = Rooms.FindIndex(r => r.Id == k.A), ["b"] = Rooms.FindIndex(r => r.Id == k.B), ["t"] = R(k.T), ["kind"] = k.Kind, ["shape"] = k.Shape, ["w"] = R(k.W), ["h"] = R(k.H), ["sill"] = R(k.Sill) })]),
            ["windows"] = new JsonArray([.. Windows.Select(k => (JsonNode)new JsonObject { ["id"] = k.Id, ["room"] = Rooms.FindIndex(r => r.Id == k.Room), ["side"] = k.Side, ["t"] = R(k.T), ["w"] = R(k.W), ["h"] = R(k.H), ["sill"] = R(k.Sill) })]),
            ["risers"] = new JsonArray([.. Risers.Select(k => (JsonNode)k.ToJson())]),
            ["joins"] = new JsonArray([.. Joins.Select(k => { var j = k.ToJson(this); j["id"] = k.Id; return (JsonNode)j; })]),
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
        var d = new ShipDesign { Name = (string?)o["name"] ?? "Ship", Stacked = (string?)o["decks orientation"] == "stacked" };
        foreach (var h in o["hulls"]?.AsArray() ?? [])
            if (h is JsonObject ho && d.Hulls.Count < MaxHulls)
                d.Hulls.Add(HullBody.FromJson(ho, d.Take(ho)));
        foreach (var k in o["decks"]?.AsArray() ?? [])
            if (k is JsonObject ko && d.Decks.Count < MaxDecks)
                d.Decks.Add(new Deck { Floor = F(ko["floor"], 0.25), Height = Math.Clamp(F(ko["height"], 3), 2.2, 8) });
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
                    T = Math.Clamp(F(wo["t"], 0.5), 0, 1), W = Math.Clamp(F(wo["w"], 1.5), 0.3, 40), H = Math.Clamp(F(wo["h"], 1), 0.3, 6), Sill = Math.Clamp(F(wo["sill"], 0.9), 0, 6) });
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
        if (d.Hulls.Count == 0)
            throw new FormatException("a ship design needs at least one hull");
        return d;
    }

    /// <summary>The id a saved thing had (kept, so the page's selection outlives an undo), or a new one.</summary>
    private int Take(JsonObject o)
    {
        var id = (int?)o["id"] ?? 0;
        if (id <= 0 || Hulls.Any(h => h.Id == id) || Rooms.Any(r => r.Id == id) || Pieces.Any(p => p.Id == id) || Joins.Any(j => j.Id == id) || Risers.Any(k => k.Id == id) || Doors.Any(k => k.Id == id) || Windows.Any(k => k.Id == id))
            return NewId();
        _next = Math.Max(_next, id + 1);
        return id;
    }

    internal int HullIndex(int id) => Hulls.FindIndex(h => h.Id == id);

    internal static double F(JsonNode? n, double or) => n is null ? or : (double)n;
    internal static double R(double v) => Math.Round(v, 3);
    internal static string S(double v) => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);
}

/// <summary>A deck: a floor's top over the whole ship and the clear height above it.</summary>
public sealed class Deck
{
    public double Floor = 0.25, Height = 3.0;
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
    public bool ToHull; // its sides go out to the skin (lined 6 cm in) where no room is beyond them, so it follows the hull's shape

    public double Area => (X1 - X0) * (Z1 - Z0);

    public JsonObject ToJson() => new()
    {
        ["deck"] = Deck, ["x0"] = ShipDesign.R(X0), ["x1"] = ShipDesign.R(X1), ["z0"] = ShipDesign.R(Z0), ["z1"] = ShipDesign.R(Z1),
        ["kind"] = Kind, ["label"] = Label, ["toHull"] = ToHull, ["height"] = ShipDesign.R(Height), ["below"] = ShipDesign.R(Below), ["group"] = Group,
    };

    public static Room FromJson(JsonObject o, int id)
    {
        var r = new Room
        {
            Id = id, Deck = (int?)o["deck"] ?? 0, X0 = ShipDesign.F(o["x0"], -1), X1 = ShipDesign.F(o["x1"], 1), Z0 = ShipDesign.F(o["z0"], 0),
            Z1 = ShipDesign.F(o["z1"], 2), Kind = (string?)o["kind"] ?? "Cabin", Label = (string?)o["label"] ?? "", ToHull = (bool?)o["toHull"] ?? false,
            Height = Math.Clamp(ShipDesign.F(o["height"], 0), 0, 40), Below = Math.Clamp(ShipDesign.F(o["below"], 0), 0, 40), Group = Math.Max(0, (int?)o["group"] ?? 0),
        };
        if (RoomKinds.Find(r.Kind) is null)
            r.Kind = "Cabin";
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
    public double Clad; // metres of cladding plate round a socket's frame on the skin (Karl, 2026-11-24: sized with the window)
    public double Size = 1; // its scale: a socket's opening and the piece drawn grow with it (Karl, 2026-11-24)
    public double Turn; // degrees about the skin's normal
    public int Room; // the room a socket opens into, picked by the player (0: the nearest of the kind it needs)

    public JsonObject ToJson() => new()
    {
        ["kind"] = Kind, ["surface"] = Surface, ["z"] = ShipDesign.R(Z), ["t"] = Math.Round(T, 4), ["u"] = ShipDesign.R(U), ["v"] = ShipDesign.R(V),
        ["side"] = Side, ["copy"] = Copy, ["mirror"] = Mirror, ["pair"] = Pair, ["size"] = ShipDesign.R(Size), ["recess"] = ShipDesign.R(Recess), ["clad"] = ShipDesign.R(Clad), ["turn"] = ShipDesign.R(Turn), ["room"] = Room,
    };

    /// <summary>Reads a piece (its hull is set by the caller).</summary>
    public static Piece FromJson(JsonObject o, int id) => new()
    {
        Id = id, Kind = (string?)o["kind"] ?? "", Surface = (string?)o["surface"] ?? "skin", Z = ShipDesign.F(o["z"], 0), T = Math.Clamp(ShipDesign.F(o["t"], 0.5), 0, 1),
        U = ShipDesign.F(o["u"], 0), V = ShipDesign.F(o["v"], 0), Side = (int?)o["side"] is -1 ? -1 : 1, Copy = (int?)o["copy"] is 1 ? 1 : 0,
        Mirror = (bool?)o["mirror"] ?? false, Pair = (bool?)o["pair"] ?? true, Size = Math.Clamp(ShipDesign.F(o["size"], 1), 0.25, 4), Recess = Math.Clamp(ShipDesign.F(o["recess"], 0), 0, 2), Clad = Math.Clamp(ShipDesign.F(o["clad"], 0), 0, 2), Turn = ShipDesign.F(o["turn"], 0), Room = (int?)o["room"] ?? 0,
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
}
