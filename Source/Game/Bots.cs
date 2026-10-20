using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>Per-bot AI state. Bots live only on the server and play through the same input commands as people.</summary>
public sealed class BotBrain(BotDifficulty skill)
{
    public readonly BotDifficulty Skill = skill;
    public byte Target = byte.MaxValue;
    public double TargetSince;
    public double NextScan;
    public Vector3 LastSeenAt;
    public double LastSeenTime = -100;
    public float StrafeDir = 1;
    public double StrafeUntil;
    public Vector3 Waypoint;
    public float AimErrYaw, AimErrPitch;
    public int Ammo;
    public double ReloadUntil;
    public double NextFire;
    public int BurstLeft;
    public Vector3 StuckCheckPos;
    public double StuckCheckTime;
    public bool JumpNext;
    public double CrouchUntil;
    public uint Seq;

    // Tuning per difficulty
    public double Reaction => Skill switch { BotDifficulty.Easy => 0.65, BotDifficulty.Normal => 0.38, _ => 0.2 };
    public float AimError => Skill switch { BotDifficulty.Easy => 0.14f, BotDifficulty.Normal => 0.075f, _ => 0.03f }; // radians
    public float TurnSpeed => Skill switch { BotDifficulty.Easy => 3.5f, BotDifficulty.Normal => 6f, _ => 10f }; // rad/s
    public float Settle => Skill switch { BotDifficulty.Easy => 1.2f, BotDifficulty.Normal => 2.4f, _ => 4.5f }; // aim error decay /s
    public double TriggerDelay => Skill switch { BotDifficulty.Easy => 0.25, BotDifficulty.Normal => 0.12, _ => 0.05 }; // extra delay on semi-autos
}

public sealed partial class Game
{
    private static readonly string[] BotNames =
        ["Ace", "Brick", "Cinder", "Dozer", "Echo", "Fang", "Grit", "Havoc", "Ion", "Jinx", "Knox", "Lynx", "Moss", "Nova"];

    /// <summary>Adds a bot (server only). Returns false if the server is full.</summary>
    public bool AddBot(BotDifficulty skill)
    {
        if (!IsServer || FreeId() is not { } id)
            return false;
        var used = Players.Values.Select(p => p.Name).ToHashSet();
        var name = BotNames.Select(n => $"[BOT] {n}").FirstOrDefault(n => !used.Contains(n)) ?? $"[BOT] {id}";
        var bot = new Player { Id = id, Name = name, Bot = new BotBrain(skill) };
        Players[id] = bot;
        Spawn(bot, ServerTick, Brutal.GlfwApi.Glfw.Time);
        Log($"{name} joined ({skill})");
        BroadcastRoster();
        return true;
    }

    private bool KickOneBot()
    {
        var bot = Players.Values.FirstOrDefault(p => p.Bot != null);
        if (bot == null)
            return false;
        Players.Remove(bot.Id);
        Log($"{bot.Name} left to make room");
        return true;
    }

    private void PickBotLoadout(Player bot)
    {
        // Mostly sensible guns, the occasional silly one.
        var primaries = Weapons.All.Where(w => w.Slot == Slot.Primary && (w.Class != WeaponClass.Special || _rng.NextDouble() < 0.15)).ToArray();
        var secondaries = Weapons.All.Where(w => w.Slot == Slot.Secondary).ToArray();
        bot.WantPrimary = primaries[_rng.Next(primaries.Length)].Id;
        bot.WantSecondary = secondaries[_rng.Next(secondaries.Length)].Id;
        bot.WantGrenade = _rng.NextDouble() < 0.8 ? ThrowableKind.Frag : ThrowableKind.Impact;
        bot.WantEquipment = ThrowableKind.None;
        bot.WantPrimaryKit = RandomKit(Weapons.Get(bot.WantPrimary));
        bot.WantSecondaryKit = RandomKit(Weapons.Get(bot.WantSecondary));
        bot.WantPerks = RandomPerks();
    }

