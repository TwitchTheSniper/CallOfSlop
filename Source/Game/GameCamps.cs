namespace CallOfSlop;

/// <summary>
/// Camps, hardship and raider bands (economy step 9; Karl, 2026-11-22: "raided cargo becomes the raiders' stock", raiders "build from loot",
/// and raiders "come from poor economic situations"). A power with no station (the Raiders) keeps its goods in its camp: what its sites dig and
/// what its raids take go there, and it builds and repairs its ships from there, waiting when it is short (as a power with stations waits on
/// them, <see cref="IPowerHost.TakeGoods"/>). A camp starts with the goods for two of the first ships it asks for, once a game.
/// Hardship is kept per planet with stations: once a minute it moves a sixtieth of the way towards the share of those stations' lines that are
/// short, food, water and fuel counting double, so an hour of shortage makes it. Over <see cref="HardshipBand"/> a raider band forms there (two
/// over <see cref="HardshipBands"/>), bringing the goods for a ship to the Raiders' camp, and the Raiders may field a ship more for each band
/// (<see cref="IPowerHost.Bands"/>); under it again, the band breaks up. With no hardship and no ships left, the Raiders lie quiet: no new bands form. Host only; kept in the markets section after the consignments (GameStationSave.cs).
/// </summary>
public sealed partial class Game
{
    private const int CampMaterials = 256, MaxBands = 4;
    private const float HardshipBand = 0.5f, HardshipBands = 0.8f, HardshipRate = 1f / 60;
    private readonly int[] _camp = new int[256 * CampMaterials]; // (by power and material)
    private readonly bool[] _campSeeded = new bool[256];
    private int _bandShips; // (bands formed whose ship's goods the Raiders' camp hasn't had yet: each band brings its own)
    private readonly Dictionary<string, float> _hardship = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _bandsAt = new(StringComparer.OrdinalIgnoreCase);
    private int _bands;

    /// <summary>Does power <paramref name="power"/> hold a station (Crew/Stations.json)?</summary>
    private static bool HasStation(byte power)
    {
        foreach (var d in StationMarkets.All)
            if (CrewPowers.Find(d.Power) == power)
                return true;
        return false;
    }

    /// <summary>Host: a ship's goods from a camp, all or nothing (TakeGoods for a power with no station).</summary>
    private bool CampTake(byte power, (byte Material, float Units)[] goods, float share, out byte shortOf)
    {
        shortOf = 0;
        if (!_campSeeded[power])
        {
            _campSeeded[power] = true;
            foreach (var (mat, units) in goods)
                _camp[power * CampMaterials + mat] += (int)MathF.Ceiling(units * 2);
            Log($"[camp] {CrewPowers.Of(power)?.Name}: its camp starts with the goods for two ships");
        }
        if (_bandShips > 0 && power == CrewPowers.Find("Raiders"))
        {
            foreach (var (mat, units) in goods)
                _camp[power * CampMaterials + mat] += (int)MathF.Ceiling(units * _bandShips);
            Log($"[camp] {CrewPowers.Of(power)?.Name}: {_bandShips} new band(s) bring the goods for a ship each");
            _bandShips = 0;
        }
        foreach (var (mat, units) in goods)
            if (_camp[power * CampMaterials + mat] < (int)MathF.Ceiling(units * share))
            {
                shortOf = mat;
                return false;
            }
        foreach (var (mat, units) in goods)
            _camp[power * CampMaterials + mat] -= (int)MathF.Ceiling(units * share);
        return true;
    }

    /// <summary>Host: goods into a camp (a site's or a raid's).</summary>
    private void CampPut(byte power, byte mat, int units)
    {
        if (units > 0 && mat < CampMaterials)
            _camp[power * CampMaterials + mat] = (int)Math.Min(int.MaxValue / 2, (long)_camp[power * CampMaterials + mat] + units);
    }

    /// <summary>Host: what a raid took, worth <paramref name="value"/>, into the raider's camp as the goods its ships are built of (<paramref name="goods"/>).</summary>
    private void CampLoot(byte raider, (byte Material, float Units)[] goods, double value)
    {
        if (!IsServer || value <= 0 || goods.Length == 0 || HasStation(raider))
            return;
        double bill = 0;
        foreach (var (mat, units) in goods)
            bill += TradeValues.Of(mat) * units;
        if (bill <= 0)
            return;
        var share = value / bill;
        foreach (var (mat, units) in goods)
            CampPut(raider, mat, (int)Math.Ceiling(units * share)); // (up: a small raid still brings something)
        Log($"[camp] {CrewPowers.Of(raider)?.Name}: a raid's loot, {value:0} of value, goes into its camp as {share:0.##} of a ship's goods");
    }

