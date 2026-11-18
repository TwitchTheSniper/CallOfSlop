using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The command view's fleet panel (docs/sandbox/design/faction-controllers.md phase 6, SURVIVAL.md's "fleet room"): a player's own hired crews'
/// ships as groups, the player (or a hand) their admiral. Bottom left: the groups you may command, the picked group's orders (hold, form on me,
/// pull back), its shape, spacing, doctrine, task and admiral; a picked ship of yours with a crew starts a group or joins one. A right click
/// with a group's ship picked adds the group's orders for what is under the click (escort, engage, board, screen; go there, a route's stops) to
/// the order menu (UiCommandOrders.cs). Everything goes to the host (<see cref="Game.SandboxFleet"/>), which checks it and answers with a
/// notice; what the panel shows is the host's word (Msg.FleetGroups). Text is made only when it changes.
/// </summary>
public sealed partial class Ui
{
    private int _cmdAdmSlot = -1; // the picked group (its slot), -1: none
    private CmdLabel _cmdAdmHead, _cmdAdmLine, _cmdAdmLine2, _cmdAdmDoctrine, _cmdAdmTask, _cmdAdmAdmiral, _cmdAdmSpacing;
    private readonly CmdLabel[] _cmdAdmRows = new CmdLabel[Game.FleetGroupsEach];
    private bool _cmdAdmHasGround; // the right click's ground point (a group's place to go or a stop)
    private Vector3 _cmdAdmGround;
    private float _cmdAdmDwell = 30;

    private static readonly string[] CmdFormations = ["COLUMN", "LINE", "WEDGE", "SCREEN"];
    private static readonly GroupTask[] CmdTasks = [GroupTask.Patrol, GroupTask.Escort, GroupTask.Strike, GroupTask.Defend, GroupTask.Convoy, GroupTask.Mining, GroupTask.Haul, GroupTask.Trade, GroupTask.Construction];

    /// <summary>The picked group, if it is still one of yours.</summary>
    private Game.FleetGroupView? CmdAdmPicked(Game g)
    {
        foreach (var v in g.FleetGroups)
            if (v.Slot == _cmdAdmSlot)
                return v;
        return null;
    }

