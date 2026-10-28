using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies box free for all" (board item 2026-10-27; Source/Game/ZombiesMode.cs, the mystery box):
/// the last part of a box offer (ZombiesRules.json box freeFraction / freeSeconds, the longer of the two) is anyone's.
/// Short seeded scenarios on Zombies Test: the window's length from the rules; another player sees "Reserved for" and
/// can't take it early, sees "Free for all" and can in the window; two takes in the same tick give it to one player and
/// nobody's hold turns into a spin; the buyer can still take it late (and still take it apart, which stays the buyer's);
/// a client sees the window from ZWorld; a co-op bot takes a free offer only when it would have taken it as its own.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesBoxGrab()
    {
        var now = 0.0;
        if (ZTestMap?.Zombies is not { } zm || zm.BoxSpots.Count == 0)
        {
            Fail("Zombies Test (with a mystery box) is missing");
            return;
        }

        // ---- The window from the rules: the longer of the fraction and the seconds, never more than the offer ----
        {
            var r = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
            var shipped = (r.BoxOfferSeconds, r.BoxFreeFraction, r.BoxFreeMinSeconds, r.BoxFreeSeconds);
            float W(float offer, float frac, float secs)
            {
                r.BoxOfferSeconds = offer;
                r.BoxFreeFraction = frac;
                r.BoxFreeMinSeconds = secs;
                return r.BoxFreeSeconds;
            }
            var a = W(10, 0.3f, 4);
            var b = W(20, 0.3f, 4);
            var c = W(3, 0.3f, 4);
            var off = W(10, 0, 0);
            ZLine($"box free window: shipped {shipped.BoxFreeSeconds:0.##} s of {shipped.BoxOfferSeconds:0.##} (fraction {shipped.BoxFreeFraction}, at least {shipped.BoxFreeMinSeconds} s); "
                  + $"10 s offer {a:0.##}, 20 s {b:0.##}, 3 s {c:0.##}, both 0 {off:0.##}");
            if (MathF.Abs(a - 4) > 1e-4 || MathF.Abs(b - 6) > 1e-4 || MathF.Abs(c - 3) > 1e-4 || off != 0 || shipped.BoxFreeSeconds <= 0)
                Fail("the box's free-for-all window should be the longer of freeFraction x offerSeconds and freeSeconds (at most the whole offer; both 0: none), and on in the shipped rules");
        }

        Vector3 AtBox(Game g, float side = 0) => zm.BoxSpots[Math.Clamp(g.BoxSpot, 0, zm.BoxSpots.Count - 1)].At + new Vector3(side, 0, -1.2f);

        // A spin by the buyer, run on to the offer: true when the box is offering a gun.
        bool Spin(Game g, Player buyer, ref double t)
        {
            g.SelfTestBoxMoveAfter = 1000; // (no teddy bear here)
            buyer.Teleport(AtBox(g));
            g.ZP(buyer).Points = 50000;
            g.SelfTestUse(buyer, t);
            ZTicks(g, ref t, g.ZRules.BoxSpinSeconds + 0.1);
            return g.BoxState == ZBoxState.Offering && g.BoxBuild >= 0 && g.BoxOwner == buyer.Id;
        }
        // Runs the offer on to <left> seconds before it ends.
        void Until(Game g, ref double t, double left)
        {
            var go = g.BoxLeft - left;
            if (go > 0)
                ZTicks(g, ref t, go);
        }
        string Name(Game g) => g.BoxBuild >= 0 ? StandardBuilds.All[g.BoxBuild].Name : "-";

        // ---- Another player: reserved early, free in the window; the take is theirs ----
        using (var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BotsBuy = false; }))
        {
            var me = g.Local!;
            var other = g.Players.Values.First(p => p != me);
            var free = g.ZRules.BoxFreeSeconds;
            var spun = Spin(g, me, ref now);
            var offer = Name(g);
            var otherPoints = g.ZP(other).Points;
            other.Teleport(AtBox(g, 0.4f));
            var early = g.FindZUse(other);
            g.SelfTestUse(other, now); // a hold on the reserved offer: refused
            var refusedEarly = g.BoxState == ZBoxState.Offering && Game.OwnsBuild(other, offer) < 0 && !g.BoxFreeForAll;
            Until(g, ref now, free + 0.25);
            var stillReserved = !g.BoxFreeForAll && g.FindZUse(other) is { Kind: ZUseKind.BoxTake, Blocked: true };
            Until(g, ref now, free - 0.25);
            var late = g.FindZUse(other);
            var mine = g.FindZUse(me);
            g.SelfTestUse(other, now);
            var took = Game.OwnsBuild(other, offer) >= 0 && Game.OwnsBuild(me, offer) < 0 && g.BoxState == ZBoxState.Idle && g.ZP(other).Points == otherPoints;
            ZLine($"box free for all: spun {spun} ('{offer}'), the other player early '{early?.Text}' (blocked {early?.Blocked}), refused {refusedEarly}, still reserved {free + 0.25:0.##} s before the end {stillReserved}; "
                  + $"{free - 0.25:0.##} s before: '{late?.Text}' (the buyer: '{mine?.Text}'), taken by the other player for nothing {took}");
            if (!spun || early is not { Kind: ZUseKind.BoxTake, Blocked: true } || early.Value.Text.IndexOf("Reserved for " + me.Name, StringComparison.Ordinal) < 0 || !refusedEarly || !stillReserved)
                Fail("before the box's free-for-all window another player should see 'Reserved for <buyer>' and not be able to take the offer");
            if (late is not { Kind: ZUseKind.BoxTake, Blocked: false } || !late.Value.Text.StartsWith("Free for all", StringComparison.Ordinal) || !took)
                Fail("in the box's free-for-all window another player should see 'Free for all' and be able to take the offer (for free)");
        }

        // ---- Two takes in the same tick: one player gets it, nobody's hold becomes a spin; the buyer can take it late ----
        using (var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BotsBuy = false; }))
        {
            var me = g.Local!;
            var other = g.Players.Values.First(p => p != me);
            var free = g.ZRules.BoxFreeSeconds;
            // The other player first in the tick (their hold reaches the buy time first: the first valid take wins).
            Spin(g, me, ref now);
            var offer = Name(g);
            Until(g, ref now, free - 0.5);
            me.Teleport(AtBox(g));
            other.Teleport(AtBox(g, 0.4f));
            g.ZP(me).Points = g.ZP(other).Points = 50000;
            var spins = g.BoxOfferSeq;
            g.ServerSetUse(me, true, now - 1);
            g.ServerSetUse(other, true, now - 1);
            g.SelfTestPerkTick(other, now);
            g.SelfTestPerkTick(me, now);
            for (var i = 0; i < 30; i++) // both keep holding: the closed box must not spin for either of them
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
            }
            var owners = (Game.OwnsBuild(me, offer) >= 0 ? 1 : 0) + (Game.OwnsBuild(other, offer) >= 0 ? 1 : 0);
            var noSpin = g.BoxState == ZBoxState.Idle && g.BoxOfferSeq == spins && g.ZP(me).Points == 50000 && g.ZP(other).Points == 50000;
            var winner = Game.OwnsBuild(other, offer) >= 0 ? "the other player" : Game.OwnsBuild(me, offer) >= 0 ? "the buyer" : "nobody";
            g.ServerSetUse(me, false, now);
            g.ServerSetUse(other, false, now);

            // The buyer, late in the window: still theirs to take (and to take apart; another player's G is refused).
            Spin(g, me, ref now);
            var offer2 = Name(g);
            Until(g, ref now, free - 0.5);
            other.Teleport(AtBox(g, 0.4f));
            var otherApart = g.BoxDisassembleRefusal(other);
            var buyerApart = g.BoxDisassembleRefusal(me);
            var buyerText = g.FindZUse(me)?.Text;
            g.SelfTestUse(me, now);
            var buyerTook = Game.OwnsBuild(me, offer2) >= 0 && g.BoxState == ZBoxState.Idle;
            ZLine($"box free for all, same tick: '{offer}' went to {winner} ({owners} owner), no spin from the held keys {noSpin}; the buyer late: '{buyerText}', took '{offer2}' {buyerTook}; "
                  + $"taking it apart in the window: the buyer '{buyerApart ?? "ok"}', the other player '{otherApart}'");
            if (owners != 1 || winner != "the other player" || !noSpin)
                Fail("two takes of a free box offer in the same tick should give it to one player (the first in the tick), and nobody's held key should spin the closed box");
            if (!buyerTook || buyerApart != null || otherApart == null)
                Fail("the buyer should still be able to take (or take apart) the offer in its free-for-all window; taking it apart stays the buyer's");
        }

        // ---- Both 0 in the rules: only the buyer, to the end ----
        using (var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BotsBuy = false; r.BoxFreeFraction = 0; r.BoxFreeMinSeconds = 0; }))
        {
            var me = g.Local!;
            var other = g.Players.Values.First(p => p != me);
            Spin(g, me, ref now);
            var offer = Name(g);
            Until(g, ref now, 0.3);
            other.Teleport(AtBox(g, 0.4f));
            g.SelfTestUse(other, now);
            var refused = Game.OwnsBuild(other, offer) < 0 && g.BoxState == ZBoxState.Offering && !g.BoxFreeForAll;
            ZLine($"box free for all off (both 0): another player refused 0.3 s before the end {refused}");
            if (!refused)
                Fail("with freeFraction and freeSeconds both 0 only the buyer should be able to take the box's offer");
        }

        // ---- A client: the window from ZWorld's box time ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            var w = new PacketWriter();
            byte[] Ents()
            {
                g.WriteZEnts(w);
                return w.Span.ToArray();
            }
            byte[] WorldMsg()
            {
                g.WriteZWorld(w);
                return w.Span.ToArray();
            }
            g.BoxState = ZBoxState.Offering;
            g.BoxBuild = Array.FindIndex(StandardBuilds.All, s => s.Sources.Contains("box"));
            g.BoxOwner = c.Local!.Id;
            g.BoxLeft = g.ZRules.BoxFreeSeconds + 2;
            c.SelfTestReadZombies(Ents(), WorldMsg());
            var before = c.BoxState == ZBoxState.Offering && !c.BoxFreeForAll;
            g.BoxLeft = g.ZRules.BoxFreeSeconds - 1;
            c.SelfTestReadZombies(Ents(), WorldMsg());
            var after = c.BoxFreeForAll;
            ZLine($"box free for all on a client: reserved with {g.ZRules.BoxFreeSeconds + 2:0.#} s left {before}, free with {g.ZRules.BoxFreeSeconds - 1:0.#} s left {after}; protocol {ServerInfo.ProtocolVersion}");
            if (!before || !after)
                Fail("a client should see the box's free-for-all window from ZWorld's box time (no wire change)");
        }

        // ---- A co-op bot: takes a free offer it would have taken as its own, never early, and leaves one it wouldn't ----
        foreach (var wants in new[] { true, false })
        {
            using var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BotReserve = 100000; }); // (the reserve: no other shopping)
            var me = g.Local!;
            var bot = g.Players.Values.First(p => p.Bot != null);
            bot.ImmuneUntil = me.ImmuneUntil = 1e9;
            var free = g.ZRules.BoxFreeSeconds;
            Spin(g, me, ref now);
            // The box's strongest gun (better than anything a bot starts with), or a melee build (a bot never takes one from the box).
            g.BoxBuild = wants
                ? Enumerable.Range(0, StandardBuilds.All.Length).Where(i => StandardBuilds.All[i].Sources.Contains("box") && !StandardBuilds.All[i].IsMelee)
                    .OrderByDescending(i => Game.AssembleCached(StandardBuilds.All[i].Build()).Def?.Dps ?? 0).ThenBy(i => i).First()
                : Array.FindIndex(StandardBuilds.All, s => s.IsMelee);
            var offer = Name(g);
            me.Teleport(AtBox(g, 3f)); // (out of the box's reach)
            var stand = zm.BoxSpots[g.BoxSpot].At + World.FlatForward(zm.BoxSpots[g.BoxSpot].Yaw) * 1.2f;
            bot.Teleport(stand + new Vector3(0.8f, 0, 0));
            bot.Bot!.HoldAnchor = bot.Position;
            bool Has() => Game.OwnsBuild(bot, offer) >= 0 || bot.MeleeBuild?.Name == offer;
            var tookEarly = false;
            var tookAt = -1.0;
            for (var i = 0; i < (g.ZRules.BoxOfferSeconds + 1) * World.TickRate && g.BoxState == ZBoxState.Offering; i++)
            {
                var left = g.BoxLeft;
                Run(g, ref now, 1.0 / World.TickRate);
                if (Has() && tookAt < 0)
                {
                    tookAt = left;
                    tookEarly = left > free + 0.05;
                }
            }
            var botHas = Has();
            ZLine($"box free for all, a co-op bot ({(wants ? "a better gun" : "one it wouldn't take")} '{offer}'): took it {botHas} ({(tookAt >= 0 ? $"{tookAt:0.00} s before the end" : "-")}), early {tookEarly}, the box {g.BoxState}");
            if (tookEarly || botHas != wants)
                Fail(wants ? "a co-op bot should take a better gun from someone else's box offer in its free-for-all window (and not before)" : "a co-op bot should leave a free box offer it wouldn't have taken as its own");
        }
    }
}
