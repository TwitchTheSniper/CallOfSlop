using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Panel Designer's panel (docs/sandbox/constructs/production.md "Panel Designer"): preset tiles and saved designs on the left, a
/// turning 3D preview of the panel in the middle (the real part made from the real key, a few facets a frame: HullLoft.PanelPreview; drawn
/// by Program after the UI pass in <see cref="DesignerView"/>), sliders, material tiles and live readouts on the right, and along the
/// bottom a name to save it under, a count, Order and the designer's open orders. The designer is a Crafting Terminal, so an order is the
/// terminal's (<see cref="CActKind.CraftNamed"/>) and the server decides. Nothing here makes a run-time part: a slider's every value is
/// only a preview. The texts are made again only when the panel, the designs or the linked machines change.
/// </summary>
public sealed partial class Ui
{
    public bool DesignerOpen { get; private set; }

    /// <summary>(Tools: the window tool's design report) The linked formers as the window counts them, its size limit's words and the fit list.</summary>
    public string DesignerLinks => $"{_pdFormers} former(s); {_pdLimit}; fits [{string.Join("; ", _pdFits)}]";

    /// <summary>The preview's rectangle in window points (min, max) as drawn last frame; zero size: none.</summary>
    public (float2 Min, float2 Max) DesignerView { get; private set; }

    /// <summary>The preview's part builder, camera and switches (Program.SandboxPanelDesigner.cs draws it).</summary>
    public readonly HullLoft.PanelPreview DesignerPreview = new();
    public float DesignerYaw = 0.7f, DesignerPitch = 0.42f, DesignerZoom = 1f;
    public bool DesignerFrame, DesignerSockets = true;
    public double DesignerTouched; // when the player last turned it (it turns by itself again a little later)

    /// <summary>What the preview's part cost (its base and facet slices), summed and the worst frame: a probe reads and clears them.</summary>
    public double DesignerSpentMs, DesignerWorstMs;

    /// <summary>The designer the panel is open on.</summary>
    public (ushort Body, int Part) DesignerTarget => (_pdBody, _pdPart);

    /// <summary>Probes: the shape on the sliders, the designs listed and the text under the sliders.</summary>
    public HullLoft.PanelShape DesignerShape => _pdKind == HullLoft.PanelBend
        ? new(_pdKind, _pdL, _pdW, _pdCrown, 0, _pdMm, RecyclingTable.Current.NameOf(_pdMat), _pdAngle, _pdFoldAt, _pdRadius)
        : _pdKind == HullLoft.PanelLoft
        ? new(_pdKind, _pdL, _pdW, 0, 0, _pdMm, RecyclingTable.Current.NameOf(_pdMat), NoseW: _pdNoseW, Back: _pdBack, Nose: _pdNose)
        : new(_pdKind, _pdL, _pdW, _pdCrown, _pdRake, _pdMm, RecyclingTable.Current.NameOf(_pdMat));

    // A lofted panel's nose width and its two edge profiles (HullLoft.Profile: eight heights a ulong); mirrored, a handle moves its twin too.
    private int _pdNoseW = 100, _pdDragH = -1;
    private ulong _pdBack = HullLoft.PanelDefault(HullLoft.PanelLoft).Back, _pdNose = HullLoft.PanelDefault(HullLoft.PanelLoft).Nose;
    private bool _pdMirror = true;

    /// <summary>Probes and the window tool: a loft's profile handle (0-7 the back edge's, 8-15 the nose's, left to right) to <paramref name="cm"/>, as a drag would (mirrored when the box is ticked).</summary>
    public void DesignerProfile(int handle, int cm)
    {
        if ((uint)handle >= 2 * HullLoft.PanelProfilePoints)
            return;
        var nose = handle >= HullLoft.PanelProfilePoints;
        ref var p = ref nose ? ref _pdNose : ref _pdBack;
        p = ProfileMove(p, handle % HullLoft.PanelProfilePoints, cm, _pdMirror);
    }

    private static ulong ProfileMove(ulong p, int k, int cm, bool mirror)
    {
        p = HullLoft.ProfileSet(p, k, cm);
        return mirror ? HullLoft.ProfileSet(p, HullLoft.PanelProfilePoints - 1 - k, cm) : p;
    }
    public int DesignerDesigns => _pdMine.Count;
    public string DesignerNote => _pdWhy;

    private ushort _pdBody;
    private int _pdPart = -1, _pdKind, _pdL = 150, _pdW = 100, _pdCrown = 10, _pdRake, _pdMm = 20, _pdCount = 1, _pdAngle = 45, _pdFoldAt = 100, _pdRadius = 10;
    private byte _pdMat;
    private int _pdStamp, _pdTextKey, _pdMaxL = HullLoft.PanelMaxCm, _pdMaxW = HullLoft.PanelMaxCm, _pdFormers, _pdMineSel = -1;
    private double _pdBuiltAt;
    private bool _pdBuilt;
    private string _pdKey = "", _pdNumbers = "", _pdWhy = "", _pdSaved = "", _pdLimit = "", _pdAt = "", _pdBig = "";
    private readonly List<string> _pdFits = new(8);
    private readonly List<bool> _pdFitOk = new(8);
    private readonly List<byte> _pdMats = new(8);
    private readonly List<bool> _pdMatFed = new(8);
    private List<PanelDesign> _pdMine = new();
    private readonly List<PanelDesign> _pdWorld = new(16);
    private ImInputString? _pdName;
    private RpTile[] _pdTiles = [];

    public void OpenDesigner(ushort body, int part)
    {
        _pdBody = body;
        _pdPart = part;
        _pdBuilt = false;
        _pdTextKey = 0;
        _crOrdersKey = 0;
        _pdSaved = "";
        _pdMine = PanelDesigns.LoadLibrary();
        if (_pdMat == 0)
            _pdMat = RecyclingTable.Current.IdOf("steel");
        if (_pdTiles.Length == 0)
        {
            _pdTiles = new RpTile[HullLoft.PanelPresets.Length];
            for (var k = 0; k < _pdTiles.Length; k++)
                _pdTiles[k] = new RpTile { Value = k, Name = HullLoft.PanelPresets[k].Label, Slot = SandboxTiles.PanelSlot(k), Swatch = new ImColor8(140, 150, 162, 255), Tip = HullLoft.PanelPresets[k].Label + ": " + HullLoft.PanelPresets[k].What + "." };
        }
        DesignerOpen = true;
    }

    public void CloseDesigner()
    {
        if (!DesignerOpen)
            return;
        DesignerOpen = false;
        DesignerView = default;
        DesignerPreview.Release(); // (every shape the preview made goes back)
    }

    /// <summary>Probes and the window tool: a preset at its own size, or one number of the shape (as the sliders would set it, clamped the same way).</summary>
    public void DesignerSet(string what, int value)
    {
        switch (what)
        {
            case "preset": DesignerPreset(Math.Clamp(value, 0, HullLoft.PanelPresets.Length - 1)); break;
            case "length": _pdL = value; break;
            case "width": _pdW = value; break;
            case "crown": _pdCrown = value; break;
            case "rake": _pdRake = value; break;
            case "thick": _pdMm = value; break;
            case "angle": _pdAngle = value; break;
            case "at": _pdFoldAt = value; break;
            case "radius": _pdRadius = value; break;
            case "nose": _pdNoseW = value; break;
            case "mirror": _pdMirror = value != 0; break;
        }
        DesignerClamp();
    }

