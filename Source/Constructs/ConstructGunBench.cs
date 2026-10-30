using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Gun Workbench's side of the hands (board item dev-sandbox-weapon-parts-wp01, part 2; docs/sandbox/CONSTRUCTS.md,
/// "The Gun Workbench"): which bench a player is at, which builder parts they have (the part in the hands, the backpack
/// and the bench's own slots, each a wrench item from <see cref="ConstructWeaponParts"/>), taking one away when a build
/// uses it, and giving one back: the bench's slots first, then <see cref="Give"/> (the hands, the backpack, the ground).
/// The server only. The build itself (the weapon, the checks, the sync) is in GameSandboxBench.cs.
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>How far from a bench's middle a player can work at it (a bench is 2.4 m wide).</summary>
    public const float BenchReach = ConstructBuild.UseReach + 1.4f;

    /// <summary>The nearest Gun Workbench within reach of <paramref name="eye"/>, or null.</summary>
    public static (Construct Body, int Part)? FindGunBench(ConstructWorld world, Vector3 eye)
    {
        (Construct, int)? best = null;
        var bestD = BenchReach;
        foreach (var c in world.Bodies)
            for (var i = 0; i < c.PartCount; i++)
            {
                if (!c.DefOf(i).IsGunBench)
                    continue;
                var at = BenchCentre(c, i);
                var d = Vector3.Distance(eye, at);
                if (d <= bestD)
                {
                    bestD = d;
                    best = (c, i);
                }
            }
        return best;
    }

    /// <summary>A bench's middle in the world.</summary>
    public static Vector3 BenchCentre(Construct c, int part)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        return t + Vector3.Transform((mn + mx) * 0.5f, q);
    }

    /// <summary>
    /// Adds up the builder parts <paramref name="player"/> has at the bench (by builder part name): the part in the hands,
    /// the bench's slots and the backpack. Fills <paramref name="into"/> (cleared first).
    /// </summary>
    public void BenchCollect(ConstructWorld world, byte player, Construct bench, int part, Dictionary<string, int> into)
    {
        into.Clear();
        void Add(ushort id, int n)
        {
            if (n > 0 && ConstructParts.Get(id) is { } d && ConstructWeaponParts.NameOf(d) is { Length: > 0 } name)
                into[name] = into.GetValueOrDefault(name) + n;
        }
        Add(Hands[player].Held, 1);
        if (world.StoreOf(bench, part) is { } st)
            for (var k = 0; k < st.SlotCount; k++)
                Add(st.Def[k], st.Count[k]);
        var pack = Packs[player];
        for (var k = 0; k < pack.SlotCount; k++)
            Add(pack.Def[k], pack.Count[k]);
    }

    /// <summary>Takes one item standing for builder part <paramref name="name"/> away: from the bench's slots, then the backpack, then the hands. False if there is none.</summary>
    public bool BenchTake(ConstructWorld world, byte player, Construct bench, int part, string name)
    {
        bool Is(ushort id) => ConstructParts.Get(id) is { } d && ConstructWeaponParts.NameOf(d).Equals(name, StringComparison.OrdinalIgnoreCase);
        if (world.StoreOf(bench, part) is { } st)
            for (var k = 0; k < st.SlotCount; k++)
                if (st.Count[k] > 0 && Is(st.Def[k]))
                {
                    world.TakeOut(bench, part, k);
                    return true;
                }
        var pack = Packs[player];
        for (var k = 0; k < pack.SlotCount; k++)
            if (pack.Count[k] > 0 && Is(pack.Def[k]))
            {
                if (--pack.Count[k] == 0)
                {
                    pack.Def[k] = ConstructParts.None;
                    pack.Rot[k] = 0;
                }
                pack.Version++;
                PackDirty.Add(player);
                return true;
            }
        ref var h = ref Hands[player];
        if (h.Held != ConstructParts.None && Is(h.Held))
        {
            h.Held = ConstructParts.None;
            h.Edit = EditKind.None;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gives a part back to the player at a bench: the bench's own slots first (a pile stacks), then <see cref="Give"/>
    /// (the hands, the backpack, the ground by the bench). Never loses it.
    /// </summary>
    public GiveResult BenchGive(ConstructWorld world, byte player, Construct bench, int part, ConstructPartDef def)
    {
        if (world.StoreOf(bench, part) is { } st)
            for (var k = 0; k < st.SlotCount; k++)
                if (ConstructStorage.Check(bench.DefOf(part), st, k, def, out _) == SlotCheck.Ok && world.SetIn(bench, part, k, def) == null)
                {
                    Done.Add($"player {player}: {def.Name} into slot {k} of the {bench.DefOf(part).Name} (construct {bench.Id})");
                    return GiveResult.Bench;
                }
        var centre = BenchCentre(bench, part);
        return Give(world, player, def, centre + new Vector3(0, 1.2f, 0));
    }
}
