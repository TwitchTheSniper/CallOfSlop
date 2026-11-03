using System.Numerics;

namespace CallOfSlop.Space;

/// <summary>One far thing in a bubble's sky (a body or a ship), as the sky shader draws it (<see cref="SpaceSkyView"/>).</summary>
public struct SkyObject
{
    /// <summary>The way to it from the camera (unit, game axes).</summary>
    public Vector3 Dir;

    /// <summary>The way from it to the star (unit, game axes): which side of it is lit.</summary>
    public Vector3 SunDir;

    /// <summary>Its colour (linear RGB 0-1, the lit side).</summary>
    public Vector3 Color;

    /// <summary>How far (m) and how big it looks (angular radius, radians; 0 for a ship).</summary>
    public double Distance, AngularRadius;

    /// <summary>How much of the star's disc it sees (1: full sun, 0: in a shadow): eclipses.</summary>
    public float Light;

    /// <summary>How much of the face turned to the camera is lit (0 new, 1 full): what a point's brightness follows.</summary>
    public float Phase;

    /// <summary>The body's index in the system (-1 for a ship), or the ship's id.</summary>
    public int Body;
    public ushort ShipId;

    /// <summary>Game axes to the body's own turning axes (the columns are the game's x, y and z in the body's frame): its ground turns with its day.</summary>
    public Vector3 ToBodyX, ToBodyY, ToBodyZ;
}

/// <summary>
/// What a bubble's sky shows besides the sun (phase 4, docs/space/SPACE.md "Seeing space"): every other body of the system
/// (not the star, not the bubble's own body) as a sphere lit by the star from its own side, with its phase, its ground
/// turning with its day and eclipses (another body between it and the star dims it), and the nearest ships as points of
/// light while the sun is on them. Far things are drawn by the sky shader in the far layer behind the world, at their
/// directions and angular sizes, so they need no depth range. All of it is a pure function of the system, the clock and
/// the camera (the ships' states aside, which come from the host), so every peer works it out itself. No allocation.
/// </summary>
public sealed class SpaceSkyView
{
    /// <summary>The most bodies and ships a sky draws (the shader's uniform arrays).</summary>
    public const int MaxBodies = 8, MaxShips = 16;

    /// <summary>Bodies, farthest first (nearer ones are drawn over them), then ships, nearest first.</summary>
    public readonly SkyObject[] Bodies = new SkyObject[MaxBodies], Ships = new SkyObject[MaxShips];
    public int BodyCount, ShipCount;

    private FramePose _pose;
    private SpaceBubble? _bubble;
    private SpaceTime _t;
    private Vec3d _cam;
    private Vec3d _starGame;
    private double _starDist, _starAr;

    /// <summary>
    /// Works out the bodies for a time and camera (game coordinates), and clears the ships: add them after with
    /// <see cref="AddShip"/>.
    /// </summary>
    public void Update(SpaceBubble b, SpaceTime t, Vector3 camera)
    {
        _bubble = b;
        _t = t;
        _pose = b.PoseAt(t);
        _cam = Vec3d.From(camera);
        var sys = b.System;
        var star = sys.PositionOf(0, t);
        _starGame = _pose.FromEcliptic(star) - _cam;
        _starDist = _starGame.Length;
        _starAr = Math.Asin(Math.Min(1, sys.Bodies[0].Radius / _starDist));
        BodyCount = 0;
        ShipCount = 0;
        for (var k = 1; k < sys.Count; k++)
        {
            if (k == b.Body)
                continue;
            var pk = sys.PositionOf(k, t);
            var rel = _pose.FromEcliptic(pk) - _cam;
            var dist = rel.Length;
            if (!(dist > sys.Bodies[k].Radius))
                continue;
            var ar = Math.Asin(sys.Bodies[k].Radius / dist);
            var dir = rel / dist;
            // Behind the star (on the far side of its disc): hidden.
            if (dist > _starDist && Separation(dir, _starGame / _starDist) < _starAr + ar)
                continue;
            var sunEcl = star - pk;
            var sunDir = _pose.Rot.InverseRotate(sunEcl).Normalized();
            var o = new SkyObject
            {
                Dir = dir.ToVector3(),
                SunDir = sunDir.ToVector3(),
                Color = sys.Bodies[k].Color ?? DefaultColor(sys.Bodies[k].Name),
                Distance = dist,
                AngularRadius = ar,
                Light = (float)SunVisible(sys, t, pk, k),
                Phase = (float)((1 - Vec3d.Dot(sunDir, dir)) * 0.5),
                Body = k,
            };
            var fixedRot = sys.PoseOf(new FrameRef(FrameKind.BodyFixed, k), t).Rot;
            o.ToBodyX = fixedRot.InverseRotate(_pose.Rot.Rotate(Vec3d.UnitX)).ToVector3();
            o.ToBodyY = fixedRot.InverseRotate(_pose.Rot.Rotate(Vec3d.UnitY)).ToVector3();
            o.ToBodyZ = fixedRot.InverseRotate(_pose.Rot.Rotate(Vec3d.UnitZ)).ToVector3();
            Keep(o);
        }
        // Farthest first, so the shader draws nearer bodies over farther ones (insertion sort: at most 8).
        for (var i = 1; i < BodyCount; i++)
        {
            var x = Bodies[i];
            var j = i - 1;
            for (; j >= 0 && Bodies[j].Distance < x.Distance; j--)
                Bodies[j + 1] = Bodies[j];
            Bodies[j + 1] = x;
        }
    }

