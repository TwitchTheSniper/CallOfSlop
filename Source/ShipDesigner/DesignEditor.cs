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
                var j = new HullJoin { Id = d.NewId(), Section = Str(o, "section", "Round"), WA = D(o, "w", 1.2), HA = D(o, "h", 1.2), Segments = I(o, "segments", 6),
                    Twist = D(o, "twist", 0), RotA = D(o, "rotA", 0), RotB = D(o, "rotB", 0), SweepUp = D(o, "sweepUp", 0), SweepFore = D(o, "sweepFore", 0), Bias = D(o, "bias", 0),
                    Mirror = (bool?)o["mirror"] ?? false, Flare = D(o, "flare", 1.6), SquareHole = (bool?)o["squareHole"] ?? false, Buttress = D(o, "buttress", 0.8), Follow = (bool?)o["follow"] ?? false, OpenA = D(o, "openA", 0.9), OpenB = D(o, "openB", 0.9),
                    LeaveA = D(o, "leaveA", 0), LeaveB = D(o, "leaveB", 0), LeaveForeA = D(o, "leaveForeA", 0), LeaveForeB = D(o, "leaveForeB", 0), TanA = D(o, "tanA", 0.9), TanB = D(o, "tanB", 0.9),
                    Collar = (bool?)o["collar"] ?? true, CollarHeight = D(o, "collarHeight", 0.22), CollarMargin = D(o, "collarMargin", 0.25), CollarBevel = D(o, "collarBevel", 0.05) };
                j.WB = D(o, "wb", j.WA);
                j.HB = D(o, "hb", j.HA);
                j.A = HullJoin.At(ha, I(ao, "copy"), D(ao, "x"), D(ao, "y"), D(ao, "z"), (string?)ao["face"]);
                if (o["b"] is JsonObject bo)
                {
                    var hb = d.Hull(I(bo, "hull")) ?? throw new InvalidOperationException("no such hull");
                    j.B = HullJoin.At(hb, I(bo, "copy"), D(bo, "x"), D(bo, "y"), D(bo, "z"), (string?)bo["face"]);
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
                    // Its middle out along the normal: the gap, then half its own reach that way (along z too, off a cap).
                    var reach = Math.Sqrt(Math.Pow(na[0] * bw / 2, 2) + Math.Pow(na[1] * bh / 2, 2) + Math.Pow(na[2] * len / 2, 2));
                    var gap = Math.Clamp(D(no, "dist", 4), 0, 100) + reach;
                    body.X = Math.Round(pa[0] + na[0] * gap, 2);
                    body.Y = Math.Round(pa[1] + na[1] * gap - bh / 2, 2);
                    body.Z = Math.Round(pa[2] + na[2] * gap - len / 2 + D(no, "slide", 0), 2);
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
                j.WA = D(o, "w", j.WA);
                j.HA = D(o, "h", j.HA);
                j.WB = D(o, "wb", j.WB);
                j.HB = D(o, "hb", j.HB);
                j.Twist = D(o, "twist", j.Twist);
                j.RotA = D(o, "rotA", j.RotA);
                j.RotB = D(o, "rotB", j.RotB);
                j.SweepUp = D(o, "sweepUp", j.SweepUp);
                j.SweepFore = D(o, "sweepFore", j.SweepFore);
                j.Bias = D(o, "bias", j.Bias);
                j.LeaveA = D(o, "leaveA", j.LeaveA);
                j.LeaveB = D(o, "leaveB", j.LeaveB);
                j.LeaveForeA = D(o, "leaveForeA", j.LeaveForeA);
                j.LeaveForeB = D(o, "leaveForeB", j.LeaveForeB);
                j.TanA = D(o, "tanA", j.TanA);
                j.TanB = D(o, "tanB", j.TanB);
                j.Segments = I(o, "segments", j.Segments);
                j.Mirror = (bool?)o["mirror"] ?? j.Mirror;
                j.OpenA = D(o, "openA", j.OpenA);
                j.Flare = D(o, "flare", j.Flare);
                j.SquareHole = (bool?)o["squareHole"] ?? j.SquareHole;
                j.Buttress = D(o, "buttress", j.Buttress);
                j.Follow = (bool?)o["follow"] ?? j.Follow;
                j.OpenB = D(o, "openB", j.OpenB);
                j.Collar = (bool?)o["collar"] ?? j.Collar;
                j.CollarHeight = D(o, "collarHeight", j.CollarHeight);
                j.CollarMargin = D(o, "collarMargin", j.CollarMargin);
                j.CollarBevel = D(o, "collarBevel", j.CollarBevel);
                if (o["clad"] is not null)
                {
                    if (!HullBody.CladKinds.Contains(Str(o, "clad")))
                        return $"unknown cladding {Str(o, "clad")}";
                    j.Clad = Str(o, "clad");
                }
                if (o["cladSeed"] is not null) j.CladSeed = Math.Clamp(I(o, "cladSeed"), 0, 99999);
                if (o["cladSize"] is not null) j.CladSize = Math.Clamp(Snap(D(o, "cladSize"), 0.05), HullBody.CladSizeMin, HullBody.CladSizeMax);
                if (o["cladDepth"] is not null) j.CladDepth = Math.Clamp(Snap(D(o, "cladDepth"), 0.05), HullBody.CladDepthMin, HullBody.CladDepthMax);
                if (o["buttressClad"] is not null)
                {
                    if (!HullBody.CladKinds.Contains(Str(o, "buttressClad")))
                        return $"unknown buttress cladding {Str(o, "buttressClad")}";
                    j.ButtressClad = Str(o, "buttressClad");
                }
                if (o["buttressSeed"] is not null) j.ButtressSeed = Math.Clamp(I(o, "buttressSeed"), 0, 99999);
                if (o["buttressSize"] is not null) j.ButtressSize = Math.Clamp(Snap(D(o, "buttressSize"), 0.05), HullBody.CladSizeMin, HullBody.CladSizeMax);
                if (o["matchClad"] is not null) j.MatchClad = (bool)o["matchClad"]!;
                if (o["matchButtress"] is not null) j.MatchButtress = (bool)o["matchButtress"]!;
                if (o["matchColour"] is not null) j.MatchColour = (bool)o["matchColour"]!;
                j.SetOwnPaint(o["paint"] as JsonObject);
                if (o["buttressDepth"] is not null) j.ButtressDepth = Math.Clamp(Snap(D(o, "buttressDepth"), 0.05), HullBody.CladDepthMin, HullBody.CladDepthMax);
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
            case "pasteHullStyle":
            {
                // Another body's settings onto this one (Karl, 2026-11-27): profile, cladding, beading, paint and the rest, not its place,
                // name or mirroring.
                var h = H();
                var from = d.Hull(I(o, "from")) ?? throw new InvalidOperationException("no such hull to copy from");
                if (from == h)
                    return "";
                h.PasteStyle(from);
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
                foreach (var o2 in d.Hulls)
                    if (o2.CladFrom == h.Id)
                        o2.CladFrom = 0;
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
                if (o["clad"] is not null)
                {
                    if (!HullBody.CladKinds.Contains(Str(o, "clad")))
                        return "cladding is Off, Plates, Heavy or Hex";
                    h.Clad = Str(o, "clad");
                }
                if (o.ContainsKey("cladFrom"))
                {
                    var f = o["cladFrom"] is null ? 0 : I(o, "cladFrom");
                    if (f == 0) h.CladFrom = 0;
                    else if (d.Hull(f) is not { } src) return "no such body to match";
                    else if (!d.CanMatchClad(h, src)) return "that would match the cladding round in a loop";
                    else h.CladFrom = f;
                }
                if (o["cladSeed"] is not null) h.CladSeed = Math.Clamp(I(o, "cladSeed"), 0, 99999);
                if (o["cladSize"] is not null) h.CladSize = Math.Clamp(Snap(D(o, "cladSize"), 0.05), HullBody.CladSizeMin, HullBody.CladSizeMax);
                if (o["roundFore"] is not null) h.RoundFore = Math.Clamp(Snap(D(o, "roundFore"), 0.05), 0, HullBody.RoundMax);
                if (o["roundAft"] is not null) h.RoundAft = Math.Clamp(Snap(D(o, "roundAft"), 0.05), 0, HullBody.RoundMax);
                if (o["accent"] is not null && Str(o, "accent").Length == 7) h.Accent = Str(o, "accent");
                if (o["accentAmount"] is not null) h.AccentAmount = Math.Clamp(D(o, "accentAmount"), 0, 1);
                if (o["pattern"] is not null)
                {
                    if (!HullBody.PaintPatterns.Contains(Str(o, "pattern")))
                        return "the pattern is Blocks, Stripes or Bands";
                    h.Pattern = Str(o, "pattern");
                }
                if (o["stripe"] is not null) h.Stripe = Math.Clamp(Snap(D(o, "stripe"), 0.05), HullBody.StripeMin, HullBody.StripeMax);
                if (o["wear"] is not null) h.Wear = Math.Clamp(D(o, "wear"), 0, 1);
                if (o["paintSeed"] is not null) h.PaintSeed = Math.Clamp(I(o, "paintSeed"), 0, 99999);
                if (o["cladDepth"] is not null) h.CladDepth = Math.Clamp(Snap(D(o, "cladDepth"), 0.05), HullBody.CladDepthMin, HullBody.CladDepthMax);
                if (o["cladFore"] is not null) h.CladFore = (bool?)o["cladFore"] ?? true;
                if (o["cladSmooth"] is not null) h.CladSmooth = (bool?)o["cladSmooth"] ?? false;
                if (o["bead"] is not null)
                {
                    if (!HullBody.BeadKinds.Contains(Str(o, "bead")))
                        return "the beading is Off, Creases or All";
                    h.Bead = Str(o, "bead");
                }
                if (o["beadSize"] is not null) h.BeadSize = Math.Clamp(Snap(D(o, "beadSize"), 0.005), HullBody.BeadSizeMin, HullBody.BeadSizeMax);
                if (o["beadColour"] is not null) h.BeadColour = Str(o, "beadColour") is { Length: 7 } bc && bc[0] == '#' ? bc.ToLowerInvariant() : "";
                if (o["cladAft"] is not null) h.CladAft = (bool?)o["cladAft"] ?? true;
                if (o["material"] is not null)
                {
                    if (!HullBody.Materials.Contains(Str(o, "material")))
                        return "unknown material " + Str(o, "material");
                    h.Material = Str(o, "material");
                }
                if (o["cladMaterial"] is not null)
                {
                    if (Str(o, "cladMaterial") != "Hull" && !HullBody.Materials.Contains(Str(o, "cladMaterial")))
                        return "unknown cladding material " + Str(o, "cladMaterial");
                    h.CladMaterial = Str(o, "cladMaterial");
                }
                if (o["damage"] is not null) h.Damage = Math.Clamp(Math.Round(D(o, "damage"), 2), 0, 1);
                if (o["damageSeed"] is not null) h.DamageSeed = Math.Clamp(I(o, "damageSeed"), 0, 99999);
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
                if (i < 0 || i >= h.Stations.Count || k < 0 || k >= h.Stations[i].P.Count)
                    return "no such point";
                var p = h.Stations[i].P;
                var lo = k == 0 ? -100 : p[k - 1].Y + 0.05;
                var hi = k == p.Count - 1 ? 100 : p[k + 1].Y - 0.05;
                if (hi < lo)
                    return "no room between its neighbours";
                p[k] = (Math.Clamp(Snap(D(o, "h"), 0.05), 0.05, 16), Math.Clamp(Snap(D(o, "y"), 0.05), lo, hi));
                if ((bool?)o["all"] ?? false) // the same point on every station (a whole run of the hull)
                    foreach (var s in h.Stations)
                        if (s != h.Stations[i] && k < s.P.Count)
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
                    var bp = b.P.Count == a.P.Count ? b.P : DesignTemplates.Resample(b.P, a.P.Count); // the new station keeps the fore one's count
                    for (var k = 0; k < a.P.Count; k++)
                        s.P.Add(((a.P[k].H + bp[k].H) / 2, (a.P[k].Y + bp[k].Y) / 2));
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
                var only = I(o, "i", -1); // one station, or every station when none is given
                if (only >= h.Stations.Count)
                    return "no such station";
                foreach (var s in h.Stations)
                    if (only < 0 || s == h.Stations[only])
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
                var p = new Piece { Id = d.NewId(), Kind = kind.Name, Mirror = (bool?)o["mirror"] ?? false, Pair = (bool?)o["pair"] ?? true, Size = Math.Clamp(D(o, "size", 1), 0.25, 4), Recess = Math.Clamp(D(o, "recess", 0), 0, 2) };
                d.Pieces.Add(p);
                Attach(p, o);
                DesignHangar.Init(d, p);
                DesignSeats.Init(d, p);
                DesignLightBars.Init(p);
                PieceWeapons.Init(p); // a weapon socket's weapon (Karl, 2026-11-27)
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
                if (o["size"] is not null) p.Size = Math.Clamp(Snap(D(o, "size"), 0.05), 0.25, 4);
                if (o["recess"] is not null) p.Recess = Math.Clamp(Snap(D(o, "recess"), 0.05), 0, 2);
                if (o["clad"] is not null) p.Clad = Math.Clamp(Snap(D(o, "clad"), 0.05), 0, 2);
                if (o["recessClad"] is not null) p.RecessClad = (bool)o["recessClad"]!;
                if (o["cladHull"] is not null) p.CladHull = (bool)o["cladHull"]!;
                if (o["revealClad"] is not null) p.RevealClad = (bool)o["revealClad"]!;
                if (o["turn"] is not null) p.Turn = Snap(D(o, "turn"), 5) % 360;
                if (o["gap"] is not null) p.Gap = D(o, "gap") < 0 ? -1 : Math.Clamp(Snap(D(o, "gap"), 0.01), 0, 2);
                if (o["hole"] is not null) p.Hole = Math.Clamp(Snap(D(o, "hole"), 0.05), 0.5, 2.5);
                if (o["depth"] is not null) p.Depth = Math.Clamp(Snap(D(o, "depth"), 0.01), 0.05, 1);
                if (o["double"] is not null) p.Double = (bool)o["double"]!;
                if (o["airW"] is not null) p.AirW = Math.Clamp(Snap(D(o, "airW"), 0.05), 0.8, 6);
                if (o["airH"] is not null) p.AirH = Math.Clamp(Snap(D(o, "airH"), 0.05), 1.2, 6);
                if (o["airD"] is not null) p.AirD = Math.Clamp(Snap(D(o, "airD"), 0.05), 0.6, 6);
                if (o["doorW"] is not null) p.DoorW = Math.Clamp(Snap(D(o, "doorW"), 0.05), 0.6, 6);
                if ((string?)o["door"] is { } door && Array.IndexOf(PieceDocking.Doors, door) >= 0) p.Door = door;
                if ((string?)o["fit"] is { } fit)
                {
                    if (fit.Length > 0 && !PieceWeapons.Fits(p.Kind, fit))
                        return "that weapon doesn't fit this mount";
                    p.Fit = fit.Length == 0 && PieceWeapons.MountOf(p.Kind)?.Only is { } only ? only : fit; // (a weapon socket keeps its weapon)
                }
                if (o["feed"] is not null)
                {
                    var fid = I(o, "feed");
                    if (fid != 0 && (d.Pieces.Find(q => q.Id == fid) is not { } f || PieceKinds.Find(f.Kind)?.Group != "Weapons" || PieceWeapons.IsFeeder(f.Kind)))
                        return "no such weapon";
                    p.Feed = fid;
                }
                if (DesignHangar.Set(d, p, o) is { Length: > 0 } herr)
                    return herr;
                if (DesignFittings.Set(p, o) is { Length: > 0 } ferr)
                    return ferr;
                if (p.Kind == DesignRamp.Kind && DesignRamp.Set(p, o) is { Length: > 0 } rerr)
                    return rerr;
                if (DesignSeats.IsSeat(p.Kind) && DesignSeats.Set(p, o) is { Length: > 0 } serr)
                    return serr;
                if (DesignLightBars.Set(p, o) is { Length: > 0 } lerr)
                    return lerr;
                if (o["room"] is not null)
                {
                    var rid = I(o, "room");
                    if (rid != 0 && d.RoomById(rid) is null)
                        return "no such room";
                    p.Room = rid;
                }
                return "";
            }
            case "centrePiece":
            {
                // Onto the ship's centreline (Karl, 2026-11-24): on a hull's end, across to x 0; on the skin, to the keel's or the
                // spine's middle, whichever is nearer. One piece there, so no mirrored twin.
                var p = P();
                var h = d.Hull(p.Hull) ?? throw new InvalidOperationException("no such hull");
                if (p.Surface is "aft" or "fore")
                    p.U = Math.Round(-h.CopyX(Math.Min(p.Copy, h.Copies - 1)), 3);
                else
                    p.T = p.T < 0.5 ? 0 : 1;
                p.Mirror = false;
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
                // A deck in a body (Karl, 2026-11-26: a side pod's own decks): the first goes 40 cm over its keel, the next over the
                // last of that body's; its rooms clip to that body.
                if (I(o, "hull") is var bh and > 0)
                {
                    var hb = d.Hull(bh) ?? throw new InvalidOperationException("no such hull");
                    var bb = hb.Bounds();
                    var own = d.Decks.Where(k => k.Hull == hb.Id).ToList();
                    Deck nd;
                    if (d.Stacked)
                    {
                        var fore = own.MinBy(k => k.Floor);
                        nd = new Deck { Floor = fore is null ? Snap(bb.Z1 - 1, 0.25) : fore.Floor - fore.Height - 0.25, Height = fore?.Height ?? 3 };
                    }
                    else
                    {
                        var last = own.MaxBy(k => k.Floor);
                        var room = bb.Y1 - bb.Y0 - 0.8;
                        nd = new Deck { Floor = last is null ? Math.Ceiling((bb.Y0 + 0.4) * 4) / 4 : last.Ceiling + 0.25, Height = last?.Height ?? Math.Clamp(Math.Floor(room * 4) / 4, 2.2, 3) };
                    }
                    nd.Hull = hb.Id;
                    d.Decks.Add(nd);
                    return "";
                }
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
                if (o["by"] is not null) d.Decks[i].Floor = Snap(d.Decks[i].Floor + D(o, "by"), 0.05);
                DesignScene.FitDeck(d, i);
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
                foreach (var k in d.Risers)
                {
                    if (k.From == i) k.From = -1; else if (k.From > i) k.From--;
                    if (k.To == i) k.To = -1; else if (k.To > i) k.To--;
                }
                d.Risers.RemoveAll(k => k.From < 0 || k.To < 0 || k.From == k.To);
                d.Passages.RemoveAll(k => d.RoomById(k.A) is null || d.RoomById(k.B) is null);
                return "";
            }
            case "setDeckHull":
            {
                var i = I(o, "deck");
                if (i < 0 || i >= d.Decks.Count)
                    return "no such deck";
                d.Decks[i].Hull = I(o, "hull") is var sh and > 0 ? (d.Hull(sh) ?? throw new InvalidOperationException("no such hull")).Id : 0;
                return "";
            }
            // ---- passages through an arm between bodies (Karl, 2026-11-26)
            case "addPassage":
            {
                if (d.Passages.Count >= ShipDesign.MaxPassages)
                    return $"at most {ShipDesign.MaxPassages} passages";
                var (a, b) = (d.RoomById(I(o, "a")), d.RoomById(I(o, "b")));
                if (a is null || b is null || a == b)
                    return "pick two rooms";
                if (d.Passages.Any(q => q.Joins(a.Id, b.Id)))
                    return "those rooms already have a passage between them";
                var k = new Passage { Id = d.NewId(), A = a.Id, B = b.Id, Kind = Passage.Kinds.Contains(Str(o, "kind")) ? Str(o, "kind") : "Lift" };
                var (why, lay) = DesignScene.LayPassage(d, k);
                if (lay is null)
                    return why;
                k.Join = lay.Join.Id;
                d.Passages.Add(k);
                o["id"] = k.Id;
                return "";
            }
            case "setPassage":
            {
                var k = d.Passages.Find(q => q.Id == I(o, "id")) ?? throw new InvalidOperationException("no such passage");
                if (o["kind"] is not null && Passage.Kinds.Contains(Str(o, "kind"))) k.Kind = Str(o, "kind");
                return "";
            }
            case "delPassage":
                d.Passages.RemoveAll(q => q.Id == I(o, "id"));
                return "";
            // ---- ladders and lifts between decks (Karl, 2026-11-24)
            case "addRiser":
            {
                if (d.Risers.Count >= ShipDesign.MaxRisers)
                    return $"at most {ShipDesign.MaxRisers} ladders and lifts";
                var k = new Riser { Id = d.NewId(), Kind = Str(o, "kind") == "Lift" ? "Lift" : "Ladder", X = Snap(D(o, "x"), 0.25), Z = Snap(D(o, "z"), 0.25), From = I(o, "from"), To = I(o, "to") };
                if (k.From < 0 || k.To < 0 || k.From >= d.Decks.Count || k.To >= d.Decks.Count || k.From == k.To)
                    return "pick two different decks";
                d.Risers.Add(k);
                return "";
            }
            case "setRiser":
            {
                var k = d.Risers.Find(q => q.Id == I(o, "id")) ?? throw new InvalidOperationException("no such ladder or lift");
                if (o["kind"] is not null) k.Kind = Str(o, "kind") == "Lift" ? "Lift" : "Ladder";
                if (o["x"] is not null) k.X = Snap(D(o, "x"), 0.25);
                if (o["z"] is not null) k.Z = Snap(D(o, "z"), 0.25);
                if (o["w"] is not null) k.W = Math.Clamp((int)Math.Round(D(o, "w")), 1, 8);
                if (o["l"] is not null) k.L = Math.Clamp((int)Math.Round(D(o, "l")), 1, 8);
                var (f, t) = (o["from"] is null ? k.From : I(o, "from"), o["to"] is null ? k.To : I(o, "to"));
                if (f < 0 || t < 0 || f >= d.Decks.Count || t >= d.Decks.Count || f == t)
                    return "pick two different decks";
                (k.From, k.To) = (f, t);
                return "";
            }
            // ---- doors placed between rooms (Karl, 2026-11-24)
            case "addDoor":
            {
                var (a, b) = (d.RoomById(I(o, "a")), d.RoomById(I(o, "b")));
                if (a is null || b is null || a == b)
                    return "pick two rooms";
                // One door a pair of rooms (placing it again moves it); windows, as many as fit.
                var window = Str(o, "kind") == "Window";
                var k = window ? null : d.Doors.Find(q => q.Kind == "Door" && q.Joins(a, b));
                if (k is null)
                    d.Doors.Add(k = new RoomDoor { Id = d.NewId(), A = a.Id, B = b.Id, Kind = window ? "Window" : "Door", W = window ? 1.2 : 0.9, H = window ? 0.8 : 2.1 });
                k.T = Math.Clamp(D(o, "t", k.T), 0, 1);
                return "";
            }
            case "setDoor":
            {
                var k = d.Doors.Find(q => q.Id == I(o, "id")) ?? throw new InvalidOperationException("no such door");
                if (o["t"] is not null) k.T = Math.Clamp(D(o, "t"), 0, 1);
                if (o["w"] is not null) k.W = Math.Clamp(Snap(D(o, "w"), 0.05), 0.3, 6);
                if (o["h"] is not null) k.H = Math.Clamp(Snap(D(o, "h"), 0.05), 0.3, 3);
                if (o["sill"] is not null) k.Sill = Math.Clamp(Snap(D(o, "sill"), 0.05), 0, 3);
                if (o["shape"] is not null && RoomDoor.Shapes.Contains(Str(o, "shape"))) k.Shape = Str(o, "shape");
                return "";
            }
            case "delDoor":
            {
                d.Doors.RemoveAll(q => q.Id == I(o, "id"));
                return "";
            }
            // ---- windows in the hull, on a room's wall that is within a metre of the skin (Karl, 2026-11-24)
            case "addHullWindow":
            {
                var r = d.RoomById(I(o, "room")) ?? throw new InvalidOperationException("no such room");
                var w = new HullWindow { Id = d.NewId(), Room = r.Id, Side = HullWindow.Sides.Contains(Str(o, "side")) ? Str(o, "side") : "x0" };
                var dk = d.DeckOf(r);
                w.H = Math.Min(1.0, dk.Height - 1.2);
                var (_, _, _, gap, ok, len) = DesignScene.Lay(d, w);
                if (!ok)
                    return $"that wall is more than {HullWindow.MaxGap:0} m from the skin: a hull window needs the room within a metre of the hull";
                w.W = Math.Min(1.5, len);
                d.Windows.Add(w);
                o["id"] = w.Id;
                return "";
            }
            case "setHullWindow":
            {
                var w = d.Windows.Find(q => q.Id == I(o, "id")) ?? throw new InvalidOperationException("no such window");
                if (o["t"] is not null) w.T = Math.Clamp(D(o, "t"), 0, 1);
                if (o["w"] is not null) w.W = Math.Clamp(Snap(D(o, "w"), 0.05), 0.3, 40);
                if (o["h"] is not null) w.H = Math.Clamp(Snap(D(o, "h"), 0.05), 0.3, 6);
                if (o["sill"] is not null) w.Sill = Math.Clamp(Snap(D(o, "sill"), 0.05), 0, 6);
                if (o["shape"] is not null && HullWindow.Shapes.Contains(Str(o, "shape"))) w.Shape = Str(o, "shape");
                if (o["gap"] is not null) w.Gap = D(o, "gap") < 0 ? -1 : Math.Clamp(Snap(D(o, "gap"), 0.01), 0, 2);
                if (o["hole"] is not null) w.Hole = Math.Clamp(Snap(D(o, "hole"), 0.05), 0.5, 2.5);
                if (o["depth"] is not null) w.Depth = Math.Clamp(Snap(D(o, "depth"), 0.01), 0.05, 1);
                if (o["double"] is not null) w.Double = (bool)o["double"]!;
                if (o["frameOut"] is not null) w.FrameOut = (bool)o["frameOut"]!;
                if (o["frameIn"] is not null) w.FrameIn = (bool)o["frameIn"]!;
                return "";
            }
            case "delHullWindow":
            {
                d.Windows.RemoveAll(q => q.Id == I(o, "id"));
                return "";
            }
            case "delRiser":
            {
                d.Risers.RemoveAll(q => q.Id == I(o, "id"));
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
                r.KindDefaults();
                SetBox(r, o);
                d.Rooms.Add(r);
                o["id"] = r.Id;
                return "";
            }
            case "setRoom":
            {
                var r = Rm();
                SetBox(r, o);
                // A room in parts is one room: its kind, name and way to the skin go to every part.
                foreach (var q in d.Rooms.Where(q => q == r || (r.Group != 0 && q.Group == r.Group)))
                {
                    if (o["kind"] is not null) { q.Kind = RoomKinds.Find(Str(o, "kind"))?.Name ?? q.Kind; if (o["wallStyle"] is null && o["floor"] is null) q.KindDefaults(); }
                    if (o["label"] is not null) q.Label = Str(o, "label");
                    if (o["toHull"] is not null) q.ToHull = (bool)o["toHull"]!;
                    if (o["runOn"] is not null) q.RunOn = (bool)o["runOn"]!;
                    if (o["runAft"] is not null) q.RunAft = (bool)o["runAft"]!;
                    if (o["wallThick"] is not null) q.WallThick = Math.Clamp(Snap(D(o, "wallThick"), 0.01), 0, Room.WallThickMax);
                    if (o["interior"] is not null) q.Interior = Room.InteriorStyles.FirstOrDefault(k => string.Equals(k, Str(o, "interior"), StringComparison.OrdinalIgnoreCase)) ?? q.Interior;
                    if (o["ribSpacing"] is not null) q.RibSpacing = Math.Clamp(Snap(D(o, "ribSpacing"), 0.25), Room.RibSpacingMin, Room.RibSpacingMax);
                    if (o["panelColour"] is not null) q.PanelColour = Room.Colour(Str(o, "panelColour"), q.PanelColour);
                    if (o["trimColour"] is not null) q.TrimColour = Room.Colour(Str(o, "trimColour"), q.TrimColour);
                    if (o["lights"] is not null) q.Lights = (bool)o["lights"]!;
                    if (o["wallStyle"] is not null) q.WallStyle = Room.WallStyles.FirstOrDefault(k => string.Equals(k, Str(o, "wallStyle"), StringComparison.OrdinalIgnoreCase)) ?? q.WallStyle;
                    if (o["wallColour"] is not null) q.WallColour = Str(o, "wallColour") is { Length: > 0 } wc ? Room.Colour(wc, q.WallColour) : "";
                    if (o["seed"] is not null) q.Seed = Math.Clamp((int)Math.Round(D(o, "seed")), 0, Room.SeedMax);
                    if (o["floor"] is not null) q.Floor = Room.FloorKinds.FirstOrDefault(k => string.Equals(k, Str(o, "floor"), StringComparison.OrdinalIgnoreCase)) ?? q.Floor;
                }
                // Its own height, snapped to 25 cm; its deck's height (or 0) clears it.
                if (o["height"] is not null)
                {
                    var hgt = Snap(Math.Clamp(D(o, "height"), 2.2, 40), 0.25);
                    hgt -= r.Below; // a room merged down through the deck under it counts its height from its own deck's floor
                    r.Height = r.Below == 0 && Math.Abs(hgt - d.Decks[r.Deck].Height) < 0.01 || D(o, "height") <= 0 ? 0 : hgt;
                }
                if (o["deck"] is not null && I(o, "deck") >= 0 && I(o, "deck") < d.Decks.Count) r.Deck = I(o, "deck");
                return "";
            }
            case "mergeRoom":
            {
                // Two rooms made one (Karl, 2026-11-24): when together they make a rectangle, one box; otherwise one group
                // of segments, open to each other with no door. Its sockets, doors and windows move to the room kept.
                var (a, b) = (d.RoomById(I(o, "a")), d.RoomById(I(o, "b")));
                if (a is null || b is null || a == b)
                    return "pick two rooms";
                const double e = 0.02;
                if (a.Deck != b.Deck)
                {
                    // Across decks (Karl, 2026-11-24): one tall room through both, on the deck the player picks ("owner": "a" or "b").
                    // It keeps the owner's plan; the other room goes.
                    if (!d.Neighbours(a.Deck, b.Deck))
                        return "merge rooms on decks next to each other (one at a time)";
                    if (Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0) < 0.5 || Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0) < 0.5)
                        return "they don't overlap from above: line one up over the other first";
                    var own = Str(o, "owner") == "b" ? b : a;
                    var other = own == a ? b : a;
                    var (lo, hi) = d.Under(a.Deck, b.Deck) ? (a, b) : (b, a);
                    var (dl, dh) = (d.DeckOf(lo), d.DeckOf(hi));
                    double bottom = dl.Floor, top = d.Stacked ? dh.Floor - dh.Height : dh.Floor + dh.Height; // world floor of the lower, roof of the upper
                    var span = Math.Abs(top - bottom);
                    var ownFloor = d.Decks[own.Deck].Floor;
                    own.Below = Math.Round(Math.Abs(ownFloor - bottom), 3);
                    own.Height = Math.Round(span - own.Below, 3);
                    d.Doors.RemoveAll(k => k.Joins(a, b));
                    d.Rooms.Remove(other);
                    foreach (var p in d.Pieces)
                        if (p.Room == other.Id)
                            p.Room = own.Id;
                    foreach (var hw in d.Windows.Where(q => q.Room == other.Id))
                        hw.Room = own.Id;
                    foreach (var k in d.Doors)
                    {
                        if (k.A == other.Id) k.A = own.Id;
                        if (k.B == other.Id) k.B = own.Id;
                    }
                    foreach (var k in d.Passages)
                    {
                        if (k.A == other.Id) k.A = own.Id;
                        if (k.B == other.Id) k.B = own.Id;
                    }
                    o["id"] = own.Id;
                    return "";
                }
                var rect = (Math.Abs(a.Z0 - b.Z0) < e && Math.Abs(a.Z1 - b.Z1) < e && (Math.Abs(a.X1 - b.X0) < e || Math.Abs(b.X1 - a.X0) < e))
                        || (Math.Abs(a.X0 - b.X0) < e && Math.Abs(a.X1 - b.X1) < e && (Math.Abs(a.Z1 - b.Z0) < e || Math.Abs(b.Z1 - a.Z0) < e));
                var touch = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0) >= -e && Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0) >= -e;
                if (!touch)
                    return "they don't touch: move one against the other first";
                d.Doors.RemoveAll(k => k.Joins(a, b));
                if (rect)
                {
                    (a.X0, a.X1, a.Z0, a.Z1) = (Math.Min(a.X0, b.X0), Math.Max(a.X1, b.X1), Math.Min(a.Z0, b.Z0), Math.Max(a.Z1, b.Z1));
                    a.Height = Math.Max(a.Height, b.Height);
                    a.Want = null;
                    if (b.Group != 0)
                        foreach (var r in d.Rooms)
                            if (r != b && r.Group == b.Group)
                                r.Group = a.Group != 0 ? a.Group : (a.Group = d.NewId());
                    d.Rooms.Remove(b);
                    foreach (var p in d.Pieces)
                        if (p.Room == b.Id)
                            p.Room = a.Id;
                    foreach (var hw in d.Windows.Where(q => q.Room == b.Id))
                        hw.Room = a.Id;
                    foreach (var k in d.Doors)
                    {
                        if (k.A == b.Id) k.A = a.Id;
                        if (k.B == b.Id) k.B = a.Id;
                    }
                    foreach (var k in d.Passages)
                    {
                        if (k.A == b.Id) k.A = a.Id;
                        if (k.B == b.Id) k.B = a.Id;
                    }
                    return "";
                }
                var g = a.Group != 0 ? a.Group : b.Group != 0 ? b.Group : d.NewId();
                foreach (var r in d.Rooms)
                    if (r == a || r == b || (r.Group != 0 && (r.Group == a.Group || r.Group == b.Group)))
                        (r.Group, r.Kind, r.ToHull) = (g, a.Kind, a.ToHull); // one room: one kind, one way to the skin
                return "";
            }
            // ---- a room grown out of a wall or up through its ceiling (Karl, 2026-11-24; replaces the cubby)
            case "addExt":
            {
                var r = Rm();
                if (r.Exts.Count >= RoomExt.Max)
                    return $"at most {RoomExt.Max} extensions a room";
                var side = Str(o, "side");
                if (!RoomExt.Sides.Contains(side))
                    return "no such side";
                var shape = Str(o, "shape");
                var e = new RoomExt { Side = side, Shape = RoomExt.ShapesFor(side).Contains(shape) ? shape : RoomExt.ShapesFor(side)[0],
                    Depth = Math.Clamp(Snap(D(o, "depth", side == "up" ? 1 : 1.5), 0.05), 0.25, 12), Width = Math.Clamp(Snap(D(o, "width", 2), 0.05), 0.5, 40) };
                r.Exts.Add(e);
                return "";
            }
            case "setExt":
            {
                var r = Rm();
                var i = I(o, "i");
                if (i < 0 || i >= r.Exts.Count)
                    return "no such extension";
                var e = r.Exts[i];
                if (o["shape"] is not null && RoomExt.ShapesFor(e.Side).Contains(Str(o, "shape"))) e.Shape = Str(o, "shape");
                if (o["depth"] is not null) e.Depth = Math.Clamp(Snap(D(o, "depth"), 0.05), 0.25, 12);
                if (o["width"] is not null) e.Width = Math.Clamp(Snap(D(o, "width"), 0.05), 0.5, 40);
                if (o["t"] is not null) e.T = Math.Clamp(D(o, "t"), 0, 1);
                return "";
            }
            case "delExt":
            {
                var r = Rm();
                var i = I(o, "i");
                if (i >= 0 && i < r.Exts.Count)
                    r.Exts.RemoveAt(i);
                return "";
            }
            case "mount":
            {
                // A socket mounted from a room (Karl, 2026-11-24): from a point on the room's wall, floor or ceiling, out along that face's
                // normal to where the line leaves the hull; the piece goes on the skin there and opens into the room. "replace" names the
                // last preview's piece, which goes first.
                if (d.PieceById(I(o, "replace")) is { } old)
                    d.Pieces.Remove(old);
                var room = d.RoomById(I(o, "room")) ?? throw new InvalidOperationException("no such room");
                var kind = PieceKinds.Find(Str(o, "kind")) ?? throw new InvalidOperationException("no such piece");
                if (kind.Where != "skin")
                    throw new InvalidOperationException($"a {kind.Name} goes on an end of a hull, not from a room");
                if (d.Pieces.Count >= ShipDesign.MaxPieces)
                    return $"at most {ShipDesign.MaxPieces} pieces";
                double x = D(o, "x"), y = D(o, "y"), z = D(o, "z"), nx = D(o, "nx"), ny = D(o, "ny"), nz = D(o, "nz");
                var nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-6)
                    return "no direction to cut in";
                (nx, ny, nz) = (nx / nl, ny / nl, nz / nl);
                HullBody? inside = null;
                double ex = 0, ey = 0, ez = 0;
                for (var f = 0.0; f < 60; f += 0.05)
                {
                    double px = x + nx * f, py = y + ny * f, pz = z + nz * f;
                    var h = d.Hulls.FirstOrDefault(b => b.Depth(px, py, pz) > 0);
                    if (h is null)
                    {
                        if (inside is not null)
                            break;
                        continue;
                    }
                    inside = h;
                    (ex, ey, ez) = (px, py, pz);
                }
                if (inside is null)
                    return "no hull that way";
                var p = new Piece { Id = d.NewId(), Kind = kind.Name, Mirror = (bool?)o["mirror"] ?? false, Pair = (bool?)o["pair"] ?? true, Size = Math.Clamp(D(o, "size", 1), 0.25, 4), Recess = Math.Clamp(D(o, "recess", 0), 0, 2), Room = room.Id };
                var copy = inside.Copies == 2 && Math.Abs(ex - inside.CopyX(1)) < Math.Abs(ex - inside.CopyX(0)) ? 1 : 0;
                d.Pieces.Add(p);
                Attach(p, new JsonObject { ["hull"] = inside.Id, ["copy"] = copy, ["surface"] = "skin", ["x"] = ex, ["y"] = ey, ["z"] = ez });
                o["id"] = p.Id;
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
                d.Doors.RemoveAll(q => q.A == r.Id || q.B == r.Id);
                d.Passages.RemoveAll(q => q.A == r.Id || q.B == r.Id);
                d.Windows.RemoveAll(q => q.Room == r.Id);
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
        if (o["x0"] is not null || o["x1"] is not null || o["z0"] is not null || o["z1"] is not null)
            r.Want = null; // drawn anew: this is its plan now
        r.Normalise();
    }

    private static void ApplySection(HullBody h, int i, string preset)
    {
        var sec = Array.Find(DesignTemplates.Sections, s => s.Name == preset);
        if (sec.P is null)
            throw new InvalidOperationException("no such section shape");
        h.Stations[i].P = DesignTemplates.Resample(sec.P, h.Stations[i].P.Count);
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
