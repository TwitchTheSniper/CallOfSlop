using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Spawning into motion (docs/sandbox/design/construct-frame-notes.md, "Spawn into motion"): anything that comes into the
/// world on or near a moving construct (a part placed, a blueprint, a paste, a dropped or crafted part, a machine's output,
/// a wreck, a piece broken off, a player set down) first finds the frame of reference it is in and that frame's motion at
/// its point, linear and turning, and enters the physics already carrying it. A ship in orbit moves hundreds of metres a
/// second: a body made there at rest in the world is left behind on its first tick.
///
/// <para>One helper for every path: <see cref="ConstructWorld.Create"/> marks each new body fresh, and before the physics
/// next steps (<see cref="ConstructWorld.Step"/>) a fresh body nobody gave a motion takes its frame's (<see cref="IntoMotion"/>).
/// A path that set its body's motion itself (a throw, a split that keeps the parent's, a drop from a rider's hands) is left
/// as it is.</para>
/// </summary>
public static partial class ConstructRide
{
    /// <summary>A frame this slow (m/s, and rad/s) isn't moving: nothing to inherit.</summary>
    private const float StillSpeed = 0.05f;

    /// <summary>
    /// A body made this close round a moving construct's bounds (m), or this far over its top, is made in its frame. Made in
    /// the air (nothing of the map within <see cref="SpawnGround"/> under it), it has the rider's whole frame
    /// (<see cref="BoundsPad"/>): a part dropped over a truck bed's side or beside a ship's hull moves with it. On the ground,
    /// only this tight box: a block set down beside a passing truck stays on the ground.
    /// </summary>
    public const float SpawnPad = 0.3f, SpawnAbove = 1f, SpawnGround = 0.6f;

    private static readonly List<Construct> _frameNear = new(32);

    /// <summary>
    /// The moving construct whose frame holds world point <paramref name="at"/> (its bounds, padded by <see cref="SpawnPad"/>:
    /// <see cref="InFrame"/>), the smallest when frames nest (a car in a cargo plane's hold); null when none moves there.
    /// </summary>
    public static Construct? FrameAt(ConstructWorld w, Vector3 at, Construct? skip = null, float pad = SpawnPad, float top = SpawnAbove)
    {
        var near = w.Near(at, BoundsAbove + 1f, _frameNear);
        Construct? best = null;
        var bestSize = float.MaxValue;
        var up = GravityFrames.UpAt(at);
        for (var i = 0; i < near.Count; i++)
        {
            var f = near[i];
            if (f == skip || f.Anchored || f.Asleep || f.PartCount == 0
                || f.Vel.LengthSquared() < StillSpeed * StillSpeed && f.AngVel.LengthSquared() < StillSpeed * StillSpeed)
                continue;
            var iq = Quaternion.Conjugate(f.Rot);
            if (!InFrame(f, Vector3.Transform(at - f.Pos, iq), Vector3.Transform(up, iq), pad, top))
                continue;
            var size = (f.LocalMax - f.LocalMin).LengthSquared();
            if (size < bestSize)
            {
                bestSize = size;
                best = f;
            }
        }
        return best;
    }

    /// <summary>
    /// Server: body <paramref name="c"/>, just come into the world, takes on the motion of the frame it is in (that of
    /// <paramref name="frame"/>'s segment <paramref name="seg"/> when given, else the frame found at its centre of mass):
    /// the frame's velocity at its centre, and its turn. False when it is in no moving frame (left as it is).
    /// </summary>
    public static bool IntoMotion(ConstructWorld w, Construct c, Construct? frame = null, int seg = 0)
    {
        if (c.Dirty)
            c.Rebuild(); // (its centre of mass, which a fresh body has yet to work out)
        if (frame == null)
        {
            var at = c.WorldCom;
            var up = GravityFrames.UpAt(at);
            var onGround = World.Query.Raycast(at + up * 0.1f, -up, SpawnGround + 0.1f + c.Radius, QueryFilter.All) < SpawnGround + 0.1f + c.Radius;
            frame = onGround ? FrameAt(w, at, c) : FrameAt(w, at, c, BoundsPad, BoundsAbove);
        }
        if (frame == null || frame == c || seg > frame.JointCount)
            return false;
        c.Vel = SurfaceVel(frame, seg, c.WorldCom);
        c.AngVel = SurfaceSpin(frame, seg);
        c.VelBefore = c.VelThen = c.Vel; // (no jolt on its first step: it was always moving so)
        c.SpeedKept = c.Vel.Length(); // (and the frame's speed even past the physics' limit: ConstructPhysics.Integrate)
        c.Asleep = false;
        c.SleepTicks = 0;
        if (c.Frame == 0 && frame.Frame == 0 && frame.SpaceShip == 0 && ConstructFrames.CanRide(c) && ConstructFrames.CanCarry(frame, c))
            ConstructFrames.Join(c, frame); // (and is in its frame from now on: local-frame-physics.md)
        return true;
    }

