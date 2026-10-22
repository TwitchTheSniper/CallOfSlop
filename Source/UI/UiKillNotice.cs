using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Two small HUD pieces: the killer's centre-screen kill notice ("KILLED Ash" in the victim's colour, with tags such as
/// HEADSHOT / MELEE / LONGSHOT 45 m / DOUBLE KILL under it, stacking for multi-kills and fading ~2 s after the last kill;
/// see <see cref="KillNoticeList"/>), and, for the owner, the health and time left of their sentry turret / attack drone.
/// </summary>
public sealed partial class Ui
{
    private static readonly float4 KillNoticeTag = new(1f, 0.85f, 0.3f, 1f);

    /// <summary>Centre screen, under the crosshair: the local player's recent kills.</summary>
    private void DrawKillNotices(Game game, double now, float2 pos, float2 size)
    {
        var list = game.KillNotices;
        var alpha = list.Alpha(now);
        if (alpha <= 0 || MenuOpen || !game.Module.ShowsKillNotices)
            return;
        const float nameSize = 22, tagSize = 16;

        // Widest line first, so every line can be centred in the auto-sized window.
        var widest = 0f;
        foreach (var n in list.Lines)
        {
            ImGui.PushFont(default, nameSize);
            widest = MathF.Max(widest, ImGui.CalcTextSize($"KILLED  {n.Victim}").X);
            ImGui.PopFont();
            if (n.Tags.Length > 0)
            {
                ImGui.PushFont(default, tagSize);
                widest = MathF.Max(widest, ImGui.CalcTextSize(n.Tags).X);
                ImGui.PopFont();
            }
        }

        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y / 2 + 64 * _scale), ImGuiCond.Always, new float2(0.5f, 0));
        if (ImGui.Begin("##killnotice"u8, Overlay))
        {
            var left = ImGui.GetCursorPosX();
            for (var i = list.Lines.Count - 1; i >= 0; i--) // newest on top
            {
                var n = list.Lines[i];
                var a = alpha * (i == list.Lines.Count - 1 ? 1f : 0.8f);
                ImGui.PushFont(default, nameSize);
                var text = $"KILLED  {n.Victim}";
                ImGui.SetCursorPosX(left + (widest - ImGui.CalcTextSize(text).X) / 2);
                var c = n.Color;
                ImGuiSafe.TextColored(new float4(MathF.Max(c.X, 0.25f), MathF.Max(c.Y, 0.25f), MathF.Max(c.Z, 0.25f), a), text);
                ImGui.PopFont();
                if (n.Tags.Length > 0)
                {
                    ImGui.PushFont(default, tagSize);
                    ImGui.SetCursorPosX(left + (widest - ImGui.CalcTextSize(n.Tags).X) / 2);
                    ImGuiSafe.TextColored(new float4(KillNoticeTag.X, KillNoticeTag.Y, KillNoticeTag.Z, a), n.Tags);
                    ImGui.PopFont();
                }
            }
        }
        ImGui.End();
    }

    /// <summary>Bottom centre, above the streak icons: your own turret / drone's health bar and seconds left.</summary>
    private void DrawOwnStreakEntities(Game game, double now, float2 pos, float2 size)
    {
        if (game.StreakEntities.Count == 0 || game.Local is not { } me || MenuOpen)
            return;
        var any = false;
        foreach (var e in game.StreakEntities.Values)
            any |= e.Owner == me.Id;
        if (!any)
            return;
        ImGui.SetNextWindowPos(new float2(pos.X + size.X / 2, pos.Y + size.Y - 175 * _scale), ImGuiCond.Always, new float2(0.5f, 1));
        if (ImGui.Begin("##ownstreakents"u8, Overlay))
        {
            foreach (var e in game.StreakEntities.Values)
            {
                if (e.Owner != me.Id)
                    continue;
                var f = Math.Clamp(e.Health / (float)Math.Max(1, e.MaxHealth), 0f, 1f);
                var life = e.Kind == StreakId.SentryTurret ? KillStreaks.TurretSeconds : KillStreaks.DroneSeconds;
                var left = Math.Max(0, (e.Until > 0 ? e.Until : e.Born + life) - now); // clients don't get Until: count from when it appeared
                var col = KillStreaks.Get(e.Kind).Color;
                ImGuiSafe.TextColored(new float4(col.X, col.Y, col.Z, 1f), $"{KillStreaks.Get(e.Kind).Name.ToUpperInvariant()}  {left:0}s");
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, f > 0.35f ? Green : Red);
                ImGui.ProgressBar(f, new float2(160 * _scale, 6 * _scale), ""u8);
                ImGui.PopStyleColor();
            }
        }
        ImGui.End();
    }
}
