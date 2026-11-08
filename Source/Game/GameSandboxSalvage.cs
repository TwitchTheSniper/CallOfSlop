using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox milestone 3, salvage (docs/sandbox/CONSTRUCTS.md "Salvage and the inventory"): every player's inventory on
/// the server (part counts and scrap; <see cref="SandboxInventory"/>), sent to its owner alone when it changes; the
/// map's rules (infinite parts, debris expiry) handed to the edit rules; wrecks cleared away when nobody is near; and
/// a leaving player's inventory and constructs let go. The taking apart itself is in ConstructEdits (BeginRemove and
/// Complete).
/// </summary>
public sealed partial class Game
{
    /// <summary>Every player's inventory (server: the real one; client: its own player's, as last heard).</summary>
    public readonly SandboxInventories SandboxInv = new();

    /// <summary>
    /// A player's inventory: part counts by construct part id, scrap, materials (the inventory window reads it; on a
    /// client only the local player's is known). <see cref="SandboxInventory.Version"/> goes up on every change.
    /// </summary>
    public SandboxInventory SandboxInventory(byte player) => SandboxInv.Of(player);

    /// <summary>The inventory window's count badges (Ui.SandboxCounts): the player's count, or "no limit" while parts are infinite.</summary>
    public sealed class InventoryCounts : ISandboxPartCounts
    {
        public static readonly InventoryCounts Instance = new();

        public int PartCount(Game game, byte playerId, ushort partId)
        {
            var inv = game.SandboxInventory(playerId);
            return inv.Infinite ? -1 : inv.Count(partId) + game.SandboxBeacon(playerId).Counts[partId]; // (a beacon in range: what its network holds; out of range the view is empty)
        }
    }

    private const int DebrisCheckEvery = 30; // ticks (half a second)

    /// <summary>Server: the edit rules get the inventories and the world's rules (cheap: every tick and every action).</summary>
    private void SandboxRules()
    {
        SandboxHands.Inventory = SandboxInv;
        SandboxHands.Query = World.Query; // (free placement checks the map)
        SandboxPhysGunRules(); // (the contested rule's lookups; GameSandboxPhysGun.cs)
        SandboxShareRules(); // (creative or survival: GameSandboxSurvival.cs)
    }

    /// <summary>Server, each tick after the edits: wrecks nobody is near or salvaging go after the map's time.</summary>
    private void SandboxDebris(double now)
    {
        if (ServerTick % DebrisCheckEvery != 0 || World.Map.Sandbox is not { } sb)
            return;
        Constructs.ExpireDebris(now, sb.DebrisSeconds, sb.DebrisNear, SandboxHands);
    }

    /// <summary>Server: each client whose inventory changed gets it (the host's own needs nothing sent).</summary>
    private void SandboxInventoryFlush()
    {
        foreach (var (_, cl) in _clients)
        {
            if (!SandboxInv.Changed(cl.Id))
                continue;
            ConstructNet.WriteInventory(SbW, SandboxInv.Of(cl.Id));
            Net.SendTo(cl.Guid, SbW.Span, PacketReliability.ReliableOrdered);
            SandboxInv.MarkSent(cl.Id);
        }
        if (LocalId != byte.MaxValue)
            SandboxInv.MarkSent(LocalId);
    }

    /// <summary>Server: a joiner's own inventory (empty, unless the id came back within the match).</summary>
    private void SendSandboxInventory(RakNetGUID to)
    {
        if (!_clients.TryGetValue(to.G, out var cl))
            return;
        ConstructNet.WriteInventory(SbW, SandboxInv.Of(cl.Id));
        Net.SendTo(to, SbW.Span, PacketReliability.ReliableOrdered);
        SandboxInv.MarkSent(cl.Id);
    }

    /// <summary>Server: scrap counted by an older inventory becomes metal in bins beside the player (board item sh01; ConstructLegacyScrap.cs). Returns a log line.</summary>
    public string SandboxImportLegacyScrap(byte player, int scrap)
    {
        if (!IsServer || !Players.TryGetValue(player, out var p))
            return "not the server, or no such player";
        return ConstructLegacyScrap.Import(Constructs, SandboxInv.Of(player), p.Position, p.Yaw, scrap);
    }

    /// <summary>Server: a player leaves: their inventory empties (the id may be someone else's next) and what they built is nobody's.</summary>
    internal void SandboxLeave(Player p)
    {
        if (!IsServer)
            return;
        SandboxSpawnsOrphan(p); // (what they spawned is nobody's now: GameSandboxSpawns.cs)
        OpeningLeave(p); // (mid-opening: kept as if woken in the colony, GameSandboxOpening.cs)
        var kept = WorldLeaves(p); // (a saved world: what they have and what they built wait for them, GameWorldSave.cs)
        SandboxDeathLeave(p.Id); // (after their record took their checkpoint: GameSandboxDeath.cs)
        if (!kept)
            FactionPlayerGone(p, 255); // (out of their faction: GameSandboxFactions.cs)
        SandboxInv.Clear(p.Id);
        SandboxBeaconReset(p.Id);
        SandboxTerminalReset(p.Id);
        ZPlayers.Remove(p.Id); // (a built weapon and the bench stock go with them)
        SandboxHands.GrabLeave(Constructs, p.Id); // (the physics gun lets go; what they froze is nobody's)
        if (p.Alive && !kept)
            SandboxHands.DropPack(Constructs, p.Id, p.Position, p.Yaw); // (what a leaving player carried stays in the world)
        SandboxHands.Pack(p.Id).Reset(SandboxHands.Pack(p.Id).SlotCount);
        SandboxHands.PackDirty.Add(p.Id);
        if (kept)
            return;
        foreach (var c in Constructs.Bodies)
            if (c.Owner == p.Id)
            {
                c.Owner = 255;
                Constructs.StructureDirty.Add(c.Id);
            }
    }
}
