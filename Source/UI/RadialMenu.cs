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
/// highlighted segment gets an accent arc; the owner's current choice (<see cref="Current"/>) is marked; disabled items are
/// greyed out. The middle shows only the highlighted name (shrunk, else cut short, to fit the centre circle at any scale:
/// <see cref="FitCentre"/>); the hint (or a disabled item's <see cref="Item.Note"/>) sits in a tooltip under the wheel.
/// <para>
/// The outer ring (board item dev-wheel-outer-ring-wo01): an item with <see cref="Item.Options"/> (a tool's modes) shows
/// a second ring around the inner one while it is pointed at, animating in quickly. Its segments sit in an arc centred on
/// the item's direction (<see cref="OptionStepMax"/> each), or round the full circle when there are too many for that.
/// Flicking further out (past <see cref="Reach"/> + <see cref="OuterGap"/>) onto one highlights it, the inner item staying
/// highlighted; letting go there gives the item and <see cref="PickedOption"/>; letting go on the inner segment gives the
/// item with no option (-1: the owner keeps its last-used option). Outside a partial arc the pointer turns the inner ring as
/// usual.
/// </para>
/// Drawn with ImGui's draw list; nothing is allocated per frame (every string is the owner's, made once; a name cut short
/// is made once and kept). Used by the Sandbox tool wheel (<see cref="SandboxToolWheel"/>; the linking tool's options are
/// <see cref="SandboxLinkWheel"/>) and the linking tool's port picker.
/// </summary>
public sealed class RadialMenu
{
    public sealed class Item
    {
        public string Label = "", Hint = "", Note = "";
        public bool Enabled = true;
        public int Icon;

        /// <summary>A toggle that is on (a green dot), for options like "highlight all".</summary>
        public bool Marked;

        /// <summary>This item's outer ring (its modes), or null for none.</summary>
        public Item[]? Options;

        /// <summary>The option marked as the current one on the outer ring, or -1.</summary>
        public int OptionCurrent = -1;

        /// <summary>Draws the options' icons (null: the menu's own drawer).</summary>
        public RadialIcon? OptionIcons;
    }

    public readonly Item[] Items;

    /// <summary>The pointer (accumulated mouse movement, pixels) must be at least this far out to pick a segment.</summary>
    public float DeadZone = 22f;

    /// <summary>The pointer is held within this distance, so turning to another segment takes a small flick, not a long drag.</summary>
    public float Reach = 70f;

    /// <summary>With an outer ring showing: the pointer must be this far past <see cref="Reach"/> to pick an option (a band that keeps the inner pick).</summary>
    public float OuterGap = 24f;

    /// <summary>With an outer ring showing the pointer is held within <see cref="Reach"/> + this.</summary>
    public float OuterDepth = 66f;

    /// <summary>An option's width in a partial arc (radians: 30 degrees); more than fit in 300 degrees go round the full circle.</summary>
    public const float OptionStepMax = MathF.PI / 6;

    /// <summary>How long the outer ring takes to animate in.</summary>
    public const float RingSeconds = 0.08f;

    /// <summary>The middle's title with nothing highlighted, and its help (in the tooltip).</summary>
    public string Title = "", Help = "";

    /// <summary>The marked item (the owner's current choice), or -1.</summary>
    public int Current = -1;

    public bool IsOpen { get; private set; }

    /// <summary>The accumulated pointer, from the centre (x right, y down).</summary>
    public Vector2 Pointer { get; private set; }

    /// <summary>The segment under the pointer (the outer ring's owner while an option is under it), or -1 in the dead zone.</summary>
    public int Hovered { get; private set; } = -1;

    /// <summary>The item whose outer ring is showing, or -1.</summary>
    public int Ring { get; private set; } = -1;

    /// <summary>The option under the pointer on the outer ring, or -1.</summary>
    public int HoveredOption { get; private set; } = -1;

    /// <summary>The option chosen by the last <see cref="Release"/> (-1: the inner segment, a cancel or none).</summary>
    public int PickedOption { get; private set; } = -1;

    /// <summary>The outer ring's animation, 0 (just shown) to 1.</summary>
    public float RingIn { get; private set; }

    public RadialMenu(int count)
    {
        Items = new Item[Math.Max(1, count)];
        for (var i = 0; i < Items.Length; i++)
            Items[i] = new Item();
    }

    public void Open()
    {
        IsOpen = true;
        Reset();
    }

    /// <summary>Closes without choosing.</summary>
    public void Close()
    {
        IsOpen = false;
        Reset();
    }

    private void Reset()
    {
        Pointer = Vector2.Zero;
        Hovered = -1;
        Ring = -1;
        HoveredOption = -1;
        RingIn = 0;
    }

