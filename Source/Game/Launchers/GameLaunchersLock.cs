using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The local player's lock-on (lock-on launchers only). Aim down sights with an enemy's airborne kill streak (the
/// Attack Drone today, any machine <see cref="LauncherTargets"/> says flies) inside the launcher's lock cone and in
/// sight: a slow tone ticks faster as the lock builds over "lockTime" seconds, then goes solid when it's locked. Fire
/// while locked and the rocket homes on it; fire before that and it flies dumb. Losing the target for more than a
/// quarter of a second starts the lock over. Ground machines (the sentry) never lock: they take dumb fire.
/// </summary>
public sealed partial class Game
{
    /// <summary>The machine being locked (255 = none).</summary>
    public byte LockTarget = byte.MaxValue;

    /// <summary>0..1 towards a lock.</summary>
    public float LockProgress;

    /// <summary>Locked: the next shot homes on <see cref="LockTarget"/>.</summary>
    public bool Locked => LockTarget != byte.MaxValue && LockProgress >= 1f;

    private double _lockSeenAt, _nextLockTone;

    /// <summary>Seconds a target may slip out of the cone before the lock starts over.</summary>
    public const double LockGrace = 0.25;

    private void ResetLock()
    {
        LockTarget = byte.MaxValue;
        LockProgress = 0;
    }

    /// <summary>Does the local player hold a lock-on launcher, aimed, alive and not reloading?</summary>
    public bool LockingNow(double now) =>
        Local is { Alive: true } && Active.Def is { IsLauncher: true, LauncherGuidance: LauncherGuidance.LockOn } && AimT > 0.6f && !IsReloading(now) && Active.Ammo > 0;

    /// <summary>The best machine to lock: an enemy's airborne one, in range, in the cone and in sight, nearest the crosshair.</summary>
    public StreakEntity? LockCandidate(Vector3 eye, Vector3 look, WeaponDef def)
    {
        if (Local is not { } me)
            return null;
        StreakEntity? best = null;
        var bestCos = MathF.Cos(def.LauncherLockCone * MathF.PI / 180f);
        foreach (var e in StreakEntities.Values)
        {
            if (!StreakMachineHostile(e, me) || !LauncherTargets.IsAirborne(e))
                continue;
            var c = e.Hitbox.Center - e.Pos + e.RenderPos;
            var to = c - eye;
            var dist = to.Length();
            if (dist > def.LauncherLockRange || dist < 2f)
                continue;
            var cos = Vector3.Dot(to / dist, look);
            if (cos < bestCos)
                continue;
            if (World.Query.Raycast(eye, to / dist, dist, QueryFilter.Shots) < dist - 1.5f)
                continue; // behind a roof or a wall
            bestCos = cos;
            best = e;
        }
        return best;
    }

    /// <summary>Per frame: builds, holds or loses the lock, and plays the lock tones.</summary>
    private void UpdateLauncherLock(double now, float dt)
    {
        if (!LockingNow(now))
        {
            ResetLock();
            return;
        }
        var me = Local!;
        var def = Active.Def;
        var cand = LockCandidate(me.RenderEye, LookDir(LookYaw, LookPitch), def);
        if (cand != null && cand.Id == LockTarget)
        {
            LockProgress = MathF.Min(1f, LockProgress + dt / MathF.Max(0.05f, def.LauncherLockTime));
            _lockSeenAt = now;
        }
        else if (cand != null && (LockTarget == byte.MaxValue || now - _lockSeenAt > LockGrace))
        {
            LockTarget = cand.Id;
            LockProgress = 0;
            _lockSeenAt = now;
        }
        else if (LockTarget != byte.MaxValue && now - _lockSeenAt > LockGrace)
        {
            ResetLock();
        }
        if (LockTarget == byte.MaxValue || now < _nextLockTone)
            return;
        if (Locked)
        {
            _nextLockTone = now + 0.11;
            Audio.Effect(Sfx.LockedTone, 0.45f);
        }
        else
        {
            _nextLockTone = now + 0.32 - 0.2 * LockProgress; // ticks faster as the lock builds
            Audio.Effect(Sfx.LockTone, 0.45f);
        }
    }

    /// <summary>Self-test: the lock as the local player would build it over <paramref name="seconds"/> (frames of <paramref name="dt"/>).</summary>
    public void SelfTestBuildLock(double start, double seconds, float dt)
    {
        for (var t = 0.0; t < seconds; t += dt)
            UpdateLauncherLock(start + t, dt);
    }
}
