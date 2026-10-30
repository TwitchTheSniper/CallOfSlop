using System.Numerics;

namespace CallOfSlop;

/// <summary>What a foot (or a bullet) lands on: picks the footstep and impact sounds.</summary>
public enum FootSurface : byte
{
    Concrete,
    Metal,
    Wood,
    Grass,
    Dirt,
    Sand,
    Rock,
    Snow,
    Count,
}

/// <summary>How a step is taken: decides loudness, weight and how far it carries.</summary>
public enum StepKind : byte
{
    Walk,
    Run,
    Crouch,
    Crawl, // prone: a scuffle, not a step
    Land, // touching down after a fall or jump (Strength = how hard)
    Jump, // the push-off
}

/// <summary>One footstep-type event for the audio (see <see cref="Footsteps"/>).</summary>
public readonly record struct StepEvent(byte Player, StepKind Kind, FootSurface Surface, Vector3 Position, float Strength);

/// <summary>
/// Works out what is under a foot: the terrain's material where it was painted (grass, dirt, rock, sand; very pale
/// "sand" on snowy maps is snow), a map box's texture (concrete, brick, tiles, wood, metal, earthy ground), a prop's
/// parts (wood or metal), and movers (always metal). One short ray straight down.
/// </summary>
public static class SurfaceProbe
{
    private static Map? _propMap;
    private static int _propCount = -1;
    private static int[] _propOfSolid = []; // Map.Solid index -> PropInstances index (-1 = a map box)
    private static FootSurface[] _propSurface = [];

    /// <summary>The surface under <paramref name="foot"/> (a point at or a little above the ground).</summary>
    public static FootSurface Under(Vector3 foot) => Under(World.Map, World.Query, foot);

    /// <summary>The surface under <paramref name="foot"/> in a given map and physics world.</summary>
    public static FootSurface Under(Map map, IWorldQuery query, Vector3 foot)
    {
        var origin = foot + new Vector3(0, 0.45f, 0);
        if (query.Raycast(origin, -Vector3.UnitY, 1.4f, QueryFilter.All, out var hit))
            return OfShape(map, hit.Shape, hit.Point);
        // Nothing within reach (an edge, a jump): the terrain under us, else the map's floor.
        return map.Terrain is { } t ? OfTerrain(t, foot.X, foot.Z) : FootSurface.Concrete;
    }

    /// <summary>The surface of a shape a ray hit at <paramref name="point"/>.</summary>
    public static FootSurface OfShape(Map map, ShapeId shape, Vector3 point)
    {
        switch (shape.Kind)
        {
            case ShapeKind.Terrain:
                return map.Terrain is { } t ? OfTerrain(t, point.X, point.Z) : FootSurface.Dirt;
            case ShapeKind.Mover or ShapeKind.MoverSweep:
                return FootSurface.Metal;
            case ShapeKind.Box or ShapeKind.Prop:
            {
                var i = shape.Index;
                if ((uint)i >= (uint)map.Solid.Count)
                    return FootSurface.Concrete;
                if (map.IsPropCollider(i) && PropSurface(map, i) is { } ps)
                    return ps;
                return OfMaterial(map.Materials[i], map.Colors[i]);
            }
            default:
                return FootSurface.Concrete; // static meshes and oriented boxes: masonry
        }
    }

    /// <summary>A box texture as a footstep surface (the box colour separates grassy hills from bare earth).</summary>
    public static FootSurface OfMaterial(Material m, Vector3 color) => m switch
    {
        Material.Wood => FootSurface.Wood,
        Material.Metal => FootSurface.Metal,
        Material.Dirt => color.Y > color.X + 0.04f && color.Y > color.Z + 0.04f ? FootSurface.Grass
            : color.X > color.Z + 0.12f && color.Y > color.Z + 0.04f && color.X > 0.55f ? FootSurface.Sand
            : FootSurface.Dirt,
        _ => FootSurface.Concrete, // concrete, brick, tiles, the prototype grid
    };

    /// <summary>The terrain surface at a point: the nearest sample's material, rock where it's drawn as cliff, snow for pale ground.</summary>
    public static FootSurface OfTerrain(TerrainData t, float x, float z)
    {
        var i = Math.Clamp((int)MathF.Round((x - t.OriginX) / t.Cell), 0, t.CountX - 1);
        var j = Math.Clamp((int)MathF.Round((z - t.OriginZ) / t.Cell), 0, t.CountZ - 1);
        var (_, rockiness) = t.VertexColor(i, j);
        var mat = (TerrainMaterial)t.MaterialIds[j * t.CountX + i];
        if (rockiness > 0.5f)
            mat = TerrainMaterial.Rock;
        var c = t.Surface.ColorOf(mat);
        if (mat != TerrainMaterial.Rock && IsSnowy(c))
            return FootSurface.Snow;
        return mat switch
        {
            TerrainMaterial.Rock => FootSurface.Rock,
            TerrainMaterial.Dirt => FootSurface.Dirt,
            TerrainMaterial.Sand => FootSurface.Sand,
            _ => FootSurface.Grass,
        };
    }

