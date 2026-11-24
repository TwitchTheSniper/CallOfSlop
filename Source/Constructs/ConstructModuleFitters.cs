using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Module Fitters (docs/sandbox/design/module-sockets.md 7, phase 4): a lifter whose part says <c>"fitter": true</c> (the Module
/// Fitter S, LifterParts.ModuleFitter) fits modules the hand can't (past 2x2 or the physics gun's lift, or lying past the hand's
/// pull) to the sockets of its own construct or of another construct within its reach (a dock's fitter and a ship pulled in
/// beside it), and takes them off onto a free Module Stand of their size (on the dock or on the ship).
/// <para>
/// Across constructs (the user, 2026-11-20: a ship has no fitting arm of its own, it pulls into a dock): the fitter's construct is
/// the job's rig, the socket's the job's ship. The rig powers the weld and cut. The two must keep still against each other: a job
/// starts only while they drift apart slower than <see cref="MaxDrift"/>, and stops when the socket has moved more than
/// <see cref="MaxSlip"/> in the rig's frame since it started (the ship flew off mid-job). Only a rig the player's faction shares, or
/// nobody's, is used.
/// </para>
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
    public const float MaxDrift = 0.3f; // m/s: a ship and another construct's fitter must be this still against each other to start a job
    public const float MaxSpin = 0.03f; // rad/s, the same for their turn
    public const float MaxSlip = 0.5f; // m: how far the socket may move in the rig's frame during a job before it stops

    public enum Phase : byte { Carry, Weld, Cut, CarryOff, WeldOff }

    /// <summary>A part a job holds on to: its index, re-found by its def, place and turn when the body is renumbered (a cut, a removal).</summary>
    public struct PartRef
    {
        public int Part;
        public ushort Def;
        public Vector3 Pos;
        public byte Rot;

        public static PartRef Of(Construct c, int part) => part < 0 || part >= c.PartCount ? new PartRef { Part = -1 } : new PartRef { Part = part, Def = c.Parts[part].Def, Pos = c.Parts[part].Pos, Rot = c.Parts[part].Rot };

        /// <summary>Is it still there (re-found if its index moved)? Cheap when it didn't; one pass over the parts when it did.</summary>
        public bool Find(Construct c)
        {
            if (Part < 0)
                return false;
            if (Part < c.PartCount && Is(c, Part))
                return true;
            for (var i = 0; i < c.PartCount; i++)
                if (Is(c, i))
                {
                    Part = i;
                    return true;
                }
            Part = -1;
            return false;
        }

        private readonly bool Is(Construct c, int i) => c.Parts[i].Def == Def && c.Parts[i].Rot == Rot && Vector3.DistanceSquared(c.Parts[i].Pos, Pos) < 1e-6f;
    }

    public sealed class Job
    {
        public ushort Ship, Module; // the construct the socket is on, the module's body (while it is one)
        public ushort Rig; // the construct the fitter is on (the ship itself, or a dock beside it)
        public ushort StandBody; // a take-off: the construct its stand is on (0 otherwise)
        public Vector3 RigAt; // the socket's face in the rig's frame when the job (or its carry off) started: the ship mustn't slip away
        public PartRef Plate; // the socket's plate (a stand's, for a take-off's last weld)
        public PartRef From; // a take-off: the socket it came off (Part -1 otherwise)
        public PartRef Stand; // a take-off: the stand reserved for it
        public PartRef Fitter; // the Module Fitter reserved for it
        public byte Turn;
        public Vector3 Rel; // the module body's offset in the ship's frame at its seat
        public Phase Phase;
        public float Seconds, Done;
        public string FitterName = "Module Fitter";
        public bool Waiting, Blocked;
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
                r = MathF.Max(r, ld.ReachMax + ld.Travel + ld.Up);
        return r;
    }

    /// <summary>Is a fitter's job on plate <paramref name="plate"/> of <paramref name="c"/> (as its socket, the socket it left or its stand), or on the module body <paramref name="body"/>?</summary>
    public static bool Busy(ConstructWorld world, Construct c, int plate, ushort body = 0)
    {
        var wj = ByWorld.GetOrCreateValue(world);
        foreach (var j in wj.Jobs)
            if (j.Ship == c.Id && (j.Plate.Part == plate || j.From.Part == plate) || j.StandBody == c.Id && j.Stand.Part == plate || body != 0 && j.Module == body)
                return true;
        return false;
    }

    /// <summary>Does a fitter's job hold body <paramref name="body"/> (carrying or welding it)? A save keeps such a body loose (WorldSaveBodies.cs).</summary>
    public static bool Holds(ConstructWorld world, ushort body)
    {
        if (!ByWorld.TryGetValue(world, out var wj))
            return false;
        foreach (var j in wj.Jobs)
            if (j.Module == body)
                return true;
        return false;
    }

    /// <summary>
    /// An idle, working, powered Module Fitter of <paramref name="c"/> that no job has reserved, whose reach holds both points (world),
    /// and whose payload takes <paramref name="kg"/>: its part, or -1 and why none.
    /// </summary>
    private static int IdleFitter(ConstructWorld world, WorldJobs wj, Construct c, Vector3 a, Vector3 b, float kg, out string why)
    {
        why = "no Module Fitter on this construct";
        if (!world.LiftSeen.TryGetValue(c.Id, out var lb))
            return -1;
        int busy = 0, far = 0, heavy = 0;
        for (var i = 0; i < lb.N; i++)
        {
            var l = lb.Lifts[i];
            if (l.Part >= c.PartCount || ConstructLifters.DefOf(c, l.Part) is not { Fitter: true } ld || c.Parts[l.Part].Broken)
                continue;
            var taken = l.State != LiftState.Idle || l.CarryBody != 0 || !ConstructRecycler.Powered(world, c, l.Part);
            foreach (var j in wj.Jobs)
                taken |= j.Rig == c.Id && j.Fitter.Part == l.Part;
            if (taken)
            {
                busy++;
                continue;
            }
            if (kg > ld.Payload)
            {
                heavy++;
                continue;
            }
            ConstructLifters.Frame(c, l.Part, ld, out _, out var lr, out var shoulder);
            if (!ConstructLifters.InReach(ld, shoulder, lr, a, ConstructLifters.SafeLift, out _) || !ConstructLifters.InReach(ld, shoulder, lr, b, ConstructLifters.SafeLift, out _))
            {
                far++;
                continue;
            }
            return l.Part;
        }
        why = heavy > 0 ? $"too heavy for the Module Fitter: {kg / 1000f:0.#} t" : far > 0 ? "no Module Fitter reaches both the module and the socket" : busy > 0 ? "every Module Fitter is busy (or has no power)" : why;
        return -1;
    }

    private static readonly List<Construct> RigBuf = new(8); // (F on a socket only: reused, not per tick)

    /// <summary>
    /// The constructs whose Module Fitter might work on a socket at <paramref name="at"/> (world) of <paramref name="c"/>: <paramref name="c"/>
    /// itself first when it carries one, then any other construct with a working fitter whose reach could take in the point (a dock
    /// beside a ship that pulled in) and that player <paramref name="player"/>'s faction shares, or nobody's. A shared list: read it before calling again.
    /// </summary>
    private static List<Construct> Rigs(ConstructWorld world, Construct c, Vector3 at, byte player)
    {
        RigBuf.Clear();
        if (Reach(world, c) > 0)
            RigBuf.Add(c);
        foreach (var o in world.Bodies)
        {
            if (o == c || o.HeldBy != 0 || o.CarriedBy != 0 || o.Owner != 255 && !Factions.Shares(o.Owner, player))
                continue;
            var r = Reach(world, o);
            if (r > 0 && Vector3.Distance(o.ToWorld(o.Com), at) <= o.Radius + r)
                RigBuf.Add(o);
        }
        return RigBuf;
    }

    /// <summary>
    /// The largest reach (m) of a Module Fitter that might work on a socket at <paramref name="at"/> of <paramref name="c"/>, on
    /// <paramref name="c"/> or on a construct beside it (<see cref="Rigs"/>); 0 when there is none.
    /// </summary>
    public static float ReachAt(ConstructWorld world, Construct c, Vector3 at, byte player)
    {
        var r = 0f;
        foreach (var rig in Rigs(world, c, at, player))
            r = MathF.Max(r, Reach(world, rig));
        return r;
    }

    /// <summary>Why ship <paramref name="c"/> and the fitter's construct <paramref name="rig"/> can't work together now (one moves against the other), or null.</summary>
    private static string? Drifts(Construct c, Construct rig) =>
        c == rig || Vector3.Distance(c.Vel, rig.Vel) <= MaxDrift && (c.AngVel - rig.AngVel).Length() <= MaxSpin ? null
            : "hold still at the Module Fitter: the ship and the fitter's construct move against each other";

    /// <summary>Where the socket on plate <paramref name="plate"/> of <paramref name="c"/> faces out, in the world.</summary>
    private static Vector3 FaceOf(Construct c, int plate)
    {
        c.SocketAt(plate, c.DefOf(plate).ModSocket, out var at, out _);
        return c.SegToWorld(c.Parts[plate].Seg, at); // (a plate welded on freeform: its numbers are in its weld's frame)
    }

    /// <summary>
    /// Server, before anything is cut or moved: could a Module Fitter on <paramref name="c"/> or beside it carry a module of
    /// <paramref name="kg"/> from <paramref name="from"/> to <paramref name="to"/> (world) now? Null, or why not.
    /// </summary>
    public static string? CanFit(ConstructWorld world, byte player, Construct c, Vector3 from, Vector3 to, float kg) // (from, to: MiddleOf, now and at the seat)
    {
        var wj = ByWorld.GetOrCreateValue(world);
        if (wj.Jobs.Count >= MaxJobs)
            return "every Module Fitter in the world is busy";
        string? why = null;
        foreach (var rig in Rigs(world, c, to, player))
        {
            if (Drifts(c, rig) is { } drift)
                why ??= drift;
            else if (IdleFitter(world, wj, rig, from, to, kg, out var no) < 0)
                why ??= no;
            else
                return null;
        }
        return why ?? "no Module Fitter on this construct or beside it";
    }

    /// <summary>
    /// Server: a Module Fitter on <paramref name="c"/> or on a construct beside it carries module body <paramref name="body"/> (whose
    /// plug is <paramref name="plug"/>) to socket <paramref name="si"/> of <paramref name="c"/> at the seat <paramref name="turn"/>,
    /// <paramref name="rel"/>, then welds it on. Null when started.
    /// </summary>
    public static string? Fit(ConstructWorld world, byte player, Construct c, int si, Construct body, int plug, byte turn, Vector3 rel, Quaternion rot, Vector3 pos)
    {
        var wj = ByWorld.GetOrCreateValue(world);
        if (wj.Jobs.Count >= MaxJobs)
            return "every Module Fitter in the world is busy";
        var s = c.ModSockets[si];
        if (Busy(world, c, s.Plate, body.Id))
            return "a Module Fitter is already at work on it";
        if (body.Dirty)
            body.Rebuild();
        // (the module's middle, now and at its seat: the body's origin may be metres off, a module cut off a construct keeps that construct's;
        // the same points as the early check before a cut off a stand, MiddleOf)
        var mid = MiddleOf(body, plug);
        var from = body.ToWorld(mid);
        var seat = pos + Vector3.Transform(mid, rot);
        string? why = null;
        foreach (var rig in Rigs(world, c, seat, player))
        {
            if (Drifts(c, rig) is { } drift)
            {
                why ??= drift;
                continue;
            }
            var fitter = IdleFitter(world, wj, rig, from, seat, body.Mass, out var no);
            if (fitter < 0 || !ConstructLifters.CarryBody(world, rig, -1, body, pos, rot, out var who, out no, fitter: true, onlyPart: fitter))
            {
                why ??= no;
                continue;
            }
            body.HeldNear = rig == c ? (ushort)0 : c.Id; // (held by the dock's fitter for the ship: no contact with the ship it goes into)
            wj.Jobs.Add(new Job { Ship = c.Id, Rig = rig.Id, Module = body.Id, Plate = PartRef.Of(c, s.Plate), From = PartRef.Of(c, -1), Stand = PartRef.Of(c, -1), Fitter = PartRef.Of(rig, fitter),
                Turn = turn, Rel = rel, Phase = Phase.Carry, FitterName = who, RigAt = rig.ToLocal(FaceOf(c, s.Plate)) });
            world.Events.Add(rig == c ? $"construct {c.Id}: the {who} carries module body {body.Id} to socket part {s.Plate}"
                : $"construct {c.Id}: the {who} of construct {rig.Id} carries module body {body.Id} to socket part {s.Plate}");
            return null;
        }
        return why ?? "no Module Fitter on this construct or beside it";
    }

    /// <summary>
    /// Server: a Module Fitter on <paramref name="c"/> or on a construct beside it cuts the seam of the module on socket
    /// <paramref name="si"/> and sets it on a free Module Stand of its size, on the fitter's construct (first) or on <paramref name="c"/>.
    /// The fitter and the stand are reserved before the cut starts. Null when started.
    /// </summary>
    public static string? TakeOff(ConstructWorld world, byte player, Construct c, int si)
    {
        var wj = ByWorld.GetOrCreateValue(world);
        if (wj.Jobs.Count >= MaxJobs)
            return "every Module Fitter in the world is busy";
        var s = c.ModSockets[si];
        if (Busy(world, c, s.Plate))
            return "a Module Fitter is already at work on it";
        var face = FaceOf(c, s.Plate);
        var kg = s.Module >= 0 ? c.Modules[s.Module].Mass : 0;
        string? why = null;
        var stands = 0;
        foreach (var rig in Rigs(world, c, face, player))
        {
            if (Drifts(c, rig) is { } drift)
            {
                why ??= drift;
                continue;
            }
            for (var pass = 0; pass < 2; pass++)
            {
                var sb = pass == 0 ? rig : c; // (a dock's own stand first: that is where a ship's module is stored)
                if (pass == 1 && sb == rig)
                    break;
                foreach (var st in sb.ModSockets)
                {
                    if (!st.Stand || st.Module >= 0 || st.W != s.W || st.L != s.L || st.Round != s.Round || st.InTheWay >= 0 || Busy(world, sb, st.Plate))
                        continue;
                    stands++;
                    var fitter = IdleFitter(world, wj, rig, face, FaceOf(sb, st.Plate), kg, out var no);
                    if (fitter < 0)
                    {
                        why ??= no;
                        continue;
                    }
                    var t = ModuleSocketTable.Current;
                    var job = new Job { Ship = c.Id, Rig = rig.Id, StandBody = sb.Id, Plate = PartRef.Of(c, s.Plate), From = PartRef.Of(c, s.Plate), Stand = PartRef.Of(sb, st.Plate),
                        Fitter = PartRef.Of(rig, fitter), Phase = Phase.Cut, FitterName = rig.DefOf(fitter).Name, RigAt = rig.ToLocal(face),
                        Seconds = world.Creative && !world.TimedMachines ? 0 : Perimeter(s.Wm, s.Lm) / t.FitterS.Cut };
                    wj.Jobs.Add(job);
                    SetWeld(world, c, s.Plate, -MathF.Max(0.1f, job.Seconds));
                    world.Events.Add(FormattableString.Invariant($"construct {c.Id}: the {job.FitterName}{(rig == c ? "" : $" of construct {rig.Id}")} cuts the seam of the module on socket part {s.Plate}, for the stand part {st.Plate} of construct {sb.Id}: {job.Seconds:0} s"));
                    return null;
                }
            }
        }
        if (stands == 0 && why == null)
            return $"no free {ModuleSocketTable.SizeText(s)} Module Stand on this construct or the fitter's to set it on (a fitter takes a module off onto a stand)";
        return why ?? "no Module Fitter on this construct or beside it";
    }

    private static float Perimeter(float w, float l) => 2f * (w + l);

    /// <summary>The middle of the module whose plug is <paramref name="plug"/>, in <paramref name="body"/>'s frame (its bounds' centre; the body's centre of mass when the plug is no module's).</summary>
    public static Vector3 MiddleOf(Construct body, int plug)
    {
        if (body.Dirty)
            body.Rebuild();
        return ModuleSockets.ModuleOfPart(body, plug) is var mi and >= 0 ? (body.Modules[mi].Min + body.Modules[mi].Max) * 0.5f : body.ToLocal(body.WorldCom);
    }

    private static void SetWeld(ConstructWorld world, Construct c, int plate, float seconds)
    {
        if (plate < 0 || plate >= c.PartCount || c.DefOf(plate).ModPart != ModPart.Socket)
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
            if (j.Phase is Phase.Weld or Phase.WeldOff && world.Get(j.Ship) is { } c && world.Get(j.Module) is { } m && m.HeldBy == j.Rig)
                Hold(c, m, j);
        }
        if (++wj.Ticks < StepEvery)
            return;
        wj.Ticks = 0;
        foreach (var j in wj.Jobs)
            if (world.Get(j.Rig) is { } jr)
                jr.ModFitWatts = 0; // (each working job adds its fitter's draw again, on the fitter's construct)
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

    /// <summary>Someone or something (a player, a loose body) where the module goes: it waits for them (the hand's own check, SocketWeld.Overlap).</summary>
    private static string? Blocked(ConstructWorld world, Construct c, Construct m, Job j) =>
        SocketWeld.Overlap(world, null, m, c, c.ToWorld(j.Rel), Quaternion.Normalize(c.Rot * Rot24.Quat(j.Turn)), world.PlayerSpots.AsSpan(0, world.PlayerSpotCount));

    /// <summary>One look at a job: true when it is over.</summary>
    private static bool Look(ConstructWorld world, Job j)
    {
        var c = world.Get(j.Ship);
        if (c == null || !j.Plate.Find(c) || c.Parts[j.Plate.Part].Broken)
            return End(world, j, c, "its socket went");
        var rig = world.Get(j.Rig);
        if (rig == null || !j.Fitter.Find(rig) || rig.Parts[j.Fitter.Part].Broken)
            return End(world, j, c, "its Module Fitter is wrecked or gone");
        var sb = j.Stand.Part >= 0 ? world.Get(j.StandBody) : null;
        if (j.Stand.Part >= 0 && (sb == null || !j.Stand.Find(sb)))
            return End(world, j, c, "its stand went");
        if (c.Dirty)
            c.Rebuild();
        if (rig != c)
        {
            if (rig.Dirty)
                rig.Rebuild();
            if (Vector3.Distance(rig.ToLocal(FaceOf(c, j.Plate.Part)), j.RigAt) > MaxSlip)
                return End(world, j, c, $"the ship moved away from the {j.FitterName} of construct {rig.Id}");
        }
        var dt = StepEvery / (float)World.TickRate;
        switch (j.Phase)
        {
            case Phase.Carry or Phase.CarryOff:
            {
                if (world.Get(j.Module) is not { } m)
                    return End(world, j, c, "the module went");
                if (ConstructLifters.CarryingBody(world, m.Id) != null)
                    return false; // (still on its way)
                if (m.HeldBy != j.Rig)
                    return End(world, j, c, "the fitter let go of it");
                Hold(c, m, j);
                if (Wait(world, j, c, m))
                    return false;
                j.Phase = j.Phase == Phase.Carry ? Phase.Weld : Phase.WeldOff;
                j.Seconds = world.Creative && !world.TimedMachines ? 0 : Perimeter(c.DefOf(j.Plate.Part).ModW * c.DefOf(j.Plate.Part).ModUnit, c.DefOf(j.Plate.Part).ModL * c.DefOf(j.Plate.Part).ModUnit) / ModuleSocketTable.Current.FitterS.Weld;
                j.Done = 0;
                SetWeld(world, c, j.Plate.Part, MathF.Max(0.1f, j.Seconds));
                world.Events.Add(FormattableString.Invariant($"construct {c.Id}: the {j.FitterName} welds module body {m.Id} on socket part {j.Plate.Part}: {j.Seconds:0} s"));
                return false;
            }
            case Phase.Weld or Phase.WeldOff:
            {
                if (world.Get(j.Module) is not { } m || m.HeldBy != j.Rig)
                    return End(world, j, c, "the module went");
                if (Wait(world, j, c, m) || !Burn(world, rig, j, dt))
                    return false;
                var si = ModuleSockets.SocketOfPlate(c, j.Plate.Part);
                if (si < 0 || c.ModSockets[si].Module >= 0)
                    return End(world, j, c, "the socket is taken");
                if (m.Dirty)
                    m.Rebuild();
                var plug = OuterPlug(m);
                if (plug < 0)
                    return End(world, j, c, "the module came apart");
                if (world.CanMerge(c, m) is { } merge)
                    return End(world, j, c, merge);
                var plateDef = c.DefOf(j.Plate.Part);
                var plugDef = m.DefOf(plug);
                m.HeldBy = 0;
                m.HeldNear = 0;
                m.Frozen = false;
                var plan = new JoinPlan { Other = m, PartA = j.Plate.Part, SockA = plateDef.ModSocket, PartB = plug, SockB = plugDef.ModSocket, Turn = j.Turn, Offset = j.Rel };
                ModuleSockets.PlanOnPlate(c, j.Plate.Part, ref plan);
                var from = m.Id;
                var parts = m.PartCount;
                if (!world.Merge(c, plan))
                {
                    ConstructEdits.Unfreeze(world, m);
                    return End(world, j, c, "it can't be fitted");
                }
                j.Module = 0;
                world.Events.Add($"construct {c.Id}: the {j.FitterName} welded module construct {from} ({parts} parts) onto socket part {j.Plate.Part}");
                return End(world, j, c, null);
            }
            case Phase.Cut:
            {
                if (!Burn(world, rig, j, dt))
                    return false;
                var si = ModuleSockets.SocketOfPlate(c, j.Plate.Part);
                if (si < 0 || c.ModSockets[si].Module < 0)
                    return End(world, j, c, "nothing is fitted there now");
                var s = c.ModSockets[si];
                var mod = c.Modules[s.Module];
                if (mod.Bridged)
                    return End(world, j, c, "that module is welded to more than its socket");
                if (sb!.Dirty)
                    sb.Rebuild();
                var standSi = ModuleSockets.SocketOfPlate(sb, j.Stand.Part);
                if (standSi < 0 || sb.ModSockets[standSi].Module >= 0 || sb.ModSockets[standSi].InTheWay >= 0)
                    return End(world, j, c, "its stand is taken");
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
                var count = 0;
                for (var q = 0; q < c.PartCount; q++)
                    if (of[q] >= 0 && take[of[q]])
                    {
                        if (c.DefOf(q).Role == PartRole.Seat && c.Parts[q].SitterPlus1 != 0)
                            return End(world, j, c, "someone is sitting in it");
                        marks[q] = true;
                        count++;
                    }
                if (count >= c.PartCount)
                    return End(world, j, c, "that's the whole construct");
                SetWeld(world, c, j.Plate.Part, 0);
                if (world.CutOut(c, marks, 255) is not { } piece)
                    return End(world, j, c, "the world is full");
                c.Rebuild();
                piece.Rebuild();
                // (the cut renumbered the ship: every part the job holds is found again by its place, on its own construct)
                if (sb.Dirty)
                    sb.Rebuild();
                if (rig.Dirty)
                    rig.Rebuild();
                if (!j.Stand.Find(sb) || !j.Fitter.Find(rig) || ModuleSockets.SocketOfPlate(sb, j.Stand.Part) is var standAfter && standAfter < 0)
                {
                    piece.Wake();
                    return End(world, j, c, "its stand or fitter went with the module");
                }
                var plug = OuterPlug(piece);
                if (plug < 0)
                {
                    piece.Wake();
                    return End(world, j, c, "the module came apart");
                }
                if (ConstructEdits.SeatPose(sb, sb.ModSockets[standAfter], piece, plug, out var turn, out var rel, out var rot, out var pos) is { } noTurn)
                {
                    piece.Wake();
                    return End(world, j, c, noTurn);
                }
                piece.Frozen = true;
                if (!ConstructLifters.CarryBody(world, rig, -1, piece, pos, rot, out var who, out var why, fitter: true, onlyPart: j.Fitter.Part))
                {
                    piece.Frozen = false;
                    piece.Wake();
                    return End(world, j, c, why);
                }
                // (from here the job's ship is the stand's construct: the module is carried to it and welded there)
                piece.HeldNear = rig == c ? (ushort)0 : c.Id; // (lifted out of the ship's socket by the dock's fitter: no contact with the ship on the way)
                (j.Module, j.Ship, j.Plate, j.Turn, j.Rel, j.Phase, j.FitterName, j.Done) = (piece.Id, sb.Id, j.Stand, turn, rel, Phase.CarryOff, who, 0);
                j.From = PartRef.Of(c, -1);
                j.RigAt = rig.ToLocal(FaceOf(sb, j.Stand.Part));
                world.Events.Add($"construct {c.Id}: the {who} cut module {piece.Id} free and carries it to the stand part {j.Stand.Part} of construct {sb.Id}");
                return false;
            }
        }
        return End(world, j, c, "no such phase");
    }

    /// <summary>The outer module's plug of a loose module body (its plug on no plate of the body), or -1.</summary>
    private static int OuterPlug(Construct m)
    {
        foreach (var pm in m.Modules)
            if (ModuleSockets.PlateOf(m, pm.Plug) < 0)
                return pm.Plug;
        return -1;
    }

    /// <summary>True while something is where the module goes (logged once a block): the job waits, the module held clear of nothing new.</summary>
    private static bool Wait(ConstructWorld world, Job j, Construct c, Construct m)
    {
        var why = Blocked(world, c, m, j);
        if (why != null && !j.Blocked)
            world.Events.Add($"construct {c.Id}: the {j.FitterName} waits at socket part {j.Plate.Part}: {why}");
        j.Blocked = why != null;
        return j.Blocked;
    }

    /// <summary>The weld or cut runs on while the ship has power for it: true when it is done.</summary>
    private static bool Burn(ConstructWorld world, Construct c, Job j, float dt)
    {
        if (j.Done >= j.Seconds)
            return true;
        if (!c.Electrical || !c.PowerOn)
        {
            if (!j.Waiting)
                world.Events.Add(FormattableString.Invariant($"construct {c.Id}: the {j.FitterName}'s {(j.Phase == Phase.Cut ? "cut" : "weld")} waits: it needs {ModuleSocketTable.Current.FitterS.Power / 1000:0.#} kW"));
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
        var rig = world.Get(j.Rig);
        if (rig != null)
            rig.ModFitWatts = 0; // (another job on the same fitter's construct adds its draw again at the next look)
        if (c != null && j.Plate.Find(c))
            SetWeld(world, c, j.Plate.Part, 0);
        if (c != null && j.From.Part >= 0 && j.From.Find(c))
            SetWeld(world, c, j.From.Part, 0);
        if (j.Module != 0 && world.Get(j.Module) is { } held)
            held.HeldNear = 0;
        if (j.Module != 0 && world.Get(j.Module) is { } m && m.HeldBy == j.Rig && ConstructLifters.CarryingBody(world, m.Id) == null)
        {
            m.HeldBy = 0;
            ConstructEdits.Unfreeze(world, m); // (a job that stops leaves the module where it is, loose)
        }
        if (why != null)
            Console.WriteLine($"[modules] construct {j.Ship}: the {j.FitterName}'s job on part {j.Plate.Part} stopped: {why}");
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
