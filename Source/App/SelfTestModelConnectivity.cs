using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static Dictionary<string, string> AllowedSeparated => BuilderPartsCheck.AllowedSeparated;
    private static Dictionary<string, string> AllowedFreeRows => BuilderPartsCheck.AllowedFreeRows;

    // The connectivity helpers are BuilderPartsCheck's (--check-builder-parts runs the same checks from the command line).
    private static List<int> FloatingBoxes(IReadOnlyList<ModelBox> boxes, int anchor, ModelBox[]?[]? proxies = null) => BuilderPartsCheck.FloatingBoxes(boxes, anchor, proxies);

    private static ModelBox[]?[] Proxies(IReadOnlyList<Matrix4x4> mats, IReadOnlyList<ushort> shapes) => BuilderPartsCheck.Proxies(mats, shapes);

    /// <summary>
    /// Model connectivity (the user, 2026-10-23: "remove the weird frame pieces that float above the grips"): no box of
    /// any part floats free of the rest of its part (on its own, drawn as on an empty grip), and no part floats free of an
    /// assembled weapon: every root grip empty, with one receiver, with every receiver socket filled (a rifle receiver
    /// and a short pistol one), with an optic and with every other socket filled, and every standard build. The rules
    /// BuildModel draws by are checked too: a grip's iron sights only with a receiver fitted and no optic, "with:SOCKET"
    /// boxes only while that socket holds a part. Same check as --check-builder-parts (BuilderPartsCheck.cs).
    /// </summary>
    private static void CheckModelConnectivity()
    {
        // The check itself: 5 cm apart floats, 0.5 cm apart and a turned box touching don't.
        var probe = new[]
        {
            ModelBox.Of(Matrix4x4.CreateScale(0.1f, 0.01f, 0.1f)),
            ModelBox.Of(Matrix4x4.CreateScale(0.01f, 0.05f, 0.01f) * Matrix4x4.CreateTranslation(0, 0.035f, 0)),
            ModelBox.Of(Matrix4x4.CreateScale(0.02f) * Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateTranslation(0.064f, 0, 0)),
            ModelBox.Of(Matrix4x4.CreateScale(0.01f) * Matrix4x4.CreateTranslation(0, 0.2f, 0)),
        };
        var probeFloat = FloatingBoxes(probe, -1);
        Line($"probe: {probe.Length} boxes, floating {string.Join(",", probeFloat)} (want 3)");
        if (probeFloat.Count != 1 || probeFloat[0] != 3)
            Fail("connectivity: the probe should find exactly the box 15 cm above the plate floating");

        // ---- 1. Every part on its own, drawn as on an empty grip ----
        int checkedParts = 0, allowed = 0;
        foreach (var p in Parts.All)
        {
            var alone = new BuildNode(p);
            var shown = p.Boxes.Where(b => BuildModel.Shown(b, alone, false)).ToArray();
            var mats = shown.Select(b => Matrix4x4.CreateScale(b.Size) * Matrix4x4.CreateFromQuaternion(b.Rotation) * Matrix4x4.CreateTranslation(b.Pos)).ToArray();
            var boxes = mats.Select(ModelBox.Of).ToArray();
            checkedParts++;
            var fl = FloatingBoxes(boxes, -1, Proxies(mats, shown.Select(b => b.ShapeId).ToArray()));
            if (fl.Count == 0)
                continue;
            if (AllowedSeparated.TryGetValue(p.Name, out var why))
            {
                allowed++;
                Line($"  allowed: {p.Name}: {fl.Count} separate box(es) ({why})");
            }
            else
                Fail($"connectivity: {p.Name} ({PartDef.CategoryName(p.Category)}) has {fl.Count} box(es) floating free of the rest of the part (at {string.Join(", ", fl.Select(i => FmtPos(boxes[i].C)))})");
        }
        Line($"parts on their own: {checkedParts} checked, {allowed} on the allow-list");

        // Drum magazines hang under the receiver with their flat faces to the gun's left and right: every round cylinder
        // box (a cylinder is a prism along its own Z) has its axis along the gun's X axis (the playtest board, drum-mag rotation).
        foreach (var drum in new[] { "Drum Mag", "Heavy Drum", "Shell Drum", "Conga Drum" })
        {
            var dp = Parts.Find(drum);
            if (dp == null)
            {
                Fail($"drums: no part called '{drum}'");
                continue;
            }
            var drumCyl = 0;
            foreach (var b in dp.Boxes)
            {
                if (b.Shape != "cylinder" || MathF.Abs(b.Size.X - b.Size.Y) > 1e-5f)
                    continue;
                drumCyl++;
                var axis = Vector3.Transform(Vector3.UnitZ, b.Rotation);
                if (MathF.Abs(axis.X) < 0.999f)
                    Fail($"drums: {drum} has a round cylinder whose axis is ({axis.X:0.##}, {axis.Y:0.##}, {axis.Z:0.##}), not the gun's X axis");
            }
            if (drumCyl == 0)
                Fail($"drums: {drum} has no round cylinder");
        }
        foreach (var name in AllowedSeparated.Keys)
            if (Parts.Find(name) == null)
                Fail($"connectivity: the allow-list names '{name}', which isn't a part any more");

        // ---- 2. Root grips assembled ----
        PartDef? P(string name) => Parts.Find(name);
        var fill = new Dictionary<string, PartDef?>
        {
            ["stock"] = P("Fixed Stock"), ["foregrip"] = P("Vertical Foregrip"), ["hopper"] = P("Manifold Hopper"), ["utility"] = P("Laser Sight"),
        };
        var optic = P("Red Dot");
        var builds = 0;
        foreach (var g in Parts.All.Where(x => x.Frame != null))
        {
            foreach (var recvName in new[] { "Workhorse Receiver", "Plinker Receiver" })
            {
                if (P(recvName) is not { } recv)
                {
                    Fail($"connectivity: needs the part '{recvName}'");
                    continue;
                }
                var recvSockets = g.Sockets.Where(s => s.Type == "receiver").ToList();
                for (var stage = 0; stage < 5; stage++)
                {
                    var b = new WeaponBuild("Connectivity", g);
                    if (stage >= 1)
                        foreach (var s in stage == 1 ? recvSockets.Take(1) : recvSockets)
                            b.Root!.Attach(s.Name, recv);
                    if (stage >= 3 && optic != null)
                        foreach (var s in g.Sockets.Where(s => s.Type == "optic"))
                            b.Root!.Attach(s.Name, optic);
                    if (stage >= 4)
                        foreach (var s in g.Sockets)
                            if (fill.TryGetValue(s.Type, out var part) && part != null)
                                b.Root!.Attach(s.Name, part);
                    builds++;
                    CheckBuildConnected($"{g.Name} [{(stage switch { 0 => "empty", 1 => "one", 2 => "every", 3 => "every + optic", _ => "everything" })} {(stage == 0 ? "" : recvName)}]", b);
                }
            }
        }

        // ---- 3. Standard builds ----
        foreach (var sb in StandardBuilds.All)
        {
            builds++;
            CheckBuildConnected($"standard build {sb.Name}", sb.Build());
        }
        Line($"assembled: {builds} builds checked (every root grip x 5 stages x 2 receivers, and {StandardBuilds.All.Length} standard builds)");

        // ---- The drawing rules ----
        if (P("Pistol Grip") is { } pg && P("Plinker Receiver") is { } plinker && optic != null)
        {
            int Irons(WeaponBuild b) => BuildModel.Build(b, Vector3.One).Mesh.Parts.Count(x => (x.Flags & GunPartFlags.Top) != 0);
            var empty = new WeaponBuild("t", pg);
            var fitted = new WeaponBuild("t", pg);
            fitted.Root!.Attach("receiver_1", plinker);
            var scoped = new WeaponBuild("t", pg);
            scoped.Root!.Attach("receiver_1", plinker);
            scoped.Root!.Attach("optic", optic);
            int ie = Irons(empty), ifit = Irons(fitted), isc = Irons(scoped);
            var m = BuildModel.Build(scoped, Vector3.One).Mesh;
            Line($"irons on the Pistol Grip: empty {ie}, with a receiver {ifit}, with a receiver and an optic {isc}; optic mount at y {m.Mount:0.000}, z {m.OpticZ:0.000}");
            if (ie != 0 || ifit != 2 || isc != 0)
                Fail("connectivity: a grip's iron sights should show only with a receiver fitted and no optic");
            // The optic still sits on the grip's optic socket (the sight lens height comes from it).
            var want = pg.Sockets.First(s => s.Type == "optic").Pos + BuildModel.GripAnchor - (pg.Hand ?? Vector3.Zero);
            // (A receiver taller than the socket raises it to the receiver's top.)
            if (m.Mount < want.Y - 1e-4f || m.Mount > want.Y + 0.01f || MathF.Abs(m.OpticZ - want.Z) > 1e-4f)
                Fail($"connectivity: the optic mount moved (y {m.Mount:0.000} z {m.OpticZ:0.000}, want {want.Y:0.000} {want.Z:0.000})");
        }
        if (P("Rifle Grip") is { } rg && P("Workhorse Receiver") is { } wh && fill["hopper"] is { } hop)
        {
            var bare = new WeaponBuild("t", rg);
            bare.Root!.Attach("receiver_1", wh);
            var fed = new WeaponBuild("t", rg);
            fed.Root!.Attach("receiver_1", wh);
            fed.Root!.Attach("hopper", hop);
            var chutes = rg.Boxes.Count(b => b.Tag == "with:hopper");
            int nb = BuildModel.Build(bare, Vector3.One).Mesh.Parts.Length, nf = BuildModel.Build(fed, Vector3.One).Mesh.Parts.Length;
            var hopperBoxes = hop.Boxes.Sum(b => b.Shape == "rod" ? 2 : 1);
            Line($"Rifle Grip hopper chute: {chutes} box(es) tagged with:hopper; {nb} boxes without a hopper, {nf} with one ({hopperBoxes} hopper boxes)");
            if (chutes != 1 || nf != nb + hopperBoxes + chutes)
                Fail("connectivity: a \"with:hopper\" box should be drawn only with a hopper fitted");
        }
        try
        {
            PartFile.Load("""
                {"format": "call-of-slop-part/1", "name": "Bad Tag Test", "category": "stock", "rarity": "common", "weight": 0.1,
                 "model": [{"size": [0.1, 0.1, 0.1], "tag": "with:nowhere"}]}
                """, "bad-tag.json");
            Fail("connectivity: a box tagged with:SOCKET for a socket the part doesn't have should be refused");
        }
        catch (WeaponFileException e)
        {
            Line($"refused: {e.Message}");
            if (!e.Message.Contains("nowhere"))
                Fail("connectivity: the refusal of a bad with: tag should name the socket");
        }
    }

    /// <summary>Fails if any part of <paramref name="b"/>'s model floats free of the grip (allow-lists aside).</summary>
    private static void CheckBuildConnected(string what, WeaponBuild b)
    {
        var model = BuildModel.Build(b, Vector3.One);
        var boxes = model.Mesh.Matrices.Select(ModelBox.Of).ToArray();
        var fl = FloatingBoxes(boxes, 0, Proxies(model.Mesh.Matrices, model.Mesh.Parts.Select(x => x.Shape).ToArray()));
        if (fl.Count == 0)
            return;
        // Who owns each floating box: the deepest build node whose box range holds it.
        var ranges = b.Nodes.Select(n => (Node: n, Range: BuildModel.NodeRange(model, n))).ToList();
        BuildNode? Owner(int i) => ranges.Where(r => i >= r.Range.From && i < r.Range.To).OrderBy(r => r.Range.To - r.Range.From).Select(r => r.Node).FirstOrDefault();
        var lowest = b.Root!.Part.Sockets.Where(s => s.Type == "receiver").Select(s => s.Pos.Y).DefaultIfEmpty(0).Min();
        var bad = new List<string>();
        foreach (var i in fl)
        {
            var o = Owner(i);
            if (o == null)
                continue;
            if (AllowedSeparated.ContainsKey(o.Part.Name))
                continue; // an allowed part's own separated pieces (fins with no barrel through them yet)
            // A free-row grip: a receiver above the bottom row (and what it carries) may hang free.
            var top = o;
            while (top.Parent != null && top.Parent != b.Root)
                top = top.Parent;
            if (AllowedFreeRows.ContainsKey(b.Root.Part.Name) && top.ParentSocket is { Type: "receiver" } rs && rs.Pos.Y > lowest + 1e-4f)
                continue;
            bad.Add($"{o.Part.Name} at {FmtPos(boxes[i].C)}");
        }
        if (bad.Count > 0)
            Fail($"connectivity: {what}: {bad.Count} box(es) float free of the weapon: {string.Join("; ", bad.Distinct().Take(6))}");
    }

    private static string FmtPos(Vector3 v) => $"({v.X:0.000}, {v.Y:0.000}, {v.Z:0.000})";
}
