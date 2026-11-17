namespace CallOfSlop;

/// <summary>
/// The crews' word on a body's machines (docs/sandbox/design/bot-brain.md phase 7, rule 1 for work stations): a lifter, a cargo dock or a
/// Building Station on a crewed NPC body works only while a crew member stands at it. The world's own hook (<see cref="ConstructWorld.CrewWorks"/>,
/// set by the game's crew on the server's world); unset (a tool's world, a client's), everything works. Players' bodies and route ships are
/// never held.
/// </summary>
public static class CrewGate
{
    public static bool Works(ConstructWorld world, Construct c, int part) => world.CrewWorks == null || world.CrewWorks(c, part);
}
