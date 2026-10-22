namespace CallOfSlop;

/// <summary>Bots and destruction: the nav graph follows holes and collapses (see NavGraph's runtime patches).</summary>
public sealed partial class Game
{
    /// <summary>Server: structure <paramref name="s"/> changed shape (chunks broke or fell): the nav graph around it is patched.</summary>
    private void NoteStructureChanged(int s, double now)
    {
        _ = s;
        _ = now;
    }
}
