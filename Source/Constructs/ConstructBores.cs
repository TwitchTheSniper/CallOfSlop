using System.Numerics;

namespace CallOfSlop;

/// <summary>A hole a bore left on a part's face, drawn on every PC (<see cref="ConstructWorld.BoreMarks"/>): in its part's segment frame.</summary>
public struct BoreMark
{
    public ushort Body;
    public ushort Part; // its index when made (found again by Key when the parts were renumbered)
    public Vector3 Key; // the part's Pos then: how it is found again
    public Vector3 Pos, Normal; // on the face, out of it (the part's segment frame)
    public float Radius;
    public double Born; // the clock the game draws by
}

/// <summary>
/// Bores (docs/sandbox/design/capital-battles.md): a heavy penetrating round (a gun with a <c>"pierce"</c> budget, the Zero Day
/// Railgun) goes on through part after part, the whole way through a ship and out into the next, until its energy is spent:
/// each part takes off a fixed cost and its armour times the metal it went through. Its damage falls with what is left. At
/// each part it leaves, a cone of spall hits what is just behind. What it does is the server's (<see cref="Through"/>); every
/// PC hears only where it went in and came out (<see cref="CFireKind.Bore"/>) and marks the holes on its own copy
/// (<see cref="Mark"/>): a scorched hole with a hot rim that cools, drawn by Program.SandboxBores.cs. No part's shape changes:
/// a bore is narrower than a person (HullHoles cuts the walk-through ones). The marks are looks, not saved.
/// </summary>
public static class ConstructBores
{
    /// <summary>The budget rule is on (false: every penetrating gun keeps the old <see cref="ConstructWeapons.MaxPierce"/> rule; --old-pierce).</summary>
    public static bool On = true;

    public const int MaxParts = 96; // parts one bore goes through at most (a bound on a shot's cost whatever its budget)
    public const float CostBase = 40f; // budget a part takes whatever it is
    public const float CostArmour = 12f; // ...and per armour point per metre of it the round crossed
    public const float MaxGap = 400f; // metres a bore looks ahead for the next part before it flies on as a round
    public const float FloorShare = 0.35f; // the damage a round with no budget left still does, as a share
    public const int MaxSpalls = 8; // exits a shot throws spall from
    public const int SpallRays = 3;
    public const float SpallReach = 5f, SpallCone = 0.45f, SpallShare = 0.12f; // metres, radians, of the round's damage at that exit

    public static long Bored, Spalled; // (counts, for the check)
    public static bool Trace = Environment.GetEnvironmentVariable("BORE_TRACE") == "1";

    /// <summary>The budget one part takes: its cost and its armour times the metal crossed.</summary>
    public static float Cost(float armour, float metres) => CostBase + armour * MathF.Max(0.05f, metres) * CostArmour;

    /// <summary>The hole's radius for a round of this damage.</summary>
    public static float Calibre(float damage) => Math.Clamp(0.06f + damage * 0.00015f, 0.06f, 0.4f); // (the Zero Day Railgun's 900: 0.2 m)

