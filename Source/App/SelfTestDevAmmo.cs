namespace CallOfSlop;

/// <summary>The host's dev Infinite ammo (F7 window, GameDevAmmo.cs): guns never run dry, grenades are never used up, outside Zombies.</summary>
public static partial class SelfTest
{
    private static void CheckDevInfiniteAmmo()
    {
        var now = 1000.0;
        using var g = ModeGame(0, GameMode.TeamDeathmatch);
        if (SpawnLocalSafely(g, ref now) is { } why)
        {
            Fail($"dev infinite ammo: {why}");
            return;
        }
        var me = g.Local!;
        if (g.InfiniteAmmoOn(now))
            Fail("infinite ammo is on in a new Team Deathmatch game");
        g.DevInfiniteAmmo = true;
        me.Grenade = ThrowableKind.Frag; // (a Team Deathmatch default loadout may carry none)
        me.GrenadesLeft = 2;
        g.LocalGrenade = ThrowableKind.Frag;
        g.GrenadesLeft = 2;
        // Grenades: three throws, none used up (the local count and the server's).
        var before = (Local: g.GrenadesLeft, Server: me.GrenadesLeft);
        for (var i = 0; i < 3; i++)
        {
            g.TryThrow(false, now);
            Run(g, ref now, 1.0);
        }
        var keptGrenades = g.GrenadesLeft == before.Local && me.GrenadesLeft == before.Server && before.Local > 0;
        // Guns: a magazine and more of pulls, all fired, the magazine still full, no reload.
        var t = now;
        var (pulls, shots, ammoSame, reloaded) = InfinitePulls(g, 0, ref t);
        var gunOk = g.InfiniteAmmoOn(t) && shots > g.Slots[0].Def.Mag && ammoSame && !reloaded;
        // Off again: a throw uses one up.
        g.DevInfiniteAmmo = false;
        now = Math.Max(now, t) + 1;
        Run(g, ref now, 1.0);
        var left = g.GrenadesLeft;
        g.TryThrow(false, now);
        var offOk = !g.InfiniteAmmoOn(now) && g.GrenadesLeft == left - 1;
        // The lobby state carries it (Zombies flags byte, bit 2), so clients skip the count too.
        g.DevInfiniteAmmo = true;
        var w = new PacketWriter();
        g.WriteLobbyState(w);
        var wire = w.Span.ToArray();
        g.DevInfiniteAmmo = false;
        g.WriteLobbyState(w);
        var differs = !wire.AsSpan().SequenceEqual(w.Span);
        Line($"  dev infinite ammo: 3 throws kept the grenades {keptGrenades} ({before.Local} local, {me.GrenadesLeft} on the server); gun '{g.Slots[0].Def.Name}' {shots} of {pulls} pulls fired, magazine full {ammoSame}, reloaded {reloaded}; off: a throw uses one {offOk}; lobby state differs {differs}");
        if (!keptGrenades)
            Fail($"dev infinite ammo: grenades were used up ({before.Local} -> {g.GrenadesLeft}, server {before.Server} -> {me.GrenadesLeft})");
        if (!gunOk)
            Fail($"dev infinite ammo: the gun ran dry or reloaded ({shots} of {pulls} pulls, magazine {g.Slots[0].Ammo}/{g.Slots[0].Def.Mag}, reloaded {reloaded})");
        if (!offOk)
            Fail("dev infinite ammo: turned off, a throw should use a grenade up");
        if (!differs)
            Fail("dev infinite ammo isn't in the lobby state (clients wouldn't know)");
    }
}
