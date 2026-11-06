using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Big constructs in one draw (the capital parts; docs/sandbox/CONSTRUCTS.md, "Size classes"). A body of
/// <see cref="BatchMinParts"/> parts or more keeps its plain structure (frames and panels on the body itself: no joint, no
/// wheel, no signal, no glass, not burning, no contents) as boxes in the body's frame in a buffer on the GPU, drawn with one
/// instanced draw at its pose. Only what changes frame by frame or needs its own mesh is walked: the other parts, as
/// always, and the plain parts' contoured boxes (one draw each, shared meshes, not rebuilt). The boxes are made again when
/// the construct rebuilds, is frozen or thawed, or a look every half second finds a part's health, paint or fire changed. No
/// allocation in a steady frame.
/// </summary>
public static partial class ConstructRender
{
    public const int BatchMinParts = 64;
    private const int BatchCheckFrames = 30;

    private sealed class Batch
    {
        public readonly Renderer.BodyBatch Gl = new();
        public Construct? Body;
        public float[] Data = [];
        public int[] Moving = []; // the parts walked every frame
        public int MovingCount;
        public Shaped[] Shapes = [];
        public int ShapeCount;
        public int Stamp = -1;
        public bool Frozen;
        public long Key;
        public int Age, Seen;
    }

    private struct Shaped
    {
        public Matrix4x4 Model;
        public Vector3 Colour;
        public int Shape;
    }

    private static readonly Dictionary<Construct, Batch> _batches = new();
    private static readonly List<Construct> _batchGone = new();
    private static int _batchFrame;

    /// <summary>Plain boxes and walked parts of the batches drawn last frame (the F9 window, the self-test).</summary>
    public static int LastBatchBoxes { get; private set; }
    public static int LastBatchWalked { get; private set; }

    /// <summary>Is part <paramref name="i"/> plain structure, the same from one frame to the next?</summary>
    public static bool Plain(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        return p.Seg == 0 && d.Role is PartRole.Frame or PartRole.Panel or PartRole.Foundation && p.Store == 0 && p.Burn == 0 && !d.HasGlass
            && c.MergeOf[i] == 0 && c.PartScale[i] <= 1 && !d.IsBeacon && !d.HasDial && d.Surface is not { Control: > 0 };
    }

