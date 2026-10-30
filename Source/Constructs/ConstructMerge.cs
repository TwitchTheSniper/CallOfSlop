using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The merge rules (Constructs/ReceiverMerge.json; docs/sandbox/CONSTRUCTS.md, "Merged receivers"): how big a square of
/// receivers merges, whether mixed types merge, and how an NxN merge's numbers scale (each a power of N).
/// </summary>
public sealed class ReceiverMergeRules
{
    public const string Format = "call-of-slop-receiver-merge/1";

    public int MinSize = 2, MaxSize = 6;
    public bool SameTypeOnly = true;
    public float Damage = 2, FireRate = -1, Recoil = 2, AmmoPerShot = 2, Magazine = 2, Spread = 0, Range = 0, PartMass = 2;
    public string Problem = ""; // why the file was refused ("" when it loaded; the defaults are used otherwise)

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "ReceiverMerge.json");

    private static ReceiverMergeRules? _current;

    /// <summary>The rules in use (loaded once from the file; the defaults above when it is missing or refused).</summary>
    public static ReceiverMergeRules Current => _current ??= Load(FilePath);

    /// <summary>Sets the rules in use (the self-test; null: load the file again).</summary>
    public static void Use(ReceiverMergeRules? rules) => _current = rules;

    public static ReceiverMergeRules Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ReceiverMergeRules { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    /// <summary>Reads a rules file; a bad one gives the defaults with <see cref="Problem"/> saying why.</summary>
    public static ReceiverMergeRules Parse(string json)
    {
        var r = new ReceiverMergeRules();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("the file is not a JSON object");
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                switch (p.Name)
                {
                    case "format" or "about": break;
                    case "minSize": r.MinSize = Int(p.Value, "minSize", 2, 9); break;
                    case "maxSize": r.MaxSize = Int(p.Value, "maxSize", 2, 9); break;
                    case "sameTypeOnly":
                        r.SameTypeOnly = p.Value.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => throw new FormatException("\"sameTypeOnly\" is true or false"),
                        };
                        break;
                    case "powers":
                        if (p.Value.ValueKind != JsonValueKind.Object)
                            throw new FormatException("\"powers\" is an object");
                        foreach (var q in p.Value.EnumerateObject())
                        {
                            var v = Pow(q.Value, q.Name);
                            switch (q.Name)
                            {
                                case "damage": r.Damage = v; break;
                                case "fireRate": r.FireRate = v; break;
                                case "recoil": r.Recoil = v; break;
                                case "ammoPerShot": r.AmmoPerShot = v; break;
                                case "magazine": r.Magazine = v; break;
                                case "spread": r.Spread = v; break;
                                case "range": r.Range = v; break;
                                case "partMass": r.PartMass = v; break;
                                default: throw new FormatException($"unknown power \"{q.Name}\"");
                            }
                        }
                        break;
                    default: throw new FormatException($"unknown key \"{p.Name}\"");
                }
            if (r.MinSize > r.MaxSize)
                throw new FormatException("\"minSize\" is more than \"maxSize\"");
            return r;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new ReceiverMergeRules { Problem = ex.Message };
        }
    }

    private static int Int(JsonElement e, string what, int min, int max)
    {
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var v) || v < min || v > max)
            throw new FormatException($"\"{what}\" is a whole number from {min} to {max}");
        return v;
    }

    private static float Pow(JsonElement e, string what)
    {
        if (e.ValueKind != JsonValueKind.Number)
            throw new FormatException($"power \"{what}\" is a number");
        var v = e.GetSingle();
        if (v < -3 || v > 3)
            throw new FormatException($"power \"{what}\" is from -3 to 3");
        return v;
    }

    /// <summary>N to the power <paramref name="power"/>.</summary>
    public static float Times(int n, float power) => n <= 1 || power == 0 ? 1f : MathF.Pow(n, power);
}

/// <summary>One merged receiver: an NxN square of identical receivers drawn as one receiver N times bigger (derived from the layout).</summary>
public struct CMerge
{
    public byte Lead; // the member with the lowest part index: the merged gun's part, and the one whose centre sockets take gun parts
    public byte N;
    public byte Rot, Seg;
    public Vector3 Origin; // rest frame: where a receiver N times the size has its origin (its back flush with the members' backs)
}

