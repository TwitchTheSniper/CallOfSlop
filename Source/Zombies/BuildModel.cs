using System.Numerics;

namespace CallOfSlop;

/// <summary>A built weapon's model: the scene graph tree (parts at their sockets) and the flattened gun body the renderer draws.</summary>
public sealed class BuiltModel
{
    public required SceneNode Root;
    public required GunMesh Mesh;

    /// <summary>World (gun space) position of every barrel's muzzle, in <see cref="BuildStats.AllLeaves"/> order ("muzzle_1"...).</summary>
    public required Vector3[] Muzzles;

    /// <summary>Gun-space hand anchors (also on <see cref="Mesh"/>): trigger hand, off hand, magazine.</summary>
    public Vector3 Grip, Support, MagWell;

    /// <summary>The scene node of each build node (for the workbench: highlight a part, find its sockets).</summary>
    public required Dictionary<BuildNode, SceneNode> Nodes;
}

/// <summary>
/// Builds a weapon's 3D model from its parts (docs/zombies/WEAPON-BUILDER.md, "Model building"): each part's boxes sit
/// in its own space, placed at the socket it plugs into, as a <see cref="SceneNode"/> tree (grip at the root, sockets
/// as socket nodes: receiver_N, optic_mount, mag_well, support, stock, muzzle_mount and one muzzle_N per barrel). The
/// tree is then flattened into a <see cref="GunMesh"/>, so the existing gun drawing, scene graph rig, IK arms and
/// sight lens all work on built weapons unchanged. The whole model is moved so the trigger hand sits where it does on
/// the standard guns.
/// </summary>
public static class BuildModel
{
    /// <summary>Where the trigger hand sits in gun space on every built weapon (close to the standard guns' grip).</summary>
    public static readonly Vector3 GripAnchor = new(0, -0.07f, 0.04f);

    /// <summary>Scene-graph name of a socket of type <paramref name="type"/> (the conventions GunRig uses).</summary>
    public static string SceneName(PartSocket s) => s.Type switch
    {
        "optic" => "optic_mount",
        "feed" => "mag_well",
        "foregrip" => "support",
        "stock" => "stock",
        "muzzle" => "muzzle_mount",
        _ => s.Name,
    };

