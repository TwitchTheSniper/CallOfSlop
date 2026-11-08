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
    public bool Step(bool digDown, bool fillDown, bool haveSpot, Vector3 at, float radius, double repeat, double now, out bool fill) =>
        Step(digDown, fillDown, haveSpot, at, radius, repeat, now, Vector3.UnitY, out fill);

    /// <summary><see cref="Step(bool, bool, bool, Vector3, float, double, double, out bool)"/> with "up" at the spot (a planetoid's away from its centre): the spacing is measured across the ground.</summary>
    public bool Step(bool digDown, bool fillDown, bool haveSpot, Vector3 at, float radius, double repeat, double now, Vector3 up, out bool fill)
    {
        fill = false;
        var filling = fillDown && !digDown; // (dig wins with both down)
        if (digDown != _digging || filling != _filling)
        {
            _digging = digDown;
            _filling = filling;
            _count = _next = 0;
        }
        if (_nextAt - now > repeat)
            _nextAt = now; // (another game's clock)
        if (!Busy || !haveSpot || now < _nextAt)
            return false;
        fill = _filling;
        if (fill)
        {
            var clear = radius * radius * 1.21f; // (1.1 x the radius: a heap's own shoulder is inside it)
            for (var k = 0; k < _count; k++)
            {
                var d = _spots[k] - at;
                d -= up * Vector3.Dot(d, up);
                if (d.LengthSquared() < clear)
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

    /// <summary>Any frame with the ground drill not out: the stroke ends, a slope's first point is let go.</summary>
    private static void SculptIdle()
    {
        _sculpt.End();
        _slopeHave = false;
        ConnectorRender.ShapeOn = false;
    }

    // Fill slope's first point (Program.SandboxSculpt.cs); Flatten's level for this stroke: a height on flat ground, a distance from the planetoid's centre on one.
    private static bool _slopeHave, _flatHave, _flatWasDown;
    private static Vector3 _slopeA;
    private static float _flatLevel;
    private static SandboxSculptWheel.DrillMode _sculptMode;

    /// <summary>A point's height as shaping sees it: its Y, or on a planetoid its distance from the centre.</summary>
    internal static float ShapeHeight(Vector3 p) => World.Map.Planetoid is { } pl ? Vector3.Distance(p, pl.Center) : p.Y;

    /// <summary>The point over (or under) <paramref name="spot"/> at shaping height <paramref name="h"/>.</summary>
    internal static Vector3 AtShapeHeight(Vector3 spot, float h)
    {
        if (World.Map.Planetoid is not { } pl)
            return spot with { Y = h };
        var d = spot - pl.Center;
        return d.LengthSquared() > 1e-6f ? pl.Center + Vector3.Normalize(d) * h : spot;
    }

    /// <summary>The ground under the crosshair within the drill's reach, if nothing stands in front of it.</summary>
    internal static bool SculptSpot(Vector3 eye, Vector3 dir, float reach, bool aimed, in ConstructHit hit, out Vector3 at)
    {
        at = default;
        float t;
        if (World.Map.TerrainCollision is { } ground)
            t = ground.Raycast(eye, dir, reach);
        else if (World.Map.Planetoid is { } planet)
            t = planet.Raycast(eye, dir, reach, out _); // a planetoid's ground (dug or not: PlanetEdits.cs)
        else
            return false;
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
        var mode = SculptWheel.Mode;
        if (mode != _sculptMode)
        {
            _sculptMode = mode;
            _sculpt.End();
            _slopeHave = _flatHave = false; // (a new mode starts clean)
        }
        ConnectorRender.ShapeOn = false;
        var have = SculptSpot(eye, dir, table.Reach, aimed, in hit, out var at);
        var lmb = Down(GameAction.Fire) && !_swallowClick;
        var rmb = Down(GameAction.Aim);
        var up = have ? GravityFrames.UpAt(at) : GravityFrames.UpAt(eye);
        var slopeLen = 0f;
        var slopeRise = 0f;
        var why = "";
        switch (mode)
        {
            case SandboxSculptWheel.DrillMode.Flatten:
            {
                var down = lmb || rmb;
                if (!down)
                    _flatHave = false;
                else if (!_flatWasDown || !_flatHave)
                {
                    // The level is taken when the stroke starts: the aimed ground (LMB) or the feet (RMB).
                    if (rmb && !lmb)
                        (_flatLevel, _flatHave) = (ShapeHeight(me.Position), true);
                    else if (have)
                        (_flatLevel, _flatHave) = (ShapeHeight(at), true);
                }
                _flatWasDown = down;
                if (have)
                {
                    var spot = AtShapeHeight(at, _flatHave ? _flatLevel : ShapeHeight(at));
                    ShapePreview(spot, spot, table.ShapeRadius, up, true);
                    if (_flatHave && _sculpt.Step(false, down, true, at, table.ShapeRadius * 0.6f, table.RepeatSeconds, now, up, out _))
                        g.SandboxSculpt(GroundShape.Grade(kind), spot, spot);
                }
                else if (down)
                    why = "Aim at bare ground in reach";
                break;
            }
            case SandboxSculptWheel.DrillMode.Clean:
                if (have)
                {
                    ShapePreview(at, at, table.CleanRadius, up, true);
                    if (_sculpt.Step(false, lmb, true, at, table.CleanRadius * 0.6f, table.RepeatSeconds, now, up, out _))
                        g.SandboxSculpt(GroundShape.Smooth(kind), at);
                }
                else if (lmb)
                    why = "Aim at bare ground in reach";
                break;
            case SandboxSculptWheel.DrillMode.Slope:
            {
                var click = Pressed(GameAction.Fire) && !_swallowClick;
                if (Pressed(GameAction.Aim))
                    _slopeHave = false;
                if (!_slopeHave)
                {
                    if (have)
                        ShapePreview(at, at, table.ShapeRadius, up, true);
                    if (click && have)
                        (_slopeA, _slopeHave) = (at, true);
                    else if (click)
                        why = "Aim at bare ground in reach";
                    break;
                }
                var b = have ? at : eye + dir * table.Reach;
                slopeLen = Vector3.Distance(_slopeA, b);
                slopeRise = ShapeHeight(b) - ShapeHeight(_slopeA);
                var ok = have && slopeLen <= GroundShape.MaxLength;
                if (slopeLen > GroundShape.MaxLength)
                    why = $"too long: a slope is at most {GroundShape.MaxLength:0} m";
                ShapePreview(_slopeA, b, table.ShapeRadius, up, ok);
                if (click)
                {
                    if (ok)
                    {
                        g.SandboxSculpt(GroundShape.Grade(kind), _slopeA, at);
                        _slopeHave = false;
                    }
                    else if (why.Length == 0)
                        why = "Aim at bare ground in reach";
                }
                break;
            }
            default:
                if (_sculpt.Step(lmb, rmb, have, at, table.ScoopRadius, table.RepeatSeconds, now, up, out var fill))
                    g.SandboxSculpt(fill ? DigKind.FillDirt + (byte)kind : DigKind.Scoop, at);
                else if ((lmb || rmb) && !have)
                    why = "Aim at bare ground in reach";
                break;
        }
        if (why.Length > 0)
            g.SandboxLookText = why;
        int dirt = inv.Materials[Game.MaterialOf(GroundKind.Dirt)], stone = inv.Materials[Game.MaterialOf(GroundKind.Stone)], sand = inv.Materials[Game.MaterialOf(GroundKind.Sand)];
        var key = ((long)dirt << 40) | ((long)stone << 26) | ((long)sand << 12) | ((long)kind << 2) | (inv.Infinite ? 1L : 0L);
        key = key * 31 + ((long)mode << 1) + (_slopeHave ? 1 : 0);
        key = key * 31 + (long)MathF.Round(slopeLen * 10) * 4099 + (long)MathF.Round(slopeRise * 10);
        if (key != _sculptHintKey || g.SandboxSculptHint.Length == 0)
        {
            _sculptHintKey = key;
            var name = DiggingTable.KindNames[(int)kind];
            var what = mode switch
            {
                SandboxSculptWheel.DrillMode.Flatten => $"{SandboxKeys.Fire}: flatten to the height where you start, {SandboxKeys.Aim}: flatten to your feet, fills with {name}",
                SandboxSculptWheel.DrillMode.Clean => $"{SandboxKeys.Fire}: fill holes and dig marks with {name}",
                SandboxSculptWheel.DrillMode.Slope when _slopeHave => $"Slope {slopeLen:0.0} m, rises {slopeRise:0.0} m: {SandboxKeys.Fire}: the other end, {SandboxKeys.Aim}: cancel, fills with {name}",
                SandboxSculptWheel.DrillMode.Slope => $"{SandboxKeys.Fire}: the slope's first end, fills with {name}",
                _ => $"{SandboxKeys.Fire}: dig, {SandboxKeys.Aim}: put back {name}",
            };
            g.SandboxSculptHint = $"{what} (wheel: modes and material), carrying dirt {dirt}, stone {stone}, sand {sand}";
        }
    }

    /// <summary>The outline of what a shaping stroke changes: a ring at each end and the ramp's sides (ConnectorRender's overlay).</summary>
    private static void ShapePreview(Vector3 a, Vector3 b, float radius, Vector3 up, bool ok)
    {
        ConnectorRender.ShapeOn = true;
        ConnectorRender.ShapeOk = ok;
        ConnectorRender.ShapeA = a;
        ConnectorRender.ShapeB = b;
        ConnectorRender.ShapeRadius = radius;
        ConnectorRender.ShapeUp = up;
    }
}
