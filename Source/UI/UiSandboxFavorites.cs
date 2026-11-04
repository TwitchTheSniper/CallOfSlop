using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Favorites in the Tab inventory (docs/sandbox/CONSTRUCTS.md, "The Tab inventory"): parts this player keeps at hand, in a row at the
/// top of the inventory. A tile's star, or a right-click on any tile, adds or removes one. They go by part name in settings.txt
/// (<c>sandbox_favorites</c>), so they outlast a part list that changes; a name no part has any more just isn't shown.
/// </summary>
public sealed partial class Ui
{
    private static readonly ImColor8 SbStarOn = new(245, 190, 60, 255), SbStarOff = new(150, 154, 164, 170), SbStarBack = new(14, 16, 20, 200);

    private static float SbStarSize(float tile) => MathF.Max(12f, tile * 0.16f);

    /// <summary>Is the mouse on this tile's star (its top left corner)?</summary>
    private static bool SbOverStar(float2 at, float tile)
    {
        var m = ImGui.GetMousePos();
        var s = SbStarSize(tile);
        return m.X >= at.X && m.Y >= at.Y && m.X <= at.X + s + 4 && m.Y <= at.Y + s + 4;
    }

    private static bool SbIsFavorite(Settings settings, SbItem it) => it.Part != null && settings.SandboxFavorites.Contains(it.Part.Name);

    private static void SbToggleFavorite(Settings settings, SbItem it)
    {
        if (it.Part is not { } part)
            return;
        if (!settings.SandboxFavorites.Remove(part.Name))
            settings.SandboxFavorites.Add(part.Name);
        settings.Save();
    }

    /// <summary>A tile's star: gold on a favorite, faint on any tile the mouse is on (click it to add), nothing otherwise.</summary>
    private static void SbDrawStar(ImDrawListPtr dl, float2 at, float tile, bool on, bool hot)
    {
        if (!on && !hot)
            return;
        var s = SbStarSize(tile);
        var c = new float2(at.X + 2 + s * 0.5f, at.Y + 2 + s * 0.5f);
        ImDrawListExtensions.AddCircleFilled(dl, c, s * 0.62f, SbStarBack, 14);
        var colour = on ? SbStarOn : SbStarOff;
        // Five points, drawn as five thin spokes: the font's own star glyph isn't ASCII.
        for (var k = 0; k < 5; k++)
        {
            var a = -MathF.PI / 2 + k * MathF.Tau / 5;
            var tip = new float2(c.X + MathF.Cos(a) * s * 0.5f, c.Y + MathF.Sin(a) * s * 0.5f);
            dl.AddLine(c, tip, colour, MathF.Max(1.5f, s * 0.16f));
        }
    }

    /// <summary>The Favorites row: a tile for each, a click takes it, a right-click or its star removes it. What was clicked, or null.</summary>
    private SbItem? SbDrawFavorites(Game game, byte me, Settings settings, float mini)
    {
        SbItem? picked = null;
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Dim, "Favorites:"u8);
        var parts = _sbItems[0];
        var shown = 0;
        var right = ImGui.GetContentRegionAvail().X + ImGui.GetCursorScreenPos().X;
        for (var k = 0; k < settings.SandboxFavorites.Count; k++)
        {
            if (ConstructParts.Find(settings.SandboxFavorites[k]) is not { } d || d.Id >= parts.Length)
                continue;
            var it = parts[d.Id];
            var next = ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + mini;
            if (next > right && shown > 0)
                ImGui.NewLine(); // (a long list wraps)
            else
                ImGui.SameLine();
            var at = ImGui.GetCursorScreenPos();
            ImGui.PushID(k);
            var overStar = SbOverStar(at, mini);
            if (ImGui.InvisibleButton("##sbfav"u8, new float2(mini, mini)))
            {
                if (overStar)
                    SbToggleFavorite(settings, it);
                else
                    picked = it;
            }
            var hot = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            SbDrawTile(dl, it, at, mini, false, hot, false);
            SbDrawStar(dl, at, mini, true, hot);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                SbToggleFavorite(settings, it);
            if (hot)
                SbTooltip(game, me, it);
            ImGui.PopID();
            shown++;
        }
        if (shown == 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, "(right-click a part, or its star)"u8);
        }
        return picked;
    }
}
