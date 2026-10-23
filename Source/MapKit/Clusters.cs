namespace CallOfSlop.MapKit;

/// <summary>
/// Groups of props and meshes that the Space generators each built by hand, as parameterised functions: a gas tank
/// (and a tank farm of them), a cargo stack, a row of market stalls, a landing pad with its markings and beacons, and a
/// crane gantry across a dock. Every solid round thing gets a hidden collider core; every group places waypoints where
/// the nav grid could miss. Colours default to the Station palette; prop names are the shipped Station / Vesper sets.
/// </summary>
public static class Clusters
{
    public const string Hull = "#cdbfa8", Hull2 = "#bfb19a", Rim = "#8d7458", Steel = "#6c6f73", Dark = "#3a3c40", Orange = "#e8701e", Hazard = "#f0c040";

    /// <summary>A big gas tank: a cylinder with a domed top and two bands, over a solid round core.</summary>
    public static void Tank(Kit k, double x, double z, double y, double radius, double height, Color? body = null, Color? dome = null, Color? band = null)
    {
        k.Mesh("cylinder", x, z, y, body ?? Hull, width: 2 * radius, height: height, depth: 2 * radius, segments: 24, material: "Metal");
        k.Mesh("sphere", x, z, y + height - radius * 0.4, dome ?? Hull2, width: 2 * radius * 0.99, height: radius * 0.8, depth: 2 * radius * 0.99, segments: 24, detail: 10);
        foreach (var f in new[] { 0.2, 0.62 })
            k.Mesh("tube", x, z, y + height * f, band ?? Rim, radius: radius + 0.08, inner: radius - 0.05, height: 0.3, segments: 24);
        k.RoundCollider(x, z, radius, y, y + height);
    }

    /// <summary>
    /// A tank farm round (cx, cz): <paramref name="count"/> tanks on a ring of <paramref name="spread"/> metres (sizes
    /// seeded), pipe runs between them, a canister rack and a riser, an oil stain in the middle.
    /// </summary>
    public static void TankFarm(Kit k, double cx, double cz, double y, int count = 3, double spread = 4.0, int seed = 1, double phaseDeg = 0)
    {
        var rng = new Rng(seed);
        for (var i = 0; i < count; i++)
        {
            var a = (phaseDeg + 360.0 * i / count) * Math.PI / 180;
            var r = rng.Uniform(1.5, 2.2);
            Tank(k, cx + spread * Math.Cos(a), cz + spread * Math.Sin(a), y, r, rng.Uniform(4.0, 7.0));
        }
        k.Prop("Station Pipe Run", cx + spread * 1.4, cz, y, 90);
        k.Prop("Volatile Gas Canister Rack", cx - spread * 1.3, cz + 1.0, y, 90);
        k.Prop("Station Pipe Riser", cx, cz - spread * 1.5, y);
        k.Decal("global:Oil Stain", cx, cz, y, size: 2.0);
        k.Waypoint(cx, cz, y);
    }

    /// <summary>A cargo stack: pods in a row with case stacks and a loose case in front, turned <paramref name="yaw"/>.</summary>
    public static void CargoStack(Kit k, double cx, double cz, double y, double yaw = 0, int pods = 2, bool withCases = true)
    {
        var a = yaw * Math.PI / 180;
        (double X, double Z) At(double lx, double lz) => (cx + lx * Math.Cos(a) + lz * Math.Sin(a), cz - lx * Math.Sin(a) + lz * Math.Cos(a));
        for (var i = 0; i < pods; i++)
        {
            var (x, z) = At((i - (pods - 1) / 2.0) * 4.2, 0);
            k.Prop("Station Cargo Pod", x, z, y, yaw + 90);
        }
        if (withCases)
        {
            var (sx, sz) = At(-(pods / 2.0) * 4.2 - 1.6, 0.5);
            k.Prop("Supply Case Stack", sx, sz, y, yaw);
            var (cxx, czz) = At(0.8, 3.2);
            k.Prop("Station Supply Case", cxx, czz, y, yaw + 20);
        }
    }

