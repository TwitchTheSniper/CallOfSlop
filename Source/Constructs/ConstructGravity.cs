using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Gravity-tech parts (phase 5, docs/space/GRAVITY.md "Gravity parts"): the <b>Gravity Well</b> pulls everything near it
/// (players, constructs, things that fall; a negative signal pushes) and, on a ship in space, the ships near it; the
/// <b>Gravity Generator</b> gives the box it fills its own "down" (the part's -Y), so a ship or a base can have a floor
/// wherever its builder wants one. Both run on the parts' outputs (<see cref="CPart.Out"/>, which clients hear) and the
/// constructs' poses, so host and clients work out the same field with nothing new on the wire.
/// <para>
/// <see cref="Refresh"/> gathers the working parts once a tick (the host after its construct step, a client after it blends
/// the poses); the queries then read flat arrays and never allocate. A part never pulls the construct it is on (no
/// pulling yourself along by your own bootstraps).
/// </para>
/// </summary>
public static class ConstructGravity
{
    public const int MaxParts = 64;

    /// <summary>The longest side a generator's box takes (m).</summary>
    public const float MaxSide = 24f;

    /// <summary>A well's strength is its pull at this distance (m); closer it rises (softened), farther it falls off to its range.</summary>
    public const float WellRefDistance = 4f;

    public const float WellSoft = 1f;

    // Wells: where, GM (strength x signal x ref^2), range, the construct they are on.
    private static readonly Vector3[] _wAt = new Vector3[MaxParts];
    private static readonly float[] _wMu = new float[MaxParts], _wRange = new float[MaxParts];
    private static readonly uint[] _wBody = new uint[MaxParts];
    private static readonly ushort[] _wShip = new ushort[MaxParts]; // (its construct's space ship id, 0: in the bubble)
    private static int _wells;

    // Generators: the box (centre, axes, half sizes), the pull inside, the construct.
    private static readonly Vector3[] _gAt = new Vector3[MaxParts], _gHalf = new Vector3[MaxParts], _gG = new Vector3[MaxParts];
    private static readonly Quaternion[] _gRot = new Quaternion[MaxParts];
    private static readonly uint[] _gBody = new uint[MaxParts];
    private static int _gens;

    public static int Wells => _wells;
    public static int Generators => _gens;

    /// <summary>The bubble's force hook for the wells (added to <see cref="GravityFrames"/> while any well works).</summary>
    public static readonly IGravityHook Hook = new WellHook();

    /// <summary>The generators' boxes as a gravity override (installed in <see cref="GravityFrames"/> while any generator works).</summary>
    public static readonly IGravityVolumes Volumes = new GenVolumes();

    /// <summary>Gathers the working gravity parts of a world (once a tick, host and clients).</summary>
    public static void Refresh(ConstructWorld world)
    {
        _wells = 0;
        _gens = 0;
        foreach (var c in world.Bodies)
        {
            if (c.GravityCount == 0)
                continue;
            for (var i = 0; i < c.PartCount && (_wells < MaxParts || _gens < MaxParts); i++)
            {
                ref var p = ref c.Parts[i];
                var d = p.D;
                if (d.Logic is not (CLogic.GravityWell or CLogic.GravityGen) || p.Broken || p.Out == 0)
                    continue;
                Freeform.PartWorld(c, i, out var pos, out var rot);
                var strength = ConstructConfig.Get(p, d, CfgKey.Strength, World.Gravity);
                if (d.Logic == CLogic.GravityWell && _wells < MaxParts)
                {
                    c.PartBounds(i, out var mn, out var mx);
                    c.WorldFrame(p.Seg, out var q, out var t);
                    _wAt[_wells] = t + Vector3.Transform((mn + mx) * 0.5f, q);
                    _wMu[_wells] = strength * Math.Clamp(p.Out, -1, 1) * WellRefDistance * WellRefDistance;
                    _wRange[_wells] = ConstructConfig.Get(p, d, CfgKey.Radius, 30);
                    _wBody[_wells] = c.Id;
                    _wShip[_wells] = c.SpaceShip;
                    _wells++;
                }
                else if (d.Logic == CLogic.GravityGen && _gens < MaxParts)
                {
                    ConstructCargo.Settings(p, d, out var size, out var at); // (the same box the configure tool shows and sizes)
                    _gAt[_gens] = pos + Vector3.Transform(at, rot);
                    _gRot[_gens] = rot;
                    _gHalf[_gens] = size * 0.5f;
                    _gG[_gens] = Vector3.Transform(-Vector3.UnitY, rot) * strength;
                    _gBody[_gens] = c.Id;
                    _gens++;
                }
            }
        }
        if (_wells > 0)
            GravityFrames.AddHook(Hook);
        else
            GravityFrames.RemoveHook(Hook);
        GravityFrames.UseVolumes(_gens > 0 ? Volumes : null);
    }

    /// <summary>Forgets every part (a new map or world).</summary>
    public static void Clear()
    {
        _wells = 0;
        _gens = 0;
        GravityFrames.RemoveHook(Hook);
        GravityFrames.UseVolumes(null);
    }

    /// <summary>The wells' pull at a point, leaving out those on construct <paramref name="skip"/> (0: none).</summary>
    public static Vector3 WellsAt(Vector3 p, uint skip)
    {
        var a = Vector3.Zero;
        for (var k = 0; k < _wells; k++)
        {
            if (_wBody[k] == skip || _wShip[k] != 0)
                continue; // (a well out in space pulls ships there, not the bubble)
            a += WellPull(p - _wAt[k], _wMu[k], _wRange[k]);
        }
        return a;
    }

