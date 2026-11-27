using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// A connection lofted between two points on hull skins (Karl, 2026-11-24): a pylon, a strut or a neck out to a nacelle or a pod. Each end
/// is a skin point (<see cref="End"/>); the body between is a section shape (one of the hull gallery's), sized and cut into segments, that
/// leaves each skin along its normal and bends to meet the other. Mirror draws a twin across the centreline.
/// </summary>
public sealed class HullJoin
{
    public const int MaxSegments = 24;
    public const double Sink = 0.3; // how far each end goes in under its skin, so no gap shows where it meets

    /// <summary>A point on a body's skin: the hull, its copy, z along it (hull-local), t round the half profile and the side.</summary>
    public struct End
    {
        public int Hull, Copy, Side;
        public double Z, T;
        /// <summary>Which face the end is on (the user, 2026-11-25): the side skin (with its rounded ends), or the fore or aft cap.
        /// On a cap, U (-1..1, across, a share of the half width at that height) and V (0 keel .. 1 spine) place it.</summary>
        public int Face;
        public double U, V;
    }

    public const int FaceSide = 0, FaceFore = 1, FaceAft = 2;
    public static readonly string[] Faces = ["Side", "Fore", "Aft"];

    public int Id;
    public End A, B;
    public string Section = "Round";
    public double WA = 1.2, HA = 1.2, WB = 1.2, HB = 1.2; // each end's size: the strut tapers between (the user, 2026-11-24: wing-like arms)
    public double Twist; // roll from end A to end B (degrees)
    public double RotA, RotB; // each end's section rolled about the arm's axis where it meets the hull (degrees), blended along the loft into the twist
    public double SweepUp, SweepFore; // the middle line's bow at mid-span (m): up/down and fore/aft
    public double Bias; // -0.9..0.9: the section's mass shifted up (+) or down (-), one side thicker
    public const double MaxSweep = 20, MaxTwist = 180, MaxBias = 0.9, MaxRot = 180;
    /// <summary>Arched arms (the user, 2026-11-25): each end leaves its skin along the normal tilted toward the other end (Leave) and along
    /// the hull (LeaveFore), in degrees, and carries that way for Tan (a share of the span) before it bends into the other end.</summary>
    public double LeaveA, LeaveB, LeaveForeA, LeaveForeB;
    public double TanA = 0.9, TanB = 0.9;
    public const double MaxLeave = 80, MinTan = 0.05, MaxTan = 2.5;

    /// <summary>The sections a connection takes beyond the hull gallery's (the user, 2026-11-24).</summary>
    public static readonly string[] ExtraSections = ["TriangleUp", "TriangleDown", "Wedge", "Diamond", "Hexagon"];

    /// <summary>Both ends' size at once (the larger end's when read).</summary>
    public double W { get => Math.Max(WA, WB); set => WA = WB = value; }
    public double H { get => Math.Max(HA, HB); set => HA = HB = value; }
    public int Segments = 6;
    public bool Mirror; // off by default (Karl, 2026-11-25)
    public double Flare = 1.6, Buttress = 0.8; // the cladding at each end: how much wider it is where it meets the skin, and how far up the strut it runs (m)
    public bool Follow; // the buttresses' bases laid onto the skin, following its curve, the rings between blending out to them (the user, 2026-11-24)
    public double OpenA = 0.9, OpenB = 0.9;
    public string Clad = "Off"; // plated cladding on the strut (as a body's: HullBody.CladKinds), its seed, panel size and depth
    public int CladSeed = 1;
    public double CladSize = 1.5, CladDepth = 1;
    public string ButtressClad = "Off"; // plated cladding on the flared ends (the buttresses), apart from the strut's: kind, seed, panel size, depth
    public int ButtressSeed = 1;
    public double ButtressSize = 0.8, ButtressDepth = 1;
    /// <summary>Match hull colour (Karl, 2026-11-25): the arm, its cladding and its base plates take hull A's paint and material; unticked,
    /// the connection's own (Own, kept while ticked).</summary>
    public bool MatchColour = true;
    public HullBody Own = new();
    /// <summary>The body whose paint and material the connection is drawn with.</summary>
    public HullBody PaintOf(ShipDesign d) => MatchColour ? d.Hull(A.Hull) ?? Own : Own;
    public bool MatchClad, MatchButtress; // the strut's or the buttresses' cladding follows hull A's (its own values kept for when it's unticked)
    /// <summary>The mounting collar (Karl, 2026-11-25): a flat pad on the skin round each end's footprint, its top Height off the skin
    /// (default a little above Heavy cladding's 20 cm), Margin wider than the footprint, its outer edge bevelled; the buttress and strut
    /// stand on its top and the hull's plates stop at its edge.</summary>
    public bool Collar = true;
    public double CollarHeight = 0.22, CollarMargin = 0.25, CollarBevel = 0.05;
    public const double MaxCollarHeight = 1, MaxCollarMargin = 2, CollarSinkDepth = 0.05;
    public bool SquareHole; // the hole each end cuts: a simple oval, or a rectangle (Karl, 2026-11-24: simple holes, not the section's outline) // each end's socket: the hole it cuts, a share of the connection's size (0 closed)

    /// <summary>The end nearest a world point on a body's copy.</summary>
    /// <summary>The end nearest a world point on a body's copy. face ("skin", "fore" or "aft": the surface clicked) picks the face;
    /// without it the nearest of the side skin and the two caps.</summary>
    public static End At(HullBody h, int copy, double x, double y, double z, string? face = null)
    {
        copy = Math.Clamp(copy, 0, h.Copies - 1);
        double lx = x - h.CopyX(copy), ly = y - h.Y, rz = z - h.Z, lz = Math.Clamp(rz, h.Front, h.Back);
        var side = new End { Hull = h.Id, Copy = copy, Z = lz, T = h.NearestT(lx, ly, lz), Side = lx < -0.01 ? -1 : 1, Face = FaceSide, V = 0.5 };
        var f = face switch { "fore" => FaceFore, "aft" => FaceAft, "skin" => FaceSide, _ => -1 };
        if (f < 0)
        {
            var s = h.OnSkin(side.Z, side.T, side.Side);
            var best = Math.Sqrt(Sq(lx - s.X) + Sq(ly - s.Y) + Sq(rz - s.Z));
            f = FaceSide;
            foreach (var c in new[] { FaceFore, FaceAft })
            {
                var (u, v) = CapUV(h, c, lx, ly);
                var (cx, cy) = CapPoint(h, c, u, v);
                var dc = Math.Sqrt(Sq(lx - cx) + Sq(ly - cy) + Sq(rz - CapZ(h, c)));
                if (dc < best - 1e-3)
                    (best, f) = (dc, c);
            }
        }
        if (f == FaceSide)
            return side;
        var (cu, cv) = CapUV(h, f, lx, ly);
        return new End { Hull = h.Id, Copy = copy, Face = f, U = cu, V = cv, Z = CapZ(h, f), T = 0.5, Side = 1 };
    }

    /// <summary>A cap's plane (hull-local z).</summary>
    public static double CapZ(HullBody h, int face) => face == FaceFore ? h.Front : h.Back;

    /// <summary>The cap's keel and spine heights (hull-local).</summary>
    private static (double Y0, double Y1) CapSpan(HullBody h, double z)
    {
        Span<double> hs = stackalloc double[HullBody.MaxLoft];
        Span<double> ys = stackalloc double[HullBody.MaxLoft];
        var n = h.At(z, hs, ys);
        return n == 0 ? (0, 0) : (ys[0], ys[n - 1]);
    }

    /// <summary>A cap point (hull-local x, y) from its place on the cap.</summary>
    public static (double X, double Y) CapPoint(HullBody h, int face, double u, double v)
    {
        var z = CapZ(h, face);
        var (y0, y1) = CapSpan(h, z);
        var y = y0 + (y1 - y0) * Math.Clamp(v, 0, 1);
        var half = Math.Max(0, h.SkinHalf(z, Math.Clamp(y, y0, y1)));
        return (Math.Clamp(u, -1, 1) * half, y);
    }

    /// <summary>The place on a cap of the cap point nearest a hull-local (x, y).</summary>
    public static (double U, double V) CapUV(HullBody h, int face, double lx, double ly)
    {
        var z = CapZ(h, face);
        var (y0, y1) = CapSpan(h, z);
        var v = y1 - y0 < 1e-6 ? 0.5 : Math.Clamp((ly - y0) / (y1 - y0), 0, 1);
        var half = Math.Max(0, h.SkinHalf(z, Math.Clamp(y0 + (y1 - y0) * v, y0, y1)));
        return (half < 1e-6 ? 0 : Math.Clamp(lx / half, -1, 1), v);
    }

    /// <summary>An end's world point and outward normal (null when its body is gone). A cap's normal is -z (fore) or +z (aft); on a
    /// rounded end's skin the normal tilts with the round.</summary>
    public static (double[] P, double[] N)? World(ShipDesign d, End e)
    {
        if (d.Hull(e.Hull) is not { } h)
            return null;
        var c = Math.Min(e.Copy, h.Copies - 1);
        if (e.Face != FaceSide)
        {
            var (x, y) = CapPoint(h, e.Face, e.U, e.V);
            return ([h.CopyX(c) + x, h.Y + y, h.Z + CapZ(h, e.Face)], [0, 0, e.Face == FaceFore ? -1 : 1]);
        }
        var s = h.OnSkin(e.Z, e.T, e.Side);
        double[] n = [s.Nx, s.Ny, 0];
        double rf = Math.Min(h.RoundFore, h.RoundLimit(false)), ra = Math.Min(h.RoundAft, h.RoundLimit(true));
        if ((rf >= 0.01 && e.Z < h.Front + rf + 1e-6) || (ra >= 0.01 && e.Z > h.Back - ra - 1e-6))
        {
            const double dz = 0.02;
            double z0 = Math.Clamp(e.Z - dz, h.Front, h.Back), z1 = Math.Clamp(e.Z + dz, h.Front, h.Back);
            if (z1 - z0 > 1e-6)
            {
                var (q0, q1) = (h.OnSkin(z0, e.T, e.Side), h.OnSkin(z1, e.T, e.Side));
                n[2] = -(s.Nx * (q1.X - q0.X) + s.Ny * (q1.Y - q0.Y)) / (z1 - z0);
                Norm(n);
            }
        }
        return ([h.CopyX(c) + s.X, h.Y + s.Y, h.Z + s.Z], n);
    }

