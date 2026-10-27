using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Zombies slot machine wins everyone can see (board item "a differently shaped slot machine for each part category, with
/// a visible win your teammates can see", 2026-10-26; docs/zombies/ZOMBIES-MODE.md, "Slot machines"). When a machine pays
/// a part or a melee build the server sends <see cref="Msg.ZSlotWin"/> to everyone (protocol 55): every PC then shows the
/// won part's own model turning over the machine in its rarity colour for <see cref="ZombiesRules.SlotWinSeconds"/> (a
/// light pillar and sparks for legendary and wonder; Program.ZombiesSlots.cs), a label naming the winner and the part,
/// and a line in the kill feed in the rarity colour. The server rolls and pays; the message only tells.
/// </summary>
public sealed partial class Game
{
    /// <summary>Bytes of a <see cref="Msg.ZSlotWin"/> after its id: machine, player, result, index (2), tier, flags.</summary>
    public const int ZSlotWinBytes = 7;

    /// <summary>Server: machine <paramref name="i"/> paid <paramref name="p"/>: show it here and tell every client.</summary>
    private void ServerSlotWin(int i, ZSlotMachine m, Player p)
    {
        ApplySlotWin(i, p.Id, m.Result, m.Part, m.Tier, m.AutoScrapped);
        if (Net.IsOffline)
            return;
        WriteSlotWin(_w, i, m);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private static void WriteSlotWin(PacketWriter w, int i, ZSlotMachine m) =>
        w.Begin(Msg.ZSlotWin).U8((byte)i).U8(m.WinPlayer).U8((byte)m.WinResult).U16((ushort)m.WinIndex).U8((byte)m.WinTier).U8(m.WinScrapped ? (byte)1 : (byte)0);

    /// <summary>Client: a <see cref="Msg.ZSlotWin"/> (after the id).</summary>
    private void ReadSlotWin(ref PacketReader r)
    {
        int i = r.U8();
        var player = r.U8();
        var result = (ZSlotResult)Math.Min(r.U8(), (byte)ZSlotResult.Melee);
        var index = (short)r.U16();
        var tier = (Rarity)Math.Min(r.U8(), (byte)Rarity.Wonder);
        var scrapped = (r.U8() & 1) != 0;
        if (result is ZSlotResult.Part or ZSlotResult.Melee)
            ApplySlotWin(i, player, result, index, tier, scrapped);
    }

    /// <summary>Both sides: machine <paramref name="i"/>'s win shows now (over the machine, a feed line, a sound).</summary>
    private void ApplySlotWin(int i, byte player, ZSlotResult result, short index, Rarity tier, bool scrapped)
    {
        if (i < 0 || i >= ZSlots.Length)
            return;
        var m = ZSlots[i];
        m.WinSeq = (byte)(m.WinSeq == 255 ? 1 : m.WinSeq + 1);
        m.WinPlayer = player;
        m.WinResult = result;
        m.WinIndex = index;
        m.WinTier = tier;
        m.WinScrapped = scrapped;
        m.WinAt = Brutal.GlfwApi.Glfw.Time;
        var category = World.Map.Zombies is { } zm && i < zm.SlotMachines.Count ? zm.SlotMachines[i].SlotCategory : -1;
        var mine = player == LocalId;
        var who = mine ? "You" : NameOf(player);
        var what = SlotWinWhat(result, index, tier);
        var line = scrapped
            ? $"{who} won {what} on the {SlotMachineName(category)} (scrapped: plenty already)"
            : $"{who} won {what} on the {SlotMachineName(category)}!";
        Feed.Add(new FeedEntry(Brutal.GlfwApi.Glfw.Time, line, mine, PartDef.RarityColor(tier)));
        Console.WriteLine(line);
        if (World.Map.Zombies is { } z2 && i < z2.SlotMachines.Count)
            Audio.Effect3D(tier >= Rarity.Legendary ? Sfx.StreakReady : tier >= Rarity.Epic ? Sfx.StreakCall : Sfx.LevelUp, z2.SlotMachines[i].At + new Vector3(0, 1.6f, 0), tier >= Rarity.Legendary ? 1f : 0.8f);
        if (tier >= Rarity.Legendary && !scrapped)
            Audio.Effect(Sfx.StreakReady, 0.5f, MixBus.Notify); // a big win is heard across the map
    }

    /// <summary>"a Rare Long Barrel" / "the Fire Axe (Epic melee)".</summary>
    public static string SlotWinWhat(ZSlotResult result, short index, Rarity tier) => result switch
    {
        ZSlotResult.Part when index >= 0 && index < Parts.All.Length => $"{(tier is Rarity.Uncommon or Rarity.Epic ? "an" : "a")} {PartDef.RarityName(tier)} {Parts.All[index].Name}",
        ZSlotResult.Melee when index >= 0 && index < StandardBuilds.All.Length => $"the {StandardBuilds.All[index].Name} ({PartDef.RarityName(tier)} melee)",
        _ => "something",
    };

    /// <summary>Is machine <paramref name="i"/>'s last win still showing at <paramref name="now"/> (this PC's clock)?</summary>
    public bool SlotWinShowing(int i, double now) => i >= 0 && i < ZSlots.Length && ZSlots[i].WinSeq != 0 && now - ZSlots[i].WinAt < ZRules.SlotWinSeconds;

    // ---- Self-test hooks -----------------------------------------------------------------------------

    /// <summary>Self-test: the <see cref="Msg.ZSlotWin"/> the server sent for machine <paramref name="i"/>'s last win.</summary>
    public void SelfTestWriteSlotWin(PacketWriter w, int i) => WriteSlotWin(w, i, ZSlots[i]);

    /// <summary>Self-test: a client reads a whole <see cref="Msg.ZSlotWin"/>.</summary>
    public void SelfTestReadSlotWin(ReadOnlySpan<byte> msg)
    {
        var r = new PacketReader(msg[1..]);
        ReadSlotWin(ref r);
    }
}
