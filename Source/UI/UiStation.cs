using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Building Station's panel (docs/sandbox/constructs/production.md "Building station"). Left: the blueprints (the shipped ones
/// by group, then the player's own saved ones), with a search box. Right: the picked one: whether it fits the bay, its bill by group
/// with what the hold and the linked stores have, where the rest is ordered and what nothing makes; then Start, Repeat, Cancel and
/// Release early (<see cref="CActKind.Station"/>: the server decides and says why not). It is opened and closed as the Crafting
/// Terminal's panel is (the station is one by data), so the Esc chain, the local reset and the mouse are that panel's.
/// </summary>
public sealed partial class Ui
{
    private readonly StationPreview _bstPreview = new();
    private readonly List<(string Name, string Group, string Search)> _bstList = new(512);
    private bool _bstListed;
    private int _bstSel = -1, _bstRepeat = 1;
    private bool _bstForever;
    private double _bstFilledAt;
    private string _bstSearch = "", _bstFit = "", _bstHead = "";
    private ImInputString? _bstSearchInput;
    private ushort _bstBody;
    private int _bstPart = -1;

    /// <summary>Smoke and probes: the blueprint picked, how far its preview is, its lines.</summary>
    public string StationPicked => _bstSel >= 0 && _bstSel < _bstList.Count ? _bstList[_bstSel].Name : "";
    public StationPreview StationPreview => _bstPreview;

