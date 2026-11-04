using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Drawing the tracks (ConstructTracks.cs): the links are boxes running round the loop at the track's real speed (instanced,
/// one batch), over a plain band, and from far off the band alone. The top runs sag by the track's tension. A Track part is drawn
/// the same way: its two ends make its loop. Worked out from the wheels each frame; nothing allocates.
/// </summary>
public static partial class ConstructRender
{
    private const int MaxLinksDrawn = 900; // on one track: past it the links drawn get longer
    private const float LinkLodPixels = 700f; // links show out to this many link lengths away
    private static readonly Vector3 TrackPad = new(0.2f, 0.205f, 0.215f), TrackBand = new(0.085f, 0.088f, 0.095f);

    // The track tool's ghost (Program.SandboxTracks.cs fills it): the wheels picked, and whether they make a track.
    public static Construct? TrackGhostBody;
    public static readonly ushort[] TrackGhostParts = new ushort[Construct.MaxTrackWheels + 1];
    public static int TrackGhostCount;
    public static bool TrackGhostOk;

    /// <summary>Links drawn last frame (a hand check's count).</summary>
    public static int LastTrackLinks { get; private set; }

    private static readonly LoopWheel[] LoopWs = new LoopWheel[Construct.MaxTrackWheels];
    private static readonly LoopRun[] LoopRuns = new LoopRun[Construct.MaxTrackWheels * 2 + 2];
    private static readonly float[] LiftU = new float[ConstructTracks.MaxStations + 2], LiftUp = new float[ConstructTracks.MaxStations + 2];
    private static int LiftCount;

