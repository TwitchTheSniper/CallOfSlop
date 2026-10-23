namespace CallOfSlop;

/// <summary>
/// The host's gravity setting (Settings "gravity", the lobby's host options and the lobby room): a percentage of every
/// map's own gravity, 25 to 200 (default 100), applied as <see cref="World.GravityScale"/> to every field (straight down,
/// a planet's pull, every gravity zone) and everything that falls. Clients learn it from the Welcome and from the lobby
/// state (protocol 48), so their prediction matches the host's; the server info carries it for the browser ('G').
/// Bots keep the map's nav graph (built at 100%); in low gravity over a void they don't hop in fights.
/// </summary>
public sealed partial class Game
{
    public const int MinGravityPercent = 25, MaxGravityPercent = 200;

    /// <summary>The settings the host can pick (the lobby's choices).</summary>
    public static readonly int[] GravityPercentChoices = [25, 50, 75, 100, 125, 150, 200];

    private int _gravityPercent = 100;

    /// <summary>
    /// Gravity as a percentage of the map's own (host: set it; clients: the host's). A planet map plays at no less than its
    /// own floor (<see cref="Map.MinGravityPercent"/>; <see cref="EffectiveGravityPercent"/>).
    /// </summary>
    public int GravityPercent
    {
        get => _gravityPercent;
        set
        {
            var v = Math.Clamp(value, MinGravityPercent, MaxGravityPercent);
            var changed = v != _gravityPercent;
            _gravityPercent = v;
            World.GravitySetting = v;
            World.ApplyGravitySetting();
            if (changed && IsServer)
                MarkLobbyDirty(); // the lobby state carries it to everyone
        }
    }

    /// <summary>The percentage in force on the current map (the setting, or the map's floor if that's higher).</summary>
    public int EffectiveGravityPercent => Math.Max(_gravityPercent, World.Map.MinGravityPercent);

    /// <summary>The multiplier for a percentage (exactly 1 at 100).</summary>
    public static float ScaleOf(int percent) => percent == 100 ? 1f : percent / 100f;

    /// <summary>"gravity 50%" for the lobby and browser lines (empty at 100%).</summary>
    public static string GravityLabel(int percent) => percent == 100 ? "" : $"gravity {percent}%";

    /// <summary>A received percentage, made safe (older hosts send nothing: 100).</summary>
    private void ApplyHostGravity(int percent) => GravityPercent = percent is >= MinGravityPercent and <= MaxGravityPercent ? percent : 100;
}
