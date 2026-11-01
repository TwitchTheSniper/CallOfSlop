namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Maps/Sandbox/ProvingGround.json: "Proving Ground", the Sandbox pack's big vehicle test field (board item
/// dev-sandbox-proving-ground-pg01, developer request 2026-10-31: Construct Yard is 64 x 64 m, too small to drive).
///
/// 384 x 384 m (halfX/halfZ 192, six times the yard's side) on terrain with a 2 m cell. The Sandbox mode's settings are
/// the yard's: prototype grid, endless parts, 8 players. Almost everything is terrain (ramps, banks, craters, a gully);
/// the few boxes are start lines, gate posts, marker pillars and the crash wall, and the cones are props, so the draw
/// count stays small and the far side of the field is only fog away.
///
/// South-west corner: the SPAWN HUB, a 56 m square pad with the parts rack, a gun bench, storage and ready-made
/// blueprints (buggy, racer, truck, trailer) and an empty middle to build in. East of it, along the south edge: a 300 m
/// DRAG STRIP (start line, a mark every 50 m, speed-trap gates at 100, 200 and 300 m, a sand run-off). North of the
/// strip: a SLALOM, NARROW GATES (2.5, 3 and 4 m), a banked OVAL (15 and 25 degree turns), the flat CRASH PAD with a
/// solid wall, four HILL-CLIMB ramps (10, 20, 30 and 40 degrees), four JUMPS, a long gentle TOW grade, and in the north
/// west the ROUGH GROUND (bumps, washboard, a rock garden, craters, a trench), a GULLY with a bridge and a raised deck
/// reached by a narrow ramp.
///
/// Signs are map logic: a zone you drive or walk through says what it is in the kill feed (map logic text). A real
/// speed-trap readout would need a map-side sensor the construct world doesn't have; carry a Speed Gauge.
/// </summary>
public sealed class ProvingGround : IMapGenerator
{
    public string Name => "Proving Ground";
    public string Pack => "Sandbox";
    public int Number => 0; // Maps/Sandbox/ProvingGround.json

    const double H = 192; // half size

    // Hub (south-west corner pad)
    const double HubX = -160, HubZ = -160, HubHalf = 28;

    // Drag strip: along x at z = StripZ, from StripX0 for 300 m
    const double StripZ = -160, StripW = 16, StripX0 = -126, StripLen = 300;

    // Banked oval: centre, half straight, centreline radius, half width
    const double OvalX = -30, OvalZ = -20, OvalL = 50, OvalR = 45, OvalHw = 7, OvalBerm = 10;
    const double BankEast = 15, BankWest = 25; // degrees

    // Hill climb: four ramps climbing +z from z = ClimbZ0 to a 10 m deck
    const double ClimbZ0 = -50, ClimbTop = 10, ClimbW = 12;
    static readonly double[] ClimbX = [100, 120, 140, 160];
    static readonly double[] ClimbDeg = [10, 20, 30, 40];

    // Crash pad
    const double PadX = 140, PadZ = -100;

    const string Dirt = "#7a6a52", Asphalt = "#4a4d52", Paint = "#e8e2d0", Red = "#b8382c", Yellow = "#d4a82c", Stone = "#8f8c84", Blue = "#2f5f9a";

    readonly List<(string Name, double X0, double Z0, double X1, double Z1, string Text)> _signs = new();

