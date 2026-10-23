using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where the APPEARANCE screen wants its 3D model drawn this frame (ImGui pixels) and what it looks like. Program renders
/// it behind the UI (Program.Appearance.cs), like the gunsmith's gun.
/// </summary>
public readonly record struct AppearancePreview(float2 WinMin, float2 WinMax, float2 ViewMin, float2 ViewMax, float Yaw, string Name,
    PlayerLook Look, HatId Hat, int Prestige);

/// <summary>
/// Main menu > APPEARANCE: who you are in a match. Left: your soldier, turning slowly (drag to turn it), wearing the hat
/// and the look picked here. Right: the HAT (moved here from the loadout screen; prestige hats, locked ones greyed) and one
/// picker per axis bots vary on (<see cref="LookAxis"/>: skin tone, hair colour, headwear, beard, helmet / cap colour) plus
/// the free-for-all outfit colour. Every axis has <b>Auto</b> (the look the game picks from your name, as before), and the
/// screen has RANDOM and RESET (everything back to Auto; the hat stays). Saved at once to appearance.txt
/// (<see cref="AppearanceStore"/>), used in every mode (team modes keep the team colour on the outfit), and sent to everyone
/// in the match (GameCosmetics.cs).
/// </summary>
public sealed partial class Ui
{
    /// <summary>The APPEARANCE screen is up (main menu only).</summary>
    public bool AppearanceOpen;

    /// <summary>Where the APPEARANCE model goes this frame (null when the screen isn't up); drawn by Program before the UI.</summary>
    public AppearancePreview? LookPreview;

    private float _lookYaw = 0.5f;
    private double _lookLastFrame, _lookDraggedAt = -100;
    private readonly System.Random _lookRng = new();

    /// <summary>Main menu > Appearance.</summary>
    public void OpenAppearance()
    {
        AppearanceOpen = true;
        _lookYaw = 0.5f;
        _lookDraggedAt = -100;
    }

    /// <summary>Back to the main menu (the footer's BACK and Esc). Everything is already saved.</summary>
    public void CloseAppearance()
    {
        AppearanceOpen = false;
        LookPreview = null;
        AppearanceStore.Save();
    }

    /// <summary>Height of a hat card on this screen (unscaled px).</summary>
    internal const float LookHatCardH = 112;

    /// <summary>Height of one axis row (heading + swatches, unscaled px).</summary>
    internal const float LookAxisRowH = 62;

    /// <summary>Size of a swatch / option button (unscaled px, height).</summary>
    internal const float LookSwatchH = 30;

    /// <summary>
    /// The screen's two areas inside the window's content (screen px): the model view on the left (40%) and the picker
    /// panel on the right, both above the footer. Pure (the self-test checks they don't overlap and fit at the user's resolutions).
    /// </summary>
    internal static (float2 ViewMin, float2 ViewMax, float2 PanelMin, float2 PanelMax) AppearanceRects(float2 contentMin, float2 contentMax, float scale)
    {
        var gap = 8 * scale;
        var footerTop = contentMax.Y - 46 * scale;
        var bottom = footerTop - gap;
        var viewW = MathF.Floor((contentMax.X - contentMin.X - gap) * 0.4f);
        var viewMin = contentMin;
        var viewMax = new float2(contentMin.X + viewW, bottom);
        return (viewMin, viewMax, new float2(viewMax.X + gap, contentMin.Y), new float2(contentMax.X, bottom));
    }

    /// <summary>The height the picker panel's content needs (unscaled px): heading, the hat row, the axis rows, the team note.</summary>
    internal static float AppearancePanelContentH() => 34 + 26 + LookHatCardH + 14 + LookAxes.Length * LookAxisRowH + 60;

    /// <summary>The axes in screen order.</summary>
    internal static readonly LookAxis[] LookAxes = Enum.GetValues<LookAxis>();

