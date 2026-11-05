using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// One end of a no-collide pair: a part, named by <see cref="CPart.Tag"/> like a connector's end, so it survives other parts
/// coming off, splits and joins. <see cref="Whole"/>: the whole rigid piece the part is on (a plain construct, or one
/// moving segment of a mechanism); not: that part alone.
/// </summary>
public struct NoCollideEnd
{
    public ushort Tag; // server: the part's tag
    public ushort Body, Part; // where it is now
    public bool Whole;
    public byte Group; // server: its rigid group now (Construct.MoveSeg of its segment)
}

/// <summary>A pair that doesn't collide (docs/sandbox/CONSTRUCTS.md, "No-collide pairs").</summary>
public struct CNoCollide
{
    public NoCollideEnd A, B;
    public int Next; // server: the next pair in its body pair's hash chain, -1 at the end
}

/// <summary>A no-collide pair in a blueprint: both ends on steps of it.</summary>
public readonly record struct NoCollideSpec(int StepA, bool WholeA, int StepB, bool WholeB);

/// <summary>
/// No-collide pairs (the attach tool's No-collide mode; docs/sandbox/CONSTRUCTS.md, "No-collide pairs"). One small list on the
/// world, grown with use, and a hash on the two bodies, remade only when the list changes: a body stamped with
/// <see cref="NoCollideStamp"/> has a pair, so a pair of bodies without one costs two integer compares, and with none in the world
/// one more. Pairs live on the server's solve; a client holds the list for the tool's marker and look line. A pair goes when
/// the part it names goes. Parts of one construct don't collide with each other at all, so a pair within a construct does
/// nothing until the two come apart, and it stays so that they don't then meet: that is also what a blueprint can carry.
/// Allocates nothing per tick.
/// </summary>
public sealed partial class ConstructWorld
{
    public const int MaxNoCollide = 1024;

    public CNoCollide[] NoCollides = [];
    public int NoCollideCount;
    public bool NoCollideDirty; // server: send the list
    public int NoCollideVersion; // bumped by every change on any peer: the tool's look line is remade then
    internal int NoCollideStamp = 1;

    private int[] _ncHeads = [];

    private static int NcHash(ushort a, ushort b, int mask)
    {
        var (lo, hi) = a < b ? (a, b) : (b, a);
        return (lo * 31 + hi) & mask;
    }

    private void NcChanged()
    {
        NoCollideDirty = true;
        NoCollideVersion++;
        NcRehash();
    }

    /// <summary>The hash and the bodies' stamps, after a change to the list.</summary>
    private void NcRehash()
    {
        NoCollideStamp = NoCollideStamp == int.MaxValue ? 1 : NoCollideStamp + 1;
        var size = 16;
        while (size < NoCollideCount * 2)
            size *= 2;
        if (_ncHeads.Length != size)
            _ncHeads = new int[size];
        Array.Fill(_ncHeads, -1);
        for (var k = 0; k < NoCollideCount; k++)
        {
            ref var p = ref NoCollides[k];
            p.Next = -1;
            if (p.A.Body == p.B.Body || Get(p.A.Body) is not { } a || Get(p.B.Body) is not { } b)
                continue; // (one construct's parts never meet)
            a.NoCollideStamp = b.NoCollideStamp = NoCollideStamp;
            ref var head = ref _ncHeads[NcHash(p.A.Body, p.B.Body, size - 1)];
            p.Next = head;
            head = k;
        }
    }

    /// <summary>The solve: the first pair that may be between two bodies, or -1. Nearly always answered by the stamps.</summary>
    internal int FirstNoCollide(Construct a, Construct b) =>
        NoCollideCount == 0 || a.NoCollideStamp != NoCollideStamp || b.NoCollideStamp != NoCollideStamp ? -1 : _ncHeads[NcHash(a.Id, b.Id, _ncHeads.Length - 1)];

    private static bool NcEnd(in NoCollideEnd e, Construct c, int group, int part) => e.Body == c.Id && (e.Whole ? e.Group == group : e.Part == part);

