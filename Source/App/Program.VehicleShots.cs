using System.Numerics;
using Brutal.GlfwApi;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--vehicle-shots "Bandit Box Truck 14" ["Other 3" ...] [--map NAME]</c>: real shaded pictures of generated vehicles (docs/vehiclekit/VEHICLEKIT.md, "Looking at one"), written next to the log as
/// smoke_vehicle_&lt;name&gt;_&lt;view&gt;.bmp, then the game quits: from the left, from either front and rear three-quarter, from above and from inside its first room. Each name is a generator and a seed, or a blueprint's name. Runs under a virtual X server.
/// </summary>
public static unsafe partial class Program
{
    private static bool _vehicleShots;

    private static void RunVehicleShots(string[] args)
    {
        var map = args.SkipWhile(a => !a.Equals("--map", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } n ? Maps.Find(n) : Maps.Find("Construct Yard");
        World.SetMap(map ?? Maps.All[0]);
        map = World.Map;
        var names = args.SkipWhile(a => !a.Equals("--vehicle-shots", StringComparison.OrdinalIgnoreCase)).Skip(1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        foreach (var name in names)
        {
            var at = name.LastIndexOf(' ');
            ConstructBuild.Blueprint? bp;
            Room[] rooms = [];
            if (at > 0 && int.TryParse(name[(at + 1)..], out var seed) && VehicleRegistry.Find(name[..at]) is { } g)
            {
                var v = VehicleRegistry.Make(g, seed);
                rooms = v.Rooms.ToArray();
                bp = ConstructBuild.ParseBlueprint(v.Text(), "shot.json");
            }
            else if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(name))
                bp = ConstructBuild.ParseBlueprint(File.ReadAllText(name), Path.GetFileName(name)); // (any blueprint file: a role vehicle written by --cargo --write)
            else
                bp = ConstructBuild.FindBlueprint(name);
            if (bp == null)
            {
                Console.WriteLine($"[vehicleshots] no vehicle \"{name}\"");
                continue;
            }
            var w = new ConstructWorld();
            var ground = ConstructWorld.Ground(World.Query, Vector3.Zero);
            var c = ConstructBuild.BuildOnGround(w, bp, ground, 0, out var why, VehicleChecks.TowOf(bp));
            if (c == null)
            {
                Console.WriteLine($"[vehicleshots] {name}: {why}");
                continue;
            }
            var mid = c.Pos + Vector3.Transform((c.LocalMin + c.LocalMax) * 0.5f, c.Rot);
            var size = MathF.Max(c.LocalMax.Z - c.LocalMin.Z, 5);
            var views = new List<(string Name, Vector3 Eye, Vector3 Look, float Fov)>
            {
                ("side", mid + new Vector3(-size * 1.2f, 0.3f, 0), mid, 0.8f),
                ("front", mid + new Vector3(size * 0.8f, 1.5f, -size * 0.8f), mid, 0.8f),
                ("rear", mid + new Vector3(-size * 0.8f, 2f, size * 0.8f), mid, 0.8f),
                ("top", mid + new Vector3(0.01f, size * 1.3f, 0), mid, 0.8f),
            };
            if (rooms.Length > 0)
            {
                var inside = c.ToWorld(rooms[0].Middle + rooms[0].FloorRest + new Vector3(0, 1.5f, -1));
                views.Add(("inside", inside, inside + new Vector3(0.1f, -0.3f, 1), 1.4f));
            }
            var file = Path.GetFileNameWithoutExtension(name).Replace(' ', '_');
            foreach (var view in views)
                ShootVehicleView(w, map, view.Eye, view.Look, view.Fov, () => SaveScreenshot($"vehicle_{file}_{view.Name}"));
            Console.WriteLine($"[vehicleshots] {name}: {views.Count} pictures");
        }
        Console.WriteLine("[vehicleshots] done");
    }

    /// <summary>Draws a vehicle's world from one camera for three frames (the first ones settle the lighting) and calls <paramref name="atLast"/> on the last, before it is shown: read the back buffer there.</summary>
    private static void ShootVehicleView(ConstructWorld w, Map map, Vector3 eye, Vector3 look, float fov, Action atLast)
    {
        for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++)
        {
            Glfw.PollEvents();
            var fb = _window.FramebufferSize;
            Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
            Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, look, Vector3.UnitY),
                Matrix4x4.CreatePerspectiveFieldOfView(fov, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, MathF.Max(map.FarPlane, 300)));
            Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
            Glfw.SetTime(ShotTime);
            DrawArena();
            ConstructRender.Draw(Gfx, w, 1f, false);
            Gfx.SetFog(map.Sky, 0, 0);
            if (frame == 2)
                atLast();
            _window.SwapBuffers();
        }
    }
}
