using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct run-over" (board dev-sandbox-runover-damage-rd01; docs/sandbox/CONSTRUCTS.md "Passengers, riding and
    /// running people over"): run-over damage comes from the construct's own motion at the contact (linear and turning,
    /// and its joints'), never the player's. A player sprinting into a parked buggy takes nothing and is stopped; a buggy
    /// nudging a player at a walk pushes them without damage; faster than the threshold the damage and knockback grow with
    /// the speed and the mass; a player dropping onto a parked buggy only lands (fall damage is the mover's, as anywhere);
    /// a gate's leaf swung fast on its motor hinge hurts. Headless, through <see cref="ConstructWorld.BlockPlayer"/> and the
    /// game's damage rule, on the yard.
    /// </summary>
    private static void CheckConstructRunOver()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"construct run-over: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var w = new ConstructWorld();
        if (Buggy(w, -27, 0, 0) is not { } car)
            return;
        StepConstructs(w, 90); // (settled on its springs)
        car.Vel = Vector3.Zero;
        car.AngVel = Vector3.Zero;
        var fwd = Vector3.Transform(car.SeatForward(), car.Rot);
        fwd = Vector3.Normalize(fwd with { Y = 0 });
        var groundY = ConstructWorld.Ground(World.Query, car.WorldCom.X, car.WorldCom.Z).Y;
        var height = World.PlayerHeight;
        var mass = car.Mass;

        // 1. Sprinting (7 m/s) into the parked buggy's side: stopped, no damage (the old rule took the player's speed as the impact).
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, fwd));
        var s = new MoveState { Position = new Vector3(car.WorldCom.X, groundY, car.WorldCom.Z) + side * 3f, Velocity = -side * 7f, Grounded = true };
        var worst = 0f;
        Construct? stoodOn = null;
        for (var t = 0; t < 40; t++)
        {
            s.Position += s.Velocity * World.TickDt;
            stoodOn ??= w.BlockPlayer(ref s, height, server: true, who: 1);
            worst = MathF.Max(worst, w.LastHitSpeed);
            if (stoodOn != null)
                break;
        }
        var into = Vector3.Dot(s.Velocity, -side);
        var sprintDamage = Game.RunOverDamage(worst, mass);
        var oldRule = Game.RunOverDamage(7f, mass);
        Line($"run-over: sprinting at 7 m/s into a parked {mass:0} kg buggy: hit speed {worst:0.00} m/s, {sprintDamage} damage (the old closing-speed rule: {oldRule}); still moving into it at {into:0.00} m/s{(stoodOn != null ? " (stepped up onto it)" : "")}");
        if (sprintDamage != 0 || worst > 0.01f)
            Fail("construct run-over: a player running into a parked vehicle was hurt (the player's own speed counted)");
        if (stoodOn == null && into > 0.05f)
            Fail("construct run-over: a player running into a parked vehicle wasn't stopped");

        // 2. The buggy creeping into a standing player at 2.5 m/s: pushed along, no damage. 3. Faster: damage and knockback grow.
        (float Hit, int Damage, float Pushed, float Knock) Nudge(float speed)
        {
            // The player walks up to the parked buggy's side and stands there, touching it.
            car.Vel = Vector3.Zero;
            car.AngVel = Vector3.Zero;
            var p = new MoveState { Position = new Vector3(car.WorldCom.X, groundY, car.WorldCom.Z) + side * 3f, Velocity = -side * 3f, Grounded = true };
            for (var t = 0; t < 90 && Vector3.Dot(p.Velocity, -side) > 0; t++)
            {
                p.Position += p.Velocity * World.TickDt;
                w.BlockPlayer(ref p, height, server: true, who: 1);
            }
            // Then the buggy moves into them: one tick's worth further in, at its speed.
            car.Vel = side * speed;
            car.Wake();
            p.Velocity = Vector3.Zero;
            p.Position -= side * MathF.Max(0.02f, speed * World.TickDt);
            w.BlockPlayer(ref p, height, server: true, who: 1);
            var hit = w.LastHitSpeed;
            var dmg = hit > Game.RunOverSpeed ? Game.RunOverDamage(hit, w.LastHitMass) : 0;
            var knock = hit > Game.RunOverSpeed ? Game.RunOverKnock(hit, w.LastHitMass, w.LastHitNormal).Length() : 0f;
            car.Vel = Vector3.Zero;
            return (hit, dmg, Vector3.Dot(p.Velocity, side), knock);
        }
        var creep = Nudge(2.5f);
        var six = Nudge(6f);
        var ten = Nudge(10f);
        var sixteen = Nudge(16f);
        Line($"run-over: the buggy into a standing player at 2.5 m/s: hit {creep.Hit:0.00} m/s, {creep.Damage} damage, pushed at {creep.Pushed:0.00} m/s; at 6, 10, 16 m/s: {six.Damage}, {ten.Damage}, {sixteen.Damage} damage, knockback {six.Knock:0.0}, {ten.Knock:0.0}, {sixteen.Knock:0.0} m/s (threshold {Game.RunOverSpeed:0.0} m/s)");
        if (creep.Damage != 0 || creep.Pushed < 2f || MathF.Abs(creep.Hit - 2.5f) > 0.3f)
            Fail("construct run-over: a vehicle at a walk should push a player along without hurting them");
        if (!(six.Damage > 0 && ten.Damage > six.Damage && sixteen.Damage > ten.Damage) || !(ten.Knock > six.Knock && sixteen.Knock > ten.Knock))
            Fail("construct run-over: damage and knockback should grow with the vehicle's speed over the threshold");
        if (MathF.Abs(ten.Hit - 10f) > 0.5f)
            Fail($"construct run-over: the hit speed should be the vehicle's own at the contact (10 m/s), was {ten.Hit:0.00}");
        if (Game.RunOverDamage(10, 2000) <= Game.RunOverDamage(10, 700) || Game.RunOverKnock(10, 2000, side).Length() <= Game.RunOverKnock(10, 700, side).Length())
            Fail("construct run-over: a heavier construct should hurt and knock back more at the same speed");

        // 4. Dropping onto the parked buggy at 8 m/s: lands on it, no run-over hit (fall damage is the mover's own rule).
        var drop = new MoveState { Position = car.WorldCom + new Vector3(0, 2.5f, 0), Velocity = new Vector3(0, -8f, 0) };
        var dropWorst = 0f;
        Construct? landed = null;
        for (var t = 0; t < 40 && landed == null; t++)
        {
            drop.Position += drop.Velocity * World.TickDt;
            landed = w.BlockPlayer(ref drop, height, server: true, who: 1);
            dropWorst = MathF.Max(dropWorst, w.LastHitSpeed);
        }
        Line($"run-over: dropping at 8 m/s onto the parked buggy: {(landed != null ? "landed on it" : "never landed")}, hit speed {dropWorst:0.00} m/s");
        if (landed == null || dropWorst > 0.01f)
            Fail("construct run-over: landing on a parked vehicle should be a landing, not a run-over hit");

        // 5. A gate's leaf swung fast on its motor hinge hurts whoever it swings into; at rest it only blocks.
        var gw = new ConstructWorld();
        var gate = BuildBlueprint(gw, "Motor Gate", -10, -12);
        if (gate == null)
            return;
        StepConstructs(gw, 10);
        var door = PartNamed(gate, "Door");
        var leaf = ConstructRender.PartCentre(gate, door);
        var seg = gate.Parts[door].Seg;
        gate.Joints[0].Vel = 1f;
        var perRad = gate.SegPointVel(seg, leaf) with { Y = 0 };
        var omega = 9f / MathF.Max(perRad.Length(), 0.05f); // (the leaf's middle at 9 m/s)
        var dir = Vector3.Normalize(perRad);
        float Swing(float vel)
        {
            gate.Joints[0].Vel = vel;
            var p = new MoveState { Position = leaf with { Y = gate.Pos.Y + 0.31f } + dir * (World.PlayerRadius - 0.05f), Grounded = true };
            gw.BlockPlayer(ref p, height, server: true, who: 1);
            return gw.LastHitSpeed;
        }
        var still = Swing(0f);
        var fast = Swing(omega);
        gate.Joints[0].Vel = 0;
        var armDamage = Game.RunOverDamage(fast, gw.LastHitMass);
        Line($"run-over: the Motor Gate's leaf at rest hits at {still:0.00} m/s; swung at {omega:0.0} rad/s it hits at {fast:0.0} m/s for {armDamage} damage");
        if (still > 0.01f || fast < 7f || armDamage <= 0)
            Fail("construct run-over: a hinged leaf swung at speed should hurt the player it hits (and not at rest)");
    }
}
