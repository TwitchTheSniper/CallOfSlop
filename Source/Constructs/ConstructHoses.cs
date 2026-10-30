using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A hose off its reel (board item wl01): the reel (a hose outlet part) it pays out from, who holds its receiver (the
/// nozzle or plug at its end), and the inlet it is plugged into. A hose on its reel has no entry.
/// </summary>
public struct CHose
{
    public ushort TagOut, TagIn; // server: the reel's and the inlet's part tags
    public ushort BodyOut, BodyIn; // where they are now (BodyIn 0: not plugged in)
    public byte PartOut, PartIn;
    public byte Holder; // the player holding the receiver (255: nobody)
    public float Value; // a data lead: the signal it carries (last tick's)

    public readonly bool Plugged => BodyIn != 0;
}

/// <summary>
/// Hose connectors (board item wl01; docs/sandbox/CONSTRUCTS.md, "Hoses"): temporary connections. A hose outlet (a reel
/// on a construct or a world station) holds a flexible hose (a fluid hose, a power or data lead) with a receiver
/// at its end; a matching hose inlet (a coupler socket) on any other construct takes it. F with empty hands on the reel
/// takes the receiver (an item held while the hose is out, not a backpack item), the hose pays out as you walk, F on a
/// matching inlet plugs it in, F on the inlet takes it out again and F on the reel hangs it back (or the Drop key lets
/// go: it reels back). It connects any two constructs, moving or not, while they stay within the hose's length: pulled
/// past it, it pops out (no damage) and reels back. What flows is the reel's construct's into the inlet's at the reel's
/// rated flow. The hoses are drawn as sagging ropes from the reel to the receiver; nothing is simulated.
/// </summary>
public sealed partial class ConstructWorld
{
    public const int MaxHoses = 32;
    public const float HoseSlip = 0.25f; // a held hose slips from the hands this far past its length

    public readonly CHose[] Hoses = new CHose[MaxHoses];
    public int HoseCount;

    /// <summary>Where a hose part's hose leaves its reel, or where a receiver plugs in (the world, its segment as it is now).</summary>
    public static Vector3 HosePoint(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, p.D.HoseAt), q);
    }

    /// <summary>The hose out of reel <paramref name="part"/>, or plugged into inlet <paramref name="part"/>, of <paramref name="c"/>; -1 if none.</summary>
    public int HoseAt(Construct c, int part)
    {
        for (var k = 0; k < HoseCount; k++)
            if (Hoses[k].BodyOut == c.Id && Hoses[k].PartOut == part || Hoses[k].BodyIn == c.Id && Hoses[k].PartIn == part)
                return k;
        return -1;
    }

    /// <summary>The hose whose receiver <paramref name="player"/> holds, or -1.</summary>
    public int HoseHeldBy(byte player)
    {
        if (player == 255)
            return -1;
        for (var k = 0; k < HoseCount; k++)
            if (Hoses[k].Holder == player)
                return k;
        return -1;
    }

    public static string HoseName(HoseCarry h) => h switch { HoseCarry.Fluid => "fluid hose", HoseCarry.Power => "power lead", _ => "data lead" };

    private static string ReceiverName(HoseCarry h) => h == HoseCarry.Fluid ? "nozzle" : "plug";

    /// <summary>
    /// Server: F on hose part <paramref name="part"/> of <paramref name="c"/> by <paramref name="player"/> (in reach; the game
    /// checked). On a reel: take the receiver (hands free of parts), or hang the one you hold back. On an inlet: plug the
    /// receiver you hold in, or take the plugged one out into your hand. Null, or why not.
    /// </summary>
    public string? HoseUse(byte player, Construct c, int part, bool handsFree)
    {
        if (part < 0 || part >= c.PartCount || c.DefOf(part) is not { Role: PartRole.Hose } d)
            return "that isn't a hose part";
        if (c.Parts[part].Broken)
            return $"the {d.Name} is wrecked";
        var held = HoseHeldBy(player);
        var here = HoseAt(c, part);
        if (d.HoseEnd == HoseEnd.Outlet)
        {
            if (held >= 0 && held == here)
            {
                RemoveHose(held);
                return null; // (hung back on its reel)
            }
            if (held >= 0)
                return "you're holding another hose: hang it on its reel or plug it in first";
            if (here >= 0)
                return Hoses[here].Plugged ? $"that {HoseName(d.HoseCarry)} is plugged in: take it out at the other end (F on the socket)" : "someone has that hose";
            if (!handsFree)
                return $"put down what you hold to take the {ReceiverName(d.HoseCarry)}";
            if (HoseCount >= MaxHoses)
                return $"at most {MaxHoses} hoses out at once";
            EnsureTag(c, part);
            Hoses[HoseCount++] = new CHose { TagOut = c.Parts[part].Tag, BodyOut = c.Id, PartOut = (byte)part, Holder = player };
            WorldLinksDirty = true;
            Events.Add($"player {player} took the {HoseName(d.HoseCarry)} off construct {c.Id}'s reel");
            return null;
        }
        // An inlet.
        if (held >= 0)
        {
            ref var h = ref Hoses[held];
            if (Get(h.BodyOut) is not { } rc || h.PartOut >= rc.PartCount)
                return "that hose's reel is gone";
            var rd = rc.DefOf(h.PartOut);
            if (rd.HoseCarry != d.HoseCarry)
                return $"a {HoseName(rd.HoseCarry)} doesn't fit a {d.Name}";
            if (here >= 0)
                return $"the {d.Name} already has a hose in it";
            if (rc == c)
                return "plug it into another construct (or hang it back on its reel)";
            var dist = Vector3.Distance(HosePoint(rc, h.PartOut), HosePoint(c, part));
            if (dist > rd.HoseLength)
                return $"the {HoseName(rd.HoseCarry)} reaches {rd.HoseLength:0} m ({dist:0.0} m here)";
            EnsureTag(c, part);
            h.TagIn = c.Parts[part].Tag;
            h.BodyIn = c.Id;
            h.PartIn = (byte)part;
            h.Holder = 255;
            WorldLinksDirty = true;
            c.Wake();
            Events.Add($"player {player} plugged the {HoseName(rd.HoseCarry)} from construct {rc.Id} into construct {c.Id}");
            return null;
        }
        if (here < 0 || !Hoses[here].Plugged)
            return $"the {d.Name} is empty: bring the {ReceiverName(d.HoseCarry)} from a matching reel";
        if (!handsFree)
            return $"put down what you hold to take the {ReceiverName(d.HoseCarry)} out";
        ref var ph = ref Hoses[here];
        ph.TagIn = 0;
        ph.BodyIn = 0;
        ph.PartIn = 0;
        ph.Holder = player;
        WorldLinksDirty = true;
        c.Wake();
        return null;
    }

    /// <summary>Server: <paramref name="player"/> lets go of the receiver they hold (the Drop key, death, leaving): it reels back. True if they held one.</summary>
    public bool HoseLetGo(byte player)
    {
        var k = HoseHeldBy(player);
        if (k < 0)
            return false;
        RemoveHose(k);
        return true;
    }

    private void RemoveHose(int k)
    {
        Hoses[k] = Hoses[--HoseCount];
        WorldLinksDirty = true;
    }

    /// <summary>A data lead's signal at inlet <paramref name="part"/> of <paramref name="c"/> (0 with nothing plugged in).</summary>
    public float HoseSignal(Construct c, int part)
    {
        for (var k = 0; k < HoseCount; k++)
            if (Hoses[k].BodyIn == c.Id && Hoses[k].PartIn == part)
                return Hoses[k].Value;
        return 0;
    }

    /// <summary>
    /// Server, each tick: every hose finds its reel and inlet again; a reel gone or wrecked drops its hose; a holder gone
    /// lets go; a held hose pulled past its length slips from the hands, a plugged one pops out (a hint, no damage), and a
    /// plugged hose carries what it carries. <paramref name="hands"/>: where a player's hands are (null: not alive).
    /// </summary>
    private void StepHoses(float dt, Func<byte, Vector3?>? hands)
    {
        for (var k = HoseCount - 1; k >= 0; k--)
        {
            ref var h = ref Hoses[k];
            var (bo, po, bi, pi) = (h.BodyOut, h.PartOut, h.BodyIn, h.PartIn);
            var rc = Resolve(h.TagOut, ref h.BodyOut, ref h.PartOut);
            if (rc == null || rc.Parts[h.PartOut].Broken || rc.DefOf(h.PartOut).Role != PartRole.Hose)
            {
                Events.Add("a hose's reel is gone: the hose went with it");
                RemoveHose(k);
                continue;
            }
            var rd = rc.DefOf(h.PartOut);
            var reel = HosePoint(rc, h.PartOut);
            if (h.Plugged)
            {
                var ic = Resolve(h.TagIn, ref h.BodyIn, ref h.PartIn);
                if (bo != h.BodyOut || po != h.PartOut || bi != h.BodyIn || pi != h.PartIn)
                    WorldLinksDirty = true;
                if (ic == null || ic.Parts[h.PartIn].Broken)
                {
                    Events.Add($"the {HoseName(rd.HoseCarry)}'s inlet is gone: it reels back");
                    RemoveHose(k);
                    continue;
                }
                var inlet = HosePoint(ic, h.PartIn);
                if (Vector3.Distance(reel, inlet) > rd.HoseLength)
                {
                    Hints.Add((inlet, $"the {HoseName(rd.HoseCarry)} popped out of the {ConstructLabel(ic)}: past its {rd.HoseLength:0} m"));
                    Events.Add($"the {HoseName(rd.HoseCarry)} from construct {rc.Id} popped out of construct {ic.Id}: stretched past its length");
                    RemoveHose(k);
                    continue;
                }
                switch (rd.HoseCarry)
                {
                    case HoseCarry.Fluid:
                        MoveFuel(rc, -1, ic, rd.HoseFlow * dt); // (whatever the tanks hold, into tanks that hold the same)
                        break;
                    case HoseCarry.Power:
                        MoveCharge(rc, ic, rd.HoseFlow * dt);
                        break;
                    default:
                        var v = rc.Parts[h.PartOut].Out;
                        if (v != h.Value)
                            ic.Wake();
                        h.Value = v;
                        ic.XWireIn++;
                        break;
                }
                continue;
            }
            if (bo != h.BodyOut || po != h.PartOut)
                WorldLinksDirty = true;
            if (h.Holder == 255 || hands?.Invoke(h.Holder) is not { } at)
            {
                RemoveHose(k);
                continue;
            }
            if (Vector3.Distance(reel, at) > rd.HoseLength + HoseSlip)
            {
                Hints.Add((at, $"the {HoseName(rd.HoseCarry)} reached its {rd.HoseLength:0} m and slipped from your hands: it reeled back"));
                RemoveHose(k);
            }
        }
    }

    /// <summary>Where a player's hands hold a receiver, from their eye (the server's rule, and the client draws the hose to it).</summary>
    public static Vector3 HandPoint(Vector3 eye, float yaw) => eye + new Vector3(MathF.Sin(yaw) * 0.35f, -0.45f, -MathF.Cos(yaw) * 0.35f);
}
