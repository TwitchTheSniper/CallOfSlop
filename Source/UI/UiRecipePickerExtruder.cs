using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Extruder's recipe picker (docs/sandbox/design/recipe-select-notes.md, option A, the user's choice of 2026-11-13): one tile per
/// Profile, its picture the section itself (drawn from its 8 by 8 mask; Round, Pipe and the foils drawn as their shape); under the tiles
/// Width and Height steppers (a Round's or a Pipe's diameter; a Pipe's wall), held to what this machine's die makes now, the piece that
/// will come out in words, then Set. Custom opens the cell grid beside the steppers (the configure panel's own, <see cref="SectionCells"/>).
/// Set sends the whole section as one act (CActKind.ExtruderSection), which the server checks as <see cref="ExtruderPicks.Refusal"/> does
/// here. Opened from the machine's own Recipe button (Program.MachineRecipePicker); a frame allocates nothing until a value changes.
/// </summary>
public sealed partial class Ui
{
    private bool _exOpen;
    private ConstructPartDef? _exDef;
    private ExtruderPick _exPick, _exSet; // the pick being made; the machine's settings when it opened (that tile says "set")
    private string[] _exNames = [];
    private string _exTitle = "";
    private int _exDieW, _exDieH, _exLongCm, _exTestSet;
    private string _exLabel = "", _exDieText = "";
    private string? _exWhy;
    private ExtruderPick _exLabelOf = new(255, 0, 0, 0, 0);
    private int _exDieOf = -1;
    private (int V, string S) _exWText = (-1, ""), _exHText = (-1, ""), _exWallText = (-1, "");

    private static readonly ImColor8 ExSection = new(214, 160, 92, 255), ExBore = new(20, 22, 26, 255);

    /// <summary>Opens the picker on Extruder <paramref name="machine"/> (its own Recipe button), on the section and sizes it has now.</summary>
    public void OpenExtruderPicker(Construct c, int machine)
    {
        var d = c.DefOf(machine);
        _rcpBody = c.Id;
        _rcpMachine = _rcpConsole = machine;
        _rcpSlot = -1;
        _rcpSame = 1;
        _rcpFromConfig = false;
        _rcpAllLabel = "";
        _rcpVariant = "";
        _rcp = null;
        _rcpDef = null; // (a later recipe picker starts with its search cleared)
        _exDef = d;
        _exNames = d.Config[d.CfgSlot[(int)CfgKey.Profile]].Options;
        _exTitle = $"Section: {d.Name}###rpick";
        _exSet = _exPick = ExtruderPicks.Current(c, machine);
        if (_exPick.Profile == Extrusion.Presets.Length && Extrusion.Refusal(_exPick.Mask) != null)
            _exPick = _exPick with { Mask = Extrusion.Presets[0].Mask };
        _exDieOf = -1;
        _exLabelOf = new ExtruderPick(255, 0, 0, 0, 0);
        _exTestSet = 0;
        _rcpTestPick = -1;
        _exOpen = true;
        _rcpCut = false;
        RecipePickerOpen = true;
    }

    private bool IsCustom(int profile) => profile == Extrusion.Presets.Length;

