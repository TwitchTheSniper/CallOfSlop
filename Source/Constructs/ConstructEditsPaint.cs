using System.Numerics;

namespace CallOfSlop;

/// <summary>The paint tool on the server (ConstructPaint.cs; docs/sandbox/CONSTRUCTS.md, "The paint tool"): free and instant in both modes.</summary>
public sealed partial class ConstructEdits
{
    /// <summary>
    /// Server: the paint tool paints (or resets) a part or a link of construct <paramref name="body"/>: <paramref name="mode"/>
    /// Part acts on part (or, with <paramref name="route"/>, link) <paramref name="index"/>; Whole on every part and link of
    /// the construct. The usual rules: no seat, the tool out, in reach, and decision 19's contested-construct check.
    /// Null, or why not.
    /// </summary>
    public string? Paint(ConstructWorld world, byte player, Vector3 eye, ushort body, PaintMode mode, bool route, int index, uint colour)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Paint)
            return "take out the paint tool (the tool wheel) to paint";
        if (world.Get(body) is not { } c)
            return "that construct is gone";
        if (mode > PaintMode.Reset)
            return "no such paint mode";
        if (mode != PaintMode.Reset && (colour == 0 || !ConstructPaint.Valid(colour)))
            return "no such colour";
        if (mode == PaintMode.Reset)
            colour = 0;
        Vector3 at;
        if (mode == PaintMode.Whole)
            at = c.WorldCom;
        else if (route)
        {
            if (index < 0 || index >= c.RouteCount)
                return "that link is gone";
            at = ConstructRoutes.WorldPoint(c, index, c.Routes[index].Count / 2);
        }
        else
        {
            if (index < 0 || index >= c.PartCount)
                return "that part is gone";
            at = ConstructRender.PartCentre(c, index);
        }
        if (Vector3.Distance(eye, at) > ConstructBuild.Reach + 1f + (mode == PaintMode.Whole ? c.Radius : 0f))
            return "too far away";
        if (Contested(c, player) is { } defended)
            return defended;
        var changed = 0;
        if (mode == PaintMode.Whole)
        {
            for (var i = 0; i < c.PartCount; i++)
                changed += SetPart(world, c, i, colour);
            for (var k = 0; k < c.RouteCount; k++)
                changed += SetRoute(world, c, k, colour);
        }
        else if (route)
            changed = SetRoute(world, c, index, colour);
        else
        {
            if (!ConstructPaint.Paintable(c.DefOf(index)))
                return $"a {c.DefOf(index).Name} has nothing that takes paint";
            changed = SetPart(world, c, index, colour);
        }
        _ = changed; // (no log line: a stroke paints many times a second and nothing here may allocate)
        return null;
    }

    private static int SetPart(ConstructWorld world, Construct c, int i, uint colour)
    {
        if (c.Parts[i].Paint == colour || colour != 0 && !ConstructPaint.Paintable(c.DefOf(i)))
            return 0;
        c.Parts[i].Paint = colour;
        world.NotePaint(c, i);
        return 1;
    }

    private static int SetRoute(ConstructWorld world, Construct c, int k, uint colour)
    {
        if (c.Routes[k].Paint == colour)
            return 0;
        c.Routes[k].Paint = colour;
        world.NotePaint(c, ConstructPaint.RouteKey | k);
        return 1;
    }
}
