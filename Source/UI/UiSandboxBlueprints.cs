using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Blueprint Desk's windows (docs/sandbox/CONSTRUCTS.md, "Build stations"): naming a selection to save it, with a bad name
/// refused and a name in use asked about; and the player's own blueprints in the Tab inventory's Blueprints tab, with a filter,
/// stamp and delete. A save is built back from its own text first (a few steps a frame), so what is on disk is a real blueprint.
/// </summary>
public sealed partial class Ui
{
    private ConstructClip? _bsClip;
    private ImInputString? _bsName;
    private string _bsWhy = "";
    private bool _bsAsk;
    private BlueprintLoad? _bsCheck;
    private string _bsPending = "";

    private List<PlayerBlueprints.Entry> _sbSaved = new();
    private bool _sbSavedStale = true;
    private ImInputString? _sbSavedFilter;
    private string _sbSavedDelete = "";

    /// <summary>The save window is up (the mouse is free).</summary>
    public bool BlueprintSaveOpen { get; private set; }

    public void OpenBlueprintSave(ConstructClip clip)
    {
        _bsClip = clip;
        _bsWhy = "";
        _bsAsk = false;
        _bsCheck = null;
        _bsName ??= new ImInputString(PlayerBlueprints.MaxName + 1);
        BlueprintSaveOpen = true;
    }

    public void CloseBlueprintSave()
    {
        BlueprintSaveOpen = false;
        _bsClip = null;
        _bsCheck = null;
    }

    /// <summary>Starts a save (the window's Save button; the bad-name check first). Overwrites only with <paramref name="overwrite"/>.</summary>
    private void BlueprintSaveStart(string name, bool overwrite)
    {
        if (PlayerBlueprints.NameRefusal(name) is { } bad)
        {
            _bsWhy = bad;
            return;
        }
        if (!overwrite && PlayerBlueprints.Exists(name))
        {
            _bsAsk = true;
            _bsWhy = "";
            return;
        }
        var text = _bsClip!.ToBlueprint(name, "", out var why);
        if (text == null)
        {
            _bsWhy = why;
            return;
        }
        _bsAsk = false;
        _bsPending = name;
        _bsCheck = new BlueprintLoad(text, name + ".json");
    }

    /// <summary>The window. Returns true while it stays open; <paramref name="said"/> is a line for the HUD when it ends in a save.</summary>
    public bool DrawBlueprintSave(out string said)
    {
        said = "";
        if (!BlueprintSaveOpen || _bsClip == null)
            return false;
        if (_bsCheck is { } check && check.Step())
        {
            _bsCheck = null;
            if (check.Clip == null)
                _bsWhy = $"it won't build back: {check.Why}";
            else if (check.Clip.Parts != _bsClip.Parts)
                _bsWhy = $"only {check.Clip.Parts} of {_bsClip.Parts} parts build back";
            else if (PlayerBlueprints.Write(_bsPending, check.Text) is { } bad)
                _bsWhy = bad;
            else
            {
                said = $"saved as {_bsPending}";
                _sbSavedStale = true;
                CloseBlueprintSave();
                return false;
            }
        }
        var open = true;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X * 0.5f - 170 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.4f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(340 * _scale, 0), ImGuiCond.Always);
        if (ImGui.Begin("Save as blueprint###sbbpsave"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
        {
            ImGui.PushTextWrapPos(0); // (the window's width is fixed, its height follows: long reasons wrap)
            ImGuiSafe.TextColored(Dim, $"{_bsClip.Parts} part{(_bsClip.Parts == 1 ? "" : "s")}, with their links, joints, settings and paint");
            if (_bsClip.LinksLeftOut > 0)
                ImGuiSafe.TextColored(Dim, $"{_bsClip.LinksLeftOut} link{(_bsClip.LinksLeftOut == 1 ? "" : "s")} left out: {(_bsClip.LinksLeftOut == 1 ? "an end is" : "ends are")} outside the selection");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##bpname"u8, "Name"u8, _bsName!);
            var name = ImGuiSafe.Text(_bsName!).Trim();
            if (_bsCheck != null)
                ImGui.TextColored(Dim, "Checking it builds..."u8);
            else if (_bsAsk)
            {
                ImGuiSafe.TextColored(Dim, $"{name} exists. Replace it?");
                if (ImGui.Button("Replace"u8))
                    BlueprintSaveStart(name, overwrite: true);
                ImGui.SameLine();
                if (ImGui.Button("Keep it"u8))
                    _bsAsk = false;
            }
            else
            {
                if (ImGui.Button("Save"u8))
                    BlueprintSaveStart(name, overwrite: false);
                ImGui.SameLine();
                if (ImGui.Button("Cancel"u8))
                    open = false;
            }
            if (_bsWhy.Length > 0)
                ImGuiSafe.TextColored(new float4(1f, 0.45f, 0.4f, 1f), _bsWhy);
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
        if (!open)
            CloseBlueprintSave();
        return BlueprintSaveOpen;
    }

    /// <summary>The player's own blueprints: the top of the Blueprints tab, or all of it on a survival inventory.</summary>
    private void SbDrawSaved(Game game, float height)
    {
        if (_sbSavedStale)
        {
            _sbSavedStale = false;
            _sbSaved = PlayerBlueprints.List();
        }
        _sbSavedFilter ??= new ImInputString(33);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Dim, "Your blueprints"u8);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200 * _scale);
        ImGui.InputTextWithHint("##sbsavedfilter"u8, "Filter..."u8, _sbSavedFilter);
        var filter = ImGuiSafe.Text(_sbSavedFilter).Trim().ToLowerInvariant();
        ImGui.SameLine();
        if (ImGui.Button("Refresh##sbsaved"u8))
            _sbSavedStale = true;
        if (ImGui.BeginChild("##sbsaved"u8, new float2(0, height), ImGuiChildFlags.Borders))
        {
            if (_sbSaved.Count == 0)
                ImGuiSafe.TextColored(Dim, $"None yet. With the selector and a Blueprint Desk: select parts, {SandboxKeys.SelSave}: save.");
            foreach (var e in _sbSaved)
            {
                if (filter.Length > 0 && !e.Search.Contains(filter) && !e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                ImGui.PushID(e.Name);
                if (ImGui.Button("Stamp"u8))
                    SbStamp(game, e.Name);
                ImGui.SameLine();
                if (_sbSavedDelete == e.Name)
                {
                    if (ImGui.Button("Really delete"u8))
                    {
                        PlayerBlueprints.Delete(e.Name);
                        _sbSavedDelete = "";
                        _sbSavedStale = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Keep"u8))
                        _sbSavedDelete = "";
                }
                else if (ImGui.Button("Delete"u8))
                    _sbSavedDelete = e.Name;
                ImGui.SameLine();
                ImGuiSafe.TextWrapped($"{e.Name}  ({e.Parts} part{(e.Parts == 1 ? "" : "s")})");
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    /// <summary>A blueprint goes on the selector: its ghost comes up, and the window goes away unless it is to stay open.</summary>
    private void SbStamp(Game game, string name)
    {
        if (game.SandboxAidOff(BuildAid.Blueprint) is { } off)
        {
            game.SandboxSay(off);
            return;
        }
        if (Program.Selector.BeginStamp(game, name) is { } why)
        {
            game.SandboxSay(why);
            return;
        }
        game.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Selector);
        CloseSandboxParts();
    }
}
