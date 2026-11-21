namespace CallOfSlop;

/// <summary>A robot shell's ore hopper while it is worn (Game/GameShellHopper.cs): what it holds, its room and its module.</summary>
public struct ShellHopper
{
    public byte Material;
    public int Amount, Capacity;
    public ShellModule? Module;
}

public sealed partial class ConstructEdits
{
    /// <summary>Server: each player's ore hopper while they wear a shell with one (docs/sandbox/design/mining-shell.md); empty otherwise.</summary>
    public readonly ShellHopper[] Hoppers = new ShellHopper[256];

    /// <summary>
    /// Server, F at a bin or a refinery's input by someone with a loaded ore hopper: the hopper tips in, as much as fits, by the
    /// same <see cref="ConstructWorld.Deposit"/> and <see cref="ConstructWorld.Feed"/> a carried load uses. "" when it tipped, a
    /// refusal to say, or null: the bin takes none of it, so F does what it would have done.
    /// </summary>
    private string? TipHopper(ConstructWorld world, byte player, Construct c, int part, ConstructPartDef d, CStore st)
    {
        ref var hp = ref Hoppers[player];
        var names = RecyclingTable.Current;
        var what = names.NameOf(hp.Material);
        int n;
        if (d.IsRefiner)
            n = world.Feed(c, part, hp.Amount, hp.Material);
        else if (d.IsAssembler || d.IsPress || d.IsPanelFormer || !ConstructStorage.BinTakes(d, hp.Material) || st.KindN > 0)
            return null;
        else if (st.Amount > 0 && st.Material != hp.Material)
            return $"the {d.Name} holds {names.NameOf(st.Material)}: the hopper's {what} goes in a bin of its own";
        else if (st.Amount >= ConstructStorage.Capacity(c, part))
            return $"the {d.Name} is full";
        else
            n = world.Deposit(c, part, hp.Amount, hp.Material);
        if (n <= 0)
            return null;
        hp.Amount -= n;
        if (hp.Amount <= 0)
            (hp.Amount, hp.Material) = (0, 0);
        Done.Add($"player {player}: tipped {n} {what} from the ore hopper into the {d.Name} (construct {c.Id})");
        return "";
    }
}
