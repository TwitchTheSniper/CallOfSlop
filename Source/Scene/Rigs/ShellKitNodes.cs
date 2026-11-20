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
    private static readonly Vector3 Steel = Hex(0x8b949c), Dark = Hex(0x30363d), Joint = Hex(0x56606a), Light = Hex(0xc4c9ce), Ore = Hex(0x5a4a3c), Bit = Hex(0xc9ced4), Hazard = Hex(0xe0a020);
    private static readonly Vector3 HopMin = new(-0.31f, 1.395f, 0.245f), HopMax = new(0.31f, 2.18f, 0.755f); // ShellParts.HopperMin/Max
    private static readonly Vector3 Elbow = new(0.47f, 1.5f, 0f);

    /// <summary>How an arm box is painted: its own colour, the shell's arm colour (the player's, as its other arm), or the shell's accent.</summary>
    private enum ArmPaint : byte { Own, Arm, Accent }

    /// <summary>One box of an arm module (the Mining Shell part's metres); <see cref="Forearm"/>: it turns with the look about the elbow; <see cref="Spins"/>: it spins while it digs (first person).</summary>
    private readonly record struct ArmBox(Vector3 Lo, Vector3 Hi, Vector3 Color, bool Forearm, bool Spins, ArmPaint Paint = ArmPaint.Own);

    /// <summary>
    /// The drill arm: what other players see on the shell and what its wearer sees in first person (<see cref="ViewModel"/>). In the
    /// shell's arm colour with a steel collar; the bit in its accent, the cutting end in hazard paint (look.md "mark what bites").
    /// </summary>
    private static readonly ArmBox[] DrillArm = MakeDrillArm();

    private static ArmBox[] MakeDrillArm()
    {
        var a = new List<ArmBox>
        {
            new(new(0.36f, 2.04f, -0.15f), new(0.585f, 2.295f, 0.15f), Joint, false, false), // shoulder
            new(new(0.39f, 1.5f, -0.09f), new(0.555f, 2.055f, 0.09f), Steel, false, false, ArmPaint.Arm), // upper arm
            new(new(0.4f, 1.26f, -0.42f), new(0.545f, 1.497f, 0.08f), Joint, true, false, ArmPaint.Arm), // housing
            new(new(0.385f, 1.24f, -0.12f), new(0.56f, 1.52f, 0.03f), Steel, true, false), // motor collar
            new(new(0.39f, 1.505f, -0.41f), new(0.555f, 1.53f, -0.2f), Hazard, true, false), // hazard band
        };
        // The bit, as the Mining Shell part has it (ShellParts.DrillBit): the collar in the shell's accent, the cone and its spiral of
        // flutes spinning as it digs, the point in hazard paint.
        foreach (var (lo, hi, c, _) in PartKit.Generators.ShellParts.DrillBit)
            a.Add(new ArmBox(V(lo), V(hi), c switch { 'h' => Hazard, 'd' => Dark, _ => Bit }, true, c != 'a', c == 'a' ? ArmPaint.Accent : ArmPaint.Own));
        return [.. a];
    }

    private static Vector3 V(double[] v) => new((float)v[0], (float)v[1], (float)v[2]);

    /// <summary>A box's colour on a shell of these colours.</summary>
    private static Vector3 PaintOf(in ArmBox b, Vector3 arm, Vector3 accent) => b.Paint switch { ArmPaint.Arm => arm, ArmPaint.Accent => accent, _ => b.Color };

    /// <summary>The shell's arm colour and accent for a player (the arms' sleeve, BodyColors.Robot's accent).</summary>
    public static (Vector3 Arm, Vector3 Accent) ColoursOf(Player p) => (BodyModel.ArmsFor(p).Sleeve, BodyColors.Robot(p.Color).Accent);

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
        BoxIn(_hopper, new(-0.36f, 1.393f, 0.2f), new(0.36f, 2.25f, 0.24f), Joint); // front wall (steel walls: the ore inside is what is brown)
        BoxIn(_hopper, new(-0.36f, 1.393f, 0.76f), new(0.36f, 2.25f, 0.8f), Light); // back wall (light plate, as the Mining Shell part: look review 2026-11-19)
        BoxIn(_hopper, new(-0.358f, 1.393f, 0.243f), new(-0.322f, 2.2f, 0.757f), Light); // side walls
        BoxIn(_hopper, new(0.322f, 1.393f, 0.243f), new(0.358f, 2.2f, 0.757f), Light);
        BoxIn(_hopper, new(-0.3f, 1.42f, 0.797f), new(-0.26f, 2.2f, 0.826f), Steel); // ribs up its back
        BoxIn(_hopper, new(0.26f, 1.42f, 0.797f), new(0.3f, 2.2f, 0.826f), Steel);
        BoxIn(_hopper, new(-0.364f, 2.253f, 0.196f), new(0.364f, 2.28f, 0.245f), Hazard); // lips
        BoxIn(_hopper, new(-0.364f, 2.253f, 0.755f), new(0.364f, 2.28f, 0.804f), Hazard);
        _ore = _hopper.Add("ore", Matrix4x4.Identity, Renderable.Box(Ore));
        _ore.Visible = false;
        _arm = _root.Add("drill_arm");
        _drill = _arm.Add("drill");
        for (var i = 0; i < DrillArm.Length; i++)
            _armBoxes[i] = BoxIn(DrillArm[i].Forearm ? _drill : _arm, DrillArm[i].Lo, DrillArm[i].Hi, DrillArm[i].Color);
    }

    private readonly SceneNode[] _armBoxes = new SceneNode[DrillArm.Length];
    private Vector3 _armColour = new(float.NaN), _accent;

    // ---- First person (a module with "view" in Shells.json: its wearer sees its own model in the hands' place) ----------------

    /// <summary>Where the drill arm's forearm sits in first person (camera space, -Z forward), about the drill's axis at the elbow.</summary>
    private static readonly Vector3 ViewAt = new(0.28f, -0.38f, -0.25f), ForearmAxis = new(0.4725f, 1.38f, 0f);

    /// <summary>The drill arm's forearm turned from <paramref name="from"/> to point at <paramref name="point"/> (camera space): the crosshair's line at the ground drill's reach, where it digs.</summary>
    private static Quaternion AimAt(Vector3 from, Vector3 point)
    {
        var d = point - from;
        return Quaternion.CreateFromYawPitchRoll(MathF.Atan2(-d.X, -d.Z), MathF.Atan2(d.Y, MathF.Sqrt(d.X * d.X + d.Z * d.Z)), 0f);
    }

    private static readonly Vector3 ViewCard = new(0.12f, -0.17f, -0.42f); // (the screen is wanted near here, built into the drill's housing: ToolDisplay.Fit)

    private static ToolModel? _drillView;
    private static float _drillViewScale;
    private static Vector3 _drillViewArm, _drillViewAccent;

    /// <summary>
    /// The model its wearer sees in first person in place of the tool <paramref name="m"/> forces, at the frame's size
    /// <paramref name="scale"/>: the module's own boxes (the ones the rig draws on the shell), or null when it has no view. Built once
    /// a size; drawn by ToolRender like a tool's, without the person's right arm (<see cref="ToolModel.Arm"/>).
    /// </summary>
    public static ToolModel? ViewModel(ShellModule m, float scale, Vector3 arm, Vector3 accent)
    {
        if (!m.View || m.Kind != ShellModuleKind.Drill)
            return null;
        if (_drillView != null && _drillViewScale == scale && _drillViewArm == arm && _drillViewAccent == accent)
            return _drillView;
        var k = scale / Built;
        var hold = AimAt(ViewAt * k, new Vector3(0, 0, -DiggingTable.Current.Reach * scale)); // (a shell reaches as far as its size: GameSandboxSculpt)
        var boxes = new List<ToolBox>();
        foreach (var b in DrillArm)
            if (b.Forearm)
                boxes.Add(new ToolBox(((b.Lo + b.Hi) * 0.5f - ForearmAxis) * k, (b.Hi - b.Lo) * k, PaintOf(b, arm, accent), b.Spins, false));
        var model = new ToolModel
        {
            Name = m.Name, Key = "shell-" + m.Name.ToLowerInvariant().Replace(' ', '-'), Tool = m.Tool, Boxes = [.. boxes], Hold = hold,
            Grip = new ToolHand(Vector3.Zero, Quaternion.Identity, HandShape.Wrap), Tip = new Vector3(0, 0, -0.9f) * k,
            SpinAxis = Vector3.UnitZ, SpinPivot = Vector3.Zero, Motion = ToolMotion.Push, Amount = 0.02f * k, Rate = 14, Spin = 45,
            SpinBoxes = boxes.Count(b => b.Spin), Arm = true, ViewAt = ViewAt * k,
        };
        // Its screen, built into the drill's housing where it is read (ToolDisplay.Fit): in the arm's colour, with its trim.
        model.Display = ToolDisplay.Fit(model.Boxes, hold, ViewAt * k, ViewCard, ToolDisplay.DefaultSize, ToolDisplay.DefaultRows);
        if (model.Display == null)
            Console.WriteLine($"[shells] the {m.Name} has no place on its housing for a screen: the drill's lines stay on the HUD");
        (_drillView, _drillViewScale, _drillViewArm, _drillViewAccent) = (model, scale, arm, accent);
        return model;
    }

    private static Vector3 Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    private static SceneNode BoxIn(SceneNode parent, Vector3 lo, Vector3 hi, Vector3 color) =>
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
            _arm.SetLocalMatrix(stance switch
            {
                2 => Matrix4x4.CreateTranslation(-Shoulder) * Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(Shoulder),
                1 => Matrix4x4.CreateTranslation(0.1f, 0.15f, 0f), // (crouched: up and out, over the knees)
                _ => Matrix4x4.Identity,
            });
        }
        if (DrillShown)
        {
            var (armC, accent) = ColoursOf(p);
            if (armC != _armColour || accent != _accent)
            {
                (_armColour, _accent) = (armC, accent);
                for (var i = 0; i < DrillArm.Length; i++)
                    _armBoxes[i].Renderable.Color = PaintOf(DrillArm[i], armC, accent);
            }
            // Held a little over the look, never down into the hips or thighs (crouched, over the knees): the reviewer, 2026-11-19.
            var pitch = seated ? 0f : Math.Clamp(p.RenderPitch + (stance == 0 ? 0.12f : stance == 1 ? 0.35f : 0f), stance == 0 ? -0.3f : stance == 1 ? 0.25f : -0.2f, 1.2f) - (stance == 2 ? MathF.PI / 2 : 0f);
            _drill.SetLocalMatrix(Matrix4x4.CreateTranslation(-Elbow) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateTranslation(Elbow));
        }
    }
}
