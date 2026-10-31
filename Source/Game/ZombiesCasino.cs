using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a casino table's round is (server-owned, mirrored in <see cref="Msg.ZCasino"/>; wire bytes).</summary>
public enum CasinoPhase : byte
{
    /// <summary>Nobody has bet: the first bet starts a round.</summary>
    Open,

    /// <summary>Blackjack: waiting a moment for other seats before the deal. Roulette: chips going down before the spin.</summary>
    Betting,

    /// <summary>Hands being played (blackjack, high-low, dice).</summary>
    Playing,

    /// <summary>Roulette: the wheel turns (the pocket is already rolled: clients land the ball on it).</summary>
    Spinning,

    /// <summary>Paid; the result shows for a moment, then the table opens again.</summary>
    Settled,
}

/// <summary>What a player can do at a table (<see cref="Msg.ZCasinoAct"/>; wire bytes).</summary>
public enum CasinoAct : byte
{
    /// <summary>Bet the amount (blackjack: take a seat; roulette: a chip on the arg spot; high-low and dice: start a hand).</summary>
    Bet,
    Hit,
    Stand,
    Double,
    Higher,
    Lower,
    CashOut,
    Roll,

    /// <summary>Roulette, while bets go down: take your chips back.</summary>
    Clear,

    /// <summary>Blackjack before the deal: take your bet back and leave the seat.</summary>
    Leave,
}

/// <summary>How a hand ended (wire bytes).</summary>
public enum CasinoOutcome : byte
{
    None,
    Lose,
    Push,
    Win,
    Blackjack,
    Bust,

    /// <summary>Walked away (or went down) with the hand unfinished: the bet is lost.</summary>
    Forfeit,
}

/// <summary>A seat at a table: who sits there, the bet, the hand. High-low and dice use seat 0 only (its cards: high-low's run of cards).</summary>
public sealed class CasinoSeat
{
    public const int MaxCards = 12;

    public byte Player = byte.MaxValue;
    public int Bet;
    public readonly byte[] Cards = new byte[MaxCards];
    public byte Count;
    public bool Done, Doubled;
    public CasinoOutcome Outcome;

    /// <summary>What the hand paid back (the bet included; 0 lost).</summary>
    public int Paid;

    public bool Taken => Player != byte.MaxValue;

    public ReadOnlySpan<byte> Hand => Cards.AsSpan(0, Count);

    public void Clear()
    {
        Player = byte.MaxValue;
        Bet = 0;
        Count = 0;
        Done = Doubled = false;
        Outcome = CasinoOutcome.None;
        Paid = 0;
    }

    public void Add(byte card)
    {
        if (Count < MaxCards)
            Cards[Count++] = card;
    }
}

/// <summary>One casino table on the map: its round, seats, the dealer's hand, roulette's chips, the dice, the last big win.</summary>
public sealed class CasinoTable
{
    public const int MaxSeats = 4, MaxBets = 48;

    public CasinoGame Game;
    public CasinoPhase Phase;

    /// <summary>When the phase ends (server clock).</summary>
    public double Until;

    /// <summary>Seconds left in the phase (the server works it out every tick; clients get it in <see cref="Msg.ZCasino"/>).</summary>
    public float Left;

    /// <summary>Counts rounds (wrapping), so a client sees a new deal or spin.</summary>
    public byte RoundId;

    /// <summary>The round's random numbers (seeded from the run's seed at each round's start).</summary>
    public uint Rng;

    public readonly CasinoSeat[] Seats = MakeSeats();

    public readonly byte[] Dealer = new byte[CasinoSeat.MaxCards];
    public byte DealerCount;

    /// <summary>The dealer's second card is face up (it is sent as <see cref="Casino.NoCard"/> until then).</summary>
    public bool HoleShown;

    /// <summary>Blackjack: the shuffled deck and the next card in it (server only).</summary>
    public readonly byte[] Deck = new byte[52];
    public byte DeckAt;

    // Roulette: the chips on the board (who, which spot, how much) and the pocket the ball landed in (255: none yet).
    public readonly byte[] BetPlayer = new byte[MaxBets], BetSpot = new byte[MaxBets];
    public readonly int[] BetAmount = new int[MaxBets];
    public byte BetCount;
    public byte Number = byte.MaxValue;

    // High-low: the pot riding on the next guess and the correct guesses so far.
    public int Pot;
    public byte Steps;

    // Bone Dice: the last roll, the point (0: the come-out) and a counter of rolls (wrapping).
    public byte Die1, Die2, Point, RollId;

    // The last big win, as everyone saw it: WinSeq counts them (0: none yet), who, how much over the bet, when it came in (this PC's clock).
    public byte WinSeq, WinPlayer = byte.MaxValue;
    public int WinAmount;
    public double WinAt = -1e9;

