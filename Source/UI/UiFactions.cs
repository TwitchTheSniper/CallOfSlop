using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Faction window (F2 in Sandbox by default; docs/sandbox/CONSTRUCTS.md, "Factions"): found a faction or accept an
/// invite, see who is in it, invite the players here (the leader), leave, and give a construct you own or the part in
/// your hands to a member. Everything a member owns is shared with the others. The server decides every action
/// (GameSandboxFactions.cs); a refusal shows on the HUD. Closed, it costs one bool check.
/// </summary>
public sealed partial class Ui
{
    public bool FactionsOpen;

    /// <summary>Most of your constructs listed for giving, nearest first.</summary>
    private const int FactionGiveRows = 12;

    private ImInputString? _faName;
    private readonly List<(Construct C, float Dist)> _faMine = new(32);
    private readonly List<byte> _faMates = new(16);

    public void DrawFactions(Game? game, int bind)
    {
        if (!FactionsOpen)
            return;
        ImGui.SetNextWindowSize(new float2(520 * _scale, 560 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.96f);
        if (ImGui.Begin("Faction###faction"u8, ref FactionsOpen))
        {
            if (game is not { SandboxOn: true } || game.Local is not { } me)
                ImGuiSafe.TextWrapped("Factions are a Sandbox thing: start or join a Sandbox game.");
            else
                DrawFactionBody(game, me);
            ImGuiSafe.TextColored(Dim, $"{Keybinds.Name(bind)} or Esc closes this window.");
        }
        ImGui.End();
    }

    private void DrawFactionBody(Game game, Player me)
    {
        var t = game.FactionTable;
        var mine = t.Of[me.Id];
        _faMates.Clear();
        if (mine != 0)
            for (var k = 0; k < 255; k++)
                if (t.Of[k] == mine && k != me.Id)
                    _faMates.Add((byte)k);

        // ---- Invites
        if (t.Invited[me.Id] is var inv and not 0 && inv != mine && t.Names[inv].Length > 0)
        {
            ImGuiSafe.TextColored(Orange, $"You are invited to join {t.Names[inv]}.");
            if (mine != 0)
                ImGuiSafe.TextColored(Dim, $"Joining it takes you out of {t.Names[mine]}.");
            if (ImGui.Button("Join##faacc"u8))
                game.SandboxFaction(FactionOp.Accept, inv);
            ImGui.SameLine();
            if (ImGui.Button("No thanks##fadec"u8))
                game.SandboxFaction(FactionOp.Decline);
            ImGui.Separator();
        }

        // ---- Your faction, or founding one
        if (mine == 0)
        {
            ImGuiSafe.TextWrapped("You are not in a faction. Found one and invite people: everything each member owns (vehicles, bases, beacons and their stores) is shared with the others, and you can give things to each other.");
            _faName ??= new ImInputString(FactionTable.NameMax + 1);
            ImGui.SetNextItemWidth(260 * _scale);
            var go = false;
            try
            {
                go = ImGui.InputTextWithHint("##faname"u8, "Faction name"u8, _faName, ImGuiInputTextFlags.EnterReturnsTrue);
            }
            catch (Exception ex)
            {
                ImGuiSafe.FieldFailed("faction name", ex);
            }
            ImGui.SameLine();
            go |= ImGui.Button("Found faction##fafound"u8);
            if (go)
            {
                game.SandboxFaction(FactionOp.Found, text: ImGuiSafe.Text(_faName));
                ImGuiSafe.SetInput(_faName, "");
            }
            DrawFactionList(game, t);
            return;
        }
        var leads = t.Leader[mine] == me.Id;
        ImGuiSafe.TextColored(Orange, $"{t.Names[mine]}{(leads ? " (you lead it)" : "")}");
        ImGuiSafe.TextColored(Dim, "What each member owns is shared: drive, build on, configure and use it as your own.");
        ImGuiSafe.TextWrapped($"You ({me.Name}){(leads ? " - leader" : "")}");
        foreach (var k in _faMates)
        {
            var here = game.Players.TryGetValue(k, out var p) && k < Game.MaxPlayers;
            ImGui.PushID(k);
            ImGuiSafe.TextWrapped($"{game.FactionMemberName(k)}{(t.Leader[mine] == k ? " - leader" : "")}{(here ? "" : " (away)")}");
            if (leads)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove"u8))
                    game.SandboxFaction(FactionOp.Remove, k);
            }
            if (here && p!.Alive && me.Alive && game.SandboxHands.Hand(me.Id).Held != ConstructParts.None && Vector3.Distance(me.Position, p.Position) <= Game.FactionHandReach)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Hand over what I hold"u8))
                    game.SandboxFaction(FactionOp.GiveHeld, k);
            }
            ImGui.PopID();
        }
        if (ImGui.Button("Leave faction##faleave"u8))
            game.SandboxFaction(FactionOp.Leave);

