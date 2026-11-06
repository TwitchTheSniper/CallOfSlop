using System.Numerics;

namespace CallOfSlop;

// THROWAWAY: removed before the final push.
public static partial class SelfTest
{
    private static Construct WtBuild(ConstructWorld w, string name, Vector3 at, float yaw = 0)
    {
        var bp = ConstructBuild.Blueprints.First(b => b.Name == name);
        var c = ConstructBuild.Build(w, bp, at, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), out var why);
        if (c == null) { Line($"{name}: doesn't build: {why}"); throw new Exception(why); }
        c.Rebuild();
        return c;
    }

    private static void WtSeat(Construct c, float dt, Buttons keys, int extra = 0)
    {
        var seat = c.DriveSeat >= 0 ? c.DriveSeat : c.ControlSeat;
        if (seat < 0) return;
        c.Driver = 0;
        c.DriverSeat = seat;
        c.Parts[seat].Keys = (ushort)((ushort)keys | extra | CPart.Occupied);
        ConstructEdits.DriveInput(c, keys, dt);
    }

    private static string WtState(Construct c, float t)
    {
        var e = Quaternion.Normalize(c.Rot);
        var f = Vector3.Transform(-Vector3.UnitZ, e);
        var u = Vector3.Transform(Vector3.UnitY, e);
        var pitch = MathF.Asin(Math.Clamp(f.Y, -1, 1)) * 57.3f;
        var rot = c.RotorCount > 0 ? $"rotor0 {c.Rotors[0].Omega * ConstructDrive.RpmPerRadS:0} rpm T {c.RotorThrust:0} N P {c.Rotors[0].Power / 1000:0} kW" : "";
        return $"t {t:0.0} pos ({c.Pos.X:0},{c.Pos.Y:0.0},{c.Pos.Z:0}) v {c.Vel.Length():0.0} vy {c.Vel.Y:0.0} pitch {pitch:0.0} roll {MathF.Atan2(-u.X, u.Y) * 57.3f:0.0} w ({c.AngVel.X:0.00},{c.AngVel.Y:0.00},{c.AngVel.Z:0.00}) lift {c.AeroLift:0} drag {c.AeroDrag:0} rpm {c.Rpm:0} share {c.ShaftShare:0.00} {rot} trim {c.AeroTrim:0.00}";
    }

    private static void WtBudget(string name, int count)
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        var list = new List<Construct>();
        for (var k = 0; k < count; k++)
        {
            var c = WtBuild(w, name, new Vector3((k % 4) * 40, 300, (k / 4) * 40));
            c.Vel = new Vector3(0, 0, -45);
            c.AeroTrim = 1;
            c.Wake();
            list.Add(c);
        }
        w.PlayerSpotCount = 1;
        var sw = new System.Diagnostics.Stopwatch();
        var ticks = 0L;
        var most = 0.0;
        for (var t = 0; t < 600; t++)
        {
            w.PlayerSpots[0] = list[0].Pos;
            foreach (var c in list)
                WtSeat(c, World.TickDt, Buttons.Sprint);
            sw.Restart();
            w.Step(q, World.TickDt);
            sw.Stop();
            if (t > 120) { ticks += sw.ElapsedTicks; most = Math.Max(most, sw.Elapsed.TotalMilliseconds); }
        }
        Line($"BUDGET {name} x{count}: {ticks / (double)System.Diagnostics.Stopwatch.Frequency * 1000 / 479:0.00} ms a tick, worst {most:0.00}");
    }

    private static void CheckWingsTmp()
    {
        WtBudget("Buggy", 1);
        WtBudget("Light Plane", 1);
        WtBudget("Light Plane", 16);
        foreach (var n in new[] { "Light Plane", "Small Helicopter" })
        {
            var bp = ConstructBuild.Blueprints.FirstOrDefault(b => b.Name == n);
            Line($"{n}: {(bp == null ? "MISSING" : ConstructDrive.Describe(bp))}");
        }
        foreach (var r in ConstructParts.Refused)
            if (r.Contains("Wing") || r.Contains("Rotor") || r.Contains("Fan") || r.Contains("Prop") || r.Contains("Elevator") || r.Contains("Rudder"))
                Line("REFUSED " + r);
        foreach (var r in ConstructBuild.RefusedBlueprints)
            Line("REFUSED BP " + r);
        var q = new DriveWorld();
        {
            var w = new ConstructWorld();
            var c = WtBuild(w, "Light Plane", new Vector3(0, 300, 0));
            ConstructAero.CentreOfLift(c, out var col);
            Line($"plane: mass {c.Mass:0}, area {c.SurfaceArea:0.0}, surfaces {c.SurfaceCount}, stall {ConstructAero.StallSpeed(c):0.0} m/s, com {c.Com}, centre of lift {col}, rotors {c.RotorCount} feed {(c.RotorCount > 0 ? c.Rotors[0].Feed : -9)} red {(c.RotorCount > 0 ? c.Rotors[0].Reduction : 0):0.00}, engine {c.Engine} seat {c.DriveSeat}, powertrain {c.HasPowertrain}, problem '{c.DriveProblem}' block '{c.DriveBlock}' wires {c.WireCount} routes {c.RouteCount}, I {c.Radius:0.0}");
            for (var k = 0; k < c.SurfaceCount; k++)
                Line($"  surf {c.DefOf(c.Surfaces[k].Part).Name} at {c.Surfaces[k].At} N {c.Surfaces[k].N} F {c.Surfaces[k].F} A {c.Surfaces[k].Area}");
            c.Vel = new Vector3(0, 0, -50);
            c.AeroTrim = 1;
            c.Wake();
            w.PlayerSpots[0] = c.Pos;
            w.PlayerSpotCount = 1;
            for (var t = 0; t < 60 * 30; t++)
            {
                w.PlayerSpots[0] = c.Pos;
                WtSeat(c, World.TickDt, Buttons.Sprint);
                w.Step(q, World.TickDt);
                if (t % 120 == 0) Line("plane " + WtState(c, t / 60f));
            }
        }
        {
            var w = new ConstructWorld();
            var c = WtBuild(w, "Small Helicopter", new Vector3(0, 1.6f, 0));
            for (var pi = 0; pi < c.PartCount; pi++) if (c.Parts[pi].D.Drive != DriveKind.Shaft || c.Parts[pi].D.Role == PartRole.Transfer) Line($"  part {pi} {c.Parts[pi].D.Name} pos {c.Parts[pi].Pos} rot {c.Parts[pi].Rot} s-y {(c.Parts[pi].D.Role == PartRole.Transfer ? Rot24.Apply(c.Parts[pi].Rot, -Vector3.UnitY) : default)} s-z {(c.Parts[pi].D.Role == PartRole.Transfer ? Rot24.Apply(c.Parts[pi].Rot, -Vector3.UnitZ) : default)}");
            Line($"heli routes {c.RouteCount} feeds {c.ShaftFeedCount} mass {c.Mass:0}, com {c.Com} rotor at {(c.RotorCount > 0 ? c.Rotors[0].At : default)} dir {(c.RotorCount > 0 ? c.Rotors[0].Dir : default)} fwd {(c.RotorCount > 0 ? c.Rotors[0].Fwd : default)}, tail {(c.RotorCount > 1 ? c.Rotors[1].Dir : default)} at {(c.RotorCount > 1 ? c.Rotors[1].At : default)} feed {(c.RotorCount > 0 ? c.Rotors[0].Feed : -9)} red {(c.RotorCount > 0 ? c.Rotors[0].Reduction : 0):0.00} engine {c.Engine} seat {c.DriveSeat}, powertrain {c.HasPowertrain} '{c.DriveProblem}' block '{c.DriveBlock}'");
            c.Wake();
            w.PlayerSpots[0] = c.Pos;
            w.PlayerSpotCount = 1;
            var hold = 8f;
            for (var t = 0; t < 60 * 30; t++)
            {
                w.PlayerSpots[0] = c.Pos;
                // A pilot: the collective by hand, and the keys to hold it level.
                var miss = hold - c.Pos.Y;
                if (t == 300) { c.Pos = new Vector3(0, 100, 0); c.Rot = Quaternion.Identity; c.Vel = c.AngVel = Vector3.Zero; hold = 105; }
                c.AeroTrim = t < 300 ? 0 : Math.Clamp(0.6f + 0.05f * miss - 0.09f * c.Vel.Y, 0, 1);
                var f = Vector3.Transform(-Vector3.UnitZ, c.Rot);
                var u = Vector3.Transform(Vector3.UnitY, c.Rot);
                var pitchNow = f.Y + 0.6f * Vector3.Dot(c.AngVel, Vector3.Transform(Vector3.UnitX, c.Rot));
                var rollNow = Vector3.Dot(u, Vector3.Transform(Vector3.UnitX, c.Rot)) - 0.6f * Vector3.Dot(c.AngVel, Vector3.Transform(-Vector3.UnitZ, c.Rot));
                Buttons want = pitchNow > 0.02f ? Buttons.Forward : pitchNow < -0.02f ? Buttons.Back : 0;
                var side = rollNow > 0.02f ? ConstructParts.KeyRollLeft : rollNow < -0.02f ? ConstructParts.KeyRollRight : 0;
                WtSeat(c, World.TickDt, want, side);
                w.Step(q, World.TickDt);
                if (t % 120 == 0) Line($"heli {WtState(c, t / 60f)} tail {c.Rotors[1].Thrust:0} N {c.Rotors[1].Omega * ConstructDrive.RpmPerRadS:0} rpm main T {c.Rotors[0].Thrust:0} charge {c.Charge:0}");
            }
        }
    }
}
