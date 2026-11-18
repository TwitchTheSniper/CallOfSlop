namespace CallOfSlop;

/// <summary>
/// Who may command or fly a ship (Karl, 2026-11-18; faction-controllers.md section 7: a player commands their own ships, and
/// later their hired groups as admiral). A player gives command view orders only to a ship that is theirs, a faction mate's, or
/// nobody's; a ship crewed by an NPC power (the Traders' Zero Day of the Zero Day start, any power's crewed ship) is that power's:
/// it is seen, picked, locked and fought, never ordered, and its helm and gun seats are its crew's. Boarded and taken (its crew
/// surrendered: GameCrewBoard.cs), it is a prize and the captor's.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// The NPC power holding <paramref name="c"/> (CrewPowers id; 0: none): nobody's (owner 255) with a living crew of a power aboard,
    /// not a player's hired crew and not a prize; or a nobody's craft docked in or carried by such a ship. The host reads its own crews; a joined client the crews' look (<see cref="CrewInfo"/>).
    /// </summary>
    public byte PowerHolding(Construct c)
    {
        if (c.Owner != 255)
            return 0;
        if (c.CarriedBy != 0 && c.CarriedBy != c.Id && Constructs.Get(c.CarriedBy) is { Owner: 255, CarriedBy: 0 } carrier && PowerHolding(carrier) is var held and not 0)
            return held; // (its own craft, docked or carried: a Shuttle on the Traders' clamp is the Traders')
        if (IsServer)
            return c.NpcFaction != 0 && Crew.ShipOf(c.Id) is { Own: false, Prize: false } s && s.Faction != 0 && Crew.Alive(s) > 0 ? s.Faction : (byte)0;
        return CrewInfo.TryGetValue(c.Id, out var i) && i.Alive > 0 ? i.Faction : (byte)0;
    }

    /// <summary>Why <paramref name="player"/> may not command <paramref name="c"/> (command view orders), or null: theirs, a faction mate's, or nobody's.</summary>
    public string? CommandRefusal(Construct c, byte player)
    {
        if (c.Owner != 255)
            return Factions.Shares(c.Owner, player) ? null
                : $"it is {(Players.TryGetValue(c.Owner, out var o) ? o.Name : $"player {c.Owner}")}'s ship";
        var f = PowerHolding(c);
        return f != 0 ? $"it is the {PowerPossessive(f)} ship" : null;
    }

    /// <summary>The command view's: why the local player's orders can't go to <paramref name="c"/> (its flight computer, or whose it is), or null.</summary>
    public string? NavNoOrders(Construct c) => NavProblem(c) ?? CommandRefusal(c, LocalId);

    /// <summary>Server (<see cref="ConstructEdits.SeatRefusal"/>): an NPC power's helm, and the seats its crew fly and fight from, are theirs.</summary>
    private string? ShipSeatRefusal(Construct c, int part, byte player)
    {
        var f = PowerHolding(c);
        if (f == 0)
            return null;
        var helm = part == c.DriveSeat;
        var s = Crew.ShipOf(c.Id);
        for (var k = 0; s != null && k < s.StCount && !helm; k++)
            if (s.StPart[k] == part && s.StKind[k] is CrewStationKind.Helm or CrewStationKind.Gun)
                helm = true;
        return helm ? $"that seat is the {PowerPossessive(f)} own: you can't fly or fight their ship" : null;
    }

    /// <summary>"Traders'", "Militia's": a power's name for "the ... ship".</summary>
    public static string PowerPossessive(byte power) =>
        CrewPowers.Of(power)?.Name is { Length: > 0 } n ? (n.EndsWith('s') ? n + "'" : n + "'s") : "crew's";
}
