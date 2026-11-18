using System.Numerics;

namespace CallOfSlop;

/// <summary>For the window tool's <c>boardwalk</c> (host): a player put in a gripped boarding pod just behind its cutter, facing the
/// hole it cut (ConstructBreach.OpenHole), so W walks them out through it into the other ship.</summary>
public sealed partial class Game
{
    /// <summary>Host: <paramref name="p"/> in the first gripped pod, <paramref name="back"/> m behind its cutter's face, on its floor.
    /// <paramref name="yaw"/>: the look that faces the hole. Null, or why not.</summary>
    internal string? BoardWalkStart(Player p, float back, out float yaw, out float pitch)
    {
        yaw = 0;
        pitch = 0;
        foreach (var pod in Constructs.Bodies)
        {
            if (!ConstructBreach.Gripped(Constructs, pod) || BoardPart(pod, BreachKind.Cutter) is var cutter && cutter < 0)
                continue;
            ConstructBreach.Face(pod, cutter, out var face, out var down);
            var up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, pod.Rot));
            var level = Vector3.Normalize(down - up * Vector3.Dot(down, up));
            var at = face - level * back;
            var feet = Constructs.Raycast(at, -up, 2.4f, out var floor, only: pod) ? floor.Point + up * 0.03f : at - up * 0.95f;
            p.Teleport(feet);
            ConstructRide.Join(ref p.Sim, pod, 0, ConstructRide.UpFor(pod, up), Vector3.Zero);
            // The look's yaw that faces the hole, as this view measures it (on a planet, against its tangent frame here).
            LookFrame = p.Position;
            var best = -2f;
            for (var a = 0; a < 720; a++)
            {
                var y = a * MathF.Tau / 720 - MathF.PI;
                var dot = Vector3.Dot(LookDir(y, 0), level);
                if (dot > best)
                    (best, yaw) = (dot, y);
            }
            best = -2f;
            for (var a = -60; a <= 60; a++)
            {
                var dot = Vector3.Dot(LookDir(yaw, a * 0.02f), level);
                if (dot > best)
                    (best, pitch) = (dot, a * 0.02f);
            }
            // What lies ahead at knee, waist and head height (the pod's door, then the hole or the hull).
            var say = new System.Text.StringBuilder($"[nav] boardwalk: pod {pod.Id}, cutter face {pod.ToLocal(face):0.00}, doors open:");
            if (pod.EnsureDoors())
                for (var k = 0; k < pod.DoorCount; k++)
                    say.Append(FormattableString.Invariant($" {pod.DefOf(pod.Doors[k].Part).Name} {ConstructDoors.OpenFraction(pod, k, true):0.00} (moving {ConstructDoors.IsMoving(pod, pod.Doors[k].Part)}, to open {ConstructDoors.TargetOpen(pod, pod.Doors[k].Part)}, wired {pod.Doors[k].Wired}, in {pod.Parts[pod.Doors[k].Part].In:0.0}/{pod.Parts[pod.Doors[k].Part].InCount}, count {pod.Parts[pod.Doors[k].Part].Count});"));
            say.Append(FormattableString.Invariant($" charge {pod.Charge:0}/{pod.Cap:0}"));
            foreach (var h in (float[])[0.4f, 1.0f, 1.6f])
            {
                var from = feet + up * h;
                say.Append(FormattableString.Invariant($"; at {h:0.0} m: "));
                if (Constructs.Raycast(from, level, 6f, out var hit) && hit.Body != null)
                    say.Append(FormattableString.Invariant($"{hit.Body.DefOf(hit.Part).Name} of {hit.Body.Id} {hit.Distance:0.00} m"));
                else
                    say.Append("clear 6 m");
            }
            // Seen through the pod's door from head height: how many of a fan of rays across its opening find nothing (space, sky or ground).
            var eye = feet + up * 1.6f;
            var across = Vector3.Normalize(Vector3.Cross(level, up));
            int open = 0, rays = 0, onSleeve = 0;
            for (var i = 0; i < 21; i++)
                for (var j = 0; j < 21; j++)
                {
                    var aim = face + across * (-0.53f + 1.06f * i / 20) + up * (-0.9f + 1.86f * j / 20);
                    rays++;
                    if (!Constructs.Raycast(eye, Vector3.Normalize(aim - eye), 60f, out var fh) || fh.Body == null)
                        open++;
                    else if (fh.Body == pod && HullHoles.SleevesOf(pod.DefOf(fh.Part)).Length > 0)
                        onSleeve++;
                }
            say.Append(FormattableString.Invariant($"; through the door: {open} of {rays} rays see out, {onSleeve} on the seal sleeve"));
            Console.WriteLine(say.ToString());
            return null;
        }
        return "no pod is gripped on a hull";
    }
}
