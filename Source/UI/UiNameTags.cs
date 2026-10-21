using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>Draws the name tags decided by <see cref="NameTags"/> (see there for the rules) with <see cref="Hud"/>'s projection.</summary>
public sealed partial class Ui
{
    private readonly TagRect[] _tagRects = new TagRect[NameTags.Capacity]; // placed this frame, nearest first
    private readonly int[] _tagOf = new int[NameTags.Capacity]; // index into NameTags.Tags of each placed rect
    private readonly float[] _tagAlpha = new float[NameTags.Capacity];
    private readonly float[] _tagPx = new float[NameTags.Capacity];

    /// <summary>
    /// Draws this frame's name tags on the background draw list (over the world, under the HUD windows): projected above each head,
    /// sized by distance and the UI scale (smaller through a magnified scope), kept on screen, and de-cluttered (a tag overlapping a
    /// nearer one stacks above it, or fades). Drawn far to near so the nearest is on top. No allocations.
    /// </summary>
    public void DrawNameTags(Game game, NameTags tags, bool healthBars, bool scoped)
    {
        var cam = Hud.Camera;
        if (tags.Count == 0 || !cam.Valid)
            return;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        var r = tags.Rules;
        var pad = 4f * _scale;
        var placed = 0;
        for (var i = 0; i < tags.Count && placed < _tagRects.Length; i++)
        {
            ref readonly var t = ref tags.Tags[i];
            if (!Hud.Project(cam.ViewProj, t.Anchor, area.X, area.Y, out var s, out _, area.Y * 0.04f))
                continue; // behind you or off screen
            var px = NameTagLayout.FontPx(r, _scale, t.Distance, scoped);
            var w = Hud.TextWidth(game.NameOf(t.Id), px);
            var h = px + (healthBars && t.Friendly ? px * 0.35f : 0f);
            var (x, y) = NameTagLayout.Clamp(s.X - w / 2, s.Y - h, w, h, area.X, area.Y, pad);
            var rect = new TagRect(x, y, w, h);
            var fade = NameTagLayout.Place(new ReadOnlySpan<TagRect>(_tagRects, 0, placed), ref rect, r, 2f * _scale);
            _tagRects[placed] = rect;
            _tagOf[placed] = i;
            _tagAlpha[placed] = t.Alpha * fade;
            _tagPx[placed] = px;
            placed++;
        }

        var dl = ImGui.GetBackgroundDrawList();
        for (var k = placed - 1; k >= 0; k--)
        {
            ref readonly var t = ref tags.Tags[_tagOf[k]];
            if (!game.Players.TryGetValue(t.Id, out var p))
                continue;
            var rect = _tagRects[k];
            var a = _tagAlpha[k];
            var px = _tagPx[k];
            Hud.ShadowText(dl, new float2(org.X + rect.X0, org.Y + rect.Y0), px, game.NameOf(t.Id), tags.ColourOf(game, p, t.Friendly), a);
            if (healthBars && t.Friendly)
            {
                // Optional teammate health bar (Settings > Video), under the name.
                var bh = MathF.Max(2f, MathF.Round(px * 0.2f));
                var b0 = new float2(org.X + rect.X0, org.Y + rect.Y0 + px + px * 0.1f);
                var bw = rect.X1 - rect.X0;
                dl.AddRectFilled(b0, new float2(b0.X + bw, b0.Y + bh), Hud.Colour(System.Numerics.Vector3.Zero, a * 0.55f));
                dl.AddRectFilled(b0, new float2(b0.X + bw * t.Health01, b0.Y + bh), Hud.Colour(new System.Numerics.Vector3(0.4f, 1f, 0.5f), a * 0.9f));
            }
        }
    }
}
