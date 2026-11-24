using System.Numerics;

namespace CallOfSlop;

/// <summary>A world's ship gravity rule (docs/sandbox/SHIP-GRAVITY.md). The byte is in saves and on the wire: add at the end.</summary>
public enum ShipGravityMode : byte
{
    Deck, // every deck pulls 1 g along its own down, wherever the ship is; a hard burn, brake or turn is felt on top (Karl, 2026-11-23)
    Space, // ships in space have no deck gravity: what their riders feel is their acceleration (on a planet, decks as before)
    Everywhere, // every vehicle, everywhere: what a rider feels is the world's pull less the vehicle's acceleration (true physics)
}

/// <summary>
/// Felt gravity aboard vehicles (Sean, 2026-11-23; the study is the "Ship gravity from acceleration" doc): with the world's rule on,
/// a rider's pull is the vehicle's proper acceleration at them, not the deck's 1 g. At rest in space nothing pulls; a burn gives
/// weight along it; past <see cref="SlowG"/> moving gets harder and past <see cref="PinG"/> it stops; past <see cref="HarmG"/>
/// it hurts, less in a seat or a suit. A Gravity Generator's box keeps its own down as before (artificial gravity).
/// <para>
/// Server: each body's felt pull (<see cref="Construct.Felt"/>, world axes, filtered over <see cref="FilterSeconds"/> so a
/// contact's one-tick spike is not felt) is worked out once a tick in <see cref="ConstructRide.BeforePhysics"/>; a rider's, at
/// their chest and in their segment's frame, goes into <see cref="MoveState.RideFelt"/> after their step, and the owner's
/// snapshot carries it, so a client's prediction steps with the server's value. Loose bodies aboard take the same rule
/// (<see cref="ConstructFrames"/>'s pull), and cargo nobody is near settles faster in 0 g (<see cref="Settles"/>) so it sleeps.
/// Nothing here allocates; with the rule off (<see cref="ShipGravityMode.Deck"/>) every path is one compare.
/// </para>
/// </summary>
public static class ShipGravity
{
    /// <summary>The world's rule (the host's: its world's own, or the lobby's; a client hears it in the Welcome and the lobby state).</summary>
    public static ShipGravityMode Mode;

    public const float G = 9.81f;

    /// <summary>Felt this much (g) or more, moving gets slower, down to <see cref="PinnedSpeed"/> of a walk at <see cref="PinG"/>.</summary>
    public const float SlowG = 1.5f;

    /// <summary>Felt this much (g) or more, a standing rider can't move or jump: pinned.</summary>
    public const float PinG = 3f;

    public const float PinnedSpeed = 0.2f;

    /// <summary>Felt this much (g) or more, a person aboard is hurt (<see cref="HarmPerG"/> a second for each g past it), unless seated or suited for more.</summary>
    public const float HarmG = 6f;

    /// <summary>Health a second for each g past the limit.</summary>
    public const float HarmPerG = 6f;

    /// <summary>A seat takes this many times <see cref="HarmG"/> (strapped in, the body braced).</summary>
    public const float SeatFactor = 2.5f;

    /// <summary>A space suit takes this many times as much (its g-suit bladders).</summary>
    public const float SuitFactor = 1.3f;

    /// <summary>
    /// Deck gravity: the hull's acceleration is felt on top of the deck's 1 g past this much (g), less this much (Karl, 2026-11-23:
    /// a high-g burn let riders jump about). Gentle driving and cruising feel as before; a hard burn pins them as it does with the
    /// rule on. The fade is continuous, so nothing pops as a burn builds.
    /// </summary>
    public const float DeckFeltFrom = 0.25f;

    /// <summary>Under this (g) a rider floats: a jump pushes off, and in the air the move keys drift them (Jump up, Crouch down).</summary>
    public const float FloatG = 0.15f;

    /// <summary>Magnet boots on: walking is this share of the speed it would be.</summary>
    public const float BootsSpeed = 0.6f;

    /// <summary>How fast a floating rider's drift goes (m/s) and turns towards what the keys ask (1/s).</summary>
    public const float DriftSpeed = 2f, DriftControl = 1.5f;

    /// <summary>The felt pull is the raw one filtered over this (s): a bump's one-tick spike isn't felt, a burn is within a few ticks.</summary>
    public const float FilterSeconds = 0.12f;

