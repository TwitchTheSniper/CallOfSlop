using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A person's panel, bottom left in Sandbox (Karl, 2026-11-18: "replacing the HP bar with percent, oxygen in suit, etc. similar to
/// the robot shell"): the robot shell panel's glass plate and hologram (UiShellPanel.cs), here a person's figure lit by their health,
/// with a helmet drawn round the head while suited. Rows, each a label, a bar and a value: health; the suit's air (or NO SUIT); breath
/// held, while it runs down; the ship's air aboard a ship with life support (ShipAir.cs); the MMU's fuel and its inertia dampeners
/// while worn, with the pack's icon in the heading (its jets lit while it burns); the magnet boots, on or off with their key
/// (Karl, 2026-11-24: moved in from the bottom right; GameSandboxMagBoots.cs), while standing (seated, the seat holds them). Under the rows, gravity (Sean, 2026-11-24: the
/// felt-g readout moved in from its own dial left of centre): how many g they feel, a dial with the pull's way seen from behind
/// them, and the way in words (<see cref="VpGravity"/>). It replaces the green health bar, the HUD's air lines and the felt-g
/// readout. Strings are made once, or once when their value changes: nothing allocates a frame.
/// </summary>
public sealed partial class Ui
{
    private static readonly string[] VpMins = MakeVpCount(" MIN", 60), VpSecs = MakeVpCount(" S", 30);
    private static readonly string[] VpWayWords = ["DOWN", "UP", "FORWARD", "BACK", "RIGHT", "LEFT"];
    private static readonly string[] VpWays = MakeVpWays(); // (a way's words by VpWay's index: one word, or two)
    private static readonly string[] VpG = new string[1000]; // ("0.0 G" to "99.9 G", each made the first time it shows)

    /// <summary>The gravity block's height under the rows (before the UI scale).</summary>
    private const float VpGravityH = 34;
    private long _vpShipKey = -1;
    private float _vpTop = float.MaxValue; // (the plate's top as last drawn, MaxValue when not: the HUD chat sits above it, UiChat.cs)
    private string _vpShipTime = "";

    private static string[] MakeVpCount(string unit, int most)
    {
        var s = new string[most + 1];
        for (var k = 0; k <= most; k++)
            s[k] = k + unit;
        return s;
    }

    /// <summary>The ship's air left as words, made again only when the minutes change.</summary>
    private string VpShipTime(int units, int aboard)
    {
        var minutes = aboard > 0 ? (int)(units / (aboard * ShipAir.PerPersonMinute)) : units;
        if (_vpShipKey != minutes)
        {
            _vpShipKey = minutes;
            _vpShipTime = minutes >= 60 ? $"{minutes / 60}H {minutes % 60:00}M" : $"{minutes} MIN";
        }
        return _vpShipTime;
    }

