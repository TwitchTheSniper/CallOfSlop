using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The seat socket (Karl, 2026-11-26: crew roles): a socket that takes a seat, set with its <see cref="Piece.Role"/>. Dropped on the skin by a
/// room (or linked to one, as any socket), the seat stands on that room's floor at the point of the room nearest where it was dropped, facing
/// the bow (turned by the piece's Turn). In the game it is a seat on its own element part (DesignParts.cs): the Helm flies the ship, the
/// Captain's seat is the command station, and the rest are the cockpit stations' kinds (CockpitStations.cs) where one matches.
/// All the seat's geometry, checks, JSON and page fields are here, so the rest of the designer only calls in.
/// </summary>
public static class DesignSeats
{
    public const string Kind = "Seat";

    /// <summary>The roles a seat can have, the first the default. The game's station for each is <see cref="StationOf"/>.</summary>
    public static readonly string[] Roles = ["Helm", "Captain", "Gunner", "Engineer", "Navigation", "Sensors", "Comms", "Passenger"];

    public const string DefRole = "Helm";

    /// <summary>How far in from the room's walls the seat's middle stays (m).</summary>
    public const double Inset = 0.6;

    public static bool IsSeat(string kind) => kind == Kind;

    public static readonly PieceKind[] Kinds =
    [
        new(Kind, "Crew", "skin", "seat", "any room", "A crew seat with its console. Drop it on the skin by a room (or link it to one): it stands on that room's floor. Set its role: the Helm flies the ship.", Draw),
    ];

    /// <summary>The game's station for a role (CockpitStations' order: 0 Auto, 1 Command, 2 Navigation, 3 Sensors, 4 Damage control, 5 Comms) and whether it is the helm.</summary>
    public static (int Station, bool Helm) StationOf(string role) => role switch
    {
        "Helm" => (0, true),
        "Captain" => (1, false),
        "Navigation" => (2, false),
        "Sensors" => (3, false),
        "Engineer" => (4, false), // (an engineer's seat is damage control's: the works stations are stood at)
        "Comms" => (5, false),
        _ => (0, false), // a gunner's seat (a gun seat when a gun is linked to it) and a passenger's couch
    };

    /// <summary>A new seat: the helm when the ship has none yet, else a passenger's.</summary>
    public static void Init(ShipDesign d, Piece p)
    {
        if (IsSeat(p.Kind))
            p.Role = d.Pieces.Any(q => q != p && IsSeat(q.Kind) && q.Role == "Helm") ? "Passenger" : DefRole;
    }

    public static JsonObject Defaults() => new() { ["role"] = DefRole };

