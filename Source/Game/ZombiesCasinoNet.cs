using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The casino tables on the wire (protocol 66; ZombiesCasino.cs): the server sends a table's whole state in
/// <see cref="Msg.ZCasino"/> whenever it changes (and every table to a joiner); a client's moves go up in
/// <see cref="Msg.ZCasinoAct"/> (table, act, arg, amount), and the server checks everything again. The dealer's hole card
/// goes out as <see cref="Casino.NoCard"/> until it's turned over, and the deck never goes out at all.
/// </summary>
public sealed partial class Game
{
    /// <summary>Server: table <paramref name="i"/> as it is now.</summary>
    private static void WriteCasino(PacketWriter w, int i, CasinoTable t)
    {
        w.Begin(Msg.ZCasino).U8((byte)i).U8((byte)t.Game).U8((byte)t.Phase).U8(t.RoundId)
            .U16((ushort)Math.Clamp((int)MathF.Ceiling(t.Left * 10), 0, 65535))
            .U8(t.WinSeq).U8(t.WinPlayer).U32((uint)Math.Max(0, t.WinAmount));
        w.U8((byte)t.Seats.Length);
        foreach (var s in t.Seats)
        {
            w.U8(s.Player).U32((uint)Math.Max(0, s.Bet)).U8((byte)((s.Done ? 1 : 0) | (s.Doubled ? 2 : 0))).U8((byte)s.Outcome).U32((uint)Math.Max(0, s.Paid)).U8(s.Count);
            for (var c = 0; c < s.Count; c++)
                w.U8(s.Cards[c]);
        }
        w.U8(t.DealerCount);
        for (var c = 0; c < t.DealerCount; c++)
            w.U8(c == 1 && !t.HoleShown ? Casino.NoCard : t.Dealer[c]);
        w.U32((uint)Math.Max(0, t.Pot)).U8(t.Steps).U8(t.Die1).U8(t.Die2).U8(t.Point).U8(t.RollId).U8(t.Number);
        w.U8(t.BetCount);
        for (var b = 0; b < t.BetCount; b++)
            w.U8(t.BetPlayer[b]).U8(t.BetSpot[b]).U32((uint)Math.Max(0, t.BetAmount[b]));
    }

    /// <summary>Client: a <see cref="Msg.ZCasino"/> (after the id). A short or odd message changes nothing it can't read.</summary>
    private void ReadCasino(ref PacketReader r)
    {
        if (!r.Has(11))
            return;
        int i = r.U8();
        var game = (CasinoGame)Math.Min(r.U8(), (byte)CasinoGame.Dice);
        var phase = (CasinoPhase)Math.Min(r.U8(), (byte)CasinoPhase.Settled);
        var round = r.U8();
        var left = r.U16() / 10f;
        var winSeq = r.U8();
        var winPlayer = r.U8();
        var winAmount = (int)Math.Min(r.U32(), int.MaxValue);
        if (i >= ZCasino.Length)
            return;
        var t = ZCasino[i];
        t.Game = game;
        t.Phase = phase;
        t.RoundId = round;
        t.Left = left;
        t.ShowUntil = Brutal.GlfwApi.Glfw.Time + left;
        if (!r.Has(1))
            return;
        int seats = r.U8();
        for (var k = 0; k < seats; k++)
        {
            if (!r.Has(12))
                return;
            var s = k < t.Seats.Length ? t.Seats[k] : null;
            var player = r.U8();
            var bet = (int)Math.Min(r.U32(), int.MaxValue);
            var flags = r.U8();
            var outcome = (CasinoOutcome)Math.Min(r.U8(), (byte)CasinoOutcome.Forfeit);
            var paid = (int)Math.Min(r.U32(), int.MaxValue);
            int n = r.U8();
            if (s != null)
            {
                s.Player = player;
                s.Bet = bet;
                s.Done = (flags & 1) != 0;
                s.Doubled = (flags & 2) != 0;
                s.Outcome = outcome;
                s.Paid = paid;
                s.Count = 0;
            }
            for (var c = 0; c < n && r.Has(1); c++)
            {
                var card = r.U8();
                s?.Add(card);
            }
        }
        if (!r.Has(1))
            return;
        int dn = r.U8();
        t.DealerCount = 0;
        for (var c = 0; c < dn && r.Has(1); c++)
        {
            var card = r.U8();
            if (t.DealerCount < t.Dealer.Length)
                t.Dealer[t.DealerCount++] = card;
        }
        t.HoleShown = t.DealerCount < 2 || t.Dealer[1] != Casino.NoCard;
        if (!r.Has(11))
            return;
        t.Pot = (int)Math.Min(r.U32(), int.MaxValue);
        t.Steps = r.U8();
        t.Die1 = r.U8();
        t.Die2 = r.U8();
        t.Point = r.U8();
        t.RollId = r.U8();
        t.Number = r.U8();
        int nb = r.U8();
        t.BetCount = 0;
        for (var b = 0; b < nb && r.Has(6); b++)
        {
            var who = r.U8();
            var spot = r.U8();
            var amount = (int)Math.Min(r.U32(), int.MaxValue);
            if (t.BetCount >= CasinoTable.MaxBets)
                continue;
            t.BetPlayer[t.BetCount] = who;
            t.BetSpot[t.BetCount] = spot;
            t.BetAmount[t.BetCount] = amount;
            t.BetCount++;
        }
        // A big win this PC hasn't shown yet (a joiner takes the last one quietly).
        if (winSeq != t.WinSeq)
        {
            var fresh = t.WinSeq != 0 || _casinoSeen[i];
            t.WinSeq = winSeq;
            t.WinPlayer = winPlayer;
            t.WinAmount = winAmount;
            if (fresh && winSeq != 0)
                ApplyCasinoWin(t);
        }
        _casinoSeen[i] = true;
    }

