namespace CallOfSlop;

/// <summary>
/// Which op of the map file built a box or a prop (the dev look-at inspector, Source/DevTools/LookAt.cs). The map file
/// loader notes where each op of its "ops" list starts in <see cref="Solid"/> and <see cref="PropInstances"/>, and one
/// closing mark after the last op, so anything added later (foliage, Clutter, the boundary, destruction chunks) belongs
/// to no op. Baked with the map like any other field (MapBake.cs), so a shipped, baked map answers too.
/// </summary>
public sealed partial class Map
{
    /// <summary>One op of the map file: its number in the file's "ops" list (1-based, as load errors count) and where its boxes and props start.</summary>
    public readonly record struct FileOpMark(int OpNumber, int FirstBox, int FirstProp);

    /// <summary>
    /// The file's ops in order (empty for a map built in code); the last mark (op number 0) closes the list. Op names are
    /// in <see cref="FileOpNames"/>, at the same position.
    /// </summary>
    public readonly List<FileOpMark> FileOpMarks = new();

    /// <summary>The builder each file op ran (PlaceProp, Block, PlaceBuilding...), at the same position as <see cref="FileOpMarks"/>.</summary>
    public readonly List<string> FileOpNames = new();

    /// <summary>Called by the map file loader before op <paramref name="opNumber"/> (1-based) runs; 0 after the last.</summary>
    internal void MarkFileOp(int opNumber, string builder)
    {
        FileOpMarks.Add(new FileOpMark(opNumber, Solid.Count, PropInstances.Count));
        FileOpNames.Add(builder);
    }

    /// <summary>The file op (its position in <see cref="FileOpMarks"/>) that added <see cref="Solid"/>[box], or -1.</summary>
    public int FileOpOfBox(int box) => FileOpOf(box, true);

    /// <summary>The file op (its position in <see cref="FileOpMarks"/>) that added <see cref="PropInstances"/>[prop], or -1.</summary>
    public int FileOpOfProp(int prop) => FileOpOf(prop, false);

    private int FileOpOf(int index, bool box)
    {
        var marks = FileOpMarks;
        if (index < 0 || marks.Count < 2)
            return -1;
        int lo = 0, hi = marks.Count - 2; // the last mark only closes the list
        if (index >= (box ? marks[^1].FirstBox : marks[^1].FirstProp))
            return -1; // added after the ops (foliage, Clutter, destruction...)
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if ((box ? marks[mid].FirstBox : marks[mid].FirstProp) <= index)
                lo = mid;
            else
                hi = mid - 1;
        }
        // Several ops can start at the same index (ops that add no boxes): the last one that starts there and still covers it.
        var start = box ? marks[lo].FirstBox : marks[lo].FirstProp;
        var end = box ? marks[lo + 1].FirstBox : marks[lo + 1].FirstProp;
        return index >= start && index < end ? lo : -1;
    }
}
