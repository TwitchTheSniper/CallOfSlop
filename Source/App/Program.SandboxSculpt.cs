using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One press of the ground drill on this PC: a held button repeats at the table's rate, and a fill never acts on what the same
/// stroke laid (the paint lesson, 2026-10-30): a new spot must clear every spot of this stroke by more than the scoop's radius
/// across the ground, so holding still places once and a sweep places a line. A dig has nothing of its own to act on: held
/// still, it bores on at the rate until the ground is out of reach.
/// </summary>
public sealed class SculptStroke
{
    /// <summary>The spots a stroke remembers (older ones fall off: at the rate, more than half a minute of sweeping).</summary>
    public const int MaxSpots = 128;

    private readonly Vector3[] _spots = new Vector3[MaxSpots];
    private int _count, _next;
    private bool _digging, _filling;
    private double _nextAt;

    /// <summary>Is a stroke running (the model's bit spins)?</summary>
    public bool Busy => _digging || _filling;

    public void End()
    {
        _digging = _filling = false;
        _count = _next = 0;
    }

    /// <summary>A frame with the buttons as they are: should a dig or a fill go now at <paramref name="at"/>? A stroke starts on the press and ends on the release.</summary>
    public bool Step(bool digDown, bool fillDown, bool haveSpot, Vector3 at, float radius, double repeat, double now, out bool fill)
    {
        fill = false;
        var filling = fillDown && !digDown; // (dig wins with both down)
        if (digDown != _digging || filling != _filling)
        {
            _digging = digDown;
            _filling = filling;
            _count = _next = 0;
        }
        if (!Busy || !haveSpot || now < _nextAt)
            return false;
        fill = _filling;
        if (fill)
        {
            var clear = radius * radius * 1.21f; // (1.1 x the radius: a heap's own shoulder is inside it)
            for (var k = 0; k < _count; k++)
            {
                var d = _spots[k] - at;
                if (d.X * d.X + d.Z * d.Z < clear)
                    return false;
            }
            _spots[_next] = at;
            _next = (_next + 1) % MaxSpots;
            _count = Math.Min(_count + 1, MaxSpots);
        }
        _nextAt = now + repeat;
        return true;
    }
}

public static partial class Program
{
    private static readonly SculptStroke _sculpt = new();
    internal static readonly SandboxSculptWheel SculptWheel = new();
    private static long _sculptHintKey = -1;

    internal static SculptStroke Sculpt => _sculpt;

    /// <summary>Any frame with the ground drill not out: the stroke ends.</summary>
    private static void SculptIdle() => _sculpt.End();

    /// <summary>The ground under the crosshair within the drill's reach, if nothing stands in front of it.</summary>
    internal static bool SculptSpot(Vector3 eye, Vector3 dir, float reach, bool aimed, in ConstructHit hit, out Vector3 at)
    {
        at = default;
        if (World.Map.TerrainCollision is not { } ground)
            return false;
        var t = ground.Raycast(eye, dir, reach);
        if (t >= reach || World.RaycastWorld(eye, dir, reach) < t - 0.02f || (aimed && hit.Distance < t))
            return false;
        at = eye + dir * t;
        return true;
    }

    /// <summary>The ground drill's frame (from SandboxInput, with the crosshair's ray).</summary>
    private static void SandboxSculptInput(Game g, Player me, Vector3 eye, Vector3 dir, bool aimed, in ConstructHit hit, double now)
    {
        var table = DiggingTable.Current;
        var inv = g.SandboxInventory(me.Id);
        var kind = SculptWheel.Place(inv);
        var have = SculptSpot(eye, dir, table.Reach, aimed, in hit, out var at);
        var dig = Down(GameAction.Fire) && !_swallowClick;
        var fillDown = Down(GameAction.Aim);
        if (_sculpt.Step(dig, fillDown, have, at, table.ScoopRadius, table.RepeatSeconds, now, out var fill))
            g.SandboxSculpt(fill ? DigKind.FillDirt + (byte)kind : DigKind.Scoop, at);
        else if ((dig || fillDown) && !have)
            g.SandboxLookText = "Aim at bare ground in reach";
        int dirt = inv.Materials[Game.MaterialOf(GroundKind.Dirt)], stone = inv.Materials[Game.MaterialOf(GroundKind.Stone)], sand = inv.Materials[Game.MaterialOf(GroundKind.Sand)];
        var key = ((long)dirt << 40) | ((long)stone << 26) | ((long)sand << 12) | ((long)kind << 2) | (inv.Infinite ? 1L : 0L);
        if (key != _sculptHintKey || g.SandboxSculptHint.Length == 0)
        {
            _sculptHintKey = key;
            g.SandboxSculptHint = $"LMB: dig. RMB: put back {DiggingTable.KindNames[(int)kind]}{(inv.Infinite ? " (free)" : "")} (wheel: pick). Carried: dirt {dirt}, stone {stone}, sand {sand}";
        }
    }
}
