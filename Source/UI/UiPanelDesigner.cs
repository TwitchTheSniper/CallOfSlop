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

    /// <summary>The preview's rectangle in window points (min, max) as drawn last frame; zero size: none.</summary>
    public (float2 Min, float2 Max) DesignerView { get; private set; }

    /// <summary>The preview's part builder, camera and switches (Program.SandboxPanelDesigner.cs draws it).</summary>
    public readonly HullLoft.PanelPreview DesignerPreview = new();
    public float DesignerYaw = 0.7f, DesignerPitch = 0.42f, DesignerZoom = 1f;
    public bool DesignerFrame, DesignerSockets = true;
    public double DesignerTouched; // when the player last turned it (it turns by itself again a little later)

    /// <summary>The designer the panel is open on.</summary>
    public (ushort Body, int Part) DesignerTarget => (_pdBody, _pdPart);

    /// <summary>Probes: the shape on the sliders, the designs listed and the text under the sliders.</summary>
    public HullLoft.PanelShape DesignerShape => new(_pdKind, _pdL, _pdW, _pdCrown, _pdRake, _pdMm, RecyclingTable.Current.NameOf(_pdMat));
    public int DesignerDesigns => _pdMine.Count;
    public string DesignerNote => _pdWhy;

    private ushort _pdBody;
    private int _pdPart = -1, _pdKind, _pdL = 150, _pdW = 100, _pdCrown = 10, _pdRake, _pdMm = 20, _pdCount = 1;
    private byte _pdMat;
    private int _pdStamp, _pdTextKey, _pdMaxL = HullLoft.PanelMaxCm, _pdMaxW = HullLoft.PanelMaxCm, _pdFormers, _pdMineSel = -1;
    private double _pdBuiltAt;
    private bool _pdBuilt;
    private string _pdKey = "", _pdNumbers = "", _pdWhy = "", _pdSaved = "", _pdLimit = "";
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
        }
        DesignerClamp();
    }

    private void DesignerPreset(int kind)
    {
        var s = HullLoft.PanelDefault(kind);
        (_pdKind, _pdL, _pdW, _pdCrown, _pdRake, _pdMm) = (s.Kind, s.L, s.W, s.Crown, s.Rake, s.Mm);
        _pdMineSel = -1;
    }

    private void DesignerLoad(in PanelDesign d)
    {
        if (HullLoft.PanelOf(d.Key) is not { } s)
            return;
        (_pdKind, _pdL, _pdW, _pdCrown, _pdRake, _pdMm) = (s.Kind, s.L, s.W, s.Crown, s.Rake, s.Mm);
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
        var rake = Math.Min(HullLoft.PanelMaxRakeCm, _pdL);
        _pdRake = Math.Clamp(_pdRake, -rake, rake);
        _pdMm = Math.Clamp(_pdMm, HullLoft.PanelMinMm, HullLoft.PanelMaxMm);
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
            if (!d.IsPanelFormer)
                continue;
            _pdFormers++;
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
        var key = HashCode.Combine(_pdKind, _pdL, _pdW, _pdCrown, _pdRake, _pdMm, _pdMat, _pdStamp);
        var bd = DesignerPreview.Base;
        key = key * 31 + (bd == null ? 0 : 1);
        if (key == _pdTextKey)
            return;
        _pdTextKey = key;
        var shape = DesignerShape;
        _pdWhy = HullLoft.PanelRefusal(shape) ?? "";
        _pdKey = _pdWhy.Length == 0 ? HullLoft.PanelKey(shape) : "";
        _pdFits.Clear();
        _pdFitOk.Clear();
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
            if (!d.IsPanelFormer)
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

    private static readonly float4 PdHead = new(0.62f, 0.8f, 1f, 1f);

    /// <summary>The panel (between NewFrame and Render). True while it stays open. Orders, saves and deletes go out after the drawing.</summary>
    public bool DrawDesigner(Game game, Construct c, int part, double now)
    {
        if (!DesignerOpen)
            return false;
        SandboxTiles.WantFew();
        var world = game.Constructs;
        DesignerBuild(world, c, part, now);
        var shape = DesignerShape;
        DesignerPreview.Want(shape);
        DesignerPreview.Pump();
        DesignerTexts(world, c, part);
        var q = c.Parts[part].Craft;
        CraftingOrders(c, q);
        var open = true;
        bool order = false, save = false, share = true;
        int cancel = 0, preset = -1, loadMine = -1, loadWorld = -1, delMine = -1;
        var mat = _pdMat;
        int l = _pdL, w = _pdW, crown = _pdCrown, rake = _pdRake, mm = _pdMm;
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
                ImGui.SliderInt("Width"u8, ref w, HullLoft.PanelMinCm, longMax, "%d cm"u8);
                ImGui.SliderInt("Crown"u8, ref crown, 0, Math.Max(1, Math.Min(HullLoft.PanelMaxCrownCm, _pdW / 2)), "%d cm"u8);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("How far the middle stands proud across the width: at most half the width, 80 cm."u8);
                var rakeMax = Math.Min(HullLoft.PanelMaxRakeCm, _pdL);
                ImGui.SliderInt(_pdKind switch { 1 => "Step"u8, 2 => "Nose drop"u8, 3 => "Arch"u8, 4 => "Turn-in"u8, 6 => "Rise"u8, _ => "Rake"u8 }, ref rake, -rakeMax, rakeMax, "%d cm"u8);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("The preset's second number: a curve's or a roof's arch along its length, an S-bend's step, a hood's nose drop, a fender's arch, a door's turn-in, a cowl's rise. Within the panel's length, 200 cm."u8);
                ImGui.SliderInt("Thickness"u8, ref mm, HullLoft.PanelMinMm, HullLoft.PanelMaxMm, "%d mm"u8);
                ImGui.PopItemWidth();
                ImGui.PushTextWrapPos(0);
                ImGuiSafe.TextColored(_pdFormers == 0 ? CrWarn : Dim, _pdLimit);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.TextColored(PdHead, "Material"u8);
                {
                    var dl = ImGui.GetWindowDrawList();
                    var s = 44 * _scale;
                    var names = RecyclingTable.Current;
                    for (var k = 0; k < _pdMats.Count; k++)
                    {
                        if (k > 0)
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
                }
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
            // ---- Bottom: name and save, count and order, the open orders.
            _pdName ??= new ImInputString(PlayerBlueprints.MaxName + 1);
            ImGui.SetNextItemWidth(220 * _scale);
            ImGui.InputTextWithHint("##pdname"u8, "Design name..."u8, _pdName);
            ImGui.SameLine();
            save = ImGui.Button("Save design"u8, new float2(120 * _scale, 0));
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
            ImGui.BeginDisabled(_pdWhy.Length > 0);
            order = ImGui.Button("ORDER"u8, new float2(130 * _scale, 0));
            ImGui.EndDisabled();
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (_pdSaved.Length > 0)
                ImGuiSafe.TextColored(CrGood, _pdSaved);
            else
                ImGui.TextColored(Dim, "A linked Panel Former forms it; it rides to its rack, or to this desk's tray by a conveyor."u8);
            if (ImGui.BeginChild("##pdorders"u8, new float2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.Borders))
                cancel = CraftingOrderList(q);
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
            share = false;
        }
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
            var key = HullLoft.PanelKey(DesignerShape);
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
        if (order && !share && HullLoft.PanelRefusal(DesignerShape) == null)
        {
            game.SandboxCraftOrder(_pdBody, _pdPart, ConstructPanelFormer.Order(HullLoft.PanelKey(DesignerShape), _pdCount));
            _pdSaved = "";
        }
        if (cancel != 0)
            game.SandboxCraftCancel(_pdBody, _pdPart, (ushort)cancel);
        if (!open)
            CloseDesigner();
        return open;
    }
}