    private bool HasRing(int i) => i >= 0 && i < Items.Length && Items[i].Enabled && Items[i].Options is { Length: > 0 };

    /// <summary>
    /// Adds a mouse movement while open (clamped to <see cref="Reach"/>, or further while an outer ring shows) and updates
    /// the highlight; <paramref name="dt"/> (seconds) animates the outer ring.
    /// </summary>
    public void Move(Vector2 delta, float dt = 1f / 60)
    {
        if (!IsOpen || !float.IsFinite(delta.X + delta.Y))
            return;
        var p = Pointer + delta;
        var length = p.Length();
        var reach = Ring >= 0 ? Reach + OuterDepth : Reach;
        if (length > reach)
        {
            p *= reach / length;
            length = reach;
        }
        var option = -1;
        var inner = SegmentAt(p, Items.Length, DeadZone);
        if (Ring >= 0 && length >= Reach + OuterGap)
        {
            option = OptionAt(p, Ring, Items.Length, Items[Ring].Options!.Length);
            if (option >= 0)
                inner = Ring; // (out on the ring: its owner stays highlighted)
        }
        var ring = option >= 0 ? Ring : HasRing(inner) ? inner : -1;
        if (ring != Ring)
        {
            Ring = ring;
            RingIn = 0;
            if (ring < 0 && length > Reach)
                p *= Reach / length; // (no ring any more: back within the inner reach)
        }
        else if (ring >= 0)
            RingIn = MathF.Min(1f, RingIn + MathF.Max(0f, float.IsFinite(dt) ? dt : 0f) / RingSeconds);
        Pointer = p;
        Hovered = inner;
        HoveredOption = option;
    }

