using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-material [LAG] [LOSS]</c>: the material painter (PartMaterials.cs; docs/sandbox/CONSTRUCTS.md, "The material painter")
/// end to end. A command-line check, not a self-test section. Offline: what the painter makes of a steel plate in each material, and
/// a sheet with parts of other materials through a blueprint, a client's Full and a world save (each part's name must come back).
/// Then a host and a client over RakNet on this PC with the client's network simulated bad: the client's own requests
/// (CActKind.Material) remake one part, sweep a row and do the whole sheet, each step waiting (bounded) for the client's copy to
/// match the host's; and the survival rules on the host (the cost in carried wood, a refusal when there isn't enough, salvage back).
/// </summary>
public static partial class SelfTest
{
    public static int BenchMaterial(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--bench-material", StringComparison.OrdinalIgnoreCase));
        var lag = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var l) ? l : 120;
        var loss = at >= 0 && at + 2 < args.Length && float.TryParse(args[at + 2], System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 3f;
        var fails = 0;
        var offered = PartMaterials.Offered;
        var paintable = ConstructParts.All.Count(PartMaterials.Paintable);
        Console.WriteLine($"[material] {offered.Length} materials ({string.Join(", ", offered)}), {paintable} of {ConstructParts.All.Count} part types can be remade");
        var plate = ConstructParts.Need("Plate Steel 0.5 m");
        for (var k = 0; k < offered.Length; k++)
            if (PartMaterials.Variant(plate, k) is { } v)
                Console.WriteLine($"[material]   {v.Name}: {v.Mass:0.00} kg, {v.Health:0} hp, armour {v.Armour:0.#}, salvages as {v.Material}, {PartMaterials.UnitsFor(v)} {offered[k]}; found by name {ReferenceEquals(ConstructParts.Find(v.Name), v)}");
            else
            {
                Console.WriteLine($"[material]   {plate.Name} in {offered[k]}: NOT MADE");
                fails++;
            }
        fails += RoundTrips(offered);
        fails += Online(lag, loss);
        Console.WriteLine($"[material] done: {fails} problem(s), {NetSafety.Dropped} packets dropped{(NetSafety.LastDrop.Length > 0 ? ": " + NetSafety.LastDrop : "")}");
        return fails;
    }

