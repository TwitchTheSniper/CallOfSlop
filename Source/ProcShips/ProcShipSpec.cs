using System.Globalization;

namespace CallOfSlop.ProcShips;

/// <summary>The ship's size class: its length, beam and how many decks it usually has.</summary>
public enum ProcClass { Corvette, Frigate, Destroyer }

/// <summary>
/// The hull's family: a chisel nose and flared flanks (the Zero Day's), a long tapering dart, a chamfered block, a wide barge with an
/// open cargo well along its back, or a stepped hull (a tall block of decks over one long low deck).
/// </summary>
public enum ProcHull { Chisel, Dart, Block, Barge, Stepped }

/// <summary>What the ship is for: it sets its rooms, the spaces that span decks (holds, hangars, torpedo bays, atria) and its sockets.</summary>
public enum ProcRole { Gunship, Freighter, Miner, Tender, Liner }

/// <summary>
/// What a procedural ship is asked for (docs/procships/PROCSHIPS.md): a seed, its class and anything the user fixed. Everything not
/// fixed comes from the seed, each choice from its own stream of the dice (<see cref="ProcDice.For"/>), so a later generator that makes
/// a new choice leaves the old ones as they were and a grade on "Frigate 17's hull" still means the same hull.
/// </summary>
public sealed record ProcShipSpec(int Seed, ProcClass Class, ProcHull? Hull = null, int? Decks = null, ProcRole? Role = null, bool Nacelles = false)
{
    /// <summary>The generator's version: a new version may change what a seed makes (the gallery says which made each ship).</summary>
    public const int Version = 2;

    public const int MaxSeed = 99999, MaxDecks = 5;

    /// <summary>
    /// A short code that names the ship in its pieces' names: the class's letter, the seed, then what was fixed: the hull's letter, n for
    /// nacelles, the decks, the role's letter ("F17", "F17d", "F17b3", "F17an2f"). A deck count straight after the seed takes a hyphen
    /// ("F5-3f", Frigate 5 with 3 decks, a freighter), or it would read as the seed's last digit ("F53f", Frigate 53, a freighter).
    /// </summary>
    public string Code
    {
        get
        {
            var s = ClassLetter(Class) + Seed.ToString(CultureInfo.InvariantCulture);
            if (Hull is { } h)
                s += HullLetter(h);
            if (Nacelles)
                s += "n";
            if (Decks is { } d)
                s += (Hull == null && !Nacelles ? "-" : "") + d.ToString(CultureInfo.InvariantCulture);
            if (Role is { } r)
                s += RoleLetter(r);
            return s;
        }
    }

    /// <summary>The ship's name: "Frigate 17", with what was fixed ("Frigate 17, dart hull, nacelles, 3 decks, freighter").</summary>
    public string Title
    {
        get
        {
            var s = $"{Class} {Seed}";
            if (Hull is { } h)
                s += $", {h.ToString().ToLowerInvariant()} hull";
            if (Nacelles)
                s += ", nacelles";
            if (Decks is { } d)
                s += $", {d} deck{(d == 1 ? "" : "s")}";
            if (Role is { } r)
                s += $", {r.ToString().ToLowerInvariant()}";
            return s;
        }
    }

    public static string ClassLetter(ProcClass c) => c switch { ProcClass.Corvette => "C", ProcClass.Frigate => "F", _ => "D" };

    public static string HullLetter(ProcHull h) => h switch { ProcHull.Chisel => "c", ProcHull.Dart => "d", ProcHull.Block => "b", ProcHull.Barge => "a", _ => "s" };

    public static string RoleLetter(ProcRole r) => r switch { ProcRole.Gunship => "g", ProcRole.Freighter => "f", ProcRole.Miner => "m", ProcRole.Tender => "t", _ => "l" };

    private static readonly Dictionary<string, ProcHull> HullWords = new(StringComparer.Ordinal)
    {
        ["chisel"] = ProcHull.Chisel, ["dart"] = ProcHull.Dart, ["block"] = ProcHull.Block, ["barge"] = ProcHull.Barge, ["stepped"] = ProcHull.Stepped, ["tower"] = ProcHull.Stepped,
    };

