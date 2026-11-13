using System.Numerics;
using System.Reflection;

namespace CallOfSlop;

/// <summary>
/// New panel and cut keys from the wire within the tick (laser-cutter-phase2-notes.md, "Phase 2b"). A cut part's first part (its plan, its
/// collision boxes, sockets and mass) costs about 0.5 ms and a formed panel's 0.3, so a packet naming many new keys at once (a Full on joining,
/// a big blueprint set down) went over the 8 ms a tick may take. Keys read from the wire are made at once until <see cref="FirstBudgetMs"/> is
/// spent in that update; past it each gets a <b>stand-in</b>: its own id and name at once (so later packets and the bodies that hold it agree),
/// drawn and colliding as one plain box over the blank sheet's bounds, with one socket. The pump (<see cref="PumpPanels"/>) makes the real part
/// from the key, oldest first, inside its budget, copies it into the stand-in in place (the id never changes, so nothing that holds it has to
/// be found), queues its facets as any new panel's, and marks the bodies that hold it for a full rebuild (their collision, mass and sockets).
/// Every peer ends with the same part from the same key; only how soon differs. Anything else that asks for a key by name (a blueprint, a save,
/// a build step, a tool) gets the real part made at once (a stand-in waiting is finished there), so sockets are always right where they are used.
/// </summary>
public static partial class HullLoft
{
    /// <summary>What the first parts of keys from the wire may take in one update, ms; past it, stand-ins.</summary>
    public const double FirstBudgetMs = 3.0;

    private static ConstructPartDef? _standInTemplate;
    private static readonly Queue<ConstructPartDef> StandInQueue = new();
    private static readonly HashSet<ConstructPartDef> StandIns = new(ReferenceEqualityComparer.Instance);
    private static readonly List<ConstructPartDef> Reshaped = new();

    /// <summary>Stand-ins whose real part is still to make.</summary>
    public static int StandInsPending
    {
        get
        {
            lock (Interned)
                return StandIns.Count;
        }
    }

