using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The casino table panel in a Zombies run (Source/Game/ZombiesCasino.cs; docs/zombies/ZOMBIES-MODE.md, "Casino
/// tables"): opened with the interact key at a table, over the match (the world goes on: zombies keep coming, and the
/// HUD stays up). The bet chooser (within the table's limits and your points), then the game's own steps: blackjack's
/// hit / stand / double, roulette's betting board (numbers, splits, colours, odd / even, halves, dozens, columns), high-
/// low's higher / lower / cash out and Bone Dice's roll. The server decides everything; this only shows its copy and
/// asks. Esc or Close goes back to the fight; walking away from an unfinished hand forfeits it. Every line is ASCII.
/// </summary>
public sealed partial class Ui
{
    /// <summary>The table whose panel is open (-1: none).</summary>
    public int CasinoTable = -1;

    public bool CasinoOpen => CasinoTable >= 0;

    private int _casinoBet;
    private bool _splitMode;
    private int _splitFirst = -1;
    private readonly StringBuilder _cs = new();

    public void OpenCasino(Game game, int table)
    {
        CasinoTable = table;
        _splitFirst = -1;
        if (World.Map.Zombies is { } zm)
        {
            var min = game.CasinoMinBet(zm, table);
            if (_casinoBet < min)
                _casinoBet = min;
        }
    }

    public void CloseCasino()
    {
        CasinoTable = -1;
        _splitFirst = -1;
    }

    private static readonly float4 FeltGreen = new(0.12f, 0.45f, 0.22f, 1f), PocketRed = new(0.7f, 0.1f, 0.1f, 1f), PocketBlack = new(0.12f, 0.12f, 0.14f, 1f);

    /// <summary>The panel (called every frame while it's open; it closes itself when the table can't be played).</summary>
    public void DrawCasino(Game game, double now)
    {
        if (World.Map.Zombies is not { } zm || CasinoTable < 0 || CasinoTable >= zm.CasinoTables.Count || CasinoTable >= game.ZCasino.Length || game.Local is not { } me)
        {
            CloseCasino();
            return;
        }
        var i = CasinoTable;
        var t = game.ZCasino[i];
        var spot = zm.CasinoTables[i];
        var zp = game.ZP(me);
        var min = game.CasinoMinBet(zm, i);
        var step = Math.Max(1, game.ZRules.CasinoBetStep);
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X - 24 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.45f), ImGuiCond.Always, new float2(1, 0.5f));
        ImGui.SetNextWindowSize(new float2((t.Game == CasinoGame.Roulette ? 640 : 470) * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f);
        if (ImGui.Begin("##casino"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.PushTextWrapPos(0); // (wrap at the window's edge)
            ImGui.PushFont(default, 26);
            ImGuiSafe.TextColored(ZGold, $"{CasinoGames.Name(spot.Game).ToUpperInvariant()}");
            ImGui.PopFont();
            ImGuiSafe.TextColored(Dim, $"Bets from {min} points{(t.Game == CasinoGame.Roulette ? " a spin" : "")}.   You have {zp.Points}.");
            ImGuiSafe.TextColored(Orange, "Zombies don't wait: walk away from an unfinished hand and the bet is lost."u8);
            ImGui.Separator();
            var left = Math.Max(0, t.ShowUntil - Brutal.GlfwApi.Glfw.Time);
            switch (t.Game)
            {
                case CasinoGame.Blackjack:
                    DrawBlackjack(game, t, me, zp, i, min, step, left);
                    break;
                case CasinoGame.Roulette:
                    DrawRoulette(game, t, me, zp, i, min, step, left);
                    break;
                case CasinoGame.HighLow:
                    DrawHighLow(game, t, me, zp, i, min, step, left);
                    break;
                default:
                    DrawDice(game, t, me, zp, i, min, step, left, now);
                    break;
            }
            ImGui.Separator();
            if (ImGui.Button("BACK TO THE FIGHT [Esc]"u8, new float2(240 * _scale, 30 * _scale)))
                CloseCasino();
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
    }

    private int _casinoBetEdit;

    /// <summary>
    /// The bet chooser: chips up and down, half, double, all in, or a typed amount, within the table's smallest bet and
    /// the points you have (there is no largest bet). Bets go in steps of <paramref name="step"/>.
    /// </summary>
    private void BetChooser(int min, int step, int points, int already = 0)
    {
        var top = Casino.BetCeiling(points, step);
        var low = already > 0 ? step : min;
        var high = Math.Max(low, top);
        if (_casinoBet > high)
            _casinoBet = high;
        if (_casinoBet < low)
            _casinoBet = low;
        ImGuiSafe.TextColored(new float4(1, 1, 1, 1), "Bet:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * _scale);
        if (!ImGui.IsAnyItemActive())
            _casinoBetEdit = _casinoBet; // (not while the box is being typed in)
        ImGui.InputInt("##bet"u8, ref _casinoBetEdit, 0, 0); // (no step buttons: a typed amount applies when the box is left or Enter is pressed)
        if (ImGui.IsItemDeactivatedAfterEdit())
            _casinoBet = Math.Max(low, (int)Math.Clamp((long)_casinoBetEdit, low, high) / step * step);
        foreach (var d in BetSteps)
        {
            if (ImGui.Button(d > 0 ? $"+{d}##bet{d}" : $"{d}##bet{d}"))
                _casinoBet = (int)Math.Clamp((long)_casinoBet + d, low, high);
            ImGui.SameLine();
        }
        ImGui.NewLine();
        if (ImGui.Button("Min##bet"u8))
            _casinoBet = low;
        ImGui.SameLine();
        if (ImGui.Button("Half##bethalf"u8))
            _casinoBet = Math.Max(low, _casinoBet / 2 / step * step);
        ImGui.SameLine();
        if (ImGui.Button("x2##betx2"u8))
            _casinoBet = (int)Math.Clamp(2L * _casinoBet, low, high);
        ImGui.SameLine();
        if (ImGui.Button("All in##betall"u8))
            _casinoBet = high; // (your points, to the step)
        if (top < low)
            ImGuiSafe.TextColored(Red, $"You need {low} points to bet here.");
    }

    private static readonly int[] BetSteps = [-100, -10, 10, 100, 500];

    private string WhoName(Game game, byte id) => id == game.LocalId ? "You" : game.NameOf(id);

    private string HandText(ReadOnlySpan<byte> cards, bool hideHole = false)
    {
        _cs.Clear();
        for (var c = 0; c < cards.Length; c++)
        {
            if (c > 0)
                _cs.Append(' ');
            _cs.Append(cards[c] >= 52 || (hideHole && c == 1) ? "[?]" : Casino.CardName(cards[c]));
        }
        var (total, soft) = Casino.HandTotal(cards);
        if (cards.Length > 0)
            _cs.Append(" = ").Append(total).Append(soft && total < 21 ? " (soft)" : "");
        return _cs.ToString();
    }

    private static string OutcomeText(CasinoSeat s) => s.Outcome switch
    {
        CasinoOutcome.Blackjack => $"BLACKJACK +{s.Paid - s.Bet}",
        CasinoOutcome.Win => $"WIN +{s.Paid - s.Bet}",
        CasinoOutcome.Push => "PUSH",
        CasinoOutcome.Bust => $"BUST -{s.Bet}",
        CasinoOutcome.Lose => $"LOSE -{s.Bet}",
        CasinoOutcome.Forfeit => $"FORFEIT -{s.Bet}",
        _ => "",
    };

    private static float4 OutcomeColour(CasinoOutcome o) => o switch
    {
        CasinoOutcome.Blackjack or CasinoOutcome.Win => new float4(0.4f, 1f, 0.5f, 1f),
        CasinoOutcome.Push => new float4(0.85f, 0.85f, 0.85f, 1f),
        CasinoOutcome.None => new float4(1f, 1f, 1f, 1f),
        _ => new float4(1f, 0.35f, 0.35f, 1f),
    };

    private void DrawBlackjack(Game game, CasinoTable t, Player me, ZPlayer zp, int i, int min, int step, double left)
    {
        ImGuiSafe.TextColored(Dim, "Beat the dealer without going over 21. The dealer draws to 16 and stands on all 17s; blackjack pays 3:2."u8);
        ImGuiSafe.TextColored(ZGold, t.Phase switch
        {
            CasinoPhase.Open => "Place a bet to take a seat.",
            CasinoPhase.Betting => $"Dealing in {Math.Ceiling(left):0} s (others can still sit down).",
            CasinoPhase.Playing => $"Hands in play: {Math.Ceiling(left):0} s to decide (then you stand).",
            _ => "The hand is over.",
        });
        if (t.DealerCount > 0)
            ImGuiSafe.TextColored(new float4(1, 1, 1, 1), $"Dealer: {HandText(t.DealerHand, !t.HoleShown)}");
        var mine = t.SeatOf(me.Id);
        for (var k = 0; k < t.Seats.Length; k++)
        {
            var s = t.Seats[k];
            if (!s.Taken)
                continue;
            var who = WhoName(game, s.Player);
            var hand = s.Count > 0 ? HandText(s.Hand) : "waiting for the deal";
            ImGuiSafe.TextColored(k == mine ? ZGold : new float4(0.85f, 0.85f, 0.9f, 1),
                $"Seat {k + 1} - {who}: {hand}   (bet {s.Bet}{(s.Doubled ? ", doubled" : "")}){(s.Outcome != CasinoOutcome.None ? "  " : "")}");
            if (s.Outcome != CasinoOutcome.None)
            {
                ImGui.SameLine();
                ImGuiSafe.TextColored(OutcomeColour(s.Outcome), OutcomeText(s));
            }
        }
        ImGui.Spacing();
        if (mine < 0)
        {
            if (t.Phase is CasinoPhase.Open or CasinoPhase.Betting)
            {
                BetChooser(min, step, zp.Points);
                ImGui.BeginDisabled(_casinoBet > zp.Points || _casinoBet < min);
                if (ImGui.Button($"SIT DOWN AND BET {_casinoBet}", new float2(260 * _scale, 34 * _scale)))
                    game.CasinoRequest(i, CasinoAct.Bet, 0, _casinoBet);
                ImGui.EndDisabled();
            }
            else
                ImGuiSafe.TextColored(Dim, "Wait for this hand to finish to sit down."u8);
            return;
        }
        var me2 = t.Seats[mine];
        if (t.Phase == CasinoPhase.Betting && ImGui.Button("LEAVE (BET BACK)"u8, new float2(200 * _scale, 30 * _scale)))
            game.CasinoRequest(i, CasinoAct.Leave, 0, 0);
        if (t.Phase == CasinoPhase.Playing && !me2.Done)
        {
            if (ImGui.Button("HIT"u8, new float2(110 * _scale, 38 * _scale)))
                game.CasinoRequest(i, CasinoAct.Hit, 0, 0);
            ImGui.SameLine();
            if (ImGui.Button("STAND"u8, new float2(110 * _scale, 38 * _scale)))
                game.CasinoRequest(i, CasinoAct.Stand, 0, 0);
            ImGui.SameLine();
            ImGui.BeginDisabled(me2.Count != 2 || zp.Points < me2.Bet);
            if (ImGui.Button($"DOUBLE ({me2.Bet} more)", new float2(170 * _scale, 38 * _scale)))
                game.CasinoRequest(i, CasinoAct.Double, 0, 0);
            ImGui.EndDisabled();
        }
        else if (t.Phase == CasinoPhase.Playing)
            ImGuiSafe.TextColored(Dim, "Waiting for the other seats..."u8);
    }

    private void DrawRoulette(Game game, CasinoTable t, Player me, ZPlayer zp, int i, int min, int step, double left)
    {
        ImGuiSafe.TextColored(Dim, "One zero. Pays 35 to 1 a number, 17 a split, 2 a dozen or column, 1 to 1 red / black, odd / even, 1-18 / 19-36."u8);
        var betting = t.Phase is CasinoPhase.Open or CasinoPhase.Betting;
        ImGuiSafe.TextColored(ZGold, t.Phase switch
        {
            CasinoPhase.Open => "Place your chips: the first chip starts the clock.",
            CasinoPhase.Betting => $"Place your chips: the wheel spins in {Math.Ceiling(left):0} s.",
            CasinoPhase.Spinning => "No more bets. The wheel is turning...",
            _ => t.Number <= 36 ? $"The ball landed on {t.Number} {Casino.ColourName(t.Number)}." : "The spin is over.",
        });
        var mineTotal = t.BetsOf(me.Id);
        if (betting)
            BetChooser(min, step, zp.Points, mineTotal);
        var can = betting && _casinoBet > 0 && _casinoBet <= zp.Points && (mineTotal > 0 || _casinoBet >= min);
        void Place(int spot)
        {
            if (can)
                game.CasinoRequest(i, CasinoAct.Bet, spot, _casinoBet);
        }
        if (betting)
        {
            ImGui.Checkbox("Split: click two numbers side by side"u8, ref _splitMode);
            if (!_splitMode)
                _splitFirst = -1;
        }
        var cell = new float2(38 * _scale, 30 * _scale);
        var won = t.Phase == CasinoPhase.Settled ? t.Number : (byte)255;
        ImGui.BeginDisabled(!betting);
        // The zero, then the numbers in three rows (3, 6 ... 36 on top) and twelve columns.
        PushPocket(0, won == 0);
        if (ImGui.Button(Mark(t, me.Id, 0, "0") + "##r0", new float2(38 * _scale, cell.Y * 3 + 8 * _scale)))
            Place(0);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.BeginGroup();
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 12; col++)
            {
                var n = col * 3 + (3 - row);
                if (col > 0)
                    ImGui.SameLine();
                PushPocket(n, won == n || _splitFirst == n);
                if (ImGui.Button($"{Mark(t, me.Id, n, n.ToString())}##r{n}", cell))
                {
                    if (!_splitMode)
                        Place(n);
                    else if (_splitFirst < 0)
                        _splitFirst = n;
                    else
                    {
                        var sp = Casino.SplitSpot(_splitFirst, n);
                        if (sp >= 0)
                            Place(sp);
                        else
                            game.LoadoutNotice = "A split is two numbers side by side on the board.";
                        _splitFirst = -1;
                    }
                }
                ImGui.PopStyleColor();
            }
        }
        ImGui.EndGroup();
        for (var k = Casino.SpotRed; k < Casino.SpotCount; k++)
        {
            if (k != Casino.SpotRed && k != Casino.SpotDozen)
                ImGui.SameLine();
            var hit = won <= 36 && Casino.Covers(k, won);
            ImGui.PushStyleColor(ImGuiCol.Button, k == Casino.SpotRed ? PocketRed : k == Casino.SpotBlack ? PocketBlack : hit ? new float4(0.8f, 0.65f, 0.15f, 1f) : FeltGreen);
            if (ImGui.Button($"{Mark(t, me.Id, k, Casino.SpotName(k))}##r{k}", new float2(k < Casino.SpotDozen ? 96 * _scale : 92 * _scale, 28 * _scale)))
                Place(k);
            ImGui.PopStyleColor();
        }
        ImGui.EndDisabled();
        // The chips on the board.
        _cs.Clear();
        var others = 0;
        var othersTotal = 0;
        for (var b = 0; b < t.BetCount; b++)
        {
            if (t.BetPlayer[b] != me.Id)
            {
                others++;
                othersTotal += t.BetAmount[b];
                continue;
            }
            if (_cs.Length > 0)
                _cs.Append(", ");
            _cs.Append(Casino.SpotName(t.BetSpot[b])).Append(' ').Append(t.BetAmount[b]);
            if (won <= 36 && Casino.Covers(t.BetSpot[b], won))
                _cs.Append(" WINS ").Append(t.BetAmount[b] * (Casino.Payout(t.BetSpot[b]) + 1));
        }
        ImGuiSafe.TextWrapped(mineTotal > 0 ? $"Your chips ({mineTotal}): {_cs}" : "You have no chips down.");
        if (others > 0)
            ImGuiSafe.TextColored(Dim, $"Other players: {others} chip(s), {othersTotal} points.");
        if (t.Phase == CasinoPhase.Betting && mineTotal > 0 && ImGui.Button("TAKE MY CHIPS BACK"u8, new float2(220 * _scale, 28 * _scale)))
            game.CasinoRequest(i, CasinoAct.Clear, 0, 0);
    }

    /// <summary>A board label with a star when you have a chip on it.</summary>
    private static string Mark(CasinoTable t, byte me, int spot, string label)
    {
        for (var b = 0; b < t.BetCount; b++)
            if (t.BetPlayer[b] == me && t.BetSpot[b] == spot)
                return label + "*";
        return label;
    }

    private static void PushPocket(int n, bool lit) =>
        ImGui.PushStyleColor(ImGuiCol.Button, lit ? new float4(0.85f, 0.7f, 0.15f, 1f) : n == 0 ? FeltGreen : Casino.IsRed(n) ? PocketRed : PocketBlack);

    private void DrawHighLow(Game game, CasinoTable t, Player me, ZPlayer zp, int i, int min, int step, double left)
    {
        ImGuiSafe.TextColored(Dim, $"Higher or lower than the card? A tie loses. Each right guess multiplies the pot; cash out when you like (at most {game.ZRules.HighLowMaxSteps} guesses).");
        var s = t.Seats[0];
        var mine = s.Player == me.Id;
        if (s.Taken && s.Count > 0)
        {
            _cs.Clear();
            for (var c = 0; c < s.Count; c++)
                _cs.Append(c == 0 ? "" : "  ").Append(Casino.CardName(s.Cards[c]));
            ImGuiSafe.TextColored(new float4(1, 1, 1, 1), $"{WhoName(game, s.Player)}: {_cs}");
            ImGui.PushFont(default, 30);
            ImGuiSafe.TextColored(ZGold, $"Pot {t.Pot}   ({t.Steps}/{game.ZRules.HighLowMaxSteps} right)");
            ImGui.PopFont();
            if (s.Outcome != CasinoOutcome.None)
                ImGuiSafe.TextColored(OutcomeColour(s.Outcome), s.Outcome is CasinoOutcome.Win or CasinoOutcome.Push ? $"Cashed out {s.Paid}" : OutcomeText(s));
        }
        if (t.Phase == CasinoPhase.Open)
        {
            BetChooser(min, step, zp.Points);
            ImGui.BeginDisabled(_casinoBet > zp.Points || _casinoBet < min);
            if (ImGui.Button($"DEAL A CARD (BET {_casinoBet})", new float2(260 * _scale, 34 * _scale)))
                game.CasinoRequest(i, CasinoAct.Bet, 0, _casinoBet);
            ImGui.EndDisabled();
            return;
        }
        if (t.Phase != CasinoPhase.Playing)
            return;
        if (!mine)
        {
            ImGuiSafe.TextColored(Dim, $"{game.NameOf(s.Player)} is playing. Wait for the next hand.");
            return;
        }
        var cur = Casino.Rank(s.Cards[s.Count - 1]);
        var hi = Casino.HighLowMultiplier(cur, true, game.ZRules.HighLowEdge);
        var lo = Casino.HighLowMultiplier(cur, false, game.ZRules.HighLowEdge);
        ImGuiSafe.TextColored(Dim, $"{Math.Ceiling(left):0} s to choose (then you cash out).");
        ImGui.BeginDisabled(hi <= 0);
        if (ImGui.Button(hi > 0 ? $"HIGHER (x{hi / 100f:0.00})" : "HIGHER (-)", new float2(150 * _scale, 38 * _scale)))
            game.CasinoRequest(i, CasinoAct.Higher, 0, 0);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(lo <= 0);
        if (ImGui.Button(lo > 0 ? $"LOWER (x{lo / 100f:0.00})" : "LOWER (-)", new float2(150 * _scale, 38 * _scale)))
            game.CasinoRequest(i, CasinoAct.Lower, 0, 0);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button($"CASH OUT {t.Pot}", new float2(140 * _scale, 38 * _scale)))
            game.CasinoRequest(i, CasinoAct.CashOut, 0, 0);
    }

    private byte _diceSeen;
    private double _diceAt = -1e9;

    /// <summary>How long the dice tumble on the table before the panel says what they show.</summary>
    public const float DiceTumbleSeconds = 0.8f;

    private void DrawDice(Game game, CasinoTable t, Player me, ZPlayer zp, int i, int min, int step, double left, double now)
    {
        ImGuiSafe.TextColored(Dim, "The pass line: a first roll of 7 or 11 wins, 2, 3 or 12 loses; any other number is the point: roll it again before a 7 to win (1 to 1)."u8);
        if (t.RollId != _diceSeen)
        {
            _diceSeen = t.RollId;
            _diceAt = Brutal.GlfwApi.Glfw.Time;
        }
        var rolling = Brutal.GlfwApi.Glfw.Time - _diceAt < DiceTumbleSeconds;
        var s = t.Seats[0];
        if (s.Taken)
        {
            ImGuiSafe.TextColored(new float4(1, 1, 1, 1), $"{WhoName(game, s.Player)} bet {s.Bet}. {(t.Point == 0 ? "Come-out roll." : $"The point is {t.Point}.")}");
            if (t.Die1 > 0)
            {
                ImGui.PushFont(default, 30);
                ImGuiSafe.TextColored(ZGold, rolling ? "Rolling..." : $"{t.Die1} + {t.Die2} = {t.Die1 + t.Die2}");
                ImGui.PopFont();
            }
            if (s.Outcome != CasinoOutcome.None && !rolling)
                ImGuiSafe.TextColored(OutcomeColour(s.Outcome), OutcomeText(s));
        }
        if (t.Phase == CasinoPhase.Open)
        {
            BetChooser(min, step, zp.Points);
            ImGui.BeginDisabled(_casinoBet > zp.Points || _casinoBet < min);
            if (ImGui.Button($"BET {_casinoBet} ON THE PASS LINE", new float2(280 * _scale, 34 * _scale)))
                game.CasinoRequest(i, CasinoAct.Bet, 0, _casinoBet);
            ImGui.EndDisabled();
            return;
        }
        if (t.Phase != CasinoPhase.Playing)
            return;
        if (s.Player != me.Id)
        {
            ImGuiSafe.TextColored(Dim, $"{game.NameOf(s.Player)} is rolling. Wait for the next hand.");
            return;
        }
        ImGuiSafe.TextColored(Dim, $"{Math.Ceiling(left):0} s to roll (then the dice roll themselves).");
        ImGui.BeginDisabled(rolling);
        if (ImGui.Button("ROLL THE DICE"u8, new float2(200 * _scale, 38 * _scale)))
            game.CasinoRequest(i, CasinoAct.Roll, 0, 0);
        ImGui.EndDisabled();
    }

    // ---- Labels over the tables (everyone) -------------------------------------------------------------

    private readonly string?[] _casinoLabels = new string?[ZombiesMap.MaxCasinoTables];
    private readonly int[] _casinoLabelKey = new int[ZombiesMap.MaxCasinoTables];
    private readonly string?[] _casinoWinLabels = new string?[ZombiesMap.MaxCasinoTables];
    private readonly byte[] _casinoWinSeq = new byte[ZombiesMap.MaxCasinoTables];

    /// <summary>
    /// Over each table within 10 m: its game and limits, and what's on it (the hands and totals, the pocket, the dice,
    /// the pot) so the people round it can follow; while a big win shows (to everyone within 25 m): who won how much.
    /// The texts are made when the table changes, not every frame.
    /// </summary>
    private void DrawCasinoLabels(Game game, Player me, double now)
    {
        if (World.Map.Zombies is not { } zm)
            return;
        for (var i = 0; i < zm.CasinoTables.Count && i < game.ZCasino.Length; i++)
        {
            var spot = zm.CasinoTables[i];
            var t = game.ZCasino[i];
            var dx = spot.At.X - me.Position.X;
            var dz = spot.At.Z - me.Position.Z;
            var d2 = dx * dx + dz * dz;
            if (game.CasinoWinShowing(i, now) && d2 <= 25 * 25)
            {
                if (_casinoWinSeq[i] != t.WinSeq || _casinoWinLabels[i] == null)
                {
                    _casinoWinSeq[i] = t.WinSeq;
                    _casinoWinLabels[i] = $"{WhoName(game, t.WinPlayer)} won {t.WinAmount} points!";
                }
                var fade = (float)Math.Clamp((game.ZRules.SlotWinSeconds - (now - t.WinAt)) * 2, 0, 1);
                Hud.WorldLabel(spot.At + new System.Numerics.Vector3(0, 2.6f + 0.3f * (float)Math.Clamp(now - t.WinAt, 0, 1), 0), _casinoWinLabels[i]!, new System.Numerics.Vector3(1f, 0.82f, 0.25f), fade, 19f * _scale);
            }
            if (d2 > 10 * 10)
                continue;
            var key = HashCode.Combine(t.Phase, t.RoundId, t.DealerCount + t.Seats[0].Count * 16 + t.Seats[1].Count * 256 + t.Seats[2].Count * 4096 + t.Seats[3].Count * 65536,
                t.RollId, t.Steps, t.BetCount, t.HoleShown, (int)t.Seats[0].Outcome);
            if (_casinoLabels[i] == null || _casinoLabelKey[i] != key)
            {
                _casinoLabelKey[i] = key;
                _casinoLabels[i] = TableLabel(game, zm, i, t);
            }
            Hud.WorldLabel(spot.At + new System.Numerics.Vector3(0, 1.75f, 0), _casinoLabels[i]!, new System.Numerics.Vector3(1f, 0.85f, 0.35f), 0.95f, 13f * _scale);
        }
    }

    private string TableLabel(Game game, ZombiesMap zm, int i, CasinoTable t)
    {
        var head = $"{CasinoGames.Name((int)t.Game).ToUpperInvariant()} from {game.CasinoMinBet(zm, i)}";
        if (zm.CasinoTables[i].NeedsPower && !game.ZPower)
            return head + " - NEEDS POWER";
        switch (t.Game)
        {
            case CasinoGame.Blackjack when t.DealerCount > 0:
            {
                var d = t.HoleShown ? Casino.HandTotal(t.DealerHand).Total.ToString() : $"{Casino.CardName(t.Dealer[0])} up";
                _cs.Clear();
                _cs.Append(head).Append(" - dealer ").Append(d);
                foreach (var s in t.Seats)
                    if (s.Taken)
                        _cs.Append(" | ").Append(game.NameOf(s.Player)).Append(' ').Append(Casino.HandTotal(s.Hand).Total).Append(s.Outcome != CasinoOutcome.None ? " " + OutcomeText(s) : "");
                return _cs.ToString();
            }
            case CasinoGame.Roulette when t.Phase == CasinoPhase.Settled && t.Number <= 36:
                return $"{head} - {t.Number} {Casino.ColourName(t.Number).ToUpperInvariant()}";
            case CasinoGame.Roulette when t.Phase == CasinoPhase.Betting:
                return $"{head} - bets open ({t.BetCount} chip(s))";
            case CasinoGame.HighLow when t.Seats[0].Taken && t.Seats[0].Count > 0:
                return $"{head} - {game.NameOf(t.Seats[0].Player)}: {Casino.CardName(t.Seats[0].Cards[t.Seats[0].Count - 1])}, pot {t.Pot}";
            case CasinoGame.Dice when t.Seats[0].Taken:
                return $"{head} - {game.NameOf(t.Seats[0].Player)}: {(t.Point == 0 ? "come-out" : $"point {t.Point}")}";
            default:
                return head;
        }
    }
}
