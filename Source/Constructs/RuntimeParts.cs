namespace CallOfSlop;

/// <summary>
/// Frees run-time parts nothing uses any more (docs/sandbox/CONSTRUCTS.md, "Run-time parts and their slots"). Parts made by key
/// (extrusions, lofted bays, cabs, trims, ordnance designs, racks, other materials) each take a slot of
/// <see cref="Extrusion.MaxDefs"/> and their drawn shapes take slots of <see cref="PartShapes.Cap"/>; a vehicle the kit
/// generates makes hundreds of new ones, so a long session or a tool that makes many vehicles would fill both. A sweep marks
/// every part a construct, a storage slot or a hand holds, in every <see cref="ConstructWorld"/> of the process, and frees the
/// rest that weren't made or asked for by key lately: the part leaves its key table (asked for again, it is made again with a new
/// id), its slot and its shapes go back. A freed slot is taken again only once every slot has been used, oldest first, and the old
/// def stays behind its id until then, so a holder the sweep didn't see reads the part it had for a long while.
/// Ids never cross the wire or a save (keys do), so each machine frees on its own.
/// </summary>
public static class RuntimeParts
{
    /// <summary>The game sweeps when either table is past this share, at most once a <see cref="EveryMs"/>.</summary>
    public const float SweepAt = 0.5f;

    public const float WarnAt = 0.8f;
    public const long EveryMs = 30_000, WarnEveryMs = 60_000;

    /// <summary>In the game a part made or asked for by key this recently is kept (a blueprint being built, a packet on the way, a ghost).</summary>
    public const long GraceMs = 120_000;

    private static readonly List<WeakReference<ConstructWorld>> Worlds = new();
    private static readonly List<WeakReference<ConstructEdits>> Hands = new();
    private static readonly object TrackGate = new();
    private static bool[] _mark = [];
    private static long _nextSweep, _nextCheck, _lastWarn = -WarnEveryMs;
    private static volatile bool _pressed;

    /// <summary>Freed in all, and in the last sweep.</summary>
    public static int FreedTotal, FreedLast, Sweeps;

    internal static void Track(ConstructWorld w) => Add(Worlds, w);

    internal static void Track(ConstructEdits e) => Add(Hands, e);

    private static void Add<T>(List<WeakReference<T>> list, T item) where T : class
    {
        lock (TrackGate)
        {
            if (list.Count >= 64 && (list.Count & 63) == 0)
                list.RemoveAll(r => !r.TryGetTarget(out _)); // (tools make many short-lived worlds)
            list.Add(new WeakReference<T>(item));
        }
    }

