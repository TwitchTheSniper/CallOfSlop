using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A worn robot shell's fitted modules on a player's rig, each drawn from its own part's model (Constructs/Shells.json "part"; the
/// boxes ShellParts.cs makes; the user, 2026-11-19: "the shell parts that are currently equipped should be visually apparent on a
/// possessed shell"). Every slot: the head on the stance's head node (in place of the person's head), the back module on the back,
/// a leg's boxes on the leg's bones (thigh, shin, the hock's metatarsal, foot: by its "joints", so a Runner Leg walks digitigrade),
/// a Hand's forearm over the arm's forearm, and a tool arm (a Welder Arm, a Grip Arm...) out of the shoulder, its forearm turned with
/// the look like the drill arm. The ore hopper and the drill arm have no part: ShellKitNodes draws them. Read from
/// <see cref="ShellFrames.Worn"/>, so a Shell Gantry's swap shows at once on every peer (Msg.Shell refits it). Nodes are made when a
/// module changes, never per frame; nothing allocates in a steady frame.
/// </summary>
public sealed class ShellLooks
{
    /// <summary>A group of box nodes refilled when a module changes: made as needed, the rest hidden.</summary>
    private sealed class Pool(SceneNode parent)
    {
        private readonly List<SceneNode> _nodes = [];
        private int _used;

        public void Begin() => _used = 0;

        public void Add(Vector3 centre, Vector3 size, Vector3 colour)
        {
            var m = Matrix4x4.CreateScale(Vector3.Max(size, new Vector3(0.004f))) * Matrix4x4.CreateTranslation(centre);
            if (_used == _nodes.Count)
                _nodes.Add(parent.Add("module_box", m, Renderable.Box(colour)));
            else
            {
                var n = _nodes[_used];
                n.SetLocalMatrix(m);
                n.Renderable.Color = colour;
                n.Visible = true;
            }
            _used++;
        }

        public void End()
        {
            for (var i = _used; i < _nodes.Count; i++)
                _nodes[i].Visible = false;
        }

        public void Clear()
        {
            Begin();
            End();
        }
    }

    /// <summary>How much wider the rig's body is than the Robot Shell part's (0.8 m shoulders over 0.49 m): a leg's girth on it.</summary>
    private const float LegBulk = 1.5f;

    /// <summary>A head module on the person's head node: its size, and how far its neck's foot (the part's origin) sits under the node.</summary>
    private const float HeadK = 1.45f, HeadDrop = 0.25f;

    /// <summary>A back module (its part's front, its lamps, faces out): its size and where its foot stands, in a person's standing metres.</summary>
    private const float PackK = 1.4f;
    private static readonly Vector3 PackAt = new(0, 0.94f, 0.35f);

    /// <summary>A tool arm, in a person's standing metres (right side; the left mirrors it): its shoulder, upper arm, and the elbow its forearm turns about.</summary>
    private const float ToolK = 1.25f;
    private static readonly Vector3 ToolShoulderLo = new(0.37f, 1.27f, -0.11f), ToolShoulderHi = new(0.52f, 1.45f, 0.11f);
    private static readonly Vector3 ToolUpperLo = new(0.395f, 0.97f, -0.07f), ToolUpperHi = new(0.495f, 1.3f, 0.07f);
    private static readonly Vector3 ToolElbowLo = new(0.385f, 0.94f, -0.085f), ToolElbowHi = new(0.505f, 1.06f, 0.085f);
    public static readonly Vector3 ToolElbow = new(0.445f, 1.0f, 0f);
    private static readonly Vector3 ToolShoulder = new(0.445f, 1.36f, 0f);
    private static readonly Vector3 Steel = Hex(0x8b949c), Joint = Hex(0x56606a), Dark = Hex(0x30363d);

    /// <summary>A Hand's wrist on its part: boxes below it are the hand, which the rig's own hand shapes draw (they hold the tools).</summary>
    private const float HandWrist = 0.115f;

