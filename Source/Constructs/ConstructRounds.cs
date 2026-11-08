using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Rounds that met nothing in their reach fly on (docs/sandbox/CONSTRUCTS.md, "Flying rounds"): a shot is resolved at once over its reach, and what is left of a round
/// that reached the end of it goes into this pool and flies until it lands. One round shot straight up comes down. A fixed pool: nothing allocates, at most
/// <see cref="ConstructWorld.MaxRounds"/> at once (past that, a round ends at its reach as it used to), and only <see cref="ConstructWorld.RoundRaysPerTick"/>
/// look for what they hit in a tick, each over the whole stretch since it last looked, so a busy sky costs a bounded tick and nothing is flown through. In air they
/// slow to a terminal speed; a hit does the damage its speed left it.
/// </summary>
public struct CRound
{
    public Vector3 Pos, Vel, Tested; // where it is, how fast, and where its last look ended
    public float Damage, Muzzle; // what it would do at the speed it left the gun at
    public int Age; // ticks
    public byte By, Weapon; // the shooter (255: nobody) and what the kill feed says
    public ushort Gun; // the construct that fired it (0: a hand gun)
}

public sealed partial class ConstructWorld
{
    public const int MaxRounds = 256; // in the air at once
    public const int RoundRaysPerTick = 48; // rounds that look for what they hit a tick
    public const float RoundDrag = 0.3f; // per second, in air: with gravity of 18 m/s2 a falling round settles at 60 m/s
    public const int RoundAirAge = 60 * World.TickRate, RoundVacuumAge = 120 * World.TickRate; // ticks before it is given up on
    public const float RoundSpaceReach = 180_000f; // metres from the bubble's origin: floats get coarse past it, and ships leave the bubble there

    public CRound[] Rounds = []; // (made with the first round)
    public int RoundCount, RoundsFlown, RoundsLanded;
    private int _roundCursor;

    /// <summary>Is there no air here (a planetoid or open space)? No drag, and rounds fly far longer.</summary>
    private static bool Vacuum => !ConstructAir.HasAir || World.Map.Planetoid != null; // (a planetoid's thin air doesn't slow a round)

    /// <summary>A round flies on. False when the pool is full.</summary>
    public bool AddRound(in CRound r)
    {
        if (RoundCount >= MaxRounds)
            return false;
        if (Rounds.Length == 0)
            Rounds = new CRound[MaxRounds];
        Rounds[RoundCount++] = r;
        RoundsFlown++;
        return true;
    }

    /// <summary>Server: a round that met nothing goes on from here, and every PC hears it.</summary>
    public void RoundFlies(Vector3 at, Vector3 vel, float damage, float muzzle, byte by, byte weapon, ushort gun)
    {
        if (AddRound(new CRound { Pos = at, Tested = at, Vel = vel, Damage = damage, Muzzle = muzzle, By = by, Weapon = weapon, Gun = gun }))
            AddFire(new CFireEvent { Kind = CFireKind.RoundOut, At = at, To = vel });
    }

    /// <summary>Every PC: the rounds fly and take their turns looking. The server's do damage; a client's copy only ends.</summary>
    public void StepRounds(IWorldQuery query, float dt, bool server)
    {
        if (RoundCount == 0)
            return;
        var vacuum = Vacuum;
        var keep = vacuum ? 1f : MathF.Exp(-RoundDrag * dt);
        var maxAge = vacuum ? RoundVacuumAge : RoundAirAge;
        var map = World.Map;
        for (var k = RoundCount - 1; k >= 0; k--)
        {
            ref var r = ref Rounds[k];
            r.Vel = (r.Vel + ConstructShells.GravityAt(r.Pos) * dt) * keep;
            r.Pos += r.Vel * dt;
            var gone = ++r.Age > maxAge || (vacuum ? r.Pos.LengthSquared() > RoundSpaceReach * RoundSpaceReach : MathF.Abs(r.Pos.X) > map.HalfX + 3f || MathF.Abs(r.Pos.Z) > map.HalfZ + 3f);
            if (gone)
                Rounds[k] = Rounds[--RoundCount]; // (out of its time, past the bubble or past the map's edge)
        }
        var turns = Math.Min(RoundCount, RoundRaysPerTick);
        for (var n = 0; n < turns; n++)
        {
            if (RoundCount == 0)
                return;
            _roundCursor = (_roundCursor + 1) % RoundCount;
            if (RoundLook(query, _roundCursor, server))
                Rounds[_roundCursor] = Rounds[--RoundCount];
        }
    }

    /// <summary>One round looks over its stretch since the last look. True when it ended.</summary>
    private bool RoundLook(IWorldQuery query, int k, bool server)
    {
        ref var r = ref Rounds[k];
        var d = r.Pos - r.Tested;
        var len = d.Length();
        if (len < 1e-4f)
            return false;
        d /= len;
        var best = query.Raycast(r.Tested, d, len, QueryFilter.Shots);
        var met = best < len - 1e-3f;
        var what = 0; // 0: the map or nothing
        ConstructHit ch = default;
        if (Raycast(r.Tested, d, best, out var hit, noTracks: true) && hit.Distance < best && !hit.Glass)
        {
            best = hit.Distance;
            ch = hit;
            met = true;
            what = 1;
        }
        var tid = -1;
        if (server && Targets != null && Targets.RayTarget(r.Tested, d, best, 0, out var td, out var id) && td < best)
        {
            best = td;
            tid = id;
            met = true;
            what = 2;
        }
        var shell = -1;
        if (server && ShellCount > 0 && RayShell(r.Tested, d, best, out var sd, out var sk) && sd < best)
        {
            best = sd;
            shell = sk;
            met = true;
            what = 3; // (a bomb or missile in the air: ConstructCookOffs.cs)
        }
        if (!met)
        {
            r.Tested = r.Pos;
            return false;
        }
        if (server)
        {
            RoundsLanded++;
            var dealt = r.Damage * MathF.Min(1f, r.Vel.Length() / MathF.Max(1f, r.Muzzle));
            var at = r.Tested + d * best;
            if (what == 3)
                HurtShell(shell, dealt);
            else if (what == 2)
                Targets!.RoundHit(tid, dealt, r.By, r.Weapon, at, d);
            else if (what == 1 && ch.Part >= 0 && ch.Part < ch.Body.PartCount && ch.RoutePlus1 == 0 && ch.SlotPlus1 == 0)
            {
                DamageBy = r.By;
                DamagePart(ch.Body, ch.Part, dealt, out _);
                DamageBy = 255;
            }
        }
        return true;
    }
}
