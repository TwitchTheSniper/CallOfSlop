using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The parts a workbench can build with (docs/zombies/WEAPON-BUILDER.md, "The workbench"). The sandbox has every part
/// (unlimited); a Zombies run has the loose parts you own plus the parts on the slot's current build (the server gives
/// those back when the build is replaced). "Free" is what is left after the build being edited takes its share.
/// </summary>
public sealed class BenchStock
{
    private readonly Dictionary<PartDef, int>? _pool;

    private BenchStock(Dictionary<PartDef, int>? pool) => _pool = pool;

    /// <summary>Every part, without limit (the main menu's sandbox).</summary>
    public static readonly BenchStock Unlimited = new(null);

    public bool IsUnlimited => _pool == null;

    /// <summary>A stock of exactly these parts.</summary>
    public static BenchStock Of(IEnumerable<(PartDef Part, int Count)> parts)
    {
        var d = new Dictionary<PartDef, int>(ReferenceEqualityComparer.Instance);
        foreach (var (p, n) in parts)
            if (n > 0)
                d[p] = d.GetValueOrDefault(p) + n;
        return new BenchStock(d);
    }

    /// <summary>A run's stock: loose parts by name plus every part on <paramref name="slotBuild"/> (the slot's current build).</summary>
    public static BenchStock FromRun(IEnumerable<KeyValuePair<string, int>> loose, WeaponBuild? slotBuild, Func<string, PartDef?> find)
    {
        var d = new Dictionary<PartDef, int>(ReferenceEqualityComparer.Instance);
        foreach (var (name, n) in loose)
            if (n > 0 && find(name) is { } p)
                d[p] = d.GetValueOrDefault(p) + n;
        if (slotBuild != null)
            foreach (var node in slotBuild.Nodes)
                d[node.Part] = d.GetValueOrDefault(node.Part) + 1;
        return new BenchStock(d);
    }

    /// <summary>
    /// The sandbox's "Zombies rules" practice stock: what an early run might own (two of every common part, one of every
    /// uncommon one, none rarer), so the missing-parts system can be tried without a run.
    /// </summary>
    public static BenchStock Practice(IEnumerable<PartDef> all) =>
        Of(all.Where(p => p.Rarity <= Rarity.Uncommon).Select(p => (p, p.Rarity == Rarity.Common ? 2 : 1)));

    /// <summary>How many of <paramref name="p"/> you own for this build (int.MaxValue when unlimited).</summary>
    public int Owned(PartDef p) => _pool == null ? int.MaxValue : _pool.GetValueOrDefault(p);

    /// <summary>How many of <paramref name="p"/> are left after <paramref name="editing"/> takes its share.</summary>
    public int Free(PartDef p, WeaponBuild editing) => _pool == null ? int.MaxValue : _pool.GetValueOrDefault(p) - Count(editing, p);

    /// <summary>The parts this stock has (the sandbox: <paramref name="all"/>), in <paramref name="all"/>'s order.</summary>
    public IEnumerable<PartDef> Parts(IEnumerable<PartDef> all) => _pool == null ? all : all.Where(p => _pool.GetValueOrDefault(p) > 0);

    /// <summary>A number that changes whenever the stock does (cheap: no allocation).</summary>
    public int Fingerprint
    {
        get
        {
            if (_pool == null)
                return -1;
            var h = _pool.Count;
            foreach (var (p, n) in _pool)
                h = unchecked(h * 31 + p.Name.Length * 7 + n);
            return h;
        }
    }

    public static int Count(WeaponBuild b, PartDef p)
    {
        var n = 0;
        foreach (var node in b.Nodes)
            if (node.Part == p)
                n++;
        return n;
    }
}

/// <summary>One kind of part a build needs more of than you own.</summary>
public readonly record struct MissingPart(PartDef Part, int Count);

/// <summary>What a build is missing against a <see cref="BenchStock"/>: by part, and which nodes are the missing ones.</summary>
public sealed class MissingReport
{
    public readonly List<MissingPart> Parts = new();

    /// <summary>The nodes that aren't covered by the stock (drawn as red ghosts, listed in red).</summary>
    public readonly HashSet<BuildNode> Nodes = new(ReferenceEqualityComparer.Instance);

    public int Total;

    public static readonly MissingReport None = new();

