using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The warden's induction on the HUD (GameTutorial.cs, docs/sandbox/TUTORIAL.md): the step's title and what to do, with the
/// player's own keys, and the warden's aside, top left; a short "Noted" when a step is done; and on the Esc menu, skip,
/// resume or replay. The step's text is filled once per step (and again if a key it names is rebound): nothing per frame.
/// </summary>
public sealed partial class Ui
{
    private int _tutFilledStep = -1, _tutFilledStamp;
    private string _tutTitle = "", _tutText = "", _tutWarden = "";

    private static readonly float4 Warden = new(0.78f, 0.82f, 0.62f, 1f);

    private void DrawTutorial(Game game, float2 pos, float2 size, Settings settings)
    {
        if (!game.SandboxOn || PauseOpen || MenuOpen)
            return;
        var since = Brutal.GlfwApi.Glfw.Time - game.TutorialChangedAt;
        var step = game.TutorialCurrent;
        var finished = game.TutorialStateNow == TutorialState.Finished && since < 4;
        if (step == null && !finished)
            return;
        ImGui.SetNextWindowPos(new float2(pos.X + 24 * _scale, pos.Y + size.Y * 0.2f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(400 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.55f);
        if (ImGui.Begin("##tutorial"u8, Overlay & ~ImGuiWindowFlags.NoBackground))
        {
            if (step == null)
                ImGuiSafe.TextColored(Green, "Induction complete. Esc replays it.");
            else
            {
                TutorialFill(game.TutorialStepNow, step, settings);
                if (since < 1.5 && game.TutorialStepNow > 0)
                    ImGuiSafe.TextColored(Green, "Noted.");
                var others = game.TutorialGroupOthers;
                if (others > 0)
                    ImGuiSafe.TextColored(Dim, $"INDUCTION  {game.TutorialStepNow + 1} / {Tutorial.Steps.Length}   with {others} other{(others == 1 ? "" : "s")}");
                else
                    ImGuiSafe.TextColored(Dim, $"INDUCTION  {game.TutorialStepNow + 1} / {Tutorial.Steps.Length}");
                ImGuiSafe.TextColored(Orange, _tutTitle);
                if (others > 0 && game.TutorialGroupDone)
                    ImGuiSafe.TextColored(Green, game.TutorialGroupWaiting.Length > 0 ? $"Done. Waiting for {game.TutorialGroupWaiting}." : "Done. The warden waits for the rest.");
                else if (others > 0 && game.TutorialGroupOthersDone > 0)
                    ImGuiSafe.TextColored(Orange, $"{game.TutorialGroupOthersDone} of {others} done: the warden is waiting for you.");
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 380 * _scale);
                ImGuiSafe.TextWrapped(_tutText);
                if (_tutWarden.Length > 0)
                {
                    ImGui.Spacing();
                    ImGuiSafe.TextColored(Warden, _tutWarden);
                }
                ImGui.PopTextWrapPos();
                ImGuiSafe.TextColored(Dim, "Esc menu: skip the induction");
            }
        }
        ImGui.End();
    }

    private void TutorialFill(int index, TutorialStep step, Settings settings)
    {
        var stamp = Tutorial.BindsStamp(step, settings);
        if (index == _tutFilledStep && stamp == _tutFilledStamp)
            return;
        _tutFilledStep = index;
        _tutFilledStamp = stamp;
        _tutTitle = step.Title;
        _tutText = Tutorial.Fill(step.Text, settings);
        _tutWarden = step.Warden.Length == 0 ? "" : Tutorial.Speaker.Length > 0 ? $"{Tutorial.Speaker}: \"{step.Warden}\"" : $"\"{step.Warden}\"";
    }

    /// <summary>The Esc menu's induction buttons (Sandbox): skip while it runs, resume a skipped one, replay from the start.</summary>
    private void DrawPauseTutorial(Game game)
    {
        if (Tutorial.Steps.Length == 0)
            return;
        ImGui.Spacing();
        switch (game.TutorialStateNow)
        {
            case TutorialState.Running:
                if (ImGui.Button("Skip the induction##tut"u8))
                    game.TutorialAsk(TutorialOp.Skip);
                break;
            case TutorialState.Skipped:
                if (ImGui.Button("Resume the induction##tut"u8))
                    game.TutorialAsk(TutorialOp.Resume);
                ImGui.SameLine();
                if (ImGui.Button("Start it again##tut"u8))
                    game.TutorialAsk(TutorialOp.Replay);
                break;
            default:
                if (ImGui.Button("Replay the induction##tut"u8))
                    game.TutorialAsk(TutorialOp.Replay);
                break;
        }
    }
}
