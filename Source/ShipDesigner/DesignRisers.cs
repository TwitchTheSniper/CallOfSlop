namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The designer's risers drawn (the walkable designs plan, package F): every ladder and lift between decks laid out once, here, as boxes, so
/// the page, the game's designer view and the game's parts (DesignParts.Risers.cs) show and collide as the same thing.
/// <list type="bullet">
/// <item>A <b>ladder</b> (a <see cref="Riser"/> of kind Ladder, or the ladder in a Shaft room's well: <see cref="ShaftLadders"/>) is two rails and
/// rungs on one edge of its well, the side its top landing lies (the side with most floor beyond it); it is climbed from inside the well and
/// stepped off over its top onto that floor. A riser ladder cuts a <see cref="LadderHole"/> square well through every floor over its foot. One
/// climb from its foot to its top: a deck between is passed, not stopped at.</item>
/// <item>A <b>lift</b> is a shaft (four corner posts, an amber strip round its well on each floor) and its car (a floor plate, four posts and a
/// top frame) drawn at its lowest stop; the car stops at every deck from its From to its To. Its well is cut through every floor over its
/// lowest stop, as squares that tile its W x L.</item>
/// <item>A ladder in a Shaft room over a <b>boarding hatch</b> carries the hatch's own ladder on up: its rails line up with the hatch kit's
/// (PieceKinds "Boarding hatch"), so the belly ladder and the shaft's are one climb.</item>
/// </list>
/// Coordinates: a riser's own frame has u across (the ship's x), h up from its lowest deck's floor and v along its plan (a room's Z), at its plan
/// point; <see cref="Lay.World"/> puts a point of it in the ship (a stacked design's up is -z).
/// </summary>
public static class DesignRisers
{
    public const double LadderHole = 0.45; // a riser ladder's well, half its side (over its 0.8 x 0.7 footprint)
    public const double RailX = 0.25, RailW = 0.05, RailInset = 0.02, RungStep = 0.3, RungT = 0.04; // (the boarding hatch kit's rails: x 0.25-0.3, z 0.38-0.43 in a 0.45 well)
    public const double LiftGap = 0.05; // a lift's well past its car's footprint, each side
    public const double CarLip = 0.03, CarPlate = 0.12, CarHeight = 2.2, CarInset = 0.05, PostW = 0.08, CarPostW = 0.05; // the car's floor stands CarLip over each stop's floor
    public const double StripW = 0.08, StripH = 0.012, StripLift = 0.004; // the amber strip round a well on a floor (never flush with it)
    public const string Steel = "#9aa3ad", Amber = "#d8b04a", Bad = "#ff5a44", Rung = "#5d646c";

    /// <summary>One box of a riser, in its own frame (u, h, v), its colour, and whether it is solid in the game.</summary>
    public readonly record struct RBox(double U0, double H0, double V0, double U1, double H1, double V1, string Colour, bool Solid);

    /// <summary>A riser laid out: where it stands, its stops and (a ladder) the side it is climbed from.</summary>
    public sealed class Lay
    {
        public Riser? Riser; // (null: a Shaft room's ladder)
        public int Id; // its riser's id, or the shaft room's id
        public bool Lift;
        public bool Ok = true;
        public double X, Z; // its plan point
        public double Hx, Hz; // half its car (a lift) or its well (a ladder)
        public double Hole; // a ladder's well, half its side
        public int[] Decks = []; // the decks it stops at, lowest first
        public Room?[] Rooms = []; // the room round it at each stop
        public double[] Stops = []; // each stop's floor top over its lowest deck's floor
        public int SideU, SideV; // a ladder: the way from its well to its top landing, in plan (one of them +-1)
        public Piece? Hatch; // a boarding hatch under it whose ladder this one carries on up
        public int HatchIndex = -1; // (its piece's index in the design)
        public double[] O = [0, 0, 0], U = [1, 0, 0], Up = [0, 1, 0], V = [0, 0, 1];

        public double Top => Stops[^1];