    /// <summary>
    /// Server: a bore from the part <paramref name="first"/> hit along <paramref name="dir"/>, with <paramref name="damage"/> and
    /// <paramref name="budget"/> left. Goes on through each part met within <see cref="MaxGap"/> of the last; when nothing is,
    /// what is left flies on as a round (<see cref="ConstructWorld.RoundFlies"/>) at <paramref name="speed"/>. Adds one
    /// <see cref="CFireKind.Bore"/> event. <paramref name="end"/> is where it stopped or came out last.
    /// </summary>
    public static CFireHit Through(ConstructWorld world, ushort own, ConstructHit first, Vector3 dir, float damage, float budget, float muzzle, float speed, float calibre, byte by, byte weapon, out Vector3 end, IWorldQuery? query = null)
    {
        var result = CFireHit.Part;
        var ownBody = own == 0 ? null : world.Get(own);
        var h = first;
        var at = first.Point;
        var start = at;
        end = at;
        var b0 = MathF.Max(1f, budget);
        var spalls = 0;
        if (calibre <= 0)
            calibre = Calibre(damage);
        for (var n = 0; n < MaxParts; n++)
        {
            var share = FloorShare + (1f - FloorShare) * Math.Clamp(budget / b0, 0f, 1f);
            var dealt = damage * share;
            var body = h.Body;
            float exit, armour;
            if (h.TrackPlus1 > 0)
            {
                exit = 0.3f;
                armour = 10f;
                ConstructTracks.Damage(world, body, h.TrackPlus1 - 1, dealt);
            }
            else if (h.RoutePlus1 > 0)
            {
                exit = 0.15f;
                armour = 1f;
                ConstructRoutes.Damage(world, body, h.RoutePlus1 - 1, h.RouteSeg, dealt, blast: false);
            }
            else
            {
                armour = body.Parts[h.Part].D.Armour;
                exit = ConstructWeapons.ExitDistance(body, h.Part, at, dir); // (before the damage: the part may go)
                world.DamageAt = at;
                if (h.SlotPlus1 > 0)
                    world.DamageStored(body, h.Part, h.SlotPlus1 - 1, dealt);
                else
                    world.DamagePart(body, h.Part, dealt, out _);
                world.DamageAt = null;
            }
            Bored++;
            if (!h.Glass)
                budget -= Cost(armour, exit);
            if (Trace)
                Console.WriteLine($"  bore {n}: {(h.RoutePlus1 > 0 ? "a link" : body.PartCount > h.Part ? body.DefOf(h.Part).Name : "?")} armour {armour:0} through {exit:0.00} m, dealt {dealt:0}, health {(h.RoutePlus1 == 0 && body.PartCount > h.Part ? body.Parts[h.Part].Health : 0):0} of {(h.RoutePlus1 == 0 && body.PartCount > h.Part ? body.DefOf(h.Part).Health : 0):0}, budget left {budget:0}");
            var outAt = at + dir * exit;
            if (budget <= 0)
            {
                end = at + dir * (exit * 0.5f); // (it stops inside the last one)
                break;
            }
            end = outAt;
            result |= CFireHit.Through;
            if (spalls < MaxSpalls && !h.Glass && h.RoutePlus1 == 0)
            {
                Spall(world, ownBody, outAt, dir, dealt * SpallShare, (uint)n * 2654435761u ^ (uint)world.ShotsFired);
                spalls++;
            }
            var from = outAt + dir * 0.01f;
            // The gap to the next part: the ground stops it, and anyone standing in the gap is hit on its way.
            var reach = MaxGap;
            var ground = false;
            if (query != null)
            {
                var g = query.Raycast(from, dir, MaxGap, QueryFilter.Shots);
                if (g < MaxGap - 1e-3f)
                {
                    reach = g;
                    ground = true;
                }
            }
            var found = world.Raycast(from, dir, reach, out var next, ownBody) && next.Distance < reach;
            var gap = found ? next.Distance : reach;
            if (world.Targets != null && world.Targets.RayTarget(from, dir, gap, own, out var td, out var tid) && td < gap)
                world.Targets.RoundHit(tid, dealt, by, weapon, from + dir * td, dir);
            if (!found && ground)
            {
                end = from + dir * reach; // (into the ground)
                break;
            }
            if (!found)
            {
                // Out of the last thing in its way: what is left flies on (into the next ship).
                if (speed > 1f)
                    world.RoundFlies(from, dir * speed, damage * share, muzzle, by, weapon, own, budget, calibre);
                break;
            }
            h = next;
            at = next.Point;
        }
        world.AddFire(new CFireEvent { Kind = CFireKind.Bore, At = start, To = end, Radius = calibre });
        return result;
    }

