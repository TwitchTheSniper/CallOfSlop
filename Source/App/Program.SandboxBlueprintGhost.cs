using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The blueprint ghost on this PC (GameSandboxBlueprintGhost.cs): while the host makes a spawn ready the look line says so; then its ghost
/// is carried on the surface under the crosshair (pushed out past the player's feet, at its carry distance when the crosshair is on
/// nothing), green where it can go and red with the reason where it can't. The wheel turns it 15 degrees a notch (Shift: 1), R sets it
/// facing away from you, Fire places it, Aim or Esc drops it. Opening a menu, dying, a seat or another tool drops it too. The host
/// checks the place again with the same function. Nothing here allocates while the ghost is carried (the look line is made again only
/// when its reason or the angle changes).
/// </summary>
public static unsafe partial class Program
{
    private static float _bgYaw;
    private static double _bgScroll;
    private static ushort _bgTokenSeen;
    private static bool _bgMenuWas, _bgSeen;
    private static bool _bgEat; // the click that placed or dropped the ghost: the gun doesn't see it (until the buttons are up)
    private static ConstructTool _bgTool;
    private static string _bgLook = "";
    private static long _bgLookKey = long.MinValue;
    private static string? _bgLookWhy;

    /// <summary>Every frame, before the menus' early return: a ghost out is dropped by a menu opening, death, a seat or another tool.</summary>
    private static void BlueprintGhostWatch(Game g)
    {
        ConstructRender.BpGhostOn = false;
        if (_bgEat && !Down(GameAction.Fire) && !Down(GameAction.Aim))
            _bgEat = false;
        var menus = MenusOpen;
        var menuNew = menus && !_bgMenuWas;
        _bgMenuWas = menus;
        if (g.GhostState == BlueprintGhostState.None)
        {
            _bgSeen = false;
            return;
        }
        if (g.Local is not { Alive: true } me)
        {
            g.SpawnCancelSend();
            return;
        }
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (g.GhostState == BlueprintGhostState.Preparing)
        {
            if (System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency - g.GhostSince > Game.HeldSeconds)
                g.SpawnCancelSend(); // (the host never answered)
            return;
        }
        if (!_bgSeen || g.GhostToken != _bgTokenSeen)
        {
            // A new ghost: facing away from the player, as the direct spawn faces; the hands as they are now.
            _bgSeen = true;
            _bgTokenSeen = g.GhostToken;
            _bgYaw = me.Yaw;
            _bgTool = h.Tool;
            _bgScroll = 0;
            return;
        }
        if (menuNew || h.Tool != _bgTool || h.Seated)
        {
            g.SpawnCancelSend();
            g.SandboxSay($"the {g.GhostName} was put away");
        }
    }

    /// <summary>The wheel while a ghost is out: it turns the ghost.</summary>
    private static bool BlueprintGhostScroll(double y)
    {
        if (_game is not { SandboxOn: true, GhostState: BlueprintGhostState.Carrying })
            return false;
        _bgScroll += y;
        return true;
    }

    /// <summary>Esc while a ghost is out (or being made): it goes, and the key is used up.</summary>
    private static bool BlueprintGhostEsc()
    {
        if (_game is not { SandboxOn: true } g || g.GhostState == BlueprintGhostState.None)
            return false;
        g.SpawnCancelSend();
        return true;
    }