    /// <summary>Do two rigid groups pass through each other whole? <paramref name="first"/> from <see cref="FirstNoCollide"/>.</summary>
    internal bool NoCollideGroups(int first, Construct a, int ga, Construct b, int gb)
    {
        for (var k = first; k >= 0; k = NoCollides[k].Next)
        {
            ref var p = ref NoCollides[k];
            if (p.A.Whole && p.B.Whole && (NcEnd(p.A, a, ga, -1) && NcEnd(p.B, b, gb, -1) || NcEnd(p.A, b, gb, -1) && NcEnd(p.B, a, ga, -1)))
                return true;
        }
        return false;
    }

    /// <summary>Do these two parts pass through each other?</summary>
    internal bool NoCollideParts(int first, Construct a, int ga, int pa, Construct b, int gb, int pb)
    {
        for (var k = first; k >= 0; k = NoCollides[k].Next)
        {
            ref var p = ref NoCollides[k];
            if (NcEnd(p.A, a, ga, pa) && NcEnd(p.B, b, gb, pb) || NcEnd(p.A, b, gb, pb) && NcEnd(p.B, a, ga, pa))
                return true;
        }
        return false;
    }

    private static bool NcCovers(in NoCollideEnd e, Construct c, int part) =>
        e.Body == c.Id && e.Part < c.PartCount && (e.Part == part || e.Whole && c.MoveSeg(c.Parts[e.Part].Seg) == c.MoveSeg(c.Parts[part].Seg));

    /// <summary>The pair that covers these two parts, or -1 (any peer: the tool's question, not the solve's).</summary>
    public int NoCollideOf(Construct a, int pa, Construct b, int pb)
    {
        if (pa < 0 || pa >= a.PartCount || pb < 0 || pb >= b.PartCount)
            return -1;
        for (var k = 0; k < NoCollideCount; k++)
        {
            ref var p = ref NoCollides[k];
            if (NcCovers(p.A, a, pa) && NcCovers(p.B, b, pb) || NcCovers(p.A, b, pb) && NcCovers(p.B, a, pa))
                return k;
        }
        return -1;
    }

    /// <summary>How many pairs a part is in.</summary>
    public int NoCollidesOn(Construct c, int part)
    {
        var n = 0;
        for (var k = 0; k < NoCollideCount; k++)
            if (NcCovers(NoCollides[k].A, c, part) || NcCovers(NoCollides[k].B, c, part))
                n++;
        return n;
    }

    /// <summary>The tool's and the server's rule for a new pair. Null, or why not.</summary>
    public string? CheckNoCollide(Construct a, int pa, Construct b, int pb)
    {
        if (pa < 0 || pa >= a.PartCount || pb < 0 || pb >= b.PartCount)
            return "that part is gone";
        if (a == b && a.MoveSeg(a.Parts[pa].Seg) == a.MoveSeg(a.Parts[pb].Seg))
            return "that's one rigid piece";
        return NoCollideCount >= MaxNoCollide ? "no room for another" : null;
    }

    /// <summary>A new pair; what it touches wakes. False when the list is full.</summary>
    public bool AddNoCollide(Construct a, int pa, bool wholeA, Construct b, int pb, bool wholeB)
    {
        if (NoCollideCount >= MaxNoCollide || pa >= a.PartCount || pb >= b.PartCount)
            return false;
        if (NoCollideCount == NoCollides.Length)
            Array.Resize(ref NoCollides, Math.Clamp(NoCollides.Length * 2, 4, MaxNoCollide));
        EnsureTag(a, pa);
        EnsureTag(b, pb);
        NoCollides[NoCollideCount++] = new CNoCollide
        {
            A = new NoCollideEnd { Tag = a.Parts[pa].Tag, Body = a.Id, Part = (ushort)pa, Whole = wholeA, Group = (byte)a.MoveSeg(a.Parts[pa].Seg) },
            B = new NoCollideEnd { Tag = b.Parts[pb].Tag, Body = b.Id, Part = (ushort)pb, Whole = wholeB, Group = (byte)b.MoveSeg(b.Parts[pb].Seg) },
        };
        a.Wake();
        b.Wake();
        NcChanged();
        return true;
    }

    /// <summary>Takes pair <paramref name="k"/> off; what it touches wakes, so what overlaps is pushed apart.</summary>
    public void RemoveNoCollide(int k)
    {
        if (k < 0 || k >= NoCollideCount)
            return;
        Get(NoCollides[k].A.Body)?.Wake();
        Get(NoCollides[k].B.Body)?.Wake();
        NoCollides[k] = NoCollides[--NoCollideCount];
        NcChanged();
    }

