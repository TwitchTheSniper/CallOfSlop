using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The hands slot's outer ring on the tool wheel (docs/sandbox/CONSTRUCTS.md, "Hands"): the two guns either side of hands (the
/// default, so in the middle of the arc: straight out from the slot). Letting go on the slot itself is always hands, so a gun
/// taken out from here never changes what that slot means; the marker shows what is in the hands now. Nothing allocates per frame.
/// </summary>
public sealed class SandboxHandsRing : IToolOptions
{
    public const int PrimaryGun = 0, HandsOption = 1, SecondaryGun = 2;

    private readonly RadialMenu.Item[] _options =
    {
        new() { Label = "Primary gun", Icon = PrimaryGun },
        new() { Label = "Hands", Hint = "pick up, drop", Icon = HandsOption },
        new() { Label = "Secondary gun", Icon = SecondaryGun },
    };

    private int _current = HandsOption;

    public RadialMenu.Item[] Options => _options;

    public static readonly RadialIcon IconDrawer = DrawIcon;
    RadialIcon? IToolOptions.Icons => IconDrawer;
    public int CurrentOption => _current;

    public void Choose(int option)
    {
        // (nothing to keep: the wheel takes the pick through GunOf)
    }

    /// <summary>The ring's marker, every frame the wheel runs: the gun in the hands, else the hands.</summary>
    public void Shown(ConstructTool tool, int activeSlot) => _current = tool != ConstructTool.None ? HandsOption : activeSlot == 1 ? SecondaryGun : PrimaryGun;

    /// <summary>The weapon slot option <paramref name="option"/> takes out, or null for hands (or no option: the slot itself).</summary>
    public int? GunOf(int option) => option switch { PrimaryGun => 0, SecondaryGun => 1, _ => null };

    public void SetKeys(string primary, string secondary)
    {
        _options[PrimaryGun].Hint = $"key {primary}";
        _options[SecondaryGun].Hint = $"key {secondary}";
    }

    private static void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        if (icon == HandsOption)
            DrawHand(dl, c, r, col);
        else
            DrawPistol(dl, c, r, col);
    }

    /// <summary>An open hand from boxes: the palm, four fingers and the thumb.</summary>
    public static void DrawHand(ImDrawListPtr dl, float2 c, float r, ImColor8 col)
    {
        void Box(float x0, float y0, float x1, float y1) =>
            ImDrawListExtensions.AddRectFilled(dl, new float2(c.X + x0 * r, c.Y + y0 * r), new float2(c.X + x1 * r, c.Y + y1 * r), col, 0.06f * r, ImDrawFlags.None);
        Box(-0.4f, 0f, 0.42f, 0.8f);
        Box(-0.4f, -0.65f, -0.22f, 0.05f);
        Box(-0.13f, -0.85f, 0.05f, 0.05f);
        Box(0.14f, -0.8f, 0.32f, 0.05f);
        Box(0.41f, -0.55f, 0.58f, 0.1f);
        Box(-0.8f, 0f, -0.4f, 0.22f);
    }

    /// <summary>A pistol from boxes.</summary>
    private static void DrawPistol(ImDrawListPtr dl, float2 c, float r, ImColor8 col)
    {
        void Box(float x0, float y0, float x1, float y1) =>
            ImDrawListExtensions.AddRectFilled(dl, new float2(c.X + x0 * r, c.Y + y0 * r), new float2(c.X + x1 * r, c.Y + y1 * r), col, 0.05f * r, ImDrawFlags.None);
        Box(-0.75f, -0.4f, 0.7f, -0.1f);
        Box(-0.6f, -0.1f, -0.2f, 0.7f);
        Box(-0.2f, -0.1f, 0.05f, 0.15f);
        Box(0.55f, -0.5f, 0.65f, -0.4f);
    }
}
