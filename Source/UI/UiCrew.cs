using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>
/// The Crew section at the top of the Faction window (docs/sandbox/design/bot-brain.md section 9, phase 6): your credits, each of your ships'
/// hired crew (job, what they are doing, health) with Dismiss and Hold fire / Fire at will, and the prisoners on ships you took, with Release,
/// Put off and Hire. Everything is the host's (Msg.CrewRoster, Msg.Credits); each button sends a crew order (CActKind.Crew).
/// </summary>
public sealed partial class Ui
{
    private void DrawCrewSection(Game game, Player me)
    {
        ImGuiSafe.TextColored(Orange, "Crew");
        ImGuiSafe.TextWrapped(game.CreditsLine.Length > 0 ? game.CreditsLine : "Credits: 0");
        var any = false;
        foreach (var (body, v) in game.CrewRosters)
        {
            if (v.Owner != me.Id && !Factions.Shares(v.Owner, me.Id))
                continue;
            any = true;
            ImGui.PushID(body);
            ImGuiSafe.TextColored(Dim, $"Construct {body}: {(v.Prize ? "taken" : "your crew")}, {v.Members.Count} aboard{(v.HoldFire ? ", holding fire" : "")}");
            if (v.Own)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(v.HoldFire ? "Fire at will"u8 : "Hold fire"u8))
                    game.SandboxSendCrewOrder(body, 0, v.HoldFire ? CrewOrder.FireAtWill : CrewOrder.HoldFire);
            }
            foreach (var (uid, name, job, state, hp, fac, officer) in v.Members)
            {
                ImGui.PushID((int)uid);
                var kind = CrewPowers.Of(fac)?.Name ?? "";
                var doing = state switch
                {
                    CrewState.Surrendered => "prisoner",
                    CrewState.Walking => "on the way",
                    CrewState.Fighting => "fighting",
                    CrewState.Free => "waiting",
                    _ => "at work",
                };
                ImGuiSafe.TextWrapped($"  {name} ({kind}): {job.ToString().ToLowerInvariant()}, {doing}, {hp * 100 / 255}%");
                if (state == CrewState.Surrendered)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Release"u8))
                        game.SandboxSendCrewOrder(body, uid, CrewOrder.Release);
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Put off"u8))
                        game.SandboxSendCrewOrder(body, uid, CrewOrder.Drop);
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Hire"u8))
                        game.SandboxSendCrewOrder(body, uid, CrewOrder.Hire);
                    if (officer)
                    {
                        // (an admiral or a captain taken prisoner: the host marks them, GameCrewFleet.cs)
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Question"u8))
                            game.SandboxSendCrewOrder(body, uid, CrewOrder.Interrogate);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Ransom"u8))
                            game.SandboxSendCrewOrder(body, uid, CrewOrder.Ransom);
                    }
                }
                else if (v.Own)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Dismiss"u8))
                        game.SandboxSendCrewOrder(body, uid, CrewOrder.Dismiss);
                    ImGui.SameLine();
                    if (ImGui.SmallButton(job == CrewJob.Interrogator ? "Off questioning"u8 : "Interrogator"u8))
                        game.SandboxSendCrewOrder(body, uid, CrewOrder.Interrogator);
                }
                ImGui.PopID();
            }
            ImGui.PopID();
        }
        if (!any)
            ImGuiSafe.TextColored(Dim, "No crew yet: take a ship and hire its crew, or hire a hand from a friendly crew nearby.");
        if (ImGui.SmallButton("Hire a hand nearby"u8))
            game.SandboxSendCrewOrder(0, 0, CrewOrder.Hire);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"(a friendly crew within {Game.HireReach:0} m; they join the ship you are aboard)");
        ImGui.Separator();
    }
}
