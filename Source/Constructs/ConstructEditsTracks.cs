using System.Numerics;

namespace CallOfSlop;

/// <summary>What a Track action does (the wire's first byte after the kind).</summary>
public enum TrackOp : byte
{
    Make, // u16 body, u8 n, n x u16 part: a new track round these wheels
    Remove, // u16 body, u8 track
    Repair, // u16 body, u8 track
    Set, // u16 body, u8 track, 4 x f32: width, link length, tension, grip
}

/// <summary>The track tool on the server (ConstructTracks.cs; docs/sandbox/CONSTRUCTS.md, "Tracks"). Free and instant, like links.</summary>
public sealed partial class ConstructEdits
{
    private string? TrackReady(ConstructWorld world, byte player, Vector3 eye, ushort body, ReadOnlySpan<ushort> parts, out Construct c, bool settings = false)
    {
        c = null!;
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Tracks && !(settings && h.Tool == ConstructTool.Configure))
            return "take out the track tool (the tool wheel)";
        if (world.Get(body) is not { } found)
            return "that construct is gone";
        c = found;
        if (Contested(c, player) is { } defended)
            return defended;
        var near = float.MaxValue;
        foreach (var i in parts)
            if (i < c.PartCount)
                near = MathF.Min(near, Vector3.Distance(eye, ReachPoint(c, i, eye)));
        return near > ConstructBuild.Reach + 2f ? "too far away" : null;
    }

    /// <summary>Server: a new track round <paramref name="parts"/>. Null, or why not.</summary>
    public string? TrackMake(ConstructWorld world, byte player, Vector3 eye, ushort body, ReadOnlySpan<ushort> parts)
    {
        if (TrackReady(world, player, eye, body, parts, out var c) is { } why)
            return why;
        if (ConstructTracks.Check(c, parts) is { } bad)
            return bad;
        ConstructTracks.Add(c, parts, ConstructTracks.DefaultWidth(c, parts), ConstructTracks.DefaultLink(c, parts), ConstructTracks.DefaultTension, ConstructTracks.DefaultGrip);
        TrackMirror(world, player, eye, c, parts);
        ConstructTracks.Changed(world, c);
        return null;
    }

    /// <summary>The Mirror Table: the same track round the wheels' images, when they are all there.</summary>
    private void TrackMirror(ConstructWorld world, byte player, Vector3 eye, Construct c, ReadOnlySpan<ushort> parts)
    {
        if (!MirrorOn(world, player, eye, c, c.Parts[parts[0]].Seg, out var ax, out var w))
            return;
        Span<ushort> twins = stackalloc ushort[parts.Length];
        for (var k = 0; k < parts.Length; k++)
        {
            ref var p = ref c.Parts[parts[k]];
            var t = ConstructMirror.Of(p.D, p.Pos, p.Rot, ax, w);
            var at = ConstructMirror.FindPart(c, t.Def, t.Pos);
            if (t.OnPlane || at < 0)
            {
                Notices.Add((player, "Mirror: a wheel of this track has no twin on the other side: make that track yourself"));
                return;
            }
            twins[k] = (ushort)at;
        }
        if (ConstructTracks.Check(c, twins) is { } why)
            Notices.Add((player, $"Mirror: no track on the other side: {why}"));
        else
            ConstructTracks.Add(c, twins, ConstructTracks.DefaultWidth(c, twins), ConstructTracks.DefaultLink(c, twins), ConstructTracks.DefaultTension, ConstructTracks.DefaultGrip);
    }

    /// <summary>Server: a track's edit: off, mended or set. Null, or why not.</summary>
    public string? TrackEdit(ConstructWorld world, byte player, Vector3 eye, ushort body, TrackOp op, int track, ReadOnlySpan<float> values)
    {
        if (world.Get(body) is not { } found || track >= found.TrackCount)
            return "that track is gone";
        var parts = found.TrackParts.AsSpan(found.Tracks[track].Start, found.Tracks[track].Count);
        if (TrackReady(world, player, eye, body, parts, out var c, op == TrackOp.Set) is { } why)
            return why;
        switch (op)
        {
            case TrackOp.Remove:
                ConstructTracks.Remove(c, track);
                break;
            case TrackOp.Repair:
                if (!ConstructTracks.Repair(world, c, track))
                    return "that track is whole";
                return null;
            case TrackOp.Set:
                ref var t = ref c.Tracks[track];
                t.Width = Math.Clamp(values[0], ConstructTracks.MinWidth, ConstructTracks.MaxWidth);
                t.LinkLength = Math.Clamp(values[1], ConstructTracks.MinLink, ConstructTracks.MaxLink);
                t.Tension = Math.Clamp(values[2], 0, 1);
                t.Grip = Math.Clamp(values[3], ConstructTracks.MinGrip, ConstructTracks.MaxGrip);
                break;
            default:
                return "no such track edit";
        }
        ConstructTracks.Changed(world, c);
        return null;
    }
}
