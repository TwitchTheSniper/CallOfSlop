using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Zombies casino tables in the world and their panel (Source/Game/ZombiesCasino.cs, Source/UI/UiCasino.cs;
/// docs/zombies/ZOMBIES-MODE.md, "Casino tables"). On each table prop (felt top at <see cref="FeltY"/>, players on its
/// -Z side, the dealer behind): blackjack's cards (face down until turned) and bet stacks, roulette's wheel turning with
/// the ball landing in the rolled pocket and everyone's chips on the board, high-low's run of cards and Bone Dice's dice
/// tumbling to a stop with the point puck. Everyone sees the same table. Client-side only and allocation-free.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>The height of every table's felt (the props are built to it: tools/zombies/make_casino_tables.py).</summary>
    public const float FeltY = 0.9f;

    private static readonly Vector3 CardFace = new(0.94f, 0.93f, 0.88f), CardBack = new(0.55f, 0.08f, 0.1f), SuitRed = new(0.85f, 0.1f, 0.1f), SuitBlack = new(0.05f);

    private static void DrawCasinoTables(ZombiesMap zm, bool live, double now)
    {
        for (var i = 0; i < zm.CasinoTables.Count && i < _game.ZCasino.Length; i++)
        {
            var spot = zm.CasinoTables[i];
            var t = _game.ZCasino[i];
            var place = Matrix4x4.CreateRotationY(-spot.Yaw) * Matrix4x4.CreateTranslation(spot.At);
            if (!zm.MachineProps && CasinoProp(zm, i) is { } def)
                foreach (var pp in def.Parts)
                    Gfx.Draw(Matrix4x4.CreateScale(pp.Size) * Matrix4x4.CreateFromQuaternion(pp.Rotation) * Matrix4x4.CreateTranslation(pp.Offset) * place, pp.Color);
            if (!live)
                continue;
            var dark = spot.NeedsPower && !_game.ZPower;
            switch (t.Game)
            {
                case CasinoGame.Blackjack:
                    DrawBlackjackTable(t, place);
                    break;
                case CasinoGame.Roulette:
                    DrawRouletteTable(t, place, now, dark);
                    break;
                case CasinoGame.HighLow:
                    DrawHighLowTable(t, place);
                    break;
                default:
                    DrawDiceTable(t, place, now);
                    break;
            }
        }
    }

    private static void Box(Matrix4x4 place, float sx, float sy, float sz, float x, float y, float z, Vector3 c, float yaw = 0, float alpha = 1f, int mode = 0) =>
        Gfx.Draw(Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(x, y, z) * place, c, alpha, mode);

    /// <summary>A playing card lying on the felt at (x, z), face up (a suit-coloured pip) or face down.</summary>
    private static void Card(Matrix4x4 place, float x, float z, byte card, float scale = 1f, float yaw = 0)
    {
        var y = FeltY + 0.004f;
        if (card >= 52)
        {
            Box(place, 0.07f * scale, 0.004f, 0.1f * scale, x, y, z, CardBack, yaw);
            Box(place, 0.05f * scale, 0.002f, 0.08f * scale, x, y + 0.003f, z, new Vector3(0.75f, 0.6f, 0.2f), yaw);
            return;
        }
        Box(place, 0.07f * scale, 0.004f, 0.1f * scale, x, y, z, CardFace, yaw);
        var col = Casino.RedSuit(card) ? SuitRed : SuitBlack;
        // The rank as a column of pips (one for an ace, two to ten for the numbers, a bar for a face card), the suit's colour.
        var rank = Casino.Rank(card);
        if (rank is >= 9 and <= 11)
            Box(place, 0.035f * scale, 0.002f, 0.05f * scale, x, y + 0.003f, z, col, yaw);
        else
        {
            var pips = rank == 12 ? 1 : rank + 2;
            var rows = (pips + 1) / 2;
            for (var p = 0; p < pips; p++)
            {
                var px = pips == 1 ? 0 : (p % 2 == 0 ? -0.014f : 0.014f) * scale;
                var pz = rows <= 1 ? 0 : (-0.035f + 0.07f * (p / 2) / Math.Max(1, rows - 1)) * scale;
                var (s, c) = MathF.SinCos(yaw);
                Box(place, (pips == 1 ? 0.022f : 0.011f) * scale, 0.002f, (pips == 1 ? 0.022f : 0.011f) * scale, x + px * c + pz * s, y + 0.003f, z - px * s + pz * c, col, yaw);
            }
        }
    }

    /// <summary>A short stack of chips (one disc per 100 points, at most 6), in <paramref name="c"/>.</summary>
    private static void Chips(Matrix4x4 place, float x, float z, int amount, Vector3 c)
    {
        var n = Math.Clamp(amount / 100 + 1, 1, 6);
        for (var k = 0; k < n; k++)
        {
            var y = FeltY + 0.006f + k * 0.009f;
            Box(place, 0.04f, 0.008f, 0.04f, x, y, z, k % 2 == 0 ? c : Vector3.Lerp(c, Vector3.One, 0.5f));
            Box(place, 0.04f, 0.008f, 0.04f, x, y, z, k % 2 == 0 ? c : Vector3.Lerp(c, Vector3.One, 0.5f), MathF.PI / 4);
        }
    }

    private static Vector3 ChipColour(byte player) => (player % 6) switch
    {
        0 => new Vector3(0.85f, 0.15f, 0.15f),
        1 => new Vector3(0.2f, 0.45f, 0.95f),
        2 => new Vector3(0.2f, 0.75f, 0.3f),
        3 => new Vector3(0.95f, 0.75f, 0.15f),
        4 => new Vector3(0.65f, 0.3f, 0.85f),
        _ => new Vector3(0.95f, 0.5f, 0.15f),
    };

    /// <summary>Seat <paramref name="k"/>'s spot along the table's curved front edge.</summary>
    private static (float X, float Z) SeatSpot(int k)
    {
        var x = -0.66f + 0.44f * k;
        return (x, -0.18f - 0.12f * x * x);
    }

    private static void DrawBlackjackTable(CasinoTable t, Matrix4x4 place)
    {
        for (var c = 0; c < t.DealerCount; c++)
            Card(place, -0.1f * (t.DealerCount - 1) / 2f + 0.1f * c, 0.25f, t.Dealer[c]);
        for (var k = 0; k < t.Seats.Length; k++)
        {
            var s = t.Seats[k];
            if (!s.Taken)
                continue;
            var (x, z) = SeatSpot(k);
            Chips(place, x, z - 0.06f, s.Bet, ChipColour(s.Player));
            for (var c = 0; c < s.Count; c++)
                Card(place, x - 0.03f + 0.025f * c, z + 0.1f + 0.012f * c, s.Cards[c], 0.95f, 0.15f);
        }
    }

    // ---- Roulette: the wheel on the table's right (+X), the board on its left ----

    private const float WheelX = 0.72f, WheelR = 0.3f;

    private static (float X, float Z) BoardSpot(int spot)
    {
        // Numbers: twelve columns along X (1-3 nearest the zero), three rows along Z (3 at the back).
        const float x0 = -0.95f, cw = 0.09f, rz = 0.12f;
        if (spot == 0)
            return (x0 - cw, 0);
        if (spot <= 36)
            return (x0 + cw * ((spot - 1) / 3), rz * ((spot - 1) % 3 - 1));
        if (spot < Casino.SpotRed)
        {
            // A split sits on the line between its two numbers.
            var a = spot < Casino.SpotVSplit ? SplitLow(spot) : spot - Casino.SpotVSplit + 1;
            var b = spot < Casino.SpotVSplit ? a + 1 : a + 3;
            var (ax, az) = BoardSpot(a);
            var (bx, bz) = BoardSpot(b);
            return ((ax + bx) / 2, (az + bz) / 2);
        }
        if (spot >= Casino.SpotColumn)
            return (x0 + cw * 12, rz * (spot - Casino.SpotColumn - 1));
        if (spot >= Casino.SpotDozen)
            return (x0 + cw * (1.5f + 4 * (spot - Casino.SpotDozen)), -0.27f);
        return (x0 + cw * (0.5f + 2 * (spot - Casino.SpotRed)), -0.38f);
    }

    private static int SplitLow(int spot)
    {
        var k = spot - Casino.SpotHSplit;
        for (var n = 1; n <= 35; n++)
            if (n % 3 != 0 && k-- == 0)
                return n;
        return 1;
    }

    private static void DrawRouletteTable(CasinoTable t, Matrix4x4 place, double now, bool dark)
    {
        var spin = Math.Max(1f, _game.ZRules.CasinoSpinSeconds);
        if (t.RoundId != t.SeenRound)
        {
            t.SeenRound = t.RoundId;
            t.SpinStart = t.Phase == CasinoPhase.Spinning ? now - Math.Clamp(spin - t.Left, 0, spin) : -1e9;
        }
        var u = (float)Math.Clamp((now - t.SpinStart) / spin, 0, 1);
        var spinning = t.Phase == CasinoPhase.Spinning;
        // The wheel turns slowly all the time and fast while it spins (slowing to a stop; a whole number of turns per spin).
        var wheel = (float)(now * 0.3) + (spinning ? 8 * MathF.PI * (u - u * u / 2) : 0);
        const float step = MathF.Tau / 37;
        var y = FeltY + 0.1f;
        var lit = dark ? 0.4f : 1f;
        for (var k = 0; k < 37; k++)
        {
            var n = Casino.WheelOrder[k];
            var a = wheel + k * step;
            var c = n == 0 ? new Vector3(0.1f, 0.6f, 0.2f) : Casino.IsRed(n) ? new Vector3(0.75f, 0.08f, 0.08f) : new Vector3(0.08f);
            var hit = t.Phase == CasinoPhase.Settled && n == t.Number && ((int)(now * 6) & 1) == 0;
            Box(place, 0.045f, 0.012f, 0.03f, WheelX + MathF.Sin(a) * WheelR, y, MathF.Cos(a) * WheelR, hit ? new Vector3(1f, 0.85f, 0.2f) : c * lit, -a, 1f, hit ? 2 : 0);
        }
        // The turret in the middle.
        Box(place, 0.05f, 0.08f, 0.05f, WheelX, y + 0.03f, 0, new Vector3(0.8f, 0.65f, 0.2f), wheel);
        Box(place, 0.16f, 0.012f, 0.02f, WheelX, y + 0.06f, 0, new Vector3(0.8f, 0.65f, 0.2f), wheel);
        Box(place, 0.16f, 0.012f, 0.02f, WheelX, y + 0.06f, 0, new Vector3(0.8f, 0.65f, 0.2f), wheel + MathF.PI / 2);
        // The ball: round the rim against the wheel, slowing, dropping into the rolled pocket at the end of the spin.
        if (t.Number <= 36 && t.Phase is CasinoPhase.Spinning or CasinoPhase.Settled)
        {
            var idx = Array.IndexOf(Casino.WheelOrder, t.Number);
            var target = wheel + idx * step;
            var ball = spinning ? target - (1 - u) * (1 - u) * 40f : target;
            var r = spinning ? WheelR + 0.06f * (1 - Smooth(0.7f, 1f, u)) : WheelR;
            var by = y + 0.02f + (spinning && u > 0.7f ? 0.015f * MathF.Abs(MathF.Sin(u * 60)) * (1 - u) : 0);
            Box(place, 0.022f, 0.022f, 0.022f, WheelX + MathF.Sin(ball) * r, by, MathF.Cos(ball) * r, new Vector3(1f), 0, 1f, 2);
        }
        // Everyone's chips on the board (the winning ones glow when the ball lands).
        for (var b = 0; b < t.BetCount; b++)
        {
            var (x, z) = BoardSpot(t.BetSpot[b]);
            var win = t.Phase == CasinoPhase.Settled && t.Number <= 36 && Casino.Covers(t.BetSpot[b], t.Number);
            Chips(place, x + 0.006f * (t.BetPlayer[b] % 3), z + 0.006f * (t.BetPlayer[b] % 2), t.BetAmount[b], win && ((int)(now * 6) & 1) == 0 ? new Vector3(1f, 0.9f, 0.4f) : ChipColour(t.BetPlayer[b]));
        }
        if (t.Phase == CasinoPhase.Settled && t.Number <= 36)
        {
            var (x, z) = BoardSpot(t.Number);
            Box(place, 0.07f, 0.003f, 0.1f, x, FeltY + 0.003f, z, new Vector3(1f, 0.85f, 0.2f), 0, 0.7f, 2); // the winning number lights up
        }
    }

    private static float Smooth(float a, float b, float x)
    {
        var k = Math.Clamp((x - a) / (b - a), 0, 1);
        return k * k * (3 - 2 * k);
    }

    private static void DrawHighLowTable(CasinoTable t, Matrix4x4 place)
    {
        var s = t.Seats[0];
        if (!s.Taken)
            return;
        for (var c = 0; c < s.Count; c++)
        {
            var last = c == s.Count - 1;
            Card(place, -0.45f + 0.085f * c + (last ? 0.03f : 0), last ? -0.02f : 0.05f, s.Cards[c], last ? 1.35f : 0.9f);
        }
        Chips(place, -0.3f, -0.25f, t.Pot, ChipColour(s.Player));
    }

    private static void DrawDiceTable(CasinoTable t, Matrix4x4 place, double now)
    {
        if (t.RollId != t.SeenRoll)
        {
            t.SeenRoll = t.RollId;
            t.RollStart = now;
        }
        var s = t.Seats[0];
        if (s.Taken)
            Chips(place, -0.55f, -0.3f, s.Bet, ChipColour(s.Player));
        // The point puck over the point's box on the back rail (4, 5, 6, 8, 9, 10 left to right).
        if (t.Point != 0)
        {
            var k = t.Point switch { 4 => 0, 5 => 1, 6 => 2, 8 => 3, 9 => 4, _ => 5 };
            Box(place, 0.07f, 0.012f, 0.07f, -0.5f + 0.2f * k, FeltY + 0.008f, 0.3f, new Vector3(0.95f), MathF.PI / 4);
            Box(place, 0.05f, 0.014f, 0.05f, -0.5f + 0.2f * k, FeltY + 0.008f, 0.3f, new Vector3(0.95f), 0);
        }
        if (t.Die1 == 0)
            return;
        var u = (float)Math.Clamp((now - t.RollStart) / Ui.DiceTumbleSeconds, 0, 1);
        for (var d = 0; d < 2; d++)
        {
            var value = d == 0 ? t.Die1 : t.Die2;
            var fx = 0.35f + 0.12f * d;
            var fz = -0.02f + 0.08f * d;
            // Thrown from the front, bouncing to a stop at the back.
            var x = float.Lerp(-0.2f + 0.1f * d, fx, 1 - (1 - u) * (1 - u));
            var z = float.Lerp(-0.3f, fz, 1 - (1 - u) * (1 - u));
            var y = FeltY + 0.03f + MathF.Abs(MathF.Sin(u * 3 * MathF.PI)) * 0.12f * (1 - u);
            var tumble = (1 - u) * (1 - u) * (14f + 3 * d + t.RollId % 5);
            var at = Matrix4x4.CreateRotationX(tumble) * Matrix4x4.CreateRotationZ(tumble * 0.7f) * Matrix4x4.CreateRotationY(0.3f * d) * Matrix4x4.CreateTranslation(x, y, z) * place;
            Gfx.Draw(Matrix4x4.CreateScale(0.06f) * at, new Vector3(0.95f, 0.93f, 0.88f));
            if (u < 1)
                continue;
            // The value on the top face (the classic layouts).
            for (var p = 0; p < 9; p++)
                if (Pip(value, p))
                    Gfx.Draw(Matrix4x4.CreateScale(0.011f, 0.004f, 0.011f) * Matrix4x4.CreateTranslation((p % 3 - 1) * 0.017f, 0.03f, (p / 3 - 1) * 0.017f) * at, value == 1 ? SuitRed : SuitBlack);
        }
    }

    /// <summary>Is pip <paramref name="p"/> (0-8, a 3 x 3 grid) on for a die showing <paramref name="v"/>?</summary>
    private static bool Pip(int v, int p) => v switch
    {
        1 => p == 4,
        2 => p is 0 or 8,
        3 => p is 0 or 4 or 8,
        4 => p is 0 or 2 or 6 or 8,
        5 => p is 0 or 2 or 4 or 6 or 8,
        _ => p is 0 or 2 or 3 or 5 or 6 or 8,
    };

    private static Map? _casinoPropsFor;
    private static readonly PropDef?[] CasinoProps = new PropDef?[ZombiesMap.MaxCasinoTables];

    /// <summary>The prop table <paramref name="i"/> stands as (looked up once per map; drawn without collision on a map without machine props).</summary>
    private static PropDef? CasinoProp(ZombiesMap zm, int i)
    {
        if (!ReferenceEquals(_casinoPropsFor, World.Map))
        {
            _casinoPropsFor = World.Map;
            for (var k = 0; k < CasinoProps.Length; k++)
                CasinoProps[k] = k < zm.CasinoTables.Count ? World.Map.PropScope.Find(ZombiesMap.MachineProp.CasinoTableFor(zm.CasinoTables[k].Game)) : null;
        }
        return i < CasinoProps.Length ? CasinoProps[i] : null;
    }

    // ---- The panel ----------------------------------------------------------------------------------------

    /// <summary>The interact key at a table opens its panel (and frees the mouse for it).</summary>
    private static void CasinoInput(bool interactPressed)
    {
        if (interactPressed && _game.Local is { } me && _game.FindZUse(me) is { Kind: ZUseKind.Casino, Blocked: false } u)
        {
            Ui.OpenCasino(_game, u.Index);
            SetMouseCaptured(false);
        }
    }

    /// <summary>The table panel over the match; it closes (and the mouse goes back to aiming) when you go down, die or walk away.</summary>
    private static void DrawCasinoInMatch(double now)
    {
        if (!Ui.CasinoOpen)
            return;
        var gone = _session == null || _game.Mode != GameMode.Zombies || _game.Local is not { Alive: true } me || _game.IsDowned(me)
                   || World.Map.Zombies is not { } zm || Ui.CasinoTable >= zm.CasinoTables.Count
                   || Vector2.Distance(new Vector2(me.Position.X, me.Position.Z), new Vector2(zm.CasinoTables[Ui.CasinoTable].At.X, zm.CasinoTables[Ui.CasinoTable].At.Z)) > _game.ZRules.CasinoLeaveRange;
        if (!gone)
            Ui.DrawCasino(_game, now);
        if (gone || !Ui.CasinoOpen)
        {
            Ui.CloseCasino();
            if (!Ui.PauseOpen && !Ui.WorkbenchOpen)
                SetMouseCaptured(true);
            _swallowClick = true;
        }
    }
}