public sealed partial class Construct
{
    public const int MaxMerges = MaxParts / 4;

    // Merged receivers (ConstructMerge.cs), rebuilt with the caches: derived from the parts, never sent or stored.
    public readonly CMerge[] Merges = new CMerge[MaxMerges];
    public int MergeCount;
    public readonly byte[] MergeOf = new byte[MaxParts]; // the merge a receiver is a member of, plus one (0: none)
    public readonly byte[] PartScale = new byte[MaxParts]; // a gun part on a merged receiver's centre sockets: drawn and simulated this many times bigger (0 or 1: as its file)

    /// <summary>How many times bigger part <paramref name="i"/> is (a gun part on a merged receiver: N; else 1).</summary>
    public float ScaleOf(int i) => (uint)i < (uint)PartCount && PartScale[i] > 1 ? PartScale[i] : 1f;

    /// <summary>Is part <paramref name="i"/> the lead of a merged receiver (its centre sockets come after its own)?</summary>
    public bool MergeLead(int i) => (uint)i < (uint)PartCount && MergeOf[i] != 0 && Merges[MergeOf[i] - 1].Lead == i;

    /// <summary>Part <paramref name="i"/>'s sockets: its own, then (a merged receiver's lead) the merged receiver's centre set.</summary>
    public int SocketCount(int i) => DefOf(i).Sockets.Length * (MergeLead(i) ? 2 : 1);

    /// <summary>Part <paramref name="i"/>'s mass: its file's, a scaled gun part's times N to the rules' partMass power.</summary>
    public float PartMass(int i) => PartScale[i] > 1 ? DefOf(i).Mass * ReceiverMergeRules.Times(PartScale[i], ReceiverMergeRules.Current.PartMass) : DefOf(i).Mass;
}

/// <summary>
/// Merged receivers (board item dev-sandbox-receiver-merge-rm01; docs/sandbox/CONSTRUCTS.md, "Merged receivers"): construct
/// weapon receivers side by side in one plane, facing the same way, that form an NxN square (N >= 2) are one receiver N
/// times bigger, with one centre socket set (the members' own sockets take no gun parts). Found greedily, largest
/// square first, in a fixed order, from the parts alone (every client and every copy finds the same), with no
/// allocation. The server settles each attach and removal: gun parts on the members' own sockets pop off to the placer,
/// and the centre parts move to the new layout's centre socket (or pop off when there is none).
/// </summary>
public static class ConstructMerge
{
    private const float Tolerance = 0.006f; // metres: how far off its grid a member may sit (positions are on a 1 cm grid)

    private static bool Candidate(Construct c, int i, int exclude) =>
        i != exclude && c.Parts[i].Working && c.Parts[i].D.IsReceiverGun;

    private static Vector3 Local(in CPart p) => Vector3.Transform(p.Pos, Quaternion.Conjugate(Rot24.Quat(p.Rot)));

