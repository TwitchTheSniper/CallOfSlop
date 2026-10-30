using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Every player's backpack (the developer's answer on carrying, 2026-10-30: the hands plus a visible backpack of about
/// eight slots of different sizes; no inventory screen; docs/sandbox/CONSTRUCTS.md "The backpack"). Its layout is the
/// <c>Backpack</c> storage part's slots (data: Constructs/Parts/Storage/Backpack.json), so the same fitting rules apply
/// (a part's box must fit a slot; its <c>maxMass</c> keeps heavy parts out). The Stow key puts the part in the hands into
/// the smallest free slot it fits; Draw takes the next stored part out into empty hands. The server decides; every
/// change goes to every client (everyone's pack is drawn on their back); a death drops the pack's contents as loose parts.
/// </summary>
public sealed partial class ConstructEdits
{
    public const string BackpackPart = "Backpack";

    /// <summary>Each player's backpack contents (fixed: one store a player id, made once).</summary>
    public readonly CStore[] Packs = MakePacks();

    /// <summary>Server: the players whose packs changed (sent after the tick's structures).</summary>
    public readonly HashSet<byte> PackDirty = new(64);

    private readonly byte[] _packCursor = new byte[256];

    private static CStore[] MakePacks()
    {
        var a = new CStore[256];
        var n = Backpack?.Slots.Length ?? 0;
        for (var k = 0; k < a.Length; k++)
        {
            a[k] = new CStore { Handle = (ushort)k, Live = n > 0 };
            a[k].Reset(n);
        }
        return a;
    }

    /// <summary>The backpack's layout (the <c>Backpack</c> storage part), or null when its file is missing.</summary>
    public static ConstructPartDef? Backpack => _backpack ??= ConstructParts.Find(BackpackPart) is { Slots.Length: > 0 } d ? d : null;

    private static ConstructPartDef? _backpack;

    public CStore Pack(byte player) => Packs[player];

    /// <summary>Empties every pack (a new match).</summary>
    public void ResetPacks()
    {
        var n = Backpack?.Slots.Length ?? 0;
        foreach (var p in Packs)
            p.Reset(n);
        Array.Clear(_packCursor);
        PackDirty.Clear();
    }

    /// <summary>The slot <paramref name="d"/> would go into: the smallest free one it fits (by volume), or -1; and why not.</summary>
    public static int PackSlotFor(CStore pack, ConstructPartDef d, out SlotCheck why, out byte rot)
    {
        why = SlotCheck.NoSlot;
        rot = 0;
        if (Backpack is not { } bp)
            return -1;
        var best = -1;
        var bestVol = float.MaxValue;
        bool heavy = false, taken = false;
        for (var k = 0; k < bp.Slots.Length && k < pack.SlotCount; k++)
        {
            var c = ConstructStorage.Check(bp, pack, k, d, out var r);
            heavy |= c == SlotCheck.TooHeavy;
            taken |= c is SlotCheck.Taken or SlotCheck.OtherKind or SlotCheck.Full;
            if (c != SlotCheck.Ok)
                continue;
            var size = bp.Slots[k].Size;
            var vol = size.X * size.Y * size.Z;
            if (vol < bestVol)
            {
                bestVol = vol;
                best = k;
                rot = r;
            }
        }
        why = heavy ? SlotCheck.TooHeavy : taken ? SlotCheck.Taken : SlotCheck.TooBig;
        if (best >= 0)
            why = SlotCheck.Ok;
        return best;
    }

    /// <summary>Server: the part in the hands into the backpack (any hands, not seated). Null, or why not.</summary>
    public string? Stow(byte player)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (ConstructParts.Get(h.Held) is not { } def)
            return "nothing in your hands to stow";
        if (Backpack is not { } bp)
            return "no backpack";
        var pack = Packs[player];
        var slot = PackSlotFor(pack, def, out var why, out var rot);
        if (slot < 0)
            return why switch
            {
                SlotCheck.TooHeavy => $"the {def.Name} is too heavy for the backpack ({def.Mass:0} kg; it takes {bp.StoreMaxMass:0})",
                SlotCheck.Taken => $"no free backpack slot takes the {def.Name}",
                _ => $"the {def.Name} is too big for the backpack (carry it in your hands)",
            };
        pack.Def[slot] = def.Id;
        pack.Rot[slot] = rot;
        pack.Count[slot] = 1;
        pack.Health[slot] = 1f;
        pack.Version++;
        PackDirty.Add(player);
        h.Held = ConstructParts.None;
        h.Edit = EditKind.None;
        Done.Add($"player {player}: stowed {def.Name} in backpack slot {slot}");
        return null;
    }

    /// <summary>Server: the next part in the backpack (after the last one drawn, round the slots) into empty hands. Null, or why not.</summary>
    public string? DrawFromPack(byte player)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Held != ConstructParts.None)
            return "your hands are full (stow or put down what you hold first)";
        var pack = Packs[player];
        var n = pack.SlotCount;
        for (var step = 1; step <= n; step++)
        {
            var k = (_packCursor[player] + step) % n;
            if (ConstructParts.Get(pack.Def[k]) is not { } d)
                continue;
            pack.Def[k] = ConstructParts.None;
            pack.Count[k] = 0;
            pack.Rot[k] = 0;
            pack.Version++;
            PackDirty.Add(player);
            _packCursor[player] = (byte)k;
            h.Held = d.Id;
            Done.Add($"player {player}: drew {d.Name} from backpack slot {k}");
            return null;
        }
        return "the backpack is empty";
    }

    /// <summary>Server: a player died or left: what their backpack held drops where they were, as loose parts anyone may pick up.</summary>
    public int DropPack(ConstructWorld world, byte player, Vector3 feet, float yaw)
    {
        var pack = Packs[player];
        var dropped = 0;
        var fwd = World.FlatForward(yaw);
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        var had = pack.Items;
        for (var k = 0; k < pack.SlotCount; k++)
        {
            if (ConstructParts.Get(pack.Def[k]) is not { } d)
                continue;
            // Round the body in a little ring, so the pickups don't land in one another.
            var a = k * MathF.Tau / Math.Max(1, (int)pack.SlotCount);
            var at = feet + (right * MathF.Cos(a) + fwd * MathF.Sin(a)) * 0.9f + new Vector3(0, 0.3f, 0);
            if (ConstructBuild.PlaceNew(world, d, at, yaw) is { } loose)
            {
                loose.Owner = 255; // (anyone's to pick up)
                dropped++;
            }
            pack.Def[k] = ConstructParts.None;
            pack.Count[k] = 0;
            pack.Rot[k] = 0;
        }
        if (had > 0)
        {
            pack.Version++;
            PackDirty.Add(player);
            Done.Add($"player {player}: the backpack dropped {dropped} part(s)");
        }
        return dropped;
    }
}
