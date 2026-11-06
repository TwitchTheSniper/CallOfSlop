namespace CallOfSlop;

/// <summary>
/// Pipe Boxes (docs/sandbox/MEDIA.md, "Pipe Boxes"): a Music Player made for the Clan MacSlop's vehicles, which strikes up the pipes by
/// itself when someone takes the wheel with the engine running, and stops a while after they leave it. The sound is a game track's
/// (<see cref="PipeTunes"/>), so it takes the Music Player's whole path: the list on the part, the server's clock, every peer playing it
/// at the part, speakers on a data cable. Only the server's word changes here, a second at a time; nothing new goes on the wire.
/// </summary>
public static class ConstructPipes
{
    /// <summary>Seconds a Pipe Box keeps playing after its driver leaves or the engine stops: a piper finishes the phrase.</summary>
    public const int PlaysOnFor = 20;

    /// <summary>Server, once a second.</summary>
    public static void Tick(ConstructWorld world, uint tick)
    {
        foreach (var c in world.Bodies)
            for (var k = 0; k < c.MediaParts.Length; k++)
            {
                int i = c.MediaParts[k];
                if (i >= c.PartCount)
                    continue;
                ref var p = ref c.Parts[i];
                if (!p.D.MediaPipes)
                    continue;
                var driven = c.Driver != 255 && c.EngineRunning && !p.Broken;
                if (p.Media is not { List.Length: > 0 })
                {
                    if (!driven)
                        continue;
                    ConstructMedia.SetList(ref p, Defaults);
                }
                var m = p.Media!;
                if (driven && !m.PipesDriven && m.State == MediaState.Stopped)
                {
                    // A driver just took the wheel: strike up, a tune the clock picks, so a convoy's boxes don't all start on one.
                    ConstructMedia.Start(m, (int)(tick / World.TickRate % (uint)m.List.Length), c.Driver, tick);
                    world.StructureDirty.Add(c.Id);
                }
                m.PipesDriven = driven;
                if (driven || m.State == MediaState.Stopped)
                    m.PipesIdle = 0;
                else if (++m.PipesIdle >= PlaysOnFor)
                {
                    ConstructMedia.Halt(m);
                    m.PipesIdle = 0;
                    world.StructureDirty.Add(c.Id);
                }
            }
    }

    /// <summary>What a Pipe Box with an empty list plays: every pipe tune.</summary>
    private static readonly string[] Defaults = PipeTunes.Tracks.Select(MediaEntry.ForTrack).ToArray();
}
