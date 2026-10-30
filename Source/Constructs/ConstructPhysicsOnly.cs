using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Physics-only parts (the developer's decision 21, board item dev-sandbox-cargo-volumes-cv01; docs/sandbox/CONSTRUCTS.md,
/// "Physics-only parts"): a part fitted with the welder, a part heavier than <see cref="ConstructParts.HandCarryLimit"/>,
/// or one whose file says <c>"physicsOnly": true</c>, is never an item. It can't be held, stowed in the backpack or set
/// into a storage slot (each says why); it exists only as a body in the world, moved with the physics gun, carried in a
/// cargo volume, or built onto something (the welder's join, or the wrench's for a loose wrench part). Every path that
/// would give one to a player goes through <see cref="Give"/>, which drops it as a body where it was; taking one off a
/// construct cuts it free there; the creative inventory and the rack spawn it as a body in front of the player.
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>How far in front of the eye a spawned physics-only part goes at least (metres to its nearest side).</summary>
    public const float SpawnGap = 0.8f;

    /// <summary>"the Frame Block is physics-only (welded): it can't go in the backpack - ...": the one wording every refusal uses.</summary>
    public static string PhysicsOnlyRefusal(ConstructPartDef d, string what) =>
        $"the {d.Name} is physics-only ({d.PhysicsWhy}): it can't {what} - move it with the physics gun or carry it in a cargo volume";

    /// <summary>What a part is, for the inventory tile and the look text: "item", or "physics only (welded)".</summary>
    public static string ClassText(ConstructPartDef d) => d.PhysicsOnly ? $"physics only ({d.PhysicsWhy})" : "item";

    /// <summary>Creative (the map's infinite parts on, as the physics gun and the contested rule read it): wrench fits and removals are instant there.</summary>
    public bool Creative => InfiniteParts;

    /// <summary>
    /// Server: physics-only part <paramref name="def"/> spawned as a body in front of the player (a short way ahead at eye
    /// level, clear of the map, constructs and players; it then falls and settles), the player's own. With the physics gun
    /// out, the gun takes hold of it. Null, or why not.
    /// </summary>
    public string? SpawnInFront(ConstructWorld world, byte player, ConstructPartDef def, Vector3 eye, float yaw, out Construct? made)
    {
        made = null;
        if (!float.IsFinite(eye.X + eye.Y + eye.Z + yaw))
            return "nowhere to put it";
        if (ConstructPlace.LimitRefusal(world, player) is { } full)
            return full;
        var fwd = World.FlatForward(yaw);
        var rot = ConstructGrab.YawQ(yaw);
        var mid = (def.BoundsMin + def.BoundsMax) * 0.5f;
        var half = (def.BoundsMax - def.BoundsMin) * 0.5f;
        var deep = MathF.Max(half.X, half.Z); // (its reach from its centre along the look, whichever way it is turned)
        Span<float> dists = [deep + SpawnGap, deep + SpawnGap + 0.6f, deep + SpawnGap + 1.4f];
        Span<float> lifts = [0f, 0.6f, -0.4f];
        foreach (var lift in lifts)
            foreach (var dist in dists)
            {
                var centre = eye + fwd * dist + new Vector3(0, lift, 0);
                var fp = new FreePlacement { Pos = centre - Vector3.Transform(mid, rot), Rot = rot };
                if (ConstructPlace.Fits(world, Query, def, fp, Feet(world)) != null)
                    continue;
                if (ConstructBuild.PlaceNew(world, def, fp) is not { } c)
                    return "the world is full of constructs";
                c.Owner = player;
                made = c;
                Done.Add($"player {player}: {def.Name} spawned as construct {c.Id} in front of them (physics-only: {def.PhysicsWhy})");
                if (Hands[player].Tool == ConstructTool.PhysicsGun && !Hands[player].Seated)
                {
                    c.Rebuild();
                    Grab(world, player, eye, yaw, c.Id, c.Com); // (the physics gun out: it holds what came)
                }
                return null;
            }
        return $"no room in front of you for the {def.Name}";
    }

    /// <summary>
    /// What a part in the hands, a backpack or storage would be if it were physics-only (an old save's, or a part file that
    /// became physics-only): it drops into the world as a body. A world load calls this; the server also runs it at every
    /// match start. <paramref name="eyeOf"/> says where each player is (null: not here; their hands and pack stay). Returns
    /// how many parts dropped.
    /// </summary>
    public int DropPhysicsOnly(ConstructWorld world, Func<byte, Vector3?> eyeOf)
    {
        var dropped = 0;
        for (var pid = 0; pid < Hands.Length; pid++)
        {
            ref var h = ref Hands[pid];
            var eye = eyeOf((byte)pid);
            if (eye is not { } at)
                continue;
            if (ConstructParts.Get(h.Held) is { PhysicsOnly: true } held)
            {
                h.Held = ConstructParts.None;
                h.Edit = EditKind.None;
                if (DropLoose(world, held, at + new Vector3(0, -0.4f, 0) + new Vector3(0.9f, 0, 0), Quaternion.Identity) is { } c)
                {
                    c.Owner = (byte)pid;
                    dropped++;
                }
                Notices.Add(((byte)pid, $"{held.Name} dropped: physics-only parts aren't carried"));
            }
            var pack = Packs[pid];
            for (var k = 0; k < pack.SlotCount; k++)
            {
                if (ConstructParts.Get(pack.Def[k]) is not { PhysicsOnly: true } d)
                    continue;
                var a = k * MathF.Tau / Math.Max(1, (int)pack.SlotCount);
                if (DropLoose(world, d, at + new Vector3(MathF.Cos(a) * 1.2f, -0.6f, MathF.Sin(a) * 1.2f), Quaternion.Identity) is { } c)
                {
                    c.Owner = (byte)pid;
                    dropped++;
                }
                pack.Def[k] = ConstructParts.None;
                pack.Count[k] = 0;
                pack.Rot[k] = 0;
                pack.Version++;
                PackDirty.Add((byte)pid);
            }
        }
        // Storage: each physics-only part set into a slot drops beside its container.
        for (var bi = world.Bodies.Count - 1; bi >= 0; bi--)
        {
            var c = world.Bodies[bi];
            for (var i = 0; i < c.PartCount; i++)
            {
                if (world.StoreOf(c, i) is not { } st)
                    continue;
                for (var k = 0; k < st.SlotCount; k++)
                    while (ConstructParts.Get(st.Def[k]) is { PhysicsOnly: true })
                    {
                        if (world.TakeOut(c, i, k) is not { } d)
                            break;
                        c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
                        c.PartBounds(i, out var mn, out var mx);
                        var top = t + Vector3.Transform(new Vector3((mn.X + mx.X) * 0.5f, mx.Y + 0.6f, (mn.Z + mx.Z) * 0.5f), q);
                        if (DropLoose(world, d, top, q) != null)
                            dropped++;
                    }
            }
        }
        if (dropped > 0)
            Done.Add($"{dropped} physics-only part(s) dropped out of hands, backpacks and storage into the world");
        return dropped;
    }

    /// <summary>
    /// The tool that joins construct <paramref name="c"/> to <paramref name="other"/>: the welder only (decision 27: what
    /// isn't attached yet is a physics-only piece, and the welder attaches those; the wrench is for small item parts).
    /// </summary>
    public static bool JoinTool(ConstructTool tool, Construct c, Construct other) => tool == ConstructTool.Welder;

    /// <summary>Creative: a wrench fit or removal done at once (no timed progress); survival keeps the timed edits.</summary>
    private string? FinishNow(ConstructWorld world, byte player, Vector3 eye)
    {
        ref var h = ref Hands[player];
        if (h.Edit == EditKind.None || h.Duration > 0)
            return null;
        var kind = h.Edit;
        h.Edit = EditKind.None;
        string result;
        world.MergePlacer = player;
        try
        {
            result = Complete(world, player, ref h, kind, eye);
        }
        finally
        {
            world.MergePlacer = 255;
        }
        Done.Add($"player {player}: {kind} {result} (instant: creative wrench)");
        GiveMergePops(world, player);
        return result.StartsWith("failed: ", StringComparison.Ordinal) ? result[8..] : null;
    }

    /// <summary>The edit's time: the part's, except a wrench edit in creative, which is instant.</summary>
    private double EditSeconds(ConstructTool tool, float seconds) => Creative && tool == ConstructTool.Wrench ? 0 : seconds;
}
