using System.Numerics;
using System.Text.Json;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Retiring the Assembler, phase 5 (Sean's plan, 2026-11-28: "Zero Day Works and Works II lose their Assemblers"). Works II had three:
/// one in each press line's row between its presses and the main station (a 2 x 2 bed at layout |x| 31 to 33, z 29 to 31, its Core at
/// |x| 33, z 33), whose bed the line's pressed parts rolled through, and the big one by the main station (2026-11-26). They go, and so do
/// their chutes, Runs, modules and the big one's arm; nothing else moves.
/// <list type="bullet">
/// <item>A press line's Assembler gives way to a third press (Sean's plan: the small-parts lines get press pairs): a 2 x 3 bed of Press
/// Bed Blocks over the Assembler's bed cells and the free row beside them, and its Press Core on free floor beside it (<see
/// cref="Bridges"/>), a Ring Die in its slot (the frigate's 101 Bearings, from bronze rounds: the line's electronics Extruder is chuted from the bronze
/// bin since). The line's runs meet its bed as they met the Assembler's (a run ends on a bed and starts off one by where the bed lies,
/// MachineBlocks.BedAt, RollerRuns.Butted), so the line's pieces roll through it to the station's pad as before: a run can't end on another
/// run (only through a Turntable Tile), and the one-tile run off the second press is half a tile off the two-tile row's grid, and a
/// conveyor from its end would be shorter than the Leg Pod Housing XL's 5.65 m. Its Run comes from the main station's console.</item>
/// <item>The fittings the Assemblers built are baked from their kits (BakeKits) at the kit lines' stations (the two big stations by the
/// big ovens, the two narrow ones by the small ovens). Their kits carry rubber, concrete and cloth sheets and copper pieces, and an oven
/// takes a kit only while every piece's material reaches an Extruder its station's consoles run (ConstructExtruder.Feeds): every kit
/// line's Extruder is chuted from the back row's copper, concrete, rubber and cloth bins on its side, beside its own steel bin. It makes
/// one material at a time and hands the rest back to its bin (ConstructExtruder.HandBack).</item>
/// </list>
/// A saved works keeps its Assemblers: they still build as the fallback where no oven serves (tools/MIGRATING.md).
/// </summary>
public sealed partial class ZeroDayWorks2
{
    /// <summary>
    /// The third presses (layout x, z; east, then west): a 2 x 3 bed of Press Bed Blocks over the Assembler's bed cells and one more row,
    /// and the Press Core beside the bed's first row, its own +x into the bed along the line. A Press's bed is low (the line's runs end on
    /// it and start off it, MachineBlocks.BedAt) only with a row between its two housings' rows, so three rows across the line; and a
    /// Press Core's box stands 0.125 m off its cell, so each Core takes the one free cell beside its bed that meets nothing: east at
    /// (29, 33), the bed's rows 33 to 29 (the Assembler's Core cell among them), west at (-29, 27), its rows 27 to 31 (the free row north
    /// of the Assembler's bed). At the mirror of either the Core met the second press's Core or bed, or a line tile, by 0.13 m.
    /// </summary>
    private static readonly (int Side, (int X, int Z)[] Cells, (int X, int Z) Core, int Dir)[] Bridges =
    [
        (1, [(31, 29), (31, 31), (31, 33), (33, 29), (33, 31), (33, 33)], (29, 33), 1),
        (-1, [(-31, 27), (-31, 29), (-31, 31), (-33, 27), (-33, 29), (-33, 31)], (-29, 27), -1),
    ];

    /// <summary>The bridge presses' die: Bearings, the most wanted pressed part a line's other presses would swap their dies for.</summary>
    private const string BridgeDie = "Ring Die";

    /// <summary>The bed cells as part items, turned as the press's Core (their own +z its own +x, along the line): a bed block turned otherwise is no part of the press's bed.</summary>
    private static IEnumerable<string> AssemblerBridges() =>
        Bridges.SelectMany(b => b.Cells.Select(c => $$"""{"t": "part", "name": "Press Bed Block", "x": {{c.X}}, "z": {{c.Z}}, "rot": {{(b.Dir > 0 ? 3 : 1)}}}"""));

    private sealed partial class Script
    {
        /// <summary>A layout item that was an Assembler's (its Core and bed blocks): left out since phase 5.</summary>
        private static bool Retired(JsonElement e) => Str(e, "name") is "Assembler" or "Assembly Bed Block";

        /// <summary>The back row's bins every kit line's Extruder draws on besides its steel (layout |x|): copper, concrete, rubber, cloth.</summary>
        private static readonly float[] KitBins = [26, 41, 46, ClothBin];

        private readonly List<VPart> _bridgePresses = new();

        /// <summary>Each press line's third press: its Core in the old Assembler's Core cell, welded onto the bed cells laid as part items.</summary>
        private void BridgePresses()
        {
            foreach (var (side, _, cell, dir) in Bridges)
            {
                var s = (float)side;
                var at = new Vector2(cell.X, -cell.Z);
                var c = Piece("Press", at, XFacing(new Vector2(dir, 0)), Line, "-y.4.4", new Vector2(1, 1), chainFirst: true);
                if (c == null)
                {
                    Note($"bridge press on side {s}: its Core is NOT placed");
                    continue;
                }
                OrdersOnly(c);
                Onto(c, "dieslot", BridgeDie, "mount");
                _bridgePresses.Add(c);
                Note($"bridge press on side {s}: a Press at ({at.X:0}, {at.Y:0}) with a {BridgeDie}, its 2 x 3 bed where the line's Assembler stood");
            }
        }

        /// <summary>
        /// A chute from a far bin up to <paramref name="height"/>, over to above the Extruder's own steel bin <paramref name="via"/>, down to
        /// <paramref name="low"/> and on as that bin's own chute goes (x, then z, then down into the Chute in). A straight high drop onto the
        /// west south line's Extruder was refused ("a segment reaches at most 8 m": a node in the air is hosted by the nearest part, and one
        /// near there is welded freeform), and its steel's way in is known to lay.
        /// </summary>
        private void ChuteVia(VPart from, VPart to, VPart via, float height, float low)
        {
            try
            {
                var (a, fa) = PortOf(from, null);
                var (w, _) = PortOf(via, null);
                var (b, fb) = PortOf(to, "Chute in");
                Vector3 a1 = a + fa, b1 = b + fb;
                Link("chute", from, to, null, "Chute in", Stock, false, null,
                    Cut(a, a1, V(a1.X, height, a1.Z), V(w.X, height, a1.Z), V(w.X, height, w.Z), V(w.X, low, w.Z), V(b1.X, low, w.Z), V(b1.X, low, b1.Z), b1));
                _links++;
            }
            catch (VehicleKitException ex)
            {
                Note($"a chute {from.Name} -> {to.Name} not laid: {ex.Message}");
                _linkMissed++;
            }
        }

        /// <summary>Each kit line's Extruder (at a line console's Extruder) chuted from its side's copper, concrete, rubber and cloth bins.</summary>
        private void KitLineStock(IEnumerable<Vector2> extruders)
        {
            var up = 21f; // (over the back row's other chutes, 16 to 20 m)
            var n = 0;
            foreach (var at in extruders.Distinct())
            {
                if (NearestLab("extruder", at, 1) is not { } core)
                    continue;
                var s = MathF.Sign(at.X);
                var missed = _linkMissed;
                var own = Nearest("Materials Bin", at, 20); // (its steel bin, 11 m behind it: each chute comes down over it and on as its steel does)
                var low = 5f;
                foreach (var x in KitBins)
                {
                    if (Find("Materials Bin", P(s, x, 114)) is { } bin && own != null)
                        ChuteVia(bin, core, own, up, low);
                    else
                        _linkMissed++;
                    low += 0.25f;
                    up += 0.25f;
                }
                if (_linkMissed > missed)
                    Note($"kit lines: {_linkMissed - missed} of the chutes into the Extruder at ({at.X:0}, {at.Y:0}) not laid");
                n++;
            }
            Note($"kit lines: {n} Extruders chuted from the copper, concrete, rubber and cloth bins");
        }
    }
}
