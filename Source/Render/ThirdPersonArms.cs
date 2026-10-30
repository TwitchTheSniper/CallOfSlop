using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Arms for other players' box bodies: the same IK rig as first person (scaled up to the chunky bodies), with
/// coarse reload / sprint / melee poses driven by replicated state (position changes) and the ReloadFx / MeleeFx
/// events (<see cref="OnReload"/>, <see cref="OnMelee"/>). State is kept per player id; nothing allocates per frame.
/// </summary>
public static class ThirdPersonArms
{
    private struct State
    {
        public double ReloadAt, ReloadDur, MeleeAt, LastTime;
        public Vector3 LastPos;
        public float Speed, SprintW;
        public Matrix4x4 GunPose; // the last drawn gun's pose (gun space -> world) and when, for the tracers (Game.TracerStart)
        public double GunAt;
        public WeaponDef? GunDef;
    }

    private static readonly State[] States = InitStates();

    private static State[] InitStates()
    {
        var s = new State[256];
        for (var i = 0; i < s.Length; i++)
        {
            s[i].ReloadAt = -100;
            s[i].MeleeAt = -100;
        }
        return s;
    }

    // Body space: feet at the origin, -Z = facing. One pair of rigs is reused for every player (posed, then drawn).
    private static readonly ArmRig Right = new(+1, Vector3.Zero, 0.36f, 0.34f, new Vector3(1f, -0.8f, 0.3f), 0.3f, 0.11f, 1.3f);
    private static readonly ArmRig Left = new(-1, Vector3.Zero, 0.36f, 0.34f, new Vector3(-1f, -0.8f, 0.3f), 0.3f, 0.11f, 1.3f);
    private static ArmAnchorSet _anchors;
    private static WeaponDef? _anchorDef;
    private static ArmColors _colors;

    /// <summary>
    /// The barrels whose muzzles flash on the player being drawn (<see cref="Game.FlashBarrels"/>; 0: no flash). Program
    /// sets it around each player's draw and clears it after.
    /// </summary>
    public static ulong ShotFlash;

    /// <summary>A player started reloading (from Msg.Reload on the server or Msg.ReloadFx on clients).</summary>
    public static void OnReload(byte id, float duration, double now)
    {
        States[id].ReloadAt = now;
        States[id].ReloadDur = Math.Max(duration, 0.1f);
    }

    /// <summary>A player swung a melee attack (Msg.MeleeFx).</summary>
    public static void OnMelee(byte id, double now) => States[id].MeleeAt = now;

