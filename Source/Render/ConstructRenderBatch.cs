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
        public float Surface;
    }

    private static Renderer.BodyBox[] _fillData = []; // (one scratch for every batch: the boxes live on the GPU, not twice)
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
        if (_fillData.Length < boxes)
            _fillData = new Renderer.BodyBox[boxes * 5 / 4];
        if (b.Moving.Length < c.PartCount)
            b.Moving = new int[c.PartCount];
        if (b.Shapes.Length < shapes)
            b.Shapes = new Shaped[shapes];
        b.MovingCount = b.ShapeCount = 0;
        var o = 0;
        var data = _fillData;
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
            var wear = p.Broken ? 1f : 1f - health;
            for (var k = 0; k < d.Boxes.Length; k++)
            {
                ref readonly var box = ref d.Boxes[k];
                if (box.Hidden)
                    continue;
                c.BoxOf(i, k, out var centre, out _);
                var tint = ConstructPaint.BoxColour(p.Paint, in box) * shade;
                if (c.Frozen)
                    tint = Vector3.Lerp(tint, Frost, 0.45f);
                var surface = Surfaces.OnPaint(box.Surface, p.Paint, box.Paintable);
                if (box.Shape != 0)
                {
                    b.Shapes[b.ShapeCount++] = new Shaped { Model = box.Model(p.Rot, centre), Colour = tint, Shape = box.Shape, Surface = Surfaces.Code(surface, wear) };
                    continue;
                }
                ref var o1 = ref data[o++];
                o1.Centre = centre;
                var half = box.Half;
                if (d.IsOrdnance && d.OrdStats is { BoxFold.Length: > 0 } fold && k < fold.BoxFold.Length && fold.BoxFold[k] > 0)
                    half.X = fold.BoxFold[k]; // (a pop-out wing, folded on its rack: ConstructRenderShells.cs)
                o1.Size = Rot24.ApplyHalf(p.Rot, half) * 2;
                o1.R = (byte)MathF.Min(tint.X * 255f + 0.5f, 255f);
                o1.G = (byte)MathF.Min(tint.Y * 255f + 0.5f, 255f);
                o1.B = (byte)MathF.Min(tint.Z * 255f + 0.5f, 255f);
                o1.Wear = (byte)(wear * 255f + 0.5f);
                o1.Surface = surface;
                o1.Mode = box.Glow ? (byte)2 : (byte)0;
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
            gfx.UploadBody(b.Gl, _fillData, count);
            b.Stamp = c.RebuildStamp;
            b.Frozen = c.Frozen;
            b.Key = LookKey(c);
        }
        gfx.DrawBody(b.Gl, body);
        var n = b.Gl.Count;
        for (var k = 0; k < b.ShapeCount; k++)
        {
            ref var s = ref b.Shapes[k];
            gfx.Draw(s.Model * body, s.Colour, 1f, mode: 0, shape: s.Shape, surface: s.Surface);
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
