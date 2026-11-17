using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A linked logistics part's panel (docs/sandbox/design/linked-logistics.md section 2): the use key on a link end, a Cargo Dock, a Cargo
/// Port, a Route Computer or a Logistics Dispatcher opens it; the command view's ROUTE opens a ship's Route Computer's from afar. It shows
/// the host's status lines (CBodyKind.Link) and sends what is changed (<see cref="CActKind.Link"/>: the server decides and says why not):
/// a name, a Receiver's code, a route's stops and leaving rules, a Dispatcher's threshold. It holds the mouse while open and closes on
/// Esc, on a local reset, and when its part goes or the player walks off (Program.SandboxLinked.cs).
/// </summary>
public sealed partial class Ui
{
    public bool LinkedOpen { get; private set; }
    public bool LinkedRemote { get; private set; } // (opened from the command view: no reach)
    private ushort _lkBody;
    private int _lkPart = -1;
    private ImInputString? _lkName, _lkCode;
    private int _lkVersion = -1;
    private int _lkStop = -1, _lkMinutes = 10, _lkFill = 95, _lkThreshold = 75, _lkTimeout = 15;
    private bool _lkByTime = true, _lkByFill = true, _lkByDone = true;

    public (ushort Body, int Part) LinkedTarget => (_lkBody, _lkPart);

    public void OpenLinked(ushort body, int part, bool remote = false)
    {
        if (_lkBody != body || _lkPart != part)
            _lkVersion = -1;
        (_lkBody, _lkPart, LinkedRemote) = (body, part, remote);
        LinkedOpen = true;
    }

    public void CloseLinked() => LinkedOpen = false;

    /// <summary>The panel (between NewFrame and Render). True while it stays open.</summary>
    public bool DrawLinked(Game game, Construct c, int part, double now)
    {
        if (!LinkedOpen)
            return false;
        var d = c.DefOf(part);
        var lp = c.Parts[part].Link;
        _lkName ??= new ImInputString(41);
        _lkCode ??= new ImInputString(12);
        if (lp != null && lp.Version != _lkVersion)
        {
            _lkVersion = lp.Version;
            ImGuiSafe.SetInput(_lkName, lp.Name);
            if (lp.Fleet is { } f)
                (_lkThreshold, _lkTimeout) = (f.Threshold, f.Timeout);
        }
        var open = true;
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(720 * _scale, vp.WorkSize.X * 0.92f);
        var h = MathF.Min(560 * _scale, vp.WorkSize.Y * 0.9f);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + (vp.WorkSize.X - w) * 0.5f, vp.WorkPos.Y + (vp.WorkSize.Y - h) * 0.5f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.95f);
        if (ImGui.Begin(ImGuiSafe.Escape(d.Name) + "###linked", ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            if (lp == null)
                ImGuiSafe.TextColored(Dim, "Waiting for the host...");
            else
            {
                foreach (var line in lp.Status.Split('\n'))
                    ImGuiSafe.TextColored(line.StartsWith("NOT", StringComparison.Ordinal) || line.StartsWith("NO ", StringComparison.Ordinal) || line.Contains("STALLED", StringComparison.Ordinal) ? StWarn : line.StartsWith("Linked", StringComparison.Ordinal) ? StGood : Dim, line);
                ImGui.Separator();
                ImGui.Text("Name"u8);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(260 * _scale);
                ImGui.InputText("##lkname"u8, _lkName);
                ImGui.SameLine();
                if (ImGui.Button("Rename"u8))
                    game.SandboxLink(c.Id, part, LinkOp.Name, text: ImGuiSafe.Text(_lkName));
                if (d.LinkKind is LinkKind.Transmitter or LinkKind.Relay)
                {
                    ImGuiSafe.TextColored(StHead, $"This end's code: {LinkedLogistics.Code(lp.Id)}  (type it into a Receiver's panel to pair)");
                    if (ImGui.Button("New code (its pairs drop)"u8))
                        game.SandboxLink(c.Id, part, LinkOp.NewCode);
                }
                if (d.LinkKind is LinkKind.Receiver or LinkKind.Relay)
                {
                    ImGui.Text("Pair with code"u8);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(120 * _scale);
                    ImGui.InputText("##lkcode"u8, _lkCode);
                    ImGui.SameLine();
                    if (ImGui.Button("Pair"u8))
                        game.SandboxLink(c.Id, part, LinkOp.Pair, text: ImGuiSafe.Text(_lkCode));
                    if (lp.Pair != 0)
                    {
                        ImGui.SameLine();
                        if (ImGui.Button("Unpair"u8))
                            game.SandboxLink(c.Id, part, LinkOp.Unpair);
                    }
                }
                if (d.IsLinkEnd && lp.Offers.Length > 0)
                {
                    ImGuiSafe.TextColored(StHead, "Linked networks hold or make (a terminal here orders them):");
                    for (var i = 0; i < lp.Offers.Length && i < 24; i++)
                    {
                        ref readonly var o = ref lp.Offers[i];
                        ImGuiSafe.TextColored(Dim, $"  {ConstructParts.Get(o.Kind)?.Name ?? "part"} - {(o.Free > 0 ? $"{o.Free} at " : "made at ")}{o.Where}{(o.Asleep ? " (asleep)" : "")}");
                    }
                }
                if (d.LinkKind == LinkKind.Port)
                    for (var i = 0; i < lp.Manifest.Count; i++)
                    {
                        var m = lp.Manifest[i];
                        if (m.Aboard == 0)
                            continue;
                        ImGui.PushID(3000 + i);
                        if (ImGui.SmallButton("Cancel line"u8))
                            game.SandboxLink(c.Id, part, LinkOp.CancelLine, m.Line);
                        ImGui.PopID();
                        ImGui.SameLine();
                        ImGuiSafe.TextColored(Dim, $"{m.Aboard} {(m.Use != 0 ? "submodule" : ConstructParts.Get(m.Kind)?.Name)} for {m.Dest}");
                    }
                if (d.LinkKind == LinkKind.Route)
                    DrawRouteEditor(game, c, part, lp);
                if (d.LinkKind == LinkKind.Dispatcher)
                {
                    ImGui.SetNextItemWidth(200 * _scale);
                    ImGui.SliderInt("Send at % of a ship's payload"u8, ref _lkThreshold, 10, 100);
                    ImGui.SetNextItemWidth(200 * _scale);
                    ImGui.SliderInt("or after minutes"u8, ref _lkTimeout, 1, 240);
                    if (ImGui.Button("Set"u8))
                        game.SandboxLink(c.Id, part, LinkOp.FleetRules, (uint)_lkThreshold, (uint)_lkTimeout);
                }
            }
            ImGui.Separator();
            if (ImGui.Button("Close"u8))
                open = false;
        }
        ImGui.End();
        if (!open)
            CloseLinked();
        return open;
    }