    /// <summary>
    /// Takes the gun pose DrawPlayer built (gun space -> world, with its scale) and the body transform, adds the coarse
    /// animation, poses both arms onto the gun and returns the gun's final pose. Call <see cref="Draw"/> right after.
    /// </summary>
    public static Matrix4x4 Animate(Player p, WeaponDef def, in Matrix4x4 gunPose, in Matrix4x4 body, double now)
    {
        ref var st = ref States[p.Id];
        if (!ReferenceEquals(def, _anchorDef))
        {
            _anchors = ArmAnchors.For(def);
            _anchorDef = def;
        }

        // Speed from how the drawn position moves (works for interpolated remote players and the server's view alike).
        var dt = (float)(now - st.LastTime);
        var pos = p.RenderPosition;
        if (dt > 0.0005f)
        {
            var dv = GravityFrames.ToLocal(pos, pos - st.LastPos); // (a planet: across its ground)
            var flat = MathF.Sqrt(dv.X * dv.X + dv.Z * dv.Z) / dt;
            if (dt > 0.5f || flat > 40f)
                flat = 0; // first sight or a teleport
            st.Speed = float.Lerp(st.Speed, flat, Math.Clamp(dt * 8f, 0f, 1f));
            st.LastPos = pos;
            st.LastTime = now;
            dt = MathF.Min(dt, 0.1f);
        }
        else
            dt = 0;

        // Reload progress: from the event, or straight from a bot's own timer on the server.
        var reload = -1f;
        if (p.Bot is { ReloadUntil: > 0 } b && b.ReloadUntil > now)
            reload = 1f - (float)((b.ReloadUntil - now) / Math.Max(def.Reload, 0.1f));
        else if (now - st.ReloadAt < st.ReloadDur)
            reload = (float)((now - st.ReloadAt) / st.ReloadDur);
        var melee = now - st.MeleeAt < 0.35 ? (float)((now - st.MeleeAt) / 0.35) : -1f;

        var wantSprint = st.Speed > World.WalkSpeed * 1.2f && reload < 0 && melee < 0 && p.RenderStance == Stance.Stand;
        st.SprintW = Math.Clamp(st.SprintW + (wantSprint ? dt * 4.5f : -dt * 6f), 0f, 1f);
        var s = ViewModelAnimator.Ease(st.SprintW);

        // Coarse poses, about the grip in body space
        float pitch = -0.55f * s, yaw = 0.6f * s, roll = 0;
        var off = new Vector3(-0.05f, -0.15f, 0.05f) * s;
        if (reload >= 0)
        {
            var tl = ViewModelAnimator.Hump(reload, 0, 0.12f, 0.85f, 1);
            pitch += 0.2f * tl;
            roll += -0.5f * tl;
            off += new Vector3(-0.08f, 0.04f, 0.02f) * tl;
        }
        if (melee >= 0)
        {
            var hit = ViewModelAnimator.Hump(melee, 0.2f, 0.45f, 0.55f, 1f);
            var wind = ViewModelAnimator.Hump(melee, 0, 0.2f, 0.2f, 0.45f);
            yaw += 0.9f * hit - 0.3f * wind;
            pitch += 0.3f * hit;
            off += new Vector3(-0.12f, 0.05f, -0.3f) * hit + new Vector3(0.05f, 0, 0.08f) * wind;
        }

        Matrix4x4.Invert(body, out var invBody);
        var inBody = gunPose * invBody;
        var grip = inBody.Translation;
        var anim = Matrix4x4.CreateTranslation(-grip) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationZ(roll)
            * Matrix4x4.CreateTranslation(grip + off);
        LastGunLocal = inBody * anim; // the gun relative to the body (the scene graph's gun node)
        var pose = LastGunLocal * body;
        var gunRot = RotationOf(pose);

        // Hands
        ref readonly var a = ref _anchors;
        var right = new HandTarget(Vector3.Transform(a.Grip, pose), Quaternion.Concatenate(a.GripRot, gunRot), HandShape.Wrap);
        var left = new HandTarget(Vector3.Transform(a.Support, pose), Quaternion.Concatenate(a.SupportRot, gunRot), a.SupportShape);
        if (reload >= 0)
        {
            // Off hand: to the magazine, down out of sight for a new one, back, then home.
            var mag = new HandTarget(Vector3.Transform(a.MagWell, pose), Quaternion.Concatenate(a.MagRot, gunRot), a.MagShape);
            var down = mag with { Pos = mag.Pos + Vector3.TransformNormal(new Vector3(-0.1f, -0.35f, 0.1f), body) };
            var toMag = ViewModelAnimator.E(reload, 0.05f, 0.25f) * (1 - ViewModelAnimator.E(reload, 0.75f, 0.92f));
            left = HandTarget.Blend(left, HandTarget.Blend(mag, down, ViewModelAnimator.Hump(reload, 0.28f, 0.42f, 0.5f, 0.66f)), toMag);
        }

        var (rr, lr) = p.RenderStance switch
        {
            Stance.Crouch => (new Vector3(0.33f, 0.8f, 0.05f), new Vector3(-0.33f, 0.8f, -0.05f)),
            Stance.Prone => (new Vector3(0.25f, 0.32f, -0.45f), new Vector3(-0.25f, 0.32f, -0.5f)),
            _ => (new Vector3(0.33f, 1.38f, 0.1f), new Vector3(-0.33f, 1.38f, 0f)),
        };
        Right.Root = rr;
        Left.Root = lr;
        Right.Solve(right, body, Vector3.Zero);
        Left.Solve(left, body, Vector3.Zero);
        _colors = ArmColors.For(p.Color * 0.75f);
        st.GunPose = pose;
        st.GunAt = now;
        st.GunDef = def;
        return pose;
    }

