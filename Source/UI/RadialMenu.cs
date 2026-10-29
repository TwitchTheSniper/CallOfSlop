using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>Draws one radial item's icon (no allocation: keep the delegate in a field).</summary>
public delegate void RadialIcon(ImDrawListPtr dl, int icon, float2 centre, float radius, ImColor8 colour, ImColor8 background);

/// <summary>
/// A generic radial (pie) menu: a ring of segments around a centre, picked by direction (docs/sandbox/CONSTRUCTS.md,
/// "The tool wheel"). The owner opens it while a key is held, feeds it the mouse movement (<see cref="Move"/>: the view
/// is frozen meanwhile, so a small flick is enough) and asks <see cref="Release"/> which segment was chosen when the key
/// is let go (-1: the pointer sat in the middle dead zone, a cancel). Segment 0 is straight up and they go clockwise. The
/// highlighted segment gets an accent arc and its name and key hint show in the middle; the owner's current choice
/// (<see cref="Current"/>) is marked; disabled items are greyed out and drawn with their <see cref="Item.Note"/>.
/// Drawn with ImGui's draw list; nothing is allocated per frame (every string is the owner's, made once). Used by the
/// Sandbox tool wheel (<see cref="SandboxToolWheel"/>); meant for parts to place, logic components, pings or emotes later.
/// </summary>
public sealed class RadialMenu
{
    public sealed class Item
    {
        public string Label = "", Hint = "", Note = "";
        public bool Enabled = true;
        public int Icon;
    }

    public readonly Item[] Items;

    /// <summary>The pointer (accumulated mouse movement, pixels) must be at least this far out to pick a segment.</summary>
    public float DeadZone = 22f;

    /// <summary>The pointer is held within this distance, so turning to another segment takes a small flick, not a long drag.</summary>
    public float Reach = 70f;

    /// <summary>The middle's text with nothing highlighted.</summary>
    public string Title = "", Help = "";

    /// <summary>The marked item (the owner's current choice), or -1.</summary>
    public int Current = -1;

    public bool IsOpen { get; private set; }

    /// <summary>The accumulated pointer, from the centre (x right, y down).</summary>
    public Vector2 Pointer { get; private set; }

    /// <summary>The segment under the pointer, or -1 in the dead zone.</summary>
    public int Hovered { get; private set; } = -1;

    public RadialMenu(int count)
    {
        Items = new Item[Math.Max(1, count)];
        for (var i = 0; i < Items.Length; i++)
            Items[i] = new Item();
    }

    public void Open()
    {
        IsOpen = true;
        Pointer = Vector2.Zero;
        Hovered = -1;
    }

    /// <summary>Closes without choosing.</summary>
    public void Close()
    {
        IsOpen = false;
        Pointer = Vector2.Zero;
        Hovered = -1;
    }

    /// <summary>Adds a mouse movement while open (clamped to <see cref="Reach"/>) and updates the highlight.</summary>
    public void Move(Vector2 delta)
    {
        if (!IsOpen || !float.IsFinite(delta.X + delta.Y))
            return;
        var p = Pointer + delta;
        var length = p.Length();
        if (length > Reach)
            p *= Reach / length;
        Pointer = p;
        Hovered = SegmentAt(p, Items.Length, DeadZone);
    }

    /// <summary>Closes and returns the segment chosen (a disabled one too: the owner says why it can't be had), or -1 for a cancel.</summary>
    public int Release()
    {
        var pick = IsOpen ? Hovered : -1;
        Close();
        return pick;
    }

    /// <summary>
    /// The segment a pointer at <paramref name="p"/> (from the centre, y down) picks out of <paramref name="count"/>:
    /// segment 0 is centred straight up and they go clockwise, each 360/count degrees wide; -1 inside the dead zone (or
    /// for a pointer that isn't a number).
    /// </summary>
    public static int SegmentAt(Vector2 p, int count, float deadZone)
    {
        if (count <= 0 || !float.IsFinite(p.X + p.Y) || p.LengthSquared() < deadZone * deadZone || p == Vector2.Zero)
            return -1;
        var a = MathF.Atan2(p.X, -p.Y); // clockwise from up, -pi..pi
        if (a < 0)
            a += MathF.Tau;
        var step = MathF.Tau / count;
        var seg = (int)MathF.Floor((a + step * 0.5f) / step);
        return seg % count;
    }

    /// <summary>The middle of segment <paramref name="i"/>, radians clockwise from up.</summary>
    public static float SegmentAngle(int i, int count) => i * MathF.Tau / Math.Max(1, count);

    // Colours (the HUD's dark panels and orange accent).
    private static readonly ImColor8 Fill = new(18, 20, 26, 215), FillHover = new(56, 44, 26, 235), FillOff = new(18, 20, 26, 160);
    private static readonly ImColor8 Accent = new(240, 150, 40, 255), Mark = new(90, 220, 120, 255), Rim = new(255, 255, 255, 40);
    private static readonly ImColor8 IconOn = new(225, 228, 235, 255), IconHover = new(255, 200, 120, 255), IconOff = new(120, 124, 132, 150);
    private static readonly Vector3 White = new(0.95f), Dim = new(0.65f), Orange = new(1f, 0.65f, 0.25f);

