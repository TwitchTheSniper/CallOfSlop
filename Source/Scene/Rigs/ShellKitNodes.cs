using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A robot shell frame's modules on a player's rig (docs/sandbox/design/mining-shell.md; Constructs/Shells.json): an ore hopper on the
/// back slot (its load drawn in the ore's colour, as every peer hears it with Msg.Shell) and a drill arm in the right-arm slot, which
/// points where they look. Drawn from the slot's module kind, not the frame, so a new frame with these modules gets them. Its boxes
/// are in the Mining Shell part's metres (ShellParts.cs) under a node that takes the frame's size back out, since the whole rig is
/// drawn at the frame's size. Built once, nothing allocates per frame.
/// </summary>
public sealed class ShellKitNodes
{
    private const float Built = 1.5f; // the size the boxes below are in (the Mining Shell's)
    private static readonly Vector3 Steel = Hex(0x8b949c), Dark = Hex(0x30363d), Joint = Hex(0x56606a), Ore = Hex(0x5a4a3c), Bit = Hex(0xc9ced4), Hazard = Hex(0xe0a020);
    private static readonly Vector3 HopMin = new(-0.31f, 1.395f, 0.245f), HopMax = new(0.31f, 2.18f, 0.755f); // ShellParts.HopperMin/Max
    private static readonly Vector3 Elbow = new(0.47f, 1.5f, 0f);

    /// <summary>One box of an arm module (the Mining Shell part's metres); <see cref="Forearm"/>: it turns with the look about the elbow; <see cref="Spins"/>: it spins while it digs (first person).</summary>
    private readonly record struct ArmBox(Vector3 Lo, Vector3 Hi, Vector3 Color, bool Forearm, bool Spins);

    /// <summary>The drill arm: what other players see on the shell and what its wearer sees in first person (<see cref="ViewModel"/>).</summary>
    private static readonly ArmBox[] DrillArm =
    [
        new(new(0.36f, 2.04f, -0.15f), new(0.585f, 2.295f, 0.15f), Joint, false, false), // shoulder
        new(new(0.39f, 1.5f, -0.09f), new(0.555f, 2.055f, 0.09f), Steel, false, false), // upper arm
        new(new(0.4f, 1.26f, -0.42f), new(0.545f, 1.497f, 0.08f), Joint, true, false), // housing
        new(new(0.385f, 1.24f, -0.12f), new(0.56f, 1.52f, 0.03f), Dark, true, false), // motor collar
        new(new(0.425f, 1.3f, -0.62f), new(0.52f, 1.46f, -0.41f), Bit, true, true), // bit
        new(new(0.445f, 1.33f, -0.8f), new(0.5f, 1.43f, -0.61f), Bit, true, true),
        new(new(0.46f, 1.355f, -0.9f), new(0.485f, 1.405f, -0.79f), Bit, true, true),
        new(new(0.39f, 1.505f, -0.41f), new(0.555f, 1.53f, -0.2f), Hazard, true, false), // hazard band
    ];

    private static readonly Vector3 Shoulder = new(0.4725f, 2.2f, 0f); // where the drill arm turns at the shoulder when it lies

    /// <summary>Per stance (stand, crouch, prone): the kit's frame, from the standing torso's to that stance's (it lies with the torso).</summary>
    private static readonly Matrix4x4[] StanceFrames = MakeStanceFrames();

    private static Matrix4x4[] MakeStanceFrames()
    {
        var f = new Matrix4x4[3];
        Matrix4x4.Invert(BodyModel.Stances[0].TorsoFrame, out var inv);
        for (var i = 0; i < 3; i++)
            f[i] = Matrix4x4.CreateScale(1f / Built) * inv * BodyModel.Stances[i].TorsoFrame;
        return f;
    }

    private readonly SceneNode _root, _hopper, _ore, _arm, _drill;
    private byte _frame = 255;
    private int _stance = -1;
    private int _capacity = 1;

    /// <summary>Is a drill arm drawn now (the rig hides its own right arm)?</summary>
    public bool DrillShown { get; private set; }

