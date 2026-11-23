using System.Text;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Everything the designer shows and checks, worked out from a design: the hull skins (cut where bodies meet), the pieces where they sit, the
/// rooms and their doors, each socket's link to its room, the issues and the totals. Written as one JSON object for the page.
/// </summary>
public static class DesignScene
{
    public sealed record Issue(string Level, string Text, string What);

    public sealed class Check
    {
        public readonly List<Issue> Issues = [];
        public readonly Dictionary<int, bool> RoomOk = [];
        public readonly Dictionary<int, bool> PieceOk = [];
        public readonly List<(double[] A, double[] B, bool Ok)> Links = [];
        public readonly List<double[]> Doors = [];
        public readonly HashSet<int> Reached = [];
    }

    // ---- where a piece sits

    public readonly record struct Place(double[] O, double[] X, double[] Y, double[] Z);

    public static Place Where(HullBody h, Piece p)
    {
        var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
        double[] o, y, z;
        switch (p.Surface)
        {
            case "aft":
                o = [cx + p.U, h.Y + p.V, h.Z + h.Back];
                y = [0, 0, 1];
                z = [0, 1, 0];
                break;
            case "fore":
                o = [cx + p.U, h.Y + p.V, h.Z + h.Front];
                y = [0, 0, -1];
                z = [0, 1, 0];
                break;
            default:
                var s = h.OnSkin(p.Z, p.T, p.Side);
                o = [cx + s.X, h.Y + s.Y, h.Z + s.Z];
                y = [s.Nx, s.Ny, 0];
                z = [0, 0, -1];
                break;
        }
        var x = Cross(y, z);
        if (p.Turn != 0)
        {
            var a = p.Turn * Math.PI / 180;
            double c = Math.Cos(a), sn = Math.Sin(a);
            var x2 = new[] { x[0] * c + z[0] * sn, x[1] * c + z[1] * sn, x[2] * c + z[2] * sn };
            var z2 = new[] { z[0] * c - x[0] * sn, z[1] * c - x[1] * sn, z[2] * c - x[2] * sn };
            (x, z) = (x2, z2);
        }
        return new Place(o, x, y, z);
    }

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    /// <summary>The world points of a piece and its mirror twin (the twin across its hull copy's centreline).</summary>
    public static IEnumerable<(double[] At, double[] N)> Spots(ShipDesign d, Piece p)
    {
        var h = d.Hull(p.Hull);
        if (h is null)
            yield break;
        var w = Where(h, p);
        yield return (w.O, w.Y);
        if (p.Mirror)
        {
            var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
            yield return ([2 * cx - w.O[0], w.O[1], w.O[2]], [-w.Y[0], w.Y[1], w.Y[2]]);
        }
    }

    // ---- hull meshes

    /// <summary>The skin of one copy of a body: the side facets, the keel and the spine, each band a little toned so the stations show; cut where it meets a body set to cut.</summary>
    public static (DesignMesh Skin, DesignMesh Fore, DesignMesh Aft) HullMesh(ShipDesign d, HullBody h, int copy)
    {
        var skin = new DesignMesh();
        var fore = new DesignMesh();
        var aft = new DesignMesh();
        var cx = h.CopyX(copy);
        var (bx0, bx1, by0, by1, bz0, bz1) = h.Bounds();
        var cutters = d.Hulls.Where(o => o != h && (o.Cut || h.Cut) && Overlap(o.Bounds(), (bx0, bx1, by0, by1, bz0, bz1))).ToList();
        bool Cut(double x, double y, double z) => cutters.Any(o => o.Depth(x, y, z) > 0.02);

        var col = DesignMesh.Hex(h.Skin);
        var dark = DesignMesh.Hex("#3a3e44");
        var n = h.Points;
        for (var i = 0; i < h.Stations.Count - 1; i++)
        {
            var a = h.Stations[i];
            var b = h.Stations[i + 1];
            var band = i % 2 == 0 ? 1.0 : 0.94;
            var nz = cutters.Count == 0 ? 1 : Math.Clamp((int)Math.Ceiling((b.Z - a.Z) / 0.5), 1, 48);
            foreach (var s in new[] { -1, 1 })
                for (var k = 0; k < n - 1; k++)
                {
                    var shade = DesignMesh.Tone(col, band * (k % 2 == 0 ? 1.0 : 0.9));
                    Strip(skin, cx, h, (s * a.P[k].H, a.P[k].Y, a.Z), (s * a.P[k + 1].H, a.P[k + 1].Y, a.Z), (s * b.P[k].H, b.P[k].Y, b.Z), (s * b.P[k + 1].H, b.P[k + 1].Y, b.Z), shade, nz, cutters.Count > 0, Cut, s < 0);
                }
            // The keel (across the bottom) and the spine (across the top).
            Strip(skin, cx, h, (-a.P[0].H, a.P[0].Y, a.Z), (a.P[0].H, a.P[0].Y, a.Z), (-b.P[0].H, b.P[0].Y, b.Z), (b.P[0].H, b.P[0].Y, b.Z), DesignMesh.Tone(dark, band), nz, cutters.Count > 0, Cut, false);
            Strip(skin, cx, h, (a.P[n - 1].H, a.P[n - 1].Y, a.Z), (-a.P[n - 1].H, a.P[n - 1].Y, a.Z), (b.P[n - 1].H, b.P[n - 1].Y, b.Z), (-b.P[n - 1].H, b.P[n - 1].Y, b.Z), DesignMesh.Tone(col, band * 1.05), nz, cutters.Count > 0, Cut, false);
        }
        Cap(fore, cx, h, h.Stations[0], DesignMesh.Tone(col, 0.85), Cut);
        Cap(aft, cx, h, h.Stations[^1], DesignMesh.Tone(dark, 1.2), Cut);
        return (skin, fore, aft);
    }

