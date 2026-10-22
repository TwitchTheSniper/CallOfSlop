using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Prop drawing (see <see cref="PropRenderer"/>) and its smoke-test step.</summary>
public static unsafe partial class Program
{
    private static readonly PropRenderer PropGfx = new();

    // ---- Smoke step: the "Prop Gallery" and "Prop Yard" maps from fixed viewpoints -------------------------------
    // Runs after the foliage step. For each view: 0.5 s to settle, 1.5 s measured, one "[smoke] props" line (fps, worst
    // frame, props drawn, draw calls, debris, prop CPU/GPU ms) and a screenshot smoke_props_VIEW.bmp. The "break" view
    // breaks every breakable prop in front of the camera first, and shoots the debris in flight. VSync is off for the
    // step so the fps numbers mean something; settings are restored afterwards (never saved).

    private static readonly (string Name, string Map, Vector3 Eye, Vector3 Target, bool Break)[] PropsSmokeViews =
    [
        ("gallery", "Prop Gallery", new Vector3(0f, 17f, 42f), new Vector3(0f, 0f, 2f), false),
        ("gallery_street", "Prop Gallery", new Vector3(-21f, 1.8f, -16.5f), new Vector3(-6f, 0.6f, -24f), false),
        ("gallery3", "Prop Gallery 3", new Vector3(0f, 16f, 34f), new Vector3(0f, 0f, -2f), false), // the indoor set: smoke_props_gallery3.bmp
        ("gallery2", "Prop Gallery 2", new Vector3(0f, 38f, 50f), new Vector3(0f, 0f, -2f), false), // the outdoor props: smoke_props_gallery2.bmp
        ("yard", "Prop Yard", new Vector3(-37f, 1.7f, 1f), new Vector3(8f, 1f, -1f), false),
        ("yard_break", "Prop Yard", new Vector3(-37f, 1.7f, 1f), new Vector3(8f, 1f, -1f), true),
        ("yard_overview", "Prop Yard", new Vector3(-30f, 24f, 34f), new Vector3(0f, 0f, 0f), false),
        // The Dev pack's Building Yard (Program.Buildings.cs): every starter building from above, a furnished living room at eye
        // height, then the corner shop with its doors opened and windows shot out (glass in the air): smoke_props_buildings_*.bmp.
        ("buildings_overview", "Building Yard", new Vector3(-62f, 42f, 58f), new Vector3(2f, 0f, -4f), false),
        ("buildings_room", "Building Yard", new Vector3(44.0f, 1.7f, -7.6f), new Vector3(41.0f, 0.8f, -10.5f), false),
        ("buildings_interact", "Building Yard", new Vector3(31f, 1.7f, -0.5f), new Vector3(31f, 1.4f, -7.5f), true),
        // The residential set's showcase (Maps/Dev/BuildingsResidential.json): the whole street, then the Modern Villa's great room.
        ("buildings_residential", "Residential Street", new Vector3(-72f, 40f, 52f), new Vector3(0f, 0f, 0f), false),
        ("buildings_residential_villa", "Residential Street", new Vector3(19f, 1.8f, -15f), new Vector3(13f, 0.9f, -18f), false),
        ("cluttered_small", "Carousel", new Vector3(-27f, 13f, 25f), new Vector3(0f, 1f, -3f), false), // Cluttered pack: smoke_cluttered_small.bmp
        // The Cluttered pack's large map (thousands of props and plants): overview from the south-west, then the market at eye height.
        ("cluttered_large", "Sprawl", new Vector3(-96f, 42f, 84f), new Vector3(8f, 0f, -12f), false),
        ("cluttered_large_market", "Sprawl", new Vector3(-24f, 1.7f, 6f), new Vector3(20f, 1.2f, -4f), false),
        ("cluttered_oldtown", "Old Town", new Vector3(-70f, 38f, 52f), new Vector3(0f, 0f, -4f), false), // 06 Old Town from the south-west: smoke_cluttered_oldtown.bmp
        ("cluttered_oldtown_street", "Old Town", new Vector3(-58f, 1.7f, -5f), new Vector3(-16f, 1.6f, -4f), false), // West Main Street at eye height
        ("cluttered_powderkeg", "Powder Keg", new Vector3(-78f, 46f, 62f), new Vector3(0f, 0f, -4f), false), // 12 Powder Keg from the south-west: smoke_cluttered_powderkeg.bmp
        ("cluttered_powderkeg_hall", "Powder Keg", new Vector3(-26f, 1.7f, -1.5f), new Vector3(14f, 1.4f, -5f), false), // the hall's staging band at eye height, racks and barrels
        ("cluttered_supermarket", "Supermarket", new Vector3(-48f, 24f, 60f), new Vector3(0f, 2f, 6f), false), // 07 Supermarket: the car park and store front
        ("cluttered_supermarket_inside", "Supermarket", new Vector3(12.5f, 5.3f, -8f), new Vector3(-12f, 1.2f, 8f), false), // over the shop floor from the mezzanine
        // World Tour 02 Slopview Mall: the mall and its sloping car park from the south-west, then the atrium from the west balcony.
        ("world_mall", "Slopview Mall", new Vector3(-72f, 34f, 78f), new Vector3(0f, 0f, 2f), false),
        ("world_mall_atrium", "Slopview Mall", new Vector3(-20.5f, 6.4f, -6.4f), new Vector3(8f, 2.2f, 2.5f), false),
        // 11 Deepcut Mine: the pit and the terraces from above the camp, then inside the North Drift at eye height, looking up the tunnel.
        ("cluttered_deepcut", "Deepcut Mine", new Vector3(-84f, 34f, 46f), new Vector3(14f, 0f, -6f), false),
        ("cluttered_deepcut_tunnel", "Deepcut Mine", new Vector3(-40f, 1.9f, -40f), new Vector3(-18f, 3.0f, -40f), false),
        ("cluttered_medium", "Crescent Bazaar", new Vector3(-46f, 15f, 33f), new Vector3(-2f, 0f, -6f), false), // Cluttered pack: smoke_cluttered_medium.bmp
        // 04 Cul-de-sac: the turning circle from above the street's mouth, then the north houses at eye height from the island.
        ("cluttered_culdesac", "Cul-de-sac", new Vector3(36f, 22f, 14f), new Vector3(-16f, 0f, -4f), false),
        ("cluttered_culdesac_street", "Cul-de-sac", new Vector3(-9f, 1.7f, -6f), new Vector3(-17f, 1.2f, -20f), false),
        // Cluttered pack, Dockside: the bay from above the west horn, then the waterfront road at eye height towards the fish market.
        ("cluttered_dockside", "Dockside", new Vector3(-62f, 24f, 38f), new Vector3(0f, 0f, -16f), false),
        ("cluttered_dockside_street", "Dockside", new Vector3(-27f, 1.7f, 2.5f), new Vector3(2f, 1.4f, 9f), false),
        // Cluttered pack, 08 Truck Stop: the whole stop from above the highway, then the motel courtyard from its balcony.
        ("cluttered_truckstop", "Truck Stop", new Vector3(-6f, 30f, 52f), new Vector3(2f, 0f, -6f), false),
        ("cluttered_truckstop_street", "Truck Stop", new Vector3(-44.8f, 5.1f, 10f), new Vector3(-30f, 0.5f, -10f), false),
        // World Tour, 01 Main Street: the town and its cornfields from above the south-west corn, then Main Street at eye height looking east.
        ("world_mainstreet", "Main Street", new Vector3(-50f, 30f, 46f), new Vector3(2f, 0f, -4f), false),
        ("world_mainstreet_street", "Main Street", new Vector3(-30f, 2.7f, 3.0f), new Vector3(20f, 2.2f, -1.5f), false),
        // Cluttered pack, 10 Timberline: the forest from above the lake in the south-west, then the woods at eye height on Blue's trail to the middle camp.
        ("cluttered_timberline", "Timberline", new Vector3(-96f, 44f, 72f), new Vector3(6f, 0f, -6f), false),
        ("cluttered_timberline_forest", "Timberline", new Vector3(-34f, 2.0f, -6f), new Vector3(0.5f, 1.2f, 2f), false),
        // World Tour, 06 Vuorikoski (Finland): the mine from above Honkala, the forest at eye height, the shaft station underground.
        ("world_finland", "Vuorikoski", new Vector3(-118f, 48f, 60f), new Vector3(0f, 6f, -8f), false),
        ("world_finland_forest", "Vuorikoski", new Vector3(-66f, 5.3f, -46f), new Vector3(-40f, 2.6f, -30f), false),
        ("world_finland_tunnel", "Vuorikoski", new Vector3(-24f, -6.3f, -4f), new Vector3(0f, -6.5f, -4f), false),
        // World Tour, 07 Cotswold Village: the hillside from above the meadow in the south-west, then up the green from the High Street at eye height.
        ("world_village", "Cotswold Village", new Vector3(-48f, 30f, 46f), new Vector3(2f, 2f, -6f), false),
        ("world_village_street", "Cotswold Village", new Vector3(13f, 3.5f, 13.6f), new Vector3(-4f, 5f, -14f), false),
        // Cluttered pack, 09 Beachhead: the beach and the bluff from out over the sea, then the bluff from behind a landing craft at eye height.
        ("cluttered_beachhead", "Beachhead", new Vector3(-60f, 32f, 92f), new Vector3(5f, 6f, 0f), false),
        ("cluttered_beachhead_beach", "Beachhead", new Vector3(-12f, 3.1f, 47f), new Vector3(1f, 10f, 9f), false),
        // World Tour, 03 Silo: the forested hill and the open silo from over Blue's motor pool, down the shaft past the missile from
        // the top gantry, and the launch control room at eye height.
        ("world_silo", "Silo", new Vector3(-112f, 46f, 66f), new Vector3(0f, 8f, 0f), false),
        ("world_silo_shaft", "Silo", new Vector3(0f, -2.1f, -11.2f), new Vector3(0f, -15f, 4f), false),
        ("world_silo_control", "Silo", new Vector3(0f, -2.2f, -41.6f), new Vector3(0f, -2.4f, -52f), false),
        // World Tour, 05 Bocage: the hedgerow maze and the hamlet from above the south-west, then down the sunken west lane at eye height.
        ("world_bocage", "Bocage", new Vector3(-112f, 46f, 86f), new Vector3(-6f, 0f, -4f), false),
        ("world_bocage_lane", "Bocage", new Vector3(-64f, 0.75f, 4.4f), new Vector3(-36f, 0.2f, 5.5f), false),
        // World Tour, 04 Paris Quarter: the quarter from above the south-west, then the raised square at eye height towards the fountain and the north row.
        ("world_paris", "Paris Quarter", new Vector3(-74f, 40f, 52f), new Vector3(0f, 6f, -10f), false),
        ("world_paris_square", "Paris Quarter", new Vector3(-22f, 4.7f, 3f), new Vector3(6f, 7f, -16f), false),
        // Animated props: two shots 0.25 s apart (smoke_props_animated_a/b.bmp show the motion), then the gallery from above.
        ("animated", "Animated Props", new Vector3(-6f, 3.2f, 17f), new Vector3(2f, 1.2f, -2f), false),
        ("animated_overview", "Animated Props", new Vector3(0f, 22f, 36f), new Vector3(0f, 0f, -3f), false),
        ("flags", "Flag Lineup", new Vector3(-1.5f, 3.4f, 4.5f), new Vector3(-1.2f, 3.9f, -6f), false), // the flag pool: US, UK, France, Finland at the front (smoke_props_flags.bmp)
        // The Dev pack's Military Props map (Props/Military): every row from above the planes, then the tanks at eye height.
        ("military_overview", "Military Props", new Vector3(0f, 52f, 100f), new Vector3(0f, 0f, -4f), false),
        ("military_tanks", "Military Props", new Vector3(-31f, 1.8f, -2f), new Vector3(-12f, 1.4f, -16f), false),
        // Decals (Program.Decals.cs): the Decal Test map's map decals from above, then at eye height after a burst of bullet impacts.
        ("decals", "Decal Test", new Vector3(0f, 26f, 30f), new Vector3(0f, 0f, -2f), false),
        ("decals_impacts", "Decal Test", new Vector3(-2f, 1.7f, 10f), new Vector3(-2f, 1.4f, -6f), false),
        // The Dev pack's Decal Surfaces map: the street, car park and yard from above, then the wall gallery and ground sampler at eye height.
        ("decals_surfaces", "Decal Surfaces", new Vector3(-4f, 30f, 44f), new Vector3(-4f, 0f, 14f), false),
        ("decals_surfaces_gallery", "Decal Surfaces", new Vector3(8f, 1.8f, -18f), new Vector3(18f, 1.2f, -29f), false),
    ];

