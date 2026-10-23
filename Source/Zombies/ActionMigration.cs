namespace CallOfSlop;

/// <summary>
/// Builds from before the fire type became a part (docs/zombies/WEAPON-BUILDER.md, "Actions: the fire type is a part").
/// Receivers used to carry their own action and fire rate; now each receiver has an "action" socket and the action part
/// in it decides both. An old build has no action parts at all, so every receiver gets the action it used to fire like
/// (its file's <c>"stockAction"</c>): the same fire type and nearly the same rate, so the gun feels the same.
/// </summary>
public static class ActionMigration
{
    /// <summary>The action part <paramref name="receiver"/> came with (its "stockAction"), or null.</summary>
    public static PartDef? StockActionOf(PartDef receiver, Func<string, PartDef?> find) =>
        receiver.Receiver?.StockAction is { Length: > 0 } name && find(name) is { Action: not null } a ? a : null;

    /// <summary>True if <paramref name="b"/> has no action part anywhere (a gun build written before actions were parts).</summary>
    public static bool IsOld(WeaponBuild b) => !b.IsMelee && b.Receivers.Count > 0 && !b.Nodes.Any(n => n.Part.Category == PartCategory.Action);

    /// <summary>If <paramref name="b"/> is an old build (<see cref="IsOld"/>), puts each receiver's stock action in. Returns how many it added.</summary>
    public static int FillIfOld(WeaponBuild b, Func<string, PartDef?> find) => IsOld(b) ? Fill(b, find) : 0;

    /// <summary>
    /// Puts each receiver's stock action into its empty action socket (receivers that already have one, or have no
    /// action socket or stock action, are left alone). Returns how many it added.
    /// </summary>
    public static int Fill(WeaponBuild b, Func<string, PartDef?>? find = null)
    {
        find ??= Parts.Find;
        var added = 0;
        foreach (var r in b.Receivers)
        {
            if (r.Part.Sockets.FirstOrDefault(s => s.Type == "action") is not { } socket || r.Child(socket.Name) != null)
                continue;
            if (StockActionOf(r.Part, find) is not { } action)
                continue;
            r.Attach(socket.Name, action);
            added++;
        }
        return added;
    }
}
