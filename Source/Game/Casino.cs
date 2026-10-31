namespace CallOfSlop;

/// <summary>The casino games a Zombies map's table can be (wire bytes and the map file's "game"; never renumber).</summary>
public enum CasinoGame : byte
{
    Blackjack,
    Roulette,
    HighLow,
    Dice,
}

/// <summary>The games' names (the map file's "game" keys, what players read, the table labels).</summary>
public static class CasinoGames
{
    /// <summary>The map file's "game" values, by <see cref="CasinoGame"/>.</summary>
    public static readonly string[] Keys = ["blackjack", "roulette", "highLow", "dice"];

    /// <summary>What players read ("Bone Dice" is a cut-down dice game on the pass line).</summary>
    public static readonly string[] Names = ["Blackjack", "Roulette", "High-Low", "Bone Dice"];

    public static string Name(int game) => Names[Math.Clamp(game, 0, Names.Length - 1)];
}

/// <summary>
/// The casino games' maths (Source/Game/ZombiesCasino.cs; docs/zombies/ZOMBIES-MODE.md, "Casino tables"): cards,
/// blackjack totals and the dealer's rule, the roulette layout, its bets and payouts, high-low's prices and the dice
/// pass line. Static and allocation-free, so the self-test can enumerate every outcome and check the house edges.
/// </summary>
public static class Casino
{
    // ---- Cards: 0-51, rank * 4 + suit; rank 0 is a two ... 12 an ace; suits 0-1 red (hearts, diamonds), 2-3 black ----

    public const byte NoCard = 255;

    public static int Rank(byte card) => card / 4;

    public static int Suit(byte card) => card % 4;

    public static bool RedSuit(byte card) => Suit(card) < 2;

    private static readonly string[] RankNames = ["2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A"];
    private static readonly string[] SuitNames = ["h", "d", "c", "s"];
    private static readonly string[] CardNames = MakeCardNames();

    private static string[] MakeCardNames()
    {
        var a = new string[52];
        for (var c = 0; c < 52; c++)
            a[c] = RankNames[c / 4] + SuitNames[c % 4];
        return a;
    }

    /// <summary>"Kh", "10s", "?" for a hidden card (ASCII: h d c s for the suits).</summary>
    public static string CardName(byte card) => card < 52 ? CardNames[card] : "?";

    public static string RankName(int rank) => RankNames[Math.Clamp(rank, 0, 12)];

    /// <summary>A card's blackjack value: 2-9, 10 for tens and faces, 11 for an ace (<see cref="HandTotal"/> counts it as 1 when it must).</summary>
    public static int CardValue(byte card) => Rank(card) switch { < 8 and var r => r + 2, 12 => 11, _ => 10 };

    /// <summary>A blackjack hand's best total (aces as 11 while that doesn't bust it) and whether it is soft (an ace still counts 11).</summary>
    public static (int Total, bool Soft) HandTotal(ReadOnlySpan<byte> cards)
    {
        var total = 0;
        var aces = 0;
        foreach (var c in cards)
        {
            if (c >= 52)
                continue; // (a hidden hole card)
            total += CardValue(c);
            if (Rank(c) == 12)
                aces++;
        }
        while (total > 21 && aces > 0)
        {
            total -= 10;
            aces--;
        }
        return (total, aces > 0);
    }

    /// <summary>Two cards making 21: an ace and a ten-value card.</summary>
    public static bool IsBlackjack(ReadOnlySpan<byte> cards) => cards.Length == 2 && HandTotal(cards).Total == 21;

    /// <summary>The dealer's rule: draw to 16, stand on every 17 (soft 17 too).</summary>
    public static bool DealerDraws(ReadOnlySpan<byte> cards) => HandTotal(cards).Total < 17;

    /// <summary>
    /// What a finished blackjack hand gets back (the bet included; 0: lost): a natural pays 3:2 (unless the dealer has
    /// one too: a push), a win 1:1, a push the bet. <paramref name="bet"/> is the whole stake (doubled if it was).
    /// </summary>
    public static int BlackjackReturn(ReadOnlySpan<byte> player, ReadOnlySpan<byte> dealer, int bet)
    {
        var (p, _) = HandTotal(player);
        var (d, _) = HandTotal(dealer);
        var pbj = IsBlackjack(player);
        var dbj = IsBlackjack(dealer);
        long stake = bet; // (long: a huge bet's win must not wrap)
        if (p > 21)
            return 0;
        if (pbj)
            return dbj ? bet : Capped(stake + stake * 3 / 2);
        if (dbj)
            return 0;
        if (d > 21 || p > d)
            return Capped(stake * 2);
        return p == d ? bet : 0;
    }

