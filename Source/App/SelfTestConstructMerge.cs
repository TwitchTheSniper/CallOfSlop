using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Merged receivers (board item dev-sandbox-receiver-merge-rm01; docs/sandbox/CONSTRUCTS.md "Merged receivers"): the
    /// rules file loads and bad ones are refused; the developer's diagram's shapes (T, L and 1x4 stay single, 2x2 and 3x3
    /// merge, a 4x4 is taken before the 3x3 inside it, mixed types and turns don't merge); built with the tools, a 2x2
    /// pops the gun parts off its members' own sockets into the builder's hands (or drops them), takes gun parts on its
    /// one centre socket set only, scales them and its numbers by the rules, and fires N squared rounds a shot; growing to
    /// 3x3 moves its centre parts; taking one cell off with the wrench takes only that receiver and the rest re-merge (the
    /// centre parts follow, or pop off); a client's copy and a copy rebuilt from the parts (as a load would be) agree; no
    /// allocation finding the merges.
    /// </summary>
    private static void CheckConstructMerge()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"construct merge: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        ReceiverMergeRules.Use(null);
        CMergeRules();
        CMergeShapes();
        CMergeBuild();
        CMergeDamage();
        ReceiverMergeRules.Use(null);
    }

    /// <summary>A merged receiver has its members' combined health; shooting a cell to pieces breaks the merge as taking it off does (its centre parts drop as loose parts: nobody placed it).</summary>
    private static void CMergeDamage()
    {
        var w = new ConstructWorld();
        var c = w.Create(ConstructWorld.Ground(World.Query, -6, 2) + new Vector3(0, 0.3f, 0), Quaternion.Identity)!;
        var d = ConstructParts.Need("Rifle Receiver");
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            var i = w.AddPart(c, d, new Vector3(x * 0.2f, y * 0.2f, 0), 0);
            ConstructBuild.AutoLink(w, c, i);
        }
        c.Rebuild();
        var barrelDef = ConstructParts.Need("Gun Barrel");
        var front = Array.FindIndex(d.Sockets, s => s.Name == "front");
        var lead = c.MergeCount > 0 ? c.Merges[0].Lead : 0;
        var why = ConstructBuild.Place(c, lead, d.Sockets.Length + front, barrelDef, Array.FindIndex(barrelDef.Sockets, s => s.Name == "back"), 0, out var g);
        var barrel = why == null ? ConstructBuild.Attach(w, c, barrelDef, g) : -1;
        c.Rebuild();
        ConstructMerge.Health(c, 3, out var hp, out var most);
        var links = c.LinkCount;
        var bodies = w.Bodies.Count;
        var pops = w.MergePopCount;
        w.DamagePart(c, 3, 100000, out _);
        if (c.Dirty)
            c.Rebuild();
        var loose = w.Bodies.Skip(bodies).Count(b => b.PartCount == 1 && b.DefOf(0) == barrelDef);
        var barrels = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i) == barrelDef)
                barrels++;
        Line($"damage: a 2x2 ({links} links) with a barrel on its centre front ({why ?? "fitted"}): health {hp:0}/{most:0}; a cell shot to pieces: {c.MergeCount} merge(s) left, {c.PartCount} parts, {w.MergePopCount - pops} popped, {loose} loose barrel(s), {barrels} still on");
        if (why != null || barrel < 0 || MathF.Abs(most - 4 * d.Health) > 0.01f || c.MergeCount != 0 || barrels != 0 || loose != 1 || w.MergePopCount - pops != 1)
            Fail("construct merge: shooting a merged receiver's cell to pieces didn't break the merge and drop its centre barrel (or its health isn't its members' together)");
    }

    private static void CMergeRules()
    {
        var rules = ReceiverMergeRules.Current;
        Line($"rules: {ReceiverMergeRules.FilePath}: sizes {rules.MinSize}..{rules.MaxSize}, same type only {rules.SameTypeOnly}, powers damage {rules.Damage}, fire rate {rules.FireRate}, recoil {rules.Recoil}, ammo a shot {rules.AmmoPerShot}, magazine {rules.Magazine}, part mass {rules.PartMass}{(rules.Problem.Length > 0 ? $"; refused: {rules.Problem}" : "")}");
        if (rules.Problem.Length > 0)
            Fail($"construct merge: the rules file was refused: {rules.Problem}");
        const string good = """{ "format": "call-of-slop-receiver-merge/1", "minSize": 2, "maxSize": 4, "powers": { "damage": 2 } }""";
        (string Json, string Want)[] bad =
        [
            (good.Replace("receiver-merge/1", "receiver-merge/9"), "\"format\""),
            (good.Replace("\"minSize\": 2", "\"minSize\": 1"), "\"minSize\""),
            (good.Replace("\"maxSize\": 4", "\"maxSize\": 4, \"colour\": 1"), "unknown key"),
            (good.Replace("\"damage\": 2", "\"damage\": 2, \"speed\": 1"), "unknown power"),
            (good.Replace("\"minSize\": 2", "\"minSize\": 5"), "more than"),
            (good.Replace("\"damage\": 2", "\"damage\": 9"), "from -3 to 3"),
        ];
        if (ReceiverMergeRules.Parse(good) is { Problem.Length: > 0 } g)
            Fail($"construct merge: a good rules file was refused: {g.Problem}");
        foreach (var (json, want) in bad)
            if (ReceiverMergeRules.Parse(json).Problem is var why && !why.Contains(want))
                Fail($"construct merge: a bad rules file wasn't refused for {want} (\"{why}\")");
    }

    /// <summary>Receivers set down in a pattern (cells on the receiver's own grid), and the merges found: "NxN" for each, singles left.</summary>
    private static string MergeShape(string receiver, (int X, int Y)[] cells, byte rot = 0, string? other = null, int otherAt = -1, byte otherRot = 0)
    {
        var w = new ConstructWorld();
        var c = w.Create(new Vector3(0, 5, 0), Quaternion.Identity)!;
        var foundation = ConstructParts.Need("Foundation");
        w.AddPart(c, foundation, new Vector3(0, -2, 0), 0);
        var d = ConstructParts.Need(receiver);
        var size = d.BoundsMax - d.BoundsMin;
        for (var k = 0; k < cells.Length; k++)
        {
            var def = k == otherAt && other != null ? ConstructParts.Need(other) : d;
            var r = k == otherAt ? otherRot : rot;
            w.AddPart(c, def, Rot24.Apply(r, new Vector3(cells[k].X * size.X, cells[k].Y * size.Y, 0)), r);
        }
        c.Rebuild();
        var parts = new List<string>();
        for (var g = 0; g < c.MergeCount; g++)
            parts.Add($"{c.Merges[g].N}x{c.Merges[g].N}");
        var singles = 0;
        for (var i = 1; i < c.PartCount; i++)
            if (c.MergeOf[i] == 0)
                singles++;
        parts.Add($"{singles} single");
        return string.Join(" + ", parts);
    }

    private static void CMergeShapes()
    {
        (int, int)[] Square(int n, int dx = 0, int dy = 0) => Enumerable.Range(0, n * n).Select(k => (k % n + dx, k / n + dy)).ToArray();
        var shapes = new (string Name, string Got, string Want)[]
        {
            ("T", MergeShape("Rifle Receiver", [(0, 1), (1, 1), (2, 1), (1, 0)]), "4 single"),
            ("L", MergeShape("Rifle Receiver", [(0, 0), (0, 1), (0, 2), (1, 0)]), "4 single"),
            ("1x4", MergeShape("Rifle Receiver", [(0, 0), (1, 0), (2, 0), (3, 0)]), "4 single"),
            ("2x2", MergeShape("Rifle Receiver", Square(2)), "2x2 + 0 single"),
            ("3x3", MergeShape("Rifle Receiver", Square(3)), "3x3 + 0 single"),
            ("4x4", MergeShape("Rifle Receiver", Square(4)), "4x4 + 0 single"),
            ("3x4", MergeShape("Rifle Receiver", [.. Square(3), (3, 0), (3, 1), (3, 2)]), "3x3 + 3 single"),
            ("4x2", MergeShape("Rifle Receiver", [.. Square(2), .. Square(2, 2)]), "2x2 + 2x2 + 0 single"),
            ("2x2 turned", MergeShape("Rifle Receiver", Square(2), rot: 5), "2x2 + 0 single"),
            ("2x2 heavy", MergeShape("Heavy Receiver", Square(2)), "2x2 + 0 single"),
            ("2x2 mixed", MergeShape("Rifle Receiver", Square(2), other: "Heavy Receiver", otherAt: 3), "4 single"),
            ("2x2 one turned", MergeShape("Rifle Receiver", Square(2), otherAt: 3, otherRot: 2), "4 single"),
        };
        Line("shapes: " + string.Join("; ", shapes.Select(s => $"{s.Name}: {s.Got}")));
        foreach (var s in shapes)
            if (s.Got != s.Want)
                Fail($"construct merge: the {s.Name} shape gave {s.Got} (wanted {s.Want})");
        // Finding them allocates nothing (it runs with every rebuild).
        var w = new ConstructWorld();
        var c = w.Create(new Vector3(0, 5, 0), Quaternion.Identity)!;
        var d = ConstructParts.Need("Rifle Receiver");
        foreach (var (x, y) in Square(4))
            w.AddPart(c, d, new Vector3(x * 0.2f, y * 0.2f, 0), 0);
        ConstructMerge.Refresh(c);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 200; k++)
            ConstructMerge.Refresh(c);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"finding the merges of a 4x4, 200 times: {bytes} bytes allocated");
        if (bytes > 0)
            Fail($"construct merge: finding the merges allocated {bytes} bytes");
    }

    private static void CMergeBuild()
    {
        var w = new ConstructWorld();
        var e = new ConstructEdits();
        const byte me = 3;
        var now = 0.0;
        var ground = ConstructWorld.Ground(World.Query, 6, 2);
        var eye = ground + new Vector3(0, 1.6f, 2.2f);
        var problems = new List<string>();
        void Wait(double s)
        {
            for (var k = 0; k < (int)(s * World.TickRate) + 1; k++)
            {
                now += World.TickDt;
                e.Tick(w, now, _ => eye);
                w.Step(World.Query, World.TickDt);
            }
        }
        e.Hand(me).Held = ConstructParts.Need("Foundation").Id;
        e.SetTool(me, ConstructParts.Need("Foundation").Tool);
        e.BeginPlaceNew(w, me, eye, ground, 0, now);
        Wait(ConstructParts.Need("Foundation").AttachSeconds + 0.1);
        if (w.Bodies.Count != 1)
        {
            Fail("construct merge: the foundation wasn't placed");
            return;
        }
        var c = w.Bodies[0];
        int Step(string part, int on, string socket, string by, ConstructTool tool = ConstructTool.Wrench, bool centre = false)
        {
            var def = ConstructParts.Need(part);
            e.Hand(me).Held = def.Id;
            e.SetTool(me, tool);
            var ts = Array.FindIndex(c.DefOf(on).Sockets, s => s.Name == socket) + (centre ? c.DefOf(on).Sockets.Length : 0);
            var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
            if (e.BeginAttach(w, me, eye, c.Id, on, ts, ds * 4, now) is { } why)
            {
                problems.Add($"{part} on {c.DefOf(on).Name} {socket}{(centre ? " (centre)" : "")}: {why}");
                return -1;
            }
            var newest = 0;
            for (var i = 0; i < c.PartCount; i++)
                newest = Math.Max(newest, c.Parts[i].Tag);
            e.Done.Clear();
            Wait(def.AttachSeconds + 0.1);
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].Tag > newest && c.Parts[i].Def == def.Id)
                    return i; // (by its tag: parts a merge pops off renumber the rest)
            problems.Add($"{part} on {c.DefOf(on).Name} {socket}: {string.Join(" / ", e.Done)}");
            return -1;
        }
        int Remove(int part)
        {
            e.Hand(me).Held = ConstructParts.None;
            e.SetTool(me, ConstructTool.Wrench);
            if (e.BeginRemove(w, me, eye, c.Id, part, now) is { } why)
            {
                problems.Add($"removing part {part}: {why}");
                return -1;
            }
            var n = c.PartCount;
            Wait(ConstructBuild.RemoveSeconds(c.DefOf(part)) + 0.1);
            return n - c.PartCount;
        }
        int Find(int tag)
        {
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].Tag == tag)
                    return i;
            return -1;
        }
        string Merges() => c.MergeCount == 0 ? "none" : string.Join(", ", Enumerable.Range(0, c.MergeCount).Select(g => $"{c.Merges[g].N}x{c.Merges[g].N} led by part {c.Merges[g].Lead}"));
        var block = Step("Frame Block", 0, "top11", "-y", ConstructTool.Welder);
        var sw = Step("Switch", 0, "top33", "-y");
        // One receiver with a barrel and a magazine fed by a box: the single gun's numbers.
        var r0 = Step("Rifle Receiver", block, "+y", "bottom");
        var barrel0 = Step("Gun Barrel", r0, "front", "back");
        var mag0 = Step("Box Magazine", r0, "left", "right");
        Step("Ammo Box", mag0, "left", "right");
        if (c.GunCount != 1 || c.Guns[0].Def is not { } single)
        {
            Fail($"construct merge: the single receiver isn't a working gun ({string.Join("; ", problems)}; {(c.GunCount > 0 ? c.Guns[0].Problem : "no gun")})");
            return;
        }
        var oneDamage = single.Damage;
        var oneRpm = single.Rpm;
        var oneMag = c.Guns[0].Mag;
        var oneKick = c.Guns[0].Kick;
        var oneBarrelMass = ConstructParts.Need("Gun Barrel").Mass;
        var tags = new int[9];
        tags[0] = c.Parts[r0].Tag;
        // Three more make a 2x2: the barrel and magazine on the first one's own sockets pop off (the hand takes one, the rest drop; the box on the magazine comes too).
        var r1 = Step("Rifle Receiver", r0, "right", "left");
        tags[1] = c.Parts[r1].Tag;
        var r2 = Step("Rifle Receiver", Find(tags[0]), "top", "bottom");
        tags[2] = c.Parts[r2].Tag;
        e.Hand(me).Held = ConstructParts.None;
        var popsBefore = w.MergePopCount;
        var bodiesBefore = w.Bodies.Count;
        var r3 = Step("Rifle Receiver", Find(tags[1]), "top", "bottom");
        tags[3] = r3 >= 0 ? c.Parts[r3].Tag : 0;
        var popped = w.MergePopCount - popsBefore;
        var inHand = ConstructParts.Get(e.Hand(me).Held)?.Name ?? "nothing";
        var dropped = w.Bodies.Count - bodiesBefore;
        var after2 = Merges();
        var lead = c.MergeCount > 0 ? c.Merges[0].Lead : -1;
        var guns2 = c.GunCount;
        Line($"2x2: {after2}; {popped} part(s) popped off the members' own sockets (\"{inHand}\" in the hand, {dropped} new loose piece(s) or wrecks); {guns2} gun(s): \"{(c.GunCount > 0 ? c.Guns[0].Problem : "")}\"");
        if (c.MergeCount != 1 || c.Merges[0].N != 2 || popped != 3 || inHand is not ("Gun Barrel" or "Box Magazine") || dropped < 1 || guns2 != 1)
            problems.Add("a 2x2 didn't merge into one gun with the barrel and magazine on its members' own sockets popped off (one to the hand, the rest dropped)");
        if (lead < 0)
        {
            Fail($"construct merge: {string.Join("; ", problems)}");
            return;
        }
        // Its members' own sockets take no gun parts; its centre sockets take nothing else.
        var len = c.DefOf(lead).Sockets.Length;
        var front = Array.FindIndex(c.DefOf(lead).Sockets, s => s.Name == "front");
        var refusedOwn = ConstructBuild.Place(c, Find(tags[1]), front, ConstructParts.Need("Gun Barrel"), Array.FindIndex(ConstructParts.Need("Gun Barrel").Sockets, s => s.Name == "back"), 0, out _);
        var refusedBox = ConstructBuild.Place(c, lead, len + front, ConstructParts.Need("Ammo Box"), 0, 0, out _);
        Span<ConstructGhost> ghosts = stackalloc ConstructGhost[ConstructBuild.MaxGhosts];
        var n = ConstructBuild.Ghosts(c, ConstructParts.Need("Gun Barrel"), ghosts, c.Parts[lead].Pos, 3f);
        var centreGhosts = 0;
        for (var k = 0; k < n; k++)
            if (ghosts[k].Part == lead && ghosts[k].Socket >= len)
                centreGhosts++;
        var memberGhosts = 0;
        for (var k = 0; k < n; k++)
            if (c.MergeOf[ghosts[k].Part] != 0 && ghosts[k].Socket < c.DefOf(ghosts[k].Part).Sockets.Length)
                memberGhosts++;
        Line($"sockets: a barrel on a member's own front: \"{refusedOwn}\"; an ammo box on the centre front: \"{refusedBox}\"; a barrel's ghosts: {centreGhosts} on the centre set, {memberGhosts} on members' own sockets");
        if (refusedOwn == null || refusedBox == null || centreGhosts == 0 || memberGhosts != 0)
            problems.Add("the merged receiver's sockets aren't one centre set for gun parts (members' own refused)");
        // The centre set: a barrel, a magazine and a box: scaled, and the numbers by the rules.
        var massBefore = c.Mass;
        var barrel = Step("Gun Barrel", lead, "front", "back", centre: true);
        var barrelTag = barrel >= 0 ? c.Parts[barrel].Tag : 0;
        var barrelMass = c.Mass - massBefore;
        var mag = Step("Box Magazine", c.Merges[0].Lead, "left", "right", centre: true);
        var magTag = mag >= 0 ? c.Parts[mag].Tag : 0;
        var box = mag >= 0 ? Step("Ammo Box", mag, "left", "right") : -1;
        var boxTag = box >= 0 ? c.Parts[box].Tag : 0;
        string Gun(out CGun gun)
        {
            gun = c.GunCount > 0 ? c.Guns[0] : default;
            return gun.Def == null ? $"no gun ({gun.Problem})" : $"{gun.MergeN}x{gun.MergeN}: {gun.Def.Damage} damage, {gun.Def.Rpm} rpm, {gun.Mag} a load, {gun.PerShot} a shot, kick {gun.Kick:0.#}";
        }
        var gun2 = Gun(out var g2);
        var bi = Find(barrelTag);
        var scaleBarrel = bi >= 0 ? c.PartScale[bi] : 0;
        c.BoxOf(Math.Max(0, bi), 0, out _, out var barrelHalf);
        var plainHalf = ConstructParts.Need("Gun Barrel").Boxes[0].Half;
        Line($"2x2 gun: {gun2} (single: {oneDamage} damage, {oneRpm} rpm, {oneMag} a load, kick {oneKick:0.#}); the barrel {scaleBarrel}x (box {barrelHalf.Z * 2:0.00} m long, its file {plainHalf.Z * 2:0.00}), weighs {barrelMass:0.0} kg (its file {oneBarrelMass:0.0}); the magazine {(Find(magTag) >= 0 ? c.PartScale[Find(magTag)] : 0)}x, the box {(Find(boxTag) >= 0 ? c.PartScale[Find(boxTag)] : 0)}x");
        if (g2.Def == null || g2.MergeN != 2 || g2.Def.Damage != oneDamage * 4 || g2.Def.Rpm != (int)MathF.Round(oneRpm / 2f) || g2.Mag != oneMag * 4 || g2.PerShot != 4 || MathF.Abs(g2.Kick - oneKick * 4) > 0.01f)
            problems.Add("a 2x2's numbers don't follow the rules (damage x4, fire rate /2, magazine x4, 4 rounds a shot, recoil x4)");
        if (scaleBarrel != 2 || MathF.Abs(barrelHalf.Z - plainHalf.Z * 2) > 0.001f || MathF.Abs(barrelMass - oneBarrelMass * 4) > 0.01f || Find(magTag) < 0 || c.PartScale[Find(magTag)] != 2 || Find(boxTag) < 0 || c.PartScale[Find(boxTag)] > 1)
            problems.Add("the gun parts on the centre sockets aren't 2x (bigger, 4x the weight; the ammo box as it is)");
        // It fires: 4 rounds a shot from its load.
        e.SetTool(me, ConstructTool.Linker);
        var wired = e.Wire(w, me, eye, c.Id, sw, c.Merges[0].Lead);
        var loaded0 = c.Parts[c.Merges[0].Lead].Rounds;
        c.Parts[sw].State = 1;
        var shots0 = w.ShotsFired;
        Wait(4.0);
        var shots = w.ShotsFired - shots0;
        var loaded = c.GunCount > 0 ? c.Parts[c.Guns[0].Part].Rounds : -1;
        var bi2 = Find(boxTag);
        var boxLeft = bi2 >= 0 ? c.Parts[bi2].Rounds : -1;
        var boxFull = ConstructParts.Need("Ammo Box").BoxRounds;
        c.Parts[sw].State = 0;
        Line($"firing 4 s: wired {wired ?? "ok"}, {shots} shots, {loaded0} -> {loaded} loaded, the box {boxFull} -> {boxLeft}");
        if (wired != null || shots < 1 || loaded % 4 != 0 || boxLeft < 0 || loaded0 + boxFull - boxLeft != loaded + shots * 4 || boxLeft == boxFull)
            problems.Add("the 2x2 didn't fire 4 rounds a shot out of its load");
        // A client's copy and a copy rebuilt from the parts (as a load would) find the same merge and scales.
        string Fingerprint(Construct x)
        {
            var s = string.Join(",", Enumerable.Range(0, x.MergeCount).Select(g => $"{x.Merges[g].N}@{x.Merges[g].Lead}:{x.Merges[g].Origin.X:0.00},{x.Merges[g].Origin.Y:0.00},{x.Merges[g].Origin.Z:0.00}"));
            s += "|" + string.Join("", Enumerable.Range(0, x.PartCount).Select(i => x.PartScale[i] > 1 ? x.PartScale[i].ToString() : "."));
            s += "|" + string.Join(";", Enumerable.Range(0, x.GunCount).Select(k => $"{x.Guns[k].Part}:{x.Guns[k].Def?.Damage}/{x.Guns[k].Def?.Rpm}/{x.Guns[k].PerShot}"));
            return s;
        }
        var client = new ConstructWorld();
        var pw = new PacketWriter(16384);
        ConstructNet.WriteFull(pw, c);
        var pr = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref pr, client, 0);
        var copy = client.Get(c.Id);
        if (copy is { Dirty: true })
            copy.Rebuild();
        var loadW = new ConstructWorld();
        var load = loadW.Create(c.Pos, c.Rot)!;
        for (var i = 0; i < c.PartCount; i++)
            load.Parts[load.PartCount++] = c.Parts[i];
        for (var l = 0; l < c.LinkCount; l++)
            load.Links[load.LinkCount++] = c.Links[l];
        load.Rebuild();
        var server = Fingerprint(c);
        Line($"server {server}; client {(copy == null ? "none" : Fingerprint(copy))}; rebuilt from the parts {Fingerprint(load)}");
        if (copy == null || Fingerprint(copy) != server || Fingerprint(load) != server)
            problems.Add("a client's copy or a copy rebuilt from the parts doesn't find the same merge");
        // Five more make a 3x3: the centre parts move onto its centre socket set, 3x.
        (int On, string Socket)[] growOn = [(1, "right"), (3, "right"), (2, "top"), (3, "top"), (5, "top")];
        for (var k = 0; k < 5; k++)
        {
            var at = Find(tags[growOn[k].On]);
            var r = at >= 0 ? Step("Rifle Receiver", at, growOn[k].Socket, growOn[k].Socket == "right" ? "left" : "bottom") : -1;
            tags[4 + k] = r >= 0 ? c.Parts[r].Tag : 0;
        }
        var gun3 = Gun(out var g3);
        var scale3 = Find(barrelTag) >= 0 ? c.PartScale[Find(barrelTag)] : 0;
        Line($"3x3: {Merges()}; gun {gun3}; the barrel {scale3}x, the magazine {(Find(magTag) >= 0 ? c.PartScale[Find(magTag)] : 0)}x");
        if (c.MergeCount != 1 || c.Merges[0].N != 3 || g3.Def == null || g3.Def.Damage != oneDamage * 9 || g3.PerShot != 9 || scale3 != 3 || Find(magTag) < 0 || c.PartScale[Find(magTag)] != 3)
            problems.Add("the 3x3 didn't take its 2x2's centre parts onto its own centre sockets at 3x");
        // The wrench takes off only the cell under it: the bottom-right one; a 2x2 and four singles are left, the centre parts back at 2x.
        var parts3 = c.PartCount;
        var took = Remove(Find(tags[4]));
        var receivers = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsReceiverGun)
                receivers++;
        var scaleBack = Find(barrelTag) >= 0 ? c.PartScale[Find(barrelTag)] : 0;
        var singles = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsReceiverGun && c.MergeOf[i] == 0)
                singles++;
        Line($"3x3 less a corner: {took} part(s) came off ({parts3} -> {c.PartCount}), {receivers} receivers; {Merges()} and {singles} singles; the barrel {scaleBack}x, the magazine {(Find(magTag) >= 0 ? c.PartScale[Find(magTag)] : 0)}x; gun {Gun(out _)}");
        if (took != 1 || receivers != 8 || c.MergeCount != 1 || c.Merges[0].N != 2 || singles != 4 || scaleBack != 2 || Find(magTag) < 0 || c.PartScale[Find(magTag)] != 2)
            problems.Add("taking one cell off a 3x3 didn't leave a 2x2 (its centre parts at 2x) and four singles");
        // Then the second cell of the bottom row: the 2x2 above takes over (a new lead), the centre parts move up with it.
        took = Remove(Find(tags[1]));
        var moved = Find(barrelTag) >= 0 && c.MergeCount == 1 && c.PartScale[Find(barrelTag)] == 2 && c.SocketUsed(c.Merges[0].Lead, c.DefOf(c.Merges[0].Lead).Sockets.Length + front);
        Line($"then the bottom row's second: {took} came off; {Merges()}; the barrel on the new merge's centre front {moved}");
        if (took != 1 || !moved)
            problems.Add("the centre parts didn't follow the merge to its new place");
        // Then the middle: no square is left: the centre parts pop off (none lost).
        popsBefore = w.MergePopCount;
        took = Remove(Find(tags[3]));
        var centrePops = w.MergePopCount - popsBefore;
        Line($"then the middle: {took} came off; {Merges()}; {centrePops} centre part(s) popped off; the barrel {(Find(barrelTag) >= 0 ? "still on" : "off")}");
        if (took < 1 || c.MergeCount != 0 || centrePops < 3 || Find(barrelTag) >= 0 || Find(magTag) >= 0)
            problems.Add("the centre parts didn't pop off when no square was left");
        foreach (var p in problems)
            Fail($"construct merge: {p}");
        ReceiverMergeRules.Use(null);
    }
}
