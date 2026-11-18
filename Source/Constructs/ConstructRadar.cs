using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A craft's radar (docs/sandbox/constructs/weapons.md "Radar"): a part with a <c>"radar"</c> block (the Long Range
/// Sensor) gives every seat of its craft a scope of the ships round it out to its range, and the seated player can lock
/// one. The lock is the craft's (one at a time, the last one set): the server keeps it in <see cref="Construct.RadarLock"/>
/// and a missile launched from the craft with a heat, radar or command seeker (or a Seeker Head) steers for it while it is
/// within the seeker's reach. Contacts are read from each PC's own copies of the bodies, so the scope needs no messages;
/// only the lock goes to the server (<see cref="CActKind.RadarLock"/>). Nothing here allocates once warm.
/// </summary>
public static class ConstructRadar
{
    /// <summary>The longest "radar.range" a part file may have (m).</summary>
    public const float MaxRange = 200000f;

    /// <summary>Lighter bodies (a loose part, a crate) are not contacts (kg).</summary>
    public const float MinMass = 250f;

    /// <summary>Contacts a scope shows at most: the nearest.</summary>
    public const int MaxContacts = 32;

    /// <summary>A lock holds out to this share of the radar's range, so a contact on the edge doesn't flicker out.</summary>
    public const float HoldShare = 1.1f;

    /// <summary>How far this craft's working radar reaches (m; 0: it has none, or none works).</summary>
    public static float Range(Construct c)
    {
        if (c.RadarStamp != c.RebuildStamp || c.RadarPartCount != c.PartCount)
            Scan(c);
        var best = 0f;
        foreach (var i in c.RadarParts)
            if (i < c.PartCount && c.Parts[i].Working && c.DefOf(i).RadarRange > best)
                best = c.DefOf(i).RadarRange;
        return best;
    }

    private static void Scan(Construct c)
    {
        c.RadarStamp = c.RebuildStamp;
        c.RadarPartCount = c.PartCount;
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).RadarRange > 0)
                n++;
        if (n == 0)
        {
            c.RadarParts = [];
            return;
        }
        var list = new int[n]; // (once a rebuild, only on a craft that has one)
        n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).RadarRange > 0)
                list[n++] = i;
        c.RadarParts = list;
    }

    /// <summary>
    /// Can this craft's radar lock: is a working radar part wired into a working Fire Director (the fire control that hands
    /// the lock to the missiles)? Without one the radar is a scope only (the rust bucket), and destroying either part loses
    /// the lock (Karl, 2026-11-16). A walk of the wires: at a press, a launch, or once a frame for the local seat's HUD.
    /// </summary>
    public static bool CanLock(Construct c)
    {
        for (var w = 0; w < c.WireCount; w++)
        {
            int from = c.Wires[w].From, to = c.Wires[w].To;
            if (from < c.PartCount && to < c.PartCount && c.DefOf(from).RadarRange > 0 && c.DefOf(to).Logic == CLogic.Director
                && c.Parts[from].Working && c.Parts[to].Working)
                return true;
        }
        return false;
    }

    /// <summary>Is <paramref name="b"/> something <paramref name="c"/>'s radar shows, <paramref name="range"/> metres out at most: a craft of its own, free, not a wreck, not in a cargo bay.</summary>
    public static bool IsContact(Construct c, Construct b, float range) =>
        b != c && !b.Wreck && !b.Anchored && b.CarriedBy == 0 && b.Mass >= MinMass && b.PartCount > 0
        && Vector3.DistanceSquared(b.WorldCom, c.WorldCom) <= range * range;

    /// <summary>The contacts of <paramref name="c"/>'s radar out to <paramref name="range"/>, nearest first, into <paramref name="into"/> (at most its length); how many.</summary>
    public static int Contacts(ConstructWorld world, Construct c, float range, Span<ushort> into)
    {
        Span<float> dist = stackalloc float[MaxContacts];
        var max = Math.Min(into.Length, MaxContacts);
        var n = 0;
        var near = world.Near(c.WorldCom, range, world.RadarNear);
        for (var k = 0; k < near.Count; k++)
        {
            var b = near[k];
            if (!IsContact(c, b, range))
                continue;
            var d = Vector3.DistanceSquared(b.WorldCom, c.WorldCom);
            if (n == max && d >= dist[n - 1])
                continue;
            var at = n < max ? n++ : n - 1; // (sorted insert, the furthest dropped when full)
            while (at > 0 && dist[at - 1] > d)
            {
                dist[at] = dist[at - 1];
                into[at] = into[at - 1];
                at--;
            }
            dist[at] = d;
            into[at] = b.Id;
        }
        return n;
    }

    /// <summary>Server: sets <paramref name="c"/>'s lock (0 clears it); the reason when it can't.</summary>
    public static string? SetLock(ConstructWorld world, Construct c, ushort target)
    {
        if (target == 0)
        {
            c.RadarLock = 0;
            return null;
        }
        var range = Range(c);
        if (range <= 0)
            return "this craft has no working radar";
        if (!CanLock(c))
            return "the radar isn't wired to a working Fire Director";
        if (world.Get(target) is not { } b || !IsContact(c, b, range * HoldShare))
            return "that contact is off the radar";
        c.RadarLock = target;
        return null;
    }

    /// <summary>Server, at a launch from <paramref name="c"/>: its radar lock while it holds and is within <paramref name="reach"/> of <paramref name="at"/>, else 0. A lock off the radar is dropped.</summary>
    public static ushort Target(ConstructWorld world, Construct c, Vector3 at, float reach)
    {
        if (c.RadarLock == 0)
            return 0;
        var range = Range(c);
        if (range <= 0 || !CanLock(c) || world.Get(c.RadarLock) is not { } b || !IsContact(c, b, range * HoldShare))
        {
            c.RadarLock = 0;
            return 0;
        }
        return Vector3.DistanceSquared(b.WorldCom, at) <= reach * reach ? b.Id : (ushort)0;
    }
}

/// <summary>The world's side of the radar.</summary>
public sealed partial class ConstructWorld
{
    internal readonly List<Construct> RadarNear = new(64);
}
