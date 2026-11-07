using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The selector's drawing (docs/sandbox/CONSTRUCTS.md, "Build stations"): the selected parts outlined, the box being dragged, and
/// the ghost of a group being placed. All of it set each frame by the tool's input. A big group draws its first
/// <see cref="MaxGroupBoxes"/> boxes and a frame round the whole; nothing here allocates once a clip's boxes are listed.
/// </summary>
public static partial class ConstructRender
{
    public const int MaxOutlines = 2048; // selected parts outlined a frame
    public const int MaxGroupBoxes = 2048; // a ghost's boxes drawn a frame

    public static PartSelection? PartSel;
    public static Construct? SelBoxBody;
    public static bool SelWorldOn; // the box is on the world (a drag started on open ground)
    public static Quaternion SelWorldTurn = Quaternion.Identity;
    public static Vector3 SelBoxMin, SelBoxMax;
    public static ConstructClip? SelGhostClip;
    public static Vector3 SelGhostPos;
    public static Quaternion SelGhostRot = Quaternion.Identity;
    public static bool SelGhostValid;

    // The fuse tool (Program.SandboxFuse.cs): the unit under the crosshair, outlined, while the construct is as it was at that rebuild.
    public static Construct? FuseShow;
    public static int FuseShowUnit, FuseShowStamp;

    private static ConstructClip? _selBoxesOf;
    private static Matrix4x4[] _selBoxes = [];
    private static Vector3[] _selBoxColours = [];
    private static float[] _selBoxAlpha = [];
    private static int[] _selBoxShape = [];
    private static int _selBoxCount;

    private static void DrawSelection(Renderer gfx, float pulse)
    {
        var blue = new Vector3(0.3f, 0.75f, 1f);
        if (FuseShow is { } fc && fc.RebuildStamp == FuseShowStamp && FuseShowUnit < fc.UnitCount)
        {
            var orange = new Vector3(1f, 0.6f, 0.15f);
            var mem = fc.UnitMembers(FuseShowUnit);
            var n = Math.Min(mem.Length, MaxOutlines);
            for (var k = 0; k < n; k++)
                Outline(gfx, fc, mem[k], orange, 0.25f + 0.1f * pulse);
        }
        if (PartSel is { Count: > 0, Body: { } c } sel)
        {
            var left = MaxOutlines;
            OutlineParts(gfx, c, sel.Items, blue, 0.2f + 0.1f * pulse, ref left);
            foreach (var e in sel.Extra)
                if (e.Body is { } b2)
                    OutlineParts(gfx, b2, e.Items, blue, 0.2f + 0.1f * pulse, ref left);
        }
        if (SelWorldOn)
        {
            var m = Matrix4x4.CreateFromQuaternion(SelWorldTurn);
            var mid = (SelBoxMin + SelBoxMax) * 0.5f;
            var half = (SelBoxMax - SelBoxMin) * 0.5f;
            gfx.Draw(Matrix4x4.CreateScale(half * 2) * Matrix4x4.CreateTranslation(mid) * m, blue, 0.06f, mode: 2);
            Outline(gfx, mid, half, m, blue, 0.9f);
        }
        if (SelBoxBody is { } b)
        {
            var m = Matrix4x4.CreateFromQuaternion(b.Rot) * Matrix4x4.CreateTranslation(b.Pos);
            var mid = (SelBoxMin + SelBoxMax) * 0.5f;
            var half = (SelBoxMax - SelBoxMin) * 0.5f;
            gfx.Draw(Matrix4x4.CreateScale(half * 2) * Matrix4x4.CreateTranslation(mid) * m, blue, 0.08f, mode: 2);
            Outline(gfx, mid, half, m, blue, 0.9f);
        }
        if (SelGhostClip is { } clip)
        {
            ListClipBoxes(clip);
            var body = Matrix4x4.CreateFromQuaternion(SelGhostRot) * Matrix4x4.CreateTranslation(SelGhostPos);
            var colour = SelGhostValid ? new Vector3(0.35f, 1f, 0.5f) : new Vector3(1f, 0.35f, 0.3f);
            for (var k = 0; k < _selBoxCount; k++)
                gfx.Draw(_selBoxes[k] * body, SelGhostValid ? _selBoxColours[k] : colour, _selBoxAlpha[k] * pulse, mode: 2, shape: _selBoxShape[k]);
            Outline(gfx, (clip.Min + clip.Max) * 0.5f, (clip.Max - clip.Min) * 0.5f, body, colour, 0.8f);
        }
    }

    private static void OutlineParts(Renderer gfx, Construct c, ReadOnlySpan<ushort> items, Vector3 colour, float alpha, ref int left)
    {
        var n = Math.Min(items.Length, left);
        left -= n;
        for (var k = 0; k < n; k++)
            if (items[k] < c.PartCount)
                Outline(gfx, c, items[k], colour, alpha);
    }

    private static void ListClipBoxes(ConstructClip clip)
    {
        if (ReferenceEquals(_selBoxesOf, clip))
            return;
        _selBoxesOf = clip;
        var c = clip.Body;
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            n += c.DefOf(i).Boxes.Length;
        n = Math.Min(n, MaxGroupBoxes);
        if (_selBoxes.Length < n)
        {
            _selBoxes = new Matrix4x4[n];
            _selBoxColours = new Vector3[n];
            _selBoxAlpha = new float[n];
            _selBoxShape = new int[n];
        }
        _selBoxCount = 0;
        for (var i = 0; i < c.PartCount && _selBoxCount < n; i++)
        {
            ref var p = ref c.Parts[i];
            var d = c.DefOf(i);
            for (var b = 0; b < d.Boxes.Length && _selBoxCount < n; b++)
            {
                ref readonly var box = ref d.Boxes[b];
                if (box.Hidden)
                    continue;
                var k = _selBoxCount++;
                _selBoxes[k] = Matrix4x4.CreateScale(1.005f) * box.Model(p.Rot, p.Pos + Rot24.Apply(p.Rot, box.Center));
                _selBoxColours[k] = GhostBoxColour(in box, true);
                _selBoxAlpha[k] = 0.35f * GhostGlass(box);
                _selBoxShape[k] = box.Shape;
            }
        }
    }
}
