using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The slug (docs/zombies/ZOMBIES-MODE.md, "Dismemberment"): a zombie with all four limbs gone (<see cref="Zombie.Slug"/>,
/// from the limb bits, so clients know it without a new field). The torso and head inch along the ground at the rules'
/// slugSpeed (m/s, whatever its class), turning slowly, along the horde's flow field like any zombie inside; it never
/// jumps, so a ledge or the bell pile stops it (it waits at the bottom), and stairs it takes slowly. It can't climb in
/// through a window: one outside lies at the window's foot. It bites only at your feet. The horde walks over it (it is
/// left out of the horde's neighbour grid, <see cref="Zombie.HordeSolid"/>, and out of the window queues), it doesn't
/// count towards the alive cap, and one stranded unseen with nobody near for slugCullSeconds is culled. No allocation.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// Server, each tick of a living slug before its state's own think: the cull watch, then what it does outside (lies at
    /// its window's foot; waits during a siege). True if it handled the tick (the state's own think is skipped).
    /// </summary>
    private bool SlugThink(ZombiesMap zm, Zombie z, double now)
    {
        if (z.Hm.ThinkNow && SlugSeen(z))
            z.SlugSeenAt = now;
        if (now - z.SlugSeenAt > ZRules.Limbs.SlugCullSeconds && z.State != ZState.Climbing)
        {
            SlugCull(z, now);
            return true;
        }
        if (z.SiegeOut && _climbEnt >= 0)
        {
            MoveZombie(z, z.Yaw, false, false); // outside during a siege: it can't climb the pile
            return true;
        }
        if (z.Window < 0 || z.Window >= zm.Windows.Count || z.State is ZState.Climbing or ZState.Rising)
            return false; // inside (or already over the sill): the usual chase
        if (z.State == ZState.Attack)
            return false; // a swipe through the window it started before it lost its arms: lands, then back here
        // Outside: no window for a slug. It inches to the window's foot and lies there (never tearing or climbing).
        if (z.State != ZState.ToWindow)
            SetZState(z, ZState.ToWindow, now);
        var w = zm.Windows[z.Window];
        if (Flat(w.Outside - z.Position) < 0.4f)
        {
            MoveZombie(z, w.Yaw, false, false);
            z.StuckAt = now;
            z.StuckPos = z.Position;
            return true;
        }
        var portalNode = z.Window < HordeAI.Portals.Length ? HordeAI.Portals[z.Window].Node : -1;
        var p = HordeAI.Steer(z, w.Outside, portalNode, out _);
        var d = p - z.Position;
        MoveZombie(z, MathF.Atan2(d.X, -d.Z), true, false);
        return true;
    }

    /// <summary>Server: is a standing player near this slug (slugCullNear) or in plain view of it (60 m)?</summary>
    private bool SlugSeen(Zombie z)
    {
        var near = ZRules.Limbs.SlugCullNear;
        var at = z.Position + new Vector3(0, 0.25f, 0);
        foreach (var p in Players.Values)
        {
            if (!IsStanding(p))
                continue;
            var to = at - p.Eye;
            var d = to.Length();
            if (Flat(p.Position - z.Position) < near && MathF.Abs(p.Position.Y - z.Position.Y) < 4f)
                return true;
            if (d < 60f && d > 0.01f && World.Query.Raycast(p.Eye, to / d, d, QueryFilter.Shots) >= d - 0.3f)
                return true;
        }
        return false;
    }

    /// <summary>Server: a stranded slug nobody saw for slugCullSeconds is gone (no points; the round's count goes down as for a kill).</summary>
    private void SlugCull(Zombie z, double now)
    {
        z.Health = 0;
        if (z.Grabbing)
            ReleaseGrab(z, now);
        SetZState(z, ZState.Dead, now);
        CrowdNoteKill(); // (a crowd map's conservation count: ZombiesCrowd.cs)
        ZSlugsCulled++;
    }

    /// <summary>A slug's facing this tick, turning towards <paramref name="yaw"/> at the rules' slugTurn (rad/s).</summary>
    private float SlugTurnTo(float from, float yaw)
    {
        var step = ZRules.Limbs.SlugTurn * World.TickDt;
        var diff = World.WrapAngle(yaw - from);
        return World.WrapAngle(from + Math.Clamp(diff, -step, step));
    }

    /// <summary>A slug's speed (m/s): the rules' slugSpeed whatever its class or type; plasma still slows it.</summary>
    private float SlugSpeedOf(Zombie z) => ZRules.Limbs.SlugSpeed * (z.SlowUntil > _zNow ? ZRules.SlowMult : 1f);

    // ---- Self-test hooks -----------------------------------------------------------------------------

    /// <summary>Self-test: the alive count the spawner holds against the cap (slugs left out).</summary>
    public int SelfTestAliveForCap => _zAlive;
}
