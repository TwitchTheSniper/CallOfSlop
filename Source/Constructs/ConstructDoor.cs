using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A door is whatever hangs on a hinge, not a kind of part (docs/sandbox/CONSTRUCTS.md, "Doors"). <see cref="ConstructEdits.Use"/>
/// already opens anything on a joint's segment; this is what a player sees of it: the look line's state, and where the yellow
/// glow goes on a panel that has no handle of its own. All of it runs on the aimed part only and allocates nothing but the line.
/// </summary>
public static class ConstructDoor
{
    private const float AreaWidth = 0.08f, AreaHeight = 0.16f, EdgeInset = 0.12f; // (half sizes and how far in from the free edge: a hand's width)

    /// <summary>The joint this part swings on as a door, or -1. Panels, frames and doors on a passive hinge or a motor hinge no wire runs: what F opens.</summary>
    public static int JointOf(Construct c, int part)
    {
        if ((uint)part >= (uint)c.PartCount || c.Loose)
            return -1;
        ref var p = ref c.Parts[part];
        if (p.D.Role is not (PartRole.Door or PartRole.Panel or PartRole.Frame))
            return -1;
        var seg = c.MoveSeg(p.Seg);
        if (seg <= 0)
            return -1;
        ref var jt = ref c.Joints[seg - 1];
        return jt.Type is JointType.Passive or JointType.Motor && !(jt.Wired && jt.Motorised) ? seg - 1 : -1;
    }

    public static bool Swings(Construct c, int part) => JointOf(c, part) >= 0;

    public static bool IsOpen(Construct c, int part) => JointOf(c, part) is var j and >= 0 && c.Joints[j].Open;

    /// <summary>The look line: "Door: shut - F: open". <paramref name="key"/> null: out of reach, or the joint can't be worked.</summary>
    public static string Look(Construct c, int part, string? key)
    {
        var open = IsOpen(c, part);
        var locked = JointOf(c, part) is var j and >= 0 && c.Joints[j].Locked;
        return SandboxKeys.Line(c.DefOf(part).Name, locked ? "locked" : open ? "open" : "shut", null, key == null || locked ? null : SandboxKeys.Do(key, open ? "shut" : "open"));
    }

    /// <summary>A part on the same swinging piece with a handle's "use" boxes (the Door Handle), or -1.</summary>
    public static int HandleOn(Construct c, int part)
    {
        var seg = c.MoveSeg(c.Parts[part].Seg);
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.UseBoxes.Length > 0 && d.Role is PartRole.Frame or PartRole.Panel && c.MoveSeg(c.Parts[i].Seg) == seg)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Where a panel with no handle is worked, in its segment's frame: a hand's width at the edge away from the hinge, halfway along the hinge's axis.
    /// (Pressing anywhere on it works, as on a Door: this is what lights.) Call only when <see cref="Swings"/>.
    /// </summary>
    public static void UseArea(Construct c, int part, out Vector3 centre, out Vector3 half)
    {
        ref var p = ref c.Parts[part];
        ref var jt = ref c.Joints[JointOf(c, part)];
        c.ShapeBounds(part, out var mn, out var mx);
        // The hinge in the part's own frame (it swings, and a freeform weld may turn it): through the world.
        c.WorldFrame(jt.ParentSeg, out var pq, out var pt);
        c.WorldFrame(p.Seg, out var q, out var t);
        var back = Quaternion.Conjugate(q);
        var pivot = Vector3.Transform(pt + Vector3.Transform(jt.Pivot, pq) - t, back);
        var axis = Vector3.Transform(Vector3.Transform(jt.Axis, pq), back);
        var along = Biggest(axis);
        var size = mx - mn;
        int free = -1, thin = -1;
        for (var a = 0; a < 3; a++)
            if (a != along && (free < 0 || size[a] > size[free]))
                free = a;
        thin = 3 - along - free;
        var far = MathF.Abs(mx[free] - pivot[free]) >= MathF.Abs(mn[free] - pivot[free]);
        var inset = MathF.Min(EdgeInset, size[free] * 0.25f);
        centre = default;
        centre[free] = far ? mx[free] - inset : mn[free] + inset;
        centre[along] = (mn[along] + mx[along]) * 0.5f;
        centre[thin] = (mn[thin] + mx[thin]) * 0.5f;
        half = default;
        half[free] = MathF.Min(AreaWidth, size[free] * 0.25f);
        half[along] = MathF.Min(AreaHeight, size[along] * 0.5f);
        half[thin] = size[thin] * 0.5f;
    }

    private static int Biggest(Vector3 v)
    {
        var a = MathF.Abs(v.X) >= MathF.Abs(v.Y) ? 0 : 1;
        return MathF.Abs(v[a]) >= MathF.Abs(v.Z) ? a : 2;
    }
}