    private static bool Overlap((double, double, double, double, double, double) a, (double, double, double, double, double, double) b) =>
        a.Item1 < b.Item2 && b.Item1 < a.Item2 && a.Item3 < b.Item4 && b.Item3 < a.Item4 && a.Item5 < b.Item6 && b.Item5 < a.Item6;

    /// <summary>One facet between two stations as a grid of quads (nz along, more across where cutting), leaving out those inside a cutter.</summary>
    private static void Strip(DesignMesh m, double cx, HullBody h, (double X, double Y, double Z) a0, (double X, double Y, double Z) a1, (double X, double Y, double Z) b0,
        (double X, double Y, double Z) b1, (byte, byte, byte) col, int nz, bool cutting, Func<double, double, double, bool> cut, bool flip)
    {
        var across = Math.Sqrt(Math.Pow(a1.X - a0.X, 2) + Math.Pow(a1.Y - a0.Y, 2));
        var nk = cutting ? Math.Clamp((int)Math.Ceiling(across / 0.5), 1, 16) : 1;
        (double, double, double) Pt(double u, double v)
        {
            var px = a0.X + (a1.X - a0.X) * u + ((b0.X + (b1.X - b0.X) * u) - (a0.X + (a1.X - a0.X) * u)) * v;
            var py = a0.Y + (a1.Y - a0.Y) * u + ((b0.Y + (b1.Y - b0.Y) * u) - (a0.Y + (a1.Y - a0.Y) * u)) * v;
            var pz = a0.Z + (b0.Z - a0.Z) * v;
            return (cx + px, h.Y + py, h.Z + pz);
        }
        for (var j = 0; j < nz; j++)
            for (var i = 0; i < nk; i++)
            {
                double u0 = (double)i / nk, u1 = (double)(i + 1) / nk, v0 = (double)j / nz, v1 = (double)(j + 1) / nz;
                if (cutting)
                {
                    var (mx, my, mz) = Pt((u0 + u1) / 2, (v0 + v1) / 2);
                    if (cut(mx, my, mz))
                        continue;
                }
                if (flip)
                    m.Quad(Pt(u0, v0), Pt(u0, v1), Pt(u1, v1), Pt(u1, v0), col, false);
                else
                    m.Quad(Pt(u0, v0), Pt(u1, v0), Pt(u1, v1), Pt(u0, v1), col, false);
            }
    }

