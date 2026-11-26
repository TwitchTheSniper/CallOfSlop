using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Hangars (Karl, 2026-11-26): a hangar bay (S, M, L, sized from the game's Hangar Bay parts in Constructs/Parts/Systems/Launch) is an
/// internal volume behind a hangar door cut through the skin, the door from 5x3 m up to 48x32 m, sliding on a piston or hinged; inside
/// it, a docking arm and a docking cradle, each data-linked to its bay and the bay's door; and the lift platform and ramp deck, sized
/// sockets that cut the skin (a lift rises through the belly, a ramp deck hinges down).
/// All the pieces' own geometry, checks, JSON and page fields are here, so the rest of the designer only calls in.
/// </summary>
public static class DesignHangar
{
    /// <summary>A bay's volume (across, tall, deep) and its door's default, from the parts: the Hangar Bay's dock side 20 m (craft to
    /// 6 t, door Medium 5x3), the Medium's 40 m (30 t, door Large 20x12), the Large's 96 m (300 t, door Capital 48x32).</summary>
    public sealed record BaySize(string Name, double W, double H, double D, double DoorW, double DoorH, string Part);

    public static readonly BaySize[] Bays =
    [
        new("Hangar bay S", 20, 8, 20, 5, 3, "Hangar Bay"),
        new("Hangar bay M", 40, 16, 40, 20, 12, "Hangar Bay Medium"),
        new("Hangar bay L", 96, 38, 96, 48, 32, "Hangar Bay Large"),
    ];

    public const double DoorMinW = 5, DoorMaxW = 48, DoorMinH = 3, DoorMaxH = 32;
    public const double DeckMin = 2, DeckMax = 48, TravelMin = 0.5, TravelMax = 30;
    public static readonly string[] DoorTypes = ["Sliding", "Hinged"];

    public static BaySize? Bay(string kind) => Array.Find(Bays, b => b.Name == kind);
    public static bool IsBay(string kind) => Bay(kind) is not null;
    public static bool IsFitting(string kind) => kind is "Docking arm" or "Docking cradle";
    public static bool IsDeck(string kind) => kind is "Lift platform" or "Ramp deck";

    /// <summary>The kinds, appended to PieceKinds.All. Their meshes are drawn by <see cref="Draw"/> from the piece's own sizes.</summary>
    public static readonly PieceKind[] Kinds =
    [
        new("Hangar bay S", "Hangar", "skin", "hangar", "nothing (it is its own room)", "A 20 m hangar bay for craft up to 6 t (the Hangar Bay), behind a hangar door cut through the skin.", _ => { }),
        new("Hangar bay M", "Hangar", "skin", "hangar", "nothing (it is its own room)", "A 40 m hangar bay for craft up to 30 t (the Hangar Bay Medium), behind a hangar door.", _ => { }),
        new("Hangar bay L", "Hangar", "skin", "hangar", "nothing (it is its own room)", "A 96 m hangar bay for craft up to 300 t (the Hangar Bay Large), behind a hangar door.", _ => { }),
        new("Docking arm", "Hangar", "skin", null, "", "A docking arm inside a hangar bay, linked to the bay and its door: it reaches out and holds a craft.", _ => { }),
        new("Docking cradle", "Hangar", "skin", null, "", "A docking point inside a hangar bay: a cradle a craft settles in, linked to the bay and its door.", _ => { }),
        new("Lift platform", "Hangar", "skin", "lift", "any room", "A lift platform that rises through the belly: width, length and travel are set on it. It cuts the skin.", _ => { }),
        new("Ramp deck", "Hangar", "skin", "rampdeck", "any room", "A ramp deck hinged at its aft edge, shown lowered by its travel. It cuts the skin.", _ => { }),
    ];

    /// <summary>A new piece's own sizes (and a fitting's link to the nearest bay).</summary>
    public static void Init(ShipDesign d, Piece p)
    {
        if (Bay(p.Kind) is { } b)
            (p.BayDoorW, p.BayDoorH) = (b.DoorW, b.DoorH);
        else if (p.Kind == "Lift platform")
            (p.W, p.L, p.Travel) = (5, 5, 3);
        else if (p.Kind == "Ramp deck")
            (p.W, p.L, p.Travel) = (5, 8, 2.5);
        else if (IsFitting(p.Kind) && d.Hull(p.Hull) is { } h)
        {
            var at = DesignScene.Where(h, p).O;
            p.Link = d.Pieces.Where(q => IsBay(q.Kind) && d.Hull(q.Hull) is not null)
                .OrderBy(q => Dist(at, DesignScene.Where(d.Hull(q.Hull)!, q).O)).FirstOrDefault()?.Id ?? 0;
        }
    }

    /// <summary>The defaults the page's reset buttons use, per kind.</summary>
    public static JsonObject Defaults()
    {
        var o = new JsonObject();
        foreach (var k in Kinds)
        {
            var p = new Piece { Kind = k.Name };
            Init(new ShipDesign(), p);
            o[k.Name] = new JsonObject { ["bayDoorW"] = p.BayDoorW, ["bayDoorH"] = p.BayDoorH, ["bayDoor"] = p.BayDoorType, ["w"] = p.W, ["l"] = p.L, ["travel"] = p.Travel, ["along"] = p.Along, ["across"] = p.Across };
        }
        return o;
    }

    // ---- JSON

    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (IsBay(p.Kind))
        {
            o["bayDoorW"] = ShipDesign.R(p.BayDoorW); o["bayDoorH"] = ShipDesign.R(p.BayDoorH); o["bayDoor"] = p.BayDoorType;
        }
        else if (IsDeck(p.Kind))
        {
            o["w"] = ShipDesign.R(p.W); o["l"] = ShipDesign.R(p.L); o["travel"] = ShipDesign.R(p.Travel);
        }
        else if (IsFitting(p.Kind))
        {
            o["link"] = p.Link; o["along"] = ShipDesign.R(p.Along); o["across"] = ShipDesign.R(p.Across);
        }
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        Init(new ShipDesign(), p);
        p.BayDoorW = Math.Clamp(ShipDesign.F(o["bayDoorW"], p.BayDoorW), DoorMinW, DoorMaxW);
        p.BayDoorH = Math.Clamp(ShipDesign.F(o["bayDoorH"], p.BayDoorH), DoorMinH, DoorMaxH);
        p.BayDoorType = (string?)o["bayDoor"] == "Hinged" ? "Hinged" : "Sliding";
        p.W = Math.Clamp(ShipDesign.F(o["w"], p.W), DeckMin, DeckMax);
        p.L = Math.Clamp(ShipDesign.F(o["l"], p.L), DeckMin, DeckMax);
        p.Travel = Math.Clamp(ShipDesign.F(o["travel"], p.Travel), TravelMin, TravelMax);
        p.Link = (int?)o["link"] ?? 0;
        p.Along = Math.Clamp(ShipDesign.F(o["along"], 0), -0.5, 0.5);
        p.Across = Math.Clamp(ShipDesign.F(o["across"], 0), -0.5, 0.5);
        return p;
    }

    /// <summary>The editor's setPiece fields for these pieces; an error, or "".</summary>
    public static string Set(ShipDesign d, Piece p, JsonObject o)
    {
        static double Sn(double v, double s) => Math.Round(v / s) * s;
        if (o["bayDoorW"] is not null) p.BayDoorW = Math.Clamp(Sn((double)o["bayDoorW"]!, 0.5), DoorMinW, DoorMaxW);
        if (o["bayDoorH"] is not null) p.BayDoorH = Math.Clamp(Sn((double)o["bayDoorH"]!, 0.5), DoorMinH, DoorMaxH);
        if (o["bayDoor"] is not null) p.BayDoorType = (string?)o["bayDoor"] == "Hinged" ? "Hinged" : "Sliding";
        if (o["w"] is not null) p.W = Math.Clamp(Sn((double)o["w"]!, 0.5), DeckMin, DeckMax);
        if (o["l"] is not null) p.L = Math.Clamp(Sn((double)o["l"]!, 0.5), DeckMin, DeckMax);
        if (o["travel"] is not null) p.Travel = Math.Clamp(Sn((double)o["travel"]!, 0.1), TravelMin, TravelMax);
        if (o["along"] is not null) p.Along = Math.Clamp(Sn((double)o["along"]!, 0.01), -0.5, 0.5);
        if (o["across"] is not null) p.Across = Math.Clamp(Sn((double)o["across"]!, 0.01), -0.5, 0.5);
        if (o["link"] is not null)
        {
            var id = (int)o["link"]!;
            if (id != 0 && (d.PieceById(id) is not { } b || !IsBay(b.Kind)))
                return "no such hangar bay";
            p.Link = id;
        }
        return "";
    }

    /// <summary>The page's extra fields for a piece (after its others in the scene).</summary>
    public static string SceneJson(ShipDesign d, Piece p)
    {
        var sb = new StringBuilder();
        if (Bay(p.Kind) is { } b)
            sb.Append(",\"bayDoorW\":").Append(ShipDesign.S(p.BayDoorW)).Append(",\"bayDoorH\":").Append(ShipDesign.S(p.BayDoorH)).Append(",\"bayDoor\":").Append(DesignScene.Q(p.BayDoorType))
              .Append(",\"bay\":[").Append(ShipDesign.S(b.W)).Append(',').Append(ShipDesign.S(b.H)).Append(',').Append(ShipDesign.S(b.D)).Append(']')
              .Append(",\"fittings\":").Append(d.Pieces.Count(q => IsFitting(q.Kind) && q.Link == p.Id));
        else if (IsDeck(p.Kind))
            sb.Append(",\"w\":").Append(ShipDesign.S(p.W)).Append(",\"l\":").Append(ShipDesign.S(p.L)).Append(",\"travel\":").Append(ShipDesign.S(p.Travel));
        else if (IsFitting(p.Kind))
            sb.Append(",\"link\":").Append(p.Link).Append(",\"along\":").Append(ShipDesign.S(p.Along)).Append(",\"across\":").Append(ShipDesign.S(p.Across));
        return sb.ToString();
    }

    /// <summary>What else a piece's mesh follows: a fitting's bay (its place and size).</summary>
    public static string Key(ShipDesign d, Piece p) =>
        IsFitting(p.Kind) && d.PieceById(p.Link) is { } b ? "@" + b.Hull + ":" + b.ToJson().ToJsonString() : "";

    // ---- where things are

    /// <summary>Which of a place's axes is nearest the world's up (0 X, 1 Y, 2 Z).</summary>
    private static int Vertical(DesignScene.Place w)
    {
        double ax = Math.Abs(w.X[1]), ay = Math.Abs(w.Y[1]), az = Math.Abs(w.Z[1]);
        return ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
    }

    /// <summary>A bay's volume along its frame's X, Y (inward) and Z: its height goes up, its depth in from the door.</summary>
    public static (double X, double Y, double Z) Extent(BaySize b, DesignScene.Place w) => Vertical(w) switch
    {
        1 => (b.W, b.H, b.D),
        0 => (b.H, b.D, b.W),
        _ => (b.W, b.D, b.H),
    };

    /// <summary>A bay door's size along its frame's X and Z.</summary>
    public static (double X, double Z) DoorExtent(Piece p, DesignScene.Place w) => Vertical(w) == 0 ? (p.BayDoorH, p.BayDoorW) : (p.BayDoorW, p.BayDoorH);

    /// <summary>The hole a sized socket cuts, in the units the hole code scales by Size x Hole; null for other pieces.</summary>
    public static (double Hx, double Hz, bool Round)? OpeningOf(Piece p, DesignScene.Place w)
    {
        if (IsBay(p.Kind))
        {
            var (ex, ez) = DoorExtent(p, w);
            return (ex / 2 / p.Size, ez / 2 / p.Size, false);
        }
        if (IsDeck(p.Kind))
            return (p.W / 2 / p.Size, p.L / 2 / p.Size, false);
        return null;
    }

    /// <summary>A fitting's place inside its linked bay: on the bay's floor (for a belly or roof bay, its back wall), Along and Across
    /// it, its Y up off the floor and its Z into the bay. Null when unlinked (it then sits on the skin where it was dropped).</summary>
    public static DesignScene.Place? PlaceOf(ShipDesign d, Piece p)
    {
        if (!IsFitting(p.Kind) || d.PieceById(p.Link) is not { } bay || Bay(bay.Kind) is not { } b || d.Hull(bay.Hull) is not { } h)
            return null;
        var w = DesignScene.Where(h, bay);
        var (ex, ey, ez) = Extent(b, w);
        double[] c = Add(w.O, w.Y, -ey / 2 - bay.Recess);
        double[] up, into, side;
        double ext, extSide, extInto;
        var v = Vertical(w);
        if (v == 1)
        {
            (up, into, side) = (w.Y, w.Z, w.X);
            (ext, extInto, extSide) = (ey, ez, ex);
        }
        else
        {
            var a = v == 0 ? w.X : w.Z;
            up = a[1] >= 0 ? a : Neg(a);
            into = Neg(w.Y);
            side = v == 0 ? w.Z : w.X;
            (ext, extInto, extSide) = (v == 0 ? ex : ez, ey, v == 0 ? ez : ex);
        }
        var o = Add(Add(Add(c, up, -ext / 2), into, p.Along * extInto), side, p.Across * extSide);
        var x = Cross(up, into);
        return new DesignScene.Place(o, x, up, into);
    }

    // ---- checks

    /// <summary>Checks a hangar piece; true when it was the whole check (a bay or a fitting), false when the usual socket check follows
    /// (the lift and the ramp deck are ways aboard like any socket).</summary>
    public static bool Check(ShipDesign d, Piece p, DesignScene.Check c, List<int> entries)
    {
        var h = d.Hull(p.Hull)!;
        var w = DesignScene.Where(h, p);
        if (Bay(p.Kind) is { } b)
        {
            var ok = true;
            if (p.BayDoorW > b.W + 1e-6)
                c.Issues.Add(new("warn", $"{p.Kind}'s door is {p.BayDoorW:0.#} m wide, wider than its bay ({b.W:0} m): make the door narrower or the bay bigger", $"piece:{p.Id}"));
            if (p.BayDoorH > b.H + 1e-6)
                c.Issues.Add(new("warn", $"{p.Kind}'s door is {p.BayDoorH:0.#} m tall, taller than its bay ({b.H:0} m): make the door lower or the bay bigger", $"piece:{p.Id}"));
            var (ex, ey, ez) = Extent(b, w);
            var back = Add(w.O, w.Y, -ey - p.Recess);
            var mid = Add(w.O, w.Y, -ey / 2 - p.Recess);
            if (h.Depth(back[0], back[1], back[2]) < 0 || h.Depth(mid[0], mid[1], mid[2]) < 0)
                c.Issues.Add(new("warn", $"{p.Kind} is {ey:0} m deep and runs out through the hull here: put it in a bigger body, or use a smaller bay", $"piece:{p.Id}"));
            // Rooms the bay's volume runs into.
            var box = Box(w.O, w.X, w.Y, w.Z, ex, ey + p.Recess, ez);
            foreach (var r in d.Rooms)
            {
                var o = DesignScene.Outer(d, r);
                if (o[0] < box[1] && o[1] > box[0] && o[2] < box[3] && o[3] > box[2] && o[4] < box[5] && o[5] > box[4])
                {
                    c.Issues.Add(new("warn", $"{p.Kind} runs into {DesignScene.Name(r)} on deck {r.Deck + 1}", $"piece:{p.Id}"));
                    break;
                }
            }
            if (d.Pieces.Count(q => IsFitting(q.Kind) && q.Link == p.Id) == 0)
                c.Issues.Add(new("warn", $"{p.Kind} has no docking arm or cradle: add one inside it", $"piece:{p.Id}"));
            c.PieceOk[p.Id] = ok;
            return true;
        }
        if (IsFitting(p.Kind))
        {
            var ok = true;
            if (p.Link == 0)
                c.Issues.Add(new("warn", $"{p.Kind} isn't linked to a hangar bay: link it to the bay it serves", $"piece:{p.Id}"));
            else if (d.PieceById(p.Link) is not { } bay || !IsBay(bay.Kind) || d.Hull(bay.Hull) is not { } bh)
            {
                ok = false;
                c.Issues.Add(new("error", $"{p.Kind}'s hangar bay is gone: link it to another", $"piece:{p.Id}"));
            }
            else if (PlaceOf(d, p) is { } fw)
            {
                var bw = DesignScene.Where(bh, bay);
                var bb = Bay(bay.Kind)!;
                // A link line to the bay's door and one to the bay's middle (the data links, drawn like the sockets').
                var (_, ey, _) = Extent(bb, bw);
                c.Links.Add((fw.O, bw.O, true, 0, false, bw.Y, "hangar"));
                c.Links.Add((fw.O, Add(bw.O, bw.Y, -ey / 2 - bay.Recess), true, 0, false, bw.Y, "hangar"));
                var need = 7.0 * p.Size;
                if (need > Math.Min(bb.W, bb.D))
                    c.Issues.Add(new("warn", $"{p.Kind} ({need:0.#} m) is too big for {bay.Kind}: make it smaller", $"piece:{p.Id}"));
            }
            c.PieceOk[p.Id] = ok;
            return true;
        }
        if (p.Kind == "Lift platform" && p.Surface == "skin" && w.Y[1] > -0.7)
            c.Issues.Add(new("warn", "Lift platform rises through the belly: put it on the underside", $"piece:{p.Id}"));
        if (p.Kind == "Ramp deck" && p.Travel > p.L)
            c.Issues.Add(new("warn", $"Ramp deck drops {p.Travel:0.#} m but is only {p.L:0.#} m long: make it longer or its travel shorter", $"piece:{p.Id}"));
        return false;
    }

    // ---- meshes

    /// <summary>Draws a hangar piece in metres (resetting the mesh's frame: a bay's volume doesn't grow with its size or hole sliders).
    /// False for other pieces.</summary>
    public static bool Draw(ShipDesign d, Piece p, DesignScene.Place w, DesignMesh m)
    {
        var o = Add(w.O, w.Y, -p.Recess);
        if (Bay(p.Kind) is { } b)
        {
            m.Frame(o[0], o[1], o[2], w.X, w.Y, w.Z, 1, 1);
            DrawBay(m, p, b, w);
            return true;
        }
        if (IsDeck(p.Kind))
        {
            m.Frame(o[0], o[1], o[2], w.X, w.Y, w.Z, 1, 1);
            if (p.Kind == "Lift platform") DrawLift(m, p); else DrawRamp(m, p);
            return true;
        }
        if (IsFitting(p.Kind))
        {
            if (p.Kind == "Docking arm") DrawArm(m); else DrawCradle(m);
            return true;
        }
        return false;
    }

    private static void Ring(DesignMesh m, double hx, double hz, double band, double y0, double y1, string col)
    {
        m.Box(-hx - band, y0, -hz - band, hx + band, y1, -hz, col);
        m.Box(-hx - band, y0, hz, hx + band, y1, hz + band, col);
        m.Box(-hx - band, y0, -hz, -hx, y1, hz, col);
        m.Box(hx, y0, -hz, hx + band, y1, hz, col);
    }

    private static void DrawBay(DesignMesh m, Piece p, BaySize b, DesignScene.Place w)
    {
        var (dx, dz) = DoorExtent(p, w);
        double hx = dx / 2 * p.Hole, hz = dz / 2 * p.Hole;
        var band = Math.Clamp(Math.Min(hx, hz) * 0.12, 0.25, 2.5);
        Ring(m, hx, hz, band, 0, 0.25, PieceKinds.Armour);
        Ring(m, hx + band, hz + band, 0.12, 0.25, 0.3, PieceKinds.Amber);
        // The door's well, from the skin to the bay's mouth.
        Ring(m, hx, hz, 0.08, -1.2, 0.02, PieceKinds.Gun);
        // The door, open: a sliding door run aside on its piston, a hinged one swung out 70 degrees on its lower (aft) edge.
        var th = Math.Clamp(Math.Min(hx, hz) * 0.06, 0.12, 1.2);
        if (p.BayDoorType == "Sliding")
        {
            var along = hz >= hx; // it slides along its long side
            double sx = along ? 0 : hx * 1.2, sz = along ? hz * 1.2 : 0;
            m.Box(-hx + sx, -1.0 - th, -hz + sz, hx + sx, -1.0, hz + sz, PieceKinds.Gun);
            m.Box(-hx * 0.9 + sx, -1.0 - th - 0.02, -0.06 + sz, hx * 0.9 + sx, -1.0 + 0.02, 0.06 + sz, PieceKinds.Amber);
            // The piston: a cylinder and its rod, along the way it slides.
            var r = Math.Max(0.12, th * 0.6);
            if (along)
            {
                m.Box(-r, -1.0 - th - 2 * r, -hz, r, -1.0 - th, hz, PieceKinds.Pale);
                m.Box(-r * 0.5, -1.0 - th - 1.5 * r, hz, r * 0.5, -1.0 - th - 0.5 * r, hz + hz * 1.2, PieceKinds.Amber);
            }
            else
            {
                m.Box(-hx, -1.0 - th - 2 * r, -r, hx, -1.0 - th, r, PieceKinds.Pale);
                m.Box(hx, -1.0 - th - 1.5 * r, -r * 0.5, hx + hx * 1.2, -1.0 - th - 0.5 * r, r * 0.5, PieceKinds.Amber);
            }
        }
        else
        {
            const double a = 70 * Math.PI / 180;
            if (Vertical(w) == 0)
            {
                // On a side wall: hinged along its lower edge, the leaf swung out and down like a drawbridge.
                var s = w.X[1] >= 0 ? -1 : 1;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                Slab(m, (s * hx - s * ca * hx, sa * hx, 0), (-s * ca * hx, sa * hx, 0), (s * sa * th / 2, ca * th / 2, 0), (0, 0, hz), PieceKinds.Gun);
                for (var k = -1; k <= 1; k += 2)
                    m.Box(s * hx - th, -th, k * hz * 0.7 - th, s * hx + th, th, k * hz * 0.7 + th, PieceKinds.Amber);
                goto bay;
            }
            var len = 2 * hz;
            // The leaf from the hinge on the -Z edge, swung out (+Y).
            var c = (0.0, Math.Sin(a) * len / 2, -hz + Math.Cos(a) * len / 2);
            Slab(m, c, (hx, 0, 0), (0, Math.Cos(a) * th / 2, -Math.Sin(a) * th / 2), (0, Math.Sin(a) * len / 2, Math.Cos(a) * len / 2), PieceKinds.Gun);
            for (var s = -1; s <= 1; s += 2)
                m.Box(s * hx * 0.7 - th, -th, -hz - th, s * hx * 0.7 + th, th, -hz + th, PieceKinds.Amber); // the motor hinges
        }
        // The bay: its frame's edges, back wall, floor and lamps (Extent: across, inward, along).
        bay:;
        var (ex, ey, ez) = Extent(b, w);
        double x = ex / 2, z = ez / 2, y0 = -1.2, y1 = -ey;
        var beam = Math.Clamp(Math.Min(ex, ez) * 0.012, 0.15, 0.8);
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                m.Box(sx * x - beam, y1, sz * z - beam, sx * x + beam, y0, sz * z + beam, PieceKinds.Gun);
        foreach (var yy in new[] { y0, y1 })
        {
            m.Box(-x, yy - beam, -z - beam, x, yy + beam, -z + beam, PieceKinds.Gun);
            m.Box(-x, yy - beam, z - beam, x, yy + beam, z + beam, PieceKinds.Gun);
            m.Box(-x - beam, yy - beam, -z, -x + beam, yy + beam, z, PieceKinds.Gun);
            m.Box(x - beam, yy - beam, -z, x + beam, yy + beam, z, PieceKinds.Gun);
        }
        m.Box(-x, y1 - beam * 0.6, -z, x, y1 + 0.02 - beam * 0.6 + 0.1, z, PieceKinds.Armour); // the back wall, behind the edge beams
        var v = Vertical(w);
        if (v != 1)
        {
            // The deck, on the bay's lower side, with a lamp strip down its middle.
            if (v == 0)
            {
                var s = w.X[1] >= 0 ? -1 : 1;
                m.Box(s * x - 0.1, y1, -z, s * x + 0.1, y0, z, PieceKinds.Armour);
                m.Box(-s * x - 0.2, y1 * 0.9, -0.15, -s * x - 0.12, y0 - 1, 0.15, PieceKinds.Glow);
            }
            else
            {
                var s = w.Z[1] >= 0 ? -1 : 1;
                m.Box(-x, y1, s * z - 0.1, x, y0, s * z + 0.1, PieceKinds.Armour);
                m.Box(-0.15, y1 * 0.9, -s * z - 0.2, 0.15, y0 - 1, -s * z - 0.12, PieceKinds.Glow);
            }
        }
        else
            for (var i = -1; i <= 1; i++)
                m.Box(i * x * 0.5 - 0.4, y1 + 0.13, -0.15, i * x * 0.5 + 0.4, y1 + 0.2, 0.15, PieceKinds.Glow);
    }

    private static void DrawLift(DesignMesh m, Piece p)
    {
        double hx = p.W / 2 * p.Hole, hz = p.L / 2 * p.Hole, t = p.Travel;
        Ring(m, hx, hz, 0.25, 0, 0.18, PieceKinds.Armour);
        Ring(m, hx + 0.25, hz + 0.25, 0.1, 0.18, 0.22, PieceKinds.Amber);
        Ring(m, hx, hz, 0.06, -1.0, 0.02, PieceKinds.Gun);
        // Lowered by its travel out of the belly, on four pistons.
        m.Box(-hx + 0.05, t, -hz + 0.05, hx - 0.05, t + 0.2, hz - 0.05, PieceKinds.Gun);
        Ring(m, hx - 0.05, hz - 0.05, 0.08, t + 0.2, t + 0.26, PieceKinds.Amber);
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double px = sx * (hx - 0.5), pz = sz * (hz - 0.5);
                m.Box(px - 0.18, -1.0, pz - 0.18, px + 0.18, t * 0.5, pz + 0.18, PieceKinds.Pale);
                m.Box(px - 0.1, t * 0.5, pz - 0.1, px + 0.1, t, pz + 0.1, PieceKinds.Amber);
            }
    }

    private static void DrawRamp(DesignMesh m, Piece p)
    {
        double hx = p.W / 2 * p.Hole, hz = p.L / 2 * p.Hole;
        Ring(m, hx, hz, 0.25, 0, 0.18, PieceKinds.Armour);
        Ring(m, hx, hz, 0.06, -1.0, 0.02, PieceKinds.Gun);
        // Hinged at its aft edge (+Z toward the bow is -Z aft... the frame's -Z), lowered till its far end is Travel out.
        var len = 2 * hz;
        var a = Math.Asin(Math.Min(1, p.Travel / len));
        const double th = 0.25;
        var c = (0.0, Math.Sin(a) * len / 2, -hz + Math.Cos(a) * len / 2);
        Slab(m, c, (hx, 0, 0), (0, Math.Cos(a) * th / 2, -Math.Sin(a) * th / 2), (0, Math.Sin(a) * len / 2, Math.Cos(a) * len / 2), PieceKinds.Gun);
        // Hazard edges and the hinge.
        foreach (var s in new[] { -1, 1 })
            Slab(m, (s * (hx - 0.1), c.Item2 - Math.Cos(a) * th * 0.6, c.Item3 + Math.Sin(a) * th * 0.6), (0.1, 0, 0), (0, Math.Cos(a) * 0.04, -Math.Sin(a) * 0.04), (0, Math.Sin(a) * len / 2, Math.Cos(a) * len / 2), PieceKinds.Amber);
        m.Box(-hx, -0.2, -hz - 0.2, hx, 0.2, -hz + 0.2, PieceKinds.Pale);
    }

    /// <summary>A docking arm in a fitting's frame (Y up off the bay's floor, Z into the bay): a base, a column and a boom out towards
    /// the door, with a clamp head.</summary>
    private static void DrawArm(DesignMesh m)
    {
        m.Box(-0.9, 0, -0.9, 0.9, 0.5, 0.9, PieceKinds.Armour);
        m.Frustum(0.5, 0.45, 3.2, 0.35, 12, PieceKinds.Gun);
        m.Box(-0.4, 3.0, -0.6, 0.4, 3.6, 0.6, PieceKinds.Armour);
        m.Box(-0.25, 3.1, -5.5, 0.25, 3.5, 0.2, PieceKinds.Gun);
        m.Box(-0.7, 2.8, -6.3, 0.7, 3.8, -5.5, PieceKinds.Amber);
        m.Box(-0.8, 2.6, -6.5, -0.6, 4.0, -6.3, PieceKinds.Gun);
        m.Box(0.6, 2.6, -6.5, 0.8, 4.0, -6.3, PieceKinds.Gun);
    }

    /// <summary>A docking cradle: a base plate, two rows of curved arms and pads a craft settles on, and a lamp at each end.</summary>
    private static void DrawCradle(DesignMesh m)
    {
        m.Box(-2.2, 0, -3.5, 2.2, 0.3, 3.5, PieceKinds.Armour);
        m.Box(-2.25, 0.3, -3.55, 2.25, 0.34, -3.35, PieceKinds.Amber);
        m.Box(-2.25, 0.3, 3.35, 2.25, 0.34, 3.55, PieceKinds.Amber);
        foreach (var z in new[] { -2.5, 0.0, 2.5 })
            foreach (var s in new[] { -1, 1 })
            {
                m.Box(s * 1.6 - 0.2, 0.3, z - 0.3, s * 1.6 + 0.2, 1.4, z + 0.3, PieceKinds.Gun);
                m.Box(s * 1.75 - 0.15, 1.4, z - 0.3, s * 1.75 + 0.15, 2.0, z + 0.3, PieceKinds.Gun);
                m.Box(s * 1.45 - 0.25, 1.35, z - 0.35, s * 1.45 + 0.25, 1.45, z + 0.35, PieceKinds.Black);
            }
        m.Box(-0.2, 0.3, -3.3, 0.2, 0.5, -3.0, PieceKinds.Glow);
        m.Box(-0.2, 0.3, 3.0, 0.2, 0.5, 3.3, PieceKinds.Glow);
    }

    /// <summary>A box at c with half axes a, b, e (in the frame's space): for a door or deck swung on its hinge.</summary>
    private static void Slab(DesignMesh m, (double, double, double) c, (double, double, double) a, (double, double, double) b, (double, double, double) e, string colour)
    {
        var col = DesignMesh.Hex(colour);
        var v = new (double, double, double)[8];
        for (var i = 0; i < 8; i++)
        {
            double sa = (i & 1) == 0 ? -1 : 1, sb = (i & 2) == 0 ? -1 : 1, se = (i & 4) == 0 ? -1 : 1;
            v[i] = (c.Item1 + sa * a.Item1 + sb * b.Item1 + se * e.Item1, c.Item2 + sa * a.Item2 + sb * b.Item2 + se * e.Item2, c.Item3 + sa * a.Item3 + sb * b.Item3 + se * e.Item3);
        }
        m.Quad(v[0], v[2], v[3], v[1], DesignMesh.Tone(col, 0.8));
        m.Quad(v[4], v[5], v[7], v[6], DesignMesh.Tone(col, 0.95));
        m.Quad(v[0], v[4], v[6], v[2], DesignMesh.Tone(col, 0.85));
        m.Quad(v[1], v[3], v[7], v[5], DesignMesh.Tone(col, 0.9));
        m.Quad(v[0], v[1], v[5], v[4], DesignMesh.Tone(col, 0.7));
        m.Quad(v[2], v[6], v[7], v[3], col);
    }

    // ---- vectors

    private static double[] Add(double[] a, double[] b, double s) => [a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s];
    private static double[] Neg(double[] a) => [-a[0], -a[1], -a[2]];
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    private static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));

    /// <summary>The world box of a frame's volume: ex across X, ey in along -Y, ez along Z (x0, x1, y0, y1, z0, z1).</summary>
    private static double[] Box(double[] o, double[] X, double[] Y, double[] Z, double ex, double ey, double ez)
    {
        double[] lo = [double.MaxValue, double.MaxValue, double.MaxValue], hi = [double.MinValue, double.MinValue, double.MinValue];
        foreach (var sx in new[] { -0.5, 0.5 })
            foreach (var sy in new[] { 0.0, -1.0 })
                foreach (var sz in new[] { -0.5, 0.5 })
                    for (var k = 0; k < 3; k++)
                    {
                        var v = o[k] + X[k] * sx * ex + Y[k] * sy * ey + Z[k] * sz * ez;
                        lo[k] = Math.Min(lo[k], v); hi[k] = Math.Max(hi[k], v);
                    }
        return [lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]];
    }
}
