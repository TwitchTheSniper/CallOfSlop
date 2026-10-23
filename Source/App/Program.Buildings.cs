using System.Numerics;

namespace CallOfSlop;

/// <summary>Building drawing (see <see cref="BuildingRenderer"/>) and its part of the props smoke step (Program.Props.cs).</summary>
public static partial class Program
{
    private static readonly BuildingRenderer BuildingGfx = new();

    /// <summary>
    /// Smoke step, "buildings_interact" view: opens every door and shatters every window within 30 m in front of the camera
    /// (no session: straight on the map), so the screenshot shows open doors, empty frames and glass in the air.
    /// </summary>
    private static void SmokeBuildings(Map map, Vector3 eye, Vector3 target)
    {
        if (map.PlacedBuildings.Count == 0)
            return;
        var fwd = Vector3.Normalize(target - eye);
        bool InView(Vector3 p) => Vector3.Distance(p, eye) < 30f && Vector3.Dot(Vector3.Normalize(p - eye), fwd) > 0.7f;
        int doors = 0, windows = 0;
        for (var i = 0; i < map.BuildingDoors.Count; i++)
            if (InView(map.BuildingDoors[i].Center) && map.SetBuildingDoor(i, true))
                doors++;
        for (var i = 0; i < map.BuildingWindows.Count; i++)
        {
            var w = map.BuildingWindows[i];
            if (!InView(w.Plan.Glass.Center) || !map.BreakBuildingWindow(i))
                continue;
            PropDebris.SpawnBox(w.Plan.Glass, eye, PropDamage.Bullet, i, 10, 0.03f, 0.12f, 3.5f, Map.GlassColor, new Vector3(0.85f, 0.93f, 0.97f));
            windows++;
        }
        Console.WriteLine($"[smoke] buildings: opened {doors} door(s) and broke {windows} window(s) in view on {map.Name}");
    }

    /// <summary>
    /// Smoke step, "destruction_*" views on the Destruction Test (no session: straight on the map, like the props step): a
    /// 2.2 m hole in the corner shop's front wall, or the apartment block's ground-floor walls gone so the floors above fall.
    /// Debris, dust and falling slabs fly; one "[smoke] destruction" line (chunks broken and fallen, CPU of the break).
    /// </summary>
    private static void SmokeDestruction(Map map, string view)
    {
        if (map.Destruction is not { } d)
            return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var broken = new List<int>();
        var fallen = new List<int>();
        if (view == "destruction_blast")
        {
            var centre = new Vector3(-4f, 1.3f, -11.6f);
            for (var c = 0; c < d.Count; c++)
                if (Vector3.Distance(Vector3.Clamp(centre, d.Original[c].Min, d.Original[c].Max), centre) < 2.2f && map.BreakChunk(c))
                    broken.Add(c);
        }
        else if (map.PlacedBuildings.FirstOrDefault(b => b.Def.Name == "Apartment Block" && b.Structure >= 0) is { } apt)
        {
            var level1 = apt.Plan.BaseY + BuildingGen.GroundSlab + apt.Def.StoreyHeight;
            var st = d.Structures[apt.Structure];
            for (var c = st.ChunkStart; c < st.ChunkStart + st.ChunkCount; c++)
                if (d.Original[c].Min.Y < level1 - 0.25f && d.Original[c].Max.Y > apt.Plan.BaseY + 0.3f && (d.Flags[c] & MapDestruction.FlagFloor) == 0 && map.BreakChunk(c))
                    broken.Add(c);
        }
        var structures = broken.Select(c => d.Owner[c]).Distinct().ToList();
        foreach (var s in structures)
            map.CollapseStructure(s, fallen);
        var ms = sw.Elapsed.TotalMilliseconds;
        foreach (var c in broken)
        {
            var col = map.Colors[d.Solid[c]];
            var m = d.MaterialOf(c);
            PropDebris.SpawnBox(d.Original[c], d.Original[c].Center + new Vector3(0, 0, 3), PropDamage.Explosion, c, Math.Min(m.DebrisCount, 4), m.DebrisMin, m.DebrisMax, 5f, col, col * 0.7f);
        }
        foreach (var c in fallen.Take(60))
            PropDebris.SpawnSlab(d.Original[c], map.Colors[d.Solid[c]], c);
        Console.WriteLine($"[smoke] destruction {view} ({map.Name}): {broken.Count} chunks broken, {fallen.Count} fell, {ms:0.0} ms (break + collapse); "
                          + $"{d.Count} chunks on the map, {PropDebris.Count} debris pieces flying");
    }

    /// <summary>One "[smoke] buildings" line for a buildings view: what the building renderer drew.</summary>
    private static void LogBuildingSmoke(string view)
    {
        var map = World.Map;
        Console.WriteLine($"[smoke] buildings {view} ({map.Name}): {map.PlacedBuildings.Count} buildings, {BuildingGfx.Boxes} shell boxes, "
                          + $"{BuildingGfx.LastInstances} boxes drawn in {BuildingGfx.LastDraws} draw calls (the frame above), "
                          + $"{map.BuildingDoors.Count(d => d.Open)}/{map.BuildingDoors.Count} doors open, {map.BuildingWindows.Count(w => w.Broken)}/{map.BuildingWindows.Count} windows broken, "
                          + $"{map.PropInstances.Count} props");
    }
}
