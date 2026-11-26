using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Skin fittings (Karl, 2026-11-26), sized from the game's parts: floodlights S, M and L (Constructs/Parts/Systems/Lights: a 0.5, 1 and
/// 4 m head) aimed by a pitch and a yaw, with a beam colour; the long-range sensor (Systems/Control/LongRangeSensor), a flat array on a
/// turntable; the heavy RCS quad (Ship/HeavyRCSThruster, 1.6 times the RCS Thruster), which like the RCS quad needs no service socket;
/// and the tow eye and tow hitch (TowEye, TowHitch), which should sit on the keel or the centreline.
/// All their geometry, checks, JSON and page fields are here, so the rest of the designer only calls in.
/// </summary>
public static class DesignFittings
{
    /// <summary>A floodlight's head across (m, the part's size) and how far its beam is drawn (m).</summary>
    public sealed record Flood(string Name, double Head, double Range, string Part);

    public static readonly Flood[] Floods =
    [
        new("Floodlight S", 0.5, 30, "Floodlight"),
        new("Floodlight M", 1.0, 60, "Floodlight Medium"),
        new("Floodlight L", 4.0, 160, "Floodlight Large"),
    ];

    public const double DefPitch = 30, DefYaw = 0;
    public const string DefBeam = "#fff4d0"; // the parts' default colour
    /// <summary>How far off its body's keel, spine or the ship's centreline a tow point may sit before the check warns (m).</summary>
    public const double TowOff = 0.6;
    /// <summary>The heavy RCS against the RCS quad: the parts' widths, 0.49 m against 0.31 m.</summary>
    public const double HeavyRcs = 1.6;

    public static Flood? FloodOf(string kind) => Array.Find(Floods, f => f.Name == kind);
    public static bool IsLight(string kind) => FloodOf(kind) is not null;
    public static bool IsTow(string kind) => kind is "Tow eye" or "Tow hitch";
    /// <summary>Drives that are fed by their pads and need no service socket inside (Karl, 2026-11-25).</summary>
    public static bool NoService(string kind) => kind is "RCS quad" or "Heavy RCS";

    public static readonly PieceKind[] Kinds =
    [
        new("Floodlight S", "Lights", "skin", null, "", "A 0.5 m floodlight (the Floodlight) on a yoke: aim it and pick its beam's colour.", _ => { }),
        new("Floodlight M", "Lights", "skin", null, "", "A 1 m floodlight (the Floodlight Medium) on a yoke.", _ => { }),
        new("Floodlight L", "Lights", "skin", null, "", "A 4 m floodlight (the Floodlight Large) for lighting a dock or a landing site.", _ => { }),
        new("Long-range sensor", "Systems", "skin", null, "", "A flat sensor array on a turntable (the Long Range Sensor): a radar of ships out to 100 km.", LongRange),
        new("Heavy RCS", "Drives", "skin", null, "", "A heavy RCS quad (the Heavy RCS Thruster), 1.6 times the RCS quad. Fed by its pad: no service socket.", m => Rcs(m, HeavyRcs)),
        new("Tow eye", "Systems", "skin", null, "", "A tow eye (the Tow Eye): a tow ball or chain hook couples to it. Put it on the keel or the centreline.", TowEye),
        new("Tow hitch", "Systems", "skin", null, "", "A tow hitch (the Tow Hitch): couples a trailer's eye. Put it on the keel or the centreline.", TowHitch),
    ];

    /// <summary>What keeps hull cladding clear round each, in its own frame.</summary>
    public static (double Hx, double Hz, bool Round)? Footprint(string name) => FloodOf(name) is { } f ? (f.Head * 0.55, f.Head * 0.55, false) : name switch
    {
        "Long-range sensor" => (0.36, 0.2, false), "Heavy RCS" => (0.35 * HeavyRcs, 0.35 * HeavyRcs, false), "Tow eye" => (0.16, 0.22, false), "Tow hitch" => (0.55, 0.14, false),
        _ => null,
    };