    /// <summary>(Read, under no lock) A key from the wire: the part, made now while the update's budget lasts, else a stand-in. Null: not one of these keys.</summary>
    private static ConstructPartDef? FromWire(string name)
    {
        if (!_panelPumped || PanelSync || !(IsCutName(name) || IsPanelName(name)) || !name.EndsWith(')') || name.Length > ConstructParts.MaxRuntimeName)
            return null;
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var have))
            {
                if (have != null)
                    have.Touched = Environment.TickCount64;
                return have;
            }
            double spent;
            lock (PanelJobs)
                spent = _panelSpent * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (spent < FirstBudgetMs)
                return null; // (Parse: the real first part, now)
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var made = StandIn(name);
            lock (PanelJobs)
                _panelSpent += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            if (made == null)
                return null;
            Interned[name] = made;
            StandIns.Add(made);
            StandInQueue.Enqueue(made);
            _standInsMade++;
            return made;
        }
    }

    /// <summary>A stand-in for a key: one box over the blank sheet's bounds, from the key's text alone (no plan, no part file). Null: the key can't be read.</summary>
    private static ConstructPartDef? StandIn(string name)
    {
        Vector3 mn, mx;
        if (IsCutName(name))
        {
            var plate = name.StartsWith(CutPlatePrefix, StringComparison.Ordinal);
            var inner = name[(plate ? CutPlatePrefix : CutPanelPrefix).Length..^1];
            var split = inner.IndexOf(CutSplit, StringComparison.Ordinal);
            if (split <= 0)
                return null;
            if (plate)
            {
                if (Extrusion.ParseKey(Extrusion.Prefix + inner[..split]) is not { } k || !SheetBounds(k, out mn, out mx))
                    return null;
            }
            else if (PanelOf(PanelPrefix + inner[..split] + ")") is not { } sh || !PanelBounds(sh, out mn, out mx))
                return null;
        }
        else if (PanelOf(name) is not { } sh || !PanelBounds(sh, out mn, out mx))
            return null;
        var t = _standInTemplate ??= ConstructParts.Parse(StandInJson, "a panel's stand-in");
        var d = t.ShallowCopy();
        d.Name = d.File = name;
        var (c, h) = ((mn + mx) / 2, (mx - mn) / 2);
        d.Boxes = [t.Boxes[0] with { Center = c, Half = h }, t.Boxes[1] with { Center = c, Half = h }];
        d.BoundsMin = mn;
        d.BoundsMax = mx;
        d.Sockets = [t.Sockets[0] with { At = new Vector3(c.X, mn.Y, c.Z) }];
        var size = mx - mn;
        d.Mass = Math.Clamp(size.X * size.Z * MathF.Min(size.Y, 0.05f) * 4000, 0.5f, ConstructParts.MaxPartMass); // (a guess for a few ticks)
        return Adopt(d, name);
    }

    // The stand-in's template: one hidden box, the slab drawn over it, one socket under it (each stand-in moves them to its own bounds).
    private const string StandInJson = """
        {"format":"call-of-slop-construct-part/1","name":"Panel stand-in","description":"Being made from its key.","group":"Production","section":"Cut parts",
         "role":"frame","tool":"welder","mass":10,"health":100,"material":"steel","color":"#8a8f96","attachSeconds":2,"stage":true,"centreSockets":false,
         "boxes":[{"min":[-0.5,-0.01,-0.5],"max":[0.5,0.01,0.5],"hidden":true},{"min":[-0.5,-0.01,-0.5],"max":[0.5,0.01,0.5],"detail":true}],
         "sockets":[{"name":"back","at":[0,-0.01,0],"dir":"-y"}]}
        """;

    /// <summary>An extruded blank's bounds as a cut plate has them: across x, its height in y about its middle, along z from its base.</summary>
    private static bool SheetBounds(in ExtrusionKey k, out Vector3 mn, out Vector3 mx)
    {
        float w = k.W / 100f, len = k.L / 100f;
        var half = (Extrusion.PresetOf(k.Mask) == PlateSection ? k.H / 4f : k.H) / 200f;
        if (Extrusion.PresetOf(k.Mask) == SolidSection && k.H > k.W)
            half = k.H / 200f; // (a bar on edge: its height stands in y)
        (mn, mx) = (new Vector3(-w / 2, -half, 0), new Vector3(w / 2, half, len));
        return w > 0.01f && len > 0.01f && half > 0.004f;
    }

    /// <summary>A panel's bounds over its grid's corners, thickened a little: a stand-in's box (the real part's boxes are whole centimetres inside).</summary>
    private static bool PanelBounds(in PanelShape sh, out Vector3 mn, out Vector3 mx)
    {
        var (nu, nv) = PanelGrid(sh);
        (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        for (var i = 0; i <= nu; i++)
            for (var j = 0; j <= nv; j++)
            {
                var p = PanelPoint(sh, PanelRowS(sh, i, nu), j * 2f / nv - 1);
                (mn, mx) = (Vector3.Min(mn, p), Vector3.Max(mx, p));
            }
        var t = MathF.Max(0.01f, sh.Mm / 2000f);
        (mn.Y, mx.Y) = (mn.Y - t, mx.Y + t);
        return mx.X - mn.X > 0.01f && mx.Z - mn.Z > 0.01f;
    }

    /// <summary>
    /// (Under <see cref="Interned"/>) A stand-in's real part from its key, copied into it in place: its first part (and its facets queued), or the
    /// whole part when <paramref name="whole"/>. False: the key made nothing (the stand-in stays as it is, and says so once).
    /// </summary>
    private static bool Fill(ConstructPartDef d, bool whole)
    {
        if (!StandIns.Remove(d))
            return true;
        var name = d.Name;
        try
        {
            ConstructPartDef real;
            PanelJob? job = null;
            if (IsCutName(name))
            {
                if (ReadCut(name, out var why) is not { } plan)
                {
                    Console.WriteLine($"[cuts] {name}: refused, {why} (its stand-in stays)");
                    return false;
                }
                real = CutFirst(name, plan, !whole);
                if (!whole)
                    job = new PanelJob { Def = d, Shape = plan.Sheet, Total = plan.Items, Cut = plan };
            }
            else
            {
                if (PanelOf(name) is not { } sh)
                    return false;
                var (nu, nv) = PanelGrid(sh);
                real = PanelFirst(name, sh, !whole);
                if (!whole)
                    job = new PanelJob { Def = d, Shape = sh, Total = nu * nv };
            }
            CopyInto(real, d);
            if (job != null)
                lock (PanelJobs)
                    PanelJobs.Enqueue(job);
            lock (Reshaped)
                Reshaped.Add(d);
            return true;
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[cuts] {name}: refused, {e.Message} (its stand-in stays)");
            return false;
        }
    }

    // Every field of a part but its identity in this machine's table.
    private static FieldInfo[]? _defFields;

    private static void CopyInto(ConstructPartDef from, ConstructPartDef to)
    {
        _defFields ??= typeof(ConstructPartDef).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.Name is not (nameof(ConstructPartDef.Id) or nameof(ConstructPartDef.Touched) or nameof(ConstructPartDef.Reclaimed)
                or nameof(ConstructPartDef.IsNamedRuntime) or nameof(ConstructPartDef.ExHash)))
            .ToArray();
        foreach (var f in _defFields)
            f.SetValue(to, f.GetValue(from)); // (OwnShapes too: the stand-in owned none)
    }

    /// <summary>Is this def a stand-in still waiting for its real part? (A Laser Cutter's job is never read from one: ConstructLaserCutter.JobOf.)</summary>
    public static bool IsStandIn(ConstructPartDef d)
    {
        lock (Interned)
            return StandIns.Count > 0 && StandIns.Contains(d);
    }

    /// <summary>(Parse, under <see cref="Interned"/>) A stand-in asked for by name off the wire: finished now, so its sockets are there for the caller.</summary>
    private static void FinishIfStandIn(ConstructPartDef d)
    {
        if (StandIns.Count == 0 || !StandIns.Contains(d))
            return;
        Fill(d, PanelSync || !_panelPumped);
    }

    // The batch of stand-ins being watched: how many, the worst update that asked (first parts and stand-ins), the pumps and the worst of them.
    private static int _standInsMade, _standInPumps;
    private static double _standInAsked, _standInWorst, _standInAll;

    private static void NoteStandInPump(double asked, double ms)
    {
        _standInAsked = Math.Max(_standInAsked, asked);
        _standInWorst = Math.Max(_standInWorst, ms);
        _standInAll += ms;
        _standInPumps++;
        if (StandInQueue.Count > 0 || PanelsPending > 0)
            return;
        Console.WriteLine($"[cuts] {_standInsMade} new keys off the wire waited behind stand-ins: the worst update that asked took {_standInAsked:0.0} ms, then {_standInPumps} pumps made them whole, the worst {_standInWorst:0.0} ms, {_standInAll:0} ms in all");
        (_standInsMade, _standInPumps, _standInAsked, _standInWorst, _standInAll) = (0, 0, 0, 0, 0);
    }

    /// <summary>(PumpPanels) Stand-ins made real, oldest first, until the budget is spent. Returns what is left of it.</summary>
    private static double PumpStandIns(long t0, double budgetMs)
    {
        while (true)
        {
            ConstructPartDef? d;
            lock (Interned)
            {
                if (StandInQueue.Count == 0)
                    break;
                d = StandInQueue.Dequeue();
                if (!d.Reclaimed)
                    Fill(d, budgetMs < 0);
                else
                    StandIns.Remove(d);
            }
            if (budgetMs >= 0 && System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= budgetMs)
                break;
        }
        lock (Reshaped)
        {
            if (Reshaped.Count > 0)
            {
                RuntimeParts.Reshaped(Reshaped);
                Reshaped.Clear();
            }
        }
        return budgetMs;
    }
}
