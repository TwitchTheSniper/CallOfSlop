using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies drop items" (Source/Game/ZombiesDropItems.cs; docs/zombies/ZOMBIES-MODE.md, "Dropping
/// things"): a player drops the gun in their hands (never their last gun) and a teammate picks it up; loose parts drop
/// from the inventory (the amount checked) and a teammate picks them up; two players grabbing at once (and two claims of
/// one pickup) never duplicate anything; co-op bots ignore pickups; the per-map cap refuses without losing anything;
/// drops despawn at the end of the next round and after the set time; and a client's copy follows every message.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesDropItems()
    {
        if (ZTestMap is not { } map || map.Zombies is null)
        {
            Fail("drop items: the Zombies Test map is missing");
            return;
        }
        var now = 0.0;
        using var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.MaxPartDrops = 4; });
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var mate = g.SelfTestAddHuman();
        g.SelfTestSpawn(mate);
        mate.ImmuneUntil = 1e9;
        var bot = g.Players.Values.First(p => p.Bot != null);
        var w = new PacketWriter();
        var spot = new Vector3(2, 0, -3); // a spawn point in the start room
        mate.Teleport(spot);
        mate.Yaw = 0;
        mate.Pitch = 0;
        me.Teleport(new Vector3(6, 0, 3));
        bot.Teleport(new Vector3(-4, 0, 3));

        // ---- The gun in your hands ----------------------------------------------------------------------
        g.GiveBuild(mate, 0, StandardBuilds.Find("Minigun")!.Build(), full: true);
        g.GiveBuild(mate, 1, StandardBuilds.Find("Wall Rifle")!.Build(), full: true);
        mate.Weapon = mate.Primary;
        var zm = g.ZP(mate);
        int Minis() => g.Players.Values.Count(p => g.ZP(p).Slots.Any(b => b?.Name == "Minigun")) + g.ZWeaponDrops.Count(d => d.Build.Name == "Minigun");
        Game.WriteDropRequest(w, Game.DropGun, "", 1);
        var why = g.SelfTestDropPacket(mate, w.Span, now);
        var gun = g.ZWeaponDrops.FirstOrDefault(d => d.Build.Name == "Minigun");
        var inFront = gun != null && Vector3.Dot(Vector3.Normalize(gun.At - spot), World.FlatForward(mate.Yaw)) > 0.8f;
        var dropped = why == null && gun != null && zm.Slots[0] == null && !Game.HasGunIn(mate, 0) && Game.SlotOf(mate, mate.Weapon) == 1
                      && gun.Owner == mate.Id && !gun.Full && gun.ExpireRound == g.ZRound + 1 && Minis() == 1
                      && !World.Blocked(gun.At + new Vector3(0, 0.02f, 0)) && Vector3.Distance(gun.At, spot) < 1.2f;
        Line($"drop gun: '{why ?? "ok"}', Minigun on the floor {gun != null} ({(gun != null ? Vector3.Distance(gun.At, spot) : 0):0.00} m away, in front {inFront}), slot 1 empty {zm.Slots[0] == null}, now holding slot {Game.SlotOf(mate, mate.Weapon) + 1}, lasts to round {gun?.ExpireRound}, {Minis()} Minigun in the world");
        if (!dropped || !inFront)
            Fail("drop items: the drop key should put the gun in your hands on the floor in front of you (and out of your slot)");

        // Never your last gun: the Wall Rifle stays.
        Game.WriteDropRequest(w, Game.DropGun, "", 1);
        why = g.SelfTestDropPacket(mate, w.Span, now);
        var kept = why != null && why.Contains("last gun") && zm.Slots[1]?.Name == "Wall Rifle" && g.ZWeaponDrops.Count == 1;
        Line($"last gun: '{why}', Wall Rifle kept {zm.Slots[1]?.Name == "Wall Rifle"}");
        if (!kept)
            Fail("drop items: dropping your last gun should be refused");

        // A teammate picks it up (the host: slots full, so it swaps with the gun in hand).
        me.Weapon = me.Primary;
        me.Teleport(gun!.At);
        var prompt = g.FindZUse(me);
        g.SelfTestUse(me, now);
        var picked = Game.OwnsBuild(me, "Minigun") >= 0 && Minis() == 1 && g.ZWeaponDrops.All(d => d.Build.Name != "Minigun");
        Line($"teammate pickup: '{prompt?.Text}' -> host has the Minigun {Game.OwnsBuild(me, "Minigun") >= 0}, {Minis()} in the world, {g.ZWeaponDrops.Count} build(s) on the floor");
        if (prompt?.Kind != ZUseKind.Pickup || !picked)
            Fail("drop items: a teammate should pick up a dropped gun with the interact key (no copy left behind)");
        foreach (var d in g.ZWeaponDrops.ToList())
            g.SelfTestClaim(mate, d.Id, gun: true, now); // (tidy: the host's swapped-out gun back to the mate's free slot)

        // ---- Loose parts ----------------------------------------------------------------------------------
        var partA = Parts.All.First(p => p.Rarity == Rarity.Rare);
        var partB = Parts.All.First(p => p.Rarity == Rarity.Common && p.Name != partA.Name);
        zm.Parts.Clear();
        zm.Parts[partA.Name] = 5;
        zm.Parts[partB.Name] = 8;
        g.ZP(me).Parts.Clear();
        int Total(PartDef part) => g.Players.Values.Sum(p => g.ZP(p).Parts.GetValueOrDefault(part.Name)) + g.ZPartDrops.Where(d => d.Part == part).Sum(d => d.Count);
        mate.Teleport(spot);
        Game.WriteDropRequest(w, Game.DropParts, partA.Name, 3);
        why = g.SelfTestDropPacket(mate, w.Span, now);
        var pd = g.ZPartDrops.FirstOrDefault();
        var partsOk = why == null && pd != null && pd.Part == partA && pd.Count == 3 && zm.Parts.GetValueOrDefault(partA.Name) == 2 && Total(partA) == 5
                      && pd.Owner == mate.Id && pd.ExpireRound == g.ZRound + 1;
        string? Refusal(string part, int n)
        {
            Game.WriteDropRequest(w, Game.DropParts, part, n);
            return g.SelfTestDropPacket(mate, w.Span, now);
        }
        var tooMany = Refusal(partA.Name, 3);
        var none = Refusal(partA.Name, 0);
        var unknown = Refusal("No Such Part 123", 1);
        var refusedOk = tooMany != null && none != null && unknown != null && zm.Parts.GetValueOrDefault(partA.Name) == 2 && g.ZPartDrops.Count == 1;
        Line($"drop parts: 3 of 5 {partA.Name} ({partA.Rarity}) -> '{why ?? "ok"}', {zm.Parts.GetValueOrDefault(partA.Name)} left, a pickup of {pd?.Count}; refused: 3 more '{tooMany}', 0 '{none}', unknown '{unknown}'");
        if (!partsOk || !refusedOk)
            Fail("drop items: dropping loose parts should take exactly that many from you into one pickup, and refuse what you don't have");

        // The prompt names it with its rarity; a co-op bot standing on it is never offered it and can't claim it.
        me.Teleport(pd!.At);
        var partPrompt = g.FindZUse(me);
        bot.Teleport(pd.At);
        var botUse = g.FindZUse(bot);
        var botClaim = g.SelfTestClaim(bot, pd.Id, gun: false, now);
        Line($"part prompt: '{partPrompt?.Text}'; a bot on it is offered {botUse?.Kind.ToString() ?? "nothing"}, its claim {(botClaim ? "WORKED" : "refused")}");
        if (partPrompt?.Kind != ZUseKind.PartPickup || !partPrompt.Value.Text.Contains(partA.Name) || !partPrompt.Value.Text.Contains(PartDef.RarityName(partA.Rarity))
            || botUse?.Kind is ZUseKind.PartPickup or ZUseKind.Pickup || botClaim || g.ZPartDrops.Count != 1)
            Fail("drop items: the part pickup should prompt with its name and rarity, and co-op bots should ignore pickups");
        bot.Teleport(new Vector3(-4, 0, 3));

        // Two players grab it in the same tick: one gets all 3, the other nothing; nothing is duplicated.
        mate.Teleport(pd.At);
        g.ServerSetUse(me, true, now - 1);
        g.ServerSetUse(mate, true, now - 1);
        now += World.TickDt;
        g.SelfTestZombiesTick(now);
        g.ServerSetUse(me, false, now);
        g.ServerSetUse(mate, false, now);
        var gotMe = g.ZP(me).Parts.GetValueOrDefault(partA.Name);
        var gotMate = zm.Parts.GetValueOrDefault(partA.Name) - 2;
        var simultaneous = g.ZPartDrops.Count == 0 && ((gotMe == 3 && gotMate == 0) || (gotMe == 0 && gotMate == 3)) && Total(partA) == 5;
        Line($"same-tick grab: host +{gotMe}, mate +{gotMate}, {g.ZPartDrops.Count} left on the floor, {Total(partA)} {partA.Name} in the world (was 5)");
        if (!simultaneous)
            Fail("drop items: two players grabbing one pickup at once should give it to exactly one of them");

        // Two claims of one pickup: the first wins.
        Game.WriteDropRequest(w, Game.DropParts, partB.Name, 2);
        g.SelfTestDropPacket(mate, w.Span, now);
        var second = g.ZPartDrops.Single();
        var first = g.SelfTestClaim(me, second.Id, gun: false, now);
        var again = g.SelfTestClaim(mate, second.Id, gun: false, now);
        Line($"double claim: first {first}, second {again}, {partB.Name}: host {g.ZP(me).Parts.GetValueOrDefault(partB.Name)}, mate {zm.Parts.GetValueOrDefault(partB.Name)} (8 in all: {Total(partB)})");
        if (!first || again || Total(partB) != 8 || g.ZP(me).Parts.GetValueOrDefault(partB.Name) != 2)
            Fail("drop items: the first claim of a pickup should win and the second find nothing");

        // ---- The cap: 4 part pickups on this test's map; the 5th is refused and the parts stay yours -------------
        mate.Teleport(spot);
        for (var i = 0; i < 4; i++)
        {
            Game.WriteDropRequest(w, Game.DropParts, partB.Name, 1);
            g.SelfTestDropPacket(mate, w.Span, now);
        }
        var before = zm.Parts.GetValueOrDefault(partB.Name);
        Game.WriteDropRequest(w, Game.DropParts, partB.Name, 1);
        var capped = g.SelfTestDropPacket(mate, w.Span, now);
        Line($"cap: {g.ZPartDrops.Count} part pickups (cap {g.ZRules.MaxPartDrops}), the next '{capped}', {partB.Name} kept {zm.Parts.GetValueOrDefault(partB.Name) == before}");
        if (g.ZPartDrops.Count != 4 || capped == null || zm.Parts.GetValueOrDefault(partB.Name) != before || Total(partB) != 8)
            Fail("drop items: past the per-map cap a drop should be refused, losing nothing");

        // ---- A client's copy: every pickup appears with the same data, and goes ------------------------------
        {
            var serverParts = g.ZPartDrops.ToList();
            var msgs = serverParts.Select(d =>
            {
                var mw = new PacketWriter();
                Game.WritePartDrop(mw, d, d.Until - now);
                return mw.Span.ToArray();
            }).ToList();
            g.GiveBuild(mate, 0, StandardBuilds.Find("Auto Shotgun")!.Build(), full: true);
            mate.Weapon = mate.Primary;
            g.SelfTestDropPacket(mate, DropGunPacket(), now);
            var wd = g.ZWeaponDrops.Single(d => d.Build.Name == "Auto Shotgun");
            var ww = new PacketWriter();
            Game.WriteWeaponDrop(ww, wd, wd.Until - now);
            var wmsg = ww.Span.ToArray();
            var snow = now;
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            World.SetMap(map);
            var cnow = 5000.0;
            foreach (var m in msgs)
                c.SelfTestReadPartDrop(m, cnow);
            c.SelfTestReadWeaponDrop(wmsg, cnow);
            var same = c.ZPartDrops.Count == serverParts.Count && serverParts.All(s => c.ZPartDrops.FirstOrDefault(x => x.Id == s.Id) is { } cd && cd.At == s.At && cd.Part.Name == s.Part.Name
                && cd.Count == s.Count && cd.Owner == s.Owner && cd.OwnerName == s.OwnerName && cd.ExpireRound == s.ExpireRound && Math.Abs((cd.Until - cnow) - (s.Until - snow)) < 0.01);
            var cw = c.ZWeaponDrops.SingleOrDefault();
            var gunSame = cw != null && cw.ExpireRound == wd.ExpireRound && cw.Rarity == wd.Rarity && cw.Build.ToText() == wd.Build.ToText();
            var prompted = false;
            if (c.Local is { } cl && c.ZPartDrops.Count > 0)
            {
                cl.Teleport(c.ZPartDrops[0].At);
                prompted = c.FindZUse(cl)?.Kind is ZUseKind.PartPickup or ZUseKind.Pickup;
            }
            var gw = new PacketWriter();
            Game.WritePartDropGone(gw, serverParts[0].Id, me.Id);
            c.SelfTestReadPartDrop(gw.Span, cnow);
            var gone = c.ZPartDrops.Count == serverParts.Count - 1 && c.ZPartDrops.All(x => x.Id != serverParts[0].Id);
            Line($"client sync: {msgs.Count} part pickups ({string.Join("/", msgs.Select(m => m.Length))} bytes) same {same}, the dropped gun's last round and rarity {gunSame} ({wd.Rarity}), prompt {prompted}, removal {gone}; protocol {ServerInfo.ProtocolVersion}, Msg.ZPartDrop {(byte)Msg.ZPartDrop}");
            if (!same || !gunSame || !prompted || !gone)
                Fail("drop items: pickups didn't replicate to a client (appear, prompt, go)");
        }
        World.SetMap(map);

        // ---- Despawn: the end of the round after the drop; and the set time ---------------------------------------
        var round = g.ZRound; // (0: before round 1, so they last to the end of round 1)
        var guns = g.ZWeaponDrops.Count(d => d.ExpireRound > 0);
        g.SelfTestBeginRound(round + 1, now);
        var duringRound = g.ZPartDrops.Count;
        g.SelfTestEndRound(now);
        var afterRound = g.ZPartDrops.Count + g.ZWeaponDrops.Count(d => d.ExpireRound > 0);
        Line($"despawn by round: dropped before round {round + 1}: {duringRound} part pickups during it, {afterRound} left after it ends ({guns} gun(s) too)");
        if (duringRound != 4 || guns == 0 || afterRound != 0)
            Fail("drop items: things dropped on purpose should last through the next round and despawn when it ends");
        mate.Teleport(spot);
        Game.WriteDropRequest(w, Game.DropParts, partB.Name, 1);
        g.SelfTestDropPacket(mate, w.Span, now);
        var timed = g.ZPartDrops.Single();
        g.SelfTestZombiesTick(timed.Until - World.TickDt);
        var still = g.ZPartDrops.Count;
        g.SelfTestZombiesTick(timed.Until);
        Line($"despawn by time: {still} one tick before {g.ZRules.PlayerDropSeconds:0} s, {g.ZPartDrops.Count} at it (expires with round {timed.ExpireRound} otherwise)");
        if (still != 1 || g.ZPartDrops.Count != 0 || timed.ExpireRound != g.ZRound + 1)
            Fail("drop items: a part pickup should despawn exactly at its set time");

        // ---- Keys: the two new actions have their own default keys (G stays the grenade key) ------------------------
        var d2 = Keybinds.Defaults;
        Line($"keys: Drop item {Keybinds.Name(d2[(int)GameAction.DropItem])}, Parts inventory {Keybinds.Name(d2[(int)GameAction.Inventory])}, Throw grenade {Keybinds.Name(d2[(int)GameAction.Grenade])}");
        if (d2[(int)GameAction.DropItem] == d2[(int)GameAction.Grenade] || d2[(int)GameAction.DropItem] == Keybinds.Unbound || d2[(int)GameAction.Inventory] == Keybinds.Unbound)
            Fail("drop items: the drop and inventory keys need their own defaults");
    }

    private static byte[] DropGunPacket()
    {
        var w = new PacketWriter();
        Game.WriteDropRequest(w, Game.DropGun, "", 1);
        return w.Span.ToArray();
    }
}
