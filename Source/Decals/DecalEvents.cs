namespace CallOfSlop;

/// <summary>
/// Box changes that decals care about. <see cref="World.SolidChanged"/> (a prop broke, a door or window opened, closed or
/// broke) and <see cref="World.InvalidateGrid"/> (props restored) report here: impacts on the box fade out at once, and
/// <see cref="Version"/> goes up so the renderer re-checks which boxes are still as the map decals were baked on.
/// </summary>
public static class DecalEvents
{
    /// <summary>Goes up on every box change of any map.</summary>
    public static int Version { get; private set; }

    /// <summary>Box <paramref name="index"/> of <paramref name="map"/> changed.</summary>
    public static void SolidChanged(Map map, int index)
    {
        Version++;
        ImpactDecals.SolidChanged(map, index);
    }

    /// <summary>Many boxes of <paramref name="map"/> changed back (broken props restored).</summary>
    public static void BoxesRestored(Map map)
    {
        _ = map;
        Version++;
    }

    /// <summary>
    /// Fills <paramref name="alive"/> (one byte per box, at least <see cref="DecalMesh.Owners"/> long) with 255 for every box
    /// that is still exactly as baked and 0 for one that broke or moved (its decals are hidden).
    /// </summary>
    public static void Alive(Map map, DecalMesh mesh, byte[] alive)
    {
        var owners = mesh.Owners;
        var solid = map.Solid;
        for (var i = 0; i < alive.Length; i++)
            alive[i] = (byte)(i < owners.Length && i < solid.Count && solid[i] == owners[i] ? 255 : 0);
    }
}
