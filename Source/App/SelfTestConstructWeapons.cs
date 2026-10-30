using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Construct weapons (milestone 3, decision 10; docs/sandbox/CONSTRUCTS.md "Construct weapons"): the weapon part files
    /// load and bad ones are refused for the right reason; a gun built from weapon builder parts with the tools (its
    /// problems as it grows, its numbers from the stat pipeline, its weight); a fixed hood gun points with its buggy and
    /// fires on the driver's fire key through an input reader; a turret follows its gunner's look (pure, and in a game
    /// where the gunner's own view stays put); a sentry fires only at a player in its sensor's range, turning onto them;
    /// an ammo box feeds its gun (through a feed chute too) and runs dry; a shot ammo box cooks off; penetrating,
    /// explosive and incendiary rounds hit the right parts; a client sees the server's guns fire and their ammo; no
    /// allocation per tick while firing and aiming.
    /// </summary>
    private static void CheckConstructWeapons()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"construct weapons: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        CWeaponFiles();
        CWeaponToolBuild();
        CWeaponHoodGun();
        CWeaponTurretAim();
        CWeaponSentry();
        CWeaponAmmo();
        CWeaponCookOff();
        CWeaponRounds();
        CWeaponInGame(yard);
        CWeaponNoAllocations();
        CWeaponLoopback(yard);
    }

    private static void CWeaponFiles()
    {
        var guns = ConstructParts.All.Where(p => p.Role == PartRole.Gun).ToList();
        var gunParts = ConstructParts.All.Count(p => p.Role == PartRole.GunPart);
        var boxes = ConstructParts.All.Count(p => p.Role == PartRole.AmmoBox);
        string[] heavy = ["Mounted MG", "Autocannon", "Cannon", "Rocket Pod", "Flamethrower"];
        var missing = heavy.Where(h => guns.All(g => g.Name != h)).ToList();
        const string gun = """{ "name": "G", "role": "gun", "tool": "wrench", "mass": 10, "health": 50, "boxes": [ { "size": [0.2, 0.2, 0.4] } ], "sockets": [ { "name": "bottom", "at": [0, -0.1, 0], "dir": "-y" } ], "gun": { "damage": 10, "rpm": 300, "mag": 20, "muzzle": [0, 0, -0.2] } }""";
        const string part = """{ "name": "P", "role": "gunpart", "tool": "wrench", "mass": 1, "health": 10, "boxes": [ { "size": [0.1, 0.1, 0.1] } ], "sockets": [ { "at": [0, -0.05, 0], "dir": "-y" } ], "gunPart": { "part": "Standard Barrel" } }""";
        const string box = """{ "name": "B", "role": "ammobox", "tool": "wrench", "mass": 5, "health": 30, "boxes": [ { "size": [0.3, 0.2, 0.3] } ], "sockets": [ { "at": [0, -0.1, 0], "dir": "-y" } ], "ammoBox": { "rounds": 100, "cookOff": { "radius": 3, "damage": 100 } } }""";
        (string Json, string Want)[] bad =
        [
            (gun.Replace("\"damage\": 10, \"rpm\": 300, \"mag\": 20, \"muzzle\": [0, 0, -0.2]", "\"receiver\": \"Nonesuch Receiver\""), "no weapon builder receiver"),
            (gun.Replace("\"damage\": 10, \"rpm\": 300, \"mag\": 20, \"muzzle\": [0, 0, -0.2]", "\"receiver\": \"Workhorse Receiver\", \"damage\": 5"), "takes its numbers from the weapon builder"),
            (gun.Replace(", \"muzzle\": [0, 0, -0.2]", ""), "missing \"muzzle\""),
            (gun.Replace("\"mag\": 20", "\"mag\": 20, \"flags\": [\"squeaky\"]"), "a heavy gun's flags"),
            (gun.Replace("\"rpm\": 300", "\"rpm\": 9000"), "\"gun.rpm\""),
            (gun.Replace("\"mag\": 20", "\"mag\": 20, \"fx\": \"laser\""), "\"gun.fx\""),
            (part.Replace("Standard Barrel", "Nonesuch Barrel"), "no weapon builder part"),
            (part.Replace("Standard Barrel", "Workhorse Receiver"), "doesn't go on a construct gun"),
            (box.Replace("\"rounds\": 100, ", ""), "missing \"rounds\""),
            (box.Replace("\"ammoBox\"", "\"gunPart\""), "\"gunPart\" is only for gunPart parts"),
        ];
        var ok = 0;
        foreach (var good in new[] { gun, part, box })
            try
            {
                ConstructParts.Parse(good, "good.json");
            }
            catch (ConstructPartException ex)
            {
                Fail($"construct weapons: a reference part is refused: {ex.Message}");
            }
        foreach (var (json, want) in bad)
            try
            {
                ConstructParts.Parse(json, "bad.json");
                Fail($"construct weapons: a bad part file loaded (wanted a refusal with {want})");
            }
            catch (ConstructPartException ex)
            {
                if (ex.Message.Contains(want, StringComparison.OrdinalIgnoreCase))
                    ok++;
                else
                    Fail($"construct weapons: refused for the wrong reason: \"{ex.Message}\" (wanted {want})");
            }
        string[] blueprints = ["Gunner Turret", "Hood Gun Buggy", "Sentry", "Gun Bench", "Target Wall", "Ammo Dump"];
        var noBp = blueprints.Where(b => ConstructBuild.FindBlueprint(b) == null).ToList();
        Line($"files: {guns.Count} guns ({string.Join(", ", guns.Select(g => g.Name))}), {gunParts} gun parts, {boxes} ammo boxes; {ok} of {bad.Length} bad files refused for the right reason; blueprints {(noBp.Count == 0 ? "all build" : "missing " + string.Join(", ", noBp))}");
        if (missing.Count > 0 || gunParts < 10 || boxes < 2 || noBp.Count > 0)
            Fail($"construct weapons: missing guns ({string.Join(", ", missing)}), gun parts, ammo boxes or blueprints ({string.Join(", ", noBp)})");
        if (ConstructParts.Refused.Count > 0)
            Fail($"construct weapons: refused part files: {string.Join("; ", ConstructParts.Refused)}");
    }

    /// <summary>A rifle gun built part by part with the tools, as a player would: what it lacks as it grows, then its numbers.</summary>
    private static void CWeaponToolBuild()
    {
        var w = new ConstructWorld();
        var e = new ConstructEdits();
        const byte me = 3;
        var now = 0.0;
        var ground = ConstructWorld.Ground(World.Query, 6, 2);
        var eye = ground + new Vector3(0, 1.6f, 2.2f);
        var problems = new List<string>();
        void Wait(double s)
        {
            for (var k = 0; k < (int)(s * World.TickRate) + 1; k++)
            {
                now += World.TickDt;
                e.Tick(w, now, _ => eye);
                w.Step(World.Query, World.TickDt);
            }
        }
        e.Hand(me).Held = ConstructParts.Need("Foundation").Id;
        e.SetTool(me, ConstructParts.Need("Foundation").Tool);
        e.BeginPlaceNew(w, me, eye, ground, 0, now);
        Wait(ConstructParts.Need("Foundation").AttachSeconds + 0.1);
        if (w.Bodies.Count != 1)
        {
            Fail("construct weapons: tool build: the foundation wasn't placed");
            return;
        }
        var c = w.Bodies[0];
        int Step(string part, int on, string socket, string by, ConstructTool tool)
        {
            var def = ConstructParts.Need(part);
            e.Hand(me).Held = def.Id;
            e.SetTool(me, tool);
            var ts = Array.FindIndex(c.DefOf(on).Sockets, s => s.Name == socket);
            var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
            if (e.BeginAttach(w, me, eye, c.Id, on, ts, ds * 4, now) is { } why)
            {
                problems.Add($"{part} on {c.DefOf(on).Name} {socket}: {why}");
                return -1;
            }
            var n = c.PartCount;
            Wait(def.AttachSeconds + 0.1);
            return c.PartCount == n + 1 ? n : -1;
        }
        string Problem() => c.GunCount > 0 ? c.Guns[0].Problem : "no gun";
        var block = Step("Frame Block", 0, "top11", "-y", ConstructTool.Welder);
        // The welder can't fit a gun part (bolted: the wrench).
        e.Hand(me).Held = ConstructParts.Need("Rifle Receiver").Id;
        e.SetTool(me, ConstructTool.Welder);
        var welder = e.BeginAttach(w, me, eye, c.Id, block, Array.FindIndex(c.DefOf(block).Sockets, s => s.Name == "+y"), 0, now);
        var massBare = c.Mass;
        var rec = Step("Rifle Receiver", block, "+y", "bottom", ConstructTool.Wrench);
        var p1 = Problem();
        var barrel = Step("Gun Barrel", rec, "front", "back", ConstructTool.Wrench);
        var p2 = Problem();
        var mag = Step("Box Magazine", rec, "left", "right", ConstructTool.Wrench);
        var p3 = Problem();
        var boxes0 = c.GunCount > 0 ? c.Guns[0].BoxCount : -1;
        var box = Step("Ammo Box", mag, "left", "right", ConstructTool.Wrench);
        Step("AP Rounds", rec, "right", "left", ConstructTool.Wrench);
        var spread0 = c.Guns[0].Spread;
        Step("Gun Scope", rec, "top", "bottom", ConstructTool.Wrench);
        var spread1 = c.Guns[0].Spread;
        var sw = Step("Switch", 0, "top33", "-y", ConstructTool.Wrench);
        e.SetTool(me, ConstructTool.Linker);
        var wired = e.Wire(w, me, eye, c.Id, sw, rec);
        ref var g = ref c.Guns[0];
        var def = g.Def;
        var massGun = c.Mass - massBare;
        var expectMass = new[] { "Rifle Receiver", "Gun Barrel", "Box Magazine", "Ammo Box", "AP Rounds", "Gun Scope", "Switch" }.Sum(n => ConstructParts.Need(n).Mass);
        // Fire it: the switch on, past the first load (the magazine's reload).
        c.Parts[sw].State = 1;
        var boxFull = c.Parts[box].Rounds; // (the gun loaded itself from the box as soon as the box went on)
        Wait(1.0);
        var shots = w.ShotsFired;
        var loaded = c.Parts[rec].Rounds;
        var boxNow = c.Parts[box].Rounds;
        c.Parts[sw].State = 0;
        Line($"tool build: the welder on a receiver: \"{welder}\"; problems as it grew: \"{p1}\" / \"{p2}\" / \"{p3}\" (boxes {boxes0}), then \"{Problem()}\" with {g.BoxCount} box; a {def?.Name} at {def?.Rpm} rpm ({g.Interval} ticks), {g.Mag} a load, {def?.Damage} damage, effects {g.Effects}; the scope took its spread {spread0 * 180 / MathF.PI:0.00} to {spread1 * 180 / MathF.PI:0.00} deg; "
             + $"wired by the linking tool {wired ?? "ok"}; the gun weighs {massGun:0.0} kg (its parts {expectMass:0.0}); the switch fired {shots} shots in 1 s ({loaded} left loaded, the box {boxFull} -> {boxNow})");
        if (welder == null || !welder.Contains("wrench"))
            problems.Add("the welder fitted a gun part");
        if (!p1.Contains("barrel") || !p2.Contains("feed") || p3.Length > 0 || boxes0 != 0 || g.Problem.Length > 0 || g.BoxCount != 1)
            problems.Add("the gun's problems didn't follow its parts (no barrel, no feed, then fine; no box, then one)");
        if (def == null || def.Rpm != 600 || g.Interval != 6 || g.Mag != 30 || (g.Effects & GunEffect.Penetrating) == 0 || spread1 >= spread0 * 0.7f)
            problems.Add("the gun's numbers aren't the weapon builder's (600 rpm, a 30-round box magazine, AP penetrating, the scope tighter)");
        if (MathF.Abs(massGun - expectMass) > 0.01f)
            problems.Add("the gun's parts don't weigh what their files say");
        if (wired != null || shots < 8 || shots > 12 || boxNow != boxFull || loaded != 30 - shots || boxFull != ConstructParts.Need("Ammo Box").BoxRounds - 30)
            problems.Add("the switch didn't fire the gun at its rate from a magazine loaded out of the box");
        foreach (var p in problems)
            Fail($"construct weapons: tool build: {p}");
    }

    /// <summary>The Hood Gun Buggy: its fixed gun points the way the buggy does, and its driver's fire key fires it through the input reader.</summary>
    private static void CWeaponHoodGun()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Hood Gun Buggy", 6, 2) is not { } car)
            return;
        var plain = ConstructBuild.Build(new ConstructWorld(), ConstructBuild.FindBlueprint("Buggy")!, Vector3.Zero, Quaternion.Identity, out _);
        StepConstructs(w, 30);
        var worst = 0f;
        foreach (var yaw in new[] { 0f, 1.1f, -2.4f })
        {
            car.Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw);
            ConstructWeapons.MuzzleWorld(car, 0, out _, out var fwd);
            var face = Vector3.Transform(car.SeatForward(), car.Rot);
            worst = MathF.Max(worst, MathF.Acos(Math.Clamp(Vector3.Dot(fwd, face), -1, 1)));
        }
        car.Rot = Quaternion.Identity;
        car.Wake();
        StepConstructs(w, 10);
        var seat = car.DriveSeat;
        var gun = car.Guns[0].Part;
        StepConstructs(w, (int)(car.Guns[0].ReloadTicks + 5), _ => car.Parts[seat].Keys = CPart.Occupied); // (loading)
        var before = w.ShotsFired;
        var spread = 0f;
        ConstructWeapons.MuzzleWorld(car, 0, out var muzzle, out var dir);
        StepConstructs(w, 60, _ =>
        {
            car.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyFire;
            for (var k = 0; k < w.FireCount; k++)
                if (w.Fires[k].Kind == CFireKind.Shot)
                    spread = MathF.Max(spread, MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(w.Fires[k].At - muzzle), dir), -1, 1)));
        });
        var fired = w.ShotsFired - before;
        StepConstructs(w, 30, _ => car.Parts[seat].Keys = CPart.Occupied);
        var after = w.ShotsFired - before - fired;
        var extra = car.Mass - (plain?.Mass ?? 0);
        Line($"hood gun: the gun points within {worst * 180 / MathF.PI:0.00} deg of the buggy's facing as it turns; the driver's fire key fired {fired} shots in 1 s (then {after} with it let go), every round within {spread * 180 / MathF.PI:0.0} deg of the barrel; the buggy weighs {extra:0} kg more with it; drives {car.CanDrive}");
        if (worst > 0.01f || fired < 8 || after > 1 || spread > car.Guns[0].Spread * 1.5f + 0.01f || extra < 40 || !car.CanDrive || gun < 0)
            Fail("construct weapons: the hood gun doesn't point with its buggy, or its driver's fire key doesn't fire it (or it weighs nothing)");
    }

    /// <summary>The Gunner Turret's turntable follows its gunner's look (both ways), and the gunner's fire key fires its gun.</summary>
    private static void CWeaponTurretAim()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Gunner Turret", 6, 2) is not { } t)
            return;
        var seat = PartNamed(t, "Seat");
        var gk = 0;
        StepConstructs(w, 10);
        float GunYaw()
        {
            ConstructWeapons.MuzzleWorld(t, gk, out _, out var f);
            return MathF.Atan2(f.X, -f.Z);
        }
        var errs = new List<string>();
        var worst = 0f;
        foreach (var want in new[] { 1.2f, -2.0f, 3.0f })
        {
            StepConstructs(w, 300, _ =>
            {
                t.Parts[seat].Keys = CPart.Occupied;
                t.Parts[seat].AimYaw = want;
                t.Parts[seat].AimPitch = 0.1f;
            });
            var off = MathF.Abs(World.WrapAngle(GunYaw() - want));
            worst = MathF.Max(worst, off);
            errs.Add($"{want * 180 / MathF.PI:0} deg: {off * 180 / MathF.PI:0.0}");
        }
        var before = w.ShotsFired;
        StepConstructs(w, (int)t.Guns[gk].ReloadTicks + 60, _ =>
        {
            t.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyFire;
            t.Parts[seat].AimYaw = 3.0f;
        });
        var fired = w.ShotsFired - before;
        // Nobody in the seat: the lever on the base turns it again (and the gun holds still).
        StepConstructs(w, 60);
        var a0 = t.Joints[0].Angle;
        t.Parts[PartNamed(t, "Lever")].State = 1;
        StepConstructs(w, 60);
        var lever = MathF.Abs(World.WrapAngle(t.Joints[0].Angle - a0));
        Line($"turret: the gun follows its gunner's look to within ({string.Join(", ", errs)}) deg; the gunner's fire key fired {fired} shots after the load; empty, the lever turned it {lever * 180 / MathF.PI:0} deg in 1 s; the gunner's seat aims {t.SeatAims(seat)}");
        if (worst > 0.05f || fired < 5 || lever < 0.4f || !t.SeatAims(seat))
            Fail("construct weapons: the turret doesn't follow its gunner's look (or its gunner can't fire it, or the lever no longer turns it)");
    }

    /// <summary>The Sentry: a player out of its sensor's range draws no fire; one in range is turned onto and shot at.</summary>
    private static void CWeaponSentry()
    {
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Sentry", 6, 2) is not { } s)
            return;
        var sensor = PartNamed(s, "Proximity Sensor");
        var range = ConstructParts.Need("Proximity Sensor").Settings[s.Parts[sensor].Setting];
        var centre = ConstructRender.PartCentre(s, sensor);
        var far = centre + new Vector3(range + 3f, 0, 0);
        w.PlayerSpots[0] = far with { Y = s.Pos.Y };
        w.PlayerSpotCount = 1;
        var a0 = s.Joints[0].Angle;
        StepConstructs(w, 300);
        var farShots = w.ShotsFired;
        var farTurn = MathF.Abs(World.WrapAngle(s.Joints[0].Angle - a0));
        // In range, off to its right: it turns onto them and fires (after its first load).
        var near = centre + new Vector3(range - 2.5f, 0, 1f);
        w.PlayerSpots[0] = near with { Y = s.Pos.Y };
        var firstAt = -1;
        var aimErr = 0f;
        StepConstructs(w, 420, tick =>
        {
            if (firstAt < 0 && w.ShotsFired > farShots)
            {
                firstAt = tick;
                ConstructWeapons.MuzzleWorld(s, 0, out var m, out var f);
                var to = near with { Y = s.Pos.Y } + new Vector3(0, ConstructWeapons.SentryAimHeight, 0) - m;
                to.Y = 0;
                f.Y = 0;
                aimErr = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(f), Vector3.Normalize(to)), -1, 1));
            }
        });
        var nearShots = w.ShotsFired - farShots;
        // Gone: it stops.
        w.PlayerSpotCount = 0;
        StepConstructs(w, 30);
        var after = w.ShotsFired;
        StepConstructs(w, 120);
        var gone = w.ShotsFired - after;
        Line($"sentry ({range:0} m sensor): a player {range + 3:0} m away drew {farShots} shots (the gun turned {farTurn * 180 / MathF.PI:0.0} deg); one {Vector3.Distance(near, centre):0.0} m away to its right was shot at {nearShots} times, the first after {firstAt / 60f:0.00} s (its load) pointing {aimErr * 180 / MathF.PI:0.0} deg off them; gone, {gone} more");
        if (farShots != 0 || farTurn > 0.02f || nearShots < 10 || firstAt < 0 || aimErr > ConstructWeapons.SentryHoldFire + 0.02f || gone != 0)
            Fail("construct weapons: the sentry doesn't fire only at a player in its sensor's range (turned onto them)");
    }

    /// <summary>A gun fires its box dry (and through a feed chute); with no box it never fires; the HUD's words.</summary>
    private static void CWeaponAmmo()
    {
        var bp = ConstructBuild.ParseBlueprint("""
            { "name": "Ammo Test", "steps": [
              { "part": "Foundation" },
              { "part": "Mounted MG", "on": 0, "socket": "top11", "by": "bottom" },
              { "part": "Feed Chute", "on": 1, "socket": "right", "by": "-x" },
              { "part": "Ammo Box", "on": 2, "socket": "+x", "by": "left" },
              { "part": "Switch", "on": 0, "socket": "top33", "by": "-y" }
            ], "wires": [[4, 1]] }
            """, "test.json");
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 6, 2), 0, out var why);
        if (c == null)
        {
            Fail($"construct weapons: ammo: the test gun doesn't build: {why}");
            return;
        }
        var box = PartNamed(c, "Ammo Box");
        var gun = PartNamed(c, "Mounted MG");
        var viaChute = c.Guns[0].BoxCount;
        c.Parts[box].Rounds = 25;
        c.Parts[PartNamed(c, "Switch")].State = 1;
        StepConstructs(w, (int)c.Guns[0].ReloadTicks + 300);
        var shot = w.ShotsFired;
        var left = c.Parts[gun].Rounds + c.Parts[box].Rounds;
        var dry = ConstructWeapons.BoxRounds(c, 0);
        // The chute shot off: no box reaches the gun.
        w.DamagePart(c, PartNamed(c, "Feed Chute"), 1000, out _);
        StepConstructs(w, 2);
        var noBox = c.Guns[0].BoxCount;
        Line($"ammo: a box through a feed chute feeds the gun ({viaChute} box); 25 rounds in it fired {shot} shots, then {left} left ({dry} in the boxes); the chute shot off: {noBox} boxes reach it");
        if (viaChute != 1 || shot != 25 || left != 0 || dry != 0 || noBox != 0)
            Fail("construct weapons: an ammo box doesn't feed its gun through a chute and run dry");
    }

    /// <summary>A gun's own rounds hole an ammo box: a beat later it cooks off, hurting the parts round it.</summary>
    private static void CWeaponCookOff()
    {
        var (w, gun, target) = RoundsRange("Mounted MG", null, """
            [ { "part": "Foundation" }, { "part": "Frame Block", "on": 0, "socket": "top11", "by": "-y" }, { "part": "Ammo Box", "on": 1, "socket": "+y", "by": "bottom" }, { "part": "Frame Block", "on": 0, "socket": "top31", "by": "-y" } ]
            """);
        if (w == null)
            return;
        var block = PartNamed(target!, "Frame Block", 1); // (the one a metre off the line)
        var hp0 = target!.Parts[block].Health;
        var blasts = 0;
        var cookTick = -1;
        var gone = -1;
        StepConstructs(w, (int)gun!.Guns[0].ReloadTicks + 240, t =>
        {
            for (var k = 0; k < w.FireCount; k++)
                if (w.Fires[k].Kind == CFireKind.CookOff)
                {
                    blasts++;
                    cookTick = t;
                }
            if (gone < 0 && PartNamed(target, "Ammo Box") < 0)
                gone = t;
        });
        var hp1 = PartNamed(target, "Frame Block", 1) is var fb and >= 0 && target.PartCount > fb ? target.Parts[fb].Health : 0; // (blown off: 0)
        Line($"cook-off: the gun's rounds holed the ammo box (gone at tick {gone}), it cooked off {(cookTick - gone) / 60f:0.00} s later ({blasts} blast), the frame block a metre away {hp0:0} -> {hp1:0} hp");
        if (gone < 0 || blasts != 1 || cookTick - gone < 15 || hp1 >= hp0)
            Fail("construct weapons: a shot ammo box doesn't cook off (or its blast hurts nothing round it)");
    }

    /// <summary>
    /// A shooting range: a gun (heavy, with ammo parts bolted on) on a foundation and a target construct 8 m in front of
    /// it at the same height, the gun fired by a switch. Returns the world, the gun's construct and the target's.
    /// </summary>
    private static (ConstructWorld? W, Construct? Gun, Construct? Target) RoundsRange(string gun, string? ammo, string targetSteps, float x = 6, float z = 4)
    {
        var steps = $$"""
            [ { "part": "Foundation" }, { "part": "Frame Block", "on": 0, "socket": "top11", "by": "-y" }, { "part": "{{gun}}", "on": 1, "socket": "+y", "by": "bottom" }, { "part": "Switch", "on": 0, "socket": "top33", "by": "-y" }
              {{(ammo != null ? $$""", { "part": "{{ammo}}", "on": 2, "socket": "top", "by": "bottom" }""" : "")}} ]
            """;
        var gbp = ConstructBuild.ParseBlueprint($$"""{ "name": "Range Gun", "steps": {{steps}}, "wires": [[3, 2]] }""", "range.json");
        var tbp = ConstructBuild.ParseBlueprint($$"""{ "name": "Range Target", "steps": {{targetSteps}} }""", "target.json");
        var w = new ConstructWorld();
        var g = ConstructBuild.BuildOnGround(w, gbp, ConstructWorld.Ground(World.Query, x, z), 0, out var why);
        var t = ConstructBuild.BuildOnGround(w, tbp, ConstructWorld.Ground(World.Query, x, z - 8), 0, out var why2);
        if (g == null || t == null)
        {
            Fail($"construct weapons: the range doesn't build: {why}{why2}");
            return (null, null, null);
        }
        StepConstructs(w, 2);
        if (g.Guns[0].Problem.Length > 0 || g.Guns[0].BoxCount > 0)
        {
            // (no box: the range loads the gun itself)
        }
        g.Parts[g.Guns[0].Part].Rounds = 60000; // (a range gun has its rounds)
        g.Parts[PartNamed(g, "Switch")].State = 1;
        return (w, g, t);
    }

    /// <summary>Penetrating rounds go on through a panel into the block behind it (ball stops at the panel); explosive ones splash a part off the line; incendiary ones set wood alight but not steel.</summary>
    private static void CWeaponRounds()
    {
        const string target = """
            [ { "part": "Foundation" }, { "part": "Frame Block", "on": 0, "socket": "top11", "by": "-y" }, { "part": "Frame Block", "on": 1, "socket": "+y", "by": "-y" },
              { "part": "Wood Panel", "on": 2, "socket": "+z", "by": "back" }, { "part": "Frame Block", "on": 0, "socket": "top30", "by": "-y" } ]
            """;
        var rows = new List<string>();
        var problems = new List<string>();
        foreach (var ammo in new string?[] { null, "AP Rounds", "HE Rounds", "Incendiary Rounds" })
        {
            var (w, g, t) = RoundsRange("Mounted MG", ammo, target);
            if (w == null)
                return;
            int panel = PartNamed(t!, "Wood Panel"), low = PartNamed(t!, "Frame Block"), behind = PartNamed(t!, "Frame Block", 1), side = PartNamed(t!, "Frame Block", 2);
            var at = new Vector3[t!.PartCount];
            for (var i = 0; i < t.PartCount; i++)
                at[i] = t.Parts[i].Pos;
            float Hp(int i)
            {
                // (by where the part sat: a part shot off is gone, at 0)
                for (var j = 0; j < t!.PartCount; j++)
                    if (t.Parts[j].Pos == at[i] && !t.Parts[j].Broken)
                        return t.Parts[j].Health;
                return 0;
            }
            float hp(int i) => i == behind ? Hp(behind) + Hp(low) : Hp(i); // (behind the panel: the block on the line and the one under it)
            float p0 = hp(panel), b0 = hp(behind), s0 = hp(side);
            var through = 0;
            var burnt = false;
            var steelBurnt = false;
            StepConstructs(w, 20, _ =>
            {
                for (var k = 0; k < w.FireCount; k++)
                    if ((w.Fires[k].Hit & CFireHit.Through) != 0)
                        through++;
                for (var i = 0; i < t!.PartCount; i++)
                {
                    burnt |= t.Parts[i].Burn > 0 && t.Parts[i].D.Name == "Wood Panel";
                    steelBurnt |= t.Parts[i].Burn > 0 && t.Parts[i].D.Name != "Wood Panel";
                }
            });
            float dp = p0 - hp(panel), db = b0 - hp(behind), ds = s0 - hp(side);
            var effects = g!.Guns[0].Effects;
            rows.Add($"{ammo ?? "ball"} ({effects}): panel -{dp:0}, block behind -{db:0}, block off the line -{ds:0}{(through > 0 ? $", {through} through" : "")}{(burnt ? ", the wood burns" : "")}");
            switch (ammo)
            {
                case null when dp <= 0 || db > 0 || ds > 0:
                    problems.Add("ball rounds didn't stop at the panel");
                    break;
                case "AP Rounds" when dp <= 0 || db <= 0 || through == 0 || ds > 0:
                    problems.Add("penetrating rounds didn't go through the panel into the block behind it (and only that)");
                    break;
                case "HE Rounds" when dp <= 0 || ds <= 0:
                    problems.Add("explosive rounds didn't splash the block off the line");
                    break;
                case "Incendiary Rounds" when !burnt || steelBurnt || ds > 0:
                    problems.Add("incendiary rounds didn't set the wood alight (or set steel alight)");
                    break;
            }
            if (ammo == "Incendiary Rounds")
            {
                // It burns down by itself once the gun stops.
                if (PartNamed(t!, "Wood Panel") is var wood and >= 0)
                    t!.Parts[wood].Health = MathF.Min(t.Parts[wood].Health, 40);
                g.Parts[PartNamed(g, "Switch")].State = 0;
                var hpStop = hp(panel);
                StepConstructs(w, 600);
                var left = PartNamed(t, "Wood Panel") is var wp and >= 0 ? t.Parts[wp].Health : 0;
                rows.Add($"burning: the panel {hpStop:0} -> {left:0} hp in 10 s with the gun off");
                if (left >= hpStop)
                    problems.Add("a burning part doesn't burn down");
            }
        }
        Line($"rounds: {string.Join("; ", rows)}");
        foreach (var p in problems)
            Fail($"construct weapons: {p}");
    }

    /// <summary>
    /// In an offline game on the yard: the local player in the Gunner Turret's seat turns it with their look (their view
    /// stays where they look) and shoots a player in front of it; the yard's sentry shoots a player who walks up to it,
    /// and not one beyond its range; credit goes to the gunner.
    /// </summary>
    private static void CWeaponInGame(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("construct weapons: in game: no local player");
            return;
        }
        me.ImmuneUntil = 1e9;
        var turret = g.Constructs.Bodies.FirstOrDefault(b => b.GunCount > 0 && b.Guns[0].Seat >= 0);
        var sentry = g.Constructs.Bodies.FirstOrDefault(b => b.GunCount > 0 && b.Guns[0].Sensor != 255);
        if (turret == null || sentry == null)
        {
            Fail("construct weapons: in game: the yard has no gunner turret (or no sentry)");
            return;
        }
        var seat = turret.Guns[0].Seat;
        var feet = ConstructEdits.SeatFeet(turret, seat);
        me.Teleport(ConstructWorld.Ground(World.Query, feet.X, feet.Z + 1.4f));
        Run(g, ref now, 0.3);
        g.SandboxSend(CActKind.Use, body: turret.Id, part: (byte)seat);
        Run(g, ref now, 0.3);
        var seated = g.SandboxHands.Hand(me.Id).Seated;
        // The target: a second player 9 m out from the turret, east of it.
        var victim = g.SelfTestAddHuman();
        g.SelfTestRespawn(victim, now);
        victim.ImmuneUntil = 0;
        var tc = turret.WorldCom;
        victim.Teleport(ConstructWorld.Ground(World.Query, tc.X + 9f, tc.Z + 0.5f));
        uint seq = 0;
        float aimed = 0, lookHeld = 0, aimYawErr = 0, aimPitchErr = 0;
        void Tick(int n)
        {
            for (var k = 0; k < n; k++)
            {
                // The gunner keeps their eye on the target's chest (the seat swings round with the turret).
                var to = victim.Chest - ConstructWeapons.SeatEye(turret, seat);
                g.LookYaw = MathF.Atan2(to.X, -to.Z);
                g.LookPitch = MathF.Asin(Math.Clamp(to.Y / to.Length(), -1, 1));
                var want = g.LookYaw;
                victim.InputQueue.Add(new InputCmd { Seq = ++seq, Yaw = victim.Yaw });
                Run(g, ref now, World.TickDt);
                lookHeld = MathF.Max(lookHeld, MathF.Abs(World.WrapAngle(g.LookYaw - want)));
                ConstructWeapons.MuzzleWorld(turret, 0, out var m, out var f);
                aimed = MathF.Acos(Math.Clamp(Vector3.Dot(f, Vector3.Normalize(victim.Chest - m)), -1, 1));
                var tc2 = victim.Chest - m;
                aimYawErr = World.WrapAngle(MathF.Atan2(f.X, -f.Z) - MathF.Atan2(tc2.X, -tc2.Z));
                aimPitchErr = MathF.Asin(f.Y) - MathF.Asin(Vector3.Normalize(tc2).Y);
            }
        }
        Tick(240);
        var wantYaw = g.LookYaw;
        float aimedAt = aimed, yawAt = aimYawErr, pitchAt = aimPitchErr; // (before the shooting: the target falls)
        var hp0 = victim.Health;
        var credit0 = me.Kills;
        var fired0 = g.Constructs.ShotsFired;
        var miss = float.MaxValue;
        g.SandboxSend(CActKind.SeatKeys, part: 1); // fire held (seated keys: fire, aim, reload)
        for (var k = 0; k < (int)turret.Guns[0].ReloadTicks + 90; k++)
        {
            Tick(1);
            if (g.Constructs.LastShotBody == turret.Id)
                miss = MathF.Min(miss, Vector3.Distance(g.Constructs.LastShotEnd, victim.Chest));
        }
        var fired = g.Constructs.ShotsFired - fired0;
        g.SandboxSend(CActKind.SeatKeys, part: 0);
        Tick(2);
        var hurt = hp0 - Math.Max(0, victim.Health);
        var killed = !victim.Alive;
        g.SandboxSend(CActKind.Stand);
        Tick(10);
        // The sentry: a player beyond its range, then one walking into it.
        var walker = g.SelfTestAddHuman();
        g.SelfTestRespawn(walker, now);
        walker.ImmuneUntil = 0;
        var sensor = sentry.Guns[0].Sensor;
        var range = sentry.DefOf(sensor).Settings[sentry.Parts[sensor].Setting];
        var sc = ConstructRender.PartCentre(sentry, sensor);
        walker.Teleport(ConstructWorld.Ground(World.Query, sc.X - range - 4, sc.Z));
        var shots0 = g.Constructs.ShotsFired;
        void Walk(int n)
        {
            for (var k = 0; k < n; k++)
            {
                walker.InputQueue.Add(new InputCmd { Seq = ++seq, Yaw = walker.Yaw });
                victim.InputQueue.Add(new InputCmd { Seq = seq, Yaw = victim.Yaw });
                Run(g, ref now, World.TickDt);
            }
        }
        me.Teleport(ConstructWorld.Ground(World.Query, sc.X + range + 10, sc.Z + range + 10)); // (the local player well out of it)
        Walk(240);
        var farShots = g.Constructs.ShotsFired - shots0;
        var whp = walker.Health;
        walker.Teleport(ConstructWorld.Ground(World.Query, sc.X - range * 0.5f, sc.Z));
        Walk(300);
        var nearShots = g.Constructs.ShotsFired - shots0 - farShots;
        var walkerHurt = whp - Math.Max(0, walker.Health);
        Line($"in game: the local player sat in the gunner turret ({seated}); looking {wantYaw * 180 / MathF.PI:0} deg turned its gun to within {aimedAt * 180 / MathF.PI:0.0} deg of them (bearing {yawAt * 180 / MathF.PI:0.0}, elevation {pitchAt * 180 / MathF.PI:0.0}) (the view held within {lookHeld * 180 / MathF.PI:0.0} deg); firing {fired} shots (the nearest ending {miss:0.00} m from their chest) hurt the player 9 m out {hurt} ({(killed ? "killed" : "alive")}; the gunner's kills {credit0} -> {me.Kills}); "
             + $"the yard's sentry ({range:0} m) shot {farShots} times at a player {range + 4:0} m off, {nearShots} at one {range * 0.5f:0.0} m off (hurt {walkerHurt})");
        if (!seated || aimedAt > 0.03f || lookHeld > 0.01f)
            Fail("construct weapons: in a game the gunner's look doesn't turn the turret (or their view turns with it)");
        if (hurt <= 0)
            Fail("construct weapons: in a game the gunner's turret gun doesn't hurt the player it is aimed at");
        if (farShots != 0 || nearShots < 5 || walkerHurt <= 0)
            Fail("construct weapons: in a game the sentry doesn't shoot only a player in its range");
    }

    private static void CWeaponNoAllocations()
    {
        // A sentry turning onto a player and firing into the ground, a gunner's turret firing, steady state.
        var w = new ConstructWorld();
        if (BuildBlueprint(w, "Sentry", 6, 2) is not { } s || BuildBlueprint(w, "Gunner Turret", 6, -6) is not { } t)
            return;
        var seat = PartNamed(t, "Seat");
        foreach (var c in new[] { s, t })
            c.Parts[c.Guns[0].Part].Rounds = 60000;
        var spot = ConstructRender.PartCentre(s, s.Guns[0].Sensor) + new Vector3(3, 0, 1);
        var writer = new PacketWriter(16384);
        var yaw = 0f;
        void Tick()
        {
            w.PlayerSpots[0] = spot with { Y = s.Pos.Y };
            w.PlayerSpotCount = 1;
            yaw = World.WrapAngle(yaw + 0.02f);
            t.Parts[seat].Keys = CPart.Occupied | ConstructParts.KeyFire;
            t.Parts[seat].AimYaw = yaw;
            t.Parts[seat].AimPitch = -0.3f;
            w.Step(World.Query, World.TickDt);
            if (w.FireCount > 0)
                ConstructNet.WriteFires(writer, w);
            for (var from = 0; from < w.AmmoDirty.Count;)
                from = ConstructNet.WriteAmmo(writer, w, w.AmmoDirty, from);
            w.AmmoDirty.Clear();
        }
        for (var k = 0; k < 120; k++)
            Tick(); // (warm up: JIT, the first loads)
        var shots0 = w.ShotsFired;
        var sw = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 120; k++)
            Tick();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var ms = sw.Elapsed.TotalMilliseconds / 120;
        var shots = w.ShotsFired - shots0;
        Line($"no allocation: a sentry and a gunner's turret firing {shots} shots in 120 ticks (aiming, loading, the fire and ammo messages written): {bytes} bytes allocated, {ms:0.000} ms a tick");
        if (bytes > 0)
            Fail($"construct weapons: {bytes} bytes allocated in 120 ticks of firing (should be none)");
        if (shots < 20)
            Fail("construct weapons: the allocation check's guns didn't fire");
    }

    /// <summary>A real host and client: a gun the server fires is seen firing on the client (tracers from its copy of the gun), with its ammo; a late joiner gets the rounds.</summary>
    private static void CWeaponLoopback(Map yard)
    {
        const ushort port = 26297;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        var bench = server.Constructs.Bodies.FirstOrDefault(b => b.GunCount > 0 && b.Guns[0].Def is { } d && b.DefOf(b.Guns[0].Part).IsReceiverGun);
        if (bench == null)
        {
            Fail("constructs weapons loopback: the yard has no gun bench (a receiver gun)");
            return;
        }
        var sw = PartNamed(bench, "Switch");
        bench.Parts[sw].State = 1;
        var seen0 = a.ConstructShotsSeen;
        Pump(server, [a], bench.Guns[0].ReloadTicks / 60.0 + 1.5, ref now, clock);
        bench.Parts[sw].State = 0;
        Pump(server, [a], 0.5, ref now, clock);
        var shots = server.Constructs.ShotsFired;
        var seen = a.ConstructShotsSeen - seen0;
        var copy = a.Constructs.Get(bench.Id);
        var box = PartNamed(bench, "Ammo Box");
        var gun = bench.Guns[0].Part;
        var sameAmmo = copy != null && copy.Parts[box].Rounds == bench.Parts[box].Rounds && copy.Parts[gun].Rounds == bench.Parts[gun].Rounds;
        var clientGun = copy != null && copy.GunCount == bench.GunCount && copy.Guns[0].Problem == bench.Guns[0].Problem && copy.Guns[0].BoxCount == bench.Guns[0].BoxCount;
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 1.5, ref now, clock);
        var late = b.Constructs.Get(bench.Id) is { } lc && lc.Parts[box].Rounds == bench.Parts[box].Rounds && lc.Parts[gun].Rounds == bench.Parts[gun].Rounds;
        Line($"loopback: the server's gun bench fired {shots} shots ({bench.Guns[0].Shots} rounds each); the client drew {seen} rounds from its copy of the gun ({a.Tracers.Count} tracers live), its ammo the server's {sameAmmo} (box {bench.Parts[box].Rounds}, loaded {bench.Parts[gun].Rounds}), its gun worked out the same {clientGun}; a late joiner got the rounds {late}");
        if (shots == 0 || seen < shots * bench.Guns[0].Shots || !sameAmmo || !clientGun || !late)
            Fail("construct weapons loopback: a client doesn't see the server's gun fire (or its ammo, or a late joiner doesn't get the rounds)");
    }
}