    private void ClearNoCollides()
    {
        if (NoCollideCount > 0)
            NoCollideDirty = true;
        NoCollideCount = 0;
        NoCollideVersion++;
    }

    /// <summary>
    /// Server, every tick before the physics: each pair finds its parts (renumbered, split off, joined) and goes when one has
    /// gone, as does a rigid group that changed. Nothing here when there are none, and it allocates nothing.
    /// </summary>
    private void StepNoCollide()
    {
        if (NoCollideCount == 0)
            return;
        var moved = false;
        for (var k = NoCollideCount - 1; k >= 0; k--)
        {
            ref var p = ref NoCollides[k];
            if (!NcResolve(ref p.A, ref moved) || !NcResolve(ref p.B, ref moved))
            {
                NoCollides[k] = NoCollides[--NoCollideCount];
                moved = true;
            }
        }
        if (moved)
            NcChanged();
    }

    private bool NcResolve(ref NoCollideEnd e, ref bool moved)
    {
        var (body, part) = (e.Body, e.Part);
        if (Resolve(e.Tag, ref e.Body, ref e.Part) is not { } c)
            return false;
        var group = c.Dirty ? e.Group : (byte)c.MoveSeg(c.Parts[e.Part].Seg);
        moved |= body != e.Body || part != e.Part || group != e.Group;
        e.Group = group;
        return true;
    }

    /// <summary>Client: the list as the server has it.</summary>
    public void ClientPutNoCollides(ReadOnlySpan<CNoCollide> list)
    {
        if (NoCollides.Length < list.Length)
            NoCollides = new CNoCollide[list.Length];
        list.CopyTo(NoCollides);
        NoCollideCount = list.Length;
        NoCollideVersion++;
    }

    /// <summary>The pairs with both ends on <paramref name="src"/>, onto its copy <paramref name="c"/> (a step's index for each part: <paramref name="marked"/>).</summary>
    private void CopyNoCollides(ConstructWorld from, Construct src, int[] marked, Construct c)
    {
        for (var k = 0; k < from.NoCollideCount; k++)
        {
            ref var p = ref from.NoCollides[k];
            if (p.A.Body != src.Id || p.B.Body != src.Id || p.A.Part >= marked.Length || p.B.Part >= marked.Length || marked[p.A.Part] < 0 || marked[p.B.Part] < 0)
                continue;
            AddNoCollide(c, marked[p.A.Part], p.A.Whole, c, marked[p.B.Part], p.B.Whole);
        }
    }

    /// <summary>A blueprint's pair onto what it built. Null, or why not.</summary>
    public string? BuildNoCollide(Construct c, in NoCollideSpec s, ReadOnlySpan<int> partOf)
    {
        if (s.StepA < 0 || s.StepB < 0 || s.StepA >= partOf.Length || s.StepB >= partOf.Length || partOf[s.StepA] < 0 || partOf[s.StepB] < 0)
            return "no such step";
        if (CheckNoCollide(c, partOf[s.StepA], c, partOf[s.StepB]) is { } why)
            return why;
        return AddNoCollide(c, partOf[s.StepA], s.WholeA, c, partOf[s.StepB], s.WholeB) ? null : "no room for another";
    }

    /// <summary>The pairs with both ends on <paramref name="c"/> as a blueprint's "noCollide" list ("" for none); a step is its part's index.</summary>
    public string NoCollidesJson(Construct c)
    {
        var sb = new StringBuilder();
        for (var k = 0; k < NoCollideCount; k++)
        {
            ref var p = ref NoCollides[k];
            if (p.A.Body != c.Id || p.B.Body != c.Id)
                continue;
            sb.Append(sb.Length == 0 ? "[\n" : ",\n");
            sb.Append($"    {{ \"a\": {p.A.Part}, \"b\": {p.B.Part}, \"wholeA\": {(p.A.Whole ? "true" : "false")}, \"wholeB\": {(p.B.Whole ? "true" : "false")} }}");
        }
        return sb.Length == 0 ? "" : sb.Append("\n  ]").ToString();
    }

