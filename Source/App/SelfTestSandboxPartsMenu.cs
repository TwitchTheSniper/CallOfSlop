using System.Numerics;
using System.Text.Json;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox parts menu": the debug parts menu (UiSandboxParts.cs; docs/sandbox/CONSTRUCTS.md). The map flag reads (and
    /// a bad one is refused); in an offline Sandbox game on the yard the menu's request hands the chosen part (by id) to
    /// the player, an unknown id is refused, the rack still works, a blueprint spawns in front of the player, the map flag
    /// turns both off; the window draws headless (the full list, a search, Up/Down/Enter state, a pick that takes out the
    /// part's tool and closes it) without ImGui errors and with no allocation per frame while open; a real client's
    /// requests go over the wire; and outside Sandbox a blueprint spawn is refused.
    /// </summary>
    private static void CheckSandboxPartsMenu()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"parts menu: the {ConstructYardMap} map is missing");
            return;
        }
        // The map flag.
        bool Flag(string json) => SandboxMap.Read(JsonDocument.Parse(json).RootElement, "test").PartsMenu;
        var byDefault = Flag("{\"rack\": [0, 0]}");
        var off = !Flag("{\"rack\": [0, 0], \"partsMenu\": false}");
        string bad;
        try
        {
            Flag("{\"partsMenu\": 1}");
            bad = "accepted";
        }
        catch (MapFileException ex)
        {
            bad = ex.Message;
        }
        Line($"parts menu flag: on by default {byDefault}, \"partsMenu\": false turns it off {off}, a number refused: \"{bad}\"");
        if (!byDefault || !off || !bad.Contains("partsMenu"))
            Fail("parts menu: the map's \"partsMenu\" flag doesn't read right");

        yard.RestoreProps();
        World.SetMap(yard);
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me || World.Map.Sandbox is not { } sb)
        {
            Fail("parts menu: no local player in the Sandbox game");
            return;
        }
        ref var hand = ref g.SandboxHands.Hand(me.Id);

        // The request: any part by id, anywhere; an unknown id refused; the rack unchanged.
        var piston = ConstructParts.Need("Piston");
        me.Teleport(ConstructWorld.Ground(World.Query, sb.RackX + 12, sb.RackZ - 12));
        Run(g, ref now, 0.2);
        g.SandboxTakePart(piston.Id);
        var gotPiston = hand.Held == piston.Id;
        g.SandboxTakePart(ConstructParts.MaxTypes); // (past every id: there are up to 250 part types)
        var unknown = g.SandboxNotice;
        var keptPiston = hand.Held == piston.Id;
        g.SandboxSend(CActKind.Rack); // (far from the rack: refused as before)
        var farRack = g.SandboxNotice;
        me.Teleport(ConstructWorld.Ground(World.Query, sb.RackX + 1, sb.RackZ));
        Run(g, ref now, 0.2);
        g.SandboxSend(CActKind.Rack);
        var fromRack = ConstructParts.Get(hand.Held)?.Name ?? "nothing";
        var rackOk = hand.Held != piston.Id && hand.Held != ConstructParts.None;
        Line($"parts menu request: a Piston by id in hand {gotPiston}; id {ConstructParts.MaxTypes} refused (\"{unknown}\"), hands kept {keptPiston}; the rack away from it: \"{farRack}\"; at it: {fromRack}");
        if (!gotPiston || !unknown.Contains("no such part") || !keptPiston)
            Fail("parts menu: the request didn't hand over the part by id, or an unknown id wasn't refused");
        if (!farRack.Contains("rack") || !rackOk)
            Fail("parts menu: the parts rack stopped working as before");

        // A blueprint in front of the player (past the yard's set where it stands in the way: GameSandbox.SandboxBuildInFront).
        me.Teleport(ConstructWorld.Ground(World.Query, sb.RackX + 12, sb.RackZ - 12));
        Run(g, ref now, 0.2);
        var turret = IndexOfBlueprint("Turret");
        var before = g.Constructs.Bodies.Count;
        g.SandboxSend(CActKind.Blueprint, part: (ushort)turret);
        var spawnWhy = g.SandboxNotice;
        var spawned = g.Constructs.Bodies.Count == before + 1 ? g.Constructs.Bodies[^1] : null;
        Run(g, ref now, 1.0);
        var ahead = spawned == null ? 0 : Vector3.Dot(spawned.WorldCom - me.Position, World.FlatForward(me.Yaw));
        var apart = spawned == null ? 0 : Vector2.Distance(new Vector2(spawned.WorldCom.X, spawned.WorldCom.Z), new Vector2(me.Position.X, me.Position.Z));
        var parts = spawned?.PartCount ?? 0;
        g.SandboxSend(CActKind.Blueprint, part: ushort.MaxValue); // (past every blueprint: the index is two bytes, and there are never 65535)
        var badBp = g.SandboxNotice;
        Line($"parts menu blueprint: the Turret spawned {(spawned != null ? $"as construct {spawned.Id} with {parts} parts, {ahead:0.0} m ahead and {apart:0.0} m away" : $"no (\"{spawnWhy}\")")}; index 65535: \"{badBp}\"");
        if (spawned == null || parts < 4 || ahead < 2 || apart > 25 || !badBp.Contains("no such blueprint"))
            Fail("parts menu: a blueprint didn't spawn in front of the player (or a bad index wasn't refused)");

        // The map flag turns both off.
        sb.PartsMenu = false;
        g.SandboxTakePart(piston.Id);
        var offPart = g.SandboxNotice;
        var offMenu = g.SandboxPartsMenu;
        sb.PartsMenu = true;
        Line($"parts menu off on the map: the menu offered {offMenu}, a part asked for: \"{offPart}\"");
        if (offMenu || !offPart.Contains("off"))
            Fail("parts menu: the map flag doesn't turn the menu off");

        SandboxPartsWindow(g, ref now);

        SandboxPartsOverWire(yard);

        // Outside Sandbox: refused.
        g.Mode = GameMode.FreeForAll;
        Run(g, ref now, 0.2);
        var outside = g.Local is { } p2 ? g.SandboxSpawnBlueprint(p2, turret) : "no player";
        Line($"parts menu outside Sandbox: offered {g.SandboxPartsMenu}, a blueprint spawn: \"{outside}\"");
        if (g.SandboxPartsMenu || outside == null || !outside.Contains("only in Sandbox"))
            Fail("parts menu: a blueprint spawn wasn't refused outside Sandbox");
    }

    private static int IndexOfBlueprint(string name)
    {
        var all = ConstructBuild.Blueprints;
        for (var i = 0; i < all.Count; i++)
            if (all[i].Name == name)
                return i;
        return -1;
    }

    /// <summary>The window headless: lists every part, a search narrows it, a pick takes out the tool and closes it; no ImGui error and no allocation per frame while open.</summary>
    private static void SandboxPartsWindow(Game g, ref double now)
    {
        var me = g.Local!;
        var ui = new Ui();
        ui.InitHeadless(new float2(1600, 900));
        var settings = new Settings();
        string? error = null;
        var frames = 0;
        void Frames(int n)
        {
            for (var i = 0; i < n && error == null; i++)
            {
                try
                {
                    ui.BeginFrame();
                    ui.DrawSandboxParts(g, settings);
                    ui.EndFrame();
                    frames++;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
        }
        try
        {
            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
            ui.SandboxPartsKeepOpen = false;
            ui.OpenSandboxParts(focusSearch: false); // (an active text box keeps its own copy of the text: the test types through SetSandboxPartsSearch)
            Frames(3);
            var all = ui.SandboxPartsListed;
            ui.SetSandboxPartsSearch("wheel");
            Frames(2);
            var wheels = ui.SandboxPartsListed;
            var first = ui.SandboxPartsHighlighted;
            ui.SetSandboxPartsSearch("zzzz nothing");
            Frames(2);
            var none = ui.SandboxPartsListed;
            ui.SetSandboxPartsSearch("lamp");
            Frames(2);
            var pick = ui.SandboxPartsHighlighted;
            if (pick != null)
                ui.SandboxPartsPick(g, pick);
            ref var hand = ref g.SandboxHands.Hand(me.Id);
            var picked = pick != null && !pick.PhysicsOnly && hand.Held == pick.Id && hand.Tool == pick.Tool && !ui.SandboxPartsOpen && ui.SandboxPartsRecent[0] == pick.Id; // (an item part: a physics-only one spawns in front, "Sandbox physics-only parts")
            Line($"parts menu window: {all} of {ConstructParts.All.Count} parts listed; \"wheel\" {wheels} (highlight {first?.Name ?? "none"}), nonsense {none}; "
                 + $"picking the {pick?.Name ?? "?"} put it in hand with the {hand.Tool.ToString().ToLowerInvariant()} out and closed the window: {picked}");
            if (all != ConstructParts.All.Count(d => d.Offered) || wheels < 1 || wheels >= all || none != 0 || !picked)
                Fail("parts menu: the window doesn't list, search or pick parts right");

            var t = now;
            SandboxInventoryTabs(g, ui, Frames, s => Run(g, ref t, s));
            now = t;

            // Open, steady frames: nothing allocated (least of three 60-frame rounds after a warm-up), with the badges
            // showing "inf" (no count source) and with counts from a source.
            ui.SetSandboxInventoryTab(Ui.SbTab.Parts);
            ui.SetSandboxInventoryNode(-1, -1);
            ui.SetSandboxPartsSearch("");
            ui.SandboxPartsKeepOpen = true;
            ui.OpenSandboxParts(focusSearch: false);
            long Steady()
            {
                Frames(40);
                Thread.Sleep(200); // (the tiered JIT)
                Frames(20);
                var least = long.MaxValue;
                for (var round = 0; round < 3; round++)
                {
                    var b0 = GC.GetAllocatedBytesForCurrentThread();
                    Frames(60);
                    least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - b0);
                }
                return least;
            }
            ui.SandboxCounts = null;
            var leastInf = Steady();
            var tiles = ui.SandboxTilesWithPicture + ui.SandboxTilesPlain;
            ui.SandboxCounts = new FakePartCounts();
            var leastCounts = Steady();
            ui.SetSandboxPartsSearch("pipe");
            var leastSearch = Steady(); // (a search on: the cached results, nothing new per frame)
            ui.SetSandboxPartsSearch("");
            ui.SandboxCounts = null;
            var stillOpen = ui.SandboxPartsOpen;
            ui.CloseSandboxParts();
            Frames(2); // (closed: nothing drawn, ImGui still balanced)
            Line($"parts menu window: {frames} headless frames, ImGui error: {error ?? "none"}; open steady frames allocate {leastInf} B per 60 with \"inf\" badges, {leastCounts} B with counts, {leastSearch} B while a search is shown "
                 + $"(least of three); {tiles} tiles drawn a frame (headless: plain swatches); stayed open {stillOpen}");
            if (error != null)
                Fail($"parts menu: the window threw in ImGui: {error}");
            if (leastInf > 0 || leastCounts > 0 || leastSearch > 0)
                Fail($"parts menu: the open window allocates {leastInf} / {leastCounts} B per 60 frames (should be nothing)");
            if (tiles < 1)
                Fail("parts menu: the inventory grid drew no tiles");
            if (!stillOpen)
                Fail("parts menu: the window closed by itself while kept open");
        }
        finally
        {
            ui.DisposeHeadless();
        }
    }

    /// <summary>The count badges' source for the self-test: a few of each part, none of some.</summary>
    private sealed class FakePartCounts : ISandboxPartCounts
    {
        public int PartCount(Game game, byte playerId, ushort partId) => partId % 4 == 0 ? 0 : partId * 7;
    }

    /// <summary>
    /// The inventory's tabs and tree headless: each tree node lists exactly its section's (or group's) items, a search
    /// narrows within a node, the Blueprints tab lists every blueprint and taking one builds it in front of the player,
    /// the Tools tab lists every tool model and taking one takes it out.
    /// </summary>
    private static void SandboxInventoryTabs(Game g, Ui ui, Action<int> frames, Action<double> advance)
    {
        var me = g.Local!;
        ui.SandboxPartsKeepOpen = true;
        ui.SetSandboxPartsSearch("");
        ui.OpenSandboxParts(focusSearch: false);
        frames(2);
        var cat = ui.SandboxInventoryCatalogue;
        if (cat == null)
        {
            Fail("inventory: the window made no catalogue");
            return;
        }
        // Every tree node lists its own items.
        var wrong = new List<string>();
        for (var gi = 0; gi < cat.Parts.Groups.Length; gi++)
        {
            var grp = cat.Parts.Groups[gi];
            ui.SetSandboxInventoryNode(gi, -1);
            frames(1);
            if (ui.SandboxPartsListed != grp.Items)
                wrong.Add($"{grp.Name}: {ui.SandboxPartsListed} listed of {grp.Items}");
            foreach (var s in grp.Sections)
            {
                ui.SetSandboxInventoryNode(gi, s);
                frames(1);
                if (ui.SandboxPartsListed != cat.Parts.Sections[s].Items.Length)
                    wrong.Add($"{grp.Name} > {cat.Parts.Sections[s].Name}: {ui.SandboxPartsListed} listed of {cat.Parts.Sections[s].Items.Length}");
            }
        }
        // A search covers every category (board item is01): "lamp" lists the same with Logic or Structure selected.
        var logic = Array.FindIndex(cat.Parts.Groups, x => x.Name == "Seats and Logic");
        var structure = Array.FindIndex(cat.Parts.Groups, x => x.Name == "Structure");
        ui.SetSandboxInventoryNode(logic, -1);
        ui.SetSandboxPartsSearch("lamp");
        frames(1);
        var lampInLogic = ui.SandboxPartsListed;
        var lampPick = ui.SandboxPartsHighlighted?.Name ?? "none";
        ui.SetSandboxInventoryNode(structure, -1);
        frames(1);
        var lampInStructure = ui.SandboxPartsListed;
        ui.SetSandboxPartsSearch("");
        ui.SetSandboxInventoryNode(-1, -1);
        frames(1);
        Line($"inventory tree: {cat.Parts.Groups.Length} groups, {cat.Parts.Sections.Length} sections; nodes listing the wrong items: {(wrong.Count == 0 ? "none" : string.Join("; ", wrong))}; "
             + $"\"lamp\" with Logic selected {lampInLogic} (highlight {lampPick}), with Structure selected {lampInStructure}");
        if (wrong.Count > 0 || logic < 0 || structure < 0 || lampInLogic < 1 || lampPick != "Lamp" || lampInStructure != lampInLogic)
            Fail("inventory: the category tree or the search within it doesn't list the right parts");

        SandboxInventorySearch(g, ui, frames);

        // Blueprints: every one listed; taking the highlighted one makes it ready on the host and hands out its ghost (the window closes), placed in front of the player.
        ui.SetSandboxInventoryTab(Ui.SbTab.Blueprints);
        frames(2);
        var bpListed = ui.SandboxPartsListed;
        ui.SetSandboxPartsSearch("buggy");
        frames(1);
        var bpSearch = ui.SandboxPartsListed;
        var bpPick = ui.SandboxInventoryHighlight;
        var before = g.Constructs.Bodies.Count;
        var took = ui.SandboxInventoryTake(g);
        for (var k = 0; k < 60 && g.GhostState == BlueprintGhostState.Preparing; k++)
            advance(1.0 / 30); // (GameSandboxBlueprintGhost.cs: made in the spawner's slices)
        var ghost = g.GhostState == BlueprintGhostState.Carrying;
        var fwd = GravityFrames.FlatForward(me.Position, me.Yaw);
        var radius = Game.GhostRadius(g.GhostMin, g.GhostMax);
        for (var k = 0; k < 6 && ghost && g.GhostState == BlueprintGhostState.Carrying; k++)
            g.SpawnPlaceSend(ConstructWorld.Ground(World.Query, me.Position + fwd * (radius + 3f + 4f * k)), me.Yaw); // (further out while something stands there)
        var built = ghost && g.Constructs.Bodies.Count == before + 1;
        ui.OpenSandboxParts(focusSearch: false); // (the Spawn closed it)
        frames(1);
        ui.SetSandboxPartsSearch("");
        Line($"inventory blueprints: {bpListed} of {ConstructBuild.Blueprints.Count} listed, \"buggy\" {bpSearch}; taking the {(bpPick >= 0 ? ConstructBuild.Blueprints[bpPick].Name : "?")} gave a ghost {ghost}, placed and built {built}");
        if (bpListed != ConstructBuild.Blueprints.Count || bpSearch < 1 || bpSearch >= bpListed || !took || !built)
            Fail("inventory: the Blueprints tab doesn't list or build the blueprints");

        // Tools: every model listed; taking the highlighted (one in the game) takes it out.
        ui.SetSandboxInventoryTab(Ui.SbTab.Tools);
        frames(2);
        var toolsListed = ui.SandboxPartsListed;
        var tool = ui.SandboxInventoryHighlight >= 0 ? ConstructTools.All[ui.SandboxInventoryHighlight].Tool : null;
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        ui.SandboxInventoryTake(g);
        var inHand = g.SandboxHands.Hand(me.Id).Tool;
        Line($"inventory tools: {toolsListed} of {ConstructTools.All.Count} listed; taking the first ({tool?.ToString() ?? "none"}) put the {inHand} in hand");
        if (toolsListed != ConstructTools.All.Count || tool == null || inHand != tool)
            Fail("inventory: the Tools tab doesn't list the tools or take one out");
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.None);
        ui.SetSandboxInventoryTab(Ui.SbTab.Parts);
        frames(2);
    }

    private static string SearchText(ConstructPartDef d, SandboxCatalogue cat) => $"{d.Name} {cat.PartGroup[d.Id]} {cat.PartSection[d.Id]} {d.Role} {d.Description}";

    /// <summary>
    /// Board item is01: the search is global. With Joints selected, "barrel" lists weapon builder barrels from every
    /// category and the tree shows no selection; results are grouped in the usual order, best matches first; every word
    /// must match; a click on Structure clears the text and lists only Structure; opening focuses the search box; clearing
    /// (Esc / the x button) goes back to the category that was selected; the Builds tab searches every standard build.
    /// </summary>
    private static void SandboxInventorySearch(Game g, Ui ui, Action<int> frames)
    {
        var cat = ui.SandboxInventoryCatalogue!;
        var parts = ConstructParts.All;
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var joints = Array.FindIndex(cat.Parts.Groups, x => x.Name == "Joints and Motion");
        var structure = Array.FindIndex(cat.Parts.Groups, x => x.Name == "Structure");
        ui.SetSandboxInventoryTab(Ui.SbTab.Parts);
        ui.SetSandboxPartsSearch("");
        ui.SetSandboxInventoryNode(joints, -1);
        frames(2);
        Want(ui.SandboxInventoryTreeSelected == 1 && ui.SandboxPartsListed == cat.Parts.Groups[joints].Items, "Joints selected without a search doesn't list Joints with one row highlighted");

        ui.SetSandboxPartsSearch("barrel");
        frames(2);
        var ids = ui.SandboxInventoryListedItems.ToArray();
        var groupsSeen = ids.Select(i => cat.PartGroup[i]).Distinct().ToList();
        Want(ids.Length > 0 && ids.Any(i => cat.PartGroup[i] != "Joints and Motion"),
            $"\"barrel\" with Joints selected lists {ids.Length} parts in {string.Join("/", groupsSeen)}: not the builder barrels of other categories");
        Want(ids.Any(i => parts[i].Name.Contains("barrel", StringComparison.OrdinalIgnoreCase)), "\"barrel\" lists no part named barrel");
        Want(ids.All(i => parts[i].Name.Contains("barrel", StringComparison.OrdinalIgnoreCase) || cat.PartSection[i].Contains("barrel", StringComparison.OrdinalIgnoreCase)
                          || parts[i].Role.ToString().Contains("barrel", StringComparison.OrdinalIgnoreCase) || parts[i].Description.Contains("barrel", StringComparison.OrdinalIgnoreCase)), "a listed part has no barrel in its name, section, role or description");
        Want(ui.SandboxInventorySearching && ui.SandboxInventoryTreeSelected == 0, "while searching a category is still highlighted");
        Want(ui.SandboxInventoryNode == (joints, -1), "the search forgot the selected category");
        // Grouped in the usual order, best matches first within a group.
        var order = ids.Select(i => Array.IndexOf(cat.Parts.Groups.Select(x => x.Name).ToArray(), cat.PartGroup[i])).ToArray();
        Want(order.Zip(order.Skip(1), (x, y) => x <= y).All(b => b), "search results aren't grouped in the tree's order");
        var starts = ids.Select(i => parts[i].Name.StartsWith("barrel", StringComparison.OrdinalIgnoreCase)).ToArray();
        var misordered = 0;
        for (var k = 1; k < ids.Length; k++)
            if (cat.PartGroup[ids[k]] == cat.PartGroup[ids[k - 1]] && starts[k] && !starts[k - 1] && !parts[ids[k - 1]].Name.Contains("barrel", StringComparison.OrdinalIgnoreCase))
                misordered++;
        Want(misordered == 0, "a name that starts with the word is listed after a weaker match");
        var barrels = ids.Length;

        // Case-insensitive, several words all must match.
        ui.SetSandboxPartsSearch("BARREL");
        frames(1);
        Want(ui.SandboxPartsListed == barrels, "the search is case-sensitive");
        ui.SetSandboxPartsSearch("gun barrel");
        frames(1);
        var two = ui.SandboxInventoryListedItems.ToArray();
        Want(two.Length <= barrels && two.All(i => SearchText(parts[i], cat).Contains("gun", StringComparison.OrdinalIgnoreCase) && SearchText(parts[i], cat).Contains("barrel", StringComparison.OrdinalIgnoreCase)),
            "a two-word search lists a part missing one of the words");
        ui.SetSandboxPartsSearch("mag");
        frames(1);
        Want(ui.SandboxPartsListed > 0 && ui.SandboxInventoryListedItems.ToArray().Any(i => parts[i].Name.Contains("mag", StringComparison.OrdinalIgnoreCase)), "\"mag\" finds no magazines");
        ui.SetSandboxPartsSearch("pipe");
        frames(1);
        Want(ui.SandboxPartsListed > 0, "\"pipe\" finds nothing");

        // Esc / x: the text goes, the remembered category is back.
        ui.SetSandboxPartsSearch("barrel");
        frames(1);
        var cleared = ui.ClearSandboxPartsSearch();
        frames(2);
        Want(cleared && !ui.SandboxInventorySearching && ui.SandboxInventoryNode == (joints, -1) && ui.SandboxPartsListed == cat.Parts.Groups[joints].Items && ui.SandboxInventoryTreeSelected == 1,
            "clearing the search doesn't bring back the previous category");
        Want(!ui.ClearSandboxPartsSearch(), "clearing an empty search claims it did something (Esc must then close the window)");

        // A click on Structure clears the text and lists only Structure.
        ui.SetSandboxPartsSearch("barrel");
        frames(1);
        ui.ClickSandboxInventoryNode(structure, -1);
        frames(2);
        Want(!ui.SandboxInventorySearching && ui.SandboxPartsListed == cat.Parts.Groups[structure].Items && ui.SandboxInventoryListedItems.ToArray().All(i => cat.PartGroup[i] == "Structure"),
            "clicking Structure doesn't clear the search and list only Structure");

        // Opening puts the keyboard in the search box.
        ui.CloseSandboxParts();
        ui.OpenSandboxParts();
        var wanted = ui.SandboxInventorySearchFocusWanted;
        frames(3);
        Want(wanted && ui.WantsTextInput, $"opening the inventory doesn't focus the search box (asked {wanted}, text input {ui.WantsTextInput})");

        // Builds tab: every standard build.
        ui.SetSandboxPartsSearch("");
        ui.SetSandboxInventoryTab(Ui.SbTab.Builds);
        frames(2);
        var allBuilds = ui.SandboxBuildsListed();
        if (allBuilds > 1)
        {
            ui.SetSandboxPartsSearch(StandardBuilds.All[^1].Name.ToUpperInvariant());
            frames(1);
            var one = ui.SandboxBuildsListed();
            ui.SetSandboxPartsSearch("zzzz nothing");
            frames(1);
            Want(one >= 1 && one < allBuilds && ui.SandboxBuildsListed() == 0, $"the Builds search lists {one} of {allBuilds}");
        }
        ui.SetSandboxPartsSearch("");
        ui.SetSandboxInventoryTab(Ui.SbTab.Parts);
        ui.SetSandboxInventoryNode(-1, -1);
        frames(2);
        Line($"inventory search: \"barrel\" with Joints selected lists {barrels} parts in {string.Join(", ", groupsSeen)}; clearing restores Joints, clicking Structure clears the text; builds listed {allBuilds}; problems: {(problems.Count == 0 ? "none" : problems.Count.ToString())}");
        if (problems.Count > 0)
            Fail("inventory search: " + string.Join("; ", problems));
    }

    /// <summary>A real client: the part by id and a blueprint go over the wire, an unknown id comes back refused.</summary>
    private static void SandboxPartsOverWire(Map yard)
    {
        const ushort port = 26343;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now); // (the countdown on the game clock, not the wall clock)
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("parts menu over the wire: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var lamp = ConstructParts.Need("Lamp");
        a.SandboxTakePart(lamp.Id);
        Pump(server, [a], 0.4, ref now, clock);
        var got = server.SandboxHands.Hand(sp.Id).Held == lamp.Id;
        var mirrored = a.SandboxHands.Hand(a.LocalId).Held == lamp.Id;
        var noSuch = (ushort)ConstructParts.All.Count; // (one past the last part: no such id)
        a.SandboxTakePart(noSuch);
        Pump(server, [a], 0.4, ref now, clock);
        var refused = a.SandboxNotice;
        var before = server.Constructs.Bodies.Count;
        a.SandboxSend(CActKind.Blueprint, part: (ushort)IndexOfBlueprint("Gate"));
        Pump(server, [a], 0.6, ref now, clock);
        var spawned = server.Constructs.Bodies.Count == before + 1;
        var copies = a.Constructs.Bodies.Count == server.Constructs.Bodies.Count;
        Line($"parts menu over the wire: a client's Lamp by id reached the server {got} (and its hands {mirrored}); id {noSuch} refused \"{refused}\"; a Gate blueprint spawned {spawned}, mirrored {copies}");
        if (!got || !mirrored || !refused.Contains("no such part") || !spawned || !copies)
            Fail("parts menu: a client's parts menu requests didn't go over the wire right");
    }
}
