using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Selector's frames round sub-blueprint uses (docs/sandbox/design/sub-blueprints.md section 1): while the selector is out, each
/// use on a body in view has a thin cyan frame round its parts and a label, "Submodule Pod v1 (live)"; a use whose parts changed since
/// its stamp is framed amber, "...: changed". Only the outermost uses are framed. A body's frames are worked out when its structure or
/// its table changes (its rebuild stamp), never per frame; a frame draws twelve thin boxes and allocates nothing.
/// </summary>
public static class UseFrameRender
{
    public const int MaxFrames = 96; // uses framed in one frame at most
    public const float MaxDistance = 60f; // bodies farther from the eye than this aren't framed
    private static readonly Vector3 Cyan = new(0.25f, 0.9f, 1f), Amber = new(1f, 0.72f, 0.15f);

    /// <summary>The frame the selector was last out (ConstructConveyor.Clock): frames show for a moment after, never with it away.</summary>
    public static double ShownAt = -10;

    private sealed class BodyFrames
    {
        public int Stamp = -1, Uses = -1;
        public readonly List<(Vector3 Min, Vector3 Max, string Label, bool Changed)> Frames = new();
    }

    private static readonly Dictionary<ushort, BodyFrames> Cache = new();

    /// <summary>This frame's labels (the ImGui pass draws them: <c>Ui.DrawUseLabels</c>).</summary>
    public static readonly List<(Vector3 At, string Text, bool Changed)> Labels = new(MaxFrames);

    public static void Draw(Renderer gfx, ConstructWorld world, Vector3 eye)
    {
        Labels.Clear();
        if (ConstructConveyor.Clock - ShownAt > 0.3)
            return;
        var n = 0;
        foreach (var c in world.Bodies)
        {
            if (c.Uses is not { Count: > 0 } || Vector3.DistanceSquared(c.Pos, eye) > (MaxDistance + c.Radius) * (MaxDistance + c.Radius))
                continue;
            var bf = Of(c);
            var body = Matrix4x4.CreateFromQuaternion(c.Rot) * Matrix4x4.CreateTranslation(c.Pos);
            foreach (var (min, max, label, changed) in bf.Frames)
            {
                if (n++ >= MaxFrames)
                    return;
                Box(gfx, min, max, body, changed ? Amber : Cyan);
                Labels.Add((Vector3.Transform(new Vector3((min.X + max.X) * 0.5f, max.Y + 0.15f, (min.Z + max.Z) * 0.5f), body), label, changed));
            }
        }
    }

    /// <summary>A body's frames: each outermost use's bounds in the body's rest frame, its label, and whether its parts changed.</summary>
    private static BodyFrames Of(Construct c)
    {
        if (!Cache.TryGetValue(c.Id, out var bf))
        {
            if (Cache.Count > 512)
                Cache.Clear(); // (bodies come and go: a stale entry costs a rebuild of its frames, nothing more)
            Cache[c.Id] = bf = new BodyFrames();
        }
        if (bf.Stamp == c.RebuildStamp && bf.Uses == c.Uses!.Count)
            return bf;
        bf.Stamp = c.RebuildStamp;
        bf.Uses = c.Uses!.Count;
        bf.Frames.Clear();
        for (var t = 0; t < c.Uses.Count; t++)
        {
            var tag = c.Uses[t];
            if (tag.Parent >= 0)
                continue;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var any = false;
            for (var i = 0; i < c.PartCount; i++)
                if (SubBlueprints.In(c, i, t))
                {
                    c.PartBounds(i, out var a, out var b);
                    min = Vector3.Min(min, a);
                    max = Vector3.Max(max, b);
                    any = true;
                }
            if (!any)
                continue;
            var changed = SubBlueprints.HashOf(c, t) != tag.Hash;
            var label = $"{tag.Name}{(tag.Version > 0 ? $" v{tag.Version}" : "")} ({(tag.Lock ? "locked" : "live")}){(changed ? ": changed" : "")}";
            bf.Frames.Add((min - new Vector3(0.03f), max + new Vector3(0.03f), label, changed));
        }
        return bf;
    }

    /// <summary>A box's twelve edges as thin boxes (the bay outline's way: RouteRender.DrawBays).</summary>
    private static void Box(Renderer gfx, Vector3 min, Vector3 max, Matrix4x4 body, Vector3 colour)
    {
        const float t = 0.02f;
        var size = max - min;
        var mid = (min + max) * 0.5f;
        for (var a = -1; a <= 1; a += 2)
            for (var b = -1; b <= 1; b += 2)
            {
                gfx.Draw(Matrix4x4.CreateScale(size.X + t, t, t) * Matrix4x4.CreateTranslation(mid.X, mid.Y + a * size.Y * 0.5f, mid.Z + b * size.Z * 0.5f) * body, colour, 0.85f);
                gfx.Draw(Matrix4x4.CreateScale(t, size.Y + t, t) * Matrix4x4.CreateTranslation(mid.X + a * size.X * 0.5f, mid.Y, mid.Z + b * size.Z * 0.5f) * body, colour, 0.85f);
                gfx.Draw(Matrix4x4.CreateScale(t, t, size.Z + t) * Matrix4x4.CreateTranslation(mid.X + a * size.X * 0.5f, mid.Y + b * size.Y * 0.5f, mid.Z) * body, colour, 0.85f);
            }
    }
}
