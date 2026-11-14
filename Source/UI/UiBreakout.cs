using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The convicts' break-out on the HUD (GameBreakout.cs, docs/story/BREAKOUT.md): what to do now, top left where the duty's
/// panel is, how far it is, and an orange marker over it. The ride itself is the crew bus's scene (UiQuarry.cs). Text is
/// filled once per stage and the distance once per metre: nothing per frame.
/// </summary>
public sealed partial class Ui
{
    private int _bkFilled = -1, _bkMetres = -1;
    private string _bkTitle = "", _bkText = "", _bkFar = "";

    private void DrawBreakout(Game game, float2 pos, float2 size)
    {
        if (!game.SandboxOn || PauseOpen || MenuOpen || game.QuarryRiding || game.TutorialCurrent != null)
            return;
        var stage = game.BreakoutLocal;
        var since = Brutal.GlfwApi.Glfw.Time - game.BreakoutChangedAt;
        if (stage == Game.BreakoutNone || stage == Game.BreakoutSpaceport && since > 60 || stage == Game.BreakoutAway && since > 30)
            return; // (at the spaceport the words stay a minute, away half a minute; the marker stays while there's somewhere to go)
        var script = BreakoutScript.Current;
        if (stage != _bkFilled)
        {
            _bkFilled = stage;
            _bkMetres = -1;
            (_bkTitle, _bkText) = stage switch
            {
                Game.BreakoutMuster => (script.MusterTitle, script.MusterText),
                Game.BreakoutAway => (script.Ship.Title, script.Ship.Text),
                _ => (script.SpaceportTitle, script.SpaceportText),
            };
        }
        if (_bkTitle.Length == 0)
            return;
        ImGui.SetNextWindowPos(new float2(pos.X + 24 * _scale, pos.Y + size.Y * 0.2f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(400 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.55f);
        if (ImGui.Begin("##breakout"u8, Overlay & ~ImGuiWindowFlags.NoBackground))
        {
            ImGuiSafe.TextColored(Dim, "THE BREAK-OUT");
            ImGuiSafe.TextColored(Orange, _bkTitle);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 380 * _scale);
            ImGuiSafe.TextWrapped(_bkText);
            ImGui.PopTextWrapPos();
            if (game.BreakoutHasTarget && game.Local is { } me)
            {
                var metres = (int)System.Numerics.Vector3.Distance(me.Position, game.BreakoutTarget);
                if (metres != _bkMetres)
                {
                    _bkMetres = metres;
                    _bkFar = metres < 3 ? "It's right here (the orange marker)." : $"The orange marker: {metres} m away.";
                }
                ImGuiSafe.TextColored(Dim, _bkFar);
            }
        }
        ImGui.End();
        if (game.BreakoutHasTarget)
            TutorialMarker(game.BreakoutTarget);
    }
}
