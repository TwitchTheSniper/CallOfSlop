using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Starter Works (retiring the Assembler, phase 6, 2026-11-28): a survival world's first kit line on one floor, so a fresh Survival
/// game can make the placed parts an Assembler no longer builds (an Engine, a Small Jet, a Chassis Tub, a Bolt Deck: --station-check). Three
/// Presses south of the pick pad (Piston, Cable and Ring Dies fitted) press the kits' Piston Sets, Wiring Looms, Bearings and Turbine Stages. An Extruder with a Die Block (a 2 x 2 m die) fed by two Materials Bins (steel, and a kit's rubber, concrete or cloth), its pieces on a conveyor onto a Pad
/// Tile pick pad beside a Building Station's 4 x 3 bay (8 x 6 m), a Roller Tile run from the bay's far end into a Bake Oven's 4 x 3 bed,
/// and a Small Genset on its fuel tanks with a Battery. An order at the oven (its screen, or a terminal) asks the station for the part's
/// kit, the station asks the Extruder for its pieces, the rover fits them, the kit rolls out into the oven and bakes. Built through the
/// game's own rules (<see cref="BlueprintScript"/>), so a step they refuse fails here with their reason. In Constructs/SurvivalStart.json.
/// </summary>
public sealed class StarterWorks : IPartGenerator
{
    public string Name => "starter_works";

    public const string File = "Constructs/Blueprints/StarterWorks.json";

    public void Make(PartOutput o) => o.File(File, Script.Build().Text());

    private sealed class Script : BlueprintScript
    {
        private Script() : base("Starter Works")
        {
        }

        private const float FloorY = 0.3f; // the Foundations' tops
        private const int TilesX = 15, TilesZ = 11; // 2 m Foundations: 30 by 22 m
        private const float Line = -7.25f; // the kit line's middle (z): the station's dock, its bay's middle row, the run, the oven's bed
        private const string Floor = "#8c8576", Power = "#b5452b", Stock = "#c9a24a", Kit = "#3d7ab8", Hazard = "#d8a020";

