using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Bullet penetration" (Source/Game/Materials, GamePenetration.cs): the material table loads; the
/// weapon powers rank as intended (a pistol stops in 20 cm of brick, a sniper goes through 10 cm of wood and 10 cm of
/// plaster); damage falloff numbers; never through the ground, terrain, thick concrete or the Bocage hedgerows; the
/// three-surface cap; touching boxes are one surface; the exit point on oriented boxes and real building parts;
/// server shots through walls (reduced damage, headshots, the wallbang flag, the origin trailer); the rewound target
/// behind a wall; entry and exit decals; bots only shoot through walls at a fresh known spot; the map override;
/// determinism; no allocations per trace. Pure geometry and single shots: no matches.
/// </summary>
public static partial class SelfTest
{
    private static readonly Vector3 PenBrick = new(0.6f, 0.25f, 0.25f), PenWood = new(0.5f, 0.35f, 0.2f), PenPale = new(0.85f), PenGrey = new(0.5f);

    /// <summary>
    /// Lanes along -Z, each with walls about 10 m out from z = 0: x = -30 20 cm brick; -20 10 cm wood then (2 m on) 10 cm
    /// plaster; -10 50 cm concrete; 0 four 5 cm wood boards 2 m apart; 10 three touching wood boards (2 + 10 + 2 cm);
    /// 20 an oriented 10 cm slab (added to the physics world by <see cref="SetPenLab"/>).
    /// </summary>
    private static Map PenLab()
    {
        var m = new Map { Name = "Penetration lab", Description = "self-test scene", HalfX = 40, HalfZ = 40 };
        m.Ground(PenGrey);
        m.Block(-33, 0, -10.2f, -27, 3, -10, PenBrick);
        m.Block(-21.5f, 0, -10.1f, -18.5f, 3, -10, PenWood);
        m.Block(-23, 0, -12.1f, -17, 3, -12, PenPale);
        m.Block(-13, 0, -10.5f, -7, 3, -10, PenGrey);
        for (var i = 0; i < 4; i++)
            m.Block(-1.5f, 0, -6.05f - 2 * i, 1.5f, 3, -6 - 2 * i, PenWood);
        m.Block(8.5f, 0, -10.02f, 11.5f, 3, -10, PenWood);
        m.Block(8.5f, 0, -10.12f, 11.5f, 3, -10.02f, PenWood);
        m.Block(8.5f, 0, -10.14f, 11.5f, 3, -10.12f, PenWood);
        m.Spawn(0, 20);
        m.Spawn(5, 20);
        return m;
    }

    /// <summary>The lab's oriented slab: 4 m wide, 3 m tall, 10 cm thick, turned 45 degrees about the vertical, centred at (20, 1.5, -10).</summary>
    private static readonly Obb PenSlab = new(new Vector3(20, 1.5f, -10), new Vector3(2, 1.5f, 0.05f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4));

    private static readonly List<string> PenNotes = new();

    /// <summary>A result line, also kept for the section's GitHub annotation.</summary>
    private static void PenNote(string s)
    {
        Line(s);
        PenNotes.Add(s);
    }

    private static void SetPenLab(Map lab)
    {
        World.SetMap(lab);
        World.Physics.AddObb(PenSlab);
    }

