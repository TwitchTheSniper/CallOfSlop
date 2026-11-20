using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Module Fitters (docs/sandbox/design/module-sockets.md 7, phase 4): a lifter whose part says <c>"fitter": true</c> (the Module
/// Fitter S, LifterParts.ModuleFitter) fits modules the hand can't (past 2x2 or the physics gun's lift, or lying past the hand's
/// pull) to the sockets of its own construct, and takes them off onto a free Module Stand of their size.
/// <para>
/// A fit is a job of two phases: the carry (the fitter's hand takes the module body along its scripted path to its seat at the
/// plate: <see cref="ConstructLifters.CarryBody"/>, posed in the lifter's frame, so a ship in flight keeps it), then the weld (the
/// module held at its seat in the ship's frame each tick while the torch runs the plug's outline at
/// the table's S fitter weld speed (m/s), drawing its power; the merge when the
/// bead closes). A take-off is the seam cut at the cutting speed (the module stays fitted, then is cut out as a body), the carry to
/// the stand, and the weld onto the stand. The plate's hidden <c>ModWeld</c> setting is the job's seconds (positive weld, negative
/// cut), which rides the wire, so every peer draws the torch (Program.SandboxModules.cs). Creative: no weld or cut time.
/// </para>
/// <para>
/// Server. Jobs are few (one a fitter): each tick poses the held bodies, every <see cref="StepEvery"/> ticks the jobs move on.
/// Jobs aren't saved (no MFIT block yet): a save mid-job keeps the module where it was carried to or fitted, the stale ModWeld is
/// cleared at the plate's next rebuild, and F starts the job again. Nothing allocated per tick.
/// </para>
/// </summary>
public static class ModuleFitters
{
    public const int StepEvery = 6; // ticks a look (10 Hz)
    public const int MaxJobs = 32; // in a world

    public enum Phase : byte { Carry, Weld, Cut, CarryOff, WeldOff }

    public sealed class Job
    {
        public ushort Ship, Module; // the construct the socket is on, the module's body (while it is one)
        public int Plate; // the socket's plate (a stand's, for a take-off's last weld)
        public ushort PlateDef;
        public int From = -1; // a take-off: the socket it came off
        public byte Turn;
        public Vector3 Rel; // the module body's offset in the ship's frame at its seat
        public Phase Phase;
        public float Seconds, Done;
        public string Fitter = "";
        public bool Waiting;
    }

    private sealed class WorldJobs
    {
        public readonly List<Job> Jobs = new(MaxJobs);
        public int Ticks;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConstructWorld, WorldJobs> ByWorld = new();

    /// <summary>Does construct <paramref name="c"/> carry a Module Fitter (working)? Its largest reach when it does (m), else 0.</summary>
    public static float Reach(ConstructWorld world, Construct c)
    {
        if (!world.LiftSeen.TryGetValue(c.Id, out var lb) || lb.N == 0)
            return 0;
        var r = 0f;
        for (var i = 0; i < lb.N; i++)
            if (lb.Lifts[i].Part < c.PartCount && ConstructLifters.DefOf(c, lb.Lifts[i].Part) is { Fitter: true } ld && !c.Parts[lb.Lifts[i].Part].Broken)
                r = MathF.Max(r, ld.ReachMax + ld.Up);
        return r;
    }

    /// <summary>Is a fitter's job on plate <paramref name="plate"/> of <paramref name="c"/> (or on the module body <paramref name="body"/>)?</summary>
    public static bool Busy(ConstructWorld world, Construct c, int plate, ushort body = 0)
    {
        var wj = ByWorld.GetOrCreateValue(world);
        foreach (var j in wj.Jobs)
            if (j.Ship == c.Id && (j.Plate == plate || j.From == plate) || body != 0 && j.Module == body)
                return true;
        return false;
    }

    /// <summary>
    /// Server: a fitter of <paramref name="c"/> carries module body <paramref name="body"/> (whose plug is <paramref name="plug"/>) to
    /// socket <paramref name="si"/> at the seat <paramref name="turn"/>, <paramref name="rel"/>, then welds it on. Null when started.
    /// </summary>
    public static string? Fit(ConstructWorld world, Construct c, int si, Construct body, int plug, byte turn, Vector3 rel, Quaternion rot, Vector3 pos)
    {
        var wj = ByWorld.GetOrCreateValue(world);
        if (wj.Jobs.Count >= MaxJobs)
            return "every Module Fitter in the world is busy";
        var s = c.ModSockets[si];
        if (Busy(world, c, s.Plate, body.Id))
            return "a Module Fitter is already at work on it";
        if (!ConstructLifters.CarryBody(world, c, -1, body, pos, rot, out var who, out var why, fitter: true))
            return why;
        wj.Jobs.Add(new Job { Ship = c.Id, Module = body.Id, Plate = s.Plate, PlateDef = c.Parts[s.Plate].Def, Turn = turn, Rel = rel, Phase = Phase.Carry, Fitter = who });
        world.Events.Add($"construct {c.Id}: the {who} carries module body {body.Id} to socket part {s.Plate}");
        return null;
    }

    /// <summary>
    /// Server: a fitter of <paramref name="c"/> cuts the seam of the module on socket <paramref name="si"/> and sets it on a free
    /// Module Stand of its size on the same construct. Null when started.
    /// </summary>
    public static string? TakeOff(ConstructWorld world, Construct c, int si)
    {
        var wj = ByWorld.GetOrCreateValue(world);
        if (wj.Jobs.Count >= MaxJobs)
            return "every Module Fitter in the world is busy";
        var s = c.ModSockets[si];
        if (Busy(world, c, s.Plate))
            return "a Module Fitter is already at work on it";
        if (FreeStand(c, s.W, s.L, -1) < 0)
            return $"no free {ModuleSocketTable.SizeText(s.W, s.L)} Module Stand on this construct to set it on (a fitter takes a module off onto a stand)";
        var t = ModuleSocketTable.Current;
        var job = new Job { Ship = c.Id, Plate = s.Plate, PlateDef = c.Parts[s.Plate].Def, From = s.Plate, Phase = Phase.Cut, Fitter = "Module Fitter",
            Seconds = world.Creative && !world.TimedMachines ? 0 : Perimeter(s.W, s.L) / t.FitterS.Cut };
        wj.Jobs.Add(job);
        SetWeld(world, c, s.Plate, -MathF.Max(0.1f, job.Seconds));
        world.Events.Add(FormattableString.Invariant($"construct {c.Id}: a Module Fitter cuts the seam of the module on socket part {s.Plate}: {job.Seconds:0} s"));
        return null;
    }

    private static float Perimeter(int w, int l) => 2f * (w + l);

    /// <summary>A free Module Stand of size W x L on <paramref name="c"/> (nothing on it, no job at it), or -1.</summary>
    private static int FreeStand(Construct c, int w, int l, int skip)
    {
        foreach (var s in c.ModSockets)
            if (s.Stand && s.Module < 0 && s.W == w && s.L == l && s.Plate != skip && s.InTheWay < 0)
                return s.Plate;
        return -1;
    }

    private static void SetWeld(ConstructWorld world, Construct c, int plate, float seconds)
    {
        if (plate >= c.PartCount || c.DefOf(plate).ModPart != ModPart.Socket)
            return;
        ref var p = ref c.Parts[plate];
        ConstructConfig.Put(ref p, p.D, CfgKey.ModWeld, seconds);
        world.StructureDirty.Add(c.Id);
    }

    /// <summary>A plate's ModWeld left by a job no longer running (a save mid-job): cleared (ModuleCut.Start, at the body's rebuild).</summary>
    public static void ClearStale(ConstructWorld world, Construct c, int plate)
    {
        if (ConstructConfig.Get(c.Parts[plate], c.DefOf(plate), CfgKey.ModWeld, 0) != 0 && !Busy(world, c, plate))
            SetWeld(world, c, plate, 0);
    }

    /// <summary>Server, every tick: modules held at their seats are posed in their ship's frame; every <see cref="StepEvery"/> ticks the jobs move on.</summary>
    public static void Step(ConstructWorld world)
    {
        if (!ByWorld.TryGetValue(world, out var wj) || wj.Jobs.Count == 0)
            return;
        for (var k = 0; k < wj.Jobs.Count; k++)
        {
            var j = wj.Jobs[k];
            if (j.Phase is Phase.Weld or Phase.WeldOff && world.Get(j.Ship) is { } c && world.Get(j.Module) is { } m && m.HeldBy == c.Id)
                Hold(c, m, j);
        }
        if (++wj.Ticks < StepEvery)
            return;
        wj.Ticks = 0;
        foreach (var j in wj.Jobs)
            if (world.Get(j.Ship) is { } jc)
                jc.ModFitWatts = 0; // (each working job adds its fitter's draw again)
        for (var k = wj.Jobs.Count - 1; k >= 0; k--)
            if (Look(world, wj.Jobs[k]))
                wj.Jobs.RemoveAt(k);
    }

    /// <summary>The module at its seat in the ship's frame (a ship in flight carries it).</summary>
    private static void Hold(Construct c, Construct m, Job j)
    {
        m.Pos = m.PrevPos = m.NetPos = m.NetFromPos = c.ToWorld(j.Rel);
        m.Rot = m.PrevRot = m.NetRot = m.NetFromRot = Quaternion.Normalize(c.Rot * Rot24.Quat(j.Turn));
        m.Vel = c.Vel;
        m.AngVel = Vector3.Zero;
        m.Wake();
    }

    /// <summary>One look at a job: true when it is over.</summary>
    private static bool Look(ConstructWorld world, Job j)
    {
        var c = world.Get(j.Ship);
        if (c == null || j.Plate >= c.PartCount || c.Parts[j.Plate].Def != j.PlateDef || c.Parts[j.Plate].Broken)
            return End(world, j, c, "its socket went");
        if (c.Dirty)
            c.Rebuild();
        var dt = StepEvery / (float)World.TickRate;
        switch (j.Phase)
        {
            case Phase.Carry or Phase.CarryOff:
            {
                if (world.Get(j.Module) is not { } m)
                    return End(world, j, c, "the module went");
                if (ConstructLifters.CarryingBody(world, m.Id) != null)
                    return false; // (still on its way)
                if (m.HeldBy != c.Id)
                    return End(world, j, c, "the fitter let go of it");
                j.Phase = j.Phase == Phase.Carry ? Phase.Weld : Phase.WeldOff;
                j.Seconds = world.Creative && !world.TimedMachines ? 0 : Perimeter(c.DefOf(j.Plate).ModW, c.DefOf(j.Plate).ModL) / ModuleSocketTable.Current.FitterS.Weld;
                j.Done = 0;
                SetWeld(world, c, j.Plate, MathF.Max(0.1f, j.Seconds));
                Hold(c, m, j);
                world.Events.Add(FormattableString.Invariant($"construct {c.Id}: the {j.Fitter} welds module body {m.Id} on socket part {j.Plate}: {j.Seconds:0} s"));
                return false;
            }
            case Phase.Weld or Phase.WeldOff:
            {
                if (world.Get(j.Module) is not { } m || m.HeldBy != c.Id)
                    return End(world, j, c, "the module went");
                if (!Burn(world, c, j, dt))
                    return false;
                var si = ModuleSockets.SocketOfPlate(c, j.Plate);
                if (si < 0 || c.ModSockets[si].Module >= 0)
                    return End(world, j, c, "the socket is taken");
                var plug = -1;
                foreach (var mm in m.Modules)
                    if (mm.Socket < 0 && !mm.OnStand)
                    {
                        plug = mm.Plug;
                        break;
                    }
                if (m.Dirty)
                    m.Rebuild();
                if (plug < 0 && m.Modules.Length > 0)
                    plug = m.Modules[0].Plug;
                if (plug < 0)
                    return End(world, j, c, "the module came apart");
                if (world.CanMerge(c, m) is { } merge)
                    return End(world, j, c, merge);
                var plateDef = c.DefOf(j.Plate);
                var plugDef = m.DefOf(plug);
                m.HeldBy = 0;
                m.Frozen = false;
                var plan = new JoinPlan { Other = m, PartA = j.Plate, SockA = plateDef.ModSocket, PartB = plug, SockB = plugDef.ModSocket, Turn = j.Turn, Offset = j.Rel };
                var from = m.Id;
                var parts = m.PartCount;
                if (!world.Merge(c, plan))
                {
                    ConstructEdits.Unfreeze(world, m);
                    return End(world, j, c, "it can't be fitted");
                }
                j.Module = 0;
                world.Events.Add($"construct {c.Id}: the {j.Fitter} welded module construct {from} ({parts} parts) onto socket part {j.Plate}");
                return End(world, j, c, null);
            }
            case Phase.Cut:
            {
                if (!Burn(world, c, j, dt))
                    return false;
                var si = ModuleSockets.SocketOfPlate(c, j.Plate);
                if (si < 0 || c.ModSockets[si].Module < 0)
                    return End(world, j, c, "nothing is fitted there now");
                var s = c.ModSockets[si];
                var mod = c.Modules[s.Module];
                if (mod.Bridged)
                    return End(world, j, c, "that module is welded to more than its socket");
                var stand = FreeStand(c, s.W, s.L, -1);
                if (stand < 0)
                    return End(world, j, c, "no free Module Stand of its size");
                var of = c.ModuleOf!;
                var take = new bool[c.Modules.Length];
                take[s.Module] = true;
                for (var more = true; more;)
                {
                    more = false;
                    for (var k = 0; k < c.Modules.Length; k++)
                        if (!take[k] && ModuleSockets.PlateOf(c, c.Modules[k].Plug) is var pl and >= 0 && of[pl] >= 0 && take[of[pl]])
                            take[k] = more = true;
                }
                var marks = new bool[c.PartCount];
                var before = 0;
                var count = 0;
                for (var q = 0; q < c.PartCount; q++)
                    if (of[q] >= 0 && take[of[q]])
                    {
                        if (c.DefOf(q).Role == PartRole.Seat && c.Parts[q].SitterPlus1 != 0)
                            return End(world, j, c, "someone is sitting in it");
                        marks[q] = true;
                        count++;
                        if (q < stand)
                            before++;
                    }
                if (count >= c.PartCount)
                    return End(world, j, c, "that's the whole construct");
                SetWeld(world, c, j.Plate, 0);
                if (world.CutOut(c, marks, 255) is not { } piece)
                    return End(world, j, c, "the world is full");
                c.Rebuild();
                piece.Rebuild();
                stand -= before; // (the stand's index after the cut: parts before it went)
                var standSi = ModuleSockets.SocketOfPlate(c, stand);
                if (standSi < 0 || piece.Modules.Length == 0)
                {
                    piece.Wake();
                    return End(world, j, c, "the stand moved");
                }
                var plug = piece.Modules[0].Plug;
                foreach (var pm in piece.Modules)
                    if (ModuleSockets.PlateOf(piece, pm.Plug) < 0)
                    {
                        plug = pm.Plug; // (the outer module: its plug is on no plate of the piece)
                        break;
                    }
                if (ConstructEdits.SeatPose(c, c.ModSockets[standSi], piece, plug, out var turn, out var rel, out var rot, out var pos) is { } noTurn)
                {
                    piece.Wake();
                    return End(world, j, c, noTurn);
                }
                piece.Frozen = true;
                if (!ConstructLifters.CarryBody(world, c, -1, piece, pos, rot, out var who, out var why, fitter: true))
                {
                    piece.Frozen = false;
                    piece.Wake();
                    return End(world, j, c, why);
                }
                (j.Module, j.Plate, j.PlateDef, j.Turn, j.Rel, j.Phase, j.Fitter, j.Done, j.From) = (piece.Id, stand, c.Parts[stand].Def, turn, rel, Phase.CarryOff, who, 0, -1);
                world.Events.Add($"construct {c.Id}: the {who} cut module {piece.Id} free and carries it to the stand part {stand}");
                return false;
            }
        }
        return End(world, j, c, "no such phase");
    }

    /// <summary>The weld or cut runs on while the ship has power for it: true when it is done.</summary>
    private static bool Burn(ConstructWorld world, Construct c, Job j, float dt)
    {
        if (j.Done >= j.Seconds)
            return true;
        if (!c.Electrical || !c.PowerOn)
        {
            if (!j.Waiting)
                world.Events.Add(FormattableString.Invariant($"construct {c.Id}: the {j.Fitter}'s {(j.Phase == Phase.Cut ? "cut" : "weld")} waits: it needs {ModuleSocketTable.Current.FitterS.Power / 1000:0.#} kW"));
            j.Waiting = true;
            return false;
        }
        j.Waiting = false;
        c.ModFitWatts += ModuleSocketTable.Current.FitterS.Power; // (ConstructPower counts it with the circuit's draw; cleared each look before the jobs move on)
        j.Done += dt;
        return j.Done >= j.Seconds;
    }

    private static bool End(ConstructWorld world, Job j, Construct? c, string? why)
    {
        if (c != null)
            c.ModFitWatts = 0; // (another job on the same ship adds its draw again at the next look)
        if (c != null && j.Plate < c.PartCount)
            SetWeld(world, c, j.Plate, 0);
        if (j.Module != 0 && world.Get(j.Module) is { } m && c != null && m.HeldBy == c.Id && ConstructLifters.CarryingBody(world, m.Id) == null)
        {
            m.HeldBy = 0;
            ConstructEdits.Unfreeze(world, m); // (a job that stops leaves the module where it is, loose)
        }
        if (why != null)
            Console.WriteLine($"[modules] construct {j.Ship}: the {j.Fitter}'s job on part {j.Plate} stopped: {why}");
        return true;
    }

    private static readonly Dictionary<(Construct, int), double> FirstSeen = new(); // (as ModuleCut's: at most MaxSeen)
    private const int MaxSeen = 64;

    /// <summary>Any peer: seconds since a fitter's weld or cut on this plate was first seen here (its <c>ModWeld</c> setting not 0), else 0.</summary>
    public static double Seen(Construct c, int plate)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        var key = (c, plate);
        if (ConstructConfig.Get(c.Parts[plate], c.DefOf(plate), CfgKey.ModWeld, 0) == 0)
        {
            FirstSeen.Remove(key);
            return 0;
        }
        if (!FirstSeen.TryGetValue(key, out var t0))
        {
            if (FirstSeen.Count >= MaxSeen)
                FirstSeen.Clear();
            FirstSeen[key] = t0 = now;
        }
        return now - t0;
    }

    /// <summary>The look line's words while a fitter works on plate <paramref name="plate"/> (any peer), else null.</summary>
    public static string? LookText(Construct c, int plate)
    {
        var total = ConstructConfig.Get(c.Parts[plate], c.DefOf(plate), CfgKey.ModWeld, 0);
        if (total == 0)
            return null;
        var f = Math.Clamp(Seen(c, plate) / MathF.Abs(total), 0, 0.99) * 100;
        return total > 0 ? FormattableString.Invariant($"a Module Fitter welds a module on, {f:0}%") : FormattableString.Invariant($"a Module Fitter cuts its module's seam, {f:0}%");
    }
}
