using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// What the ground drill puts back, on the tool wheel's outer ring (docs/sandbox/TERRAIN-DIGGING.md, "Sculpting"): stone, dirt
/// (the default, so in the middle of the arc: straight out from the tool) and sand. The pick is this PC's; the rule for one that
/// has run out is <see cref="Place"/>. Nothing allocates per frame.
/// </summary>
public sealed class SandboxSculptWheel : IToolOptions
{
    private static readonly GroundKind[] Order = [GroundKind.Stone, GroundKind.Dirt, GroundKind.Sand];

    private readonly RadialMenu.Item[] _options;

    public RadialMenu.Item[] Options => _options;

    /// <summary>What RMB puts back (this PC's pick).</summary>
    public GroundKind Kind = GroundKind.Dirt;

    public readonly RadialIcon IconDrawer;
    RadialIcon? IToolOptions.Icons => IconDrawer;
    public int CurrentOption => Array.IndexOf(Order, Kind);
    public void Choose(int option) => Kind = Order[option];

    public SandboxSculptWheel()
    {
        IconDrawer = DrawIcon;
        _options = new RadialMenu.Item[Order.Length];
        for (var k = 0; k < Order.Length; k++)
            _options[k] = new RadialMenu.Item { Label = DiggingTable.KindNames[(int)Order[k]] is var n ? char.ToUpperInvariant(n[0]) + n[1..] : "", Hint = "RMB puts this back", Icon = (int)Order[k] };
    }

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