    public void Build(Kit k)
    {
        k.Describe("Sandbox vehicle test field, 384 x 384 m: a spawn hub with the parts rack, gun bench, storage and ready-made vehicles; a 300 m drag strip with speed-trap gates; "
                   + "a banked oval; hill-climb ramps at 10, 20, 30 and 40 degrees; jumps; rough ground (washboard, rock garden, craters, a trench); a slalom and narrow gates; "
                   + "a gully with a bridge; a raised deck; a long tow grade; and a flat pad with a solid wall for crash tests.", order: 931, maxPlayers: 8);
        k.Size(H, H);
        k.Top.Set("prototypeGrid", true);
        k.Top.Set("sky", "#a9c6e2");
        k.Terrain(Terrain());

        k.Ground("#56703f");
        k.Perimeter(3, "#9a9a9e");

        Hub(k);
        DragStrip(k);
        Oval(k);
        Slalom(k);
        HillClimb(k);
        Jumps(k);
        Rough(k);
        Gully(k);
        Tow(k);
        CrashPad(k);
        Signs(k);

        // Spawns on the hub (four a team) and the hills the objective checks want (the hub and the oval's infield).
        // (The map's two spawn groups are the hub and the strip's staging area, 110 m apart, as the objective checks want; flags at the hub and the crash pad.)
        for (var i = 0; i < 4; i++)
        {
            k.Emit(new JObject(("op", "Spawn"), ("x", Kit.R(HubX - 6 + i * 4)), ("z", Kit.R(HubZ + 6))));
            k.Emit(new JObject(("op", "Spawn"), ("x", Kit.R(-58 + i * 4)), ("z", Kit.R(StripZ + 22))));
        }
        k.Flag(HubX, HubZ + 18);
        k.Flag(PadX, PadZ);
        k.Hill(HubX, HubZ, 6);
        k.AutoWaypoints(10);

        k.Top.Set("sandbox", Sandbox());
        k.Test(new JObject(
            ("size", "large"), ("maxPlayers", 8), ("handPlaced", false), ("minHills", 1), ("minTeamSpawns", 4), ("walkFairness", 0.0),
            ("maxDrawnBoxes", 160), ("maxNavBuildMs", 9000), ("minMainNavPct", 60),
            ("probes", new JArray(Probes())),
            ("botMatchSeconds", 6), ("spawnsStand", true)));
    }

    // ---- terrain ----------------------------------------------------------------------------------------------------

    static JObject F(string type, params (string Key, object? Value)[] more)
    {
        var o = new JObject(("type", type));
        foreach (var (key, value) in more)
            if (value != null)
                o.Add(key, value);
        return o;
    }

    static JArray P(double x, double z) => JArray.Of(Kit.R(x), Kit.R(z));

    static JArray P(double x, double z, double y) => JArray.Of(Kit.R(x), Kit.R(z), Kit.R(y));

