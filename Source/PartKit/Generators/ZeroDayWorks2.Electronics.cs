using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The materials and lines Works II lacked for the frigate's fittings (Sean, 2026-11-28: "add them on the free floor"): nothing made
/// bronze (Bearings), cloth (Tyre Carcasses), glass or laminate (every Circuit Board, Button, Bakelite Switch, OR Gate and Input Reader is
/// pressed from a laminate blank), and every Extruder was fed steel, so no copper rod (Wiring Loom, Copper Coil) or laminate plate was made.
/// All of it is added beside Sean's pieces on free floor; none of his move.
/// <list type="bullet">
/// <item>Each back row grows outward past its Sawmill (layout |x| 64 to 88): an Ore Silo of sand into a Smelter (glass), a Foundry on
/// planks and glass (laminate), a Foundry on copper and metal (bronze) and a Sawmill on planks (cloth), each into a Materials Bin in the
/// bins' row (|x| 61 to 76). Every Assembler is chuted from the glass, bronze and cloth bins too.</item>
/// <item>Each small press line gets a one-die Extruder east (west) of its turntables (|x| 66, flowing north), chuted straight from the back
/// row's laminate and copper bins: it makes either in turn, handing what it holds back to its bin when the next order wants the other
/// (<see cref="ConstructExtruder.HandBack"/>). Its Run-out Table ends on a tile into a Turntable Tile at the head of a row along z 18, and a conveyor
/// from that row's end goes into the line's entry turntable from the north, as the second blank Extruder's comes in from the east
/// (<see cref="Script.JoinSecondRun"/>). Its Run comes from the press line's console.</item>
/// </list>
/// </summary>
public sealed partial class ZeroDayWorks2
{
    /// <summary>
    /// The electronics Extruder's die cell's layout |x|: even, on the slabs' 2 m lattice, so nothing is welded freeform. Its Core stands
    /// 2 m to the site's -x of its die on both sides (its dies weld on its own +x): east between the second blank Extruder's tiles and the
    /// die, west between the die and the laser beds. Two Extruders don't fit there beside the second blank Extruder's tables, so one makes
    /// both materials in turn (<see cref="ConstructExtruder.HandBack"/>).
    /// </summary>
    private const float ElecX = 66;

    // (the Extruder's layout z, and the row its pieces leave by: the die cell stands 6.75 m up the flow, on the slabs' lattice, its table
    // meets a tile 9.75 m down it, and a Turntable Tile past that turns them onto the row; it ends short of the Medium Dock Expansion at |x| 70)
    private const float ElecZ = 23, ElecRowZ = 18;

    /// <summary>The new back-row refiners (layout |x|, recipe) and their bins (|x|, material): glass, laminate, bronze, cloth.</summary>
    private const float GlassSmelter = 64, LamFoundry = 72, BronzeFoundry = 80, ClothSawmill = 88;

    private const float GlassBin = 61, LamBin = 66, BronzeBin = 71, ClothBin = 76;

    private static IEnumerable<string> ElectronicsAdditions() => new[] { 1, -1 }.SelectMany(s =>
    {
        int west = s > 0 ? 1 : 3; // (a lab rot toward the turntables: 1 -x, 3 +x)
        const float ex = ElecX;
        string[] all =
        [
            // The back row's outer end.
            $$"""{"t": "part", "name": "Ore Silo 4 m", "x": {{s * GlassSmelter}}, "z": -133, "rot": 0}""",
            $$"""{"t": "part", "name": "Smelter", "x": {{s * GlassSmelter}}, "z": -122, "rot": 2}""",
            $$"""{"t": "part", "name": "Foundry", "x": {{s * LamFoundry}}, "z": -122, "rot": 2}""",
            $$"""{"t": "part", "name": "Foundry", "x": {{s * BronzeFoundry}}, "z": -122, "rot": 2}""",
            $$"""{"t": "part", "name": "Sawmill", "x": {{s * ClothSawmill}}, "z": -122, "rot": 2}""",
            $$"""{"t": "part", "name": "Materials Bin", "x": {{s * GlassBin}}, "z": -114, "rot": 2}""",
            $$"""{"t": "part", "name": "Materials Bin", "x": {{s * LamBin}}, "z": -114, "rot": 2}""",
            $$"""{"t": "part", "name": "Materials Bin", "x": {{s * BronzeBin}}, "z": -114, "rot": 2}""",
            $$"""{"t": "part", "name": "Materials Bin", "x": {{s * ClothBin}}, "z": -114, "rot": 2}""",
            // The small press line's electronics Extruder and the row its tables end on (the tiles first, so a table welds onto its tile).
            $$"""{"t": "part", "name": "Roller Tile", "x": {{s * ex}}, "z": {{ElecRowZ + 2}}, "rot": 2}""", // (a table welds onto a plain tile, not a turntable)
            $$"""{"t": "part", "name": "Roller Tile", "x": {{s * ex}}, "z": {{ElecRowZ}}, "rot": {{west}}, "swivel": true}""",
            .. Enumerable.Range(1, 4).Select(k => $$"""{"t": "part", "name": "Roller Tile", "x": {{s * (ex - 2 * k)}}, "z": {{ElecRowZ}}, "rot": {{west}}}"""),
            $$"""{"t": "lab", "id": "extruder", "w": 1, "l": 2, "x": {{s * ex}}, "z": {{ElecZ}}, "rot": 2, "mirror": false}""",
        ];
        return all;
    });

