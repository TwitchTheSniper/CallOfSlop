namespace CallOfSlop;

/// <summary>
/// Which start this world plays (docs/story/STARTS.md; StoryStarts.cs). The server's alone: the host's choice (the Story
/// page) for a fresh world, the save's for a saved one, StoryStarts.Legacy (no opening) for a world saved before starts.
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

    /// <summary>Server, as a player wakes for the first time: the start's standing with its faction, when it sets one.</summary>
    private void StartStanding(Player p)
    {
        var start = PlayedStart;
        if (start.Standing == 0 || start.Faction.Length == 0 || Story is not { } s)
            return;
        var who = StoryEntityOf(p);
        var f = s.Factions.Find(start.Faction);
        if (who < 0 || f < 0)
        {
            Log($"[start] {p.Name}: no standing with {start.Faction} ({(f < 0 ? "no such faction" : "not in the story yet")})");
            return;
        }
        if (s.Factions.StartStanding(f, who, start.Standing))
            Log($"[start] {p.Name}: {start.Faction} standing {start.Standing} ({start.Name})");
    }
}