    JObject Terrain()
    {
        var f = new List<JNode>();

        // The banked oval first, as an explicit height grid (everything else is clear of its box).
        f.Add(OvalGrid());
        foreach (var s in new[] { -1, 1 }) // paint the straights
            f.Add(F("ramp", ("points", JArray.Of(P(OvalX - OvalL, OvalZ + s * OvalR, 0), P(OvalX + OvalL, OvalZ + s * OvalR, 0))), ("width", OvalHw * 2), ("edge", 2), ("material", "dirt")));

        // Hub, drag strip, crash pad and the sand run-off.
        f.Add(F("flatten", ("x", HubX), ("z", HubZ), ("w", HubHalf * 2), ("d", HubHalf * 2), ("height", 0), ("edge", 4), ("material", "dirt")));
        f.Add(F("flatten", ("x", StripX0 + StripLen / 2), ("z", StripZ), ("w", StripLen), ("d", StripW), ("height", 0), ("edge", 3), ("material", "dirt")));
        f.Add(F("flatten", ("x", StripX0 + StripLen + 9), ("z", StripZ), ("w", 18), ("d", StripW + 4), ("height", 0), ("edge", 3), ("material", "sand")));
        f.Add(F("flatten", ("x", PadX), ("z", PadZ), ("w", 84), ("d", 64), ("height", 0), ("edge", 4), ("material", "dirt")));

        // Hill climb: a ramp at each grade up to a 10 m deck.
        for (var i = 0; i < ClimbX.Length; i++)
        {
            var len = ClimbTop / Math.Tan(ClimbDeg[i] * Math.PI / 180);
            f.Add(F("ramp", ("points", JArray.Of(P(ClimbX[i], ClimbZ0, 0), P(ClimbX[i], ClimbZ0 + len, ClimbTop))), ("width", ClimbW), ("edge", 2), ("material", "dirt")));
            f.Add(F("plateau", ("x", ClimbX[i]), ("z", ClimbZ0 + len + 5), ("w", ClimbW), ("d", 10), ("height", ClimbTop), ("edge", 2), ("mode", "fill"), ("material", "dirt")));
        }

        // Jumps (a kicker, a gap, a landing slope down), side by side climbing +z.
        foreach (var (x, h, up, gap, down) in JumpSpecs)
        {
            f.Add(F("ramp", ("points", JArray.Of(P(x, JumpZ0, 0), P(x, JumpZ0 + up, h))), ("width", 12), ("edge", 1.5), ("material", "dirt")));
            f.Add(F("ramp", ("points", JArray.Of(P(x, JumpZ0 + up + gap, h), P(x, JumpZ0 + up + gap + down, 0))), ("width", 12), ("edge", 1.5), ("material", "dirt")));
        }

        // Tow grade: a long gentle slope (about 4.4 degrees) with a flat top.
        f.Add(F("ramp", ("points", JArray.Of(P(TowX, TowZ0, 0), P(TowX, TowZ0 + TowLen, TowRise))), ("width", 16), ("edge", 4), ("material", "dirt")));
        f.Add(F("plateau", ("x", TowX), ("z", TowZ0 + TowLen + 10), ("w", 16), ("d", 20), ("height", TowRise), ("edge", 4), ("mode", "fill"), ("material", "dirt")));

        // Rough ground (north-west).
        f.Add(F("noise", ("amplitude", 0.45), ("scale", 7), ("octaves", 2), ("seed", 5), ("x", -165), ("z", 55), ("radius", 26)));
        for (var i = 0; i < 18; i++) // washboard: low ridges across a lane
            f.Add(F("ridge", ("points", JArray.Of(P(-186 + i * 3.6, 92), P(-186 + i * 3.6, 106))), ("width", 2.4), ("height", 0.28)));
        var rng = new Rng(11);
        for (var i = 0; i < 26; i++) // rock garden: lumps
            f.Add(F("hill", ("x", Math.Round(-188 + rng.Next() * 56, 1)), ("z", Math.Round(118 + rng.Next() * 30, 1)), ("radius", Math.Round(1.4 + rng.Next() * 1.8, 1)), ("height", Math.Round(0.4 + rng.Next() * 1.0, 2))));
        f.Add(F("valley", ("points", JArray.Of(P(-190, 168), P(-100, 168))), ("width", 6), ("depth", 2.0)));
        f.Add(F("valley", ("points", JArray.Of(P(-190, 182), P(-100, 182))), ("width", 3.2), ("depth", 1.2)));
        foreach (var (x, z, r, d) in new (double, double, double, double)[] { (-150, 30, 9, 1.5), (-132, 42, 6, 1.0), (-155, 58, 7, 1.2), (-128, 66, 8, 1.4), (-168, 20, 5, 0.8), (-140, 14, 4.5, 0.9) })
            f.Add(F("basin", ("x", x), ("z", z), ("radius", r), ("depth", d)));

        // Gully with a bridge, and the raised deck with a narrow ramp.
        f.Add(F("valley", ("points", JArray.Of(P(-96, 130), P(-4, 130))), ("width", 22), ("depth", 6)));
        f.Add(F("plateau", ("x", DeckX), ("z", DeckZ), ("w", 16), ("d", 16), ("height", 3), ("edge", 2), ("mode", "fill"), ("material", "dirt")));
        f.Add(F("ramp", ("points", JArray.Of(P(DeckX, DeckZ - 24, 0), P(DeckX, DeckZ - 8, 3))), ("width", 2.5), ("edge", 0.8), ("material", "dirt")));

        return new JObject(
            ("cell", 2),
            ("margin", 24),
            ("surface", new JObject(("grass", "#6d7d5a"), ("dirt", Dirt), ("rock", "#7b7770"), ("sand", "#b5a577"), ("dirtSlope", 12), ("rockSlope", 40), ("sandBelow", -9))),
            ("features", new JArray(f)));
    }

    // Jumps: x, launch height, kicker length, gap, landing length.
    const double JumpZ0 = 50;
    static readonly (double X, double H, double Up, double Gap, double Down)[] JumpSpecs =
    [
        (20, 0.8, 6, 4, 8),
        (42, 2.0, 10, 8, 14),
        (64, 4.0, 14, 16, 24),
        (86, 6.0, 16, 30, 36),
    ];

    const double TowX = 150, TowZ0 = 60, TowLen = 130, TowRise = 10;
    const double DeckX = -30, DeckZ = 168;

