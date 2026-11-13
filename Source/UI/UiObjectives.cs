using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "what next" line on the HUD (GameObjectives.cs, docs/story/OBJECTIVES.md): where the induction's panel sits, top
/// left, once no story panel is up. The goal's title and one line; its memo for the first seconds, its hint for a while
/// after <see cref="ObjectiveScript.HintAfter"/>, then just the two lines. In the story text's size and colour (Settings > Video).
/// The words are made once per change (a new goal, new numbers, a rebound key): nothing per frame.
/// </summary>
public sealed partial class Ui
{
    private int _objFilled = -1, _objFilledHave, _objFilledNeed, _objFilledStamp;
    private string _objTitle = "", _objText = "", _objMemo = "", _objHint = "", _objMetLine = "";

    /// <summary>How long the memo shows, and the hint once it's up.</summary>
    private const double ObjectiveMemoFor = 20, ObjectiveHintFor = 60, ObjectiveMetFor = 5;

    private void DrawObjective(Game game, float2 pos, float2 size, Settings settings)
    {
        if (!game.SandboxOn || PauseOpen || MenuOpen || game.QuarryRiding || game.ObjectiveNow is not { } goal)
            return;
        // Only where no story panel is up (the server holds the goal back while one runs; these cover their closing seconds).
        var t = Brutal.GlfwApi.Glfw.Time;
        if (game.TutorialCurrent != null || game.TutorialStateNow == TutorialState.Finished && t - game.TutorialChangedAt < 4)
            return;
        if (game.QuarryLocal != Game.QuarryNone && (game.QuarryLocal != Game.QuarryDone || t - game.QuarryChangedAt <= 6))
            return;
        if (game.DepotLocal != Game.DepotNone && (game.DepotLocal != Game.DepotDone || t - game.DepotChangedAt <= 8))
            return;
        var stamp = settings.Bind(GameAction.FactionWindow) ^ settings.Bind(GameAction.Interact) << 8 ^ settings.Bind(GameAction.Logbook) << 16 ^ settings.Bind(GameAction.StoryWindow) << 4; // (the keys the goals name)
        if (_objFilled != game.ObjectiveLocal || _objFilledHave != game.ObjectiveHave || _objFilledNeed != game.ObjectiveNeed || _objFilledStamp != stamp)
        {
            if (_objFilled != game.ObjectiveLocal)
                _objMetLine = game.ObjectiveMet < ObjectiveScript.Current.Goals.Length ? $"Filed: {ObjectiveScript.Current.Goals[game.ObjectiveMet].Title}." : "";
            (_objFilled, _objFilledHave, _objFilledNeed, _objFilledStamp) = (game.ObjectiveLocal, game.ObjectiveHave, game.ObjectiveNeed, stamp);
            _objTitle = goal.Title;
            _objText = ObjectiveScript.Fill(goal.Text, game.ObjectiveHave, game.ObjectiveNeed, settings);
            var voice = ObjectiveScript.Current.Voice;
            _objMemo = goal.Memo.Length == 0 ? "" : voice.Length > 0 ? $"{voice}: {goal.Memo}" : goal.Memo;
            _objHint = goal.Hint.Length == 0 ? "" : ObjectiveScript.Fill(goal.Hint, game.ObjectiveHave, game.ObjectiveNeed, settings);
        }
        var since = t - game.ObjectiveChangedAt;
        var hintAfter = ObjectiveScript.Current.HintAfter;
        var px = ImGui.GetStyle().FontSizeBase * (_subSize / (SubtitleSizeDefault / 100f)); // (the story text setting; its default: the HUD's own size)
        var wide = 360 * _scale * MathF.Max(1f, _subSize / (SubtitleSizeDefault / 100f));
        ImGui.SetNextWindowPos(new float2(pos.X + 24 * _scale, pos.Y + size.Y * 0.2f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(wide, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.35f);
        if (ImGui.Begin("##objective"u8, Overlay & ~ImGuiWindowFlags.NoBackground))
        {
            ImGui.PushFont(default, px);
            if (_objMetLine.Length > 0 && since < ObjectiveMetFor)
                ImGuiSafe.TextColored(Green, _objMetLine);
            ImGui.TextColored(Dim, "NEXT"u8);
            ImGuiSafe.TextColored(Orange, _objTitle);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wide - 20 * _scale);
            var tint = SubtitleTint;
            ImGuiSafe.TextColored(new float4(tint.X, tint.Y, tint.Z, 1f), _objText);
            if (_objMemo.Length > 0 && since < ObjectiveMemoFor)
            {
                ImGui.Spacing();
                ImGuiSafe.TextColored(Warden, _objMemo);
            }
            if (_objHint.Length > 0 && since >= hintAfter && since < hintAfter + ObjectiveHintFor)
            {
                ImGui.Spacing();
                ImGuiSafe.TextColored(Dim, _objHint);
            }
            ImGui.PopTextWrapPos();
            ImGui.PopFont();
        }
        ImGui.End();
    }
}