    /// <summary>An end closed flat: a fan from the middle of the profile round both sides.</summary>
    private static void Cap(DesignMesh m, double cx, HullBody h, Station s, (byte, byte, byte) col, Func<double, double, double, bool> cut)
    {
        var ring = new List<(double, double, double)>();
        foreach (var (hh, y) in s.P)
            ring.Add((cx + hh, h.Y + y, h.Z + s.Z));
        for (var k = s.P.Count - 1; k >= 0; k--)
            ring.Add((cx - s.P[k].H, h.Y + s.P[k].Y, h.Z + s.Z));
        var mid = (cx, h.Y + (s.P[0].Y + s.P[^1].Y) / 2, h.Z + s.Z);
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            if (cut((a.Item1 + b.Item1 + mid.Item1) / 3, (a.Item2 + b.Item2 + mid.Item2) / 3, (a.Item3 + b.Item3 + mid.Item3) / 3))
                continue;
            m.Tri(mid, a, b, col, false);
        }
    }

    // ---- checks

    public static Check Run(ShipDesign d)
    {
        var c = new Check();
        // Rooms inside the skin (with 6 cm for the plates, as the Zero Day's MustFit), and apart.
        foreach (var r in d.Rooms)
        {
            var ok = true;
            var dk = d.Decks[r.Deck];
            foreach (var y in new[] { dk.Floor + 0.05, dk.Ceiling - 0.02 })
            {
                var nx = Math.Max(1, (int)Math.Ceiling(r.X1 - r.X0));
                var nz = Math.Max(1, (int)Math.Ceiling(r.Z1 - r.Z0));
                for (var i = 0; i <= nx && ok; i++)
                    for (var j = 0; j <= nz && ok; j++)
                    {
                        if (i != 0 && i != nx && j != 0 && j != nz)
                            continue; // the edges only
                        var x = r.X0 + (r.X1 - r.X0) * i / nx;
                        var z = r.Z0 + (r.Z1 - r.Z0) * j / nz;
                        if (!d.Hulls.Any(h => h.Depth(x, y, z) >= 0.06))
                        {
                            ok = false;
                            c.Issues.Add(new("error", $"{Name(r)} on deck {r.Deck + 1} pokes through the skin near x {x:0.0}, z {z:0.0}", $"room:{r.Id}"));
                        }
                    }
            }
            foreach (var o in d.Rooms)
                if (o.Id < r.Id && o.Deck == r.Deck && Math.Min(r.X1, o.X1) - Math.Max(r.X0, o.X0) > 0.01 && Math.Min(r.Z1, o.Z1) - Math.Max(r.Z0, o.Z0) > 0.01)
                {
                    ok = false;
                    c.RoomOk[o.Id] = false;
                    c.Issues.Add(new("error", $"{Name(r)} and {Name(o)} overlap on deck {r.Deck + 1}", $"room:{r.Id}"));
                }
            if (!c.RoomOk.ContainsKey(r.Id))
                c.RoomOk[r.Id] = ok;
        }

        // Doors: rooms on one deck that share at least 0.9 m of wall; shafts join the decks they overlap.
        var joins = d.Rooms.ToDictionary(r => r.Id, _ => new List<int>());
        foreach (var a in d.Rooms)
            foreach (var b in d.Rooms)
            {
                if (a.Id >= b.Id)
                    continue;
                if (a.Deck == b.Deck)
                {
                    var dk = d.Decks[a.Deck];
                    var oz = Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0);
                    var ox = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0);
                    double[]? door = null;
                    if (oz >= 0.9 && (Math.Abs(a.X1 - b.X0) < 0.02 || Math.Abs(b.X1 - a.X0) < 0.02))
                        door = [Math.Abs(a.X1 - b.X0) < 0.02 ? a.X1 : a.X0, dk.Floor, (Math.Max(a.Z0, b.Z0) + Math.Min(a.Z1, b.Z1)) / 2];
                    else if (ox >= 0.9 && (Math.Abs(a.Z1 - b.Z0) < 0.02 || Math.Abs(b.Z1 - a.Z0) < 0.02))
                        door = [(Math.Max(a.X0, b.X0) + Math.Min(a.X1, b.X1)) / 2, dk.Floor, Math.Abs(a.Z1 - b.Z0) < 0.02 ? a.Z1 : a.Z0];
                    if (door is not null)
                    {
                        joins[a.Id].Add(b.Id);
                        joins[b.Id].Add(a.Id);
                        c.Doors.Add(door);
                    }
                }
                else if (Math.Abs(a.Deck - b.Deck) == 1 && (a.Kind == "Shaft" || b.Kind == "Shaft") &&
                         Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0) >= 0.8 && Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0) >= 0.8)
                {
                    joins[a.Id].Add(b.Id);
                    joins[b.Id].Add(a.Id);
                    var s = a.Kind == "Shaft" ? a : b;
                    var hi = d.Decks[Math.Max(a.Deck, b.Deck)];
                    c.Doors.Add([(s.X0 + s.X1) / 2, hi.Floor, (s.Z0 + s.Z1) / 2]);
                }
            }

        // Pieces: on their hull, not where the skin is cut away; sockets reach a room of the kind they need.
        var lowest = d.Decks.Count == 0 ? -1 : d.Decks.IndexOf(d.Decks.MinBy(k => k.Floor)!);
        var entries = new List<int>();
        var anySocket = false;
        foreach (var p in d.Pieces)
        {
            var h = d.Hull(p.Hull)!;
            var kind = PieceKinds.Find(p.Kind)!;
            var ok = true;
            if (p.Surface != "skin")
            {
                var st = p.Surface == "aft" ? h.Stations[^1] : h.Stations[0];
                var half = Half(st, p.V);
                if (half < 0 || Math.Abs(p.U) > half)
                {
                    ok = false;
                    c.Issues.Add(new("error", $"{p.Kind} is off the {(p.Surface == "aft" ? "stern" : "bow")}'s end", $"piece:{p.Id}"));
                }
            }
            foreach (var (at, n) in Spots(d, p))
            {
                if (d.Hulls.Any(o => o != h && (o.Cut || h.Cut) && o.Depth(at[0], at[1], at[2]) > 0.05))
                {
                    ok = false;
                    c.Issues.Add(new("warn", $"{p.Kind} sits where the skin is cut away (inside {OtherName(d, h, at)})", $"piece:{p.Id}"));
                }
                if (kind.Socket is null)
                    continue;
                anySocket = true;
                Room? best = null;
                var bestD = double.MaxValue;
                double[] bestPt = at;
                foreach (var r in d.Rooms)
                {
                    if (kind.Socket == "cargo" && r.Kind != "Hold")
                        continue;
                    var dk = d.Decks[r.Deck];
                    if (kind.Socket == "shaft" && (r.Deck != lowest || at[1] > dk.Floor || at[0] < r.X0 - 0.3 || at[0] > r.X1 + 0.3 || at[2] < r.Z0 - 0.3 || at[2] > r.Z1 + 0.3))
                        continue;
                    double[] q = [Math.Clamp(at[0], r.X0, r.X1), Math.Clamp(at[1], dk.Floor, dk.Ceiling), Math.Clamp(at[2], r.Z0, r.Z1)];
                    var dist = Math.Sqrt(Math.Pow(q[0] - at[0], 2) + Math.Pow(q[1] - at[1], 2) + Math.Pow(q[2] - at[2], 2));
                    if (dist < bestD)
                        (best, bestD, bestPt) = (r, dist, q);
                }
                var linked = best is not null && bestD <= kind.Reach;
                if (linked)
                    entries.Add(best!.Id);
                else
                {
                    ok = false;
                    c.Issues.Add(new("error", best is null ? $"{p.Kind} needs {kind.Needs} behind it" :
                        $"{p.Kind} is {bestD:0.0} m from {Name(best)} (at most {kind.Reach:0.0} m): move it or the room closer", $"piece:{p.Id}"));
                }
                c.Links.Add((at, bestPt, linked));
            }
            c.PieceOk[p.Id] = ok;
        }

        // Every room reachable from a way aboard.
        var queue = new Queue<int>(entries.Distinct());
        foreach (var e in queue)
            c.Reached.Add(e);
        while (queue.Count > 0)
            foreach (var nb in joins[queue.Dequeue()])
                if (c.Reached.Add(nb))
                    queue.Enqueue(nb);
        if (d.Rooms.Count > 0 && entries.Count == 0)
            c.Issues.Add(new("error", anySocket ? "No airlock, dock or boarding hatch opens into a room: nobody can get aboard" : "No way aboard: add an airlock, a docking collar or a boarding hatch", ""));
        else
            foreach (var r in d.Rooms)
                if (!c.Reached.Contains(r.Id))
                    c.Issues.Add(new("warn", $"{Name(r)} on deck {r.Deck + 1} has no door to the rest (rooms need 0.9 m of shared wall; a Shaft joins decks)", $"room:{r.Id}"));
        if (!d.Rooms.Any(r => r.Kind == "Bridge"))
            c.Issues.Add(new("warn", "No bridge", ""));
        return c;
    }

    private static double Half(Station s, double y)
    {
        if (y < s.P[0].Y || y > s.P[^1].Y)
            return -1;
        for (var k = 0; k < s.P.Count - 1; k++)
            if (y <= s.P[k + 1].Y)
                return s.P[k].H + (s.P[k + 1].H - s.P[k].H) * (y - s.P[k].Y) / (s.P[k + 1].Y - s.P[k].Y);
        return s.P[^1].H;
    }

    private static string OtherName(ShipDesign d, HullBody h, double[] at) =>
        d.Hulls.FirstOrDefault(o => o != h && o.Depth(at[0], at[1], at[2]) > 0.05)?.Name ?? "another body";

    public static string Name(Room r) => r.Label.Length > 0 ? r.Label : r.Kind;

    // ---- the page's JSON

    public static string Json(ShipDesign d)
    {
        var c = Run(d);
        var sb = new StringBuilder(1 << 16);
        sb.Append("{\"name\":").Append(Q(d.Name)).Append(",\"hulls\":[");
        for (var i = 0; i < d.Hulls.Count; i++)
        {
            var h = d.Hulls[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(h.Id).Append(",\"name\":").Append(Q(h.Name))
              .Append(",\"x\":").Append(ShipDesign.S(h.X)).Append(",\"y\":").Append(ShipDesign.S(h.Y)).Append(",\"z\":").Append(ShipDesign.S(h.Z))
              .Append(",\"mirror\":").Append(h.Mirror ? "true" : "false").Append(",\"cut\":").Append(h.Cut ? "true" : "false").Append(",\"skin\":").Append(Q(h.Skin))
              .Append(",\"stations\":[");
            for (var s = 0; s < h.Stations.Count; s++)
            {
                if (s > 0) sb.Append(',');
                sb.Append("{\"z\":").Append(ShipDesign.S(h.Stations[s].Z)).Append(",\"p\":[");
                for (var k = 0; k < h.Points; k++)
                    sb.Append(k > 0 ? "," : "").Append('[').Append(ShipDesign.S(h.Stations[s].P[k].H)).Append(',').Append(ShipDesign.S(h.Stations[s].P[k].Y)).Append(']');
                sb.Append("]}");
            }
            sb.Append("],\"copies\":[");
            for (var copy = 0; copy < h.Copies; copy++)
            {
                var (skin, fore, aft) = HullMesh(d, h, copy);
                if (copy > 0) sb.Append(',');
                sb.Append("{\"copy\":").Append(copy).Append(",\"skin\":");
                skin.Write(sb);
                sb.Append(",\"fore\":");
                fore.Write(sb);
                sb.Append(",\"aft\":");
                aft.Write(sb);
                sb.Append('}');
            }
            sb.Append("]}");
        }
        sb.Append("],\"pieces\":[");
        for (var i = 0; i < d.Pieces.Count; i++)
        {
            var p = d.Pieces[i];
            var h = d.Hull(p.Hull)!;
            var kind = PieceKinds.Find(p.Kind)!;
            var m = new DesignMesh();
            var w = Where(h, p);
            m.Frame(w.O[0], w.O[1], w.O[2], w.X, w.Y, w.Z);
            kind.Draw(m);
            if (p.Mirror)
                m.MirrorFrom(0, h.CopyX(Math.Min(p.Copy, h.Copies - 1)));
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(p.Id).Append(",\"kind\":").Append(Q(p.Kind)).Append(",\"hull\":").Append(p.Hull).Append(",\"surface\":").Append(Q(p.Surface))
              .Append(",\"mirror\":").Append(p.Mirror ? "true" : "false").Append(",\"turn\":").Append(ShipDesign.S(p.Turn))
              .Append(",\"socket\":").Append(kind.Socket is null ? "null" : Q(kind.Socket)).Append(",\"ok\":").Append(c.PieceOk.GetValueOrDefault(p.Id, true) ? "true" : "false")
              .Append(",\"at\":[").Append(ShipDesign.S(w.O[0])).Append(',').Append(ShipDesign.S(w.O[1])).Append(',').Append(ShipDesign.S(w.O[2])).Append("],\"mesh\":");
            m.Write(sb);
            sb.Append('}');
        }
        sb.Append("],\"decks\":[");
        for (var i = 0; i < d.Decks.Count; i++)
            sb.Append(i > 0 ? "," : "").Append("{\"floor\":").Append(ShipDesign.S(d.Decks[i].Floor)).Append(",\"height\":").Append(ShipDesign.S(d.Decks[i].Height)).Append('}');
        sb.Append("],\"rooms\":[");
        for (var i = 0; i < d.Rooms.Count; i++)
        {
            var r = d.Rooms[i];
            sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(r.Id).Append(",\"deck\":").Append(r.Deck)
              .Append(",\"x0\":").Append(ShipDesign.S(r.X0)).Append(",\"x1\":").Append(ShipDesign.S(r.X1)).Append(",\"z0\":").Append(ShipDesign.S(r.Z0)).Append(",\"z1\":").Append(ShipDesign.S(r.Z1))
              .Append(",\"kind\":").Append(Q(r.Kind)).Append(",\"label\":").Append(Q(r.Label)).Append(",\"colour\":").Append(Q(RoomKinds.Find(r.Kind)!.Colour))
              .Append(",\"ok\":").Append(c.RoomOk.GetValueOrDefault(r.Id, true) ? "true" : "false").Append(",\"reached\":").Append(c.Reached.Contains(r.Id) ? "true" : "false").Append('}');
        }
        sb.Append("],\"doors\":[");
        for (var i = 0; i < c.Doors.Count; i++)
            sb.Append(i > 0 ? "," : "").Append('[').Append(ShipDesign.S(c.Doors[i][0])).Append(',').Append(ShipDesign.S(c.Doors[i][1])).Append(',').Append(ShipDesign.S(c.Doors[i][2])).Append(']');
        sb.Append("],\"links\":[");
        for (var i = 0; i < c.Links.Count; i++)
        {
            var (a, b, ok) = c.Links[i];
            sb.Append(i > 0 ? "," : "").Append("{\"a\":[").Append(string.Join(',', a.Select(ShipDesign.S))).Append("],\"b\":[").Append(string.Join(',', b.Select(ShipDesign.S)))
              .Append("],\"ok\":").Append(ok ? "true" : "false").Append('}');
        }
        sb.Append("],\"issues\":[");
        for (var i = 0; i < c.Issues.Count; i++)
            sb.Append(i > 0 ? "," : "").Append("{\"level\":").Append(Q(c.Issues[i].Level)).Append(",\"text\":").Append(Q(c.Issues[i].Text)).Append(",\"what\":").Append(Q(c.Issues[i].What)).Append('}');
        sb.Append("],\"stats\":");
        Stats(d, sb);
        return sb.Append('}').ToString();
    }

    private static void Stats(ShipDesign d, StringBuilder sb)
    {
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
        foreach (var h in d.Hulls)
        {
            var b = h.Bounds();
            (x0, x1, y0, y1, z0, z1) = (Math.Min(x0, b.X0), Math.Max(x1, b.X1), Math.Min(y0, b.Y0), Math.Max(y1, b.Y1), Math.Min(z0, b.Z0), Math.Max(z1, b.Z1));
        }
        var floor = d.Rooms.Sum(r => r.Area);
        var hold = d.Rooms.Where(r => r.Kind == "Hold").Sum(r => r.Area * d.Decks[r.Deck].Height);
        var bunks = d.Rooms.Where(r => r.Kind == "Cabin").Sum(r => Math.Floor(r.Area / 6));
        sb.Append("{\"length\":").Append(ShipDesign.S(z1 - z0)).Append(",\"beam\":").Append(ShipDesign.S(x1 - x0)).Append(",\"height\":").Append(ShipDesign.S(y1 - y0))
          .Append(",\"floor\":").Append(ShipDesign.S(floor)).Append(",\"hold\":").Append(ShipDesign.S(hold)).Append(",\"bunks\":").Append(bunks)
          .Append(",\"rooms\":").Append(d.Rooms.Count).Append(",\"pieces\":").Append(d.Pieces.Sum(p => p.Mirror ? 2 : 1)).Append(",\"hulls\":").Append(d.Hulls.Count).Append('}');
    }

    public static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
            sb.Append(ch switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "", '\t' => " ", _ => ch < ' ' ? "" : ch.ToString() });
        return sb.Append('"').ToString();
    }
}
