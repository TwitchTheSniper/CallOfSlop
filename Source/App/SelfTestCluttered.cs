namespace CallOfSlop;

/// <summary>
/// The "Cluttered pack" self-test section: the three maps in Maps/Cluttered (a small circle, a medium bean and a large
/// irregular blob), each packed with props and foliage. The shared part checks the pack and prints one summary line per
/// map; each map's own checks (shape, density, everything inside the outline, objectives, fairness, a bot match) live in
/// a partial method in its own file, so the three maps can be worked on separately.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredPack = "Cluttered";

    private static readonly List<string> ClutteredNotes = new();

    /// <summary>A result line, also collected for the section's GitHub notice.</summary>
    private static void ClutteredLine(string text)
    {
        Line(text);
        ClutteredNotes.Add(text);
    }

    private static void CheckCluttered()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        var pack = Maps.FindPack(ClutteredPack);
        if (pack == null)
        {
            Fail($"no {ClutteredPack} pack (Maps/Cluttered)");
            return;
        }
        ClutteredNotes.Clear();
        foreach (var m in pack.Maps)
        {
            var props = m.PropInstances;
            ClutteredLine($"{m.Name,-16} {m.Size,-6} {(m.Boundary != null ? m.Boundary.Spec.Shape.ToString().ToLowerInvariant() : "rect")} {m.PlayArea:0} m2  max {m.MaxPlayers}  "
                + $"boxes {Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox)}  props {props.Count} (solid {props.Count(p => p.Def.IsSolid)}, breakable {props.Count(p => p.IsBreakable)})  "
                + $"plants {m.FoliageInstances.Length} (trees {m.TrunkCount})  meshes {m.MeshPlacements.Count}");
            if (m.Boundary == null || m.Boundary.Spec.Shape == BoundaryShape.Rect)
                Fail($"{m.Name}: the Cluttered pack's maps have no rectangular outline");
        }
        CheckClutteredSmall();
        CheckClutteredMedium(); // SelfTestCluttered.Medium.cs: Crescent Bazaar
        CheckClutteredLarge();
        CheckClutteredCulDeSac(); // SelfTestCluttered.CulDeSac.cs: 04 Cul-de-sac
        CheckClutteredDockside(); // SelfTestCluttered.Dockside.cs: Dockside
        CheckClutteredOldTown(); // SelfTestCluttered.OldTown.cs: 06 Old Town
        CheckClutteredSupermarket(); // SelfTestCluttered.Supermarket.cs: 07 Supermarket
        World.SetMap(Maps.All[0]);
        if (ClutteredNotes.Count > 0)
            Line("::notice title=Cluttered pack::" + string.Join("%0A", ClutteredNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    /// <summary>The small (circle) map's checks, in its own file when it exists.</summary>
    static partial void CheckClutteredSmall();

    /// <summary>The medium (bean) map's checks: SelfTestCluttered.Medium.cs.</summary>
    static partial void CheckClutteredMedium();

    /// <summary>The large (irregular blob) map's checks, in its own file when it exists.</summary>
    static partial void CheckClutteredLarge();

    /// <summary>The harbour map's checks: SelfTestCluttered.Dockside.cs.</summary>
    static partial void CheckClutteredDockside();
    /// <summary>06 Old Town's checks: SelfTestCluttered.OldTown.cs.</summary>
    static partial void CheckClutteredOldTown();
}
