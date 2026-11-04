using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Hands tool (<see cref="ConstructTool.Hands"/>; docs/sandbox/CONSTRUCTS.md, "Hands"): LMB takes what lies loose, or a stored
/// part (<see cref="CActKind.TakeStored"/>, the same as F), into the hands, RMB sets what they hold down where the player looks.
/// It never fits or removes a part: that stays the wrench's and the welder's. What the hands hold stays there when the tool
/// changes. The give rule, the reach and full hands are <see cref="PickUp"/>'s; on top of it this adds the contested rule
/// (decision 19), so a defended enemy's parts stay theirs.
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>How fast a dropped part leaves the hands along the look (m/s), on top of what the player carried.</summary>
    public const float DropToss = 1f;

    /// <summary>How far a dropped part's centre stays from the eye, past its own size (m).</summary>
    public const float DropGap = 0.5f;

    /// <summary>Steps along the look when the spot aimed at has no room (m).</summary>
    private const float DropStep = 0.3f;

    /// <summary>
    /// Why hands can't take body <paramref name="c"/> (null: they can), given decision 19's answer for it. Both sides ask this: the
    /// look line and the click here, the server again. Reach and full hands are <see cref="PickUp"/>'s.
    /// </summary>
    public static string? HandsRefusal(ConstructWorld world, Construct c, string? contested)
    {
        if (!c.Loose && !WreckedAlone(c))
            return "that isn't loose";
        var d = c.DefOf(0);
        if (!c.Loose)
            return $"the wrecked {d.Name} needs hauling: use the physics gun";
        if (d.PhysicsOnly)
            return $"the {d.Name} is physics-only: use the physics gun";
        if (world.StoreOf(c, 0) is { Empty: false })
            return $"empty the {d.Name} first";
        if (c.GrabbedBy != 255)
            return "someone is holding it";
        return contested;
    }

    /// <summary>Server: hands out, the loose part <paramref name="body"/> into them. Null, or why not.</summary>
    public string? HandTake(ConstructWorld world, byte player, Vector3 eye, ushort body)
    {
        ref var h = ref Hands[player];
        if (h.Tool != ConstructTool.Hands)
            return "take your hands out first";
        if (h.Seated)
            return "get out of the seat first";
        if (world.Get(body) is not { } c)
            return "that is gone";
        return HandsRefusal(world, c, Contested(c, player)) ?? PickUp(world, player, eye, body);
    }

    /// <summary>
    /// Server: hands out, what they hold goes down as a loose part: just short of <paramref name="at"/> (the crosshair's point, at
    /// most <see cref="ConstructBuild.UseReach"/> away) or the nearest clear spot back along the look, else straight in front.
    /// It leaves at <paramref name="carry"/> (the player's speed in the world, a vehicle's included) plus a small toss. Null, or why not;
    /// with no room the part stays in the hands.
    /// </summary>
    public string? DropAt(ConstructWorld world, byte player, Vector3 eye, Vector3 at, float yaw, Vector3 carry)
    {
        ref var h = ref Hands[player];
        if (h.Tool != ConstructTool.Hands)
            return "take your hands out first";
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(h.Held) is not { } def)
            return "nothing in your hands";
        var front = GravityFrames.FlatForward(eye, yaw);
        var look = at - eye;
        var dist = look.Length();
        var dir = float.IsFinite(dist) && dist > 0.01f ? look / dist : front;
        if (!float.IsFinite(dist))
            dist = ConstructBuild.UseReach;
        var size = ((def.BoundsMax - def.BoundsMin) * 0.5f).Length();
        var near = size + DropGap;
        var rot = ConstructGrab.YawQ(yaw);
        var spot = default(Vector3?);
        for (var d = MathF.Min(dist, ConstructBuild.UseReach) - size; d >= near && spot == null; d -= DropStep)
            spot = DropClear(world, def, eye + dir * d, rot);
        spot ??= DropClear(world, def, eye + dir * near, rot) ?? DropClear(world, def, eye + front * near, rot);
        if (spot is not { } centre)
            return "no room to drop it there";
        if (DropLoose(world, def, centre, rot) is not { } dropped)
            return "the world is full of constructs";
        dropped.Owner = player;
        var vel = carry + dir * DropToss;
        dropped.Vel = float.IsFinite(vel.X + vel.Y + vel.Z) ? vel : Vector3.Zero;
        h.Held = ConstructParts.None;
        h.Edit = EditKind.None;
        Done.Add($"player {player}: {def.Name} dropped as construct {dropped.Id}");
        return null;
    }

    /// <summary><paramref name="centre"/> when part <paramref name="def"/> fits there.</summary>
    private Vector3? DropClear(ConstructWorld world, ConstructPartDef def, Vector3 centre, Quaternion rot)
    {
        var mid = (def.BoundsMin + def.BoundsMax) * 0.5f;
        var fp = new FreePlacement { Pos = centre - Vector3.Transform(mid, rot), Rot = rot };
        return ConstructPlace.Fits(world, Query, def, fp, Feet(world)) == null ? centre : null;
    }
}
