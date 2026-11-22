namespace CallOfSlop;

/// <summary>What a cockpit switch or button is linked to (its Function setting, <see cref="CfgKey.SeatFn"/>).</summary>
public enum SeatFn : byte
{
    None,
    Dampeners, // the craft's inertia dampeners (ConstructFlight.ToggleDampeners)
    MouseSteer, // its mouse steering (ConstructFlight.ToggleMouseSteer)
    Reactor, // its power plants on (ConstructShutdown.ToggleReactor)
    Thrusters, // its thrusters on (ConstructShutdown.ToggleThrusters)
    Gear, // its landing leg pods down (ConstructDoors.ToggleGear)
    Caution, // MASTER CAUTION: lit while a caution is new; a press acknowledges it
    HoldFire, // the crew's order: hold fire (lit) or fire at will; worked from the command seat only (CockpitStations.Commands)
    COUNT,
}

/// <summary>
/// The modular cockpit's seat link (docs/sandbox/constructs/cockpits.md "The seat link"): a switch or button with a Function does
/// what the seat's key for it does, and shows that state as its own output (its bat or cap, and anything wired from it: a lamp, a
/// light bank), so turning the dampeners off by the seat's key flips the dash switch too. The state is the craft's, as the seat
/// panel shows it; the logic tick (server) sets the output each tick from it, the press goes through the same toggles as the keys.
/// MASTER CAUTION lights when any caution comes on that wasn't acknowledged: dampeners off, the reactor down, the thrusters down.
/// </summary>
public static class CockpitFns
{
    public static readonly string[] Options = ["None", "Dampeners", "Mouse steering", "Reactor", "Thrusters", "Landing legs", "Master caution", "Crew hold fire"];

    /// <summary>Set by the game (server): does the craft's own crew hold fire, and the order given (true: hold fire). Null: no crew (a check).</summary>
    public static Func<Construct, bool>? CrewHoldsFire;
    public static Func<Construct, byte, bool, string?>? CrewOrderFire;

    /// <summary>A Console Drawer's tray (cockpits phase 6; the part kit's CockpitParts.TrayName): F on it from a seat in reach slides it.</summary>
    public static bool IsDrawer(ConstructPartDef d) => d.Door != null && d.Name.StartsWith("Console Drawer Tray", StringComparison.Ordinal);

    /// <summary>A crew order: given only from the command seat (the drive seat on a craft with no Command seat).</summary>
    public static bool IsOrder(SeatFn fn) => fn == SeatFn.HoldFire;

    /// <summary>The part's Function (None when it has no such setting).</summary>
    public static SeatFn Of(in CPart p, ConstructPartDef d)
    {
        if (d.CfgSlot[(int)CfgKey.SeatFn] < 0)
            return SeatFn.None;
        var v = (int)ConstructConfig.Get(p, d, CfgKey.SeatFn, 0);
        return v is > 0 and < (int)SeatFn.COUNT ? (SeatFn)v : SeatFn.None;
    }

    /// <summary>The craft's cautions as bits: 1 dampeners off, 2 reactor down, 4 thrusters down.</summary>
    public static ushort Cautions(Construct c) =>
        (ushort)((ConstructFlight.DampenersOff(c) ? 1 : 0) | (ConstructShutdown.ReactorOff(c) ? 2 : 0) | (ConstructShutdown.ThrustersOff(c) ? 4 : 0));

    /// <summary>The logic tick (server): the output a part with Function <paramref name="fn"/> shows now. MASTER CAUTION keeps the cautions it was acknowledged for in <c>Count</c>.</summary>
    public static float Shown(Construct c, ref CPart p, SeatFn fn)
    {
        switch (fn)
        {
            case SeatFn.Dampeners:
                return ConstructFlight.HasDampeners(c) && !ConstructFlight.DampenersOff(c) ? 1 : 0;
            case SeatFn.MouseSteer:
                return ConstructFlight.HasMouseSteer(c) && !ConstructFlight.MouseSteerOff(c) ? 1 : 0;
            case SeatFn.Reactor:
                return ConstructShutdown.HasReactor(c) && !ConstructShutdown.ReactorOff(c) ? 1 : 0;
            case SeatFn.Thrusters:
                return ConstructShutdown.HasThrusters(c) && !ConstructShutdown.ThrustersOff(c) ? 1 : 0;
            case SeatFn.Gear:
                return ConstructDoors.GearState(c) == 2 ? 1 : 0;
            case SeatFn.HoldFire:
                return CrewHoldsFire?.Invoke(c) == true ? 1 : 0;
            case SeatFn.Caution:
            {
                var now = Cautions(c);
                p.Count &= now; // (a caution that went out is forgotten: it lights again when it comes back)
                return (now & ~p.Count) != 0 ? 1 : 0;
            }
            default:
                return 0;
        }
    }

    /// <summary>Server: the use key (or a click from a seat) on a part with Function <paramref name="fn"/>: the craft's toggle, or the caution acknowledged. A refusal's reason, or null.</summary>
    public static string? Press(ConstructWorld world, Construct c, ref CPart p, SeatFn fn, byte player)
    {
        switch (fn)
        {
            case SeatFn.Dampeners:
                return ConstructFlight.ToggleDampeners(world, c);
            case SeatFn.MouseSteer:
                return ConstructFlight.ToggleMouseSteer(world, c);
            case SeatFn.Reactor:
                return ConstructShutdown.ToggleReactor(world, c);
            case SeatFn.Thrusters:
                return ConstructShutdown.ToggleThrusters(world, c);
            case SeatFn.Gear:
                return ConstructDoors.ToggleGear(world, c);
            case SeatFn.HoldFire:
                return CrewOrderFire == null ? "no crew aboard to order" : CrewOrderFire(c, player, CrewHoldsFire?.Invoke(c) != true);
            case SeatFn.Caution:
                p.Count = Cautions(c);
                c.Wake();
                return null;
            default:
                return null;
        }
    }
}
