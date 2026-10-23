using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Blast push" (Source/Game/Knockback.cs): every explosion shoves whoever it doesn't kill. Short
/// scenarios on a TDM server with a survivor (health raised so the blast can't kill): a frag, an impact grenade, every
/// launcher, a landmine, an exploding barrel and a mortar shell each throw them clearly away and off the ground, and the
/// stagger keeps the ground from cancelling it; Flak Jacket and prone cut it, the floor blocks it, a teammate isn't
/// pushed; a bot is thrown and recovers; the feedback (shake, view kick) fires for the host's own player; nothing allocates.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> PushNotes = new();

    private static void PLine(string s)
    {
        Line(s);
        PushNotes.Add(s);
    }

    private static void CheckBlastPush()
    {
        PushNotes.Clear();
        try
        {
            BlastPushCurve();
            BlastPushScenarios();
        }
        finally
        {
            World.SetMap(Maps.All[0]);
            Line("::notice title=Blast push::" + string.Join("%0A", PushNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    /// <summary>The push curve: survivors near the edge still get a real shove with lift; the stagger window.</summary>
    private static void BlastPushCurve()
    {
        var frag = Throwables.Get(ThrowableKind.Frag);
        // Chest 1.2 m above a grenade on the floor, at a range of distances.
        var parts = new List<string>();
        var prev = float.MaxValue;
        foreach (var d in new[] { 3f, 5f, 6.5f, 8f, 8.8f })
        {
            var v = Knockback.Impulse(Vector3.Zero, frag.Radius, frag.Knockback, new Vector3(d, 1.2f, 0), false, false);
            var flat = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
            parts.Add($"{d} m: {flat:0.0} away / {v.Y:0.0} up");
            if (!(v.Length() < prev) || v.Y < Knockback.LiftBase || flat < 0.5f || v.X <= 0)
                Fail($"blast push: the frag's push at {d} m is wrong ({v})");
            prev = v.Length();
        }
        PLine($"frag push (m/s) by distance: {string.Join(", ", parts)}; stagger {Knockback.StaggerTicks} ticks (steering {Knockback.StaggerGroundControl}/s ground, {Knockback.StaggerAirControl}/s air)");
        var edge = Knockback.Impulse(Vector3.Zero, frag.Radius, frag.Knockback, new Vector3(8.8f, 1.2f, 0), false, false);
        if (!Knockback.Staggers(edge) || edge.Y < 2f)
            Fail("blast push: a near miss at the edge of a frag should still hop and stagger you");
        if (Knockback.StaggerAt(10, 10) != Knockback.StaggerTicks || Knockback.StaggerAt(10, 12) != Knockback.StaggerTicks - 2
            || Knockback.StaggerAt(10, 10 + Knockback.StaggerTicks) != 0 || Knockback.StaggerAt(11, 10) != 0)
            Fail("blast push: StaggerAt is wrong");
        // The stagger counts down once per input, mantling or not.
        var ms = new MoveState { Grounded = true };
        Knockback.Apply(ref ms, new Vector3(5, 4, 0));
        var tiny = new MoveState { Grounded = true };
        Knockback.Apply(ref tiny, new Vector3(0.5f, 0.3f, 0));
        if (ms.KnockTicks != Knockback.StaggerTicks || ms.Grounded || tiny.KnockTicks != 0 || !tiny.Grounded)
            Fail("blast push: Apply's stagger / lift-off is wrong");
    }

    private static void BlastPushScenarios()
    {
        var savedMover = World.Mover;
        try
        {
            foreach (var mover in new IMover[] { ClassicMover.Instance, SweptMover.Instance })
            {
                World.Mover = mover;
                BlastPushOn(mover.Mode);
            }
        }
        finally
        {
            World.Mover = savedMover;
        }
    }

    private static void BlastPushOn(PhysicsMode mode)
    {
        using var game = ModeGame(3, GameMode.TeamDeathmatch);
        var me = game.Players[0];
        var mate = game.Players.Values.First(p => p != me && p.Team == me.Team);
        var foes = game.Players.Values.Where(p => p.Team != me.Team).ToList();
        var foe = foes[0];
        var bot = foes[1];
        var spot = World.Map.Spawns.Count > 0 ? World.Map.Spawns[0] : Vector3.Zero;
        double now = 1;

        // A direction with open, level ground: 10 m clear behind the victim (where it flies) and 4 m on the blast side.
        game.SelfTestPlace(foe, spot, 0);
        var away = Vector3.UnitX;
        for (var k = 0; k < 16; k++)
        {
            var dir = World.FlatForward(k * MathF.PI / 8);
            if (World.RaycastWorld(foe.Chest, dir, 10f) >= 10f && World.RaycastWorld(foe.Chest, -dir, 4f) >= 4f
                && World.RaycastWorld(foe.Position + new Vector3(0, 0.3f, 0), dir, 6f) >= 6f)
            {
                away = dir;
                break;
            }
        }

        void Clear(Player victim, PerkSet perks)
        {
            foreach (var p in game.Players.Values)
            {
                game.SelfTestPerks(p, PerkSet.Empty);
                game.SelfTestPlace(p, spot + new Vector3(40 + p.Id * 4, 0, 40), 0); // out of the way
            }
            game.SelfTestPerks(victim, perks);
            game.SelfTestPlace(victim, spot, 0);
            var s = victim.Sim;
            for (var k = 0; k < 20; k++)
                World.Simulate(ref s, new InputCmd { Seq = 0 }); // settle on the ground
            s.Velocity = Vector3.Zero;
            s.KnockTicks = 0;
            victim.Sim = s;
            victim.Health = 100000; // a survivor, whatever hits it
        }

        // One blast on a survivor: how far it's thrown away (m) and how high it rises (m) over the next second of no input.
        (float Away, float Rise, bool Alive, Vector3 Push) Trial(Player victim, PerkSet perks, Stance stance, Action<Vector3> blast, float range, float height = 0.1f)
        {
            Clear(victim, perks);
            if (stance != Stance.Stand)
                victim.Sim.Stance = stance;
            var start = victim.Position;
            var knocks = game.KnocksApplied;
            blast(start - away * range + new Vector3(0, height, 0));
            var push = game.KnocksApplied > knocks ? game.LastKnock : Vector3.Zero;
            var alive = victim.Alive;
            var rise = 0f;
            var s = victim.Sim;
            var buttons = stance == Stance.Prone ? Buttons.Prone : Buttons.None;
            for (var k = 0; k < World.TickRate; k++)
            {
                World.Simulate(ref s, new InputCmd { Seq = (uint)k + 1, Buttons = buttons });
                rise = MathF.Max(rise, s.Position.Y - start.Y);
            }
            victim.Sim = s;
            return (Vector3.Dot(s.Position - start, away), rise, alive, push);
        }

        foe.Bot = null; // (scripted: no AI steering it back)
        var notes = new List<string>();
        void Expect(string name, (float Away, float Rise, bool Alive, Vector3 Push) r, float minAway, float minRise = 0.15f)
        {
            notes.Add($"{name} {r.Away:0.0}/{r.Rise:0.00}");
            if (!r.Alive)
                Fail($"blast push ({mode}): the {name} killed the survivor");
            else if (!(r.Away >= minAway) || !(r.Rise >= minRise))
                Fail($"blast push ({mode}): the {name} threw a survivor only {r.Away:0.00} m away and {r.Rise:0.00} m up (want at least {minAway} m and {minRise} m; push {r.Push})");
        }

        var frag = Trial(foe, PerkSet.Empty, Stance.Stand, c => game.SelfTestDetonate(me, ThrowableKind.Frag, c, now), 3f);
        Expect("frag at 3 m", frag, 2.5f, 0.3f);
        Expect("frag at 7 m", Trial(foe, PerkSet.Empty, Stance.Stand, c => game.SelfTestDetonate(me, ThrowableKind.Frag, c, now), 7f), 0.8f, 0.1f);
        Expect("impact at 3 m", Trial(foe, PerkSet.Empty, Stance.Stand, c => game.SelfTestDetonate(me, ThrowableKind.Impact, c, now), 3f), 1.5f);
        foreach (var w in Weapons.All.Where(w => w.IsLauncher))
        {
            var range = MathF.Min(3f, w.LauncherRadius * 0.5f);
            game.SelfTestRefillLauncher(me);
            Expect($"{w.Name} at {range:0.#} m", Trial(foe, PerkSet.Empty, Stance.Stand, c => game.SelfTestLauncherBlast(me, w, c, now), range, 0.3f), 1.2f);
        }
        if (Props.Find("Landmine") is { Breakable: { } mine })
            Expect("landmine at 2.5 m", Trial(foe, PerkSet.Empty, Stance.Stand, c => game.SelfTestPropExplode(c + new Vector3(0, 0.1f, 0), mine, null, now), 2.5f), 1.5f);
        else
            Fail("blast push: no Landmine prop");
        var barrelDef = Props.All.FirstOrDefault(p => p.Breakable is { ExplodeRadius: > 0, IsMine: false } && p.Name.Contains("Barrel", StringComparison.OrdinalIgnoreCase))
                        ?? Props.All.FirstOrDefault(p => p.Breakable is { ExplodeRadius: > 0, IsMine: false });
        if (barrelDef is { Breakable: { } barrel })
            Expect($"{barrelDef.Name} at 3 m", Trial(foe, PerkSet.Empty, Stance.Stand, c => game.SelfTestPropExplode(c + new Vector3(0, 0.5f, 0), barrel, me, now), 3f), 1.2f);
        else
            Fail("blast push: no exploding barrel prop");
        var mortarRadius = KillStreaks.BlastRadiusOf(StreakId.MortarBarrage);
        Expect("mortar at 3 m", Trial(foe, PerkSet.Empty, Stance.Stand, c => game.SelfTestStreakBlast(me, c, mortarRadius, 1f, now), 3f), 1.5f);
        PLine($"[{mode}] survivor thrown (m away / m up, 1 s): {string.Join(", ", notes)}");

        // Fairness: Flak Jacket and prone cut it; the floor blocks it; a teammate isn't pushed; your own grenade pushes you.
        var flak = Trial(foe, PerkSet.Empty.With(PerkCategory.Survival, PerkId.FlakJacket), Stance.Stand, c => game.SelfTestDetonate(me, ThrowableKind.Frag, c, now), 3f);
        var prone = Trial(foe, PerkSet.Empty, Stance.Prone, c => game.SelfTestDetonate(me, ThrowableKind.Frag, c, now), 3f);
        var floor = Trial(foe, PerkSet.Empty, Stance.Stand, c => game.SelfTestDetonate(me, ThrowableKind.Frag, c, now), 0f, -World.HeightOf(Stance.Stand) * 0.65f - 1.5f);
        var mateRun = Trial(mate, PerkSet.Empty, Stance.Stand, c => game.SelfTestDetonate(me, ThrowableKind.Frag, c, now), 3f);
        var self = Trial(me, PerkSet.Empty, Stance.Stand, c => game.SelfTestDetonate(me, ThrowableKind.Frag, c, now), 3f);
        PLine($"[{mode}] frag at 3 m: plain {frag.Push.Length():0.0} m/s ({frag.Away:0.0} m), Flak Jacket {flak.Push.Length():0.0} ({flak.Away:0.0} m), prone {prone.Push.Length():0.0} ({prone.Away:0.0} m); "
              + $"floor between {floor.Push.Length():0.0}; teammate {mateRun.Push.Length():0.0} ({mateRun.Away:0.00} m); yourself {self.Push.Length():0.0} ({self.Away:0.0} m)");
        if (MathF.Abs(flak.Push.Length() - frag.Push.Length() * Perks.FlakKnockMult) > 0.05f || !(flak.Away < frag.Away * 0.7f))
            Fail($"blast push ({mode}): Flak Jacket doesn't cut the push");
        if (!(prone.Push.Length() < frag.Push.Length() * 0.6f) || !(prone.Away < frag.Away))
            Fail($"blast push ({mode}): prone doesn't cut the push");
        if (floor.Push != Vector3.Zero || MathF.Abs(floor.Away) > 0.05f)
            Fail($"blast push ({mode}): a blast under the floor pushed");
        if (mateRun.Push != Vector3.Zero || MathF.Abs(mateRun.Away) > 0.05f)
            Fail($"blast push ({mode}): a teammate was pushed");
        if (!(self.Away > 1f))
            Fail($"blast push ({mode}): your own grenade didn't push you");
        if (game.KnockJolt < 0.5f)
            Fail($"blast push ({mode}): the host's own push gave no feedback (shake / view kick)");

        // Without the stagger the ground would have cancelled most of it: the same push, steering at full strength.
        Clear(foe, PerkSet.Empty);
        var plain = foe.Sim;
        var plainStart = plain.Position;
        Knockback.Apply(ref plain, frag.Push);
        plain.KnockTicks = 0;
        for (var k = 0; k < World.TickRate; k++)
            World.Simulate(ref plain, new InputCmd { Seq = (uint)k + 1 });
        var unstaggered = Vector3.Dot(plain.Position - plainStart, away);
        PLine($"[{mode}] the same frag push without the stagger: {unstaggered:0.0} m (with it {frag.Away:0.0} m)");
        if (!(frag.Away > unstaggered + 0.3f))
            Fail($"blast push ({mode}): the stagger doesn't carry the push further");

        // A bot is thrown too, and picks itself up (2 s of the real server loop).
        Clear(bot, PerkSet.Empty);
        var botStart = bot.Position;
        var botKnocks = game.KnocksApplied;
        game.SelfTestDetonate(me, ThrowableKind.Frag, botStart - away * 3f + new Vector3(0, 0.1f, 0), now);
        var botPushed = game.KnocksApplied > botKnocks;
        var botAir = false;
        var botFar = 0f;
        var botLanded = false;
        for (var k = 0; k < 2 * World.TickRate; k++)
        {
            now += World.TickDt;
            game.Update(now, World.TickDt);
            if (k < 30)
            {
                botAir |= !bot.Sim.Grounded;
                botFar = MathF.Max(botFar, Vector3.Dot(bot.Position - botStart, away));
            }
            else
                botLanded |= bot.Sim.Grounded;
        }
        PLine($"[{mode}] bot: pushed {botPushed}, airborne {botAir}, {botFar:0.0} m away in 0.5 s; after 2 s alive {bot.Alive}, landed {botLanded}, stagger {bot.Sim.KnockTicks}");
        if (!botPushed || !botAir || !(botFar > 1f))
            Fail($"blast push ({mode}): a bot wasn't thrown");
        else if (bot.Alive && (bot.Sim.KnockTicks != 0 || !botLanded))
            Fail($"blast push ({mode}): a bot didn't recover from the push");

        // No allocations: the push maths, Apply and the server's knockback pass.
        Clear(foe, PerkSet.Empty);
        var center = foe.Position - away * 3f;
        for (var k = 0; k < 10; k++)
            game.SelfTestBlastKnockback(me, center, 9f, 11f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sink = Vector3.Zero;
        for (var k = 0; k < 500; k++)
        {
            var ms = new MoveState();
            var v = Knockback.Impulse(center, 9f, 11f, foe.Chest, false, false);
            Knockback.Apply(ref ms, v);
            sink += ms.Velocity;
            foe.Sim.Velocity = Vector3.Zero;
            game.SelfTestBlastKnockback(me, center, 9f, 11f);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes > 0)
            Fail($"blast push ({mode}): the push allocates ({bytes} bytes over 500 blasts)");
        _ = sink;
    }
}
