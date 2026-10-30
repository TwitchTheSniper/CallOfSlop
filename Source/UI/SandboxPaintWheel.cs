using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The paint tool's ring on the tool wheel (board item dev-sandbox-paint-tool-pp01, decision 29; docs/sandbox/CONSTRUCTS.md,
/// "The paint tool"): the outer ring round the paint tool holds the three modes (Part, the default; Whole construct;
/// Reset colour), the ten swatches (<see cref="ConstructPaint.Swatches"/>), "Custom..." (opens the colour picker,
/// UiSandboxPaint.cs) and "Recent colours", which has the last eight colours used as the third tier's arc. Letting go on
/// the paint tool itself keeps the last mode and colour. This PC's state only (nothing on the wire: the server is asked for
/// each stroke). The recent colours are kept for the session. Nothing allocates per frame (a recent colour's label is made
/// when the colour is pushed).
/// </summary>
public sealed class SandboxPaintWheel : IToolOptions
{
    public enum Option
    {
        Part,
        Whole,
        Reset,
        Swatch0, // (ten swatches: Swatch0 + k)
        Custom = Swatch0 + 10,
        Recent,
        COUNT,
    }

    public const int RecentMax = 8;

    private readonly RadialMenu.Item[] _options = new RadialMenu.Item[(int)Option.COUNT];
    private readonly RadialMenu.Item[] _recentItems = new RadialMenu.Item[RecentMax];

    public RadialMenu.Item[] Options => _options;

    /// <summary>What the next stroke does (this PC's pick).</summary>
    public PaintMode Mode = PaintMode.Part;

    /// <summary>The colour in the can (a set <see cref="ConstructPaint"/> colour).</summary>
    public uint Colour = ConstructPaint.Swatches[0];

    /// <summary>The last colours used, newest first (0: none yet).</summary>
    public readonly uint[] Recent = new uint[RecentMax];

    /// <summary>"Custom..." was picked: Program opens the picker (set for one read).</summary>
    public bool WantCustom;

    public readonly RadialIcon IconDrawer;
    RadialIcon? IToolOptions.Icons => IconDrawer;

    public SandboxPaintWheel()
    {
        IconDrawer = DrawIcon;
        Set(Option.Part, "Part", "the part under the crosshair", 20);
        Set(Option.Whole, "Whole construct", "every part and link of it", 21);
        Set(Option.Reset, "Reset colour", "back to its own colours", 22);
        for (var k = 0; k < 10; k++)
            Set((Option)((int)Option.Swatch0 + k), ConstructPaint.SwatchNames[k], "paint with this", k);
        Set(Option.Custom, "Custom...", "pick any colour", 23);
        Set(Option.Recent, "Recent colours", "flick out: the last 8", 24);
        for (var k = 0; k < RecentMax; k++)
            _recentItems[k] = new RadialMenu.Item { Label = "empty", Hint = "", Note = "no colour used yet", Icon = 100 + k, Enabled = false };
        _options[(int)Option.Recent].Options = _recentItems;
        _options[(int)Option.Recent].OptionIcons = IconDrawer;
        Sync();
    }

    private void Set(Option o, string label, string hint, int icon) =>
        _options[(int)o] = new RadialMenu.Item { Label = label, Hint = hint, Icon = icon };

    /// <summary>The ring's current option: the swatch the can holds (Custom for any other colour); the mode shows as a green dot.</summary>
    public int CurrentOption
    {
        get
        {
            Sync();
            var k = Array.IndexOf(ConstructPaint.Swatches, Colour);
            return k >= 0 ? (int)Option.Swatch0 + k : (int)Option.Custom;
        }
    }

    private void Sync()
    {
        _options[(int)Option.Part].Marked = Mode == PaintMode.Part;
        _options[(int)Option.Whole].Marked = Mode == PaintMode.Whole;
        _options[(int)Option.Reset].Marked = Mode == PaintMode.Reset;
    }

    public void Choose(int option)
    {
        if (option <= (int)Option.Reset)
            Mode = (PaintMode)option;
        else if (option < (int)Option.Custom)
            Colour = ConstructPaint.Swatches[option - (int)Option.Swatch0];
        else if (option == (int)Option.Custom)
            WantCustom = true;
    }

