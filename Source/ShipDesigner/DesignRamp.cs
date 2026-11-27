using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The boarding ramp (Karl, 2026-11-27): a hatch 4 m across (60% wider than the 2.5 m it was) and 3.2 m along, and a ramp in it
/// that works. Stowed, the ramp lies flat in the room's floor over the well, its walking face just under the floor's plane; opened, it
/// hinges down out of the hatch on its fore edge (at the floor) and then runs its telescoping second section out along itself by
/// <see cref="Piece.Extend"/>, to reach the ground. The designer draws it lowered and run out; the game builds it stowed, the ramp a
/// motor hinge and the extension a piston riding it (DesignParts.Pieces.cs, RampLeaves), which F works (DesignParts.LiftUse).
/// <para>Its frame (the piece's, moved in to the room's floor): X across, Y out of the skin (down on the belly), Z towards the bow.
/// Leaf 0 is the ramp, leaf 1 its extension; the rest (the sills round it at the floor) are boxes, so the game's frame collides as boxes.</para>
/// </summary>
public static class DesignRamp
{
    public const string Kind = "Boarding ramp";
    /// <summary>The hatch's half opening across and along (Karl, 2026-11-27: across 1.25 x 1.6).</summary>
    public const double Hx = 2.0, Hz = 1.6;
    // The ramp's plate: across, its far end (aft), its hinge line (fore), its walking face 4 mm out of the floor's plane (no shared plane
    // with the floor round its hole) and its back.
    private const double LeafHx = 1.92, LeafZ0 = -1.56, LeafZ1 = 1.56, HingeZ = 1.6, LeafY0 = 0.004, LeafY1 = 0.09;
    // The extension, nested under the ramp (out of it) while stowed; it runs out aft, at least Overlap of it kept under the ramp.
    private const double ExtHx = 1.8, ExtZ0 = -1.5, ExtZ1 = 1.35, ExtY0 = 0.094, ExtY1 = 0.15, Overlap = 0.3;
    /// <summary>The extension's longest run in the piece's own units (times its size and hole: metres).</summary>
    public const double SlideMax = ExtZ1 - LeafZ0 - Overlap;
    /// <summary>The designer's slider's range and default (m); a piece's run is held to what its size allows (<see cref="ExtendOf"/>).</summary>
    public const double MaxExtend = 2.5, DefExtend = 1.5;
    /// <summary>Seconds the ramp takes to swing down, and its extension to run out (each).</summary>
    public const double Seconds = 1.75;

    /// <summary>The ramp's swing (radians): steep enough that a person walking up it clears the hatch's aft lip with the room's floor
    /// <paramref name="depth"/> in from the skin, never past what a person walks up (World.MaxSlopeDeg, 42).</summary>
    public static double Angle(double depth) => Math.Clamp(Math.Atan2(1.9 + Math.Max(0, depth), 2 * Hz), 34 * Math.PI / 180, 40 * Math.PI / 180);

    /// <summary>The extension's run (m) as the piece asks, held to what the ramp of its size keeps hold of.</summary>
    public static double ExtendOf(Piece p) => Math.Clamp(p.Extend, 0, SlideMax * p.Size * p.Hole);

