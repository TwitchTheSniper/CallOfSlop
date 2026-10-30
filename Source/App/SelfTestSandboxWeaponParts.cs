using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Weapon builder parts in Sandbox (board item dev-sandbox-weapon-parts-wp01; docs/sandbox/CONSTRUCTS.md, "Weapon
    /// builder parts in Sandbox"): every builder part has a construct def (generated, or the hand-made file that wraps it)
    /// with a drawn model, in the inventory's "Weapon parts" group; the hand-made parts keep their ids and generating
    /// again gives the same ids; the Zombies-economy parts are creative-only by the rules file; a builder Long Barrel and a
    /// feed wrenched onto a generated receiver make a gun with the Zombies builder's own numbers; a grip on it goes where
    /// the builder's rules say; a 2x2 of a generated receiver merges with exactly one front barrel socket at its front
    /// centre; and over a real host and a separate client, the client takes a generated part by id from the parts menu and
    /// both sides see the same part.
    /// </summary>
    private static void CheckSandboxWeaponParts()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"sandbox weapon parts: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        ReceiverMergeRules.Use(null);
        WpCoverage();
        WpGun();
        WpMerge();
        WpOverWire(yard);
    }

    /// <summary>The construct def standing for a builder part (generated or hand-made), or null.</summary>
    private static ConstructPartDef? WpDefOf(PartDef p) =>
        ConstructParts.All.FirstOrDefault(d => d.GunPartName.Equals(p.Name, StringComparison.OrdinalIgnoreCase) || d.GunReceiver.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && d.IsReceiverGun);

    private static void WpCoverage()
    {
        var all = ConstructParts.All;
        var gen = all.Where(d => d.Generated).ToList();
        var missing = Parts.All.Where(p => WpDefOf(p) == null).Select(p => p.Name).ToList();
        var noModel = gen.Where(d => d.Boxes.Length - d.SolidBoxes < 1 || d.Mass > 40 || !d.UsesTool(ConstructTool.Wrench)).Select(d => d.Name).ToList();
        var handMade = all.Count(d => !d.Generated);
        var order = gen.Count == 0 || gen.Min(d => d.Id) == handMade && gen.Max(d => d.Id) == all.Count - 1;
        var refused = ConstructParts.Refused.Where(r => r.StartsWith("(weapon part)") || r.StartsWith("WeaponParts.json")).ToList();
        // Generating again (as a client does at its own start) gives the same names in the same ids.
        var again = ConstructParts.WithWeaponParts(all.Where(d => !d.Generated).ToArray(), new List<string>());
        var same = again.Length == all.Count && again.Select(d => (d.Id, d.Name)).SequenceEqual(all.Select(d => (d.Id, d.Name)));
        var cat = SandboxCatalogue.Make();
        var grouped = gen.Count(d => cat.PartGroup[d.Id] == "Weapon parts");
        var sections = cat.Parts.Sections.Count(s => cat.Parts.Groups[s.Group].Name == "Weapon parts");
        var creative = gen.Where(d => d.CreativeOnly).Select(d => Parts.Find(d.GunPartName)!.Category).Distinct().OrderBy(c => c).ToList();
        var rules = ConstructWeaponParts.Current;
        var stdOk = StandardBuilds.All.Count(b => b.Build().Nodes.All(n => WpDefOf(n.Part) != null));
        Line($"coverage: {Parts.All.Length} builder parts, {gen.Count} generated defs (ids {handMade}..{all.Count - 1} of {ConstructParts.MaxTypes}), {missing.Count} without a def{(missing.Count > 0 ? $" ({string.Join(", ", missing.Take(6))})" : "")}, {noModel.Count} bad models, {refused.Count} refused{(refused.Count > 0 ? $" ({refused[0]})" : "")}; again the same ids {same}; {grouped} in \"Weapon parts\" ({sections} sections); creative-only: {string.Join(", ", creative)} (rules: {string.Join(", ", rules.CreativeOnly)}); standard builds whose every part has a def: {stdOk}/{StandardBuilds.All.Length}");
        if (missing.Count > 0 || noModel.Count > 0 || refused.Count > 0 || !order || !same || all.Count > ConstructParts.MaxTypes)
            Fail("sandbox weapon parts: not every builder part has a deterministic wrench item def with a model");
        if (grouped != gen.Count || sections < 10)
            Fail("sandbox weapon parts: the generated parts aren't grouped like the builder in the inventory's Weapon parts group");
        if (!creative.SequenceEqual(rules.CreativeOnly.OrderBy(c => c)) || !rules.CreativeOnly.Contains(PartCategory.Upgrade) || !rules.CreativeOnly.Contains(PartCategory.Multiplier))
            Fail("sandbox weapon parts: the Upgrade and Multiplier parts aren't creative-only by the rules file");
        if (stdOk != StandardBuilds.All.Length)
            Fail("sandbox weapon parts: a standard build uses a part with no Sandbox def");
        // A bad rules file is refused with the reason (the defaults stay).
        var bad = new List<string>();
        ConstructWeaponParts.Load("""{ "format": "call-of-slop-weapon-parts/1", "creativeOnly": ["laser"] }""", "bad.json", bad);
        ConstructWeaponParts.Load("""{ "format": "call-of-slop-weapon-parts/1", "scale": 99 }""", "bad2.json", bad);
        if (bad.Count != 2 || !bad[0].Contains("laser") || !bad[1].Contains("scale"))
            Fail($"sandbox weapon parts: bad rules files weren't refused for their reasons ({string.Join(" / ", bad)})");
    }

    /// <summary>A generated receiver that takes the builder's Long Barrel, and a feed (its construct def) it takes.</summary>
    private static (ConstructPartDef Rec, ConstructPartDef Feed)? WpRifleReceiver()
    {
        var lb = Parts.Find("Long Barrel")!;
        foreach (var d in ConstructParts.All.Where(x => x.Generated && x.IsReceiverGun))
        {
            var rec = Parts.Find(d.GunReceiver)!;
            var root = new BuildNode(rec);
            root.Attach(Array.Find(rec.Sockets, s => s.Type == "action")!.Name, Parts.Find(d.GunAction)!);
            var bs = Array.Find(rec.Sockets, s => s.Type == "barrel");
            var fs = Array.Find(rec.Sockets, s => s.Type == "feed");
            if (bs == null || fs == null || BuildRules.SocketWhy(root, bs, lb) != null)
                continue;
            foreach (var f in Parts.All.Where(p => p.Category == PartCategory.Feed))
                if (BuildRules.SocketWhy(root, fs, f) == null && WpDefOf(f) is { } fd)
                    return (d, fd);
        }
        return null;
    }

    private static void WpGun()
    {
        if (WpRifleReceiver() is not var (recDef, feedDef))
        {
            Fail("sandbox weapon parts: no generated receiver takes a Long Barrel");
            return;
        }
        var w = new ConstructWorld();
        var c = w.Create(ConstructWorld.Ground(World.Query, -8, 6) + new Vector3(0, 0.5f, 0), Quaternion.Identity)!;
        var rec = w.AddPart(c, recDef, Vector3.Zero, 0);
        c.Rebuild();
        var lastWhy = "";
        int Fit(int on, string socket, ConstructPartDef def, string by)
        {
            var why = ConstructBuild.Place(c, on, Array.FindIndex(c.DefOf(on).Sockets, s => s.Name == socket), def, Array.FindIndex(def.Sockets, s => s.Name == by), 0, out var g);
            var i = why == null ? ConstructBuild.Attach(w, c, def, g) : -1;
            lastWhy = why ?? "";
            c.Rebuild();
            return i;
        }
        var barrelDef = WpDefOf(Parts.Find("Long Barrel")!)!;
        var barrel = Fit(rec, "front", barrelDef, "back");
        var feed = Fit(rec, "left", feedDef, "right");
        var gun = c.GunCount > 0 ? c.Guns[0] : default;
        // The Zombies builder's numbers for the same parts.
        var rp = Parts.Find(recDef.GunReceiver)!;
        var root = new BuildNode(rp);
        root.Attach(Array.Find(rp.Sockets, s => s.Type == "action")!.Name, Parts.Find(recDef.GunAction)!);
        root.Attach(Array.Find(rp.Sockets, s => s.Type == "feed")!.Name, Parts.Find(feedDef.GunPartName)!);
        root.Attach(Array.Find(rp.Sockets, s => s.Type == "ammo")!.Name, Parts.Find("FMJ")!);
        root.Attach(Array.Find(rp.Sockets, s => s.Type == "barrel")!.Name, Parts.Find("Long Barrel")!);
        var zw = BuildStats.ReceiverStats(recDef.Name, root).Def;
        // A builder grip bolted on: the builder's rules decide (on, or the gun says why).
        var gripDef = ConstructParts.All.First(d => d.Generated && Parts.Find(d.GunPartName)?.Category == PartCategory.Grip);
        var grip = -1;
        var gripOn = "";
        foreach (var (on, by) in new[] { ("bottom", "top"), ("back", "front"), ("right", "left"), ("top", "bottom") })
            if ((grip = Fit(rec, on, gripDef, by)) >= 0)
            {
                gripOn = on;
                break;
            }
        var gripProblem = c.GunCount > 0 ? c.Guns[0].Problem : "no gun";
        Line($"gun: a {recDef.Name} (generated) with a builder Long Barrel ({barrelDef.Name}, part {barrel}) and a {feedDef.Name} (part {feed}): problem \"{gun.Problem}\", {gun.Def?.Damage} damage at {gun.Def?.Rpm} rpm, mag {gun.Def?.Mag}, range {gun.Def?.RangeM:0.#} m; the Zombies builder's: {zw.Damage} at {zw.Rpm} rpm, mag {zw.Mag}, range {zw.RangeM:0.#} m; a {gripDef.Name} on its {gripOn} (part {grip}{(grip < 0 ? $": {lastWhy}" : "")}): \"{gripProblem}\"");
        if (barrel < 0 || feed < 0 || gun.Def == null || gun.Problem.Length > 0)
            Fail("sandbox weapon parts: a builder Long Barrel on a generated construct receiver doesn't make a working gun");
        else if (gun.Def.Damage != zw.Damage || gun.Def.Rpm != zw.Rpm || gun.Def.Mag != zw.Mag || MathF.Abs(gun.Def.RangeM - zw.RangeM) > 0.01f)
            Fail("sandbox weapon parts: the construct gun's numbers aren't the Zombies builder's for the same parts");
        if (grip < 0)
            Fail("sandbox weapon parts: a builder grip can't be wrenched onto a construct receiver");
    }

    private static void WpMerge()
    {
        if (WpRifleReceiver() is not var (d, _))
            return;
        var w = new ConstructWorld();
        var c = w.Create(ConstructWorld.Ground(World.Query, -8, -6) + new Vector3(0, 0.5f, 0), Quaternion.Identity)!;
        var size = d.BoundsMax - d.BoundsMin;
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            ConstructBuild.AutoLink(w, c, w.AddPart(c, d, new Vector3(x * size.X, y * size.Y, 0), 0));
        c.Rebuild();
        if (c.MergeCount != 1)
        {
            Line($"merge: four {d.Name}s in a 2x2: {c.MergeCount} merges");
            Fail("sandbox weapon parts: a 2x2 of a generated receiver didn't merge");
            return;
        }
        ref var m = ref c.Merges[0];
        var own = d.Sockets.Length;
        var fronts = 0;
        var ok = true;
        ConstructMerge.MergedBounds(c, m, out var mc, out var mh);
        for (var s = own; s < c.SocketCount(m.Lead); s++)
        {
            c.SocketAt(m.Lead, s, out var at, out var dir);
            if (dir != 5)
                continue;
            fronts++;
            ok &= MathF.Abs(at.X - mc.X) < 0.01f && MathF.Abs(at.Y - mc.Y) < 0.01f && MathF.Abs(at.Z - (mc.Z - mh.Z)) < 0.01f;
        }
        var barrelDef = WpDefOf(Parts.Find("Long Barrel")!)!;
        var why = ConstructBuild.Place(c, m.Lead, own + Array.FindIndex(d.Sockets, s => s.Name == "front"), barrelDef, Array.FindIndex(barrelDef.Sockets, s => s.Name == "back"), 0, out _);
        Line($"merge: four {d.Name}s in a 2x2: N {m.N}, {fronts} front centre socket(s) on the merged receiver (at its front centre {ok}); a Long Barrel there: {why ?? "fits"}");
        if (m.N != 2 || fronts != 1 || !ok || why != null)
            Fail("sandbox weapon parts: a merged generated receiver doesn't have exactly one front centre barrel socket");
    }

    private static void WpOverWire(Map yard)
    {
        const ushort port = 26371;
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
            Fail("sandbox weapon parts over the wire: the client didn't join");
            return;
        }
        if (!pa.Alive)
            server.SelfTestRespawn(pa, now);
        Pump(server, [a], 0.3, ref now, clock);
        var item = ConstructParts.All.Last(d => d.Generated && !d.CreativeOnly && d.Mass <= 40);
        a.SandboxTakePart(item.Id);
        Pump(server, [a], 0.4, ref now, clock);
        var held = server.SandboxHands.Hand(pa.Id).Held;
        a.SandboxSend(CActKind.Stow);
        Pump(server, [a], 0.4, ref now, clock);
        var pack = a.SandboxHands.Pack(pa.Id);
        var packed = pack.Def.Take(pack.SlotCount).Contains(item.Id);
        // Creative-only parts: refused with infinite parts off (survival).
        var upgrade = ConstructParts.All.First(d => d.CreativeOnly);
        var hands = server.SandboxHands;
        var inf = hands.InfiniteParts;
        hands.InfiniteParts = false;
        var refused = hands.TakePart(yard.Sandbox, pa.Id, upgrade.Id);
        hands.InfiniteParts = inf;
        Line($"over the wire: the client asked for part {item.Id} ({item.Name}): the server's hands hold {held} ({ConstructParts.Get(held)?.Name}); stowed, the client's copy of its backpack has it {packed}; a {upgrade.Name} with infinite parts off: \"{refused}\"");
        if (held != item.Id || !packed)
            Fail("sandbox weapon parts over the wire: a client and the host don't agree on a generated part's id");
        if (refused == null || !refused.Contains("creative-only"))
            Fail("sandbox weapon parts: a creative-only part was handed out with infinite parts off");
    }
}