    JNode OvalGrid()
    {
        var pad = OvalHw + OvalBerm + 2;
        var x0 = OvalX - OvalL - OvalR - pad;
        var x1 = OvalX + OvalL + OvalR + pad;
        var z0 = OvalZ - OvalR - pad;
        var z1 = OvalZ + OvalR + pad;
        x0 = Math.Floor(x0 / 2) * 2;
        z0 = Math.Floor(z0 / 2) * 2;
        var cols = (int)Math.Ceiling((x1 - x0) / 2) + 1;
        var rows = (int)Math.Ceiling((z1 - z0) / 2) + 1;
        var hs = new List<JNode>(cols * rows);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                hs.Add(new JNumber(Kit.R(OvalHeight(x0 + c * 2, z0 + r * 2), 2)));
        return F("grid", ("x0", x0), ("z0", z0), ("cell", 2), ("cols", cols), ("rows", rows), ("heights", new JArray(hs)), ("mode", "set"));
    }

    /// <summary>The oval's ground: a banked turn at each end (a straight line across, climbing to the outside), the straights flat.</summary>
    static double OvalHeight(double x, double z)
    {
        var dx = x - OvalX;
        var dz = z - OvalZ;
        double dOut, f;
        double deg;
        if (Math.Abs(dx) <= OvalL)
        {
            dOut = Math.Abs(dz) - OvalR;
            f = 0;
            deg = 0;
        }
        else
        {
            var vx = Math.Abs(dx) - OvalL;
            var r = Math.Sqrt(vx * vx + dz * dz);
            dOut = r - OvalR;
            var cosT = r < 1e-6 ? 1 : vx / r; // 1 at the turn's apex, 0 where it meets the straight
            var t = Math.Clamp(cosT / 0.8, 0, 1);
            f = t * t * (3 - 2 * t);
            deg = dx > 0 ? BankEast : BankWest;
        }
        var top = 2 * OvalHw * Math.Tan(deg * Math.PI / 180) * f;
        if (dOut <= -OvalHw)
            return 0;
        if (dOut <= OvalHw)
            return top * (dOut + OvalHw) / (2 * OvalHw);
        var u = Math.Clamp((dOut - OvalHw) / OvalBerm, 0, 1);
        return top * (1 - u * u * (3 - 2 * u));
    }

    // ---- hub ----------------------------------------------------------------------------------------------------------

    static readonly string[] RackParts =
    [
        "Frame Block", "Frame Beam", "Panel", "Corner", "Deck Plate", "Seat", "Engine", "Fuel Tank", "Fuel Line", "Transmission", "Differential", "Suspension", "Steering Pivot", "Wheel",
        "Hinge", "Motor Hinge", "Turntable", "Piston", "Door", "Foundation",
        "Button", "Lever", "Switch", "Pressure Plate", "Input Reader", "Speed Sensor", "Proximity Sensor", "AND Gate", "OR Gate", "NOT Gate", "Timer", "Delay", "Toggle", "Compare", "Scale", "Lamp", "Horn", "Brake",
        "Battery", "Alternator", "Tow Hitch", "Tow Eye", "Chain Hook", "Wrecking Ball", "Bumper", "Fender", "Speed Gauge", "Fuel Gauge", "Charge Gauge", "Gear Selector",
        "Rifle Receiver", "Heavy Receiver", "Shell Receiver", "Gun Barrel", "Long Gun Barrel", "Twin Splitter", "Box Magazine", "Belt Feed", "Shell Feed",
        "FMJ Rounds", "AP Rounds", "HE Rounds", "Incendiary Rounds", "Gun Muzzle Brake", "Gun Suppressor", "Gun Scope",
        "Mounted MG", "Autocannon", "Cannon", "Rocket Pod", "Flamethrower", "Ammo Box", "Ammo Crate", "Feed Chute", "Wood Panel",
    ];

    const double RackX = -176, RackZ = -137;

