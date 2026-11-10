using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Drawing walker legs (ConstructWalk.cs, docs/sandbox/GIANTS.md): each leg is posed by analytic IK from its hip on the
/// drawn body to its foot (a coxa out for an insect, the last segment set by the leg's kind, two bones solved by the law of
/// cosines) and drawn as boxes in one batch. The feet are always the server's: the host's own, a client's from the poses at
/// the tick the body is drawn (protocol 229, ConstructNet.SampleFeet). Nothing allocates.
/// </summary>
public static partial class ConstructRender
{
    private static readonly Vector3[] LegPts = new Vector3[LegDef.MaxSegments + 1];

    private static int DrawLegs(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        var n = 0;
        var cam = gfx.CameraPosition;
        var cull = !gfx.ViewProj.IsIdentity;
        var origin = gfx.Origin;
        gfx.BeginBatch();
        gfx.Relative = true;
        foreach (var c in world.Bodies)
        {
            if (c.LegCount == 0 || (AwayOnly != 0 ? c.Id != AwayOnly : c.SpaceAway != 0))
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            if (cull && CullLevel(cam, c, pos, rot) == 0)
                continue;
            var up = Vector3.Transform(Vector3.UnitY, rot);
            for (var k = 0; k < c.LegCount; k++)
            {
                ref var leg = ref c.Legs[k];
                ref var p = ref c.Parts[leg.Part];
                if (p.D.Leg is not { } d)
                    continue;
                var count = ConstructPhysics.LegPose(c, k, pos, rot, LegPts, out var outward);
                var colour = d.Colour;
                for (var s = 0; s < count; s++)
                {
                    var w = ConstructPhysics.LegWidth(d, s);
                    n += Bone(gfx, LegPts[s], LegPts[s + 1], w, outward, origin, colour);
                    n += Knuckle(gfx, LegPts[s], w * 1.18f, rot, origin, d.JointColour);
                }
                // The foot pad.
                var foot = LegPts[count];
                gfx.Draw(Matrix4x4.CreateScale(d.Foot * 2, d.Foot * 0.35f, d.Foot * 2) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(foot + up * (d.Foot * 0.175f) - origin), d.JointColour);
                n++;
                if (c.RiderCount > 0)
                    n += DrawRiders(gfx, world, c, k, count, pos, rot, origin, alpha);
            }
        }
        gfx.Relative = false;
        gfx.EndBatch();
        return n;
    }

    private static bool _drawingRiders;

    /// <summary>The parts riding leg <paramref name="k"/>'s segments (ConstructWalkRiders.cs), each segment's in its frame as drawn.</summary>
    private static int DrawRiders(Renderer gfx, ConstructWorld world, Construct c, int k, int segs, Vector3 pos, Quaternion rot, Vector3 origin, float alpha)
    {
        var n = 0;
        var leg = c.Legs[k].Part + 1;
        _drawingRiders = true;
        try
        {
            for (var s = 0; s < segs; s++)
            {
                var framed = false;
                var frame = Matrix4x4.Identity;
                for (var i = 0; i < c.PartCount; i++)
                {
                    if (c.RideOf[i] != leg || c.RideSeg[i] != s)
                        continue;
                    if (!framed)
                    {
                        ConstructPhysics.RideFrame(c, k, s, pos, rot, out var q, out var t);
                        frame = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t - origin);
                        framed = true;
                    }
                    n += DrawPart(gfx, world, c, i, frame, alpha, 1f);
                }
            }
        }
        finally
        {
            _drawingRiders = false;
        }
        return n;
    }

    private static int Bone(Renderer gfx, Vector3 a, Vector3 b, float width, Vector3 across, Vector3 origin, Vector3 colour)
    {
        var len = Vector3.Distance(a, b);
        if (len < 1e-4f)
            return 0;
        var z = (b - a) / len;
        var x = across - z * Vector3.Dot(across, z);
        x = x.LengthSquared() > 1e-6f ? Vector3.Normalize(x) : Vector3.Normalize(Vector3.Cross(z, Vector3.UnitY + Vector3.UnitX * 0.1f));
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        gfx.Draw(Matrix4x4.CreateScale(width, width * 0.92f, len) * basis * Matrix4x4.CreateTranslation((a + b) * 0.5f - origin), colour);
        return 1;
    }

    private static int Knuckle(Renderer gfx, Vector3 at, float size, Quaternion rot, Vector3 origin, Vector3 colour)
    {
        gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(at - origin), colour);
        return 1;
    }
}
