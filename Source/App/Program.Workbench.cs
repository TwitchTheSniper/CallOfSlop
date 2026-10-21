using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Weapon Workbench (Zombies preview): opening the Workbench Range with two built weapons, coming back to the
/// workbench afterwards, and a small smoke-test step (workbench screenshots, then the range with a spun-up minigun).
/// </summary>
public static unsafe partial class Program
{
    /// <summary>Set while StartSession builds a Workbench Range session (read by its hooks).</summary>
    private static WorkbenchLaunch? _rangeLaunch;

    /// <summary>Reopen the workbench when the range session ends.</summary>
    private static bool _returnToWorkbench;

    /// <summary>Held trigger for the smoke step (ORed into the real trigger).</summary>
    private static bool _smokeFireHeld;

    private const string RangeMapName = "Workbench Range";

    private static void StartWorkbenchRange(WorkbenchLaunch launch)
    {
        if (Maps.Find(RangeMapName) == null)
        {
            _lobbyMessage = "The Workbench Range map is missing (Maps/Dev/WorkbenchRange.json).";
            Ui.WorkbenchOpen = false;
            return;
        }
        Ui.WorkbenchOpen = false;
        _rangeLaunch = launch;
        try
        {
            StartSession(LobbyAction.SinglePlayer);
        }
        finally
        {
            _rangeLaunch = null;
        }
    }

    /// <summary>StartSession hook: the range map while launching from the workbench, else null.</summary>
    private static Map? RangeMap => _rangeLaunch != null ? Maps.Find(RangeMapName) : null;

    /// <summary>StartSession hook: turns a fresh single-player session into the Workbench Range.</summary>
    private static void ConfigureRange(Game g)
    {
        var l = _rangeLaunch!;
        g.BuilderRange = true; // no loadout screen; you respawn on your own
        g.WarmupEnabled = false;
        g.Mode = GameMode.FreeForAll;
        g.UseBuiltWeapons(l.Primary, l.Secondary, l.Third, l.Fourth, l.Melee, l.ExtraSlots);
        g.SlotStation = SlotStationSpot; // the weapon-slot upgrade, bought with the interact key (free here)
        if (l.Dummies > 0)
            g.AddPracticeDummies(l.Dummies);
        g.RequestRespawn();
        g.LoadoutNotice = $"Workbench Range: {l.Primary.Name} + {l.Secondary.Name}. Esc > Leave to go back to the workbench.";
        _returnToWorkbench = !_smoke;
        Console.WriteLine($"[workbench] range: {l.Primary.Name} ({l.Primary.BarrelCount} barrel(s), {l.Primary.Dps:0} DPS) + {l.Secondary.Name}"
                          + $"{(l.Third != null ? $" + {l.Third.Name}" : "")}{(l.Fourth != null ? $" + {l.Fourth.Name}" : "")}, slots {(l.ExtraSlots ? 4 : 2)}, "
                          + $"melee {l.Melee?.ToString() ?? "standard"}, {l.Dummies} dummies");
    }

    /// <summary>The lobby frame while the workbench is up (or should come back after the range).</summary>
    private static bool DrawWorkbenchLobby(double now)
    {
        if (_returnToWorkbench && _session == null)
        {
            _returnToWorkbench = false;
            Ui.OpenWorkbench();
        }
        if (!Ui.WorkbenchOpen)
            return false;
        if (Ui.DrawWorkbench(AudioSystem, now) is { } launch)
            StartWorkbenchRange(launch);
        return true;
    }

    /// <summary>Where the Workbench Range's slot upgrade station stands (by the spawn line, right side).</summary>
    public static readonly Vector3 SlotStationSpot = new(12.5f, 0, 61.5f);

    /// <summary>The world view matrix of this frame (set while drawing the world; the pushed cart's viewmodel pose uses it).</summary>
    private static Matrix4x4 _worldView = Matrix4x4.Identity;

    /// <summary>Where your pushed cart is drawn this frame: its server position moved with your own render smoothing.</summary>
    private static Vector3 LocalCartRenderPos(CartEntity c, Player me) => c.Body.Position + (me.RenderPosition - me.Position);

