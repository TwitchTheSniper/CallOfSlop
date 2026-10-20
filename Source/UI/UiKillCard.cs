using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The kill card: after you die, who killed you, with what (a 3D picture of their gun and its
/// attachments), headshot, distance, their remaining health and streak. It fades and slides in
/// during the death cam (bottom centre of the HUD safe area), then sits beside the loadout window
/// when there's room for it there (ultra-wide screens); otherwise it goes away when the loadout opens.
/// </summary>
public sealed partial class Ui
{
    private static readonly float4 KillCardRed = new(1f, 0.3f, 0.28f, 1f);
    private static readonly float4 KillCardCyan = new(0.4f, 0.9f, 1f, 1f);

    /// <summary>Seconds after death before the card starts to appear, and how long it takes to fade in.</summary>
    private const float KillCardDelay = 0.35f, KillCardFade = 0.3f;

    /// <summary>Draws the kill card if there's one to show. Call after the loadout screen (it uses that window's position).</summary>
    public void DrawKillCard(Game game, DeathCam cam, double now, Settings settings)
    {
        var info = cam.Info;
        if (info == null || !cam.Active || game.Local is not { Alive: false } || PauseOpen || ShowScoreboard || game.Phase == MatchPhase.Ended)
            return;
        var age = cam.Elapsed(now) - KillCardDelay;
        if (age <= 0)
            return;
        var t = Math.Clamp(age / KillCardFade, 0f, 1f);
        var ease = 1 - (1 - t) * (1 - t) * (1 - t);

        var width = 330 * _scale;
        var margin = 18 * _scale;
        float2 anchor, pivot;
        if (MenuOpen)
        {
            // Beside the loadout window, if it fits to its left without overlapping.
            var vp = ImGui.GetMainViewport();
            if (!_loadoutDrawn || _loadoutRect.Min.X - vp.WorkPos.X < width + margin * 2)
                return;
            anchor = new float2(_loadoutRect.Min.X - margin, _loadoutRect.Min.Y);
            pivot = new float2(1, 0);
        }
        else
        {
            var (pos, size) = HudArea(settings.HudWidth);
            anchor = new float2(pos.X + size.X / 2, pos.Y + size.Y - 70 * _scale + (1 - ease) * 30 * _scale);
            pivot = new float2(0.5f, 1);
        }

        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, pivot);
        ImGui.SetNextWindowSize(new float2(width, 0));
        ImGui.SetNextWindowBgAlpha(0.86f);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ease);
        if (ImGui.Begin("##killcard"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings
                                        | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
        {
            KillCardBody(info, ease);
            if (!MenuOpen && cam.Running(now))
            {
                ImGui.Spacing();
                ImGuiSafe.TextColored(Dim, $"Click to skip   {Keybinds.Name(settings.Bind(GameAction.Loadout))}: loadout");
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void KillCardBody(KillInfo info, float alpha)
    {
        var c = info.KillerColor;
        var killerCol = new float4(c.X, c.Y, c.Z, 1f);

        // ---- Header ---------------------------------------------------------------------
        if (info.Kind == KillKind.Self)
        {
            ImGui.PushFont(default, 26);
            ImGui.TextColored(KillCardRed, "YOU KILLED YOURSELF"u8);
            ImGui.PopFont();
            ImGuiSafe.TextColored(Dim, $"with your own {info.WeaponName}");
            return;
        }
        ImGui.TextColored(Dim, info.Kind == KillKind.Streak ? "KILLED BY A KILL STREAK FROM"u8 : "KILLED BY"u8);
        ImGui.PushFont(default, 28);
        ImGuiSafe.TextColored(killerCol, info.KillerName);
        ImGui.PopFont();
        if (info.Headshot)
        {
            ImGui.SameLine();
            ImGui.TextColored(KillCardRed, "HEADSHOT"u8);
        }

        // ---- Weapon ---------------------------------------------------------------------
        if (info.Weapon is { } def)
        {
            // 3D picture of their gun (Program draws it over the UI; the veil fades it in with the card).
            var dl = ImGui.GetWindowDrawList();
            var p0 = ImGui.GetCursorScreenPos();
            var size = new float2(ImGui.GetContentRegionAvail().X, 104 * _scale);
            dl.AddRectFilled(p0, p0 + size, new ImColor8(11, 12, 15, (byte)(255 * alpha)), 3 * _scale);
            if (alpha > 0.5f)
                Thumbs.Add(new GunThumb(def, p0, p0 + size, Dim: Math.Clamp((1 - alpha) * 2, 0f, 1f)));
            ImGui.Dummy(size);
            ImGui.PushFont(default, 20);
            ImGui.Text(def.Name);
            ImGui.PopFont();
            ImGui.SameLine();
            ImGuiSafe.TextColored(Dim, def.ClassName);

            var any = false;
            foreach (var a in Attachments.InKit(info.Kit))
            {
                any = true;
                var optic = a.Slot == AttachSlot.Optic;
                ImGuiSafe.TextColored(optic ? KillCardCyan : Dim, optic ? "OPTIC" : Attachments.SlotName(a.Slot));
                ImGui.SameLine(110 * _scale);
                if (optic)
                    ImGuiSafe.TextColored(KillCardCyan, a.Name);
                else
                    ImGui.Text(a.Name);
            }
            if (!any)
                ImGui.TextColored(Dim, "No attachments"u8);
        }
        else
        {
            // Melee, grenades and streak rewards: the name, big.
            ImGui.PushFont(default, 24);
            ImGuiSafe.TextColored(Orange, info.WeaponName.ToUpperInvariant());
            ImGui.PopFont();
            if (info.Kind == KillKind.Melee)
                ImGui.TextColored(Dim, "Close quarters"u8);
        }

        // ---- Numbers --------------------------------------------------------------------
        ImGui.Separator();
        var dist = info.Distance >= 0
            ? info.Kind == KillKind.Throwable ? $"Thrown from {info.Distance:0} m" : $"{info.Distance:0} m away"
            : "";
        if (dist.Length > 0)
        {
            ImGui.Text(dist);
            ImGui.SameLine();
        }
        if (info.KillerHp >= 0)
        {
            var hp = info.KillerHp;
            var hpCol = hp <= 0 ? Dim : hp > 60 ? Green : hp > 30 ? Orange : KillCardRed;
            ImGuiSafe.TextColored(hpCol, hp <= 0 ? "   killer is dead too" : $"   killer has {hp} HP");
        }
        else
        {
            ImGui.NewLine();
        }
        if (info.KillerStreak > 1)
            ImGuiSafe.TextColored(Orange, $"{info.KillerStreak} kill streak" + (info.KillerKills > 0 ? $"   ({info.KillerKills} kills)" : ""));
        else if (info.KillerKills > 0)
            ImGuiSafe.TextColored(Dim, $"{info.KillerKills} kill{(info.KillerKills == 1 ? "" : "s")} this match");
    }
}