    /// <summary>Picks a blueprint by name (the window tool's <c>station pick</c>). False when the list has none of that name.</summary>
    public bool StationPick(string name)
    {
        StationList();
        var k = _bstList.FindIndex(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (k < 0)
            return false;
        _bstSel = k;
        if (ConstructStation.Find(_bstList[k].Name) is { } bp)
            _bstPreview.Begin(bp);
        return true;
    }

    private void StationList()
    {
        if (_bstListed)
            return;
        _bstListed = true;
        _bstList.Clear();
        foreach (var bp in ConstructBuild.Blueprints)
            _bstList.Add((bp.Name, bp.Group.Length > 0 ? bp.Group : "Blueprints", bp.Name.ToLowerInvariant()));
        _bstList.Sort(static (a, b) => string.CompareOrdinal(a.Group, b.Group) is var g and not 0 ? g : string.CompareOrdinal(a.Name, b.Name));
        foreach (var e in PlayerBlueprints.List())
            _bstList.Add((e.Name, "My blueprints", e.Name.ToLowerInvariant()));
    }

    private static readonly float4 StGood = new(0.45f, 0.86f, 0.55f, 1f), StWarn = new(1f, 0.72f, 0.3f, 1f), StBad = new(1f, 0.42f, 0.36f, 1f), StHead = new(0.62f, 0.78f, 1f, 1f);

    /// <summary>The panel (between NewFrame and Render). True while it stays open.</summary>
    private bool DrawStation(Game game, Construct c, int part, double now)
    {
        if (_bstBody != c.Id || _bstPart != part)
        {
            (_bstBody, _bstPart, _bstListed) = (c.Id, part, false); // (opened on another station: the saved blueprints are read again)
            _bstFilledAt = 0;
        }
        StationList();
        var world = game.Constructs;
        var j = c.Parts[part].Station;
        var busy = j is { State: StationState.Costing or StationState.Building };
        RouteRender.BayPanel(c.Id, part, _bstSel >= 0 && _bstPreview.Ready && !_bstFit.StartsWith("FITS", StringComparison.Ordinal));
        if (_bstSel >= 0 && !_bstPreview.Ready)
            _bstPreview.Step(2.0);
        if (_bstSel >= 0 && _bstPreview.Ready && now - _bstFilledAt > 0.5)
        {
            _bstFilledAt = now;
            _bstPreview.Fill(world, c, part);
            var bay = ConstructStation.BaySize(c, part);
            _bstFit = _bstPreview.Why.Length > 0 ? $"It doesn't build: {_bstPreview.Why}" : ConstructStation.FitRefusal(_bstPreview.Size, bay) is { } no ? $"DOESN'T FIT: {no}"
                : $"FITS: {_bstPreview.Size.X:0.0} x {_bstPreview.Size.Z:0.0} m, {_bstPreview.Size.Y:0.0} m high, in a bay {bay.X:0.0} x {bay.Z:0.0} m, {bay.Y:0.0} m high";
            _bstHead = $"{_bstPreview.Name}: {_bstPreview.Parts} parts of {_bstPreview.Kinds} kinds{(_bstPreview.Unmade > 0 ? $", {_bstPreview.Unmade} that nothing linked makes" : "")}";
        }
        var open = true;
        var pick = -1;
        bool start = false, cancel = false, release = false, repeat = false;
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(1040 * _scale, vp.WorkSize.X * 0.92f);
        var h = MathF.Min(660 * _scale, vp.WorkSize.Y * 0.9f);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + (vp.WorkSize.X - w) * 0.5f, vp.WorkPos.Y + (vp.WorkSize.Y - h) * 0.5f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.95f);
        if (ImGui.Begin("Building Station###station"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            ImGuiSafe.TextColored(j == null || j.State == StationState.Idle ? Dim : j.State == StationState.Failed ? StBad : StGood,
                j == null || j.State == StationState.Idle ? "Idle. Pick a blueprint, check it fits the bay, then Start." : ConstructStation.StateText(j));
            if (j is { Note.Length: > 0 })
                ImGuiSafe.TextColored(j.State == StationState.Failed ? StBad : StWarn, j.Note);
            ImGui.Separator();
            var foot = ImGui.GetFrameHeightWithSpacing() * 2 + 6 * _scale;
            if (ImGui.BeginChild("##stlist"u8, new float2(300 * _scale, -foot), ImGuiChildFlags.Borders))
            {
                _bstSearchInput ??= new ImInputString(33);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##stsearch"u8, "Search blueprints..."u8, _bstSearchInput))
                    _bstSearch = ImGuiSafe.Text(_bstSearchInput).ToLowerInvariant();
                var group = "";
                for (var i = 0; i < _bstList.Count; i++)
                {
                    var e = _bstList[i];
                    if (_bstSearch.Length > 0 && !e.Search.Contains(_bstSearch, StringComparison.Ordinal))
                        continue;
                    if (e.Group != group)
                    {
                        group = e.Group;
                        ImGuiSafe.TextColored(StHead, group);
                    }
                    ImGui.PushID(i);
                    if (ImGui.Selectable(ImGuiSafe.Escape(e.Name), i == _bstSel))
                        pick = i;
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
            ImGui.SameLine();
            if (ImGui.BeginChild("##stbill"u8, new float2(0, -foot), ImGuiChildFlags.Borders))
            {
                if (_bstSel < 0)
                    ImGui.TextColored(Dim, "Pick a blueprint on the left: shipped vehicles and your own saved ones."u8);
                else if (!_bstPreview.Ready)
                    ImGui.TextColored(Dim, "Working out its size and bill..."u8);
                else
                {
                    ImGuiSafe.TextColored(StHead, _bstHead);
                    ImGuiSafe.TextColored(_bstFit.StartsWith("FITS", StringComparison.Ordinal) ? StGood : StBad, _bstFit);
                    ImGui.Separator();
                    foreach (var (text, tone) in _bstPreview.Lines)
                        if (tone == 0)
                            ImGuiSafe.TextColored(StHead, text);
                        else
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, tone == 1 ? StGood : tone == 2 ? StWarn : StBad);
                            ImGuiSafe.TextWrapped(text);
                            ImGui.PopStyleColor();
                        }
                }
            }
            ImGui.EndChild();
            var can = _bstSel >= 0 && _bstPreview.Ready && _bstPreview.Why.Length == 0 && !busy;
            ImGui.BeginDisabled(!can);
            ImGui.PushStyleColor(ImGuiCol.Button, CrTabOn);
            start = ImGui.Button("Start"u8, new float2(120 * _scale, 0));
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.Text("Build"u8);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100 * _scale);
            ImGui.BeginDisabled(_bstForever);
            ImGui.InputInt("##strepeat"u8, ref _bstRepeat, 1, 5);
            ImGui.EndDisabled();
            _bstRepeat = Math.Clamp(_bstRepeat, 1, 999);
            ImGui.SameLine();
            ImGui.Checkbox("forever"u8, ref _bstForever);
            ImGui.SameLine();
            ImGui.BeginDisabled(j == null || j.State == StationState.Idle);
            repeat = ImGui.Button("Set repeat"u8, new float2(110 * _scale, 0));
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(j == null || j.State == StationState.Idle);
            cancel = ImGui.Button("Cancel build"u8, new float2(120 * _scale, 0));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Takes the build apart: its parts go back to linked racks with room, the rest beside the bay. Orders are cancelled."u8);
            ImGui.SameLine();
            ImGui.BeginDisabled(j is not { State: StationState.Building });
            release = ImGui.Button("Release early"u8, new float2(130 * _scale, 0));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("What is fitted becomes a normal construct to finish by hand. Orders are cancelled."u8);
            if (ImGui.Button("Close"u8))
                open = false;
            ImGui.SameLine();
            ImGui.TextColored(Dim, "Parts arrive by conveyor or chute, from linked racks, or carry one here and press Use."u8);
        }
        ImGui.End();
        // After the drawing: the pick and the actions.
        if (pick >= 0 && pick != _bstSel)
        {
            _bstSel = pick;
            _bstFilledAt = 0;
            if (ConstructStation.Find(_bstList[pick].Name) is { } bp)
                _bstPreview.Begin(bp);
        }
        var n = _bstForever ? ushort.MaxValue : (ushort)(_bstRepeat - 1);
        if (start && _bstSel >= 0)
            game.SandboxStation(_bstBody, _bstPart, StationOp.Start, n, _bstList[_bstSel].Name);
        if (repeat)
            game.SandboxStation(_bstBody, _bstPart, StationOp.Repeat, n);
        if (cancel)
            game.SandboxStation(_bstBody, _bstPart, StationOp.Cancel);
        if (release)
            game.SandboxStation(_bstBody, _bstPart, StationOp.Release);
        if (!open)
            CloseCrafting();
        return open;
    }
}
