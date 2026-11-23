using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// What a body's boxes are, as numbers that only change when they do (2026-11-23, the Zero Day works' once-a-second 40 ms frame):
/// <see cref="Construct.RebuildStamp"/> goes up on every rebuild, a wire, a setting or a link as much as a part added, so caches
/// of a big body's boxes keyed to it (the sun's caster, the lamps' shadow maps, the contact shadow's footprint) were made again
/// whole for changes that moved no box. On a new stamp this hashes every part's type, box list, place and turn (about 30 us for
/// 6,000 parts, nothing allocated), apart for the door parts and the rest, and bumps a version only when a hash differs.
/// <see cref="Main"/> covers the parts no door moves; <see cref="Doors"/> the doors' parts at their settled poses, together with
/// <see cref="Construct.DoorPoseStamp"/>. Render only: nothing on the wire or in a save.
/// </summary>
public static class ConstructShapeVersion
{
    /// <summary>The version of the parts that no door moves: goes up only when one of them changed type, boxes, place or turn, or came or went.</summary>
    public static int Main(Construct c)
    {
        Refresh(c);
        return c.ShapeMainVersion;
    }

    /// <summary>The version of the door parts (a door and the leaves riding it): their boxes and places, and every door start or settle.</summary>
    public static int Doors(Construct c)
    {
        Refresh(c);
        return c.ShapeDoorVersion;
    }

    /// <summary>Both together, for a cache of all of a body's boxes.</summary>
    public static long Both(Construct c)
    {
        Refresh(c);
        return (long)c.ShapeMainVersion << 32 | (uint)c.ShapeDoorVersion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Mix(long h, long v) => (h ^ v) * 0x100000001B3L + (h >> 29);

    private static void Refresh(Construct c)
    {
        if (c.ShapeStamp == c.RebuildStamp && c.ShapeParts == c.PartCount && c.ShapeDoorPose == c.DoorPoseStamp)
            return;
        var doorsMoved = c.ShapeDoorPose != c.DoorPoseStamp;
        if (c.ShapeStamp != c.RebuildStamp || c.ShapeParts != c.PartCount)
        {
            c.EnsureDoors();
            long main = c.PartCount, doors = 17;
            for (var i = 0; i < c.PartCount; i++)
            {
                ref var p = ref c.Parts[i];
                var d = p.D;
                var h = Mix(Mix(Mix(Mix(RuntimeHelpers.GetHashCode(d), RuntimeHelpers.GetHashCode(d.Boxes)), BitConverter.SingleToInt32Bits(p.Pos.X)), BitConverter.SingleToInt32Bits(p.Pos.Y)), BitConverter.SingleToInt32Bits(p.Pos.Z) ^ (long)p.Rot << 32 ^ (long)p.Seg << 40);
                if (c.DoorOfListed(i) >= 0)
                    doors = Mix(doors, h ^ i);
                else
                    main = Mix(main, h);
            }
            if (main != c.ShapeMainHash || c.ShapeStamp < 0)
            {
                c.ShapeMainHash = main;
                c.ShapeMainVersion++;
            }
            if (doors != c.ShapeDoorHash || c.ShapeStamp < 0)
            {
                c.ShapeDoorHash = doors;
                doorsMoved = true;
            }
            c.ShapeStamp = c.RebuildStamp;
            c.ShapeParts = c.PartCount;
        }
        if (doorsMoved)
            c.ShapeDoorVersion++;
        c.ShapeDoorPose = c.DoorPoseStamp;
    }
}

public sealed partial class Construct
{
    // (ConstructShapeVersion: the stamp, part count and door pose its hashes were taken at, the hashes and the versions)
    internal int ShapeStamp = -1, ShapeParts = -1, ShapeDoorPose = -1, ShapeMainVersion, ShapeDoorVersion;
    internal long ShapeMainHash, ShapeDoorHash;
}
