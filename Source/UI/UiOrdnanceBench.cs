using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Ordnance Bench's window (docs/sandbox/CONSTRUCTS.md, "Modular ordnance"): a picture of the design, the sections in build order, the design
/// from nose to tail with its settings, the numbers, and saved designs. One design for bombs and missiles: a motor that lights as it leaves makes
/// it a missile (<see cref="OrdnanceDesign.Powered"/>). The server checks everything.
/// </summary>
public sealed partial class Ui
{
    public bool OrdnanceBenchOpen;

    private Game? _ordGame;
    private OrdnanceDesign _ord = new();
    private ImInputString? _ordName, _ordFilter;
    private ConstructPartDef[]? _ordSections;
    private int _ordSel = -1; // the section picked in the picture or the list
    private OrdKind? _ordSavedAs; // what a loaded design's key says it is, when its sections now make it the other
    private bool _ordScroll; // bring it into view in the list
    private float _ordHeadLen = -1, _ordHeadDia, _ordHeadCom;
    private string _ordHead = "";

    private static readonly (byte R, byte G, byte B)[] OrdHues =
    [
        (204, 128, 64), (92, 150, 204), (126, 176, 92), (214, 186, 82),
        (172, 104, 176), (86, 182, 170), (214, 108, 108), (150, 152, 172),
    ];
    private static readonly string[] OrdStageNames = ["S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9", "S+"];
    private static readonly string OrdMissileWhy = $"A motor lights as it leaves: it flies as a missile, and goes off {ConstructOrdnance.MissileCoast:0} s after its last burn if it meets nothing.";