    private static readonly Dictionary<string, ProcRole> RoleWords = new(StringComparer.Ordinal)
    {
        ["gunship"] = ProcRole.Gunship, ["warship"] = ProcRole.Gunship, ["freighter"] = ProcRole.Freighter, ["hauler"] = ProcRole.Freighter, ["cargo"] = ProcRole.Freighter,
        ["miner"] = ProcRole.Miner, ["mining"] = ProcRole.Miner, ["tender"] = ProcRole.Tender, ["carrier"] = ProcRole.Tender, ["liner"] = ProcRole.Liner, ["passenger"] = ProcRole.Liner,
    };

    /// <summary>
    /// A spec from text: a code ("F17", "f17d3", "D5b", "F17an2f", "F5-3f") or words ("frigate 17", "17 dart 3 decks", "corvette 4 block",
    /// "destroyer 9 stepped liner", "frigate 3 chisel nacelles gunship"). Null when it names no seed or a word it doesn't know.
    /// </summary>
    public static ProcShipSpec? Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        // A code: a class letter, digits, then an optional hull letter, n (nacelles), deck digit (after a hyphen when it follows the
        // seed) and role letter.
        var m = System.Text.RegularExpressions.Regex.Match(text, "^([CFD])(\\d{1,5})([cdbas])?(n)?(-?[1-5])?([gfmtl])?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var cls = char.ToUpperInvariant(m.Groups[1].Value[0]) switch { 'C' => ProcClass.Corvette, 'F' => ProcClass.Frigate, _ => ProcClass.Destroyer };
            ProcHull? hull = m.Groups[3].Success ? char.ToLowerInvariant(m.Groups[3].Value[0]) switch { 'c' => ProcHull.Chisel, 'd' => ProcHull.Dart, 'b' => ProcHull.Block, 'a' => ProcHull.Barge, _ => ProcHull.Stepped } : null;
            int? decks = m.Groups[5].Success ? m.Groups[5].Value[^1] - '0' : null;
            ProcRole? role = m.Groups[6].Success ? char.ToLowerInvariant(m.Groups[6].Value[0]) switch { 'g' => ProcRole.Gunship, 'f' => ProcRole.Freighter, 'm' => ProcRole.Miner, 't' => ProcRole.Tender, _ => ProcRole.Liner } : null;
            var seed = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return seed is >= 1 and <= MaxSeed ? new ProcShipSpec(seed, cls, hull, decks, role, m.Groups[4].Success) : null;
        }
        ProcClass? c = null;
        ProcHull? hl = null;
        ProcRole? rl = null;
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
            else if (w is not ("hull" or "seed" or "with" or "a" or "an"))
                return null;
        }
        return sd is { } seedOf ? new ProcShipSpec(seedOf, c ?? ProcClass.Frigate, hl, dk, rl, nacelles) : null;
    }

    /// <summary>The role a spec has: the one fixed, else the dice's, weighted by class.</summary>
    public static ProcRole RoleOf(ProcShipSpec spec, ProcDice dice) => spec.Role ?? spec.Class switch
    {
        ProcClass.Corvette => dice.For("ship.role").Weighted((ProcRole.Gunship, 0.34), (ProcRole.Freighter, 0.2), (ProcRole.Miner, 0.26), (ProcRole.Tender, 0.08), (ProcRole.Liner, 0.12)),
        ProcClass.Frigate => dice.For("ship.role").Weighted((ProcRole.Gunship, 0.3), (ProcRole.Freighter, 0.22), (ProcRole.Miner, 0.14), (ProcRole.Tender, 0.16), (ProcRole.Liner, 0.18)),
        _ => dice.For("ship.role").Weighted((ProcRole.Gunship, 0.34), (ProcRole.Freighter, 0.2), (ProcRole.Miner, 0.08), (ProcRole.Tender, 0.2), (ProcRole.Liner, 0.18)),
    };
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