    private static long LookKey(Construct c)
    {
        var h = 17L;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var shade = (int)(16 * Math.Clamp(p.Health / MathF.Max(1, p.D.Health), 0, 1));
            h = h * 31 + (shade | (p.Broken ? 32 : 0) | (p.Burn > 0 ? 64 : 0) | (long)p.Paint << 8);
        }
        return h;
    }

    /// <summary>The batch's boxes, moving parts and contoured boxes, as the construct is now. Returns the plain boxes (the self-test calls it with no GL).</summary>
    private static int Fill(Batch b, Construct c)
    {
        var boxes = 0;
        var shapes = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (Plain(c, i))
                foreach (var box in c.Parts[i].D.Boxes.AsSpan())
                    if (!box.Hidden)
                    {
                        if (box.Shape == 0)
                            boxes++;
                        else
                            shapes++;
                    }
        if (b.Data.Length < boxes * Renderer.BatchFloats)
            b.Data = new float[boxes * Renderer.BatchFloats * 5 / 4];
        if (b.Moving.Length < c.PartCount)
            b.Moving = new int[c.PartCount];
        if (b.Shapes.Length < shapes)
            b.Shapes = new Shaped[shapes];
        b.MovingCount = b.ShapeCount = 0;
        var o = 0;
        var data = b.Data;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (!Plain(c, i))
            {
                b.Moving[b.MovingCount++] = i;
                continue;
            }
            ref var p = ref c.Parts[i];
            var d = p.D;
            var health = Math.Clamp(p.Health / MathF.Max(1, d.Health), 0, 1);
            var shade = p.Broken ? 0.25f : 0.55f + 0.45f * health;
            for (var k = 0; k < d.Boxes.Length; k++)
            {
                ref readonly var box = ref d.Boxes[k];
                if (box.Hidden)
                    continue;
                c.BoxOf(i, k, out var centre, out _);
                var model = box.Model(p.Rot, centre);
                var tint = ConstructPaint.BoxColour(p.Paint, in box) * shade;
                if (c.Frozen)
                    tint = Vector3.Lerp(tint, Frost, 0.45f);
                if (box.Shape != 0)
                {
                    b.Shapes[b.ShapeCount++] = new Shaped { Model = model, Colour = tint, Shape = box.Shape };
                    continue;
                }
                data[o] = model.M11; data[o + 1] = model.M12; data[o + 2] = model.M13; data[o + 3] = model.M14;
                data[o + 4] = model.M21; data[o + 5] = model.M22; data[o + 6] = model.M23; data[o + 7] = model.M24;
                data[o + 8] = model.M31; data[o + 9] = model.M32; data[o + 10] = model.M33; data[o + 11] = model.M34;
                data[o + 12] = model.M41; data[o + 13] = model.M42; data[o + 14] = model.M43; data[o + 15] = model.M44;
                data[o + 16] = tint.X; data[o + 17] = tint.Y; data[o + 18] = tint.Z; data[o + 19] = 1f;
                data[o + 20] = box.Glow ? 2f : 0f; data[o + 21] = (float)Material.Grid;
                o += Renderer.BatchFloats;
            }
        }
        return boxes;
    }

    /// <summary>The self-test: how a construct splits, with no GL: plain boxes in the one draw, parts still walked each frame, contoured boxes.</summary>
    public static (int Boxes, int Walked, int Shaped) BatchCounts(Construct c)
    {
        var b = new Batch();
        var boxes = Fill(b, c);
        return (boxes, b.MovingCount, b.ShapeCount);
    }

    private static int DrawBatched(Renderer gfx, ConstructWorld world, Construct c, in Matrix4x4 body, float alpha)
    {
        if (!_batches.TryGetValue(c, out var b))
            _batches[c] = b = new Batch { Body = c };
        b.Seen = _batchFrame;
        var stale = b.Stamp != c.RebuildStamp || b.Frozen != c.Frozen;
        if (!stale && ++b.Age >= BatchCheckFrames)
        {
            b.Age = 0;
            stale = LookKey(c) != b.Key;
        }
        if (stale)
        {
            var count = Fill(b, c);
            gfx.UploadBody(b.Gl, b.Data, count);
            b.Stamp = c.RebuildStamp;
            b.Frozen = c.Frozen;
            b.Key = LookKey(c);
        }
        gfx.DrawBody(b.Gl, body);
        var n = b.Gl.Count;
        for (var k = 0; k < b.ShapeCount; k++)
        {
            ref var s = ref b.Shapes[k];
            gfx.Draw(s.Model * body, s.Colour, 1f, mode: 0, shape: s.Shape);
        }
        n += b.ShapeCount;
        for (var k = 0; k < b.MovingCount; k++)
        {
            var i = b.Moving[k];
            if (i >= c.PartCount)
                continue;
            n += DrawPart(gfx, world, c, i, body, alpha, 1f);
            if (c.Parts[i].Store != 0)
                n += DrawStored(gfx, world, c, i, body, alpha);
        }
        LastBatchBoxes += b.Gl.Count;
        LastBatchWalked += b.MovingCount + b.ShapeCount;
        return n;
    }

    /// <summary>After a frame: the batches of bodies not drawn for a while go (their GL objects with them).</summary>
    private static void BatchSweep(Renderer gfx, ConstructWorld world)
    {
        _ = world;
        _batchFrame++;
        if (_batchFrame % 120 != 0 || _batches.Count == 0)
            return;
        foreach (var (c, b) in _batches)
            if (_batchFrame - b.Seen > 600)
                _batchGone.Add(c);
        foreach (var c in _batchGone)
        {
            gfx.FreeBody(_batches[c].Gl);
            _batches.Remove(c);
        }
        _batchGone.Clear();
    }
}
