using System.Runtime.InteropServices.JavaScript;

namespace CallOfSlop.ShipDesigner;

/// <summary>What the page calls: the game's own <see cref="DesignSession"/>, which the in-game editor uses too.</summary>
public static partial class Api
{
    private static readonly DesignSession _s = new();

    public static void Main() { }

    [JSExport]
    public static string Scene() => _s.Scene();

    [JSExport]
    public static string Apply(string command) => _s.Apply(command);

    [JSExport]
    public static string Undo() => _s.Undo();

    [JSExport]
    public static string Redo() => _s.Redo();

    [JSExport]
    public static string Save() => _s.Save();

    [JSExport]
    public static string Catalogue() => DesignSession.Catalogue();
}
