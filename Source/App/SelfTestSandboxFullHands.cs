using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox full hands" (board item rc01, a crash): taking a part off a construct while holding one crashed the game.
    /// The cause: the ghost sockets of the held part are found by the input before the frame's tick (or packets) takes the
    /// part off the same construct, and the draw then indexed the compacted parts with stale part and socket numbers
    /// (IndexOutOfRangeException in Construct.SocketAt, from ConstructRender.DrawGhosts). Checked here: stale ghosts are
    /// skipped for every blueprint with every part taken off;
    /// one rule gives a part to a player (ConstructEdits.Give: the hands, else the backpack by the Stow rules, else a loose
    /// pickup where it was, with a HUD line): taking a part off with full hands drops it as a pickup that falls and rests
    /// (hands unchanged), or stows it when it fits the backpack; empty hands take it; taking from storage and picking up
    /// with full hands go to the backpack or are refused (never lost); a bad hands state is emptied and logged, never a
    /// crash; and over the real transport a client's take-off with full hands leaves the part on the ground on the host
    /// and the client, its hands unchanged on both, with the notice on the client's HUD (and the backpack case on both).
    /// </summary>
    private static void CheckSandboxFullHands()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"sandbox full hands: the {ConstructYardMap} map (with its sandbox section) is missing");
            return;
        }
        if (ConstructEdits.Backpack == null)
        {
            Fail("sandbox full hands: the Backpack part didn't load");
            return;
        }
        FullHandsStaleGhosts();
        FullHandsOffline(yard);
        FullHandsOverWire(yard);
    }

    /// <summary>Every blueprint, every part taken off after the held part's ghosts were found: the draw's checks skip the stale ones.</summary>
    private static void FullHandsStaleGhosts()
    {
        var lamp = ConstructParts.Need("Lamp");
        int cases = 0, uncheckedThrows = 0, checkedThrows = 0, stale = 0;
        string? first = null;
        foreach (var bp in ConstructBuild.Blueprints)
            for (var i = 0; i < Construct.MaxParts; i++)
            {
                var w = new ConstructWorld();
                if (ConstructBuild.Build(w, bp, Vector3.Zero, Quaternion.Identity, out _) is not { } c || i >= c.PartCount)
                    break;
                ConstructRender.SetGhosts(c, ConstructBuild.Ghosts(c, lamp, ConstructRender.Ghosts, c.Com, 50));
                w.RemovePart(c, i);
                cases++;
                // What the draw did before the fix: every ghost, unchecked.
                try
                {
                    for (var k = 0; k < ConstructRender.GhostCount; k++)
                    {
                        ref var g = ref ConstructRender.Ghosts[k];
                        c.SocketAt(g.Part, g.Socket, out _, out _);
                        c.SegPose(c.Parts[g.Part].Seg, out _, out _);
                    }
                }
                catch (Exception ex)
                {
                    uncheckedThrows++;
                    first ??= $"{bp.Name} without part {i}: {ex.GetType().Name}";
                }
                // The draw now: only live ghosts.
                try
                {
                    for (var k = 0; k < ConstructRender.GhostCount; k++)
                    {
                        if (!ConstructRender.GhostLive(c, k))
                        {
                            stale++;
                            continue;
                        }
                        ref var g = ref ConstructRender.Ghosts[k];
                        c.SocketAt(g.Part, g.Socket, out _, out _);
                        c.SegPose(c.Parts[g.Part].Seg, out _, out _);
                    }
                }
                catch (Exception)
                {
                    checkedThrows++;
                }
            }
        ConstructRender.ClearAim();
        Line($"full hands: stale ghosts: {cases} take-offs over {ConstructBuild.Blueprints.Count} blueprints; the unchecked draw threw on {uncheckedThrows} (first: {first ?? "none"}), the checked draw on {checkedThrows}, {stale} stale ghosts skipped");
        // (Before the receiver merge, SocketAt indexed the sockets unchecked and the unchecked loop threw on about half the
        // take-offs; it now wraps the socket index, so a stale ghost draws in the wrong place instead: the check still skips it.)
        if (cases == 0 || stale == 0 || checkedThrows != 0)
            Fail($"sandbox full hands: stale ghosts after a take-off: {stale} skipped, the checked draw threw {checkedThrows} times (the unchecked one {uncheckedThrows} of {cases})");
    }

    private static Vector3 EyeBy(Construct c, int part) => PartCentre(c, part) + new Vector3(0, 0.4f, 1.2f);

    private static int PartNamed(Construct c, ConstructPartDef d) => Enumerable.Range(0, c.PartCount).FirstOrDefault(k => c.DefOf(k) == d, -1);

    private static Construct BuildAt(ConstructWorld w, string blueprint, float x, float z) =>
        ConstructBuild.BuildOnGround(w, ConstructBuild.Blueprints[IndexOfBlueprint(blueprint)], ConstructWorld.Ground(World.Query, x, z), 0, out _)!;

    /// <summary>The rule on a construct world of its own (the yard's ground): the hands, the backpack, a pickup; storage, picking up, a bad hands state.</summary>
    private static void FullHandsOffline(Map yard)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var engine = ConstructParts.Need("Engine");
        var lamp = ConstructParts.Need("Lamp");
        var w = new ConstructWorld();
        var hands = new ConstructEdits();
        hands.ResetPacks();
        const byte me = 0;
        ref var h = ref hands.Hand(me);
        var now = 0.0;

        // Full hands (an engine: too heavy for the pack), a door off a gate (too big for it): dropped where it was.
        var gate = BuildAt(w, "Gate", 30, 30);
        gate.Owner = me;
        var door = PartOf(gate, PartRole.Door);
        var doorDef = gate.DefOf(door);
        var doorAt = PartCentre(gate, door);
        h.Held = engine.Id;
        h.Tool = doorDef.Tool;
        var eye = EyeBy(gate, door);
        var began = hands.BeginRemove(w, me, eye, gate.Id, door, now);
        now += ConstructBuild.RemoveSeconds(doorDef) + 0.1;
        Exception? threw = null;
        try
        {
            hands.Tick(w, now, _ => eye);
        }
        catch (Exception ex)
        {
            threw = ex;
        }
        var dropped = w.Bodies.FirstOrDefault(b => b.Loose && b.DefOf(0) == doorDef);
        var near = dropped != null ? Vector3.Distance(dropped.WorldCom, doorAt) : -1;
        var dropNote = hands.Notices.LastOrDefault().Text ?? "";
        Line($"full hands: a {doorDef.Name} off a gate holding an {engine.Name}: began {began ?? "ok"}, threw {threw?.GetType().Name ?? "nothing"}, hands {ConstructParts.Get(h.Held)?.Name ?? "empty"}, "
             + $"a loose {doorDef.Name} {(dropped != null ? $"{near:0.00} m from where it was" : "missing")}, note \"{dropNote}\"");
        Want(began == null && threw == null && h.Held == engine.Id && dropped != null && near < 0.3f && dropNote == $"{doorDef.Name} dropped: hands full",
            "taking a part off with full hands drops it as a pickup where it was, keeps the hands, and says so");

        // It falls and rests like any dropped part.
        if (dropped != null)
        {
            var y0 = dropped.WorldCom.Y;
            for (var k = 0; k < 300 && !dropped.Asleep; k++)
                w.Step(World.Query, World.TickDt);
            var ground = ConstructWorld.Ground(World.Query, dropped.WorldCom.X, dropped.WorldCom.Z).Y;
            Line($"full hands: the dropped {doorDef.Name}: from {y0:0.00} m to {dropped.WorldCom.Y:0.00} m (ground {ground:0.00}), resting {dropped.Asleep}, still there {w.Get(dropped.Id) == dropped}");
            Want(dropped.WorldCom.Y < y0 - 0.05f && dropped.WorldCom.Y > ground && w.Get(dropped.Id) == dropped, "the dropped part falls to the ground and stays a pickup");
        }

        // Full hands and a part the backpack takes (a lamp off a lamp post): into the backpack.
        var post = BuildAt(w, "Lamp Post", 36, 30);
        post.Owner = me;
        var li = PartNamed(post, lamp);
        var pack = hands.Pack(me);
        hands.Notices.Clear();
        if (li >= 0)
        {
            h.Tool = lamp.Tool;
            eye = EyeBy(post, li);
            began = hands.BeginRemove(w, me, eye, post.Id, li, now);
            now += ConstructBuild.RemoveSeconds(lamp) + 0.1;
            hands.Tick(w, now, _ => eye);
        }
        var inPack = pack.Def.Take(pack.SlotCount).Contains(lamp.Id);
        var packNote = hands.Notices.LastOrDefault().Text ?? "";
        Line($"full hands: a {lamp.Name} off a lamp post holding an {engine.Name}: began {began ?? "ok"}, in the backpack {inPack}, hands {ConstructParts.Get(h.Held)?.Name ?? "empty"}, note \"{packNote}\"");
        Want(li >= 0 && began == null && inPack && h.Held == engine.Id && !w.Bodies.Any(b => b.Loose && b.DefOf(0) == lamp) && packNote == $"{lamp.Name} into the backpack: hands full",
            "with the backpack free the part goes into it");

        // Empty hands: into them, as before.
        var block = PartOf(gate, PartRole.Frame);
        var blockDef = gate.DefOf(block);
        h.Held = ConstructParts.None;
        h.Tool = blockDef.Tool;
        eye = EyeBy(gate, block);
        began = hands.BeginRemove(w, me, eye, gate.Id, block, now);
        now += ConstructBuild.RemoveSeconds(blockDef) + 0.1;
        hands.Tick(w, now, _ => eye);
        Want(began == null && h.Held == blockDef.Id, $"empty hands take the part (\"{began}\", {ConstructParts.Get(h.Held)?.Name ?? "empty"})");

        // Storage with full hands: into the backpack when it fits; refused (still stored) when it doesn't.
        var chest = BuildAt(w, "Chest", 30, 36);
        chest.Owner = me;
        var st = w.StoreOf(chest, 0);
        var slot = -1;
        for (var s = 0; st != null && s < st.SlotCount && slot < 0; s++)
            if (w.SetIn(chest, 0, s, lamp) == null)
                slot = s;
        eye = EyeBy(chest, 0);
        h.Held = engine.Id;
        var took = slot >= 0 ? hands.TakeStored(w, me, eye, chest.Id, 0, slot) : "no slot takes a lamp";
        var lamps = pack.Def.Take(pack.SlotCount).Count(d => d == lamp.Id);
        Want(took == null && lamps == 2 && h.Held == engine.Id, $"taking from storage with full hands stows it (\"{took}\", {lamps} lamps in the pack)");
        var saved = (ushort[])pack.Def.Clone();
        for (var k = 0; k < pack.SlotCount; k++)
            if (pack.Def[k] == ConstructParts.None)
            {
                pack.Def[k] = lamp.Id;
                pack.Count[k] = 1;
            }
        if (slot >= 0)
            w.SetIn(chest, 0, slot, lamp);
        var refused = slot >= 0 ? hands.TakeStored(w, me, eye, chest.Id, 0, slot) : null;
        var still = st != null && slot >= 0 && st.Def[slot] == lamp.Id;
        Want(refused != null && refused.Contains("full") && still && h.Held == engine.Id, $"with the pack full too it is refused and stays stored (\"{refused}\", stored {still})");
        // Picking up a loose part with full hands: refused with the pack full (it stays on the ground), stowed with room.
        var loose = ConstructBuild.PlaceNew(w, lamp, ConstructWorld.Ground(World.Query, 33, 36), 0)!;
        var pickEye = loose.WorldCom + new Vector3(0, 0.5f, 1);
        var pick = hands.PickUp(w, me, pickEye, loose.Id);
        Want(pick != null && w.Get(loose.Id) == loose, $"picking up with full hands and a full pack is refused (\"{pick}\")");
        Array.Copy(saved, pack.Def, saved.Length);
        for (var k = 0; k < pack.SlotCount; k++)
            pack.Count[k] = (byte)(pack.Def[k] == ConstructParts.None ? 0 : 1);
        pick = hands.PickUp(w, me, pickEye, loose.Id);
        lamps = pack.Def.Take(pack.SlotCount).Count(d => d == lamp.Id);
        Want(pick == null && w.Get(loose.Id) == null && lamps == 3 && h.Held == engine.Id, $"picking up with full hands and room in the pack stows it (\"{pick}\", {lamps} lamps)");

        // A bad hands state (a part id nothing names, an edit on a construct that's gone): emptied and logged, never a crash.
        var bad = (ushort)Math.Min(ConstructParts.All.Count, ConstructParts.None - 1);
        h.Held = bad;
        h.Edit = EditKind.Remove;
        h.Body = 60000;
        h.Start = h.Duration = 0;
        threw = null;
        try
        {
            hands.Tick(w, now, _ => eye);
            hands.Give(w, me, lamp, eye);
        }
        catch (Exception ex)
        {
            threw = ex;
        }
        Line($"full hands: a hand holding part id {bad} (none such) and an edit on a missing construct: threw {threw?.GetType().Name ?? "nothing"}, hands now {ConstructParts.Get(h.Held)?.Name ?? "empty"}");
        Want(threw == null && h.Held == lamp.Id, "a bad hands state is emptied, not a crash");

        if (problems.Count > 0)
            Fail($"sandbox full hands: offline: {string.Join("; ", problems)}");
    }

    private static void FullHandsOverWire(Map yard)
    {
        const ushort port = 26361;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("sandbox full hands over the wire: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var engine = ConstructParts.Need("Engine");
        var lamp = ConstructParts.Need("Lamp");

        // The client's own gate; holding an engine, it takes the door off (aiming at it: its ghosts up, as in the game).
        StandAt(sp, 10, 0);
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Gate"));
        Pump(server, [a], 0.6, ref now, clock);
        var gate = server.Constructs.Bodies[^1];
        var door = PartOf(gate, PartRole.Door);
        var doorDef = gate.DefOf(door);
        var doorAt = PartCentre(gate, door);
        StandBy(sp, doorAt, 1.8f, gate.WorldCom);
        server.SandboxHands.Hand(sp.Id).Held = engine.Id;
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxSend(CActKind.Tool, part: (byte)doorDef.Tool);
        Pump(server, [a], 0.2, ref now, clock);
        var copy = a.Constructs.Get(gate.Id);
        if (copy != null)
            ConstructRender.SetGhosts(copy, ConstructBuild.Ghosts(copy, engine, ConstructRender.Ghosts, copy.Com, 50));
        var before = server.Constructs.Bodies.Select(b => b.Id).ToHashSet();
        a.SandboxSend(CActKind.Remove, body: gate.Id, part: (byte)door);
        Exception? threw = null;
        try
        {
            Pump(server, [a], ConstructBuild.RemoveSeconds(doorDef) + 0.6, ref now, clock);
            for (var k = 0; copy != null && k < ConstructRender.GhostCount; k++)
                if (ConstructRender.GhostLive(copy, k))
                    copy.SocketAt(ConstructRender.Ghosts[k].Part, ConstructRender.Ghosts[k].Socket, out _, out _);
        }
        catch (Exception ex)
        {
            threw = ex;
        }
        ConstructRender.ClearAim();
        var hostHeld = server.SandboxHands.Hand(sp.Id).Held;
        var drop = server.Constructs.Bodies.FirstOrDefault(b => !before.Contains(b.Id) && b.Loose && b.DefOf(0) == doorDef);
        var onClient = drop != null && a.Constructs.Get(drop.Id) is { Loose: true } cd && cd.DefOf(0) == doorDef;
        var clientHeld = a.SandboxHands.Hand(a.LocalId).Held;
        var note = a.SandboxNotice;
        Line($"full hands over the wire: the client took a {doorDef.Name} off holding an {engine.Name}: threw {threw?.GetType().Name ?? "nothing"}; the host's hands {ConstructParts.Get(hostHeld)?.Name ?? "empty"}, "
             + $"the client's copy {ConstructParts.Get(clientHeld)?.Name ?? "empty"}; a loose {doorDef.Name} on the host {(drop != null ? $"{Vector3.Distance(drop.WorldCom, doorAt):0.00} m from where it was" : "missing")}, on the client {onClient}; the client's HUD \"{note}\"");
        Want(threw == null && hostHeld == engine.Id && clientHeld == engine.Id && drop != null && onClient && note == $"{doorDef.Name} dropped: hands full",
            "a client's take-off with full hands drops the part on the host and the client, keeps the hands and says so");

        // The backpack free: a lamp off the client's lamp post goes into it, on both.
        StandAt(sp, -10, 0);
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Lamp Post"));
        Pump(server, [a], 0.6, ref now, clock);
        var post = server.Constructs.Bodies[^1];
        var li = PartNamed(post, lamp);
        if (li >= 0)
        {
            StandBy(sp, PartCentre(post, li), 1.5f, post.WorldCom);
            Pump(server, [a], 0.3, ref now, clock);
            a.SandboxSend(CActKind.Tool, part: (byte)lamp.Tool);
            Pump(server, [a], 0.2, ref now, clock);
            a.SandboxSend(CActKind.Remove, body: post.Id, part: (byte)li);
            Pump(server, [a], ConstructBuild.RemoveSeconds(lamp) + 0.6, ref now, clock);
        }
        bool Packed(Game g) => g.SandboxHands.Pack(sp.Id).Def.Take(g.SandboxHands.Pack(sp.Id).SlotCount).Contains(lamp.Id);
        var packHost = Packed(server);
        var packClient = Packed(a);
        hostHeld = server.SandboxHands.Hand(sp.Id).Held;
        Line($"full hands over the wire: a {lamp.Name} off the client's lamp post: in the backpack on the host {packHost}, on the client {packClient}; hands {ConstructParts.Get(hostHeld)?.Name ?? "empty"}; the client's HUD \"{a.SandboxNotice}\"");
        Want(li >= 0 && packHost && packClient && hostHeld == engine.Id && a.SandboxNotice == $"{lamp.Name} into the backpack: hands full", "with the backpack free a client's take-off goes into it on both");
        if (problems.Count > 0)
            Fail($"sandbox full hands over the wire: {string.Join("; ", problems)}");
    }
}