    /// <summary>Draws the APPEARANCE screen (main menu). <paramref name="name"/> is your player name (the Auto look comes from it).</summary>
    public void DrawAppearance(Audio audio, double now, string name)
    {
        LookPreview = null;
        if (!AppearanceOpen)
            return;
        TrackSecondClick();
        var dt = (float)Math.Clamp(now - _lookLastFrame, 0, 0.1);
        _lookLastFrame = now;
        var vp = ImGui.GetMainViewport();
        var center = new float2(vp.WorkPos.X + vp.WorkSize.X / 2, vp.WorkPos.Y + vp.WorkSize.Y / 2);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new float2(MathF.Min(1150 * _scale, vp.WorkSize.X - 40), MathF.Min(760 * _scale, vp.WorkSize.Y - 40)));
        var flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground; // Program paints it behind the model
        if (ImGui.Begin("APPEARANCE"u8, flags))
        {
            var winPos = ImGui.GetWindowPos();
            var winSize = ImGui.GetWindowSize();
            var style = ImGui.GetStyle();
            var contentMin = ImGui.GetCursorScreenPos();
            var contentMax = winPos + winSize - style.WindowPadding;
            var (viewMin, viewMax, panelMin, panelMax) = AppearanceRects(contentMin, contentMax, _scale);
            var prestige = CosmeticPrestige.Of(Progress);
            var autoLook = BodyModel.VariantFor(name, 0);

            // ---- The model: drag to turn it; it turns slowly by itself when left alone ----
            ImGui.SetCursorScreenPos(viewMin);
            ImGui.InvisibleButton("##lookview", new float2(MathF.Max(1, viewMax.X - viewMin.X), MathF.Max(1, viewMax.Y - viewMin.Y)));
            if (ImGui.IsItemActive())
            {
                _lookYaw += ImGui.GetIO().MouseDelta.X * 0.01f;
                _lookDraggedAt = now;
            }
            else if (now - _lookDraggedAt > 2.5)
                _lookYaw += dt * 0.35f;
            _lookYaw %= MathF.Tau;
            var dl = ImGui.GetWindowDrawList();
            DlText(dl, viewMin + new float2(10 * _scale, 8 * _scale), Dim8, "drag to turn");
            var worn = AppearanceStore.EffectiveHat(Progress);
            LookPreview = new AppearancePreview(winPos, winPos + winSize, viewMin, viewMax, _lookYaw, name, AppearanceStore.Look, worn, prestige);

            // ---- The pickers ----
            ImGui.SetCursorScreenPos(panelMin);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new float4(0.06f, 0.065f, 0.08f, 0.96f));
            if (ImGui.BeginChild("##lookpanel"u8, new float2(panelMax.X - panelMin.X, panelMax.Y - panelMin.Y), ImGuiChildFlags.Borders))
            {
                DrawLookButtons(audio);
                DrawLookHats(audio, prestige);
                foreach (var axis in LookAxes)
                    DrawLookAxis(audio, axis, autoLook);
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                ImGuiSafe.TextWrapped("Team modes: your shirt, sleeves, trousers and a bandana wear the team colour so sides stay readable. "
                                      + "Without teams (free-for-all, co-op, Zombies) they wear your outfit colour. Everything else (skin, hair, headwear, beard, helmet colour, hat) shows in every mode.");
                ImGui.PopStyleColor();
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();

            // ---- Footer ----
            var footerTop = contentMax.Y - 46 * _scale;
            ImGui.SetCursorScreenPos(new float2(contentMin.X, footerTop + 3 * _scale));
            if (ImGui.Button("< BACK###lookback"u8, new float2(200 * _scale, 40 * _scale)))
                CloseAppearance();
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + new float2(10 * _scale, 10 * _scale));
            ImGuiSafe.TextColored(Dim, AppearanceStore.Look.IsAuto
                ? "Saved automatically. All Auto: the look the game picks from your name. Everyone in your matches sees it."
                : "Saved automatically. Everyone in your matches sees this look.");
        }
        ImGui.End();
    }

    /// <summary>RANDOM and RESET, at the top of the panel.</summary>
    private void DrawLookButtons(Audio audio)
    {
        ImGui.PushFont(default, 20);
        ImGui.TextColored(Orange, "YOUR LOOK"u8);
        ImGui.PopFont();
        ImGui.SameLine();
        var btn = new float2(110 * _scale, 0);
        if (ImGui.Button("RANDOM"u8, btn))
        {
            SetLook(PlayerLook.Randomized(_lookRng));
            audio.Effect(Sfx.HitTick, 0.4f);
        }
        ImGui.SameLine();
        if (ImGui.Button("RESET"u8, btn))
        {
            SetLook(PlayerLook.Auto);
            audio.Effect(Sfx.HitTick, 0.4f);
        }
    }

    private static void SetLook(PlayerLook look)
    {
        AppearanceStore.Look = look.Sanitized();
        AppearanceStore.Save();
    }

    /// <summary>The hat row: No hat and each prestige hat, a small drawing each; locked ones greyed with their prestige.</summary>
    private void DrawLookHats(Audio audio, int prestige)
    {
        ImGui.TextColored(Orange, "Hat"u8);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"- unlocks with prestige (yours: {prestige}). Cosmetic only.");
        var choices = HatChoices();
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var origin = ImGui.GetCursorScreenPos();
        var w = MathF.Max(40 * _scale, (ImGui.GetContentRegionAvail().X - gap * (choices.Length - 1)) / choices.Length);
        var h = LookHatCardH * _scale;
        var dl = ImGui.GetWindowDrawList();
        var worn = AppearanceStore.EffectiveHat(Progress);
        for (var i = 0; i < choices.Length; i++)
        {
            var id = choices[i];
            var min = origin + new float2(i * (w + gap), 0);
            var max = min + new float2(w, h);
            var unlocked = Cosmetics.IsUnlocked(id, prestige);
            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"##lookhat{i}", new float2(w, h));
            var hot = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick)
            {
                if (!unlocked)
                    LockedFeedback(audio);
                else if (AppearanceStore.Hat != id)
                {
                    AppearanceStore.Hat = id;
                    AppearanceStore.Save();
                    Acted();
                    audio.Effect(Sfx.HitTick, 0.4f);
                }
            }
            var on = worn == id;
            var rr = 4 * _scale;
            dl.AddRectFilled(min, max, on ? new ImColor8(41, 51, 66, 248) : !unlocked ? new ImColor8(18, 18, 20, 220) : hot ? Hover8 : Card8, rr);
            dl.AddRect(min, max, on ? Orange8 : hot && unlocked ? new ImColor8(115, 190, 242, 255) : new ImColor8(60, 64, 74, 255), rr, ImDrawFlags.None, (on ? 2f : 1f) * _scale);
            var textH = 38 * _scale;
            var r = MathF.Max(4, MathF.Min(w, h - textH) * 0.36f);
            DrawHatPreview(dl, id, new float2((min.X + max.X) / 2, min.Y + (h - textH) / 2 + 2 * _scale), r, !unlocked);
            if (!unlocked)
                DrawPadlock(dl, new float2(max.X - 12 * _scale, min.Y + 14 * _scale), 14 * _scale, new ImColor8(150, 150, 158, 255));
            var clip = new float4(min.X, min.Y, max.X, max.Y);
            var def = Cosmetics.Get(id);
            ImGui.PushFont(default, 14);
            DlText(dl, new float2(min.X + 5 * _scale, max.Y - textH), unlocked ? White8 : Dim8, def?.Name ?? "No hat", clip);
            DlText(dl, new float2(min.X + 5 * _scale, max.Y - textH + 17 * _scale), on ? Orange8 : unlocked ? new ImColor8(115, 190, 242, 255) : new ImColor8(220, 120, 90, 255),
                on ? "Wearing" : unlocked ? "Unlocked" : $"LOCKED - {HatRequirement(id)}", clip);
            ImGui.PopFont();
        }
        ImGui.SetCursorScreenPos(origin + new float2(0, h + 8 * _scale));
        ImGui.Dummy(new float2(1, 1));
    }

    /// <summary>One axis: its heading (what it is now), then Auto and a swatch or a named button per option bots use.</summary>
    private void DrawLookAxis(Audio audio, LookAxis axis, in BodyVariant autoLook)
    {
        var look = AppearanceStore.Look;
        var value = look.Get(axis);
        var autoValue = PlayerLook.AutoValue(axis, autoLook, 0);
        var now = value == 0
            ? axis == LookAxis.Outfit ? "Auto (your player slot's colour)" : $"Auto ({PlayerLook.OptionName(axis, autoValue)})"
            : PlayerLook.OptionName(axis, value);
        ImGuiSafe.TextColored(Orange, PlayerLook.AxisName(axis));
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"- {now}");

        var dl = ImGui.GetWindowDrawList();
        var h = LookSwatchH * _scale;
        var gap = 6 * _scale;
        var pos = ImGui.GetCursorScreenPos();
        var rowX = pos.X;
        var right = pos.X + ImGui.GetContentRegionAvail().X;
        var colour = PlayerLook.Swatch(axis, 1) != null;
        for (var v = 0; v <= PlayerLook.Options(axis); v++)
        {
            var label = v == 0 ? "Auto" : PlayerLook.OptionName(axis, v);
            var w = v == 0 || !colour ? ImGui.CalcTextSize(label).X + 18 * _scale : h * 1.4f;
            if (pos.X + w > right && v > 0)
                pos = new float2(rowX, pos.Y + h + gap); // wrap (narrow windows)
            var min = pos;
            var max = pos + new float2(w, h);
            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"##look{(int)axis}_{v}", new float2(w, h));
            var hot = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !SecondClick && v != value)
            {
                SetLook(look.With(axis, v));
                Acted();
                audio.Effect(Sfx.HitTick, 0.35f);
            }
            var sel = v == value;
            var rr = 3 * _scale;
            if (PlayerLook.Swatch(axis, v) is { } c)
            {
                dl.AddRectFilled(min, max, new ImColor8((byte)Math.Clamp(c.X * 255, 0, 255), (byte)Math.Clamp(c.Y * 255, 0, 255), (byte)Math.Clamp(c.Z * 255, 0, 255), 255), rr);
            }
            else
            {
                dl.AddRectFilled(min, max, sel ? new ImColor8(44, 38, 32, 245) : hot ? Hover8 : Card8, rr);
                DlText(dl, min + new float2(9 * _scale, (h - ImGui.GetFontSize()) / 2), sel ? White8 : Dim8, label);
            }
            dl.AddRect(min, max, sel ? Orange8 : hot ? new ImColor8(115, 190, 242, 255) : new ImColor8(60, 64, 74, 255), rr, ImDrawFlags.None, (sel ? 2.5f : 1f) * _scale);
            pos = new float2(max.X + gap, min.Y);
        }
        ImGui.SetCursorScreenPos(new float2(rowX, pos.Y + h + 10 * _scale));
        ImGui.Dummy(new float2(1, 1));
    }
}
