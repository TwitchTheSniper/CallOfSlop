using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The Space window (a button in the F7 weapon tweak window, docs/space/SPACE.md "The bubble"): which star system and body
/// this map is, the space time and its speed, where the sun stands here, the moon, and the ships the host flies. The time
/// speed is set in the command view (UiCommand.cs; it was this window's warp buttons until 2026-11-16). Closed, it costs
/// one bool check a frame.
/// </summary>
public sealed partial class Ui
{
    public bool SpaceOpen;

    private void DrawSpaceWindow(Game? session)
    {
        if (!SpaceOpen)
            return;
        ImGui.SetNextWindowSize(new float2(520 * _scale, 360 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.94f);
        if (ImGui.Begin("Space###spacewindow"u8, ref SpaceOpen))
        {
            if (session?.Space is not { } s)
            {
                ImGuiSafe.TextColored(Dim, "This map isn't in a star system. A body's \"map\" in Systems/*.json puts a map in one (the Dev system's Planetoid)."u8);
                ImGui.End();
                return;
            }
            var b = s.Bubble;
            var t = s.Now;
            ImGuiSafe.TextWrapped($"{World.Map.Name} is {b.BodyInfo.Name} in {s.System.Name} ({s.System.File}).");
            ImGuiSafe.TextWrapped($"Space time {t}, time speed {s.Speed}x{(s.Flight is { } f && f.RequestedSpeed != f.Clock.Speed ? $" (asked {f.RequestedSpeed}x)" : "")}.");
            var at = session.Local?.Position ?? default;
            var elev = b.SunElevation(t, at);
            ImGuiSafe.TextWrapped(FormattableString.Invariant($"Here the sun stands {elev:0.0} degrees {(elev >= 0 ? "above" : "below")} the horizon."));
            if (b.Moon >= 0 && b.Look(b.Moon, t, at, out _, out var dist, out var ar))
                ImGuiSafe.TextWrapped(FormattableString.Invariant($"Moon: {s.System.Bodies[b.Moon].Name}, {dist / 1000:0.0} km away, {2 * ar * 180 / Math.PI:0.00} degrees across."));
            ImGui.Separator();
            if (s.Flight is { } hf)
                ImGuiSafe.TextWrapped($"Ships: {hf.Ships.Count} (the fastest time speed they allow: {hf.Ships.MaxSpeed}x).");
            else
                ImGuiSafe.TextWrapped($"Ships heard from the host: {s.Ships.Count}.");
            ImGui.TextColored(Dim, "The host sets the time speed in the command view (Home)."u8);
        }
        ImGui.End();
    }
}
