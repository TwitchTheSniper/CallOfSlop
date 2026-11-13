using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A part file's <c>lifter</c> block (docs/sandbox/design/arms-and-cranes.md, section 3.1): a scripted machine that carries pieces.</summary>
public sealed class LifterDef
{
    public bool Bridge; // "kind": "arm" (a ring round Base) or "bridge" (phase 6)
    public Vector3 Base; // the shoulder, in the part's frame: the reach ring's middle, where the boom is drawn from
    public float ReachMin, ReachMax, Up, Down; // the ring: radii round Base, and how far above and below it
    public float Payload; // kg
    public float PieceAcross, PieceLong; // the largest piece: its larger side across, its length (m)
    public float Speed, Handle; // m/s at the hand; seconds for a pick and a place together
    public float Upper, Fore; // an arm's two links (m): shoulder to elbow, elbow to wrist ("arm")
    public int Tier = 1; // the part's tier (its "tier" key): the drawn half's dressing
    public float Ring; // the slewing ring's top in the part's frame, where the drawn turret stands ("ring"; default 0.66 m under the shoulder)
    public Vector3 Turret = new(0.478f, 0.267f, 0.196f), Weight = new(0.184f, 0.165f, 0.149f), UpperCol = new(0.478f, 0.267f, 0.196f),
        ForeCol = new(0.42f, 0.29f, 0.21f), Joint = new(0.545f, 0.522f, 0.478f), Pin = new(0.94f, 0.47f, 0.09f); // the drawn half's colours ("look")
    public const float Fold = 0.6f; // the elbow never folds the wrist closer to the shoulder than this past the links' difference (no arm through itself)
    public const float Straight = 0.98f; // ...nor reaches past this share of the two links (never stretched straight)
    public float LinkMax => (Upper + Fore) * Straight;
    public float LinkMin => MathF.Abs(Upper - Fore) + Fold;

    public static LifterDef Parse(JsonElement e, string file)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: \"lifter\" is an object");
        foreach (var p in e.EnumerateObject())
            if (p.Name is not ("kind" or "base" or "reach" or "up" or "down" or "payload" or "piece" or "speed" or "handle" or "arm" or "ring" or "look"))
                throw new ConstructPartException($"{file}: lifter.{p.Name}: no such key");
        var kind = e.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
        if (kind is not ("arm" or "bridge"))
            throw new ConstructPartException($"{file}: lifter.kind is \"arm\" or \"bridge\"");
        var d = new LifterDef { Bridge = kind == "bridge" };
        var b = Nums(e, "base", 3, file);
        d.Base = new Vector3(b[0], b[1], b[2]);
        var reach = Nums(e, "reach", 2, file);
        (d.ReachMin, d.ReachMax) = (reach[0], reach[1]);
        d.Up = Num(e, "up", 0, 64, file);
        d.Down = Num(e, "down", 0, 64, file);
        d.Payload = Num(e, "payload", 1, 1e7f, file);
        var piece = Nums(e, "piece", 2, file);
        (d.PieceAcross, d.PieceLong) = (piece[0], piece[1]);
        d.Speed = Num(e, "speed", 0.1f, 50, file);
        d.Handle = Num(e, "handle", 0, 10, file);
        if (!d.Bridge)
        {
            var arm = Nums(e, "arm", 2, file);
            (d.Upper, d.Fore) = (arm[0], arm[1]);
            if (d.Upper < 0.3f || d.Fore < 0.3f || d.LinkMax <= d.LinkMin + 0.5f)
                throw new ConstructPartException($"{file}: lifter.arm is [upper arm, forearm] in m, each 0.3 m or more and near enough alike to bend");
        }
        d.Ring = e.TryGetProperty("ring", out var rg) ? Num(e, "ring", -64, 64, file) : d.Base.Y - 0.66f;
        if (d.Ring > d.Base.Y - 0.2f)
            throw new ConstructPartException($"{file}: lifter.ring (the slewing ring's top) is 0.2 m or more under the shoulder (base)");
        if (e.TryGetProperty("look", out var look))
        {
            if (look.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{file}: lifter.look is an object of colours: turret, weight, upper, fore, joint, pin");
            foreach (var c in look.EnumerateObject())
            {
                var v = ConstructParts.Colour(c.Value, file, "lifter.look." + c.Name);
                switch (c.Name)
                {
                    case "turret": d.Turret = v; break;
                    case "weight": d.Weight = v; break;
                    case "upper": d.UpperCol = v; break;
                    case "fore": d.ForeCol = v; break;
                    case "joint": d.Joint = v; break;
                    case "pin": d.Pin = v; break;
                    default: throw new ConstructPartException($"{file}: lifter.look.{c.Name}: no such key (turret, weight, upper, fore, joint, pin)");
                }
            }
        }
        if (d.ReachMin < 0 || d.ReachMax <= d.ReachMin || d.ReachMax > 128 || d.PieceAcross <= 0 || d.PieceLong < d.PieceAcross || d.PieceLong > 256)
            throw new ConstructPartException($"{file}: lifter: reach is [min, max] with 0 <= min < max <= 128, piece [across, long] with 0 < across <= long <= 256");
        return d;
    }

    private static float Num(JsonElement e, string key, float lo, float hi, string file)
    {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number || v.GetSingle() is var f && (f < lo || f > hi || !float.IsFinite(f)))
            throw new ConstructPartException($"{file}: lifter.{key} is a number from {lo} to {hi}");
        return f;
    }

    private static float[] Nums(JsonElement e, string key, int n, string file)
    {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != n)
            throw new ConstructPartException($"{file}: lifter.{key} is {n} numbers");
        var o = new float[n];
        var i = 0;
        foreach (var x in v.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.Number || !float.IsFinite(x.GetSingle()) || MathF.Abs(x.GetSingle()) > 256)
                throw new ConstructPartException($"{file}: lifter.{key} is {n} numbers");
            o[i++] = x.GetSingle();
        }
        return o;
    }
}

public enum LiftState : byte
{
    Idle, // the hand waits where its last move ended
    Carry, // a piece in the hand, on its way
    Return, // the hand goes back empty
    Stuck, // a piece in the hand nothing takes: it waits with it (a piece loaded in the hand with no hold in reach to take it)
}

/// <summary>What an idle or stuck lifter waits on: its look line (both peers: sent).</summary>
public enum LiftWhy : byte
{
    None,
    NoPower,
    RunOff, // a wire into Run holds it
    Off, // its Works setting keeps it from station work
    NoStation, // no Building Station building in reach
    NoPart, // a station in reach, but no part it can take now (not ready, not in the hold, too heavy or out of reach)
    Holding, // a piece in the hand (from a save) and no hold in reach takes it
    PutBack, // carrying a piece back into a station's hold (from a save)
    Player, // its piece would pin a player against something: the move waits (ConstructLifterPush.cs)
    PlayerStuck, // ...for over 10 s (Alarm)
}

/// <summary>
/// One lifter part's state (ConstructLifters.cs). The first block is what both peers hold; the move is a pure function of
/// <see cref="Age"/> (ticks into it): the path is worked out the same on every peer from From, To, Rise and the def.
/// </summary>
public sealed class CLift
{
    public ushort Body, Part;
    public LiftState State;
    public ushort Def; // the piece in the hand (0: none)
    public uint Paint;
    public Vector3 From, To; // the piece's origin at the pick and at the place, in the lifter part's frame
    public Quaternion FromRot = Quaternion.Identity, ToRot = Quaternion.Identity; // its turn there, in the same frame
    public int Age, Dur; // ticks into the move (server: from the clock at each step), and its length
    public uint Start; // the server tick (the mover clock) the move began
    public int Held; // ticks the move has waited (a player in the way, no power): the path runs on Start + Held
    public uint WaitFrom; // the tick it stopped at while it waits (0: it runs): both peers pose it there (ConstructLifterPush.cs)
    public bool Moved; // a move has been made or heard (else the hand is drawn at its rest)
    public float Rise = ConstructLifters.CarryRise; // the carry height over the higher end, from its Carry height setting when the move began
    public LiftWhy Why; // what it waits on (the look line)
    public ushort Moves; // parts it has fitted since the world loaded (its Moved output)
    public ushort StationBody; // the station it works for in this move (0: none)
    public int Step = -1; // the station's step it carries (-1: none)

    // ---- Server only.
    public int StationPart = -1;
    public StationJob? Job;
    public StationJob.LiftPlan? Plan; // the job's step places when the step was claimed (a restart makes a new one)
    public bool Fitted; // the place happened; Return is the empty hand's way back
    public bool ToHold; // this carry ends in the station's hold, not on the build (a piece loaded in the hand)
    public bool PlayerWait, PowerWait; // what it waits on: a player its piece would pin (ConstructLifterPush.cs), no power
}