        /// <summary>A point of its own frame in the ship.</summary>
        public double[] World(double u, double h, double v) => [O[0] + U[0] * u + Up[0] * h + V[0] * v, O[1] + U[1] * u + Up[1] * h + V[1] * v, O[2] + U[2] * u + Up[2] * h + V[2] * v];
    }

    /// <summary>Every riser of the design laid out, and the ladder of each Shaft room's well (<see cref="ShaftLadders"/>).</summary>
    public static List<Lay> Lays(ShipDesign d)
    {
        var o = new List<Lay>();
        foreach (var k in d.Risers)
            if (Of(d, k) is { } l)
                o.Add(l);
        o.AddRange(ShaftLadders(d));
        return o;
    }

    /// <summary>A riser laid out (null: its decks are gone).</summary>
    public static Lay? Of(ShipDesign d, Riser k)
    {
        if (k.From < 0 || k.To < 0 || k.From >= d.Decks.Count || k.To >= d.Decks.Count || k.From == k.To)
            return null;
        var (a, b) = d.Under(k.From, k.To) ? (k.From, k.To) : (k.To, k.From);
        var (lo, hi) = (d.Decks[a].Floor, d.Decks[b].Floor);
        var decks = Enumerable.Range(0, d.Decks.Count).Where(i => Between(d.Decks[i].Floor, lo, hi)).OrderBy(i => d.Stacked ? -d.Decks[i].Floor : d.Decks[i].Floor).ToArray();
        var lift = k.Kind == "Lift";
        var (hx, hz) = k.Half;
        var l = new Lay { Riser = k, Id = k.Id, Lift = lift, X = k.X, Z = k.Z, Hx = hx, Hz = hz, Hole = LadderHole };
        Stops(d, l, decks, r => k.X - hx >= r.X0 - 0.01 && k.X + hx <= r.X1 + 0.01 && k.Z - hz >= r.Z0 - 0.01 && k.Z + hz <= r.Z1 + 0.01);
        l.Ok = l.Rooms.All(r => r is not null);
        if (!lift)
            Side(d, l);
        return l;
    }

    private static bool Between(double f, double lo, double hi) => f >= Math.Min(lo, hi) - 1e-6 && f <= Math.Max(lo, hi) + 1e-6;

    /// <summary>Its stops (the decks, the rooms round it, each floor's top) and its frame, from its lowest deck.</summary>
    private static void Stops(ShipDesign d, Lay l, int[] decks, Func<Room, bool> round)
    {
        l.Decks = decks;
        l.Rooms = [.. decks.Select(di => d.Rooms.FirstOrDefault(r => r.Deck == di && round(r)))];
        var bottom = d.Decks[decks[0]];
        l.Stops = [.. decks.Select((di, i) => Math.Abs(d.Decks[di].Floor - bottom.Floor) + (l.Rooms[i] is { } r ? DesignScene.ShellDepth(r) : 0))];
        l.O = d.World(bottom, l.X, l.Z, 0);
        var up = d.World(bottom, l.X, l.Z, 1);
        var v = d.World(bottom, l.X, l.Z + 1, 0);
        l.Up = [up[0] - l.O[0], up[1] - l.O[1], up[2] - l.O[2]];
        l.V = [v[0] - l.O[0], v[1] - l.O[1], v[2] - l.O[2]];
        l.U = [1, 0, 0];
    }

