using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A lifter's panel (docs/sandbox/design/arms-and-cranes.md section 5 "By hand", phase 7): its points in reach with what each holds, and
/// its hand orders. Click a kind under a pick point (a rack, a pad, a machine's out), then a put point, set a count and press Order: the
/// arm or crane carries them before its own work (<see cref="CActKind.Lifter"/>: the server decides and says why not). The list is this
/// peer's own, worked out at 2 Hz from what it sees; the orders are the server's, heard with the lifts. It holds the mouse while open
/// and closes on Esc, on a local reset, and when the lifter goes or the player walks off (Program.SandboxLifter.cs).
/// </summary>
public sealed partial class Ui
{
    public bool LifterOpen { get; private set; }
    private ushort _lfBody;
    private int _lfPart = -1;
    private readonly LiftPoint[] _lfPoints = new LiftPoint[ConstructLifters.MaxPoints];
    private int _lfN;
    private readonly string[] _lfNames = new string[ConstructLifters.MaxPoints];
    private readonly ushort[][] _lfDefs = new ushort[ConstructLifters.MaxPoints][];
    private readonly int[][] _lfCounts = new int[ConstructLifters.MaxPoints][];
    private readonly int[] _lfKinds = new int[ConstructLifters.MaxPoints];
    private readonly string[][] _lfKindText = new string[ConstructLifters.MaxPoints][];
    private readonly string?[] _lfNote = new string?[ConstructLifters.MaxPoints]; // (a pad's: a piece on it that it won't take, and why)
    private double _lfFilledAt;
    private int _lfFrom = -1, _lfKind = -1, _lfTo = -1, _lfCount = 1;
    private string _lfLook = "";

    /// <summary>The window tool: the next frame picks point <paramref name="from"/>'s kind named <paramref name="kind"/>, point <paramref name="to"/>, the count, and presses Order.</summary>
    public (int From, string Kind, int To, int Count)? LifterAuto;
    public string LifterSaid { get; private set; } = "";

    public (ushort Body, int Part) LifterTarget => (_lfBody, _lfPart);
    public int LifterPointCount => _lfN;
    public string LifterPointText(int k) => k >= 0 && k < _lfN ? $"{k}: {_lfNames[k]}{(_lfKinds[k] > 0 ? " holds " + string.Join(", ", _lfKindText[k].Take(_lfKinds[k])) : "")}{(_lfNote[k] is { } note ? " - " + note : "")}" : "";

    public void OpenLifter(ushort body, int part)
    {
        if (_lfBody != body || _lfPart != part)
            (_lfFrom, _lfKind, _lfTo, _lfCount) = (-1, -1, -1, 1);
        (_lfBody, _lfPart, _lfFilledAt) = (body, part, 0);
        LifterOpen = true;
    }

    public void CloseLifter() => LifterOpen = false;

    private void LifterFill(ConstructWorld world, Construct c, int part)
    {
        _lfN = ConstructLifters.PanelPoints(world, c, part, _lfPoints);
        for (var k = 0; k < _lfN; k++)
        {
            _lfDefs[k] ??= new ushort[12];
            _lfCounts[k] ??= new int[12];
            _lfKindText[k] ??= new string[12];
            _lfNames[k] = ConstructLifters.PointName(world, _lfPoints[k]);
            _lfKinds[k] = ConstructLifters.Picks(_lfPoints[k].Kind) ? ConstructLifters.PointHolds(world, _lfPoints[k], _lfDefs[k], _lfCounts[k]) : 0;
            _lfNote[k] = world.Get(_lfPoints[k].Body) is { } pb ? ConstructLifters.PadRefusedNote(world, pb, _lfPoints[k].Part) : null;
            for (var q = 0; q < _lfKinds[k]; q++)
                _lfKindText[k][q] = $"{_lfCounts[k][q]} {ConstructParts.Get(_lfDefs[k][q])?.Name ?? "part"}";
        }
        _lfLook = ConstructLifters.Look(world, c, part);
    }