/// <summary>A construct's lifter parts, found when its parts change (<see cref="Construct.RebuildStamp"/>).</summary>
public sealed class LiftBody
{
    public ushort Body;
    public int Stamp = -1, Count = -1;
    public CLift[] Lifts = [];
    public int N;
}

/// <summary>
/// Lifters (docs/sandbox/design/arms-and-cranes.md): scripted machines that carry pieces, no joints and no solver. Phase 1:
/// the Loader Arm as a Building Station's extra pair of hands. An idle, powered arm asks the open stations whose hold and bay
/// it reaches for a step (<see cref="ConstructStation.LiftOffer"/>), takes the part out of the hold, carries it round its
/// base and fits it with the station's own <c>FitOne</c> when the move ends; at most one fit a tick a world. A claimed step
/// is <c>-2</c> in <see cref="StationJob.PartOf"/>: neither the station's boom nor a hand gives it. Server: nothing runs while
/// no station is open and no hand holds a piece. The wire: <see cref="CBodyKind.Lifts"/>, a body's lifters when a move starts
/// or ends (protocol 287); clients draw the move from it (LifterRender.cs).
/// </summary>
public static class ConstructLifters
{
    public const int Every = 6; // the step's rate: 10 Hz
    public const int ScanEvery = 60; // ticks between looks for lifter parts while any station is open (a body is looked at only when its parts changed)
    public const int DecisionsAStep = 4; // idle lifters that ask in one step
    public const float CarryRise = 0.4f; // over the higher end of a move, before it turns
    public const float GripLift = 0.05f; // the gripper's pads over the piece's top

    // ---- Finding the lifters (both peers) ----------------------------------------------------------------------------------

    /// <summary>Looks again at bodies whose parts changed; drops gone ones (their hands' pieces go back: server).</summary>
    public static void Scan(ConstructWorld world, bool server)
    {
        foreach (var c in world.Bodies)
            ScanBody(world, c, server);
        for (var k = world.LiftBodies.Count - 1; k >= 0; k--)
        {
            var lb = world.LiftBodies[k];
            if (world.Get(lb.Body) != null)
                continue;
            if (server)
                for (var i = 0; i < lb.N; i++)
                    Drop(world, null, lb.Lifts[i], lost: true);
            world.LiftBodies.RemoveAt(k);
            world.LiftSeen.Remove(lb.Body);
        }
        foreach (var (id, lb) in world.LiftSeen)
            if (lb.N == 0 && world.Get(id) == null)
                world.LiftSeen.Remove(id); // (a body without lifters that went: a removal while looking is allowed on a Dictionary)
    }