    /// <summary>A player set down at <paramref name="feet"/> (a respawn, a teleport aboard): moving as the frame there moves, so they land on its floor.</summary>
    public static Vector3 SpawnVel(ConstructWorld w, Vector3 feet, Construct? frame = null, int seg = 0)
    {
        frame ??= FrameAt(w, feet);
        return frame == null || seg > frame.JointCount ? Vector3.Zero : SurfaceVel(frame, seg, feet);
    }

    /// <summary>
    /// Server, before the physics steps: every body made since (<see cref="ConstructWorld.Fresh"/>) that no path gave a motion
    /// takes its frame's. Only the bodies made this tick are looked at (all of them past <see cref="ConstructWorld.FreshCap"/>),
    /// and only one with a moving construct near its origin is rebuilt and placed; nothing allocates.
    /// </summary>
    public static void SpawnIntoMotion(ConstructWorld w)
    {
        if (w.FreshOverflow)
        {
            w.FreshOverflow = false;
            w.Fresh.Clear();
            var bodies = w.Bodies;
            for (var i = 0; i < bodies.Count; i++)
                if (bodies[i].Fresh)
                    SpawnOne(w, bodies[i]);
            return;
        }
        var fresh = w.Fresh;
        for (var i = 0; i < fresh.Count; i++)
            if (fresh[i].Fresh && w.Get(fresh[i].Id) == fresh[i])
                SpawnOne(w, fresh[i]);
        fresh.Clear();
    }

    private static void SpawnOne(ConstructWorld w, Construct c)
    {
        c.Fresh = false;
        c.SpeedKept = c.Vel.Length(); // (a path that gave it its frame's motion, a split or a cut: past the speed limit too, ConstructPhysics.Integrate)
        if (c.Anchored || c.Pinned || c.Frozen || c.Asleep || c.CarriedBy != 0 || c.SpaceShip != 0 || c.HeldBy != 0 || c.PartCount == 0
            || c.Vel != Vector3.Zero || c.AngVel != Vector3.Zero)
            return; // (held, asleep, or given a motion by the path that made it)
        if (c.Dirty && c.PartCount > 1)
            c.Rebuild(); // (a spawned blueprint, rare: its centre and size; a single part's origin is its place)
        if (!MovingNear(w, c.Dirty ? c.Pos : c.WorldCom, c))
            return; // (no moving construct anywhere near: nothing to rebuild it for)
        IntoMotion(w, c);
    }

    /// <summary>Is any construct moving with its bounds near world point <paramref name="at"/> (a cheap sphere test before a fresh body is rebuilt)?</summary>
    private static bool MovingNear(ConstructWorld w, Vector3 at, Construct skip)
    {
        var near = w.Near(at, BoundsAbove + 1f, _frameNear);
        for (var i = 0; i < near.Count; i++)
        {
            var f = near[i];
            if (f == skip || f.Anchored || f.Asleep || f.PartCount == 0
                || f.Vel.LengthSquared() < StillSpeed * StillSpeed && f.AngVel.LengthSquared() < StillSpeed * StillSpeed)
                continue;
            var reach = f.Radius + skip.Radius + BoundsAbove + 2f; // (a fresh body's radius may not be worked out yet: the pad covers a part's size)
            if (Vector3.DistanceSquared(at, f.WorldCom) <= reach * reach)
                return true;
        }
        return false;
    }
}