    public ShellKitNodes(SceneNode gear)
    {
        _root = gear.Add("shell_kit");
        _root.Visible = false; // (its matrix: the stance's, in Pose)
        _hopper = _root.Add("hopper");
        BoxIn(_hopper, new(-0.36f, 1.35f, 0.2f), new(0.36f, 1.39f, 0.8f), Dark); // floor
        BoxIn(_hopper, new(-0.36f, 1.393f, 0.2f), new(0.36f, 2.25f, 0.24f), Ore); // front wall
        BoxIn(_hopper, new(-0.36f, 1.393f, 0.76f), new(0.36f, 2.25f, 0.8f), Ore); // back wall
        BoxIn(_hopper, new(-0.358f, 1.393f, 0.243f), new(-0.322f, 2.2f, 0.757f), Ore); // side walls
        BoxIn(_hopper, new(0.322f, 1.393f, 0.243f), new(0.358f, 2.2f, 0.757f), Ore);
        BoxIn(_hopper, new(-0.364f, 2.253f, 0.196f), new(0.364f, 2.28f, 0.245f), Hazard); // lips
        BoxIn(_hopper, new(-0.364f, 2.253f, 0.755f), new(0.364f, 2.28f, 0.804f), Hazard);
        _ore = _hopper.Add("ore", Matrix4x4.Identity, Renderable.Box(Ore));
        _ore.Visible = false;
        _arm = _root.Add("drill_arm");
        _drill = _arm.Add("drill");
        foreach (var b in DrillArm)
            BoxIn(b.Forearm ? _drill : _arm, b.Lo, b.Hi, b.Color);
    }

    // ---- First person (a module with "view" in Shells.json: its wearer sees its own model in the hands' place) ----------------

    /// <summary>Where the drill arm's forearm sits in first person (camera space, -Z forward), about the drill's axis at the elbow.</summary>
    private static readonly Vector3 ViewAt = new(0.3f, -0.31f, -0.2f), ForearmAxis = new(0.4725f, 1.38f, 0f);

    /// <summary>The drill arm's forearm turned in to the crosshair a little and up a little, as it reaches forward.</summary>
    private static readonly Quaternion ViewHold = Quaternion.CreateFromYawPitchRoll(0.06f, 0.1f, 0f);

    private static readonly Vector3 ViewCard = new(0.0f, -0.17f, -0.42f);

    private static ToolModel? _drillView;
    private static float _drillViewScale;

    /// <summary>
    /// The model its wearer sees in first person in place of the tool <paramref name="m"/> forces, at the frame's size
    /// <paramref name="scale"/>: the module's own boxes (the ones the rig draws on the shell), or null when it has no view. Built once
    /// a size; drawn by ToolRender like a tool's, without the person's right arm (<see cref="ToolModel.Arm"/>).
    /// </summary>
    public static ToolModel? ViewModel(ShellModule m, float scale)
    {
        if (!m.View || m.Kind != ShellModuleKind.Drill)
            return null;
        if (_drillView != null && _drillViewScale == scale)
            return _drillView;
        var k = scale / Built;
        var boxes = new List<ToolBox>();
        foreach (var b in DrillArm)
            if (b.Forearm)
                boxes.Add(new ToolBox(((b.Lo + b.Hi) * 0.5f - ForearmAxis) * k, (b.Hi - b.Lo) * k, b.Color, b.Spins, false));
        var model = new ToolModel
        {
            Name = m.Name, Key = "shell-" + m.Name.ToLowerInvariant().Replace(' ', '-'), Tool = m.Tool, Boxes = [.. boxes], Hold = ViewHold,
            Grip = new ToolHand(Vector3.Zero, Quaternion.Identity, HandShape.Wrap), Tip = new Vector3(0, 0, -0.9f) * k,
            SpinAxis = Vector3.UnitZ, SpinPivot = Vector3.Zero, Motion = ToolMotion.Push, Amount = 0.02f * k, Rate = 14, Spin = 45,
            SpinBoxes = boxes.Count(b => b.Spin), Arm = true, ViewAt = ViewAt * k,
        };
        // Its screen: inboard of the arm, low and clear of the crosshair (left of where a hand tool's is, the arm being wider), on a stalk from the housing's inboard side.
        var at = ViewCard;
        var inv = Quaternion.Conjugate(ViewHold);
        var face = Vector3.Normalize(-at);
        var up = Vector3.Normalize(Vector3.UnitY - face * face.Y);
        var mount = new Vector3(-0.075f, 0.02f, -0.2f) * k;
        model.Display = new ToolDisplay(Vector3.Transform(at - ViewAt * k, inv), Vector3.Transform(face, inv), Vector3.Transform(up, inv), ToolDisplay.DefaultSize, ToolDisplay.DefaultRows, mount);
        (_drillView, _drillViewScale) = (model, scale);
        return model;
    }

