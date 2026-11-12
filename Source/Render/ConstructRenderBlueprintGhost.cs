using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The blueprint ghost (GameSandboxBlueprintGhost.cs): the whole blueprint drawn translucent at its true size where it would go, its own
/// colours washed green when the place is free, red when it isn't, and a frame round its bounds (which shows the rest of a blueprint
/// past <see cref="MaxGroupBoxes"/> boxes). Set each frame by the input (Program.SandboxBlueprintGhost.cs); nothing here allocates.
/// </summary>
public static partial class ConstructRender
{
    public static bool BpGhostOn, BpGhostValid;
    public static Matrix4x4 BpGhostBody = Matrix4x4.Identity;
    public static Vector3 BpGhostMin, BpGhostMax;
    public static Matrix4x4[] BpGhostBoxes = [];
    public static Vector3[] BpGhostColours = [];
    public static float[] BpGhostAlpha = [];
    public static int[] BpGhostShapes = [];
    public static int BpGhostCount;

    private static void DrawBlueprintGhost(Renderer gfx, float pulse)
    {
        if (!BpGhostOn)
            return;
        var tint = BpGhostValid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.3f, 0.25f);
        var n = Math.Min(BpGhostCount, Math.Min(BpGhostBoxes.Length, MaxGroupBoxes));
        for (var k = 0; k < n; k++)
            gfx.Draw(BpGhostBoxes[k] * BpGhostBody, Vector3.Lerp(BpGhostColours[k], tint, 0.55f), BpGhostAlpha[k] * pulse, mode: 2, shape: BpGhostShapes[k]);
        Outline(gfx, (BpGhostMin + BpGhostMax) * 0.5f, (BpGhostMax - BpGhostMin) * 0.5f, BpGhostBody, tint, 0.85f);
    }
}