    private static Vector3 WellPull(Vector3 d, float mu, float range)
    {
        var d2 = d.LengthSquared();
        if (d2 >= range * range)
            return Vector3.Zero;
        var r2 = d2 + WellSoft * WellSoft;
        var fade = 1f - d2 / (range * range);
        return d * (-mu * fade * fade / (r2 * MathF.Sqrt(r2)));
    }

    /// <summary>The generator box holding a point, or -1 (leaving out those on construct <paramref name="skip"/>).</summary>
    public static int GenAt(Vector3 p, uint skip)
    {
        for (var k = 0; k < _gens; k++)
        {
            if (_gBody[k] == skip)
                continue;
            var l = Vector3.Transform(p - _gAt[k], Quaternion.Conjugate(_gRot[k]));
            var h = _gHalf[k];
            if (MathF.Abs(l.X) <= h.X && MathF.Abs(l.Y) <= h.Y && MathF.Abs(l.Z) <= h.Z)
                return k;
        }
        return -1;
    }

    /// <summary>
    /// Gravity on construct <paramref name="c"/> at <paramref name="at"/>: a generator's box it is in (not its own), else the
    /// map's field, plus every well that isn't on it.
    /// </summary>
    public static Vector3 GravityFor(Construct c, Vector3 at)
    {
        var k = _gens > 0 ? GenAt(at, c.Id) : -1;
        var g = k >= 0 ? _gG[k] * World.GravityScale : (GravityFrames.Base is { } f ? f.GravityAt(at) : new Vector3(0, -World.Gravity, 0)) * World.GravityScale;
        return _wells > 0 ? g + WellsAt(at, c.Id) : g;
    }

    private sealed class WellHook : IGravityHook
    {
        public Vector3 AccelAt(Vector3 position) => WellsAt(position, 0);
    }

    private sealed class GenVolumes : IGravityVolumes
    {
        public bool TryAt(Vector3 p, out Vector3 gravity, out Vector3 up, out Vector3 axis)
        {
            var k = GenAt(p, 0);
            if (k < 0)
            {
                gravity = up = axis = default;
                return false;
            }
            gravity = _gG[k];
            var len = gravity.Length();
            up = len > 1e-6f ? -gravity / len : Vector3.Transform(Vector3.UnitY, _gRot[k]);
            axis = Vector3.Transform(-Vector3.UnitZ, _gRot[k]); // (the frame's north: the part's -Z)
            return true;
        }
    }

    // ---- Ships in space -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The wells on ships in space as a force on the other ships (<see cref="IShipForce"/>): each well sits on its own ship,
    /// moved on from where the ship was at the tick's start at its speed then, and never pulls the ship it is on.
    /// </summary>
    public sealed class SpaceWells(SpaceShips ships) : IShipForce
    {
        private readonly SpaceShips _ships = ships;
        private readonly int[] _ship = new int[MaxParts];
        private readonly SpacePos[] _at = new SpacePos[MaxParts];
        private readonly Vec3d[] _vel = new Vec3d[MaxParts];
        private readonly double[] _mu = new double[MaxParts], _range = new double[MaxParts];
        private long _ns;
        private int _n;

        public int Count => _n;

        /// <summary>The wells on ships in space, as they are now (before the ships step).</summary>
        public void Refresh()
        {
            _n = 0;
            _ns = _ships.Time.Ns;
            for (var k = 0; k < _wells; k++)
            {
                if (_wShip[k] == 0)
                    continue;
                var i = _ships.IndexOfId(_wShip[k]);
                if (i < 0)
                    continue;
                _ship[_n] = i;
                _at[_n] = _ships.PositionOf(i);
                _vel[_n] = _ships.VelocityOf(i);
                _mu[_n] = _wMu[k];
                _range[_n] = _wRange[k];
                _n++;
            }
        }

        /// <summary>Adds a well directly (tests): on ship <paramref name="ship"/>, as it is now.</summary>
        public void Add(int ship, double mu, double range)
        {
            if (_n >= MaxParts)
                return;
            _ns = _ships.Time.Ns;
            _ship[_n] = ship;
            _at[_n] = _ships.PositionOf(ship);
            _vel[_n] = _ships.VelocityOf(ship);
            _mu[_n] = mu;
            _range[_n] = range;
            _n++;
        }

        public Vec3d Accel(SpacePos pos, Vec3d vel, SpaceTime t) => AccelOn(-1, pos, vel, t);

        public Vec3d AccelOn(int ship, SpacePos pos, Vec3d vel, SpaceTime t)
        {
            var a = Vec3d.Zero;
            var dt = (t.Ns - _ns) / (double)SpaceTime.NsPerSecond;
            for (var k = 0; k < _n; k++)
            {
                if (_ship[k] == ship)
                    continue;
                var d = (pos - _at[k]) - _vel[k] * dt;
                var d2 = d.LengthSquared;
                var range = _range[k];
                if (d2 >= range * range)
                    continue;
                var r2 = d2 + WellSoft * WellSoft;
                var fade = 1 - d2 / (range * range);
                a -= d * (_mu[k] * fade * fade / (r2 * Math.Sqrt(r2)));
            }
            return a;
        }
    }
}
