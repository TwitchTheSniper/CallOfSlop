using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Light bars (Karl, 2026-11-27: "adjustable light bar sockets to both interior and exterior"): a glowing strip in a dark channel, its length,
/// colour and brightness set per piece. Two kinds:
/// <list type="bullet">
/// <item><b>Light bar</b>, on the hull's skin where it is dropped. Turned along the hull (Turn 0 or 180) it follows the skin's curve along its
/// length; turned any other way it runs across the skin at that heading, following its curve and slope as a window beside it does. How it meets the hull's cladding is its <see cref="Piece.BarClad"/>
/// (Karl, 2026-11-27: "option for light bars to interact with cladding"): Cut (the plates are cut round its whole line and it lies on the
/// skin in the channel), Flush (cut tight, the bar raised on a housing so its face is level with the plates' tops) or Over (the plates run
/// on under it and it sits on a housing over them): <see cref="Cuts"/>, <see cref="Lift"/>.</item>
/// <item><b>Light bar (room)</b>, inside a room: dropped on the skin by a room (or linked to one, as a seat is), it goes on that room's wall
/// nearest where it was dropped, at <see cref="Piece.BarUp"/> over the floor, or on its ceiling or floor (<see cref="Piece.BarOn"/>). Turned
/// 90 degrees it stands upright on a wall, or runs across the room on a ceiling or floor. A room out to the hull has it follow the skin.</item>
/// </list>
/// Its strip is a lit run of its mesh (<see cref="DesignMesh.Glowing"/>): the designer and the page draw it unlit, the game's design shader
/// unlit with bloom, and in the game its part holds lamps along it (DesignParts.Lights.cs) in its colour at its brightness, always on.
/// All its geometry, JSON and page fields are here, so the rest of the designer only calls in.
/// </summary>
public static class DesignLightBars
{
    public const string Hull = "Light bar", Room = "Light bar (room)";
    public const double DefLen = 2, MinLen = 0.25, MaxLen = 24, DefBright = 1, MinBright = 0.05, DefUp = 0.3;
    public const string DefColour = "#3fd2ff"; // (the cyan strip of Karl's reference)
    /// <summary>Where a room's bar goes: the wall nearest where it was dropped, the ceiling or the floor.</summary>
    public static readonly string[] Mounts = ["Wall", "Ceiling", "Floor"];
    /// <summary>How a hull bar meets the hull's cladding (<see cref="Piece.BarClad"/>).</summary>
    public static readonly string[] CladModes = ["Cut", "Flush", "Over"];
    public const string DefClad = "Cut";
    /// <summary>The clearance a Flush bar's cut keeps when its gap is auto: the plates come up to its housing.</summary>
    private const double FlushGap = 0.01;
    // Its section at Size 1: the channel 8 cm across and 2.5 cm deep, the strip 70% of its width and 6 mm proud of it.
    private const double HalfW = 0.04, Depth = 0.025, StripRise = 0.006;
    private const string Channel = "#24272b";

    public static bool IsBar(string kind) => kind is Hull or Room;

    public static readonly PieceKind[] Kinds =
    [
        new(Hull, "Lights", "skin", null, "", "A glowing light bar on the hull: set its length, colour and brightness. Along the hull it follows the skin's curve; turn it for a bar across.", _ => { }),
        new(Room, "Lights", "skin", "seat", "any room", "A glowing light bar inside a room: drop it on the skin by a room (or link it to one) and it goes on that room's nearest wall, or its ceiling or floor. Set its length, height, colour and brightness.", _ => { }),
    ];

    /// <summary>The nominal footprint that gives a hull bar the cladding sliders (its real one is <see cref="FootprintOf"/>, by its length).</summary>
    public static (double Hx, double Hz, bool Round)? Footprint(string name) => name == Hull ? (HalfW + 0.03, DefLen / 2 + 0.03, false) : null;