    /// <summary>
    /// Builds the model of any build with a root (complete or not). <paramref name="accent"/> colours "accent" boxes.
    /// <paramref name="tints"/> (the workbench) recolours some parts' own boxes: rgb is blended in, w is their opacity
    /// (missing parts as a red outline-like ghost, a part previewed in a socket as a bright highlight).
    /// </summary>
    public static BuiltModel Build(WeaponBuild b, Vector3 accent, IReadOnlyDictionary<BuildNode, Vector4>? tints = null)
    {
        var root = new SceneNode("weapon");
        var map = new Dictionary<BuildNode, SceneNode>();
        var hasOptic = b.Root != null && b.Root.Part.Frame?.Ads != false && b.Nodes.Any(n => n.Part.Category == PartCategory.Optic);
        // A grip's iron sights stand on its receiver: on an empty grip (or under an optic) they would hang in the air.
        var irons = !hasOptic && b.Receivers.Count > 0;
        if (b.Root != null)
            AddPart(root, b.Root, accent, irons, map, tints);
        root.UpdateWorld();

        // Muzzles: one socket per barrel muzzle, after any muzzle device on that barrel.
        var leaves = BuildStats.AllLeaves(b);
        var muzzles = new Vector3[leaves.Count];
        for (var i = 0; i < leaves.Count; i++)
        {
            var leaf = leaves[i].Leaf;
            var bn = map[leaf.Barrel];
            var world = bn.ToWorld(leaf.Barrel.Part.Muzzles[leaf.Muzzle] * BuildRules.BarrelScale(leaf.Barrel)); // the receiver sized the barrel
            foreach (var (s, dev) in leaf.Barrel.Children())
                if (s.Type == "muzzle" && dev.Part.Muzzles.Length > 0 && map.TryGetValue(dev, out var dn))
                    world += dn.ToWorld(dev.Part.Muzzles[0]) - dn.WorldPosition;
            bn.AddSocket($"muzzle_{i + 1}", bn.ToLocal(world));
        }

        // Move everything so the trigger hand sits at the standard grip anchor.
        var hand = b.Root?.Part.Hand ?? Vector3.Zero;
        root.LocalPosition = GripAnchor - hand;
        root.UpdateWorld();
        for (var i = 0; i < leaves.Count; i++)
            muzzles[i] = root.Find($"muzzle_{i + 1}")!.WorldPosition;

        var mesh = Flatten(root);
        if (root.ChildCount > 0)
        {
            // Flatten walks depth first, so the root part's own boxes come first (a push cart's frame: the rest is its gun).
            var rootPart = root.Child(0);
            for (var i = 0; i < rootPart.ChildCount; i++)
                if (rootPart.Child(i) is { Renderable.Kind: RenderKind.Box } n && (n.Flags & NodeFlags.Socket) == 0)
                    mesh.RootParts++;
        }
        var grip = GripAnchor;
        var lead = b.Receivers.FirstOrDefault();
        Vector3 support;
        if (b.Nodes.FirstOrDefault(n => n.Part.Category == PartCategory.Grip && n.Part.Mount == "foregrip") is { } fg && map.TryGetValue(fg, out var fgn))
            support = fgn.ToWorld(fg.Part.Hand ?? Vector3.Zero);
        else if (b.Root?.Part.Frame?.Support is { } sp)
            support = sp + root.LocalPosition;
        else if (lead != null && BuildStats.Leaves(lead).FirstOrDefault() is { Barrel: not null } l && map.TryGetValue(l.Barrel, out var lbn))
            support = lbn.ToWorld((l.Barrel.Part.Hand ?? new Vector3(0, -0.03f, -0.1f)) * BuildRules.BarrelScale(l.Barrel));
        else
            support = grip + new Vector3(0, 0.03f, -0.2f);
        var magWell = grip + new Vector3(0, -0.02f, -0.1f);
        if (lead?.Walk().FirstOrDefault(n => n.Part.Category == PartCategory.Feed) is { } feed && map.TryGetValue(feed, out var feedNode))
            magWell = feedNode.ToWorld(feed.Part.Hand ?? new Vector3(0, -0.05f, 0));

        mesh.Grip = grip;
        mesh.Support = support;
        mesh.MagWell = magWell;
        mesh.Underbarrel = support;
        mesh.GripAngle = (b.Root?.Part.GripAngleDeg ?? 0) * MathF.PI / 180f;
        mesh.Muzzles = muzzles;
        mesh.Bore = muzzles.Length > 0 ? muzzles[0].Y : 0f;
        mesh.MuzzleZ = muzzles.Length > 0 ? muzzles[0].Z : mesh.Front;
        if (lead != null && map.TryGetValue(lead, out var leadNode))
        {
            var (mn, mx) = Bounds(leadNode, false);
            if (mn.X <= mx.X)
                (mesh.RecvLen, mesh.RecvH, mesh.RecvW) = (mx.Z - mn.Z, mx.Y - mn.Y, mx.X - mn.X);
        }
        if (mesh.RecvLen <= 0)
            (mesh.RecvLen, mesh.RecvH, mesh.RecvW) = (0.2f, 0.08f, 0.06f);
        var opticMount = root.Find("optic_mount");
        if (opticMount != null)
        {
            mesh.Mount = opticMount.WorldPosition.Y;
            mesh.OpticZ = opticMount.WorldPosition.Z;
        }
        else
        {
            mesh.Mount = mesh.Top;
            mesh.OpticZ = (mesh.Front + mesh.Back) * 0.5f;
        }
        mesh.Iron = (b.Root?.Part.Frame?.IronHeight ?? 0.07f) + root.LocalPosition.Y;
        mesh.RailWidth = MathF.Max(0.02f, MathF.Min(mesh.RecvW, 0.04f));
        mesh.RailFront = mesh.OpticZ - 0.06f;
        mesh.RailBack = mesh.OpticZ + 0.06f;
        mesh.StockPoint = root.Find("stock")?.WorldPosition ?? new Vector3(0, 0, mesh.Back);
        return new BuiltModel { Root = root, Mesh = mesh, Muzzles = muzzles, Grip = grip, Support = support, MagWell = magWell, Nodes = map };
    }

