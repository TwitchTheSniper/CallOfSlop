namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Maps/Space/04-TwinShard.json: "Twin Shard", a binary asteroid with two gravity wells (medium, 16 players). The
/// port of tools/space/twinshard.py to the map kit, and the kit's worked example (docs/mapkit/MAPKIT.md).
///
/// Two 36 m shards of one broken asteroid, each with a pull of its own (a point field 26 m out from its centre), mined
/// by the invented Oresong Collective: Blue's camp on the west shard, Red's on the east. Two tethers join them: the
/// Line (upper, a railed catwalk at a third of normal gravity with the Knot in the middle round an open eye, the Drop,
/// and a side platform off it) and the Keel Line (lower; its underside is the floor, gravity pulls up). The Pebble is a
/// small moonlet between them with its own pull. Between the wells is plain gravity down into the void.
///
/// Mirror-symmetric about x = 0. Shards are solid icosphere meshes; rocks, pods, masts and pads are solid meshes stood
/// on the surface by their own rotations (there's one Planet op per map, so the Surface ops can't build two). Props
/// stand only near the poles (they're drawn upright).
/// </summary>
public sealed class TwinShard : IMapGenerator
{
    public string Name => "Twin Shard";
    public string Pack => "Space";
    public int Number => 4;

    const double FallKill = -4.0; // (the lowest a map may put it: everything sits 24 m up so the Keel Line's walkers stay above it)
    const double Y0 = 24.0; // the shards' centre height
    const double ShardX = 34.0, ShardR = 18.0, ShardZone = 26.0;
    const double TetherY = Y0 + 18.2; // top of the upper tether's deck
    const double KeelY = Y0 - 18.2; // underside of the lower tether's deck (its floor)
    const double PebbleR = 4.0, PebbleZone = 4.8; // just over its surface: a jump leaves its pull
    const double LineG = 6.0;

    static readonly Color Rock = (0.44, 0.40, 0.36), RockB = (0.40, 0.37, 0.35);
    const string Rock2 = "#5a524b", Ore = "#7a6a4a", Steel = "#6c6f73", Dark = "#3a3c40", Brass = "#8d7458", Hab = "#b8b0a0", Pad = "#8f949a";

    public void Build(Kit k)
    {
        k.Top.Set("description", "A binary asteroid: two gravity wells, a low-g tether over the top, an upside-down one under, and a moonlet between.");
        k.Top.Set("order", 604);
        k.Top.Set("destructible", false);
        k.Top.Set("fallKill", FallKill);
        k.Top.Set("maxPlayers", 16);
        k.Test(new JObject(("shape", "non-rectangular"), ("minProps", 18), ("minDecals", 60), ("minDecalShare", 0), ("symmetry", "mirrorX"),
            ("walkFairness", 0.12), ("void", false), ("botMatchSeconds", 12), ("botMatchMode", "koth"), ("spawnsStand", true), ("maxStuckBots", 4)));
        k.Top.Set("skybox", new JObject(("preset", "night"), ("zenith", "#05060c"), ("horizon", "#06070e"), ("ground", "#05060c"),
            ("sun", new JObject(("direction", JArray.Of(0.2, 0.55, -0.8)), ("intensity", 1.15), ("size", 2.2))),
            ("moon", new JObject(("direction", JArray.Of(-0.6, -0.2, 0.75)), ("size", 20), ("color", "#c9a27a"), ("intensity", 0.5))),
            ("stars", 1.9), ("clouds", false), ("ambient", 2.0)));
        k.Top.Set("boundary", new JObject(("shape", "smooth"), ("points", Boundary()), ("edge", "none"), ("barrierHeight", 1.0)));
        k.Top.Set("gravity", Zones());

        Shard(k, -ShardX, 0);
        Shard(k, ShardX, 1);
        Tethers(k);

        k.Hill(0, -4, 3.5, TetherY);
        k.Hill(0, 0, 4.0, KeelY);
        k.Hill(0, 17, 3.5, TetherY);
        k.Flag(-ShardX, 0, TetherY); // (on the Line's landing over each top pole)
        k.Flag(ShardX, 0, TetherY);
    }