    private void DrawVitalsPanel(Game game, float2 pos, float2 size)
    {
        _vpTop = float.MaxValue;
        if (PauseOpen || MenuOpen || CommandOpen || !game.SandboxOn || game.Local is not { Alive: true, InShell: false } me)
            return;
        var dl = ImGui.GetForegroundDrawList();
        var s = _scale;
        var now = Brutal.GlfwApi.Glfw.Time;
        var blink = 0.72f + 0.28f * MathF.Sin((float)(now * 7.0));
        var suited = (me.Eva & Eva.Suit) != 0;
        var mmu = (me.Eva & Eva.Mmu) != 0;
        var ship = game.ShipAirForHud(me);
        var shipAir = ShipAir.Pressurised(ship);
        var vacuum = !shipAir && Eva.Vacuum(me.Position);
        var holding = Eva.Breathes(me) && me.Breath < Eva.BreathSeconds - 0.05f;

        var hp = Math.Clamp(me.Health * 100 / World.MaxHealth, 0, 100);
        var suitPct = Math.Clamp((int)(me.SuitAir * 100 / Eva.SuitSeconds), 0, 100);
        var boots = !game.SandboxSeatedHere && _hudSettings != null;
        var rows = 2 + (holding ? 1 : 0) + (ship != null ? 1 : 0) + (mmu ? 2 : 0) + (boots ? 1 : 0);
        ref var hand = ref game.SandboxHands.Hand(me.Id);
        var g = me.FeltPull.Length() / ShipGravity.G;
        var crushing = ShipGravity.Mode != ShipGravityMode.Deck && g > ShipGravity.Limit(game.Constructs, hand.SeatBody, hand.SeatPart, suited); // (hurt each second: GameShipGravity.cs)

        // The plate, bottom left where the health bar was.
        var w = 292 * s;
        var h = (64 + rows * 19.5f + 20 + VpGravityH) * s;
        h = MathF.Max(h, 150 * s);
        var p0 = new float2(pos.X + 20 * s, pos.Y + size.Y - 30 * s - h);
        var p1 = p0 + new float2(w, h);
        _vpTop = p0.Y;
        var hpCol = SpColour(hp, 50);
        var danger = vacuum && (!suited || me.SuitAir <= 0) || hp <= 15 || crushing;
        CmdPlate(dl, p0, p1, 1f, danger ? CmdRed : vacuum && suited && suitPct < 25 ? CmdAmber : CmdCyan);

        // The heading: who, where they breathe, their health large on the right.
        CmdText(dl, p0 + new float2(12 * s, 7 * s), 13.5f, CmdWhite, 0.95f, "VITALS");
        var (state, stateCol) = vacuum
            ? suited && me.SuitAir > 0 ? ("VACUUM: ON SUIT AIR", CmdAmber) : ("VACUUM: NO AIR", CmdRed)
            : shipAir ? ("SHIP AIR", CmdDim) : ("BREATHABLE AIR", CmdDim);
        CmdText(dl, p0 + new float2(12 * s, 23 * s), 10.5f, stateCol, stateCol == CmdRed ? 0.9f * blink : 0.9f, state);
        CmdText(dl, new float2(p1.X - 12 * s, p0.Y + 6 * s), 19f, hpCol, hp <= 15 ? blink : 1f, SpPct[hp], 2);
        CmdLine(dl, p0 + new float2(10 * s, 40 * s), new float2(p1.X - 10 * s, p0.Y + 40 * s), CmdDim, 0.5f);
        if (mmu)
            VpMmuIcon(dl, p0 + new float2(176 * s, 20 * s), me, blink);

        // The hologram: a person seen from behind, lit by their health; a helmet round the head while suited, the pack on the back.
        var cx = p0.X + 54 * s;
        var top = p0.Y + 50 * s;
        var a = hp <= 15 ? blink : 1f;
        SpPart(dl, new float2(cx - 7 * s, top + 2 * s), new float2(cx + 7 * s, top + 16 * s), hp, 50, blink);
        SpPart(dl, new float2(cx - 13 * s, top + 20 * s), new float2(cx + 13 * s, top + 54 * s), hp, 50, blink);
        SpPart(dl, new float2(cx - 22 * s, top + 21 * s), new float2(cx - 16 * s, top + 52 * s), hp, 50, blink);
        SpPart(dl, new float2(cx + 16 * s, top + 21 * s), new float2(cx + 22 * s, top + 52 * s), hp, 50, blink);
        SpPart(dl, new float2(cx - 12 * s, top + 57 * s), new float2(cx - 2 * s, top + 96 * s), hp, 50, blink);
        SpPart(dl, new float2(cx + 2 * s, top + 57 * s), new float2(cx + 12 * s, top + 96 * s), hp, 50, blink);
        if (suited)
        {
            var sc = SpColour(suitPct, 25);
            var sa = (vacuum && suitPct <= 10 ? blink : 1f) * 0.9f;
            CmdGlow(dl, new float2(cx - 10 * s, top - 1 * s), new float2(cx + 10 * s, top - 1 * s), sc, sa); // (the helmet's cube)
            CmdGlow(dl, new float2(cx + 10 * s, top - 1 * s), new float2(cx + 10 * s, top + 19 * s), sc, sa);
            CmdGlow(dl, new float2(cx + 10 * s, top + 19 * s), new float2(cx - 10 * s, top + 19 * s), sc, sa);
            CmdGlow(dl, new float2(cx - 10 * s, top + 19 * s), new float2(cx - 10 * s, top - 1 * s), sc, sa);
            dl.AddRectFilled(new float2(cx - 9 * s, top + 25 * s), new float2(cx + 9 * s, top + 44 * s), Hud.Colour(sc, 0.18f * sa)); // (its air pack)
        }
        if (mmu)
            CmdLine(dl, new float2(cx - 24 * s, top + 28 * s), new float2(cx + 24 * s, top + 28 * s), CmdCyan, 0.6f, 1.4f); // (the MMU's arms)
        CmdBrackets(dl, new float2(cx, top + 48 * s), 38 * s, CmdDim, 0.55f, 1f);
        var sweep = (float)(now * 0.45 % 1.0);
        var sy = top - 2 * s + sweep * 100 * s;
        CmdLine(dl, new float2(cx - 34 * s, sy), new float2(cx + 34 * s, sy), CmdCyan, 0.22f * MathF.Sin(sweep * MathF.PI) * a, 1.2f);

        // The rows.
        var y = p0.Y + 48 * s;
        VpRow(dl, p0, p1, ref y, "HEALTH", hp, hpCol, hp <= 15 ? blink : 1f, SpPct[hp]);
        if (suited)
        {
            var sc = SpColour(suitPct, 25);
            VpRow(dl, p0, p1, ref y, "SUIT AIR", suitPct, sc, vacuum && suitPct <= 10 ? blink : 1f, VpMins[Math.Clamp((int)MathF.Ceiling(me.SuitAir / 60), 0, 60)]);
        }
        else
            VpRow(dl, p0, p1, ref y, "SUIT AIR", 0, vacuum ? CmdRed : CmdDim, vacuum ? blink : 0.8f, "NO SUIT");
        if (holding)
        {
            var bp = Math.Clamp((int)(me.Breath * 100 / Eva.BreathSeconds), 0, 100);
            VpRow(dl, p0, p1, ref y, "BREATH", bp, bp <= 35 ? CmdRed : CmdAmber, blink, VpSecs[Math.Clamp((int)MathF.Ceiling(me.Breath), 0, 30)]);
        }
        if (ship != null)
        {
            var sp = ship.AirMax > 0 ? Math.Clamp(ship.AirUnits * 100 / ship.AirMax, 0, 100) : 0;
            var col = ship.AirUnits <= 0 ? CmdRed : sp <= 25 ? CmdAmber : CmdCyan;
            VpRow(dl, p0, p1, ref y, "SHIP AIR", sp, col, ship.AirUnits <= 0 ? blink : 1f,
                !ship.AirWorks ? "NO UNIT" : ship.AirUnits <= 0 ? "EMPTY" : VpShipTime(ship.AirUnits, Math.Max(1, (int)ship.AirAboard)));
        }
        if (mmu)
        {
            var fp = Math.Clamp(me.Sim.JetFuel * 100 / Eva.MmuFull, 0, 100);
            VpRow(dl, p0, p1, ref y, "MMU FUEL", fp, SpColour(fp, 25), fp <= 15 ? blink : 1f, SpPct[fp]);
            var coast = game.MmuCoasting;
            VpRow(dl, p0, p1, ref y, "INERTIA", -1, coast ? CmdAmber : CmdCyan, 1f, coast ? "COASTING" : "DAMPED");
        }
        if (boots)
            VpRow(dl, p0, p1, ref y, "MAG BOOTS", -1, game.MagBootsOn ? CmdCyan : CmdDim, 1f, VpBootsText(game.MagBootsOn));
        VpGravity(dl, game, me, hand.SeatBody, p0, p1, y, g, crushing, blink);
        if (ship != null && ship.AirWorks && ship.AirUnits > 0)
            CmdText(dl, new float2(p0.X + 108 * s, p1.Y - 16 * s), 10f, CmdDim, 0.8f, VpAboard((int)ship.AirAboard), 0, false);
    }