    /// <summary>Cargo floats freely in 0 g; where nobody is within this (m), it is slowed until it sleeps (<see cref="Settles"/>).</summary>
    public const float WatchRange = 30f;

    /// <summary>At most this many sleeping bodies wake in one tick when a frame's pull changes (the rest wake in the ticks after).</summary>
    public const int WakesPerTick = 24;

    /// <summary>Is the rule on for things aboard <paramref name="c"/> now?</summary>
    public static bool Active(Construct c) => Mode switch
    {
        ShipGravityMode.Everywhere => true,
        ShipGravityMode.Space => c.SpaceShip != 0 || c.FeltSpace, // (out of the bubble, or a bubble with no pull: space; Update keeps it)
        _ => false,
    };

    /// <summary>The world's own pull at <paramref name="at"/> (the map's field and its gravity setting; no gravity parts).</summary>
    public static Vector3 WorldPull(Vector3 at) =>
        (GravityFrames.Base is { } f ? f.GravityAt(at) : new Vector3(0, -World.Gravity, 0)) * World.GravityScale;

    /// <summary>Server, once a tick per body after its acceleration (<see cref="ConstructRide.BeforePhysics"/>): its felt pull, filtered.</summary>
    public static void Update(Construct c)
    {
        if (c.Asleep && c.SpaceShip == 0 && c.CarriedBy == 0)
            return; // (resting: its last pull stands, and the map's field isn't asked for every sleeper every tick)
        Vector3 raw;
        if (Mode == ShipGravityMode.Deck)
            raw = c.SpaceShip != 0 ? (ConstructFrames.SpaceTimeSpeed > 1 ? Vector3.Zero : -c.SpaceAccel) // (deck gravity: only the hull's change of motion, the deck stands in for the world's pull)
                : c.CarriedBy != 0 ? c.Felt : -c.Accel;
        else if (c.SpaceShip != 0)
            raw = ConstructFrames.SpaceTimeSpeed > 1 ? Vector3.Zero : -c.SpaceAccel; // (a ship on its flight is in free fall: only its drive is felt; at a raised time speed nothing)
        else if (c.CarriedBy != 0)
            raw = c.Felt; // (a docked craft feels its carrier's: left as it was, the carrier's own is read by its riders)
        else
        {
            var pull = WorldPull(c.WorldCom);
            c.FeltSpace = pull.LengthSquared() < 0.25f; // (a bubble with no pull counts as space)
            raw = pull - c.Accel; // (resting: the world's pull; falling freely: nothing)
        }
        c.Felt += (raw - c.Felt) * MathF.Min(1f, World.TickDt / FilterSeconds);
    }

    /// <summary>Server, after a rider's step: what they feel at their chest, in their segment's frame (or nothing: the deck's 1 g).</summary>
    public static void SetRider(ref MoveState s, Construct c)
    {
        s.RideFeltOn = false;
        if (s.RideUp == Vector3.Zero || s.RideBody != c.Id)
            return;
        var body = c.CarriedBy != 0 && ConstructRide.Bodies?.Get(c.CarriedBy) is { } carrier ? carrier : c; // (in a docked craft: the carrier's motion)
        var deck = Mode == ShipGravityMode.Deck;
        if (!deck && !Active(body))
            return;
        var chest = s.Position + s.RideUpW * 1.2f;
        if (ConstructGravity.Generators > 0 && ConstructGravity.GenAt(chest, 0) >= 0)
            return; // (a Gravity Generator's box: its own down, as before)
        var r = chest - body.WorldCom;
        var felt = body.Felt - Vector3.Cross(body.AngVel, Vector3.Cross(body.AngVel, r)); // (a turning hull throws them outwards)
        c.WorldFrame(s.RideSeg, out var q, out _);
        if (deck)
        {
            if (felt.LengthSquared() <= DeckFeltFrom * G * DeckFeltFrom * G)
                return; // (as before: the deck's 1 g alone)
            felt = DeckFelt(Vector3.Transform(-Vector3.Normalize(s.RideUp), q), felt);
        }
        s.RideFelt = Vector3.Transform(felt, Quaternion.Conjugate(q));
        s.RideFeltOn = true;
    }

