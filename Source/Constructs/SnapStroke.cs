using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The hold-to-paint rules of snappy building (board item dev-sandbox-paint-too-strong-pt01). One press places exactly one
/// part. A held press paints on further sockets only after <see cref="HoldSecs"/>, only on sockets that existed when the
/// press began (never on a part this stroke placed: its own socket sits under the crosshair), only on a socket the
/// crosshair newly reached, at most once per <see cref="MinGap"/>, only after the server confirmed the last placement, and
/// along the line set by the first two placements. Pure state: the caller feeds it the frame's facts.
/// </summary>
public sealed class SnapStroke
{
    public const double HoldSecs = 0.3, MinGap = 0.08, ConfirmSecs = 1.0;
    public const float LineTolerance = 0.5f;

    private bool _active;
    private double _start, _lastPlace;
    private int _body, _startCount;
    private long _lastKey = -1;
    private int _placed;
    private Vector3 _p0, _dir;
    private int _pendBody = -1, _pendCount;
    private double _pendUntil;

    /// <summary>Placements made in the current stroke (the press's included).</summary>
    public int Placed => _placed;

    public void Reset()
    {
        _active = false;
        _placed = 0;
        _lastKey = -1;
        _pendBody = -1;
        _dir = Vector3.Zero;
    }

    /// <summary>The fire button went down on this construct: parts at or above its current count are this stroke's.</summary>
    public void Press(int body, int partCount, double now)
    {
        Reset();
        _active = true;
        _start = now;
        _lastPlace = now;
        _body = body;
        _startCount = partCount;
    }

    /// <summary>A placement was sent (the press's or a paint): remembered for the repeat rules.</summary>
    public void Sent(int body, int newCount, double now, long key, Vector3 pos)
    {
        if (!_active)
            return;
        _lastKey = key;
        _lastPlace = now;
        _pendBody = body;
        _pendCount = newCount;
        _pendUntil = now + ConfirmSecs;
        if (_placed == 0)
            _p0 = pos;
        else if (_placed == 1)
        {
            var d = pos - _p0;
            _dir = d.LengthSquared() > 1e-4f ? Vector3.Normalize(d) : Vector3.Zero;
        }
        _placed++;
    }

    /// <summary>May the socket under the crosshair be painted this frame?</summary>
    public bool Want(double now, int body, int partCount, int part, long key, Vector3 pos)
    {
        if (!_active || body != _body || partCount < _startCount || part >= _startCount)
            return false; // (a part of this stroke: never act on what the stroke created)
        if (now - _start < HoldSecs || now - _lastPlace < MinGap || key == _lastKey)
            return false;
        if (_pendBody >= 0)
        {
            if (body == _pendBody && partCount >= _pendCount || now > _pendUntil)
                _pendBody = -1;
            else
                return false; // (the last placement is not confirmed yet)
        }
        if (_placed >= 2 && _dir != Vector3.Zero)
        {
            var off = pos - _p0;
            if ((off - _dir * Vector3.Dot(off, _dir)).Length() > LineTolerance)
                return false; // (off the line the first two placements set)
        }
        return true;
    }
}
