using System.Globalization;

namespace CallOfSlop.ProcShips;

/// <summary>The ship's size class: its length, beam and how many decks it usually has.</summary>
public enum ProcClass { Corvette, Frigate, Destroyer }

/// <summary>
/// The hull's family, its section: a chisel nose and flared flanks (the Zero Day's), a long tapering dart, a chamfered block, a wide
/// barge with an open cargo well along its back, a stepped hull (a tall block of decks over one long low deck), a kite (a low, flat,
/// sharply faceted stealth section, widest at one chine) or a cage (a barge whose deep well is a hollow shell of ring frames, where an
/// ice hauler carries its chunks). New families go at the end.
/// </summary>
public enum ProcHull { Chisel, Dart, Block, Barge, Stepped, Kite, Cage }

/// <summary>
/// The bow, by what it is for: the family's own (a chisel's knife, a dart's needle, a block's face, a barge's rake, a kite's flat
/// blade), a lance (a long cone ending in a spinal railgun's muzzle), a fork (two prongs either side of a slot: a salvage berth, a
/// repair berth or a railgun's rails), a hammer (a wide sensor bar on a neck), a mouth (a blunt face open into a hangar or a cargo
/// hold), a dome (a glazed observation dome) or a jaw (a raked dozer face with a lip for cutters).
/// </summary>
public enum ProcBow { Family, Lance, Fork, Hammer, Mouth, Dome, Jaw }

/// <summary>What the ship is for: it sets its bow, its rooms, the spaces that span decks (holds, hangars, torpedo bays, atria), its sides and its sockets. New roles go at the end.</summary>
public enum ProcRole { Gunship, Freighter, Miner, Tender, Liner, Lancer, Carrier, Comms, Stealth, Salvager, IceHauler }

/// <summary>
/// What a procedural ship is asked for (docs/procships/PROCSHIPS.md): a seed, its class and anything the user fixed. Everything not
/// fixed comes from the seed, each choice from its own stream of the dice (<see cref="ProcDice.For"/>), so a later generator that makes
/// a new choice leaves the old ones as they were and a grade on "Frigate 17's hull" still means the same hull.
/// </summary>
public sealed record ProcShipSpec(int Seed, ProcClass Class, ProcHull? Hull = null, int? Decks = null, ProcRole? Role = null, bool Nacelles = false, ProcBow? Bow = null)
{
    /// <summary>The generator's version: a new version may change what a seed makes (the gallery says which made each ship).</summary>
    public const int Version = 3;

    public const int MaxSeed = 99999, MaxDecks = 5;

    /// <summary>
    /// A short code that names the ship in its pieces' names: the class's letter, the seed, then what was fixed: the hull's letter, the
    /// bow's letter, n for nacelles, the decks, the role's letter ("F17", "F17d", "F17b3", "F17an2f", "F17cy2v"). A deck count straight
    /// after the seed takes a hyphen ("F5-3f", Frigate 5 with 3 decks, a freighter), or it would read as the seed's last digit ("F53f",
    /// Frigate 53, a freighter). The letter sets never share a letter, so every part is optional.
    /// </summary>
    public string Code
    {
        get
        {
            var s = ClassLetter(Class) + Seed.ToString(CultureInfo.InvariantCulture);
            if (Hull is { } h)
                s += HullLetter(h);
            if (Bow is { } b)
                s += BowLetter(b);
            if (Nacelles)
                s += "n";
            if (Decks is { } d)
                s += (Hull == null && Bow == null && !Nacelles ? "-" : "") + d.ToString(CultureInfo.InvariantCulture);
            if (Role is { } r)
                s += RoleLetter(r);
            return s;
        }
    }

    /// <summary>The ship's name: "Frigate 17", with what was fixed ("Frigate 17, dart hull, fork bow, nacelles, 3 decks, salvager").</summary>
    public string Title
    {
        get
        {
            var s = $"{Class} {Seed}";
            if (Hull is { } h)
                s += $", {h.ToString().ToLowerInvariant()} hull";
            if (Bow is { } b)
                s += $", {BowWord(b)} bow";
            if (Nacelles)
                s += ", nacelles";
            if (Decks is { } d)
                s += $", {d} deck{(d == 1 ? "" : "s")}";
            if (Role is { } r)
                s += $", {RoleWord(r)}";
            return s;
        }
    }

    /// <summary>A role as words ("ice hauler").</summary>
    public static string RoleWord(ProcRole r) => r == ProcRole.IceHauler ? "ice hauler" : r.ToString().ToLowerInvariant();

    public static string BowWord(ProcBow b) => b.ToString().ToLowerInvariant();

    public static string ClassLetter(ProcClass c) => c switch { ProcClass.Corvette => "C", ProcClass.Frigate => "F", _ => "D" };

    // The letter sets (hulls, bows, n, roles) share no letter: a code's parts are all optional and still read one way.
    private const string HullLetters = "cdbasko", BowLetters = "qpyhuwj", RoleLetters = "gfmtlrxezvi";

