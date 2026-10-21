using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A sentry turret as a scene graph: base (at the entity's position) -> hub (tripod legs, column) and -> head (turns and
/// pitches with the gun: housing, owner's stripe, ammo box, twin barrels, sensor eye, "muzzle" socket with the flash),
/// plus the health bar. Draws what <see cref="StreakRender"/>'s turret code draws, in the same order.
/// </summary>
public sealed class TurretRig
{
    private static readonly Vector3 Gunmetal = new(0.22f, 0.24f, 0.26f);
    private static readonly Vector3 DarkMetal = new(0.12f, 0.13f, 0.14f);

    public readonly SceneNode Base, Hub, Head, Muzzle;
    private readonly SceneNode[] _legs = new SceneNode[3];
    private readonly SceneNode _stripe, _eye, _flash;
    private readonly HealthBarNodes _bar;

    public TurretRig()
    {
        Base = new SceneNode("turret");
        Hub = Base.Add("hub");
        Hub.LocalPosition = new Vector3(0, 0.62f, 0);
        for (var i = 0; i < 3; i++)
            _legs[i] = Hub.Add("leg", Matrix4x4.Identity, Renderable.Box(DarkMetal, material: Material.Metal));
        Hub.Add("column", Matrix4x4.CreateScale(0.18f, 0.5f, 0.18f) * Matrix4x4.CreateTranslation(0, 0.15f, 0), Renderable.Box(Gunmetal, material: Material.Metal));
        Head = Base.Add("head");
        Head.Add("housing", Matrix4x4.CreateScale(0.5f, 0.3f, 0.55f), Renderable.Box(Gunmetal, material: Material.Metal));
        _stripe = Head.Add("stripe", Matrix4x4.CreateScale(0.52f, 0.07f, 0.4f) * Matrix4x4.CreateTranslation(0, 0.12f, 0.05f), Renderable.Box(Vector3.One));
        Head.Add("ammo_box", Matrix4x4.CreateScale(0.3f, 0.2f, 0.3f) * Matrix4x4.CreateTranslation(0, -0.02f, 0.38f), Renderable.Box(DarkMetal));
        for (var s = -1; s <= 1; s += 2)
            Head.Add("barrel", Matrix4x4.CreateScale(0.06f, 0.06f, 0.62f) * Matrix4x4.CreateTranslation(s * 0.09f, 0, -0.55f), Renderable.Box(DarkMetal));
        _eye = Head.Add("eye", Matrix4x4.CreateScale(0.12f, 0.08f, 0.04f) * Matrix4x4.CreateTranslation(0, 0.05f, -0.29f), Renderable.Box(Vector3.One, 1f, 2));
        Muzzle = Head.AddSocket("muzzle", new Vector3(0, 0, -0.95f));
        _flash = Muzzle.Add("flash", Matrix4x4.CreateScale(0.22f), Renderable.Box(new Vector3(1f, 0.85f, 0.4f), 1f, 2));
        _bar = new HealthBarNodes(Base, new Vector3(0, 1.55f, 0));
    }

    public void Pose(StreakEntity e, Vector3 stripe, bool friendly, double now)
    {
        Base.LocalPosition = e.RenderPos;
        for (var i = 0; i < 3; i++)
        {
            var a = e.BaseYaw + i * MathF.Tau / 3;
            _legs[i].SetLocalMatrix(Matrix4x4.CreateScale(0.07f, 0.78f, 0.07f) * Matrix4x4.CreateTranslation(0, -0.39f, 0) * Matrix4x4.CreateRotationX(0.5f)
                                    * Matrix4x4.CreateRotationY(-a));
        }
        Head.SetLocalMatrix(Matrix4x4.CreateRotationX(e.RenderPitch) * Matrix4x4.CreateRotationY(-e.RenderYaw) * Matrix4x4.CreateTranslation(0, 1.05f, 0));
        _stripe.Renderable.Color = stripe;
        var lit = e.Target != byte.MaxValue;
        var eye = friendly ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.15f, 0.1f);
        _eye.Renderable.Color = eye * (lit ? 1.6f : 0.8f);
        _flash.Visible = e.Firing && ((int)(now * 30) & 1) == 0;
        _bar.Pose(e, friendly);
    }
}

/// <summary>
/// An attack drone as a scene graph: body (position + yaw) -> hull, stripe, rotor arms, four hubs with spinning rotors,
/// the gun pod (pitches towards the target, "muzzle" socket with the flash) and the status light; plus the health bar.
/// </summary>
public sealed class DroneRig
{
    private static readonly Vector3 Gunmetal = new(0.22f, 0.24f, 0.26f);
    private static readonly Vector3 DarkMetal = new(0.12f, 0.13f, 0.14f);

    public readonly SceneNode Root, Body, Pod, Muzzle;
    private readonly SceneNode _stripe, _light, _flash;
    private readonly SceneNode[] _rotors = new SceneNode[4];
    private readonly HealthBarNodes _bar;