    /// <summary>One body: its lifter parts found again when its parts changed; a lifter that stays keeps its state.</summary>
    public static LiftBody ScanBody(ConstructWorld world, Construct c, bool server)
    {
        if (!world.LiftSeen.TryGetValue(c.Id, out var lb))
            world.LiftSeen[c.Id] = lb = new LiftBody { Body = c.Id };
        if (lb.Stamp == c.RebuildStamp && lb.Count == c.PartCount)
            return lb;
        lb.Stamp = c.RebuildStamp;
        lb.Count = c.PartCount;
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Lifter != null)
                n++;
        var old = lb.Lifts;
        var oldN = lb.N;
        var fresh = n == 0 ? [] : new CLift[n];
        n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.DefOf(i).Lifter == null)
                continue;
            CLift? keep = null;
            for (var o = 0; o < oldN; o++)
                if (old[o] is { } ol && ol.Part == i)
                {
                    keep = ol;
                    old[o] = null!;
                    break;
                }
            fresh[n++] = keep ?? new CLift { Body = c.Id, Part = (ushort)i };
        }
        if (server)
            for (var o = 0; o < oldN; o++)
                if (old[o] is { } gone)
                    Drop(world, c, gone, lost: true);
        lb.Lifts = fresh;
        lb.N = n;
        var at = world.LiftBodies.IndexOf(lb);
        if (n > 0 && at < 0)
            world.LiftBodies.Add(lb);
        else if (n == 0 && at >= 0)
            world.LiftBodies.RemoveAt(at);
        if (server && n > 0)
            world.LiftChanged(c.Id);
        return lb;
    }

    // ---- The step (server) -------------------------------------------------------------------------------------------------

    /// <summary>Server, every tick after the stations': nothing while no station is open and no hand is busy.</summary>
    public static void Step(ConstructWorld world)
    {
        var fitTaken = world.FitThisTick;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        StepLifts(world, fitTaken);
        if (world.LiftBodies.Count > 0)
        {
            var dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            LifterProbe.StepTicks++;
            LifterProbe.StepTotal += dt;
            LifterProbe.StepWorst = Math.Max(LifterProbe.StepWorst, dt);
        }
        world.FitThisTick = false; // (after: a lifter's own fit sets it too)
    }

    private static void StepLifts(ConstructWorld world, bool fitTaken)
    {
        Now = MoverPhysics.Clock; // (the server tick: Game.ServerTickStep sets it first)
        if (world.StationOpen.Count == 0 && world.LiftBusy == 0)
            return;
        world.LiftTick++;
        if (world.LiftTick % ScanEvery == 1)
            Scan(world, server: true);
        if (world.LiftTick % Every != 0)
            return;
        var busy = 0;
        var decisions = 0;
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            if (world.Get(lb.Body) is not { } c)
                continue;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l.Part >= c.PartCount || c.DefOf(l.Part).Lifter is not { } ld)
                    continue;
                if (!ConstructRecycler.Powered(world, c, l.Part))
                {
                    Why(world, l, LiftWhy.NoPower); // (it stops where it is, and goes on when power returns; a client poses it there too)
                    if (l.State is LiftState.Carry or LiftState.Return && !l.PowerWait)
                    {
                        l.PowerWait = true;
                        Freeze(world, l, Now);
                    }
                    busy += l.State == LiftState.Idle ? 0 : 1;
                    Ports(c, l);
                    continue;
                }
                if (l.PowerWait)
                {
                    l.PowerWait = false;
                    Thaw(world, l);
                }
                if (l.Why == LiftWhy.NoPower)
                    Why(world, l, l.PlayerWait ? LiftWhy.Player : l.ToHold ? LiftWhy.PutBack : LiftWhy.None);
                if (l.PlayerWait && l.State == LiftState.Carry && (l.ToHold || ClaimHolds(world, l)))
                {
                    // Waiting for a player its piece would pin (ConstructLifterPush.cs): on the tick they are clear; Stuck (Alarm) after 10 s.
                    if (ConstructLifterPush.Clear(world, c, l, ld))
                    {
                        l.PlayerWait = false;
                        Thaw(world, l);
                        Why(world, l, l.ToHold ? LiftWhy.PutBack : LiftWhy.None);
                    }
                    else
                    {
                        if (Now - l.WaitFrom > ConstructLifterPush.StuckTicks)
                            Why(world, l, LiftWhy.PlayerStuck);
                        busy++;
                        Ports(c, l);
                        continue;
                    }
                }
                switch (l.State)
                {
                    case LiftState.Idle:
                        var mode = (int)ConstructConfig.Get(c.Parts[l.Part], c.DefOf(l.Part), CfgKey.LiftMode, 0);
                        if (mode is 2 or 3)
                            Why(world, l, LiftWhy.Off); // (Carry only: phase 4 brings carries between machines; Off: it stands still)
                        else if (ConstructExtruder.RunHeld(c, l.Part))
                            Why(world, l, LiftWhy.RunOff);
                        else if (world.StationOpen.Count == 0)
                            Why(world, l, LiftWhy.NoStation);
                        else if (decisions < DecisionsAStep)
                        {
                            decisions++;
                            if (Claim(world, c, l, ld))
                                world.LiftChanged(c.Id);
                        }
                        break;
                    case LiftState.Stuck:
                        // A piece loaded in the hand: back into a reached station's hold when one takes it, a look every 2 s.
                        if ((world.LiftTick + l.Part * Every) % (20 * Every) == 0 && decisions < DecisionsAStep)
                        {
                            decisions++;
                            if (PutBack(world, c, l, ld))
                                world.LiftChanged(c.Id);
                        }
                        break;
                    case LiftState.Carry:
                        if (l.ToHold)
                        {
                            l.Age = (int)AgeAt(l, Now);
                            if (l.Age >= l.Dur)
                            {
                                Stow(world, c, l, ld);
                                world.LiftChanged(c.Id);
                            }
                            break;
                        }
                        if (!ClaimHolds(world, l))
                        {
                            Drop(world, c, l, lost: false);
                            world.LiftChanged(c.Id);
                            break;
                        }
                        l.Age = (int)AgeAt(l, Now);
                        if (l.Age >= l.Dur && !fitTaken)
                        {
                            fitTaken = true;
                            Fit(world, c, l, ld);
                            world.LiftChanged(c.Id);
                        }
                        break;
                    case LiftState.Return:
                        l.Age = (int)AgeAt(l, Now);
                        if (l.Age >= l.Dur)
                        {
                            l.State = LiftState.Idle;
                            world.LiftChanged(c.Id);
                        }
                        break;
                }
                if (l.State != LiftState.Idle)
                    busy++;
                Ports(c, l);
            }
        }
        world.LiftBusy = busy;
    }

    public static readonly string[] OutPorts = ["Busy", "Alarm", "Moved"]; // a lifter's outputs: Out, Out1 and port 2 (CPart.Mem: ConstructModules.Output)

    /// <summary>Server: its outputs. Busy while a move runs, Alarm while it holds a piece nothing takes, Moved the parts it fitted.</summary>
    private static void Ports(Construct c, CLift l)
    {
        ref var p = ref c.Parts[l.Part];
        var busy = l.State is LiftState.Carry or LiftState.Return && l.Why != LiftWhy.NoPower ? 1f : 0f;
        var alarm = l.State == LiftState.Stuck || l.Why == LiftWhy.PlayerStuck ? 1f : 0f; // (a player in the way over 10 s raises it too)
        if (p.Out == busy && p.Out1 == alarm && p.Mem == l.Moves)
            return;
        p.Out = p.OutNext = busy;
        p.Out1 = alarm;
        p.Mem = l.Moves;
        c.Wake();
    }

    /// <summary>What it waits on; a change is sent (the look line on clients).</summary>
    private static void Why(ConstructWorld world, CLift l, LiftWhy why)
    {
        if (l.Why == why)
            return;
        l.Why = why;
        world.LiftChanged(l.Body);
    }

    /// <summary>The Carry height setting, as a rise over a move's higher end.</summary>
    private static float RiseOf(Construct c, int part, LifterDef ld) =>
        Math.Clamp(ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.CarryHeight, CarryRise), 0, ld.Up);


    /// <summary>The shoulder in the world, and the lifter part's world frame.</summary>
    public static void Frame(Construct c, int part, LifterDef ld, out Vector3 pos, out Quaternion rot, out Vector3 shoulder)
    {
        Freeform.PartWorld(c, part, out pos, out rot);
        shoulder = pos + Vector3.Transform(ld.Base, rot);
    }

    /// <summary>
    /// Is a piece whose origin is at a world point in reach: in the ring round the shoulder, and (an arm) its wrist, <paramref name="lift"/>
    /// over that point, within what the two links reach, neither stretched straight nor folded through itself?
    /// <paramref name="far"/>: how far it is across.
    /// </summary>
    public static bool InReach(LifterDef ld, Vector3 shoulder, Quaternion rot, Vector3 at, float lift, out float far)
    {
        var d = Vector3.Transform(at - shoulder, Quaternion.Conjugate(rot));
        far = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        if (far < ld.ReachMin - 0.01f || far > ld.ReachMax + 0.01f || d.Y > ld.Up + 0.01f || d.Y < -ld.Down - 0.01f)
            return false;
        if (ld.Bridge)
            return true;
        var wrist = MathF.Sqrt(far * far + (d.Y + lift) * (d.Y + lift));
        return wrist <= ld.LinkMax && wrist >= ld.LinkMin;
    }

    public const float Wrist = 0.35f; // the wrist over the gripper bar
    public const float SafeLift = GripLift + Wrist; // an empty hand's wrist over its point

    /// <summary>How far an arm's wrist can be over a piece's origin, at most, however the piece is turned.</summary>
    public static float WristLift(ConstructPartDef? d) =>
        d == null ? SafeLift : MathF.Max(d.BoundsMin.Length(), d.BoundsMax.Length()) + SafeLift;

    /// <summary>Does a part fit the hand: its mass and its size (the longest side against the length, the next against across)?</summary>
    public static bool Carries(LifterDef ld, ConstructPartDef d)
    {
        var s = d.BoundsMax - d.BoundsMin;
        var a = MathF.Max(s.X, MathF.Max(s.Y, s.Z));
        var b = s.X + s.Y + s.Z - a - MathF.Min(s.X, MathF.Min(s.Y, s.Z));
        return d.Mass <= ld.Payload && a <= ld.PieceLong + 0.005f && b <= ld.PieceAcross + 0.005f;
    }

    /// <summary>An idle lifter asks the open stations it reaches for a step; true when it took one.</summary>
    private static bool Claim(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        var near = false; // (a station building in reach: else the look line says none is)
        for (var si = 0; si < world.StationOpen.Count; si++)
        {
            if (world.Get(world.StationOpen[si]) is not { } sc)
                continue;
            for (var t = 0; t < sc.PartCount; t++)
            {
                if (sc.Parts[t].Station is not { State: StationState.Building, Plan: { } plan } j || !sc.DefOf(t).IsBuildStation)
                    continue;
                if (world.StoreOf(sc, t) is not { } tray || !ConstructRecycler.Powered(world, sc, t))
                    continue;
                ConstructStation.Bay(sc, t, out var floor, out _, out var bay);
                if (Vector3.Distance(floor, shoulder) > ld.ReachMax + bay.Length() * 0.5f + ld.Down)
                    continue;
                // The pick: the point of the hold nearest the shoulder, a hand's height over its floor.
                var hold = HoldPoint(sc, t, shoulder);
                if (!InReach(ld, shoulder, lr, hold, SafeLift, out _))
                    continue;
                near = true;
                var k = ConstructStation.LiftOffer(j, tray, ld, shoulder, lr);
                if (k < 0)
                    continue;
                var d = ConstructParts.Find(j.Bp!.Steps[k].Part)!;
                if (!InReach(ld, shoulder, lr, hold, WristLift(d), out _))
                    continue; // (the hold's corner is too close or too far for the arm with this piece in its hand)
                if (!world.StationTake(sc, t, tray, d))
                    continue;
                world.CraftChanged(sc);
                ConstructStation.LiftClaim(j, k);
                var inv = Quaternion.Conjugate(lr);
                var place = j.Pos + Vector3.Transform(plan.At[k], j.Rot);
                Freeform.PartWorld(sc, t, out _, out var sr);
                l.Def = d.Id;
                l.Paint = k < j.Bp.StepPaint.Count ? j.Bp.StepPaint[k] : 0;
                l.From = Vector3.Transform(hold - Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, sr) - lp, inv);
                l.FromRot = Quaternion.Normalize(inv * sr);
                l.To = Vector3.Transform(place - lp, inv);
                l.ToRot = Quaternion.Normalize(inv * j.Rot * plan.Rot[k]);
                l.Age = 0;
                l.Rise = RiseOf(c, l.Part, ld);
                l.Dur = Ticks(ld, l.From, l.To, true, WristLift(d), l.Rise);
                l.Why = LiftWhy.None;
                l.ToHold = false;
                Begin(l);
                l.State = LiftState.Carry;
                LifterProbe.CarriesHeard++; // (the host counts its own; a client counts what it hears)
                l.Moved = true;
                l.Fitted = false;
                (l.StationBody, l.StationPart, l.Step, l.Job, l.Plan) = (sc.Id, t, k, j, plan);
                return true;
            }
        }
        Why(world, l, near ? LiftWhy.NoPart : LiftWhy.NoStation);
        return false;
    }

    /// <summary>The point of a station's hold nearest a shoulder: the hold is the half of the station on its bay side.</summary>
    public static Vector3 HoldPoint(Construct sc, int t, Vector3 shoulder)
    {
        var d = sc.DefOf(t);
        Freeform.PartWorld(sc, t, out var sp, out var sr);
        var local = Vector3.Transform(shoulder - sp, Quaternion.Conjugate(sr));
        var mid = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f;
        var x = Math.Clamp(local.X, mid + 0.3f, d.BoundsMax.X - 0.3f);
        var z = Math.Clamp(local.Z, d.BoundsMin.Z + 0.4f, d.BoundsMax.Z - 0.4f);
        return sp + Vector3.Transform(new Vector3(x, d.BoundsMin.Y + 0.9f, z), sr);
    }

    /// <summary>Is the step this hand carries for still wanted (the same job, building, the step still claimed)?</summary>
    private static bool ClaimHolds(ConstructWorld world, CLift l)
    {
        if (l.Fitted || l.Job is not { } j || world.Get(l.StationBody) is not { } sc || l.StationPart >= sc.PartCount || sc.Parts[l.StationPart].Station != j)
            return false;
        return j.State == StationState.Building && j.Plan == l.Plan && l.Step < j.PartOf.Length && j.PartOf[l.Step] == -2;
    }

    /// <summary>The move has ended: the step goes on by the station's own fit, and the hand goes back to the hold.</summary>
    private static void Fit(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        var sc = world.Get(l.StationBody)!;
        ConstructStation.LiftFit(world, sc, l.StationPart, l.Job!, l.Step);
        LifterProbe.Fits++;
        l.Moves++;
        l.Fitted = true;
        Back(c, l, ld, sc, l.StationPart);
    }

    /// <summary>The empty hand's way back: from where the piece was to the hold it came from (or where it is, with no station).</summary>
    private static void Back(Construct c, CLift l, LifterDef ld, Construct? sc, int t)
    {
        var from = l.State == LiftState.Carry ? Pose(ld, l, l.Age, out _) : l.To;
        var to = l.From;
        if (sc != null && t >= 0 && t < sc.PartCount)
        {
            Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
            to = Vector3.Transform(HoldPoint(sc, t, shoulder) - lp, Quaternion.Conjugate(lr));
        }
        l.From = from;
        l.To = to;
        l.FromRot = l.ToRot;
        l.Def = 0;
        l.Paint = 0;
        l.Age = 0;
        l.Dur = Ticks(ld, from, to, false, SafeLift, l.Rise);
        Begin(l);
        l.State = LiftState.Return;
        l.ToHold = false;
        if (l.Why == LiftWhy.PutBack)
            l.Why = LiftWhy.None;
        l.Job = null;
        l.Plan = null;
        l.Step = -1;
    }

    /// <summary>
    /// Server: a hand's piece whose step is no longer wanted (a cancel, a release, a failed or restarted job) goes back with the
    /// rest of the job's parts; with no station, or a lifter that went (<paramref name="lost"/>), it is set down loose where it is.
    /// </summary>
    private static void Drop(ConstructWorld world, Construct? c, CLift l, bool lost)
    {
        if (l.State is not (LiftState.Carry or LiftState.Stuck) || l.Fitted || l.Def == 0)
        {
            if (lost)
                l.State = LiftState.Idle;
            return;
        }
        var d = ConstructParts.Get(l.Def);
        var sc = world.Get(l.StationBody);
        if (d != null && l.Job is { } j && sc != null && l.StationPart < sc.PartCount && sc.Parts[l.StationPart].Station == j)
        {
            ConstructStation.LiftUnclaim(j, l.Plan, l.Step);
            j.Return.Add((l.Def, 1, false)); // (given back as the rest is: ConstructStation.GiveBack)
            world.StationOpened(sc);
        }
        else if (d != null && c != null && l.Part < c.PartCount && c.DefOf(l.Part).Lifter is { } ld)
        {
            Frame(c, l.Part, ld, out var lp, out var lr, out _);
            var at = lp + Vector3.Transform(Pose(ld, l, l.Age, out _), lr);
            ConstructBuild.PlaceNew(world, d, at, 0);
        }
        Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: its {d?.Name ?? "piece"} {(lost ? "was set down: the lifter went" : "goes back: its step is no longer wanted")}");
        if (lost || c == null || l.Part >= c.PartCount || c.DefOf(l.Part).Lifter is not { } lf)
        {
            l.State = LiftState.Idle;
            l.Def = 0;
            l.Job = null;
            l.ToHold = false;
            return;
        }
        Back(c, l, lf, sc, l.StationPart);
    }

    /// <summary>
    /// Server: a hand holding a piece nothing claimed (one loaded in the hand) carries it back into the hold of a station that is
    /// building in reach, when the hold takes it; then the station's own work or a lifter's claim fits it. True when it set off.
    /// </summary>
    private static bool PutBack(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        if (ConstructParts.Get(l.Def) is not { } d)
        {
            (l.State, l.Def, l.Why) = (LiftState.Idle, 0, LiftWhy.None);
            return true;
        }
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        for (var si = 0; si < world.StationOpen.Count; si++)
        {
            if (world.Get(world.StationOpen[si]) is not { } sc)
                continue;
            for (var t = 0; t < sc.PartCount; t++)
            {
                if (sc.Parts[t].Station is not { State: StationState.Building } || !sc.DefOf(t).IsBuildStation || world.StoreOf(sc, t) is not { } tray)
                    continue;
                if (ConstructConveyor.RackRefusal(sc, t, tray, d) != null)
                    continue;
                var hold = HoldPoint(sc, t, shoulder);
                if (!InReach(ld, shoulder, lr, hold, WristLift(d), out _))
                    continue;
                var inv = Quaternion.Conjugate(lr);
                l.From = Pose(ld, l, l.Age, out var rot);
                l.FromRot = rot;
                l.To = Vector3.Transform(hold - lp, inv);
                l.ToRot = rot;
                l.Age = 0;
                l.Rise = RiseOf(c, l.Part, ld);
                l.Dur = Ticks(ld, l.From, l.To, true, WristLift(d), l.Rise);
                Begin(l);
                (l.State, l.ToHold, l.Why, l.Moved, l.Fitted) = (LiftState.Carry, true, LiftWhy.PutBack, true, false);
                (l.StationBody, l.StationPart, l.Step, l.Job, l.Plan) = (sc.Id, t, -1, null, null);
                return true;
            }
        }
        Why(world, l, LiftWhy.Holding);
        return false;
    }

    /// <summary>The put-back's end: the piece into the hold (it stays in the hand, stuck, if the hold no longer takes it).</summary>
    private static void Stow(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        var sc = world.Get(l.StationBody);
        if (sc != null && l.StationPart >= 0 && l.StationPart < sc.PartCount && ConstructParts.Get(l.Def) is { } d && world.RackPut(sc, l.StationPart, d))
        {
            world.CraftChanged(sc);
            world.StationOpened(sc);
            Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: put its {d.Name} back into the station's hold");
            Back(c, l, ld, sc, l.StationPart);
            return;
        }
        (l.State, l.ToHold, l.Why, l.Age, l.From) = (LiftState.Stuck, false, LiftWhy.Holding, l.Dur, l.To);
    }

    // ---- The save (the LIFT block: WorldSaveBodies.cs) ---------------------------------------------------------------------

    /// <summary>The tick's copy for a save: each hand holding a piece that is not yet fitted (part, piece, paint). Null: none.</summary>
    public static (ushort Part, ushort Def, uint Paint)[]? Snapshot(ConstructWorld world, Construct c)
    {
        if (!world.LiftSeen.TryGetValue(c.Id, out var lb) || lb.N == 0)
            return null;
        var n = 0;
        for (var i = 0; i < lb.N; i++)
            if (Holding(lb.Lifts[i]))
                n++;
        if (n == 0)
            return null;
        var o = new (ushort, ushort, uint)[n];
        n = 0;
        for (var i = 0; i < lb.N; i++)
            if (Holding(lb.Lifts[i]))
                o[n++] = (lb.Lifts[i].Part, lb.Lifts[i].Def, lb.Lifts[i].Paint);
        return o;
    }

    private static bool Holding(CLift l) => l.Def != 0 && !l.Fitted && l.State is LiftState.Carry or LiftState.Stuck;

    /// <summary>
    /// A load: each saved piece in its lifter's hand, at rest, stuck; the lifter's first look carries it back into the hold of a
    /// station building in reach (and a claim then fits it), else it waits with it (Alarm). A part that is no longer a lifter
    /// sets its piece down loose beside it.
    /// </summary>
    public static void ApplySave(ConstructWorld world, Construct c, (ushort Part, ushort Def, uint Paint)[] held)
    {
        var lb = ScanBody(world, c, server: true);
        foreach (var (part, def, paint) in held)
        {
            if (def == 0 || ConstructParts.Get(def) is not { } d)
                continue;
            CLift? l = null;
            for (var i = 0; i < lb.N; i++)
                if (lb.Lifts[i].Part == part)
                    l = lb.Lifts[i];
            if (l == null || c.DefOf(part).Lifter is not { } ld)
            {
                Freeform.PartWorld(c, Math.Min(part, c.PartCount - 1), out var at, out _);
                ConstructBuild.PlaceNew(world, d, at + Vector3.UnitY * 2, 0);
                continue;
            }
            var rest = ld.Base + new Vector3(ld.ReachMin + 0.9f, -0.9f, 0);
            (l.State, l.Def, l.Paint, l.From, l.To, l.FromRot, l.ToRot) = (LiftState.Stuck, def, paint, rest, rest, Quaternion.Identity, Quaternion.Identity);
            (l.Age, l.Dur, l.Start, l.Held, l.WaitFrom, l.Moved, l.Why, l.Fitted, l.ToHold) = (Every, Every, 0u, 0, 0u, true, LiftWhy.Holding, false, false);
            world.LiftBusy++; // (the step runs while a hand holds a piece)
        }
        world.LiftChanged(c.Id);
    }

    // ---- The path (both peers) ---------------------------------------------------------------------------------------------

    /// <summary>The move's length in ticks: half the handling at each end, the path at the hand's speed.</summary>
    public static int Ticks(LifterDef ld, Vector3 from, Vector3 to, bool loaded, float lift, float rise)
    {
        var len = PathLength(ld, from, to, lift, rise);
        var sec = (loaded ? ld.Handle : ld.Handle * 0.25f) + len / ld.Speed;
        return Math.Clamp((int)MathF.Ceiling(sec * World.TickRate), Every, 60 * World.TickRate);
    }

    /// <summary>
    /// The carry height: a little over the higher end, never over the ring's top, and (an arm) never so high that the wrist,
    /// <paramref name="lift"/> over the piece, leaves the links' reach at the wider of the two ends (the ends are in reach, so
    /// the whole path is). Never under the higher end.
    /// </summary>
    private static float CarryY(LifterDef ld, Vector3 from, Vector3 to, float lift, float rise)
    {
        var high = MathF.Max(from.Y, to.Y);
        var h = MathF.Min(ld.Base.Y + ld.Up, high + rise);
        if (!ld.Bridge)
        {
            Polar(ld, from, out var r0, out _);
            Polar(ld, to, out var r1, out _);
            var r = MathF.Max(r0, r1);
            var room = ld.LinkMax * ld.LinkMax - r * r;
            if (room > 0)
                h = MathF.Min(h, ld.Base.Y + MathF.Sqrt(room) - lift);
        }
        return MathF.Max(h, high);
    }

    private static void Polar(LifterDef ld, Vector3 p, out float r, out float a)
    {
        var dx = p.X - ld.Base.X;
        var dz = p.Z - ld.Base.Z;
        r = MathF.Sqrt(dx * dx + dz * dz);
        a = MathF.Atan2(dz, dx);
    }

    private static float Turn(float a0, float a1)
    {
        var d = a1 - a0;
        while (d > MathF.PI)
            d -= MathF.Tau;
        while (d < -MathF.PI)
            d += MathF.Tau;
        return d;
    }

    /// <summary>Straight up to the carry height, round the base in angle and radius (never through the mast), straight down.</summary>
    public static float PathLength(LifterDef ld, Vector3 from, Vector3 to, float lift, float rise)
    {
        var h = CarryY(ld, from, to, lift, rise);
        Polar(ld, from, out var r0, out var a0);
        Polar(ld, to, out var r1, out var a1);
        var arc = MathF.Abs(Turn(a0, a1)) * (r0 + r1) * 0.5f + MathF.Abs(r1 - r0);
        return MathF.Max(0, h - from.Y) + arc + MathF.Max(0, h - to.Y);
    }

    /// <summary>The piece's origin (lifter part frame) and turn <paramref name="age"/> ticks into a move.</summary>
    public static Vector3 Pose(LifterDef ld, CLift l, float age, out Quaternion rot)
    {
        var loaded = l.Def != 0;
        var still = (loaded ? ld.Handle : ld.Handle * 0.25f) * 0.5f;
        var sec = age / World.TickRate - still;
        var from = l.From;
        var to = l.To;
        rot = l.FromRot;
        if (sec <= 0)
            return from;
        var h = CarryY(ld, from, to, l.Def != 0 ? WristLift(ConstructParts.Get(l.Def)) : SafeLift, l.Rise);
        var up = MathF.Max(0, h - from.Y);
        Polar(ld, from, out var r0, out var a0);
        Polar(ld, to, out var r1, out var a1);
        var turn = Turn(a0, a1);
        var arc = MathF.Abs(turn) * (r0 + r1) * 0.5f + MathF.Abs(r1 - r0);
        var down = MathF.Max(0, h - to.Y);
        var s = sec * ld.Speed;
        if (s < up)
            return new Vector3(from.X, from.Y + s, from.Z);
        s -= up;
        if (s < arc && arc > 1e-4f)
        {
            var f = s / arc;
            var r = r0 + (r1 - r0) * f;
            var a = a0 + turn * f;
            rot = Quaternion.Slerp(l.FromRot, l.ToRot, f);
            return new Vector3(ld.Base.X + r * MathF.Cos(a), h, ld.Base.Z + r * MathF.Sin(a));
        }
        s -= arc;
        rot = l.ToRot;
        return s < down ? new Vector3(to.X, h - s, to.Z) : to;
    }

    /// <summary>
    /// Ticks into the move at a mover-clock tick (both peers, the same answer from the same word): the path is a pure function
    /// of the server tick, as a map mover's pose is (arms-and-cranes.md 3.3); a waiting move is posed where it stopped.
    /// </summary>
    public static float AgeAt(CLift l, double tick)
    {
        if (l.State is not (LiftState.Carry or LiftState.Return))
            return l.Dur;
        var t = l.WaitFrom != 0 ? l.WaitFrom : tick;
        return (float)Math.Clamp(t - l.Start - l.Held, 0, l.Dur);
    }

    /// <summary>Ticks into the move as this peer draws it this frame (<see cref="DrawTick"/>).</summary>
    public static float AgeNow(CLift l) => AgeAt(l, DrawTick);

    /// <summary>The frame's pose tick (Game.MoverRenderTick: the host's server tick, a client's predicted one), set by Program each frame.</summary>
    public static double DrawTick;

    /// <summary>Server: the mover clock at this step (the server tick).</summary>
    public static uint Now;

    /// <summary>Server: a move starts now.</summary>
    private static void Begin(CLift l) =>
        (l.Start, l.Held, l.WaitFrom, l.Age, l.PlayerWait, l.PowerWait) = (Now, 0, 0u, 0, false, false);

    /// <summary>Server: the move stops where it is (a player in the way, no power); sent.</summary>
    internal static void Freeze(ConstructWorld world, CLift l, uint at)
    {
        if (l.WaitFrom != 0)
            return;
        l.WaitFrom = Math.Max(1, at);
        world.LiftChanged(l.Body);
    }

    /// <summary>
    /// Server (ConstructLifterPush.cs): its piece would pin a player at <paramref name="tick"/>'s pose, so the move waits there
    /// (both peers pose it at that tick) until the player is clear.
    /// </summary>
    internal static void Pin(ConstructWorld world, CLift l, uint tick)
    {
        if (l.PlayerWait || l.State != LiftState.Carry)
            return;
        l.PlayerWait = true;
        Freeze(world, l, tick);
        Why(world, l, LiftWhy.Player);
        Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: waits, a player is in the way (tick {tick}, {AgeAt(l, tick):0}/{l.Dur} into its move)");
    }

    /// <summary>Server: the move goes on from where it stopped, once nothing holds it; sent.</summary>
    internal static void Thaw(ConstructWorld world, CLift l)
    {
        if (l.WaitFrom == 0 || l.PlayerWait || l.PowerWait)
            return;
        l.Held += unchecked((int)(Now - l.WaitFrom));
        l.WaitFrom = 0;
        world.LiftChanged(l.Body);
    }

    private static double _fittersAt = -10;
    private static ushort _fittersBody;
    private static int _fittersPart = -1;
    private static string _fittersText = "";

    /// <summary>
    /// A station panel's line (arms-and-cranes.md section 4): "Fitters: the station, 2 Loader Arms (Workshop, Scrap) - about 1.4 parts
    /// a second". Every lifter whose hand reaches the station's hold counts; the rate is a light part's, the station's own 2 s and
    /// each arm's cycle from the hold to the bay's middle and back. Made again once a second while the panel is open.
    /// </summary>
    public static string FittersLine(ConstructWorld world, Construct sc, int t)
    {
        var now = ConstructConveyor.Clock;
        if (_fittersBody == sc.Id && _fittersPart == t && now - _fittersAt < 1.0)
            return _fittersText;
        (_fittersBody, _fittersPart, _fittersAt) = (sc.Id, t, now);
        var rate = 1f / ConstructStation.FitSeconds;
        var n = 0;
        var tiers = new System.Text.StringBuilder();
        ConstructStation.Bay(sc, t, out var floor, out _, out var bay);
        var middle = floor + Vector3.UnitY * MathF.Min(bay.Y * 0.5f, 1f);
        foreach (var lb in world.LiftBodies)
        {
            if (world.Get(lb.Body) is not { } c)
                continue;
            for (var i = 0; i < lb.N; i++)
            {
                var part = lb.Lifts[i].Part;
                if (part >= c.PartCount || c.DefOf(part).Lifter is not { Bridge: false } ld)
                    continue;
                Frame(c, part, ld, out _, out var lr, out var shoulder);
                var hold = HoldPoint(sc, t, shoulder);
                if (!InReach(ld, shoulder, lr, hold, SafeLift, out _))
                    continue;
                n++;
                tiers.Append(tiers.Length > 0 ? ", " : "").Append(TierName(ld.Tier));
                rate += 1f / (ld.Handle * 1.25f + 2 * Vector3.Distance(hold, middle) / ld.Speed);
            }
        }
        _fittersText = n == 0
            ? "Fitters: the station alone. A Loader Arm whose reach takes in the hold fits parts beside it."
            : $"Fitters: the station, {n} Loader Arm{(n == 1 ? "" : "s")} ({tiers}) - about {rate:0.0} light parts a second";
        return _fittersText;
    }

    private static string TierName(int tier) => tier switch { 2 => "Workshop", 3 => "Industrial", 4 => "Military", 5 => "Plasma", _ => "Scrap" };

    // ---- The look line (both peers) ---------------------------------------------------------------------------------------

    /// <summary>A key that changes when the look line would (Program.Sandbox.cs builds the text only then).</summary>
    public static long LookKey(ConstructWorld world, Construct c, int part) =>
        Find(world, c.Id, part) is { } l ? (long)l.State | (long)l.Why << 4 | (long)l.Def << 12 | (long)(l.Step + 1 & 0xFFF) << 28 | (long)l.Moves << 40 : -1;

    /// <summary>The lifter state this peer has for a part, or null.</summary>
    public static CLift? Find(ConstructWorld world, ushort body, int part)
    {
        if (!world.LiftSeen.TryGetValue(body, out var lb))
            return null;
        for (var i = 0; i < lb.N; i++)
            if (lb.Lifts[i].Part == part)
                return lb.Lifts[i];
        return null;
    }

    /// <summary>"Workshop Loader Arm: fitting a Wheel Hub to the Buggy, step 12 of 26 - reaches 1.2 to 5 m, carries 1 t; 7 fitted".</summary>
    public static string Look(ConstructWorld world, Construct c, int part)
    {
        var d = c.DefOf(part);
        if (d.Lifter is not { } ld)
            return d.Name;
        var l = Find(world, c.Id, part);
        var piece = l != null && l.Def != 0 ? ConstructParts.Get(l.Def)?.Name ?? "piece" : "piece";
        string what;
        if (l == null)
            what = "idle";
        else
            what = l.State switch
            {
                LiftState.Carry when l.Why == LiftWhy.PutBack => $"putting a {piece} back into the station's hold",
                LiftState.Carry => $"fitting a {piece}{JobText(world, l)}",
                LiftState.Return => "going back to the hold",
                LiftState.Stuck => $"holding a {piece} - stuck: no station's hold in reach takes it",
                _ => l.Why switch
                {
                    LiftWhy.RunOff => "held off by its Run wire",
                    LiftWhy.Off => "set not to work at stations (Works)",
                    LiftWhy.NoStation => "idle: no Building Station building in reach",
                    LiftWhy.NoPart => "waiting: nothing in the hold it can carry to a place in its reach",
                    _ => "idle",
                },
            };
        if (l is { Why: LiftWhy.NoPower })
            what += " - no power, stopped";
        else if (l is { Why: LiftWhy.Player })
            what += " - waiting: a player is in the way";
        else if (l is { Why: LiftWhy.PlayerStuck })
            what += " - stuck: a player has been in the way for over 10 s";
        var carries = ld.Payload >= 1000 ? $"{ld.Payload / 1000f:0.#} t" : $"{ld.Payload:0} kg";
        return $"{d.Name}: {what} - reaches {ld.ReachMin:0.#} to {ld.ReachMax:0.#} m, carries {carries}{(l is { Moves: > 0 } ? $"; {l.Moves} fitted" : "")}";
    }

    private static string JobText(ConstructWorld world, CLift l)
    {
        if (world.Get(l.StationBody) is not { } sc)
            return "";
        for (var t = 0; t < sc.PartCount; t++)
            if (sc.Parts[t].Station is { } j && sc.DefOf(t).IsBuildStation)
                return l.Step >= 0 ? $" to the {j.Name}, step {l.Step + 1} of {j.Steps}" : $" to the {j.Name}";
        return "";
    }

    // ---- The wire (CBodyKind.Lifts, protocol 290) --------------------------------------------------------------------------

    private static ushort Cm(float v) => unchecked((ushort)(short)Math.Clamp(MathF.Round(v * 100), short.MinValue, short.MaxValue));
    private static float FromCm(ushort v) => unchecked((short)v) / 100f;
    private static ushort Unit(float v) => unchecked((ushort)(short)Math.Clamp(MathF.Round(v * 32767), -32767, 32767));
    private static float FromUnit(ushort v) => unchecked((short)v) / 32767f;

    private static void Vec(PacketWriter w, Vector3 v) => w.U16(Cm(v.X)).U16(Cm(v.Y)).U16(Cm(v.Z));
    private static void Rot(PacketWriter w, Quaternion q) => w.U16(Unit(q.X)).U16(Unit(q.Y)).U16(Unit(q.Z)).U16(Unit(q.W));

    /// <summary>A body's lifters, the whole list (a move started or ended; a joiner): about 53 bytes a lifter.</summary>
    public static void Write(PacketWriter w, LiftBody lb)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Lifts).U16(lb.Body).U8((byte)Math.Min(lb.N, 255));
        for (var i = 0; i < lb.N && i < 255; i++)
        {
            var l = lb.Lifts[i];
            w.U16(l.Part).U8((byte)l.State).U8((byte)(l.Moved ? 1 : 0)).U16(l.Def).U32(l.Paint);
            Vec(w, l.From);
            Vec(w, l.To);
            Rot(w, l.FromRot);
            Rot(w, l.ToRot);
            w.U16((ushort)Math.Min(l.Dur, ushort.MaxValue)).U32(l.Start).U32(unchecked((uint)l.Held)).U32(l.WaitFrom); // (protocol 296: the move on the mover clock, and where it waits)
            w.U16(Cm(l.Rise)).U8((byte)l.Why).U16((ushort)(l.Step + 1)).U16(l.StationBody).U16(l.Moves); // (protocol 290: the look line's and the carry height)
        }
    }

    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var id = r.U16();
        int n = r.U8();
        var c = world.Get(id);
        LiftBody? lb = null;
        if (c != null)
        {
            if (!world.LiftSeen.TryGetValue(id, out lb))
                world.LiftSeen[id] = lb = new LiftBody { Body = id };
            if (lb.Lifts.Length < n)
                Array.Resize(ref lb.Lifts, n);
            if (!world.LiftBodies.Contains(lb))
                world.LiftBodies.Add(lb);
        }
        for (var k = 0; k < n; k++)
        {
            var part = r.U16();
            var state = (LiftState)r.U8();
            var moved = r.U8() != 0;
            var def = r.U16();
            var paint = r.U32();
            var from = new Vector3(FromCm(r.U16()), FromCm(r.U16()), FromCm(r.U16()));
            var to = new Vector3(FromCm(r.U16()), FromCm(r.U16()), FromCm(r.U16()));
            var fr = Quaternion.Normalize(new Quaternion(FromUnit(r.U16()), FromUnit(r.U16()), FromUnit(r.U16()), FromUnit(r.U16())));
            var tr = Quaternion.Normalize(new Quaternion(FromUnit(r.U16()), FromUnit(r.U16()), FromUnit(r.U16()), FromUnit(r.U16())));
            int dur = r.U16();
            var start = r.U32();
            var held = unchecked((int)r.U32());
            var waitFrom = r.U32();
            var rise = FromCm(r.U16());
            var why = (LiftWhy)r.U8();
            var step = r.U16() - 1;
            var station = r.U16();
            var moves = r.U16();
            if (lb == null || c == null || part >= c.PartCount || c.DefOf(part).Lifter == null)
                continue;
            CLift? l = null;
            for (var i = 0; i < lb.N; i++)
                if (lb.Lifts[i].Part == part)
                    l = lb.Lifts[i];
            if (l == null)
            {
                if (lb.N >= lb.Lifts.Length)
                    Array.Resize(ref lb.Lifts, lb.N + 1);
                lb.Lifts[lb.N++] = l = new CLift { Body = id, Part = part };
            }
            if (state == LiftState.Carry && (l.State != LiftState.Carry || l.Def != def))
                LifterProbe.CarriesHeard++;
            (l.State, l.Moved, l.Def, l.Paint, l.From, l.To, l.FromRot, l.ToRot, l.Dur, l.Start, l.Held, l.WaitFrom) = (state, moved, def, paint, from, to, fr, tr, dur, start, held, waitFrom);
            l.Age = (int)AgeAt(l, MoverPhysics.Clock);
            (l.Rise, l.Why, l.Step, l.StationBody, l.Moves) = (rise, why, step, station, moves);
        }
    }

    /// <summary>Server: every body's lifters to a joiner (after its bodies).</summary>
    public static void WriteAll(ConstructWorld world, ushort body, PacketWriter w, out bool any)
    {
        any = false;
        for (var i = 0; i < world.LiftBodies.Count; i++)
            if (world.LiftBodies[i].Body == body && world.LiftBodies[i].N > 0)
            {
                Write(w, world.LiftBodies[i]);
                any = true;
            }
    }
}

