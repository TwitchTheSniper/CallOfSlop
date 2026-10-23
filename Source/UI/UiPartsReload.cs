namespace CallOfSlop;

/// <summary>
/// The UI side of part hot reload (Source/Zombies/PartReload.cs). Kept in its own file so the workbench screen can be
/// rewritten freely: the only thing it needs to offer is a way to re-assemble what it shows.
/// </summary>
public sealed partial class Ui
{
    /// <summary>
    /// Called after <see cref="Parts.Reload()"/> (Program subscribes it to <see cref="Parts.Reloaded"/>). The parts on the
    /// workbench were updated in place, so re-assembling the build shows the new models and numbers.
    /// </summary>
    public void OnPartsReloaded()
    {
        if (WorkbenchOpen)
            WorkbenchChanged(); // HOOK (workbench screen): re-assemble the build being edited
    }
}