    /// <summary>What keeps hull cladding clear round a hull bar, in its own frame before its size and hole scale (which the caller applies).</summary>
    public static (double Hx, double Hz, bool Round) FootprintOf(Piece p) => (HalfW + 0.03, (p.BarLen / 2 + 0.03) / Math.Max(0.05, p.Size * p.Hole), false);

    /// <summary>A new bar: its own colour (a floodlight's beam default is a warm white).</summary>
    public static void Init(Piece p)
    {
        if (IsBar(p.Kind))
            p.Beam = DefColour;
    }

    public static JsonObject Defaults() => new() { ["barLen"] = DefLen, ["bright"] = DefBright, ["barUp"] = DefUp, ["barOn"] = Mounts[0], ["beam"] = DefColour, ["barClad"] = DefClad };

    /// <summary>A bar's JSON fields (only a bar's: every other piece's JSON stays as it was, so its design's hash does).</summary>
    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (!IsBar(p.Kind))
            return o;
        o["barLen"] = ShipDesign.R(p.BarLen);
        o["bright"] = ShipDesign.R(p.Bright);
        o["beam"] = p.Beam;
        if (p.Kind == Room)
        {
            o["barOn"] = p.BarOn;
            o["barUp"] = ShipDesign.R(p.BarUp);
        }
        else
            o["barClad"] = p.BarClad;
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        if (!IsBar(p.Kind))
            return p;
        p.BarLen = Math.Clamp(ShipDesign.F(o["barLen"], DefLen), MinLen, MaxLen);
        p.Bright = Math.Clamp(ShipDesign.F(o["bright"], DefBright), MinBright, 1);
        p.BarUp = Math.Clamp(ShipDesign.F(o["barUp"], DefUp), 0, 40);
        p.BarOn = Array.IndexOf(Mounts, (string?)o["barOn"]) >= 0 ? (string)o["barOn"]! : Mounts[0];
        p.BarClad = Array.IndexOf(CladModes, (string?)o["barClad"]) >= 0 ? (string)o["barClad"]! : DefClad;
        if (o["beam"] is null)
            p.Beam = DefColour;
        return p;
    }

    /// <summary>The page's setPiece fields (its colour is the floodlights' "beam": DesignFittings.Set); an error text, or empty.</summary>
    public static string Set(Piece p, JsonObject o)
    {
        if (!IsBar(p.Kind))
            return "";
        if (o["barLen"] is not null) p.BarLen = Math.Clamp(Math.Round(ShipDesign.F(o["barLen"], DefLen) / 0.05) * 0.05, MinLen, MaxLen);
        if (o["bright"] is not null) p.Bright = Math.Clamp(Math.Round(ShipDesign.F(o["bright"], DefBright) / 0.05) * 0.05, MinBright, 1);
        if (o["barUp"] is not null) p.BarUp = Math.Clamp(Math.Round(ShipDesign.F(o["barUp"], DefUp) / 0.05) * 0.05, 0, 40);
        if ((string?)o["barOn"] is { } on)
        {
            if (Array.IndexOf(Mounts, on) < 0)
                return $"a light bar goes on one of {string.Join(", ", Mounts)}";
            p.BarOn = on;
        }
        if ((string?)o["barClad"] is { } bc)
        {
            if (Array.IndexOf(CladModes, bc) < 0)
                return $"a light bar meets the cladding one of these ways: {string.Join(", ", CladModes)}";
            p.BarClad = bc;
        }
        return "";
    }

    /// <summary>The page's bar fields: its length, brightness, colour, and a room bar's mount, height and the room it is in.</summary>
    public static string SceneJson(ShipDesign d, Piece p)
    {
        if (!IsBar(p.Kind))
            return "";
        var sb = new StringBuilder();
        sb.Append(",\"bar\":true,\"barLen\":").Append(ShipDesign.S(p.BarLen)).Append(",\"bright\":").Append(ShipDesign.S(p.Bright)).Append(",\"beam\":").Append(DesignScene.Q(p.Beam));
        if (p.Kind == Room)
        {
            sb.Append(",\"barOn\":").Append(DesignScene.Q(p.BarOn)).Append(",\"barUp\":").Append(ShipDesign.S(p.BarUp));
            var r = DesignSeats.RoomOf(d, p);
            sb.Append(",\"barRoom\":").Append(r is null ? "null" : DesignScene.Q(DesignScene.Name(r) + $" on deck {r.Deck + 1}"));
            sb.Append(",\"barUpMax\":").Append(ShipDesign.S(r is null ? 3 : d.DeckOf(r).Height));
        }
        else
            sb.Append(",\"barClad\":").Append(DesignScene.Q(p.BarClad)).Append(",\"barCladOn\":").Append(d.Hull(p.Hull) is { } h && d.CladSource(h).Clad != "Off" ? "true" : "false");
        return sb.ToString();
    }

    // ---- the hull's cladding

    /// <summary>
    /// How far a hull bar stands off the skin for its <see cref="Piece.BarClad"/>: 0 for Cut (or a body with no cladding); Flush, so its
    /// channel's top is 4 mm over the tallest plate; Over, so its channel sits 4 mm over the tallest plate (and a Heavy plate's box).
    /// </summary>
    public static double Lift(ShipDesign d, Piece p)
    {
        if (p.Kind != Hull || p.BarClad == "Cut" || d.Hull(p.Hull) is not { } h)
            return 0;
        var s = d.CladSource(h);
        if (s.Clad == "Off")
            return 0;
        var top = HullBody.CladRise(s.Clad) * s.CladDepth;
        return p.BarClad == "Flush" ? Math.Max(0, top - Depth * p.Size + 0.004) : top + (s.Clad == "Heavy" ? 0.04 * s.CladDepth : 0) + 0.004;
    }

    /// <summary>What a hull bar's mesh depends on beyond its own fields and the bodies' shapes: its body's cladding (for its lift).</summary>
    public static string Key(ShipDesign d, Piece p) =>
        p.Kind == Hull && p.BarClad != "Cut" && d.Hull(p.Hull) is { } h && d.CladSource(h) is var s ? $"|bc{s.Clad}/{ShipDesign.R(s.CladDepth)}" : "";

    /// <summary>
    /// The cuts a hull bar makes in its body's cladding (none for Over): one rectangle per run of its line, so a bar that follows the skin's
    /// curve is cleared along its whole length (one flat cut left plates over the ends of a long bar on a curved hull). Each is its place
    /// (X across the bar, Y out of the skin, Z along it), its half sizes, and the gap the plates keep (the piece's own, or auto: Cut's
    /// worked out from the plates' height, Flush's <see cref="FlushGap"/>).
    /// </summary>
    public static List<(DesignScene.Place W, double Hx, double Hz, double Gap)> Cuts(ShipDesign d, Piece p)
    {
        var o = new List<(DesignScene.Place, double, double, double)>();
        if (p.Kind != Hull || p.BarClad == "Over" || LineOf(d, p) is not { } l)
            return o;
        var gap = p.Gap >= 0 ? p.Gap : p.BarClad == "Flush" ? FlushGap : -1;
        var hw = HalfW * p.Size + 0.01;
        var cap = 0.012 * p.Size + 0.01;
        for (var i = 0; i < l.P.Count - 1; i++)
        {
            var (a, b) = (l.P[i], l.P[i + 1]);
            var len = Dist(a, b);
            if (len < 1e-4)
                continue;
            var z = Norm(Sub(b, a));
            var y = Norm(Add(l.N[i], l.N[i + 1], 1));
            y = Norm(Add(y, z, -Dot(y, z)));
            var x = Cross(y, z);
            // Each run's cut reaches a little past its ends, so the cuts of a curved bar overlap at its bends; the first and last past its end caps.
            double e0 = i == 0 ? cap : 0.01, e1 = i == l.P.Count - 2 ? cap : 0.01;
            var mid = Add(Lerp(a, b, 0.5), z, (e1 - e0) / 2);
            o.Add((new DesignScene.Place(mid, x, y, z), hw, len / 2 + (e0 + e1) / 2, gap));
        }
        return o;
    }

    // ---- where it lies

    /// <summary>Its line: points along it, each with the way out of the surface it lies on (the strip faces that way), in the ship's frame.</summary>
    public readonly record struct Line(List<double[]> P, List<double[]> N);

    /// <summary>A bar's line (null for any other piece, or a room bar with no room).</summary>
    public static Line? LineOf(ShipDesign d, Piece p)
    {
        if (!IsBar(p.Kind) || d.Hull(p.Hull) is not { } h)
            return null;
        return p.Kind == Hull ? HullLine(h, p) : RoomLine(d, h, p);
    }

    /// <summary>A room bar's place for the scene and the game (its middle, X along it, Y out of its surface); null for any other piece.</summary>
    public static DesignScene.Place? PlaceOf(ShipDesign d, Piece p)
    {
        if (p.Kind != Room || LineOf(d, p) is not { } l || l.P.Count < 2)
            return null;
        var k = l.P.Count / 2;
        var mid = l.P.Count % 2 == 1 ? l.P[k] : Lerp(l.P[k - 1], l.P[k], 0.5);
        var along = Norm(Sub(l.P[^1], l.P[0]));
        var n = Norm(l.N[k]);
        return new DesignScene.Place(mid, along, n, Cross(along, n));
    }

    private static Line HullLine(HullBody h, Piece p)
    {
        var w = DesignScene.Where(h, p);
        var len = p.BarLen;
        var line = new Line([], []);
        if (p.Surface != "skin")
        {
            // (fore or aft: a flat face, so a straight bar)
            foreach (var a in (double[])[-len / 2, len / 2])
            {
                line.P.Add(Add(Add(w.O, w.Z, a), w.Y, -p.Recess));
                line.N.Add(Norm(w.Y));
            }
            return line;
        }
        // On the skin it follows the skin at any turn (Karl, 2026-11-27: a bar turned across ran straight off the skin while a window beside
        // it followed the hull's slope): walked out from its middle both ways in short steps, each step along the bar's heading in the skin's
        // own frame there (its turn from the hull's length) and put back on the skin at the nearest point round the section.
        var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
        var half = Math.Clamp((int)Math.Ceiling(len), 1, 32); // (a run every half metre or so, as before)
        var step = len / 2 / half;
        var mid = h.OnSkin(p.Z, p.T, p.Side);
        var along = Math.Abs(Math.IEEERemainder(p.Turn, 180)) < 1; // (along the hull: it keeps its line round the section, as it always has)
        var back = new List<(double[] At, double[] N)>();
        var fwd = new List<(double[] At, double[] N)>();
        foreach (var dir in (int[])[-1, 1])
        {
            var into = dir < 0 ? back : fwd;
            var s = mid;
            for (var k = 0; k < half; k++)
            {
                var head = Heading(s.Nx, s.Ny, p.Turn);
                double x = s.X + head[0] * step * dir, y = s.Y + head[1] * step * dir;
                var z = Math.Clamp(s.Z + head[2] * step * dir, h.Front + 0.02, h.Back - 0.02);
                s = h.OnSkin(z, along ? p.T : h.NearestT(x, y, z), p.Side);
                into.Add(([cx + s.X, h.Y + s.Y, h.Z + s.Z], [s.Nx, s.Ny, 0]));
            }
        }
        back.Reverse();
        back.Add(([cx + mid.X, h.Y + mid.Y, h.Z + mid.Z], [mid.Nx, mid.Ny, 0]));
        back.AddRange(fwd);
        foreach (var (at, nn) in back)
        {
            line.P.Add(Add(at, nn, -p.Recess));
            line.N.Add(Norm(nn));
        }
        return line;
    }

    /// <summary>The way a skin piece's Z points, turned <paramref name="turn"/> degrees, where the skin's normal is (nx, ny, 0) (as <see cref="DesignScene.Where"/>).</summary>
    private static double[] Heading(double nx, double ny, double turn)
    {
        double[] y = [nx, ny, 0], z = [0, 0, -1];
        var x = Cross(y, z);
        var a = turn * Math.PI / 180;
        double c = Math.Cos(a), sn = Math.Sin(a);
        return [z[0] * c - x[0] * sn, z[1] * c - x[1] * sn, z[2] * c - x[2] * sn];
    }

    private static Line? RoomLine(ShipDesign d, HullBody h, Piece p)
    {
        if (DesignSeats.RoomOf(d, p) is not { } r)
            return null;
        var dk = d.DeckOf(r);
        var b = d.Box(r);
        int ax = 0, al = d.Stacked ? 1 : 2, up = d.Stacked ? 2 : 1;
        double[] lo = [b[0], b[2], b[4]], hi = [b[1], b[3], b[5]];
        var sg = d.Stacked ? -1.0 : 1.0;
        var floor = d.Stacked ? hi[up] : lo[up];
        var hgt = dk.Height;
        var sd = DesignScene.ShellDepth(r);
        var stand = sd + (r.Interior != "Off" ? 0.045 : 0.004); // (off its wall's shell, and proud of the interior cladding's panels)
        var (l0, l1) = DesignScene.Span(d, r);
        var hull = r.ToHull;
        var at = DesignScene.Where(h, p).O;
        double fa = at[ax], fl = at[al];
        var upright = Math.Abs(Math.IEEERemainder(p.Turn, 180)) > 45; // (turned: upright on a wall, across on a ceiling or floor)
        double[] W(double a, double l, double u) { var v = new double[3]; v[ax] = a; v[al] = l; v[up] = floor + sg * u; return v; }
        double[] Dir(int axis, double sign) { var v = new double[3]; v[axis] = axis == up ? sign * sg : sign; return v; }
        // The room's side faces at a place along it and a height (out to the skin for a room out to the hull).
        (double L, double R) Sides(double l, double u) => hull ? DesignScene.Edges(d, r, l, u) : (lo[ax], hi[ax]);
        const double margin = 0.1;
        var line = new Line([], []);
        double Fit(double want, double a, double c, double len) => c - a <= len ? (a + c) / 2 : Math.Clamp(want, a + len / 2, c - len / 2);

        if (p.BarOn is "Ceiling" or "Floor")
        {
            var ceiling = p.BarOn == "Ceiling";
            var u = ceiling ? hgt - stand : DesignScene.FloorTop(r) + 0.004;
            double[] nn = Dir(up, ceiling ? -1 : 1);
            if (!upright)
            {
                // Along the room, across at the drop point.
                var len = Math.Min(p.BarLen, l1 - l0 - 2 * (sd + margin));
                var lc = Fit(fl, l0 + sd + margin, l1 - sd - margin, len);
                var (sl, sr) = Sides(lc, u);
                var ac = Math.Clamp(fa, sl + sd + margin, Math.Max(sl + sd + margin, sr - sd - margin));
                line.P.Add(W(ac, lc - len / 2, u)); line.P.Add(W(ac, lc + len / 2, u));
            }
            else
            {
                var lc = Math.Clamp(fl, l0 + sd + margin, Math.Max(l0 + sd + margin, l1 - sd - margin));
                var (sl, sr) = Sides(lc, u);
                var len = Math.Min(p.BarLen, sr - sl - 2 * (sd + margin));
                var ac = Fit(fa, sl + sd + margin, sr - sd - margin, len);
                line.P.Add(W(ac - len / 2, lc, u)); line.P.Add(W(ac + len / 2, lc, u));
            }
            line.N.Add(nn); line.N.Add(nn);
            return line.P.Count == 2 && Dist(line.P[0], line.P[1]) > 0.05 ? line : null;
        }

        // On the wall nearest the drop point (by the room's plan).
        var walls = new[] { Math.Abs(fa - lo[ax]), Math.Abs(hi[ax] - fa), Math.Abs(fl - l0), Math.Abs(l1 - fl) };
        var wall = Array.IndexOf(walls, walls.Min());
        var side = wall < 2; // (a side wall: across x; else an end wall)
        var lowU = DesignScene.FloorTop(r) + 0.02;
        var highU = hgt - sd - 0.02;
        if (highU - lowU < 0.1)
            return null;
        // A point on the chosen wall at (along it, up), with the way into the room: a side wall out to the hull leans as the skin does.
        (double[] P, double[] N) OnWall(double t, double u)
        {
            if (side)
            {
                var left = wall == 0;
                double E(double uu) { var e = Sides(t, Math.Clamp(uu, 0, hgt)); return left ? e.L : e.R; }
                var slope = hull ? (E(u + 0.1) - E(u - 0.1)) / 0.2 : 0;
                var inward = left ? 1.0 : -1.0;
                // The face's normal in (across, up): perpendicular to its slope, pointing into the room.
                var nl = Math.Sqrt(1 + slope * slope);
                var na = inward / nl;
                var nu = -inward * slope / nl;
                var a = E(u) + inward * (hull ? 0 : sd) + na * (stand - (hull ? 0 : sd));
                var n = new double[3];
                n[ax] = na; n[up] = nu * sg;
                return (W(a, t, u + nu * (stand - (hull ? 0 : sd))), n);
            }
            var front = wall == 2;
            var lw = front ? l0 + stand : l1 - stand;
            return (W(t, lw, u), Dir(al, front ? 1 : -1));
        }
        if (!upright)
        {
            var u = Math.Clamp(p.BarUp, lowU, highU);
            double t0, t1;
            if (side)
            {
                (t0, t1) = (l0 + sd + margin, l1 - sd - margin);
            }
            else
            {
                var (sl, sr) = Sides(wall == 2 ? l0 : l1, u);
                (t0, t1) = (sl + sd + margin, sr - sd - margin);
            }
            if (t1 - t0 < 0.1)
                return null;
            var len = Math.Min(p.BarLen, t1 - t0);
            var tc = Fit(side ? fl : fa, t0, t1, len);
            var n = side && hull ? Math.Clamp((int)Math.Ceiling(len / 0.5), 1, 64) : 1;
            for (var k = 0; k <= n; k++)
            {
                var (pt, nn) = OnWall(tc - len / 2 + len * k / n, u);
                line.P.Add(pt); line.N.Add(nn);
            }
        }
        else
        {
            var len = Math.Min(p.BarLen, highU - lowU);
            var uc = Fit(p.BarUp, lowU, highU, len);
            double t;
            if (side)
                t = Math.Clamp(fl, l0 + sd + margin, Math.Max(l0 + sd + margin, l1 - sd - margin));
            else
            {
                var (sl, sr) = Sides(wall == 2 ? l0 : l1, uc);
                t = Math.Clamp(fa, sl + sd + margin, Math.Max(sl + sd + margin, sr - sd - margin));
            }
            var n = side && hull ? Math.Clamp((int)Math.Ceiling(len / 0.5), 1, 64) : 1;
            for (var k = 0; k <= n; k++)
            {
                var (pt, nn) = OnWall(t, uc - len / 2 + len * k / n);
                line.P.Add(pt); line.N.Add(nn);
            }
        }
        return line;
    }

    // ---- the drawing

    /// <summary>The strip's colour at the bar's brightness.</summary>
    public static (byte R, byte G, byte B) StripColour(Piece p) => DesignMesh.Tone(DesignMesh.Hex(p.Beam), 0.3 + 0.7 * p.Bright);

    /// <summary>Draws a bar along its line: the channel, then its strip as a lit run; false for any other piece (or a room bar with no room).</summary>
    public static bool Draw(ShipDesign d, Piece p, DesignMesh m)
    {
        if (!IsBar(p.Kind))
            return false;
        if (LineOf(d, p) is not { } l)
            return true; // (no room to stand in: nothing drawn; the checks say why)
        var s = p.Size;
        double hw = HalfW * s, dp = Depth * s, rise = StripRise * s, cap = 0.012 * s, lift = Lift(d, p);
        var c = StripColour(p);
        var strip = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
        var last = l.P.Count - 2;
        for (var i = 0; i <= last; i++)
        {
            var (a, b) = (l.P[i], l.P[i + 1]);
            var len = Dist(a, b);
            if (len < 1e-4)
                continue;
            var z = Norm(Sub(b, a));
            var y = Norm(Add(l.N[i], l.N[i + 1], 1));
            y = Norm(Add(y, z, -Dot(y, z))); // (square to the run)
            var x = Cross(y, z);
            double e0 = i == 0 ? cap : 0, e1 = i == last ? cap : 0;
            m.Frame(a[0], a[1], a[2], x, y, z);
            // (Flush or Over the cladding: the channel on a housing down into the skin, its face level with the plates' tops or over them)
            m.Box(-hw, lift > 0 ? -0.01 : 0, -e0, hw, lift + dp, len + e1, Channel);
            m.Glowing(() => m.Box(-hw * 0.7, lift + dp - 0.004 * s, 0, hw * 0.7, lift + dp + rise, len, strip));
        }
        return true;
    }

    /// <summary>
    /// The game's lamps along a bar (the ship's frame) a little off its strip, about one every <paramref name="spacing"/> metres, at most
    /// <paramref name="max"/>: each its middle, half its share of the bar along it (the game lights it as a line) and the way the strip faces.
    /// </summary>
    public static List<(double[] At, double[] Half, double[] Face)> Lamps(ShipDesign d, Piece p, double spacing, int max)
    {
        var o = new List<(double[] At, double[] Half, double[] Face)>();
        if (LineOf(d, p) is not { } l || l.P.Count < 2)
            return o;
        var lift = Lift(d, p);
        var cum = new double[l.P.Count];
        for (var i = 1; i < l.P.Count; i++)
            cum[i] = cum[i - 1] + Dist(l.P[i - 1], l.P[i]);
        var total = cum[^1];
        var n = Math.Clamp((int)Math.Round(total / spacing), 1, max);
        for (var k = 0; k < n; k++)
        {
            var t = total * (k + 0.5) / n;
            var i = 1;
            while (i < l.P.Count - 1 && cum[i] < t) i++;
            var f = cum[i] - cum[i - 1] < 1e-9 ? 0 : (t - cum[i - 1]) / (cum[i] - cum[i - 1]);
            var at = Lerp(l.P[i - 1], l.P[i], f);
            var nn = Norm(Lerp(l.N[i - 1], l.N[i], f));
            var along = Norm(Sub(l.P[i], l.P[i - 1]));
            // (in front of the strip, so the bar's own channel never shades it; it lights only the side it faces, so it can sit close)
            o.Add((Add(at, nn, lift + (Depth + StripRise) * p.Size + 0.04), Add([0, 0, 0], along, total / n * 0.5), nn));
        }
        return o;
    }

    private static double[] Add(double[] a, double[] b, double k) => [a[0] + b[0] * k, a[1] + b[1] * k, a[2] + b[2] * k];
    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    private static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double Dist(double[] a, double[] b) => Math.Sqrt(Dot(Sub(a, b), Sub(a, b)));
    private static double[] Norm(double[] v) { var l = Math.Sqrt(Dot(v, v)); return l < 1e-9 ? [0, 1, 0] : [v[0] / l, v[1] / l, v[2] / l]; }
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}
