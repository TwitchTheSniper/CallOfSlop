using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>Visual-only effect: a bullet tracer that fades out.</summary>
public struct Tracer
{
    public Vector3 From;
    public Vector3 To;
    public Vector3 Color;
    public float Width;
    public double Born;
}

/// <summary>Ammo / reload state of one weapon slot in the local player's loadout.</summary>
public sealed class WeaponState(WeaponDef def)
{
    public WeaponDef Def = def;
    public int Ammo = def.Mag;
    public double ReloadUntil;
    public double ReloadStarted;
}

public readonly record struct FeedEntry(double Time, string Text, bool Mine);

/// <summary>
/// Multiplayer game session with prediction-based netcode and weapon loadouts.
///
/// Both sides run the same fixed 60 Hz simulation (<see cref="World.Simulate"/>).
///   - Server (host): authoritative. Consumes each client's input commands from a small jitter
///     buffer, keeps 2 s of position history per player, and resolves shots with lag compensation
///     (rewinding targets to the tick the shooter was looking at). It validates weapon ownership,
///     fire rate, spread and movement speed against the weapon stats.
///   - Client: predicts its own movement instantly from local input, then reconciles: when a
///     snapshot acknowledges input N, it resets to the server state and replays inputs N+1..now.
///     Remote players are drawn slightly in the past, interpolated between buffered snapshots.
/// </summary>
public sealed partial class Game : IDisposable
{
    public const int MaxPlayers = 8;
    private const int InputRedundancy = 4;
    private const int JitterBufferTarget = 2;
    private const int MaxStarvedTicks = 4;

    public readonly NetPeer Net;
    public readonly Audio Audio;
    public bool IsServer => Net.IsServer;

    public readonly Dictionary<byte, Player> Players = new();
    public readonly List<Tracer> Tracers = new();
    public readonly List<FeedEntry> Feed = new();

    // ---- Local player -----------------------------------------------------
    public byte LocalId = byte.MaxValue;
    public Player? Local => Players.GetValueOrDefault(LocalId);
    public bool HasLocalPlayer => Players.ContainsKey(LocalId);

    /// <summary>Set by the input code every frame. Look is applied immediately; buttons are sampled each tick.</summary>
    public float LookYaw;
    public float LookPitch;
    public Buttons HeldButtons;
    public bool AimHeld;

    // ---- Loadout / weapons ---------------------------------------------------
    public byte ChosenPrimary = Weapons.DefaultPrimary.Id; // what the player picked in the menu
    public byte ChosenSecondary = Weapons.DefaultSecondary.Id;
    public uint ChosenPrimaryKit, ChosenSecondaryKit; // attachments picked in the gunsmith
    public PerkSet ChosenPerks = PerkSet.Empty;
    public readonly WeaponState[] Slots = [new(Weapons.DefaultPrimary), new(Weapons.DefaultSecondary)];
    public int ActiveSlot;
    public WeaponState Active => Slots[ActiveSlot];
    public double EquipUntil;
    public double NextFireTime;
    public double LastShotTime = -10;
    public bool Aiming;
    public float AimT; // 0..1 aim-down-sights blend
    public float Bloom; // extra spread from sustained fire (radians)

    /// <summary>The local player's recoil: spray pattern, recovery, and the visual shake/punch/kick (see Recoil.cs).</summary>
    public readonly RecoilState LocalRecoil = new();

    /// <summary>Viewmodel kick from recoil right now (decays to zero). For Program.DrawViewModel.</summary>
    public WeaponKick WeaponKick => LocalRecoil.Kick;

    /// <summary>Render-only camera offset from recoil (shake and punch). Never changes the actual aim.</summary>
    public CameraKick RecoilCamera => LocalRecoil.Camera;
    private int _burstLeft;
    public string LoadoutNotice = "";

    // ---- Feedback ----------------------------------------------------------
    public double MuzzleFlashUntil;
    public double HitMarkerUntil;
    public bool HitMarkerHead;
    public double DamageFlashUntil;
    public bool Disconnected;
    public string Status = "";

    // ---- Simulation clock ---------------------------------------------------
    public uint ServerTick;
    private double _accumulator;
    private float _alpha;

    // ---- Client prediction ------------------------------------------------
    private uint _nextSeq = 1;
    private readonly List<InputCmd> _pendingInputs = new();
    private Vector3 _correction;
    public float LastCorrection;
    private bool _localInitialized;

    // ---- Client interpolation clock -----------------------------------------
    public double RenderTick;
    private bool _clockStarted;
    private double _lastSnapTime;
    private uint _lastSnapTick;
    private double _jitterTicks = 1;
    public double InterpDelayTicks => Math.Clamp(World.SnapshotEvery * 2 + _jitterTicks * 3, 3, 18);

    private int _lastHealth = World.MaxHealth;
    private bool _wasAlive; // false: you start each session waiting to deploy
    private double _nextInfoLog;
    private uint _shotSeed;

    // ---- Server bookkeeping -------------------------------------------------
    private readonly Dictionary<ulong, (RakNetGUID Guid, byte Id)> _clients = new();
    private readonly PacketWriter _w = new();
    private readonly PacketWriter _body = new();
    private readonly Random _rng = new();

    public readonly string LocalName;

    /// <summary>The local player's progression (XP is awarded for the local player's kills).</summary>
    public Progress? Progress;

    public Game(NetPeer net, Audio audio, string playerName, byte primary, byte secondary, int bots = 0,
        BotDifficulty botSkill = BotDifficulty.Normal, byte mapId = 0,
        ThrowableKind grenade = ThrowableKind.None, ThrowableKind equipment = ThrowableKind.None, uint primaryKit = 0, uint secondaryKit = 0,
        PerkSet? perks = null)
    {
        ChosenPerks = (perks ?? PerkSet.Empty).Sanitized();
        ChosenPrimaryKit = primaryKit;
        ChosenSecondaryKit = secondaryKit;
        ChosenGrenade = grenade;
        ChosenEquipment = equipment;
        if (net.IsServer)
            World.SetMap(Maps.Get(mapId)); // clients get the map from the server's Welcome
        Net = net;
        Audio = audio;
        LocalName = Settings.Clean(playerName);
        ChosenPrimary = primary;
        ChosenSecondary = secondary;
        _shotSeed = (uint)Environment.TickCount;

        if (IsServer)
        {
            LocalId = 0;
            var host = new Player
            {
                Id = 0, Name = LocalName, WantPrimary = primary, WantSecondary = secondary, WantGrenade = grenade, WantEquipment = equipment,
                WantPrimaryKit = primaryKit, WantSecondaryKit = secondaryKit, WantPerks = ChosenPerks,
            };
            Players[0] = host;
            PlaceWaiting(host, 0); // you pick a loadout and click Respawn to drop in
            OnLocalSpawn(host);
            for (var i = 0; i < bots; i++)
                AddBot(botSkill);
            Status = net.IsOffline ? "Single player" : "Hosting";
        }
        else
        {
            Status = "Connecting...";
        }
    }

    // =====================================================================
    // Per-frame update
    // =====================================================================

    public void Update(double now, double frameDt)
    {
        var tp0 = TickProfile.Now();
        Net.Poll((sender, data) => OnPacket(sender, data, now));

        UpdateWeaponState(now, (float)frameDt);
        UpdateThrowableEffects(now, (float)frameDt);
        UpdateStreakEffects(now, (float)frameDt);
        TickServerAdvert(now);
        TickProfile.Add(TickProfile.Pre, tp0);

        _accumulator += frameDt;
        var steps = 0;
        while (_accumulator >= World.TickDt && steps++ < 8)
        {
            _accumulator -= World.TickDt;
            if (IsServer)
                ServerTickStep(now);
            else
                ClientTickStep();
        }
        if (_accumulator > World.TickDt)
            _accumulator = 0;
        _alpha = (float)(_accumulator / World.TickDt);
        tp0 = TickProfile.Now();

        if (!IsServer)
        {
            AdvanceInterpolationClock(now, frameDt);
            ClientTickMatch(frameDt);
        }

        UpdateRenderStates((float)frameDt);
        UpdateFeedback(now);
        UpdatePerkFeedback(now);
        TickProfile.Add(TickProfile.Post, tp0);
    }

