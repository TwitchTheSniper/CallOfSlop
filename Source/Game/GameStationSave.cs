using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The stations' markets in a saved world (the MRKT section, <see cref="WorldSaveWriter.MarketsTag"/>; Karl, 2026-11-22: "station stock isn't saved").
/// The host's main world keeps every station's stock (by station, good or part name, so a changed Crew/Stations.json still finds what it can,
/// and a line the file no longer lists is dropped with a log line); every world file keeps the moorings of its own ships, so a ship left in a
/// berth is still held there and its hauler's leg goes on (HaulAdopt). A landing keeps the markets it had in memory and reads only the
/// moorings of the world it arrives at (GameSpaceLanding.cs).
/// </summary>
public sealed partial class Game
{
    private const byte MarketsSaveVersion = 3; // 2: the stations' life first (GameStationLife.cs), its length before it; 3: each station's owner (a player faction) and growth

    /// <summary>Host: moorings read from a world, set once their ships and stations are in (<see cref="StationMoorsAdopt"/>).</summary>
    private readonly List<(ushort Ship, ushort Station, ushort Part, Vector3 At, Quaternion Rot)> _moorsLoaded = new();

    /// <summary>Host, saving: the markets' stock (the main world only: <paramref name="stock"/>) and this world's moorings. Null: nothing to keep.</summary>
    private byte[]? MarketsBytes(bool stock, Func<Construct, bool>? keep = null)
    {
        if (!IsServer)
            return null;
        var w = new SaveWriter(1024);
        w.U8(MarketsSaveVersion);
        if (stock)
        {
            var life = new SaveWriter(1024);
            StationLifeWrite(life); // (before the stock: a station made in play is made again before its stock is read)
            w.I32(life.Length).Bytes(life.Span);
        }
        else
            w.I32(0);
        var n = 0;
        if (stock)
            foreach (var m in _marketByDef)
                if (m != null)
                    n++;
        w.I32(n);
        if (stock)
            for (var d = 0; d < _marketByDef.Length && d < StationMarkets.All.Length; d++)
            {
                if (_marketByDef[d] is not { } m)
                    continue;
                var def = StationMarkets.All[d];
                w.Str(def.Name);
                var lines = Math.Min(m.Stock.Length, def.Goods.Length + def.Parts.Length);
                w.I32(lines);
                for (var k = 0; k < lines; k++)
                {
                    var good = k < def.Goods.Length;
                    w.U8(good ? (byte)0 : (byte)1)
                        .Str(good ? RecyclingTable.Current.NameOf(def.Goods[k].Material) : ConstructParts.Def(def.Parts[k - def.Goods.Length].Part).Name)
                        .I32(m.Stock[k]).F64(k < m.Frac.Length ? m.Frac[k] : 0);
                }
            }
        var moors = 0;
        foreach (var c in Constructs.Bodies)
            if (Kept(c, keep))
                moors++;
        w.I32(moors);
        foreach (var c in Constructs.Bodies)
            if (Kept(c, keep))
                w.U16(c.Id).U16(c.MooredTo).U16(c.MoorPart).F32(c.MoorAt.X).F32(c.MoorAt.Y).F32(c.MoorAt.Z)
                    .F32(c.MoorRot.X).F32(c.MoorRot.Y).F32(c.MoorRot.Z).F32(c.MoorRot.W);
        if (stock)
        {
            ConsignWrite(w); // (after the moorings: an older reader stops before them, GameConsignments.cs)
            CampWrite(w); // (GameCamps.cs)
        }
        if (stock && n > 0)
            Log($"[station] markets saved: {n} stations, {StockTotal()} units in stock; {moors} moorings; {_consigns.Count} consignments");
        return n == 0 && moors == 0 && (!stock || _consigns.Count == 0) ? null : w.Span.ToArray();

        // (a mooring is kept when the ship is in its berth and both bodies go into this file: a planetoid's stash takes only some)
        bool Kept(Construct c, Func<Construct, bool>? keep) => c.MooredTo != 0 && c.MoorDraw >= 1 && Constructs.Get(c.MooredTo) is { IsStation: true } st
            && (keep == null || keep(c) && keep(st));
    }

