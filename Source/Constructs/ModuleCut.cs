using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Module Socket's own cutting ring (docs/sandbox/design/module-sockets.md 2.4, phase 2): a flush or opening socket whose well
/// has hull in it burns the well through. The cut is a tunnel the footprint plus <see cref="Margin"/> a side, from just over the
/// surface down to just over the plate's face; every hull part it reaches is swapped for its holed part (HullHoles.cs, the
/// breaching cutter's cut: plain boxes and lofted hull facets alike), and the part nearest the surface takes the well's liner, a
/// seal sleeve of four walls from the surface to the plate (<see cref="Liner"/>), so the well is a lined, shootable box in the
/// ship. A part that can't be holed (a machine, a joint's part, a part made at run time) leaves the socket blocked, named in its
/// look line. Time: <see cref="ModuleSocketTable.CutSecondsPerMetrePer10cm"/> a metre of outline for every 10 cm of hull, drawing
/// <see cref="ModuleSocketTable.CutWattsPerMetre"/> a metre of the footprint's mean side from the ship's circuit; with no power it
/// waits. Creative: at once. Survival: the metal cut out falls away as a loose slug.
/// <para>
/// Server. Sockets that want a cut are flagged at the rebuild (<see cref="Construct.ModCutPending"/>), so the world looks at a
/// body only after a rebuild; jobs run at 10 Hz and are few (one a socket). The cut's state on the plate is its hidden
/// <c>ModCut</c> setting (the job's seconds while it runs, 0 when idle), which rides saves and the wire, so every peer draws the
/// ring's kerf (Program.SandboxModules.cs). A save mid-cut starts the cut again on load.
/// </para>
/// </summary>
public static class ModuleCut
{
    public const int StepEvery = 6; // ticks a look (10 Hz)
    public const float Margin = 0.07f; // m: the hole past the footprint, a side (the liner's walls stand in it)
    public const float LinerGap = 0.01f; // m: the liner's inner face past the footprint, a side
    public const float WellSide = 0.005f; // m: a well's room past the footprint, a side (inside the liner)
    public const float OverSurface = 0.05f; // m: the tunnel starts this far over the surface
    public const float OverPlate = 0.01f; // m: ...and stops this far over the plate's face
    public const float LinerOverPlate = 0.003f; // m: the liner's foot over the plate's face (z-fighting rule)
    public const float LinerOverSurface = 0.003f; // m: the liner's rim over the surface

    private sealed class Job
    {
        public ushort Body;
        public int Plate;
        public ushort PlateDef;
        public float Seconds, Done, Watts;
        public bool Waiting; // no power last look (logged once)
    }

    private sealed class WorldJobs
    {
        public readonly List<Job> Jobs = new(8);
        public int Ticks;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConstructWorld, WorldJobs> ByWorld = new();

    /// <summary>The cut's plan for one socket: the parts to swap and what they become, the first that can't be cut, the hull's thickness.</summary>
    /// <summary>(A null Holed: the part lies wholly in the well and burns away.)</summary>
    public readonly record struct Plan(List<(int Part, ConstructPartDef? Holed)> Cuts, int Blocker, string Why, float Thick, float Seconds, float Watts);

    /// <summary>Server, each tick: bodies flagged at their rebuild start jobs; jobs burn on.</summary>
    public static void Step(ConstructWorld world)
    {
        var wj = ByWorld.GetOrCreateValue(world);
        if (++wj.Ticks < StepEvery)
            return;
        wj.Ticks = 0;
        var bodies = world.Bodies;
        for (var b = 0; b < bodies.Count; b++)
        {
            var c = bodies[b];
            if (!c.ModCutPending || c.Dirty)
                continue;
            c.ModCutPending = false;
            Start(world, wj, c);
        }
        foreach (var j in wj.Jobs)
            if (world.Get(j.Body) is { } jc)
                jc.ModCutWatts = 0; // (each burning job adds its ring's draw again below: two cuts on one body draw both)
        for (var k = wj.Jobs.Count - 1; k >= 0; k--)
            if (Burn(world, wj.Jobs[k]))
                wj.Jobs.RemoveAt(k);
    }