    /// <summary>"Missing: 1x Long Barrel, 1x Drum Mag" ("" when nothing is missing).</summary>
    public string Text()
    {
        if (Total == 0)
            return "";
        var sb = new StringBuilder("Missing: ");
        for (var i = 0; i < Parts.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(Parts[i].Count).Append("x ").Append(Parts[i].Part.Name);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Compares <paramref name="b"/> with <paramref name="stock"/>. Per part, the instances past what you own are the
    /// missing ones: those in <paramref name="prefer"/> first (a copied receiver assembly), then the last ones in the
    /// build's order.
    /// </summary>
    public static MissingReport Of(WeaponBuild b, BenchStock stock, IReadOnlySet<BuildNode>? prefer = null)
    {
        if (stock.IsUnlimited || b.Root == null)
            return None;
        var r = new MissingReport();
        var byPart = new Dictionary<PartDef, List<BuildNode>>(ReferenceEqualityComparer.Instance);
        var order = new List<PartDef>();
        foreach (var n in b.Nodes)
        {
            if (!byPart.TryGetValue(n.Part, out var list))
            {
                byPart[n.Part] = list = new List<BuildNode>();
                order.Add(n.Part);
            }
            list.Add(n);
        }
        foreach (var p in order)
        {
            var nodes = byPart[p];
            var extra = nodes.Count - stock.Owned(p);
            if (extra <= 0)
                continue;
            r.Parts.Add(new MissingPart(p, extra));
            r.Total += extra;
            var left = extra;
            if (prefer != null)
                foreach (var n in nodes)
                    if (left > 0 && prefer.Contains(n) && r.Nodes.Add(n))
                        left--;
            for (var i = nodes.Count - 1; i >= 0 && left > 0; i--)
                if (r.Nodes.Add(nodes[i]))
                    left--;
        }
        return r;
    }

    /// <summary>
    /// The scrap it costs to craft every missing part at a workbench, or -1 if one can't be crafted (a wonder part, or
    /// above <paramref name="cap"/>, the tier the round crafts up to).
    /// </summary>
    public int CraftCost(Rarity cap)
    {
        var total = 0;
        foreach (var m in Parts)
        {
            var c = Scrap.CraftCost(m.Part);
            if (c < 0 || m.Part.Rarity > cap)
                return -1;
            total += c * m.Count;
        }
        return total;
    }
}

/// <summary>What a row of the linear build list is.</summary>
public enum BenchRowKind : byte
{
    /// <summary>A fitted part.</summary>
    Part,

    /// <summary>An empty socket the build needs filled ("needs a feed").</summary>
    Needed,
}

/// <summary>One row of the build as a linear list: Grip, Receiver(s), Feed, Barrel(s), Muzzle, Stock, Optic...</summary>
public struct BenchRow
{
    public BenchRowKind Kind;

    /// <summary>Indent: the grip 0, receivers 1, their parts 2, a splitter's barrels 3...</summary>
    public int Depth;

    /// <summary>The fitted part (Part rows).</summary>
    public BuildNode? Node;

    /// <summary>The part whose socket this is (null for the grip's own row).</summary>
    public BuildNode? Parent;

    public PartSocket? Socket;

    /// <summary>The parent's path (<see cref="BuildNode.Path"/>), so the row can be addressed after the tree changes.</summary>
    public string ParentPath;

    /// <summary>The part isn't covered by your parts (copy assembly, presets): drawn red.</summary>
    public bool Missing;
}

/// <summary>A socket on the 3D model: where it is (gun space) and what's in it.</summary>
public struct SocketSpot
{
    public BuildNode Parent;
    public PartSocket Socket;
    public string ParentPath;
    public BuildNode? Child;

    /// <summary>Gun-space position of the socket (the model's space: where the part plugs in).</summary>
    public Vector3 Pos;

    /// <summary>The build needs it filled.</summary>
    public bool Needed;

    /// <summary>The part in it is missing (not in your parts).</summary>
    public bool Missing;
}

/// <summary>The camera of the workbench's big 3D view: orbit (yaw, pitch) and zoom around the gun's middle.</summary>
public struct BenchCamera
{
    public float Yaw, Pitch, Zoom;

    /// <summary>The gun's bounds (gun space) the view frames.</summary>
    public Vector3 Center;

    public float Radius;

    /// <summary>A short decaying shake after a part snaps in (a little camera nudge).</summary>
    public float Nudge;

    public const float Fov = 0.5f;

    public static BenchCamera Default => new() { Yaw = 0.18f, Pitch = 0.12f, Zoom = 1f, Radius = 0.4f };

    /// <summary>
    /// The pose (gun space to world), view and projection for a view <paramref name="aspect"/> wide: the gun turned by
    /// yaw and pitch about its middle, looked at side-on from -X (muzzle to the left, like the gunsmith preview).
    /// </summary>
    public readonly void Matrices(float aspect, out Matrix4x4 pose, out Matrix4x4 view, out Matrix4x4 proj)
    {
        aspect = MathF.Max(aspect, 0.1f);
        var r = MathF.Max(Radius, 0.08f);
        var fit = MathF.Max(r / MathF.Tan(Fov / 2), r / (MathF.Tan(Fov / 2) * aspect)) * 1.12f;
        var dist = fit / Math.Clamp(Zoom, 0.4f, 4f);
        var shake = MathF.Sin(Nudge * 40f) * Nudge * 0.06f;
        pose = Matrix4x4.CreateTranslation(-Center) * Matrix4x4.CreateRotationY(Yaw + shake) * Matrix4x4.CreateRotationZ(Pitch);
        view = Matrix4x4.CreateLookAt(new Vector3(-dist, 0, 0), Vector3.Zero, Vector3.UnitY);
        proj = Matrix4x4.CreatePerspectiveFieldOfView(Fov, aspect, MathF.Max(0.01f, dist - r * 3f), dist + r * 4f);
    }

    /// <summary>Frames the gun body <paramref name="m"/> (its boxes' bounds).</summary>
    public void Frame(GunMesh? m)
    {
        if (m == null || m.Matrices.Length == 0)
        {
            Center = Vector3.Zero;
            Radius = 0.3f;
            return;
        }
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (ref readonly var w in m.Matrices.AsSpan())
        {
            var half = new Vector3(new Vector3(w.M11, w.M12, w.M13).Length(), new Vector3(w.M21, w.M22, w.M23).Length(), new Vector3(w.M31, w.M32, w.M33).Length()) * 0.5f;
            min = Vector3.Min(min, w.Translation - half);
            max = Vector3.Max(max, w.Translation + half);
        }
        Center = (min + max) * 0.5f;
        Radius = MathF.Max(0.12f, (max - min).Length() * 0.5f);
    }

    /// <summary>
    /// Screen position of gun-space point <paramref name="p"/> in a view at <paramref name="viewMin"/> sized
    /// <paramref name="viewSize"/> (y down), given <paramref name="pvm"/> = pose x view x projection. False behind the camera.
    /// </summary>
    public static bool Project(Vector3 p, in Matrix4x4 pvm, Vector2 viewMin, Vector2 viewSize, out Vector2 screen, out float depth)
    {
        var c = Vector4.Transform(new Vector4(p, 1f), pvm);
        depth = c.W;
        if (c.W <= 1e-5f)
        {
            screen = default;
            return false;
        }
        var ndc = new Vector2(c.X / c.W, c.Y / c.W);
        screen = new Vector2(viewMin.X + (ndc.X * 0.5f + 0.5f) * viewSize.X, viewMin.Y + (0.5f - ndc.Y * 0.5f) * viewSize.Y);
        return true;
    }

    /// <summary>The gun-space ray under screen point <paramref name="screen"/> (origin on the near plane, unit direction).</summary>
    public static bool Ray(Vector2 screen, in Matrix4x4 pvm, Vector2 viewMin, Vector2 viewSize, out Vector3 origin, out Vector3 dir)
    {
        origin = dir = default;
        if (!Matrix4x4.Invert(pvm, out var inv) || viewSize.X < 1 || viewSize.Y < 1)
            return false;
        var nx = (screen.X - viewMin.X) / viewSize.X * 2f - 1f;
        var ny = 1f - (screen.Y - viewMin.Y) / viewSize.Y * 2f;
        var a = Vector4.Transform(new Vector4(nx, ny, 0f, 1f), inv);
        var b = Vector4.Transform(new Vector4(nx, ny, 1f, 1f), inv);
        if (MathF.Abs(a.W) < 1e-9f || MathF.Abs(b.W) < 1e-9f)
            return false;
        origin = new Vector3(a.X, a.Y, a.Z) / a.W;
        var far = new Vector3(b.X, b.Y, b.Z) / b.W;
        var d = far - origin;
        if (d.LengthSquared() < 1e-12f)
            return false;
        dir = Vector3.Normalize(d);
        return true;
    }
}

/// <summary>
/// The workbench's editing model (docs/zombies/WEAPON-BUILDER.md, "The workbench"), with no UI in it so the self-test can
/// drive it headless: the build being edited and its undo history, the parts you have (<see cref="BenchStock"/>), the
/// build as a linear list, every socket on the 3D model, which sockets a dragged part snaps to (and the nearest one
/// under the mouse ray), placing with swap semantics (a replaced part's parts move onto the new one where they fit),
/// moving and removing parts, double-click auto-place, auto-complete, copying a whole receiver assembly, and what's
/// missing. Everything that allocates runs when the build changes, not per frame.
/// </summary>
public sealed class BenchModel
{
    public BenchModel(WeaponBuild build, BenchStock stock, Func<string, PartDef?>? find = null)
    {
        _find = find ?? CallOfSlop.Parts.Find;
        Stock = stock;
        Build = build;
        Changed();
    }

    private readonly Func<string, PartDef?> _find;

    public WeaponBuild Build { get; private set; }

    public BenchStock Stock { get; private set; }

    /// <summary>The build put together (problems, stats, the model with missing parts as red ghosts).</summary>
    public AssembledWeapon Asm { get; private set; } = null!;

    public MissingReport Missing { get; private set; } = MissingReport.None;

    /// <summary>The build as an ordered, indented list.</summary>
    public readonly List<BenchRow> Rows = new();

    /// <summary>Every socket on the model (filled or not), in <see cref="Rows"/> order.</summary>
    public readonly List<SocketSpot> Spots = new();

    /// <summary>Bumped on every change (UI caches compare it).</summary>
    public int Version { get; private set; }

    /// <summary>Nodes of the last copied receiver assembly: marked missing first.</summary>
    private HashSet<BuildNode>? _copied;

    private readonly List<string> _undo = new(), _redo = new();

    public const int UndoDepth = 64;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Red ghost tint for missing parts (rgb, opacity).</summary>
    public static readonly Vector4 MissingTint = new(1f, 0.18f, 0.15f, 0.42f);

    /// <summary>The highlight of a part previewed in a socket.</summary>
    public static readonly Vector4 PreviewTint = new(0.45f, 1f, 0.55f, 0.9f);

    /// <summary>Complete, legal and nothing missing: it can be saved or applied.</summary>
    public bool CanFinalize => Asm.Ok && Missing.Total == 0;

    // ---- Changing the build ----------------------------------------------------------------------------------

    /// <summary>Starts editing <paramref name="b"/> (clears the undo history unless <paramref name="keepHistory"/>).</summary>
    public void SetBuild(WeaponBuild b, bool keepHistory = false)
    {
        if (keepHistory)
            PushUndo();
        else
        {
            _undo.Clear();
            _redo.Clear();
        }
        Build = b;
        _copied = null;
        Changed();
    }

    /// <summary>
    /// The "Chain fire" checkbox: on = the receivers fire in sequence, one per shot; off = a volley, all together. Stored
    /// on the build (saved in its text), one undo step. False when nothing changed.
    /// </summary>
    public bool SetChainFire(bool on)
    {
        var want = on ? FireStyle.Rotate : FireStyle.Volley;
        if (Build.Style == want && Build.StyleChoice != null)
            return false;
        PushUndo();
        Build.StyleChoice = want;
        Changed();
        return true;
    }

    /// <summary>A new stock (a run's parts changed): recomputes what's missing.</summary>
    public void SetStock(BenchStock stock)
    {
        Stock = stock;
        Changed(bump: true);
    }

    private void PushUndo()
    {
        _undo.Add(Build.ToText());
        if (_undo.Count > UndoDepth)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    public bool Undo() => Step(_undo, _redo);

    public bool Redo() => Step(_redo, _undo);

    private bool Step(List<string> from, List<string> to)
    {
        if (from.Count == 0)
            return false;
        var text = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(Build.ToText());
        try
        {
            Build = WeaponBuild.Parse(text, _find);
        }
        catch (FormatException)
        {
            return false;
        }
        _copied = null;
        Changed();
        return true;
    }

    /// <summary>Recomputes everything derived from the build (call after changing it from outside, e.g. the name).</summary>
    public void Changed(bool bump = true)
    {
        Missing = MissingReport.Of(Build, Stock, _copied);
        Dictionary<BuildNode, Vector4>? tints = null;
        if (Missing.Nodes.Count > 0)
        {
            tints = new Dictionary<BuildNode, Vector4>(ReferenceEqualityComparer.Instance);
            foreach (var n in Missing.Nodes)
                tints[n] = MissingTint;
        }
        Asm = WeaponBuilder.Assemble(Build, tints);
        BuildRows();
        BuildSpots();
        if (bump)
            Version++;
    }

    /// <summary>Order of socket types in the linear list (Grip, Receiver(s), Feed, Ammo, Barrel(s), Muzzle, ..., Stock, Optic).</summary>
    public static int TypeRank(string type) => type switch
    {
        "receiver" => 0,
        "head" => 0,
        "feed" => 1,
        "hopper" => 1,
        "ammo" => 2,
        "barrel" => 3,
        "muzzle" => 4,
        "amp" => 5,
        "upgrade" => 6,
        "stock" => 7,
        "optic" => 8,
        "foregrip" => 9,
        "mod" => 10,
        "utility" => 11,
        _ => 12 + Math.Max(0, Array.IndexOf(PartFile.SocketTypes, type)), // a socket type added in Parts/_Registry.json: after the built-in ones, in registry order
    };

    /// <summary>A part's sockets in list order (by type rank, then the part's own order).</summary>
    public static IEnumerable<PartSocket> Ordered(PartDef p) =>
        p.Sockets.Select((s, i) => (s, i)).OrderBy(x => TypeRank(x.s.Type)).ThenBy(x => x.i).Select(x => x.s);

    private void BuildRows()
    {
        Rows.Clear();
        if (Build.Root is not { } root)
            return;
        var fed = Build.HopperFed();
        Rows.Add(new BenchRow { Kind = BenchRowKind.Part, Depth = 0, Node = root, ParentPath = "", Missing = Missing.Nodes.Contains(root) });
        var anyReceiver = Build.IsMelee || Build.Receivers.Count > 0;
        AddRows(root, "", 1, fed, ref anyReceiver);
    }

    private void AddRows(BuildNode n, string path, int depth, List<BuildNode> fed, ref bool anyReceiver)
    {
        foreach (var s in Ordered(n.Part))
        {
            var child = n.Child(s.Name);
            if (child != null)
            {
                Rows.Add(new BenchRow { Kind = BenchRowKind.Part, Depth = depth, Node = child, Parent = n, Socket = s, ParentPath = path, Missing = Missing.Nodes.Contains(child) });
                AddRows(child, child.Path(), depth + 1, fed, ref anyReceiver);
            }
            else if (BuildRules.NeedsFilling(Build, n, s, fed) || (!anyReceiver && s.Type == "receiver" && n == Build.Root))
            {
                if (s.Type == "receiver")
                    anyReceiver = true; // one "needs a receiver" row, not one per empty slot
                Rows.Add(new BenchRow { Kind = BenchRowKind.Needed, Depth = depth, Parent = n, Socket = s, ParentPath = path });
            }
        }
    }

    private void BuildSpots()
    {
        Spots.Clear();
        if (Build.Root is not { } root || Asm.Model is not { } model)
            return;
        var fed = Build.HopperFed();
        void Visit(BuildNode n, string path)
        {
            if (!model.Nodes.TryGetValue(n, out var sn))
                return;
            foreach (var s in Ordered(n.Part))
            {
                var child = n.Child(s.Name);
                Spots.Add(new SocketSpot
                {
                    Parent = n, Socket = s, ParentPath = path, Child = child, Pos = sn.ToWorld(s.Pos),
                    Needed = child == null && (BuildRules.NeedsFilling(Build, n, s, fed) || (s.Type == "receiver" && n == root && Build.Receivers.Count == 0 && !Build.IsMelee)),
                    Missing = child != null && Missing.Nodes.Contains(child),
                });
                if (child != null)
                    Visit(child, child.Path());
            }
        }
        Visit(root, "");
    }

    /// <summary>The spot index of <paramref name="parentPath"/>/<paramref name="socket"/>, or -1.</summary>
    public int SpotOf(string parentPath, string socket)
    {
        for (var i = 0; i < Spots.Count; i++)
            if (Spots[i].ParentPath == parentPath && Spots[i].Socket.Name.Equals(socket, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // ---- Snapping -------------------------------------------------------------------------------------------

    /// <summary>
    /// Why <paramref name="part"/> can't be dropped into spot <paramref name="spot"/> (replacing what's there), or null.
    /// Checks your parts first (you need a free one, unless <paramref name="moving"/> is a part already on the build),
    /// then every assembly rule.
    /// </summary>
    public string? WhyNot(int spot, PartDef part, BuildNode? moving = null)
    {
        if ((uint)spot >= (uint)Spots.Count)
            return "That isn't a socket.";
        var s = Spots[spot];
        if (moving != null)
        {
            if (s.Child == moving)
                return "It's already there.";
            for (var p = s.Parent; p != null; p = p.Parent)
                if (p == moving)
                    return $"The {moving.Part.Name} can't go onto itself.";
            return BuildRules.SocketWhy(s.Parent, s.Socket, part); // the build's limits are checked once it has moved (Move)
        }
        else if (Stock.Free(part, Build) <= 0)
            return $"You have no {part.Name} left (zombies drop parts; or craft one with scrap).";
        return BuildRules.WhyNot(Build, s.Parent, s.Socket, part);
    }

    /// <summary>
    /// Fills <paramref name="valid"/> (one per spot) with where <paramref name="part"/> can be dropped. Allocates (rule
    /// checks build trial trees): call it once when a drag starts, not per frame.
    /// </summary>
    public int ValidSpots(PartDef part, bool[] valid, BuildNode? moving = null)
    {
        var n = 0;
        for (var i = 0; i < Spots.Count && i < valid.Length; i++)
        {
            valid[i] = BuildRules.SocketWhy(Spots[i].Parent, Spots[i].Socket, part) == null && WhyNot(i, part, moving) == null;
            if (valid[i])
                n++;
        }
        return n;
    }

    /// <summary>Whether <paramref name="part"/> has a socket on the build at all (socket rules only: cheap, for dimming the list).</summary>
    public bool FitsAnywhere(PartDef part)
    {
        if (part.IsRoot)
            return false;
        foreach (var s in Spots)
            if (BuildRules.SocketWhy(s.Parent, s.Socket, part) == null)
                return true;
        return false;
    }

    /// <summary>
    /// The reason <paramref name="part"/> fits nowhere. When a socket's own rules take it, the reason is what the whole
    /// build says (weight, barrel or part limits; you have none left), not a socket elsewhere. Otherwise the refusal of the
    /// best socket of its type: empty ones first, each judged against the receiver it hangs under. Or a generic line.
    /// </summary>
    public string WhyNowhere(PartDef part)
    {
        if (part.IsRoot)
            return part.Hilt != null ? "A hilt starts a new melee weapon (New weapon)." : "A grip starts a new weapon (New weapon, or Change grip).";
        if (NowhereSpot(part, out var socketOk) is var i and >= 0)
            return socketOk ? WhyNot(i, part) ?? "It doesn't fit there." : BuildRules.SocketWhy(Spots[i].Parent, Spots[i].Socket, part) ?? "It doesn't fit there.";
        return $"This build has no {part.Mount} socket for it.";
    }

    /// <summary>
    /// The spot that best explains why <paramref name="part"/> can't go on: one whose socket rules take it (empty first:
    /// <paramref name="socketOk"/> true, so the build's limits are the reason), else one of its mount type that refuses it
    /// (empty first). -1 if the build has no socket of its type.
    /// </summary>
    private int NowhereSpot(PartDef part, out bool socketOk)
    {
        socketOk = false;
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < Spots.Count; i++)
                if (Spots[i].Socket.Type == part.Mount && (pass == 1 || Spots[i].Child == null) && BuildRules.SocketWhy(Spots[i].Parent, Spots[i].Socket, part) == null)
                {
                    socketOk = true;
                    return i;
                }
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < Spots.Count; i++)
                if (Spots[i].Socket.Type == part.Mount && (pass == 1 || Spots[i].Child == null))
                    return i;
        return -1;
    }

    /// <summary>
    /// <see cref="WhyNowhere"/> as a short phrase for the inventory tooltip ("needs a shell bore feed socket",
    /// "no muzzle socket on this build"). Allocates: cache it (see <see cref="BenchInventory"/>).
    /// </summary>
    public string WhyNowhereShort(PartDef part)
    {
        if (part.IsRoot)
            return part.Hilt != null ? "starts a new melee weapon" : "starts a new weapon";
        if (NowhereSpot(part, out var socketOk) is var i and >= 0)
            return socketOk ? "fits a socket, but not on this build as it is" : BuildRules.SocketWhyShort(Spots[i].Parent, Spots[i].Socket, part) ?? "doesn't fit";
        return $"no {part.Mount} socket on this build";
    }

    /// <summary>
    /// The valid spot nearest the gun-space ray (<paramref name="origin"/>, unit <paramref name="dir"/>) within
    /// <paramref name="tanTolerance"/> (radius per metre of distance, i.e. an angle); the closer one wins a tie. -1 if none.
    /// No allocation (per frame while dragging).
    /// </summary>
    public static int PickRay(List<SocketSpot> spots, ReadOnlySpan<bool> valid, Vector3 origin, Vector3 dir, float tanTolerance)
    {
        var best = -1;
        var bestScore = float.MaxValue;
        for (var i = 0; i < spots.Count; i++)
        {
            if (i < valid.Length && !valid[i])
                continue;
            var v = spots[i].Pos - origin;
            var t = Vector3.Dot(v, dir);
            if (t <= 1e-4f)
                continue;
            var perp = (v - dir * t).Length();
            var ang = perp / t;
            if (ang > tanTolerance)
                continue;
            var score = ang + t * 1e-4f; // nearer the ray first, then nearer the eye
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// The best free socket for <paramref name="part"/> (double-click): the first empty socket it fits in list order,
    /// needed ones first. -1 if none.
    /// </summary>
    public int BestFreeSpot(PartDef part)
    {
        var best = -1;
        for (var pass = 0; pass < 2 && best < 0; pass++)
            for (var i = 0; i < Spots.Count; i++)
            {
                var s = Spots[i];
                if (s.Child != null || (pass == 0 && !s.Needed))
                    continue;
                if (BuildRules.SocketWhy(s.Parent, s.Socket, part) == null && WhyNot(i, part) == null)
                {
                    best = i;
                    break;
                }
            }
        return best;
    }

    /// <summary>
    /// The build as it would be with <paramref name="part"/> (or the part <paramref name="moving"/>, with what's on it)
    /// snapped into spot <paramref name="spot"/>, the new part highlighted: the live preview while hovering a socket.
    /// Null if it can't go there. Allocates: cache it per hovered spot.
    /// </summary>
    public AssembledWeapon? TrialPlace(int spot, PartDef part, BuildNode? moving = null)
    {
        if ((uint)spot >= (uint)Spots.Count || WhyNot(spot, part, moving) != null)
            return null;
        var s = Spots[spot];
        var t = Build.Clone();
        var parent = t.At(s.ParentPath);
        if (parent == null)
            return null;
        BuildNode nn;
        if (moving != null)
        {
            var path = moving.Path();
            if (t.At(path) is not { } m || path.Length == 0)
                return null;
            m.Detach();
            nn = parent.Attach(s.Socket.Name, m);
        }
        else
        {
            var old = parent.Child(s.Socket.Name);
            nn = parent.Attach(s.Socket.Name, part);
            if (old != null)
                foreach (var (os, oc) in old.Children().ToList())
                    if (part.Sockets.FirstOrDefault(x => x.Name.Equals(os.Name, StringComparison.OrdinalIgnoreCase) && x.Type == os.Type) is { } ns
                        && BuildRules.SocketWhy(nn, ns, oc.Part) == null)
                        nn.Attach(ns.Name, oc);
        }
        var tints = new Dictionary<BuildNode, Vector4>(ReferenceEqualityComparer.Instance);
        foreach (var n in nn.Walk())
            tints[n] = PreviewTint;
        return WeaponBuilder.Assemble(t, tints);
    }

    // ---- Edits ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Puts <paramref name="part"/> into spot <paramref name="spot"/>. A part already there comes off (back to your
    /// parts), and whatever was plugged into it moves onto the new part where it has the same socket and still fits
    /// (a barrel swapped keeps its muzzle device, a receiver swapped keeps its barrel, feed and ammo if the bore
    /// matches); the rest comes off too. Returns why not, or null. <paramref name="placed"/> is the new node.
    /// </summary>
    public string? Place(int spot, PartDef part, out BuildNode? placed)
    {
        placed = null;
        if (WhyNot(spot, part) is { } why)
            return why;
        var s = Spots[spot];
        PushUndo();
        var old = s.Child;
        var nn = s.Parent.Attach(s.Socket.Name, part);
        if (old != null)
            foreach (var (os, oc) in old.Children().ToList())
            {
                var ns = part.Sockets.FirstOrDefault(x => x.Name.Equals(os.Name, StringComparison.OrdinalIgnoreCase) && x.Type == os.Type);
                if (ns == null || BuildRules.SocketWhy(nn, ns, oc.Part) != null)
                    continue;
                nn.Attach(ns.Name, oc);
                if (BuildRules.LimitProblem(Build) != null || HasRuleError(oc))
                    oc.Detach(); // it doesn't fit the new part after all: back to your parts
            }
        placed = nn;
        if (old != null)
            _copied?.Remove(old);
        Changed();
        return null;
    }

    /// <summary>A rule error inside <paramref name="sub"/> (bores, actions, depth) in the build as it is.</summary>
    private static bool HasRuleError(BuildNode sub)
    {
        foreach (var n in sub.Walk())
            foreach (var (s, c) in n.Children())
                if (BuildRules.SocketWhy(n, s, c.Part) != null)
                    return true;
        return false;
    }

    /// <summary>Takes <paramref name="node"/> (and everything on it) off, back to your parts. The grip can't come off.</summary>
    public string? Remove(BuildNode node)
    {
        if (node == Build.Root || node.Parent == null)
            return "The grip is the weapon: pick another one with Change grip.";
        PushUndo();
        node.Detach();
        Changed();
        return null;
    }

    /// <summary>
    /// Double-click on a part in the build list (row <paramref name="row"/> of <see cref="Rows"/>): it comes off, back to
    /// your parts, with everything plugged into it (a receiver brings its barrel, feed and ammo: the same as dragging it
    /// off, and one undo step puts the whole assembly back). Returns why not (the grip, an empty socket row), or null.
    /// <paramref name="removed"/> is the part that came off.
    /// </summary>
    public string? DoubleClickRow(int row, out BuildNode? removed)
    {
        removed = null;
        if ((uint)row >= (uint)Rows.Count || Rows[row] is not { Kind: BenchRowKind.Part, Node: { } node })
            return "That's an empty socket: drag a part onto it.";
        var why = Remove(node);
        removed = why == null ? node : null;
        return why;
    }

    /// <summary>Double-click on a part on the gun (spot <paramref name="spot"/>, its socket): as <see cref="DoubleClickRow"/>.</summary>
    public string? DoubleClickSpot(int spot, out BuildNode? removed)
    {
        removed = null;
        if ((uint)spot >= (uint)Spots.Count || Spots[spot].Child is not { } node)
            return "That socket is empty: drag a part onto it.";
        var why = Remove(node);
        removed = why == null ? node : null;
        return why;
    }

    /// <summary>Moves <paramref name="node"/> (with its parts) into spot <paramref name="spot"/>, swapping out what's there. Returns why not, or null.</summary>
    public string? Move(BuildNode node, int spot)
    {
        if (WhyNot(spot, node.Part, node) is { } why)
            return why;
        var s = Spots[spot];
        var before = Build.ToText();
        PushUndo();
        node.Detach();
        s.Parent.Attach(s.Socket.Name, node);
        if (BuildRules.LimitProblem(Build) is { } limit)
            return Revert(before, limit);
        if (HasRuleError(node))
            return Revert(before, BuildRules.Check(Build).FirstOrDefault(p => !p.Incomplete).Message ?? "Its parts don't fit there.");
        Changed();
        return null;
    }

    private string Revert(string text, string why)
    {
        _undo.RemoveAt(_undo.Count - 1);
        Build = WeaponBuild.Parse(text, _find);
        Changed();
        return why;
    }

    /// <summary>Double-click: <paramref name="part"/> into its best free socket. Returns why not, or null.</summary>
    public string? AutoPlace(PartDef part, out BuildNode? placed)
    {
        placed = null;
        if (part.IsRoot)
            return WhyNowhere(part);
        if (!IsUnlimitedOrFree(part))
            return $"You have no {part.Name} left.";
        var i = BestFreeSpot(part);
        if (i < 0)
            return FitsAnywhere(part) ? $"No free socket takes the {part.Name} (drop it onto a part to swap it)." : WhyNowhere(part);
        return Place(i, part, out placed);
    }

    private bool IsUnlimitedOrFree(PartDef p) => Stock.Free(p, Build) > 0;

    /// <summary>
    /// Fills every empty needed socket with parts you have (the rarest that fits first; new needs a placed part brings,
    /// like a receiver's feed, are filled too). One undo step. Returns how many parts went on.
    /// </summary>
    public int AutoComplete(IEnumerable<PartDef> candidates)
    {
        var pool = candidates.Where(p => !p.IsRoot && p.IsMeleePart == Build.IsMelee).ToList();
        // Parts whose own needed sockets something in the pool can fill come first (a heavy receiver with no heavy ammo
        // to hand would be a dead end), then the rarest.
        var list = pool.OrderByDescending(p => Viable(p, pool)).ThenByDescending(p => p.Rarity).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
        var before = Build.ToText();
        var placed = 0;
        var skip = new HashSet<string>();
        for (var guard = 0; guard < 64; guard++)
        {
            var target = -1;
            for (var i = 0; i < Spots.Count; i++)
                if (Spots[i].Needed && !skip.Contains(Spots[i].ParentPath + "/" + Spots[i].Socket.Name))
                {
                    target = i;
                    break;
                }
            if (target < 0)
                break;
            var spot = Spots[target];
            PartDef? pick = null;
            foreach (var p in list)
                if (BuildRules.SocketWhy(spot.Parent, spot.Socket, p) == null && WhyNot(target, p) == null)
                {
                    pick = p;
                    break;
                }
            if (pick == null)
            {
                skip.Add(spot.ParentPath + "/" + spot.Socket.Name);
                continue;
            }
            spot.Parent.Attach(spot.Socket.Name, pick);
            placed++;
            Changed(bump: false);
        }
        if (placed > 0)
        {
            _undo.Add(before);
            if (_undo.Count > UndoDepth)
                _undo.RemoveAt(0);
            _redo.Clear();
        }
        Changed();
        return placed;
    }

    /// <summary>Whether every required socket of <paramref name="part"/> has something in <paramref name="pool"/> that mounts there (bores and actions of a receiver checked).</summary>
    private static bool Viable(PartDef part, List<PartDef> pool)
    {
        foreach (var s in part.Sockets)
        {
            if (!s.Required)
                continue;
            var ok = false;
            foreach (var c in pool)
                if (c.Mount == s.Type && (s.Accepts.Length == 0 || s.Accepts.Contains(c.Category))
                    && (part.Receiver is not { } r || ((!BuildRules.BoreBound(c.Category) || c.FitsBore(r.Bore)) && (c.RequiresAction.Length == 0 || c.RequiresAction.Contains(r.Action)))))
                {
                    ok = true;
                    break;
                }
            if (!ok)
                return false;
        }
        return true;
    }

    /// <summary>The empty receiver socket a copy of <paramref name="receiver"/> goes to: the first after it, else the first. Null if full.</summary>
    public PartSocket? CopyTarget(BuildNode receiver)
    {
        if (Build.Root is not { } root || receiver.Parent != root || receiver.ParentSocket is not { } src)
            return null;
        var sockets = root.Part.Sockets;
        var at = Array.IndexOf(sockets, src);
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < sockets.Length; i++)
                if (sockets[i].Type == "receiver" && root.Child(sockets[i].Name) == null && (pass == 1 || i > at))
                    return sockets[i];
        return null;
    }

    /// <summary>
    /// Copies <paramref name="receiver"/> and everything hanging under it onto another free receiver socket of the grip.
    /// Works without the parts: the copy's parts you don't have show as missing (red), and the build can't be finalised
    /// until you have them. Refused only by the grip (no free receiver slot) or the build's limits (weight...).
    /// </summary>
    public string? CopyReceiver(BuildNode receiver, out PartSocket? target)
    {
        target = null;
        if (receiver.Part.Category != PartCategory.Receiver || receiver.Parent != Build.Root)
            return "Only a receiver on the grip can be copied.";
        target = CopyTarget(receiver);
        if (target == null)
            return $"The {Build.Root!.Part.Name} has no free receiver slot ({Build.Root.Part.ReceiverCapacity} in all, every one in use).";
        if (BuildRules.SocketWhy(Build.Root!, target, receiver.Part) is { } why)
            return why;
        var before = Build.ToText();
        PushUndo();
        var copy = receiver.Clone();
        Build.Root.Attach(target.Name, copy);
        if (BuildRules.LimitProblem(Build) is { } limit)
        {
            target = null;
            return Revert(before, limit);
        }
        _copied = new HashSet<BuildNode>(copy.Walk(), ReferenceEqualityComparer.Instance);
        Changed();
        return null;
    }

    /// <summary>A short tooltip for the Build / Apply button: what stops it, or "Ready".</summary>
    public string FinalizeTip()
    {
        var sb = new StringBuilder();
        if (Missing.Total > 0)
            sb.Append(Missing.Text()).Append(" (shown red on the gun and in the list).");
        foreach (var p in Asm.Problems.Take(4))
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(p.Incomplete ? "Needs: " : "Problem: ").Append(p.Message);
        }
        return sb.Length == 0 ? "Ready." : sb.ToString();
    }
}

/// <summary>
/// The workbench's part inventory as a filtered list (left panel): owned parts of the editing kind, narrowed by the
/// category chip and the search, and, unless "Show incompatible parts" is on, only the parts that fit somewhere on the
/// build and have a spare copy. Parts shown that can't be fitted carry a short reason (incompatible, or "none spare"). Rebuilt only when its inputs change: calling
/// <see cref="Refresh"/> every frame with the same inputs allocates nothing.
/// </summary>
public sealed class BenchInventory
{
    /// <summary>The listed parts, grouped by category in part-file order.</summary>
    public readonly List<PartDef> Items = new();

    /// <summary>Per item: compatible with the build and a free one left (drawn bright, draggable).</summary>
    public readonly List<bool> Fits = new();

    /// <summary>Per item: the socket rules allow it somewhere on the build (false = incompatible).</summary>
    public readonly List<bool> Compatible = new();

    /// <summary>Per item: how many are free (int.MaxValue when unlimited).</summary>
    public readonly List<int> Free = new();

    /// <summary>Per item: the short reason it can't be fitted now: incompatible, or "none spare" (null when it fits).</summary>
    public readonly List<string?> Why = new();

    /// <summary>The category chips: categories with listed parts (search and the toggle applied, not the chip itself), with counts.</summary>
    public readonly List<PartCategory> Cats = new();

    public readonly List<int> CatCounts = new();

    /// <summary>Chip labels ("Barrel 3"), built with the list so drawing them allocates nothing.</summary>
    public readonly List<string> CatLabels = new();

    /// <summary>The "All" chip's label with the count of every listed part across categories.</summary>
    public string AllLabel { get; private set; } = "All";

    /// <summary>Parts left out by the toggle (search applied): incompatible ones and ones with none spare. "N hidden" hint.</summary>
    public int Hidden { get; private set; }

    /// <summary>The reason for a compatible part with no spare copy (every one owned is already on the build).</summary>
    public const string NoneSpare = "none spare";

    private BenchModel? _model;
    private (int Version, int Stock, int Filter, string Search, bool Melee, bool ShowAll) _key = (-1, 0, 0, "", false, false);

    /// <summary>Forgets the cached inputs (part files reloaded in place).</summary>
    public void Invalidate() => _model = null;

    /// <summary>
    /// Rebuilds the list from <paramref name="all"/> if the build, stock, filter (-1 = all), search, kind or toggle
    /// changed. Returns true when it rebuilt.
    /// </summary>
    public bool Refresh(BenchModel bm, IEnumerable<PartDef> all, int filter, string search, bool melee, bool showIncompatible)
    {
        var key = (bm.Version, bm.Stock.Fingerprint, filter, search, melee, showIncompatible);
        if (ReferenceEquals(bm, _model) && key == _key)
            return false;
        _model = bm;
        _key = key;
        Items.Clear();
        Fits.Clear();
        Compatible.Clear();
        Free.Clear();
        Why.Clear();
        Cats.Clear();
        CatCounts.Clear();
        CatLabels.Clear();
        Hidden = 0;
        var total = 0;
        foreach (var p in bm.Stock.Parts(all))
        {
            if (p.IsRoot || p.IsMeleePart != melee)
                continue;
            if (search.Length > 0 && !p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !PartDef.CategoryName(p.Category).Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            var compatible = bm.FitsAnywhere(p);
            var free = bm.Stock.Free(p, bm.Build);
            if ((!compatible || free <= 0) && !showIncompatible)
            {
                Hidden++;
                continue;
            }
            var ci = Cats.IndexOf(p.Category);
            if (ci < 0)
            {
                Cats.Add(p.Category);
                CatCounts.Add(1);
            }
            else
                CatCounts[ci]++;
            total++;
            if (filter >= 0 && (int)p.Category != filter)
                continue;
            Items.Add(p);
            Compatible.Add(compatible);
            Free.Add(free);
            Fits.Add(compatible && free > 0);
            Why.Add(!compatible ? bm.WhyNowhereShort(p) : free <= 0 ? NoneSpare : null);
        }
        // The chosen chip stays visible even when nothing in it is listed now (so it can be switched off).
        if (filter >= 0 && !Cats.Contains((PartCategory)filter))
        {
            Cats.Add((PartCategory)filter);
            CatCounts.Add(0);
        }
        for (var i = 0; i < Cats.Count; i++)
            CatLabels.Add($"{PartDef.CategoryName(Cats[i])} {CatCounts[i]}");
        AllLabel = $"All {total}";
        return true;
    }
}