    /// <summary>Deck gravity: the deck's 1 g along <paramref name="downW"/>, and the hull's acceleration on top past <see cref="DeckFeltFrom"/> (faded in).</summary>
    public static Vector3 DeckFelt(Vector3 downW, Vector3 hull)
    {
        var a = hull.Length();
        var from = DeckFeltFrom * G;
        var deck = downW * G; // (1 g in felt terms; the mover's own deck gravity is stronger, Rider scales it)
        return a <= from ? deck : deck + hull * ((a - from) / a);
    }

    /// <summary>
    /// Server, each tick: the pull a person feels now, world axes (the felt-g readout, UiVitalsPanel.cs): seated, the seat's
    /// vehicle's; standing aboard, their ride's; on the ground, the world's; falling or floating, nothing.
    /// </summary>
    public static Vector3 PersonPull(in MoveState s, ConstructWorld w, ushort seatBody, bool alive)
    {
        if (!alive)
            return Vector3.Zero;
        if (seatBody != 0)
        {
            if (w.Get(seatBody) is not { } seat)
                return Vector3.Zero;
            var body = seat.CarriedBy != 0 && w.Get(seat.CarriedBy) is { } carrier ? carrier : seat;
            var down = Vector3.Transform(-Vector3.UnitY, seat.Rot);
            if (Mode == ShipGravityMode.Deck)
                return DeckFelt(down, body.Felt);
            return Active(body) ? body.Felt : down * G;
        }
        if (s.RideBody != 0 && s.RideUp != Vector3.Zero && s.RideUpW != Vector3.Zero)
        {
            if (s.RideFeltOn && w.Get(s.RideBody) is { } c)
            {
                c.WorldFrame(s.RideSeg, out var q, out _);
                return Vector3.Transform(s.RideFelt, q);
            }
            return -Vector3.Normalize(s.RideUpW) * G; // (the deck's 1 g)
        }
        return s.Grounded ? WorldPull(s.Position) : Vector3.Zero;
    }

    /// <summary>A rider's step with the rule on: the pull along their down, the rest as a push, and how much they can still move.</summary>
    public static float Rider(ref MoveState s, Vector3 up, ref Vector3 wish, ref float speed, ref float control, ref Buttons buttons, out Vector3 push, bool boots = false)
    {
        var l = s.RideFelt;
        var along = -Vector3.Dot(l, up);
        push = along > 0 ? l + up * along : l; // (what isn't down: sideways in a turn, up to the ceiling when they brake hard)
        if (Mode == ShipGravityMode.Deck)
        {
            // Deck gravity holds them to the deck: the burn makes them heavier along their down (weak jumps, slow moving,
            // pinned), never slides or lifts them (a turning or braking vehicle keeps its riders on their spots, as before).
            along = World.Gravity * World.GravityScale * l.Length() / G; // (the mover's deck gravity is 1 g: scaled by the g felt)
            push = Vector3.Zero;
        }
        var g = l.Length() / G;
        if (g >= PinG)
        {
            wish = Vector3.Zero;
            buttons &= ~Buttons.Jump;
        }
        else if (g > SlowG)
            speed *= 1f - (1f - PinnedSpeed) * (g - SlowG) / (PinG - SlowG);
        if (boots && s.Grounded)
        {
            // Magnet boots (Karl, 2026-11-24): the deck holds them, whatever pulls them off it or across it. At least the
            // mover's 1 g along their down, no push, no jump, a slower walk; a pin still pins.
            push = Vector3.Zero;
            buttons &= ~Buttons.Jump;
            speed *= BootsSpeed;
            return MathF.Max(along, World.Gravity * World.GravityScale);
        }
        if (g < FloatG && !s.Grounded)
        {
            // Floating: the keys drift them in the frame (Jump up, Crouch down), slowly, as pushing off and pulling along does.
            if ((buttons & Buttons.Jump) != 0)
                wish += up;
            if ((buttons & Buttons.Crouch) != 0)
                wish -= up;
            if (wish.LengthSquared() > 1f)
                wish = Vector3.Normalize(wish);
            speed = MathF.Min(speed, DriftSpeed);
            control = DriftControl;
        }
        return MathF.Max(0, along);
    }