    /// <summary>Closes and returns the segment chosen (a disabled one too: the owner says why it can't be had), or -1 for a cancel; <see cref="PickedOption"/> says which option.</summary>
    public int Release()
    {
        var pick = IsOpen ? Hovered : -1;
        PickedOption = IsOpen && pick >= 0 ? HoveredOption : -1;
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

    /// <summary>An outer ring's option width (radians): <see cref="OptionStepMax"/>, or the full circle shared out when more than fit in 300 degrees.</summary>
    public static float OptionStep(int options) =>
        options <= 0 ? 0f : options * OptionStepMax > MathF.Tau * (300f / 360f) ? MathF.Tau / options : OptionStepMax;

    /// <summary>True when the outer ring of <paramref name="options"/> goes round the full circle.</summary>
    public static bool FullCircle(int options) => options > 0 && OptionStep(options) < OptionStepMax;

    /// <summary>The middle of option <paramref name="i"/> on item <paramref name="owner"/>'s outer ring (radians clockwise from up): the options centred on the item's direction, in order clockwise.</summary>
    public static float OptionAngle(int i, int owner, int count, int options) =>
        SegmentAngle(owner, count) + (i - (options - 1) * 0.5f) * OptionStep(options);

    /// <summary>The option of item <paramref name="owner"/>'s outer ring in the direction of <paramref name="p"/>, or -1 outside its arc.</summary>
    public static int OptionAt(Vector2 p, int owner, int count, int options)
    {
        if (options <= 0 || !float.IsFinite(p.X + p.Y) || p == Vector2.Zero)
            return -1;
        var step = OptionStep(options);
        var a = MathF.Atan2(p.X, -p.Y) - (OptionAngle(0, owner, count, options) - step * 0.5f);
        a %= MathF.Tau;
        if (a < 0)
            a += MathF.Tau;
        var i = (int)MathF.Floor(a / step);
        return i >= 0 && i < options ? i : FullCircle(options) ? options - 1 : -1;
    }

    // Colours (the HUD's dark panels and orange accent).
    private static readonly ImColor8 Fill = new(18, 20, 26, 215), FillHover = new(56, 44, 26, 235), FillOff = new(18, 20, 26, 160), FillOwner = new(42, 34, 22, 230);
    private static readonly ImColor8 Accent = new(240, 150, 40, 255), Mark = new(90, 220, 120, 255), Rim = new(255, 255, 255, 40);
    private static readonly ImColor8 IconOn = new(225, 228, 235, 255), IconHover = new(255, 200, 120, 255), IconOff = new(120, 124, 132, 150);
    private static readonly ImColor8 Plate = new(12, 13, 17, 200);
    private static readonly Vector3 White = new(0.95f), Dim = new(0.65f), Orange = new(1f, 0.65f, 0.25f);

    // The geometry, in HUD pixels at scale 1 (times the scale).
    public const float InnerEdge = 72f, OuterEdge = 170f, CentreRadius = InnerEdge - 6f, RingGap = 8f, RingDepth = 62f;
    public const float CentreSize = 20f, CentreMinSize = 12f, CentrePad = 6f;

    /// <summary>Draws the open menu around <paramref name="centre"/> (screen pixels) at HUD scale <paramref name="scale"/>.</summary>
    public void Draw(ImDrawListPtr dl, float2 centre, float scale, RadialIcon? icons)
    {
        if (!IsOpen)
            return;
        var n = Items.Length;
        var r0 = InnerEdge * scale; // inner edge (the dead zone's ring)
        var r1 = OuterEdge * scale; // outer edge
        var step = MathF.Tau / n;
        var gap = MathF.Min(0.035f, step * 0.08f);
        ImDrawListExtensions.AddCircleFilled(dl, in centre, CentreRadius * scale, new ImColor8(12, 13, 17, 210), 40);
        ImDrawListExtensions.AddCircle(dl, in centre, CentreRadius * scale, Rim, 40, 1f * scale);
        for (var i = 0; i < n; i++)
        {
            var item = Items[i];
            var hover = i == Hovered;
            var owner = hover && HoveredOption >= 0; // (out on its ring: still highlighted, a shade quieter)
            var mid = SegmentAngle(i, n);
            var fill = hover && item.Enabled ? owner ? FillOwner : FillHover : item.Enabled ? Fill : FillOff;
            Sector(dl, centre, r0, r1, mid - step * 0.5f + gap, mid + step * 0.5f - gap, fill, hover, item.Enabled, i == Current, item.Marked, scale, 12);
            var ic = At(centre, mid, (r0 + r1) * 0.5f);
            var colour = !item.Enabled ? IconOff : hover ? IconHover : IconOn;
            icons?.Invoke(dl, item.Icon, ic, 30f * scale, colour, fill);
        }

        // The outer ring: it grows out from the inner one.
        var rOut = r1;
        if (Ring >= 0 && Items[Ring].Options is { Length: > 0 } opts)
        {
            var t = 1f - (1f - RingIn) * (1f - RingIn); // (ease out)
            var r2 = r1 + RingGap * scale;
            var r3 = r2 + RingDepth * scale * (0.35f + 0.65f * t);
            rOut = r3;
            var os = OptionStep(opts.Length);
            var og = MathF.Min(0.03f, os * 0.08f);
            var draw = Items[Ring].OptionIcons ?? icons;
            for (var i = 0; i < opts.Length; i++)
            {
                var o = opts[i];
                var mid = OptionAngle(i, Ring, n, opts.Length);
                var hover = i == HoveredOption;
                var fill = hover && o.Enabled ? FillHover : o.Enabled ? Fill : FillOff;
                Sector(dl, centre, r2, r3, mid - os * 0.5f + og, mid + os * 0.5f - og, fill, hover, o.Enabled, i == Items[Ring].OptionCurrent, o.Marked, scale, 8);
                var colour = !o.Enabled ? IconOff : hover ? IconHover : IconOn;
                draw?.Invoke(dl, o.Icon, At(centre, mid, (r2 + r3) * 0.5f), MathF.Min(22f * scale, (r3 - r2) * 0.36f), colour, fill);
            }
        }

        // The pointer's direction: a small dot inside the middle.
        if (Pointer.LengthSquared() > 1f)
        {
            var dir = Vector2.Normalize(Pointer);
            var k = MathF.Min(1f, Pointer.Length() / MathF.Max(Reach, 1f));
            var dot = new float2(centre.X + dir.X * (r0 - 14f * scale) * k, centre.Y + dir.Y * (r0 - 14f * scale) * k);
            ImDrawListExtensions.AddCircleFilled(dl, in dot, 3.5f * scale, Hovered >= 0 ? Accent : IconOff, 10);
        }

        // The middle: only the name (the option's, the item's, else the title), fitted to the centre circle.
        Item? it = Hovered >= 0 ? HoveredOption >= 0 && Items[Hovered].Options is { } ho ? ho[HoveredOption] : Items[Hovered] : null;
        var name = FitCentre(it?.Label ?? Title, scale, out var size);
        if (name.Length > 0)
        {
            var w = Hud.TextWidth(name, size);
            Hud.ShadowText(dl, new float2(centre.X - w * 0.5f, centre.Y - size * 0.5f), size, name, it == null || it.Enabled ? White : Dim, 1f);
        }

        // The tooltip under the wheel: the hint (a disabled item's note), else the help.
        var tip = it == null ? Help : it.Enabled ? it.Hint : it.Note;
        if (tip.Length > 0)
        {
            var ts = 15f * scale;
            var tw = Hud.TextWidth(tip, ts);
            var top = centre.Y + rOut + 10f * scale;
            var a = new float2(centre.X - tw * 0.5f - 8f * scale, top);
            var b = new float2(centre.X + tw * 0.5f + 8f * scale, top + ts + 8f * scale);
            ImDrawListExtensions.AddRectFilled(dl, a, b, Plate, 4f * scale, ImDrawFlags.None);
            Hud.ShadowText(dl, new float2(centre.X - tw * 0.5f, top + 4f * scale), ts, tip, it != null && it.Enabled ? Orange : Dim, 1f);
        }
    }

    /// <summary>One annular sector with its rim (the accent arc when highlighted) and the current / toggled marks.</summary>
    private static void Sector(ImDrawListPtr dl, float2 centre, float rIn, float rOut, float a0, float a1, ImColor8 fill, bool hover, bool enabled, bool current, bool marked, float scale, int slices)
    {
        var im0 = a0 - MathF.PI / 2;
        var im1 = a1 - MathF.PI / 2;
        // The outer arc out, the inner arc back (a concave fill, so no seams between slices).
        ImDrawListExtensions.PathClear(dl);
        ImDrawListExtensions.PathArcTo(dl, in centre, rOut, im0, im1, slices);
        ImDrawListExtensions.PathArcTo(dl, in centre, rIn, im1, im0, slices);
        ImDrawListExtensions.PathFillConcave(dl, fill);
        ImDrawListExtensions.PathClear(dl);
        ImDrawListExtensions.PathArcTo(dl, in centre, rOut + (hover ? 5f : 1f) * scale, im0, im1, 16);
        ImDrawListExtensions.PathStroke(dl, hover ? (enabled ? Accent : IconOff) : Rim, ImDrawFlags.None, (hover ? 6f : 1.5f) * scale);
        if (current)
        {
            // The current choice: a green arc on the inner edge.
            ImDrawListExtensions.PathClear(dl);
            ImDrawListExtensions.PathArcTo(dl, in centre, rIn + 4f * scale, im0, im1, 12);
            ImDrawListExtensions.PathStroke(dl, Mark, ImDrawFlags.None, 4f * scale);
        }
        if (current || marked)
        {
            var dot = At(centre, (a0 + a1) * 0.5f, rOut - 12f * scale);
            ImDrawListExtensions.AddCircleFilled(dl, in dot, 4.5f * scale, Mark, 12);
        }
    }

    /// <summary>The width the centre circle has for a line of text <paramref name="size"/> pixels tall, centred on it, at HUD scale <paramref name="scale"/>.</summary>
    public static float CentreWidth(float scale, float size)
    {
        var r = CentreRadius * scale;
        var h = size * 0.5f;
        return h >= r ? 0f : 2f * MathF.Sqrt(r * r - h * h) - 2f * CentrePad * scale;
    }

    private static readonly Dictionary<string, (float Fit, string Shown)> _fits = new(StringComparer.Ordinal);
    private static float _fitsFont = -1f;

    /// <summary>
    /// The centre text as drawn: <paramref name="text"/> at <see cref="CentreSize"/> if it fits the centre circle, else
    /// shrunk to fit (down to <see cref="CentreMinSize"/>), else cut short with "..." at that size; <paramref name="size"/>
    /// is the size it is drawn at. Text widths scale with size and the circle with the scale, so the fit is worked out once
    /// a text (and again if the font changes); call inside an ImGui frame.
    /// </summary>
    public static string FitCentre(string text, float scale, out float size)
    {
        size = CentreSize * scale;
        if (text.Length == 0)
            return text;
        var font = ImGui.GetFontSize();
        if (font != _fitsFont)
        {
            _fits.Clear();
            _fitsFont = font;
        }
        if (!_fits.TryGetValue(text, out var f))
        {
            // At scale 1: the width a size gives, against the room the circle has at that size.
            float W(string s, float sz) => Hud.TextWidth(s, sz);
            var sz = CentreSize;
            var shown = text;
            for (var k = 0; k < 4 && W(text, sz) > CentreWidth(1f, sz) && sz > CentreMinSize; k++)
                sz = MathF.Max(CentreMinSize, sz * CentreWidth(1f, sz) / W(text, sz) * 0.995f);
            if (W(text, sz) > CentreWidth(1f, sz))
            {
                sz = CentreMinSize;
                var keep = text.Length - 1;
                while (keep > 1 && W(string.Concat(text.AsSpan(0, keep).TrimEnd(), "..."), sz) > CentreWidth(1f, sz))
                    keep--;
                shown = string.Concat(text.AsSpan(0, keep).TrimEnd(), "...");
            }
            f = (sz / CentreSize, shown);
            _fits[text] = f;
        }
        size = CentreSize * scale * f.Fit;
        return f.Shown;
    }

    private static float2 At(float2 c, float clockwiseFromUp, float r) =>
        new(c.X + MathF.Sin(clockwiseFromUp) * r, c.Y - MathF.Cos(clockwiseFromUp) * r);
}
