using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The catwalks (doc "Catwalks"): Steel Catwalks a storey up (3 m) on Catwalk Legs.
        //  - The north crossing: one walk across the whole shed between the materials row's bunkers and the extruder strip (z -37.9 to
        //    -39.6), over the dies at the head of every line and north of the crane rails, so no bridge passes it. A Roamer Stair at the
        //    west wall (its foot on the floor's metre grid: every piece hangs off the last), 14 runs of 6 m and one of 3 m, a stair down
        //    at the east wall. A leg goes under each run where the floor under it is free (a die or a bunker's chute stands there).
        //  - The doc's two long walks beside the column rows are left out: lines B and C's cutter beds and Out Tables run up to the
        //    columns, so a walk there would hang over a bed the cranes lift from (Sean decides).
        public const float CrossingZ = -38f;

        private void LayWalks()
        {
            var stair = Paint(On("Roamer Stair", 2, CrossingZ, East, "b0_0"), Steel);
            var run = Run(stair, "head", 14, tail: "Steel Catwalk 3 m");
            Stair(run, "+z");
        }

        /// <summary><paramref name="sixes"/> 6 m runs on from <paramref name="from"/>'s <paramref name="socket"/>, then a <paramref name="tail"/>
        /// piece (if any): the last piece.</summary>
        private VPart Run(VPart from, string socket, int sixes, string? tail = "Steel Catwalk 1.5 m", string by = "-z")
        {
            var last = from;
            var at = socket;
            for (var i = 0; i < sixes; i++)
            {
                last = Piece(last, at, "Steel Catwalk 6 m", i == 0 ? by : "-z");
                at = "+z";
            }
            if (tail != null)
                last = Piece(last, at, tail, sixes == 0 ? by : "-z");
            return last;
        }

        /// <summary>A catwalk piece on the last one's open end, and a pair of legs under it (where the floor under it is free).</summary>
        private VPart Piece(VPart on, string socket, string part, string by = "-z")
        {
            var p = Paint(Onto(on, socket, part, by), Steel);
            if (part.EndsWith(" m"))
            {
                var k = p.Def.Sockets.Any(s => s.Name == "legL2") ? 2 : 0;
                foreach (var side in new[] { "L", "R" })
                    Leg(p, $"leg{side}{k}");
            }
            return p;
        }

        private void Leg(VPart p, string socket)
        {
            try
            {
                Paint(Onto(p, socket, "Steel Catwalk Leg", "+y"), Steel);
            }
            catch (VehicleKitException e) when (Refused(e))
            {
                // (a die or a chute under the walk there: the run is held by its neighbours' legs)
            }
        }

        /// <summary>A Roamer Stair down from a walk's open end.</summary>
        private void Stair(VPart end, string socket) => Paint(Onto(end, socket, "Roamer Stair", "head"), Steel);
    }
}
