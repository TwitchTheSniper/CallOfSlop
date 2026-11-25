using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Ship Designer's panels (the page's leftPanel, stationPanel, profileEditor, connectSection, joinControls, inspector and
/// checks): the same sections, buttons, sliders and hints, in the same order.
/// </summary>
public sealed partial class Ui
{
    private string _shapePick = "Zero Day beam";
    private string _newBody = "Main hull";

    /// <summary>The Connect tool's settings (the page's cOpt).</summary>
    private sealed class JoinOpt
    {
        public string Section = "Round";
        public double W = 1.2, H = 1.6, Wb = 1.2, Hb = 1.6, Twist, SweepUp, SweepFore, Bias, Flare = 1.6, Buttress = 0.8, OpenA = 0.9, OpenB = 0.9;
        public int Segments = 6;
        public bool Mirror = true, Follow, SquareHole;

        public void Into(JsonObject o)
        {
            o["section"] = Section;
            o["w"] = W;
            o["h"] = H;
            o["wb"] = Wb;
            o["hb"] = Hb;
            o["twist"] = Twist;
            o["sweepUp"] = SweepUp;
            o["sweepFore"] = SweepFore;
            o["bias"] = Bias;
            o["segments"] = Segments;
            o["mirror"] = Mirror;
            o["flare"] = Flare;
            o["buttress"] = Buttress;
            o["follow"] = Follow;
            o["squareHole"] = SquareHole;
            o["openA"] = OpenA;
            o["openB"] = OpenB;
        }
    }

    private readonly JoinOpt _cOpt = new();
    private (string Section, double W, double H, double Len, int Segments, double Dist, double Slide) _cBody = ("Barrel", 3, 3, 12, 4, 5, 0);

    private static JsonObject C(string op, params (string K, JsonNode? V)[] kv)
    {
        var o = new JsonObject { ["op"] = op };
        foreach (var (k, v) in kv)
            o[k] = v;
        return o;
    }

    private static void Checkbox(string label, bool v, Action<bool> set)
    {
        var x = v;
        if (ImGui.Checkbox(label, ref x))
            set(x);
    }

    private static void Row() => ImGui.SameLine();

    /// <summary>Buttons in a row that wrap at the panel's edge.</summary>
    private static bool WrapButton(string label, bool on = false, bool disabled = false)
    {
        var w = ImGui.CalcTextSize(label.Split("##")[0]).X + ImGui.GetStyle().FramePadding.X * 2;
        var last = ImGui.GetItemRectMax().X;
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X;
        if (last + ImGui.GetStyle().ItemSpacing.X + w < right && _wrapInRow)
            ImGui.SameLine();
        _wrapInRow = true;
        return OnButton(label, on, disabled);
    }

    private static bool _wrapInRow;

    private static void EndRow() => _wrapInRow = false;

