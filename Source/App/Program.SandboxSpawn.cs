using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox's spawn placement on this PC (GameSandboxSpawns.cs; the inventory's Spawn tab starts it): a ghost circle
/// where the crosshair meets the ground, green where the spawn can go and red (with the reason) where it can't; Fire
/// spawns there (again and again), Aim or Esc stops. The server checks it all again.
/// </summary>
public static unsafe partial class Program
{
    private static string _spLook = "";
    private static int _spLookKey = -1;

    /// <summary>While placing a spawn: the ghost and the keys. Returns true when it took the input this frame.</summary>
    private static bool SandboxSpawnInput(Game g, Player me)
    {
        if (!g.SpawnPlacing)
        {
            g.SpawnGhostShown = false;
            return false;
        }
        if (!g.SandboxCreativeSpawns)
        {
            g.SpawnPlacing = false;
            return false;
        }
        SandboxAimRay(g, me, out var eye, out var dir);
        const float reach = Game.SpawnReach;
        var wall = World.RaycastWorld(eye, dir, reach);
        string? why;
        g.SpawnGhostShown = wall < reach;
        if (!g.SpawnGhostShown)
            why = "aim at the ground (too far)";
        else if (g.Constructs.Raycast(eye, dir, wall, out _))
        {
            why = "on a construct: aim at the ground beside it";
            g.SpawnGhost = eye + dir * wall;
        }
        else
        {
            why = g.SandboxSpawnSpot(eye + dir * wall, out var feet);
            g.SpawnGhost = feet;
        }
        g.SpawnGhostOk = why == null;
        g.SpawnGhostWhy = why ?? "";
        var key = (why?.Length ?? 0) * 31 + (int)g.SpawnPick * 7 + g.SpawnCount;
        if (key != _spLookKey)
        {
            _spLookKey = key;
            var what = g.SpawnPick switch
            {
                SbSpawnKind.FriendlyBot => $"{g.SpawnBots} friendly bot{(g.SpawnBots == 1 ? "" : "s")}",
                SbSpawnKind.EnemyBot => $"{g.SpawnBots} enemy bot{(g.SpawnBots == 1 ? "" : "s")}",
                _ => $"{g.SpawnZombies:N0} zombie{(g.SpawnZombies == 1 ? "" : "s")}",
            };
            _spLook = why == null ? $"Spawn {what}: LMB here, RMB or Esc to stop" : $"Spawn {what}: {why} (RMB or Esc to stop)";
        }
        g.SandboxLookText = _spLook;
        if (Pressed(GameAction.Aim))
        {
            g.SpawnPlacing = false;
            return true;
        }
        if (Pressed(GameAction.Fire) && !_swallowClick)
        {
            if (why != null)
                g.SandboxSay(why);
            else
                g.SandboxSendSpawn(g.SpawnPick, g.SpawnSkill, g.SpawnLoad, g.SpawnLoadValue, g.SpawnMix, g.SpawnCount, g.SpawnGhost);
        }
        return true;
    }
}
