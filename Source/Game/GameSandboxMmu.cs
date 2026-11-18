using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The local player's attitude in a worn MMU (Karl, 2026-11-18; docs/sandbox/EVA.md "Flying"): in vacuum (inside a ship or out) and
/// not seated, the pack is a vehicle, so the mouse turns and pitches the spacewalker about their own axes, over the top and
/// round as far as they like, and the roll keys (Q and E, a seat's) roll them. The attitude is kept in the world (an orbit's
/// frame turning under it doesn't turn it); <see cref="LookYaw"/> and <see cref="LookPitch"/> are measured back from it, so
/// aim, tools and shots look where the camera does, and the roll goes to the server with each command
/// (<see cref="InputCmd.Roll"/>, <see cref="PilotGear.MmuAxes"/>). Client only: nothing here allocates.
/// </summary>
public sealed partial class Game
{
    public const float MmuRollRate = 1.6f; // rad/s on the roll keys

    private Vector3 _mmuFwd, _mmuUp;
    private bool _mmuOn;

    /// <summary>Does the local player fly a worn MMU now: alive, wearing it in vacuum (inside a ship or out), not in a seat?</summary>
    public bool MmuSteering => SandboxOn && Local is { Alive: true } me && (me.Eva & Eva.Mmu) != 0 && !SandboxSeatedHere && PilotGear.MmuFree(me.Sim);

    /// <summary>
    /// The frame's start: whether the attitude steers the look. Taken from the look (level, unrolled) when the flight starts,
    /// and turned to follow the look if something else moved it (a respawn, a teleport, a gun's kick), keeping the roll.
    /// </summary>
    public bool MmuLookSync()
    {
        if (!MmuSteering)
        {
            _mmuOn = false;
            return false;
        }
        var f = LookDir(LookYaw, LookPitch);
        if (!_mmuOn)
        {
            PilotGear.MmuAxes(LookFrame, LookYaw, LookPitch, 0, out _mmuFwd, out _, out _mmuUp);
            _mmuOn = true;
        }
        else if (Vector3.Dot(f, _mmuFwd) < 0.999f) // (past the pitch clamp's band at the poles, 1.2 degrees)
        {
            var up = _mmuUp - f * Vector3.Dot(_mmuUp, f);
            if (up.LengthSquared() < 1e-6f)
                PilotGear.MmuAxes(LookFrame, LookYaw, LookPitch, 0, out _, out _, out up);
            _mmuFwd = f;
            _mmuUp = Vector3.Normalize(up);
        }
        return true;
    }

    /// <summary>Turns the attitude by the mouse and the roll keys (radians: right, up, rolled right) and measures the look from it.</summary>
    public void MmuTurn(float yawRight, float pitchUp, float rollRight)
    {
        if (!_mmuOn)
            return;
        var right = Vector3.Normalize(Vector3.Cross(_mmuFwd, _mmuUp));
        if (pitchUp != 0)
        {
            var q = Quaternion.CreateFromAxisAngle(right, pitchUp);
            _mmuFwd = Vector3.Transform(_mmuFwd, q);
            _mmuUp = Vector3.Transform(_mmuUp, q);
        }
        if (yawRight != 0)
            _mmuFwd = Vector3.Transform(_mmuFwd, Quaternion.CreateFromAxisAngle(_mmuUp, -yawRight));
        if (rollRight != 0)
            _mmuUp = Vector3.Transform(_mmuUp, Quaternion.CreateFromAxisAngle(_mmuFwd, rollRight));
        // Kept square and unit length, so the drift of many small turns never skews the view.
        _mmuFwd = Vector3.Normalize(_mmuFwd);
        _mmuUp = Vector3.Normalize(_mmuUp - _mmuFwd * Vector3.Dot(_mmuUp, _mmuFwd));
        var (yaw, pitch) = GravityFrames.Angles(LookFrame, _mmuFwd);
        LookYaw = World.WrapAngle(yaw);
        LookPitch = Math.Clamp(pitch, -1.55f, 1.55f);
    }

    /// <summary>The camera's forward and up while the MMU steers (first person).</summary>
    public bool MmuCamera(out Vector3 fwd, out Vector3 up)
    {
        fwd = _mmuFwd;
        up = _mmuUp;
        return _mmuOn && MmuSteering;
    }

    /// <summary>The roll this command carries: the attitude's up measured about the look, as the server will rebuild it; 0 when the MMU doesn't steer.</summary>
    /// <summary>
    /// The local player's MMU dampeners off (Karl, 2026-11-18: an inertia setting for the pack, as a ship's seat has on Z): off,
    /// the pack keeps what speed it has; on (the default), with no key held it brakes them to the nearest craft's motion
    /// (PilotGear.MmuBrake). Sent with each command; kept until toggled again (a new pack, a respawn: still the player's choice).
    /// </summary>
    public bool MmuCoasting { get; private set; }

    /// <summary>The dampener key while flying the pack: flips it and says so.</summary>
    public void MmuToggleDampers()
    {
        MmuCoasting = !MmuCoasting;
        SandboxSay(MmuCoasting ? "MMU inertia dampeners OFF: you keep your speed" : "MMU inertia dampeners ON: holding still against the nearest craft");
    }

        private float MmuCmdRoll() => _mmuOn && MmuSteering ? World.QuantRoll(PilotGear.MmuRollOf(LookFrame, LookYaw, LookPitch, _mmuUp)) : 0f;

    /// <summary>The window tool's "mmu" (host): a suit and a full MMU on the host's own player, for pictures and tries.</summary>
    public void WsGiveMmu(Player p)
    {
        p.Eva |= Eva.Suit | Eva.Mmu;
        p.SuitAir = Eva.SuitSeconds;
        p.Sim.JetFuel = p.MmuFuel = Eva.MmuFull;
        p.Sim.Gear = SandboxWorn(p);
        BroadcastRoster();
    }
}
