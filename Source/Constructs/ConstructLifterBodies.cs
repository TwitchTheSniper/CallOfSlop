using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Lifters carrying a finished submodule (docs/sandbox/design/sub-blueprints.md section 4, the user's answer 7): a Loader Arm within
/// its payload and largest piece, or a Bridge Crane, whose reach holds a sub-job's finished body and its use's spot on the main
/// station's build carries the body there; the main station's next look takes it for the big fit. The hand moves on its usual
/// scripted path (both peers pose the arm from the lift word, an empty hand: no new bytes); the server poses the body on the hand
/// each tick, frozen, and clients see it through its poses. A body whose station let go of it, or that went, ends the carry.
/// </summary>
public static partial class ConstructLifters
{
    /// <summary>
    /// Server: a lifter of construct <paramref name="c"/> that is idle, powered, not manned, whose payload and largest piece take the
    /// body and whose reach holds the body where it is and at <paramref name="toPos"/>, <paramref name="toRot"/>, starts carrying it
    /// there. True and the lifter's name, or false and why none does.
    /// </summary>
    public static bool CarryBody(ConstructWorld world, Construct c, Construct body, Vector3 toPos, Quaternion toRot, out string who, out string why)
    {
        who = "";
        why = "no Loader Arm or Bridge Crane on this construct";
        if (!world.LiftSeen.TryGetValue(c.Id, out var lb) || lb.N == 0)
            return false;
        if (body.Dirty)
            body.Rebuild();
        var size = body.LocalMax - body.LocalMin;
        var longest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        var across = size.X + size.Y + size.Z - longest - MathF.Min(size.X, MathF.Min(size.Y, size.Z));
        var com = Vector3.Transform(body.WorldCom - body.Pos, Quaternion.Conjugate(body.Rot)); // (the body's middle in its own frame)
        var toCom = toPos + Vector3.Transform(com, toRot);
        var heavy = 0;
        var big = 0;
        var far = 0;
        for (var i = 0; i < lb.N; i++)
        {
            var l = lb.Lifts[i];
            if (l.Part >= c.PartCount || DefOf(c, l.Part) is not { } ld)
                continue;
            if (l.State != LiftState.Idle || l.CabPart >= 0 || l.Steered || l.CarryBody != 0 || !ConstructRecycler.Powered(world, c, l.Part)
                || (int)ConstructConfig.Get(c.Parts[l.Part], c.DefOf(l.Part), CfgKey.LiftMode, 0) == 3)
                continue;
            if (body.Mass > ld.Payload)
            {
                heavy++;
                continue;
            }
            if (longest > ld.PieceLong + 0.005f || across > ld.PieceAcross + 0.005f)
            {
                big++;
                continue;
            }
            Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
            if (!InReach(ld, shoulder, lr, body.WorldCom, SafeLift, out _) || !InReach(ld, shoulder, lr, toCom, SafeLift, out _))
            {
                far++;
                continue;
            }
            var inv = Quaternion.Conjugate(lr);
            l.Def = 0;
            l.Paint = 0;
            l.From = Vector3.Transform(body.Pos - lp, inv);
            l.FromRot = Quaternion.Normalize(inv * body.Rot);
            l.To = Vector3.Transform(toPos - lp, inv);
            l.ToRot = Quaternion.Normalize(inv * toRot);
            l.Age = 0;
            l.Rise = Math.Clamp(RiseOf(c, l.Part, ld) + size.Y * 0.5f, 0, ld.Bridge ? ld.Down : ld.Up); // (over the higher end by half its height more: it clears what it passes more often)
            l.Dur = Ticks(ld, l.From, l.To, true, SafeLift, l.Rise);
            l.Why = LiftWhy.None;
            (l.ToHold, l.ToKind, l.Job, l.Plan, l.Step, l.StationBody) = (false, LiftPointKind.None, null, null, -1, 0);
            Begin(l);
            l.State = LiftState.Carry;
            l.Moved = true;
            l.Fitted = false;
            l.CarryBody = body.Id;
            body.Frozen = true;
            body.HeldBy = c.Id;
            body.Vel = body.AngVel = Vector3.Zero;
            if (!world.BodyCarries.Contains(l))
                world.BodyCarries.Add(l);
            LifterProbe.CarriesHeard++;
            world.LiftChanged(c.Id);
            who = c.DefOf(l.Part).Name;
            Console.WriteLine($"[lifter] {who} {l.Part} carries submodule body {body.Id} ({body.PartCount} parts, {body.Mass:0} kg) to its main station: {l.Dur / (float)World.TickRate:0.0} s");
            return true;
        }
        why = heavy > 0 ? $"too heavy for {(heavy == 1 ? "the lifter" : "every lifter")} that could take it: {body.Mass:0} kg"
            : big > 0 ? $"too big for the lifters' hands: {longest:0.0} m long"
            : far > 0 ? "no Loader Arm or Bridge Crane reaches both its station's bay and its spot" : "every lifter is busy";
        return false;
    }

    /// <summary>The lifter carrying body <paramref name="body"/>, by name, or null.</summary>
    public static string? CarryingBody(ConstructWorld world, ushort body)
    {
        foreach (var l in world.BodyCarries)
            if (l.CarryBody == body && world.Get(l.Body) is { } c && l.Part < c.PartCount)
                return c.DefOf(l.Part).Name;
        return null;
    }

    /// <summary>Server, every tick: each carried body posed on its hand; at the move's end it is set at its spot and the hand goes back.</summary>
    public static void PoseBodies(ConstructWorld world)
    {
        if (world.BodyCarries.Count == 0)
            return;
        Now = MoverPhysics.Clock;
        for (var k = world.BodyCarries.Count - 1; k >= 0; k--)
        {
            var l = world.BodyCarries[k];
            var c = world.Get(l.Body);
            var ld = c != null && l.Part < c.PartCount ? DefOf(c, l.Part) : null;
            var body = world.Get(l.CarryBody);
            if (c == null || ld == null || l.State != LiftState.Carry || body == null || body.HeldBy != c.Id)
            {
                // (the body went, or its station let go of it, or the lifter did: the carry ends where it is)
                if (body != null && c != null && body.HeldBy == c.Id)
                    body.HeldBy = 0;
                l.CarryBody = 0;
                world.BodyCarries.RemoveAt(k);
                if (c != null && ld != null && l.State == LiftState.Carry)
                {
                    Back(c, l, ld, null, -1);
                    world.LiftChanged(c.Id);
                }
                continue;
            }
            Frame(c, l.Part, ld, out var lp, out var lr, out _);
            var age = AgeAt(l, Now);
            var at = Pose(ld, l, age, out var rot);
            body.Pos = body.PrevPos = body.NetPos = body.NetFromPos = lp + Vector3.Transform(at, lr);
            body.Rot = body.PrevRot = body.NetRot = body.NetFromRot = Quaternion.Normalize(lr * rot);
            body.Vel = body.AngVel = Vector3.Zero;
            body.Wake(); // (an awake body's pose goes to clients every pose packet)
            if (age < l.Dur || l.WaitFrom != 0)
                continue;
            // At its spot: the main station's next look takes it (ConstructStationTree.Watch: a body in its bay).
            l.CarryBody = 0;
            world.BodyCarries.RemoveAt(k);
            l.Moves++;
            Back(c, l, ld, null, -1);
            world.LiftChanged(c.Id);
            world.StructureDirty.Add(body.Id);
        }
    }
}

public sealed partial class ConstructWorld
{
    public readonly List<CLift> BodyCarries = new(4); // server: lifters carrying a submodule body (ConstructLifterBodies.cs)
}
