using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sub-kits (Sean, 2026-11-28: bigger oven beds only for parts that can't be split; everything else is built as sub-assemblies on the
/// smallest oven that fits, so the work spreads over every oven size). A rule kit too big for the smallest oven is split along its boxes
/// (<see cref="Split"/>); each group of two or more pieces that fits the smallest oven is a <b>sub-kit</b>: built at a station as any kit
/// (its blueprint <c>Baked (#hash) kit</c>), baked on a small oven into one baked part, <c>Baked (#hash)</c>, whose design is the sub-kit's
/// pieces where they stand in the part (its canonical design, the same on every peer). The part's <b>split kit</b>
/// (<c>{part} split kit</c>) is those baked sub-assemblies and the pieces left over, each at its place, baked on the oven its whole
/// footprint needs: the bake reads a baked subpart as its own subparts (<see cref="BakedParts.Bake"/>), so the split kit's design is the
/// whole kit's and comes out as the part. Keys, never run-time ids: a sub-kit is named by its design's hash. Worked out once a process for
/// every rule kit (<see cref="WarmSubs"/>, with <see cref="Warm"/>). Docs: production.md "Sub-kits".
/// </summary>
public static partial class BakeKits
{
    public const string SplitSuffix = " split kit";

    /// <summary>A sub-kit: its baked part's key and design, its pieces in the part's frame, its design's origin there, its sides and oven.</summary>
    public sealed class SubKit
    {
        public string Key = "", Hash = "", Text = "";
        public Piece[] Pieces = [];
        public Vector3 Origin, Size;
        public int Oven;
        public ConstructPartDef? Of; // (the first part whose split holds it)
        internal ConstructBuild.Blueprint? Print;
        internal bool Printed;
        internal Dictionary<string, Hit>? Turns; // (its design at the 24 turns, by hash: made at its first bake)
    }

    /// <summary>A part's split kit: its items (baked sub-assemblies and leftover pieces, each in the part's frame) and its sub-kits.</summary>
    public sealed class SplitKit
    {
        public Piece[] Items = [];
        public SubKit[] Subs = [];
        public int Oven; // (the whole footprint's: the final bake's oven)
        internal ConstructBuild.Blueprint? Print;
        internal bool Printed;
    }

    private static readonly Dictionary<string, SplitKit?> Splits = new(StringComparer.OrdinalIgnoreCase); // by part name
    private static readonly Dictionary<string, SubKit> SubsByKey = new(StringComparer.Ordinal);
    private static AssemblingTable? _subsTable;
    private static readonly HashSet<string> GoneSplits = new(StringComparer.OrdinalIgnoreCase);
    private static int _splitCount, _noHelp, _tooBig;

    /// <summary>
    /// A rule kit's split (null: it fits the smallest oven whole, isn't a rule kit, or splitting frees nothing for a smaller oven). Every
    /// group of two or more pieces the split leaves on the smallest oven is a sub-kit; the rest are its items as they are. Under no lock.
    /// </summary>
    public static SplitKit? SplitOf(ConstructPartDef d)
    {
        if (d.IsNamedRuntime || !Has(d) || IsNamed(d.Name))
            return null; // (listed kits, the decks and the keel, bake as they are listed)
        WarmSubs();
        lock (Gate)
            return Splits.GetValueOrDefault(d.Name);
    }

    /// <summary>The sub-kit a baked key names (null: none this build works out).</summary>
    public static SubKit? SubOf(string? key)
    {
        if (key == null || !key.StartsWith(BakedParts.Prefix, StringComparison.Ordinal))
            return null;
        WarmSubs();
        lock (Gate)
            return SubsByKey.GetValueOrDefault(key);
    }

    /// <summary>The sub-kit a part is (a baked part whose key is one), or null. Allocates nothing.</summary>
    public static SubKit? SubOf(ConstructPartDef d) => d.IsNamedRuntime && BakedParts.IsBaked(d) ? SubOf(d.Name) : null;

    /// <summary>Is this a sub-kit's baked part (an oven takes an order for it, a station builds its kit)?</summary>
    public static bool IsSub(ConstructPartDef d) => SubOf(d) != null;

    /// <summary>A sub-assembly's order by its key, its id this process's own (null: no such sub-kit, or its part can't be made).</summary>
    public static WorkOrder? SubOrder(string key, int count) => SubOf(key) != null && BakedParts.Parse(key) is { } d
        ? new WorkOrder { Name = key, Part = (ushort)d.Id, Count = (ushort)Math.Clamp(count, 1, WorkOrders.MaxCount) }
        : null;

    /// <summary>What an oven's order bakes: a kit's catalogue part, or a sub-kit's baked part (null: neither).</summary>
    public static ConstructPartDef? OvenDef(in WorkOrder o)
    {
        if (o.IsPiece)
            return null;
        if (o.Name == null)
            return ConstructParts.Get(o.Part);
        if (o.IsMade) // (an Ord Rack or ordnance design by its key, phase 5: its kit's, when it has one)
            return (ConstructParts.Get(o.Part) is { } got && got.Name == o.Name ? got : AssembledParts.Def(o.Name)) is { } md && Has(md) ? md : null;
        return o.IsSub && SubOf(o.Name) != null ? ConstructParts.Get(o.Part) ?? BakedParts.Parse(o.Name) : null;
    }

    /// <summary>Is this part baked from a kit an oven takes an order for: a kit's part, or a sub-kit's baked part?</summary>
    public static bool Bakes(ConstructPartDef d) => Has(d) || IsSub(d);

    /// <summary>The blueprint an oven's order is built from: a sub-kit's, or the part's whole kit (<see cref="BlueprintOf"/>).</summary>
    public static ConstructBuild.Blueprint? PrintOfOrder(ConstructPartDef d) => SubOf(d) is { } s ? SubPrint(s) : BlueprintOf(d);

    /// <summary>The sides (m) of what an order bakes: a sub-kit's, else the kit's.</summary>
    public static Vector3 BakeSize(ConstructPartDef d) => SubOf(d) is { } s ? s.Size : KitSize(d);

    /// <summary>A sub-kit's blueprint, <c>Baked (#hash) kit</c>: its pieces, the biggest the root. Made once.</summary>
    public static ConstructBuild.Blueprint? SubPrint(SubKit s)
    {
        lock (Gate)
        {
            if (!s.Printed)
                (s.Print, s.Printed) = (PrintOf(s.Key + KitSuffix, s.Pieces), true);
            return s.Print;
        }
    }

    /// <summary>A part's split kit's blueprint, <c>{part} split kit</c>: its baked sub-assemblies and leftover pieces. Made once.</summary>
    public static ConstructBuild.Blueprint? SplitPrint(ConstructPartDef d)
    {
        if (SplitOf(d) is not { } sk)
            return null;
        lock (Gate)
        {
            if (!sk.Printed)
                (sk.Print, sk.Printed) = (PrintOf(d.Name + SplitSuffix, sk.Items), true);
            return sk.Print;
        }
    }

    /// <summary>A split kit's or a sub-kit's blueprint by its name (null: neither).</summary>
    private static ConstructBuild.Blueprint? FindSubPrint(string name)
    {
        if (name.EndsWith(SplitSuffix, StringComparison.OrdinalIgnoreCase))
        {
            if (ConstructParts.Find(name[..^SplitSuffix.Length]) is not { } d)
                return null;
            if (SplitPrint(d) is { } sp)
                return sp;
            // (a job saved on a split this build no longer makes, a mod's table or a changed generator: its whole kit, said once)
            lock (Gate)
                if (GoneSplits.Add(d.Name))
                    Console.WriteLine($"[bake kits] {name}: this build doesn't split the {d.Name}'s kit: its whole kit stands in");
            return BlueprintOf(d);
        }
        if (name.StartsWith(BakedParts.Prefix, StringComparison.Ordinal) && name.EndsWith(KitSuffix, StringComparison.Ordinal))
            return SubOf(name[..^KitSuffix.Length]) is { } s ? SubPrint(s) : null;
        return null;
    }

    /// <summary>Is this blueprint name one of a part's kits (whole or split)?</summary>
    public static bool IsKitOf(string name, ConstructPartDef d) =>
        name.Length == d.Name.Length + KitSuffix.Length && name.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase) && name.EndsWith(KitSuffix, StringComparison.OrdinalIgnoreCase)
        || name.Length == d.Name.Length + SplitSuffix.Length && name.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase) && name.EndsWith(SplitSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every rule kit too big for the smallest oven split, once a process (and again when the Assembler table changes): its sub-kits'
    /// designs registered (<see cref="BakedParts.Register"/>, so every peer makes their parts from their keys), their blueprints and parts
    /// made only when first asked for. Deterministic: the same kit, the same split and the same keys. Said in the log with its cost.
    /// </summary>
    public static void WarmSubs()
    {
        if (ReferenceEquals(_subsTable, AssemblingTable.Current))
            return;
        lock (Gate)
        {
            if (ReferenceEquals(_subsTable, AssemblingTable.Current))
                return;
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Splits.Clear();
            SubsByKey.Clear();
            (_splitCount, _noHelp, _tooBig) = (0, 0, 0);
            var tooBig = new System.Text.StringBuilder();
            var subs = 0;
            var all = ConstructParts.All;
            for (var k = 0; k < all.Count; k++)
            {
                if (all[k] is not { } d || d.IsNamedRuntime || IsNamed(d.Name) || !Has(d) || KitOf(d, true) is not { } kit || kit.Length < 3)
                    continue;
                var (lo, hi) = Bounds(kit, 0, kit.Length, null);
                var whole = MakerRoutes.HearthFor(hi - lo);
                if (whole == 0)
                    continue;
                if (whole >= MakerRoutes.Ovens.Length)
                {
                    // (the split kit's final bake holds the whole kit as the whole kit's does: splitting can't bring it onto any oven)
                    if (_tooBig++ < 6)
                        tooBig.Append(tooBig.Length > 0 ? ", " : "").Append(d.Name).Append(FormattableString.Invariant($" {MathF.Max(hi.X - lo.X, MathF.Max(hi.Y - lo.Y, hi.Z - lo.Z)):0.#} m"));
                    continue;
                }
                var sk = MakeSplit(d, kit, whole);
                Splits[d.Name] = sk;
                if (sk == null)
                {
                    _noHelp++;
                    continue;
                }
                _splitCount++;
                subs += sk.Subs.Length;
            }
            _subsTable = AssemblingTable.Current;
            Console.WriteLine($"[bake kits] split: {_splitCount} rule kits too big for the {MakerRoutes.OvenName(0)} into {subs} sub-kits ({SubsByKey.Count} designs), "
                + $"{_noHelp} don't split into anything smaller, in {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms");
            if (_tooBig > 0)
                Console.WriteLine($"[bake kits] {_tooBig} rule kits fit no oven whole ({tooBig}{(_tooBig > 6 ? ", ..." : "")}): not split, since the split kit's last bake "
                    + $"holds the whole kit as its own does; they stay their Assembler rows' (no oven's bed, the {MakerRoutes.Ovens[^1].Name} the biggest, holds them)");
        }
    }

    /// <summary>The split of one kit (null: it leaves no sub-kit of two or more pieces on an oven smaller than its whole one's). Under <see cref="Gate"/>.</summary>
    private static SplitKit? MakeSplit(ConstructPartDef d, Piece[] kit, int whole)
    {
        var all = new List<int>(kit.Length);
        for (var i = 0; i < kit.Length; i++)
            all.Add(i);
        var tree = SplitOf(kit, all, 0);
        // The groups: each node on the smallest oven (or a lone piece) whole; a node on a bigger oven that didn't split gives its pieces loose.
        var groups = new List<List<int>>();
        Gather(tree, groups);
        var items = new List<Piece>(groups.Count);
        var subs = new List<SubKit>();
        foreach (var g in groups)
        {
            var (glo, ghi) = Bounds(kit, 0, 0, g);
            if (g.Count < 2 || MakerRoutes.HearthFor(ghi - glo) >= whole)
            {
                foreach (var i in g)
                    items.Add(kit[i]);
                continue;
            }
            g.Sort(); // (the kit's own order: its biggest piece first, the sub-kit's root)
            var pieces = new Piece[g.Count];
            for (var i = 0; i < g.Count; i++)
                pieces[i] = kit[g[i]];
            if (SubFor(d, pieces, ghi - glo, out var at) is not { } sub)
                return null;
            subs.Add(sub);
            items.Add(new Piece(default, at, 0, 0, "", sub.Key)); // (at this part's own place: a sub-kit alike in two parts stands where each puts it)
        }
        if (subs.Count == 0 || items.Count < 2)
            return null;
        // (the biggest item first, the split kit's root: a sub-assembly by its sides, a piece by its own)
        var vol = new float[items.Count];
        for (var i = 0; i < items.Count; i++)
            vol[i] = items[i].Name != null && SubsByKey.TryGetValue(items[i].Name!, out var s) ? s.Size.X * s.Size.Y * s.Size.Z * 1e6f : Volume(items[i]);
        var order = new int[items.Count];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => vol[b] != vol[a] ? vol[b].CompareTo(vol[a]) : a.CompareTo(b));
        return new SplitKit { Items = [.. order.Select(i => items[i])], Subs = [.. subs], Oven = whole };
    }

    private static void Gather(SplitNode n, List<List<int>> into)
    {
        if (n.Kids.Count == 0)
            into.Add([.. n.Pieces]);
        else
            foreach (var k in n.Kids)
                Gather(k, into);
    }

    /// <summary>A sub-kit of these pieces: its canonical design (the pieces where they stand in the part) registered and kept by key. Under <see cref="Gate"/>.</summary>
    private static SubKit? SubFor(ConstructPartDef d, Piece[] pieces, Vector3 size, out Vector3 origin)
    {
        origin = default;
        var group = new (ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].Def is not { } pd)
                return null;
            group[i] = (pd, pieces[i].Pos, pieces[i].Rot, pieces[i].Paint, pieces[i].Finish);
        }
        var text = BakedParts.DesignText(group, out origin);
        if (BakedParts.Register(text) is not { } hash)
        {
            Console.WriteLine($"[bake kits] {d.Name}: a sub-kit's design can't be kept ({BakedParts.LastRefusal}): it bakes whole");
            return null;
        }
        var key = BakedParts.KeyOf(hash);
        if (SubsByKey.TryGetValue(key, out var have))
            return have; // (two parts alike in a corner: one sub-kit)
        var sub = new SubKit { Key = key, Hash = hash, Text = text, Pieces = pieces, Origin = origin, Size = size, Oven = MakerRoutes.HearthFor(size), Of = d };
        SubsByKey[key] = sub;
        return sub;
    }

    /// <summary>
    /// The sub-kit whose design at some turn hashes to <paramref name="hash"/>, among the oven's order's (<paramref name="want"/>) and the
    /// rest of its queue: its baked part at that turn and its origin from the design's origin (as <see cref="Target"/> gives a kit's part).
    /// A sub-kit's 24 turns are hashed at its first bake.
    /// </summary>
    public static Hit? SubTarget(string hash, ushort want, OrderList? queue)
    {
        if (ConstructParts.Get(want) is { } wd && SubOf(wd) is { } ws && SubHit(ws, hash) is { } hw)
            return hw;
        if (queue is { Bill: false })
            for (var k = 0; k < queue.Count; k++)
                if (queue.Items[k] is { IsSub: true } o && o.Part != want && SubOf(o.Name) is { } qs && SubHit(qs, hash) is { } hq)
                    return hq;
        return null;
    }

    private static Hit? SubHit(SubKit s, string hash)
    {
        if (BakedParts.Parse(s.Key) is not { } part)
            return null;
        lock (Gate)
        {
            if (s.Turns == null)
            {
                s.Turns = new Dictionary<string, Hit>(StringComparer.Ordinal);
                var subs = new (ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)[s.Pieces.Length];
                for (byte r = 0; r < Rot24.Count; r++)
                {
                    for (var i = 0; i < s.Pieces.Length; i++)
                        subs[i] = (s.Pieces[i].Def!, Rot24.Apply(r, s.Pieces[i].Pos), Rot24.Compose(r, s.Pieces[i].Rot), s.Pieces[i].Paint, s.Pieces[i].Finish);
                    var h = BakedParts.HashOf(BakedParts.DesignText(subs, out var origin));
                    // (the baked part's origin is the canonical design's origin, turned by r, from this turn's design origin)
                    s.Turns.TryAdd(h, new Hit(part, r, Rot24.Apply(r, s.Origin) - origin));
                }
            }
            return s.Turns.TryGetValue(hash, out var hit) ? hit : null;
        }
    }

    /// <summary>For <c>--maker-report</c> and the log: the split kits and the kits that don't split smaller.</summary>
    public static (int Split, int NoHelp, int Subs) SplitCounts()
    {
        WarmSubs();
        lock (Gate)
            return (_splitCount, _noHelp, SubsByKey.Count);
    }
}
