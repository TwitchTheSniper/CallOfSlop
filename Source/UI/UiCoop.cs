using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The co-op HUD extras (docs/pve/DESIGN.md): the big centre banner. The objective panel itself is the module's HUD lines (CoopModule.HudLines).</summary>
public sealed partial class Ui
{
    /// <summary>OBJECTIVE 2/4: ..., SQUAD WIPED - REGROUPING, MISSION COMPLETE: across the upper middle for a few seconds.</summary>
    private static void DrawCoopBanner(Game game, double now, float2 pos, float2 size)
    {
        if (game.Mode != GameMode.Coop || game.CoopBannerText.Length == 0)
            return;
        var age = now - game.CoopBannerTime;
        if (age is < 0 or > 4)
            return;
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y * 0.24f), ImGuiCond.Always, new float2(0.5f, 0.5f));
        if (ImGui.Begin("##coopbanner"u8, Overlay))
        {
            ImGui.PushFont(default, 40);
            ImGuiSafe.TextColored(new float4(1f, 0.62f, 0.18f, (float)Math.Clamp(4 - age, 0, 1)), game.CoopBannerText);
            ImGui.PopFont();
        }
        ImGui.End();
    }
}
