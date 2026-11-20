using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Weld seams (user, 2026-11-19): the attach tool's RMB breaks one seam at a time instead of taking the whole part off.
/// A seam is every socket link between two parts on the same segment (a part may meet a neighbour at several sockets: one
/// seam). Breaking it cuts those links only; the parts stay where they are, held through any other seams, and when it was
/// the last way between two halves of the body the lighter side splits off as a body of its own where it is, as a cut
/// freeform attachment does (<see cref="ConstructWorld.FreeformCut"/>). Shift+RMB is the old take-off of the whole part.
/// The client picks the seam under the crosshair and draws it (Program.SandboxWeldSeam.cs, SocketWeldRender); the server
/// checks the same rules here (<see cref="ConstructEdits.BreakSeam"/>). Nothing here allocates.
/// </summary>
public static class PartSeams
{
    /// <summary>How many socket links join parts <paramref name="a"/> and <paramref name="b"/> (0: no seam).</summary>
    public static int Count(Construct c, int a, int b)
    {
        if ((uint)a >= (uint)c.PartCount || (uint)b >= (uint)c.PartCount || a == b)
            return 0;
        var n = 0;
        for (var e = c.FirstLink(a); e >= 0; e = c.NextLink(e))
            if (c.OtherEnd(e) == b)
                n++;
        return n;
    }

    /// <summary>Why the seam between <paramref name="a"/> and <paramref name="b"/> can't be broken (null: it can). The part rules only: reach, rights and tools are the caller's.</summary>
    public static string? Refusal(Construct c, int a, int b)
    {
        if (Count(c, a, b) == 0)
            return "those two parts aren't attached to each other";
        if (c.Parts[a].Seg != c.Parts[b].Seg)
            return "that is a joint's link: take the joint off instead";
        if (c.MergeOf[a] != 0 && c.MergeOf[a] == c.MergeOf[b])
            return "both are in one merged gun";
        for (var e = c.FirstLink(a); e >= 0; e = c.NextLink(e))
        {
            if (c.OtherEnd(e) != b)
                continue;
            ref var l = ref c.Links[e >> 1];
            if (l.SA >= c.DefOf(l.A).Sockets.Length || l.SB >= c.DefOf(l.B).Sockets.Length)
                return "that is a merged gun's centre socket"; // (ConstructMerge.CentreSocket)
        }
        return null;
    }

    /// <summary>
    /// The neighbour of <paramref name="part"/> whose seam is nearest <paramref name="rest"/> (a point in the rest frame, the
    /// crosshair's), and the socket of <paramref name="part"/> on that seam nearest it. -1: the part has no seam.
    /// </summary>
    public static int Nearest(Construct c, int part, Vector3 rest, out int socket)
    {
        socket = -1;
        if ((uint)part >= (uint)c.PartCount)
            return -1;
        var best = -1;
        var bestD = float.MaxValue;
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
        {
            var other = c.OtherEnd(e);
            if ((uint)other >= (uint)c.PartCount || other == part)
                continue;
            ref var l = ref c.Links[e >> 1];
            var s = (e & 1) == 0 ? l.SA : l.SB;
            c.SocketAt(part, s, out var at, out _);
            var d = Vector3.DistanceSquared(at, rest);
            if (d < bestD)
            {
                bestD = d;
                best = other;
                socket = s;
            }
        }
        return best;
    }

    /// <summary>
    /// The seam as a flat box in the rest frame: on the plane of <paramref name="a"/>'s socket <paramref name="socket"/>,
    /// as wide as the two parts' bounds overlap across it (at least a 10 cm square round the socket).
    /// </summary>
    public static void Face(Construct c, int a, int b, int socket, out Vector3 min, out Vector3 max)
    {
        c.SocketAt(a, socket, out var at, out var dir);
        var n = ConstructParts.DirVector(dir);
        var axis = MathF.Abs(n.X) >= MathF.Abs(n.Y) && MathF.Abs(n.X) >= MathF.Abs(n.Z) ? 0 : MathF.Abs(n.Y) >= MathF.Abs(n.Z) ? 1 : 2;
        c.PartBounds(a, out var amin, out var amax);
        c.PartBounds(b, out var bmin, out var bmax);
        min = Vector3.Max(amin, bmin);
        max = Vector3.Min(amax, bmax);
        for (var k = 0; k < 3; k++)
        {
            if (k == axis)
            {
                Set(ref min, k, Get(at, k) - 0.004f); // (a thin slab on the socket's plane, a few mm proud of both faces)
                Set(ref max, k, Get(at, k) + 0.004f);
            }
            else if (Get(max, k) - Get(min, k) < 0.1f)
            {
                Set(ref min, k, MathF.Min(Get(min, k), Get(at, k) - 0.05f)); // (no overlap across, or a sliver: a square round the socket)
                Set(ref max, k, MathF.Max(Get(max, k), Get(at, k) + 0.05f));
                if (Get(max, k) - Get(min, k) > 0.1f && (Get(at, k) < Get(min, k) + 0.05f || Get(at, k) > Get(max, k) - 0.05f))
                {
                    Set(ref min, k, Get(at, k) - 0.05f);
                    Set(ref max, k, Get(at, k) + 0.05f);
                }
            }
            else
            {
                Set(ref min, k, Get(min, k) - 0.004f);
                Set(ref max, k, Get(max, k) + 0.004f);
            }
        }
    }

    private static float Get(Vector3 v, int k) => k == 0 ? v.X : k == 1 ? v.Y : v.Z;

