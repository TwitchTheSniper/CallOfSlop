using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox gun bench" (board item dev-sandbox-weapon-parts-wp01, part 2; docs/sandbox/CONSTRUCTS.md, "The Gun Workbench"):
    /// the Gun Workbench part loads; a creative build at the bench doesn't consume and puts the weapon in the hands with the
    /// Zombies builder's own numbers (and the shot lands); a survival build uses up the parts of the hands, the backpack and
    /// the bench's slots (a missing part refuses the whole build, nothing taken) and taking it apart gives every part back
    /// (the bench first, then hands or backpack, none on the floor); a standard build spawns complete (every one of them,
    /// creative only); the window's stock follows the world's rule; a real host and separate clients agree, including a
    /// joiner who arrives later.
    /// </summary>
    private static void CheckSandboxGunBench()
    {
        if (Maps.Find(ConstructYardMap) is not { Sandbox: not null } yard)
        {
            Fail($"sandbox gun bench: the {ConstructYardMap} map (with its sandbox section) is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var keep = yard.Sandbox!.InfiniteParts;
        try
        {
            GbPart();
            GbCreative(yard);
            GbSurvival(yard);
            GbSpawn(yard);
            GbUi(yard);
            GbOverWire(yard);
        }
        finally
        {
            yard.Sandbox!.InfiniteParts = keep;
        }
    }

    private static ConstructPartDef GbBenchDef => ConstructParts.Find("Gun Workbench") ?? throw new ConstructPartException("no Gun Workbench part; refused: " + string.Join(" / ", ConstructParts.Refused.Where(r => r.Contains("Workbench"))));

    /// <summary>The first standard gun build that is small enough to check by hand: fewest parts.</summary>
    private static StandardBuild GbSmallBuild(BuildKind kind = BuildKind.Gun) =>
        StandardBuilds.All.Where(b => b.Build().Kind == kind).OrderBy(b => b.Build().Nodes.Count()).ThenBy(b => b.Name, StringComparer.Ordinal).First();

    private static void GbPart()
    {
        var d = GbBenchDef;
        var fit = 0;
        var unfit = new List<string>();
        foreach (var g in ConstructParts.All.Where(x => ConstructWeaponParts.NameOf(x).Length > 0 && !x.CreativeOnly))
        {
            var ok = false;
            for (var k = 0; k < d.Slots.Length && !ok; k++)
                ok = ConstructStorage.Fits(d.Slots[k], g, out _);
            if (ok)
                fit++;
            else
                unfit.Add(g.Name);
        }
        var items = ConstructParts.All.Count(x => ConstructWeaponParts.NameOf(x).Length > 0 && !x.CreativeOnly);
        Line($"bench part: {d.Name} (id {d.Id}), role {d.Role}, {d.Slots.Length} slots, mass {d.Mass} kg, physics-only {d.PhysicsOnly}; {fit}/{items} survival weapon items fit a slot (the rest go to the hands or backpack; {string.Join(", ", unfit.Take(8))})");
        if (!d.IsGunBench || d.Role != PartRole.Storage || d.Slots.Length < 4 || d.PhysicsOnly || fit * 10 < items * 8)
            Fail("sandbox gun bench: the Gun Workbench part isn't a hand-carried storage part with gunBench and slots that take most weapon parts");
        if (ConstructParts.All.Count(x => x.IsGunBench) != 1)
            Fail("sandbox gun bench: more than one part claims to be the Gun Workbench");
        // A part name is turned back into the item that stands for it, both ways, for every builder part with an item.
        var bad = Parts.All.Where(p => ConstructWeaponParts.DefFor(p.Name) is { } def && !ConstructWeaponParts.NameOf(def).Equals(p.Name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToList();
        if (bad.Count > 0)
            Fail($"sandbox gun bench: DefFor/NameOf disagree for {string.Join(", ", bad.Take(5))}");
    }

    /// <summary>An offline Sandbox game with the host standing at a Gun Workbench; the bench's body and part.</summary>
    private static (Game G, Player Me, Construct Bench, int Part)? GbSetup(Map yard, ref double now, bool creative, int bots = 0)
    {
        yard.Sandbox!.InfiniteParts = creative;
        var g = ModeGame(bots, GameMode.Sandbox, yard.Id);
        var at = ConstructWorld.Ground(World.Query, 12, -10);
        if (SpawnLocalSafely(g, ref now) is { } why)
        {
            g.Dispose();
            Fail($"sandbox gun bench: no local player ({why})");
            return null;
        }
        var me = g.Local!;
        me.Teleport(at + new Vector3(0, 0.05f, 2.2f));
        var bench = ConstructBuild.PlaceNew(g.Constructs, GbBenchDef, at, 0);
        if (bench == null)
        {
            g.Dispose();
            Fail("sandbox gun bench: the bench wouldn't place");
            return null;
        }
        Run(g, ref now, 0.2);
        return (g, me, bench, 0);
    }

    private static Dictionary<string, int> GbItems(Game g, Player p, Construct bench)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        g.SandboxHands.BenchCollect(g.Constructs, p.Id, bench, 0, d);
        return d;
    }

    private static int GbTotal(Dictionary<string, int> d) => d.Values.Sum();

    private static Dictionary<string, int> GbNeeds(WeaponBuild b)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in b.Nodes)
            d[n.Part.Name] = d.GetValueOrDefault(n.Part.Name) + 1;
        return d;
    }

    private static bool GbSame(WeaponDef? a, WeaponDef? b) =>
        a != null && b != null && a.Damage == b.Damage && a.Pellets == b.Pellets && a.RangeM == b.RangeM && a.Rpm == b.Rpm && a.Mag == b.Mag && a.Reload == b.Reload
        && a.HipSpreadDeg == b.HipSpreadDeg && a.Class == b.Class && a.Mode == b.Mode && a.Name == b.Name;

    private static void GbCreative(Map yard)
    {
        var now = 0.0;
        if (GbSetup(yard, ref now, creative: true, bots: 1) is not var (g, me, bench, part))
            return;
        using var gg = g;
        var sb = GbSmallBuild();
        var build = sb.Build();
        var refDef = Game.AssembleCached(build).Def;
        var pack0 = g.SandboxHands.Pack(me.Id).Items;
        var before = GbItems(g, me, bench).Count;
        var why = g.SandboxBench(me, 0, 0, build.ToText());
        var after = GbItems(g, me, bench);
        var def = me.BuiltPrimary;
        var inHand = g.Local!.Id == me.Id && g.Slots[0].Def != null && GbSame(g.Slots[0].Def, def);
        Line($"creative: {sb.Name} ({build.Nodes.Count()} parts) built at the bench: refused \"{why}\"; items before/after {before}/{after.Count}; hands {g.SandboxHands.Hand(me.Id).Held}, pack {g.SandboxHands.Pack(me.Id).Items - pack0} more; built gun {def?.Name} dmg {def?.Damage} rpm {def?.Rpm} mag {def?.Mag}; the builder's {refDef?.Name} dmg {refDef?.Damage} rpm {refDef?.Rpm} mag {refDef?.Mag}; in the local slot {inHand}");
        if (why != null || after.Count != 0 || g.SandboxHands.Hand(me.Id).Held != ConstructParts.None || g.SandboxHands.Pack(me.Id).Items != pack0)
            Fail("sandbox gun bench: a creative build was refused or took/gave parts");
        if (!GbSame(def, refDef) || ReferenceEquals(def, refDef) && false || !inHand || g.ZP(me).Slots[0] is not { } held || held.ToText() != build.ToText())
            Fail("sandbox gun bench: a bench-built gun doesn't have the Zombies builder's stats, or isn't the one in the hands");
        // It fires: the server's shot path with the gun in hand lands on a bot ahead.
        if (g.Players.Values.FirstOrDefault(p => p.Bot != null) is { } bot)
        {
            var feet = me.Position + new Vector3(0, 0, -6f);
            bot.Teleport(feet);
            bot.ImmuneUntil = 0;
            Run(g, ref now, 0.1);
            var hp = bot.Health;
            g.SelfTestServerShot(me, me.Eye, Vector3.Normalize(bot.Position + new Vector3(0, 1.0f, 0) - me.Eye), now);
            Run(g, ref now, 0.05);
            Line($"creative: a shot with the bench-built {def?.Name}: the bot's health {hp} -> {bot.Health}");
            if (!(bot.Health < hp) && bot.Alive)
                Fail("sandbox gun bench: a shot from the bench-built gun did no damage");
        }
        // Taking it apart in creative gives nothing back and empties the slot.
        var dis = g.SandboxBench(me, Game.BenchDisassemble, 0, "");
        Line($"creative: taken apart: \"{dis}\"; slot now {(g.ZP(me).Slots[0] == null ? "empty" : "still built")}, items {GbItems(g, me, bench).Count}");
        if (dis != null || g.ZP(me).Slots[0] != null || GbItems(g, me, bench).Count != 0)
            Fail("sandbox gun bench: a creative disassembly failed or returned parts it never took");
        // No bench within reach: refused.
        me.Teleport(me.Position + new Vector3(0, 0, 40));
        var far = g.SandboxBench(me, 0, 0, build.ToText());
        if (far == null || !far.Contains("Workbench"))
            Fail($"sandbox gun bench: a build far from the bench wasn't refused (\"{far}\")");
    }

    /// <summary>Puts every part of <paramref name="b"/> into the hands, the backpack and the bench's slots in turn; false if one wouldn't go.</summary>
    private static bool GbStock(Game g, Player me, Construct bench, WeaponBuild b, out List<string> placed)
    {
        placed = [];
        var ok = true;
        var k = 0;
        foreach (var n in b.Nodes)
        {
            var def = ConstructWeaponParts.DefFor(n.Part.Name);
            if (def == null)
            {
                ok = false;
                continue;
            }
            GiveResult r;
            if (k % 3 == 2)
            {
                var st = g.Constructs.StoreOf(bench, 0)!;
                var slot = Enumerable.Range(0, st.SlotCount).FirstOrDefault(s => ConstructStorage.Check(bench.DefOf(0), st, s, def, out _) == SlotCheck.Ok, -1);
                r = slot >= 0 && g.Constructs.SetIn(bench, 0, slot, def) == null ? GiveResult.Bench : GiveResult.Dropped;
            }
            else
                r = g.SandboxHands.Give(g.Constructs, me.Id, def, bench.WorldCom);
            if (r is GiveResult.Dropped or GiveResult.Lost or GiveResult.Inventory)
                ok = false;
            placed.Add($"{def.Name}:{r}");
            k++;
        }
        return ok;
    }

    private static void GbSurvival(Map yard)
    {
        var now = 0.0;
        if (GbSetup(yard, ref now, creative: false) is not var (g, me, bench, part))
            return;
        using var gg = g;
        var sb = GbSmallBuild();
        var build = sb.Build();
        var needs = GbNeeds(build);
        var bodies0 = g.Constructs.Bodies.Count;
        // Without the parts: refused, nothing built.
        var none = g.SandboxBench(me, 0, 0, build.ToText());
        if (none == null || !none.Contains("don't have") || g.ZP(me).Slots[0] != null || me.BuiltPrimary != null)
            Fail($"sandbox gun bench: a survival build with no parts wasn't refused (\"{none}\")");
        if (!GbStock(g, me, bench, build, out var placed))
        {
            Fail($"sandbox gun bench: couldn't stock the test ({string.Join(", ", placed)})");
            return;
        }
        var have = GbItems(g, me, bench);
        var srcHands = g.SandboxHands.Hand(me.Id).Held != ConstructParts.None;
        var srcPack = g.SandboxHands.Pack(me.Id).Items;
        var srcBench = g.Constructs.StoreOf(bench, 0)!.Items;
        Line($"survival: {sb.Name} needs {build.Nodes.Count()} parts; stocked {placed.Count} ({string.Join(", ", placed)}): hands {srcHands}, backpack {srcPack}, bench {srcBench}; counted {GbTotal(have)}");
        if (GbTotal(have) != build.Nodes.Count() || !srcHands || srcPack == 0 || srcBench == 0 || needs.Any(kv => have.GetValueOrDefault(kv.Key) != kv.Value))
            Fail("sandbox gun bench: the parts of the hands, backpack and bench weren't all counted");
        // One part short: the whole build is refused and nothing is taken.
        var shortName = needs.Keys.First();
        var g2 = GbShort(g, me, bench, build, shortName);
        if (!g2)
            Fail("sandbox gun bench: a build one part short wasn't refused whole (or took parts)");
        // Re-stock whatever the short test took and build for real.
        var now2 = GbItems(g, me, bench);
        if (g.ZP(me).Slots[0] != null)
        {
            g.SandboxBench(me, Game.BenchDisassemble, 0, "");
            now2 = GbItems(g, me, bench);
        }
        var haveNow = GbTotal(now2);
        if (haveNow != build.Nodes.Count())
        {
            Fail($"sandbox gun bench: setup drifted: {haveNow} parts, wanted {build.Nodes.Count()}");
            return;
        }
        var why = g.SandboxBench(me, 0, 0, build.ToText());
        var left = GbItems(g, me, bench);
        var def = me.BuiltPrimary;
        var refDef = Game.AssembleCached(build).Def;
        Line($"survival: built: \"{why}\"; parts left {GbTotal(left)}; the gun {def?.Name} same as the builder's {GbSame(def, refDef)}");
        if (why != null || GbTotal(left) != 0 || !GbSame(def, refDef) || g.ZP(me).Slots[0] == null)
            Fail("sandbox gun bench: a survival build didn't consume exactly its parts or didn't make the builder's gun");
        // Building the same thing again changes nothing (its own parts count as yours).
        var again = g.SandboxBench(me, 0, 0, build.ToText());
        if (again != null || GbTotal(GbItems(g, me, bench)) != 0)
            Fail($"sandbox gun bench: building the same gun again took parts (\"{again}\")");
        // Disassembly: every part back, the bench's slots first, nothing on the floor.
        var dis = g.SandboxBench(me, Game.BenchDisassemble, 0, "");
        var back = GbItems(g, me, bench);
        var onBench = g.Constructs.StoreOf(bench, 0)!.Items;
        var loose = g.Constructs.Bodies.Count - bodies0;
        var same = needs.All(kv => back.GetValueOrDefault(kv.Key) == kv.Value) && GbTotal(back) == build.Nodes.Count();
        Line($"survival: taken apart: \"{dis}\"; back {GbTotal(back)} of {build.Nodes.Count()} (the same parts {same}), {onBench} on the bench's slots, hands {g.SandboxHands.Hand(me.Id).Held != ConstructParts.None}, backpack {g.SandboxHands.Pack(me.Id).Items}, {loose} loose bodies on the floor; the slot is {(g.ZP(me).Slots[0] == null ? "empty" : "built")}");
        if (dis != null || !same || loose != 0 || g.ZP(me).Slots[0] != null || onBench * 2 < build.Nodes.Count())
            Fail("sandbox gun bench: a survival disassembly lost parts, put them on the floor, or didn't use the bench's slots first");
    }

    /// <summary>A build short of <paramref name="name"/> (one taken from the stock) is refused whole and takes nothing. True if so.</summary>
    private static bool GbShort(Game g, Player me, Construct bench, WeaponBuild build, string name)
    {
        // Take one of the part away through the bench's own rule, then ask.
        var def = ConstructWeaponParts.DefFor(name)!;
        if (!g.SandboxHands.BenchTake(g.Constructs, me.Id, bench, 0, name))
            return false;
        var before = GbItems(g, me, bench);
        var why = g.SandboxBench(me, 0, 0, build.ToText());
        var after = GbItems(g, me, bench);
        var same = before.Count == after.Count && before.All(kv => after.GetValueOrDefault(kv.Key) == kv.Value);
        g.SandboxHands.BenchGive(g.Constructs, me.Id, bench, 0, def); // (put it back)
        return why != null && why.Contains("don't have") && same && me.BuiltPrimary == null;
    }

    private static void GbSpawn(Map yard)
    {
        var now = 0.0;
        if (GbSetup(yard, ref now, creative: true) is not var (g, me, benchUnused, partUnused))
            return;
        using var gg = g;
        var fails = new List<string>();
        int guns = 0, melee = 0;
        foreach (var b in StandardBuilds.All)
        {
            var build = b.Build();
            var why = g.SandboxBench(me, Game.BenchSpawn, 0, build.ToText());
            if (why != null)
            {
                fails.Add($"{b.Name}: {why}");
                continue;
            }
            switch (build.Kind)
            {
                case BuildKind.Melee:
                    if (me.MeleeBuild != null && g.ZP(me).Slots[Game.MeleeSlot]?.Name == build.Name)
                        melee++;
                    break;
                default:
                    if (GbSame(me.BuiltPrimary, Game.AssembleCached(build).Def) && g.ZP(me).Slots[0]?.Name == build.Name)
                        guns++;
                    break;
            }
        }
        var total = StandardBuilds.All.Length;
        // (No standard grenade builds ship: a hand-made grenade goes to the grenade slot the same way.)
        var frag = GBuild("Bench Frag", "Lunchbox Casing", "Pull Pin Fuse", "Black Powder");
        var fragWhy = frag == null ? "its parts are missing" : g.SandboxBench(me, Game.BenchSpawn, 0, frag.ToText());
        var fragOk = frag != null && fragWhy == null && me.GrenadeBuild != null && g.ZP(me).Slots[Game.GrenadeSlot]?.Name == "Bench Frag" && me.Grenade == ThrowableKind.Built;
        Line($"spawn: {total} standard builds spawned complete in creative: {guns} guns, {melee} melee; {fails.Count} refused{(fails.Count > 0 ? $" ({fails[0]})" : "")}; a hand-made grenade in the grenade slot: {fragOk} ({fragWhy})");
        if (fails.Count > 0 || guns + melee != total || guns == 0 || melee == 0 || !fragOk)
            Fail("sandbox gun bench: not every standard build spawns complete into the hands in creative");
        // Not in survival.
        yard.Sandbox!.InfiniteParts = false;
        var no = g.SandboxBench(me, Game.BenchSpawn, 0, StandardBuilds.All[0].Build().ToText());
        yard.Sandbox.InfiniteParts = true;
        if (no == null || !no.Contains("creative"))
            Fail($"sandbox gun bench: a standard build spawned in a survival world (\"{no}\")");
        // A bad build text is refused with a reason.
        var bad = g.SandboxBench(me, Game.BenchSpawn, 0, "not a build");
        if (bad == null)
            Fail("sandbox gun bench: garbage was taken for a build");
    }

    private static void GbUi(Map yard)
    {
        var now = 0.0;
        if (GbSetup(yard, ref now, creative: true) is not var (g, me, bench, _))
            return;
        using var gg = g;
        var ui = new Ui();
        ui.OpenSandboxBench(g, ConstructEdits.BenchCentre(bench, 0));
        var creative = ui.SandboxBenchStockForTest();
        ui.CloseBench();
        // Survival: the stock is what the server counted.
        yard.Sandbox!.InfiniteParts = false;
        g.SandboxBench(me, 0, 0, "x"); // (the rules flag on the hands)
        var sb = GbSmallBuild();
        var build = sb.Build();
        GbStock(g, me, bench, build, out _);
        ui.OpenSandboxBench(g, ConstructEdits.BenchCentre(bench, 0));
        var surv = ui.SandboxBenchStockForTest();
        var needs = GbNeeds(build);
        var ok = !surv.IsUnlimited && needs.All(kv => surv.Owned(Parts.Find(kv.Key)!) >= kv.Value);
        var other = Parts.All.FirstOrDefault(p => !needs.ContainsKey(p.Name));
        var noOther = other != null && surv.Owned(other) == 0;
        ui.CloseBench();
        Line($"window: creative stock unlimited {creative.IsUnlimited}; survival stock unlimited {surv.IsUnlimited}, has the build's parts {ok}, none of a part it lacks ({other?.Name}) {noOther}");
        if (!creative.IsUnlimited || !ok || !noOther)
            Fail("sandbox gun bench: the workbench window's stock isn't every part in creative and the counted parts in survival");
    }

    private static void GbOverWire(Map yard)
    {
        yard.Sandbox!.InfiniteParts = true;
        const ushort port = 26373;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var pa))
        {
            Fail("sandbox gun bench over the wire: the client didn't join");
            return;
        }
        if (!pa.Alive)
            server.SelfTestRespawn(pa, now);
        Pump(server, [a], 0.3, ref now, clock);
        var at = ConstructWorld.Ground(World.Query, 12, -10);
        pa.Teleport(at + new Vector3(0, 0.05f, 2.2f));
        var bench = ConstructBuild.PlaceNew(server.Constructs, GbBenchDef, at, 0)!;
        Pump(server, [a], 0.5, ref now, clock);
        var sb = GbSmallBuild();
        var build = sb.Build();
        var refDef = Game.AssembleCached(build).Def;
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        // Creative: the client asks for a standard build; the host's player and the client's own copy agree.
        a.SandboxSendBench(Game.BenchSpawn, 0, build.ToText());
        Pump(server, [a], 0.6, ref now, clock);
        var sp = server.Players[pa.Id];
        Want(GbSame(sp.BuiltPrimary, refDef), "the host's copy of the client's spawned gun differs from the builder's");
        Want(a.Local != null && GbSame(a.Local.BuiltPrimary, refDef), "the client's own gun differs from the builder's");
        Want(GbSame(a.Slots[0].Def, refDef) && a.Slots[0].Reserve < 0, "the client's slot 1 isn't the builder's gun with endless ammo");
        Want(a.ZP(a.Local!).Slots[0]?.ToText() == build.ToText(), "the client doesn't hold the build text");
        // Take it apart through the wire; the client's slot and the host's agree again.
        a.SandboxSendBench(Game.BenchDisassemble, 0, "");
        Pump(server, [a], 0.6, ref now, clock);
        Want(server.ZP(sp).Slots[0] == null && a.ZP(a.Local!).Slots[0] == null && Game.IsEmptyGun(a.Local!.BuiltPrimary), "the disassembled gun is still there on one side");
        // Survival: parts in the client's hands/backpack/bench, open the bench over the wire (the parts come back in ZRun), build, take apart.
        yard.Sandbox.InfiniteParts = false;
        Pump(server, [a], 0.5, ref now, clock);
        GbStock(server, sp, bench, build, out _);
        Pump(server, [a], 0.4, ref now, clock);
        a.SandboxSendBench(Game.BenchOpen, 0, "");
        Pump(server, [a], 0.6, ref now, clock);
        var needs = GbNeeds(build);
        var told = a.ZP(a.Local!).Parts;
        Want(needs.All(kv => told.GetValueOrDefault(kv.Key) == kv.Value), $"the client was told {string.Join(", ", told.Select(kv => kv.Key + "x" + kv.Value))}, not the parts the server counted");
        a.SandboxSendBench(0, 0, build.ToText());
        Pump(server, [a], 0.6, ref now, clock);
        Want(GbTotal(GbItems(server, sp, bench)) == 0 && GbSame(sp.BuiltPrimary, refDef) && GbSame(a.Local!.BuiltPrimary, refDef), "the survival build over the wire didn't consume the parts or make the gun on both sides");
        // A joiner who arrives later sees the built gun in the client's hands.
        using var b2 = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b2], 1.5, ref now, clock);
        var seen = b2.Players.TryGetValue(pa.Id, out var theirs) ? theirs : null;
        Want(seen != null && GbSame(seen.BuiltPrimary, refDef), "a client who joined later doesn't see the first client's built gun");
        a.SandboxSendBench(Game.BenchDisassemble, 0, "");
        Pump(server, [a, b2], 0.6, ref now, clock);
        var back = GbItems(server, sp, bench);
        Want(GbTotal(back) == build.Nodes.Count() && needs.All(kv => back.GetValueOrDefault(kv.Key) == kv.Value), "the parts weren't all returned over the wire");
        yard.Sandbox.InfiniteParts = true;
        Line($"over the wire: {sb.Name} spawned, taken apart, then built from parts and taken apart in survival; the host, the client and a later joiner agree: {(problems.Count == 0 ? "yes" : string.Join("; ", problems))}");
        if (problems.Count > 0)
            Fail($"sandbox gun bench over the wire: {string.Join("; ", problems)}");
    }
}
