using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Boarding assist" (board item dev-sandbox-board-assist-ba01; ConstructRide.cs, ConstructWorld.BlockPlayer): seeded
    /// and scripted on the Construct Yard with decks moved kinematically. A player jumping onto a flatbed moving at 8 m/s
    /// lands and rides (within 10 cm of the landing spot a second later); running into a 1.2 m deck edge with jump vaults
    /// onto it at 6 m/s; E at a boarding ladder on a vehicle at 10 m/s latches on and is standing on the deck within a
    /// second; running away from a vehicle closing at 6 m/s (2 m/s between them) does no damage, standing still does; E
    /// on a seat aimed 2.5 m away boards it while the vehicle rolls at 5 m/s; a friendly bot follows its leader into a
    /// vehicle's free seat.
    /// </summary>
    private static void CheckBoardingAssist()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"boarding assist: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        BoardJump();
        BoardVault();
        BoardLadder();
        BoardGentleSides();
        BoardSeat();
        BoardBot(yard);
    }

    /// <summary>Moves a deck on at <paramref name="vel"/> for a tick.</summary>
    private static void BoardRoll(ConstructWorld w, Construct c, Vector3 vel) => RidePose(w, c, c.Pos + vel * World.TickDt, c.Rot, vel, Vector3.Zero);

    private static void BoardJump()
    {
        var w = new ConstructWorld();
        if (RideDeck(w, -10, -14, 0.4f, 3) is not { } c)
            return;
        var vel = new Vector3(0, 0, 8f);
        for (var t = 0; t < 30; t++)
            BoardRoll(w, c, vel);
        var front = c.ToWorld(new Vector3(0, 0, 5));
        var s = new MoveState { Position = ConstructWorld.Ground(World.Query, front.X + 0.2f, front.Z + 4f), Grounded = true };
        var jumped = false;
        var landed = Vector3.Zero;
        var rodeAt = -1;
        var worst = 0f;
        for (var t = 0; t < 150; t++)
        {
            BoardRoll(w, c, vel);
            var gap = s.Position.Z - c.ToWorld(new Vector3(0, 0, 5)).Z;
            var jump = !jumped && gap < 1.2f;
            jumped |= jump;
            RideTick(w, ref s, jump ? Buttons.Jump : Buttons.None);
            if (rodeAt < 0 && s.RideBody == c.Id && s.Grounded && s.RideAir == 0)
            {
                rodeAt = t;
                landed = s.RideLocal;
            }
            else if (rodeAt >= 0 && t <= rodeAt + 60)
                worst = MathF.Max(worst, s.RideBody == c.Id ? Vector3.Distance(s.RideLocal with { Y = 0 }, landed with { Y = 0 }) : 99f);
        }
        Line($"boarding: jumping onto a flatbed at 8 m/s: {(rodeAt >= 0 ? "landed and rides it" : "never rode it")}, {worst * 100:0.0} cm from the landing spot over the next second");
        if (rodeAt < 0 || worst > 0.10f)
            Fail($"boarding: a player jumping onto a moving flatbed didn't land and ride it ({worst * 100:0.0} cm)");
    }

    private static void BoardVault()
    {
        var w = new ConstructWorld();
        if (RideDeck(w, -10, -14, 1.1f, 3) is not { } c)
            return;
        var vel = new Vector3(0, 0, 6f);
        for (var t = 0; t < 30; t++)
            BoardRoll(w, c, vel);
        var mid = c.ToWorld(new Vector3(1, 0, 3.5f));
        var s = new MoveState { Position = ConstructWorld.Ground(World.Query, mid.X + 1.0f, mid.Z), Grounded = true };
        var top = c.ToWorld(DeckTop(0, 2)).Y;
        var high = top - s.Position.Y;
        var rode = -1;
        var climbed = false;
        for (var t = 0; t < 90 && rode < 0; t++)
        {
            BoardRoll(w, c, vel);
            RideTick(w, ref s, Buttons.Forward | Buttons.Jump, -MathF.PI / 2); // (into its side, towards -x)
            climbed |= s.RideClimb > 0;
            if (s.RideBody == c.Id && s.RideClimb == 0 && s.Grounded)
                rode = t;
        }
        Line($"boarding: running into a {high:0.00} m deck edge with jump at 6 m/s: {(rode >= 0 ? $"aboard after {rode / 60f:0.00} s" : "not aboard")} (vaulted {climbed}), feet {s.Position.Y - top:+0.00;-0.00} m from its top");
        if (rode < 0 || !climbed || MathF.Abs(s.Position.Y - top) > 0.05f)
            Fail($"boarding: running into a 1.2 m deck edge with jump didn't vault the player onto the moving deck (vaulted {climbed})");
    }

    private static void BoardLadder()
    {
        var w = new ConstructWorld();
        if (RideDeck(w, -10, -14, 1.2f, 3) is not { } c)
            return;
        var ladder = Fit(w, c, 1, "+x", "Boarding Ladder", "back");
        if (ladder < 0)
            return;
        c.Rebuild();
        var vel = new Vector3(0, 0, 10f);
        for (var t = 0; t < 30; t++)
            BoardRoll(w, c, vel);
        var at = ConstructRender.PartCentre(c, ladder);
        var s = new MoveState { Position = ConstructWorld.Ground(World.Query, at.X + 0.9f, at.Z + 12f), Grounded = true };
        string? why = "never near it";
        var grabbedAt = -1;
        var aboardAt = -1;
        for (var t = 0; t < 150 && aboardAt < 0; t++)
        {
            BoardRoll(w, c, vel);
            if (grabbedAt < 0 && MathF.Abs(ConstructRender.PartCentre(c, ladder).Z - s.Position.Z) < 0.8f)
            {
                why = ConstructRide.GrabOn(ref s, c, ladder, World.PlayerHeight, (c.Vel - s.Velocity).Length() * 0.15f);
                if (why == null)
                    grabbedAt = t;
            }
            RideTick(w, ref s);
            if (grabbedAt >= 0 && s.RideBody == c.Id && s.RideClimb == 0 && s.Grounded && s.RideAir == 0)
                aboardAt = t;
        }
        var top = c.ToWorld(DeckTop(0, 2)).Y;
        Line($"boarding: E at a boarding ladder on a deck at 10 m/s: {why ?? "latched on"}, standing on the deck {(aboardAt >= 0 ? $"{(aboardAt - grabbedAt) / 60f:0.00} s later" : "never")} (feet {s.Position.Y - top:+0.00;-0.00} m from its top)");
        if (why != null || aboardAt < 0 || aboardAt - grabbedAt > 60 || MathF.Abs(s.Position.Y - top) > 0.05f)
            Fail($"boarding: E at a boarding ladder on a moving vehicle didn't latch on and stand them on the deck within a second ({why})");
        if (!ConstructParts.Need("Grab Handle").Boarding || !ConstructParts.Need("Step Plate").Boarding || !ConstructParts.Need("Boarding Ladder").Boarding)
            Fail("boarding: the grab handle, step plate and boarding ladder should be boarding parts");
    }

    private static void BoardGentleSides()
    {
        (float Hit, int Damage) Closing(float playerSpeed)
        {
            var w = new ConstructWorld();
            if (RideDeck(w, -10, -14, 1.2f, 3) is not { } c)
                return (99, 99);
            var vel = new Vector3(6f, 0, 0);
            for (var t = 0; t < 30; t++)
                BoardRoll(w, c, vel);
            var side = c.ToWorld(new Vector3(1, 0, 2));
            var s = new MoveState { Position = ConstructWorld.Ground(World.Query, side.X + World.PlayerRadius + 0.3f, side.Z), Grounded = true, Velocity = new Vector3(playerSpeed, 0, 0) };
            var worst = 0f;
            var mass = c.Mass;
            for (var t = 0; t < 30; t++)
            {
                BoardRoll(w, c, vel);
                s.Velocity.X = MathF.Max(s.Velocity.X, playerSpeed); // (running on away from it)
                s.Position += s.Velocity * World.TickDt;
                w.BlockPlayer(ref s, World.PlayerHeight, server: true, who: 1);
                worst = MathF.Max(worst, w.LastHitSpeed);
            }
            return (worst, worst > Game.RunOverSpeed ? Game.RunOverDamage(worst, mass) : 0);
        }
        var away = Closing(4f);
        var still = Closing(0f);
        Line($"boarding: a deck closing at 6 m/s on a player running away at 4 m/s: hit {away.Hit:0.00} m/s, {away.Damage} damage; on one standing still: hit {still.Hit:0.00} m/s, {still.Damage} damage");
        if (away.Damage != 0 || away.Hit > 2.3f)
            Fail("boarding: a vehicle 2 m/s faster than a player running alongside hurt them (only the speed between them should count)");
        if (still.Damage <= 0)
            Fail("boarding: a vehicle at 6 m/s into a standing player should still hurt");
    }

    private static void BoardSeat()
    {
        string? TryAt(float speed, float across, float behind)
        {
            var w = new ConstructWorld();
            if (Buggy(w, -10, -14, 0) is not { } car)
                return "no buggy";
            StepConstructs(w, 60);
            var seat = car.DriveSeat;
            if (seat < 0)
                return "no seat";
            var centre = ConstructRender.PartCentre(car, seat);
            var fwd = Vector3.Normalize(Vector3.Transform(car.SeatForward(), car.Rot) with { Y = 0 });
            var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, fwd));
            car.Vel = fwd * speed;
            car.Asleep = speed == 0;
            var edits = new ConstructEdits();
            var why = edits.Use(w, 3, centre + side * across - fwd * behind + new Vector3(0, 0.9f, 0), car.Id, seat);
            return why ?? (edits.Hand(3).Seated ? null : "not seated");
        }
        // Aimed from 2.5 m while it rolls at 5 m/s; the server hears it 0.25 s later, the buggy 1.25 m on.
        var rolling = TryAt(5f, 2.5f, 1.25f);
        // Past the use reach at the server (4.3 m): reached rolling (it moved on since the aim), refused parked.
        var further = TryAt(5f, 3.0f, 3.0f);
        var parked = TryAt(0f, 3.0f, 3.0f);
        Line($"boarding: E on a seat aimed 2.5 m away while the buggy rolls at 5 m/s: \"{rolling ?? "seated"}\"; 4.3 m away rolling: \"{further ?? "seated"}\", parked: \"{parked ?? "seated"}\"");
        if (rolling != null || further != null)
            Fail($"boarding: E on a seat a step away didn't board it while the vehicle rolled at 5 m/s (\"{rolling}\", \"{further}\")");
        if (parked == null)
            Fail("boarding: a parked vehicle's seat was reached from further than the use reach");
    }

    private static void BoardBot(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        var at = ConstructWorld.Ground(World.Query, 10, -14);
        if (SpawnHost(g, ref now, at) is not { } me)
        {
            Fail("boarding: no local player");
            return;
        }
        var bp = ConstructBuild.FindBlueprint("Wired Buggy");
        var car = bp == null ? null : ConstructBuild.BuildOnGround(g.Constructs, bp, ConstructWorld.Ground(World.Query, 10, -8), 0, out _);
        if (car == null)
        {
            Fail("boarding: the Wired Buggy didn't build");
            return;
        }
        Run(g, ref now, 1.0);
        var seat = car.DriveSeat;
        me.Teleport(ConstructRender.PartCentre(car, seat) + new Vector3(1.5f, 0, 0) with { Y = at.Y });
        var sat = g.SandboxHands.Use(g.Constructs, me.Id, me.Eye, car.Id, seat);
        g.SandboxSpawnRequest(me, SbSpawnKind.FriendlyBot, BotDifficulty.Normal, 0, 0, 0, 1, ConstructWorld.Ground(World.Query, 16, -2));
        Player? bot = null;
        var boardedAt = -1.0;
        for (var t = 0.0; t < 12 && boardedAt < 0; t += 0.25)
        {
            Run(g, ref now, 0.25);
            bot ??= g.Players.Values.FirstOrDefault(p => p.SbSpawned);
            if (bot != null && g.SandboxHands.Hand(bot.Id).Seated && g.SandboxHands.Hand(bot.Id).SeatBody == car.Id)
                boardedAt = t;
        }
        Line($"boarding: the host in the Wired Buggy's drive seat (\"{sat ?? "seated"}\"); a friendly bot {(boardedAt >= 0 ? $"followed and boarded its free seat in {boardedAt:0.0} s" : $"didn't board (at {(bot == null ? "-" : $"{Vector3.Distance(bot.Position, car.WorldCom):0.0} m")})")}");
        if (sat != null || boardedAt < 0)
            Fail("boarding: a friendly bot didn't follow its leader into the vehicle's free seat");
    }
}