    private static void AddPart(SceneNode parent, BuildNode node, Vector3 accent, bool irons, Dictionary<BuildNode, SceneNode> map,
        IReadOnlyDictionary<BuildNode, Vector4>? tints)
    {
        var p = node.Part;
        var pn = parent.Add(new SceneNode(p.Name));
        map[node] = pn;
        var flags = p.Category switch
        {
            PartCategory.Feed => GunPartFlags.Mag,
            PartCategory.Muzzle => GunPartFlags.Muzzle,
            PartCategory.Utility => GunPartFlags.Underbarrel,
            _ => GunPartFlags.None,
        };
        Vector4? tint = tints != null && tints.TryGetValue(node, out var tv) ? tv : null;
        // A barrel takes the size of the receiver it hangs under (pistol: thinner and shorter, heavy: fatter and longer):
        // its boxes and socket points scale in its own space; what plugs into it (a muzzle device) keeps its own size.
        var scale = BuildRules.BarrelScale(node);
        var scaled = scale != Vector3.One;
        var post = scaled ? Matrix4x4.CreateScale(scale) : Matrix4x4.Identity;
        for (var i = 0; i < p.Boxes.Length; i++)
        {
            var bx = p.Boxes[i];
            if (!Shown(bx, node, irons))
                continue;
            var color = bx.Color.Resolve(accent);
            var mode = bx.Tag == "glow" ? 2 : 0;
            var alpha = bx.Alpha;
            if (tint is { } t)
            {
                color = Vector3.Lerp(color, new Vector3(t.X, t.Y, t.Z), 0.7f);
                alpha = MathF.Min(alpha, t.W);
                mode = 2; // flat and bright: reads as a marker, not as a real part
            }
            var f = flags | (alpha < 0.999f ? GunPartFlags.Blend : 0) | (bx.Tag == "irons" ? GunPartFlags.Top : 0);
            var m = Matrix4x4.CreateScale(bx.Size) * Matrix4x4.CreateFromQuaternion(bx.Rotation) * Matrix4x4.CreateTranslation(bx.Pos) * post;
            var n = pn.Add($"box{i}", m, Renderable.Box(color, alpha, mode));
            n.Tag = f;
            if (bx.Shape == "rod")
            {
                // An octagonal rod: a second box turned 45 degrees about the rod's own axis (Z).
                var m2 = Matrix4x4.CreateScale(bx.Size) * Matrix4x4.CreateRotationZ(MathF.PI / 4) * Matrix4x4.CreateFromQuaternion(bx.Rotation)
                         * Matrix4x4.CreateTranslation(bx.Pos) * post;
                pn.Add($"box{i}r", m2, Renderable.Box(color * 0.93f, alpha, mode)).Tag = f;
            }
        }
        foreach (var s in p.Sockets)
        {
            var sn = pn.AddSocket(SceneName(s), scaled ? s.Pos * scale : s.Pos, s.Rotation);
            if (node.Child(s.Name) is { } child)
                AddPart(sn, child, accent, irons, map, tints);
        }
    }

    /// <summary>
    /// Whether box <paramref name="bx"/> of <paramref name="node"/>'s part is drawn: "irons" only when
    /// <paramref name="irons"/> (a receiver fitted, no optic), "with:SOCKET" only while that socket holds a part.
    /// </summary>
    public static bool Shown(PartBox bx, BuildNode node, bool irons) =>
        bx.Tag == "irons" ? irons : !bx.Tag.StartsWith("with:", StringComparison.Ordinal) || node.Child(bx.Tag[5..]) != null;

    /// <summary>
    /// The boxes of <paramref name="node"/> and everything plugged into it, as a range of <see cref="GunMesh.Parts"/>
    /// (<see cref="Flatten"/> walks depth first, so a part's subtree is one run of boxes). (0, 0) if it isn't on the model.
    /// </summary>
    public static (int From, int To) NodeRange(BuiltModel m, BuildNode node)
    {
        if (!m.Nodes.TryGetValue(node, out var target))
            return (0, 0);
        var count = 0;
        var from = -1;
        var to = -1;
        void Visit(SceneNode n)
        {
            if (n == target)
                from = count;
            if (n.Renderable.Kind == RenderKind.Box && (n.Flags & NodeFlags.Socket) == 0)
                count++;
            for (var i = 0; i < n.ChildCount && to < 0; i++)
                Visit(n.Child(i));
            if (n == target)
                to = count;
        }
        Visit(m.Root);
        return from >= 0 && to >= from ? (from, to) : (0, 0);
    }

