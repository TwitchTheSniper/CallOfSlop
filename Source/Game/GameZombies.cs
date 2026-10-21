using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a push cart is doing.</summary>
public enum CartMode : byte
{
    /// <summary>In its owner's hands: rolls in front of them. Both hands are on the bar, so it doesn't fire.</summary>
    Pushed,

    /// <summary>Standing where it was left (parked, or switched away from). Tap E at its bar to man it, hold E to push it.</summary>
    Idle,

    /// <summary>
    /// Manned: parked, with its owner standing at the push bar aiming and firing its gun by hand (within the traverse
    /// limits). Decided by the user: no auto-firing turret (that may come later as a separate deployable).
    /// </summary>
    Manned,
}

/// <summary>A push cart in the world: one per owner (server-owned; clients get <see cref="Msg.CartState"/>).</summary>
public sealed class CartEntity
{
    public byte Owner;
    public CartMode Mode;
    public CartBody Body;
    public CartShape Shape;

    /// <summary>The cart build (the owner's; on clients the owner's own or a stand-in Push Cart for others).</summary>
    public WeaponDef? Def;

    /// <summary>Manned: where its gun points, as a yaw offset from the cart's facing and a pitch (both clamped to the traverse limits).</summary>
    public float AimYaw;

    public float AimPitch;

    /// <summary>When the mode last changed: the weapon-switch rules wait a moment so the owner's switch can catch up.</summary>
    public double ModeAt = -100;

    /// <summary>Client: when the last state arrived (carts not heard of for a while are dropped).</summary>
    public double HeardAt;

    /// <summary>Where the push bar is (the owner stands just behind it to push or man it).</summary>
    public Vector3 Bar => Body.Position - World.FlatForward(Body.Yaw) * (Shape.PushDist - CartShape.PlayerGap);

    /// <summary>Where the owner's feet are held while manning it: the spot they'd push it from.</summary>
    public Vector3 Stand => Body.Position - World.FlatForward(Body.Yaw) * Shape.PushDist;
}

/// <summary>
/// The Zombies preview systems that live in a match (docs/zombies/WEAPON-BUILDER.md), all used by the Workbench Range
/// today and by the Zombies mode later: the push cart as a real object (pushed, parked, manned as a gun emplacement),
/// weapon slots 3 and 4 (the per-run slot upgrade), the built melee weapon on the melee key (damage, reach, cleave,
/// stun), and the range's practice dummies. Everything is server-authoritative; standard matches never create any of it.
/// </summary>
public sealed partial class Game
{
    /// <summary>How close to a cart's push bar you must be to pick it up (or park it).</summary>
    public const float CartPickupRange = 2.4f;

    /// <summary>Manned cart gun: how far it turns either side of the cart's facing, and how far down and up it tilts (radians).</summary>
    public const float CartTraverseYaw = 60f * MathF.PI / 180f;

    public const float CartPitchDown = -15f * MathF.PI / 180f;
    public const float CartPitchUp = 30f * MathF.PI / 180f;

    /// <summary>Holding the interact key this long at a parked (or manned) cart pushes it instead of manning it (seconds).</summary>
    public const double CartHoldSeconds = 0.4;

    /// <summary>How close to the slot upgrade station the interact key buys it.</summary>
    public const float StationRange = 1.8f;

    /// <summary>Every push cart, by owner.</summary>
    public readonly Dictionary<byte, CartEntity> Carts = new();

    /// <summary>Where the weapon-slot upgrade is bought on this map (the Workbench Range has one; null = none).</summary>
    public Vector3? SlotStation;

    private bool _cartsLive; // server: carts existed when the last state went out (one empty state follows the last one)
    private readonly Dictionary<WeaponDef, CartShape> _cartShapes = new(ReferenceEqualityComparer.Instance);
    private static WeaponDef? _cartStandIn;

    /// <summary>Is <paramref name="d"/> a push cart build (its grip carries the "cart" flag)?</summary>
    public static bool IsCart(WeaponDef d) => d.BuildFlags.Contains("cart");

    /// <summary>The push cart among <paramref name="p"/>'s built weapons, if any.</summary>
    public static WeaponDef? CartWeaponOf(Player p)
    {
        foreach (var d in (ReadOnlySpan<WeaponDef?>)[p.BuiltPrimary, p.BuiltSecondary, p.ExtraSlots ? p.BuiltExtra[0] : null, p.ExtraSlots ? p.BuiltExtra[1] : null])
            if (d != null && IsCart(d))
                return d;
        return null;
    }