    private sealed partial class Script
    {
        /// <summary>Is the Extruder at site <paramref name="at"/> one of the electronics Extruder (<see cref="ElecX"/>)?</summary>
        private static bool IsElectronics(Vector2 at) => MathF.Abs(at.Y + ElecZ) < 0.5f && MathF.Abs(MathF.Abs(at.X) - ElecX) < 0.5f;

        /// <summary>The back row's new refiners: their feeds and their bins (after <see cref="BackRow"/>, whose bins they draw on).</summary>
        private void BackRowElectronics(float s)
        {
            VPart? R(string name, float x) => Find(name, P(s, x, 122), 3);
            VPart? B(float x) => Find("Materials Bin", P(s, x, 114));
            var (glass, lam, bronze, cloth) = (R("Smelter", GlassSmelter), R("Foundry", LamFoundry), R("Foundry", BronzeFoundry), R("Sawmill", ClothSawmill));
            Config(lam, ("recipe", 4)); // (planks + glass: laminate)
            Config(bronze, ("recipe", 6)); // (copper + metal: bronze)
            var silo = Find("Ore Silo 4 m", P(s, GlassSmelter, 133));
            Fill(silo, "sand");
            var h = 9f;
            Chute(silo, "Chute out", glass, "Chute in", 14f); // (over the silo's top)
            foreach (var (from, to) in new[] { (glass, B(GlassBin)), (lam, B(LamBin)), (bronze, B(BronzeBin)), (cloth, B(ClothBin)) })
                Chute(from, "Chute out", to, null, h += 0.5f);
            Chute(B(GlassBin), null, lam, "Chute in", h += 0.5f); // (the laminate Foundry's glass)
            Chute(B(51), null, lam, "Chute in", h += 0.5f); // (its planks, from the Sawmill's bin)
            Chute(B(26), null, bronze, "Chute in", h += 0.5f); // (the bronze Foundry's copper)
            Chute(B(16), null, bronze, "Chute in", h += 0.5f); // (its metal, from the Smelter's bin: the reserve bin beside it is drained by the steel Foundry)
            Chute(B(51), null, cloth, "Chute in", h += 0.5f); // (the cloth Sawmill's planks)
        }

        /// <summary>
        /// The small press line's electronics Extruders: each bin from its back-row bin, into its Extruder; each Run from the press line's
        /// console; and the conveyor from their joined run's end into the line's entry turntable (<see cref="JoinSecondRun"/>'s, from the north).
        /// </summary>
        private void LinesElectronics(float s, VPart? console, ref float high)
        {
            const float ex = ElecX;
            var core = NearestLab("extruder", P(s, ex, -ElecZ), 12);
            if (core == null)
            {
                Note($"electronics: no Extruder at ({s * ex:0}, {-ElecZ:0})");
                _linkMissed++;
                return;
            }
            // (straight from the back row's bins: a bin of its own beside it would stand in the second blank Extruder's way, FindShifts)
            foreach (var from in new[] { LamBin, 26f })
            {
                Chute(Find("Materials Bin", P(s, from, 114)), null, core, "Chute in", high);
                high += 0.5f;
            }
            if (console != null)
                Cable(console, core, "Run", 6.5f);
            Note($"electronics: the Extruder at ({s * ex:0}, {-ElecZ:0}), fed laminate and copper from the back row's bins");
            var end = Find("Roller Tile", P(s, ex - 8, -ElecRowZ), 0.5f);
            var entry = Find("Roller Tile", P(s, 53, -32), 0.5f);
            if (end == null || entry == null || !entry.Def.IsTurntable)
            {
                Note($"electronics run on side {s}: no run end or entry turntable");
                _linkMissed++;
                return;
            }
            try
            {
                var (a, fa) = PortOf(end, "Piece out");
                var toward = new Vector3(0, 0, 1); // (from the entry turntable out toward the row on z 18: the layout's north)
                var port = entry.Def.NamedPorts.Where(np => np.Name.StartsWith("In ", StringComparison.Ordinal))
                    .OrderByDescending(np => Vector3.Dot(PortOf(entry, np.Name).Face, toward)).First().Name;
                var (b, fb) = PortOf(entry, port);
                Vector3 a1 = a + fa, b1 = b + fb;
                Link("conveyor", end, entry, "Piece out", port, Line, false, null, Cut(a, a1, V(b1.X, a1.Y, a1.Z), b1));
                _links++;
                Note($"electronics run on side {s}: a conveyor from its end at ({a.X:0.#}, {a.Z:0.#}) into the turntable's {port} at ({b.X:0.#}, {b.Z:0.#})");
            }
            catch (VehicleKitException e)
            {
                Note($"electronics run on side {s}: the conveyor not laid: {e.Message}");
                _linkMissed++;
            }
        }
    }
}
