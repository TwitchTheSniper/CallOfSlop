namespace CallOfSlop;

/// <summary>
/// A magazine's rounds while a player carries it away from the hands. A magazine in the hands has them in <see cref="SandboxHand.MagPack"/>.
/// Put away (the backpack, the inventory, a store), a part is only a part id in a count, so the rounds stay with the player: they are noted
/// here by kind, and the fullest one of that kind comes back when a magazine of it is next in their hands. So a backpack or inventory keeps
/// them without a new field in a slot, on the wire or in a save. Server only, kept for this session: a world save keeps a magazine's rounds
/// while it is fitted or lying loose, not while it is carried.
/// </summary>
public sealed partial class ConstructEdits
{
    private const int MagsPerPlayer = 64; // (more than a backpack and hands hold: past it the emptiest go)

    private readonly Dictionary<byte, List<(ushort Def, ushort Pack)>> _magsPut = new();

    /// <summary>Every tick, each hand: a magazine that left the hands leaves its rounds noted; one that came into them takes the fullest noted of its kind.</summary>
    private void CarryMagazine(byte player, ref SandboxHand h)
    {
        if (h.MagFor != 0 && h.MagFor != h.Held)
        {
            MagPut(player, h.MagFor, h.MagPack);
            h.MagFor = h.MagPack = 0;
        }
        if (h.MagFor == 0 && h.Held != ConstructParts.None && _magsPut.Count > 0 && MagTake(player, h.Held) is { } pack)
            (h.MagFor, h.MagPack) = (h.Held, pack);
    }

    private void MagPut(byte player, ushort def, ushort pack)
    {
        if (ConstructAmmo.PackRounds(pack) == 0)
            return;
        if (!_magsPut.TryGetValue(player, out var list))
            _magsPut[player] = list = new List<(ushort, ushort)>(MagsPerPlayer + 1);
        list.Add((def, pack));
        if (list.Count <= MagsPerPlayer)
            return;
        var least = 0;
        for (var k = 1; k < list.Count; k++)
            if (ConstructAmmo.PackRounds(list[k].Pack) < ConstructAmmo.PackRounds(list[least].Pack))
                least = k;
        list.RemoveAt(least);
    }

    private ushort? MagTake(byte player, ushort def)
    {
        if (!_magsPut.TryGetValue(player, out var list))
            return null;
        var best = -1;
        for (var k = 0; k < list.Count; k++)
            if (list[k].Def == def && (best < 0 || ConstructAmmo.PackRounds(list[k].Pack) > ConstructAmmo.PackRounds(list[best].Pack)))
                best = k;
        if (best < 0)
            return null;
        var pack = list[best].Pack;
        list.RemoveAt(best);
        if (list.Count == 0)
            _magsPut.Remove(player);
        return pack;
    }

    private void MagsForget(byte player) => _magsPut.Remove(player);

    /// <summary>A magazine came off something with these rounds: into the hands with them, else noted as put away.</summary>
    private void MagazineBack(byte player, ref SandboxHand h, ConstructPartDef def, GiveResult given, ushort pack)
    {
        if (!ConstructAmmo.IsMagazine(def))
            return;
        if (given == GiveResult.Hands)
            (h.MagFor, h.MagPack) = (def.Id, pack);
        else
            MagPut(player, def.Id, pack);
    }

    /// <summary>A dead player's backpack spills: a magazine on the ground has its rounds in it.</summary>
    private void LoadDropped(ConstructWorld world, byte player, Construct loose, ConstructPartDef d)
    {
        if (ConstructAmmo.IsMagazine(d) && MagTake(player, d.Id) is { } pack)
        {
            loose.Parts[0].Rounds = pack;
            world.NoteAmmo(loose, 0);
        }
    }
}
