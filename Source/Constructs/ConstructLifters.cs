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

    public static LifterDef Parse(JsonElement e, string file)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: \"lifter\" is an object");
        foreach (var p in e.EnumerateObject())
            if (p.Name is not ("kind" or "base" or "reach" or "up" or "down" or "payload" or "piece" or "speed" or "handle"))
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
    Stuck, // a piece in the hand nothing takes (phase 4)
}

/// <summary>
/// One lifter part's state (ConstructLifters.cs). The first block is what both peers hold; the move is a pure function of
/// <see cref="Age"/> (ticks into it): the path is worked out the same on every peer from From, To and the def.
/// </summary>
public sealed class CLift
{
    public ushort Body, Part;
    public LiftState State;
    public ushort Def; // the piece in the hand (0: none)
    public uint Paint;
    public Vector3 From, To; // the piece's origin at the pick and at the place, in the lifter part's frame
    public Quaternion FromRot = Quaternion.Identity, ToRot = Quaternion.Identity; // its turn there, in the same frame
    public int Age, Dur; // ticks into the move, and its length
    public ushort Held; // ticks the move waited for a player (phase 3: 0 until then)
    public double At; // ConstructConveyor.Clock when Age was last set or heard (the drawing runs on from it)
    public bool Moved; // a move has been made or heard (else the hand is drawn at its rest)