    private readonly Pool[] _heads = new Pool[3];
    private readonly Pool[,] _legs = new Pool[2, 4]; // per leg (0 right, 1 left): thigh, shin, metatarsal, foot
    private readonly Pool[] _fore = new Pool[2]; // a Hand's forearm on the rig's arm (0 right, 1 left)
    private readonly SceneNode _root;
    private readonly Pool _back;
    private readonly SceneNode[] _toolArm = new SceneNode[2], _toolPivot = new SceneNode[2];
    private readonly Pool[] _toolUpper = new Pool[2], _toolFore = new Pool[2];
    private readonly ShellModule?[] _mods = new ShellModule?[ShellFrames.SlotCount];
    private bool _on;
    private float _foreLen = float.NaN, _foreThick;
    private int _stance = -1;

    /// <summary>Is the head module drawn (the rig hides the person's head)?</summary>
    public bool HeadShown { get; private set; }

    /// <summary>Per leg (0 right, 1 left): its module drawn (the rig hides the person's leg boxes).</summary>
    public readonly bool[] LegShown = new bool[2];

    /// <summary>Per arm (0 right, 1 left): a tool arm drawn in its place (the rig hides that arm).</summary>
    public readonly bool[] ToolShown = new bool[2];

    /// <summary>Per arm (0 right, 1 left): a Hand's forearm drawn over the arm's (the rig hides its sleeve's forearm).</summary>
    public readonly bool[] HandShown = new bool[2];

    /// <summary>Per arm (0 right, 1 left): the tool arm's tool, while one is drawn.</summary>
    public readonly ConstructTool?[] ToolOf = new ConstructTool?[2];

    public ShellLooks(SceneNode gear, SceneNode[] heads, SceneNode[] thighs, SceneNode[] shins, SceneNode[] metas, SceneNode[] feet, SceneNode rightFore, SceneNode leftFore)
    {
        for (var i = 0; i < 3; i++)
            _heads[i] = new Pool(heads[i].Add("shell_head"));
        for (var i = 0; i < 2; i++)
        {
            _legs[i, 0] = new Pool(thighs[i].Add("shell_thigh"));
            _legs[i, 1] = new Pool(shins[i].Add("shell_shin"));
            _legs[i, 2] = new Pool(metas[i].Add("shell_meta"));
            _legs[i, 3] = new Pool(feet[i].Add("shell_foot"));
        }
        _fore[0] = new Pool(rightFore.Add("shell_fore"));
        _fore[1] = new Pool(leftFore.Add("shell_fore"));
        _root = gear.Add("shell_looks");
        _back = new Pool(_root.Add("back"));
        for (var j = 0; j < 2; j++)
        {
            _toolArm[j] = _root.Add(j == 0 ? "tool_arm_r" : "tool_arm_l");
            _toolUpper[j] = new Pool(_toolArm[j]);
            _toolPivot[j] = _toolArm[j].Add("forearm");
            _toolFore[j] = new Pool(_toolPivot[j]);
            _toolArm[j].Visible = false;
        }
        _root.Visible = false;
    }

    private static readonly Matrix4x4[] StanceFrames = MakeStanceFrames();

    private static Matrix4x4[] MakeStanceFrames()
    {
        var f = new Matrix4x4[3];
        Matrix4x4.Invert(BodyModel.Stances[0].TorsoFrame, out var inv);
        for (var i = 0; i < 3; i++)
            f[i] = inv * BodyModel.Stances[i].TorsoFrame;
        return f;
    }