    /// <summary>Is socket <paramref name="s"/> one whose well wants cutting now (a flush or opening plate with no module, something in its well)?</summary>
    public static bool Wants(in CModSocket s) => !s.Stand && s.Style != ModStyle.Proud && s.Module < 0 && s.InTheWay >= 0;

    private static void Start(ConstructWorld world, WorldJobs wj, Construct c)
    {
        for (var si = 0; si < c.ModSockets.Length; si++)
        {
            var s = c.ModSockets[si];
            var running = wj.Jobs.Exists(j => j.Body == c.Id && j.Plate == s.Plate);
            if (!running && !s.Stand && ConstructConfig.Get(c.Parts[s.Plate], c.DefOf(s.Plate), CfgKey.ModCut, 0) > 0)
            {
                ref var sp = ref c.Parts[s.Plate];
                ConstructConfig.Put(ref sp, sp.D, CfgKey.ModCut, 0); // (a stale cut: a save made mid-cut, or a plate that came off a body; started again below if it still wants one)
                world.StructureDirty.Add(c.Id);
            }
            ModuleFitters.ClearStale(world, c, s.Plate); // (a fitter's weld or cut left by a save mid-job)
            if (!Wants(s) || running)
                continue;
            var plan = PlanOf(c, si);
            if (plan.Blocker >= 0)
            {
                Console.WriteLine($"[modules] construct {c.Id}: socket part {s.Plate} can't cut its well: {plan.Why}");
                continue;
            }
            if (plan.Cuts.Count == 0)
                continue; // (only things the tunnel misses: its room's own test names them)
            var job = new Job { Body = c.Id, Plate = s.Plate, PlateDef = c.Parts[s.Plate].Def, Seconds = world.Creative && !world.TimedMachines ? 0 : plan.Seconds, Watts = plan.Watts };
            wj.Jobs.Add(job);
            ref var p = ref c.Parts[s.Plate];
            ConstructConfig.Put(ref p, p.D, CfgKey.ModCut, MathF.Max(0.1f, job.Seconds));
            world.StructureDirty.Add(c.Id);
            world.Events.Add(FormattableString.Invariant($"construct {c.Id}: socket part {s.Plate} starts cutting its {(s.Style == ModStyle.Through ? "opening" : "well")}: {plan.Cuts.Count} parts, {plan.Thick * 100:0} cm of hull, {plan.Seconds:0} s at {plan.Watts / 1000:0.#} kW"));
        }
    }

    /// <summary>One look at a job: true when it is over (done, or its socket gone).</summary>
    private static bool Burn(ConstructWorld world, Job j)
    {
        var c = world.Get(j.Body);
        if (c == null || j.Plate >= c.PartCount || c.Parts[j.Plate].Def != j.PlateDef || c.Parts[j.Plate].Broken)
        {
            if (c != null && j.Plate < c.PartCount && ConstructConfig.Get(c.Parts[j.Plate], c.DefOf(j.Plate), CfgKey.ModCut, 0) > 0)
            {
                ref var bp = ref c.Parts[j.Plate];
                ConstructConfig.Put(ref bp, bp.D, CfgKey.ModCut, 0); // (a plate shot off or changed: its ring stops; a copy that left with a detached plate is cleared by Start)
                world.StructureDirty.Add(c.Id);
            }
            return true;
        }
        if (c.Dirty)
            c.Rebuild();
        var si = ModuleSockets.SocketOfPlate(c, j.Plate);
        if (si < 0 || !Wants(c.ModSockets[si]))
            return Stop(world, c, j, "nothing left to cut");
        var dt = StepEvery / (float)World.TickRate;
        if (j.Done < j.Seconds)
        {
            if (!c.Electrical || !c.PowerOn)
            {
                if (!j.Waiting)
                    world.Events.Add(FormattableString.Invariant($"construct {c.Id}: socket part {j.Plate}'s cut waits: it needs {j.Watts / 1000:0.#} kW"));
                j.Waiting = true;
                return false;
            }
            j.Waiting = false;
            c.ModCutWatts += j.Watts; // (ConstructPower counts it with the circuit's draw; reset each look before the jobs burn)
            j.Done += dt;
            if (j.Done < j.Seconds)
                return false;
        }
        var plan = PlanOf(c, si); // (again: the hull may have changed while it burned)
        if (plan.Blocker >= 0)
            return Stop(world, c, j, plan.Why);
        Apply(world, c, si, plan);
        return Stop(world, c, j, null);
    }