    private void LeftPanel()
    {
        var rooms = Arr(S, "rooms").ToList();
        if (_sdMode == "hull")
        {
            var hulls = Arr(S, "hulls").ToList();
            H2("Hull bodies", $"{hulls.Count}");
            foreach (var h in hulls)
                if (OnButton($"{Str(h, "name")} - {Arr(h, "stations").Count()} stations{(B(h, "mirror") ? " - pair" : "")}##sdhb{I(h, "id")}", _selKind == "hull" && _selId == I(h, "id")))
                    Select("hull", I(h, "id"));
            ImGui.SetNextItemWidth(150 * _scale);
            if (ImGui.BeginCombo("##sdnewbody"u8, _newBody))
            {
                foreach (var b in Arr(CAT, "bodies"))
                    if (ImGui.Selectable(b.GetString()!, b.GetString() == _newBody))
                        _newBody = b.GetString()!;
                ImGui.EndCombo();
            }
            Row();
            if (ImGui.Button("Add body##sdaddbody"u8))
            {
                Cmd(C("addHull", ("preset", _newBody)));
                Select("hull", I(Arr(S, "hulls").Last(), "id"));
            }
            Hint("Bodies that meet are cut open where they touch (Cut, on each body), so a pod or a dorsal block opens into the main hull.");
            ImGui.Separator();
            ConnectSection();
            var sel = _selKind == "hull" ? ById(hulls, _selId) : default;
            if (Found(sel))
            {
                ImGui.Separator();
                StationPanel(sel);
            }
        }
        else if (_sdMode == "pieces")
        {
            H2("Pieces");
            Hint("Drag onto the hull. Sockets (amber edge) cut their opening through the skin and open into a room inside; an airlock brings its own airlock room.");
            Checkbox("Mirror on its own body##sdpm", _pMirror, v => _pMirror = v);
            Checkbox("On a mirrored pair of bodies, put it on both##sdpp", _pPair, v => _pPair = v);
            Slider("Size to place", Math.Round(_pieceSize * 100), 25, 400, 5, "%", v => _pieceSize = v / 100, "ps");
            Slider("Recess to place", Math.Round(_pieceRecess * 100), 0, 200, 5, " cm", v => _pieceRecess = v / 100, "pr");
            var pieces = Arr(CAT, "pieces").ToList();
            var groups = pieces.Select(p => Str(p, "group")).Distinct().OrderBy(g => g == "Sockets" ? 0 : 1).ToList();
            foreach (var g in groups)
            {
                H2(g == "Sockets" ? "Airlocks, docks and hatches" : g);
                EndRow();
                foreach (var k in pieces.Where(p => Str(p, "group") == g))
                {
                    var name = Str(k, "name");
                    var sock = Has(k, "socket");
                    if (sock)
                        ImGui.PushStyleColor(ImGuiCol.Border, SdAmber);
                    WrapButton($"{name}##sdpal{name}", _paletteDrag?.Kind == name);
                    if (sock)
                        ImGui.PopStyleColor();
                    if (ImGui.IsItemActivated())
                        PaletteStart(name);
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.WrappedTooltip($"{Str(k, "desc")}\n{(sock ? "needs " + Str(k, "needs") : Str(k, "where") == "skin" ? "on the skin" : "on a hull's end")}", 320 * _scale);
                }
                EndRow();
            }
        }
        else
        {
            var decks = Arr(S, "decks").ToList();
            void DeckButtons()
            {
                EndRow();
                for (var i = 0; i < decks.Count; i++)
                    if (WrapButton($"Deck {i + 1}##sddk{i}", i == _sdDeck))
                    {
                        _sdDeck = i;
                        Rebuild();
                    }
                if (WrapButton("+ Deck##sdadddeck"))
                {
                    Cmd(C("addDeck"));
                    _sdDeck = Arr(S, "decks").Count() - 1;
                    Rebuild();
                }
                EndRow();
            }
            var d = _sdDeck < decks.Count ? decks[_sdDeck] : default;
            if (_sdMode == "connectors")
            {
                H2("Decks");
                DeckButtons();
                H2("Ladders and lifts");
                foreach (var k in new[] { "Ladder", "Lift" })
                {
                    var on = _placingRiser is { } pr && pr.Kind == k && pr.Room == 0;
                    if (OnButton($"{k}##sdpr{k}", on))
                    {
                        _placingRiser = on ? null : (k, 1, 0);
                        _sdTip = _placingRiser != null ? $"Click this deck where the {k.ToLowerInvariant()} goes: it runs to the deck above." : "";
                    }
                    Row();
                }
                ImGui.NewLine();
                Hint("Joins the rooms it stands in on each deck it passes. A ladder needs 0.8 x 0.7 m of room; a lift its car, 2 x 2 m to start (1 to 8 m a side).");
                return;
            }
            H2("Decks");
            Hint("Orientation");
            if (OnButton("Along the length##sdor0", !Stacked) && Stacked)
            {
                Cmd(C("orient", ("stacked", false)));
                _sdDeck = 0;
                Toast("Decks laid along the length. Undo brings the old decks and rooms back.", true);
            }
            Row();
            if (OnButton("Stacked (thrust is down)##sdor1", Stacked) && !Stacked)
            {
                Cmd(C("orient", ("stacked", true)));
                _sdDeck = 0;
                Toast("Decks stacked across the ship, floors facing the bow: thrust is down. Undo brings the old ones back.", true);
            }
            DeckButtons();
            if (Found(d))
            {
                var deck = _sdDeck;
                ImGui.Text(Stacked ? "floor at z" : "floor m");
                Row();
                Num("deckfloor", D(d, "floor"), v => Cmd(C("setDeck", ("deck", deck), ("floor", v))), 0.05, 90 * _scale);
                Row();
                ImGui.Text("clear m"u8);
                Row();
                Num("deckheight", D(d, "height"), v => Cmd(C("setDeck", ("deck", deck), ("height", v))), 0.05, 90 * _scale);
                ImGui.Text("Move deck"u8);
                EndRow();
                foreach (var (by, t) in new[] { (-1.0, Stacked ? "1 m aft" : "Down 1 m"), (-0.25, Stacked ? "25 cm aft" : "Down 25 cm"), (0.25, Stacked ? "25 cm fore" : "Up 25 cm"), (1.0, Stacked ? "1 m fore" : "Up 1 m") })
                    if (WrapButton($"{t}##sdmd{by}"))
                        Cmd(C("setDeck", ("deck", deck), ("by", by)));
                EndRow();
                Hint("Its rooms move with it. One out to the hull follows the skin; the rest shrink to fit inside it, and grow back to the size you drew when the deck moves back.");
                if (ImGui.Button("Remove deck and its rooms##sddeldeck"u8))
                {
                    Cmd(C("delDeck", ("deck", deck)));
                    _sdDeck = Math.Max(0, _sdDeck - 1);
                    Rebuild();
                }
            }
            H2("Rooms");
            Checkbox("Draw rooms by dragging##sddraw", _drawing, v => _drawing = v);
            EndRow();
            foreach (var k in Arr(CAT, "rooms"))
            {
                var name = Str(k, "name");
                var c = DesignMeshHex(Str(k, "colour"));
                ImGui.PushStyleColor(ImGuiCol.Text, name == _roomKind ? new float4(1, 1, 1, 1) : new float4(c.X * 0.5f + 0.5f, c.Y * 0.5f + 0.5f, c.Z * 0.5f + 0.5f, 1));
                if (WrapButton($"{name}##sdrk{name}", name == _roomKind))
                    _roomKind = name;
                ImGui.PopStyleColor();
                var mn = ImGui.GetItemRectMin();
                var mx = new float2(mn.X + 4 * _scale, ImGui.GetItemRectMax().Y);
                ImDrawListExtensions.AddRectFilled(ImGui.GetWindowDrawList(), in mn, in mx, new ImColor8((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), 255), 0, default);
                if (ImGui.IsItemHovered())
                    ImGuiSafe.WrappedTooltip(Str(k, "desc"), 300 * _scale);
            }
            EndRow();
            Hint("Rooms snap to 25 cm. Rooms that share 0.9 m of wall get a door. A Shaft joins the decks it overlaps. A cargo hatch needs a Hold behind it.");
        }
    }

    private static float3 DesignMeshHex(string hex)
    {
        var c = ShipDesigner.DesignMesh.Hex(hex);
        return new float3(c.R / 255f, c.G / 255f, c.B / 255f);
    }

    private void StationPanel(JsonElement h)
    {
        var hid = I(h, "id");
        var stations = Arr(h, "stations").ToList();
        var i = _selStation;
        H2(Str(h, "name"), i >= 0 ? $"station {i + 1} of {stations.Count}" : "pick a station ring");
        ImGui.Text("name"u8);
        Row();
        TextBox("hullname" + hid, Str(h, "name"), v => Cmd(C("setHull", ("hull", hid), ("name", v))));
        double hx = D(h, "x"), hy = D(h, "y"), hz = D(h, "z");
        ImGui.Text("at x y z"u8);
        var w3 = (ImGui.GetContentRegionAvail().X - 2 * ImGui.GetStyle().ItemSpacing.X) / 3;
        Num("hx" + hid, hx, v => Cmd(C("moveHull", ("hull", hid), ("x", v), ("y", hy), ("z", hz))), 0.25, w3);
        Row();
        Num("hy" + hid, hy, v => Cmd(C("moveHull", ("hull", hid), ("x", hx), ("y", v), ("z", hz))), 0.25, w3);
        Row();
        Num("hz" + hid, hz, v => Cmd(C("moveHull", ("hull", hid), ("x", hx), ("y", hy), ("z", v))), 0.25, w3);
        ImGui.Text("skin"u8);
        Row();
        Colour("skin" + hid, Str(h, "skin"), v => Cmd(C("setHull", ("hull", hid), ("skin", v))));
        Checkbox("Cut where it meets##sdcut", B(h, "cut"), v => Cmd(C("setHull", ("hull", hid), ("cut", v))));
        Row();
        Checkbox("Mirrored pair##sdmir", B(h, "mirror"), v => Cmd(C("setHull", ("hull", hid), ("mirror", v))));

        // Cladding: plates of varied size laid on the skin, raised a few cm, off every opening.
        var clad = Get(h, "clad");
        var ck = Str(clad, "kind", "Off");
        H3("Cladding");
        foreach (var k in new[] { "Off", "Plates", "Heavy" })
        {
            if (OnButton($"{k}##sdhc{k}", ck == k))
                Cmd(C("setHull", ("hull", hid), ("clad", k)));
            Row();
        }
        ImGui.NewLine();
        if (ck != "Off")
        {
            Slider("Panel size", Math.Round(D(clad, "size", 1.5) * 100), 40, 600, 5, " cm", v => Cmd(C("setHull", ("hull", hid), ("cladSize", v / 100))), "hcs");
            Slider("Depth", Math.Round(D(clad, "depth", 1) * 100), 25, 250, 5, "%", v => Cmd(C("setHull", ("hull", hid), ("cladDepth", v / 100))), "hcd");
            SeedRow("hcseed", I(clad, "seed", 1), v => Cmd(C("setHull", ("hull", hid), ("cladSeed", v))));
        }
        Hint("Plates rise 1-8 cm (Heavy: up to 20 cm, with greebles); the depth slider scales that. Windows and sockets stay bare.");

        // Rounded ends: the skin curves into the cap over this radius.
        var rMaxF = Math.Max(0, Math.Floor(D(h, "roundMaxFore") * 20) * 5);
        var rMaxA = Math.Max(0, Math.Floor(D(h, "roundMaxAft") * 20) * 5);
        H3("Rounded ends");
        Slider("Fore radius", Math.Round(Math.Min(D(h, "roundFore"), D(h, "roundMaxFore")) * 100), 0, rMaxF, 5, " cm", v => Cmd(C("setHull", ("hull", hid), ("roundFore", v / 100))), "rf");
        Slider("Aft radius", Math.Round(Math.Min(D(h, "roundAft"), D(h, "roundMaxAft")) * 100), 0, rMaxA, 5, " cm", v => Cmd(C("setHull", ("hull", hid), ("roundAft", v / 100))), "ra");
        Hint("0: a hard edge. At most half the end's smallest half-size, and short of the next station.");

        // Paint: mostly dark with bold accent areas, over the facets and the cladding plates.
        var pa = Get(h, "paint");
        H3("Paint");
        ImGui.Text("Base"u8);
        Row();
        Colour("base" + hid, Str(h, "skin"), v => Cmd(C("setHull", ("hull", hid), ("skin", v))));
        Row();
        ImGui.Text("Accent"u8);
        Row();
        Colour("accent" + hid, Str(pa, "accent", "#d8742a"), v => Cmd(C("setHull", ("hull", hid), ("accent", v))));
        Slider("Accent amount", Math.Round(D(pa, "amount") * 100), 0, 100, 1, "%", v => Cmd(C("setHull", ("hull", hid), ("accentAmount", v / 100))), "pam");
        ImGui.Text("Pattern"u8);
        var pat = Str(pa, "pattern", "Blocks");
        foreach (var k in new[] { "Blocks", "Stripes", "Bands" })
        {
            Row();
            if (OnButton($"{k}##sdpat{k}", pat == k))
                Cmd(C("setHull", ("hull", hid), ("pattern", k)));
        }
        if (pat != "Blocks")
            Slider("Stripe width", Math.Round(D(pa, "stripe", 1.5) * 100), 20, 1000, 5, " cm", v => Cmd(C("setHull", ("hull", hid), ("stripe", v / 100))), "pst");
        Slider("Wear", Math.Round(D(pa, "wear") * 100), 0, 100, 1, "%", v => Cmd(C("setHull", ("hull", hid), ("wear", v / 100))), "pw");
        SeedRow("pseed", I(pa, "seed", 1), v => Cmd(C("setHull", ("hull", hid), ("paintSeed", v))));
        Hint("Blocks: that share of facets and plates in the accent. Stripes run along the ship, bands across it. Wear darkens and varies the tones. Connections take hull A's paint.");

        H3("Segments");
        if (ImGui.Button("+ Segment at bow##sdsb"u8))
            Cmd(C("addStationFront", ("hull", hid), ("preset", _shapePick), ("len", 4)));
        Row();
        if (ImGui.Button("+ Segment at stern##sdss"u8))
            Cmd(C("addStation", ("hull", hid), ("i", stations.Count - 1), ("preset", _shapePick), ("len", 4)));
        Hint("Pick a shape, then add a segment or use it on the picked station.");
        Gallery("sdgal", _shapePick, n => _shapePick = n, 300);
        if (i >= 0 && i < stations.Count)
        {
            var s = stations[i];
            ProfileEditor(h, i);
            ImGui.Text("z m"u8);
            Row();
            Num("stz" + hid + "." + i, D(s, "z"), v => Cmd(C("moveStation", ("hull", hid), ("i", i), ("z", v))), 0.25);
            Checkbox("Move the point on every station##sdall", _allStations, v => _allStations = v);
            if (ImGui.Button("Use shape##sduse"u8))
                Cmd(C("section", ("hull", hid), ("i", i), ("preset", _shapePick)));
            Row();
            if (ImGui.Button("Split after##sdsplit"u8))
                Cmd(C("addStation", ("hull", hid), ("i", i)));
            Row();
            if (ImGui.Button("Remove##sddelst"u8))
            {
                Cmd(C("delStation", ("hull", hid), ("i", i)));
                _selStation = -1;
                Rebuild();
            }
            EndRow();
            foreach (var (t, sx, sy) in new[] { ("Wider", 1.1, 1.0), ("Narrower", 0.9, 1.0), ("Taller", 1.0, 1.1), ("Lower", 1.0, 0.9), ("Bigger", 1.1, 1.1), ("Smaller", 0.9, 0.9) })
                if (WrapButton($"{t}##sdsc{t}"))
                    Cmd(C("scaleStation", ("hull", hid), ("i", i), ("sx", sx), ("sy", sy)));
            EndRow();
        }
        // Each station keeps its own point count: with a station picked the list changes that one, else every station.
        var one = _selStation >= 0 && _selStation < stations.Count;
        var cur = Arr(stations[one ? _selStation : 0], "p").Count();
        ImGuiSafe.TextColored(SdDimText, one ? "This station:" : "Every station:");
        Row();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##sdpts"u8, $"{cur} points ({(cur - 1) * 2 + 2} facets round)"))
        {
            for (var n = 3; n <= 16; n++)
                if (ImGui.Selectable($"{n} points ({(n - 1) * 2 + 2} facets round)", n == cur))
                    Cmd(one ? C("points", ("hull", hid), ("i", _selStation), ("n", n)) : C("points", ("hull", hid), ("n", n)));
            ImGui.EndCombo();
        }
        if (ImGui.Button("Copy body##sdcopyb"u8))
            Cmd(C("copyHull", ("hull", hid)));
        Row();
        if (ImGui.Button("Remove body##sddelb"u8))
        {
            Cmd(C("delHull", ("hull", hid)));
            Select(null, 0);
        }
    }

    private void SeedRow(string id, int seed, Action<int> set)
    {
        ImGui.Text("Seed"u8);
        Row();
        Num(id, seed, v => set((int)Math.Round(v)), 1, 110 * _scale);
        Row();
        if (ImGui.Button($"New pattern##{id}"))
            set(Random.Shared.Next(99999));
    }

    /// <summary>The section shapes as a gallery: each drawn whole, three a row, scrolling.</summary>
    private void Gallery(string id, string pick, Action<string> onPick, float tall)
    {
        var secs = Arr(CAT, "sections").ToList();
        var cell = (ImGui.GetContentRegionAvail().X - 2 * ImGui.GetStyle().ItemSpacing.X) / 3;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new float4(0.05f, 0.065f, 0.075f, 1));
        if (ImGui.BeginChild("##" + id, new float2(0, MathF.Min(tall * _scale, (cell + 4) * MathF.Ceiling(secs.Count / 3f) + 8)), ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
        {
            cell = (ImGui.GetContentRegionAvail().X - 2 * ImGui.GetStyle().ItemSpacing.X) / 3;
            for (var k = 0; k < secs.Count; k++)
            {
                var s = secs[k];
                var name = Str(s, "name");
                if (k % 3 != 0)
                    Row();
                var p0 = ImGui.GetCursorScreenPos();
                ImGui.InvisibleButton($"##{id}b{name}", new float2(cell, cell));
                if (ImGui.IsItemClicked())
                    onPick(name);
                var hot = ImGui.IsItemHovered();
                if (hot)
                    ImGuiSafe.SetTooltip(name);
                var dl = ImGui.GetWindowDrawList();
                var p1 = p0 + new float2(cell, cell);
                ImDrawListExtensions.AddRectFilled(dl, in p0, in p1, name == pick ? Sd8(SdOn) : hot ? new ImColor8(40, 52, 60, 255) : new ImColor8(26, 34, 40, 255), 4, default);
                ShapeIcon(dl, Arr(s, "p").Select(q => (q[0].GetDouble(), q[1].GetDouble())).ToList(), p0, cell * 0.8f, name == pick ? Hex8(0xffb02e) : Hex8(0x9fb3c0));
                var label = name.Length > 12 ? name[..12] : name;
                var lp = new float2(p0.X + 4, p1.Y - ImGui.GetTextLineHeight() - 2);
                ImDrawListExtensions.AddText(dl, in lp, new ImColor8(160, 176, 186, 255), label);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    /// <summary>A half profile drawn whole (both sides) in a square of <paramref name="size"/> at <paramref name="p0"/>.</summary>
    private static void ShapeIcon(ImDrawListPtr dl, List<(double H, double Y)> p, float2 p0, float size, ImColor8 col)
    {
        if (p.Count == 0)
            return;
        double w = 0.5, y0 = 1e9, y1 = -1e9;
        foreach (var (x, y) in p)
        {
            w = Math.Max(w, x);
            y0 = Math.Min(y0, y);
            y1 = Math.Max(y1, y);
        }
        var span = Math.Max(w * 2, y1 - y0) * 1.15;
        var sc = size / span;
        var cx = p0.X + size / 0.8f / 2;
        var cy = p0.Y + size / 0.8f * 0.45f + (y0 + y1) / 2 * sc;
        var pts = new List<(double, double)> { (0, p[0].Y) };
        pts.AddRange(p);
        pts.Add((0, p[^1].Y));
        var all = pts.Select(q => new float2((float)(cx + q.Item1 * sc), (float)(cy - q.Item2 * sc))).ToList();
        all.AddRange(pts.AsEnumerable().Reverse().Select(q => new float2((float)(cx - q.Item1 * sc), (float)(cy - q.Item2 * sc))));
        all.Add(all[0]);
        ImDrawListExtensions.AddPolyline(dl, all.ToArray(), col, ImDrawFlags.None, 1.5f);
    }

    // The half profile at one station, drawn whole (both sides), with its neighbours ghosted and the decks across it; drag a point to move it.
    private int _profDrag = -1;

    private void ProfileEditor(JsonElement h, int i)
    {
        var hid = I(h, "id");
        var stations = Arr(h, "stations").Select(s => Arr(s, "p").Select(q => (X: q[0].GetDouble(), Y: q[1].GetDouble())).ToList()).ToList();
        var W = ImGui.GetContentRegionAvail().X;
        var Hh = W * 230 / 260;
        var pad = 14 * _scale;
        double maxH = 1, y0 = 1e9, y1 = -1e9;
        foreach (var s in stations)
            foreach (var (x, y) in s)
            {
                maxH = Math.Max(maxH, x);
                y0 = Math.Min(y0, y);
                y1 = Math.Max(y1, y);
            }
        var span = Math.Max(maxH * 2, y1 - y0) * 1.12;
        var sc = (Math.Min(W, Hh) - pad * 2) / span;
        var p0 = ImGui.GetCursorScreenPos();
        var cx = p0.X + W / 2;
        var cy = p0.Y + Hh / 2 + (y0 + y1) / 2 * sc;
        float X(double x) => (float)(cx + x * sc);
        float Y(double y) => (float)(cy - y * sc);
        ImGui.InvisibleButton("##sdprof"u8, new float2(W, Hh));
        var dl = ImGui.GetWindowDrawList();
        var pEnd = p0 + new float2(W, Hh);
        ImDrawListExtensions.AddRectFilled(dl, in p0, in pEnd, new ImColor8(14, 19, 23, 255), 4, default);
        void Path(List<(double X, double Y)> p, ImColor8 col, float width)
        {
            var pts = new List<(double X, double Y)> { (0, p[0].Y) };
            pts.AddRange(p);
            pts.Add((0, p[^1].Y));
            var all = pts.Select(q => new float2(X(q.X), Y(q.Y))).ToList();
            all.AddRange(pts.AsEnumerable().Reverse().Select(q => new float2(X(-q.X), Y(q.Y))));
            ImDrawListExtensions.AddPolyline(dl, all.ToArray(), col, ImDrawFlags.None, width);
        }
        if (!Stacked)
            foreach (var d in Arr(S, "decks"))
                foreach (var y in new[] { D(d, "floor"), D(d, "floor") + D(d, "height") })
                {
                    var ly = Y(y - D(h, "y"));
                    for (var x = p0.X; x < p0.X + W; x += 6)
                    {
                        var a = new float2(x, ly);
                        var b = new float2(MathF.Min(x + 3, p0.X + W), ly);
                        ImDrawListExtensions.AddLine(dl, in a, in b, Hex8(0x3c6a52), 1);
                    }
                }
        var ax0 = new float2(cx, p0.Y);
        var ax1 = new float2(cx, p0.Y + Hh);
        ImDrawListExtensions.AddLine(dl, in ax0, in ax1, Hex8(0x33414b), 1);
        if (i > 0)
            Path(stations[i - 1], Hex8(0x5d7383), 1);
        if (i < stations.Count - 1)
            Path(stations[i + 1], Hex8(0x5d7383, 140), 1);
        var cur = stations[i];
        Path(cur, Hex8(0xffb02e), 2);
        var mouse = ImGui.GetIO().MousePos;
        if (ImGui.IsItemActivated())
        {
            _profDrag = -1;
            var best = 9 * _scale;
            for (var k = 0; k < cur.Count; k++)
            {
                var dx = mouse.X - X(cur[k].X);
                var dy = mouse.Y - Y(cur[k].Y);
                var dd = MathF.Sqrt(dx * dx + dy * dy);
                if (dd < best)
                {
                    best = dd;
                    _profDrag = k;
                }
            }
            _dragFirst = true;
        }
        if (ImGui.IsItemActive() && _profDrag >= 0 && _profDrag < cur.Count)
        {
            var nx = Math.Max(0.05, Math.Abs((mouse.X - cx) / sc));
            var ny = (cy - mouse.Y) / sc;
            cur[_profDrag] = (nx, ny);
            DragCmd(C("setPoint", ("hull", hid), ("i", i), ("k", _profDrag), ("h", nx), ("y", ny), ("all", _allStations)));
        }
        if (ImGui.IsItemDeactivated() && _profDrag >= 0)
        {
            _profDrag = -1;
            Flush();
        }
        for (var k = 0; k < cur.Count; k++)
        {
            var c = new float2(X(cur[k].X), Y(cur[k].Y));
            ImDrawListExtensions.AddCircleFilled(dl, in c, 7 * _scale, Hex8(0x1b2329), 16);
            ImDrawListExtensions.AddCircleFilled(dl, in c, 5.5f * _scale, Hex8(0xffb02e), 16);
            var dx = mouse.X - c.X;
            var dy = mouse.Y - c.Y;
            if (ImGui.IsItemHovered() && dx * dx + dy * dy < 81 * _scale * _scale)
                ImGuiSafe.SetTooltip($"point {k + 1}: {Fmt(cur[k].X)} m out, {Fmt(cur[k].Y)} m up");
        }
        var lab = new float2(p0.X + 8, p0.Y + Hh - ImGui.GetTextLineHeight() - 6);
        ImDrawListExtensions.AddText(dl, in lab, Hex8(0x93a3ad), $"{Fmt(maxH * 2)} m beam - decks dashed");
    }

    // The Connect tool: a first point on a hull, the connection's shape, size and segments, then the second body (a point on one already
    // there, or a new body set out at a distance); the design lofts the connection between. Every change places it again.
    private void ConnectSection()
    {
        H2("Connect");
        var label = _conn?.Step == "a" ? "Click the first point..." : _conn != null ? "Connecting (press to stop)" : "Connect";
        if (OnButton(label + "##sdconn", _conn != null))
        {
            _conn = _conn != null ? null : new ConnState();
            _sdTip = _conn != null ? "Click the hull where the connection starts." : SdTips["hull"];
        }
        if (_conn == null || _conn.Step == "a")
        {
            Hint("Press Connect, click a point on the hull, set the connection, then click the second body or add a new one. A pylon, a strut or a neck out to a nacelle or a pod.");
            return;
        }
        JoinControls(_cOpt, () =>
        {
            if (_conn.JoinId != 0)
                PlaceJoin();
        }, "cn");
        void LiveBody()
        {
            if (_conn!.NewBody)
                PlaceJoin();
        }
        H3("Second object");
        Hint("Click another body where the connection should meet it, or add a new body this far out:");
        Gallery("sdbgal", _cBody.Section, n => { _cBody.Section = n; LiveBody(); }, 150);
        Slider("Width", _cBody.W, 0.5, 20, 0.25, " m", v => { _cBody.W = v; LiveBody(); }, "bw");
        Slider("Height", _cBody.H, 0.5, 20, 0.25, " m", v => { _cBody.H = v; LiveBody(); }, "bh");
        Slider("Length", _cBody.Len, 1, 80, 0.5, " m", v => { _cBody.Len = v; LiveBody(); }, "bl");
        Slider("Segments", _cBody.Segments, 1, 16, 1, "", v => { _cBody.Segments = (int)v; LiveBody(); }, "bs");
        Slider("Distance", _cBody.Dist, 0, 30, 0.25, " m", v => { _cBody.Dist = v; LiveBody(); }, "bd");
        Slider("Slide along", _cBody.Slide, -40, 40, 0.25, " m", v => { _cBody.Slide = v; LiveBody(); }, "bsl");
        if (ImGui.Button(_conn.NewBody ? "New body placed##sdnb" : "Add new body##sdnb"))
        {
            _conn.NewBody = true;
            _conn.B = null;
            PlaceJoin();
        }
        Row();
        if (ImGui.Button("Done##sdcdone"u8))
        {
            var id = _conn.JoinId;
            _conn = null;
            _sdTip = SdTips["hull"];
            if (id != 0)
                Select("join", id);
        }
    }

    // Places the connection again (the first placing is an undo step, the rest merge into it).
    private void PlaceJoin()
    {
        if (_conn == null || (_conn.B == null && !_conn.NewBody))
            return;
        var op = C("connect", ("a", _conn.A!.DeepClone()), ("replaceJoin", _conn.JoinId), ("replaceHull", _conn.HullId));
        _cOpt.Into(op);
        if (_conn.NewBody)
            op["body"] = new JsonObject { ["section"] = _cBody.Section, ["w"] = _cBody.W, ["h"] = _cBody.H, ["len"] = _cBody.Len, ["segments"] = _cBody.Segments, ["dist"] = _cBody.Dist, ["slide"] = _cBody.Slide };
        else
            op["b"] = _conn.B!.DeepClone();
        var a = Cmd(op, _conn.JoinId != 0);
        if (Str(a, "error").Length > 0)
        {
            Toast(Str(a, "error"));
            return;
        }
        _conn.JoinId = I(a, "id");
        _conn.Step = "set";
        _conn.HullId = _conn.NewBody ? I(Arr(S, "hulls").Last(), "id") : 0;
    }

    private void JoinControls(JoinOpt o, Action apply, string id)
    {
        H3("Connection shape");
        Gallery("sdjg" + id, o.Section, n => { o.Section = n; apply(); }, 150);
        EndRow();
        foreach (var (n, label) in new[] { ("TriangleUp", "Triangle up"), ("TriangleDown", "Triangle down"), ("Wedge", "Wedge"), ("Diamond", "Diamond"), ("Hexagon", "Hexagon") })
            if (WrapButton($"{label}##sdjx{id}{n}", o.Section == n))
            {
                o.Section = n;
                apply();
            }
        EndRow();
        Slider("Width A", o.W, 0.2, 8, 0.1, " m", v => { o.W = v; apply(); }, id + "wa");
        Slider("Height A", o.H, 0.2, 8, 0.1, " m", v => { o.H = v; apply(); }, id + "ha");
        Slider("Width B", o.Wb, 0.2, 8, 0.1, " m", v => { o.Wb = v; apply(); }, id + "wb");
        Slider("Height B", o.Hb, 0.2, 8, 0.1, " m", v => { o.Hb = v; apply(); }, id + "hb");
        Slider("Twist", o.Twist, -180, 180, 1, " deg", v => { o.Twist = v; apply(); }, id + "tw");
        Slider("Sweep up", o.SweepUp, -20, 20, 0.1, " m", v => { o.SweepUp = v; apply(); }, id + "su");
        Slider("Sweep fore", o.SweepFore, -20, 20, 0.1, " m", v => { o.SweepFore = v; apply(); }, id + "sf");
        Slider("Thickness bias", Math.Round(o.Bias * 100), -90, 90, 5, "%", v => { o.Bias = v / 100; apply(); }, id + "bi");
        Slider("Segments", o.Segments, 1, 24, 1, "", v => { o.Segments = (int)v; apply(); }, id + "sg");
        H3("Buttress (the cladding where it meets each hull)");
        Slider("Flare", Math.Round(o.Flare * 100), 100, 400, 5, "%", v => { o.Flare = v / 100; apply(); }, id + "fl");
        Slider("Runs up", o.Buttress, 0, 6, 0.1, " m", v => { o.Buttress = v; apply(); }, id + "ru");
        Checkbox($"Follow the hull (its base laid onto the skin)##sdfol{id}", o.Follow, v => { o.Follow = v; apply(); });
        H3("Sockets (the hole each end cuts)");
        Checkbox($"Square holes (else oval)##sdsq{id}", o.SquareHole, v => { o.SquareHole = v; apply(); });
        Slider("First end", Math.Round(o.OpenA * 100), 0, 150, 5, "%", v => { o.OpenA = v / 100; apply(); }, id + "oa");
        Slider("Second end", Math.Round(o.OpenB * 100), 0, 150, 5, "%", v => { o.OpenB = v / 100; apply(); }, id + "ob");
        Checkbox($"Mirror to the other side##sdjm{id}", o.Mirror, v => { o.Mirror = v; apply(); });
    }

    private void CladControls(string id, JsonElement clad, double defSize, string kindKey, string sizeKey, string depthKey, string seedKey, Action<JsonObject> set)
    {
        var kind = Str(clad, "kind", "Off");
        foreach (var k in new[] { "Off", "Plates", "Heavy" })
        {
            if (OnButton($"{k}##sdcl{id}{k}", kind == k))
                set(new JsonObject { [kindKey] = k });
            Row();
        }
        ImGui.NewLine();
        if (kind == "Off")
            return;
        Slider("Panel size", Math.Round(D(clad, "size", defSize) * 100), 40, 600, 5, " cm", v => set(new JsonObject { [sizeKey] = v / 100 }), id + "s");
        Slider("Depth", Math.Round(D(clad, "depth", 1) * 100), 25, 250, 5, "%", v => set(new JsonObject { [depthKey] = v / 100 }), id + "d");
        SeedRow(id + "seed", I(clad, "seed", 1), v => set(new JsonObject { [seedKey] = v }));
    }

    private JsonElement Merge(string op, int id, JsonObject extra, string idKey = "id")
    {
        var o = C(op, (idKey, id));
        foreach (var kv in extra.ToList())
        {
            extra.Remove(kv.Key);
            o[kv.Key] = kv.Value;
        }
        return Cmd(o);
    }

    private void Inspector()
    {
        var rooms = Arr(S, "rooms").ToList();
        var stacked = Stacked;
        if (_selKind == "hullwin")
        {
            var w = ById(Arr(S, "hullWindows"), _selId);
            if (!Found(w))
                return;
            var r = ById(rooms, I(w, "room"));
            var wid = I(w, "id");
            void Sl(string label, string key, double v, double min, double max, double step, string unit, double scale) =>
                Slider(label, Math.Round(v * scale), min, max, step, unit, x => Cmd(C("setHullWindow", ("id", wid), (key, x / scale))), "hw" + key);
            H2("Hull window", B(w, "ok") ? $"{Fmt(D(w, "gap"))} m out" : "too far from the skin");
            var side = Str(w, "side") switch { "x0" => "port", "x1" => "starboard", "z0" => stacked ? "lower" : "fore", _ => stacked ? "upper" : "aft" };
            Hint($"On {(Found(r) ? RoomName(r) : "a room")}'s {side} wall. It follows the hull's contour; sizes apply when you let go.");
            Sl("Along the wall", "t", D(w, "t"), 0, 100, 1, "%", 100);
            Sl("Width", "w", Math.Min(D(w, "w"), D(w, "len")), 30, Math.Max(30, Math.Round(D(w, "len") * 100)), 5, " cm", 100);
            Sl("Height", "h", D(w, "h"), 30, 400, 5, " cm", 100);
            Sl("Sill", "sill", D(w, "sill"), 0, 300, 5, " cm", 100);
            ImGui.Text("Shape"u8);
            var shape = Str(w, "shape", "Rect");
            foreach (var (k, t) in new[] { ("Rect", "Square"), ("Round", "Round"), ("Oval", "Oval"), ("Arch", "Arched"), ("Hex", "Hexagon") })
            {
                Row();
                if (OnButton($"{t}##sdhws{k}", shape == k))
                    Cmd(C("setHullWindow", ("id", wid), ("shape", k)));
            }
            if (ImGui.Button("Delete##sdhwdel"u8))
            {
                Cmd(C("delHullWindow", ("id", wid)));
                Select(null, 0);
            }
            return;
        }
        if (_selKind == "door")
        {
            var p = ById(Arr(S, "placedDoors"), _selId);
            if (!Found(p))
                return;
            var pid = I(p, "id");
            string Nm(int id) => ById(rooms, id) is var rr && Found(rr) ? RoomName(rr) : "?";
            void Sl(string label, string key, double v, double min, double max, double step, string unit, double scale) =>
                Slider(label, Math.Round(v * scale), min, max, step, unit, x => Cmd(C("setDoor", ("id", pid), (key, x / scale))), "dr" + key);
            var kind = Str(p, "kind");
            H2(kind);
            Hint($"Between {Nm(I(p, "a"))} and {Nm(I(p, "b"))}. Drag it along their wall, or:");
            Sl("Along the wall", "t", D(p, "t"), 0, 100, 1, "%", 100);
            if (kind == "Door")
            {
                EndRow();
                foreach (var (n, w, h) in new[] { ("Narrow", 0.75, 2.0), ("Standard", 0.9, 2.1), ("Wide", 1.2, 2.1), ("Double", 1.8, 2.1), ("Cargo", 3.0, 2.8), ("Bay", 5.0, 3.0) })
                    if (WrapButton($"{n} {Fmt(w)} m##sddp{n}", Math.Abs(D(p, "w") - w) < 0.01 && Math.Abs(D(p, "h") - h) < 0.01))
                        Cmd(C("setDoor", ("id", pid), ("w", w), ("h", h)));
                EndRow();
                Sl("Width", "w", D(p, "w"), 50, 600, 5, " cm", 100);
                Sl("Height", "h", D(p, "h"), 150, 300, 5, " cm", 100);
            }
            if (kind == "Window")
            {
                var shape = Str(p, "shape");
                foreach (var sh in new[] { "Rect", "Round", "Oval", "Arch" })
                {
                    if (OnButton($"{(sh == "Rect" ? "Square" : sh)}##sdws{sh}", shape == sh))
                        Cmd(C("setDoor", ("id", pid), ("shape", sh)));
                    Row();
                }
                ImGui.NewLine();
                Sl("Width", "w", D(p, "w"), 30, 400, 5, " cm", 100);
                if (shape != "Round")
                    Sl("Height", "h", D(p, "h"), 30, 240, 5, " cm", 100);
                Sl("Sill", "sill", D(p, "sill"), 0, 200, 5, " cm", 100);
            }
            if (ImGui.Button(kind == "Window" ? "Delete##sddd" : "Back to the middle##sddd"))
            {
                Cmd(C("delDoor", ("id", pid)));
                Select(null, 0);
            }
            return;
        }
        if (_selKind == "riser")
        {
            var k = ById(Arr(S, "risers"), _selId);
            if (!Found(k))
                return;
            var kid = I(k, "id");
            var decks = Arr(S, "decks").Count();
            void DeckSel(string key, int v)
            {
                ImGui.SetNextItemWidth(120 * _scale);
                if (ImGui.BeginCombo("##sdrs" + key, $"Deck {v + 1}"))
                {
                    for (var i = 0; i < decks; i++)
                        if (ImGui.Selectable($"Deck {i + 1}", i == v))
                        {
                            var a = Cmd(C("setRiser", ("id", kid), (key, i)));
                            if (Str(a, "error").Length > 0)
                                Toast(Str(a, "error"));
                        }
                    ImGui.EndCombo();
                }
            }
            var kind = Str(k, "kind");
            H2(kind, B(k, "ok") ? "fits" : "see checks");
            foreach (var t in new[] { "Ladder", "Lift" })
            {
                if (OnButton($"{t}##sdrk{t}", kind == t))
                    Cmd(C("setRiser", ("id", kid), ("kind", t)));
                Row();
            }
            ImGui.NewLine();
            ImGui.Text("from"u8);
            Row();
            DeckSel("from", I(k, "from"));
            Row();
            ImGui.Text("to"u8);
            Row();
            DeckSel("to", I(k, "to"));
            ImGui.Text("x"u8);
            Row();
            Num("rx" + kid, D(k, "x"), v => Cmd(C("setRiser", ("id", kid), ("x", v))), 0.25, 100 * _scale);
            Row();
            ImGui.Text(stacked ? "y" : "z");
            Row();
            Num("rz" + kid, D(k, "z"), v => Cmd(C("setRiser", ("id", kid), ("z", v))), 0.25, 100 * _scale);
            if (kind == "Lift")
            {
                foreach (var (key, label) in new[] { ("w", "car across m"), ("l", "car along m") })
                {
                    ImGui.Text(label);
                    Row();
                    ImGui.SetNextItemWidth(90 * _scale);
                    var cur = I(k, key);
                    if (ImGui.BeginCombo("##sdcar" + key, $"{cur} m"))
                    {
                        for (var i = 1; i <= 8; i++)
                            if (ImGui.Selectable($"{i} m", i == cur))
                                Cmd(C("setRiser", ("id", kid), (key, i)));
                        ImGui.EndCombo();
                    }
                }
            }
            Hint("It stops at every deck between its two ends, and joins the room round it on each.");
            if (ImGui.Button("Delete##sdrdel"u8))
            {
                Cmd(C("delRiser", ("id", kid)));
                Select(null, 0);
            }
            return;
        }
        if (_selKind == "join")
        {
            var j = ById(Arr(S, "joins"), _selId);
            if (!Found(j))
                return;
            var jid = I(j, "id");
            var o = new JoinOpt
            {
                Section = Str(j, "section"), W = D(j, "w"), H = D(j, "h"), Wb = D(j, "wb", D(j, "w")), Hb = D(j, "hb", D(j, "h")), Twist = D(j, "twist"),
                SweepUp = D(j, "sweepUp"), SweepFore = D(j, "sweepFore"), Bias = D(j, "bias"), Segments = I(j, "segments"), Mirror = B(j, "mirror"),
                Flare = D(j, "flare"), Buttress = D(j, "buttress"), Follow = B(j, "follow"), SquareHole = B(j, "squareHole"), OpenA = D(j, "openA"), OpenB = D(j, "openB"),
            };
            H2("Connection");
            JoinControls(o, () =>
            {
                var op = C("setJoin", ("id", jid));
                o.Into(op);
                Cmd(op);
            }, "j" + jid);
            // Cladding on the strut, as a body's; the flared ends into the skins have their own.
            H3("Cladding");
            CladControls("jc", Get(j, "clad"), 1.5, "clad", "cladSize", "cladDepth", "cladSeed", v => Merge("setJoin", jid, v));
            Hint("Plates rise 1-8 cm (Heavy: up to 20 cm, with greebles); the depth slider scales that. The flared ends have their own cladding below.");
            H3("Buttress cladding");
            CladControls("jb", Get(j, "buttressClad"), 0.8, "buttressClad", "buttressSize", "buttressDepth", "buttressSeed", v => Merge("setJoin", jid, v));
            Hint("Rows of plates over each flared end, out to where it sinks under the skin; the mouths stay clear. Needs a buttress length above 0.");
            if (ImGui.Button("Delete##sdjdel"u8))
            {
                Cmd(C("delJoin", ("id", jid)));
                Select(null, 0);
            }
            return;
        }
        if (_selKind == "piece")
        {
            var p = ById(Arr(S, "pieces"), _selId);
            if (!Found(p))
                return;
            var pid = I(p, "id");
            var k = Arr(CAT, "pieces").FirstOrDefault(x => Str(x, "name") == Str(p, "kind"));
            var sock = Has(k, "socket");
            H2(Str(p, "kind"), B(p, "ok", true) ? "fits" : "see checks");
            Hint(Str(k, "desc"));
            Slider("Size", Math.Round(D(p, "size") * 100), 25, 400, 5, "%", v => Cmd(C("setPiece", ("id", pid), ("size", v / 100))), "pcs");
            if (sock)
                Slider("Cladding", Math.Round(D(p, "clad") * 100), 0, 200, 5, " cm", v => Cmd(C("setPiece", ("id", pid), ("clad", v / 100))), "pcc");
            Slider("Recess", Math.Round(D(p, "recess") * 100), 0, 200, 5, " cm", v => Cmd(C("setPiece", ("id", pid), ("recess", v / 100))), "pcr");
            Checkbox("Mirror on its own body##sdpcm", B(p, "mirror"), v => Cmd(C("setPiece", ("id", pid), ("mirror", v))));
            if (B(p, "onPair"))
                Checkbox("Both bodies of the pair##sdpcp", B(p, "pair"), v => Cmd(C("setPiece", ("id", pid), ("pair", v))));
            if (ImGui.Button("Centre##sdpcc"u8))
                Cmd(C("centrePiece", ("id", pid)));
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetTooltip("Onto the ship's centreline: the middle of its keel or spine, or of a hull's end");
            Row();
            var turn = D(p, "turn");
            if (ImGui.Button("Turn left##sdpctl"u8))
                Cmd(C("setPiece", ("id", pid), ("turn", turn - 15)));
            Row();
            if (ImGui.Button("Turn right##sdpctr"u8))
                Cmd(C("setPiece", ("id", pid), ("turn", turn + 15)));
            Row();
            ImGui.Text($"{Fmt(turn)} deg");
            var roomId = I(p, "room");
            string RoomText()
            {
                var r = ById(rooms, roomId);
                return Found(r) ? RoomName(r) + $" on deck {I(r, "deck") + 1}" : "a room that is gone";
            }
            var drive = Str(k, "group") == "Drives";
            if (drive)
                Hint($"Its service socket (feed and access panel) is inside, opposite it, in {(roomId != 0 ? RoomText() : "the nearest room it can reach")}.");
            if (sock)
                Hint(roomId != 0 ? $"Opens into {RoomText()}." : "Opens into the nearest room it can reach.");
            if (drive || sock)
            {
                if (OnButton("Pick its room##sdpcpr", _pickRoomFor == pid))
                {
                    _pickRoomFor = pid;
                    Rebuild();
                    _sdTip = drive ? "Click the room its service socket goes in." : "Click the room it opens into (the rooms show through the hull).";
                }
                if (roomId != 0)
                {
                    Row();
                    if (ImGui.Button("Nearest room##sdpcnr"u8))
                        Cmd(C("setPiece", ("id", pid), ("room", 0)));
                }
                if (sock)
                {
                    Row();
                    if (ImGui.Button("Add a room behind it##sdpcar"u8))
                        Cmd(C("autoRoom", ("id", pid)));
                }
            }
            if (ImGui.Button("Take off##sdpcdel"u8))
            {
                Cmd(C("delPiece", ("id", pid)));
                Select(null, 0);
            }
            return;
        }
        if (_selKind == "room")
        {
            var r = ById(rooms, _selId);
            if (!Found(r))
                return;
            RoomInspector(r, rooms);
            return;
        }
        if (_selKind == "hull")
        {
            var h = ById(Arr(S, "hulls"), _selId);
            if (!Found(h))
                return;
            var st = Arr(h, "stations").ToList();
            var len = D(st[^1], "z") - D(st[0], "z");
            H2(Str(h, "name"));
            Hint($"{Fmt(len)} m long, {st.Count - 1} segments, {string.Join("/", st.Select(s => Arr(s, "p").Count()).Distinct())} points a profile{(B(h, "mirror") ? ", a mirrored pair" : "")}. Drag the arrows to move the body.");
            return;
        }
        H2("Selection");
        Hint(_sdMode == "pieces" ? "Click a piece to pick it." : _sdMode == "interior" ? "Click a room to pick it." : "Click a hull body to pick it.");
    }

    private void RoomInspector(JsonElement r, List<JsonElement> rooms)
    {
        var rid = I(r, "id");
        var rd = I(r, "deck");
        var stacked = Stacked;
        if (_sdMode == "connectors")
        {
            H2(RoomName(r), $"deck {rd + 1}");
            H3("Ladder or lift from this room");
            EndRow();
            foreach (var (k, dir, t) in new[] { ("Ladder", -1, "Ladder down"), ("Ladder", 1, "Ladder up"), ("Lift", -1, "Lift down"), ("Lift", 1, "Lift up") })
                if (WrapButton($"{t}##sdrr{t}", _placingRiser is { } pr && pr.Room == rid && pr.Kind == k && pr.Dir == dir, OtherDeck(rd, dir) < 0))
                {
                    _placingRiser = (k, dir, rid);
                    _sdDeck = rd;
                    Rebuild();
                    _sdTip = $"Click where in {RoomName(r)} the {k.ToLowerInvariant()} goes (it reaches the room {(dir < 0 ? "below" : "above")}).";
                }
            EndRow();
            Hint("Then click where in the room it goes; off the room puts it in the middle.");
            H3("Doors and windows");
            foreach (var kd in new[] { "Door", "Window" })
            {
                if (OnButton($"Add a {kd.ToLowerInvariant()}##sdad{kd}", _doorFrom == rid && _doorKind == kd))
                {
                    var on = _doorFrom == rid && _doorKind == kd;
                    _doorFrom = on ? 0 : rid;
                    _doorKind = kd;
                    _sdTip = _doorFrom != 0 ? $"Click the room the {kd.ToLowerInvariant()} from {RoomName(r)} looks into." : SdTips[_sdMode];
                }
                Row();
            }
            ImGui.NewLine();
            Hint("Then click the other room: it goes in the wall they share, and you can drag it along. One door a pair of rooms; as many windows as fit.");
            return;
        }
        H2(RoomName(r), $"deck {rd + 1}, {Fmt(D(r, "area"))} m2");
        Checkbox("Out to the hull (its open sides follow the skin)##sdth", B(r, "toHull"), v => Cmd(C("setRoom", ("id", rid), ("toHull", v))));
        if (B(r, "toHull") && !stacked)
        {
            Checkbox("Run on into the nose (its fore end follows the hull; it can't be dragged while on)##sdro", B(r, "runOn"), v => Cmd(C("setRoom", ("id", rid), ("runOn", v))));
            Checkbox("Run on into the stern (its aft end follows the hull; it can't be dragged while on)##sdra", B(r, "runAft"), v => Cmd(C("setRoom", ("id", rid), ("runAft", v))));
        }
        ImGui.Text("kind"u8);
        Row();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##sdrkind"u8, Str(r, "kind")))
        {
            foreach (var k in Arr(CAT, "rooms"))
                if (ImGui.Selectable(Str(k, "name"), Str(k, "name") == Str(r, "kind")))
                    Cmd(C("setRoom", ("id", rid), ("kind", Str(k, "name"))));
            ImGui.EndCombo();
        }
        ImGui.Text("name"u8);
        Row();
        TextBox("roomlabel" + rid, Str(r, "label"), v => Cmd(C("setRoom", ("id", rid), ("label", v))), Str(r, "kind"));
        var half = (ImGui.GetContentRegionAvail().X - 120 * _scale) / 2;
        foreach (var (a, b, la, lb) in new[] { ("x0", "x1", "x from", "x to"), ("z0", "z1", "z from", "z to") })
        {
            ImGui.Text(la);
            Row();
            Num("r" + a + rid, D(r, a), v => Cmd(C("setRoom", ("id", rid), (a, v))), 0.25, half);
            Row();
            ImGui.Text(lb);
            Row();
            Num("r" + b + rid, D(r, b), v => Cmd(C("setRoom", ("id", rid), (b, v))), 0.25, half);
        }
        ImGui.Text("height"u8);
        Row();
        Num("rh" + rid, D(r, "height"), v => Cmd(C("setRoom", ("id", rid), ("height", v))), 0.25, half);
        Hint("Drag its top to make it taller (through the decks above). Split cuts it in two parts that stay one room, each sized on its own.");
        Hint(Str(r, "kind") == "Hold" ? $"{Fmt(D(r, "area") * D(r, "height"))} m3 of cargo." : B(r, "reached") ? "Reached from a way aboard." : "Not reached from any way aboard yet.");
        if (OnButton("Merge with...##sdmerge", _mergeFrom == rid))
        {
            _mergeFrom = _mergeFrom == rid ? 0 : rid;
            _mergeAcross = null;
            Rebuild();
            _sdTip = _mergeFrom != 0 ? "Click the room to merge into this one (on this deck, or the deck above or below)." : SdTips[_sdMode];
        }
        if (_mergeAcross is { } ma && ma.A == rid && ById(rooms, ma.B) is var o && Found(o))
        {
            void Go(string owner)
            {
                var a = Cmd(C("mergeRoom", ("a", ma.A), ("b", ma.B), ("owner", owner)));
                _mergeAcross = null;
                if (Str(a, "error").Length > 0)
                    Toast(Str(a, "error"));
                else
                {
                    var kept = ById(Arr(S, "rooms"), owner == "a" ? rid : ma.B);
                    if (Found(kept))
                    {
                        _sdDeck = I(kept, "deck");
                        Select("room", I(kept, "id"));
                    }
                    Toast("Merged through both decks. Undo splits them again.", true);
                }
            }
            Hint($"One room through deck {rd + 1} and deck {I(o, "deck") + 1}. Which deck owns it (its plan, and where it is listed)?");
            if (ImGui.Button($"Deck {rd + 1} ({RoomName(r)})##sdma"))
                Go("a");
            Row();
            if (ImGui.Button($"Deck {I(o, "deck") + 1} ({RoomName(o)})##sdmb"))
                Go("b");
            Row();
            if (ImGui.Button("Cancel##sdmc"u8))
                _mergeAcross = null;
        }
        H3("Hull window");
        var sides = stacked ? new[] { ("x0", "Port"), ("x1", "Starboard"), ("z0", "Down"), ("z1", "Up") } : new[] { ("x0", "Port"), ("x1", "Starboard"), ("z0", "Fore"), ("z1", "Aft") };
        foreach (var (side, t) in sides)
        {
            if (ImGui.Button($"{t}##sdhw{side}"))
            {
                var a = Cmd(C("addHullWindow", ("room", rid), ("side", side)));
                if (Str(a, "error").Length > 0)
                    Toast(Str(a, "error"));
                else
                {
                    Select("hullwin", I(a, "id"));
                    Toast("Window on the skin. Size it on the right: it follows the hull when you let go.", true);
                }
            }
            Row();
        }
        ImGui.NewLine();
        Hint("Glass on the skin outside this room's wall, following the ship's shape. The wall must be within 1 m of the hull.");
        H3("Extend the room");
        var extSides = new List<(string, string)> { ("x0", "Port"), ("x1", "Starboard") };
        extSides.AddRange(stacked ? [("z0", "Down"), ("z1", "Up")] : [("z0", "Fore"), ("z1", "Aft"), ("up", "Ceiling")]);
        EndRow();
        foreach (var (side, t) in extSides)
            if (WrapButton($"{t}##sdext{side}"))
            {
                var a = Cmd(C("addExt", ("id", rid), ("side", side), ("shape", side == "up" ? _extUp : _extShape), ("depth", side == "up" ? 1 : 1.5), ("width", 2)));
                if (Str(a, "error").Length > 0)
                    Toast(Str(a, "error"));
                else
                    Toast("The room grows that way. Shape and size it below.", true);
            }
        EndRow();
        var exts = Arr(r, "exts").ToList();
        ImGui.Text("Wall shape"u8);
        foreach (var k in new[] { "Box", "Bay", "Hex", "Round" })
        {
            Row();
            if (OnButton($"{k}##sdews{k}", _extShape == k))
            {
                _extShape = k;
                var last = exts.Select((e, i) => (e, i)).LastOrDefault(x => Str(x.e, "side") != "up");
                if (last.e.ValueKind == JsonValueKind.Object)
                    Cmd(C("setExt", ("id", rid), ("i", last.i), ("shape", k)));
                else
                    Toast("Now pick a wall above: the room grows out of it in this shape.", true);
            }
        }
        ImGui.Text("Ceiling shape"u8);
        foreach (var k in new[] { "Vault", "Gable", "Hex" })
        {
            Row();
            if (OnButton($"{k}##sdecs{k}", _extUp == k))
            {
                _extUp = k;
                var last = exts.Select((e, i) => (e, i)).LastOrDefault(x => Str(x.e, "side") == "up");
                if (last.e.ValueKind == JsonValueKind.Object)
                    Cmd(C("setExt", ("id", rid), ("i", last.i), ("shape", k)));
                else
                    Toast("Now press Ceiling above: the ceiling rises in this shape.", true);
            }
        }
        for (var i = 0; i < exts.Count; i++)
        {
            var e = exts[i];
            var ei = i;
            void Set(JsonObject x)
            {
                var a = Merge("setExt", rid, x.Also("i", ei));
                if (Str(a, "error").Length > 0)
                    Toast(Str(a, "error"));
            }
            var side = Str(e, "side");
            var up = side == "up";
            var off = side switch { "x0" => "port", "x1" => "starboard", "z0" => stacked ? "the bottom" : "fore", _ => stacked ? "the top" : "aft" };
            Hint($"{i + 1}. {Str(e, "shape")} {(up ? "ceiling" : "off " + off)}");
            foreach (var k in up ? new[] { "Vault", "Gable", "Hex" } : ["Box", "Bay", "Hex", "Round"])
            {
                if (OnButton($"{k}##sdex{i}{k}", Str(e, "shape") == k))
                    Set(new JsonObject { ["shape"] = k });
                Row();
            }
            if (ImGui.Button($"Delete##sdexd{i}"))
            {
                var a = Cmd(C("delExt", ("id", rid), ("i", ei)));
                if (Str(a, "error").Length > 0)
                    Toast(Str(a, "error"));
            }
            Slider(up ? "Rise" : "Depth", Math.Round(D(e, "depth") * 100), 25, 600, 5, " cm", v => Set(new JsonObject { ["depth"] = v / 100 }), "exd" + i);
            if (!up)
            {
                Slider("Width", Math.Round(D(e, "width") * 100), 50, 1200, 5, " cm", v => Set(new JsonObject { ["width"] = v / 100 }), "exw" + i);
                Slider("Along the wall", Math.Round(D(e, "t") * 100), 0, 100, 1, "%", v => Set(new JsonObject { ["t"] = v / 100 }), "ext" + i);
            }
        }
        Hint("Grows the room out of a wall (a box, an angled bay, half a hexagon or a round apse) or raises its ceiling (vaulted, gabled or chamfered). Red: it pokes through the skin.");
        H3("Walls");
        Slider("Wall thickness", Math.Round(D(r, "wallThick", 0.1) * 100), 0, 30, 1, " cm", v =>
        {
            var a = Cmd(C("setRoom", ("id", rid), ("wallThick", v / 100)));
            if (Str(a, "error").Length > 0)
                Toast(Str(a, "error"));
        }, "wt");
        Hint("A shell round the room, outside its box, so it keeps its full inside space. It stops at the skin and leaves doors between rooms open (0: none).");
        H3("Interior");
        void SetI(JsonObject x)
        {
            var a = Merge("setRoom", rid, x);
            if (Str(a, "error").Length > 0)
                Toast(Str(a, "error"));
        }
        ImGui.Text("Style"u8);
        var style = Str(r, "interior", "Off");
        foreach (var k in new[] { "Off", "Clean", "Industrial" })
        {
            Row();
            if (OnButton($"{k}##sdis{k}", style == k))
                SetI(new JsonObject { ["interior"] = k });
        }
        Slider("Rib spacing", D(r, "ribSpacing", 2), 1, 4, 0.25, " m", v => SetI(new JsonObject { ["ribSpacing"] = v }), "rib");
        ImGui.Text("Panels"u8);
        Row();
        Colour("panel" + rid, Str(r, "panelColour", "#d9dee3"), v => SetI(new JsonObject { ["panelColour"] = v }));
        Row();
        ImGui.Text("Trim"u8);
        Row();
        Colour("trim" + rid, Str(r, "trimColour", "#e0a526"), v => SetI(new JsonObject { ["trimColour"] = v }));
        Row();
        var lights = B(r, "lights", true);
        if (OnButton(lights ? "Lights on##sdli" : "Lights off##sdli", lights))
            SetI(new JsonObject { ["lights"] = !lights });
        Hint("Cladding on the room's inside: framed panels, ribs at the spacing, a kick plate, light strips on the ceiling and floor. Industrial adds vents and hazard trim. Doors stay clear.");
        H3("Mount a socket from this room");
        EndRow();
        foreach (var k in Arr(CAT, "pieces").Where(k => Has(k, "socket") && Str(k, "where") == "skin"))
            if (WrapButton($"{Str(k, "name")}##sdmt{Str(k, "name")}", _mounting?.Kind == Str(k, "name")))
                StartMount(Str(k, "name"), rid);
        EndRow();
        Slider("Size", Math.Round(_pieceSize * 100), 25, 400, 5, "%", v => _pieceSize = v / 100, "mts");
        Slider("Recess into the wall", Math.Round(_pieceRecess * 100), 0, 200, 5, " cm", v => _pieceRecess = v / 100, "mtr");
        if (ImGui.Button("Split##sdsplitr"u8))
        {
            var a = Cmd(C("splitRoom", ("id", rid)));
            if (Str(a, "error").Length == 0)
                Select("room", I(a, "id"));
        }
        Row();
        if (ImGui.Button("Remove room##sddelr"u8))
        {
            Cmd(C("delRoom", ("id", rid)));
            Select(null, 0);
        }
    }

    private void Checks()
    {
        var issues = Arr(S, "issues").ToList();
        var errs = issues.Count(i => Str(i, "level") == "error");
        H2("Checks", issues.Count > 0 ? $"{errs} to fix, {issues.Count - errs} to look at" : "all clear");
        if (issues.Count == 0)
            ImGuiSafe.TextColoredWrapped(SdGood, "Every room fits, every socket opens into a room, and every room can be reached.");
        var n = 0;
        foreach (var i in issues)
        {
            var col = Str(i, "level") == "error" ? SdBad : SdWarn;
            ImGui.PushStyleColor(ImGuiCol.Text, col);
            ImGui.Bullet();
            ImGui.SameLine();
            ImGuiSafe.TextWrapped(Str(i, "text"));
            n++;
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetTooltip("Click to pick it.");
            if (ImGui.IsItemClicked())
            {
                var what = Str(i, "what").Split(':');
                if (what.Length == 2 && int.TryParse(what[1], out var id))
                {
                    if (what[0] == "room")
                    {
                        var r = ById(Arr(S, "rooms"), id);
                        if (Found(r))
                        {
                            _sdDeck = I(r, "deck");
                            if (_sdMode != "interior")
                                SetMode("interior");
                            Select("room", id);
                        }
                    }
                    else if (what[0] == "piece")
                    {
                        if (_sdMode != "pieces")
                            SetMode("pieces");
                        Select("piece", id);
                    }
                }
            }
            ImGui.PopStyleColor();
        }
        ImGui.Separator();
        H2("Ship");
        var st = Get(S, "stats");
        var cells = new[] { ($"{Fmt(D(st, "length"))} m", "length"), ($"{Fmt(D(st, "beam"))} m", "beam"), ($"{Fmt(D(st, "height"))} m", "height"), ($"{Fmt(D(st, "floor"))} m2", "floor"),
            ($"{Fmt(D(st, "hold"))} m3", "cargo hold"), ($"{I(st, "bunks")}", "bunks"), ($"{I(st, "rooms")}", "rooms"), ($"{I(st, "pieces")}", "pieces") };
        if (ImGui.BeginTable("##sdstats"u8, 2, ImGuiTableFlags.None))
        {
            foreach (var (v, k) in cells)
            {
                ImGui.TableNextColumn();
                ImGui.Text(v);
                ImGuiSafe.TextColored(SdDimText, k);
            }
            ImGui.EndTable();
        }
    }
}

internal static class SdJsonExt
{
    public static JsonObject Also(this JsonObject o, string k, JsonNode? v)
    {
        o[k] = v;
        return o;
    }
}