    private static string Names(Construct c)
    {
        var parts = new string[c.PartCount];
        for (var i = 0; i < c.PartCount; i++)
            parts[i] = c.DefOf(i).Name;
        Array.Sort(parts, StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    private static int RoundTrips(string[] offered)
    {
        var block = ConstructParts.Need("Frame Block");
        var w = new ConstructWorld();
        var c = w.Create(new Vector3(0, 1, 0), Quaternion.Identity)!;
        var size = block.BoundsMax - block.BoundsMin;
        for (var i = 0; i < 24; i++)
            w.AddPart(c, PartMaterials.Variant(block, i % offered.Length) ?? block, new Vector3(i % 6 * size.X, 0, i / 6 * size.Z), 0);
        w.AutoLinkMany(c, 0, c.PartCount);
        c.Rebuild();
        var want = Names(c);
        var clip = ConstructClip.Of(c, "material round trip", w);
        var json = clip.ToBlueprint("material round trip", "", out var why);
        var built = json == null ? null : ConstructBuild.Build(new ConstructWorld(), ConstructBuild.ParseBlueprint(json, "material round trip"), Vector3.Zero, Quaternion.Identity, out why);
        var full = new PacketWriter(1 << 16);
        ConstructNet.WriteFull(full, c, 1);
        var other = new ConstructWorld();
        var r = new PacketReader(full.Span);
        r.U8();
        ConstructNet.Read(ref r, other, 0);
        var copy = other.Get(c.Id);
        var snap = new WorldSnapshot();
        w.CaptureInto(snap);
        var names = new WorldNames();
        var bodyBytes = new SaveWriter();
        ConstructWorld.WriteBody(bodyBytes, snap, snap.Bodies[0], names);
        var nameBytes = new SaveWriter();
        names.Write(nameBytes);
        var read = new WorldNamesRead();
        read.Read(new SaveReader(nameBytes.Span.ToArray()));
        var loaded = new ConstructWorld().ReadBody(new SaveReader(bodyBytes.Span.ToArray()), read, _ => 0);
        var bp = built != null && Names(built) == want;
        var wire = copy != null && Names(copy) == want;
        var save = loaded != null && Names(loaded) == want;
        Console.WriteLine($"[material] round trips of {c.PartCount} parts in {offered.Length} materials ({c.Mass:0} kg): blueprint {(bp ? "same" : built == null ? "refused: " + why : "DIFFERENT")}, a client's Full {(wire ? "same" : "DIFFERENT")} ({(copy != null ? $"{copy.Mass:0} kg" : "none")}), a world save {(save ? "same" : "DIFFERENT")}");
        return (bp ? 0 : 1) + (wire ? 0 : 1) + (save ? 0 : 1);
    }

    private static int Online(int lag, float loss)
    {
        Brutal.GlfwApi.Glfw.Init();
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Console.WriteLine("[material net] no yard map");
            return 1;
        }
        const ushort port = 26393;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Console.WriteLine($"[material net] simulating {lag} ms and {loss}% loss on the client: {a.Net.SimulateBadNetwork(lag, lag / 5, loss)}");
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 60 && (server.Phase != MatchPhase.Playing || a.LocalId == byte.MaxValue); wait++)
            Pump(server, [a], 0.25, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Console.WriteLine("[material net] the client didn't join");
            return 1;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.5, ref now, clock);
        var plate = ConstructParts.Need("Plate Steel 0.5 m");
        var w = server.Constructs;
        var c = w.Create(sp.Position + new Vector3(1.5f, 0.2f, 0), Quaternion.Identity)!;
        var size = plate.BoundsMax - plate.BoundsMin;
        for (var i = 0; i < 16; i++)
            w.AddPart(c, plate, new Vector3(i % 4 * size.X, 0, i / 4 * size.Z), 0);
        w.AutoLinkMany(c, 0, c.PartCount);
        c.Pinned = true;
        c.Rebuild();
        w.StructureDirty.Add(c.Id);
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Material);
        int wood = PartMaterials.IndexOf("wood"), stone = PartMaterials.IndexOf("stone");
        var fails = 0;
        bool Wait(string what, Func<bool> ok)
        {
            for (var t = 0; t < 40 && !ok(); t++)
                Pump(server, [a], 0.1, ref now, clock);
            var got = ok();
            var copy = a.Constructs.Get(c.Id);
            if (c.Dirty)
                c.Rebuild();
            if (copy is { Dirty: true })
                copy.Rebuild();
            Console.WriteLine($"[material net] {what}: {(got ? "ok" : "NOT REACHED")} after {clock.Elapsed.TotalSeconds:0.0} s; host {c.Mass:0.0} kg, client {(copy != null ? $"{copy.Mass:0.0} kg" : "no copy")}; notice \"{a.SandboxNotice}\"");
            fails += got ? 0 : 1;
            return got;
        }
        bool Same() => a.Constructs.Get(c.Id) is { } k && k.PartCount == c.PartCount && Names(k) == Names(c);
        int Count(Construct k, string m)
        {
            var n = 0;
            for (var i = 0; i < k.PartCount; i++)
                n += PartMaterials.MaterialOf(k.DefOf(i)) == m ? 1 : 0;
            return n;
        }
        Wait("the client has the material painter and the sheet", () => server.SandboxHands.Hand(sp.Id).Tool == ConstructTool.Material && a.Constructs.Get(c.Id) is { PartCount: 16 });
        a.SandboxMaterial(c.Id, PaintMode.Part, 5, wood);
        Wait("one part remade in wood by the client", () => Count(c, "wood") == 1 && Same());
        for (var i = 0; i < 4; i++)
            a.SandboxMaterial(c.Id, PaintMode.Part, i, stone); // (a sweep along a row: one request a part)
        Wait("a row swept in stone", () => Count(c, "stone") == 4 && Same());
        a.SandboxMaterial(c.Id, PaintMode.Reset, 1, -1);
        Wait("a part back to its own material", () => Count(c, "stone") == 3 && Same());
        a.SandboxMaterial(c.Id, PaintMode.Whole, 9, wood);
        Wait("the whole sheet in wood", () => Count(c, "wood") == 16 && Same());
        // Survival on the host: the client's player carries 10 wood; the sheet back to steel costs steel they don't have.
        var hands = server.SandboxHands;
        var inv = hands.Inventory!.Of(sp.Id);
        bool keepInf = hands.InfiniteParts, keepInv = inv.Infinite;
        hands.InfiniteParts = false;
        inv.Infinite = false;
        var names = RecyclingTable.Current;
        byte woodId = names.IdOf("wood"), steelId = names.IdOf("steel"), metalId = names.IdOf("metal");
        inv.TakeMaterial(steelId, 9999);
        var eye = sp.Eye;
        var noSteel = hands.Rematerial(w, sp.Id, eye, c.Id, PaintMode.Reset, 2, -1);
        inv.AddMaterial(steelId, 10);
        var metal0 = inv.Materials[metalId];
        var w0 = inv.Materials[woodId];
        var paid = hands.Rematerial(w, sp.Id, eye, c.Id, PaintMode.Reset, 2, -1);
        Console.WriteLine($"[material net] survival: with no steel \"{noSteel ?? "ok"}\"; with 10 steel \"{paid ?? "ok"}\", steel left {inv.Materials[steelId]} (a plate takes {PartMaterials.UnitsFor(plate)}), wood back {inv.Materials[woodId] - w0}, metal {inv.Materials[metalId] - metal0}");
        if (noSteel == null || paid != null || inv.Materials[steelId] != 10 - PartMaterials.UnitsFor(plate) || inv.Materials[woodId] <= w0)
            fails++;
        hands.InfiniteParts = keepInf;
        inv.Infinite = keepInv;
        Wait("the paid part reached the client", () => Count(c, "steel") == 1 && Same());
        return fails;
    }
}
