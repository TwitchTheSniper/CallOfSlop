using System.Numerics;

namespace CallOfSlop;

/// <summary>A zombie's animation clocks (both sides; drawing only, never gameplay). See Source/Render/ZombieAnim.cs.</summary>
public sealed partial class Zombie
{
    /// <summary>
    /// Tearing: the board cycle, 0-255 (the board comes off at 255); climbing: the climb's progress. The server works it out
    /// every tick; clients get it in <see cref="Msg.ZEnts"/> in place of the yaw byte (protocol 43). <see cref="AnimByteTick"/>: the tick it was true at.
    /// </summary>
    public byte AnimByte;

    public double AnimByteTick;

    /// <summary>The state the animation last saw, the one before it, and the server tick it changed at.</summary>
    public ZState AnimState = (ZState)255, AnimPrevState = (ZState)255;

    public double AnimStateTick;

    /// <summary>Server ticks: it arrived at its window, started a swipe, landed from a vault, was hurt.</summary>
    public double WindowTick = -1e9, AttackTick = -1e9, LandTick = -1e9, FlinchTick = -1e9;

    /// <summary>How hard the last hit was (0.35-1), and the health byte last seen.</summary>
    public float FlinchAmt;

    public byte AnimHealth = 255;

    /// <summary>How fast it moves as drawn (smoothed, m/s), and where it was drawn last frame.</summary>
    public float AnimSpeed;

    public Vector3 AnimPrevPos;
    public bool AnimPrevSet;

    /// <summary>How much it is moving, 0 standing .. 1 at its speed (scales the stride).</summary>
    public float AnimMove;

    /// <summary>The smoothed pose targets (a change of state blends over a third of a second) and when they were last updated (local clock).</summary>
    public ZombieTargets AnimTargets;

    public double AnimSmoothAt = -1e9;

    /// <summary>Drawing: the last pose, where the body was for it, its state and the frame it was worked out (animation level of detail).</summary>
    public ZombiePose AnimPose;

    public Vector3 AnimPoseAt;
    public ZState AnimPoseState = (ZState)255;
    public long AnimPoseFrame = -100;
}

