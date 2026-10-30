using System.Numerics;

namespace CallOfSlop;

/// <summary>Where <see cref="ToolRender.Emit{T}"/> puts its boxes: the immediate <see cref="Renderer"/> or a scene <see cref="DrawList"/> (structs: no boxing).</summary>
public interface IToolBoxOut
{
    void Box(in Matrix4x4 model, Vector3 color, int mode);
}

/// <summary>Boxes straight to the renderer.</summary>
public struct ToolGfxOut(Renderer g) : IToolBoxOut
{
    public void Box(in Matrix4x4 model, Vector3 color, int mode) => g.Draw(model, color, 1f, mode);
}

/// <summary>Boxes as self-contained items of a scene draw list.</summary>
public struct ToolListOut(DrawList list) : IToolBoxOut
{
    public void Box(in Matrix4x4 model, Vector3 color, int mode) => list.Add(model, Renderable.Box(color, 1f, mode));
}

/// <summary>One frame of a tool in someone's hands, worked out by <see cref="ToolRender"/> and drawn by <see cref="ToolRender.Emit{T}"/>.</summary>
public struct ToolFrame
{
    public ToolModel? Model; // null: nothing in the strong hand (a part carried in both hands)
    public Matrix4x4 Pose; // tool space -> the drawing space (camera space in first person, the world in third)
    public float Spin, Glow;
    public bool Using; // a timed edit (or a wire) is running: sparks, beam
    public double Now;
    public ConstructPartDef? Held; // a part carried to place
    public Matrix4x4 HeldPose; // the part's own space -> the drawing space (scaled down to fit the hands)
    public HandTarget Right, Left;
}

/// <summary>
/// The Sandbox tools in the hands (docs/sandbox/CONSTRUCTS.md, "Tools in hand"): each tool's box model
/// (<see cref="ConstructTools"/>, Constructs/Tools/*.json) posed and animated for the first-person view (with the view
/// model's arms) and on other players and bots (with <see cref="ThirdPersonArms"/>), in place of the gun while a tool is
/// out. Idle sway and bob, the tool's use motion while a timed edit runs (the wrench cranks, the welder shakes and throws
/// sparks with a flickering light, the linking tool's probe throws a beam, spin parts turn), and equipping: the old tool
/// drops out of sight and the new one comes up. A part carried to place sits in the off hand (both hands with no tool).
/// State is a fixed array per player; nothing allocates per frame.
/// </summary>
public static class ToolRender
{
    /// <summary>Seconds the old tool takes to drop out of sight, and the new one to come up.</summary>
    public const float LowerSeconds = 0.14f, RaiseSeconds = 0.24f;

    /// <summary>The state slot of the local first-person view (players use their id).</summary>
    public const int ViewSlot = 256;

    /// <summary>Everyone's hands this frame (the game's; null outside Sandbox: every player holds their gun).</summary>
    public static ConstructEdits? Hands;

    private struct State
    {
        public ConstructTool Want, From;
        public double ChangeAt, LastTime;
        public float SpinAngle, BobPhase;
        public Vector3 Tip; // the tool's tip in the world, last drawn (lights)
        public double LitUntil;
        public Vector3 LightColour;
    }

    private static readonly State[] States = InitStates();

    private static State[] InitStates()
    {
        var s = new State[ViewSlot + 1];
        for (var i = 0; i < s.Length; i++)
            s[i].ChangeAt = -100;
        return s;
    }

    /// <summary>Forgets every player's equip and animation state (a new match, the self-test).</summary>
    public static void Reset()
    {
        for (var i = 0; i < States.Length; i++)
            States[i] = new State { ChangeAt = -100 };
    }

    /// <summary>A plain grey block for a tool whose model file was refused.</summary>
    private static readonly ToolModel Fallback = new()
    {
        Name = "Tool", Key = "tool", Boxes = [new ToolBox(new Vector3(0, 0.03f, -0.06f), new Vector3(0.045f, 0.1f, 0.2f), new Vector3(0.5f), false, false)],
        Grip = new ToolHand(Vector3.Zero, Quaternion.Identity, HandShape.Wrap), Tip = new Vector3(0, 0.03f, -0.16f),
    };

    /// <summary>The model drawn for a tool (its file's, or the grey block).</summary>
    public static ToolModel ModelFor(ConstructTool tool) => ShowModel ?? ConstructTools.For(tool) ?? Fallback;