    /// <summary>Bots run a couple of random attachments too (no optics: they don't need them).</summary>
    private uint RandomKit(WeaponDef w)
    {
        uint kit = 0;
        foreach (var a in Attachments.For(w))
            if (a.Slot != AttachSlot.Optic && Attachments.Slot(kit, a.Slot) == 0 && _rng.NextDouble() < 0.25)
                kit = Attachments.With(kit, a.Slot, a.Index);
        return kit;
    }

    private Vector3 RandomWaypoint()
    {
        var w = World.Map.Waypoints;
        return w.Count > 0 ? w[_rng.Next(w.Count)] : Vector3.Zero;
    }

    /// <summary>
    /// Prefers a waypoint the bot can walk to in a straight line (clear at knee and chest height),
    /// not too close and not across the whole map. Bots steer in straight lines, so on big maps
    /// with lots of buildings this keeps them flowing down streets instead of hugging walls.
    /// </summary>
    private Vector3 NextWaypoint(Vector3 from)
    {
        var w = World.Map.Waypoints;
        if (w.Count == 0)
            return Vector3.Zero;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var c = w[_rng.Next(w.Count)];
            var d = Vector3.Distance(from, c);
            if (d < 6f || d > 70f)
                continue;
            if (HasLineOfSight(from + new Vector3(0, 0.5f, 0), c + new Vector3(0, 0.5f, 0))
                && HasLineOfSight(from + new Vector3(0, 1.3f, 0), c + new Vector3(0, 1.3f, 0)))
                return c;
        }
        return w[_rng.Next(w.Count)];
    }

    private static float PreferredRange(WeaponDef w) =>
        w.Class switch
        {
            WeaponClass.Shotgun => w.Pellets > 1 ? 4f : 14f,
            WeaponClass.SMG or WeaponClass.Pistol => 9f,
            WeaponClass.Sniper => 26f,
            WeaponClass.DMR => 20f,
            _ => 14f,
        };

    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var dist = d.Length();
        if (dist < 0.01f)
            return true;
        if (World.RaycastWorld(from, d / dist, dist) < dist - 0.05f)
            return false;
        return !SmokeBlocks(from, to, Brutal.GlfwApi.Glfw.Time); // bots can't see through smoke either
    }

    /// <summary>One simulation tick of bot thinking: perceive, aim, shoot, move.</summary>
    private void BotTick(Player p, double now)
    {
        var b = p.Bot!;
        if (!p.Alive)
        {
            b.Target = byte.MaxValue;
            return;
        }

        var def = p.WeaponDef;
        var dt = World.TickDt;
        if (b.ReloadUntil > 0 && now >= b.ReloadUntil)
        {
            b.Ammo = def.Mag;
            b.ReloadUntil = 0;
        }

        // ---- Perception (a few times a second) ----------------------------------------
        Player? target = Players.GetValueOrDefault(b.Target);
        if (now >= b.NextScan)
        {
            b.NextScan = now + 0.2 + _rng.NextDouble() * 0.1;
            var fwd = World.FlatForward(p.Yaw);
            Player? best = null;
            var bestScore = float.MaxValue;
            foreach (var o in Players.Values)
            {
                if (o == p || !o.Alive)
                    continue;
                var to = o.Position - p.Position;
                var dist = to.Length();
                var flat = Vector3.Normalize(new Vector3(to.X, 0, to.Z) + new Vector3(1e-4f, 0, 0));
                var inView = Vector3.Dot(flat, fwd) > -0.2f || dist < 7f; // ~200 deg vision, plus "hearing" up close
                if (!inView || !HasLineOfSight(p.Eye, o.Chest))
                    continue;
                var score = dist + (o.Id == b.Target ? -5f : 0f); // stick with the current target a little
                if (score < bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }

            if (best != null && best.Id != b.Target)
            {
                b.TargetSince = now;
                b.AimErrYaw = ((float)_rng.NextDouble() - 0.5f) * 2 * b.AimError * 3;
                b.AimErrPitch = ((float)_rng.NextDouble() - 0.5f) * 2 * b.AimError * 2;
            }
            b.Target = best?.Id ?? byte.MaxValue;
            target = best;
        }
        if (target is { Alive: false })
        {
            target = null;
            b.Target = byte.MaxValue;
        }

        var visible = target != null && HasLineOfSight(p.Eye, target.Chest);
        if (visible)
        {
            b.LastSeenAt = target!.Position;
            b.LastSeenTime = now;
        }

        // ---- Aim -------------------------------------------------------------------
        float wantYaw = p.Yaw, wantPitch = 0;
        float aimError = 10;
        var distToTarget = 0f;
        if (visible)
        {
            var chest = target!.Chest + target.Sim.Velocity * 0.08f;
            if (b.Skill == BotDifficulty.Hard && _rng.NextDouble() < 0.02 && target.Sim.Stance != Stance.Prone)
                chest.Y += World.HeightOf(target.Sim.Stance) * 0.25f; // occasionally go for the head
            var d = chest - p.Eye;
            distToTarget = d.Length();
            d /= distToTarget;
            b.AimErrYaw *= MathF.Exp(-b.Settle * dt);
            b.AimErrPitch *= MathF.Exp(-b.Settle * dt);
            var wobble = b.AimError * 0.15f;
            wantYaw = MathF.Atan2(d.X, -d.Z) + b.AimErrYaw + ((float)_rng.NextDouble() - 0.5f) * wobble;
            wantPitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f)) + b.AimErrPitch;
        }
        else if (now - b.LastSeenTime < 3)
        {
            var d = b.LastSeenAt - p.Position;
            wantYaw = MathF.Atan2(d.X, -d.Z);
        }
        else
        {
            if (Vector3.Distance(p.Position, b.Waypoint) < 2.5f || b.Waypoint == Vector3.Zero)
                b.Waypoint = NextWaypoint(p.Position);
            var d = b.Waypoint - p.Position;
            wantYaw = MathF.Atan2(d.X, -d.Z);
        }

        var yawDelta = World.WrapAngle(wantYaw - p.Yaw);
        var maxTurn = b.TurnSpeed * dt;
        var yaw = World.WrapAngle(p.Yaw + Math.Clamp(yawDelta, -maxTurn, maxTurn));
        var pitch = p.Pitch + Math.Clamp(wantPitch - p.Pitch, -maxTurn, maxTurn);
        if (visible)
            aimError = MathF.Abs(World.WrapAngle(wantYaw - yaw)) + MathF.Abs(wantPitch - pitch);

        // ---- Shoot -------------------------------------------------------------------
        var aiming = visible && distToTarget > 14f && def.AdsZoom >= 1.4f;
        if (b.Ammo <= 0 && b.ReloadUntil == 0)
            BotReload(p, def, now);
        else if (!visible && now - b.LastSeenTime > 2 && b.Ammo < def.Mag * 0.4f && b.ReloadUntil == 0)
            BotReload(p, def, now);

        var tolerance = MathF.Atan2(0.45f, MathF.Max(distToTarget, 1f)) + 0.02f;
        var ready = visible && now - b.TargetSince >= b.Reaction && aimError < tolerance;
        // Right up close: melee instead of shooting.
        var meleed = visible && DamageEnabled && now - b.TargetSince >= b.Reaction && aimError < 0.5f
                     && BotTryMelee(p, b, target!, distToTarget, yaw, pitch, now);
        if (!meleed && (ready || b.BurstLeft > 0) && now >= b.NextFire && b.Ammo > 0 && b.ReloadUntil == 0 && DamageEnabled
            && (distToTarget < def.RangeM * 3f || def.Class == WeaponClass.Sniper))
        {
            var spread = def.HipSpreadRad * (aiming ? def.AdsSpreadMult : 1f) + (b.Skill == BotDifficulty.Easy ? 0.02f : 0f);
            ServerResolveShot(p, def, p.Eye, World.Forward(yaw, pitch), spread, (uint)_rng.Next(), ServerTick, now);
            b.Ammo--;
            b.NextFire = now + def.FireInterval;
            switch (def.Mode)
            {
                case FireMode.Semi:
                    b.NextFire += b.TriggerDelay + _rng.NextDouble() * b.TriggerDelay;
                    break;
                case FireMode.Burst:
                    b.BurstLeft = b.BurstLeft > 0 ? b.BurstLeft - 1 : def.BurstCount - 1;
                    if (b.BurstLeft == 0)
                        b.NextFire += def.BurstDelay + b.TriggerDelay;
                    break;
            }
            // Recoil nudges their aim off target; they have to pull it back.
            var kick = def.RecoilDeg * MathF.PI / 180f;
            b.AimErrPitch += kick * 0.6f;
            b.AimErrYaw += ((float)_rng.NextDouble() - 0.5f) * kick * 0.5f;
        }

        // ---- Grenades -------------------------------------------------------------------
        // Lob a frag at someone in mid range now and then (never on Easy).
        if (visible && b.Skill != BotDifficulty.Easy && p.Grenade != ThrowableKind.None && p.GrenadesLeft > 0
            && now >= p.NextThrow && distToTarget is > 6f and < 15f
            && _rng.NextDouble() < (b.Skill == BotDifficulty.Hard ? 0.006 : 0.003))
        {
            // Pick the launch angle that lands it at the target's distance (flat-ground ballistics).
            var v = Throwables.ThrowSpeed;
            var s = Math.Clamp(World.Gravity * distToTarget / (v * v), 0f, 1f);
            var launch = 0.5f * MathF.Asin(s) - 0.12f; // a little lower: throws also get +2.5 m/s upward
            var toTarget = target!.Position - p.Position;
            var throwYaw = MathF.Atan2(toTarget.X, -toTarget.Z) + ((float)_rng.NextDouble() - 0.5f) * b.AimError;
            ServerThrow(p, p.Grenade, p.Eye, World.Forward(throwYaw, launch), now);
        }

        // ---- Move ------------------------------------------------------------------
        var wish = Vector3.Zero;
        var buttons = Buttons.None;
        if (visible)
        {
            var to = target!.Position - p.Position;
            to.Y = 0;
            var dist = to.Length();
            var dir = dist > 0.01f ? to / dist : Vector3.Zero;
            var pref = PreferredRange(def);
            if (dist > pref + 3)
                wish += dir;
            else if (dist < pref - 3)
                wish -= dir;
            if (now >= b.StrafeUntil)
            {
                b.StrafeDir = _rng.NextDouble() < 0.5 ? -1 : 1;
                b.StrafeUntil = now + 0.5 + _rng.NextDouble() * 1.2;
            }
            wish += new Vector3(-dir.Z, 0, dir.X) * b.StrafeDir * (b.Skill == BotDifficulty.Easy ? 0.5f : 1f);
            if (b.Skill == BotDifficulty.Hard && _rng.NextDouble() < 0.004)
                buttons |= Buttons.Jump;
        }
        else
        {
            var goal = now - b.LastSeenTime < 3 ? b.LastSeenAt : b.Waypoint;
            var to = goal - p.Position;
            to.Y = 0;
            if (to.LengthSquared() > 1)
                wish = Vector3.Normalize(to);
            if (to.Length() > 8)
                buttons |= Buttons.Sprint;
        }

        // Stuck on a crate? Hop and pick somewhere else.
        if (now - b.StuckCheckTime > 1.0)
        {
            if (wish.LengthSquared() > 0.1f && Vector3.Distance(p.Position, b.StuckCheckPos) < 0.6f)
            {
                b.JumpNext = true;
                b.StrafeDir = -b.StrafeDir;
                b.Waypoint = NextWaypoint(p.Position);
            }
            b.StuckCheckPos = p.Position;
            b.StuckCheckTime = now;
        }
        if (b.JumpNext && p.Sim.Grounded)
        {
            buttons |= Buttons.Jump;
            b.JumpNext = false;
        }

        // Stance: steady long shots by crouching; hard snipers go prone. Stand up to move or when nothing's in sight.
        var longGun = def.Class is WeaponClass.Sniper or WeaponClass.DMR or WeaponClass.LMG;
        if (visible && distToTarget > 16f && longGun && b.Skill != BotDifficulty.Easy)
        {
            if (b.Skill == BotDifficulty.Hard && def.Class == WeaponClass.Sniper && distToTarget > 24f)
            {
                buttons |= Buttons.Prone;
                wish = Vector3.Zero; // stay put while prone
            }
            else
            {
                buttons |= Buttons.Crouch;
            }
            buttons &= ~Buttons.Jump;
        }
        else if (visible && b.Skill == BotDifficulty.Hard && _rng.NextDouble() < 0.01)
        {
            b.CrouchUntil = now + 0.4 + _rng.NextDouble() * 0.6; // crouch-peek in close fights
        }
        if (now < b.CrouchUntil)
            buttons |= Buttons.Crouch;

        // World-space wish -> buttons relative to where the bot is facing.
        if (wish.LengthSquared() > 0.01f)
        {
            wish = Vector3.Normalize(wish);
            var f = Vector3.Dot(wish, World.FlatForward(yaw));
            var r = Vector3.Dot(wish, World.FlatRight(yaw));
            if (f > 0.35f) buttons |= Buttons.Forward;
            if (f < -0.35f) buttons |= Buttons.Back;
            if (r > 0.35f) buttons |= Buttons.Right;
            if (r < -0.35f) buttons |= Buttons.Left;
        }

        var cmd = new InputCmd
        {
            Seq = ++b.Seq,
            Buttons = buttons,
            Yaw = yaw,
            Pitch = Math.Clamp(pitch, -1.5f, 1.5f),
            Weapon = p.Weapon,
            SpeedPct = (byte)Math.Clamp((int)(def.Mobility * (aiming ? World.AdsSpeedMult : 1f) * 100), 1, 200),
        };
        ApplyCmd(p, cmd);
    }

    private void BotReload(Player p, WeaponDef def, double now)
    {
        var b = p.Bot!;
        b.ReloadUntil = now + def.Reload;
        b.BurstLeft = 0;
        Audio.Reload(def, p.RenderEye);
        Net.Broadcast(ReloadFx(p.Id, def.Id), PacketReliability.Unreliable);
    }

    // ---- Names / roster ------------------------------------------------------------

    /// <summary>Sends everyone the id -> name table (server only).</summary>
    private void BroadcastRoster()
    {
        if (!IsServer)
            return;
        _w.Begin(Msg.Roster).U8((byte)Players.Count);
        foreach (var p in Players.Values)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(p.Name);
            _w.U8(p.Id).U8(p.Bot != null ? (byte)1 : (byte)0).U8((byte)bytes.Length);
            foreach (var c in bytes)
                _w.U8(c);
        }
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void ReadRoster(ref PacketReader r)
    {
        int count = r.U8();
        for (var i = 0; i < count && r.Has(3); i++)
        {
            var id = r.U8();
            var isBot = r.U8() != 0;
            int len = r.U8();
            var chars = new char[len];
            for (var k = 0; k < len && r.Has(1); k++)
                chars[k] = (char)r.U8();
            var name = Settings.Clean(new string(chars));
            _names[id] = isBot ? name : name;
            if (Players.TryGetValue(id, out var p))
                p.Name = name;
        }
    }

    private readonly Dictionary<byte, string> _names = new();

    public string NameOf(byte id) =>
        Players.TryGetValue(id, out var p) && p.Name.Length > 0 ? p.Name : _names.GetValueOrDefault(id) ?? $"Player {id}";
}
