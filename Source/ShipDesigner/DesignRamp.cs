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
    public const double MaxExtend = 30, DefExtend = 1.5;
    /// <summary>How many telescoping sections run out of the ramp (Karl, 2026-11-27): each nests under the one before and runs out after it,
    /// so the run is shared between them and the ramp reaches the ground from a hatch nearer the ship's end. Old designs: 1.</summary>
    public const int MinSegments = 1, MaxSegments = 5, DefSegments = 1;
    // Each further section: this much lower (thicker stack), narrower each side and shorter at its inner end than the one it nests in.
    private const double SegStepY = 0.06, SegStepX = 0.06, SegStepZ = 0.05;
    /// <summary>The ramp's set angle's range (degrees; 0 is the automatic angle, <see cref="Angle"/>) and the slope a person walks up.</summary>
    public const double MinAngle = 15, MaxAngle = 60, WalkDeg = 42;
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
    public static double Angle(Piece p, double depth) => p.RampAngle >= MinAngle ? Math.Clamp(p.RampAngle, MinAngle, MaxAngle) * Math.PI / 180 : AutoAngle(p, depth);

    /// <summary>The automatic swing (radians), from before the angle was a setting.</summary>
    public static double AutoAngle(Piece p, double depth) => Math.Clamp(Math.Atan2(1.9 + Math.Max(0, depth), p.RampL), 34 * Math.PI / 180, 40 * Math.PI / 180);

    /// <summary>One section's longest run (m): its length less what it keeps under the one it nests in, so in proportion to the ramp's.</summary>
    public static double SlideMax(Piece p) => Math.Max(0, p.RampL - (LeafInZ + ExtInZ1 + SegStepZ * (SegmentsOf(p) - 1) + Overlap) * p.Size);

    /// <summary>The piece's sections, held to the range.</summary>
    public static int SegmentsOf(Piece p) => Math.Clamp(p.Segments, MinSegments, MaxSegments);

    /// <summary>The longest total run (m) its sections give.</summary>
    public static double TotalMax(Piece p) => Math.Min(MaxExtend, SegmentsOf(p) * SlideMax(p));

    /// <summary>The total run (m) as the piece asks, held to what its sections keep hold of; each section runs an equal share (<see cref="RunOf"/>).</summary>
    public static double ExtendOf(Piece p) => Math.Clamp(p.Extend, 0, TotalMax(p));

    /// <summary>Each section's run (m) out of the one it nests in.</summary>
    public static double RunOf(Piece p) => ExtendOf(p) / SegmentsOf(p);

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
        var run = shut ? 0 : RunOf(p) / s;
        var n = SegmentsOf(p);
        for (var k = 0; k < n; k++)
        {
            // Section k nests under section k - 1 (the first under the ramp), lower, narrower and shorter, and runs out k + 1 shares.
            double off = run * (k + 1), sy = SegStepY * k, sx = extHx - SegStepX * k;
            double e0 = -hz + ExtInZ0 - hingeZ - off, e1 = hz - ExtInZ1 - SegStepZ * k - hingeZ - off;
            var last = k == n - 1;
            m.Leaf(1 + k, () =>
            {
                m.Box(-sx, ExtY0 - LeafY0 + sy, e0, sx, ExtY1 - LeafY0 + sy, e1, PieceKinds.Armour);
                if (last)
                    m.Box(-sx - 0.004, ExtY0 - LeafY0 + sy - 0.004, e0 - 0.004, sx + 0.004, ExtY1 - LeafY0 + sy + 0.004, e0 + 0.1, PieceKinds.Amber); // (its toe)
            });
        }
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

    // ---- The boarding hatch's ladder (Karl, 2026-11-27): 1 to 3 telescoping sections that come down out of its well once its lid is open
    // (DesignParts.Pieces.cs HatchDrop). It keeps its count in the piece's Segments, as "ladderSegs" in the file.

    public const string HatchKind = "Boarding hatch";
    public const int MinLadderSegments = 1, MaxLadderSegments = 3, DefLadderSegments = 1;

    /// <summary>A boarding hatch's ladder sections, held to the range.</summary>
    public static int LadderSegmentsOf(Piece p) => Math.Clamp(p.Segments, MinLadderSegments, MaxLadderSegments);

    // ---- the design file and the page

    public static JsonObject Defaults() => new() { ["extend"] = DefExtend, ["rampW"] = DefWidth, ["rampL"] = DefLength, ["rampSegs"] = DefSegments, ["rampAngle"] = 0, ["ladderSegs"] = DefLadderSegments };

    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (p.Kind == HatchKind && LadderSegmentsOf(p) != DefLadderSegments)
            o["ladderSegs"] = LadderSegmentsOf(p);
        if (p.Kind == Kind)
        {
            o["extend"] = ShipDesign.R(p.Extend);
            o["rampW"] = ShipDesign.R(p.RampW);
            o["rampL"] = ShipDesign.R(p.RampL);
            if (p.Segments != DefSegments)
                o["rampSegs"] = p.Segments;
            if (p.RampAngle > 0)
                o["rampAngle"] = ShipDesign.R(p.RampAngle);
        }
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        p.Extend = Math.Clamp(ShipDesign.F(o["extend"], DefExtend), 0, MaxExtend);
        if (p.Kind == HatchKind)
            p.Segments = LadderSegs(o, DefLadderSegments);
        if (p.Kind != Kind)
            return p;
        // A design from before the two keeps the hatch it had (the kind's opening times its size and hole size); from then on the
        // ramp's width and length are its hole and the generic hole size stays at 1.
        var was = p.Size * p.Hole;
        p.RampW = Math.Clamp(ShipDesign.F(o["rampW"], 2 * Hx * was), MinWidth, MaxWidth);
        p.RampL = Math.Clamp(ShipDesign.F(o["rampL"], 2 * Hz * was), MinLength, MaxLength);
        p.Segments = (int)Math.Clamp(Math.Round(ShipDesign.F(o["rampSegs"], DefSegments)), MinSegments, MaxSegments);
        p.RampAngle = AngleSetting(ShipDesign.F(o["rampAngle"], 0));
        p.Hole = 1;
        return p;
    }

    /// <summary>The page's setPiece fields; an error text, or empty.</summary>
    public static string Set(Piece p, JsonObject o)
    {
        if (p.Kind == HatchKind)
        {
            if (o["ladderSegs"] is not null)
                p.Segments = LadderSegs(o, LadderSegmentsOf(p));
            return "";
        }
        if (o["extend"] is not null)
            p.Extend = Math.Clamp(Math.Round(ShipDesign.F(o["extend"], DefExtend) * 20) / 20, 0, MaxExtend);
        if (o["rampW"] is not null)
            p.RampW = Math.Clamp(Math.Round(ShipDesign.F(o["rampW"], DefWidth) * 20) / 20, MinWidth, MaxWidth);
        if (o["rampL"] is not null)
            p.RampL = Math.Clamp(Math.Round(ShipDesign.F(o["rampL"], DefLength) * 20) / 20, MinLength, MaxLength);
        if (o["rampSegs"] is not null)
            p.Segments = (int)Math.Clamp(Math.Round(ShipDesign.F(o["rampSegs"], DefSegments)), MinSegments, MaxSegments);
        if (o["rampAngle"] is not null)
            p.RampAngle = AngleSetting(Math.Round(ShipDesign.F(o["rampAngle"], 0)));
        p.Hole = 1; // (its width and length are its hole)
        return "";
    }

    private static int LadderSegs(JsonObject o, int dflt) => (int)Math.Clamp(Math.Round(ShipDesign.F(o["ladderSegs"], dflt)), MinLadderSegments, MaxLadderSegments);

    /// <summary>A set angle in degrees, or 0 (the automatic one) below the range.</summary>
    private static double AngleSetting(double deg) => deg < MinAngle - 0.5 ? 0 : Math.Clamp(deg, MinAngle, MaxAngle);

    /// <summary>The page's ramp fields: its run and the longest its length allows, its width and length.</summary>
    public static string SceneJson(Piece p, double depth) => p.Kind == HatchKind ? $",\"ladderSegs\":{LadderSegmentsOf(p)}" : p.Kind != Kind ? "" :
        new StringBuilder(",\"extend\":").Append(ShipDesign.S(p.Extend)).Append(",\"extendMax\":")
            .Append(ShipDesign.S(Math.Floor(TotalMax(p) * 20) / 20))
            .Append(",\"rampSegs\":").Append(SegmentsOf(p)).Append(",\"rampAngle\":").Append(ShipDesign.S(p.RampAngle))
            .Append(",\"rampAngleNow\":").Append(ShipDesign.S(Math.Round(Angle(p, depth) * 180 / Math.PI, 1)))
            .Append(",\"rampW\":").Append(ShipDesign.S(p.RampW)).Append(",\"rampL\":").Append(ShipDesign.S(p.RampL)).ToString();
}