    /// <summary>
    /// Host, loading (WorldReadSection, or a landing with <paramref name="stock"/> false): the markets' stock over the data's, by name,
    /// and the moorings to set once the bodies are in.
    /// </summary>
    private void WorldReadMarkets(SaveReader r, bool stock)
    {
        var bytes = r.Bytes(r.I32());
        if (!IsServer)
            return;
        var s = new SaveReader(bytes);
        var v = s.U8();
        if (v > MarketsSaveVersion)
        {
            Log($"[station] the markets section is of version {v} (this build reads {MarketsSaveVersion}): stations start from Crew/Stations.json");
            return;
        }
        if (v >= 2)
        {
            var life = s.Bytes(s.I32());
            if (stock && life.Length > 0)
                StationLifeRead(new SaveReader(life), v);
        }
        int stations = s.Count(6), kept = 0, dropped = 0;
        for (var i = 0; i < stations; i++)
        {
            var name = s.Str();
            var lines = s.Count(14);
            var d = StationMarkets.Find(name);
            if (!stock || d < 0)
            {
                for (var k = 0; k < lines; k++)
                {
                    s.U8();
                    s.Str();
                    s.I32();
                    s.F64();
                }
                if (stock)
                    Log($"[station] the saved market of \"{name}\" is dropped: Crew/Stations.json has no station of that name");
                continue;
            }
            var def = StationMarkets.All[d];
            var m = MarketOfDef(d);
            if (m.Frac.Length != def.Goods.Length + def.Parts.Length)
                m.Frac = new double[def.Goods.Length + def.Parts.Length];
            for (var k = 0; k < lines; k++)
            {
                var part = s.U8() == 1;
                var key = s.Str();
                var amount = s.I32();
                var frac = s.F64();
                var at = -1;
                if (!part)
                {
                    var mat = RecyclingTable.Current.IdOf(key);
                    for (var j = 0; j < def.Goods.Length && at < 0; j++)
                        if (mat != 0 && def.Goods[j].Material == mat)
                            at = j;
                }
                else if (ConstructParts.Find(key) is { } pd)
                    for (var j = 0; j < def.Parts.Length && at < 0; j++)
                        if (def.Parts[j].Part == pd.Id)
                            at = def.Goods.Length + j;
                if (at < 0 || at >= m.Stock.Length)
                {
                    dropped++;
                    Log($"[station] {name}: the saved stock of \"{key}\" is dropped: the station no longer trades it");
                    continue;
                }
                var most = at < def.Goods.Length ? def.Goods[at].Most : def.Parts[at - def.Goods.Length].Most;
                m.Stock[at] = Math.Clamp(amount, 0, most);
                m.Frac[at] = double.IsFinite(frac) ? Math.Clamp(frac, -1, 1) : 0;
                kept++;
            }
        }
        var moors = s.Count(32);
        for (var k = 0; k < moors; k++)
        {
            var ship = s.U16();
            var station = s.U16();
            var part = s.U16();
            var at = new Vector3(s.F32(), s.F32(), s.F32());
            var rot = new Quaternion(s.F32(), s.F32(), s.F32(), s.F32());
            if (float.IsFinite(at.X + at.Y + at.Z + rot.X + rot.Y + rot.Z + rot.W) && rot.LengthSquared() > 0.5f)
                _moorsLoaded.Add((ship, station, part, at, Quaternion.Normalize(rot)));
        }
        if (stock)
        {
            ConsignRead(s); // (GameConsignments.cs)
            CampRead(s); // (GameCamps.cs)
        }
        if (stock)
            Log($"[station] markets loaded: {kept} stock lines kept, {dropped} dropped, {StockTotal()} units in stock; {_moorsLoaded.Count} moorings to take up");
        StationsVersion++;
    }

    /// <summary>Every market's stock added up (a save's and a load's log line, to see the two agree).</summary>
    private long StockTotal()
    {
        long t = 0;
        foreach (var m in _marketByDef)
            if (m != null)
                foreach (var v in m.Stock)
                    t += v;
        return t;
    }

    /// <summary>
    /// Host, from the station tick: the moorings a loaded world kept, set on their ships once both bodies are in (the world's
    /// bodies come in the same load). A ship or a berth that is gone is let go with a log line.
    /// </summary>
    private void StationMoorsAdopt()
    {
        if (_moorsLoaded.Count == 0)
            return;
        foreach (var (shipId, stationId, part, at, rot) in _moorsLoaded)
        {
            if (Constructs.Get(shipId) is not { } ship || Constructs.Get(stationId) is not { IsStation: true } st
                || part >= st.PartCount || !st.DefOf(part).IsDock || ship.MooredTo != 0 && ship.MooredTo != stationId)
            {
                Log($"[station] a saved mooring of construct {shipId} at construct {stationId}, berth {part}, is let go: the ship or its berth isn't there");
                continue;
            }
            ship.MooredTo = stationId;
            ship.MoorPart = part;
            ship.MoorAt = at;
            ship.MoorRot = rot;
            ship.MoorDraw = 1;
            ship.NavThrottle = -1;
            Log($"[station] construct {shipId} moored again at {StationName(st)} (construct {stationId}), berth {part}, as saved");
        }
        _moorsLoaded.Clear();
        StationsVersion++;
    }
}