    static JObject Sandbox() => new(
        ("rack", JArray.Of(RackX, RackZ, 180)),
        ("rackParts", new JArray(RackParts.Select(n => (JNode)new JString(n)))),
        ("constructs", new JArray(
        [
            C("Gun Bench", -156, -138), C("Cargo Rack", -142, -138), C("Chest", -190 + 6, -150), C("Storage Corner", -184, -166),
            C("Ammo Dump", -184, -182), C("Lamp Post", -170, -184), C("Lamp Post", -150, -184),
            C("Buggy", -142, -152), C("Racer", -142, -164), C("4x4 Truck", -146, -178), C("Trailer", -166, -178),
        ])),
        ("loose", new JArray(
        [
            L("Wheel", -170, -150), L("Wheel", -168, -151), L("Frame Beam", -172, -156, 90), L("Engine", -165, -154), L("Fuel Tank", -163, -150),
        ])));

    static JNode C(string blueprint, double x, double z, double yaw = 0) => new JObject(("blueprint", blueprint), ("at", P(x, z)), ("yaw", yaw));

    static JNode L(string part, double x, double z, double yaw = 0)
    {
        var o = new JObject(("part", part), ("at", P(x, z)));
        if (yaw != 0)
            o.Add("yaw", yaw);
        return o;
    }

    void Hub(Kit k)
    {
        // The parts rack stand: a slab, a backboard and two bands (the yard's).
        const double x = RackX, z = RackZ;
        k.Block(x - 4, 0, z - 3, x + 4, 0.15, z + 3, "#5f6a74");
        k.Block(x - 4, 0.15, z + 2.4, x + 4, 2.6, z + 3, "#4b5560");
        k.Block(x - 4, 1.1, z + 1.4, x + 4, 1.2, z + 2.45, "#c7a23a");
        k.Block(x - 4, 2.0, z + 1.4, x + 4, 2.1, z + 2.45, "#c7a23a");
        // Marker pillars on the pad's corners (so the open middle reads as one build area).
        foreach (var (px, pz) in new[] { (HubX - HubHalf + 3, HubZ - HubHalf + 3), (HubX + HubHalf - 3, HubZ - HubHalf + 3), (HubX - HubHalf + 3, HubZ + HubHalf - 3), (HubX + HubHalf - 3, HubZ + HubHalf - 3) })
            k.Block(px - 0.3, 0, pz - 0.3, px + 0.3, 3, pz + 0.3, Yellow);
        _signs.Add(("Hub", HubX - 8, HubZ - 8, HubX + 8, HubZ + 8, "PROVING GROUND: build area. The drag strip starts east; the sand run-off and ramps are signed."));
        _signs.Add(("Rack", RackX - 4, RackZ - 3, RackX + 4, RackZ + 3, "Parts rack: take any part here."));
    }

    // ---- drag strip ---------------------------------------------------------------------------------------------------

    void DragStrip(Kit k)
    {
        const double x0 = StripX0;
        const double hw = StripW / 2;
        // Start and finish lines (flat paint lips, 2 cm, with the lane's centre dashes between marks).
        k.Block(x0, 0, StripZ - hw, x0 + 1.0, 0.02, StripZ + hw, Paint);
        k.Block(x0 + StripLen - 1.0, 0, StripZ - hw, x0 + StripLen, 0.02, StripZ + hw, "#222226");
        for (var m = 50; m < StripLen; m += 50)
        {
            var x = x0 + m;
            k.Block(x - 0.2, 0, StripZ - hw, x + 0.2, 0.02, StripZ + hw, Paint);
            // A marker pillar each side, taller each 50 m.
            var h = 1.5 + m / 50 * 0.5;
            k.Block(x - 0.3, 0, StripZ - hw - 2.6, x + 0.3, h, StripZ - hw - 2.0, m % 100 == 0 ? Red : Yellow);
            k.Block(x - 0.3, 0, StripZ + hw + 2.0, x + 0.3, h, StripZ + hw + 2.6, m % 100 == 0 ? Red : Yellow);
        }
        // Speed-trap gates at 100, 200 and 300 m: a beam over the lane on two posts.
        foreach (var m in new[] { 100, 200, 300 })
        {
            var x = x0 + (m == 300 ? m - 3 : m);
            k.Block(x - 0.3, 0, StripZ - hw - 0.9, x + 0.3, 5.4, StripZ - hw - 0.3, Blue);
            k.Block(x - 0.3, 0, StripZ + hw + 0.3, x + 0.3, 5.4, StripZ + hw + 0.9, Blue);
            k.Block(x - 0.4, 5.0, StripZ - hw - 0.9, x + 0.4, 5.4, StripZ + hw + 0.9, Yellow);
            _signs.Add(($"Trap{m}", x - 1.5, StripZ - hw, x + 1.5, StripZ + hw, $"SPEED TRAP at {m} m: read your Speed Gauge."));
        }
        _signs.Add(("DragStart", x0 - 3, StripZ - hw, x0 + 3, StripZ + hw, "DRAG STRIP: start line. 300 m, a mark every 50 m."));
        foreach (var m in new[] { 50, 150, 250 })
            _signs.Add(($"Drag{m}", x0 + m - 1.5, StripZ - hw, x0 + m + 1.5, StripZ + hw, $"{m} m"));
        _signs.Add(("DragEnd", x0 + StripLen - 3, StripZ - hw, x0 + StripLen + 1, StripZ + hw, "FINISH. 300 m. Sand run-off ahead."));
        // Sand-trap kerb at the very end.
        k.Block(x0 + StripLen + 16, 0, StripZ - hw - 2, x0 + StripLen + 17, 0.8, StripZ + hw + 2, Stone);
    }

