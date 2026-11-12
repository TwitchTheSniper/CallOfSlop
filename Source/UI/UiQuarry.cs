using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The first Ministry duty on the HUD (GameQuarry.cs, docs/story/DUTIES.md): the step's title and what to do with the player's
/// own keys, a hint once the step has been up a while, how far the step's place is, top left where the induction's panel was;
/// an orange marker over the place; the words over the screen while the crew bus carries them; and the Esc menu's skip. Text
/// is filled once per state (and again if a key it names is rebound) and the distance once per metre: nothing per frame.
/// </summary>
public sealed partial class Ui
{
    private int _qyFilled = -1, _qyFilledStamp, _qyMetres = -1;
    private string _qyTitle = "", _qyText = "", _qyHint = "", _qyFar = "";

    private void DrawQuarry(Game game, float2 pos, float2 size, Settings settings)
    {
        if (!game.SandboxOn || PauseOpen || MenuOpen)
            return;
        if (game.QuarryRiding)
        {
            DrawQuarryRide(game); // (the induction rides the same bus)
            return;
        }
        if (game.TutorialCurrent != null)
            return;
        var state = game.QuarryLocal;
        var since = Brutal.GlfwApi.Glfw.Time - game.QuarryChangedAt;
        if (state == Game.QuarryNone || state == Game.QuarryDone && since > 6)
            return;
        var script = QuarryScript.Current;
        var step = game.QuarryStepNow;
        var stamp = settings.Bind(GameAction.Interact) ^ settings.Bind(GameAction.Fire) << 8 ^ settings.Bind(GameAction.Streak2) << 16 ^ settings.Bind(GameAction.Aim) << 4;
        if (state != _qyFilled || stamp != _qyFilledStamp)
        {
            _qyFilled = state;
            _qyFilledStamp = stamp;
            _qyMetres = -1;
            (_qyTitle, _qyText, _qyHint) = state == Game.QuarryReport ? (script.ReportTitle, Tutorial.Fill(script.ReportText, settings), "")
                : state == Game.QuarryDone ? ("Duty done", "The Ministry will call you for the next one.", "")
                : step != null ? (step.Title, Tutorial.Fill(step.Text, settings), step.Hint.Length > 0 ? Tutorial.Fill(step.Hint, settings) : "") : ("", "", "");
        }
        if (_qyTitle.Length == 0)
            return;
        ImGui.SetNextWindowPos(new float2(pos.X + 24 * _scale, pos.Y + size.Y * 0.2f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(400 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.55f);
        if (ImGui.Begin("##quarry"u8, Overlay & ~ImGuiWindowFlags.NoBackground))
        {
            if (since < 1.5 && state > Game.QuarryFirstStep)
                ImGuiSafe.TextColored(Green, "Done.");
            if (step != null)
                ImGuiSafe.TextColored(Dim, $"MINISTRY DUTY: THE QUARRY  {state - Game.QuarryFirstStep + 1} / {script.Steps.Length}");
            else
                ImGuiSafe.TextColored(Dim, "MINISTRY OF SLOP: DUTIES");
            ImGuiSafe.TextColored(Orange, _qyTitle);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 380 * _scale);
            ImGuiSafe.TextWrapped(_qyText);
            if (_qyHint.Length > 0 && step != null && since >= step.HintAfter)
            {
                ImGui.Spacing();
                ImGuiSafe.TextColored(Orange, _qyHint);
            }
            if (game.PlayAreaOutLocal && step != null)
            {
                ImGui.Spacing();
                ImGuiSafe.TextColored(Red, PlayAreaLine(step.Area));
            }
            ImGui.PopTextWrapPos();
            if (game.QuarryHasTarget && game.Local is { } me)
            {
                var metres = (int)System.Numerics.Vector3.Distance(me.Position, game.QuarryTarget);
                if (metres != _qyMetres)
                {
                    _qyMetres = metres;
                    _qyFar = metres < 3 ? "It's right here (the orange marker)." : $"The orange marker: {metres} m away.";
                }
                ImGuiSafe.TextColored(Dim, _qyFar);
            }
            if (state != Game.QuarryDone)
                ImGuiSafe.TextColored(Dim, "Esc menu: skip this duty");
        }
        ImGui.End();
        if (game.QuarryHasTarget && state != Game.QuarryDone)
            TutorialMarker(game.QuarryTarget);
    }

    /// <summary>The crew bus scene's words: a line at the bottom while it pulls away, then "Some time later..." in the black.</summary>
    private void DrawQuarryRide(Game game)
    {
        var t = (game.MoverRenderTick - game.QuarryRideFrom) / World.TickRate;
        var vp = ImGui.GetMainViewport();
        var dl = ImGui.GetForegroundDrawList();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        var black = Program.QuarryRideBlack(t);
        if (black > 0)
            ImDrawListExtensions.AddRectFilled(dl, in org, new float2(org.X + area.X, org.Y + area.Y), Hud.Colour(System.Numerics.Vector3.Zero, black));
        // Letterbox bars: the ride is a cinematic (no HUD, Program.cs), eased in over its first half second.
        var bar = area.Y * 0.1f * (float)Math.Clamp(t / 0.5, 0, 1);
        ImDrawListExtensions.AddRectFilled(dl, in org, new float2(org.X + area.X, org.Y + bar), Hud.Colour(System.Numerics.Vector3.Zero, 1f));
        ImDrawListExtensions.AddRectFilled(dl, new float2(org.X, org.Y + area.Y - bar), new float2(org.X + area.X, org.Y + area.Y), Hud.Colour(System.Numerics.Vector3.Zero, 1f));
        var line = t > Game.QuarryRideAway && t < Game.QuarryRideAway + Game.QuarryRideBlack + 0.6 ? "Some time later..." : "Company-approved transport. Do not touch anything.";
        SubtitleLine(dl, line, org.Y + area.Y * (black > 0.5f ? 0.5f : 0.8f), 22f);
    }

    /// <summary>A story ride's screen (Program.cs draws this instead of the HUD while <see cref="Game.QuarryRiding"/>): its words only.</summary>
    public void DrawRideUi(Game game)
    {
        if (!game.SandboxOn || PauseOpen || MenuOpen)
            return;
        DrawQuarryRide(game);
    }

    /// <summary>The Esc menu's duty button (Sandbox): skip the quarry while it runs.</summary>
    private void DrawPauseQuarry(Game game)
    {
        if (game.QuarryLocal is Game.QuarryNone or Game.QuarryDone || game.QuarryRiding)
            return;
        ImGui.Spacing();
        if (ImGui.Button("Skip this Ministry duty##quarry"u8))
            game.QuarryAskSkip();
    }
}
