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
/// <item>A press line's row is bridged where its Assembler's bed stood: two Roller Tiles in the bed's column nearest the station (rolling
/// toward it as the row does), welded onto the row's run to the pad, and a short conveyor over the bed's other column from the end of
/// the one-tile run off the second press into the first of them (a run ends on another only through a Turntable Tile, and the one-tile
/// run is half a tile off the two-tile row's grid, as it was off the bed's). That column and the Core's cell stay free floor.</item>
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
    /// <summary>The press lines' Assembler beds' cells (layout |x|, z), now Roller Tiles.</summary>
    private static readonly (int X, int Z)[] BridgeCells = [(31, 29), (31, 31)];

    /// <summary>The one-tile run off a press line's second press (layout |x|, z), whose end the bridge takes.</summary>
    private const int BridgeFromX = 35, BridgeFromZ = 30;

    private static IEnumerable<string> AssemblerBridges() =>
        new[] { 1, -1 }.SelectMany(s => BridgeCells.Select(c => $$"""{"t": "part", "name": "Roller Tile", "x": {{s * c.X}}, "z": {{c.Z}}, "rot": {{(s > 0 ? 3 : 1)}}}"""));

    private sealed partial class Script
    {
        /// <summary>A layout item that was an Assembler's (its Core and bed blocks): left out since phase 5.</summary>
        private static bool Retired(JsonElement e) => Str(e, "name") is "Assembler" or "Assembly Bed Block";

        /// <summary>The back row's bins every kit line's Extruder draws on besides its steel (layout |x|): copper, concrete, rubber, cloth.</summary>
        private static readonly float[] KitBins = [26, 41, 46, ClothBin];

        /// <summary>A press line's bridge: a conveyor from the second press's one-tile run's end into the bridge's first tile.</summary>
        private void BridgeRun(float s)
        {
            var end = Find("Roller Tile", P(s, BridgeFromX, -BridgeFromZ), 0.5f);
            var into = Find("Roller Tile", P(s, BridgeCells[^1].X, -BridgeCells[^1].Z), 0.5f);
            if (end == null || into == null)
            {
                Note($"bridge on side {s}: no run end or bridge tile");
                _linkMissed++;
                return;
            }
            try
            {
                var (a, fa) = PortOf(end, "Piece out");
                var (b, fb) = PortOf(into, "Piece in");
                Vector3 a1 = a + fa, b1 = b + fb * 0.5f; // (1 m on past the run's end, across to the bridge's row, and into its first tile)
                Link("conveyor", end, into, "Piece out", "Piece in", Line, false, null, Cut(a, a1, V(a1.X, a1.Y, b1.Z), b1));
                _links++;
                Note($"bridge on side {s}: a conveyor from the run's end at ({a.X:0.#}, {a.Z:0.#}) into the bridge at ({b.X:0.#}, {b.Z:0.#})");
            }
            catch (VehicleKitException ex)
            {
                Note($"bridge on side {s}: the conveyor not laid: {ex.Message}");
                _linkMissed++;
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
