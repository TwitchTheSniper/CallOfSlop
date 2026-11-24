using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The designer's edits: one command at a time, as JSON (<c>{"op": "movePiece", "id": 7, ...}</c>), each kept for undo. A drag sends many commands
/// with <c>"merge": true</c> after its first, so one drag is one undo step. The page and the game send the same commands.
/// </summary>
public sealed class DesignEditor
{
    public ShipDesign Design { get; private set; }
    private readonly List<string> _undo = [], _redo = [];
    private const int MaxUndo = 200;

    public DesignEditor(ShipDesign d) => Design = d;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        _redo.Add(Design.ToJson());
        Design = ShipDesign.FromJson(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        _undo.Add(Design.ToJson());
        Design = ShipDesign.FromJson(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    /// <summary>Runs one command; returns "" when it worked, else why it didn't (the design is left as it was).</summary>
    public string Apply(string json)
    {
        JsonObject o;
        try { o = JsonNode.Parse(json)!.AsObject(); }
        catch (Exception e) { return "bad command: " + e.Message; }
        var before = Design.ToJson();
        string why;
        try { why = Run(o); }
        catch (Exception e) { why = e.Message; }
        if (why.Length > 0)
        {
            Design = ShipDesign.FromJson(before);
            return why;
        }
        // Ids are made afresh when a design is read back, so undo keeps the design's text, and the ids stay as long as nothing is undone.
        if (!((bool?)o["merge"] ?? false))
        {
            _undo.Add(before);
            if (_undo.Count > MaxUndo)
                _undo.RemoveAt(0);
            _redo.Clear();
        }
        return "";
    }

    private static double D(JsonObject o, string k, double or = 0) => o[k] is null ? or : (double)o[k]!;
    private static int I(JsonObject o, string k, int or = 0) => o[k] is null ? or : (int)o[k]!;
    private static string Str(JsonObject o, string k, string or = "") => (string?)o[k] ?? or;
    private static double Snap(double v, double step) => step <= 0 ? v : Math.Round(v / step) * step;

    private string Run(JsonObject o)
    {
        var d = Design;
        var op = Str(o, "op");
        HullBody H() => d.Hull(I(o, "hull")) ?? throw new InvalidOperationException("no such hull");
        Piece P() => d.PieceById(I(o, "id")) ?? throw new InvalidOperationException("no such piece");
        Room Rm() => d.RoomById(I(o, "id")) ?? throw new InvalidOperationException("no such room");
        switch (op)
        {
            case "load":
                Design = ShipDesign.FromJson(Str(o, "json"));
                return "";
            case "start":
                Design = DesignTemplates.Start(Str(o, "name", "Zero Day"));
                return "";
            case "rename":
                d.Name = Str(o, "name", d.Name).Trim();
                if (d.Name.Length is 0 or > 60)
                    return "a name is 1 to 60 characters";
                return "";

            // ---- hulls
            case "addHull":
            {
                if (d.Hulls.Count >= ShipDesign.MaxHulls)
                    return $"at most {ShipDesign.MaxHulls} hull bodies";
                var h = DesignTemplates.Body(d, Str(o, "preset", "Main hull"));
                if (o["x"] is not null) { h.X = D(o, "x"); h.Y = D(o, "y"); h.Z = D(o, "z"); }
                d.Hulls.Add(h);
                return "";
            }
            case "connect":
            {
                // A connection lofted from a skin point to another (Karl, 2026-11-24): to a point on a body already there, or to a new body
                // set out along the first point's normal at a distance. Placed again as the page's sliders move: "replace" names the
                // last placing's join and body, which go first.
                if (d.JoinById(I(o, "replaceJoin")) is { } oldJ)
                    d.Joins.Remove(oldJ);
                if (I(o, "replaceHull") is var rh and > 0 && d.Hull(rh) is { } oldH && d.Hulls.Count > 1)
                {
                    d.Hulls.Remove(oldH);
                    d.Pieces.RemoveAll(p => p.Hull == oldH.Id);
                    d.Joins.RemoveAll(j => j.A.Hull == oldH.Id || j.B.Hull == oldH.Id);
                }
                if (d.Joins.Count >= ShipDesign.MaxJoins)
                    return $"at most {ShipDesign.MaxJoins} connections";
                var ao = o["a"]?.AsObject() ?? throw new InvalidOperationException("no first point");
                var ha = d.Hull(I(ao, "hull")) ?? throw new InvalidOperationException("no such hull");
                var j = new HullJoin { Id = d.NewId(), Section = Str(o, "section", "Round"), W = D(o, "w", 1.2), H = D(o, "h", 1.2), Segments = I(o, "segments", 6),
                    Mirror = (bool?)o["mirror"] ?? true, Flare = D(o, "flare", 1.6), SquareHole = (bool?)o["squareHole"] ?? false, Buttress = D(o, "buttress", 0.8), OpenA = D(o, "openA", 0.9), OpenB = D(o, "openB", 0.9) };
                j.A = HullJoin.At(ha, I(ao, "copy"), D(ao, "x"), D(ao, "y"), D(ao, "z"));
                if (o["b"] is JsonObject bo)
                {
                    var hb = d.Hull(I(bo, "hull")) ?? throw new InvalidOperationException("no such hull");
                    j.B = HullJoin.At(hb, I(bo, "copy"), D(bo, "x"), D(bo, "y"), D(bo, "z"));
                }
                else
                {
                    var no = o["body"]?.AsObject() ?? throw new InvalidOperationException("no second point or body");
                    if (d.Hulls.Count >= ShipDesign.MaxHulls)
                        return $"at most {ShipDesign.MaxHulls} hull bodies";
                    var (pa, na) = HullJoin.World(d, j.A)!.Value;
                    var bw = D(no, "w", 3);
                    var bh = D(no, "h", 3);
                    var len = D(no, "len", 12);
                    var body = DesignTemplates.Sized(d, Str(no, "section", "Barrel"), bw, bh, len, I(no, "segments", 4));
                    // Its middle out along the normal: the gap, then half its own reach that way.
                    var reach = Math.Sqrt(Math.Pow(na[0] * bw / 2, 2) + Math.Pow(na[1] * bh / 2, 2));
                    var gap = Math.Clamp(D(no, "dist", 4), 0, 100) + reach;
                    body.X = Math.Round(pa[0] + na[0] * gap, 2);
                    body.Y = Math.Round(pa[1] + na[1] * gap - bh / 2, 2);
                    body.Z = Math.Round(pa[2] - len / 2 + D(no, "slide", 0), 2);
                    body.Mirror = j.Mirror && Math.Abs(body.X) > 0.3;
                    body.Skin = ha.Skin;
                    d.Hulls.Add(body);
                    o["hullId"] = body.Id;
                    // The second end: the new body's skin point that faces the first.
                    j.B = HullJoin.At(body, 0, pa[0], pa[1], pa[2]);
                }
                j.Tidy();
                d.Joins.Add(j);
                o["id"] = j.Id;
                return "";
            }
            case "setJoin":
            {
                var j = d.JoinById(I(o, "id")) ?? throw new InvalidOperationException("no such connection");
                j.Section = Str(o, "section", j.Section);
                j.W = D(o, "w", j.W);
                j.H = D(o, "h", j.H);
                j.Segments = I(o, "segments", j.Segments);
                j.Mirror = (bool?)o["mirror"] ?? j.Mirror;
                j.OpenA = D(o, "openA", j.OpenA);
                j.Flare = D(o, "flare", j.Flare);
                j.SquareHole = (bool?)o["squareHole"] ?? j.SquareHole;
                j.Buttress = D(o, "buttress", j.Buttress);
                j.OpenB = D(o, "openB", j.OpenB);
                j.Tidy();
                return "";
            }
            case "delJoin":
            {
                var j = d.JoinById(I(o, "id")) ?? throw new InvalidOperationException("no such connection");
                d.Joins.Remove(j);
                return "";
            }
            case "copyHull":
            {
                if (d.Hulls.Count >= ShipDesign.MaxHulls)
                    return $"at most {ShipDesign.MaxHulls} hull bodies";
                var h = H().Clone(d.NewId());
                h.Name += " copy";
                h.X += 4;
                d.Hulls.Add(h);
                return "";
            }
            case "delHull":
            {
                if (d.Hulls.Count == 1)
                    return "a ship keeps at least one hull";
                var h = H();
                d.Hulls.Remove(h);
                d.Pieces.RemoveAll(p => p.Hull == h.Id);
                d.Joins.RemoveAll(j => j.A.Hull == h.Id || j.B.Hull == h.Id);
                return "";
            }
            case "moveHull":
            {
                var h = H();
                h.X = Snap(D(o, "x", h.X), 0.05); h.Y = Snap(D(o, "y", h.Y), 0.05); h.Z = Snap(D(o, "z", h.Z), 0.05);
                if (Math.Abs(h.X) > 200 || Math.Abs(h.Y) > 200 || Math.Abs(h.Z) > 400)
                    return "too far from the ship";
                return "";
            }
            case "setHull":
            {
                var h = H();
                if (o["name"] is not null) h.Name = Str(o, "name");
                if (o["mirror"] is not null) h.Mirror = (bool)o["mirror"]!;
                if (o["cut"] is not null) h.Cut = (bool)o["cut"]!;
                if (o["skin"] is not null && Str(o, "skin").Length == 7) h.Skin = Str(o, "skin");
                return "";
            }
            case "moveStation":
            {
                var h = H();
                var i = I(o, "i");
                if (i < 0 || i >= h.Stations.Count)
                    return "no such station";
                var z = Snap(D(o, "z"), 0.05);
                var lo = i == 0 ? -500 : h.Stations[i - 1].Z + HullBody.MinGap;
                var hi = i == h.Stations.Count - 1 ? 500 : h.Stations[i + 1].Z - HullBody.MinGap;
                h.Stations[i].Z = Math.Clamp(z, lo, hi);
                KeepPieces(h);
                return "";
            }
            case "setPoint":
            {
                var h = H();
                var i = I(o, "i");
                var k = I(o, "k");
                if (i < 0 || i >= h.Stations.Count || k < 0 || k >= h.Points)
                    return "no such point";
                var p = h.Stations[i].P;
                var lo = k == 0 ? -100 : p[k - 1].Y + 0.05;
                var hi = k == p.Count - 1 ? 100 : p[k + 1].Y - 0.05;
                if (hi < lo)
                    return "no room between its neighbours";
                p[k] = (Math.Clamp(Snap(D(o, "h"), 0.05), 0.05, 16), Math.Clamp(Snap(D(o, "y"), 0.05), lo, hi));
                if ((bool?)o["all"] ?? false) // the same point on every station (a whole run of the hull)
                    foreach (var s in h.Stations)
                        if (s != h.Stations[i])
                            s.P[k] = (p[k].H, Math.Clamp(p[k].Y, k == 0 ? -100 : s.P[k - 1].Y + 0.05, k == s.P.Count - 1 ? 100 : s.P[k + 1].Y - 0.05));
                h.Tidy();
                return "";
            }
            case "scaleStation":
            {
                var h = H();
                var i = I(o, "i");
                if (i < 0 || i >= h.Stations.Count)
                    return "no such station";
                double sx = D(o, "sx", 1), sy = D(o, "sy", 1);
                if (sx <= 0.05 || sy <= 0.05)
                    return "too small";
                var p = h.Stations[i].P;
                var mid = (p[0].Y + p[^1].Y) / 2;
                for (var k = 0; k < p.Count; k++)
                    p[k] = (p[k].H * sx, mid + (p[k].Y - mid) * sy);
                h.Tidy();
                return "";
            }
            case "addStation":
            {
                var h = H();
                if (h.Stations.Count >= ShipDesign.MaxStations)
                    return $"at most {ShipDesign.MaxStations} stations a body";
                var i = Math.Clamp(I(o, "i"), 0, h.Stations.Count - 1);
                Station s;
                if (i < h.Stations.Count - 1) // halfway to the next
                {
                    var a = h.Stations[i];
                    var b = h.Stations[i + 1];
                    if (b.Z - a.Z < 2 * HullBody.MinGap)
                        return "too close to the next station to split";
                    s = new Station { Z = (a.Z + b.Z) / 2 };
                    for (var k = 0; k < h.Points; k++)
                        s.P.Add(((a.P[k].H + b.P[k].H) / 2, (a.P[k].Y + b.P[k].Y) / 2));
                    h.Stations.Insert(i + 1, s);
                }
                else // a new segment past the end, the same shape
                {
                    var len = D(o, "len", 4);
                    s = h.Stations[^1].Clone();
                    s.Z += Math.Max(HullBody.MinGap, len);
                    h.Stations.Add(s);
                }
                if (Str(o, "preset").Length > 0)
                    ApplySection(h, h.Stations.IndexOf(s), Str(o, "preset"));
                h.Tidy();
                return "";
            }
            case "addStationFront":
            {
                var h = H();
                if (h.Stations.Count >= ShipDesign.MaxStations)
                    return $"at most {ShipDesign.MaxStations} stations a body";
                var s = h.Stations[0].Clone();
                s.Z -= Math.Max(HullBody.MinGap, D(o, "len", 4));
                h.Stations.Insert(0, s);
                if (Str(o, "preset").Length > 0)
                    ApplySection(h, 0, Str(o, "preset"));
                h.Tidy();
                return "";
            }
            case "delStation":
            {
                var h = H();
                if (h.Stations.Count <= 2)
                    return "a body keeps at least two stations";
                var i = I(o, "i");
                if (i < 0 || i >= h.Stations.Count)
                    return "no such station";
                h.Stations.RemoveAt(i);
                KeepPieces(h);
                return "";
            }
            case "section":
            {
                var h = H();
                var i = I(o, "i");
                if (i < 0 || i >= h.Stations.Count)
                    return "no such station";
                ApplySection(h, i, Str(o, "preset"));
                h.Tidy();
                return "";
            }
            case "points":
            {
                var h = H();
                var n = I(o, "n");
                if (n < HullBody.MinPoints || n > HullBody.MaxPoints)
                    return $"a profile has {HullBody.MinPoints} to {HullBody.MaxPoints} points";
                foreach (var s in h.Stations)
                    s.P = DesignTemplates.Resample(s.P, n);
                h.Tidy();
                return "";
            }

            // ---- pieces
            case "addPiece":
            {
                if (d.Pieces.Count >= ShipDesign.MaxPieces)
                    return $"at most {ShipDesign.MaxPieces} pieces";
                var kind = PieceKinds.Find(Str(o, "kind")) ?? throw new InvalidOperationException("no such piece");
                var p = new Piece { Id = d.NewId(), Kind = kind.Name, Mirror = (bool?)o["mirror"] ?? false, Pair = (bool?)o["pair"] ?? true };
                d.Pieces.Add(p);
                Attach(p, o);
                o["id"] = p.Id;
                return "";
            }
            case "movePiece":
                Attach(P(), o);
                return "";
            case "setPiece":
            {
                var p = P();
                if (o["mirror"] is not null) p.Mirror = (bool)o["mirror"]!;
                if (o["pair"] is not null) p.Pair = (bool)o["pair"]!;
                if (o["turn"] is not null) p.Turn = Snap(D(o, "turn"), 5) % 360;
                if (o["room"] is not null)
                {
                    var rid = I(o, "room");
                    if (rid != 0 && d.RoomById(rid) is null)
                        return "no such room";
                    p.Room = rid;
                }
                return "";
            }
            case "autoRoom":
                return AutoRoom(P());
            case "pastePiece":
            {
                // A copy of a piece, moved along its hull (Ctrl+V).
                if (d.Pieces.Count >= ShipDesign.MaxPieces)
                    return $"at most {ShipDesign.MaxPieces} pieces";
                var src = P();
                var h = d.Hull(src.Hull)!;
                var p = Piece.FromJson(src.ToJson(), d.NewId());
                p.Hull = src.Hull;
                p.Z = Math.Clamp(p.Z + D(o, "dz", 2), h.Front, h.Back);
                p.U += p.Surface == "skin" ? 0 : D(o, "dz", 2);
                d.Pieces.Add(p);
                return "";
            }
            case "pasteRoom":
            {
                if (d.Rooms.Count >= ShipDesign.MaxRooms)
                    return $"at most {ShipDesign.MaxRooms} rooms";
                var src = Rm();
                var r = Room.FromJson(src.ToJson(), d.NewId());
                var len = r.Z1 - r.Z0;
                r.Z0 += len; r.Z1 += len; // laid next to it, sharing a wall
                d.Rooms.Add(r);
                return "";
            }
            case "delPiece":
                d.Pieces.Remove(P());
                return "";

            // ---- decks and rooms
            case "orient":
            {
                // Lay the decks the other way: the rooms go (undo brings them back), and fresh decks fill the first hull.
                var stacked = (bool?)o["stacked"] ?? !d.Stacked;
                if (stacked == d.Stacked)
                    return "";
                d.Stacked = stacked;
                d.Rooms.Clear();
                d.Decks.Clear();
                var b = d.Hulls[0].Bounds();
                if (!stacked)
                    for (var f = Math.Ceiling((b.Y0 + 0.4) * 4) / 4; f + 3 < b.Y1 - 0.3 && d.Decks.Count < 3; f += 3.25)
                        d.Decks.Add(new Deck { Floor = f, Height = 3 });
                else
                    for (var f = Math.Floor((b.Z1 - 1.5) * 4) / 4; f - 3 > b.Z0 + 2 && d.Decks.Count < ShipDesign.MaxDecks; f -= 3.25)
                        d.Decks.Add(new Deck { Floor = f, Height = 3 });
                if (d.Decks.Count == 0)
                    d.Decks.Add(new Deck { Floor = stacked ? b.Z1 - 1 : 0.25, Height = 3 });
                return "";
            }
            case "addDeck":
            {
                if (d.Decks.Count >= ShipDesign.MaxDecks)
                    return $"at most {ShipDesign.MaxDecks} decks";
                if (d.Stacked)
                {
                    var fore = d.Decks.Count == 0 ? null : d.Decks.MinBy(k => k.Floor);
                    d.Decks.Add(new Deck { Floor = fore is null ? d.Hulls[0].Bounds().Z1 - 1 : fore.Floor - fore.Height - 0.25, Height = fore?.Height ?? 3 });
                    return "";
                }
                var top = d.Decks.Count == 0 ? null : d.Decks.MaxBy(k => k.Floor);
                d.Decks.Add(new Deck { Floor = top is null ? 0.25 : top.Ceiling + 0.25, Height = top?.Height ?? 3 });
                return "";
            }
            case "setDeck":
            {
                var i = I(o, "deck");
                if (i < 0 || i >= d.Decks.Count)
                    return "no such deck";
                if (o["floor"] is not null) d.Decks[i].Floor = Snap(D(o, "floor"), 0.05);
                if (o["height"] is not null) d.Decks[i].Height = Math.Clamp(Snap(D(o, "height"), 0.05), 2.2, 8);
                return "";
            }
            case "delDeck":
            {
                var i = I(o, "deck");
                if (i < 0 || i >= d.Decks.Count)
                    return "no such deck";
                d.Decks.RemoveAt(i);
                d.Rooms.RemoveAll(r => r.Deck == i);
                foreach (var r in d.Rooms)
                    if (r.Deck > i)
                        r.Deck--;
                return "";
            }
            case "addRoom":
            {
                if (d.Rooms.Count >= ShipDesign.MaxRooms)
                    return $"at most {ShipDesign.MaxRooms} rooms";
                var deck = I(o, "deck");
                if (deck < 0 || deck >= d.Decks.Count)
                    return "no such deck";
                var r = new Room { Id = d.NewId(), Deck = deck, Kind = RoomKinds.Find(Str(o, "kind"))?.Name ?? "Cabin" };
                SetBox(r, o);
                d.Rooms.Add(r);
                o["id"] = r.Id;
                return "";
            }
            case "setRoom":
            {
                var r = Rm();
                SetBox(r, o);
                if (o["kind"] is not null) r.Kind = RoomKinds.Find(Str(o, "kind"))?.Name ?? r.Kind;
                if (o["label"] is not null) r.Label = Str(o, "label");
                if (o["toHull"] is not null) r.ToHull = (bool)o["toHull"]!;
                // Its own height, snapped to 25 cm; its deck's height (or 0) clears it.
                if (o["height"] is not null)
                {
                    var hgt = Snap(Math.Clamp(D(o, "height"), 2.2, 40), 0.25);
                    r.Height = Math.Abs(hgt - d.Decks[r.Deck].Height) < 0.01 || D(o, "height") <= 0 ? 0 : hgt;
                }
                if (o["deck"] is not null && I(o, "deck") >= 0 && I(o, "deck") < d.Decks.Count) r.Deck = I(o, "deck");
                return "";
            }
            case "splitRoom":
            {
                // A room cut in two across its longer side; the parts stay one room (a group), each sized on its own.
                if (d.Rooms.Count >= ShipDesign.MaxRooms)
                    return $"at most {ShipDesign.MaxRooms} rooms";
                var r = Rm();
                var alongX = r.X1 - r.X0 >= r.Z1 - r.Z0;
                var len = alongX ? r.X1 - r.X0 : r.Z1 - r.Z0;
                if (len < 2)
                    return "a room needs 2 m to split (each part keeps a metre)";
                if (r.Group == 0)
                    r.Group = d.NewId();
                var n = Room.FromJson(r.ToJson(), d.NewId());
                n.Height = r.Height;
                var mid = Snap((alongX ? r.X0 : r.Z0) + len / 2, 0.25);
                if (alongX) { r.X1 = mid; n.X0 = mid; } else { r.Z1 = mid; n.Z0 = mid; }
                d.Rooms.Add(n);
                o["id"] = n.Id;
                return "";
            }
            case "delRoom":
            {
                var r = Rm();
                d.Rooms.Remove(r);
                foreach (var p in d.Pieces)
                    if (p.Room == r.Id)
                        p.Room = 0;
            }
                return "";
            default:
                return "unknown command " + op;
        }
    }

    /// <summary>
    /// An airlock's chamber: an Airlock room 2.5 m square behind a socket on a side, on the deck its door opens onto, set in from the skin
    /// until it fits (no room is made when one is already in reach).
    /// </summary>
    private string AutoRoom(Piece p)
    {
        var d = Design;
        var kind = PieceKinds.Find(p.Kind)!;
        var (at, n) = DesignScene.Spots(d, p).First();
        // The door's place and its outward normal on the deck's plane (u across, v the floor's other way), and its height off the floor.
        var (pu, pv, nu, nv, nUp) = d.Stacked ? (at[0], at[1], n[0], n[1], n[2]) : (at[0], at[2], n[0], n[2], n[1]);
        if (Math.Abs(nUp) > 0.7)
            return "an airlock room goes behind a door in a wall; through a floor or a ceiling, lay the room yourself";
        var deck = d.Decks.FindIndex(k => d.Stacked ? at[2] <= k.Floor + 0.6 && at[2] >= k.Floor - k.Height : at[1] >= k.Floor - 0.6 && at[1] <= k.Ceiling);
        if (deck < 0)
            return "the door is on no deck: move it to a deck";
        var dk = d.Decks[deck];
        var across = Math.Abs(nu) >= Math.Abs(nv);
        var sgn = Math.Sign(across ? nu : nv);
        for (var off = 0.25; off <= 3.01; off += 0.25)
        {
            double edge = Snap((across ? pu : pv) - sgn * off, 0.25), far = edge - sgn * 2.5, mid = Snap(across ? pv : pu, 0.25);
            var r = across ? new Room { X0 = edge, X1 = far, Z0 = mid - 1.25, Z1 = mid + 1.25 } : new Room { Z0 = edge, Z1 = far, X0 = mid - 1.25, X1 = mid + 1.25 };
            r.Normalise();
            var fits = true;
            foreach (var up in new[] { 0.05, dk.Height - 0.02 })
                foreach (var u in new[] { r.X0, r.X1 })
                    foreach (var v in new[] { r.Z0, r.Z1 })
                    {
                        var w = d.World(dk, u, v, up);
                        fits &= d.Hulls.Any(h => h.Depth(w[0], w[1], w[2]) >= 0.06);
                    }
            if (!fits)
                continue;
            if (d.Rooms.Any(o => o.Deck == deck && Math.Min(r.X1, o.X1) - Math.Max(r.X0, o.X0) > 0.01 && Math.Min(r.Z1, o.Z1) - Math.Max(r.Z0, o.Z0) > 0.01))
                return ""; // a room is already there: the airlock opens into it
            r.Id = d.NewId();
            r.Deck = deck;
            r.Kind = "Airlock room";
            d.Rooms.Add(r);
            return "";
        }
        return $"no 2.5 m airlock room fits behind the {kind.Name} on deck {deck + 1}";
    }

    private static void SetBox(Room r, JsonObject o)
    {
        if (o["x0"] is not null) r.X0 = Snap(D(o, "x0"), 0.25);
        if (o["x1"] is not null) r.X1 = Snap(D(o, "x1"), 0.25);
        if (o["z0"] is not null) r.Z0 = Snap(D(o, "z0"), 0.25);
        if (o["z1"] is not null) r.Z1 = Snap(D(o, "z1"), 0.25);
        r.Normalise();
    }

    private static void ApplySection(HullBody h, int i, string preset)
    {
        var sec = Array.Find(DesignTemplates.Sections, s => s.Name == preset);
        if (sec.P is null)
            throw new InvalidOperationException("no such section shape");
        h.Stations[i].P = DesignTemplates.Resample(sec.P, h.Points);
    }

    /// <summary>
    /// Puts a piece where the page's pointer met a hull: <c>hull</c>, <c>copy</c>, <c>surface</c> (skin, fore, aft) and the world point <c>x y z</c>.
    /// </summary>
    private void Attach(Piece p, JsonObject o)
    {
        var h = Design.Hull(I(o, "hull", p.Hull)) ?? throw new InvalidOperationException("no such hull");
        var kind = PieceKinds.Find(p.Kind)!;
        var surface = Str(o, "surface", "skin");
        if (kind.Where != "skin" && surface == "skin")
            throw new InvalidOperationException($"a {p.Kind} goes on an end of a hull (its stern or bow face)");
        p.Hull = h.Id;
        p.Copy = Math.Clamp(I(o, "copy"), 0, h.Copies - 1);
        p.Surface = surface;
        var lx = D(o, "x") - h.CopyX(p.Copy);
        var ly = D(o, "y") - h.Y;
        var lz = D(o, "z") - h.Z;
        if (surface == "skin")
        {
            p.Z = Snap(Math.Clamp(lz, h.Front, h.Back), 0.1);
            p.Side = lx < -0.01 ? -1 : 1;
            p.T = Math.Round(h.NearestT(lx, ly, p.Z), 4);
        }
        else
        {
            p.U = Snap(lx, 0.1);
            p.V = Snap(ly, 0.1);
        }
    }

    /// <summary>After a station moves, a piece past the body's ends stays on its end.</summary>
    private void KeepPieces(HullBody h)
    {
        foreach (var p in Design.Pieces)
            if (p.Hull == h.Id && p.Surface == "skin")
                p.Z = Math.Clamp(p.Z, h.Front, h.Back);
    }
}
