using CallOfSlop.PartKit.Generators;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.ShipGen;

/// <summary>
/// The Traders' hauler (docs/sandbox/shipgen/README.md): the first hull of the procedural generator, for the Traders' first job,
/// carrying goods between markets. Its architecture is the faction's, not a kit's: a tall crew cab with a cockpit at the bow (open or sealed),
/// a thin walkable spine with container cradles hung off its flanks, a squat engineering block aft and a drive nacelle on a pylon
/// either side of it. The dice choose how many cargo bays, how high the containers stack, the hull's lines (its sections, its
/// tumblehome, the nose), where the nacelles sit and how it is painted; the job fixes the rooms (a cockpit, bunks, a head, the trade
/// office, a galley, an airlock, stores, engineering) and the slots a buyer fits: two couches, a keyed engine socket on each
/// nacelle's tail plate, a reactor socket in engineering, a sensor socket on the roof.
///
/// The hull is lofted through stations whose sections all share their facets' directions (a flat keel, 45 degree chines, a
/// vertical lower side, a tumblehome of the hull's own slope, a 45 degree upper chine, a flat roof), so every facet between two
/// stations is a flat trapezoid: no twisted quads. Ribs stand proud at every station where the hull doesn't step in, and the
/// plates stop 4 mm short of them, so no two plates' ends share a plane.
/// </summary>
public sealed class TraderHauler
{
    public static readonly string[] Stages = ["Brief", "Layout", "Hull", "Inside", "Slots", "Drive and lift", "Dress", "Pieces"];

    private const string Section = "Trader hauler";
    public const string Shared = "Trader Hauler"; // (the parts every hauler shares: cradles, jets, the hatch, the ladder, the plant, the computer)

    // ---- the Traders' paint (FactionStyle IT): old office-case beige over a greyer skin, corporate blue, a dark frame, lanyard yellow
    // where things move and where people go in, link blue lights. Inside: pale office walls over a blue dado.
    private const string Skin = "#d8d0b8", Skin2 = "#c4bca3", Armour = "#1f4e8c", Armour2 = "#2a64a8", Bare = "#8e9296", Frame = "#3b3f44",
        Dark = "#26292d", Hazard = "#f2c400", Red = "#c0392b", Green = "#2f9a52", Glass = "#86a9b8", Link = "#0f7bd8", Paper = "#e6e2d6",
        Wall = "#cdcac0", WallLow = "#2a64a8", WallTrim = "#3b3f44", Deck = "#5d6266", Grate = "#3d4144", Ceil = "#b4b6b2", RackCol = "#34383d", Soot = "#4a4644";

    // ---- what the job and the game fix (not rolled)

    private const double Ft = 0.25; // the deck's top, all along the ship
    private const double DoorH = 2.25;
    private const double CCeil = 2.75, CRoof = 3.0, Lw = 3.0; // the cab: ceiling, roof slab's top, the rooms' inner half width
    private const double SCeil = 2.6, SRoof = 2.85, Sw = 0.8, SLine = 0.06; // the spine's corridor: its half width, the lining
    private const double ECeil = 2.75, ERoof = 3.0, Ew = 2.75; // engineering
    private const double CabLen = 11.0; // the cockpit's back wall to the cab's aft wall (the rooms' plan: the Traders' crew of four)
    private const double EngLen = 8.5; // engineering's fore wall to its aft wall
    private const double BayLen = 7.6; // a cargo bay: a 6.5 m container, its cradle's hoops and the gap to the next
    private const double CtW = 2.5, CtH = 2.5, CtL = 6.5; // the Standard Container 2m
    private const double Shelf = 0.12; // a cradle's shelf
    private const double CabKeel = 2.6; // the cab's flat keel's half width: open ground starts within a grab's reach of the belly hatch's ladder
    private const double KeelY = -0.8; // the cab's and engineering's belly (the belly hatch and the jets sit on it)
    private const double LiftForce = 1500000, JetScale = 1.15, Burn = 2.5;
    private const string LegPod = "S";
    private const double PadProud = 0.1, LegProud = 0.16;
    private static double Ground => KeelY - LegProud - LegPodParts.ReachOf(LegPod);
    private const string Rcs = "Heavy RCS Thruster";
    private const string BoxName = "Standard Container 2m";
    private const int CabFuel = 4000, NacFuel = 6000;

    // The boarding shaft in the airlock (starboard, mid cab; relative to the cockpit's back wall), the belly hatch and its ladder.
    private const double HatchX0 = 1.1, HatchX1 = 2.25, HatchR0 = 5.825, HatchR1 = 7.075;
    private static double HatchX => HatchX0 + 0.575;
    private static double HatchBottom => KeelY - 0.012;
    private static double HatchTop => HatchBottom + 0.08;
    private static double HatchTravel => Math.Min(HatchBottom - Ground - 0.005, CCeil - 0.03 - Ft);
    private static double BoardRise => Ft - (HatchTop - HatchTravel);

    // ---- the run

    private readonly ShipBrief _brief;
    private readonly ChoiceReport _rep = new();
    private readonly Dice _dice;
    public readonly string Word, ShipName;
    private readonly HullPieces _hp;

    // Rolled.
    private readonly int _bays, _stack;
    private readonly double _k; // the tumblehome's slope (x in per m up)
    private readonly double _noseLen, _ckLen;
    private readonly CockpitKind _cockpit;
    private readonly Sec _nose, _cab, _spine, _eng, _transom;
    private readonly double _nacX, _nacY, _nacF, _nacTank, _nacTailOver;
    private readonly string _livery;
    private readonly string _cabTop;

    // Derived.
    private readonly double _zCkW, _zCabW, _zCabEnd, _zSp0, _zSp1, _zEng, _zEngEnd, _zStern;
    private readonly double[] _bayZ; // the bays' middles
    private readonly double _rackY; // a cradle's hardpoint: its height
    private readonly List<(double Z, Sec S, bool Rib)> _st = [];

    private readonly record struct Sec(double W, double Y0, double C0, double Yb, double Y3, double C1)
    {
        public static Sec Lerp(Sec a, Sec b, double t) => new(a.W + (b.W - a.W) * t, a.Y0 + (b.Y0 - a.Y0) * t, a.C0 + (b.C0 - a.C0) * t,
            a.Yb + (b.Yb - a.Yb) * t, a.Y3 + (b.Y3 - a.Y3) * t, a.C1 + (b.C1 - a.C1) * t);

        public double Top => Y3 + C1;
    }

    public TraderHauler(ShipBrief brief)
    {
        _brief = brief;
        _dice = new Dice(brief.Seed, _rep);
        // ---- Brief: what the job asks for.
        _rep.Note("Brief", "Job: carry standard containers between markets, with a crew of four living aboard for weeks (bunks, a head, a galley, a trade office, an airlock).");
        _rep.Note("Brief", "Sold as a hull with its slots, proved by a reference fit: two flight couches and a flight computer, a keyed 2x2 engine socket on each nacelle (a 600 kN drive and its Drive Core inside), a 2x2 reactor socket in engineering, a 1x1 sensor socket on the roof.");
        _cockpit = brief.Cockpit ?? CockpitKind.Open;
        _rep.Note("Brief", _cockpit == CockpitKind.Open
            ? "Cockpit: open, under a glass canopy" + (brief.Cockpit == null ? " (the job's own choice: a trader lands at markets and docks by eye)." : " (asked for in the brief).")
            : "Cockpit: sealed, a room inside the hull flying on screens and a nose camera" + (brief.Cockpit == null ? "." : " (asked for in the brief: a hull for routes where the glass would be shot at)."));
        var name = _dice.Stream("name", "Brief");
        Word = brief.Name ?? name.Pick("Class name", ["Tally", "Ledger", "Tender", "Remit", "Bursar", "Quire", "Folio", "Tithe"], s => s, "a Traders' word for the books; the class is known by it");
        ShipName = $"{Word} Hauler";
        _hp = new HullPieces(Word, Section);

        // ---- Layout: the bays and the containers, then the zones along the ship.
        var lay = _dice.Stream("layout", "Layout");
        _bays = lay.Int("Cargo bays", 2, 4, "each bay a container a side: 2 is a short-haul feeder, 4 a long-haul trunk ship");
        _stack = lay.Int("Containers stacked a side", 1, 2, "two high doubles the cargo but hangs the lower box under the keel line (still clear of the ground)");
        _rackY = _stack == 1 ? 1.4 : 1.45;

        // ---- Hull: the sections and the nose.
        var hull = _dice.Stream("hull", "Hull");
        _k = hull.Range("Tumblehome", 0.06, 0.16, 0.01, "how far the upper sides lean in per metre: the Traders' cab reads taller the more it leans");
        _noseLen = hull.Range("Nose length", 2.4, 3.0, 0.05, "the nose's taper from the bow to the cab's full section");
        _ckLen = hull.Range("Cockpit depth behind the nose", 1.4, 2.0, 0.05, "room behind the couches; the cockpit's back wall is the cab's first frame");
        var cabW = hull.Range("Cab half beam", 3.65, 3.95, 0.05, "the cab's rooms are 6 m across inside; the rest is void and frame");
        var cabUp = hull.Range("Cab upper chine", 3.15, 3.45, 0.05, "where the tall sides turn in to the roof: a higher chine is a boxier cab");
        _cab = new Sec(cabW, KeelY, Math.Round(cabW - CabKeel, 3), 1.6, cabUp, hull.Range("Cab roof chamfer", 0.5, 0.8, 0.05, "the roof's edge"));
        _nose = new Sec(hull.Range("Nose half width", 1.8, 2.2, 0.05, "the windscreen's width at the bow"), 0.0, 0.45, 1.0, 2.25, 0.4);
        var spW = hull.Range("Spine half beam", 1.2, 1.35, 0.05, "just round the 1.6 m corridor: the spine is a walkway with a hull, not a hold");
        _spine = new Sec(spW, -0.4, 0.35, 1.5, 2.95, 0.4);
        var engW = hull.Range("Engineering half beam", 3.45, 3.7, 0.05, "engineering is lower and wider than the cab: the plant and the pylons' roots");
        _eng = new Sec(engW, KeelY, 0.8, 1.4, 3.1, 0.6);
        _transom = new Sec(engW - 0.9, -0.3, 0.5, 1.2, 2.5, 0.45);
        // (every section's room must fit inside it: checked below with the rest)

        // ---- Drive and lift: the nacelles.
        var drive = _dice.Stream("drive", "Drive and lift");
        _nacF = drive.Range("Nacelle flat", 0.95, 1.15, 0.05, "the nacelle's octagon to its flats: room round a Drive Core S (0.6 m round) and a 2 m engine socket on its tail");
        _nacX = Math.Round(engW + 1.4 + _nacF + drive.Range("Nacelle stand-off", 0.0, 0.8, 0.1, "clear air between engineering and the nacelle; wider is easier to fit a drive past"), 2);
        _nacY = drive.Range("Nacelle height", 0.5, 1.2, 0.05, "on engineering's straight lower side, where the pylon meets a flat skin");
        _nacTank = drive.Range("Nacelle tank length", 5.0, 7.0, 0.5, "fuel for the drive and the nacelle's jets");
        _nacTailOver = drive.Range("Nacelle tail past the transom", 0.3, 1.2, 0.1, "the drives' bells clear of the stern's jets");

        // ---- Dress.
        var dress = _dice.Stream("dress", "Dress");
        _livery = dress.Pick("Livery", ["blue keel", "blue belt", "blue crown"], s => s, "where the Traders' blue goes on the beige: under the chines, a band at the beam, or over the shoulders");
        _cabTop = dress.Pick("Cab roof", ["plain", "antenna farm", "sun visor"], s => s, "what stands on the cab's roof behind the sensor");

        // ---- The zones along the ship (bow at z 0).
        _zCkW = Math.Round(_noseLen + _ckLen, 3);
        _zCabW = _zCkW + CabLen;
        _zCabEnd = _zCabW + 0.125;
        _zSp0 = _zCabEnd + 1.6;
        var bay0 = _zSp0 + 0.3;
        _bayZ = [.. Enumerable.Range(0, _bays).Select(i => Math.Round(bay0 + BayLen * (i + 0.5), 3))];
        _zSp1 = Math.Round(bay0 + BayLen * _bays + 0.3, 3);
        _zEng = _zSp1 + 1.6;
        _zEngEnd = _zEng + EngLen + 0.25; // (the fore wall's fore face to the aft wall's aft face)
        _zStern = _zEngEnd + 1.4;
        _rep.Note("Layout", $"Zones from the bow: {(_cockpit == CockpitKind.Open ? "glass nose and open cockpit" : "plated nose and sealed cockpit")} 0 to {Fmt(_zCkW)} m, the crew cab to {Fmt(_zCabEnd)} m, a neck, the spine with {_bays} bays {Fmt(_zSp0)} to {Fmt(_zSp1)} m, a neck, engineering {Fmt(_zEng)} to {Fmt(_zEngEnd)} m, the stern to {Fmt(_zStern)} m.");
        _rep.Note("Layout", $"Cargo: {_bays * 2 * _stack} standard containers ({_bays} bays, one cradle a side each, {_stack} high), hung off the spine's flanks with their hatches outboard, so a crane or a shell takes them out sideways.");
        _rep.Note("Layout", "One deck all along (its top 0.25 m up), so the crew walk from the cockpit to the engines without a step: cab, spine corridor, engineering.");

        // Stations: (z, section, rib). A rib stands at every station the hull doesn't step in at.
        _st.Add((0, _nose, true));
        _st.Add((_noseLen, _cab, true));
        _st.Add((_zCkW, _cab, true));
        _st.Add((Math.Round(_zCkW + 5.5, 3), _cab, true));
        _st.Add((_zCabEnd, _cab, true));
        _st.Add((_zSp0, _spine, false));
        _st.Add((Math.Round(bay0, 3), _spine, true));
        for (var i = 1; i <= _bays; i++)
            _st.Add((Math.Round(bay0 + BayLen * i, 3), _spine, true));
        _st.Add((_zSp1, _spine, false)); // (the neck aft of it widens: a rib there would be buried)
        _st.Add((_zEng, _eng, true));
        _st.Add((Math.Round(_zEng + 4.25, 3), _eng, true));
        _st.Add((_zEngEnd, _eng, true));
        _st.Add((_zStern, _transom, true));
        _hp.ZShift = Math.Round(_zStern / 2 / 0.25) * 0.25;
        Fits();
    }

    // ---- the loft

    private (double X, double Y) Pt(Sec s, int k) => k switch
    {
        0 => (s.W - s.C0, s.Y0),
        1 => (s.W, s.Y0 + s.C0),
        2 => (s.W, s.Yb),
        3 => (s.W - _k * (s.Y3 - s.Yb), s.Y3),
        _ => (s.W - _k * (s.Y3 - s.Yb) - s.C1, s.Y3 + s.C1),
    };

    /// <summary>The section's half width at height y (0 above its roof or under its keel).</summary>
    private double Half(Sec s, double y)
    {
        if (y < s.Y0 - 1e-9 || y > s.Top + 1e-9)
            return 0;
        if (y < s.Y0 + s.C0)
            return s.W - s.C0 + (y - s.Y0);
        if (y <= s.Yb)
            return s.W;
        if (y <= s.Y3)
            return s.W - _k * (y - s.Yb);
        return s.W - _k * (s.Y3 - s.Yb) - (y - s.Y3);
    }

    private Sec At(double z)
    {
        if (z <= _st[0].Z)
            return _st[0].S;
        for (var i = 0; i < _st.Count - 1; i++)
            if (z <= _st[i + 1].Z)
                return Sec.Lerp(_st[i].S, _st[i + 1].S, (z - _st[i].Z) / (_st[i + 1].Z - _st[i].Z));
        return _st[^1].S;
    }