        private static readonly Orient Front = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ);
        private static readonly Orient Flow = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX); // own +z along +x
        private static readonly Orient Toward = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX); // own +z along -x

        public static Script Build()
        {
            var s = new Script
            {
                Description = "A survival world's first kit line, 30 x 22 m: an Extruder fed by a steel and a rubber bin, its pieces onto a pick pad beside a Building Station's 8 x 6 m bay, a roller run from the bay into a Bake Oven's 8 x 6 m bed, three Presses (Piston, Cable and Ring Dies fitted), each fed by its own Extruder, their parts onto the pad, and a Small Genset with a Battery. Order a part at the oven: the station fits its kit from the pieces and pressed parts and rolls it into the oven, which bakes it into the part. Tip steel and rubber into the two bins by the Extruder, bronze into the bin by the Ring press and the one at the back, copper into the other one at the back.",
            };
            s.Group = "Production";
            s.Lay();
            return s;
        }

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        private VPart On(string part, float x, float z, Orient o, string by = "bottom", string? paint = null)
        {
            var p = Stand(part, V(x, FloorY, z), o, null, by);
            if (paint != null)
                Paint(p, paint);
            return p;
        }

        /// <summary>A named port's place on a part (vehicle coordinates) and the way it faces.</summary>
        private (Vector3 At, Vector3 Face) PortOf(VPart p, string name)
        {
            var np = p.Def.NamedPorts.FirstOrDefault(x => x.Name == name) ?? throw new VehicleKitException($"a {p.Name} has no port \"{name}\"");
            ref var cp = ref Live.Parts[p.Index];
            return (FromRest(cp.Pos + Rot24.Apply(cp.Rot, np.At)), Rot24.Apply(cp.Rot, ConstructParts.DirVector(np.Face)));
        }

        private const float ChuteUp = 6.6f; // (over the Extruders' hoppers, 5.9 m)
        private const float SegmentMax = 7.5f; // (a chute's segment reaches at most 8 m)

        /// <summary>A chute from a bin's top, up over the floor's machines, along x, then z, and down to <paramref name="end"/> beside the part it feeds.</summary>
        private void Chute(VPart bin, VPart to, Vector3 end)
        {
            var a = FromRest(Live.Parts[bin.Index].Pos) + V(0, 0.6f, 0); // (a bin has no named port: its chute leaves its top)
            var a1 = a + V(0, 0.6f, 0);
            var pts = new[] { a1, V(a1.X, ChuteUp, a1.Z), V(end.X, ChuteUp, a1.Z), V(end.X, ChuteUp, end.Z), end };
            var nodes = new List<Vector3>();
            var last = a;
            foreach (var q in pts)
            {
                if (Vector3.Distance(q, last) < 0.05f)
                    continue;
                var n = (int)MathF.Ceiling(Vector3.Distance(q, last) / SegmentMax);
                for (var k = 1; k <= n; k++)
                    nodes.Add(Vector3.Lerp(last, q, k / (float)n));
                last = q;
            }
            Link("chute", bin, to, null, null, Stock, false, null, nodes.ToArray());
        }

        /// <summary>A data wire from the Line Console into a machine's Run (or the station's Orders), by a node 3 m up halfway when it is longer than a segment.</summary>
        private void Wire(VPart console, VPart to, string port = "Run")
        {
            var a = FromRest(Live.Parts[console.Index].Pos);
            var b = FromRest(Live.Parts[to.Index].Pos);
            if (Vector3.Distance(a, b) < SegmentMax)
                Link("data", console, to, null, port);
            else
                Link("data", console, to, null, port, null, false, V((a.X + b.X) * 0.5f, 3f, (a.Z + b.Z) * 0.5f));
        }

        private void Lay()
        {
            for (var j = 0; j < TilesZ; j++)
                for (var i = 0; i < TilesX; i++)
                {
                    var at = V(1 + 2 * i, 0.15f, -1 - 2 * j);
                    var t = i == 0 && j == 0 ? Root("Foundation", at, Front) : Put("Foundation", at, Front);
                    Paint(t, Floor);
                    Finish(t, "concrete");
                }

            // Power: a Small Genset on two fuel tanks and a Battery, in the near corner.
            var genset = On("Small Genset", 2.25f, -1.25f, Front, "-y", Power);
            var tank = Onto(genset, "+x", "Fuel Tank", "-x");
            Paint(tank, Power);
            Paint(Onto(tank, "+x", "Fuel Tank", "-x"), Power);
            On("Battery", 6.25f, -1.25f, Front, "-y", Power);

            // The Building Station: its dock pad x 1 to 5, its 4 x 3 bay of Bay Blocks out of its mouth (x 5 to 13, z -10 to -4).
            var station = On("Building Station", 4.25f, Line, Front, "-y.0.0");
            var row = new VPart[4];
            row[0] = Onto(station, "+x.0", "Bay Block", "-x");
            for (var k = 1; k < 4; k++)
                row[k] = Onto(row[k - 1], "+x", "Bay Block", "-x");
            foreach (var b in row)
            {
                Onto(b, "+z", "Bay Block", "-z");
                Onto(b, "-z", "Bay Block", "+z");
            }

            // Its pick pad: two Pad Tiles past the bay's -z side (z -12 to -10), within its span along x.
            var pad = new VPart[2];
            for (var k = 0; k < 2; k++)
                pad[k] = On("Pad Tile", 8.25f + 2 * k, -11.25f, Front, "bottom", Hazard);

            // The Extruder, fed steel from a bin, its pieces east along z -11 onto the pad's -x side.
            var ex = On("Extruder", 3.25f, -11.25f, Flow, "bottom", Kit);
            Paint(Onto(ex, "+x", "Die Block", "-x"), Kit); // (a 2 x 2 m die beside the Core's own 0.5 m one: a kit's plates and sheets)
            var bin = On("Materials Bin", 0.75f, -11.25f, Front, "bottom", Stock);
            Link("chute", bin, ex, null, null, Stock);
            var bin2 = On("Materials Bin", 0.75f, -9.25f, Front, "bottom", Stock); // (a second material: a kit's rubber, concrete or cloth sheet)
            Link("chute", bin2, ex, null, null, Stock);
            Link("conveyor", ex, pad[0], "Piece out", "Piece in -x", Kit, false);
            // A Line Console with the Extruder on its line, wired into the station's Orders: the station's kit pieces are the line's orders.
            var console = On("Line Console", 5.25f, -15.75f, Front); // (south of the Die Block's run: a kit's long pieces grow east from its die along z -13.25)
            Wire(console, ex);
            Wire(console, station, "Orders");

            // The press row south of the pad (z -16.5 to -14.3): three Press Cores, each with its die fitted and its own Extruder south of
            // it (die facing north into bay 1), each press's Piece out north onto the pick pad. The Piston Die (Piston Sets: an Engine's kit),
            // the Cable Die (Wiring Looms: copper rounds with a rubber kit) and the Ring Die (Bearings from bronze rounds, Turbine Stages
            // from steel ones with a bronze kit: a Small Jet's). Bay 2's kits come by chute from a bronze bin of their own and the rubber bin.
            var bronze = On("Materials Bin", 4.75f, -20.75f, Front, "bottom", Stock); // (the Ring press's Bearing blanks)
            var kitBronze = On("Materials Bin", 15.75f, -15.25f, Front, "bottom", Stock); // (bay 2's bronze on its own bin: an Extruder's hopper draws a shared bin dry)
            var copper = On("Materials Bin", 16.75f, -20.75f, Front, "bottom", Stock);
            var presses = new (string Die, float X, VPart Pad, string Port, VPart[] Blanks, VPart? Kit)[]
            {
                ("Piston Die", 7.25f, pad[0], "Piece in", [bin], kitBronze),
                ("Cable Die", 9.75f, pad[1], "Piece in", [copper], bin2),
                ("Ring Die", 12.75f, pad[1], "Piece in +x", [bin, bronze], kitBronze),
            };
            foreach (var (die, px, to, port, blanks, kit) in presses)
            {
                var press = On("Press", px, -16.25f, Front, "-y.0.0", Kit);
                Onto(press, "dieslot", die, "mount");
                var pex = On("Extruder", px + 1.0f, -18.25f, Front, "bottom", Kit);
                OrdersOnly(pex);
                Link("conveyor", pex, press, "Piece out", "Piece in", Kit, false);
                var (pin, pf) = PortOf(pex, "Chute in");
                foreach (var b in blanks)
                    Chute(b, pex, pin + pf * 0.6f);
                if (kit != null) // (bay 2's kit: down beside the Core's west side, in at 1.4 m)
                    Chute(kit, press, V(px - 0.35f, 1.4f, -15.4f));
                Link("conveyor", press, to, "Piece out", port, Kit, false);
                Wire(console, press); // (its Extruder, orders only, makes the blanks the press's orders ask for down its conveyor)
            }

            // The way out of the bay and into the oven: two Roller Tiles along the line (x 13 to 17).
            On("Roller Tile", 14.25f, Line, Flow, "bottom", Kit);
            On("Roller Tile", 16.25f, Line, Flow, "bottom", Kit);

            // The Bake Oven: its Core at the bed's far end (x 25 to 27), its 4 x 3 bed of Oven Bed Blocks x 17 to 25.
            var oven = On("Bake Oven", 26.25f, Line, Front, "bottom");
            var bed = new VPart[4];
            bed[0] = Onto(oven, "-x", "Oven Bed Block", "+x");
            for (var k = 1; k < 4; k++)
                bed[k] = Onto(bed[k - 1], "-x", "Oven Bed Block", "+x");
            foreach (var b in bed)
            {
                Onto(b, "+z", "Oven Bed Block", "-z");
                Onto(b, "-z", "Oven Bed Block", "+z");
            }
        }
    }
}
