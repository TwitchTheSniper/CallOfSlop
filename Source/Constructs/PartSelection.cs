using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The selector's selection on this PC (docs/sandbox/CONSTRUCTS.md, "Build stations"): parts of one construct, each stamped
/// with what it was when selected (its type, place and turn). Checked against the construct every frame before it is used
/// (<see cref="Check"/>): a part another peer's edit renumbered is found again by its stamp, one that went or changed drops
/// out, so nothing ever acts on a neighbour. Allocates only when the construct changed under it.
/// </summary>
public sealed class PartSelection
{
    private ushort[] _items = new ushort[256];
    private ushort[] _defs = new ushort[256];
    private byte[] _rots = new byte[256];
    private Vector3[] _pos = new Vector3[256];
    private bool[] _on = new bool[256];

    public Construct? Body { get; private set; }
    public int Count { get; private set; }

    /// <summary>A box started on open ground takes in many constructs: the others' parts, one selection each (<see cref="Body"/> is the first).</summary>
    public readonly List<PartSelection> Extra = new();

    /// <summary>Parts selected, on every construct.</summary>
    public int Total
    {
        get
        {
            var n = Count;
            foreach (var e in Extra)
                n += e.Count;
            return n;
        }
    }

    /// <summary>The selection holding <paramref name="c"/>'s parts, made if it's asked for and there is none.</summary>
    public PartSelection? For(Construct c, bool make)
    {
        if (Body == null && make || ReferenceEquals(Body, c))
            return this;
        foreach (var e in Extra)
            if (ReferenceEquals(e.Body, c))
                return e;
        if (!make)
            return null;
        var added = new PartSelection();
        Extra.Add(added);
        return added;
    }

    public ReadOnlySpan<ushort> Items => _items.AsSpan(0, Count);

    public bool Contains(int part) => part >= 0 && part < _on.Length && _on[part];

    public void Clear()
    {
        for (var k = 0; k < Count; k++)
            _on[_items[k]] = false;
        Count = 0;
        Body = null;
        Extra.Clear();
    }

    private void Fit(int count, int parts)
    {
        if (_items.Length < count)
        {
            var n = Math.Min(Math.Max(count, _items.Length * 2), Construct.MaxParts);
            Array.Resize(ref _items, n);
            Array.Resize(ref _defs, n);
            Array.Resize(ref _rots, n);
            Array.Resize(ref _pos, n);
        }
        if (_on.Length < parts)
            Array.Resize(ref _on, Math.Max(parts, _on.Length * 2));
    }