        // ---- Invite
        if (leads)
        {
            ImGui.Separator();
            ImGuiSafe.TextColored(Orange, "Invite");
            var any = false;
            foreach (var p in game.Players.Values)
            {
                if (p.Id == me.Id || !Game.FactionPerson(p) || t.Of[p.Id] == mine)
                    continue;
                any = true;
                ImGui.PushID(p.Id + 1000);
                ImGuiSafe.TextWrapped($"{p.Name}{(t.Of[p.Id] != 0 ? $" ({t.Names[t.Of[p.Id]]})" : "")}");
                ImGui.SameLine();
                if (t.Invited[p.Id] == mine)
                    ImGuiSafe.TextColored(Dim, "invited");
                else if (ImGui.SmallButton("Invite"u8))
                    game.SandboxFaction(FactionOp.Invite, p.Id);
                ImGui.PopID();
            }
            if (!any)
                ImGuiSafe.TextColored(Dim, "Nobody else is here to invite.");
        }

        // ---- Give
        ImGui.Separator();
        ImGuiSafe.TextColored(Orange, "Give a construct you own");
        if (_faMates.Count == 0)
        {
            ImGuiSafe.TextColored(Dim, "There is nobody else in your faction to give to yet.");
            return;
        }
        _faMine.Clear();
        foreach (var c in game.Constructs.Bodies)
            if (c.Owner == me.Id && !c.Wreck && !c.Loose)
                _faMine.Add((c, Vector3.Distance(me.Position, c.WorldCom)));
        _faMine.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        if (_faMine.Count == 0)
            ImGuiSafe.TextColored(Dim, "You don't own a construct.");
        for (var i = 0; i < _faMine.Count && i < FactionGiveRows; i++)
        {
            var (c, dist) = _faMine[i];
            ImGui.PushID(c.Id + 2000);
            ImGuiSafe.TextWrapped($"Construct {c.Id}: {c.Parts[0].D.Name}, {c.PartCount} part{(c.PartCount == 1 ? "" : "s")}, {dist:0} m away");
            foreach (var k in _faMates)
            {
                ImGui.SameLine();
                ImGui.PushID(k);
                if (ImGui.SmallButton($"Give to {game.FactionMemberName(k)}"))
                    game.SandboxFaction(FactionOp.GiveBody, c.Id, k);
                ImGui.PopID();
            }
            ImGui.PopID();
        }
        if (_faMine.Count > FactionGiveRows)
            ImGuiSafe.TextColored(Dim, $"...and {_faMine.Count - FactionGiveRows} more further away.");
    }

    /// <summary>The factions there are (when you're in none).</summary>
    private static void DrawFactionList(Game game, FactionTable t)
    {
        var any = false;
        for (var id = 1; id < FactionTable.Max; id++)
        {
            if (t.Names[id].Length == 0)
                continue;
            if (!any)
            {
                ImGui.Separator();
                ImGuiSafe.TextColored(Orange, "Factions here (their leader can invite you)");
                any = true;
            }
            ImGuiSafe.TextWrapped($"{t.Names[id]}: {t.Count((byte)id)} member{(t.Count((byte)id) == 1 ? "" : "s")}, led by {(t.Leader[id] == 255 ? "nobody" : game.FactionMemberName(t.Leader[id]))}");
        }
    }
}
