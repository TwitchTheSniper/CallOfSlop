namespace CallOfSlop;

/// <summary>
/// The crews' word on a body's machines (docs/sandbox/design/bot-brain.md phase 7, rule 1 for work stations): a lifter, a cargo dock or a
/// Building Station on a crewed NPC body works only while a crew member stands at it. Set by the game's crew (<see cref="Game.Crew"/>);
/// unset (a tool's world, a client), everything works. Players' bodies and route ships are never held.
/// </summary>
public static class CrewGate
{
    public static Func<Construct, int, bool>? Check;

    public static bool Works(Construct c, int part) => Check == null || Check(c, part);
}
