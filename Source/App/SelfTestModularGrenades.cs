using System.Numerics;

namespace CallOfSlop;

/// <summary>Helpers other sections still use; the section this file held was removed (user, 2026-11-03).</summary>
public static partial class SelfTest
{

    /// <summary>A grenade build from part names (null when one is missing).</summary>
    private static WeaponBuild? GBuild(string name, string casing, string fuse, string payload, params string[] mods)
    {
        if (Parts.Find(casing) is not { Casing: not null } c || Parts.Find(fuse) is not { Fuse: not null } f || Parts.Find(payload) is not { Payload: not null } p)
            return null;
        var b = new WeaponBuild(name, c);
        b.Root!.Attach("fuse", f);
        b.Root.Attach("payload", p);
        var free = c.Sockets.Where(s => s.Type == "gmod").ToList();
        for (var i = 0; i < mods.Length && i < free.Count; i++)
            if (Parts.Find(mods[i]) is { GMod: not null } m)
                b.Root.Attach(free[i].Name, m);
        return b;
    }
}