    /// <summary>This frame's look: refits when a module changed, turns the tool arms' forearms with the look.</summary>
    public void Pose(Player p, bool seated, ArmRig rightArm)
    {
        var on = p.InShell && p.Alive;
        var kit = on ? ShellFrames.Worn(p).Modules : null;
        var changed = on != _on || rightArm.Fore != _foreLen || rightArm.Thick != _foreThick;
        if (kit != null && !changed)
            for (var i = 0; i < _mods.Length; i++)
                changed |= !ReferenceEquals(kit[i], _mods[i]);
        if (changed)
        {
            _on = on;
            (_foreLen, _foreThick) = (rightArm.Fore, rightArm.Thick);
            for (var i = 0; i < _mods.Length; i++)
                _mods[i] = kit?[i];
            Refit();
        }
        if (!_on || !_root.Visible)
            return;
        var stance = seated ? 0 : BodyModel.StanceIndex(p.RenderStance);
        if (stance != _stance)
        {
            _stance = stance;
            _root.SetLocalMatrix(StanceFrames[stance]);
            for (var j = 0; j < 2; j++)
            {
                var sh = ToolShoulder with { X = ToolShoulder.X * (j == 0 ? 1 : -1) };
                _toolArm[j].SetLocalMatrix(stance == 2 ? Matrix4x4.CreateTranslation(-sh) * Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(sh) : Matrix4x4.Identity);
            }
        }
        if (ToolShown[0] || ToolShown[1])
        {
            var pitch = seated ? 0f : Math.Clamp(p.RenderPitch, -1.2f, 1.2f) - (stance == 2 ? MathF.PI / 2 : 0f);
            for (var j = 0; j < 2; j++)
                if (ToolShown[j])
                {
                    var e = ToolElbow with { X = ToolElbow.X * (j == 0 ? 1 : -1) };
                    _toolPivot[j].SetLocalMatrix(Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateTranslation(e));
                }
        }
    }

    private void Refit()
    {
        _stance = -1;
        var anyKit = false;
        // The head: on each stance's head node, the person's head hidden under it.
        var head = _on ? _mods[0] : null;
        var hd = PartOf(head, ShellModuleKind.Head);
        HeadShown = hd != null;
        for (var s = 0; s < 3; s++)
        {
            var pool = _heads[s];
            pool.Begin();
            if (hd != null)
            {
                var hs = BodyModel.Stances[s].HeadScale;
                foreach (var b in hd.Boxes)
                    if (!b.Hidden)
                        pool.Add((b.Center * HeadK - new Vector3(0, HeadDrop, 0)) * hs, b.Half * 2 * HeadK * hs, b.Color);
            }
            pool.End();
        }
        // The back: a pack with a part (the ore hopper is ShellKitNodes'), turned so its front faces out behind.
        _back.Begin();
        if (_on && PartOf(_mods[1], ShellModuleKind.Pack) is { } pk)
        {
            anyKit = true;
            foreach (var b in pk.Boxes)
                if (!b.Hidden)
                    _back.Add(PackAt + new Vector3(-b.Center.X, b.Center.Y, -b.Center.Z) * PackK, b.Half * 2 * PackK, b.Color);
        }
        _back.End();
        // The arms: slot 3 the right (rig arm 0), slot 2 the left (rig arm 1).
        for (var j = 0; j < 2; j++)
        {
            var m = _on ? _mods[j == 0 ? 3 : 2] : null;
            var side = j == 0 ? 1f : -1f;
            var tool = PartOf(m, ShellModuleKind.Tool);
            ToolShown[j] = tool != null;
            ToolOf[j] = tool != null ? m!.Tool : null;
            _toolArm[j].Visible = tool != null;
            _toolUpper[j].Begin();
            _toolFore[j].Begin();
            if (tool != null)
            {
                anyKit = true;
                Mirrored(_toolUpper[j], ToolShoulderLo, ToolShoulderHi, side, Joint);
                Mirrored(_toolUpper[j], ToolUpperLo, ToolUpperHi, side, Steel);
                Mirrored(_toolUpper[j], ToolElbowLo, ToolElbowHi, side, Dark);
                AddToolForearm(_toolFore[j], tool, side, ToolK);
            }
            _toolUpper[j].End();
            _toolFore[j].End();
            var hand = PartOf(m, ShellModuleKind.Hand);
            HandShown[j] = hand != null && _foreLen > 0;
            _fore[j].Begin();
            if (HandShown[j])
            {
                var top = Top(hand!);
                var along = _foreLen / MathF.Max(top - HandWrist, 0.05f);
                var cross = _foreThick * 0.9f / 0.11f; // (the Shell Hand's forearm is 11 cm across)
                foreach (var b in hand!.Boxes)
                    if (!b.Hidden && b.Center.Y >= HandWrist)
                        _fore[j].Add(new Vector3(b.Center.X * cross * side, -b.Center.Z * cross, (b.Center.Y - top) * along), new Vector3(b.Half.X * cross, b.Half.Z * cross, b.Half.Y * along) * 2, b.Color);
            }
            _fore[j].End();
        }
        // The legs: slot 5 the right (rig leg 0), slot 4 the left (rig leg 1).
        for (var i = 0; i < 2; i++)
        {
            var m = _on ? _mods[i == 0 ? 5 : 4] : null;
            var d = PartOf(m, ShellModuleKind.Leg);
            LegShown[i] = d != null && m!.Hip > 0;
            for (var k = 0; k < 4; k++)
                _legs[i, k].Begin();
            if (LegShown[i])
                FillLeg(i, m!, d!);
            for (var k = 0; k < 4; k++)
                _legs[i, k].End();
        }
        _root.Visible = _on && anyKit;
    }