    /// <summary>The panel (between NewFrame and Render). True while it stays open.</summary>
    public bool DrawLifter(Game game, Construct c, int part, double now)
    {
        if (!LifterOpen)
            return false;
        var world = game.Constructs;
        if (now - _lfFilledAt > 0.5 || _lfFilledAt == 0)
        {
            _lfFilledAt = now;
            LifterFill(world, c, part);
        }
        if (_lfFrom >= _lfN || _lfTo >= _lfN)
            (_lfFrom, _lfKind, _lfTo) = (-1, -1, -1);
        var l = ConstructLifters.Find(world, c.Id, part);
        var open = true;
        var order = false;
        var cancel = -1;
        var clear = false;
        var vp = ImGui.GetMainViewport();
        var w = MathF.Min(900 * _scale, vp.WorkSize.X * 0.92f);
        var h = MathF.Min(560 * _scale, vp.WorkSize.Y * 0.9f);
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + (vp.WorkSize.X - w) * 0.5f, vp.WorkPos.Y + (vp.WorkSize.Y - h) * 0.5f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(w, h), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(0.95f);
        if (ImGui.Begin(ImGuiSafe.Escape(c.DefOf(part).Name) + "###lifter", ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
        {
            ImGuiSafe.TextWrapped(_lfLook);
            ImGui.Separator();
            var foot = ImGui.GetFrameHeightWithSpacing() * 2 + 140 * _scale;
            var half = (ImGui.GetContentRegionAvail().X - 8 * _scale) * 0.5f;
            if (ImGui.BeginChild("##lffrom"u8, new float2(half, -foot), ImGuiChildFlags.Borders))
            {
                ImGuiSafe.TextColored(StHead, "1. Take from (click a kind)");
                var any = false;
                for (var k = 0; k < _lfN; k++)
                {
                    if (!ConstructLifters.Picks(_lfPoints[k].Kind))
                        continue;
                    any = true;
                    ImGuiSafe.TextColored(Dim, _lfNames[k]);
                    if (_lfKinds[k] == 0)
                        ImGuiSafe.TextColored(Dim, "    (empty)");
                    if (_lfNote[k] is { } note)
                        ImGuiSafe.TextColored(StWarn, "    " + note); // (it lies there but can't be ordered: the pad's reason)
                    for (var q = 0; q < _lfKinds[k]; q++)
                    {
                        ImGui.PushID(k * 16 + q);
                        if (ImGui.Selectable("    " + ImGuiSafe.Escape(_lfKindText[k][q]), _lfFrom == k && _lfKind == q))
                            (_lfFrom, _lfKind) = (k, q);
                        ImGui.PopID();
                    }
                }
                if (!any)
                    ImGuiSafe.TextColored(Dim, "Nothing it can take from is in reach: a rack, a Lay-down Pad or a machine's Piece out with no conveyor.");
            }
            ImGui.EndChild();
            ImGui.SameLine();
            if (ImGui.BeginChild("##lfto"u8, new float2(0, -foot), ImGuiChildFlags.Borders))
            {
                ImGuiSafe.TextColored(StHead, "2. Put to");
                for (var k = 0; k < _lfN; k++)
                {
                    if (!ConstructLifters.Puts(_lfPoints[k].Kind) || k == _lfFrom)
                        continue;
                    ImGui.PushID(1000 + k);
                    if (ImGui.Selectable(ImGuiSafe.Escape(_lfNames[k]), _lfTo == k))
                        _lfTo = k;
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
            var ready = _lfFrom >= 0 && _lfKind >= 0 && _lfKind < _lfKinds[_lfFrom] && _lfTo >= 0;
            ImGui.Text("3. How many"u8);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(110 * _scale);
            ImGui.InputInt("##lfcount"u8, ref _lfCount, 1, 5);
            _lfCount = Math.Clamp(_lfCount, 1, ConstructLifters.MaxOrderCount);
            ImGui.SameLine();
            ImGui.BeginDisabled(!ready || l is { OrderN: >= ConstructLifters.MaxOrders });
            ImGui.PushStyleColor(ImGuiCol.Button, CrTabOn);
            order = ImGui.Button("Order"u8, new float2(120 * _scale, 0));
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGuiSafe.TextColored(ready ? StGood : Dim, ready ? $"{_lfCount} {ConstructParts.Get(_lfDefs[_lfFrom][_lfKind])?.Name}: {_lfNames[_lfFrom]} -> {_lfNames[_lfTo]}" : "Pick a kind, then where it goes.");
            ImGui.Separator();
            ImGuiSafe.TextColored(StHead, $"Orders ({l?.OrderN ?? 0} of {ConstructLifters.MaxOrders}): carried first, in this order, before its own work");
            for (var oi = 0; l != null && oi < l.OrderN; oi++)
            {
                ImGui.PushID(2000 + oi);
                if (ImGui.SmallButton("Cancel"u8))
                    cancel = l.Orders![oi].Id;
                ImGui.PopID();
                ImGui.SameLine();
                ImGuiSafe.TextColored(l.Orders![oi].Why == LiftOrderWhy.None ? StGood : StWarn, ConstructLifters.OrderText(world, l.Orders[oi]));
            }
            if (l is { OrderN: > 0 })
                clear = ImGui.Button("Clear orders"u8);
            if (l is { OrderN: > 0 })
                ImGui.SameLine();
            if (ImGui.Button("Close"u8))
                open = false;
        }
        ImGui.End();
        if (LifterAuto is { } auto)
        {
            // (the window tool: the same selection and the same button, by index and name)
            LifterAuto = null;
            (_lfFrom, _lfTo, _lfCount, _lfKind) = (auto.From, auto.To, Math.Clamp(auto.Count, 1, ConstructLifters.MaxOrderCount), -1);
            for (var q = 0; auto.From >= 0 && auto.From < _lfN && q < _lfKinds[auto.From]; q++)
                if (_lfKind < 0 && (ConstructParts.Get(_lfDefs[auto.From][q])?.Name ?? "").Contains(auto.Kind, StringComparison.OrdinalIgnoreCase))
                    _lfKind = q;
            order = _lfFrom >= 0 && _lfFrom < _lfN && _lfKind >= 0 && _lfTo >= 0 && _lfTo < _lfN;
            LifterSaid = order ? "ordered" : $"no such pick (from {auto.From}, kind {auto.Kind}, to {auto.To}; {_lfN} points){(auto.From >= 0 && auto.From < _lfN && _lfNote[auto.From] is { } why ? ": " + why : "")}";
        }
        if (order && _lfFrom >= 0 && _lfKind >= 0 && _lfTo >= 0)
            game.SandboxLifter(_lfBody, _lfPart, LiftOrderOp.Add, 0, _lfDefs[_lfFrom][_lfKind], _lfCount, _lfPoints[_lfFrom], _lfPoints[_lfTo]);
        if (cancel >= 0)
            game.SandboxLifter(_lfBody, _lfPart, LiftOrderOp.Cancel, (byte)cancel);
        if (clear)
            game.SandboxLifter(_lfBody, _lfPart, LiftOrderOp.Clear);
        if (!open)
            CloseLifter();
        return open;
    }
}
