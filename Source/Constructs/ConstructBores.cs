using System.Numerics;

namespace CallOfSlop;

/// <summary>A hole a bore left on a part's face, drawn on every PC (<see cref="ConstructWorld.BoreMarks"/>): in its part's segment frame.</summary>
public struct BoreMark
{
    public ushort Body;
    public ushort Part; // its index when made (found again by Key when the parts were renumbered)
    public Vector3 Key; // the part's Pos then: how it is found again
    public ConstructPartDef? Base, Def; // ...with the file part it was holed from (a big shape's facets share one Pos), and its def last seen
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
    public const float BurstGap = 3f; // ...and one that doesn't fly on (an explosive round): out of what it is in and this far clear, it bursts there
    public const float FloorShare = 0.35f; // the damage a round with no budget left still does, as a share
    public const int MaxSpalls = 8; // exits a shot throws spall from
    public const int SpallRays = 3;
    public const float SpallReach = 5f, SpallCone = 0.45f, SpallShare = 0.12f; // metres, radians, of the round's damage at that exit

    public const int SameSteps = 6; // steps of SameReach at most through a part it just left and met again at once
    public const float SameReach = 0.25f;
    public static long Bored, Spalled, HolesCut; // (counts, for the check)

    /// <summary>Bores cut real holes through the parts they cross (false: scorch marks only; --no-bore-holes).</summary>
    public static bool Holes = true;
    public const int HoleEvery = 12; // ticks between cuts (a body is rebuilt at most this often by holes: 5 a second)
    public const double HoleBudgetMs = 2; // ms of holed parts made in one cut at most, the first always made (server only, so a clock is fine)
    public const int MaxWaiting = 256; // holes waiting at most (more in one fifth of a second are left as scorch marks)
    public const int MaxHoleParts = 12; // parts one shot holes at most (each a new holed def and its share of the body's rebuild)
    public const float HoleScale = 1f; // the hole's half size, as a share of the calibre (the mark's radius)
    public const float HoleBefore = 0.05f, HoleOver = 0.1f; // metres the tunnel starts before the face, and goes on past the far one
    public static bool Trace = Environment.GetEnvironmentVariable("BORE_TRACE") == "1";

    // Penetration by calibre (Karl, 2026-11-20: "add penetration for other shells, weapons etc as well where relevant"). A heavy gun
    // with no "pierce" of its own gets a budget from its round's punch (damage x muzzle speed, in thousands): nothing below
    // ImpliedFloor (a PDC's, a machine gun's, a slow cannon's: they stop at what they hit, as before), ImpliedPerPunch a unit above
    // it, capped at ImpliedMax. An explosive round's is capped lower (ImpliedBurst): it goes in through the hull and bursts inside.
    public const float ImpliedFloor = 120f, ImpliedPerPunch = 1.2f, ImpliedMax = 2500f, ImpliedBurst = 1100f;

