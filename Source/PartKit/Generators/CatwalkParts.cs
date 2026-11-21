using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Steel catwalks in the Roamer Stair's style (Sean, 2026-11-21: "using Carl's staircase design, make a series of catwalks in the
/// same style"; Karl's stair is RoamerParts.OpenStair): the stair's grey steel slats with a dark angle under each one's front,
/// hazard-striped nosings (yellow, black slashes) where a walk ends or opens onto the next piece, slim dirty-steel channels along
/// the railed sides standing 15 mm proud of the deck, and square-tube hand rails 0.9 m up on posts, here with a knee rail too.
/// Open underneath, as the stair is. Constructs/Parts/Catwalks.
///
/// Every piece shares the stair's frame: 1.68 m across over its channels (x -0.09 to 1.59, the middle at x 0.75), the deck's top
/// at y 0.125 and the channels' undersides 0.15 m under it (slim, as the stair's stringers). An open end's socket is at the middle, 75 mm under the deck's top, which is
/// where the Roamer Stair's "head" socket is, so a stair's head meets any open end level. A Catwalk Leg (2.85 m) under a channel
/// stands the deck 3 m over the floor its foot is on: a storey, the stair's climb.
///
/// The pieces: straight runs (1.5, 3 and 6 m), and squares as wide as a run is: a Corner, a Tee, a Cross and an End (railed on three
/// sides). The squares' open sides take a run's end.
/// </summary>
public sealed class CatwalkParts : IPartGenerator
{
    public string Name => "catwalk_parts";

    private const string Out = "Constructs/Parts/Catwalks";
    private const string Group = "Building";
    private const string Section = "Stairs and access";

    // The Roamer Stair's colours and finishes.
    private const string SlatC = "#7d848c", ChannelC = "#7a8088", RailC = "#a4aab0", Yellow = "#d8b62a", Black = "#1e1e1c";

    private const double Mid = 0.75; // the walk's middle line (x), as the stair's
    private const double Half = 0.84; // half the width over the channels
    private const double X0 = Mid - Half, X1 = Mid + Half; // -0.09, 1.59
    private const double Top = 0.125; // the deck's top
    private const double Under = Top - 0.15; // the channels' undersides: slim, as the stair's stringers
    private const double ChanW = 0.06; // a channel's width, its top 15 mm over the deck
    private const double RailIn = 0.81; // a rail's line from the middle (x -0.06 and 1.56, as the stair's)
    private const double RailH = 0.9, KneeH = 0.45, Tube = 0.022, PostR = 0.016;
    private const double SockY = Top - 0.075; // an open end's socket: the stair head's
    private const double Z0 = -0.125; // where every piece starts along z
    private const double Square = 2 * Half; // a junction's side

    /// <summary>A piece's sides: which are railed (the rest are open, taking a run's end).</summary>
    [Flags]
    private enum Side { None = 0, NegX = 1, PosX = 2, NegZ = 4, PosZ = 8 }