    /// <summary>Selects a part. A part of another construct replaces the selection: one construct at a time.</summary>
    public bool Add(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount)
            return false;
        if (!ReferenceEquals(Body, c))
        {
            var keep = Count == 0 ? new List<PartSelection>(Extra) : null; // (empty, it takes on a construct; a part of another one replaces the lot)
            Clear();
            if (keep != null)
                Extra.AddRange(keep);
            Body = c;
        }
        Fit(Count + 1, c.PartCount);
        if (_on[part])
            return false;
        _on[part] = true;
        Stamp(Count++, c, part);
        return true;
    }

    private void Stamp(int k, Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        _items[k] = (ushort)part;
        _defs[k] = p.Def;
        _rots[k] = p.Rot;
        _pos[k] = p.Pos;
    }

    public bool Remove(int part)
    {
        if (!Contains(part))
            return false;
        _on[part] = false;
        for (var k = 0; k < Count; k++)
            if (_items[k] == part)
            {
                Count--;
                _items[k] = _items[Count];
                _defs[k] = _defs[Count];
                _rots[k] = _rots[Count];
                _pos[k] = _pos[Count];
                break;
            }
        if (Count == 0)
            Body = null;
        return true;
    }

    /// <summary>Checks the selection against the construct as it is now. Returns how many parts dropped out.</summary>
    public int Check(ConstructWorld world)
    {
        var gone = 0;
        for (var k = Extra.Count - 1; k >= 0; k--)
        {
            gone += Extra[k].Check(world);
            if (Extra[k].Count == 0)
                Extra.RemoveAt(k);
        }
        gone += CheckThis(world);
        if (Count == 0 && Extra.Count > 0)
        {
            // The first construct's parts all went: the next one's stand in.
            var next = Extra[0];
            Extra.RemoveAt(0);
            foreach (var i in next.Items)
                Add(next.Body!, i);
        }
        return gone;
    }

    private int CheckThis(ConstructWorld world)
    {
        if (Body is not { } c)
            return 0;
        if (Count == 0 || !ReferenceEquals(world.Get(c.Id), c))
        {
            var had = Count;
            for (var k = 0; k < Count; k++)
                _on[_items[k]] = false;
            Count = 0;
            Body = null;
            return had;
        }
        var same = true;
        for (var k = 0; k < Count && same; k++)
        {
            int i = _items[k];
            same = i < c.PartCount && c.Parts[i].Def == _defs[k] && c.Parts[i].Rot == _rots[k] && c.Parts[i].Pos == _pos[k];
        }
        if (same)
            return 0;
        // The construct changed: parts are found again by their stamps; what isn't there any more drops out.
        var at = new Dictionary<(Vector3, ushort, byte), int>(c.PartCount);
        for (var i = 0; i < c.PartCount; i++)
            at.TryAdd((c.Parts[i].Pos, c.Parts[i].Def, c.Parts[i].Rot), i);
        for (var k = 0; k < Count; k++)
            _on[_items[k]] = false;
        Fit(Count, c.PartCount);
        var kept = 0;
        for (var k = 0; k < Count; k++)
            if (at.TryGetValue((_pos[k], _defs[k], _rots[k]), out var i) && !_on[i])
            {
                _on[i] = true;
                _items[kept] = (ushort)i;
                _defs[kept] = _defs[k];
                _rots[kept] = _rots[k];
                _pos[kept++] = _pos[k];
            }
        var gone = Count - kept;
        Count = kept;
        if (kept == 0)
            Body = null;
        return gone;
    }

    /// <summary>The whole connected section a part is in: every part reached over its links without crossing a joint.</summary>
    public int AddSection(Construct c, int part, bool remove)
    {
        var seen = new bool[c.PartCount];
        var queue = new int[c.PartCount];
        int head = 0, tail = 0;
        seen[part] = true;
        queue[tail++] = part;
        var n = 0;
        while (head < tail)
        {
            var at = queue[head++];
            n += (remove ? Remove(at) : Add(c, at)) ? 1 : 0;
            for (var e = c.FirstLink(at); e >= 0; e = c.NextLink(e))
            {
                var o = c.OtherEnd(e);
                if (seen[o] || c.Parts[o].Seg != c.Parts[at].Seg)
                    continue;
                seen[o] = true;
                queue[tail++] = o;
            }
        }
        return n;
    }

    /// <summary>
    /// Every part a box in the construct's frame touches (a drag across a flat face takes that face's parts). Adds them (replacing the selection unless <paramref name="add"/>),
    /// or with <paramref name="remove"/> takes them out. Returns how many changed.
    /// </summary>
    public int AddBox(Construct c, Vector3 min, Vector3 max, bool add, bool remove)
    {
        if (!add && !remove)
            Clear();
        var hits = Construct.PartScratch;
        var near = c.PartsInBox(min, max, hits);
        var n = 0;
        for (var h = 0; h < near; h++)
        {
            var i = hits[h];
            c.PartBounds(i, out var a, out var b);
            if (a.X > max.X || a.Y > max.Y || a.Z > max.Z || b.X < min.X || b.Y < min.Y || b.Z < min.Z)
                continue;
            n += (remove ? Remove(i) : Add(c, i)) ? 1 : 0;
        }
        return n;
    }

    /// <summary>
    /// A box on the world (a drag started on open ground; the ground itself is never selected): in a frame turned <paramref name="turn"/>,
    /// from <paramref name="min"/> to <paramref name="max"/>. Over every construct in reach that <paramref name="may"/> takes: one whose middle is in the
    /// box comes whole; of one that only reaches in, the parts whose middles are. Adds them (replacing the selection unless <paramref name="add"/>). How many constructs.
    /// </summary>
    public int AddWorldBox(ConstructWorld world, Quaternion turn, Vector3 min, Vector3 max, bool add, Func<Construct, bool> may, int most)
    {
        if (!add)
            Clear();
        var inv = Quaternion.Conjugate(turn);
        bool In(Vector3 w)
        {
            var p = Vector3.Transform(w, inv);
            return p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z;
        }
        var bodies = 0;
        foreach (var c in world.Bodies)
        {
            if (c.PartCount == 0 || !may(c) || bodies >= most)
                continue;
            var whole = In(c.WorldCom);
            PartSelection? into = null;
            for (var i = 0; i < c.PartCount; i++)
            {
                c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
                c.PartBounds(i, out var a, out var b);
                if (!whole && !In(t + Vector3.Transform((a + b) * 0.5f, q)))
                    continue;
                into ??= For(c, true);
                into!.Add(c, i);
            }
            bodies += into != null ? 1 : 0;
        }
        return bodies;
    }

    /// <summary>Every part of a construct whose middle lands on the screen inside a rectangle (<see cref="AddBox"/>'s other way in).</summary>
    public int AddScreen(Construct c, in Matrix4x4 viewProj, Vector2 screen, Vector2 a, Vector2 b, bool add, bool remove, bool countOnly = false)
    {
        if (!add && !remove && !countOnly)
            Clear();
        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            c.PartBounds(i, out var pa, out var pb);
            var clip = Vector4.Transform(new Vector4(t + Vector3.Transform((pa + pb) * 0.5f, q), 1), viewProj);
            if (clip.W <= 0.01f)
                continue;
            var at = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * screen.X, (0.5f - clip.Y / clip.W * 0.5f) * screen.Y);
            if (at.X < min.X || at.Y < min.Y || at.X > max.X || at.Y > max.Y)
                continue;
            n += countOnly ? 1 : (remove ? Remove(i) : Add(c, i)) ? 1 : 0;
        }
        return n;
    }
}
