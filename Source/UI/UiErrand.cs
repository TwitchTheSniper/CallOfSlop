using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// An errand on the HUD (GameErrands.cs, docs/story/DUTIES.md "Errands"): who asked and the step, what to do with the player's
/// own keys, a hint once the step has been up a while, how far the marker is, top left where the duties' panels are (it waits
/// while the induction, the quarry, the break-out or the depot's steps hold that place); an orange marker; the Esc menu's
/// drop. Text is filled once per step (and again when a key it names is rebound), the distance once per metre.
/// </summary>
public sealed partial class Ui
{
    private int _erFilled = -1, _erFilledStamp, _erMetres = -1;
    private string _erHead = "", _erTitle = "", _erText = "", _erHint = "", _erFar = "";

    /// <summary>Does another story panel hold the top left now? (The errand's waits; its steps still count.)</summary>
    private static bool ErrandMakesWay(Game game) =>
        game.QuarryRiding || game.TutorialCurrent != null
        || game.QuarryLocal is not (Game.QuarryNone or Game.QuarryDone)
        || BreakoutPanelUp(game)
        || game.DepotLocal is >= Game.DepotFirstStep and < Game.DepotDone
        || game.DepotLocal == Game.DepotDone && Brutal.GlfwApi.Glfw.Time - game.DepotChangedAt <= 8;

    /// <summary>The break-out's panel is up (as UiBreakout.cs shows it: the muster, then a minute at the spaceport, half a minute away).</summary>
    private static bool BreakoutPanelUp(Game game)
    {
        var since = Brutal.GlfwApi.Glfw.Time - game.BreakoutChangedAt;
        return game.BreakoutLocal switch
        {
            Game.BreakoutNone => false,
            Game.BreakoutSpaceport => since <= 60,
            Game.BreakoutAway => since <= 30,
            _ => true,
        };
    }

    /// <summary>An errand is on screen (the depot's report and the "what next" goal wait for it).</summary>
    public static bool ErrandShowing(Game game) =>
        game.ErrandLocal != 0 && (game.ErrandStepLocal != Game.ErrandFinished || Brutal.GlfwApi.Glfw.Time - game.ErrandChangedAt <= Game.ErrandDoneShows) && !ErrandMakesWay(game);

    private void DrawErrand(Game game, float2 pos, float2 size, Settings settings)
    {
        if (!game.SandboxOn || PauseOpen || MenuOpen || !ErrandShowing(game) || game.ErrandNow is not { } e)
            return;
        var since = Brutal.GlfwApi.Glfw.Time - game.ErrandChangedAt;
        var done = game.ErrandStepLocal == Game.ErrandFinished;
        var step = game.ErrandStepNow;
        var key = game.ErrandLocal << 8 | game.ErrandStepLocal;
        var stamp = settings.Bind(GameAction.Interact) ^ settings.Bind(GameAction.Fire) << 8 ^ settings.Bind(GameAction.Streak2) << 16 ^ settings.Bind(GameAction.ToolWheel) << 4;
        if (key != _erFilled || stamp != _erFilledStamp)
        {
            _erFilled = key;
            _erFilledStamp = stamp;
            _erMetres = -1;
            _erHead = done ? $"{e.Giver.ToUpperInvariant()}'S ERRAND" : $"{e.Giver.ToUpperInvariant()}'S ERRAND  {game.ErrandStepLocal + 1} / {e.Steps.Length}";
            (_erTitle, _erText, _erHint) = done ? (e.Title, "Done. You know your way round the " + e.Machine + " now.", "")
                : step != null ? (step.Title, Tutorial.Fill(step.Text, settings), step.Hint.Length > 0 ? Tutorial.Fill(step.Hint, settings) : "") : ("", "", "");
        }
        if (_erTitle.Length == 0)
            return;
        ImGui.SetNextWindowPos(new float2(pos.X + 24 * _scale, pos.Y + size.Y * 0.2f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(400 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.55f);
        if (ImGui.Begin("##errand"u8, Overlay & ~ImGuiWindowFlags.NoBackground))
        {
            if (since < 1.5 && game.ErrandStepLocal > 0 && !done)
                ImGuiSafe.TextColored(Green, "Done.");
            ImGuiSafe.TextColored(Dim, _erHead);
            ImGuiSafe.TextColored(done ? Green : Orange, _erTitle);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 380 * _scale);
            ImGuiSafe.TextWrapped(_erText);
            if (_erHint.Length > 0 && step != null && since >= step.HintAfter)
            {
                ImGui.Spacing();
                ImGuiSafe.TextColored(Orange, _erHint);
            }
            ImGui.PopTextWrapPos();
            if (!done && game.ErrandHasTarget && game.Local is { } me)
            {
                var metres = (int)System.Numerics.Vector3.Distance(me.Position, game.ErrandTarget);
                if (metres != _erMetres)
                {
                    _erMetres = metres;
                    _erFar = metres < 3 ? "It's right here (the orange marker)." : $"The orange marker: {metres} m away.";
                }
                ImGuiSafe.TextColored(Dim, _erFar);
            }
            if (!done)
                ImGuiSafe.TextColored(Dim, "Esc menu: drop this errand");
        }
        ImGui.End();
        if (!done && game.ErrandHasTarget)
            TutorialMarker(game.ErrandTarget);
    }

    /// <summary>The Esc menu's errand button (Sandbox): drop the errand under way.</summary>
    private void DrawPauseErrand(Game game)
    {
        if (game.ErrandLocal == 0 || game.ErrandStepLocal == Game.ErrandFinished)
            return;
        ImGui.Spacing();
        if (ImGui.Button("Drop this errand##errand"u8))
            game.ErrandAskDrop();
    }
}
