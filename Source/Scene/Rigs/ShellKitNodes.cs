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

    private readonly SceneNode _root, _hopper, _ore, _arm, _drill;
    private byte _frame = 255;
    private int _capacity = 1;

    /// <summary>Is a drill arm drawn now (the rig hides its own right arm)?</summary>
    public bool DrillShown { get; private set; }

    public ShellKitNodes(SceneNode gear)
    {
        _root = gear.Add("shell_kit");
        _root.SetLocalMatrix(Matrix4x4.CreateScale(1f / Built));
        _root.Visible = false;
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
        BoxIn(_arm, new(0.36f, 2.04f, -0.15f), new(0.585f, 2.295f, 0.15f), Joint); // shoulder
        BoxIn(_arm, new(0.39f, 1.5f, -0.09f), new(0.555f, 2.055f, 0.09f), Steel); // upper arm
        _drill = _arm.Add("drill");
        BoxIn(_drill, new(0.4f, 1.26f, -0.42f), new(0.545f, 1.497f, 0.08f), Joint); // housing
        BoxIn(_drill, new(0.385f, 1.24f, -0.12f), new(0.56f, 1.52f, 0.03f), Dark); // motor collar
        BoxIn(_drill, new(0.425f, 1.3f, -0.62f), new(0.52f, 1.46f, -0.41f), Bit); // bit
        BoxIn(_drill, new(0.445f, 1.33f, -0.8f), new(0.5f, 1.43f, -0.61f), Bit);
        BoxIn(_drill, new(0.46f, 1.355f, -0.9f), new(0.485f, 1.405f, -0.79f), Bit);
        BoxIn(_drill, new(0.39f, 1.505f, -0.41f), new(0.555f, 1.53f, -0.2f), Hazard); // hazard band
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
        if (DrillShown)
        {
            var pitch = seated ? 0f : Math.Clamp(p.RenderPitch, -1.2f, 1.2f);
            _drill.SetLocalMatrix(Matrix4x4.CreateTranslation(-Elbow) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateTranslation(Elbow));
        }
    }
}
