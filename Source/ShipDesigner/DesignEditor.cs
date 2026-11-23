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
                var p = new Piece { Id = d.NewId(), Kind = kind.Name, Mirror = (bool?)o["mirror"] ?? false };
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
                if (o["turn"] is not null) p.Turn = Snap(D(o, "turn"), 5) % 360;
                return "";
            }
            case "delPiece":
                d.Pieces.Remove(P());
                return "";

            // ---- decks and rooms
            case "addDeck":
            {
                if (d.Decks.Count >= ShipDesign.MaxDecks)
                    return $"at most {ShipDesign.MaxDecks} decks";
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
                if (o["deck"] is not null && I(o, "deck") >= 0 && I(o, "deck") < d.Decks.Count) r.Deck = I(o, "deck");
                return "";
            }
            case "delRoom":
                d.Rooms.Remove(Rm());
                return "";
            default:
                return "unknown command " + op;
        }
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
