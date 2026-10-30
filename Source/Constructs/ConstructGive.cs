using System.Numerics;

namespace CallOfSlop;

/// <summary>Where <see cref="ConstructEdits.Give"/> put a part.</summary>
public enum GiveResult : byte
{
    Hands, // the hands were empty
    Backpack, // the hands were full: into the backpack (the Stow rules: a free slot it fits, within the weight limit)
    Dropped, // hands and backpack full: a loose pickup where it came from
    Inventory, // the world had no room for another construct: counted in the inventory instead
    Lost, // nowhere at all (logged; never expected)
}

/// <summary>
/// The one rule for handing a part to a player (board item rc01, 2026-10-30): the hands if they are empty, else the
/// backpack (the same fit and weight rules as the Stow key), else a loose pickup dropped where the part was (it falls
/// and rests, like any dropped part). What the hands already hold stays there. Every path that gives a part to a player
/// calls <see cref="Give"/> (taking a part off, salvage into the hands, receiver-merge pop-offs), so none of them can
/// lose the part or assume the hands are free. A physics-only part (decision 21: welded, over the hand-carry limit, or
/// bulky) never goes to the hands or the backpack: it is dropped as a body where it was. A full-hands outcome tells the player why on the HUD (<see cref="Notices"/>).
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>Server: HUD lines for one player each (the game sends them as notices, then clears the list).</summary>
    public readonly List<(byte Player, string Text)> Notices = new(8);

    /// <summary>
    /// Server: part <paramref name="def"/> goes to <paramref name="player"/>: the hands, then the backpack, then dropped as
    /// a loose pickup centred on <paramref name="where"/> (turned <paramref name="rot"/>). Never throws, never loses it.
    /// </summary>
    public GiveResult Give(ConstructWorld world, byte player, ConstructPartDef def, Vector3 where, Quaternion? rot = null)
    {
        ref var h = ref Hands[player];
        CheckHand(player, ref h);
        if (def.PhysicsOnly)
        {
            // Decision 21: never an item: a body where it came from (ConstructPhysicsOnly.cs).
            if (DropLoose(world, def, where, rot ?? Quaternion.Identity) is { } body)
            {
                body.Owner = player;
                Notices.Add((player, $"{def.Name} left as a body: physics-only ({def.PhysicsWhy})"));
                Done.Add($"player {player}: {def.Name} dropped as construct {body.Id} (physics-only: {def.PhysicsWhy})");
                return GiveResult.Dropped;
            }
            if (Inventory != null && Inventory.Of(player).Add(def.Id))
                return GiveResult.Inventory;
            Console.WriteLine($"[sandbox] error: the physics-only {def.Name} for player {player} had nowhere to go (the world is full)");
            return GiveResult.Lost;
        }
        if (h.Held == ConstructParts.None && world.HoseHeldBy(player) < 0) // (a hose's receiver in the hands counts as full hands: board item wl01)
        {
            h.Held = def.Id;
            return GiveResult.Hands;
        }
        var pack = Packs[player];
        if (Backpack != null && PackSlotFor(pack, def, out _, out var turn) is var slot and >= 0)
        {
            pack.Def[slot] = def.Id;
            pack.Rot[slot] = turn;
            pack.Count[slot] = 1;
            pack.Health[slot] = 1f;
            pack.Version++;
            PackDirty.Add(player);
            Notices.Add((player, $"{def.Name} into the backpack: hands full"));
            Done.Add($"player {player}: {def.Name} into backpack slot {slot} (hands full)");
            return GiveResult.Backpack;
        }
        if (DropLoose(world, def, where, rot ?? Quaternion.Identity) is { } loose)
        {
            loose.Owner = player;
            Notices.Add((player, $"{def.Name} dropped: hands full"));
            Done.Add($"player {player}: {def.Name} dropped as construct {loose.Id} (hands full)");
            return GiveResult.Dropped;
        }
        if (Inventory != null && Inventory.Of(player).Add(def.Id))
        {
            Notices.Add((player, $"{def.Name} into the inventory: hands full and no room to drop it"));
            return GiveResult.Inventory;
        }
        Console.WriteLine($"[sandbox] error: the {def.Name} for player {player} had nowhere to go (hands, backpack and world full)");
        return GiveResult.Lost;
    }

    /// <summary>A loose single-part construct with its centre at <paramref name="centre"/>: it falls and rests like any dropped part.</summary>
    public static Construct? DropLoose(ConstructWorld world, ConstructPartDef def, Vector3 centre, Quaternion rot)
    {
        if (!float.IsFinite(centre.X + centre.Y + centre.Z))
            return null;
        rot = rot.LengthSquared() > 0.5f && float.IsFinite(rot.X + rot.Y + rot.Z + rot.W) ? Quaternion.Normalize(rot) : Quaternion.Identity;
        var mid = (def.BoundsMin + def.BoundsMax) * 0.5f;
        return ConstructBuild.PlaceNew(world, def, new FreePlacement { Pos = centre - Vector3.Transform(mid, rot), Rot = rot });
    }

    /// <summary>
    /// A hand whose part id names no loaded part (a bad state: a stale id, a hot-reloaded part file) is logged and
    /// emptied, so nothing downstream indexes the parts list with it. Cheap: run on every hand every tick.
    /// </summary>
    private static void CheckHand(byte player, ref SandboxHand h)
    {
        if (h.Held == ConstructParts.None || ConstructParts.Get(h.Held) != null)
            return;
        Console.WriteLine($"[sandbox] error: player {player}'s hands held part id {h.Held}, which isn't a loaded part: emptied");
        h.Held = ConstructParts.None;
        h.Edit = EditKind.None;
    }
}
