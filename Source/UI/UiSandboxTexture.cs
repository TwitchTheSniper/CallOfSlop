using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the texture paint tool's list (docs/sandbox/CONSTRUCTS.md, "The texture paint tool"; R with the tool out, or
/// "Textures..." on its ring): every material the tool offers (Materials/Surfaces.json, by its "category"), each a swatch drawn
/// by the real box shader (SurfaceThumbs) with its name, a search box and the categories as buttons. Clicking one puts it on the
/// roller and closes the list; Close and Esc leave the roller as it was. The names and ids are made when it opens; a frame of it
/// makes nothing.
/// </summary>
public sealed partial class Ui
{
    public bool SandboxTexturePickerOpen { get; private set; }

    private ImInputString? _txSearchInput;
    private string _txSearch = "";
    private int _txCategory; // 0: all, else _txCategories[k - 1]
    private bool _txFocus;
    private string[] _txIds = [], _txNames = [], _txCategories = [], _txCategoryIds = [];
    private int[] _txRowCat = [];

    /// <summary>Opens the list (the search box takes the keyboard at once).</summary>
    public void OpenSandboxTexturePicker()
    {
        SandboxTexturePickerOpen = true;
        _txFocus = true;
        _txSearchInput ??= new ImInputString(33);
        var rows = Surfaces.File.Rows;
        if (_txIds.Length != rows.Length)
        {
            _txIds = new string[rows.Length];
            _txNames = new string[rows.Length];
            _txRowCat = new int[rows.Length];
            var cats = new List<string>();
            for (var i = 0; i < rows.Length; i++)
            {
                _txIds[i] = $"##tx{i}";
                var n = rows[i].Name;
                _txNames[i] = n.Length > 0 ? char.ToUpperInvariant(n[0]) + n[1..] : n;
                var cat = rows[i].Category.Length > 0 ? rows[i].Category : "Other";
                var k = cats.IndexOf(cat);
                if (k < 0 && ConstructFinish.Offered(i))
                {
                    cats.Add(cat);
                    k = cats.Count - 1;
                }
                _txRowCat[i] = k + 1;
            }
            _txCategories = cats.ToArray();
            _txCategoryIds = cats.Select((c, k) => $"{c}##txc{k}").Prepend("All##txc").ToArray();
        }
    }

    public void CloseSandboxTexturePicker() => SandboxTexturePickerOpen = false;

    /// <summary>The list's search (the self-test and Program read and set it).</summary>
    public string SandboxTextureSearch
    {
        get => _txSearch;
        set
        {
            _txSearch = value.Trim();
            if (_txSearchInput != null)
                ImGuiSafe.SetInput(_txSearchInput, value);
        }
    }

    /// <summary>Whether row <paramref name="row"/> shows with the search and category as they are.</summary>
    public bool SandboxTextureShown(int row) =>
        ConstructFinish.Offered(row) && (_txCategory == 0 || row < _txRowCat.Length && _txRowCat[row] == _txCategory)
        && (_txSearch.Length == 0 || Surfaces.Row(row).Name.Contains(_txSearch, StringComparison.OrdinalIgnoreCase)
            || Surfaces.Row(row).Category.Contains(_txSearch, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The list's window. Returns true while it stays open; <paramref name="chosen"/> is the row clicked this frame (else 0).
    /// <paramref name="current"/> is the roller's material (outlined).
    /// </summary>
    public bool DrawSandboxTexturePicker(byte current, out byte chosen)
    {
        chosen = 0;
        if (!SandboxTexturePickerOpen)
            return false;
        var open = true;
        var vp = ImGui.GetMainViewport();
        var w = 560 * _scale;
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X * 0.5f - w * 0.5f, vp.WorkPos.Y + vp.WorkSize.Y * 0.5f - 230 * _scale), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, 460 * _scale), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.92f);
        if (ImGui.Begin("Textures###sbtexpick"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            if (_txFocus)
            {
                ImGui.SetKeyboardFocusHere();
                _txFocus = false;
            }
            ImGui.SetNextItemWidth(220 * _scale);
            try
            {
                if (ImGui.InputTextWithHint("##txsearch"u8, "Search textures..."u8, _txSearchInput!))
                    _txSearch = ImGuiSafe.Text(_txSearchInput!).Trim();
            }
            catch (Exception ex)
            {
                ImGuiSafe.FieldFailed("texture search", ex);
            }
            for (var k = 0; k < _txCategoryIds.Length; k++)
            {
                ImGui.SameLine(0, 4 * _scale);
                var on = _txCategory == k;
                if (on)
                    ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.24f, 0.4f, 0.6f, 1f));
                if (ImGui.SmallButton(_txCategoryIds[k]))
                    _txCategory = k;
                if (on)
                    ImGui.PopStyleColor();
            }
            ImGui.Separator();
            if (ImGui.BeginChild("##txgrid"u8, new float2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.None))
            {
                var dl = ImGui.GetWindowDrawList();
                var tile = 100 * _scale;
                var nameH = 20 * _scale;
                var gap = 8 * _scale;
                var cols = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + gap) / (tile + gap)));
                var col = 0;
                var shown = 0;
                var rows = Surfaces.File.Rows;
                for (var i = 0; i < rows.Length && i < _txIds.Length; i++)
                {
                    if (!SandboxTextureShown(i))
                        continue;
                    if (col > 0)
                        ImGui.SameLine(0, gap);
                    var at = ImGui.GetCursorScreenPos();
                    if (ImGui.InvisibleButton(_txIds[i], new float2(tile, tile + nameH)))
                    {
                        chosen = (byte)i;
                        open = false;
                    }
                    var hot = ImGui.IsItemHovered();
                    var p1 = new float2(at.X + tile, at.Y + tile + nameH);
                    dl.AddRectFilled(at, p1, i == current ? SbCardSel : hot ? SbCardHot : SbCard, 5 * _scale);
                    var pad = tile * 0.06f;
                    var tex = SurfaceThumbs.Get(i);
                    if (tex != 0)
                        dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, new float2(at.X + pad, at.Y + pad), new float2(at.X + tile - pad, at.Y + tile - pad),
                            new float2(0, 1), new float2(1, 0), new ImColor8(255, 255, 255, 255));
                    else
                        dl.AddRectFilled(new float2(at.X + tile * 0.3f, at.Y + tile * 0.3f), new float2(at.X + tile * 0.7f, at.Y + tile * 0.7f), SbDim, 3 * _scale);
                    var clip = new float4(at.X + 4 * _scale, at.Y, p1.X - 4 * _scale, p1.Y);
                    DlText(dl, new float2(at.X + 6 * _scale, at.Y + tile - 2 * _scale), SbText, _txNames[i], clip);
                    if (i == current)
                        dl.AddRect(at, p1, SbOutline, 5 * _scale, ImDrawFlags.None, 2f * _scale);
                    else if (hot)
                        dl.AddRect(at, p1, SbHotOutline, 5 * _scale, ImDrawFlags.None, 1.5f * _scale);
                    col = (col + 1) % cols;
                    shown++;
                }
                if (shown == 0)
                    ImGui.TextColored(Dim, "Nothing matches the search."u8);
            }
            ImGui.EndChild();
            ImGui.TextColored(Dim, "Click a texture to put it on the roller, then paint parts with it."u8);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - 90 * _scale);
            if (ImGui.Button("Close"u8, new float2(90 * _scale, 0)))
                open = false;
        }
        ImGui.End();
        if (!open)
            SandboxTexturePickerOpen = false;
        return SandboxTexturePickerOpen;
    }
}
