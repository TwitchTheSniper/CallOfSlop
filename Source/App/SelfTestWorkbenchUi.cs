using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The workbench's editing model (BenchModel.cs, headless): first-tier grips hold one receiver, the linear list order,
    /// snapping (socket compatibility, the nearest valid socket under a ray, the camera's projection round trip), swap and
    /// replace semantics (with the old Attach-replace crash regression), move / remove / undo, auto-place and auto-complete,
    /// copying a receiver assembly with and without the parts (the missing list exact, finalising blocked, the craft cost),
    /// saving and loading builds through a temp file, and no allocations in the per-frame calls.
    /// </summary>
    private static void CheckWorkbenchUi()
    {
        PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"workbench test needs the part '{name}'");

        // ---- First-tier grips: one receiver ------------------------------------------------------------------
        var grips = Parts.All.Where(p => p.Frame != null).ToList();
        var early = grips.Where(g => g.Rarity <= Rarity.Uncommon).ToList();
        Line("grips by tier: " + string.Join(", ", grips.Select(g => $"{g.Name} ({g.Rarity}, {g.ReceiverCapacity})")));
        if (!early.Any(g => g.Rarity == Rarity.Common) || !early.Any(g => g.Rarity == Rarity.Uncommon))
            Fail("workbench: there should be common and uncommon (first-tier) grips");
        foreach (var g in early)
            if (g.ReceiverCapacity != 1)
                Fail($"workbench: first-tier grip {g.Name} ({g.Rarity}) holds {g.ReceiverCapacity} receivers (expected exactly 1)");
        foreach (var g in grips.Where(g => g.ReceiverCapacity > 1))
            if (g.Rarity < Rarity.Rare)
                Fail($"workbench: multi-receiver grip {g.Name} is {g.Rarity} (multi-receiver frames should be rare or rarer)");
        foreach (var sb in StandardBuilds.All.Where(s => s.Sources.Contains("start") || s.Sources.Contains("wall")))
            if (sb.Build() is { Root.Part: { Frame: not null } rg } && rg.ReceiverCapacity != 1)
                Fail($"workbench: the {string.Join("/", sb.Sources)} build {sb.Name} is on {rg.Name} ({rg.ReceiverCapacity} receivers; start and wall guns should use one-receiver grips)");
        // A saved build on a multi-receiver grip still loads whole.
        var oldSave = "Old Triple|volley|Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball},receiver_3:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball}}";
        var droppedOld = new List<string>();
        var old = WeaponBuild.Parse(oldSave, Parts.Find, droppedOld);
        Line($"old save on the Pistol Grip: {old.Receivers.Count} receivers, dropped {droppedOld.Count}, complete {BuildRules.IsComplete(old)}, stock actions added {old.ActionsAdded}");
        // Written before actions were parts: each Plinker gets its stock action, and nothing else changes.
        var migratedSave = oldSave.Replace("ammo:9mm Ball}", "ammo:9mm Ball,action:Semi-Auto Trigger}");
        if (droppedOld.Count > 0 || old.Receivers.Count != 2 || !BuildRules.IsComplete(old) || old.ActionsAdded != 2 || old.ToText() != migratedSave)
            Fail($"workbench: a saved build on a multi-receiver grip should load whole (with its stock actions) and stay valid: {old.ToText()}");

        // ---- The linear list ---------------------------------------------------------------------------------
        var rifle = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel{muzzle:Flash Hider},feed:Box Mag,ammo:5.56 FMJ},stock:Fixed Stock,optic:Red Dot}"), BenchStock.Unlimited);
        var order = string.Join(" > ", rifle.Rows.Select(r => r.Socket?.Type ?? "grip"));
        Line($"linear list: {order}");
        if (order != "grip > receiver > action > feed > ammo > barrel > muzzle > stock > optic" || !rifle.CanFinalize)
            Fail($"workbench: the linear list should read grip > receiver > action > feed > ammo > barrel > muzzle > stock > optic (got {order})");
        var split = new BenchModel(BuildOf("Sport Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Y-Splitter{barrel_1:Scattermouth Barrel,barrel_2:Scattermouth Barrel},feed:Shell Tube,ammo:00 Buckshot}}"), BenchStock.Unlimited);
        var depths = string.Join(",", split.Rows.Select(r => r.Depth));
        Line($"splitter indent: {string.Join(", ", split.Rows.Select(r => $"{r.Depth}:{r.Node?.Part.Name}"))}");
        if (depths != "0,1,2,2,2,2,3,3")
            Fail($"workbench: a splitter's barrels should be indented under it (depths {depths})");

        // ---- An empty action socket's marker sits where the receiver's stock action would go ----------------------
        var checkedMarkers = 0;
        foreach (var rp in Parts.InCategory(PartCategory.Receiver).Where(r => r.Sockets.Any(x => x.Type == "action")).Take(40))
        {
            if (ActionMigration.StockActionOf(rp, Parts.Find) is not { } stockAct)
                continue;
            var emptyB = new WeaponBuild("Marker", P("Service Rifle Grip"));
            emptyB.Root!.Attach("receiver_1", rp);
            var fullB = new WeaponBuild("Marker", P("Service Rifle Grip"));
            fullB.Root!.Attach("receiver_1", rp).Attach("action", stockAct);
            var em = new BenchModel(emptyB, BenchStock.Unlimited);
            var fm = new BenchModel(fullB, BenchStock.Unlimited);
            var es = em.SpotOf("receiver_1", "action");
            var fs = fm.SpotOf("receiver_1", "action");
            checkedMarkers++;
            if (es < 0 || fs < 0 || System.Numerics.Vector3.Distance(em.Spots[es].Pos, fm.Spots[fs].Pos) > 1e-4f)
                Fail($"workbench: the empty action marker on the {rp.Name} should sit where its stock action ({stockAct.Name}, {stockAct.Placement ?? "side"}) goes");
        }
        Line($"empty action markers: {checkedMarkers} receivers match their stock action's placement");
        if (checkedMarkers == 0)
            Fail("workbench: no receiver checked for the empty action marker position");

        // ---- Snapping: compatibility, nearest valid socket under a ray ----------------------------------------
        var m = new BenchModel(new WeaponBuild("Snap", P("Service Rifle Grip")), BenchStock.Unlimited);
        var valid = new bool[64];
        var recvSpot = m.SpotOf("", "receiver_1");
        if (recvSpot < 0 || !m.Spots[recvSpot].Needed || m.Rows.Count(r => r.Kind == BenchRowKind.Needed) != 1)
            Fail("workbench: an empty grip should show one needed receiver socket");
        var nRecv = m.ValidSpots(P("Workhorse Receiver"), valid);
        var nMag = m.ValidSpots(P("Box Mag"), new bool[64]);
        Line($"snap: an empty Service Rifle Grip takes a receiver in {nRecv} socket(s) (receiver_1: {(recvSpot >= 0 && valid[recvSpot])}), a Box Mag in {nMag}");
        if (nRecv != 1 || recvSpot < 0 || !valid[recvSpot] || nMag != 0)
            Fail("workbench: a receiver should snap only to the grip's receiver socket, a magazine nowhere until there's a receiver");
        if (m.Place(recvSpot, P("Workhorse Receiver"), out var recv) != null || recv == null)
            Fail("workbench: placing the receiver failed");
        var feedSpot = m.SpotOf("receiver_1", "feed");
        Array.Clear(valid);
        m.ValidSpots(P("Box Mag"), valid);
        var onlyFeed = feedSpot >= 0 && valid[feedSpot] && valid.Count(v => v) == 1;
        var why9 = m.WhyNot(m.SpotOf("receiver_1", "ammo"), P("9mm Ball"));
        Line($"snap: Box Mag fits only the feed: {onlyFeed}; 9mm Ball in the rifle receiver: {why9}");
        if (!onlyFeed || why9 == null || !why9.Contains("pistol"))
            Fail("workbench: the Box Mag should fit only the receiver's feed, and 9mm into a rifle receiver should be refused with the bore");
        // The ray: straight at a socket picks it; a valid flag off or a ray well wide picks nothing; the nearer of two wins.
        var spots = m.Spots;
        var target = feedSpot;
        var pos = spots[target].Pos;
        var hit = BenchModel.PickRay(spots, valid, pos - Vector3.UnitX, Vector3.UnitX, 0.03f);
        var miss = BenchModel.PickRay(spots, valid, pos - Vector3.UnitX + new Vector3(0, 0.3f, 0), Vector3.UnitX, 0.03f);
        var none = BenchModel.PickRay(spots, new bool[spots.Count], pos - Vector3.UnitX, Vector3.UnitX, 0.03f);
        var anyHit = BenchModel.PickRay(spots, ReadOnlySpan<bool>.Empty, pos - Vector3.UnitX, Vector3.UnitX, 0.001f);
        Line($"ray pick: straight at the feed {hit} (feed {target}), 30 cm wide {miss}, nothing valid {none}, any socket {anyHit}");
        if (hit != target || miss != -1 || none != -1 || anyHit != target)
            Fail("workbench: the ray pick should find the valid socket under the ray and nothing else");
        // The camera: project a socket to the screen and cast a ray back through that pixel: it passes through the socket.
        var cam = BenchCamera.Default;
        cam.Frame(m.Asm.PreviewDef?.BuiltMesh);
        cam.Yaw = 0.6f;
        cam.Pitch = 0.3f;
        cam.Zoom = 1.4f;
        cam.Matrices(16f / 9f, out var pose, out var view, out var proj);
        var pvm = pose * view * proj;
        var worst = 0f;
        for (var i = 0; i < spots.Count; i++)
        {
            if (!BenchCamera.Project(spots[i].Pos, pvm, new Vector2(100, 50), new Vector2(1280, 720), out var sp, out _)
                || !BenchCamera.Ray(sp, pvm, new Vector2(100, 50), new Vector2(1280, 720), out var o, out var d))
            {
                worst = float.MaxValue;
                continue;
            }
            var v = spots[i].Pos - o;
            worst = MathF.Max(worst, (v - d * Vector3.Dot(v, d)).Length());
        }
        Line($"camera round trip: {spots.Count} sockets projected and cast back, worst miss {worst * 1000:0.000} mm");
        if (worst > 0.001f)
            Fail("workbench: a ray through a socket's projected pixel should pass through the socket");

        // ---- Swap / replace semantics -------------------------------------------------------------------------
        var s = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel{muzzle:Flash Hider},feed:Box Mag,ammo:5.56 FMJ}}"), BenchStock.Unlimited);
        var oldBarrel = s.Build.At("receiver_1/barrel")!;
        var err = s.Place(s.SpotOf("receiver_1", "barrel"), P("Long Barrel"), out var nb);
        var keptMuzzle = nb?.Child("muzzle")?.Part.Name;
        var orphanPath = oldBarrel.Path(); // the Attach-replace crash: a replaced node must be fully off the build
        Line($"swap barrel: {err ?? "ok"}, the Long Barrel kept the {keptMuzzle ?? "nothing"}; the old barrel's parent {(oldBarrel.Parent == null ? "none" : oldBarrel.Parent.Part.Name)}, path '{orphanPath}'");
        if (err != null || keptMuzzle != "Flash Hider" || oldBarrel.Parent != null || oldBarrel.ParentSocket != null || orphanPath != "" || !s.CanFinalize)
            Fail("workbench: swapping a barrel should keep its muzzle device and leave the old barrel fully detached");
        oldBarrel.Detach(); // detaching the orphan must not pull the new barrel off
        if (s.Build.At("receiver_1/barrel")?.Part.Name != "Long Barrel")
            Fail("workbench: detaching a replaced part took the new one off (Attach-replace regression)");
        // The raw regression: attach twice into one socket, then use the first node.
        var gn = new BuildNode(P("Service Rifle Grip"));
        var first = gn.Attach("receiver_1", P("Workhorse Receiver"));
        var second = gn.Attach("receiver_1", P("Triad Burst Receiver"));
        first.Detach();
        if (first.Path() != "" || gn.Child("receiver_1") != second || second.Path() != "receiver_1")
            Fail("workbench: BuildNode.Attach replacing a part should orphan the old node cleanly");
        // A receiver swapped for one of the same bore keeps everything; another bore keeps only what fits.
        var r1 = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:5.56 FMJ}}"), BenchStock.Unlimited);
        r1.Place(r1.SpotOf("", "receiver_1"), P("Triad Burst Receiver"), out _);
        var r2 = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:5.56 FMJ}}"), BenchStock.Unlimited);
        r2.Place(r2.SpotOf("", "receiver_1"), P("Plinker Receiver"), out _);
        Line($"swap receiver: rifle -> rifle {r1.Build.ToText()}; rifle -> pistol {r2.Build.ToText()}");
        if (r1.Build.At("receiver_1/feed") == null || r1.Build.At("receiver_1/ammo") == null || r1.Build.At("receiver_1/barrel") == null
            || r2.Build.At("receiver_1/barrel")?.Part.Name != "Standard Barrel" || r2.Build.At("receiver_1/feed") != null || r2.Build.At("receiver_1/ammo") != null
            || BuildRules.Check(r2.Build).Any(p => !p.Incomplete))
            Fail("workbench: a swapped receiver should keep the parts that fit its bore and drop the rest");

        // ---- Tree click: a part row jumps the parts list to its category ----
        if (Ui.TreeRowFilter(PartCategory.Feed, true, -1) != (int)PartCategory.Feed || Ui.TreeRowFilter(PartCategory.Feed, false, 3) != 3)
            Fail("workbench: a tree row click should select the part's category chip when the list shows it, else leave the filter");

        // ---- Move, remove, undo / redo, auto-place, auto-complete ---------------------------------------------
        var mv = new BenchModel(BuildOf("Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball}}"), BenchStock.Unlimited);
        var before = mv.Build.ToText();
        var moveWhy = mv.Move(mv.Build.At("receiver_1")!, mv.SpotOf("", "receiver_2"));
        var moved = mv.Build.At("receiver_2/feed")?.Part.Name;
        var ontoSelf = mv.WhyNot(mv.SpotOf("receiver_2", "barrel"), P("Plinker Receiver"), mv.Build.At("receiver_2"));
        var gripRemove = mv.Remove(mv.Build.Root!);
        mv.Remove(mv.Build.At("receiver_2/barrel")!);
        var afterRemove = mv.Build.At("receiver_2/barrel");
        mv.Undo();
        var undone = mv.Build.At("receiver_2/barrel") != null;
        mv.Undo();
        var backToStart = mv.Build.ToText() == before;
        mv.Redo();
        Line($"move: {moveWhy ?? "ok"} (receiver_2 feed {moved}); onto itself: {ontoSelf}; grip remove: {gripRemove}; undo x2 back to start {backToStart}; redo {mv.Build.At("receiver_2") != null}");
        if (moveWhy != null || moved != "Stick Mag" || mv.Build.At("receiver_1") != null || ontoSelf == null || gripRemove == null || afterRemove != null || !undone || !backToStart)
            Fail("workbench: move / remove / undo / redo misbehaved");
        var ap = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver}"), BenchStock.Unlimited);
        var apWhy = ap.AutoPlace(P("Box Mag"), out var apNode);
        Line($"double-click: Box Mag went to {apNode?.Path() ?? apWhy}");
        if (apNode?.Path() != "receiver_1/feed")
            Fail("workbench: double-click should put a magazine on the receiver's feed");
        var practice = BenchStock.Practice(Parts.All);
        var ac = new BenchModel(new WeaponBuild("Auto", P("Service Rifle Grip")), practice);
        var placed = ac.AutoComplete(practice.Parts(Parts.All));
        Line($"auto-complete (practice parts): {placed} part(s): {ac.Build.ToText()}; complete {ac.Asm.Ok}, missing {ac.Missing.Total}");
        if (placed < 4 || !ac.CanFinalize || !ac.CanUndo)
            Fail("workbench: auto-complete should finish an empty one-receiver grip from practice parts, as one undo step");
        ac.Undo();
        if (ac.Build.Receivers.Count != 0)
            Fail("workbench: undoing auto-complete should take every part it fitted off again");

        // ---- Copy receiver assembly -------------------------------------------------------------------------
        const string copySrc = "Pistol Grip{receiver_1:Plinker Receiver{barrel:Long Barrel{muzzle:Compensator},feed:Extended Mag,ammo:9mm Ball}}";
        var full = new BenchModel(BuildOf(copySrc), BenchStock.Unlimited);
        var fullWhy = full.CopyReceiver(full.Build.At("receiver_1")!, out var fullTarget);
        var a = full.Build.At("receiver_1")!;
        var b = full.Build.At(fullTarget?.Name ?? "receiver_2");
        var same = b != null && new WeaponBuild { Root = a.Clone() }.ToText() == new WeaponBuild { Root = b.Clone() }.ToText();
        Line($"copy (all parts): {fullWhy ?? "ok"} to {fullTarget?.Name}; same assembly {same}; missing {full.Missing.Total}; can finalise {full.CanFinalize}");
        if (fullWhy != null || fullTarget?.Name != "receiver_2" || !same || full.Missing.Total != 0 || !full.CanFinalize)
            Fail("workbench: copying a receiver assembly with every part should duplicate it whole onto the next free receiver slot");
        var stock = BenchStock.Of([(P("Pistol Grip"), 1), (P("Plinker Receiver"), 2), (P("Long Barrel"), 1), (P("Compensator"), 1), (P("Extended Mag"), 1), (P("9mm Ball"), 2), (P("Semi-Auto Trigger"), 2)]);
        var cp = new BenchModel(BuildOf(copySrc), stock);
        var cpWhy = cp.CopyReceiver(cp.Build.At("receiver_1")!, out _);
        var text = cp.Missing.Text();
        var copyNodes = cp.Build.At("receiver_2")?.Walk().ToHashSet() ?? [];
        var redRows = cp.Rows.Count(r => r.Missing);
        var redSpots = cp.Spots.Count(x => x.Missing);
        var ghosts = cp.Asm.Model?.Mesh.Parts.Count(x => x.Alpha < 0.5f) ?? 0;
        Line($"copy (short of parts): {cpWhy ?? "ok"}; {text}; {redRows} red row(s), {redSpots} red socket(s), {ghosts} ghost box(es); can finalise {cp.CanFinalize}");
        Line($"  finalise tip: {cp.FinalizeTip().Replace('\n', ' ')}");
        if (cpWhy != null || text != "Missing: 1x Long Barrel, 1x Compensator, 1x Extended Mag" || cp.Missing.Total != 3
            || !cp.Missing.Nodes.All(copyNodes.Contains) || redRows != 3 || redSpots != 3 || ghosts == 0 || cp.CanFinalize || !cp.Asm.Ok
            || !cp.FinalizeTip().Contains("Missing: 1x Long Barrel"))
            Fail("workbench: a copy without the parts should list exactly what's missing, mark the copy's parts red and block finalising");
        var craft = cp.Missing.CraftCost(Rarity.Uncommon);
        var craftCommon = cp.Missing.CraftCost(Rarity.Common);
        Line($"craft the missing parts: {craft} scrap (round cap uncommon), cap common: {craftCommon}");
        if (craft != 3 * (P("Long Barrel").Scrap + P("Compensator").Scrap + P("Extended Mag").Scrap) || craftCommon != -1)
            Fail("workbench: crafting the missing parts should cost 3x their scrap, and be refused above the round's tier");
        cp.SetStock(BenchStock.Of([(P("Pistol Grip"), 1), (P("Plinker Receiver"), 2), (P("Long Barrel"), 2), (P("Compensator"), 2), (P("Extended Mag"), 2), (P("9mm Ball"), 2), (P("Semi-Auto Trigger"), 2)]));
        if (cp.Missing.Total != 0 || !cp.CanFinalize)
            Fail("workbench: once the missing parts are owned the copied build should finalise");
        var one = new BenchModel(BuildOf("Service Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball}}"), BenchStock.Unlimited);
        var oneWhy = one.CopyReceiver(one.Build.At("receiver_1")!, out _);
        Line($"copy on a one-receiver grip: {oneWhy}");
        if (oneWhy == null || !oneWhy.Contains("no free receiver slot"))
            Fail("workbench: copying on a full grip should be refused with the reason");
        // Dragging needs a free part (the stock), the copy doesn't.
        var none2 = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver}"), BenchStock.Of([(P("Service Rifle Grip"), 1), (P("Workhorse Receiver"), 1)]));
        var noMag = none2.WhyNot(none2.SpotOf("receiver_1", "feed"), P("Box Mag"));
        if (noMag == null || !noMag.Contains("no Box Mag"))
            Fail($"workbench: dropping a part you don't have should be refused (got: {noMag ?? "allowed"})");
        // The live preview: the part highlighted in place.
        var trial = rifle.TrialPlace(rifle.SpotOf("", "foregrip"), P("Vertical Foregrip"));
        var lit = trial?.Model?.Mesh.Parts.Count(x => x.Mode == 2) ?? 0;
        if (trial?.PreviewDef == null || lit == 0 || rifle.Build.Root!.Child("foregrip") != null)
            Fail("workbench: hovering a socket should preview the part in place (highlighted) without changing the build");

        // ---- Save / load through a temp file (never the real workbench.txt) ------------------------------------
        var savedSlots = (WeaponBuild?[])BuildStore.Slots.Clone();
        var (savedMelee, savedSel, savedSecond, savedExtra, savedDummies) = (BuildStore.Melee, BuildStore.Selected, BuildStore.Second, BuildStore.ExtraSlotsUnlocked, BuildStore.Dummies);
        var temp = Path.Combine(Path.GetTempPath(), $"cos_workbench_test_{Environment.ProcessId}.txt");
        try
        {
            Array.Clear(BuildStore.Slots);
            BuildStore.Slots[0] = full.Build.Clone();
            BuildStore.Slots[1] = rifle.Build.Clone();
            BuildStore.Slots[2] = old.Clone();
            File.WriteAllLines(temp, BuildStore.Lines());
            var want = BuildStore.Slots.Select(x => x?.ToText()).ToArray();
            Array.Clear(BuildStore.Slots);
            BuildStore.Load(File.ReadAllLines(temp), Parts.Find, out var dropped);
            var got = BuildStore.Slots.Select(x => x?.ToText()).ToArray();
            Line($"save/load (temp file): {got.Count(x => x != null)} slot(s), dropped {dropped.Count}, round trip {want.SequenceEqual(got)}");
            if (!want.SequenceEqual(got) || dropped.Count > 0)
                Fail("workbench: builds (new grips, copied receivers, an old multi-receiver save) should save and load unchanged");
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
            Array.Copy(savedSlots, BuildStore.Slots, BuildStore.SlotCount);
            (BuildStore.Melee, BuildStore.Selected, BuildStore.Second, BuildStore.ExtraSlotsUnlocked, BuildStore.Dummies) = (savedMelee, savedSel, savedSecond, savedExtra, savedDummies);
        }

        // ---- No allocations per frame (the calls the UI makes every frame) -------------------------------------
        var frameModel = cp;
        var flags = new bool[frameModel.Spots.Count];
        frameModel.ValidSpots(P("Stub Barrel"), flags);
        long bestBytes = long.MaxValue;
        var sink = 0;
        for (var round = 0; round < 3; round++)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 600; f++)
            {
                var c = cam;
                c.Yaw += f * 0.001f;
                c.Matrices(1.6f, out var p1, out var v1, out var j1);
                var m1 = p1 * v1 * j1;
                for (var i = 0; i < frameModel.Spots.Count; i++)
                    if (BenchCamera.Project(frameModel.Spots[i].Pos, m1, new Vector2(10, 10), new Vector2(900, 600), out var spx, out _))
                        sink += (int)spx.X;
                if (BenchCamera.Ray(new Vector2(400, 300), m1, new Vector2(10, 10), new Vector2(900, 600), out var ro, out var rd))
                {
                    sink += BenchModel.PickRay(frameModel.Spots, flags, ro, rd, 0.05f);
                    sink += BenchModel.PickRay(frameModel.Spots, ReadOnlySpan<bool>.Empty, ro, rd, 0.05f);
                }
                sink += frameModel.Stock.Fingerprint + frameModel.Version + frameModel.Rows.Count + frameModel.Missing.Total + (frameModel.CanFinalize ? 1 : 0);
                for (var i = 0; i < frameModel.Rows.Count; i++)
                    sink += frameModel.Rows[i].Depth;
            }
            bestBytes = Math.Min(bestBytes, GC.GetAllocatedBytesForCurrentThread() - start);
        }
        Line($"per-frame calls (camera, projection, ray, pick, stock fingerprint, rows) x600: {bestBytes} bytes allocated (best of 3; {sink & 1})");
        if (bestBytes > 0)
            Fail($"workbench: the per-frame model calls allocated {bestBytes} bytes over 600 frames (expected none)");
    }
}