    /// <summary>One entry of a blueprint's "noCollide" list.</summary>
    public static NoCollideSpec ParseNoCollide(JsonElement e, string file)
    {
        try
        {
            return new NoCollideSpec(e.GetProperty("a").GetInt32(), e.GetProperty("wholeA").GetBoolean(), e.GetProperty("b").GetInt32(), e.GetProperty("wholeB").GetBoolean());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ConstructPartException($"{file}: a noCollide pair needs a, b, wholeA, wholeB: {ex.Message}");
        }
    }

    /// <summary>A snapshot of the list for a save.</summary>
    private void SnapshotNoCollides(WorldSnapshot s)
    {
        WorldSnapshot.Grow(ref s.NoCollides, NoCollideCount);
        Array.Copy(NoCollides, s.NoCollides, s.NoCollideCount = NoCollideCount);
    }

    public static void WriteNoCollides(SaveWriter w, WorldSnapshot s)
    {
        w.I32(s.NoCollideCount);
        foreach (ref var p in s.NoCollides.AsSpan(0, s.NoCollideCount))
        {
            WriteEnd(w, p.A);
            WriteEnd(w, p.B);
        }

        static void WriteEnd(SaveWriter w, in NoCollideEnd e) => w.U16(e.Tag).U16(e.Body).U16(e.Part).U8((byte)(e.Whole ? 1 : 0)).U8(e.Group);
    }

    public void ReadNoCollides(SaveReader r)
    {
        var n = r.I32();
        NoCollideCount = 0;
        for (var k = 0; k < n; k++)
        {
            var p = new CNoCollide { A = ReadEnd(r), B = ReadEnd(r) };
            if (NoCollideCount >= MaxNoCollide)
                continue;
            if (NoCollideCount == NoCollides.Length)
                Array.Resize(ref NoCollides, Math.Clamp(NoCollides.Length * 2, 4, MaxNoCollide));
            NoCollides[NoCollideCount++] = p;
        }
        NcChanged();

        static NoCollideEnd ReadEnd(SaveReader r) => new() { Tag = r.U16(), Body = r.U16(), Part = r.U16(), Whole = r.U8() != 0, Group = r.U8() };
    }

    /// <summary>After a load that left parts out: pairs whose part didn't come go now.</summary>
    private void RelinkNoCollides()
    {
        for (var k = NoCollideCount - 1; k >= 0; k--)
        {
            ref var p = ref NoCollides[k];
            if (Resolve(p.A.Tag, ref p.A.Body, ref p.A.Part) == null || Resolve(p.B.Tag, ref p.B.Body, ref p.B.Part) == null)
                NoCollides[k] = NoCollides[--NoCollideCount];
        }
        NcChanged();
    }
}

/// <summary>What the attach tool asks of no-collide pairs, on the server.</summary>
public sealed partial class ConstructEdits
{
    /// <summary>The attach tool's No-collide mode: the pair stops colliding, or collides again if it was. Null, or why not.</summary>
    public string? NoCollideToggle(ConstructWorld world, byte player, Vector3 eye, ushort bodyA, ushort partA, bool wholeA, ushort bodyB, ushort partB, bool wholeB)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Welder)
            return "take out the attach tool";
        if (world.Get(bodyA) is not { } a || world.Get(bodyB) is not { } b || partA >= a.PartCount || partB >= b.PartCount)
            return "that is gone";
        if ((Contested(a, player) ?? Contested(b, player)) is { } defended)
            return defended;
        var near = MathF.Min(Vector3.Distance(eye, ConstructWorld.PartPoint(a, partA, Vector3.Zero)), Vector3.Distance(eye, ConstructWorld.PartPoint(b, partB, Vector3.Zero)));
        if (near > ConnectorTake + MathF.Max(a.Radius, b.Radius))
            return "too far away";
        var k = world.NoCollideOf(a, partA, b, partB);
        if (k >= 0)
        {
            world.RemoveNoCollide(k);
            Done.Add($"player {player}: construct {bodyA} and {bodyB} collide again");
            return null;
        }
        if (world.CheckNoCollide(a, partA, b, partB) is { } why)
            return why;
        if (!world.AddNoCollide(a, partA, wholeA, b, partB, wholeB))
            return "no room for another";
        Done.Add($"player {player}: construct {bodyA} and {bodyB} no longer collide");
        return null;
    }
}