    /// <summary>--tool-shots and the self-test: draw this model for whatever tool is out (the tools still to come have no tool to take out yet).</summary>
    public static ToolModel? ShowModel;

    // ---- Equipping --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Which tool <paramref name="slot"/> shows now while it wants <paramref name="want"/>, and how far down it is (0 up, 1
    /// out of sight): on a change the old tool drops for <see cref="LowerSeconds"/>, then the new one comes up for
    /// <see cref="RaiseSeconds"/>. From the gun there is nothing to drop; to the gun the tool drops, then
    /// <see cref="ConstructTool.None"/> (the gun's own equip takes over).
    /// </summary>
    public static ConstructTool Shown(int slot, ConstructTool want, double now, out float down)
    {
        ref var st = ref States[slot];
        if (want != st.Want)
        {
            var shown = ShownOf(st, now, out var d);
            st.From = shown;
            st.Want = want;
            // (part way down already: carry on from there; from the gun: nothing to drop)
            st.ChangeAt = shown == ConstructTool.None ? now - LowerSeconds : now - LowerSeconds * Math.Clamp(d, 0f, 1f);
        }
        return ShownOf(st, now, out down);
    }

    private static ConstructTool ShownOf(in State st, double now, out float down)
    {
        var t = (float)(now - st.ChangeAt);
        if (t < LowerSeconds && st.From != ConstructTool.None)
        {
            down = Math.Clamp(t / LowerSeconds, 0f, 1f);
            return st.From;
        }
        if (st.Want == ConstructTool.None)
        {
            down = 1;
            return ConstructTool.None;
        }
        down = 1f - Math.Clamp((t - LowerSeconds) / RaiseSeconds, 0f, 1f);
        return st.Want;
    }

    // ---- Animation ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The tool's local animation (tool space, about the grip): the use motion, the hold, idle sway, the equip drop. Also
    /// advances its spin parts and works out the glow.
    /// </summary>
    private static Matrix4x4 Animate(ToolModel m, ref State st, double now, bool use, float down, out float spin, out float glow)
    {
        var dt = (float)Math.Clamp(now - st.LastTime, 0, 0.1);
        st.LastTime = now;
        st.SpinAngle = (st.SpinAngle + dt * (use ? m.Spin : m.IdleSpin)) % MathF.Tau;
        spin = st.SpinAngle;
        var t = (float)(now % 10000.0);
        var motion = Matrix4x4.Identity;
        if (use && m.Motion != ToolMotion.None)
        {
            var ph = t * m.Rate * MathF.Tau;
            var a = m.Amount;
            motion = m.Motion switch
            {
                ToolMotion.Ratchet => Matrix4x4.CreateFromAxisAngle(m.MotionAxis, a * MathF.Sin(ph)),
                ToolMotion.Jitter => Matrix4x4.CreateTranslation(MathF.Sin(ph) * a, MathF.Sin(ph * 1.37f + 1f) * a, MathF.Sin(ph * 0.71f + 2f) * a),
                ToolMotion.Push => Matrix4x4.CreateTranslation(0, 0, -a * (0.5f - 0.5f * MathF.Cos(ph))),
                ToolMotion.Sweep => Matrix4x4.CreateRotationY(a * MathF.Sin(ph)),
                _ => Matrix4x4.Identity,
            };
        }
        glow = use ? 0.8f + 0.2f * MathF.Sin(t * 53f) * MathF.Sin(t * 31f) : 0.45f + 0.08f * MathF.Sin(t * 2.1f);
        var e = ViewModelAnimator.Ease(Math.Clamp(down, 0f, 1f));
        var sway = Matrix4x4.CreateRotationX(MathF.Sin(t * 1.7f + 0.7f) * 0.01f - 1.0f * e) * Matrix4x4.CreateRotationZ(MathF.Sin(t * 0.9f + 1.3f) * 0.012f + 0.3f * e)
                   * Matrix4x4.CreateTranslation(0, MathF.Sin(t * 1.7f) * 0.002f - 0.3f * e, 0.06f * e);
        return motion * Matrix4x4.CreateFromQuaternion(m.Hold) * sway;
    }

    /// <summary>A part's own space -> a box <paramref name="fit"/> metres across at most, centred on its bounds.</summary>
    private static Matrix4x4 HeldScale(ConstructPartDef d, float fit, out float halfHeight, out float halfWidth)
    {
        var ext = d.BoundsMax - d.BoundsMin;
        var s = MathF.Min(1f, fit / MathF.Max(1e-3f, MathF.Max(ext.X, MathF.Max(ext.Y, ext.Z))));
        halfHeight = ext.Y * s * 0.5f;
        halfWidth = MathF.Max(ext.X, ext.Z) * s * 0.5f;
        return Matrix4x4.CreateTranslation(-(d.BoundsMin + d.BoundsMax) * 0.5f) * Matrix4x4.CreateScale(s);
    }

