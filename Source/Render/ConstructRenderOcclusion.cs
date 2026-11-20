using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Occlusion culling of merged chunks (docs/render/MERGED-MESHES.md "Occlusion"): after the constructs pass, each merged chunk
/// drawn in the main view has its box tested against the depth just drawn (an occlusion query, colour and depth writes off),
/// and the next frame draws it only if any sample of that box passed, the GPU deciding through conditional rendering, so the
/// CPU never waits. A ship's inside, behind its hull, costs nothing on the GPU from outside, and rooms behind walls nothing
/// from inside. A hole shot in a wall shows what is behind it the next frame. Off for small chunks (a query costs about what
/// they do), a chunk the eye is at, a frame after the eye jumped (a cut, a teleport, a picture from elsewhere), and the camera
/// feeds' passes (their views aren't the frame before's). <c>--no-occlusion</c> turns it off.
/// </summary>
public static partial class ConstructRender
{
    public static bool OcclusionOn = true; // (--no-occlusion, the bench)
    public static bool OcclusionSetting = true; // (the Video settings' "Skip hidden parts")

    /// <summary>Chunks with fewer slots aren't tested; the eye this close to a chunk's sphere draws it untested; a query's box is this much bigger than its boxes.</summary>
    public const int OcclusionMinSlots = 48;
    public const float OcclusionNear = 4f, OcclusionPad = 0.5f;

    /// <summary>The eye's move or turn past which last frame's queries say nothing about this one.</summary>
    private const float OcclusionJump = 6f, OcclusionTurn = 0.985f;

    private static bool _occlusionUse, _occlusionQuery;
    private static Vector3 _occEye, _occFwd;
    private static int _occFrameSeen = -1;
    private static long _occStamp;
    public static double OcclusionGap = 0.25; // seconds between main view frames past which last frame's results aren't used (the bench, whose configurations take turns on a slow software GPU, lifts it)

    /// <summary>Merged chunks left to the GPU's query result and queries issued, last frame (the F9 window, the bench).</summary>
    public static int LastOccTested { get; private set; }
    public static int LastOccQueries { get; private set; }

    /// <summary>The bench, after a glFinish: chunks this frame's queries found hidden, and their slots (what the next frame leaves to the GPU to skip).</summary>
    public static (int Chunks, int Slots, int Tested) BenchHidden(Renderer gfx)
    {
        int chunks = 0, slots = 0, tested = 0;
        foreach (var (_, s) in _merges)
            for (var k = 0; k < s.ChunkCount; k++)
                if (gfx.QuerySaw(s.Chunks[k].Gl) is { } saw)
                {
                    tested++;
                    if (!saw)
                    {
                        chunks++;
                        slots += s.Chunks[k].Gl.Slots;
                    }
                }
        return (chunks, slots, tested);
    }

    /// <summary>The start of the constructs pass: whether this frame tests chunks and draws by last frame's tests.</summary>
    private static void OcclusionBegin(Renderer gfx)
    {
        _occlusionQuery = _occlusionUse = false;
        if (CameraFeeds.InPass)
            return; // (a feed's pass neither asks nor uses queries, and leaves the main view's counters alone)
        LastOccTested = gfx.OccludedTested;
        LastOccQueries = gfx.OcclusionQueries;
        gfx.OccludedTested = gfx.OcclusionQueries = 0;
        if (!OcclusionOn || !OcclusionSetting || !MergeOn || gfx.ViewProj.IsIdentity || !gfx.OcclusionQueriesOn)
            return;
        var stamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var fresh = System.Diagnostics.Stopwatch.GetElapsedTime(_occStamp, stamp).TotalSeconds < OcclusionGap; // (frames that drew no world, a minimised window: the results are from however long ago)
        _occStamp = stamp;
        if (!Matrix4x4.Invert(gfx.ViewRelative, out var inv))
            return;
        var eye = gfx.CameraPosition - gfx.Origin; // (from the render origin: aboard a ship in orbit the eye moves kilometres a second in the world and hardly at all against what it sees)
        var fwd = Vector3.Normalize(-new Vector3(inv.M31, inv.M32, inv.M33));
        var steady = fresh && _occFrameSeen == gfx.OcclusionFrame && Vector3.DistanceSquared(eye, _occEye) < OcclusionJump * OcclusionJump && Vector3.Dot(fwd, _occFwd) > OcclusionTurn;
        gfx.OcclusionFrame++;
        _occFrameSeen = gfx.OcclusionFrame;
        _occEye = eye;
        _occFwd = fwd;
        _occlusionQuery = true;
        _occlusionUse = steady;
    }

    /// <summary>Is a chunk worth testing: queries on this frame, big enough, and the eye not at it (a box round the eye fails its own test at the near plane).</summary>
    private static bool OcclusionWanted(MergeChunk ch, float away) => _occlusionQuery && ch.Gl.Slots >= OcclusionMinSlots && away > OcclusionNear;

    /// <summary>The eye's move against one chunk (in the chunk's own frame) past which last frame's query says nothing about this one.</summary>
    public static float OcclusionEyeMove = 2f;

    /// <summary>
    /// Has the eye hardly moved against this chunk since its query was asked for? Measured in the chunk's own frame, so it
    /// holds whatever the render origin is (the eye itself in third person and on a spacewalk), for a camera cut, a ship
    /// passing at speed, a body whose pose snapped and a segment swinging on its joint.
    /// </summary>
    private static bool OcclusionEyeSteady(Renderer gfx, MergeChunk ch, in Matrix4x4 frame)
    {
        var rel = _occEye - frame.Translation; // (both from the render origin)
        var local = new Vector3(rel.X * frame.M11 + rel.Y * frame.M12 + rel.Z * frame.M13, rel.X * frame.M21 + rel.Y * frame.M22 + rel.Z * frame.M23, rel.X * frame.M31 + rel.Y * frame.M32 + rel.Z * frame.M33);
        var steady = OcclusionEyeMove <= 0 || ch.EyeFrame == gfx.OcclusionFrame - 1 && Vector3.DistanceSquared(local, ch.EyeAt) < OcclusionEyeMove * OcclusionEyeMove;
        ch.EyeAt = local;
        ch.EyeFrame = gfx.OcclusionFrame;
        return steady;
    }

    /// <summary>After the opaque constructs are drawn: each queued chunk that wants it, its box tested against the depth.</summary>
    private static void QueryMerged(Renderer gfx)
    {
        if (_occlusionQuery && _mergeQueued > 0)
        {
            var rel = gfx.Relative;
            gfx.Relative = true; // (the frames are relative to the render origin already)
            gfx.BeginOcclusion();
            var pad = new Vector3(OcclusionPad);
            for (var k = 0; k < _mergeQueued; k++)
            {
                ref var q = ref _mergeQueue[k];
                if (q.Test)
                    gfx.QueryMerged(q.Mesh, in q.Frame, q.Min - pad, q.Max + pad);
            }
            gfx.EndOcclusion();
            gfx.Relative = rel;
        }
        for (var k = 0; k < _mergeQueued; k++)
            _mergeQueue[k].Mesh = null!;
        _mergeQueued = 0;
    }
}
