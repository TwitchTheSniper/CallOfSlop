using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Another player's box body as a scene graph: player (feet at the origin, -Z = facing) -> one body group per stance
/// (torso box, head -> head box and visor), the gun in hand (<see cref="GunRig"/>, posed by <see cref="ThirdPersonArms"/>)
/// with both arms holding it, the health bar, and the corpse box. Draws exactly what <see cref="PlayerRender.DrawImmediate"/>
/// draws, in the same order.
/// </summary>
public sealed class PlayerRig
{
    private static readonly Vector3 Visor = new(0.05f, 0.05f, 0.08f);

    public readonly SceneNode Root;
    public readonly GunRig Gun;
    public readonly ArmNodes Right, Left;
    private readonly SceneNode[] _stances = new SceneNode[3]; // Stand, Crouch, Prone
    private readonly SceneNode _health, _corpse;
    private Vector3 _color = new(float.NaN);

    public PlayerRig(byte id)
    {
        Root = new SceneNode("player_" + id);
        var headH = World.PlayerHeight - World.HeadStart;
        _stances[0] = MakeStance("stand", new Vector3(0.8f, World.HeadStart, 0.5f), new Vector3(0, World.HeadStart / 2, 0),
            new Vector3(0, World.HeadStart + headH / 2, 0), new Vector3(0.45f, headH, 0.45f), new Vector3(0.38f, 0.1f, 0.06f), new Vector3(0, 1.63f, -0.24f));
        const float body = 0.85f, top = 1.2f;
        _stances[1] = MakeStance("crouch", new Vector3(0.8f, body, 0.6f), new Vector3(0, body / 2, 0),
            new Vector3(0, (body + top) / 2, -0.05f), new Vector3(0.45f, top - body, 0.45f), new Vector3(0.38f, 0.1f, 0.06f), new Vector3(0, 1.07f, -0.29f));
        _stances[2] = MakeStance("prone", new Vector3(0.6f, 0.4f, 1.3f), new Vector3(0, 0.2f, 0.15f),
            new Vector3(0, 0.3f, -0.75f), new Vector3(0.4f, 0.38f, 0.4f), new Vector3(0.34f, 0.1f, 0.06f), new Vector3(0, 0.36f, -0.96f));
        Gun = new GunRig();
        Root.Add(Gun.Root);
        Right = new ArmNodes("r", +1, carry: false);
        Left = new ArmNodes("l", -1, carry: false);
        Root.Add(Right.Root);
        Root.Add(Left.Root);
        _health = Root.Add("health_bar");
        _health.Renderable = Renderable.Box(Vector3.One, 1f, 2);
        _corpse = Root.Add("corpse", Matrix4x4.CreateScale(0.8f, 0.25f, 1.8f) * Matrix4x4.CreateTranslation(0, 0.125f, 0), Renderable.Box(Vector3.One));
    }

    private SceneNode MakeStance(string name, Vector3 torsoSize, Vector3 torsoAt, Vector3 headAt, Vector3 headSize, Vector3 visorSize, Vector3 visorAt)
    {
        var g = Root.Add(name);
        g.Add("torso", Matrix4x4.CreateScale(torsoSize) * Matrix4x4.CreateTranslation(torsoAt), Renderable.Box(Vector3.One));
        var head = g.Add("head");
        head.LocalPosition = headAt;
        head.Add("head_box", Matrix4x4.CreateScale(headSize), Renderable.Box(Vector3.One));
        head.Add("visor", Matrix4x4.CreateScale(visorSize) * Matrix4x4.CreateTranslation(visorAt - headAt), Renderable.Box(Visor));
        return g;
    }

    /// <summary>Poses the whole player for this frame (runs <see cref="ThirdPersonArms.Animate"/>, which keeps per-player state).</summary>
    public void Pose(Player p, double time)
    {
        var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
        var at = Matrix4x4.CreateTranslation(p.RenderPosition);
        var body = rot * at;
        Root.SetLocalMatrix(body);
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
        }

        _corpse.Visible = !p.Alive;
        _health.Visible = Gun.Root.Visible = Right.Root.Visible = Left.Root.Visible = p.Alive;
        var stance = p.Alive ? p.RenderStance switch { Stance.Prone => 2, Stance.Crouch => 1, _ => 0 } : -1;
        for (var i = 0; i < 3; i++)
            _stances[i].Visible = i == stance;
        if (!p.Alive)
            return;

        var (gunAt, barY) = stance switch
        {
            2 => (new Vector3(0.25f, 0.3f, -0.9f), 0.9f),
            1 => (new Vector3(0.3f, 0.82f, -0.3f), 1.45f),
            _ => (new Vector3(0.3f, 1.3f, -0.25f), 2.05f),
        };
        var def = p.WeaponDef;
        var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(gunAt) * rot * at;
        ThirdPersonArms.Animate(p, def, gunPose, body, time);
        Gun.Ensure(def);
        Gun.Pose(ThirdPersonArms.LastGunLocal, 0f, Vector3.Zero, false, false, (float)time);
        var (r, l) = ThirdPersonArms.Rigs;
        var colors = ThirdPersonArms.Colors;
        Right.Pose(r, body, colors);
        Left.Pose(l, body, colors);

        var frac = Math.Clamp(p.Health / (float)World.MaxHealth, 0f, 1f);
        _health.SetLocalMatrix(Matrix4x4.CreateScale(0.8f * frac, 0.06f, 0.06f) * Matrix4x4.CreateTranslation(0, barY, 0));
        _health.Renderable.Color = Vector3.Lerp(new Vector3(1, 0.1f, 0.1f), new Vector3(0.2f, 1, 0.2f), frac);
    }
}
