using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The server's half of the Inventory Beacon (ConstructBeacons.cs; docs/sandbox/CONSTRUCTS.md "The Inventory Beacon"), for a
/// player in survival in range of a working beacon of their own: parts and materials come off the network by radio, and
/// what would come back to them goes to the network first. Decision 18's deliberate exception to "survival stays physical".
/// </summary>
public sealed partial class ConstructEdits
{
    private readonly double[] _bulkNoteAt = new double[256];
    private const double BulkNoteSeconds = 2; // a material going to the network says so at most this often (digging comes in many small scoops)

    /// <summary>The networks a player at <paramref name="at"/> reaches (0: none, or creative: nothing is counted there).</summary>
    public ulong NetworkOf(ConstructWorld? world, byte player, Vector3? at) =>
        InfiniteParts || world == null || at is not { } spot ? 0 : world.Beacons.MaskFor(world, player, spot);

    /// <summary>Why the parts menu refuses a player: their beacon's reason, or the map's rule.</summary>
    private string NoMenuWhy(ConstructWorld? world, byte player, Vector3? at)
    {
        var why = !InfiniteParts && world != null && at is { } spot ? world.Beacons.Why(world, player, spot) : "";
        return why.Length > 0 ? why : "the parts menu is off on this map";
    }

    /// <summary>
    /// A part coming back to a player goes into the network, if they are in range and it has room: a store that already holds
    /// its kind first, then any free slot it fits. A physics-only part is never stored (decision 21): it stays a body, as
    /// ever. True when it went in; false: the caller's usual path (<see cref="Give"/>), with a HUD line when only room was short.
    /// </summary>
    internal bool ToNetwork(ConstructWorld world, byte player, ConstructPartDef def, Vector3 at)
    {
        if (def.PhysicsOnly || NetworkOf(world, player, at) is not (var net and not 0))
            return false;
        if (world.Beacons.PutPart(world, this, player, net, def) is { } into)
        {
            Notices.Add((player, $"{def.Name} into the network ({into})"));
            Done.Add($"player {player}: {def.Name} into the network ({into})");
            return true;
        }
        Notices.Add((player, $"no room on the network for the {def.Name}: it comes to you"));
        return false;
    }

    /// <summary>One of a part off the network into the hands (<see cref="Give"/>: the rules for a hose in the hands and so on). What they held goes back first: the network, else the backpack or the ground.</summary>
    private string? FromNetwork(ConstructWorld world, byte player, ConstructPartDef def, Vector3 eye, ulong net)
    {
        ref var h = ref Hands[player];
        if (!world.Beacons.TakePart(world, this, player, net, def))
            return $"no {def.Name} left on the network";
        if (ConstructParts.Get(h.Held) is { } held)
        {
            if (!ToNetwork(world, player, held, eye))
            {
                if (Survival || Inventory == null || !Inventory.Of(player).Add(held.Id)) // (a run-time part isn't counted: never lost)
                    Give(world, player, held, eye); // (hands full: the backpack, else set down)
            }
            h.Held = ConstructParts.None;
        }
        Give(world, player, def, eye);
        h.Edit = EditKind.None;
        Done.Add($"player {player}: took a {def.Name} off the network");
        return null;
    }

    /// <summary>
    /// A physics-only part off the network: a body in front of the player, as creative's menu gives it. Only a part that became
    /// physics-only while stored can be there: storage takes none. Checked first, so a world with no room takes nothing out.
    /// </summary>
    private string? PhysicsFromNetwork(ConstructWorld world, byte player, ConstructPartDef def, Vector3 eye, float yaw, ulong net)
    {
        if (!world.Beacons.Has(world, this, player, net, def))
            return $"no {def.Name} left on the network";
        if (SpawnInFront(world, player, def, eye, yaw, out _) is { } why)
            return why;
        world.Beacons.TakePart(world, this, player, net, def);
        Done.Add($"player {player}: took a {def.Name} off the network as a body");
        return null;
    }

    /// <summary>Takes a material off the network, filling what the player carries up to a load. Null, or why not.</summary>
    public string? TakeNetworkMaterial(ConstructWorld world, byte player, Vector3 eye, byte material)
    {
        if (Inventory == null || material == 0 || material >= SandboxInventory.MaxMaterials || material > RecyclingTable.Current.Materials.Length)
            return "no such material";
        if (NetworkOf(world, player, eye) is not (var net and not 0))
            return NoMenuWhy(world, player, eye);
        if (Hands[player].Seated)
            return "get out of the seat first";
        var inv = Inventory.Of(player);
        var name = RecyclingTable.Current.NameOf(material);
        var want = ConstructStorage.TakeLoad - inv.Materials[material];
        if (want <= 0)
            return $"you already carry a load of {name} ({inv.Materials[material]})";
        var got = world.Beacons.TakeBulk(world, this, player, net, material, want);
        if (got <= 0)
            return $"no {name} left on the network";
        inv.AddMaterial(material, got);
        Done.Add($"player {player}: took {got} {name} off the network");
        return null;
    }

    /// <summary>
    /// Materials a player has just been given: in range of a beacon as many as fit go on to its bins (those holding it, then
    /// empty ones), out of what they carry; the rest stay carried. How much went. Says so at most every few seconds.
    /// </summary>
    public int MaterialToNetwork(ConstructWorld world, byte player, Vector3 at, byte material, int amount, double now)
    {
        if (amount <= 0 || Inventory == null || NetworkOf(world, player, at) is not (var net and not 0))
            return 0;
        var put = world.Beacons.PutBulk(world, this, player, net, material, amount);
        if (put <= 0)
            return 0;
        Inventory.Of(player).TakeMaterial(material, put);
        if (now >= _bulkNoteAt[player])
        {
            _bulkNoteAt[player] = now + BulkNoteSeconds;
            Notices.Add((player, $"{RecyclingTable.Current.NameOf(material)} into the network"));
        }
        return put;
    }
}