public sealed partial class StationJob
{
    /// <summary>Each step's part on the body as costing built it (body frame): where a lifter puts it (ConstructLifters.cs). Made again by every costing.</summary>
    public sealed class LiftPlan
    {
        public Vector3[] At = [], Centre = [];
        public Quaternion[] Rot = [];
    }

    public LiftPlan? Plan; // server: null until costed
}

public static partial class ConstructStation
{
    public const int OfferLooks = 64; // unfitted steps a lifter's offer looks at

    /// <summary>The costing's end: each step's part place and turn, kept for the lifters.</summary>
    private static void LiftCost(StationJob j, ConstructBuild.BuildRun run, Construct built)
    {
        var n = j.Bp!.Steps.Count;
        var plan = new StationJob.LiftPlan { At = new Vector3[n], Centre = new Vector3[n], Rot = new Quaternion[n] };
        var of = run.PartOf;
        for (var k = 0; k < n; k++)
        {
            var i = of != null && k < of.Length ? of[k] : -1;
            if (i < 0 || i >= built.PartCount)
            {
                plan.Rot[k] = Quaternion.Identity;
                continue;
            }
            ref readonly var p = ref built.Parts[i];
            plan.At[k] = p.Pos;
            plan.Rot[k] = Rot24.Quat(p.Rot);
            var d = built.DefOf(i);
            plan.Centre[k] = p.Pos + Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, plan.Rot[k]);
        }
        j.Plan = plan;
    }

    /// <summary>
    /// A lifter's offer: the first unfitted, unclaimed step whose parent is in place, whose kind the hold has, whose part's middle
    /// is in the lifter's reach and whose part it carries. Looks at <see cref="OfferLooks"/> steps at most. -1: none.
    /// </summary>
    public static int LiftOffer(StationJob j, CStore tray, LifterDef ld, Vector3 shoulder, Quaternion rot)
    {
        if (j.Plan is not { } plan || j.Bp is not { } bp || plan.At.Length != j.PartOf.Length)
            return -1;
        var looked = 0;
        for (var k = j.Cursor; k < j.PartOf.Length && looked < OfferLooks; k++)
        {
            if (j.PartOf[k] != -1)
                continue;
            looked++;
            if (!Ready(j, k) || j.StepKind[k] < 0)
                continue;
            var def = j.KindDef[j.StepKind[k]];
            if (ConstructCrafting.Holds(tray, def) <= 0 || ConstructParts.Get(def) is not { } d || !ConstructLifters.Carries(ld, d))
                continue;
            if (!ConstructLifters.InReach(ld, shoulder, rot, j.Pos + Vector3.Transform(plan.At[k], j.Rot), ConstructLifters.WristLift(d), out _))
                continue;
            return k;
        }
        return -1;
    }

    /// <summary>A step claimed: out of the station's and a hand's reach, and counted as fitted for the orders (its part has left the hold).</summary>
    public static void LiftClaim(StationJob j, int k)
    {
        j.PartOf[k] = -2;
        if (j.StepKind[k] >= 0)
            j.KindFit[j.StepKind[k]]++;
    }

    /// <summary>A claim given up (the piece goes back).</summary>
    public static void LiftUnclaim(StationJob j, StationJob.LiftPlan? plan, int k)
    {
        if (j.Plan != plan || k < 0 || k >= j.PartOf.Length || j.PartOf[k] != -2)
            return;
        j.PartOf[k] = -1;
        if (j.StepKind[k] >= 0)
            j.KindFit[j.StepKind[k]]--;
    }

    /// <summary>A lifter's place: the station's own fit, on its job, of the step it claimed.</summary>
    public static void LiftFit(ConstructWorld world, Construct c, int t, StationJob j, int k)
    {
        j.PartOf[k] = -1;
        if (j.StepKind[k] >= 0)
            j.KindFit[j.StepKind[k]]--;
        FitOne(world, c, t, j, k);
        world.StationOpened(c);
    }
}

