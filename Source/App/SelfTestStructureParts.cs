using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The structural parts (Constructs/Parts/Structure, tools/constructs/structure_parts.py): beams, panels, armour, shapes.</summary>
    private static List<ConstructPartDef> StructuralParts() =>
        ConstructParts.All.Where(d => d.Folder.StartsWith("Structure", StringComparison.Ordinal)).ToList();

    private const float CellStep = 0.25f; // the frame step (docs/sandbox/CONSTRUCTS.md, "Structural parts")

    private static bool OnCm(float v) => MathF.Abs(v * 100 - MathF.Round(v * 100)) < 1e-3f;

    private static bool HalfCm(float v) => OnCm(v + 0.005f) && !OnCm(v);

    private static bool OnCell(float v) => MathF.Abs(v / CellStep - MathF.Round(v / CellStep)) < 1e-4f;

    private static bool OnCellFace(float v) => OnCell(v - CellStep / 2);

    /// <summary>
    /// "Structural parts" (docs/sandbox/CONSTRUCTS.md, "Structural parts"): every file in Constructs/Parts/Structure loads,
    /// keeps the 0.25 m cell rules (sockets on cell faces, at cell centres; a plate's front and edges on whole
    /// centimetres) and is in the inventory under its group and section. Each one goes onto a 0.25 m cube by every socket
    /// on every face in every spin (all 24 Rot24 turns), its origin always on a whole centimetre and its cells on the
    /// cube's lattice; each of its sockets takes a copy of itself (beam to beam, plate to plate edge) and welds. Shaped
    /// boxes turn with their part exactly as boxes do. Armour against the real guns' damage: armour plate stops every
    /// SMG, rifle, LMG and shotgun round, heavy armour every pistol, SMG, rifle, LMG, shotgun and marksman round, while
    /// snipers and the heavy construct guns get through; a gun port's slit lets a round through where the plate stops it.
    /// Fitting checks and damage allocate nothing.
    /// </summary>
    private static void CheckStructureParts()
    {
        var parts = StructuralParts();
        var refused = ConstructParts.Refused.Where(r => !r.Contains("missing")).ToList();
        Line($"structural parts: {parts.Count} in Constructs/Parts/Structure, {ConstructParts.All.Count} parts in all (at most {ConstructParts.MaxTypes}); refused: {(refused.Count == 0 ? "none" : string.Join("; ", refused))}");
        if (refused.Count > 0)
            Fail($"construct part files refused: {string.Join("; ", refused)}");
        if (parts.Count < 100)
            Fail($"only {parts.Count} structural parts loaded (110 ship)");
        string[] groups = ["Beams", "Panels", "Armour", "Shapes"];
        foreach (var g in groups)
            if (!parts.Any(d => d.Group == g))
                Fail($"no structural part in the {g} group");

        // The cell rules, from the data.
        var badRules = new List<string>();
        foreach (var d in parts)
        {
            if (Array.IndexOf(groups, d.Group) < 0 || d.Section.Length == 0)
                badRules.Add($"{d.Name}: group \"{d.Group}\" section \"{d.Section}\"");
            if (d.Role is not (PartRole.Frame or PartRole.Panel))
                badRules.Add($"{d.Name}: role {d.Role}");
            foreach (var s in d.Sockets)
            {
                var axis = s.Dir >> 1;
                for (var a = 0; a < 3; a++)
                {
                    // Out of a face: half a centimetre off the whole ones, as a cell face is (12.5 cm from a cell centre),
                    // so two faces that meet always put the new part on a whole centimetre. Along it: on a cell centre
                    // (a plate's edge sockets 1 cm into the plate: a whole centimetre).
                    var v = s.At[a];
                    var ok = a == axis ? HalfCm(v) : OnCell(v) || d.Role == PartRole.Panel && a == 1 && OnCm(v);
                    if (!ok)
                    {
                        badRules.Add($"{d.Name} socket {s.Name} at {s.At}");
                        break;
                    }
                }
            }
        }
        Line($"structural parts: cell rules {(badRules.Count == 0 ? "kept by every part" : "broken: " + string.Join("; ", badRules.Take(8)))}");
        if (badRules.Count > 0)
            Fail($"{badRules.Count} structural part rule(s) broken: {string.Join("; ", badRules.Take(8))}");

        // Every socket onto a 0.25 m cube's six faces in all four spins: all 24 turns, origins on whole centimetres, cells on the lattice.
        var world = new ConstructWorld();
        var cube = ConstructParts.Need("Square Beam 0.25 m");
        if (ConstructBuild.PlaceNew(world, cube, new Vector3(0, 0.2f, 0), 0) is not { } node)
        {
            Fail("structural parts: couldn't place the 0.25 m cube");
            return;
        }
        int placements = 0, turnsShort = 0;
        var fitFails = new List<string>();
        var turns = new bool[Rot24.Count];
        void TryAll(bool record)
        {
            foreach (var d in parts)
            {
                if (record)
                    Array.Clear(turns);
                for (var s = 0; s < cube.Sockets.Length; s++)
                for (var ds = 0; ds < d.Sockets.Length; ds++)
                for (var spin = 0; spin < 4; spin++)
                {
                    var why = ConstructBuild.Place(node, 0, s, d, ds, spin, out var g);
                    if (!record)
                        continue;
                    placements++;
                    if (why != null)
                    {
                        if (fitFails.Count < 8)
                            fitFails.Add($"{d.Name} by {d.Sockets[ds].Name} on {cube.Sockets[s].Name} spin {spin}: {why}");
                        continue;
                    }
                    turns[g.Rot] = true;
                    node.SocketAt(0, s, out var at, out var dir);
                    var raw = at - Rot24.Apply(g.Rot, d.Sockets[ds].At);
                    var axis = dir >> 1;
                    var badPos = !OnCm(raw.X) || !OnCm(raw.Y) || !OnCm(raw.Z);
                    // Along the face, the new part's origin is on a cell centre wherever its socket is.
                    var sockR = Rot24.Apply(g.Rot, d.Sockets[ds].At);
                    for (var a = 0; a < 3; a++)
                        if (a != axis && OnCell(sockR[a]) && !OnCell(raw[a] - node.Parts[0].Pos[a]))
                            badPos = true;
                    if (badPos && fitFails.Count < 8)
                        fitFails.Add($"{d.Name} by {d.Sockets[ds].Name} on {cube.Sockets[s].Name} spin {spin}: origin {raw - node.Parts[0].Pos} is off the grid");
                }
                if (record && turns.Any(t => !t))
                {
                    turnsShort++;
                    if (fitFails.Count < 8)
                        fitFails.Add($"{d.Name}: only {turns.Count(t => t)} of 24 turns");
                }
            }
        }
        TryAll(true);
        var fitFailCount = fitFails.Count;
        Line($"structural parts on a 0.25 m cube: {placements} placements tried (every socket, face and spin), {turnsShort} part(s) short of 24 turns; {(fitFailCount == 0 ? "all fit on the grid" : string.Join("; ", fitFails))}");
        if (fitFailCount > 0)
            Fail($"structural parts don't all fit a cube in every turn: {string.Join("; ", fitFails)}");
        TryAll(false); // (warm)
        var before = GC.GetAllocatedBytesForCurrentThread();
        TryAll(false);
        var fitAlloc = GC.GetAllocatedBytesForCurrentThread() - before;

        // Neighbours: each socket takes a copy of the same part (end to end, edge to edge, face to face), welded.
        var lonely = new List<string>();
        int welds = 0;
        foreach (var d in parts)
        {
            if (ConstructBuild.PlaceNew(world, d, new Vector3(10, 0.2f, 0), 0) is not { } c)
            {
                lonely.Add($"{d.Name}: couldn't place");
                continue;
            }
            ConstructGhost first = default;
            for (var s = 0; s < d.Sockets.Length; s++)
            {
                var fits = false;
                for (var v = 0; v < d.Sockets.Length * 4 && !fits; v++)
                    if (ConstructBuild.Place(c, 0, s, d, v / 4, v % 4, out var g) == null)
                    {
                        fits = true;
                        if (s == 0)
                            first = g;
                    }
                if (!fits && lonely.Count < 12)
                    lonely.Add($"{d.Name} {d.Sockets[s].Name}");
            }
            var links = c.LinkCount;
            if (ConstructBuild.Attach(world, c, d, first) < 0 || c.LinkCount <= links)
                lonely.Add($"{d.Name}: a copy on {d.Sockets[0].Name} didn't weld");
            else
                welds++;
            world.Remove(c);
        }
        Line($"structural parts next to themselves: {welds} copies welded on; {(lonely.Count == 0 ? "every socket takes a neighbour" : "no neighbour on: " + string.Join(", ", lonely))}");
        if (lonely.Count > 0)
            Fail($"structural part sockets that take no copy of their part: {string.Join(", ", lonely)}");

        // Shapes turn with their part: a shaped box's model fills exactly the turned box, in every turn.
        int shaped = 0, shapeBad = 0;
        foreach (var d in parts)
            foreach (var b in d.Boxes)
            {
                if (b.Shape == 0)
                    continue;
                shaped++;
                if (PartShapes.Mesh(b.Shape).Length == 0)
                    shapeBad++;
                for (byte r = 0; r < Rot24.Count; r++)
                {
                    var m = b.Model(r, Vector3.Zero);
                    var mn = new Vector3(float.MaxValue);
                    var mx = new Vector3(float.MinValue);
                    for (var k = 0; k < 8; k++)
                    {
                        var p = Vector3.Transform(new Vector3((k & 1) - 0.5f, ((k >> 1) & 1) - 0.5f, ((k >> 2) & 1) - 0.5f), m);
                        mn = Vector3.Min(mn, p);
                        mx = Vector3.Max(mx, p);
                    }
                    if (Vector3.Distance(mx, Rot24.ApplyHalf(r, b.Half)) > 1e-4f || Vector3.Distance(mn, -Rot24.ApplyHalf(r, b.Half)) > 1e-4f)
                        shapeBad++;
                    // ...and its own way round: the shape's +Z goes where the part's +Z goes.
                    if (Vector3.Distance(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m)), Rot24.Apply(r, Vector3.UnitZ)) > 1e-3f)
                        shapeBad++;
                }
            }
        Line($"structural shapes: {shaped} shaped boxes (wedges, arcs, cylinders, bevels) over hidden stepped collision; {(shapeBad == 0 ? "each turns exactly with its part" : shapeBad + " wrong")}");
        if (shapeBad > 0)
            Fail($"{shapeBad} shaped box turn(s) don't match the part's boxes");

        // Armour against the real guns (bullets take a part's armour off every hit: ConstructWorld.DamagePart).
        float Dealt(string part, float damage)
        {
            if (ConstructBuild.PlaceNew(world, ConstructParts.Need(part), new Vector3(20, 0.2f, 0), 0) is not { } c)
                return -1;
            world.DamagePart(c, 0, damage, out var dealt);
            world.Remove(c);
            return dealt;
        }
        var guns = Weapons.All;
        var notes = new List<string>();
        void Expect(string part, Func<WeaponDef, bool> which, bool blocked, string what)
        {
            var hits = guns.Where(which).ToList();
            var wrong = hits.Where(w => (Dealt(part, w.Damage) <= 0) != blocked).Select(w => $"{w.Name} {w.Damage}").ToList();
            notes.Add($"{part} {(blocked ? "stops" : "lets through")} {what} ({hits.Count} guns){(wrong.Count == 0 ? "" : ": NOT " + string.Join(", ", wrong))}");
            if (hits.Count == 0 || wrong.Count > 0)
                Fail($"{part} should {(blocked ? "stop" : "let through")} {what}: {(hits.Count == 0 ? "no such guns" : string.Join(", ", wrong))}");
        }
        Expect("Armour Plate 1 m", w => w.Class is WeaponClass.SMG or WeaponClass.AssaultRifle or WeaponClass.LMG || w.Class == WeaponClass.Shotgun && w.Pellets > 1, true, "every SMG, rifle, LMG and buckshot round");
        Expect("Armour Plate 1 m", w => w.Class == WeaponClass.Pistol && w.Damage <= 40, true, "pistols up to 40 damage");
        Expect("Armour Plate 1 m", w => w.Class is WeaponClass.Sniper or WeaponClass.DMR && w.Damage > 40, false, "marksman and sniper rounds over 40");
        Expect("Heavy Armour 1 m", w => w.Class is WeaponClass.Pistol or WeaponClass.SMG or WeaponClass.AssaultRifle or WeaponClass.LMG or WeaponClass.Shotgun or WeaponClass.DMR && w.Damage <= 68, true, "every pistol, SMG, rifle, LMG, shotgun pellet and marksman round up to 68");
        Expect("Heavy Armour 1 m", w => w.Class == WeaponClass.Sniper && w.Damage > 68, false, "sniper rounds over 68");
        Expect("Plate Steel 1 m", w => w.Class == WeaponClass.Shotgun && w.Pellets > 1, true, "every buckshot pellet");
        Expect("Plate Steel 1 m", w => w.Class == WeaponClass.AssaultRifle, false, "rifle rounds");
        Expect("Sheet Steel 1 m", w => w.Class is WeaponClass.SMG or WeaponClass.AssaultRifle, false, "SMG and rifle rounds");
        Expect("Glass Pane 1 m", w => w.Class != WeaponClass.Launcher, false, "any round");
        var heavyGuns = new List<string>();
        foreach (var gname in new[] { "Mounted MG", "Autocannon", "Cannon", "Rocket Pod" })
            if (ConstructParts.Find(gname) is { } gd)
            {
                float onArmour = Dealt("Armour Plate 1 m", gd.GunDamage), onHeavy = Dealt("Heavy Armour 1 m", gd.GunDamage);
                heavyGuns.Add($"{gname} {gd.GunDamage:0}: armour {onArmour:0}, heavy {onHeavy:0}");
                if (gname == "Mounted MG" ? onArmour > 0 : onHeavy <= 0)
                    Fail($"construct gun {gname} ({gd.GunDamage:0}) against armour {onArmour:0}, heavy armour {onHeavy:0}: the MG should be stopped by armour, the heavy guns get through heavy armour");
            }
        Line($"structural armour: {string.Join("; ", notes)}; construct guns: {string.Join(", ", heavyGuns)}");

        // Materials order: thicker, tougher plates weigh more and stop more.
        string[] ladder = ["Glass Pane 1 m", "Wood Planks 2 m", "Sheet Steel 1 m", "Tread Plate 1 m", "Plate Steel 1 m", "Armour Plate 1 m", "Heavy Armour 1 m"];
        var ladderParts = ladder.Select(ConstructParts.Find).ToList();
        if (ladderParts.Any(p => p == null))
            Fail($"structural parts: a material ladder part is missing ({string.Join(", ", ladder.Where((_, i) => ladderParts[i] == null))})");
        else
        {
            for (var i = 1; i < ladderParts.Count; i++)
                if (ladderParts[i]!.Armour <= ladderParts[i - 1]!.Armour && !(ladderParts[i - 1]!.Name.StartsWith("Glass") && ladderParts[i]!.Armour > 0))
                    Fail($"structural parts: {ladderParts[i]!.Name} (armour {ladderParts[i]!.Armour}) should stop more than {ladderParts[i - 1]!.Name} ({ladderParts[i - 1]!.Armour})");
            Line($"structural materials: {string.Join(", ", ladderParts.Select(p => $"{p!.Name} {p.Mass:0.#} kg {p.Health:0} hp armour {p.Armour:0} ({p.Material})"))}");
        }

        // A gun port's slit lets a round through where the plate round it stops one.
        if (ConstructParts.Find("Gun Port Panel 1 m") is { } port && ConstructBuild.PlaceNew(world, port, new Vector3(30, 0.2f, 0), 0) is { } pc)
        {
            pc.Rebuild();
            // The plate lies flat: shoot down through the slit's middle, and through the plate beside it.
            var slit = pc.Pos + Vector3.Transform(new Vector3(-0.125f + 0.5f, 0, -0.125f + 0.525f), pc.Rot);
            var plate = pc.Pos + Vector3.Transform(new Vector3(-0.125f + 0.5f, 0, -0.125f + 0.2f), pc.Rot);
            var throughSlit = !world.Raycast(slit + new Vector3(0, 2, 0), -Vector3.UnitY, 4, out _);
            var stopped = world.Raycast(plate + new Vector3(0, 2, 0), -Vector3.UnitY, 4, out _);
            Line($"structural openings: a round down through the Gun Port Panel's slit {(throughSlit ? "goes through" : "is stopped")}, beside it {(stopped ? "hits the plate" : "goes through")}");
            if (!throughSlit || !stopped)
                Fail("structural openings: the gun port's slit should let a round through and the plate round it stop one");
            world.Remove(pc);
        }
        else
            Fail("structural openings: no Gun Port Panel 1 m to shoot through");

        // Damage allocates nothing either.
        if (ConstructBuild.PlaceNew(world, ConstructParts.Need("Armour Plate 1 m"), new Vector3(40, 0.2f, 0), 0) is { } ac)
        {
            world.DamagePart(ac, 0, 30, out _);
            before = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 50; k++)
                world.DamagePart(ac, 0, 30, out _);
            var dmgAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
            Line($"structural parts: fitting checks ({placements} placements) allocated {fitAlloc} bytes, 50 blocked hits {dmgAlloc} bytes");
            if (fitAlloc > 0 || dmgAlloc > 0)
                Fail($"structural parts allocate: fitting {fitAlloc} bytes, hits {dmgAlloc} bytes (want 0)");
        }

        // The inventory lists every structural part under its group and section; the old big pieces come after the new ones.
        var cat = SandboxCatalogue.Make();
        var missing = new List<string>();
        foreach (var d in parts)
        {
            var sec = cat.Parts.Sections.FirstOrDefault(s => s.Items.Contains(d.Id));
            if (sec == null || cat.Parts.Groups[sec.Group].Name != d.Group || sec.Name != d.Section)
                missing.Add(d.Name);
        }
        var lines = new List<string>();
        foreach (var g in groups)
            if (cat.Parts.Groups.FirstOrDefault(x => x.Name == g) is { } grp)
                lines.Add($"{g}: {string.Join(", ", grp.Sections.Select(s => $"{cat.Parts.Sections[s].Name} {cat.Parts.Sections[s].Items.Length}"))}");
        Line($"structural inventory: {string.Join("; ", lines)}{(missing.Count == 0 ? "" : "; NOT LISTED: " + string.Join(", ", missing))}");
        if (missing.Count > 0)
            Fail($"structural parts missing from their inventory section: {string.Join(", ", missing.Take(10))}");
        if (cat.Parts.Groups.FirstOrDefault(x => x.Name == "Beams") is { } beams && cat.Parts.Sections[beams.Sections[^1]].Name != "Blocks")
            Fail("inventory: the old Frame Block and Frame Beam (\"Blocks\") should come after the new beam sections");
    }
}