    /// <summary>
    /// The gravity block under the rows (felt gravity, ShipGravity.cs: the server's pull, <see cref="ShipGravity.PersonPull"/>, the
    /// owner's snapshot on a client). On the left GRAVITY and the pull's way in words (or WEIGHTLESS); in the bars' column a dial,
    /// the pull as seen from behind them facing forward (right is right, down is down), its end filled while it presses them back
    /// (into the seat or the aft bulkhead), a ring while it pulls them forward (past a tenth of a g), else an arrowhead; on the right the g. Cyan, amber from
    /// <see cref="ShipGravity.SlowG"/> (moving slows), red and PINNED from <see cref="ShipGravity.PinG"/>; past the g they take
    /// unhurt (<see cref="ShipGravity.Limit"/>, not under deck gravity) it pulses CRUSHING and the plate turns red.
    /// </summary>
    private void VpGravity(ImDrawListPtr dl, Game game, Player me, ushort seatBody, float2 p0, float2 p1, float y, float g, bool crushing, float blink)
    {
        var s = _scale;
        var felt = me.FeltPull;

        // Their frame: up from the seat's vehicle, the deck they stand on, or the ground; forward their look, levelled.
        var up = seatBody != 0 && game.Constructs.Get(seatBody) is { } seat ? Vector3.Transform(Vector3.UnitY, seat.Rot)
            : me.Sim.RideBody != 0 && me.Sim.RideUpW != Vector3.Zero ? Vector3.Normalize(me.Sim.RideUpW)
            : GravityFrames.UpAt(me.Position);
        var cam = Hud.Camera;
        var look = cam.Valid ? cam.Forward : Vector3.Zero;
        var fwd = look - up * Vector3.Dot(look, up);
        if (fwd.LengthSquared() < 1e-6f)
            fwd = Vector3.Cross(up, MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ);
        fwd = Vector3.Normalize(fwd);
        var right = Vector3.Cross(fwd, up);
        float down = -Vector3.Dot(felt, up), ahead = Vector3.Dot(felt, fwd), side = Vector3.Dot(felt, right);

        var floating = g < ShipGravity.FloatG;
        var col = crushing || g >= ShipGravity.PinG ? CmdRed : g >= ShipGravity.SlowG ? CmdAmber : floating ? CmdDim : CmdCyan;
        var a = crushing ? 0.55f + 0.45f * blink : 1f; // (a soft blink: the figure stays readable when it matters most)
        var x = p0.X + 108 * s;
        CmdText(dl, new float2(x, y), 11f, col == CmdCyan ? CmdWhite : col, 0.9f * a, "GRAVITY", 0, false);
        CmdText(dl, new float2(x, y + 15 * s), 10f, floating ? CmdDim : col == CmdCyan ? CmdWhite : col, floating ? 0.95f : col == CmdCyan ? 0.62f : 0.8f * a,
            floating ? "WEIGHTLESS" : VpWays[VpWay(down, ahead, side)], 0, false);

        // The dial, in the bars' column: a ring with ticks at its four points (no crosshair through the middle, where a straight
        // down arrow would be lost on it).
        var c = new float2(p0.X + 201 * s, y + 15 * s);
        var r = 14 * s;
        var tick = r * 0.72f;
        ImDrawListExtensions.AddCircleFilled(dl, in c, r, Hud.Colour(col, 0.07f), 32);
        CmdRing(dl, c, r, CmdDim, 0.85f, 1f);
        CmdLine(dl, new float2(c.X - r, c.Y), new float2(c.X - tick, c.Y), CmdDim, 0.6f);
        CmdLine(dl, new float2(c.X + tick, c.Y), new float2(c.X + r, c.Y), CmdDim, 0.6f);
        CmdLine(dl, new float2(c.X, c.Y - r), new float2(c.X, c.Y - tick), CmdDim, 0.6f);
        CmdLine(dl, new float2(c.X, c.Y + tick), new float2(c.X, c.Y + r), CmdDim, 0.6f);
        if (!floating)
        {
            // The arrow grows with the g, full at the pin (by a low power, so 1 g still reaches well out: 58%); a pull straight
            // ahead or back is short, its end saying which: a filled dot pressed back, a ring pulled forward, else a head (under
            // a tenth of a g either way, so a still seat doesn't flicker between the two).
            var flat = MathF.Sqrt(side * side + down * down);
            var reach = r * 0.85f * MathF.Pow(MathF.Min(1f, g / ShipGravity.PinG), 0.35f) * flat / (g * ShipGravity.G);
            var tip = flat > 1e-3f ? new float2(c.X + side / flat * reach, c.Y + down / flat * reach) : c;
            CmdGlow(dl, c, tip, col, a, 2f);
            if (ahead < -0.1f * ShipGravity.G)
                ImDrawListExtensions.AddCircleFilled(dl, in tip, 3.2f * s, Hud.Colour(col, a), 12);
            else if (ahead > 0.1f * ShipGravity.G)
                CmdRing(dl, tip, 3.2f * s, col, a, 1.4f);
            else if (flat > 1e-3f)
            {
                float ux = side / flat, uy = down / flat, len = 4.5f * s, half = 3f * s;
                ImDrawListExtensions.AddTriangleFilled(dl, new float2(tip.X + ux * 1.5f * s, tip.Y + uy * 1.5f * s),
                    new float2(tip.X - ux * len - uy * half, tip.Y - uy * len + ux * half),
                    new float2(tip.X - ux * len + uy * half, tip.Y - uy * len - ux * half), Hud.Colour(col, a));
            }
        }

        // The g, and what it does to them.
        CmdText(dl, new float2(p1.X - 10 * s, y), 13.5f, col, a, VpGText(g), 2, false);
        if (crushing || g >= ShipGravity.PinG)
            CmdText(dl, new float2(p1.X - 10 * s, y + 17 * s), 10f, col, 0.9f * a, crushing ? "CRUSHING" : "PINNED", 2, false);
    }

