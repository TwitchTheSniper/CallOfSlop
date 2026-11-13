using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The recipe picker (docs/sandbox/constructs/production.md "The recipe picker"): a refiner's or an Assembler's recipe chosen from a grid
/// of tiles like the inventory's, in place of stepping through them. Each tile is the output's picture (a loose resource's icon,
/// ResourceIcons.cs, or the part's tile; both from <see cref="SandboxTiles"/>), its name, the inputs as small icons with their amounts and the
/// time a batch takes on this machine; Auto (a refiner) or None (an Assembler) comes first; the current recipe is outlined; a recipe the
/// machine's tier can't make is dim, with the reason on hover. A long list gets a search box and the inventory's category tabs. A click
/// picks and closes; Esc or Close leaves it as it was.
/// <para>
/// Opened from a Line Console's Recipe button (the console's picked machine; the pick goes to the server as
/// <see cref="CActKind.LineRecipe"/>, optionally to every machine of that kind on the line) or from the configure panel's recipe row
/// (the configure tool's own request). The tiles are made once per machine kind; a frame of it allocates nothing.
/// </para>
/// </summary>
public sealed partial class Ui
{
    /// <summary>A recipe tile, its texts made once.</summary>
    private sealed class RpTile
    {
        public int Value = -1; // the recipe setting's value; -1: this machine can't make it (dim)
        public string Name = "", Search = "", Tip = "", Out = "", Time = "";
        public int Group; // index into RpSet.Groups (0: all)
        public int Slot = -1; // the output's tile in the atlas (-1: none: Auto or None)
        public ImColor8 Swatch;
        public int[] InSlot = [];
        public string[] InText = [];
        public ImColor8[] InSwatch = [];
    }

    /// <summary>A machine kind's tiles and categories.</summary>
    private sealed class RpSet
    {
        public RpTile[] Tiles = [];
        public string[] Groups = ["All"];
        public string Title = "";
        public int Key;
    }

    public bool RecipePickerOpen { get; private set; }

    private readonly Dictionary<(ConstructPartDef, string), RpSet> _rcpSets = new(); // (by machine kind and variant: a press's fitted die)
    private RpSet? _rcp;
    private ConstructPartDef? _rcpDef;
    private string _rcpVariant = "";
    private ushort _rcpBody;
    private int _rcpConsole = -1, _rcpMachine = -1, _rcpSlot = -1, _rcpSame;
    private bool _rcpAll, _rcpFromConfig;
    private int _rcpGroup, _rcpFilterVersion = 1, _rcpFilterBuilt;
    private ImInputString? _rcpSearchInput;
    private string _rcpSearch = "";
    private int[] _rcpVisible = [];
    private int _rcpVisibleCount;
    private string _rcpAllLabel = "";
    private bool _rcpFocusSearch;

    /// <summary>The tiles drawn with their picture and as a plain swatch last frame (smoke log).</summary>
    public int RecipePickerTiles { get; private set; }

    public int RecipePickerPictures { get; private set; }

    /// <summary>
    /// Machines the picker serves: a refiner with recipes to choose, an Assembler, a Panel Former (its presets), a press (its die's patterns),
    /// a Die Lathe (the dies it cuts), a Fan Former (what it makes) and an Airframe Former (what it cuts). The setting it sets is
    /// <see cref="ProductionLines.RecipeKey"/>.
    /// </summary>
    public static bool RecipePickable(ConstructPartDef d)
    {
        var slot = d.CfgSlot[(int)ProductionLines.RecipeKey(d)];
        if (slot < 0 || d.Config[slot].Options.Length == 0)
            return false;
        return d.IsRefiner && d.RefineChoices.Length > 0 || d.IsPanelFormer || d.IsFanFormer || d.IsAirframeFormer
            || d.IsLathe && PressTable.Current.Lathe.Length > 0 || d.IsPress && !d.IsLathe && PressTable.Current.Dies.Length > 0
            || d.IsAssembler && AssemblingTable.Current.ByMachine[d.AsmMachine].Length > 0;
    }

    /// <summary>What a machine's tiles depend on beyond its kind: a press's fitted die (its family), else "".</summary>
    public static string RecipeVariant(Construct c, int part) =>
        c.DefOf(part) is { IsPress: true, IsLathe: false } && ConstructPress.DieIn(c, part) is var die and >= 0 ? c.DefOf(die).DieFamily : "";

