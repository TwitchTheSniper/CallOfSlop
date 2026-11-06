namespace CallOfSlop;

/// <summary>
/// Streamed terrain (TerrainStream.cs): the server asks for the ground round every live player, and a client round its
/// own player, a little ahead of time, so the tiles are made on a worker before anyone walks, shoots or lands on them. A
/// pilot's drone counts too: it is where they are looking, and it flies on that ground.
/// </summary>
public sealed partial class Game
{
    /// <summary>Ground made ready round each player (metres): two tiles at 2 m cells, so a sprint never reaches unmade ground.</summary>
    private const float StreamRadius = 260f;

    /// <summary>Ticks between asks (the ask is a few array reads when the tiles are held).</summary>
    private const int StreamEvery = 6;

    private void ServerTickTerrainStream()
    {
        if (World.Map.Terrain?.Stream is not { } s || ServerTick % StreamEvery != 0)
            return;
        foreach (var p in Players.Values)
            if (p.Alive)
            {
                s.Prefetch(p.Position.X, p.Position.Z, StreamRadius);
                if (SandboxOn && SandboxHands.Hand(p.Id).RemoteBody != 0)
                {
                    var at = SandboxInterestAt(p);
                    s.Prefetch(at.X, at.Z, StreamRadius);
                }
            }
    }

    private void ClientTickTerrainStream()
    {
        if (World.Map.Terrain?.Stream is not { } s || Local is not { Alive: true } me || _nextSeq % StreamEvery != 0)
            return;
        s.Prefetch(me.Position.X, me.Position.Z, StreamRadius);
        if (LocalDrone() is { } drone)
        {
            var at = drone.WorldCom;
            s.Prefetch(at.X, at.Z, StreamRadius);
        }
    }
}
