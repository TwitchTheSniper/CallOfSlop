using System.Numerics;

namespace CallOfSlop;

/// <summary>The texture paint tool on the server (ConstructFinish.cs; docs/sandbox/CONSTRUCTS.md, "The texture paint tool"): free and instant in both modes, as the colour paint tool.</summary>
public sealed partial class ConstructEdits
{
    /// <summary>
    /// Server: the texture paint tool puts material <paramref name="finish"/> (a row, 0 with Reset) on part <paramref name="index"/>
    /// of construct <paramref name="body"/> (<paramref name="mode"/> Whole: on every part). The colour paint tool's rules: no seat,
    /// the tool out, in reach, and decision 19's contested-construct check. Null, or why not.
    /// </summary>
    public string? Finish(ConstructWorld world, byte player, Vector3 eye, ushort body, PaintMode mode, int index, byte finish)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Texture)
            return "take out the texture paint tool (the tool wheel) to paint textures";
        if (world.Get(body) is not { } c)
            return "that construct is gone";
        if (mode > PaintMode.Reset)
            return "no such paint mode";
        if (mode == PaintMode.Reset)
            finish = 0;
        else if (!ConstructFinish.Offered(finish))
            return "no such texture";
        Vector3 at;
        if (mode == PaintMode.Whole)
            at = c.WorldCom;
        else
        {
            if (index < 0 || index >= c.PartCount)
                return "that part is gone";
            at = ReachPoint(c, index, eye);
        }
        if (Vector3.Distance(eye, at) > ConstructBuild.Reach + 1f + (mode == PaintMode.Whole ? c.Radius : 0f))
            return "too far away";
        if (Contested(c, player) is { } defended)
            return defended;
        if (mode == PaintMode.Whole)
        {
            for (var i = 0; i < c.PartCount; i++)
                SetFinish(world, c, i, finish);
            return null;
        }
        if (!ConstructFinish.Paintable(c.DefOf(index)))
            return $"a {c.DefOf(index).Name} has nothing that takes a texture";
        SetFinish(world, c, index, finish);
        return null; // (no log line: a stroke paints many times a second and nothing here may allocate)
    }

    private static void SetFinish(ConstructWorld world, Construct c, int i, byte finish)
    {
        if (c.Parts[i].Finish == finish || finish != 0 && !ConstructFinish.Paintable(c.DefOf(i)))
            return;
        c.Parts[i].Finish = finish;
        world.NoteFinish(c, i);
    }
}