    /// <summary>
    /// Finds the merged receivers of <paramref name="c"/> (leaving part <paramref name="exclude"/> out, -1: none) into
    /// <paramref name="groups"/> and <paramref name="mergeOf"/> (group plus one per part). Returns how many.
    /// </summary>
    public static int Detect(Construct c, int exclude, Span<CMerge> groups, Span<byte> mergeOf)
    {
        var rules = ReceiverMergeRules.Current;
        mergeOf.Clear();
        var count = 0;
        Span<bool> classed = stackalloc bool[Construct.MaxParts];
        Span<byte> members = stackalloc byte[Construct.MaxParts];
        Span<short> cx = stackalloc short[Construct.MaxParts], cy = stackalloc short[Construct.MaxParts];
        Span<bool> used = stackalloc bool[Construct.MaxParts];
        Span<byte> grid = stackalloc byte[1024];
        for (var i = 0; i < c.PartCount; i++)
        {
            if (classed[i] || !Candidate(c, i, exclude))
                continue;
            classed[i] = true;
            ref var pi = ref c.Parts[i];
            var di = pi.D;
            var size = di.BoundsMax - di.BoundsMin;
            var li = Local(pi);
            var n = 0;
            members[n] = (byte)i;
            cx[n] = cy[n] = 0;
            n++;
            for (var j = i + 1; j < c.PartCount; j++)
            {
                if (classed[j] || !Candidate(c, j, exclude))
                    continue;
                ref var pj = ref c.Parts[j];
                if (pj.Rot != pi.Rot || pj.Seg != pi.Seg)
                    continue;
                var dj = pj.D;
                if (rules.SameTypeOnly ? dj != di : Vector3.DistanceSquared(dj.BoundsMax - dj.BoundsMin, size) > 1e-6f || Vector3.DistanceSquared(dj.BoundsMin, di.BoundsMin) > 1e-6f)
                    continue;
                var d = Local(pj) - li;
                if (MathF.Abs(d.Z) > Tolerance)
                    continue;
                var gx = MathF.Round(d.X / size.X);
                var gy = MathF.Round(d.Y / size.Y);
                if (MathF.Abs(d.X - gx * size.X) > Tolerance || MathF.Abs(d.Y - gy * size.Y) > Tolerance || MathF.Abs(gx) > 100 || MathF.Abs(gy) > 100)
                    continue;
                classed[j] = true;
                members[n] = (byte)j;
                cx[n] = (short)gx;
                cy[n] = (short)gy;
                n++;
            }
            if (n < rules.MinSize * rules.MinSize)
                continue;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (var k = 0; k < n; k++)
            {
                minX = Math.Min(minX, cx[k]);
                maxX = Math.Max(maxX, cx[k]);
                minY = Math.Min(minY, cy[k]);
                maxY = Math.Max(maxY, cy[k]);
            }
            var w = maxX - minX + 1;
            var h = maxY - minY + 1;
            var useGrid = w * h <= grid.Length;
            if (useGrid)
            {
                grid[..(w * h)].Clear();
                for (var k = 0; k < n; k++)
                    grid[(cy[k] - minY) * w + cx[k] - minX] = (byte)(k + 1);
            }
            used[..n].Clear();
            // The member at a cell (its slot in the class, or -1).
            int At(Span<byte> grid, Span<short> cx, Span<short> cy, int x, int y)
            {
                if (x < minX || x > maxX || y < minY || y > maxY)
                    return -1;
                if (useGrid)
                    return grid[(y - minY) * w + x - minX] - 1;
                for (var k = 0; k < n; k++)
                    if (cx[k] == x && cy[k] == y)
                        return k;
                return -1;
            }
            var top = Math.Min(rules.MaxSize, (int)MathF.Sqrt(n) + 1);
            while (top * top > n)
                top--;
            // Largest square first; each size scanned in rows (the part frame's up, then its right), so every copy finds the same squares.
            for (var size2 = top; size2 >= rules.MinSize && count < groups.Length; size2--)
                for (var y = minY; y + size2 - 1 <= maxY && count < groups.Length; y++)
                for (var x = minX; x + size2 - 1 <= maxX && count < groups.Length; x++)
                {
                    var whole = true;
                    for (var yy = 0; yy < size2 && whole; yy++)
                    for (var xx = 0; xx < size2 && whole; xx++)
                    {
                        var k = At(grid, cx, cy, x + xx, y + yy);
                        whole = k >= 0 && !used[k];
                    }
                    if (!whole)
                        continue;
                    var lead = 255;
                    for (var yy = 0; yy < size2; yy++)
                    for (var xx = 0; xx < size2; xx++)
                    {
                        var k = At(grid, cx, cy, x + xx, y + yy);
                        used[k] = true;
                        mergeOf[members[k]] = (byte)(count + 1);
                        lead = Math.Min(lead, members[k]);
                    }
                    // The merged receiver's origin: its sides flush with the square's, its back with the members' backs.
                    var bmin = di.BoundsMin;
                    var bmax = di.BoundsMax;
                    var o = new Vector3(
                        li.X + x * size.X + bmin.X - size2 * bmin.X,
                        li.Y + y * size.Y + bmin.Y - size2 * bmin.Y,
                        li.Z + bmax.Z - size2 * bmax.Z);
                    groups[count++] = new CMerge { Lead = (byte)lead, N = (byte)size2, Rot = pi.Rot, Seg = pi.Seg, Origin = Rot24.Apply(pi.Rot, o) };
                }
        }
        return count;
    }

