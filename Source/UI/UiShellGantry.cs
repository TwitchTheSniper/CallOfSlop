using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Shell Gantry's panel (ConstructGantry.cs; mining-shell.md "Shell Gantry layout"): the shell in the bay, its parts table, and a row a
/// slot with the module fitted there and a button for each module that could go in its place (how many its table holds). A click asks the
/// server (<see cref="CActKind.Gantry"/>), which decides and says why not; the arm's work is seen in the world. It holds the mouse while
/// open and closes on Esc, on a local reset, and when the gantry goes or the player walks off (Program.SandboxShellGantry.cs).
/// </summary>
public sealed partial class Ui
{
    public bool ShellGantryOpen { get; private set; }
    private ushort _gnBody;
    private int _gnPart = -1;
    private double _gnFilledAt;
    private string _gnShell = "", _gnTable = "", _gnBusy = "";
    private bool _gnHasShell, _gnCreative;
    private readonly string[] _gnFitted = new string[ShellFrames.SlotCount];
    private readonly bool[] _gnFixed = new bool[ShellFrames.SlotCount];
    private readonly int[] _gnCurrent = new int[ShellFrames.SlotCount];
    private int[] _gnOnTable = [];
    private string[] _gnRow = new string[ShellFrames.SlotCount]; // each slot's line, made when the state changes (not each frame)
    private string[][] _gnBtn = []; // [slot][module]: its button's label; null: no button
    private long _gnKey = -1;

    /// <summary>The window tool: the next frame presses the button of module <c>Module</c> (by name) for slot <c>Slot</c>.</summary>
    public (int Slot, string Module)? ShellGantryAuto;
    public string ShellGantrySaid { get; private set; } = "";
    public (ushort Body, int Part) ShellGantryTarget => (_gnBody, _gnPart);

    public void OpenShellGantry(ushort body, int part)
    {
        (_gnBody, _gnPart, _gnFilledAt, _gnKey) = (body, part, 0, -1);
        ShellGantryOpen = true;
    }

    public void CloseShellGantry() => ShellGantryOpen = false;

    private void ShellGantryFill(ConstructWorld world, Construct c, int part)
    {
        var all = ShellFrames.Modules;
        if (_gnOnTable.Length != all.Length)
            _gnOnTable = new int[all.Length];
        var shell = ConstructGantry.ShellIn(world, c, part);
        var table = ConstructGantry.TableOf(world, c, part, out var tb);
        _gnCreative = world.Creative;
        _gnHasShell = shell != null;
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        var frame = (byte)0;
        if (shell != null)
        {
            frame = ShellFrames.FrameOf(shell.Parts[0].D);
            ShellFrames.ReadKit(shell.Parts[0], shell.Parts[0].D, kit);
            _gnShell = $"In the bay: {shell.Parts[0].D.Name} ({ShellFrames.Of(frame).Name} frame)";
        }
        else
            _gnShell = "No shell in the bay: walk a shell in and leave it (Esc menu > Leave the shell), or set one down there.";
        _gnTable = table >= 0 && tb != null ? $"Parts table: {world.StoreOf(tb, table)?.ItemCount ?? 0} parts on it"
            : _gnCreative ? "No Shell Parts Table in reach (creative: modules come free)" : "No Shell Parts Table behind the rear frame in the arms' reach: place one there.";
        for (var s = 0; s < ShellFrames.SlotCount; s++)
        {
            var m = shell != null ? ShellFrames.ModuleAt(frame, kit, s) : null;
            _gnFitted[s] = m?.Name ?? "-";
            _gnCurrent[s] = m?.Index ?? -1;
            _gnFixed[s] = shell != null && ShellFrames.Of(frame).Fixed[s];
        }
        for (var i = 0; i < all.Length; i++)
            _gnOnTable[i] = ConstructGantry.OnTable(world, tb, table, all[i]);
        var k = ConstructGantry.JobAt(world, c.Id, part);
        var key = (long)(shell?.Id ?? 0) * 31 + (tb?.Id ?? 0) + (k >= 0 ? (world.GantryJobs[k].New + 1) << 20 : 0) + (_gnCreative ? 1L << 40 : 0) + frame * 7919L;
        for (var s = 0; s < ShellFrames.SlotCount; s++)
            key = key * 131 + _gnCurrent[s] + 2;
        for (var i = 0; i < all.Length; i++)
            key = key * 37 + _gnOnTable[i];
        if (key == _gnKey && _gnBtn.Length == ShellFrames.SlotCount)
            return; // (nothing changed: the rows' text stays)
        _gnKey = key;
        _gnBusy = k >= 0 && world.GantryJobs[k].New - 1 is var n and >= 0 && n < all.Length
            ? $"Working: the {ConstructGantry.ArmNames[world.GantryJobs[k].Arm]} arm fits a {all[n].Name} to the {ShellFrames.SlotLabel(world.GantryJobs[k].Slot)}" : "";
        if (_gnBtn.Length != ShellFrames.SlotCount)
            _gnBtn = new string[ShellFrames.SlotCount][];
        for (var s = 0; s < ShellFrames.SlotCount; s++)
        {
            var label = ShellFrames.SlotLabel(s);
            _gnRow[s] = $"{char.ToUpperInvariant(label[0])}{label[1..]}: {_gnFitted[s]}{(_gnFixed[s] ? "  (fixed)" : "")}";
            if (_gnBtn[s] == null || _gnBtn[s].Length != all.Length)
                _gnBtn[s] = new string[all.Length];
            for (var i = 0; i < all.Length; i++)
            {
                var m = all[i];
                _gnBtn[s][i] = shell == null || _gnFixed[s] || m.Part.Length == 0 || m.Slot != ShellFrames.SlotKind(s) || m.Index == _gnCurrent[s] || m.Kind is ShellModuleKind.Hopper or ShellModuleKind.Drill
                    ? null! : $"Fit {m.Name} ({_gnOnTable[i]} on table)"; // (a button label is no printf text: kept as made)
            }
        }
    }

