namespace CallOfSlop;

/// <summary>
/// What the city's signs say (docs/planets/CITY.md "Signs"): the words the shader writes with the panels' font onto shop
/// fascias, neon blades, the market's sign and the stations' name boards. A sign holds a word's index; the words go to
/// the GPU once as a small table (a row a word, a column a letter, the last column its length). Player-visible text: ASCII.
/// </summary>
public static class CitySigns
{
    /// <summary>Letters a word may have (the table's last column holds its length).</summary>
    public const int MaxLetters = 15;

    /// <summary>What shops are called; a shop picks one by its seed.</summary>
    public static readonly string[] Shops =
    [
        "DINER", "PIZZA", "NOODLES", "SLOP MART", "PHARMACY", "CINEMA", "BANK", "CAFE", "BOOKS", "RADIO", "TAILOR",
        "DELI", "LAUNDRY", "KEBAB", "BAKERY", "JEWELLER", "PAWN", "TATTOO", "ARCADE", "CLUB", "GYM", "SUSHI", "FLOWERS",
        "TOYS", "HARDWARE", "BARBER", "DENTIST", "OPEN 24H", "OFF LICENCE", "HOTEL", "BAR", "GRILL", "NEWS", "OPTICIAN",
        "FISH + CHIPS", "BURGERS", "PET SHOP", "SHOES", "REPAIRS", "LOANS", "KARAOKE", "DUMPLINGS", "TEA ROOM", "BUTCHER",
        "SLOP COLA", "CASINO", "LOTTO", "VIDEO", "HATS", "RAMEN",
    ];

    /// <summary>The metro's stations, from the line's south end to its north (more than there are is fine).</summary>
    public static readonly string[] Metro = ["DOCK GATE", "SOUTH LOOP", "CENTRAL", "NORTH LOOP", "HIGH STREET", "CANAL", "OLD TOWN"];

    /// <summary>The elevated railway's stations, round the loop.</summary>
    public static readonly string[] Loop = ["SOUTH LOOP", "EAST LOOP", "NORTH LOOP", "WEST LOOP"];

    /// <summary>Every word in the table, in order: the shops, the market, the metro's stations, the loop's.</summary>
    public static readonly string[] All = [.. Shops, "MARKET", .. Metro, .. Loop];

    public static int MarketWord => Shops.Length;

    public static int MetroWord(int station) => Shops.Length + 1 + Math.Min(station, Metro.Length - 1);

    public static int LoopWord(int station) => Shops.Length + 1 + Metro.Length + station % Loop.Length;

    /// <summary>The table: All.Length rows of 16 bytes, a letter's code less 32 (0: a space or past the end), the length last.</summary>
    public static byte[] Table()
    {
        var t = new byte[All.Length * (MaxLetters + 1)];
        for (var w = 0; w < All.Length; w++)
        {
            var word = All[w];
            for (var i = 0; i < Math.Min(word.Length, MaxLetters); i++)
                t[w * (MaxLetters + 1) + i] = (byte)Math.Clamp(word[i] - 32, 0, 94);
            t[w * (MaxLetters + 1) + MaxLetters] = (byte)Math.Min(word.Length, MaxLetters);
        }
        return t;
    }
}