    public JsonObject ToJson(ShipDesign d) => new()
    {
        ["a"] = EndJson(d, A), ["b"] = EndJson(d, B), ["section"] = Section, ["w"] = ShipDesign.R(WA), ["h"] = ShipDesign.R(HA), ["wb"] = ShipDesign.R(WB), ["hb"] = ShipDesign.R(HB),
        ["twist"] = ShipDesign.R(Twist), ["rotA"] = ShipDesign.R(RotA), ["rotB"] = ShipDesign.R(RotB), ["sweepUp"] = ShipDesign.R(SweepUp), ["sweepFore"] = ShipDesign.R(SweepFore), ["bias"] = ShipDesign.R(Bias),
        ["segments"] = Segments, ["mirror"] = Mirror, ["flare"] = ShipDesign.R(Flare), ["buttress"] = ShipDesign.R(Buttress), ["follow"] = Follow, ["squareHole"] = SquareHole, ["openA"] = ShipDesign.R(OpenA), ["openB"] = ShipDesign.R(OpenB),
        ["leaveA"] = ShipDesign.R(LeaveA), ["leaveB"] = ShipDesign.R(LeaveB), ["leaveForeA"] = ShipDesign.R(LeaveForeA), ["leaveForeB"] = ShipDesign.R(LeaveForeB), ["tanA"] = ShipDesign.R(TanA), ["tanB"] = ShipDesign.R(TanB),
        ["collar"] = Collar, ["collarHeight"] = ShipDesign.R(CollarHeight), ["collarMargin"] = ShipDesign.R(CollarMargin), ["collarBevel"] = ShipDesign.R(CollarBevel),
        ["clad"] = Clad == "Off" ? null : new JsonObject { ["kind"] = Clad, ["seed"] = CladSeed, ["size"] = ShipDesign.R(CladSize), ["depth"] = ShipDesign.R(CladDepth) },
        ["buttressClad"] = ButtressClad == "Off" ? null : new JsonObject { ["kind"] = ButtressClad, ["seed"] = ButtressSeed, ["size"] = ShipDesign.R(ButtressSize), ["depth"] = ShipDesign.R(ButtressDepth) },
        ["matchClad"] = MatchClad ? true : null,
        ["matchButtress"] = MatchButtress ? true : null,
        ["matchColour"] = MatchColour ? null : false,
        ["paint"] = MatchColour ? null : new JsonObject { ["skin"] = Own.Skin, ["accent"] = Own.Accent, ["amount"] = ShipDesign.R(Own.AccentAmount), ["pattern"] = Own.Pattern,
            ["wear"] = ShipDesign.R(Own.Wear), ["seed"] = Own.PaintSeed, ["material"] = Own.Material },
    };

    /// <summary>The cladding a section is drawn with: its own, or (matched) hull A's as that body draws it.</summary>
    private readonly record struct CladSpec(string Kind, int Seed, double Size, double Depth, double Damage, int DamageSeed);

    private static CladSpec Spec(HullBody s) => new(s.Clad, s.CladSeed, s.CladSize, s.CladDepth, s.Damage, s.DamageSeed);

    private static JsonObject EndJson(ShipDesign d, End e) => e.Face == FaceSide
        ? new() { ["hull"] = d.HullIndex(e.Hull), ["copy"] = e.Copy, ["z"] = ShipDesign.R(e.Z), ["t"] = ShipDesign.R4(e.T), ["side"] = e.Side }
        : new() { ["hull"] = d.HullIndex(e.Hull), ["copy"] = e.Copy, ["face"] = Faces[e.Face].ToLowerInvariant(), ["u"] = ShipDesign.R4(e.U), ["v"] = ShipDesign.R4(e.V) };

    /// <summary>Reads a join; null when an end names no body.</summary>
    public static HullJoin? FromJson(ShipDesign d, JsonObject o, int id)
    {
        End? E(JsonNode? n)
        {
            if (n is not JsonObject e)
                return null;
            var hi = (int?)e["hull"] ?? -1;
            if (hi < 0 || hi >= d.Hulls.Count)
                return null;
            // Files from before the cap ends hold no face: the side skin.
            var face = ((string?)e["face"])?.ToLowerInvariant() switch { "fore" => FaceFore, "aft" => FaceAft, _ => FaceSide };
            return new End
            {
                Hull = d.Hulls[hi].Id, Copy = (int?)e["copy"] is 1 ? 1 : 0, Z = ShipDesign.F(e["z"], 0), T = Math.Clamp(ShipDesign.F(e["t"], 0.5), 0, 1),
                Side = (int?)e["side"] is -1 ? -1 : 1, Face = face,
                U = Math.Clamp(ShipDesign.F(e["u"], 0), -1, 1), V = Math.Clamp(ShipDesign.F(e["v"], 0.5), 0, 1),
            };
        }
        if (E(o["a"]) is not { } a || E(o["b"]) is not { } b)
            return null;
        var j = new HullJoin { Id = id, A = a, B = b, Section = (string?)o["section"] ?? "Round", WA = ShipDesign.F(o["w"], 1.2), HA = ShipDesign.F(o["h"], 1.2),
            Segments = (int?)o["segments"] ?? 6, Mirror = (bool?)o["mirror"] ?? true, Flare = ShipDesign.F(o["flare"], 1.6), Buttress = ShipDesign.F(o["buttress"], 0.8), Follow = (bool?)o["follow"] ?? false, SquareHole = (bool?)o["squareHole"] ?? false, OpenA = ShipDesign.F(o["openA"], 0.9), OpenB = ShipDesign.F(o["openB"], 0.9) };
        // Files from before the taper hold one size: both ends take it.
        j.WB = ShipDesign.F(o["wb"], j.WA);
        j.HB = ShipDesign.F(o["hb"], j.HA);
        j.Twist = ShipDesign.F(o["twist"], 0);
        j.RotA = ShipDesign.F(o["rotA"], 0);
        j.RotB = ShipDesign.F(o["rotB"], 0);
        j.SweepUp = ShipDesign.F(o["sweepUp"], 0);
        j.SweepFore = ShipDesign.F(o["sweepFore"], 0);
        j.Bias = ShipDesign.F(o["bias"], 0);
        // Files from before the arched arms: a straight departure along each normal.
        j.LeaveA = ShipDesign.F(o["leaveA"], 0);
        j.LeaveB = ShipDesign.F(o["leaveB"], 0);
        j.LeaveForeA = ShipDesign.F(o["leaveForeA"], 0);
        j.LeaveForeB = ShipDesign.F(o["leaveForeB"], 0);
        j.TanA = ShipDesign.F(o["tanA"], 0.9);
        j.TanB = ShipDesign.F(o["tanB"], 0.9);
        j.Collar = (bool?)o["collar"] ?? true;
        j.CollarHeight = ShipDesign.F(o["collarHeight"], 0.22);
        j.CollarMargin = ShipDesign.F(o["collarMargin"], 0.25);
        j.CollarBevel = ShipDesign.F(o["collarBevel"], 0.05);
        if (o["clad"] is JsonObject c)
        {
            j.Clad = (string?)c["kind"] ?? "Off";
            j.CladSeed = (int?)c["seed"] ?? 1;
            j.CladSize = ShipDesign.F(c["size"], 1.5);
            j.CladDepth = ShipDesign.F(c["depth"], 1);
        }
        if (o["buttressClad"] is JsonObject bc)
        {
            j.ButtressClad = (string?)bc["kind"] ?? "Off";
            j.ButtressSeed = (int?)bc["seed"] ?? 1;
            j.ButtressSize = ShipDesign.F(bc["size"], 0.8);
            j.ButtressDepth = ShipDesign.F(bc["depth"], 1);
        }
        j.MatchClad = (bool?)o["matchClad"] ?? false;
        j.MatchButtress = (bool?)o["matchButtress"] ?? false;
        j.MatchColour = (bool?)o["matchColour"] ?? true;
        j.SetOwnPaint(o["paint"] as JsonObject);
        j.Tidy();
        return j;
    }

    /// <summary>Sets the connection's own paint from a "paint" object (skin, accent, amount, pattern, wear, seed, material); missing keys kept.</summary>
    public void SetOwnPaint(JsonObject? p)
    {
        if (p is null)
            return;
        if (p["skin"] is JsonValue sk && ((string?)sk)?.Length == 7) Own.Skin = (string)sk!;
        if (p["accent"] is JsonValue ac && ((string?)ac)?.Length == 7) Own.Accent = (string)ac!;
        if (p["amount"] is not null) Own.AccentAmount = Math.Clamp(ShipDesign.F(p["amount"], 0), 0, 1);
        if (p["pattern"] is JsonValue pt && HullBody.PaintPatterns.Contains((string?)pt)) Own.Pattern = (string)pt!;
        if (p["wear"] is not null) Own.Wear = Math.Clamp(ShipDesign.F(p["wear"], 0), 0, 1);
        if (p["seed"] is not null) Own.PaintSeed = Math.Clamp((int?)p["seed"] ?? 1, 0, 99999);
        if (p["material"] is JsonValue m) Own.Material = HullBody.MaterialOr((string?)m, Own.Material);
    }

