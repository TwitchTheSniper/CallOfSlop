namespace CallOfSlop;

/// <summary>
/// Part hot reload during a session ("Reload part files" in the pause menu, or --watch-parts): re-reads the Parts folder
/// (<see cref="Parts.Reload()"/>) and rebuilds every built weapon in play in place, so a part maker can edit a part file,
/// reload and see the gun in their hands (and the Workbench Range dummies' numbers) change. Offline, or hosting with
/// nobody connected only: connected players assembled their weapons from the old files, and the part fingerprint sent
/// when someone joins (<see cref="PartSet"/>) keeps anyone with a different Parts folder out afterwards.
/// </summary>
public sealed partial class Game
{
    /// <summary>Why part files can't be reloaded right now (null = they can).</summary>
    public string? PartReloadBlocked =>
        !IsServer ? "Only the host can reload part files."
        : !Net.IsOffline && Net.ConnectionCount > 0 ? "Players are connected: their weapons were built from the old part files. Reload when nobody else is in the game."
        : null;

    /// <summary>Server side: reloads the part files and rebuilds the built weapons in play. Returns a line for the menu.</summary>
    public string ReloadPartFiles()
    {
        if (PartReloadBlocked is { } why)
        {
            Log(why, true);
            return why;
        }
        var r = Parts.Reload();
        var msg = r.Summary;
        var (rebuilt, problems) = RebuildBuiltWeapons();
        if (rebuilt > 0)
            msg += $". Rebuilt {rebuilt} weapon(s) in play";
        if (problems.Count > 0)
            msg += $". Kept as they were: {string.Join("; ", problems.Take(3))}";
        Log(msg, true);
        return msg;
    }

    /// <summary>
    /// Every built weapon held by a player (guns in slots 1-4 and the melee build) assembled again from its build text
    /// and updated in place (ids, slots and kits stay). The local player's slots are refilled with the new numbers.
    /// </summary>
    public (int Rebuilt, List<string> Problems) RebuildBuiltWeapons()
    {
        var done = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var problems = new List<string>();
        var n = 0;
        void One(WeaponDef? d)
        {
            if (d?.BuildText == null || !done.Add(d))
                return;
            if (Parts.Rebuild(d, out var why))
                n++;
            else if (why != null)
                problems.Add(why);
        }
        foreach (var p in Players.Values)
        {
            One(p.BuiltPrimary);
            One(p.BuiltSecondary);
            One(p.BuiltExtra[0]);
            One(p.BuiltExtra[1]);
            if (p.MeleeBuild is { BuildText: not null } m && done.Add(m))
            {
                if (Parts.Rebuild(m, out var why))
                    n++;
                else if (why != null)
                    problems.Add(why);
            }
        }
        _cartShapes.Clear(); // push-cart collision shapes come from the built body (GameZombies.cs)
        if (n > 0 && Local is { } me)
            SyncLocalSlots(me, refill: true);
        return (n, problems);
    }
}
