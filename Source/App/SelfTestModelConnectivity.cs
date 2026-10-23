using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Intentional separated pieces in part models: the part's name and what joins them in a build. Every other part's
    /// boxes must form one connected piece on their own (docs/zombies/WEAPON-BUILDER.md, "Model building").
    /// </summary>
    private static readonly Dictionary<string, string> AllowedSeparated = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bastille LMG Receiver"] = "cooling fins round the barrel socket: the fitted barrel runs through them",
        ["Sawmill Receiver"] = "cooling fins round the barrel socket: the fitted barrel runs through them",
        ["Hammerfall Autocannon Receiver"] = "cooling fins round the barrel socket: the fitted barrel runs through them",
        ["Barbed Wire Wrap"] = "two wraps of wire round the hilt's handle: the hilt joins them",
        ["Nail Ring"] = "nails driven into the hilt's handle: the hilt joins them",
    };

    /// <summary>Grips whose upper receiver rows may hang free (part name and why).</summary>
    private static readonly Dictionary<string, string> AllowedFreeRows = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Push Cart"] = "receivers piled in the basket in rows 11 cm apart; only the bottom row stands on the floor",
    };

    /// <summary>Two boxes touch when they overlap or are at most this far apart (1 cm, and 0.1 mm for rounding).</summary>
    private const float ConnectTolerance = 0.0101f;

    /// <summary>
    /// Model connectivity (the user, 2026-10-23: "remove the weird frame pieces that float above the grips"): no box of
    /// any part floats free of the rest of its part (on its own, drawn as on an empty grip), and no part floats free of an
    /// assembled weapon: every root grip empty, with one receiver, with every receiver socket filled (a rifle receiver
    /// and a short pistol one), with an optic and with every other socket filled, and every standard build. The rules
    /// BuildModel draws by are checked too: a grip's iron sights only with a receiver fitted and no optic, "with:SOCKET"
    /// boxes only while that socket holds a part. Same check as tools/parts/floaters.py.
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
            var boxes = p.Boxes.Where(b => BuildModel.Shown(b, alone, false))
                .Select(b => ModelBox.Of(Matrix4x4.CreateScale(b.Size) * Matrix4x4.CreateFromQuaternion(b.Rotation) * Matrix4x4.CreateTranslation(b.Pos))).ToArray();
            checkedParts++;
            var fl = FloatingBoxes(boxes, -1);
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
            if (MathF.Abs(m.Mount - want.Y) > 1e-4f || MathF.Abs(m.OpticZ - want.Z) > 1e-4f)
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
        var fl = FloatingBoxes(boxes, 0);
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

    /// <summary>
    /// The boxes outside the main connected piece: the piece holding box <paramref name="anchor"/>, or (-1) the piece
    /// with the most volume.
    /// </summary>
    private static List<int> FloatingBoxes(IReadOnlyList<ModelBox> boxes, int anchor)
    {
        var n = boxes.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++)
            parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                if (Find(i) != Find(j) && boxes[i].Touches(boxes[j], ConnectTolerance))
                    parent[Find(i)] = Find(j);
        if (n == 0)
            return [];
        int main;
        if (anchor >= 0)
            main = Find(anchor);
        else
        {
            var volume = new Dictionary<int, float>();
            for (var i = 0; i < n; i++)
                volume[Find(i)] = volume.GetValueOrDefault(Find(i)) + boxes[i].H.X * boxes[i].H.Y * boxes[i].H.Z;
            main = volume.MaxBy(kv => kv.Value).Key;
        }
        var list = new List<int>();
        for (var i = 0; i < n; i++)
            if (Find(i) != main)
                list.Add(i);
        return list;
    }

    /// <summary>An oriented box: centre, unit axes and half extents (from a unit-cube world matrix).</summary>
    private readonly record struct ModelBox(Vector3 C, Vector3 X, Vector3 Y, Vector3 Z, Vector3 H)
    {
        public static ModelBox Of(Matrix4x4 w)
        {
            var x = new Vector3(w.M11, w.M12, w.M13);
            var y = new Vector3(w.M21, w.M22, w.M23);
            var z = new Vector3(w.M31, w.M32, w.M33);
            float lx = MathF.Max(x.Length(), 1e-9f), ly = MathF.Max(y.Length(), 1e-9f), lz = MathF.Max(z.Length(), 1e-9f);
            return new ModelBox(w.Translation, x / lx, y / ly, z / lz, new Vector3(lx, ly, lz) * 0.5f);
        }

        /// <summary>Separating axes: no axis splits the two boxes by more than <paramref name="tol"/>.</summary>
        public bool Touches(ModelBox o, float tol)
        {
            var t = o.C - C;
            Span<Vector3> a = [X, Y, Z];
            Span<Vector3> b = [o.X, o.Y, o.Z];
            for (var k = 0; k < 15; k++)
            {
                var axis = k < 3 ? a[k] : k < 6 ? b[k - 3] : Vector3.Cross(a[(k - 6) / 3], b[(k - 6) % 3]);
                var len = axis.Length();
                if (len < 1e-6f)
                    continue;
                axis /= len;
                var ra = H.X * MathF.Abs(Vector3.Dot(X, axis)) + H.Y * MathF.Abs(Vector3.Dot(Y, axis)) + H.Z * MathF.Abs(Vector3.Dot(Z, axis));
                var rb = o.H.X * MathF.Abs(Vector3.Dot(o.X, axis)) + o.H.Y * MathF.Abs(Vector3.Dot(o.Y, axis)) + o.H.Z * MathF.Abs(Vector3.Dot(o.Z, axis));
                if (MathF.Abs(Vector3.Dot(t, axis)) > ra + rb + tol)
                    return false;
            }
            return true;
        }
    }
}
