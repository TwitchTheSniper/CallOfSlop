using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>Throwaway diagnosis (removed before finishing).</summary>
public static class VehicleDriveDiag
{
    public static int Cli(string file)
    {
        var text = File.ReadAllText(file);
        var bp = ConstructBuild.ParseBlueprint(text, Path.GetFileName(file));
        var q = new SelfTest.DriveWorld();
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out var why, VehicleChecks.TowOf(bp));
        if (c == null)
        {
            Console.WriteLine("no build: " + why);
            return 1;
        }
        Console.WriteLine($"mass {c.Mass:0} com {c.Com} pos {c.Pos}");
        for (var t = 0; t <= 240; t++)
        {
            if (t % 30 == 0)
            {
                var up = Vector3.Transform(Vector3.UnitY, c.Rot);
                var fwd = Vector3.Transform(-Vector3.UnitZ, c.Rot);
                Console.WriteLine($"t {t}: pos {c.Pos.X:0.00},{c.Pos.Y:0.00},{c.Pos.Z:0.00} up {up.X:0.000},{up.Y:0.000},{up.Z:0.000} fwd.y {fwd.Y:0.000} vel {c.Vel.Length():0.00}");
            }
            w.Step(q, World.TickDt);
        }
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var wh = ref c.Wheels[k];
            Console.WriteLine($"wheel {k} {c.Parts[wh.Part].D.Name} on {c.Parts[wh.Suspension].D.Name}: centre {wh.Centre} r {wh.Radius:0.00} travel {wh.Travel:0.00} comp {wh.Compression:0.000} touching {wh.Touching} maxload {wh.MaxLoad:0} driven {wh.Driven} steers {wh.Steers} seg {wh.Seg}");
        }
        for (var i = 0; i < c.PartCount; i++)
        {
            c.PartBounds(i, out var mn, out var mx);
            c.SegPose(c.Parts[i].Seg, out var sq, out var st);
            var low = float.MaxValue;
            for (var k = 0; k < 8; k++)
            {
                var p = new Vector3((k & 1) != 0 ? mx.X : mn.X, (k & 2) != 0 ? mx.Y : mn.Y, (k & 4) != 0 ? mx.Z : mn.Z);
                low = MathF.Min(low, c.ToWorld(st + Vector3.Transform(p, sq)).Y);
            }
            if (low < 0.12f && c.DefOf(i).Role != PartRole.Wheel)
                Console.WriteLine($"low part {i} {c.DefOf(i).Name} seg {c.Parts[i].Seg}: {low:0.000} bounds {mn} {mx}");
        }
        return 0;
    }
}
