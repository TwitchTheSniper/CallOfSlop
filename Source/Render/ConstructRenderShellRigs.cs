using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Robot shells nobody wears, drawn as the robot a wearer is (the user, 2026-11-20: "the unpossessed shells should look the same as the
/// possessed shells"): while <see cref="ShellRigs"/> is on (Program.DrawSandbox, the scene graph on), a Robot Shell part in view is not
/// drawn from its part's boxes; its frame goes on a list and Game.DrawStandingShells draws a player's rig there, with its frame's size,
/// fitted modules, wear and hopper load (ScenePresentation.DrawShellStand). A shell a Shell Gantry has a module off, a broken one, a ghost
/// and anything past <see cref="ShellRigMax"/> a frame keep the part's own boxes. Fixed arrays: nothing allocates.
/// </summary>
public static partial class ConstructRender
{
    public const int ShellRigMax = 32;

    /// <summary>On for the game's world pass only: tools, previews and camera feeds draw a shell's part as ever.</summary>
    public static bool ShellRigs;

    /// <summary>The shells to draw as rigs this frame: their body, part and the part's frame in the world (feet at its origin, -Z its front).</summary>
    public static readonly ushort[] ShellRigBody = new ushort[ShellRigMax];
    public static readonly int[] ShellRigPart = new int[ShellRigMax];
    public static readonly Matrix4x4[] ShellRigFrame = new Matrix4x4[ShellRigMax];
    public static int ShellRigCount { get; private set; }

    /// <summary>Part <paramref name="i"/> a standing shell drawn as a rig (queued: its boxes aren't drawn), or false: drawn as a part.</summary>
    private static bool QueueShellRig(Renderer gfx, ConstructWorld world, Construct c, int i, in Matrix4x4 seg, in Matrix4x4 body, float opacity)
    {
        ref var p = ref c.Parts[i];
        if (!ShellRigs || opacity < 1f || CameraFeeds.InPass || ShellRigCount >= ShellRigMax || p.Broken || c.GantryHold || c.MergeOf[i] != 0 || c.PartScale[i] > 1
            || world.GantryJobCount > 0 && ConstructGantry.OffSlots(world, c.Id, ConstructConveyor.Clock) != 0)
            return false;
        var k = ShellRigCount++;
        ShellRigBody[k] = c.Id;
        ShellRigPart[k] = i;
        ShellRigFrame[k] = Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * seg * body * Matrix4x4.CreateTranslation(gfx.Origin); // (body is from the render origin; a rig is drawn in the world)
        return true;
    }

    /// <summary>A new frame's list (from <see cref="Draw"/>).</summary>
    private static void ShellRigsBegin() => ShellRigCount = 0;
}