    /// <summary>Called when a slot is taken: one throttled warning past <see cref="WarnAt"/>, and a sweep asked for at once past 90%.</summary>
    internal static void NoteFill(int used, int cap, string what)
    {
        if (used < cap * WarnAt)
            return;
        if (used >= cap * 0.9f)
            _pressed = true;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastWarn) < WarnEveryMs)
            return;
        Interlocked.Exchange(ref _lastWarn, now);
        Console.WriteLine($"[parts] warning: {what} at {used} of {cap} ({used * 100 / cap}%): a sweep frees what nothing holds (RuntimeParts.cs)"); // (no other table's lock here: callers hold theirs)
    }

    /// <summary>Use of both tables, for a log line.</summary>
    public static string Line() => $"run-time parts {Extrusion.Live}/{Extrusion.Limit} (slots used {Extrusion.DefCount}), part shapes {PartShapes.Live}/{PartShapes.Limit} (slots used {PartShapes.Count}), box looks {BoxLooks.Count}/65535, freed {FreedTotal} in {Sweeps} sweeps";

    /// <summary>
    /// Once a frame, between the game's ticks (Program's loop): a sweep when a table is past <see cref="SweepAt"/> (or near full),
    /// at most once a <see cref="EveryMs"/> (a vehicle being generated or set down holds its parts in a tracked world, or made them within the grace). Allocates nothing when it has nothing to do.
    /// </summary>
    public static void Tick()
    {
        var now = Environment.TickCount64;
        if (now < _nextCheck)
            return;
        _nextCheck = now + 1000;
        var full = MathF.Max(Extrusion.Live / (float)Extrusion.Limit, PartShapes.Live / (float)PartShapes.Limit);
        if (full < SweepAt && !_pressed || now < _nextSweep && !(_pressed && now >= _nextSweep - EveryMs + 2000))
            return;
        _pressed = false;
        _nextSweep = now + EveryMs;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var freed = Sweep(GraceMs);
        Console.WriteLine($"[parts] swept in {clock.Elapsed.TotalMilliseconds:0.0} ms: freed {freed}; {Line()}");
    }

    /// <summary>
    /// Frees every run-time part no tracked world or hand holds and that wasn't made or asked for by key in the last
    /// <paramref name="graceMs"/>. Returns how many. On the game's thread, between ticks; a tool calls it with 0 between
    /// vehicles, holding no part of its own.
    /// </summary>
    public static int Sweep(long graceMs)
    {
        var cutoff = Environment.TickCount64 - graceMs;
        if (graceMs == 0)
            GC.Collect(); // (a tool: worlds it has let go of hold nothing)
        int count;
        lock (Extrusion.Gate)
            count = Extrusion.DefCount;
        if (_mark.Length < count)
            _mark = new bool[Extrusion.MaxDefs];
        Array.Clear(_mark);
        MarkRoots();
        // The candidates: neither held nor touched lately (re-checked under each key table's lock, where a lookup touches them).
        var maybe = new HashSet<ConstructPartDef>(ReferenceEqualityComparer.Instance);
        lock (Extrusion.Gate)
            for (var k = 0; k < count; k++)
                if (!_mark[k] && Extrusion.AtSlot(k) is { Reclaimed: false } d && d.Touched < cutoff)
                    maybe.Add(d);
        if (maybe.Count == 0)
            return 0;
        var dropped = new List<ConstructPartDef>(maybe.Count);
        bool Doomed(ConstructPartDef d)
        {
            if (!maybe.Contains(d) || d.Touched >= cutoff)
                return false;
            dropped.Add(d);
            return true;
        }
        HullLoft.Forget(Doomed);
        Pressed.Forget(Doomed);
        Ordnance.Forget(Doomed);
        ModularRack.Forget(Doomed);
        PartMaterials.Forget(Doomed);
        lock (Extrusion.Gate)
        {
            Extrusion.Forget(Doomed);
            foreach (var d in dropped)
            {
                d.Reclaimed = true;
                Extrusion.FreeSlot(d);
            }
        }
        foreach (var d in dropped)
            PartShapes.Release(d.OwnShapes);
        Forgotten();
        FreedLast = dropped.Count;
        FreedTotal += dropped.Count;
        Sweeps++;
        return dropped.Count;
    }

    // Caches that know a part by its id or hold its def: emptied (rare; they fill again as they are asked).
    private static void Forgotten()
    {
        ConstructMirror.ForgetRuntime();
        ConstructAmmo.ForgetRuntime();
        ConstructRender.ForgetRuntime();
    }

    /// <summary>
    /// A run-time part's drawn boxes were swapped for others in place (a formed panel's facets for its slabs: HullLoft.PumpPanels); its solid
    /// boxes, bounds and mass are as they were. Every body's drawn batch is made again and the caches that count a part's boxes are emptied. Rare.
    /// </summary>
    internal static void Redrawn()
    {
        Forgotten();
        lock (TrackGate)
            foreach (var r in Worlds)
                if (r.TryGetTarget(out var w))
                    foreach (var c in w.Bodies)
                        c.RebuildStamp++;
    }

    private static void MarkRoots()
    {
        lock (TrackGate)
        {
            foreach (var r in Worlds)
                if (r.TryGetTarget(out var w))
                    MarkWorld(w);
            foreach (var r in Hands)
                if (r.TryGetTarget(out var e))
                    foreach (ref readonly var h in e.Hands.AsSpan())
                    {
                        Mark(h.Held);
                        Mark(h.HeldGone);
                        Mark(h.MagFor);
                    }
        }
    }

    private static void MarkWorld(ConstructWorld w)
    {
        foreach (var c in w.Bodies)
            for (var i = 0; i < c.PartCount; i++)
                Mark(c.Parts[i].Def);
        foreach (var s in w.Stores)
        {
            if (s == null || !s.Live)
                continue;
            for (var k = 0; k < s.SlotCount; k++)
                Mark(s.Def[k]);
            for (var k = 0; k < s.KindN; k++)
                Mark(s.KindDef![k]);
        }
    }

    private static void Mark(ushort id)
    {
        var k = id - Extrusion.RuntimeBase;
        if (k >= 0 && k < _mark.Length)
            _mark[k] = true;
    }

    /// <summary>(Under the table's own lock) Takes the doomed parts out of a key table.</summary>
    internal static void Drop<TKey>(Dictionary<TKey, ConstructPartDef> table, Func<ConstructPartDef, bool> doomed) where TKey : notnull
    {
        List<TKey>? gone = null;
        foreach (var (k, v) in table)
            if (doomed(v))
                (gone ??= new List<TKey>()).Add(k);
        if (gone != null)
            foreach (var k in gone)
                table.Remove(k);
    }

    /// <summary>The same for a table that also remembers keys that made nothing.</summary>
    internal static void DropOrNull<TKey>(Dictionary<TKey, ConstructPartDef?> table, Func<ConstructPartDef?, bool> doomed) where TKey : notnull
    {
        List<TKey>? gone = null;
        foreach (var (k, v) in table)
            if (doomed(v))
                (gone ??= new List<TKey>()).Add(k);
        if (gone != null)
            foreach (var k in gone)
                table.Remove(k);
    }
}