    // ---- oval ---------------------------------------------------------------------------------------------------------

    void Oval(Kit k)
    {
        // Start/finish line across the south straight and a pillar at each turn's apex with its bank angle.
        k.Block(OvalX - 0.5, 0, OvalZ - OvalR - OvalHw, OvalX + 0.5, 0.02, OvalZ - OvalR + OvalHw, Paint);
        k.Block(OvalX + OvalL + OvalR + OvalHw + 1, 0, OvalZ - 0.3, OvalX + OvalL + OvalR + OvalHw + 1.6, 4, OvalZ + 0.3, Red);
        k.Block(OvalX - OvalL - OvalR - OvalHw - 1.6, 0, OvalZ - 0.3, OvalX - OvalL - OvalR - OvalHw - 1, 4, OvalZ + 0.3, Red);
        _signs.Add(("OvalStart", OvalX - 3, OvalZ - OvalR - OvalHw, OvalX + 3, OvalZ - OvalR + OvalHw, "OVAL: start/finish. Turns are banked 15 degrees (east) and 25 degrees (west)."));
        _signs.Add(("BankEast", OvalX + OvalL + OvalR - 3, OvalZ - 3, OvalX + OvalL + OvalR + 3, OvalZ + 3, "East turn: banked 15 degrees."));
        _signs.Add(("BankWest", OvalX - OvalL - OvalR - 3, OvalZ - 3, OvalX - OvalL - OvalR + 3, OvalZ + 3, "West turn: banked 25 degrees."));
    }

    // ---- slalom and narrow gates ---------------------------------------------------------------------------------------

    const double SlalomZ = -122;

    void Slalom(Kit k)
    {
        // A wide slalom (9 m between cones), then a tight one (6 m), cones alternating either side of the line.
        for (var i = 0; i < 12; i++)
            k.Prop("Traffic Cone", -110 + i * 9, SlalomZ + (i % 2 == 0 ? 2.6 : -2.6));
        for (var i = 0; i < 10; i++)
            k.Prop("Traffic Cone", 4 + i * 6, SlalomZ + (i % 2 == 0 ? 2.2 : -2.2) + 22);
        // Narrow gates: 2.5, 3 and 4 m between the posts, in a row across the lane to drive through one by one.
        var gx = 60.0;
        foreach (var w in new[] { 2.5, 3.0, 4.0 })
        {
            k.Block(gx - 0.3, 0, SlalomZ - w / 2 - 0.6, gx + 0.3, 2.4, SlalomZ - w / 2, Red);
            k.Block(gx - 0.3, 0, SlalomZ + w / 2, gx + 0.3, 2.4, SlalomZ + w / 2 + 0.6, Red);
            _signs.Add(($"Gate{(int)(w * 10)}", gx - 1.5, SlalomZ - w / 2, gx + 1.5, SlalomZ + w / 2, $"Gate {w:0.0} m wide."));
            gx += 14;
        }
        _signs.Add(("Slalom", -112, SlalomZ - 5, -104, SlalomZ + 5, "SLALOM: 9 m cones, then a tight one at 6 m. Gates after: 2.5, 3 and 4 m."));
    }

    // ---- hill climb ----------------------------------------------------------------------------------------------------

