using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>A second world pass for a watched camera (CameraFeeds.cs), and the pilot's view from a drone.</summary>
public static partial class Program
{
    /// <summary>
    /// One camera's picture, before the frame's own pass, with this frame's state and none of its own: the map, the sky and the
    /// constructs. No foliage, decals, glass or panels, and players are plain boxes: their animation is per frame, and at this size they read the same.
    /// </summary>
    private static void DrawCameraFeed(double now)
    {
        if (_session is not { SandboxOn: true } g || !CameraFeeds.Next(now, out var bodyId, out var part, out var layer))
            return;
        if (g.Constructs.Get(bodyId) is not { } c || part >= c.PartCount || !c.DefOf(part).IsCamera)
        {
            CameraFeeds.Forget(layer);
            return;
        }
        if (!CameraFeeds.Bind(layer, out var was))
            return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var perf = Perf.On;
        Perf.On = false; // (the F9 window's passes are the frame's own)
        CameraFeeds.InPass = true;
        var map = World.Map;
        Gfx.BeginPass(CameraFeeds.Width, CameraFeeds.Height, map.Sky);
        ConstructRemote.CameraPose(c, part, g.TickAlpha, g.IsServer, out var eye, out var rot);
        var fwd = Vector3.Transform(-Vector3.UnitZ, rot);
        var up = Vector3.Transform(Vector3.UnitY, rot);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(c.DefOf(part).CameraFov * MathF.PI / 180f, CameraFeeds.Width / (float)CameraFeeds.Height, 0.05f, MathF.Min(map.FarPlane, 800));
        Gfx.SetCameraAt(eye, Matrix4x4.CreateLookAt(Vector3.Zero, fwd, up), proj);
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        DrawArena();
        ConstructRender.Now = now;
        ConstructRender.Draw(Gfx, g.Constructs, g.TickAlpha, blendTicks: g.IsServer);
        foreach (var p in g.Players.Values)
            if (p.Alive)
            {
                var feet = p.RenderPosition;
                Gfx.Draw(Matrix4x4.CreateScale(0.5f, 1.4f, 0.32f) * Matrix4x4.CreateTranslation(feet + new Vector3(0, 0.7f, 0)), p.Color);
                Gfx.Draw(Matrix4x4.CreateScale(0.26f) * Matrix4x4.CreateTranslation(feet + new Vector3(0, 1.55f, 0)), new Vector3(0.8f, 0.65f, 0.55f));
            }
        Gfx.SetFog(map.Sky, 0, 0);
        CameraFeeds.InPass = false;
        Perf.On = perf;
        CameraFeeds.Unbind(was);
        CameraFeeds.MarkDrawn(layer, now);
        CameraFeeds.Passes++;
        CameraFeeds.LastMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>
    /// Seated at a console with a drone on the link, the view is the drone's: from its first camera, or from behind it with none. The mouse still looks
    /// within the seat's limits, turned onto the drone, so it is one view and no extra pass. Third person (its key) gives the seat back.
    /// </summary>
    private static void PilotCamera(ref Vector3 eye, ref Vector3 fwd, ref Vector3 up)
    {
        if (_session is not { SandboxOn: true } g || g.Local is not { Alive: true } me || !g.SandboxHands.Hand(me.Id).Seated)
            return;
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (ConstructRemote.Piloted(g.Constructs, me.Id) is not { } d || g.Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount)
            return;
        ConstructRender.SeatPose(c, h.SeatPart, g.TickAlpha, g.IsServer, out var seat, out _);
        var look = Vector3.Transform(fwd, Quaternion.Conjugate(seat));
        if (d.CameraParts.Length > 0 && d.CameraParts[0] < d.PartCount)
        {
            ConstructRemote.CameraPose(d, d.CameraParts[0], g.TickAlpha, g.IsServer, out eye, out var rot);
            fwd = Vector3.Transform(look, rot);
            up = Vector3.Transform(Vector3.UnitY, rot);
            return;
        }
        var rx = d.Receiver;
        ref var r = ref d.Parts[rx];
        d.WorldFrame(r.Seg, out var q, out _);
        var turn = q * Rot24.Quat(r.Rot);
        fwd = Vector3.Transform(look, turn);
        up = Vector3.Transform(Vector3.UnitY, turn);
        eye = d.WorldCom - Vector3.Transform(-Vector3.UnitZ, turn) * (d.Radius + 2) + up * (d.Radius * 0.5f + 1);
    }
}
