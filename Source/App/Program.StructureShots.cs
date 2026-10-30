using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--structure-shots</c>: pictures of the Sandbox's structural parts (docs/sandbox/CONSTRUCTS.md, "Structural parts")
/// on Construct Yard, written next to the log as smoke_structure_&lt;view&gt;.bmp, then the game quits: a gallery wall per
/// inventory group (Beams, Panels, Armour, Shapes: every part of the group stood on the ground in rows, in the
/// inventory's order), and a small frame welded from them through the building rules (rails, cross beams, a deck, armour
/// sides, a wedge nose, a gun port). An offline Sandbox game; the constructs are drawn, not simulated. Runs under a virtual
/// X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _structureShots;

    private static void RunStructureShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[structureshots] no Construct Yard map");
            return;
        }
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), AudioSystem, "Shots", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            Mode = GameMode.Sandbox,
        };
        _session = g;
        var now = Glfw.Time;
        for (var t = 0.0; t < Game.CountdownSeconds + 0.3; t += World.TickDt)
            g.Update(now += World.TickDt, World.TickDt);
        var w = g.Constructs;
        void Clear()
        {
            for (var i = w.Bodies.Count - 1; i >= 0; i--)
                w.Remove(w.Bodies[i]);
        }

        void Shot(string name, Vector3 eye, Vector3 target, float fov = 0.9f)
        {
            for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++)
            {
                Glfw.PollEvents();
                var fb = _window.FramebufferSize;
                Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
                    Matrix4x4.CreatePerspectiveFieldOfView(fov, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
                Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                DrawArena();
                DrawSandbox(now);
                Gfx.SetFog(map.Sky, 0, 0);
                if (frame == 2)
                {
                    _shotFull = true;
                    SaveScreenshot($"structure_{name}");
                    Console.WriteLine($"[structureshots] {name}: {w.Bodies.Count} constructs, {ConstructRender.LastBoxes} boxes drawn");
                }
                _window.SwapBuffers();
            }
        }

        // A gallery wall per group: the inventory's order, rows left to right, front to back.
        var cat = SandboxCatalogue.Make();
        var origin = new Vector3(-14, 0, 14);
        foreach (var group in new[] { "Beams", "Panels", "Armour", "Shapes" })
        {
            Clear();
            if (cat.Parts.Groups.FirstOrDefault(x => x.Name == group) is not { } grp)
                continue;
            var ids = grp.Sections.SelectMany(s => cat.Parts.Sections[s].Items).ToList();
            float x = 0, z = 0, rowDepth = 0;
            const float RowWidth = 14f;
            var placed = 0;
            foreach (var id in ids)
            {
                var d = ConstructParts.All[id];
                var size = d.BoundsMax - d.BoundsMin;
                if (x + size.X > RowWidth && x > 0)
                {
                    x = 0;
                    z += rowDepth + 0.6f;
                    rowDepth = 0;
                }
                var at = origin + new Vector3(x - d.BoundsMin.X, 0, z - d.BoundsMin.Z);
                var ground = ConstructWorld.Ground(World.Query, at.X, at.Z);
                if (w.Bodies.Count < ConstructWorld.MaxBodies && ConstructBuild.PlaceNew(w, d, ground, 0) is { } c)
                {
                    c.Anchored = true;
                    placed++;
                }
                x += size.X + 0.45f;
                rowDepth = MathF.Max(rowDepth, size.Z);
            }
            var depth = z + rowDepth;
            Console.WriteLine($"[structureshots] {group}: {placed} of {ids.Count} parts placed, {depth:0.0} m deep");
            var mid = origin + new Vector3(RowWidth / 2, 0, depth / 2);
            var floor = ConstructWorld.Ground(World.Query, mid.X, mid.Z).Y;
            Shot(group.ToLowerInvariant(), new Vector3(mid.X, floor + 6.5f + depth * 0.35f, origin.Z - 3.5f), new Vector3(mid.X, floor, mid.Z + 0.5f), 1.0f);
            Shot(group.ToLowerInvariant() + "_close", new Vector3(origin.X + 2.5f, floor + 2.6f, origin.Z - 2.2f), new Vector3(origin.X + 4.5f, floor + 0.2f, origin.Z + 2.2f), 0.9f);
        }

        // A small frame welded from the new parts through the building rules.
        Clear();
        var fails = new List<string>();
        var yard = ConstructWorld.Ground(World.Query, origin.X + 6, origin.Z + 4);
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Square Beam 2 m"), yard, 0) is { } car)
        {
            car.Anchored = true;
            int Fit(int on, string socket, string part, string by = "")
            {
                var d = ConstructParts.Need(part);
                var td = car.DefOf(on);
                var ts = Array.FindIndex(td.Sockets, s => s.Name == socket);
                for (var ds = 0; ds < d.Sockets.Length && ts >= 0; ds++)
                {
                    if (by.Length > 0 && d.Sockets[ds].Name != by)
                        continue;
                    for (var spin = 0; spin < 4; spin++)
                        if (ConstructBuild.Place(car, on, ts, d, ds, spin, out var gh) == null)
                            return ConstructBuild.Attach(w, car, d, gh);
                }
                fails.Add($"{part} on {td.Name}.{socket}");
                return -1;
            }
            var front = Fit(0, "+x0", "Square Beam 1 m", "-z");
            var back = Fit(0, "+x7", "Square Beam 1 m", "-z");
            var rail = front < 0 ? -1 : Fit(front, "+z", "Square Beam 2 m", "-x0");
            if (back >= 0)
                Fit(back, "+y3", "Square Beam 0.25 m", "-y");
            Fit(0, "+y1", "Tread Plate 1x2 m", "back00");
            Fit(0, "-x2", "Armour Plate 0.5x1 m", "back00");
            if (rail >= 0)
                Fit(rail, "+x2", "Heavy Armour 0.5 m", "back00");
            Fit(0, "-z", "Wedge 1:1 0.25 m", "back");
            if (rail >= 0)
                Fit(rail, "-z", "Quarter Round 0.25 m", "back");
            Fit(0, "+y0", "Steel Tube 1 m", "end-");
            Fit(0, "+z", "Beam End Cap", "back");
            if (rail >= 0)
                Fit(rail, "+y7", "Gun Port Panel 1 m", "-z0");
            car.Rebuild();
            Console.WriteLine($"[structureshots] frame: {car.PartCount} parts, {car.LinkCount} welds; {(fails.Count == 0 ? "every fit went on" : "refused: " + string.Join(", ", fails))}");
            var c0 = yard + new Vector3(0.6f, 0.4f, 0.9f);
            Shot("frame", c0 + new Vector3(-3.2f, 2.6f, -3.0f), c0);
            Shot("frame_back", c0 + new Vector3(3.4f, 2.2f, 3.2f), c0);
        }
        Console.WriteLine("[structureshots] done");
    }
}
