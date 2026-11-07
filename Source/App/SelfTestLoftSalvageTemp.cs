using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>THROWAWAY (phase 1 of body styles): a lofted bay salvaged off a kit-5 vehicle, built on a player's construct, through the wire, a clip, a blueprint and a world save. Removed before the branch is done.</summary>
    private static void CheckLoftSalvageTemp()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail("loft salvage: no Construct Yard");
            return;
        }
        const ushort port = 26491;
        var keep = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.InfiniteParts = false;
        var dropped = NetSafety.Dropped;
        string name = "";
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.2, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                Pump(server, [a], 0.25, ref now, clock);
            Pump(server, [a], 0.3, ref now, clock);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
            {
                Fail("loft salvage: the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            Pump(server, [a], 0.4, ref now, clock);
            sp.ImmuneUntil = 1e9;
            server.Net.SimulateBadNetwork(30, 8, 2);
            a.Net.SimulateBadNetwork(30, 8, 2);
            bool Await(Func<bool> done, double seconds = 4)
            {
                var until = clock.Elapsed.TotalSeconds + seconds;
                while (!done() && clock.Elapsed.TotalSeconds < until)
                    Pump(server, [a], 0.05, ref now, clock);
                return done();
            }
            var w = server.Constructs;
            var g = VehicleKit.VehicleRegistry.Find("Bandit War Bus")!;
            var v = VehicleKit.VehicleRegistry.Make(g, 1);
            Line($"loft salvage: {v.Name} kit {v.Kit}, {string.Join(", ", v.Modules.Where(m => m.StartsWith("language", StringComparison.Ordinal)))}");
            var bp = ConstructBuild.ParseBlueprint(v.Text(), "loft.json");
            var spot = OpenSpot(w) ?? Vector3.Zero;
            if (ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, spot.X, spot.Z), 0, out var why, VehicleKit.VehicleChecks.TowOf(bp)) is not { } truck)
            {
                Fail($"loft salvage: the truck doesn't build: {why}");
                return;
            }
            truck.Owner = 255;
            truck.Rebuild();
            var i = Enumerable.Range(0, truck.PartCount).FirstOrDefault(k => truck.DefOf(k).IsNamedRuntime && truck.DefOf(k).Name.Contains("d ", StringComparison.Ordinal) == false, -1);
            if (i < 0)
            {
                Fail("loft salvage: no lofted bay on a kit-5 truck");
                return;
            }
            name = truck.DefOf(i).Name;
            Line($"loft salvage: bay {i} {name} ({name.Length} chars), {truck.DefOf(i).Mass} kg, physics-only {truck.DefOf(i).PhysicsWhy}, yield {RecyclingTable.Current.NameOf(RecyclingTable.Current.YieldOf(truck.Parts[i]).Material)} x{RecyclingTable.Current.YieldOf(truck.Parts[i]).Amount}");
            if (!Await(() => a.Constructs.Get(truck.Id) is { } m && m.PartCount == truck.PartCount && m.DefOf(i).Name == name))
                Fail("loft salvage: the client's truck doesn't have the same bay");
            // Salvage it as a player does: the attach tool, aimed from beside it.
            a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
            Await(() => server.SandboxHands.Hand(a.LocalId).Tool == ConstructTool.Welder);
            truck.WorldFrame(truck.Parts[i].Seg, out var q, out var t);
            truck.PartBounds(i, out var mn, out var mx);
            var mid = t + Vector3.Transform((mn + mx) * 0.5f, q);
            var feet = ConstructWorld.Ground(World.Query, mid.X - 2.2f, mid.Z) + new Vector3(0, 0.05f, 0);
            sp.Teleport(feet);
            a.Local!.Teleport(feet);
            Pump(server, [a], 0.3, ref now, clock);
            var before = w.Bodies.Select(b => b.Id).ToHashSet();
            a.SandboxSend(CActKind.Remove, body: truck.Id, part: (ushort)i);
            Construct? loose = null;
            Await(() => (loose = w.Bodies.FirstOrDefault(b => !before.Contains(b.Id) && b.PartCount == 1 && b.DefOf(0).Name == name)) != null, 8);
            foreach (var d in server.SandboxHands.Done.TakeLast(4))
                Line($"loft salvage: server: {d}");
            if (loose == null)
            {
                Fail("loft salvage: the bay didn't come off as a body of its own");
                return;
            }
            Line($"loft salvage: salvaged as body {loose.Id}, owner {loose.Owner}");
            if (!Await(() => a.Constructs.Get(loose.Id) is { PartCount: 1 } m && m.DefOf(0).Name == name))
                Fail("loft salvage: the client doesn't see the salvaged bay by its key");
            // Build with it: a Bolt Deck of the player's own, the bay stood on it as the kit stands one.
            var deckDef = ConstructParts.Need("Bolt Deck 1x2 m");
            var at = OpenSpot(w) ?? Vector3.Zero;
            var deck = ConstructBuild.PlaceNew(w, deckDef, new FreePlacement { Pos = ConstructWorld.Ground(World.Query, at.X, at.Z) + new Vector3(0, 0.3f, 0), Rot = Quaternion.Identity })!;
            deck.Owner = sp.Id;
            var bayDef = loose.DefOf(0);
            var foot = Array.FindIndex(bayDef.Sockets, s => s.Name == "footL0");
            var placed = -1;
            for (var ts = 0; ts < deckDef.Sockets.Length && placed < 0; ts++)
                for (var spin = 0; spin < 4 && placed < 0; spin++)
                    if (ConstructBuild.Place(deck, 0, ts, bayDef, foot, spin, out var gh) == null)
                        placed = ConstructBuild.Attach(w, deck, bayDef, gh);
            if (placed < 0)
                Fail("loft salvage: the salvaged bay won't go on a player's deck");
            else
            {
                w.Remove(loose);
                deck.Rebuild();
                Line($"loft salvage: built onto the player's deck as part {placed}");
                if (!Await(() => a.Constructs.Get(deck.Id) is { } m && m.PartCount == deck.PartCount && m.DefOf(placed).Name == name))
                    Fail("loft salvage: the client's copy of the player's build doesn't have the bay");
                // A clip (copy) and its paste, and a blueprint saved from it and built back.
                var clip = ConstructClip.Of(deck, "loft clip", w);
                var pasteAt = OpenSpot(w) ?? Vector3.Zero;
                var pasted = w.CopyPiece(deck, ConstructClip.WalkOrder(deck, Enumerable.Range(0, deck.PartCount).Select(x => (ushort)x).ToArray()), ConstructWorld.Ground(World.Query, pasteAt.X, pasteAt.Z) + new Vector3(0, 0.4f, 0), Quaternion.Identity, out var pwhy, w);
                if (pasted == null || !Enumerable.Range(0, pasted.PartCount).Any(k => pasted.DefOf(k).Name == name))
                    Fail($"loft salvage: a paste lost the bay ({pwhy})");
                else if (!Await(() => a.Constructs.Get(pasted.Id) is { } m && Enumerable.Range(0, m.PartCount).Any(k => m.DefOf(k).Name == name)))
                    Fail("loft salvage: the client's paste doesn't have the bay");
                else
                    Line("loft salvage: copy and paste keep the bay, host and client");
                if (clip.ToBlueprint("Loft test", "", out var bwhy) is not { } text)
                    Fail($"loft salvage: no blueprint: {bwhy}");
                else
                {
                    var bp2 = ConstructBuild.ParseBlueprint(text, "LoftTest.json");
                    var wb = new ConstructWorld();
                    var back = ConstructBuild.BuildOnGround(wb, bp2, Vector3.Zero, 0, out var bbwhy);
                    if (back == null || !Enumerable.Range(0, back.PartCount).Any(k => back.DefOf(k).Name == name))
                        Fail($"loft salvage: the blueprint didn't build the bay back ({bbwhy})");
                    else
                        Line($"loft salvage: a blueprint keeps it ({text.Length} chars)");
                }
            }
            if (NetSafety.Dropped != dropped)
                Fail($"loft salvage: {NetSafety.Dropped - dropped} packet(s) dropped");
        }
        finally
        {
            sb.InfiniteParts = keep;
        }

        // A world save with the bay on a player's build, read back.
        const string saveName = "Loft salvage test";
        var folder = WorldStore.FolderFor(saveName);
        WorldStore.Delete(folder);
        yard.RestoreProps();
        World.SetMap(yard);
        var session = Game.WorldNew(saveName, yard, false);
        var def = ConstructParts.Find(name);
        using (var game = ModeGame(0, GameMode.Sandbox, yard.Id))
        {
            game.WorldAttach(session);
            Run(game, ref now, 0.5);
            if (def != null && OpenSpot(game.Constructs) is { } spot && ConstructBuild.PlaceNew(game.Constructs, def, new FreePlacement { Pos = spot + new Vector3(0, 1, 0), Rot = Quaternion.Identity }) is { } piece)
            {
                piece.Owner = 0;
                if (!game.WorldSaveNow(true))
                    Fail("loft salvage: the world didn't save");
            }
            else
                Fail("loft salvage: no room for the save's bay");
        }
        if (WorldStore.List().FirstOrDefault(e => e.Folder == folder) is { } entry && Game.WorldOpen(entry, out _) is { } loaded)
            using (var game = ModeGame(0, GameMode.Sandbox, yard.Id))
            {
                game.WorldAttach(loaded);
                Run(game, ref now, 0.5);
                if (!game.Constructs.Bodies.Any(b => b.PartCount == 1 && b.DefOf(0).Name == name))
                    Fail("loft salvage: the bay didn't come back from a world save");
                else
                    Line("loft salvage: a world save keeps it");
            }
        else
            Fail("loft salvage: the saved world didn't open");
        WorldStore.Delete(folder);
    }
}
