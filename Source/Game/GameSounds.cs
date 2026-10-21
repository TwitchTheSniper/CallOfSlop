using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Game-side sound logic: footsteps for every player (Source/Audio/Footsteps.cs), the reverb per map, hit
/// confirmations for the shooter, and what each tracer sounds like to everyone else (flesh and headshot hits in
/// the world, near-miss whizzes and cracks, impacts by surface and ricochets).
/// </summary>
public sealed partial class Game
{
    /// <summary>Shot flag (Msg.Shot): the shot hit a turret or drone.</summary>
    public const byte ShotHitMachine = 8;

    /// <summary>Shot flag (Msg.Shot): the shot damaged a player.</summary>
    public const byte ShotHitPlayer = 16;

    private readonly Footsteps _footsteps = new();
    private Action<StepEvent>? _emitStep;
    private Map? _soundMap;
    private readonly double[] _fleshSoundAt = new double[256];
    private readonly Random _soundRng = new(4242);
    private int _impactsThisFrame;
    private double _soundNow;

    /// <summary>Per frame, after the render states: the map's reverb, footsteps, audio stats.</summary>
    private void UpdateSounds(double now)
    {
        _soundNow = now;
        _impactsThisFrame = 0;
        if (World.Map != _soundMap)
        {
            _soundMap = World.Map;
            Audio.SetEnvironment(World.Map);
            _footsteps.Reset();
        }
        Audio.Tick(now);
        if (!Audio.Enabled)
            return; // headless (self-test, no device): no footstep rays at all
        _emitStep ??= e => Audio.Footstep(e, e.Player == LocalId);
        foreach (var p in Players.Values)
            _footsteps.Update(p, now, _emitStep, LegAnimator.PhaseOf(p.Id, now));
    }

    /// <summary>The shooter's own hit confirmation: kills, headshot crunch, body thwack, metal ping.</summary>
    private void PlayHitConfirm(byte flags)
    {
        var head = (flags & 2) != 0;
        var machineOnly = (flags & ShotHitMachine) != 0 && (flags & ShotHitPlayer) == 0;
        if ((flags & 4) != 0)
        {
            Audio.Effect(Sfx.Kill, 0.7f);
            if (head)
            {
                Audio.Effect(Sfx.Headshot, 0.8f);
                Audio.Effect(Sfx.KillHeadshot, 0.55f);
            }
            else
                Audio.Effect(machineOnly ? Sfx.HitMetal : Sfx.HitBody, 0.45f);
        }
        else if (head)
            Audio.Effect(Sfx.Headshot, 0.75f);
        else if (machineOnly)
            Audio.Effect(Sfx.HitMetal, 0.55f);
        else
            Audio.Effect(Sfx.HitBody, 0.6f);
    }

    /// <summary>
    /// What a tracer from <paramref name="from"/> to <paramref name="to"/> sounds like: a hit on a player is a flesh or
    /// headshot sound in the world for everyone but the shooter; a near miss whizzes (or cracks, for rifles) past the
    /// listener; anything else is an impact on the surface it hit, sometimes with a ricochet off hard ones.
    /// <paramref name="hitFlags"/> is the shot's Msg.Shot flags, or -1 when unknown (local prediction).
    /// </summary>
    private void TracerSounds(Vector3 from, Vector3 to, WeaponDef def, int hitFlags)
    {
        if (!Audio.Enabled)
            return;
        var listener = Audio.ListenerPosition;
        var own = Local is { } me && Vector3.DistanceSquared(from, me.RenderEye) < 4f;

        // ---- On a player? ----
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var box = World.Hitbox(p.RenderPosition, p.RenderStance, p.RenderYaw);
            var grown = new Box(box.Center, box.Half + new Vector3(0.25f));
            if (to.X < grown.Min.X || to.Y < grown.Min.Y || to.Z < grown.Min.Z || to.X > grown.Max.X || to.Y > grown.Max.Y || to.Z > grown.Max.Z)
                continue;
            if (own || hitFlags == 0 || (hitFlags > 0 && (hitFlags & 1) == 0) || _soundNow - _fleshSoundAt[p.Id] < 0.06)
                return; // the shooter hears their own 2D confirmation; one flesh sound per victim per burst of pellets
            _fleshSoundAt[p.Id] = _soundNow;
            var head = World.IsHeadshot(p.RenderPosition, p.RenderStance, p.RenderYaw, to) || (hitFlags > 0 && (hitFlags & 2) != 0 && to.Y > box.Max.Y - 0.45f);
            Audio.Effect3D(head ? Sfx.Headshot3D : Sfx.FleshHit3D, to, head ? 0.75f : 0.55f);
            return;
        }

        // ---- Near miss: the closest point of the tracer to the listener ----
        if (!own)
        {
            var seg = to - from;
            var len2 = seg.LengthSquared();
            var t = len2 > 1e-6f ? Math.Clamp(Vector3.Dot(listener - from, seg) / len2, 0f, 1f) : 0f;
            var closest = from + seg * t;
            var miss = Vector3.Distance(closest, listener);
            if (miss < 3f && t > 0.02f && t < 0.98f)
            {
                var supersonic = def.Sound is SoundStyle.Gun or SoundStyle.Boom && !def.Suppressor
                                 && def.Class is WeaponClass.AssaultRifle or WeaponClass.DMR or WeaponClass.Sniper or WeaponClass.LMG;
                Audio.Effect3DSmall(supersonic ? Sfx.Crack : Sfx.Whizz, closest, 0.9f * (1f - miss / 3f) + 0.15f, 10f);
            }
        }

        // ---- Impact on the world ----
        if (_impactsThisFrame >= 3 || Vector3.DistanceSquared(to, listener) > 45f * 45f)
            return;
        var dir = to - from;
        var dist = dir.Length();
        if (dist < 0.5f || dist > World.Range - 1f)
            return; // into the sky
        dir /= dist;
        if (!World.Query.Raycast(to - dir * 0.3f, dir, 0.6f, QueryFilter.All, out var hit))
            return;
        _impactsThisFrame++;
        var surface = SurfaceProbe.OfShape(World.Map, hit.Shape, hit.Point);
        var impact = surface switch
        {
            FootSurface.Metal => Sfx.ImpactMetal,
            FootSurface.Wood => Sfx.ImpactWood,
            FootSurface.Concrete or FootSurface.Rock => Sfx.ImpactConcrete,
            _ => Sfx.ImpactDirt,
        };
        Audio.Effect3DSmall(impact, to, own ? 0.35f : 0.45f);
        var ricochet = surface switch
        {
            FootSurface.Metal => 0.35,
            FootSurface.Concrete or FootSurface.Rock => 0.2,
            _ => 0,
        };
        if (def.Sound is SoundStyle.Gun or SoundStyle.Boom && _soundRng.NextDouble() < ricochet)
            Audio.Effect3DSmall(Sfx.Ricochet, to, 0.4f, 60f);
    }
}
