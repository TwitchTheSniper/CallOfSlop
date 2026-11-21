using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Production machines that hurt whoever is in the way (machines never stop for people: hazard stripes and signs are the warning).
/// The construct step leaves this step's boxes in <see cref="ConstructWorld.Hazards"/> (a Bake Oven's hood coming down, its closed
/// hood's heat: ConstructBakeOven.cs); the server hurts every living player whose middle is in one, every tick, through the usual
/// path, so it needs no new packet.
/// </summary>
public sealed partial class Game
{
    private void ServerTickMachineHazards(double now)
    {
        var list = Constructs.Hazards;
        if (list.Count == 0)
            return;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.Sim.Noclip)
                continue;
            var mid = p.Position + Vector3.UnitY * 0.9f;
            for (var k = 0; k < list.Count; k++)
            {
                var h = list[k];
                var v = Vector3.Transform(mid, h.ToLocal);
                if (v.X < h.Min.X || v.Y < h.Min.Y || v.Z < h.Min.Z || v.X > h.Max.X || v.Y > h.Max.Y || v.Z > h.Max.Z)
                    continue;
                DamagePlayer(p, p, h.Damage, false, SuicideKillId, now);
                break;
            }
        }
    }
}