    private InputCmd SampleLocalInput(uint seq)
    {
        var def = Active.Def;
        var speed = def.Mobility * (Aiming ? World.AdsSpeedMult : 1f);
        return new InputCmd
        {
            Seq = seq,
            Buttons = HeldButtons,
            Yaw = LookYaw,
            Pitch = LookPitch,
            Weapon = def.Id,
            SpeedPct = (byte)Math.Clamp((int)MathF.Round(speed * 100), 1, 200),
        };
    }

    private void UpdateRenderStates(float dt)
    {
        _correction *= MathF.Exp(-12f * dt);

        foreach (var p in Players.Values)
        {
            if (p.Id == LocalId)
            {
                p.RenderPosition = Vector3.Lerp(p.PrevPosition, p.Position, _alpha) + (IsServer ? Vector3.Zero : _correction);
                p.RenderYaw = p.Yaw = LookYaw;
                p.RenderPitch = p.Pitch = LookPitch;
                p.Weapon = Active.Def.Id;
            }
            else if (IsServer)
            {
                p.RenderPosition = Vector3.Lerp(p.PrevPosition, p.Position, _alpha);
                p.RenderYaw = p.Yaw;
                p.RenderPitch = p.Pitch;
            }
            else
            {
                SampleSnapshots(p, RenderTick);
            }

            if (p.Id == LocalId || IsServer)
                p.RenderStance = p.Sim.Stance;
            // Ease the eye height so stance changes glide instead of snapping the camera.
            p.RenderEyeHeight += (World.EyeOf(p.RenderStance) - p.RenderEyeHeight) * (1f - MathF.Exp(-14f * dt));
        }
    }

    private void UpdateFeedback(double now)
    {
        if (Local is { } local)
        {
            if (local.Alive && local.Health < _lastHealth)
            {
                DamageFlashUntil = now + 0.25;
                Audio.Effect(Sfx.Hurt, 0.6f);
            }
            if (!local.Alive && _wasAlive)
            {
                DamageFlashUntil = now + 0.4;
                Audio.Effect(Sfx.Death, 0.6f);
                LocalDeathTime = now;
                RespawnRequested = false;
            }
            if (local.Alive && !_wasAlive)
            {
                Audio.Effect(Sfx.Respawn, 0.5f);
                RespawnRequested = false;
            }
            _lastHealth = local.Alive ? local.Health : 0;
            _wasAlive = local.Alive;
        }

        Tracers.RemoveAll(t => now - t.Born > 0.25);
        Feed.RemoveAll(f => now - f.Time > 8);
        while (Feed.Count > 6)
            Feed.RemoveAt(0);
    }

    // =====================================================================
    // Local weapons
    // =====================================================================

    private void UpdateWeaponState(double now, float dt)
    {
        var w = Active;
        if (w.ReloadUntil > 0 && now >= w.ReloadUntil)
        {
            w.Ammo = w.Def.Mag;
            w.ReloadUntil = 0;
        }

        var alive = Local is { Alive: true };
        Aiming = AimHeld && alive && !IsReloading(now) && now >= EquipUntil;
        AimT = Math.Clamp(AimT + (Aiming ? dt : -dt) / w.Def.AdsTime, 0f, 1f); // optics and grips change aim speed
        Bloom = MathF.Max(0, Bloom - dt * 0.12f);
        if (alive)
        {
            LocalRecoil.Update(dt, now, ref LookPitch, ref LookYaw);
            LookPitch = Math.Clamp(LookPitch, -1.55f, 1.55f);
        }
    }

    /// <summary>Current cone half-angle (radians) for the next shot.</summary>
    public float CurrentSpread()
    {
        var def = Active.Def;
        var spread = float.Lerp(def.HipSpreadRad, def.HipSpreadRad * def.AdsSpreadMult, AimT);
        if (Local is { } me)
        {
            var v = me.Sim.Velocity;
            var moving = MathF.Min(new Vector2(v.X, v.Z).Length() / World.WalkSpeed, 1.5f);
            spread += moving * 0.014f * (Aiming ? 0.5f : 1f);
            if (!me.Sim.Grounded)
                spread += 0.035f;
            spread *= World.SpreadOf(me.Sim.Stance);
        }
        spread += Bloom;
        return Math.Clamp(spread, 0f, def.MaxSpreadRad);
    }

    /// <summary>Called every frame with the trigger state. Handles semi, auto and burst fire.</summary>
    public void UpdateTrigger(double now, bool held, bool pressed)
    {
        if (Local is not { Alive: true } || Phase != MatchPhase.Playing)
        {
            _burstLeft = 0;
            return;
        }
        if (StreakTrigger(now, held, pressed))
            return; // targeting a streak: fire calls it in
        var w = Active;
        var def = w.Def;
        if (now < EquipUntil || IsReloading(now))
            return;

        if (w.Ammo <= 0)
        {
            _burstLeft = 0;
            if (pressed)
            {
                Audio.Effect(Sfx.DryFire, 0.6f);
                StartReload(now);
            }
            return;
        }

        // Don't bank up shots while idle, but allow several per frame for very fast guns at low FPS.
        NextFireTime = Math.Max(NextFireTime, now - 0.02);

        switch (def.Mode)
        {
            case FireMode.Semi:
                if (pressed && now >= NextFireTime)
                    FireOnce(now);
                break;
            case FireMode.Auto:
                for (var i = 0; held && now >= NextFireTime && w.Ammo > 0 && i < 4; i++)
                    FireOnce(now);
                break;
            case FireMode.Burst:
                if (pressed && _burstLeft == 0 && now >= NextFireTime)
                    _burstLeft = def.BurstCount;
                for (var i = 0; _burstLeft > 0 && now >= NextFireTime && w.Ammo > 0 && i < 4; i++)
                {
                    FireOnce(now);
                    if (--_burstLeft == 0)
                        NextFireTime += def.BurstDelay;
                }
                if (w.Ammo == 0)
                    _burstLeft = 0;
                break;
        }

        if (w.Ammo == 0)
            StartReload(now);
    }

