namespace CallOfSlop;

/// <summary>
/// Magnet boots (Karl, 2026-11-24): the local player's boots on or off, on their key (7 by default). On, standing on a deck
/// under felt gravity (a hard burn, a turn, weightless), the deck holds them: no push off it, no jump, a slower walk
/// (<see cref="ShipGravity.Rider"/>). Sent with each command, so the server and the client's replay step them alike; kept until
/// toggled again. The HUD's bottom-right line shows the state and the key (UiSandboxHud.cs).
/// </summary>
public sealed partial class Game
{
    public bool MagBootsOn { get; private set; }

    /// <summary>The boots' key: flips them and says so.</summary>
    public void MagBootsToggle()
    {
        MagBootsOn = !MagBootsOn;
        SandboxSay(MagBootsOn ? "Magnet boots ON: the deck holds you" : "Magnet boots OFF");
    }
}
