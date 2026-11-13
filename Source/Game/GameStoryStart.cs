namespace CallOfSlop;

/// <summary>
/// Which start this world plays (docs/story/STARTS.md; StoryStarts.cs). The server's alone: the host's choice (the Story
/// page) for a fresh world, the save's for a saved one, the default for a world saved before starts. Clients don't need
/// it: the opening's beats and lines come from the server, and the tutorial is the induction or nothing.
/// </summary>
public sealed partial class Game
{
    private string _startAsked = ""; // the host's choice (Settings.StoryStart), or a save's
    private StoryStart? _start; // resolved on this map once asked

    /// <summary>The start this world plays: the one asked for on its own planetoid, else the default (penal colony).</summary>
    public StoryStart PlayedStart => _start ??= StoryStarts.For(_startAsked, World.Map.Name);

    /// <summary>The start's opening (server). Clients keep the default's, which only gives the skip hint.</summary>
    private OpeningScript OpScript => PlayedStart.Script;

    /// <summary>Server: the host's choice as the game starts (Settings.StoryStart). A saved world's own replaces it as it loads.</summary>
    public void SetStoryStart(string id)
    {
        _startAsked = id ?? "";
        _start = null;
    }

    /// <summary>Server, loading a world: the start it was made with ("" a world from before starts: the default).</summary>
    private void StartLoad(string id)
    {
        SetStoryStart(id);
        if (id.Length > 0)
            Log($"[start] the world was made with the {id} start");
    }

    /// <summary>Server, taking a save: the start's id, or "" for the default (so a default world's save is as it was).</summary>
    private string StartCapture() => PlayedStart == StoryStarts.Default ? "" : PlayedStart.Id;

    /// <summary>Server: the start's tutorial follows the opening (the induction), or there is none.</summary>
    private bool StartInduction => PlayedStart.Induction;

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