    public void OpenOrdnanceBench(Game game)
    {
        _ordGame = game;
        OrdnanceBenchOpen = true;
        OrdnanceStore.EnsureLoaded();
        _ordSections ??= ConstructParts.All.Where(d => d.Ord != null)
            .OrderBy(d => d.Ord!.GroupOrder).ThenBy(d => d.Ord!.Group, StringComparer.Ordinal).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The window, between NewFrame and Render.</summary>
    public void DrawOrdnanceBench()
    {
        if (!OrdnanceBenchOpen || _ordGame is not { } game || _ordSections == null)
            return;
        var open = true;
        ImGui.SetNextWindowSize(new float2(900 * _scale, 680 * _scale), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Ordnance Bench###ordbench"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            _ord.Kind = _ord.Powered(); // (bomb or missile is what its sections make it)
            if (_ordSel >= _ord.Pieces.Count)
                _ordSel = -1;
            ImGui.SetNextItemWidth(160 * _scale);
            var cm = _ord.DiameterCm;
            if (ImGui.InputInt("Diameter, cm"u8, ref cm, 5, 25))
                _ord.DiameterCm = Math.Clamp(cm, OrdnanceDesign.MinCm, OrdnanceDesign.MaxCm);
            ImGui.SameLine();
            if (ImGui.Button("Clear"u8))
            {
                _ordSavedAs = null;
                _ord.Pieces.Clear();
                _ordSel = -1;
            }
            var st = _ord.Stats();
            OrdPicture(st);
            if (ImGui.BeginTable("##ordcols"u8, 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableNextColumn();
                OrdList();
                ImGui.TableNextColumn();
                OrdStack();
                ImGui.TableNextColumn();
                OrdNumbers(game, st);
                ImGui.EndTable();
            }
        }
        ImGui.End();
        if (!open)
            OrdnanceBenchOpen = false;
    }

    private void OrdList()
    {
        ImGui.TextColored(Orange, "SECTIONS, NOSE TO TAIL"u8);
        _ordFilter ??= new ImInputString(33);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ordfilter"u8, "Filter..."u8, _ordFilter);
        var filter = ImGuiSafe.Text(_ordFilter);
        ImGui.BeginChild("##ordlist"u8, new float2(0, 0));
        string? group = null;
        foreach (var d in _ordSections!)
        {
            var o = d.Ord!;
            if (filter.Length > 0 && !d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (o.Group != group)
            {
                group = o.Group;
                ImGuiSafe.TextDisabled(group);
            }
            ImGui.PushID(d.Id);
            if (ImGui.Selectable(d.Name))
                OrdAdd(d);
            ImGui.PopID();
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetTooltip(d.Description);
        }
        ImGui.EndChild();
    }

    /// <summary>A section goes in where it is built: after the last section of its group or one before it, nose to tail (a plain section next to its twin adds one to the row).</summary>
    private void OrdAdd(ConstructPartDef d)
    {
        var o = d.Ord!;
        var values = new float[o.Settings.Length];
        for (var k = 0; k < values.Length; k++)
            values[k] = o.Settings[k].Default;
        var at = 0;
        for (var i = 0; i < _ord.Pieces.Count; i++)
            if (_ord.Pieces[i].Section.Ord!.GroupOrder <= o.GroupOrder)
                at = i + 1;
        if (at > 0 && _ord.Pieces[at - 1].Section == d && values.Length == 0)
        {
            _ord.Pieces[at - 1] = _ord.Pieces[at - 1] with { Count = Math.Min(OrdnanceDesign.MaxCount, _ord.Pieces[at - 1].Count + 1) };
            _ordSel = at - 1;
        }
        else
        {
            _ord.Pieces.Insert(at, new OrdPiece(d, 1, values));
            _ordSel = at;
        }
        _ordScroll = true;
    }

    private void OrdStack()
    {
        ImGui.TextColored(Orange, "NOSE"u8);
        ImGui.BeginChild("##ordstack"u8, new float2(0, -ImGui.GetTextLineHeightWithSpacing()));
        var dl = ImGui.GetWindowDrawList();
        for (var i = 0; i < _ord.Pieces.Count; i++)
        {
            var p = _ord.Pieces[i];
            ImGui.PushID(i);
            var sw = ImGui.GetTextLineHeight();
            var c0 = ImGui.GetCursorScreenPos();
            ImDrawListExtensions.AddRectFilled(dl, c0 + new float2(0, 2 * _scale), c0 + new float2(sw * 0.6f, sw), OrdShade(OrdHues[i % OrdHues.Length], 1f), 2 * _scale, ImDrawFlags.None);
            ImGui.Dummy(new float2(sw * 0.6f, sw));
            ImGui.SameLine();
            if (ImGui.SmallButton("^"u8) && i > 0)
            {
                (_ord.Pieces[i - 1], _ord.Pieces[i]) = (_ord.Pieces[i], _ord.Pieces[i - 1]);
                _ordSel = _ordSel == i ? i - 1 : _ordSel == i - 1 ? i : _ordSel;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("v"u8) && i + 1 < _ord.Pieces.Count)
            {
                (_ord.Pieces[i + 1], _ord.Pieces[i]) = (_ord.Pieces[i], _ord.Pieces[i + 1]);
                _ordSel = _ordSel == i ? i + 1 : _ordSel == i + 1 ? i : _ordSel;
            }
            ImGui.SameLine();
            var gone = ImGui.SmallButton("x"u8);
            ImGui.SameLine();
            if (ImGui.Selectable(p.Section.Name, i == _ordSel))
                _ordSel = i;
            if (i == _ordSel && _ordScroll)
            {
                ImGui.SetScrollHereY(0.3f);
                _ordScroll = false;
            }
            ImGui.SetNextItemWidth(110 * _scale);
            var count = p.Count;
            if (ImGui.InputInt("in a row"u8, ref count))
                _ord.Pieces[i] = p with { Count = Math.Clamp(count, 1, OrdnanceDesign.MaxCount) };
            var s = p.Section.Ord!.Settings;
            for (var k = 0; k < s.Length && k < p.Values.Length; k++)
            {
                ImGui.SetNextItemWidth(160 * _scale);
                ImGui.SliderFloat($"{s[k].Label} {s[k].Unit}##ords{k}", ref p.Values[k], s[k].Min, s[k].Max);
            }
            ImGui.PopID();
            ImGui.Separator();
            if (gone)
            {
                _ord.Pieces.RemoveAt(i);
                _ordSel = _ordSel == i ? -1 : _ordSel > i ? _ordSel - 1 : _ordSel;
                break;
            }
        }
        if (_ord.Pieces.Count == 0)
            ImGuiSafe.TextDisabled("Empty: pick sections on the left. Each goes in where it is built, nose to tail.");
        ImGui.EndChild();
        ImGui.TextColored(Orange, "TAIL"u8);
    }

    // ---- The picture ----------------------------------------------------------------------------------------------

    private static ImColor8 OrdShade((byte R, byte G, byte B) c, float k) => new((byte)Math.Min(255, c.R * k), (byte)Math.Min(255, c.G * k), (byte)Math.Min(255, c.B * k), 255);

    /// <summary>
    /// The design from above, to scale: each section's boxes (its body in its list colour, fins, wings and nozzles darker), nose on the left. Marks the
    /// centre of mass, where stages split, and each powered stage's push. Hover a section for its name; click it to pick it in the list.
    /// </summary>
    private void OrdPicture(OrdStats st)
    {
        var w = MathF.Max(200f, ImGui.GetContentRegionAvail().X);
        var h = 150 * _scale;
        var p0 = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##ordpicture"u8, new float2(w, h));
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p0, p0 + new float2(w, h), new ImColor8(20, 22, 26, 255));
        var line = ImGui.GetTextLineHeight();
        var pieces = _ord.Pieces;
        if (pieces.Count == 0)
        {
            dl.AddText(p0 + new float2(10 * _scale, h * 0.5f - line * 0.5f), new ImColor8(140, 140, 140, 255), "Add sections: the design is drawn here, nose on the left.");
            return;
        }

        // Its size, from above: the widest box (wings, fins) and the length.
        var s = _ord.Scale;
        var cube = s * s * s;
        float length = 0, wide = _ord.DiameterCm / 200f, massZ = 0, mass = 0;
        foreach (var p in pieces)
        {
            var len = p.Section.Ord!.Length * p.Count * s;
            foreach (var b in p.Section.Boxes)
                if (!b.Hidden)
                    wide = MathF.Max(wide, (MathF.Abs(b.Center.X) + b.Half.X) * s);
            var kg = (p.Section.Mass + OrdFlightStats.SettingMass(p)) * p.Count * cube;
            massZ += kg * (length + len * 0.5f);
            mass += kg;
            length += len;
        }
        var com = mass > 0 ? massZ / mass : length * 0.5f;
        float padL = 12 * _scale, padR = 34 * _scale, padT = line * 2.2f, padB = line * 1.4f;
        var r = _ord.DiameterCm / 200f;
        var px = MathF.Min((w - padL - padR) / MathF.Max(0.01f, length), (h - padT - padB) / (2 * MathF.Min(wide, r * 2.5f))); // (fins fit; long wings run off the edge)
        var midY = p0.Y + padT + (h - padT - padB) * 0.5f;
        var x0 = p0.X + padL;
        var view = new OrdView(x0, midY, px);
        float2 At(float z, float x) => view.At(z, x);

        // Which section the mouse is over.
        var over = -1;
        var mouse = ImGui.GetMousePos();
        var zMouse = (mouse.X - x0) / px;
        {
            var z = 0f;
            for (var i = 0; i < pieces.Count && hovered; i++)
            {
                var len = pieces[i].Section.Ord!.Length * pieces[i].Count * s;
                if (zMouse >= z && zMouse < z + len)
                    over = i;
                z += len;
            }
        }
        if (clicked && over >= 0)
        {
            _ordSel = over;
            _ordScroll = true;
        }

        // The sections: details behind, then each body, then outlines.
        dl.PushClipRect(new float2(p0.X, p0.Y + padT - 2 * _scale), new float2(p0.X + w, p0.Y + h - padB + 2 * _scale), true);
        var zAt = 0f;
        for (var pass = 0; pass < 2; pass++, zAt = 0) // (fins, wings and nozzles first, then the bodies over them)
            for (var i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                var len = p.Section.Ord!.Length * p.Count * s;
                var hue = OrdHues[i % OrdHues.Length];
                var lit = i == over ? 1.25f : 1f;
                var first = true;
                foreach (var b in p.Section.Boxes)
                {
                    if (b.Hidden)
                        continue;
                    if (first == (pass == 1))
                    {
                        var cz = zAt + len * 0.5f + b.Center.Z * s * p.Count;
                        var hz = b.Half.Z * s * p.Count;
                        var a = At(cz - hz, b.Center.X * s - b.Half.X * s);
                        var c = At(cz + hz, b.Center.X * s + b.Half.X * s);
                        ImDrawListExtensions.AddRectFilled(dl, in a, in c, OrdShade(hue, (first ? 1f : 0.55f) * lit), first ? 2 * _scale : 0, ImDrawFlags.None);
                    }
                    first = false;
                }
                if (pass == 1)
                {
                    if (first) // (a section with nothing drawn: its body)
                    {
                        var a = At(zAt, -r);
                        var c = At(zAt + len, r);
                        ImDrawListExtensions.AddRectFilled(dl, in a, in c, OrdShade(hue, lit), 2 * _scale, ImDrawFlags.None);
                    }
                    for (var k = 1; k < p.Count && p.Count <= 64; k++) // (a row: where each one ends)
                    {
                        var zk = zAt + len * k / p.Count;
                        dl.AddLine(At(zk, -r), At(zk, r), new ImColor8(0, 0, 0, 120), 1f);
                    }
                    if (i == _ordSel || i == over)
                        dl.AddRect(At(zAt, -r) - new float2(1, 1), At(zAt + len, r) + new float2(1, 1), i == _ordSel ? new ImColor8(255, 230, 120, 255) : new ImColor8(255, 255, 255, 160), 2 * _scale, ImDrawFlags.None, 1.5f * _scale);
                }
                zAt += len;
            }
        dl.PopClipRect();

        // Stages: a dashed line at each separator, the stage names (S1 burns first, at the tail), and each powered stage's push towards the nose.
        var stages = st.Flight.Stages; // (tail first)
        var count = stages.Count;
        var seg = 0;
        var segFrom = 0f;
        zAt = 0;
        for (var i = 0; i <= pieces.Count; i++)
        {
            var end = i == pieces.Count;
            var isSep = !end && pieces[i].Section.Ord!.Slot == "stage";
            var len = end ? 0 : pieces[i].Section.Ord!.Length * pieces[i].Count * s;
            if (isSep || end)
            {
                var number = count - seg; // (1: the tail's)
                var tail = isSep ? zAt + len * 0.5f : zAt;
                if (number >= 1 && number <= count)
                {
                    if (count > 1)
                        dl.AddText(new float2(At((segFrom + tail) * 0.5f, 0).X - line * 0.5f, p0.Y + line * 1.1f), new ImColor8(170, 200, 230, 255), OrdStageNames[Math.Min(number, OrdStageNames.Length) - 1]);
                    if (stages[number - 1].Thrust > 0)
                        OrdThrust(dl, pieces, seg, s, view, r, number == 1 ? length : -1);
                }
                if (isSep)
                {
                    var top = At(tail, -wide);
                    for (var y = p0.Y + line * 1.1f; y < p0.Y + h - padB * 0.5f; y += 6 * _scale)
                        dl.AddLine(new float2(top.X, y), new float2(top.X, MathF.Min(y + 3 * _scale, p0.Y + h)), new ImColor8(170, 200, 230, 200), 1f);
                    segFrom = tail;
                    seg++;
                }
            }
            zAt += len;
        }

        // The centre of mass.
        var cm = At(com, 0);
        ImDrawListExtensions.AddCircleFilled(dl, in cm, 5 * _scale, new ImColor8(20, 22, 26, 255), 16);
        ImDrawListExtensions.AddCircle(dl, in cm, 5 * _scale, new ImColor8(255, 255, 255, 255), 16, 1.5f * _scale);
        dl.AddLine(cm - new float2(5 * _scale, 0), cm + new float2(5 * _scale, 0), new ImColor8(255, 255, 255, 255), 1f);
        dl.AddLine(cm - new float2(0, 5 * _scale), cm + new float2(0, 5 * _scale), new ImColor8(255, 255, 255, 255), 1f);
        dl.AddText(new float2(cm.X - line, p0.Y + h - padB + 3 * _scale), new ImColor8(220, 220, 220, 255), "CoM");

        // Its size and where the centre of mass is (the text made again only when they change).
        if (st.Length != _ordHeadLen || st.Diameter != _ordHeadDia || com != _ordHeadCom)
        {
            (_ordHeadLen, _ordHeadDia, _ordHeadCom) = (st.Length, st.Diameter, com);
            _ordHead = $"{st.Length:0.##} m long, {st.Diameter * 100:0} cm across ({wide * 200:0} cm with fins and wings), centre of mass {com:0.##} m from the nose";
        }
        dl.AddText(p0 + new float2(padL, 3 * _scale), new ImColor8(220, 200, 160, 255), _ordHead);
        dl.AddText(new float2(x0, p0.Y + line * 1.1f), new ImColor8(140, 140, 140, 255), "NOSE");
        dl.AddText(new float2(At(length, 0).X - line * 2f, p0.Y + h - line - 2 * _scale), new ImColor8(140, 140, 140, 255), "TAIL");

        if (over >= 0)
        {
            var p = pieces[over];
            ImGuiSafe.SetTooltip(p.Count > 1
                ? $"{p.Section.Name} x{p.Count}: {p.Section.Ord!.Length * p.Count * s:0.##} m, {(p.Section.Mass + OrdFlightStats.SettingMass(p)) * p.Count * cube:0.#} kg. Click to pick it."
                : $"{p.Section.Name}: {p.Section.Ord!.Length * s:0.##} m, {(p.Section.Mass + OrdFlightStats.SettingMass(p)) * cube:0.#} kg. Click to pick it.");
        }
    }

    /// <summary>A powered stage's push: an arrow over its tail-most motor pointing the way it drives (to the nose), and for the stage that burns first a flame out of the tail at <paramref name="tailEnd"/>.</summary>
    private void OrdThrust(ImDrawListPtr dl, List<OrdPiece> pieces, int seg, float s, in OrdView view, float r, float tailEnd)
    {
        var px = view.Px;
        var zAt = 0f;
        var k = 0;
        float motorFrom = -1, motorTo = -1;
        foreach (var p in pieces)
        {
            var o = p.Section.Ord!;
            var len = o.Length * p.Count * s;
            if (k == seg && o.Slot == "motor" && o.Num("thrust") > 0)
                (motorFrom, motorTo) = (zAt, zAt + len);
            if (o.Slot == "stage")
                k++;
            zAt += len;
        }
        if (motorFrom < 0)
            return;
        if (tailEnd > 0)
        {
            var flame = MathF.Min(30 * _scale, MathF.Max(14 * _scale, r * px * 1.6f));
            var tail = view.At(tailEnd, 0);
            var up = view.At(tailEnd, -r * 0.6f);
            var down = view.At(tailEnd, r * 0.6f);
            var tip = tail + new float2(flame, 0);
            ImDrawListExtensions.AddTriangleFilled(dl, in up, in down, in tip, new ImColor8(255, 150, 40, 220));
        }
        var y = view.At(0, -r).Y - 7 * _scale;
        var from = new float2(view.At(motorTo, 0).X, y);
        var to = new float2(MathF.Min(view.At(motorFrom, 0).X, from.X - 18 * _scale), y);
        dl.AddLine(from, to, new ImColor8(255, 190, 90, 255), 2f * _scale);
        var a = to + new float2(7 * _scale, -4 * _scale);
        var b = to + new float2(7 * _scale, 4 * _scale);
        ImDrawListExtensions.AddTriangleFilled(dl, in to, in a, in b, new ImColor8(255, 190, 90, 255));
    }

    /// <summary>The picture's scale: metres along (from the nose) and across, to the screen.</summary>
    private readonly record struct OrdView(float X0, float MidY, float Px)
    {
        public float2 At(float z, float x) => new(X0 + z * Px, MidY + x * Px);
    }

    // ---- The numbers ----------------------------------------------------------------------------------------------

    /// <summary>Dim text that wraps at the column's edge.</summary>
    private static void OrdDim(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGuiSafe.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }

    private static void OrdWarn(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGuiSafe.TextColored(Orange, text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>What its sections make it, in words, and why.</summary>
    private static string OrdType(OrdnanceDesign d, OrdStats st, out string why)
    {
        var f = st.Flight;
        var guided = f.Seek != OrdSeek.None && f.TurnAir + f.TurnBurn + f.TurnAny > 0;
        if (d.Kind == OrdKind.Missile)
        {
            why = OrdMissileWhy;
            var powered = 0;
            var air = false;
            foreach (var g in f.Stages)
            {
                powered += g.Thrust > 0 ? 1 : 0;
                air |= g.AirThrust > 0;
            }
            return air && f.Lift > 0 ? guided ? "Guided cruise missile" : "Cruise missile"
                : powered > 1 ? guided ? "Guided multi-stage missile" : "Multi-stage missile"
                : guided ? "Guided missile" : "Unguided rocket";
        }
        var assisted = f.Stages.Exists(g => g.Thrust > 0);
        why = assisted
            ? "Its motor waits after the drop, then drives it on: it still falls as a bomb and goes off where it hits. A motor that lights at once makes a missile."
            : "Nothing pushes it: it falls as a bomb and goes off where it hits (or by its fuses). A motor makes it a missile.";
        return assisted ? guided ? "Guided rocket-assisted bomb" : "Rocket-assisted bomb"
            : f.Lift > 0 ? guided ? "Guided glide bomb" : "Glide bomb"
            : guided ? "Guided bomb"
            : f.Deploy > 0 && f.Drag >= 0.4f ? "Retarded bomb"
            : "Free-fall bomb";
    }

    /// <summary>Sections that do nothing, or something odd, in this design: the physics, said rather than hidden.</summary>
    private void OrdNotes(OrdStats st)
    {
        if (_ordSavedAs is { } was && was != _ord.Kind)
            OrdDim(was == OrdKind.Bomb
                ? "Saved as a bomb: with its motor, built here now it flies as a missile. Ones already built stay as they were."
                : "Saved as a missile: with no motor that lights at once, built here now it falls as a bomb. Ones already built stay as they were.");
        var burns = st.Flight.Stages.Exists(g => g.Thrust > 0);
        ConstructPartDef? last = null;
        foreach (var p in _ord.Pieces)
        {
            var o = p.Section.Ord!;
            if (p.Section == last)
                continue;
            last = p.Section;
            if (!burns && o.Num("turnBurn") > 0)
                OrdWarn($"{p.Section.Name}: it turns only while a motor burns, and nothing burns here.");
            else if (o.Num("turnAir") > 0 && st.Flight.Seek == OrdSeek.None)
                OrdDim($"{p.Section.Name}: steers only for what a seeker finds (or a Seeker Head on its rack).");
            if (_ord.Kind == OrdKind.Missile && o.Num("deploy") > 0 && o.Num("drag") >= 0.4f)
                OrdDim($"{p.Section.Name}: opens {o.Num("deploy"):0.#} s after it leaves, and brakes a missile hard.");
            if (o.Text == "mirv" && st.Flight.Seek == OrdSeek.None)
                OrdDim($"{p.Section.Name}: with no seeker its warheads fan out round a point 300 m ahead.");
        }
    }

    private void OrdNumbers(Game game, OrdStats st)
    {
        var type = OrdType(_ord, st, out var why);
        ImGui.TextColored(Orange, "TYPE"u8);
        ImGui.SameLine();
        ImGuiSafe.TextWrapped(type);
        OrdDim(why);
        ImGui.Separator();
        ImGui.TextColored(Orange, "NUMBERS"u8);
        ImGuiSafe.TextWrapped($"Mass {st.Mass:0.#} kg");
        ImGuiSafe.TextWrapped($"Length {st.Length:0.##} m, {st.Diameter * 100:0} cm across");
        ImGuiSafe.TextWrapped($"Payload {st.Payload:0.#} kg");
        ImGuiSafe.TextWrapped($"Blast {st.Blast:0.#} m, damage {st.Damage:0}");
        ImGuiSafe.TextWrapped($"Crater {st.Crater:0.#} m, fuse {st.Fuse:0.##} s");
        OrdDim($"Blast grows with the cube root of the filling, damage with its square root. Survival: {st.Items} section parts.");
        OrdCurve(st);
        OrdFlight(st);
        OrdNotes(st);
        if (st.Problem.Length > 0)
            OrdWarn(st.Problem);
        else if (ImGui.Button("Build"u8))
            game.SandboxSendOrdnance(_ord.Key());
        ImGui.Separator();
        ImGui.TextColored(Orange, "SAVED DESIGNS"u8);
        ImGui.SetNextItemWidth(160 * _scale);
        _ordName ??= new ImInputString(33);
        ImGui.InputTextWithHint("##ordname"u8, "Name"u8, _ordName);
        ImGui.SameLine();
        if (ImGui.Button("Save"u8) && st.Problem.Length == 0)
            OrdnanceStore.Put(ImGuiSafe.Text(_ordName), _ord.Key());
        for (var i = 0; i < OrdnanceStore.Designs.Count; i++)
        {
            var (name, key) = OrdnanceStore.Designs[i];
            ImGui.PushID(i);
            if (ImGui.SmallButton("x"u8))
            {
                OrdnanceStore.Remove(i);
                ImGui.PopID();
                break;
            }
            ImGui.SameLine();
            if (ImGui.Selectable(name) && OrdnanceDesign.Parse(key, out _) is { } d)
            {
                _ordSavedAs = d.Kind != d.Powered() ? d.Kind : null;
                _ord = d;
                _ordSel = -1;
                ImGuiSafe.SetInput(_ordName, name);
            }
            ImGui.PopID();
        }
    }

    /// <summary>The blast's curve: radius against kilograms of filling, this design's marked.</summary>
    private void OrdCurve(OrdStats st)
    {
        var w = MathF.Max(120f, ImGui.GetContentRegionAvail().X);
        var h = 70 * _scale;
        var p0 = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##ordcurve"u8, new float2(w, h));
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p0, p0 + new float2(w, h), new ImColor8(20, 22, 26, 255));
        var top = MathF.Max(100f, st.Payload * 2f);
        var most = OrdnanceDesign.Charge(top, top, top, top).Blast;
        float2 At(float kg) => new(p0.X + kg / top * w, p0.Y + h - OrdnanceDesign.Charge(kg, kg, kg, kg).Blast / most * (h - 4));
        var prev = At(0);
        for (var i = 1; i <= 40; i++)
        {
            var next = At(top * i / 40f);
            dl.AddLine(prev, next, new ImColor8(220, 150, 60, 255), 2f * _scale);
            prev = next;
        }
        if (st.Payload > 0)
        {
            var me = new float2(p0.X + st.Payload / top * w, p0.Y + h - st.Blast / most * (h - 4));
            ImDrawListExtensions.AddCircleFilled(dl, in me, 4f * _scale, new ImColor8(255, 230, 90, 255), 12);
        }
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip($"Blast radius against filling, 0 to {top:0} kg ({most:0.#} m). The dot is this design (plain filling).");
    }

    /// <summary>The flight: thrust, stages, range, steering, seeker, fuses, charges and the engine limits it meets.</summary>
    private static void OrdFlight(OrdStats st)
    {
        var f = st.Flight;
        if (f.Stages.Exists(g => g.Thrust > 0))
        {
            ImGuiSafe.TextWrapped($"Thrust {f.ThrustWeight:0.##}x its weight at launch{(f.ThrustWeight < 1 ? ": too weak to leave a rail" : "")}");
            for (var i = 0; i < f.Stages.Count; i++)
            {
                var g = f.Stages[i];
                if (g.Thrust > 0)
                    ImGuiSafe.TextWrapped($"Stage {i + 1}: {g.Thrust / 1000f:0.#} kN for {g.Burn:0.#} s ({g.Fuels}{(g.AirThrust > 0 ? ", air only" : "")}{(g.MinSpeed > 0 ? $", from {g.MinSpeed:0} m/s" : "")}{(g.Wait > 0 ? $", lights {g.Wait:0.#} s after the drop" : "")}), {g.Mass:0} kg{(i + 1 < f.Stages.Count ? ", drops when spent" : "")}");
            }
            ImGuiSafe.TextWrapped($"About {f.Range:0} m on a level run, {f.TopSpeed:0} m/s at best");
        }
        if (f.TurnAir + f.TurnBurn + f.TurnAny > 0)
            ImGuiSafe.TextWrapped($"Turns {f.TurnAir:0} deg/s on fins at speed, {f.TurnBurn:0} while burning, {f.TurnAny:0} any time");
        if (f.Lift > 0)
            ImGuiSafe.TextWrapped($"Wings hold up {MathF.Min(1, f.Lift) * 100:0}% of its weight at speed{(f.Deploy > 0 ? $", open after {f.Deploy:0.#} s" : "")}: about {f.Glide:0.#} m along for each metre down");
        ImGuiSafe.TextWrapped($"Drag {f.Drag * 100:0.#}% of its speed a second at 100 m/s{(f.Deploy > 0 && f.Lift <= 0 ? $", once its chute opens ({f.Deploy:0.#} s)" : "")}");
        if (f.Seek != OrdSeek.None)
            ImGuiSafe.TextWrapped($"Seeker: {f.Seek.ToString().ToLowerInvariant()}, {f.SeekReach:0} m{(f.Seek is OrdSeek.Laser or OrdSeek.Radio ? $", channel {f.Channel:0}" : "")}");
        if (f.Fuses != OrdFuse.None)
            ImGuiSafe.TextWrapped($"Fuses: impact{((f.Fuses & OrdFuse.Prox) != 0 ? $", proximity {f.Prox:0} m" : "")}{((f.Fuses & OrdFuse.Altitude) != 0 ? $", altitude {f.FuseHeight:0} m" : "")}{((f.Fuses & OrdFuse.Airburst) != 0 ? $", airburst {f.FuseDistance:0} m from its aim" : "")}{((f.Fuses & OrdFuse.Timer) != 0 ? $", timer {f.Timer:0.#} s" : "")}{((f.Fuses & OrdFuse.Remote) != 0 ? $", remote on channel {f.RemoteChannel:0}" : "")}");
        for (var i = 0; i < f.Charges.Count; i++)
        {
            var c = f.Charges[i];
            if (f.Charges.Count > 1 || c.Sub != OrdSub.None || c.Kinetic)
                ImGuiSafe.TextWrapped($"Charge {i + 1}: {c.Payload:0} kg, {c.Blast:0.#} m{(c.Sub != OrdSub.None ? $", lets go {c.Count} {OrdFlightStats.SubWord(c.Sub)}" : "")}{(c.Kinetic ? ", hits with its speed" : "")}");
        }
        foreach (var line in f.Limits)
            OrdDim($"Limit: {line}.");
    }
}