    /// <summary>The variant the open picker was made for (it closes when its machine's changes: a die fitted or taken out).</summary>
    public string RecipePickerVariant => _rcpVariant;

    /// <summary>Opens the picker for a Line Console's machine (<paramref name="same"/>: machines of its kind on the line, itself counted).</summary>
    public void OpenRecipePicker(ushort body, int console, int machine, ConstructPartDef d, int same, string variant = "")
    {
        Open(d, variant);
        _rcpBody = body;
        _rcpConsole = console;
        _rcpMachine = machine;
        _rcpSame = same;
        _rcpFromConfig = false;
        _rcpAllLabel = same > 1 ? $"Apply to all {same} {d.Name}s on the line" : "";
    }

    /// <summary>Opens the picker from a machine's own Recipe button (the pick goes as <see cref="CActKind.LineRecipe"/> with the machine as its console).</summary>
    public void OpenRecipePickerMachine(ushort body, int machine, ConstructPartDef d, string variant = "") => OpenRecipePicker(body, machine, machine, d, 1, variant);

    /// <summary>Opens the picker for the configure panel's recipe row (its selection gets the pick, as the row would).</summary>
    public void OpenRecipePickerConfig(ushort body, int part, ConstructPartDef d, int slot, string variant = "")
    {
        Open(d, variant);
        _rcpBody = body;
        _rcpConsole = -1;
        _rcpMachine = part;
        _rcpSlot = slot;
        _rcpSame = 1;
        _rcpFromConfig = true;
        _rcpAllLabel = "";
    }

    private void Open(ConstructPartDef d, string variant)
    {
        var key = SandboxTiles.Key();
        if (!_rcpSets.TryGetValue((d, variant), out var set) || set.Key != key)
            _rcpSets[(d, variant)] = set = MakeRecipeSet(d, key, variant);
        if (_rcpDef != d || _rcpVariant != variant)
        {
            _rcpVariant = variant;
            _rcpGroup = 0;
            _rcpSearch = "";
            if (_rcpSearchInput != null)
                ImGuiSafe.SetInput(_rcpSearchInput, "");
        }
        _rcp = set;
        _rcpDef = d;
        _rcpFilterVersion++;
        _rcpFocusSearch = set.Tiles.Length > 12;
        RecipePickerOpen = true;
    }

    public void CloseRecipePicker()
    {
        RecipePickerOpen = false;
        _rcp = null;
    }

    /// <summary>The picker belongs to the configure panel (it closes with it).</summary>
    public bool RecipePickerFromConfig => RecipePickerOpen && _rcpFromConfig;

    /// <summary>The machine the picker sets (body, part), and the console it was opened from (-1: the configure panel).</summary>
    public (ushort Body, int Machine, int Console) RecipePickerTarget => (_rcpBody, _rcpMachine, _rcpConsole);

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static ImColor8 Col8(System.Numerics.Vector3 c) => new((byte)(Math.Clamp(c.X, 0, 1) * 255), (byte)(Math.Clamp(c.Y, 0, 1) * 255), (byte)(Math.Clamp(c.Z, 0, 1) * 255), 255);

    private static string Secs(float s) => s >= 10 ? $"{s:0} s" : s >= 1 ? $"{s:0.#} s" : $"{s:0.##} s";

