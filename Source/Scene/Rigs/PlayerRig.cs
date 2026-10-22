using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Another player's box body as a scene graph: player (feet at the origin, -Z = facing) -> "upper_body" (posed by the legs:
/// hip bob, lean) -> one body group per stance (torso box, head -> head box and visor), the legs (pelvis -> thigh -> shin
/// -> foot per side, posed by <see cref="LegAnimator"/>), "upper_gear" (same pose as upper_body) -> the gun in hand
/// (<see cref="GunRig"/>, posed by <see cref="ThirdPersonArms"/>) with both arms holding it, the health bar, and the corpse
/// box, plus the prestige hat (<see cref="HatRender"/>) on each stance's head and on the ground by the corpse. Draws exactly what <see cref="PlayerRender.DrawImmediate"/> draws, in the same order.
/// </summary>
public sealed class PlayerRig
{
    private static readonly Vector3 Visor = new(0.05f, 0.05f, 0.08f);

    public readonly SceneNode Root;
    public readonly GunRig Gun;
    public readonly ArmNodes Right, Left;
    public readonly SceneNode Pelvis;
    public readonly SceneNode[] Thighs = new SceneNode[2], Shins = new SceneNode[2], Feet = new SceneNode[2];
    private readonly SceneNode _upper, _gear, _pelvisBox;
    private readonly SceneNode[] _stances = new SceneNode[3]; // Stand, Crouch, Prone
    private readonly SceneNode[] _legBoxes = new SceneNode[LegAnimator.LegParts.Length * 2];
    private readonly SceneNode _health, _corpse;
    private readonly SceneNode[] _hats = new SceneNode[3]; // per stance, under the head after the visor
    private readonly Vector3[] _headSizes = new Vector3[3];
    private readonly SceneNode _corpseHat;
    private HatDef? _hat;
    private Vector3 _color = new(float.NaN);

    /// <summary>First-person legs: drawn this far behind the feet so looking down shows them a step ahead of the camera, not under it.</summary>
    public const float FirstPersonBack = 0.1f;

    public PlayerRig(byte id)
    {
        Root = new SceneNode("player_" + id);
        _upper = Root.Add("upper_body");
        var headH = World.PlayerHeight - World.HeadStart;
        const float hip = LegAnimator.StandPelvis;
        _stances[0] = MakeStance("stand", new Vector3(0.8f, World.HeadStart - hip, 0.5f), new Vector3(0, (World.HeadStart + hip) / 2, 0),
            new Vector3(0, World.HeadStart + headH / 2, 0), new Vector3(0.45f, headH, 0.45f), new Vector3(0.38f, 0.1f, 0.06f), new Vector3(0, 1.63f, -0.24f));
        const float body = 0.85f, top = 1.2f, chip = LegAnimator.CrouchPelvis - 0.04f;
        _stances[1] = MakeStance("crouch", new Vector3(0.8f, body - chip, 0.6f), new Vector3(0, (body + chip) / 2, 0),
            new Vector3(0, (body + top) / 2, -0.05f), new Vector3(0.45f, top - body, 0.45f), new Vector3(0.38f, 0.1f, 0.06f), new Vector3(0, 1.07f, -0.29f));
        _stances[2] = MakeStance("prone", new Vector3(0.6f, 0.4f, 0.6f), new Vector3(0, 0.2f, -0.27f),
            new Vector3(0, 0.3f, -0.75f), new Vector3(0.4f, 0.38f, 0.4f), new Vector3(0.34f, 0.1f, 0.06f), new Vector3(0, 0.36f, -0.96f));

        // Legs: pelvis -> thigh -> shin -> foot, boxes in LegAnimator's draw order.
        Pelvis = Root.Add("pelvis");
        var pp = LegAnimator.PelvisPart;
        _pelvisBox = Pelvis.Add("pelvis_box", Matrix4x4.CreateScale(pp.Size) * Matrix4x4.CreateTranslation(pp.Center), Renderable.Box(Vector3.One));
        var parts = LegAnimator.LegParts;
        for (var i = 0; i < 2; i++)
        {
            var sfx = i == 0 ? "_r" : "_l";
            Thighs[i] = Pelvis.Add("thigh" + sfx);
            for (var k = 0; k < parts.Length; k++)
            {
                // each bone is created after its parent's boxes, so the draw order is thigh box, knee, shin box, boot, sole
                if (parts[k].Bone == 2 && Shins[i] is null)
                    Shins[i] = Thighs[i].Add("shin" + sfx);
                if (parts[k].Bone == 3 && Feet[i] is null)
                    Feet[i] = Shins[i].Add("foot" + sfx);
                var bone = parts[k].Bone switch { 1 => Thighs[i], 2 => Shins[i], _ => Feet[i] };
                _legBoxes[i * parts.Length + k] = bone.Add("box", Matrix4x4.CreateScale(parts[k].Size) * Matrix4x4.CreateTranslation(parts[k].Center), Renderable.Box(Vector3.One));
            }
        }

        _gear = Root.Add("upper_gear");
        Gun = new GunRig();
        _gear.Add(Gun.Root);
        Right = new ArmNodes("r", +1, carry: false);
        Left = new ArmNodes("l", -1, carry: false);
        _gear.Add(Right.Root);
        _gear.Add(Left.Root);
        _health = Root.Add("health_bar");
        _health.Renderable = Renderable.Box(Vector3.One, 1f, 2);
        _corpse = Root.Add("corpse", Matrix4x4.CreateScale(0.8f, 0.25f, 1.8f) * Matrix4x4.CreateTranslation(0, 0.125f, 0), Renderable.Box(Vector3.One));
        _corpseHat = Root.Add("corpse_hat");
        _corpseHat.Visible = false;
    }