    // ---- Server only.
    public ushort StationBody;
    public int StationPart = -1, Step = -1;
    public StationJob? Job;
    public StationJob.LiftPlan? Plan; // the job's step places when the step was claimed (a restart makes a new one)
    public bool Fitted; // the place happened; Return is the empty hand's way back
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
/// or ends (protocol 286); clients draw the move from it (LifterRender.cs).
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
        {
            if (!world.LiftSeen.TryGetValue(c.Id, out var lb))
                world.LiftSeen[c.Id] = lb = new LiftBody { Body = c.Id };
            if (lb.Stamp == c.RebuildStamp && lb.Count == c.PartCount)
                continue;
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
        }
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
    }

    // ---- The step (server) -------------------------------------------------------------------------------------------------

    /// <summary>Server, every tick after the stations': nothing while no station is open and no hand is busy.</summary>
    public static void Step(ConstructWorld world)
    {
        var fitTaken = world.FitThisTick;
        StepLifts(world, fitTaken);
        world.FitThisTick = false; // (after: a lifter's own fit sets it too)
    }

    private static void StepLifts(ConstructWorld world, bool fitTaken)
    {
        if (world.StationOpen.Count == 0 && world.LiftBusy == 0)
            return;
        world.LiftTick++;
        if (world.LiftTick % ScanEvery == 1)
            Scan(world, server: true);
        if (world.LiftTick % Every != 0)
            return;
        var busy = 0;
        var decisions = 0;
        var now = ConstructConveyor.Clock;
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
                    busy += l.State == LiftState.Idle ? 0 : 1; // (it stops where it is, and goes on when power returns)
                    continue;
                }
                switch (l.State)
                {
                    case LiftState.Idle:
                        if (world.StationOpen.Count > 0 && decisions < DecisionsAStep)
                        {
                            decisions++;
                            if (Claim(world, c, l, ld))
                                world.LiftChanged(c.Id);
                        }
                        break;
                    case LiftState.Carry:
                        if (!ClaimHolds(world, l))
                        {
                            Drop(world, c, l, lost: false);
                            world.LiftChanged(c.Id);
                            break;
                        }
                        l.Age = Math.Min(l.Dur, l.Age + Every);
                        l.At = now;
                        if (l.Age >= l.Dur && !fitTaken)
                        {
                            fitTaken = true;
                            Fit(world, c, l, ld);
                            world.LiftChanged(c.Id);
                        }
                        break;
                    case LiftState.Return:
                        l.Age = Math.Min(l.Dur, l.Age + Every);
                        l.At = now;
                        if (l.Age >= l.Dur)
                        {
                            l.State = LiftState.Idle;
                            world.LiftChanged(c.Id);
                        }
                        break;
                }
                if (l.State != LiftState.Idle)
                    busy++;
            }
        }
        world.LiftBusy = busy;
    }

    /// <summary>The shoulder in the world, and the lifter part's world frame.</summary>
    public static void Frame(Construct c, int part, LifterDef ld, out Vector3 pos, out Quaternion rot, out Vector3 shoulder)
    {
        Freeform.PartWorld(c, part, out pos, out rot);
        shoulder = pos + Vector3.Transform(ld.Base, rot);
    }

    /// <summary>Is a world point in the ring round the shoulder? <paramref name="far"/>: how far it is across.</summary>
    public static bool InReach(LifterDef ld, Vector3 shoulder, Quaternion rot, Vector3 at, out float far)
    {
        var d = Vector3.Transform(at - shoulder, Quaternion.Conjugate(rot));
        far = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        return far >= ld.ReachMin - 0.01f && far <= ld.ReachMax + 0.01f && d.Y <= ld.Up + 0.01f && d.Y >= -ld.Down - 0.01f;
    }

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
                if (!InReach(ld, shoulder, lr, hold, out _))
                    continue;
                var k = ConstructStation.LiftOffer(j, tray, ld, shoulder, lr);
                if (k < 0)
                    continue;
                var d = ConstructParts.Find(j.Bp!.Steps[k].Part)!;
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
                l.Dur = Ticks(ld, l.From, l.To, true);
                l.At = ConstructConveyor.Clock;
                l.State = LiftState.Carry;
                LifterProbe.CarriesHeard++; // (the host counts its own; a client counts what it hears)
                l.Moved = true;
                l.Fitted = false;
                (l.StationBody, l.StationPart, l.Step, l.Job, l.Plan) = (sc.Id, t, k, j, plan);
                return true;
            }
        }
        return false;
    }

    /// <summary>The point of a station's hold nearest a shoulder: the hold is the half of the station on its bay side.</summary>
    private static Vector3 HoldPoint(Construct sc, int t, Vector3 shoulder)
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
        l.Dur = Ticks(ld, from, to, false);
        l.At = ConstructConveyor.Clock;
        l.State = LiftState.Return;
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
        if (l.State != LiftState.Carry || l.Fitted || l.Def == 0)
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
            return;
        }
        Back(c, l, lf, sc, l.StationPart);
    }

    // ---- The path (both peers) ---------------------------------------------------------------------------------------------

    /// <summary>The move's length in ticks: half the handling at each end, the path at the hand's speed.</summary>
    public static int Ticks(LifterDef ld, Vector3 from, Vector3 to, bool loaded)
    {
        var len = PathLength(ld, from, to);
        var sec = (loaded ? ld.Handle : ld.Handle * 0.25f) + len / ld.Speed;
        return Math.Clamp((int)MathF.Ceiling(sec * World.TickRate), Every, 60 * World.TickRate);
    }

    private static float CarryY(LifterDef ld, Vector3 from, Vector3 to) =>
        MathF.Min(ld.Base.Y + ld.Up, MathF.Max(from.Y, to.Y) + CarryRise);

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
    public static float PathLength(LifterDef ld, Vector3 from, Vector3 to)
    {
        var h = CarryY(ld, from, to);
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
        var h = CarryY(ld, from, to);
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

    /// <summary>Ticks into the move as this peer draws it now (it runs on from the last word, to the move's end).</summary>
    public static float AgeNow(CLift l) =>
        l.State is LiftState.Carry or LiftState.Return ? MathF.Min(l.Dur, l.Age + (float)((ConstructConveyor.Clock - l.At) * World.TickRate)) : l.Dur;

    // ---- The wire (CBodyKind.Lifts, protocol 286) --------------------------------------------------------------------------

    private static ushort Cm(float v) => unchecked((ushort)(short)Math.Clamp(MathF.Round(v * 100), short.MinValue, short.MaxValue));
    private static float FromCm(ushort v) => unchecked((short)v) / 100f;
    private static ushort Unit(float v) => unchecked((ushort)(short)Math.Clamp(MathF.Round(v * 32767), -32767, 32767));
    private static float FromUnit(ushort v) => unchecked((short)v) / 32767f;

    private static void Vec(PacketWriter w, Vector3 v) => w.U16(Cm(v.X)).U16(Cm(v.Y)).U16(Cm(v.Z));
    private static void Rot(PacketWriter w, Quaternion q) => w.U16(Unit(q.X)).U16(Unit(q.Y)).U16(Unit(q.Z)).U16(Unit(q.W));

    /// <summary>A body's lifters, the whole list (a move started or ended; a joiner): about 44 bytes a lifter.</summary>
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
            w.U16((ushort)Math.Min(l.Dur, ushort.MaxValue)).U16((ushort)Math.Min(l.Age, ushort.MaxValue)).U16(l.Held);
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
        var now = ConstructConveyor.Clock;
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
            int dur = r.U16(), age = r.U16();
            var held = r.U16();
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
            (l.State, l.Moved, l.Def, l.Paint, l.From, l.To, l.FromRot, l.ToRot, l.Dur, l.Age, l.Held, l.At) = (state, moved, def, paint, from, to, fr, tr, dur, age, held, now);
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
            if (!ConstructLifters.InReach(ld, shoulder, rot, j.Pos + Vector3.Transform(plan.Centre[k], j.Rot), out _))
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
                sb.Append($" [body {lb.Body} part {l.Part}: {l.State}{(l.Def != 0 ? " " + (ConstructParts.Get(l.Def)?.Name ?? "?") : "")} {l.Age}/{l.Dur} ticks]");
            }
        return $"{n} lifter(s), {CarriesHeard} carries seen, {Fits} fitted by lifters (host count){sb}";
    }

    /// <summary>Host: a Loader Arm on the ground behind the station's hold, inside its bay's back corner, turned as the station is.</summary>
    public static string Place(ConstructWorld world, Construct sc, int t)
    {
        if (ConstructParts.Find("Loader Arm") is not { Lifter: { } ld } d)
            return "no Loader Arm part";
        var sd = sc.DefOf(t);
        Freeform.PartWorld(sc, t, out var sp, out var sr);
        var centre = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var local = new Vector3(sd.BoundsMax.X + ConstructStation.BayGap + 0.9f - centre.X, sd.BoundsMin.Y - d.BoundsMin.Y + 0.02f, sd.BoundsMin.Z - 0.9f - centre.Z);
        var made = ConstructBuild.PlaceNew(world, d, new FreePlacement { Pos = sp + Vector3.Transform(local, sr), Rot = sr });
        return made == null ? "no room for the arm" : $"a Loader Arm stands at the bay's back corner (construct {made.Id}), shoulder {ld.Base}";
    }
}