    /// <summary>A solid mesh standing on the sphere (cx, cy, cz, radius) at latitude / longitude, sunk <paramref name="sink"/> m, its +Y along the surface normal.</summary>
    static void Stand(Kit k, string kind, double cx, double cy, double cz, double radius, double lat, double lon, Color color, double sink = 0.3, double above = 0.0,
        double? width = null, double? height = null, double? depth = null, double? boxRadius = null, int? detail = null, string? material = null)
    {
        var d = Geometry.SurfaceDir(lat, lon);
        var h = radius - sink + above;
        double px = cx + d.X * h, py = cy + d.Y * h, pz = cz + d.Z * h;
        var yaw = Math.Atan2(d.X, d.Z) * 180 / Math.PI;
        var pitch = Math.Acos(Math.Clamp(d.Y, -1.0, 1.0)) * 180 / Math.PI;
        k.Mesh(kind, px, pz, py, color, width, height, depth, boxRadius, null, yaw, pitch, null, null, detail, null, material, solid: true);
    }

    /// <summary>One shard at (sx, 0, 0): the ball, rocks, the camp near its top pole, a drill mast on its outer flank, a pad on its outer pole, spawns, waypoints.</summary>
    static void Shard(Kit k, double sx, int team)
    {
        double cx = sx, cy = Y0, cz = 0.0, r = ShardR;
        var outDir = sx > 0 ? 1 : -1; // the outer direction (away from the other shard)
        double lonOut = outDir > 0 ? 90 : 270, lonIn = outDir > 0 ? 270 : 90;
        k.Mesh("icosphere", cx, cz, cy - r, outDir < 0 ? Rock : RockB, width: 2 * r, height: 2 * r, depth: 2 * r, detail: 4, material: "Dirt", solid: true);
        // Rocks: a seeded scatter clear of the camp (top), the pad (outer pole) and the keel landing (bottom).
        var seed = outDir < 0 ? 11 : 23;
        var rng = new Rng(seed);
        var placed = 0;
        while (placed < 44)
        {
            var lat = Math.Asin(rng.Uniform(-0.75, 0.75)) * 180 / Math.PI;
            var lon = rng.Uniform(0, 360);
            if (lat > 50 || lat < -55)
                continue;
            var dlon = ((lon - lonOut + 180) % 360 + 360) % 360 - 180;
            if (Math.Abs(dlon) < 30 && Math.Abs(lat) < 25)
                continue; // the mast's flank stays clear
            var size = rng.Uniform(0.9, 2.6);
            Stand(k, "icosphere", cx, cy, cz, r, lat, lon, placed % 3 != 0 ? Rock2 : Ore, sink: size * 0.3, width: size, height: size * rng.Uniform(0.5, 0.9),
                depth: size * rng.Uniform(0.7, 1.2), detail: 1, material: placed % 3 != 0 ? "Dirt" : "Metal");
            placed++;
        }
        // The camp round the top pole (lat 70-85): habitat pods, a console, cargo, lights; two spawns inside the ring.
        foreach (var lon in new[] { lonOut, lonOut + 60, lonOut - 60 })
            Stand(k, "roundedbox", cx, cy, cz, r, 72, lon, Hab, sink: 0.4, width: 4.0, height: 2.6, depth: 3.0, boxRadius: 0.3, material: "Metal");
        Stand(k, "cylinder", cx, cy, cz, r, 76, 180, Steel, sink: 0.4, width: 1.2, depth: 1.2, height: 6.0, material: "Metal"); // (the camp mast, off the Line)
        Stand(k, "box", cx, cy, cz, r, 64, lonOut + 30, Brass, sink: 0.3, width: 2.4, height: 1.0, depth: 0.6, material: "Metal");
        Stand(k, "box", cx, cy, cz, r, 64, lonOut - 30, Brass, sink: 0.3, width: 2.4, height: 1.0, depth: 0.6, material: "Metal");
        // The drill mast on the outer flank at the equator, with ore crates and a cover ring.
        Stand(k, "cylinder", cx, cy, cz, r, 0, lonOut, Dark, sink: 0.5, width: 2.2, depth: 2.2, height: 12.0, material: "Metal");
        Stand(k, "box", cx, cy, cz, r, 0, lonOut, Steel, sink: 0.3, above: 0.0, width: 5.0, height: 0.9, depth: 5.0, material: "Metal");
        foreach (var (lat, lon) in new[] { (12, lonOut + 14), (-12, lonOut - 14), (12, lonOut - 14), (-12, lonOut + 14) })
            Stand(k, "box", cx, cy, cz, r, lat, lon, Ore, sink: 0.3, width: 1.6, height: 1.1, depth: 1.4, material: "Metal");
        // Keel landing: a brass ring round the bottom pole marks where the Keel Line meets the shard.
        for (var lon = 0; lon < 360; lon += 60)
            Stand(k, "box", cx, cy, cz, r, -70, lon, Brass, sink: 0.2, width: 1.6, height: 0.4, depth: 0.6, material: "Metal");
        // Props near the top pole (drawn upright: keep them within 15 degrees of it), clear of the Line's landing over the pole (z within +-2.5).
        k.Prop("Station Console", cx + outDir * 1.0, cz + 4.0, cy + r - 0.5, outDir < 0 ? 0 : 180);
        k.Prop("Station Cargo Pod", cx - outDir * 1.0, cz - 4.2, cy + r - 0.55, 0);
        k.Prop("Gravity Warning Beacon", cx + outDir * 4.0, cz + 3.0, cy + r - 0.7);
        k.Prop("Deck Light Post", cx - outDir * 4.0, cz - 3.0, cy + r - 0.7, 180);
        k.Prop("Station Supply Case", cx + outDir * 4.5, cz - 3.2, cy + r - 0.85, 35);
        k.Prop("Salvage Scrap", cx - outDir * 3.0, cz + 4.5, cy + r - 0.85);
        // Spawns: eight per shard, on the upper half within 35 degrees of the top (clear of the camp pods and rocks).
        foreach (var (lat, lon) in new[] { (60, lonIn + 30), (60, lonIn - 30), (58, lonOut + 100), (58, lonOut - 100), (48, lonIn + 15), (48, lonIn - 15), (46, lonOut + 140), (46, lonOut - 140) })
        {
            var d = Geometry.SurfaceDir(lat, lon);
            k.Spawn(team, cx + d.X * (r + 0.1), cz + d.Z * (r + 0.1), cy + d.Y * (r + 0.1));
        }
        // Waypoints spread over the shard (the zone pass probes its own grid; these anchor the poles and the flanks).
        foreach (var (lat, lon) in new[] { (85.0, 0.0), (-85, 0), (0, lonIn), (30, 0), (30, 180), (-30, 0), (-30, 180), (0, lonOut + 45), (0, lonOut - 45) })
        {
            var d = Geometry.SurfaceDir(lat, lon);
            k.Waypoint(cx + d.X * (r + 0.05), cz + d.Z * (r + 0.05), cy + d.Y * (r + 0.05));
        }
        // Decals on the top: the collective's mark, plating seams, stains, a sign on a pod.
        k.Decal("Verrin Salvage Mark", cx, cz, cy + r + 0.2, size: 5);
        k.DecalScatter(["Gravity Plating Grid", "Oil Stain", "Rust Stain", "Scorch Mark"], cx - 7, -7, cx + 7, 7, 24, seed, y: cy + r - 0.6, gap: 2.2);
        k.DecalScatter(["Oil Stain", "Scorch Mark", "Rust Stain", "Skid Marks"], cx - 15, -15, cx + 15, 15, 30, seed + 1, y: cy + r - 6, gap: 3.0);
        foreach (var lon in new[] { lonOut, lonOut + 60, lonOut - 60 })
        {
            var d = Geometry.SurfaceDir(72, lon);
            double px = cx + d.X * (r + 1.0), pz = cz + d.Z * (r + 1.0);
            k.Decal("Vent Grille", px + (outDir > 0 ? 0.5 : -0.5), pz, cy + d.Y * (r + 1.0) + 1.0, "wall", size: 1.2);
        }
    }