    /// <summary>The fleet panel, bottom left (nothing when you have no group and the picked craft has no crew of yours).</summary>
    private void CmdAdmiral(ImDrawListPtr dl, Game g, float fade)
    {
        var mine = g.CrewRosters.TryGetValue(_cmdSel, out var roster) && roster.Own && g.Local is { } me && Factions.Shares(roster.Owner, me.Id);
        var inGroup = g.FleetGroupOf(_cmdSel);
        if (inGroup != null)
            _cmdAdmSlot = inGroup.Slot; // (picking a group's ship picks its group)
        var picked = CmdAdmPicked(g);
        if (g.FleetGroups.Count == 0 && !mine)
            return;
        var pad = 22 * _scale;
        var w = 330 * _scale;
        var row = 21 * _scale;
        var bh = 19 * _scale;
        var rows = g.FleetGroups.Count;
        var h = 34 * _scale + rows * row + (picked != null ? 9 * row : 0) + (mine && inGroup == null ? 2 * row + 4 * _scale : 0) + 8 * _scale;
        var p0 = new float2(_cvOrg.X + pad, _cvOrg.Y + _cvSize.Y - pad - 54 * _scale - h);
        var p1 = p0 + new float2(w, h);
        CmdPlate(dl, p0, p1, fade, CmdGreen);
        CmdPanelArea(p0, p1);
        if (_cmdAdmHead.Stale(g.FleetGroups.Count))
            _cmdAdmHead.Text = $"FLEET GROUPS  [{g.FleetGroups.Count}]";
        CmdText(dl, p0 + new float2(12 * _scale, 9 * _scale), 12, CmdGreen, fade, _cmdAdmHead.Text!);
        var x = p0.X + 8 * _scale;
        var y = p0.Y + 32 * _scale;
        var m = ImGui.GetMousePos();
        var clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_cmdOrderOpen;
        for (var k = 0; k < rows && k < _cmdAdmRows.Length; k++)
        {
            var v = g.FleetGroups[k];
            ref var lab = ref _cmdAdmRows[k];
            var key = v.Number | (long)v.Count << 16 | (long)v.Order << 24 | (long)v.OnNet << 32 | (long)(v.Heard ? 1 : 0) << 40 | (long)(v.PlayerAdmiral ? 1 : 0) << 41 | (long)(v.Retreating ? 1 : 0) << 42;
            if (lab.Stale(key))
                lab.Text = $"GROUP {v.Number}   {v.Count} SHIP{(v.Count == 1 ? "" : "S")}   {CmdOrderWord(v.Order)}{(v.Retreating ? " (PULLING BACK)" : "")}   {(v.Heard ? $"NET {v.OnNet}/{v.Count}" : "OUT OF REACH")}";
            var q0 = new float2(x, y);
            var q1 = q0 + new float2(w - 16 * _scale, row - 2 * _scale);
            var hover = m.X >= q0.X && m.X < q1.X && m.Y >= q0.Y && m.Y < q1.Y && !_cmdOrderOpen;
            var on = v.Slot == _cmdAdmSlot;
            if (on || hover)
                dl.AddRectFilled(q0, q1, Hud.Colour(CmdGreen, (on ? 0.18f : 0.09f) * fade));
            CmdText(dl, q0 + new float2(6 * _scale, 3 * _scale), 11, on ? CmdWhite : v.Heard ? CmdGreen : CmdAmber, fade, lab.Text!, 0, false);
            if (hover && clicked)
            {
                _cmdAdmSlot = v.Slot;
                if (v.Count > 0 && !v.Has(_cmdSel))
                    _cmdSel = v.Ships[0]; // (its first ship picked: a right click then orders the group)
            }
            y += row;
        }
        if (mine && inGroup == null)
        {
            // A ship of yours with a hired crew, in no group: start one, or join the picked group.
            y += 4 * _scale;
            var bw = (w - 22 * _scale) * 0.5f;
            if (CmdButton(dl, new float2(x, y), new float2(x + bw, y + bh), "NEW GROUP: THIS SHIP", CmdGreen, fade, false, 10.5f))
                g.SandboxFleet(FleetOp.New, body: _cmdSel);
            if (CmdButton(dl, new float2(x + bw + 6 * _scale, y), new float2(x + 2 * bw + 6 * _scale, y + bh), "NEW GROUP: CREWS NEAR ME", CmdGreen, fade, false, 10.5f))
                g.SandboxFleet(FleetOp.New, body: _cmdSel, arg: 1);
            y += row;
            if (picked != null && CmdButton(dl, new float2(x, y), new float2(x + 2 * bw + 6 * _scale, y + bh), $"ADD THIS SHIP TO GROUP {picked.Number}", CmdGreen, fade, false, 10.5f))
                g.SandboxFleet(FleetOp.Add, picked.Slot, _cmdSel);
            y += row;
        }
        if (picked == null)
            return;
        CmdAdmGroup(dl, g, picked, x, y, w - 16 * _scale, row, bh, fade);
    }

