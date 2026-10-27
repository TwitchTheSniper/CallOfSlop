using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// "Independent fire rates" (board fr01, 2026-10-27): every receiver of a volley build fires at its own rate, with its
/// own trigger, feed, spool-up and burst; chain fire stays the staggered alternative. Short, scripted: the real local
/// trigger loop at 240 frames a second, a second or two of trigger per case.
/// </summary>
public static partial class SelfTest
{
    private static void CheckIndependentFireRates()
    {
        const string Work = "Workhorse Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:FMJ,action:%A%}";
        string Two(string a, string b) => $"Rifle Grip{{receiver_1:{Work.Replace("%A%", a)},receiver_2:{Work.Replace("%A%", b)}}}";
        WeaponDef Def(WeaponBuild b) => WeaponBuilder.Assemble(b).Def ?? throw new InvalidOperationException($"fire rates: '{b.ToText()}' isn't complete");
        // A test-only action part (no shipped action fires at 100 rpm): the stock action's mount and boxes, the given trigger.
        WeaponBuild WithActions(WeaponBuild b, params ActionInfo?[] acts)
        {
            for (var i = 0; i < acts.Length; i++)
                if (acts[i] is { } a && WeaponBuild.ActionNodeOf(b.Receivers[i]) is { } an)
                    b.Receivers[i].Attach(an.ParentSocket!.Name, new PartDef
                    {
                        Name = $"Test {a.Rpm} rpm {a.Kind}", Category = PartCategory.Action, Mount = "action", Weight = an.Part.Weight, Action = a, Boxes = an.Part.Boxes,
                    });
            return b;
        }

        // ---- The numbers: each receiver its own rate and mode; DPS adds up; chain fire unchanged -----------------------
        var slowFast = Def(WithActions(BuildOf(Two("Full-Auto Sear", "Full-Auto Sear")), new ActionInfo { Kind = ActionKind.Auto, Rpm = 100 }, new ActionInfo { Kind = ActionKind.Auto, Rpm = 900 }));
        var fg = slowFast.FireGroups;
        Line($"100 + 900 rpm volley: groups {(fg == null ? "none" : string.Join(", ", fg.Select(f => $"{f.Rpm} rpm {f.Mode} feed {f.Feed}/{f.FeedMag}")))}, combined {slowFast.Rpm} rpm {slowFast.ModeName}, mag {slowFast.Mag}, DPS {slowFast.Dps:0}");
        if (fg is not { Length: 2 } || fg[0].Rpm != 100 || fg[1].Rpm != 900 || fg[0].Feed == fg[1].Feed)
            Fail("fire rates: a 100 + 900 rpm volley should keep each receiver's own rate and its own magazine");
        else
        {
            var expect = slowFast.GroupShotDamage(0) * 100 / 60f + slowFast.GroupShotDamage(1) * 900 / 60f;
            if (MathF.Abs(slowFast.Dps - expect) > 0.5f || slowFast.Mag != fg[0].FeedMag + fg[1].FeedMag)
                Fail($"fire rates: the volley's DPS should be its receivers' added up ({slowFast.Dps:0} vs {expect:0}) and its magazine both feeds ({slowFast.Mag})");
        }
        var chainBuild = WithActions(BuildOf(Two("Full-Auto Sear", "Full-Auto Sear")), new ActionInfo { Kind = ActionKind.Auto, Rpm = 100 }, new ActionInfo { Kind = ActionKind.Auto, Rpm = 900 });
        chainBuild.StyleChoice = FireStyle.Rotate;
        var chained = Def(chainBuild);
        Line($"the same as chain fire: {chained.Rpm} rpm, rotate {chained.RotateGroups}, groups {(chained.FireGroups == null ? "none" : "some")}");
        if (chained.FireGroups != null || !chained.RotateGroups || chained.Rpm != 1000)
            Fail("fire rates: chain fire should stay the staggered mode (rates add, receivers take turns, no independent triggers)");
        var single = Def(BuildOf($"Rifle Grip{{receiver_1:{Work.Replace("%A%", "Full-Auto Sear")}}}"));
        if (single.FireGroups != null)
            Fail("fire rates: a single-receiver build should fire as one trigger, as before");

        // ---- Through the real trigger loop -------------------------------------------------------------------------------
        World.SetMap(Maps.Find("Workbench Range") ?? Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: Maps.Find("Workbench Range")?.Id ?? 0);
        game.BuilderRange = true;
        var me = game.Local!;
        me.Alive = true;
        game.SelfTestPerks(me, PerkSet.Empty);
        const double Frame = 1.0 / 240;
        var clock = 1000.0;
        // Fires def for `seconds` (held throughout, pulled once at the start, or every frame: spam): each receiver's shot times.
        List<double>[] Fire(WeaponDef def, double seconds, bool spam)
        {
            game.UseBuiltWeapons(def, def);
            game.EquipUntil = 0;
            game.NextFireTime = 0;
            game.Active.Ammo = game.Active.Def.Mag;
            game.Active.ReloadUntil = 0;
            for (var i = 0; i < 40; i++)
                game.UpdateTrigger(clock += 0.25, false, false); // spools wind down
            var groups = def.FireGroups!;
            var shots = groups.Select(_ => new List<double>()).ToArray();
            var start = clock;
            for (var t = 0.0; t < seconds; t += Frame)
            {
                var now = start + t;
                game.SelfTestWeaponFrame(now, (float)Frame);
                var nexts = game.Active.Groups is { } before ? (double[])before.Next.Clone() : null;
                game.UpdateTrigger(now, true, spam || t == 0);
                var gs = game.Active.Groups!;
                for (var g = 0; g < groups.Length; g++)
                    // A receiver fired when its next-shot time moved past where the trigger loop's catch-up would put it
                    // (feeds can be shared, so not by its rounds).
                    if (gs.Next[g] > Math.Max(nexts?[g] ?? 0, now - 0.02) + 1e-6)
                        shots[g].Add(t);
            }
            clock = start + seconds;
            return shots;
        }
        void Expect(string what, List<double> shots, int min, int max)
        {
            Line($"  {what}: {shots.Count} shot(s) at {string.Join(" ", shots.Take(8).Select(s => s.ToString("0.000")))}{(shots.Count > 8 ? " ..." : "")}");
            if (shots.Count < min || shots.Count > max)
                Fail($"fire rates: {what}: {shots.Count} shots, expected {min}-{max}");
        }
        try
        {
            // 100 + 900 rpm, held 1 s: 2 and 15-16 shots, the first on the same frame.
            var sf = Fire(slowFast, 1.0, false);
            Expect("100 rpm receiver, held 1 s", sf[0], 2, 2);
            Expect("900 rpm receiver beside it, held 1 s", sf[1], 15, 16);
            if (sf[0].Count > 0 && sf[1].Count > 0 && sf[0][0] != sf[1][0])
                Fail("fire rates: receivers that are ready should fire on the same tick");

            // Semi + auto: held, the semi fires once and the auto runs; tapped every frame, the semi fires at its own cycle.
            var semiAuto = Def(BuildOf(Two("Semi-Auto Trigger", "Full-Auto Sear")));
            var held = Fire(semiAuto, 1.0, false);
            Expect("semi (400 rpm) + auto (600 rpm), held 1 s: the semi", held[0], 1, 1);
            Expect("  ... the auto", held[1], 10, 11);
            var tapped = Fire(semiAuto, 1.0, true);
            Expect("semi + auto, pulled every frame for 1 s: the semi", tapped[0], 6, 7);
            Expect("  ... the auto", tapped[1], 10, 11);

            // Burst + auto: one pull, the burst fires its three and waits for the next pull; the auto keeps going.
            var burstAuto = Def(BuildOf(Two("Three-Round Burst Group", "Full-Auto Sear")));
            var ba = Fire(burstAuto, 1.0, false);
            Expect("3-round burst + auto, one pull held 1 s: the burst", ba[0], 3, 3);
            Expect("  ... the auto", ba[1], 10, 11);
            Expect("3-round burst + auto, pulled every frame for 2 s: the burst", Fire(burstAuto, 2.0, true)[0], 12, 15);

            // Pump and bolt keep their cycle times beside an auto.
            var pumpAuto = Def(BuildOf(Two("Pump Slide", "Full-Auto Sear")));
            var pa = Fire(pumpAuto, 2.0, true);
            Expect("pump (70 rpm) + auto, pulled every frame for 2 s: the pump", pa[0], 3, 3);
            Expect("  ... the auto", pa[1], 20, 21);
            Expect("bolt (45 rpm) + auto, pulled every frame for 2 s: the bolt", Fire(Def(BuildOf(Two("Turn Bolt", "Full-Auto Sear"))), 2.0, true)[0], 2, 2);

            // Spool-up: a chain gun receiver winds up on its own; the auto beside it fires at full rate from the start.
            var chainAuto = Def(BuildOf($"Rifle Grip{{receiver_1:Thumper Chain Gun Receiver{{barrel:Big Iron Barrel,feed:Belt Box,ammo:Armour Piercing,action:Chain Drive Motor}},receiver_2:{Work.Replace("%A%", "Full-Auto Sear")}}}"));
            var ca = Fire(chainAuto, 1.5, false);
            var firstGap = ca[0].Count > 2 ? ca[0][1] - ca[0][0] : 0;
            var lastGap = ca[0].Count > 2 ? ca[0][^1] - ca[0][^2] : 0;
            Line($"  chain ({chainAuto.FireGroups![0].Rpm} rpm, spool {chainAuto.FireGroups[0].SpoolTime} s) + auto: {ca[0].Count} chain shots, first gap {firstGap:0.000} s, last {lastGap:0.000} s; {ca[1].Count} auto shots");
            if (ca[0].Count < 4 || firstGap < lastGap * 1.5 || MathF.Abs((float)lastGap - chainAuto.FireGroups[0].FireInterval) > 0.01f)
                Fail("fire rates: a chain receiver should wind up to its own full rate");
            if (ca[1].Count is < 15 or > 16)
                Fail($"fire rates: the auto beside a spooling receiver should fire at its own rate from the start ({ca[1].Count} in 1.5 s, expected 15-16)");

            // A single-shot receiver runs dry and reloads on its own while the auto keeps firing.
            var breakAuto = Def(BuildOf(Two("Break Action", "Full-Auto Sear")));
            var bk = Fire(breakAuto, breakAuto.FireGroups![0].Reload + 0.4, true);
            Expect($"break action (reloads its own chamber, {breakAuto.FireGroups[0].Reload:0.00} s) + auto: the break action", bk[0], 2, 2);
            var autoExpect = (int)((breakAuto.FireGroups[0].Reload + 0.4) / breakAuto.FireGroups[1].FireInterval);
            Expect("  ... the auto, never waiting for it", bk[1], autoExpect - 1, autoExpect + 2);
            if (bk[0].Count == 2 && bk[0][1] - bk[0][0] < breakAuto.FireGroups[0].Reload - 0.01)
                Fail("fire rates: the break action should wait out its own reload");

            // A shared hopper pools its rounds: three receivers draw on one feed.
            var hopper = Def(BuildOf("Mule Frame{receiver_1:Workhorse Receiver{barrel:Standard Barrel,action:Full-Auto Sear},receiver_2:Workhorse Receiver{barrel:Standard Barrel,action:Full-Auto Sear},receiver_3:Workhorse Receiver{barrel:Standard Barrel,action:Full-Auto Sear},hopper:Manifold Hopper{ammo:FMJ}}"));
            var hp = Fire(hopper, 0.5, false);
            var used = hopper.Mag - game.Active.Ammo;
            Line($"  hopper volley: {hp.Sum(x => x.Count)} receiver shots in 0.5 s, {used} rounds from the pool of {hopper.Mag}");
            if (used != hp.Sum(x => x.Count) || hp.Any(x => x.Count == 0))
                Fail("fire rates: receivers on a shared feed should all fire and draw a round each from the one pool");
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }

        // ---- The server's budget: each receiver at its own rate; a shot naming one too early drops it -------------------
        {
            var fgs = slowFast.FireGroups!;
            Array.Clear(me.GroupShotClock);
            // At most its rate over the 0.85 slack, plus the 0.3 s bank and the 0.05 s lead.
            int Most(int g) => (int)(1.35 / (fgs[g].FireInterval * 0.85)) + 1;
            int slow = 0, fast = 0;
            for (var t = 0.0; t < 1.0; t += 1.0 / 60)
            {
                var m = game.SelfTestGroupBudget(me, slowFast, 0, 5000 + t); // a client claiming both every tick
                slow += m & 1;
                fast += (m >> 1) & 1;
            }
            Line($"server budget, both claimed every tick for 1 s: {slow} slow, {fast} fast receiver shots allowed");
            if (slow > Most(0) || fast > Most(1) || slow < 2 || fast < 15)
                Fail($"fire rates: the server should hold each receiver to its own rate ({slow} at 100 rpm, at most {Most(0)}; {fast} at 900 rpm, at most {Most(1)} in 1 s)");
        }

        // ---- Bots: the same per-receiver trigger --------------------------------------------------------------------------
        {
            var bot = new BotBrain(BotDifficulty.Hard);
            var next = 0.0;
            int slow = 0, fast = 0;
            for (var t = 0.0; t < 1.0; t += 1.0 / 60)
            {
                if (6000 + t < next)
                    continue;
                var m = game.BotFireGroups(bot, me, slowFast, 6000 + t, ref next);
                slow += m & 1;
                fast += (m >> 1) & 1;
            }
            Line($"bot trigger, 1 s: {slow} slow, {fast} fast receiver shots");
            if (slow != 2 || fast is < 14 or > 16)
                Fail($"fire rates: a bot should fire each receiver at its own rate ({slow} at 100 rpm, {fast} at 900 rpm in 1 s)");
        }

        // ---- No allocations per shot --------------------------------------------------------------------------------------
        {
            var gs = new FireGroupState();
            var fgs = slowFast.FireGroups!;
            gs.Reset(slowFast);
            gs.Fill(fgs, slowFast.Mag);
            var rays = new List<ShotRay>(64);
            Game.ShotRays(slowFast, Vector3.Zero, -Vector3.UnitZ, 0.01f, 1, rays, 3); // warm
            gs.Step(fgs, 0, true, true, true, 0, 1f, true);
            game.SelfTestGroupBudget(me, slowFast, 3, 7000);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 600; i++)
            {
                var now = 1 + i / 60.0;
                gs.UpdateSpool(fgs, true, now);
                var m = gs.Step(fgs, now, true, i % 2 == 0, true, 0, 1f, true);
                if (m != 0)
                    Game.ShotRays(slowFast, Vector3.Zero, -Vector3.UnitZ, 0.01f, (uint)i, rays, m);
                game.SelfTestGroupBudget(me, slowFast, m, 7001 + now);
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Line($"allocations over 600 ticks of the receivers' triggers, rays and server budget: {bytes} bytes");
            if (bytes > 0)
                Fail($"fire rates: firing receivers at their own rates allocated {bytes} bytes (should be none)");
        }

        // ---- The standard builds: which volleys' DPS the new rule changes ------------------------------------------------
        var changed = 0;
        var volleys = 0;
        foreach (var sb in StandardBuilds.All.Where(s => !s.IsMelee))
        {
            var d = WeaponBuilder.Assemble(sb.Build()).Def;
            if (d?.FireGroups is not { } g)
                continue;
            volleys++;
            // The old linked volley: the slowest receiver's rate for all, the strictest trigger.
            var slowest = g.MinBy(f => f.Rpm);
            var mode = g.Any(f => f.Mode == FireMode.Semi) ? FireMode.Semi : g.Any(f => f.Mode == FireMode.Burst) ? FireMode.Burst : FireMode.Auto;
            var burst = g.FirstOrDefault(f => f.Mode == FireMode.Burst);
            var interval = 60f / slowest.Rpm;
            var linked = mode == FireMode.Burst ? d.ShotDamage * burst.BurstCount / (burst.BurstCount * interval + burst.BurstDelay) : d.ShotDamage / interval;
            var ratio = d.Dps / MathF.Max(1e-3f, linked);
            if (MathF.Abs(ratio - 1) > 0.005f)
            {
                changed++;
                Line($"  DPS change: {sb.Tier} '{sb.Name}' ({string.Join(" + ", g.Select(f => $"{f.Rpm} {f.FireType}"))}): {linked:0} -> {d.Dps:0} (x{ratio:0.00})");
            }
            if (ratio < 0.99f)
                Fail($"fire rates: '{sb.Name}' should never lose DPS from firing its receivers at their own rates ({linked:0} -> {d.Dps:0})");
        }
        Line($"standard builds: {volleys} volley builds fire their receivers at their own rates; {changed} change DPS (mixed rates or triggers)");
    }
}