    /// <summary>Client: which tables this PC has had a message for (the first one's win is old news).</summary>
    private readonly bool[] _casinoSeen = new bool[ZombiesMap.MaxCasinoTables];

    /// <summary>A client's <see cref="Msg.ZCasinoAct"/>.</summary>
    public static void WriteCasinoAct(PacketWriter w, int table, CasinoAct act, int arg, int amount) =>
        w.Begin(Msg.ZCasinoAct).U8((byte)table).U8((byte)act).U8((byte)Math.Clamp(arg, 0, 255)).U32((uint)Math.Max(0, amount));

    /// <summary>Server: a <see cref="Msg.ZCasinoAct"/> from <paramref name="p"/> (after the id); returns why not, or null.</summary>
    private string? OnCasinoAct(Player p, ref PacketReader r, double now)
    {
        int table = r.U8();
        var act = r.U8();
        int arg = r.U8();
        var amount = (int)Math.Min(r.U32(), int.MaxValue);
        if (act > (byte)CasinoAct.Leave)
            return "That's not a casino move.";
        return ServerCasino(p, table, (CasinoAct)act, arg, amount, now);
    }

    /// <summary>Server: every table to a joiner.</summary>
    private void SendCasinoTo(RakNetGUID guid)
    {
        if (World.Map.Zombies is not { } zm)
            return;
        for (var i = 0; i < zm.CasinoTables.Count && i < ZCasino.Length; i++)
        {
            WriteCasino(_w, i, ZCasino[i]);
            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    // ---- Self-test hooks -----------------------------------------------------------------------------

    /// <summary>Self-test: the <see cref="Msg.ZCasino"/> the server sends for table <paramref name="i"/>.</summary>
    public void SelfTestWriteCasino(PacketWriter w, int i) => WriteCasino(w, i, ZCasino[i]);

    /// <summary>Self-test: a client reads a whole <see cref="Msg.ZCasino"/>.</summary>
    public void SelfTestReadCasino(ReadOnlySpan<byte> msg)
    {
        var r = new PacketReader(msg[1..]);
        ReadCasino(ref r);
    }

    /// <summary>Self-test: the server gets a whole <see cref="Msg.ZCasinoAct"/> from <paramref name="p"/>.</summary>
    public string? SelfTestCasinoPacket(Player p, ReadOnlySpan<byte> msg, double now)
    {
        var r = new PacketReader(msg[1..]);
        return OnCasinoAct(p, ref r, now);
    }
}