    private static bool Stop(ConstructWorld world, Construct c, Job j, string? why)
    {
        ref var p = ref c.Parts[j.Plate];
        ConstructConfig.Put(ref p, p.D, CfgKey.ModCut, 0);
        c.Dirty = true;
        world.StructureDirty.Add(c.Id);
        if (why != null)
            Console.WriteLine($"[modules] construct {c.Id}: socket part {j.Plate}'s cut stopped: {why}");
        return true;
    }

    /// <summary>
    /// The tunnel a socket's cut burns, from its mouth along -<paramref name="n"/> for <paramref name="len"/>, and its liner's two ends (the mouth's
    /// first). A well (flush, Well): from inside the plate's frame back to the well's floor, <c>Seat</c> behind its face. An Opening
    /// (<see cref="ModStyle.Through"/>): from inside the plate back through what it is welded to, <c>Depth</c> behind its back, lined from its back.
    /// </summary>
    public static void Tunnel(Construct c, in CModSocket s, Vector3 face, Vector3 n, out Vector3 mouth, out float len, out Vector3 linerA, out Vector3 linerB)
    {
        if (s.Style == ModStyle.Through)
        {
            var thick = ModuleSockets.Thickness(c.DefOf(s.Plate));
            mouth = face - n * OverPlate;
            len = thick + s.Depth - OverPlate;
            linerA = face - n * (thick + LinerOverPlate);
            linerB = face - n * (thick + s.Depth);
            return;
        }
        if (s.Seat <= 0)
        {
            // A built bay's well (a Wall Bay Socket: ModuleSockets.Build): over the plate's face, from just over the surface down to the face.
            mouth = face + n * (s.Depth + OverSurface);
            len = s.Depth + OverSurface - OverPlate;
            linerA = face + n * (s.Depth + LinerOverSurface);
            linerB = face + n * LinerOverPlate;
            return;
        }
        // A well (flush, Well): dug into what the plate is welded to, from inside the frame down to the floor, Seat behind the face (the
        // plate on the surface, Sean 2026-11-21); lined from the plate's back.
        var back = ModuleSockets.Thickness(c.DefOf(s.Plate));
        mouth = face - n * OverPlate;
        len = s.Seat - OverPlate;
        linerA = face - n * (back + LinerOverPlate);
        linerB = face - n * s.Seat;
    }