    /// <summary>
    /// The hands on a carried part centred at <paramref name="at"/> (in <paramref name="space"/>, whose rotation is
    /// <paramref name="rot"/>): in both hands, one on each side, palms in; else the off hand under it.
    /// </summary>
    private static void Carry(ref ToolFrame f, Vector3 at, float hh, float hw, bool two, Quaternion rot, in Matrix4x4 space)
    {
        if (two)
        {
            var side = hw * 0.9f + 0.03f;
            f.Left = new HandTarget(Vector3.Transform(at + new Vector3(-side, -hh * 0.3f, 0), space),
                Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), rot), HandShape.Flat);
            f.Right = new HandTarget(Vector3.Transform(at + new Vector3(side, -hh * 0.3f, 0), space),
                Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2), rot), HandShape.Flat);
        }
        else
            f.Left = new HandTarget(Vector3.Transform(at - new Vector3(0, hh + 0.012f, 0), space), rot, HandShape.Under);
    }

    private static HandTarget OnTool(in ToolHand h, in Matrix4x4 pose, Quaternion rot) =>
        new(Vector3.Transform(h.At, pose), Quaternion.Concatenate(h.Rot, rot), h.Shape);

    // ---- First person -------------------------------------------------------------------------------------------------

    /// <summary>What the first-person view needs from the game this frame.</summary>
    public struct ViewInput
    {
        public ConstructTool Tool;
        public ConstructPartDef? Held;
        public bool Using;
        public double Now;
        public Vector3 Velocity;
        public bool Grounded;
    }

    /// <summary>
    /// The first-person view (camera space, -Z forward): the tool low on the right, a carried part in the off hand.
    /// Returns false when nothing is shown (the gun's turn). Then pose the arms with <paramref name="right"/> and
    /// <paramref name="left"/> and draw with <see cref="Emit{T}"/>.
    /// </summary>
    public static bool FirstPerson(in ViewInput i, ref ToolFrame f, out ConstructTool shown)
    {
        ref var st = ref States[ViewSlot];
        shown = Shown(ViewSlot, i.Tool, i.Now, out var down);
        f = default;
        f.Now = i.Now;
        if (shown == ConstructTool.None && i.Held == null)
        {
            st.LastTime = i.Now;
            return false;
        }
        // Walk bob (the view model's, smaller).
        var dt = (float)Math.Clamp(i.Now - st.LastTime, 0, 0.1);
        var speed = MathF.Sqrt(i.Velocity.X * i.Velocity.X + i.Velocity.Z * i.Velocity.Z);
        var move = i.Grounded ? MathF.Min(speed / World.WalkSpeed, 1.5f) : 0f;
        st.BobPhase = (st.BobPhase + dt * 10f) % (MathF.Tau * 2);
        var bob = new Vector3(MathF.Cos(st.BobPhase * 0.5f) * 0.008f, MathF.Sin(st.BobPhase) * 0.012f, 0) * move;
        if (shown != ConstructTool.None)
        {
            var m = ModelFor(shown);
            var local = Animate(m, ref st, i.Now, i.Using && shown == i.Tool, down, out f.Spin, out f.Glow);
            f.Model = m;
            f.Using = i.Using && shown == i.Tool && down <= 0.01f;
            f.Pose = local * Matrix4x4.CreateTranslation(new Vector3(0.21f, -0.22f, -0.44f) + bob);
            var rot = ThirdPersonArms.RotationOf(f.Pose);
            f.Right = OnTool(m.Grip, f.Pose, rot);
            f.Left = m.Support is { } sup ? OnTool(sup, f.Pose, rot) : new HandTarget(new Vector3(-0.24f, -0.52f, -0.2f), Quaternion.Identity, HandShape.Flat);
        }
        else
        {
            st.LastTime = i.Now;
            f.Right = new HandTarget(new Vector3(0.26f, -0.52f, -0.2f), Quaternion.Identity, HandShape.Flat);
        }
        if (i.Held is { } held)
        {
            // In the off hand, low on the left; with no tool out, in both hands in the middle.
            var two = f.Model == null;
            var at = new Vector3(two ? 0f : -0.17f, two ? -0.17f : -0.16f, two ? -0.55f : -0.5f) + bob;
            var e = ViewModelAnimator.Ease(f.Model == null ? 0f : MathF.Min(down, 1f));
            at.Y -= 0.25f * e;
            var scale = HeldScale(held, two ? 0.24f : 0.16f, out var hh, out var hw);
            f.Held = held;
            f.HeldPose = scale * Matrix4x4.CreateRotationY(two ? 0.2f : 0.45f) * Matrix4x4.CreateTranslation(at);
            Carry(ref f, at, hh, hw, two, Quaternion.Identity, Matrix4x4.Identity);
        }
        return true;
    }

    /// <summary>Notes where the first-person tool's tip is in the world (its light; <paramref name="cameraToWorld"/> is the inverse view).</summary>
    public static void NoteViewTip(in ToolFrame f, in Matrix4x4 cameraToWorld) => NoteTip(ViewSlot, f, cameraToWorld);

    private static void NoteTip(int slot, in ToolFrame f, in Matrix4x4 toWorld)
    {
        ref var st = ref States[slot];
        if (f.Model is not { Light: { } light } m || !f.Using)
            return;
        st.Tip = Vector3.Transform(Vector3.Transform(m.Tip, f.Pose), toWorld);
        st.LitUntil = f.Now + 0.1;
        st.LightColour = light;
    }

    /// <summary>This frame's tool lights (a welding arc), from where each lit tool's tip was last drawn; flickering. Lit maps only (LightGrid).</summary>
    public static void AddLights(double now)
    {
        var t = (float)(now % 10000.0);
        for (var i = 0; i < States.Length; i++)
        {
            ref var st = ref States[i];
            if (now >= st.LitUntil || now < st.LitUntil - 1)
                continue;
            var flicker = 0.75f + 0.25f * MathF.Sin(t * 47f + i) * MathF.Sin(t * 29f);
            LightGrid.AddDynamic(st.Tip, st.LightColour * (1.6f * flicker), 4.5f);
        }
    }

    // ---- Third person -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Another player's (or a bot's) tool: the gun pose PlayerRig / PlayerRender built (gun space -> world, scale 1.3) is
    /// where the tool sits; poses both arms (<see cref="ThirdPersonArms.PoseHands"/>). Returns false when they hold their
    /// gun (outside Sandbox, no tool and no part, seated, dead).
    /// </summary>
    public static bool ThirdPerson(Player p, in Matrix4x4 gunPose, in Matrix4x4 upper, double time, ref ToolFrame f)
    {
        f = default;
        if (Hands is not { } hands || !p.Alive)
            return false;
        ref var h = ref hands.Hand(p.Id);
        if (h.Seated)
            return false;
        ref var st = ref States[p.Id];
        var shown = Shown(p.Id, h.Tool, time, out var down);
        var held = ConstructParts.Get(h.Held);
        if (shown == ConstructTool.None && held == null)
        {
            st.LastTime = time;
            return false;
        }
        f.Now = time;
        var use = h.Edit != EditKind.None && shown == h.Tool;
        var (rest, side) = p.RenderStance switch
        {
            Stance.Prone => (0.3f, 0.32f),
            Stance.Crouch => (0.82f, 0.36f),
            _ => (1.3f, 0.38f),
        };
        if (shown != ConstructTool.None)
        {
            var m = ModelFor(shown);
            var local = Animate(m, ref st, time, use, down, out f.Spin, out f.Glow);
            f.Model = m;
            f.Using = use && down <= 0.01f;
            f.Pose = local * gunPose;
            var rot = ThirdPersonArms.RotationOf(f.Pose);
            f.Right = OnTool(m.Grip, f.Pose, rot);
            f.Left = m.Support is { } sup ? OnTool(sup, f.Pose, rot)
                : new HandTarget(Vector3.Transform(new Vector3(-side, rest - 0.5f, 0.02f), upper), ThirdPersonArms.RotationOf(upper), HandShape.Flat);
            NoteTip(p.Id, f, Matrix4x4.Identity);
        }
        else
        {
            st.LastTime = time;
            f.Right = new HandTarget(Vector3.Transform(new Vector3(side, rest - 0.5f, 0.02f), upper), ThirdPersonArms.RotationOf(upper), HandShape.Flat);
        }
        if (held != null)
        {
            var two = f.Model == null;
            var at = new Vector3(two ? 0f : -0.14f, rest - 0.12f - 0.3f * ViewModelAnimator.Ease(two ? 0f : Math.Clamp(down, 0f, 1f)), p.RenderStance == Stance.Prone ? -0.75f : -0.45f);
            var scale = HeldScale(held, two ? 0.5f : 0.36f, out var hh, out var hw);
            f.Held = held;
            f.HeldPose = scale * Matrix4x4.CreateRotationY(0.3f) * Matrix4x4.CreateTranslation(at) * upper;
            Carry(ref f, at, hh, hw, two, ThirdPersonArms.RotationOf(upper), upper);
        }
        ThirdPersonArms.PoseHands(p, f.Right, f.Left, upper);
        return true;
    }

    // ---- Drawing -----------------------------------------------------------------------------------------------------

    /// <summary>Boxes <see cref="Emit{T}"/> draws for a frame (the self-test checks the count).</summary>
    public static int BoxCount(in ToolFrame f)
    {
        var n = f.Held?.Boxes.Length ?? 0;
        if (f.Model is { } m)
        {
            n += m.Boxes.Length;
            if (f.Using && m.Sparks != null)
                n += Sparks;
            if (f.Using && m.Beam != null)
                n++;
        }
        return n;
    }

    private const int Sparks = 10;

    /// <summary>Draws the tool (its spin parts turned, glow parts lit), its sparks and beam while in use, and the carried part.</summary>
    public static void Emit<T>(ref T o, in ToolFrame f) where T : struct, IToolBoxOut
    {
        if (f.Model is { } m)
        {
            var spin = m.SpinBoxes > 0 ? Matrix4x4.CreateTranslation(-m.SpinPivot) * Matrix4x4.CreateFromAxisAngle(m.SpinAxis, f.Spin) * Matrix4x4.CreateTranslation(m.SpinPivot) : Matrix4x4.Identity;
            foreach (ref readonly var b in m.Boxes.AsSpan())
            {
                var local = Matrix4x4.CreateScale(b.Size) * Matrix4x4.CreateTranslation(b.Center);
                if (b.Spin)
                    local *= spin;
                if (b.Glow)
                    o.Box(local * f.Pose, b.Color * f.Glow, 2);
                else
                    o.Box(local * f.Pose, b.Color, 0);
            }
            if (f.Using && m.Sparks is { } sc)
            {
                var t = f.Now * 7.0;
                for (var k = 0; k < Sparks; k++)
                {
                    var cycle = t + k / (double)Sparks;
                    var life = (float)(cycle - Math.Floor(cycle));
                    var seed = (int)Math.Floor(cycle) * 31 + k * 7919;
                    var dir = new Vector3(Hash(seed) * 2 - 1, Hash(seed + 1) * 1.4f - 0.2f, Hash(seed + 2) * 2 - 1.3f);
                    var at = m.Tip + dir * (life * 0.16f) + new Vector3(0, -0.16f * life * life, 0);
                    var size = 0.014f * (1f - 0.6f * life);
                    o.Box(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(at) * f.Pose, sc * (1.4f - 0.8f * life), 2);
                }
            }
            if (f.Using && m.Beam is { } bc)
            {
                var len = m.BeamLength;
                var pulse = 0.75f + 0.25f * MathF.Sin((float)(f.Now % 1000.0) * 25f);
                o.Box(Matrix4x4.CreateScale(0.006f, 0.006f, len) * Matrix4x4.CreateTranslation(m.Tip - new Vector3(0, 0, len * 0.5f)) * f.Pose, bc * pulse, 2);
            }
        }
        if (f.Held is { } d)
            foreach (ref readonly var b in d.Boxes.AsSpan())
                o.Box(Matrix4x4.CreateScale(b.Half * 2) * Matrix4x4.CreateTranslation(b.Center) * f.HeldPose, b.Color, 0);
    }

    /// <summary>A number in [0, 1) from an integer (the sparks' directions: the same every run).</summary>
    private static float Hash(int a)
    {
        var x = (uint)a * 747796405u + 2891336453u;
        x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
        x = (x >> 22) ^ x;
        return (x & 0xFFFFFF) / 16777216f;
    }
}

/// <summary>A player rig's tool node (PlayerRig.cs): emits the frame <see cref="ToolRender.ThirdPerson"/> worked out for it.</summary>
public sealed class ToolNode : IRenderable
{
    public ToolFrame Frame;

    public void Emit(DrawList list, SceneNode node)
    {
        var o = new ToolListOut(list);
        ToolRender.Emit(ref o, Frame);
    }
}