    /// <summary>
    /// A heavy gun's bore budget when its part file gives none: from <paramref name="damage"/> and <paramref name="speed"/> (m/s).
    /// 0 for anything that isn't a fast solid round: flames, harpoons, shells, rockets, beams, and rounds too light to matter.
    /// </summary>
    public static float Implied(GunEffect effects, float damage, float speed, GunFx fx)
    {
        if ((effects & (GunEffect.Flame | GunEffect.Harpoon | GunEffect.Shell)) != 0 || fx is GunFx.Rocket or GunFx.Flame or GunFx.Beam or GunFx.Harpoon || speed <= 0)
            return 0f;
        var punch = damage * speed / 1000f;
        if (punch <= ImpliedFloor)
            return 0f;
        return MathF.Min((punch - ImpliedFloor) * ImpliedPerPunch, (effects & GunEffect.Explosive) != 0 ? ImpliedBurst : ImpliedMax);
    }

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
        var holed = 0;
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
                if (Holes && holed < MaxHoleParts && !h.Glass && h.SlotPlus1 == 0 && body.Parts[h.Part].Health > dealt
                    && Cut(world, body, h.Part, at, dir, calibre, budget - Cost(armour, exit) <= 0 ? exit * 0.5f : exit + HoleOver))
                    holed++;
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
            // A glancing hit (its box said it was out at once, but its shape goes on: a lofted hull piece's slanted facets) that meets
            // the same part again at once: step on along it, charged once.
            for (var k = 0; k < SameSteps && exit < 0.02f && h.RoutePlus1 == 0 && h.TrackPlus1 == 0 && world.Raycast(from, dir, SameReach, out var same, ownBody) && same.Body == body && same.Part == h.Part && same.Distance < SameReach; k++)
            {
                from += dir * (same.Distance + SameReach);
                end = from;
                if (Trace)
                    Console.WriteLine($"    same part again {same.Distance:0.000} m on: stepped to {from}");
            }
            // The gap to the next part: the ground stops it, and anyone standing in the gap is hit on its way.
            var reach = speed > 1f ? MaxGap : BurstGap;
            var ground = false;
            if (query != null)
            {
                var g = query.Raycast(from, dir, reach, QueryFilter.Shots);
                if (g < reach - 1e-3f)
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
                    world.RoundFlies(from, dir * speed, damage * share, muzzle, by, weapon, own, budget, calibre, body.Id); // (its first look passes the body it came out of: a round leaving along a hull's face would meet it again every tick)
                break;
            }
            h = next;
            at = next.Point;
        }
        world.AddFire(new CFireEvent { Kind = CFireKind.Bore, At = start, To = end, Radius = calibre });
        return result;
    }

    /// <summary>
    /// Server: a real hole through part <paramref name="part"/> along the bore (Karl, 2026-11-21: "penetrating weapons do not leave
    /// holes"): a square tunnel of half size <paramref name="calibre"/> from just before <paramref name="at"/>, <paramref name="depth"/> m
    /// along <paramref name="dir"/>, cut by HullHoles (mesh and collision, so you see through it), the part swapped for its holed self.
    /// A part HullHoles refuses (a machine, a door, a lamp), or one with all its holes already, keeps only the scorch mark. The cut
    /// waits for <see cref="Step"/>, so a body under fire is rebuilt at most every <see cref="HoleEvery"/> ticks whatever the guns' rate;
    /// the swap rides the structure patch and saves as the part's key.
    /// </summary>
    public static bool Cut(ConstructWorld world, Construct body, int part, Vector3 at, Vector3 dir, float calibre, float depth)
    {
        ref var p = ref body.Parts[part];
        if (p.Broken || body.Rides(part) || world.HolesWaiting.Count >= MaxWaiting)
            return false;
        var d = body.DefOf(part);
        if (HullHoles.Refusal(HullHoles.BaseOf(d)) != null || HullHoles.HolesOf(d).Length >= HullHoles.MaxHoles)
            return false;
        Freeform.PartWorld(body, part, out var pos, out var rot);
        var iq = Quaternion.Conjugate(rot);
        // The square's sides along the part's own axes (a plain box is cut along them: a tunnel turned in its face would cut its
        // bounding square instead, up to 1.4 times as wide), its up the axis of the face that isn't the way in.
        var ld = Vector3.Transform(dir, iq);
        var a = MathF.Abs(ld.X) >= MathF.Abs(ld.Y) && MathF.Abs(ld.X) >= MathF.Abs(ld.Z) ? 0 : MathF.Abs(ld.Y) >= MathF.Abs(ld.Z) ? 1 : 2;
        var lup = a == 1 ? Vector3.UnitZ : Vector3.UnitY;
        var half = calibre * HoleScale;
        var local = HullHoles.Hole.Of(Vector3.Transform(at - dir * HoleBefore - pos, iq), ld, lup, half, half, depth + HoleBefore);
        world.HolesWaiting.Add((body.Id, part, p.Pos, local));
        return true;
    }

    /// <summary>Server, each tick: every <see cref="HoleEvery"/> ticks the holes waiting are cut, each part swapped for its holed self.</summary>
    public static void Step(ConstructWorld world, long tick)
    {
        var list = world.HolesWaiting;
        if (list.Count == 0 || tick % HoleEvery != 0)
            return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var done = 0;
        for (; done < list.Count; done++)
        {
            if (done > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= HoleBudgetMs)
                break; // (the rest wait for the next cut: a broadside's holes all made in one tick took 8-28 ms, 2026-11-22)
            var (id, part, key, hole) = list[done];
            if (world.Get(id) is not { } body || part >= body.PartCount || body.Parts[part].Pos != key || body.Parts[part].Broken)
                continue; // (it broke off or was renumbered meanwhile: no hole)
            var d = body.DefOf(part);
            if (HullHoles.WithHole(d, hole) is not { } holedDef)
                continue; // (it has all its holes, or the cut would leave nothing: the scorch mark stays)
            body.Parts[part].Def = holedDef.Id;
            body.Dirty = true;
            world.StructureDirty.Add(body.Id);
            HolesCut++;
            if (Trace)
                Console.WriteLine(FormattableString.Invariant($"    holed {HullHoles.BaseOf(d).Name}: {hole.HalfW * 200:0} cm square, {hole.Depth:0.00} m deep"));
        }
        list.RemoveRange(0, done);
    }

    /// <summary>Two directions square to <paramref name="dir"/> and to each other, the same on every PC (a hole's up and across).</summary>
    public static Vector3 Across(Vector3 dir, out Vector3 right)
    {
        right = MathF.Abs(dir.Y) < 0.95f ? Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY)) : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(right, dir));
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
        // A second ray just outside the hole's edge: the server may have cut the holes (Cut) before this PC marks them, and the middle
        // ray then goes clean through. Whichever meets a part first is the one, its point taken back onto the middle line.
        Across(d, out var right);
        var aside = right * (radius * HoleScale * 1.5f + 0.01f); // (past the square's corner whichever way it is turned)
        for (var k = 0; k < MaxParts && left > 0; k++)
        {
            var mid = world.Raycast(pos, d, left, out var hit);
            if (world.Raycast(pos + aside, d, left, out var edge) && (!mid || edge.Distance < hit.Distance - 0.005f))
            {
                var dn = Vector3.Dot(d, edge.Normal);
                var t = MathF.Abs(dn) > 0.2f ? MathF.Max(0f, Vector3.Dot(edge.Point - pos, edge.Normal) / dn) : edge.Distance;
                // Only a part the line itself crosses (its cut hole): a part the side ray grazes beside the line would put the
                // mark in the open, off its face's plane.
                if (InBounds(edge.Body, edge.Part, pos + d * t))
                {
                    edge.Point = pos + d * t;
                    edge.Distance = t;
                    hit = edge;
                    mid = true;
                }
            }
            if (!mid || hit.RoutePlus1 > 0 || hit.TrackPlus1 > 0)
                break;
            var c = hit.Body;
            if (!hit.Glass)
                world.AddBoreMark(c, hit.Part, hit.Point, hit.Normal, radius, now);
            var exit = ExitFace(c, hit.Part, hit.Point, d, out var normal);
            var outAt = hit.Point + d * exit;
            var gone = hit.Distance + exit;
            if (gone > left)
                break; // (it stopped inside)
            // The bounds' face is where the round flies on from, but the mark goes on the part's real far face (a pipe, a wedge,
            // a part with a gap: its bounds' face can be in the open), found by a ray back along the line; none, no mark.
            if (!hit.Glass && RealExit(c, hit.Part, hit.Point, d, exit, aside, out var realAt, out var realNormal))
                world.AddBoreMark(c, hit.Part, realAt, realNormal, radius, now);
            if (n < exits.Length)
                exits[n++] = outAt;
            pos = outAt + d * 0.01f;
            left -= gone + 0.01f;
        }
        return n;
    }

    /// <summary>
    /// Where a bore that entered part <paramref name="part"/> at <paramref name="at"/> really leaves it: a ray back from just past its
    /// bounds' exit (<paramref name="exit"/>) meets the part's far face. A second ray <paramref name="aside"/> of the line finds a face
    /// whose middle was cut away (a real hole), its point taken back onto the line. False when neither meets the part.
    /// </summary>
    private static bool RealExit(Construct c, int part, Vector3 at, Vector3 d, float exit, Vector3 aside, out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        if (c.MergeOf[part] != 0)
        {
            ExitFace(c, part, at, d, out normal); // (a merged receiver is one box: its bounds are its face)
            point = at + d * exit;
            return true;
        }
        var span = exit + 0.02f;
        var back = at + d * span;
        var found = PartRay(c, part, back, -d, span, out var t, out normal);
        if (PartRay(c, part, back + aside, -d, span, out var te, out var ne) && (!found || te < t - 0.005f))
        {
            var dn = Vector3.Dot(-d, ne);
            t = MathF.Abs(dn) > 0.2f ? MathF.Max(0f, Vector3.Dot(aside - d * te, ne) / dn) : te;
            if (InBounds(c, part, back - d * t))
            {
                normal = ne;
                found = true;
            }
            else if (found)
                PartRay(c, part, back, -d, span, out t, out normal); // (the side ray met it beside the line: the middle's answer stands)
        }
        if (!found || t > span)
            return false;
        point = back - d * t;
        return true;
    }

    /// <summary>Is a world point within part <paramref name="part"/>'s bounds (a centimetre of slack)?</summary>
    private static bool InBounds(Construct c, int part, Vector3 at)
    {
        if (part < 0 || part >= c.PartCount)
            return false;
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var lo = Vector3.Transform(at - t, Quaternion.Conjugate(q));
        c.PartBounds(part, out var mn, out var mx);
        var s = new Vector3(0.01f);
        return lo.X >= mn.X - s.X && lo.Y >= mn.Y - s.Y && lo.Z >= mn.Z - s.Z && lo.X <= mx.X + s.X && lo.Y <= mx.Y + s.Y && lo.Z <= mx.Z + s.Z;
    }

    /// <summary>A ray against one part's solid boxes and hulls (world space): the nearest hit and its outward normal.</summary>
    private static bool PartRay(Construct c, int i, Vector3 origin, Vector3 dir, float max, out float dist, out Vector3 normal)
    {
        dist = max;
        normal = default;
        var found = false;
        ref var p = ref c.Parts[i];
        c.WorldFrame(p.Seg, out var q, out var t);
        var iq = Quaternion.Conjugate(q);
        var lo = Vector3.Transform(origin - t, iq);
        var ld = Vector3.Transform(dir, iq);
        var boxes = c.SolidCount(i);
        for (var bi = 0; bi < boxes; bi++)
        {
            if (c.MergeOf[i] == 0 && ConstructHulls.Covered(p.D, bi))
                continue; // (a shape's hidden boxes: its hull stands for them)
            c.SolidBox(i, bi, out var centre, out var half);
            if (Geometry.RayAabb(centre - half, centre + half, lo, ld, dist) is { } bd && bd < dist && bd > 0)
            {
                dist = bd;
                found = true;
                normal = Vector3.Transform(Geometry.BoxEntryNormal(centre - half, centre + half, lo, ld, bd), q);
            }
        }
        if (c.MergeOf[i] == 0)
        {
            var hulls = ConstructHulls.Of(p.D);
            var pr = Rot24.Quat(p.Rot);
            var bk = Quaternion.Conjugate(pr);
            var pl = Vector3.Transform(lo - p.Pos, bk);
            var pd = Vector3.Transform(ld, bk);
            for (var h = 0; h < hulls.Length; h++)
                if (hulls[h].Ray(pl, pd, dist, out var hd, out var pn) && hd < dist && hd > 0)
                {
                    dist = hd;
                    found = true;
                    normal = Vector3.Transform(Vector3.Transform(pn, pr), q);
                }
        }
        return found;
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
    public readonly List<(ushort Body, int Part, Vector3 Key, HullHoles.Hole Hole)> HolesWaiting = new(); // (ConstructBores.Cut, cut by its Step)
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
            Def = c.DefOf(part), Base = HullHoles.BaseOf(c.DefOf(part)),
            Pos = Vector3.Transform(at - t, iq), Normal = Vector3.Normalize(Vector3.Transform(normal, iq)),
        };
        _boreNext = (_boreNext + 1) % MaxBoreMarks;
        BoreMarkCount = Math.Min(BoreMarkCount + 1, MaxBoreMarks);
    }

    /// <summary>
    /// Does mark <paramref name="k"/> (live, on <paramref name="c"/>) sit on a hole its part was cut with (<see cref="ConstructBores.Cut"/>)?
    /// Then it is drawn as a frame round the opening, not over it: <paramref name="across"/> and <paramref name="up"/> are the opening's
    /// half sides on the face (the tunnel's square met by the face, so stretched where it goes in slanting), in the part's segment frame.
    /// </summary>
    public bool BoreMarkHoled(int k, Construct c, out Vector3 across, out Vector3 up)
    {
        across = up = default;
        ref var m = ref BoreMarks[k];
        ref var p = ref c.Parts[m.Part];
        var d = c.DefOf(m.Part);
        if (!HullHoles.IsHoled(d))
            return false;
        var iq = Quaternion.Conjugate(Rot24.Quat(p.Rot));
        var local = Vector3.Transform(m.Pos - p.Pos, iq);
        var n = Vector3.Transform(m.Normal, iq);
        foreach (var h in HullHoles.HolesOf(d))
        {
            var dir = h.Dir;
            var hu = h.Up;
            var rel = local - h.Origin;
            var along = Vector3.Dot(rel, dir);
            if (along < -0.06f || along > h.Depth + 0.06f)
                continue;
            var slack = 0.02f + m.Radius * 0.5f;
            var ha = Vector3.Cross(dir, hu);
            if (MathF.Abs(Vector3.Dot(rel, hu)) > h.HalfH + slack || MathF.Abs(Vector3.Dot(rel, ha)) > h.HalfW + slack)
                continue;
            // Each side projected along the way in onto the face (n . dir kept from 0.4: a grazing hole isn't drawn metres long).
            var nd = Vector3.Dot(n, dir);
            nd = nd < 0 ? MathF.Min(nd, -0.4f) : MathF.Max(nd, 0.4f);
            var pr = Rot24.Quat(p.Rot);
            across = Vector3.Transform((ha - dir * (Vector3.Dot(n, ha) / nd)) * h.HalfW, pr);
            up = Vector3.Transform((hu - dir * (Vector3.Dot(n, hu) / nd)) * h.HalfH, pr);
            return true;
        }
        return false;
    }

    /// <summary>Is part <paramref name="i"/> the mark's own (not a facet of the same shape beside it, at the same Pos)? Its def, or one holed from the same file part.</summary>
    private static bool Same(ref BoreMark m, Construct c, int i)
    {
        var d = c.DefOf(i);
        if (ReferenceEquals(d, m.Def))
            return true;
        if (!ReferenceEquals(HullHoles.BaseOf(d), m.Base))
            return false;
        m.Def = d; // (holed since: the base lookup once)
        return true;
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
        if (i >= 0 && body.Parts[i].Pos == m.Key && Same(ref m, body, i))
            return true;
        for (; i >= 0; i--)
            if (body.Parts[i].Pos == m.Key && Same(ref m, body, i))
            {
                m.Part = (ushort)i;
                return true;
            }
        m.Body = 0; // (its part broke off, or the piece it was on went: ConstructBreakUp moves none across)
        return false;
    }
}