    /// <summary>Server: changed since it was last sent.</summary>
    public bool Dirty;

    // Client: when this PC's copy of the phase ends (Glfw time), and the animation's start for the round / roll seen.
    public double ShowUntil;
    public byte SeenRound, SeenRoll;
    public double SpinStart = -1e9, RollStart = -1e9;

    private static CasinoSeat[] MakeSeats()
    {
        var a = new CasinoSeat[MaxSeats];
        for (var i = 0; i < a.Length; i++)
            a[i] = new CasinoSeat();
        return a;
    }

    public ReadOnlySpan<byte> DealerHand => Dealer.AsSpan(0, DealerCount);

    /// <summary>The seat <paramref name="player"/> holds, or -1.</summary>
    public int SeatOf(byte player)
    {
        for (var i = 0; i < Seats.Length; i++)
            if (Seats[i].Player == player)
                return i;
        return -1;
    }

    /// <summary>Everything <paramref name="player"/> has on the roulette board.</summary>
    public int BetsOf(byte player)
    {
        var n = 0;
        for (var i = 0; i < BetCount; i++)
            if (BetPlayer[i] == player)
                n += BetAmount[i];
        return n;
    }

    /// <summary>Back to an open table (keeps the win display and the counters).</summary>
    public void ClearRound()
    {
        Phase = CasinoPhase.Open;
        Until = 0;
        Left = 0;
        foreach (var s in Seats)
            s.Clear();
        DealerCount = 0;
        HoleShown = false;
        DeckAt = 0;
        BetCount = 0;
        Pot = 0;
        Steps = 0;
        Point = 0;
    }

    public void Reset()
    {
        ClearRound();
        RoundId = 0;
        Rng = 0;
        Number = byte.MaxValue;
        Die1 = Die2 = 0;
        RollId = 0;
        WinSeq = 0;
        WinPlayer = byte.MaxValue;
        WinAmount = 0;
        WinAt = -1e9;
        Dirty = true;
        ShowUntil = 0;
        SeenRound = SeenRoll = 0;
        SpinStart = RollStart = -1e9;
    }
}

