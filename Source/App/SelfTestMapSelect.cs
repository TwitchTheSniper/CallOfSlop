namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The map select screen's logic (MapSelect.cs) and True Random: the pack column's order (pack.json order, Dev last,
    /// True Random at the bottom), Random pre-selected whenever a pack is opened, choices round-tripping through
    /// settings.txt (old files too), True Random drawing from every pack but Dev and respecting player limits, the
    /// True Random map vote pool, the server browser's "True Random" tag and the thumbnail camera.
    /// </summary>
    private static void CheckMapSelect()
    {
        // ---- Pack column -------------------------------------------------------------------------
        var list = MapSelect.Build(Maps.Packs, Maps.All);
        Line("pack column: " + string.Join(" | ", list.Select(p => $"{p.Name} ({p.CountLine}; {p.SizeLine}; picture of {string.Join(", ", p.Collage.Select(m => m.Name))})")));
        if (list.Count != Maps.Packs.Count(p => p.Maps.Count > 0) + 1)
            Fail($"pack column has {list.Count} tiles for {Maps.Packs.Length} packs (expected one per pack plus True Random)");
        if (!list[^1].IsTrueRandom || list[^1].Key != MapPack.TrueRandomKey || list[^1].Name != "True Random")
            Fail("True Random isn't the last tile");
        if (list.Take(list.Count - 1).Any(p => p.IsTrueRandom))
            Fail("True Random appears before the end");
        var real = list.Where(p => !p.IsTrueRandom).ToList();
        var inTr = real.Where(p => p.Pack!.InTrueRandom).ToList();
        for (var i = 1; i < inTr.Count; i++)
            if (inTr[i - 1].Pack!.Order > inTr[i].Pack!.Order)
                Fail($"packs out of pack.json order: {inTr[i - 1].Name} ({inTr[i - 1].Pack!.Order}) before {inTr[i].Name} ({inTr[i].Pack!.Order})");
        var firstExcluded = real.FindIndex(p => !p.Pack!.InTrueRandom);
        if (firstExcluded >= 0 && real.Skip(firstExcluded).Any(p => p.Pack!.InTrueRandom))
            Fail("a pack left out of True Random (Dev) isn't at the end of the real packs");
        var dev = Maps.FindPack("Dev");
        if (Directory.Exists(Maps.Folder) && dev != null)
        {
            if (dev.InTrueRandom)
                Fail("Dev/pack.json \"trueRandom\": false wasn't read");
            if (list.Count < 2 || list[^2].Pack != dev)
                Fail($"Dev isn't the last pack before True Random (it's at {list.FindIndex(p => p.Pack == dev)} of {list.Count})");
        }
        if (real.Any(p => p.Collage.Length != Math.Min(4, p.Maps.Length) || p.Collage.Distinct().Count() != p.Collage.Length || p.Collage.Any(m => !p.Maps.Contains(m))))
            Fail("a pack's picture doesn't use up to 4 different maps of the pack");
        if (real.Any(p => p.Maps.Zip(p.Maps.Skip(1)).Any(z => z.First.Size > z.Second.Size)))
            Fail("a pack's maps aren't listed smallest first");
        if (MapSelect.Spread(Maps.All, 4).Length != Math.Min(4, Maps.All.Length) || MapSelect.Spread([], 4).Length != 0)
            Fail("MapSelect.Spread miscounted");

        // ---- Random is pre-selected whenever a pack is opened -----------------------------------
        var st = new MapSelectState { Pack = 0, Entry = Math.Min(3, list[0].Maps.Length) };
        for (var i = 0; i < list.Count; i++)
        {
            st.Entry = list[i].Maps.Length;
            st.OpenPack(i);
            if (st.Pack != i || st.Entry != 0)
                Fail($"opening {list[i].Name} didn't pre-select Random (entry {st.Entry})");
        }
        st.OpenPack(0);
        st.Step(1, list);
        var afterDown = st.Entry;
        st.StepPack(1, list);
        var afterRight = st;
        st.Step(-5, list);
        var clampedUp = st.Entry;
        st.OpenPack(list.Count - 1);
        st.Step(3, list); // True Random has only Random
        if (afterDown != Math.Min(1, list[0].Maps.Length) || afterRight.Pack != Math.Min(1, list.Count - 1) || afterRight.Entry != 0 || clampedUp != 0 || st.Entry != 0)
            Fail($"keyboard steps wrong: down {afterDown}, right ({afterRight.Pack}, {afterRight.Entry}), clamp {clampedUp}, True Random {st.Entry}");

        // ---- Choices round-trip through settings.txt -------------------------------------------
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-mapselect-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var cases = new List<(string What, int Pack, int Entry)>();
            for (var i = 0; i < list.Count; i++)
            {
                cases.Add(($"Random from {list[i].Name}", i, 0));
                if (list[i].Maps.Length > 0)
                    cases.Add(($"{list[i].Maps[^1].Name} from {list[i].Name}", i, list[i].Maps.Length));
            }
            var trips = 0;
            foreach (var (what, pi, entry) in cases)
            {
                var s = new Settings { MapName = "Proto Box", MapRandom = false, MapPack = "Classic" };
                MapSelect.Apply(s, list[pi], entry);
                s.Save(tmp);
                var back = Settings.Load(tmp);
                var at = MapSelect.FromSettings(back, list);
                if (at != (pi, entry))
                    Fail($"{what}: saved and loaded back as tile {at.Pack} entry {at.Entry} (expected {pi}, {entry}); map_pack={back.MapPack} map={back.MapName} map_random={back.MapRandom}");
                trips++;
            }
            var trueRandom = new Settings();
            MapSelect.Apply(trueRandom, list[^1], 0);
            trueRandom.Save(tmp);
            var trLine = File.ReadAllLines(tmp).FirstOrDefault(l => l.StartsWith("map_pack=")) ?? "";
            if (trLine != "map_pack=*all" || !Settings.Load(tmp).MapRandom || !MapSelect.ChoiceText(Settings.Load(tmp)).StartsWith("True Random"))
                Fail($"True Random saved as '{trLine}' (expected map_pack=*all with map_random=1)");
            Line($"{trips} choices saved to settings.txt and loaded back to the same tile and entry; True Random is stored as {trLine}");

            // Old settings files (from before the map select screen, or ones that name a pack that's gone).
            (string Text, string Pack, int Entry)[] old =
            [
                ("name=Old\nmap=Proto Box\nmap_random=1\nmap_pack=Classic\n", "Classic", 0),
                ("map=Proto Box\nmap_random=0\nmap_pack=Classic\n", "Classic", -1),
                ("map=Proto Box\nmap_random=1\n", "Classic", 0), // before packs: no map_pack line
                ("map_random=1\nmap_pack=Gone Pack\n", "", 0), // a pack whose folder was removed: first tile, setting kept
                ("map_random=1\nmap_pack=*ALL\n", "True Random", 0),
            ];
            foreach (var (text, packName, entry) in old)
            {
                File.WriteAllText(tmp, text);
                var s = Settings.Load(tmp);
                var at = MapSelect.FromSettings(s, list);
                var wantPack = packName.Length == 0 ? 0 : list.FindIndex(p => p.Name == packName);
                if (packName.Length > 0 && wantPack < 0)
                    continue; // that pack isn't installed here
                var wantEntry = entry >= 0 ? entry : Array.FindIndex(list[wantPack].Maps, m => m.Name == "Proto Box") + 1;
                if (at != (wantPack, wantEntry))
                    Fail($"old settings '{text.Replace("\n", "; ")}' opened at tile {at.Pack} entry {at.Entry} (expected {wantPack}, {wantEntry})");
                if (text.Contains("Gone Pack") && s.MapPack != "Gone Pack")
                    Fail("a missing pack's name wasn't kept in the settings");
                if (s.PickMap(Maps.Packs, Maps.All, new Random(3)) == null)
                    Fail("an old settings file picked no map");
            }
            Line($"{old.Length} old-style settings files load and open the screen where they should");
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
            }
        }

        // ---- True Random draws from every pack but Dev, within the player limits ------------------
        {
            var s = new Settings { MapRandom = true, MapPack = MapPack.TrueRandomKey };
            var pool = MapPack.TrueRandomPool(Maps.Packs, Maps.All);
            var rng = new Random(11);
            var picks = Enumerable.Range(0, 1500).Select(_ => s.PickMap(Maps.Packs, Maps.All, rng)).ToList();
            if (picks.Any(m => m.Pack is { InTrueRandom: false }))
                Fail($"True Random picked a Dev map: {picks.First(m => m.Pack is { InTrueRandom: false }).Name}");
            var packsHit = picks.Select(m => m.Pack).Distinct().Count();
            var packsIn = MapPack.TrueRandomPacks(Maps.Packs).Count();
            if (packsHit != packsIn)
                Fail($"True Random only reached {packsHit} of {packsIn} packs in 1500 picks");
            if (picks.Distinct().Count() < pool.Count * 9 / 10)
                Fail($"True Random only picked {picks.Distinct().Count()} of {pool.Count} maps in 1500 picks");
            if (s.LobbyMaxPlayers(Maps.Packs, Maps.All) != pool.Max(m => m.MaxPlayers))
                Fail("the bot slider limit for True Random isn't its biggest map's");
            foreach (var players in new[] { 9, 13, 17, 25 })
            {
                var fits = pool.Where(m => m.MaxPlayers >= players).ToList();
                var sized = Enumerable.Range(0, 400).Select(_ => s.PickMap(Maps.Packs, Maps.All, rng, players)).ToList();
                if (fits.Count > 0 && sized.Any(m => m.MaxPlayers < players || m.Pack is { InTrueRandom: false }))
                    Fail($"True Random for {players} players picked a map that's too small (or a Dev map)");
                if (fits.Count == 0 && sized.Any(m => m.MaxPlayers != pool.Max(x => x.MaxPlayers)))
                    Fail($"True Random for {players} players didn't fall back to the biggest map");
            }
            Line($"True Random: {pool.Count} maps from {packsIn} packs (Dev left out), {picks.Distinct().Count()} different maps in 1500 picks, player limits respected");
        }

        // ---- The same with a made-up Maps folder: trueRandom: false, and every pack excluded ----------
        var dir = Path.Combine(Path.GetTempPath(), "callofslop-truerandom-" + Guid.NewGuid().ToString("N"));
        try
        {
            static string MapJson(string name, float half) => $$"""
                { "name": "{{name}}", "halfX": {{half}}, "halfZ": {{half}}, "ops": [
                  { "op": "Ground", "color": [0.4, 0.4, 0.4] },
                  { "op": "RingSpawns", "count": 8, "frac": 0.8 }, { "op": "AutoWaypoints", "spacing": 6 } ] }
                """;
            void Write(string rel, string text)
            {
                var path = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            Write("A/pack.json", """{ "name": "Pack A", "order": 2 }""");
            Write("A/a1.json", MapJson("TR A1", 20));
            Write("A/a2.json", MapJson("TR A2", 100));
            Write("B/pack.json", """{ "name": "Pack B", "order": 1 }""");
            Write("B/b1.json", MapJson("TR B1", 50));
            Write("T/pack.json", """{ "name": "Tests", "order": 0, "trueRandom": false }""");
            Write("T/t1.json", MapJson("TR T1", 20));
            var errors = new List<string>();
            var (all, packs) = Maps.LoadFolder(dir, errors);
            var col = MapSelect.Build(packs, all);
            var order = string.Join(", ", col.Select(p => p.Name));
            if (order != "Pack B, Pack A, Tests, True Random")
                Fail($"made-up pack column came out as {order} (expected Pack B, Pack A, Tests, True Random: order, excluded packs last)");
            var pool = MapPack.TrueRandomPool(packs, all);
            if (pool.Count != 3 || pool.Any(m => m.Name == "TR T1"))
                Fail($"True Random pool over the made-up packs: {string.Join(", ", pool.Select(m => m.Name))} (expected the 3 maps outside Tests)");
            var vote = Game.TrueRandomVotePool("TR A1", packs, all);
            if (vote.Count != 2 || vote.Any(m => m.Name is "TR A1" or "TR T1"))
                Fail($"True Random vote pool after TR A1: {string.Join(", ", vote.Select(m => m.Name))} (expected TR A2 and TR B1)");
            var big = Game.FitForPeople(vote, 12);
            if (vote.Any(m => m.MaxPlayers >= 12) && big.Any(m => m.MaxPlayers < 12))
                Fail("the True Random vote offered a map too small for the people playing");
            foreach (var p in packs)
                p.InTrueRandom = false;
            if (MapPack.TrueRandomPool(packs, all).Count != all.Length)
                Fail("with every pack left out, True Random didn't fall back to all maps");
            Line($"made-up folder: {order}; vote pool after TR A1: {string.Join(", ", vote.Select(m => m.Name))}; errors: {errors.Count}");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }

        // ---- The real True Random vote pool --------------------------------------------------------
        {
            var played = Maps.All.FirstOrDefault(m => m.Pack is { InTrueRandom: true }) ?? Maps.All[0];
            var pool = Game.TrueRandomVotePool(played.Name, Maps.Packs, Maps.All);
            if (pool.Any(m => m.Name == played.Name || m.Pack is { InTrueRandom: false }))
                Fail("the True Random vote pool has the map just played or a Dev map");
            if (pool.Select(m => m.Pack).Distinct().Count() != MapPack.TrueRandomPacks(Maps.Packs).Count())
                Fail("the True Random vote pool doesn't cover every pack");
            foreach (var humans in new[] { 2, 11, 17, 30 })
            {
                var fit = Game.FitForPeople(pool, humans);
                if (pool.Any(m => m.MaxPlayers >= humans) && fit.Any(m => m.MaxPlayers < humans))
                    Fail($"the True Random vote for {humans} people offers a map that's too small");
            }
            Line($"True Random vote after {played.Name}: {pool.Count} candidates from {pool.Select(m => m.Pack).Distinct().Count()} packs, filtered by player count");
        }

        // ---- Server browser tag ---------------------------------------------------------------------
        {
            var info = new ServerInfo("TR server", "Harbor", 3, 16, 2, MatchPhase.Playing, 60123, ServerInfo.ProtocolVersion, GameMode.FreeForAll, PhysicsMode.Swept, MapPack.TrueRandomName);
            var back = ServerInfo.Find([0x1c, 1, 2, 3, .. info.Encode()]);
            if (back != info)
                Fail($"server info with a rotation didn't round-trip: {back}");
            var plain = info with { Rotation = "" };
            var plainBack = ServerInfo.Find(plain.Encode());
            if (plainBack is not { Rotation: "", Physics: PhysicsMode.Swept } || plainBack != plain)
                Fail("server info without a rotation changed");
            if (info.Encode().Length != plain.Encode().Length + 1 + MapPack.TrueRandomName.Length)
                Fail("the rotation isn't just a trailing string (older builds would misread the info)");
            Line($"server info: rotation \"{back?.Rotation}\" rides as {MapPack.TrueRandomName.Length + 1} trailing bytes; without one the info is unchanged");
        }

        // ---- Thumbnail camera ---------------------------------------------------------------------
        var badCam = 0;
        foreach (var m in Maps.All)
        {
            var (eye, target, fov, far) = MapThumbs.Camera(m);
            var ok = float.IsFinite(eye.X + eye.Y + eye.Z + far) && eye.Y > target.Y + 5 && far > (eye - target).Length() && fov is > 0.2f and < 1.5f;
            if (!ok)
            {
                badCam++;
                Fail($"{m.Name}: thumbnail camera eye {eye}, target {target}, far {far}");
            }
        }
        Line($"thumbnail cameras for {Maps.All.Length} maps: {badCam} bad ({MapThumbs.Width}x{MapThumbs.Height}, rendered once per map in the game, never here)");
    }
}
