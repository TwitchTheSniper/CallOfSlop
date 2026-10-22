using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Tactical gear" (see SelfTest.cs): the eight tactical items (TacticalGear.cs, GameTactical.cs).
/// Their definitions, sounds and text; the flash and stun formulas (by angle, distance and walls, no strobing); each
/// effect measured on a server (flash blinding people and bots, stun slowing, stim healing, trophy intercepts, motion
/// sensor pings and Ghost, decoy bursts luring bots, cover stopping bullets and blasts, tactical insertion spawns,
/// devices breaking); replication to a client; bots using them; modes without equipment; unlock levels and
/// loadout.txt; determinism; and no per-tick allocations.
/// </summary>
public static partial class SelfTest
{
    private static readonly ThrowableKind[] TacKinds =
    [
        ThrowableKind.Flash, ThrowableKind.Stun, ThrowableKind.Decoy, ThrowableKind.MotionSensor,
        ThrowableKind.Trophy, ThrowableKind.Cover, ThrowableKind.Insertion, ThrowableKind.Stim,
    ];

    private static void CheckTacticalGear()
    {
        TacticalDefs();
        TacticalFormulas();
        TacticalUnlocksAndSaves();
        TacticalProtocol();
        var map = MapFile.Load("""
            { "name": "Tactical test", "halfX": 40, "halfZ": 40, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Block", "x0": -4, "y0": 0, "z0": 8, "x1": 4, "y1": 4.0, "z1": 9, "color": "#808080" },
              { "op": "Spawn", "x": 30, "z": 30 } ] }
            """, "tactical-test.json");
        try
        {
            var first = TacticalServer(map, log: true);
            var again = TacticalServer(map, log: false);
            Line($"determinism: two identical runs give {(first == again ? "the same" : "DIFFERENT")} results ({first.Length} chars of measurements)");
            if (first != again)
                Fail($"tactical gear: two identical server runs differ:%0A{first}%0A{again}");
            TacticalTeams(map);
            TacticalNoEquipmentModes();
        }
        finally
        {
            World.SetMap(Maps.All[0]);
        }
    }

    // ---- Definitions, sounds, text ---------------------------------------------------------------

    private static void TacticalDefs()
    {
        var names = new List<string>();
        foreach (var k in TacKinds)
        {
            var d = Throwables.Get(k);
            var lines = TacticalGear.StatLines(k).ToArray();
            names.Add($"{d.Name} ({TacticalGear.UseOf(k)}, {d.PerLife}/life, level {Progress.UnlockLevel(k)})");
            if (d.Kind != k || !d.IsEquipment || !TacticalGear.Is(k) || TacticalGear.UseOf(k) == TacticalUse.None || d.PerLife < 1)
                Fail($"tactical gear: {k} is defined wrong (kind {d.Kind}, equipment {d.IsEquipment}, use {TacticalGear.UseOf(k)}, per life {d.PerLife})");
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(d.Blurb))
                Fail($"tactical gear: {d.Name} has no blurb or stat lines");
            foreach (var text in lines.Append(d.Blurb).Append(d.Name))
                if (text.Any(c => c < ' ' || c > '~'))
                    Fail($"tactical gear: non-ASCII text in {d.Name}: \"{text}\"");
        }
        Line("items: " + string.Join(", ", names));
        if (TacticalGear.Is(ThrowableKind.Recon) || TacticalGear.Is(ThrowableKind.Frag) || Throwables.Grenades.Any(g => g.IsEquipment))
            Fail("tactical gear: the old grenades or the Recon Pulse changed category");

