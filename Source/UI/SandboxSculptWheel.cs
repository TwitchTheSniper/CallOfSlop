using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The ground drill's outer ring (docs/sandbox/TERRAIN-DIGGING.md, "Sculpting" and "Shaping"): what it puts back (stone, dirt, sand:
/// the current option) and what it does (Dig, the default, in the middle of the arc; Flatten, Clean holes, Fill slope: a green dot).
/// The picks are this PC's; the rule for a material that has run out is <see cref="Place"/>. Nothing allocates per frame.
/// </summary>
public sealed class SandboxSculptWheel : IToolOptions
{
    private static readonly GroundKind[] Order = [GroundKind.Stone, GroundKind.Dirt, GroundKind.Sand];

    /// <summary>What the drill does (after the materials on the ring).</summary>
    public enum DrillMode : byte
    {
        Dig, // LMB digs, RMB puts back
        Flatten, // LMB levels to where the stroke began, RMB to the feet
        Clean, // LMB fills pits and dig marks
        Slope, // LMB twice: a ramp between the points
        COUNT,
    }

    private static readonly string[] ModeNames = ["Dig", "Flatten", "Clean holes", "Fill slope"];
    private static readonly string[] ModeHints = ["LMB digs, RMB puts back", "level the ground", "fill pits and dig marks", "a ramp between two points"];

    /// <summary>What LMB and RMB do (this PC's pick).</summary>
    public DrillMode Mode = DrillMode.Dig;

    private readonly RadialMenu.Item[] _options;

    public RadialMenu.Item[] Options => _options;

    /// <summary>What RMB puts back (this PC's pick).</summary>
    public GroundKind Kind = GroundKind.Dirt;

    public readonly RadialIcon IconDrawer;
    RadialIcon? IToolOptions.Icons => IconDrawer;
    /// <summary>The ring's current option: the mode; the material shows as a green dot (read every frame the wheel is out).</summary>
    public int CurrentOption
    {
        get
        {
            for (var k = 0; k < Order.Length; k++)
                _options[k].Marked = Order[k] == Kind;
            return Order.Length + (int)Mode;
        }
    }
    public void Choose(int option)
    {
        if (option >= 0 && option < Order.Length)
            Kind = Order[option];
        else if (option >= Order.Length && option < Order.Length + (int)DrillMode.COUNT)
            Mode = (DrillMode)(option - Order.Length);
    }

    public SandboxSculptWheel()
    {
        IconDrawer = DrawIcon;
        _options = new RadialMenu.Item[Order.Length + (int)DrillMode.COUNT];
        for (var k = 0; k < Order.Length; k++)
            _options[k] = new RadialMenu.Item { Label = DiggingTable.KindNames[(int)Order[k]] is var n ? char.ToUpperInvariant(n[0]) + n[1..] : "", Hint = "", Icon = (int)Order[k] };
        for (var k = 0; k < (int)DrillMode.COUNT; k++)
            _options[Order.Length + k] = new RadialMenu.Item { Label = ModeNames[k], Hint = ModeHints[k], Icon = ModeIcon + k };
    }

    private const int ModeIcon = 10;

    /// <summary>What a fill uses: the pick, or when none of it is carried the first of dirt, stone, sand that is (creative: always the pick).</summary>
    public GroundKind Place(SandboxInventory inv)
    {
        if (inv.Infinite || inv.Materials[Game.MaterialOf(Kind)] > 0)
            return Kind;
        for (var k = GroundKind.Dirt; k < GroundKind.COUNT; k++)
            if (inv.Materials[Game.MaterialOf(k)] > 0)
                return k;
        return Kind;
    }

    private static void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        switch (icon - ModeIcon)
        {
            case (int)DrillMode.Dig: // a bit pointing down
                ImDrawListExtensions.AddRectFilled(dl, P(-0.25f, -0.9f), P(0.25f, 0.1f), col, 0.05f * r, ImDrawFlags.None);
                ImDrawListExtensions.AddTriangleFilled(dl, P(-0.25f, 0.1f), P(0.25f, 0.1f), P(0f, 0.85f), col);
                return;
            case (int)DrillMode.Flatten: // a level bar over flat ground
                ImDrawListExtensions.AddRectFilled(dl, P(-0.9f, 0.25f), P(0.9f, 0.75f), col, 0f, ImDrawFlags.None);
                ImDrawListExtensions.AddLine(dl, P(-0.6f, -0.35f), P(0.6f, -0.35f), col, 0.14f * r);
                return;
            case (int)DrillMode.Clean: // ground with a pit being filled
                ImDrawListExtensions.AddRectFilled(dl, P(-0.9f, 0.3f), P(-0.25f, 0.8f), col, 0f, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, P(0.25f, 0.3f), P(0.9f, 0.8f), col, 0f, ImDrawFlags.None);
                ImDrawListExtensions.AddCircleFilled(dl, P(0f, -0.25f), 0.25f * r, col, 10);
                return;
            case (int)DrillMode.Slope: // a ramp
                ImDrawListExtensions.AddTriangleFilled(dl, P(-0.9f, 0.75f), P(0.9f, 0.75f), P(0.9f, -0.6f), col);
                return;
        }
        switch ((GroundKind)icon)
        {
            case GroundKind.Stone: // a block on a block
                ImDrawListExtensions.AddRectFilled(dl, P(-0.75f, 0f), P(0.75f, 0.7f), col, 0.08f * r, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, P(-0.4f, -0.7f), P(0.4f, -0.08f), col, 0.08f * r, ImDrawFlags.None);
                break;
            case GroundKind.Sand: // grains
                for (var k = 0; k < 7; k++)
                    ImDrawListExtensions.AddCircleFilled(dl, P(-0.6f + (k % 4) * 0.4f, 0.55f - (k / 4) * 0.5f - (k % 2) * 0.12f), 0.15f * r, col, 8);
                break;
            default: // a heap
                ImDrawListExtensions.AddCircleFilled(dl, P(0f, 0.55f), 0.8f * r, col, 18);
                ImDrawListExtensions.AddRectFilled(dl, P(-1f, 0.6f), P(1f, 1.4f), bg, 0f, ImDrawFlags.None);
                break;
        }
    }
}
