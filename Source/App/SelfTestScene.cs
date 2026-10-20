using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Scene graph" (Source/Scene): transform maths (parent/child, TRS composition, dirty propagation,
/// reparenting with and without keeping the world transform), sockets and lookups, the glTF-style builder, gravity frames
/// and vehicle seats; then parity: the scene-graph viewmodel, third-person players, movers, turrets and drones are drawn
/// into a recording renderer next to the old immediate-mode code and compared draw by draw (matrices within 1e-5, same
/// colours, alpha, shading mode and blend state, same order); finally no allocations per frame and the cost of 10k nodes.
/// </summary>
public static partial class SelfTest
{
    /// <summary>What a renderer was asked to draw (with the blend / depth-write state at the time).</summary>
    private struct DrawRec
    {
        public Matrix4x4 M;
        public Vector3 Color;
        public float Alpha;
        public int Mode;
        public Material Material;
        public bool Blend, DepthWrite;
    }

    private sealed class DrawRecorder : IDrawRecorder
    {
        public DrawRec[] Items = new DrawRec[1024];
        public int Count;
        public bool Blend, DepthWrite = true;
        public bool Keep = true; // false: count only (allocation checks)

        public void Draw(in Matrix4x4 model, Vector3 color, float alpha, int mode, Material material)
        {
            if (Keep)
            {
                if (Count == Items.Length)
                    Array.Resize(ref Items, Items.Length * 2);
                Items[Count] = new DrawRec { M = model, Color = color, Alpha = alpha, Mode = mode, Material = material, Blend = Blend, DepthWrite = DepthWrite };
            }
            Count++;
        }

        public void SetBlend(bool on) => Blend = on;
        public void SetDepthWrite(bool on) => DepthWrite = on;

        public void Reset()
        {
            Count = 0;
            Blend = false;
            DepthWrite = true;
        }
    }

