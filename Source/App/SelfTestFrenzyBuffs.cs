using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Frenzy buffs" (Source/Game/Frenzy): the server's per-player meter gives the same levels as the
/// client's music meter for the same kill sequence (one shared <see cref="FrenzyMeterCore"/>); the level and class
/// survive the snapshot round trip; the class rule (most recent gun kill; melee, throwables and streaks keep it);
/// classes.json loads with an entry per class; each buff's effect per level, measured through the same calls the game
/// uses (and two through the real game paths); the buffs-off setting; bots; the server's grace for prediction; and
/// determinism (two runs, identical traces).
/// </summary>
public static partial class SelfTest
{
    private static string F2(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    private static WeaponDef? FirstOfClass(WeaponClass c) => Weapons.All.FirstOrDefault(w => w.Class == c && w.SourceFile != null) ?? Weapons.All.FirstOrDefault(w => w.Class == c);

    private static void CheckFrenzyBuffs()
    {
        // ---- The data file ----
        FrenzyClasses.Reload();
        var all = FrenzyClasses.All;
        foreach (var e in FrenzyClasses.LoadErrors)
            Fail("classes.json: " + e);
        Line($"classes.json ({FrenzyClasses.Format}): {all.Count} entries for {Enum.GetValues<WeaponClass>().Length} classes, {FrenzyClasses.LoadErrors.Count} problem(s)");
        foreach (var c in Enum.GetValues<WeaponClass>())
        {
            var e = FrenzyClasses.Get(c);
            if (e.Class != c || e.Buffs.Effects.Length == 0)
                Fail($"classes.json: {c} has no buff");
            if (e.Music.ValueKind != System.Text.Json.JsonValueKind.Object || e.Visual.ValueKind != System.Text.Json.JsonValueKind.Object || e.Sound.ValueKind != System.Text.Json.JsonValueKind.Object)
                Fail($"classes.json: {c} is missing a music/visual/sound section");
            var def = FrenzyClasses.DefaultBuffs(c);
            if (def.Effects.Length != e.Buffs.Effects.Length || def.Effects.Zip(e.Buffs.Effects).Any(z => z.First != z.Second) || def.MinLevel != e.Buffs.MinLevel)
                Fail($"classes.json: {c}'s buff differs from the built-in fallback (keep them in step)");
        }
        if (!FrenzyClasses.IsFrenzyFile(Path.Combine("Frenzy", "classes.json")) || FrenzyClasses.IsFrenzyFile(Path.Combine("SMGs", "x.json")) || Weapons.All.Any(w => w.SourceFile != null && FrenzyClasses.IsFrenzyFile(w.SourceFile)))
            Fail("the weapon loader should skip Weapons/Frenzy (and only it)");
        var broken = new List<string>();
        FrenzyClasses.Parse("{\"format\":\"call-of-slop-frenzy-classes/1\",\"classes\":{\"Pistol\":{\"buffs\":{\"effects\":[{\"stat\":\"equipTime\",\"atMin\":0.1,\"atMax\":3}]}},\"Laser\":{}}}", broken);
        Line($"a bad file: {broken.Count} problems reported ({string.Join(" | ", broken.Take(3))} ...)");
        if (broken.Count < 3)
            Fail("classes.json parser: a clamped effect, an unknown class and missing classes should all be reported");

        // ---- Buff table: every class at every level (the same calls the game makes) ----
        foreach (var c in Enum.GetValues<WeaponClass>())
            Line("  " + FrenzyBuffs.Describe(c));
        var table = new Dictionary<WeaponClass, string>();
        foreach (var c in Enum.GetValues<WeaponClass>())
        {
            var w = FirstOfClass(c);
            if (w == null)
            {
                Fail($"no weapon of class {c} to measure the buff on");
                continue;
            }
            var p = new Player { FrenzyClass = c, FrenzyBuffsOn = true };
            var vals = new List<string>();
            for (byte l = 0; l <= FrenzyMeterCore.MaxLevel; l++)
            {
                p.FrenzyLevel = l;
                vals.Add(c switch
                {
                    WeaponClass.Pistol => $"draw {F2(w.EquipTime * FrenzyBuffs.Value(p, w, FrenzyStat.EquipTime))} s",
                    WeaponClass.SMG => $"run spread {F2(1.5f * 0.014f * FrenzyBuffs.Value(p, w, FrenzyStat.MoveSpread) * 1000)} mrad",
                    WeaponClass.AssaultRifle => $"kick {F2(w.RecoilDeg * FrenzyBuffs.Value(p, w, FrenzyStat.Recoil))} deg",
                    WeaponClass.DMR => $"ADS {F2(w.AdsTime * FrenzyBuffs.Value(p, w, FrenzyStat.AdsTime))} s",
                    WeaponClass.Sniper => $"aimed cone {F2(w.HipSpreadRad * w.AdsSpreadMult * FrenzyBuffs.ConeMult(p, w, 1f) * 1000)} mrad",
                    WeaponClass.Shotgun => $"cone {F2(w.HipSpreadRad * FrenzyBuffs.ConeMult(p, w, 0f) * 1000)} mrad",
                    WeaponClass.LMG => $"reload {F2(w.Reload * FrenzyBuffs.Value(p, w, FrenzyStat.Reload))} s, speed {F2(FrenzyBuffs.Mobility(p, w))}",
                    WeaponClass.Launcher => $"reload {F2(w.Reload * FrenzyBuffs.Value(p, w, FrenzyStat.Reload))} s",
                    _ => $"heal {FrenzyBuffs.Value(p, w, FrenzyStat.KillHeal):0} HP",
                });
                // Never stronger than the safety limits, never a nerf, nothing below level 3.
                foreach (FrenzyStat st in Enum.GetValues<FrenzyStat>())
                {
                    var v = FrenzyBuffs.Value(p, w, st);
                    var ok = st == FrenzyStat.KillHeal ? v is >= 0 and <= 50 : v is >= 0.5f and <= 1f;
                    if (!ok || (l < 3 && v != (st == FrenzyStat.KillHeal ? 0 : 1)))
                        Fail($"{c} L{l}: {st} = {v} (out of range, or a buff below level 3)");
                }
            }
            table[c] = string.Join(" | ", vals);
            Line($"  {c} ({w.Name}) L0..L6: {table[c]}");
            // Other classes' weapons are untouched by this class's frenzy.
            var other = Weapons.All.First(x => x.Class != c);
            p.FrenzyLevel = 6;
            foreach (FrenzyStat st in Enum.GetValues<FrenzyStat>())
                if (FrenzyBuffs.Value(p, other, st) != (st == FrenzyStat.KillHeal ? 0 : 1))
                    Fail($"a {c} frenzy buffed a {other.Class} ({other.Name})");
            // Monotonic: each level at least as strong as the one before.
            for (byte l = 1; l <= 6; l++)
                foreach (var e in FrenzyClasses.Get(c).Buffs.Effects)
                {
                    float a = e.At(l - 1, FrenzyClasses.Get(c).Buffs.MinLevel), b = e.At(l, FrenzyClasses.Get(c).Buffs.MinLevel);
                    if (e.Stat == FrenzyStat.KillHeal ? b < a : b > a)
                        Fail($"{c}: the buff gets weaker from L{l - 1} to L{l}");
                }
        }

        // ---- Server vs client meter: the same kill sequence gives identical levels ----
        string Trace(int seed, bool buffsOn, out string notes)
        {
            World.SetMap(Maps.All[0]);
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
            game.FrenzyBuffsEnabled = buffsOn;
            var me = game.Players[0];
            var bot = game.Players.Values.First(x => x.Id != 0);
            game.SelfTestPerks(me, PerkSet.Empty); // no Last Stand saves, no Bloodthirst heals (bots pick random perks)
            game.SelfTestPerks(bot, PerkSet.Empty);
            game.SelfTestPlace(me, new Vector3(5, 0, 5), MathF.PI); // the local player starts unspawned: a dead killer's frenzy stays at 0
            var client = new FrenzyMeter();
            var rng = new Random(seed);
            var smg = FirstOfClass(WeaponClass.SMG)!;
            var t = 100.0;
            var sb = new System.Text.StringBuilder();
            var mismatches = 0;
            game.SelfTestFrenzyTick(t);
            for (var i = 0; i < 18; i++)
            {
                t += 0.25 + rng.NextDouble() * (i < 12 ? 1.6 : 3.5);
                var head = rng.Next(3) == 0;
                game.SelfTestPlace(bot, new Vector3(5, 0, 8), 0);
                game.SelfTestFrenzyTick(t - 0.01);
                client.Update(t - 0.01); // the same steps on both sides (the client also updates every frame)
                game.SelfTestFrenzyKill(me, bot, smg.Id, head, t);
                client.OnKill(t, head);
                if (me.FrenzyLevel != client.Level)
                    mismatches++;
                sb.Append(me.FrenzyLevel);
            }
            sb.Append(':');
            for (var k = 0; k < 60; k++)
            {
                t += 0.5;
                game.SelfTestFrenzyTick(t);
                client.Update(t);
                if (me.FrenzyLevel != client.Level)
                    mismatches++;
                sb.Append(me.FrenzyLevel);
            }
            notes = $"{mismatches} mismatches, class {me.FrenzyClass}, buffs {(me.FrenzyBuffsOn ? "on" : "off")}";
            if (mismatches > 0)
                Fail($"server and client frenzy meters disagree ({mismatches} times, seed {seed})");
            return sb.ToString();
        }
        var trace1 = Trace(7, true, out var n1);
        var trace2 = Trace(7, true, out _);
        var trace3 = Trace(8, false, out var n3);
        Line($"server vs client meter, 18 kills then 30 s idle (levels per kill : per 0.5 s): {trace1} ({n1})");
        Line($"another sequence, buffs off: {trace3} ({n3})");
        Line($"determinism: the same sequence twice gives {(trace1 == trace2 ? "identical" : "DIFFERENT")} traces");
        if (trace1 != trace2)
            Fail("frenzy traces differ between two identical runs");
        if (!trace1.Contains('6') && !trace1.Contains('5'))
            Fail("the test spree never reached level 5 (the sequence proves little)");

        // Zombies weighting: the shared meter's reduced-weight kills (ZombieScale).
        var zm = new FrenzyMeterCore { Zombies = true };
        var pm = new FrenzyMeterCore();
        zm.OnKill(1, false);
        pm.OnKill(1, false);
        Line($"Zombies: a kill adds {zm.Heat:0.0} heat vs {pm.Heat:0.0} in PvP (x{FrenzyMeterCore.ZombieScale})");
        if (MathF.Abs(zm.Heat - pm.Heat * FrenzyMeterCore.ZombieScale) > 1e-3f)
            Fail("Zombies kills should be worth ZombieScale of a PvP kill");

        // ---- Class rule, buffs, heal, bots, grace, replication ----
        World.SetMap(Maps.All[0]);
        using (var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1))
        {
            var me = game.Players[0];
            var bot = game.Players.Values.First(x => x.Id != 0);
            game.SelfTestPerks(me, PerkSet.Empty); // no Last Stand saves, no Bloodthirst heals (bots pick random perks)
            game.SelfTestPerks(bot, PerkSet.Empty);
            var t = 10.0;
            void Kill(Player a, Player v, byte id, bool head = false)
            {
                game.SelfTestPlace(v, new Vector3(5, 0, 8), 0);
                if (!a.Alive)
                    game.SelfTestPlace(a, new Vector3(5, 0, 5), MathF.PI);
                t += 0.6;
                game.SelfTestFrenzyTick(t - 0.01);
                game.SelfTestFrenzyKill(a, v, id, head, t);
            }
            var smg = FirstOfClass(WeaponClass.SMG)!;
            var pistol = FirstOfClass(WeaponClass.Pistol)!;
            var lmg = FirstOfClass(WeaponClass.LMG)!;
            var special = FirstOfClass(WeaponClass.Special)!;
            var classes = new List<string>();
            Kill(me, bot, smg.Id);
            classes.Add($"SMG kill -> {me.FrenzyClass}");
            Kill(me, bot, Game.MeleeKillId);
            classes.Add($"melee -> {me.FrenzyClass}");
            Kill(me, bot, (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag));
            classes.Add($"frag -> {me.FrenzyClass}");
            Kill(me, bot, KillInfo.StreakKillIdFirst);
            classes.Add($"streak -> {me.FrenzyClass}");
            var smgKept = me.FrenzyClass == WeaponClass.SMG;
            Kill(me, bot, pistol.Id);
            classes.Add($"pistol kill -> {me.FrenzyClass}");
            Line($"class rule (most recent gun kill): {string.Join(", ", classes)}; level {me.FrenzyLevel}");
            if (!smgKept || me.FrenzyClass != WeaponClass.Pistol || classes[0] != "SMG kill -> SMG")
                Fail("frenzy class rule: the class should follow the latest gun kill and stay through melee, grenades and streaks");

            // Special: kills give health back from level 3.
            var heals = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                me.Health = 40;
                var lvlBefore = me.FrenzyLevel;
                Kill(me, bot, special.Id);
                heals.Add($"L{me.FrenzyLevel}: 40->{me.Health}");
                if (me.FrenzyLevel >= 3 && me.Health != Math.Min(World.MaxHealth, 40 + (int)MathF.Round(FrenzyBuffs.At(WeaponClass.Special, me.FrenzyLevel, FrenzyStat.KillHeal))))
                    Fail($"Special frenzy heal at L{me.FrenzyLevel}: 40 -> {me.Health}");
                if (me.FrenzyLevel < 3 && me.Health != 40)
                    Fail($"Special heal below level 3 (L{lvlBefore}->{me.FrenzyLevel})");
            }
            Line($"Special kill heals: {string.Join(", ", heals)}");

            // Replication: the snapshot carries level, class and the buffs bit to every client.
            me.FrenzyClass = WeaponClass.LMG;
            game.SelfTestFrenzyTick(t + 0.01);
            var gotMe = new Player();
            var gotBot = new Player();
            var packet = new Snapshot.Packet();
            var seenSlow = 0;
            foreach (var bytes in game.SelfTestSnapshotFor(me))
            {
                var r = new PacketReader(bytes.AsSpan(1));
                if (!Snapshot.Read(ref r, packet))
                    continue;
                if (packet.Own is { } own)
                {
                    Game.FrenzyFromWire(gotMe, own.Slow.FrenzyLevel, own.Slow.FrenzyClass);
                    seenSlow++;
                }
                foreach (var s in packet.Slow)
                    if (s.Id == bot.Id)
                        Game.FrenzyFromWire(gotBot, s.FrenzyLevel, s.FrenzyClass);
            }
            Line($"replication: sent L{me.FrenzyLevel} {me.FrenzyClass} buffs {me.FrenzyBuffsOn} -> got L{gotMe.FrenzyLevel} {gotMe.FrenzyClass} buffs {gotMe.FrenzyBuffsOn}; slow entry {Snapshot.SlowBytes} bytes, protocol {ServerInfo.ProtocolVersion}");
            if (seenSlow == 0 || gotMe.FrenzyLevel != me.FrenzyLevel || gotMe.FrenzyClass != me.FrenzyClass || gotMe.FrenzyBuffsOn != me.FrenzyBuffsOn)
                Fail("frenzy fields didn't survive the snapshot");
            var trips = 0;
            foreach (var c in Enum.GetValues<WeaponClass>())
                for (byte l = 0; l <= 6; l++)
                    foreach (var on in new[] { false, true })
                    {
                        var w = new PacketWriter();
                        w.Begin(Msg.State);
                        var src = new Player { FrenzyClass = c, FrenzyLevel = l, FrenzyBuffsOn = on };
                        Snapshot.WriteSlow(w, new SnapSlow { Id = 3, Kills = 7, FrenzyLevel = l, FrenzyClass = Game.FrenzyWire(src) });
                        var rr = new PacketReader(w.Span[1..]);
                        var s = Snapshot.ReadSlow(ref rr);
                        var dst = new Player();
                        Game.FrenzyFromWire(dst, s.FrenzyLevel, s.FrenzyClass);
                        if (w.Length - 1 == Snapshot.SlowBytes && s.Kills == 7 && dst.FrenzyLevel == l && dst.FrenzyClass == c && dst.FrenzyBuffsOn == on)
                            trips++;
                    }
            var wantTrips = Enum.GetValues<WeaponClass>().Length * 7 * 2;
            Line($"slow entry round trips ({Enum.GetValues<WeaponClass>().Length} classes x 7 levels x buffs on/off): {trips}/{wantTrips}");
            if (trips != wantTrips)
                Fail("frenzy slow-entry round trip");
            var junk = new Player();
            Game.FrenzyFromWire(junk, 200, 0x7E);
            if (junk.FrenzyLevel > 6 || !Enum.IsDefined(junk.FrenzyClass))
                Fail("bad frenzy bytes from the wire aren't sanitised");

            // The server's grace for prediction: an LMG frenzy's speed is allowed for a moment after it ends.
            for (var i = 0; i < 6; i++)
                Kill(me, bot, lmg.Id);
            var before = game.SelfTestFrenzyAllowed(me, lmg);
            game.SelfTestFrenzyKill(bot, me, bot.Weapon, false, t + 0.1); // dies: frenzy over
            var during = game.SelfTestFrenzyAllowed(me, lmg);
            game.SelfTestAdvanceTicks(Game.FrenzyGraceTicks + 1);
            var after = game.SelfTestFrenzyAllowed(me, lmg);
            Line($"server checks for {lmg.Name} (mobility {F2(lmg.Mobility)}): allowed speed {F2(before.Mobility)} in frenzy, {F2(during.Mobility)} right after dying (grace), {F2(after.Mobility)} after {Game.FrenzyGraceTicks} ticks; level now {me.FrenzyLevel}");
            if (lmg.Mobility < 1f && (before.Mobility <= lmg.Mobility || during.Mobility != before.Mobility || after.Mobility != lmg.Mobility))
                Fail("server move-speed allowance for the LMG frenzy buff (or its grace) is wrong");

            // Bots: a bot's kills build its own frenzy, and its buffs work through the same calls.
            game.SelfTestPlace(bot, new Vector3(5, 0, 5), MathF.PI);
            for (var i = 0; i < 8; i++)
                Kill(bot, me, lmg.Id);
            var botReload = FrenzyBuffs.Value(bot, lmg, FrenzyStat.Reload);
            Line($"bots: after 8 quick LMG kills the bot is at L{bot.FrenzyLevel} {bot.FrenzyClass}, reload x{F2(botReload)}, speed {F2(FrenzyBuffs.Mobility(bot, lmg))}; the victim's frenzy L{me.FrenzyLevel}");
            if (bot.FrenzyLevel < 3 || bot.FrenzyClass != WeaponClass.LMG || botReload >= 1f)
                Fail("bots should build a frenzy and get its buffs");
            if (me.FrenzyLevel != 0)
                Fail("dying should end the frenzy");

            // Buffs off: the level and class still replicate (for music and visuals), the buffs don't apply.
            game.FrenzyBuffsEnabled = false;
            game.SelfTestFrenzyTick(t + 0.05);
            var offReload = FrenzyBuffs.Value(bot, lmg, FrenzyStat.Reload);
            Line($"buffs off: bot still L{bot.FrenzyLevel} {bot.FrenzyClass}, buffs bit {bot.FrenzyBuffsOn}, reload x{F2(offReload)}, wire 0x{Game.FrenzyWire(bot):X2}");
            if (bot.FrenzyBuffsOn || offReload != 1f || bot.FrenzyLevel == 0 || (Game.FrenzyWire(bot) & 0x80) != 0)
                Fail("the Frenzy buffs setting off should switch the buffs off (and only them)");
            game.FrenzyBuffsEnabled = true;
            game.SelfTestFrenzyTick(t + 0.06);

            // Through the real client path: the spread the local player would fire with (a temporary test buff on its weapon's class).
            var held = game.Active.Def;
            var entry = FrenzyClasses.Get(held.Class);
            var saved = entry.Buffs;
            try
            {
                entry.Buffs = new FrenzyBuffDef { Name = "test", MinLevel = 3, Effects = [new FrenzyEffect(FrenzyStat.MoveSpread, 0.5f, 0.5f), new FrenzyEffect(FrenzyStat.Spread, 0.8f, 0.8f)] };
                game.SelfTestPlace(me, new Vector3(5, 0, 5), 0);
                me.Sim.Velocity = new Vector3(6, 0, 0);
                me.FrenzyClass = held.Class;
                me.FrenzyBuffsOn = true;
                me.FrenzyLevel = 0;
                var plain = game.CurrentSpread();
                me.FrenzyLevel = 4;
                var buffed = game.CurrentSpread();
                me.FrenzyBuffsOn = false;
                var off = game.CurrentSpread();
                var baseCone = held.HipSpreadRad * Perks.SpreadMult(me.Perks, 0f) * World.SpreadOf(me.Sim.Stance);
                var move = plain - baseCone - game.Bloom;
                var want = (baseCone + move * 0.5f + game.Bloom) * 0.8f;
                Line($"client spread ({held.Name}, moving): {F2(plain * 1000)} mrad plain, {F2(buffed * 1000)} with a test move-spread x0.5 / cone x0.8 buff (want {F2(want * 1000)}), {F2(off * 1000)} with buffs off");
                if (MathF.Abs(buffed - MathF.Min(want, held.MaxSpreadRad)) > 1e-4f || off != plain)
                    Fail("CurrentSpread doesn't apply the frenzy spread buffs");
            }
            finally
            {
                entry.Buffs = saved;
            }
        }
    }
}
