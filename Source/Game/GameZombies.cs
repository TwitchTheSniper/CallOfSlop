using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a push cart is doing.</summary>
public enum CartMode : byte
{
    /// <summary>In its owner's hands: rolls in front of them, fires when they pull the trigger.</summary>
    Pushed,

    /// <summary>Standing where it was left (switched away from, or a turret that ran out). Walk up and pick it up.</summary>
    Idle,

    /// <summary>Parked as a turret: fires its own build at the nearest enemy for a while (proposed, open for the user).</summary>
    Turret,
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

    /// <summary>Turret: when it stops, rounds left, next shot, shots fired (the shot seed), target and aim pitch.</summary>
    public double TurretUntil;

    public int TurretAmmo;
    public double NextShot;
    public uint Shots;
    public byte Target = byte.MaxValue;
    public float AimPitch;

    /// <summary>Picked up this recently: stays pushed while the owner's weapon switch catches up.</summary>
    public double PickedUpAt = -100;

    /// <summary>Client: when the last state arrived (carts not heard of for a while are dropped).</summary>
    public double HeardAt;

    /// <summary>Where the push bar is (the owner stands just behind it to push).</summary>
    public Vector3 Bar => Body.Position - World.FlatForward(Body.Yaw) * (Shape.PushDist - CartShape.PlayerGap);

    /// <summary>Where a parked cart's shots start: above its centre at about barrel height.</summary>
    public Vector3 TurretMuzzle => Body.Position + new Vector3(0, MathF.Max(0.3f, Shape.BarHeight * 0.65f), 0) + World.FlatForward(Body.Yaw) * Shape.Half.Z;
}

/// <summary>
/// The Zombies preview systems that live in a match (docs/zombies/WEAPON-BUILDER.md), all used by the Workbench Range
/// today and by the Zombies mode later: the push cart as a real object (pushed, parked as a turret, picked up again),
/// weapon slots 3 and 4 (the per-run slot upgrade), the built melee weapon on the melee key (damage, reach, cleave,
/// stun), and the range's practice dummies. Everything is server-authoritative; standard matches never create any of it.
/// </summary>
public sealed partial class Game
{
    /// <summary>How close to a cart's push bar you must be to pick it up (or park it).</summary>
    public const float CartPickupRange = 2.4f;

    /// <summary>Parked turret: how long it fires on its own, how many magazines of the build it gets, how far it sees, how fast it turns.</summary>
    public const double TurretSeconds = 30;

    public const int TurretMags = 2;
    public const float TurretRange = 45f;
    public const float TurretTurnRate = 2.5f;

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
        if (hits.Count == 0)
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
            if (toCenter.Length() > 0.3f && World.Query.Raycast(origin, Vector3.Normalize(toCenter), toCenter.Length(), QueryFilter.All) < toCenter.Length() - 0.35f)
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

