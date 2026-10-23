using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Launchers" (Source/Game/Launchers): every launcher loads as a secondary; flight is deterministic
/// (the server's rocket and a client's copy from the spawn message fly the same path); blasts fall off with distance
/// and walls shield you (a thermobaric round's share gets round); arming (an unarmed hit is a dud); lock-on locks an
/// airborne streak and kills the drone in one hit, a "gunship-class" profile in three; secondary-only and loadout
/// save/load; packet round trips; no allocations with 20 rockets in flight; a bot fires at a drone; sticky charges and
/// cluster bomblets; attachments; the launch sounds are distinct. Short scenarios only (no bot matches).
/// </summary>
public static partial class SelfTest
{
    private static WeaponDef? LauncherNamed(string name) => Weapons.All.FirstOrDefault(w => w.IsLauncher && w.Name == name);

    private static void CheckLaunchers()
    {
        LauncherNotes.Clear();
        try
        {
            CheckLaunchersBody();
        }
        finally
        {
            // One annotation on the CI run with the scenario results.
            Line("::notice title=Launchers::" + string.Join("%0A", LauncherNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static readonly List<string> LauncherNotes = new();

    private static void LLine(string s)
    {
        Line(s);
        LauncherNotes.Add(s);
    }

    private static void CheckLaunchersBody()
    {
        // ---- Every launcher loads, secondary only ----------------------------------------------------
        var launchers = Weapons.All.Where(w => w.IsLauncher).ToArray();
        LLine($"launchers ({launchers.Length}): " + string.Join("; ", launchers.Select(w =>
            $"{w.Name} {w.LauncherProjectile}/{w.LauncherGuidance} {w.Damage} dmg r{w.LauncherRadius} (players x{w.LauncherPlayerMult} r{w.PlayerBlastRadius}) +{w.LauncherDirect} direct, {w.Mag}+{w.LauncherReserve} rounds, lvl {Progress.UnlockLevel(w)}")));
        if (launchers.Length is < 6 or > 8)
            Fail($"{launchers.Length} launchers (want 6-8)");
        foreach (var w in launchers)
        {
            if (w.Slot != Slot.Secondary)
                Fail($"{w.Name}: a launcher in the {w.Slot} slot");
            if (w.SourceFile == null || !w.SourceFile.Replace('\\', '/').StartsWith("Launchers/", StringComparison.Ordinal))
                Fail($"{w.Name}: not loaded from Weapons/Launchers ({w.SourceFile})");
            if (w.Model == null || GunModel.Mesh(w) == null)
                Fail($"{w.Name}: no model");
            if (w.Sound is not (SoundStyle.Rocket or SoundStyle.Thump))
                Fail($"{w.Name}: sound {w.Sound} (launchers use Rocket or Thump)");
            if (w.LauncherReserve is < 1 or > 8)
                Fail($"{w.Name}: {w.LauncherReserve} reserve rounds (launchers carry a few, 1-8)");
        }
        foreach (var kind in new[] { (LauncherProjectile.Rocket, LauncherGuidance.None), (LauncherProjectile.Rocket, LauncherGuidance.LockOn), (LauncherProjectile.Rocket, LauncherGuidance.Wire),
                     (LauncherProjectile.Grenade, LauncherGuidance.None), (LauncherProjectile.Sticky, LauncherGuidance.None) })
            if (!launchers.Any(w => w.LauncherProjectile == kind.Item1 && w.LauncherGuidance == kind.Item2))
                Fail($"no {kind.Item1} launcher with {kind.Item2} guidance");
        if (!launchers.Any(w => w.LauncherCluster > 0) || !launchers.Any(w => w.LauncherWallPierce > 0) || !launchers.Any(w => w.Mag >= 6))
            Fail("missing the cluster, thermobaric or drum launcher");
        if (Weapons.LegacyNames.Length != 50 || launchers.Any(w => Array.IndexOf(Weapons.LegacyNames, w.Name) >= 0 || w.Id < 50))
            Fail("launchers must come after the 50 legacy weapons and never touch LegacyNames");
        if (GunGame.Ladder.Any(w => w.IsLauncher))
            Fail("a launcher on the Gun Game ladder");
        try
        {
            WeaponFile.Load(WeaponFile.ToJson(Weapons.DefaultPrimary).Replace("\"model\":", "\"launcher\": { \"speed\": 20 },\n  \"model\":"), "not a launcher");
            Fail("a non-launcher with a launcher block loaded");
        }
        catch (WeaponFileException)
        {
        }
        if (WeaponFile.ToJson(Weapons.DefaultPrimary).Contains("\"launcher\"", StringComparison.Ordinal))
            Fail("a non-launcher's file text got a launcher block (it would change every weapon's fingerprint)");
        var mule = LauncherNamed("Mule RPG");
        var sky = LauncherNamed("Skylance AA");
        var furnace = LauncherNamed("Furnace Thermobaric");
        var sticky = launchers.FirstOrDefault(w => w.LauncherProjectile == LauncherProjectile.Sticky);
        var cluster = launchers.FirstOrDefault(w => w.LauncherCluster > 0);
        if (mule == null || sky == null || furnace == null || sticky == null || cluster == null)
        {
            Fail("the Mule RPG, Skylance AA, Furnace Thermobaric, a sticky and a cluster launcher are needed for the scenarios");
            return;
        }

        // ---- Attachments: only the launcher ones fit, and they work ----------------------------------
        var all = Attachments.BySlot.SelectMany(x => x).ToArray();
        var forLaunchers = all.Where(a => a.Rule?.Classes.Contains(WeaponClass.Launcher) == true).ToArray();
        foreach (var w in launchers)
            foreach (var a in Attachments.For(w))
                if (!forLaunchers.Contains(a))
                    Fail($"{a.Name} fits the {w.Name} (only launcher attachments should)");
        foreach (var a in forLaunchers)
            if (Weapons.All.Any(w => !w.IsLauncher && a.Fits(w)))
                Fail($"{a.Name} fits a weapon that isn't a launcher");
        LLine($"launcher attachments ({forLaunchers.Length}): {string.Join(", ", forLaunchers.Select(a => a.Name))}; the Skylance takes {Attachments.For(sky).Count}");
        if (forLaunchers.Length < 3)
            Fail($"{forLaunchers.Length} launcher attachments (want 3-5)");
        if (Attachments.Find("Quick-Lock Module") is { } ql && Attachments.Find("Spare Round") is { } sr)
        {
            var kitted = Attachments.Apply(sky, Attachments.With(Attachments.With(0, ql.Slot, ql.Index), sr.Slot, sr.Index));
            if (MathF.Abs(kitted.LauncherLockTime - sky.LauncherLockTime * 0.7f) > 1e-3f || kitted.LauncherReserve != sky.LauncherReserve + 1)
                Fail($"Quick-Lock + Spare Round: lock {kitted.LauncherLockTime:0.00} s (want {sky.LauncherLockTime * 0.7f:0.00}), reserve {kitted.LauncherReserve}");
        }

        // ---- Packets ----------------------------------------------------------------------------------
        var w8 = new PacketWriter();
        var probe = new Rocket { Id = 4321, Owner = 7, WeaponId = sky.Id, Kit = 0x12345, Target = 9, Wire = true, Seed = 0xDEADBEEF, Pos = new(1.5f, 2, -3), Vel = new(10, 20, -30) };
        Game.WriteRocketSpawn(w8, probe, 5);
        var spawnLen = w8.Length;
        var rd = new PacketReader(w8.Span[1..]);
        var back = new Rocket();
        var age = Game.ReadRocketSpawn(ref rd, back);
        if (spawnLen != 1 + Game.RocketSpawnBytes || age != 5 || back.Id != probe.Id || back.Owner != 7 || back.WeaponId != sky.Id || back.Kit != 0x12345 || back.Target != 9
            || !back.Wire || back.Seed != probe.Seed || back.Pos != probe.Pos || back.Vel != probe.Vel || !back.Def.IsLauncher)
            Fail($"rocket spawn round trip ({spawnLen} bytes)");
        Game.WriteRocketBoom(w8, probe, RocketEnd.Dud, new Vector3(4, 5, 6), 21);
        var boomLen = w8.Length;
        rd = new PacketReader(w8.Span[1..]);
        var boom = Game.ReadRocketBoom(ref rd);
        if (boomLen != 1 + Game.RocketBoomBytes || boom.Id != probe.Id || boom.End != RocketEnd.Dud || boom.Pos != new Vector3(4, 5, 6) || boom.Flags != 21 || boom.Seed != probe.Seed || boom.Kit != 0x12345)
            Fail($"rocket boom round trip ({boomLen} bytes)");
        w8.Begin(Msg.RocketSync);
        Game.WriteRocketSyncEntry(w8, probe);
        if (w8.Length != 1 + Game.RocketSyncBytes)
            Fail($"rocket sync entry is {w8.Length - 1} bytes (want {Game.RocketSyncBytes})");
        int protocol = ServerInfo.ProtocolVersion;
        if (protocol < 28 || (byte)Msg.RocketDetonate != (byte)Msg.RocketSpawn + 3 || Enum.GetValues<Msg>().Max() < Msg.RocketDetonate)
            Fail("launcher messages: protocol not bumped or the messages aren't together");
        LLine($"packets: spawn {spawnLen} B, sync {Game.RocketSyncBytes} B per guided rocket, boom {boomLen} B; protocol {protocol}");

        // ---- Loadouts: secondary only, saved by name -----------------------------------------------------
        var saveBackup = LoadoutStore.SaveLines().ToArray();
        try
        {
            LoadoutStore.LoadLines(["loadout1.primary=Mule RPG", "loadout1.secondary=Skylance AA"]);
            var l = LoadoutStore.Slots[0];
            if (l.Primary == mule.Id || l.Secondary != sky.Id)
                Fail($"loadout file: a launcher as primary must be refused and as secondary kept (primary {Weapons.Get(l.Primary).Name}, secondary {Weapons.Get(l.Secondary).Name})");
            var lines = LoadoutStore.SaveLines().ToArray();
            LoadoutStore.LoadLines(lines);
            if (LoadoutStore.Slots[0].Secondary != sky.Id || !lines.Contains("loadout1.secondary=Skylance AA"))
                Fail("a launcher secondary didn't survive a loadout save and load");
            var maxed = new Progress { ReadOnly = true, Xp = Progress.TotalFor(Progress.MaxLevel) };
            var fresh = new Progress { ReadOnly = true };
            if (LoadoutStore.Effective(LoadoutStore.Slots[0], maxed).Secondary != sky.Id || LoadoutStore.Effective(LoadoutStore.Slots[0], fresh).Secondary == sky.Id)
                Fail("a locked launcher should fall back to an unlocked secondary (and an unlocked one stay)");
        }
        finally
        {
            LoadoutStore.LoadLines(saveBackup);
        }

        // ---- A test range: flat ground, one wall -------------------------------------------------------
        var map = MapFile.Load("""
            { "name": "Launcher test", "halfX": 80, "halfZ": 80, "ops": [
              { "op": "Ground", "color": [0.4, 0.4, 0.4] },
              { "op": "Block", "x0": -4, "y0": 0, "z0": 1.5, "x1": 4, "y1": 4.0, "z1": 2.5, "color": "#808080" },
              { "op": "Block", "x0": -10, "y0": 0, "z0": -40.5, "x1": 10, "y1": 6.0, "z1": -39.5, "color": "#808080" },
              { "op": "Spawn", "x": 60, "z": 60 } ] }
            """, "launchertest.json");
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2);
        game.SelfTestStreakTick(0); // match reset
        World.SetMap(map); // (a server game loads its own map: the test range goes in after)
        var me = game.Players[0];
        var bots = game.Players.Values.Where(p => p.Bot != null).ToArray();
        Player b1 = bots[0], b2 = bots[1];
        foreach (var p in game.Players.Values)
            game.SelfTestPerks(p, PerkSet.Empty);
        var now = 10.0;
        void Ready(Player p, WeaponDef w)
        {
            p.Secondary = w.Id;
            p.SecondaryKit = 0;
            game.SelfTestRefillLauncher(p);
        }
        int Run(double seconds)
        {
            var ticks = 0;
            for (var t = 0.0; t < seconds && game.Rockets.Count > 0; t += World.TickDt, ticks++)
            {
                now += World.TickDt;
                game.SelfTestRocketTick(now);
            }
            return ticks;
        }

        // ---- Deterministic flight: the server's rocket and a client's copy from its spawn message ----
        game.SelfTestPlace(me, new Vector3(0, 0, 30), 0);
        game.SelfTestPlace(b1, new Vector3(70, 0, 70), 0);
        game.SelfTestPlace(b2, new Vector3(-70, 0, 70), 0);
        Ready(me, mule);
        var aimUp = Vector3.Normalize(new Vector3(0.1f, 0.25f, -1f));
        var rocket = game.SelfTestFireLauncher(me, mule, aimUp, byte.MaxValue, now);
        if (rocket == null)
        {
            Fail("the Mule RPG didn't fire");
            return;
        }
        Game.WriteRocketSpawn(w8, rocket, 0);
        rd = new PacketReader(w8.Span[1..]);
        var copy = new Rocket();
        Game.ReadRocketSpawn(ref rd, copy);
        var worst = 0f;
        var flown = 0;
        for (var i = 0; i < 120 && game.Rockets.ContainsKey(rocket.Id); i++, flown++)
        {
            now += World.TickDt;
            game.SelfTestRocketTick(now);
            game.SelfTestClientStep(copy);
            if (game.Rockets.ContainsKey(rocket.Id))
                worst = MathF.Max(worst, Vector3.Distance(rocket.Pos, copy.Pos));
        }
        LLine($"flight: {flown} ticks, the client's copy is at most {worst * 1000:0.000} mm off the server's rocket");
        if (flown < 60 || worst > 1e-4f)
            Fail($"rocket flight isn't deterministic ({flown} ticks, {worst} m apart)");
        Run(10);

        // ---- Splash: falloff, walls, thermobaric, self damage, arming -------------------------------------
        var falloff = new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f }.Select(d => RocketFlight.PlayerBlastDamage(mule, d)).ToArray();
        LLine($"Mule RPG blast on players by distance 0-8 m: {string.Join(", ", falloff.Select(f => f.ToString("0")))} (player radius {mule.PlayerBlastRadius} m, x{mule.LauncherPlayerMult}; "
             + $"objects {mule.Damage} over {mule.LauncherRadius} m)");
        for (var i = 1; i < falloff.Length; i++)
            if (falloff[i] > falloff[i - 1] || MathF.Abs(falloff[0] - mule.Damage * mule.LauncherPlayerMult) > 0.5f || RocketFlight.PlayerBlastDamage(mule, mule.PlayerBlastRadius + 0.1f) != 0
                || Game.LauncherBlastAt(mule, new Vector3(0, 1, 20), new Vector3(mule.LauncherRadius + 0.1f, 1, 20), mule.LauncherRadius) != 0
                || MathF.Abs(Game.LauncherBlastAt(mule, new Vector3(0, 1, 20), new Vector3(0, 1, 20), mule.LauncherRadius) - mule.Damage) > 0.5f)
                Fail("launcher blast damage must fall off with distance from the full damage at the centre to nothing at the radius (players: x playerMult over the player radius)");

        // Every launcher's blast is deadlier against players than before the buff (the old numbers: "damage" over
        // "radius", no player multiplier), while what objects and machines take stays on "damage" and "radius".
        var lethalRows = new List<string>();
        foreach (var w in launchers)
        {
            var oldLethal = w.Damage < World.MaxHealth ? 0f : w.LauncherRadius * (1f - MathF.Pow(World.MaxHealth / w.Damage, 1f / w.LauncherFalloff));
            var lethal = RocketFlight.PlayerLethalRadius(w, World.MaxHealth);
            var selfAtArm = RocketFlight.PlayerBlastDamage(w, w.LauncherArmDistance) * w.LauncherSelfDamage;
            lethalRows.Add($"{w.Name} {w.Damage * w.LauncherPlayerMult:0}/{w.PlayerBlastRadius} m kills within {lethal:0.00} m (was {oldLethal:0.00}), own blast at {w.LauncherArmDistance} m {selfAtArm:0}");
            if (w.LauncherPlayerMult is < 1.2f or > 1.5f || w.PlayerBlastRadius < w.LauncherRadius * 1.2f || w.PlayerBlastRadius > w.LauncherRadius * 1.45f)
                Fail($"{w.Name}: the player buff should be about +30-40% damage and radius (x{w.LauncherPlayerMult}, {w.PlayerBlastRadius} m over {w.LauncherRadius} m)");
            if (lethal <= oldLethal || w.Damage * w.LauncherPlayerMult < World.MaxHealth)
                Fail($"{w.Name}: the blast's lethal radius against a {World.MaxHealth} HP player must grow ({lethal:0.00} m, was {oldLethal:0.00}) and its centre must kill");
            if (selfAtArm >= World.MaxHealth / 2f)
                Fail($"{w.Name}: an armed round going off at its arming distance ({w.LauncherArmDistance} m) does {selfAtArm:0} to its owner (want under half their health)");
        }
        LLine($"lethal radius vs {World.MaxHealth} HP (blast alone, in the open): {string.Join("; ", lethalRows)}");
        var singles = launchers.Where(w => w.LauncherCluster == 0).ToArray();
        var drum = launchers.First(w => w.Mag >= 6);
        if (launchers.Any(w => w != furnace && w.PlayerBlastRadius >= furnace.PlayerBlastRadius) || launchers.Any(w => w != sky && w.PlayerBlastRadius <= sky.PlayerBlastRadius)
            || singles.Any(w => w != drum && w.Damage * w.LauncherPlayerMult <= drum.Damage * drum.LauncherPlayerMult))
            Fail("launcher identities: the thermobaric blast must stay the widest, the Skylance's the smallest, the drum's the lightest single blast");
        game.SelfTestPlace(me, new Vector3(-30, 0, -20), 0);
        game.SelfTestPlace(b1, new Vector3(3, 0, 0), 0); // in the open, 3 m away
        game.SelfTestPlace(b2, new Vector3(0, 0, 3), 0); // 3 m away behind the wall
        game.SelfTestLauncherBlast(me, mule, new Vector3(0, 0.1f, 0), now);
        int open = World.MaxHealth - b1.Health, walled = World.MaxHealth - b2.Health;
        game.SelfTestPlace(b1, new Vector3(3, 0, 0), 0);
        game.SelfTestPlace(b2, new Vector3(0, 0, 3), 0);
        game.SelfTestLauncherBlast(me, furnace, new Vector3(0, 0.1f, 0), now);
        int thermoOpen = World.MaxHealth - (b1.Alive ? b1.Health : 0), thermoWall = World.MaxHealth - (b2.Alive ? b2.Health : 0);
        game.SelfTestPlace(me, new Vector3(2, 0, -1), 0);
        game.SelfTestLauncherBlast(me, mule, new Vector3(0, 0.1f, -1), now);
        var self = World.MaxHealth - (me.Alive ? me.Health : 0);
        LLine($"blast at 3 m: Mule open {open}, behind a wall {walled}; Furnace (thermobaric) open {thermoOpen}, behind the wall {thermoWall}; your own Mule at 2 m: {self}");
        if (open <= 0 || walled != 0)
            Fail("a launcher blast must hurt in the open and never through a wall");
        if (thermoWall <= 0 || thermoWall >= thermoOpen)
            Fail("a thermobaric blast should get part of its damage round a wall (less than in the open)");
        if (self <= 0)
            Fail("your own launcher blast must hurt you");

        // A near miss now kills: 2.6 m to the side of a Mule RPG blast (2.8 m to the chest) used to leave a 120 HP
        // player on about 40 health.
        game.SelfTestPlace(me, new Vector3(-30, 0, -20), 0);
        game.SelfTestPlace(b1, new Vector3(-20 + 2.6f, 0, -10), 0);
        var nearDist = Vector3.Distance(new Vector3(-20, 0.25f, -10), b1.Chest);
        game.SelfTestLauncherBlast(me, mule, new Vector3(-20, 0.1f, -10), now);
        var oldNear = mule.Damage * MathF.Pow(1f - nearDist / mule.LauncherRadius, mule.LauncherFalloff);
        LLine($"Mule RPG near miss ({nearDist:0.0} m to the chest): the target {(b1.Alive ? $"took {World.MaxHealth - b1.Health}" : "died")} (before the buff: {oldNear:0})");
        if (b1.Alive)
            Fail("a Mule RPG blast 2.8 m from a player should kill them now");

        // Arming: a hit before the arming distance is a dud (the direct hit's damage only, no blast).
        game.SelfTestPlace(me, new Vector3(-20, 0, 30), MathF.PI / 2); // facing +X
        game.SelfTestPlace(b1, new Vector3(-17, 0, 30), 0); // 3 m ahead: inside the 8 m arming distance
        game.SelfTestPlace(b2, new Vector3(60, 0, -60), 0);
        Ready(me, mule);
        var toB1 = Vector3.Normalize(b1.Chest - me.Eye);
        game.SelfTestFireLauncher(me, mule, toB1, byte.MaxValue, now += 10);
        Run(3);
        var dud = World.MaxHealth - b1.Health;
        game.SelfTestPlace(b1, new Vector3(0, 0, 30), 0); // 20 m: armed
        toB1 = Vector3.Normalize(b1.Chest - me.Eye);
        game.SelfTestFireLauncher(me, mule, toB1, byte.MaxValue, now += 10);
        Run(3);
        LLine($"arming ({mule.LauncherArmDistance} m): a direct hit at 3 m did {dud} (a dud: direct {mule.LauncherDirect} only); at 20 m the target {(b1.Alive ? $"took {World.MaxHealth - b1.Health}" : "died")}");
        if (dud != (int)MathF.Round(mule.LauncherDirect) || b1.Alive)
            Fail("arming: an unarmed hit should do only its direct damage, an armed one should kill");

        // ---- Lock-on: an airborne streak locks; the drone dies to one lock-on hit -----------------------------
        game.SelfTestPlace(me, new Vector3(0, 0, 40), 0); // facing -Z
        game.SelfTestPlace(b1, new Vector3(60, 0, 60), 0);
        Ready(me, sky);
        var drone = game.SelfTestSpawnEntity(b1, StreakId.AttackDrone, new Vector3(0, 18, 5), now);
        var turret = game.SelfTestSpawnEntity(b1, StreakId.SentryTurret, new Vector3(6, 0, 20), now);
        game.SyncLocalSlots(me, refill: true);
        game.ActiveSlot = 1;
        game.LookYaw = MathF.Atan2(drone.Pos.X - me.Eye.X, -(drone.Pos.Z - me.Eye.Z));
        var toDrone = drone.Hitbox.Center - me.Eye;
        game.LookPitch = MathF.Asin(toDrone.Y / toDrone.Length());
        game.AimHeld = true;
        game.AimT = 1;
        game.SelfTestBuildLock(now, sky.LauncherLockTime * 0.5, 1f / 60);
        var halfway = game.Locked;
        game.SelfTestBuildLock(now + sky.LauncherLockTime * 0.5, sky.LauncherLockTime * 0.7, 1f / 60);
        var locked = game.Locked && game.LockTarget == drone.Id;
        var lockedOn = game.LockTarget;
        // The sentry on the ground never locks.
        game.LookYaw = MathF.Atan2(turret.Pos.X - me.Eye.X, -(turret.Pos.Z - me.Eye.Z));
        game.LookPitch = -0.05f;
        game.SelfTestBuildLock(now + 3, sky.LauncherLockTime * 2, 1f / 60);
        var turretLocked = game.Locked;
        LLine($"lock-on: half the lock time {halfway}, after the lock time {locked} (target {lockedOn}, the drone is {drone.Id}); a ground sentry locks: {turretLocked}");
        if (halfway || !locked || turretLocked)
            Fail("lock-on: should lock an airborne streak after its lock time (not before), and never a ground sentry");
        var hits = 0;
        for (var i = 0; i < 3 && game.StreakEntities.ContainsKey(drone.Id); i++)
        {
            game.SelfTestRefillLauncher(me);
            game.SelfTestFireLauncher(me, sky, Vector3.Normalize(new Vector3(0.3f, 0.6f, -1f)), drone.Id, now += 10); // fired off to the side: it homes
            hits++;
            Run(6);
        }
        LLine($"Skylance AA: the drone ({KillStreaks.Get(StreakId.AttackDrone).Health} HP) is down after {hits} lock-on rocket(s) (stated: {LauncherTargets.HitsToKill(sky, StreakId.AttackDrone, true)})");
        if (game.StreakEntities.ContainsKey(drone.Id) || hits != 1)
            Fail($"a lock-on rocket should kill the attack drone in one hit ({hits} fired, alive {game.StreakEntities.ContainsKey(drone.Id)})");

        // The Gunship (750 HP, explosives x1.5, one flare): locked Skylance rockets until it's down.
        var ship = game.SelfTestSpawnEntity(b1, StreakId.Gunship, new Vector3(-10, 26, 0), now);
        var shipHits = 0;
        while (game.StreakEntities.ContainsKey(ship.Id) && shipHits < 5)
        {
            game.SelfTestRefillLauncher(me);
            game.SelfTestFireLauncher(me, sky, Vector3.Normalize(ship.Hitbox.Center - me.Eye), ship.Id, now += 10);
            shipHits++;
            Run(8);
        }
        var shipStated = LauncherTargets.HitsToKill(sky, StreakId.Gunship, true);
        var muleOnTurret = LauncherTargets.HitsToKill(mule, StreakId.SentryTurret, false);
        LLine($"Gunship ({KillStreaks.Get(StreakId.Gunship).Health} HP): down after {shipHits} locked Skylance rockets ({shipStated} hits plus its flare); "
             + $"the sentry takes {muleOnTurret} Mule RPG direct hits, {LauncherTargets.HitsToKill(sky, StreakId.SentryTurret, false)} dumb-fire Skylance hits");
        if (game.StreakEntities.ContainsKey(ship.Id) || shipHits is < 2 or > 3 || shipStated is < 2 or > 3)
            Fail($"a gunship should need 2-3 lock-on rockets (took {shipHits}, stated {shipStated})");
        if (muleOnTurret is < 1 or > 3)
            Fail($"the sentry should take 1-3 rocket hits ({muleOnTurret})");
        if (game.StreakEntities.ContainsKey(turret.Id))
        {
            Ready(me, mule);
            var shots = 0;
            game.SelfTestPlace(me, new Vector3(6, 0, 40), 0);
            while (game.StreakEntities.ContainsKey(turret.Id) && shots < 4)
            {
                game.SelfTestRefillLauncher(me);
                game.SelfTestFireLauncher(me, mule, Vector3.Normalize(turret.Hitbox.Center - me.Eye), byte.MaxValue, now += 10);
                shots++;
                Run(4);
            }
            LLine($"Mule RPG dumb fire: the sentry is {(game.StreakEntities.ContainsKey(turret.Id) ? "still up" : "down")} after {shots} rocket(s)");
            if (game.StreakEntities.ContainsKey(turret.Id) || shots != muleOnTurret)
                Fail($"the sentry should go down to {muleOnTurret} dumb-fire rockets (took {shots})");
        }

        // ---- Ammo: limited rounds per life, reload checked ----------------------------------------------
        Ready(me, mule);
        var fired = 0;
        for (var i = 0; i < 8; i++)
            if (game.SelfTestFireLauncher(me, mule, new Vector3(0, 0.3f, -1), byte.MaxValue, now += 5) != null)
                fired++;
        var tooSoon = game.SelfTestFireLauncher(me, mule, new Vector3(0, 0.3f, -1), byte.MaxValue, now);
        game.SelfTestRefillLauncher(me);
        var first = game.SelfTestFireLauncher(me, mule, new Vector3(0, 0.3f, -1), byte.MaxValue, now += 5) != null;
        var noReload = game.SelfTestFireLauncher(me, mule, new Vector3(0, 0.3f, -1), byte.MaxValue, now += 0.5) != null;
        Run(10);
        LLine($"ammo: {fired} Mule RPG rounds in a life (mag {mule.Mag} + reserve {mule.LauncherReserve}); a second round 0.5 s later without a reload: {noReload}");
        if (fired != mule.Mag + mule.LauncherReserve || tooSoon != null || !first || noReload)
            Fail("launcher ammo: the server must allow exactly magazine + reserve per life and check the reload");

        // ---- Sticky charges and cluster bomblets -------------------------------------------------------------
        game.SelfTestPlace(me, new Vector3(0, 0, -30), 0); // the far wall is 9.5 m ahead
        Ready(me, sticky);
        game.SelfTestFireLauncher(me, sticky, new Vector3(0, 0.05f, -1), byte.MaxValue, now += 5);
        for (var i = 0; i < 90; i++)
            game.SelfTestRocketTick(now += World.TickDt);
        var stuck = game.Rockets.Values.Count(r => r.Owner == me.Id && r.Stuck);
        game.SelfTestDetonate(me, now += 0.1);
        LLine($"sticky: {stuck} charge stuck to the wall; after the detonation {game.Rockets.Count} projectile(s) left");
        if (stuck != 1 || game.Rockets.Count != 0)
            Fail("a sticky charge should stick to the wall and go off when its owner detonates it");
        game.SelfTestPlace(me, new Vector3(30, 0, 50), 0);
        Ready(me, cluster);
        game.SelfTestFireLauncher(me, cluster, Vector3.Normalize(new Vector3(0, -0.12f, -1)), byte.MaxValue, now += 5);
        Run(4);
        var pending = game.SelfTestPendingClusters;
        for (var i = 0; i < 90; i++)
            game.SelfTestRocketTick(now += World.TickDt);
        LLine($"cluster: {pending} bomblets scheduled ({cluster.LauncherCluster} expected), {game.SelfTestPendingClusters} left after 1.5 s");
        if (pending != cluster.LauncherCluster || game.SelfTestPendingClusters != 0)
            Fail("a cluster rocket should throw out its bomblets and they should all go off");

        // ---- No allocations with 20 rockets in flight (server tick + the per-frame visual update) ----------
        game.SelfTestPlace(me, new Vector3(0, 0, 0), 0);
        for (var i = 0; i < 20; i++)
            game.SelfTestAddRocket(me, mule, new Vector3(-40 + i * 4, 20, 60), new Vector3(0, 1.5f, -12), now);
        for (var i = 0; i < 30; i++)
        {
            game.SelfTestRocketTick(now += World.TickDt);
            game.SelfTestLauncherVisuals(now, World.TickDt);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 120; i++)
        {
            game.SelfTestRocketTick(now += World.TickDt);
            game.SelfTestLauncherVisuals(now, World.TickDt);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        LLine($"20 rockets in flight: {game.Rockets.Count} still flying after 150 ticks, {allocated} bytes allocated over 120 ticks + frames");
        if (game.Rockets.Count < 15)
            Fail($"the allocation check's rockets ended early ({game.Rockets.Count} left)");
        if (allocated > 0)
            Fail($"launcher flight allocates {allocated} bytes per 120 ticks with 20 rockets (want 0)");
        Run(20);

        // ---- A bot fires at a drone --------------------------------------------------------------------------
        game.SelfTestPlace(b1, new Vector3(0, 0, 40), 0);
        game.SelfTestPlace(me, new Vector3(60, 0, -60), 0);
        Ready(b1, sky);
        b1.Weapon = b1.Primary;
        var enemyDrone = game.SelfTestSpawnEntity(me, StreakId.AttackDrone, new Vector3(10, 18, 10), now);
        Rocket? botRocket = null;
        double busySince = -1;
        var botStart = now;
        for (var i = 0; i < 6 * World.TickRate && botRocket == null; i++)
        {
            now += World.TickDt;
            if (game.SelfTestBotLauncher(b1, now) && busySince < 0)
                busySince = now;
            botRocket = game.Rockets.Values.FirstOrDefault(r => r.Owner == b1.Id);
        }
        LLine($"bot vs drone: launcher out after {(busySince < 0 ? "never" : $"{busySince - botStart:0.0} s")}, rocket {(botRocket == null ? "none" : $"locked on {botRocket.Target}")}, holding {Weapons.Get(b1.Weapon).Name}");
        if (botRocket == null || botRocket.Target != enemyDrone.Id)
            Fail("a bot with a lock-on launcher should lock and fire at an enemy drone it can see");
        Run(6);
        if (b1.Weapon != b1.Primary)
            Fail("the bot should put the launcher away after its shot");

        // ---- Launch sounds: each launcher its own ----------------------------------------------------------
        var prints = launchers.Select(w => (W: w, F: GunSnap.Fingerprint([Synth.Gunshot(w)]))).ToArray();
        var closest = (D: float.MaxValue, A: "", B: "");
        for (var i = 0; i < prints.Length; i++)
            for (var j = i + 1; j < prints.Length; j++)
            {
                var d = GunSnap.Distance(prints[i].F, prints[j].F);
                if (d < closest.D)
                    closest = (d, prints[i].W.Name, prints[j].W.Name);
            }
        LLine($"launch sounds (synth, their own path): closest pair {closest.A} / {closest.B} {closest.D:0.00} dB apart");
        if (closest.D < 0.3f)
            Fail($"{closest.A} and {closest.B} sound alike ({closest.D:0.00} dB)");
    }
}
