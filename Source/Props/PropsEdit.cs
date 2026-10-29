namespace CallOfSlop;

/// <summary>
/// Live prop edits for the dev prop editor (Source/DevTools/PropEditSession.cs): a loaded prop is changed in place (every
/// map and building that holds it sees the change) and <see cref="EditVersion"/> goes up, so the prop renderer rebuilds
/// its meshes and instance batches on the next frame. Only the editor changes props this way; a game never does.
/// </summary>
public static partial class Props
{
    /// <summary>Goes up by one on every live edit (<see cref="CopyInto"/> or a moved placed prop); the prop renderer rebuilds when it changes.</summary>
    public static int EditVersion { get; private set; }

    /// <summary>Asks the prop renderer to rebuild its meshes and batches (after a live edit).</summary>
    public static void MarkEdited() => EditVersion++;

    /// <summary>
    /// Makes <paramref name="target"/> look and behave like <paramref name="from"/> (a prop freshly loaded from edited text):
    /// its model, colliders, bounds, lights and file settings. The identity fields stay (name, index, pack, flag variant),
    /// so maps and the renderer keep pointing at the same object.
    /// </summary>
    public static void CopyInto(PropDef from, PropDef target)
    {
        target.Category = from.Category;
        target.Description = from.Description;
        target.Author = from.Author;
        target.Collision = from.Collision;
        target.Parts.Clear();
        target.Parts.AddRange(from.Parts);
        target.Colliders = from.Colliders;
        target.Bounds = from.Bounds;
        target.RandomYaw = from.RandomYaw;
        target.YawStep = from.YawStep;
        target.YawJitter = from.YawJitter;
        target.ScaleMin = from.ScaleMin;
        target.ScaleMax = from.ScaleMax;
        target.GroundSnap = from.GroundSnap;
        target.Weight = from.Weight;
        target.FadeDistance = from.FadeDistance;
        target.Breakable = from.Breakable;
        target.Lights = from.Lights;
        target.Concealment = from.Concealment;
        target.FlagCloth = from.FlagCloth;
        MarkEdited();
    }

    /// <summary>A copy of a prop's editable state (to put it back with <see cref="CopyInto"/>).</summary>
    public static PropDef Snapshot(PropDef def)
    {
        var copy = new PropDef { Name = def.Name, SourceFile = def.SourceFile, Pack = def.Pack, Index = def.Index };
        copy.Parts.AddRange(def.Parts);
        copy.Category = def.Category;
        copy.Description = def.Description;
        copy.Author = def.Author;
        copy.Collision = def.Collision;
        copy.Colliders = def.Colliders;
        copy.Bounds = def.Bounds;
        copy.RandomYaw = def.RandomYaw;
        copy.YawStep = def.YawStep;
        copy.YawJitter = def.YawJitter;
        copy.ScaleMin = def.ScaleMin;
        copy.ScaleMax = def.ScaleMax;
        copy.GroundSnap = def.GroundSnap;
        copy.Weight = def.Weight;
        copy.FadeDistance = def.FadeDistance;
        copy.Breakable = def.Breakable;
        copy.Lights = def.Lights;
        copy.Concealment = def.Concealment;
        copy.FlagCloth = def.FlagCloth;
        return copy;
    }
}