    private SceneNode MakeStance(string name, Vector3 torsoSize, Vector3 torsoAt, Vector3 headAt, Vector3 headSize, Vector3 visorSize, Vector3 visorAt)
    {
        var g = _upper.Add(name);
        g.Add("torso", Matrix4x4.CreateScale(torsoSize) * Matrix4x4.CreateTranslation(torsoAt), Renderable.Box(Vector3.One));
        var head = g.Add("head");
        head.LocalPosition = headAt;
        head.Add("head_box", Matrix4x4.CreateScale(headSize), Renderable.Box(Vector3.One));
        head.Add("visor", Matrix4x4.CreateScale(visorSize) * Matrix4x4.CreateTranslation(visorAt - headAt), Renderable.Box(Visor));
        var stance = name switch { "crouch" => 1, "prone" => 2, _ => 0 };
        _hats[stance] = head.Add("hat");
        _hats[stance].Visible = false;
        _headSizes[stance] = headSize;
        return g;
    }

    /// <summary>The prestige hat: rebuilt only when it changes, posed on the visible stance's head (or by the corpse).</summary>
    private void PoseHat(Player p, double time, bool legsOnly)
    {
        var hat = legsOnly ? null : HatRender.HatFor(p);
        if (hat != _hat)
        {
            _hat = hat;
            foreach (var h in _hats)
                HatRender.Build(h, hat);
            HatRender.Build(_corpseHat, hat);
            if (hat != null)
                _corpseHat.SetLocalMatrix(HatRender.OnGround(hat));
        }
        var stance = p.RenderStance switch { Stance.Prone => 2, Stance.Crouch => 1, _ => 0 };
        for (var i = 0; i < 3; i++)
        {
            var on = hat != null && p.Alive && i == stance;
            _hats[i].Visible = on;
            if (on)
                _hats[i].SetLocalMatrix(HatRender.OnHead(hat!, _headSizes[i], p, time));
        }
        _corpseHat.Visible = hat != null && !p.Alive;
    }

    /// <summary>The hat node on a stance's head (0 stand, 1 crouch, 2 prone; self-test).</summary>
    public SceneNode HatNode(int stance) => _hats[stance];