    /// <summary>The picked group's lines and buttons.</summary>
    private void CmdAdmGroup(ImDrawListPtr dl, Game g, Game.FleetGroupView v, float x, float y, float w, float row, float bh, float fade)
    {
        CmdLine(dl, new float2(x, y + 2 * _scale), new float2(x + w, y + 2 * _scale), CmdGreen, 0.4f * fade);
        y += 6 * _scale;
        if (_cmdAdmLine.Stale(v.Number | (long)v.Order << 16 | (long)v.OrderBody << 24 | (long)v.RouteCount << 40 | (long)v.RouteAt << 48))
            _cmdAdmLine.Text = $"ORDER: {CmdOrderWord(v.Order)}{(v.OrderBody != 0 ? $" CRAFT {v.OrderBody}" : "")}{(v.RouteCount > 0 ? $"   ROUTE: STOP {v.RouteAt + 1} OF {v.RouteCount}" : "")}";
        CmdText(dl, new float2(x + 4 * _scale, y), 11, CmdWhite, fade, _cmdAdmLine.Text!, 0, false);
        y += row - 4 * _scale;
        if (_cmdAdmLine2.Stale(v.Number | (long)v.Task << 16 | (long)v.Formation << 24))
            _cmdAdmLine2.Text = $"RIGHT CLICK: GROUP ORDERS FOR WHAT IS UNDER IT";
        CmdText(dl, new float2(x + 4 * _scale, y), 10, CmdDim, fade, _cmdAdmLine2.Text!, 0, false);
        y += row - 2 * _scale;
        // Row: the plain orders.
        var b4 = (w - 3 * 5 * _scale) / 4;
        float2 B(int i, float yy) => new(x + i * (b4 + 5 * _scale), yy);
        if (CmdButton(dl, B(0, y), B(0, y) + new float2(b4, bh), "HOLD", CmdCyan, fade, v.Order == OrderKind.Hold, 10.5f))
            g.SandboxFleet(FleetOp.Order, v.Slot, arg: (byte)OrderKind.Hold);
        if (CmdButton(dl, B(1, y), B(1, y) + new float2(b4, bh), "FORM ON ME", CmdCyan, fade, v.Order == OrderKind.Form, 10.5f))
            g.SandboxFleet(FleetOp.Order, v.Slot, arg: (byte)OrderKind.Form);
        if (CmdButton(dl, B(2, y), B(2, y) + new float2(b4, bh), "PULL BACK", CmdAmber, fade, v.Order == OrderKind.Retreat, 10.5f))
            g.SandboxFleet(FleetOp.Order, v.Slot, arg: (byte)OrderKind.Retreat);
        if (CmdButton(dl, B(3, y), B(3, y) + new float2(b4, bh), "STAND DOWN", CmdRed, fade, false, 10.5f))
            g.SandboxFleet(FleetOp.Disband, v.Slot);
        y += row;
        // Row: the shape.
        for (var i = 0; i < 4; i++)
            if (CmdButton(dl, B(i, y), B(i, y) + new float2(b4, bh), CmdFormations[i], CmdGreen, fade, (int)v.Formation == i, 10.5f))
                g.SandboxFleet(FleetOp.Formation, v.Slot, arg: (byte)i);
        y += row;
        // Row: spacing, doctrine.
        if (_cmdAdmSpacing.Stale((long)v.Spacing))
            _cmdAdmSpacing.Text = $"{v.Spacing:0} M APART";
        if (CmdButton(dl, B(0, y), B(0, y) + new float2(b4 * 0.45f, bh), "-", CmdGreen, fade, false, 11))
            g.SandboxFleet(FleetOp.Spacing, v.Slot, value: MathF.Max(CrewFleet.MinSpacing, v.Spacing / 1.25f));
        CmdText(dl, new float2(B(0, y).X + b4 * 0.95f, y + 3 * _scale), 10.5f, CmdWhite, fade, _cmdAdmSpacing.Text!, 1, false);
        if (CmdButton(dl, B(1, y) + new float2(b4 * 0.45f, 0), B(1, y) + new float2(b4 * 0.9f, bh), "+", CmdGreen, fade, false, 11))
            g.SandboxFleet(FleetOp.Spacing, v.Slot, value: MathF.Min(CrewFleet.MaxSpacing, v.Spacing * 1.25f));
        if (_cmdAdmDoctrine.Stale(v.Doctrine))
            _cmdAdmDoctrine.Text = "DOCTRINE: " + Game.DoctrineName(v.Doctrine).ToUpperInvariant();
        if (CmdButton(dl, B(2, y), B(3, y) + new float2(b4, bh), _cmdAdmDoctrine.Text!, CmdGreen, fade, false, 10.5f))
            g.SandboxFleet(FleetOp.Doctrine, v.Slot, arg: (byte)((v.Doctrine + 1) % Math.Max(1, Game.DoctrineCount)));
        y += row;
        // Row: task, admiral.
        var ti = Array.IndexOf(CmdTasks, v.Task);
        if (_cmdAdmTask.Stale((long)v.Task))
            _cmdAdmTask.Text = "TASK: " + v.Task.ToString().ToUpperInvariant();
        if (CmdButton(dl, B(0, y), B(1, y) + new float2(b4, bh), _cmdAdmTask.Text!, CmdGreen, fade, false, 10.5f))
            g.SandboxFleet(FleetOp.Task, v.Slot, arg: (byte)CmdTasks[(ti + 1) % CmdTasks.Length]);
        if (_cmdAdmAdmiral.Stale(v.PlayerAdmiral ? -1 : v.AdmiralName))
            _cmdAdmAdmiral.Text = v.PlayerAdmiral ? "ADMIRAL: YOU" : "ADMIRAL: " + CrewNames.Of(v.AdmiralName).ToUpperInvariant();
        if (CmdButton(dl, B(2, y), B(3, y) + new float2(b4, bh), _cmdAdmAdmiral.Text!, CmdGreen, fade, false, 10.5f))
            g.SandboxFleet(FleetOp.Admiral, v.Slot, uid: CmdAdmNextHand(g, v));
        y += row;
        // Row: the picked ship in the group.
        if (v.Has(_cmdSel))
        {
            if (CmdButton(dl, B(0, y), B(1, y) + new float2(b4, bh), "MAKE THIS THE FLAGSHIP", CmdGreen, fade, false, 10.5f))
                g.SandboxFleet(FleetOp.Flagship, v.Slot, _cmdSel);
            if (CmdButton(dl, B(2, y), B(3, y) + new float2(b4, bh), "TAKE THIS SHIP OUT", CmdAmber, fade, false, 10.5f))
                g.SandboxFleet(FleetOp.Remove, v.Slot, _cmdSel);
        }
        y += row;
        // Row: the route's stops' dwell.
        CmdText(dl, new float2(x + 4 * _scale, y + 3 * _scale), 10, CmdDim, fade, "A STOP'S WORK TIME", 0, false);
        if (CmdButton(dl, B(2, y), B(2, y) + new float2(b4 * 0.45f, bh), "-", CmdGreen, fade, false, 11))
            _cmdAdmDwell = MathF.Max(0, _cmdAdmDwell - 15);
        if (_cmdAdmDwellLabel.Stale((long)_cmdAdmDwell))
            _cmdAdmDwellLabel.Text = $"{_cmdAdmDwell:0} S";
        CmdText(dl, new float2(B(2, y).X + b4 * 0.95f, y + 3 * _scale), 10.5f, CmdWhite, fade, _cmdAdmDwellLabel.Text!, 1, false);
        if (CmdButton(dl, B(3, y) + new float2(b4 * 0.45f, 0), B(3, y) + new float2(b4 * 0.9f, bh), "+", CmdGreen, fade, false, 11))
            _cmdAdmDwell = MathF.Min(600, _cmdAdmDwell + 15);
        CmdAdmMarks(dl, g, v, fade);
    }