    /// <summary>The panel (between NewFrame and Render). True while it stays open.</summary>
    public bool DrawShellGantry(Game game, Construct c, int part, double now)
    {
        if (!ShellGantryOpen)
            return false;
        var world = game.Constructs;
        if (now - _gnFilledAt > 0.5 || _gnFilledAt == 0)
        {
            _gnFilledAt = now;
            ShellGantryFill(world, c, part);
        }
        var all = ShellFrames.Modules;
        var open = true;
        int pickSlot = -1, pickModule = -1;
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(620 * _scale, vp.WorkSize.X * 0.92f);
        var h = MathF.Min(520 * _scale, vp.WorkSize.Y * 0.9f);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + (vp.WorkSize.X - w) * 0.5f, vp.WorkPos.Y + (vp.WorkSize.Y - h) * 0.5f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.95f);
        if (ImGui.Begin("Shell Gantry###gantry", ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            ImGuiSafe.TextWrapped(_gnShell);
            ImGuiSafe.TextColored(Dim, _gnTable);
            if (_gnBusy.Length > 0)
                ImGuiSafe.TextColored(StWarn, _gnBusy);
            ImGui.Separator();
            for (var s = 0; s < ShellFrames.SlotCount; s++)
            {
                ImGui.PushID(s);
                ImGuiSafe.TextColored(StHead, _gnRow[s]);
                if (_gnHasShell && !_gnFixed[s] && s < _gnBtn.Length)
                {
                    var any = false;
                    for (var i = 0; i < all.Length && i < _gnBtn[s].Length; i++)
                    {
                        if (_gnBtn[s][i] == null)
                            continue;
                        if (any)
                            ImGui.SameLine();
                        any = true;
                        var have = _gnOnTable[i] > 0 || _gnCreative;
                        ImGui.PushID(i);
                        ImGui.BeginDisabled(!have || _gnBusy.Length > 0);
                        if (ImGui.SmallButton(_gnBtn[s][i]))
                            (pickSlot, pickModule) = (s, i);
                        ImGui.EndDisabled();
                        ImGui.PopID();
                    }
                    if (!any)
                        ImGuiSafe.TextColored(Dim, "    no other module fits here");
                }
                ImGui.PopID();
            }
            ImGui.Separator();
            ImGuiSafe.TextColored(Dim, "Modules are parts: make them on an Assembler and put them on the parts table. The old module goes onto the table.");
            if (ImGui.Button("Close"u8))
                open = false;
        }
        ImGui.End();
        if (ShellGantryAuto is { } auto)
        {
            ShellGantryAuto = null; // (the window tool: the same request the button makes, by slot and module name)
            var i = Array.FindIndex(all, m => m.Name.Equals(auto.Module, StringComparison.OrdinalIgnoreCase));
            (pickSlot, pickModule) = i >= 0 && auto.Slot >= 0 && auto.Slot < ShellFrames.SlotCount ? (auto.Slot, i) : (-1, -1);
            ShellGantrySaid = pickModule >= 0 ? $"asked: {all[i].Name} into the {ShellFrames.SlotLabel(auto.Slot)}" : $"no module named {auto.Module}";
        }
        if (pickSlot >= 0 && pickModule >= 0)
        {
            game.SandboxGantry(_gnBody, _gnPart, pickSlot, pickModule);
            _gnFilledAt = 0;
        }
        if (!open)
            CloseShellGantry();
        return open;
    }
}
