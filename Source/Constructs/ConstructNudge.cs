using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Nudge tool (docs/sandbox/CONSTRUCTS.md, "The Nudge tool"): moves and turns whole constructs precisely by a triad's axes
/// and a set of arcs. The numbers and the frame are here, the server's hold in <see cref="ConstructEdits"/> below, the client's
/// side in Program.SandboxNudge.cs and GameSandboxNudge.cs. Unlike the physics gun it pulls nothing: a nudged body is
/// posed, held out of the physics (<see cref="Construct.NudgeBy"/>), so a move lands to the step asked, anchored and frozen
/// bodies stay so, and a selection of bodies moves as one. It shares the physics gun's refusals and its rule for what
/// blocks (only a new overlap, never what a body is joined to). Nothing here allocates.
/// </summary>
public static class Nudge
{
    public const int MaxBodies = 64; // bodies one nudge moves together
    public const float SettleTime = 0.05f; // s: the pose closes on what is asked over this long, so watchers see a smooth move
    public const float MaxSpeed = 30f, MaxTurn = 6f; // m/s, rad/s: the most a nudge moves a tick's worth
    public const float Done = 0.0005f; // m and rad: this near what is asked, it is exactly what is asked
    public const int BoxesPerTick = 128; // boxes tested against the map each tick: a big body goes round them in turn
    public const int EndTicks = 60; // ticks a let-go nudge has to land (a blocked one stops where it is)

    public static readonly float[] Steps = [0f, 0.01f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 4f]; // m: the move step (0: free)
    public static readonly float[] Angles = [1f, 5f, 10f, 15f, 22.5f, 30f, 45f, 90f]; // degrees: the angle snap
    public const float DefaultStep = 0.1f, DefaultAngle = 15f;

    /// <summary>
    /// The triad: Z down along gravity (<paramref name="gravity"/>; none, straight down the world), X and Y square to it.
    /// World: X as near the map's X as Z lets it be. Local: the body's own axes under the same names, so an unturned body's
    /// local triad is the world's.
    /// </summary>
    public static void Axes(Vector3 gravity, bool local, Quaternion rot, out Vector3 x, out Vector3 y, out Vector3 z)
    {
        if (local)
        {
            x = Vector3.Transform(Vector3.UnitX, rot);
            y = Vector3.Transform(Vector3.UnitZ, rot);
            z = Vector3.Transform(-Vector3.UnitY, rot);
            return;
        }
        z = gravity.LengthSquared() > 1e-4f && float.IsFinite(gravity.X + gravity.Y + gravity.Z) ? Vector3.Normalize(gravity) : -Vector3.UnitY;
        x = Vector3.UnitX - z * Vector3.Dot(Vector3.UnitX, z);
        if (x.LengthSquared() < 0.01f)
            x = Vector3.UnitZ - z * Vector3.Dot(Vector3.UnitZ, z);
        x = Vector3.Normalize(x);
        y = Vector3.Cross(z, x);
    }

    /// <summary>A value on steps of <paramref name="step"/> (0: free).</summary>
    public static float Snap(float value, float step) => step > 0 ? MathF.Round(value / step) * step : value;

    /// <summary>The body pose a nudge of <paramref name="move"/> and <paramref name="turn"/> about <paramref name="pivot"/> gives a body that started at <paramref name="pos"/> / <paramref name="rot"/>.</summary>
    public static void Pose(Vector3 pos, Quaternion rot, Vector3 pivot, Vector3 move, Quaternion turn, out Vector3 at, out Quaternion q)
    {
        at = pivot + Vector3.Transform(pos - pivot, turn) + move;
        q = Quaternion.Normalize(turn * rot);
    }

    /// <summary>The angle between two turns (radians).</summary>
    public static float Angle(Quaternion a, Quaternion b) => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f));
}

/// <summary>A nudge under way, on the server (one a player).</summary>
internal sealed class NudgeHold
{
    public readonly Construct[] Bodies = new Construct[Nudge.MaxBodies];
    public readonly Vector3[] StartPos = new Vector3[Nudge.MaxBodies];
    public readonly Quaternion[] StartRot = new Quaternion[Nudge.MaxBodies];
    public int Count, BoxCursor, EndLeft = -1;
    public Vector3 Pivot, Move, WantMove;
    public Quaternion Turn = Quaternion.Identity, WantTurn = Quaternion.Identity;
    public float Radius;
    public bool Blocked;
}

public sealed partial class ConstructEdits
{
    private readonly NudgeHold?[] _nudges = new NudgeHold?[256];

    /// <summary>How far the Nudge tool works from the eye: the physics gun's beam.</summary>
    public float NudgeRange => InfiniteParts ? ConstructGrab.Range : ConstructGrab.SurvivalRange;