    // ---- boxes (in the piece's own metres)

    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish) =>
        p.Add(Box([Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)], color, shape: Kw(("finish", finish))));

    private static void Look(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish) =>
        p.Add(Box([Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)], color, detail: true, shape: Kw(("finish", finish))));

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1) =>
        p.Add(Box([Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)], hidden: true));

    /// <summary>A convex solid between two outlines (drawn only), as the stair's slashes.</summary>
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish)
    {
        var both = a.Concat(b).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false), ("finish", finish));
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    // ---- the deck

    /// <summary>The slats across z from z0 to z1 (x from xa to xb), each with a dark angle under its front, as the stair's treads;
    /// and the hidden slab a person walks on, the whole piece's floor.</summary>
    private static void Deck(Part p, double xa, double xb, double z0, double z1)
    {
        Hidden(p, X0, Top - 0.05, z0, X1, Top, z1);
        var n = Math.Max(1, (int)Math.Round((z1 - z0) / 0.3));
        var run = (z1 - z0) / n;
        for (var k = 0; k < n; k++)
        {
            double a = z0 + run * k + 0.012, b = a + run - 0.03;
            Look(p, xa, Top - 0.045, a, xb, Top - 0.002, b, SlatC, "steel");
            Look(p, xa + 0.035, Top - 0.085, a + 0.004, xb - 0.035, Top - 0.045, a + 0.045, ChannelC, "dirty steel");
        }
    }

    /// <summary>A nosing along an open side: a yellow strip, black slashes across it (the stair's). Along x at z (the strip from z
    /// s0 to s1), or along z at x.</summary>
    private static void Nosing(Part p, bool alongX, double from, double to, double s0, double s1)
    {
        double[] At(double u, double v, double y) => alongX ? [u, y, v] : [v, y, u];
        if (alongX)
            Look(p, from, Top - 0.008, s0, to, Top + 0.008, s1, Yellow, "hazard paint");
        else
            Look(p, s0, Top - 0.008, from, s1, Top + 0.008, to, Yellow, "hazard paint");
        for (var u = from + 0.02; u + 0.1 <= to - 0.01; u += 0.1)
        {
            double[][] Slash(double y) => [At(u, s0 + 0.004, y), At(u + 0.04, s0 + 0.004, y), At(u + 0.09, s1 - 0.004, y), At(u + 0.05, s1 - 0.004, y)];
            Prism(p, Slash(Top + 0.01), Slash(Top + 0.015), Black, "hazard paint");
        }
    }

    // ---- the railed sides

    /// <summary>A railed side along z at x side (-1 or +1) from z0 to z1: the channel under it (solid, its underside the piece's), a
    /// hand rail and a knee rail on posts, and a hidden guard so nobody walks off through them.</summary>
    private static void RailZ(Part p, int side, double z0, double z1)
    {
        var cx = Mid + side * (Half - ChanW / 2);
        Solid(p, cx - ChanW / 2, Under, z0, cx + ChanW / 2, Top + 0.015, z1, ChannelC, "dirty steel");
        var rx = Mid + side * RailIn;
        Rails(p, z0, z1, (u, y, w, h) => (rx - w, y - h, u.A, rx + w, y + h, u.B));
        Hidden(p, rx - 0.02, Top + 0.02, z0, rx + 0.02, Top + RailH + 0.05, z1);
    }

    /// <summary>A railed side along x at z side (-1: the piece's start, +1: its far side of a square) from x0 to x1.</summary>
    private static void RailX(Part p, int side, double x0, double x1, double zMid)
    {
        var cz = zMid + side * (Half - ChanW / 2);
        Solid(p, x0, Under, cz - ChanW / 2, x1, Top + 0.015, cz + ChanW / 2, ChannelC, "dirty steel");
        var rz = zMid + side * RailIn;
        Rails(p, x0, x1, (u, y, w, h) => (u.A, y - h, rz - w, u.B, y + h, rz + w));
        Hidden(p, x0, Top + 0.02, rz - 0.02, x1, Top + RailH + 0.05, rz + 0.02);
    }

    /// <summary>The rails and posts along one side from a to b. box(span, height, half width, half height) gives a box's corners.</summary>
    private static void Rails(Part p, double a, double b, Func<(double A, double B), double, double, double, (double, double, double, double, double, double)> box)
    {
        void Put((double, double, double, double, double, double) q) => Look(p, q.Item1, q.Item2, q.Item3, q.Item4, q.Item5, q.Item6, RailC, "steel");
        double ra = a + 0.003, rb = b - 0.003; // (the rail stops just short of the piece's end: the next one's rail meets it)
        Put(box((ra, rb), Top + RailH, Tube, Tube));
        Put(box((ra, rb), Top + KneeH, Tube * 0.8, Tube * 0.8));
        // Posts at both ends, a hand in from them, and between them no more than 1.5 m apart (the stair's spacing).
        double pa = a + 0.06, pb = b - 0.06;
        var gaps = Math.Max(1, (int)Math.Ceiling((pb - pa) / 1.5 - 1e-6));
        for (var k = 0; k <= gaps; k++)
        {
            var u = pa + (pb - pa) * k / gaps;
            var h = (RailH - Tube - 0.015 - 0.005) / 2;
            Put(box((u - PostR, u + PostR), Top + 0.015 + 0.003 + h, PostR, h));
        }
    }

    /// <summary>An open side's under beam: a channel below the deck (its top under the slats), so the deck's edge is carried.</summary>
    private static void BeamX(Part p, double x0, double x1, double z)
    {
        Solid(p, x0, Under, z - ChanW / 2, x1, Top - 0.05, z + ChanW / 2, ChannelC, "dirty steel");
    }

    private static void BeamZ(Part p, double z0, double z1, double x)
    {
        Solid(p, x - ChanW / 2, Under, z0, x + ChanW / 2, Top - 0.05, z1, ChannelC, "dirty steel");
    }

    /// <summary>Leg sockets under a channel along z at x (side -1 or +1), near each end and between, every 1.5 m or less.</summary>
    private static void LegSocksZ(Part p, string tag, int side, double z0, double z1)
    {
        var x = Mid + side * (Half - ChanW / 2);
        var n = Math.Max(1, (int)Math.Ceiling((z1 - z0 - 0.5) / 1.5 - 1e-6));
        for (var k = 0; k <= n; k++)
        {
            var z = Cm(z0 + 0.25 + (z1 - z0 - 0.5) * k / n);
            p.Sock($"leg{tag}{k}", [x, Under, z], "-y");
        }
    }

    private static double Cm(double v) => Math.Round(v * 100) / 100;

    // ---- the pieces

    /// <summary>A straight run of length l (metres): open at both ends, railed both sides.</summary>
    private static Part Straight(double l)
    {
        var p = new Part($"Steel Catwalk {Fmt(l)} m", Small, Section, $"An open steel catwalk {Fmt(l)} m long, railed both sides, striped where it meets the next piece. Ends take a Roamer Stair's head.", role: "frame", tool: "welder", material: "steel", color: SlatC, group: Group);
        double z0 = Z0, z1 = Z0 + l;
        Deck(p, X0 + 0.04, X1 - 0.04, z0, z1);
        RailZ(p, -1, z0, z1);
        RailZ(p, 1, z0, z1);
        Nosing(p, true, X0 + ChanW + 0.005, X1 - ChanW - 0.005, z0 + 0.012, z0 + 0.092);
        Nosing(p, true, X0 + ChanW + 0.005, X1 - ChanW - 0.005, z1 - 0.092, z1 - 0.012);
        p.Sock("-z", [Mid, SockY, z0], "-z");
        p.Sock("+z", [Mid, SockY, z1], "+z");
        LegSocksZ(p, "L", -1, z0, z1);
        LegSocksZ(p, "R", 1, z0, z1);
        p.Set(("walk", "floor"), ("collider", "boxes"), ("centreSockets", false));
        return p.Stats(Py.Round(70 * l, 1), 220 * l, 20, 3.0);
    }

    /// <summary>A square as wide as a run, its railed sides given; every other side is open and takes a run's end.</summary>
    private static Part Junction(string name, string desc, Side railed)
    {
        var p = new Part($"Steel Catwalk {name}", Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: SlatC, group: Group);
        double z0 = Z0, z1 = Z0 + Square, zMid = Z0 + Half;
        Deck(p, X0 + 0.04, X1 - 0.04, z0, z1);
        // The railed sides; the corners where two meet share one post line (the z side's rail runs the whole side, the x side's
        // stops at the channel).
        double xa = railed.HasFlag(Side.NegX) ? X0 + ChanW + 0.003 : X0, xb = railed.HasFlag(Side.PosX) ? X1 - ChanW - 0.003 : X1;
        if (railed.HasFlag(Side.NegX))
            RailZ(p, -1, z0, z1);
        else
            BeamZ(p, z0 + 0.004, z1 - 0.004, X0 + ChanW / 2 + 0.004);
        if (railed.HasFlag(Side.PosX))
            RailZ(p, 1, z0, z1);
        else
            BeamZ(p, z0 + 0.004, z1 - 0.004, X1 - ChanW / 2 - 0.004);
        if (railed.HasFlag(Side.NegZ))
            RailX(p, -1, xa, xb, zMid);
        else
            BeamX(p, xa + 0.004, xb - 0.004, z0 + ChanW / 2 + 0.004);
        if (railed.HasFlag(Side.PosZ))
            RailX(p, 1, xa, xb, zMid);
        else
            BeamX(p, xa + 0.004, xb - 0.004, z1 - ChanW / 2 - 0.004);
        // Nosings on the open sides; sockets there.
        const double In = ChanW + 0.005;
        if (!railed.HasFlag(Side.NegZ))
        {
            Nosing(p, true, X0 + In, X1 - In, z0 + 0.012, z0 + 0.092);
            p.Sock("-z", [Mid, SockY, z0], "-z");
        }
        if (!railed.HasFlag(Side.PosZ))
        {
            Nosing(p, true, X0 + In, X1 - In, z1 - 0.092, z1 - 0.012);
            p.Sock("+z", [Mid, SockY, z1], "+z");
        }
        if (!railed.HasFlag(Side.NegX))
        {
            Nosing(p, false, z0 + In + 0.1, z1 - In - 0.1, X0 + 0.012, X0 + 0.092);
            p.Sock("-x", [X0, SockY, Cm(zMid)], "-x");
        }
        if (!railed.HasFlag(Side.PosX))
        {
            Nosing(p, false, z0 + In + 0.1, z1 - In - 0.1, X1 - 0.092, X1 - 0.012);
            p.Sock("+x", [X1, SockY, Cm(zMid)], "+x");
        }
        // Legs under the corners (under a channel or a beam, which reach the piece's underside).
        foreach (var (x, tag) in new[] { (X0 + ChanW / 2 + 0.004, "L"), (X1 - ChanW / 2 - 0.004, "R") })
        {
            var cx = railed.HasFlag(tag == "L" ? Side.NegX : Side.PosX) ? Mid + (tag == "L" ? -1 : 1) * (Half - ChanW / 2) : x;
            p.Sock($"leg{tag}0", [Cm(cx), Under, Cm(z0 + 0.25)], "-y");
            p.Sock($"leg{tag}1", [Cm(cx), Under, Cm(z1 - 0.25)], "-y");
        }
        p.Set(("walk", "floor"), ("collider", "boxes"), ("centreSockets", false));
        return p.Stats(Py.Round(70 * Square * 1.2, 1), 220 * Square, 20, 3.0);
    }

    /// <summary>The leg: a square post on a foot plate, braced, the deck 3 m over its foot (a storey: a Roamer Stair's head).</summary>
    private static Part Leg()
    {
        const double h = 3.0 - (Top - Under); // (the channels' undersides over the floor: the deck's top 3 m up)
        var p = new Part("Steel Catwalk Leg", Small, Section, "A steel post for a catwalk's channel: stands its deck a storey (3 m) over the floor, level with a Roamer Stair's head.", role: "frame", tool: "welder", material: "steel", color: ChannelC, group: Group);
        const double foot = -0.125, top = foot + h;
        Solid(p, -0.1, foot, -0.1, 0.1, foot + 0.02, 0.1, ChannelC, "dirty steel"); // the foot plate
        Solid(p, -0.03, foot + 0.02, -0.03, 0.03, top, 0.03, ChannelC, "dirty steel"); // the post (as wide as a channel)
        Look(p, -0.045, top - 0.022, -0.045, 0.045, top - 0.004, 0.045, ChannelC, "dirty steel"); // the cap plate's lip, just under the channel
        // Four bolts on the foot, and a gusset either side of the post.
        foreach (var (x, z) in new[] { (-0.07, -0.07), (0.07, -0.07), (-0.07, 0.07), (0.07, 0.07) })
            Look(p, x - 0.012, foot + 0.022, z - 0.012, x + 0.012, foot + 0.034, z + 0.012, RailC, "steel");
        foreach (var s in new[] { -1, 1 })
        {
            double[][] G(double x) => [[x, foot + 0.022, s * 0.033], [x, foot + 0.022, s * 0.095], [x, foot + 0.2, s * 0.033]];
            Prism(p, G(-0.006), G(0.006), ChannelC, "dirty steel");
        }
        // A hazard band at knee height, so it is seen on a busy floor.
        Look(p, -0.034, foot + 0.4, -0.034, 0.034, foot + 0.55, 0.034, Yellow, "hazard paint");
        p.Sock("-y", [0, foot, 0], "-y");
        p.Sock("+y", [0, top, 0], "+y");
        p.Set(("centreSockets", false));
        return p.Stats(45, 260, 20, 3.0);
    }

    public void Make(PartOutput o) => WriteParts(o, Out, new (string, Part)[]
    {
        ("", Straight(1.5)),
        ("", Straight(3)),
        ("", Straight(6)),
        ("", Junction("Corner", "A catwalk corner: railed on two sides, a run's end joins each of the other two.", Side.NegX | Side.PosZ)),
        ("", Junction("Tee", "A catwalk tee: railed on one side, a run's end joins each of the other three.", Side.PosZ)),
        ("", Junction("Cross", "A catwalk crossing: open on all four sides.", Side.None)),
        ("", Junction("End", "A catwalk's dead end, railed on three sides. Or a lookout.", Side.NegX | Side.PosX | Side.PosZ)),
        ("", Leg()),
    });
}