    /// <summary>Draws the open menu around <paramref name="centre"/> (screen pixels) at HUD scale <paramref name="scale"/>.</summary>
    public void Draw(ImDrawListPtr dl, float2 centre, float scale, RadialIcon? icons)
    {
        if (!IsOpen)
            return;
        var n = Items.Length;
        var r0 = 72f * scale; // inner edge (the dead zone's ring)
        var r1 = 170f * scale; // outer edge
        var step = MathF.Tau / n;
        var gap = MathF.Min(0.035f, step * 0.08f);
        const int Slices = 12;
        ImDrawListExtensions.AddCircleFilled(dl, in centre, r0 - 6 * scale, new ImColor8(12, 13, 17, 210), 40);
        ImDrawListExtensions.AddCircle(dl, in centre, r0 - 6 * scale, Rim, 40, 1f * scale);
        for (var i = 0; i < n; i++)
        {
            var item = Items[i];
            var hover = i == Hovered;
            var mid = SegmentAngle(i, n);
            var a0 = mid - step * 0.5f + gap;
            var a1 = mid + step * 0.5f - gap;
            var fill = hover && item.Enabled ? FillHover : item.Enabled ? Fill : FillOff;
            // One annular sector: the outer arc out, the inner arc back (a concave fill, so no seams between slices).
            ImDrawListExtensions.PathClear(dl);
            ImDrawListExtensions.PathArcTo(dl, in centre, r1, a0 - MathF.PI / 2, a1 - MathF.PI / 2, Slices);
            ImDrawListExtensions.PathArcTo(dl, in centre, r0, a1 - MathF.PI / 2, a0 - MathF.PI / 2, Slices);
            ImDrawListExtensions.PathFillConcave(dl, fill);
            // The accent arc on the highlighted segment, a thin rim on the rest.
            var im0 = a0 - MathF.PI / 2;
            var im1 = a1 - MathF.PI / 2;
            ImDrawListExtensions.PathClear(dl);
            ImDrawListExtensions.PathArcTo(dl, in centre, r1 + (hover ? 5f : 1f) * scale, im0, im1, 16);
            ImDrawListExtensions.PathStroke(dl, hover ? (item.Enabled ? Accent : IconOff) : Rim, ImDrawFlags.None, (hover ? 6f : 1.5f) * scale);
            if (i == Current)
            {
                // The current choice: a green arc on the inner edge and a dot.
                ImDrawListExtensions.PathClear(dl);
                ImDrawListExtensions.PathArcTo(dl, in centre, r0 + 4f * scale, im0, im1, 12);
                ImDrawListExtensions.PathStroke(dl, Mark, ImDrawFlags.None, 4f * scale);
                var dot = At(centre, mid, r1 - 14f * scale);
                ImDrawListExtensions.AddCircleFilled(dl, in dot, 4.5f * scale, Mark, 12);
            }
            var ic = At(centre, mid, (r0 + r1) * 0.5f);
            var colour = !item.Enabled ? IconOff : hover ? IconHover : IconOn;
            icons?.Invoke(dl, item.Icon, ic, 30f * scale, colour, fill);
        }

        // The pointer's direction: a small dot inside the middle.
        if (Pointer.LengthSquared() > 1f)
        {
            var dir = Vector2.Normalize(Pointer);
            var k = MathF.Min(1f, Pointer.Length() / MathF.Max(Reach, 1f));
            var dot = new float2(centre.X + dir.X * (r0 - 14f * scale) * k, centre.Y + dir.Y * (r0 - 14f * scale) * k);
            ImDrawListExtensions.AddCircleFilled(dl, in dot, 3.5f * scale, Hovered >= 0 ? Accent : IconOff, 10);
        }

        // The middle: the highlighted item's name and key hint (or its note), else the title and help.
        var size = 20f * scale;
        var small = 14f * scale;
        if (Hovered >= 0)
        {
            var it = Items[Hovered];
            Centred(dl, centre, -size * 0.85f, size, it.Label, it.Enabled ? White : Dim);
            Centred(dl, centre, size * 0.35f, small, it.Enabled ? it.Hint : it.Note, it.Enabled ? Orange : Dim);
        }
        else
        {
            Centred(dl, centre, -size * 0.85f, size, Title, White);
            Centred(dl, centre, size * 0.35f, small, Help, Dim);
        }
    }

    private static float2 At(float2 c, float clockwiseFromUp, float r) =>
        new(c.X + MathF.Sin(clockwiseFromUp) * r, c.Y - MathF.Cos(clockwiseFromUp) * r);

    private static void Centred(ImDrawListPtr dl, float2 c, float dy, float size, string text, Vector3 colour)
    {
        if (text.Length == 0)
            return;
        var w = Hud.TextWidth(text, size);
        Hud.ShadowText(dl, new float2(c.X - w * 0.5f, c.Y + dy), size, text, colour, 1f);
    }
}