    /// <summary>The skin's least half width over a z range and a y range (the section is convex and the loft linear, so the ends and the stations between hold it).</summary>
    private double SkinMin(double z0, double z1, double y0, double y1)
    {
        var zs = new List<double> { z0, z1 };
        zs.AddRange(_st.Select(s => s.Z).Where(z => z > z0 && z < z1));
        return zs.Min(z => Math.Min(Half(At(z), y0), Half(At(z), y1)));
    }

    /// <summary>A point of the section at z: k 0..4 up the side s (+1 starboard).</summary>
    private double[] Q(double z, int k, int s)
    {
        var (x, y) = Pt(At(z), k);
        return [s * x, y, z];
    }

    private const int Roof = 4, Belly = 5;

    /// <summary>A facet's edge at z: facets 0..3 up the side s, Roof (port to starboard), Belly.</summary>
    private (double[] A, double[] B) Edge(double z, int f, int s) => f switch
    {
        Roof => (Q(z, 4, -1), Q(z, 4, 1)),
        Belly => (Q(z, 0, -1), Q(z, 0, 1)),
        _ => (Q(z, f, s), Q(z, f + 1, s)),
    };

    /// <summary>The facet's outward normal in the section (2D, x and y).</summary>
    private static double[] Out2(int f, int s) => f switch
    {
        Roof => [0, 1, 0],
        Belly => [0, -1, 0],
        0 => [s * 0.7071, -0.7071, 0],
        1 => [s, 0, 0],
        3 => [s * 0.7071, 0.7071, 0],
        _ => [0, 0, 0],
    };

    private double[] Out(int f, int s) => f == 2 ? HullPieces.Unit([s * 1.0, _k, 0]) : Out2(f, s);

