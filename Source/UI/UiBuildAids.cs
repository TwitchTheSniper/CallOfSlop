using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The Replacer's part picker (Program.SandboxAids.cs): what fits the aimed part, by name, with what the player has. One click swaps.</summary>
public sealed partial class Ui
{
    private ushort _rpBody, _rpPart, _rpDef;
    private System.Numerics.Vector3 _rpAt;
    private readonly List<ConstructPartDef> _rpFits = [];
    private readonly Dictionary<ushort, int> _rpLoose = [];
    private ImInputString? _rpInput;

    public bool ReplacePickerOpen { get; private set; }

    /// <summary>Opens on a part: every part that fits it (Plan on each candidate of its size class, once, here).</summary>
    public void OpenReplacePicker(Game game, Construct c, int part)
    {
        ReplacePickerOpen = true;
        _rpBody = c.Id;
        _rpPart = (ushort)part;
        _rpDef = c.Parts[part].Def;
        _rpAt = c.Parts[part].Pos;
        _rpInput ??= new ImInputString(33);
        ImGuiSafe.SetInput(_rpInput, "");
        _rpFits.Clear();
        var cls = c.DefOf(part).Class;
        foreach (var d in ConstructParts.All)
            if (d.Offered && d.Class == cls && ConstructReplace.Plan(game.Constructs, c, part, d, out _) == null)
                _rpFits.Add(d);
        _rpLoose.Clear();
        if (game.Local is { } me)
            foreach (var d in _rpFits)
                if (d.PhysicsOnly)
                    _rpLoose[d.Id] = ConstructAid.LooseCount(game.Constructs, me.Id, d, me.Eye);
        _rpFits.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    public void CloseReplacePicker()
    {
        ReplacePickerOpen = false;
        _rpFits.Clear();
    }

    /// <summary>The window, in the ImGui pass. False when it is shut (the caller gives the mouse back).</summary>
    public bool DrawReplacePicker(Game game)
    {
        if (!ReplacePickerOpen)
            return false;
        if (game.Constructs.Get(_rpBody) is not { } c || _rpPart >= c.PartCount || c.Parts[_rpPart].Def != _rpDef || c.Parts[_rpPart].Pos != _rpAt)
        {
            CloseReplacePicker(); // (the part changed under the window: its index is stamped)
            return false;
        }
        var open = true;
        var size = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowSize(new float2(420 * Scale, 460 * Scale), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(new float2(size.X * 0.5f, size.Y * 0.5f), ImGuiCond.Appearing, new float2(0.5f, 0.5f));
        if (ImGui.Begin("Replace part"u8, ref open, ImGuiWindowFlags.NoCollapse))
        {
            ImGuiSafe.TextWrapped($"Replace the {c.DefOf(_rpPart).Name} with:");
            ImGui.InputTextWithHint("##rpfind"u8, "Search..."u8, _rpInput!);
            var find = ImGuiSafe.Text(_rpInput!);
            if (_rpFits.Count == 0)
                ImGuiSafe.TextWrapped("Nothing else fits its connections here.");
            ImGui.BeginChild("fits"u8);
            var inv = game.SandboxInv.Of(game.LocalId);
            var beacon = game.SandboxBeacon(game.LocalId);
            foreach (var d in _rpFits)
            {
                if (find.Length > 0 && d.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var have = game.SandboxInfiniteParts ? -1 : d.PhysicsOnly ? _rpLoose.GetValueOrDefault(d.Id) : inv.Count(d.Id) + (beacon.On ? beacon.CountOf(d.Id) : 0);
                ImGui.BeginDisabled(have == 0);
                if (ImGui.Selectable(have < 0 ? d.Name : $"{d.Name}  ({have})"))
                {
                    Program.SandboxReplaceSend(game, _rpBody, _rpPart, d.Id);
                    open = false;
                }
                ImGui.EndDisabled();
            }
            ImGui.EndChild();
        }
        ImGui.End();
        if (!open)
            CloseReplacePicker();
        return open;
    }
}