    public void ChooseSub(int option, int sub)
    {
        if (option == (int)Option.Recent && sub >= 0 && sub < RecentMax && Recent[sub] != 0)
            Colour = Recent[sub];
        else
            Choose(option);
    }

    /// <summary>The can takes this colour (the eyedropper, the picker) and remembers it as the latest.</summary>
    public void SetColour(uint colour)
    {
        if (!ConstructPaint.IsSet(colour))
            return;
        Colour = colour;
        Remember(colour);
    }

    /// <summary>Puts a colour first in the recent list (the colour in use again moves up; the oldest drops off).</summary>
    public void Remember(uint colour)
    {
        if (Recent[0] == colour)
            return;
        var at = Array.IndexOf(Recent, colour);
        if (at < 0)
            at = RecentMax - 1;
        for (var k = at; k > 0; k--)
            Recent[k] = Recent[k - 1];
        Recent[0] = colour;
        for (var k = 0; k < RecentMax; k++)
        {
            var it = _recentItems[k];
            it.Enabled = Recent[k] != 0;
            it.Label = Recent[k] != 0 ? ConstructPaint.ToHex(Recent[k]) : "empty";
        }
    }

    private static ImColor8 Col(uint c) => new((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);

    private void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        void Disc(float x, float y, float rad, ImColor8 colour)
        {
            var p = P(x, y);
            ImDrawListExtensions.AddCircleFilled(dl, in p, rad * r, colour, 16);
        }
        void Sq(float x0, float y0, float x1, float y1, ImColor8 colour) =>
            ImDrawListExtensions.AddRectFilled(dl, P(x0, y0), P(x1, y1), colour, 0.05f * r, ImDrawFlags.None);
        if (icon < 10) // a swatch: a disc of the colour in a ring
        {
            Disc(0, 0, 0.95f, col);
            Disc(0, 0, 0.78f, Col(ConstructPaint.Swatches[icon]));
            return;
        }
        if (icon >= 100) // a recent colour
        {
            var rc = Recent[icon - 100];
            Disc(0, 0, 0.95f, col);
            Disc(0, 0, 0.78f, rc != 0 ? Col(rc) : bg);
            return;
        }
        switch (icon)
        {
            case 20: // Part: one square lit among faint ones
            {
                var faint = new ImColor8(150, 156, 168, 80);
                Sq(-0.85f, -0.85f, -0.05f, -0.05f, col);
                Sq(0.05f, -0.85f, 0.85f, -0.05f, faint);
                Sq(-0.85f, 0.05f, -0.05f, 0.85f, faint);
                Sq(0.05f, 0.05f, 0.85f, 0.85f, faint);
                break;
            }
            case 21: // Whole construct: all four squares lit
                Sq(-0.85f, -0.85f, -0.05f, -0.05f, col);
                Sq(0.05f, -0.85f, 0.85f, -0.05f, col);
                Sq(-0.85f, 0.05f, -0.05f, 0.85f, col);
                Sq(0.05f, 0.05f, 0.85f, 0.85f, col);
                break;
            case 22: // Reset: a turning-back arrow
            {
                var o = P(0, 0);
                ImDrawListExtensions.PathClear(dl);
                ImDrawListExtensions.PathArcTo(dl, in o, 0.62f * r, -MathF.PI * 0.2f, MathF.PI * 1.35f, 14);
                ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, 0.16f * r);
                var a = P(0.55f, -0.95f);
                var b = P(0.95f, -0.3f);
                var d = P(0.1f, -0.35f);
                ImDrawListExtensions.AddTriangleFilled(dl, in a, in b, in d, col);
                break;
            }
            case 23: // Custom: three overlapping coloured discs
                Disc(-0.3f, -0.25f, 0.5f, new ImColor8(220, 60, 50, 255));
                Disc(0.3f, -0.25f, 0.5f, new ImColor8(70, 190, 90, 255));
                Disc(0f, 0.3f, 0.5f, new ImColor8(60, 110, 220, 255));
                break;
            default: // Recent: a row of small discs
                Disc(-0.6f, 0, 0.3f, col);
                Disc(0, 0, 0.3f, col);
                Disc(0.6f, 0, 0.3f, col);
                break;
        }
    }
}