/// <summary>
/// Zombies casino tables (board item "Zombies: casino games to bet points", 2026-10-27; docs/zombies/ZOMBIES-MODE.md,
/// "Casino tables"): a map's "casino" tables take bets of POINTS only (the run's currency: no money, no progression).
/// Four games, every one played in several steps at a panel opened with the interact key while the world goes on
/// (zombies keep coming): <b>blackjack</b> (up to four seats against one dealer: hit, stand or double; the dealer draws
/// to 16 and stands on every 17; a natural pays 3:2), <b>roulette</b> (a single-zero wheel: chips on numbers, splits,
/// colours, odd / even, halves, dozens and columns from everyone at the table, then one spin; standard payouts),
/// <b>high-low</b> (guess whether the next card is higher or lower; each correct guess multiplies the pot by (1 - edge)
/// over its chance; cash out when you like) and <b>Bone Dice</b> (the pass line: 7 or 11 wins the come-out, 2, 3 or 12
/// loses, otherwise roll the point again before a 7). Server-authoritative and seeded per round from the run's seed;
/// the house keeps a small edge like the real games (the self-test works every one out). Walking away from (or going
/// down in) an unfinished hand forfeits its bet; roulette chips ride the spin. Bots ignore the tables. Nothing here
/// allocates per tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>Every table's state (only the first <see cref="ZombiesMap.CasinoTables"/>.Count are used).</summary>
    public readonly CasinoTable[] ZCasino = MakeCasinoTables();

    private static CasinoTable[] MakeCasinoTables()
    {
        var a = new CasinoTable[ZombiesMap.MaxCasinoTables];
        for (var i = 0; i < a.Length; i++)
            a[i] = new CasinoTable();
        return a;
    }

    private void ResetCasino()
    {
        var zm = World.Map.Zombies;
        for (var i = 0; i < ZCasino.Length; i++)
        {
            ZCasino[i].Reset();
            ZCasino[i].Game = zm != null && i < zm.CasinoTables.Count ? (CasinoGame)zm.CasinoTables[i].Game : CasinoGame.Blackjack;
            _casinoSeen[i] = false;
        }
    }

    /// <summary>Table <paramref name="i"/>'s smallest bet (its own, else the rules' for its game). There is no largest: a bet can be any amount up to the points you have.</summary>
    public int CasinoMinBet(ZombiesMap zm, int i)
    {
        if (i < 0 || i >= zm.CasinoTables.Count)
            return 0;
        var s = zm.CasinoTables[i];
        return s.BetMin > 0 ? s.BetMin : ZRules.CasinoMin[Math.Clamp(s.Game, 0, 3)];
    }

    /// <summary>The interact prompt at table <paramref name="i"/> (FindZUse): press to open its panel.</summary>
    private ZUse CasinoUse(ZombiesMap zm, int i)
    {
        var s = zm.CasinoTables[i];
        var name = CasinoGames.Name(s.Game);
        if (s.NeedsPower && !ZPower)
            return new ZUse(ZUseKind.Casino, i, 0, TextCache.Of($"{name}: needs the power on"), Blocked: true);
        return new ZUse(ZUseKind.Casino, i, 0, TextCache.Of($"Press [E] to play {name} (bets from {CasinoMinBet(zm, i)} points)"));
    }

    /// <summary>Is <paramref name="p"/> close enough to table <paramref name="i"/> to play it?</summary>
    public bool AtCasinoTable(Player p, int i) => World.Map.Zombies is { } zm && i >= 0 && i < zm.CasinoTables.Count && Flat(zm.CasinoTables[i].At - p.Position) <= ZRules.CasinoUseRange + 0.3f && ZReachSpot(p, zm.CasinoTables[i].At); // (the reach rule: ZombiesReach.cs)

    // ---- Requests ------------------------------------------------------------------------------------------

    /// <summary>The local player's move at table <paramref name="table"/>: done now on the host, sent to the server by a client.</summary>
    public void CasinoRequest(int table, CasinoAct act, int arg, int amount)
    {
        if (Local is not { } me)
            return;
        if (IsServer)
        {
            if (ServerCasino(me, table, act, arg, amount, _zNow) is { } why)
                LoadoutNotice = why;
            return;
        }
        WriteCasinoAct(_w, table, act, arg, amount);
        Net.SendToServer(_w.Span, Brutal.RakNetApi.PacketReliability.ReliableOrdered);
    }

    /// <summary>
    /// Server: <paramref name="p"/> does <paramref name="act"/> at table <paramref name="i"/> (<paramref name="arg"/>: a
    /// roulette spot; <paramref name="amount"/>: a bet). Returns why not, or null. Points are taken before anything is
    /// dealt, and never more than you have.
    /// </summary>
    public string? ServerCasino(Player p, int i, CasinoAct act, int arg, int amount, double now)
    {
        if (ZMap is not { } zm || i < 0 || i >= zm.CasinoTables.Count || i >= ZCasino.Length)
            return "That's not a casino table.";
        var spot = zm.CasinoTables[i];
        if (!p.Alive || IsDowned(p) || Phase != MatchPhase.Playing)
            return "You can't play right now.";
        if (spot.NeedsPower && !ZPower)
            return $"{CasinoGames.Name(spot.Game)}: needs the power on.";
        if (!AtCasinoTable(p, i))
            return "You're not at the table.";
        if (LogicLocksZ(LogicKind.ZMachine, LogicMachine.Casino, i))
            return "The table is closed."; // a disabled zmachine (map logic, GameLogic.cs)
        var t = ZCasino[i];
        var zp = ZP(p);
        string? why;
        if (act == CasinoAct.Bet)
        {
            var min = CasinoMinBet(zm, i);
            var step = Math.Max(1, ZRules.CasinoBetStep);
            if (amount <= 0 || amount % step != 0)
                return $"Bets go in steps of {step} points.";
            if (amount > zp.Points)
                return $"You only have {zp.Points} points.";
            why = t.Game switch
            {
                CasinoGame.Blackjack => BlackjackBet(t, p, amount, min, now),
                CasinoGame.Roulette => RouletteBet(t, p, arg, amount, min, now),
                _ => SoloBet(t, p, amount, min, now),
            };
            if (why == null)
            {
                zp.Points -= amount; // (checked above: never below 0)
                t.Dirty = true;
                LogicZMachineUsed(LogicMachine.Casino, i, p); // map logic: a zmachine "casino" sends "used" on every bet (GameLogic.cs)
            }
            return why;
        }
        why = t.Game switch
        {
            CasinoGame.Blackjack => BlackjackAct(t, p, zp, act, now),
            CasinoGame.Roulette => RouletteAct(t, p, act),
            CasinoGame.HighLow => HighLowAct(t, p, act, now),
            _ => DiceAct(t, p, act, now),
        };
        if (why == null)
            t.Dirty = true;
        return why;
    }

    private string? BlackjackBet(CasinoTable t, Player p, int amount, int min, double now)
    {
        if (t.Phase is not (CasinoPhase.Open or CasinoPhase.Betting))
            return "Wait for this hand to finish.";
        if (t.SeatOf(p.Id) >= 0)
            return "You already have a seat at this table.";
        if (amount < min)
            return $"This table's smallest bet is {min}.";
        var seat = -1;
        for (var s = 0; s < t.Seats.Length && seat < 0; s++)
            if (!t.Seats[s].Taken)
                seat = s;
        if (seat < 0)
            return "The table is full.";
        t.Seats[seat].Player = p.Id;
        t.Seats[seat].Bet = amount;
        if (t.Phase == CasinoPhase.Open)
        {
            t.Phase = CasinoPhase.Betting;
            t.Until = now + ZRules.CasinoDealSeconds;
        }
        var full = true;
        foreach (var s in t.Seats)
            full &= s.Taken;
        if (full)
            t.Until = now; // every seat bet: deal at once
        return null;
    }

    private string? RouletteBet(CasinoTable t, Player p, int spot, int amount, int min, double now)
    {
        if (t.Phase is not (CasinoPhase.Open or CasinoPhase.Betting))
            return "No more bets: wait for the next spin.";
        if (spot < 0 || spot >= Casino.SpotCount)
            return "That's not a spot on the board.";
        var mine = t.BetsOf(p.Id);
        if (mine == 0 && amount < min)
            return $"This table's smallest bet is {min}.";
        var k = -1;
        for (var b = 0; b < t.BetCount; b++)
            if (t.BetPlayer[b] == p.Id && t.BetSpot[b] == spot)
                k = b;
        if (k < 0)
        {
            if (t.BetCount >= CasinoTable.MaxBets)
                return "The board is full.";
            k = t.BetCount++;
            t.BetPlayer[k] = p.Id;
            t.BetSpot[k] = (byte)spot;
            t.BetAmount[k] = 0;
        }
        t.BetAmount[k] += amount;
        if (t.Phase == CasinoPhase.Open)
        {
            t.Phase = CasinoPhase.Betting;
            t.Until = now + ZRules.CasinoRouletteBetSeconds;
            t.Number = byte.MaxValue;
        }
        return null;
    }

    /// <summary>High-low and dice: one player at a time; the bet starts the hand.</summary>
    private string? SoloBet(CasinoTable t, Player p, int amount, int min, double now)
    {
        if (t.Phase != CasinoPhase.Open || t.Seats[0].Taken)
            return t.Seats[0].Player == p.Id ? "Finish this hand first." : "Someone is playing: wait for them to finish.";
        if (amount < min)
            return $"This table's smallest bet is {min}.";
        var s = t.Seats[0];
        s.Clear();
        s.Player = p.Id;
        s.Bet = amount;
        t.RoundId++;
        t.Rng = ZNext();
        t.Phase = CasinoPhase.Playing;
        t.Until = now + ZRules.CasinoActSeconds;
        if (t.Game == CasinoGame.HighLow)
        {
            t.Pot = amount;
            t.Steps = 0;
            s.Add(DrawAny(t));
        }
        else
            t.Point = 0;
        return null;
    }

    /// <summary>A card from an endless deck (high-low: every rank 1 in 13 every time).</summary>
    private static byte DrawAny(CasinoTable t) => (byte)(Casino.Next(ref t.Rng) % 52);

    private static byte DrawDeck(CasinoTable t)
    {
        if (t.DeckAt >= t.Deck.Length)
        {
            Casino.Shuffle(t.Deck, ref t.Rng); // (can't happen with four seats; just in case)
            t.DeckAt = 0;
        }
        return t.Deck[t.DeckAt++];
    }

    // ---- Blackjack ----------------------------------------------------------------------------------------

    private string? BlackjackAct(CasinoTable t, Player p, ZPlayer zp, CasinoAct act, double now)
    {
        var k = t.SeatOf(p.Id);
        if (k < 0)
            return "You don't have a seat: bet to sit down.";
        var s = t.Seats[k];
        if (act == CasinoAct.Leave)
        {
            if (t.Phase != CasinoPhase.Betting)
                return "Play the hand out (walking away forfeits it).";
            AddPoints(p, s.Bet); // before the deal: the bet back
            s.Clear();
            if (!AnySeat(t))
                t.ClearRound();
            return null;
        }
        if (t.Phase != CasinoPhase.Playing || s.Done)
            return "It's not your turn.";
        switch (act)
        {
            case CasinoAct.Hit:
                s.Add(DrawDeck(t));
                break;
            case CasinoAct.Stand:
                s.Done = true;
                break;
            case CasinoAct.Double:
                if (s.Count != 2)
                    return "You can only double on your first two cards.";
                if (zp.Points < s.Bet)
                    return $"Doubling needs {s.Bet} more points.";
                if ((long)s.Bet * 2 > Casino.PointsCap)
                    return "That double is more than the points ceiling.";
                zp.Points -= s.Bet;
                s.Bet *= 2;
                s.Doubled = true;
                s.Add(DrawDeck(t));
                s.Done = true;
                break;
            default:
                return "Hit, stand or double.";
        }
        if (Casino.HandTotal(s.Hand).Total >= 21)
            s.Done = true; // bust, or 21: nothing more to do
        if (AllDone(t))
            SettleBlackjack(t, now);
        return null;
    }

    private static bool AnySeat(CasinoTable t)
    {
        foreach (var s in t.Seats)
            if (s.Taken)
                return true;
        return false;
    }

    private static bool AllDone(CasinoTable t)
    {
        foreach (var s in t.Seats)
            if (s.Taken && !s.Done)
                return false;
        return true;
    }

    /// <summary>Self-test: the next blackjack deal uses these cards first (in dealing order), then the shuffle's.</summary>
    public byte[]? SelfTestStackedDeck;

    private void DealBlackjack(CasinoTable t, double now)
    {
        t.RoundId++;
        t.Rng = ZNext();
        Casino.Shuffle(t.Deck, ref t.Rng);
        if (SelfTestStackedDeck is { } stacked)
        {
            for (var c = 0; c < stacked.Length && c < t.Deck.Length; c++)
                t.Deck[c] = stacked[c];
            SelfTestStackedDeck = null;
        }
        t.DeckAt = 0;
        t.DealerCount = 0;
        t.HoleShown = false;
        for (var round = 0; round < 2; round++)
        {
            foreach (var s in t.Seats)
                if (s.Taken)
                    s.Add(DrawDeck(t));
            t.Dealer[t.DealerCount++] = DrawDeck(t);
        }
        t.Phase = CasinoPhase.Playing;
        t.Until = now + ZRules.CasinoTurnSeconds;
        foreach (var s in t.Seats)
            if (s.Taken && Casino.IsBlackjack(s.Hand))
                s.Done = true; // a natural: nothing to decide
        // The dealer peeks with an ace or a ten up: a natural ends the hand at once (no one plays into it).
        if (Casino.IsBlackjack(t.DealerHand))
            foreach (var s in t.Seats)
                s.Done = true;
        t.Dirty = true;
        if (AllDone(t))
            SettleBlackjack(t, now);
    }

    private void SettleBlackjack(CasinoTable t, double now)
    {
        t.HoleShown = true;
        // The dealer only draws when a hand is still in it (not every seat bust, forfeit or a natural).
        var live = false;
        foreach (var s in t.Seats)
            if (s.Taken && s.Outcome != CasinoOutcome.Forfeit && Casino.HandTotal(s.Hand).Total <= 21 && !Casino.IsBlackjack(s.Hand))
                live = true;
        if (live && !Casino.IsBlackjack(t.DealerHand))
            while (t.DealerCount < CasinoSeat.MaxCards && Casino.DealerDraws(t.DealerHand))
                t.Dealer[t.DealerCount++] = DrawDeck(t);
        var dealer = Casino.HandTotal(t.DealerHand).Total;
        foreach (var s in t.Seats)
        {
            if (!s.Taken)
                continue;
            var total = Casino.HandTotal(s.Hand).Total;
            if (s.Outcome == CasinoOutcome.Forfeit)
                s.Paid = 0;
            else
            {
                s.Paid = Casino.BlackjackReturn(s.Hand, t.DealerHand, s.Bet);
                s.Outcome = total > 21 ? CasinoOutcome.Bust
                    : s.Paid > 2L * s.Bet ? CasinoOutcome.Blackjack
                    : s.Paid > s.Bet ? CasinoOutcome.Win
                    : s.Paid == s.Bet ? CasinoOutcome.Push : CasinoOutcome.Lose;
            }
            s.Done = true;
            CasinoPay(t, s.Player, s.Paid, s.Bet, s.Outcome switch
            {
                CasinoOutcome.Blackjack => $"Blackjack! You win {s.Paid - s.Bet} (3:2).",
                CasinoOutcome.Win => $"Blackjack: you win {s.Paid - s.Bet} ({total} to the dealer's {(dealer > 21 ? "bust" : dealer)}).",
                CasinoOutcome.Push => $"Blackjack: a push ({total} each): your {s.Bet} back.",
                CasinoOutcome.Bust => $"Blackjack: bust ({total}). You lose {s.Bet}.",
                CasinoOutcome.Forfeit => $"Blackjack: you walked away from the hand and lost {s.Bet}.",
                _ => $"Blackjack: the dealer wins ({(Casino.IsBlackjack(t.DealerHand) ? "blackjack" : dealer)} to {total}). You lose {s.Bet}.",
            });
        }
        t.Phase = CasinoPhase.Settled;
        t.Until = now + ZRules.CasinoShowSeconds;
        t.Dirty = true;
    }

    // ---- Roulette -----------------------------------------------------------------------------------------

    private string? RouletteAct(CasinoTable t, Player p, CasinoAct act)
    {
        if (act != CasinoAct.Clear)
            return "Put chips on the board.";
        if (t.Phase != CasinoPhase.Betting)
            return "No more bets: the wheel is turning.";
        var back = 0;
        var n = 0;
        for (var b = 0; b < t.BetCount; b++)
        {
            if (t.BetPlayer[b] == p.Id)
            {
                back += t.BetAmount[b];
                continue;
            }
            t.BetPlayer[n] = t.BetPlayer[b];
            t.BetSpot[n] = t.BetSpot[b];
            t.BetAmount[n] = t.BetAmount[b];
            n++;
        }
        if (back == 0)
            return "You have no chips down.";
        t.BetCount = (byte)n;
        AddPoints(p, back);
        if (n == 0)
            t.ClearRound();
        return null;
    }

    private void SpinRoulette(CasinoTable t, double now)
    {
        t.RoundId++;
        t.Rng = ZNext();
        t.Number = (byte)(Casino.Next(ref t.Rng) % 37);
        t.Phase = CasinoPhase.Spinning;
        t.Until = now + ZRules.CasinoSpinSeconds;
        t.Dirty = true;
    }

    private void SettleRoulette(CasinoTable t, double now)
    {
        var n = t.Number;
        // Pay every player once (all their chips together), in the order they first bet.
        for (var b = 0; b < t.BetCount; b++)
        {
            var who = t.BetPlayer[b];
            var first = true;
            for (var e = 0; e < b; e++)
                first &= t.BetPlayer[e] != who;
            if (!first)
                continue;
            int staked = 0;
            long back = 0; // (long: a win on a big bet can pass int.MaxValue before the cap)
            for (var e = b; e < t.BetCount; e++)
                if (t.BetPlayer[e] == who)
                {
                    staked += t.BetAmount[e];
                    back += Casino.RouletteBack(t.BetSpot[e], t.BetAmount[e], n);
                }
            var paid = Casino.Capped(back);
            CasinoPay(t, who, paid, staked, paid > staked ? $"Roulette: {n} {Casino.ColourName(n)}. You win {paid - staked} (staked {staked})."
                : paid > 0 ? $"Roulette: {n} {Casino.ColourName(n)}. {paid} back of your {staked}."
                : $"Roulette: {n} {Casino.ColourName(n)}. You lose {staked}.");
        }
        t.Phase = CasinoPhase.Settled;
        t.Until = now + ZRules.CasinoShowSeconds;
        t.Dirty = true;
    }

    // ---- High-low -----------------------------------------------------------------------------------------

    private string? HighLowAct(CasinoTable t, Player p, CasinoAct act, double now)
    {
        var s = t.Seats[0];
        if (t.Phase != CasinoPhase.Playing || s.Player != p.Id)
            return "Bet to start a hand.";
        switch (act)
        {
            case CasinoAct.Higher or CasinoAct.Lower:
            {
                var cur = Casino.Rank(s.Cards[s.Count - 1]);
                var mult = Casino.HighLowMultiplier(cur, act == CasinoAct.Higher, ZRules.HighLowEdge);
                if (mult <= 0)
                    return act == CasinoAct.Higher ? "Nothing is higher than an ace." : "Nothing is lower than a two.";
                var next = DrawAny(t);
                if (s.Count >= CasinoSeat.MaxCards)
                {
                    Array.Copy(s.Cards, 1, s.Cards, 0, CasinoSeat.MaxCards - 1); // (keep the latest run of cards)
                    s.Count--;
                }
                s.Add(next);
                var r = Casino.Rank(next);
                if (act == CasinoAct.Higher ? r > cur : r < cur)
                {
                    t.Pot = Casino.HighLowPot(t.Pot, mult);
                    t.Steps++;
                    if (t.Steps >= ZRules.HighLowMaxSteps)
                        SettleHighLow(t, now, $"High-low: {ZRules.HighLowMaxSteps} right in a row! The table cashes you out.");
                    else
                        t.Until = now + ZRules.CasinoActSeconds;
                }
                else
                {
                    s.Outcome = CasinoOutcome.Lose;
                    s.Paid = 0;
                    SettleHighLow(t, now, r == cur ? $"High-low: a {Casino.RankName(r)} again (a tie loses). You lose {s.Bet}." : $"High-low: a {Casino.RankName(r)}. You lose {s.Bet}.");
                }
                return null;
            }
            case CasinoAct.CashOut:
                SettleHighLow(t, now, "");
                return null;
            default:
                return "Higher, lower or cash out.";
        }
    }

    /// <summary>The hand ends: a loss (Outcome already set) pays nothing; anything else cashes the pot out.</summary>
    private void SettleHighLow(CasinoTable t, double now, string line)
    {
        var s = t.Seats[0];
        if (s.Outcome is not (CasinoOutcome.Lose or CasinoOutcome.Forfeit))
        {
            s.Paid = t.Pot;
            s.Outcome = t.Pot > s.Bet ? CasinoOutcome.Win : CasinoOutcome.Push;
            if (line.Length == 0)
                line = t.Pot > s.Bet ? $"High-low: cashed out {t.Pot} ({t.Steps} right): you win {t.Pot - s.Bet}." : $"High-low: cashed out: your {s.Bet} back.";
            else
                line += $" {t.Pot} paid: you win {t.Pot - s.Bet}.";
        }
        s.Done = true;
        CasinoPay(t, s.Player, s.Paid, s.Bet, line);
        t.Phase = CasinoPhase.Settled;
        t.Until = now + ZRules.CasinoShowSeconds;
        t.Dirty = true;
    }

    // ---- Bone Dice (the pass line) ----------------------------------------------------------------------

    private string? DiceAct(CasinoTable t, Player p, CasinoAct act, double now)
    {
        var s = t.Seats[0];
        if (t.Phase != CasinoPhase.Playing || s.Player != p.Id)
            return "Bet to start a hand.";
        if (act != CasinoAct.Roll)
            return "Roll the dice.";
        RollDice(t, now);
        return null;
    }

    private void RollDice(CasinoTable t, double now)
    {
        var s = t.Seats[0];
        t.Die1 = (byte)(1 + Casino.Next(ref t.Rng) % 6);
        t.Die2 = (byte)(1 + Casino.Next(ref t.Rng) % 6);
        t.RollId++;
        var sum = t.Die1 + t.Die2;
        int point = t.Point;
        var res = Casino.PassLine(ref point, sum);
        var hadPoint = t.Point != 0;
        t.Point = (byte)point;
        t.Dirty = true;
        if (res == 0)
        {
            t.Until = now + ZRules.CasinoActSeconds;
            return;
        }
        s.Paid = res > 0 ? Casino.Capped(2L * s.Bet) : 0;
        s.Outcome = res > 0 ? CasinoOutcome.Win : CasinoOutcome.Lose;
        s.Done = true;
        CasinoPay(t, s.Player, s.Paid, s.Bet, res > 0
            ? hadPoint ? $"Bone Dice: {sum}, the point! You win {s.Bet}." : $"Bone Dice: a natural {sum}! You win {s.Bet}."
            : hadPoint ? $"Bone Dice: seven out. You lose {s.Bet}." : $"Bone Dice: craps ({sum}). You lose {s.Bet}.");
        t.Phase = CasinoPhase.Settled;
        t.Until = now + ZRules.CasinoShowSeconds;
    }

    // ---- Paying, forfeits, the tick -------------------------------------------------------------------------

    /// <summary>Server: <paramref name="back"/> points (the bet included) to <paramref name="player"/>, their line in the feed, and a big win for everyone to see.</summary>
    private void CasinoPay(CasinoTable t, byte player, int back, int staked, string line)
    {
        if (!Players.TryGetValue(player, out var p))
            return; // (left the game: nothing to pay)
        if (back > 0)
            AddPoints(p, back);
        ZFeedTo(p, line, back > staked ? Sfx.LevelUp : back > 0 ? Sfx.HitTick : Sfx.DryFire);
        if (back - staked >= Math.Max(1, ZRules.CasinoAnnounceAt))
        {
            t.WinSeq = (byte)(t.WinSeq == 255 ? 1 : t.WinSeq + 1);
            t.WinPlayer = player;
            t.WinAmount = back - staked;
            ApplyCasinoWin(t);
        }
    }

    /// <summary>
    /// Server, every tick: rounds move on when their time is up (the deal, the spin, a hand left to stand / cash out / roll
    /// on its own); a player who walked away from an unfinished hand (or went down, or left) forfeits it; changed tables
    /// go to every client. No allocation.
    /// </summary>
    private void TickCasino(ZombiesMap zm, double now)
    {
        for (var i = 0; i < zm.CasinoTables.Count && i < ZCasino.Length; i++)
        {
            var t = ZCasino[i];
            var at = zm.CasinoTables[i].At;
            // Forfeits: seated players who went away (roulette chips ride the spin).
            if (t.Game != CasinoGame.Roulette && t.Phase is CasinoPhase.Betting or CasinoPhase.Playing)
                for (var k = 0; k < t.Seats.Length; k++)
                {
                    var s = t.Seats[k];
                    if (!s.Taken || s.Done)
                        continue;
                    var gone = !Players.TryGetValue(s.Player, out var p) || !p.Alive || IsDowned(p) || Flat(at - p.Position) > ZRules.CasinoLeaveRange;
                    if (!gone)
                        continue;
                    if (t.Phase == CasinoPhase.Betting)
                    {
                        if (p != null && p.Alive)
                            AddPoints(p, s.Bet); // before the deal: no hand yet, the bet comes back
                        s.Clear();
                        if (!AnySeat(t))
                            t.ClearRound();
                    }
                    else
                    {
                        s.Outcome = CasinoOutcome.Forfeit;
                        s.Paid = 0;
                        s.Done = true;
                        if (t.Game == CasinoGame.HighLow)
                            SettleHighLow(t, now, $"High-low: you walked away from the hand and lost {s.Bet}.");
                        else if (t.Game == CasinoGame.Dice)
                        {
                            CasinoPay(t, s.Player, 0, s.Bet, $"Bone Dice: you walked away from the roll and lost {s.Bet}.");
                            t.Phase = CasinoPhase.Settled;
                            t.Until = now + ZRules.CasinoShowSeconds;
                        }
                        else if (AllDone(t))
                            SettleBlackjack(t, now);
                    }
                    t.Dirty = true;
                }
            if (now >= t.Until)
                switch (t.Phase)
                {
                    case CasinoPhase.Betting when t.Game == CasinoGame.Blackjack:
                        DealBlackjack(t, now);
                        break;
                    case CasinoPhase.Betting:
                        SpinRoulette(t, now);
                        break;
                    case CasinoPhase.Spinning:
                        SettleRoulette(t, now);
                        break;
                    case CasinoPhase.Playing when t.Game == CasinoGame.Blackjack:
                        foreach (var s in t.Seats)
                            s.Done = true; // out of time: everyone still deciding stands
                        SettleBlackjack(t, now);
                        break;
                    case CasinoPhase.Playing when t.Game == CasinoGame.HighLow:
                        SettleHighLow(t, now, ""); // out of time: cashed out
                        break;
                    case CasinoPhase.Playing:
                        RollDice(t, now); // out of time: the dice roll themselves
                        break;
                    case CasinoPhase.Settled:
                        t.ClearRound();
                        t.Dirty = true;
                        break;
                }
            t.Left = t.Phase == CasinoPhase.Open ? 0 : (float)Math.Max(0, t.Until - now);
            t.ShowUntil = Brutal.GlfwApi.Glfw.Time + t.Left;
            if (t.Dirty)
            {
                t.Dirty = false;
                if (!Net.IsOffline)
                {
                    WriteCasino(_w, i, t);
                    Net.Broadcast(_w.Span, Brutal.RakNetApi.PacketReliability.ReliableOrdered);
                }
            }
        }
    }

    /// <summary>Both sides: table <paramref name="t"/>'s new big win shows (a feed line for everyone, a sound, the label over the table).</summary>
    private void ApplyCasinoWin(CasinoTable t)
    {
        t.WinAt = Brutal.GlfwApi.Glfw.Time;
        var i = Array.IndexOf(ZCasino, t);
        var mine = t.WinPlayer == LocalId;
        var line = $"{(mine ? "You" : NameOf(t.WinPlayer))} won {t.WinAmount} points at {CasinoGames.Name((int)t.Game)}!";
        Feed.Add(new FeedEntry(Brutal.GlfwApi.Glfw.Time, line, mine, new Vector3(1f, 0.82f, 0.25f)));
        if (World.Map.Zombies is { } zm && i >= 0 && i < zm.CasinoTables.Count)
            Audio.Effect3D(Sfx.StreakReady, zm.CasinoTables[i].At + new Vector3(0, 1.4f, 0), 0.9f);
    }

    /// <summary>Is table <paramref name="i"/>'s last big win still showing at <paramref name="now"/> (this PC's clock)?</summary>
    public bool CasinoWinShowing(int i, double now) => i >= 0 && i < ZCasino.Length && ZCasino[i].WinSeq != 0 && now - ZCasino[i].WinAt < ZRules.SlotWinSeconds;

    // ---- Self-test hooks --------------------------------------------------------------------------------

    /// <summary>Self-test: the tables' server tick alone.</summary>
    public void SelfTestTickCasino(double now)
    {
        if (ZMap is { } zm)
            TickCasino(zm, now);
    }
}
