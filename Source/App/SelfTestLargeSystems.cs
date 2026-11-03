using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string LargeSystemsMap = "Proving Ground";

    /// <summary>A flat stretch of the map, half a side <paramref name="half"/> m, nothing standing on it (the ground under its middle).</summary>
    private static Vector3? LsFlat(float half)
    {
        for (var x = -160f; x <= 160f; x += 8f)
            for (var z = -160f; z <= 160f; z += 8f)
            {
                var g0 = ConstructWorld.Ground(World.Query, x, z);
                var flat = true;
                for (var dx = -half; dx <= half && flat; dx += 4f)
                    for (var dz = -half; dz <= half && flat; dz += 4f)
                        flat = MathF.Abs(ConstructWorld.Ground(World.Query, x + dx, z + dz).Y - g0.Y) < 0.01f;
                if (flat)
                    return g0;
            }
        return null;
    }

    /// <summary>Fits a part onto a socket through the game's own rules, in the spin <paramref name="turn"/> accepts (-1: refused, noted).</summary>
    private static int LsFit(ConstructWorld w, Construct c, int on, string socket, ConstructPartDef def, string by, List<string> problems, Func<byte, bool>? turn = null)
    {
        var s = Array.FindIndex(c.DefOf(on).Sockets, x => x.Name == socket);
        var b = Array.FindIndex(def.Sockets, x => x.Name == by);
        var why = "no spin fits";
        for (var spin = 0; spin < 4; spin++)
        {
            if (ConstructBuild.Place(c, on, s, def, b, spin, out var g) is { } refused)
            {
                why = refused;
                continue;
            }
            if (turn != null && !turn(g.Rot))
                continue;
            var i = ConstructBuild.Attach(w, c, def, g);
            c.Rebuild();
            return i;
        }
        problems.Add($"{def.Name} on {c.DefOf(on).Name} ({socket}): {why}");
        return -1;
    }

    /// <summary>A part welded on by hand with a plain link (a small control on a big part, as an adapter does).</summary>
    private static int LsBolt(ConstructWorld w, Construct c, int on, ConstructPartDef def, Vector3 at)
    {
        var i = w.AddPart(c, def, at, 0);
        w.AddLink(c, on, 0, i, 0);
        c.Rebuild();
        return i;
    }

    /// <summary>A new construct: one part, pinned or free, at a world point.</summary>
    private static Construct LsBase(ConstructWorld w, ConstructPartDef def, Vector3 at, bool pinned, byte rot = 0)
    {
        var c = w.Create(at, Quaternion.Identity)!;
        w.AddPart(c, def, Vector3.Zero, rot);
        c.Pinned = pinned;
        c.Rebuild();
        return c;
    }

    private static Construct LsCar(ConstructWorld w, string blueprint, Vector3 at, float yaw)
    {
        var c = ConstructBuild.Build(w, ConstructBuild.Blueprints[IndexOfBlueprint(blueprint)], at, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw), out var why);
        if (c == null)
            throw new InvalidOperationException($"{blueprint}: {why}");
        c.Rebuild();
        return c;
    }

    /// <summary>A car moved so its centre of mass stands over <paramref name="x"/>, <paramref name="z"/>.</summary>
    private static Construct LsOver(Construct c, float x, float z)
    {
        c.Pos += new Vector3(x - c.WorldCom.X, 0, z - c.WorldCom.Z);
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        c.NetHistReset();
        return c;
    }

    private static void LsTicks(ConstructWorld w, int n, Action? each = null)
    {
        for (var k = 0; k < n; k++)
        {
            each?.Invoke();
            w.Step(World.Query, World.TickDt);
        }
    }

    /// <summary>
    /// "Large systems" (docs/sandbox/CONSTRUCTS.md, "Large systems"; a mothership launching small vehicles): every part of
    /// tools/constructs/large_systems.py loads and grows sensibly with its class; a parked car rides a moving, turning
    /// deck and its driver drives it off onto the ground; a lift carries a car up and down on a wired signal, its weight on
    /// the piston's load; a drawbridge lowers on a signal and a car drives down it; a docking clamp holds a crewed car
    /// fast, charges and refuels it, lets go on the next signal; a magnetic lock; a catapult fired by a wire throws a car,
    /// the ship takes the reaction, a flat battery stops it; arrestor gear stops it again; a winch reels a load, stalls past
    /// its rating; a conveyor carries a crate; a fire director turns two turrets onto what its gunner looks at and fires
    /// them; a hangar door opens on a signal; a player climbs a ladder and walks a catwalk on a moving ship; no allocation.
    /// </summary>
    private static void CheckLargeSystems()
    {
        if (Maps.Find(LargeSystemsMap) is not { } map)
        {
            Fail($"large systems: the {LargeSystemsMap} map is missing");
            return;
        }
        map.RestoreProps();
        World.SetMap(map);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        LsData(Want);
        if (LsFlat(44) is not { } g)
        {
            Fail("large systems: no flat ground on the map");
            return;
        }
        Line($"large systems: flat ground at {g}");
        LsRideDeck(g, problems, Want);
        LsLift(g, problems, Want);
        LsRamp(g, problems, Want);
        LsDock(g, problems, Want);
        LsCatapult(g, problems, Want);
        LsWinch(g, problems, Want);
        LsConveyor(g, problems, Want);
        LsTurrets(g, problems, Want);
        LsDoor(g, problems, Want);
        LsWalk(g, problems, Want);
        LsBlueprints(g, problems, Want);
        LsCost(g, Want);
        foreach (var p in problems.Take(20))
            Fail($"large systems: {p}");
    }

    private static void LsData(Action<bool, string> want)
    {
        var mine = ConstructParts.All.Where(d => d.Folder.StartsWith("Systems", StringComparison.Ordinal)).ToList();
        Line($"large systems: {mine.Count} parts, {ConstructParts.Refused.Count} refused");
        foreach (var r in ConstructParts.Refused)
            Line($"large systems: refused: {r}");
        want(mine.Count >= 70, $"only {mine.Count} parts in Constructs/Parts/Systems");
        want(ConstructParts.Refused.Count == 0, $"refused: {string.Join(" | ", ConstructParts.Refused.Take(4))}");
        foreach (var d in mine)
            foreach (var why in ConstructParts.PortProblems(d))
                want(false, $"{d.Name}: {why}");
        // Bigger classes hold more, weigh more and last longer.
        string[] classes = ["Medium", "Large", "Capital"];
        foreach (var family in new[] { "Motor Hinge", "Turntable", "Docking Clamp" })
        {
            var list = classes.Select(k => ConstructParts.Need($"{family} {k}")).ToList();
            for (var k = 1; k < 3; k++)
                want(list[k].Mass > list[k - 1].Mass * 2 && list[k].Health > list[k - 1].Health * 2 && (family[0] == 'D' ? list[k].DockMaxMass : list[k].MotorStrength) > (family[0] == 'D' ? list[k - 1].DockMaxMass : list[k - 1].MotorStrength) * 2, $"{family}: {classes[k]} doesn't grow on {classes[k - 1]}");
        }
        want(ConstructParts.Need("Bridge Helm").Helm && ConstructParts.Need("Winch Large 32 m").IsWinch && ConstructParts.Need("Winch Large 32 m").MaxInputs == 1, "the helm and winch flags");
        var bad = (string json, string word) =>
        {
            try
            {
                ConstructParts.Parse(json, "bad.json");
                want(false, $"a bad part file loaded (wanted a refusal with \"{word}\")");
            }
            catch (ConstructPartException ex)
            {
                want(ex.Message.Contains(word, StringComparison.Ordinal), $"refused for the wrong reason: {ex.Message}");
            }
        };
        const string shell = "\"format\": \"call-of-slop-construct-part/1\", \"name\": \"X\", \"role\": \"logic\", \"mass\": 5, \"health\": 5, \"boxes\": [{ \"size\": [1, 1, 1] }], \"sockets\": [{ \"at\": [0.5, 0, 0], \"dir\": \"+x\" }]";
        bad("{" + shell + ", \"logic\": { \"kind\": \"lamp\" }, \"dock\": { \"side\": 4 }}", "cargo");
        bad("{" + shell + ", \"logic\": { \"kind\": \"launch\" }}", "launch");
        bad("{" + shell + ", \"logic\": { \"kind\": \"conveyor\" }}", "conveyor");
        bad("{" + shell + ", \"logic\": { \"kind\": \"lamp\" }, \"launch\": { \"width\": 2, \"height\": 2, \"accel\": 2 }}", "launch");
    }

    /// <summary>A parked car rides a moving, turning deck; then its driver drives it off onto the ground.</summary>
    private static void LsRideDeck(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var y0 = g.Y + 3f;
        var ship = LsBase(w, ConstructParts.Need("Ramp Deck Large 20x20"), new Vector3(g.X - 14, y0, g.Z), pinned: false);
        var centre = ship.ToWorld(new Vector3(8, 0, 0));
        var car = LsCar(w, "Buggy", centre + new Vector3(0, 1.2f, 0), MathF.PI / 2);
        var v = Vector3.Zero;
        var spin = 0f;
        void Sail()
        {
            ship.Wake();
            ship.Vel = v;
            ship.AngVel = new Vector3(0, spin, 0);
        }
        void Hold() => ship.Pos = ship.Pos with { Y = y0 };
        var tick = () =>
        {
            Sail();
            w.Step(World.Query, World.TickDt);
            Hold();
        };
        for (var t = 0; t < 120; t++)
            tick();
        var settled = ship.ToLocal(car.WorldCom);
        var touching = Enumerable.Range(0, car.WheelCount).Count(k => car.Wheels[k].Touching);
        want(touching == car.WheelCount && car.WorldCom.Y > y0, $"the car stands on the deck on its wheels ({touching} of {car.WheelCount} touching, y {car.WorldCom.Y - y0:0.00} over it)");
        v = new Vector3(6, 0, 0);
        for (var t = 0; t < 120; t++)
        {
            v = new Vector3(6 * MathF.Min(1, t / 60f), 0, 0); // (the ship gets going)
            tick();
        }
        spin = 0.25f;
        for (var t = 0; t < 180; t++)
        {
            v = Vector3.Transform(new Vector3(6, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, spin * t * World.TickDt));
            tick();
        }
        var drift = Vector3.Distance(ship.ToLocal(car.WorldCom), settled);
        Line($"large systems: a parked car drifted {drift:0.00} m on a deck doing 6 m/s and turning 0.25 rad/s");
        want(drift < 1f, $"the parked car rides the moving deck (drifted {drift:0.00} m)");
        // Drive it off (the deck going on), down onto the ground.
        spin = 0;
        car.Driver = 1;
        var fwd = Vector3.Transform(car.SeatForward(), car.Rot);
        var off = false;
        for (var t = 0; t < 360 && !off; t++)
        {
            ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt);
            tick();
            var lc = ship.ToLocal(car.WorldCom);
            off = MathF.Abs(lc.X - 8) > 11 || MathF.Abs(lc.Z) > 11;
        }
        car.Driver = 255;
        var moved = Vector3.Distance(ship.ToLocal(car.WorldCom), settled);
        want(off, $"its driver drives it off the deck (moved {moved:0.0} m on it, facing {fwd})");
        v = Vector3.Zero;
        LsTicks(w, 180, () =>
        {
            Sail();
            Hold();
        });
        want(car.WorldCom.Y < y0 - 0.5f, $"it lands on the ground (com {car.WorldCom.Y - g.Y:0.00} m over it)");
    }

    /// <summary>A lift on a wired signal: its weight shows on the piston, and the car rides it up and, woken, down.</summary>
    private static void LsLift(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var lift = LsBase(w, ConstructParts.Need("Piston Medium 4 m"), g + new Vector3(0, 0.51f, 0), pinned: true);
        var plat = LsFit(w, lift, 0, "top", ConstructParts.Need("Lift Platform Medium 5x5"), "-y", problems);
        if (plat < 0)
            return;
        var sw = LsBolt(w, lift, 0, ConstructParts.Need("Switch"), new Vector3(0.6f, 0, 0));
        w.AddWire(lift, sw, 0);
        lift.Rebuild();
        LsTicks(w, 30);
        var empty = MathF.Abs(lift.Joints[0].Load);
        var top = lift.ToWorld(new Vector3(0, 0, 0));
        lift.WorldFrame(lift.Parts[plat].Seg, out var q, out var t);
        var deck = t + Vector3.Transform(lift.Parts[plat].Pos + new Vector3(2, 0, 0), q);
        var car = LsOver(LsCar(w, "Buggy", deck + new Vector3(0, 1.3f, 0), 0), deck.X, deck.Z);
        var carried = 0f;
        LsTicks(w, 180, () => carried = MathF.Max(carried, MathF.Abs(lift.Joints[0].Load)));
        var y0 = car.WorldCom.Y;
        Line($"large systems: lift load empty {empty:0} N, with a {car.Mass:0} kg car {carried:0} N; rating {lift.Joints[0].Strength:0} N");
        want(carried - empty > car.Mass * 9f * 0.7f, $"the car's weight is on the piston ({empty:0} N empty, {carried:0} N with it)");
        lift.Parts[sw].State ^= 1; // (the switch thrown)
        LsTicks(w, 700);
        var up = car.WorldCom.Y - y0;
        Line($"large systems: the lift raised the car {up:0.00} m (joint at {lift.Joints[0].Angle:0.00} m, moving {lift.Joints[0].Vel:0.000}; car at {car.Vel} over wheels {car.Wheels[0].Compression:0.000}, lift asleep {lift.Asleep}, car {car.Asleep})");
        want(up > 3.7f, $"the lift carries the car up (it rose {up:0.00} m, the joint is at {lift.Joints[0].Angle:0.00})");
        want(lift.Asleep && car.Asleep, $"both sleep at the top (lift {lift.Asleep}, car {car.Asleep})");
        lift.Parts[sw].State ^= 1;
        LsTicks(w, 700);
        var back = car.WorldCom.Y - y0;
        want(MathF.Abs(back) < 0.3f, $"it comes down with the platform, the sleeping car too (it is {back:0.00} m from where it began)");
    }

    /// <summary>A drawbridge lowered on a signal, and a car driven down it onto the ground.</summary>
    private static void LsRamp(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var hinge = ConstructParts.Need("Motor Hinge Medium");
        byte rot = 0;
        for (byte r = 0; r < Rot24.Count; r++)
            if (Vector3.Distance(Rot24.Apply(r, Vector3.UnitY), Vector3.UnitZ) < 0.01f && Vector3.Distance(Rot24.Apply(r, Vector3.UnitX), Vector3.UnitX) < 0.01f)
                rot = r;
        var y0 = g.Y + 2f;
        var at = new Vector3(g.X - 8, y0, g.Z);
        var bridge = LsBase(w, hinge, at, pinned: true, rot);
        var ramp = LsFit(w, bridge, 0, "leaf", ConstructParts.Need("Ramp Deck Medium 8x5"), "-x", problems, r => Rot24.Apply(r, Vector3.UnitY).Y > 0.9f);
        if (ramp < 0)
            return;
        var lever = LsBolt(w, bridge, 0, ConstructParts.Need("Lever"), new Vector3(-0.8f, 0, 0));
        w.AddWire(bridge, lever, 0);
        bridge.Rebuild();
        ConstructConfig.Set(ref bridge.Parts[0], hinge, hinge.CfgSlot[(int)CfgKey.Min], -60f);
        bridge.Rebuild();
        LsTicks(w, 60);
        bridge.WorldFrame(bridge.Parts[ramp].Seg, out var q, out var t);
        var mid = t + Vector3.Transform(bridge.Parts[ramp].Pos + new Vector3(2, 0, 0), q);
        var car = LsOver(LsCar(w, "Buggy", mid + new Vector3(0, 1.3f, 0), MathF.PI / 2), mid.X, mid.Z);
        LsTicks(w, 90);
        var angle0 = bridge.Joints[0].Angle;
        bridge.Parts[lever].State = 3; // (the lever back: -1, a signal down)
        LsTicks(w, 240);
        var tip = bridge.Parts[ramp].Pos + new Vector3(7.5f, 0, 0);
        bridge.WorldFrame(bridge.Parts[ramp].Seg, out q, out t);
        var tipY = (t + Vector3.Transform(tip, q)).Y - g.Y;
        Line($"large systems: the drawbridge went from {angle0 * 57.3f:0} to {bridge.Joints[0].Angle * 57.3f:0} degrees, its tip {tipY:0.00} m over the ground; the car {car.WorldCom.Y - g.Y:0.00} m up");
        want(tipY < 0.6f, $"the drawbridge lowers to the ground on a wired signal (its tip is {tipY:0.00} m up, angle {bridge.Joints[0].Angle * 57.3f:0})");
        car.Driver = 1;
        var fwd = Vector3.Transform(car.SeatForward(), car.Rot) with { Y = 0 };
        var start = car.WorldCom;
        var down = false;
        for (var k = 0; k < 400 && !down; k++)
        {
            ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt);
            w.Step(World.Query, World.TickDt);
            down = Vector3.Dot(car.WorldCom - at, Vector3.UnitX) > 9f && Enumerable.Range(0, car.WheelCount).All(i => car.Wheels[i].Touching);
        }
        car.Driver = 255;
        want(down && car.WorldCom.Y - g.Y < 1.4f, $"a car drives down the ramp onto the ground (it went {Vector3.Dot(car.WorldCom - start, Vector3.UnitX):0.0} m along, com {car.WorldCom.Y - g.Y:0.00} m up, forward {fwd})");
    }

    /// <summary>A docking clamp and a magnetic lock on a moving ship: held fast with a driver aboard, charged and refuelled, let go on a signal.</summary>
    private static void LsDock(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var y0 = g.Y + 3f;
        var ship = LsBase(w, ConstructParts.Need("Ramp Deck Medium 8x5"), new Vector3(g.X - 12, y0, g.Z), pinned: false);
        var clampDef = ConstructParts.Need("Docking Clamp Medium");
        var clamp = LsFit(w, ship, 0, "+x", clampDef, "-x", problems);
        if (clamp < 0)
            return;
        var sw = LsBolt(w, ship, 0, ConstructParts.Need("Switch"), new Vector3(-1f, 0.3f, 1.5f));
        w.AddWire(ship, sw, clamp);
        var tank = LsBolt(w, ship, 0, ConstructParts.Need("Fuel Tank"), new Vector3(-1f, 0.5f, -1.5f));
        var batt = LsBolt(w, ship, 0, ConstructParts.Need("Battery"), new Vector3(0f, 0.3f, -1.5f));
        ship.Rebuild();
        _ = tank;
        _ = batt;
        ConstructConfig.Set(ref ship.Parts[clamp], clampDef, clampDef.CfgSlot[(int)CfgKey.AtX], -4f);
        var car = LsCar(w, "Wired Buggy", ship.ToWorld(new Vector3(3, 0, 0)) + new Vector3(0, 1.3f, 0), MathF.PI / 2);
        var v = Vector3.Zero;
        var spin = 0f;
        var tick = () =>
        {
            ship.Wake();
            ship.Vel = v;
            ship.AngVel = new Vector3(0, spin, 0);
            w.Step(World.Query, World.TickDt);
            ship.Pos = ship.Pos with { Y = y0 };
        };
        for (var t = 0; t < 90; t++)
            tick();
        car.Fuel = 0.5f;
        car.Charge = 1f;
        ship.Charge = ship.ChargeCapacity;
        var fuel0 = ship.Fuel;
        car.Driver = 1; // (crew aboard: a dock holds them)
        ship.Parts[sw].State ^= 1;
        for (var t = 0; t < 30; t++)
            tick();
        want(car.CarriedBy == ship.Id, $"the clamp catches the crewed car on a wired signal (carried by {car.CarriedBy}, clamp {ship.Parts[clamp].Out})");
        var local = ship.ToLocal(car.Pos);
        v = new Vector3(5, 0, 0);
        spin = 0.3f;
        for (var t = 0; t < 180; t++)
        {
            ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt); // (a docked car's driver goes nowhere)
            v = Vector3.Transform(new Vector3(5, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, spin * t * World.TickDt));
            tick();
        }
        var shift = Vector3.Distance(ship.ToLocal(car.Pos), local);
        Line($"large systems: docked for 3 s at 5 m/s and 0.3 rad/s: {shift * 100:0.0} cm in the ship's frame; fuel {car.Fuel:0.0} l, charge {car.Charge:0.0} kJ");
        want(shift < 0.03f, $"docked, it holds fast on the moving ship (it moved {shift:0.00} m in its frame, driver on the throttle)");
        want(car.Fuel > 0.6f && ship.Fuel < fuel0, $"the dock refuels it from the ship (car {car.Fuel:0.0} l, ship {ship.Fuel:0.0} of {fuel0:0.0} l)");
        want(car.Charge > 1.5f, $"the dock charges it from the ship ({car.Charge:0.0} kJ)");
        spin = 0;
        v = new Vector3(5, 0, 0) * 0;
        ship.Parts[sw].State ^= 1;
        for (var t = 0; t < 12; t++)
            tick();
        want(car.CarriedBy == 0, $"a signal lets it go (carried by {car.CarriedBy})");
        var before = ship.ToLocal(car.WorldCom);
        for (var t = 0; t < 90; t++)
        {
            ConstructEdits.DriveInput(car, Buttons.Forward, World.TickDt);
            tick();
        }
        car.Driver = 255;
        want(Vector3.Distance(ship.ToLocal(car.WorldCom), before) > 1f, "released, it drives away");
        // The magnetic lock: small, on a pinned post, catching a car put in its box when thrown; a flat battery lets go.
        var w2 = new ConstructWorld();
        var lockDef = ConstructParts.Need("Magnetic Lock");
        var post = LsBase(w2, lockDef, g + new Vector3(0, 0.13f, 0), pinned: true);
        LsTicks(w2, 2);
        var held = LsOver(LsCar(w2, "Buggy", g + new Vector3(0, 0.9f, 0), 0), g.X, g.Z);
        LsTicks(w2, 60);
        post.Parts[0].State ^= 1; // (F)
        LsTicks(w2, 12);
        want(held.CarriedBy == post.Id, $"a magnetic lock takes hold (carried by {held.CarriedBy}, com {held.WorldCom - post.Pos})");
        post.Parts[0].State ^= 1;
        LsTicks(w2, 12);
        want(held.CarriedBy == 0, "and lets go");
    }

    /// <summary>A catapult fired by a wire, the ship's reaction, a flat battery; arrestor gear stops what comes in fast.</summary>
    private static void LsCatapult(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var def = ConstructParts.Need("Catapult Medium 16 m");
        var w = new ConstructWorld();
        var at = new Vector3(g.X, g.Y + 0.51f, g.Z + 24);
        var rail = LsBase(w, def, at, pinned: true);
        var sw = LsBolt(w, rail, 0, ConstructParts.Need("Switch"), new Vector3(0.7f, 0, 0));
        w.AddWire(rail, sw, 0);
        rail.Rebuild();
        var car = LsOver(LsCar(w, "Buggy", at + new Vector3(0, 1.3f, -1), 0), at.X, at.Z - 2);
        LsTicks(w, 90);
        rail.Parts[sw].State ^= 1;
        var top = 0f;
        var fired = false;
        LsTicks(w, 150, () =>
        {
            top = MathF.Max(top, car.Vel.Length());
            fired |= rail.Parts[0].Out >= 0.5f;
        });
        var expect = MathF.Sqrt(2 * def.LaunchAccel * 12);
        Line($"large systems: the catapult threw a {car.Mass:0} kg car to {top:0.0} m/s (about {expect:0.0} expected)");
        want(fired && top > expect * 0.7f, $"a wired catapult throws the car (top speed {top:0.0} m/s, its output on: {fired})");
        want(car.WorldCom.Z < at.Z - 16f, $"it leaves the rail going forward ({car.WorldCom - at})");
        // Arrestor gear in its path: a car coming in fast stops on it.
        var wa = new ConstructWorld();
        var ar = LsBase(wa, ConstructParts.Need("Arrestor Gear Medium 16 m"), at, pinned: true);
        var fast = LsOver(LsCar(wa, "Buggy", at + new Vector3(0, 1.3f, -1), 0), at.X, at.Z - 2);
        LsTicks(wa, 60);
        fast.Vel = new Vector3(0, 0, -20);
        fast.Wake();
        LsTicks(wa, 60);
        want(fast.Vel.Length() < 8f, $"arrestor gear slows a fast car ({fast.Vel.Length():0.0} m/s after 1 s, Out {ar.Parts[0].Out})");
        // The reaction: a free ship kicks back by the momentum; a flat battery holds the shot.
        var ws = new ConstructWorld();
        var hy = g.Y + 3f;
        var ship = LsBase(ws, def, new Vector3(g.X, hy, g.Z + 24), pinned: false);
        var deck = LsFit(ws, ship, 0, "+x", ConstructParts.Need("Lift Platform Medium 5x5"), "-x", problems);
        var batt = LsBolt(ws, ship, 0, ConstructParts.Need("Battery"), new Vector3(-0.7f, 0, 0));
        ship.Rebuild();
        ship.Charge = 0.1f;
        _ = deck;
        _ = batt;
        var spot = ship.ToWorld(new Vector3(0, 2f, -3));
        var load = LsOver(LsCar(ws, "Buggy", spot, 0), spot.X, spot.Z);
        for (var k = 0; k < 2; k++)
        {
            ship.Wake();
            ws.Step(World.Query, World.TickDt);
        }
        ship.Parts[0].State |= 1;
        ship.Vel = ship.AngVel = Vector3.Zero;
        load.Vel = Vector3.Zero;
        ws.Step(World.Query, World.TickDt);
        ws.Step(World.Query, World.TickDt);
        want(load.Vel.Z > -2f, $"a flat battery holds the catapult ({load.Vel.Z:0.0} m/s)");
        ship.Charge = ship.ChargeCapacity;
        ship.Parts[0].State |= 1;
        ship.Vel = ship.AngVel = Vector3.Zero;
        load.Vel = Vector3.Zero;
        ws.Step(World.Query, World.TickDt);
        ws.Step(World.Query, World.TickDt);
        ws.Step(World.Query, World.TickDt);
        want(ship.Vel.Z > 0.01f && load.Vel.Z < -0.5f, $"the ship takes the reaction (ship {ship.Vel.Z:0.00} m/s, car {load.Vel.Z:0.00} m/s)");
    }

    /// <summary>A winch on a signal reels a load up and lowers it, and stalls past its rating.</summary>
    private static void LsWinch(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var def = ConstructParts.Need("Winch Medium 16 m");
        var crane = LsBase(w, def, g + new Vector3(0, 9f, 0), pinned: true);
        var lever = LsBolt(w, crane, 0, ConstructParts.Need("Lever"), new Vector3(0.8f, 0, 0));
        w.AddWire(crane, lever, 0);
        crane.Rebuild();
        var eye = ConstructParts.Need("Lifting Eye Medium");
        var load = LsBase(w, eye, g + new Vector3(0, 0.4f, 0), pinned: false);
        LsTicks(w, 60);
        var why = w.CanCouple(crane, 0, load, 0);
        want(why == null, $"a winch hooks an eye in reach ({why})");
        if (why != null || !w.Couple(crane, 0, load, 0))
            return;
        var length = w.Couplings[0].Length;
        var y0 = load.WorldCom.Y;
        crane.Parts[lever].State = 1; // (forward: reel in)
        LsTicks(w, 180);
        want(load.WorldCom.Y - y0 > 3f && w.Couplings[0].Length < length - 4f, $"it reels the load up ({load.WorldCom.Y - y0:0.00} m, chain {w.Couplings[0].Length:0.0} of {length:0.0} m)");
        crane.Parts[lever].State = 3; // (back: pay out)
        LsTicks(w, 120);
        want(w.Couplings[0].Length > length - 3f, $"and pays it out ({w.Couplings[0].Length:0.0} m)");
        crane.Parts[lever].State = 0;
        // Past its rating it stalls.
        var heavy = ConstructParts.Need("Ramp Deck Capital 48x48");
        load.Pinned = false;
        var hw = new ConstructWorld();
        var c2 = LsBase(hw, def, g + new Vector3(0, 9f, 0), pinned: true);
        var lv2 = LsBolt(hw, c2, 0, ConstructParts.Need("Lever"), new Vector3(0.8f, 0, 0));
        hw.AddWire(c2, lv2, 0);
        c2.Rebuild();
        var big = LsBase(hw, heavy, g + new Vector3(-16, 2f, 0), pinned: false);
        var eye2 = LsBolt(hw, big, 0, eye, new Vector3(16, 0.5f, 0));
        LsTicks(hw, 2);
        if (hw.CanCouple(c2, 0, big, eye2) is { } why2)
            want(false, $"the heavy load couples ({why2})");
        else
        {
            hw.Couple(c2, 0, big, eye2);
            var l0 = hw.Couplings[0].Length;
            c2.Parts[lv2].State = 1;
            LsTicks(hw, 60);
            want(MathF.Abs(hw.Couplings[0].Length - l0) < 0.01f, $"past its rating ({big.Mass:0} kg) it stalls ({l0:0.00} to {hw.Couplings[0].Length:0.00} m)");
        }
    }

    private static void LsConveyor(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var def = ConstructParts.Need("Conveyor Medium 4x2");
        var belt = LsBase(w, def, g + new Vector3(0, 0.51f, 0), pinned: true);
        var at = belt.ToWorld(new Vector3(0.5f, 0.6f, -0.5f));
        var crate = ConstructBuild.PlaceNew(w, ConstructParts.Need("Frame Block"), at, 0)!;
        LsTicks(w, 60);
        var z0 = crate.WorldCom.Z;
        want(crate.Asleep, "the crate settles on the stopped belt");
        belt.Parts[0].State ^= 1; // (F: run)
        LsTicks(w, 60);
        var moved = z0 - crate.WorldCom.Z;
        Line($"large systems: a conveyor at {def.BeltSpeed:0.0} m/s moved a crate {moved:0.00} m in 1 s");
        want(moved > def.BeltSpeed * 0.4f, $"a running conveyor carries a crate along it ({moved:0.00} m in 1 s)");
    }

    /// <summary>One gunner, a fire director, two turrets: they turn to what the gunner looks at, hold fire until they bear, then fire and hit.</summary>
    private static void LsTurrets(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var ring = ConstructParts.Need("Turntable Large");
        var navy = ConstructParts.Need("Naval Gun 20 cm");
        var mag = ConstructParts.Need("Shell Magazine Large");
        var ship = LsBase(w, ring, g + new Vector3(0, 2.01f, 0), pinned: true);
        var gun = LsFit(w, ship, 0, "top", navy, "bottom", problems);
        if (gun < 0)
            return;
        var box = LsFit(w, ship, gun, "back", mag, "-z", problems);
        var ring2 = LsBolt(w, ship, 0, ring, new Vector3(0, 0, 8.01f));
        var gun2 = LsFit(w, ship, ring2, "top", navy, "bottom", problems);
        var box2 = gun2 >= 0 ? LsFit(w, ship, gun2, "back", mag, "-z", problems) : -1;
        var seat = LsBolt(w, ship, 0, ConstructParts.Need("Crew Station"), new Vector3(3f, 0, 0));
        var dir = LsBolt(w, ship, 0, ConstructParts.Need("Fire Director"), new Vector3(3f, 0.2f, 1.5f));
        w.AddWire(ship, seat, dir);
        w.AddWire(ship, dir, gun);
        if (gun2 >= 0)
            w.AddWire(ship, dir, gun2);
        ship.Rebuild();
        _ = box;
        _ = box2;
        want(ship.GunCount == 2 && ship.Guns[0].Director == dir && ship.Guns[0].AimJoints == 1 && ship.Guns[0].Problem.Length == 0, $"two guns on the director ({ship.GunCount}, director {ship.Guns[0].Director}, {ship.Guns[0].AimJoints} joints, \"{ship.Guns[0].Problem}\")");
        // The target, off to the west at 50 m (the guns start pointing north).
        var target = LsBase(w, ConstructParts.Need("Hangar Door Medium 5x3"), g + new Vector3(-50, 3.5f, 0), pinned: true);
        var shots0 = w.ShotsFired;
        void Gunner(bool fire)
        {
            ship.Parts[seat].Keys = (ushort)(CPart.Occupied | (fire ? ConstructParts.KeyFire : 0));
            var eye = ConstructWeapons.SeatEye(ship, seat);
            var to = target.WorldCom - eye;
            ship.Parts[seat].AimYaw = MathF.Atan2(to.X, -to.Z);
            ship.Parts[seat].AimPitch = MathF.Asin(to.Y / to.Length());
        }
        LsTicks(w, 2, () => Gunner(true));
        want(w.ShotsFired == shots0, "they hold fire until they bear");
        var hp = target.Parts[0].Health;
        var bore = 9f;
        LsTicks(w, 900, () =>
        {
            Gunner(true);
            ConstructWeapons.MuzzleWorld(ship, 0, out var m, out var f);
            bore = MathF.Min(bore, MathF.Acos(Math.Clamp(Vector3.Dot(f, Vector3.Normalize(target.WorldCom - m)), -1, 1)));
        });
        Line($"large systems: director guns fired {w.ShotsFired - shots0} shots, nearest bearing {bore * 57.3f:0.0} degrees, target health {hp:0} to {(target.PartCount > 0 ? target.Parts[0].Health : 0):0}");
        want(bore < 0.14f && w.ShotsFired - shots0 >= 2, $"both turrets turn onto the gunner's aim and fire (bearing {bore * 57.3f:0.0} degrees, {w.ShotsFired - shots0} shots)");
        want(target.PartCount == 0 || target.Parts[0].Health < hp, "and hit it");
    }

    private static void LsDoor(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var ram = ConstructParts.Need("Piston Medium 4 m");
        byte rot = 0;
        for (byte r = 0; r < Rot24.Count; r++)
            if (Vector3.Distance(Rot24.Apply(r, Vector3.UnitY), Vector3.UnitX) < 0.01f)
                rot = r;
        var at = g + new Vector3(-4, 2f, 0);
        var frame = LsBase(w, ram, at, pinned: true, rot);
        var door = LsFit(w, frame, 0, "top", ConstructParts.Need("Hangar Door Medium 5x3"), "-x", problems, r => Rot24.Apply(r, Vector3.UnitY).Y > 0.9f);
        if (door < 0)
            return;
        var sw = LsBolt(w, frame, 0, ConstructParts.Need("Switch"), new Vector3(0, 0, 0.7f));
        w.AddWire(frame, sw, 0);
        frame.Rebuild();
        LsTicks(w, 30);
        var from = PartCentre(frame, door) + new Vector3(0, 0, -3);
        var shut = w.Raycast(from, Vector3.UnitZ, 6, out _);
        frame.Parts[sw].State ^= 1;
        LsTicks(w, 540);
        var open = w.Raycast(from, Vector3.UnitZ, 6, out _);
        Line($"large systems: the hangar door slid {frame.Joints[0].Angle:0.00} m: shut {shut}, open {open}");
        want(shut && !open && frame.Joints[0].Angle > 3.8f, $"a hangar door opens on a signal (shut: blocked {shut}; open: blocked {open}; at {frame.Joints[0].Angle:0.00} m)");
    }

    /// <summary>A player climbs a ladder and walks a catwalk on a ship under way.</summary>
    private static void LsWalk(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        var ladder = ConstructParts.Need("Ladder 4 m");
        var y = g.Y;
        var bottom = LsBase(w, ladder, g + new Vector3(0, 0.51f, 0), pinned: true);
        var height = World.PlayerHeight;
        var climb = new MoveState { Position = g + new Vector3(0, 0, 1.2f), Grounded = true };
        var highest = climb.Position.Y;
        for (var t = 0; t < 360; t++)
        {
            climb.Velocity.Z = -2f;
            climb.Velocity.X = 0;
            climb.Position.Z += climb.Velocity.Z * World.TickDt;
            climb.Velocity.Y -= 20f * World.TickDt;
            climb.Position.Y += climb.Velocity.Y * World.TickDt;
            if (climb.Position.Y <= y)
            {
                climb.Position.Y = y;
                climb.Velocity.Y = 0;
            }
            climb.Grounded = climb.Position.Y <= y;
            w.BlockPlayer(ref climb, height, server: true);
            highest = MathF.Max(highest, climb.Position.Y);
        }
        _ = bottom;
        Line($"large systems: a player climbed {highest - y:0.00} m up a 4 m ladder");
        want(highest - y > 3f, $"a player climbs the ladder ({highest - y:0.00} m)");
        // A catwalk on a ship under way: the walker stays in its frame and walks its length.
        var ws = new ConstructWorld();
        var deckY = g.Y + 3f;
        var ship = LsBase(ws, ConstructParts.Need("Catwalk 8 m"), new Vector3(g.X, deckY, g.Z), pinned: false);
        void Sail()
        {
            ship.Wake();
            ship.Vel = new Vector3(3, 0, 0);
            ship.AngVel = Vector3.Zero;
            ws.Step(World.Query, World.TickDt);
            ship.Pos = ship.Pos with { Y = deckY };
        }
        for (var t = 0; t < 60; t++)
            Sail(); // (under way first: its first jolt would throw a rider off)
        var walker = new MoveState { Position = ship.ToWorld(new Vector3(0, 0.1f, 0.5f)), Grounded = true };
        for (var t = 0; t < 180; t++)
        {
            Sail();
            walker.Velocity.X = 0;
            walker.Velocity.Z = 2f;
            walker.Velocity.Y -= 20f * World.TickDt;
            walker.Position += walker.Velocity * World.TickDt;
            walker.Grounded = false;
            ws.BlockPlayer(ref walker, height, server: true);
        }
        var along = ship.ToLocal(walker.Position);
        Line($"large systems: walked a catwalk under way to {along} (riding {walker.RideBody})");
        want(walker.RideBody == ship.Id && along.Z > 3f && MathF.Abs(along.X) < 0.5f && MathF.Abs(along.Y) < 0.3f, $"a player walks a catwalk on a moving ship ({along}, riding {walker.RideBody})");
    }

    /// <summary>The blueprints build through the game's rules; the Gate Ramp's bridge lies flat and its gate stands on the deck's far edge.</summary>
    private static void LsBlueprints(Vector3 g, List<string> problems, Action<bool, string> want)
    {
        var w = new ConstructWorld();
        foreach (var r in ConstructBuild.RefusedBlueprints)
            want(false, $"blueprint refused: {r}");
        foreach (var name in new[] { "Deck Lift", "Naval Turret", "Harbour Crane", "Gate Ramp" })
        {
            var bp = ConstructBuild.FindBlueprint(name);
            var c = bp == null ? null : ConstructBuild.Build(w, bp, g + new Vector3(0, 6, 0), Quaternion.Identity, out _);
            want(c != null, $"the {name} blueprint builds");
            if (c == null || name != "Gate Ramp")
                continue;
            c.Rebuild();
            var parts = string.Join("; ", Enumerable.Range(0, c.PartCount).Select(i => $"{c.DefOf(i).Name} rot {c.Parts[i].Rot} y+ {Rot24.Apply(c.Parts[i].Rot, Vector3.UnitY)}"));
            Line($"large systems: Gate Ramp: {parts}");
            want(Rot24.Apply(c.Parts[2].Rot, Vector3.UnitY).Y > 0.9f && MathF.Abs(Rot24.Apply(c.Parts[1].Rot, Vector3.UnitY).Y) < 0.1f, $"the gate ramp's deck lies flat on a level hinge ({parts})");
            want(Rot24.Apply(c.Parts[3].Rot, Vector3.UnitX).Z > 0.9f || Rot24.Apply(c.Parts[3].Rot, Vector3.UnitX).Z < -0.9f, $"the gate stands across the deck's edge ({parts})");
        }
    }

    /// <summary>Lifts of every class carry what they are rated for; a crowded deck costs little and allocates nothing.</summary>
    private static void LsCost(Vector3 g, Action<bool, string> want)
    {
        var problems = new List<string>();
        foreach (var (ram, deck, step, load) in new[] { ("Piston Large 16 m", "Lift Platform Large 20x20", 8f, "Ramp Deck Large 20x20"), ("Piston Capital 32 m", "Lift Platform Capital 48x48", 24f, "Ramp Deck Capital 48x48") })
        {
            var w = new ConstructWorld();
            var def = ConstructParts.Need(ram);
            var lift = LsBase(w, def, g + new Vector3(0, -def.BoundsMin.Y + 0.1f, 0), pinned: true);
            var plat = LsFit(w, lift, 0, "top", ConstructParts.Need(deck), "-y", problems);
            if (plat < 0)
                continue;
            lift.WorldFrame(lift.Parts[plat].Seg, out var q, out var t);
            var top = t + Vector3.Transform(lift.Parts[plat].Pos + new Vector3(step, 0, 0), q);
            var freight = LsCar(w, "Buggy", top + new Vector3(0, 1.3f, 0), 0);
            LsTicks(w, 60);
            lift.Joints[0].Open = true;
            lift.Wake();
            var y0 = freight.WorldCom.Y;
            LsTicks(w, 240);
            var up = freight.WorldCom.Y - y0;
            Line($"large systems: {ram} carried {deck} and a car up {up:0.00} m in 4 s (load {lift.Joints[0].Load:0} N of {lift.Joints[0].Strength:0})");
            want(up > 2f && MathF.Abs(lift.Joints[0].Load) < lift.Joints[0].Strength, $"{ram}: carries its platform and a car ({up:0.00} m; load {lift.Joints[0].Load:0} N of {lift.Joints[0].Strength:0})");
            _ = load;
        }
        foreach (var p in problems)
            want(false, p);
        // Sixteen cars on a big deck under way: the ticks' cost and allocations.
        var ws = new ConstructWorld();
        var y = g.Y + 3f;
        var ship = LsBase(ws, ConstructParts.Need("Ramp Deck Capital 48x48"), new Vector3(g.X - 8, y, g.Z), pinned: false);
        for (var i = 0; i < 16; i++)
            LsCar(ws, "Buggy", ship.ToWorld(new Vector3(2 + (i % 4) * 7, 1.3f, -12 + i / 4 * 7)), 0);
        var tick = () =>
        {
            ship.Wake();
            ship.Vel = new Vector3(4, 0, 0);
            ship.AngVel = Vector3.Zero;
            ws.Step(World.Query, World.TickDt);
            ship.Pos = ship.Pos with { Y = y };
        };
        for (var k = 0; k < 120; k++)
            tick();
        var clock = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 120; k++)
            tick();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var ms = clock.Elapsed.TotalMilliseconds / 120;
        Line($"large systems: 16 cars on a deck under way: {ms:0.00} ms a tick, {bytes} B in 120 ticks");
        want(bytes < 1024, $"a crowded deck allocates nothing ({bytes} B in 120 ticks)");
        want(ms < 8, $"a crowded deck costs {ms:0.00} ms a tick");
    }

    private static void CheckLargeSystemsNet()
    {
        if (Maps.Find(LargeSystemsMap) is not { } map || map.Sandbox == null)
        {
            Fail($"large systems network: the {LargeSystemsMap} map is missing");
            return;
        }
        const ushort port = 26473;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        World.SetMap(map);
        if (LsFlat(30) is not { } g)
        {
            Fail("large systems network: no flat ground");
            return;
        }
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("large systems network: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var w = server.Constructs;
        // A lift with a car on it, a dock holding a car, a winch holding a load, all on the host's world.
        var lift = LsBase(w, ConstructParts.Need("Piston Medium 4 m"), g + new Vector3(0, 0.51f, 0), pinned: true);
        var plat = LsFit(w, lift, 0, "top", ConstructParts.Need("Lift Platform Medium 5x5"), "-y", problems);
        var sw = LsBolt(w, lift, 0, ConstructParts.Need("Switch"), new Vector3(0.6f, 0, 0));
        w.AddWire(lift, sw, 0);
        lift.Rebuild();
        w.StructureDirty.Add(lift.Id);
        Pump(server, [a], 0.4, ref now, clock);
        lift.WorldFrame(lift.Parts[plat].Seg, out var q, out var t);
        var car = LsCar(w, "Buggy", t + Vector3.Transform(lift.Parts[plat].Pos + new Vector3(2, 0, 0), q) + new Vector3(0, 1.3f, 0), 0);
        w.StructureDirty.Add(car.Id);
        var crane = LsBase(w, ConstructParts.Need("Winch Medium 16 m"), g + new Vector3(14, 9f, 0), pinned: true);
        var lever = LsBolt(w, crane, 0, ConstructParts.Need("Lever"), new Vector3(0.8f, 0, 0));
        w.AddWire(crane, lever, 0);
        crane.Rebuild();
        w.StructureDirty.Add(crane.Id);
        var load = LsBase(w, ConstructParts.Need("Lifting Eye Medium"), g + new Vector3(14, 0.4f, 0), pinned: false);
        w.StructureDirty.Add(load.Id);
        Pump(server, [a], 1.0, ref now, clock);
        Want(w.CanCouple(crane, 0, load, 0) == null && w.Couple(crane, 0, load, 0), "the winch hooks its load");
        // The client throws the lift's switch (F, over the wire).
        StandBy(sp, PartCentre(lift, sw), 1.6f);
        Pump(server, [a], 0.4, ref now, clock);
        a.SandboxSend(CActKind.Use, body: lift.Id, part: (byte)sw);
        Pump(server, [a], 3.0, ref now, clock);
        var lc = a.Constructs.Get(lift.Id);
        Line($"large systems network: the lift at {lift.Joints[0].Angle:0.00} m on the host, {(lc != null && lc.JointCount > 0 ? lc.Joints[0].Angle : -1):0.00} on the client (\"{a.SandboxNotice}\")");
        Want(lift.Joints[0].Angle > 1f && lc != null && MathF.Abs(lc.Joints[0].Angle - lift.Joints[0].Angle) < 0.3f, $"a client's switch drives the lift, and its copy follows ({lift.Joints[0].Angle:0.00}, {lc?.Joints[0].Angle:0.00})");
        Want(a.Constructs.CouplingCount == 1 && MathF.Abs(a.Constructs.Couplings[0].Length - w.Couplings[0].Length) < 0.3f, $"the client has the winch's chain ({a.Constructs.CouplingCount}: {(a.Constructs.CouplingCount > 0 ? a.Constructs.Couplings[0].Length : -1):0.00}, host {w.Couplings[0].Length:0.00})");
        crane.Parts[lever].State = 1;
        Pump(server, [a], 1.5, ref now, clock);
        Want(a.Constructs.CouplingCount == 1 && MathF.Abs(a.Constructs.Couplings[0].Length - w.Couplings[0].Length) < 0.5f && w.Couplings[0].Length < 8.2f, $"the chain reels on the client too ({(a.Constructs.CouplingCount > 0 ? a.Constructs.Couplings[0].Length : -1):0.00}, host {w.Couplings[0].Length:0.00})");
        // A dock holding a crewed vehicle: a late joiner sees it carried.
        var lockDef = ConstructParts.Need("Magnetic Lock Medium");
        var clamp = LsFit(w, lift, plat, "+x", lockDef, "-x", problems);
        ConstructConfig.Set(ref lift.Parts[clamp], lockDef, lockDef.CfgSlot[(int)CfgKey.AtX], -3f);
        w.StructureDirty.Add(lift.Id);
        Pump(server, [a], 0.4, ref now, clock);
        lift.Parts[clamp].State ^= 1; // (the lock's own F is the same bit; the switch above went over the wire)
        Pump(server, [a], 0.8, ref now, clock);
        Want(car.CarriedBy == lift.Id, $"the lock on the platform takes the car (carried by {car.CarriedBy}; the lock's signal {lift.Parts[clamp].Out}, com {ConstructCargo.ToPart(lift, clamp, car.WorldCom)}, {lift.PartCount} parts)");
        using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, late], 1.5, ref now, clock);
        var lcar = late.Constructs.Get(car.Id);
        var lj = late.Constructs.Get(lift.Id);
        Want(lcar is { CarriedBy: not 0 } && lcar.CarriedBy == lift.Id, $"a late joiner sees the car held ({lcar?.CarriedBy})");
        Want(lj != null && lj.JointCount == 1 && MathF.Abs(lj.Joints[0].Angle - lift.Joints[0].Angle) < 0.4f, $"a late joiner has the lift where it is ({lj?.Joints[0].Angle:0.00}, host {lift.Joints[0].Angle:0.00})");
        Want(late.Constructs.CouplingCount == 1 && MathF.Abs(late.Constructs.Couplings[0].Length - w.Couplings[0].Length) < 0.5f, "a late joiner has the winch's chain");
        foreach (var p in problems)
            Fail($"large systems network: {p}");
    }
}