    public static string HullLetter(ProcHull h) => HullLetters[(int)h].ToString();

    /// <summary>The bow's letter: q fixes the family's own bow (no letter leaves the bow to the dice, by role).</summary>
    public static string BowLetter(ProcBow b) => BowLetters[(int)b].ToString();

    public static string RoleLetter(ProcRole r) => RoleLetters[(int)r].ToString();

    private static readonly Dictionary<string, ProcHull> HullWords = new(StringComparer.Ordinal)
    {
        ["chisel"] = ProcHull.Chisel, ["dart"] = ProcHull.Dart, ["block"] = ProcHull.Block, ["barge"] = ProcHull.Barge, ["stepped"] = ProcHull.Stepped, ["tower"] = ProcHull.Stepped,
        ["kite"] = ProcHull.Kite, ["cage"] = ProcHull.Cage, ["caged"] = ProcHull.Cage, ["shell"] = ProcHull.Cage,
    };

    private static readonly Dictionary<string, ProcBow> BowWords = new(StringComparer.Ordinal)
    {
        ["lance"] = ProcBow.Lance, ["spear"] = ProcBow.Lance, ["fork"] = ProcBow.Fork, ["forked"] = ProcBow.Fork, ["prongs"] = ProcBow.Fork, ["hammer"] = ProcBow.Hammer,
        ["hammerhead"] = ProcBow.Hammer, ["mouth"] = ProcBow.Mouth, ["dome"] = ProcBow.Dome, ["jaw"] = ProcBow.Jaw, ["dozer"] = ProcBow.Jaw, ["shovel"] = ProcBow.Jaw,
    };

    private static readonly Dictionary<string, ProcRole> RoleWords = new(StringComparer.Ordinal)
    {
        ["gunship"] = ProcRole.Gunship, ["warship"] = ProcRole.Gunship, ["freighter"] = ProcRole.Freighter, ["hauler"] = ProcRole.Freighter, ["cargo"] = ProcRole.Freighter,
        ["miner"] = ProcRole.Miner, ["mining"] = ProcRole.Miner, ["tender"] = ProcRole.Tender, ["liner"] = ProcRole.Liner, ["passenger"] = ProcRole.Liner,
        ["lancer"] = ProcRole.Lancer, ["railgun"] = ProcRole.Lancer, ["spinal"] = ProcRole.Lancer, ["carrier"] = ProcRole.Carrier, ["comms"] = ProcRole.Comms,
        ["sensor"] = ProcRole.Comms, ["picket"] = ProcRole.Comms, ["stealth"] = ProcRole.Stealth, ["prowler"] = ProcRole.Stealth, ["salvager"] = ProcRole.Salvager,
        ["salvage"] = ProcRole.Salvager, ["ice"] = ProcRole.IceHauler, ["icehauler"] = ProcRole.IceHauler,
    };

