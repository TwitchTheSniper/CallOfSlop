using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test for taking builds apart at a Zombies workbench (part of the "Zombies mode" section; docs/zombies/ZOMBIES-MODE.md,
/// "Disassembling a build"): a multi-receiver build gives back exactly its parts and leaves its slot empty, wall and box
/// builds and the melee build come apart the same way, part counts are conserved, the gun in hand moves on (to empty
/// hands with no gun left, which must not break anything), the checks (at a bench, your slot, something in it), one
/// Undo, and the co-op round trip (ZBuild cleared, ZRun, a joiner's sync) leaving a client with the server's state.
/// </summary>
public static partial class SelfTest
{
    private static Dictionary<string, int> DisParts(WeaponBuild? b)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (b != null)
            foreach (var n in b.Nodes)
                d[n.Part.Name] = d.GetValueOrDefault(n.Part.Name) + 1;
        return d;
    }

    /// <summary>Every part a player owns: loose plus on the builds in their slots.</summary>
    private static Dictionary<string, int> DisOwned(ZPlayer zp)
    {
        var d = new Dictionary<string, int>(zp.Parts, StringComparer.OrdinalIgnoreCase);
        foreach (var b in zp.Slots)
            foreach (var (name, n) in DisParts(b))
                d[name] = d.GetValueOrDefault(name) + n;
        return d;
    }

    private static bool DisSame(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b) =>
        a.Where(kv => kv.Value != 0).All(kv => b.GetValueOrDefault(kv.Key) == kv.Value) && b.Where(kv => kv.Value != 0).All(kv => a.GetValueOrDefault(kv.Key) == kv.Value);

    /// <summary>The loose parts gained between two snapshots.</summary>
    private static Dictionary<string, int> DisGained(IReadOnlyDictionary<string, int> before, IReadOnlyDictionary<string, int> after)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var n = after.GetValueOrDefault(name) - before.GetValueOrDefault(name);
            if (n != 0)
                d[name] = n;
        }
        return d;
    }

    private static void CheckZombieDisassemble(ZombiesMap zm, ref double now)
    {
        var first = ZNotes.Count;
        try
        {
            CheckZombieDisassembleBody(zm, ref now);
        }
        finally
        {
            // Its own annotation (the Zombies results one is too long to show these).
            Line("::warning title=Zombies disassemble (results)::" + string.Join("%0A", ZNotes.Skip(first).Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static void CheckZombieDisassembleBody(ZombiesMap zm, ref double now)
    {
        var guns = StandardBuilds.All.Where(s => !s.IsMelee).ToList();
        var multi = guns.OrderByDescending(s => s.Build().Receivers.Count).ThenBy(s => s.Name).First();
        var wall = guns.FirstOrDefault(s => s.Sources.Contains("wall") && s != multi);
        var box = guns.FirstOrDefault(s => s.Sources.Contains("box") && s != multi && s != wall);
        if (multi.Build().Receivers.Count < 2 || wall == null || box == null)
        {
            Fail($"disassemble: needs a standard build with 2+ receivers ({multi.Name}: {multi.Build().Receivers.Count}), a wall build and a box build");
            return;
        }

        // ---- The host takes everything apart, slot by slot ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var zp = g.ZP(me);
            me.Teleport(zm.Benches[0].At + new Vector3(0, 0, -1.2f));
            var mustBeYours = g.ServerBench(me, Game.BenchDisassemble, 2, ""); // slots 3-4 before the upgrade
            me.ExtraSlots = true;
            g.GiveBuild(me, 2, wall.Build(), full: true);
            g.GiveBuild(me, 3, box.Build(), full: true);
            g.GiveBuild(me, 0, multi.Build(), full: true); // slot 1, in hand
            zp.Parts["Red Dot"] = 2; // loose parts already there stay as they are
            var owned = DisOwned(zp);
            var inHandBefore = g.ActiveSlot;

            // 1. The multi-receiver build in hand.
            var loose0 = new Dictionary<string, int>(zp.Parts, StringComparer.OrdinalIgnoreCase);
            var expect = DisParts(zp.Slots[0]);
            var r0 = g.ServerBench(me, Game.BenchDisassemble, 0, "");
            var gained0 = DisGained(loose0, zp.Parts);
            var exact = r0 == null && DisSame(gained0, expect) && expect.Values.Sum() == multi.Build().Nodes.Count() && expect.ContainsKey(multi.Build().Root!.Part.Name);
            var emptied = zp.Slots[0] == null && Game.IsEmptyGun(me.BuiltPrimary) && Game.IsEmptyGun(g.Slots[0].Def) && !Game.HasGunIn(me, 0);
            var movedOn = g.ActiveSlot != 0 && g.SlotHasGun(g.ActiveSlot) && !Game.IsEmptyGun(g.Active.Def) && me.Weapon == Game.WeaponIn(me, g.ActiveSlot);
            var again = g.ServerBench(me, Game.BenchDisassemble, 0, "");
            ZLine($"disassemble: '{multi.Name}' ({multi.Build().Receivers.Count} receivers, {expect.Values.Sum()} parts) in hand (slot {inHandBefore + 1}) -> {r0 ?? "ok"}, gained exactly its parts {DisSame(gained0, expect)}, "
                  + $"slot empty {emptied}, hands moved to slot {g.ActiveSlot + 1} ({g.Active.Def.Name}); again: '{again}'; slot 3 before the upgrade: '{mustBeYours}'");
            if (!exact)
                Fail($"disassembling the {multi.Name} should put exactly its {expect.Values.Sum()} parts (grip included) in the loose parts");
            if (!emptied)
                Fail("a disassembled gun slot should be left empty (Empty hands)");
            if (!movedOn)
                Fail("taking apart the gun in your hands should switch to another gun");
            if (again == null || mustBeYours == null)
                Fail("disassemble should refuse an empty slot and a slot that isn't yours");

            // 2. Wall and box builds, slots 3-4, like any other.
            var wb = zp.Slots[2];
            var bb = zp.Slots[3];
            var loose1 = new Dictionary<string, int>(zp.Parts, StringComparer.OrdinalIgnoreCase);
            var rw = g.ServerBench(me, Game.BenchDisassemble, 2, "");
            var rb = g.ServerBench(me, Game.BenchDisassemble, 3, "");
            var gained1 = DisGained(loose1, zp.Parts);
            var both = DisParts(wb);
            foreach (var (name, n) in DisParts(bb))
                both[name] = both.GetValueOrDefault(name) + n;
            var extraOk = rw == null && rb == null && DisSame(gained1, both) && me.BuiltExtra[0] == null && me.BuiltExtra[1] == null && zp.Slots[2] == null && zp.Slots[3] == null
                          && g.WeaponSlots == 2 && Game.FreeGunSlot(me) == 0 && Game.TargetSlot(me) == 0;
            ZLine($"disassemble: wall build '{wb?.Name}' and box build '{bb?.Name}' (slots 3-4) -> {rw ?? "ok"} / {rb ?? "ok"}, parts back {DisSame(gained1, both)}, {g.WeaponSlots} slots left, a new gun goes to slot {Game.TargetSlot(me) + 1}");
            if (!extraOk)
                Fail("wall and box builds in slots 3-4 should come apart into their parts and leave the slots free (a new gun goes to the empty slot 1)");

            // 3. The last gun (the start pistol in hand): allowed, empty hands, the melee key still there.
            var r1 = g.ServerBench(me, Game.BenchDisassemble, 1, "");
            var noGuns = r1 == null && Enumerable.Range(0, 4).All(s => !Game.HasGunIn(me, s)) && Game.IsEmptyGun(g.Active.Def) && g.ActiveSlot is 0 or 1;
            var next = g.NextSlot;
            g.SwitchTo(g.ActiveSlot == 0 ? 1 : 0, now + 1);
            var stayed = Game.IsEmptyGun(g.Active.Def);
            var ammoBefore = g.Active.Ammo;
            g.UpdateTrigger(now + 2, true, true);
            g.UpdateTrigger(now + 2.1, true, true);
            var fired = g.Active.Ammo != ammoBefore || g.LastShotTime >= now + 2;
            ZTicks(g, ref now, 1.0); // the run goes on with no guns
            g.SelfTestBeginRound(1, now);
            ZTicks(g, ref now, 1.5);
            var stillFine = me.Alive && g.Phase == MatchPhase.Playing && g.ZRound == 1 && me.MeleeBuild != null;
            ZLine($"disassemble: the last gun -> {r1 ?? "ok"}: no guns {noGuns}, in hand '{g.Active.Def.Name}', next slot {next + 1}, switch stays empty {stayed}, trigger fired {fired}, run goes on {stillFine} (melee: {me.MeleeBuild?.Name})");
            if (!noGuns || next != g.ActiveSlot || !stayed || fired || !stillFine)
                Fail("taking apart your last gun should leave empty hands (no switching to an empty slot, no shots) with the run and the melee key going on");

            // 4. The melee slot: hilt, head and mods.
            var mb = zp.Slots[4];
            var loose2 = new Dictionary<string, int>(zp.Parts, StringComparer.OrdinalIgnoreCase);
            var rm = g.ServerBench(me, Game.BenchDisassemble, 4, "");
            var meleeOk = rm == null && mb != null && DisSame(DisGained(loose2, zp.Parts), DisParts(mb)) && zp.Slots[4] == null && me.MeleeBuild == null
                          && mb.Nodes.Any(n => n.Part.Hilt != null);
            ZLine($"disassemble: melee '{mb?.Name}' ({mb?.Nodes.Count()} parts: {(mb == null ? "" : string.Join(", ", mb.Nodes.Select(n => n.Part.Name)))}) -> {rm ?? "ok"}, back to the standard strike {me.MeleeBuild == null}");
            if (!meleeOk)
                Fail("the melee build should come apart into its hilt, head and mods, leaving the standard melee strike");

            // 5. Nothing duplicated or lost; salvage is still its own action; one Undo puts a build back.
            var conserved = DisSame(DisOwned(zp), owned);
            var scrap = zp.Scrap;
            var salvage = g.ServerBench(me, 1, 0, "Red Dot");
            var salvaged = salvage == null && zp.Scrap > scrap && zp.Parts.GetValueOrDefault("Red Dot") == 1;
            zp.Parts["Red Dot"] = 2;
            var undo = g.ServerBench(me, 0, 0, multi.Build().ToText()); // what the bench's Undo sends
            var undone = undo == null && zp.Slots[0]?.ToText() == multi.Build().ToText() && !Game.IsEmptyGun(me.BuiltPrimary) && DisSame(DisOwned(zp), owned);
            me.Teleport(new Vector3(-30, 0, -30));
            var away = g.ServerBench(me, Game.BenchDisassemble, 0, "");
            ZLine($"disassemble: parts conserved {conserved} ({owned.Values.Sum()} owned), salvage separate {salvaged}, undo {undo ?? "ok"} ({undone}), away from a bench '{away}'");
            if (!conserved)
                Fail("disassembling should never duplicate or lose parts (loose + on builds stays the same)");
            if (!salvaged)
                Fail("salvaging a loose part for scrap should still work after disassembling");
            if (!undone)
                Fail("putting a disassembled build back (Undo) should restore it from the loose parts");
            if (away == null || zp.Slots[0] == null)
                Fail("disassembling should only work at a workbench");
        }

        // ---- At the mystery box: the OFFERED build comes apart, never the gun in your hands (board bug, 2026-10-26) ----
        if (zm.BoxSpots.Count == 0)
            Fail("disassemble at the box: the test map needs a mystery box");
        else
            CheckBoxOfferDisassemble(zm, multi, wall, box, ref now);

        // ---- Co-op: the server's messages leave a client (and a joiner) with the same slots and parts ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var gl = g.Local!;
            var zp = g.ZP(gl);
            gl.Teleport(zm.Benches[0].At + new Vector3(0, 0, -1.2f));
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            var w = new PacketWriter();
            void Send(Action<PacketWriter> write, Func<byte[], bool> apply)
            {
                write(w);
                apply(w.Span.ToArray());
            }
            // The client first hears the build (ZBuild) and the parts (ZRun), as after any workbench change.
            g.GiveBuild(gl, 0, multi.Build(), full: true);
            Send(x => Game.SelfTestWriteBuild(x, gl, 0, zp.Slots[0]!), m => c.SelfTestApplyBuild(m));
            Send(x => g.SelfTestWriteRun(x, gl), m => { c.SelfTestReadRun(m); return true; });
            var held = c.ActiveSlot == 0 && c.Active.Def.Name == multi.Name;
            // The request (what a client's ZBench kind 3 runs on the server), then what the server sends back.
            var why = g.ServerBench(gl, Game.BenchDisassemble, 0, "");
            Send(x => Game.SelfTestWriteCleared(x, gl, 0), m => c.SelfTestApplyBuild(m));
            Send(x => g.SelfTestWriteRun(x, gl), m => { c.SelfTestReadRun(m); return true; });
            var cz = c.ZP(c.Local!);
            var slotOk = cz.Slots[0] == null && Game.IsEmptyGun(c.Local!.BuiltPrimary) && c.Local.Primary == gl.Primary && Game.IsEmptyGun(c.Slots[0].Def);
            var partsOk = DisSame(cz.Parts, zp.Parts);
            var handsOk = c.ActiveSlot == g.ActiveSlot && c.ActiveSlot == 1 && !Game.IsEmptyGun(c.Active.Def);
            // The melee slot the same way.
            g.ServerBench(gl, Game.BenchDisassemble, 4, "");
            Send(x => Game.SelfTestWriteCleared(x, gl, 4), m => c.SelfTestApplyBuild(m));
            Send(x => g.SelfTestWriteRun(x, gl), m => { c.SelfTestReadRun(m); return true; });
            var meleeOk = c.Local.MeleeBuild == null && cz.Slots[4] == null && DisSame(cz.Parts, zp.Parts);
            // A joiner (a fresh client) gets the builds still there and the empty slot 1.
            using var j = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            var sync = g.SelfTestJoinerBuilds(gl);
            var applied = sync.All(m => j.SelfTestApplyBuild(m));
            var joinerOk = applied && Game.IsEmptyGun(j.Local!.BuiltPrimary) && j.Local.Primary == gl.Primary && j.Local.BuiltSecondary?.Name == gl.BuiltSecondary?.Name
                           && j.ZP(j.Local).Slots[1]?.ToText() == zp.Slots[1]?.ToText();
            ZLine($"disassemble (co-op): held on the client {held}, server '{why ?? "ok"}', client slot 1 empty {slotOk}, parts match {partsOk} ({cz.Parts.Values.Sum()} loose), "
                  + $"hands on slot {c.ActiveSlot + 1} like the server {handsOk}, melee cleared {meleeOk}; joiner: {sync.Count} message(s), slot 1 empty {joinerOk}; protocol {ServerInfo.ProtocolVersion}");
            if (!held || why != null || !slotOk || !partsOk || !handsOk || !meleeOk)
                Fail("disassembling in co-op should leave the client with the server's slots (empty), loose parts and weapon in hand");
            if (!joinerOk || ServerInfo.ProtocolVersion < 41)
                Fail("a joiner should see a disassembled slot as empty (protocol 41 or later)");
        }
    }

    /// <summary>What each of a player's five slots holds (build text, or "-"), plus the weapon in hand.</summary>
    private static string DisSlots(Game g, Player p) =>
        string.Join("|", g.ZP(p).Slots.Select(b => b?.ToText() ?? "-")) + $"|hand {p.Weapon}|{p.MeleeBuild?.Name}";

    /// <summary>
    /// The box's offer taken apart (ZBench kind 5, protocol 54): exactly the offered build's parts, the box closes, every slot
    /// and the gun in hand stay as they were (a gun in hand, empty hands, all four slots full); refused with no offer,
    /// someone else's offer, away from the box; kind 3 is a bench-only action; a client's request does the same.
    /// </summary>
    private static void CheckBoxOfferDisassemble(ZombiesMap zm, StandardBuild multi, StandardBuild wall, StandardBuild box, ref double now)
    {
        var boxIdx = Array.IndexOf(StandardBuilds.All, box);
        var expect = DisParts(box.Build());
        Vector3 AtBox(Game g) => zm.BoxSpots[Math.Clamp(g.BoxSpot, 0, zm.BoxSpots.Count - 1)].At + new Vector3(0, 0, -1.2f);
        void Offer(Game g, Player to)
        {
            g.BoxState = ZBoxState.Offering;
            g.BoxBuild = boxIdx;
            g.BoxOwner = to.Id;
        }
        bool Closed(Game g) => g.BoxState == ZBoxState.Idle && g.BoxBuild == -1 && g.BoxOwner == byte.MaxValue;

        // Three holds: the multi-receiver gun in hand; empty hands (slots 1-2 taken apart at a bench); all four slots full.
        foreach (var hold in new[] { "gun in hand", "empty hands", "full slots" })
        {
            using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            var me = g.Local!;
            var zp = g.ZP(me);
            g.GiveBuild(me, 0, multi.Build(), full: true);
            if (hold == "empty hands")
            {
                me.Teleport(zm.Benches[0].At + new Vector3(0, 0, -1.2f));
                g.ServerBench(me, Game.BenchDisassemble, 0, "");
                g.ServerBench(me, Game.BenchDisassemble, 1, "");
            }
            else if (hold == "full slots")
            {
                me.ExtraSlots = true;
                g.GiveBuild(me, 2, wall.Build(), full: true);
                g.GiveBuild(me, 3, wall.Build(), full: true);
                g.SwitchTo(0, now);
            }
            me.Teleport(AtBox(g));
            var slotsBefore = DisSlots(g, me);
            var noOffer = g.BoxDisassemblePrompt("G", now) == null && g.BoxDisassembleOffer() < 0;
            g.BoxDisassembleKey(now); // no offer: nothing happens
            g.BoxDisassembleKey(now + 0.2);
            var idleSame = DisSlots(g, me) == slotsBefore && g.ServerBench(me, Game.BenchBoxDisassemble, 0, "") != null;
            Offer(g, me);
            var prompt = g.BoxDisassemblePrompt("G", now);
            var take = g.FindZUse(me) is { Kind: ZUseKind.BoxTake }; // the take prompt is still there (E)
            var kind3 = g.ServerBench(me, Game.BenchDisassemble, Math.Max(0, g.ActiveSlot), ""); // the old request at the box: bench only now
            var before = new Dictionary<string, int>(zp.Parts, StringComparer.OrdinalIgnoreCase);
            g.BoxDisassembleKey(now); // first press: armed, nothing moves
            var armed = g.BoxDisassembleArmed(now) && !Closed(g) && DisSame(before, zp.Parts);
            g.BoxDisassembleKey(now + 0.5); // second press: the host runs it at once
            var gained = DisGained(before, zp.Parts);
            var exact = DisSame(gained, expect) && gained.Values.Sum() == box.Build().Nodes.Count();
            var untouched = DisSlots(g, me) == slotsBefore;
            var closed = Closed(g) && g.BoxDisassemblePrompt("G", now + 1) == null;
            ZLine($"disassemble at the box ({hold}): offer '{box.Name}' ({expect.Values.Sum()} parts), prompt '{prompt}', E take still offered {take}, kind 3 at the box '{kind3}', "
                  + $"armed first {armed}, gained exactly its parts {exact}, slots and hand untouched {untouched}, box closed {closed}; with no offer: nothing {noOffer && idleSame}");
            if (!exact || !untouched || !closed)
                Fail($"disassembling at the box ({hold}) should give exactly the offered {box.Name}'s parts, close the box and leave the held weapon and every slot alone");
            if (prompt == null || !take || !armed || kind3 == null)
                Fail("at the box the offer's prompt (G twice) sits beside the take (E), the first press only arms it, and a slot's disassembly needs a workbench");
            if (!noOffer || !idleSame)
                Fail("with no offer, G at the box should do nothing (no prompt, no request accepted)");
        }

        // Refusals: someone else's offer, too far away, spinning, the teddy bear.
        using (var g = ZGame(1, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            var other = g.Players.Values.First(p => p != me);
            me.Teleport(AtBox(g));
            Offer(g, other);
            var notYours = g.ServerBench(me, Game.BenchBoxDisassemble, 0, "");
            var noPrompt = g.BoxDisassemblePrompt("G", now) == null;
            Offer(g, me);
            me.Teleport(AtBox(g) + new Vector3(0, 0, -(Game.BoxDisassembleRange + 1.5f)));
            var far = g.ServerBench(me, Game.BenchBoxDisassemble, 0, "");
            me.Teleport(AtBox(g));
            g.BoxState = ZBoxState.Spinning;
            var spinning = g.ServerBench(me, Game.BenchBoxDisassemble, 0, "");
            g.BoxState = ZBoxState.Teddy;
            var teddy = g.ServerBench(me, Game.BenchBoxDisassemble, 0, "");
            ZLine($"disassemble at the box, refusals: someone else's offer '{notYours}' (no prompt {noPrompt}), too far '{far}', spinning '{spinning}', teddy '{teddy}'");
            if (notYours == null || !noPrompt || far == null || spinning == null || teddy == null)
                Fail("taking the box's offer apart should need your own offer, at the box");
        }

        // A client: sees the offer in ZWorld, its key sends ZBench kind 5, the server moves the parts, the client's ZRun and ZWorld follow.
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var gl = g.Local!;
            var zp = g.ZP(gl);
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            var w = new PacketWriter();
            g.GiveBuild(gl, 0, multi.Build(), full: true);
            Game.SelfTestWriteBuild(w, gl, 0, zp.Slots[0]!);
            c.SelfTestApplyBuild(w.Span.ToArray());
            gl.Teleport(AtBox(g));
            c.Local!.Teleport(AtBox(g));
            Offer(g, gl);
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
            c.SelfTestReadZombies(Ents(), WorldMsg());
            var clientBefore = DisSlots(c, c.Local);
            var seen = c.BoxState == ZBoxState.Offering && c.BoxBuild == boxIdx && c.BoxDisassemblePrompt("G", now) != null;
            var first = c.BoxDisassembleConfirmed(now);
            var second = c.BoxDisassembleConfirmed(now + 0.5);
            Game.WriteBenchRequest(w, Game.BenchBoxDisassemble, 0, ""); // what BenchRequest sends to the server
            var before = new Dictionary<string, int>(zp.Parts, StringComparer.OrdinalIgnoreCase);
            var serverBefore = DisSlots(g, gl);
            var why = g.SelfTestBenchPacket(gl, w.Span.ToArray());
            var serverOk = why == null && DisSame(DisGained(before, zp.Parts), expect) && DisSlots(g, gl) == serverBefore && Closed(g);
            g.SelfTestWriteRun(w, gl);
            c.SelfTestReadRun(w.Span.ToArray());
            c.SelfTestReadZombies(Ents(), WorldMsg());
            var cz = c.ZP(c.Local);
            var clientOk = DisSame(cz.Parts, zp.Parts) && DisSlots(c, c.Local) == clientBefore && c.BoxState == ZBoxState.Idle && c.BoxDisassemblePrompt("G", now + 1) == null;
            ZLine($"disassemble at the box (co-op): client sees the offer {seen}, key arms then sends {!first && second}, server '{why ?? "ok"}' ({serverOk}), "
                  + $"client parts match and slots untouched, box closed {clientOk}; protocol {ServerInfo.ProtocolVersion}");
            if (!seen || first || !second || !serverOk || !clientOk || ServerInfo.ProtocolVersion < 54)
                Fail("a client's box disassembly (ZBench kind 5, protocol 54) should give the offer's parts and leave its slots as they were");
        }
    }
}
