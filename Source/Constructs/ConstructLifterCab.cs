using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Lifters phase 8 (docs/sandbox/design/arms-and-cranes.md 3.7, the user's answer 6): the Lifter Cab. A seat part (<c>lifterCab</c>)
/// steers the nearest lifter on its own construct (found when the body's parts change, on both peers). Manned, the lifter finishes the
/// move it is in, then answers only the cab: at the 10 Hz step the hook moves toward where the seat's keys push it, in the sitter's view
/// (W/S away and towards, A/D sideways, Space and Ctrl up and down) at the lifter's speeds, clamped to its reach; F grabs a loose
/// one-part body or a pick point's piece under the hook (within 1.5 m) and sets it down at a put point that takes it, else loose where
/// it hangs; E leaves. The hand's moves go out in the lift word (10 Hz while it moves; a client eases between them as a straight move).
/// Leaving the seat with a piece: the lifter's own rule takes it on (a taker in reach, else it waits with it). Nothing allocates in the step.
/// </summary>
public static partial class ConstructLifters
{
    public const float CabGrabReach = 1.5f; // m from the hook

    /// <summary>Both peers, on a body's scan: each lifter's cab (the nearest Lifter Cab on its construct), or -1.</summary>
    private static void FindCabs(Construct c, LiftBody lb)
    {
        for (var i = 0; i < lb.N; i++)
            lb.Lifts[i].CabPart = -1;
        for (var p = 0; p < c.PartCount; p++)
        {
            if (!c.DefOf(p).LifterCab)
                continue;
            Freeform.PartWorld(c, p, out var cp, out _);
            CLift? best = null;
            var bestD = float.MaxValue;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l.CabPart >= 0 || l.Part >= c.PartCount || c.DefOf(l.Part).Lifter == null)
                    continue; // (one cab a lifter: the first cab found keeps it)
                Freeform.PartWorld(c, l.Part, out var lp, out _);
                var dd = Vector3.DistanceSquared(lp, cp);
                if (dd < bestD)
                    (best, bestD) = (l, dd);
            }
            if (best != null)
                best.CabPart = p;
        }
    }

    /// <summary>Is this lifter's cab manned (server and client: the seat's sitter)?</summary>
    public static bool Manned(Construct c, CLift l) => l.CabPart >= 0 && l.CabPart < c.PartCount && !c.Parts[l.CabPart].Broken && c.Parts[l.CabPart].SitterPlus1 != 0;

    /// <summary>The lifter a cab steers, or null (either peer).</summary>
    public static CLift? CabLifter(ConstructWorld world, Construct c, int cab)
    {
        if (!world.LiftSeen.TryGetValue(c.Id, out var lb))
            return null;
        for (var i = 0; i < lb.N; i++)
            if (lb.Lifts[i].CabPart == cab)
                return lb.Lifts[i];
        return null;
    }

    /// <summary>
    /// Server, the 10 Hz step, for a lifter with a cab: true when the cab has it (the caller skips the lifter's own work). Takes over an
    /// idle or stuck lifter, steers it, grabs and sets down; gives it back when the cab is left.
    /// </summary>
    private static bool CabStep(ConstructWorld world, IWorldQuery? query, Construct c, CLift l, LifterDef ld)
    {
        var manned = Manned(c, l);
        if (!manned)
        {
            if (l.Steered)
                CabLeave(world, c, l, ld);
            return false;
        }
        if (!l.Steered)
        {
            if (l.State is LiftState.Carry or LiftState.Return)
                return false; // (it finishes the move it is in)
            var hand = l.Moved ? l.To : RestOf(ld);
            (l.Steered, l.From, l.To, l.FromRot, l.Moved) = (true, hand, hand, l.ToRot, true);
            (l.ToKind, l.ToHold, l.Step, l.Job, l.Plan, l.Fitted, l.OrderId) = (LiftPointKind.None, false, -1, null, null, false, 0);
            l.State = l.Def != 0 ? LiftState.Carry : LiftState.Return;
            l.Why = LiftWhy.Manned;
            SteerBegin(l, hand);
            world.LiftChanged(c.Id);
            Console.WriteLine($"[lifter] body {c.Id} part {l.Part}: its cab is manned");
        }
        if (l.GrabAsk)
        {
            l.GrabAsk = false;
            CabGrab(world, query, c, l, ld);
            world.LiftChanged(c.Id);
        }
        var cp = c.Parts[l.CabPart];
        var keys = (Buttons)(cp.Keys & 0xFF);
        Vector3 move = default;
        var fwd = World.Forward(cp.AimYaw, 0);
        fwd.Y = 0;
        fwd = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : Vector3.UnitZ;
        var right = Vector3.Cross(fwd, Vector3.UnitY);
        if ((keys & Buttons.Forward) != 0)
            move += fwd;
        if ((keys & Buttons.Back) != 0)
            move -= fwd;
        if ((keys & Buttons.Right) != 0)
            move += right;
        if ((keys & Buttons.Left) != 0)
            move -= right;
        var dt = Every / (float)World.TickRate;
        if (move.LengthSquared() > 1e-6f)
            move = Vector3.Normalize(move) * ld.Speed * dt;
        var vertical = (keys & Buttons.Jump) != 0 ? 1f : (keys & (Buttons.Crouch | Buttons.Prone)) != 0 ? -1f : 0f;
        move.Y = vertical * (ld.Bridge ? ld.Hoist : ld.Speed) * dt;
        if (l.PlayerWait)
        {
            if (!ConstructLifterPush.Clear(world, c, l, ld))
                move = default; // (a player its piece would pin: it holds still until they are clear)
            else
                (l.PlayerWait, l.WaitFrom) = (false, 0);
        }
        var was = l.To;
        var next = was;
        if (move != default)
        {
            Frame(c, l.Part, ld, out _, out var lr, out _);
            next = Clamp(ld, l, was + Vector3.Transform(move, Quaternion.Conjugate(lr)));
        }
        var moving = Vector3.DistanceSquared(next, was) > 1e-8f;
        if (moving || Vector3.DistanceSquared(l.From, l.To) > 1e-8f)
        {
            // (a step's move, straight, over the next 10 Hz step: From where the hand is, To where it goes; a still hand sends one last word)
            l.From = was;
            l.To = next;
            l.Start = Now;
            l.Dur = Every;
            (l.Held, l.WaitFrom) = (0, 0u);
            world.LiftChanged(c.Id);
        }
        return true;
    }

    private static void SteerBegin(CLift l, Vector3 hand)
    {
        (l.From, l.To) = (hand, hand);
        (l.Start, l.Dur, l.Held, l.WaitFrom, l.Age) = (Now, Every, 0, 0u, Every);
    }

    /// <summary>The hand's place kept in the lifter's reach (its part frame; a piece's origin, its middle for the box).</summary>
    private static Vector3 Clamp(LifterDef ld, CLift l, Vector3 p)
    {
        var lift = l.Def != 0 ? WristLift(ConstructParts.Get(l.Def)) : SafeLift;
        var d = p - ld.Base;
        if (ld.Bridge)
        {
            d.X = Math.Clamp(d.X, ld.AcrossMin, MathF.Max(ld.AcrossMin, ld.AcrossMax));
            d.Z = Math.Clamp(d.Z, ld.RunLo, MathF.Max(ld.RunLo, ld.RunHi));
            d.Y = Math.Clamp(d.Y, -ld.Down, MathF.Max(-ld.Down, -ld.Depth - lift));
            return ld.Base + d;
        }
        var far = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        if (far > 1e-4f)
        {
            var k = Math.Clamp(far, ld.ReachMin, ld.ReachMax) / far;
            (d.X, d.Z) = (d.X * k, d.Z * k);
        }
        else
            d.X = ld.ReachMin;
        d.Y = Math.Clamp(d.Y, -ld.Down, ld.Up);
        // (the two links: a wrist out of their range stays where it was)
        far = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        var wrist = MathF.Sqrt(far * far + (d.Y + lift) * (d.Y + lift));
        return wrist <= ld.LinkMax && wrist >= ld.LinkMin ? ld.Base + d : l.To;
    }

    /// <summary>The cab was left (or went): the lifter's own work takes the hand on from where it is.</summary>
    private static void CabLeave(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        l.Steered = false;
        l.PlayerWait = false;
        var hand = l.To;
        (l.From, l.FromRot) = (hand, l.ToRot);
        Console.WriteLine($"[lifter] body {c.Id} part {l.Part}: its cab was left{(l.Def != 0 ? " with a piece in the hand" : "")}");
        if (l.Def != 0 && ConstructParts.Get(l.Def) is { } d)
        {
            (l.State, l.Age, l.Dur) = (LiftState.Stuck, Every, Every);
            if (!Redirect(world, c, l, ld, d))
                (l.State, l.Why, l.Age, l.Dur, l.ToKind) = (LiftState.Stuck, LiftWhy.NoTaker, Every, Every, LiftPointKind.None);
        }
        else
            (l.State, l.Why, l.Def, l.Age, l.Dur) = (LiftState.Idle, LiftWhy.None, 0, Every, Every);
        world.LiftChanged(c.Id);
    }

    /// <summary>Server: F in the cab. An empty hand grabs what is under the hook; a full one sets its piece down.</summary>
    private static void CabGrab(ConstructWorld world, IWorldQuery? query, Construct c, CLift l, LifterDef ld)
    {
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        var inv = Quaternion.Conjugate(lr);
        var hook = lp + Vector3.Transform(l.To, lr); // (where the hand goes this step: the piece's origin)
        if (l.Points == null || world.LiftTick - l.PointsAt >= PointEvery)
            FindPoints(world, c, l, ld);
        if (l.Def == 0)
        {
            // A loose one-part body under the hook, nearest first.
            Construct? best = null;
            var bestD = CabGrabReach * CabGrabReach;
            var many = false;
            foreach (var lo in world.Bodies)
            {
                if (lo == c || lo.Anchored || lo.HeldBy != 0 || lo.GrabbedBy != 255 || lo.CarriedBy != 0 || lo.Driver != 255)
                    continue;
                var dd = Vector3.DistanceSquared(lo.WorldCom, hook);
                if (dd > bestD + lo.Radius * lo.Radius)
                    continue;
                if (lo.PartCount != 1)
                {
                    many = true;
                    continue;
                }
                if (!lo.Loose || dd >= bestD)
                    continue;
                (best, bestD) = (lo, dd);
            }
            if (best != null)
            {
                var d = best.DefOf(0);
                if (!Carries(ld, d))
                {
                    CabSay(l, LiftWhy.CabTooBig, CarryRefusal(ld, d));
                    return;
                }
                Freeform.PartWorld(best, 0, out var bp, out var br);
                (l.Health, l.Paint) = ((byte)Math.Clamp(255 * best.Parts[0].Health / MathF.Max(1, d.Health), 1, 255), best.Parts[0].Paint);
                world.Remove(best);
                CabHold(l, d, Clamp(ld, l, Vector3.Transform(bp - lp, inv)), Quaternion.Normalize(inv * br));
                CabSay(l, LiftWhy.CabGrabbed, $"took the {d.Name}");
                return;
            }
            // A pick point's piece: a rack or pad under the hook, a machine's tray.
            for (var k = 0; k < l.PointN; k++)
            {
                var pt = l.Points![k];
                if (!Picks(pt.Kind) || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount || world.StoreOf(b, pt.Part) is not { } st)
                    continue;
                if (pt.Kind == LiftPointKind.Rack)
                {
                    for (var q = 0; q < st.KindN && st.KindDef != null; q++)
                    {
                        if (ConstructCrafting.Available(world, st, st.KindDef[q]) <= 0 || ConstructParts.Get(st.KindDef[q]) is not { } d || !Carries(ld, d))
                            continue;
                        if (!RackSpot(b, pt.Part, ld, shoulder, lr, SafeLift, WristLift(d), out var at) || Vector3.Distance(at, hook) > CabGrabReach + 1f)
                            continue;
                        if (!world.StationTake(b, pt.Part, st, d))
                            continue;
                        world.CraftChanged(b);
                        at.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
                        Freeform.PartWorld(b, pt.Part, out _, out var rr);
                        var rot = Quaternion.Normalize(inv * rr);
                        (l.Health, l.Paint) = (255, 0);
                        CabHold(l, d, Clamp(ld, l, Vector3.Transform(at - Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, rr) - lp, inv)), rot);
                        CabSay(l, LiftWhy.CabGrabbed, $"took a {d.Name} from the {b.DefOf(pt.Part).Name}");
                        return;
                    }
                }
                else if (PointAt(b, pt, out var outAt) && Vector3.Distance(outAt, hook) <= CabGrabReach + 1f && (b.DefOf(pt.Part).IsPress || b.DefOf(pt.Part).IsAssembler))
                    for (var q = 0; q < st.SlotCount; q++)
                        if (st.Count[q] > 0 && ConstructParts.Get(st.Def[q]) is { } d && Carries(ld, d) && ConstructCrafting.Available(world, st, (ushort)d.Id) > 0
                            && ConstructCrafting.TakeOne(world, b, pt.Part, st, d))
                        {
                            world.CraftChanged(b);
                            Freeform.PartWorld(b, pt.Part, out _, out var mr);
                            (l.Health, l.Paint) = (255, 0);
                            CabHold(l, d, Clamp(ld, l, Vector3.Transform(outAt - lp, inv)), Quaternion.Normalize(inv * mr));
                            CabSay(l, LiftWhy.CabGrabbed, $"took a {d.Name} from the {b.DefOf(pt.Part).Name}");
                            return;
                        }
            }
            CabSay(l, LiftWhy.CabNothing, many ? "only a single piece: that is a construct" : "nothing to take under the hook");
            return;
        }
        if (ConstructParts.Get(l.Def) is not { } pd)
            return;
        // Set down: a put point near the hook that takes it, else loose where it hangs.
        var mid = hook + Vector3.Transform((pd.BoundsMin + pd.BoundsMax) * 0.5f, lr * l.ToRot);
        for (var k = 0; k < l.PointN; k++)
        {
            var pt = l.Points![k];
            if (!Puts(pt.Kind) || world.Get(pt.Body) is not { } b || !OrderPut(world, l, ld, shoulder, lr, b, pt, pd, out var at) || Vector3.Distance(at, mid) > CabGrabReach + 1f)
                continue;
            if (!PutPiece(world, query, b, pt.Kind, pt.Part, pt.Bay, pd, l.Health, l.Paint))
                continue;
            l.Moves++;
            CabSay(l, LiftWhy.CabPut, $"put the {pd.Name} into the {b.DefOf(pt.Part).Name}");
            CabEmpty(l);
            return;
        }
        var loose = ConstructEdits.DropLoose(world, pd, mid, lr * l.ToRot);
        if (loose == null)
        {
            CabSay(l, LiftWhy.CabRefused, "no room to set it down here");
            return;
        }
        if (l.Health < 255)
            loose.Parts[0].Health = pd.Health * l.Health / 255f;
        loose.Parts[0].Paint = l.Paint;
        l.Moves++;
        CabSay(l, LiftWhy.CabDropped, $"set the {pd.Name} down");
        CabEmpty(l);
    }

    private static void CabHold(CLift l, ConstructPartDef d, Vector3 origin, Quaternion rot)
    {
        l.Def = (ushort)d.Id;
        (l.FromRot, l.ToRot, l.State) = (rot, rot, LiftState.Carry);
        (l.From, l.To) = (origin, origin);
        (l.Start, l.Dur) = (Now, Every);
        LifterProbe.CarriesHeard++;
    }

    private static void CabEmpty(CLift l)
    {
        (l.Def, l.Paint, l.Health, l.State) = (0, 0u, (byte)255, LiftState.Return);
        (l.From, l.Start, l.Dur) = (l.To, Now, Every);
    }

    private static void CabSay(CLift l, LiftWhy why, string text)
    {
        l.Why = why;
        Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: cab: {text}");
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>Server: F in a Lifter Cab (CActKind.Lifter, Grab): the player must sit in that cab. The grab or set-down runs on the lifter's next step.</summary>
    public string? CabGrab(ConstructWorld world, byte player, ushort body, ushort cab)
    {
        ref var h = ref Hands[player];
        if (!h.Seated || h.SeatBody != body || h.SeatPart != cab || world.Get(body) is not { } c || cab >= c.PartCount || !c.DefOf(cab).LifterCab)
            return "sit in the Lifter Cab first";
        if (ConstructLifters.CabLifter(world, c, cab) is not { } l)
            return "this cab has no lifter on its construct to steer";
        if (!l.Steered)
            return "the lifter is finishing its move: a moment";
        l.GrabAsk = true;
        return null;
    }
}
