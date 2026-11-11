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
    private string _tutTitle = "", _tutText = "", _tutWarden = "", _tutHint = "", _tutFar = "";
    private int _tutFarMetres = -1;
    private double _tutLookedAt = -1;
    private System.Numerics.Vector3? _tutTarget; // the step's marker (its "target"), looked for twice a second

    /// <summary>How far the marker looks for a step's target.</summary>
    private const float TutorialTargetRange = 60f, TutorialBunkRange = 200f;

    private static readonly float4 Warden = new(0.78f, 0.82f, 0.62f, 1f);

    private void DrawTutorial(Game game, float2 pos, float2 size, Settings settings)
    {
        if (!game.SandboxOn || PauseOpen || MenuOpen || game.QuarryRiding)
            return; // (the crew bus scene has the screen: UiQuarry.cs)
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
                if (_tutHint.Length > 0 && since >= step.HintAfter)
                {
                    ImGui.Spacing();
                    ImGuiSafe.TextColored(Orange, _tutHint);
                }
                ImGui.PopTextWrapPos();
                TutorialLook(game, step);
                if (_tutTarget is { } at && game.Local is { } me)
                {
                    var metres = (int)System.Numerics.Vector3.Distance(me.Position, at);
                    if (metres != _tutFarMetres)
                    {
                        _tutFarMetres = metres;
                        _tutFar = metres < 3 ? "It's right here (the orange marker)." : $"The orange marker: {metres} m away.";
                    }
                    ImGuiSafe.TextColored(Dim, _tutFar);
                }
                ImGuiSafe.TextColored(Dim, "Esc menu: skip the induction");
            }
        }
        ImGui.End();
        if (step != null && _tutTarget is { } spot)
            TutorialMarker(spot);
    }

    /// <summary>
    /// The step's target (<see cref="TutorialStep.Target"/>), nearest to this PC's player, from the constructs this peer has:
    /// looked for twice a second (a walk over the bodies, nothing allocated), none when the step has no target.
    /// </summary>
    private void TutorialLook(Game game, TutorialStep step)
    {
        var now = Brutal.GlfwApi.Glfw.Time;
        if (step.Spot != QuarrySpotKind.None)
        {
            _tutTarget = game.QuarryHasTarget ? game.QuarryTarget : null; // (the server's: a bus stop, the bus, the mine wall)
            return;
        }
        if (step.Target == TutorialTarget.None || game.Local is not { } me)
        {
            _tutTarget = null;
            return;
        }
        if (now - _tutLookedAt < 0.5 && now >= _tutLookedAt)
            return;
        _tutLookedAt = now;
        _tutTarget = null;
        var from = me.Position;
        var best = step.Target == TutorialTarget.Bunk ? TutorialBunkRange * TutorialBunkRange : TutorialTargetRange * TutorialTargetRange;
        var bestDropped = false;
        foreach (var c in game.Constructs.Bodies)
        {
            if (step.Target == TutorialTarget.Loose)
            {
                if (c.PartCount != 1 || c.Anchored || c.Pinned)
                    continue;
                var at = c.WorldCom;
                var d = System.Numerics.Vector3.DistanceSquared(from, at);
                var dropped = TutorialDropped(step, c.DefOf(0).Name);
                if (dropped && !bestDropped && d < TutorialTargetRange * TutorialTargetRange || dropped == bestDropped && d < best)
                {
                    best = d;
                    bestDropped = dropped;
                    _tutTarget = at;
                }
                continue;
            }
            if (System.Numerics.Vector3.DistanceSquared(from, c.WorldCom) > (TutorialBunkRange + 60) * (TutorialBunkRange + 60))
                continue;
            for (var k = 0; k < c.PartCount; k++)
            {
                ref var part = ref c.Parts[k];
                if (!part.D.Bunk || part.Broken || part.SitterPlus1 != 0)
                    continue;
                var at = c.SegToWorld(part.Seg, part.Pos);
                var d = System.Numerics.Vector3.DistanceSquared(from, at);
                if (d < best)
                {
                    best = d;
                    _tutTarget = at;
                }
            }
        }
    }

    private static bool TutorialDropped(TutorialStep step, string name)
    {
        foreach (var n in step.Drops)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>A diamond over the step's target (the background list, under the HUD windows), while it is on screen.</summary>
    private void TutorialMarker(System.Numerics.Vector3 spot)
    {
        var cam = Hud.Camera;
        if (!cam.Valid)
            return;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        if (!Hud.Project(cam.ViewProj, spot + new System.Numerics.Vector3(0, 0.9f, 0), area.X, area.Y, out var sp, out _, 30f))
            return;
        var dl = ImGui.GetBackgroundDrawList();
        var c = new float2(org.X + sp.X, org.Y + sp.Y);
        var r = 7f * _scale;
        var pulse = 1.5f + 0.3f * MathF.Sin((float)Brutal.GlfwApi.Glfw.Time * 4f);
        ImDrawListExtensions.AddNgonFilled(dl, in c, r, Hud.Colour(new System.Numerics.Vector3(0.95f, 0.55f, 0.18f), 0.85f), 4);
        ImDrawListExtensions.AddCircle(dl, in c, r * pulse, Hud.Colour(new System.Numerics.Vector3(1f, 0.75f, 0.35f), 0.6f), 4, 1.5f * _scale);
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
        _tutHint = step.Hint.Length == 0 ? "" : Tutorial.Fill(step.Hint, settings);
        _tutFarMetres = -1;
        _tutLookedAt = -1;
        var who = step.Speaker.Length > 0 ? step.Speaker : Tutorial.Speaker;
        _tutWarden = step.Warden.Length == 0 ? "" : who.Length > 0 ? $"{who}: \"{step.Warden}\"" : $"\"{step.Warden}\"";
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
