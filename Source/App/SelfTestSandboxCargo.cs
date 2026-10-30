using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Sandbox physics-only parts" (decision 21, board item cv01; docs/sandbox/CONSTRUCTS.md "Physics-only parts"): the
    /// class comes from the tool and the mass for every loaded part (no list) and from a file's "physicsOnly"; a welded part
    /// can't be held into the backpack, set into a storage slot or picked up (each says why), is given as a body where it
    /// was, and comes off a construct as a body there; a wrench part still goes into the hands, the backpack and a slot;
    /// the creative inventory spawns a physics-only part in front of you (held by the physics gun when it is out) and a
    /// wrench part into the hands; a creative wrench fit and removal are instant, survival's timed; what an old save held
    /// drops into the world; the wrench bolts on only a loose wrench part.
    /// </summary>
    private static void CheckSandboxPhysicsOnly()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"physics-only parts: the {ConstructYardMap} map (with its sandbox section) is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var frame = ConstructParts.Need("Frame Block");
        var engine = ConstructParts.Need("Engine");
        var door = ConstructParts.Need("Wood Door 1.25 m");
        var lamp = ConstructParts.Need("Lamp");
        var volume = ConstructParts.Need("Cargo Volume");

        // The class, by the tool and the mass (so every new welder or heavy part follows), and the file's flag.
        var misfiled = ConstructParts.All.Where(d => d.PhysicsOnly != (d.Tool == ConstructTool.Welder || d.Mass > ConstructParts.HandCarryLimit || d.PhysicsWhy == "bulky")).Select(d => d.Name).ToList();
        var welded = ConstructParts.All.Count(d => d.Tool == ConstructTool.Welder);
        var heavy = ConstructParts.All.Count(d => d.Tool != ConstructTool.Welder && d.Mass > ConstructParts.HandCarryLimit);
        var bulky = ConstructParts.All.Where(d => d.PhysicsWhy == "bulky").Select(d => d.Name).ToList();
        Line($"physics-only: {welded} welded, {heavy} heavy (over {ConstructParts.HandCarryLimit:0} kg), {bulky.Count} bulky ({string.Join(", ", bulky)}); items {ConstructParts.All.Count(d => !d.PhysicsOnly)}; "
             + $"Frame Block {ConstructEdits.ClassText(frame)}, Engine {ConstructEdits.ClassText(engine)}, {door.Name} {ConstructEdits.ClassText(door)}, Lamp {ConstructEdits.ClassText(lamp)}, Cargo Volume {ConstructEdits.ClassText(volume)}");
        Want(misfiled.Count == 0 && frame.PhysicsWhy == "welded" && engine.PhysicsWhy.StartsWith("heavy", StringComparison.Ordinal) && door.PhysicsWhy == "bulky" && !lamp.PhysicsOnly && !volume.PhysicsOnly,
            $"the class follows the tool, the mass and the file ({string.Join(", ", misfiled.Take(5))})");
        var lampText = File.ReadAllText(Path.Combine(ConstructParts.Folder, lamp.Folder, lamp.File));
        var at = lampText.IndexOf('{');
        var flagged = ConstructParts.Parse(lampText[..(at + 1)] + " \"physicsOnly\": true," + lampText[(at + 1)..], "flagged.json");
        string? badFlag = null;
        try
        {
            ConstructParts.Parse(lampText[..(at + 1)] + " \"physicsOnly\": 3," + lampText[(at + 1)..], "bad.json");
        }
        catch (ConstructPartException ex)
        {
            badFlag = ex.Message;
        }
        Want(flagged.PhysicsOnly && flagged.PhysicsWhy == "bulky" && badFlag != null && badFlag.Contains("physicsOnly"), $"\"physicsOnly\": true makes a light part physics-only; a bad value is refused (\"{badFlag}\")");

        // Hands, the backpack, storage, picking up: refused with the reason; a wrench part still goes everywhere.
        var w = new ConstructWorld();
        var e = new ConstructEdits();
        const byte me = 3;
        var now = 0.0;
        ref var h = ref e.Hand(me);
        h.Held = frame.Id;
        var stow = e.Stow(me);
        Want(stow != null && stow.Contains("physics-only (welded)") && stow.Contains("backpack") && h.Held == frame.Id, $"a welded part can't be stowed (\"{stow}\")");
        var chest = BuildAt(w, "Chest", 30, 36);
        chest.Owner = me;
        var chestEye = EyeBy(chest, 0);
        var slotted = e.StoreHeld(w, me, chestEye, chest.Id, 0, 0);
        Want(slotted != null && slotted.Contains("physics-only") && h.Held == frame.Id, $"a welded part can't be set into a storage slot (\"{slotted}\")");
        h.Held = lamp.Id;
        var lampStow = e.Stow(me);
        var lampDraw = e.DrawFromPack(me);
        var lampSlot = e.StoreHeld(w, me, chestEye, chest.Id, 0, 0);
        Want(lampStow == null && lampDraw == null && lampSlot == null && h.Held == ConstructParts.None, $"a lamp (an item) is stowed, drawn and set into a slot (\"{lampStow}\", \"{lampDraw}\", \"{lampSlot}\")");
        var where = ConstructWorld.Ground(World.Query, 33, 30) + new Vector3(0, 1.2f, 0);
        var given = e.Give(w, me, frame, where);
        var body = w.Bodies.LastOrDefault(b => b.Loose && b.DefOf(0) == frame);
        Want(given == GiveResult.Dropped && h.Held == ConstructParts.None && body != null && Vector3.Distance(body.WorldCom, where) < 0.05f && body.Owner == me,
            $"the give rule leaves a welded part as a body where it was ({given}, hands {(h.Held == ConstructParts.None ? "empty" : "full")})");
        var picked = body == null ? "no body" : e.PickUp(w, me, body.WorldCom + new Vector3(0, 0.5f, 1f), body.Id);
        Want(picked != null && picked.Contains("physics-only") && body != null && w.Get(body.Id) == body, $"a welded body can't be picked up (\"{picked}\")");

        // Taking parts off: a welded one is cut free as a body where it was; a lamp goes into the hands (instant in creative).
        var gate = BuildAt(w, "Gate", 36, 24);
        gate.Owner = me;
        var block = PartOf(gate, PartRole.Frame);
        var blockDef = gate.DefOf(block);
        var blockAt = PartCentre(gate, block);
        h.Tool = ConstructTool.Welder;
        var eye = EyeBy(gate, block);
        var began = e.BeginRemove(w, me, eye, gate.Id, block, now);
        var timed = h.Edit == EditKind.Remove;
        now += ConstructBuild.RemoveSeconds(blockDef) + 0.1;
        e.Tick(w, now, _ => eye);
        var cut = w.Bodies.LastOrDefault(b => b.Loose && b.DefOf(0) == blockDef);
        Want(began == null && timed && h.Held == ConstructParts.None && cut != null && Vector3.Distance(cut.WorldCom, blockAt) < 0.05f,
            $"the welder cuts a welded part free as a body where it was (\"{began}\", timed {timed}, {(cut != null ? $"{Vector3.Distance(cut.WorldCom, blockAt):0.00} m off" : "no body")})");
        var post = BuildAt(w, "Lamp Post", 24, 36);
        post.Owner = me;
        var li = PartNamed(post, lamp);
        h.Tool = ConstructTool.Wrench;
        eye = EyeBy(post, li);
        var lampParts = post.PartCount;
        began = e.BeginRemove(w, me, eye, post.Id, li, now);
        var instant = post.PartCount == lampParts - 1 && h.Held == lamp.Id && h.Edit == EditKind.None;
        Want(began == null && instant, $"a creative wrench removal is instant, into the hands (\"{began}\", {post.PartCount}/{lampParts} parts)");
        Span<ConstructGhost> ghosts = stackalloc ConstructGhost[ConstructBuild.MaxGhosts];
        var gn = ConstructBuild.Ghosts(post, lamp, ghosts, post.Com, 50);
        var fitNow = gn > 0 && e.BeginAttach(w, me, eye, post.Id, ghosts[0].Part, ghosts[0].Socket, ghosts[0].Variant, now) == null && post.PartCount == lampParts;
        // Survival: the same edits are timed.
        e.InfiniteParts = false;
        e.Survival = true;
        li = PartNamed(post, lamp);
        began = li < 0 ? "no lamp" : e.BeginRemove(w, me, eye, post.Id, li, now);
        var survivalTimed = began == null && h.Edit == EditKind.Remove && post.PartCount == lampParts;
        now += ConstructBuild.RemoveSeconds(lamp) + 0.1;
        e.Tick(w, now, _ => eye);
        var survivalDone = post.PartCount == lampParts - 1 && h.Held == lamp.Id;
        e.InfiniteParts = true;
        e.Survival = false;
        Want(fitNow && survivalTimed && survivalDone, $"a creative wrench fit is instant ({fitNow}); survival's wrench removal is timed ({survivalTimed}, then done {survivalDone}; \"{began}\")");

        // The creative inventory: a physics-only part spawns in front of you (held by the physics gun if it's out); a wrench part comes to the hands.
        var sb = yard.Sandbox;
        h.Held = ConstructParts.None;
        var noWorld = e.TakePart(sb, me, frame.Id);
        if (OpenSpot(w) is not { } spot)
        {
            problems.Add("no open ground on the yard");
            spot = ConstructWorld.Ground(World.Query, 0, 0);
        }
        var feet = spot + new Vector3(0, 0.05f, 0);
        var menuEye = feet + new Vector3(0, World.EyeHeight, 0);
        var count = w.Bodies.Count;
        var spawn = e.TakePart(sb, me, frame.Id, w, menuEye, 0);
        var spawned = w.Bodies.Count == count + 1 ? w.Bodies[^1] : null;
        var ahead = spawned == null ? -1 : Vector3.Dot(spawned.WorldCom - menuEye, World.FlatForward(0));
        Want(noWorld != null && noWorld.Contains("physics-only") && spawn == null && spawned != null && spawned.DefOf(0) == frame && h.Held == ConstructParts.None && ahead is > 0.5f and < 3.5f && spawned.Owner == me,
            $"the creative inventory spawns a welded part {ahead:0.0} m in front of you, not into the hands (\"{spawn}\")");
        h.Tool = ConstructTool.PhysicsGun;
        var held = e.TakePart(sb, me, engine.Id, w, menuEye, 0);
        var heldBody = w.Bodies[^1];
        Want(held == null && heldBody.DefOf(0) == engine && heldBody.GrabbedBy == me && h.GrabBody == heldBody.Id, $"with the physics gun out, the gun holds the spawned engine (\"{held}\")");
        e.Release(w, me);
        h.Tool = ConstructTool.Wrench;
        var lampTake = e.TakePart(sb, me, lamp.Id, w, menuEye, 0);
        Want(lampTake == null && h.Held == lamp.Id, $"a wrench part from the inventory comes into the hands (\"{lampTake}\")");

        // An old save's hands, backpack and storage with physics-only parts in them: they drop into the world as bodies.
        h.Held = frame.Id;
        var pack = e.Pack(me);
        pack.Def[0] = engine.Id;
        pack.Count[0] = 1;
        pack.Def[1] = lamp.Id;
        pack.Count[1] = 1;
        var st = w.StoreOf(chest, 0)!;
        st.Def[1] = frame.Id;
        st.Count[1] = 1;
        var bodies = w.Bodies.Count;
        var dropped = e.DropPhysicsOnly(w, id => id == me ? menuEye : null);
        Want(dropped == 3 && w.Bodies.Count == bodies + 3 && h.Held == ConstructParts.None && pack.Def[0] == ConstructParts.None && pack.Def[1] == lamp.Id && st.Def[1] == ConstructParts.None && st.Def[0] == lamp.Id,
            $"physics-only parts held, in the backpack and in storage drop into the world; the items stay ({dropped} dropped)");

        // The wrench bolts on only a loose wrench part; the welder joins any two.
        var looseEngine = ConstructBuild.PlaceNew(w, engine, ConstructWorld.Ground(World.Query, 30, 30) + new Vector3(0, 0.5f, 0), 0)!;
        var looseBlock = ConstructBuild.PlaceNew(w, frame, ConstructWorld.Ground(World.Query, 31, 30) + new Vector3(0, 0.5f, 0), 0)!;
        Want(ConstructEdits.JoinTool(ConstructTool.Wrench, gate, looseEngine) && !ConstructEdits.JoinTool(ConstructTool.Wrench, gate, looseBlock) && ConstructEdits.JoinTool(ConstructTool.Welder, gate, looseBlock),
            "the wrench joins a loose wrench part, the welder anything");
        foreach (var p in problems)
            Fail($"physics-only parts: {p}");
    }

    /// <summary>
    /// "Sandbox cargo volumes" (decision 21; docs/sandbox/CONSTRUCTS.md "Cargo volumes"): on a buggy, a frozen volume catches
    /// a loose block resting in it, which then stays put on the buggy while it drives, turns and pitches, and counts in its
    /// mass; unfreezing drops it with the buggy's velocity; the manual toggle and the data input both work (the last change
    /// wins); something falling in is caught once it settles; the physics gun takes a carried body out; the box's handles
    /// drag and snap, the server snaps and clamps, a smaller box lets go; at most four volumes; a blueprint carries cargo
    /// and is written back; the construct message carries it; the cargo step allocates nothing.
    /// </summary>
    private static void CheckSandboxCargo()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"cargo volumes: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var w = new ConstructWorld();
        var e = new ConstructEdits();
        const byte me = 5;
        var now = 0.0;
        ref var h = ref e.Hand(me);
        var volume = ConstructParts.Need("Cargo Volume");
        var frame = ConstructParts.Need("Frame Block");
        var sw = ConstructParts.Need("Switch");
        var buttons = Buttons.None;
        Construct? driven = null;
        void Ticks(int n)
        {
            for (var k = 0; k < n; k++)
            {
                now += World.TickDt;
                if (driven != null)
                    ConstructEdits.DriveInput(driven, buttons, World.TickDt);
                e.Tick(w, now, _ => null);
                w.Step(World.Query, World.TickDt);
            }
        }
        if (OpenSpot(w) is not { } spot)
        {
            Fail("cargo volumes: no open ground on the yard");
            return;
        }
        // Facing the middle of the yard (open ground to drive on), whichever corner the spot is in.
        var face = Enumerable.Range(0, 4).Select(k => k * MathF.PI / 2).MaxBy(y => Vector3.Dot(World.FlatForward(y), -(spot with { Y = 0 })));
        var car = ConstructBuild.BuildOnGround(w, ConstructBuild.Blueprints[IndexOfBlueprint("Buggy")], spot, face, out var why)!;
        if (car == null)
        {
            Fail($"cargo volumes: the buggy didn't build ({why})");
            return;
        }
        car.Owner = me;
        Ticks(60);
        // A volume on it, its box over the whole buggy and above it (set as the configure tool would).
        var ghosts = new ConstructGhost[ConstructBuild.MaxGhosts];
        int Fit(ConstructPartDef def)
        {
            var n = ConstructBuild.Ghosts(car, def, ghosts, car.Com, 50);
            return n > 0 ? ConstructBuild.Attach(w, car, def, ghosts[0]) : -1;
        }
        var vi = Fit(volume);
        if (vi < 0)
        {
            Fail("cargo volumes: no socket on the buggy takes a Cargo Volume");
            return;
        }
        car.Rebuild();
        Span<byte> slots = stackalloc byte[6];
        ConstructCargo.BoxSlots(volume, slots);
        var centre = ConstructCargo.ToPart(car, vi, car.WorldCom + Vector3.Transform(Vector3.UnitY, car.Rot) * 0.6f);
        float[] box = [8f, 3.6f, 8f, ConstructCargo.SnapTo(centre.X), ConstructCargo.SnapTo(centre.Y), ConstructCargo.SnapTo(centre.Z)];
        for (var k = 0; k < 6; k++)
            ConstructConfig.Set(ref car.Parts[vi], volume, slots[k], box[k]);
        var mass0 = car.Mass;
        // A loose block dropped onto it settles there.
        var top = car.ToWorld(new Vector3(car.Com.X, car.LocalMax.Y + 0.35f, car.Com.Z));
        var load = ConstructBuild.PlaceNew(w, frame, top, 0)!;
        Ticks(90);
        var inside = ConstructCargo.MostlyInside(car, vi, load);
        var useEye = PartCentre(car, vi) + new Vector3(0, 0.6f, 0.8f);
        var use = e.Use(w, me, useEye, car.Id, vi);
        Ticks(3);
        var caught = load.CarriedBy == car.Id;
        Ticks(3);
        var massIn = car.Mass;
        Line($"cargo: a block on the buggy, inside {inside}; F on the volume (\"{use ?? "ok"}\"): frozen {ConstructCargo.IsFrozen(car.Parts[vi])}, caught {caught}; the buggy's mass {mass0:0} -> {massIn:0} kg (the block {load.Mass:0})");
        Want(inside && use == null && caught && MathF.Abs(massIn - mass0 - load.Mass) < 0.5f, "a frozen volume catches the block resting in it and the buggy's mass counts it");

        // Drive, turn and pitch: the block stays where it is on the buggy.
        var seat = PartOf(car, PartRole.Seat);
        var sat = e.Use(w, me, PartCentre(car, seat) + new Vector3(0, 0.6f, 0.8f), car.Id, seat);
        driven = car;
        var rel0 = car.ToLocal(load.Pos);
        var start = car.WorldCom;
        var drift = 0f;
        var asleep = true;
        buttons = Buttons.Forward;
        for (var k = 0; k < 80; k++)
        {
            Ticks(1);
            drift = MathF.Max(drift, Vector3.Distance(car.ToLocal(load.Pos), rel0));
            asleep &= load.Asleep;
        }
        buttons = Buttons.Forward | Buttons.Right;
        for (var k = 0; k < 70; k++)
        {
            Ticks(1);
            drift = MathF.Max(drift, Vector3.Distance(car.ToLocal(load.Pos), rel0));
        }
        var moved = Vector3.Distance(car.WorldCom with { Y = 0 }, start with { Y = 0 });
        var turned = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, car.Rot), Vector3.Transform(Vector3.UnitZ, ConstructGrab.YawQ(face))), -1, 1)) * 180 / MathF.PI;
        // A climb: the buggy pitched nose up by 20 degrees (as on a slope); the block turns with it.
        // (the pose set and the cargo step run on it alone, then the pose put back: the drive goes on undisturbed)
        var rotWas = car.Rot;
        car.Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitX, car.Rot), 20f * MathF.PI / 180f) * car.Rot);
        ConstructCargo.Step(w);
        var pitchDrift = Vector3.Distance(car.ToLocal(load.Pos), rel0);
        drift = MathF.Max(drift, pitchDrift);
        car.Rot = rotWas;
        ConstructCargo.Step(w);
        Line($"cargo while driving (\"{sat ?? "seated"}\", drives {car.CanDrive}): moved {moved:0.0} m, turned {turned:0} deg, pitched 20 deg; the block's drift on the buggy {drift * 100:0.00} cm, asleep throughout {asleep}");
        Want(sat == null && moved > 2f && turned > 10f && drift < 0.01f && asleep && load.CarriedBy == car.Id, "the frozen block stays in place on the buggy while it drives, turns and climbs, with no physics of its own");

        // Unfreezing while it moves: the block goes free at the buggy's velocity there.
        buttons = Buttons.Forward;
        Ticks(10);
        e.Use(w, me, PartCentre(car, vi) + new Vector3(0, 0.6f, 0.8f), car.Id, vi);
        Ticks(1); // (let go in this tick's cargo step: compared before the contacts with the deck act on it)
        var pv = car.PointVel(load.WorldCom);
        var velOff = (load.Vel - pv).Length();
        var freed = load.CarriedBy == 0 && !load.Asleep;
        Ticks(3);
        Line($"cargo let go at {car.Vel.Length():0.0} m/s: free {freed}, its velocity {load.Vel.Length():0.0} m/s, {velOff:0.00} m/s off the buggy's there; the buggy's mass {car.Mass:0} kg");
        Want(freed && velOff < 0.5f && car.Vel.Length() > 1f && MathF.Abs(car.Mass - mass0) < 0.5f, "unfreezing drops it with the buggy's velocity and takes its mass off");
        buttons = Buttons.Jump; // (the brake)
        Ticks(150);
        driven = null;
        e.Stand(w, me);

        // The data input and the manual toggle on the same part: the last change wins.
        var si = Fit(sw);
        var wired = si >= 0 && ConstructBuild.CanWire(w, car, si, vi) == null && w.AddWire(car, si, vi);
        car.Rebuild();
        var swEye = PartCentre(car, si < 0 ? 0 : si) + new Vector3(0, 0.6f, 0.8f);
        bool Frozen() => ConstructCargo.IsFrozen(car.Parts[vi]);
        var s0 = Frozen();
        e.Use(w, me, swEye, car.Id, si);
        Ticks(4);
        var s1 = Frozen();
        e.Use(w, me, PartCentre(car, vi) + new Vector3(0, 0.6f, 0.8f), car.Id, vi);
        Ticks(4);
        var s2 = Frozen();
        e.Use(w, me, swEye, car.Id, si);
        Ticks(4);
        var s3 = Frozen();
        e.Use(w, me, swEye, car.Id, si);
        Ticks(4);
        var s4 = Frozen();
        Line($"cargo logic: wired {wired}; frozen {s0}, switch on {s1}, F on the volume {s2}, switch off {s3}, switch on {s4}");
        Want(wired && !s0 && s1 && !s2 && !s3 && s4, "the data input and the manual toggle both set the volume (the last change wins)");

        // Something thrown in while it is frozen: caught once it settles, not while it flies.
        var drop = ConstructBuild.PlaceNew(w, frame, car.ToWorld(new Vector3(car.Com.X + 0.3f, car.LocalMax.Y + 1.2f, car.Com.Z)), 0)!;
        drop.Vel = new Vector3(0, -5f, 0);
        drop.Wake();
        Ticks(2);
        var flying = drop.CarriedBy;
        Ticks(120);
        var settled = drop.CarriedBy == car.Id;
        Want(flying == 0 && settled, $"a block thrown into the frozen volume is caught once it settles (flying {flying}, settled {settled})");

        // The physics gun takes a carried body out (and it isn't caught again while held).
        h.Tool = ConstructTool.PhysicsGun;
        var gunEye = drop.WorldCom + new Vector3(0, 1.5f, 3f);
        var grab = e.Grab(w, me, gunEye, 0, drop.Id, drop.Com);
        Ticks(20);
        Want(grab == null && drop.CarriedBy == 0 && drop.GrabbedBy == me, $"the physics gun takes a carried block out (\"{grab}\", carried by {drop.CarriedBy})");
        e.Release(w, me);
        h.Tool = ConstructTool.None;

        // The box: its handles drag and snap (the opposite face stays), drawing one out spans the corners; the server snaps and clamps.
        ConstructCargo.DragFace(new Vector3(2, 1, 2), new Vector3(0, 0.5f, 0), 0, new Vector3(3.04f, 0.5f, 5), -Vector3.UnitZ, out var size1, out var at1);
        ConstructCargo.DragFace(new Vector3(2, 1, 2), new Vector3(0, 0.5f, 0), 3, new Vector3(0, -0.47f, 5), -Vector3.UnitZ, out var size2, out var at2);
        ConstructCargo.DragFace(new Vector3(2, 1, 2), new Vector3(0, 0.5f, 0), 0, new Vector3(50, 0.5f, 5), -Vector3.UnitZ, out var size3, out _);
        ConstructCargo.DragOut(new Vector3(-0.33f, 0.02f, -0.21f), new Vector3(1.26f, 0.4f, 0.88f), 1.04f, out var size4, out var at4);
        bool OnGrid(float v) => MathF.Abs(v / ConstructCargo.Snap - MathF.Round(v / ConstructCargo.Snap)) < 1e-3f;
        Line($"cargo box: +x dragged to 3.04: {size1.X:0.###} wide at {at1.X:0.###}; -y down to -0.47: {size2.Y:0.###} high at {at2.Y:0.###}; +x to 50: {size3.X:0.###}; drawn out: {size4.X:0.###} x {size4.Y:0.###} x {size4.Z:0.###} at {at4.X:0.###}, {at4.Y:0.###}, {at4.Z:0.###}");
        Want(MathF.Abs(size1.X - 4) < 1e-3f && MathF.Abs(at1.X - 1) < 1e-3f && MathF.Abs(size2.Y - 1.5f) < 1e-3f && MathF.Abs(at2.Y - 0.25f) < 1e-3f && MathF.Abs(size3.X - ConstructCargo.MaxSide) < 1e-3f
             && OnGrid(size4.X) && OnGrid(size4.Z) && OnGrid(at4.X - size4.X * 0.5f) && MathF.Abs(size4.X - 1.6f) < 1e-3f && MathF.Abs(size4.Z - 1.1f) < 1e-3f && MathF.Abs(size4.Y - 1f) < 1e-3f,
            "the box's face handles drag and snap to the grid with the opposite face kept, and a drawn box spans its corners");
        h.Tool = ConstructTool.Configure;
        var cfgEye = PartCentre(car, vi) + new Vector3(0, 0.6f, 0.8f);
        var cfg = e.Configure(w, me, cfgEye, car.Id, [(byte)vi], [slots[0], slots[1]], [1.234f, 20f], null);
        var gotX = ConstructConfig.At(car.Parts[vi], volume, slots[0]);
        var gotY = ConstructConfig.At(car.Parts[vi], volume, slots[1]);
        Want(cfg == null && MathF.Abs(gotX - 1.2f) < 1e-4f && MathF.Abs(gotY - ConstructCargo.MaxSide) < 1e-4f, $"the server snaps a size to 10 cm and holds it to {ConstructCargo.MaxSide} m (\"{cfg}\", {gotX}, {gotY})");
        // Made small and moved away, the box lets the block go at its next look.
        var kept = drop.CarriedBy;
        Ticks(30);
        var keptCarried = drop.CarriedBy == car.Id;
        e.Configure(w, me, cfgEye, car.Id, [(byte)vi], [slots[0], slots[1], slots[2], slots[3]], [0.2f, 0.2f, 0.2f, 6f], null);
        Ticks(12);
        Want(kept == 0 && keptCarried && drop.CarriedBy == 0, $"a box made smaller and moved off lets its cargo go (carried before {keptCarried}, after {drop.CarriedBy})");
        h.Tool = ConstructTool.None;

        // At most four volumes a construct.
        var volumes = 1;
        while (volumes < ConstructCargo.MaxVolumes && Fit(volume) >= 0)
            volumes++;
        var fifth = ConstructBuild.Ghosts(car, volume, ghosts, car.Com, 50);
        var lampGhosts = ConstructBuild.Ghosts(car, ConstructParts.Need("Lamp"), ghosts, car.Com, 50);
        Want(volumes == ConstructCargo.MaxVolumes && fifth == 0 && lampGhosts > 0, $"a construct takes at most {ConstructCargo.MaxVolumes} cargo volumes ({volumes} fitted, {fifth} sockets for a fifth)");

        // A blueprint built carrying its cargo, written back, built again the same; cargo outside a frozen volume is refused.
        var rack = ConstructBuild.FindBlueprint("Cargo Rack");
        var rackAt = ConstructWorld.Ground(World.Query, spot.X + 12, spot.Z);
        var r1 = rack == null ? null : ConstructBuild.BuildOnGround(w, rack, rackAt, 0, out why);
        var carried1 = r1 == null ? new List<Construct>() : w.Bodies.Where(b => b.CarriedBy == r1.Id).ToList();
        var json = r1 == null ? null : ConstructCargo.ToJson(w, r1);
        string? rebuilt = null;
        var same = false;
        if (r1 != null && json != null)
        {
            var text = File.ReadAllText(Path.Combine(ConstructBuild.BlueprintFolder, rack!.File));
            var cut0 = text.IndexOf("\"cargo\"", StringComparison.Ordinal);
            var copy = text[..cut0] + "\"cargo\": " + json + "\n}\n";
            var bp2 = ConstructBuild.ParseBlueprint(copy, "copy.json");
            var w2 = new ConstructWorld();
            var r2 = ConstructBuild.Build(w2, bp2, r1.Pos, r1.Rot, out rebuilt);
            var c2 = r2 == null ? new List<Construct>() : w2.Bodies.Where(b => b.CarriedBy == r2.Id).ToList();
            same = r2 != null && c2.Count == carried1.Count && c2.Zip(carried1).All(p => Vector3.Distance(p.First.Pos, p.Second.Pos) < 0.01f && p.First.CarryPart == p.Second.CarryPart);
        }
        var outside = ConstructBuild.ParseBlueprint("""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Bad Cargo", "steps": [ { "part": "Foundation" } ],
              "cargo": [ { "part": "Frame Block", "at": [0, 3, 0] } ] }
            """, "bad.json");
        var refused = ConstructBuild.Build(new ConstructWorld(), outside, Vector3.Zero, Quaternion.Identity, out var refusedWhy);
        Line($"cargo blueprint: the {rack?.Name ?? "missing"} built carrying {carried1.Count}, its mass {r1?.Mass:0} kg; written back as {json}; built again the same {same} (\"{rebuilt}\"); cargo outside: \"{refusedWhy}\"");
        Want(r1 != null && carried1.Count == 2 && same && refused == null && refusedWhy.Contains("isn't inside"), "a blueprint builds with its cargo caught, writes it back, builds the same; cargo outside a frozen volume is refused");

        // The construct message carries a carried body (its carrier and its pose there); the client draws it with the carrier.
        if (r1 != null && carried1.Count > 0)
        {
            var client = new ConstructWorld();
            var pw = new PacketWriter(16384);
            foreach (var c in w.Bodies)
            {
                ConstructNet.WriteFull(pw, c);
                var pr = new PacketReader(pw.Span[1..]);
                ConstructNet.Read(ref pr, client, 0);
            }
            var b0 = carried1[0];
            var copyB = client.Get(b0.Id);
            client.Get(r1.Id)!.Pos += new Vector3(0, 0, 0); // (as received)
            ConstructCargo.FollowAll(client);
            var netOk = copyB != null && copyB.CarriedBy == r1.Id && Vector3.Distance(copyB.CarryPos, b0.CarryPos) < 1e-4f && Vector3.Distance(copyB.Pos, b0.Pos) < 0.005f;
            Want(netOk, $"the construct message carries the carrier and the pose ({(copyB == null ? "missing" : $"carried by {copyB.CarriedBy}, {Vector3.Distance(copyB.Pos, b0.Pos) * 100:0.00} cm off")})");
        }

        // The cargo step allocates nothing.
        ConstructCargo.Step(w);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 600; k++)
        {
            w.CargoScanNow = k % 7 == 0;
            ConstructCargo.Step(w);
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Want(alloc == 0, $"the cargo step allocates nothing ({alloc} B over 600 steps)");
        foreach (var p in problems)
            Fail($"cargo volumes: {p}");
    }

    /// <summary>
    /// "Sandbox cargo network": a real host and a separate client over the transport. The client spawns the Cargo Rack
    /// (its two blocks carried): its copies say carried by the rack and are drawn where the host has them; F on its volume
    /// from the client lets them go everywhere; the client's configure request resizes the box (snapped) on both; and the
    /// client's parts menu spawns a welded block in front of its player, its hands empty.
    /// </summary>
    private static void CheckSandboxCargoNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"cargo network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26371;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("cargo network: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        Pump(server, [a], 0.3, ref now, clock);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        var w = server.Constructs;
        if (OpenSpot(w) is not { } spot)
        {
            Fail("cargo network: no open ground on the yard");
            return;
        }
        sp.Teleport(spot + new Vector3(0, 0.05f, 0));
        sp.Yaw = 0;
        Pump(server, [a], 0.3, ref now, clock);
        var before = w.Bodies.Select(b => b.Id).ToHashSet();
        a.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Cargo Rack"));
        Pump(server, [a], 0.8, ref now, clock);
        var rack = w.Bodies.FirstOrDefault(b => !before.Contains(b.Id) && b.CarriedBy == 0 && ConstructCargo.CountVolumes(b) > 0);
        if (rack == null)
        {
            Fail($"cargo network: the Cargo Rack didn't spawn (\"{a.SandboxNotice}\")");
            return;
        }
        var carried = w.Bodies.Where(b => b.CarriedBy == rack.Id).ToList();
        ConstructNet.Blend(a.Constructs, a.SandboxNow + 1, 0.016f); // (the client's draw: blended poses, then the carried bodies with their carrier)
        var copies = carried.Select(b => a.Constructs.Get(b.Id)).ToList();
        var off = carried.Zip(copies).Max(p => p.Second == null ? 99f : Vector3.Distance(p.First.Pos, p.Second.Pos));
        Line($"cargo network: the rack {rack.Id} carries {carried.Count} on the host; on the client {copies.Count(c => c?.CarriedBy == rack.Id)}, drawn {off * 100:0.0} cm from the host's");
        Want(carried.Count == 2 && copies.All(c => c != null && c.CarriedBy == rack.Id) && off < 0.05f, "the client's copies are carried by the rack and drawn where the host has them");

        // F on the volume from the client: everything is let go on both.
        var vi = Enumerable.Range(0, rack.PartCount).First(i => ConstructCargo.IsVolume(rack.DefOf(i)));
        StandBy(sp, PartCentre(rack, vi), 1.8f);
        Pump(server, [a], 0.3, ref now, clock);
        a.SandboxSend(CActKind.Use, body: rack.Id, part: (byte)vi);
        Pump(server, [a], 0.6, ref now, clock);
        var freeHost = carried.All(b => b.CarriedBy == 0);
        var freeClient = carried.All(b => a.Constructs.Get(b.Id) is { CarriedBy: 0 });
        Want(freeHost && freeClient && !ConstructCargo.IsFrozen(rack.Parts[vi]), $"F on the volume from the client lets the cargo go on the host ({freeHost}) and the client ({freeClient}) (\"{a.SandboxNotice}\")");

        // The client resizes the box with the configure tool's request: snapped on the server, the same on its copy.
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
        Pump(server, [a], 0.2, ref now, clock);
        Span<byte> slots = stackalloc byte[6];
        ConstructCargo.BoxSlots(rack.DefOf(vi), slots);
        a.SandboxConfigure(rack.Id, [(byte)vi], [slots[2]], [1.234f]);
        Pump(server, [a], 0.4, ref now, clock);
        var hostZ = ConstructConfig.At(rack.Parts[vi], rack.DefOf(vi), slots[2]);
        var clientZ = a.Constructs.Get(rack.Id) is { } rc ? ConstructConfig.At(rc.Parts[vi], rc.DefOf(vi), slots[2]) : -1;
        Want(MathF.Abs(hostZ - 1.2f) < 1e-4f && MathF.Abs(clientZ - 1.2f) < 1e-4f, $"the client's resize is snapped on the host ({hostZ}) and its copy ({clientZ}) (\"{a.SandboxNotice}\")");

        // The client's parts menu: a welded block spawns in front of its player, never into its hands.
        a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Wrench);
        sp.Teleport(ConstructWorld.Ground(World.Query, spot.X - 6, spot.Z) + new Vector3(0, 0.05f, 0));
        sp.Yaw = 0;
        Pump(server, [a], 0.3, ref now, clock);
        before = w.Bodies.Select(b => b.Id).ToHashSet();
        a.SandboxTakePart(ConstructParts.Need("Frame Block").Id);
        Pump(server, [a], 0.5, ref now, clock);
        var spawned = w.Bodies.FirstOrDefault(b => !before.Contains(b.Id));
        var hands = server.SandboxHands.Hand(sp.Id).Held;
        Want(spawned is { Loose: true } && spawned.DefOf(0).Name == "Frame Block" && hands == ConstructParts.None && a.Constructs.Get(spawned.Id) != null && spawned.Owner == sp.Id,
            $"the client's parts menu spawns a welded block in front of its player, hands empty (\"{a.SandboxNotice}\")");
        foreach (var p in problems)
            Fail($"cargo network: {p}");
    }
}