    /// <summary>
    /// A leg's boxes onto the rig's bones by its "joints": above the knee the thigh, above the hock (or ankle) the shin, above the ankle
    /// (a hock leg) the metatarsal, the rest the foot. Each bone's boxes stretch along it to the rig's length; across, the leg keeps its
    /// own girth times <see cref="LegBulk"/>, so a Heavy Leg stays thick and a Runner Leg thin whatever their heights.
    /// </summary>
    private void FillLeg(int i, ShellModule m, ConstructPartDef d)
    {
        var cross = LegBulk * LegAnimator.StandPelvis / m.Hip;
        var mirror = i == 1 ? -1f : 1f;
        var hock = m.Hock > 0;
        var low = hock ? m.Hock : m.Ankle;
        var metaLen = MathF.Sqrt(m.HockUp * m.HockUp + m.HockBack * m.HockBack);
        var footY = m.Ankle > 0.01f ? LegAnimator.AnkleHeight / m.Ankle : 1f;
        foreach (var b in d.Boxes)
        {
            if (b.Hidden)
                continue;
            var c = b.Center;
            var h = b.Half * 2;
            int bone;
            float top, along;
            if (c.Y >= m.Knee)
                (bone, top, along) = (0, m.Hip, LegAnimator.Thigh / (m.Hip - m.Knee));
            else if (c.Y >= low)
                (bone, top, along) = (1, m.Knee, LegAnimator.Shin / (m.Knee - low));
            else if (hock && c.Y >= m.Ankle)
                (bone, top, along) = (2, m.Hock, metaLen / (m.Hock - m.Ankle));
            else
            {
                _legs[i, 3].Add(new Vector3(c.X * cross * mirror, (c.Y - m.Ankle) * footY, c.Z * cross), new Vector3(h.X * cross, h.Y * footY, h.Z * cross), b.Color);
                continue;
            }
            // A bone's frame: -Z down the bone from its top joint, +Y the way the knee points (forward), +X out to the right.
            _legs[i, bone].Add(new Vector3(c.X * cross * mirror, -c.Z * cross, (c.Y - top) * along), new Vector3(h.X * cross, h.Z * cross, h.Y * along), b.Color);
        }
    }

    /// <summary>
    /// A tool arm's forearm from its part (its elbow coupling on top, the tool's end at its foot) turned to point forward from the elbow:
    /// the part's down is forward (-Z), its front up. Shared by the rig and the first-person view (<see cref="ViewModel"/>).
    /// </summary>
    private static void AddToolForearm(Pool pool, ConstructPartDef d, float side, float k)
    {
        var top = Top(d) - 0.03f;
        foreach (var b in d.Boxes)
            if (!b.Hidden)
                pool.Add(ForearmAt(b.Center, top, side) * k, new Vector3(b.Half.X, b.Half.Z, b.Half.Y) * 2 * k, b.Color);
    }

    private static Vector3 ForearmAt(Vector3 c, float top, float side) => new(c.X * side, -c.Z, c.Y - top);

    private static void Mirrored(Pool pool, Vector3 lo, Vector3 hi, float side, Vector3 colour)
    {
        var c = (lo + hi) * 0.5f;
        pool.Add(c with { X = c.X * side }, hi - lo, colour);
    }

