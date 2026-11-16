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
    private ClipUses? _bsUses; // its sub-blueprint uses: which stay uses, their lock, what a changed one becomes (ConstructClipUses.cs)
    private readonly Dictionary<int, ImInputString> _bsNewNames = new();

    private List<PlayerBlueprints.Entry> _sbSaved = new();
    private bool _sbSavedStale = true;
    private ImInputString? _sbSavedFilter;
    private string _sbSavedDelete = "";
    private readonly Dictionary<string, (long Stamp, Task<string> Why)> _sbBroken = new(); // blueprints with uses: does each still build (SubBlueprints.BrokenWhy, off the frame)
    private string _sbSavedSaid = "";

    // ---- The window tool's "bpui" verb (Program.WindowShotSubBp.cs): the buttons' own calls, for a run that drives them ----

    internal void ToolStamp(Game game, string name) => SbStamp(game, name);

    /// <summary>The save window's Save (and Replace) with this name; with the given lock on every kept use, or a changed use's way.</summary>
    internal string ToolSave(string name, bool overwrite, bool? lockAll, UseChoice? changed, string newName)
    {
        if (!BlueprintSaveOpen || _bsClip == null)
            return "the save window isn't open";
        _bsName ??= new ImInputString(PlayerBlueprints.MaxName + 1);
        ImGuiSafe.SetInput(_bsName, name);
        foreach (var row in _bsUses?.Rows ?? [])
        {
            if (row.Kept && lockAll is { } l)
                row.Lock = l;
            if (row.Changed && changed is { } ch)
            {
                row.Choice = ch;
                if (ch == UseChoice.NewBlueprint)
                {
                    if (!_bsNewNames.TryGetValue(row.Tag, out var nn))
                        _bsNewNames[row.Tag] = nn = new ImInputString(PlayerBlueprints.MaxName + 1);
                    ImGuiSafe.SetInput(nn, newName);
                }
            }
        }
        BlueprintSaveStart(name, overwrite);
        return _bsWhy.Length > 0 ? _bsWhy : _bsAsk ? "asks to replace" : "checking";
    }

    /// <summary>The save window's Uses list as its rows say it.</summary>
    internal IEnumerable<string> ToolSaveRows()
    {
        foreach (var row in _bsUses?.Rows ?? [])
            yield return $"{row.Name} v{row.Version} (tag {row.Tag}, {row.Parts} parts): {(row.Kept ? row.Lock ? "kept, Lock" : "kept, Live" : row.Why + (row.Changed ? $" [{row.Choice}]" : ""))}";
    }

    internal string ToolSaveWhy => _bsWhy;

    internal void ToolSetLock(string name, int use, bool locked)
    {
        _sbSaved = PlayerBlueprints.List();
        if (_sbSaved.Find(e => e.Name == name) is { } e)
            SbSetLock(e, use, locked);
        else
            _sbSavedSaid = $"no blueprint {name}";
    }

    internal void ToolPack(string name)
    {
        _sbSaved = PlayerBlueprints.List();
        if (_sbSaved.Find(e => e.Name == name) is { } e)
            SbPack(e);
        else
            _sbSavedSaid = $"no blueprint {name}";
    }

    internal string ToolSaid => _sbSavedSaid;

    /// <summary>The labels of the selector's frames round sub-blueprint uses (UseFrameRender.cs), this frame's.</summary>
    public void DrawUseLabels()
    {
        foreach (var (at, text, changed) in UseFrameRender.Labels)
            Hud.WorldLabel(at, text, changed ? new System.Numerics.Vector3(1f, 0.72f, 0.15f) : new System.Numerics.Vector3(0.35f, 0.92f, 1f), 0.9f, 13f * _scale, UseFrameRender.MaxDistance);
    }

    /// <summary>The save window is up (the mouse is free).</summary>
    public bool BlueprintSaveOpen { get; private set; }

    public void OpenBlueprintSave(ConstructClip clip)
    {
        _bsClip = clip;
        _bsWhy = "";
        _bsAsk = false;
        _bsCheck = null;
        _bsName ??= new ImInputString(PlayerBlueprints.MaxName + 1);
        _bsUses = ClipUses.Plan(clip);
        _bsNewNames.Clear();
        BlueprintSaveOpen = true;
    }

    public void CloseBlueprintSave()
    {
        BlueprintSaveOpen = false;
        _bsClip = null;
        _bsCheck = null;
        _bsUses = null;
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
        // A changed use the player chose to keep as a blueprint: its parts are saved first (a new version, or a new blueprint), then it
        // is a use like the others (ConstructClipUses.SaveUse).
        if (_bsUses != null)
        {
            foreach (var row in _bsUses.Rows)
                if (row.Changed && row.Choice != UseChoice.Plain)
                {
                    if (row.Choice == UseChoice.NewBlueprint)
                        row.NewName = _bsNewNames.TryGetValue(row.Tag, out var nn) ? ImGuiSafe.Text(nn).Trim() : "";
                    if (ClipUses.SaveUse(_bsClip!, row) is { } useWhy)
                    {
                        _bsWhy = $"{row.Name}: {useWhy}";
                        return;
                    }
                }
            _bsUses = ClipUses.Plan(_bsClip!, _bsUses.Rows);
        }
        var text = _bsClip!.ToBlueprint(name, "", out var why, _bsUses);
        if (text == null)
        {
            _bsWhy = why;
            return;
        }
        // Saved over a blueprint with an id: the old text goes to its history, this one is its next version (every live use gets it).
        if (overwrite)
            text = SubBlueprints.NextVersion(name, text);
        if (_bsUses is { Count: > 0 } && SubBlueprints.IdOf(text).Id.Length == 0)
            text = SubBlueprints.WithId(text, SubBlueprints.NewId(), 1); // (a blueprint saved with uses has an id from the start)
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
                var (sid, sver) = SubBlueprints.IdOf(check.Text);
                if (sid.Length > 0 && SubBlueprints.UsedLiveBy(sid) is { Count: > 0 } users)
                    said += $" (v{sver}); used live by {string.Join(", ", users.Take(4))}{(users.Count > 4 ? ", ..." : "")}: the Blueprints tab shows any it broke";
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
            if (_bsUses is { Rows.Count: > 0 } plan)
                DrawSaveUses(plan);
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

    /// <summary>The save window's Uses list: grouped by blueprint, Live or Lock on each kept use, the three ways for a changed one.</summary>
    private void DrawSaveUses(ClipUses plan)
    {
        ImGui.TextColored(Dim, "Uses (other blueprints built into it):"u8);
        foreach (var group in plan.Rows.GroupBy(r => r.Id))
        {
            var first = group.First();
            var kept = group.Count(r => r.Kept);
            ImGuiSafe.TextWrapped($"{first.Name} x{group.Count()} - v{first.Version}{(kept < group.Count() ? $" ({kept} kept as uses)" : "")}");
            var n = 0;
            foreach (var row in group)
            {
                n++;
                ImGui.PushID(row.Tag);
                ImGui.Indent();
                if (row.Kept)
                {
                    ImGuiSafe.TextColored(Dim, $"#{n}: {row.Parts} parts");
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Live"u8, !row.Lock))
                        row.Lock = false;
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Lock"u8, row.Lock))
                        row.Lock = true;
                    if (ImGui.IsItemHovered())
                        ImGuiSafe.SetTooltip($"Lock: always builds v{row.Version}. Live: builds the newest version of {row.Name}.");
                }
                else
                {
                    ImGuiSafe.TextColoredWrapped(new float4(1f, 0.75f, 0.35f, 1f), $"#{n}: {row.Why}");
                    if (row.Changed)
                    {
                        if (ImGui.RadioButton("As plain parts"u8, row.Choice == UseChoice.Plain))
                            row.Choice = UseChoice.Plain;
                        ImGui.SameLine();
                        if (ImGui.RadioButton("New version"u8, row.Choice == UseChoice.NewVersion))
                            row.Choice = UseChoice.NewVersion;
                        ImGui.SameLine();
                        if (ImGui.RadioButton("New blueprint"u8, row.Choice == UseChoice.NewBlueprint))
                            row.Choice = UseChoice.NewBlueprint;
                        if (row.Choice == UseChoice.NewBlueprint)
                        {
                            if (!_bsNewNames.TryGetValue(row.Tag, out var nn))
                                _bsNewNames[row.Tag] = nn = new ImInputString(PlayerBlueprints.MaxName + 1);
                            ImGui.SetNextItemWidth(-1);
                            ImGui.InputTextWithHint("##usename"u8, "Its name"u8, nn);
                        }
                    }
                }
                ImGui.Unindent();
                ImGui.PopID();
            }
        }
        ImGui.Separator();
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
                if (e.Uses.Count > 0 && ImGui.Button("Pack"u8))
                    SbPack(e);
                if (e.Uses.Count > 0)
                    ImGui.SameLine();
                ImGuiSafe.TextWrapped($"{e.Name}  ({e.Parts} part{(e.Parts == 1 ? "" : "s")}{(e.Version > 0 ? $", v{e.Version}" : "")})");
                if (e.Uses.Count > 0)
                    SbDrawUses(e);
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        if (_sbSavedSaid.Length > 0)
            ImGuiSafe.TextColoredWrapped(Dim, _sbSavedSaid);
    }

    /// <summary>A saved blueprint's uses: the line, red when it no longer builds (with a lock to the version before), Live / Lock per blueprint used.</summary>
    private void SbDrawUses(PlayerBlueprints.Entry e)
    {
        if (!_sbBroken.TryGetValue(e.Name, out var check) || check.Stamp != e.Stamp)
        {
            var file = e.File;
            var name = e.Name;
            _sbBroken[e.Name] = check = (e.Stamp, Task.Run(() =>
            {
                try
                {
                    return SubBlueprints.BrokenWhy(File.ReadAllText(file), name + ".json");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return ex.Message;
                }
            }));
        }
        ImGui.Indent();
        var broken = check.Why.IsCompleted ? check.Why.Result : "";
        if (broken.Length > 0)
            ImGuiSafe.TextColoredWrapped(new float4(1f, 0.42f, 0.36f, 1f), $"BROKEN: {broken}");
        else
            ImGuiSafe.TextColoredWrapped(Dim, check.Why.IsCompleted ? e.UsesLine : e.UsesLine + " (checking...)");
        foreach (var g in e.Uses.GroupBy(u => u.Id))
        {
            var u = g.First();
            ImGui.PushID(u.Id);
            ImGuiSafe.TextColored(Dim, $"{u.Name} x{g.Count()}:");
            ImGui.SameLine();
            if (ImGui.RadioButton("Live"u8, !u.Lock) && u.Lock)
                SbSetLock(e, u.Index, false);
            ImGui.SameLine();
            if (ImGui.RadioButton("Lock"u8, u.Lock) && !u.Lock)
                SbSetLock(e, u.Index, true);
            // A broken blueprint whose live use of this one built before: lock it to the version it was saved with.
            if (broken.Length > 0 && !u.Lock && broken.Contains(u.Name, StringComparison.Ordinal))
            {
                ImGui.SameLine();
                if (ImGui.Button("Lock use to the version it was saved with"u8))
                    SbSetLock(e, u.Index, true);
            }
            ImGui.PopID();
        }
        ImGui.Unindent();
    }

    private void SbSetLock(PlayerBlueprints.Entry e, int use, bool locked)
    {
        _sbSavedSaid = SubBlueprints.SetLock(e.Name, use, locked) is { } bad ? $"{e.Name}: {bad}" : $"{e.Name}: {(locked ? "locked" : "live")} (saved as a new version)";
        _sbSavedStale = true;
    }

    /// <summary>One file with the blueprint and every version its tree names: NAME.pack.json beside it, for another player's folder.</summary>
    private void SbPack(PlayerBlueprints.Entry e)
    {
        try
        {
            var text = File.ReadAllText(e.File);
            if (SubBlueprints.PackOf(text, e.Name + ".json", out var why) is not { } packed)
            {
                _sbSavedSaid = $"{e.Name} can't be packed: {why}";
                return;
            }
            var path = Path.Combine(PlayerBlueprints.Folder, SubBlueprints.PacksFolder, e.Name + ".pack.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _sbSavedSaid = PlayerBlueprints.WriteFile(path, packed) is { } bad ? bad
                : $"packed as {path}: a friend drops it in their own {PlayerBlueprints.FolderName} folder";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _sbSavedSaid = ex.Message;
        }
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
