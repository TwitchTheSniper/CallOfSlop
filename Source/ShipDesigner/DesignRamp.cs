using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The boarding ramp (Karl, 2026-11-27): a hatch of its own width (across) and length (along), 2.5 x 5.1 m by default, and a ramp in it
/// that works. Stowed, the ramp lies flat in the room's floor over the well, its walking face just under the floor's plane; opened, it
/// hinges down out of the hatch on its fore edge (at the floor) and then runs its telescoping second section out along itself by
/// <see cref="Piece.Extend"/>, to reach the ground. The designer draws it lowered and run out; the game builds it stowed, the ramp a
/// motor hinge and the extension a piston riding it (DesignParts.Pieces.cs, RampLeaves), which F works (DesignParts.LiftUse).
/// <para>Its frame (the piece's, moved in to the room's floor): X across, Y out of the skin (down on the belly), Z towards the bow.
/// Leaf 0 is the ramp, leaf 1 its extension; the rest (the sills round it at the floor) are boxes, so the game's frame collides as boxes.</para>
/// <para>The hatch's width and length (<see cref="Piece.RampW"/>, <see cref="Piece.RampL"/>, metres) are its hole: the generic hole size
/// is held at 1 for a ramp, and everything (skin and floor holes, frame, cladding, plate, extension, sills, swing) follows them.</para>
/// </summary>
public static class DesignRamp
{
    public const string Kind = "Boarding ramp";
    /// <summary>The hatch's width (across) and length (along) in metres, each set apart (Karl, 2026-11-27): range and default.</summary>
    public const double MinWidth = 1.5, MaxWidth = 5, DefWidth = 2.5, MinLength = 2, MaxLength = 8, DefLength = 5.1;
    /// <summary>The kind's default half opening across and along (a piece's own: <see cref="OpeningOf"/>).</summary>
    public const double Hx = DefWidth / 2, Hz = DefLength / 2;
    // The ramp's plate, in from the hatch's edges (across, and at each end), its walking face 4 mm out of the floor's plane (no shared
    // plane with the floor round its hole) and its back. Its hinge line is the hatch's fore edge.
    private const double LeafInX = 0.08, LeafInZ = 0.04, LeafY0 = 0.004, LeafY1 = 0.09;
    // The extension, nested under the ramp (out of it) while stowed; it runs out aft, at least Overlap of it kept under the ramp.
    private const double ExtInX = 0.2, ExtInZ0 = 0.1, ExtInZ1 = 0.25, ExtY0 = 0.094, ExtY1 = 0.15, Overlap = 0.3;
    /// <summary>The designer's slider's top (m); a piece's run is held to what its ramp's length allows (<see cref="ExtendOf"/>).</summary>
    public const double MaxExtend = 7.5, DefExtend = 1.5;
    /// <summary>Seconds the ramp takes to swing down, and its extension to run out (each).</summary>
    public const double Seconds = 1.75;

    /// <summary>The piece's half opening across and along in the units the hole code scales by Size x Hole (so metres once scaled).</summary>
    public static (double Hx, double Hz, bool Round) OpeningOf(Piece p)
    {
        var u = Math.Max(0.05, p.Size * p.Hole);
        return (p.RampW / 2 / u, p.RampL / 2 / u, false);
    }

    /// <summary>The ramp's swing (radians): steep enough that a person walking up it clears the hatch's aft lip with the room's floor
    /// <paramref name="depth"/> in from the skin, never past what a person walks up (World.MaxSlopeDeg, 42).</summary>
    public static double Angle(Piece p, double depth) => Math.Clamp(Math.Atan2(1.9 + Math.Max(0, depth), p.RampL), 34 * Math.PI / 180, 40 * Math.PI / 180);

    /// <summary>The extension's longest run (m): its length less what it keeps under the ramp, so in proportion to the ramp's.</summary>
    public static double SlideMax(Piece p) => Math.Max(0, p.RampL - (LeafInZ + ExtInZ1 + Overlap) * p.Size);

    /// <summary>The extension's run (m) as the piece asks, held to what the ramp of its length keeps hold of.</summary>
    public static double ExtendOf(Piece p) => Math.Clamp(p.Extend, 0, SlideMax(p));

    /// <summary>The ramp's hinge line's middle (the ship's frame), its floor <paramref name="depth"/> in from the skin at place <paramref name="w"/>.</summary>
    public static double[] Hinge(Piece p, DesignScene.Place w, double depth)
    {
        var o = Floor(p, w, depth);
        double y = LeafY0 * p.Size, z = p.RampL / 2;
        return [o[0] + w.Y[0] * y + w.Z[0] * z, o[1] + w.Y[1] * y + w.Z[1] * z, o[2] + w.Y[2] * y + w.Z[2] * z];
    }

    private static double[] Floor(Piece p, DesignScene.Place w, double depth)
    {
        var dd = depth + p.Recess;
        return [w.O[0] - w.Y[0] * dd, w.O[1] - w.Y[1] * dd, w.O[2] - w.Y[2] * dd];
    }

