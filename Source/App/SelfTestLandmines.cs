using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Landmines" (GameMines.cs, Props/Military/Landmine.json, Maps/Dev/Landmines.json), short scenarios
/// only: the prop file; stepping on a mine (click, the blast after its fuse, damage falling off with distance, a neutral
/// kill); stepping on and straight off (it still goes off: the click commits) and jumping over (nothing); a shot mine
/// (the shooter's kill); a mine setting off a barrel and a minefield rippling; a wall shielding; Flak Jacket; a bot
/// walking round a mine; a zombie setting one off; a mine that comes back ("respawn"); and the per-tick cost with 50 mines.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> MineNotes = new();

    /// <summary>Seeds the scenarios run with (each a fresh game: bot personalities and choices differ), so one CI run checks them several times.</summary>
    private static readonly int[] MineSeeds = [7101, 7202, 7303];

    private static bool _mineQuiet; // repeat runs: lines go to the log only, not the notice

    private static void MLine(string s)
    {
        Line(s);
        if (!_mineQuiet)
            MineNotes.Add(s);
    }

    private static void CheckLandmines()
    {
        MineNotes.Clear();
        try
        {
            LandmineFile();
            var failsBefore = _failures;
            var clean = 0;
            for (var k = 0; k < MineSeeds.Length; k++)
            {
                _mineQuiet = k > 0;
                var before = _failures;
                Line($"-- scenarios, seed {MineSeeds[k]} --");
                LandmineScenarios(MineSeeds[k]);
                clean += _failures == before ? 1 : 0;
            }
            _mineQuiet = false;
            MLine($"scenarios repeated with {MineSeeds.Length} seeds (fresh game each, different bot personalities): {clean} clean, {_failures - failsBefore} failure(s)");
            LandmineCost();
        }
        finally
        {
            World.SetMap(Maps.All[0]);
            Line("::notice title=Landmines::" + string.Join("%0A", MineNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static void LandmineFile()
    {
        if (Props.Find("Landmine") is not { Breakable: { } br } mine || Props.Find("Mine Warning Sign") is not { } sign)
        {
            Fail("Props/Military/Landmine.json or MineWarningSign.json didn't load (or the mine isn't breakable)");
            return;
        }
        var height = mine.Bounds.Max.Y;
        MLine($"Landmine: {mine.Category}, {(mine.IsSolid ? "solid" : "cosmetic")}, {height:0.00} m tall, {mine.Parts.Count} parts; plate {br.TriggerRadius} m x {br.TriggerHeight} m, "
              + $"fuse {br.ExplodeFuse} s, blast {br.ExplodeRadius} m / {br.ExplodeDamage} dmg / knockback {br.ExplodeKnockback} m/s, {br.Health} hp; sign: {sign.Category}, {sign.Parts.Count} parts");
        if (!br.IsMine || br.TriggerRadius is < 0.5f or > 0.7f || br.ExplodeFuse is < 0.2f or > 0.6f || br.ExplodeRadius <= 0 || br.Health > 20)
            Fail("the Landmine prop should have a ~0.6 m plate, a short fuse, a blast and low health");
        if (mine.IsSolid || height > 0.2f)
            Fail("the Landmine should be a low cosmetic prop (walked over, not into)");
        if (Props.All.Count(p => p.Breakable is { IsMine: true }) < 1 || Props.All.Any(p => p.Breakable is { IsMine: true } b && b.ExplodeFuse <= 0))
            Fail("a mine without a fuse");
        // The trigger block's errors, and a plate without a fuse getting the default one.
        (string Json, string Expect)[] cases =
        [
            ("{ \"name\": \"X\", \"parts\": [ { \"size\": [1, 1, 1] } ], \"breakable\": { \"explode\": { \"radius\": 3, \"damage\": 50, \"trigger\": { \"size\": 1 } } } }", "unknown key"),
            ("{ \"name\": \"X\", \"parts\": [ { \"size\": [1, 1, 1] } ], \"breakable\": { \"explode\": { \"radius\": 3, \"damage\": 50, \"trigger\": { \"radius\": 9 } } } }", "0.1"),
            ("{ \"name\": \"X\", \"parts\": [ { \"size\": [1, 1, 1] } ], \"breakable\": { \"explode\": { \"radius\": 3, \"damage\": 50, \"trigger\": 1 } } }", "an object"),
        ];
        var rejected = 0;
        foreach (var (json, expect) in cases)
        {
            try
            {
                PropFile.Load(json, "test.json");
                Fail($"a broken trigger block loaded: {json}");
            }
            catch (PropFileException ex) when (ex.Message.Contains(expect))
            {
                rejected++;
            }
            catch (Exception ex)
            {
                Fail($"a broken trigger block gave the wrong error (expected \"{expect}\"): {ex.Message}");
            }
        }
        var plain = PropFile.Load("{ \"name\": \"X\", \"parts\": [ { \"size\": [0.3, 0.1, 0.3] } ], \"breakable\": { \"explode\": { \"radius\": 3, \"damage\": 50, \"trigger\": {} } } }", "test.json");
        var respawnError = "";
        try
        {
            MapFile.Load(PropTestMap("{ \"op\": \"Prop\", \"name\": \"Dumpster\", \"x\": 0, \"z\": 0, \"respawn\": 5 }"), "test map");
        }
        catch (MapFileException ex)
        {
            respawnError = ex.Message;
        }
        MLine($"trigger block: {rejected}/{cases.Length} broken ones rejected; an empty one gives a {plain.Breakable?.TriggerRadius} m plate and a {plain.Breakable?.ExplodeFuse} s fuse; "
              + $"\"respawn\" on an unbreakable prop: {(respawnError.Contains("breakable") ? "rejected" : "NOT rejected")}");
        if (rejected != cases.Length || plain.Breakable is not { TriggerRadius: 0.6f, ExplodeFuse: PropFile.DefaultTriggerFuse })
            Fail("the trigger block isn't read as documented");
        if (!respawnError.Contains("breakable"))
            Fail("\"respawn\" on an unbreakable prop should be an error");
        if (Throwables.KillName(Game.LandmineKillId) != "Landmine" || KillInfo.NameFor(Game.LandmineKillId) != "Landmine" || Knockback.ForKill(Game.LandmineKillId).Strength <= 0
            || (Game.PropRestoreFlag & Game.PropLitFlag) != 0 || Game.PropRestoreFlag <= (byte)PropDamage.All || ServerInfo.ProtocolVersion < 31)
            Fail("landmine kill name, ragdoll push, restore flag or protocol version is wrong");
    }

    private static void LandmineScenarios(int seed)
    {
        if (Maps.Find("Landmines") is not { } map)
        {
            Fail("Maps/Dev/Landmines.json ('Landmines') didn't load");
            return;
        }
        map.RestoreProps(); // earlier sections (the bot match on every map) may have left mines broken or damaged on this map
        World.SetMap(map);
        Game.SeedNextGame = seed; // the server's randomness (bot personalities, spawn picks, path timing) the same every run
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 3, botSkill: BotDifficulty.Normal, mapId: map.Id);
        game.MapVoteEnabled = false;
        World.SetMap(map);
        var me = game.Players[0];
        var bots = game.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToList();
        if (bots.Count < 3)
        {
            Fail($"expected 3 bots, got {bots.Count}");
            return;
        }
        foreach (var b in bots)
            b.Bot!.Dummy = true; // stand still, never shoot (practice dummies)
        Player a = bots[0], bb = bots[1], c = bots[2];
        var props = map.PropInstances;
        var mines = map.TriggerProps;
        MLine($"{map.Name}: {props.Count} props, {mines.Length} mines ({mines.Count(i => map.PropRespawn(i) > 0)} with respawn), "
              + $"{props.Count(p => p.Def.Name == "Mine Warning Sign")} warning signs, {props.Count(p => p.Def.Breakable is { ExplodeRadius: > 0, IsMine: false })} barrels");
        int MineAt(float x, float z) => mines.OrderBy(i => Vector2.Distance(new Vector2(props[i].Position.X, props[i].Position.Z), new Vector2(x, z))).First();
        var now = 0.0;
        const double dt = 1.0 / World.TickRate;
        // Warm-up and countdown first (the first tick starts the pre-match): the scenarios run in a live match.
        var preMatch = false;
        while (!(preMatch && game.Phase == MatchPhase.Playing) && now < 60)
        {
            now += dt;
            game.Update(now, dt);
            preMatch |= game.Phase != MatchPhase.Playing; // (a new game says Playing until its first tick)
        }
        if (game.Phase != MatchPhase.Playing)
        {
            Fail($"the Landmines test match never started ({game.Phase} after 60 s)");
            return;
        }
        var matchNo = game.MatchNumber;
        void Place(Player p, float x, float z)
        {
            game.SelfTestPlace(p, new Vector3(x, map.GroundHeight(x, z, 5), z), 0);
            p.LastStandUsed = true;
            p.ImmuneUntil = 0;
            p.LastHurtTime = now; // no health regen during a scenario
            if (p.Bot is { } bot)
            {
                bot.DummyHome = p.Position;
                bot.DummyPlaced = true;
            }
        }
        void Park(Player p) // out of the way: dead, and (bots) not coming back during the scenario
        {
            p.Alive = false;
            p.RespawnAt = now + 1e6;
        }
        void Reset()
        {
            game.SelfTestResetProps();
            foreach (var p in game.Players.Values)
                Park(p);
        }
        void Tick(double seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                now += dt;
                game.Update(now, dt);
            }
        }
        double TickUntilGone(int mine, double max)
        {
            var start = now;
            while (now - start < max && !map.IsPropBroken(mine))
            {
                now += dt;
                game.Update(now, dt);
            }
            return map.IsPropBroken(mine) ? now - start : -1;
        }
        Vector3 Plate(int i) => props[i].Position;
        float Shoot(int i, Vector3 from)
        {
            var dir = Vector3.Normalize(props[i].Bounds.Center - from);
            var dist = World.RaycastWorld(from, dir, 40);
            game.ShotProps(me, from, dir, dist, 30, now);
            return dist;
        }
        var fuse = PropFile.DefaultTriggerFuse;

        // 1. Step on it: a click, then the blast after the fuse; damage falls off with distance; a neutral kill.
        var lone = MineAt(6, -8);
        fuse = props[lone].Def.Breakable!.ExplodeFuse;
        Reset();
        var p0 = Plate(lone);
        Place(me, p0.X, p0.Z);
        Place(a, p0.X + 3f, p0.Z);
        Place(bb, p0.X, p0.Z - 4.5f);
        int kills0 = game.Players.Values.Sum(p => (int)p.Kills), deaths0 = me.Deaths, clicks0 = game.MinesClicked;
        Tick(dt * 1.5);
        var litAfterStep = game.IsPropLit(lone) && !map.IsPropBroken(lone) && game.MinesClicked == clicks0 + 1;
        var wentOff = TickUntilGone(lone, 2) + dt * 2;
        int hpMe = me.Alive ? me.Health : 0, hpA = a.Health, hpB = bb.Health;
        // (Not the kill feed: it keeps 6 lines and drops old ones by the wall clock, so it depends on what ran before.)
        var neutralKill = game.LastPropBlastKill == (me.Id, me.Id, Game.LandmineKillId) && game.Players.Values.Sum(p => (int)p.Kills) == kills0 && me.Deaths == deaths0 + 1;
        MLine($"step on: clicked {litAfterStep}, went off {wentOff:0.00} s later (fuse {fuse:0.00} s); health after: on the plate {hpMe}, 3 m {hpA}, 4.5 m {hpB}; "
              + $"neutral kill (\"Landmine\", nobody's kill, a death) {neutralKill}");
        if (!litAfterStep)
            Fail("stepping on a mine didn't light it (with a click)");
        if (wentOff < fuse - 0.05 || wentOff > fuse + 0.1)
            Fail($"a stepped-on mine went off after {wentOff:0.00} s (fuse {fuse:0.00} s)");
        if (me.Alive || !(hpA < World.MaxHealth && hpA > 30) || !(hpB > hpA))
            Fail($"blast damage doesn't fall off as it should (plate {hpMe}, 3 m {hpA}, 4.5 m {hpB})");
        if (!neutralKill)
            Fail("a stepped-on mine's kill should read \"Landmine\" and be nobody's kill");

        // 2. On and straight off: the click commits, it still goes off (and misses whoever got clear); jumping over does nothing.
        var lane = MineAt(0, 10);
        Reset();
        var pl = Plate(lane);
        Place(a, pl.X + 0.3f, pl.Z);
        Tick(dt * 1.5);
        var litOnOff = game.IsPropLit(lane);
        Place(a, pl.X + 6f, pl.Z); // gone within a tick
        var offWent = TickUntilGone(lane, 2);
        var aHp = a.Health;
        var inst = props[lane];
        var lbr = inst.Def.Breakable!;
        bool inside = Game.OnPlate(inst, lbr, pl + new Vector3(0.55f * inst.Scale, 0, 0)), outside = Game.OnPlate(inst, lbr, pl + new Vector3(0.65f * inst.Scale, 0, 0));
        bool jumped = Game.OnPlate(inst, lbr, pl + new Vector3(0, 0.8f, 0)), crouched = Game.OnPlate(inst, lbr, pl + new Vector3(0.2f, 0.05f, 0));
        MLine($"on and off: lit {litOnOff}, still went off after {offWent:0.00} s, the one who got 6 m clear has {aHp} hp; plate at 0.55 m {inside}, 0.65 m {outside}, 0.8 m up {jumped}, feet on it {crouched}");
        if (!litOnOff || offWent < 0)
            Fail("a mine stepped on and straight off should still go off (the click commits)");
        if (aHp != World.MaxHealth)
            Fail($"someone 6 m clear of a 5 m blast was hurt ({aHp} hp)");
        if (!inside || outside || jumped || !crouched)
            Fail("the pressure plate's reach is wrong");

        // 3. Shot: it goes off after its fuse and the blast is the shooter's (their kill, in the feed as "Landmine").
        Reset();
        Place(me, p0.X, p0.Z + 12f);
        Place(a, p0.X + 0.3f, p0.Z);
        var meKills = me.Kills;
        var shotDist = Shoot(lone, me.Eye);
        var shotLit = game.IsPropLit(lone);
        var shotWent = TickUntilGone(lone, 2);
        Tick(dt * 2);
        var credited = !a.Alive && me.Kills == meKills + 1 && game.LastPropBlastKill == (me.Id, a.Id, Game.LandmineKillId);
        // Stepped on and shot in the same server tick (the plate is checked first): the shooter still gets the blast.
        Reset();
        Place(me, p0.X, p0.Z + 12f);
        Place(a, p0.X + 0.3f, p0.Z);
        meKills = me.Kills;
        game.SelfTestTickMines(now); // a steps on it: lit, nobody's yet
        var steppedFirst = game.IsPropLit(lone);
        Shoot(lone, me.Eye); // the same tick's shot
        TickUntilGone(lone, 2);
        Tick(dt * 2);
        var sameTick = steppedFirst && !a.Alive && me.Kills == meKills + 1 && game.LastPropBlastKill == (me.Id, a.Id, Game.LandmineKillId);
        MLine($"shot from {shotDist:0.0} m: lit {shotLit}, went off after {shotWent:0.00} s; the victim on it dead {!a.Alive}, the shooter's kill {credited}; "
              + $"stepped on and shot in one tick: the shooter's kill {sameTick}; shooter {me.Health} hp");
        if (!shotLit || shotWent < 0)
            Fail("a shot didn't set the mine off");
        if (!credited)
            Fail($"a shot mine's kill should be the shooter's (\"Landmine\"; last blast kill {game.LastPropBlastKill}, victim alive {a.Alive})");
        if (!sameTick)
            Fail("a mine stepped on and shot in the same tick should be the shooter's kill");

        // 4. Chains: a mine sets off the barrel next to it; one shot mine ripples through the minefield.
        var chain = MineAt(10, 5);
        var barrel = Enumerable.Range(0, props.Count).Where(i => props[i].Def.Name == "Explosive Barrel")
            .OrderBy(i => Vector3.Distance(props[i].Position, props[chain].Position)).First();
        Reset();
        var pc = Plate(chain);
        Place(a, pc.X, pc.Z);
        var det0 = game.PropDetonations;
        Tick(1.0);
        var chainOk = map.IsPropBroken(chain) && map.IsPropBroken(barrel);
        var chainDets = game.PropDetonations - det0;
        Reset();
        Place(me, -9.5f, 14f);
        var field = mines.Where(i => props[i].Position.X < -2 && props[i].Position.X > -16 && MathF.Abs(props[i].Position.Z) < 5).ToList();
        var first = field.OrderBy(i => props[i].Position.X).First();
        Shoot(first, me.Eye);
        Tick(2.5);
        var rippled = field.Count(map.IsPropBroken);
        MLine($"chains: mine -> barrel {chainOk} ({chainDets} explosions); one shot mine set off {rippled} of the {field.Count} in the minefield");
        if (!chainOk || chainDets < 2)
            Fail("a mine didn't set off the explosive barrel next to it");
        if (rippled < Math.Min(4, field.Count))
            Fail($"a shot mine set off only {rippled} of the minefield's {field.Count} mines (they're 3 m apart, the blast reaches 5 m)");

        // 5. A wall shields: behind it no damage, the same distance in the open hurts.
        var walled = MineAt(18, -6);
        Reset();
        var pw = Plate(walled);
        Place(a, pw.X + 3.5f, pw.Z); // behind the wall at x 19.5-20.1
        Place(bb, pw.X, pw.Z - 3.5f); // in the open
        Place(me, pw.X - 9f, pw.Z);
        Shoot(walled, me.Eye);
        TickUntilGone(walled, 2);
        Tick(dt * 2);
        MLine($"wall: behind it {a.Health} hp, the same distance in the open {bb.Health} hp");
        if (a.Health != World.MaxHealth || bb.Health >= World.MaxHealth)
            Fail("a wall didn't shield the blast (or the open side wasn't hurt)");

        // 6. Flak Jacket: the same blast at the same distance, less damage.
        Reset();
        game.SelfTestPerks(a, PerkSet.Empty);
        game.SelfTestPerks(bb, new PerkSet(PerkId.None, PerkId.FlakJacket, PerkId.None));
        Place(a, p0.X + 2.5f, p0.Z);
        Place(bb, p0.X - 2.5f, p0.Z);
        Place(me, p0.X, p0.Z + 12f);
        Shoot(lone, me.Eye);
        TickUntilGone(lone, 2);
        Tick(dt * 2);
        int plainDmg = World.MaxHealth - a.Health, flakDmg = World.MaxHealth - bb.Health;
        MLine($"Flak Jacket at 2.5 m: {plainDmg} damage without, {flakDmg} with (x{Perks.FlakMult})");
        if (plainDmg <= 0 || Math.Abs(flakDmg - (int)MathF.Round(plainDmg * Perks.FlakMult)) > 1)
            Fail($"Flak Jacket doesn't cut mine damage ({plainDmg} vs {flakDmg})");
        game.SelfTestPerks(bb, PerkSet.Empty);

        // 7. Bots: nodes next to a mine cost extra, and a bot told to walk past one goes round it.
        Reset();
        for (var wait = 0; wait < 1000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var nav = NavGraph.Current;
        var walker = c;
        walker.Bot!.Dummy = false;
        var goal = new Vector3(pl.X + 7f, pl.Y, pl.Z);
        Place(walker, pl.X - 7f, pl.Z);
        var pathLen = game.SelfTestBotPlan(walker, nav, goal, now);
        var pathNear = float.MaxValue;
        for (var k = 0; k < walker.Bot.PathLen; k++)
            pathNear = MathF.Min(pathNear, Vector2.Distance(new Vector2(nav.Nodes[walker.Bot.Path[k]].X, nav.Nodes[walker.Bot.Path[k]].Z), new Vector2(pl.X, pl.Z)));
        walker.Bot.ClearPath();
        walker.Bot.ForcedGoal = goal;
        var nearest = float.MaxValue;
        var arrived = -1.0;
        var startWalk = now;
        while (now - startWalk < 8)
        {
            now += dt;
            game.Update(now, dt);
            nearest = MathF.Min(nearest, Vector2.Distance(new Vector2(walker.Position.X, walker.Position.Z), new Vector2(pl.X, pl.Z)));
            if (Vector2.Distance(new Vector2(walker.Position.X, walker.Position.Z), new Vector2(goal.X, goal.Z)) < 1.5f)
            {
                arrived = now - startWalk;
                break;
            }
        }
        var walkTriggered = game.IsPropLit(lane) || map.IsPropBroken(lane);
        walker.Bot.ForcedGoal = null;
        walker.Bot.Dummy = true;
        MLine($"bots ({walker.Bot.Personality}): nav {nav.Nodes.Length} nodes, a planned path of {pathLen} nodes keeps {pathNear:0.00} m from the mine; walking past it the bot came within {nearest:0.00} m "
              + $"(plate {lbr.TriggerRadius} m), set it off {walkTriggered}, arrived after {(arrived < 0 ? "NOT" : $"{arrived:0.0} s")}; hazard builds {game.MineHazardBuilds}");
        if (nav.Nodes.Length == 0 || pathLen == 0)
            Fail("no nav graph or no bot path on the Landmines map");
        else if (pathNear < lbr.TriggerRadius + World.PlayerRadius)
            Fail($"a bot's planned path runs {pathNear:0.00} m from a mine");
        if (walkTriggered || nearest <= lbr.TriggerRadius)
            Fail($"a bot walking past a mine stepped on it (came within {nearest:0.00} m)");
        if (arrived < 0)
            Fail("the bot walking round the mine didn't get to its goal within 8 s");

        // 8. Zombies set mines off too, and a neutral blast kills them.
        Reset();
        var zombie = new Zombie { Id = 999, Sim = new MoveState { Position = pl, Grounded = true }, State = ZState.Chase, Health = 150, MaxHealth = 150 };
        game.Horde.Add(zombie);
        var zLit = false;
        var zDead = false;
        try
        {
            game.SelfTestTickMines(now);
            zLit = game.IsPropLit(lane);
            game.SelfTestTickPropFuses(now + fuse + 0.02);
            zDead = zombie.State == ZState.Dead && map.IsPropBroken(lane);
        }
        finally
        {
            game.Horde.Remove(zombie);
        }
        MLine($"zombies: one on the plate set it off {zLit}; the blast killed it {zDead} ({game.ZombiesKilledByMines} by mines)");
        if (!zLit || !zDead)
            Fail("a zombie on a mine should set it off, and the blast should kill it");

        // 9. Respawn: a mine placed with "respawn" is back that long after it went off, not under someone's feet.
        var back = mines.FirstOrDefault(i => map.PropRespawn(i) > 0, -1);
        if (back < 0)
        {
            Fail("no mine with \"respawn\" on the Landmines map");
            return;
        }
        Reset();
        var pr = Plate(back);
        var wait1 = map.PropRespawn(back);
        Place(me, pr.X, pr.Z - 14f);
        Shoot(back, me.Eye);
        TickUntilGone(back, 2);
        var pending = game.PropRespawnsPending;
        var respawned0 = game.PropsRespawned;
        Place(a, pr.X + 0.4f, pr.Z); // standing on the spot when it's due
        a.Health = World.MaxHealth;
        Tick(wait1 + 0.3);
        var heldBack = map.IsPropBroken(back);
        Place(a, pr.X + 8f, pr.Z);
        Tick(1.3);
        var isBack = !map.IsPropBroken(back) && !game.IsPropLit(back) && game.PropsRespawned == respawned0 + 1;
        Place(bb, pr.X, pr.Z);
        Tick(dt * 1.5);
        var worksAgain = game.IsPropLit(back);
        Tick(1);
        if (game.MatchNumber != matchNo || game.Phase != MatchPhase.Playing)
            Fail($"the match changed during the Landmines scenarios (match {game.MatchNumber}, {game.Phase})");
        MLine($"respawn {wait1:0} s: pending after it went off {pending}; still gone with someone on the spot {heldBack}; back once they left {isBack}; sets off again {worksAgain}");
        if (pending != 1 || !heldBack || !isBack || !worksAgain)
            Fail("a mine with \"respawn\" didn't come back as it should");
        game.SelfTestResetProps();
    }

    /// <summary>The per-tick cost of the pressure plates with 50 mines and 8 players (none on a plate): no allocation, a few microseconds.</summary>
    private static void LandmineCost()
    {
        if (Maps.Find("Landmines") is not { } home)
            return;
        var ops = new List<string>();
        for (var k = 0; k < 50; k++)
            ops.Add($"{{ \"op\": \"Prop\", \"name\": \"Landmine\", \"x\": {-12 + (k % 10) * 2.6f:0.0}, \"z\": {-10 + (k / 10) * 2.6f:0.0} }}");
        var map = MapFile.Load(PropTestMap(string.Join(", ", ops)), "fifty mines");
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 7, botSkill: BotDifficulty.Normal, mapId: home.Id);
        game.MapVoteEnabled = false;
        World.SetMap(map);
        game.Phase = MatchPhase.Playing;
        var i = 0;
        foreach (var p in game.Players.Values)
        {
            game.SelfTestPlace(p, new Vector3(-13 + i * 3.1f, 0, 14), 0);
            i++;
        }
        var bot = game.Players.Values.First(p => p.Bot != null);
        game.SelfTestPlace(bot, new Vector3(-13f, 0, -10), 0); // next to a mine, off its plate: the steering runs
        var now = 1.0;
        for (var k = 0; k < 200; k++)
        {
            game.SelfTestTickMines(now);
            game.SelfTestAvoidMines(bot, Vector3.UnitX);
        }
        const int ticks = 3000;
        var sw = new Stopwatch();
        var before = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        var steer = Vector3.Zero;
        for (var k = 0; k < ticks; k++)
        {
            now += 1.0 / World.TickRate;
            game.SelfTestTickMines(now);
            steer = game.SelfTestAvoidMines(bot, Vector3.UnitX);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        sw.Stop();
        var us = sw.Elapsed.TotalMilliseconds * 1000 / ticks;
        var lit = map.TriggerProps.Count(game.IsPropLit);
        MLine($"cost: {map.TriggerProps.Length} mines, {game.Players.Count} players: {us:0.0} us per tick (plates + one bot's steering), {alloc} bytes allocated over {ticks} ticks; "
              + $"{lit} set off; steering next to one {steer.X:0.00},{steer.Z:0.00}");
        if (map.TriggerProps.Length != 50 || alloc > 0)
            Fail($"the pressure plates allocate per tick ({alloc} bytes over {ticks} ticks) or the 50 mines didn't load");
        if (lit > 0)
            Fail("a mine went off with nobody on it");
        if (us > 50)
            Fail($"the pressure plates cost {us:0.0} us per tick with 50 mines");
        World.SetMap(home);
    }
}