    private static void CheckPenetration()
    {
        var previous = World.Map;
        var quality = ImpactDecals.Quality;
        try
        {
            CheckPenData();
            var lab = PenLab();
            SetPenLab(lab);
            CheckPenTraces(lab);
            CheckPenDecals(lab);
            CheckPenBuildingParts();
            CheckPenHedgerowsAndTerrain();
            CheckPenShots();
            Line("::notice title=Bullet penetration (results)::" + string.Join("%0A", PenNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
        finally
        {
            ImpactDecals.Configure(quality);
            World.SetMap(previous);
        }
    }

    // ---- Data and powers ----

    private static void CheckPenData()
    {
        MaterialPenetration.EnsureLoaded();
        PenNote(MaterialPenetration.Describe());
        if (MaterialPenetration.Source == "built-in")
            Fail($"Materials/penetration.json didn't load: {MaterialPenetration.LoadError}");
        if (MaterialPenetration.MaxSurfaces != 3)
            Fail($"penetration: {MaterialPenetration.MaxSurfaces} surfaces max (want 3)");
        foreach (var k in new[] { MaterialKey.Rock, MaterialKey.Hedgerow, MaterialKey.Asphalt })
            if (!MaterialPenetration.Get(k).Stops)
                Fail($"penetration: {k} doesn't stop every bullet");
        foreach (var name in MaterialKeys.Names)
            if (MaterialKeys.Of(name).ToString() != name)
                Fail($"material key '{name}' doesn't round-trip");
        foreach (var s in DecalFile.ImpactMaterialNames.Where(n => n is not ("Explosive" or "Gore")))
            if (!MaterialKeys.TryParse(s, out _))
                Fail($"impact surface '{s}' has no material key");
        if (MaterialPenetration.HasWords("Carousel", "Car") || !MaterialPenetration.HasWords("Old Car", "Car") || !MaterialPenetration.HasWords("Bocage Hedgerow End", "Bocage Hedgerow"))
            Fail("penetration: prop name matching isn't by whole words");

        // A broken file is refused with a message, not half-applied.
        var tmp = Path.Combine(Path.GetTempPath(), $"cos-pen-{Environment.ProcessId}.json");
        try
        {
            File.WriteAllText(tmp, "{ \"format\": \"call-of-slop-penetration/1\", \"materials\": { \"Wood\": { \"resistPerCm\": 99 }, \"Unobtainium\": {} } }");
            if (MaterialPenetration.Load(tmp) || MaterialPenetration.LoadError?.Contains("Unobtainium") != true || MaterialPenetration.Get(MaterialKey.Wood).ResistPerCm != 1f)
                Fail($"penetration: a bad file wasn't refused cleanly ({MaterialPenetration.LoadError}, wood {MaterialPenetration.Get(MaterialKey.Wood).ResistPerCm})");
        }
        finally
        {
            File.Delete(tmp);
            MaterialPenetration.Load(Path.Combine(MaterialPenetration.Folder, "penetration.json"));
        }

        // Powers by class: every pistol stops in 20 cm of brick, every sniper passes 10 cm wood + 10 cm plaster.
        var brick20 = MaterialPenetration.Cost(MaterialKey.Brick, 20);
        var woodPlaster = MaterialPenetration.Cost(MaterialKey.Wood, 10) + MaterialPenetration.Cost(MaterialKey.Plaster, 10);
        foreach (var cls in Enum.GetValues<WeaponClass>())
        {
            var guns = Weapons.All.Where(w => w.Class == cls).ToArray();
            if (guns.Length == 0)
                continue;
            PenNote($"penetration power {cls}: " + string.Join(", ", guns.Select(w => $"{w.Name} {MaterialPenetration.PowerOf(w):0.#}")));
        }
        foreach (var w in Weapons.All)
        {
            var p = MaterialPenetration.PowerOf(w);
            if (w.Class == WeaponClass.Pistol && p >= brick20)
                Fail($"penetration: pistol {w.Name} ({p:0.#}) goes through 20 cm of brick ({brick20:0.#})");
            if (w.Class == WeaponClass.Sniper && p < woodPlaster)
                Fail($"penetration: sniper {w.Name} ({p:0.#}) can't pass 10 cm wood + 10 cm plaster ({woodPlaster:0.#})");
            if ((w.IsLauncher || w.Sound == SoundStyle.Toy) && p != 0)
                Fail($"penetration: {w.Name} has power {p} (launchers and the toy have none)");
            if (w.Pellets > 1 && p != MaterialPenetration.PelletPower)
                Fail($"penetration: {w.Name}'s pellets have power {p} (want {MaterialPenetration.PelletPower})");
        }
        var ap = Attachments.BySlot.SelectMany(s => s).FirstOrDefault(a => a.Name == "AP Rounds");
        var rifle = Weapons.All.First(w => w.Class == WeaponClass.AssaultRifle);
        if (ap == null)
            Fail("penetration: the AP Rounds attachment is missing");
        else
        {
            var copy = rifle.Clone();
            foreach (var e in ap.Effects)
                e.ApplyTo(copy);
            PenNote($"AP Rounds on {rifle.Name}: power {MaterialPenetration.PowerOf(rifle):0.#} -> {MaterialPenetration.PowerOf(copy):0.#}");
            if (MaterialPenetration.PowerOf(copy) <= MaterialPenetration.PowerOf(rifle) * 1.4f)
                Fail("penetration: AP Rounds don't raise the penetration power");
        }

        // Falloff numbers: 10 cm wood then 10 cm plaster at 89.5 power (the Needle's).
        const float p0 = 89.5f;
        var wood = MaterialPenetration.Get(MaterialKey.Wood);
        var plaster = MaterialPenetration.Get(MaterialKey.Plaster);
        var f1 = MaterialPenetration.Kept(wood, p0, p0 - 10);
        var f2 = f1 * MaterialPenetration.Kept(plaster, p0 - 10, p0 - 18);
        PenNote($"falloff at {p0}: after 10 cm wood x{f1:0.000}, then 10 cm plaster x{f2:0.000} (95 damage -> {95 * f2:0.0})");
        if (MathF.Abs(f1 - 0.7553f) > 0.002f || MathF.Abs(f2 - 0.6097f) > 0.002f)
            Fail($"penetration falloff changed: {f1:0.0000}, {f2:0.0000} (want 0.7553, 0.6097)");
    }

    // ---- Traces on the lab ----

    private static void CheckPenTraces(Map lab)
    {
        var t = new PenetrationTrace();
        var phys = World.Physics;
        var fwd = -Vector3.UnitZ;
        Vector3 Eye(float x) => new(x, 1.5f, 0);
        var needle = Weapons.All.First(w => w.Name == "Needle Bolt-Action");
        var pistol = Weapons.All.First(w => w.Name == "P9 Sidearm");
        var cannon = Weapons.All.First(w => w.Name == "Bastion Hand Cannon");

        // Brick 20 cm: the hand cannon stops at its front, a power-999 round passes.
        var end = t.Run(lab, phys, MaterialPenetration.PowerOf(cannon), Eye(-30), fwd, World.Range, 3);
        PenNote($"20 cm brick: {cannon.Name} ends at {end:0.000} m ({t.Stopped}, {t.Surfaces} surfaces)");
        if (MathF.Abs(end - 10f) > 0.01f || t.Surfaces != 0 || t.Stopped != PenetrationTrace.StopReason.Power)
            Fail($"penetration: a pistol got into 20 cm of brick (ended {end}, {t.Stopped})");
        end = t.Run(lab, phys, 999, Eye(-30), fwd, World.Range, 3);
        if (t.Surfaces != 1 || t.Pieces[0].Material != MaterialKey.Brick || MathF.Abs(t.Pieces[0].Out - 10.2f) > 0.002f)
            Fail($"penetration: a strong round didn't pass the brick wall ({t.Surfaces} surfaces, {t.Pieces[0].Material}, out {t.Pieces[0].Out})");

        // Wood 10 + plaster 10: the sniper passes both, the pistol neither.
        end = t.Run(lab, phys, MaterialPenetration.PowerOf(needle), Eye(-20), fwd, World.Range, 3);
        PenNote($"10 cm wood + 10 cm plaster: {needle.Name} {t.Surfaces} surfaces ({string.Join(", ", t.Pieces.Take(t.PieceCount).Select(p => $"{p.Material} {p.In:0.00}-{p.Out:0.00}"))}), "
             + $"x{t.FactorAt(14):0.000} at 14 m, power left {t.PowerLeft:0.0}");
        if (t.Surfaces != 2 || t.PieceCount != 2 || t.Pieces[0].Material != MaterialKey.Wood || t.Pieces[1].Material != MaterialKey.Plaster || end < World.Range - 0.01f)
            Fail($"penetration: the sniper didn't pass wood then plaster ({t.Surfaces} surfaces, end {end})");
        if (MathF.Abs(t.FactorAt(14) - 0.6097f) > 0.003f || t.FactorAt(5) != 1f || MathF.Abs(t.FactorAt(11) - 0.7553f) > 0.003f)
            Fail($"penetration: damage factors along the ray {t.FactorAt(5)}, {t.FactorAt(11)}, {t.FactorAt(14)} (want 1, 0.755, 0.610)");
        end = t.Run(lab, phys, MaterialPenetration.PowerOf(pistol), Eye(-20), fwd, World.Range, 3);
        if (t.Surfaces != 0 || MathF.Abs(end - 10f) > 0.01f)
            Fail($"penetration: the P9 ({MaterialPenetration.PowerOf(pistol)}) went into 10 cm of wood ({t.Surfaces} surfaces, end {end})");

        // Thick concrete (50 cm) and the ground slab: never, whatever the power.
        end = t.Run(lab, phys, 1e6f, Eye(-10), fwd, World.Range, 3);
        if (t.Surfaces != 0 || MathF.Abs(end - 10f) > 0.01f || t.Stopped != PenetrationTrace.StopReason.Thickness)
            Fail($"penetration: through 50 cm of concrete ({t.Surfaces} surfaces, end {end}, {t.Stopped})");
        end = t.Run(lab, phys, 1e6f, Eye(5), Vector3.Normalize(new Vector3(0, -1, -0.3f)), World.Range, 3);
        if (t.Surfaces != 0 || t.Stopped != PenetrationTrace.StopReason.Unpassable)
            Fail($"penetration: into the ground slab ({t.Surfaces} surfaces, {t.Stopped})");

        // The cap: four 5 cm boards, a strong round passes three and stops at the fourth's front.
        end = t.Run(lab, phys, 1e6f, Eye(0), fwd, World.Range, MaterialPenetration.MaxSurfaces);
        if (t.Surfaces != 3 || MathF.Abs(end - 12f) > 0.01f || t.Stopped != PenetrationTrace.StopReason.SurfaceCap)
            Fail($"penetration: the 3-surface cap ({t.Surfaces} surfaces, end {end}, {t.Stopped})");

        // Touching boards: one surface of three pieces.
        t.Run(lab, phys, 1e6f, Eye(10), fwd, World.Range, 3);
        if (t.Surfaces != 1 || t.PieceCount != 3 || MathF.Abs(t.SurfaceEnd[0] - 10.14f) > 0.002f)
            Fail($"penetration: touching boards weren't one surface ({t.Surfaces} surfaces, {t.PieceCount} pieces, ends {t.SurfaceEnd[0]})");

        // The oriented slab: the path through it is 10 cm / cos 45 degrees, exit point on its far face.
        t.Run(lab, phys, 1e6f, Eye(20), fwd, World.Range, 3);
        var thick = t.PieceCount > 0 ? t.Pieces[0].Out - t.Pieces[0].In : 0;
        var exitLocal = t.PieceCount > 0 ? PenSlab.ToLocal(Eye(20) + fwd * t.Pieces[0].Out) : Vector3.Zero;
        PenNote($"oriented slab: {t.PieceCount} piece(s), {thick * 100:0.00} cm along the ray (want {10 / MathF.Cos(MathF.PI / 4):0.00}), exit at local z {exitLocal.Z:0.0000}");
        if (t.PieceCount != 1 || t.Pieces[0].Shape.Kind != ShapeKind.Obb || MathF.Abs(thick - 0.1f / MathF.Cos(MathF.PI / 4)) > 0.001f || MathF.Abs(MathF.Abs(exitLocal.Z) - 0.05f) > 0.0005f)
            Fail($"penetration: wrong exit through the oriented slab (thickness {thick}, local exit {exitLocal})");
        else if (Vector3.Dot(t.Pieces[0].OutNormal, fwd) <= 0 || MathF.Abs(t.Pieces[0].OutNormal.Length() - 1) > 1e-4f)
            Fail($"penetration: the oriented slab's exit normal {t.Pieces[0].OutNormal} doesn't face along the ray");

        // Determinism and no allocations.
        var a = t.Run(lab, phys, 89.5f, Eye(-20), fwd, World.Range, 3);
        var fa = t.FactorAt(14);
        var b = t.Run(lab, phys, 89.5f, Eye(-20), fwd, World.Range, 3);
        if (BitConverter.SingleToUInt32Bits(a) != BitConverter.SingleToUInt32Bits(b) || BitConverter.SingleToUInt32Bits(fa) != BitConverter.SingleToUInt32Bits(t.FactorAt(14)))
            Fail("penetration: the same ray gave two answers");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sum = 0f;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            sum += t.Run(lab, phys, 89.5f, Eye(-20 + (i % 5) * 10), fwd, World.Range, 3);
            sum += t.FactorAt(14);
        }
        sw.Stop();
        // Allocations: the least of three more rounds of the same traces (a one-off JIT tier-up can't fail it).
        var bytes = Math.Min(GC.GetAllocatedBytesForCurrentThread() - before, SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                sum += t.Run(lab, phys, 89.5f, Eye(-20 + (i % 5) * 10), fwd, World.Range, 3);
                sum += t.FactorAt(14);
            }
        }, warmups: 0));
        PenNote($"1000 penetration traces: {sw.Elapsed.TotalMilliseconds / 1000 * 1000:0.0} us each, {bytes} bytes allocated (checksum {sum:0})");
        if (bytes > 0)
            Fail($"penetration traces allocate ({bytes} bytes for 1000)");
    }

    private static void CheckPenDecals(Map lab)
    {
        ImpactDecals.Configure(ImpactDecalQuality.High);
        var pool = ImpactDecals.Pool;
        pool.Clear(lab, 0);
        var entry = PenetrationTrace.EntryMarks;
        var exit = PenetrationTrace.ExitMarks;
        var origin = new Vector3(-20, 1.5f, 0);
        var end = new Vector3(-20, 1.5f, -30);
        var placed = PenetrationTrace.OnPenetratedTracer(lab, origin, end, 1, 1.0, false, 1f);
        PenNote($"wood + plaster wallbang: {placed} marks ({PenetrationTrace.EntryMarks - entry} entry, {PenetrationTrace.ExitMarks - exit} exit), pool {pool.Count}");
        if (placed != 4 || PenetrationTrace.EntryMarks - entry != 2 || PenetrationTrace.ExitMarks - exit != 2 || pool.Count != 4)
            Fail($"penetration decals: {placed} marks for two walls (want an entry and an exit mark on each)");
        var t = 2.0;
        // The least of a few identical rounds (SelfTestCpuRatio.cs): a real per-call allocation shows in every round, a one-off runtime allocation in one at most.
        var bytes = SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 50; i++)
                PenetrationTrace.OnPenetratedTracer(lab, origin + new Vector3(0, i * 0.01f, 0), end, 1, t += 1, false, 1f);
        });
        if (bytes > 0)
            Fail($"penetration decals allocate ({bytes} bytes for 50 tracers)");
        // The end itself is on the far side: nothing between, no extra marks.
        pool.Clear(lab, 100);
        if (PenetrationTrace.OnPenetratedTracer(lab, origin, new Vector3(-20, 1.5f, -10), 1, 100, false, 1f) != 0)
            Fail("penetration decals: marks placed for a tracer that ends on the first wall");
        ImpactDecals.Configure(ImpactDecalQuality.Off);
    }

    /// <summary>Real building parts: rays straight through wall boxes of a placed building leave each box exactly at its far face.</summary>
    private static void CheckPenBuildingParts()
    {
        var map = Maps.All.FirstOrDefault(m => m.PlacedBuildings.Count > 0 && m.BuildingLooks.Count > 0);
        if (map == null)
        {
            Fail("penetration: no map with buildings to check building parts on");
            return;
        }
        World.SetMap(map);
        var t = new PenetrationTrace();
        int tried = 0, matched = 0, bad = 0;
        foreach (var (i, _) in map.BuildingLooks)
        {
            if (tried >= 60 || i == map.GroundIndex)
                break;
            var box = map.Solid[i];
            var size = box.Half * 2;
            var axis = size.X < size.Z ? 0 : 2;
            if (size[axis] > 0.6f || size.Y < 1.5f || size[2 - axis] < 1f)
                continue;
            tried++;
            var dir = Geometry.Axis(axis, -1);
            var origin = Geometry.With(box.Center, axis, box.Max[axis] + 1.5f);
            t.Run(map, World.Physics, 1e6f, origin, dir, 4f, 3);
            for (var k = 0; k < t.PieceCount; k++)
            {
                if (t.Pieces[k].Shape.Index != i || t.Pieces[k].Shape.Kind != ShapeKind.Box)
                    continue;
                matched++;
                var wantIn = 1.5f;
                var wantOut = 1.5f + size[axis];
                // (a box right behind a touching one is found from just inside it: its entry is within the cast step)
                if (MathF.Abs(t.Pieces[k].In - wantIn) > PenetrationTrace.Step * 2 || MathF.Abs(t.Pieces[k].Out - wantOut) > 0.001f || t.Pieces[k].OutNormal != -t.Pieces[k].InNormal && t.Pieces[k].In > 0)
                    bad++;
            }
        }
        PenNote($"building parts on {map.Name}: {tried} wall boxes shot through, {matched} passed with their exit checked, {bad} wrong");
        if (matched == 0 || bad > 0)
            Fail($"penetration: building part exits ({matched} checked, {bad} wrong)");
    }

    /// <summary>The Bocage hedgerows (earth banks) stop every bullet; so does the terrain.</summary>
    private static void CheckPenHedgerowsAndTerrain()
    {
        var map = Maps.All.FirstOrDefault(m => m.PropInstances.Any(p => p.Def.Name.StartsWith("Bocage Hedgerow", StringComparison.Ordinal)));
        if (map == null)
        {
            Fail("penetration: the Bocage map isn't loaded");
            return;
        }
        World.SetMap(map);
        var t = new PenetrationTrace();
        int hedges = 0, through = 0, terrain = 0, terrainThrough = 0;
        foreach (var p in map.PropInstances.Where(p => p.Def.Name.StartsWith("Bocage Hedgerow", StringComparison.Ordinal) && p.SolidCount > 0).Take(40))
        {
            var c = map.Solid[p.SolidStart];
            var axis = c.Half.X < c.Half.Z ? 0 : 2;
            var origin = Geometry.With(c.Center, axis, c.Max[axis] + 2f);
            if (!World.Physics.Raycast(origin, Geometry.Axis(axis, -1), 5f, QueryFilter.Shots, out var first) || first.Kind != ShapeKind.Prop
                || first.Shape.Index < p.SolidStart || first.Shape.Index >= p.SolidStart + p.SolidCount)
                continue; // something else in front
            hedges++;
            if (PenetrationTrace.PropKey(map, first.Shape.Index) != MaterialKey.Hedgerow)
                Fail($"penetration: a Bocage hedgerow piece is {PenetrationTrace.PropKey(map, first.Shape.Index)}, not Hedgerow");
            t.Run(map, World.Physics, 1e6f, origin, Geometry.Axis(axis, -1), 20f, 3);
            if (t.Surfaces > 0)
                through++;
        }
        if (map.Terrain != null)
        {
            var rng = new Random(5);
            for (var i = 0; i < 40; i++)
            {
                var x = (float)(rng.NextDouble() * 2 - 1) * map.HalfX * 0.8f;
                var z = (float)(rng.NextDouble() * 2 - 1) * map.HalfZ * 0.8f;
                var o = new Vector3(x, map.GroundHeight(x, z) + 30f, z);
                if (!World.Physics.Raycast(o, -Vector3.UnitY, 60f, QueryFilter.Shots, out var hit) || hit.Kind != ShapeKind.Terrain)
                    continue;
                terrain++;
                var end = t.Run(map, World.Physics, 1e6f, o, -Vector3.UnitY, 60f, 3);
                if (t.Surfaces > 0 || MathF.Abs(end - hit.Distance) > 1e-3f)
                    terrainThrough++;
            }
        }
        PenNote($"{map.Name}: {hedges} hedgerow pieces shot side-on, {through} passed; {terrain} terrain hits from above, {terrainThrough} passed");
        if (hedges < 5 || through > 0)
            Fail($"penetration: bullets go through the Bocage hedgerows ({through} of {hedges})");
        if (terrain < 5 || terrainThrough > 0)
            Fail($"penetration: bullets go through the terrain ({terrainThrough} of {terrain})");
    }

    // ---- Server shots ----

    private static void CheckPenShots()
    {
        var lab = PenLab();
        using var game = ModeGame(0, GameMode.FreeForAll);
        game.SelfTestChangeMap(lab);
        World.Physics.AddObb(PenSlab);
        var shooter = game.Players[game.LocalId];
        var target = game.SelfTestAddHuman();
        game.SelfTestPerks(shooter, PerkSet.Empty);
        game.SelfTestPerks(target, PerkSet.Empty);
        var needle = Weapons.All.First(w => w.Name == "Needle Bolt-Action");
        var pistol = Weapons.All.First(w => w.Name == "P9 Sidearm");
        var now = 10.0;
        game.ServerTick = 400;

        (int Damage, byte Flags) Shoot(WeaponDef def, float x, Vector3 aimAt, bool head = false)
        {
            shooter.Primary = def.Id;
            shooter.Weapon = def.Id;
            game.SelfTestPlace(shooter, new Vector3(x, 0, 0), 0);
            game.SelfTestPlace(target, new Vector3(x, 0, -14), MathF.PI);
            game.LastHitFlags = 0;
            now += 2;
            var eye = shooter.Eye;
            var at = head ? target.Position + new Vector3(0, World.HeightOf(target.Sim.Stance) - 0.12f, 0) : aimAt;
            game.SelfTestPenShot(shooter, def, eye, Vector3.Normalize(at - eye), game.ServerTick, now);
            return (World.MaxHealth - target.Health, game.LastHitFlags);
        }

        // Through 10 cm wood + 10 cm plaster: the sniper hits with the reduced damage, the pistol doesn't.
        var (dmg, flags) = Shoot(needle, -20, new Vector3(-20, 1.2f, -14));
        var f = game.SelfTestLastPen.FactorAt(14f);
        var want = (int)MathF.Round(needle.Damage * f);
        PenNote($"{needle.Name} through wood + plaster at a body 14 m out: {dmg} damage (x{f:0.000}), flags {flags}");
        if (dmg != want || (flags & Game.ShotWallbang) == 0 || (flags & Game.ShotPenetrated) == 0 || (flags & 1) == 0)
            Fail($"penetration shot: {dmg} damage, flags {flags} (want {want}, a hit with the wallbang and penetrated flags)");
        var packet = game.SelfTestLastPacket;
        if (packet.Length != 5 + 12 + 12 || (packet[3] & Game.ShotPenetrated) == 0)
            Fail($"penetration shot: the Shot packet is {packet.Length} bytes with flags {packet[3]} (want the end then the origin, 29 bytes)");
        (dmg, flags) = Shoot(needle, -20, Vector3.Zero, head: true);
        if ((flags & 2) == 0 || dmg <= want)
            Fail($"penetration shot: a headshot through the wall gave {dmg} damage, flags {flags}");
        (dmg, flags) = Shoot(pistol, -20, new Vector3(-20, 1.2f, -14));
        if (dmg != 0 || (flags & (1 | Game.ShotPenetrated)) != 0)
            Fail($"penetration shot: the P9 went through 10 cm of wood ({dmg} damage, flags {flags})");
        if (game.SelfTestLastPacket.Length != 5 + 12)
            Fail($"penetration shot: a shot that passed nothing sent {game.SelfTestLastPacket.Length} bytes (want 17, no origin)");
        (dmg, _) = Shoot(needle, -10, new Vector3(-10, 1.2f, -14));
        if (dmg != 0)
            Fail($"penetration shot: through 50 cm of concrete ({dmg} damage)");

        // Lag compensation: the target was behind the wall 3 ticks ago, it has moved since. The rewound spot is hit through the wall.
        {
            shooter.Primary = needle.Id;
            shooter.Weapon = needle.Id;
            game.SelfTestPlace(shooter, new Vector3(-20, 0, 0), 0);
            game.SelfTestPlace(target, new Vector3(-26, 0, -14), MathF.PI); // live: out of the line
            var t0 = game.ServerTick;
            for (var k = t0 - 4; k <= t0; k++)
                target.History[k % Player.HistorySize] = new HistoryEntry { Position = new Vector3(-20, 0, -14), Stance = Stance.Stand, Yaw = MathF.PI, Tick = k };
            target.History[t0 % Player.HistorySize].Tick = 0; // the current tick not recorded yet: live
            var eye = shooter.Eye;
            var dir = Vector3.Normalize(new Vector3(-20, 1.2f, -14) - eye);
            now += 2;
            game.SelfTestPenShot(shooter, needle, eye, dir, t0 - 3, now);
            var rewound = World.MaxHealth - target.Health;
            target.Health = World.MaxHealth;
            now += 2;
            game.SelfTestPenShot(shooter, needle, eye, dir, t0, now);
            var live = World.MaxHealth - target.Health;
            PenNote($"lag compensation through a wall: rewound target {rewound} damage, live (moved away) {live}");
            if (rewound != want || live != 0)
                Fail($"penetration with lag compensation: rewound {rewound} (want {want}), live {live} (want 0)");
        }

        // Bots: a line through thin walls only to a fresh known spot.
        shooter.Primary = needle.Id;
        shooter.Weapon = needle.Id;
        game.SelfTestPlace(shooter, new Vector3(-20, 0, 0), 0);
        var spot = new Vector3(-20, 1.2f, -14);
        var order = new SquadOrder { Kind = SquadOrderKind.Suppress, Look = spot, HasLook = true, Fire = true };
        var stale = order with { Fire = false };
        var concrete = order with { Look = new Vector3(-10, 1.2f, -14) };
        game.SelfTestPlace(shooter, new Vector3(-20, 0, 0), 0);
        var okLine = game.SelfTestBotWallbang(shooter, spot);
        var okFresh = game.SelfTestBotSuppressThroughWall(shooter, order);
        var okStale = game.SelfTestBotSuppressThroughWall(shooter, stale);
        game.SelfTestPlace(shooter, new Vector3(-10, 0, 0), 0);
        var okConcrete = game.SelfTestBotSuppressThroughWall(shooter, concrete);
        PenNote($"bots: wallbang line {okLine}, fresh spot {okFresh}, no known spot {okStale}, behind thick concrete {okConcrete}");
        if (!okLine || !okFresh || okStale || okConcrete)
            Fail("penetration: bots' through-the-wall suppression rule is wrong (must need a fresh known spot and a wall their gun passes)");

        // The map override.
        lab.Penetration = false;
        (dmg, flags) = Shoot(needle, -20, new Vector3(-20, 1.2f, -14));
        lab.Penetration = true;
        if (dmg != 0 || (flags & Game.ShotPenetrated) != 0)
            Fail($"penetration: \"penetration\": false didn't stop the sniper ({dmg} damage, flags {flags})");
        lab.Penetration = false;
        var json = MapFile.ToJson(lab);
        lab.Penetration = true;
        if (!json.Contains("\"penetration\": false", StringComparison.Ordinal) || MapFile.ToJson(lab).Contains("\"penetration\"", StringComparison.Ordinal))
            Fail("penetration: the map writer doesn't write \"penetration\": false (and only then)");
        var mini = "{ \"format\": \"" + MapFile.Format + "\", \"name\": \"Pen Off\", \"halfX\": 10, \"halfZ\": 10, \"penetration\": false, "
                   + "\"ops\": [ { \"op\": \"Ground\", \"color\": [0.5, 0.5, 0.5] }, { \"op\": \"Spawn\", \"x\": 0, \"z\": 0 } ] }";
        if (MapFile.Load(mini, "penetration test").Penetration || !MapFile.Load(mini.Replace("\"penetration\": false, ", ""), "penetration test").Penetration)
            Fail("penetration: the map file's \"penetration\" key doesn't load (false turns it off, missing keeps it on)");
        if (Maps.All.Any(m => !m.Penetration))
            PenNote($"maps with penetration off: {string.Join(", ", Maps.All.Where(m => !m.Penetration).Select(m => m.Name))}");

        // Same shot twice: the same damage.
        var (d1, _) = Shoot(needle, -20, new Vector3(-20, 1.2f, -14));
        var (d2, _) = Shoot(needle, -20, new Vector3(-20, 1.2f, -14));
        if (d1 != d2)
            Fail($"penetration: the same shot did {d1} then {d2} damage");
    }
}