    public void Tidy()
    {
        if (Array.FindIndex(DesignTemplates.Sections, s => s.Name == Section) < 0 && !ExtraSections.Contains(Section))
            Section = "Round";
        WA = Math.Clamp(WA, 0.2, 20);
        HA = Math.Clamp(HA, 0.2, 20);
        WB = Math.Clamp(WB, 0.2, 20);
        HB = Math.Clamp(HB, 0.2, 20);
        Twist = Math.Clamp(Twist, -MaxTwist, MaxTwist);
        RotA = Math.Clamp(RotA, -MaxRot, MaxRot);
        RotB = Math.Clamp(RotB, -MaxRot, MaxRot);
        SweepUp = Math.Clamp(SweepUp, -MaxSweep, MaxSweep);
        SweepFore = Math.Clamp(SweepFore, -MaxSweep, MaxSweep);
        Bias = Math.Clamp(Bias, -MaxBias, MaxBias);
        LeaveA = Math.Clamp(LeaveA, -MaxLeave, MaxLeave);
        LeaveB = Math.Clamp(LeaveB, -MaxLeave, MaxLeave);
        LeaveForeA = Math.Clamp(LeaveForeA, -MaxLeave, MaxLeave);
        LeaveForeB = Math.Clamp(LeaveForeB, -MaxLeave, MaxLeave);
        TanA = Math.Clamp(TanA, MinTan, MaxTan);
        TanB = Math.Clamp(TanB, MinTan, MaxTan);
        Segments = Math.Clamp(Segments, 1, MaxSegments);
        OpenA = Math.Clamp(OpenA, 0, 1.5);
        Flare = Math.Clamp(Flare, 1, 4);
        Buttress = Math.Clamp(Buttress, 0, 10);
        CollarHeight = Math.Clamp(CollarHeight, 0.02, MaxCollarHeight);
        CollarMargin = Math.Clamp(CollarMargin, 0, MaxCollarMargin);
        CollarBevel = Math.Clamp(CollarBevel, 0, Math.Min(CollarHeight, CollarMargin) * 0.9);
        OpenB = Math.Clamp(OpenB, 0, 1.5);
        if (!HullBody.CladKinds.Contains(Clad))
            Clad = "Off";
        CladSeed = Math.Clamp(CladSeed, 0, 99999);
        CladSize = Math.Clamp(CladSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        CladDepth = Math.Clamp(CladDepth, HullBody.CladDepthMin, HullBody.CladDepthMax);
        if (!HullBody.CladKinds.Contains(ButtressClad))
            ButtressClad = "Off";
        ButtressSeed = Math.Clamp(ButtressSeed, 0, 99999);
        ButtressSize = Math.Clamp(ButtressSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        ButtressDepth = Math.Clamp(ButtressDepth, HullBody.CladDepthMin, HullBody.CladDepthMax);
    }

    /// <summary>The section as a closed ring of (across, up) points fitting a unit square (-0.5..0.5), the +x side first.</summary>
    public static List<(double U, double V)> Ring(string section)
    {
        if (Polygon(section) is { } poly)
            return poly;
        var p = Array.Find(DesignTemplates.Sections, s => s.Name == section).P ?? DesignTemplates.Sections[0].P;
        var w = p.Max(q => q.H);
        double y0 = p.Min(q => q.Y), y1 = p.Max(q => q.Y);
        var ring = new List<(double, double)>();
        foreach (var (h, y) in p)
            ring.Add((h / w / 2, (y - (y0 + y1) / 2) / Math.Max(1e-6, y1 - y0)));
        for (var k = p.Count - 1; k >= 0; k--)
            ring.Add((-p[k].H / w / 2, (p[k].Y - (y0 + y1) / 2) / Math.Max(1e-6, y1 - y0)));
        return ring;
    }

    /// <summary>The connection's own sections as rings in the unit square (null for a hull gallery section): corners with points along each edge.</summary>
    private static List<(double U, double V)>? Polygon(string section)
    {
        List<(double, double)> Edges((double, double)[] c, int per)
        {
            var l = new List<(double, double)>();
            for (var i = 0; i < c.Length; i++)
            {
                var (a, b) = (c[i], c[(i + 1) % c.Length]);
                for (var k = 0; k < per; k++)
                    l.Add((a.Item1 + (b.Item1 - a.Item1) * k / per, a.Item2 + (b.Item2 - a.Item2) * k / per));
            }
            return l;
        }
        switch (section)
        {
            case "TriangleUp": return Edges([(0.5, -0.5), (0, 0.5), (-0.5, -0.5)], 6);
            case "TriangleDown": return Edges([(0.5, 0.5), (-0.5, 0.5), (0, -0.5)], 6);
            case "Diamond": return Edges([(0.5, 0), (0, 0.5), (-0.5, 0), (0, -0.5)], 4);
            case "Hexagon":
            {
                var c = new (double, double)[6];
                for (var i = 0; i < 6; i++)
                    c[i] = (0.5 * Math.Cos(i * Math.PI / 3), 0.5 * Math.Sin(i * Math.PI / 3) / Math.Sin(Math.PI / 3));
                return Edges(c, 3);
            }
            case "Wedge":
            {
                // An aerofoil (NACA 00xx thickness): a round leading edge at -U, thinning to a sharp trailing edge at +U, scaled to the full height.
                static double T(double x) => 0.2969 * Math.Sqrt(x) - 0.126 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1036 * x * x * x * x;
                const int n = 14;
                var tmax = T(0.3);
                var l = new List<(double, double)>();
                for (var k = n; k >= 0; k--)
                {
                    var x = 0.5 - 0.5 * Math.Cos(Math.PI * k / n);
                    l.Add((x - 0.5, 0.5 * T(x) / tmax));
                }
                for (var k = 1; k < n; k++)
                {
                    var x = 0.5 - 0.5 * Math.Cos(Math.PI * k / n);
                    l.Add((x - 0.5, -0.5 * T(x) / tmax));
                }
                return l;
            }
        }
        return null;
    }

    /// <summary>A ring point's height with the thickness bias: the up side grows by 1 + bias, the down side shrinks by 1 - bias.</summary>
    private double Biased(double v) => v * (v > 0 ? 1 + Bias : 1 - Bias);

    /// <summary>The join's mesh (and its twin when mirrored off the centreline); empty when an end's body is gone.</summary>
    public DesignMesh Mesh(ShipDesign d) => Build(d, null);

    /// <summary>The strut's middle line as rings' frames (middle, across, up), so rooms can run through it (Karl, 2026-11-24).</summary>
    public List<(double[] C, double[] S, double[] U, double W, double H)> Spine(ShipDesign d)
    {
        var key = GeomKey(d);
        if (_spine is { } hit && hit.Key == key)
            return hit.L;
        var l = new List<(double[] C, double[] S, double[] U, double W, double H)>();
        Build(d, null, l);
        if (Mirror && l.Count > 0 && World(d, A) is { } a && Math.Abs(a.P[0]) > 0.3)
            foreach (var (c, sd, u, w, hh) in l.ToArray())
                l.Add(([-c[0], c[1], c[2]], [-sd[0], sd[1], sd[2]], [-u[0], u[1], u[2]], w, hh));
        _spine = (key, l);
        return l;
    }

    /// <summary>Whether a world point is inside a strut's walls (margin m in from them), by its nearest spine frame.</summary>
    public bool Inside(List<(double[] C, double[] S, double[] U, double W, double H)> spine, double x, double y, double z, double margin)
    {
        var best = double.MaxValue;
        var bi = -1;
        for (var i = 0; i < spine.Count; i++)
        {
            var c = spine[i].C;
            var dd = Sq(x - c[0]) + Sq(y - c[1]) + Sq(z - c[2]);
            if (dd < best) (best, bi) = (dd, i);
        }
        if (bi < 0)
            return false;
        var (cc, sd, up, fw, fh) = spine[bi];
        double qx = x - cc[0], qy = y - cc[1], qz = z - cc[2];
        var hw = fw / 2 - margin;
        var a = qx * up[0] + qy * up[1] + qz * up[2];
        // The bias makes the up and down halves different heights.
        var hh = fh / 2 * (a > 0 ? 1 + Bias : 1 - Bias) - margin;
        if (hw <= 0 || hh <= 0)
            return false;
        var u = (qx * sd[0] + qy * sd[1] + qz * sd[2]) / hw;
        var v = a / hh;
        return u * u + v * v <= 1;
    }

    /// <summary>A connection's mouth on a body: where its end ring sits (middle, along, across, up) and the section's size.</summary>
    public readonly record struct Mouth(int Hull, double[] C, double[] T, double[] S, double[] U, double W, double H, bool Square)
    {
        /// <summary>Whether a world point is in the opening (the body's skin is left out there, so the connection opens into it).</summary>
        public bool Holds(double x, double y, double z)
        {
            double qx = x - C[0], qy = y - C[1], qz = z - C[2];
            var reach = Math.Max(W, H) + 0.5;
            var a = qx * T[0] + qy * T[1] + qz * T[2];
            if (Math.Abs(a) > reach)
                return false;
            if (W < 0.01 || H < 0.01)
                return false;
            var u = (qx * S[0] + qy * S[1] + qz * S[2]) / W;
            var v = (qx * U[0] + qy * U[1] + qz * U[2]) / H;
            return Square ? Math.Abs(u) <= 0.5 && Math.Abs(v) <= 0.5 : u * u + v * v <= 0.25;
        }
    }

    /// <summary>The mouths at both ends (and the twin's when mirrored).</summary>
    public List<Mouth> Mouths(ShipDesign d)
    {
        var key = GeomKey(d);
        if (_mouths is { } hit && hit.Key == key)
            return hit.L;
        var l = new List<Mouth>();
        var f = new List<Foot>();
        Build(d, l, null, f);
        _mouths = (key, l);
        _feet = (key, f);
        return l;
    }

    /// <summary>The buttresses' footprints on the skins at both ends (and the twin's), which a body's cladding stays off (Karl, 2026-11-25).</summary>
    public List<Foot> Feet(ShipDesign d)
    {
        Mouths(d);
        return _feet!.Value.L;
    }

    private (string Key, List<Foot> L)? _feet;

    /// <summary>How far past a footprint's outline a body's plates stay off (m): a cell that straddles the edge goes bare, so the hull's plates don't poke through the buttress.</summary>
    public const double FootMargin = 0.08;

    /// <summary>
    /// Where a connection's end ring (the flared buttress's base, or the strut's own) meets a skin, as an outline round the line in its end
    /// frame (middle, along, across, up), grown by <see cref="FootMargin"/>.
    /// </summary>
    public sealed record Foot(int Hull, double[] C, double[] T, double[] S, double[] U, double[] Pu, double[] Pv, double Reach)
    {
        public bool Holds(double x, double y, double z)
        {
            double qx = x - C[0], qy = y - C[1], qz = z - C[2];
            if (Math.Abs(qx * T[0] + qy * T[1] + qz * T[2]) > Reach)
                return false;
            double u = qx * S[0] + qy * S[1] + qz * S[2], v = qx * U[0] + qy * U[1] + qz * U[2];
            var inside = false;
            for (int i = 0, k = Pu.Length - 1; i < Pu.Length; k = i++)
                if ((Pv[i] > v) != (Pv[k] > v) && u < (Pu[k] - Pu[i]) * (v - Pv[i]) / (Pv[k] - Pv[i]) + Pu[i])
                    inside = !inside;
            return inside;
        }
    }

    // The last mouths and spine worked out, under the key of all they come from (the checks ask for them once a room).
    private (string Key, List<Mouth> L)? _mouths;
    private (string Key, List<(double[] C, double[] S, double[] U, double W, double H)> L)? _spine;

    /// <summary>Everything the connection's shape comes from (not its cladding), with the shapes of the bodies it joins, as text.</summary>
    public string GeomKey(ShipDesign d) => ShipDesign.Whole(() =>
    {
        var o = ToJson(d);
        o.Remove("clad");
        o.Remove("buttressClad");
        o.Remove("matchClad");
        o.Remove("matchButtress");
        o.Remove("matchColour");
        o.Remove("paint");
        return Id + ":" + o.ToJsonString() + "|" + d.Hull(A.Hull)?.ShapeKey() + "|" + d.Hull(B.Hull)?.ShapeKey();
    });

    private DesignMesh Build(ShipDesign d, List<Mouth>? mouths, List<(double[] C, double[] S, double[] U, double W, double H)>? spine = null, List<Foot>? feet = null)
    {
        var m = new DesignMesh();
        if (World(d, A) is not { } a || World(d, B) is not { } b)
            return m;
        double[] p0 = [a.P[0] - a.N[0] * Sink, a.P[1] - a.N[1] * Sink, a.P[2] - a.N[2] * Sink];
        double[] p1 = [b.P[0] - b.N[0] * Sink, b.P[1] - b.N[1] * Sink, b.P[2] - b.N[2] * Sink];
        var len = Math.Sqrt(Sq(p1[0] - p0[0]) + Sq(p1[1] - p0[1]) + Sq(p1[2] - p0[2]));
        if (len < 0.05)
            return m;
        // A Hermite curve (a cubic Bezier) leaving each skin along its normal, tilted by the end's leave angles and carried its tangent
        // length before it bends: a pylon stands off the hull and meets the pod square on, or arches out of the top into a nacelle's side.
        var da = Leave(a.N, [p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]], LeaveA, LeaveForeA);
        var db = Leave(b.N, [p0[0] - p1[0], p0[1] - p1[1], p0[2] - p1[2]], LeaveB, LeaveForeB);
        double ka = len * TanA, kb = len * TanB;
        double[] m0 = [da[0] * ka, da[1] * ka, da[2] * ka], m1 = [-db[0] * kb, -db[1] * kb, -db[2] * kb];
        var ring = Ring(Section);
        var ha = d.Hull(A.Hull)!;
        var pb = PaintOf(d);
        var col = DesignMesh.Hex(pb.Skin);
        var hb = d.Hull(B.Hull)!;
        (double[] C, double[] T) Curve(double s)
        {
            double h00 = 2 * s * s * s - 3 * s * s + 1, h10 = s * s * s - 2 * s * s + s, h01 = -2 * s * s * s + 3 * s * s, h11 = s * s * s - s * s;
            double d00 = 6 * s * s - 6 * s, d10 = 3 * s * s - 4 * s + 1, d01 = -6 * s * s + 6 * s, d11 = 3 * s * s - 2 * s;
            var c = new double[3];
            var t = new double[3];
            for (var q = 0; q < 3; q++)
            {
                c[q] = h00 * p0[q] + h10 * m0[q] + h01 * p1[q] + h11 * m1[q];
                t[q] = d00 * p0[q] + d10 * m0[q] + d01 * p1[q] + d11 * m1[q];
            }
            Norm(t);
            return (c, t);
        }
        // Where the middle line leaves the first skin and enters the second: the connection is drawn between, its end rings laid on the skins
        // (Karl, 2026-11-24: it meets the hull, not a hoop inside it).
        static double Cross0(Func<double, bool> inside, double lo, double hi)
        {
            if (!inside(lo) || inside(hi))
                return lo;
            for (var i = 0; i < 24; i++)
            {
                var mid = (lo + hi) / 2;
                if (inside(mid)) lo = mid; else hi = mid;
            }
            return lo;
        }
        bool In(HullBody h, double[] q) => h.Depth(q[0], q[1], q[2]) > 0;
        var s0 = Cross0(s => In(ha, Curve(s).C), 0, 0.5);
        var s1 = 1 - Cross0(s => In(hb, Curve(1 - s).C), 0, 0.5);
        if (s1 <= s0 + 1e-3)
            return m;
        var rings = new List<(double, double, double)[]>();
        var centres = new List<double[]>();
        double[]? side = null;
        var collars = new List<PlateMesh>();
        var plateFeet = new List<(HullBody H, int Face, Foot Body)>();
        var ends = new List<Mouth>(); // this build's own two mouths (unmirrored), kept clear of the buttresses' plates
        // Where the rings go: the ends on the skins (flared into the buttresses' cladding), the strut's own rings between.
        var dl = Math.Min(Buttress / len, 0.3 * (s1 - s0));
        var at = new List<(double S, double Scale)>();
        if (dl > 1e-4)
            at.Add((s0, Flare));
        for (var q = 0; q <= Segments; q++)
            at.Add((s0 + dl + (s1 - s0 - 2 * dl) * q / Segments, dl > 1e-4 ? 1 : Flare));
        if (dl > 1e-4)
            at.Add((s1, Flare));
        var last = at.Count - 1;
        double[] bowDir = [0, SweepUp, SweepFore];
        for (var i = 0; i <= last; i++)
        {
            var (c, t) = Curve(at[i].S);
            var sc = at[i].Scale;
            // Along the span 0..1: the taper, the twist and the bow (zero at both ends, so they still meet their skins).
            var q = Math.Clamp((at[i].S - s0) / (s1 - s0), 0, 1);
            double rw = WA + (WB - WA) * q, rh = HA + (HB - HA) * q;
            if (SweepUp != 0 || SweepFore != 0)
            {
                var bow = 4 * q * (1 - q);
                var dbow = 4 * (1 - 2 * q) / ((s1 - s0) * len);
                for (var e = 0; e < 3; e++)
                {
                    c[e] += bowDir[e] * bow;
                    t[e] += bowDir[e] * dbow;
                }
                Norm(t);
            }
            // The ring's across axis: carried along from the last ring (so the strut doesn't twist), first from the world's up.
            if (side is null)
            {
                double[] up = Math.Abs(t[1]) > 0.9 ? [0, 0, 1] : [0, 1, 0];
                side = Cross(up, t);
            }
            else
            {
                var dot = side[0] * t[0] + side[1] * t[1] + side[2] * t[2];
                side = [side[0] - t[0] * dot, side[1] - t[1] * dot, side[2] - t[2] * dot];
            }
            Norm(side);
            var upv0 = Cross(t, side);
            // The twist rolls this ring's frame round the line, on top of each end's own roll blended from A's to B's; the carried axis stays
            // untwisted. The mouths take the end rings' frames, so the holes roll too; the twin is mirrored from it, so its roll is mirrored.
            var tw = (RotA + (RotB - RotA) * q + Twist * q) * Math.PI / 180;
            double cs = Math.Cos(tw), sn = Math.Sin(tw);
            double[] rs = [side[0] * cs + upv0[0] * sn, side[1] * cs + upv0[1] * sn, side[2] * cs + upv0[2] * sn];
            double[] upv = [upv0[0] * cs - side[0] * sn, upv0[1] * cs - side[1] * sn, upv0[2] * cs - side[2] * sn];
            // The strut's own rings (not the flared ends), sampled finely enough for the room check.
            if (spine is not null && sc == 1 || spine is not null && i > 0 && i < last)
                spine.Add((c, rs, upv, rw, rh));
            if (i == 0 || i == last)
            {
                var hid = i == 0 ? A.Hull : B.Hull;
                var f = i == 0 ? OpenA : OpenB;
                // Each end's own size (and the bias's thicker half, so the hole covers it).
                var mh = rh * f * (1 + Math.Abs(Bias));
                ends.Add(new Mouth(hid, c, t, rs, upv, rw * f, mh, SquareHole));
                if (mouths is not null)
                mouths.Add(new Mouth(hid, c, t, rs, upv, rw * f, mh, SquareHole));
                if (mouths is not null && Mirror && Math.Abs(a.P[0]) > 0.3)
                    mouths.Add(new Mouth(hid, [-c[0], c[1], c[2]], [-t[0], t[1], t[2]], [-rs[0], rs[1], rs[2]], [-upv[0], upv[1], upv[2]], rw * f, mh, SquareHole));
            }
            var r = new (double, double, double)[ring.Count];
            for (var j = 0; j < ring.Count; j++)
            {
                var (u, v) = ring[j];
                double uw = u * rw * sc, vh = Biased(v) * rh * sc;
                r[j] = (c[0] + rs[0] * uw + upv[0] * vh, c[1] + rs[1] * uw + upv[1] * vh, c[2] + rs[2] * uw + upv[2] * vh);
            }
            // An end ring slid along the line onto its skin, each point where it meets it.
            if (i == 0 || i == last)
            {
                var h = i == 0 ? ha : hb;
                var dir = i == 0 ? -1.0 : 1.0;
                var reach = 3 * Math.Max(rw, rh) * sc * (1 + Math.Abs(Bias)) + 2;
                for (var j = 0; j < r.Length; j++)
                {
                    var v = r[j];
                    double[] At(double f) => [v.Item1 + t[0] * dir * f, v.Item2 + t[1] * dir * f, v.Item3 + t[2] * dir * f];
                    // f from 0 (this point) to reach (deeper in): the first f inside the skin.
                    // Inside already: back out to where it leaves the skin; else on in to where it meets it.
                    var inside = In(h, At(0));
                    if (inside ? In(h, At(-reach)) : !In(h, At(reach)))
                    {
                        // The line along the strut misses this body (a wide flare past a curved side): the nearest skin point instead.
                        r[j] = Nearest(h, i == 0 ? A.Face : B.Face, v.Item1, v.Item2, v.Item3);
                        continue;
                    }
                    double lo = inside ? -reach : 0, hi = inside ? 0 : reach;
                    for (var it = 0; it < 20; it++)
                    {
                        var mid = (lo + hi) / 2;
                        if (In(h, At(mid))) hi = mid; else lo = mid;
                    }
                    var e = At(hi + 0.01);
                    r[j] = (e[0], e[1], e[2]);
                }
                if (feet is not null)
                {
                    // The outline in the end frame, grown out from its middle by the margin.
                    var n = r.Length;
                    double[] pu = new double[n], pv = new double[n];
                    double mu = 0, mv = 0, far = 0;
                    for (var j = 0; j < n; j++)
                    {
                        double qx = r[j].Item1 - c[0], qy = r[j].Item2 - c[1], qz = r[j].Item3 - c[2];
                        pu[j] = qx * rs[0] + qy * rs[1] + qz * rs[2];
                        pv[j] = qx * upv[0] + qy * upv[1] + qz * upv[2];
                        far = Math.Max(far, Math.Abs(qx * t[0] + qy * t[1] + qz * t[2]));
                        mu += pu[j] / n;
                        mv += pv[j] / n;
                    }
                    if (Collar)
                    {
                        // The base plate's own outline (the one it is drawn from), densely sampled, grown by the margin past its edge.
                        var (fo, sub) = PlateOutline(r, c, rs, upv, Follow && dl > 1e-4 ? ring : null);
                        pu = new double[n * sub];
                        pv = new double[n * sub];
                        for (var k = 0; k < n * sub; k++)
                            (pu[k], pv[k]) = PlateAt(fo, n, sub, k, CollarMargin + FootMargin);
                        far += CollarMargin + 0.3;
                    }
                    else
                        for (var j = 0; j < n; j++)
                        {
                            double du = pu[j] - mu, dv = pv[j] - mv, l = Math.Max(1e-9, Math.Sqrt(du * du + dv * dv));
                            pu[j] += du / l * FootMargin;
                            pv[j] += dv / l * FootMargin;
                        }
                    var hid = i == 0 ? A.Hull : B.Hull;
                    var reachF = far + 0.5;
                    feet.Add(new Foot(hid, c, t, rs, upv, pu, pv, reachF));
                    if (Mirror && Math.Abs(a.P[0]) > 0.3)
                        feet.Add(new Foot(hid, [-c[0], c[1], c[2]], [-t[0], t[1], t[2]], [-rs[0], rs[1], rs[2]], [-upv[0], upv[1], upv[2]], pu, pv, reachF));
                }
            }
            if ((i == 0 || i == last) && Collar && spine is null && mouths is null)
            {
                // The collar round this end (drawn below), then the ring lifted onto its top: the buttress stands on the pad.
                var h = i == 0 ? ha : hb;
                var face = i == 0 ? A.Face : B.Face;
                var plate = CollarRing(r, c, t, i == 0 ? -1.0 : 1.0, rs, upv, h, face, pt => In(h, pt), Follow && dl > 1e-4 ? ring : null);
                collars.Add(plate);
                plateFeet.Add((h, face, plate.Body));
                for (var j = 0; j < r.Length; j++)
                    r[j] = OnTop(h, face, r[j].Item1, r[j].Item2, r[j].Item3, CollarHeight - 0.005);
            }
            rings.Add(r);
            centres.Add(c);
        }
        if (spine is not null || mouths is not null)
            return m;
        foreach (var cr in collars)
            DrawCollar(m, cr.Rings, col); // the mouths and the spine are known by here: what follows only draws
        // Follow the hull: each flared end's outer ring laid onto the skin 4 mm off it, the rings between blending from the strut's section
        // out to that footprint, so the buttress's base wraps the hull's curve and meets it flush.
        var blend = 0;
        if (Follow && dl > 1e-4)
        {
            blend = FollowRings;
            var lift = Collar ? CollarHeight - 0.005 : FollowLift;
            // With a collar, the fillet rises from the collar's clean inner top edge (Karl, 2026-11-25: no playdough).
            var fa = Collar && collars.Count == 2 ? Lifted(collars[0].InnerTop) : null;
            var fb = Collar && collars.Count == 2 ? Lifted(collars[1].InnerTop) : null;
            FollowEnd(rings, centres, 0, 1, ha, A.Face, lift, fa);
            FollowEnd(rings, centres, rings.Count - 1, rings.Count - 2, hb, B.Face, lift, fb);
            last = rings.Count - 1;
        }
        for (var i = 0; i < last; i++)
            for (var j = 0; j < ring.Count; j++)
            {
                var j1 = (j + 1) % ring.Count;
                // Hull A's paint (the joint takes its colour from A): along by the ring's share of the length, round by j.
                var pc = pb.Paint(len * i / Math.Max(1, last), (j + 0.5) / ring.Count, 4, Id * 4099 + i * 64 + j);
                var shade = DesignMesh.Tone(pc, (i % 2 == 0 ? 1.0 : 0.94) * (j % 2 == 0 ? 1.0 : 0.9));
                m.Quad(rings[i][j], rings[i + 1][j], rings[i + 1][j1], rings[i][j1], shade, false);
            }
        // The strut's own rings are clad; the flared ends into the skins (dl > 0) stay bare.
        var src = d.CladSource(ha);
        var strut = MatchClad ? Spec(src) : new CladSpec(Clad, CladSeed, CladSize, CladDepth, ha.Damage, ha.DamageSeed);
        var butt = MatchButtress ? Spec(src) : new CladSpec(ButtressClad, ButtressSeed, ButtressSize, ButtressDepth, ha.Damage, ha.DamageSeed);
        if (strut.Kind != "Off")
            CladStrut(m, rings, centres, dl > 1e-4 ? 1 + blend : 0, dl > 1e-4 ? last - 1 - blend : last, col, pb, strut);
        // The buttresses: their own cladding, in rows from the strut out to where the flare sinks under each skin.
        if (butt.Kind != "Off" && dl > 1e-4)
        {
            // Open: a mouth, or under a base plate's top (Karl, 2026-11-25: the arm's plates never cross below it).
            bool Open((double X, double Y, double Z) p) => ends.Any(e => e.Holds(p.X, p.Y, p.Z)) || plateFeet.Any(f => UnderPlate(f.H, f.Face, f.Body, p));
            CladButtress(m, rings, centres, 1 + blend, 0, col, pb, Open, 1, butt);
            CladButtress(m, rings, centres, last - 1 - blend, last, col, pb, Open, 2, butt);
        }
        if (Mirror && Math.Abs(a.P[0]) > 0.3)
            m.MirrorFrom(0, 0);
        return m;
    }

    /// <summary>
    /// Plated cladding on the strut between rings i0 and i1 (the user, 2026-11-24: the nacelles' arms had none): a grid round the ring
    /// (by its length) and along the strut (by the middle line's), laid with the bodies' plates (<see cref="DesignScene.LayPlates"/>),
    /// each plate's normal out from the middle line. Seeded, so the same design always clads the same.
    /// </summary>
    private void CladStrut(DesignMesh m, List<(double, double, double)[]> rings, List<double[]> centres, int i0, int i1, (byte, byte, byte) col, HullBody ha, CladSpec cs)
    {
        if (i1 <= i0)
            return;
        var nr = rings[0].Length;
        // Across: the share of the way round the ring, by the length of the strut's middle ring; along: by the middle line's length.
        var mid = rings[(i0 + i1) / 2];
        var cu = new double[nr + 1];
        for (var j = 0; j < nr; j++)
        {
            var (p, q) = (mid[j], mid[(j + 1) % nr]);
            cu[j + 1] = cu[j] + Math.Sqrt(Sq(q.Item1 - p.Item1) + Sq(q.Item2 - p.Item2) + Sq(q.Item3 - p.Item3));
        }
        var cv = new double[i1 - i0 + 1];
        for (var i = i0; i < i1; i++)
            cv[i - i0 + 1] = cv[i - i0] + Math.Sqrt(Sq(centres[i + 1][0] - centres[i][0]) + Sq(centres[i + 1][1] - centres[i][1]) + Sq(centres[i + 1][2] - centres[i][2]));
        double arc = cu[nr], len = cv[^1];
        if (arc < 0.3 || len < 0.3)
            return;
        static (int K, double F) Find(double[] c, double x)
        {
            var l = Math.Clamp(x, 0, 1) * c[^1];
            for (var k = 0; k < c.Length - 2; k++)
                if (l <= c[k + 1])
                    return (k, c[k + 1] - c[k] < 1e-9 ? 0 : (l - c[k]) / (c[k + 1] - c[k]));
            var n = c.Length - 2;
            return (n, c[n + 1] - c[n] < 1e-9 ? 0 : Math.Clamp((l - c[n]) / (c[n + 1] - c[n]), 0, 1));
        }
        (double X, double Y, double Z) Lerp((double, double, double) p, (double, double, double) q, double f) =>
            (p.Item1 + (q.Item1 - p.Item1) * f, p.Item2 + (q.Item2 - p.Item2) * f, p.Item3 + (q.Item3 - p.Item3) * f);
        (double X, double Y, double Z) P(double u, double v)
        {
            var (j, fu) = Find(cu, u);
            var (i, fv) = Find(cv, v);
            i += i0;
            var j1 = (j + 1) % nr;
            var p = Lerp(rings[i][j], rings[i][j1], fu);
            var q = Lerp(rings[i + 1][j], rings[i + 1][j1], fu);
            return Lerp(p, q, fv);
        }
        (double X, double Y, double Z) Centre(double v)
        {
            var (i, fv) = Find(cv, v);
            i += i0;
            var (a, b) = (centres[i], centres[i + 1]);
            return Lerp((a[0], a[1], a[2]), (b[0], b[1], b[2]), fv);
        }
        (double X, double Y, double Z) Nrm(double u, double v)
        {
            const double e = 1e-3;
            var pu = Sub(P(Math.Min(1, u + e), v), P(Math.Max(0, u - e), v));
            var pv = Sub(P(u, Math.Min(1, v + e)), P(u, Math.Max(0, v - e)));
            double x = pu.Y * pv.Z - pu.Z * pv.Y, y = pu.Z * pv.X - pu.X * pv.Z, z = pu.X * pv.Y - pu.Y * pv.X;
            var l = Math.Sqrt(x * x + y * y + z * z);
            var o = Sub(P(u, v), Centre(v));
            if (l < 1e-12)
            {
                var ol = Math.Max(1e-9, Math.Sqrt(o.X * o.X + o.Y * o.Y + o.Z * o.Z));
                return (o.X / ol, o.Y / ol, o.Z / ol);
            }
            var sg = x * o.X + y * o.Y + z * o.Z >= 0 ? 1 : -1;
            return (sg * x / l, sg * y / l, sg * z / l);
        }
        (double, double, double) W(double u, double v, double off)
        {
            var p = P(u, v);
            var q = Nrm(u, v);
            return (p.X + q.X * off, p.Y + q.Y * off, p.Z + q.Z * off);
        }
        var size = Math.Clamp(cs.Size, HullBody.CladSizeMin, HullBody.CladSizeMax);
        int nu = Math.Clamp((int)Math.Round(arc / size), 1, 64), nv = Math.Clamp((int)Math.Round(len / size), 1, 64);
        var start = m.Tris;
        DesignScene.LayPlates(m, nu, nv, arc, len, new Random(unchecked(cs.Seed * 1000003 + Id * 7919)), cs.Kind == "Heavy", cs.Depth, col, P, Nrm, W, new bool[nu, nv],
            (u, v, cell) => ha.Paint(len * v, u, arc, Id * 8191 + cell, cs.Kind == "Panelled"), 0, cs.Kind == "Hex", cs.Damage, cs.DamageSeed, cs.Kind == "Panelled");
        if (m.Tris - start > DesignScene.MaxCladTris)
            Console.WriteLine($"ship designer: cladding on connection {Id} has {m.Tris - start} triangles, past the bodies' cap of {DesignScene.MaxCladTris}");
    }

    /// <summary>
    /// Plated cladding on one buttress, the flared band between ring iS (on the strut) and ring iE (sunk under the skin): rows along the flare,
    /// each with its own count of cells round by that row's girth (as on a body's tapering terminus bands), so plates don't sliver where it
    /// widens. Cells that reach a mouth stay bare; the plates stand off the surface by at least LayPlates' floor (6 mm).
    /// </summary>
    private void CladButtress(DesignMesh m, List<(double, double, double)[]> rings, List<double[]> centres, int iS, int iE, (byte, byte, byte) col, HullBody ha,
        Func<(double X, double Y, double Z), bool> open, int end, CladSpec cs)
    {
        var nr = rings[0].Length;
        // The rings from the strut (iS) to the skin (iE): one band, or several when the buttress follows the hull.
        var step = iE > iS ? 1 : -1;
        var nb = Math.Abs(iE - iS);
        static (double X, double Y, double Z) L((double, double, double) p, (double, double, double) q, double f) =>
            (p.Item1 + (q.Item1 - p.Item1) * f, p.Item2 + (q.Item2 - p.Item2) * f, p.Item3 + (q.Item3 - p.Item3) * f);
        (int B, double F) Band(double v)
        {
            var x = Math.Clamp(v, 0, 1) * nb;
            var b = Math.Min(nb - 1, (int)x);
            return (iS + b * step, x - b);
        }
        (double X, double Y, double Z) Along(int j, double v)
        {
            var (i, f) = Band(v);
            return L(rings[i][j], rings[i + step][j], f);
        }
        // The flare as a surface: u round the ring (by point index), v from the strut (0) to the skin (1).
        (double X, double Y, double Z) S(double u, double v)
        {
            var x = (u - Math.Floor(u)) * nr;
            var j = Math.Min(nr - 1, (int)x);
            var fu = x - j;
            var j1 = (j + 1) % nr;
            var (p, q) = (Along(j, v), Along(j1, v));
            return L((p.X, p.Y, p.Z), (q.X, q.Y, q.Z), fu);
        }
        (double X, double Y, double Z) C(double v)
        {
            var (i, f) = Band(v);
            var (a, b) = (centres[i], centres[i + step]);
            return L((a[0], a[1], a[2]), (b[0], b[1], b[2]), f);
        }
        double Girth(double v)
        {
            var g = 0.0;
            for (var j = 0; j < nr; j++)
            {
                var (p, q) = (Along(j, v), Along((j + 1) % nr, v));
                g += Math.Sqrt(Sq(q.X - p.X) + Sq(q.Y - p.Y) + Sq(q.Z - p.Z));
            }
            return g;
        }
        var reach = 0.0;
        for (var j = 0; j < nr; j++)
            for (var i = iS; i != iE; i += step)
                reach += Math.Sqrt(Sq(rings[i + step][j].Item1 - rings[i][j].Item1) + Sq(rings[i + step][j].Item2 - rings[i][j].Item2) + Sq(rings[i + step][j].Item3 - rings[i][j].Item3)) / nr;
        if (reach < 0.1 || Girth(0) < 0.3)
            return;
        var size = Math.Clamp(cs.Size, HullBody.CladSizeMin, HullBody.CladSizeMax);
        var rows = Math.Clamp((int)Math.Round(reach / size), 1, 24);
        var heavy = cs.Kind == "Heavy";
        var rng = new Random(unchecked(cs.Seed * 1000003 + Id * 7919 + end * 31337));
        var start = m.Tris;
        for (var r = 0; r < rows; r++)
        {
            double v0 = (double)r / rows, v1 = (double)(r + 1) / rows;
            var rowArc = Girth((v0 + v1) / 2);
            var cells = Math.Clamp((int)Math.Round(rowArc / size), 3, 64);
            var shift = (r & 1) == 1 ? 0.5 / cells : 0;
            (double X, double Y, double Z) P(double u, double v) => S(u + shift, v0 + (v1 - v0) * v);
            (double X, double Y, double Z) N(double u, double v)
            {
                const double e = 1e-3;
                var pu = Sub(P(u + e, v), P(u - e, v));
                var pv = Sub(P(u, v + e), P(u, v - e));
                double x = pu.Y * pv.Z - pu.Z * pv.Y, y = pu.Z * pv.X - pu.X * pv.Z, z = pu.X * pv.Y - pu.Y * pv.X;
                var o = Sub(P(u, v), C(v0 + (v1 - v0) * v));
                var l = Math.Sqrt(x * x + y * y + z * z);
                if (l < 1e-12)
                {
                    var ol = Math.Max(1e-9, Math.Sqrt(o.X * o.X + o.Y * o.Y + o.Z * o.Z));
                    return (o.X / ol, o.Y / ol, o.Z / ol);
                }
                var sg = x * o.X + y * o.Y + z * o.Z >= 0 ? 1 : -1;
                return (sg * x / l, sg * y / l, sg * z / l);
            }
            (double, double, double) W(double u, double v, double off)
            {
                var p = P(u, v);
                var q = N(u, v);
                return (p.X + q.X * off, p.Y + q.Y * off, p.Z + q.Z * off);
            }
            var taken = new bool[cells, 1];
            for (var a = 0; a < cells; a++)
            {
                double u0 = (double)a / cells, u1 = (double)(a + 1) / cells;
                for (var i = 0; i <= 2 && !taken[a, 0]; i++)
                    for (var k = 0; k <= 2; k++)
                    {
                        var q = W(u0 + (u1 - u0) * i / 2, k / 2.0, 0.004);
                        if (open((q.Item1, q.Item2, q.Item3)))
                        {
                            taken[a, 0] = true;
                            break;
                        }
                    }
            }
            DesignScene.LayPlates(m, cells, 1, rowArc, Math.Max(0.05, reach / rows), rng, heavy, cs.Depth, col, P, N, W, taken,
                (u, v, cell) => ha.Paint(v0 * reach, u, rowArc, Id * 12289 + end * 5000 + r * 97 + cell, cs.Kind == "Panelled"), 0, cs.Kind == "Hex", cs.Damage, cs.DamageSeed, cs.Kind == "Panelled");
        }
        if (m.Tris - start > DesignScene.MaxCladTris)
            Console.WriteLine($"ship designer: buttress cladding on connection {Id} has {m.Tris - start} triangles, past the bodies' cap of {DesignScene.MaxCladTris}");
    }

    /// <summary>The rings a following buttress blends through between the strut and its footprint on the skin.</summary>
    public const int FollowRings = 4;

    /// <summary>How far a following buttress's base stands off the skin (m): flush, but not on it.</summary>
    public const double FollowLift = 0.004;

    /// <summary>A base plate as drawn: its rings from the inner foot out over the top to the outer foot (dense), the inner top edge at the
    /// strut's ring resolution (what the buttress lands on), and its body's outline (to its outer edge) for the cladding checks.</summary>
    private sealed record PlateMesh((double, double, double)[][] Rings, (double, double, double)[] InnerTop, Foot Body);

    /// <summary>How many points a base plate's outline has at least, so it hugs a curved skin between the strut's own ring points.</summary>
    public const int PlatePoints = 48;

    /// <summary>The base plate's outline in an end frame (relative to the middle c): for ring point j grown out by g, and the points put in
    /// between each pair of ring points (so the outline has at least PlatePoints).</summary>
    private (Func<int, double, (double U, double V)> F, int Sub) PlateOutline((double, double, double)[] r, double[] c, double[] rs, double[] upv,
        List<(double U, double V)>? clean)
    {
        var n = r.Length;
        double mu = 0, mv = 0;
        var pu = new double[n];
        var pv = new double[n];
        for (var j = 0; j < n; j++)
        {
            double qx = r[j].Item1 - c[0], qy = r[j].Item2 - c[1], qz = r[j].Item3 - c[2];
            pu[j] = qx * rs[0] + qy * rs[1] + qz * rs[2];
            pv[j] = qx * upv[0] + qy * upv[1] + qz * upv[2];
            mu += pu[j] / n;
            mv += pv[j] / n;
        }
        var sub = Math.Max(1, (int)Math.Ceiling((double)PlatePoints / n));
        if (clean is not null && clean.Count == n)
        {
            // A clean outline (Karl, 2026-11-25): the strut's own section scaled to the footprint's extents, grown by scaling each axis
            // (so straight edges stay straight and the margin is even on each side).
            double a = 1e-3, b = 1e-3, su = 1e-6, sv = 1e-6;
            for (var j = 0; j < n; j++)
            {
                a = Math.Max(a, Math.Abs(pu[j] - mu));
                b = Math.Max(b, Math.Abs(pv[j] - mv));
            }
            foreach (var (u, v) in clean) { su = Math.Max(su, Math.Abs(u)); sv = Math.Max(sv, Math.Abs(v)); }
            return ((j, g) => (mu + clean[j % n].U / su * (a + g), mv + clean[j % n].V / sv * (b + g)), sub);
        }
        return ((j, g) =>
        {
            j %= n;
            double du = pu[j] - mu, dv = pv[j] - mv, l = Math.Max(1e-9, Math.Sqrt(du * du + dv * dv));
            return (pu[j] + du / l * g, pv[j] + dv / l * g);
        }, sub);
    }

    /// <summary>Dense outline point k (of n * sub) at grow g: between ring points k / sub and the next.</summary>
    private static (double U, double V) PlateAt(Func<int, double, (double U, double V)> f, int n, int sub, int k, double g)
    {
        var j = k / sub;
        var fr = (double)(k % sub) / sub;
        var (a, b) = (f(j, g), f((j + 1) % n, g));
        return (a.U + (b.U - a.U) * fr, a.V + (b.V - a.V) * fr);
    }

    /// <summary>
    /// A base plate round one end (Karl, 2026-11-25: "take a few points round the base plate and meld it round the hull"): the outline is
    /// sampled densely, each point cast along the strut's line onto the skin, and the plate laid as a thin shell on it: the top HEIGHT out
    /// along the skin's normal at each sample, rings across it so the top follows the curve, and the bevel dropping to 5 mm off the skin.
    /// Rings: inner foot (sunk), inner top, the top's rings out to Margin less the bevel, the bevel's foot (5 mm up), the outer foot (sunk).
    /// </summary>
    private PlateMesh CollarRing((double, double, double)[] r, double[] c, double[] t, double dir, double[] rs, double[] upv, HullBody h, int face,
        Func<double[], bool> inside, List<(double U, double V)>? clean = null)
    {
        var n = r.Length;
        var (fo, sub) = PlateOutline(r, c, rs, upv, clean);
        var nd = n * sub;
        var reach = 2 + CollarMargin * 2;
        // The skin under end-frame point (u, v): cast along the line (in, dir) from the frame's plane; the nearest skin point if it misses.
        (double, double, double) Skin(double u, double v)
        {
            double[] At(double f) => [c[0] + rs[0] * u + upv[0] * v + t[0] * dir * f, c[1] + rs[1] * u + upv[1] * v + t[1] * dir * f,
                c[2] + rs[2] * u + upv[2] * v + t[2] * dir * f];
            var inn = inside(At(0));
            if (inn ? inside(At(-reach)) : !inside(At(reach)))
            {
                var p0 = At(0);
                return OnTop(h, face, p0[0], p0[1], p0[2], 0);
            }
            double lo = inn ? -reach : 0, hi = inn ? 0 : reach;
            for (var it = 0; it < 24; it++)
            {
                var mid = (lo + hi) / 2;
                if (inside(At(mid))) hi = mid; else lo = mid;
            }
            var e = At(hi);
            return OnTop(h, face, e[0], e[1], e[2], 0);
        }
        // The levels across the plate, inner to outer: (grow, lift off the skin).
        const int topRings = 4;
        var levels = new List<(double G, double L)> { (0, -CollarSinkDepth), (0, CollarHeight) };
        var flat = Math.Max(0, CollarMargin - CollarBevel);
        for (var k = 1; k <= topRings; k++)
            levels.Add((flat * k / topRings, CollarHeight));
        levels.Add((CollarMargin, Math.Min(CollarHeight, 0.005)));
        levels.Add((CollarMargin, -CollarSinkDepth));
        var o = new (double, double, double)[levels.Count][];
        for (var li = 0; li < levels.Count; li++)
        {
            var (g, lift) = levels[li];
            o[li] = new (double, double, double)[nd];
            for (var k = 0; k < nd; k++)
            {
                var (u, v) = PlateAt(fo, n, sub, k, g);
                var s = Skin(u, v);
                o[li][k] = Math.Abs(lift) < 1e-9 ? s : OnTop(h, face, s.Item1, s.Item2, s.Item3, lift);
            }
        }
        var inner = new (double, double, double)[n];
        for (var j = 0; j < n; j++)
            inner[j] = o[1][j * sub];
        // The plate's body for the cladding checks: its outline at its edge, along the line as far as it could reach.
        double[] bu = new double[nd], bv = new double[nd];
        for (var k = 0; k < nd; k++)
            (bu[k], bv[k]) = PlateAt(fo, n, sub, k, CollarMargin);
        double[] td = [t[0] * dir, t[1] * dir, t[2] * dir];
        return new PlateMesh(o, inner, new Foot(0, c, td, rs, upv, bu, bv, reach));
    }

    /// <summary>A point in under a base plate's top: inside its outline and lower off the skin than its height (or in the body).</summary>
    private bool UnderPlate(HullBody h, int face, Foot body, (double X, double Y, double Z) p)
    {
        if (!body.Holds(p.X, p.Y, p.Z))
            return false;
        if (h.Depth(p.X, p.Y, p.Z) > 0)
            return true;
        var s = OnTop(h, face, p.X, p.Y, p.Z, 0);
        return Math.Sqrt(Sq(p.X - s.Item1) + Sq(p.Y - s.Item2) + Sq(p.Z - s.Item3)) < CollarHeight - 0.002;
    }

    /// <summary>Draws a base plate's rings as a closed band: the inner wall, the top, the bevel and the outer wall (the page draws both
    /// sides of every face), the top a shade lighter than the walls.</summary>
    private static void DrawCollar(DesignMesh m, (double, double, double)[][] o, (byte, byte, byte) col)
    {
        var n = o[0].Length;
        var top = DesignMesh.Tone(col, 0.86);
        var wall = DesignMesh.Tone(col, 0.68);
        var last = o.Length - 2;
        for (var k = 0; k < o.Length - 1; k++)
        {
            var c = k == 0 || k == last ? wall : k == last - 1 ? DesignMesh.Tone(col, 0.78) : top;
            for (var j = 0; j < n; j++)
            {
                var j1 = (j + 1) % n;
                m.Quad(o[k][j], o[k + 1][j], o[k + 1][j1], o[k][j1], c, false);
            }
        }
    }

    /// <summary>
    /// A following buttress at one end: ring iEnd (the flared ring already slid onto the skin) laid onto the skin FollowLift off it, by the
    /// nearest skin point (its normal), and FollowRings rings put in between it and the strut's ring iStrut, each point's offset from the
    /// middle line growing from the strut's to the footprint's on an easing curve (a fillet), held FollowLift off the skin where it would dip in.
    /// </summary>
    private static (double, double, double)[] Lifted((double, double, double)[] top) => top.Select(p => p).ToArray();

    private static void FollowEnd(List<(double, double, double)[]> rings, List<double[]> centres, int iEnd, int iStrut, HullBody h, int face, double lift,
        (double, double, double)[]? given = null)
    {
        var nr = rings[iEnd].Length;
        var foot = given ?? new (double, double, double)[nr];
        if (given is null)
            for (var j = 0; j < nr; j++)
            {
                var (x, y, z) = rings[iEnd][j];
                foot[j] = OnTop(h, face, x, y, z, lift);
            }
        double[] cf = [0, 0, 0];
        foreach (var (x, y, z) in foot)
        {
            cf[0] += x / nr; cf[1] += y / nr; cf[2] += z / nr;
        }
        var (rsS, csS) = (rings[iStrut], centres[iStrut]);
        if (given is not null)
        {
            // A clean loft from the strut's ring down to the collar's top edge: each point on a quadratic curve that leaves the strut
            // along its line and lands on the edge, the rings spaced evenly along it (no pushing onto the skin, so no lumps).
            var nS = centres[iStrut];
            var nP = centres[iStrut + (iEnd == 0 ? 1 : -1)];
            double[] td = [nS[0] - nP[0], nS[1] - nP[1], nS[2] - nP[2]];
            Norm(td);
            var addC = new List<((double, double, double)[] R, double[] C)>();
            for (var k = 1; k <= FollowRings; k++)
            {
                var v = (double)k / (FollowRings + 1);
                double w0 = (1 - v) * (1 - v), w1 = 2 * v * (1 - v), w2 = v * v;
                var r = new (double, double, double)[nr];
                for (var j = 0; j < nr; j++)
                {
                    var s0 = rsS[j];
                    var f = foot[j];
                    var dd = Math.Max(0, (f.Item1 - s0.Item1) * td[0] + (f.Item2 - s0.Item2) * td[1] + (f.Item3 - s0.Item3) * td[2]);
                    double cx = s0.Item1 + td[0] * dd, cy = s0.Item2 + td[1] * dd, cz = s0.Item3 + td[2] * dd;
                    r[j] = (w0 * s0.Item1 + w1 * cx + w2 * f.Item1, w0 * s0.Item2 + w1 * cy + w2 * f.Item2, w0 * s0.Item3 + w1 * cz + w2 * f.Item3);
                }
                double[] c = [csS[0] + (cf[0] - csS[0]) * v, csS[1] + (cf[1] - csS[1]) * v, csS[2] + (cf[2] - csS[2]) * v];
                addC.Add((r, c));
            }
            rings[iEnd] = foot;
            centres[iEnd] = cf;
            if (iEnd == 0)
            {
                addC.Reverse();
                rings.InsertRange(1, addC.Select(a => a.R));
                centres.InsertRange(1, addC.Select(a => a.C));
            }
            else
            {
                rings.InsertRange(iEnd, addC.Select(a => a.R));
                centres.InsertRange(iEnd, addC.Select(a => a.C));
            }
            return;
        }
        var add = new List<((double, double, double)[] R, double[] C)>();
        for (var k = 1; k <= FollowRings; k++)
        {
            var v = (double)k / (FollowRings + 1); // 0 at the strut, 1 at the skin
            var e = v * v; // the fillet: the section holds near the strut and spreads at the base
            double[] c = [csS[0] + (cf[0] - csS[0]) * v, csS[1] + (cf[1] - csS[1]) * v, csS[2] + (cf[2] - csS[2]) * v];
            var r = new (double, double, double)[nr];
            for (var j = 0; j < nr; j++)
            {
                double ox = rsS[j].Item1 - csS[0], oy = rsS[j].Item2 - csS[1], oz = rsS[j].Item3 - csS[2];
                double fx = foot[j].Item1 - cf[0], fy = foot[j].Item2 - cf[1], fz = foot[j].Item3 - cf[2];
                double px = c[0] + ox + (fx - ox) * e, py = c[1] + oy + (fy - oy) * e, pz = c[2] + oz + (fz - oz) * e;
                r[j] = h.Depth(px, py, pz) > -lift ? OnTop(h, face, px, py, pz, lift) : (px, py, pz);
            }
            add.Add((r, c));
        }
        rings[iEnd] = foot;
        centres[iEnd] = cf;
        // Put in nearest the strut first: at the start (iEnd 0) the order runs skin..strut, at the end strut..skin.
        if (iEnd == 0)
        {
            add.Reverse();
            rings.InsertRange(1, add.Select(a => a.R));
            centres.InsertRange(1, add.Select(a => a.C));
        }
        else
        {
            rings.InsertRange(iEnd, add.Select(a => a.R));
            centres.InsertRange(iEnd, add.Select(a => a.C));
        }
    }

    /// <summary>The skin point nearest a world point on a body, lifted off it along its normal by lift (negative: in under it).</summary>
    private static (double, double, double) OnTop(HullBody h, int face, double x, double y, double z, double lift)
    {
        var copy = h.Copies == 2 && Math.Abs(x - h.CopyX(1)) < Math.Abs(x - h.CopyX(0)) ? 1 : 0;
        if (face != FaceSide)
            return OnCap(h, face, copy, x, y, lift);
        double lx = x - h.CopyX(copy), ly = y - h.Y, lz = Math.Clamp(z - h.Z, h.Front, h.Back);
        var s = h.OnSkin(lz, h.NearestT(lx, ly, lz), lx < -0.01 ? -1 : 1);
        return (h.CopyX(copy) + s.X + s.Nx * lift, h.Y + s.Y + s.Ny * lift, h.Z + s.Z);
    }

    private static (double X, double Y, double Z) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>The cap point nearest a world point (its x and y), lifted off the cap by lift (negative: in behind it).</summary>
    private static (double, double, double) OnCap(HullBody h, int face, int copy, double x, double y, double lift)
    {
        var (u, v) = CapUV(h, face, x - h.CopyX(copy), y - h.Y);
        var (cx, cy) = CapPoint(h, face, u, v);
        return (h.CopyX(copy) + cx, h.Y + cy, h.Z + CapZ(h, face) + (face == FaceFore ? -1 : 1) * lift);
    }

    /// <summary>A departure: the normal n tilted toward the other end (to, by tilt degrees) and along the hull (z; up on a cap, by fore degrees).</summary>
    private static double[] Leave(double[] n, double[] to, double tilt, double fore)
    {
        double[] r = [n[0], n[1], n[2]];
        if (Math.Abs(tilt) > 1e-3)
        {
            var k = to[0] * n[0] + to[1] * n[1] + to[2] * n[2];
            double[] t = [to[0] - n[0] * k, to[1] - n[1] * k, to[2] - n[2] * k];
            var l = Math.Sqrt(t[0] * t[0] + t[1] * t[1] + t[2] * t[2]);
            if (l > 1e-6)
            {
                var g = Math.Tan(tilt * Math.PI / 180) / l;
                for (var q = 0; q < 3; q++) r[q] += t[q] * g;
            }
        }
        if (Math.Abs(fore) > 1e-3)
        {
            double[] ax = Math.Abs(n[2]) > 0.9 ? [0, 1, 0] : [0, 0, 1];
            var k = ax[0] * n[0] + ax[1] * n[1] + ax[2] * n[2];
            double[] t = [ax[0] - n[0] * k, ax[1] - n[1] * k, ax[2] - n[2] * k];
            Norm(t);
            var g = Math.Tan(fore * Math.PI / 180);
            for (var q = 0; q < 3; q++) r[q] += t[q] * g;
        }
        Norm(r);
        return r;
    }

    private static (double, double, double) Nearest(HullBody h, int face, double x, double y, double z)
    {
        var copy = h.Copies == 2 && Math.Abs(x - h.CopyX(1)) < Math.Abs(x - h.CopyX(0)) ? 1 : 0;
        if (face != FaceSide)
            return OnCap(h, face, copy, x, y, -0.01);
        double lx = x - h.CopyX(copy), ly = y - h.Y, lz = Math.Clamp(z - h.Z, h.Front, h.Back);
        var s = h.OnSkin(lz, h.NearestT(lx, ly, lz), lx < -0.01 ? -1 : 1);
        // 1 cm in under the skin, so no seam shows.
        return (h.CopyX(copy) + s.X - s.Nx * 0.01, h.Y + s.Y - s.Ny * 0.01, h.Z + s.Z);
    }

    private static double Sq(double v) => v * v;
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    private static void Norm(double[] v)
    {
        var l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (l < 1e-9) { v[0] = 1; v[1] = 0; v[2] = 0; return; }
        v[0] /= l; v[1] /= l; v[2] /= l;
    }
}
