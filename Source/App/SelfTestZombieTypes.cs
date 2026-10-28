using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie types" (Source/Zombies/ZombieTypes.cs, Source/Game/ZombieTypes.cs; docs/zombies/ZOMBIES-MODE.md
/// "Zombie types"): the shipped types load (and the hard rule holds: no type reaches past the melee limit, a type with a
/// ranged key is refused), each type spawns with its health, gait, size and hit box, moves at its speed and attacks as
/// specified (the brute's shove and its shrugged-off stun, the armoured one's plates shot off before the body, the
/// grabber's hold and its release when hit, the pouncer's leap that still only hurts in reach, the bloater's burst off by
/// default and in reach only when on), a wall climber climbs a wall its prey stands on, the boss rounds, the spawn mix
/// by round (walkers early, specials phased in and capped, packs), a client sees the types, no allocations and the tick
/// cost of a mixed horde. Short seeded scenarios on Horde Test.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombieTypes()
    {
        if (Maps.Find("Horde Test") is not { Zombies: { } zm } map)
        {
            Fail("no Horde Test map");
            return;
        }
        var rulesText = File.ReadAllText(ZombiesRules.PathName);
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.PowerFirstAt = 0;
            r.PowerDropChance = 0f;
        }, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var arena = zm.AreaIndex("Arena");
        var rules = g.ZRules;
        var types = rules.Types;
        var t = now;
        var spawner = zm.Spawners.FindIndex(s => s.Window >= 0);
        Vector3 At(float x, float z) => new(x, map.GroundHeight(x, z), z);
        int T(string key) => rules.TypeIndex(key);
        Zombie Z(string key, Vector3 p, float yaw = 0)
        {
            var z = g.SelfTestSpawnTyped(spawner, T(key), t);
            z.State = ZState.Chase;
            z.Window = -1;
            z.Area = arena;
            z.Sim = new MoveState { Position = p, Grounded = true };
            z.PrevPos = p;
            z.Yaw = yaw;
            for (var i = 0; i < z.History.Length; i++)
                z.History[i] = p;
            return z;
        }
        void Clear()
        {
            foreach (var z in g.Horde)
                z.State = ZState.Dead;
            g.SelfTestTickHorde(t += 2);
            me.Health = World.MaxHealth;
            g.ZP(me).Downed = false; // (never left downed: the next scenario needs a target)
        }
        me.Teleport(At(-9, 14));

        // ---- The rules: the shipped types, the melee limit, a ranged key refused ----
        {
            var names = string.Join(", ", types.Select(k => $"{k.Key}{(k.Enabled ? "" : " (off)")}"));
            var reaches = string.Join(", ", types.Select(k => $"{k.Key} {k.ReachFor(rules):0.00} m"));
            var worst = types.Max(k => k.ReachFor(rules));
            string? refused = null;
            try
            {
                ZombiesRules.Load(rulesText.Replace("\"knockback\": 7,", "\"knockback\": 7, \"spitRange\": 12,"), "ranged test");
            }
            catch (WeaponFileException ex)
            {
                refused = ex.Message;
            }
            string? farRefused = null;
            try
            {
                ZombiesRules.Load(rulesText.Replace("\"reachMult\": 1.6,", "\"reachMult\": 2.0,"), "reach test");
            }
            catch (WeaponFileException ex)
            {
                farRefused = ex.Message;
            }
            var defaults = ZombieTypeDef.Defaults();
            var same = defaults.Count == types.Count && defaults.Zip(types).All(p => p.First.Key == p.Second.Key && p.First.Enabled == p.Second.Enabled && p.First.Special == p.Second.Special);
            Line($"rules: {types.Count} types ({names}); reaches {reaches}; the furthest {worst:0.00} m of {ZombieTypeDef.MaxReach} m allowed; a \"spitRange\" key: {refused ?? "ACCEPTED"}; a 2.5 m reach: {farRefused ?? "ACCEPTED"}; the built-in defaults match the file {same}");
            if (types.Count < 9 || types[0].Key != "walker" || T("brute") < 0 || T("skitterer") < 0 || T("climber") < 0 || T("armoured") < 0 || T("grabber") < 0 || T("pouncer") < 0 || T("bloater") < 0 || T("juggernaut") < 0)
                Fail("the rules should ship the walker, brute, skitterer, wall climber, armoured, grabber, pouncer, bloater and juggernaut");
            if (worst > ZombieTypeDef.MaxReach || refused == null || farRefused == null || !same)
                Fail("no type may reach past the melee limit, a ranged key must be refused, and the built-in defaults must match the shipped file");
            if (types[T("bloater")].Enabled)
                Fail("the bloater ships switched off (the developer's question: is a burst a ranged attack?)");
            foreach (var k in types)
                if (k.Special == "none" && k.Key != "walker" && k.Key != "skitterer")
                    Fail($"{k.Key} has no special");
        }

        // ---- Spawning: health, gait, size, hit box, plates; the speed of each ----
        {
            var round = Math.Max(1, g.ZRound);
            var baseHp = rules.ZombieHealth(round);
            var rows = new List<string>();
            var bad = false;
            foreach (var k in types)
            {
                var z = Z(k.Key, At(-9, -6));
                var box = Game.ZombieBox(z, z.Position, z.Yaw);
                var wantHp = k.HealthFixed > 0 ? k.HealthFixed : (int)MathF.Round(baseHp * k.HealthMult);
                var speed = g.SelfTestSpeedOf(z);
                var wantSpeed = g.ZombieNominalSpeed(k.Gait >= 0 ? (ZSpeed)k.Gait : z.Speed) * k.SpeedMult;
                rows.Add($"{k.Key}: {z.Health} hp, {z.Speed} {speed:0.00} m/s, box {box.Half.X * 2:0.00} x {box.Half.Y * 2:0.00} m, plates {z.Plates}, reach {g.ZombieReachFor(z):0.00} m");
                if (z.Health != wantHp || z.Kind != k || z.Type != types.IndexOf(k) || MathF.Abs(speed - wantSpeed) > 0.01f || (k.Gait >= 0 && (int)z.Speed != k.Gait)
                    || MathF.Abs(box.Half.Y * 2 - World.PlayerHeight * k.Height) > 0.01f || MathF.Abs(box.Half.X * 2 - World.PlayerRadius * 2 * k.Width) > 0.01f || (k.Armour > 0) != (z.Plates == 3))
                    bad = true;
                z.State = ZState.Dead;
            }
            Line("spawned: " + string.Join("; ", rows));
            if (bad)
                Fail("each type should spawn with its health (the round's x its multiplier), gait, speed, scaled hit box and plates");
            g.SelfTestTickHorde(t += 2);

            // Moving: every type comes at the player at its speed (the climber and pouncer on open ground like the rest).
            me.Teleport(At(-9, 10));
            var zs = types.Select((k, i) => Z(k.Key, At(-20 + i * 2.5f, -17))).ToList();
            var from = zs.Select(z => z.Position).ToArray();
            for (var k = 0; k < 90; k++)
                g.SelfTestZombiesTick(t += World.TickDt);
            var moved = zs.Select((z, i) => Flat2(z.Position - from[i])).ToArray();
            Line("moving 1.5 s: " + string.Join(", ", zs.Select((z, i) => $"{z.Kind.Key} {moved[i]:0.0} m ({moved[i] / 1.5f / MathF.Max(0.01f, g.SelfTestSpeedOf(z)):0.00}x)")));
            for (var i = 0; i < zs.Count; i++)
                if (moved[i] < g.SelfTestSpeedOf(zs[i]) * 1.5f * 0.5f)
                    Fail($"{zs[i].Kind.Key} should come at the player at its speed (moved {moved[i]:0.0} m in 1.5 s at {g.SelfTestSpeedOf(zs[i]):0.0} m/s)");
            Clear();
        }

        // ---- Attacking: every type hurts a player next to it, as specified; nothing hurts from further than its reach ----
        {
            me.ImmuneUntil = 0;
            var results = new List<(string Key, int Damage, double At, int Hits)>();
            var zp = At(-9, -6);
            foreach (var k in types)
            {
                me.Health = World.MaxHealth;
                var stand = zp + new Vector3(0, 0, -1.0f);
                me.Teleport(stand);
                var z = Z(k.Key, zp);
                var t0 = t;
                var dmg = 0;
                var at = -1.0;
                var hits = 0;
                for (var i = 0; i < 240 && hits == 0; i++)
                {
                    g.SelfTestZombiesTick(t += World.TickDt);
                    me.Teleport(stand);
                    me.Sim.Velocity = Vector3.Zero;
                    if (me.Health < World.MaxHealth)
                    {
                        at = t - t0;
                        hits++;
                        dmg = World.MaxHealth - me.Health; // the first hit's damage (a grabber's hold starts with it)
                        me.Health = World.MaxHealth;
                    }
                }
                results.Add((k.Key, dmg, at, hits));
                if (g.ZP(me).Held)
                    g.SelfTestLimbShot(me, z, z.Position + new Vector3(0, 1.1f * k.Height, -0.4f * k.Width), 5, t);
                Clear();
            }
            var walker = results[0];
            Line("attacks: " + string.Join(", ", results.Select(r => $"{r.Key} {r.Damage} at {r.At:0.00} s")));
            if (walker.Damage <= 0)
                Fail("a walker should swipe a player next to it");
            foreach (var r in results)
            {
                var k = types[T(r.Key)];
                if (!k.Enabled)
                    continue;
                if (r.Damage <= 0)
                    Fail($"{r.Key} should hurt a player next to it");
                else if (k.DamageMult > 1.2f && r.Damage <= walker.Damage)
                    Fail($"{r.Key} should hit harder than a walker ({r.Damage} vs {walker.Damage})");
                else if (k.DamageMult < 0.8f && r.Damage >= walker.Damage && k.GrabSeconds <= 0)
                    Fail($"{r.Key} should hit softer than a walker ({r.Damage} vs {walker.Damage})");
            }
            me.ImmuneUntil = 1e9;
            me.Health = World.MaxHealth;
        }

        // ---- The brute: a shove, and it shrugs off a stun ----
        {
            me.ImmuneUntil = 0;
            me.Health = World.MaxHealth;
            var zp = At(-9, -6);
            me.Teleport(zp + new Vector3(0, 0, -1.0f));
            var z = Z("brute", zp);
            var knocks0 = g.KnocksApplied;
            var pushed = 0f;
            var ticks = 0;
            for (var i = 0; i < 180 && g.KnocksApplied == knocks0; i++)
            {
                g.SelfTestZombiesTick(t += World.TickDt);
                ticks++;
            }
            var vel = me.Sim.Velocity;
            for (var i = 0; i < 20; i++)
            {
                g.SelfTestZombiesTick(t += World.TickDt);
                me.Sim.Position += me.Sim.Velocity * World.TickDt; // (the test moves the player by its velocity: no input loop here)
                me.Sim.Velocity.Y -= World.Gravity * World.TickDt;
                if (me.Sim.Position.Y < zp.Y)
                {
                    me.Sim.Position.Y = zp.Y;
                    me.Sim.Velocity.Y = 0;
                }
            }
            pushed = Flat2(me.Position - zp);
            z.StunnedUntil = t + 5;
            var before = z.Position;
            me.Teleport(zp + new Vector3(0, 0, -6f));
            for (var i = 0; i < 30; i++)
                g.SelfTestZombiesTick(t += World.TickDt);
            var stunnedMoved = Flat2(z.Position - before);
            Line($"brute: the first hit after {ticks} ticks threw the player back at {Flat2(vel):0.0} m/s ({g.KnocksApplied - knocks0} push{(g.KnocksApplied - knocks0 == 1 ? "" : "es")}), to {pushed:0.0} m away; stunned for 5 s it still walked {stunnedMoved:0.00} m in half a second; health {z.Health} ({z.Kind.HealthMult}x)");
            if (g.KnocksApplied - knocks0 < 1 || Flat2(vel) < 3f || pushed < 1.2f)
                Fail("a brute's hit should throw the player back");
            if (stunnedMoved < 0.3f)
                Fail("a brute should shrug off a stun");
            me.Sim.Velocity = Vector3.Zero;
            Clear();
            me.ImmuneUntil = 1e9;
        }

        // ---- The armoured: the helmet and chest plate take shots until they come off; limbs are bare ----
        {
            var zp = At(-9, -6);
            var z = Z("armoured", zp);
            var k = z.Kind;
            var hp0 = z.Health;
            var head = zp + new Vector3(0, 1.6f * k.Height, -0.4f * k.Width);
            var chest = zp + new Vector3(0, 1.1f * k.Height, -0.4f * k.Width);
            var leg = zp + new Vector3(0.2f * k.Width, 0.5f * k.Height, -0.4f * k.Width);
            var f1 = g.SelfTestLimbShot(me, z, head, 100, t);
            var afterHead = z.Health;
            var platesAfter1 = z.Plates;
            g.SelfTestLimbShot(me, z, leg, 40, t);
            var afterLeg = z.Health;
            var shots = 0;
            while ((z.Plates & 1) != 0 && shots < 10)
            {
                g.SelfTestLimbShot(me, z, head, 100, t);
                shots++;
            }
            var afterPlatesOff = z.Health;
            var f2 = g.SelfTestLimbShot(me, z, head, 100, t);
            var afterBareHead = z.Health;
            g.SelfTestLimbShot(me, z, chest, 100, t);
            var chestPlateLeft = z.PlateHpChest;
            Line($"armoured: a 100 head shot on the helmet: health {hp0} -> {afterHead} (hit flags {f1}, plates {platesAfter1}); a 40 leg shot -> {afterLeg}; the helmet came off after {shots} more ({g.ZPlatesOff} plates off), then a head shot -> {afterBareHead} (flags {f2}); a chest shot left the chest plate at {chestPlateLeft:0} of {k.Armour}");
            if (afterHead != hp0 || (f1 & 1) == 0 || platesAfter1 != 3 || afterLeg != hp0 - 40 || afterPlatesOff != afterLeg || (z.Plates & 1) != 0 || afterBareHead >= afterPlatesOff || chestPlateLeft != k.Armour - 100)
                Fail("an armoured zombie's plates should take head and chest shots until they come off; limbs are bare");
            Clear();
        }

        // ---- The grabber: holds you still until it is hit ----
        {
            me.ImmuneUntil = 0;
            me.Health = World.MaxHealth;
            var zp = At(-9, -6);
            var stand = zp + new Vector3(0, 0, -1.0f);
            me.Teleport(stand);
            var z = Z("grabber", zp);
            var held = -1.0;
            var t0 = t;
            for (var i = 0; i < 240 && held < 0; i++)
            {
                g.SelfTestZombiesTick(t += World.TickDt);
                if (g.ZP(me).Held)
                    held = t - t0;
            }
            var mult = g.ZombieMoveMult(me);
            var hp1 = me.Health;
            for (var i = 0; i < 75; i++)
                g.SelfTestZombiesTick(t += World.TickDt);
            var bites = hp1 - me.Health;
            var stillHeld = g.ZP(me).Held && z.Grabbing;
            g.SelfTestLimbShot(me, z, zp + new Vector3(0, 1.1f * z.Kind.Height, -0.4f * z.Kind.Width), 5, t);
            g.SelfTestZombiesTick(t += World.TickDt);
            var freed = !g.ZP(me).Held && !z.Grabbing && g.ZP(me).HeldBy == 0;
            var multAfter = g.ZombieMoveMult(me);
            z.State = ZState.Dead; // (out of the way of the next hold)
            // Timed out: a hold ends on its own.
            var grabs1 = g.ZGrabs;
            var z2 = Z("grabber", zp + new Vector3(0.3f, 0, 0));
            var held2 = false;
            for (var i = 0; i < 240 && !held2; i++)
            {
                g.SelfTestZombiesTick(t += World.TickDt);
                held2 = g.ZP(me).Held;
            }
            var hold = g.ZP(me).HeldUntil - t;
            me.ImmuneUntil = 1e9; // (its bites don't matter here: the hold's clock does)
            for (var i = 0; i < 60 * z2.Kind.GrabSeconds + 30; i++)
                g.SelfTestZombiesTick(t += World.TickDt);
            var timedOut = !g.ZP(me).Held && !z2.Grabbing;
            Line($"grabber: held after {held:0.00} s (move x{mult:0.00}), bitten for {bites} over 1.25 s, still held {stillHeld}; a 5 damage hit freed {freed} (move x{multAfter:0.00}); a second hold ({hold:0.0} s left of {z2.Kind.GrabSeconds}) ended on its own {timedOut}; grabs {g.ZGrabs - grabs1} here, releases {g.ZGrabReleases}");
            if (held < 0 || mult > 0.05f || bites <= 0 || !stillHeld || !freed || multAfter < 1f || !held2 || !timedOut || g.ZGrabs - grabs1 != 1 || g.ZGrabReleases != g.ZGrabs)
                Fail("a grabber should hold a player still (biting) until it is hit or the hold runs out");
            me.ImmuneUntil = 1e9;
            Clear();
        }

        // ---- The pouncer: a leap from a few metres, then a swipe where it lands; nothing from further than its reach ----
        {
            me.ImmuneUntil = 0;
            me.Health = World.MaxHealth;
            var zp = At(-9, -10);
            var stand = At(-9, -5.5f);
            me.Teleport(stand);
            var z = Z("pouncer", zp);
            var pounces0 = g.ZPounces;
            var leftGround = false;
            var hurtAt = -1.0;
            var pounceAt = -1.0;
            var t0 = t;
            var farthestHurt = 0f;
            for (var i = 0; i < 240 && hurtAt < 0; i++)
            {
                g.SelfTestZombiesTick(t += World.TickDt);
                me.Teleport(stand);
                if (g.ZPounces > pounces0 && pounceAt < 0)
                    pounceAt = t - t0;
                if (!z.Sim.Grounded)
                    leftGround = true;
                if (me.Health < World.MaxHealth)
                {
                    hurtAt = t - t0;
                    farthestHurt = Flat2(z.Position - me.Position);
                }
            }
            var reach = g.ZombieReachFor(z) + World.PlayerRadius + 0.45f;
            Line($"pouncer: leapt at {pounceAt:0.00} s from {Flat2(zp - stand):0.0} m (in the air {leftGround}), hurt the player at {hurtAt:0.00} s from {farthestHurt:0.00} m (its reach {reach:0.00} m); pounces {g.ZPounces - pounces0}");
            if (pounceAt < 0 || !leftGround || hurtAt < 0 || farthestHurt > reach + 0.1f)
                Fail("a pouncer should leap at a player a few metres off and hurt them only once in reach");
            // Nothing hurts from beyond reach: a player 3 m off and a pouncer that may not leap yet.
            Clear();
            me.Health = World.MaxHealth;
            var z2 = Z("pouncer", zp);
            z2.PounceReadyAt = t + 100;
            me.Teleport(At(-9, -7));
            for (var i = 0; i < 60; i++)
            {
                g.SelfTestZombiesTick(t += World.TickDt);
                me.Teleport(At(-9, -7));
                z2.Sim.Position = zp;
                z2.PrevPos = zp;
            }
            var farHurt = World.MaxHealth - me.Health;
            Line($"a pouncer held 3 m off (no leap): hurt {farHurt}");
            if (farHurt != 0)
                Fail("no type hurts a player it hasn't reached");
            me.ImmuneUntil = 1e9;
            Clear();
        }

        // ---- The bloater: off by default; on, it bursts only on whoever is right next to it ----
        {
            var zp = At(-9, -6);
            me.ImmuneUntil = 0;
            me.Health = World.MaxHealth;
            var off = Z("bloater", zp);
            var bursts0 = g.ZBursts;
            using var gb = ZGame(0, ref now, tweak: r =>
            {
                r.FirstRoundDelay = 9999;
                r.PowerFirstAt = 0;
                r.PowerDropChance = 0f;
                r.Types[r.TypeIndex("bloater")].Enabled = true;
            }, onMap: map);
            World.SetMap(map);
            var mb = gb.Local!;
            mb.ImmuneUntil = 0;
            var tb = now;
            var zb = gb.SelfTestSpawnTyped(spawner, gb.ZRules.TypeIndex("bloater"), tb);
            zb.State = ZState.Chase;
            zb.Window = -1;
            zb.Area = arena;
            zb.Sim = new MoveState { Position = zp, Grounded = true };
            zb.PrevPos = zp;
            mb.Teleport(zp + new Vector3(0, 0, -1.0f));
            var t0 = tb;
            for (var i = 0; i < 240 && zb.Alive; i++)
            {
                gb.SelfTestZombiesTick(tb += World.TickDt);
                mb.Teleport(zp + new Vector3(0, 0, -1.0f));
            }
            var burstDamage = World.MaxHealth - mb.Health;
            var burstAt = tb - t0;
            var kills = gb.ZP(mb).Kills;
            // Shot dead from 4 m off: it bursts, and nobody that far off feels it.
            mb.Health = World.MaxHealth;
            var zb2 = gb.SelfTestSpawnTyped(spawner, gb.ZRules.TypeIndex("bloater"), tb);
            zb2.State = ZState.Chase;
            zb2.Window = -1;
            zb2.Area = arena;
            zb2.Sim = new MoveState { Position = zp, Grounded = true };
            zb2.PrevPos = zp;
            for (var i = 0; i < zb2.History.Length; i++)
                zb2.History[i] = zp;
            mb.Teleport(zp + new Vector3(0, 0, -4f));
            gb.SelfTestLimbShot(mb, zb2, zp + new Vector3(0, 1.1f * zb2.Kind.Height, -0.4f * zb2.Kind.Width), 100000, tb);
            var farDamage = World.MaxHealth - mb.Health;
            Line($"bloater: shipped {(off.Kind.Enabled ? "ON" : "off")} (bursts {g.ZBursts - bursts0} when it hit in the default rules); switched on, its swipe burst at {burstAt:0.00} s for {burstDamage} on the player next to it (dead {!zb.Alive}, kills credited {kills}); shot dead from 4 m: bursts {gb.ZBursts}, the player 4 m off took {farDamage}");
            if (off.Kind.Enabled || burstDamage <= 0 || zb.Alive || kills != 0 || gb.ZBursts != 2 || farDamage != 0)
                Fail("the bloater ships off; on, its burst hurts only whoever is right next to it (never from a distance)");
            me.ImmuneUntil = 1e9;
            World.SetMap(map);
            Clear();
        }

        // ---- The wall climber: climbs the wall its prey stands on top of; a walker takes the long way ----
        {
            var top = new Vector3(-14f, 3.2f, 0f); // the 3.2 m tower west of the dais (stairs on its east side)
            me.Teleport(top);
            var zc = Z("climber", At(-17.2f, 0f), yaw: MathF.PI / 2);
            var zw = Z("walker", At(-17.2f, -1.2f), yaw: MathF.PI / 2);
            var scaled = false;
            var reachedAt = -1.0;
            var t0 = t;
            float highest = 0;
            for (var i = 0; i < 600 && reachedAt < 0; i++)
            {
                g.SelfTestZombiesTick(t += World.TickDt);
                me.Teleport(top);
                if (zc.State == ZState.Scaling)
                    scaled = true;
                highest = MathF.Max(highest, zc.Position.Y);
                if (zc.Position.Y > 3.1f && zc.State == ZState.Chase && Flat2(zc.Position - top) < 3f)
                    reachedAt = t - t0;
            }
            Line($"wall climber: climbed {scaled} (started {g.ZFreeClimbs}, over the top {g.ZFreeClimbOver}), highest {highest:0.0} m, on the tower top beside the player at {reachedAt:0.0} s; the walker beside it climbed {g.ZFreeClimbs > 1} and is at {zw.Position.Y:0.0} m");
            if (!scaled || g.ZFreeClimbs != 1 || g.ZFreeClimbOver != 1 || reachedAt < 0 || reachedAt > 6)
                Fail("a wall climber should scale the tower wall to the player on top (and a walker never climbs)");
            // Shot on the wall: it lets go and falls.
            Clear();
            me.Teleport(top);
            var zc2 = Z("climber", At(-17.2f, 0f), yaw: MathF.PI / 2);
            for (var i = 0; i < 90 && zc2.State != ZState.Scaling; i++)
                g.SelfTestZombiesTick(t += World.TickDt);
            for (var i = 0; i < 30; i++)
                g.SelfTestZombiesTick(t += World.TickDt);
            var onWall = zc2.State == ZState.Scaling && zc2.Position.Y > 0.5f;
            var yBefore = zc2.Position.Y;
            g.SelfTestLimbShot(me, zc2, zc2.Position + new Vector3(0, 1.0f * zc2.Kind.Height, 0), 5, t, from: zc2.Position + new Vector3(-3, 1.0f * zc2.Kind.Height, 0));
            var fell = zc2.State != ZState.Scaling && !zc2.FreeClimb;
            for (var i = 0; i < 60; i++)
                g.SelfTestZombiesTick(t += World.TickDt);
            Line($"shot on the wall at {yBefore:0.0} m: let go {fell} (on the wall before {onWall}), a second later at {zc2.Position.Y:0.0} m, stunned {zc2.StunnedUntil > t}, fell {g.ClimbFell}");
            if (!onWall || !fell || zc2.Position.Y > yBefore - 0.3f)
                Fail("a wall climber shot on the wall should let go and fall");
            Clear();
        }

        // ---- Boss rounds: one juggernaut every 10 rounds, on top of the round ----
        {
            var jug = T("juggernaut");
            var due = new List<int>();
            foreach (var r in new[] { 1, 9, 10, 11, 20, 30 })
            {
                g.SelfTestTypesBeginRound(r);
                due.Add(g.SelfTestBossDue);
            }
            g.SelfTestTypesBeginRound(10);
            var first = g.SelfTestPickType(10, []);
            var second = g.SelfTestPickType(10, []);
            g.SelfTestTypesBeginRound(1);
            Line($"boss rounds: due in rounds 1/9/10/11/20/30: {string.Join("/", due.Select(d => d < 0 ? "-" : types[d].Key))}; in round 10 the first spawn is a {types[first].Key}, the next a {types[second].Key}; every {types[jug].BossEvery} rounds, {types[jug].HealthMult}x health, {types[jug].Width}x{types[jug].Height} size, {types[jug].PointsMult}x points");
            if (due[0] != -1 || due[1] != -1 || due[2] != jug || due[3] != -1 || due[4] != jug || due[5] != jug || first != jug || second == jug)
                Fail("a juggernaut should be due every 10 rounds, once, first out");
            var zj = Z("juggernaut", At(-9, -6));
            var boss = g.ZBoss();
            Line($"the boss on the HUD: {boss?.Kind.Label ?? "none"} at {(boss != null ? g.ZombieHealthFraction(boss) : 0):0.00}");
            if (boss != zj)
                Fail("the juggernaut should show as the boss");
            Clear();
        }

        // ---- The spawn mix by round: walkers early, specials phased in and capped ----
        {
            var rows = new List<string>();
            var bad = false;
            foreach (var round in new[] { 1, 3, 5, 8, 12, 20, 30 })
            {
                var counts = new int[types.Count];
                for (var i = 0; i < 600; i++)
                    counts[g.SelfTestPickType(round, [])]++;
                var specials = 600 - counts[0];
                rows.Add($"r{round}: " + string.Join(" ", types.Select((k, i) => counts[i] > 0 ? $"{k.Key} {counts[i]}" : null).Where(s => s != null)));
                for (var i = 1; i < types.Count; i++)
                {
                    var k = types[i];
                    if (counts[i] > 0 && (!k.Enabled || round < k.FirstRound || k.BossEvery > 0))
                        bad = true; // too early, off, or a boss in the roll
                    if (k.Enabled && k.BossEvery == 0 && round >= k.FirstRound + 2 && k.Weight.At(round) > 0 && counts[i] == 0)
                        bad = true; // phased in but never rolled
                }
                if (round <= 3 && specials != 0)
                    bad = true;
                if (specials > 600 * 0.55f)
                    bad = true; // a round stays fair: walkers are most of it
            }
            // Caps: a type at its cap is never rolled.
            var alive = new int[types.Count];
            var brute = T("brute");
            alive[brute] = types[brute].MaxAlive;
            var capped = 0;
            for (var i = 0; i < 300; i++)
                if (g.SelfTestPickType(25, alive) == brute)
                    capped++;
            Line("spawn mix (600 rolls): " + string.Join("; ", rows) + $"; with {alive[brute]} brutes alive (the cap) a brute rolled {capped} times");
            if (bad || capped != 0)
                Fail("early rounds should be all walkers, specials phase in by their first round and stay under half, and a capped type is never rolled");
        }

        // ---- The network: a client sees the types (size, colours, plates, a hold) for two bytes more a typed zombie ----
        {
            var zw = Z("walker", At(-12, -6));
            var za = Z("armoured", At(-6, -6));
            var zb = Z("brute", At(-9, -9));
            g.ServerTick++;
            var w = new PacketWriter();
            g.WriteZEnts(w);
            var bytes3 = w.Length;
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            World.SetMap(map);
            c.SelfTestReadEnts(w.Span, now);
            var ca = c.Horde.FirstOrDefault(x => x.Id == za.Id);
            var cb = c.Horde.FirstOrDefault(x => x.Id == zb.Id);
            var cw = c.Horde.FirstOrDefault(x => x.Id == zw.Id);
            var sameBox = cb != null && Game.ZombieBox(cb, zb.Position, zb.Yaw) == Game.ZombieBox(zb, zb.Position, zb.Yaw);
            var platesBefore = ca?.Plates;
            g.SelfTestLimbShot(me, za, za.Position + new Vector3(0, 1.6f * za.Kind.Height, -0.4f * za.Kind.Width), 1000, t); // the helmet off
            g.ServerTick += 2;
            g.WriteZEnts(w);
            c.SelfTestReadEnts(w.Span, now += 0.03);
            za.State = zb.State = ZState.Dead;
            g.SelfTestTickHorde(t += 2);
            g.ServerTick++;
            g.WriteZEnts(w);
            var bytes1 = w.Length;
            Line($"network: {bytes3} bytes for a walker, an armoured and a brute, {bytes1} for the walker alone (+{(bytes3 - bytes1 - 2 * (Game.ZEntBytes - 1)) / 2f:0} a typed zombie), protocol {ServerInfo.ProtocolVersion}; "
                 + $"the client sees {cw?.Kind.Key}, {ca?.Kind.Key} (plates {platesBefore}, after the helmet shot {ca?.Plates}) and {cb?.Kind.Key} with the same box {sameBox}");
            if (cw?.Type != 0 || ca?.Kind.Key != "armoured" || cb?.Kind.Key != "brute" || !sameBox || ca.Plates != 2 || bytes3 - bytes1 != 2 * (Game.ZEntBytes - 1) + 2 * (Game.ZEntLimbBytes + Game.ZEntTypeBytes) || ServerInfo.ProtocolVersion < 73)
                Fail("a client should see each zombie's type and plates, two bytes more a typed zombie, protocol 73 or later");
            Clear();
        }

        // ---- No allocations: a mixed horde thinking, grabbing, pouncing, climbing, sent, read and drawn ----
        {
            me.ImmuneUntil = 1e9;
            var zs = new List<Zombie>();
            for (var i = 0; i < 16; i++)
            {
                var k = types[1 + i % (types.Count - 1)];
                zs.Add(Z(k.Key, At(-20 + (i % 8) * 2.5f, -12 - (i / 8) * 3)));
            }
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            World.SetMap(map);
            var w = new PacketWriter();
            var rec = new DrawRecorder { Keep = false };
            var gfx = new Renderer { Recorder = rec };
            var cnow = now;
            void Round()
            {
                for (var k = 0; k < 30; k++)
                {
                    me.Teleport(HordeLoop(map, t, 0));
                    g.SelfTestZombiesTick(t += World.TickDt);
                    if (k % 2 == 0)
                    {
                        g.WriteZEnts(w);
                        c.SelfTestReadEnts(w.Span, cnow += World.TickDt * 2);
                    }
                    foreach (var z in zs)
                    {
                        g.ZombieAnimInputFor(z, t, out var a);
                        ZombieAnim.Evaluate(a, out var pose);
                        ZombieAnim.Scale(ref pose, a.Pos, z.Kind.Width, z.Kind.Height);
                        ZombieAnim.Draw(gfx, pose, z.Kind.Shirt, z.Kind.Skin, z.Kind.Pants, true, false, true, z.Limbs);
                        if (z.Plates != 0)
                            ZombieAnim.DrawPlates(gfx, pose, z.Plates);
                    }
                    g.ZBoss();
                }
            }
            for (var i = 0; i < 3; i++)
                Round();
            var before = GC.GetAllocatedBytesForCurrentThread();
            Round();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            allocated = RecheckAllocBytes(allocated, Round);
            Line($"allocations: {allocated} bytes over 30 ticks of 16 typed zombies thinking, ZEnts written and read, posed, scaled and drawn (pounces {g.ZPounces}, climbs {g.ZFreeClimbs}, grabs {g.ZGrabs})");
            if (allocated > 0)
                Fail($"zombie types allocate ({allocated} bytes over 30 ticks)");
            Clear();
        }

        // ---- Tick cost: a mixed horde of 100 against 100 walkers ----
        {
            double Cost(bool mixed)
            {
                for (var i = 0; i < 100; i++)
                {
                    var k = mixed ? types[i % types.Count] : types[0];
                    if (!k.Enabled || k.BossEvery > 0)
                        k = types[0];
                    Z(k.Key, At(-22 + (i % 20) * 2.2f, -22 + (i / 20) * 2.5f));
                }
                for (var i = 0; i < 30; i++)
                {
                    me.Teleport(HordeLoop(map, t, 0));
                    g.SelfTestZombiesTick(t += World.TickDt);
                }
                g.ZAiMs = g.ZAiTicks = g.ZAiZombieTicks = 0;
                for (var i = 0; i < 120; i++)
                {
                    me.Teleport(HordeLoop(map, t, 0));
                    g.SelfTestZombiesTick(t += World.TickDt);
                }
                var us = g.ZAiZombieTicks > 0 ? g.ZAiMs * 1000 / g.ZAiZombieTicks : 0;
                Clear();
                return us;
            }
            var walkers = Cost(false);
            var mix = Cost(true);
            Line($"tick cost: {walkers:0.0} us per walker per tick, {mix:0.0} us per zombie per tick for a mixed horde of 100 (2 s each)");
            if (mix > Math.Max(walkers * 3, 60))
                Fail("a mixed horde should cost about what walkers do per zombie");
        }
    }
}