    /// <summary>The plate's frame in the world: its face's middle, its normal (out of the face), its across (x) and its up (z, the long side).</summary>
    private static void Frame(Construct c, int plate, out Vector3 face, out Vector3 n, out Vector3 across, out Vector3 up)
    {
        // (Through the plate's segment frame: a plate socket-welded onto a craft sits on a freeform segment, whose numbers are in its weld's frame.)
        var d = c.DefOf(plate);
        ref var p = ref c.Parts[plate];
        c.SocketAt(plate, d.ModSocket, out var at, out _);
        c.WorldFrame(p.Seg, out var q, out var t);
        face = t + Vector3.Transform(at, q);
        n = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitY), q));
        across = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitX), q));
        up = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitZ), q));
    }

    /// <summary>
    /// What cutting socket <paramref name="si"/>'s well would do: for every part the tunnel reaches, its holed part; or the first that
    /// can't be holed. Makes the holed defs (each key once). Server, at a job's start and end only.
    /// </summary>
    public static Plan PlanOf(Construct c, int si)
    {
        var s = c.ModSockets[si];
        var cuts = new List<(int, ConstructPartDef?)>();
        var t = ModuleSocketTable.Current;
        if (!c.Rigid(c.Parts[s.Plate].Seg))
            return new Plan(cuts, s.Plate, "a socket on a joint can't cut a well", 0, 0, 0); // (a freeform weld, as a player's socket weld makes, is rigid: it cuts)
        Frame(c, s.Plate, out var face, out var n, out var across, out var up);
        var depth = s.Depth;
        Tunnel(c, s, face, n, out var mouth, out var len, out _, out _);
        var halfW = s.W * 0.5f + Margin;
        var halfL = s.L * 0.5f + Margin;
        // The candidates: parts near the tunnel's box in the rest frame (the plate is on the body's own frame).
        var inv = Quaternion.Conjugate(c.Rot);
        Vector3 R(Vector3 w) => Vector3.Transform(w - c.Pos, inv);
        var ra = R(mouth - n * len - across * halfW - up * halfL);
        var rb = R(mouth + across * halfW + up * halfL);
        var lo = Vector3.Min(ra, rb);
        var hi = Vector3.Max(ra, rb);
        var hits = Construct.PartScratch;
        var near = c.PartsInBox(lo, hi, hits);
        var list = new int[near];
        Array.Copy(hits, list, near); // (PartScratch is shared: the holed parts' making may use it)
        var of = c.ModuleOf;
        var thick = 0f;
        foreach (var j in list)
        {
            if (j == s.Plate || c.Parts[j].Broken || c.Rides(j) || of != null && j < of.Length && of[j] >= 0)
                continue;
            var d = c.DefOf(j);
            if (d.ModPart != ModPart.None)
                continue; // (another plate or plug: the room's test names it)
            Freeform.PartWorld(c, j, out var pos, out var rot);
            var iq = Quaternion.Conjugate(rot);
            var local = HullHoles.Hole.Of(Vector3.Transform(mouth - pos, iq), Vector3.Transform(-n, iq), Vector3.Transform(up, iq), halfW, halfL, len);
            var mn = d.HasSkin ? Vector3.Min(d.BoundsMin, d.SkinMin) : d.BoundsMin;
            var mx = d.HasSkin ? Vector3.Max(d.BoundsMax, d.SkinMax) : d.BoundsMax;
            if (!HullHoles.ReachesBounds(mn, mx, local))
                continue;
            if (!c.Rigid(c.Parts[j].Seg))
                return new Plan(cuts, j, $"the {d.Name} (part {j}) is on a joint", 0, 0, 0);
            if (Array.Exists(HullHoles.HolesOf(d), h => h.TunnelText == local.TunnelText))
                continue; // (cut already: this well's hole)
            if (HullHoles.WithHole(d, local, recut: true) is { } holed)
            {
                cuts.Add((j, holed));
                // The hull's thickness here: the part's extent along the normal inside the well.
                ModuleSockets.RestBounds(c, j, out var bmn, out var bmx);
                var rn = Vector3.Abs(Vector3.Transform(n, inv));
                var ext = Vector3.Dot(bmx - bmn, rn);
                thick += MathF.Min(ext, depth);
                continue;
            }
            var why = HullHoles.LastRefusal ?? "";
            if (why == HullHoles.Misses)
                continue;
            if (why == HullHoles.NothingLeft && HullHoles.Refusal(HullHoles.BaseOf(d)) == null)
            {
                cuts.Add((j, null)); // (wholly inside the well: burned away)
                ModuleSockets.RestBounds(c, j, out var bmn, out var bmx);
                thick += MathF.Min(Vector3.Dot(bmx - bmn, Vector3.Abs(Vector3.Transform(n, inv))), depth);
                continue;
            }
            return new Plan(cuts, j, $"in the way: the {HullHoles.Shown(d)} (part {j}) can't be cut ({why})", 0, 0, 0);
        }
        thick = MathF.Max(thick, 0.01f);
        var outline = 2f * (s.W + s.L);
        var seconds = t.CutSecondsPerMetrePer10cm * outline * MathF.Ceiling(thick / 0.1f - 0.001f);
        var watts = t.CutWattsPerMetre * (s.W + s.L) * 0.5f;
        return new Plan(cuts, -1, "", thick, seconds, watts);
    }

    /// <summary>The cut made: the parts swapped for their holed parts, the liner on the one nearest the surface, the slug dropped (Survival).</summary>
    private static void Apply(ConstructWorld world, Construct c, int si, in Plan plan)
    {
        var s = c.ModSockets[si];
        Frame(c, s.Plate, out var face, out var n, out var across, out var up);
        var lost = 0f;
        var top = -1;
        var topAt = float.MinValue;
        foreach (var (j, holed) in plan.Cuts)
        {
            ref var p = ref c.Parts[j];
            if (holed == null)
                continue; // (burned below, once every swap is made: a break may split the body)
            lost += MathF.Max(0, p.D.Mass - holed.Mass);
            p.Def = holed.Id;
            ModuleSockets.RestBounds(c, j, out var mn, out var mx); // (the rest frame: parts on freeform welds too)
            var h = Vector3.Dot(c.ToWorld((mn + mx) * 0.5f) - face, n);
            if (h > topAt)
                (top, topAt) = (j, h);
        }
        if (top >= 0)
            Liner(c, top, s, face, n, across, up);
        foreach (var (j, holed) in plan.Cuts)
            if (holed == null && j < c.PartCount && !c.Parts[j].Broken)
            {
                lost += c.Parts[j].D.Mass;
                world.DamagePart(c, j, c.Parts[j].Health + 1f, out _, raw: true); // (wholly inside the well: burned away, as a breaching cutter's fitting)
            }
        c.Dirty = true;
        world.StructureDirty.Add(c.Id);
        c.Wake();
        world.Events.Add(FormattableString.Invariant($"construct {c.Id}: socket part {s.Plate} cut its {ModuleSocketTable.SizeText(s.W, s.L, s.Round)} {(s.Style == ModStyle.Through ? "opening" : "well")}, {s.Depth:0.##} m deep, through {plan.Cuts.Count} parts"));
        if ((!world.Creative || world.TimedMachines) && lost > 1f)
            Slug(world, c, s, face, n, lost);
    }

    /// <summary>
    /// The well's liner: the hole in part <paramref name="part"/> (the one nearest the surface) lined from the surface down to the plate's
    /// face, four walls through the hull's thickness (<see cref="HullHoles.Hole.WithLining"/>: on the hole's own entry, so a deck with two
    /// wells keeps its key short enough for both liners, Sean 2026-11-22).
    /// </summary>
    private static void Liner(Construct c, int part, in CModSocket s, Vector3 face, Vector3 n, Vector3 across, Vector3 up)
    {
        Freeform.PartWorld(c, part, out var pos, out var rot);
        var iq = Quaternion.Conjugate(rot);
        Tunnel(c, s, face, n, out var mouth, out var len, out var a, out var b);
        var local = HullHoles.Hole.Of(Vector3.Transform(mouth - pos, iq), Vector3.Transform(-n, iq), Vector3.Transform(up, iq), s.W * 0.5f + Margin, s.L * 0.5f + Margin, len);
        var d = c.DefOf(part);
        if (HullHoles.WithLining(d, local.TunnelText, Vector3.Dot(a - mouth, -n), Vector3.Dot(b - mouth, -n), Margin - LinerGap) is not { } lined)
        {
            Console.WriteLine($"[modules] construct {c.Id}: the well of socket part {s.Plate} has no liner: the {HullHoles.Shown(d)} can't take one ({HullHoles.LastRefusal})");
            return;
        }
        c.Parts[part].Def = lined.Id;
    }

    /// <summary>The metal cut out, as one loose steel plate of the footprint at the thickness its mass makes, just over the well.</summary>
    private static void Slug(ConstructWorld world, Construct c, in CModSocket s, Vector3 face, Vector3 n, float kg)
    {
        const float Steel = 7850f;
        var cm = kg / (Steel * s.W * s.L) * 100f; // (a plate section is a quarter of its height thick: HullLoft.StockMm)
        var h = Math.Clamp((int)MathF.Round(cm * 4 / Extrusion.StepCm) * Extrusion.StepCm, Extrusion.MinSectionCm, 200);
        var name = FormattableString.Invariant($"{Extrusion.Prefix}plate {s.W * 100}x{h}x{s.L * 100} steel");
        if (Extrusion.ParseKey(name) is not { } k || Extrusion.Def(k) is not { } def)
        {
            Console.WriteLine($"[modules] construct {c.Id}: the slug of socket part {s.Plate}'s well ({kg:0} kg) is dropped: no piece \"{name}\"");
            return;
        }
        if (def.Mass > kg * 1.05f)
        {
            Console.WriteLine(FormattableString.Invariant($"[modules] construct {c.Id}: the slug of socket part {s.Plate}'s well ({kg:0} kg) is too thin to keep as a plate (the thinnest is {def.Mass:0} kg): burned away"));
            return;
        }
        var at = face + n * (s.Depth + 0.3f + h * 0.0025f);
        ModuleSockets.PlateRest(c, s.Plate, out var pr, out _, out _); // (the rest frame: a plate welded on freeform is in its weld's)
        if (world.Create(at, c.Rot * Rot24.Quat(pr)) is not { } slug)
        {
            Console.WriteLine($"[modules] construct {c.Id}: the slug of socket part {s.Plate}'s well is dropped: the world is full");
            return;
        }
        world.AddPart(slug, def, Vector3.Zero, 0);
        slug.Vel = c.Vel + n * 0.5f;
        slug.Wake();
        world.Events.Add($"construct {c.Id}: the well's slug fell away ({kg:0} kg) -> construct {slug.Id}");
    }

    private static readonly Dictionary<(Construct, int), double> FirstSeen = new(); // (by the body itself, so another world's body of the same id is another key; at most MaxSeen)
    private const int MaxSeen = 64;

    /// <summary>Any peer: seconds since this plate's cut was first seen running here (its <c>ModCut</c> setting over 0), else 0.</summary>
    public static double Seen(Construct c, int plate)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        var on = ConstructConfig.Get(c.Parts[plate], c.DefOf(plate), CfgKey.ModCut, 0) > 0;
        var key = (c, plate);
        if (!on)
        {
            FirstSeen.Remove(key);
            return 0;
        }
        if (!FirstSeen.TryGetValue(key, out var t0))
        {
            if (FirstSeen.Count >= MaxSeen)
                FirstSeen.Clear(); // (bodies gone or worlds left while cutting: the next look starts their clocks again)
            FirstSeen[key] = t0 = now;
        }
        return now - t0;
    }

    /// <summary>The look line's words for a socket's cut (any peer): its progress while it burns, or why it can't.</summary>
    public static string? LookText(Construct c, in CModSocket s, double sinceSeen)
    {
        if (s.Style == ModStyle.Proud || s.Stand)
            return null;
        var total = ConstructConfig.Get(c.Parts[s.Plate], c.DefOf(s.Plate), CfgKey.ModCut, 0);
        if (total > 0)
            return FormattableString.Invariant($"{(s.Style == ModStyle.Through ? "punching its opening" : "cutting its well")}, {Math.Clamp(sinceSeen / total, 0, 0.99) * 100:0}% ({ModuleSocketTable.Current.CutWattsPerMetre * (s.W + s.L) * 0.5f / 1000:0.#} kW)");
        if (s.InTheWay < 0 || s.Module >= 0)
            return null;
        var d = c.DefOf(s.InTheWay);
        if (!c.Rigid(c.Parts[s.InTheWay].Seg))
            return $"the {d.Name} is in its well, on a joint: it can't be cut";
        return HullHoles.Refusal(HullHoles.BaseOf(d)) is { } why
            ? $"the {HullHoles.Shown(d)} is in its well and can't be cut ({why}): move it"
            : $"the {HullHoles.Shown(d)} is in its well: the ring cuts it when the circuit has power";
    }
}

public sealed partial class Construct
{
    public float ModFitWatts; // server: the draw of Module Fitters' welds and cuts on this body (ConstructModuleFitters.cs), counted by ConstructPower
    public bool ModCutPending; // a flush or opening socket's well has something in it (ModuleSockets.Build): the server's ModuleCut looks
    public float ModCutWatts; // server: what the sockets' cutting rings draw now (W; ConstructPower adds it)
}
