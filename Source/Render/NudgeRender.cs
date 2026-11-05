using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Nudge tool drawn (Program.SandboxNudge.cs sets the state each frame): the triad's three arrows, or the three arcs, at the
/// pivot, over everything (the pivot is often inside the construct), X red, Y green, Z blue, the one under the crosshair or
/// dragged lit. Nothing allocates.
/// </summary>
public static class NudgeRender
{
    public const int ArcPoints = 48;
    public static bool On, Arcs;
    public static Vector3 At;
    public static float Size;
    public static int Hot = -1; // the axis under the crosshair, or dragged
    public static readonly Vector3[] Axis = new Vector3[3], Ref = new Vector3[3];

    private static readonly Vector3[] Colours = [new(0.95f, 0.25f, 0.2f), new(0.3f, 0.9f, 0.35f), new(0.3f, 0.5f, 1f)];
    private static readonly Vector3 HotColour = new(1f, 0.9f, 0.35f);

    public static void Clear()
    {
        On = false;
        Hot = -1;
    }

    /// <summary>A point of axis <paramref name="k"/>'s arc: <paramref name="i"/> of <see cref="ArcPoints"/> round its ring.</summary>
    public static Vector3 ArcPoint(int k, int i)
    {
        var th = i * MathF.Tau / ArcPoints;
        return At + (Ref[k] * MathF.Cos(th) + Vector3.Cross(Axis[k], Ref[k]) * MathF.Sin(th)) * Size;
    }

    public static void Draw(Renderer gfx)
    {
        if (!On)
            return;
        gfx.SetDepthTest(false);
        var thick = Size * 0.025f;
        for (var k = 0; k < 3; k++)
        {
            var hot = k == Hot;
            var col = hot ? HotColour : Colours[k];
            var a = hot ? 1f : 0.85f;
            if (Arcs)
                for (var i = 0; i < ArcPoints; i++)
                    SocketWeldRender.Bar(gfx, ArcPoint(k, i), ArcPoint(k, i + 1), thick * (hot ? 1.6f : 1f), col, a);
            else
            {
                var tip = At + Axis[k] * Size;
                SocketWeldRender.Bar(gfx, At, tip, thick * (hot ? 1.6f : 1f), col, a);
                SocketWeldRender.Bar(gfx, tip, tip + Axis[k] * Size * 0.16f, thick * 3.2f, col, a);
            }
        }
        gfx.Draw(Matrix4x4.CreateScale(thick * 2.4f) * Matrix4x4.CreateTranslation(At), new Vector3(0.95f), 0.9f, mode: 2);
        gfx.SetDepthTest(true);
    }
}