    /// <summary>
    /// A spec from text: a code ("F17", "f17d3", "D5b", "F17an2f", "F5-3f", "D4by3v") or words ("frigate 17", "17 dart 3 decks",
    /// "corvette 4 block", "destroyer 9 stepped liner", "frigate 3 chisel nacelles gunship", "frigate 8 fork bow salvager", "destroyer
    /// 2 cage ice hauler"). Null when it names no seed or a word it doesn't know.
    /// </summary>
    public static ProcShipSpec? Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        // A code: a class letter, digits, then an optional hull letter, bow letter, n (nacelles), deck digit (after a hyphen when it
        // follows the seed) and role letter.
        var m = System.Text.RegularExpressions.Regex.Match(text, $"^([CFD])(\\d{{1,5}})([{HullLetters}])?([{BowLetters}])?(n)?(-?[1-5])?([{RoleLetters}])?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var cls = char.ToUpperInvariant(m.Groups[1].Value[0]) switch { 'C' => ProcClass.Corvette, 'F' => ProcClass.Frigate, _ => ProcClass.Destroyer };
            ProcHull? hull = m.Groups[3].Success ? (ProcHull)HullLetters.IndexOf(char.ToLowerInvariant(m.Groups[3].Value[0])) : null;
            ProcBow? bow = m.Groups[4].Success ? (ProcBow)BowLetters.IndexOf(char.ToLowerInvariant(m.Groups[4].Value[0])) : null;
            int? decks = m.Groups[6].Success ? m.Groups[6].Value[^1] - '0' : null;
            ProcRole? role = m.Groups[7].Success ? (ProcRole)RoleLetters.IndexOf(char.ToLowerInvariant(m.Groups[7].Value[0])) : null;
            var seed = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            // A hyphen only where the decks follow the seed straight (so each ship has one code).
            if (m.Groups[6].Success && m.Groups[6].Value.StartsWith('-') != (hull == null && bow == null && !m.Groups[5].Success))
                return null;
            return seed is >= 1 and <= MaxSeed ? new ProcShipSpec(seed, cls, hull, decks, role, m.Groups[5].Success, bow) : null;
        }
        ProcClass? c = null;
        ProcHull? hl = null;
        ProcRole? rl = null;
        ProcBow? bw = null;
        var nacelles = false;
        int? dk = null, sd = null;
        var words = text.ToLowerInvariant().Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (Enum.TryParse<ProcClass>(w, true, out var pc))
                c = pc;
            else if (HullWords.TryGetValue(w, out var ph))
                hl = ph;
            else if (BowWords.TryGetValue(w, out var pb))
                bw = pb;
            else if (RoleWords.TryGetValue(w, out var pr))
                rl = pr;
            else if (w is "nacelles" or "nacelle")
                nacelles = true;
            else if (w is "deck" or "decks" && i > 0 && int.TryParse(words[i - 1], out var nd) && nd is >= 1 and <= MaxDecks)
            {
                dk = nd;
                if (sd == nd)
                    sd = null;
            }
            else if (int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= MaxSeed)
            {
                if (i + 1 < words.Length && words[i + 1] is "deck" or "decks")
                    continue; // (the deck count, read with its word)
                sd ??= n;
            }
            else if (w is not ("hull" or "seed" or "with" or "a" or "an" or "bow" or "hauler" or "ship"))
                return null;
        }
        return sd is { } seedOf ? new ProcShipSpec(seedOf, c ?? ProcClass.Frigate, hl, dk, rl, nacelles, bw) : null;
    }

    /// <summary>The role a spec has: the one fixed, else the dice's, weighted by class (version 3: eleven roles).</summary>
    public static ProcRole RoleOf(ProcShipSpec spec, ProcDice dice) => spec.Role ?? dice.For("ship.role3").Weighted<ProcRole>(spec.Class switch
    {
        ProcClass.Corvette => [(ProcRole.Gunship, 0.15), (ProcRole.Freighter, 0.1), (ProcRole.Miner, 0.14), (ProcRole.Tender, 0.04), (ProcRole.Liner, 0.06), (ProcRole.Lancer, 0.1),
            (ProcRole.Carrier, 0.03), (ProcRole.Comms, 0.1), (ProcRole.Stealth, 0.14), (ProcRole.Salvager, 0.1), (ProcRole.IceHauler, 0.04)],
        ProcClass.Frigate => [(ProcRole.Gunship, 0.12), (ProcRole.Freighter, 0.09), (ProcRole.Miner, 0.07), (ProcRole.Tender, 0.07), (ProcRole.Liner, 0.08), (ProcRole.Lancer, 0.11),
            (ProcRole.Carrier, 0.1), (ProcRole.Comms, 0.08), (ProcRole.Stealth, 0.08), (ProcRole.Salvager, 0.1), (ProcRole.IceHauler, 0.1)],
        _ => [(ProcRole.Gunship, 0.13), (ProcRole.Freighter, 0.08), (ProcRole.Miner, 0.04), (ProcRole.Tender, 0.06), (ProcRole.Liner, 0.08), (ProcRole.Lancer, 0.12),
            (ProcRole.Carrier, 0.16), (ProcRole.Comms, 0.07), (ProcRole.Stealth, 0.03), (ProcRole.Salvager, 0.09), (ProcRole.IceHauler, 0.14)],
    });

    /// <summary>Is the role a warship's (it arms first and carries little)?</summary>
    public static bool Warship(ProcRole r) => r is ProcRole.Gunship or ProcRole.Lancer or ProcRole.Stealth;
}

/// <summary>
/// The generator's dice: SplitMix64 streams, one per named choice, from the seed. The same seed and name always give the same
/// numbers, whatever else the generator asks for, so choices stay put when new ones are added.
/// </summary>
public sealed class ProcDice(int seed)
{
    private readonly ulong _seed = Mix((ulong)seed * 0x9E3779B97F4A7C15UL ^ 0x5DEECE66DUL);

    /// <summary>The stream for one choice ("hull.bow", "rooms.pitch").</summary>
    public Stream For(string what) => new(Mix(_seed ^ Fnv(what)));

    private static ulong Fnv(string s)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in s)
            h = (h ^ ch) * 1099511628211UL;
        return h;
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public sealed class Stream(ulong state)
    {
        private ulong _s = state;

        public ulong Next()
        {
            _s += 0x9E3779B97F4A7C15UL;
            return Mix(_s);
        }

        /// <summary>A number in [0, 1).</summary>
        public double U() => (Next() >> 11) * (1.0 / (1UL << 53));

        public double Range(double a, double b) => a + (b - a) * U();

        /// <summary>A whole number from a to b, both included.</summary>
        public int Int(int a, int b) => a + (int)(U() * (b - a + 1));

        public bool Chance(double p) => U() < p;

        public T Pick<T>(params T[] xs) => xs[Math.Min(xs.Length - 1, (int)(U() * xs.Length))];

        public T Weighted<T>(params (T Value, double Weight)[] xs)
        {
            var total = xs.Sum(x => x.Weight);
            var r = U() * total;
            foreach (var (v, w) in xs)
            {
                if (r < w)
                    return v;
                r -= w;
            }
            return xs[^1].Value;
        }
    }
}