    private void FireOnce(double now)
    {
        var me = Local!;
        var w = Active;
        var def = w.Def;

        w.Ammo--;
        NextFireTime += def.FireInterval;
        LastShotTime = now;
        MuzzleFlashUntil = now + 0.05;
        Audio.Shot(def, null);

        var origin = me.RenderEye;
        var dir = World.Forward(LookYaw, LookPitch);
        var spread = CurrentSpread();
        var seed = _shotSeed++;

        // Recoil (the gun's pattern, see Recoil.cs) + bloom
        var (kickPitch, kickYaw) = LocalRecoil.Shot(def, me.Sim.Stance, AimT, now, LookPitch, LookYaw);
        LookPitch = Math.Clamp(LookPitch + kickPitch, -1.5f, 1.5f);
        LookYaw = World.WrapAngle(LookYaw + kickYaw);
        var kick = def.RecoilDeg * MathF.PI / 180f * (Aiming ? 0.7f : 1f) * World.RecoilOf(me.Sim.Stance);
        Bloom = MathF.Min(Bloom + kick * 0.25f, 0.06f);

        if (IsServer)
        {
            ServerResolveShot(me, def, origin, dir, spread, seed, Math.Max(0.0, (double)ServerTick - 1 + _alpha), now);
            return;
        }

        // Predict every pellet against what we see; the server rewinds to the same tick.
        var predictedHit = false;
        for (var i = 0; i < def.Pellets; i++)
        {
            var d = Weapons.SpreadDirection(dir, spread, seed, i);
            var dist = World.RaycastWorld(origin, d, World.Range);
            foreach (var other in Players.Values)
            {
                if (other.Id == LocalId || !other.Alive)
                    continue;
                if (World.Hitbox(other.RenderPosition, other.RenderStance, other.RenderYaw).Raycast(origin, d, dist) is { } t)
                {
                    dist = t;
                    predictedHit = true;
                }
            }
            AddTracer(MuzzleWorld(me), origin + d * dist, def, now);
        }
        if (predictedHit)
            HitMarkerUntil = now + 0.12;

        var tickBase = (uint)Math.Max(0, Math.Floor(RenderTick));
        _w.Begin(Msg.Fire).U8(def.Id).Vec(origin).Vec(dir).F32(spread).U32(seed).U32(tickBase).F32((float)(RenderTick - tickBase));
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    public void StartReload(double now)
    {
        var w = Active;
        if (IsReloading(now) || w.Ammo == w.Def.Mag || now < EquipUntil || Local is not { Alive: true })
            return;
        w.ReloadStarted = now;
        w.ReloadUntil = now + w.Def.Reload * StreakReloadMult(Local, now); // Overdrive halves it
        _burstLeft = 0;
        Audio.Reload(w.Def, null);

        if (IsServer)
        {
            Net.Broadcast(ReloadFx(LocalId, w.Def.Id), PacketReliability.Unreliable);
        }
        else
        {
            _w.Begin(Msg.Reload).U8(w.Def.Id);
            Net.SendToServer(_w.Span, PacketReliability.Unreliable);
        }
    }

    private ReadOnlySpan<byte> ReloadFx(byte playerId, byte weapon) => _w.Begin(Msg.ReloadFx).U8(playerId).U8(weapon).Span;

    public bool IsReloading(double now) => Active.ReloadUntil > now;

    /// <summary>0..1 progress of the current reload, or -1 if not reloading.</summary>
    public float ReloadProgress(double now)
    {
        var w = Active;
        if (w.ReloadUntil <= now)
            return -1;
        return (float)Math.Clamp((now - w.ReloadStarted) / Math.Max(w.ReloadUntil - w.ReloadStarted, 0.01), 0, 1);
    }

    public void SwitchTo(int slot, double now)
    {
        if (slot == ActiveSlot || slot < 0 || slot > 1 || Local is not { Alive: true })
            return;
        Active.ReloadUntil = 0; // switching cancels a reload
        ActiveSlot = slot;
        _burstLeft = 0;
        EquipUntil = now + Active.Def.EquipTime;
        NextFireTime = Math.Max(NextFireTime, EquipUntil);
        Audio.Equip(Active.Def);
    }

    /// <summary>Called by the loadout menu with the loadout slot to play.</summary>
    public void ChooseLoadout(Loadout l, double now)
    {
        ChosenStreaks = l.Streaks.Sanitized();
        ChooseLoadout(l.Primary, l.Secondary, l.Grenade, l.Equipment, l.PrimaryKit, l.SecondaryKit, now, l.Perks);
    }

    public void ChooseLoadout(byte primary, byte secondary, ThrowableKind grenade, ThrowableKind equipment, uint primaryKit, uint secondaryKit, double now,
        PerkSet? perks = null)
    {
        ChosenPerks = (perks ?? ChosenPerks).Sanitized();
        ChosenPrimary = primary;
        ChosenSecondary = secondary;
        ChosenGrenade = grenade;
        ChosenEquipment = equipment;
        ChosenPrimaryKit = primaryKit;
        ChosenSecondaryKit = secondaryKit;
        if (Net.IsOffline)
            SetReady(true); // single player: Deploy during the warmup starts the countdown
        if (IsServer)
        {
            if (Local is { } me)
            {
                ServerRequestLoadout(me, primary, secondary, grenade, equipment, primaryKit, secondaryKit, ChosenPerks, now);
                StreakRequest(me, ChosenStreaks);
            }
        }
        else
        {
            SendLoadout();
            LoadoutNotice = "Loadout sent...";
        }
    }

    private void SendLoadout()
    {
        _w.Begin(Msg.Loadout).U8(ChosenPrimary).U8(ChosenSecondary).U8((byte)ChosenGrenade).U8((byte)ChosenEquipment)
            .U24(ChosenPrimaryKit).U24(ChosenSecondaryKit)
            .U8((byte)ChosenPerks.Movement).U8((byte)ChosenPerks.Survival).U8((byte)ChosenPerks.Damage)
            .U8((byte)ChosenStreaks.A).U8((byte)ChosenStreaks.B).U8((byte)ChosenStreaks.C);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Loadouts can only be changed while dead (or in the pre-match countdown).</summary>
    public bool CanChangeLoadout => Local is not { Alive: true } || Phase is MatchPhase.Countdown or MatchPhase.Warmup;

    /// <summary>The Respawn button: spawn with the chosen loadout as soon as the respawn timer allows.</summary>
    public void RequestRespawn()
    {
        if (Local is { Alive: true })
            return;
        if (Net.IsOffline || SoloMatch)
            SetReady(true); // single player (or hosting alone): Respawn during the warmup starts the countdown
        if (IsServer)
        {
            if (Local is { } me)
                me.WantsRespawn = true;
        }
        else
        {
            _w.Begin(Msg.Respawn);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
        RespawnRequested = true;
    }

    /// <summary>Set once Respawn was clicked, until we're alive again (the button shows "Spawning...").</summary>
    public bool RespawnRequested;

    /// <summary>When the local player last died (client clock), for the respawn countdown on the death screen.</summary>
    public double LocalDeathTime = -100;

    /// <summary>Seconds until the Respawn button works.</summary>
    public double RespawnWait(double now) => Math.Max(0, LocalDeathTime + World.RespawnDelay - now);

    /// <summary>Makes the local weapon slots match the loadout the server says we have.</summary>
    private void SyncLocalSlots(Player p, bool refill)
    {
        var primary = p.DefFor(p.Primary);
        var secondary = p.DefFor(p.Secondary);
        if (refill || Slots[0].Def != primary || Slots[1].Def != secondary)
        {
            var changed = Slots[0].Def != primary || Slots[1].Def != secondary;
            Slots[0] = new WeaponState(primary);
            Slots[1] = new WeaponState(secondary);
            ActiveSlot = 0;
            _burstLeft = 0;
            Bloom = 0;
            LocalRecoil.Reset();
            if (changed)
            {
                RefillThrowables();
                var now = Brutal.GlfwApi.Glfw.Time;
                EquipUntil = now + Active.Def.EquipTime;
                Audio.Equip(Active.Def);
                LoadoutNotice = $"Equipped {Slots[0].Def.Name} + {Slots[1].Def.Name}";
            }
        }
    }

    public static Vector3 MuzzleWorld(Player p)
    {
        var fwd = World.Forward(p.RenderYaw, p.RenderPitch);
        var right = World.FlatRight(p.RenderYaw);
        return p.RenderEye + fwd * (0.4f + p.WeaponDef.Length) + right * 0.25f - Vector3.UnitY * 0.2f;
    }

    private void AddTracer(Vector3 from, Vector3 to, WeaponDef def, double now)
    {
        var color = def.Sound switch
        {
            SoundStyle.Zap => new Vector3(0.4f, 0.95f, 1f),
            SoundStyle.Toy => new Vector3(1f, 0.45f, 0.85f),
            SoundStyle.Suppressed => new Vector3(0.45f, 0.45f, 0.45f),
            _ => new Vector3(1f, 0.85f, 0.5f),
        };
        var width = def.Sound == SoundStyle.Zap ? 0.06f : def.Pellets > 1 ? 0.015f : 0.02f + def.Caliber * 0.02f;
        if (def.Sound == SoundStyle.Suppressed)
            width *= 0.6f; // suppressed shots leave faint, thin tracers
        Tracers.Add(new Tracer { From = from, To = to, Color = color, Width = width, Born = now });
    }

    private void OnLocalSpawn(Player p)
    {
        _lastHealth = p.Health;
        LookYaw = p.Yaw;
        LookPitch = p.Pitch;
        _correction = Vector3.Zero;
        SyncLocalSlots(p, refill: true);
        RefillThrowables();
        EquipUntil = 0;
    }

    private void Log(string line, bool mine = false)
    {
        Feed.Add(new FeedEntry(Brutal.GlfwApi.Glfw.Time, line, mine));
        Console.WriteLine(line);
    }

    // =====================================================================
    // Client: prediction + reconciliation
    // =====================================================================

    private void ClientTickStep()
    {
        ClientTickProjectiles(Brutal.GlfwApi.Glfw.Time);
        if (Local is not { } me)
            return;

        var cmd = FreezeFilter(SampleLocalInput(_nextSeq++));
        _pendingInputs.Add(cmd);
        if (_pendingInputs.Count > 240)
            _pendingInputs.RemoveAt(0);

        me.PrevPosition = me.Position;
        if (me.Alive)
            World.Simulate(ref me.Sim, cmd);

        var count = Math.Min(InputRedundancy, _pendingInputs.Count);
        _w.Begin(Msg.Input).U8((byte)count);
        for (var i = _pendingInputs.Count - count; i < _pendingInputs.Count; i++)
        {
            var c = _pendingInputs[i];
            _w.U32(c.Seq).U8((byte)c.Buttons).F32(c.Yaw).F32(c.Pitch).U8(c.Weapon).U8(c.SpeedPct);
        }
        Net.SendToServer(_w.Span, PacketReliability.UnreliableSequenced);
    }

    private void Reconcile(Player me, uint ackSeq, MoveState server, bool alive, bool respawned)
    {
        _pendingInputs.RemoveAll(c => c.Seq <= ackSeq);

        var predicted = me.Position;
        var state = server;
        if (alive)
            foreach (var cmd in _pendingInputs)
                World.Simulate(ref state, cmd);

        var error = predicted - state.Position;
        LastCorrection = error.Length();
        me.Sim = state;

        if (respawned || LastCorrection > 2f)
        {
            me.PrevPosition = state.Position;
            _correction = Vector3.Zero;
        }
        else
        {
            me.PrevPosition -= error;
            _correction += error;
        }
    }

    // =====================================================================
    // Client: interpolation of remote players
    // =====================================================================

    private void AdvanceInterpolationClock(double now, double frameDt)
    {
        if (!_clockStarted)
            return;
        var serverNow = _lastSnapTick + (now - _lastSnapTime) * World.TickRate;
        var target = serverNow - InterpDelayTicks;
        var drift = target - RenderTick;
        if (Math.Abs(drift) > 30)
            RenderTick = target;
        else
            RenderTick += frameDt * World.TickRate + drift * Math.Min(1.0, frameDt * 2.0);
    }

    private void OnSnapshotTiming(uint tick, double now)
    {
        if (!_clockStarted)
        {
            _clockStarted = true;
            _lastSnapTick = tick;
            _lastSnapTime = now;
            RenderTick = tick - InterpDelayTicks;
            return;
        }
        if (tick <= _lastSnapTick)
            return;
        var expected = (tick - _lastSnapTick) * World.TickDt;
        var actual = now - _lastSnapTime;
        var errTicks = Math.Abs(actual - expected) * World.TickRate;
        _jitterTicks += (errTicks - _jitterTicks) * 0.1;
        _lastSnapTick = tick;
        _lastSnapTime = now;
    }

    private static void SampleSnapshots(Player p, double renderTick)
    {
        var snaps = p.Snaps;
        if (snaps.Count == 0)
        {
            p.RenderPosition = p.Position;
            p.RenderYaw = p.Yaw;
            p.RenderPitch = p.Pitch;
            return;
        }

        while (snaps.Count > 2 && snaps[1].Tick < renderTick)
            snaps.RemoveAt(0);

        var first = snaps[0];
        var last = snaps[^1];
        if (renderTick <= first.Tick)
        {
            Set(p, first.Position, first.Yaw, first.Pitch);
            p.RenderStance = first.Stance;
        }
        else if (renderTick >= last.Tick)
        {
            var ahead = (float)Math.Min(renderTick - last.Tick, 8) * World.TickDt;
            var pos = World.ClampToArena(last.Position + new Vector3(last.Velocity.X, 0, last.Velocity.Z) * ahead);
            Set(p, pos, last.Yaw, last.Pitch);
            p.RenderStance = last.Stance;
        }
        else
        {
            for (var i = 0; i < snaps.Count - 1; i++)
            {
                Snap a = snaps[i], b = snaps[i + 1];
                if (renderTick < a.Tick || renderTick > b.Tick)
                    continue;
                var t = (float)((renderTick - a.Tick) / Math.Max(1, b.Tick - a.Tick));
                Set(p, Vector3.Lerp(a.Position, b.Position, t), World.LerpAngle(a.Yaw, b.Yaw, t), float.Lerp(a.Pitch, b.Pitch, t));
                p.RenderStance = t < 0.5f ? a.Stance : b.Stance;
                break;
            }
        }

        static void Set(Player p, Vector3 pos, float yaw, float pitch)
        {
            p.RenderPosition = pos;
            p.RenderYaw = yaw;
            p.RenderPitch = pitch;
        }
    }

    // =====================================================================
    // Server
    // =====================================================================

    private void ServerTickStep(double now)
    {
        ServerTick++;
        var tp = TickProfile.Now();
        ServerTickMatch(now);
        TickProfile.Add(TickProfile.Match, tp);
        tp = TickProfile.Now();
        ServerTickProjectiles(now);
        TickProfile.Add(TickProfile.Proj, tp);
        tp = TickProfile.Now();
        ServerTickStreaks(now);
        TickProfile.Add(TickProfile.Streaks, tp);

        foreach (var p in Players.Values)
        {
            p.PrevPosition = p.Position;

            if (p.Id == LocalId)
            {
                var cmd = FreezeFilter(SampleLocalInput(0));
                p.Yaw = cmd.Yaw;
                p.Pitch = cmd.Pitch;
                p.Weapon = cmd.Weapon;
                if (p.Alive)
                    World.Simulate(ref p.Sim, cmd);
            }
            else if (p.Bot != null)
            {
                BotTick(p, now);
            }
            else
            {
                ConsumeClientInput(p);
            }

            // Bots respawn on their own; people respawn when they click Respawn (never forced).
            if (!p.Alive && (p.Bot != null || p.WantsRespawn) && now >= p.RespawnAt)
            {
                tp = TickProfile.Now();
                Spawn(p, ServerTick, now);
                if (p.Id == LocalId)
                    OnLocalSpawn(p);
                TickProfile.Add(TickProfile.Spawn, tp);
            }

            p.History[ServerTick % Player.HistorySize] = new HistoryEntry { Position = p.Position, Stance = p.Sim.Stance, Yaw = p.Yaw };
        }

        tp = TickProfile.Now();
        if (ServerTick % World.SnapshotEvery == 0)
            SendSnapshots();
        TickProfile.Add(TickProfile.Snap, tp);

        if (now >= _nextInfoLog && _clients.Count > 0)
        {
            _nextInfoLog = now + 10;
            foreach (var (guid, id) in _clients.Values)
                if (Players.TryGetValue(id, out var p))
                    Console.WriteLine($"[net] player {id}: ping {Net.PingTo(guid)} ms, input buffer {p.InputQueue.Count}, weapon {p.WeaponDef.Name}");
        }
    }

    private void ApplyCmd(Player p, InputCmd cmd)
    {
        cmd = FreezeFilter(cmd);
        p.LastInput = cmd;
        p.LastProcessedSeq = cmd.Seq;
        p.Yaw = cmd.Yaw;
        p.Pitch = cmd.Pitch;
        if (p.Owns(cmd.Weapon))
            p.Weapon = cmd.Weapon;
        if (p.Alive)
            World.Simulate(ref p.Sim, cmd);
    }

    private void ConsumeClientInput(Player p)
    {
        var q = p.InputQueue;

        if (p.Buffering && q.Count >= JitterBufferTarget)
            p.Buffering = false;

        if (!p.Buffering && q.Count > 0)
        {
            var steps = q.Count > JitterBufferTarget + 4 ? 2 : 1;
            for (var i = 0; i < steps && q.Count > 0; i++)
            {
                var cmd = q[0];
                q.RemoveAt(0);
                if (cmd.Seq <= p.LastProcessedSeq)
                {
                    i--;
                    continue;
                }
                ApplyCmd(p, cmd);
            }
            p.StarvedTicks = 0;
            return;
        }

        // Starved: guess they're still holding the same keys, and count the guess as their next command.
        p.Buffering = true;
        if (p.StarvedTicks++ < MaxStarvedTicks && p.LastInput.Seq != 0)
        {
            var guess = p.LastInput;
            guess.Seq = p.LastProcessedSeq + 1;
            ApplyCmd(p, guess);
        }
    }

    /// <summary>Puts a player at a spawn point but not alive yet: they're on the loadout screen until they click Respawn.</summary>
    private void PlaceWaiting(Player p, double now)
    {
        Spawn(p, ServerTick, now);
        p.Alive = false;
        p.Health = 0;
        p.RespawnAt = now;
        p.WantsRespawn = false;
    }

    private void Spawn(Player p, uint tick, double now)
    {
        var spawns = SpawnPointsFor(p); // the team's own spawns in team modes
        var best = spawns[_rng.Next(spawns.Count)];
        var bestScore = -1f;
        foreach (var sp in spawns)
        {
            var nearest = float.MaxValue;
            foreach (var o in Players.Values)
                if (o != p && o.Alive && AreEnemies(p, o))
                    nearest = MathF.Min(nearest, Vector3.Distance(o.Position, sp));
            var score = nearest + (float)_rng.NextDouble() * 2f;
            if (score > bestScore)
            {
                bestScore = score;
                best = sp;
            }
        }

        p.Teleport(best);
        p.Yaw = MathF.Atan2(-best.X, best.Z);
        p.Pitch = 0;
        p.RenderYaw = p.Yaw;
        p.RenderPitch = 0;
        p.Health = World.MaxHealth;
        p.Alive = true;
        p.SpawnTick = tick;
        p.LastCombatTime = -100;
        if (p.Bot != null)
            PickBotLoadout(p);
        p.Primary = p.WantPrimary;
        p.Secondary = p.WantSecondary;
        p.PrimaryKit = p.WantPrimaryKit;
        p.SecondaryKit = p.WantSecondaryKit;
        ApplyPerks(p, p.WantPerks);
        StreakOnSpawn(p);
        p.WantsRespawn = false;
        p.Weapon = p.Primary;
        p.Grenade = p.WantGrenade;
        p.Equipment = p.WantEquipment;
        p.GrenadesLeft = Throwables.Get(p.Grenade).PerLife;
        p.EquipmentLeft = Throwables.Get(p.Equipment).PerLife;
        ApplyModeLoadout(p); // Gun Game: the server picks the gun
        if (p.Bot is { } brain)
        {
            brain.Ammo = p.DefFor(p.Primary).Mag;
            brain.ReloadUntil = 0;
            brain.Target = byte.MaxValue;
            brain.Waypoint = Vector3.Zero;
        }
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = best, Yaw = p.Yaw };
    }

    /// <summary>A player asked for a new loadout: apply now if out of combat, otherwise on next spawn.</summary>
    private void ServerRequestLoadout(Player p, byte primary, byte secondary, ThrowableKind grenade, ThrowableKind equipment, uint primaryKit, uint secondaryKit,
        PerkSet perks, double now)
    {
        p.WantPerks = perks.Sanitized();
        // Unlocks are tracked on each player's own PC, so the server just checks the slots make sense.
        p.WantGrenade = Throwables.Get(grenade) is { IsEquipment: false } g ? g.Kind : ThrowableKind.None;
        p.WantEquipment = Throwables.Get(equipment) is { IsEquipment: true } e ? e.Kind : ThrowableKind.None;
        var pw = Weapons.Get(primary);
        var sw = Weapons.Get(secondary);
        if (pw.Slot != Slot.Primary)
            pw = Weapons.DefaultPrimary;
        if (sw.Slot != Slot.Secondary)
            sw = Weapons.DefaultSecondary;
        p.WantPrimary = pw.Id;
        p.WantSecondary = sw.Id;
        p.WantPrimaryKit = Attachments.Sanitize(pw, primaryKit);
        p.WantSecondaryKit = Attachments.Sanitize(sw, secondaryKit);

        // Loadouts change on (re)spawn. The only exception is the pre-match countdown, where you're
        // already standing in the map, frozen, and can still swap.
        var instant = !p.Alive || Phase is MatchPhase.Countdown or MatchPhase.Warmup;
        if (instant && p.Alive && Mode != GameMode.GunGame)
        {
            p.Primary = pw.Id;
            p.Secondary = sw.Id;
            p.PrimaryKit = p.WantPrimaryKit;
            p.SecondaryKit = p.WantSecondaryKit;
            p.Weapon = pw.Id;
            p.Grenade = p.WantGrenade;
            p.Equipment = p.WantEquipment;
            p.GrenadesLeft = Throwables.Get(p.Grenade).PerLife;
            p.EquipmentLeft = Throwables.Get(p.Equipment).PerLife;
            ApplyPerks(p, p.WantPerks);
        }
        if (p.Id == LocalId)
        {
            if (instant && p.Alive)
            {
                SyncLocalSlots(p, refill: false);
                RefillThrowables();
            }
            LoadoutNotice = instant ? $"Equipped {pw.Name} + {sw.Name}" : "New loadout applies when you respawn";
        }
    }

    /// <summary>Where <paramref name="p"/> was, and how they stood, at a fractional past tick.</summary>
    private HistoryEntry HistoricState(Player p, double tick)
    {
        tick = Math.Clamp(tick, Math.Max(0.0, (double)ServerTick - World.MaxRewindTicks), ServerTick);
        var t0 = (uint)Math.Floor(tick);
        var t1 = Math.Min(t0 + 1, ServerTick);
        var frac = (float)(tick - t0);
        var a = p.History[t0 % Player.HistorySize];
        var b = p.History[t1 % Player.HistorySize];
        return new HistoryEntry
        {
            Position = Vector3.Lerp(a.Position, b.Position, frac),
            Stance = frac < 0.5f ? a.Stance : b.Stance,
            Yaw = World.LerpAngle(a.Yaw, b.Yaw, frac),
        };
    }

    /// <summary>
    /// Applies damage (server only), after the victim's survival perks. Returns true if it killed.
    /// <paramref name="killId"/> is a weapon id, 240+ for throwables, or <see cref="MeleeKillId"/>.
    /// </summary>
    private bool DamagePlayer(Player attacker, Player victim, int amount, bool head, byte killId, double now, DamageKind kind = DamageKind.Bullet,
        Vector3? from = null)
    {
        if (!victim.Alive || amount <= 0 || !DamageEnabled || now < victim.ImmuneUntil)
            return false;
        if (!CanDamage(attacker, victim))
            return false; // no friendly fire in team modes (your own grenade still hurts you)
        victim.LastCombatTime = now;
        attacker.LastCombatTime = now;
        if (kind == DamageKind.Explosive && victim.Perks.Has(PerkId.FlakJacket))
            amount = Math.Max(1, (int)MathF.Round(amount * Perks.FlakMult));
        if (kind == DamageKind.Bullet && victim.Perks.Has(PerkId.Entrenched) && IsDugIn(victim))
            amount = Math.Max(1, (int)MathF.Round(amount * Perks.EntrenchedMult));
        victim.Health -= amount;
        if (victim.Health > 0)
            return false;
        if (victim.Perks.Has(PerkId.LastStand) && !victim.LastStandUsed)
        {
            // Saved: 1 HP and a moment of immunity to get to cover.
            victim.Health = 1;
            victim.LastStandUsed = true;
            victim.ImmuneUntil = now + Perks.LastStandImmunity;
            return false;
        }
        victim.Health = 0;
        victim.Alive = false;
        victim.Deaths++;
        victim.RespawnAt = now + World.RespawnDelay;
        StreakOnKill(attacker, victim, killId, now);
        if (attacker != victim)
        {
            attacker.Kills++;
            if (attacker.Alive && attacker.Perks.Has(PerkId.Bloodthirst))
                attacker.Health = World.MaxHealth;
            if (attacker.Alive && attacker.Perks.Has(PerkId.Afterburner))
                attacker.Sim.BoostTicks = Perks.BoostTicks;
        }
        var kill = new KillPacket(attacker.Id, victim.Id, head, killId, KitForKill(attacker, killId),
            attacker.Alive ? Math.Clamp(attacker.Health, 0, 255) : 0, from ?? attacker.Eye);
        kill.Write(_w);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        LogKill(attacker.Id, victim.Id, head, killId);
        RecordKill(kill);
        OnModeKill(attacker, victim, killId, now);
        BotNoteKill(attacker, victim, head, killId, now);
        return true;
    }

    /// <summary>Hitscan with lag compensation (targets rewound to <paramref name="viewTick"/>), one ray per pellet.</summary>
    private void ServerResolveShot(Player shooter, WeaponDef def, Vector3 origin, Vector3 dir, float spread, uint seed, double viewTick, double now)
    {
        // Validation against the weapon's real stats.
        if (!shooter.Alive || !shooter.Owns(def.Id))
            return;
        // Fire-rate budget: each shot costs ~one fire interval. Up to 0.3 s can be banked so
        // packets that arrive bunched together aren't rejected, but the average rate can't exceed the gun's.
        shooter.ShotClock = Math.Max(shooter.ShotClock, now - 0.3);
        if (shooter.ShotClock > now + 0.05)
            return;
        shooter.ShotClock += def.FireInterval * 0.85;
        shooter.LastShotTime = now;
        shooter.LastCombatTime = now;
        if (Vector3.Distance(origin, shooter.Eye) > 7.5f) // ~0.5 s of a boosted sprint of client/server drift
            origin = shooter.Eye;
        if (dir.LengthSquared() < 0.5f)
            return;
        dir = Vector3.Normalize(dir);
        spread = Math.Clamp(spread, def.MinSpreadRad, def.MaxSpreadRad);

        var rewindTo = Math.Max(viewTick, (double)ServerTick - World.MaxRewindTicks);
        var targets = new List<(Player P, Box Hitbox, HistoryEntry At)>();
        foreach (var p in Players.Values)
        {
            if (p == shooter || !p.Alive || p.SpawnTick > rewindTo)
                continue;
            var at = HistoricState(p, rewindTo);
            targets.Add((p, World.Hitbox(at.Position, at.Stance, at.Yaw), at));
        }

        var damage = new Dictionary<Player, float>();
        var headshotOn = new HashSet<Player>();
        byte flags = 0;
        var ends = new List<Vector3>(def.Pellets);

        for (var i = 0; i < def.Pellets; i++)
        {
            var d = Weapons.SpreadDirection(dir, spread, seed, i);
            var dist = World.RaycastWorld(origin, d, World.Range);
            var turret = StreakRaycast(origin, d, ref dist, shooter); // enemy turrets and drones stop bullets too
            (Player P, Box Hitbox, HistoryEntry At)? hit = null;
            foreach (var t in targets)
            {
                if (t.Hitbox.Raycast(origin, d, dist) is { } td && td < dist)
                {
                    dist = td;
                    hit = t;
                    turret = null;
                }
            }
            var end = origin + d * dist;
            ends.Add(end);
            if (turret != null && DamageStreakEntity(turret, shooter, (int)MathF.Round(def.Damage * def.DamageMultAt(dist)), now))
                flags |= 4;
            else if (turret != null)
                flags |= 1;
            if (hit is { } h)
            {
                var head = World.IsHeadshot(h.At.Position, h.At.Stance, h.At.Yaw, end);
                var dmg = def.Damage * def.DamageMultAt(dist) * (head ? def.HeadMult : 1f)
                          * Perks.AttackMult(shooter.Perks, dist, d, h.At.Yaw, h.P.Health, melee: false);
                damage[h.P] = damage.GetValueOrDefault(h.P) + dmg;
                if (head)
                    headshotOn.Add(h.P);
            }
        }

        foreach (var (victim, dmg) in damage)
        {
            flags |= 1;
            var head = headshotOn.Contains(victim);
            if (head)
                flags |= 2;
            if (DamagePlayer(shooter, victim, (int)MathF.Round(dmg), head, def.Id, now, DamageKind.Bullet))
                flags |= 4;
        }

        // Host-side feedback
        if (shooter.Id == LocalId)
            OnOwnShotConfirmed(flags, now);
        else
            Audio.Shot(def, shooter.RenderEye);
        foreach (var end in ends)
            AddTracer(MuzzleWorld(shooter), end, def, now);

        _w.Begin(Msg.Shot).U8(shooter.Id).U8(def.Id).U8(flags).U8((byte)ends.Count);
        foreach (var end in ends)
            _w.Vec(end);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void OnOwnShotConfirmed(byte flags, double now)
    {
        if ((flags & 1) == 0)
            return;
        HitMarkerUntil = now + 0.15;
        HitMarkerHead = (flags & 2) != 0;
        if ((flags & 4) != 0)
            Audio.Effect(Sfx.Kill, 0.7f);
        else if (HitMarkerHead)
            Audio.Effect(Sfx.Headshot, 0.6f);
        else
            Audio.Effect(Sfx.HitTick, 0.6f);
    }

    private void SendSnapshots()
    {
        _body.Begin(Msg.State).U8((byte)Players.Count);
        foreach (var p in Players.Values)
        {
            _body.U8(p.Id)
                .Vec(p.Position)
                .Vec(p.Sim.Velocity)
                .F32(p.Yaw)
                .F32(p.Pitch)
                .U8((byte)Math.Clamp(p.Health, 0, 255))
                .U8((byte)((p.Alive ? 1 : 0) | (p.Sim.Grounded ? 2 : 0) | ((int)p.Sim.Stance << 2)))
                .U16(p.Kills)
                .U16(p.Deaths)
                .U8(p.Weapon)
                .U8(p.Primary)
                .U8(p.Secondary)
                .U24(p.PrimaryKit)
                .U24(p.SecondaryKit)
                .U8(p.Sim.MantleTicks)
                .Vec(p.Sim.MantleTo)
                .U8((byte)p.Perks.Movement)
                .U8((byte)p.Perks.Survival)
                .U8((byte)p.Perks.Damage)
                .U8(p.Sim.BoostTicks);
        }
        var body = _body.Span[1..];

        foreach (var (guid, id) in _clients.Values)
        {
            if (!Players.TryGetValue(id, out var p))
                continue;
            _w.Begin(Msg.State).U32(ServerTick).U32(p.LastProcessedSeq).U8((byte)Phase).F32((float)TimeLeft).U8(Winner);
            foreach (var b in body)
                _w.U8(b);
            Net.SendTo(guid, _w.Span, PacketReliability.UnreliableSequenced);
        }
    }

    private byte? FreeId()
    {
        for (byte id = 1; id < MaxPlayers; id++)
            if (!Players.ContainsKey(id))
                return id;
        return null;
    }

    // =====================================================================
    // Packets
    // =====================================================================

    public const int SnapPlayerBytes = 65;

    private void OnPacket(RakNetGUID sender, ReadOnlySpan<byte> data, double now)
    {
        var id = data[0];
        var r = new PacketReader(data[1..]);

        switch (id)
        {
            // ---------------- RakNet system messages ----------------
            case (byte)DefaultMessageIDTypes.NewIncomingConnection when IsServer:
            {
                if (FreeId() == null)
                    KickOneBot(); // humans take priority over bots
                if (FreeId() is not { } newId)
                    break;
                var p = new Player { Id = newId, Name = $"Player {newId}" };
                Players[newId] = p;
                PlaceWaiting(p, now);
                _clients[sender.G] = (sender, newId);
                _w.Begin(Msg.Welcome).U8(newId).Str(World.Map.Name).U32(World.Map.ContentHash)
                    .U32(WeaponSet.WeaponsHash).U8((byte)Weapons.All.Length).U32(WeaponSet.AttachmentsHash).U8((byte)Attachments.BySlot.Sum(x => x.Length));
                Net.SendTo(sender, _w.Span, PacketReliability.ReliableOrdered);
                if (VoteOpen)
                    SendVoteState(sender); // joined during the results screen: show the ballot
                Log($"Player {newId} connected ({Net.ConnectionCount} connected)");
                BroadcastRoster();
                break;
            }
            case (byte)DefaultMessageIDTypes.DisconnectionNotification:
            case (byte)DefaultMessageIDTypes.ConnectionLost:
                if (IsServer)
                {
                    if (_clients.Remove(sender.G, out var gone))
                    {
                        Log($"{NameOf(gone.Id)} left");
                        Players.Remove(gone.Id);
                        BroadcastRoster();
                    }
                }
                else
                {
                    Disconnected = true;
                    Status = "Disconnected from host";
                    Log(Status);
                }
                break;
            case (byte)DefaultMessageIDTypes.ConnectionRequestAccepted:
                Status = "Connected, waiting for welcome...";
                break;
            case (byte)DefaultMessageIDTypes.ConnectionAttemptFailed:
                Disconnected = true;
                Status = "Could not reach host (connection attempt failed)";
                Log(Status);
                break;
            case (byte)DefaultMessageIDTypes.NoFreeIncomingConnections:
                Disconnected = true;
                Status = "Host is full";
                Log(Status);
                break;

            // ---------------- Client -> server ----------------
            case (byte)Msg.Input when IsServer && r.Has(1):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                int count = r.U8();
                var maxSpeed = (byte)Math.Clamp((int)MathF.Round(MathF.Max(p.DefFor(p.Primary).Mobility, p.DefFor(p.Secondary).Mobility) * 100), 1, 200);
                for (var i = 0; i < count && r.Has(15); i++)
                {
                    var cmd = new InputCmd
                    {
                        Seq = r.U32(),
                        Buttons = (Buttons)r.U8(),
                        Yaw = r.F32(),
                        Pitch = r.F32(),
                        Weapon = r.U8(),
                        SpeedPct = r.U8(),
                    };
                    if (cmd.Seq <= p.HighestQueuedSeq || cmd.Seq <= p.LastProcessedSeq || !float.IsFinite(cmd.Yaw + cmd.Pitch))
                        continue;
                    cmd.Yaw = World.WrapAngle(cmd.Yaw);
                    cmd.Pitch = Math.Clamp(cmd.Pitch, -1.55f, 1.55f);
                    if (cmd.SpeedPct == 0 || cmd.SpeedPct > maxSpeed)
                        cmd.SpeedPct = maxSpeed; // no speed hacks beyond your fastest weapon
                    p.HighestQueuedSeq = cmd.Seq;
                    if (p.InputQueue.Count >= 60)
                        p.InputQueue.RemoveAt(0);
                    p.InputQueue.Add(cmd);
                }
                break;
            }
            case (byte)Msg.Fire when IsServer && r.Has(41):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                var weapon = p.DefFor(r.U8()); // with their attachments
                var origin = r.Vec();
                var dir = r.Vec();
                var spread = r.F32();
                var seed = r.U32();
                var viewTick = r.U32() + (double)Math.Clamp(r.F32(), 0f, 1f);
                if (float.IsFinite(origin.X + origin.Y + origin.Z + dir.X + dir.Y + dir.Z + spread))
                    ServerResolveShot(p, weapon, origin, dir, spread, seed, viewTick, now);
                break;
            }
            case (byte)Msg.Loadout when IsServer && r.Has(10):
            {
                if (_clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p))
                {
                    var (lp, ls, lg, le, lpk, lsk) = (r.U8(), r.U8(), (ThrowableKind)r.U8(), (ThrowableKind)r.U8(), r.U24(), r.U24());
                    var perks = r.Has(3) ? new PerkSet((PerkId)r.U8(), (PerkId)r.U8(), (PerkId)r.U8()) : PerkSet.Empty;
                    ServerRequestLoadout(p, lp, ls, lg, le, lpk, lsk, perks, now);
                    ReadLoadoutStreaks(p, ref r);
                }
                break;
            }
            case (byte)Msg.Suicide when IsServer:
                OnSuicidePacket(sender, now);
                break;
            case (byte)Msg.Respawn when IsServer:
            {
                if (_clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p) && !p.Alive)
                    p.WantsRespawn = true;
                break;
            }
            case (byte)Msg.Melee:
            case (byte)Msg.MeleeFx:
                OnMeleePacket(id, ref r, sender, now);
                break;
            case (byte)Msg.Throw:
            case (byte)Msg.ProjSpawn:
            case (byte)Msg.Explode:
                OnThrowablePacket(id, ref r, sender, now);
                break;
            case (byte)Msg.StreakUse:
            case (byte)Msg.StreakEvent:
            case (byte)Msg.StreakState:
            case (byte)Msg.StreakEnts:
                OnStreakPacket(id, ref r, sender, now);
                break;
            case (byte)Msg.Reload when IsServer && r.Has(1):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                var weapon = r.U8();
                if (!p.Owns(weapon))
                    break;
                Audio.Reload(Weapons.Get(weapon), p.RenderEye);
                ThirdPersonArms.OnReload(p.Id, p.DefFor(weapon).Reload, now);
                Net.Broadcast(ReloadFx(p.Id, weapon), PacketReliability.Unreliable);
                break;
            }

