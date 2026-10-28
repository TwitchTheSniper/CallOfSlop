using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A gun as scene nodes: the gun frame (origin = grip, -Z = muzzle; its local matrix is the gun's pose) with its boxes
/// grouped under part nodes in draw order ("body", "muzzle_device", "underbarrel", "magazine", "optic"), the muzzle
/// flash under the "muzzle" socket, and sockets for what attaches to a gun: "grip" and "support" (where the hands go,
/// with the hands' rotations), "mag_well", "action", "optic_mount", "underbarrel_mount" and "stock".
///
/// The boxes come from <see cref="GunModel.Emit"/>, the same code that draws the gun immediately, so the two can't drift
/// apart. The nodes are built once per weapon (when <see cref="WeaponDef"/> or its built mesh changes); each frame
/// <see cref="Pose"/> only sets the pose, fades the optic housing (<see cref="GunModel.AdsCutaway"/>), moves or hides the
/// magazine for the reload, and shows or spins the flash. No allocations per frame.
/// </summary>
public sealed class GunRig : GunModel.IPartSink
{
    /// <summary>The gun frame. Its local matrix is the pose passed to <see cref="Pose"/>.</summary>
    public readonly SceneNode Root;

    public SceneNode Muzzle { get; private set; } = null!;
    public SceneNode Flash { get; private set; } = null!;
    public SceneNode Grip { get; private set; } = null!;
    public SceneNode Support { get; private set; } = null!;
    public SceneNode MagWell { get; private set; } = null!;
    public SceneNode OpticMount { get; private set; } = null!;

    /// <summary>The weapon the nodes were built for (null before the first <see cref="Build"/>).</summary>
    public WeaponDef? Def { get; private set; }

    private GunMesh? _mesh;

    private struct Slot
    {
        public SceneNode Node;
        public float BaseAlpha;
        public bool Shell;
    }

    private Slot[] _slots = [];
    private int _slotCount;
    private SceneNode[] _mags = [];
    private int _magCount;
    private bool _cutawayShapes; // some part's shape follows the cutaway (the holo hood): re-emit when it changes
    private float _cutaway = float.NaN;
    private float _flashSize, _flashAlpha;
    private Vector3 _flashColor;
    private SceneNode[] _flashes = [];
    private int _flashCount;
    private Vector3 _magOffset;
    private bool _magHidden;

    // Build state
    private enum Mode : byte
    {
        Build,
        Refresh,
        Probe,
    }

    private Mode _mode;
    private int _cursor;
    private SceneNode? _group;
    private GunModel.PartGroup _groupKind;

    public GunRig(string name = "gun") => Root = new SceneNode(name);

    /// <summary>Guns (re)built so far, all rigs together (the scene stats tell build frames from steady ones by it).</summary>
    public static int Builds { get; private set; }

    /// <summary>Number of box nodes (for stats and tests).</summary>
    public int PartCount => _slotCount;

    /// <summary>Builds the nodes for <paramref name="def"/> unless they're already built for it. Returns true if it rebuilt.</summary>
    public bool Ensure(WeaponDef def)
    {
        var mesh = GunModel.Mesh(def);
        if (ReferenceEquals(def, Def) && ReferenceEquals(mesh, _mesh))
            return false;
        Build(def);
        return true;
    }

    /// <summary>(Re)builds the gun's nodes. Allocates: done only when the weapon changes.</summary>
    public void Build(WeaponDef def)
    {
        Builds++;
        Root.ClearChildren();
        Def = def;
        _mesh = GunModel.Mesh(def);
        _slotCount = 0;
        _magCount = 0;
        _group = null;
        _cutaway = float.NaN;
        _mode = Mode.Build;
        GunModel.Emit(def, this, 0f);

        // One flash per muzzle (GunMuzzles.cs: the end of each barrel, past its muzzle device); a shot shows the barrels that fired.
        var muzzles = GunMuzzles.Count(def);
        if (_flashes.Length < muzzles)
            _flashes = new SceneNode[muzzles];
        _flashCount = muzzles;
        for (var i = 0; i < muzzles; i++)
        {
            var socket = Root.AddSocket(i == 0 ? "muzzle" : $"muzzle_{i + 1}", GunMuzzles.Local(def, i));
            if (i == 0)
                Muzzle = socket;
            _flashes[i] = socket.Add("flash", Matrix4x4.CreateScale(_flashSize), Renderable.Box(_flashColor, _flashAlpha, 2, Material.Grid, RenderFlags.Blend));
            _flashes[i].Visible = false;
        }
        Flash = _flashes[0];

        // Sockets: where the hands and attachments go (the hand anchors carry the hands' rotations).
        var a = ArmAnchors.For(def);
        Grip = Root.AddSocket("grip", a.Grip, a.GripRot);
        Support = Root.AddSocket("support", a.Support, a.SupportRot);
        MagWell = Root.AddSocket("mag_well", a.MagWell, a.MagRot);
        Root.AddSocket("action", a.Action, a.ActionRot);
        if (_mesh is { } m)
        {
            OpticMount = Root.AddSocket("optic_mount", new Vector3(0, m.Mount, m.OpticZ));
            Root.AddSocket("underbarrel_mount", m.Underbarrel);
            Root.AddSocket("stock", m.StockPoint);
        }
        else
        {
            OpticMount = Root.AddSocket("optic_mount", new Vector3(0, GunModel.SightHeight(def), 0));
            Root.AddSocket("underbarrel_mount", a.Support);
            Root.AddSocket("stock", Vector3.Zero);
        }

        // Does any shape follow the cutaway? Emit once more at full cutaway and compare.
        _cutawayShapes = false;
        _mode = Mode.Probe;
        _cursor = 0;
        GunModel.Emit(def, this, 1f);
        _mode = Mode.Build;
    }

    /// <summary>
    /// Poses the gun for this frame: <paramref name="pose"/> (gun space -> parent space), the optic cutaway (0..1, see
    /// <see cref="GunModel.AdsCutaway"/>), the magazine's reload offset / hidden state, and the muzzle flash.
    /// </summary>
    public void Pose(in Matrix4x4 pose, float cutaway, Vector3 magOffset, bool magHidden, bool flash, float time)
    {
        Root.SetLocalMatrix(pose);
        if (cutaway != _cutaway)
            ApplyCutaway(cutaway);
        if (magOffset != _magOffset || magHidden != _magHidden || _magCount > 0 && _mags[0].Visible == magHidden)
        {
            _magOffset = magOffset;
            _magHidden = magHidden;
            for (var i = 0; i < _magCount; i++)
            {
                _mags[i].SetLocal(magOffset, Quaternion.Identity, Vector3.One);
                _mags[i].Visible = !magHidden;
            }
        }
        var barrels = GunModel.FlashBarrels;
        for (var i = 0; i < _flashCount; i++)
        {
            var f = _flashes[i];
            var on = flash && (barrels & 1UL << (i & 63)) != 0;
            f.Visible = on;
            if (on)
                f.SetLocalMatrix(Matrix4x4.CreateScale(_flashSize) * Matrix4x4.CreateRotationZ(time * 50f) * Matrix4x4.CreateTranslation(0, 0, -_flashSize * 0.4f));
        }
    }

    /// <summary>The flash node of barrel <paramref name="barrel"/> (its parent socket sits at that muzzle; self-test).</summary>
    public SceneNode FlashOf(int barrel) => _flashes[Math.Clamp(barrel, 0, _flashCount - 1)];

    /// <summary>Muzzle flashes (one per barrel).</summary>
    public int FlashCount => _flashCount;

    private void ApplyCutaway(float cutaway)
    {
        _cutaway = cutaway;
        if (_cutawayShapes)
        {
            _mode = Mode.Refresh;
            _cursor = 0;
            GunModel.Emit(Def!, this, cutaway);
            _mode = Mode.Build;
        }
        // Optic housings fade out as the cutaway rises, exactly as GunModel.Shell draws them.
        for (var i = 0; i < _slotCount; i++)
        {
            ref var s = ref _slots[i];
            if (!s.Shell)
                continue;
            var a = s.BaseAlpha * (1f - cutaway);
            var n = s.Node;
            n.Visible = a > 0.02f;
            if (a >= 0.999f)
            {
                n.Renderable.Alpha = 1f;
                n.Renderable.Flags = RenderFlags.None;
            }
            else
            {
                n.Renderable.Alpha = a;
                n.Renderable.Flags = RenderFlags.Blend | RenderFlags.NoDepthWrite;
            }
        }
    }

    // ---- GunModel.IPartSink -----------------------------------------------------------------

    void GunModel.IPartSink.Part(in Matrix4x4 local, Vector3 color, float alpha, int mode, GunModel.PartGroup group, GunModel.EmitFlags flags, int shape)
    {
        switch (_mode)
        {
            case Mode.Refresh:
                if (_cursor < _slotCount)
                    _slots[_cursor].Node.SetLocalMatrix(local);
                _cursor++;
                return;
            case Mode.Probe:
                if (_cursor >= _slotCount || _slots[_cursor].Node.LocalMatrix != local)
                    _cutawayShapes = true;
                _cursor++;
                return;
        }

        // A new group node whenever the kind changes, so the draw order stays exactly the build order.
        if (_group == null || group != _groupKind)
        {
            _group = Root.Add(GroupName(group));
            _groupKind = group;
            if (group == GunModel.PartGroup.Magazine)
            {
                if (_magCount == _mags.Length)
                    Array.Resize(ref _mags, Math.Max(2, _magCount * 2));
                _mags[_magCount++] = _group;
            }
        }
        var shell = (flags & GunModel.EmitFlags.Shell) != 0;
        var r = Renderable.Box(color, alpha, mode, Material.Grid, (flags & GunModel.EmitFlags.Blend) != 0 ? RenderFlags.Blend : RenderFlags.None);
        r.Shape = (ushort)shape;
        var node = _group.Add("part", local, r);
        if (_slotCount == _slots.Length)
            Array.Resize(ref _slots, Math.Max(32, _slotCount * 2));
        _slots[_slotCount++] = new Slot { Node = node, BaseAlpha = alpha, Shell = shell };
    }

    void GunModel.IPartSink.Flash(Vector3 muzzle, float size, Vector3 color, float alpha)
    {
        if (_mode != Mode.Build)
            return;
        // The flash nodes go at every muzzle after the emit (Build); this is the look.
        (_flashSize, _flashColor, _flashAlpha) = (size, color, alpha);
        _group = null;
    }

    private static string GroupName(GunModel.PartGroup g) =>
        g switch
        {
            GunModel.PartGroup.Muzzle => "muzzle_device",
            GunModel.PartGroup.Underbarrel => "underbarrel",
            GunModel.PartGroup.Magazine => "magazine",
            GunModel.PartGroup.Optic => "optic",
            _ => "body",
        };
}