    /// <summary>The cart shape of a cart build (from its model; cached).</summary>
    public CartShape ShapeOf(WeaponDef d)
    {
        if (!_cartShapes.TryGetValue(d, out var s))
            _cartShapes[d] = s = d.BuiltMesh is { } m ? CartShape.From(m) : new CartShape(new Vector3(0.3f, 0.5f, 0.45f), Vector3.Zero, 1.0f, 1f);
        return s;
    }

    /// <summary>How many weapon slots the local player has: 2, or 3-4 once the slot upgrade is bought and the builds are there.</summary>
    public int WeaponSlots => Local is { ExtraSlots: true } me && me.BuiltExtra[0] != null ? me.BuiltExtra[1] != null ? 4 : 3 : 2;

    /// <summary>The slot after the active one (the swap key and the mouse wheel cycle through every slot you have).</summary>
    public int NextSlot => (ActiveSlot + 1) % WeaponSlots;

    // ---- Built weapons and the melee slot ------------------------------------------------

    /// <summary>
    /// Gives the local (host) player built weapons: slots 1-2, slots 3-4 (used once <paramref name="extraSlots"/> is on or
    /// the upgrade is bought), and a built melee weapon. Built weapons borrow a shipped gun's id for their sounds; any
    /// that collide get another id so every slot stays apart.
    /// </summary>
    public void UseBuiltWeapons(WeaponDef primary, WeaponDef secondary, WeaponDef? third, WeaponDef? fourth, MeleeDef? melee, bool extraSlots)
    {
        if (Local is not { } me)
            return;
        var defs = new List<WeaponDef?> { primary, secondary, third, fourth };
        var used = new HashSet<byte>();
        for (var i = 0; i < defs.Count; i++)
        {
            if (defs[i] is not { } d)
                continue;
            if (!used.Add(d.Id) || (i > 0 && defs.Take(i).Any(x => ReferenceEquals(x, d))))
            {
                var copy = d.Clone();
                copy.Id = Weapons.All.First(w => !used.Contains(w.Id)).Id;
                used.Add(copy.Id);
                defs[i] = copy;
            }
        }
        me.BuiltExtra[0] = defs[2];
        me.BuiltExtra[1] = defs[3];
        me.ExtraSlots = extraSlots;
        me.MeleeBuild = melee;
        UseBuiltWeapons(defs[0]!, defs[1]!);
    }