    /// <summary>
    /// The camera-space pose of the cart you're pushing or manning (its world matrix times the view: the hands go to its
    /// bar), or null when you aren't at one. A manned cart stands still and is drawn in the world (its gun turns).
    /// </summary>
    private static Matrix4x4? PushedCartPose(WeaponDef def)
    {
        if (_game.Local is not { Alive: true } me || !Game.IsCart(def) || !_game.Carts.TryGetValue(me.Id, out var c) || c.Mode == CartMode.Idle)
            return null;
        if (c.Mode == CartMode.Manned)
            return c.Shape.World(c.Body.Position, c.Body.Yaw) * _worldView;
        var rattle = 0.004f * MathF.Sin(c.Body.Rolled * 23f); // the wheels rattle as it rolls
        var pose = c.Shape.World(LocalCartRenderPos(c, me) + new Vector3(0, rattle, 0), c.Body.Yaw) * _worldView;
        // Pushing, the real cart sits under the bottom of the view (your eyes are well above its basket). Swing it up about
        // the eye so its basket and gun read ahead of you, no more than CartViewDeg below the middle; the hands follow the bar.
        var centre = Vector3.Transform(c.Shape.ModelGround + new Vector3(0, c.Shape.Half.Y, 0), pose);
        if (centre.Z < -0.05f)
        {
            var below = MathF.Atan2(-centre.Y, -centre.Z);
            var lift = Math.Clamp(below - CartViewDeg * MathF.PI / 180f, 0f, 0.7f);
            if (lift > 0)
                pose *= Matrix4x4.CreateRotationX(lift);
        }
        return pose;
    }

    /// <summary>The pushed cart's basket shows at most this far below the middle of the view (degrees).</summary>
    private const float CartViewDeg = 17f;

    /// <summary>A cart build split for the manned view: the cart's frame (stays put) and its gun (everything mounted on it, turns), turned about <see cref="Pivot"/>.</summary>
    private sealed record CartGunParts(GunMesh Source, WeaponDef Frame, WeaponDef? Gun, Vector3 Pivot);

    private static readonly Dictionary<WeaponDef, CartGunParts> _cartGunParts = new(ReferenceEqualityComparer.Instance);

    /// <summary>The frame / gun split of a cart build (cached per build mesh), or null for a weapon without a built mesh.</summary>
    private static CartGunParts? CartParts(WeaponDef def)
    {
        if (def.BuiltMesh is not { } m)
            return null;
        if (_cartGunParts.TryGetValue(def, out var cached) && ReferenceEquals(cached.Source, m))
            return cached;
        var k = Math.Clamp(m.RootParts, 0, m.Parts.Length);
        var frame = def.Clone();
        frame.Optic = OpticKind.Iron; // an optic rides on the gun
        frame.BuiltMesh = m.Slice(0, k);
        frame.BuiltMesh.Muzzles = [];
        WeaponDef? gun = null;
        var pivot = Vector3.Zero;
        if (k < m.Parts.Length)
        {
            gun = def.Clone();
            gun.BuiltMesh = m.Slice(k, m.Parts.Length);
            var mn = new Vector3(float.MaxValue);
            var mx = new Vector3(float.MinValue);
            foreach (var w in gun.BuiltMesh.Matrices)
            {
                mn = Vector3.Min(mn, w.Translation);
                mx = Vector3.Max(mx, w.Translation);
            }
            pivot = (mn + mx) * 0.5f;
        }
        return _cartGunParts[def] = new CartGunParts(m, frame, gun, pivot);
    }

    /// <summary>The cart gun's pose in the cart's model space: turned <paramref name="aimYaw"/> (from the cart's facing) and tilted to <paramref name="aimPitch"/> about its middle.</summary>
    private static Matrix4x4 CartGunPose(CartGunParts parts, float aimYaw, float aimPitch) =>
        Matrix4x4.CreateTranslation(-parts.Pivot) * Matrix4x4.CreateRotationX(aimPitch) * Matrix4x4.CreateRotationY(-aimYaw) * Matrix4x4.CreateTranslation(parts.Pivot);

