using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Orbital Depot on the HUD (GameDepot.cs, docs/story/DEPOT.md): the step's title and what to do, with the player's own
/// keys, and how far the step's place is, top left where the induction's panel was. Text is filled once per step (and again if a
/// key it names is rebound) and the distance line once per metre: nothing per frame.
/// </summary>
public sealed partial class Ui
{
    private int _dpFilled = -1, _dpFilledStamp, _dpMetres = -1;
    private string _dpTitle = "", _dpText = "", _dpFar = "";

    private void DrawDepot(Game game, float2 pos, float2 size, Settings settings)
    {
        if (!game.SandboxOn || PauseOpen || MenuOpen || game.TutorialCurrent != null)
            return;
        var state = game.DepotLocal;
        var since = Brutal.GlfwApi.Glfw.Time - game.DepotChangedAt;
        if (state == Game.DepotNone || state == Game.DepotDone && since > 8)
            return;
        var script = DepotScript.Current;
        var step = game.DepotStepNow;
        var stamp = settings.Bind(GameAction.Interact) ^ settings.Bind(GameAction.Sprint) << 8 ^ settings.Bind(GameAction.CrouchHold) << 16 ^ settings.Bind(GameAction.Streak2) << 4;
        if (state != _dpFilled || stamp != _dpFilledStamp)
        {
            _dpFilled = state;
            _dpFilledStamp = stamp;
            (_dpTitle, _dpText) = state == Game.DepotReport ? (script.ReportTitle, Tutorial.Fill(script.ReportText, settings))
                : state == Game.DepotDone ? ("Delivered", "Your lander waits on the Remote Works' apron.")
                : step != null ? (step.Title, Tutorial.Fill(step.Text, settings)) : ("", "");
        }
        if (_dpTitle.Length == 0)
            return;
        ImGui.SetNextWindowPos(new float2(pos.X + 24 * _scale, pos.Y + size.Y * 0.2f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(400 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.55f);
        if (ImGui.Begin("##depot"u8, Overlay & ~ImGuiWindowFlags.NoBackground))
        {
            if (since < 1.5 && state > Game.DepotFirstStep)
                ImGuiSafe.TextColored(Green, "Done.");
            if (step != null)
                ImGuiSafe.TextColored(Dim, $"ORBITAL DEPOT  {state - Game.DepotFirstStep + 1} / {script.Steps.Length}");
            else
                ImGuiSafe.TextColored(Dim, "MINISTRY OF SLOP: REMOTE LABOUR");
            ImGuiSafe.TextColored(Orange, _dpTitle);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 380 * _scale);
            ImGuiSafe.TextWrapped(_dpText);
            ImGui.PopTextWrapPos();
            if (step != null && !game.ShellLocal)
                ImGuiSafe.TextColored(Dim, "Sit in a Remote Works control chair to go back up.");
            if (game.DepotHasTarget && game.Local is { } me)
            {
                var metres = (int)System.Numerics.Vector3.Distance(me.Position, game.DepotTarget);
                if (metres != _dpMetres)
                {
                    _dpMetres = metres;
                    _dpFar = $"{metres} m away";
                }
                ImGuiSafe.TextColored(Dim, _dpFar);
            }
        }
        ImGui.End();
        DepotMarker(game);
    }

    /// <summary>A diamond over the step's place (the background list, under the HUD windows), while it is on screen.</summary>
    private void DepotMarker(Game game)
    {
        var cam = Hud.Camera;
        if (!game.DepotHasTarget || !cam.Valid)
            return;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        if (!Hud.Project(cam.ViewProj, game.DepotTarget + new System.Numerics.Vector3(0, 1.2f, 0), area.X, area.Y, out var sp, out _, 30f))
            return;
        var dl = ImGui.GetBackgroundDrawList();
        var c = new float2(org.X + sp.X, org.Y + sp.Y);
        var r = 8f * _scale;
        ImDrawListExtensions.AddNgonFilled(dl, in c, r, Hud.Colour(new System.Numerics.Vector3(0.95f, 0.55f, 0.18f), 0.85f), 4);
        ImDrawListExtensions.AddCircle(dl, in c, r * 1.7f, Hud.Colour(new System.Numerics.Vector3(1f, 0.75f, 0.35f), 0.6f), 4, 1.5f * _scale);
    }
}