    public static JsonObject Defaults() => new() { ["pitch"] = DefPitch, ["yaw"] = DefYaw, ["beam"] = DefBeam };

    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (IsLight(p.Kind))
        {
            o["pitch"] = ShipDesign.R(p.Pitch);
            o["yaw"] = ShipDesign.R(p.Yaw);
            o["beam"] = p.Beam;
        }
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        p.Pitch = Math.Clamp(ShipDesign.F(o["pitch"], DefPitch), -90, 90);
        p.Yaw = Math.Clamp(ShipDesign.F(o["yaw"], DefYaw), -180, 180);
        p.Beam = Colour((string?)o["beam"]) ?? DefBeam;
        return p;
    }

    /// <summary>The page's setPiece fields; an error text, or empty.</summary>
    public static string Set(Piece p, JsonObject o)
    {
        if (o["pitch"] is not null) p.Pitch = Math.Clamp(Math.Round(ShipDesign.F(o["pitch"], DefPitch)), -90, 90);
        if (o["yaw"] is not null) p.Yaw = Math.Clamp(Math.Round(ShipDesign.F(o["yaw"], DefYaw)), -180, 180);
        if (o["beam"] is not null)
        {
            if (Colour((string?)o["beam"]) is not { } c)
                return "a beam colour is #rrggbb";
            p.Beam = c;
        }
        return "";
    }

    private static string? Colour(string? s) =>
        s is { Length: 7 } && s[0] == '#' && int.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? s.ToLowerInvariant() : null;

    // ---- the scene

    /// <summary>A floodlight's aim in its piece frame: pitch tilts it off the skin's normal towards the bow, yaw swings that about the normal.</summary>
    private static double[] AimLocal(Piece p)
    {
        double pa = p.Pitch * Math.PI / 180, ya = p.Yaw * Math.PI / 180;
        return [Math.Sin(pa) * Math.Sin(ya), Math.Cos(pa), Math.Sin(pa) * Math.Cos(ya)];
    }

    private static double[] ToWorld(DesignScene.Place w, double[] v) =>
        [w.X[0] * v[0] + w.Y[0] * v[1] + w.Z[0] * v[2], w.X[1] * v[0] + w.Y[1] * v[1] + w.Z[1] * v[2], w.X[2] * v[0] + w.Y[2] * v[1] + w.Z[2] * v[2]];

    private static double[] Norm(double[] v) { var l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]); return l < 1e-9 ? [0, 1, 0] : [v[0] / l, v[1] / l, v[2] / l]; }
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    /// <summary>The page's light fields: aim and colour, and each beam (its lens, its direction, its reach), mirrored as the mesh is.</summary>
    public static string SceneJson(ShipDesign d, Piece p, DesignScene.Place w)
    {
        if (FloodOf(p.Kind) is not { } f)
            return "";
        var h = d.Hull(p.Hull)!;
        var s = f.Head * p.Size;
        var dir = Norm(ToWorld(w, AimLocal(p)));
        var pivot = 0.55 * s - p.Recess;
        double[] at = [w.O[0] + w.Y[0] * pivot + dir[0] * 0.35 * s, w.O[1] + w.Y[1] * pivot + dir[1] * 0.35 * s, w.O[2] + w.Y[2] * pivot + dir[2] * 0.35 * s];
        var beams = new List<(double[] At, double[] Dir)> { (at, dir) };
        static (double[], double[]) Mx((double[] A, double[] D) b, double c) => ([2 * c - b.A[0], b.A[1], b.A[2]], [-b.D[0], b.D[1], b.D[2]]);
        if (p.Mirror)
            beams.Add(Mx(beams[0], h.CopyX(Math.Min(p.Copy, h.Copies - 1))));
        if (p.Pair && h.Copies == 2)
            beams.AddRange(beams.ToArray().Select(b => Mx(b, 0)));
        var sb = new StringBuilder();
        sb.Append(",\"pitch\":").Append(ShipDesign.S(p.Pitch)).Append(",\"yaw\":").Append(ShipDesign.S(p.Yaw)).Append(",\"beam\":").Append(DesignScene.Q(p.Beam)).Append(",\"beams\":[");
        for (var i = 0; i < beams.Count; i++)
        {
            var (a, v) = beams[i];
            sb.Append(i > 0 ? "," : "").Append("{\"at\":[").Append(string.Join(',', a.Select(ShipDesign.S))).Append("],\"dir\":[").Append(string.Join(',', v.Select(ShipDesign.S)))
              .Append("],\"r\":").Append(ShipDesign.S(0.4 * s)).Append(",\"range\":").Append(ShipDesign.S(f.Range * Math.Sqrt(p.Size))).Append('}');
        }
        return sb.Append(']').ToString();
    }

    /// <summary>Draws a floodlight from its aim (the mesh's frame is set at the piece, sized); false for the other kinds.</summary>
    public static bool Draw(Piece p, DesignScene.Place w, DesignMesh m)
    {
        if (FloodOf(p.Kind) is not { } f)
            return false;
        var k = f.Head / 0.5; // the drawing is the S's, 0.5 m, scaled
        // The base and yoke, in the piece's frame (scaled by its size).
        m.Frame(w.O[0] - w.Y[0] * p.Recess, w.O[1] - w.Y[1] * p.Recess, w.O[2] - w.Y[2] * p.Recess, w.X, w.Y, w.Z, p.Size * k, 1);
        m.Box(-0.25, 0, -0.25, 0.25, 0.08, 0.25, PieceKinds.Armour);
        m.Frustum(0.08, 0.12, 0.2, 0.1, 10, PieceKinds.Gun);
        m.Box(-0.26, 0.15, -0.04, -0.22, 0.32, 0.04, PieceKinds.Gun);
        m.Box(0.22, 0.15, -0.04, 0.26, 0.32, 0.04, PieceKinds.Gun);
        m.Box(-0.22, 0.15, -0.04, 0.22, 0.19, 0.04, PieceKinds.Gun);
        // The head, along its aim, from the yoke's pivot (0.275 up in S units).
        var y = Norm(ToWorld(w, AimLocal(p)));
        var x = Cross(y, w.Z);
        x = Math.Sqrt(x[0] * x[0] + x[1] * x[1] + x[2] * x[2]) < 1e-6 ? w.X : Norm(x);
        var z = Cross(x, y);
        var s = p.Size * k;
        var piv = 0.275 * s - p.Recess;
        m.Frame(w.O[0] + w.Y[0] * piv, w.O[1] + w.Y[1] * piv, w.O[2] + w.Y[2] * piv, x, y, z, s, 1);
        m.Frustum(-0.12, 0.12, 0.05, 0.2, 12, PieceKinds.Armour);
        m.Frustum(0.05, 0.21, 0.16, 0.21, 12, PieceKinds.Gun, cap1: false);
        m.Frustum(0.16, 0.22, 0.18, 0.22, 12, PieceKinds.Armour, cap0: false, cap1: false);
        m.Frustum(0.1, 0.19, 0.155, 0.19, 12, p.Beam, cap0: false); // the lens, 5 mm inside the rim
        return true;
    }

    private static void LongRange(DesignMesh m)
    {
        // A turntable, a short mast and a flat array facing the bow, its face of tiles 4 mm proud of the back plate.
        m.Frustum(0, 0.16, 0.1, 0.14, 12, PieceKinds.Armour);
        m.Frustum(0.1, 0.05, 0.24, 0.05, 8, PieceKinds.Gun);
        m.Box(-0.3, 0.24, -0.06, 0.3, 0.6, 0.03, PieceKinds.Gun);
        m.Box(-0.33, 0.22, -0.08, 0.33, 0.25, 0.05, PieceKinds.Armour);
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 2; j++)
            {
                double x0 = -0.28 + i * 0.14, y0 = 0.28 + j * 0.16;
                m.Box(x0, y0, 0.03, x0 + 0.12, y0 + 0.14, 0.034, i % 2 == j % 2 ? PieceKinds.Pale : PieceKinds.Glow);
            }
        m.Box(-0.03, 0.6, -0.03, 0.03, 0.68, 0.03, PieceKinds.Amber);
    }

    /// <summary>The RCS quad's drawing, scaled by <paramref name="k"/>.</summary>
    private static void Rcs(DesignMesh m, double k)
    {
        m.Box(-0.35 * k, 0, -0.35 * k, 0.35 * k, 0.5 * k, 0.35 * k, PieceKinds.Armour);
        m.Barrel(0, 0.3 * k, 0.35 * k, 0.6 * k, 0.1 * k, 8, PieceKinds.Gun);
        m.Barrel(0, 0.3 * k, -0.35 * k, -0.6 * k, 0.1 * k, 8, PieceKinds.Gun);
        m.Box(0.35 * k, 0.22 * k, -0.08 * k, 0.55 * k, 0.38 * k, 0.08 * k, PieceKinds.Gun);
        m.Box(-0.55 * k, 0.22 * k, -0.08 * k, -0.35 * k, 0.38 * k, 0.08 * k, PieceKinds.Gun);
        m.Box(-0.354 * k, 0.4 * k, -0.354 * k, 0.354 * k, 0.45 * k, 0.354 * k, PieceKinds.Amber); // the heavy's band, 4 mm proud of its sides
    }

    private static void TowEye(DesignMesh m)
    {
        // A pad 0.2 x 0.3 and an eye standing up off it along the ship.
        m.Box(-0.1, 0, -0.15, 0.1, 0.06, 0.15, PieceKinds.Armour);
        m.Box(-0.03, 0.06, -0.1, 0.03, 0.26, -0.06, PieceKinds.Gun);
        m.Box(-0.03, 0.06, 0.06, 0.03, 0.26, 0.1, PieceKinds.Gun);
        m.Box(-0.03, 0.26, -0.1, 0.03, 0.3, 0.1, PieceKinds.Gun);
        m.Box(-0.08, 0.06, -0.18, 0.08, 0.1, -0.15, PieceKinds.Amber);
    }

    private static void TowHitch(DesignMesh m)
    {
        // A 1 m bar on the skin, a shank out of it and the ball.
        m.Box(-0.5, 0, -0.1, 0.5, 0.2, 0.1, PieceKinds.Armour);
        m.Box(-0.06, 0.2, -0.06, 0.06, 0.38, 0.06, PieceKinds.Gun);
        m.Frustum(0.38, 0.06, 0.48, 0.06, 10, PieceKinds.Pale);
        m.Box(-0.52, 0.02, -0.02, -0.5, 0.18, 0.02, PieceKinds.Amber);
        m.Box(0.5, 0.02, -0.02, 0.52, 0.18, 0.02, PieceKinds.Amber);
    }

    // ---- checks

    /// <summary>Adds the fittings' own warnings (the usual piece checks still run).</summary>
    public static void Check(ShipDesign d, Piece p, DesignScene.Check c)
    {
        if (!IsTow(p.Kind))
            return;
        var h = d.Hull(p.Hull)!;
        var w = DesignScene.Where(h, p);
        var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
        // On its body's keel or spine (its middle across), or on the ship's centreline.
        var off = Math.Min(Math.Abs(w.O[0] - cx), Math.Abs(w.O[0]));
        if (off > TowOff)
            c.Issues.Add(new("warn", $"{p.Kind} is {off:0.0} m off the keel and the centreline: a tow pulls the ship round. Press Centre to put it there", $"piece:{p.Id}"));
        else if (p.Mirror || (p.Pair && h.Copies == 2 && Math.Abs(w.O[0]) > TowOff))
            c.Issues.Add(new("warn", $"{p.Kind} is mirrored: a ship tows from one point on its centreline", $"piece:{p.Id}"));
    }
}