    /// <summary>Server: unlocks weapon slots 3-4 for <paramref name="p"/> for the rest of the run (free in the sandbox).</summary>
    public void ServerBuySlots(Player p)
    {
        if (p.ExtraSlots)
            return;
        p.ExtraSlots = true;
        if (p.Id == LocalId)
            OnRunState();
        else if (_clients.Values.FirstOrDefault(c => c.Id == p.Id) is { Guid: var g } && g.G != 0)
        {
            _w.Begin(Msg.RunState).U8(1);
            Net.SendTo(g, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    private void OnRunState()
    {
        if (Local is not { } me)
            return;
        SyncLocalSlots(me, refill: false);
        LoadoutNotice = me.ExtraSlots ? $"Weapon slots 3-4 unlocked: {WeaponSlots} slots this run." : "";
        Audio.Effect(Sfx.HitTick, 0.6f);
    }

    /// <summary>Server: a swing with a built melee weapon: up to its cleave count of targets in its reach and arc, nearest first; each is stunned.</summary>
    private void ServerBuiltMelee(Player attacker, MeleeDef m, float yaw, float pitch, double viewTick, double now)
    {
        attacker.NextMelee = now + m.Swing * 0.85; // a little slack for packet timing
        attacker.LastCombatTime = now;
        var origin = attacker.Eye;
        var dir = World.Forward(yaw, pitch);
        var rewindTo = Math.Max(viewTick, (double)ServerTick - World.MaxRewindTicks);
        var hits = MeleeTargets(attacker, origin, dir, m.Reach, m.ConeCos, rewindTo);
        byte flags = 0;
        foreach (var (victim, at, dist) in hits.Take(m.Cleave))
        {
            if (IsFriendlyHit(attacker, victim))
            {
                flags |= ShotHitFriendly; // a teammate: no damage or stun, a friendly marker (FriendlyHits.cs)
                continue;
            }
            var backstab = Perks.IsFromBehind(dir, at.Yaw);
            var dmg = m.Damage * (backstab ? 2f : 1f) * Perks.AttackMult(attacker.Perks, dist, dir, at.Yaw, victim.Health, melee: true);
            flags |= 1;
            if (backstab)
                flags |= 8;
            if (m.Stun > 0)
                victim.StunnedUntil = Math.Max(victim.StunnedUntil, now + m.Stun);
            if (DamagePlayer(attacker, victim, (int)MathF.Round(dmg), false, MeleeKillId, now, DamageKind.Melee))
                flags |= 4;
        }
        var taken = Math.Min(hits.Count, m.Cleave);
        flags |= ZombieMelee(attacker, origin, dir, m.Reach, m.ConeCos, m.Damage, m.Cleave - taken, m.Stun, viewTick, now); // Zombies (ZombieAI.cs)
        if (hits.Count == 0 && flags == 0)
            MeleeProps(attacker, origin, dir, now);
        if (attacker.Id == LocalId)
            OnOwnMelee(flags, now);
        else
            PlayMeleeFx(attacker, flags, now);
        _w.Begin(Msg.MeleeFx).U8(attacker.Id).U8(flags);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Everyone a swing could hit (within reach of the eyes, inside the arc, not behind a wall), nearest first (ties by id).</summary>
    private List<(Player P, HistoryEntry At, float Dist)> MeleeTargets(Player attacker, Vector3 origin, Vector3 dir, float reach, float coneCos, double rewindTo)
    {
        var list = new List<(Player, HistoryEntry, float)>();
        foreach (var p in Players.Values.OrderBy(x => x.Id))
        {
            if (p == attacker || !p.Alive || p.SpawnTick > rewindTo)
                continue;
            var at = HistoricState(p, rewindTo);
            var box = World.Hitbox(at.Position, at.Stance, at.Yaw);
            var closest = Vector3.Clamp(origin, box.Min, box.Max);
            var dist = (closest - origin).Length();
            if (dist > reach)
                continue;
            var toCenter = box.Center - origin;
            var aimed = box.Raycast(origin, dir, reach) != null
                        || (toCenter.LengthSquared() > 1e-4f && Vector3.Dot(Vector3.Normalize(toCenter), dir) >= coneCos);
            if (!aimed)
                continue;
            if (toCenter.Length() > 0.3f && World.Query.Raycast(origin, Vector3.Normalize(toCenter), toCenter.Length(), QueryFilter.Shots) < toCenter.Length() - 0.35f)
                continue;
            list.Add((p, at, dist));
        }
        list.Sort((a, b) => a.Item3 != b.Item3 ? a.Item3.CompareTo(b.Item3) : a.Item1.Id.CompareTo(b.Item1.Id));
        return list;
    }

    // ---- Practice dummies and stun ------------------------------------------------------------

    /// <summary>Workbench Range: adds <paramref name="count"/> practice dummies standing down the range (they never move or shoot, and respawn).</summary>
    public void AddPracticeDummies(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var before = Players.Keys.ToHashSet();
            if (!AddBot(BotDifficulty.Easy, BotPersonality.Tactician, quiet: true))
                return;
            var bot = Players.Values.First(p => !before.Contains(p.Id));
            bot.Bot!.Dummy = true;
            bot.Name = $"[DUMMY] {i + 1}";
            // In a row 12-30 m down the range, spread across it.
            bot.Bot.DummyHome = new Vector3(-6f + 4f * (i % 4), 0, 52f - 9f * (i / 4) - 3f * (i % 2));
            bot.Bot.DummyPlaced = false;
        }
        BroadcastRoster();
    }

    /// <summary>A bot tick that does nothing: practice dummies stand at home, stunned bots stand still. True = skip the AI.</summary>
    private bool BotHeld(Player p, BotBrain b, double now)
    {
        if (!b.Dummy && now >= p.StunnedUntil)
            return false;
        if (!p.Alive)
        {
            b.DummyPlaced = false;
            b.Target = byte.MaxValue;
            return true;
        }
        if (b.Dummy && !b.DummyPlaced)
        {
            var home = b.DummyHome;
            if (CartPhysics.Ground(World.Query, home, 0) is { } g)
                home.Y = g;
            p.Teleport(home);
            p.Yaw = 0; // facing back up the range, at you
            for (var i = 0; i < Player.HistorySize; i++)
                p.History[i] = new HistoryEntry { Position = home, Yaw = p.Yaw };
            b.DummyPlaced = true;
        }
        World.Simulate(ref p.Sim, new InputCmd { Yaw = p.Yaw, Pitch = 0, Weapon = p.Weapon, SpeedPct = 100 });
        return true;
    }

    // ---- Push carts (server) ----------------------------------------------------------------

    /// <summary>Server, every tick: create, push, park and man every push cart.</summary>
    private void ServerTickCarts(double now)
    {
        foreach (var id in Players.Keys.Order().ToList())
        {
            var p = Players[id];
            var def = CartWeaponOf(p);
            if (def == null || !p.Alive)
            {
                Carts.Remove(id); // a dead owner's cart goes with them; it comes back in front of them on respawn
                continue;
            }
            var holding = p.Weapon == def.Id;
            if (!Carts.TryGetValue(id, out var c))
            {
                c = new CartEntity { Owner = id, Def = def, Shape = ShapeOf(def), Mode = holding ? CartMode.Pushed : CartMode.Idle };
                c.Body = CartPhysics.Place(c.Shape, p.Position, p.Yaw, World.Query);
                Carts[id] = c;
            }
            if (!ReferenceEquals(c.Def, def))
            {
                c.Def = def;
                c.Shape = ShapeOf(def);
            }
            // Selecting the cart weapon next to a parked cart picks it up; switching away leaves it standing (and steps
            // off a manned one). Both wait a moment after a mode change so the owner's own weapon switch can catch up.
            var settled = now - c.ModeAt > 1.0;
            if (holding && c.Mode == CartMode.Idle && settled && NearBar(p, c))
                SetCartMode(p, c, CartMode.Pushed, now);
            else if (!holding && c.Mode != CartMode.Idle && settled)
                SetCartMode(p, c, CartMode.Idle, now);
            switch (c.Mode)
            {
                case CartMode.Pushed when holding:
                {
                    var feet = CartPhysics.Step(ref c.Body, c.Shape, p.Position, p.Yaw, World.TickDt, World.Query);
                    if (Vector3.DistanceSquared(feet, p.Position) > 1e-8f)
                    {
                        p.Sim.Position = feet; // held at the push bar: the cart is stuck
                        p.Sim.Velocity.X = p.Sim.Velocity.Z = 0;
                    }
                    break;
                }
                case CartMode.Manned:
                {
                    // The owner stands at the push bar (the cart doesn't move); its gun follows their aim within the limits.
                    var stand = c.Stand;
                    p.Sim.Position = new Vector3(stand.X, p.Sim.Position.Y, stand.Z);
                    p.Sim.Velocity.X = p.Sim.Velocity.Z = 0;
                    var (yaw, pitch) = ClampCartAim(c.Body.Yaw, p.Yaw, p.Pitch);
                    c.AimYaw = World.WrapAngle(yaw - c.Body.Yaw);
                    c.AimPitch = pitch;
                    break;
                }
            }
        }
        if (ServerTick % World.SnapshotEvery == 0 && (Carts.Count > 0 || _cartsLive))
        {
            WriteCartState(_w, Carts.Values.OrderBy(c => c.Owner));
            Net.Broadcast(_w.Span, PacketReliability.UnreliableSequenced);
            _cartsLive = Carts.Count > 0;
        }
    }

    /// <summary>
    /// A manned cart gun's aim: <paramref name="yaw"/> and <paramref name="pitch"/> clamped to the traverse limits around
    /// a cart facing <paramref name="cartYaw"/> (+-<see cref="CartTraverseYaw"/>, <see cref="CartPitchDown"/> to <see cref="CartPitchUp"/>).
    /// </summary>
    public static (float Yaw, float Pitch) ClampCartAim(float cartYaw, float yaw, float pitch)
    {
        var rel = Math.Clamp(World.WrapAngle(yaw - cartYaw), -CartTraverseYaw, CartTraverseYaw);
        return (World.WrapAngle(cartYaw + rel), Math.Clamp(pitch, CartPitchDown, CartPitchUp));
    }

    private static bool NearBar(Player p, CartEntity c)
    {
        var d = c.Bar - p.Position;
        return new Vector2(d.X, d.Z).Length() <= CartPickupRange && MathF.Abs(d.Y) < 1.5f;
    }

    private void SetCartMode(Player owner, CartEntity c, CartMode mode, double now)
    {
        var old = c.Mode;
        if (old == mode)
            return;
        c.Mode = mode;
        c.ModeAt = now;
        if (mode == CartMode.Manned)
        {
            var (yaw, pitch) = ClampCartAim(c.Body.Yaw, owner.Yaw, owner.Pitch);
            c.AimYaw = World.WrapAngle(yaw - c.Body.Yaw);
            c.AimPitch = pitch;
        }
        else
            c.AimYaw = c.AimPitch = 0;
        if (owner.Id == LocalId)
            OnOwnCartMode(c, old, now);
    }

    /// <summary>
    /// Server: the interact key (<paramref name="hold"/>: held at a parked or manned cart). Buys the slot upgrade at its
    /// station; parks the cart you're pushing; tap at a parked cart's bar to man its gun, hold to push it; tap while
    /// manning to step off, hold to push it from there.
    /// </summary>
    private void ServerInteract(Player p, double now, bool hold = false)
    {
        if (!p.Alive || Phase != MatchPhase.Playing)
            return;
        if (!hold && SlotStation is { } st && !p.ExtraSlots && Vector3.Distance(st, p.Position) <= StationRange)
        {
            ServerBuySlots(p);
            return;
        }
        if (!Carts.TryGetValue(p.Id, out var c) || c.Def == null)
            return;
        switch (c.Mode)
        {
            case CartMode.Pushed when p.Weapon == c.Def.Id:
                SetCartMode(p, c, CartMode.Idle, now); // parked where it stands
                break;
            case CartMode.Idle when NearBar(p, c):
                SetCartMode(p, c, hold ? CartMode.Pushed : CartMode.Manned, now);
                break;
            case CartMode.Manned:
                SetCartMode(p, c, hold ? CartMode.Pushed : CartMode.Idle, now);
                break;
        }
    }

    /// <summary>
    /// Server: may <paramref name="shooter"/> not fire <paramref name="def"/> right now? A cart only fires while its owner
    /// mans it (not while pushing it: both hands are on the bar), and then only within the traverse limits, so
    /// <paramref name="dir"/> is clamped to them. Anything that isn't a cart is never blocked.
    /// </summary>
    private bool CartBlocksShot(Player shooter, WeaponDef def, ref Vector3 dir)
    {
        if (!IsCart(def))
            return false;
        if (!Carts.TryGetValue(shooter.Id, out var c) || c.Mode != CartMode.Manned || dir.LengthSquared() < 0.5f)
            return true;
        var n = Vector3.Normalize(dir);
        var (yaw, pitch) = ClampCartAim(c.Body.Yaw, MathF.Atan2(n.X, -n.Z), MathF.Asin(Math.Clamp(n.Y, -1f, 1f)));
        dir = World.Forward(yaw, pitch);
        return false;
    }

    // ---- Push carts (both sides) --------------------------------------------------------------

    private int _slotBeforeCart = -1; // the weapon you held before you took the cart (leaving the cart goes back to it)
    private Buttons _cartPrevButtons;
    private double _cartLeaveSentAt = -100;

    /// <summary>The local player's cart, when they're manning it.</summary>
    public CartEntity? MannedCart => Local is { Alive: true } && Carts.TryGetValue(LocalId, out var c) && c.Mode == CartMode.Manned ? c : null;

    /// <summary>
    /// Where your view sits while you man your cart: low behind its gun (just over the push bar, a step back), so the gun
    /// in the basket is in view. Null when you aren't manning one. Your shots leave from here too.
    /// </summary>
    public Vector3? MannedEye => MannedCart is { } c
        ? c.Bar + new Vector3(0, c.Shape.BarHeight + 0.05f, 0) - World.FlatForward(c.Body.Yaw) * 0.55f
        : null;

    /// <summary>The interact key (E): park your cart, man it (tap at its bar) or push it (<paramref name="hold"/>), step off, buy the slot upgrade.</summary>
    public void Interact(double now, bool hold = false)
    {
        if (Local is not { Alive: true } me || Phase != MatchPhase.Playing)
            return;
        if (IsServer)
            ServerInteract(me, now, hold);
        else
        {
            _w.Begin(Msg.CartUse).U8(hold ? (byte)1 : (byte)0);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Would holding the interact key do something different from tapping it right now (at a parked or manned cart)?</summary>
    public bool InteractCanHold =>
        Local is { Alive: true } me && Carts.TryGetValue(LocalId, out var c) && (c.Mode == CartMode.Manned || c.Mode == CartMode.Idle && NearBar(me, c));

    /// <summary>What the interact key would do right now (for the HUD hint), or null.</summary>
    public string? InteractHint()
    {
        if (Local is not { Alive: true } me)
            return null;
        if (SlotStation is { } st && !me.ExtraSlots && Vector3.Distance(st, me.Position) <= StationRange)
            return "unlock weapon slots 3-4 (free in the sandbox)";
        if (!Carts.TryGetValue(LocalId, out var c))
            return null;
        if (c.Mode == CartMode.Pushed && IsCart(Active.Def))
            return "park the cart";
        if (c.Mode == CartMode.Idle && NearBar(me, c))
            return "man the cart gun (hold: push the cart)";
        return null;
    }

    /// <summary>
    /// Every frame (before the input goes out): a manned cart keeps your view inside its traverse limits, and jump or
    /// sprint steps you off it. Your feet stay at the bar, so the movement keys do nothing while you man it.
    /// </summary>
    private void UpdateCartControls(double now)
    {
        var pressed = HeldButtons & ~_cartPrevButtons;
        _cartPrevButtons = HeldButtons;
        if (MannedCart is not { } c)
            return;
        (LookYaw, LookPitch) = ClampCartAim(c.Body.Yaw, LookYaw, LookPitch);
        if ((pressed & (Buttons.Jump | Buttons.Sprint)) != 0 && now - _cartLeaveSentAt > 0.5)
        {
            _cartLeaveSentAt = now;
            Interact(now); // a tap while manning steps off
        }
    }

    /// <summary>The buttons that go out with the input: none of the movement ones while you man a cart (you stand at its bar).</summary>
    private Buttons CartInputButtons(Buttons held) =>
        MannedCart != null ? held & ~(Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right | Buttons.Jump | Buttons.Sprint) : held;

    /// <summary>The trigger is refused: your cart only fires while you man it (park it with E, then tap E at its bar).</summary>
    private bool CartHoldsFire(bool pressed)
    {
        if (!IsCart(Active.Def) || MannedCart != null)
            return false;
        if (pressed)
            LoadoutNotice = "Both hands are on the bar: park the cart (E), then tap E at its bar to man its gun.";
        return true;
    }

    /// <summary>The local player's own cart changed mode: switch to it when it's pushed or manned, back to your weapon when you step away.</summary>
    private void OnOwnCartMode(CartEntity c, CartMode old, double now)
    {
        if (c.Mode != CartMode.Idle)
        {
            if (!IsCart(Active.Def))
                _slotBeforeCart = ActiveSlot;
            for (var i = 0; i < WeaponSlots; i++)
                if (IsCart(Slots[i].Def) && i != ActiveSlot)
                {
                    SwitchTo(i, now);
                    break;
                }
            LoadoutNotice = c.Mode == CartMode.Pushed ? "Pushing the cart. E parks it." : "Manning the cart gun. Leave: E (or jump / sprint). Hold E to push it.";
            if (c.Mode == CartMode.Manned)
                (LookYaw, LookPitch) = ClampCartAim(c.Body.Yaw, LookYaw, LookPitch);
        }
        else if (IsCart(Active.Def))
        {
            var back = _slotBeforeCart >= 0 && _slotBeforeCart < WeaponSlots && !IsCart(Slots[_slotBeforeCart].Def) ? _slotBeforeCart : -1;
            for (var i = 0; back < 0 && i < WeaponSlots; i++)
                if (!IsCart(Slots[i].Def))
                    back = i;
            if (back >= 0)
                SwitchTo(back, now);
            LoadoutNotice = old == CartMode.Pushed ? "Cart parked. Tap E at its bar to man its gun, hold E to push it." : "You stepped off the cart gun.";
        }
    }

    /// <summary>The weapon switch is refused: it's your cart, and the cart is parked out of reach.</summary>
    private bool CartRefusesSwitch(int slot)
    {
        if (!IsCart(Slots[slot].Def) || !Carts.TryGetValue(LocalId, out var c) || c.Mode != CartMode.Idle || Local is not { } me || NearBar(me, c))
            return false;
        LoadoutNotice = "Your cart is parked over there: walk up to it to push it again.";
        return true;
    }

    /// <summary>Bytes per cart in <see cref="Msg.CartState"/>.</summary>
    public const int CartStateBytes = 30;

    /// <summary>Writes every cart's state (owner, mode, position, yaw, rolled distance, gun aim yaw offset and pitch).</summary>
    public static void WriteCartState(PacketWriter w, IEnumerable<CartEntity> carts)
    {
        var list = carts.Take(32).ToList();
        w.Begin(Msg.CartState).U8((byte)list.Count);
        foreach (var c in list)
            w.U8(c.Owner).U8((byte)c.Mode).Vec(c.Body.Position).F32(c.Body.Yaw).F32(c.Body.Rolled).F32(c.AimYaw).F32(c.AimPitch);
    }

    /// <summary>Reads a cart state message (after the message id): (owner, mode, body, aim yaw offset, aim pitch) per cart.</summary>
    public static List<(byte Owner, CartMode Mode, CartBody Body, float AimYaw, float AimPitch)> ReadCartState(ref PacketReader r)
    {
        var list = new List<(byte, CartMode, CartBody, float, float)>();
        int n = r.Has(1) ? r.U8() : 0;
        for (var i = 0; i < n && r.Has(CartStateBytes); i++)
        {
            var owner = r.U8();
            var mode = (CartMode)Math.Min(r.U8(), (byte)CartMode.Manned);
            var body = new CartBody { Position = r.Vec(), Yaw = r.F32(), Rolled = r.F32() };
            var aimYaw = r.F32();
            var pitch = r.F32();
            if (float.IsFinite(body.Position.X + body.Position.Y + body.Position.Z + body.Yaw + body.Rolled + aimYaw + pitch))
                list.Add((owner, mode, body, Math.Clamp(aimYaw, -CartTraverseYaw, CartTraverseYaw), Math.Clamp(pitch, CartPitchDown, CartPitchUp)));
        }
        return list;
    }

    private void OnCartPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.CartUse when IsServer:
                if (_clients.TryGetValue(sender.G, out var cl) && Players.TryGetValue(cl.Id, out var p))
                    ServerInteract(p, now, hold: r.Has(1) && r.U8() == 1);
                break;
            case (byte)Msg.CartState when !IsServer:
            {
                var seen = new HashSet<byte>();
                foreach (var (owner, mode, body, aimYaw, pitch) in ReadCartState(ref r))
                {
                    seen.Add(owner);
                    if (!Carts.TryGetValue(owner, out var c))
                        Carts[owner] = c = new CartEntity { Owner = owner, Mode = mode };
                    var old = c.Mode;
                    c.Def ??= owner == LocalId ? Slots.Select(s => s.Def).FirstOrDefault(IsCart) ?? CartStandIn() : CartStandIn();
                    c.Shape = c.Def != null ? ShapeOf(c.Def) : default;
                    c.Body = body;
                    c.AimYaw = aimYaw;
                    c.AimPitch = pitch;
                    c.HeardAt = now;
                    c.Mode = mode;
                    if (old != mode)
                        c.ModeAt = now;
                    if (owner == LocalId && old != mode)
                        OnOwnCartMode(c, old, now);
                }
                foreach (var gone in Carts.Keys.Where(k => !seen.Contains(k)).ToList())
                    Carts.Remove(gone);
                break;
            }
            case (byte)Msg.RunState when !IsServer && r.Has(1):
                if (Local is { } me)
                {
                    me.ExtraSlots = (r.U8() & 1) != 0;
                    OnRunState();
                }
                break;
        }
    }

    /// <summary>What other players' carts are drawn as on a client (their builds stay on their host): a bare Push Cart.</summary>
    public static WeaponDef? CartStandIn()
    {
        if (_cartStandIn != null)
            return _cartStandIn;
        if (Parts.All.FirstOrDefault(p => p.Frame?.Hands == "cart") is not { } grip)
            return null;
        var a = WeaponBuilder.Assemble(new WeaponBuild("Push Cart", grip));
        return _cartStandIn = a.PreviewDef;
    }

    // ---- Self-test hooks ------------------------------------------------------------------------

    public void SelfTestInteract(Player p, double now, bool hold = false) => ServerInteract(p, now, hold);

    public void SelfTestTickCarts(double now)
    {
        ServerTick++;
        ServerTickCarts(now);
    }
}
