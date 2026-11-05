using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Nudge tool's modes on the tool wheel's outer ring (ConstructNudge.cs, Program.SandboxNudge.cs): "Rotate snap", "Move" (the
/// default, in the middle of the arc), "Rotate free". The key toggles world or local, the mouse wheel sets the step or
/// the snap angle. This PC's state only: the server is asked for a pose, however it was picked. Nothing allocates per frame.
/// </summary>
public sealed class SandboxNudgeModes : IToolOptions
{
    public enum Option
    {
        RotateSnap,
        Move, // (the default: the middle of the arc)
        RotateFree,
        COUNT,
    }

    private readonly RadialMenu.Item[] _options =
    {
        new() { Label = "Rotate snap", Hint = "arcs, in steps of the snap angle", Icon = 0 },
        new() { Label = "Move", Hint = "the triad's axes", Icon = 1 },
        new() { Label = "Rotate free", Hint = "arcs, any angle", Icon = 2 },
    };

    public RadialMenu.Item[] Options => _options;

    public Option Mode = Option.Move;
    public bool Rotating => Mode != Option.Move;
    public bool Local; // the triad and arcs on the body's axes, not the world's
    public float Step = Nudge.DefaultStep, Angle = Nudge.DefaultAngle;

    public static readonly RadialIcon Icons = DrawIcon;
    RadialIcon? IToolOptions.Icons => Icons;

    public int CurrentOption => (int)Mode;

    public void Choose(int option)
    {
        if (option is >= 0 and < (int)Option.COUNT)
            Mode = (Option)option;
    }

    /// <summary>The mouse wheel: the next step or snap angle up or down the list.</summary>
    public void Scroll(int notches)
    {
        if (Rotating)
            Angle = Next(Nudge.Angles, Angle, notches);
        else
            Step = Next(Nudge.Steps, Step, notches);
    }

    private static float Next(float[] list, float now, int notches)
    {
        var at = Array.IndexOf(list, now);
        return list[Math.Clamp((at < 0 ? 0 : at) + notches, 0, list.Length - 1)];
    }

    private static void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        var mark = new ImColor8(255, 170, 60, 255);
        if (icon == 1)
        {
            // Move: three axes out of a point.
            ImDrawListExtensions.AddLine(dl, P(-0.3f, 0.3f), P(0.8f, 0.3f), col, 0.14f * r);
            ImDrawListExtensions.AddLine(dl, P(-0.3f, 0.3f), P(-0.3f, -0.8f), col, 0.14f * r);
            ImDrawListExtensions.AddLine(dl, P(-0.3f, 0.3f), P(-0.75f, 0.75f), mark, 0.14f * r);
            return;
        }
        // Rotate: an arc; snapped, with ticks on it.
        var a = P(-0.75f, 0.2f);
        var c1 = P(-0.65f, -0.8f);
        var c2 = P(0.65f, -0.8f);
        var b = P(0.75f, 0.2f);
        ImDrawListExtensions.PathClear(dl);
        ImDrawListExtensions.PathLineTo(dl, in a);
        ImDrawListExtensions.PathBezierCubicCurveTo(dl, in c1, in c2, in b, 12);
        ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, 0.14f * r);
        ImDrawListExtensions.AddLine(dl, P(0.45f, 0.1f), P(0.75f, 0.45f), mark, 0.12f * r);
        if (icon == 0)
        {
            ImDrawListExtensions.AddLine(dl, P(-0.05f, -0.75f), P(-0.05f, -0.3f), mark, 0.1f * r);
            ImDrawListExtensions.AddLine(dl, P(-0.6f, -0.35f), P(-0.3f, -0.1f), mark, 0.1f * r);
        }
        _ = bg;
    }
}