    private static Vector3 Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    private static void BoxIn(SceneNode parent, Vector3 lo, Vector3 hi, Vector3 color) =>
        parent.Add("box", Matrix4x4.CreateScale(hi - lo) * Matrix4x4.CreateTranslation((lo + hi) * 0.5f), Renderable.Box(color));

    /// <summary>This frame's look: which modules show, the hopper's load, the drill along the look.</summary>
    public void Pose(Player p, bool seated)
    {
        var frame = p.InShell ? p.ShellFrame : (byte)0;
        if (frame != _frame)
        {
            _frame = frame;
            if (frame == 0)
            {
                // A person or a Robot Shell: nothing to draw, and nothing looked up (the shells file is read on first use: never in a frame of someone who isn't in one).
                DrillShown = _hopper.Visible = _arm.Visible = _root.Visible = false;
                return;
            }
            var f = ShellFrames.Of(frame);
            var hop = frame != 0 && f.Modules[1]?.Kind == ShellModuleKind.Hopper;
            DrillShown = frame != 0 && f.Modules[3]?.Kind == ShellModuleKind.Drill;
            _hopper.Visible = hop;
            _arm.Visible = DrillShown;
            _root.Visible = hop || DrillShown;
            _capacity = Math.Max(1, ConstructParts.Find(f.Part)?.BulkCapacity ?? 1);
        }
        if (!_root.Visible)
            return;
        if (_hopper.Visible)
        {
            var fill = Math.Clamp(p.HopperAmount / (float)_capacity, 0f, 1f);
            _ore.Visible = p.HopperAmount > 0;
            if (_ore.Visible)
            {
                var top = HopMin.Y + (HopMax.Y - HopMin.Y) * MathF.Max(fill, 0.03f);
                _ore.SetLocalMatrix(Matrix4x4.CreateScale(HopMax.X - HopMin.X, top - HopMin.Y, HopMax.Z - HopMin.Z) * Matrix4x4.CreateTranslation(0, (HopMin.Y + top) * 0.5f, (HopMin.Z + HopMax.Z) * 0.5f));
                _ore.Renderable.Color = RecyclingTable.Current.ColourOf(p.HopperMaterial);
            }
        }
        var stance = seated ? 0 : BodyModel.StanceIndex(p.RenderStance);
        if (stance != _stance)
        {
            // Crouched it comes down with the torso; prone it lies with it, the drill arm reaching ahead past the head (turned up
            // about the shoulder before the torso lies down, so it points where they look along the ground).
            _stance = stance;
            _root.SetLocalMatrix(StanceFrames[stance]);
            _arm.SetLocalMatrix(stance == 2 ? Matrix4x4.CreateTranslation(-Shoulder) * Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(Shoulder) : Matrix4x4.Identity);
        }
        if (DrillShown)
        {
            var pitch = seated ? 0f : Math.Clamp(p.RenderPitch, -1.2f, 1.2f) - (stance == 2 ? MathF.PI / 2 : 0f);
            _drill.SetLocalMatrix(Matrix4x4.CreateTranslation(-Elbow) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateTranslation(Elbow));
        }
    }
}