    /// <summary>Bright, nearly colourless ground reads as snow (the maps paint snow with the "sand" colour).</summary>
    public static bool IsSnowy(Vector3 c) => MathF.Min(c.X, MathF.Min(c.Y, c.Z)) > 0.78f && MathF.Max(c.X, MathF.Max(c.Y, c.Z)) - MathF.Min(c.X, MathF.Min(c.Y, c.Z)) < 0.12f;

    private static FootSurface? PropSurface(Map map, int solid)
    {
        if (_propMap != map || _propCount != map.PropInstances.Count || _propOfSolid.Length != map.Solid.Count)
        {
            _propMap = map;
            _propCount = map.PropInstances.Count;
            _propOfSolid = new int[map.Solid.Count];
            Array.Fill(_propOfSolid, -1);
            _propSurface = new FootSurface[map.PropInstances.Count];
            for (var k = 0; k < map.PropInstances.Count; k++)
            {
                var pi = map.PropInstances[k];
                _propSurface[k] = OfProp(pi.Def);
                for (var s = pi.SolidStart; s < pi.SolidStart + pi.SolidCount && s < _propOfSolid.Length; s++)
                    if (s >= 0)
                        _propOfSolid[s] = k;
            }
        }
        var idx = _propOfSolid[solid];
        return idx < 0 ? null : _propSurface[idx];
    }

    /// <summary>A prop's surface: its biggest textured parts decide (wood, metal, masonry), else how it breaks, else wood.</summary>
    public static FootSurface OfProp(PropDef def)
    {
        Span<float> volume = stackalloc float[(int)Material.Count];
        foreach (var part in def.Parts)
            if (part.Layer >= 0 && part.Layer < (int)Material.Count)
                volume[part.Layer] += part.Size.X * part.Size.Y * part.Size.Z;
        var best = -1;
        for (var m = 0; m < volume.Length; m++)
            if (volume[m] > 0 && (best < 0 || volume[m] > volume[best]))
                best = m;
        if (best >= 0)
            return (Material)best switch
            {
                Material.Wood => FootSurface.Wood,
                Material.Metal => FootSurface.Metal,
                Material.Dirt => FootSurface.Dirt,
                _ => FootSurface.Concrete,
            };
        return def.Breakable?.Sound switch
        {
            PropBreakSound.Metal => FootSurface.Metal,
            PropBreakSound.Glass => FootSurface.Concrete,
            _ => FootSurface.Wood,
        };
    }
}

/// <summary>
/// Footsteps for every player, local and remote (bots too), from what is drawn: <see cref="Player.RenderVelocity"/>,
/// <see cref="Player.RenderGrounded"/> and <see cref="Player.RenderStance"/>. A gait phase runs at the leg animation's
/// cadence (<see cref="LegAnimator.Cadence"/>: 3.7 cycles a second at walking speed, 5 at a sprint, two steps per
/// cycle) and is pulled onto the drawn legs' phase whenever they were posed this frame, so a step sounds as the foot
/// plants. Walk = soft, sprint = heavy, crouch-walk = near silent, prone = a scuffle; nothing in the air; a jump
/// pushes off and a landing thuds by fall speed. Headless (no audio): the self-test drives it directly.
/// </summary>
public sealed class Footsteps
{
    /// <summary>Below this (smoothed) speed nobody takes steps.</summary>
    public const float MinSpeed = 0.8f;

    /// <summary>Falls slower than this land silently (stairs, small bumps).</summary>
    public const float MinLandSpeed = 2.5f;

    private struct State
    {
        public bool Init, Grounded;
        public float Phase, AirTime, MinVy, SinceStep;
        public Vector2 Vel;
        public double Last;
    }

    private readonly State[] _st = new State[256];

    /// <summary>Steps (and jumps / landings) emitted since the last read: for stats and the self-test.</summary>
    public int Emitted;

    /// <summary>Forgets every player's state (a new map or session).</summary>
    public void Reset() => Array.Clear(_st);