    public DroneRig()
    {
        Root = new SceneNode("drone");
        Body = Root.Add("body");
        Body.Add("hull", Matrix4x4.CreateScale(0.55f, 0.2f, 0.7f), Renderable.Box(Gunmetal, material: Material.Metal));
        _stripe = Body.Add("stripe", Matrix4x4.CreateScale(0.57f, 0.06f, 0.4f) * Matrix4x4.CreateTranslation(0, 0.11f, 0), Renderable.Box(Vector3.One));
        for (var i = 0; i < 2; i++)
            Body.Add("rotor_arm", Matrix4x4.CreateScale(1.9f, 0.06f, 0.09f) * Matrix4x4.CreateRotationY(MathF.PI / 4 + i * MathF.PI / 2), Renderable.Box(DarkMetal));
        for (var i = 0; i < 4; i++)
        {
            var a = MathF.PI / 4 + i * MathF.PI / 2;
            var hubPos = new Vector3(MathF.Cos(a) * 0.67f, 0.1f, MathF.Sin(a) * 0.67f);
            var hub = Body.Add("rotor_hub");
            hub.LocalPosition = hubPos;
            hub.Add("hub_box", Matrix4x4.CreateScale(0.1f, 0.12f, 0.1f), Renderable.Box(DarkMetal));
            _rotors[i] = hub.Add("rotor", Matrix4x4.Identity, Renderable.Box(new Vector3(0.5f, 0.5f, 0.52f)));
        }
        Pod = Body.Add("pod");
        Pod.Add("pod_box", Matrix4x4.CreateScale(0.16f, 0.14f, 0.5f) * Matrix4x4.CreateTranslation(0, 0, -0.1f), Renderable.Box(DarkMetal));
        Muzzle = Pod.AddSocket("muzzle", new Vector3(0, 0, -0.45f));
        _flash = Muzzle.Add("flash", Matrix4x4.CreateScale(0.25f), Renderable.Box(new Vector3(1f, 0.85f, 0.4f), 1f, 2));
        _light = Body.Add("light", Matrix4x4.CreateScale(0.1f) * Matrix4x4.CreateTranslation(0, -0.08f, -0.38f), Renderable.Box(Vector3.One, 1f, 2));
        _bar = new HealthBarNodes(Root, new Vector3(0, 0.6f, 0));
    }

    public void Pose(StreakEntity e, Vector3 stripe, bool friendly, double now)
    {
        Root.LocalPosition = e.RenderPos;
        Body.SetLocalMatrix(Matrix4x4.CreateRotationY(-e.RenderYaw));
        _stripe.Renderable.Color = stripe;
        var spin = (float)now * 45f;
        for (var i = 0; i < 4; i++)
            _rotors[i].SetLocalMatrix(Matrix4x4.CreateScale(0.62f, 0.015f, 0.07f) * Matrix4x4.CreateRotationY(spin + i) * Matrix4x4.CreateTranslation(0, 0.07f, 0));
        // The pod hangs under the body (straight down, whatever the yaw) and pitches towards the target.
        Pod.SetLocalMatrix(Matrix4x4.CreateRotationX(e.RenderPitch) * Matrix4x4.CreateTranslation(0, -0.25f, 0));
        var light = friendly ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.15f, 0.1f);
        var blink = ((int)(now * 3) & 1) == 0;
        _light.Renderable.Color = light * (blink ? 1.6f : 0.5f);
        _flash.Visible = e.Firing && ((int)(now * 30) & 1) == 0;
        _bar.Pose(e, friendly);
    }
}

/// <summary>The small health bar over a turret or drone (always shown to friends, to enemies once it's damaged).</summary>
internal sealed class HealthBarNodes
{
    private readonly SceneNode _root, _back, _fill;

    public HealthBarNodes(SceneNode parent, Vector3 at)
    {
        _root = parent.Add("health_bar");
        _root.LocalPosition = at;
        _back = _root.Add("back", Matrix4x4.CreateScale(0.8f, 0.06f, 0.06f), Renderable.Box(new Vector3(0.1f, 0.1f, 0.1f), 1f, 2));
        _fill = _root.Add("fill", Matrix4x4.Identity, Renderable.Box(Vector3.One, 1f, 2));
    }

    public void Pose(StreakEntity e, bool friendly)
    {
        var f = Math.Clamp(e.Health / (float)e.MaxHealth, 0f, 1f);
        _root.Visible = f < 0.999f || friendly;
        if (!_root.Visible)
            return;
        _fill.SetLocalMatrix(Matrix4x4.CreateScale(0.8f * f, 0.07f, 0.07f) * Matrix4x4.CreateTranslation(-0.4f * (1 - f), 0, 0));
        _fill.Renderable.Color = Vector3.Lerp(new Vector3(1f, 0.2f, 0.15f), new Vector3(0.3f, 1f, 0.35f), f);
    }
}
