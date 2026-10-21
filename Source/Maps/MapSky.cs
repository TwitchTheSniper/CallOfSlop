namespace CallOfSlop;

/// <summary>A map's optional sky (the "skybox" section of its file; see <see cref="SkySettings"/> and Maps/README.md "Sky").</summary>
public sealed partial class Map
{
    /// <summary>
    /// The map's sky, or null for the classic look: a flat <see cref="Sky"/> colour as the background and fog, and the
    /// game's usual sun. With a sky, <see cref="Sky"/> is its horizon colour. Visual only: it is not part of
    /// <see cref="ContentHash"/>, so a sky change never stops players with different files from joining each other.
    /// </summary>
    public SkySettings? Skybox;
}