    /// <summary>While a ghost is out: its pose, its check, the keys. Returns true when it took the input this frame.</summary>
    private static bool SandboxBlueprintGhostInput(Game g, Player me)
    {
        if (g.GhostState == BlueprintGhostState.Preparing)
        {
            var key = -1L - g.GhostName.Length;
            if (key != _bgLookKey || !ReferenceEquals(_bgLookWhy, g.GhostName))
            {
                _bgLookKey = key;
                _bgLookWhy = g.GhostName;
                _bgLook = $"Preparing {g.GhostName}... - Esc: cancel";
            }
            g.SandboxLookText = _bgLook;
            return false; // (the hands work on while it is made)
        }
        if (g.GhostState != BlueprintGhostState.Carrying || !_bgSeen)
            return false;
        // Turning: the wheel (15 degrees a notch, Shift 1), R facing away from you.
        if (Math.Abs(_bgScroll) >= 0.5)
        {
            var notches = (int)Math.Round(_bgScroll);
            _bgScroll = 0;
            var step = ShiftHeld ? 1f : 15f;
            _bgYaw += notches * step * MathF.PI / 180f;
            _bgYaw = MathF.IEEERemainder(_bgYaw, MathF.Tau);
            BuildSounds.Play(BuildSound.Rotate, default);
        }
        if (Pressed(GameAction.Reload))
        {
            _bgYaw = me.Yaw;
            BuildSounds.Play(BuildSound.Rotate, default);
        }
        // Where: the surface under the crosshair (terrain or a construct), pushed out past the player's feet; on nothing, the ground at its carry distance.
        var radius = Game.GhostRadius(g.GhostMin, g.GhostMax);
        var reach = Game.GhostReach(radius);
        SandboxAimRay(g, me, out var eye, out var dir);
        var wall = World.RaycastWorld(eye, dir, reach);
        var found = wall < reach;
        var at = eye + dir * MathF.Min(wall, reach);
        if (g.Constructs.Raycast(eye, dir, MathF.Min(wall, reach), out var hit))
        {
            at = hit.Point;
            found = true;
        }
        var fwd = GravityFrames.FlatForward(me.Position, me.Yaw);
        var up = GravityFrames.UpAt(me.Position);
        var off = at - me.Position;
        var flat = (off - up * Vector3.Dot(off, up)).Length();
        var near = radius + 1.5f;
        if (!found)
            at = ConstructWorld.Ground(World.Query, me.Position + fwd * MathF.Max(near, Game.GhostCarry(radius)));
        else if (flat < near)
            at = ConstructWorld.Ground(World.Query, me.Position + fwd * near);
        Game.BlueprintGhostPose(at, _bgYaw, g.GhostMin, g.GhostMax, g.GhostLow, out _, out var rot, out var bodyPos);
        var why = g.BlueprintGhostRefusal(me.Id, me.Eye, at, rot, bodyPos, g.GhostMin, g.GhostMax);
        ConstructRender.BpGhostOn = true;
        ConstructRender.BpGhostValid = why == null;
        ConstructRender.BpGhostBody = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(bodyPos);
        ConstructRender.BpGhostMin = g.GhostMin;
        ConstructRender.BpGhostMax = g.GhostMax;
        ConstructRender.BpGhostBoxes = g.GhostBoxes;
        ConstructRender.BpGhostColours = g.GhostColours;
        ConstructRender.BpGhostAlpha = g.GhostAlpha;
        ConstructRender.BpGhostShapes = g.GhostShapes;
        ConstructRender.BpGhostCount = g.GhostCount;
        // The look line: the controls and the angle (a compass heading, so it holds still while you look round).
        var deg = ((int)MathF.Round(_bgYaw * 180f / MathF.PI) % 360 + 360) % 360;
        var lookKey = deg + ((long)g.GhostToken << 16) + ((long)_settings.Bind(GameAction.Reload) << 32);
        if (lookKey != _bgLookKey || !ReferenceEquals(why, _bgLookWhy))
        {
            _bgLookKey = lookKey;
            _bgLookWhy = why;
            var keys = $"{SandboxKeys.Fire}: place, wheel: turn ({deg} deg), {Keybinds.NameOf(_settings, GameAction.Reload)}: reset, {SandboxKeys.Aim}/Esc: cancel";
            _bgLook = why == null ? $"{g.GhostName} - {keys}" : $"{g.GhostName} - can't place: {why} - {keys}";
        }
        g.SandboxLookText = _bgLook;
        if (Pressed(GameAction.Aim))
        {
            _bgEat = true;
            g.SpawnCancelSend();
            return true;
        }
        if (Pressed(GameAction.Fire) && !_swallowClick)
        {
            if (why != null)
            {
                g.SandboxSay(why);
                BuildSounds.Play(BuildSound.Refuse, default);
            }
            else
                g.SpawnPlaceSend(at, _bgYaw);
            _bgEat = true;
        }
        return true;
    }
}