    /// <summary>
    /// Poses the whole player for this frame (runs <see cref="LegAnimator.Animate"/> and <see cref="ThirdPersonArms.Animate"/>,
    /// which keep per-player state). <paramref name="legsOnly"/>: the local player's own legs in first person (no torso,
    /// head, gun, arms, health bar or pelvis box; drawn <see cref="FirstPersonBack"/> behind the feet).
    /// </summary>
    public void Pose(Player p, double time, bool legsOnly = false)
    {
        var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
        var at = Matrix4x4.CreateTranslation(p.RenderPosition);
        var body = rot * at;
        Root.SetLocalMatrix(legsOnly ? Matrix4x4.CreateTranslation(0, 0, FirstPersonBack) * body : body);
        var color = p.Color;
        if (color != _color)
        {
            _color = color;
            foreach (var g in _stances)
            {
                g.Child(0).Renderable.Color = color;
                g.Child(1).Child(0).Renderable.Color = color * 1.1f;
            }
            _corpse.Renderable.Color = color * 0.4f;
            var lc = LegColors.For(color);
            _pelvisBox.Renderable.Color = lc.Of(LegAnimator.PelvisPart.Paint);
            var parts = LegAnimator.LegParts;
            for (var i = 0; i < _legBoxes.Length; i++)
                _legBoxes[i].Renderable.Color = lc.Of(parts[i % parts.Length].Paint);
        }

        _corpse.Visible = !p.Alive && !legsOnly;
        PoseHat(p, time, legsOnly);
        Pelvis.Visible = p.Alive;
        _pelvisBox.Visible = !legsOnly;
        _health.Visible = _gear.Visible = p.Alive && !legsOnly;
        var stance = p.Alive && !legsOnly ? p.RenderStance switch { Stance.Prone => 2, Stance.Crouch => 1, _ => 0 } : -1;
        for (var i = 0; i < 3; i++)
            _stances[i].Visible = i == stance;
        if (!p.Alive)
            return;

        var legs = LegAnimator.Animate(p, time);
        _upper.SetLocalMatrix(legs.Upper);
        Pelvis.SetLocalMatrix(legs.PelvisLocal);
        for (var i = 0; i < 2; i++)
        {
            Thighs[i].SetLocalMatrix(legs.ThighLocal[i]);
            Shins[i].SetLocalMatrix(legs.ShinLocal[i]);
            Feet[i].SetLocalMatrix(legs.FootLocal[i]);
        }
        if (legsOnly)
            return;

        _gear.SetLocalMatrix(legs.Upper);
        var upper = legs.Upper * body;
        var (gunAt, barY) = p.RenderStance switch
        {
            Stance.Prone => (new Vector3(0.25f, 0.3f, -0.9f), 0.9f),
            Stance.Crouch => (new Vector3(0.3f, 0.82f, -0.3f), 1.45f),
            _ => (new Vector3(0.3f, 1.3f, -0.25f), 2.05f),
        };
        var def = p.WeaponDef;
        var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(gunAt) * upper;
        ThirdPersonArms.Animate(p, def, gunPose, upper, time);
        Gun.Ensure(def);
        Gun.Pose(ThirdPersonArms.LastGunLocal, 0f, Vector3.Zero, false, false, (float)time);
        var (r, l) = ThirdPersonArms.Rigs;
        var colors = ThirdPersonArms.Colors;
        Right.Pose(r, upper, colors);
        Left.Pose(l, upper, colors);

        var frac = Math.Clamp(p.Health / (float)World.MaxHealth, 0f, 1f);
        _health.SetLocalMatrix(Matrix4x4.CreateScale(0.8f * frac, 0.06f, 0.06f) * Matrix4x4.CreateTranslation(0, barY, 0));
        _health.Renderable.Color = Vector3.Lerp(new Vector3(1, 0.1f, 0.1f), new Vector3(0.2f, 1, 0.2f), frac);
    }
}
