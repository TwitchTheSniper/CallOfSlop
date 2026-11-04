using Brutal.ImGuiApi;
using Brutal.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The Space window (a button in the F7 weapon tweak window, docs/space/SPACE.md "The bubble"): which star system and body
/// this map is, the space time and warp, where the sun stands here, the moon, and the ships the host flies. The host sets
/// the warp; the provisional rule (<see cref="SpaceWarpRule"/>) keeps it at 1x while a ship in space burns. Closed, it costs one
/// bool check a frame.
/// </summary>
public sealed partial class Ui
{
    public bool SpaceOpen;

    private static readonly int[] SpaceWarps = [0, 1, 10, 100, 1000, 10_000, 100_000];
    private static readonly string[] SpaceWarpLabels = ["Pause", "1x", "10x", "100x", "1000x", "10000x", "100000x"];

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
            ImGuiSafe.TextWrapped($"Space time {t}, warp {s.Warp}x{(s.Flight is { } f && f.RequestedWarp != f.Clock.Warp ? $" (asked {f.RequestedWarp}x; a ship in space is burning, so 1x)" : "")}.");
            var at = session.Local?.Position ?? default;
            var elev = b.SunElevation(t, at);
            ImGuiSafe.TextWrapped(FormattableString.Invariant($"Here the sun stands {elev:0.0} degrees {(elev >= 0 ? "above" : "below")} the horizon."));
            if (b.Moon >= 0 && b.Look(b.Moon, t, at, out _, out var dist, out var ar))
                ImGuiSafe.TextWrapped(FormattableString.Invariant($"Moon: {s.System.Bodies[b.Moon].Name}, {dist / 1000:0.0} km away, {2 * ar * 180 / Math.PI:0.00} degrees across."));
            ImGui.Separator();
            if (s.Flight is { } hf)
            {
                ImGuiSafe.TextColored(Dim, "Warp (host only; drops to 1x while a person moves or rides, or a construct is awake: provisional)"u8);
                for (var k = 0; k < SpaceWarps.Length; k++)
                {
                    if (k > 0)
                        ImGui.SameLine();
                    var on = hf.RequestedWarp == SpaceWarps[k];
                    if (on)
                        ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.25f, 0.45f, 0.25f, 1f));
                    if (ImGui.Button(SpaceWarpLabels[k]))
                        session.RequestSpaceWarp(SpaceWarps[k]);
                    if (on)
                        ImGui.PopStyleColor();
                }
                ImGuiSafe.TextWrapped($"Ships: {hf.Ships.Count} (fastest warp they allow: {hf.Ships.MaxWarp}x).");
            }
            else
            {
                ImGuiSafe.TextColored(Dim, "Joined game: only the host sets the warp."u8);
                ImGuiSafe.TextWrapped($"Ships heard from the host: {s.Ships.Count}.");
            }
        }
        ImGui.End();
    }
}