    private static int DrawTracks(Renderer gfx, ConstructWorld world, float alpha, bool blendTicks)
    {
        var n = 0;
        LastTrackLinks = 0;
        var cam = gfx.CameraPosition;
        var cull = !gfx.ViewProj.IsIdentity;
        var origin = gfx.Origin;
        var alphaSeg = blendTicks ? alpha : 1f;
        gfx.BeginBatch();
        gfx.Relative = true;
        foreach (var c in world.Bodies)
        {
            if (c.WheelCount == 0 && c.TrackCount == 0 || (AwayOnly != 0 ? c.Id != AwayOnly : c.SpaceAway != 0))
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            if (cull && CullLevel(cam, c, pos, rot) != 1)
                continue;
            var body = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos - origin);
            var forward = c.SeatForward();
            var camBody = Vector3.Transform(cam - pos, Quaternion.Conjugate(rot));
            for (var k = 0; k < c.TrackCount; k++)
            {
                ref var t = ref c.Tracks[k];
                if (!t.Live)
                    continue;
                var wheels = ConstructTracks.LiveWheels(c, t, LoopWs);
                if (wheels < 2)
                    continue;
                var runs = TrackLoop.Wrap(LoopWs.AsSpan(0, wheels), LoopRuns);
                var frame = t.Seg == 0 ? body : SegMatrix(c, t.Seg, alphaSeg) * body;
                int lead = c.TrackWheel[t.Start];
                var travelled = lead < c.WheelCount ? c.Wheels[lead].Spin * c.Wheels[lead].Radius : 0;
                var mid = (t.Min + t.Max) * 0.5f;
                TrackLift(c, k, wheels, runs, ConstructTracks.Thickness(t.LinkLength));
                n += DrawLoop(gfx, wheels, runs, t.Along, t.Axle, t.Plane, t.Width, t.LinkLength, t.Tension, -MathF.Sign(Vector3.Dot(t.Along, forward)) * travelled, frame, Vector3.Distance(camBody, mid));
            }
            // A Track part: its own two ends.
            for (var k = 0; k < c.WheelCount; k++)
            {
                ref var w = ref c.Wheels[k];
                if (!w.Track || w.Loop != 0 || w.Lead || k > 0 && c.Wheels[k - 1].Part == w.Part)
                    continue;
                var d = c.Parts[w.Part].D;
                c.PartBounds(w.Part, out var mn, out var mx);
                var centre = (mn + mx) * 0.5f;
                var along = Vector3.Cross(w.Axle, Vector3.UnitY);
                var reach = d.TrackLength * 0.5f - d.WheelRadius;
                LoopWs[0] = new LoopWheel { U = Vector3.Dot(centre, along) - reach, V = centre.Y, R = d.WheelRadius };
                LoopWs[1] = new LoopWheel { U = Vector3.Dot(centre, along) + reach, V = centre.Y, R = d.WheelRadius };
                var runs = TrackLoop.Wrap(LoopWs.AsSpan(0, 2), LoopRuns);
                var seg = w.Seg == 0 ? body : SegMatrix(c, w.Seg, alphaSeg) * body;
                var frame = ConstructTracks.UnitPose(c, w.Part, k) * seg;
                var width = MathF.Abs(Vector3.Dot(mx - mn, w.Axle)) * 0.98f;
                LiftCount = 0;
                n += DrawLoop(gfx, 2, runs, along, w.Axle, Vector3.Dot(centre, w.Axle), width, ConstructTracks.UnitLink(d.WheelRadius), 0.8f, -MathF.Sign(Vector3.Dot(along, forward)) * w.Spin * w.Radius, frame, Vector3.Distance(camBody, centre));
            }
        }
        gfx.Relative = false;
        gfx.EndBatch();
        if (TrackGhostCount > 0 && TrackGhostBody is { } gc)
            n += DrawTrackGhost(gfx, gc, alpha, blendTicks);
        return n;
    }

    /// <summary>
    /// What the ground does to a track's lower runs between its wheels: at each contact ray along it, how far the ground stands above the
    /// straight run (the hull alone would cut through a rise). The run is drawn up over those points.
    /// </summary>
    private static void TrackLift(Construct c, int track, int wheels, int runs, float thick)
    {
        LiftCount = 0;
        ref var t = ref c.Tracks[track];
        var ws = LoopWs.AsSpan(0, wheels);
        var loop = LoopRuns.AsSpan(0, runs);
        for (var k = 0; k < c.WheelCount && LiftCount < LiftU.Length; k++)
        {
            ref var w = ref c.Wheels[k];
            if (w.Loop != track + 1 || !w.Station)
                continue;
            var u = Vector3.Dot(w.Centre, t.Along);
            var run = TrackLoop.Lower(ws, loop, u);
            if (!float.IsFinite(run))
                continue;
            var ground = w.Centre.Y - w.Travel - w.Radius - w.Band + w.Compression;
            LiftU[LiftCount] = u;
            LiftUp[LiftCount++] = w.Touching ? MathF.Max(0, ground - (run - thick)) : 0;
        }
    }

    /// <summary>How far a lower run is lifted at <paramref name="u"/>, by the contact rays either side of it.</summary>
    private static float LiftAt(float u)
    {
        for (var k = 0; k + 1 < LiftCount; k++)
        {
            float a = LiftU[k], b = LiftU[k + 1];
            if (u >= MathF.Min(a, b) && u <= MathF.Max(a, b))
                return float.Lerp(LiftUp[k], LiftUp[k + 1], MathF.Abs(b - a) < 1e-4f ? 0 : (u - a) / (b - a));
        }
        return 0;
    }

    /// <summary>How much a run hangs at <paramref name="f"/> along it: a top run's sag, or a lower run lifted by the ground, fading out at its wheels.</summary>
    private static float RunDip(in LoopRun run, float f, float sag, float rUnder)
    {
        var dip = 4 * sag * f * (1 - f);
        if (LiftCount > 0 && run.Bx > run.Ax)
            dip += MathF.Min(rUnder * 0.8f, LiftAt(run.Ax + (run.Bx - run.Ax) * f)) * MathF.Min(1, 8 * MathF.Min(f, 1 - f));
        return dip;
    }

    /// <summary>How much a top run hangs in its middle.</summary>
    private static float SagOf(in LoopRun run, float tension, float rUnder)
    {
        var ny = -(run.Bx - run.Ax) / MathF.Max(1e-4f, run.Len); // (the outward normal's height: a top run's points up)
        if (ny < 0.5f || run.Len < 0.2f)
            return 0;
        return MathF.Min(rUnder * 0.7f, run.Len * (0.012f + ConstructTracks.SlackSag * (1 - tension)));
    }

    private static int DrawLoop(Renderer gfx, int wheels, int runCount, Vector3 along, Vector3 axle, float plane, float width, float link, float tension, float travelled, in Matrix4x4 frame, float distance)
    {
        var ws = LoopWs.AsSpan(0, wheels);
        var runs = LoopRuns.AsSpan(0, runCount);
        var thick = ConstructTracks.Thickness(link);
        var n = 0;
        // The band: chords round each wheel, the straights, a sagging top run in pieces.
        foreach (ref readonly var run in runs)
        {
            ref readonly var w = ref ws[run.Wheel];
            var pieces = Math.Clamp((int)(run.Turn / 0.45f), 1, 12);
            for (var p = 0; p < pieces; p++)
            {
                var a0 = run.Leave - run.Turn + run.Turn * p / pieces;
                var a1 = run.Leave - run.Turn + run.Turn * (p + 1) / pieces;
                var (s0, c0) = MathF.SinCos(a0);
                var (s1, c1) = MathF.SinCos(a1);
                var r = w.R + thick * 0.35f;
                n += Chord(gfx, w.U + r * s0, w.V - r * c0, w.U + r * s1, w.V - r * c1, along, axle, plane, width * 0.94f, thick * 0.7f, frame, TrackBand);
            }
            if (run.Len > 1e-3f)
            {
                var sag = SagOf(run, tension, w.R);
                var steps = sag > 0 || LiftCount > 0 && run.Bx > run.Ax ? 8 : 1;
                var (ex, ey) = ((run.Bx - run.Ax) / run.Len, (run.By - run.Ay) / run.Len);
                var (nx, ny) = (ey, -ex);
                var oldX = run.Ax + nx * thick * 0.35f;
                var oldY = run.Ay + ny * thick * 0.35f;
                for (var p = 1; p <= steps; p++)
                {
                    var f = p / (float)steps;
                    var dip = RunDip(run, f, sag, w.R);
                    var x = run.Ax + (run.Bx - run.Ax) * f + nx * (thick * 0.35f - dip);
                    var y = run.Ay + (run.By - run.Ay) * f + ny * (thick * 0.35f - dip);
                    n += Chord(gfx, oldX, oldY, x, y, along, axle, plane, width * 0.94f, thick * 0.7f, frame, TrackBand);
                    (oldX, oldY) = (x, y);
                }
            }
        }
        if (distance > LinkLodPixels * link)
            return n;
        // The links: spaced evenly by the loop's length, sliding round it with the distance run.
        var total = TrackLoop.Length(ws, runs);
        var count = Math.Clamp((int)MathF.Round(total / link), 4, MaxLinksDrawn);
        var pitch = total / count;
        var offset = travelled % pitch;
        if (offset < 0)
            offset += pitch;
        var padLength = pitch * 0.74f;
        var at = offset; // (the next link's distance round the loop)
        var walked = 0f;
        foreach (ref readonly var run in runs)
        {
            ref readonly var w = ref ws[run.Wheel];
            var arc = run.Turn * w.R;
            var radius = w.R + thick * 0.5f;
            while (at < walked + arc)
            {
                var a = run.Leave - run.Turn + (at - walked) / MathF.Max(1e-4f, w.R);
                var (s, co) = MathF.SinCos(a);
                n += Pad(gfx, w.U + radius * s, w.V - radius * co, co, s, along, axle, plane, width, thick, padLength, frame);
                at += pitch;
            }
            walked += arc;
            if (run.Len > 1e-3f)
            {
                var sag = SagOf(run, tension, w.R);
                var (ex, ey) = ((run.Bx - run.Ax) / run.Len, (run.By - run.Ay) / run.Len);
                var (nx, ny) = (ey, -ex);
                while (at < walked + run.Len)
                {
                    var along0 = at - walked;
                    var f = along0 / run.Len;
                    var dip = RunDip(run, f, sag, w.R);
                    var slope = (RunDip(run, MathF.Min(1, f + 0.02f), sag, w.R) - RunDip(run, MathF.Max(0, f - 0.02f), sag, w.R)) / (0.04f * run.Len); // (a link tilts with the curve)
                    var (tx, ty) = (ex - nx * slope, ey - ny * slope);
                    var tl = MathF.Sqrt(tx * tx + ty * ty);
                    n += Pad(gfx, run.Ax + ex * along0 + nx * (thick * 0.5f - dip), run.Ay + ey * along0 + ny * (thick * 0.5f - dip), tx / tl, ty / tl, along, axle, plane, width, thick, padLength, frame);
                    at += pitch;
                }
                walked += run.Len;
            }
        }
        return n;
    }

    private static int Pad(Renderer gfx, float u, float v, float tu, float tv, Vector3 along, Vector3 axle, float plane, float width, float thick, float length, in Matrix4x4 frame)
    {
        var z = along * tu + Vector3.UnitY * tv;
        var y = Vector3.Cross(z, axle);
        var basis = new Matrix4x4(axle.X, axle.Y, axle.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        var at = along * u + axle * plane + new Vector3(0, v, 0);
        gfx.Draw(Matrix4x4.CreateScale(width, thick, length) * basis * Matrix4x4.CreateTranslation(at) * frame, TrackPad);
        LastTrackLinks++;
        return 1;
    }

    private static int Chord(Renderer gfx, float u0, float v0, float u1, float v1, Vector3 along, Vector3 axle, float plane, float width, float thick, in Matrix4x4 frame, Vector3 colour, float opacity = 1f, int mode = 0)
    {
        var (du, dv) = (u1 - u0, v1 - v0);
        var len = MathF.Sqrt(du * du + dv * dv);
        if (len < 1e-4f)
            return 0;
        var z = along * (du / len) + Vector3.UnitY * (dv / len);
        var y = Vector3.Cross(z, axle);
        var basis = new Matrix4x4(axle.X, axle.Y, axle.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        var at = along * ((u0 + u1) * 0.5f) + axle * plane + new Vector3(0, (v0 + v1) * 0.5f, 0);
        gfx.Draw(Matrix4x4.CreateScale(width, thick, len * 1.04f) * basis * Matrix4x4.CreateTranslation(at) * frame, colour, opacity, mode);
        return 1;
    }

    /// <summary>The picks' loop in the plane of the first wheel. Its wheels' count (0: none that fit).</summary>
    private static int GhostWheels(Construct c, out Vector3 along, out Vector3 axle, out float plane)
    {
        along = Vector3.UnitZ;
        axle = Vector3.UnitX;
        plane = 0;
        var n = 0;
        for (var k = 0; k < TrackGhostCount && n < LoopWs.Length; k++)
        {
            int i = TrackGhostParts[k];
            if (i >= c.PartCount || c.DefOf(i).Role != PartRole.Wheel)
                continue;
            if (n == 0 && ConstructTracks.AxleOf(c, i, out axle))
                along = Vector3.Cross(axle, Vector3.UnitY);
            c.PartBounds(i, out var mn, out var mx);
            var centre = (mn + mx) * 0.5f;
            if (n == 0)
                plane = Vector3.Dot(centre, axle);
            LoopWs[n++] = new LoopWheel { U = Vector3.Dot(centre, along), V = centre.Y, R = c.DefOf(i).WheelRadius };
        }
        return n;
    }

    /// <summary>How long the ghost's loop is (the HUD).</summary>
    public static float TrackGhostLength(Construct c)
    {
        var n = GhostWheels(c, out _, out _, out _);
        if (n < 2)
            return 0;
        var runs = TrackLoop.Wrap(LoopWs.AsSpan(0, n), LoopRuns);
        return TrackLoop.Length(LoopWs.AsSpan(0, n), LoopRuns.AsSpan(0, runs));
    }

    private static int DrawTrackGhost(Renderer gfx, Construct c, float alpha, bool blendTicks)
    {
        var count = GhostWheels(c, out var along, out var axle, out var plane);
        if (count == 0)
            return 0;
        var wheels = LoopWs.AsSpan(0, count);
        var runs = TrackLoop.Wrap(wheels, LoopRuns);
        var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
        var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
        var frame = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var colour = TrackGhostOk ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.25f, 0.2f);
        var width = 0f;
        for (var k = 0; k < TrackGhostCount; k++)
            if (TrackGhostParts[k] < c.PartCount && c.DefOf(TrackGhostParts[k]).Role == PartRole.Wheel)
                width = MathF.Max(width, ConstructTracks.WheelWidth(c, TrackGhostParts[k], axle));
        width = MathF.Max(0.1f, width * 1.06f);
        var thick = Math.Clamp(width * 0.12f, 0.03f, 0.6f);
        var n = 0;
        foreach (ref readonly var run in LoopRuns.AsSpan(0, runs))
        {
            ref readonly var w = ref wheels[run.Wheel];
            var pieces = Math.Clamp((int)(run.Turn / 0.3f), 1, 21);
            for (var p = 0; p < pieces; p++)
            {
                var (s0, c0) = MathF.SinCos(run.Leave - run.Turn + run.Turn * p / pieces);
                var (s1, c1) = MathF.SinCos(run.Leave - run.Turn + run.Turn * (p + 1) / pieces);
                var r = w.R + thick;
                n += Chord(gfx, w.U + r * s0, w.V - r * c0, w.U + r * s1, w.V - r * c1, along, axle, plane, width, thick, frame, colour, 0.5f, 2);
            }
            if (run.Len > 1e-3f)
            {
                var (ex, ey) = ((run.Bx - run.Ax) / run.Len, (run.By - run.Ay) / run.Len);
                n += Chord(gfx, run.Ax + ey * thick, run.Ay - ex * thick, run.Bx + ey * thick, run.By - ex * thick, along, axle, plane, width, thick, frame, colour, 0.5f, 2);
            }
        }
        return n;
    }
}
