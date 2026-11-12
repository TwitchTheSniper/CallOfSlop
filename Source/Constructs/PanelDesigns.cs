using System.Text;

namespace CallOfSlop;

/// <summary>A panel design: the name a player gave it and the panel's key (<c>Formed Panel (...)</c>: HullLoft.Panels.cs).</summary>
public readonly record struct PanelDesign(string Name, string Key);

/// <summary>
/// Panel designs (docs/sandbox/constructs/production.md "Panel Designer"). A design is a name and a formed panel's key. Each player keeps
/// a library in the save folder (<see cref="LibraryFile"/>, in the blueprint library's folder, so the save rules that cover that folder
/// cover it); a world keeps the designs its players saved at a Panel Designer (<see cref="ConstructWorld.Designs"/>, at most
/// <see cref="MaxWorld"/>): they are sent whole to everyone when they change and to a joiner, and ride in the world's save. Names are player
/// text: the blueprint name rule (ASCII, 32 characters) on every peer.
/// </summary>
public static class PanelDesigns
{
    public const int MaxWorld = 64, MaxLibrary = 128;
    public const string LibraryName = "PanelDesigns.txt";

    public static string LibraryFile => Path.Combine(PlayerBlueprints.Folder, LibraryName);

    /// <summary>Why a design can't be kept, or null: its name by the blueprint rule, its key a panel some machine could make.</summary>
    public static string? Refusal(string name, string key) =>
        PlayerBlueprints.NameRefusal(name) is { } why ? "a design's name: " + why
        : key.Length > ConstructParts.MaxRuntimeName || HullLoft.PanelOf(key) == null ? "that is not a panel a Panel Former makes"
        : null;

    // ---- The world's list (server: the truth; client: as heard) ---------------------------------------------------------

    /// <summary>Server: keeps a design in the world (a design of that name is replaced). Null, or why not.</summary>
    public static string? Set(ConstructWorld world, string name, string key)
    {
        if (Refusal(name, key) is { } why)
            return why;
        var list = world.Designs;
        for (var i = 0; i < list.Count; i++)
            if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (list[i].Key == key && list[i].Name == name)
                    return null;
                list[i] = new PanelDesign(name, key);
                world.DesignsChanged();
                return null;
            }
        if (list.Count >= MaxWorld)
            return $"this world holds {MaxWorld} panel designs: delete one first";
        list.Add(new PanelDesign(name, key));
        world.DesignsChanged();
        return null;
    }

    /// <summary>Server: drops a design from the world. Null, or why not.</summary>
    public static string? Remove(ConstructWorld world, string name)
    {
        var list = world.Designs;
        for (var i = 0; i < list.Count; i++)
            if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
                world.DesignsChanged();
                return null;
            }
        return "that design is gone";
    }

    /// <summary>The whole list: u8 n, each a name and a key (CBodyKind.Designs, protocol 257).</summary>
    public static void Write(PacketWriter w, ConstructWorld world)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Designs).U8((byte)world.Designs.Count);
        foreach (var d in world.Designs)
            w.Str(d.Name).Str(d.Key);
    }

    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U8();
        if (n > MaxWorld)
            throw new MalformedPacketException($"{n} panel designs");
        world.Designs.Clear();
        for (var k = 0; k < n; k++)
        {
            var name = r.Text(PlayerBlueprints.MaxName);
            var key = r.Text(ConstructParts.MaxRuntimeName);
            if (Refusal(name, key) == null) // (what this build can't make or name is left out)
                world.Designs.Add(new PanelDesign(name, key));
        }
        world.DesignStamp++;
    }

    public static void WriteSave(SaveWriter w, IReadOnlyList<PanelDesign> list)
    {
        w.I32(list.Count);
        foreach (var d in list)
            w.Str(d.Name).Str(d.Key);
    }

    public static void ReadSave(SaveReader r, ConstructWorld world)
    {
        var n = r.Count(2);
        world.Designs.Clear();
        for (var k = 0; k < n; k++)
        {
            var name = r.Str();
            var key = r.Str();
            if (world.Designs.Count < MaxWorld && Refusal(name, key) == null)
                world.Designs.Add(new PanelDesign(name, key));
        }
        world.DesignStamp++;
    }

    // ---- The player's library (this PC) ---------------------------------------------------------------------------------

    /// <summary>The player's own designs, by name: one <c>name TAB key</c> line each. A line this build can't read is left out.</summary>
    public static List<PanelDesign> LoadLibrary()
    {
        var list = new List<PanelDesign>();
        try
        {
            if (!File.Exists(LibraryFile))
                return list;
            foreach (var line in File.ReadAllLines(LibraryFile))
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0 || list.Count >= MaxLibrary)
                    continue;
                var (name, key) = (line[..tab], line[(tab + 1)..].Trim());
                if (Refusal(name, key) == null && !list.Exists(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new PanelDesign(name, key));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[sandbox] panel designs not read: {ex.Message}");
        }
        list.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>Writes the library (atomically, with a .bak, as every save). Null, or why not.</summary>
    public static string? SaveLibrary(List<PanelDesign> list)
    {
        var sb = new StringBuilder();
        foreach (var d in list)
            sb.Append(d.Name).Append('\t').Append(d.Key).Append('\n');
        return PlayerBlueprints.WriteFile(LibraryFile, sb.ToString());
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>The panel designs this world's players saved at a Panel Designer (PanelDesigns.cs): shared with everyone, saved with the world.</summary>
    public readonly List<PanelDesign> Designs = new(8);

    /// <summary>Server: the list changed and goes to everyone at the next flush. Either peer: <see cref="DesignStamp"/> counts changes (a panel's list key).</summary>
    public bool DesignsDirty;
    public int DesignStamp;

    public void DesignsChanged()
    {
        DesignsDirty = true;
        DesignStamp++;
    }
}