/// <summary>
/// Zombie animation sync (docs/zombies/ZOMBIES-MODE.md, "Animation"): the server's per-tick animation byte (the board
/// cycle while tearing, the climb's progress while climbing), the state / swipe / landing / hurt clocks kept in server
/// ticks on both sides (the host from its own ticks, clients from the snapshot ticks in <see cref="Msg.ZEnts"/>), and the
/// pose input for drawing. Clients see what the host sees: every clock is the server's, read at the interpolation tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>The tick zombies are drawn at: the host blends its last two ticks, clients interpolate behind the snapshots.</summary>
    public double ZAnimTick => SelfTestAnimTick ?? (IsServer ? (double)ServerTick - 1 + _alpha : RenderTick);

    /// <summary>Self-test: draw zombies at this tick instead (to compare a host and a client at the same moment).</summary>
    public double? SelfTestAnimTick;

    /// <summary>Self-test: the per-tick animation update for every zombie (as the AI tick does it), without thinking.</summary>
    public void SelfTestZombieAnimTick(double now)
    {
        foreach (var z in Horde)
            ZombieAnimTick(z, now);
    }

    /// <summary>A zombie's health as the byte <see cref="Msg.ZEnts"/> carries.</summary>
    public static byte ZombieHealthByte(Zombie z) => (byte)Math.Clamp(z.MaxHealth > 0 ? z.Health * 255 / z.MaxHealth : 0, 0, 255);

    /// <summary>Does the yaw byte of <see cref="Msg.ZEnts"/> carry the animation byte for this state (protocol 43)?</summary>
    public static bool ZAnimByteState(ZState s, int window) => s is ZState.Tearing or ZState.Climbing && window is >= 0 and < 255;

    /// <summary>Server, every tick: the animation byte and the clocks.</summary>
    private void ZombieAnimTick(Zombie z, double now)
    {
        if (z.State == ZState.Tearing)
            z.AnimByte = (byte)MathF.Round(Math.Clamp(1f - (float)((z.NextTear - now) / Math.Max(0.05, ZRules.BoardTearSeconds)), 0f, 1f) * 255f);
        else if (z.State == ZState.Climbing)
            z.AnimByte = (byte)MathF.Round(Math.Clamp((float)((now - z.StateAt) / Math.Max(0.05, ClimbSecondsFor(z))), 0f, 1f) * 255f);
        else
            z.AnimByte = 0;
        z.AnimByteTick = ServerTick;
        ZombieAnimTrack(z, ServerTick, ZombieHealthByte(z));
    }

    /// <summary>Both sides: notes state changes, swipes, landings and hurts at server tick <paramref name="tick"/>.</summary>
    public static void ZombieAnimTrack(Zombie z, double tick, byte health)
    {
        var first = z.AnimState == (ZState)255;
        if (z.State != z.AnimState)
        {
            var prev = z.AnimState;
            z.AnimPrevState = prev;
            z.AnimState = z.State;
            z.AnimStateTick = tick;
            if (z.State == ZState.Attack)
                z.AttackTick = tick;
            if (prev == ZState.Climbing && z.State != ZState.Dead)
                z.LandTick = tick;
            var atWindow = z.State == ZState.Tearing || (z.State == ZState.Attack && z.Window >= 0);
            var wasAtWindow = prev is ZState.Tearing || (prev == ZState.Attack && z.Window >= 0);
            if (atWindow && (!wasAtWindow || first))
                z.WindowTick = tick;
        }
        if (!first && health < z.AnimHealth && z.State != ZState.Dead)
        {
            z.FlinchTick = tick;
            z.FlinchAmt = Math.Clamp((z.AnimHealth - health) / 255f * 5f, 0.35f, 1f);
        }
        z.AnimHealth = health;
    }

    /// <summary>Both sides, every frame: how fast the zombie moves as drawn, and its walk cycle (keyed to that speed, so the feet don't slide).</summary>
    private void ZombieAnimFrame(Zombie z, float dt)
    {
        if (dt > 0 && z.AnimPrevSet)
        {
            var d = z.RenderPos - z.AnimPrevPos;
            d.Y = 0;
            var v = d.Length() / dt;
            if (v > 9f)
                v = z.AnimSpeed; // a snap (spawn, teleport): ignore
            z.AnimSpeed += (v - z.AnimSpeed) * MathF.Min(1f, dt * 8f);
        }
        z.AnimPrevPos = z.RenderPos;
        z.AnimPrevSet = true;
        z.AnimMove = Math.Clamp(z.AnimSpeed / MathF.Max(0.3f, 0.6f * ZombieNominalSpeed(z.Speed) * (z.Limbs != 0 ? LimbSpeedMult(z) : 1f)), 0f, 1f);
        if (z.State is ZState.ToWindow or ZState.Chase or ZState.Attack)
        {
            z.Gait += dt * z.AnimSpeed * MathF.Tau / (4f * ZombieAnim.StrideHalf(z.Speed, z.Limbs) * MathF.Max(0.35f, z.AnimMove));
            if (z.Gait > 1000f)
                z.Gait -= MathF.Tau * 150; // keep it small (a whole number of cycles)
        }
    }

    /// <summary>The nominal speed of a zombie's class (m/s).</summary>
    public float ZombieNominalSpeed(ZSpeed s) => s switch { ZSpeed.Sprint => ZRules.SprintSpeed, ZSpeed.Run => ZRules.RunSpeed, _ => ZRules.WalkSpeed };

    /// <summary>The tearing cycle (0-1) of a zombie at a window now, as drawn (for the board's wobble); negative if it isn't tearing.</summary>
    public float ZombieTearPhase(Zombie z)
    {
        if (z.State != ZState.Tearing || z.Window < 0)
            return -1;
        var ph = z.AnimByte / 255f + (float)((ZAnimTick - z.AnimByteTick) * World.TickDt / Math.Max(0.05, ZRules.BoardTearSeconds));
        return ph - MathF.Floor(ph);
    }

    /// <summary>
    /// The pose input for drawing zombie <paramref name="z"/> now (<paramref name="now"/>: the local clock, for idle sway):
    /// its state, clocks and window from what this side knows. Both sides give the same input for the same server state.
    /// </summary>
    public void ZombieAnimInputFor(Zombie z, double now, out ZombieAnimInput a) => ZombieAnimInputAt(z, ZAnimTick, z.RenderPos, z.RenderYaw, now, out a);

    /// <summary>
    /// Where the hit box of zombie <paramref name="z"/> sits relative to <paramref name="pos"/> at server tick
    /// <paramref name="tick"/>: at a window it follows the drawn body (<see cref="ZombieAnim.BodyShift"/>), elsewhere zero.
    /// </summary>
    public Vector3 ZombieHitShift(Zombie z, double tick, Vector3 pos)
    {
        if (z.Window < 0 || (z.State != ZState.Tearing && z.State != ZState.Attack))
            return Vector3.Zero;
        ZombieAnimInputAt(z, tick, pos, z.Yaw, 0, out var a);
        return ZombieAnim.BodyShift(a);
    }

    /// <summary>The pose input at server tick <paramref name="rt"/> for a zombie at <paramref name="pos"/> (see <see cref="ZombieAnimInputFor"/>).</summary>
    private void ZombieAnimInputAt(Zombie z, double rt, Vector3 pos, float yaw, double now, out ZombieAnimInput a)
    {
        a = default;
        const float dt = World.TickDt;
        a.Pos = pos;
        a.Yaw = yaw;
        a.Id = z.Id;
        a.Speed = z.Speed;
        a.Gait = z.Gait;
        a.Move = z.AnimMove;
        a.SinceState = MathF.Max(0, (float)((rt - z.AnimStateTick) * dt));
        a.Time = (float)(now % 4096.0) + z.Id * 0.37f;
        a.Windup = ZombieWindupFor(z); // (one arm, a bite: ZombieLimbs.cs)
        a.Limbs = z.Limbs;
        a.TearSeconds = ZRules.BoardTearSeconds;
        var attack = (float)((rt - z.AttackTick) * dt);
        a.Attack = attack >= 0 && attack < 2f ? attack : -1f;
        var land = (float)((rt - z.LandTick) * dt);
        a.Stagger = land >= 0 && land < 1f ? land : -1f;
        var hurt = (float)((rt - z.FlinchTick) * dt);
        a.Flinch = hurt < 0 || hurt > 0.3f ? 0f : z.FlinchAmt * (hurt < 0.05f ? hurt / 0.05f : 1f - (hurt - 0.05f) / 0.25f);

        var zm = World.Map.Zombies;
        var win = zm != null && z.Window >= 0 && z.Window < zm.Windows.Count ? zm.Windows[z.Window] : null;
        switch (z.State)
        {
            case ZState.Dead:
                a.Mode = ZAnimMode.Dead;
                a.Phase = a.SinceState;
                return;
            case ZState.Rising:
                a.Mode = ZAnimMode.Rising;
                a.Phase = a.SinceState / MathF.Max(0.1f, ZRules.RiseSeconds);
                return;
            case ZState.Climbing when win != null:
                a.Mode = ZAnimMode.Vaulting;
                a.Phase = Math.Clamp(z.AnimByte / 255f + (float)((rt - z.AnimByteTick) * dt / MathF.Max(0.05f, ClimbSecondsFor(z))), 0f, 1f);
                a.Yaw = win.Yaw;
                a.SetWindow(win, Boards(z.Window));
                return;
            case ZState.Tearing when win != null:
            case ZState.Attack when win != null:
            {
                a.SetWindow(win, Boards(z.Window));
                a.Yaw = win.Yaw;
                a.SinceState = MathF.Max(0, (float)((rt - z.WindowTick) * dt));
                if (z.State == ZState.Tearing)
                {
                    a.Mode = ZAnimMode.Tearing;
                    var ph = z.AnimByte / 255f + (float)((rt - z.AnimByteTick) * dt / MathF.Max(0.05f, ZRules.BoardTearSeconds));
                    a.Phase = ph - MathF.Floor(ph);
                }
                else
                    a.Mode = ZAnimMode.WindowIdle;
                return;
            }
            default:
                a.Mode = ZAnimMode.Locomotion;
                return;
        }
    }

    private int Boards(int window) => window >= 0 && window < ZBoards.Length ? ZBoards[window] : 0;
}
