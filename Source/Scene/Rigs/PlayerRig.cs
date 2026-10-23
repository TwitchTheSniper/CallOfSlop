using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Another player's box body as a scene graph: player (feet at the origin, -Z = facing) -> "upper_body" (posed by the legs:
/// hip bob, lean) -> one body group per stance (torso -> <see cref="BodyModel"/> torso parts, head -> head parts and the hat), the legs (pelvis -> thigh -> shin
/// -> foot per side, posed by <see cref="LegAnimator"/>), "upper_gear" (same pose as upper_body) -> the gun in hand
/// (<see cref="GunRig"/>, posed by <see cref="ThirdPersonArms"/>) with both arms holding it, the health bar, and the corpse
/// box, plus the prestige hat (<see cref="HatRender"/>) on each stance's head and on the ground by the corpse. Draws exactly what <see cref="PlayerRender.DrawImmediate"/> draws, in the same order.
/// </summary>
public sealed class PlayerRig
{
    public readonly SceneNode Root;
    public readonly GunRig Gun;
    public readonly ArmNodes Right, Left;
    public readonly SceneNode Pelvis;
    public readonly SceneNode[] Thighs = new SceneNode[2], Shins = new SceneNode[2], Feet = new SceneNode[2];
    private readonly SceneNode _upper, _gear, _pelvisBox;
    private readonly SceneNode[] _stances = new SceneNode[3]; // Stand, Crouch, Prone
    private readonly SceneNode[] _legBoxes = new SceneNode[LegAnimator.LegParts.Length * 2];
    private readonly SceneNode _health, _corpse;
    private readonly SceneNode[] _hats = new SceneNode[3]; // per stance, under the head (after its body model parts)
    private readonly Vector3[] _headSizes = new Vector3[3];
    private readonly BodyParts[] _torsoParts = new BodyParts[3], _headParts = new BodyParts[3];
    private string? _name;
    private byte _id;
    private bool _bodyHat, _bodyBuilt;
    private Vector3 _bodyColor;
    private readonly SceneNode _corpseHat;
    private HatDef? _hat;
    private Vector3 _color = new(float.NaN);

    /// <summary>First-person legs: drawn this far behind the feet so looking down shows them a step ahead of the camera, not under it.</summary>
    public const float FirstPersonBack = 0.1f;

    public PlayerRig(byte id)
    {
        Root = new SceneNode("player_" + id);
        _upper = Root.Add("upper_body");
        for (var i = 0; i < 3; i++)
            _stances[i] = MakeStance(i);

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

    private SceneNode MakeStance(int stance)
    {
        var s = BodyModel.Stances[stance];
        var g = _upper.Add(stance switch { 1 => "crouch", 2 => "prone", _ => "stand" });
        var torso = g.Add("torso");
        torso.SetLocalMatrix(s.TorsoFrame);
        torso.Custom = _torsoParts[stance] = new BodyParts();
        var head = g.Add("head");
        head.LocalPosition = s.HeadAt;
        head.Custom = _headParts[stance] = new BodyParts();
        _hats[stance] = head.Add("hat");
        _hats[stance].Visible = false;
        _headSizes[stance] = s.HeadSize;
        return g;
    }

    /// <summary>The torso and head parts of a stance (0 stand, 1 crouch, 2 prone; self-test).</summary>
    public (BodyParts Torso, BodyParts Head) Body(int stance) => (_torsoParts[stance], _headParts[stance]);

    /// <summary>Rebuilds the three stances' torso and head parts when the look changes (colour, name, hat on or off).</summary>
    private void PoseBody(Player p, bool hat)
    {
        if (_bodyBuilt && hat == _bodyHat && p.Color == _bodyColor && _id == p.Id && ReferenceEquals(_name, p.Name))
            return;
        (_bodyBuilt, _bodyHat, _bodyColor, _id, _name) = (true, hat, p.Color, p.Id, p.Name);
        var v = BodyModel.VariantFor(p.Name, p.Id);
        var c = BodyColors.For(p.Color, v);
        for (var i = 0; i < 3; i++)
            BodyModel.Build(_torsoParts[i], _headParts[i], i, v, c, hat);
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
        BodyBloodRender.Reset(); // marks are placed below for a living body (Source/Render/BodyBloodRender.cs)
        var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
        var at = Matrix4x4.CreateTranslation(p.RenderPosition);
        var body = GravityFrames.Curved ? rot * GravityFrames.Basis(p.RenderPosition) * at : rot * at; // a planet: standing on its curve
        Root.SetLocalMatrix(legsOnly ? Matrix4x4.CreateTranslation(0, 0, FirstPersonBack) * body : body);
        var color = p.Color;
        if (color != _color)
        {
            _color = color;
            _corpse.Renderable.Color = color * 0.4f;
            var lc = LegColors.For(color);
            _pelvisBox.Renderable.Color = lc.Of(LegAnimator.PelvisPart.Paint);
            var parts = LegAnimator.LegParts;
            for (var i = 0; i < _legBoxes.Length; i++)
                _legBoxes[i].Renderable.Color = lc.Of(parts[i % parts.Length].Paint);
        }

        var ragdoll = !p.Alive && Ragdolls.OwnsBody(p.Id); // the body is a ragdoll (Source/Render/Ragdolls.cs), drawn with the debris
        _corpse.Visible = !p.Alive && !legsOnly && !ragdoll;
        PoseHat(p, time, legsOnly || ragdoll);
        if (!legsOnly)
            PoseBody(p, _hat != null);
        Pelvis.Visible = p.Alive;
        _pelvisBox.Visible = !legsOnly;
        _health.Visible = _gear.Visible = p.Alive && !legsOnly;
        var stance = p.Alive && !legsOnly ? p.RenderStance switch { Stance.Prone => 2, Stance.Crouch => 1, _ => 0 } : -1;
        for (var i = 0; i < 3; i++)
            _stances[i].Visible = i == stance;
        if (stance >= 0)
            _torsoParts[stance].ShowDetail = _headParts[stance].ShowDetail = BodyModel.WantsDetail(p.RenderPosition);
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
        BodyBloodRender.PlacePlayer(p, legs, body, stance); // blood marks by health (bound around this rig's draws)

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
