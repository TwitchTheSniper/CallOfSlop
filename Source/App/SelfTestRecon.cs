using System.Numerics;

namespace CallOfSlop;

/// <summary>Self-test section "Recon reveal colours" (see SelfTest.cs): who a Recon Sweep / Pulse reveals, to whom, in what colour.</summary>
public static partial class SelfTest
{
    private static void CheckReconReveal()
    {
        using var game = ModeGame(3, GameMode.TeamDeathmatch);
        var all = game.Players.Values.OrderBy(p => p.Id).ToArray();
        if (all.Length < 4)
        {
            Fail($"recon test needs 4 players, got {all.Length}");
            return;
        }
        Player me = all[0], mate = all[1], foe1 = all[2], foe2 = all[3];
        me.Team = 0;
        mate.Team = 0;
        foe1.Team = 1;
        foe2.Team = 1;
        for (var i = 0; i < all.Length; i++)
        {
            game.SelfTestPerks(all[i], PerkSet.Empty); // no Low Profile
            game.SelfTestPlace(all[i], new Vector3(2 * i, 0, 3), 0);
        }
        var localBackup = game.LocalId;
        string Seen(Player viewer, double now)
        {
            game.LocalId = viewer.Id;
            var ids = game.Players.Values.Where(p => game.IsRevealed(p, now)).OrderBy(p => p.Id).Select(p => p.Id);
            return string.Join(",", ids);
        }
        string Ids(params Player[] ps) => string.Join(",", ps.Select(p => p.Id).OrderBy(x => x));

        // Allies never revealed, revealed players always in their own team colour.
        void CheckColours(Player viewer, double now, string label)
        {
            game.LocalId = viewer.Id;
            foreach (var p in game.Players.Values.Where(q => game.IsRevealed(q, now)))
            {
                if (game.IsTeamMode && p.Team == viewer.Team)
                    Fail($"{label}: viewer {viewer.Id} sees ally {p.Id} revealed");
                var want = game.IsTeamMode && p.Team < 2 ? GameModes.TeamColors[p.Team] : ReconOverlay.FfaColor;
                if (ReconOverlay.ColorFor(game, p) != want)
                    Fail($"{label}: player {p.Id} (team {p.Team}) overlay colour {ReconOverlay.ColorFor(game, p)} (want {want})");
            }
        }

        // ---- TDM: a Blue sweep helps the whole Blue team and shows only Red players --------------
        if (Seen(me, 1) != "")
            Fail("something was revealed with no recon running");
        game.StreakEffects.Add(new StreakEffect(StreakId.ReconSweep, me.Id, 0, KillStreaks.ReconSweepSeconds));
        var blueCaller = Seen(me, 1);
        var blueMate = Seen(mate, 1);
        var redViewer = Seen(foe1, 1);
        Line($"TDM Blue sweep: caller sees [{blueCaller}], Blue teammate sees [{blueMate}], Red player sees [{redViewer}] (Red = [{Ids(foe1, foe2)}])");
        if (blueCaller != Ids(foe1, foe2) || blueMate != Ids(foe1, foe2) || redViewer != "")
            Fail("a Blue Recon Sweep must show exactly the Red players to every Blue player, and nothing to Red");
        CheckColours(me, 1, "TDM Blue viewer");
        CheckColours(mate, 1, "TDM Blue teammate");
        game.LocalId = me.Id;
        if (ReconOverlay.ColorFor(game, foe1) != GameModes.TeamColors[1])
            Fail("a revealed Red player must be drawn red");

        // A Red sweep: Red players see the Blue team (in blue); Blue sees nothing new.
        game.StreakEffects.Clear();
        game.StreakEffects.Add(new StreakEffect(StreakId.ReconSweep, foe1.Id, 0, KillStreaks.ReconSweepSeconds));
        var redCaller = Seen(foe2, 1);
        var blueAfter = Seen(me, 1);
        Line($"TDM Red sweep: Red teammate sees [{redCaller}], Blue sees [{blueAfter}]");
        if (redCaller != Ids(me, mate) || blueAfter != "")
            Fail("a Red Recon Sweep must show exactly the Blue players to Red, and nothing to Blue");
        CheckColours(foe2, 1, "TDM Red viewer");
        game.LocalId = foe2.Id;
        if (ReconOverlay.ColorFor(game, me) != GameModes.TeamColors[0])
            Fail("a revealed Blue player must be drawn blue");

        // Mid-match team change: read live, so the switched player is revealed (and red) at once.
        game.StreakEffects.Clear();
        game.StreakEffects.Add(new StreakEffect(StreakId.ReconSweep, me.Id, 0, KillStreaks.ReconSweepSeconds));
        mate.Team = 1;
        var afterSwitch = Seen(me, 2);
        game.LocalId = me.Id;
        var switchedColour = ReconOverlay.ColorFor(game, mate);
        mate.Team = 0;
        Line($"team switch: Blue caller now sees [{afterSwitch}], switched player colour {switchedColour}");
        if (afterSwitch != Ids(mate, foe1, foe2) || switchedColour != GameModes.TeamColors[1])
            Fail("a player who switched to the enemy team must be revealed in their new team's colour");

        // No local player (not joined / spectating without a body): nothing revealed.
        game.LocalId = 200;
        if (game.Players.Values.Any(p => game.IsRevealed(p, 1)))
            Fail("a viewer without a player must not see recon reveals");

        // Recon Pulse (equipment): its owner sees enemies in range, never allies in range.
        game.StreakEffects.Clear();
        game.Sensors.Add(new ReconSensor(new Vector3(3, 0, 3), me.Id, 0, Throwables.ReconDuration));
        var pulse = Seen(me, 1);
        var pulseMate = Seen(mate, 1);
        Line($"Recon Pulse: owner sees [{pulse}] (ally {mate.Id} in range), teammate sees [{pulseMate}]");
        if (pulse != Ids(foe1, foe2) || pulseMate != "")
            Fail("a Recon Pulse must show only enemies in range, to its owner only (allies were shown as enemies)");
        CheckColours(me, 1, "Recon Pulse");
        game.Sensors.Clear();

        // Strike markers: friendly to the caller and their team, hostile (red) to the other team.
        game.LocalId = mate.Id;
        var mateFriendly = StreakRender.StrikeIsFriendly(game, me.Id);
        game.LocalId = foe1.Id;
        var foeFriendly = StreakRender.StrikeIsFriendly(game, me.Id);
        game.LocalId = me.Id;
        var ownFriendly = StreakRender.StrikeIsFriendly(game, me.Id);

        // ---- FFA: the caller sees everyone else (orange); nobody else benefits ---------------------
        game.Mode = GameMode.FreeForAll; // resets teams and respawns everyone: put them back
        for (var i = 0; i < all.Length; i++)
            game.SelfTestPlace(all[i], new Vector3(2 * i, 0, 3), 0);
        game.StreakEffects.Add(new StreakEffect(StreakId.ReconSweep, me.Id, 0, KillStreaks.ReconSweepSeconds));
        var ffaCaller = Seen(me, 1);
        var ffaOther = Seen(mate, 1);
        Line($"FFA sweep: caller sees [{ffaCaller}], another player sees [{ffaOther}]");
        if (ffaCaller != Ids(mate, foe1, foe2) || ffaOther != "")
            Fail("an FFA Recon Sweep must show every other player to its caller only");
        CheckColours(me, 1, "FFA");
        game.LocalId = mate.Id;
        var ffaStrike = StreakRender.StrikeIsFriendly(game, me.Id);
        Line($"strike markers friendly: own {ownFriendly}, TDM teammate {mateFriendly}, TDM enemy {foeFriendly}, FFA other {ffaStrike}");
        if (!ownFriendly || !mateFriendly || foeFriendly || ffaStrike)
            Fail("strike markers must be friendly for the caller's team only");
        game.StreakEffects.Clear();
        game.LocalId = localBackup;

        // ---- Overlay shape: inside the character, translucent -------------------------------------
        Span<Box> parts = stackalloc Box[4];
        var worst = float.MaxValue;
        foreach (var stance in new[] { Stance.Stand, Stance.Crouch, Stance.Prone })
        {
            var n = ReconOverlay.LocalParts(stance, parts);
            if (n != 4)
                Fail($"{stance}: {n} overlay parts (want 4)");
            foreach (var yaw in new[] { 0f, MathF.PI / 2, MathF.PI, -MathF.PI / 2 })
            {
                var feet = new Vector3(3, 1, -2);
                var hb = World.Hitbox(feet, stance, yaw);
                var body = Matrix4x4.CreateRotationY(-yaw) * Matrix4x4.CreateTranslation(feet);
                for (var i = 0; i < n; i++)
                    for (var c = 0; c < 8; c++)
                    {
                        var corner = parts[i].Center + parts[i].Half * new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1);
                        var w = Vector3.Transform(corner, body) - hb.Center;
                        var margin = hb.Half - Vector3.Abs(w);
                        var m = MathF.Min(margin.X, MathF.Min(margin.Y, margin.Z));
                        worst = MathF.Min(worst, m);
                        if (m <= 0.001f)
                            Fail($"{stance} yaw {yaw:0.00}: overlay part {i} pokes out of the character ({m:0.000} m)");
                    }
            }
        }
        var alphas = Enumerable.Range(0, 200).Select(i => ReconOverlay.Alpha(i * 0.01)).ToArray();
        Line($"overlay: 4 parts at {ReconOverlay.Inset:0.00} size, closest to the outside {worst * 100:0.0} cm in; alpha {alphas.Min():0.00}-{alphas.Max():0.00}");
        float maxA = ReconOverlay.MaxAlpha, minA = ReconOverlay.MinAlpha, inset = ReconOverlay.Inset; // locals: no constant-folding warnings
        if (alphas.Max() > maxA + 1e-4f || alphas.Min() < minA - 1e-4f || maxA > 0.5f || minA <= 0)
            Fail("the recon overlay must stay translucent (alpha within 0..0.5)");
        if (inset is >= 0.95f or < 0.7f)
            Fail("the recon overlay must be slightly smaller than the character");
    }
}
