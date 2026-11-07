using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The texture paint tool's ring on the tool wheel (docs/sandbox/CONSTRUCTS.md, "The texture paint tool"): the three modes (Part,
/// the default; Whole construct; Reset texture), "Textures..." (opens the list, UiSandboxTexture.cs) and "Recent textures", the last
/// eight materials used as the third tier's arc, each drawn with its swatch. Letting go on the tool itself keeps the last mode and
/// material. This PC's state only: the server is asked for each stroke. Nothing allocates per frame.
/// </summary>
public sealed class SandboxTextureWheel : IToolOptions
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

    /// <summary>The material on the roller (a row of Materials/Surfaces.json the tool offers; 0 before the table is read).</summary>
    public byte Finish;

    /// <summary>The last materials used, newest first (0: none yet).</summary>
    public readonly byte[] Recent = new byte[RecentMax];

    /// <summary>"Textures..." was picked: Program opens the list (set for one read).</summary>
    public bool WantList;

    public readonly RadialIcon IconDrawer;
    RadialIcon? IToolOptions.Icons => IconDrawer;

    public SandboxTextureWheel()
    {
        IconDrawer = DrawIcon;
        Set(Option.Part, "Part", 20);
        Set(Option.Whole, "Whole construct", 21);
        Set(Option.Reset, "Reset texture", 22);
        Set(Option.List, "Textures...", 23);
        Set(Option.Recent, "Recent textures", 24);
        for (var k = 0; k < RecentMax; k++)
            _recentItems[k] = new RadialMenu.Item { Label = "empty", Hint = "", Note = "no texture used yet", Icon = 100 + k, Enabled = false };
        _options[(int)Option.Recent].Options = _recentItems;
        _options[(int)Option.Recent].OptionIcons = IconDrawer;
        Sync();
    }

    private void Set(Option o, string label, int icon) => _options[(int)o] = new RadialMenu.Item { Label = label, Hint = "", Icon = icon };

    /// <summary>The material on the roller, the first the file offers until one is picked.</summary>
    public byte Current
    {
        get
        {
            if (Finish == 0)
                for (var i = Surfaces.FirstShaded; i < Surfaces.File.Rows.Length && Finish == 0; i++)
                    if (ConstructFinish.Offered(i))
                        Finish = (byte)i;
            return Finish;
        }
    }

    /// <summary>The tool card's mode line.</summary>
    public string ModeText => Mode switch { PaintMode.Whole => "whole construct", PaintMode.Reset => "reset texture", _ => ConstructFinish.Name(Current) };

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
        if (option == (int)Option.Recent && sub >= 0 && sub < RecentMax && Recent[sub] != 0)
        {
            Finish = Recent[sub];
            if (Mode == PaintMode.Reset)
                Mode = PaintMode.Part;
        }
        else
            Choose(option);
    }

    /// <summary>The roller takes this material (the list, the eyedropper) and remembers it as the latest.</summary>
    public void SetFinish(byte finish)
    {
        if (!ConstructFinish.Offered(finish))
            return;
        Finish = finish;
        if (Mode == PaintMode.Reset)
            Mode = PaintMode.Part;
        Remember(finish);
    }

    /// <summary>Puts a material first in the recent list (one in use again moves up; the oldest drops off).</summary>
    public void Remember(byte finish)
    {
        if (finish == 0 || Recent[0] == finish)
            return;
        var at = Array.IndexOf(Recent, finish);
        if (at < 0)
            at = RecentMax - 1;
        for (var k = at; k > 0; k--)
            Recent[k] = Recent[k - 1];
        Recent[0] = finish;
        for (var k = 0; k < RecentMax; k++)
        {
            var it = _recentItems[k];
            it.Enabled = Recent[k] != 0;
            it.Label = Recent[k] != 0 ? ConstructFinish.Name(Recent[k]) : "empty"; // (the row's own name: nothing is made)
        }
    }

    private void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        void Sq(float x0, float y0, float x1, float y1, ImColor8 colour) =>
            ImDrawListExtensions.AddRectFilled(dl, P(x0, y0), P(x1, y1), colour, 0.05f * r, ImDrawFlags.None);
        if (icon >= 100) // a recent material: its swatch
        {
            var row = Recent[icon - 100];
            var tex = row != 0 ? SurfaceThumbs.Get(row) : 0;
            Sq(-0.95f, -0.95f, 0.95f, 0.95f, col);
            if (tex != 0)
                dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, P(-0.8f, -0.8f), P(0.8f, 0.8f), new float2(0, 1), new float2(1, 0), new ImColor8(255, 255, 255, 255));
            else
                Sq(-0.8f, -0.8f, 0.8f, 0.8f, bg);
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
            case 23: // the list: a grid of tiles
                for (var y = 0; y < 3; y++)
                    for (var x = 0; x < 3; x++)
                        Sq(-0.85f + x * 0.6f, -0.85f + y * 0.6f, -0.35f + x * 0.6f, -0.35f + y * 0.6f, col);
                break;
            default: // Recent: a row of small tiles
                Sq(-0.9f, -0.25f, -0.4f, 0.25f, col);
                Sq(-0.25f, -0.25f, 0.25f, 0.25f, col);
                Sq(0.4f, -0.25f, 0.9f, 0.25f, col);
                break;
        }
    }
}