    /// <summary>
    /// Why this player can't nudge this construct (or null). The physics gun's refusals (someone holds it or is in it, survival's
    /// mass cap, the contested rule), and what can't be posed; in survival an anchored one stays where it is, a frozen one
    /// may move. In creative anything goes, as the physics gun lifts anything.
    /// </summary>
    public string? NudgeRefusal(Construct c, byte player)
    {
        if (c.GrabbedBy != 255 && c.GrabbedBy != player || c.NudgeBy != 255 && c.NudgeBy != player)
            return "someone else is holding it";
        if (c.CarriedBy != 0)
            return "it rides in a cargo volume: let it go first";
        if (c.SpaceShip != 0)
            return "it is in flight";
        if (AboardWhy(c, player) is { } aboard)
            return aboard;
        if (!InfiniteParts)
        {
            if (c.Anchored && !c.Frozen)
                return "it is anchored to the ground";
            if (c.Mass > ConstructGrab.NudgeMaxMass)
                return $"too heavy to nudge ({c.Mass:0} kg; {ConstructGrab.NudgeMaxMass:0} kg at most in survival)";
        }
        if (SalvageRefusal(c, player) != null)
            return "that construct is someone else's";
        return Contested(c, player);
    }

    /// <summary>Starts a nudge of these bodies about <paramref name="pivot"/>, ending any before it. Null, or why not.</summary>
    public string? NudgeBegin(ConstructWorld world, byte player, Vector3 eye, ReadOnlySpan<ushort> bodies, Vector3 pivot)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Nudge)
            return "take out the Nudge tool";
        if (bodies.Length is 0 or > Nudge.MaxBodies || !float.IsFinite(pivot.X + pivot.Y + pivot.Z))
            return "nothing to nudge";
        NudgeRelease(player);
        var hold = _nudges[player] ??= new NudgeHold();
        hold.Count = 0;
        var far = true;
        var radius = 0f;
        for (var i = 0; i < bodies.Length; i++)
        {
            if (world.Get(bodies[i]) is not { } c || c.PartCount == 0)
                return "that is gone";
            if (NudgeRefusal(c, player) is { } why)
                return why;
            for (var k = 0; k < i; k++)
                if (bodies[k] == bodies[i])
                    return "nothing to nudge";
            var reach = Vector3.Distance(c.WorldCom, pivot) + c.Radius;
            radius = MathF.Max(radius, reach);
            if (Reachable(eye, c.WorldCom, NudgeRange + c.Radius) == null)
                far = false;
        }
        if (far)
            return "too far away";
        if (Vector3.Distance(eye, pivot) > NudgeRange + radius + 2f)
            return "too far away";
        for (var i = 0; i < bodies.Length; i++)
        {
            var c = world.Get(bodies[i])!;
            c.NudgeBy = player;
            c.Vel = c.AngVel = Vector3.Zero;
            hold.Bodies[hold.Count] = c;
            hold.StartPos[hold.Count] = c.Pos;
            hold.StartRot[hold.Count] = c.Rot;
            hold.Count++;
        }
        hold.Pivot = pivot;
        hold.Move = hold.WantMove = Vector3.Zero;
        hold.Turn = hold.WantTurn = Quaternion.Identity;
        hold.Radius = radius;
        hold.Blocked = false;
        hold.BoxCursor = 0;
        hold.EndLeft = -1;
        h.Edit = EditKind.None;
        return null;
    }

    /// <summary>The bodies' mass-weighted centre, as the client makes its pivot (Program.SandboxNudge.cs); null if one is gone (the begin then says so).</summary>
    public static Vector3? NudgeCentre(ConstructWorld world, ReadOnlySpan<ushort> bodies)
    {
        var sum = Vector3.Zero;
        var mass = 0f;
        foreach (var id in bodies)
        {
            if (world.Get(id) is not { PartCount: > 0 } c)
                return null;
            NudgeWeigh(world, c, ref sum, ref mass);
        }
        return mass > 0 ? sum / mass : null;
    }

    /// <summary>
    /// A body's share of a nudge's centre, the same on the server and a client: its mass at its centre, and what its
    /// frozen cargo volumes carry. The server counts cargo in the body's own mass (<see cref="Construct.CargoCount"/>);
    /// a client's copy never has a cargo list (ConstructCargo.Step is the server's), so it adds the carried bodies here.
    /// </summary>
    public static void NudgeWeigh(ConstructWorld world, Construct c, ref Vector3 sum, ref float mass)
    {
        sum += c.WorldCom * MathF.Max(c.Mass, 0.01f);
        mass += MathF.Max(c.Mass, 0.01f);
        if (c.CargoCount > 0 || !c.HasLogic)
            return; // (its cargo is in its mass, or it has no cargo volume to carry any)
        var n = 0;
        foreach (var b in world.Bodies)
            if (b.CarriedBy == c.Id && n++ < ConstructCargo.MaxPerConstruct)
            {
                sum += b.WorldCom * b.Mass;
                mass += b.Mass;
            }
    }

    /// <summary>What the drag asks for now: the whole move and turn since it began.</summary>
    public void NudgeSteer(byte player, Vector3 move, Quaternion turn)
    {
        if (_nudges[player] is not { Count: > 0, EndLeft: < 0 } hold)
            return;
        NudgeWant(hold, move, turn);
    }

    /// <summary>The drag let go, at this move and turn: it lands there, or as near as it gets.</summary>
    public void NudgeEnd(byte player, Vector3 move, Quaternion turn)
    {
        if (_nudges[player] is not { Count: > 0 } hold)
            return;
        NudgeWant(hold, move, turn);
        hold.EndLeft = Nudge.EndTicks;
    }

    private void NudgeWant(NudgeHold hold, Vector3 move, Quaternion turn)
    {
        if (!float.IsFinite(move.X + move.Y + move.Z + turn.X + turn.Y + turn.Z + turn.W) || turn.LengthSquared() < 0.5f)
            return;
        hold.WantMove = move;
        hold.WantTurn = Quaternion.Normalize(turn);
        hold.Blocked = false; // (a new ask: it may be clear that way)
    }

    /// <summary>Every tick, for every player: a nudge under way moves on towards what is asked. Eye null: dead or gone.</summary>
    private void NudgeTick(ConstructWorld world, byte player, Vector3? eye)
    {
        if (_nudges[player] is not { Count: > 0 } hold)
            return;
        ref var h = ref Hands[player];
        if (eye == null || h.Tool != ConstructTool.Nudge || h.Seated)
        {
            NudgeRelease(player);
            return;
        }
        var n = 0;
        for (var i = 0; i < hold.Count; i++)
        {
            var c = hold.Bodies[i];
            if (!ReferenceEquals(world.Get(c.Id), c) || c.PartCount == 0 || AboardWhy(c, player) != null)
            {
                if (c.NudgeBy == player)
                    NudgeFree(c);
                continue; // (it went, or someone unfriendly got in: the rest carry on)
            }
            hold.Bodies[n] = c;
            hold.StartPos[n] = hold.StartPos[i];
            hold.StartRot[n] = hold.StartRot[i];
            n++;
        }
        hold.Count = n;
        if (n == 0 || hold.EndLeft == 0)
        {
            NudgeLand(world, hold, player);
            NudgeRelease(player);
            return;
        }
        if (hold.EndLeft > 0)
            hold.EndLeft--;
        var dt = (float)World.TickDt;
        var gap = hold.WantMove - hold.Move;
        var angle = Nudge.Angle(hold.Turn, hold.WantTurn);
        if (gap.Length() <= Nudge.Done && angle <= Nudge.Done)
        {
            var exact = gap != Vector3.Zero || hold.Turn != hold.WantTurn;
            hold.Move = hold.WantMove;
            hold.Turn = hold.WantTurn;
            if (exact)
                NudgePose(hold, dt);
            else
                NudgeStill(hold);
            if (hold.EndLeft >= 0)
            {
                NudgeLand(world, hold, player);
                NudgeRelease(player);
            }
            return;
        }
        if (hold.Blocked)
        {
            NudgeStill(hold);
            return;
        }
        // Eased towards what is asked, at most the top speed: a step the watchers' interpolation carries smoothly.
        var share = 1f - MathF.Exp(-dt / Nudge.SettleTime);
        var step = gap * share;
        if (step.Length() > Nudge.MaxSpeed * dt)
            step *= Nudge.MaxSpeed * dt / step.Length();
        var turnShare = angle * share > Nudge.MaxTurn * dt ? Nudge.MaxTurn * dt / angle : share;
        var nextMove = hold.Move + step;
        var nextTurn = Quaternion.Normalize(Quaternion.Slerp(hold.Turn, hold.WantTurn, turnShare));
        if (Vector3.Distance(eye.Value, hold.Pivot + nextMove) > NudgeRange + hold.Radius + 2f)
        {
            NudgeBlock(hold, player, "too far away");
            return;
        }
        if (NudgeBlocked(world, player, hold, nextMove, nextTurn))
        {
            // Half the step, so it comes up against what stops it rather than a step short.
            nextMove = hold.Move + step * 0.5f;
            nextTurn = Quaternion.Normalize(Quaternion.Slerp(hold.Turn, nextTurn, 0.5f));
            if (NudgeBlocked(world, player, hold, nextMove, nextTurn))
            {
                NudgeBlock(hold, player, "something is in the way");
                return;
            }
        }
        hold.Move = nextMove;
        hold.Turn = nextTurn;
        NudgePose(hold, dt);
    }

    /// <summary>A drag let go that moved or turned its bodies: a loose one freezes where it landed, to build on (a cancelled drag has moved nothing; nobody is frozen in a driver's seat).</summary>
    private void NudgeLand(ConstructWorld world, NudgeHold hold, byte player)
    {
        if (hold.EndLeft < 0 || (hold.Move == Vector3.Zero && hold.Turn == Quaternion.Identity))
            return;
        for (var i = 0; i < hold.Count; i++)
            if (hold.Bodies[i] is { Frozen: false, Anchored: false, Driver: 255 } c)
                FreezeBody(world, c, player);
    }

    private void NudgeBlock(NudgeHold hold, byte player, string why)
    {
        if (!hold.Blocked)
            Notices.Add((player, why));
        hold.Blocked = true;
        NudgeStill(hold);
    }

    private static void NudgeStill(NudgeHold hold)
    {
        for (var i = 0; i < hold.Count; i++)
        {
            hold.Bodies[i].Vel = hold.Bodies[i].AngVel = Vector3.Zero;
            hold.Bodies[i].Wake();
        }
    }

    /// <summary>Every body at the hold's move and turn, carrying the motion a contact or a rider reads.</summary>
    private static void NudgePose(NudgeHold hold, float dt)
    {
        for (var i = 0; i < hold.Count; i++)
        {
            var c = hold.Bodies[i];
            Nudge.Pose(hold.StartPos[i], hold.StartRot[i], hold.Pivot, hold.Move, hold.Turn, out var at, out var rot);
            c.PrevPos = c.Pos;
            c.PrevRot = c.Rot;
            c.Vel = (at - c.Pos) / dt;
            var d = Quaternion.Normalize(rot * Quaternion.Conjugate(c.Rot));
            if (d.W < 0)
                d = -d;
            c.AngVel = new Vector3(d.X, d.Y, d.Z) * 2f / dt;
            c.Pos = at;
            c.Rot = rot;
            c.Wake();
        }
    }

    /// <summary>Would this pose of the group put any of it newly into another construct, a player or the map? (Only a new overlap blocks: lesson 2026-10-31.)</summary>
    private bool NudgeBlocked(ConstructWorld world, byte player, NudgeHold hold, Vector3 nextMove, Quaternion nextTurn)
    {
        var feet = Feet(world);
        for (var i = 0; i < hold.Count; i++)
        {
            var c = hold.Bodies[i];
            Nudge.Pose(hold.StartPos[i], hold.StartRot[i], hold.Pivot, nextMove, nextTurn, out var at, out var rot);
            var turn = Quaternion.Normalize(rot * Quaternion.Conjugate(c.Rot));
            if (ConstructPhysics.NudgeHits(world, c, player, c.Pos, at, turn))
                return true;
            for (var k = 0; k < feet.Length; k++)
                if (NudgeOnto(c, at, feet[k]))
                    return true;
            if (Query != null && ConstructPhysics.NudgeHitsMap(Query, c, c.Pos, at, turn, ref hold.BoxCursor))
                return true;
        }
        return false;
    }

    /// <summary>Would the body at <paramref name="at"/> come into a player's height at <paramref name="feet"/> it isn't in now? Their standing on it is a ride, which goes with it.</summary>
    private static bool NudgeOnto(Construct c, Vector3 at, Vector3 feet)
    {
        var mid = feet + new Vector3(0, World.PlayerHeight * 0.5f, 0);
        var reach = c.Radius + World.PlayerHeight;
        var com = at + (c.WorldCom - c.Pos);
        if (Vector3.DistanceSquared(com, mid) > reach * reach)
            return false;
        var near = Vector3.Distance(com, mid) < c.Radius * 0.5f + World.PlayerRadius;
        return near && Vector3.Distance(c.WorldCom, mid) >= c.Radius * 0.5f + World.PlayerRadius;
    }

    private static void NudgeFree(Construct c)
    {
        c.NudgeBy = 255;
        c.Vel = c.AngVel = Vector3.Zero;
        c.PrevPos = c.Pos;
        c.PrevRot = c.Rot;
        c.Wake();
    }

    /// <summary>The nudge ends: its bodies go back to the physics where they are, at rest (an anchored or frozen one stays so).</summary>
    public void NudgeRelease(byte player)
    {
        if (_nudges[player] is not { } hold)
            return;
        for (var i = 0; i < hold.Count; i++)
            if (hold.Bodies[i].NudgeBy == player)
                NudgeFree(hold.Bodies[i]);
        Array.Clear(hold.Bodies);
        hold.Count = 0;
        hold.EndLeft = -1;
    }
}