    /// <summary>The outward normal of a facet between za and zb (with the taper's z part).</summary>
    private double[] Normal(double za, double zb, int f, int s)
    {
        var (a, b) = Edge(za, f, s);
        var (c, _) = Edge(zb, f, s);
        var n = HullPieces.Unit(HullPieces.Cross(HullPieces.Sub(b, a), HullPieces.Sub(c, a)));
        return HullPieces.Dot(n, Out(f, s)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>A point on a facet: u along it from za to zb, v across it from its edge's A to B, d out from the skin's middle.</summary>
    private double[] On(double za, double zb, int f, int s, double u, double v, double d)
    {
        var z = za + (zb - za) * u;
        var (a, b) = Edge(z, f, s);
        var q = HullPieces.Lerp(a, b, v);
        return HullPieces.Add(q, Normal(za, zb, f, s), d);
    }

    private bool RibAt(double z) => _st.Any(t => Math.Abs(t.Z - z) < 1e-6 && t.Rib);

    /// <summary>A facet's plate between za and zb, stopped 4 mm short of a rib at either end.</summary>
    private void Facet(double za, double zb, int f, int s, string color, string finish = "painted plating", bool glass = false, double u0 = 0, double u1 = 1)
    {
        double a = za + (zb - za) * u0, b = za + (zb - za) * u1;
        var odd = f is 1 or 3 or Belly; // (neighbouring facets' ends are staggered, so no two end caps share a plane)
        if (u0 == 0)
            a += RibAt(za) ? (odd ? 0.0065 : 0.004) : (odd ? 0.0025 : 0);
        if (u1 == 1)
            b -= RibAt(zb) ? (odd ? 0.0065 : 0.004) : (odd ? 0.0025 : 0);
        var (p0, p1) = Edge(a, f, s);
        var (q0, q1) = Edge(b, f, s);
        _hp.Quad(p0, p1, q1, q0, color, finish, glass);
    }

    /// <summary>A raised slab on a facet (a panel, a porthole's rim, a livery band): u, v its extent, from d0 to d1 off the skin's middle.</summary>
    private void Slab(double za, double zb, int f, int s, double u0, double u1, double v0, double v1, double d0, double d1, string color, string finish = "painted plating", bool glow = false)
    {
        double[][] lo = [On(za, zb, f, s, u0, v0, d0), On(za, zb, f, s, u0, v1, d0), On(za, zb, f, s, u1, v1, d0), On(za, zb, f, s, u1, v0, d0)];
        double[][] hi = [On(za, zb, f, s, u0, v0, d1), On(za, zb, f, s, u0, v1, d1), On(za, zb, f, s, u1, v1, d1), On(za, zb, f, s, u1, v0, d1)];
        _hp.Prism(lo, hi, color, finish, glow);
    }

    /// <summary>
    /// A rib's segment on facet f at station z, proud of the plates. Its ends are mitred on the bisector of the corner they meet, so
    /// nothing runs past a corner (the old ends ran 5 cm straight on and poked out through the next facet's rib: the beading Karl saw
    /// clip, 2026-11-22). Neighbours alternate shallow and deep; a shallow one runs 1.5 cm past the mitre into its deep neighbour,
    /// which it stays inside, so the joint is closed and no two faces share a plane. The keel has no rib: there the end is just mitred.
    /// </summary>
    private void RibSeg(double z, int f, int s, string color)
    {
        var (a, b) = Edge(z, f, s);
        var dir = HullPieces.Unit(HullPieces.Sub(b, a));
        var n = Out(f, s);
        var even = f == Roof || f % 2 == 0;
        double din = HullPieces.T / 2 + 0.006, dout = din + (even ? 0.07 : 0.085), hw = even ? 0.09 : 0.0935; // (neighbours differ in depth and width)
        // The facets either side: at A the one below (the keel's under facet 0), at B the one above (the roof over facet 3).
        var (fa, sa) = f switch { Roof => (3, -1), Belly => (0, -1), 0 => (Belly, s), _ => (f - 1, s) };
        var (fb, sb) = f switch { Roof => (3, 1), Belly => (0, 1), 3 => (Roof, s), _ => (f + 1, s) };
        double[] End(double[] c, int fn, int sn, double d, double away)
        {
            var m = Out(fn, sn);
            var cos = n[0] * m[0] + n[1] * m[1];
            var k = d / (1 + cos);
            var ext = even && fn != Belly ? 0.015 : 0.0;
            return [c[0] + (n[0] + m[0]) * k + dir[0] * away * ext, c[1] + (n[1] + m[1]) * k + dir[1] * away * ext, c[2]];
        }
        double[] P(double[] q, double dz) => [q[0], q[1], q[2] + dz];
        double[] ai = End(a, fa, sa, din, -1), ao = End(a, fa, sa, dout, -1), bi = End(b, fb, sb, din, 1), bo = End(b, fb, sb, dout, 1);
        _hp.Prism([P(ai, -hw), P(bi, -hw), P(bo, -hw), P(ao, -hw)], [P(ai, hw), P(bi, hw), P(bo, hw), P(ao, hw)], color, "dirty steel");
    }

    /// <summary>A chine rail along the corner k (1 or 3) of side s from za to zb: a strip standing off both plates, stopped short of the ribs.</summary>
    private void Rail(double za, double zb, int k, int s, string color)
    {
        za += RibAt(za) ? 0.1 : 0.01;
        zb -= RibAt(zb) ? 0.1 : 0.01;
        if (zb - za < 0.3)
            return;
        var n1 = Out(k - 1, s);
        var n2 = Out(k, s);
        var n = HullPieces.Unit(HullPieces.Add(n1, n2));
        double[] Pt3(double z, double d, double w)
        {
            var q = Q(z, k, s);
            var along = HullPieces.Unit(HullPieces.Sub(n2, n1));
            return [q[0] + n[0] * d + along[0] * w, q[1] + n[1] * d + along[1] * w, z];
        }
        double d0 = HullPieces.T / 2 + 0.01, d1 = d0 + 0.05;
        _hp.Prism([Pt3(za, d0, -0.05), Pt3(za, d0, 0.05), Pt3(za, d1, 0.05), Pt3(za, d1, -0.05)],
            [Pt3(zb, d0, -0.05), Pt3(zb, d0, 0.05), Pt3(zb, d1, 0.05), Pt3(zb, d1, -0.05)], color, "painted steel");
    }

    /// <summary>The void between the rooms and the skin as hidden boxes (rounds stop at the skin): side s from x xin, sliced a metre at most along z.</summary>
    private void Void(double za, double zb, double ya, double yb, double xin, int s)
    {
        var n = Math.Max(1, (int)Math.Ceiling((zb - za) / 1.0));
        for (var i = 0; i < n; i++)
        {
            double a = za + (zb - za) * i / n, b = za + (zb - za) * (i + 1) / n;
            var xo = Math.Floor((SkinMin(a, b, ya, yb) - 0.06) * 100) / 100;
            if (xo < xin + 0.05)
                continue;
            _hp.Hidden(s * xin, ya, a + (i == 0 ? 0.004 : 0), s * xo, yb, b - (i == n - 1 ? 0.004 : 0));
        }
    }

    /// <summary>A hidden fill across the hull (both sides) between ya and yb, inside the skin.</summary>
    private void Fill(double za, double zb, double ya, double yb, double trim = 0.004)
    {
        var n = Math.Max(1, (int)Math.Ceiling((zb - za) / 1.0));
        for (var i = 0; i < n; i++)
        {
            double a = za + (zb - za) * i / n, b = za + (zb - za) * (i + 1) / n;
            var xo = Math.Floor((SkinMin(a, b, ya, yb) - 0.06) * 100) / 100;
            if (xo < 0.1)
                continue;
            _hp.Hidden(-xo, ya, a + (i == 0 ? trim : 0), xo, yb, b - (i == n - 1 ? trim : 0));
        }
    }

    /// <summary>Checks the rooms fit their sections (the generator's own rule: a room never pokes through the skin).</summary>
    private void Fits()
    {
        void Must(string what, double z0, double z1, double hx, double y0, double y1)
        {
            var s = SkinMin(z0, z1, y0, y1);
            if (s < hx + 0.1)
                _rep.Problem($"{what}: the skin is {s:0.00} m out at its narrowest, the room needs {hx + 0.1:0.00}");
        }
        Must("cab", _zCkW - 0.125, _zCabEnd, Lw + 0.25, 0, CRoof);
        Must("spine", _zCabEnd, _zEng, Sw + SLine, 0, SRoof);
        Must("engineering", _zEng, _zEngEnd, Ew + 0.25, 0, ERoof);
        Must("cockpit", _noseLen * 0.4, _zCkW - 0.125, 1.6, 0, 0.95);
        if (_cockpit == CockpitKind.Sealed)
            Must("sealed cockpit", SealedFront - 0.1, _zCkW - 0.125, 1.55, Ft, Ft + 2.4);
    }

    // ---- the pieces, zone by zone

    private int BandOf(double z)
    {
        for (var i = 0; i < _st.Count - 1; i++)
            if (z >= _st[i].Z - 1e-6 && z <= _st[i + 1].Z + 1e-6)
                return i;
        throw new InvalidOperationException($"z {z} is off the hull");
    }

    private Part Keel(int b) => _hp.Piece($"Keel {b}", $"A length of the {ShipName}'s keel: the deck there, the tankage under it, the belly plating and the mounts on it.", Deck, "fueltank");
    private Part Side(int b, int s) => _hp.Piece($"Side {(s < 0 ? "L" : "R")} {b}", $"A length of the {ShipName}'s side {(s < 0 ? "to port" : "to starboard")}: the plating, its frame and chine rails, the void behind it.", Skin);
    private Part RoofP(int b) => _hp.Piece($"Roof {b}", $"A length of the {ShipName}'s roof: the plating, the ceiling under it and the void between.", Skin);

    private string ColourOf(int f, double z)
    {
        var spine = z > _zCabEnd + 0.2 && z < _zEng - 0.2;
        var baseCol = spine ? Skin2 : Skin;
        return _livery switch
        {
            "blue keel" => f is 0 or Belly ? Armour : baseCol,
            "blue belt" => f == 1 && !spine ? Armour : f == Belly ? Frame : baseCol,
            _ => f is 3 or Roof && !spine ? Armour : f == Belly ? Frame : baseCol,
        };
    }

    /// <summary>One band's skin (plates, ribs at its fore station, chine rails), dealt to its pieces; windows where glass is asked for.</summary>
    private void Skin_(int b, Func<int, int, bool>? glass = null, Part? only = null, bool belly = true)
    {
        double za = _st[b].Z, zb = _st[b + 1].Z;
        foreach (var s in new[] { -1, 1 })
            _hp.In(only ?? Side(b, s), () =>
            {
                for (var f = 0; f < 4; f++)
                {
                    var g = glass?.Invoke(f, s) ?? false;
                    Facet(za, zb, f, s, g ? Glass : ColourOf(f, za), g ? "glass" : "painted plating", g);
                    if (_st[b].Rib && b > 0)
                        RibSeg(za, f, s, f == 1 ? Armour2 : Frame);
                }
                if (!(glass?.Invoke(2, s) ?? false))
                    Rail(za, zb, 1, s, Frame);
                if (!(glass?.Invoke(3, s) ?? false))
                    Rail(za, zb, 3, s, Dark);
            });
        _hp.In(only ?? RoofP(b), () =>
        {
            var g = glass?.Invoke(Roof, 0) ?? false;
            Facet(za, zb, Roof, 0, g ? Glass : ColourOf(Roof, za), g ? "glass" : "painted plating", g);
            if (_st[b].Rib && b > 0)
                RibSeg(za, Roof, 0, Frame);
        });
        if (belly)
            _hp.In(only ?? Keel(b), () => Facet(za, zb, Belly, 0, ColourOf(Belly, za), "plated steel"));
    }

    /// <summary>A pad for an attitude jet: a square solid standing proud off the surface point, sunk 11 cm in; its outer face takes the socket.</summary>
    private void RcsPad(string name, double x, double y, double z, string dir, double proud)
    {
        var s = dir[0] == '-' ? -1 : 1;
        var axis = dir[1] - 'x';
        double[] at = [x, y, z];
        double[] lo = [x - 0.3, y - 0.3, z - 0.3], hi = [x + 0.3, y + 0.3, z + 0.3];
        lo[axis] = Math.Min(at[axis] - s * 0.11, at[axis] + s * proud);
        hi[axis] = Math.Max(at[axis] - s * 0.11, at[axis] + s * proud);
        at[axis] += s * proud;
        _hp.Solid(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], Frame, "dirty steel");
        _hp.Sock(name, at[0], at[1], at[2], dir);
    }

    /// <summary>A mount under the belly: a square solid sunk 11 cm in and standing proud; its underside takes the socket.</summary>
    private void MountPad(string name, double x, double z, double half, double proud = PadProud)
    {
        _hp.Solid(x - half, KeelY - proud, z - half, x + half, KeelY + 0.11, z + half, Frame, "dirty steel");
        _hp.Sock(name, x, KeelY - proud, z, "-y");
    }

    private readonly record struct Gap(double A, double W, double Sill, double Head);

    private static Gap Door(double a) => new(a, 1.5, 0, DoorH);

    /// <summary>A wall 0.25 m thick centred on c, along x (alongX) or z from a0 to a1, from y0 to y1, with its doorways; a blue dado, dark jambs and a hazard head on each doorway.</summary>
    private void WallRun(bool alongX, double a0, double a1, double c, double y0, double y1, params Gap[] gaps)
    {
        void Run(double a, double b, double ya, double yb, string col, string fin, double th, bool detail)
        {
            if (b - a < 0.02 || yb - ya < 0.02)
                return;
            if (alongX)
            {
                if (detail) _hp.Detail(a, ya, c - th, b, yb, c + th, col, fin);
                else _hp.Solid(a, ya, c - th, b, yb, c + th, col, fin);
            }
            else if (detail) _hp.Detail(c - th, ya, a, c + th, yb, b, col, fin);
            else _hp.Solid(c - th, ya, a, c + th, yb, b, col, fin);
        }
        var at = a0;
        foreach (var g in gaps.OrderBy(g => g.A))
        {
            Run(at, g.A, y0, y1, Wall, "painted plating", 0.125, false);
            Run(g.A, g.A + g.W, g.Head + y0, y1, Wall, "painted plating", 0.125, false);
            Run(at + 0.05, g.A - 0.08, y0 + 0.003, y0 + 0.75, WallLow, "painted plating", 0.137, true);
            Run(g.A - 0.07, g.A, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A + g.W, g.A + g.W + 0.07, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A, g.A + g.W, y0 + g.Head, y0 + g.Head + 0.07, Hazard, "hazard paint", 0.145, true);
            at = g.A + g.W;
        }
        Run(at, a1, y0, y1, Wall, "painted plating", 0.125, false);
        Run(at + (gaps.Length > 0 ? 0.08 : 0.05), a1 - 0.05, y0 + 0.003, y0 + 0.75, WallLow, "painted plating", 0.137, true);
    }

    /// <summary>The socket a door hangs by in a doorway that starts at a: on the wall's centre line.</summary>
    private void DoorSock(string name, bool alongX, double a, double c) =>
        _hp.Sock(name, alongX ? a : c, Ft + 1.125, alongX ? c : a, alongX ? "+x" : "+z");

    private void CeilStrip(double x0, double x1, double z0, double z1, double ceil) =>
        _hp.Detail(x0, ceil - 0.028, z0, x1, ceil - 0.003, z1, "#d8f0ff", glow: true);

    // ---- the nose and the cockpit

    private void NoseAndCockpit()
    {
        var open = _cockpit == CockpitKind.Open;
        var nose = _hp.Piece("Nose", open
            ? $"The {ShipName}'s glass nose and open cockpit: the windscreen, the bumper ring round it, the dash, the side consoles under the glass and the overhead."
            : $"The {ShipName}'s plated nose and sealed cockpit: the nose cap with its camera, and the cockpit room inside, its screen wall, desks, walls and ceiling.", open ? Glass : Skin);
        double z1 = _noseLen;
        // The nose band: glass over the beam (open) or plating all round (sealed), the cap at the bow, a bumper ring round it.
        _hp.In(nose, () =>
        {
            foreach (var s in new[] { -1, 1 })
                for (var f = 0; f < 4; f++)
                {
                    var g = open && f >= 2;
                    Facet(0, z1, f, s, g ? Glass : ColourOf(f, 0), g ? "glass" : "painted plating", g);
                }
            Facet(0, z1, Roof, 0, open ? Glass : ColourOf(Roof, 0), open ? "glass" : "painted plating", open);
            Facet(0, z1, Belly, 0, ColourOf(Belly, 0), "plated steel");
            // The cap: under the beam skin, over it glass (or all skin), a hair aft of z 0 (the bumper ring is at the bow).
            var sec = At(0);
            double[][] Ring(double y0, double y1)
            {
                var pts = new List<double[]>();
                for (var k = 0; k <= 4; k++)
                {
                    var (x, y) = Pt(sec, k);
                    if (y >= y0 - 1e-6 && y <= y1 + 1e-6)
                        pts.Add([x, y, 0.004]);
                }
                pts.Insert(0, [Half(sec, y0), y0, 0.004]);
                pts.Add([Half(sec, y1), y1, 0.004]);
                var right = pts.DistinctBy(q => (Math.Round(q[0], 4), Math.Round(q[1], 4))).ToList();
                var left = right.AsEnumerable().Reverse().Select(q => new[] { -q[0], q[1], q[2] });
                return [.. right, .. left];
            }
            if (open)
            {
                _hp.Plate(Ring(sec.Y0, 0.95), ColourOf(1, 0), "painted plating");
                _hp.Plate(Ring(0.954, sec.Top), Glass, "glass", glass: true);
            }
            else
                _hp.Plate(Ring(sec.Y0, sec.Top), ColourOf(1, 0), "painted plating");
            for (var f = 0; f < 4; f++)
                foreach (var s in new[] { -1, 1 })
                    RibSeg(0, f, s, f == 1 ? Hazard : Dark);
            RibSeg(0, Roof, 0, Dark);
            if (open)
            {
                // Glazing bars down the windscreen and across the cap.
                foreach (var s in new[] { -1, 1 })
                {
                    Slab(0, z1, 2, s, 0.03, 0.97, 0.48, 0.52, HullPieces.T / 2 + 0.003, HullPieces.T / 2 + 0.03, Dark, "painted steel");
                    Slab(0, z1, 3, s, 0.03, 0.97, 0.0, 0.04, HullPieces.T / 2 + 0.003, HullPieces.T / 2 + 0.03, Dark, "painted steel");
                }
                _hp.Detail(-0.03, 0.96, -0.02, 0.03, sec.Top - 0.1, -0.004, Dark, "painted steel");
            }
            else
            {
                // The nose camera the sealed cockpit flies by, in a dark sensor band across the cap: an armoured housing, its lens, a nav
                // lamp each side; a hazard-striped bumper under it where the glass would be on an open nose.
                var bw = Math.Floor((Half(sec, 1.85) - 0.2) * 100) / 100;
                _hp.Detail(-bw, 1.4, -0.03, bw, 1.9, -0.012, Dark, "painted steel");
                _hp.Detail(-0.3, 1.45, -0.08, 0.3, 1.85, -0.025, Frame, "painted steel");
                var hw0 = Math.Floor((Half(sec, 0.95) - 0.25) * 100) / 100;
                for (var i = 0; i * 0.3 < 2 * hw0 - 0.15; i++)
                    _hp.Detail(-hw0 + i * 0.3, 0.8, -0.03, Math.Min(-hw0 + i * 0.3 + 0.15, hw0), 0.95, -0.012, i % 2 == 0 ? Hazard : Dark, "hazard paint");
                _hp.Detail(-0.12, 1.55, -0.093, 0.12, 1.75, -0.075, "#1a2a38", "glass");
                _hp.Detail(-0.05, 1.62, -0.098, 0.05, 1.68, -0.09, "#40d0ff", glow: true);
                foreach (var s in new[] { -1, 1 })
                    _hp.Detail(s * 0.55, 1.6, -0.045, s * 0.75, 1.7, -0.025, s < 0 ? Red : Green, glow: true);
            }
            // The cockpit floor and the void ahead of the cockpit.
            _hp.Solid(-1.6, 0, 0.5, 1.6, Ft, z1, Deck, "painted deck plate");
            Fill(0, 0.5, sec.Y0 + 0.05, 1.9);
            if (open)
            {
                // The dash across the bow, its screens and dials; the glass's backing (what stops a body at the glass), the void under the dash.
                _hp.Solid(-1.45, Ft, 0.5, 1.45, 0.95, 0.95, Dark, "plastic");
                foreach (var (x0, x1, col) in new[] { (-1.25, -0.4, "#78c8e8"), (-0.3, 0.3, "#60e090"), (0.4, 1.25, "#78c8e8") })
                    _hp.Detail(x0, 0.953, 0.55, x1, 0.969, 0.9, col, glow: true);
                for (var i = 0; i < 9; i++)
                    _hp.Detail(-0.8 + i * 0.2, 0.6, 0.952, -0.72 + i * 0.2, 0.68, 0.965, i % 3 == 0 ? Hazard : Paper, "plastic");
                Backing(0.5, z1, _ => nose, nose);
            }
            Void(0.5, z1, 0, 0.95, 1.6, -1);
            Void(0.5, z1, 0, 0.95, 1.6, 1);
        });
        // The band behind the nose: the cockpit's side glass (open: the tumblehome and the upper chine) over the cab's livery, its floor, the voids.
        var b = 1;
        _hp.In(nose, () =>
        {
            _hp.Solid(-1.6, 0, z1, 1.6, Ft, _zCkW - 0.125, Deck, "painted deck plate");
            Fill(z1, _zCkW - 0.125, KeelY + 0.05, 0);
        });
        if (open)
            Backing(z1, _zCkW - 0.125, s => Side(b, s), RoofP(b));
        _hp.In(RoofP(b), () => Fill(_zCkW - 0.125, _zCkW, CCeil, Math.Min(At(_zCkW - 0.125).Top, At(_zCkW).Top) - 0.06, 0)); // (over the cockpit's back wall)
        foreach (var s in new[] { -1, 1 })
            _hp.In(Side(b, s), () => Void(z1, _zCkW - 0.125, 0, 0.95, 1.6, s));
        Skin_(b, (f, s) => open && f is 2 or 3, belly: true);
        _hp.In(Keel(b), () => _hp.Solid(-Lw - 0.25, 0, _zCkW - 0.125, Lw + 0.25, Ft, _zCkW, Deck, "painted deck plate"));
        _hp.In(nose, () =>
        {
            if (open)
                OpenCockpit(z1);
            else
                SealedCockpit();
        });
    }

    // The open cockpit's layout: everything the glass shows is finished. Consoles run down both sides from the dash to the back wall,
    // their sloped desks rising to the sill at the glass's foot; a ceiling and an overhead under the plated roof; a bulkhead face over
    // the back wall up to the roof. Under the sill and over the bulkhead the hull is the room's wall (Karl, 2026-11-22: the open cockpit
    // is where the hull may be the interior).
    private const double ConsoleX = 1.47, DeskY = 1.05; // the consoles' inner face (clear of the dash's end) and their desks' inner edge

    private void OpenCockpit(double z1)
    {
        double back = _zCkW - 0.125;
        // One console a band each side (the loft is straight within a band, so each is one convex solid); a frame strip over the joint.
        foreach (var s in new[] { -1, 1 })
        {
            Console_(0.506, z1 - 0.002, s, 0);
            Console_(z1 + 0.002, back - 0.004, s, 0.004);
            double[][] J(double z) => ConsoleSection(z, s, 0.004, 0.006);
            _hp.Prism(J(z1 - 0.025), J(z1 + 0.025), Frame, "painted steel");
        }
        // The ceiling under the plated roof of the band behind the nose, a light strip down it, the overhead between the couches.
        var top = Math.Min(At(z1).Top, At(back).Top) - 0.12;
        var tw = Math.Floor((Pt(At(back), 4).X - 0.12) * 100) / 100;
        _hp.Detail(-tw, top - 0.025, z1 + 0.06, tw, top - 0.004, back - 0.006, Ceil, "painted plating");
        double oy = top - 0.025, oz0 = back - 1.7, oz1 = back - 0.5;
        var overhead = oz0 > z1 + 0.2;
        if (Math.Min(overhead ? oz0 - 0.1 : back - 0.3, back - 0.3) - (z1 + 0.4) > 0.3)
            CeilStrip(-0.1, 0.1, z1 + 0.4, overhead ? oz0 - 0.1 : back - 0.3, oy);
        if (overhead)
        {
            _hp.Detail(-0.45, oy - 0.22, oz0, 0.45, oy - 0.003, oz1, Frame, "plastic");
            for (var i = 0; i < 6; i++)
                _hp.Detail(-0.36 + i * 0.13, oy - 0.236, oz0 + 0.2, -0.28 + i * 0.13, oy - 0.218, oz0 + 0.5, i % 3 == 1 ? Hazard : Paper, "plastic");
            _hp.Detail(-0.3, oy - 0.234, oz0 + 0.65, 0.3, oy - 0.218, oz1 - 0.15, "#78c8e8", glow: true);
        }
        // The bulkhead's face over the back wall, from the wall's top to the ceiling, following the section.
        var sec = At(back);
        double y0 = CCeil + 0.004, y1 = top - 0.03, zf = back - 0.009, za = back - 0.005;
        var ys = new List<double> { y0 };
        if (sec.Y3 > y0 && sec.Y3 < y1)
            ys.Add(sec.Y3);
        ys.Add(y1);
        var face = new List<double[]>();
        foreach (var y in ys)
            face.Add([Half(sec, y) - 0.1, y, 0]);
        foreach (var y in Enumerable.Reverse(ys))
            face.Add([-(Half(sec, y) - 0.1), y, 0]);
        _hp.Prism([.. face.Select(q => new[] { q[0], q[1], zf })], [.. face.Select(q => new[] { q[0], q[1], za })], Wall, "painted plating");
        _hp.Detail(-0.6, CCeil + 0.15, zf - 0.012, 0.6, CCeil + 0.45, zf - 0.002, "#78c8e8", glow: true); // (the trade board: prices at the markets on the route)
    }

    /// <summary>A side console's section at z (side s): its inner face at ConsoleX, a desk sloping from DeskY up to the sill at the glass's
    /// foot, its outer side down the hull's inside; grown by d all round and moved in by dx.</summary>
    private double[][] ConsoleSection(double z, int s, double dx, double d)
    {
        var sec = At(z);
        var sill = sec.Yb;
        double xi = ConsoleX + dx - d, yt = Math.Min(DeskY, sill - 0.1) + d, yb = Ft - 0.03 - d;
        var gap = HullPieces.T / 2 + 0.025 - d;
        var pts = new List<(double X, double Y)> { (xi, yb), (xi, yt), (Half(sec, sill) - gap, sill - 0.005 + d) };
        var ych = sec.Y0 + sec.C0;
        if (ych > yb + 0.02 && ych < sill - 0.02)
            pts.Add((Half(sec, ych) - gap, ych));
        pts.Add((Half(sec, yb) - gap, yb));
        return [.. pts.Select(p => new[] { s * p.X, p.Y, z })];
    }

    /// <summary>A side console from za to zb: its body, the desk's dark top with its screens, the sill along the glass's foot.</summary>
    private void Console_(double za, double zb, int s, double dx)
    {
        _hp.Prism(ConsoleSection(za, s, dx, 0), ConsoleSection(zb, s, dx, 0), WallLow, "painted plating");
        // The desk: a slab on the slope from its inner edge up to the sill, the screens on it, the sill's padded strip.
        double[] OnDesk(double z, double t, double d)
        {
            var sec = At(z);
            var sill = sec.Yb;
            double xi = ConsoleX + dx, yt = Math.Min(DeskY, sill - 0.1), xo = Half(sec, sill) - (HullPieces.T / 2 + 0.025), yo = sill - 0.005;
            double ex = xo - xi, ey = yo - yt, l = Math.Sqrt(ex * ex + ey * ey);
            double nx = -ey / l, ny = ex / l; // (the slope's normal, up and inboard)
            return [s * (xi + ex * t + nx * d), yt + ey * t + ny * d, z];
        }
        void Pad(double z0, double z1, double t0, double t1, double d0, double d1, string col, string? finish, bool glow = false)
        {
            double[][] lo = [OnDesk(z0, t0, d0), OnDesk(z0, t1, d0), OnDesk(z1, t1, d0), OnDesk(z1, t0, d0)];
            double[][] hi = [OnDesk(z0, t0, d1), OnDesk(z0, t1, d1), OnDesk(z1, t1, d1), OnDesk(z1, t0, d1)];
            _hp.Prism(lo, hi, col, finish ?? "plastic", glow);
        }
        Pad(za + 0.03, zb - 0.03, 0.04, 0.86, 0.003, 0.012, Frame, "plastic");
        Pad(za + 0.01, zb - 0.01, 0.88, 1.0, 0.003, 0.03, Dark, "rubber");
        var n = Math.Max(1, (int)Math.Floor((zb - za - 0.1) / 0.9));
        var w = (zb - za - 0.1) / n;
        for (var i = 0; i < n; i++)
        {
            double a = za + 0.05 + i * w + 0.08, c = za + 0.05 + (i + 1) * w - 0.08;
            if (c - a < 0.2)
                continue;
            Pad(a, c, 0.25, 0.75, 0.014, 0.022, (i + (s > 0 ? 1 : 0)) % 2 == 0 ? "#78c8e8" : "#60e090", null, glow: true);
            Pad(a, a + Math.Min(0.25, (c - a) / 3), 0.08, 0.2, 0.014, 0.024, Hazard, "plastic");
        }
    }

    // The sealed cockpit: a room inside the hull with no glass (a combat ship's kind). A screen wall ahead (the nose camera's picture
    // and the instruments), desks either side of the flight computer, lined walls and a ceiling; the nose ahead of the room is void.
    private double SealedFront => Math.Round(_zCkW - 2.75, 3);

    private void SealedCockpit()
    {
        double zf = SealedFront, back = _zCkW - 0.125, hw = 1.55, ceil = Ft + 2.3, th = 0.1;
        // The walls: the screen wall ahead, the sides, the ceiling (pale office walls over a blue dado, as in the cab).
        _hp.Solid(-hw, Ft, zf - th, hw, ceil + th, zf, Wall, "painted plating");
        foreach (var s in new[] { -1, 1 })
        {
            _hp.Solid(s * (hw - th), Ft, zf, s * hw, ceil, back, Wall, "painted plating");
            _hp.Detail(s * (hw - th - 0.014), Ft, zf + 0.004, s * (hw - th - 0.002), Ft + 0.9, back - 0.004, WallLow, "painted plating");
            _hp.Detail(s * (hw - th - 0.018), Ft + 0.9, zf + 0.004, s * (hw - th - 0.001), Ft + 0.95, back - 0.004, WallTrim, "painted steel");
        }
        _hp.Solid(-hw, ceil, zf, hw, ceil + th, back, Ceil, "painted plating");
        CeilStrip(-0.12, 0.12, zf + 0.3, back - 0.3, ceil);
        // The screen wall: the camera's picture over the middle, the instruments either side, a strip of status lamps under them.
        double sz = zf + 0.003, sz1 = zf + 0.016;
        _hp.Detail(-1.35, Ft + 0.95, zf + 0.002, 1.35, ceil - 0.15, zf + 0.008, Dark, "plastic");
        _hp.Detail(-0.7, Ft + 1.05, sz + 0.006, 0.7, Ft + 1.95, sz1 + 0.004, "#40d0ff", glow: true);
        foreach (var s in new[] { -1, 1 })
        {
            _hp.Detail(s * 0.8, Ft + 1.35, sz + 0.006, s * 1.25, Ft + 1.95, sz1 + 0.004, "#78c8e8", glow: true);
            _hp.Detail(s * 0.8, Ft + 1.05, sz + 0.006, s * 1.25, Ft + 1.28, sz1 + 0.004, "#60e090", glow: true);
        }
        for (var i = 0; i < 11; i++)
            _hp.Detail(-1.0 + i * 0.2, Ft + 0.985, sz + 0.006, -0.94 + i * 0.2, Ft + 1.015, sz1, i % 4 == 0 ? Red : i % 2 == 0 ? Hazard : "#60e090", glow: true);
        // The desks either side of the flight computer, their tops' screens and switches.
        foreach (var s in new[] { -1, 1 })
        {
            _hp.Solid(s * 0.5, Ft, zf, s * 1.4, Ft + 0.75, zf + 0.55, Dark, "plastic");
            _hp.Detail(s * 0.6, Ft + 0.753, zf + 0.08, s * 1.3, Ft + 0.766, zf + 0.4, "#78c8e8", glow: true);
            for (var i = 0; i < 4; i++)
                _hp.Detail(s * (0.62 + i * 0.17), Ft + 0.753, zf + 0.44, s * (0.7 + i * 0.17), Ft + 0.77, zf + 0.5, i % 2 == 0 ? Hazard : Paper, "plastic");
        }
    }

    /// <summary>The glass's backing over the dash from za to zb: thin hidden walls just inside the side glass and a hidden ceiling under the roof glass.</summary>
    private void Backing(double za, double zb, Func<int, Part> side, Part roof)
    {
        var n = Math.Max(1, (int)Math.Ceiling((zb - za) / 0.6));
        for (var i = 0; i < n; i++)
        {
            double a = za + (zb - za) * i / n + 0.002, c = za + (zb - za) * (i + 1) / n - 0.002;
            var hx = Math.Floor((SkinMin(a, c, 0.95, 2.4) - 0.1) * 100) / 100;
            foreach (var s in new[] { -1, 1 })
                _hp.In(side(s), () => _hp.Hidden(s * (hx - 0.08), 0.95, a, s * hx, 2.4, c));
            var top = Math.Min(At(a).Top, At(c).Top) - 0.12;
            var tw = Math.Floor((SkinMin(a, c, top, top) - 0.06) * 100) / 100;
            _hp.In(roof, () => _hp.Hidden(-tw, top - 0.08, a, tw, top, c));
        }
        if (za < 0.6) // (in front of the dash: the nose's glass is the wall)
            _hp.In(roof, () => _hp.Hidden(-1.4, 0.95, 0.05, 1.4, 1.9, 0.5));
    }

    // ---- the cab: the crew's rooms off a corridor

    private void Cab()
    {
        double R(double r) => _zCkW + r;
        int b2 = BandOf(R(2.0)), b3 = BandOf(R(8.0));
        var walls = _hp.Piece("Cab Walls", $"The {ShipName}'s cab walls: the cockpit's back wall, the corridor's walls with the rooms' doors, the rooms' cross walls and the aft wall.", Wall);
        // Floors (each keel band's), with the boarding shaft through band 3; the ceilings; the keel's tank under the floor.
        foreach (var bb in new[] { b2, b3 })
        {
            double za = _st[bb].Z, zb = _st[bb + 1].Z;
            _hp.In(Keel(bb), () =>
            {
                var shaft = bb == b3;
                if (!shaft)
                {
                    _hp.Solid(-Lw - 0.25, 0, za == _zCkW ? za : za, Lw + 0.25, Ft, zb, Deck, "painted deck plate");
                    Fill(za, zb, KeelY + 0.05, 0);
                }
                else
                {
                    double h0 = R(HatchR0), h1 = R(HatchR1);
                    _hp.Solid(-Lw - 0.25, 0, za, Lw + 0.25, Ft, h0, Deck, "painted deck plate");
                    _hp.Solid(-Lw - 0.25, 0, h1, Lw + 0.25, Ft, zb, Deck, "painted deck plate");
                    _hp.Solid(-Lw - 0.25, 0, h0, HatchX0, Ft, h1, Deck, "painted deck plate");
                    _hp.Solid(HatchX1, 0, h0, Lw + 0.25, Ft, h1, Deck, "painted deck plate");
                    var xo = Math.Floor((SkinMin(za, zb, KeelY + 0.05, 0) - 0.06) * 100) / 100;
                    var hy = HatchTop + 0.006; // (over the hatch's edges, which lap the shaft's by 2 cm)
                    _hp.Hidden(-xo, hy, za + 0.004, xo, 0, h0);
                    _hp.Hidden(-xo, hy, h1, xo, 0, zb - 0.004);
                    _hp.Hidden(-xo, hy, h0, HatchX0, 0, h1);
                    _hp.Hidden(HatchX1, hy, h0, xo, 0, h1);
                    // The shaft's lining.
                    _hp.Detail(HatchX0, KeelY + 0.05, h0, HatchX0 + 0.012, Ft - 0.004, h1, Bare, "painted steel");
                    _hp.Detail(HatchX1 - 0.012, KeelY + 0.05, h0, HatchX1, Ft - 0.004, h1, Bare, "painted steel");
                    _hp.Detail(HatchX0 + 0.012, KeelY + 0.05, h0, HatchX1 - 0.012, Ft - 0.004, h0 + 0.012, Bare, "painted steel");
                    _hp.Sock("boarding", HatchX, Ft - 0.125, h1, "-z");
                }
                // Fuel under the floor: the jets on this keel draw from it.
                _hp.Cur.Set(("fuelTank", Kw(("capacity", CabFuel / 2))));
            });
            _hp.In(RoofP(bb), () =>
            {
                double c0 = Math.Max(za, _zCkW - 0.125), c1 = Math.Min(zb, _zCabEnd);
                _hp.Solid(-Lw - 0.25, CCeil, c0, Lw + 0.25, CRoof, c1, Ceil, "painted plating");
                Fill(c0, c1, CRoof, At(za).Top - 0.06, 0.004);
            });
            foreach (var s in new[] { -1, 1 })
                _hp.In(Side(bb, s), () => Void(za, zb, 0, CRoof, Lw + 0.25, s));
            Skin_(bb, belly: bb != b3);
        }
        // The belly round the boarding shaft: four plates (their thicknesses a hair apart, so their faces never share a plane).
        _hp.In(Keel(b3), () =>
        {
            double za = _st[b3].Z + 0.004, zb = _st[b3 + 1].Z - 0.004, h0 = R(HatchR0) - 0.02, h1 = R(HatchR1) + 0.02;
            var x = Pt(_cab, 0).X;
            var y = KeelY;
            void Flat(double x0, double z0, double x1, double z1, double th) =>
                _hp.Plate([[x0, y, z0], [x1, y, z0], [x1, y, z1], [x0, y, z1]], ColourOf(Belly, za), "plated steel", thick: th);
            Flat(-x, za, HatchX0 - 0.02, zb, 0.04);
            Flat(HatchX1 + 0.02, za, x, zb, 0.044);
            Flat(HatchX0 - 0.02, za, HatchX1 + 0.02, h0, 0.048);
            Flat(HatchX0 - 0.02, h1, HatchX1 + 0.02, zb, 0.052);
        });
        // The walls.
        _hp.In(walls, () =>
        {
            WallRun(true, -Lw - 0.25, Lw + 0.25, _zCkW, Ft, CCeil, Door(-0.75));
            foreach (var s in new[] { -1, 1 })
                WallRun(false, _zCkW + 0.125, _zCabW - 0.125, s * (Lw + 0.125), Ft, CCeil);
            WallRun(false, _zCkW + 0.125, _zCabW - 0.125, -0.875, Ft, CCeil, Door(R(0.725)), Door(R(4.925)), Door(R(8.225)));
            WallRun(false, _zCkW + 0.125, _zCabW - 0.125, 0.875, Ft, CCeil, Door(R(1.025)), Door(R(7.025)), Door(R(9.125)));
            foreach (var r in new[] { 4.5, 7.75 })
                WallRun(true, -Lw, -1.0, R(r), Ft, CCeil);
            foreach (var r in new[] { 4.0, 8.75 })
                WallRun(true, 1.0, Lw, R(r), Ft, CCeil);
            WallRun(true, -Lw - 0.25, Lw + 0.25, _zCabW, Ft, CCeil, Door(-0.75));
            // The rail round the boarding shaft, open at its aft edge where the ladder lands people; the cycle lights.
            double h0 = R(HatchR0), h1 = R(HatchR1);
            _hp.Solid(HatchX0 - 0.06, Ft, h0 - 0.06, HatchX1 + 0.06, Ft + 1.0, h0, Frame, "painted steel");
            _hp.Solid(HatchX0 - 0.06, Ft, h0, HatchX0, Ft + 1.0, h1 - 0.3, Frame, "painted steel");
            _hp.Solid(HatchX1, Ft, h0, HatchX1 + 0.06, Ft + 1.0, h1 - 0.3, Frame, "painted steel");
            _hp.Detail(HatchX0 - 0.075, Ft + 0.96, h0 - 0.075, HatchX1 + 0.075, Ft + 1.04, h0 + 0.015, Hazard, "hazard paint");
            _hp.Detail(HatchX0 - 0.12, Ft + 0.003, h1, HatchX1 + 0.12, Ft + 0.015, h1 + 0.12, Hazard, "hazard paint");
            _hp.Detail(1.003, Ft + DoorH + 0.15, R(7.7), 1.019, Ft + DoorH + 0.3, R(8.0), Red, glow: true);
            _hp.Detail(0.731, Ft + DoorH + 0.15, R(7.7), 0.747, Ft + DoorH + 0.3, R(8.0), "#40e070", glow: true);
            // The corridor: a grating runner, light strips.
            _hp.Detail(-0.6, Ft + 0.003, _zCkW + 0.3, 0.6, Ft + 0.015, _zCabW - 0.3, Grate, "painted grating");
            CeilStrip(-0.12, 0.12, _zCkW + 0.6, _zCabW - 0.6, CCeil);
            // The doors' sockets, and every room's fittings' (the Traders' crew cab: the slots a buyer fills).
            DoorSock("doorCk", true, -0.75, _zCkW);
            DoorSock("doorAft", true, -0.75, _zCabW);
            DoorSock("doorBunk", false, R(0.725), -0.875);
            DoorSock("doorHead", false, R(4.925), -0.875);
            DoorSock("doorOffice", false, R(8.225), -0.875);
            DoorSock("doorGalley", false, R(1.025), 0.875);
            DoorSock("doorAirlock", false, R(7.025), 0.875);
            DoorSock("doorStores", false, R(9.125), 0.875);
            double lx = _cockpit == CockpitKind.Open ? 1.4 : 1.0, ly = _cockpit == CockpitKind.Open ? 2.2 : 1.95; // (under the sealed room's ceiling)
            _hp.Sock("lampCkL", -lx, Ft + ly, R(-0.125), "-z");
            _hp.Sock("lampCkR", lx, Ft + ly, R(-0.125), "-z");
            _hp.Sock("switchCk", 1.1, Ft + 1.3, R(-0.125), "-z");
            _hp.Sock("lockerBunk", -1.5, Ft + 0.125, R(0.125), "+z");
            _hp.Sock("lampBunk", -Lw, Ft + 2.2, R(2.225), "+x");
            _hp.Sock("switchBunk", -1.0, Ft + 1.3, R(0.425), "-x");
            _hp.Sock("toilet", -Lw, Ft + 0.125, R(5.275), "+x");
            _hp.Sock("basin", -Lw, Ft + 0.125, R(6.175), "+x");
            _hp.Sock("shower", -2.45, Ft + 0.125, R(7.625), "-z");
            _hp.Sock("lampHead", -Lw, Ft + 2.3, R(5.625), "+x");
            _hp.Sock("switchHead", -1.0, Ft + 1.3, R(7.025), "-x");
            _hp.Sock("cabinet", -2.6, Ft + 0.125, R(7.875), "+z");
            _hp.Sock("lampOffice", -Lw, Ft + 2.3, R(8.725), "+x");
            _hp.Sock("switchOffice", -1.0, Ft + 1.3, R(10.025), "-x");
            _hp.Sock("range", 2.3, Ft + 0.45, R(0.125), "+z");
            _hp.Sock("coffee", 1.4, Ft + 0.125, R(0.125), "+z");
            _hp.Sock("lampGalley", Lw, Ft + 2.2, R(2.025), "-x");
            _hp.Sock("switchGalley", 1.0, Ft + 1.3, R(0.425), "+x");
            _hp.Sock("mmuDock", 2.35, Ft + 0.125, R(4.125), "+z");
            _hp.Sock("suit0", Lw, Ft + 0.125, R(6.575), "-x");
            _hp.Sock("suit1", Lw, Ft + 0.125, R(8.075), "-x");
            _hp.Sock("switchHatch", 1.0, Ft + 1.45, R(6.325), "+x");
            _hp.Sock("lampAirlock", Lw, Ft + 2.3, R(5.025), "-x");
            _hp.Sock("lifeSupport", Lw, Ft + 0.125, R(9.425), "-x");
            _hp.Sock("o2Tank0", Lw, Ft + 0.125, R(10.325), "-x");
            _hp.Sock("lampStores", 2.0, Ft + 2.3, R(10.875), "-z");
            _hp.Sock("lampHallA", -0.75, Ft + 2.3, R(3.625), "+x");
            _hp.Sock("lampHallB", 0.75, Ft + 2.3, R(5.625), "-x");
        });
        // What stands on the floor: the couches, the computer, the bunks, the office's desk and racks, the galley's table.
        _hp.In(_hp.Named("Nose"), () =>
        {
            _hp.Sock("pilot", -0.8, Ft, R(-1.275), "+y");
            _hp.Sock("copilot", 0.8, Ft, R(-1.275), "+y");
            _hp.Sock("computer", 0, Ft, R(-2.375), "+y");
        });
        _hp.In(Keel(b2), () =>
        {
            _hp.Sock("bunk0", -2.45, Ft, R(1.175), "+y");
            _hp.Sock("bunk1", -2.45, Ft, R(3.325), "+y");
            _hp.Sock("table", 2.3, Ft, R(2.525), "+y");
        });
        _hp.In(Keel(b3), () =>
        {
            _hp.Sock("computerDesk", -2.85, Ft, R(9.425), "+y");
            _hp.Sock("server0", -2.675, Ft, R(10.225), "+y");
            _hp.Sock("server1", -1.975, Ft, R(10.225), "+y");
        });
        _rep.Note("Inside", _cockpit == CockpitKind.Open
            ? "Cockpit (open): the dash across the bow; a console down each side from the dash to the back wall, its desk sloping up to the sill at the glass's foot with screens and switches on it; a ceiling and an overhead panel under the roof behind the nose; a bulkhead face over the back wall with the trade board. Under the sill and over the bulkhead the hull is the room's wall, finished; nothing bare shows through the glass."
            : $"Cockpit (sealed): a room {Fmt(_zCkW - 0.125 - SealedFront)} m deep inside the hull, 2.9 m across and 2.3 m high: a screen wall ahead (the nose camera's picture and the instruments), desks either side of the flight computer, walls lined like the cab's and a ceiling with a light strip. The nose ahead of it is void.");
        _rep.Note("Inside", "Cab: the cockpit (two couches and the flight computer behind the dash), then a corridor; to port the bunk room, the head and the trade office (its computer and two server racks: the market's books), to starboard the galley, the airlock over the boarding shaft (belly hatch, ladder, MMU dock, two suit lockers) and the stores (life support, oxygen).");
    }

    // ---- the necks and the spine

    private void Spine()
    {
        for (var bb = BandOf(_zCabEnd + 0.01); bb < _st.Count - 1 && _st[bb].Z < _zEng - 1e-6; bb++)
        {
            double za = _st[bb].Z, zb = _st[bb + 1].Z;
            var band = bb;
            _hp.In(Keel(band), () =>
            {
                _hp.Solid(-Sw, 0, za, Sw, Ft, zb, Deck, "painted deck plate");
                var yb = Math.Min(At(za).Y0, At(zb).Y0);
                Fill(za, zb, Math.Max(At(za).Y0, At(zb).Y0) + 0.05, 0);
                _hp.Detail(-0.5, Ft + 0.003, za + 0.2, 0.5, Ft + 0.015, zb - 0.2, Grate, "painted grating");
                _hp.Cur.D["role"] = "frame";
            });
            _hp.In(RoofP(band), () =>
            {
                _hp.Solid(-Sw, SCeil, za, Sw, SRoof, zb, Ceil, "painted plating");
                Fill(za, zb, SRoof, Math.Min(At(za).Top, At(zb).Top) - 0.06);
                if (zb - za > 1.5)
                {
                    CeilStrip(-0.1, 0.1, za + 0.5, zb - 0.3, SCeil);
                    _hp.Sock($"lampSp{band}", 0.3, SCeil, Math.Round((za + zb) / 2, 2), "-y");
                }
            });
            foreach (var s in new[] { -1, 1 })
                _hp.In(Side(band, s), () =>
                {
                    // The lining is the corridor's wall, a length a band, so a side shot off opens the corridor.
                    var face = s * Sw;
                    double xa = Math.Min(face, face + s * SLine), xb = Math.Max(face, face + s * SLine);
                    _hp.Solid(xa, 0, za, xb, Ft + 0.9, zb, WallLow, "painted plating");
                    _hp.Solid(xa, Ft + 0.9, za, xb, SRoof, zb, Wall, "painted plating");
                    _hp.Detail(face - s * 0.09, Ft + 1.0, za + 0.25, face - s * 0.05, Ft + 1.05, zb - 0.25, Hazard, "worn paint");
                    for (var z = za + 0.5; z < zb - 0.3; z += 1.5)
                        _hp.Detail(face - s * 0.06, Ft + 1.0, z - 0.03, face - s * 0.004, Ft + 1.04, z + 0.03, Frame, "painted steel");
                    _hp.Detail(face - s * 0.02, Ft + 0.87, za + 0.004, face - s * 0.004, Ft + 0.93, zb - 0.004, WallTrim, "painted steel");
                    if (s < 0)
                        foreach (var (y, col) in new[] { (2.15, Red), (2.28, Link), (2.41, Bare) })
                            _hp.Detail(face + 0.014, y, za + 0.2, face + 0.09, y + 0.07, zb - 0.2, col, "painted steel");
                    Void(za, zb, 0, SRoof, Sw + SLine, s);
                });
            Skin_(band);
        }
        // The hardpoints: one a side at each bay's middle, a block through the skin; a cradle hangs on its face.
        for (var i = 0; i < _bays; i++)
        {
            var z = _bayZ[i];
            foreach (var s in new[] { -1, 1 })
                _hp.In(Side(BandOf(z), s), () =>
                {
                    var face = Math.Round(_spine.W + 0.15, 2);
                    _hp.Solid(s * (Sw + SLine), _rackY - 0.42, z - 0.36, s * face, _rackY + 0.42, z + 0.36, Frame, "dirty steel");
                    _hp.Sock($"cradle{(s < 0 ? "L" : "R")}{i}", s * face, _rackY, z, s < 0 ? "-x" : "+x");
                });
        }
        _rep.Note("Inside", $"Spine: a 1.6 m corridor inside the hull itself (its lining is the wall, a length a band), a handrail and the ship's pipes down it, a lamp in each band's ceiling; doors at both ends.");
    }

    // ---- engineering and the stern

    private void Engineering()
    {
        double zW = _zEng + 0.125, zAW = _zEngEnd - 0.125;
        double R(double r) => zW + r;
        var walls = _hp.Piece("Eng Walls", $"The {ShipName}'s engineering walls: the fore wall with its door, the sides and the aft wall.", Wall);
        foreach (var bb in new[] { BandOf(_zEng + 1), BandOf(_zEngEnd - 1) })
        {
            double za = _st[bb].Z, zb = _st[bb + 1].Z;
            _hp.In(Keel(bb), () =>
            {
                _hp.Solid(-Ew - 0.25, 0, za, Ew + 0.25, Ft, zb, Deck, "painted deck plate");
                Fill(za, zb, KeelY + 0.05, 0);
                _hp.Cur.Set(("fuelTank", Kw(("capacity", CabFuel / 2))));
            });
            _hp.In(RoofP(bb), () =>
            {
                _hp.Solid(-Ew - 0.25, ECeil, za, Ew + 0.25, ERoof, zb, Ceil, "painted plating");
                Fill(za, zb, ERoof, Math.Min(At(za).Top, At(zb).Top) - 0.06);
            });
            foreach (var s in new[] { -1, 1 })
                _hp.In(Side(bb, s), () => Void(za, zb, 0, ERoof, Ew + 0.25, s));
            Skin_(bb);
        }
        _hp.In(walls, () =>
        {
            WallRun(true, -Ew - 0.25, Ew + 0.25, zW, Ft, ECeil, Door(-0.75));
            foreach (var s in new[] { -1, 1 })
                WallRun(false, zW + 0.125, zAW - 0.125, s * (Ew + 0.125), Ft, ECeil);
            WallRun(true, -Ew - 0.25, Ew + 0.25, zAW, Ft, ECeil);
            // The fuel manifolds up each side wall to the pylons, a hazard line round the reactor's place.
            foreach (var s in new[] { -1, 1 })
            {
                _hp.Detail(s * (Ew - 0.004), Ft + 0.004, R(3.8), s * (Ew - 0.12), CCeil - 0.004, R(3.95), Red, "painted steel");
                _hp.Detail(s * (Ew - 0.004), Ft + 1.6, R(3.0), s * (Ew - 0.1), Ft + 1.7, R(5.5), Red, "painted steel");
            }
            CeilStrip(-0.12, 0.12, zW + 0.6, zAW - 0.6, ECeil);
            DoorSock("doorEng", true, -0.75, zW);
            _hp.Sock("panel", -Ew, Ft + 0.6, R(1.925), "+x");
            _hp.Sock("board", -1.9, Ft + 0.9, zW + 0.125, "+z");
            _hp.Sock("suit", -2.2, Ft + 0.125, zAW - 0.125, "-z");
            _hp.Sock("locker", -1.4, Ft + 0.125, zAW - 0.125, "-z");
            _hp.Sock("tools", -Ew, Ft + 0.9, R(6.125), "+x");
            _hp.Sock("lampEngL", -Ew, Ft + 2.3, R(5.025), "+x");
            _hp.Sock("lampEngR", Ew, Ft + 2.3, R(5.325), "-x");
            _hp.Sock("switchEng", 1.2, Ft + 1.3, zW + 0.125, "+z");
        });
        _hp.In(Keel(BandOf(R(1.5))), () =>
        {
            for (var i = 0; i < 3; i++)
                _hp.Sock($"battery{i}", Ew - 0.25, Ft, R(1.125 + i * 0.7), "+y");
        });
        _hp.In(Keel(BandOf(R(6.975))), () =>
        {
            _hp.Detail(1.55 - 1.1, Ft + 0.003, R(6.975) - 1.1, 1.55 + 1.1, Ft + 0.019, R(6.975) - 1.04, Hazard, "hazard paint");
            _hp.Sock("reactor", 1.55, Ft, R(6.975), "+y");
        });
        // The pylons' roots: a block through engineering's straight lower side at the nacelle's height.
        var pz = Math.Round(_zEng + 4.25 + 1.06, 2);
        foreach (var s in new[] { -1, 1 })
            _hp.In(Side(BandOf(pz), s), () =>
            {
                _hp.Solid(s * (Ew + 0.25), _nacY - 0.253, pz - 1.0, s * _eng.W, _nacY + 0.253, pz + 1.0, Frame, "dirty steel"); // (0.253: never level with the wall's dado at 1.0 m)
                _hp.Sock(s < 0 ? "nacL" : "nacR", s * _eng.W, _nacY, pz, s < 0 ? "-x" : "+x");
            });
        _rep.Note("Inside", "Engineering: the reactor socket with its plant, the Engineer's Panel, the Power Board, three batteries, a suit locker and a Pressure Locker for the walk out to the nacelles, a tool rack; the fuel manifolds up the walls to the pylons.");
    }

    private void Stern()
    {
        var b = _st.Count - 2;
        var stern = _hp.Piece("Stern", $"The {ShipName}'s stern: the transom behind engineering, its tail lights and the stern's attitude jets.", Skin);
        Skin_(b, only: stern);
        double za = _st[b].Z, zb = _st[b + 1].Z;
        var sec = At(zb);
        _hp.In(stern, () =>
        {
            var pts = new List<double[]>();
            for (var k = 0; k <= 4; k++)
            {
                var (x, y) = Pt(sec, k);
                pts.Add([x, y, zb - 0.004]);
            }
            var cap = pts.Concat(pts.AsEnumerable().Reverse().Select(q => new[] { -q[0], q[1], q[2] })).ToArray();
            _hp.Plate(cap, Frame, "dirty steel");
            for (var f = 0; f < 4; f++)
                foreach (var s in new[] { -1, 1 })
                    RibSeg(zb, f, s, Dark);
            RibSeg(zb, Roof, 0, Dark);
            Fill(za, zb, Math.Max(At(za).Y0, sec.Y0) + 0.05, Math.Min(At(za).Top, sec.Top) - 0.06, 0); // (against engineering's aft wall)
            // The transom's lights (white), the stern's jets every way.
            foreach (var s in new[] { -1, 1 })
            {
                var n = s < 0 ? "L" : "R";
                var s2 = At(zb - 0.5);
                var (xu, yu) = Pt(s2, 4);
                var (x0, y0) = Pt(s2, 0);
                RcsPad("sRcsUp" + n, s * Math.Min(1.0, xu - 0.4), yu, zb - 0.5, "+y", 0.05);
                RcsPad("sRcsDown" + n, s * Math.Min(1.0, x0 - 0.4), y0, zb - 0.5, "-y", 0.05);
                RcsPad("sRcsOut" + n, s * Pt(s2, 2).X, (s2.Y0 + s2.C0 + s2.Yb) / 2, zb - 0.5, s < 0 ? "-x" : "+x", 0.05);
                RcsPad("sRcsAlong" + n, s * 1.4, 1.2, zb + 0.016, "+z", 0.05);
                _hp.Detail(s * 2.0 - 0.15, 2.0, zb + 0.02, s * 2.0 + 0.15, 2.15, zb + 0.045, "#fff4e0", glow: true);
            }
            _hp.Detail(-0.6, 0.4, zb + 0.02, 0.6, 0.55, zb + 0.036, Hazard, "hazard paint");
        });
    }

    // ---- the bow's jets, the jets and legs under the keel, the roof's gear

    private void Mounts()
    {
        double R(double r) => _zCkW + r;
        double ze0 = _zEng + 0.125;
        _hp.In(Keel(BandOf(R(2.6))), () =>
        {
            MountPad("leg0", -2.0, R(2.6), 0.4, LegProud);
            MountPad("leg1", 2.0, R(2.6), 0.4, LegProud);
            RcsPad("rcsDownL", -0.55, KeelY, R(0.8), "-y", 0.05);
            RcsPad("rcsDownR", 0.55, KeelY, R(0.8), "-y", 0.05);
        });
        _hp.In(Keel(BandOf(R(8.6))), () =>
        {
            MountPad("lift0", -1.5, R(8.6), 0.45);
            MountPad("lift1", 1.5, R(8.6), 0.45);
        });
        _hp.In(Keel(BandOf(ze0 + 3.5)), () =>
        {
            MountPad("lift2", -1.5, ze0 + 3.5, 0.45);
            MountPad("lift3", 1.5, ze0 + 3.5, 0.45);
        });
        _hp.In(Keel(BandOf(ze0 + 7.5)), () =>
        {
            MountPad("leg2", -2.0, ze0 + 7.5, 0.4, LegProud);
            MountPad("leg3", 2.0, ze0 + 7.5, 0.4, LegProud);
        });
        // The bow's jets: up on the cab's roof, out on its flanks (down: under the keel, above).
        var bz = R(1.2);
        _hp.In(RoofP(BandOf(bz)), () =>
        {
            RcsPad("rcsUpL", -1.2, _cab.Top, bz, "+y", 0.056);
            RcsPad("rcsUpR", 1.2, _cab.Top, bz, "+y", 0.056);
        });
        foreach (var s in new[] { -1, 1 })
            _hp.In(Side(BandOf(bz), s), () => RcsPad("rcsOut" + (s < 0 ? "L" : "R"), s * _cab.W, 0.8, bz, s < 0 ? "-x" : "+x", 0.28));
        // The roof's sensor socket over the cab's middle, and its gear.
        var sz = R(7.0);
        _hp.In(RoofP(BandOf(sz)), () =>
        {
            _hp.Solid(-0.55, _cab.Top - 0.11, sz - 0.55, 0.55, _cab.Top + 0.06, sz + 0.55, Frame, "dirty steel");
            _hp.Sock("sensor", 0, _cab.Top + 0.06, sz, "+y");
        });
    }

    // ---- dressing the outside

    private void Dress()
    {
        double R(double r) => _zCkW + r;
        // Portholes in the cab's upper sides (the bunk room, the galley, the office, the head): a dark rim, a glass disc.
        foreach (var (r, s) in new[] { (1.6, -1), (3.0, -1), (9.3, -1), (1.8, 1), (9.9, 1), (5.9, -1) })
        {
            var z = R(r);
            var b = BandOf(z);
            double za = _st[b].Z, zb = _st[b + 1].Z, len = zb - za, u = (z - za) / len, du = 0.3 / len;
            _hp.In(Side(b, s), () =>
            {
                Slab(za, zb, 2, s, u - du - 0.06 / len, u + du + 0.06 / len, 0.3, 0.72, HullPieces.T / 2 + 0.004, HullPieces.T / 2 + 0.05, Dark, "painted steel");
                Slab(za, zb, 2, s, u - du, u + du, 0.36, 0.66, HullPieces.T / 2 + 0.05, HullPieces.T / 2 + 0.062, "#24384a", "glass");
            });
        }
        // The nav lights at the cab's widest (red to port, green to starboard), the Traders' mark aft of them, a pinstripe.
        foreach (var s in new[] { -1, 1 })
        {
            var b = BandOf(R(10.0));
            double za = _st[b].Z, zb = _st[b + 1].Z;
            _hp.In(Side(b, s), () =>
            {
                Slab(za, zb, 1, s, 0.82, 0.9, 0.62, 0.72, HullPieces.T / 2 + 0.004, HullPieces.T / 2 + 0.06, s < 0 ? Red : Green, glow: true);
                Slab(za, zb, 2, s, 0.25, 0.62, 0.05, 0.25, HullPieces.T / 2 + 0.004, HullPieces.T / 2 + 0.03, Paper, "painted plating");
                Slab(za, zb, 2, s, 0.28, 0.36, 0.08, 0.22, HullPieces.T / 2 + 0.03, HullPieces.T / 2 + 0.045, Armour, "painted plating");
            });
            for (var bb = BandOf(_zCkW + 0.1); bb <= BandOf(_zEngEnd - 0.1); bb++)
            {
                double a = _st[bb].Z, c = _st[bb + 1].Z;
                if (c - a < 1.2 || (a >= _zCabEnd - 0.01 && c <= _zEng + 0.01 && (a < _zSp0 + 0.01 || c > _zSp1 - 0.01)))
                    continue;
                double u0 = 0.15 / (c - a), u1 = 1 - u0;
                _hp.In(Side(bb, s), () => Slab(a, c, 1, s, u0, u1, 0.9, 0.95, HullPieces.T / 2 + 0.004, HullPieces.T / 2 + 0.018, Hazard, "worn paint"));
            }
        }
        // The spine's dorsal conduit between the ribs, and a cargo floodlight over each cradle.
        for (var bb = BandOf(_zSp0 + 0.1); bb < BandOf(_zSp1 - 0.1) + 1; bb++)
        {
            double a = _st[bb].Z + 0.12, c = _st[bb + 1].Z - 0.12, top = _spine.Top;
            if (c - a < 0.5)
                continue;
            var band = bb;
            _hp.In(RoofP(band), () =>
            {
                _hp.Detail(-0.16, top + 0.026, a, -0.04, top + 0.12, c, Red, "painted steel");
                _hp.Detail(0.04, top + 0.026, a, 0.16, top + 0.1, c, Link, "painted steel");
                for (var z = a + 0.4; z < c - 0.2; z += 1.6)
                    _hp.Detail(-0.22, top + 0.026, z, 0.22, top + 0.15, z + 0.08, Frame, "dirty steel");
            });
        }
        foreach (var z in _bayZ)
            _hp.In(RoofP(BandOf(z)), () =>
            {
                foreach (var s in new[] { -1, 1 })
                {
                    _hp.Detail(s * 0.3, _spine.Top + 0.026, z - 0.08, s * 0.38, _spine.Top + 0.5, z + 0.08, Frame, "painted steel");
                    _hp.Detail(s * 0.3, _spine.Top + 0.5, z - 0.18, s * 0.6, _spine.Top + 0.62, z + 0.18, "#fff0c0", glow: true);
                }
            });
        // The cab's roof: a beacon, and what the dice stood on it.
        var rb = BandOf(R(9.0));
        _hp.In(RoofP(rb), () =>
        {
            var top = _cab.Top;
            _hp.Detail(-0.12, top + 0.026, R(9.0) - 0.12, 0.12, top + 0.3, R(9.0) + 0.12, Frame, "painted steel");
            _hp.Detail(-0.1, top + 0.3, R(9.0) - 0.1, 0.1, top + 0.45, R(9.0) + 0.1, "#ffb040", glow: true);
            if (_cabTop == "antenna farm")
                foreach (var (x, h) in new[] { (-1.3, 1.4), (-0.9, 0.9), (1.1, 1.7) })
                {
                    _hp.Detail(x - 0.025, top + 0.026, R(8.0) - 0.025, x + 0.025, top + h, R(8.0) + 0.025, "#9aa0a4", "steel");
                    _hp.Detail(x - 0.08, top + 0.026, R(8.0) - 0.08, x + 0.08, top + 0.12, R(8.0) + 0.08, Frame, "dirty steel");
                }
            else if (_cabTop == "sun visor")
                _hp.Detail(-2.2, top + 0.06, R(3.4) - 0.01, 2.2, top + 0.3, R(3.4) + 0.09, Armour, "painted plating");
        });
        if (_cabTop == "sun visor")
            _hp.In(RoofP(BandOf(R(3.4))), () => _hp.Detail(-2.0, _cab.Top + 0.026, R(3.4) - 0.04, 2.0, _cab.Top + 0.06, R(3.4) + 0.04, Frame, "painted steel"));
        // Engineering: louvred vents along its upper sides, soot behind them.
        var eb = BandOf(_zEngEnd - 1);
        foreach (var s in new[] { -1, 1 })
        {
            double za = _st[eb].Z, zb = _st[eb + 1].Z;
            _hp.In(Side(eb, s), () =>
            {
                for (var i = 0; i < 5; i++)
                {
                    double u0 = 0.12 + i * 0.15, u1 = u0 + 0.1;
                    Slab(za, zb, 2, s, u0, u1, 0.35, 0.75, HullPieces.T / 2 + 0.004, HullPieces.T / 2 + 0.04, Dark, "dirty steel");
                }
                Slab(za, zb, 2, s, 0.1, 0.9, 0.78, 0.86, HullPieces.T / 2 + 0.004, HullPieces.T / 2 + 0.012, Soot, "soot");
            });
        }
    }

    // ---- the nacelle (one per side: its own part, on the pylon's root)

    private double PylonZ => Math.Round(_zEng + 4.25 + 1.06, 2);
    private double NacTail => Math.Round(_zStern + _nacTailOver, 2);
    private double NacTank1 => Math.Round(NacTail - 2.6, 2);
    private double NacTank0 => Math.Round(NacTank1 - _nacTank, 2);
    private double NacNose => Math.Round(NacTank0 - 1.4, 2);

    private Part Nacelle(int s)
    {
        var name = $"{Word} Nacelle {(s < 0 ? "L" : "R")}";
        var p = new Part(name, Small, Section, $"One of the {ShipName}'s drive nacelles, {(s < 0 ? "to port" : "to starboard")}: a fuel tank forward of an engine housing on a pylon off engineering's flank. Its tail plate is a keyed 2x2 engine socket's place, the drive's Drive Core inside behind it, mended through the service hatch in its outboard flank.", role: "fueltank", tool: "welder", material: "steel", color: Skin, group: "Ship");
        double cx = s * _nacX, cy = _nacY, f = _nacF, c = EngineHousing.HalfFlat(f);
        Func<double, double, double, double[]> at = _hp.P;
        EngineHousing.Ring(p, at, cx, cy, 0.3, 0.12, NacNose, 0.6, 0.25, NacNose + 0.4, Hazard, "painted plating");
        EngineHousing.Ring(p, at, cx, cy, 0.6, 0.25, NacNose + 0.404, f, c, NacTank0, Skin, "painted plating");
        var mid = Math.Round((NacTank0 + NacTank1) / 2, 2);
        var bandCol = _livery == "blue keel" ? Armour : Armour2;
        EngineHousing.Ring(p, at, cx, cy, f, c, NacTank0 + 0.004, f, c, mid - 0.6, Skin2, "painted plating");
        EngineHousing.Ring(p, at, cx, cy, f, c, mid - 0.596, f, c, mid + 0.6, bandCol, "painted plating");
        EngineHousing.Ring(p, at, cx, cy, f, c, mid + 0.604, f, c, NacTank1 - 0.004, Skin, "painted plating");
        foreach (var z in new[] { NacTank0 + 0.6, mid - 0.6, mid + 0.6 })
            EngineHousing.Ring(p, at, cx, cy, f + 0.03, c + 0.012, z - 0.08, f + 0.03, c + 0.012, z + 0.08, Frame, "dirty steel");
        EngineHousing.Draw(p, at, cx, cy, f, c, NacTank1, NacTail, s, Skin, Skin2, Frame, Hazard, "worn paint", ShipWorksParts.CoreR[0], ShipWorksParts.CoreD[0], ShipWorksParts.CoreD[0]);
        EngineHousing.Ring(p, at, cx, cy, f + 0.035, c + 0.014, NacTank1 - 0.1, f + 0.035, c + 0.014, NacTank1 + 0.1, Armour, "plated steel");
        void D(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool glow = false) =>
            p.Add(Box(_hp.P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), _hp.P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), col, detail: true, shape: glow ? Kw(("glow", true)) : Kw(("finish", fin))));
        void S(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin) =>
            p.Add(Box(_hp.P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), _hp.P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), col, shape: Kw(("finish", fin))));
        void Hid(double x0, double y0, double z0, double x1, double y1, double z1) =>
            p.Add(Box(_hp.P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), _hp.P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));
        // A fuel line along its inboard flank, a grab rail and a step under the hatch, a fill cap, its nav light at the tip.
        D(cx - s * (f + 0.02), cy - 0.35, NacTank0 + 0.4, cx - s * (f + 0.08), cy - 0.29, NacTank1 - 0.2, Red, "painted steel");
        var hz = EngineHousing.HatchZ(NacTail, ShipWorksParts.CoreD[0]);
        foreach (var z in new[] { hz - 0.4, hz + 1.4 })
            D(cx + s * (f + 0.02), cy + 0.5, z - 0.03, cx + s * (f + 0.3), cy + 0.54, z + 0.03, "#9a9c98", "steel");
        D(cx + s * (f + 0.28), cy + 0.5, hz - 0.45, cx + s * (f + 0.33), cy + 0.56, hz + 1.45, Hazard, "worn paint");
        D(cx - 0.1, cy + f + 0.004, mid + 1.2, cx + 0.1, cy + f + 0.12, mid + 1.4, Red, "worn paint");
        D(cx - 0.06, cy - 0.06, NacNose - 0.1, cx + 0.06, cy + 0.06, NacNose + 0.004, s < 0 ? Red : Green, "", glow: true);
        // The pylon: a solid spar from the root into the tank under a swept fairing.
        var py0 = PylonZ - 1.0;
        S(s * _eng.W, cy - 0.2, py0, cx - s * (f - 0.03), cy + 0.2, PylonZ + 1.0, Frame, "dirty steel");
        var fair = new[]
        {
            _hp.P(s * (_eng.W + 0.03), cy - 0.3, py0 - 0.5), _hp.P(s * (_eng.W + 0.03), cy + 0.3, py0 - 0.5),
            _hp.P(s * (_eng.W + 0.03), cy + 0.3, PylonZ + 1.3), _hp.P(s * (_eng.W + 0.03), cy - 0.3, PylonZ + 1.3),
        };
        var fair2 = new[]
        {
            _hp.P(cx - s * (f + 0.004), cy - 0.24, py0 + 0.3), _hp.P(cx - s * (f + 0.004), cy + 0.24, py0 + 0.3),
            _hp.P(cx - s * (f + 0.004), cy + 0.24, PylonZ + 1.1), _hp.P(cx - s * (f + 0.004), cy - 0.24, PylonZ + 1.1),
        };
        var both = fair.Concat(fair2).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        p.Add(Box([.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))], [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))], ColourOf(1, PylonZ) == Armour ? Armour : Armour2, detail: true,
            shape: Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("finish", "painted plating"))));
        D(s * (_eng.W + 0.25), cy + 0.29, py0 + 0.1, cx - s * (f + 0.1), cy + 0.35, py0 + 0.6, Hazard, "hazard paint");
        Hid(cx - 0.82 * f, cy - 0.82 * f, NacTank0, cx + 0.82 * f, cy + 0.82 * f, NacTank1 - 0.004);
        p.Sock("hull", _hp.P(s * _eng.W, cy, PylonZ), s < 0 ? "+x" : "-x");
        Works.Mount(p, outside: true);
        void Pad(string n, double x, double y, double z, string dir)
        {
            var sg = dir[0] == '-' ? -1 : 1;
            var ax = dir[1] - 'x';
            double[] a = [x, y, z];
            double[] lo = [x - 0.3, y - 0.3, z - 0.3], hi = [x + 0.3, y + 0.3, z + 0.3];
            lo[ax] = Math.Min(a[ax] - sg * 0.11, a[ax] + sg * 0.05);
            hi[ax] = Math.Max(a[ax] - sg * 0.11, a[ax] + sg * 0.05);
            a[ax] += sg * 0.05;
            S(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], Frame, "dirty steel");
            p.Sock(n, _hp.P(a[0], a[1], a[2]), dir);
        }
        Pad("rcsFore", cx, cy, NacNose + 0.06, "-z");
        Pad("rcsUp", cx, cy + f, Math.Round(mid + 1.9, 2), "+y");
        Pad("rcsDown", cx, cy - f, Math.Round(mid + 1.9, 2), "-y");
        p.Set(("fuelTank", Kw(("capacity", NacFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. _hp.P(cx - s * 0.82 * f, cy, mid).Cast<object>()], s < 0 ? "+x" : "-x", "fuel"))));
        return p.Stats(3500, 7000, 20, 10.0);
    }

    // ---- the shared parts (one set for every Traders hauler)

    /// <summary>A cradle for n containers stacked: an inboard strongback on the hardpoint, an end hoop at each end (a C of chamfered
    /// girders round the boxes), a shelf of rails under each box, a clamp beam along the top, twistlocks, marker lamps. Origin at its
    /// hardpoint face, +x out; the boxes' middle is at x 1.5.</summary>
    public static Part Cradle(int n)
    {
        var p = new Part($"{Shared} Cradle {n}", Small, Section, $"A Traders hauler's container cradle: an open frame on a spine hardpoint holding {(n == 1 ? "one standard container" : "two standard containers one over the other")}, end hoops round them, a shelf under each, a clamp beam over the top. Open outboard: a crane or a shell takes the boxes out sideways. The Cargo Volume on its top holds what is in it.", role: "frame", tool: "welder", material: "steel", color: RackCol, group: "Ship");
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: Kw(("finish", fin))));
        var (yLo, yHi) = CradleY(n);
        const double hz = CtL / 2 + 0.2, po = 0.15, d = 3.0;
        // The strongback on the hardpoint and up the inner face; the hardpoint's block.
        B(0, -0.38, -0.32, 0.14, 0.38, 0.32, Frame, "dirty steel"); // (inside the hardpoint's face, so their edges never share a plane)
        B(0, yLo, -0.15, 0.12, yHi, 0.15, RackCol, "painted steel");
        // The end hoops: a bottom girder, an outer post, a top girder at each end; braces cut across the corners (drawn).
        foreach (var sz in new[] { -1, 1 })
        {
            double z0 = sz > 0 ? hz - po : -hz, z1 = sz > 0 ? hz : -hz + po;
            B(0, yLo, z0, po, yHi, z1, RackCol, "painted steel");
            B(po, yLo, z0, d, yLo + Shelf, z1, RackCol, "painted steel");
            B(d - po, yLo + Shelf, z0, d, yHi - Shelf, z1, RackCol, "painted steel");
            B(po, yHi - Shelf, z0, d, yHi, z1, RackCol, "painted steel");
            // (the hoop's corner gussets, a hair proud of its end face)
            var ze = sz > 0 ? hz + 0.004 : -hz - 0.004;
            var zf = sz > 0 ? hz + 0.024 : -hz - 0.024;
            foreach (var (cx, cy, dx, dy) in new[] { (d - po, yLo + Shelf, -1, 1), (d - po, yHi - Shelf, -1, -1), (po, yLo + Shelf, 1, 1), (po, yHi - Shelf, 1, -1) })
            {
                double[][] tri = [[cx, cy, Math.Min(ze, zf)], [cx + dx * 0.4, cy, Math.Min(ze, zf)], [cx, cy + dy * 0.4, Math.Min(ze, zf)]];
                double[][] tri2 = [[cx, cy, Math.Max(ze, zf)], [cx + dx * 0.4, cy, Math.Max(ze, zf)], [cx, cy + dy * 0.4, Math.Max(ze, zf)]];
                var pts = tri.Concat(tri2).ToList();
                p.Add(Box([.. Enumerable.Range(0, 3).Select(k => pts.Min(q => q[k]))], [.. Enumerable.Range(0, 3).Select(k => pts.Max(q => q[k]))], Hazard, detail: true,
                    shape: Kw(("shape", "hull"), ("points", pts.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("finish", "hazard paint"))));
            }
        }
        // The shelves: rails along under each box (the bottom one on the hoops' girders, a middle one between two boxes).
        for (var i = 0; i < n; i++)
        {
            var y0 = yLo + i * (CtH + Shelf + 0.006);
            foreach (var x in new[] { 0.35, 1.3, 2.3 })
                B(x, y0, -hz + po, x + 0.3, y0 + Shelf, hz - po, RackCol, "painted steel");
            if (i > 0)
                foreach (var sz in new[] { -1, 1 })
                    B(po, y0, sz > 0 ? hz - po : -hz, d - po, y0 + Shelf, sz > 0 ? hz : -hz + po, RackCol, "painted steel");
            // Twistlocks at the box's corners (yellow).
            foreach (var x in new[] { 0.5, 2.45 })
                foreach (var sz in new[] { -1, 1 })
                    B(x - 0.08, y0 + Shelf + 0.004, sz * (CtL / 2 - 0.1) - 0.08, x + 0.08, y0 + Shelf + 0.05, sz * (CtL / 2 - 0.1) + 0.08, Hazard, "hazard paint", true);
        }
        // The clamp beam along the top, its two arms down to the top box.
        B(1.3, yHi - Shelf, -hz + po, 1.7, yHi, hz - po, Frame, "dirty steel");
        foreach (var z in new[] { -1.6, 1.6 })
            B(1.4, yHi - Shelf - 0.22, z - 0.1, 1.6, yHi - Shelf - 0.006, z + 0.1, Hazard, "hazard paint", true);
        // Marker lamps on the outer corners (amber), a number plate on the outer post.
        foreach (var y in new[] { yLo + 0.06, yHi - 0.06 })
            foreach (var sz in new[] { -1, 1 })
                B(d + 0.004, y - 0.04, sz * (hz - 0.075) - 0.04, d + 0.05, y + 0.04, sz * (hz - 0.075) + 0.04, "#ffb040", "glass", true);
        B(d + 0.004, yHi - 0.9, hz - po + 0.01, d + 0.012, yHi - 0.3, hz - 0.01, Paper, "painted plating", true);
        p.Sock("hull", [0, 0, 0], "-x");
        p.Sock("volume", [1.5, yHi, 0.0], "+y");
        return p.Stats(1100 + 500 * n, 6000, 25, 8.0);
    }

    /// <summary>A cradle's bottom and top (its own frame, the hardpoint at y 0).</summary>
    private static (double Lo, double Hi) CradleY(int n)
    {
        var h = n * (CtH + Shelf + 0.006) + Shelf + 0.006;
        var lo = n == 1 ? -1.3 : -2.67;
        return (lo, Math.Round(lo + h, 3));
    }

    public static Part LiftJet()
    {
        const double k = JetScale;
        var p = new Part($"{Shared} Lift Jet", Small, Section, $"A lift jet: {LiftForce / 1e6:0.0} MN straight down. Under a Traders hauler's cab and engineering, from the keel's tanks.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: "Ship");
        double[] Rr(double r, double y, int i, int n = 8)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * k * Math.Cos(an), 4), Py.Round(y * k, 4), Py.Round(r * k * Math.Sin(an), 4)];
        }
        void Hull(IEnumerable<double[]> pts, string col, string fin, bool glow = false)
        {
            var q = pts.ToList();
            p.Add(Box([.. Enumerable.Range(0, 3).Select(i => q.Min(a => a[i]))], [.. Enumerable.Range(0, 3).Select(i => q.Max(a => a[i]))], col, detail: true,
                shape: glow ? Kw(("shape", "hull"), ("points", q.Select(a => (object?)a.Select(x => (object?)x).ToList()).ToList()), ("glow", true))
                    : Kw(("shape", "hull"), ("points", q.Select(a => (object?)a.Select(x => (object?)x).ToList()).ToList()), ("finish", fin))));
        }
        Hull(Enumerable.Range(0, 8).Select(i => Rr(0.55, -0.003, i)).Concat(Enumerable.Range(0, 8).Select(i => Rr(0.48, -0.45, i))), Armour, "worn paint");
        Hull(Enumerable.Range(0, 8).Select(i => Rr(0.565, -0.32, i)).Concat(Enumerable.Range(0, 8).Select(i => Rr(0.565, -0.24, i))), Hazard, "hazard paint");
        (double Y, double R)[] st = [(-0.45, 0.32), (-0.6, 0.27), (-0.9, 0.38), (-1.05, 0.44)];
        for (var s = 0; s < st.Length - 1; s++)
            Hull(Enumerable.Range(0, 8).Select(i => Rr(st[s].R, st[s].Y - (s == 0 ? 0.002 : 0), i)).Concat(Enumerable.Range(0, 8).Select(i => Rr(st[s + 1].R, st[s + 1].Y, i))), s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        Hull(Enumerable.Range(0, 8).Select(i => Rr(0.24, -0.605, i)).Concat(Enumerable.Range(0, 8).Select(i => Rr(0.24, -0.62, i))), "#ffb060", "", glow: true);
        p.Add(Box([-0.45 * k, -0.45 * k, -0.45 * k], [0.45 * k, 0, 0.45 * k], hidden: true));
        p.Add(Box([-0.4 * k, -1.05 * k, -0.4 * k], [0.4 * k, -0.45 * k, 0.4 * k], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", LiftForce), ("spool", 0.6), ("exhaust", "-y"), ("at", Py.L(0, Py.Round(-1.05 * k, 4), 0)), ("burnRate", Py.Round(LiftForce / 1e6 * Burn, 4)))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Py.Round(0.45 * k, 4), Py.Round(-0.2 * k, 4), 0], "+x", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, Py.Round(-1.05 * k, 4), 0)), ("dir", "-y"), ("radius", Py.Round(0.4 * k, 4)), ("length", Py.Round(2.8 * k, 4)), ("kind", "Rocket")))));
        return p.Stats(1000, 4000, 25, 6.0);
    }

    public static Part PowerPlant()
    {
        var p = new Part($"{Shared} Power Plant", Small, Section, "A Traders hauler's power plant: a stack of cells in a cage on a 2x2 plug, 400 kW for the cradles' lamps, the life support and the flight computer. No fuel. A reactor module: it fits any 2x2 reactor socket.", role: "generator", tool: "welder", material: "steel", color: Armour, group: "Ship");
        double[] O(double x, double y, double z, double r, int i, int n = 8)
        {
            var a = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(x + r * Math.Cos(a), 4), y, Py.Round(z + r * Math.Sin(a), 4)];
        }
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: Kw(("finish", fin))));
        void Hull(List<double[]> q, string col, string fin, bool glow = false) =>
            p.Add(Box([.. Enumerable.Range(0, 3).Select(i => q.Min(a => a[i]))], [.. Enumerable.Range(0, 3).Select(i => q.Max(a => a[i]))], col, detail: true,
                shape: glow ? Kw(("shape", "hull"), ("points", q.Select(a => (object?)a.Select(x => (object?)x).ToList()).ToList()), ("glow", true))
                    : Kw(("shape", "hull"), ("points", q.Select(a => (object?)a.Select(x => (object?)x).ToList()).ToList()), ("finish", fin))));
        B(-0.8, 0, -0.8, 0.8, 0.2, 0.8, Frame, "dirty steel");
        foreach (var (x, z) in new[] { (-0.38, -0.38), (0.38, -0.38), (-0.38, 0.38), (0.38, 0.38) })
        {
            Hull([.. Enumerable.Range(0, 8).Select(i => O(x, 0.2, z, 0.32, i)), .. Enumerable.Range(0, 8).Select(i => O(x, 1.7, z, 0.32, i))], Skin, "worn paint");
            Hull([.. Enumerable.Range(0, 8).Select(i => O(x, 1.0, z, 0.34, i)), .. Enumerable.Range(0, 8).Select(i => O(x, 1.08, z, 0.34, i))], "#80d0ff", "", glow: true);
        }
        foreach (var (x, z) in new[] { (-0.77, -0.77), (0.77, -0.77), (-0.77, 0.77), (0.77, 0.77) })
            B(x - 0.04, 0.2, z - 0.04, x + 0.04, 1.9, z + 0.04, Armour, "painted steel", true);
        B(-0.81, 1.86, -0.81, 0.81, 1.94, 0.81, Armour, "painted steel", true);
        foreach (var (x, col) in new[] { (-0.1, Red), (0.1, Link) })
            B(x - 0.05, 1.94, -0.05, x + 0.05, 2.4, 0.05, col, "painted steel", true);
        p.Add(Box([-0.74, 0.2, -0.74], [0.74, 1.7, 0.74], hidden: true));
        B(-0.79, 0.2, -0.812, -0.45, 0.45, -0.79, Hazard, "hazard paint", true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", 400), ("spool", 6), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.1, 0.8)), ("face", "+z")))));
        return p.Stats(2400, 3500, 25, 10.0);
    }

    public static Part FlightComputer()
    {
        const string console = "#22262b";
        var p = new Part($"{Shared} Flight Computer", Small, Section, "A Traders hauler's flight computer: a flight stabilizer with a freighter's gyro in a console between the couches. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: console, group: "Ship");
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, PyDict shape, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: shape));
        B(-0.4, 0, -0.28, 0.4, 0.75, 0.28, console, Kw(("finish", "plastic")));
        B(-0.34, 0.38, 0.28, 0.34, 0.68, 0.298, "#78c8e8", Kw(("glow", true)), true);
        for (var i = 0; i < 5; i++)
            B(-0.28 + i * 0.12, 0.24, 0.28, -0.22 + i * 0.12, 0.3, 0.298, i % 2 == 0 ? "#60e090" : Hazard, Kw(("glow", true)), true);
        B(-0.05, 0.751, -0.2, 0.05, 0.767, 0.1, Hazard, Kw(("finish", "hazard paint")), true);
        B(-0.12, 0.751, -0.2, 0.12, 0.767, -0.12, Hazard, Kw(("finish", "hazard paint")), true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 40000000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(300, 600, 8, 3.0);
    }

    public static Part Ladder()
    {
        var rise = Py.Round(BoardRise, 3);
        var p = new Part($"{Shared} Ladder", Small, Section, "A Traders hauler's boarding ladder: it stands on the belly hatch, stowed up through the airlock's floor while the hatch is shut, and runs from the ground up the shaft when it is open. F climbs it.", role: "frame", tool: "welder", material: "steel", color: Frame, group: "Ship");
        var top = rise - 0.03;
        p.Add(Box([-0.3, 0, -0.19], [0.3, top, -0.005], hidden: true));
        foreach (var x in new[] { -0.25, 0.25 })
        {
            p.Add(Box([x - 0.03, 0.0, -0.18], [x + 0.03, top, -0.12], "#9aa0a4", detail: true, shape: Kw(("finish", "painted steel"))));
            for (var y = 0.6; y < top - 0.2; y += 1.2)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.01], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        for (var y = 0.3; y < rise + 0.1 && y < top - 0.05; y += 0.3)
            p.Add(Box([-0.22, y - 0.02, -0.165], [0.22, y + 0.02, -0.135], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.29, top - 0.06, -0.19], [0.29, top, -0.11], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("foot", [0, 0, -0.1], "-y");
        p.Set(("ladder", rise));
        return p.Stats(60 + rise * 12, 400, 6, 3.0);
    }

    /// <summary>The belly hatch (the Dray's kind): a plate shutting the boarding shaft flush under the keel, on a strut up the shaft's aft wall to the floor's edge, where it hangs; it slides down to the ground with the ladder on it.</summary>
    public static Part BellyHatch()
    {
        var p = new Part($"{Shared} Belly Hatch", Small, Section, "A Traders hauler's belly hatch: a plate that shuts the boarding shaft under the keel. F (or the airlock's switch) slides it down to the ground, and the boarding ladder standing on it comes down with it.", role: "door", tool: "welder", material: "steel", color: Armour, group: "Ship");
        // (in the cab's frame: the shaft from HatchX0 to HatchX1 across and from 0 to its length along; its socket on the aft edge)
        const double len = HatchR1 - HatchR0;
        double[] S = [HatchX, Ft - 0.125, len];
        double[] L(double x, double y, double z) => [Py.Round(x - S[0], 4), Py.Round(y - S[1], 4), Py.Round(z - S[2], 4)];
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string? col, string? fin, bool detail = false, bool hidden = false) =>
            p.Add(hidden ? Box(L(x0, y0, z0), L(x1, y1, z1), hidden: true) : Box(L(x0, y0, z0), L(x1, y1, z1), col!, detail: detail, shape: Kw(("finish", fin!))));
        double x0 = HatchX0 - 0.02, x1 = HatchX1 + 0.02, z0 = -0.02, z1 = len - 0.035;
        B(x0, HatchBottom, z0, x1, HatchTop, z1, Armour, "plated steel");
        for (var i = 0; i < 4; i++)
        {
            var z = z0 + 0.15 + i * 0.3;
            B(x0 + 0.1, HatchBottom - 0.008, z, x1 - 0.1, HatchBottom - 0.002, z + 0.12, Hazard, "hazard paint", detail: true);
        }
        B(x0 + 0.03, HatchTop + 0.002, z0 + 0.03, x0 + 0.13, HatchTop + 0.06, z1 - 0.03, Frame, "dirty steel", detail: true);
        B(x1 - 0.13, HatchTop + 0.002, z0 + 0.03, x1 - 0.03, HatchTop + 0.06, z1 - 0.03, Frame, "dirty steel", detail: true);
        B(HatchX - 0.3, HatchTop, len - 0.03, HatchX + 0.3, Ft - 0.01, len, null, null, hidden: true);
        B(HatchX - 0.28, HatchTop + 0.004, len - 0.028, HatchX + 0.28, Ft - 0.02, len - 0.004, Frame, "painted steel");
        p.Sock("hang", [0, 0, 0], "+z");
        p.Sock("ladder", L(HatchX, HatchTop, len - 0.03 - 0.1), "+y");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-y"), ("travel", Py.Round(HatchTravel, 3)), ("seconds", 4.0), ("hang", "hang"))));
        return p.Stats(500, 4000, 30, 6.0);
    }

    public static List<Part> SharedParts() => [Cradle(1), Cradle(2), LiftJet(), PowerPlant(), FlightComputer(), Ladder(), BellyHatch()];

    // ---- making it

    public sealed record Made(string ShipName, List<PyDict> Parts, PyDict Blueprint, string Report, List<string> Problems);

    public Made Make(Dictionary<string, PyDict> sharedDefs)
    {
        NoseAndCockpit();
        Cab();
        Spine();
        Engineering();
        Stern();
        Mounts();
        Dress();
        // ---- Pieces: stats, the keel's fuel and its ports, the welds.
        foreach (var p in _hp.All)
        {
            var name = (string)p.D["name"]!;
            var what = name[(Word.Length + 1)..];
            var len = 1.0;
            if (what.StartsWith("Keel") || what.StartsWith("Side") || what.StartsWith("Roof"))
            {
                var b = int.Parse(what.Split(' ')[^1]);
                len = _st[b + 1].Z - _st[b].Z;
            }
            _ = what switch
            {
                "Nose" => p.Stats(3500, 5000, 20, 10.0),
                "Stern" => p.Stats(3000, 6000, 30, 8.0),
                "Cab Walls" => p.Stats(3000, 6000, 15, 8.0),
                "Eng Walls" => p.Stats(2000, 5000, 20, 8.0),
                _ when what.StartsWith("Keel") => p.Stats(Math.Round(900 * len), 2500 * len, 30, 8.0),
                _ when what.StartsWith("Side") => p.Stats(Math.Round(320 * len), 1500 * len, 25, 6.0),
                _ => p.Stats(Math.Round(300 * len), 1200 * len, 20, 6.0),
            };
            if (p.D.Get("fuelTank") is PyDict && (string)p.D["role"]! == "fueltank")
                FuelPort(p);
            else if ((string)p.D["role"]! == "fueltank")
                p.D["role"] = "frame";
        }
        var nacelles = new[] { Nacelle(-1), Nacelle(1) };
        _hp.Connect();
        var order = _hp.PlaceOrder("Keel 2");
        _rep.Note("Pieces", $"{_hp.All.Count} welded pieces (the keel, sides and roof cut at every station, the nose, the walls, the stern), each welded to the pieces it touches, placed out from the keel under the cockpit's back wall: a hit takes a panel, not a deck.");
        var parts = new List<Part>(_hp.All);
        parts.AddRange(nacelles);
        foreach (var p in parts)
            if (p.Boxes.Count > 256 || p.Sockets.Count > 64)
                _rep.Problem($"{p.D["name"]}: {p.Boxes.Count} boxes and {p.Sockets.Count} sockets (at most 256 and 64)");
        var defs = new Dictionary<string, PyDict>(sharedDefs);
        var partDefs = parts.Select(p => p.Json()).ToList();
        foreach (var d in partDefs)
            defs[(string)d["name"]!] = d;
        // ---- Drive and lift: what the reference fit weighs and what holds it up.
        var hullMass = parts.Sum(p => Convert.ToDouble(p.D["mass"]));
        var cradleMass = _bays * 2 * Convert.ToDouble(sharedDefs[$"{Shared} Cradle {_stack}"]["mass"]);
        var boxes = _bays * 2 * _stack;
        var fitMass = 16000.0; // (the fittings, the drives, the legs, the plant: the reference fit, about)
        var mass = hullMass + cradleMass + boxes * 1938 + fitMass;
        var lift = 4 * LiftForce;
        var ratio = lift / (mass * 9.81);
        _rep.Note("Drive and lift", $"Mass with the reference fit and every container empty: about {mass / 1000:0} t (the hull {hullMass / 1000:0} t, {_bays * 2} cradles {cradleMass / 1000:0} t, {boxes} containers {boxes * 1.938:0} t, the fit about {fitMass / 1000:0} t).");
        _rep.Note("Drive and lift", $"Four lift jets of {LiftForce / 1e6:0.0} MN under the cab and engineering: {ratio:0.0} times its weight at 1 g (the rule: at least 2, so a loaded hauler still climbs).");
        _rep.Note("Drive and lift", $"Two nacelles at {Fmt(_nacX)} m out, their tails {Fmt(_nacTailOver)} m past the transom; each tail plate is a keyed 2x2 engine socket, fitted with a 600 kN drive and a Drive Core S: {1.2e6 / (mass * 9.81):0.00} g along.");
        _rep.Note("Drive and lift", "Attitude jets every way at both ends (heavy RCS on the cab's roof, flanks and keel, the stern's four sides and the nacelles' noses, tops and bottoms), so it turns about any axis.");
        if (ratio < 2)
            _rep.Problem($"lift: {ratio:0.0} times its weight, under 2");
        _rep.Note("Slots", "Seats: two flight couches on the cockpit's floor sockets (pilot and copilot), the flight computer between them. A buyer may fit any seat that stands on a floor socket.");
        _rep.Note("Slots", "Drives: a keyed 2x2 engine Module Socket on each nacelle's tail plate. The nacelle is the drive's mount: whatever drive goes in, its Drive Core bolts inside behind the plate, mended through the service hatch.");
        _rep.Note("Slots", "Reactor: a 2x2 reactor Module Socket on engineering's floor, aft to starboard, a hazard line round it; the reference fit is the Traders Hauler Power Plant (400 kW).");
        _rep.Note("Slots", "Sensor: a 1x1 Module Socket on the cab's roof over its middle, the reference fit a Long Range Sensor.");
        _rep.Note("Slots", $"Cargo: {_bays * 2} cradle hardpoints on the spine, each with a {_stack}-high cradle and a frozen Cargo Volume over it.");
        var bp = Blueprint(defs, order);
        var report = _rep.Markdown(_brief, $"{ShipName} (seed {_brief.Seed})", Stages);
        return new Made(ShipName, partDefs, bp, report, _rep.Problems);
    }

    /// <summary>A keel tank's Fuel out on its aft face, on the centre line under the floor.</summary>
    private static void FuelPort(Part q)
    {
        var solid = q.Boxes.Where(bx => bx.Get("detail") is not true).ToList();
        double V(PyDict b, string k, int i) => Convert.ToDouble(((List<object?>)b[k]!)[i]);
        var zmax = solid.Max(bx => V(bx, "max", 2));
        var aft = solid.Where(bx => Math.Abs(V(bx, "max", 2) - zmax) < 0.002 && V(bx, "min", 0) <= -HullPieces.H && V(bx, "max", 0) >= -HullPieces.H).ToList();
        var y = aft.Count > 0 ? Math.Round((aft.Min(bx => V(bx, "min", 1)) + aft.Max(bx => V(bx, "max", 1))) / 2, 3) : -0.4;
        q.Set(("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [-HullPieces.H, y, zmax], "+z", "fuel"))));
    }

    // ---- the blueprint: the reference fit

    private PyDict Blueprint(Dictionary<string, PyDict> defs, List<string> order)
    {
        var bp = new BuildingParts.Blueprint(ShipName, $"A Traders hauler for carrying goods between markets, made by the ship generator (seed {_brief.Seed}): a crew cab under {(_cockpit == CockpitKind.Open ? "a glass nose" : "a plated nose over a sealed cockpit")}, a walkable spine with {_bays * 2 * _stack} standard containers in cradles off its flanks, an engineering block aft and two drive nacelles on pylons. Bunks, a head, a trade office, a galley, an airlock over the boarding shaft and the stores in the cab; the plant, the panel and the lockers in engineering. Its slots carry the reference fit: two couches, a drive on each nacelle's engine socket, the plant on the reactor socket, a sensor on the roof.", defs, maxSteps: 700);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in order)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        int On(string sock) => placed[_hp.SockPiece[sock]];
        int Hang(string part, string sock, string by, int spin = 0) => bp.Hang(part, On(sock), sock, by, spin);
        int Paint(int step, string paint, string? finish = null)
        {
            bp.Steps[step]["paint"] = paint;
            if (finish != null)
                bp.Steps[step]["finish"] = finish;
            return step;
        }
        int Lamp(string sock, string colour = "#fff0d0")
        {
            var s = Hang("Bulkhead Light", sock, "back");
            bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
            return s;
        }
        void Switch(string sock, bool startOn, params int[] lamps)
        {
            var s = Hang("Bakelite Switch", sock, "back");
            bp.Steps[s]["config"] = new PyDict { ["startOn"] = startOn };
            foreach (var l in lamps)
                bp.Wires.Add((s, l));
        }
        int ModuleSocket(int on, string sock, int w, int l, int kind = 0, bool keyed = false)
        {
            var s = bp.Hang($"Module Socket {w}x{l}", on, sock, "bc");
            var cfg = new PyDict();
            if (kind != 0)
                cfg["modKind"] = kind;
            if (keyed)
                cfg["modKeyed"] = 1;
            if (cfg.Count > 0)
                bp.Steps[s]["config"] = cfg;
            return s;
        }
        int Fitted(int plate, int w, int l, string part, string by)
        {
            var plug = bp.Hang($"Module Plug {w}x{l}", plate, "module", "module");
            return bp.HangTurned(part, plug, ModuleSocketParts.CentreTop(w, l), by, BuildingParts.R0);
        }
        foreach (var d in new[] { "doorCk", "doorAft", "doorBunk", "doorHead", "doorOffice", "doorGalley", "doorAirlock", "doorStores", "doorEng" })
            Paint(Hang("Bulkhead Door 1.5x2.25 m", d, "hang"), Hazard, "worn paint");
        // The cockpit's seats and computer (Ship keys, the mouse aims the nose).
        var pilot = Hang("Flight Couch", "pilot", "bottom");
        Hang("Flight Couch", "copilot", "bottom");
        var fc = Hang($"{Shared} Flight Computer", "computer", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight();
        bp.Wires.Add((pilot, fc));
        // The crew's rooms.
        Hang("Bunk", "bunk0", "bottom");
        Hang("Bunk", "bunk1", "bottom");
        Paint(Hang("Service Locker", "lockerBunk", "wall"), Armour);
        Hang("Ship Toilet", "toilet", "wall");
        Hang("Ship Basin", "basin", "wall");
        Hang("Shower Stall", "shower", "wall");
        Hang("Computer", "computerDesk", "foot00");
        Hang("Server Rack", "server0", "foot00");
        Hang("Server Rack", "server1", "foot00");
        Paint(Hang("Filing Cabinet", "cabinet", "wall"), Skin);
        Hang("Ship Galley Range", "range", "back");
        Hang("Galley Coffee Station", "coffee", "wall");
        bp.HangTurned("Ship Mess Table", On("table"), "table", "bottom", BuildingParts.RY90);
        EvaParts.FitDock(bp, On("mmuDock"), "mmuDock");
        foreach (var n in new[] { "suit0", "suit1" })
            Hang(EvaParts.LockerName, n, "wall");
        Hang(EvaParts.LifeSupportName, "lifeSupport", "wall");
        Hang(EvaParts.OxygenTankName, "o2Tank0", "wall");
        var hatch = Hang($"{Shared} Belly Hatch", "boarding", "hang");
        Paint(bp.Hang($"{Shared} Ladder", hatch, "ladder", "foot"), Frame, "painted steel");
        Switch("switchHatch", false, hatch);
        // Engineering.
        Hang("Engineer's Panel", "panel", "back");
        Hang("Power Board", "board", "back");
        Fitted(ModuleSocket(On("reactor"), "reactor", 2, 2, kind: 2), 2, 2, $"{Shared} Power Plant", "bottom");
        for (var i = 0; i < 3; i++)
            Hang("Battery", $"battery{i}", "-y");
        Hang(EvaParts.LockerName, "suit", "wall");
        Paint(Hang("Pressure Locker", "locker", "wall"), Armour);
        Hang("Tool Rack", "tools", "back");
        // The nacelles: each on its pylon, the drive on its tail plate's engine socket, the engine inside, the hatch in its flank.
        foreach (var (s, n) in new[] { (-1, "L"), (1, "R") })
        {
            var nac = Hang($"{Word} Nacelle {n}", $"nac{n}", "hull");
            Fitted(ModuleSocket(nac, "drive", 2, 2, kind: 1, keyed: true), 2, 2, "Rust Bucket Drive", "front");
            bp.Hang("Drive Core S", nac, "core", "front");
            Paint(Works.HangHatch(bp, nac, s), Armour, "worn paint");
            foreach (var j in new[] { "rcsFore", "rcsUp", "rcsDown" })
                Paint(bp.Hang(Rcs, nac, j, "base"), Frame, "dirty steel");
        }
        // The attitude jets: the bow's up, down and out, the stern's every way.
        foreach (var n in new[] { "L", "R" })
        {
            foreach (var d in new[] { "Up", "Down", "Out" })
                Paint(Hang(Rcs, $"rcs{d}{n}", "base"), Frame, "dirty steel");
        }
        foreach (var n in new[] { "L", "R" })
            foreach (var d in new[] { "Up", "Down", "Out", "Along" })
                Paint(Hang(Rcs, $"sRcs{d}{n}", "base"), Frame, "dirty steel");
        // Lift jets and legs under the keel; the roof's sensor.
        for (var i = 0; i < 4; i++)
            Hang($"{Shared} Lift Jet", $"lift{i}", "top");
        for (var i = 0; i < 4; i++)
            LegPodParts.HangPod(bp, LegPod, On($"leg{i}"), $"leg{i}");
        Fitted(ModuleSocket(On("sensor"), "sensor", 1, 1), 1, 1, "Long Range Sensor", "-y");
        // The cradles: one on each hardpoint, a frozen Cargo Volume on each holding its containers.
        var cargo = new List<object?>();
        var halfRound = Enumerable.Range(0, 24).First(r => BuildingParts.Apply(r, [1, 0, 0]) is [-1, 0, 0] && BuildingParts.Apply(r, [0, 1, 0]) is [0, 1, 0]);
        var (yLo, yHi) = CradleY(_stack);
        for (var i = 0; i < _bays; i++)
            foreach (var (s, n) in new[] { (-1, "L"), (1, "R") })
            {
                var rack = Paint(Hang($"{Shared} Cradle {_stack}", $"cradle{n}{i}", "hull"), RackCol, "painted steel");
                var vol = bp.Hang("Cargo Volume", rack, "volume", "-y");
                bp.Steps[vol]["config"] = new PyDict
                {
                    ["startOn"] = true, ["sizeX"] = 2.7, ["sizeY"] = Py.Round(yHi - yLo - 0.1, 2), ["sizeZ"] = 6.8,
                    ["atX"] = 0.0, ["atY"] = Py.Round(-(yHi - yLo) / 2, 2), ["atZ"] = 0.0,
                };
                var x = s * (Math.Round(_spine.W + 0.15, 2) + 1.5);
                for (var k = 0; k < _stack; k++)
                {
                    var y = _rackY + yLo + k * (CtH + Shelf + 0.006) + Shelf + 0.003 + CtH;
                    cargo.Add(Kw(("part", BoxName), ("at", Py.L(Py.Round(x - HullPieces.H, 3), Py.Round(y - HullPieces.H, 3), Py.Round(_bayZ[i] - HullPieces.H - _hp.ZShift, 3))), ("turn", s < 0 ? halfRound : 0)));
                }
            }
        // Lamps and their switches.
        Switch("switchCk", true, Lamp("lampCkL", "#ffd080"), Lamp("lampCkR", "#ffd080"));
        Switch("switchBunk", true, Lamp("lampBunk", "#ffe0b0"));
        Switch("switchHead", true, Lamp("lampHead"));
        Switch("switchOffice", true, Lamp("lampOffice", "#f0f4ff"));
        Switch("switchGalley", true, Lamp("lampGalley", "#ffe0b0"));
        Lamp("lampAirlock", "#ffd040");
        Lamp("lampStores");
        Lamp("lampHallA");
        Lamp("lampHallB");
        foreach (var sock in _hp.SockPiece.Keys.Where(k => k.StartsWith("lampSp")).ToList())
            Lamp(sock, "#d8ecff");
        Switch("switchEng", true, Lamp("lampEngL", "#a0d0ff"), Lamp("lampEngR", "#a0d0ff"));
        var json = bp.Json();
        json["cargo"] = cargo;
        return json;
    }
}