    /// <summary>Advances one player to <paramref name="now"/>; calls <paramref name="emit"/> for each step taken.</summary>
    public void Update(Player p, double now, Action<StepEvent> emit, float? legPhase = null)
    {
        ref var st = ref _st[p.Id];
        var dt = (float)(now - st.Last);
        var vel = p.RenderVelocity;
        var flat = new Vector2(vel.X, vel.Z);
        var grounded = p.RenderGrounded || p.RenderMantling;
        if (!st.Init || dt > 0.5f || dt < 0 || !p.Alive || p.RenderSeat != SeatKind.None) // (seated in a construct: carried, no steps)
        {
            st = default;
            st.Init = p.Alive;
            st.Last = now;
            st.Vel = flat;
            st.Grounded = grounded;
            st.SinceStep = 1;
            return;
        }
        st.Last = now;
        dt = MathF.Min(dt, 0.1f);
        st.SinceStep += dt;
        st.Vel = Vector2.Lerp(st.Vel, flat, 1f - MathF.Exp(-10f * dt)); // the same smoothing as the legs
        var speed = st.Vel.Length();
        var stance = p.RenderStance;

        // ---- Air: jump push-off, landing thud ----
        if (!grounded)
        {
            if (st.Grounded && vel.Y > 2f && stance != Stance.Prone)
            {
                emit(new StepEvent(p.Id, StepKind.Jump, SurfaceProbe.Under(p.RenderPosition), p.RenderPosition, Math.Clamp(vel.Y / World.JumpSpeed, 0.3f, 1f)));
                Emitted++;
            }
            st.AirTime += dt;
            st.MinVy = MathF.Min(st.MinVy, vel.Y);
            st.Grounded = false;
            return;
        }
        if (!st.Grounded)
        {
            var fall = -st.MinVy;
            if (st.AirTime > 0.08f && fall >= MinLandSpeed)
            {
                emit(new StepEvent(p.Id, StepKind.Land, SurfaceProbe.Under(p.RenderPosition), p.RenderPosition, Math.Clamp(fall / 14f, 0.15f, 1f)));
                Emitted++;
                st.SinceStep = 0;
                st.Phase = 0.05f; // the landing was a plant: the next step comes half a cycle later
            }
            st.AirTime = 0;
            st.MinVy = 0;
            st.Grounded = true;
        }

        if (speed < MinSpeed)
            return;

        // ---- Gait phase: a plant at 0 (left foot) and 0.5 (right foot) ----
        var cadence = stance == Stance.Prone ? speed / 0.7f : stance == Stance.Crouch ? LegAnimator.CrouchCadence(speed) : LegAnimator.Cadence(speed);
        var prev = st.Phase;
        var next = prev + cadence * dt;
        if (legPhase is { } lp && float.IsFinite(lp))
        {
            var diff = lp - next;
            diff -= MathF.Round(diff); // -0.5..0.5
            next += diff * MathF.Min(1f, 8f * dt); // phase-lock onto the drawn legs, gently (never jumps a plant)
        }
        if (next < prev)
            next = prev; // never run backwards over a plant
        var crossed = MathF.Floor(next * 2f) > MathF.Floor(prev * 2f);
        st.Phase = next - MathF.Floor(next);
        if (!crossed || st.SinceStep < 0.06f)
            return;

        var right = MathF.Floor(next * 2f) % 2 == 0; // the legs plant foot 0 (the right) at phase 0
        var kind = stance switch
        {
            Stance.Prone => StepKind.Crawl,
            Stance.Crouch => StepKind.Crouch,
            _ => speed > (World.WalkSpeed + World.SprintSpeed) * 0.5f ? StepKind.Run : StepKind.Walk,
        };
        var foot = p.RenderPosition + World.FlatRight(p.RenderYaw) * (right ? 0.15f : -0.15f);
        var strength = Math.Clamp(speed / World.WalkSpeed, 0.35f, 1.3f);
        emit(new StepEvent(p.Id, kind, SurfaceProbe.Under(foot), foot, strength));
        Emitted++;
        st.SinceStep = 0;
    }
}

/// <summary>
/// The footstep voices: a fixed number of OpenAL sources only footsteps use, so 32 running players can't take every
/// voice from the gunfire. A new step takes a free voice, else the farthest playing one if it is nearer than that,
/// else it is dropped. Pure bookkeeping (no OpenAL), so the self-test can check the cap.
/// </summary>
public sealed class FootstepVoicePool
{
    private readonly double[] _until;
    private readonly float[] _dist;

    /// <summary>Steps dropped because every voice was busy with a nearer one.</summary>
    public int Dropped;

    public FootstepVoicePool(int voices)
    {
        _until = new double[voices];
        _dist = new float[voices];
    }

    public int Count => _until.Length;

    /// <summary>Voices still sounding at <paramref name="now"/>.</summary>
    public int Busy(double now)
    {
        var n = 0;
        foreach (var u in _until)
            if (u > now)
                n++;
        return n;
    }

    /// <summary>A voice for a step <paramref name="distance"/> m away lasting <paramref name="seconds"/>, or -1 (dropped).</summary>
    public int Acquire(double now, float distance, float seconds)
    {
        var pick = -1;
        var farthest = -1f;
        for (var i = 0; i < _until.Length; i++)
        {
            if (_until[i] <= now)
            {
                pick = i;
                farthest = float.MaxValue;
                break;
            }
            if (_dist[i] > farthest)
            {
                farthest = _dist[i];
                pick = i;
            }
        }
        if (pick < 0 || (farthest != float.MaxValue && farthest <= distance))
        {
            Dropped++;
            return -1;
        }
        _until[pick] = now + seconds;
        _dist[pick] = distance;
        return pick;
    }

    /// <summary>Re-books voice <paramref name="v"/> (a sound cutting the same source's last one).</summary>
    public void Take(int v, double now, float distance, float seconds)
    {
        if ((uint)v >= (uint)_until.Length)
            return;
        _until[v] = now + seconds;
        _dist[v] = distance;
    }
}