    /// <summary>Keeps the <see cref="MaxBodies"/> biggest-looking bodies.</summary>
    private void Keep(in SkyObject o)
    {
        if (BodyCount < MaxBodies)
        {
            Bodies[BodyCount++] = o;
            return;
        }
        var smallest = 0;
        for (var i = 1; i < BodyCount; i++)
            if (Bodies[i].AngularRadius < Bodies[smallest].AngularRadius)
                smallest = i;
        if (o.AngularRadius > Bodies[smallest].AngularRadius)
            Bodies[smallest] = o;
    }

    /// <summary>
    /// A ship at a system position (after <see cref="Update"/>): kept if it's among the <see cref="MaxShips"/> nearest and
    /// not behind a body's disc. False when not kept.
    /// </summary>
    public bool AddShip(ushort id, SpacePos pos)
    {
        if (_bubble is not { } b)
            return false;
        var sys = b.System;
        var rel = _pose.FromEcliptic(pos) - _cam;
        var dist = rel.Length;
        if (!(dist > 1))
            return false;
        var dir = rel / dist;
        // Inside or behind the bubble's own body: the ground hides it anyway; behind a far body's disc: hidden here.
        for (var i = 0; i < BodyCount; i++)
            if (Bodies[i].Distance < dist && Separation(dir, Vec3d.From(Bodies[i].Dir)) < Bodies[i].AngularRadius)
                return false;
        var slot = ShipCount;
        if (ShipCount == MaxShips)
        {
            if (dist >= Ships[MaxShips - 1].Distance)
                return false;
            slot = MaxShips - 1;
        }
        else
            ShipCount++;
        // Nearest first.
        while (slot > 0 && Ships[slot - 1].Distance > dist)
        {
            Ships[slot] = Ships[slot - 1];
            slot--;
        }
        var star = sys.PositionOf(0, _t);
        var sunDir = _pose.Rot.InverseRotate(star - pos).Normalized();
        Ships[slot] = new SkyObject
        {
            Dir = dir.ToVector3(),
            SunDir = sunDir.ToVector3(),
            Color = ShipColor,
            Distance = dist,
            Light = (float)SunVisible(sys, _t, pos, -1),
            Phase = (float)((1 - Vec3d.Dot(sunDir, dir)) * 0.5),
            Body = -1,
            ShipId = id,
        };
        return true;
    }

    /// <summary>The colour of a ship's glint.</summary>
    public static readonly Vector3 ShipColor = new(1f, 0.95f, 0.85f);

    /// <summary>The angle between two unit vectors (radians), exact for small angles too.</summary>
    public static double Separation(Vec3d a, Vec3d b) => 2 * Math.Atan2((a - b).Length, (a + b).Length);

    /// <summary>
    /// How much of the star's disc is seen from a point (1 all, 0 none): every body but the star and <paramref name="self"/>
    /// can cover it (the bubble's own body casts the night side's shadow on a moon, a moon covers the sun for a ship).
    /// </summary>
    public static double SunVisible(StarSystem sys, SpaceTime t, SpacePos at, int self)
    {
        var toStar = sys.PositionOf(0, t) - at;
        var ds = toStar.Length;
        if (!(ds > 0))
            return 1;
        var sunAr = Math.Asin(Math.Min(1, sys.Bodies[0].Radius / ds));
        var sDir = toStar / ds;
        var seen = 1.0;
        for (var o = 1; o < sys.Count; o++)
        {
            if (o == self)
                continue;
            var toO = sys.PositionOf(o, t) - at;
            var d = toO.Length;
            if (!(d > 0) || d > ds)
                continue;
            if (d <= sys.Bodies[o].Radius)
                return 0; // inside it
            var oAr = Math.Asin(sys.Bodies[o].Radius / d);
            seen *= 1 - DiscCover(sunAr, oAr, Separation(sDir, toO / d));
            if (seen <= 0)
                return 0;
        }
        return seen;
    }

    /// <summary>
    /// The share of a disc of angular radius <paramref name="r1"/> covered by one of radius <paramref name="r2"/> whose
    /// centre is <paramref name="sep"/> away (flat-sky circle overlap, fine for the small discs in a sky).
    /// </summary>
    public static double DiscCover(double r1, double r2, double sep)
    {
        if (r1 <= 0)
            return 0;
        if (sep >= r1 + r2)
            return 0;
        if (sep <= Math.Abs(r2 - r1))
            return r2 >= r1 ? 1 : r2 * r2 / (r1 * r1);
        var a1 = r1 * r1 * Math.Acos(Math.Clamp((sep * sep + r1 * r1 - r2 * r2) / (2 * sep * r1), -1, 1));
        var a2 = r2 * r2 * Math.Acos(Math.Clamp((sep * sep + r2 * r2 - r1 * r1) / (2 * sep * r2), -1, 1));
        var k = 0.5 * Math.Sqrt(Math.Max(0, (-sep + r1 + r2) * (sep + r1 - r2) * (sep - r1 + r2) * (sep + r1 + r2)));
        return Math.Clamp((a1 + a2 - k) / (Math.PI * r1 * r1), 0, 1);
    }

    private static readonly Vector3[] Rocks = [new(0.55f, 0.52f, 0.48f), new(0.6f, 0.5f, 0.4f), new(0.5f, 0.5f, 0.52f), new(0.58f, 0.46f, 0.36f)];

    /// <summary>A body with no "color": one of a few rock tones, picked from its name so it's the same on every peer.</summary>
    public static Vector3 DefaultColor(string name)
    {
        var h = 0u;
        foreach (var c in name)
            h = h * 31 + c;
        return Rocks[h % (uint)Rocks.Length];
    }
}
