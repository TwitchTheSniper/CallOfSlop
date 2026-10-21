using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Health": Last Stand (the lethal hit leaves 1 HP, exactly the next 3 bullet hits are blocked and
/// the 4th hurts, a shotgun blast uses one charge, melee and explosions go straight through, once per life, reset on
/// respawn, the shield runs out after its time, bots behave the same, the charges reach clients in the snapshot and
/// the shield hit message) and health regeneration (nothing within the delay, the right rate after it, stopped by
/// taking or dealing damage, capped at max, bots too, never while dead).
/// </summary>
public static partial class SelfTest
{
    private static void CheckHealth()
    {
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var human = game.Players[0];
        var bot = game.Players.Values.First(p => p.Id != 0);
        if (bot.Bot == null)
            Fail("Health: the second player isn't a bot");
        var lastStand = new PerkSet(PerkId.None, PerkId.LastStand, PerkId.None);

        void Place(Player attacker, PerkSet attackerPerks, Player victim, PerkSet victimPerks)
        {
            game.SelfTestPerks(attacker, attackerPerks);
            game.SelfTestPerks(victim, victimPerks);
            game.SelfTestPlace(attacker, new Vector3(5, 0, 5), MathF.PI); // facing +Z, at the victim
            game.SelfTestPlace(victim, new Vector3(5, 0, 8), 0);
        }

        // ---- Last Stand: the same rules for a person and for a bot ----
        void LastStandRules(Player attacker, Player victim, string who)
        {
            Place(attacker, PerkSet.Empty, victim, lastStand);
            var killedByLethal = game.SelfTestDamage(attacker, victim, 150, DamageKind.Bullet, 1);
            var hpAfterLethal = victim.Health;
            var chargesAfterLethal = victim.ShieldCharges;
            var hp = new List<int>();
            var charges = new List<int>();
            var flashes = 0;
            for (var i = 0; i < Perks.LastStandCharges; i++)
            {
                var t = 1.5 + i * 0.5;
                var killed = game.SelfTestDamage(attacker, victim, 50, DamageKind.Bullet, t);
                hp.Add(killed ? 0 : victim.Health);
                charges.Add(victim.ShieldCharges);
                if (victim.ShieldFlashTime == t)
                    flashes++;
            }
            var fourthKills = game.SelfTestDamage(attacker, victim, 50, DamageKind.Bullet, 3.5);
            Line($"Last Stand ({who}): lethal 150 -> {hpAfterLethal} HP, {chargesAfterLethal} charges; next {Perks.LastStandCharges} bullet hits -> HP {string.Join("/", hp)}, "
                 + $"charges {string.Join("/", charges)}, {flashes} shield flashes; 4th hit kills {fourthKills}");
            if (killedByLethal || hpAfterLethal != 1 || chargesAfterLethal != Perks.LastStandCharges)
                Fail($"Last Stand ({who}): the lethal hit didn't leave 1 HP and a full shield");
            if (hp.Any(h => h != 1) || !charges.SequenceEqual(Enumerable.Range(0, Perks.LastStandCharges).Select(i => Perks.LastStandCharges - 1 - i)))
                Fail($"Last Stand ({who}): the next {Perks.LastStandCharges} bullet hits weren't each blocked, one charge at a time");
            if (flashes != Perks.LastStandCharges)
                Fail($"Last Stand ({who}): {flashes} shield hit events for {Perks.LastStandCharges} blocked hits");
            if (!fourthKills || victim.Alive)
                Fail($"Last Stand ({who}): the hit after the shield was used up didn't hurt");
        }
        LastStandRules(bot, human, "person");
        LastStandRules(human, bot, "bot");

        // ---- Once per life, reset on respawn ----
        Place(bot, PerkSet.Empty, human, lastStand);
        game.SelfTestDamage(bot, human, 150, DamageKind.Bullet, 10);
        var beforeExpiry = human.ShieldCharges;
        game.SelfTestHealthTick(10 + Perks.LastStandShieldTime - 0.05);
        var justBefore = human.ShieldCharges;
        game.SelfTestHealthTick(10 + Perks.LastStandShieldTime + 0.01);
        var afterExpiry = human.ShieldCharges;
        human.Health = 1;
        var secondLethalKills = game.SelfTestDamage(bot, human, 150, DamageKind.Bullet, 10 + Perks.LastStandShieldTime + 0.5);
        Line($"Last Stand shield: {beforeExpiry} charges, {justBefore} just before {Perks.LastStandShieldTime:0} s, {afterExpiry} after; a second lethal hit in the same life kills {secondLethalKills}");
        if (beforeExpiry != Perks.LastStandCharges || justBefore != Perks.LastStandCharges || afterExpiry != 0)
            Fail($"Last Stand: the shield doesn't run out after {Perks.LastStandShieldTime} s");
        if (!secondLethalKills)
            Fail("Last Stand saved twice in one life");
        human.WantPerks = lastStand;
        game.SelfTestSpawn(human); // a real respawn (with the person's chosen perks)
        var respawnedClean = human.Alive && !human.LastStandUsed && human.ShieldCharges == 0 && human.Health == World.MaxHealth;
        var savedAgain = !game.SelfTestDamage(bot, human, 500, DamageKind.Bullet, 30) && human.Health == 1 && human.ShieldCharges == Perks.LastStandCharges;
        Line($"Last Stand after a respawn: clean {respawnedClean}, saves again {savedAgain}");
        if (!respawnedClean || !savedAgain)
            Fail("Last Stand doesn't reset on respawn");

        // ---- Melee and explosions go straight through the shield ----
        foreach (var kind in new[] { DamageKind.Melee, DamageKind.Explosive })
        {
            Place(bot, PerkSet.Empty, human, lastStand);
            game.SelfTestDamage(bot, human, 150, DamageKind.Bullet, 40);
            var killed = game.SelfTestDamage(bot, human, 20, kind, 40.5);
            Line($"Last Stand vs {kind}: shielded at 1 HP, a 20 damage hit kills {killed}");
            if (!killed)
                Fail($"Last Stand: {kind} damage should go straight through the shield");
        }
        // A lethal blast still triggers Last Stand (only the shield is bullets-only).
        Place(bot, PerkSet.Empty, human, lastStand);
        var blastKills = game.SelfTestDamage(bot, human, 300, DamageKind.Explosive, 45);
        if (blastKills || human.Health != 1 || human.ShieldCharges != Perks.LastStandCharges)
            Fail("Last Stand: a lethal explosion didn't trigger it");
        // Your own bullets... can't hit you, but your own grenade isn't stopped by your shield either (attacker == victim).
        Place(bot, PerkSet.Empty, human, lastStand);
        game.SelfTestDamage(bot, human, 150, DamageKind.Bullet, 46);
        if (!game.SelfTestDamage(human, human, 20, DamageKind.Bullet, 46.5))
            Fail("Last Stand: self-damage was blocked by your own shield");

        // ---- A shotgun blast is one hit, however many pellets land ----
        var shotgun = Weapons.All.FirstOrDefault(w => w.Pellets > 1 && w.Slot == Slot.Primary);
        if (shotgun == null)
        {
            Fail("Health: no multi-pellet primary to test with");
        }
        else
        {
            void Arm(Player p)
            {
                p.Primary = shotgun.Id;
                p.PrimaryKit = 0;
                p.Weapon = shotgun.Id;
            }
            Place(human, PerkSet.Empty, bot, PerkSet.Empty);
            Arm(human);
            game.SelfTestShoot(human, Vector3.Normalize(bot.Chest - human.Eye), 50);
            var unshieldedDamage = World.MaxHealth - (bot.Alive ? bot.Health : 0);
            Place(human, PerkSet.Empty, bot, lastStand);
            Arm(human);
            game.SelfTestDamage(human, bot, 150, DamageKind.Bullet, 51);
            game.SelfTestShoot(human, Vector3.Normalize(bot.Chest - human.Eye), 52);
            var afterBlast = (bot.Alive, bot.Health, bot.ShieldCharges);
            Line($"{shotgun.Name} ({shotgun.Pellets} pellets x {shotgun.Damage:0}): {unshieldedDamage} damage unshielded; on a shielded bot -> alive {afterBlast.Alive}, {afterBlast.Health} HP, "
                 + $"{afterBlast.ShieldCharges} charges left");
            if (unshieldedDamage <= shotgun.Damage * 1.5f)
                Fail($"Health: the test blast didn't land several pellets ({unshieldedDamage} damage), so it proves nothing");
            if (!afterBlast.Alive || afterBlast.Health != 1 || afterBlast.ShieldCharges != Perks.LastStandCharges - 1)
                Fail("Last Stand: a shotgun blast should use exactly one shield charge");
        }

        // ---- The charges reach clients ----
        Place(bot, PerkSet.Empty, human, PerkSet.Empty);
        Place(human, PerkSet.Empty, bot, lastStand);
        game.SelfTestDamage(human, bot, 150, DamageKind.Bullet, 60);
        game.SelfTestDamage(human, bot, 50, DamageKind.Bullet, 60.2); // 2 left
        var seen = -1;
        var packet = new Snapshot.Packet();
        foreach (var bytes in game.SelfTestSnapshotFor(human))
        {
            var r = new PacketReader(bytes.AsSpan(1));
            if (Snapshot.Read(ref r, packet))
                foreach (var f in packet.Fast)
                    if (f.Id == bot.Id)
                        seen = f.Shield;
        }
        var roundTrips = 0;
        for (byte c = 0; c <= Perks.LastStandCharges; c++)
            foreach (var regen in new[] { false, true })
                foreach (var stance in new[] { Stance.Stand, Stance.Prone })
                {
                    var w = new PacketWriter();
                    w.Begin(Msg.State);
                    var sent = new SnapPlayer { Id = 7, Health = 42, Alive = true, Grounded = true, Stance = stance, Weapon = 3, Boost = 9, Shield = c, Regen = regen };
                    Snapshot.WriteFast(w, sent);
                    var r = new PacketReader(w.Span[1..]);
                    var got = Snapshot.ReadFast(ref r);
                    if (got.Shield == c && got.Regen == regen && got.Stance == stance && got.Alive && got.Grounded && got.Health == 42 && got.Weapon == 3 && got.Boost == 9)
                        roundTrips++;
                    else
                        Fail($"snapshot entry: shield {c}, regen {regen}, {stance} came back as shield {got.Shield}, regen {got.Regen}, {got.Stance}");
                }
        var ev = new PacketWriter();
        Game.WriteShieldHit(ev, 5, 1);
        var er = new PacketReader(ev.Span[1..]);
        var evGot = Game.ReadShieldHit(ref er);
        Line($"replication: the bot's 2 charges arrive as {seen} in the snapshot; {roundTrips}/16 entry round trips; shield hit message {ev.Length} bytes -> player {evGot.Id}, {evGot.Charges} left");
        if (seen != 2)
            Fail($"Last Stand: the snapshot carries {seen} shield charges, not 2");
        if (ev.Span[0] != (byte)Msg.ShieldHit || evGot != (5, 1))
            Fail("the shield hit message doesn't round-trip");

        // ---- Health regeneration ----
        const int Hz = World.TickRate;
        void TickFor(double from, double seconds)
        {
            var n = (int)Math.Round(seconds * Hz);
            for (var i = 1; i <= n; i++)
                game.SelfTestHealthTick(from + i / (double)Hz);
        }
        Place(human, PerkSet.Empty, bot, PerkSet.Empty);
        human.Health = 50;
        human.LastHurtTime = 90; // the human was last hurt earlier: regen would start at 95, but dealing damage at 100 restarts the wait
        game.SelfTestDamage(human, bot, 60, DamageKind.Bullet, 100); // bot on 40
        TickFor(100, Regen.Delay - 0.05);
        var duringDelay = bot.Health;
        var regenDuringDelay = bot.Regenerating;
        var humanDuringDelay = human.Health;
        TickFor(100 + Regen.Delay - 0.05, 0.05); // the delay is over
        var start = bot.Health;
        TickFor(100 + Regen.Delay, 2);
        var afterTwo = bot.Health;
        var regenFlag = bot.Regenerating;
        var humanAfter = human.Health;
        var rate = (afterTwo - start) / 2.0;
        Line($"regen: {Regen.Rate:0} HP/s after {Regen.Delay:0} s; bot on 40 -> {duringDelay} HP after {Regen.Delay - 0.05:0.00} s (regenerating {regenDuringDelay}), "
             + $"{start} -> {afterTwo} HP over the next 2 s ({rate:0.0} HP/s, flag {regenFlag}); the attacker (dealt damage) {humanDuringDelay} -> {humanAfter} HP");
        if (duringDelay != 40 || regenDuringDelay)
            Fail("regen: health came back within the delay");
        if (Math.Abs(afterTwo - start - 2 * Regen.Rate) > 1 || !regenFlag)
            Fail($"regen: {rate:0.0} HP/s instead of {Regen.Rate}");
        if (humanDuringDelay != 50 || humanAfter <= 50)
            Fail("regen: dealing damage didn't restart the wait, or the person doesn't regenerate after it");

        // Taking damage stops it and restarts the wait.
        var t0 = 100 + Regen.Delay + 2;
        game.SelfTestDamage(human, bot, 10, DamageKind.Bullet, t0);
        var hit = bot.Health;
        TickFor(t0, Regen.Delay - 0.1);
        var stopped = bot.Health == hit && !bot.Regenerating;
        TickFor(t0 + Regen.Delay - 0.1, 30);
        var full = bot.Health;
        Line($"regen: hit at {hit} HP -> {(stopped ? "stopped" : "kept going")} for the delay; 30 s later {full} HP (regenerating {bot.Regenerating})");
        if (!stopped)
            Fail("regen: taking damage didn't stop it");
        if (full != World.MaxHealth || bot.Regenerating)
            Fail($"regen: ended on {full} HP, not capped at {World.MaxHealth}");

        // Dead players don't regenerate (and lose any shield).
        game.SelfTestDamage(human, bot, 500, DamageKind.Bullet, 200);
        TickFor(200, Regen.Delay + 5);
        Line($"regen: dead bot -> alive {bot.Alive}, {bot.Health} HP, regenerating {bot.Regenerating} after {Regen.Delay + 5:0} s");
        if (bot.Alive || bot.Health != 0 || bot.Regenerating)
            Fail("regen: a dead player regenerated");
    }
}
