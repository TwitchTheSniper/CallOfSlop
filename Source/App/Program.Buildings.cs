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