    /// <summary>The window; true while it stays open. Set sends the pick and closes it.</summary>
    private bool DrawExtruderPicker(Game game)
    {
        if (_exDef is not { } d || game.Constructs.Get(_rcpBody) is not { } c || _rcpMachine >= c.PartCount || c.DefOf(_rcpMachine) != d)
        {
            CloseRecipePicker();
            return false;
        }
        var (dw, dh) = ExtruderPicks.DieCm(c, _rcpMachine); // (Die Frames welded on or shot off while it is open)
        _exDieW = dw;
        _exDieH = dh;
        _exLongCm = ConstructExtruder.MaxLengthCm(c, _rcpMachine); // (and Run-out Tables)
        if (_rcpTestPick >= 0)
        {
            if (_rcpTestPick < _exNames.Length)
                SelectProfile(_rcpTestPick); // (a window shot's "picker N": as a click on that tile)
            _rcpTestPick = -1;
        }
        HoldSizes();
        var open = true;
        var send = false;
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(900 * _scale, vp.WorkSize.X * 0.9f);
        var h = MathF.Min(640 * _scale, vp.WorkSize.Y * 0.88f);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + (vp.WorkSize.X - w) * 0.5f, vp.WorkPos.Y + (vp.WorkSize.Y - h) * 0.5f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.95f);
        ImGui.SetNextWindowFocus();
        if (ImGui.Begin(_exTitle, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            ImGui.TextColored(Dim, "Pick a section, then its size, then Set. Esc closes."u8);
            ImGui.Separator();
            SectionTiles();
            ImGui.Separator();
            Texts(c, d);
            ImGui.BeginGroup();
            var shape = Extrusion.ShapeOf(_exPick.Profile);
            var wv = (int)_exPick.W;
            var round = shape != ExShape.Section;
            if (Stepper(round ? "Diameter"u8 : "Width"u8, "##exw"u8, ref wv, 5, Extrusion.MinSectionCm, round ? Math.Min(dw, dh) : dw, ref _exWText, true))
                _exPick = _exPick with { W = (ushort)wv };
            if (!round)
            {
                var hv = (int)_exPick.H;
                if (Stepper("Height"u8, "##exh"u8, ref hv, 5, Extrusion.MinSectionCm, dh, ref _exHText, true))
                    _exPick = _exPick with { H = (ushort)hv };
            }
            if (shape == ExShape.Pipe)
            {
                var lv = (int)_exPick.Wall;
                if (Stepper("Pipe wall"u8, "##exl"u8, ref lv, 1, 1, ExtruderPicks.MaxWallCm(_exPick.W), ref _exWallText, false))
                    _exPick = _exPick with { Wall = (ushort)lv };
            }
            ImGui.Spacing();
            ImGui.Text("Makes: "u8);
            ImGui.SameLine();
            ImGuiSafe.TextColored(new float4(0.55f, 0.86f, 0.6f, 1), _exLabel);
            ImGuiSafe.TextColored(Dim, _exDieText);
            if (_exWhy != null)
                ImGuiSafe.TextColored(new float4(0.94f, 0.43f, 0.35f, 1), _exWhy);
            ImGui.EndGroup();
            if (IsCustom(_exPick.Profile))
            {
                // Custom: the configure panel's cell grid, in this window (a click flips that cell).
                ImGui.SameLine(0, 40 * _scale);
                ImGui.BeginGroup();
                ImGui.TextColored(Dim, "Cells: click to fill or clear"u8);
                var cell = SectionCells(_exPick.Mask, 18 * _scale);
                if (cell >= 0)
                    _exPick = _exPick with { Mask = _exPick.Mask ^ 1ul << cell };
                SectionCellsRefusal(_exPick.Mask);
                ImGui.EndGroup();
            }
            ImGui.Spacing();
            ImGui.BeginDisabled(_exWhy != null);
            send = ImGui.Button("Set"u8, new float2(110 * _scale, 0));
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close"u8))
                open = false;
        }
        ImGui.End();
        if (_exTestSet > 0)
        {
            send |= _exWhy == null; // (a window shot's "picker set": as a click on Set)
            _exTestSet = 0;
        }
        if (send && _exWhy == null)
        {
            game.SandboxExtruderSection(_rcpBody, _rcpMachine, _exPick);
            open = false;
        }
        if (!open)
            CloseRecipePicker();
        return open;
    }

    /// <summary>A tile clicked: its Profile. Custom starts from the section shown when its own cells can't be made.</summary>
    private void SelectProfile(int k)
    {
        if (IsCustom(k) && !IsCustom(_exPick.Profile) && (_exPick.Mask == 0 || Extrusion.Refusal(_exPick.Mask) != null))
            _exPick = _exPick with { Mask = ExtruderPicks.MaskOf(_exPick.Profile, _exPick) };
        _exPick = _exPick with { Profile = (byte)k };
        if (Extrusion.ShapeOf(k) == ExShape.Pipe && _exPick.Wall > ExtruderPicks.MaxWallCm(_exPick.W))
            _exPick = _exPick with { Wall = (ushort)ExtruderPicks.MaxWallCm(_exPick.W) };
    }