    private static int _propsSmoke = -1; // view index; -1 = not running
    private static double _propsSmokeStart, _propsSmokeMeasureStart, _propsSmokeWorst;
    private static bool _propsSmokeMeasuring, _propsSmokeAdvance, _propsSmokeShot, _propsSmokeShotA, _propsSmokeSavedVSync;
    private static int _propsSmokeFrames;

    private static bool PropsSmokeActive => _propsSmoke >= 0;

    private static void StartPropsSmoke(double now)
    {
        if (Maps.Find("Prop Gallery") == null && Maps.Find("Prop Yard") == null)
        {
            Console.WriteLine("[smoke] props: maps 'Prop Gallery' and 'Prop Yard' not found, step skipped");
            StartMeshSmoke(now); // then the mesh step (Program.Meshes.cs), then on to the maps
            return;
        }
        _propsSmokeSavedVSync = _settings.VSync;
        _settings.VSync = false;
        _propsSmoke = 0;
        BeginPropsSmokeView(now);
    }

    private static void BeginPropsSmokeView(double now)
    {
        _propsSmokeStart = now;
        _propsSmokeMeasuring = false;
        _propsSmokeAdvance = false;
        _propsSmokeShot = false;
        _propsSmokeShotA = false;
        var view = PropsSmokeViews[_propsSmoke];
        if (Maps.Find(view.Map) is { } map)
        {
            if (World.Map != map)
                World.SetMap(map);
            if (view.Break)
                SmokeBreakProps(map, view.Eye, view.Target);
            if (view.Name == "decals_impacts")
                SmokeDecalImpacts(map, view.Eye, view.Target); // Program.Decals.cs
        }
    }

