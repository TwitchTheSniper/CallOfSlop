using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// Where a gun's muzzles are, in gun space (<see cref="GunModel"/>: origin at the grip, -Z towards the muzzle), and the
/// sum that puts a first-person muzzle into the world.
///
/// Shots are hitscan from the eye (the crosshair tells the truth; built guns move each barrel's ray sideways by its
/// offset, <see cref="Game.ShotRays"/>). What you SEE start at the muzzle is the tracer and the flash: both come from
/// these points, for the barrel that fired (<see cref="ShotRay.Barrel"/>), on the gun as it is drawn (your view model,
/// or another player's gun in their hands). See <see cref="Game.TracerStart"/>.
/// </summary>
public static class GunMuzzles
{
    private sealed class Entry
    {
        public uint Kit;
        public OpticKind Optic;
        public WeaponClass Class;
        public float Length, Caliber;
        public object? Model;
        public Vector3 Point;
    }

    private static readonly ConditionalWeakTable<WeaponDef, Entry> Cache = new();

    /// <summary>How many muzzles <paramref name="d"/> has (a built gun's barrels; 1 for every other gun).</summary>
    public static int Count(WeaponDef d) => d.BuiltMesh is { Muzzles.Length: > 0 } m ? m.Muzzles.Length : 1;

    /// <summary>
    /// The muzzle of barrel <paramref name="barrel"/> (<see cref="ShotRay.Barrel"/>, the index into
    /// <see cref="WeaponDef.MuzzlePoints"/>) in gun space: the end of that barrel, past any muzzle device on it. A gun
    /// with one muzzle gives it for any index: the same point the muzzle flash is drawn at.
    /// </summary>
    public static Vector3 Local(WeaponDef d, int barrel = 0)
    {
        if (d.BuiltMesh is { Muzzles.Length: > 0 } built)
            return built.Muzzles[Math.Clamp(barrel, 0, built.Muzzles.Length - 1)];
        if (Cache.TryGetValue(d, out var e) && e.Kit == d.Kit && e.Optic == d.Optic && e.Class == d.Class && e.Length == d.Length
            && e.Caliber == d.Caliber && ReferenceEquals(e.Model, (object?)d.BuiltMesh ?? d.Model))
            return e.Point;
        e = new Entry { Kit = d.Kit, Optic = d.Optic, Class = d.Class, Length = d.Length, Caliber = d.Caliber, Model = (object?)d.BuiltMesh ?? d.Model };
        var probe = new Probe();
        GunModel.Emit(d, probe, 0f); // the flash's spot: the end of the barrel, after the muzzle device (the same code that draws the gun)
        e.Point = probe.Muzzle;
        Cache.AddOrUpdate(d, e);
        return e.Point;
    }

    /// <summary>
    /// A point of the first-person view model (its camera space: -Z forward, drawn with the fixed
    /// <see cref="Sights.ViewModelFovDeg"/>) as the world point drawn at the same spot on screen, at the same depth,
    /// through a world camera with <paramref name="invView"/> (camera to world) and vertical field of view tan(fov / 2) =
    /// <paramref name="tanHalfWorldFov"/>. A tracer from there leaves the muzzle you see, at any zoom.
    /// </summary>
    public static Vector3 ViewToWorld(Vector3 cam, in Matrix4x4 invView, float tanHalfWorldFov)
    {
        var k = tanHalfWorldFov / Sights.TanHalfFov;
        return Vector3.Transform(new Vector3(cam.X * k, cam.Y * k, cam.Z), invView);
    }

    private sealed class Probe : GunModel.IPartSink
    {
        public Vector3 Muzzle;

        public void Part(in Matrix4x4 local, Vector3 color, float alpha, int mode, GunModel.PartGroup group, GunModel.EmitFlags flags, int shape)
        {
        }

        public void Flash(Vector3 muzzle, float size, Vector3 color, float alpha) => Muzzle = muzzle;
    }
}