    // ---- Cut-outs (laser cutter phase 4): openings and cut corners on the sheet, ordered at a linked Laser Cutter -----------------------

    private readonly List<HullLoft.Cut> _pdCuts = new(HullLoft.MaxCuts);
    private readonly List<bool> _pdGlazed = new(HullLoft.MaxCuts); // (laser cutter phase 6: each cut's Glazed box, by the same index; ORDER adds a pane for each)
    private string _pdPaneNote = "";
    private string _pdCutWhy = "";
    private int _pdCutters;
    private readonly List<string> _pdCutFits = new(4);
    private readonly List<bool> _pdCutFitOk = new(4);

    /// <summary>The cuts as they stand (the window tool and probes read them).</summary>
    public int DesignerCuts => _pdCuts.Count;

    /// <summary>The design's key: the formed panel's, or with cuts the cut panel's (its blank that panel).</summary>
    public string DesignerKey => _pdCuts.Count == 0 ? HullLoft.PanelKey(DesignerShape) : CutKeyOf(DesignerShape);

    private string CutKeyOf(in HullLoft.PanelShape s)
    {
        var blank = HullLoft.PanelKey(s);
        return HullLoft.CutKey(false, blank[HullLoft.PanelPrefix.Length..^1], _pdCuts);
    }

    /// <summary>Probes and the window tool: a cut by its key text ("r70,60,40,50,5" an opening, "m0,30,30" a corner), "clear" or "drop N".</summary>
    public string DesignerCut(string text)
    {
        if (text == "clear")
        {
            _pdCuts.Clear();
            _pdGlazed.Clear();
            return "cuts cleared";
        }
        if (text.StartsWith("drop ", StringComparison.Ordinal) && int.TryParse(text.AsSpan(5), out var at))
        {
            if (at < 0 || at >= _pdCuts.Count)
                return "no such cut";
            _pdCuts.RemoveAt(at);
            _pdGlazed.RemoveAt(at);
            return $"cut {at} dropped";
        }
        if (text.StartsWith("glaze ", StringComparison.Ordinal) && int.TryParse(text.AsSpan(6), out var gz))
        {
            if (gz < 0 || gz >= _pdCuts.Count || HullLoft.PaneFor(_pdCuts[gz], _pdMm) == null)
                return "no opening there a pane fits";
            _pdGlazed[gz] = !_pdGlazed[gz];
            return $"opening {gz} {(_pdGlazed[gz] ? "glazed" : "open")}";
        }
        if (_pdCuts.Count >= HullLoft.MaxCuts)
            return $"a panel takes {HullLoft.MaxCuts} cuts";
        var corner = text.StartsWith('m');
        if (!corner && !text.StartsWith('r'))
            return "a cut is rU,V,A,B,C or mK,A,B";
        var nums = text[1..].Split(',');
        var v = new int[nums.Length];
        for (var k = 0; k < nums.Length; k++)
            if (!int.TryParse(nums[k], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v[k]))
                return "a cut's numbers are whole cm";
        if (corner ? v.Length != 3 : v.Length != 5)
            return "a cut is rU,V,A,B,C or mK,A,B";
        _pdCuts.Add(corner ? new HullLoft.Cut(true, v[0], 0, v[1], v[2], 0) : new HullLoft.Cut(false, v[0], v[1], v[2], v[3], v[4]));
        _pdGlazed.Add(false);
        return $"{_pdCuts.Count} cut(s)";
    }