    private CmdLabel _cmdAdmDwellLabel;

    /// <summary>The next hand to lead the group (cycling through its ships' rosters), or 0: you.</summary>
    private uint CmdAdmNextHand(Game g, Game.FleetGroupView v)
    {
        var take = v.PlayerAdmiral; // (you lead it: the first hand next)
        for (var k = 0; k < v.Count; k++)
            if (g.CrewRosters.TryGetValue(v.Ships[k], out var r))
                foreach (var mem in r.Members)
                {
                    if (mem.State is CrewState.Dead or CrewState.Surrendered)
                        continue;
                    if (take)
                        return mem.Uid;
                    if (mem.Job == CrewJob.Admiral)
                        take = true;
                }
        return 0;
    }

    /// <summary>Brackets round the picked group's ships on the map.</summary>
    private void CmdAdmMarks(ImDrawListPtr dl, Game g, Game.FleetGroupView v, float fade)
    {
        for (var k = 0; k < _cmdCraftCount; k++)
            if (v.Has(_cmdCraft[k].Id) && CmdOn(_cmdCraft[k].At))
                CmdBrackets(dl, _cmdCraft[k].At, MathF.Max(9 * _scale, MathF.Min(_cmdCraft[k].Px, 60 * _scale)) + 5 * _scale, CmdGreen, 0.8f * fade, 1.2f);
    }

    private static string CmdOrderWord(OrderKind k) => k switch
    {
        OrderKind.None or OrderKind.Hold => "HOLD",
        OrderKind.Form => "FORM UP",
        OrderKind.GoTo => "GO TO",
        OrderKind.Engage => "ENGAGE",
        OrderKind.Escort => "ESCORT",
        OrderKind.Retreat => "PULL BACK",
        OrderKind.Board => "BOARD",
        OrderKind.Screen => "SCREEN",
        OrderKind.Route => "ROUTE",
        _ => k.ToString().ToUpperInvariant(),
    };

    // ---- The right click: the group's orders in the order menu ---------------------------------------------------------------------