    private static float Top(ConstructPartDef d)
    {
        var top = 0f;
        foreach (var b in d.Boxes)
            if (!b.Hidden)
                top = MathF.Max(top, b.Center.Y + b.Half.Y);
        return top;
    }

    /// <summary>A module's part, when it is of the kind and its part is known (else null: nothing drawn for it here).</summary>
    private static ConstructPartDef? PartOf(ShellModule? m, ShellModuleKind kind) =>
        m != null && m.Kind == kind && m.Part.Length > 0 ? ConstructParts.Find(m.Part) : null;

    // ---- First person (a tool arm's "view"): its forearm where the drill arm's is, aimed down the crosshair ----------------

    private static readonly Vector3 ViewAt = new(0.19f, -0.25f, -0.17f), ViewCard = new(-0.12f, -0.17f, -0.42f);
    private static readonly ToolModel?[] _views = new ToolModel?[256];
    private static readonly float[] _viewScales = new float[256];

    /// <summary>
    /// A tool arm's first-person model at the frame's size <paramref name="scale"/>: the forearm boxes the rig draws, its tip where the
    /// tool's sparks, light or beam come from (as the tool's own model has them). Built once a module and size; null for any other module.
    /// </summary>
    public static ToolModel? ViewModel(ShellModule m, float scale)
    {
        if (!m.View || m.Kind != ShellModuleKind.Tool || m.Tool is not { } tool || ConstructParts.Find(m.Part) is not { } d)
            return null;
        if (_views[m.Index] is { } cached && _viewScales[m.Index] == scale)
            return cached;
        var k = ToolK * scale * 0.7f; // (in first person the arm is nearer the eye than a shoulder is: drawn a little smaller)
        var top = Top(d) - 0.03f;
        var boxes = new List<ToolBox>();
        var reach = 0f;
        foreach (var b in d.Boxes)
            if (!b.Hidden)
            {
                var c = ForearmAt(b.Center, top, 1) * k;
                boxes.Add(new ToolBox(c, new Vector3(b.Half.X, b.Half.Z, b.Half.Y) * 2 * k, b.Color, false, false));
                reach = MathF.Min(reach, c.Z - b.Half.Y * k);
            }
        var at = ViewAt * scale;
        var dd = new Vector3(0, 0, -3f * scale) - at; // (aimed at the crosshair 3 m out)
        var hold = Quaternion.CreateFromYawPitchRoll(MathF.Atan2(-dd.X, -dd.Z), MathF.Atan2(dd.Y, MathF.Sqrt(dd.X * dd.X + dd.Z * dd.Z)), 0f);
        var own = ToolRender.ModelFor(tool);
        var tip = new Vector3(0, 0, reach);
        var model = new ToolModel
        {
            Name = m.Name, Key = "shell-" + m.Name.ToLowerInvariant().Replace(' ', '-'), Tool = tool, Boxes = [.. boxes], Hold = hold,
            Grip = new ToolHand(Vector3.Zero, Quaternion.Identity, HandShape.Wrap), Tip = tip, Motion = own.Motion, MotionAxis = own.MotionAxis,
            Amount = own.Amount, Rate = own.Rate, Sparks = own.Sparks != null ? tip : null, Light = own.Light != null ? tip : null,
            Beam = own.Beam != null ? tip : null, BeamLength = own.BeamLength, Arm = true, ViewAt = at,
        };
        var inv = Quaternion.Conjugate(hold);
        var face = Vector3.Normalize(-ViewCard);
        var up = Vector3.Normalize(Vector3.UnitY - face * face.Y);
        var cardAt = Vector3.Transform(ViewCard - at, inv);
        var cardNormal = Vector3.Transform(face, inv);
        model.Display = new ToolDisplay(cardAt, cardNormal, Vector3.Transform(up, inv), ToolDisplay.DefaultSize, ToolDisplay.DefaultRows, cardAt - cardNormal * 0.012f);
        (_views[m.Index], _viewScales[m.Index]) = (model, scale);
        return model;
    }

    private static Vector3 Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
}
