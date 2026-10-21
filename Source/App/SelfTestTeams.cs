namespace CallOfSlop;

/// <summary>
/// Self-test for team selection (Source/Game/TeamPick.cs): the size-difference rule, bots giving way, picks immune to
/// autobalance, the switch cooldown, no switching with the flag or while alive mid-match, no score penalty, the host's
/// Auto only setting, joining in progress, the replicated picks and determinism.
/// </summary>
public static partial class SelfTest
{
    /// <summary>A roomy map for team tests (12+ players, not a Zombies map).</summary>
    private static byte TeamTestMap() => (byte)Maps.All.First(m => m.MaxPlayers >= 12 && m.Zombies == null).Id;

    private static void CheckTeamPick()
    {
        var map = TeamTestMap();
        const byte Blue = 0, Red = 1;
        var now = Brutal.GlfwApi.Glfw.Time + 1000; // well past anyone's spawn protection
        int Count(Game g, byte t) => g.Players.Values.Count(p => p.Team == t);

        // ---- The difference rule: 6 people (no bots), max difference 2, everyone picks Blue in turn ----
        using (var g = ModeGame(0, GameMode.TeamDeathmatch, map))
        {
            for (var i = 0; i < 5; i++)
                g.SelfTestAddHuman();
            g.Phase = MatchPhase.Warmup;
            g.SelfTestAssignTeams();
            var people = g.Players.Values.OrderBy(p => p.Id).ToList();
            var answers = people.Select(p => g.SelfTestSetTeam(p, Blue, now)).ToList();
            var accepted = answers.Count(a => a == null);
            var refusal = answers.FirstOrDefault(a => a != null) ?? "";
            g.SelfTestBalance(now);
            int b = Count(g, Blue), r = Count(g, Red);
            Line($"rule (max 2): {accepted}/6 Blue picks accepted, then '{refusal}'; sides {b}v{r}");
            if (accepted != 4 || !refusal.Contains("full", StringComparison.Ordinal) || b != 4 || r != 2)
                Fail($"team pick: expected 4 Blue picks then 'full' and 4v2, got {accepted} ('{refusal}') and {b}v{r}");

            // Picks are never moved by autobalance: drop the two Red players, the 4 picks stay on Blue.
            foreach (var p in g.Players.Values.Where(p => p.Team == Red).ToList())
                g.Players.Remove(p.Id);
            var moved = g.SelfTestBalance(now);
            if (moved != 0 || Count(g, Blue) != 4)
                Fail($"team pick: autobalance moved {moved} picked players (Blue {Count(g, Blue)})");
            // ...but a person on Auto is: one Blue pick goes back to Auto and gets moved over (dead, so allowed).
            var auto = g.Players.Values.Where(p => p.Team == Blue && p.Id != g.LocalId).OrderByDescending(p => p.Id).First();
            var autoOk = g.SelfTestSetTeam(auto, Game.NoTeam, now) == null;
            auto.Alive = false;
            moved = g.SelfTestBalance(now);
            Line($"autobalance: 4 picks on Blue vs 0 -> moved {moved} (only the Auto player: {auto.Team == Red})");
            if (!autoOk || moved != 1 || auto.Team != Red || Count(g, Blue) != 3)
                Fail($"team pick: autobalance should move only the Auto player (moved {moved}, Auto on {auto.Team}, Blue {Count(g, Blue)})");

            // Max difference 0: sides must stay even.
            g.MaxTeamDiff = 0;
            foreach (var p in g.Players.Values)
                p.TeamChoice = Game.NoTeam;
            for (var i = 0; i < 2; i++)
                g.SelfTestAddHuman();
            g.SelfTestAssignTeams();
            var even = g.Players.Values.OrderBy(p => p.Id).Select(p => g.SelfTestSetTeam(p, Blue, now + 20) == null).Count(ok => ok);
            Line($"rule (max 0): {even}/{g.Players.Count} Blue picks accepted");
            if (even != g.Players.Count / 2)
                Fail($"team pick: with max difference 0, {even} of {g.Players.Count} could pick Blue");
        }

        // ---- Bots give way: 4 friends pick Blue in a 4 people + 4 bots lobby -> 4 people vs 4 bots ----
        using (var g = ModeGame(4, GameMode.TeamDeathmatch, map))
        {
            for (var i = 0; i < 3; i++)
                g.SelfTestAddHuman();
            g.Phase = MatchPhase.Warmup;
            g.SelfTestAssignTeams();
            var humans = g.Players.Values.Where(p => p.Bot == null).ToList();
            var refused = humans.Select(p => g.SelfTestSetTeam(p, Blue, now)).Count(a => a != null);
            var blue = g.Players.Values.Where(p => p.Team == Blue).ToList();
            var red = g.Players.Values.Where(p => p.Team == Red).ToList();
            Line($"bots give way: {refused} refused, Blue {blue.Count} ({blue.Count(p => p.Bot == null)} people), Red {red.Count} ({red.Count(p => p.Bot != null)} bots)");
            if (refused != 0 || blue.Count != 4 || blue.Any(p => p.Bot != null) || red.Count != 4 || red.Any(p => p.Bot == null))
                Fail("team pick: bots didn't give way to a group of 4 friends");

            // Join in progress: a newcomer lands on a side as Auto, sees the roster and can join a friend's team.
            g.Phase = MatchPhase.Playing;
            g.SelfTestAddHuman(); // 9 players: the map has room
            var late = g.Players.Values.OrderByDescending(p => p.Id).First();
            var lateTeam = late.Team;
            var friendJoin = g.SelfTestSetTeam(late, Blue, now); // dead (waiting to deploy), so allowed
            g.SelfTestBalance(now);
            Line($"join in progress: newcomer on {GameModes.TeamName(lateTeam)} (Auto {late.TeamChoice == Game.NoTeam || friendJoin == null}), join Blue friends: {friendJoin ?? "ok"}; sides {Count(g, Blue)}v{Count(g, Red)}");
            if (lateTeam > 1 || friendJoin != null || late.Team != Blue || Math.Abs(Count(g, Blue) - Count(g, Red)) > 1)
                Fail($"team pick: join in progress (team {lateTeam}, join '{friendJoin}', sides {Count(g, Blue)}v{Count(g, Red)})");

            // The picks and rules replicate in the mode state (a fresh game reads them back).
            var w = new PacketWriter();
            g.WriteModeState(w);
            using var c = ModeGame(4, GameMode.TeamDeathmatch, map);
            for (var i = 0; i < 4; i++)
                c.SelfTestAddHuman();
            c.MaxTeamDiff = 4;
            c.TeamSelection = TeamSelection.AutoOnly;
            var rd = new PacketReader(w.Span[1..]);
            c.ReadModeState(ref rd);
            var same = g.Players.Values.All(p => c.Players.TryGetValue(p.Id, out var q) && q.Team == p.Team && q.TeamChoice == p.TeamChoice);
            Line($"replication: mode state {w.Length} bytes, picks and teams match {same}, rules {c.TeamSelection}/{c.MaxTeamDiff}, protocol {ServerInfo.ProtocolVersion}, Msg.TeamPick {(byte)Msg.TeamPick}");
            if (!same || c.TeamSelection != TeamSelection.Manual || c.MaxTeamDiff != TeamRules.DefaultMaxDiff || (byte)Msg.TeamPick <= (byte)Msg.ZPickup)
                Fail("team pick: picks or rules didn't replicate");
        }

        // ---- Switching mid-match: only while dead, cooldown, no flag, no score penalty, no streak gain ----
        using (var g = ModeGame(3, GameMode.CaptureTheFlag, map))
        {
            var me = g.Local!;
            foreach (var p in g.Players.Values)
                g.SelfTestPerks(p, PerkSet.Empty);
            var enemy = g.Players.Values.First(p => p.Bot != null && p.Team != me.Team);
            g.Phase = MatchPhase.Playing;
            var start = me.Team;
            var alive = g.SelfTestSetTeam(me, (byte)(1 - start), now);
            g.SelfTestDamageWith(enemy, me, 1000, Weapons.DefaultPrimary.Id, now);
            var (kills, deaths, s0, s1, enemyKills) = (me.Kills, me.Deaths, g.TeamScore[0], g.TeamScore[1], enemy.Kills);
            var dead = g.SelfTestSetTeam(me, (byte)(1 - start), now + 1);
            var back = g.SelfTestSetTeam(me, start, now + 2);
            var later = g.SelfTestSetTeam(me, start, now + 1 + TeamRules.SwitchCooldown + 0.1);
            var noPenalty = me.Kills == kills && me.Deaths == deaths && g.TeamScore[0] == s0 && g.TeamScore[1] == s1 && enemy.Kills == enemyKills;
            Line($"switching: alive '{alive}', dead {dead ?? "ok"}, again '{back}', after {TeamRules.SwitchCooldown:0} s {later ?? "ok"}; deaths {deaths} -> {me.Deaths}, scores unchanged {noPenalty}");
            if (me.Alive || alive == null || dead != null || back == null || !back.StartsWith("Wait", StringComparison.Ordinal) || later != null || !noPenalty || me.Team != start)
                Fail("team pick: mid-match switching rules (dead only, cooldown, no score change) broken");

            // Carrying the flag: never.
            var enemyFlag = 1 - me.Team;
            g.Flags[enemyFlag].Carrier = me.Id;
            var flag = g.SelfTestSetTeam(me, (byte)(1 - me.Team), now + 60);
            g.Flags[enemyFlag].Carrier = byte.MaxValue;
            Line($"flag carrier: '{flag}'");
            if (flag == null || !flag.Contains("flag", StringComparison.Ordinal))
                Fail("team pick: switched teams while carrying the flag");

            // Warmup: alive is fine; it's a respawn on the new side (no death, the kill streak count starts over).
            g.Phase = MatchPhase.Warmup;
            g.SelfTestSpawn(me);
            me.Streak.Count = 3;
            var d0 = me.Deaths;
            var warm = g.SelfTestSetTeam(me, (byte)(1 - me.Team), now + 80);
            var spawns = g.Objectives.TeamSpawns[me.Team];
            var onSide = spawns.Any(sp => System.Numerics.Vector3.Distance(sp, me.Position) < 1.5f);
            Line($"warmup switch: {warm ?? "ok"}, alive {me.Alive}, at a {GameModes.TeamName(me.Team)} spawn {onSide}, deaths {d0} -> {me.Deaths}, streak {me.Streak.Count}");
            if (warm != null || !me.Alive || !onSide || me.Deaths != d0 || me.Streak.Count != 0)
                Fail("team pick: a warmup switch should respawn you on the new side with no death and no streak");
        }

        // ---- Host setting Auto only: picks cleared and refused; the old even split ----
        using (var g = ModeGame(5, GameMode.KingOfTheHill, map))
        {
            g.Phase = MatchPhase.Warmup;
            var me = g.Local!;
            var picked = g.SelfTestSetTeam(me, (byte)(1 - me.Team), now) == null;
            g.SetTeamRules(TeamSelection.AutoOnly, 2);
            var refused = g.SelfTestSetTeam(me, Blue, now + 30) ?? "";
            g.SelfTestAssignTeams();
            Line($"Auto only: picked first {picked}, pick cleared {me.TeamChoice == Game.NoTeam}, then '{refused}', sides {Count(g, Blue)}v{Count(g, Red)}");
            if (!picked || me.TeamChoice != Game.NoTeam || !refused.Contains("Auto only", StringComparison.Ordinal) || Count(g, Blue) != 3 || Count(g, Red) != 3)
                Fail("team pick: Auto only didn't clear and refuse picks");
        }

        // ---- Determinism: the same lobby and picks give the same teams ----
        string Teams()
        {
            using var g = ModeGame(5, GameMode.TeamDeathmatch, map);
            for (var i = 0; i < 3; i++)
                g.SelfTestAddHuman();
            g.Phase = MatchPhase.Warmup;
            g.SelfTestAssignTeams();
            foreach (var p in g.Players.Values.Where(p => p.Bot == null).OrderBy(p => p.Id))
                g.SelfTestSetTeam(p, (byte)(p.Id % 3 == 0 ? Red : Blue), now);
            g.SelfTestAssignTeams(); // a new match keeps the picks
            return string.Join(",", g.Players.Values.OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.Team}{(p.TeamChoice < 2 ? "*" : "")}"));
        }
        var t1 = Teams();
        var t2 = Teams();
        Line($"determinism: {t1}");
        if (t1 != t2)
            Fail($"team pick: not deterministic ({t1} vs {t2})");

        // ---- settings.txt: team_select / team_max_diff; older files get Manual and 2 ----
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-teams-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllLines(tmp, ["name=Old", "mode=TeamDeathmatch"]);
            var old = Settings.Load(tmp);
            new Settings { TeamSelect = TeamSelection.AutoOnly, TeamMaxDiff = 3 }.Save(tmp);
            var saved = Settings.Load(tmp);
            File.WriteAllLines(tmp, ["team_max_diff=99"]);
            var high = Settings.Load(tmp);
            if (old.TeamSelect != TeamSelection.Manual || old.TeamMaxDiff != 2 || saved.TeamSelect != TeamSelection.AutoOnly || saved.TeamMaxDiff != 3 || high.TeamMaxDiff != TeamRules.MaxDiffLimit)
                Fail($"team pick settings: old {old.TeamSelect}/{old.TeamMaxDiff}, saved {saved.TeamSelect}/{saved.TeamMaxDiff}, 99 -> {high.TeamMaxDiff}");
        }
        finally
        {
            File.Delete(tmp);
        }
        Line("settings: team_select and team_max_diff save and load; older files get Manual and 2");
    }
}
