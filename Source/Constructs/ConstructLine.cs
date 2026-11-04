using System.Numerics;

namespace CallOfSlop;

/// <summary>What a Line Layer's stroke asks for: the first part on a socket, then a row along one axis and rows of it along another (a field).</summary>
public struct LineSpec
{
    public ushort Body, Part;
    public byte Socket, Variant;
    public byte DirU, DirV; // rest-frame directions (ConstructParts.DirVector)
    public float PitchU, PitchV; // metres between cells
    public ushort CountU, CountV; // at least 1 each
}

/// <summary>
/// The Line Layer (docs/sandbox/CONSTRUCTS.md "Build stations"): hold fire on a socket and drag: a row of the held part, then
/// a field. One server action a stroke, however long; each cell is a pose, placed by the same rules as a click
/// (<see cref="ConstructAid.FindFit"/>), so it only lands where a free socket meets it: on the existing row of sockets it
/// was dragged along, or on the cell before it. The client works out the drag, the server every cell.
/// </summary>
public static class ConstructLine
{
    public const int MaxCells = 400; // a stroke (a 20 by 20 field); more is two strokes
    public const float MaxPitch = 256;

    /// <summary>
    /// The step between cells along a rest-frame direction, for a part at turn <paramref name="rot"/>: to the next free matching
    /// socket on the construct if the row runs along existing sockets, else the part's own opposite sockets, else its size.
    /// </summary>
    public static float Pitch(Construct c, ConstructPartDef def, byte rot, in ConstructGhost g, byte dir)
    {
        var v = ConstructParts.DirVector(dir);
        // Along a row of sockets: the target's next socket that way (the same facing and kind).
        if (g.Part < c.PartCount && g.Socket < c.SocketCount(g.Part))
        {
            c.SocketAt(g.Part, g.Socket, out var at, out var facing);
            if (Math.Abs(Vector3.Dot(ConstructParts.DirVector(facing), v)) < 0.5f)
            {
                var best = float.MaxValue;
                var reach = v * MathF.Max(8f, (def.BoundsMax - def.BoundsMin).Length() * 4);
                var hits = Construct.PartScratch;
                var n = c.PartsInBox(Vector3.Min(at, at + reach) - new Vector3(0.1f), Vector3.Max(at, at + reach) + new Vector3(0.1f), hits);
                var kind = c.DefOf(g.Part).Sockets[g.Socket % c.DefOf(g.Part).Sockets.Length].Kind;
                for (var k = 0; k < n; k++)
                {
                    var d = c.DefOf(hits[k]);
                    for (var s = 0; s < d.Sockets.Length; s++)
                    {
                        if (d.Sockets[s].Kind != kind)
                            continue;
                        c.SocketAt(hits[k], s, out var sa, out var sf);
                        if (sf != facing)
                            continue;
                        var off = sa - at;
                        var along = Vector3.Dot(off, v);
                        if (along > 0.01f && along < best && (off - v * along).LengthSquared() < 0.0004f)
                            best = along;
                    }
                }
                if (best < float.MaxValue)
                    return best;
            }
        }
        // The part's own: a socket facing that way and its opposite number in line.
        var best2 = float.MaxValue;
        for (var a = 0; a < def.Sockets.Length; a++)
        {
            if (Rot24.ApplyDir(rot, def.Sockets[a].Dir) != dir)
                continue;
            var pa = Rot24.Apply(rot, def.Sockets[a].At);
            for (var b = 0; b < def.Sockets.Length; b++)
            {
                if (Rot24.ApplyDir(rot, def.Sockets[b].Dir) != (dir ^ 1) || def.Sockets[a].Kind != def.Sockets[b].Kind || def.Sockets[a].Class != def.Sockets[b].Class)
                    continue;
                var off = pa - Rot24.Apply(rot, def.Sockets[b].At);
                var along = Vector3.Dot(off, v);
                if (along > 0.01f && along < best2 && (off - v * along).LengthSquared() < 0.0004f)
                    best2 = along;
            }
        }
        if (best2 < float.MaxValue)
            return best2;
        return MathF.Max(0.05f, Vector3.Dot(Vector3.Abs(Rot24.ApplyHalf(rot, (def.BoundsMax - def.BoundsMin) * 0.5f)) * 2, Vector3.Abs(v)));
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>
    /// A Line Layer's stroke: every cell in turn, row by row, each with its mirror image if a plane is on. Survival takes each
    /// part from the inventory, the network, then the hands, and places what it can. Null, or why nothing happened; the
    /// count goes on the HUD. One action: a stroke costs what its parts cost to fit, once.
    /// </summary>
    public string? BuildLine(ConstructWorld world, byte player, Vector3 eye, in LineSpec s)
    {
        ref var h = ref Hands[player];
        if (AidWhy(world, player, eye, BuildAid.Line) is { } off)
            return off;
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(h.Held) is not { } def)
            return "hold a part first";
        if (!ConstructBuild.ToolFits(h.Tool, def))
            return $"the {def.Name} needs the {def.Tool.ToString().ToLowerInvariant()}";
        if (world.Get(s.Body) is not { } c)
            return "that construct is gone";
        if (Contested(c, player) is { } defended)
            return defended;
        if (c.Driver != 255 && c.Driver != player)
            return "someone is driving it";
        var claim = Claims(c, player);
        if (claim && ConstructPlace.LimitRefusal(world, player) is { } full)
            return full;
        if (s.DirU > 5 || s.DirV > 5 || s.CountU == 0 || s.CountV == 0 || s.CountU * s.CountV > ConstructLine.MaxCells || !(s.PitchU is > 0 and <= ConstructLine.MaxPitch) || !(s.PitchV is > 0 and <= ConstructLine.MaxPitch))
            return "bad stroke";
        var v = s.Variant % Math.Max(1, def.Sockets.Length * 4);
        if (ConstructBuild.Place(c, s.Part, s.Socket, def, v / 4, v % 4, out var g) is { } why)
            return why;
        c.SocketAt(s.Part, s.Socket, out var socketAt, out _);
        if (Reachable(eye, c.ToWorld(socketAt), ConstructBuild.Reach) is { } far && Reachable(eye, c.ToWorld(g.Pos), ConstructBuild.Reach) != null)
            return far;
        var reach = BeaconTable.Current.Range("line");
        var frame = c.FreeformCount > 0 ? c.FrameOf(g.Seg) : 0;
        Vector3 start = g.Pos, u = ConstructParts.DirVector(s.DirU) * s.PitchU, w = ConstructParts.DirVector(s.DirV) * s.PitchV;
        int placed = 0, blocked = 0, shortOf = 0;
        var mirrored = false;
        for (var j = 0; j < s.CountV && shortOf == 0; j++)
            for (var i = 0; i < s.CountU; i++)
            {
                var pos = ConstructWorld.Quantise(start + u * i + w * j);
                ConstructGhost gi;
                if (i == 0 && j == 0)
                    gi = g;
                else if (Vector3.Distance(eye, c.ToWorld(pos)) > reach || !ConstructAid.FindFit(c, def, pos, g.Rot, frame, out gi))
                {
                    blocked++;
                    continue;
                }
                if (!TakeForAid(world, player, def, eye))
                {
                    shortOf = s.CountU * s.CountV - placed - blocked;
                    break;
                }
                var k = ConstructBuild.Attach(world, c, def, gi);
                if (k < 0)
                {
                    ReturnFromAid(world, player, def, eye, c.ToWorld(pos));
                    blocked++;
                    continue;
                }
                placed++;
                SnapAfter(player, ref h, def, c, k);
                MirrorAttach(world, player, ref h, c, def, k, eye);
                mirrored |= _lastMirrored;
            }
        if (placed > 0 && claim)
        {
            c.Wreck = false;
            c.Owner = player;
            world.StructureDirty.Add(c.Id);
        }
        if (h.Held == ConstructParts.None)
            Refill(world, player, ref h, def, eye);
        Notices.Add((player, SandboxKeys.Line("Line", $"{placed} placed{(mirrored ? " and mirrored" : "")}", SandboxKeys.Join(shortOf > 0 ? $"{shortOf} short" : "", blocked > 0 ? $"{blocked} didn't fit" : ""))));
        Done.Add($"player {player}: line of {placed} {def.Name} on construct {c.Id}, {shortOf} short, {blocked} didn't fit");
        return null;
    }
}