    /// <summary>The most points a player can hold or be paid: the largest int (bets are capped by the points you have, so a payout can't wrap).</summary>
    public const int PointsCap = int.MaxValue;

    /// <summary>The largest bet you can make with <paramref name="points"/> at a table with bets in steps of <paramref name="step"/> ("All in": your points, down to the step).</summary>
    public static int BetCeiling(int points, int step) => Math.Max(0, points) / Math.Max(1, step) * Math.Max(1, step);

    /// <summary>A points amount from a wider sum, clamped to 0 .. <see cref="PointsCap"/>.</summary>
    public static int Capped(long points) => (int)Math.Clamp(points, 0, PointsCap);

    /// <summary>What one roulette bet of <paramref name="amount"/> on <paramref name="spot"/> returns (the stake included) when the wheel lands on <paramref name="n"/>: 0 if it loses.</summary>
    public static int RouletteBack(int spot, int amount, int n) => Covers(spot, n) ? Capped((long)amount * (Payout(spot) + 1)) : 0;

    /// <summary>Shuffles a fresh 52-card deck into <paramref name="deck"/> from <paramref name="rng"/> (Fisher-Yates; no allocation).</summary>
    public static void Shuffle(Span<byte> deck, ref uint rng)
    {
        for (var i = 0; i < deck.Length; i++)
            deck[i] = (byte)(i % 52);
        for (var i = deck.Length - 1; i > 0; i--)
        {
            var j = (int)(Next(ref rng) % (uint)(i + 1));
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    /// <summary>The tables' random numbers (xorshift32: a round is seeded from the run's seed, so the self-test can replay it).</summary>
    public static uint Next(ref uint s)
    {
        if (s == 0)
            s = 0x9E3779B9u;
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return s;
    }

    // ---- Roulette: a single-zero wheel (37 pockets: the house keeps 1 in 37 of every bet over time) ----

    /// <summary>The pockets round the wheel, clockwise from the zero.</summary>
    public static readonly byte[] WheelOrder = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26];

    private static readonly bool[] Reds = MakeReds();

    private static bool[] MakeReds()
    {
        var r = new bool[37];
        foreach (var n in new[] { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 })
            r[n] = true;
        return r;
    }

    public static bool IsRed(int n) => n is > 0 and <= 36 && Reds[n];

    public static bool IsBlack(int n) => n is > 0 and <= 36 && !Reds[n];

    /// <summary>
    /// The betting board's spots, as the wire byte a chip goes on: 0-36 a single number (straight up), then the splits
    /// across a row (n and n + 1 side by side: 24), the splits down a column (n and n + 3: 33), then red, black, odd,
    /// even, 1-18, 19-36, the three dozens and the three columns. 106 spots.
    /// </summary>
    public const int SpotHSplit = 37, SpotVSplit = SpotHSplit + 24, SpotRed = SpotVSplit + 33, SpotBlack = SpotRed + 1, SpotOdd = SpotRed + 2,
        SpotEven = SpotRed + 3, SpotLow = SpotRed + 4, SpotHigh = SpotRed + 5, SpotDozen = SpotRed + 6, SpotColumn = SpotDozen + 3, SpotCount = SpotColumn + 3;

    private static readonly byte[] HSplitLow = MakeHSplits();

    private static byte[] MakeHSplits()
    {
        var a = new byte[24];
        var k = 0;
        for (var n = 1; n <= 35; n++)
            if (n % 3 != 0)
                a[k++] = (byte)n;
        return a;
    }

    /// <summary>The spot for the split between <paramref name="a"/> and <paramref name="b"/> (side by side on the board, either order), or -1.</summary>
    public static int SplitSpot(int a, int b)
    {
        if (a > b)
            (a, b) = (b, a);
        if (a < 1 || b > 36)
            return -1;
        if (b == a + 1 && a % 3 != 0)
            return SpotHSplit + Array.IndexOf(HSplitLow, (byte)a);
        if (b == a + 3)
            return SpotVSplit + a - 1;
        return -1;
    }

    /// <summary>Does a chip on <paramref name="spot"/> win when the ball lands in <paramref name="n"/> (0-36)?</summary>
    public static bool Covers(int spot, int n)
    {
        if (spot < 0 || spot >= SpotCount || n < 0 || n > 36)
            return false;
        if (spot <= 36)
            return spot == n;
        if (spot < SpotVSplit)
        {
            var a = HSplitLow[spot - SpotHSplit];
            return n == a || n == a + 1;
        }
        if (spot < SpotRed)
        {
            var a = spot - SpotVSplit + 1;
            return n == a || n == a + 3;
        }
        if (n == 0)
            return false; // the zero loses every outside bet
        return spot switch
        {
            SpotRed => IsRed(n),
            SpotBlack => IsBlack(n),
            SpotOdd => n % 2 == 1,
            SpotEven => n % 2 == 0,
            SpotLow => n <= 18,
            SpotHigh => n >= 19,
            < SpotColumn => (n - 1) / 12 == spot - SpotDozen,
            _ => (n - 1) % 3 == spot - SpotColumn,
        };
    }

    /// <summary>What a winning chip pays "to one" (the chip comes back as well): 35 straight up, 17 a split, 2 a dozen or column, 1 the even-money bets.</summary>
    public static int Payout(int spot) => spot switch
    {
        <= 36 => 35,
        < SpotRed => 17,
        < SpotDozen => 1,
        _ => 2,
    };

    private static readonly string[] OutsideNames = ["Red", "Black", "Odd", "Even", "1-18", "19-36", "1st 12", "2nd 12", "3rd 12", "Column 1", "Column 2", "Column 3"];
    private static readonly string[] SpotNames = MakeSpotNames();

    private static string[] MakeSpotNames()
    {
        var a = new string[SpotCount];
        for (var s = 0; s < SpotCount; s++)
            a[s] = s <= 36 ? s.ToString()
                : s < SpotVSplit ? $"{HSplitLow[s - SpotHSplit]}/{HSplitLow[s - SpotHSplit] + 1}"
                : s < SpotRed ? $"{s - SpotVSplit + 1}/{s - SpotVSplit + 4}"
                : OutsideNames[s - SpotRed];
        return a;
    }

    /// <summary>"17", "8/11", "Red", "2nd 12".</summary>
    public static string SpotName(int spot) => spot >= 0 && spot < SpotCount ? SpotNames[spot] : "?";

    /// <summary>A pocket's colour name.</summary>
    public static string ColourName(int n) => n == 0 ? "green" : IsRed(n) ? "red" : "black";

    // ---- High-low: guess whether the next card is higher or lower (a tie loses; infinite deck: every rank 1 in 13) ----

    /// <summary>
    /// The pot multiplier for a correct guess from a card of <paramref name="rank"/> (0 two .. 12 ace): (1 - edge) over
    /// the guess's chance, in hundredths (rounded down); 0 when the guess can't win (higher than an ace, lower than a two).
    /// </summary>
    public static int HighLowMultiplier(int rank, bool higher, float edge)
    {
        var wins = higher ? 12 - rank : rank;
        if (wins <= 0)
            return 0;
        return (int)MathF.Floor((1f - edge) * 13f / wins * 100f + 1e-4f);
    }

    /// <summary>A pot after a correct guess (whole points, rounded down: never more than the multiplier).</summary>
    public static int HighLowPot(int pot, int multiplier) => Capped((long)pot * multiplier / 100);

    // ---- Bone Dice: the pass line (a come-out 7 or 11 wins, 2, 3 or 12 loses; else that's the point, then the point before a 7 wins) ----

    /// <summary>The pass line after a roll of <paramref name="sum"/>: +1 a win, -1 a loss, 0 rolling on (the come-out sets the point).</summary>
    public static int PassLine(ref int point, int sum)
    {
        if (point == 0)
        {
            if (sum is 7 or 11)
                return 1;
            if (sum is 2 or 3 or 12)
                return -1;
            point = sum;
            return 0;
        }
        if (sum == point)
            return 1;
        return sum == 7 ? -1 : 0;
    }
}
