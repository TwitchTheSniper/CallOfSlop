namespace CallOfSlop;

/// <summary>What a seat is for, as its Station setting names it (docs/sandbox/constructs/cockpits.md "Command stations"). Auto: the drive seat is
/// the helm, a seat that works a gun the gun seat, any other a couch where spares wait.</summary>
public enum SeatStation : byte
{
    Auto,
    Command, // the captain's station: crew orders, and every other station's pages repeated read only
    Navigation,
    Sensors,
    Damage, // damage control
    Comms,
    COUNT,
}

/// <summary>
/// The seat's Station setting (phase 4 of the modular cockpits): every seat part with no setting of its own is given one, its choice
/// (<see cref="CPart.Setting"/>, so it saves, travels with the body and is cycled by the linking tool's reload key as any setting). The crew
/// man a station seat as they man the helm or a gun (CrewWorld.Scan), and the seat panel at a Command seat shows the crew orders and the
/// other stations read only (Game/GameSeatControls.cs).
/// </summary>
public static class CockpitStations
{
    public static readonly string[] Labels = ["Auto", "Command", "Navigation", "Sensors", "Damage control", "Comms"];

    /// <summary>Def loading: a seat with no setting gets the Station choice.</summary>
    public static void Give(ConstructPartDef d)
    {
        if (d.Role != PartRole.Seat || d.Settings.Length > 0)
            return;
        d.Settings = new float[(int)SeatStation.COUNT];
        for (var i = 0; i < d.Settings.Length; i++)
            d.Settings[i] = i;
        d.SettingLabels = Labels;
        d.SettingName = "station";
    }

    /// <summary>Seat <paramref name="seat"/>'s station as set (Auto for a part that isn't a seat with the setting).</summary>
    public static SeatStation Of(Construct c, int seat)
    {
        if (seat < 0 || seat >= c.PartCount)
            return SeatStation.Auto;
        var d = c.DefOf(seat);
        if (d.Role != PartRole.Seat || !ReferenceEquals(d.SettingLabels, Labels))
            return SeatStation.Auto;
        var v = c.Parts[seat].Setting;
        return v < (byte)SeatStation.COUNT ? (SeatStation)v : SeatStation.Auto;
    }

    /// <summary>The craft's command seat (the first seat set to Command), or -1.</summary>
    public static int CommandSeat(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.Seat && !c.Parts[i].Broken && Of(c, i) == SeatStation.Command)
                return i;
        return -1;
    }

    /// <summary>Does seat <paramref name="seat"/> give the crew orders: the Command seat, or the drive seat on a craft with none.</summary>
    public static bool Commands(Construct c, int seat) =>
        seat >= 0 && (Of(c, seat) == SeatStation.Command || seat == c.DriveSeat && CommandSeat(c) < 0);

    public static string Name(SeatStation s) => s switch
    {
        SeatStation.Command => "command",
        SeatStation.Navigation => "navigation",
        SeatStation.Sensors => "sensors",
        SeatStation.Damage => "damage control",
        SeatStation.Comms => "comms",
        _ => "",
    };
}
