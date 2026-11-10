using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Drawing walker legs (ConstructWalk.cs, docs/sandbox/GIANTS.md): each leg is posed by analytic IK from its hip on the
/// drawn body to its foot (a coxa out for an insect, the last segment set by the leg's kind, two bones solved by the law of
/// cosines) and drawn as boxes in one batch. On the host the feet are the server's; a client poses them itself on the
/// pose it draws (ConstructPhysics.WalkFeet), so nothing about the feet is on the wire. Nothing allocates.
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
        var now = Stopwatch.GetTimestamp();
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
            var serverFeet = (now - c.WalkStamp) < Stopwatch.Frequency / 4; // (the host's server stepped them: draw those)
            if (!serverFeet)
                PoseFeet(c, pos, rot, now);
            c.WalkDrawStamp = now;
            var fwd = Vector3.Transform(c.SeatForward(), rot);
            var up = Vector3.Transform(Vector3.UnitY, rot);
            for (var k = 0; k < c.LegCount; k++)
            {
                ref var leg = ref c.Legs[k];
                ref var p = ref c.Parts[leg.Part];
                if (p.D.Leg is not { } d)
                    continue;
                var hip = pos + Vector3.Transform(leg.Hip, rot);
                var outward = Vector3.Transform(leg.Rest - leg.Hip, rot);
                outward -= up * Vector3.Dot(outward, up);
                outward = outward.LengthSquared() > 1e-6f ? Vector3.Normalize(outward) : Vector3.UnitX;
                var count = Pose(d, hip, c.FeetSet ? leg.Foot : pos + Vector3.Transform(leg.Rest, rot), up, fwd, outward);
                var colour = d.Colour;
                for (var s = 0; s < count; s++)
                {
                    var w = d.Width * (1 - 0.14f * s);
                    n += Bone(gfx, LegPts[s], LegPts[s + 1], w, outward, origin, colour);
                    n += Knuckle(gfx, LegPts[s], w * 1.18f, rot, origin, d.JointColour);
                }
                // The foot pad.
                var foot = LegPts[count];
                gfx.Draw(Matrix4x4.CreateScale(d.Foot * 2, d.Foot * 0.35f, d.Foot * 2) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(foot + up * (d.Foot * 0.175f) - origin), d.JointColour);
                n++;
            }
        }
        gfx.Relative = false;
        gfx.EndBatch();
        return n;
    }

    /// <summary>A client's feet: stepped on the drawn pose, at the frame's own pace.</summary>
    private static void PoseFeet(Construct c, Vector3 pos, Quaternion rot, long now)
    {
        var dt = c.WalkDrawStamp == 0 ? 0f : MathF.Min(0.1f, (float)((now - c.WalkDrawStamp) / (double)Stopwatch.Frequency));
        var vel = (c.Pos - c.PrevPos) * World.TickRate;
        var turn = Quaternion.Normalize(c.Rot * Quaternion.Conjugate(c.PrevRot));
        if (turn.W < 0)
            turn = Quaternion.Negate(turn);
        var axis = new Vector3(turn.X, turn.Y, turn.Z);
        var angVel = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) * (2 * MathF.Acos(Math.Min(1, turn.W)) * World.TickRate) : Vector3.Zero;
        if (c.LegGroups == 0)
        {
            var tripod = c.LegCount >= 6 && c.LegCount % 2 == 0;
            for (var k = 0; k < c.LegCount; k++)
                c.Legs[k].Group = (byte)(tripod ? k % 2 : k);
            c.LegGroups = (byte)(tripod ? 2 : Math.Max(1, c.LegCount));
        }
        var swing = 0f;
        for (var k = 0; k < c.LegCount; k++)
            swing += c.Parts[c.Legs[k].Part].D.Leg!.StepTime;
        swing /= Math.Max(1, c.LegCount);
        if (dt > 0 || !c.FeetSet)
            ConstructPhysics.WalkFeet(c, World.Query, pos, rot, vel, angVel, swing, dt, false);
    }

    /// <summary>The leg's points, hip to foot, into <see cref="LegPts"/>; returns its segment count.</summary>
    private static int Pose(LegDef d, Vector3 hip, Vector3 foot, Vector3 up, Vector3 fwd, Vector3 outward)
    {
        var segs = d.Segments;
        var n = segs.Length;
        var i = 0;
        LegPts[0] = hip;
        var root = hip;
        if (d.Kind == LegKind.Insect && n >= 3)
        {
            // The coxa: out from the hip towards the foot, level.
            var across = foot - hip;
            across -= up * Vector3.Dot(across, up);
            var dir = across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : outward;
            root = hip + dir * MathF.Min(segs[0], across.Length() * 0.8f);
            LegPts[1] = root;
            i = 1;
        }
        var end = foot;
        if (n - i == 3)
        {
            // The last segment: straight down to the foot, or swept back on its toes.
            var last = segs[n - 1];
            var lean = d.Kind == LegKind.Digitigrade ? Vector3.Normalize(up * 0.75f - fwd * 0.66f) : up;
            end = foot + lean * last;
        }
        var pole = d.Kind == LegKind.Insect ? Vector3.Normalize(up + outward * 0.3f) : fwd;
        var knee = Knee(root, end, segs[i], segs[i + 1], pole);
        LegPts[i + 1] = knee;
        LegPts[i + 2] = end;
        if (n - i == 3)
            LegPts[i + 3] = foot;
        return n;
    }

    /// <summary>Two bones of <paramref name="a"/> and <paramref name="b"/> from <paramref name="root"/> towards <paramref name="target"/>, bending towards <paramref name="pole"/>.</summary>
    private static Vector3 Knee(Vector3 root, Vector3 target, float a, float b, Vector3 pole)
    {
        var to = target - root;
        var d = to.Length();
        var dir = d > 1e-5f ? to / d : -Vector3.UnitY;
        d = Math.Clamp(d, MathF.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        var cos = Math.Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1);
        var side = pole - dir * Vector3.Dot(pole, dir);
        side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitX + Vector3.UnitZ * 0.3f));
        return root + dir * (a * cos) + side * (a * MathF.Sqrt(1 - cos * cos));
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
