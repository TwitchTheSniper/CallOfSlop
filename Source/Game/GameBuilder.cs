using System.Numerics;

namespace CallOfSlop;

/// <summary>One projectile of a shot: where it starts, where it goes, its damage and which barrel fired it.</summary>
public readonly record struct ShotRay(Vector3 Origin, Vector3 Dir, float Damage, Vector2 Lateral, int Barrel);

/// <summary>
/// Firing support for built weapons (Zombies weapon builder): several barrels per weapon, each firing its own pellets
/// from its own muzzle, receivers taking turns, and minigun spool-up. Standard weapons (no <see cref="WeaponDef.MuzzlePoints"/>,
/// no spool) produce exactly the rays they always did: one per pellet from the eye, seeded by pellet index.
/// </summary>
public sealed partial class Game
{
    /// <summary>A barrel this far or more off the bore line (sideways or up) is pulled in to it (keeps rays out of walls next to you).</summary>
    public const float MaxBarrelOffset = 0.45f;

    /// <summary>
    /// The projectiles of one shot. <paramref name="seed"/> is the shot's seed (the client's shot counter), which also
    /// picks whose turn it is on rotating grips and rotary receivers, so client prediction and the server agree.
    /// Each barrel starts at the eye moved sideways/up by its muzzle's offset from the barrels' centre (in view space), is
    /// turned by its splay, and gets its own pellets (seeded by a running pellet index) with its own spread and damage.
    /// </summary>
    public static List<ShotRay> ShotRays(WeaponDef def, Vector3 origin, Vector3 dir, float spread, uint seed, List<ShotRay>? into = null)
    {
        var rays = into ?? new List<ShotRay>(def.Pellets);
        rays.Clear();
        if (def.MuzzlePoints is not { Length: > 0 } pts)
        {
            for (var i = 0; i < def.Pellets; i++)
                rays.Add(new ShotRay(origin, Weapons.SpreadDirection(dir, spread, seed, i), def.Damage, Vector2.Zero, 0));
            return rays;
        }

        var right = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY));
        var up = Vector3.Cross(right, dir);
        var centre = Vector2.Zero;
        foreach (var p in pts)
            centre += new Vector2(p.Muzzle.X, p.Muzzle.Y);
        centre /= pts.Length;

        var groups = 0;
        foreach (var p in pts)
            groups = Math.Max(groups, p.Group + 1);
        var turnGroup = def.RotateGroups && groups > 0 ? (int)(seed % (uint)groups) : -1;
        var turn = def.RotateGroups && groups > 0 ? seed / (uint)groups : seed; // shots this group has had (rotary barrels within it)
        var pellet = 0;
        for (var g = 0; g < groups; g++)
        {
            if (turnGroup >= 0 && g != turnGroup)
                continue;
            var n = 0;
            var rotary = false;
            foreach (var p in pts)
                if (p.Group == g)
                {
                    n++;
                    rotary |= p.Rotary;
                }
            var pick = rotary && n > 0 ? (int)(turn % (uint)n) : -1;
            var k = 0;
            for (var b = 0; b < pts.Length; b++)
            {
                var p = pts[b];
                if (p.Group != g)
                    continue;
                if (pick >= 0 && k++ != pick)
                    continue;
                var lat = new Vector2(p.Muzzle.X, p.Muzzle.Y) - centre;
                if (lat.Length() > MaxBarrelOffset)
                    lat = Vector2.Normalize(lat) * MaxBarrelOffset;
                var o = origin + right * lat.X + up * lat.Y;
                var d = dir;
                if (p.YawDeg != 0 || p.PitchDeg != 0)
                    d = Vector3.Normalize(dir + right * MathF.Tan(p.YawDeg * MathF.PI / 180f) + up * MathF.Tan(p.PitchDeg * MathF.PI / 180f));
                var s = spread * p.SpreadMult;
                for (var j = 0; j < p.Pellets; j++)
                    rays.Add(new ShotRay(o, Weapons.SpreadDirection(d, s, seed, pellet++), def.Damage * p.DamageMult, lat, b));
            }
        }
        return rays;
    }

    /// <summary>Where a built weapon's barrel shows its tracer: the usual muzzle point moved by the barrel's offset.</summary>
    public static Vector3 MuzzleWorld(Player p, Vector2 lateral)
    {
        var m = MuzzleWorld(p);
        if (lateral == Vector2.Zero)
            return m;
        var fwd = GravityFrames.Forward(p.RenderPosition, p.RenderYaw, p.RenderPitch);
        var right = Vector3.Normalize(Vector3.Cross(fwd, MathF.Abs(fwd.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        return m + right * lateral.X + up * lateral.Y;
    }

    private double _testShotClock;

    /// <summary>Self-test: resolves one shot of <paramref name="def"/> by the local (host) player through the real server path.</summary>
    internal void TestServerShot(WeaponDef def, Vector3 dir, uint seed)
    {
        if (Local is { } me)
        {
            // Its own clock, 10 s apart per test shot, so the fire-rate budget never refuses one (the window clock can be
            // stopped or 0 in a headless run, e.g. on Linux CI with no display).
            _testShotClock += 10;
            ServerResolveShot(me, def, me.Eye, dir, 0f, seed, ServerTick, Math.Max(Brutal.GlfwApi.Glfw.Time, 0) + _testShotClock);
        }
    }

    /// <summary>Self-test: one frame of the local weapon's state (a reload finishing, aiming) as the frame loop runs it.</summary>
    internal void SelfTestWeaponFrame(double now, float dt) => UpdateWeaponState(now, dt);

    // ---- Spool-up (miniguns, chain guns) ---------------------------------------------------

    private float _spool;
    private double _spoolAt;

    /// <summary>Fraction of full fire rate the active weapon fires at right now (1 for anything without a spool-up).</summary>
    public float SpoolRate { get; private set; } = 1f;

    /// <summary>Called every frame with the trigger: winds the spool up while held, down (twice as fast) when released.</summary>
    private void UpdateSpool(WeaponDef def, bool held, double now)
    {
        var dt = (float)Math.Clamp(now - _spoolAt, 0, 0.25);
        _spoolAt = now;
        if (def.SpoolTime <= 0)
        {
            _spool = 1f;
            SpoolRate = 1f;
            return;
        }
        _spool = Math.Clamp(_spool + (held ? dt : -2f * dt) / def.SpoolTime, 0f, 1f);
        SpoolRate = float.Lerp(def.SpoolStart, 1f, _spool);
    }

    /// <summary>Seconds until the next shot at the current spool rate.</summary>
    private float SpooledInterval(WeaponDef def) => def.SpoolTime > 0 ? def.FireInterval / MathF.Max(0.05f, SpoolRate) : def.FireInterval;

    // ---- Workbench range (single-player sandbox) ------------------------------------------

    /// <summary>A Workbench Range session (Zombies preview): built weapons, no loadout screen, respawn on your own.</summary>
    public bool BuilderRange;

    /// <summary>Gives the local (host) player built weapons in both slots for the Workbench Range.</summary>
    public void UseBuiltWeapons(WeaponDef primary, WeaponDef secondary)
    {
        if (Local is not { } me)
            return;
        if (secondary.Id == primary.Id && !ReferenceEquals(secondary, primary))
        {
            // Two builds borrowing the same gun's sounds: the second takes another id so the slots stay apart.
            secondary = secondary.Clone();
            secondary.Id = Weapons.All.First(w => w.Id != primary.Id).Id;
        }
        me.BuiltPrimary = primary;
        me.BuiltSecondary = secondary;
        me.WantPrimary = me.Primary = primary.Id;
        me.WantSecondary = me.Secondary = secondary.Id;
        me.WantPrimaryKit = me.WantSecondaryKit = me.PrimaryKit = me.SecondaryKit = 0;
        me.Weapon = me.Primary;
        SyncLocalSlots(me, refill: true);
    }
}