    private static string VpGText(float g)
    {
        var k = Math.Clamp((int)MathF.Round(g * 10), 0, VpG.Length - 1);
        return VpG[k] ??= $"{k / 10}.{k % 10} G";
    }

    private static string[] MakeVpWays()
    {
        var s = new string[6 * 7];
        for (var a = 0; a < 6; a++)
            for (var b = -1; b < 6; b++)
                s[a * 7 + b + 1] = b < 0 ? VpWayWords[a] : VpWayWords[a] + ", " + VpWayWords[b];
        return s;
    }

    /// <summary>The pull's way as an index into <see cref="VpWays"/>: the strongest of down/up, forward/back and right/left, and a second one when it is a third of the first or more.</summary>
    private static int VpWay(float down, float ahead, float side)
    {
        float ad = MathF.Abs(down), aa = MathF.Abs(ahead), asd = MathF.Abs(side);
        var first = ad >= aa && ad >= asd ? 0 : aa >= asd ? 1 : 2;
        var firstMag = first == 0 ? ad : first == 1 ? aa : asd;
        var second = -1;
        var secondMag = 0f;
        for (var k = 0; k < 3; k++)
        {
            var m = k == 0 ? ad : k == 1 ? aa : asd;
            if (k != first && m > secondMag)
            {
                second = k;
                secondMag = m;
            }
        }
        var also = second >= 0 && secondMag >= firstMag * 0.35f ? VpWayWord(second, down, ahead, side) : -1;
        return VpWayWord(first, down, ahead, side) * 7 + also + 1;
    }