    /// <summary>Push carts standing in the world (yours when you aren't pushing it, everyone else's) and the slot upgrade station.</summary>
    private static void DrawZombiesWorld(double now)
    {
        foreach (var c in _game.Carts.Values)
        {
            if (c.Def == null)
                continue;
            var mine = c.Owner == _game.LocalId && _game.Local is { Alive: true };
            if (mine && c.Mode == CartMode.Pushed && Game.IsCart(_game.Active.Def))
                continue; // drawn with your hands on it (the viewmodel)
            var pos = c.Body.Position;
            if (c.Owner != _game.LocalId && _game.Players.TryGetValue(c.Owner, out var owner) && c.Mode == CartMode.Pushed)
                pos += owner.RenderPosition - owner.Position; // keep a pushed cart with its owner as they're drawn
            var world = c.Shape.World(pos, c.Body.Yaw);
            if (c.Mode == CartMode.Manned && CartParts(c.Def) is { } parts)
            {
                // Manned: the cart stands still and its gun turns to where its owner aims (yours follows your view directly).
                var (aimYaw, aimPitch) = (c.AimYaw, c.AimPitch);
                if (mine)
                {
                    var (y, p) = Game.ClampCartAim(c.Body.Yaw, _game.LookYaw, _game.LookPitch);
                    (aimYaw, aimPitch) = (World.WrapAngle(y - c.Body.Yaw), p);
                }
                DrawGun(parts.Frame, world, false);
                if (parts.Gun != null)
                    DrawGun(parts.Gun, CartGunPose(parts, aimYaw, aimPitch) * world, mine && now < _game.MuzzleFlashUntil);
                continue;
            }
            DrawGun(c.Def, world, false);
        }
        if (_game.SlotStation is { } st)
        {
            var unlocked = _game.Local?.ExtraSlots == true;
            var glow = unlocked ? new Vector3(0.3f, 0.9f, 0.4f) : new Vector3(1f, 0.75f, 0.2f) * (0.8f + 0.2f * MathF.Sin((float)now * 3f));
            Gfx.Draw(Matrix4x4.CreateScale(0.7f, 1.3f, 0.5f) * Matrix4x4.CreateTranslation(st + new Vector3(0, 0.65f, 0)), new Vector3(0.25f, 0.27f, 0.3f));
            Gfx.Draw(Matrix4x4.CreateScale(0.5f, 0.35f, 0.52f) * Matrix4x4.CreateTranslation(st + new Vector3(0, 1.0f, 0)), glow, 1f, mode: 2);
            Gfx.Draw(Matrix4x4.CreateScale(0.12f, 0.12f, 0.53f) * Matrix4x4.CreateTranslation(st + new Vector3(-0.15f, 0.55f, 0)), new Vector3(0.9f, 0.9f, 0.9f), 1f, mode: 2);
            Gfx.Draw(Matrix4x4.CreateScale(0.12f, 0.12f, 0.53f) * Matrix4x4.CreateTranslation(st + new Vector3(0.15f, 0.55f, 0)), new Vector3(0.9f, 0.9f, 0.9f), 1f, mode: 2);
        }
    }

    // ---- Smoke step (after the map select screen): the workbench with two builds, then the range with a minigun ----

    private static int _wbSmokeStage;
    private static double _wbSmokeStart;
    private static int _wbSmokeFrames;
    private static bool WorkbenchSmokeActive => _wbSmokeStage > 0;
    private static int _wbMeleeTries;
    private static double _wbMeleeAt, _wbShotAt;

    /// <summary>The smoke step's melee swing (the chainsaw sword), without the cart's toast over it.</summary>
    private static void SmokeMeleeSwing(Game? g, double now)
    {
        if (g == null)
            return;
        g.LoadoutNotice = "";
        g.TryMelee(now);
    }

    private static void StartWorkbenchSmoke(double now)
    {
        BuildStore.ReadOnly = true; // never write workbench.txt
        _wbSmokeStage = 1;
        _wbSmokeStart = now;
        Ui.OpenWorkbench();
        Console.WriteLine($"[smoke] workbench: {Parts.All.Length} parts, {Parts.LoadErrors.Count} part error(s)");
    }

