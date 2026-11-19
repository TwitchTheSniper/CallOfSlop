using System.Numerics;

namespace CallOfSlop;

public static partial class ConstructRender
{
    /// <summary>A flush pod's pocket is opened in the surface round it within this many metres of the eye (further, the lid reads as the floor).</summary>
    public const float PocketNear = 60f;

    // This frame's chosen pockets (the nearest Renderer.CutMax to the eye): body, part and distance; then each body's run of openings.
    private static readonly Construct?[] _pocketBody = new Construct?[Renderer.CutMax];
    private static readonly int[] _pocketPart = new int[Renderer.CutMax];
    private static readonly float[] _pocketDist = new float[Renderer.CutMax];
    private static readonly Construct?[] _cutBody = new Construct?[Renderer.CutMax];
    private static readonly int[] _cutCode = new int[Renderer.CutMax];
    private static int _pocketCount, _cutBodies;

    /// <summary>The cut code of a body's own openings this frame (0: none; Renderer.CutRun).</summary>
    private static int CutCodeOf(Construct c)
    {
        if (c.PodCount == 0)
            return 0;
        for (var k = 0; k < _cutBodies; k++)
            if (ReferenceEquals(_cutBody[k], c))
                return _cutCode[k];
        return 0;
    }

    /// <summary>
    /// Before the bodies are drawn: the flush shell pods' pockets nearest the eye (at most <see cref="Renderer.CutMax"/>, within
    /// <see cref="PocketNear"/>) as this frame's openings (Renderer.Cuts.cs), grouped body by body from each body's frame as it is
    /// drawn, so its boxes are cut by its own pockets alone (<see cref="CutCodeOf"/>). Allocates nothing.
    /// </summary>
    private static void GatherPocketCuts(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        gfx.ClearCuts();
        _pocketCount = _cutBodies = 0;
        Array.Clear(_pocketBody);
        Array.Clear(_cutBody);
        if (!gfx.CanCut)
            return;
        var eye = gfx.CameraPosition;
        foreach (var c in world.Bodies)
        {
            if (c.PodCount == 0 || (AwayOnly != 0 ? c.Id != AwayOnly : c.SpaceAway != 0))
                continue;
            if (Vector3.DistanceSquared(c.WorldCom, eye) > (PocketNear + c.Radius) * (PocketNear + c.Radius))
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            for (var q = 0; q < c.PodCount; q++)
            {
                int i = c.Pods[q];
                if (i >= c.PartCount || c.Parts[i].D.ShellPod is not { Flush: true })
                    continue;
                var d = Vector3.Distance(pos + Vector3.Transform(c.Parts[i].Pos, rot), eye);
                if (d > PocketNear)
                    continue;
                // Kept in order of distance: insert, dropping the farthest when full.
                var at = _pocketCount;
                while (at > 0 && _pocketDist[at - 1] > d)
                    at--;
                if (at >= Renderer.CutMax)
                    continue;
                var last = Math.Min(_pocketCount, Renderer.CutMax - 1);
                for (var k = last; k > at; k--)
                    (_pocketBody[k], _pocketPart[k], _pocketDist[k]) = (_pocketBody[k - 1], _pocketPart[k - 1], _pocketDist[k - 1]);
                (_pocketBody[at], _pocketPart[at], _pocketDist[at]) = (c, i, d);
                _pocketCount = Math.Min(_pocketCount + 1, Renderer.CutMax);
            }
        }
        // Body by body: each one's pockets as one run of openings.
        var origin = gfx.Origin;
        for (var k = 0; k < _pocketCount; k++)
        {
            var c = _pocketBody[k]!;
            if (CutCodeOf(c) != 0 || _cutBodies >= Renderer.CutMax)
                continue; // (done with an earlier pocket of it)
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            var body = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos - origin); // (as Draw has it)
            var first = gfx.CutCount;
            for (var j = k; j < _pocketCount; j++)
            {
                if (!ReferenceEquals(_pocketBody[j], c))
                    continue;
                ref var p = ref c.Parts[_pocketPart[j]];
                var seg = p.Seg > 0 && p.Seg <= c.JointCount ? SegMatrix(c, p.Seg, blendTicks ? alpha : 1f) : Matrix4x4.Identity;
                var frame = Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * seg * body;
                ConstructShellPod.Pocket(p.D.ShellPod!, out var centre, out var half);
                gfx.AddCut(frame, centre, half);
            }
            _cutBody[_cutBodies] = c;
            _cutCode[_cutBodies++] = Renderer.CutRun(first, gfx.CutCount - first);
        }
    }
}