    private static int VpWayWord(int k, float down, float ahead, float side) => k switch
    {
        0 => down >= 0 ? 0 : 1,
        1 => ahead >= 0 ? 2 : 3,
        _ => side >= 0 ? 4 : 5,
    };

    /// <summary>
    /// The pack's icon in the heading while an MMU is worn: its box seen from behind, the two arms forward to their hand
    /// controllers, and a jet at each corner, lit while it burns (thrust or the dampeners' brake). Its colour is its fuel's.
    /// </summary>
    private void VpMmuIcon(ImDrawListPtr dl, float2 c, Player me, float blink)
    {
        var s = _scale;
        var fp = Math.Clamp(me.Sim.JetFuel * 100 / Eva.MmuFull, 0, 100);
        var col = SpColour(fp, 25);
        var a = fp <= 15 ? blink : 1f;
        var burning = me.Sim.Canopy == PilotGear.Burning;
        float2 P(float x, float y) => new(c.X + x * s, c.Y + y * s);
        CmdGlow(dl, P(-7, -10), P(7, -10), col, a); // (the box)
        CmdGlow(dl, P(7, -10), P(7, 10), col, a);
        CmdGlow(dl, P(7, 10), P(-7, 10), col, a);
        CmdGlow(dl, P(-7, 10), P(-7, -10), col, a);
        dl.AddRectFilled(P(-5, -8), P(5, 8), Hud.Colour(col, 0.16f * a));
        CmdLine(dl, P(-7, 2), P(-13, 2), col, 0.8f * a, 1.4f); // (its arms and their hand controllers)
        CmdLine(dl, P(-13, 2), P(-13, -4), col, 0.8f * a, 1.4f);
        CmdLine(dl, P(7, 2), P(13, 2), col, 0.8f * a, 1.4f);
        CmdLine(dl, P(13, 2), P(13, -4), col, 0.8f * a, 1.4f);
        for (var k = 0; k < 4; k++)
        {
            var sx = (k & 1) == 0 ? -1f : 1f;
            var sy = k < 2 ? -1f : 1f;
            CmdLine(dl, P(7 * sx, 10 * sy), P(10 * sx, 13 * sy), col, 0.7f * a, 1.2f); // (a corner jet)
            if (burning)
                CmdGlow(dl, P(10 * sx, 13 * sy), P(13 * sx, 16 * sy), CmdWhite, 0.55f + 0.35f * blink, 1.6f);
        }
        CmdText(dl, P(18, -6), 10f, col, 0.9f * a, "MMU", 0, false);
    }

