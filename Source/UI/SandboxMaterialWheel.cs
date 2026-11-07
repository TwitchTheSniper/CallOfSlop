using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The material painter's ring on the tool wheel (docs/sandbox/CONSTRUCTS.md, "The material painter"): the three modes (Part, the
/// default; Whole construct; Own material, which puts a part back to its file's), "Materials..." (opens the list,
/// UiSandboxMaterial.cs) and "Recent materials", the last eight used as the third tier's arc, each a swatch of its colour.
/// Letting go on the tool itself keeps the last mode and material. This PC's state only: the server is asked for each stroke.
/// Nothing allocates per frame (a recent item's label is the material's own name string).
/// </summary>
public sealed class SandboxMaterialWheel : IToolOptions
{
    public enum Option
    {
        Part,
        Whole,
        Reset,
        List,
        Recent,
        COUNT,
    }

    public const int RecentMax = 8;

    private readonly RadialMenu.Item[] _options = new RadialMenu.Item[(int)Option.COUNT];
    private readonly RadialMenu.Item[] _recentItems = new RadialMenu.Item[RecentMax];

    public RadialMenu.Item[] Options => _options;

    /// <summary>What the next stroke does.</summary>
    public PaintMode Mode = PaintMode.Part;

    /// <summary>The material in hand: its place in <see cref="PartMaterials.Offered"/> (-1 before one is picked: the first).</summary>
    public int Material = -1;

    /// <summary>The last materials used, newest first (-1: none yet).</summary>
    public readonly int[] Recent = [-1, -1, -1, -1, -1, -1, -1, -1];

    /// <summary>"Materials..." was picked: Program opens the list (set for one read).</summary>
    public bool WantList;

    public readonly RadialIcon IconDrawer;
    RadialIcon? IToolOptions.Icons => IconDrawer;

    public SandboxMaterialWheel()
    {
        IconDrawer = DrawIcon;
        Set(Option.Part, "Part", 20);
        Set(Option.Whole, "Whole construct", 21);
        Set(Option.Reset, "Own material", 22);
        Set(Option.List, "Materials...", 23);
        Set(Option.Recent, "Recent materials", 24);
        for (var k = 0; k < RecentMax; k++)
            _recentItems[k] = new RadialMenu.Item { Label = "empty", Hint = "", Note = "no material used yet", Icon = 100 + k, Enabled = false };
        _options[(int)Option.Recent].Options = _recentItems;
        _options[(int)Option.Recent].OptionIcons = IconDrawer;
        Sync();
    }

    private void Set(Option o, string label, int icon) => _options[(int)o] = new RadialMenu.Item { Label = label, Hint = "", Icon = icon };

    /// <summary>The material in hand, the first offered until one is picked (-1: the painter offers none).</summary>
    public int Current
    {
        get
        {
            var n = PartMaterials.Offered.Length;
            if ((uint)Material >= (uint)n)
                Material = n > 0 ? 0 : -1;
            return Material;
        }
    }

    /// <summary>A material's name (its place in <see cref="PartMaterials.Offered"/>), "" for none.</summary>
    public static string NameOf(int k) => (uint)k < (uint)PartMaterials.Offered.Length ? PartMaterials.Offered[k] : "";

    /// <summary>The tool card's mode line.</summary>
    public string ModeText => Mode switch { PaintMode.Whole => "whole construct", PaintMode.Reset => "own material", _ => NameOf(Current) };

    public int CurrentOption
    {
        get
        {
            Sync();
            return (int)Mode;
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
        else if (option == (int)Option.List)
            WantList = true;
    }

    public void ChooseSub(int option, int sub)
    {
        if (option == (int)Option.Recent && sub >= 0 && sub < RecentMax && Recent[sub] >= 0)
        {
            Material = Recent[sub];
            if (Mode == PaintMode.Reset)
                Mode = PaintMode.Part;
        }
        else
            Choose(option);
    }

    /// <summary>The material in hand becomes this one (the list, the eyedropper) and is remembered as the latest.</summary>
    public void SetMaterial(int k)
    {
        if ((uint)k >= (uint)PartMaterials.Offered.Length)
            return;
        Material = k;
        if (Mode == PaintMode.Reset)
            Mode = PaintMode.Part;
        Remember(k);
    }

    /// <summary>Puts a material first in the recent list (one in use again moves up; the oldest drops off).</summary>
    public void Remember(int k)
    {
        if (k < 0 || Recent[0] == k)
            return;
        var at = Array.IndexOf(Recent, k);
        if (at < 0)
            at = RecentMax - 1;
        for (var i = at; i > 0; i--)
            Recent[i] = Recent[i - 1];
        Recent[0] = k;
        for (var i = 0; i < RecentMax; i++)
        {
            var it = _recentItems[i];
            it.Enabled = Recent[i] >= 0;
            it.Label = Recent[i] >= 0 ? NameOf(Recent[i]) : "empty";
        }
    }

    /// <summary>A material's swatch colour (Recycling.json's "colours").</summary>
    public static ImColor8 Swatch(int k)
    {
        var names = RecyclingTable.Current;
        var c = names.ColourOf(names.IdOf(NameOf(k)));
        return new ImColor8((byte)(Math.Clamp(c.X, 0, 1) * 255), (byte)(Math.Clamp(c.Y, 0, 1) * 255), (byte)(Math.Clamp(c.Z, 0, 1) * 255), 255);
    }

    private void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        void Sq(float x0, float y0, float x1, float y1, ImColor8 colour) =>
            ImDrawListExtensions.AddRectFilled(dl, P(x0, y0), P(x1, y1), colour, 0.05f * r, ImDrawFlags.None);
        if (icon >= 100) // a recent material: its swatch
        {
            var k = Recent[icon - 100];
            Sq(-0.95f, -0.95f, 0.95f, 0.95f, col);
            Sq(-0.8f, -0.8f, 0.8f, 0.8f, k >= 0 ? Swatch(k) : bg);
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
            case 22: // Own material: a turning-back arrow
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
            case 23: // the list: rows of a swatch and a line
                for (var y = 0; y < 3; y++)
                {
                    Sq(-0.85f, -0.85f + y * 0.6f, -0.45f, -0.45f + y * 0.6f, col);
                    Sq(-0.3f, -0.72f + y * 0.6f, 0.85f, -0.58f + y * 0.6f, col);
                }
                break;
            default: // Recent: a row of small tiles
                Sq(-0.9f, -0.25f, -0.4f, 0.25f, col);
                Sq(-0.25f, -0.25f, 0.25f, 0.25f, col);
                Sq(0.4f, -0.25f, 0.9f, 0.25f, col);
                break;
        }
    }
}