    /// <summary>
    /// Draws the ramp in <paramref name="m"/>: stowed (<paramref name="shut"/>: the game's rest) or lowered and run out (the designer's view),
    /// with the sills round it at the floor. Leaves the mesh's frame on the floor.
    /// </summary>
    public static void Draw(DesignMesh m, Piece p, DesignScene.Place w, double depth, bool shut)
    {
        // In the piece's own units (metres over its size), from its own opening.
        double s = Math.Max(0.05, p.Size), hx = p.RampW / 2 / s, hz = p.RampL / 2 / s;
        double leafHx = hx - LeafInX, hingeZ = hz, extHx = hx - ExtInX;
        var a = shut ? 0 : Angle(p, depth);
        double ca = Math.Cos(a), sa = Math.Sin(a);
        // The hinge's frame: turned about X so the ramp's far end goes out of the skin (+Y).
        var h = Hinge(p, w, depth);
        double[] y = [w.Y[0] * ca + w.Z[0] * sa, w.Y[1] * ca + w.Z[1] * sa, w.Y[2] * ca + w.Z[2] * sa];
        double[] z = [w.Z[0] * ca - w.Y[0] * sa, w.Z[1] * ca - w.Y[1] * sa, w.Z[2] * ca - w.Y[2] * sa];
        m.Frame(h[0], h[1], h[2], w.X, y, z, s);
        double y0 = 0, y1 = LeafY1 - LeafY0, z0 = -hz + LeafInZ - hingeZ, z1 = hz - LeafInZ - hingeZ;
        m.Leaf(0, () =>
        {
            m.Box(-leafHx, y0, z0, leafHx, y1, z1, PieceKinds.Gun);
            // Amber edges along its sides on the walking face (5 mm in from its edges: no shared plane).
            m.Box(-leafHx + 0.005, y0 - 0.003, z0 + 0.005, -leafHx + 0.12, y0 + 0.02, z1 - 0.005, PieceKinds.Amber);
            m.Box(leafHx - 0.12, y0 - 0.003, z0 + 0.005, leafHx - 0.005, y0 + 0.02, z1 - 0.005, PieceKinds.Amber);
        });
        var run = shut ? 0 : ExtendOf(p) / s;
        m.Leaf(1, () =>
        {
            double e0 = -hz + ExtInZ0 - hingeZ - run, e1 = hz - ExtInZ1 - hingeZ - run;
            m.Box(-extHx, ExtY0 - LeafY0, e0, extHx, ExtY1 - LeafY0, e1, PieceKinds.Armour);
            m.Box(-extHx - 0.004, ExtY0 - LeafY0 - 0.004, e0 - 0.004, extHx + 0.004, ExtY1 - LeafY0 + 0.004, e0 + 0.1, PieceKinds.Amber); // (its toe)
        });
        // The sills: the floor's hole is square, its half the opening's longer side (DesignScene.SocketDoor), so plates at the floor fill
        // what the ramp's plate doesn't (fore and aft of it, and its sides), level with it.
        var o = Floor(p, w, depth);
        m.Frame(o[0], o[1], o[2], w.X, w.Y, w.Z, s);
        var r2 = Math.Max(DesignScene.DoorHalfDefault / s, Math.Max(hx, hz));
        if (r2 > hingeZ + 0.02)
        {
            m.Box(-r2, LeafY0, hingeZ + 0.004, r2, LeafY1, r2, PieceKinds.Gun); // (4 mm off the well's walls' faces)
            m.Box(-r2, LeafY0, -r2, r2, LeafY1, -hingeZ - 0.004, PieceKinds.Gun);
        }
        if (r2 > leafHx + 0.03)
        {
            m.Box(-r2 + 0.004, LeafY0, -hingeZ + 0.003, -leafHx - 0.01, LeafY1, hingeZ - 0.003, PieceKinds.Gun);
            m.Box(leafHx + 0.01, LeafY0, -hingeZ + 0.003, r2 - 0.004, LeafY1, hingeZ - 0.003, PieceKinds.Gun);
        }
    }

    // ---- the design file and the page

    public static JsonObject Defaults() => new() { ["extend"] = DefExtend, ["rampW"] = DefWidth, ["rampL"] = DefLength };

    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (p.Kind == Kind)
        {
            o["extend"] = ShipDesign.R(p.Extend);
            o["rampW"] = ShipDesign.R(p.RampW);
            o["rampL"] = ShipDesign.R(p.RampL);
        }
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        p.Extend = Math.Clamp(ShipDesign.F(o["extend"], DefExtend), 0, MaxExtend);
        if (p.Kind != Kind)
            return p;
        // A design from before the two keeps the hatch it had (the kind's opening times its size and hole size); from then on the
        // ramp's width and length are its hole and the generic hole size stays at 1.
        var was = p.Size * p.Hole;
        p.RampW = Math.Clamp(ShipDesign.F(o["rampW"], 2 * Hx * was), MinWidth, MaxWidth);
        p.RampL = Math.Clamp(ShipDesign.F(o["rampL"], 2 * Hz * was), MinLength, MaxLength);
        p.Hole = 1;
        return p;
    }

    /// <summary>The page's setPiece fields; an error text, or empty.</summary>
    public static string Set(Piece p, JsonObject o)
    {
        if (o["extend"] is not null)
            p.Extend = Math.Clamp(Math.Round(ShipDesign.F(o["extend"], DefExtend) * 20) / 20, 0, MaxExtend);
        if (o["rampW"] is not null)
            p.RampW = Math.Clamp(Math.Round(ShipDesign.F(o["rampW"], DefWidth) * 20) / 20, MinWidth, MaxWidth);
        if (o["rampL"] is not null)
            p.RampL = Math.Clamp(Math.Round(ShipDesign.F(o["rampL"], DefLength) * 20) / 20, MinLength, MaxLength);
        p.Hole = 1; // (its width and length are its hole)
        return "";
    }

    /// <summary>The page's ramp fields: its run and the longest its length allows, its width and length.</summary>
    public static string SceneJson(Piece p) => p.Kind != Kind ? "" :
        new StringBuilder(",\"extend\":").Append(ShipDesign.S(p.Extend)).Append(",\"extendMax\":")
            .Append(ShipDesign.S(Math.Min(MaxExtend, Math.Floor(SlideMax(p) * 20) / 20)))
            .Append(",\"rampW\":").Append(ShipDesign.S(p.RampW)).Append(",\"rampL\":").Append(ShipDesign.S(p.RampL)).ToString();
}