    private void DrawRouteEditor(Game game, Construct c, int part, LinkPart lp)
    {
        ImGui.Separator();
        ImGuiSafe.TextColored(StHead, "Leaving rules for a new stop (the first one met):");
        ImGui.Checkbox("full"u8, ref _lkByFill);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90 * _scale);
        ImGui.SliderInt("%##lkfill"u8, ref _lkFill, 10, 100);
        ImGui.Checkbox("waited"u8, ref _lkByTime);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90 * _scale);
        ImGui.SliderInt("min##lkmin"u8, ref _lkMinutes, 1, 60);
        ImGui.Checkbox("nothing more to load or unload"u8, ref _lkByDone);
        var rules = (uint)((_lkByTime ? LinkRoutes.LeaveTime : 0) | (_lkByFill ? LinkRoutes.LeaveFill : 0) | (_lkByDone ? LinkRoutes.LeaveDone : 0)) | (uint)_lkMinutes << 8 | (uint)_lkFill << 24;
        if (lp.Route is { N: > 0 } r)
            for (var i = 0; i < r.N; i++)
            {
                ImGui.PushID(4000 + i);
                if (ImGui.SmallButton("Set rules"u8))
                    game.SandboxLink(c.Id, part, LinkOp.RouteRules, (uint)i, rules);
                ImGui.PopID();
                ImGui.SameLine();
                ImGuiSafe.TextColored(Dim, $"stop {i + 1}: {r.Stops[i].Name}");
            }
        ImGuiSafe.TextColored(StHead, "Cargo Docks it can stop at:");
        if (lp.Docks.Length == 0)
            ImGuiSafe.TextColored(Dim, "  none known: a Cargo Dock on this planetoid, or on a network linked to one here");
        for (var i = 0; i < lp.Docks.Length; i++)
        {
            ref readonly var dk = ref lp.Docks[i];
            ImGui.PushID(5000 + i);
            if (ImGui.SmallButton("Add stop"u8))
                game.SandboxLink(c.Id, part, LinkOp.RouteAdd, (uint)dk.Body << 16 | dk.Part, rules, dk.World);
            ImGui.PopID();
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, $"{dk.Name} ({LinkLedger.Title(dk.World)})");
        }
        if (ImGui.Button("Start / Resume"u8))
            game.SandboxLink(c.Id, part, LinkOp.RouteStart);
        ImGui.SameLine();
        if (ImGui.Button("Suspend"u8))
            game.SandboxLink(c.Id, part, LinkOp.RouteStop);
        ImGui.SameLine();
        if (ImGui.Button("Clear route"u8))
            game.SandboxLink(c.Id, part, LinkOp.RouteClear);
    }
}
