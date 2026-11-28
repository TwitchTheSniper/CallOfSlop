using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Retiring the Assembler, phase 5 (Sean's plan, 2026-11-28: "Zero Day Works and Works II lose their Assemblers"). The first works had
/// two: the block Assembler in bay 3 (its Core at x 75, z -132, a 4 x 4 bed, its Thruster Jig, the Block Rack and the Sewing Table block)
/// and the yard Assembler west of the yard's bay (a 6 x 11 bed). They go with their chutes, Runs and the Router's belt into the first.
/// <list type="bullet">
/// <item>Where the block Assembler stood, a small press (a Core and a 2-block bed, as the two south of it) with a Cable Die: the frigate's
/// Wiring Looms and Copper Coils, which no other press here makes (the works' Die Racks aren't chuted to the presses), from copper rounds
/// cut by its own Extruder east of its bed. Its parts ride a belt into the small presses' Join's free west side, and on through the
/// Router with the others. The rest of that floor, and the yard Assembler's, stays free.</item>
/// <item>The fittings the Assemblers built are baked from their kits (BakeKits) on the line ovens at lines A, B and C, whose kits carry
/// rubber and concrete sheets and copper pieces: an oven takes a kit only while every piece's material reaches an Extruder its station's
/// consoles run (ConstructExtruder.Feeds), so each of those lines' Sheet Extruders is chuted from the copper, concrete and rubber bins
/// too. This works makes no cloth, so a kit with a cloth sheet (the Bunk, the Space Suit Locker) has no maker here.</item>
/// </list>
/// A saved works keeps its Assemblers: they still build as the fallback where no oven serves (tools/MIGRATING.md).
/// </summary>
public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        /// <summary>The press where the block Assembler stood: its Core's place, its die and its blanks' material.</summary>
        public const float AsmPressX = 75f, AsmPressZ = -131f;

        private const string AsmPressDie = "Cable Die", AsmPressBlank = "copper";

        private VPart? _asmPress;

        /// <summary>The small press where the block Assembler stood, its Extruder and its stock chute.</summary>
        private void LayAssemblerPress()
        {
            var press = SmallPress(AsmPressX, AsmPressZ);
            Onto(press, "dieslot", AsmPressDie, "mount");
            _makers.Add(press);
            _asmPress = press;
            // (its Extruder as the small presses': east of the bed, turned West so its die faces the press, its conveyor into bay 1)
            var bedEnd = BoundsOf(BedEnd(press)).Hi.X;
            var d = Def("Extruder");
            var ex = MathF.Ceiling(bedEnd + 1.3f + d.BoundsMax.Z);
            var ext = On("Extruder", ex, AsmPressZ, West, paint: General);
            OrdersOnly(ext);
            Belt(ext, "Piece out", press, "Piece in", null, General2);
            // Its copper: over the bays at 10 m (the band the Assemblers' chutes took), down west of the Extruder's back to 3 m, then in.
            var (back, fb) = PortOf(ext, "Chute in");
            var b1 = back + fb;
            var leg = MathF.Min(b1.X, 92f);
            var (a, fa) = PortOf(_bins[AsmPressBlank], "Chute out");
            var a1 = a + fa;
            const float up = 10f;
            Link("chute", _bins[AsmPressBlank], ext, "Chute out", "Chute in", Stock, false, null,
                Cut(a, a1, V(a1.X, up, a1.Z), V(leg, up, a1.Z), V(leg, up, b1.Z), V(leg, 3f, b1.Z), V(b1.X, 3f, b1.Z), b1));
        }

        /// <summary>The kit lines' Sheet Extruders (lines A, B and C, whose ovens bake the fittings' kits) chuted from the copper, concrete and rubber bins.</summary>
        private void KitLineStock()
        {
            var up = 10.5f;
            foreach (var line in new[] { "A", "B", "C" })
                foreach (var m in new[] { "copper", "concrete", "rubber" })
                {
                    Chute(_bins[m], _lineExtruders[line], up);
                    up += 0.25f;
                }
        }
    }
}