    /// <summary>The group's part of the order menu (UiCommandOrders.cs): true when an order was sent (the menu closes).</summary>
    private bool CmdAdmOrders(Game g)
    {
        if (g.FleetGroupOf(_cmdOrderShip) is not { } v)
            return false;
        var sent = false;
        var wide = 300 * _scale;
        ImGui.Separator();
        if (_cmdAdmMenuTitle.Stale(v.Number | (long)v.Count << 16))
            _cmdAdmMenuTitle.Text = $"GROUP {v.Number} ({v.Count} SHIPS)";
        ImGuiSafe.TextColored(new float4(CmdGreen.X, CmdGreen.Y, CmdGreen.Z, 1f), _cmdAdmMenuTitle.Text!);
        if (_cmdOrderDock != 0 && !v.Has(_cmdOrderDock))
        {
            var t = _cmdOrderDock;
            if (ImGui.Button("GROUP: ESCORT IT"u8, new float2(wide, 0)))
                sent = Send(OrderKind.Escort, t);
            if (ImGui.Button("GROUP: ENGAGE IT (FOCUS FIRE)"u8, new float2(wide, 0)))
                sent = Send(OrderKind.Engage, t);
            if (ImGui.Button("GROUP: SCREEN IT"u8, new float2(wide, 0)))
                sent = Send(OrderKind.Screen, t);
            if (ImGui.Button("GROUP: FORM ON IT"u8, new float2(wide, 0)))
                sent = Send(OrderKind.Form, t);
            if (ImGui.Button("GROUP: CLOSE TO BOARD IT"u8, new float2(wide, 0)))
                sent = Send(OrderKind.Board, t);
        }
        if (_cmdAdmHasGround)
        {
            if (ImGui.Button("GROUP: GO HERE"u8, new float2(wide, 0)))
            {
                g.SandboxFleet(FleetOp.Order, v.Slot, at: _cmdAdmGround, arg: (byte)OrderKind.GoTo);
                sent = true;
            }
            if (ImGui.Button("GROUP: PULL BACK HERE"u8, new float2(wide, 0)))
            {
                g.SandboxFleet(FleetOp.Order, v.Slot, at: _cmdAdmGround, arg: (byte)OrderKind.Retreat);
                sent = true;
            }
            ImGuiSafe.TextColored(new float4(CmdDim.X, CmdDim.Y, CmdDim.Z, 1f), "ADD A STOP TO ITS ROUTE (LOOPED):");
            var half = (wide - 6 * _scale) * 0.5f;
            if (ImGui.Button("LOAD"u8, new float2(half, 0)))
                sent = Stop(OrderKind.Load);
            ImGui.SameLine();
            if (ImGui.Button("UNLOAD"u8, new float2(half, 0)))
                sent = Stop(OrderKind.Unload);
            if (ImGui.Button("MINE"u8, new float2(half, 0)))
                sent = Stop(OrderKind.Mine);
            ImGui.SameLine();
            if (ImGui.Button("DELIVER"u8, new float2(half, 0)))
                sent = Stop(OrderKind.Deliver);
            if (ImGui.Button("PASS THROUGH"u8, new float2(wide, 0)))
                sent = Stop(OrderKind.Route);
        }
        return sent;

        bool Send(OrderKind kind, ushort body)
        {
            g.SandboxFleet(FleetOp.Order, v.Slot, body, arg: (byte)kind);
            return true;
        }

        bool Stop(OrderKind work)
        {
            g.SandboxFleet(FleetOp.Stop, v.Slot, at: _cmdAdmGround, arg: (byte)work, value: work == OrderKind.Route ? 0 : _cmdAdmDwell);
            return true;
        }
    }

    private CmdLabel _cmdAdmMenuTitle;

    /// <summary>The script and the checks: a group order without the mouse, for the picked craft's group. Null, or why not.</summary>
    public string? CommandFleet(Game g, FleetOp op, OrderKind kind = OrderKind.None, ushort body = 0, Vector3 at = default, byte arg = 0, float value = 0)
    {
        var v = g.FleetGroupOf(_cmdSel) ?? CmdAdmPicked(g);
        if (op == FleetOp.New)
        {
            g.SandboxFleet(FleetOp.New, body: body != 0 ? body : _cmdSel, arg: arg);
            return null;
        }
        if (v == null)
            return "no fleet group picked";
        g.SandboxFleet(op, v.Slot, body, at: at, arg: op == FleetOp.Order ? (byte)kind : arg, value: value);
        return null;
    }
}
