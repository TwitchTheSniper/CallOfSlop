using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The penal colony's day (docs/story/LABOUR.md "The day"): the timetable in Story/Labour/PenalColony.json ("day") runs
/// round from the morning count while the colony stands, and each gang works only in the periods it names ("when"). At
/// each change the Ministry's tannoy calls it (a line in the chat of everyone near the colony, and the chimes from the
/// nearest loudspeaker), and every PC lets the gangs off shift go, and the ones coming on arrive, only while nobody is
/// looking (Render/LabourRender.cs): the colony changes behind your back. Host only, apart from what it sends: one
/// <see cref="Msg.LabourDay"/> (u8 period, u64 the scenes on shift) a change, and to each joiner. Nothing saved: a loaded
/// world starts the day again. Nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>Server, from <see cref="LabourTick"/> (every 2 s): which period it is now; at a change, the gangs on shift and the call.</summary>
    private void LabourDayTick(double now)
    {
        var day = LabourScript.Current.Day;
        var period = -1;
        if (_lbColony != 0 && LabourCount > 0 && day.Length > 0)
        {
            var length = 0.0;
            foreach (var p in day)
                length += p.Seconds;
            var into = (now - _lbDayFrom) % length;
            if (into < 0)
                into += length;
            period = 0;
            while (period < day.Length - 1 && into >= day[period].Seconds)
                into -= day[period++].Seconds;
        }
        if (period == LabourPeriod)
            return;
        var first = LabourPeriod < 0;
        LabourPeriod = period;
        var shift = ulong.MaxValue;
        if (period >= 0)
        {
            shift = 0;
            var gangs = LabourScript.Current.Scenes;
            for (var i = 0; i < LabourCount && i < 64; i++)
                if (_lbGang[i] < 0 || _lbGang[i] >= gangs.Length || (gangs[_lbGang[i]].Periods >> period & 1) != 0)
                    shift |= 1ul << i;
        }
        LabourShift = shift;
        LabourDayVersion++;
        if (period >= 0)
        {
            var on = 0;
            var people = 0;
            for (var i = 0; i < LabourCount; i++)
                if (LabourOnShift(i))
                {
                    on++;
                    people += LabourScenes[i].Inmates + LabourScenes[i].Guards;
                }
            Log($"[labour] the colony's day: {day[period].Name}, {on} gangs and {people} people on shift");
        }
        LabourDaySend(null);
        if (period >= 0 && !first)
        {
            LabourCall(day[period], now);
            PartAmbience.AnnounceSoon(); // (the chimes from the nearest loudspeaker, on this PC; a client's in ReadLabourDay)
        }
    }

    /// <summary>
    /// Server, the window tool's "period NAME": the day jumps to the start of that period (taken at the next look, 2 s at
    /// most). False when there is no such period.
    /// </summary>
    public bool LabourJumpTo(string name)
    {
        var day = LabourScript.Current.Day;
        var from = 0.0;
        for (var k = 0; k < day.Length; k++)
        {
            if (day[k].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _lbDayFrom = SandboxNow - from - 0.5;
                _lbAt = double.NegativeInfinity; // (looked at again on the next tick)
                return true;
            }
            from += day[k].Seconds;
        }
        return false;
    }

    /// <summary>The window tool's "gang NAME": where that gang stands (its first point) and the way it faces (server: the file's names).</summary>
    public bool LabourGangAt(string name, out System.Numerics.Vector3 at, out System.Numerics.Vector3 facing)
    {
        at = facing = default;
        var gangs = LabourScript.Current.Scenes;
        for (var i = 0; i < LabourCount; i++)
            if (_lbGang[i] >= 0 && _lbGang[i] < gangs.Length && gangs[_lbGang[i]].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                at = LabourScenes[i].P0;
                facing = LabourScenes[i].Facing;
                return true;
            }
        return false;
    }

    /// <summary>Server: the period's call on the tannoy, in the chat of each player near the colony (not those still in the opening's scenes).</summary>
    private void LabourCall(LabourScript.Period period, double now)
    {
        if (period.Tannoy.Length == 0 || Constructs.Get(_lbColony) is not { } colony)
            return;
        var at = colony.ToWorld(System.Numerics.Vector3.Zero);
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers || OpeningHas(p.Id))
                continue;
            if (System.Numerics.Vector3.DistanceSquared(p.Position, at) > LabourCallReach * LabourCallReach)
                continue;
            OpeningSay(p, null, PlayAreas.Speaker, period.Tannoy, now);
        }
    }

    /// <summary>Server: the period and the gangs on shift, to everyone (<paramref name="to"/> null) or one joiner.</summary>
    private void LabourDaySend(RakNetGUID? to)
    {
        if (Net.IsOffline || !IsServer)
            return;
        var w = _lbDayW ??= new PacketWriter(16);
        w.Begin(Msg.LabourDay).U8((byte)(LabourPeriod + 1)).U64(LabourShift);
        if (to is { } g)
            Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
        else
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.LabourDay"/> (after its id). A new period after the first heard chimes the nearest tannoy.</summary>
    private void ReadLabourDay(ref PacketReader r)
    {
        if (!r.Has(9))
            return;
        var period = r.U8() - 1;
        var shift = r.U64();
        var heard = LabourPeriod >= 0;
        if (period != LabourPeriod && period >= 0)
        {
            var day = LabourScript.Current.Day;
            Log($"[labour] heard the colony's day: {(period < day.Length ? day[period].Name : $"period {period}")}");
            if (heard)
                PartAmbience.AnnounceSoon();
        }
        LabourPeriod = period;
        LabourShift = shift;
        LabourDayVersion++;
    }
}
