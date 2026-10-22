using System.Numerics;
using System.Reflection;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static readonly List<string> GibNotes = new();

    private static void GLine(string s)
    {
        Line("  " + s);
        GibNotes.Add(s);
    }

    /// <summary>Every real animal prop: health, gibs and what its skin bursts into (Props/README.md, "Animals").</summary>
    private static readonly (string Name, int Health, int Gibs, GibStyle Style)[] AnimalProps =
    [
        ("Bird on a Perch", 8, 5, GibStyle.Bird), ("Chicken", 15, 8, GibStyle.Bird), ("Mallard Duck", 15, 8, GibStyle.Bird), ("Hen Duck", 15, 8, GibStyle.Bird),
        ("Duck on a Pond", 15, 8, GibStyle.Bird), ("Pigeons", 20, 10, GibStyle.Bird), ("Duck Family", 25, 12, GibStyle.Bird), ("Cat", 45, 12, GibStyle.Fur),
        ("Dog", 60, 14, GibStyle.Fur), ("Sheep", 110, 18, GibStyle.Wool), ("Dairy Cow", 250, 24, GibStyle.Fur), ("Grazing Cow", 250, 24, GibStyle.Fur),
    ];

    /// <summary>Toys, decoys and things with animals on them: never meat.</summary>
    private static readonly string[] NotAnimals = ["Rubber Duck", "Giant Rubber Duck", "Duck Decoy", "Duck Pond", "Teddy Bear", "Bird Bath", "Bird Cage Stand", "Plush Toy Bin"];

    /// <summary>
    /// Gibs (Source/Props/Gibs.cs, Source/Game/GameGibs.cs): the animal props (health, toys not breakable into meat), the
    /// "gibs" block's errors, burst sizes by animal and by the Gore setting, settling, fading, the pool cap, blood splats
    /// and no per-frame allocation; animals breaking in a game from bullets, melee, blasts and strikes (a near blast at
    /// once), no kill or feed entry, respawn; the same burst on two clients from the same break packet; the server's
    /// "gibbed" call on blast kills (near: gibs, edge: ragdoll), the kill packet's flag, body bursts and the hat, zombies,
    /// and Off giving ragdolls.
    /// </summary>
    private static void CheckGibs()
    {
        GibNotes.Clear();
        var savedMode = Gibs.Mode;
        var savedQuality = ImpactDecals.Quality;
        var savedMap = World.Map;
        try
        {
            GibPropData();
            GibBursts();
            GibGame();
            GibBodies();
        }
        finally
        {
            Gibs.Mode = savedMode;
            Gibs.Clear();
            ImpactDecals.Configure(savedQuality);
            Ragdolls.Clear();
            World.SetMap(savedMap);
            if (GibNotes.Count > 0)
                Line("::notice title=Gibs::" + string.Join("%0A", GibNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static void GibPropData()
    {
        var parts = new List<string>();
        foreach (var (name, health, gibs, style) in AnimalProps)
        {
            if (Props.Find(name) is not { } def)
            {
                Fail($"animal prop '{name}' not found");
                continue;
            }
            var b = def.Breakable;
            if (b is not { IsAnimal: true } || b.Health != health || b.GibCount != gibs || b.GibStyle != style || b.BreakBy != PropDamage.All || b.Sound != PropBreakSound.Meat)
                Fail($"{name}: breakable {b != null}, {b?.Health} hp, {b?.GibCount} {b?.GibStyle} gibs, by {b?.BreakBy}, sound {b?.Sound} (want {health} hp, {gibs} {style} gibs, all damage, meat)");
            parts.Add($"{name} {health} hp/{gibs}");
        }
        foreach (var name in NotAnimals)
            if (Props.Find(name) is { Breakable.IsAnimal: true })
                Fail($"{name} is a toy or an object, not meat");
        var animals = Props.All.Where(p => p.Breakable is { IsAnimal: true }).Select(p => p.Name).ToList();
        foreach (var n in animals)
            if (!AnimalProps.Any(a => a.Name == n))
                Fail($"{n} bursts into gibs but isn't in the self-test's animal list");
        // Tougher animals for bigger ones: birds < cat < dog < sheep < cow.
        int Hp(string n) => Props.Find(n)?.Breakable?.Health ?? 0;
        if (!(Hp("Pigeons") < Hp("Cat") && Hp("Cat") < Hp("Dog") && Hp("Dog") < Hp("Sheep") && Hp("Sheep") < Hp("Dairy Cow")))
            Fail("animal health should rise with size: birds < cat < dog < sheep < cow");
        GLine($"animals: {string.Join(", ", parts)}; toys stay unbroken meat-free ({string.Join(", ", NotAnimals)})");

        // The "gibs" block's errors.
        string File(string gibs, string extra = "") => $$"""
            { "format": "call-of-slop-prop/1", "name": "Bad Gib", "collision": "cosmetic",
              "breakable": { "health": 10{{extra}}, "gibs": {{gibs}} },
              "parts": [ { "size": [0.2, 0.2, 0.2], "offset": [0, 0.1, 0], "color": "white" } ] }
            """;
        (string Json, string Want)[] bad =
        [
            (File("{ \"count\": 0 }"), "count"),
            (File("{ \"count\": 8, \"style\": \"scales\" }"), "\"fur\", \"bird\" or \"wool\""),
            (File("{ \"count\": 8, \"colour\": 1 }"), "\"gibs\" uses: count, style"),
            (File("{ \"count\": 8 }", ", \"explode\": { \"radius\": 3, \"damage\": 50 }"), "can't also explode"),
            (File("8"), "an object like"),
        ];
        var rejected = 0;
        foreach (var (json, want) in bad)
        {
            try
            {
                PropFile.Load(json, "Misc/BadGib.json");
                Fail($"a bad \"gibs\" block loaded (wanted an error about {want})");
            }
            catch (PropFileException ex) when (ex.Message.Contains(want))
            {
                rejected++;
            }
            catch (PropFileException ex)
            {
                Fail($"a bad \"gibs\" block gave '{ex.Message}' (wanted {want})");
            }
        }
        var good = PropFile.Load(File("{ \"style\": \"wool\" }"), "Misc/GoodGib.json");
        GLine($"\"gibs\" errors: {rejected} of {bad.Length} rejected with the right message; a block without a count gives {good.Breakable?.GibCount} {good.Breakable?.GibStyle} gibs");
        if (good.Breakable is not { GibCount: 8, GibStyle: GibStyle.Wool, Sound: PropBreakSound.Meat })
            Fail("a \"gibs\" block without a count should give 8 gibs and the meat sound");
    }

    /// <summary>A flat test map with every animal in a row (z = 0, 3 m apart) and a chicken that comes back after 2 s.</summary>
    private static Map GibTestMap()
    {
        var ops = new List<string>();
        for (var k = 0; k < AnimalProps.Length; k++)
            ops.Add($"{{ \"op\": \"Prop\", \"name\": \"{AnimalProps[k].Name}\", \"x\": {-16.5f + k * 3f:0.0}, \"z\": 0, \"yaw\": 0 }}");
        ops.Add("{ \"op\": \"Prop\", \"name\": \"Chicken\", \"x\": 0, \"z\": 10, \"respawn\": 2 }");
        ops.Add("{ \"op\": \"Prop\", \"name\": \"Rubber Duck\", \"x\": 4, \"z\": 10 }");
        var map = MapFile.Load(PropTestMap(string.Join(", ", ops)), "gib test");
        map.Validate();
        return map;
    }

    private static int GibProp(Map map, string name) => Enumerable.Range(0, map.PropInstances.Count).First(i => map.PropInstances[i].Def.Name == name);

    private static void GibBursts()
    {
        var map = GibTestMap();
        World.SetMap(map);
        Gibs.Mode = GoreMode.Full;
        ImpactDecals.Configure(ImpactDecalQuality.High);
        ImpactDecals.Pool.Clear(map, 0);

        // Burst size by animal and setting; Off only poofs (prop debris).
        var sizes = new List<string>();
        foreach (var (name, _, gibs, _) in AnimalProps)
        {
            var i = GibProp(map, name);
            var p = map.PropInstances[i];
            var from = p.Bounds.Center + new Vector3(0, 0.2f, 3f);
            var got = new int[3];
            for (var m = 0; m < 3; m++)
            {
                Gibs.Mode = (GoreMode)m;
                Gibs.Clear();
                PropDebris.Clear();
                got[m] = Gibs.SpawnProp(p, from, PropDamage.Bullet, Gibs.SeedFor(i, from));
                if (Gibs.Count != got[m])
                    Fail($"{name}: {got[m]} gibs reported but {Gibs.Count} in the pool");
                if (m == 2 && PropDebris.Count == 0)
                    Fail($"{name}: on Off gore it should poof into feathers or dust");
            }
            sizes.Add($"{name} {got[0]}/{got[1]}/{got[2]}");
            if (got[0] != gibs || got[1] != Math.Max(3, (gibs + 1) / 2) || got[2] != 0)
                Fail($"{name}: {got[0]} / {got[1]} / {got[2]} gibs on Full / Reduced / Off (want {gibs} / {Math.Max(3, (gibs + 1) / 2)} / 0)");
        }
        PropDebris.Clear();
        GLine($"gibs per animal (Full/Reduced/Off): {string.Join(", ", sizes)}");

        // A cow burst: flies, lands, bleeds, slides, settles, lies there, then fades and is gone; no allocation per frame.
        Gibs.Mode = GoreMode.Full;
        Gibs.Clear();
        var cow = GibProp(map, "Dairy Cow");
        var cp = map.PropInstances[cow];
        var spot = new Vector3(0, 0, -6); // the middle of the map: every piece lands on the floor
        cp = cp with { Position = spot, Bounds = new Box(spot + (cp.Bounds.Center - cp.Position), cp.Bounds.Half) };
        var blast = cp.Bounds.Center + new Vector3(0.5f, -0.4f, 1f);
        var splats0 = Gibs.Splats;
        Gibs.SpawnProp(cp, blast, PropDamage.Explosion, Gibs.SeedFor(cow, blast));
        var thrown = Gibs.Count;
        const float dt = 1f / 60f;
        double now = 1;
        for (var f = 0; f < 60; f++)
            Gibs.Update(dt, now += dt);
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 0; f < 120; f++)
            Gibs.Update(dt, now += dt);
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        var (resting, lowest, _) = Gibs.Inspect();
        var splats = Gibs.Splats - splats0;
        var gore = ImpactDecals.Pool.Count;
        for (var f = 0; f < 60 * 11; f++)
            Gibs.Update(dt, now += dt);
        var at14 = Gibs.Count;
        for (var f = 0; f < 60 * 4; f++)
            Gibs.Update(dt, now += dt);
        GLine($"cow burst: {thrown} gibs; after 3 s {resting} resting, lowest {lowest:0.000} m above its floor, {splats} blood splats ({gore} impact decals); "
              + $"after 14 s {at14} still lying there, after 18 s {Gibs.Count}; {alloc} bytes allocated in 2 s of updates");
        if (resting < thrown * 3 / 4 || lowest < -0.01f)
            Fail("gibs should settle on the floor within 3 s (not sink into it)");
        if (splats < 1 || splats > Gibs.MaxSplatsPerBurst || gore < 1)
            Fail($"a cow burst left {splats} blood splats (want 1-{Gibs.MaxSplatsPerBurst})");
        if (at14 != thrown || Gibs.Count != 0)
            Fail("gibs should lie about for about 15 s, then be gone");
        if (alloc > 0)
            Fail($"gib updates allocated {alloc} bytes");

        // Reduced: no blood. The pool cap: the oldest go first.
        Gibs.Mode = GoreMode.Reduced;
        splats0 = Gibs.Splats;
        Gibs.SpawnProp(cp, blast, PropDamage.Explosion, 1);
        for (var f = 0; f < 120; f++)
            Gibs.Update(dt, now += dt);
        var reducedSplats = Gibs.Splats - splats0;
        Gibs.Mode = GoreMode.Full;
        Gibs.Clear();
        var recycled0 = Gibs.Recycled;
        for (var k = 0; k < 20; k++)
        {
            Gibs.SpawnProp(cp, blast, PropDamage.Explosion, (uint)k);
            Gibs.Update(0.05f, now += 0.05);
        }
        var buffer = new float[(PropDebris.Capacity + Gibs.Capacity) * PropDebris.FloatsPerPiece];
        var written = Gibs.Write(buffer, 7);
        GLine($"Reduced: {reducedSplats} splats; 20 cow bursts: {Gibs.Count} alive (cap {Gibs.Capacity}), {Gibs.Recycled - recycled0} oldest recycled; written after 7 debris -> {written}");
        if (reducedSplats != 0)
            Fail("Reduced gore shouldn't leave blood splats");
        if (Gibs.Count != Gibs.Capacity || Gibs.Recycled - recycled0 != 20 * 24 - Gibs.Capacity || written != 7 + Gibs.Capacity)
            Fail("the gib pool should hold its cap, recycling the oldest");
        if (!float.IsFinite(buffer.Sum()))
            Fail("gib instance data has a NaN");
        Gibs.Clear();
        ImpactDecals.Pool.Clear(map, now);
    }

    private static void GibGame()
    {
        var map = GibTestMap();
        Gibs.Mode = GoreMode.Full;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: Maps.All[0].Id)
        {
            MapVoteEnabled = false,
        };
        World.SetMap(map);
        var me = game.Players[game.LocalId];
        game.SelfTestPlace(me, new Vector3(0, 0, -16), 0);
        Vector3 Centre(int i) => map.PropInstances[i].Bounds.Center;
        int Shots(int i, float damage)
        {
            var n = 0;
            var from = Centre(i) + new Vector3(0, 0.05f, 4f);
            var dir = Vector3.Normalize(Centre(i) - from);
            while (!map.IsPropBroken(i) && n < 100)
            {
                game.ShotProps(me, from, dir, World.RaycastWorld(from, dir, 30), damage, 0);
                n++;
            }
            return n;
        }
        var kills = me.Kills;
        var feed = game.Feed.Count;
        var gibbed0 = game.AnimalsGibbed;
        Gibs.Clear();
        var pigeon = Shots(GibProp(map, "Pigeons"), 30);
        var pigeonGibs = Gibs.Count;
        var cowShots = Shots(GibProp(map, "Dairy Cow"), 30);
        var cat = GibProp(map, "Cat");
        var eye = Centre(cat) + new Vector3(0, 0.5f, 1.4f);
        game.MeleeProps(me, eye, Vector3.Normalize(Centre(cat) - eye), 0);
        // A blast 1.2 m from the sheep breaks it at once; one 4.5 m from the grazing cow (edge of a 5 m blast) doesn't.
        var sheep = GibProp(map, "Sheep");
        game.BlastProps(me, Centre(sheep) + new Vector3(0, 0, 1.2f), 2.5f, 60f, PropDamage.Explosion, 0); // (small: the neighbours stay out of it)
        var grazing = GibProp(map, "Grazing Cow");
        game.BlastProps(me, Centre(grazing) + new Vector3(0, 0, 4.4f), 5f, 120f, PropDamage.Explosion, 0);
        var dog = GibProp(map, "Dog");
        game.BlastProps(me, Centre(dog) + new Vector3(0, 0.5f, 2f), 8f, 150f, PropDamage.Streak, 0);
        var duck = GibProp(map, "Rubber Duck");
        Shots(duck, 30);
        GLine($"in a game: pigeons {pigeon} shot(s) of 30 ({pigeonGibs} gibs), dairy cow {cowShots} shots; melee breaks the cat {map.IsPropBroken(cat)}; "
              + $"a grenade 1.2 m off breaks the sheep {map.IsPropBroken(sheep)}, one 4.4 m off the grazing cow {map.IsPropBroken(grazing)}; a strike breaks the dog {map.IsPropBroken(dog)}; "
              + $"{game.AnimalsGibbed - gibbed0} animals gibbed, {Gibs.Count} gibs; kills {me.Kills - kills}, feed lines {game.Feed.Count - feed}; the rubber duck took {map.IsPropBroken(duck)}");
        if (pigeon != 1 || pigeonGibs != 10 || cowShots != 9)
            Fail("pigeons should go in one rifle shot (10 gibs), a cow in 9");
        if (!map.IsPropBroken(cat) || !map.IsPropBroken(sheep) || map.IsPropBroken(grazing) || !map.IsPropBroken(dog))
            Fail("melee, a near grenade and a strike should break animals; a blast at its edge shouldn't break a cow");
        if (map.IsPropBroken(duck))
            Fail("the rubber duck broke into meat");
        if (game.AnimalsGibbed - gibbed0 != 5)
            Fail($"{game.AnimalsGibbed - gibbed0} animals gibbed (want 5)");
        if (me.Kills != kills || game.Feed.Count != feed)
            Fail("an animal gave a kill or a kill feed line");

        // Respawn: the chicken placed with "respawn": 2 is back 2 s later; a new match puts every animal back.
        var hen = Enumerable.Range(0, map.PropInstances.Count).First(i => map.PropInstances[i].Def.Name == "Chicken" && map.PropRespawn(i) > 0);
        var from = Centre(hen) + new Vector3(0, 0.05f, 4f);
        game.ShotProps(me, from, Vector3.Normalize(Centre(hen) - from), 30, 30, 10);
        var gone = map.IsPropBroken(hen);
        game.SelfTestTickPropRespawns(11);
        var early = map.IsPropBroken(hen);
        game.SelfTestTickPropRespawns(12.1);
        var back = !map.IsPropBroken(hen);
        typeof(Game).GetMethod("OnMatchStarted", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(game, null);
        GLine($"respawn: the chicken broke {gone}, still gone at 1 s {early}, back at 2.1 s {back}; a new match: {map.BrokenProps.Count} animals still broken, {Gibs.Count} gibs");
        if (!gone || !early || !back)
            Fail("an animal placed with \"respawn\" didn't come back on time");
        if (map.BrokenProps.Count != 0 || Gibs.Count != 0)
            Fail("a new match should bring every animal back and clear the gibs");

        // Two clients get the same break packet: the same burst, the same as the server's.
        var w = new PacketWriter();
        var pi = GibProp(map, "Duck Family");
        var hit = Centre(pi) + new Vector3(0.3f, 0.4f, 3f);
        Game.WritePropBreak(w, pi, PropDamage.Bullet, hit);
        var packet = w.Span.ToArray();
        var hashes = new uint[3];
        var counts = new int[3];
        Gibs.Clear();
        Gibs.SpawnProp(map.PropInstances[pi], hit, PropDamage.Bullet, Gibs.SeedFor(pi, hit));
        (hashes[0], counts[0]) = (Gibs.LastBurstHash, Gibs.LastBurstCount);
        for (var c = 1; c <= 2; c++)
        {
            using var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest" + c, Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            World.SetMap(map);
            Gibs.Clear();
            client.SelfTestPacket(packet, 1);
            (hashes[c], counts[c]) = (Gibs.LastBurstHash, Gibs.LastBurstCount);
            if (!map.IsPropBroken(pi) || client.AnimalsGibbed != 1)
                Fail($"client {c}: the break packet didn't break and gib the duck family");
        }
        GLine($"loopback: one break packet ({packet.Length} bytes) on two clients: {counts[1]} and {counts[2]} gibs, hashes {hashes[1]:x8} / {hashes[2]:x8} (server {hashes[0]:x8})");
        if (counts[1] != 12 || hashes[1] != hashes[2] || hashes[1] != hashes[0])
            Fail("two clients given the same break event should throw the same gib burst");
        World.SetMap(map);
    }

    private static void GibBodies()
    {
        // ---- Server: which blast kills gib ----
        var napalm = KillStreaks.KillId(StreakId.NapalmStrike);
        var frag = (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag);
        if (!Game.ShouldGib(DamageKind.Explosive, frag, 95, 100) || Game.ShouldGib(DamageKind.Explosive, frag, 40, 100) || !Game.ShouldGib(DamageKind.Explosive, frag, 70, 5)
            || Game.ShouldGib(DamageKind.Bullet, 1, 500, 100) || Game.ShouldGib(DamageKind.Melee, Game.MeleeKillId, 500, 100) || Game.ShouldGib(DamageKind.Explosive, napalm, 500, 10))
            Fail("ShouldGib: a big blast hit or overkill gibs; bullets, melee, napalm and a weak blast don't");

        using (var game = ModeGame(1, GameMode.FreeForAll))
        {
            var map = World.Map;
            var me = game.Players[game.LocalId];
            var bot = game.Players.Values.First(p => p != me);
            var spot = map.Spawns.Count > 0 ? map.Spawns[0] : Vector3.Zero;
            bool Blast(Vector3 offset, int health)
            {
                foreach (var p in game.Players.Values)
                    game.SelfTestPerks(p, PerkSet.Empty);
                game.SelfTestPlace(me, spot + new Vector3(40, 0, 40), 0);
                game.SelfTestPlace(bot, spot, 0);
                bot.ImmuneUntil = 0;
                bot.LastStandUsed = true;
                bot.Health = health;
                game.SelfTestStreakBlast(me, spot + offset, 6f, 150f, 5);
                return !bot.Alive && game.LastKill.Victim == bot.Id && game.LastKill.Gibbed;
            }
            var near = Blast(new Vector3(0.3f, 0, 0), World.MaxHealth);
            var nearDead = !bot.Alive;
            var edge = Blast(new Vector3(0, 4.2f, 0), 10); // straight above (no wall in between), about 3.2 m from the chest
            var edgeDead = !bot.Alive;
            game.SelfTestPlace(bot, spot, 0);
            game.DebugKill(me, bot, frag, false, 6);
            var debug = game.LastKill.Gibbed;
            GLine($"server (phase {game.Phase}): a strike shell 0.3 m away kills {nearDead}, gibbed {near}; one 3.2 m above finishing a 10 hp bot kills {edgeDead}, gibbed {edge}; a frag DebugKill gibbed {debug}");
            if (!near || !nearDead || edge || !edgeDead || !debug)
                Fail("a blast kill next to the explosion should gib, one at the edge give a ragdoll");
        }

        // ---- The kill packet's flag ----
        var w = new PacketWriter();
        new KillPacket(1, 5, false, frag, 0, 100, new Vector3(2, 0.2f, 1)) { Gibbed = true }.Write(w);
        var r = new PacketReader(w.Span[1..]);
        var back = KillPacket.Read(ref r);
        new KillPacket(1, 5, true, 3).Write(w);
        var r2 = new PacketReader(w.Span[1..]);
        var head = KillPacket.Read(ref r2);
        GLine($"kill packet: {w.Length} bytes; gibbed round-trips {back.Gibbed} (headshot {back.Headshot}); a headshot stays a headshot {head.Headshot}, not gibbed {!head.Gibbed}; protocol {ServerInfo.ProtocolVersion}");
        if (!back.Gibbed || back.Headshot || !head.Headshot || head.Gibbed || ServerInfo.ProtocolVersion < 33)
            Fail("the kill packet's gibbed flag doesn't round-trip (or protocol not bumped)");

        // ---- Clients: a gibbed kill bursts the body (the same on two PCs); Off gives a ragdoll ----
        var tmap = GibTestMap();
        World.SetMap(tmap);
        Ragdolls.WorldOverride = null;
        var feet = new Vector3(2, 0, -10);
        var kill = back with { Victim = 5, Source = feet + new Vector3(0.4f, 0.3f, 0.2f) };
        var hashes = new uint[2];
        var counts = new int[2];
        var torso = new Vector3?[2];
        for (var c = 0; c < 2; c++)
        {
            using var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest" + c, Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            World.SetMap(tmap);
            var p = new Player { Id = 5, Name = "Ash", Alive = false, RenderPosition = feet, RenderYaw = 0.7f };
            client.Players[5] = p;
            Gibs.Mode = GoreMode.Full;
            Gibs.Clear();
            Ragdolls.Clear();
            Ragdolls.SpawnPlayerForTest(client, p, kill, 1);
            (hashes[c], counts[c]) = (Gibs.LastBurstHash, Gibs.LastBurstCount);
            torso[c] = Ragdolls.BodyPoint(5);
            if (Ragdolls.LiveCount != 0)
                Fail("a gibbed body (no hat) shouldn't leave a ragdoll");
        }
        var (fullParts, reducedParts) = Gibs.BodyPartCounts;
        for (var f = 0; f < 240; f++)
            Gibs.Update(1f / 60, 2 + f / 60.0);
        var landed = Ragdolls.BodyPoint(5);
        // Reduced and Off.
        Gibs.Mode = GoreMode.Reduced;
        Gibs.Clear();
        using (var client = new Game(NetPeer.OfflineClient(), new Audio(), "Guest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id))
        {
            World.SetMap(tmap);
            var p = new Player { Id = 5, Name = "Ash", Alive = false, RenderPosition = feet };
            client.Players[5] = p;
            Ragdolls.SpawnPlayerForTest(client, p, kill, 1);
            var reduced = Gibs.LastBurstCount;
            Gibs.Mode = GoreMode.Off;
            Gibs.Clear();
            Ragdolls.Clear();
            Ragdolls.SpawnPlayerForTest(client, p, kill, 1);
            var offGibs = Gibs.Count;
            var offBodies = Ragdolls.LiveCount;
            // The edge kill (not gibbed) is a ragdoll on Full too.
            Gibs.Mode = GoreMode.Full;
            Ragdolls.Clear();
            Ragdolls.SpawnPlayerForTest(client, p, kill with { Gibbed = false }, 1);
            var edgeGibs = Gibs.Count;
            var edgeBodies = Ragdolls.LiveCount;
            GLine($"body burst: {counts[0]} pieces ({fullParts} parts + meat and bone) on two clients, hashes {hashes[0]:x8} / {hashes[1]:x8}; the death camera's torso at {torso[0]} then {landed}; "
                  + $"Reduced {reduced} ({reducedParts} parts); Off: {offGibs} gibs, {offBodies} ragdoll; not gibbed: {edgeGibs} gibs, {edgeBodies} ragdoll");
            if (counts[0] != fullParts + 18 || hashes[0] != hashes[1] || torso[0] is null || landed is null)
                Fail("a gibbed body should burst the same on two clients, and the death camera should find its torso");
            if (landed is { } l && (l.Y > 0.6f || Vector3.Distance(l, feet) < 0.2f))
                Fail("the torso chunk should be flung from the blast and land");
            if (reduced != reducedParts + 6 || offGibs != 0 || offBodies != 1 || edgeGibs != 0 || edgeBodies != 1)
                Fail("Reduced gore throws fewer gibs; Off gore and an edge kill give a ragdoll");
        }

        // The prestige hat flies off as a gib (a hat-only body slot), lands, and draws only the hat.
        if (Cosmetics.Get(HatId.TallTopHat) is { } hat)
        {
            Ragdolls.Clear();
            var d = new RagdollDeath { Feet = feet, Victim = 5, Killer = 1, KillId = frag, BlastAt = feet + new Vector3(0.5f, 0.3f, 0), BlastStrength = 11f, BlastRadius = 5f };
            var hb = Ragdolls.SpawnHatGib(hat, d, 1, 5, "Ash", GameModes.TeamColors[0]);
            for (var s = 0; s < 300; s++)
                Ragdolls.StepAll();
            var boxes = Ragdolls.CountBoxes(hb, new float[RagdollBody.MaxBoxes * PropDebris.FloatsPerPiece]);
            GLine($"hat gib ({hat.Name}): off {hb.HatOff}, landed {hb.HatResting} {Vector3.Distance(hb.HatPos, feet):0.0} m away, draws {boxes} boxes (the hat has {hat.Boxes.Count}); body asleep {hb.Sim.Asleep}");
            if (!hb.HatOff || !hb.HatResting || boxes != hat.Boxes.Count || !hb.Gibbed)
                Fail("a gibbed player's hat should fly off, land and be all that body slot draws");
            Ragdolls.Clear();
        }

        // Zombies: blown apart near the blast, a ragdoll at the edge, never on Off.
        var zfeet = new Vector3(0, 0, 0);
        Gibs.Mode = GoreMode.Full;
        var zNear = Ragdolls.ZombieGibbed(new Vector3(0.5f, 0.5f, 0), 5f, zfeet);
        var zEdge = Ragdolls.ZombieGibbed(new Vector3(4f, 0.5f, 0), 5f, zfeet);
        Gibs.Mode = GoreMode.Off;
        var zOff = Ragdolls.ZombieGibbed(new Vector3(0.5f, 0.5f, 0), 5f, zfeet);
        GLine($"zombies: a blast 0.8 m off gibs {zNear}, at 4 m {zEdge}, on Off {zOff}; {Ragdolls.GibbedBodies} bodies gibbed so far");
        if (!zNear || zEdge || zOff)
            Fail("zombies: near blasts gib, edge blasts and Off gore don't");
        Gibs.Mode = GoreMode.Full;
        Gibs.Clear();
    }
}