    /// <summary>The tethers and the pebble.</summary>
    static void Tethers(Kit k)
    {
        double x0 = -ShardX - 1, x1 = ShardX + 1; // the decks run into the rock past the poles (a landing 0.2 m over each pole)
        // The Line (upper): a 3 m deck, railings (solid bands 0.45-1.1 m up, bullets go under), the Knot and its side platform.
        k.Block(x0, TetherY - 0.4, -1.5, -2, TetherY, 1.5, Steel);
        k.Block(2, TetherY - 0.4, -1.5, x1, TetherY, 1.5, Steel);
        foreach (var s in new[] { -1, 1 })
            foreach (var xa in new[] { x0, 6 })
            {
                var xb = xa == x0 ? -6 : x1;
                k.Block(xa, TetherY + Structures.RailLow, s * 1.5, xb, TetherY + Structures.RailHigh, s * 1.7, Brass);
            }
        // The Knot: a 12 m pad with a 4 m eye in its middle (the Drop: fall through it onto the Pebble).
        k.Block(-6, TetherY - 0.4, -6, -2, TetherY, 6, Pad);
        k.Block(2, TetherY - 0.4, -6, 6, TetherY, 6, Pad);
        k.Block(-2, TetherY - 0.4, -6, 2, TetherY, -2, Pad);
        k.Block(-2, TetherY - 0.4, 2, 2, TetherY, 6, Pad);
        k.Block(-3, TetherY - 0.4, 6, 3, TetherY, 16, Steel);
        k.Block(-6, TetherY - 0.4, 14, 6, TetherY, 20, Pad);
        k.Block(-6, TetherY + 0.45, -6.2, 6, TetherY + 1.1, -6, Brass);
        k.Block(-6, TetherY + 0.45, 20, 6, TetherY + 1.1, 20.2, Brass);
        k.Block(-6.2, TetherY + 0.45, 14, -6, TetherY + 1.1, 20, Brass);
        k.Block(6, TetherY + 0.45, 14, 6.2, TetherY + 1.1, 20, Brass);
        k.Block(-3.2, TetherY + 0.45, 6, -3, TetherY + 1.1, 14, Brass);
        k.Block(3, TetherY + 0.45, 6, 3.2, TetherY + 1.1, 14, Brass);
        foreach (var s in new[] { -1, 1 })
        {
            k.Block(s * 6, TetherY + 0.45, -6, s * 6.2, TetherY + 1.1, -1.7, Brass);
            k.Block(s * 6, TetherY + 0.45, 1.7, s * 6.2, TetherY + 1.1, 6, Brass);
        }
        // Pylons under the Line, cosmetic, and the cable under the Keel Line.
        for (var x = -24; x <= 24; x += 12)
            k.Mesh("cylinder", x, 0, TetherY - 3.4, Dark, width: 0.8, depth: 0.8, height: 3.0, material: "Metal");
        k.Mesh("cylinder", 0, 0, KeelY - 1.0, Dark, width: 0.5, depth: 0.5, height: 2 * ShardX - 4, roll: 90, segments: 12, material: "Metal");
        // The Keel Line (lower): its underside is the floor in the inverted zone; its top is a plain floor you can fall onto.
        k.Block(x0, KeelY, -1.5, x1, KeelY + 0.4, 1.5, Steel);
        k.Block(-6, KeelY, -5, 6, KeelY + 0.4, 5, Pad);
        // Rails on the underside (hanging "down" in world terms: below the floor's level in that frame means y below KeelY).
        foreach (var s in new[] { -1, 1 })
        {
            k.Block(x0, KeelY - 1.1, s * 1.5, -6, KeelY - 0.45, s * 1.7, Brass);
            k.Block(6, KeelY - 1.1, s * 1.5, x1, KeelY - 0.45, s * 1.7, Brass);
            k.Block(-6, KeelY - 1.1, s * 5, 6, KeelY - 0.45, s * 5.2, Brass);
            k.Block(s * 5, KeelY - 1.1, -5, s * 5.2, KeelY - 0.45, -1.7, Brass);
            k.Block(s * 5, KeelY - 1.1, 1.7, s * 5.2, KeelY - 0.45, 5, Brass);
        }
        // The Pebble.
        k.Mesh("icosphere", 0, 0, Y0 - PebbleR, Ore, width: 2 * PebbleR, height: 2 * PebbleR, depth: 2 * PebbleR, detail: 3, material: "Dirt", solid: true);
        foreach (var (lat, lon, size) in new[] { (40, 30, 1.4), (-20, 150, 1.1), (10, 250, 1.6), (-50, 320, 1.0) })
            Stand(k, "icosphere", 0, Y0, 0, PebbleR, lat, lon, Rock2, sink: size * 0.3, width: size, height: size * 0.6, depth: size * 0.9, detail: 1, material: "Dirt");
        // Props on the Knot and the side platform; signs at the Line's ends and on the Knot's rails.
        k.Prop("Gravity Plating Node", 0, 4.5, TetherY);
        k.Prop("Station Deck Barrier", -4, 2.5, TetherY, 90);
        k.Prop("Station Deck Barrier", 4, 2.5, TetherY, 90);
        k.Prop("Mooring Clamp", 4.2, 18.6, TetherY); // (off hill 3's centre)
        k.Prop("Gravity Warning Beacon", -4.5, 15, TetherY);
        k.Prop("Gravity Warning Beacon", 4.5, 15, TetherY);
        k.Prop("Station Supply Case", 3.5, -4, TetherY, 20);
        k.Prop("Salvage Scrap", -3, -4.5, TetherY);
        foreach (var (x, z, rot) in new (double, double, double)[] { (0, -2.6, 0), (0, 2.6, 0), (-2.6, 0, 90), (2.6, 0, 90) })
            k.Decal("Hazard Stripes", x, z, TetherY, size: 4, rotation: rot);
        k.Decal("Stratos Pad Ring", 0, 17, TetherY, size: 5);
        k.Decal("Hazard Stripes", 0, 7, TetherY, size: 2.4, rotation: 0);
        k.Decal("Hazard Stripes", 0, 13, TetherY, size: 2.4, rotation: 0);
        foreach (var x in new[] { -26, -16, 16, 26 })
            k.Decal("Gravity Arrow Cyan", x, 0, TetherY, size: 1.6, rotation: x < 0 ? 90 : 270);
        foreach (var x in new[] { -20, 20 })
            k.Decal("Gravity Arrow Amber", x, 0, KeelY - 0.1, "ceiling", size: 1.6, rotation: x < 0 ? 90 : 270);
        k.Decal("Stratos Pad Ring", 0, 0, KeelY - 0.1, "ceiling", size: 7);
        k.DecalScatter(["Gravity Plating Grid", "Rust Stain"], x0, -1.4, x1, 1.4, 30, 5, y: TetherY, gap: 2.5);
        // Waypoints along both tethers (the grid can miss a 3 m catwalk).
        for (var x = -35; x <= 35; x += 5)
            if (x != 0)
                k.Waypoint(x, 0, TetherY); // (none in the Knot's eye)
        for (var i = -14; i <= 14; i++)
            k.Waypoint(i * 2.5, 0, KeelY - 0.05); // (zone nodes link up to 4.8 m apart: closer than the Line's)
        foreach (var s in new[] { -1, 1 })
        {
            k.Waypoint(s * 4, 4, TetherY);
            k.Waypoint(s * 4, -4, TetherY);
        }
        k.Waypoint(0, 10, TetherY);
        k.Waypoint(0, 17, TetherY);
        k.Waypoint(0, 3, KeelY - 0.05);
        k.Waypoint(0, -3, KeelY - 0.05);
    }