    /// <summary>
    /// Poses both arms onto hand targets in the world (a Sandbox tool or a carried part instead of a gun; ToolRender.cs),
    /// from the shoulders of <paramref name="p"/>'s stance in <paramref name="body"/>. Call <see cref="Draw"/> right after.
    /// </summary>
    public static void PoseHands(Player p, in HandTarget right, in HandTarget left, in Matrix4x4 body)
    {
        var (rr, lr) = p.RenderStance switch
        {
            Stance.Crouch => (new Vector3(0.33f, 0.8f, 0.05f), new Vector3(-0.33f, 0.8f, -0.05f)),
            Stance.Prone => (new Vector3(0.25f, 0.32f, -0.45f), new Vector3(-0.25f, 0.32f, -0.5f)),
            _ => (new Vector3(0.33f, 1.38f, 0.1f), new Vector3(-0.33f, 1.38f, 0f)),
        };
        Right.Root = rr;
        Left.Root = lr;
        Right.Solve(right, body, Vector3.Zero);
        Left.Solve(left, body, Vector3.Zero);
        _colors = ArmColors.For(p.Color * 0.75f);
    }

    /// <summary>
    /// The pose (gun space -> world) <paramref name="def"/> was last drawn at in this player's hands, if it was drawn in
    /// the last quarter second: shots from another player start at the muzzle of the gun you see them holding.
    /// </summary>
    public static bool DrawnGun(byte id, WeaponDef def, double now, out Matrix4x4 pose)
    {
        ref readonly var st = ref States[id];
        pose = st.GunPose;
        return ReferenceEquals(st.GunDef, def) && now - st.GunAt is >= 0 and < 0.25;
    }

    /// <summary>
    /// Where a player's gun sits before the arm animation (the same frame <see cref="PlayerRig"/> and
    /// <see cref="PlayerRender"/> start from, without the hips' sway): for a gun nobody has drawn yet.
    /// </summary>
    public static Matrix4x4 RestGun(Player p)
    {
        var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
        var at = Matrix4x4.CreateTranslation(p.RenderPosition);
        var body = GravityFrames.Curved ? rot * GravityFrames.Basis(p.RenderPosition) * at : rot * at;
        var gunAt = p.RenderStance switch
        {
            Stance.Prone => new Vector3(0.25f, 0.3f, -0.9f),
            Stance.Crouch => new Vector3(0.3f, 0.82f, -0.3f),
            _ => new Vector3(0.3f, 1.3f, -0.25f),
        };
        return Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(gunAt) * body;
    }

    /// <summary>The gun's pose relative to the body from the last <see cref="Animate"/> (its pose = this * body).</summary>
    public static Matrix4x4 LastGunLocal { get; private set; }

    /// <summary>The arm colours chosen by the last <see cref="Animate"/>.</summary>
    public static ArmColors Colors => _colors;

    /// <summary>The two rigs as posed by the last <see cref="Animate"/> (for the self-test).</summary>
    public static (ArmRig Right, ArmRig Left) Rigs => (Right, Left);

    /// <summary>Draws the arms posed by the last <see cref="Animate"/>.</summary>
    public static void Draw(Renderer g)
    {
        Right.Draw(g, _colors);
        Left.Draw(g, _colors);
    }

    /// <summary>The rotation part of a (uniformly or not) scaled rigid matrix, as a quaternion.</summary>
    public static Quaternion RotationOf(in Matrix4x4 m)
    {
        var x = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        var y = Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23));
        var z = Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33));
        var r = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(r);
    }
}
