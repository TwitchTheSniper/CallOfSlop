using System.Globalization;

namespace CallOfSlop.ProcShips;

/// <summary>The ship's size class: its length, beam and how many decks it usually has.</summary>
public enum ProcClass { Corvette, Frigate, Destroyer }

/// <summary>The hull's family: a chisel nose and flared flanks (the Zero Day's), a long tapering dart, or a chamfered block.</summary>
public enum ProcHull { Chisel, Dart, Block }

/// <summary>
/// What a procedural ship is asked for (docs/procships/PROCSHIPS.md): a seed, its class and anything the user fixed. Everything not
/// fixed comes from the seed, each choice from its own stream of the dice (<see cref="ProcDice.For"/>), so a later generator that makes
/// a new choice leaves the old ones as they were and a grade on "Frigate 17's hull" still means the same hull.
/// </summary>
public sealed record ProcShipSpec(int Seed, ProcClass Class, ProcHull? Hull = null, int? Decks = null)
{
    /// <summary>The generator's version: a new version may change what a seed makes (the gallery says which made each ship).</summary>
    public const int Version = 1;

    public const int MaxSeed = 99999;

    /// <summary>A short code that names the ship in its pieces' names: the class's letter, the seed, then what was fixed ("F17", "F17d", "F17b3").</summary>
    public string Code
    {
        get
        {
            var s = ClassLetter(Class) + Seed.ToString(CultureInfo.InvariantCulture);
            if (Hull is { } h)
                s += h switch { ProcHull.Chisel => "c", ProcHull.Dart => "d", _ => "b" };
            if (Decks is { } d)
                s += d.ToString(CultureInfo.InvariantCulture);
            return s;
        }
    }

    /// <summary>The ship's name: "Frigate 17", with what was fixed ("Frigate 17, dart hull, 3 decks").</summary>
    public string Title
    {
        get
        {
            var s = $"{Class} {Seed}";
            if (Hull is { } h)
                s += $", {h.ToString().ToLowerInvariant()} hull";
            if (Decks is { } d)
                s += $", {d} deck{(d == 1 ? "" : "s")}";
            return s;
        }
    }

    public static string ClassLetter(ProcClass c) => c switch { ProcClass.Corvette => "C", ProcClass.Frigate => "F", _ => "D" };

    /// <summary>
    /// A spec from text: a code ("F17", "f17d3", "D5b") or words ("frigate 17", "17 dart 3 decks", "corvette 4 block"). Null when it
    /// names no seed or a class twice.
    /// </summary>
    public static ProcShipSpec? Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        // A code: a class letter, digits, then an optional hull letter and deck digit.
        var m = System.Text.RegularExpressions.Regex.Match(text, "^([CcFfDd])(\\d{1,5})([cdbCDB])?([1-3])?$");
        if (m.Success)
        {
            var cls = char.ToUpperInvariant(m.Groups[1].Value[0]) switch { 'C' => ProcClass.Corvette, 'F' => ProcClass.Frigate, _ => ProcClass.Destroyer };
            ProcHull? hull = m.Groups[3].Success ? char.ToLowerInvariant(m.Groups[3].Value[0]) switch { 'c' => ProcHull.Chisel, 'd' => ProcHull.Dart, _ => ProcHull.Block } : null;
            int? decks = m.Groups[4].Success ? m.Groups[4].Value[0] - '0' : null;
            var seed = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return seed is >= 1 and <= MaxSeed ? new ProcShipSpec(seed, cls, hull, decks) : null;
        }
        ProcClass? c = null;
        ProcHull? hl = null;
        int? dk = null, sd = null;
        var words = text.ToLowerInvariant().Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (Enum.TryParse<ProcClass>(w, true, out var pc))
                c = pc;
            else if (Enum.TryParse<ProcHull>(w, true, out var ph))
                hl = ph;
            else if (w is "deck" or "decks" && i > 0 && int.TryParse(words[i - 1], out var nd) && nd is >= 1 and <= 3)
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
            else if (w is not ("hull" or "seed"))
                return null;
        }
        return sd is { } seedOf ? new ProcShipSpec(seedOf, c ?? ProcClass.Frigate, hl, dk) : null;
    }
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