    /// <summary>Finds the merges and the gun parts they scale (the start of <see cref="Construct.Rebuild"/>).</summary>
    public static void Refresh(Construct c)
    {
        c.MergeCount = Detect(c, -1, c.Merges, c.MergeOf);
        DeriveScales(c);
    }

    /// <summary>Every gun part on a merged receiver's centre sockets, and every gun part bolted on through them, is N times bigger.</summary>
    public static void DeriveScales(Construct c)
    {
        c.PartScale.AsSpan().Clear();
        if (c.MergeCount == 0)
            return;
        Span<byte> queue = stackalloc byte[Construct.MaxParts];
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var link = ref c.Links[l];
            int lead, other, socket;
            if (link.A < c.PartCount && c.MergeLead(link.A) && link.SA >= c.DefOf(link.A).Sockets.Length)
                (lead, other, socket) = (link.A, link.B, link.SA);
            else if (link.B < c.PartCount && c.MergeLead(link.B) && link.SB >= c.DefOf(link.B).Sockets.Length)
                (lead, other, socket) = (link.B, link.A, link.SB);
            else
                continue;
            _ = socket;
            if (other >= c.PartCount || c.Parts[other].D.Role != PartRole.GunPart || c.PartScale[other] > 1)
                continue;
            var n = c.Merges[c.MergeOf[lead] - 1].N;
            int head = 0, tail = 0;
            c.PartScale[other] = n;
            queue[tail++] = (byte)other;
            while (head < tail)
            {
                int at = queue[head++];
                for (var k = 0; k < c.LinkCount; k++)
                {
                    ref var lk = ref c.Links[k];
                    var o = lk.A == at ? lk.B : lk.B == at ? lk.A : -1;
                    if (o < 0 || o >= c.PartCount || c.PartScale[o] > 1 || c.Parts[o].D.Role != PartRole.GunPart)
                        continue;
                    c.PartScale[o] = n;
                    queue[tail++] = (byte)o;
                }
            }
        }
    }

    /// <summary>Centre socket <paramref name="s"/> of merge <paramref name="m"/> (rest frame): the receiver's socket, N times out from the merged origin.</summary>
    public static void CentreSocket(Construct c, in CMerge m, int s, out Vector3 at, out byte dir)
    {
        var sock = c.DefOf(m.Lead).Sockets[s];
        at = m.Origin + Rot24.Apply(m.Rot, sock.At * m.N);
        dir = Rot24.ApplyDir(m.Rot, sock.Dir);
    }

    /// <summary>Merge <paramref name="m"/>'s drawn solid box (rest frame): the receiver's solid bounds N times bigger at its origin.</summary>
    public static void MergedBounds(Construct c, in CMerge m, out Vector3 centre, out Vector3 half)
    {
        var d = c.DefOf(m.Lead);
        centre = m.Origin + Rot24.Apply(m.Rot, (d.BoundsMin + d.BoundsMax) * 0.5f * m.N);
        half = Rot24.ApplyHalf(m.Rot, (d.BoundsMax - d.BoundsMin) * 0.5f * m.N);
    }

    /// <summary>Does a box (rest frame, on segment <paramref name="seg"/>) overlap the front of a merged receiver beyond its members (a centimetre of slack)?</summary>
    public static bool InMerged(Construct c, Vector3 centre, Vector3 half, int seg)
    {
        for (var g = 0; g < c.MergeCount; g++)
        {
            ref var m = ref c.Merges[g];
            if (m.Seg != seg)
                continue;
            MergedBounds(c, m, out var mc, out var mh);
            var h = half + mh - new Vector3(0.01f);
            if (MathF.Abs(centre.X - mc.X) < h.X && MathF.Abs(centre.Y - mc.Y) < h.Y && MathF.Abs(centre.Z - mc.Z) < h.Z)
                return true;
        }
        return false;
    }

    /// <summary>The scale a part <paramref name="def"/> fitted to socket <paramref name="socket"/> of part <paramref name="part"/> gets (a gun part on a centre socket or on a scaled gun part: N).</summary>
    public static byte ScaleFor(Construct c, int part, int socket, ConstructPartDef def)
    {
        if (def.Role != PartRole.GunPart)
            return 1;
        if (c.MergeLead(part) && socket >= c.DefOf(part).Sockets.Length)
            return c.Merges[c.MergeOf[part] - 1].N;
        return c.PartScale[part] > 1 ? c.PartScale[part] : (byte)1;
    }

    /// <summary>The merged receiver's combined health and most health (a member: its merge's; else the part's own).</summary>
    public static void Health(Construct c, int part, out float health, out float max)
    {
        health = c.Parts[part].Health;
        max = c.DefOf(part).Health;
        if (c.MergeOf[part] == 0)
            return;
        health = max = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.MergeOf[i] == c.MergeOf[part])
            {
                health += c.Parts[i].Health;
                max += c.DefOf(i).Health;
            }
    }

    // ---- The gun's numbers ------------------------------------------------------------------------------------------

    /// <summary>An NxN merged receiver's gun: its numbers from the single receiver's by the rules (N squared receivers' worth, not strictly better).</summary>
    public static void ScaleGun(ref CGun g, WeaponDef w, int n)
    {
        var r = ReceiverMergeRules.Current;
        w.Damage = Math.Max(1, (int)MathF.Round(w.Damage * ReceiverMergeRules.Times(n, r.Damage)));
        w.Rpm = Math.Max(1, (int)MathF.Round(w.Rpm * ReceiverMergeRules.Times(n, r.FireRate)));
        w.Mag = Math.Max(1, (int)MathF.Round(w.Mag * ReceiverMergeRules.Times(n, r.Magazine)));
        w.RangeM *= ReceiverMergeRules.Times(n, r.Range);
        w.HipSpreadDeg *= ReceiverMergeRules.Times(n, r.Spread);
        g.Spread *= ReceiverMergeRules.Times(n, r.Spread);
        g.Kick *= ReceiverMergeRules.Times(n, r.Recoil);
        g.PerShot = (ushort)Math.Clamp((int)MathF.Round(ReceiverMergeRules.Times(n, r.AmmoPerShot)), 1, 1000);
        g.MergeN = (byte)n;
    }

    // ---- Settling an edit (server) ----------------------------------------------------------------------------------

    /// <summary>
    /// After a receiver goes on (or before one comes off: <paramref name="exclude"/>), the merges change: gun parts on the
    /// members' own sockets pop off, and each old merge's centre parts move onto the new merge that holds most of its
    /// members (scaled to its size), or pop off when there is none or something is in the way. Popped parts go to
    /// <see cref="ConstructWorld.MergePlacer"/> (the edit's player) or drop as loose parts. Returns where part
    /// <paramref name="keep"/> is now (the parts are renumbered when some pop off).
    /// </summary>
    public static int Settle(ConstructWorld world, Construct c, int exclude, int keep)
    {
        Span<CMerge> oldGroups = stackalloc CMerge[Construct.MaxMerges];
        Span<byte> oldOf = stackalloc byte[Construct.MaxParts];
        var oldCount = c.MergeCount;
        c.Merges.AsSpan(0, oldCount).CopyTo(oldGroups);
        c.MergeOf.AsSpan().CopyTo(oldOf);
        Span<CMerge> groups = stackalloc CMerge[Construct.MaxMerges];
        Span<byte> mergeOf = stackalloc byte[Construct.MaxParts];
        var count = Detect(c, exclude, groups, mergeOf);
        if (oldCount == 0 && count == 0)
            return keep;
        Span<bool> pop = stackalloc bool[Construct.MaxParts];
        Span<bool> tree = stackalloc bool[Construct.MaxParts];
        // Gun parts on the members' own sockets: off.
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var link = ref c.Links[l];
            for (var side = 0; side < 2; side++)
            {
                int member = side == 0 ? link.A : link.B, other = side == 0 ? link.B : link.A, socket = side == 0 ? link.SA : link.SB;
                if (member >= c.PartCount || other >= c.PartCount || mergeOf[member] == 0 || socket >= c.DefOf(member).Sockets.Length)
                    continue;
                if (c.Parts[other].D.Role != PartRole.GunPart || pop[other] || other == exclude)
                    continue;
                // (a gun part on an old merge's centre socket that is also bolted to a member: it moves with its merge below)
                if (OnCentre(c, oldGroups, oldCount, oldOf, other))
                    continue;
                tree.Clear();
                Tree(c, other, tree, pop, exclude);
                Hangers(c, tree, exclude);
                for (var k = 0; k < c.PartCount; k++)
                    if (tree[k])
                        pop[k] = true;
            }
        }
        // Each old merge's centre parts: onto the new merge that holds most of its members, else off.
        Span<bool> claimed = stackalloc bool[Construct.MaxMerges];
        for (var og = 0; og < oldCount; og++)
        {
            ref var old = ref oldGroups[og];
            if (old.Lead >= c.PartCount)
                continue;
            var target = -1;
            var best = 0;
            for (var ng = 0; ng < count; ng++)
            {
                var overlap = 0;
                for (var i = 0; i < c.PartCount; i++)
                    if (oldOf[i] == og + 1 && mergeOf[i] == ng + 1)
                        overlap++;
                if (overlap > best || overlap == best && overlap > 0 && groups[ng].N > groups[target].N)
                {
                    best = overlap;
                    target = ng;
                }
            }
            if (target >= 0 && claimed[target])
                target = -1;
            if (target >= 0)
                claimed[target] = true;
            var len = c.DefOf(old.Lead).Sockets.Length;
            for (var l = 0; l < c.LinkCount; l++)
            {
                ref var link = ref c.Links[l];
                int other, socket;
                bool aSide;
                if (link.A == old.Lead && link.SA >= len)
                    (other, socket, aSide) = (link.B, link.SA - len, true);
                else if (link.B == old.Lead && link.SB >= len)
                    (other, socket, aSide) = (link.A, link.SB - len, false);
                else
                    continue;
                if (other >= c.PartCount || pop[other] || c.Parts[other].D.Role != PartRole.GunPart)
                    continue;
                tree.Clear();
                Tree(c, other, tree, pop, exclude);
                var same = target >= 0 && groups[target].Lead == old.Lead && groups[target].N == old.N && Vector3.DistanceSquared(groups[target].Origin, old.Origin) < 1e-6f;
                if (same)
                    continue; // (the merge didn't change)
                if (target >= 0 && Reseat(c, old, groups[target], socket, tree, pop, exclude, mergeOf, groups, count))
                {
                    var lead = groups[target].Lead;
                    var at = (byte)(c.DefOf(lead).Sockets.Length + socket);
                    if (aSide)
                        (link.A, link.SA) = (lead, at);
                    else
                        (link.B, link.SB) = (lead, at);
                    c.Dirty = true;
                    world.StructureDirty.Add(c.Id);
                    continue;
                }
                Hangers(c, tree, exclude);
                for (var k = 0; k < c.PartCount; k++)
                    if (tree[k])
                        pop[k] = true;
            }
        }
        var popped = 0;
        for (var k = 0; k < c.PartCount; k++)
            if (pop[k])
                popped++;
        if (popped == 0)
            return keep;
        var shift = 0;
        for (var k = 0; k < keep && k < c.PartCount; k++)
            if (pop[k])
                shift++;
        world.PopParts(c, pop);
        return keep - shift;
    }

    /// <summary>Is gun part <paramref name="part"/> on (or bolted on through) an old merge's centre sockets?</summary>
    private static bool OnCentre(Construct c, Span<CMerge> groups, int count, Span<byte> mergeOf, int part)
    {
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var link = ref c.Links[l];
            if (link.A == part && link.B < c.PartCount && mergeOf[link.B] != 0 && groups[mergeOf[link.B] - 1].Lead == link.B && link.SB >= c.DefOf(link.B).Sockets.Length)
                return true;
            if (link.B == part && link.A < c.PartCount && mergeOf[link.A] != 0 && groups[mergeOf[link.A] - 1].Lead == link.A && link.SA >= c.DefOf(link.A).Sockets.Length)
                return true;
        }
        return c.PartScale[part] > 1; // (bolted on through one: scaled)
    }

    /// <summary>A gun part and every gun part bolted on through it (never through a receiver), into <paramref name="tree"/>.</summary>
    private static void Tree(Construct c, int start, Span<bool> tree, Span<bool> skip, int exclude)
    {
        Span<byte> queue = stackalloc byte[Construct.MaxParts];
        int head = 0, tail = 0;
        tree[start] = true;
        queue[tail++] = (byte)start;
        while (head < tail)
        {
            int at = queue[head++];
            for (var k = 0; k < c.LinkCount; k++)
            {
                ref var lk = ref c.Links[k];
                var o = lk.A == at ? lk.B : lk.B == at ? lk.A : -1;
                if (o < 0 || o >= c.PartCount || tree[o] || skip[o] || o == exclude || c.Parts[o].D.Role != PartRole.GunPart)
                    continue;
                tree[o] = true;
                queue[tail++] = (byte)o;
            }
        }
    }

    /// <summary>Moves a centre part's tree from an old merge's socket to the same socket of a new merge, scaled to its size; false when something would be in the way (nothing moves then).</summary>
    private static bool Reseat(Construct c, in CMerge from, in CMerge to, int socket, Span<bool> tree, Span<bool> pop, int exclude, Span<byte> mergeOf, Span<CMerge> groups, int count)
    {
        if (to.Rot != from.Rot || to.Seg != from.Seg)
            return false;
        CentreSocket(c, from, socket, out var oldAt, out _);
        CentreSocket(c, to, socket, out var newAt, out _);
        var ratio = (float)to.N / from.N;
        Span<bool> hang = stackalloc bool[Construct.MaxParts];
        for (var b = 0; b < c.PartCount; b++)
            hang[b] = !tree[b] && b != exclude && HangsOn(c, b, tree, exclude, out _, out _);
        // Something in the way: any other part (not popping, not the one coming off), or the front of another merge.
        for (var i = 0; i < c.PartCount; i++)
        {
            if (!tree[i])
                continue;
            var d = c.DefOf(i);
            var pos = ConstructWorld.Quantise(newAt + (c.Parts[i].Pos - oldAt) * ratio);
            var s = to.N * (c.ScaleOf(i) / from.N);
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                var bc = pos + Rot24.Apply(c.Parts[i].Rot, d.Boxes[b].Center * s);
                var bh = Rot24.ApplyHalf(c.Parts[i].Rot, d.Boxes[b].Half * s) - new Vector3(0.01f);
                for (var j = 0; j < c.PartCount; j++)
                {
                    if (tree[j] || pop[j] || hang[j] || j == exclude || c.Parts[j].Seg != to.Seg)
                        continue;
                    var od = c.DefOf(j);
                    for (var k = 0; k < od.SolidBoxes; k++)
                    {
                        c.BoxOf(j, k, out var oc, out var oh);
                        if (MathF.Abs(bc.X - oc.X) < bh.X + oh.X && MathF.Abs(bc.Y - oc.Y) < bh.Y + oh.Y && MathF.Abs(bc.Z - oc.Z) < bh.Z + oh.Z)
                            return false;
                    }
                }
                for (var g = 0; g < count; g++)
                {
                    if (groups[g].Lead == to.Lead || groups[g].Seg != to.Seg)
                        continue;
                    MergedBounds(c, groups[g], out var mc, out var mh);
                    if (MathF.Abs(bc.X - mc.X) < bh.X + mh.X && MathF.Abs(bc.Y - mc.Y) < bh.Y + mh.Y && MathF.Abs(bc.Z - mc.Z) < bh.Z + mh.Z)
                        return false;
                }
            }
        }
        _ = mergeOf;
        // Ammo boxes hanging only on the tree keep their size and move with the socket they are on.
        for (var b = 0; b < c.PartCount; b++)
        {
            if (!hang[b] || !HangsOn(c, b, tree, exclude, out var t, out var ts))
                continue;
            c.SocketAt(t, ts, out var was, out _);
            c.Parts[b].Pos = ConstructWorld.Quantise(c.Parts[b].Pos + newAt + (was - oldAt) * ratio - was);
        }
        for (var i = 0; i < c.PartCount; i++)
            if (tree[i])
                c.Parts[i].Pos = ConstructWorld.Quantise(newAt + (c.Parts[i].Pos - oldAt) * ratio);
        return true;
    }

    /// <summary>Is part <paramref name="b"/> an ammo box bolted only to parts of <paramref name="tree"/> (the first: <paramref name="on"/>, its socket <paramref name="socket"/>)?</summary>
    private static bool HangsOn(Construct c, int b, Span<bool> tree, int exclude, out int on, out int socket)
    {
        on = socket = -1;
        if (c.Parts[b].D.Role != PartRole.AmmoBox)
            return false;
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var lk = ref c.Links[l];
            int o, os;
            if (lk.A == b)
                (o, os) = (lk.B, lk.SB);
            else if (lk.B == b)
                (o, os) = (lk.A, lk.SA);
            else
                continue;
            if (o == exclude)
                continue;
            if (o >= c.PartCount || !tree[o] || c.Parts[o].D.Role != PartRole.GunPart)
                return false;
            if (on < 0)
                (on, socket) = (o, os);
        }
        return on >= 0;
    }

    /// <summary>Adds to <paramref name="tree"/> the ammo boxes bolted only to it (they pop off with it).</summary>
    private static void Hangers(Construct c, Span<bool> tree, int exclude)
    {
        for (var b = 0; b < c.PartCount; b++)
            if (!tree[b] && b != exclude && HangsOn(c, b, tree, exclude, out _, out _))
                tree[b] = true; // (a box on such a box stays, and falls away as a piece with it)
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>The player whose edit is being done (ConstructEdits sets it): parts a merge pops off go to them; 255: they drop where they were.</summary>
    public byte MergePlacer = 255;

    /// <summary>Parts a merge popped off, waiting for <see cref="MergePlacer"/> (ConstructEdits hands them over after the edit).</summary>
    public readonly List<(ushort Def, Vector3 At, byte Owner)> MergePops = new(16);

    /// <summary>Parts that popped off merged receivers in all (the self-test).</summary>
    public int MergePopCount;

    /// <summary>Takes the parts marked in <paramref name="gone"/> off <paramref name="c"/> (a merge popped them), splitting off what no longer reaches the root as wrecks, as <see cref="RemovePart"/> does.</summary>
    public void PopParts(Construct c, Span<bool> gone)
    {
        for (var i = 0; i < c.PartCount; i++)
        {
            if (!gone[i])
                continue;
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            c.PartBounds(i, out var mn, out var mx);
            MergePops.Add((c.Parts[i].Def, t + Vector3.Transform((mn + mx) * 0.5f, q), c.Owner));
            MergePopCount++;
            Events.Add($"construct {c.Id}: {c.DefOf(i).Name} popped off a merged receiver");
        }
        Span<int> comp = stackalloc int[Construct.MaxParts];
        for (var i = 0; i < c.PartCount; i++)
            comp[i] = gone[i] ? -2 : -1;
        var root = c.Root < c.PartCount && !gone[c.Root] ? c.Root : -1;
        for (var i = 0; i < c.PartCount && root < 0; i++)
            if (!gone[i] && c.Parts[i].D.Role is PartRole.Seat or PartRole.Foundation)
                root = i;
        for (var i = 0; i < c.PartCount && root < 0; i++)
            if (!gone[i])
                root = i;
        var comps = 0;
        if (root >= 0)
            Flood(c, root, comps++, comp);
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == -1)
                Flood(c, i, comps++, comp);
        for (var k = 1; k < comps; k++)
            SplitOff(c, comp, k);
        Compact(c, comp, 0);
        c.Root = c.PickRoot();
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        if (MergePlacer == 255)
            DropMergePops();
    }

    /// <summary>Every part a merge popped off that nobody took drops as a loose part where it was.</summary>
    public void DropMergePops()
    {
        foreach (var (def, at, owner) in MergePops)
            if (ConstructParts.Get(def) is { } d && ConstructBuild.PlaceNew(this, d, at with { Y = at.Y - 0.3f }, 0) is { } dropped)
                dropped.Owner = owner;
        MergePops.Clear();
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>After an edit: the parts a merge popped off go into the player's hands, else their backpack, else drop where they were as loose parts (the shared give rule).</summary>
    private void GiveMergePops(ConstructWorld world, byte player)
    {
        if (world.MergePops.Count == 0)
            return;
        for (var k = 0; k < world.MergePops.Count; k++)
        {
            var (def, at, _) = world.MergePops[k];
            if (ConstructParts.Get(def) is not { } d)
                continue;
            var where = Give(world, player, d, at); // (the one rule: ConstructGive.cs)
            Done.Add($"player {player}: {d.Name} popped off a merged receiver {where switch { GiveResult.Hands => "into the hands", GiveResult.Backpack => "into the backpack", GiveResult.Dropped => "and dropped", GiveResult.Inventory => "into the inventory", _ => "with nowhere to go" }}");
        }
        world.MergePops.Clear();
    }
}