        // Sounds: each one exists, is its own, and renders the same every time.
        Sfx[] sounds = [Sfx.FlashBang, Sfx.StunPop, Sfx.Tinnitus, Sfx.TrophyZap, Sfx.MotionPing, Sfx.SensorBeep, Sfx.StimHiss, Sfx.DeployDevice];
        var made = new List<short[]>();
        foreach (var s in sounds)
        {
            var pcm = Synth.Effect(s, 0);
            var peak = pcm.Length == 0 ? 0 : pcm.Max(v => Math.Abs((int)v));
            if (pcm.Length < 1000 || peak < 1000)
                Fail($"tactical gear: sound {s} is missing or silent ({pcm.Length} samples, peak {peak})");
            if (!pcm.AsSpan().SequenceEqual(Synth.Effect(s, 0)))
                Fail($"tactical gear: sound {s} isn't deterministic");
            if (made.Any(m => m.AsSpan().SequenceEqual(pcm)))
                Fail($"tactical gear: sound {s} is the same as another tactical sound");
            made.Add(pcm);
        }
        Line("sounds: " + string.Join(", ", sounds.Select((s, i) => $"{s} {made[i].Length / 44.1f:0} ms")));
    }

    // ---- Pure formulas ------------------------------------------------------------------------------

    private static void TacticalFormulas()
    {
        var eye = Vector3.Zero;
        var fwd = -Vector3.UnitZ;
        float F(Vector3 at, bool line = true, bool flak = false, bool self = false) => TacticalGear.FlashStrength(eye, fwd, at, line, flak, self);
        var facing4 = F(new Vector3(0, 0, -4));
        var side4 = F(new Vector3(4, 0, 0));
        var behind4 = F(new Vector3(0, 0, 4));
        var facing12 = F(new Vector3(0, 0, -12));
        var walled = F(new Vector3(0, 0, -4), line: false);
        var flak = F(new Vector3(0, 0, -4), flak: true);
        var self = F(new Vector3(0, 0, -4), self: true);
        var outside = F(new Vector3(0, 0, -TacticalGear.FlashRadius - 0.1f));
        Line($"flash strength at 4 m: facing {facing4:0.00}, side-on {side4:0.00}, behind {behind4:0.00}; facing at 12 m {facing12:0.00}; "
             + $"wall {walled:0.00}, Flak Jacket {flak:0.00}, own {self:0.00}, past {TacticalGear.FlashRadius:0} m {outside:0.00}");
        if (facing4 < 0.85f || side4 >= facing4 || behind4 >= side4 || behind4 > 0.2f || facing12 >= facing4 * 0.5f)
            Fail("flash strength doesn't fall off with angle and distance as designed");
        if (walled != 0 || outside != 0 || MathF.Abs(flak - facing4 * 0.5f) > 0.01f || MathF.Abs(self - facing4 * 0.5f) > 0.01f)
            Fail("flash strength: walls, range, Flak Jacket or your own flash are wrong");

        // White-out: a flat hold, then it only ever falls (no strobing), gone at FlashTotal. Deafness the same.
        var rises = 0;
        var worst = "";
        foreach (var s in new[] { 0.1f, 0.3f, 0.6f, 0.9f, 1f })
        {
            float prevA = TacticalGear.FlashAlpha(s, 0), prevD = TacticalGear.FlashDeafen(s, 0);
            for (var t = 1f / 240; t < 8f; t += 1f / 240)
            {
                float a = TacticalGear.FlashAlpha(s, t), dd = TacticalGear.FlashDeafen(s, t);
                if (a > prevA + 1e-6f || dd > prevD + 1e-6f)
                {
                    rises++;
                    worst = $"s {s} at {t:0.000} s";
                }
                prevA = a;
                prevD = dd;
            }
            var total = TacticalGear.FlashTotal(s);
            if (TacticalGear.FlashAlpha(s, total + 0.01f) != 0 || TacticalGear.FlashAlpha(s, TacticalGear.FlashHold(s) * 0.99f) != TacticalGear.FlashAlpha(s, 0))
                Fail($"flash at strength {s}: no flat hold or not gone after {total:0.00} s");
        }
        Line($"flash overlay: full flash {TacticalGear.FlashHold(1):0.0} s hold + {TacticalGear.FlashFade(1):0.0} s fade (total {TacticalGear.FlashTotal(1):0.0} s), "
             + $"behind at 4 m {TacticalGear.FlashTotal(behind4):0.0} s; bot blind {TacticalGear.BotBlindTime(facing4):0.00} s facing, {TacticalGear.BotBlindTime(behind4):0.00} s behind; "
             + $"{rises} brightening steps (strobe check)");
        if (rises > 0)
            Fail($"the flash overlay or deafness gets stronger again after it starts ({worst}): that's a strobe");
        if (TacticalGear.BotBlindTime(facing4) < 1.5f || TacticalGear.BotBlindTime(behind4) != 0)
            Fail("bots: facing a flash must blind them for a while; looking away must not");

        // Stun: strength by distance, slows by strength, a short tail.
        float s0 = TacticalGear.StunStrength(0, true, false, false), s4 = TacticalGear.StunStrength(4, true, false, false),
            s8 = TacticalGear.StunStrength(TacticalGear.StunRadius, true, false, false), sw = TacticalGear.StunStrength(2, false, false, false);
        Line($"stun strength: 0 m {s0:0.00}, 4 m {s4:0.00}, {TacticalGear.StunRadius:0} m {s8:0.00}, behind a wall {sw:0.00}; full stun {TacticalGear.StunTime(1):0.0} s at "
             + $"{TacticalGear.StunMove(1) * 100:0}% speed, {TacticalGear.StunLook(1) * 100:0}% turning, {TacticalGear.StunAds(1) * 100:0}% aim speed");
        if (s0 != 1 || MathF.Abs(s4 - (1 - MathF.Pow(0.5f, 1.5f))) > 0.01f || s8 != 0 || sw != 0)
            Fail("stun strength by distance is wrong");
        if (TacticalGear.StunNow(1, 0.1f) != 1 || TacticalGear.StunNow(1, TacticalGear.StunTime(1)) != 0 || TacticalGear.StunNow(1, TacticalGear.StunTime(1) - 0.25f) is <= 0 or >= 1)
            Fail("stun: not full at the start, or no smooth tail at the end");

        // Deterministic rolls and bot picks.
        var rolls = Enumerable.Range(0, 2000).Select(i => TacticalGear.Roll((uint)i, (uint)(i * 7), 0xE0)).ToArray();
        if (rolls.Any(r => r is < 0 or >= 1) || Math.Abs(rolls.Average() - 0.5) > 0.05 || TacticalGear.Roll(3, 4, 5) != TacticalGear.Roll(3, 4, 5))
            Fail("TacticalGear.Roll isn't a deterministic 0..1 spread");
        foreach (var kind in Enum.GetValues<BotPersonality>())
        {
            var picks = rolls.Select(r => TacticalGear.BotPick(kind, r)).ToArray();
            if (picks.Any(k => k != ThrowableKind.None && !TacticalGear.Is(k)) || picks.Distinct().Count() < 3)
                Fail($"bots: {kind} picks an odd mix of tactical items ({string.Join(", ", picks.Distinct())})");
        }
    }

    // ---- Unlocks and saves --------------------------------------------------------------------------

    private static void TacticalUnlocksAndSaves()
    {
        var levels = new List<string>();
        foreach (var (kind, level) in TacticalGear.UnlockLevels)
        {
            levels.Add($"{Throwables.Get(kind).Name} {Progress.UnlockLevel(kind)}");
            var below = new Progress { Xp = Progress.TotalFor(level - 1) };
            var at = new Progress { Xp = Progress.TotalFor(level) };
            if (Progress.UnlockLevel(kind) != level || below.IsUnlocked(kind) || !at.IsUnlocked(kind) || level > Progress.MaxLevel)
                Fail($"{Throwables.Get(kind).Name}: unlocks at {Progress.UnlockLevel(kind)} (want {level}; locked below: {!below.IsUnlocked(kind)})");
        }
        if (TacticalGear.UnlockLevels.Length != TacKinds.Length || TacKinds.Any(k => !TacticalGear.UnlockLevels.Any(u => u.Kind == k)))
            Fail("every tactical item needs one unlock level");
        // The older unlocks didn't move.
        var old = $"Frag {Progress.UnlockLevel(ThrowableKind.Frag)}, Recon {Progress.UnlockLevel(ThrowableKind.Recon)}, Impact {Progress.UnlockLevel(ThrowableKind.Impact)}, Smoke {Progress.UnlockLevel(ThrowableKind.Smoke)}";
        Line($"unlock levels: {string.Join(", ", levels)} (older: {old})");
        if (old != "Frag 4, Recon 8, Impact 13, Smoke 20")
            Fail($"the older grenade unlock levels moved: {old}");

        // loadout.txt: a tactical item saves and loads; it can't sit in the grenade slot; old files load as before.
        var backup = LoadoutStore.SaveLines().ToArray();
        try
        {
            LoadoutStore.LoadLines(["selected=0", "loadout1.name=Tac", "loadout1.equipment=MotionSensor", "loadout1.grenade=Flash"]);
            var e1 = LoadoutStore.Slots[0].Equipment;
            var g1 = LoadoutStore.Slots[0].Grenade;
            LoadoutStore.LoadLines(LoadoutStore.SaveLines().ToArray());
            var e2 = LoadoutStore.Slots[0].Equipment;
            var locked = LoadoutStore.Effective(LoadoutStore.Slots[0], new Progress()).Equipment;
            var open = LoadoutStore.Effective(LoadoutStore.Slots[0], new Progress { Xp = Progress.TotalFor(Progress.MaxLevel) }).Equipment;
            LoadoutStore.LoadLines(["selected=0", "loadout1.name=Old", "loadout1.grenade=Frag", "loadout1.equipment=Recon"]);
            var oldE = LoadoutStore.Slots[0].Equipment;
            var oldG = LoadoutStore.Slots[0].Grenade;
            Line($"loadout.txt: equipment {e1} -> saved and loaded {e2}; Flashbang as a grenade -> {g1}; level 1 uses {locked}, max level {open}; an old file: {oldG} + {oldE}");
            if (e1 != ThrowableKind.MotionSensor || e2 != e1 || g1 == ThrowableKind.Flash || locked != ThrowableKind.None || open != e1
                || oldE != ThrowableKind.Recon || oldG != ThrowableKind.Frag)
                Fail("loadout.txt: tactical equipment doesn't save, load, lock or migrate correctly");
        }
        finally
        {
            LoadoutStore.LoadLines(backup);
        }
    }

    private static void TacticalProtocol()
    {
        var last = Enum.GetValues<Msg>().Max();
        Line($"protocol {ServerInfo.ProtocolVersion}; Msg.TacState {(byte)Msg.TacState}, Msg.TacEvent {(byte)Msg.TacEvent} (last {last}); "
             + $"device {TacDevice.NetBytes} bytes, event {1 + Game.TacEventBytes} bytes");
        if (ServerInfo.ProtocolVersion < 26 || (byte)Msg.TacEvent != (byte)Msg.TacState + 1)
            Fail("tactical gear: protocol version not bumped, or the new messages aren't together");
        if ((byte)ThrowableKind.Flash != (byte)ThrowableKind.Recon + 1)
            Fail("tactical gear: the new ThrowableKind values must come after the old ones");
    }

    // ---- On a server ------------------------------------------------------------------------------

    /// <summary>
    /// Every effect measured on a fresh free-for-all server on the test map. Returns the measurements as text (so two
    /// runs can be compared for determinism); <paramref name="log"/> prints lines and checks them.
    /// </summary>
    private static string TacticalServer(Map map, bool log)
    {
        var sb = new System.Text.StringBuilder();
        void Note(string s)
        {
            sb.Append(s).Append('\n');
            if (log)
                Line(s);
        }
        void Check(bool ok, string what)
        {
            if (log && !ok)
                Fail(what);
        }

        using var game = ModeGame(3, GameMode.FreeForAll);
        World.SetMap(map);
        var me = game.Players[0];
        var bots = game.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToArray();
        var (b1, b2, b3) = (bots[0], bots[1], bots[2]);
        foreach (var p in game.Players.Values)
            game.SelfTestPerks(p, PerkSet.Empty);
        var now = 100.0;
        void Park()
        {
            foreach (var p in game.Players.Values)
            {
                game.SelfTestPlace(p, new Vector3(-36 + p.Id * 1.5f, 0, -36), 0);
                if (p.Bot is { } brain)
                    brain.Target = byte.MaxValue;
            }
        }
        void Next()
        {
            game.SelfTestClearTactical();
            game.Projectiles.Clear();
            now += 20;
            Park();
        }
        void Tick(double seconds, bool effects = false)
        {
            for (var t = 0.0; t < seconds; t += World.TickDt)
            {
                now += World.TickDt;
                game.SelfTestTacticalTick(now);
                if (effects)
                    game.SelfTestUpdateTacticalEffects(now, (float)World.TickDt);
            }
        }
        TacDevice? Newest(ThrowableKind k) => game.TacDevices.LastOrDefault(d => d.Kind == k);

        // ---- Flashbang: by angle, walls, own flash; bots lose their target ----
        Park();
        var burst = new Vector3(0, 1.5f, 0);
        game.SelfTestPlace(b1, new Vector3(0, 0, 5), 0); // facing it (yaw 0 looks along -Z)
        game.SelfTestPlace(b2, new Vector3(5, 0, 0), 0); // side-on
        game.SelfTestPlace(b3, new Vector3(0, 0, -5), 0); // back to it
        game.SelfTestPlace(me, new Vector3(0, 0, 11), 0); // facing it, behind the wall
        b1.Bot!.Target = me.Id;
        b3.Bot!.Target = me.Id;
        game.SelfTestTacticalDetonate(me, ThrowableKind.Flash, burst - new Vector3(0, 0.15f, 0), now);
        float f1 = game.SelfTestFlashOf(b1).Strength, f2 = game.SelfTestFlashOf(b2).Strength, f3 = game.SelfTestFlashOf(b3).Strength, fMe = game.SelfTestFlashOf(me).Strength;
        var blind1 = TacticalGear.BotBlindTime(f1);
        bool lost1 = b1.Bot.Target == byte.MaxValue, kept3 = b3.Bot.Target == me.Id;
        bool blindNow = game.SelfTestBotBlinded(b1, now + 0.5), blindAfter = game.SelfTestBotBlinded(b1, now + blind1 + 0.05), blind3 = game.SelfTestBotBlinded(b3, now + 0.1);
        Note($"flashbang 5 m away: facing {f1:0.00}, side-on {f2:0.00}, back turned {f3:0.00}, thrower behind a wall {fMe:0.00}; "
             + $"facing bot lost its target {lost1}, blind {blindNow} for {blind1:0.00} s (clear after: {!blindAfter}); back-turned bot kept its target {kept3} (blind {blind3})");
        Check(f1 > 0.75f && f2 < f1 && f2 > f3 && f3 > 0 && f3 < 0.2f && fMe == 0, "flashbang: strengths by angle and walls are wrong on the server");
        Check(lost1 && blindNow && !blindAfter && kept3 && !blind3, "flashbang: bots facing it must lose their target for the blind time; bots looking away mustn't");

        // Your own flash, facing it: half strength, and the local white-out and deafness start from the event.
        now += 10;
        game.SelfTestPlace(me, new Vector3(0, 0, 4), 0);
        game.SelfTestTacticalDetonate(me, ThrowableKind.Flash, burst - new Vector3(0, 0.15f, 0), now);
        game.SelfTestUpdateTacticalEffects(now + 0.1, 0.1f);
        var own = game.SelfTestFlashOf(me).Strength;
        var alpha = game.LocalFlashAlpha(now + 0.1);
        var deaf = game.Audio.Deafen;
        var hides = game.LocalFlashHidesHud(now + 0.1);
        Note($"own flashbang 4 m away, facing it: strength {own:0.00} (half), local overlay {alpha:0.00}, deafen {deaf:0.00}, HUD markers hidden {hides}");
        Check(own > 0.3f && own < 0.55f && alpha > 0.5f && deaf > 0.4f && hides, "own flashbang: should be half strength and white out and deafen the local player");
        game.SelfTestUpdateTacticalEffects(now + 6, 0.1f);
        Check(game.LocalFlashAlpha(now + 6) == 0 && game.Audio.Deafen == 0, "own flashbang: the white-out or deafness didn't wear off");

        // ---- Stun: slows movement (bots exactly, people capped), turning; a Stim clears it ----
        Next();
        game.SelfTestPlace(b1, new Vector3(0, 0, 3), 0);
        game.SelfTestPlace(me, new Vector3(1, 0, 3), 0);
        game.SelfTestTacticalDetonate(b2, ThrowableKind.Stun, new Vector3(0, 0.1f, 0), now);
        var st1 = game.SelfTestStunOf(b1).Strength;
        var stMe = game.SelfTestStunOf(me).Strength;
        var botCmd = game.SelfTestTacticalFilter(b1, new InputCmd { SpeedPct = 100 }, now + 0.2);
        var myCmd = game.SelfTestTacticalFilter(me, new InputCmd { SpeedPct = 100 }, now + 0.2);
        var botLater = game.SelfTestTacticalFilter(b1, new InputCmd { SpeedPct = 100 }, now + TacticalGear.StunTime(st1) + 0.05);
        Note($"stun grenade 3 m away: strength {st1:0.00} (bot), {stMe:0.00} (you); bot speed 100% -> {botCmd.SpeedPct}% (then {botLater.SpeedPct}% after {TacticalGear.StunTime(st1):0.0} s), "
             + $"your speed 100% -> {myCmd.SpeedPct}%, turning x{TacticalGear.StunLook(st1):0.00}");
        Check(st1 > 0.6f && stMe > 0.6f && Math.Abs(botCmd.SpeedPct - MathF.Round(100 * TacticalGear.StunMove(st1))) <= 1 && botLater.SpeedPct == 100 && myCmd.SpeedPct < 85,
            "stun grenade: should slow bots and people for its duration, then stop");
        me.Health = 10;
        game.SelfTestUseTactical(me, ThrowableKind.Stim, -Vector3.UnitZ, now);
        var afterShot = me.Health;
        var stunCleared = game.SelfTestStunOf(me).Strength == 0;
        Tick(2);
        var after2 = me.Health;
        Tick(3);
        var after5 = me.Health;
        Note($"stim shot at 10 HP: {afterShot} at once, {after2} after 2 s, {after5} after 5 s; stun cleared {stunCleared}");
        Check(afterShot == 10 + TacticalGear.StimHeal && Math.Abs(after2 - (afterShot + 2 * TacticalGear.StimRegen)) <= 2 && after5 == World.MaxHealth && stunCleared,
            "stim shot: wrong heal, regen or stun clearing");
        me.Health = 40;
        Tick(1);
        Check(me.Health == 40, "stim shot: the regen kept going after its time");

        // ---- Trophy System: shoots down enemy grenades (not its owner's), 3 charges, then breaks ----
        Next();
        game.SelfTestPlace(me, new Vector3(-10, 0, -10), 0);
        game.SelfTestUseTactical(me, ThrowableKind.Trophy, -Vector3.UnitZ, now);
        var trophy = Newest(ThrowableKind.Trophy);
        var intercepts = new List<string>();
        var seen0 = game.TacInterceptsSeen;
        if (trophy == null)
            Check(false, "trophy system: wasn't placed");
        else
        {
            game.Projectiles[501] = new Projectile { Id = 501, Kind = ThrowableKind.Frag, Owner = me.Id, Pos = trophy.Pos + new Vector3(2, 1, 0), Born = now };
            for (ushort i = 0; i < 3; i++)
            {
                var id = (ushort)(600 + i);
                game.Projectiles[id] = new Projectile { Id = id, Kind = i == 1 ? ThrowableKind.Stun : ThrowableKind.Frag, Owner = b1.Id, Pos = trophy.Pos + new Vector3(-3, 1.5f, 2), Born = now };
                var t0 = now;
                Tick(1.5);
                intercepts.Add(game.Projectiles.ContainsKey(id) ? "missed" : $"{(int)((now - t0) * 1000) / 50 * 50} ms, {trophy.Charges} left");
            }
            var gone = !game.TacDevices.Contains(trophy);
            Note($"trophy system: enemy grenades shot down: {string.Join("; ", intercepts)}; broke after the last {gone}; its owner's frag left alone {game.Projectiles.ContainsKey(501)}; "
                 + $"{game.TacInterceptsSeen - seen0} intercepts seen");
            Check(intercepts.All(s => s != "missed") && gone && game.Projectiles.ContainsKey(501) && game.TacInterceptsSeen - seen0 == 3,
                "trophy system: should shoot down 3 enemy throwables, leave its owner's alone, then break");
        }
        // Shot to pieces by an enemy: 60 health.
        game.SelfTestUseTactical(me, ThrowableKind.Trophy, -Vector3.UnitZ, now);
        if (Newest(ThrowableKind.Trophy) is { } t2)
        {
            var ownFlags = game.SelfTestShotTactical(t2, me, 100, now);
            var shots = 0;
            byte flags = 0;
            while (game.TacDevices.Contains(t2) && shots < 20)
            {
                flags = game.SelfTestShotTactical(t2, b1, 25, now);
                shots++;
            }
            Note($"trophy system: its owner's shot does nothing (flags {ownFlags}); an enemy breaks it in {shots} shots of 25 (last flags {flags})");
            Check(ownFlags == 0 && shots == 3 && (flags & 4) != 0 && (flags & Game.ShotHitMachine) != 0, "trophy system: wrong health or shot flags");
        }

        // ---- Motion Sensor: moving enemies only, crouch-speed and Ghost stay off it ----
        Next();
        game.SelfTestPlace(me, new Vector3(-10, 0, 14), 0);
        game.SelfTestTacticalDetonate(me, ThrowableKind.MotionSensor, new Vector3(-10, 0.05f, 10), now);
        var sensor = Newest(ThrowableKind.MotionSensor);
        if (sensor == null)
            Check(false, "motion sensor: no device after it landed");
        else
        {
            bool Sees(Vector3 at, Vector3 vel, PerkSet perks)
            {
                game.SelfTestPerks(b1, perks);
                game.SelfTestPlace(b1, at, 0);
                b1.Sim.Velocity = vel;
                return game.SensorSees(sensor, me, b1);
            }
            var ghost = PerkSet.Empty.With(PerkCategory.Movement, PerkId.Ghost);
            bool walk = Sees(new Vector3(-10, 0, 4), new Vector3(6, 0, 0), PerkSet.Empty), creep = Sees(new Vector3(-10, 0, 4), new Vector3(3, 0, 0), PerkSet.Empty),
                still = Sees(new Vector3(-10, 0, 4), Vector3.Zero, PerkSet.Empty), far = Sees(new Vector3(-10, 0, -10), new Vector3(6, 0, 0), PerkSet.Empty),
                ghosted = Sees(new Vector3(-10, 0, 4), new Vector3(6, 0, 0), ghost), mate = game.SensorSees(sensor, me, me);
            game.SelfTestPerks(b1, PerkSet.Empty);
            game.SelfTestPlace(b1, new Vector3(-10, 0, 4), 0);
            b1.Sim.Velocity = new Vector3(6, 0, 0);
            var pings0 = game.TacPingsSeen;
            Tick(2, effects: true);
            var pings = game.TacPingsSeen - pings0;
            var hudPing = game.Pings.Count(p => p.Target == b1.Id && now - p.At < 1);
            Note($"motion sensor: walking enemy {walk}, crouch speed {creep}, still {still}, 20 m away {far}, with Ghost {ghosted}, its owner {mate}; "
                 + $"{pings} pings in 2 s ({hudPing} on the HUD)");
            Check(walk && !creep && !still && !far && !ghosted && !mate && pings >= 3 && hudPing >= 1, "motion sensor: pings the wrong people");
            var hits = 0;
            while (game.TacDevices.Contains(sensor) && hits < 10)
            {
                game.SelfTestShotTactical(sensor, b1, 20, now);
                hits++;
            }
            Check(hits == 1, $"motion sensor: should break in one rifle shot (took {hits})");
            b1.Sim.Velocity = Vector3.Zero;
        }

        // ---- Decoy: fake bursts of its owner's gun; enemy bots go and look ----
        Next();
        game.SelfTestPlace(me, new Vector3(10, 0, -14), 0);
        game.SelfTestPlace(b2, new Vector3(10, 0, 20), 0);
        game.SelfTestPlace(b3, new Vector3(-30, 0, 30), 0); // about 57 m from the decoy: out of earshot (45 m)
        b2.Bot!.HeardTime = -1;
        b3.Bot!.HeardTime = -1;
        game.SelfTestTacticalDetonate(me, ThrowableKind.Decoy, new Vector3(10, 0.05f, -10), now);
        var decoy = Newest(ThrowableKind.Decoy);
        var shots0 = game.TacDecoyShotsPlayed;
        Tick(3, effects: true);
        var played = game.TacDecoyShotsPlayed - shots0;
        var lured = decoy != null && Vector3.Distance(b2.Bot.HeardAt, decoy.Pos) < 0.1f && b2.Bot.HeardTime > 0;
        var farLured = b3.Bot.HeardTime > 0;
        Note($"decoy: {played} fake shots in 3 s; a bot 30 m away goes to check {lured}, one 57 m away {farLured}");
        Check(decoy != null && played >= 2 && lured && !farLured, "decoy: should fire fake bursts and lure enemy bots in earshot only");
        Tick(TacticalGear.DecoyLife, effects: true);
        Check(decoy == null || !game.TacDevices.Contains(decoy), "decoy: still there after its life");

        // ---- Deployable Cover: stops bullets both ways (not over the top), stops blasts, explosives hurt it double ----
        Next();
        game.SelfTestPlace(me, new Vector3(20, 0, -20), 0);
        game.SelfTestPlace(b1, new Vector3(20, 0, -32), MathF.PI);
        game.SelfTestUseTactical(me, ThrowableKind.Cover, -Vector3.UnitZ, now);
        var cover = Newest(ThrowableKind.Cover);
        if (cover == null)
            Check(false, "deployable cover: wasn't placed");
        else
        {
            bool Ray(Vector3 from, Vector3 dir, Player who) => game.SelfTestTacticalRay(from, dir, 30, who) == cover;
            var enemyLow = Ray(new Vector3(20, 0.8f, -32), Vector3.UnitZ, b1);
            var mineLow = Ray(new Vector3(20, 0.8f, -19), -Vector3.UnitZ, me);
            var over = Ray(new Vector3(20, 1.6f, -32), Vector3.UnitZ, b1);
            var past = Ray(new Vector3(22, 0.8f, -32), Vector3.UnitZ, b1);
            var hp0 = cover.Health;
            var ownFlags = game.SelfTestShotTactical(cover, me, 30, now);
            var hpOwn = cover.Health;
            var foeFlags = game.SelfTestShotTactical(cover, b1, 30, now);
            var hpFoe = cover.Health;
            game.SelfTestBlastTactical(b1, cover.Hitbox.Center + new Vector3(0, 0, -1f), 6, 30, now);
            var blastLoss = hpFoe - cover.Health;
            Note($"deployable cover {Vector3.Distance(me.Position, cover.Pos):0.0} m ahead: stops an enemy's low shot {enemyLow}, your own low shot {mineLow}, "
                 + $"not a shot over the top {!over}, not one past its edge {!past}; health {hp0}: your shot -> {hpOwn} (flags {ownFlags}), enemy 30 -> {hpFoe}, "
                 + $"a blast {blastLoss} (double)");
            Check(enemyLow && mineLow && !over && !past, "deployable cover: blocks the wrong shots");
            Check(hp0 == TacticalGear.CoverHealth && hpOwn == hp0 && ownFlags == 0 && hpFoe == hp0 - 30 && (foeFlags & Game.ShotHitMachine) != 0 && blastLoss > 0 && blastLoss % 2 == 0,
                "deployable cover: wrong damage rules");

            // A frag on the far side: the wall takes it for you.
            var blastAt = new Vector3(20, 0.3f, -23.4f);
            game.SelfTestPlace(me, new Vector3(20, 0, -20), 0);
            game.SelfTestDetonate(b1, ThrowableKind.Frag, blastAt, now);
            var shielded = World.MaxHealth - me.Health;
            var blocks = game.CoverBlocks(blastAt, me.Chest);
            game.SelfTestClearTactical();
            game.SelfTestPlace(me, new Vector3(20, 0, -20), 0);
            game.SelfTestDetonate(b1, ThrowableKind.Frag, blastAt, now + 1);
            var open = World.MaxHealth - me.Health;
            Note($"a frag 3.4 m away on the far side of the cover: {shielded} damage (blocked {blocks}); without the cover {open}");
            Check(blocks && shielded == 0 && open > 0, "deployable cover: should stop a grenade blast");
        }

        // ---- Tactical Insertion: one at a time, your next spawn, enemies can shoot it out ----
        Next();
        game.SelfTestPlace(me, new Vector3(-20, 0, 20), 0);
        game.SelfTestUseTactical(me, ThrowableKind.Insertion, -Vector3.UnitZ, now);
        game.SelfTestPlace(me, new Vector3(-22, 0, 22), 0);
        game.SelfTestUseTactical(me, ThrowableKind.Insertion, -Vector3.UnitZ, now + 1);
        var mine = game.TacDevices.Count(d => d.Kind == ThrowableKind.Insertion && d.Owner == me.Id);
        var picked = new Vector3(30, 0, 30);
        var spawn1 = game.SelfTestTacticalSpawn(me, picked, now + 2);
        var spawn2 = game.SelfTestTacticalSpawn(me, picked, now + 3);
        game.SelfTestUseTactical(me, ThrowableKind.Insertion, -Vector3.UnitZ, now + 4);
        var ins = Newest(ThrowableKind.Insertion);
        var insFlags = ins != null ? game.SelfTestShotTactical(ins, b1, TacticalGear.InsertionHealth, now + 4) : (byte)0;
        var spawn3 = game.SelfTestTacticalSpawn(me, picked, now + 5);
        Note($"tactical insertion: {mine} down after placing two; next spawn at ({spawn1.X:0.0}, {spawn1.Z:0.0}), the one after back at the picked spawn {spawn2 == picked}; "
             + $"shot out by an enemy (flags {insFlags}) -> spawn at the picked spot {spawn3 == picked}");
        Check(mine == 1 && Vector2.Distance(new(spawn1.X, spawn1.Z), new(-22, 22)) < 0.1f && spawn2 == picked && (insFlags & 4) != 0 && spawn3 == picked,
            "tactical insertion: wrong spawn rules");

        // ---- Bots use their items ----
        Next();
        var used = new List<string>();
        void BotTry(Player bot, ThrowableKind kind, Player? target, bool visible, float dist, Action? setup = null)
        {
            bot.Equipment = kind;
            bot.EquipmentLeft = Throwables.Get(kind).PerLife;
            bot.NextThrow = 0;
            var uses0 = game.BotTacticalUses[(int)kind];
            var ticks = 0;
            for (; ticks < 6000 && game.BotTacticalUses[(int)kind] == uses0; ticks++)
            {
                setup?.Invoke();
                game.ServerTick++;
                game.SelfTestBotTactical(bot, target, visible, dist, now);
            }
            used.Add(game.BotTacticalUses[(int)kind] > uses0 ? $"{Throwables.Get(kind).Name} after {ticks} ticks" : $"{Throwables.Get(kind).Name} NEVER");
        }
        game.SelfTestPlace(b1, new Vector3(0, 0, 0), 0);
        game.SelfTestPlace(b2, new Vector3(0, 0, -10), 0);
        b1.Health = 30;
        BotTry(b1, ThrowableKind.Stim, null, false, 0);
        var stimmed = b1.Health;
        BotTry(b1, ThrowableKind.Flash, b2, true, 10);
        var flashProj = game.Projectiles.Values.FirstOrDefault(p => p.Kind == ThrowableKind.Flash && p.Owner == b1.Id);
        var towards = flashProj != null && Vector3.Dot(Vector3.Normalize(flashProj.Vel with { Y = 0 }), -Vector3.UnitZ) > 0.9f;
        game.Projectiles.Clear();
        BotTry(b1, ThrowableKind.Trophy, b2, false, 10, () => game.Projectiles[700] = new Projectile { Id = 700, Kind = ThrowableKind.Frag, Owner = b2.Id, Pos = b1.Position + new Vector3(4, 1, 0), Born = now });
        game.Projectiles.Clear();
        BotTry(b1, ThrowableKind.Cover, b2, true, 20);
        var hook = false;
        b1.Equipment = ThrowableKind.Insertion;
        b1.EquipmentLeft = 1;
        b1.NextThrow = 0;
        var ready = game.BotReadyTactical(b1, now);
        hook = game.BotUseTacticalNow(b1, b1.Position, now);
        var hookAgain = game.BotUseTacticalNow(b1, b1.Position, now + 5);
        Note($"bots: {string.Join(", ", used)}; stim healed 30 -> {stimmed}; the flash flew at its target {towards}; "
             + $"devices down {string.Join(", ", game.TacDevices.Select(d => Throwables.Get(d.Kind).Name))}; hook: ready {ready}, used {hook}, again with none left {hookAgain}");
        Check(used.All(u => !u.EndsWith("NEVER")) && stimmed == 30 + TacticalGear.StimHeal && towards && ready == ThrowableKind.Insertion && hook && !hookAgain,
            "bots: didn't use their tactical items as they should (or the hook is wrong)");

        // ---- Replication: devices and events on a client ----
        Next();
        game.SelfTestPlace(me, new Vector3(0, 0, -20), 0.7f);
        game.SelfTestUseTactical(me, ThrowableKind.Trophy, World.Forward(0.7f, 0), now);
        game.SelfTestUseTactical(me, ThrowableKind.Cover, World.Forward(0.7f, 0), now);
        game.SelfTestTacticalDetonate(me, ThrowableKind.MotionSensor, new Vector3(3, 0.05f, -24), now);
        if (game.TacDevices.FirstOrDefault(d => d.Kind == ThrowableKind.Cover) is { } dmg)
            game.SelfTestShotTactical(dmg, b1, 70, now);
        var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using (client)
        {
            client.LocalId = 3;
            client.Players[3] = new Player { Id = 3, Name = "Guest", Alive = true };
            var w = new PacketWriter();
            Game.WriteTacState(w, 50, game.TacDevices);
            var size = w.Span.Length;
            client.SelfTestPacket(default(RakNetGUID), w.Span, 1);
            var same = client.TacDevices.Count == game.TacDevices.Count && game.TacDevices.All(s => client.FindTacDevice(s.Id) is { } c && c.Kind == s.Kind && c.Owner == s.Owner
                && Vector3.Distance(c.Pos, s.Pos) < 0.01f && MathF.Abs(World.WrapAngle(c.Yaw - s.Yaw)) < 0.001f && Math.Abs(c.Health - s.Health) <= Math.Max(1, s.MaxHealth / 255 + 1) && c.Charges == s.Charges);
            Game.WriteTacEvent(w, TacEventKind.Afflict, 3, (byte)ThrowableKind.Flash, (ushort)(0.8f * 65535), new Vector3(1, 1, 1));
            client.SelfTestPacket(default(RakNetGUID), w.Span, 1.1);
            var flashed = client.LocalFlashAlpha(1.2);
            Game.WriteTacEvent(w, TacEventKind.Afflict, 3, (byte)ThrowableKind.Stun, 65535, new Vector3(1, 1, 1));
            client.SelfTestPacket(default(RakNetGUID), w.Span, 1.2);
            client.SelfTestUpdateTacticalEffects(1.4, 0.1f); // (well within a second of the last device update)
            var look = client.TacticalLookMult;
            client.Projectiles[900] = new Projectile { Id = 900, Kind = ThrowableKind.Frag, Owner = 1, Pos = Vector3.Zero };
            var trophyId = game.TacDevices.First(d => d.Kind == ThrowableKind.Trophy).Id;
            Game.WriteTacEvent(w, TacEventKind.Intercept, trophyId, (byte)ThrowableKind.Frag, 900, Vector3.Zero);
            client.SelfTestPacket(default(RakNetGUID), w.Span, 1.5);
            var shotDown = !client.Projectiles.ContainsKey(900);
            Game.WriteTacEvent(w, TacEventKind.Destroyed, trophyId, (byte)ThrowableKind.Trophy, (ushort)(me.Id | b1.Id << 8), Vector3.Zero);
            client.SelfTestPacket(default(RakNetGUID), w.Span, 1.6);
            var removed = client.FindTacDevice(trophyId) == null;
            Game.WriteTacState(w, 49, []);
            client.SelfTestPacket(default(RakNetGUID), w.Span, 1.7); // older: ignored
            var keptOld = client.TacDevices.Count;
            Game.WriteTacState(w, 60, []);
            client.SelfTestPacket(default(RakNetGUID), w.Span, 1.8);
            Note($"client: {game.TacDevices.Count} devices in {size} bytes match the server {same}; flash event -> overlay {flashed:0.00}; stun event -> turning x{look:0.00}; "
                 + $"intercept removes the grenade {shotDown}; destroyed removes the device {removed}; an older update ignored {keptOld == game.TacDevices.Count - 1}; empty update clears {client.TacDevices.Count == 0}");
            Check(same && size == 1 + 5 + game.TacDevices.Count * TacDevice.NetBytes && flashed > 0.9f && look < 0.5f && shotDown && removed
                  && keptOld == game.TacDevices.Count - 1 && client.TacDevices.Count == 0, "client: tactical replication is wrong");
        }

        // ---- No allocations per tick with devices out ----
        Next();
        game.SelfTestPlace(me, new Vector3(0, 0, -20), 0);
        game.SelfTestUseTactical(me, ThrowableKind.Trophy, -Vector3.UnitZ, now);
        game.SelfTestUseTactical(me, ThrowableKind.Cover, Vector3.UnitX, now);
        game.SelfTestUseTactical(me, ThrowableKind.Insertion, -Vector3.UnitZ, now);
        game.SelfTestTacticalDetonate(me, ThrowableKind.MotionSensor, new Vector3(3, 0.05f, -24), now);
        foreach (var p in game.Players.Values)
            p.Sim.Velocity = Vector3.Zero;
        Tick(1, effects: true); // warm up
        var before = GC.GetAllocatedBytesForCurrentThread();
        Tick(10, effects: true);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (log) // (not in the compared text: the first run also pays for JIT)
            Line($"allocations: {bytes} bytes over 10 s of ticks with {game.TacDevices.Count} devices out");
        Check(bytes < 1024 && game.TacDevices.Count == 4, "tactical gear: the server tick or the client effects allocate");

        // Match end clears everything.
        game.SelfTestEndMatchTactical(now);
        Check(game.TacDevices.Count == 0, "tactical gear: devices outlived the match");
        return sb.ToString();
    }

    /// <summary>Team modes: teammates are spared by flashes and stuns, and see each other's devices as friendly.</summary>
    private static void TacticalTeams(Map map)
    {
        using var game = ModeGame(3, GameMode.TeamDeathmatch);
        World.SetMap(map);
        var me = game.Players[0];
        var mate = game.Players.Values.First(p => p != me && p.Team == me.Team);
        var foe = game.Players.Values.First(p => p.Team != me.Team);
        foreach (var p in game.Players.Values)
        {
            game.SelfTestPerks(p, PerkSet.Empty);
            game.SelfTestPlace(p, new Vector3(-36 + p.Id * 1.5f, 0, -36), 0);
        }
        game.SelfTestPlace(mate, new Vector3(-2, 0, -14), 0);
        game.SelfTestPlace(foe, new Vector3(2, 0, -14), 0);
        game.SelfTestTacticalDetonate(me, ThrowableKind.Flash, new Vector3(0, 1.35f, -19), 100);
        game.SelfTestTacticalDetonate(me, ThrowableKind.Stun, new Vector3(0, 0.1f, -15), 100);
        var flak = PerkSet.Empty.With(PerkCategory.Survival, PerkId.FlakJacket);
        float fm = game.SelfTestFlashOf(mate).Strength, ff = game.SelfTestFlashOf(foe).Strength, sm = game.SelfTestStunOf(mate).Strength, sf = game.SelfTestStunOf(foe).Strength;
        game.SelfTestPerks(foe, flak);
        game.SelfTestTacticalDetonate(me, ThrowableKind.Flash, new Vector3(0, 1.35f, -19), 110);
        var ffFlak = game.SelfTestFlashOf(foe).Strength;
        Line($"team deathmatch: flash on a teammate {fm:0.00}, an enemy {ff:0.00} (with Flak Jacket {ffFlak:0.00}); stun on a teammate {sm:0.00}, an enemy {sf:0.00}");
        if (fm != 0 || sm != 0 || ff <= 0.3f || sf <= 0.3f || MathF.Abs(ffFlak - ff * TacticalGear.FlakTacticalMult) > 0.02f)
            Fail("team deathmatch: flashes and stuns must spare teammates, and Flak Jacket halve them");
    }

    /// <summary>Modes without equipment (Gun Game): nobody spawns with a tactical item and bots don't use one.</summary>
    private static void TacticalNoEquipmentModes()
    {
        using var game = ModeGame(1, GameMode.GunGame);
        var me = game.Players[0];
        var bot = game.Players.Values.First(p => p.Bot != null);
        me.WantEquipment = bot.WantEquipment = ThrowableKind.Flash;
        game.SelfTestSpawn(me);
        game.SelfTestSpawn(bot);
        var botUses = game.BotTacticalUses.Sum();
        for (var i = 0; i < 2000; i++)
        {
            game.ServerTick++;
            game.SelfTestBotTactical(bot, me, true, 10, 100);
        }
        var hook = game.BotUseTacticalNow(bot, me.Position, 100);
        Line($"Gun Game: spawned with {me.Equipment} x{me.EquipmentLeft} (bot {bot.Equipment} x{bot.EquipmentLeft}); bot uses {game.BotTacticalUses.Sum() - botUses}, hook {hook}");
        if (me.EquipmentLeft != 0 || bot.EquipmentLeft != 0 || game.BotTacticalUses.Sum() != botUses || hook)
            Fail("Gun Game: tactical equipment showed up in a mode without equipment");
    }
}
