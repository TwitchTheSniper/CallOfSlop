namespace CallOfSlop;

public sealed partial class Game
{
    /// <summary>
    /// Kills by the local player (not suicides) and how many were headshots, counted on both sides from the kill
    /// message. Program.Frenzy.cs reads the change each frame to drive the kill-frenzy music. Zombies kills aren't
    /// kill messages: that mode reads the local player's zombie kill count instead.
    /// </summary>
    public int LocalKillCount, LocalHeadshotCount;

    private void NoteFrenzyKill(in KillPacket kill)
    {
        if (kill.Killer != LocalId || kill.Victim == LocalId || kill.Killer == kill.Victim)
            return;
        LocalKillCount++;
        if (kill.Headshot)
            LocalHeadshotCount++;
    }
}