    /// <summary>A seat's JSON fields (only a seat's: every other piece's JSON stays as it was, so its design's hash does).</summary>
    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (IsSeat(p.Kind))
            o["role"] = p.Role;
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        p.Role = (string?)o["role"] is { } r && Array.IndexOf(Roles, r) >= 0 ? r : DefRole;
        return p;
    }

    /// <summary>The page's setPiece field; an error text, or empty.</summary>
    public static string Set(Piece p, JsonObject o)
    {
        if ((string?)o["role"] is not { } r)
            return "";
        if (Array.IndexOf(Roles, r) < 0)
            return $"a seat's role is one of {string.Join(", ", Roles)}";
        p.Role = r;
        return "";
    }

    /// <summary>The page's seat fields: its role.</summary>
    public static string SceneJson(Piece p) => IsSeat(p.Kind) ? ",\"role\":" + DesignScene.Q(p.Role) : "";

    /// <summary>The room a seat stands in: the one it is linked to, else the nearest to where it was dropped (as a socket's).</summary>
    public static Room? RoomOf(ShipDesign d, Piece p)
    {
        if (d.Hull(p.Hull) is not { } h)
            return null;
        if (p.Room != 0)
            return d.RoomById(p.Room);
        var at = DesignScene.Where(h, p).O;
        return d.Rooms.MinBy(r => Dist(Clamp(at, DesignScene.Outer(d, r)), at));
    }

    /// <summary>
    /// A seat's place: on its room's floor (its frame's Y up out of the floor, Z the way the sitter faces: the bow, turned by Turn), at the room's
    /// point nearest where it was dropped, kept <see cref="Inset"/> in from its walls. Null for any other piece, or a seat with no room.
    /// </summary>
    public static DesignScene.Place? PlaceOf(ShipDesign d, Piece p)
    {
        var fur = DesignFurnishings.Of(p.Kind);
        if (!IsSeat(p.Kind) && fur is null || d.Hull(p.Hull) is not { } h || RoomOf(d, p) is not { } r)
            return null;
        var at = DesignScene.Where(h, p).O;
        var b = d.Box(r);
        // An interior piece (DesignFurnishings) keeps its own half sizes in from the walls; a wall one backs onto the nearest wall.
        var s = Math.Max(0.25, p.Size);
        // (and stands on its shell's floor, clear of its walls: DesignScene.FloorTop, ShellDepth)
        var wall = fur is null ? 0 : DesignScene.ShellDepth(r);
        double inX = fur is null ? Inset : fur.Hx * s + 0.02 + wall, inZ = fur is null ? Inset : fur.Hz * s + 0.02 + wall;
        double In(double v, double lo, double hi, double inset) => hi - lo <= 2 * inset ? (lo + hi) / 2 : Math.Clamp(v, lo + inset, hi - inset);
        // Plan axes a (across) and l (along), the floor's up axis; stacked decks have up as -z (a floor is the deck's aft face).
        int ax = 0, al = d.Stacked ? 1 : 2, upA = d.Stacked ? 2 : 1;
        var o = new double[3];
        double[] up = d.Stacked ? [0, 0, -1] : [0, 1, 0];
        double[] face = d.Stacked ? [0, 1, 0] : [0, 0, -1]; // (a seat faces the bow; stacked: the dorsal side)
        var lift = fur is null ? 0 : DesignScene.FloorTop(r) + 0.004;
        o[upA] = d.Stacked ? b[5] - lift : b[2] + lift;
        if (fur is { Wall: true })
        {
            // The wall nearest the drop: its back against it, facing into the room, its width along the wall.
            double a0 = b[2 * ax], a1 = b[2 * ax + 1], l0 = b[2 * al], l1 = b[2 * al + 1];
            var walls = new (double Dist, int Axis, int Sign)[] { (at[ax] - a0, ax, 1), (a1 - at[ax], ax, -1), (at[al] - l0, al, 1), (l1 - at[al], al, -1) };
            var w = walls.MinBy(x => x.Dist);
            face = [0, 0, 0];
            face[w.Axis] = w.Sign;
            var other = w.Axis == ax ? al : ax;
            o[w.Axis] = w.Sign > 0 ? b[2 * w.Axis] + inZ : b[2 * w.Axis + 1] - inZ;
            if (b[2 * w.Axis + 1] - b[2 * w.Axis] <= 2 * inZ)
                o[w.Axis] = (b[2 * w.Axis] + b[2 * w.Axis + 1]) / 2;
            o[other] = In(at[other], b[2 * other], b[2 * other + 1], inX);
        }
        else
        {
            o[ax] = In(at[ax], b[2 * ax], b[2 * ax + 1], fur is null ? Inset : Math.Max(inX, inZ));
            o[al] = In(at[al], b[2 * al], b[2 * al + 1], fur is null ? Inset : Math.Max(inX, inZ));
        }
        if (p.Turn != 0)
        {
            // Turned about the floor's normal (positive: to the right, seen from above).
            var a = -p.Turn * Math.PI / 180;
            var side = Cross(face, up);
            double c = Math.Cos(a), sn = Math.Sin(a);
            face = [face[0] * c + side[0] * sn, face[1] * c + side[1] * sn, face[2] * c + side[2] * sn];
        }
        var x = Cross(up, face);
        return new DesignScene.Place(o, x, up, face);
    }

    /// <summary>The seat and its console, in its place's frame: X across, Y up, Z the way the sitter faces.</summary>
    private static void Draw(DesignMesh m)
    {
        const string A = PieceKinds.Armour, G = PieceKinds.Gun, P = PieceKinds.Pale, Am = PieceKinds.Amber, Gl = PieceKinds.Glow;
        // (Every box that meets another runs 4 mm into it, so no two faces share a plane; the feet stand 4 mm off the floor.)
        m.Box(-0.2, 0.004, -0.2, 0.2, 0.384, 0.2, A);          // the pedestal
        m.Box(-0.3, 0.38, -0.3, 0.3, 0.5, 0.3, G);             // the seat pan
        m.Box(-0.3, 0.496, -0.34, 0.3, 1.25, -0.22, G);        // the back, behind the sitter
        m.Box(-0.22, 1.1, -0.36, 0.22, 1.3, -0.336, P);        // its head rest, proud of the back's rear face
        m.Box(-0.36, 0.46, -0.216, -0.296, 0.72, 0.22, A);     // arm rests on the pan's sides
        m.Box(0.296, 0.46, -0.216, 0.36, 0.72, 0.22, A);
        m.Box(-0.45, 0.004, 0.55, 0.45, 0.8, 0.75, A);         // the console in front of it
        m.Box(-0.42, 0.796, 0.5, 0.42, 0.86, 0.78, G);         // its desk
        m.Box(-0.38, 0.856, 0.68, 0.38, 1.25, 0.72, G);        // its screen's frame
        m.Box(-0.34, 0.9, 0.66, 0.34, 1.21, 0.684, Gl);        // the screen, in front of its frame
        m.Box(-0.45, 0.02, 0.53, 0.45, 0.06, 0.554, Am);       // a kick strip at the console's foot
    }

    /// <summary>The design's seat warnings: a ship that has drives needs a helm to fly it; more than one helm, the first flies it.</summary>
    public static void Check(ShipDesign d, DesignScene.Check c)
    {
        var seats = d.Pieces.Where(p => IsSeat(p.Kind)).ToList();
        foreach (var p in seats)
            if (RoomOf(d, p) is null)
                c.Issues.Add(new("error", "The seat has no room to stand in: drop it by a room, or link it to one", $"piece:{p.Id}"));
        var drives = d.Pieces.Any(p => PieceKinds.Find(p.Kind)?.Group == "Drives");
        var helms = seats.Count(p => p.Role == "Helm");
        if (drives && helms == 0)
            c.Issues.Add(new("warn", seats.Count == 0 ? "No seat: add a seat and set it to Helm, or nobody can fly the ship" : "No seat is set to Helm: nobody can fly the ship", ""));
        else if (helms > 1)
            c.Issues.Add(new("warn", $"{helms} seats are set to Helm: the first flies the ship, the others are spare helms", ""));
    }

    private static double[] Clamp(double[] at, double[] bx) => [Math.Clamp(at[0], bx[0], bx[1]), Math.Clamp(at[1], bx[2], bx[3]), Math.Clamp(at[2], bx[4], bx[5])];
    private static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}