public sealed partial class ConstructWorld
{
    public readonly Dictionary<ushort, LiftBody> LiftSeen = new(); // every body looked at, by id (both peers; a body without lifters has N 0)
    public readonly List<LiftBody> LiftBodies = new(8); // the bodies that have lifters
    public readonly List<ushort> LiftDirty = new(8); // server: bodies whose lifters' moves changed (CBodyKind.Lifts)
    public bool FitThisTick; // server: a part was fitted at a station this tick (one a tick a world)
    public int LiftBusy; // server: lifters not idle at the last step
    public int LiftTick;

    public void LiftClear()
    {
        LiftSeen.Clear();
        LiftBodies.Clear();
        LiftDirty.Clear();
        (LiftBusy, LiftTick, FitThisTick) = (0, 0, false);
    }

    public void LiftChanged(ushort body)
    {
        if (!LiftDirty.Contains(body))
            LiftDirty.Add(body);
    }
}

/// <summary>The window tool's "lifter" verb (Program.WindowShot.cs): every lifter as this peer has it, and an arm set beside a station.</summary>
public static class LifterProbe
{
    public static int Fits; // server: steps fitted by lifters since start
    public static int CarriesHeard; // either peer: moves seen to start (state became Carry)

    public static string Text(ConstructWorld world)
    {
        var sb = new System.Text.StringBuilder();
        var n = 0;
        foreach (var lb in world.LiftBodies)
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                n++;
                sb.Append($" [body {lb.Body} part {l.Part} {world.Get(lb.Body)?.DefOf(l.Part).Name}: {l.State} {l.Why} moves {l.Moves}{(l.Def != 0 ? " " + (ConstructParts.Get(l.Def)?.Name ?? "?") : "")} {l.Age}/{l.Dur} ticks{(l.WaitFrom != 0 ? $" waiting at tick {l.WaitFrom}" : "")} held {l.Held}{IkOf(world, lb.Body, l)}]");
            }
        var f = System.Diagnostics.Stopwatch.Frequency / 1e6;
        var cost = StepTicks > 0 ? $"; lifter step {StepTotal / f / StepTicks:0.0} us a tick on average, worst {StepWorst / f:0} us (with its fits) over {StepTicks} ticks" : "";
        if (ConstructLifterPush.BlockCalls > 0)
            cost += $"; push {ConstructLifterPush.BlockTotal / f / ConstructLifterPush.BlockCalls:0.00} us a player tick on average, worst {ConstructLifterPush.BlockWorst / f:0.0} us over {ConstructLifterPush.BlockCalls}";
        return $"{n} lifter(s), {CarriesHeard} carries seen, {Fits} fitted by lifters (host count), players pushed {ConstructLifterPush.Pushes} times, pinned {ConstructLifterPush.Pins} (this peer; last in the way: {ConstructLifterPush.LastBlock}){cost}{sb}";
    }

    /// <summary>Host: a Loader Arm on the ground behind the station's hold, inside its bay's back corner, turned as the station is.</summary>
    public static string Place(ConstructWorld world, Construct sc, int t, string tier = "", int slot = 0)
    {
        var name = (tier.Length > 0 && tier != "Scrap" ? tier + " " : "") + "Loader Arm";
        if (ConstructParts.Find(name) is not { Lifter: { } ld } d)
            return $"no {name} part";
        var sd = sc.DefOf(t);
        Freeform.PartWorld(sc, t, out var sp, out var sr);
        var centre = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var half = (d.BoundsMax - d.BoundsMin) * 0.5f;
        // Slot 0: the bay's back corner (phase 1's place); 1: its front corner; 2 and 3 the same, 6 m further along the bay.
        var x = sd.BoundsMax.X + ConstructStation.BayGap + half.X + 0.15f + slot / 2 * 6f;
        var z = slot % 2 == 0 ? sd.BoundsMin.Z - half.Z - 0.15f : sd.BoundsMax.Z + half.Z + 0.15f;
        var local = new Vector3(x - centre.X, sd.BoundsMin.Y - d.BoundsMin.Y + 0.02f, z - centre.Z);
        var made = ConstructBuild.PlaceNew(world, d, new FreePlacement { Pos = sp + Vector3.Transform(local, sr), Rot = sr });
        return made == null ? $"no room for the {name}" : $"a {name} stands at the bay's slot {slot} (construct {made.Id}), shoulder {ld.Base}";
    }

    public static long StepTicks, StepTotal, StepWorst; // server: the lifter step's own cost (Stopwatch ticks), for the window tool

    /// <summary>
    /// The window tool: an arm's pose over its whole move as this peer has it, posed as LifterRender poses it. The wrist's
    /// wanted distance from the shoulder against the links' range (under 1 at LinkMin: folded; over 1 at LinkMax: stretched,
    /// which the IK clamps), and the elbow at the move's middle tick (host and client compare it).
    /// </summary>
    public static string Ik(LifterDef ld, CLift l)
    {
        if (ld.Bridge || l.State is not (LiftState.Carry or LiftState.Return) || l.Dur <= 0)
            return "";
        float lo = float.MaxValue, hi = 0;
        var clamped = 0;
        for (var a = 0; a <= l.Dur; a += 3)
        {
            var grip = Grip(ld, l, a);
            var len = (grip + Vector3.UnitY * ConstructLifters.Wrist - ld.Base).Length();
            lo = MathF.Min(lo, len);
            hi = MathF.Max(hi, len);
            clamped += len < ld.LinkMin - 0.001f || len > ld.LinkMax + 0.001f ? 1 : 0;
        }
        ArmIk.Solve(ld, Grip(ld, l, l.Dur * 0.5f), out _, out var elbow, out _);
        return $" ik: wrist {lo:0.00} to {hi:0.00} m of links {ld.LinkMin:0.00} to {ld.LinkMax:0.00}, {clamped} sample(s) clamped, elbow at mid-move {elbow:0.000}";
    }

    private static string IkOf(ConstructWorld world, ushort body, CLift l) =>
        world.Get(body) is { } c && l.Part < c.PartCount && c.DefOf(l.Part).Lifter is { } ld ? Ik(ld, l) : "";

    private static Vector3 Grip(LifterDef ld, CLift l, float age)
    {
        var origin = ConstructLifters.Pose(ld, l, age, out var turn);
        if (l.Def == 0 || ConstructParts.Get(l.Def) is not { } d)
            return origin + Vector3.UnitY * ConstructLifters.GripLift;
        var top = new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, d.BoundsMax.Y, (d.BoundsMin.Z + d.BoundsMax.Z) * 0.5f);
        return origin + Vector3.Transform(top, turn) + Vector3.UnitY * ConstructLifters.GripLift;
    }

    /// <summary>
    /// Host (the window tool's "lifter stand" and "lifter pen"): a spot on the ground the piece of a carry that has just begun
    /// will pass over, 60 % into its move; null while no such carry runs. <paramref name="info"/>: the piece's box there.
    /// </summary>
    public static Vector3? PathSpot(ConstructWorld world, out string info)
    {
        info = "";
        foreach (var lb in world.LiftBodies)
        {
            if (world.Get(lb.Body) is not { } c)
                continue;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l is not { State: LiftState.Carry, ToHold: false, WaitFrom: 0 } || l.Def == 0 || l.Part >= c.PartCount || c.DefOf(l.Part).Lifter is not { } ld)
                    continue;
                var age = ConstructLifters.AgeAt(l, ConstructLifters.Now);
                if (age > l.Dur * 0.2f)
                    continue;
                // The first point from 35 % on whose ground is clear of constructs (not on the build) and where the piece is at body height.
                for (var f = 0.25f; f <= 0.95f; f += 0.05f)
                {
                    if (!ConstructLifterPush.PieceBox(c, l, ld, l.Dur * f, out var centre, out var half))
                        break;
                    var ground = World.Map.GroundHeight(centre.X, centre.Z);
                    var feet = new Vector3(centre.X, ground + 0.5f, centre.Z); // (over a floor plate: the build, not the bay's floor, is what rules a point out)
                    if (centre.Y - half.Y - ground > 1.6f || world.BodyIn(feet, World.PlayerHeight - 0.5f))
                    {
                        info = $"the last carry's points are over a construct or above head height (at {f * 100:0} %: {centre.Y - half.Y - ground:0.00} m up)";
                                            continue;
                    }
                    info = $"{ConstructParts.Get(l.Def)?.Name} of body {l.Body} part {l.Part}, {age:0}/{l.Dur} ticks in: at {f * 100:0} % its box spans {centre.Y - half.Y - ground:0.00} to {centre.Y + half.Y - ground:0.00} m over the ground, {half.X * 2:0.00} x {half.Z * 2:0.00} m across";
                    return new Vector3(centre.X, ground, centre.Z);
                }
            }
        }
        return null;
    }

    /// <summary>Host: four Crate Stacks round a spot, their near faces 0.45 m from it (a pen a player cannot be pushed out of).</summary>
    public static int Pen(ConstructWorld world, Vector3 spot)
    {
        if (ConstructParts.Find("Crate Stack") is not { } d)
            return 0;
        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var half = (d.BoundsMax - d.BoundsMin) * 0.5f;
        var n = 0;
        for (var k = 0; k < 4; k++)
        {
            var dir = k switch { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitZ };
            var along = MathF.Abs(dir.X) * half.X + MathF.Abs(dir.Z) * half.Z;
            var at = spot + dir * (0.45f + along) - new Vector3(mid.X, 0, mid.Z);
            if (ConstructBuild.PlaceNew(world, d, new FreePlacement { Pos = at with { Y = spot.Y - d.BoundsMin.Y + 0.02f }, Rot = Quaternion.Identity }) is { } made)
            {
                made.Anchored = true;
                n++;
            }
        }
        return n;
    }
}