    static JObject Zones()
    {
        var z = new JArray();
        // (The Line's zone is only as wide as its deck: the shards' tops either side of it keep their own pull.)
        z.Add(new JObject(("shape", "box"), ("name", "the line"), ("min", JArray.Of(-ShardX + 3, Y0 + 15, -2.5)), ("max", JArray.Of(ShardX - 3, Y0 + 26, 2.5)),
            ("field", new JObject(("type", "uniform"), ("strength", LineG)))));
        z.Add(new JObject(("shape", "box"), ("name", "the knot"), ("min", JArray.Of(-7, Y0 + 15, -7)), ("max", JArray.Of(7, Y0 + 26, 21)),
            ("field", new JObject(("type", "uniform"), ("strength", LineG)))));
        z.Add(new JObject(("shape", "box"), ("name", "the keel line"), ("min", JArray.Of(-ShardX + 3, KeelY - 6, -6)), ("max", JArray.Of(ShardX - 3, KeelY, 6)),
            ("field", new JObject(("type", "uniform"), ("direction", JArray.Of(0, 1, 0))))));
        z.Add(new JObject(("shape", "sphere"), ("name", "the pebble"), ("center", JArray.Of(0, Y0, 0)), ("radius", PebbleZone),
            ("field", new JObject(("type", "point"), ("center", JArray.Of(0, Y0, 0)), ("strength", 14)))));
        var west = new JObject(("shape", "sphere"), ("name", "west shard"), ("center", JArray.Of(-ShardX, Y0, 0)), ("radius", ShardZone),
            ("field", new JObject(("type", "point"), ("center", JArray.Of(-ShardX, Y0, 0)), ("strength", 18))));
        z.Add(west);
        z.Add(Kit.TwinZone(west, Symmetry.MirrorX, "west", "east"));
        return new JObject(("type", "zones"), ("default", new JObject(("type", "uniform"))), ("zones", z));
    }

    static JArray Boundary()
    {
        // The north-east quarter, mirrored in z then in x (points on the axes are shared).
        var quarter = new List<(double X, double Z)> { (0, -30), (14, -28), (30, -32), (50, -30), (60, -16), (62, 0) };
        var east = Geometry.FromHalf(quarter, Symmetry.MirrorZ).Select(p => (p[0], p[1])).ToList();
        var all = Geometry.FromHalf(east, Symmetry.MirrorX);
        return new JArray(all.Select(p => (JNode)JArray.Of((int)p[0], (int)p[1])));
    }
}