    /// <summary>How much a person feels now (g), and the limit before it hurts them: riding, seated, or neither (0).</summary>
    public static float FeltG(in MoveState s, ConstructWorld w, ushort seatBody, ushort seatPart, out float limit, bool suit)
    {
        limit = Limit(w, seatBody, seatPart, suit);
        if (Mode == ShipGravityMode.Deck)
            return 0;
        if (seatBody != 0)
        {
            if (w.Get(seatBody) is not { } seat)
                return 0;
            var body = seat.CarriedBy != 0 && w.Get(seat.CarriedBy) is { } carrier ? carrier : seat;
            return Active(body) ? body.Felt.Length() / G : 0;
        }
        return s.RideFeltOn ? s.RideFelt.Length() / G : 0;
    }

    /// <summary>
    /// The felt g a person takes unhurt (<see cref="FeltG"/>'s limit; the vitals panel shows it, UiVitalsPanel.cs): <see cref="HarmG"/>,
    /// <see cref="SuitFactor"/> times in a suit; seated, a rated seat's own if more, else <see cref="SeatFactor"/> times.
    /// </summary>
    public static float Limit(ConstructWorld w, ushort seatBody, ushort seatPart, bool suit)
    {
        var limit = HarmG * (suit ? SuitFactor : 1f);
        if (seatBody == 0 || w.Get(seatBody) is not { } seat)
            return limit;
        var rated = seatPart < seat.PartCount ? seat.DefOf(seatPart).SeatG : 0; // (a crash couch's own rating: "seat"."g")
        return rated > 0 ? MathF.Max(rated, limit) : limit * SeatFactor;
    }

    // ---- Cargo in 0 g ---------------------------------------------------------------------------------------------

    private static readonly Vector3[] _watchers = new Vector3[256];
    private static int _watcherCount;
    private static int _wakes;

    private static uint _watchTick;

    /// <summary>Server, each tick for each person in play: where they are (cargo near them floats as it would). A new tick starts the list again.</summary>
    public static void Watch(uint tick, Vector3 at)
    {
        if (tick != _watchTick)
        {
            _watchTick = tick;
            _watcherCount = 0;
        }
        if (_watcherCount < _watchers.Length)
            _watchers[_watcherCount++] = at;
        _sinceWatch = 0;
    }

    /// <summary>Each tick's wake budget starts again (ConstructFrames.After, the first pass).</summary>
    public static void NewTick()
    {
        _wakes = 0;
        if (_sinceWatch < 255)
            _sinceWatch++; // (nobody watched for a while, everyone dead or gone: the list is stale, Settles reads it as empty)
    }

    private static byte _sinceWatch = 255;

    /// <summary>May a sleeping body woken by its frame's change wake this tick? (Rule off: always.)</summary>
    public static bool MayWake(Construct f)
    {
        if (Mode == ShipGravityMode.Deck || !Active(f))
            return true;
        return _wakes++ < WakesPerTick;
    }

    /// <summary>
    /// Does a loose body in <paramref name="f"/>'s frame settle faster now? In 0 g aboard (the rule on, the frame's pull under
    /// <see cref="FloatG"/>) with nobody within <see cref="WatchRange"/>: what drifts or bounces there is slowed, as the hold's air
    /// would, until it sleeps. Nobody near sees it; near a person it floats as it would.
    /// </summary>
    public static bool Settles(Construct c, Construct f)
    {
        if (Mode == ShipGravityMode.Deck || !Active(f) || f.Felt.LengthSquared() > FloatG * G * FloatG * G)
            return false;
        var at = c.WorldCom;
        var watchers = _sinceWatch > 2 ? 0 : _watcherCount;
        for (var i = 0; i < watchers; i++)
            if (Vector3.DistanceSquared(_watchers[i], at) < WatchRange * WatchRange)
                return false;
        return true;
    }

    public static string Label(ShipGravityMode m) => m switch
    {
        ShipGravityMode.Space => "Felt in space",
        ShipGravityMode.Everywhere => "Felt everywhere",
        _ => "Deck gravity",
    };

    public static string Hint(ShipGravityMode m) => m switch
    {
        ShipGravityMode.Space => "Ships in space have no deck gravity: you float at rest and feel their thrust. Hard burns pin you, harder ones hurt unless you sit. On planets decks pull as usual.",
        ShipGravityMode.Everywhere => "Every vehicle, everywhere: you feel the world's pull less the vehicle's acceleration. Turns push you sideways, burns pin you, hard ones hurt unless you sit.",
        _ => "Every deck pulls 1 g towards its floor, wherever the ship is. Hard burns, brakes and turns still press you on top of it.",
    };
}