    /// <summary>Largest matrix element difference, relative to the element's size once it's over 1 (world coordinates).</summary>
    private static float MatDiff(in Matrix4x4 a, in Matrix4x4 b)
    {
        var worst = 0f;
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 4; c++)
            {
                var x = a[r, c];
                var y = b[r, c];
                var d = MathF.Abs(x - y) / MathF.Max(1f, MathF.Abs(x));
                if (!(d <= worst))
                    worst = float.IsNaN(d) ? float.PositiveInfinity : d;
            }
        return worst;
    }

    /// <summary>Parity totals per group.</summary>
    private sealed class Parity
    {
        public int Cases, Draws, Bad;
        public float Worst;
    }

    /// <summary>
    /// Compares two recorded draw streams. Ordered: item i must match item i. Unordered: every item must match some unused
    /// item of the other stream (opaque boxes drawn in a different order give the same pixels).
    /// </summary>
    private static void CompareDraws(string what, DrawRecorder old, DrawRecorder now, Parity p, bool ordered = true)
    {
        p.Cases++;
        p.Draws += old.Count;
        if (old.Count != now.Count)
        {
            if (p.Bad++ < 6)
                Fail($"scene graph {what}: {now.Count} draws, the old code made {old.Count}");
            return;
        }
        Span<bool> used = old.Count <= 2048 ? stackalloc bool[old.Count] : new bool[old.Count];
        for (var i = 0; i < old.Count; i++)
        {
            ref readonly var a = ref old.Items[i];
            var best = float.PositiveInfinity;
            var bestJ = -1;
            for (var j = ordered ? i : 0; j < (ordered ? i + 1 : now.Count); j++)
            {
                if (!ordered && used[j])
                    continue;
                ref readonly var b = ref now.Items[j];
                if (a.Mode != b.Mode || a.Material != b.Material || a.Blend != b.Blend || a.DepthWrite != b.DepthWrite || (a.Color - b.Color).Length() > 1e-6f
                    || MathF.Abs(a.Alpha - b.Alpha) > 1e-6f)
                    continue;
                var d = MatDiff(a.M, b.M);
                if (d < best)
                    (best, bestJ) = (d, j);
            }
            if (bestJ < 0 || best > 1e-5f)
            {
                if (p.Bad++ < 6)
                {
                    ref readonly var b = ref now.Items[Math.Min(i, now.Count - 1)];
                    Fail($"scene graph {what}: draw {i} differs (matrix diff {best:0.0e0}; old colour {a.Color} alpha {a.Alpha} mode {a.Mode} blend {a.Blend}/{a.DepthWrite}, "
                         + $"new {b.Color} alpha {b.Alpha} mode {b.Mode} blend {b.Blend}/{b.DepthWrite})");
                }
                return;
            }
            used[bestJ] = true;
            p.Worst = MathF.Max(p.Worst, best);
        }
    }

    private static void CheckSceneGraph()
    {
        CheckSceneTransforms();
        CheckSceneLookups();
        CheckSceneFuture();
        CheckSceneParity();
        CheckScenePerformance();
    }

    private static bool Near(Vector3 a, Vector3 b, float tol = 1e-5f) => (a - b).Length() <= tol;

    // ---- Transform maths ------------------------------------------------------------------------

    private static void CheckSceneTransforms()
    {
        var scene = new Scene();
        var root = scene.CreateRoot("root");
        var arm = root.Add("arm");
        var hand = arm.Add("hand");
        var rot = Quaternion.CreateFromYawPitchRoll(0.7f, -0.3f, 0.2f);
        root.SetLocal(new Vector3(1, 2, 3), rot, new Vector3(2, 2, 2));
        arm.LocalPosition = new Vector3(0.5f, 0, -1);
        hand.SetLocal(new Vector3(0, 0.25f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f), Vector3.One);
        var n = scene.UpdateTransforms();
        if (n != 3)
            Fail($"first transform update touched {n} nodes, expected 3");

        // TRS composition is S * R * T (bit-identical to the three products), and World = Local * Parent.World.
        var srt = Matrix4x4.CreateScale(2) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(1, 2, 3);
        if (root.LocalMatrix != srt)
            Fail($"TRS local matrix isn't S * R * T (diff {MatDiff(root.LocalMatrix, srt):0.0e0})");
        var expectHand = hand.LocalMatrix * arm.LocalMatrix * root.LocalMatrix;
        if (MatDiff(hand.World, expectHand) > 1e-6f)
            Fail($"child world isn't local * parent world (diff {MatDiff(hand.World, expectHand):0.0e0})");
        var p = new Vector3(0.1f, 0.2f, 0.3f);
        if (!Near(hand.ToWorld(p), Vector3.Transform(Vector3.Transform(Vector3.Transform(p, hand.LocalMatrix), arm.LocalMatrix), root.LocalMatrix)))
            Fail("a point through the hand's world matrix doesn't match the chain of locals");
        if (!Near(hand.ToLocal(hand.ToWorld(p)), p, 1e-4f))
            Fail("ToLocal doesn't undo ToWorld");

        // Dirty propagation: nothing dirty -> nothing updated; a leaf -> 1 node; the root -> the whole tree, once.
        if (scene.UpdateTransforms() != 0)
            Fail("an update with nothing dirty recomputed nodes");
        hand.LocalPosition = new Vector3(0, 0.3f, 0);
        if ((n = scene.UpdateTransforms()) != 1)
            Fail($"moving a leaf updated {n} nodes, expected 1");
        var before = hand.WorldVersion;
        hand.LocalPosition = new Vector3(0, 0.35f, 0); // child first, then its root: still one pass over 3 nodes
        root.LocalPosition = new Vector3(4, 2, 3);
        if ((n = scene.UpdateTransforms()) != 3 || hand.WorldVersion != before + 1)
            Fail($"child + root dirty updated {n} nodes (hand recomputed {hand.WorldVersion - before} times), expected 3 nodes and once");
        if (!Near(hand.WorldPosition, Vector3.Transform(Vector3.Transform(new Vector3(0, 0.35f, 0), arm.LocalMatrix), root.LocalMatrix)))
            Fail("moving the root didn't move the hand with it");
        var version = arm.WorldVersion;
        arm.Visible = false; // visibility isn't a transform change
        if (scene.UpdateTransforms() != 0 || arm.WorldVersion != version)
            Fail("hiding a node recomputed transforms");
        arm.Visible = true;

        // Matrix locals and switching back to TRS
        var m = Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateTranslation(0, 1, 0);
        arm.SetLocalMatrix(m);
        scene.UpdateTransforms();
        if (MatDiff(arm.World, m * root.World) > 1e-6f)
            Fail("a matrix local doesn't compose with the parent");
        arm.LocalScale = Vector3.One; // back to TRS (position (0.5, 0, -1), identity rotation)
        scene.UpdateTransforms();
        if (!Near(arm.World.Translation, Vector3.Transform(new Vector3(0.5f, 0, -1), root.World)))
            Fail("setting a TRS component after a matrix local didn't switch back to TRS");

        // Reparenting: keep the world transform when asked, keep the local otherwise.
        var other = scene.CreateRoot("other");
        other.SetLocal(new Vector3(-3, 1, 7), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.1f), new Vector3(1.5f));
        scene.UpdateTransforms();
        var handWorld = hand.World;
        hand.SetParent(other, keepWorld: true);
        scene.UpdateTransforms();
        if (MatDiff(hand.World, handWorld) > 1e-5f)
            Fail($"reparenting with keepWorld moved the node (diff {MatDiff(hand.World, handWorld):0.0e0})");
        if (arm.ChildCount != 0 || other.ChildCount != 1 || hand.Parent != other)
            Fail("reparenting didn't move the node between the children lists");
        var local = hand.LocalMatrix;
        hand.SetParent(arm, keepWorld: false);
        scene.UpdateTransforms();
        if (hand.LocalMatrix != local || MatDiff(hand.World, local * arm.World) > 1e-6f)
            Fail("reparenting without keepWorld changed the local transform");
        try
        {
            root.SetParent(hand, keepWorld: false);
            Fail("parenting a node under its own descendant was allowed");
        }
        catch (InvalidOperationException)
        {
        }
        if (scene.Roots.Count != 2)
            Fail($"scene has {scene.Roots.Count} roots, expected 2");
        other.SetParent(root, keepWorld: true); // a root becoming a child leaves the root list
        if (scene.Roots.Count != 1)
            Fail("a root parented under another node is still listed as a root");

        // Flatten: hidden subtrees are skipped, order is depth first in child order.
        var list = new DrawList();
        var a = root.Add("a", Matrix4x4.Identity, Renderable.Box(new Vector3(1, 0, 0)));
        a.Add("a1", Matrix4x4.Identity, Renderable.Box(new Vector3(2, 0, 0)));
        root.Add("b", Matrix4x4.Identity, Renderable.Box(new Vector3(3, 0, 0)));
        scene.UpdateTransforms();
        list.Flatten(root);
        if (list.Count != 3 || list.Item(0).Color.X != 1 || list.Item(1).Color.X != 2 || list.Item(2).Color.X != 3)
            Fail("flatten isn't depth first in child order");
        a.Visible = false;
        list.Clear();
        list.Flatten(root);
        if (list.Count != 1)
            Fail($"a hidden node's subtree was flattened ({list.Count} items)");
        var counter = new CountVisitor();
        root.Visit(ref counter);
        if (counter.Entered != root.CountNodes() || counter.Left != counter.Entered)
            Fail("the visitor didn't visit every node exactly once");
        Line($"  transforms: S*R*T locals, dirty subtrees only, reparenting (world kept within {MatDiff(hand.World, local * arm.World):0.0e0}), flatten order and hiding OK");
    }

    private struct CountVisitor : ISceneVisitor
    {
        public int Entered, Left;

        public bool Enter(SceneNode node)
        {
            Entered++;
            return true;
        }

        public void Leave(SceneNode node) => Left++;
    }

    // ---- Sockets and lookups --------------------------------------------------------------------

    private static void CheckSceneLookups()
    {
        var vm = new ViewModelRig();
        var scene = new Scene();
        scene.AddRoot(vm.Camera);
        var anim = new ViewModelAnimator();
        var def = Weapons.DefaultPrimary;
        anim.Update(BaseInput(def));
        vm.Pose(anim, def, 0, true, 1f, ArmColors.For(Vector3.One));
        scene.UpdateTransforms();
        foreach (var name in (ReadOnlySpan<string>)["muzzle", "grip", "support", "mag_well", "action", "optic_mount", "underbarrel_mount", "stock", "hand_r", "hand_l"])
            if (vm.Camera.FindSocket(name) is not { } s || (s.Flags & NodeFlags.Socket) == 0)
                Fail($"viewmodel has no '{name}' socket");
        if (vm.Camera.FindSocket("gun") != null)
            Fail("FindSocket found a node that isn't a socket");
        var index = vm.Camera.BuildIndex();
        foreach (var name in (ReadOnlySpan<string>)["gun", "body", "magazine", "flash", "shoulder_r", "upper_r", "forearm_l", "hand_l"])
            if (!index.ContainsKey(name) || vm.Camera.Find(name) == null)
                Fail($"viewmodel has no node called '{name}'");
        if (vm.Gun.Root.FindChild("muzzle") is not { } muzzle || muzzle.FindChild("flash") is not { } flash || !flash.Visible)
            Fail("the flash isn't under the muzzle socket");
        else
        {
            // The muzzle socket sits where the old code drew the flash, in camera space.
            var mesh = GunModel.Mesh(def);
            if (mesh != null && !Near(muzzle.WorldPosition, Vector3.Transform(new Vector3(0, mesh.Bore, mesh.MuzzleZ), anim.GunPose)))
                Fail("the muzzle socket isn't at the built gun's muzzle");
        }
        // The hands hold the gun: the hand sockets land on the gun's grip / support sockets (both reachable at the hip).
        var grip = vm.Camera.FindSocket("grip")!;
        var handR = vm.Camera.FindSocket("hand_r")!;
        if (anim.Right.Reached && !Near(handR.WorldPosition, grip.WorldPosition, 1e-4f))
            Fail($"hand_r is {(handR.WorldPosition - grip.WorldPosition).Length() * 1000:0.0} mm off the gun's grip socket");
        var mag = vm.Gun.Root.FindChild("magazine");
        Line($"  sockets: {def.Name} has {vm.Camera.CountNodes()} nodes ({vm.Gun.PartCount} gun boxes{(mag != null ? $", magazine group of {mag.ChildCount}" : "")}); "
             + $"hand_r on the grip socket within {(handR.WorldPosition - grip.WorldPosition).Length() * 1000:0.000} mm");
    }

    // ---- Future: glTF builder, gravity frames, vehicles, mover riders ------------------------------------

    private sealed class FixedPose(MoverPose pose) : IMoverPose
    {
        public MoverPose Pose = pose;
        public MoverPose PoseAt(double tick) => Pose;
    }

    private static void CheckSceneFuture()
    {
        // glTF-shaped records: a child listed before its parent, a socket, a mesh renderable.
        var b = new SceneBuilder();
        var barrel = b.Add("barrel", 2, new Vector3(0, 0, -0.3f), Quaternion.Identity, Vector3.One, Renderable.Mesh(7, Vector3.One));
        var muzzle = b.AddSocket("muzzle", barrel, new Vector3(0, 0, -0.25f), Quaternion.Identity);
        var gun = b.Add("gun", -1, new Vector3(0.2f, -0.2f, -0.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), Vector3.One);
        var scene = new Scene();
        var root = scene.CreateRoot("model");
        b.Build(root);
        scene.UpdateTransforms();
        if (root.ChildCount != 1 || b[gun].ChildCount != 1 || b[barrel].Parent != b[gun] || b[muzzle].Parent != b[barrel])
            Fail("scene builder linked the records wrong");
        var expect = Vector3.Transform(new Vector3(0, 0, -0.55f), Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateTranslation(0.2f, -0.2f, -0.4f));
        if (!Near(root.FindSocket("muzzle")!.WorldPosition, expect))
            Fail("scene builder's muzzle socket isn't where its TRS chain puts it");
        var list = new DrawList();
        list.Flatten(root);
        var rec = new DrawRecorder();
        var g = new Renderer { Recorder = rec };
        list.Submit(g);
        if (list.Count != 1 || list.Item(0).MeshId != 7 || rec.Count != 0 || list.SkippedMeshes != 1)
            Fail("a mesh renderable wasn't flattened as a mesh item (or was drawn as a box without a mesh drawer)");
        var cyc = new SceneBuilder();
        cyc.Add("a", 1, Vector3.Zero, Quaternion.Identity, Vector3.One);
        cyc.Add("b", 0, Vector3.Zero, Quaternion.Identity, Vector3.One);
        try
        {
            cyc.Build(new SceneNode("x"));
            Fail("scene builder accepted a parent cycle");
        }
        catch (InvalidDataException)
        {
        }

        // Gravity frames: "up" comes from the nearest frame, world +Y outside any.
        var zone = scene.CreateRoot("zone", NodeFlags.GravityFrame);
        zone.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2); // +Y turns to -X
        var walker = zone.Add("walker");
        walker.LocalPosition = new Vector3(0, 1, 0);
        var outside = scene.CreateRoot("outside");
        scene.UpdateTransforms();
        if (!Near(walker.GravityUp, -Vector3.UnitX) || !Near(outside.GravityUp, Vector3.UnitY))
            Fail($"gravity up is {walker.GravityUp} in the turned zone (expected -X) and {outside.GravityUp} outside (expected +Y)");

        // Vehicles: seats follow the vehicle's pose; a boarded rider rides along; leaving keeps it where it is.
        var source = new FixedPose(new MoverPose(new Vector3(10, 0, -4), 0.8f));
        var car = new VehicleRig("car", source);
        scene.AddRoot(car.Frame);
        car.AddSeat(new Vector3(-0.4f, 0.6f, 0.2f), Quaternion.Identity);
        car.AddSeat(new Vector3(0.4f, 0.6f, 0.2f), Quaternion.Identity);
        var rider = scene.CreateRoot("rider");
        car.Board(rider, 1);
        car.Pose(0);
        scene.UpdateTransforms();
        var seatWorld = source.Pose.ToWorld(new Vector3(0.4f, 0.6f, 0.2f));
        if (!Near(car.Seat(1).WorldPosition, seatWorld, 1e-4f) || !Near(rider.WorldPosition, seatWorld, 1e-4f))
            Fail($"vehicle seat / rider at {rider.WorldPosition}, expected {seatWorld}");
        if (!Near(rider.GravityUp, Vector3.UnitY))
            Fail("a rider's up isn't the (level) vehicle's up");
        source.Pose = new MoverPose(new Vector3(12, 1, -4), 1.3f);
        car.Pose(1);
        scene.UpdateTransforms();
        seatWorld = source.Pose.ToWorld(new Vector3(0.4f, 0.6f, 0.2f));
        if (!Near(rider.WorldPosition, seatWorld, 1e-4f))
            Fail("the rider didn't move with the vehicle");
        VehicleRig.Leave(rider, null);
        scene.AddRoot(rider);
        scene.UpdateTransforms();
        if (!Near(rider.WorldPosition, seatWorld, 1e-4f))
            Fail("leaving the vehicle moved the rider");

        // Mover riders: a node under the mover's riders node lands where MoverPose.ToWorld says.
        if (Maps.Find("Physics Test") is { Movers.Count: > 0 } map)
        {
            var set = new MoverRigSet(map);
            scene.AddRoot(set.Root);
            var probe = set.Rigs[0].Riders.Add("probe");
            probe.LocalPosition = new Vector3(1.5f, 0.2f, -0.7f);
            foreach (var tick in (ReadOnlySpan<double>)[0, 333.5, 4321.25])
            {
                set.Pose(tick);
                scene.UpdateTransforms();
                var want = map.Movers[0].PoseAt(tick).ToWorld(new Vector3(1.5f, 0.2f, -0.7f));
                if (!Near(probe.WorldPosition, want, 1e-4f))
                    Fail($"mover rider at tick {tick}: {probe.WorldPosition}, MoverPose.ToWorld says {want}");
            }
        }
        Line("  glTF-style builder (out-of-order parents, sockets, mesh ids, cycle check), gravity frames, vehicle seats and mover riders OK");
    }

    // ---- Parity with the old immediate-mode code ----------------------------------------------------

    private static void CheckSceneParity()
    {
        var oldRec = new DrawRecorder();
        var newRec = new DrawRecorder();
        var oldG = new Renderer { Recorder = oldRec };
        var newG = new Renderer { Recorder = newRec };
        var list = new DrawList();
        var scene = new Scene();

        // ---- First person: every weapon (and the attachments that change the model) through hip, aim, reload, flash ----
        var defs = new List<WeaponDef>();
        foreach (var w in Weapons.All)
        {
            defs.Add(w);
            foreach (var opt in Attachments.For(w))
                if (opt.Slot is AttachSlot.Optic or AttachSlot.Muzzle or AttachSlot.Underbarrel or AttachSlot.Magazine or AttachSlot.Barrel or AttachSlot.Stock)
                    defs.Add(Attachments.Apply(w, Attachments.With(0, opt.Slot, opt.Index)));
        }
        var vm = new ViewModelRig();
        scene.AddRoot(vm.Camera);
        var fp = new Parity();
        var colors = ArmColors.For(new Vector3(0.4f, 0.5f, 0.3f));
        foreach (var d in defs)
        {
            var anim = new ViewModelAnimator();
            var i = BaseInput(d);
            // (aim, reload, flash): hip, ADS (cutaway follows), half aimed, mid-reload steps (magazine out, falling, gone), firing
            foreach (var (aim, reload, flash) in (ReadOnlySpan<(float, float, bool)>)[(0f, -1f, false), (1f, -1f, false), (0.7f, -1f, true), (0f, 0.15f, false),
                         (0f, 0.35f, false), (0f, 0.42f, false), (0f, 0.5f, false), (0f, 0.6f, false), (0f, 0.8f, false), (0.2f, -1f, true)])
            {
                i.Now += 0.37;
                i.Aim = aim;
                i.Reload = reload;
                anim.Update(i);
                var cut = GunModel.CutawayFor(aim);
                var time = (float)i.Now;

                oldRec.Reset();
                GunModel.AdsCutaway = cut;
                (GunModel.MagOffset, GunModel.MagHidden) = (anim.MagOffset, anim.MagHidden);
                GunModel.Draw(oldG, d, anim.GunPose, flash, time);
                (GunModel.MagOffset, GunModel.MagHidden) = (Vector3.Zero, false);
                GunModel.AdsCutaway = 0;
                anim.DrawArms(oldG, colors);

                newRec.Reset();
                vm.Pose(anim, d, cut, flash, time, colors);
                scene.UpdateTransforms();
                list.Clear();
                list.Flatten(vm.Camera);
                list.Submit(newG);
                CompareDraws($"viewmodel {d.Name} [{Attachments.Describe(d.Kit)}] aim {aim} reload {reload}", oldRec, newRec, fp);
            }
        }
        Line($"  viewmodel parity: {fp.Cases} poses of {defs.Count} weapon setups, {fp.Draws} draws, worst matrix difference {fp.Worst:0.0e0}, {fp.Bad} mismatching");

        // ---- Third person: weapons, kits, stances, reloading, dead ----
        var tp = new Parity();
        var p = new Player { Id = 7 };
        p.Teleport(new Vector3(12.3f, 0.5f, -7.8f));
        var rig = new PlayerRig(p.Id);
        scene.AddRoot(rig.Root);
        var now = 500.0;
        foreach (var w in Weapons.All)
        {
            p.Primary = p.Weapon = w.Id;
            var kits = Attachments.For(w).Take(3).Select(o => Attachments.With(0, o.Slot, o.Index)).Prepend(0u).ToArray();
            foreach (var kit in kits)
            {
                p.PrimaryKit = kit;
                foreach (var stance in (ReadOnlySpan<Stance>)[Stance.Stand, Stance.Crouch, Stance.Prone])
                {
                    now += 0.21;
                    p.RenderStance = stance;
                    p.RenderYaw = (float)(now * 0.7 % MathF.Tau);
                    p.RenderPitch = (float)Math.Sin(now) * 0.6f;
                    p.RenderPosition += new Vector3(0.05f, 0, 0.03f);
                    p.Health = 30 + (int)(now * 10) % 70;
                    if (stance == Stance.Crouch)
                        ThirdPersonArms.OnReload(p.Id, w.Reload, now - w.Reload * 0.4);
                    oldRec.Reset();
                    PlayerRender.DrawImmediate(oldG, p, now);
                    newRec.Reset();
                    rig.Pose(p, now);
                    scene.UpdateTransforms();
                    list.Clear();
                    list.Flatten(rig.Root);
                    list.Submit(newG);
                    CompareDraws($"third person {w.Name} [{Attachments.Describe(kit)}] {stance}", oldRec, newRec, tp);
                }
            }
        }
        p.Alive = false;
        oldRec.Reset();
        PlayerRender.DrawImmediate(oldG, p, now);
        newRec.Reset();
        rig.Pose(p, now);
        scene.UpdateTransforms();
        list.Clear();
        list.Flatten(rig.Root);
        list.Submit(newG);
        CompareDraws("third person corpse", oldRec, newRec, tp);
        p.Alive = true;
        Line($"  third-person parity: {tp.Cases} poses, {tp.Draws} draws, worst matrix difference {tp.Worst:0.0e0}, {tp.Bad} mismatching");

        // ---- Movers: every mover of every map that has them, at a few ticks ----
        var mv = new Parity();
        foreach (var map in Maps.All.Where(m => m.Movers.Count > 0))
        {
            var set = new MoverRigSet(map);
            scene.AddRoot(set.Root);
            foreach (var tick in (ReadOnlySpan<double>)[0, 17.25, 600, 12345.5])
            {
                oldRec.Reset();
                MoverRender.DrawImmediate(oldG, map, tick);
                newRec.Reset();
                set.Pose(tick);
                scene.UpdateTransforms();
                list.Clear();
                list.Flatten(set.Root);
                list.Submit(newG);
                CompareDraws($"movers on {map.Name} at tick {tick}", oldRec, newRec, mv);
            }
            scene.RemoveRoot(set.Root);
        }
        Line($"  mover parity: {mv.Cases} map poses, {mv.Draws} draws, worst matrix difference {mv.Worst:0.0e0}, {mv.Bad} mismatching");

        // ---- Kill streak entities: turrets and drones, friend and foe, firing, damaged ----
        var st = new Parity();
        var turret = new TurretRig();
        var drone = new DroneRig();
        scene.AddRoot(turret.Base);
        scene.AddRoot(drone.Root);
        for (var k = 0; k < 24; k++)
        {
            var t = 100 + k * 0.173;
            var e = new StreakEntity
            {
                Id = 1, Kind = (k & 1) == 0 ? StreakId.SentryTurret : StreakId.AttackDrone, RenderPos = new Vector3(5 + k * 0.4f, 0.2f + (k & 1) * 6f, -9 + k * 0.3f),
                BaseYaw = k * 0.41f, RenderYaw = k * 0.73f, RenderPitch = -0.4f + k * 0.03f, Target = (k & 2) != 0 ? (byte)3 : byte.MaxValue, Firing = (k % 3) != 0,
            };
            e.Health = k % 4 == 0 ? e.MaxHealth : e.MaxHealth * (k % 4) / 5;
            var stripe = new Vector3(0.2f + k * 0.01f, 0.5f, 0.8f);
            var friendly = (k & 4) != 0;
            oldRec.Reset();
            newRec.Reset();
            SceneNode root;
            if (e.Kind == StreakId.SentryTurret)
            {
                StreakRender.DrawTurret(oldG, e, stripe, friendly, t);
                turret.Pose(e, stripe, friendly, t);
                root = turret.Base;
            }
            else
            {
                StreakRender.DrawDrone(oldG, e, stripe, friendly, t);
                drone.Pose(e, stripe, friendly, t);
                root = drone.Root;
            }
            scene.UpdateTransforms();
            list.Clear();
            list.Flatten(root);
            list.Submit(newG);
            // The drone's status light and muzzle flash swap places in the draw order (both opaque, so the pixels match).
            CompareDraws($"{e.Kind} #{k}", oldRec, newRec, st, ordered: e.Kind == StreakId.SentryTurret);
        }
        Line($"  turret/drone parity: {st.Cases} poses, {st.Draws} draws, worst matrix difference {st.Worst:0.0e0}, {st.Bad} mismatching");
        Line($"[scene] parity worst matrix diff: viewmodel {fp.Worst:0.0e0} ({fp.Draws} draws), third person {tp.Worst:0.0e0} ({tp.Draws}), movers {mv.Worst:0.0e0} ({mv.Draws}), "
             + $"turrets/drones {st.Worst:0.0e0} ({st.Draws}); mismatches {fp.Bad + tp.Bad + mv.Bad + st.Bad}");
    }

    // ---- Allocations and cost ------------------------------------------------------------------------

    private static void CheckScenePerformance()
    {
        // No allocations per frame: a warmed-up viewmodel + 8 players + movers + a turret, posed, updated and flattened.
        var scene = new Scene();
        var list = new DrawList();
        var vm = new ViewModelRig();
        scene.AddRoot(vm.Camera);
        var anim = new ViewModelAnimator();
        var def = Weapons.DefaultPrimary;
        var input = BaseInput(def);
        var players = new Player[8];
        var rigs = new PlayerRig[8];
        for (var k = 0; k < players.Length; k++)
        {
            players[k] = new Player { Id = (byte)(20 + k) };
            players[k].Primary = players[k].Weapon = Weapons.All[k % Weapons.All.Length].Id;
            players[k].Teleport(new Vector3(k * 3f, 0, -10));
            rigs[k] = new PlayerRig(players[k].Id);
            scene.AddRoot(rigs[k].Root);
        }
        var map = Maps.Find("Physics Test");
        var movers = map != null ? new MoverRigSet(map) : null;
        if (movers != null)
            scene.AddRoot(movers.Root);
        var turret = new TurretRig();
        scene.AddRoot(turret.Base);
        var ent = new StreakEntity { Id = 2, Kind = StreakId.SentryTurret, RenderPos = new Vector3(3, 0, 3), Health = 50 };
        var colors = ArmColors.For(Vector3.One * 0.5f);

        var draws = 0;
        var parts = new long[6]; // bytes allocated by: animation, viewmodel pose, players, movers + turret, update, flatten
        long Mark(int part, long since)
        {
            var now = GC.GetAllocatedBytesForCurrentThread();
            parts[part] += now - since;
            return now;
        }

        void Frame(int f)
        {
            var t = GC.GetAllocatedBytesForCurrentThread();
            input.Now += 1.0 / 240;
            input.Aim = (f / 60) % 2 == 0 ? 0 : 1;
            input.Reload = f % 240 < 120 ? f % 240 / 120f : -1;
            anim.Update(input);
            t = Mark(0, t);
            vm.Pose(anim, def, GunModel.CutawayFor(input.Aim), (f & 3) == 0, (float)input.Now, colors);
            t = Mark(1, t);
            for (var k = 0; k < players.Length; k++)
            {
                var pl = players[k];
                pl.RenderPosition += new Vector3(0.01f, 0, 0);
                pl.RenderYaw = f * 0.01f + k;
                pl.RenderStance = (Stance)((f / 50 + k) % 3);
                rigs[k].Pose(pl, input.Now);
            }
            t = Mark(2, t);
            movers?.Pose(f * 0.5);
            ent.RenderYaw = f * 0.02f;
            turret.Pose(ent, Vector3.One, true, input.Now);
            t = Mark(3, t);
            list.Clear(); // as the game does it: one update + flatten walk per rig, then drain the queue
            list.UpdateAndFlatten(vm.Camera);
            for (var k = 0; k < rigs.Length; k++)
                list.UpdateAndFlatten(rigs[k].Root);
            if (movers != null)
                list.UpdateAndFlatten(movers.Root);
            list.UpdateAndFlatten(turret.Base);
            draws = list.Count;
            t = Mark(5, t);
            scene.UpdateTransforms();
            Mark(4, t);
        }

        for (var f = 0; f < 1200; f++)
            Frame(f); // warm up: every stance, reload phase and cutaway seen (twice, so the JIT has settled)
        Array.Clear(parts);
        var sw = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        const int frames = 600;
        for (var f = 0; f < frames; f++)
            Frame(f);
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 0)
            Fail($"scene graph frames allocate: {allocated} bytes over {frames} frames (animation {parts[0]}, viewmodel pose {parts[1]}, players {parts[2]}, "
                 + $"movers + turret {parts[3]}, update {parts[4]}, flatten {parts[5]})");
        var gameUs = sw.Elapsed.TotalMilliseconds * 1000 / frames;
        Line($"  game-like frame (viewmodel, 8 players, {movers?.Rigs.Length ?? 0} movers, a turret: {scene.CountNodes()} nodes, {draws} draws): "
             + $"{gameUs:0} us per frame for animation + pose + update + flatten, {allocated} bytes allocated over {frames} frames");

        // 10k nodes: 100 moving roots x (9 groups x 10 boxes + 9 + 1), every node recomputed and flattened each frame.
        var big = new Scene();
        var roots = new SceneNode[100];
        for (var r = 0; r < roots.Length; r++)
        {
            roots[r] = big.CreateRoot("root");
            for (var gi = 0; gi < 9; gi++)
            {
                var grp = roots[r].Add("group");
                grp.SetLocal(new Vector3(gi * 0.3f, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, gi * 0.2f), Vector3.One);
                for (var b = 0; b < 10; b++)
                    grp.Add("box", Matrix4x4.CreateScale(0.1f) * Matrix4x4.CreateTranslation(0, b * 0.1f, 0), Renderable.Box(new Vector3(b / 10f)));
            }
        }
        var nodes = big.CountNodes();
        var bigList = new DrawList();
        var updated = 0;
        long updTicks = 0, flatTicks = 0;
        void BigFrame(int f, bool move, bool fused)
        {
            var t0 = Stopwatch.GetTimestamp();
            if (move)
                for (var r = 0; r < roots.Length; r++)
                    roots[r].SetLocal(new Vector3(r, 0, f * 0.01f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, f * 0.01f + r), Vector3.One);
            bigList.Clear();
            if (fused)
            {
                updated = 0;
                for (var r = 0; r < roots.Length; r++)
                    updated += bigList.UpdateAndFlatten(roots[r]);
                big.UpdateTransforms(); // drains the (already clean) queue
                updTicks += Stopwatch.GetTimestamp() - t0;
                return;
            }
            updated = big.UpdateTransforms();
            var t1 = Stopwatch.GetTimestamp();
            for (var r = 0; r < roots.Length; r++)
                bigList.Flatten(roots[r]);
            var t2 = Stopwatch.GetTimestamp();
            updTicks += t1 - t0;
            flatTicks += t2 - t1;
        }

        for (var f = 0; f < 100; f++)
        {
            BigFrame(f, true, false);
            BigFrame(f, true, true);
        }
        var samples = new double[15];
        (double Total, double Update, double Flatten) Median(bool move, bool fused)
        {
            updTicks = flatTicks = 0;
            for (var s = 0; s < samples.Length; s++)
            {
                var t0 = Stopwatch.GetTimestamp();
                for (var f = 0; f < 20; f++)
                    BigFrame(s * 20 + f, move, fused);
                samples[s] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / 20;
            }
            Array.Sort(samples);
            var n = samples.Length * 20.0;
            return (samples[samples.Length / 2], updTicks * 1000.0 / Stopwatch.Frequency / n, flatTicks * 1000.0 / Stopwatch.Frequency / n);
        }

        // TEMP probe: the same walk over 10 roots only (1k nodes, cache resident) to see if 10k is memory bound
        {
            var t0 = Stopwatch.GetTimestamp();
            for (var f = 0; f < 2000; f++)
            {
                for (var r = 0; r < 10; r++)
                    roots[r].SetLocal(new Vector3(r, 0, f * 0.01f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, f * 0.01f + r), Vector3.One);
                bigList.Clear();
                for (var r = 0; r < 10; r++)
                    bigList.UpdateAndFlatten(roots[r]);
                big.UpdateTransforms();
            }
            var ns = (Stopwatch.GetTimestamp() - t0) * 1e9 / Stopwatch.Frequency / 2000 / 1000;
            Line($"[scene] probe: 1k-node walk {ns:0.0} ns per node");
        }
        var bigBefore = GC.GetAllocatedBytesForCurrentThread();
        var separate = Median(true, false);
        var sepUpdated = updated;
        var moving = Median(true, true);
        var movingUpdated = updated;
        var still = Median(false, true);
        var bigAlloc = GC.GetAllocatedBytesForCurrentThread() - bigBefore;
        if (movingUpdated != nodes || sepUpdated != nodes)
            Fail($"10k benchmark: {movingUpdated} (fused) / {sepUpdated} (separate) of {nodes} nodes updated with every root moving");
        if (updated != 0)
            Fail($"10k benchmark: {updated} nodes updated with nothing moving");
        if (bigAlloc > 0)
            Fail($"10k benchmark allocates {bigAlloc} bytes");
        if (moving.Total > 0.3)
            Fail($"10k nodes update + flatten takes {moving.Total:0.000} ms per frame (budget 0.3 ms)");
        Line($"  {nodes} nodes ({bigList.Count} boxes): update + flatten {moving.Total * 1000:0} us per frame with everything moving (one walk; as two passes "
             + $"{separate.Total * 1000:0} us = update {separate.Update * 1000:0} + flatten {separate.Flatten * 1000:0}), {still.Total * 1000:0} us when nothing moved");
        Line($"[scene] perf: {nodes} nodes update+flatten {moving.Total * 1000:0} us all moving (two passes {separate.Total * 1000:0} = {separate.Update * 1000:0} + {separate.Flatten * 1000:0}), "
             + $"{still.Total * 1000:0} us static; game-like frame {gameUs:0} us ({scene.CountNodes()} nodes, {draws} draws); allocations {allocated} + {bigAlloc} B");
    }
}