    /// <summary>The sizes held to the die as it is now (a Round's or a Pipe's diameter to its smaller side), a Pipe's wall to its bore.</summary>
    private void HoldSizes()
    {
        var round = Extrusion.ShapeOf(_exPick.Profile) != ExShape.Section;
        var w = Math.Clamp((int)_exPick.W, Extrusion.MinSectionCm, round ? Math.Min(_exDieW, _exDieH) : _exDieW);
        var h = Math.Clamp((int)_exPick.H, Extrusion.MinSectionCm, _exDieH);
        var wall = Math.Clamp((int)_exPick.Wall, 1, ExtruderPicks.MaxWallCm(w));
        if (w != _exPick.W || h != _exPick.H || wall != _exPick.Wall)
            _exPick = _exPick with { W = (ushort)w, H = (ushort)h, Wall = (ushort)wall };
    }

    /// <summary>The piece's words, the die's and the refusal, made again only when the pick or the die changes.</summary>
    private void Texts(Construct c, ConstructPartDef d)
    {
        if (_exPick != _exLabelOf)
        {
            _exLabelOf = _exPick;
            _exLabel = ExtruderPicks.Label(_exPick);
            _exWhy = ExtruderPicks.Refusal(c, _rcpMachine, _exPick);
            if (_exWhy != null)
                _exWhy = Cap(_exWhy) + ".";
        }
        var dieKey = HashCode.Combine(_exDieW, _exDieH, _exLongCm);
        if (dieKey != _exDieOf)
        {
            _exDieOf = dieKey;
            _exDieText = $"Its die makes up to {_exDieW / 100f:0.##} x {_exDieH / 100f:0.##} m"
                + (ConstructExtruder.WidenWith(d) is { } widen && _exDieW < (int)MathF.Round(ConstructExtruder.MostSection(d).X * 100) ? $"; {widen} widen it to {ConstructExtruder.MostSection(d).X:0.##} m" : "")
                + $". Pieces up to {_exLongCm / 100f:0.#} m"
                + (ConstructExtruder.LengthenWith(d) is { } longer && _exLongCm < (int)MathF.Round(ConstructExtruder.MostLength(d) * 100) ? $"; {longer} make them {ConstructExtruder.MostLength(d):0.#} m." : ".");
            _exLabelOf = new ExtruderPick(255, 0, 0, 0, 0); // (the refusal follows the die)
        }
    }

