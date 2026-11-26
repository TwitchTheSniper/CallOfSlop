using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Sun shadows from ships and vehicles (Settings > Video > Sun shadows; Karl, 2026-11-21, phase 5 of the ship looks from the Ship Render
/// Bench). Before the world is drawn, the nearest constructs' boxes (each body's parts at rest, made once per rebuild as one instanced
/// list) are drawn from the key light into one depth map that covers <see cref="Reach"/> metres round the camera. Every scene shader
/// reads it through <c>Lighting.Glsl</c>'s <c>SunShadow</c> (the LitFrame block's uShadowVP, uShadowC, uShadowQ; texture unit
/// <see cref="Unit"/>): the sun on terrain, props, buildings and the ships themselves is dimmed where a hull stands between, with a
/// soft edge of 12 taps, fading out at the map's edge. The baked light grid already shades the static world; this adds what moves.
/// Moving parts (doors, turrets) cast at their rest pose. Cost: one depth pass of instanced boxes (at most <see cref="MaxBodies"/>
/// bodies) and 12 texture reads per lit pixel; off, nothing runs and the shaders skip it.
/// </summary>
public static unsafe class SunShadows
{
    public const int Unit = 20, MaxBodies = 12;

    /// <summary>The setting; <see cref="ForcedOff"/>: --no-sun-shadows.</summary>
    public static bool On = true, ForcedOff;

    /// <summary>The map's size in texels (Video: Sun shadows Medium 2048, High 4096).</summary>
    public static int Size = 2048;

    /// <summary>Half the side of the square the map covers round the camera (m), and the key light's multiplier (Video: Key light).</summary>
    public static float Reach = 48f, KeyLight = 1f;

    /// <summary>Bodies drawn into the map last frame (F9, checks).</summary>
    public static int LastBodies, LastBoxes;

    /// <summary>True while the map holds this frame's shadows (the LitFrame block turns them on).</summary>
    public static bool Live { get; private set; }

    /// <summary>World to the map (u, v, depth as stored), relative to <see cref="Centre"/>, for the LitFrame block.</summary>
    public static Matrix4x4 Lookup { get; private set; } = Matrix4x4.Identity;

    public static Vector3 Centre { get; private set; }

    /// <summary>The map's texel in uv, and the comparison's sign (stored depth runs the other way with reversed depth).</summary>
    public static float TexelUv => 1f / Size;

    public static float DepthSign => RenderCaps.ReversedDepth ? -1f : 1f;

    /// <summary>The depth bias in stored units (about 4 cm) and the normal offset in metres (about two texels).</summary>
    public static float DepthBias => 0.04f / Depth;

    public static float NormalOffset => 2.2f * Reach * 2f / Size;

    private const float Depth = 600f; // (the light's depth range: 300 m above the centre to 300 m below)

    private sealed class Caster
    {
        public GlBuffer Vbo, Shapes;
        public int Count, ShapeVerts, Used;
        public int MainVer = -1, DoorVer = -1, PieceVer = -1; // (ConstructShapeVersion: what this list, Door's and Piece's were made from)
        public float Radius;

        // The door parts (each door and the leaves riding it) as a small list of their own, made again when a door starts or
        // settles; this one holds every other part and is made again only when one of them changed (2026-11-23: a door on the
        // Zero Day works made all of its 250,000 boxes again, a 40 ms frame).
        public Caster? Door;

        // The pieces machines made (ConstructShapeVersion.Pieces), likewise: they come and go a few times a second on a works.
        public Caster? Piece;

        // The boxes and shapes sorted by cell of the body's frame (CellSize), and each non-empty cell's slice and bounds, in
        // cell order: a pass that covers part of a big body (the sun's square, a lamp's reach) draws only the cells it reaches.
        public int Cells;
        public int[] BoxFirst = [], BoxCount = [], ShapeFirst = [], ShapeCount = [];
        public Vector3[] Min = [], Max = [];
    }

    /// <summary>The side of a caster's cell (m, the body's frame): a works' 96 x 240 m floor is some 90 cells, a ship a few.</summary>
    public const float CellSize = 16f;

    /// <summary>Most cells a caster is cut into (the cell grows past it): the per-pass loop stays short whatever the body's size.</summary>
    private const int MaxCells = 1024;

    /// <summary>Casters' cells drawn and cells skipped as out of reach in the last frame's passes (F9, the bench).</summary>
    public static int CellsDrawn, CellsSkipped;

    private struct Pick
    {
        public Construct Body;
        public float Dist2;
    }

    private static readonly Dictionary<int, Caster> Casters = new();
    private static readonly List<int> Gone = new();
    private static readonly Pick[] Picks = new Pick[MaxBodies];

    // ---- The still bodies' map kept between frames (ShadowMapCache) -----------------------------------------------------------

    /// <summary>Which picked bodies a pass draws: all, the still ones (into a kept map) or the moving ones (over its copy).</summary>
    internal enum Which : byte { All, Still, Moving }

    /// <summary>The map's square moves in steps of about this many metres (the still map is drawn again at each), and the sun is held while within this of the true one (a cosine: about 0.15 degrees).</summary>
    public const float SnapStep = 2f, SunHold = 0.9999966f;

    private static readonly ShadowMapCache Cache = new();
    private static Vector3 _keySun;
    private static int _stillBodies, _stillBoxes;

    /// <summary>Times a still map was drawn (the sun's here and the dark interiors' in <see cref="RoomShade"/>): F9, the bench.</summary>
    public static int StillDrawn;

    /// <summary>Whether each picked body counts as still this frame (<see cref="ClassifyPicks"/>).</summary>
    internal static readonly bool[] PickStill = new bool[MaxBodies];

    private sealed class PoseRec
    {
        public Vector3 Pos;
        public Quaternion Rot;
        public int Since, Seen;
    }

    private static readonly Dictionary<int, PoseRec> Poses = new();

    /// <summary>
    /// Marks each of the <paramref name="n"/> picked bodies still (the same ticked pose for the last two frames, and not moving
    /// within the tick) or moving, and returns
    /// a key of the still ones (their ids, boxes and poses, in any order) and how many move.
    /// </summary>
    internal static long ClassifyPicks(int n, float alpha, bool blendTicks, out int moving)
    {
        _ = alpha;
        long key = 0;
        moving = 0;
        for (var k = 0; k < n; k++)
        {
            var c = Picks[k].Body;
            // (the pose as ticked, not as blended: a blend between two equal poses still moves by a rounding with the frame's alpha)
            var pos = c.Pos;
            var rot = c.Rot;
            var still = !blendTicks || c.PrevPos == pos && c.PrevRot == rot;
            if (!Poses.TryGetValue(c.Id, out var r))
                Poses[c.Id] = r = new PoseRec { Pos = pos, Rot = rot, Since = _frame, Seen = _frame };
            else if (r.Seen != _frame)
            {
                r.Seen = _frame;
                if (r.Pos != pos || r.Rot != rot || !still)
                    (r.Pos, r.Rot, r.Since) = (pos, rot, _frame);
            }
            PickStill[k] = still && _frame - r.Since >= 2;
            if (!PickStill[k])
            {
                moving++;
                continue;
            }
            var h = Mix(Mix(Mix(Mix((long)c.Id * -7046029254386353131L, ConstructShapeVersion.Both(c)), BitConverter.SingleToInt32Bits(pos.X)), BitConverter.SingleToInt32Bits(pos.Y)), BitConverter.SingleToInt32Bits(pos.Z));
            key += Mix(Mix(h, BitConverter.SingleToInt32Bits(rot.X) ^ (long)BitConverter.SingleToInt32Bits(rot.Y) << 32), BitConverter.SingleToInt32Bits(rot.Z) ^ (long)BitConverter.SingleToInt32Bits(rot.W) << 32); // (a sum: the picks' order doesn't matter)
        }
        return key;
    }

    private static long Mix(long h, long v) => (h ^ v) * 0x100000001B3L + (h >> 29);
    private static float[] _boxes = new float[6 * 256];
    private static float[] _shapes = new float[6 * 1024];
    private static bool _ready, _broken;
    private static int _frame, _size;
    private static GlProgram _prog;
    private static int _lMvp;
    private static GlVertexArray _vao, _shapeVao;
    private static GlBuffer _cube, _cubeIdx;
    private static GlFramebuffer _fbo;
    private static uint _tex;

    /// <summary>
    /// The frame's map: the bodies of <paramref name="world"/> within reach of <paramref name="cam"/>, posed as ConstructRender
    /// will draw them (<paramref name="alpha"/> between ticks when <paramref name="blendTicks"/>). Call after the camera is set and
    /// before the world is drawn; leaves the main target bound with its viewport.
    /// </summary>
    public static void Render(Renderer gfx, ConstructWorld? world, Vector3 cam, Vector3 fwd, float alpha, bool blendTicks)
    {
        Live = false;
        LastBodies = LastBoxes = 0;
        CellsDrawn = CellsSkipped = 0; // (this frame's passes, the sun's first: RoomShade and LampShadows add theirs)
        _frame++;
        if ((_frame & 255) == 0)
            DropOld();
        var sun = Lighting.Current.SunDir;
        // (no key light: night, or the star behind the ground or eclipsed; any direction otherwise, since the star lights a ship
        // in orbit from below too and a planetoid's "up" isn't world +Y)
        if (!On || ForcedOff || _broken || world == null || world.Bodies.Count == 0 || Lighting.Current.SunColor.LengthSquared() < 1e-6f || CameraFeeds.InPass)
            return;
        var n = PickNear(world, cam, Reach, alpha, blendTicks);
        if (n == 0 || !Init())
        {
            Clear(n);
            return;
        }
        // The light's square: centred a little ahead of the camera, snapped to whole texels in the light's frame so it doesn't shimmer
        // as you move, and to steps of about SnapStep (along the light too), so the still bodies' map (Cache) stays right for a while.
        // The sun's direction is the one the still map was drawn with while it is within SunHold of the true one.
        if (Cache.Key != 0 && Vector3.Dot(sun, _keySun) > SunHold)
            sun = _keySun;
        var up = MathF.Abs(sun.Y) > 0.98f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(up, sun));
        var lup = Vector3.Cross(sun, right);
        var flat = new Vector3(fwd.X, 0f, fwd.Z);
        var ahead = cam + (flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) * (Reach * 0.35f) : Vector3.Zero);
        var texel = Reach * 2f / Size;
        var step = texel * MathF.Max(1f, MathF.Round(SnapStep / texel));
        double cr = Math.Round(Vector3.Dot(ahead, right) / (double)step) * step, cu = Math.Round(Vector3.Dot(ahead, lup) / (double)step) * step, cs = Math.Round(Vector3.Dot(ahead, sun) / (double)SnapStep) * SnapStep;
        var centre = new Vector3((float)(right.X * cr + lup.X * cu + sun.X * cs), (float)(right.Y * cr + lup.Y * cu + sun.Y * cs), (float)(right.Z * cr + lup.Z * cu + sun.Z * cs));
        var view = Matrix4x4.CreateLookAt(sun * (Depth * 0.5f), Vector3.Zero, up);
        var ortho = Matrix4x4.CreateOrthographicOffCenter(-Reach, Reach, -Reach, Reach, 0f, Depth); // (z 0 at the eye, 1 at the far end)
        var toClip = Matrix4x4.Identity;
        var toMap = Matrix4x4.Identity;
        (toMap.M11, toMap.M41, toMap.M22, toMap.M42) = (0.5f, 0.5f, 0.5f, 0.5f);
        if (RenderCaps.ReversedDepth)
            (toClip.M33, toClip.M43, toMap.M33, toMap.M43) = (-1f, 1f, -1f, 1f); // (clip 0..1 with the near end at 1, as the game draws)
        else
            (toClip.M33, toClip.M43) = (2f, -1f); // (GL's -1..1 clip; stored 0..1)
        var lightClip = view * ortho * toClip;
        Lookup = view * ortho * toMap;
        Centre = centre;

        var vp = stackalloc int[4];
        Native.glGetIntegerv(GL_VIEWPORT, vp);
        var blend = Native.glIsEnabled((GlEnum)(uint)GlCapability.Blend);
        var cull = Native.glIsEnabled((GlEnum)(uint)GlCapability.CullFace);
        var scissor = Native.glIsEnabled((GlEnum)(uint)GlCapability.ScissorTest);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        Native.glViewport(0, 0, Size, Size);
        Native.glColorMask(false, false, false, false);
        Native.glDepthMask(true);
        Native.glEnable(GlCapability.DepthTest);
        Native.glDisable(GlCapability.Blend);
        Native.glDisable(GlCapability.CullFace);
        Native.glDisable(GlCapability.ScissorTest);
        _prog.Use();
        Native.glBindVertexArray(_vao);
        int bodies = 0, boxes = 0;
        var stillKey = ClassifyPicks(n, alpha, blendTicks, out var moving);
        var key = Mix(Mix(Mix(Mix(stillKey, BitConverter.SingleToInt32Bits(centre.X)), BitConverter.SingleToInt32Bits(centre.Y)), BitConverter.SingleToInt32Bits(centre.Z)),
            BitConverter.SingleToInt32Bits(sun.X) ^ (long)BitConverter.SingleToInt32Bits(sun.Y) << 20 ^ (long)BitConverter.SingleToInt32Bits(sun.Z) << 40) | 1; // (never 0: no map yet)
        if (key != Cache.Key)
        {
            // The still bodies into their own map, kept until something it was drawn from changes.
            Cache.TargetStill(_fbo, 0);
            Native.glClear(GL_DEPTH_BUFFER_BIT); // (the game's clear depth: the far end either way)
            _stillBodies = _stillBoxes = 0;
            DrawPicked(n, lightClip, centre, sun, Reach * 1.415f, alpha, blendTicks, ref _stillBodies, ref _stillBoxes, Which.Still);
            Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, _tex, 0);
            (Cache.Key, Cache.LiveDiffers, _keySun) = (key, true, sun);
            StillDrawn++;
        }
        if (moving > 0 || Cache.LiveDiffers)
        {
            Cache.CopyToLive(_tex, 0);
            Cache.LiveDiffers = moving > 0;
            if (moving > 0)
            {
                Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
                DrawPicked(n, lightClip, centre, sun, Reach * 1.415f, alpha, blendTicks, ref bodies, ref boxes, Which.Moving);
            }
        }
        (LastBodies, LastBoxes) = (bodies + _stillBodies, boxes + _stillBoxes);
        Native.glBindVertexArray(default);
        Native.glBindBuffer(GL_ARRAY_BUFFER, default);
        Native.glColorMask(true, true, true, true);
        if (blend)
            Native.glEnable(GlCapability.Blend);
        if (cull)
            Native.glEnable(GlCapability.CullFace);
        if (scissor)
            Native.glEnable(GlCapability.ScissorTest);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, Renderer.MainFbo);
        Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D, _tex);
        Native.glActiveTexture(GL_TEXTURE0);
        gfx.Rebind(); // (the box renderer's program and arrays: its unbatched draws trust them)
        Live = LastBodies > 0;
        Clear(n);
    }

    /// <summary>
    /// Fills <see cref="Picks"/> with the nearest bodies (at most <see cref="MaxBodies"/>) whose sphere comes within 1.5 x
    /// <paramref name="reach"/> of <paramref name="cam"/>, as ConstructRender draws them; returns how many. <see cref="RoomShade"/> shares it.
    /// <paramref name="stillBySurface"/> (the dark interiors' far ring, which draws still bodies alone): a body moving this tick is left
    /// out, and the nearest are those whose sphere is nearest, not their middle, so a 240 m works 100 m off isn't pushed out by a dozen
    /// crates nearer the camera.
    /// </summary>
    internal static int PickNear(ConstructWorld world, Vector3 cam, float reach, float alpha, bool blendTicks, bool stillBySurface = false)
    {
        // The nearest bodies whose sphere reaches the square (a ship 40 m off still shades the ground at your feet).
        var n = 0;
        foreach (var c in world.Bodies)
        {
            if (c.PartCount == 0 || (ConstructRender.AwayOnly != 0 ? c.Id != ConstructRender.AwayOnly : c.SpaceAway != 0)) // (as ConstructRender draws them)
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var d2 = Vector3.DistanceSquared(pos, cam);
            var r = reach * 1.5f + c.Radius;
            if (d2 > r * r)
                continue;
            if (stillBySurface)
            {
                if (blendTicks && (c.PrevPos != c.Pos || c.PrevRot != c.Rot))
                    continue;
                var gap = MathF.Max(MathF.Sqrt(d2) - c.Radius, 0f);
                d2 = gap * gap;
            }
            var k = n;
            if (n == MaxBodies)
            {
                k = 0;
                for (var i = 1; i < n; i++)
                    if (Picks[i].Dist2 > Picks[k].Dist2)
                        k = i;
                if (Picks[k].Dist2 <= d2)
                    continue;
            }
            else
                n++;
            Picks[k] = new Pick { Body = c, Dist2 = d2 };
        }
        return n;
    }

    /// <summary>
    /// Draws the picked bodies' boxes into the bound depth target with <paramref name="lightClip"/> (positions less <paramref name="centre"/>),
    /// an orthographic view along <paramref name="dir"/> that covers <paramref name="reach"/> m either side of the line through
    /// <paramref name="centre"/>: only the cells of each body that reach that line are drawn. The depth program and arrays must be bound.
    /// </summary>
    internal static void DrawPicked(int n, Matrix4x4 lightClip, Vector3 centre, Vector3 dir, float reach, float alpha, bool blendTicks, ref int bodies, ref int boxes, Which which = Which.All)
    {
        for (var k = 0; k < n; k++)
        {
            if (which != Which.All && PickStill[k] != (which == Which.Still))
                continue;
            var c = Picks[k].Body;
            var cast = CasterOf(c);
            if (cast.Count == 0 && cast.ShapeVerts == 0 && cast.Door is not { Count: > 0 } && cast.Door is not { ShapeVerts: > 0 } && cast.Piece is not { Count: > 0 } && cast.Piece is not { ShapeVerts: > 0 })
                continue;
            var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
            var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
            var mvp = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos - centre) * lightClip;
            Native.glUniformMatrix4fv(_lMvp, 1, false, &mvp.M11);
            var back = Quaternion.Conjugate(rot);
            var at = Vector3.Transform(centre - pos, back);
            var along = Vector3.Transform(dir, back);
            boxes += DrawCells(cast, at, along, reach, -1, false);
            if (cast.Door != null)
                boxes += DrawCells(cast.Door, at, along, reach, -1, false);
            if (cast.Piece != null)
                boxes += DrawCells(cast.Piece, at, along, reach, -1, false);
            bodies++;
        }
    }

    /// <summary>
    /// A body's caster into the bound depth target, the body's matrix set: the cells whose bounds come within <paramref name="reach"/> of
    /// <paramref name="at"/> (the body's frame), or of the line through it along <paramref name="dir"/> when that isn't zero. Runs of
    /// neighbouring cells go as one draw. <paramref name="selfLoc"/>: a uniform set to <paramref name="self"/> for the boxes and 0 for the
    /// shapes (<see cref="LampShadows"/>' fixture test), or -1. Returns the boxes drawn.
    /// </summary>
    private static int DrawCells(Caster k, Vector3 at, Vector3 dir, float reach, int selfLoc, bool self)
    {
        var line = dir.LengthSquared() > 1e-8f;
        if (line)
            dir = Vector3.Normalize(dir);
        var drawn = 0;
        if (k.Count > 0)
        {
            if (selfLoc >= 0)
                Native.glUniform1f(selfLoc, self ? 1f : 0f);
            Native.glBindBuffer(GL_ARRAY_BUFFER, k.Vbo);
            var first = -1;
            var count = 0;
            for (var q = 0; q <= k.Cells; q++)
            {
                var take = q < k.Cells && k.BoxCount[q] > 0 && Reaches(k.Min[q], k.Max[q], at, dir, line, reach);
                if (q < k.Cells && !take && k.BoxCount[q] > 0)
                    CellsSkipped++;
                if (take && first >= 0 && first + count == k.BoxFirst[q])
                {
                    count += k.BoxCount[q];
                    CellsDrawn++;
                    continue;
                }
                if (count > 0)
                {
                    Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 24, (void*)(first * 24L));
                    Native.glVertexAttribPointer(2, 3, GL_FLOAT, false, 24, (void*)(first * 24L + 12));
                    Native.glDrawElementsInstanced(GL_TRIANGLES, 36, GL_UNSIGNED_SHORT, null, count);
                    drawn += count;
                }
                (first, count) = take ? (k.BoxFirst[q], k.BoxCount[q]) : (-1, 0);
                if (take)
                    CellsDrawn++;
            }
        }
        if (k.ShapeVerts > 0)
        {
            if (selfLoc >= 0)
                Native.glUniform1f(selfLoc, 0f); // (the fixture test is for boxes: a shape's corner near the lamp would tear its triangle)
            Native.glBindVertexArray(_shapeVao);
            Native.glBindBuffer(GL_ARRAY_BUFFER, k.Shapes);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 24, (void*)12); // (zeros: no corner)
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 24, (void*)0);
            Native.glVertexAttribPointer(2, 3, GL_FLOAT, false, 24, (void*)12); // (zeros: no half size)
            var first = -1;
            var count = 0;
            for (var q = 0; q <= k.Cells; q++)
            {
                var take = q < k.Cells && k.ShapeCount[q] > 0 && Reaches(k.Min[q], k.Max[q], at, dir, line, reach);
                if (take && first >= 0 && first + count == k.ShapeFirst[q])
                {
                    count += k.ShapeCount[q];
                    continue;
                }
                if (count > 0)
                    Native.glDrawArrays(GL_TRIANGLES, first, count);
                (first, count) = take ? (k.ShapeFirst[q], k.ShapeCount[q]) : (-1, 0);
            }
            Native.glBindVertexArray(_vao);
        }
        return drawn;
    }

    /// <summary>Does a cell's box come within <paramref name="reach"/> of a point (or of the line through it along <paramref name="dir"/>, a unit vector)?</summary>
    private static bool Reaches(Vector3 min, Vector3 max, Vector3 at, Vector3 dir, bool line, float reach)
    {
        var mid = (min + max) * 0.5f;
        var half = (max - min) * 0.5f;
        if (!line)
        {
            var d = Vector3.Max(Vector3.Abs(at - mid) - half, Vector3.Zero);
            return d.LengthSquared() <= reach * reach;
        }
        // (the line against the cell's bounding sphere: a little generous, never short)
        var rel = mid - at;
        var off = rel - dir * Vector3.Dot(rel, dir);
        var r = reach + half.Length();
        return off.LengthSquared() <= r * r;
    }

    /// <summary>A body's caster boxes within <paramref name="reach"/> of <paramref name="at"/> (its own frame), into the bound depth target (<see cref="LampShadows"/>, the body's matrix set); the boxes drawn.</summary>
    internal static int DrawNear(Construct c, Vector3 at, float reach, int selfLoc, bool self)
    {
        var k = CasterOf(c);
        return DrawCells(k, at, Vector3.Zero, reach, selfLoc, self) + (k.Door != null ? DrawCells(k.Door, at, Vector3.Zero, reach, selfLoc, self) : 0)
            + (k.Piece != null ? DrawCells(k.Piece, at, Vector3.Zero, reach, selfLoc, self) : 0);
    }

    /// <summary>Binds the depth program and its arrays (<see cref="RoomShade"/>'s passes); false when it can't be made.</summary>
    internal static bool BindDepthProgram()
    {
        if (!InitProgram())
            return false;
        _prog.Use();
        Native.glBindVertexArray(_vao);
        return true;
    }

    /// <summary>The unit cube's arrays with the caster attributes (<see cref="LampShadows"/>' own program draws with them); false when they can't be made.</summary>
    internal static bool BindCubeArrays()
    {
        if (!InitProgram())
            return false;
        Native.glBindVertexArray(_vao);
        return true;
    }

    /// <summary>Lets go of the picked bodies (no references kept past the frame).</summary>
    internal static void ClearPicks(int n) => Clear(n);

    /// <summary>Turns the map off for the passes after the world (the gun, menus): the next <see cref="Render"/> turns it on again.</summary>
    public static void EndWorld() => Live = false;

    private static void Clear(int n)
    {
        for (var k = 0; k < n; k++)
            Picks[k].Body = null!;
    }

    private const string Vs = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 iCentre;
        layout(location = 2) in vec3 iHalf;
        uniform mat4 uMvp;
        void main() { gl_Position = uMvp * vec4(iCentre + aPos * iHalf, 1.0); }
        """;

    private const string Fs = """
        #version 410 core
        out vec4 FragColor;
        void main() { FragColor = vec4(1.0); }
        """;

    private static bool _progReady, _progBroken;

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready && Resize();
        if (!InitProgram())
        {
            _broken = true;
            return false;
        }
        try
        {
            GlFramebuffer fb;
            Native.glGenFramebuffers(1, &fb);
            _fbo = fb;
            _ready = true;
            if (!Resize())
                return false;
            Console.WriteLine($"[shadows] sun shadows ready: {Size} x {Size}, {Reach * 2:0} m square, texture unit {Unit}");
        }
        catch (Exception ex)
        {
            _broken = true;
            _ready = false;
            Console.WriteLine($"[shadows] sun shadows off: {ex.Message}");
        }
        return _ready;
    }

    /// <summary>The depth program and the unit cube's arrays, shared by the sun's map and <see cref="RoomShade"/>'s.</summary>
    private static bool InitProgram()
    {
        if (_progReady || _progBroken)
            return _progReady;
        try
        {
            var v = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(Vs)).Compile();
            var f = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(Fs)).Compile();
            _prog = CreateProgram();
            Native.glAttachShader(_prog, v);
            Native.glAttachShader(_prog, f);
            Native.glLinkProgram(_prog);
            GlDebug.Label(GL_PROGRAM, _prog.Id, "SunShadows");
            int linked;
            Native.glGetProgramiv(_prog, GL_LINK_STATUS, &linked);
            if (linked == 0)
                throw new InvalidOperationException("the depth program did not link");
            v.Dispose();
            f.Dispose();
            fixed (byte* nm = "uMvp\0"u8)
                _lMvp = Native.glGetUniformLocation(_prog, nm);

            _vao = GenVertexArray();
            Native.glBindVertexArray(_vao);
            var corners = stackalloc float[24];
            for (var i = 0; i < 8; i++)
            {
                corners[i * 3] = (i & 1) == 0 ? -1f : 1f;
                corners[i * 3 + 1] = (i & 2) == 0 ? -1f : 1f;
                corners[i * 3 + 2] = (i & 4) == 0 ? -1f : 1f;
            }
            var idx = stackalloc ushort[36] { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 };
            _cube = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _cube);
            Native.glBufferData(GL_ARRAY_BUFFER, 24 * sizeof(float), corners, GL_STATIC_DRAW);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 12, (void*)0);
            EnableVertexAttribArray(0);
            _cubeIdx = GenBuffer();
            Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _cubeIdx);
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, 36 * sizeof(ushort), idx, GL_STATIC_DRAW);
            EnableVertexAttribArray(1);
            EnableVertexAttribArray(2);
            Native.glVertexAttribDivisor(1, 1);
            Native.glVertexAttribDivisor(2, 1);
            Native.glBindVertexArray(default);
            Native.glBindBuffer(GL_ARRAY_BUFFER, default);
            GlDebug.Label(GL_VERTEX_ARRAY, _vao.Id, "SunShadows");
            _shapeVao = GenVertexArray(); // (shaped boxes' triangles: DrawShapes points its three attributes at a body's list, no divisors)
            Native.glBindVertexArray(_shapeVao);
            EnableVertexAttribArray(0);
            EnableVertexAttribArray(1);
            EnableVertexAttribArray(2);
            Native.glBindVertexArray(default);
            _progReady = true;
        }
        catch (Exception ex)
        {
            _progBroken = true;
            Console.WriteLine($"[shadows] the depth program failed: {ex.Message}");
        }
        return _progReady;
    }

    /// <summary>The depth map at <see cref="Size"/> (made again when the setting changes).</summary>
    private static bool Resize()
    {
        if (_size == Size && _tex != 0)
            return true;
        if (_tex != 0)
        {
            var old = new GlTexture(_tex);
            Native.glDeleteTextures(1, &old);
        }
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        _tex = tex.Id;
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D, _tex);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_DEPTH_COMPONENT32F, Size, Size, 0, GL_DEPTH_COMPONENT, GL_FLOAT, null);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, _tex, 0);
        var status = Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, Renderer.MainFbo);
        _size = Size;
        Cache.Ensure(Size, 1, false);
        if (status != GL_FRAMEBUFFER_COMPLETE)
        {
            Console.WriteLine($"[shadows] sun shadows off: the depth map's framebuffer is incomplete (0x{status:x})");
            _broken = true;
            _ready = false;
            return false;
        }
        return true;
    }

    /// <summary>A shaped box smaller than this (its largest half size, m) still casts as its box: a bolt's or a knob's shadow doesn't show the difference.</summary>
    private const float ShapeMinHalf = 0.12f;

    /// <summary>
    /// The body's boxes at rest in its own frame (axis-aligned there: parts turn in quarter turns), in two lists: the door parts,
    /// made again whenever a door starts moving or settles (<see cref="Construct.DoorPoseStamp"/>: a settled door's parts sit at
    /// its pose, shut or open, and a moving door is left out, so an open door lets the sun, the sky and lamps through), and the
    /// rest, made again only when one of those parts changed (<see cref="ConstructShapeVersion"/>), not on every rebuild.
    /// </summary>
    private static Caster CasterOf(Construct c)
    {
        if (!Casters.TryGetValue(c.Id, out var k))
        {
            Casters[c.Id] = k = new Caster();
            k.Vbo = GenBuffer();
            GlDebug.Label(GL_BUFFER, k.Vbo.Id, "SunShadows.caster");
        }
        k.Used = _frame;
        var main = ConstructShapeVersion.Main(c);
        var doors = ConstructShapeVersion.Doors(c);
        if (k.MainVer != main)
        {
            k.MainVer = main;
            Fill(k, c, false);
        }
        var pieces = ConstructShapeVersion.Pieces(c);
        if (k.PieceVer != pieces)
        {
            k.PieceVer = pieces;
            if (k.Piece == null)
            {
                k.Piece = new Caster { Vbo = GenBuffer() };
                GlDebug.Label(GL_BUFFER, k.Piece.Vbo.Id, "SunShadows.pieces");
            }
            Fill(k.Piece, c, false, true);
        }
        if (k.DoorVer != doors)
        {
            k.DoorVer = doors;
            if (k.Door == null && c.DoorCount > 0)
            {
                k.Door = new Caster { Vbo = GenBuffer() };
                GlDebug.Label(GL_BUFFER, k.Door.Vbo.Id, "SunShadows.doors");
            }
            if (k.Door != null)
                Fill(k.Door, c, true);
        }
        return k;
    }

    /// <summary>One of <see cref="CasterOf"/>'s lists: the door parts at rest (a moving door left out), the machines' pieces (<paramref name="pieces"/>) or every other part.</summary>
    private static void Fill(Caster k, Construct c, bool doorParts, bool pieces = false)
    {
        var n = 0;
        var sv = 0;
        var groups = 0;
        if (doorParts)
        {
            for (var d = 0; d < c.DoorCount; d++)
            {
                var dp = c.Doors[d].Part;
                if (dp >= c.PartCount || ConstructDoors.IsMoving(c, dp))
                    continue; // (a door on its way: out until it settles, when its parts are at the new pose)
                AddPart(c, dp, ref n, ref sv, ref groups);
                for (var m = 0; m < c.Doors[d].LeafCount; m++)
                    if (c.Doors[d].LeafFirst + m < c.DoorLeafCount && c.DoorLeaves[c.Doors[d].LeafFirst + m] is var lp && lp < c.PartCount)
                        AddPart(c, lp, ref n, ref sv, ref groups);
            }
        }
        else
            for (var i = 0; i < c.PartCount; i++)
                if (c.DoorOfListed(i) < 0 && !c.Growing(i) && ConstructShapeVersion.IsPiece(c, i) == pieces)
                    AddPart(c, i, ref n, ref sv, ref groups); // (a piece growing on a die casts once it is cut or done: its length changes twice a second, Construct.GrowStamp)
        k.Count = n;
        SortCells(k, n, groups, sv);
        Native.glBindBuffer(GL_ARRAY_BUFFER, k.Vbo);
        fixed (float* p = _boxes)
            Native.glBufferData(GL_ARRAY_BUFFER, Math.Max(n, 1) * 24, p, GL_STATIC_DRAW);
        k.ShapeVerts = sv;
        if (sv > 0)
        {
            if (k.Shapes.Id == 0)
            {
                k.Shapes = GenBuffer();
                Native.glBindBuffer(GL_ARRAY_BUFFER, k.Shapes);
                GlDebug.Label(GL_BUFFER, k.Shapes.Id, "SunShadows.shapes");
            }
            Native.glBindBuffer(GL_ARRAY_BUFFER, k.Shapes);
            fixed (float* p = _shapes)
                Native.glBufferData(GL_ARRAY_BUFFER, sv * 24, p, GL_STATIC_DRAW);
        }
        Native.glBindBuffer(GL_ARRAY_BUFFER, default);
    }

    /// <summary>Part <paramref name="i"/>'s boxes and shapes onto the list being made (<see cref="_boxes"/>, <see cref="_shapes"/>).</summary>
    private static void AddPart(Construct c, int i, ref int n, ref int sv, ref int groups)
    {
        ref var part = ref c.Parts[i];
        if (c.HasDesign && DesignParts.LookOf(c.DefOf(i)) is { } look)
        {
            AddDesign(look, part.Pos, part.Rot, ref sv, ref groups); // (its one box is hidden: the drawn mesh casts, Renderer.Design.cs)
            return;
        }
        foreach (var b in c.DefOf(i).Boxes)
        {
            if (b.Hidden || b.Glass > 0f || b.BySeams && b.SeamHidden(i < c.BlockJoined.Length ? c.BlockJoined[i] : (byte)0))
                continue; // (a block machine's box not drawn by its cell's welds casts nothing: a Die Block under its press, ExtruderRender.cs)
            if (b.Shape != 0 && MathF.Max(b.Half.X, MathF.Max(b.Half.Y, b.Half.Z)) >= ShapeMinHalf && PartShapes.Mesh(b.Shape) is { Length: > 0 } mesh)
            {
                // A shape (a sloped hull plate, a wedge, a cylinder) casts as itself, not as the box it fills: a sloped plate's
                // box is a solid block round it, which hid the plate's own face from the sky and the sun (Karl, 2026-11-21: the
                // Zero Day's sides went black outside, the dark interiors taking them for a shut room).
                var verts = mesh.Length / PartShapes.FloatsPerVertex;
                if ((sv + verts) * 6 > _shapes.Length)
                    Array.Resize(ref _shapes, Math.Max(_shapes.Length * 2, (sv + verts) * 6));
                for (var v = 0; v < verts; v++)
                {
                    var m = v * PartShapes.FloatsPerVertex;
                    var at = part.Pos + Rot24.Apply(part.Rot, b.ShapePoint(new Vector3(mesh[m], mesh[m + 1], mesh[m + 2])));
                    var so = (sv + v) * 6;
                    (_shapes[so], _shapes[so + 1], _shapes[so + 2], _shapes[so + 3], _shapes[so + 4], _shapes[so + 5]) = (at.X, at.Y, at.Z, 0f, 0f, 0f);
                }
                if (groups == _grpFirst.Length)
                {
                    Array.Resize(ref _grpFirst, groups * 2);
                    Array.Resize(ref _grpCentre, groups * 2);
                }
                _grpFirst[groups] = sv;
                _grpCentre[groups++] = part.Pos + Rot24.Apply(part.Rot, b.Center);
                sv += verts;
                continue;
            }
            if ((n + 1) * 6 > _boxes.Length)
                Array.Resize(ref _boxes, _boxes.Length * 2); // (grows to the biggest body once)
            var centre = part.Pos + Rot24.Apply(part.Rot, b.Center);
            var half = Rot24.ApplyHalf(part.Rot, b.Half);
            var o = n * 6;
            (_boxes[o], _boxes[o + 1], _boxes[o + 2], _boxes[o + 3], _boxes[o + 4], _boxes[o + 5]) = (centre.X, centre.Y, centre.Z, half.X, half.Y, half.Z);
            n++;
        }
    }

    /// <summary>Triangles of a ship design's look per shape group (a group goes into one cell: a long hull band is cut into many).</summary>
    private const int DesignGroupTris = 64;

    /// <summary>
    /// A ship design part's look (DesignParts.Look, its triangles in the part's frame) onto the shape list, in groups of
    /// <see cref="DesignGroupTris"/> triangles: the sun, the dark interiors and the lamps see the hull as it is drawn (Karl, 2026-11-26: a
    /// design cast nothing, its hidden box being all the caster knew of it).
    /// </summary>
    private static void AddDesign(DesignParts.Look look, Vector3 pos, byte rot, ref int sv, ref int groups)
    {
        var verts = look.Vertices - look.Vertices % 3;
        if (verts <= 0)
            return;
        if ((sv + verts) * 6 > _shapes.Length)
            Array.Resize(ref _shapes, Math.Max(_shapes.Length * 2, (sv + verts) * 6));
        var p = look.Pos;
        for (var v = 0; v < verts; v++)
        {
            var at = pos + Rot24.Apply(rot, new Vector3(p[v * 3], p[v * 3 + 1], p[v * 3 + 2]));
            var so = (sv + v) * 6;
            (_shapes[so], _shapes[so + 1], _shapes[so + 2], _shapes[so + 3], _shapes[so + 4], _shapes[so + 5]) = (at.X, at.Y, at.Z, 0f, 0f, 0f);
        }
        for (var first = 0; first < verts; first += DesignGroupTris * 3)
        {
            var end = Math.Min(verts, first + DesignGroupTris * 3);
            if (groups == _grpFirst.Length)
            {
                Array.Resize(ref _grpFirst, groups * 2);
                Array.Resize(ref _grpCentre, groups * 2);
            }
            var sum = Vector3.Zero;
            for (var v = first; v < end; v++)
            {
                var so = (sv + v) * 6;
                sum += new Vector3(_shapes[so], _shapes[so + 1], _shapes[so + 2]);
            }
            _grpFirst[groups] = sv + first;
            _grpCentre[groups++] = sum / (end - first);
        }
        sv += verts;
    }

    private static int[] _grpFirst = new int[64], _cellOf = new int[256], _cellCount = [], _cellAt = [];
    private static Vector3[] _grpCentre = new Vector3[64], _cellMin = [], _cellMax = [];
    private static float[] _sorted = new float[6 * 256];

    /// <summary>
    /// <see cref="CasterOf"/>'s boxes (in <see cref="_boxes"/>) and shapes (in <see cref="_shapes"/>, <paramref name="groups"/> of them
    /// starting at <see cref="_grpFirst"/>) put in cell order, each cell's slice and bounds kept on the caster.
    /// </summary>
    private static void SortCells(Caster k, int n, int groups, int sv)
    {
        k.Cells = 0;
        if (n + groups == 0)
            return;
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var b = 0; b < n; b++)
        {
            var at = new Vector3(_boxes[b * 6], _boxes[b * 6 + 1], _boxes[b * 6 + 2]);
            lo = Vector3.Min(lo, at);
            hi = Vector3.Max(hi, at);
        }
        for (var g = 0; g < groups; g++)
        {
            lo = Vector3.Min(lo, _grpCentre[g]);
            hi = Vector3.Max(hi, _grpCentre[g]);
        }
        var size = CellSize;
        int nx, ny, nz;
        while (true)
        {
            nx = (int)((hi.X - lo.X) / size) + 1;
            ny = (int)((hi.Y - lo.Y) / size) + 1;
            nz = (int)((hi.Z - lo.Z) / size) + 1;
            if ((long)nx * ny * nz <= MaxCells)
                break;
            size *= 2f;
        }
        var cells = nx * ny * nz;
        if (_cellCount.Length < cells * 2)
        {
            _cellCount = new int[cells * 2];
            _cellAt = new int[cells * 2];
            _cellMin = new Vector3[cells];
            _cellMax = new Vector3[cells];
        }
        if (_cellOf.Length < n + groups)
            _cellOf = new int[Math.Max(n + groups, _cellOf.Length * 2)];
        Array.Clear(_cellCount, 0, cells * 2); // (boxes in the first half, shape vertices in the second)
        Array.Fill(_cellMin, new Vector3(float.MaxValue), 0, cells);
        Array.Fill(_cellMax, new Vector3(float.MinValue), 0, cells);
        int Cell(Vector3 at) => (Math.Clamp((int)((at.X - lo.X) / size), 0, nx - 1) * ny + Math.Clamp((int)((at.Y - lo.Y) / size), 0, ny - 1)) * nz + Math.Clamp((int)((at.Z - lo.Z) / size), 0, nz - 1);
        for (var b = 0; b < n; b++)
        {
            var o = b * 6;
            var at = new Vector3(_boxes[o], _boxes[o + 1], _boxes[o + 2]);
            var half = new Vector3(_boxes[o + 3], _boxes[o + 4], _boxes[o + 5]);
            var q = _cellOf[b] = Cell(at);
            _cellCount[q]++;
            _cellMin[q] = Vector3.Min(_cellMin[q], at - half);
            _cellMax[q] = Vector3.Max(_cellMax[q], at + half);
        }
        for (var g = 0; g < groups; g++)
        {
            var q = _cellOf[n + g] = Cell(_grpCentre[g]);
            var end = g + 1 < groups ? _grpFirst[g + 1] : sv;
            _cellCount[cells + q] += end - _grpFirst[g];
            for (var v = _grpFirst[g]; v < end; v++)
            {
                var p = new Vector3(_shapes[v * 6], _shapes[v * 6 + 1], _shapes[v * 6 + 2]);
                _cellMin[q] = Vector3.Min(_cellMin[q], p);
                _cellMax[q] = Vector3.Max(_cellMax[q], p);
            }
        }
        // Each cell's start, boxes and shape vertices apart.
        int bAt = 0, sAt = 0, used = 0;
        for (var q = 0; q < cells; q++)
        {
            _cellAt[q] = bAt;
            _cellAt[cells + q] = sAt;
            bAt += _cellCount[q];
            sAt += _cellCount[cells + q];
            if (_cellCount[q] + _cellCount[cells + q] > 0)
                used++;
        }
        if (k.BoxFirst.Length < used)
        {
            k.BoxFirst = new int[used];
            k.BoxCount = new int[used];
            k.ShapeFirst = new int[used];
            k.ShapeCount = new int[used];
            k.Min = new Vector3[used];
            k.Max = new Vector3[used];
        }
        var u = 0;
        for (var q = 0; q < cells; q++)
            if (_cellCount[q] + _cellCount[cells + q] > 0)
            {
                (k.BoxFirst[u], k.BoxCount[u], k.ShapeFirst[u], k.ShapeCount[u], k.Min[u], k.Max[u]) = (_cellAt[q], _cellCount[q], _cellAt[cells + q], _cellCount[cells + q], _cellMin[q], _cellMax[q]);
                u++;
            }
        k.Cells = used;
        // The boxes in cell order.
        if (_sorted.Length < Math.Max(n * 6, sv * 6))
            _sorted = new float[Math.Max(n * 6, sv * 6)];
        for (var b = 0; b < n; b++)
        {
            var to = _cellAt[_cellOf[b]]++ * 6;
            Array.Copy(_boxes, b * 6, _sorted, to, 6);
        }
        (_boxes, _sorted) = (_sorted, _boxes);
        if (groups == 0)
            return;
        if (_sorted.Length < sv * 6)
            _sorted = new float[sv * 6];
        for (var g = 0; g < groups; g++)
        {
            var end = g + 1 < groups ? _grpFirst[g + 1] : sv;
            var verts = end - _grpFirst[g];
            var to = _cellAt[cells + _cellOf[n + g]];
            _cellAt[cells + _cellOf[n + g]] += verts;
            Array.Copy(_shapes, _grpFirst[g] * 6, _sorted, to * 6, verts * 6);
        }
        (_shapes, _sorted) = (_sorted, _shapes);
    }

    private static void DropOld()
    {
        Gone.Clear();
        foreach (var (id, k) in Casters)
            if (_frame - k.Used > 600)
            {
                Free(k);
                if (k.Door != null)
                    Free(k.Door);
                if (k.Piece != null)
                    Free(k.Piece);
                Gone.Add(id);
            }
        foreach (var id in Gone)
            Casters.Remove(id);
        Gone.Clear();
        foreach (var (id, r) in Poses)
            if (_frame - r.Seen > 600)
                Gone.Add(id);
        foreach (var id in Gone)
            Poses.Remove(id);
    }

    private static void Free(Caster k)
    {
        var b = k.Vbo;
        Native.glDeleteBuffers(1, &b);
        if (k.Shapes.Id != 0)
        {
            var sb = k.Shapes;
            Native.glDeleteBuffers(1, &sb);
        }
    }
}