    /// <summary>
    /// A row of market stalls along X at z, fronts facing <paramref name="facing"/> (+1: +Z, -1: -Z): stalls every
    /// <paramref name="spacing"/> metres alternating the shipped Vesper stall kinds, holo sign posts between, a lamp at each
    /// end and lantern strings over the lane.
    /// </summary>
    public static void MarketStalls(Kit k, double x0, double x1, double z, int facing, double y = 0, double spacing = 5.0, int seed = 3)
    {
        string[] stalls = ["Bazaar Stall Red", "Bazaar Stall Blue", "Noodle Counter", "Bazaar Stall Blue"];
        var rng = new Rng(seed);
        var yaw = facing > 0 ? 0 : 180;
        var n = Math.Max(1, (int)Math.Floor((x1 - x0) / spacing));
        for (var i = 0; i < n; i++)
        {
            var x = x0 + spacing * (i + 0.5);
            k.Prop(stalls[i % stalls.Length], x, z, y, yaw);
            if (i + 1 < n)
                k.Prop("Holo Sign Post", x + spacing / 2, z + facing * 0.5, y);
        }
        k.Prop("Plaza Lamp", x0 - 1.0, z + facing * 2.5, y);
        k.Prop("Plaza Lamp", x1 + 1.0, z + facing * 2.5, y);
        for (var x = x0 + spacing; x < x1 - 1; x += spacing * 2)
            k.Prop("Lantern String", x, z + facing * 3.0, y);
        k.Prop("Trash Can", x0 + rng.Uniform(1, 3), z + facing * 3.5, y);
        k.DecalScatter(["global:Paper Litter", "global:Cigarette Butts", "global:Oil Stain"], x0, Math.Min(z, z + facing * 5), x1, Math.Max(z, z + facing * 5), 8, seed, y, gap: 2.5);
        for (var x = x0 + 2; x < x1; x += 6)
            k.Waypoint(x, z + facing * 3.0, y);
    }

    /// <summary>
    /// A landing pad: a ring mark of <paramref name="radius"/>, the pad glyph, a number, eight floor lights round the rim,
    /// two beacons and (unless <paramref name="empty"/>) a parked skimmer with cargo. <paramref name="side"/> turns the
    /// skimmer and the number: +1 faces east, -1 west.
    /// </summary>
    public static void LandingPad(Kit k, double cx, double cz, double y, double radius = 7.75, int number = 1, int side = 1, bool empty = false)
    {
        k.Decal("Stratos Pad Ring", cx, cz, y, size: radius * 2);
        k.Decal("Stratos Pad Glyph", cx, cz, y, size: radius * 0.97, rotation: side > 0 ? 90 : 270);
        k.Decal(number == 1 ? "Stratos Pad Number 1" : "Stratos Pad Number 2", cx - side * radius * 0.71, cz, y, size: 2.4, rotation: side < 0 ? 90 : 270);
        if (!empty)
        {
            k.Prop("Parked Cloud Skimmer", cx + side * radius * 0.84, cz, y, 90);
            k.Prop("Station Cargo Pod", cx + side * 2.0, cz - radius * 0.62, y);
            k.Prop("Station Supply Case", cx - side * 2.2, cz - radius * 0.72, y, 20);
        }
        for (var i = 0; i < 8; i++)
        {
            var a = (22.5 + 45 * i) * Math.PI / 180;
            k.Prop("Deck Floor Light", cx + radius * 1.41 * Math.Cos(a), cz + radius * 1.41 * Math.Sin(a), y);
        }
        k.Prop("Landing Beacon", cx + side * radius * 1.06, cz - radius * 0.65, y);
        k.Prop("Landing Beacon", cx + side * radius * 1.06, cz + radius * 0.65, y);
        k.Waypoint(cx, cz, y);
    }

