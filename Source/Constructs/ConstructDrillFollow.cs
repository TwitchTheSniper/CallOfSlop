namespace CallOfSlop;

/// <summary>
/// Drills that follow the ore (the user, 2026-11-17: "drills deplete the ground under them, so every extraction post must move its drills
/// over the deposit"). A spot gives <see cref="ResourceField.SpotUnits"/> and is then worked out (<see cref="ResourceField.WorkedOut"/>).
/// A drill whose carriage rides an Electric rail (a Linear Actuator run, in Position mode, with no wire into its Set) moves itself on: when its
/// spot is worked out the server steps the rail's Rest a spot's width (<see cref="ResourceField.SpotSize"/>) along, and the carriage carries
/// the drill there. Two such rails in a chain (a gantry: a cross rail on another rail's carriage) work a patch row by row: the inner rail
/// steps to its end, then runs back to its start as the outer one steps. At the ends of both the drill stops ("worked out": move the post).
/// The Rest it sets is the drive section's own setting, so saves, blueprints and the clients' copies follow it (the structure goes out);
/// nothing per tick: it runs from the gatherer's 10 Hz step, and only when a spot runs out.
/// </summary>
public static class ConstructDrillFollow
{
    private const float Arrived = 0.05f; // metres: a carriage this near its target has got there
    private const int MaxRails = 2;

    /// <summary>The drill on part <paramref name="part"/> is on a worked-out spot: step the rails that carry it. True while it is being moved on (or was just sent).</summary>
    public static bool Advance(ConstructWorld world, Construct c, int part)
    {
        Span<int> rails = stackalloc int[MaxRails];
        var n = 0;
        int seg = c.Parts[part].Seg, guard = 0;
        while (seg > 0 && seg <= c.JointCount && n < MaxRails && guard++ < 16)
        {
            ref var j = ref c.Joints[seg - 1];
            if (Follows(c, in j))
                rails[n++] = seg - 1;
            seg = j.ParentSeg;
        }
        if (n == 0)
            return false;
        for (var k = 0; k < n; k++)
            if (MathF.Abs(c.Joints[rails[k]].Angle - ConstructLinear.Target(c.Joints[rails[k]])) > Arrived)
                return true; // (still on its way to the last step)
        for (var k = 0; k < n; k++)
        {
            ref var j = ref c.Joints[rails[k]];
            var travel = MathF.Max(0.01f, j.Max - j.Min);
            var step = ResourceField.SpotSize / travel;
            if (j.RestAt + step <= 1f + 1e-3f)
            {
                SetRest(world, c, rails[k], j.RestAt + step);
                for (var i = 0; i < k; i++)
                    SetRest(world, c, rails[i], 0f); // (the inner rails back to their start: a new row)
                return true;
            }
        }
        return false; // (every rail at its end: the patch is worked out)
    }

    /// <summary>A rail that moves a drill on by itself: an Electric run in Position mode with nothing wired into its Set.</summary>
    public static bool Follows(Construct c, in CJoint j) =>
        j.Rail && !j.Wired && !j.SpeedMode && !j.ShaftRail && j.Type == JointType.Piston && j.Drive < c.PartCount && !c.Parts[j.Drive].Broken;

    private static void SetRest(ConstructWorld world, Construct c, int joint, float at)
    {
        ref var j = ref c.Joints[joint];
        at = Math.Clamp(at, 0, 1);
        j.RestAt = at;
        j.Open = false;
        ref var p = ref c.Parts[j.Drive];
        var slot = p.D.CfgSlot[(int)CfgKey.Rest];
        if (slot >= 0)
            p.Cfg[slot] = p.D.Config[slot].Clamp(at * 100f);
        world.StructureDirty.Add(c.Id);
        c.Wake();
    }
}
