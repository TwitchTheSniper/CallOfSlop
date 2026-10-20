namespace CallOfSlop;

/// <summary>The pages of the loadout screen.</summary>
public enum LoadoutPage : byte
{
    /// <summary>Slot list + loadout cards (the bottom of the stack).</summary>
    Overview,
    /// <summary>Weapon select for the primary slot.</summary>
    Primary,
    /// <summary>Weapon select for the secondary slot.</summary>
    Secondary,
    Lethal,
    Tactical,
    Perks,
    /// <summary>The attachment showcase for one gun (the entry's argument is the weapon id).</summary>
    Attachments,
    /// <summary>Level, XP and every unlock by level.</summary>
    Progression,
}

/// <summary>One page on the loadout screen's stack, with an optional argument (the weapon id for <see cref="LoadoutPage.Attachments"/>).</summary>
public readonly record struct LoadoutNavEntry(LoadoutPage Page, int Arg = -1);

/// <summary>
/// The loadout screen's page stack, so Back (and Esc) always go back one page:
/// attachments return to the weapon select (or the overview if they were opened from a card),
/// the pickers return to the overview, progression returns to whatever was open before, and
/// Back on the overview tells the caller to close the screen. Pure logic (checked by the self-test).
/// </summary>
public sealed class LoadoutNav
{
    private readonly List<LoadoutNavEntry> _stack = [new(LoadoutPage.Overview)];

    /// <summary>The page on top.</summary>
    public LoadoutPage Current => _stack[^1].Page;

    /// <summary>The top page's argument (-1 if none).</summary>
    public int Arg => _stack[^1].Arg;

    /// <summary>How many pages are stacked (1 = just the overview).</summary>
    public int Depth => _stack.Count;

    /// <summary>The page at a depth, 0 = the overview at the bottom (for the breadcrumb).</summary>
    public LoadoutNavEntry this[int index] => _stack[index];

    /// <summary>Back to just the overview.</summary>
    public void Reset()
    {
        _stack.Clear();
        _stack.Add(new LoadoutNavEntry(LoadoutPage.Overview));
    }

    /// <summary>
    /// Opens a page on top of the current one. Opening a page that's already on the stack goes back
    /// down to it instead (taking the new argument), so the stack never loops or grows without bound.
    /// Opening the overview resets the stack.
    /// </summary>
    public void Open(LoadoutPage page, int arg = -1)
    {
        if (page == LoadoutPage.Overview)
        {
            Reset();
            return;
        }
        var at = _stack.FindLastIndex(e => e.Page == page);
        if (at >= 0)
            _stack.RemoveRange(at, _stack.Count - at);
        _stack.Add(new LoadoutNavEntry(page, arg));
    }

    /// <summary>Goes back one page. Returns true when already on the overview: the caller should close the screen.</summary>
    public bool Back()
    {
        if (_stack.Count <= 1)
            return true;
        _stack.RemoveAt(_stack.Count - 1);
        return false;
    }
}