/// <summary>
/// An arm's pose from its hand (both peers, the same numbers from the same hand: nothing of it is sent). The turret turns to
/// the wrist; the upper arm and the forearm solve the triangle shoulder, elbow, wrist with the elbow up, in the vertical plane
/// through the wrist. The wrist's distance is clamped to what the links reach (<see cref="LifterDef.LinkMin"/> to
/// <see cref="LifterDef.LinkMax"/>), so the arm is never stretched past straight or folded through itself; the reach test and
/// the carry height keep a move inside that, so the clamp only matters for a pose the path never asks for.
/// </summary>
public static class ArmIk
{
    /// <summary>All in the lifter part's frame. <paramref name="grip"/>: where the gripper bar is wanted.</summary>
    public static void Solve(LifterDef ld, Vector3 grip, out float yaw, out Vector3 elbow, out Vector3 wrist)
    {
        var want = grip + Vector3.UnitY * ConstructLifters.Wrist;
        var v = want - ld.Base;
        var flat = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
        yaw = flat > 1e-3f ? MathF.Atan2(v.Z, v.X) : 0f;
        var len = v.Length();
        var dir = len > 1e-4f ? v / len : new Vector3(1, 0, 0);
        var d = Math.Clamp(len, ld.LinkMin, ld.LinkMax);
        wrist = ld.Base + dir * d;
        var cosA = Math.Clamp((ld.Upper * ld.Upper + d * d - ld.Fore * ld.Fore) / (2 * ld.Upper * d), -1f, 1f);
        var sinA = MathF.Sqrt(1 - cosA * cosA);
        var up = Vector3.UnitY - Vector3.Dot(Vector3.UnitY, dir) * dir; // (up, square to the shoulder-wrist line, in its vertical plane)
        up = up.LengthSquared() > 1e-6f ? Vector3.Normalize(up) : new Vector3(-MathF.Cos(yaw), 0, -MathF.Sin(yaw)); // (wrist straight over or under: the elbow goes back)
        elbow = ld.Base + ld.Upper * (cosA * dir + sinA * up);
    }
}