    /// <summary>A size row: its label, a minus and a plus button round its value (cm; shown in m or cm), held to [lo, hi]. True when it changed.</summary>
    private bool Stepper(ReadOnlySpan<byte> label, ReadOnlySpan<byte> id, ref int v, int step, int lo, int hi, ref (int V, string S) text, bool metres)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(110 * _scale);
        ImGui.PushID(id);
        var was = v;
        ImGui.BeginDisabled(v <= lo);
        if (ImGui.Button("-"u8, new float2(30 * _scale, 0)))
            v = Math.Max(lo, v - step);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (text.V != v)
            text = (v, metres ? $"{v / 100f:0.00} m" : $"{v} cm");
        ImGui.AlignTextToFramePadding();
        ImGui.Text(text.S);
        ImGui.SameLine(110 * _scale + 30 * _scale + 90 * _scale);
        ImGui.BeginDisabled(v >= hi);
        if (ImGui.Button("+"u8, new float2(30 * _scale, 0)))
            v = Math.Min(hi, v + step);
        ImGui.EndDisabled();
        ImGui.PopID();
        return v != was;
    }

    /// <summary>One tile per Profile, in rows: its section drawn, its name under it (shrunk to fit), the machine's own marked "set".</summary>
    private void SectionTiles()
    {
        var dl = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 8 * _scale;
        var tw = 104 * _scale;
        var cols = Math.Max(1, (int)((avail + gap) / (tw + gap)));
        tw = (avail - gap * (cols - 1)) / cols;
        var icon = MathF.Min(tw * 0.62f, 64 * _scale);
        var th = icon + 34 * _scale;
        var start = ImGui.GetCursorScreenPos();
        var rows = (_exNames.Length + cols - 1) / cols;
        for (var k = 0; k < _exNames.Length; k++)
        {
            var at = new float2(start.X + k % cols * (tw + gap), start.Y + k / cols * (th + gap));
            ImGui.SetCursorScreenPos(at);
            ImGui.PushID(k);
            var clicked = ImGui.InvisibleButton("##ext"u8, new float2(tw, th));
            ImGui.PopID();
            var hot = ImGui.IsItemHovered();
            if (clicked)
                SelectProfile(k);
            if (hot)
                ImGuiSafe.SetTooltip(SectionTip(k));
            var sel = k == _exPick.Profile;
            var p1 = new float2(at.X + tw, at.Y + th);
            var r = 5 * _scale;
            dl.AddRectFilled(at, p1, sel ? SbCardSel : hot ? SbCardHot : SbCard, r);
            var i0 = new float2(at.X + (tw - icon) * 0.5f, at.Y + 5 * _scale);
            DrawSection(dl, k, i0, icon);
            TileName(dl, _exNames[k], at.X, tw, at.Y + icon + 9 * _scale, SbText, new float4(at.X + 4 * _scale, at.Y, p1.X - 4 * _scale, p1.Y));
            if (k == _exSet.Profile)
            {
                ImGui.PushFont(default, 13);
                DlText(dl, new float2(p1.X - 6 * _scale - ImGui.CalcTextSize("set"u8).X, at.Y + 3 * _scale), RpCurrent, "set"u8);
                ImGui.PopFont();
            }
            if (sel)
                dl.AddRect(at, p1, SbOutline, r, ImDrawFlags.None, 2.5f * _scale);
            else if (hot)
                dl.AddRect(at, p1, SbHotOutline, r, ImDrawFlags.None, 1.5f * _scale);
        }
        ImGui.SetCursorScreenPos(new float2(start.X, start.Y + rows * (th + gap)));
        ImGui.Dummy(new float2(1, 1));
    }

    private string SectionTip(int k) =>
        IsCustom(k) ? "Custom: your own section, drawn on the 8 by 8 cell grid that opens beside the sizes."
        : Extrusion.ShapeOf(k) == ExShape.Round ? "Round: a solid rod; Diameter is its size."
        : Extrusion.ShapeOf(k) == ExShape.Pipe ? "Pipe: a tube; Diameter and its wall (it leaves a bore of at least 5 cm)."
        : k >= Extrusion.AirfoilProfile(0) ? $"{_exNames[k]}: a wing's section, Width its chord and Height its thickness."
        : $"{_exNames[k]}: Width across, Height up.";

    /// <summary>
    /// A Profile's picture in a square <paramref name="size"/> wide at <paramref name="p0"/>: a grid section cell by cell (Custom: the cells
    /// being drawn), a Round as a disc, a Pipe as a ring of its wall, a foil as its outline (from its thickness and camber, not the grid).
    /// </summary>
    private void DrawSection(ImDrawListPtr dl, int profile, float2 p0, float size)
    {
        dl.AddRectFilled(p0, new float2(p0.X + size, p0.Y + size), new ImColor8(14, 16, 20, 255), 3 * _scale);
        var pad = size * 0.08f;
        var s = size - 2 * pad;
        var o = new float2(p0.X + pad, p0.Y + pad);
        var centre = new float2(o.X + s * 0.5f, o.Y + s * 0.5f);
        var shape = Extrusion.ShapeOf(profile);
        if (shape == ExShape.Round)
        {
            dl.AddCircleFilled(centre, s * 0.5f, ExSection, 32);
            return;
        }
        if (shape == ExShape.Pipe)
        {
            var share = _exPick.Profile == profile ? Math.Clamp(_exPick.Wall / (float)Math.Max(1, (int)_exPick.W), 0.04f, 0.5f) : 0.12f;
            dl.AddCircleFilled(centre, s * 0.5f, ExSection, 32);
            dl.AddCircleFilled(centre, s * 0.5f * (1 - 2 * share), ExBore, 32);
            return;
        }
        var foil = profile - Extrusion.AirfoilProfile(0);
        if (foil >= 0 && !IsCustom(profile))
        {
            Foil(dl, foil, o, s);
            return;
        }
        var mask = IsCustom(profile) ? _exPick.Mask : profile < Extrusion.Presets.Length ? Extrusion.Presets[profile].Mask : 0ul;
        var cell = s / Extrusion.Grid;
        for (var y = 0; y < Extrusion.Grid; y++)
            for (var x = 0; x < Extrusion.Grid; x++)
                if ((mask >> (y * Extrusion.Grid + x) & 1) != 0)
                {
                    var a = new float2(o.X + x * cell, o.Y + (Extrusion.Grid - 1 - y) * cell); // (y up: row 0 at the bottom)
                    dl.AddRectFilled(a, new float2(a.X + cell + 0.5f, a.Y + cell + 0.5f), ExSection);
                }
    }

    /// <summary>
    /// A foil's outline: a NACA four-digit thickness about a camber line, in thin strips along the chord. The three shipped foils (Extrusion.json's
    /// "airfoils": cambered, symmetric, thin) by their place; a later row draws as the first.
    /// </summary>
    private static void Foil(ImDrawListPtr dl, int k, float2 o, float s)
    {
        var (t, m) = k switch { 1 => (0.15f, 0f), 2 => (0.07f, 0.02f), _ => (0.12f, 0.05f) };
        const float p = 0.4f;
        const int n = 24;
        var cy = o.Y + s * 0.55f;
        float Thick(float x) => 5 * t * (0.2969f * MathF.Sqrt(x) - 0.126f * x - 0.3516f * x * x + 0.2843f * x * x * x - 0.1036f * x * x * x * x);
        float Camber(float x) => m == 0 ? 0 : x < p ? m / (p * p) * (2 * p * x - x * x) : m / ((1 - p) * (1 - p)) * (1 - 2 * p + 2 * p * x - x * x);
        float2 Up(float x) => new(o.X + x * s, cy - (Camber(x) + Thick(x)) * s);
        float2 Down(float x) => new(o.X + x * s, cy - (Camber(x) - Thick(x)) * s);
        for (var i = 0; i < n; i++)
        {
            // (strips closer at the nose, where it bends most)
            var x0 = MathF.Pow(i / (float)n, 1.6f);
            var x1 = MathF.Pow((i + 1) / (float)n, 1.6f);
            dl.AddQuadFilled(Up(x0), Up(x1), Down(x1), Down(x0), ExSection);
        }
    }

    /// <summary>Window shots: the open Extruder picker as text (its tiles, the pick, the piece, the die, any refusal).</summary>
    private string ExtruderPickerDump() =>
        $"{_exTitle.Split("###")[0]}, {_exNames.Length} tiles: {string.Join("; ", _exNames.Select((n, k) => $"{k} {n}{(k == _exSet.Profile ? " (set)" : "")}"))}"
        + $" | picked {_exPick.Profile} {_exNames.ElementAtOrDefault(_exPick.Profile)}, {_exPick.W} x {_exPick.H} cm, wall {_exPick.Wall} cm, cells {_exPick.Mask:x16}"
        + $" | makes {_exLabel} | die {_exDieW} x {_exDieH} cm{(_exWhy != null ? " | refused: " + _exWhy : "")}";

    /// <summary>
    /// Window shots (--do "picker size W H [WALL]", "picker cell N", "picker set"): the steppers set to those cm (held to the die, as the buttons
    /// are), a Custom grid's cell flipped, or Set clicked next frame. Null when the Extruder picker isn't open.
    /// </summary>
    public string? ExtruderPickerDo(string arg)
    {
        if (!RecipePickerOpen || !_exOpen)
            return null;
        var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int Num(int i, int or) => words.Length > i && int.TryParse(words[i], out var v) ? v : or;
        switch (words.Length > 0 ? words[0] : "")
        {
            case "size":
                _exPick = _exPick with { W = (ushort)Math.Max(0, Num(1, _exPick.W)), H = (ushort)Math.Max(0, Num(2, _exPick.H)), Wall = (ushort)Math.Max(0, Num(3, _exPick.Wall)) };
                return $"size {_exPick.W} x {_exPick.H} cm, wall {_exPick.Wall} cm (held to the die next frame)";
            case "cell" when Num(1, -1) is var b and >= 0 and < 64:
                if (!IsCustom(_exPick.Profile))
                    SelectProfile(Extrusion.Presets.Length);
                _exPick = _exPick with { Mask = _exPick.Mask ^ 1ul << b };
                return $"cell {b}: cells {_exPick.Mask:x16}";
            case "set":
                _exTestSet = 1;
                return "set clicked";
            default:
                return "picker size W H [WALL] | cell N | set";
        }
    }
}