    void HillClimb(Kit k)
    {
        for (var i = 0; i < ClimbX.Length; i++)
        {
            var x = ClimbX[i];
            // A pillar at the foot with one band per ten degrees (10 = 1 band ... 40 = 4) and a sign zone.
            k.Block(x - 7.2, 0, ClimbZ0 - 3.3, x - 6.6, 4.5, ClimbZ0 - 2.7, Yellow);
            for (var b = 0; b <= i; b++)
                k.Block(x - 7.25, 0.4 + b * 0.8, ClimbZ0 - 3.35, x - 6.55, 0.9 + b * 0.8, ClimbZ0 - 2.65, Red);
            _signs.Add(($"Climb{(int)ClimbDeg[i]}", x - ClimbW / 2, ClimbZ0 - 4, x + ClimbW / 2, ClimbZ0 + 1, $"HILL CLIMB {ClimbDeg[i]:0} degrees, up to a 10 m deck."));
        }
    }

    // ---- jumps ---------------------------------------------------------------------------------------------------------

    void Jumps(Kit k)
    {
        var names = new[] { "Small kicker", "Medium ramp", "Big ramp", "Huge ramp" };
        for (var i = 0; i < JumpSpecs.Length; i++)
        {
            var (x, h, up, gap, down) = JumpSpecs[i];
            k.Block(x - 7.2, 0, JumpZ0 - 3.3, x - 6.6, 3.5, JumpZ0 - 2.7, Blue);
            _signs.Add(($"Jump{i + 1}", x - 6, JumpZ0 - 4, x + 6, JumpZ0 + 1, $"JUMP {i + 1}: {names[i]}, {h:0.#} m lip, {gap:0} m gap, a {down:0} m landing."));
        }
    }

    // ---- rough ground --------------------------------------------------------------------------------------------------

    void Rough(Kit k)
    {
        // Rocks in the rock garden (solid boulders) and signs at each patch.
        var rng = new Rng(23);
        for (var i = 0; i < 7; i++)
            k.Rocks(-186 + rng.Next() * 50, 120 + rng.Next() * 26, 1.6, "#7b7770", 40 + i);
        _signs.Add(("Bumps", -180, 40, -150, 70, "BUMPS: rough ground."));
        _signs.Add(("Craters", -156, 14, -124, 70, "CRATER FIELD: shallow craters, 4 to 9 m across."));
        _signs.Add(("Washboard", -190, 92, -122, 106, "WASHBOARD: 3.6 m ridges."));
        _signs.Add(("RockGarden", -188, 116, -132, 150, "ROCK GARDEN: lumps and boulders."));
        _signs.Add(("Trench", -190, 164, -100, 172, "TRENCH: 6 m wide, 2 m deep."));
        _signs.Add(("Trench2", -190, 180, -100, 184, "Narrow trench: 3 m wide, 1.2 m deep."));
        k.Block(-186, 0, 88, -185.4, 3, 88.6, Yellow);
        k.Block(-186, 0, 162, -185.4, 3, 162.6, Yellow);
    }

    // ---- gully, bridge, deck -------------------------------------------------------------------------------------------

    void Gully(Kit k)
    {
        // The bridge: a slab 6 m wide over the gully (z 116 to 144), with kerbs.
        const double bx = -50;
        k.Block(bx - 3, -0.44, 114, bx + 3, 0.06, 146, "#6b6f74", anchor: "world");
        k.Block(bx - 3, 0.06, 114, bx - 2.6, 0.5, 146, "#8a8d92", anchor: "world");
        k.Block(bx + 2.6, 0.06, 114, bx + 3, 0.5, 146, "#8a8d92", anchor: "world");
        // Piers.
        k.Block(bx - 0.6, -6.5, 129.4, bx + 0.6, -0.44, 130.6, "#5d6065", anchor: "world");
        _signs.Add(("Bridge", bx - 3, 112, bx + 3, 118, "BRIDGE: 6 m wide over a 6 m gully."));
        _signs.Add(("Gully", -96, 120, -4, 125, "GULLY: 22 m wide, 6 m deep. Cross by the bridge."));
        _signs.Add(("Deck", DeckX - 8, DeckZ - 8, DeckX + 8, DeckZ + 8, "RAISED DECK: 3 m up, reached by the narrow ramp."));
        _signs.Add(("DeckRamp", DeckX - 1.5, DeckZ - 26, DeckX + 1.5, DeckZ - 22, "NARROW RAMP: 2.5 m wide."));
    }

