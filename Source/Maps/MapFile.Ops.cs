using System.Reflection;

namespace CallOfSlop;

public static partial class MapFile
{
    /// <summary>
    /// The builder method a map-file op calls (the same table <see cref="Load"/> dispatches on), so the map kit
    /// (Source/MapKit) can check the ops it emits against the real op definitions: name, parameter names and types.
    /// <paramref name="hasName"/>: whether the op carries a "name" key, which turns Prop into PlaceProp and Building into
    /// PlaceBuilding. Null when the op is unknown.
    /// </summary>
    public static MethodInfo? OpBuilder(string op, bool hasName)
    {
        if (!Builders.TryGetValue(op, out var method))
            return null;
        if (hasName && method.Name == nameof(Map.Prop))
            return Builders[nameof(Map.PlaceProp)];
        if (hasName && method.Name == nameof(Map.Building))
            return Builders[nameof(Map.PlaceBuilding)];
        return method;
    }

    /// <summary>A parameter type's name as the docs give it (number, integer, colour...).</summary>
    public static string TypeNameOf(Type t) => TypeName(t);

    /// <summary>Every op name a map file can use.</summary>
    public static IEnumerable<string> OpNames => Builders.Keys.OrderBy(k => k);
}