    /// <summary>A cone of spall out of where a bore left a part: a few short rays, each hurting the first part it meets.</summary>
    private static void Spall(ConstructWorld world, Construct? ownBody, Vector3 at, Vector3 dir, float damage, uint seed)
    {
        if (damage < 1f)
            return;
        var right = MathF.Abs(dir.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY)) : Vector3.UnitX;
        var up = Vector3.Cross(right, dir);
        for (var k = 0; k < SpallRays; k++)
        {
            var a = (seed + (uint)k * 0x9e3779b9u) % 6283 / 1000f;
            var spread = MathF.Tan(SpallCone) * (0.4f + 0.6f * ((seed >> 8) + (uint)k * 7919u) % 1000 / 1000f);
            var d = Vector3.Normalize(dir + (right * MathF.Cos(a) + up * MathF.Sin(a)) * spread);
            if (!world.Raycast(at + d * 0.02f, d, SpallReach, out var hit, ownBody) || hit.RoutePlus1 > 0 || hit.TrackPlus1 > 0 || hit.SlotPlus1 > 0)
                continue;
            world.DamageAt = hit.Point;
            world.DamagePart(hit.Body, hit.Part, damage, out _);
            world.DamageAt = null;
            Spalled++;
        }
    }

    // ---- The holes on every PC -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Every PC: the holes of a bore from <paramref name="from"/> to <paramref name="to"/> on its own copy of the constructs (a
    /// mark where it went into each part and where it came out), and the exits into the open for the sparks. Returns how many
    /// exits went into <paramref name="exits"/>.
    /// </summary>
    public static int Mark(ConstructWorld world, Vector3 from, Vector3 to, float radius, double now, Span<Vector3> exits)
    {
        var d = to - from;
        var len = d.Length();
        if (len < 0.01f)
            return 0;
        d /= len;
        var pos = from - d * 0.05f;
        var left = len + 0.1f;
        var n = 0;
        for (var k = 0; k < MaxParts && left > 0; k++)
        {
            if (!world.Raycast(pos, d, left, out var hit) || hit.RoutePlus1 > 0 || hit.TrackPlus1 > 0)
                break;
            var c = hit.Body;
            if (!hit.Glass)
                world.AddBoreMark(c, hit.Part, hit.Point, hit.Normal, radius, now);
            var exit = ExitFace(c, hit.Part, hit.Point, d, out var normal);
            var outAt = hit.Point + d * exit;
            var gone = hit.Distance + exit;
            if (gone > left)
                break; // (it stopped inside)
            if (!hit.Glass)
                world.AddBoreMark(c, hit.Part, outAt, normal, radius, now);
            if (n < exits.Length)
                exits[n++] = outAt;
            pos = outAt + d * 0.01f;
            left -= gone + 0.01f;
        }
        return n;
    }

    /// <summary><see cref="ConstructWeapons.ExitDistance"/>, and the world normal of the face it leaves by.</summary>
    public static float ExitFace(Construct c, int part, Vector3 at, Vector3 dir, out Vector3 normal)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var iq = Quaternion.Conjugate(q);
        var lo = Vector3.Transform(at - t, iq);
        var ld = Vector3.Transform(dir, iq);
        c.PartBounds(part, out var mn, out var mx);
        var exit = 4f;
        var axis = Vector3.Zero;
        for (var a = 0; a < 3; a++)
        {
            if (MathF.Abs(ld[a]) < 1e-6f)
                continue;
            var tx = MathF.Max(0, ((ld[a] > 0 ? mx[a] : mn[a]) - lo[a]) / ld[a]);
            if (tx < exit)
            {
                exit = tx;
                axis = Vector3.Zero;
                axis[a] = ld[a] > 0 ? 1 : -1;
            }
        }
        normal = axis == Vector3.Zero ? dir : Vector3.Transform(axis, q);
        return exit;
    }
}

public sealed partial class ConstructWorld
{
    public const int MaxBoreMarks = 4096; // holes drawn at once, in every world (the oldest go first)

    public BoreMark[] BoreMarks = []; // (made with the first)
    public int BoreMarkCount;
    private int _boreNext;

    /// <summary>A hole on part <paramref name="part"/> at a world point, out along <paramref name="normal"/>: kept in its segment's frame (a hull plate is often on a welded segment of its own).</summary>
    public void AddBoreMark(Construct c, int part, Vector3 at, Vector3 normal, float radius, double now)
    {
        if (part < 0 || part >= c.PartCount)
            return;
        if (BoreMarks.Length == 0)
            BoreMarks = new BoreMark[MaxBoreMarks];
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var iq = Quaternion.Conjugate(q);
        BoreMarks[_boreNext] = new BoreMark
        {
            Body = c.Id, Part = (ushort)part, Key = c.Parts[part].Pos, Radius = radius, Born = now,
            Pos = Vector3.Transform(at - t, iq), Normal = Vector3.Normalize(Vector3.Transform(normal, iq)),
        };
        _boreNext = (_boreNext + 1) % MaxBoreMarks;
        BoreMarkCount = Math.Min(BoreMarkCount + 1, MaxBoreMarks);
    }

    /// <summary>Is mark <paramref name="k"/>'s part still on its body? Finds it again after a renumbering (parts keep their order); false (and the mark goes) when it is gone.</summary>
    public bool BoreMarkLive(int k, out Construct c)
    {
        ref var m = ref BoreMarks[k];
        c = null!;
        if (m.Body == 0 || Get(m.Body) is not { } body)
        {
            m.Body = 0;
            return false;
        }
        c = body;
        var i = Math.Min(m.Part, body.PartCount - 1);
        if (i >= 0 && body.Parts[i].Pos == m.Key)
            return true;
        for (; i >= 0; i--)
            if (body.Parts[i].Pos == m.Key)
            {
                m.Part = (ushort)i;
                return true;
            }
        m.Body = 0; // (its part broke off, or the piece it was on went: ConstructBreakUp moves none across)
        return false;
    }
}
