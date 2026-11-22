using System.Numerics;
using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>
/// The Faction window's politics (docs/sandbox/design/faction-controllers.md phase 5): a faction's pacts with the world's powers (a leader proposes,
/// takes an offer or breaks one; the server decides: GameCrewPolitics.cs), the call for allies' help, and the powers' contracts (paid on a tab: GameTabs.cs) anyone may
/// take. Drawn only while the window is open.
/// </summary>
public sealed partial class Ui
{
    private static readonly PactKind[] PartyKinds = [PactKind.Ceasefire, PactKind.Tribute, PactKind.NonAggression, PactKind.Trade, PactKind.Alliance];

    private static string PactButton(PactKind k) => k switch
    {
        PactKind.Ceasefire => "Ceasefire",
        PactKind.Tribute => "Pay tribute",
        PactKind.NonAggression => "Non-aggression",
        PactKind.Trade => "Trade",
        PactKind.Alliance => "Alliance",
        _ => "Pact",
    };

    /// <summary>Faction <paramref name="mine"/>'s pacts with each power, offers, proposals waiting, and (leader) the buttons; any member's call for help.</summary>
    private void DrawPartyPacts(Game game, FactionTable t, Player me, byte mine)
    {
        var n = Math.Min(t.PowerCount, t.StoryNames.Length);
        if (n == 0)
            return;
        PowerColumns(t, n);
        var leader = t.Leader[mine] == me.Id;
        var rules = RelationRules.Current;
        ImGui.Separator();
        ImGuiSafe.TextColored(Orange, "Pacts with the world's powers");
        ImGuiSafe.TextColored(Dim, leader
            ? "Propose a pact and the power answers soon by its own lights. A pact holds both sides' fire; attacking a partner, or breaking a pact here, is news that every power that hears thinks less of you for."
            : "Your leader makes and breaks pacts. Attacking a partner breaks the pact.");
        for (var s = 0; s < _powShownCount; s++)
        {
            var f = _powShown[s];
            if ((t.PowerGone >> f & 1) != 0)
                continue;
            ImGui.PushID(f + 7000);
            var name = t.StoryNames[f];
            int v = t.StoryStanding[mine * FactionTable.MaxStory + f];
            var pact = t.PartyPactOf(f, mine);
            if (t.PartyFeud[mine * FactionTable.MaxStory + f])
                ImGuiSafe.TextColored(Red, $"{name}: BLOOD FEUD - they will never make peace with you");
            else
            {
                var line = pact == 0 ? $"{name}: no pact ({StandingWord(v)}, {v:+0;-0;0})" : $"{name}: {PactWord(pact)}{PartyPactLeft(t, f, mine)} ({StandingWord(v)}, {v:+0;-0;0})";
                ImGuiSafe.TextColored(pact == (byte)PactKind.Alliance ? Green : BandColour(v), line);
                for (var k = 0; k < t.AskCount; k++)
                    if (t.AskPower[k] == f && t.AskParty[k] == mine)
                    {
                        ImGui.SameLine();
                        ImGuiSafe.TextColored(Dim, $"(your {PactWord(t.AskKind[k]).ToLowerInvariant()} waits for an answer)");
                    }
                var id = (byte)(CrewPowers.Find(name) is var pid and > 0 ? pid : f + 1);
                if (leader)
                {
                    foreach (var kind in PartyKinds)
                    {
                        var plausible = kind switch
                        {
                            PactKind.Ceasefire or PactKind.Tribute => v < 0 && pact is 0 or (byte)PactKind.Ceasefire && (kind != PactKind.Ceasefire || pact == 0),
                            PactKind.NonAggression => v >= rules.NeutralLine && pact is 0 or (byte)PactKind.Ceasefire,
                            PactKind.Trade => v >= rules.NeutralLine && pact is 0 or (byte)PactKind.Ceasefire or (byte)PactKind.NonAggression,
                            _ => v >= rules.FriendlyLine && pact != (byte)PactKind.Alliance,
                        };
                        if (!plausible)
                            continue;
                        ImGui.SameLine();
                        if (ImGui.SmallButton(PactButton(kind)))
                            game.SandboxFaction(FactionOp.Pact, id, (int)kind);
                    }
                    if (pact != 0)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Break pact"u8))
                            game.SandboxFaction(FactionOp.PactBreak, id);
                        if (ImGui.IsItemHovered())
                            ImGuiSafe.SetTooltip($"{name} takes it hard, and every power that hears of it thinks less of {t.Names[mine]}.");
                    }
                }
                for (var k = 0; k < t.OfferCount; k++)
                    if (t.OfferPower[k] == f && t.OfferParty[k] == mine)
                    {
                        ImGuiSafe.TextColored(Green, $"  {name} offers you a {PactWord(t.OfferKind[k]).ToLowerInvariant()}.");
                        if (leader)
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton("Accept"u8))
                                game.SandboxFaction(FactionOp.PactAnswer, id, 1);
                            ImGui.SameLine();
                            if (ImGui.SmallButton("Decline"u8))
                                game.SandboxFaction(FactionOp.PactAnswer, id, 0);
                        }
                    }
            }
            ImGui.PopID();
        }
        if (ImGui.Button("Call allies for help##facall"u8))
            game.SandboxFaction(FactionOp.CallAllies);
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetTooltip($"Allied powers (and friendly ones fighting the same foe) send warships to where you are. Once every {rules.CallCooldownMinutes} min for the faction.");
    }

    private static string PartyPactLeft(FactionTable t, int f, byte mine)
    {
        for (var k = 0; k < t.PartyPactCount; k++)
            if (t.PartyPactPower[k] == f && t.PartyPactParty[k] == mine)
                return t.PartyPactLeft[k] == 0xFFFF ? $" ({t.PartyPactHeld[k]} min)" : $" ({t.PartyPactLeft[k]} min left)";
        return "";
    }

    /// <summary>The powers' contracts: offers anyone may take (the power decides whom it trusts), and those held, with how far along.</summary>
    private void DrawContracts(Game game, Player me)
    {
        if (game.ContractsCount == 0)
            return;
        ImGui.Separator();
        ImGuiSafe.TextColored(Orange, "Contracts (paid on your tab with the power)");
        ImGuiSafe.TextColored(Dim, "A power gives work to those it trusts (standing 0 or more, or a pact). Done, it pays and thinks better of you; failed, it thinks less.");
        for (var i = 0; i < game.ContractsCount; i++)
        {
            ref readonly var c = ref game.ContractsView[i];
            ImGui.PushID(i + 7500);
            var what = PowerContracts.Describe(c);
            switch (c.State)
            {
                case ContractState.Offered:
                    ImGuiSafe.TextWrapped($"{what}. Pay {c.Pay} on your tab ({c.Until / 60 + 1} min to take it).");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Take"u8))
                        game.SandboxFaction(FactionOp.ContractTake, c.Body, c.Serial);
                    break;
                case ContractState.Taken when c.Taker == me.Id:
                {
                    var dist = Vector3.Distance(me.Position, c.At);
                    var how = c.Kind switch
                    {
                        ContractKind.Escort => $"{c.Progress / 60}:{c.Progress % 60:00} of {c.Need / 60}:{c.Need % 60:00} near it; it is {dist:0} m away",
                        ContractKind.Deliver when c.Target > 0 => $"the station is {dist:0} m away; dock there with the load aboard",
                        ContractKind.Deliver => $"the place is {dist:0} m away; bring the load in a ship within {RelationRules.Current.DeliverRange:0} m",
                        _ => "destroy or take one of their ships",
                    };
                    ImGuiSafe.TextColored(Green, $"Yours: {what} ({how}; {c.Until / 60 + 1} min left, pay {c.Pay}).");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Give up"u8))
                        game.SandboxFaction(FactionOp.ContractDrop, c.Body, c.Serial);
                    break;
                }
                case ContractState.Taken:
                    ImGuiSafe.TextColored(Dim, $"{what}: taken by {(game.Players.TryGetValue(c.Taker, out var tp) ? tp.Name : "someone away")}.");
                    break;
                case ContractState.Done:
                    ImGuiSafe.TextColored(Dim, $"Done: {what}.");
                    break;
                case ContractState.Failed:
                    ImGuiSafe.TextColored(Dim, $"Failed: {what}.");
                    break;
            }
            ImGui.PopID();
        }
    }
}