    /// <summary>The ramp's hinge line's middle (the ship's frame), its floor <paramref name="depth"/> in from the skin at place <paramref name="w"/>.</summary>
    public static double[] Hinge(Piece p, DesignScene.Place w, double depth)
    {
        var o = Floor(p, w, depth);
        double y = LeafY0 * p.Size, z = HingeZ * p.Size * p.Hole;
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
        double s = p.Size, across = p.Hole;
        var a = shut ? 0 : Angle(depth);
        double ca = Math.Cos(a), sa = Math.Sin(a);
        // The hinge's frame: turned about X so the ramp's far end goes out of the skin (+Y).
        var h = Hinge(p, w, depth);
        double[] y = [w.Y[0] * ca + w.Z[0] * sa, w.Y[1] * ca + w.Z[1] * sa, w.Y[2] * ca + w.Z[2] * sa];
        double[] z = [w.Z[0] * ca - w.Y[0] * sa, w.Z[1] * ca - w.Y[1] * sa, w.Z[2] * ca - w.Y[2] * sa];
        m.Frame(h[0], h[1], h[2], w.X, y, z, s, across);
        const double y0 = 0, y1 = LeafY1 - LeafY0, z0 = LeafZ0 - HingeZ, z1 = LeafZ1 - HingeZ;
        m.Leaf(0, () =>
        {
            m.Box(-LeafHx, y0, z0, LeafHx, y1, z1, PieceKinds.Gun);
            // Amber edges along its sides on the walking face (5 mm in from its edges: no shared plane).
            m.Box(-LeafHx + 0.005, y0 - 0.003, z0 + 0.005, -LeafHx + 0.12, y0 + 0.02, z1 - 0.005, PieceKinds.Amber);
            m.Box(LeafHx - 0.12, y0 - 0.003, z0 + 0.005, LeafHx - 0.005, y0 + 0.02, z1 - 0.005, PieceKinds.Amber);
        });
        var run = shut ? 0 : ExtendOf(p) / (s * across);
        m.Leaf(1, () =>
        {
            double e0 = ExtZ0 - HingeZ - run, e1 = ExtZ1 - HingeZ - run;
            m.Box(-ExtHx, ExtY0 - LeafY0, e0, ExtHx, ExtY1 - LeafY0, e1, PieceKinds.Armour);
            m.Box(-ExtHx - 0.004, ExtY0 - LeafY0 - 0.004, e0 - 0.004, ExtHx + 0.004, ExtY1 - LeafY0 + 0.004, e0 + 0.1, PieceKinds.Amber); // (its toe)
        });
        // The sills: the floor's hole is square, its half the opening's longer side (DesignScene.SocketDoor), so plates at the floor fill
        // what the ramp's plate doesn't (fore and aft of it, and its sides), level with it.
        var o = Floor(p, w, depth);
        m.Frame(o[0], o[1], o[2], w.X, w.Y, w.Z, s, across);
        var r2 = Math.Max(Hx, Hz) / across;
        if (r2 > HingeZ + 0.02)
        {
            m.Box(-r2, LeafY0, HingeZ + 0.004, r2, LeafY1, r2, PieceKinds.Gun); // (4 mm off the well's walls' faces)
            m.Box(-r2, LeafY0, -r2, r2, LeafY1, -HingeZ - 0.004, PieceKinds.Gun);
        }
        if (r2 > LeafHx + 0.03)
        {
            m.Box(-r2 + 0.004, LeafY0, -HingeZ + 0.003, -LeafHx - 0.01, LeafY1, HingeZ - 0.003, PieceKinds.Gun);
            m.Box(LeafHx + 0.01, LeafY0, -HingeZ + 0.003, r2 - 0.004, LeafY1, HingeZ - 0.003, PieceKinds.Gun);
        }
    }

    // ---- the design file and the page

    public static JsonObject Defaults() => new() { ["extend"] = DefExtend };

    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (p.Kind == Kind)
            o["extend"] = ShipDesign.R(p.Extend);
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        p.Extend = Math.Clamp(ShipDesign.F(o["extend"], DefExtend), 0, MaxExtend);
        return p;
    }

    /// <summary>The page's setPiece fields; an error text, or empty.</summary>
    public static string Set(Piece p, JsonObject o)
    {
        if (o["extend"] is not null)
            p.Extend = Math.Clamp(Math.Round(ShipDesign.F(o["extend"], DefExtend) * 20) / 20, 0, MaxExtend);
        return "";
    }

    /// <summary>The page's ramp fields: its run and the longest its size allows.</summary>
    public static string SceneJson(Piece p) => p.Kind != Kind ? "" :
        new StringBuilder(",\"extend\":").Append(ShipDesign.S(p.Extend)).Append(",\"extendMax\":")
            .Append(ShipDesign.S(Math.Min(MaxExtend, Math.Floor(SlideMax * p.Size * p.Hole * 20) / 20))).ToString();
}