    private static void Set(ref Vector3 v, int k, float x)
    {
        if (k == 0)
            v.X = x;
        else if (k == 1)
            v.Y = x;
        else
            v.Z = x;
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>
    /// Breaks the seam between parts <paramref name="a"/> and <paramref name="b"/> of <paramref name="c"/>: every link between
    /// the two goes; what no longer reaches the root falls away as a body of its own where it is. Returns that body (null when
    /// the two still meet round another way), and how many links went.
    /// </summary>
    public Construct? BreakSeam(Construct c, int a, int b, out int cut)
    {
        cut = 0;
        if (PartSeams.Refusal(c, a, b) != null)
            return null;
        c.Dirty = true; // (first: a plain edit's journal is closed, so the compaction below takes the tree as stale)
        var ln = 0;
        for (var l = 0; l < c.LinkCount; l++)
        {
            var link = c.Links[l];
            if (link.A == a && link.B == b || link.A == b && link.B == a)
            {
                cut++;
                continue;
            }
            c.Links[ln++] = link;
        }
        c.LinkCount = ln;
        c.LinksChanged();
        c.FuseNoteCut(a, b); // (a fused unit the cut runs through is flooded again at the rebuild: ConstructFuse.cs)
        var owner = c.Owner;
        var piece = SeamSplit(c, a, b);
        if (piece != null)
        {
            piece.Wreck = false; // (cut free, not broken off)
            piece.Damaged = c.Damaged;
            piece.Owner = owner;
            StructureDirty.Add(piece.Id);
        }
        Events.Add($"construct {c.Id}: seam between parts {a} and {b} broken ({cut} link{(cut == 1 ? "" : "s")}){(piece != null ? $", construct {piece.Id} split off" : "")}");
        return piece;
    }

    /// <summary>
    /// After a seam's links went: when <paramref name="a"/> and <paramref name="b"/> no longer meet any way round, the lighter
    /// side (by mass, then parts) splits off as a body of its own where it is and the heavier keeps the body (its id, fuel,
    /// charge, seats and orders), whichever side the root is on. Returns the piece split off, or null.
    /// </summary>
    private Construct? SeamSplit(Construct c, int a, int b)
    {
        using var scratch = Scratch.Begin();
        Span<int> comp = scratch.Take<int>(c.PartCount);
        for (var i = 0; i < c.PartCount; i++)
            comp[i] = -1;
        Flood(c, a, 0, comp);
        if (comp[b] == 0)
        {
            c.Wake();
            StructureDirty.Add(c.Id);
            return null; // (they still meet round another way: nothing comes off)
        }
        Flood(c, b, 1, comp);
        float massA = 0, massB = 0;
        int nA = 0, nB = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (comp[i] == 0)
            {
                massA += c.DefOf(i).Mass;
                nA++;
            }
            else if (comp[i] == 1)
            {
                massB += c.DefOf(i).Mass;
                nB++;
            }
        }
        if (massB > massA || massB == massA && nB > nA)
            for (var i = 0; i < c.PartCount; i++)
                comp[i] = comp[i] == 0 ? 1 : comp[i] == 1 ? 0 : comp[i]; // (b's side is the bigger: it keeps the body)
        var comps = 2;
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == -1)
                Flood(c, i, comps++, comp); // (anything already apart: as a resplit does)
        Construct? first = null;
        for (var k = 1; k < comps; k++)
            first ??= SplitOff(c, comp, k);
        Compact(c, comp, 0);
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        return first;
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>
    /// The attach tool's RMB on a seam: the seam between parts <paramref name="a"/> and <paramref name="b"/> of the body is
    /// broken at once (survival too, as a freeform cut). Null, or why not.
    /// </summary>
    public string? BreakSeam(ConstructWorld world, byte player, Vector3 eye, ushort body, int a, int b)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Welder)
            return "take out the attach tool (4) to break a seam";
        if (world.Get(body) is not { } c || a < 0 || a >= c.PartCount || b < 0 || b >= c.PartCount)
            return "that part is gone";
        if (c.Loose)
            return "that is a loose part";
        if (!ConstructBuild.ToolFits(ConstructTool.Welder, c.DefOf(a)) && !ConstructBuild.ToolFits(ConstructTool.Welder, c.DefOf(b)))
            return $"the {c.DefOf(a).Name} is held with the {c.DefOf(a).Tool.Label()}";
        if (PartSeams.Refusal(c, a, b) is { } why)
            return why;
        if (c.Driver != 255)
            return "someone is driving it";
        if ((SalvageRefusal(c, player) ?? (Salvageable(c, player) ? null : Factions.RankRefusal(c.Owner, player))) is { } theirs)
            return theirs;
        if (Contested(c, player) is { } defended)
            return defended;
        if (Reachable(eye, ReachPoint(c, a, eye), ConstructBuild.Reach) is { } far)
            return far;
        for (var i = 0; i < Hands.Length; i++)
            if (Hands[i].Edit != EditKind.None && (Hands[i].Body == c.Id || Hands[i].Body2 == c.Id))
                Hands[i].Edit = EditKind.None; // (part indices are about to change)
        var nameA = c.DefOf(a).Name;
        var nameB = c.DefOf(b).Name;
        var piece = world.BreakSeam(c, a, b, out var cut);
        Done.Add($"player {player}: broke the seam {nameA} - {nameB} of construct {body} ({cut} link{(cut == 1 ? "" : "s")}){(piece != null ? $": construct {piece.Id} split off" : "")}");
        return null;
    }
}