    /// <summary>A machine kind's tiles, made once (the texts, the icons' slots, the categories).</summary>
    private static RpSet MakeRecipeSet(ConstructPartDef d, int key, string variant)
    {
        if (d.IsPress || d.IsFanFormer || d.IsAirframeFormer)
            return MakeMakerSet(d, key, variant);
        var names = RecyclingTable.Current;
        var speed = MathF.Max(0.01f, d.RefineWork / (float)ConstructRefining.BaseWork);
        var tiles = new List<RpTile>();
        var groups = new List<string> { "All" };
        void Inputs(RpTile t, IReadOnlyList<(int Slot, string Text, ImColor8 Swatch)> ins)
        {
            t.InSlot = ins.Select(i => i.Slot).ToArray();
            t.InText = ins.Select(i => i.Text).ToArray();
            t.InSwatch = ins.Select(i => i.Swatch).ToArray();
        }
        (int, string, ImColor8) Mat(byte m, int units) => (SandboxTiles.ResourceSlot(m), $"{units}", Col8(names.ColourOf(m)));
        if (d.IsPanelFormer)
        {
            // A Panel Former's presets (HullLoft.PanelPresets, the Preset setting's options): a swatch each, its size and what it is.
            var opts = d.Config[d.CfgSlot[(int)CfgKey.Recipe]].Options;
            var shades = new ImColor8[] { new(96, 120, 150, 255), new(110, 140, 112, 255), new(150, 128, 92, 255), new(128, 104, 140, 255) };
            for (var k = 0; k < opts.Length; k++)
            {
                var pr = HullLoft.PanelPresets[Math.Clamp(k, 0, HullLoft.PanelPresets.Length - 1)];
                tiles.Add(new RpTile
                {
                    Value = k,
                    Name = opts[k],
                    Out = $"{pr.L / 100f:0.##} x {pr.W / 100f:0.##} m",
                    Search = $"{opts[k]} {pr.What}".ToLowerInvariant(),
                    Tip = $"{opts[k]}: {pr.What}. Queue orders it at {pr.L / 100f:0.##} x {pr.W / 100f:0.##} m in what the bin holds.",
                    Swatch = shades[k % shades.Length],
                });
            }
        }
        else if (d.IsRefiner)
        {
            tiles.Add(new RpTile { Value = 0, Name = "Auto", Search = "auto", Tip = "Auto: what is in the input picks the recipe.", Swatch = new ImColor8(70, 76, 88, 255) });
            var table = RefiningTable.Current;
            var choices = d.RefineChoices;
            foreach (var r in table.Recipes.Where(r => r.Machine == d.RefineMachine))
            {
                var k = Array.IndexOf(choices, r);
                var t = new RpTile
                {
                    Value = k >= 0 ? k + 1 : -1,
                    Name = Cap(r.OutputName),
                    Out = r.UnitsOut > 1 ? $"x{r.UnitsOut}" : "",
                    Time = Secs(r.Seconds / speed),
                    Slot = SandboxTiles.ResourceSlot(r.Output),
                    Swatch = Col8(names.ColourOf(r.Output)),
                };
                var ins = r.Mix.Length > 0 ? r.Mix.Select(m => Mat(m.Material, m.Units)).ToList() : [Mat(r.Input, r.UnitsIn)];
                Inputs(t, ins);
                var batch = r.Mix.Length > 0 ? r.Batch : $"{r.UnitsIn} {r.InputName}";
                t.Search = $"{r.OutputName} {batch}".ToLowerInvariant();
                t.Tip = k >= 0
                    ? $"{Cap(r.OutputName)}: {batch} make {r.UnitsOut} {r.OutputName} in {Secs(r.Seconds / speed)} on this {d.Name}."
                    : $"{Cap(r.OutputName)} ({batch}): needs a tier {r.MinTier} {d.Name}; this one is tier {d.Tier}.";
                tiles.Add(t);
            }
        }
        else
        {
            tiles.Add(new RpTile { Value = 0, Name = "None", Search = "none", Tip = "None: the Assembler builds nothing.", Swatch = new ImColor8(70, 76, 88, 255) });
            var list = AssemblingTable.Current.ByMachine[d.AsmMachine];
            var groupOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var k = 0; k < list.Length; k++)
            {
                var r = list[k];
                var g = SandboxCatalogue.Classify(r.Output).Group;
                if (!groupOf.TryGetValue(g, out var gi))
                {
                    groupOf[g] = gi = groups.Count;
                    groups.Add(g);
                }
                var ok = r.MinTier <= d.Tier;
                var t = new RpTile
                {
                    Value = ok ? k + 1 : -1,
                    Name = r.Name,
                    Out = r.Count > 1 ? $"x{r.Count}" : "",
                    Time = Secs(r.Seconds / speed),
                    Slot = SandboxTiles.SlotOf(SandboxTiles.Kind.Part, r.Output.Id),
                    Swatch = Col8(r.Output.Boxes.Length > 0 ? r.Output.Boxes[0].Color : new System.Numerics.Vector3(0.5f)),
                    Group = gi,
                };
                var ins = r.Materials.Select(m => Mat(m.Material, m.Units)).ToList();
                foreach (var (p, n) in r.Parts)
                    ins.Add((SandboxTiles.SlotOf(SandboxTiles.Kind.Part, p.Id), $"{n}", Col8(p.Boxes.Length > 0 ? p.Boxes[0].Color : new System.Numerics.Vector3(0.5f))));
                Inputs(t, ins);
                t.Search = $"{r.Name} {r.Batch} {g}".ToLowerInvariant();
                t.Tip = ok
                    ? $"{r.Name}{(r.Count > 1 ? $" x{r.Count}" : "")}: {r.Batch}, {Secs(r.Seconds / speed)} a batch on this {d.Name}."
                    : $"{r.Name}: needs a tier {r.MinTier} assembler; this one is tier {d.Tier}.";
                tiles.Add(t);
            }
        }
        return new RpSet { Tiles = tiles.ToArray(), Groups = groups.ToArray(), Title = $"Recipe: {d.Name}###rpick", Key = key };
    }

    private void RecipeFilter()
    {
        if (_rcp == null || _rcpFilterBuilt == _rcpFilterVersion)
            return;
        _rcpFilterBuilt = _rcpFilterVersion;
        if (_rcpVisible.Length < _rcp.Tiles.Length)
            _rcpVisible = new int[_rcp.Tiles.Length];
        _rcpVisibleCount = 0;
        var words = _rcpSearch.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < _rcp.Tiles.Length; i++)
        {
            var t = _rcp.Tiles[i];
            if (i > 0 && _rcpGroup > 0 && t.Group != _rcpGroup && words.Length == 0)
                continue;
            var match = true;
            foreach (var w in words)
                match &= t.Search.Contains(w, StringComparison.Ordinal);
            if (match)
                _rcpVisible[_rcpVisibleCount++] = i;
        }
    }

    /// <summary>
    /// The picker's window (between NewFrame and Render). <paramref name="current"/> is the machine's recipe value as last heard.
    /// True while it stays open. A pick is sent after the window is drawn.
    /// </summary>
    public bool DrawRecipePicker(Game game, int current, double now)
    {
        if (!RecipePickerOpen || _rcp is not { } set || _rcpDef is not { } d)
            return false;
        SandboxTiles.WantFew(); // (the pictures, made over a few frames if they aren't yet)
        var open = true;
        var picked = -1;
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(940 * _scale, vp.WorkSize.X * 0.9f);
        // As tall as its rows need (five tiles a row at this width), up to most of the screen: a Smelter's eight tiles make a short window.
        var rows = (set.Tiles.Length + 4) / 5;
        var head = (set.Groups.Length > 2 ? 110 : set.Tiles.Length > 12 ? 80 : 70) * _scale;
        var h = MathF.Min(MathF.Min(head + rows * 182 * _scale, 640 * _scale), vp.WorkSize.Y * 0.88f);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + (vp.WorkSize.X - w) * 0.5f, vp.WorkPos.Y + (vp.WorkSize.Y - h) * 0.5f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.95f);
        ImGui.SetNextWindowFocus();
        if (ImGui.Begin(set.Title, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            if (set.Tiles.Length > 12)
            {
                _rcpSearchInput ??= new ImInputString(33);
                ImGui.SetNextItemWidth(260 * _scale);
                if (_rcpFocusSearch)
                {
                    ImGui.SetKeyboardFocusHere();
                    _rcpFocusSearch = false;
                }
                if (ImGui.InputTextWithHint("##rpsearch"u8, "Search recipes..."u8, _rcpSearchInput))
                {
                    _rcpSearch = ImGuiSafe.Text(_rcpSearchInput);
                    _rcpFilterVersion++;
                }
                ImGui.SameLine();
            }
            if (_rcpAllLabel.Length > 0)
            {
                ImGui.Checkbox(_rcpAllLabel, ref _rcpAll);
                ImGui.SameLine();
            }
            ImGui.TextColored(Dim, "Click a recipe to set it. Esc closes."u8);
            if (set.Groups.Length > 2)
            {
                // The inventory's categories, as a row of tabs (a search covers them all).
                for (var g = 0; g < set.Groups.Length; g++)
                {
                    if (g > 0)
                        ImGui.SameLine();
                    var sel = g == _rcpGroup;
                    if (sel)
                        ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.18f, 0.32f, 0.5f, 1f));
                    if (ImGui.Button(set.Groups[g]))
                    {
                        _rcpGroup = g;
                        _rcpFilterVersion++;
                    }
                    if (sel)
                        ImGui.PopStyleColor();
                    if (ImGui.GetItemRectMax().X > ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - 140 * _scale && g + 1 < set.Groups.Length)
                        ImGui.NewLine(); // (wraps a long row of categories)
                }
            }
            ImGui.Separator();
            RecipeFilter();
            if (ImGui.BeginChild("##rpgrid"u8, new float2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.None))
                picked = RecipeGrid(set, current);
            ImGui.EndChild();
            if (ImGui.Button("Close"u8))
                open = false;
            if (_rcpVisibleCount == 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(Dim, "Nothing matches the search."u8);
            }
        }
        ImGui.End();
        if (_rcpTestPick >= 0)
        {
            if (Array.Exists(set.Tiles, t => t.Value == _rcpTestPick))
                picked = _rcpTestPick; // (a window shot's "picker N": as a click on that tile)
            _rcpTestPick = -1;
        }
        if (picked >= 0)
        {
            // After the drawing: the pick goes to the server (or the configure panel), and the picker closes.
            if (_rcpFromConfig)
                SandboxConfigSet(game, _rcpSlot, picked, now);
            else
                game.SandboxLineRecipe(_rcpBody, _rcpConsole, _rcpMachine, picked, _rcpAll && _rcpSame > 1);
            open = false;
        }
        if (!open)
            CloseRecipePicker();
        return open;
    }

    /// <summary>The tiles in rows; the one clicked this frame (its value), else -1.</summary>
    private int RecipeGrid(RpSet set, int current)
    {
        var picked = -1;
        var dl = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 8 * _scale;
        var tw = 150 * _scale;
        var cols = Math.Max(1, (int)((avail + gap) / (tw + gap)));
        tw = (avail - gap * (cols - 1)) / cols; // (the row filled)
        var icon = MathF.Min(tw * 0.62f, 96 * _scale);
        var th = icon + 74 * _scale;
        int pics = 0, tiles = 0;
        var rows = (_rcpVisibleCount + cols - 1) / cols;
        // Only the rows in view are drawn; the rest are space (the list scrolls as one).
        var rowH = th + gap;
        var top = ImGui.GetCursorPosY();
        var first = Math.Clamp((int)((ImGui.GetScrollY() - top) / rowH), 0, rows);
        var last = Math.Clamp((int)((ImGui.GetScrollY() + ImGui.GetWindowHeight() - top) / rowH) + 1, first, rows);
        ImGui.SetCursorPosY(top + first * rowH);
        {
            for (var row = first; row < last; row++)
            {
                var rowAt = ImGui.GetCursorScreenPos();
                for (var col = 0; col < cols; col++)
                {
                    var v = row * cols + col;
                    if (v >= _rcpVisibleCount)
                        break;
                    var t = set.Tiles[_rcpVisible[v]];
                    var at = new float2(rowAt.X + col * (tw + gap), rowAt.Y);
                    ImGui.SetCursorScreenPos(at);
                    ImGui.PushID(_rcpVisible[v]);
                    var clicked = ImGui.InvisibleButton("##rt"u8, new float2(tw, th));
                    ImGui.PopID();
                    var hot = ImGui.IsItemHovered();
                    if (clicked && t.Value >= 0)
                        picked = t.Value;
                    if (hot && t.Tip.Length > 0)
                        ImGuiSafe.SetTooltip(t.Tip);
                    if (RecipeTile(dl, t, at, tw, th, icon, t.Value >= 0 && t.Value == current, hot))
                        pics++;
                    tiles++;
                }
                ImGui.SetCursorScreenPos(rowAt);
                ImGui.Dummy(new float2(avail, rowH - ImGui.GetStyle().ItemSpacing.Y));
            }
        }
        ImGui.SetCursorPosY(top + rows * rowH);
        ImGui.Dummy(new float2(1, 1)); // (the list's full height, so the scroll bar knows it)
        RecipePickerTiles = tiles;
        RecipePickerPictures = pics;
        return picked;
    }

    private static readonly ImColor8 RpDimCard = new(20, 22, 26, 220), RpTime = new(150, 200, 235, 255), RpCurrent = new(120, 220, 140, 255);

    /// <summary>One tile: its card, the output's picture, its name, the inputs as small icons with amounts, and the time a batch. True when the picture was drawn.</summary>
    private bool RecipeTile(ImDrawListPtr dl, RpTile t, float2 at, float w, float h, float icon, bool current, bool hot, bool setMark = true)
    {
        var usable = t.Value >= 0;
        var p1 = new float2(at.X + w, at.Y + h);
        var r = 5 * _scale;
        dl.AddRectFilled(at, p1, !usable ? RpDimCard : current ? SbCardSel : hot ? SbCardHot : SbCard, r);
        var tint = usable ? new ImColor8(255, 255, 255, 255) : new ImColor8(150, 150, 160, 170);
        var i0 = new float2(at.X + (w - icon) * 0.5f, at.Y + 4 * _scale);
        var pic = Picture(dl, t.Slot, t.Swatch, i0, new float2(i0.X + icon, i0.Y + icon), tint, t.Slot < 0 ? t.Name : null);
        var clip = new float4(at.X + 4 * _scale, at.Y, p1.X - 4 * _scale, p1.Y);
        var y = at.Y + icon + 6 * _scale;
        ImGui.PushFont(default, 16);
        DlText(dl, new float2(at.X + 7 * _scale, y), usable ? SbText : SbDim, t.Name, clip);
        if (t.Out.Length > 0)
            DlText(dl, new float2(p1.X - 7 * _scale - ImGui.CalcTextSize(t.Out).X, at.Y + 6 * _scale), usable ? SbText : SbDim, t.Out);
        ImGui.PopFont();
        y += 22 * _scale;
        // The inputs: a small icon each, its amount beside it.
        ImGui.PushFont(default, 14);
        var x = at.X + 6 * _scale;
        var s = 28 * _scale;
        for (var k = 0; k < t.InSlot.Length; k++)
        {
            var tw = ImGui.CalcTextSize(t.InText[k]).X;
            if (x + s + tw + 2 * _scale > p1.X - 4 * _scale)
            {
                DlText(dl, new float2(x, y + 7 * _scale), SbDim, "+"u8);
                break;
            }
            Picture(dl, t.InSlot[k], t.InSwatch[k], new float2(x, y), new float2(x + s, y + s), tint, null);
            DlText(dl, new float2(x + s + 1 * _scale, y + 7 * _scale), usable ? SbText : SbDim, t.InText[k]);
            x += s + tw + 7 * _scale;
        }
        if (t.Time.Length > 0)
            DlText(dl, new float2(at.X + 7 * _scale, y + s + 3 * _scale), usable ? RpTime : SbDim, t.Time);
        if (current && setMark) // (the Crafting Terminal's selected tile is outlined only)
            DlText(dl, new float2(p1.X - 7 * _scale - ImGui.CalcTextSize("set"u8).X, y + s + 3 * _scale), RpCurrent, "set"u8);
        ImGui.PopFont();
        if (current)
            dl.AddRect(at, p1, SbOutline, r, ImDrawFlags.None, 2.5f * _scale);
        else if (hot && usable)
            dl.AddRect(at, p1, SbHotOutline, r, ImDrawFlags.None, 1.5f * _scale);
        return pic;
    }

    /// <summary>
    /// A tile picture from the atlas into a rectangle, or (until it is made, or with no slot) a plain swatch; <paramref name="letter"/>
    /// on a swatch with no picture (Auto, None). True when the picture was drawn.
    /// </summary>
    private bool Picture(ImDrawListPtr dl, int slot, ImColor8 swatch, float2 p0, float2 p1, ImColor8 tint, string? letter)
    {
        if (slot >= 0 && SandboxTiles.TryGet(slot, out var tex, out var uv0, out var uv1))
        {
            dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, p0, p1, new float2(uv0.X, uv0.Y), new float2(uv1.X, uv1.Y), tint);
            return true;
        }
        SandboxTiles.Prefer(slot); // (on screen: drawn before the rest of the atlas)
        var inset = (p1.X - p0.X) * 0.22f;
        dl.AddRectFilled(new float2(p0.X + inset, p0.Y + inset), new float2(p1.X - inset, p1.Y - inset), swatch, 4 * _scale);
        if (letter != null)
        {
            ImGui.PushFont(default, 22);
            var ts = ImGui.CalcTextSize(letter);
            DlText(dl, new float2((p0.X + p1.X - ts.X) * 0.5f, (p0.Y + p1.Y - ts.Y) * 0.5f), SbText, letter);
            ImGui.PopFont();
        }
        return false;
    }

    /// <summary>A loose resource's icon (a material id) in a rectangle, for lists (a swatch until the atlas has it).</summary>
    internal bool ResourceIcon(ImDrawListPtr dl, byte material, float2 p0, float2 p1)
    {
        SandboxTiles.WantFew();
        return Picture(dl, SandboxTiles.ResourceSlot(material), Col8(RecyclingTable.Current.ColourOf(material)), p0, p1, new ImColor8(255, 255, 255, 255), null);
    }
}
