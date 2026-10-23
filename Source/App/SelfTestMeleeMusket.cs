using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test sections "Melee one-hit kill" and "Musket": a standard melee hit kills a full-health player standing,
/// crouched with Entrenched (it only cuts bullets), with Flak Jacket and with Last Stand (never saves you from melee);
/// and the Old Faithful Musket one-shots to the body at close and mid range, not at the far end of its falloff, and
/// always kills with a headshot within its effective range (3x its full-damage range).
/// </summary>
public static partial class SelfTest
{
    private static void CheckMeleeOneHit()
    {
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var a = game.Players[0];
        var v = game.Players.Values.First(p => p.Id != 0);
        var t = 1.0;

        (bool Killed, int Hp, bool LastStandUsed) Swing(PerkSet victimPerks, Stance stance)
        {
            game.SelfTestPerks(a, PerkSet.Empty);
            game.SelfTestPerks(v, victimPerks);
            game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
            game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), 0); // facing the attacker
            v.Sim.Stance = stance;
            for (var i = 0; i < Player.HistorySize; i++)
                v.History[i].Stance = stance;
            game.SelfTestMelee(a, MathF.PI, stance == Stance.Stand ? 0 : -0.45f, t += 2); // look down at someone crouched
            return (!v.Alive, v.Health, v.LastStandUsed);
        }

        var entrenched = new PerkSet(PerkId.None, PerkId.Entrenched, PerkId.None);
        var standing = Swing(PerkSet.Empty, Stance.Stand);
        var crouched = Swing(PerkSet.Empty, Stance.Crouch);
        var dugIn = Swing(entrenched, Stance.Crouch);
        var flak = Swing(new PerkSet(PerkId.None, PerkId.FlakJacket, PerkId.None), Stance.Stand);
        var lastStand = Swing(new PerkSet(PerkId.None, PerkId.LastStand, PerkId.None), Stance.Stand);

        // Entrenched is really active in that pose: the same 100 bullet damage leaves the dug-in victim alive.
        game.SelfTestPerks(v, entrenched);
        game.SelfTestPlace(v, new Vector3(5, 0, 6.3f), 0);
        v.Sim.Stance = Stance.Crouch;
        var wasDugIn = Game.IsDugIn(v);
        var bulletSurvived = !game.SelfTestDamage(a, v, World.MaxHealth, DamageKind.Bullet, t += 2) && v.Alive;

        Line($"one melee hit on full health: standing kills {standing.Killed}, crouched {crouched.Killed}, crouched + Entrenched {dugIn.Killed} "
             + $"(dug in {wasDugIn}, a 100 bullet hit leaves {v.Health} HP), Flak Jacket {flak.Killed}, Last Stand {lastStand.Killed} (save used {lastStand.LastStandUsed})");
        if (!standing.Killed || !crouched.Killed)
            Fail($"melee: one hit should kill a full-health player (standing {standing.Hp} HP left, crouched {crouched.Hp} HP left)");
        if (!dugIn.Killed || !flak.Killed)
            Fail("melee: Entrenched / Flak Jacket stopped the one-hit melee kill");
        if (!lastStand.Killed || lastStand.LastStandUsed)
            Fail("melee: Last Stand should never save you from a melee hit");
        if (!wasDugIn || !bulletSurvived)
            Fail("melee test: the Entrenched pose wasn't active, so the crouched check proves nothing");
        if (Game.BotMeleeWindup(BotDifficulty.Easy) <= Game.BotMeleeWindup(BotDifficulty.Normal) || Game.BotMeleeWindup(BotDifficulty.Hard) < 0.1)
            Fail("melee: bot wind-ups should be longest on Easy and never instant");
    }

    private static void CheckMusket()
    {
        var musket = Weapons.All.FirstOrDefault(w => w.Name == "Old Faithful Musket");
        if (musket == null)
        {
            Fail("Musket: Old Faithful Musket not found");
            return;
        }
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var a = game.Players[0];
        var v = game.Players.Values.First(p => p.Id != 0);
        var t = 1.0;

        void Place(PerkSet victimPerks)
        {
            game.SelfTestPerks(a, PerkSet.Empty);
            game.SelfTestPerks(v, victimPerks);
            game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
            game.SelfTestPlace(v, new Vector3(5, 0, 8), 0);
        }

        // A real shot from 3 m (the gun file, lag-compensated hitscan, DamagePlayer): one body shot kills.
        Place(PerkSet.Empty);
        a.Primary = musket.Id;
        a.PrimaryKit = 0;
        a.Weapon = musket.Id;
        game.SelfTestShoot(a, Vector3.Normalize(v.Chest - a.Eye), t += 2);
        var realKill = !v.Alive;

        // At range: the gun's own falloff through DamagePlayer (walls on the test map would get in the way of real shots).
        bool Hit(float dist, bool head, PerkSet victimPerks, Stance stance = Stance.Stand)
        {
            Place(victimPerks);
            v.Sim.Stance = stance;
            var dmg = (int)MathF.Round(musket.Damage * musket.DamageMultAt(dist) * (head ? musket.HeadMult : 1f));
            return game.SelfTestDamage(a, v, dmg, DamageKind.Bullet, t += 2);
        }
        var body10 = Hit(10, false, PerkSet.Empty);
        var body30 = Hit(30, false, PerkSet.Empty);
        var body85 = Hit(85, false, PerkSet.Empty);
        var effective = musket.RangeM * 3f; // falloff ends here: the gun's effective range
        var headFar = Hit(effective, true, PerkSet.Empty);
        var dugIn10 = Hit(10, false, new PerkSet(PerkId.None, PerkId.Entrenched, PerkId.None), Stance.Crouch);
        int D(float dist, bool head = false) => (int)MathF.Round(musket.Damage * musket.DamageMultAt(dist) * (head ? musket.HeadMult : 1f));
        Line($"{musket.Name}: {musket.Damage} dmg to {musket.RangeM:0} m, x{musket.MinDamageMult:0.00} at {effective:0} m, head x{musket.HeadMult:0.0}, {musket.Rpm} rpm, reload {musket.Reload:0.0} s. "
             + $"Real 3 m body shot kills {realKill}; body 10 m {D(10)} kills {body10}, 30 m {D(30)} kills {body30}, 85 m {D(85)} kills {body85}; "
             + $"head at {effective:0} m {D(effective, true)} kills {headFar}; Entrenched at 10 m kills {dugIn10}");
        if (!realKill || !body10 || !body30)
            Fail("Musket: a body shot should kill at close and mid range (10-30 m)");
        if (body85)
            Fail("Musket: a body shot at 85 m should no longer kill (the falloff should matter)");
        if (!headFar)
            Fail("Musket: a headshot should kill anywhere within its effective range");
        if (!dugIn10)
            Fail("Musket: a close body shot should still kill a crouched Entrenched player");
        if (musket.Reload < 2.5f || musket.Mag != 1)
            Fail("Musket: its one-shot power is balanced by a single round and a slow reload");

        // The guns whose blurbs promise a one-shot kill still deliver it at max health (120 since the health bump).
        var oneShots = new[] { "Breacher Pump", "Leviathan .50", "Railspike", "Old Faithful Musket" };
        var shown = new List<string>();
        foreach (var name in oneShots)
        {
            var w = Weapons.All.FirstOrDefault(x => x.Name == name);
            if (w == null)
            {
                Fail($"one-shot guns: {name} not found");
                continue;
            }
            shown.Add($"{name} {w.ShotDamage:0}");
            if (w.ShotsToKill(false) != 1)
                Fail($"one-shot guns: {name} does {w.ShotDamage:0} with every projectile landing, less than {World.MaxHealth} health");
        }
        Line($"one-shot guns at {World.MaxHealth} health (full damage, every pellet): {string.Join(", ", shown)}; melee {Game.MeleeDamage:0}, backstab {Game.MeleeBackstabDamage:0}");
        float melee = Game.MeleeDamage, backstab = Game.MeleeBackstabDamage; // (locals: a constant condition would warn as unreachable)
        if (melee < World.MaxHealth || backstab < World.MaxHealth)
            Fail("melee: no longer a one-hit kill at max health");
    }
}