    /// <summary>
    /// A ladder's side: the way from its well whose top landing (past its rails, a person's width on) lies deepest in the room at its top,
    /// so stepping off over its top lands on floor. Ties: towards -v (the designer's old drawing had its rails there).
    /// </summary>
    private static void Side(ShipDesign d, Lay l)
    {
        var top = l.Rooms[^1];
        (int U, int V) best = (0, -1);
        var room = double.MinValue;
        foreach (var (su, sv) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
        {
            if (top is null)
                break;
            var reach = l.Hole + 0.45; // (the landing's middle: past the rails and half a person)
            double px = l.X + su * reach, pz = l.Z + sv * reach;
            var clear = Math.Min(Math.Min(px - top.X0, top.X1 - px), Math.Min(pz - top.Z0, top.Z1 - pz));
            if (clear > room + 0.01)
                (room, best) = (clear, (su, sv));
        }
        (l.SideU, l.SideV) = best;
    }

    /// <summary>
    /// The ladder in each Shaft room's well (DesignScene.Run's shaft hole, through the upper floor at the shaft's plan middle): from the lower
    /// room's floor to the upper's. Over a boarding hatch in the lower room it lines up with the hatch's own ladder (<see cref="Lay.Hatch"/>).
    /// </summary>
    public static List<Lay> ShaftLadders(ShipDesign d)
    {
        var o = new List<Lay>();
        foreach (var a in d.Rooms)
            foreach (var b in d.Rooms)
            {
                if (a.Id >= b.Id || a.Deck == b.Deck || !d.Neighbours(a.Deck, b.Deck) || a.Kind != "Shaft" && b.Kind != "Shaft" ||
                    Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0) < 0.8 || Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0) < 0.8)
                    continue;
                var s = a.Kind == "Shaft" ? a : b;
                var (low, high) = d.Under(a.Deck, b.Deck) ? (a, b) : (b, a);
                var l = new Lay { Id = s.Id, X = (s.X0 + s.X1) / 2, Z = (s.Z0 + s.Z1) / 2, Hole = DesignScene.DoorHalfDefault };
                l.Hx = l.Hz = l.Hole;
                Stops(d, l, [low.Deck, high.Deck], r => r == low || r == high);
                l.Rooms = [low, high];
                l.Stops = [DesignScene.ShellDepth(low), Math.Abs(d.Decks[high.Deck].Floor - d.Decks[low.Deck].Floor) + DesignScene.ShellDepth(high)];
                Side(d, l);
                if (!d.Stacked && HatchUnder(d, low, l) is var (hp, hi, hx, hz, su, sv))
                {
                    // On up from the boarding hatch's own ladder: its rails' line, in a well as deep as the hatch kit's.
                    (l.Hatch, l.HatchIndex, l.SideU, l.SideV) = (hp, hi, su, sv);
                    l.Hole = LadderHole * hp.Size;
                    (l.X, l.Z) = (hx, hz);
                    Stops(d, l, [low.Deck, high.Deck], r => r == low || r == high);
                    l.Rooms = [low, high];
                    l.Stops = [DesignScene.ShellDepth(low), Math.Abs(d.Decks[high.Deck].Floor - d.Decks[low.Deck].Floor) + DesignScene.ShellDepth(high)];
                }
                o.Add(l);
            }
        return o;
    }

    /// <summary>A boarding hatch under room <paramref name="r"/> whose ladder lies inside the shaft's well: the piece, its index, its well's plan middle and the way its rails face (null: none).</summary>
    private static (Piece P, int Index, double X, double Z, int SU, int SV)? HatchUnder(ShipDesign d, Room r, Lay shaft)
    {
        for (var i = 0; i < d.Pieces.Count; i++)
        {
            var p = d.Pieces[i];
            if (p.Kind != "Boarding hatch" || d.Hull(p.Hull) is not { } h || p.Mirror)
                continue;
            var w = DesignScene.Where(h, p);
            if (w.O[0] < r.X0 - 0.3 || w.O[0] > r.X1 + 0.3 || w.O[2] < r.Z0 - 0.3 || w.O[2] > r.Z1 + 0.3 || w.Y[1] > -0.9)
                continue; // (not under it, or not on a belly facing down)
            if (Math.Abs(w.O[0] - shaft.X) > shaft.Hole || Math.Abs(w.O[2] - shaft.Z) > shaft.Hole)
                continue;
            // Its rails stand on its frame's +Z side of the well: the way its landing lies.
            int su = 0, sv = 0;
            if (Math.Abs(w.Z[0]) > Math.Abs(w.Z[2]))
                su = Math.Sign(w.Z[0]);
            else
                sv = Math.Sign(w.Z[2]);
            return (p, i, w.O[0], w.O[2], su, sv);
        }
        return null;
    }

    /// <summary>A riser's well holes through the floors over its lowest stop (DesignScene.Check.Doors: holes, cut in the floor and the ceiling under it).</summary>
    public static void Holes(ShipDesign d, Lay l, List<double[]> doors)
    {
        if (l.Riser is null)
            return; // (a Shaft room's well is DesignScene.Run's own hole)
        for (var i = 1; i < l.Decks.Length; i++)
        {
            var dk = d.Decks[l.Decks[i]];
            if (!l.Lift)
            {
                doors.Add(DesignScene.DoorAt(d.World(dk, l.X, l.Z, 0), l.Hole, DesignScene.DoorTopDefault, hole: true));
                continue;
            }
            // A lift's well: squares that tile its car and its gap (a hole is square).
            double gx = l.Hx + LiftGap, gz = l.Hz + LiftGap;
            var half = Math.Min(gx, gz);
            var alongX = gx > gz;
            var span = alongX ? gx : gz;
            var n = Math.Max(1, (int)Math.Ceiling(span / half - 1e-6));
            for (var t = 0; t < n; t++)
            {
                var off = n == 1 ? 0 : -(span - half) + 2 * (span - half) * t / (n - 1);
                doors.Add(DesignScene.DoorAt(d.World(dk, l.X + (alongX ? off : 0), l.Z + (alongX ? 0 : off), 0), half, DesignScene.DoorTopDefault, hole: true));
            }
        }
    }

    /// <summary>A box across a ladder (a: across its rails, b: from its well's middle towards its landing), in its own frame.</summary>
    private static RBox Across(Lay l, double a0, double a1, double h0, double h1, double b0, double b1, string col, bool solid)
    {
        if (l.SideV != 0)
        {
            var (v0, v1) = l.SideV > 0 ? (b0, b1) : (-b1, -b0);
            return new RBox(a0, h0, v0, a1, h1, v1, col, solid);
        }
        var (u0, u1) = l.SideU > 0 ? (b0, b1) : (-b1, -b0);
        return new RBox(u0, h0, a0, u1, h1, a1, col, solid);
    }

    /// <summary>A ladder's rails (solid) and rungs from <paramref name="from"/> to <paramref name="to"/> (heights in its frame), on its well's landing edge.</summary>
    public static List<RBox> LadderBoxes(Lay l, double from, double to)
    {
        var o = new List<RBox>();
        double b1 = l.Hole - RailInset, b0 = b1 - RailW;
        foreach (var sx in new[] { -1.0, 1.0 })
            o.Add(Across(l, sx > 0 ? RailX : -RailX - RailW, sx > 0 ? RailX + RailW : -RailX, from, to, b0, b1, Amber, true));
        for (var y = from + RungStep; y < to - 0.05; y += RungStep)
            o.Add(Across(l, -RailX, RailX, y, y + RungT, b0 + 0.01, b1 - 0.01, Rung, false));
        return o;
    }

    /// <summary>What stands still of a riser: a ladder's rails and rungs, or a lift's corner posts; and the amber strips round its well on each floor over its foot.</summary>
    public static List<RBox> Fixed(ShipDesign d, Lay l)
    {
        var o = new List<RBox>();
        var strip = l.Ok ? Amber : Bad;
        if (!l.Lift)
        {
            o.AddRange(LadderBoxes(l, l.Stops[0] - 0.02, l.Top - 0.01));
            for (var i = 1; i < l.Stops.Length && l.Riser is not null; i++)
            {
                // (round its well but on the landing side, where the climber steps off)
                var y = l.Stops[i] + StripLift;
                var e = l.Hole;
                o.Add(Across(l, -e - StripW, e + StripW, y, y + StripH, -e - StripW, -e, strip, false));
                o.Add(Across(l, -e - StripW, -e, y, y + StripH, -e, e, strip, false));
                o.Add(Across(l, e, e + StripW, y, y + StripH, -e, e, strip, false));
            }
            return o;
        }
        double gx = l.Hx + LiftGap, gz = l.Hz + LiftGap;
        var topDeck = d.Decks[l.Decks[^1]];
        var postTop = l.Top + Math.Min(CarHeight + 0.25, Math.Max(0.5, topDeck.Height - 0.1 - (l.Top - Math.Abs(topDeck.Floor - d.Decks[l.Decks[0]].Floor))));
        foreach (var (su, sv) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            double pu = su * (l.Hx + 0.01), pv = sv * (l.Hz + 0.01);
            o.Add(new RBox(pu - PostW / 2, l.Stops[0] - 0.02, pv - PostW / 2, pu + PostW / 2, postTop, pv + PostW / 2, Steel, true));
        }
        for (var i = 1; i < l.Stops.Length; i++)
        {
            var y = l.Stops[i] + StripLift;
            o.Add(new RBox(-gx - StripW, y, -gz - StripW, gx + StripW, y + StripH, -gz, strip, false));
            o.Add(new RBox(-gx - StripW, y, gz, gx + StripW, y + StripH, gz + StripW, strip, false));
            o.Add(new RBox(-gx - StripW, y, -gz, -gx, y + StripH, gz, strip, false));
            o.Add(new RBox(gx, y, -gz, gx + StripW, y + StripH, gz, strip, false));
        }
        return o;
    }

    /// <summary>A lift's car at its lowest stop: its floor plate and four posts (solid) and its top frame.</summary>
    public static List<RBox> Car(Lay l)
    {
        var o = new List<RBox>();
        double cu = l.Hx - CarInset, cv = l.Hz - CarInset, top = l.Stops[0] + CarLip;
        o.Add(new RBox(-cu, top - CarPlate, -cv, cu, top, cv, Steel, true));
        double pu = cu - CarPostW, pv = cv - CarPostW, roof = top + CarHeight;
        foreach (var (su, sv) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            o.Add(new RBox(su * pu - CarPostW / 2, top + 0.002, sv * pv - CarPostW / 2, su * pu + CarPostW / 2, roof, sv * pv + CarPostW / 2, Steel, true));
        o.Add(new RBox(-cu, roof, -cv, cu, roof + 0.05, -cv + 0.05, Steel, false));
        o.Add(new RBox(-cu, roof, cv - 0.05, cu, roof + 0.05, cv, Steel, false));
        o.Add(new RBox(-cu, roof, -cv + 0.05, -cu + 0.05, roof + 0.05, cv - 0.05, Steel, false));
        o.Add(new RBox(cu - 0.05, roof, -cv + 0.05, cu, roof + 0.05, cv - 0.05, Steel, false));
        return o;
    }

    /// <summary>Boxes of a riser as one mesh in the ship (lifted <paramref name="rise"/> along its up: a car at another stop).</summary>
    public static DesignMesh Mesh(Lay l, IEnumerable<RBox> boxes, double rise = 0)
    {
        var m = new DesignMesh();
        m.Frame(l.O[0] + l.Up[0] * rise, l.O[1] + l.Up[1] * rise, l.O[2] + l.Up[2] * rise, l.U, l.Up, l.V);
        foreach (var b in boxes)
            m.Box(b.U0, b.H0, b.V0, b.U1, b.H1, b.V1, b.Colour);
        return m;
    }

    /// <summary>A box of a riser's frame as the ship has it: its two corners, lowest first on each axis.</summary>
    public static (double[] Lo, double[] Hi) InShip(Lay l, RBox b, double rise = 0)
    {
        var p = l.World(b.U0, b.H0 + rise, b.V0);
        var q = l.World(b.U1, b.H1 + rise, b.V1);
        return ([Math.Min(p[0], q[0]), Math.Min(p[1], q[1]), Math.Min(p[2], q[2])], [Math.Max(p[0], q[0]), Math.Max(p[1], q[1]), Math.Max(p[2], q[2])]);
    }
}