    private static void WorkbenchSmokeTick(double now)
    {
        var t = now - _wbSmokeStart;
        var examples = BuilderExamples.All();
        WeaponDef? Def(string name) => examples.FirstOrDefault(e => e.Build.Name == name) is { } e ? WeaponBuilder.Assemble(e.Build).Def : null;
        var g = _session;
        switch (_wbSmokeStage)
        {
            case 1 when t > 1.0:
                SmokeShot("workbench");
                _wbSmokeStage = 2;
                break;
            case 2 when t > 1.3:
                if (examples.FirstOrDefault(e => e.Build.Name == "Lumberjack") is { } lj)
                    Ui.SmokeWorkbenchBuild(lj.Build);
                _wbSmokeStage = 3;
                break;
            case 3 when t > 2.2:
                SmokeShot("workbench_chainsaw");
                _wbSmokeStage = 31;
                break;
            case 31 when t > 2.5:
                if (BuilderExamples.Melee().FirstOrDefault(m => m.Build.Name == "Chainsaw Sword") is { Build: { } cs })
                    Ui.SmokeWorkbenchBuild(cs);
                _wbSmokeStage = 32;
                break;
            case 32 when t > 3.3:
                SmokeShot("workbench_melee");
                _wbSmokeStage = 4;
                break;
            case 4 when t > 3.6:
            {
                // The range: the Shopping Spree cart in hand, the minigun second, the triple pistol in slot 3 (unlocked),
                // the chainsaw sword on the melee key and four practice dummies down range.
                var cart = Def("Shopping Spree");
                var mg = Def("Minigun");
                var tp = Def("Triple Pistol");
                var sword = BuilderExamples.Melee().FirstOrDefault(m => m.Build.Name == "Chainsaw Sword") is { Build: { } sb } ? WeaponBuilder.Assemble(sb).Melee : null;
                if (cart == null || mg == null)
                {
                    Console.WriteLine("[smoke] workbench: example builds missing, range skipped");
                    _wbSmokeStage = 9;
                    break;
                }
                StartWorkbenchRange(new WorkbenchLaunch(cart, mg, tp, null, sword, ExtraSlots: true, Dummies: 4));
                _wbSmokeStage = 5;
                break;
            }
            case 5 when g is { Phase: MatchPhase.Playing, Local.Alive: true } || t > 18:
                _smokeButtons = Buttons.Forward; // push the cart down the range
                _wbSmokeFrames = _smokeFrames;
                _wbSmokeStart = now; // own clock from here
                _wbSmokeStage = 51;
                break;
            case 51 when t > 1.6:
            {
                _smokeButtons = Buttons.None;
                SmokeShot("range_cart");
                var c = g?.Local is { } me && g.Carts.TryGetValue(me.Id, out var cc) ? cc : null;
                Console.WriteLine($"[smoke] cart: {(c == null ? "none" : $"{c.Mode} at {c.Body.Position.X:0.0},{c.Body.Position.Z:0.0}, rolled {c.Body.Rolled:0.0} m, blocked {c.Body.Blocked}")}, "
                                  + $"{(_smokeFrames - _wbSmokeFrames) / 1.6:0} fps while pushing");
                _wbSmokeStage = 52;
                break;
            }
            case 52 when t > 1.9:
                g?.Interact(now); // park it (it doesn't fire on its own)
                _wbSmokeStage = 521;
                break;
            case 521 when t > 2.1:
                g?.Interact(now); // tap E at its bar: man its gun
                _wbSmokeFrames = _smokeFrames;
                _wbSmokeStage = 522;
                break;
            case 522 when t > 2.6:
                if (g != null)
                    g.LookYaw = World.WrapAngle(g.LookYaw + 0.35f); // swing the gun right a little: the cart stays put
                _smokeFireHeld = true; // fire it by hand at the dummies
                _wbSmokeStage = 53;
                break;
            case 53 when t > 3.9:
            {
                // Still manning it (the shot is saved after this frame is drawn, so stepping off waits for the next stage).
                SmokeShot("range_cart_manned");
                var c = g?.Local is { } me && g.Carts.TryGetValue(me.Id, out var cc) ? cc : null;
                Console.WriteLine($"[smoke] cart manned: {(c == null ? "none" : $"{c.Mode}, gun aim {c.AimYaw * 180 / MathF.PI:0} deg yaw, {c.AimPitch * 180 / MathF.PI:0} deg pitch")}, "
                                  + $"ammo {g?.Active.Ammo}/{g?.Active.Def.Mag}, {g?.Tracers.Count ?? 0} tracer(s), {(_smokeFrames - _wbSmokeFrames) / 1.8:0} fps");
                _wbSmokeStage = 531;
                break;
            }
            case 531 when t > 4.05:
                _smokeFireHeld = false;
                g?.Interact(now); // step off: back to your other weapon
                _wbSmokeStage = 54;
                break;
            // The melee shot: wait until you're off the cart with your weapon raised, swing, and save the frame mid-swing
            // (retrying if the swing was refused, e.g. while a step off still mantles you), so the built melee is in view.
            case 54 when t > 4.35 && (g == null || (g.MannedCart == null && !Game.IsCart(g.Active.Def) && now >= g.EquipUntil) || t > 6.0):
                SmokeMeleeSwing(g, now);
                _wbMeleeTries = 1;
                _wbMeleeAt = t;
                _wbSmokeStage = 55;
                break;
            case 55:
            {
                var p = g?.MeleeProgress(now) ?? -1f;
                if (p >= 0.3f || t > _wbMeleeAt + 2.5)
                {
                    SmokeShot("range_melee");
                    var mb = g?.Local?.MeleeBuild;
                    var inHand = g == null ? "nothing" : p >= 0 && mb != null ? $"{mb.View.Name} (melee build, swing {p:0.00})" : $"{g.Active.Def.Name} (gun{(p >= 0 ? $", standard melee swing {p:0.00}" : ", not swinging")})";
                    Console.WriteLine($"[smoke] melee: in hand {inHand} after {_wbMeleeTries} swing(s); build {mb?.ToString() ?? "standard"}"
                                      + $"{(g?.Local is { } who ? $", mantle {who.Sim.MantleTicks}, manned {g.MannedCart != null}" : "")}");
                    _wbShotAt = t;
                    _wbSmokeStage = 56;
                }
                else if (p < 0 && t > _wbMeleeAt + 0.25 * _wbMeleeTries && _wbMeleeTries < 8)
                {
                    SmokeMeleeSwing(g, now); // refused (or already over): swing again
                    _wbMeleeTries++;
                }
                break;
            }
            case 56 when t > _wbShotAt + 0.6:
                if (g != null)
                    for (var i = 0; i < g.WeaponSlots; i++)
                        if (g.Slots[i].Def.SpoolTime > 0)
                            g.SwitchTo(i, now);
                _smokeFireHeld = true; // spin the minigun up
                _wbSmokeFrames = _smokeFrames;
                _wbShotAt = t;
                _wbSmokeStage = 6;
                break;
            case 6 when t > _wbShotAt + 3.0:
            {
                SmokeShot("range_minigun");
                Console.WriteLine($"[smoke] workbench range: {(g?.Local?.Alive == true ? "alive" : "not spawned")}, {g?.WeaponSlots} slot(s), spool {g?.SpoolRate:0.00}, "
                                  + $"{g?.Tracers.Count ?? 0} tracer(s) live, {(_smokeFrames - _wbSmokeFrames) / (t - _wbShotAt):0} fps while firing");
                _wbShotAt = t;
                _wbSmokeStage = 7;
                break;
            }
            case 7 when t > _wbShotAt + 0.3:
                _smokeFireHeld = false;
                EndSession("");
                _returnToWorkbench = false;
                Ui.WorkbenchOpen = false;
                _wbSmokeStage = 9;
                break;
            case 9:
                _wbSmokeStage = 0;
                StartMoversSmoke(now); // the usual chain carries on
                break;
        }
    }
}