    /// <summary>
    /// A crane gantry across a dock at x = <paramref name="gx"/>: a railed 3 m bridge at <paramref name="bridgeY"/> spanning
    /// z = -half..half, a leg on each side standing on <paramref name="legY"/>, the crane beam over it with a trolley and a
    /// hook over z = <paramref name="hookZ"/>, hazard marks at both ends and waypoints along the bridge.
    /// </summary>
    public static void CraneGantry(Kit k, double gx, double half, double bridgeY, double legY, double hookZ = 0, Color? deck = null, bool railingGapsAtEnds = true)
    {
        var beamY = bridgeY + 8.0;
        k.Block(gx - 1.5, bridgeY - 0.5, -half, gx + 1.5, bridgeY, half, deck ?? "#9a9590");
        foreach (var s in new[] { -1, 1 })
        {
            Structures.Railing(k, gx + s * 1.5, -half, bridgeY, gx + s * 1.5, half, bridgeY, gaps: railingGapsAtEnds ? [(half, 1.7)] : null);
            k.Block(gx - 3.6, legY, s * (half - 2) - 1, gx - 1.6, beamY, s * (half - 2) + 1, Orange);
            k.Mesh("box", gx - 2.6, s * (half - 2), beamY, Hazard, width: 2.4, height: 0.3, depth: 2.4);
            k.Decal("global:Hazard Stripes", gx, s * (half - 1.5), bridgeY, size: 2.8, rotation: 0);
        }
        k.Mesh("box", gx - 2.6, 0, beamY, Orange, width: 2.2, height: 1.6, depth: 2 * half + 4, material: "Metal");
        k.Mesh("box", gx + 0.2, 0, beamY + 0.2, Steel, width: 2.0, height: 0.6, depth: 2 * half);
        k.Mesh("box", gx - 1.2, hookZ, beamY - 1.4, Steel, width: 3.6, height: 1.4, depth: 2.2);
        k.Mesh("cylinder", gx - 1.2, hookZ, beamY - 4.8, Dark, width: 0.12, height: 3.4, depth: 0.12, segments: 8);
        k.Mesh("box", gx - 1.2, hookZ, beamY - 5.8, Hazard, width: 0.8, height: 1.0, depth: 0.5);
        k.MeshAt("torus", gx - 1.2, beamY - 6.4, hookZ, Dark, pitch: 90, radius: 0.45, inner: 0.1, segments: 16, detail: 6);
        for (var z = -half + 2; z <= half - 2; z += 5)
            k.Waypoint(gx, z, bridgeY);
    }

    /// <summary>A drum with a dome on top (solid drum, cosmetic dome) and a ring of lamps, standing at height y.</summary>
    public static void DomeTower(Kit k, double x, double z, double y, double radius, double drumHeight, Color? color = null, bool light = true)
    {
        var c = color ?? Hull;
        k.Mesh("cylinder", x, z, y, c, width: 2 * radius, height: drumHeight, depth: 2 * radius, segments: 32, material: "Metal");
        k.Mesh("tube", x, z, y + drumHeight - 0.5, Rim, radius: radius + 0.15, inner: radius - 0.1, height: 0.5, segments: 32);
        k.Mesh("sphere", x, z, y + drumHeight - radius * 0.5, c, width: 2 * radius * 0.98, height: radius * 1.3, depth: 2 * radius * 0.98, segments: 32, detail: 12);
        k.Mesh("cylinder", x, z, y + drumHeight + radius * 0.62, Rim, width: radius * 0.5, height: 0.5, depth: radius * 0.5, segments: 16);
        if (light)
            k.Mesh("sphere", x, z, y + drumHeight + radius * 0.62 + 0.5, "#ff5a40", width: 0.5, height: 0.5, depth: 0.5, segments: 10, detail: 5);
        for (var i = 0; i < 12; i++)
        {
            var a = 2 * Math.PI * i / 12;
            k.Mesh("box", x + (radius + 0.02) * Math.Cos(a), z + (radius + 0.02) * Math.Sin(a), y + drumHeight * 0.55, "#ffd9a0", width: 0.1, height: 0.5, depth: 0.7, yaw: -a * 180 / Math.PI);
        }
        k.RoundCollider(x, z, radius, y, y + drumHeight);
    }
}
