using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Loudener": the loudness stat (<see cref="WeaponDef.Loudness"/>, Attachments/README.md). The
/// Loudener makes a gun 2.5x as loud and a suppressor half as loud, in both systems (an attachment and a Zombies part);
/// bots hear a loud gun that much further away and a suppressed one that much closer (BotCombat.cs, on a real server
/// scan); another player's shot carries that much further through the gunshot mix (Audio.GunMix.cs); the muzzle flash
/// grows with it (GunModel.FlashSize); and none of it allocates per shot or per scan.
/// </summary>
public static partial class SelfTest
{
    private static void CheckLoudener()
    {
        var loudener = Attachments.Find("Loudener");
        var suppressor = Attachments.Find("Suppressor");
        if (loudener is not { Slot: AttachSlot.Muzzle } || suppressor is not { Slot: AttachSlot.Muzzle })
        {
            Fail("the Loudener or the Suppressor isn't loaded as a muzzle attachment");
            return;
        }
        var rifle = Weapons.Find("Vanguard AR") ?? Weapons.DefaultPrimary;
        if (!loudener.Fits(rifle) || !suppressor.Fits(rifle))
        {
            Fail($"the Loudener or the Suppressor doesn't fit the {rifle.Name}");
            return;
        }
        var loud = Attachments.Apply(rifle, Attachments.With(0, AttachSlot.Muzzle, loudener.Index));
        var quiet = Attachments.Apply(rifle, Attachments.With(0, AttachSlot.Muzzle, suppressor.Index));
        Line($"{rifle.Name}: loudness stock {rifle.Loudness}, Loudener {loud.Loudness}, Suppressor {quiet.Loudness}; flash size {GunModel.FlashSize(rifle):0.000} / {GunModel.FlashSize(loud):0.000} / {GunModel.FlashSize(quiet):0.000} m");
        if (rifle.Loudness != 1f || loud.Loudness < 2f || loud.Loudness > WeaponDef.MaxLoudness || quiet.Loudness > 0.6f || quiet.Loudness < WeaponDef.MinLoudness)
            Fail("loudness: stock 1, the Loudener 2-4, the Suppressor at most 0.6");
        if (GunModel.FlashSize(loud) < GunModel.FlashSize(rifle) * 1.4f || GunModel.FlashSize(quiet) > GunModel.FlashSize(rifle) * 0.5f)
            Fail("the muzzle flash should grow with the Loudener and shrink with the Suppressor");
        if (loud.Damage <= rifle.Damage || loud.RecoilDeg <= rifle.RecoilDeg || loud.Mobility >= rifle.Mobility)
            Fail("the Loudener should hit harder, kick harder and slow the gun");
        foreach (var a in Attachments.BySlot[(int)AttachSlot.Muzzle].Where(a => a.Fits(rifle)))
        {
            var d = Attachments.Apply(rifle, Attachments.With(0, AttachSlot.Muzzle, a.Index));
            if (d.Suppressor && d.Loudness >= 1f)
                Fail($"{a.Name}: a suppressed gun should be quieter than 1 (loudness {d.Loudness})");
        }

        // ---- The Zombies part: the same stat through the build pipeline, and the loudest receiver counts ----
        if (Parts.Find("Loudener") is { Category: PartCategory.Muzzle } part && Parts.Find("Suppressor") is { } can)
        {
            var wall = Parts.All.FirstOrDefault(p => p.Name == "Service Rifle Grip") ?? Parts.All.First(p => p.Category == PartCategory.Grip && p.Frame != null);
            var receiver = Parts.All.First(p => p.Name == "Workhorse Receiver");
            var barrel = Parts.All.First(p => p.Name == "Standard Barrel");
            WeaponDef? Stats(PartDef? muzzle)
            {
                var b = new WeaponBuild("Loud test", wall);
                var r = b.Root!.Attach("receiver_1", receiver);
                r.Attach("action", Parts.All.First(p => p.Name == "Carbine Sear"));
                var bar = r.Attach("barrel", barrel);
                if (muzzle != null)
                    bar.Attach("muzzle", muzzle);
                r.Attach("feed", Parts.All.First(p => p.Name == "Box Mag"));
                r.Attach("ammo", Parts.Find("FMJ") ?? Parts.All.First(p => p.Category == PartCategory.Ammo));
                return BuildStats.Compute(b, out var problems) ?? throw new InvalidOperationException(string.Join("; ", problems.Select(x => x.Message)));
            }
            try
            {
                var plain = Stats(null)!;
                var horn = Stats(part)!;
                var hushed = Stats(can)!;
                Line($"Zombies build: loudness plain {plain.Loudness}, Loudener {horn.Loudness}, Suppressor {hushed.Loudness} (suppressed {hushed.Suppressor})");
                if (plain.Loudness != 1f || horn.Loudness < 2f || hushed.Loudness > 0.6f || !hushed.Suppressor)
                    Fail("the Zombies Loudener and Suppressor parts should set the same loudness as the attachments");
            }
            catch (InvalidOperationException ex)
            {
                Fail($"loudness test build: {ex.Message}");
            }
        }
        else
            Fail("the Loudener or the Suppressor isn't loaded as a Zombies muzzle part");

        // ---- Sound: another player's loud shot carries; your own is capped; nothing allocates ----
        var at60 = Audio.PlanShot(own: false, suppressed: false, hasFar: true, distance: 60f);
        var loud60 = Audio.Louder(at60, loud.Loudness, own: false);
        float Level(Audio.ShotPlan p, float d) => p.Near * p.NearFalloff.Attenuation(d) + p.Far * p.FarFalloff.Attenuation(d);
        var stockLevel = Level(at60, 60f);
        var loudLevel = Level(loud60, 60f);
        var ownLoud = Audio.Louder(Audio.PlanShot(own: true, suppressed: false, hasFar: false, distance: 0f), loud.Loudness, own: true);
        Line($"another player's shot at 60 m: level {stockLevel:0.000} stock, {loudLevel:0.000} with the Loudener ({loudLevel / stockLevel:0.0}x; falloff reference {at60.NearFalloff.Reference:0} -> {loud60.NearFalloff.Reference:0} m); own shot near gain {ownLoud.Near:0.00}");
        if (loudLevel < stockLevel * 1.8f || loud60.NearFalloff.Max <= at60.NearFalloff.Max || ownLoud.Near > 1f || Audio.Louder(at60, 1f, false) != at60)
            Fail("the Loudener should carry a shot about 2.5x further (and 1 should change nothing)");
        var warm = Audio.Louder(at60, 2.5f, false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            warm = Audio.Louder(at60, 2.5f, (i & 1) == 0);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes > 0)
            Fail($"Audio.Louder allocates ({bytes} bytes over 1000 shots)");

        // ---- Bots: on a server, a bot hears a Loudener from further and a Suppressor from less far ----
        using var game = ModeGame(2, GameMode.FreeForAll);
        var map = MapFile.Load("""
            { "name": "Loudener test", "halfX": 80, "halfZ": 80, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Spawn", "x": 60, "z": 60 } ] }
            """, "loudener-test.json");
        World.SetMap(map);
        var me = game.Players[0];
        var bot = game.Players.Values.First(p => p.Bot != null);
        foreach (var p in game.Players.Values)
            game.SelfTestPerks(p, PerkSet.Empty);
        me.Primary = rifle.Id;
        me.Weapon = rifle.Id;
        var hearing = 20f + 30f * bot.Bot!.Profile.Aggression;
        var now = 100.0;
        bool Hears(uint kit, float distance)
        {
            now += 10;
            me.PrimaryKit = kit;
            game.SelfTestPlace(me, new Vector3(0, 0, 0), 0);
            game.SelfTestPlace(bot, new Vector3(0, 0, distance), 0); // facing away from the shooter, so it's the ear that matters
            me.LastShotTime = now;
            return game.SelfTestBotHears(bot, now);
        }
        var stockKit = 0u;
        var loudKit = Attachments.With(0, AttachSlot.Muzzle, loudener.Index);
        var quietKit = Attachments.With(0, AttachSlot.Muzzle, suppressor.Index);
        bool near = Hears(stockKit, hearing * 0.8f), beyond = Hears(stockKit, hearing * 1.6f), loudBeyond = Hears(loudKit, hearing * 1.6f),
            loudFar = Hears(loudKit, hearing * 2.8f), quietNear = Hears(quietKit, hearing * 0.8f), quietClose = Hears(quietKit, hearing * 0.4f);
        Line($"bot (hearing {hearing:0} m): hears a stock gun at {hearing * 0.8f:0} m {near}, at {hearing * 1.6f:0} m {beyond}; a Loudener at {hearing * 1.6f:0} m {loudBeyond}, at {hearing * 2.8f:0} m {loudFar}; "
             + $"a Suppressor at {hearing * 0.8f:0} m {quietNear}, at {hearing * 0.4f:0} m {quietClose}");
        if (!near || beyond || !loudBeyond || loudFar || quietNear || !quietClose)
            Fail("bots should hear a Loudener about 2.5x further than a stock gun and a Suppressor about half as far");
        if (Game.HearingRange(hearing, false, 2.5f) != hearing * 2.5f || Game.HearingRange(hearing, true, 1f) != 60f * MathF.Max(1f, hearing / 60f))
            Fail("HearingRange: the base range (60 m when alert) times the gun's loudness");
        me.PrimaryKit = 0;
    }
}