            // ---------------- Server -> client ----------------
            case (byte)Msg.Welcome when !IsServer && r.Has(1):
                LocalId = r.U8();
                if (r.Has(1) && !UseHostMap(r.Str(), r.Has(4) ? r.U32() : 0, initial: true))
                    break;
                // Weapons and attachments are data files: both sides must have the same set (ids, stats and kits must agree).
                if (r.Has(10) && WeaponSet.Mismatch(r.U32(), r.U8(), r.U32(), r.U8()) is { } weaponProblem)
                {
                    Status = weaponProblem;
                    Log(Status, true);
                    Disconnected = true;
                    break;
                }
                Status = $"Connected as player {LocalId} - {World.Map.Name}";
                Log(Status);
                // Tell the server who we are and what we want to play with.
                var nameBytes = System.Text.Encoding.ASCII.GetBytes(LocalName);
                _w.Begin(Msg.Hello).U8((byte)nameBytes.Length);
                foreach (var c in nameBytes)
                    _w.U8(c);
                Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
                SendLoadout();
                break;
            case (byte)Msg.Roster when !IsServer && r.Has(1):
                ReadRoster(ref r);
                break;
            case (byte)Msg.Hello when IsServer && r.Has(1):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                int len = r.U8();
                var chars = new char[len];
                for (var k = 0; k < len && r.Has(1); k++)
                    chars[k] = (char)r.U8();
                var name = Settings.Clean(new string(chars));
                if (Players.Values.Any(o => o != p && o.Name == name))
                    name = Settings.Clean($"{name[..Math.Min(name.Length, 13)]} {p.Id}");
                p.Name = name;
                Log($"{name} joined");
                BroadcastRoster();
                break;
            }
            case (byte)Msg.MatchEnd:
            case (byte)Msg.MatchStart:
                OnMatchPacket(id, ref r);
                break;
            case (byte)Msg.Ready:
            case (byte)Msg.ModeState:
            case (byte)Msg.ModeEvent:
                OnModePacket(id, ref r, sender);
                break;
            case (byte)Msg.MapVote:
            case (byte)Msg.VoteState:
                OnVotePacket(id, ref r, sender);
                break;
            case (byte)Msg.State when !IsServer && r.Has(15):
                ReadState(ref r, now);
                break;
            case (byte)Msg.Shot when !IsServer && r.Has(4):
            {
                var shooter = r.U8();
                var weaponId = r.U8();
                var def = Players.TryGetValue(shooter, out var who) ? who.DefFor(weaponId) : Weapons.Get(weaponId);
                var flags = r.U8();
                int n = r.U8();
                if (shooter == LocalId)
                {
                    OnOwnShotConfirmed(flags, now);
                    break; // we already drew and heard our own shot
                }
                if (!Players.TryGetValue(shooter, out var sp))
                    break;
                Audio.Shot(def, sp.RenderEye);
                for (var i = 0; i < n && r.Has(12); i++)
                    AddTracer(MuzzleWorld(sp), r.Vec(), def, now);
                break;
            }
            case (byte)Msg.ReloadFx when !IsServer && r.Has(2):
            {
                var who = r.U8();
                var weapon = r.U8();
                if (who != LocalId && Players.TryGetValue(who, out var p))
                {
                    Audio.Reload(Weapons.Get(weapon), p.RenderEye);
                    ThirdPersonArms.OnReload(who, p.DefFor(weapon).Reload, now);
                }
                break;
            }
            case (byte)Msg.Kill when !IsServer && r.Has(4):
            {
                var kill = KillPacket.Read(ref r); // the protocol 5 extras are read only if present
                LogKill(kill.Killer, kill.Victim, kill.Headshot, kill.KillId);
                RecordKill(kill);
                break;
            }
        }
    }

    private void ReadState(ref PacketReader r, double now)
    {
        var tick = r.U32();
        var ackSeq = r.U32();
        var phase = (MatchPhase)r.U8();
        var timeLeft = r.F32();
        var winner = r.U8();
        if (tick <= ServerTick && _clockStarted)
            return;
        var wasCountdown = Phase == MatchPhase.Countdown;
        if (Phase == MatchPhase.Warmup && phase == MatchPhase.Countdown)
            _countdownStartedFlag = true; // everyone's ready: the countdown opens the loadout screen
        Phase = phase;
        TimeLeft = timeLeft;
        if (wasCountdown && phase == MatchPhase.Playing)
            OnPlayStarted();
        Winner = winner;
        ServerTick = tick;
        OnSnapshotTiming(tick, now);

        int count = r.U8();
        var seen = new HashSet<byte>();
        for (var i = 0; i < count && r.Has(SnapPlayerBytes); i++)
        {
            var id = r.U8();
            var pos = r.Vec();
            var vel = r.Vec();
            var yaw = r.F32();
            var pitch = r.F32();
            var hp = r.U8();
            var flags = r.U8();
            var kills = r.U16();
            var deaths = r.U16();
            var weapon = r.U8();
            var primary = r.U8();
            var secondary = r.U8();
            var primaryKit = r.U24();
            var secondaryKit = r.U24();
            var mantleTicks = r.U8();
            var mantleTo = r.Vec();
            var perks = new PerkSet((PerkId)r.U8(), (PerkId)r.U8(), (PerkId)r.U8());
            var boost = r.U8();
            var alive = (flags & 1) != 0;
            var grounded = (flags & 2) != 0;
            var stance = (Stance)((flags >> 2) & 3);
            seen.Add(id);

            if (!Players.TryGetValue(id, out var p))
            {
                p = new Player { Id = id, Yaw = yaw, Pitch = pitch, Name = _names.GetValueOrDefault(id) ?? "" };
                p.Teleport(pos);
                Players[id] = p;
            }

            var wasAlive = p.Alive;
            p.Health = hp;
            p.Alive = alive;
            p.Kills = kills;
            p.Deaths = deaths;
            p.Primary = primary;
            p.Secondary = secondary;
            p.PrimaryKit = primaryKit;
            p.SecondaryKit = secondaryKit;
            p.Perks = perks;

            if (id == LocalId)
            {
                var respawned = !_localInitialized || (alive && !wasAlive);
                _localInitialized = true;
                if (respawned)
                {
                    p.Yaw = yaw;
                    p.Pitch = pitch;
                    OnLocalSpawn(p);
                }
                else
                {
                    SyncLocalSlots(p, refill: false); // loadout applied mid-life (out of combat)
                }
                Reconcile(p, ackSeq, new MoveState
                {
                    Position = pos, Velocity = vel, Grounded = grounded, Stance = stance, MantleTicks = mantleTicks, MantleTo = mantleTo,
                    MovePerk = perks.Movement, BoostTicks = boost,
                }, alive, respawned);
            }
            else
            {
                p.Weapon = weapon;
                p.Sim = new MoveState
                {
                    Position = pos, Velocity = vel, Grounded = grounded, Stance = stance, MantleTicks = mantleTicks, MantleTo = mantleTo,
                    MovePerk = perks.Movement, BoostTicks = boost,
                };
                p.Yaw = yaw;
                p.Pitch = pitch;
                if (alive && !wasAlive)
                    p.Snaps.Clear();
                p.Snaps.Add(new Snap { Tick = tick, Position = pos, Velocity = vel, Yaw = yaw, Pitch = pitch, Stance = stance });
                if (p.Snaps.Count > 64)
                    p.Snaps.RemoveAt(0);
            }
        }

        foreach (var stale in Players.Keys.Where(k => !seen.Contains(k)).ToList())
            Players.Remove(stale);
    }

    private void LogKill(byte killer, byte victim, bool headshot, byte killId)
    {
        string Name(byte id) => id == LocalId ? "You" : NameOf(id);
        Log($"{Name(killer)}  [{Throwables.KillName(killId)}{(headshot ? " - HEADSHOT" : "")}]  {Name(victim)}", killer == LocalId || victim == LocalId);

        // Progression: the local player earns XP for every kill, in single player and multiplayer.
        if (killer == LocalId && victim != LocalId && Progress != null)
        {
            var now = Brutal.GlfwApi.Glfw.Time;
            if (Progress.AwardKill(headshot, killId >= Throwables.KillIdBase, now, killId) != null)
                Audio.Effect(Sfx.LevelUp, 0.8f);
        }
    }

    public void Dispose() => Net.Dispose();
}