    /// <summary>Every box under <paramref name="root"/> (world matrices) as a gun body.</summary>
    public static GunMesh Flatten(SceneNode root)
    {
        var parts = new List<GunPart>();
        var mats = new List<Matrix4x4>();
        var stack = new Stack<SceneNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            for (var i = n.ChildCount - 1; i >= 0; i--)
                stack.Push(n.Child(i));
            if (n.Renderable.Kind != RenderKind.Box || (n.Flags & NodeFlags.Socket) != 0)
                continue;
            var w = n.World;
            mats.Add(w);
            parts.Add(new GunPart
            {
                Sx = new Vector3(w.M11, w.M12, w.M13).Length(), Sy = new Vector3(w.M21, w.M22, w.M23).Length(), Sz = new Vector3(w.M31, w.M32, w.M33).Length(),
                X = w.M41, Y = w.M42, Z = w.M43, Color = n.Renderable.Color, Alpha = n.Renderable.Alpha, Mode = n.Renderable.Mode,
                Flags = n.Tag is GunPartFlags f ? f : GunPartFlags.None,
            });
        }
        var mesh = new GunMesh { Parts = parts.ToArray(), Matrices = mats.ToArray() };
        var (mn, mx) = Bounds(root, true);
        if (mn.X > mx.X)
            (mn, mx) = (new Vector3(-0.03f, -0.05f, -0.2f), new Vector3(0.03f, 0.05f, 0.1f));
        mesh.Top = mx.Y;
        mesh.Front = mn.Z;
        mesh.Back = mx.Z;
        return mesh;
    }

    /// <summary>World-space bounds of the boxes under <paramref name="n"/> (min > max when there are none).</summary>
    public static (Vector3 Min, Vector3 Max) Bounds(SceneNode n, bool includeChildren)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        void Visit(SceneNode x, int depth)
        {
            if (x.Renderable.Kind == RenderKind.Box)
                for (var i = 0; i < 8; i++)
                {
                    var p = Vector3.Transform(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f), x.World);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            for (var i = 0; i < x.ChildCount; i++)
            {
                var c = x.Child(i);
                // A part's own boxes are its direct children; its sockets lead to other parts.
                if (includeChildren || (c.Flags & NodeFlags.Socket) == 0)
                    Visit(c, depth + 1);
            }
        }
        Visit(n, 0);
        return (min, max);
    }
}

/// <summary>A build put together: its problems, its weapon stats (null until complete and legal) and its model.</summary>
public sealed class AssembledWeapon
{
    public required WeaponBuild Build;
    public required List<BuildProblem> Problems;

    /// <summary>The finished weapon (stats, barrels at their muzzles, model), or null if the build isn't complete and legal.</summary>
    public WeaponDef? Def;

    /// <summary>The model (built even for incomplete builds, for the workbench preview).</summary>
    public BuiltModel? Model;

    /// <summary>Something to draw: <see cref="Def"/>, or a stand-in carrying the partial model.</summary>
    public WeaponDef? PreviewDef;

    /// <summary>A complete melee build's numbers (melee builds have no <see cref="Def"/>).</summary>
    public MeleeDef? Melee;

    public bool Ok => Def != null || Melee != null;
}

/// <summary>Puts builds together: rules, stats, model, muzzle positions (docs/zombies/WEAPON-BUILDER.md).</summary>
public static class WeaponBuilder
{
    /// <summary>Checks, computes and models <paramref name="b"/> (<paramref name="tints"/>: see <see cref="BuildModel.Build"/>).</summary>
    public static AssembledWeapon Assemble(WeaponBuild b, IReadOnlyDictionary<BuildNode, Vector4>? tints = null)
    {
        var def = BuildStats.Compute(b, out var problems);
        var a = new AssembledWeapon { Build = b, Problems = problems, Def = def };
        if (def != null)
            def.BuildText = b.ToText(); // so a part hot reload can rebuild this weapon in place (Source/Zombies/PartReload.cs)
        if (b.Root == null)
            return a;
        var accent = def?.Accent ?? PartDef.RarityColor(b.Nodes.Max(n => n.Part.Rarity));
        var model = BuildModel.Build(b, accent, tints);
        a.Model = model;
        var shown = def ?? Stub(b, accent);
        shown.BuiltMesh = model.Mesh;
        shown.Length = Math.Clamp(-model.Mesh.Front, 0.1f, 1.5f);
        if (def?.MuzzlePoints is { } pts)
            for (var i = 0; i < pts.Length && i < model.Muzzles.Length; i++)
                pts[i] = pts[i] with { Muzzle = model.Muzzles[i] };
        a.PreviewDef = shown;
        if (b.IsMelee && problems.Count == 0)
        {
            a.Melee = MeleeStats.Compute(b, shown);
            a.Melee.BuildText = b.ToText();
        }
        return a;
    }

    /// <summary>A drawable stand-in for an incomplete build.</summary>
    private static WeaponDef Stub(WeaponBuild b, Vector3 accent)
    {
        var w = new WeaponDef
        {
            Name = WeaponBuild.CleanName(b.Name),
            Class = b.IsMelee ? WeaponClass.Pistol : b.Receivers.FirstOrDefault()?.Part.Receiver?.Class ?? WeaponClass.AssaultRifle, // melee: the one-handed swing
            Damage = 1, RangeM = 1, Rpm = 60, Mag = 1, Reload = 1, HipSpreadDeg = 1, Accent = accent, Order = 10000,
        };
        if (b.Nodes.FirstOrDefault(n => n.Part.Category == PartCategory.Optic) is { } o && b.Frame?.Ads != false)
        {
            w.Optic = o.Part.Optic;
            w.AdsZoom = PartFile.ZoomOf(o.Part);
        }
        return w;
    }
}