    /// <summary>The cuts' editor, under the sliders: a row of sliders each, Add, Mirror and Clear.</summary>
    private void CutEditor(ref int dropCut, ref int addCut)
    {
        ImGui.Spacing();
        ImGui.TextColored(PdHead, "Cut-outs"u8);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Openings and cut corners a Laser Cutter cuts out of the formed panel. Openings of 10 cm and more open for real."u8);
        for (var k = 0; k < _pdCuts.Count; k++)
        {
            var cut = _pdCuts[k];
            ImGui.PushID(4000 + k);
            if (ImGui.SmallButton("x"u8))
                dropCut = k;
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, cut.Corner ? $"Corner {k + 1}" : $"Opening {k + 1}");
            ImGui.PushItemWidth(190 * _scale);
            int u = cut.U, vv = cut.V, a = cut.A, b = cut.B, cc = cut.C;
            if (cut.Corner)
            {
                ImGui.SliderInt("Corner"u8, ref u, 0, 3, "%d"u8);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Which corner: 0 back left, 1 back right, 2 nose right, 3 nose left."u8);
                ImGui.SliderInt("Along"u8, ref a, HullLoft.CutMinCm, Math.Max(HullLoft.CutMinCm, _pdL - HullLoft.CutMinCm), "%d cm"u8);
                ImGui.SliderInt("Across"u8, ref b, HullLoft.CutMinCm, Math.Max(HullLoft.CutMinCm, _pdW - HullLoft.CutMinCm), "%d cm"u8);
            }
            else
            {
                ImGui.SliderInt("Middle along"u8, ref u, 0, _pdL, "%d cm"u8);
                ImGui.SliderInt("Middle across"u8, ref vv, 0, _pdW, "%d cm"u8);
                ImGui.SliderInt("Long"u8, ref a, HullLoft.CutMinCm, Math.Max(HullLoft.CutMinCm, _pdL), "%d cm"u8);
                ImGui.SliderInt("Wide"u8, ref b, HullLoft.CutMinCm, Math.Max(HullLoft.CutMinCm, _pdW), "%d cm"u8);
                ImGui.SliderInt("Corners cut"u8, ref cc, 0, Math.Max(0, Math.Min(a, b) / 2), "%d cm"u8);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("How far each corner of the opening is cut back: 0 square, about 0.29 of its side an eight-sided round."u8);
                // Glazed (phase 6): ORDER adds a glass pane cut to this opening's outline, to weld into its socket.
                var fits = a >= HullLoft.CutOpenCm && b >= HullLoft.CutOpenCm;
                var glazed = fits && k < _pdGlazed.Count && _pdGlazed[k];
                ImGui.BeginDisabled(!fits);
                if (ImGui.Checkbox("Glazed"u8, ref glazed) && k < _pdGlazed.Count)
                    _pdGlazed[k] = glazed;
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(fits ? "ORDER also orders a glass pane cut to this opening; weld it into the opening's socket by its back."u8 : "An opening under 10 cm a side has no socket for a pane."u8);
            }
            ImGui.PopItemWidth();
            var now = new HullLoft.Cut(cut.Corner, u, vv, a, b, cut.Corner ? 0 : Math.Clamp(cc, 0, Math.Min(a, b) / 2));
            if (now != cut)
                _pdCuts[k] = now;
            ImGui.PopID();
        }
        ImGui.BeginDisabled(_pdCuts.Count >= HullLoft.MaxCuts);
        if (ImGui.Button("+ Opening"u8))
            addCut = 1;
        ImGui.SameLine();
        if (ImGui.Button("+ Corner"u8))
            addCut = 2;
        ImGui.SameLine();
        if (ImGui.Button("Mirror"u8))
            addCut = 3;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Adds a twin of the last cut across the sheet's centre line."u8);
        ImGui.EndDisabled();
        if (_pdCuts.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear"u8))
                addCut = 4;
        }
        ImGuiSafe.TextColored(Dim, $"Cuts {_pdCuts.Count} of {HullLoft.MaxCuts}");
        if (_pdCutWhy.Length > 0)
        {
            ImGui.PushTextWrapPos(0);
            ImGuiSafe.TextColored(CrWarn, _pdCutWhy);
            ImGui.PopTextWrapPos();
        }
        else if (_pdPaneNote.Length > 0)
        {
            ImGui.PushTextWrapPos(0);
            ImGuiSafe.TextColored(Dim, _pdPaneNote);
            ImGui.PopTextWrapPos();
        }
    }

    /// <summary>The panes ORDER adds for one panel: one per glazed opening a pane fits, equal ones counted together. Allocates: at ORDER and on a change only.</summary>
    private List<(string Key, int Count)> DesignerPanes()
    {
        var list = new List<(string Key, int Count)>(HullLoft.MaxCuts);
        for (var k = 0; k < _pdCuts.Count && k < _pdGlazed.Count; k++)
            if (_pdGlazed[k] && HullLoft.PaneFor(_pdCuts[k], _pdMm) is { } pane)
            {
                var at = list.FindIndex(x => x.Key == pane);
                if (at < 0)
                    list.Add((pane, 1));
                else
                    list[at] = (pane, list[at].Count + 1);
            }
        return list;
    }

    /// <summary>The window tool: the panes ORDER would add, by key and count per panel.</summary>
    public string DesignerPaneText => string.Join("; ", DesignerPanes().Select(p => $"{p.Count} x {p.Key}"));

    /// <summary>
    /// What the Glazed boxes order, and a note for each glazed opening the sheet curves over by more than 2 cm (a pane is flat: it sits flat in
    /// the opening). From the cut key's spots on its sheet (HullLoft.CutSpot): read only when the shape, the cuts or the boxes change.
    /// </summary>
    private void DesignerPaneNote(string key)
    {
        var sb = new System.Text.StringBuilder();
        var panes = DesignerPanes();
        if (panes.Count > 0)
            sb.Append($"With each panel: {string.Join(", ", panes.Select(p => $"{p.Count} {HullLoft.PanelLabel(p.Key)}"))}. Weld each into its opening's socket by its back.");
        for (var k = 0; k < _pdCuts.Count && k < _pdGlazed.Count; k++)
        {
            var c = _pdCuts[k];
            if (!_pdGlazed[k] || c.Corner || !HullLoft.CutSpot(key, c.U, c.V, out var mid, out var n))
                continue;
            var sag = 0f;
            foreach (var (du, dv) in (ReadOnlySpan<(int, int)>)[(-1, -1), (1, -1), (1, 1), (-1, 1), (0, -1), (0, 1), (-1, 0), (1, 0)])
                if (HullLoft.CutSpot(key, c.U + du * c.A / 2.0, c.V + dv * c.B / 2.0, out var at, out _))
                    sag = MathF.Max(sag, MathF.Abs(System.Numerics.Vector3.Dot(at - mid, n)));
            if (sag > 0.02f)
                sb.Append($" Opening {k + 1}: the sheet curves {sag * 100:0} cm over it; its pane sits flat.");
        }
        _pdPaneNote = sb.ToString();
    }

    private int _pdPrevHash;
    private string? _pdPrevCut;

    /// <summary>
    /// The cut key the preview shows for this frame's shape and cuts, or null (no cuts, or the key refused): made again only when they change,
    /// so the key and the shape it is built on always come from the same frame.
    /// </summary>
    private string? PreviewCut(in HullLoft.PanelShape shape)
    {
        if (_pdCuts.Count == 0 || HullLoft.PanelRefusal(shape) != null)
            return null;
        var h = shape.GetHashCode();
        for (var k = 0; k < _pdCuts.Count; k++)
            h = h * 31 + _pdCuts[k].GetHashCode();
        if (h == _pdPrevHash && _pdPrevCut != null)
            return _pdPrevCut.Length > 0 ? _pdPrevCut : null;
        _pdPrevHash = h;
        var key = CutKeyOf(shape);
        _pdPrevCut = HullLoft.CutRefusal(key) == null ? key : "";
        return _pdPrevCut.Length > 0 ? _pdPrevCut : null;
    }

    private void CutApply(int dropCut, int addCut)
    {
        if (dropCut >= 0 && dropCut < _pdCuts.Count)
        {
            _pdCuts.RemoveAt(dropCut);
            _pdGlazed.RemoveAt(dropCut);
        }
        if (addCut == 4)
        {
            _pdCuts.Clear();
            _pdGlazed.Clear();
        }
        if (addCut is < 1 or > 3 || _pdCuts.Count >= HullLoft.MaxCuts || addCut == 3 && _pdCuts.Count == 0)
            return;
        _pdGlazed.Add(false); // (the cut added below; a mirrored opening is glazed as its twin)
        if (addCut == 3 && _pdCuts.Count > 0)
            _pdGlazed[^1] = _pdGlazed[^2];
        if (addCut == 1)
            _pdCuts.Add(new HullLoft.Cut(false, _pdL / 2, _pdW / 2, Math.Max(HullLoft.CutMinCm, _pdL / 4), Math.Max(HullLoft.CutMinCm, _pdW / 4), 0));
        else if (addCut == 2)
            _pdCuts.Add(new HullLoft.Cut(true, 0, 0, Math.Max(HullLoft.CutMinCm, _pdL / 5), Math.Max(HullLoft.CutMinCm, _pdW / 5), 0));
        else if (_pdCuts.Count > 0)
        {
            var last = _pdCuts[^1];
            _pdCuts.Add(last.Corner ? last with { U = last.U switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 } } : last with { V = _pdW - last.V });
        }
    }

    private bool _pdPressSave, _pdPressOrder;

    /// <summary>The window tool: Save design or ORDER, pressed at the panel's next frame.</summary>
    public void DesignerPress(string what)
    {
        _pdPressSave |= what == "save";
        _pdPressOrder |= what == "order";
    }

    /// <summary>Probes and the window tool: the name in the Save box, as if typed.</summary>
    public void DesignerName(string name)
    {
        _pdName ??= new ImInputString(PlayerBlueprints.MaxName + 1);
        ImGuiSafe.SetInput(_pdName, name);
    }

    private void DesignerPreset(int kind)
    {
        var s = HullLoft.PanelDefault(kind);
        (_pdKind, _pdL, _pdW, _pdCrown, _pdRake, _pdMm) = (s.Kind, s.L, s.W, s.Crown, s.Rake, s.Mm);
        if (s.Kind == HullLoft.PanelBend)
            (_pdAngle, _pdFoldAt, _pdRadius) = (s.Angle, s.At, s.Radius);
        if (s.Kind == HullLoft.PanelLoft)
            (_pdNoseW, _pdBack, _pdNose) = (s.NoseW, s.Back, s.Nose);
        _pdMineSel = -1;
    }

    private void DesignerLoad(in PanelDesign d)
    {
        // A cut design (laser cutter phase 4): its blank's shape and its cuts. A cut plate's design isn't drawn here (its blank is an extrusion).
        var cuts = HullLoft.IsCutName(d.Key) ? HullLoft.ReadCut(d.Key, out _) : null;
        if ((cuts != null ? cuts.Plate ? null : HullLoft.PanelOf(cuts.Blank) : HullLoft.PanelOf(d.Key)) is not { } s)
            return;
        _pdCuts.Clear();
        if (cuts != null)
            _pdCuts.AddRange(cuts.Cuts);
        _pdGlazed.Clear();
        for (var k = 0; k < _pdCuts.Count; k++)
            _pdGlazed.Add(false); // (a design is its cut key: glazing is chosen at ORDER, not saved)
        (_pdKind, _pdL, _pdW, _pdCrown, _pdRake, _pdMm) = (s.Kind, s.L, s.W, s.Crown, s.Rake, s.Mm);
        if (s.Kind == HullLoft.PanelBend)
            (_pdAngle, _pdFoldAt, _pdRadius) = (s.Angle, s.At, s.Radius);
        if (s.Kind == HullLoft.PanelLoft)
        {
            (_pdNoseW, _pdBack, _pdNose) = (s.NoseW, s.Back, s.Nose);
            _pdMirror = s.Back == Mirrored(s.Back) && s.Nose == Mirrored(s.Nose); // (a design drawn lopsided opens with the mirror off)
        }
        if (RecyclingTable.Current.IdOf(s.Material) is var m and not 0)
            _pdMat = m;
        _pdName ??= new ImInputString(PlayerBlueprints.MaxName + 1);
        ImGuiSafe.SetInput(_pdName, d.Name);
    }

    /// <summary>The sliders' values into what a key takes and the largest linked former makes; <see cref="_pdLimit"/> says which limit holds.</summary>
    private void DesignerClamp()
    {
        static int Step(int v) => (int)MathF.Round(v / (float)HullLoft.PanelStepCm) * HullLoft.PanelStepCm;
        // The former takes a panel whose longest side is within its length and the next within its width: the shorter of the two sliders has the width's limit.
        var longMax = Math.Min(HullLoft.PanelMaxCm, _pdMaxL);
        var shortMax = Math.Min(HullLoft.PanelMaxCm, _pdMaxW);
        _pdL = Math.Clamp(Step(_pdL), HullLoft.PanelMinCm, longMax);
        _pdW = Math.Clamp(Step(_pdW), HullLoft.PanelMinCm, longMax);
        if (Math.Min(_pdL, _pdW) > shortMax)
        {
            if (_pdL <= _pdW)
                _pdL = shortMax;
            else
                _pdW = shortMax;
        }
        _pdCrown = Math.Clamp(_pdCrown, 0, Math.Min(HullLoft.PanelMaxCrownCm, _pdW / 2));
        // A loft's nose is a width too: within the same limits, and the wider of its two ends is what the former must take.
        _pdNoseW = Math.Clamp(Step(_pdNoseW), HullLoft.PanelMinCm, longMax);
        if (_pdKind == HullLoft.PanelLoft && Math.Min(_pdL, _pdNoseW) > shortMax)
        {
            if (_pdL <= _pdNoseW)
                _pdL = shortMax;
            else
                _pdNoseW = shortMax;
        }
        var rake = Math.Min(HullLoft.PanelMaxRakeCm, _pdL);
        _pdRake = Math.Clamp(_pdRake, -rake, rake);
        _pdMm = Math.Clamp(_pdMm, HullLoft.PanelMinMm, HullLoft.PanelMaxMm);
        // A fold: within 90 degrees, on the sheet, and no longer than it (the radius gives way first).
        _pdAngle = Math.Clamp(_pdAngle, -HullLoft.PanelMaxAngle, HullLoft.PanelMaxAngle);
        _pdFoldAt = Math.Clamp(_pdFoldAt, 0, _pdL);
        var tight = _pdAngle == 0 ? HullLoft.PanelMaxRadiusCm : (int)(_pdL / (Math.Abs(_pdAngle) * MathF.PI / 180f));
        _pdRadius = Math.Clamp(_pdRadius, HullLoft.PanelMinRadiusCm, Math.Max(HullLoft.PanelMinRadiusCm, Math.Min(HullLoft.PanelMaxRadiusCm, tight)));
    }

    /// <summary>What the designer's network offers: the formers and their sizes, the materials (those a former is fed first). At most twice a second.</summary>
    private void DesignerBuild(ConstructWorld world, Construct c, int part, double now)
    {
        var stamp = ConstructCrafting.Stamp(world, c, part);
        if (_pdBuilt && (stamp == _pdStamp || now - _pdBuiltAt < 0.5))
            return;
        _pdBuilt = true;
        _pdStamp = stamp;
        _pdBuiltAt = now;
        _pdTextKey = 0;
        var net = ConstructCrafting.NetOf(world, c, part);
        _pdFormers = 0;
        float maxL = 0, maxW = 0;
        _pdMats.Clear();
        _pdMatFed.Clear();
        var names = RecyclingTable.Current;
        for (var i = 0; i < net.MachineN; i++)
        {
            var d = c.DefOf(net.Machines[i]);
            if (!d.IsPanelFormer || d.IsLaserCutter) // (a cutter forms nothing: phase 4 lists it for cut designs)
                continue;
            _pdFormers++;
            if (d.ExtWheelMax.Y > maxL)
                _pdBig = $"the {d.Name} takes {d.ExtWheelMax.Y:0.##} x {d.ExtWheelMax.X:0.##} m";
            maxL = MathF.Max(maxL, d.ExtWheelMax.Y);
            maxW = MathF.Max(maxW, d.ExtWheelMax.X);
            if (world.StoreOf(c, net.Machines[i]) is { Amount: > 0 } st && !_pdMats.Contains(st.Material) && ExtrusionTable.Current.Find(st.Material) != null)
            {
                _pdMats.Add(st.Material);
                _pdMatFed.Add(true);
            }
        }
        for (byte m = 1; m <= names.Materials.Length && _pdMats.Count < 8; m++)
            if (ExtrusionTable.Current.Find(m) != null && !_pdMats.Contains(m))
            {
                _pdMats.Add(m);
                _pdMatFed.Add(false);
            }
        _pdMaxL = _pdFormers > 0 ? (int)(maxL * 100 + 0.5f) / HullLoft.PanelStepCm * HullLoft.PanelStepCm : HullLoft.PanelMaxCm;
        _pdMaxW = _pdFormers > 0 ? (int)(maxW * 100 + 0.5f) / HullLoft.PanelStepCm * HullLoft.PanelStepCm : HullLoft.PanelMaxCm;
        _pdLimit = _pdFormers == 0 ? "No Panel Former is linked: link one by a conveyor or a Line Console's wire into Orders."
            : $"Largest linked former: {maxL:0.##} x {maxW:0.##} m. A panel is {HullLoft.PanelMinCm} to {HullLoft.PanelMaxCm} cm, crown at most half its width, rake within its length.";
        _pdWorld.Clear();
        _pdWorld.AddRange(world.Designs);
        DesignerClamp();
    }

    /// <summary>The readouts for the shape as it stands, made again only when it or the network changed.</summary>
    private void DesignerTexts(ConstructWorld world, Construct c, int part)
    {
        var key = HashCode.Combine(_pdKind, _pdL, _pdW, _pdCrown, _pdRake, _pdMm, _pdMat, _pdStamp) * 31 + DesignerPreview.Made + HashCode.Combine(_pdAngle, _pdFoldAt, _pdRadius, _pdNoseW, _pdBack, _pdNose);
        for (var k = 0; k < _pdCuts.Count; k++)
            key = key * 31 + _pdCuts[k].GetHashCode() + (k < _pdGlazed.Count && _pdGlazed[k] ? 7 : 0);
        var bd = DesignerPreview.Base;
        key = key * 31 + (bd == null ? 0 : 1);
        if (key == _pdTextKey)
            return;
        _pdTextKey = key;
        var shape = DesignerShape;
        _pdWhy = HullLoft.PanelRefusal(shape) ?? "";
        // Which limits the sliders stand at, and why.
        var at = new System.Text.StringBuilder();
        var longMax = Math.Min(HullLoft.PanelMaxCm, _pdMaxL);
        var linked = _pdFormers > 0 && longMax < HullLoft.PanelMaxCm;
        if (Math.Max(_pdL, _pdW) >= longMax)
            at.Append(linked ? $"{longMax} cm is the longest side a linked former forms ({_pdBig}). " : $"{longMax} cm is the longest a panel's key allows. ");
        if (_pdFormers > 0 && Math.Min(_pdL, _pdW) >= Math.Min(HullLoft.PanelMaxCm, _pdMaxW) && _pdMaxW < HullLoft.PanelMaxCm)
            at.Append($"The shorter side stops at {_pdMaxW} cm ({_pdBig}). ");
        if (_pdCrown > 0 && _pdCrown >= Math.Min(HullLoft.PanelMaxCrownCm, _pdW / 2))
            at.Append(_pdCrown >= HullLoft.PanelMaxCrownCm ? $"Crown stops at {HullLoft.PanelMaxCrownCm} cm. " : $"Crown stops at half the width ({_pdW / 2} cm). ");
        if (_pdRake != 0 && Math.Abs(_pdRake) >= Math.Min(HullLoft.PanelMaxRakeCm, _pdL))
            at.Append(Math.Abs(_pdRake) >= HullLoft.PanelMaxRakeCm ? $"Rake stops at {HullLoft.PanelMaxRakeCm} cm. " : $"Rake stops at the panel's length ({_pdL} cm). ");
        if (_pdKind == HullLoft.PanelBend && Math.Abs(_pdAngle) >= HullLoft.PanelMaxAngle)
            at.Append($"A fold stops at {HullLoft.PanelMaxAngle} degrees. ");
        if (_pdKind == HullLoft.PanelBend && _pdAngle != 0 && _pdRadius < HullLoft.PanelMaxRadiusCm && _pdRadius >= (int)(_pdL / (Math.Abs(_pdAngle) * MathF.PI / 180f)))
            at.Append($"The fold's radius stops at {_pdRadius} cm: the fold is as long as the sheet. ");
        if (_pdMm <= HullLoft.PanelMinMm || _pdMm >= HullLoft.PanelMaxMm)
            at.Append($"Sheet is {HullLoft.PanelMinMm} to {HullLoft.PanelMaxMm} mm thick. ");
        _pdAt = at.ToString();
        _pdKey = _pdWhy.Length == 0 ? HullLoft.PanelKey(shape) : "";
        _pdFits.Clear();
        _pdFitOk.Clear();
        DesignerCutTexts(world, c, part, shape);
        if (bd == null || _pdWhy.Length > 0)
        {
            _pdNumbers = "";
            return;
        }
        var (nu, nv) = HullLoft.PanelGrid(shape);
        var size = bd.BoundsMax - bd.BoundsMin;
        _pdNumbers = $"{size.Z:0.00} x {size.X:0.00} x {size.Y:0.00} m, {bd.Mass:0.#} kg\nSheet: {HullLoft.FormedUnits(bd, _pdMat)} {RecyclingTable.Current.NameOf(_pdMat)}\nFacets {nu * nv} of {HullLoft.PanelMaxAlong * HullLoft.PanelMaxAcross}, sockets {bd.Sockets.Length} of 25";
        var net = ConstructCrafting.NetOf(world, c, part);
        var seen = 0;
        for (var i = 0; i < net.MachineN && _pdFits.Count < 6; i++)
        {
            var m = net.Machines[i];
            var d = c.DefOf(m);
            if (!d.IsPanelFormer || d.IsLaserCutter)
                continue;
            seen++;
            string? why = null;
            if (!ConstructPanelFormer.Fits(d, bd))
            {
                ConstructPanelFormer.Sides(bd, out var a, out var b);
                why = $"too large ({a:0.##} x {b:0.##} m): takes {d.ExtWheelMax.Y:0.##} x {d.ExtWheelMax.X:0.##} m";
            }
            else if (HullLoft.FormedUnits(bd, _pdMat) > d.BulkCapacity)
                why = $"needs {HullLoft.FormedUnits(bd, _pdMat)} sheet: its bin holds {d.BulkCapacity}";
            else if (world.StoreOf(c, m) is { Amount: > 0 } st && st.Material != _pdMat)
                why = $"is fed {RecyclingTable.Current.NameOf(st.Material)}: another line";
            _pdFits.Add(why == null ? $"{d.Name} {seen}: yes" : $"{d.Name} {seen}: {why}");
            _pdFitOk.Add(why == null);
        }
    }

    /// <summary>The cuts' refusal and each linked Laser Cutter's answer (laser cutter phase 4); the key shown becomes the cut panel's.</summary>
    private void DesignerCutTexts(ConstructWorld world, Construct c, int part, in HullLoft.PanelShape shape)
    {
        _pdCutWhy = "";
        _pdPaneNote = "";
        _pdCutFits.Clear();
        _pdCutFitOk.Clear();
        _pdCutters = 0;
        var net = ConstructCrafting.NetOf(world, c, part);
        for (var i = 0; i < net.MachineN; i++)
            if (c.DefOf(net.Machines[i]).IsLaserCutter)
                _pdCutters++;
        if (_pdCuts.Count == 0 || _pdWhy.Length > 0)
            return;
        var key = CutKeyOf(shape);
        if (HullLoft.CutRefusal(key) is { } why)
        {
            _pdCutWhy = why;
            return;
        }
        _pdKey = key;
        DesignerPaneNote(key);
        if (_pdCutters == 0)
        {
            _pdCutFits.Add("No Laser Cutter linked: this design has cut-outs. Link one by a conveyor or a Line Console's wire into Orders.");
            _pdCutFitOk.Add(false);
            return;
        }
        // (read from the key and the preview's bounds: no run-time part is made for a key the sliders pass through)
        if (!HullLoft.CutJob(key, out _, out _, out _, out var edge, out var mm) || DesignerPreview.Base is not { } bd)
            return;
        var s = bd.BoundsMax - bd.BoundsMin;
        var job = new ConstructLaserCutter.Job { EdgeM = edge, Mm = mm, Longest = MathF.Max(s.X, MathF.Max(s.Y, s.Z)), Least = MathF.Min(s.X, MathF.Min(s.Y, s.Z)) };
        job.Next = s.X + s.Y + s.Z - job.Longest - job.Least;
        var seen = 0;
        for (var i = 0; i < net.MachineN && _pdCutFits.Count < 4; i++)
        {
            var d = c.DefOf(net.Machines[i]);
            if (!d.IsLaserCutter)
                continue;
            seen++;
            var ok = ConstructLaserCutter.Fits(d, job);
            _pdCutFitOk.Add(ok);
            _pdCutFits.Add(ok ? $"{d.Name} {seen}: yes, {ConstructLaserCutter.Seconds(job, d):0.#} s a cut ({edge:0.##} m of edge through {mm:0} mm)"
                : job.Least > d.CutMaxHeight + 0.005f ? $"{d.Name} {seen}: too tall for its bed: {job.Least:0.##} m" : $"{d.Name} {seen}: too large for its bed: {job.Longest:0.##} x {job.Next:0.##} m");
        }
    }

    private static readonly float4 PdHead = new(0.62f, 0.8f, 1f, 1f);

    private static ulong Mirrored(ulong p)
    {
        var m = 0UL;
        for (var k = 0; k < HullLoft.PanelProfilePoints; k++)
            m = HullLoft.ProfileSet(m, k, HullLoft.ProfileAt(p, HullLoft.PanelProfilePoints - 1 - k));
        return m;
    }

    /// <summary>
    /// A loft edge's profile, seen along the panel: its eight heights as handles over a line at 0 (the left side on the left), the curve the
    /// former follows through them. Drag a handle up or down (the nearest across where the drag starts); mirrored, its twin moves with it.
    /// </summary>
    private void ProfileEditor(string label, int handleBase, ref ulong profile)
    {
        ImGuiSafe.TextColored(Dim, label);
        var dl = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos();
        var size = new float2(MathF.Max(120 * _scale, ImGui.GetContentRegionAvail().X - 8 * _scale), 64 * _scale);
        ImGui.PushID(handleBase + 7000);
        ImGui.InvisibleButton("##pdprof"u8, size);
        ImGui.PopID();
        var active = ImGui.IsItemActive();
        var pad = 10 * _scale;
        float X(int k) => at.X + pad + (size.X - 2 * pad) * k / (HullLoft.PanelProfilePoints - 1);
        float Y(float cm) => at.Y + size.Y * 0.5f - cm / HullLoft.PanelMaxProfileCm * (size.Y * 0.5f - 6 * _scale);
        var mouse = ImGui.GetIO().MousePos;
        if (active)
        {
            if (_pdDragH < 0 || _pdDragH / HullLoft.PanelProfilePoints != handleBase / HullLoft.PanelProfilePoints)
            {
                var best = 0;
                for (var k = 1; k < HullLoft.PanelProfilePoints; k++)
                    if (MathF.Abs(X(k) - mouse.X) < MathF.Abs(X(best) - mouse.X))
                        best = k;
                _pdDragH = handleBase + best;
            }
            var cm = (int)MathF.Round((at.Y + size.Y * 0.5f - mouse.Y) / (size.Y * 0.5f - 6 * _scale) * HullLoft.PanelMaxProfileCm);
            profile = ProfileMove(profile, _pdDragH - handleBase, cm, _pdMirror);
        }
        else if (_pdDragH >= handleBase && _pdDragH < handleBase + HullLoft.PanelProfilePoints)
            _pdDragH = -1;
        dl.AddRectFilled(at, new float2(at.X + size.X, at.Y + size.Y), new ImColor8(34, 40, 50, 255), 4 * _scale);
        dl.AddLine(new float2(at.X + pad, Y(0)), new float2(at.X + size.X - pad, Y(0)), new ImColor8(90, 100, 115, 255), 1);
        var prev = default(float2);
        const int Steps = 28;
        for (var i = 0; i <= Steps; i++)
        {
            var x = i * 2f / Steps - 1;
            var pt = new float2(at.X + pad + (size.X - 2 * pad) * (x + 1) / 2, Y(HullLoft.ProfileHeight(profile, x) * 100));
            if (i > 0)
                dl.AddLine(prev, pt, new ImColor8(160, 200, 255, 255), 2 * _scale);
            prev = pt;
        }
        for (var k = 0; k < HullLoft.PanelProfilePoints; k++)
        {
            var c = new float2(X(k), Y(HullLoft.ProfileAt(profile, k)));
            ImDrawListExtensions.AddCircleFilled(dl, in c, 4.5f * _scale, _pdDragH == handleBase + k ? new ImColor8(255, 200, 90, 255) : new ImColor8(230, 236, 245, 255), 12);
        }
        if (ImGui.IsItemHovered() && !active)
            ImGui.SetTooltip("Drag a point up or down: the edge's height there, -99 to 99 cm."u8);
    }

    /// <summary>The panel (between NewFrame and Render). True while it stays open. Orders, saves and deletes go out after the drawing.</summary>
    public bool DrawDesigner(Game game, Construct c, int part, double now)
    {
        if (!DesignerOpen)
            return false;
        SandboxTiles.WantFew();
        var world = game.Constructs;
        DesignerBuild(world, c, part, now);
        var shape = DesignerShape;
        var pt0 = System.Diagnostics.Stopwatch.GetTimestamp();
        DesignerPreview.Want(shape, PreviewCut(shape)); // (with its cuts once they pass: laser cutter phase 4)
        DesignerPreview.Pump();
        var pms = System.Diagnostics.Stopwatch.GetElapsedTime(pt0).TotalMilliseconds;
        DesignerSpentMs += pms;
        DesignerWorstMs = Math.Max(DesignerWorstMs, pms);
        DesignerTexts(world, c, part);
        var q = c.Parts[part].Craft;
        CraftingOrders(c, q);
        var open = true;
        bool order = false, save = false, share = true;
        int cancel = 0, preset = -1, loadMine = -1, loadWorld = -1, delMine = -1, dropCut = -1, addCut = 0;
        var mat = _pdMat;
        int l = _pdL, w = _pdW, crown = _pdCrown, rake = _pdRake, mm = _pdMm, angle = _pdAngle, bendAt = _pdFoldAt, radius = _pdRadius, noseW = _pdNoseW;
        ulong back = _pdBack, nose = _pdNose;
        var vp = ImGui.GetMainViewport();
        var ww = MathF.Min(1180 * _scale, vp.WorkSize.X * 0.96f);
        var wh = MathF.Min(690 * _scale, vp.WorkSize.Y * 0.94f);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + (vp.WorkSize.X - ww) * 0.5f, vp.WorkPos.Y + (vp.WorkSize.Y - wh) * 0.5f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(ww, wh), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.96f);
        var view = default((float2, float2));
        if (ImGui.Begin("Panel Designer###paneldesigner"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            var foot = ImGui.GetFrameHeightWithSpacing() * 5.2f;
            var bodyH = MathF.Max(120 * _scale, ImGui.GetContentRegionAvail().Y - foot);
            var leftW = 250 * _scale;
            var rightW = 330 * _scale;
            var midW = MathF.Max(160 * _scale, ImGui.GetContentRegionAvail().X - leftW - rightW - ImGui.GetStyle().ItemSpacing.X * 2);
            // ---- Left: the presets and the designs.
            if (ImGui.BeginChild("##pdleft"u8, new float2(leftW, bodyH), ImGuiChildFlags.Borders))
            {
                ImGui.TextColored(PdHead, "Presets"u8);
                var dl = ImGui.GetWindowDrawList();
                var avail = ImGui.GetContentRegionAvail().X;
                var gap = 6 * _scale;
                var tw = (avail - gap) / 2;
                var icon = MathF.Min(tw * 0.7f, 76 * _scale);
                var th = icon + 28 * _scale;
                for (var k = 0; k < _pdTiles.Length; k++)
                {
                    if (k % 2 == 1)
                        ImGui.SameLine(0, gap);
                    var at = ImGui.GetCursorScreenPos();
                    ImGui.PushID(k);
                    if (ImGui.InvisibleButton("##pdp"u8, new float2(tw, th)))
                        preset = k;
                    ImGui.PopID();
                    var hot = ImGui.IsItemHovered();
                    if (hot)
                        ImGuiSafe.WrappedTooltip(_pdTiles[k].Tip, 360 * _scale);
                    RecipeTile(dl, _pdTiles[k], at, tw, th, icon, k == _pdKind, hot, setMark: false);
                }
                ImGui.Spacing();
                ImGui.TextColored(PdHead, "Your designs"u8);
                if (_pdMine.Count == 0)
                    ImGui.TextColored(Dim, "None yet: name one below and Save."u8);
                for (var k = 0; k < _pdMine.Count; k++)
                {
                    ImGui.PushID(1000 + k);
                    if (ImGui.SmallButton("x"u8))
                        delMine = k;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Delete this design from your library (the world keeps its copy)."u8);
                    ImGui.SameLine();
                    if (ImGui.Selectable(ImGuiSafe.Escape(_pdMine[k].Name), k == _pdMineSel))
                        loadMine = k;
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetTooltip(_pdMine[k].Key);
                    ImGui.PopID();
                }
                ImGui.Spacing();
                ImGui.TextColored(PdHead, "This world's designs"u8);
                if (_pdWorld.Count == 0)
                    ImGui.TextColored(Dim, "None shared yet."u8);
                for (var k = 0; k < _pdWorld.Count; k++)
                {
                    ImGui.PushID(2000 + k);
                    if (ImGui.Selectable(ImGuiSafe.Escape(_pdWorld[k].Name)))
                        loadWorld = k;
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetTooltip(_pdWorld[k].Key);
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
            ImGui.SameLine();
            // ---- Middle: the preview (drawn after the UI pass, in this rectangle).
            if (ImGui.BeginChild("##pdmid"u8, new float2(midW, bodyH), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                ImGui.Checkbox("On a frame"u8, ref DesignerFrame);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Shows the panel on a plain beam frame, to see how it would sit."u8);
                ImGui.SameLine();
                ImGui.Checkbox("Edge sockets"u8, ref DesignerSockets);
                ImGui.SameLine();
                ImGui.TextColored(Dim, DesignerPreview.Building ? "forming the preview..."u8 : "drag to turn, wheel to zoom"u8);
                var p0 = ImGui.GetCursorScreenPos();
                var sz = ImGui.GetContentRegionAvail();
                sz.Y -= ImGui.GetTextLineHeightWithSpacing();
                if (sz.X > 8 && sz.Y > 8)
                {
                    ImGui.InvisibleButton("##pdview"u8, sz);
                    var io = ImGui.GetIO();
                    if (ImGui.IsItemActive())
                    {
                        DesignerYaw -= io.MouseDelta.X * 0.01f;
                        DesignerPitch = Math.Clamp(DesignerPitch + io.MouseDelta.Y * 0.01f, -0.2f, 1.45f);
                        DesignerTouched = now;
                    }
                    if (ImGui.IsItemHovered() && io.MouseWheel != 0)
                    {
                        DesignerZoom = Math.Clamp(DesignerZoom * MathF.Pow(1.12f, -io.MouseWheel), 0.4f, 3f);
                        DesignerTouched = now;
                    }
                    ImGui.GetWindowDrawList().AddRectFilled(p0, new float2(p0.X + sz.X, p0.Y + sz.Y), new ImColor8(34, 40, 50, 255), 4 * _scale);
                    view = (p0, new float2(p0.X + sz.X, p0.Y + sz.Y));
                    ImGui.TextColored(Dim, "The bar on the floor is 1 m; the figure is a player (1.8 m)."u8);
                }
            }
            ImGui.EndChild();
            ImGui.SameLine();
            // ---- Right: the sliders, the materials, the readouts.
            if (ImGui.BeginChild("##pdright"u8, new float2(rightW, bodyH), ImGuiChildFlags.Borders))
            {
                ImGuiSafe.TextColored(PdHead, HullLoft.PanelPresets[_pdKind].Label);
                ImGui.PushItemWidth(190 * _scale);
                var longMax = Math.Min(HullLoft.PanelMaxCm, _pdMaxL);
                ImGui.SliderInt("Length"u8, ref l, HullLoft.PanelMinCm, longMax, "%d cm"u8);
                ImGui.SliderInt(_pdKind == HullLoft.PanelLoft ? "Back width"u8 : "Width"u8, ref w, HullLoft.PanelMinCm, longMax, "%d cm"u8);
                if (_pdKind != HullLoft.PanelLoft)
                {
                    ImGui.SliderInt("Crown"u8, ref crown, 0, Math.Max(1, Math.Min(HullLoft.PanelMaxCrownCm, _pdW / 2)), "%d cm"u8);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("How far the middle stands proud across the width: at most half the width, 80 cm."u8);
                }
                var rakeMax = Math.Min(HullLoft.PanelMaxRakeCm, _pdL);
                if (_pdKind == HullLoft.PanelLoft)
                {
                    ImGui.SliderInt("Nose width"u8, ref noseW, HullLoft.PanelMinCm, longMax, "%d cm"u8);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("How wide the nose edge is: the sheet tapers or flares straight from the back edge to it."u8);
                    ImGui.Checkbox("Mirrored"u8, ref _pdMirror);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("A handle moves its twin on the other side too, so the panel stays even."u8);
                    ProfileEditor("Back edge", 0, ref back);
                    ProfileEditor("Nose edge", HullLoft.PanelProfilePoints, ref nose);
                }
                else if (_pdKind == HullLoft.PanelBend)
                {
                    ImGui.SliderInt("Fold angle"u8, ref angle, -HullLoft.PanelMaxAngle, HullLoft.PanelMaxAngle, "%d deg"u8);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("How far the sheet folds along a line across it: up is positive. Within 90 degrees."u8);
                    ImGui.SliderInt("Fold at"u8, ref bendAt, 0, _pdL, "%d cm"u8);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Where the fold's middle lies along the sheet, from its back edge."u8);
                    ImGui.SliderInt("Fold radius"u8, ref radius, HullLoft.PanelMinRadiusCm, HullLoft.PanelMaxRadiusCm, "%d cm"u8);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("How tight the fold is: 2 cm is a crease, 100 cm a long sweep. The fold can't be longer than the sheet."u8);
                }
                else
                {
                    ImGui.SliderInt(_pdKind switch { 1 => "Step"u8, 2 => "Nose drop"u8, 3 => "Arch"u8, 4 => "Turn-in"u8, 6 => "Rise"u8, _ => "Rake"u8 }, ref rake, -rakeMax, rakeMax, "%d cm"u8);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("The preset's second number: a curve's or a roof's arch along its length, an S-bend's step, a hood's nose drop, a fender's arch, a door's turn-in, a cowl's rise. Within the panel's length, 200 cm."u8);
                }
                ImGui.SliderInt("Thickness"u8, ref mm, HullLoft.PanelMinMm, HullLoft.PanelMaxMm, "%d mm"u8);
                ImGui.PopItemWidth();
                CutEditor(ref dropCut, ref addCut);
                ImGui.PushTextWrapPos(0);
                ImGuiSafe.TextColored(_pdFormers == 0 ? CrWarn : Dim, _pdLimit);
                if (_pdAt.Length > 0)
                    ImGuiSafe.TextColored(CrWarn, _pdAt);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.TextColored(PdHead, "Material"u8);
                {
                    var dl = ImGui.GetWindowDrawList();
                    var s = 44 * _scale;
                    var names = RecyclingTable.Current;
                    for (var k = 0; k < _pdMats.Count; k++)
                    {
                        if (k % 6 != 0)
                            ImGui.SameLine(0, 6 * _scale);
                        var at = ImGui.GetCursorScreenPos();
                        ImGui.PushID(3000 + k);
                        if (ImGui.InvisibleButton("##pdm"u8, new float2(s, s)))
                            mat = _pdMats[k];
                        ImGui.PopID();
                        if (ImGui.IsItemHovered())
                            ImGuiSafe.SetTooltip(_pdMatFed[k] ? $"{names.NameOf(_pdMats[k])}: a linked former is fed it" : $"{names.NameOf(_pdMats[k])}: no linked former holds it yet");
                        var p1 = new float2(at.X + s, at.Y + s);
                        dl.AddRectFilled(at, p1, _pdMats[k] == _pdMat ? SbCardSel : SbCard, 4 * _scale);
                        Picture(dl, SandboxTiles.ResourceSlot(_pdMats[k]), Col8(names.ColourOf(_pdMats[k])), new float2(at.X + 3, at.Y + 3), new float2(p1.X - 3, p1.Y - 3), _pdMatFed[k] ? new ImColor8(255, 255, 255, 255) : new ImColor8(150, 150, 160, 120), null);
                        if (_pdMats[k] == _pdMat)
                            dl.AddRect(at, p1, SbOutline, 4 * _scale, ImDrawFlags.None, 2 * _scale);
                    }
                }
                ImGui.Spacing();
                ImGui.TextColored(PdHead, "It will be"u8);
                ImGui.PushTextWrapPos(0);
                if (_pdWhy.Length > 0)
                    ImGuiSafe.TextColored(CrWarn, _pdWhy);
                else
                {
                    ImGuiSafe.TextWrapped(_pdKey);
                    ImGuiSafe.TextColored(Dim, _pdNumbers);
                    for (var k = 0; k < _pdFits.Count; k++)
                        ImGuiSafe.TextColored(_pdFitOk[k] ? CrGood : CrWarn, _pdFits[k]);
                    for (var k = 0; k < _pdCutFits.Count; k++)
                        ImGuiSafe.TextColored(_pdCutFitOk[k] ? CrGood : CrWarn, _pdCutFits[k]);
                }
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
            // ---- Bottom: name and save, count and order, the open orders.
            _pdName ??= new ImInputString(PlayerBlueprints.MaxName + 1);
            ImGui.SetNextItemWidth(220 * _scale);
            ImGui.InputTextWithHint("##pdname"u8, "Design name..."u8, _pdName);
            ImGui.SameLine();
            save = ImGui.Button("Save design"u8, new float2(120 * _scale, 0)) | _pdPressSave;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Keeps it in your library and shares it with this world: anyone here can order it, and the world's save keeps it."u8);
            ImGui.SameLine(0, 24 * _scale);
            if (ImGui.Button("-"u8, new float2(28 * _scale, 0)))
                _pdCount--;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70 * _scale);
            ImGui.InputInt("##pdcount"u8, ref _pdCount, 0, 0);
            ImGui.SameLine();
            if (ImGui.Button("+"u8, new float2(28 * _scale, 0)))
                _pdCount++;
            _pdCount = Math.Clamp(_pdCount, 1, WorkOrders.MaxCount);
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, CrTabOn);
            ImGui.BeginDisabled(_pdWhy.Length > 0 || _pdCutWhy.Length > 0);
            order = ImGui.Button("ORDER"u8, new float2(130 * _scale, 0)) | _pdPressOrder;
            _pdPressSave = _pdPressOrder = false;
            ImGui.EndDisabled();
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (_pdSaved.Length > 0)
                ImGuiSafe.TextColored(CrGood, _pdSaved);
            else
                ImGui.TextColored(Dim, _pdCuts.Count > 0 ? "Cut by a linked Laser Cutter from a formed blank; collect it at its rack or in this tray."u8 : "Formed by a linked Panel Former; collect it at its rack or in this tray."u8);
            if (ImGui.BeginChild("##pdorders"u8, new float2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.Borders))
            {
                if (q is { Count: > 0 })
                    cancel = CraftingOrderList(q);
                else
                    ImGui.TextColored(Dim, "No open orders. Set the panel, then ORDER."u8);
            }
            ImGui.EndChild();
            if (ImGui.Button("Close"u8))
                open = false;
        }
        ImGui.End();
        DesignerView = view;
        // After the drawing: what the clicks asked for.
        if (preset >= 0)
            DesignerPreset(preset);
        else if (loadMine >= 0 && loadMine < _pdMine.Count)
        {
            DesignerLoad(_pdMine[loadMine]);
            _pdMineSel = loadMine;
        }
        else if (loadWorld >= 0 && loadWorld < _pdWorld.Count)
            DesignerLoad(_pdWorld[loadWorld]);
        else
        {
            (_pdL, _pdW, _pdCrown, _pdRake, _pdMm, _pdMat) = (l, w, crown, rake, mm, mat);
            (_pdAngle, _pdFoldAt, _pdRadius) = (angle, bendAt, radius);
            (_pdNoseW, _pdBack, _pdNose) = (noseW, back, nose);
            share = false;
        }
        CutApply(dropCut, addCut);
        DesignerClamp();
        if (delMine >= 0 && delMine < _pdMine.Count)
        {
            _pdMine.RemoveAt(delMine);
            _pdMineSel = -1;
            _pdSaved = PanelDesigns.SaveLibrary(_pdMine) is { } bad ? "Not saved: " + bad : "Deleted from your library.";
        }
        if (save && !share)
        {
            var name = ImGuiSafe.Text(_pdName!).Trim();
            var key = DesignerKey;
            if (PanelDesigns.Refusal(name, key) is { } why)
                _pdSaved = "Not saved: " + why;
            else
            {
                _pdMine.RemoveAll(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
                if (_pdMine.Count >= PanelDesigns.MaxLibrary)
                    _pdSaved = $"Not saved: your library holds {PanelDesigns.MaxLibrary} designs";
                else
                {
                    _pdMine.Add(new PanelDesign(name, key));
                    _pdMine.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    _pdMineSel = _pdMine.FindIndex(d => d.Name == name);
                    _pdSaved = PanelDesigns.SaveLibrary(_pdMine) is { } bad ? "Not saved: " + bad : $"Saved \"{name}\" and shared it with this world.";
                    game.SandboxPanelDesign(name, key);
                }
            }
        }
        if (order && !share && HullLoft.PanelRefusal(DesignerShape) == null && (_pdCuts.Count == 0 || HullLoft.CutRefusal(DesignerKey) == null))
        {
            game.SandboxCraftOrder(_pdBody, _pdPart, ConstructPanelFormer.Order(DesignerKey, _pdCount));
            // (phase 6) One pane per glazed opening for each panel: equal openings share one order.
            if (_pdCuts.Count > 0)
                foreach (var (pane, n) in DesignerPanes())
                    game.SandboxCraftOrder(_pdBody, _pdPart, ConstructPanelFormer.Order(pane, Math.Min(WorkOrders.MaxCount, n * _pdCount)));
            _pdSaved = "";
        }
        if (cancel != 0)
            game.SandboxCraftCancel(_pdBody, _pdPart, (ushort)cancel);
        if (!open)
            CloseDesigner();
        return open;
    }
}