    /// <summary>Server, every tick: create, push, park and fire every push cart.</summary>
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
            // Holding the cart weapon next to a parked cart picks it up; switching away leaves it standing.
            if (holding && c.Mode != CartMode.Pushed && NearBar(p, c))
                SetCartMode(p, c, CartMode.Pushed, now);
            else if (!holding && c.Mode == CartMode.Pushed && now - c.PickedUpAt > 1.0)
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
                case CartMode.Turret:
                    TickTurret(p, c, now);
                    break;
            }
        }
        if (ServerTick % World.SnapshotEvery == 0 && (Carts.Count > 0 || _cartsLive))
        {
            WriteCartState(_w, Carts.Values.OrderBy(c => c.Owner), now);
            Net.Broadcast(_w.Span, PacketReliability.UnreliableSequenced);
            _cartsLive = Carts.Count > 0;
        }
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
        if (mode == CartMode.Pushed)
            c.PickedUpAt = now;
        if (mode == CartMode.Turret)
        {
            c.TurretUntil = now + TurretSeconds;
            c.TurretAmmo = Math.Max(1, (c.Def?.Mag ?? 1) * TurretMags);
            c.NextShot = now + 0.5;
            c.Target = byte.MaxValue;
        }
        if (owner.Id == LocalId)
            OnOwnCartMode(c, old, now);
    }

    /// <summary>A parked cart: turns toward the nearest enemy it can see and fires its build at it until its time or rounds run out.</summary>
    private void TickTurret(Player owner, CartEntity c, double now)
    {
        if (now >= c.TurretUntil || c.TurretAmmo <= 0 || c.Def is not { } def)
        {
            SetCartMode(owner, c, CartMode.Idle, now);
            return;
        }
        var muzzle = c.TurretMuzzle;
        Player? best = null;
        var bestD = TurretRange;
        foreach (var t in Players.Values.OrderBy(x => x.Id))
        {
            if (t == owner || !t.Alive || !AreEnemies(owner, t))
                continue;
            var d = Vector3.Distance(t.Chest, muzzle);
            if (d < bestD && HasLineOfSight(muzzle, t.Chest))
            {
                best = t;
                bestD = d;
            }
        }
        c.Target = best?.Id ?? byte.MaxValue;
        if (best == null)
            return;
        var to = best.Chest - muzzle;
        var wantYaw = MathF.Atan2(to.X, -to.Z);
        var turn = World.WrapAngle(wantYaw - c.Body.Yaw);
        c.Body.Yaw = World.WrapAngle(c.Body.Yaw + Math.Clamp(turn, -TurretTurnRate * World.TickDt, TurretTurnRate * World.TickDt));
        c.AimPitch = Math.Clamp(MathF.Asin(Math.Clamp(to.Y / MathF.Max(0.01f, to.Length()), -1f, 1f)), -0.45f, 0.45f);
        if (MathF.Abs(World.WrapAngle(wantYaw - c.Body.Yaw)) > 0.07f || now < c.NextShot)
            return;
        c.NextShot = now + MathF.Max(0.05f, def.FireInterval);
        var dir = World.Forward(c.Body.Yaw, c.AimPitch);
        ServerResolveShot(owner, def, muzzle, dir, def.HipSpreadDeg * MathF.PI / 180f * 0.5f, c.Shots++, ServerTick, now, turret: true);
        c.TurretAmmo--;
    }

    /// <summary>Server: the interact key: buy the slot upgrade at its station, park the cart you're pushing, or pick up your cart.</summary>
    private void ServerInteract(Player p, double now)
    {
        if (!p.Alive || Phase != MatchPhase.Playing)
            return;
        if (SlotStation is { } st && !p.ExtraSlots && Vector3.Distance(st, p.Position) <= StationRange)
        {
            ServerBuySlots(p);
            return;
        }
        if (!Carts.TryGetValue(p.Id, out var c) || c.Def == null)
            return;
        if (c.Mode == CartMode.Pushed && p.Weapon == c.Def.Id)
            SetCartMode(p, c, CartMode.Turret, now);
        else if (c.Mode != CartMode.Pushed && NearBar(p, c))
            SetCartMode(p, c, CartMode.Pushed, now);
    }

    /// <summary>Server: may <paramref name="shooter"/> not fire <paramref name="def"/> by hand right now (a cart that isn't being pushed)?</summary>
    private bool CartBlocksShot(Player shooter, WeaponDef def) =>
        IsCart(def) && (!Carts.TryGetValue(shooter.Id, out var c) || c.Mode != CartMode.Pushed);

    // ---- Push carts (both sides) --------------------------------------------------------------

    /// <summary>The interact key (E): park / pick up your cart, buy the slot upgrade.</summary>
    public void Interact(double now)
    {
        if (Local is not { Alive: true } me || Phase != MatchPhase.Playing)
            return;
        if (IsServer)
            ServerInteract(me, now);
        else
        {
            _w.Begin(Msg.CartUse);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

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
            return "park the cart as a turret";
        if (c.Mode != CartMode.Pushed && NearBar(me, c))
            return c.Mode == CartMode.Turret ? "pick the cart up (stops the turret)" : "push the cart";
        return null;
    }

    /// <summary>The local player's own cart changed mode: switch to it when it's picked up, away from it when it's parked.</summary>
    private void OnOwnCartMode(CartEntity c, CartMode old, double now)
    {
        if (c.Mode == CartMode.Pushed)
        {
            for (var i = 0; i < WeaponSlots; i++)
                if (IsCart(Slots[i].Def) && i != ActiveSlot)
                {
                    SwitchTo(i, now);
                    break;
                }
            LoadoutNotice = "Pushing the cart.";
        }
        else if (old == CartMode.Pushed && IsCart(Active.Def))
        {
            for (var i = 0; i < WeaponSlots; i++)
                if (!IsCart(Slots[i].Def))
                {
                    SwitchTo(i, now);
                    break;
                }
            LoadoutNotice = c.Mode == CartMode.Turret ? $"Cart parked: it fires on its own for {TurretSeconds:0} s." : "";
        }
        if (c.Mode == CartMode.Idle && old == CartMode.Turret)
            LoadoutNotice = "Your cart turret stopped. Walk up to it to push it again.";
    }

    /// <summary>The weapon switch is refused: it's your cart, and the cart is parked out of reach.</summary>
    private bool CartRefusesSwitch(int slot)
    {
        if (!IsCart(Slots[slot].Def) || !Carts.TryGetValue(LocalId, out var c) || c.Mode == CartMode.Pushed || Local is not { } me || NearBar(me, c))
            return false;
        LoadoutNotice = "Your cart is parked over there: walk up to it to push it again.";
        return true;
    }

    /// <summary>Writes every cart's state (owner, mode, position, yaw, rolled distance, aim pitch, turret seconds left).</summary>
    public static void WriteCartState(PacketWriter w, IEnumerable<CartEntity> carts, double now)
    {
        var list = carts.Take(32).ToList();
        w.Begin(Msg.CartState).U8((byte)list.Count);
        foreach (var c in list)
            w.U8(c.Owner).U8((byte)c.Mode).Vec(c.Body.Position).F32(c.Body.Yaw).F32(c.Body.Rolled).F32(c.AimPitch)
                .U8((byte)Math.Clamp(c.Mode == CartMode.Turret ? Math.Ceiling(c.TurretUntil - now) : 0, 0, 255));
    }

    /// <summary>Reads a cart state message (after the message id): (owner, mode, body, aim pitch, turret seconds left) per cart.</summary>
    public static List<(byte Owner, CartMode Mode, CartBody Body, float AimPitch, byte SecondsLeft)> ReadCartState(ref PacketReader r)
    {
        var list = new List<(byte, CartMode, CartBody, float, byte)>();
        int n = r.Has(1) ? r.U8() : 0;
        for (var i = 0; i < n && r.Has(23); i++)
        {
            var owner = r.U8();
            var mode = (CartMode)Math.Min(r.U8(), (byte)CartMode.Turret);
            var body = new CartBody { Position = r.Vec(), Yaw = r.F32(), Rolled = r.F32() };
            var pitch = r.F32();
            var left = r.U8();
            if (float.IsFinite(body.Position.X + body.Position.Y + body.Position.Z + body.Yaw + body.Rolled + pitch))
                list.Add((owner, mode, body, pitch, left));
        }
        return list;
    }

    private void OnCartPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.CartUse when IsServer:
                if (_clients.TryGetValue(sender.G, out var cl) && Players.TryGetValue(cl.Id, out var p))
                    ServerInteract(p, now);
                break;
            case (byte)Msg.CartState when !IsServer:
            {
                var seen = new HashSet<byte>();
                foreach (var (owner, mode, body, pitch, left) in ReadCartState(ref r))
                {
                    seen.Add(owner);
                    if (!Carts.TryGetValue(owner, out var c))
                        Carts[owner] = c = new CartEntity { Owner = owner, Mode = mode };
                    var old = c.Mode;
                    c.Def ??= owner == LocalId ? Slots.Select(s => s.Def).FirstOrDefault(IsCart) ?? CartStandIn() : CartStandIn();
                    c.Shape = c.Def != null ? ShapeOf(c.Def) : default;
                    c.Body = body;
                    c.AimPitch = pitch;
                    c.TurretUntil = now + left;
                    c.HeardAt = now;
                    c.Mode = mode;
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

    public void SelfTestInteract(Player p, double now) => ServerInteract(p, now);

    public void SelfTestTickCarts(double now)
    {
        ServerTick++;
        ServerTickCarts(now);
    }
}
