using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Faction window (F2 in Sandbox by default; docs/sandbox/CONSTRUCTS.md, "Factions"): found a faction or accept an
/// invite (or join an open one, or ask), pick its colour and tag, set ranks, stand allied or at war with other factions,
/// see how the story's factions see you, leave, and give a construct you own or the part in
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

    private ImInputString? _faTag;
    private float3 _faColour;
    private bool _faOpen;
    private int _faLooksFor = -1; // the faction (and table version) the looks fields were filled from
    private int _faLooksVersion = -1;

    private static float4 FactionColour(FactionTable t, int id)
    {
        var c = t.Colour[id];
        return new float4((c >> 16 & 255) / 255f, (c >> 8 & 255) / 255f, (c & 255) / 255f, 1f);
    }

    /// <summary>A standing (-100 to 100) in a word.</summary>
    private static string StandingWord(int v) => v switch
    {
        <= -60 => "hunts you",
        <= -20 => "hostile",
        < 0 => "wary",
        0 => "doesn't know you",
        < 20 => "neutral",
        < 60 => "friendly",
        _ => "trusts you",
    };

    private void DrawFactionBody(Game game, Player me)
    {
        var t = game.FactionTable;
        var ranks = FactionRanks.Player;
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
            ImGuiSafe.TextWrapped("You are not in a faction. Found one and invite people: everything each member owns (vehicles, bases, beacons and their stores) is shared with the others, and you can give things to each other. The other factions out there, the IT Department and the rest, remember what your faction does.");
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
            DrawFactionList(game, t, me);
            return;
        }
        var myRank = t.RankOf(me.Id);
        var leads = t.Leader[mine] == me.Id;
        ImGuiSafe.TextColored(FactionColour(t, mine), $"[{t.Tag[mine]}] {t.Names[mine]}");
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"you are {ranks.RankName(myRank)}{(t.Open[mine] ? ", open to anyone" : ", invite only")}");
        if (t.Notorious[mine])
            ImGuiSafe.TextColored(new float4(1f, 0.3f, 0.25f, 1f), "NOTORIOUS: every faction hunts you.");
        ImGuiSafe.TextColored(Dim, "What each member owns is shared: drive, build on, configure and use it as your own. Taking a mate's things apart needs a rank.");

        // ---- Looks
        if (t.May(me.Id, FactionMay.Looks) && ImGui.CollapsingHeader("Colour, tag and joining##falooks"u8))
        {
            _faTag ??= new ImInputString(FactionTable.TagMax + 1);
            if (_faLooksFor != mine || _faLooksVersion < 0)
            {
                var c = FactionColour(t, mine);
                (_faColour, _faOpen, _faLooksFor, _faLooksVersion) = (new float3(c.X, c.Y, c.Z), t.Open[mine], mine, t.Version);
                ImGuiSafe.SetInput(_faTag, t.Tag[mine]);
            }
            ImGui.ColorEdit3("Colour##facol"u8, ref _faColour, ImGuiColorEditFlags.NoInputs);
            ImGui.SetNextItemWidth(90 * _scale);
            try
            {
                ImGui.InputTextWithHint("Tag##fatag"u8, "TAG"u8, _faTag);
            }
            catch (Exception ex)
            {
                ImGuiSafe.FieldFailed("faction tag", ex);
            }
            ImGui.Checkbox("Anyone may join (no invite needed)##faopen"u8, ref _faOpen);
            if (ImGui.Button("Apply##falooksgo"u8))
            {
                static uint B(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
                game.SandboxFaction(FactionOp.Looks, (int)(B(_faColour.X) << 16 | B(_faColour.Y) << 8 | B(_faColour.Z)), _faOpen ? 1 : 0, ImGuiSafe.Text(_faTag));
                _faLooksVersion = -1;
            }
        }
        else
            _faLooksVersion = -1;

        // ---- Members
        ImGui.Separator();
        ImGuiSafe.TextColored(Orange, "Members");
        ImGuiSafe.TextWrapped($"You ({me.Name}) - {ranks.RankName(myRank)}");
        foreach (var k in _faMates)
        {
            var here = game.Players.TryGetValue(k, out var p) && k < Game.MaxPlayers;
            var theirs = t.RankOf(k);
            ImGui.PushID(k);
            ImGuiSafe.TextWrapped($"{game.FactionMemberName(k)} - {ranks.RankName(theirs)}{(here ? "" : " (away)")}");
            if (theirs < myRank && t.May(me.Id, FactionMay.Promote))
            {
                if (theirs + 1 < myRank && theirs + 1 < ranks.Top)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Promote"u8))
                        game.SandboxFaction(FactionOp.SetRank, k, theirs + 1);
                }
                if (theirs > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Demote"u8))
                        game.SandboxFaction(FactionOp.SetRank, k, theirs - 1);
                }
            }
            if (theirs < myRank && t.May(me.Id, FactionMay.Remove))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove"u8))
                    game.SandboxFaction(FactionOp.Remove, k);
            }
            if (leads)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Make leader"u8))
                    game.SandboxFaction(FactionOp.MakeLeader, k);
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

        // ---- Invite, and those asking
        if (t.May(me.Id, FactionMay.Invite))
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
                if (t.Asked[p.Id] == mine)
                {
                    ImGuiSafe.TextColored(Orange, "asks to join");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Let in"u8))
                        game.SandboxFaction(FactionOp.Answer, p.Id, 1);
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Turn away"u8))
                        game.SandboxFaction(FactionOp.Answer, p.Id, 0);
                }
                else if (t.Invited[p.Id] == mine)
                    ImGuiSafe.TextColored(Dim, "invited");
                else if (ImGui.SmallButton("Invite"u8))
                    game.SandboxFaction(FactionOp.Invite, p.Id);
                ImGui.PopID();
            }
            if (!any)
                ImGuiSafe.TextColored(Dim, "Nobody else is here to invite.");
        }

        // ---- Other player factions
        DrawFactionRelations(game, t, me, mine);

        // ---- How the story's factions see you
        if (t.StoryNames.Length > 0)
        {
            ImGui.Separator();
            ImGuiSafe.TextColored(Orange, "How the world's factions see you");
            for (var f = 0; f < t.StoryNames.Length; f++)
            {
                int v = t.StoryStanding[mine * FactionTable.MaxStory + f];
                ImGuiSafe.TextWrapped($"{t.StoryNames[f]}: {StandingWord(v)} ({v:+0;-0;0})");
            }
        }

        // ---- Give
        ImGui.Separator();
        ImGuiSafe.TextColored(Orange, "Give a construct you own");
        if (_faMates.Count == 0)
        {
            ImGuiSafe.TextColored(Dim, "There is nobody else in your faction to give to yet.");
            return;
        }
        if (!t.May(me.Id, FactionMay.Give))
        {
            ImGuiSafe.TextColored(Dim, $"Your rank ({ranks.RankName(myRank)}) can't give constructs away.");
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

    /// <summary>The other player factions: how yours stands with each, and (with the rank for it) alliance, neutral or war.</summary>
    private static void DrawFactionRelations(Game game, FactionTable t, Player me, byte mine)
    {
        var speaks = t.May(me.Id, FactionMay.Relations);
        var any = false;
        for (var id = 1; id < FactionTable.Max; id++)
        {
            if (id == mine || t.Names[id].Length == 0)
                continue;
            if (!any)
            {
                ImGui.Separator();
                ImGuiSafe.TextColored(Orange, "Other factions");
                any = true;
            }
            var rel = t.Relation(mine, (byte)id);
            int ours = t.Stance[mine * FactionTable.Max + id], theirs = t.Stance[id * FactionTable.Max + mine];
            var what = rel switch
            {
                FactionTable.Relations.Allied => "allies",
                FactionTable.Relations.War => ours < 0 ? "at war (you declared it)" : "at war (they declared it)",
                _ => theirs > 0 ? "neutral, they offer an alliance" : ours > 0 ? "neutral, you offered an alliance" : "neutral",
            };
            ImGui.PushID(id + 3000);
            ImGuiSafe.TextColored(FactionColour(t, id), $"[{t.Tag[id]}] {t.Names[id]}");
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, $"{t.Count((byte)id)} member{(t.Count((byte)id) == 1 ? "" : "s")}, {what}{(t.Notorious[id] ? ", NOTORIOUS" : "")}");
            if (speaks)
            {
                if (ours <= 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton(theirs > 0 ? "Accept alliance"u8 : "Offer alliance"u8))
                        game.SandboxFaction(FactionOp.Stance, id, 1);
                }
                if (ours != 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton(ours < 0 ? "Make peace"u8 : "Withdraw offer"u8))
                        game.SandboxFaction(FactionOp.Stance, id, 0);
                }
                if (ours >= 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Declare war"u8))
                        game.SandboxFaction(FactionOp.Stance, id, -1);
                }
            }
            ImGui.PopID();
        }
    }

    /// <summary>The factions there are (when you're in none): join an open one, or ask to join.</summary>
    private static void DrawFactionList(Game game, FactionTable t, Player me)
    {
        var any = false;
        for (var id = 1; id < FactionTable.Max; id++)
        {
            if (t.Names[id].Length == 0)
                continue;
            if (!any)
            {
                ImGui.Separator();
                ImGuiSafe.TextColored(Orange, "Factions here");
                any = true;
            }
            ImGui.PushID(id + 4000);
            ImGuiSafe.TextColored(FactionColour(t, id), $"[{t.Tag[id]}] {t.Names[id]}");
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, $"{t.Count((byte)id)} member{(t.Count((byte)id) == 1 ? "" : "s")}, led by {(t.Leader[id] == 255 ? "nobody" : game.FactionMemberName(t.Leader[id]))}{(t.Notorious[id] ? ", NOTORIOUS" : "")}");
            ImGui.SameLine();
            if (t.Open[id])
            {
                if (ImGui.SmallButton("Join"u8))
                    game.SandboxFaction(FactionOp.Join, id);
            }
            else if (t.Asked[me.Id] == id)
                ImGuiSafe.TextColored(Dim, "asked");
            else if (ImGui.SmallButton("Ask to join"u8))
                game.SandboxFaction(FactionOp.Join, id);
            ImGui.PopID();
        }
    }
}
