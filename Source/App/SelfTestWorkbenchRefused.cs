namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Pressing Build on an incomplete weapon (BenchModel.RefuseBuild): the required-but-empty sockets of every receiver
    /// subtree (barrel, feed, ammo, action, a splitter's outputs) are marked and nothing optional (stock, optic, muzzle,
    /// utility...) is; a complete build marks none; an empty multi-slot grip marks one receiver slot; the marks clear one
    /// by one as the sockets are filled (for good: undo brings the socket back yellow), the rest survive undo, and all go when another build is loaded; the sandbox's practice
    /// stock (missing parts) refuses for that reason with no sockets marked; re-marking allocates nothing per frame.
    /// </summary>
    private static void CheckWorkbenchRefused()
    {
        static string Marked(BenchModel m) => string.Join(", ", m.Spots.Where(s => s.Refused).Select(s => $"{(s.ParentPath.Length == 0 ? "grip" : s.ParentPath)}/{s.Socket.Name}"));

        // A receiver with its action but no barrel, feed or ammo: exactly those three.
        var bare = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{action:Full-Auto Sear}}"), BenchStock.Unlimited);
        var n = bare.RefuseBuild();
        var types = bare.Spots.Where(s => s.Refused).Select(s => s.Socket.Type).OrderBy(t => t).ToList();
        var optional = bare.Spots.Count(s => s.Refused && !s.Socket.Required);
        var emptyOptional = bare.Spots.Count(s => s.Child == null && !s.Socket.Required);
        Line($"bare receiver: can finalise {bare.CanFinalize}, refused -> {n} marked ({Marked(bare)}); {emptyOptional} empty optional sockets, {optional} of them marked");
        if (bare.CanFinalize || n != 3 || bare.RefusedCount != 3 || string.Join(",", types) != "ammo,barrel,feed" || optional != 0)
            Fail("workbench refused: a receiver missing barrel, feed and ammo should mark exactly those three sockets (nothing optional)");

        // A complete build: nothing to mark (it can be built).
        var full = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:FMJ}}"), BenchStock.Unlimited);
        var nFull = full.RefuseBuild();
        Line($"complete rifle (no stock, optic or muzzle): can finalise {full.CanFinalize}, {nFull} marked");
        if (!full.CanFinalize || nFull != 0 || full.Spots.Any(s => s.Refused))
            Fail("workbench refused: a complete build should mark no sockets");

        // An empty three-slot grip: one receiver slot, not three.
        var grip = new BenchModel(BuildOf("Triple Rifle Grip"), BenchStock.Unlimited);
        var nGrip = grip.RefuseBuild();
        var needed = grip.Spots.Count(s => s.Needed);
        Line($"empty Triple Rifle Grip: {needed} sockets needed (yellow), refused -> {nGrip} marked ({Marked(grip)})");
        if (nGrip != 1 || grip.Spots.Count(s => s.Refused && s.Socket.Type == "receiver") != 1)
            Fail("workbench refused: an empty multi-receiver grip should mark one receiver slot");

        // Filling clears its mark only; undo brings the empty socket back marked; loading another build clears them all.
        var barrel = bare.SpotOf("receiver_1", "barrel");
        var why = bare.Place(barrel, Parts.Find("Standard Barrel")!, out _);
        var afterBarrel = Marked(bare);
        var undone = bare.Undo();
        var afterUndo = Marked(bare);
        var redone = bare.Redo();
        bare.Place(bare.SpotOf("receiver_1", "feed"), Parts.Find("Box Mag")!, out _);
        bare.Place(bare.SpotOf("receiver_1", "ammo"), Parts.Find("FMJ")!, out _);
        var afterAll = bare.RefusedCount;
        Line($"filling: barrel placed ({why ?? "ok"}) -> {afterBarrel}; undo {undone} -> {afterUndo}; redo {redone}, feed and ammo placed -> {afterAll} marked, can finalise {bare.CanFinalize}");
        if (why != null || afterBarrel != "receiver_1/feed, receiver_1/ammo" && afterBarrel != "receiver_1/ammo, receiver_1/feed" || bare.Spots.Count(s => s.Refused) != 0 || afterAll != 0 || !bare.CanFinalize)
            Fail("workbench refused: each mark should clear as its socket is filled");
        if (!undone || afterUndo != afterBarrel)
            Fail("workbench refused: a filled socket's mark is gone for good (undo brings the socket back yellow, the other marks stay)");
        var reload = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{action:Full-Auto Sear}}"), BenchStock.Unlimited);
        reload.RefuseBuild();
        reload.SetBuild(BuildOf("Sport Rifle Grip{receiver_1:Workhorse Receiver{action:Full-Auto Sear}}"));
        Line($"another build loaded: {reload.RefusedCount} marked (its empty sockets stay yellow: {reload.Spots.Count(s => s.Needed)})");
        if (reload.RefusedCount != 0 || reload.Spots.Any(s => s.Refused))
            Fail("workbench refused: loading another build should clear the marks");

        // Complete but missing a part you own: refused for that, no socket marked.
        var practice = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:FMJ}}"), BenchStock.Of([]));
        var nMissing = practice.RefuseBuild();
        Line($"complete build, nothing owned: can finalise {practice.CanFinalize}, {nMissing} sockets marked, tip '{practice.FinalizeTip().Split('\n')[0]}'");
        if (practice.CanFinalize || nMissing != 0)
            Fail("workbench refused: a complete build missing owned parts is refused without socket marks");

        // A splitter's empty outputs count as needed.
        var split = new BenchModel(BuildOf("Sport Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Y-Splitter,feed:Shell Tube,ammo:Buckshot}}"), BenchStock.Unlimited);
        var nSplit = split.RefuseBuild();
        Line($"empty Y-Splitter: {nSplit} marked ({Marked(split)})");
        if (nSplit != 2 || split.Spots.Count(s => s.Refused && s.Parent.Part.Category == PartCategory.Splitter) != 2)
            Fail("workbench refused: a splitter's two empty barrel outputs should be marked");

        // Re-marking with nothing changed (what a frame does: reads Spots) allocates nothing; RefuseBuild itself reuses its lists.
        var m2 = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{action:Full-Auto Sear}}"), BenchStock.Unlimited);
        m2.RefuseBuild();
        long best = long.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            var on = 0;
            for (var f = 0; f < 600; f++)
                for (var i = 0; i < m2.Spots.Count; i++)
                    if (m2.Spots[i].Refused)
                        on++;
            m2.ClearRefused();
            m2.RefuseBuild();
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - start);
            if (on != 600 * 3)
                Fail($"workbench refused: the marks changed while reading them ({on})");
        }
        Line($"reading the marks x600 frames + one re-mark: {best} bytes allocated (best of 3)");
        if (best > 0)
            Fail($"workbench refused: reading or re-marking the refused sockets allocated {best} bytes (expected none)");
    }
}