    // ---- tow lane ------------------------------------------------------------------------------------------------------

    void Tow(Kit k)
    {
        k.Block(TowX - 9.6, 0, TowZ0 - 1, TowX - 9, 3.5, TowZ0 - 0.4, Yellow);
        foreach (var d in new[] { 40, 80, 120 })
            k.Block(TowX - 9.6, 0, TowZ0 + d, TowX - 9, 1.5, TowZ0 + d + 0.6, Paint, anchor: "ground");
        _signs.Add(("TowFoot", TowX - 8, TowZ0 - 4, TowX + 8, TowZ0 + 2, "TOW GRADE: 130 m long, rising 10 m (about 4.4 degrees)."));
        _signs.Add(("TowTop", TowX - 8, TowZ0 + TowLen, TowX + 8, TowZ0 + TowLen + 6, "Top of the tow grade: 10 m up."));
    }

    // ---- crash pad -----------------------------------------------------------------------------------------------------

    void CrashPad(Kit k)
    {
        // The wall to drive into on the pad's east side: 40 m long, 5 m high, 3 m thick; a low kerb line and steps to climb.
        k.Block(PadX + 34, 0, PadZ - 20, PadX + 37, 5, PadZ + 20, "#7e7d79");
        k.Block(PadX + 33.7, 4.6, PadZ - 20.3, PadX + 34.05, 5.0, PadZ + 20.3, Yellow);
        for (var i = 0; i < 4; i++) // kerbs of 0.3, 0.6, 0.9, 1.2 m to test climbing
            k.Block(PadX - 36 + i * 4.4, 0, PadZ + 26.5, PadX - 36 + i * 4.4 + 2, 0.3 * (i + 1), PadZ + 29, Stone);
        for (var i = 0; i < 5; i++) // jersey blocks as pushable-looking obstacles
            k.Prop("Road Barrier", PadX - 20 + i * 4.5, PadZ - 26, yaw: 90);
        _signs.Add(("Pad", PadX - 30, PadZ - 25, PadX + 30, PadZ + 25, "CRASH PAD: open flat ground for big builds. The wall to the east is solid."));
        _signs.Add(("Wall", PadX + 28, PadZ - 20, PadX + 34, PadZ + 20, "CRASH WALL: 5 m high, 3 m thick."));
        _signs.Add(("Kerbs", PadX - 37, PadZ + 25, PadX - 18, PadZ + 30, "KERBS: 0.3, 0.6, 0.9 and 1.2 m."));
    }

    // ---- signs ---------------------------------------------------------------------------------------------------------

    void Signs(Kit k)
    {
        foreach (var (name, x0, z0, x1, z1, text) in _signs)
        {
            k.Logic.Zone("Z" + name, Math.Min(x0, x1), -10, Math.Min(z0, z1), Math.Max(x0, x1), 40, Math.Max(z0, z1), "humans");
            k.Logic.Text("T" + name, text, toActivator: true);
            k.Logic.Wire("Z" + name + ".entered", "T" + name + ".show");
        }
    }

    // ---- test probes ---------------------------------------------------------------------------------------------------

    static IEnumerable<JNode> Probes()
    {
        yield return JArray.Of("hub", HubX, HubZ, 0);
        yield return JArray.Of("drag start", StripX0 + 2, StripZ, 0);
        yield return JArray.Of("drag 150 m", StripX0 + 150, StripZ, 0);
        yield return JArray.Of("drag finish", StripX0 + StripLen - 2, StripZ, 0);
        yield return JArray.Of("crash pad", PadX, PadZ, 0);
        yield return JArray.Of("tow top", TowX, TowZ0 + TowLen, TowRise);
        for (var i = 0; i < ClimbX.Length; i++)
        {
            var len = ClimbTop / Math.Tan(ClimbDeg[i] * Math.PI / 180);
            yield return JArray.Of($"climb {ClimbDeg[i]:0} deg mid", ClimbX[i], Math.Round(ClimbZ0 + len / 2, 1), Math.Round(ClimbTop / 2, 2));
        }
    }
}
