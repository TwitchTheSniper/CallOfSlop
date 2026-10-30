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
        _returnToWorkbench = true;
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
                {
                    var gunPose = CartGunPose(parts, aimYaw, aimPitch) * world;
                    if (mine)
                        RememberViewGun(c.Def, gunPose, inWorld: true); // manning it: your tracers leave its muzzles (Program.Muzzle.cs)
                    GunModel.FlashBarrels = mine ? LocalFlashBarrels(now) : GunModel.AllBarrels;
                    DrawGun(parts.Gun, gunPose, mine && now < _game.MuzzleFlashUntil);
                    GunModel.FlashBarrels = GunModel.AllBarrels;
                }
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
}