    private string? _vpBootsKey;
    private string _vpBootsOn = "", _vpBootsOff = "";

    /// <summary>The boots' state and their key ("ON [7]"), made again only when the key is rebound.</summary>
    private string VpBootsText(bool on)
    {
        var key = Keybinds.Name(_hudSettings!.Bind(GameAction.MagBoots));
        if (!ReferenceEquals(key, _vpBootsKey))
        {
            _vpBootsKey = key;
            _vpBootsOn = $"ON [{key}]";
            _vpBootsOff = $"OFF [{key}]";
        }
        return on ? _vpBootsOn : _vpBootsOff;
    }

    private int _vpAboardN = -1;
    private string _vpAboard = "";

    private string VpAboard(int n)
    {
        if (n != _vpAboardN)
        {
            _vpAboardN = n;
            _vpAboard = n == 1 ? "SHIP AIR FOR 1 ABOARD" : $"SHIP AIR FOR {n} ABOARD";
        }
        return _vpAboard;
    }

    /// <summary>One row: its label, a bar and its value, then the next row's height.</summary>
    private void VpRow(ImDrawListPtr dl, float2 p0, float2 p1, ref float y, string label, int pct, Vector3 col, float a, string value)
    {
        var s = _scale;
        var lx = p0.X + 108 * s;
        var bx0 = p0.X + 172 * s;
        var bx1 = p1.X - 62 * s;
        CmdText(dl, new float2(lx, y), 11f, col == CmdCyan ? CmdWhite : col, 0.9f * a, label, 0, false);
        var by = y + 7 * s;
        if (pct < 0)
        {
            // No bar: a switch's two marks, the lit one its state (the inertia dampeners: on at the right, off at the left).
            var on = col == CmdCyan;
            bx1 -= 12 * s; // (room for its longer word)
            var mid = (bx0 + bx1) * 0.5f;
            dl.AddRectFilled(new float2(bx0, by - 2.5f * s), new float2(mid - 2 * s, by + 2.5f * s), Hud.Colour(on ? CmdDim : col, on ? 0.25f : 0.9f * a));
            dl.AddRectFilled(new float2(mid + 2 * s, by - 2.5f * s), new float2(bx1, by + 2.5f * s), Hud.Colour(on ? col : CmdDim, on ? 0.9f * a : 0.25f));
        }
        else
            dl.AddRectFilled(new float2(bx0, by - 1.5f * s), new float2(bx1, by + 1.5f * s), Hud.Colour(CmdDim, 0.25f));
        if (pct > 0)
            dl.AddRectFilled(new float2(bx0, by - 1.5f * s), new float2(bx0 + (bx1 - bx0) * pct / 100f, by + 1.5f * s), Hud.Colour(col, 0.9f * a));
        CmdText(dl, new float2(p1.X - 10 * s, y), 11f, col, a, value, 2, false);
        y += 19.5f * s;
    }
}