    /// <summary>Host, once a minute (the needs step): each planet's hardship from its stations' shortages, and the raider bands it forms.</summary>
    private void HardshipTick()
    {
        var all = StationMarkets.All;
        var bands = 0;
        foreach (var planet in Controllers.Territory.Planets)
        {
            double short_ = 0, lines = 0;
            for (var d = 0; d < all.Length; d++)
            {
                if (!all[d].Planet.Equals(planet, StringComparison.OrdinalIgnoreCase) || _marketByDef.Length <= d || _marketByDef[d] is not { } m)
                    continue;
                for (var k = 0; k < all[d].Goods.Length; k++)
                {
                    if (all[d].Goods[k].Use <= 0)
                        continue;
                    var w = Basic(all[d].Goods[k].Material) ? 2 : 1;
                    lines += w;
                    if (m.Short(k))
                        short_ += w;
                }
            }
            if (lines <= 0)
                continue;
            var was = _hardship.GetValueOrDefault(planet);
            var now = (float)(was + (short_ / lines - was) * HardshipRate);
            _hardship[planet] = now;
            var here = now > HardshipBands ? 2 : now > HardshipBand ? 1 : 0;
            var before = _bandsAt.GetValueOrDefault(planet);
            if (here != before)
            {
                _bandsAt[planet] = here;
                if (here > before)
                    _bandShips = Math.Min(MaxBands, _bandShips + here - before);
                Log(here > before ? $"[hardship] {planet} is poor (hardship {now:0.00}: its stations go short): a raider band forms there ({here})"
                    : $"[hardship] {planet} is better off (hardship {now:0.00}): a raider band there breaks up ({here} left)");
            }
            bands += here;
        }
        _bands = Math.Min(bands, MaxBands);

        // (food, water and fuel: what a crew can't do without)
        static bool Basic(byte mat)
        {
            var n = RecyclingTable.Current.NameOf(mat);
            return n is "produce" or "water" or "hydrogen";
        }
    }

    /// <summary>Host, saving: the camps, then each planet's hardship (after the consignments).</summary>
    private void CampWrite(SaveWriter w)
    {
        var n = 0;
        for (var i = 0; i < _camp.Length; i++)
            if (_camp[i] > 0)
                n++;
        w.I32(n);
        for (var i = 0; i < _camp.Length; i++)
            if (_camp[i] > 0)
                w.Str(CrewPowers.Of((byte)(i / CampMaterials))?.Name ?? "").Str(RecyclingTable.Current.NameOf((byte)(i % CampMaterials))).I32(_camp[i]);
        var seeded = 0;
        foreach (var s in _campSeeded)
            seeded += s ? 1 : 0;
        w.I32(seeded);
        for (var p = 0; p < _campSeeded.Length; p++)
            if (_campSeeded[p])
                w.Str(CrewPowers.Of((byte)p)?.Name ?? "");
        w.I32(_hardship.Count);
        foreach (var (planet, h) in _hardship)
            w.Str(planet).F32(h);
        w.I32(_bandShips);
    }

    /// <summary>Host, loading: the camps and hardship by name; a power or good gone is dropped with a log line.</summary>
    private void CampRead(SaveReader s)
    {
        CampsReset();
        if (!s.Has(4))
            return;
        var n = s.Count(10);
        for (var i = 0; i < n; i++)
        {
            string power = s.Str(), mat = s.Str();
            var units = s.I32();
            var p = CrewPowers.Find(power);
            var m = RecyclingTable.Current.IdOf(mat);
            if (p == 0 || m == 0 || m >= CampMaterials)
            {
                Log($"[camp] the saved {units} {mat} of {power}'s camp is dropped: the power or the good is gone");
                continue;
            }
            CampPut(p, m, units);
        }
        var seeded = s.Count(1);
        for (var i = 0; i < seeded; i++)
            if (CrewPowers.Find(s.Str()) is var p and > 0)
                _campSeeded[p] = true;
        var hn = s.Count(5);
        for (var i = 0; i < hn; i++)
        {
            var planet = s.Str();
            var h = s.F32();
            if (float.IsFinite(h))
                _hardship[planet] = Math.Clamp(h, 0, 1);
        }
        if (s.Has(4))
            _bandShips = Math.Clamp(s.I32(), 0, MaxBands);
        foreach (var (planet, h) in _hardship)
            _bandsAt[planet] = h > HardshipBands ? 2 : h > HardshipBand ? 1 : 0;
        _bands = Math.Min(_bandsAt.Values.Sum(), MaxBands);
    }

    /// <summary>Host, a new game: no camps, no hardship.</summary>
    private void CampsReset()
    {
        Array.Clear(_camp);
        Array.Clear(_campSeeded);
        _hardship.Clear();
        _bandsAt.Clear();
        _bands = 0;
        _bandShips = 0;
    }
}
