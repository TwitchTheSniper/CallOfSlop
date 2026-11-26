namespace CallOfSlop;

/// <summary>
/// The Station Console (Sean, 2026-11-26: "a console ... that just mirrors the build station screen so that a player could set up a
/// control room with a bunch of consoles and not have to walk around the entire factory"). A desk like the Line Console, a logic part
/// whose second output is a watch link (as a Level Sensor's: <see cref="ConstructRoutes.WatchPort"/>) to a Building Station on the same
/// body. Its screen (panel source <c>station.remote</c>) is that station's own screen under the station's number on the body; the use key
/// at the desk opens that station's panel (Program.Sandbox.cs), and the server lets a station be worked from a console that watches it
/// (<see cref="ReachFrom"/>). Its first output, Done, is on while the station's build is done. Nothing new goes on the wire: the watch
/// link is an ordinary data link and the station's job already reaches clients. Nothing here allocates.
/// </summary>
public static class ConstructStationConsole
{
    /// <summary>The Building Station a console's watch link names (<see cref="CPart.Watch"/>: its index plus one), or -1.</summary>
    public static int StationOf(Construct c, ushort watch)
    {
        var s = watch - 1;
        return s >= 0 && s < c.PartCount && c.DefOf(s).IsBuildStation ? s : -1;
    }

    /// <summary>The console's output: 1 while its station's build is done, else 0.</summary>
    public static float Done(Construct c, ushort watch) => StationOf(c, watch) is var s and >= 0 && c.Parts[s].Station is { State: StationState.Done } ? 1 : 0;

    /// <summary>A station's number on its body, 1 up, in part order (what a console's screen calls it).</summary>
    public static int Number(Construct c, int station)
    {
        var n = 1;
        for (var k = 0; k < station && k < c.PartCount; k++)
            if (c.DefOf(k).IsBuildStation)
                n++;
        return n;
    }

    private static readonly string[] Titles = Enumerable.Range(0, 33).Select(n => $"STATION {n}").ToArray();

    /// <summary>The console's screen: the watched station's own, titled with its number, or how to link it.</summary>
    public static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        if (!ConstructPower.On(c, part))
        {
            card.State = PanelState.Fault;
            card.Add(0, "No power");
            return;
        }
        var s = StationOf(c, p.Watch);
        if (s < 0)
        {
            card.State = PanelState.Idle;
            card.Add(0, "STATION CONSOLE");
            int at = 0, column = 0;
            card.Wrap("Link its watch output to a Building Station.", card.Cols, 1, card.TextRows - 1, 0, ref at, ref column);
            return;
        }
        var n = Number(c, s);
        ConstructStation.Fill(world, c, s, card, n < Titles.Length ? Titles[n] : "BUILDING STATION");
    }

    /// <summary>
    /// The nearest a player at <paramref name="eye"/> stands to station <paramref name="station"/>'s work through a console: the closest
    /// Station Console on the body that watches it (its world centre), or infinity. The server's reach check for a station's panel.
    /// </summary>
    public static float ReachFrom(Construct c, int station, System.Numerics.Vector3 eye)
    {
        var best = float.PositiveInfinity;
        for (var k = 0; k < c.PartCount; k++)
        {
            ref readonly var q = ref c.Parts[k];
            if (q.Watch != station + 1 || c.DefOf(k).Logic != CLogic.StationConsole || q.Broken)
                continue;
            best = MathF.Min(best, System.Numerics.Vector3.Distance(eye, ConstructEdits.ReachPoint(c, k, eye)));
        }
        return best;
    }
}
