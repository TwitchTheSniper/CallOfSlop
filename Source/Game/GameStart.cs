namespace CallOfSlop;

/// <summary>
/// Which start this world plays (docs/story/STARTS.md; StoryStarts.cs). The server's alone: the host's choice (the menus'
/// Start row) for a fresh world, the save's for a saved one, StoryStarts.Legacy (no opening) for a world saved before starts.
/// Clients don't need it: the opening's beats and lines come from the server.
/// </summary>
public sealed partial class Game
{
    private string _startAsked = ""; // the host's choice (Settings.StoryStart), or a save's
    private bool _startLegacy; // a loaded world saved without a start (one from before starts): StoryStarts.Legacy
    private StoryStart? _start; // resolved on this map once asked

    /// <summary>The start this world plays: the one asked for on its own planetoid, else the any-map one or the default; a loaded world saved without one, Legacy.</summary>
    public StoryStart PlayedStart => _start ??= _startLegacy ? StoryStarts.Legacy : StoryStarts.For(_startAsked, World.Map.Name);

    /// <summary>The start's opening (server). Clients keep the default's, which only gives the skip hint.</summary>
    private OpeningScript OpScript => PlayedStart.Script;

    /// <summary>Server: the host's choice as the game starts (Settings.StoryStart). A saved world's own replaces it as it loads.</summary>
    public void SetStoryStart(string id)
    {
        _startAsked = id ?? "";
        _startLegacy = false;
        _start = null;
    }

    /// <summary>Server, loading a world: the start it was made with ("" a world from before starts: StoryStarts.Legacy).</summary>
    private void StartLoad(string id)
    {
        SetStoryStart(id);
        _startLegacy = id.Length == 0; // (the save's Start section, read after, sets it back with the id)
        if (id.Length > 0)
            Log($"[start] the world was made with the {id} start");
    }

    /// <summary>Server, taking a save: the start's id (always written since 2026-11-17: "" is a world from before starts).</summary>
    private string StartCapture() => PlayedStart.Id;
}