    /// <summary>Breaks every breakable prop within 30 m in front of the camera (no session: straight on the map, then debris).</summary>
    private static void SmokeBreakProps(Map map, Vector3 eye, Vector3 target)
    {
        var fwd = Vector3.Normalize(target - eye);
        var broken = 0;
        foreach (var i in map.BreakableProps)
        {
            var p = map.PropInstances[i];
            var to = p.Bounds.Center - eye;
            if (to.Length() > 30f || Vector3.Dot(Vector3.Normalize(to), fwd) < 0.75f || !map.BreakProp(i))
                continue;
            PropDebris.Spawn(p, eye, PropDamage.Bullet, i);
            broken++;
        }
        Console.WriteLine($"[smoke] props: broke {broken} prop(s) in view on {map.Name}, {PropDebris.Count} debris pieces flying");
        SmokeBuildings(map, eye, target); // and open the doors and break the windows in view (Program.Buildings.cs)
    }

    private static void PropsSmokeTick(double now)
    {
        if (_propsSmokeAdvance)
        {
            if (PropsSmokeViews[_propsSmoke].Break)
                World.Map.RestoreProps();
            _propsSmoke++;
            if (_propsSmoke >= PropsSmokeViews.Length)
            {
                _propsSmoke = -1;
                _settings.VSync = _propsSmokeSavedVSync;
                PropDebris.Clear();
                StartMeshSmoke(now); // then the mesh step (Program.Meshes.cs), then on to the maps
                return;
            }
            BeginPropsSmokeView(now);
            return;
        }
        var view = PropsSmokeViews[_propsSmoke];
        var t = now - _propsSmokeStart;
        if (view.Break && !_propsSmokeShot && t > 0.35)
        {
            SmokeShot("props_" + view.Name); // debris in the air
            _propsSmokeShot = true;
        }
        if (view.Name == "animated" && !_propsSmokeShot && t > 0.6)
        {
            // A pair a quarter of a second apart: fans, flags and lights are visibly elsewhere in the second one.
            if (!_propsSmokeShotA)
            {
                SmokeShot("props_animated_a");
                _propsSmokeShotA = true;
            }
            else if (t > 0.85)
            {
                SmokeShot("props_animated_b");
                _propsSmokeShot = true;
            }
        }
        if (!_propsSmokeMeasuring)
        {
            if (t > 0.5)
            {
                _propsSmokeMeasuring = true;
                _propsSmokeFrames = 0;
                _propsSmokeWorst = 0;
                _propsSmokeMeasureStart = now;
                PropGfx.ResetWindow(now);
                FoliageGfx.ResetWindow(now);
                DecalGfx.ResetWindow(now);
            }
            return;
        }
        _propsSmokeFrames++;
        _propsSmokeWorst = Math.Max(_propsSmokeWorst, FrameDt);
        if (t < 2.0)
            return;
        var secs = Math.Max(now - _propsSmokeMeasureStart, 1e-6);
        var w = PropGfx.Window;
        Console.WriteLine($"[smoke] props {view.Name,-14} ({World.Map.Name}): {_propsSmokeFrames / secs:0} fps avg ({secs * 1000 / Math.Max(_propsSmokeFrames, 1):0.00} ms/frame), "
            + $"worst {_propsSmokeWorst * 1000:0.0} ms, {PropGfx.Uploaded} props uploaded, {w.AvgInstances:0} drawn in {w.AvgDraws:0.0} draw calls, "
            + $"debris {w.AvgDebris:0}, props CPU {w.AvgCpuMs:0.000} ms, GPU {(w.AvgGpuMs >= 0 ? $"{w.AvgGpuMs:0.000} ms" : "n/a")}, "
            + $"{World.Map.BrokenProps.Count} broken, GL error 0x{(uint)OpenGl.Native.glGetError():X}"
            + (PropGfx.UploadedAnimated > 0
                ? $"; animated {PropGfx.UploadedAnimated} ({(PropGfx.AnimationOn ? "on" : "off")}, {PropGfx.AnimatedTypes} types): {w.AvgAnimInstances:0} moving + {w.AvgFrozenInstances:0} still in "
                  + $"{w.AvgAnimDraws:0.0} draw calls, anim GPU {(w.AvgAnimGpuMs >= 0 ? $"{w.AvgAnimGpuMs:0.000} ms" : "n/a")}"
                : ""));
        if (view.Name.StartsWith("buildings_", StringComparison.Ordinal))
            LogBuildingSmoke(view.Name); // the Dev pack's Building Yard (Program.Buildings.cs)
        if (view.Name.StartsWith("decals", StringComparison.Ordinal))
            LogDecalSmoke(view.Name); // Program.Decals.cs
        if (view.Name.StartsWith("cluttered_", StringComparison.Ordinal))
        {
            // Cluttered pack maps: the foliage and box side of the same frames (boxes are still one draw call each).
            var f = FoliageGfx.Window;
            var map = World.Map;
            var boxes = 0;
            for (var i = 0; i < map.Solid.Count; i++)
                if (map.IsDrawnBox(i))
                    boxes++;
            Console.WriteLine($"[smoke] foliage {view.Name} ({map.Name}): {FoliageGfx.Uploaded} plants uploaded, {f.AvgInstances:0} drawn in {f.AvgDraws:0.0} draw calls, "
                + $"foliage CPU {f.AvgCpuMs:0.000} ms, GPU {(f.AvgGpuMs >= 0 ? $"{f.AvgGpuMs:0.000} ms" : "n/a")}; {boxes} boxes, {map.PropInstances.Count} props, "
                + $"{map.MeshPlacements.Count} meshes, quality {_settings.FoliageQuality}");
        }
        if (!view.Break && view.Name != "animated") // the animated view took its pair already
            SmokeShot(view.Name.StartsWith("cluttered") ? view.Name : "props_" + view.Name); // the Cluttered pack's views keep their own names
        _propsSmokeAdvance = true;
    }

    /// <summary>Renders the current props smoke view (no menu over it).</summary>
    private static void DrawPropsSmoke()
    {
        var fb = _window.FramebufferSize;
        if (fb.X <= 0 || fb.Y <= 0)
            return;
        var (_, _, eye, target, _) = PropsSmokeViews[Math.Max(_propsSmoke, 0)];
        var map = World.Map;
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(BaseFov, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);
    }
}
