using System.Numerics;

namespace CallOfSlop;

public static partial class ConstructRender
{
    /// <summary>A flush pod's pocket is opened in the surface round it within this many metres of the eye (further, the lid reads as the floor).</summary>
    public const float PocketNear = 60f;

    /// <summary>
    /// A body's flush shell pods' pockets into this frame's openings (Renderer.Cuts.cs), from <paramref name="body"/> as its parts
    /// are drawn. True when it listed any: the body's boxes are then drawn with the cut mark, so the floor, wall or ceiling a pod
    /// sinks into isn't drawn across its opening. Allocates nothing.
    /// </summary>
    private static bool AddPodCuts(Renderer gfx, Construct c, in Matrix4x4 body, float alpha)
    {
        if (c.PodCount == 0)
            return false;
        var eye = gfx.CameraPosition - gfx.Origin;
        var any = false;
        for (var q = 0; q < c.PodCount; q++)
        {
            int i = c.Pods[q];
            if (i >= c.PartCount)
                continue;
            ref var p = ref c.Parts[i];
            if (p.D.ShellPod is not { Flush: true } pd)
                continue;
            var seg = p.Seg > 0 && p.Seg <= c.JointCount ? SegMatrix(c, p.Seg, alpha) : Matrix4x4.Identity;
            var frame = Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * seg * body;
            if (Vector3.DistanceSquared(frame.Translation, eye) > PocketNear * PocketNear)
                continue;
            ConstructShellPod.Pocket(pd, out var centre, out var half);
            if (!gfx.AddCut(frame, centre, half))
                break; // (the list is full: the rest show the surface over their lids)
            any = true;
        }
        return any;
    }
}
