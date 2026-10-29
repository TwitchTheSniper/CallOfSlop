using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A body part a death reaction poses (the hit side's or the other side's; <see cref="DeathReaction"/>).</summary>
public enum ReactionPart : byte
{
    Head, ElbowHit, HandHit, KneeHit, FootHit, ElbowOther, HandOther, KneeOther, FootOther,
}

/// <summary>Where a reaction's target is: a point in the body's balance frame, or on the body (the wound, the face, the belly).</summary>
public enum ReactionAnchor : byte
{
    Point, Wound, Face, Belly,
}

/// <summary>One pose target of a death reaction: a part, where it goes, and how hard it is driven there (0-1).</summary>
public readonly record struct ReactionTarget(ReactionPart Part, ReactionAnchor Anchor, Vector3 At, float Weight);

/// <summary>
/// One death reaction (Ragdolls/Ragdolls.json "reactions"): when it applies (causes, hit regions, running, legless) and
/// what the powered ragdoll does for its short active phase (Source/Physics/RagdollActive.cs). Angles in degrees,
/// speeds in m/s; x is towards the hit side, y up, z back (away from where the body faces).
/// </summary>
public sealed class DeathReaction
{
    public string Name = "";
    public DeathCause[]? Causes;

    /// <summary>Hit regions it applies to (null: any): <see cref="BloodPart"/> values (None: unknown, e.g. a zombie's body shot).</summary>
    public BloodPart[]? Regions;

    /// <summary>Only when the body was moving faster than <see cref="RagdollActive.MovingSpeed"/> (true), only when not (false), or either (null).</summary>
    public bool? Moving;

    /// <summary>Only legless zombies (crawlers and slugs) take a reaction that says true; nothing else applies to them.</summary>
    public bool Legless;

    public float Weight = 1, Strength = 1, Duration = 1;

    /// <summary>How hard the torso is held up over the hips (0-1), and how much of the body's weight the legs hold at first (0-1; it fades).</summary>
    public float Balance = 0.6f, Support = 0.5f;

    /// <summary>Torso lean: pitch (forward +) and roll (towards the hit side +), degrees.</summary>
    public float LeanPitch, LeanRoll;

    /// <summary>Turning towards the hit side (degrees per second).</summary>
    public float Turn;

    /// <summary>The hips' stagger (m/s): towards the hit side, and away from the attacker.</summary>
    public float StaggerSide, StaggerBack;

    /// <summary>The share of the running speed kept (a forward stumble), the pull towards the attacker and down (m/s), the limbs' flailing (0-1).</summary>
    public float Carry, Pull, Flail;

    /// <summary>Steps a foot to catch the fall when the hips move off the feet.</summary>
    public bool Step;

    public ReactionTarget[] Targets = [];

    /// <summary>How many conditions it has: among the reactions that apply, the most specific win.</summary>
    public int Specificity => (Causes != null ? 1 : 0) + (Regions != null ? 1 : 0) + (Moving != null ? 1 : 0) + (Legless ? 1 : 0);

    public bool Applies(DeathCause cause, BloodPart region, bool moving, bool legless) =>
        Legless == legless && (Causes == null || Array.IndexOf(Causes, cause) >= 0) && (Regions == null || Array.IndexOf(Regions, region) >= 0)
        && (Moving == null || Moving == moving);
}

/// <summary>
/// Active ragdolls: a death that was "a near thing" (the killing hit only just did enough: small overkill) gets a short
/// active phase, 0.4-1.5 s (longer the nearer it was), in which soft muscle springs drive the body towards a balance
/// and stumble pose from the death reaction table (Ragdolls/Ragdolls.json "reactions": a leg hit buckles that knee, a
/// chest hit staggers back clutching the wound, an arm hit spins the body, fire flails, a melee hit staggers away...),
/// with a simple balance controller (the torso held up over the hips, the legs holding part of the weight, a foot stepping
/// to catch the fall), its strength fading to nothing so it hands over smoothly to the limp ragdoll. Big overkill
/// (headshots that do far more than needed, blasts, point-blank shotguns, snipers) stays limp.
///
/// The choice is deterministic from the kill (cause, hit region, overkill, kill id, victim, killer: every client picks
/// the same reaction, side and duration); at most MaxActive bodies are powered at once (zombies MaxActiveZombies; the rest
/// go limp). The drive works on particle velocities like the relax pull (no extra constraints), so the soft limits, the
/// collisions and the sub-steps keep it stable. No allocations.
/// </summary>
public static class RagdollActive
{
    /// <summary>A body moving faster than this (m/s, across gravity) when it died counts as running.</summary>
    public const float MovingSpeed = 3f;

    /// <summary>The reactions (from the file; empty if it has none: every death is limp).</summary>
    public static DeathReaction[] Reactions = [];

    /// <summary>One body's active phase (in <see cref="RagdollBody"/>).</summary>
    public struct State
    {
        public bool Active;
        public int Reaction;
        public float Time, Duration, Strength;

        /// <summary>+1: the hit side is the body's right; -1: its left.</summary>
        public float Side;

        /// <summary>The heading at death (across gravity), away from the attacker, towards the attacker, the run's velocity.</summary>
        public Vector3 Heading, Away, Toward, RunVel;
        public int Wound;

        // Stepping: which foot (-1 none), where to, how far through the step (s left).
        public int StepFoot;
        public Vector3 StepFrom, StepTo;
        public float StepLeft, StepWait;
    }

    /// <summary>What a death's reaction would be: the reaction (-1: limp), the hit side and the active time.</summary>
    public readonly record struct Choice(int Reaction, float Side, float Duration, float Strength);

    /// <summary>A hash of the kill only (no positions: they differ a little between PCs), for the reaction's variant and side.</summary>
    public static uint KillHash(in RagdollDeath d)
    {
        var h = 2166136261u;
        void Mix(int v) => h = (h ^ (uint)v) * 16777619u;
        Mix(d.Victim);
        Mix(d.Killer);
        Mix(d.KillId);
        Mix(d.Headshot ? 1 : 0);
        Mix((int)d.HitPart);
        Mix((int)d.Cause);
        Mix(d.OverkillHp);
        Mix(d.OverkillFraction < 0 ? -1 : (int)MathF.Round(d.OverkillFraction * 254));
        h ^= h >> 15;
        h *= 0x2c1b3c6du;
        h ^= h >> 12;
        return h;
    }

    /// <summary>The hit region a reaction matches on (a headshot is the head whatever part was recorded).</summary>
    public static BloodPart Region(in RagdollDeath d) => d.Headshot ? BloodPart.Head : d.HitPart;

    /// <summary>
    /// How near a thing the death was: 0 = plenty of overkill (limp), up to 1 = the hit did exactly enough. A near thing
    /// is overkill under OverkillShare of the hit or under OverkillHp; unknown overkill is never one.
    /// </summary>
    public static float Nearness(in RagdollDeath d)
    {
        var t = RagdollTuning.Values;
        float share = t[(int)RT.OverkillShare], hp = t[(int)RT.OverkillHp];
        var n = 0f;
        if (d.OverkillFraction >= 0 && share > 0)
            n = MathF.Max(n, 1f - d.OverkillFraction / share);
        if (d.OverkillHp >= 0 && hp > 0)
            n = MathF.Max(n, 1f - d.OverkillHp / hp);
        return Math.Clamp(n, 0f, 1f);
    }

    /// <summary>The reaction a death gets (the same on every PC), ignoring the cap on active bodies.</summary>
    public static Choice Choose(in RagdollDeath d)
    {
        var reactions = Reactions;
        if (reactions.Length == 0 || d.BlastStrength > 0 || d.Cause == DeathCause.Explosive)
            return new Choice(-1, 1, 0, 0);
        var near = Nearness(d);
        if (near <= 0)
            return new Choice(-1, 1, 0, 0);
        var region = Region(d);
        var flat = d.Velocity;
        flat.Y = 0;
        var moving = flat.LengthSquared() > MovingSpeed * MovingSpeed;
        var best = -1;
        var total = 0f;
        for (var i = 0; i < reactions.Length; i++)
        {
            if (!reactions[i].Applies(d.Cause, region, moving, d.Legless))
                continue;
            var spec = reactions[i].Specificity;
            if (best < 0 || spec > reactions[best].Specificity)
            {
                best = i;
                total = 0;
            }
            if (spec == reactions[best].Specificity)
                total += MathF.Max(reactions[i].Weight, 0);
        }
        if (best < 0 || total <= 0)
            return new Choice(-1, 1, 0, 0);
        var h = KillHash(d);
        var pick = (h & 0xFFFF) / 65536f * total;
        var bestSpec = reactions[best].Specificity;
        var chosen = best;
        for (var i = 0; i < reactions.Length; i++)
        {
            if (reactions[i].Specificity != bestSpec || !reactions[i].Applies(d.Cause, region, moving, d.Legless))
                continue;
            chosen = i;
            pick -= MathF.Max(reactions[i].Weight, 0);
            if (pick < 0)
                break;
        }
        var side = region is BloodPart.ArmR or BloodPart.LegR ? 1f : region is BloodPart.ArmL or BloodPart.LegL ? -1f : (h & 0x10000) != 0 ? 1f : -1f;
        var t = RagdollTuning.Values;
        float min = t[(int)RT.ActiveMin], max = MathF.Max(t[(int)RT.ActiveMax], min);
        var r = reactions[chosen];
        return new Choice(chosen, side, (min + (max - min) * near) * r.Duration, r.Strength);
    }

    /// <summary>Starts a body's active phase (the body is placed; <paramref name="c"/> from <see cref="Choose"/>).</summary>
    public static void Start(Ragdoll sim, ref State a, in Choice c, in RagdollDeath d)
    {
        a = default;
        a.Active = true;
        a.Reaction = c.Reaction;
        a.Side = c.Side;
        a.Duration = MathF.Max(c.Duration, Ragdoll.Step);
        a.Strength = c.Strength;
        a.StepFoot = -1;
        var up = Up(sim.Pelvis);
        var fwd = sim.Forward();
        fwd -= up * Vector3.Dot(fwd, up);
        a.Heading = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : Vector3.UnitZ * -1;
        var away = d.HasSource ? sim.Pelvis - d.Source : -a.Heading;
        away -= up * Vector3.Dot(away, up);
        a.Away = away.LengthSquared() > 1e-6f ? Vector3.Normalize(away) : -a.Heading;
        a.Toward = -a.Away;
        var run = d.Velocity;
        run -= up * Vector3.Dot(run, up);
        a.RunVel = float.IsFinite(run.X + run.Y + run.Z) ? run : Vector3.Zero;
        a.Wound = WoundParticle(Region(d), a.Side);
        sim.Driven = true;
    }

    private static Vector3 Up(Vector3 at) => GravityFrames.Curved ? GravityFrames.UpAt(at) : Vector3.UnitY;

    /// <summary>The particle a hit region's wound is at (a hand clutches it).</summary>
    public static int WoundParticle(BloodPart region, float side) => region switch
    {
        BloodPart.Head => RagdollRig.Head,
        BloodPart.Back => RagdollRig.Back,
        BloodPart.ArmR => RagdollRig.ElbowR,
        BloodPart.ArmL => RagdollRig.ElbowL,
        BloodPart.LegR => RagdollRig.KneeR,
        BloodPart.LegL => RagdollRig.KneeL,
        _ => side > 0 ? RagdollRig.ShoulderR : RagdollRig.ShoulderL,
    };

    /// <summary>Stops the active phase: the body goes limp (and may relax and sleep from here).</summary>
    public static void Stop(Ragdoll sim, ref State a)
    {
        if (!a.Active)
            return;
        a.Active = false;
        sim.Driven = false;
        sim.Wake();
    }

    /// <summary>The share of the strength left at time <paramref name="t"/> of <paramref name="duration"/>: full, then a smooth fade to nothing.</summary>
    public static float Fade(float t, float duration)
    {
        var x = Math.Clamp((t / duration - 0.4f) / 0.6f, 0f, 1f);
        return 1f - x * x * (3f - 2f * x);
    }

    /// <summary>Particles of a reaction part (by side: the hit side's or the other).</summary>
    private static int PartParticle(ReactionPart p, float side)
    {
        var hitRight = side > 0;
        return p switch
        {
            ReactionPart.Head => RagdollRig.Head,
            ReactionPart.ElbowHit => hitRight ? RagdollRig.ElbowR : RagdollRig.ElbowL,
            ReactionPart.HandHit => hitRight ? RagdollRig.HandR : RagdollRig.HandL,
            ReactionPart.KneeHit => hitRight ? RagdollRig.KneeR : RagdollRig.KneeL,
            ReactionPart.FootHit => hitRight ? RagdollRig.FootR : RagdollRig.FootL,
            ReactionPart.ElbowOther => hitRight ? RagdollRig.ElbowL : RagdollRig.ElbowR,
            ReactionPart.HandOther => hitRight ? RagdollRig.HandL : RagdollRig.HandR,
            ReactionPart.KneeOther => hitRight ? RagdollRig.KneeL : RagdollRig.KneeR,
            _ => hitRight ? RagdollRig.FootL : RagdollRig.FootR,
        };
    }

    // The standing pose relative to the hips' middle (the default limb targets, loosely held).
    private static readonly Vector3 StandMidHip = (RagdollRig.Stand[RagdollRig.HipR] + RagdollRig.Stand[RagdollRig.HipL]) * 0.5f;

    // Per step scratch: each particle's target and weight this step (no allocation).
    private static readonly Vector3[] TargetAt = new Vector3[RagdollRig.Count];
    private static readonly float[] TargetW = new float[RagdollRig.Count];

    /// <summary>Phase offsets for the flailing limbs.</summary>
    private static readonly float[] FlailPhase = [0, 0, 0, 0, 0, 0, 0.7f, 1.9f, 0.3f, 3.3f, 2.4f, 4.1f, 5.2f, 1.2f, 2.8f];

    /// <summary>Bodies currently powered (stats, the cap).</summary>
    public static int Driving;

    /// <summary>
    /// One 60 Hz step of the drive, before the body's own step: the hips staggered and held, the torso held up and
    /// leaned, the limbs driven to the reaction's targets (the rest loosely to a standing pose), a foot stepping to catch
    /// the fall; all scaled by the fading strength. Ends the phase (limp) when its time is up.
    /// </summary>
    public static void Drive(Ragdoll sim, ref State a)
    {
        if (!a.Active)
            return;
        if (sim.Asleep || sim.Broken || a.Time >= a.Duration || (uint)a.Reaction >= (uint)Reactions.Length)
        {
            Stop(sim, ref a);
            return;
        }
        var r = Reactions[a.Reaction];
        var tune = RagdollTuning.Values;
        var t = a.Time;
        a.Time += Ragdoll.Step;
        var s = a.Strength * Fade(t, a.Duration);
        if (s <= 0)
            return;
        var pelvis = sim.Pelvis;
        var up = Up(pelvis);

        // The balance frame: the heading at death turned by the reaction, the torso's "up" leaned.
        var heading = a.Heading;
        if (r.Turn != 0)
        {
            var q = Quaternion.CreateFromAxisAngle(up, -r.Turn * a.Side * t * (MathF.PI / 180f));
            heading = Vector3.Normalize(Vector3.Transform(heading, q));
        }
        var right = Vector3.Cross(heading, up);
        var hitSide = right * a.Side;
        var leanUp = Vector3.Normalize(up + heading * MathF.Tan(Math.Clamp(r.LeanPitch, -80, 80) * (MathF.PI / 180f))
                                          + hitSide * MathF.Tan(Math.Clamp(r.LeanRoll, -80, 80) * (MathF.PI / 180f)));
        var back = Vector3.Cross(right, leanUp); // (right-handed: x right, y up, z back)

        // The hips: staggered, carried, pulled; held up by the legs (support fading over the phase).
        var progress = t / a.Duration;
        var stagger = a.Away * r.StaggerBack + hitSide * r.StaggerSide + a.RunVel * (r.Carry * (1f - progress)) + (a.Toward - up * 0.6f) * r.Pull;
        var support = Math.Clamp(r.Support * (1f - progress), 0f, 1f);
        var hipHeight = Vector3.Dot(pelvis - LowestFoot(sim, up), up);
        var gain = tune[(int)RT.MuscleGain];
        var blend = tune[(int)RT.MuscleBlend] * s;
        var maxMove = tune[(int)RT.MuscleSpeed] * Ragdoll.Step;
        for (var i = RagdollRig.HipR; i <= RagdollRig.Back; i++)
        {
            var v = sim.Pos[i] - sim.Prev[i];
            var vUp = Vector3.Dot(v, up);
            var flat = v - up * vUp;
            flat += (stagger * Ragdoll.Step - flat) * (blend * 0.5f);
            if (vUp < 0 && hipHeight > 0.45f)
                vUp *= 1f - support * s; // the legs hold part of the weight
            sim.Prev[i] = sim.Pos[i] - (flat + up * vUp);
        }

        // Targets: every limb loosely towards a standing pose in the balance frame, the reaction's parts firmly to theirs.
        Array.Clear(TargetW);
        for (var i = RagdollRig.Head; i < RagdollRig.Count; i++)
        {
            var p = RagdollRig.Stand[i] - StandMidHip;
            TargetAt[i] = pelvis + right * p.X + leanUp * p.Y + back * p.Z;
            TargetW[i] = i == RagdollRig.Head ? r.Balance : 0.3f;
        }
        TargetW[RagdollRig.FootR] = TargetW[RagdollRig.FootL] = 0; // feet: planted or stepping (below)
        TargetW[RagdollRig.KneeR] = TargetW[RagdollRig.KneeL] = 0.15f;
        foreach (var tg in r.Targets)
        {
            var i = PartParticle(tg.Part, a.Side);
            TargetAt[i] = tg.Anchor switch
            {
                ReactionAnchor.Wound => sim.Pos[a.Wound] - heading * 0.12f,
                ReactionAnchor.Face => sim.Pos[RagdollRig.Head] + heading * 0.12f - leanUp * 0.05f,
                ReactionAnchor.Belly => sim.Pos[RagdollRig.Belly] + heading * 0.1f,
                _ => pelvis + hitSide * tg.At.X + leanUp * tg.At.Y + back * tg.At.Z,
            };
            TargetW[i] = tg.Weight;
        }
        if (r.Flail > 0)
            for (var i = RagdollRig.ElbowR; i < RagdollRig.Count; i++)
            {
                var ph = t * 13f + FlailPhase[i];
                TargetAt[i] += (right * MathF.Sin(ph) * 0.35f + leanUp * MathF.Sin(ph * 1.3f + 1f) * 0.3f + heading * MathF.Cos(ph * 0.8f) * 0.2f) * r.Flail;
                TargetW[i] = MathF.Max(TargetW[i], 0.6f * r.Flail);
            }

        // The torso up: the shoulders over the hips along the leaned "up" (the balance).
        var shoulderUp = (RagdollRig.Stand[RagdollRig.ShoulderR] + RagdollRig.Stand[RagdollRig.ShoulderL]) * 0.5f - StandMidHip;
        for (var k = 0; k < 2; k++)
        {
            var i = RagdollRig.ShoulderR + k;
            var p = RagdollRig.Stand[i] - StandMidHip;
            TargetAt[i] = pelvis + right * p.X + leanUp * shoulderUp.Y;
            TargetW[i] = r.Balance;
        }

        // A foot steps to catch the fall: when the hips have moved off the feet, the foot further behind goes ahead.
        if (r.Step && support > 0.05f)
            StepFeet(sim, ref a, up, pelvis);
        if (a.StepFoot >= 0)
        {
            var k = 1f - a.StepLeft / StepTime;
            TargetAt[a.StepFoot] = Vector3.Lerp(a.StepFrom, a.StepTo, k) + up * (0.14f * MathF.Sin(MathF.PI * k));
            TargetW[a.StepFoot] = 1f;
            a.StepLeft -= Ragdoll.Step;
            if (a.StepLeft <= 0)
                a.StepFoot = -1;
        }

        for (var i = RagdollRig.ShoulderR; i < RagdollRig.Count; i++)
        {
            var w = TargetW[i];
            if (w <= 0 || i == RagdollRig.Belly || i == RagdollRig.Back)
                continue;
            var v = sim.Pos[i] - sim.Prev[i];
            var want = (TargetAt[i] - sim.Pos[i]) * gain;
            var l2 = want.LengthSquared();
            if (l2 > maxMove * maxMove)
                want *= maxMove / MathF.Sqrt(l2);
            var nv = v + (want - v) * MathF.Min(1f, blend * w);
            if (float.IsFinite(nv.X + nv.Y + nv.Z))
                sim.Prev[i] = sim.Pos[i] - nv;
        }
        if (a.Time >= a.Duration)
        {
            // The hand-over: a body still up (standing, kneeling, sitting: the hips well off the ground) tips over the way it
            // leaned, or away from the hit, so it doesn't stay propped up on its knees.
            Ragdoll.TorsoAxes(sim.Pos, out _, out var torsoUp, out _);
            if (Vector3.Dot(torsoUp, up) > 0.35f || Vector3.Dot(sim.Pelvis - LowestFoot(sim, up), up) > 0.3f)
            {
                var dir = leanUp - up * Vector3.Dot(leanUp, up); // the way it leans, else away from the hit
                dir = dir.LengthSquared() > 0.01f ? Vector3.Normalize(dir) : a.Away;
                for (var i = RagdollRig.ShoulderR; i <= RagdollRig.Head; i++)
                    if (i != RagdollRig.Belly)
                        sim.Push(i, dir * (i == RagdollRig.Back ? 0.8f : 1.7f));
            }
            Stop(sim, ref a);
        }
    }

    private const float StepTime = 0.28f;

    private static Vector3 LowestFoot(Ragdoll sim, Vector3 up) =>
        Vector3.Dot(sim.Pos[RagdollRig.FootR], up) < Vector3.Dot(sim.Pos[RagdollRig.FootL], up) ? sim.Pos[RagdollRig.FootR] : sim.Pos[RagdollRig.FootL];

    private static void StepFeet(Ragdoll sim, ref State a, Vector3 up, Vector3 pelvis)
    {
        if (a.StepWait > 0)
            a.StepWait -= Ragdoll.Step;
        if (a.StepFoot >= 0 || a.StepWait > 0)
            return;
        var fr = sim.Pos[RagdollRig.FootR];
        var fl = sim.Pos[RagdollRig.FootL];
        var mid = (fr + fl) * 0.5f;
        var off = pelvis - mid;
        off -= up * Vector3.Dot(off, up);
        var v = (sim.Pos[RagdollRig.HipR] - sim.Prev[RagdollRig.HipR] + sim.Pos[RagdollRig.HipL] - sim.Prev[RagdollRig.HipL]) * (0.5f / Ragdoll.Step);
        v -= up * Vector3.Dot(v, up);
        if (off.LengthSquared() < 0.2f * 0.2f && v.LengthSquared() < 0.6f * 0.6f)
            return;
        var dir = v.LengthSquared() > 0.04f ? Vector3.Normalize(v) : Vector3.Normalize(off);
        var foot = Vector3.Dot(fr - mid, dir) < Vector3.Dot(fl - mid, dir) ? RagdollRig.FootR : RagdollRig.FootL; // the one further behind
        var p = sim.Pos[foot];
        var ground = Vector3.Dot(LowestFoot(sim, up), up);
        var to = pelvis + dir * 0.32f + Vector3.Cross(dir, up) * (foot == RagdollRig.FootR ? -0.12f : 0.12f);
        to += up * (ground - Vector3.Dot(to, up));
        a.StepFoot = foot;
        a.StepFrom = p;
        a.StepTo = to;
        a.StepLeft = StepTime;
        a.StepWait = 0.08f;
    }

    // ---- The table (Ragdolls/Ragdolls.json "reactions") --------------------------------------------------------

    private static readonly string[] CauseNames = ["bullet", "explosive", "melee", "fire", "poison", "fall", "zombie", "shock"];
    private static readonly string[] RegionNames = ["none", "head", "chest", "back", "armL", "armR", "legL", "legR"];
    private static readonly string[] PartNames = ["head", "elbowHit", "handHit", "kneeHit", "footHit", "elbowOther", "handOther", "kneeOther", "footOther"];

    /// <summary>Reads the "reactions" list (throws <see cref="WeaponFileException"/> with the reason).</summary>
    public static DeathReaction[] Load(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw new WeaponFileException($"{where}: a list of reactions");
        var list = new List<DeathReaction>();
        var k = 0;
        foreach (var item in e.EnumerateArray())
        {
            var at = $"{where}[{k++}]";
            if (item.ValueKind != JsonValueKind.Object)
                throw new WeaponFileException($"{at}: a reaction is an object");
            var r = new DeathReaction();
            foreach (var f in item.EnumerateObject())
            {
                var w = $"{at}.{f.Name}";
                switch (f.Name)
                {
                    case "note": break;
                    case "name": r.Name = WeaponFile.Text(f.Value, w); break;
                    case "when": ReadWhen(r, f.Value, w); break;
                    case "weight": r.Weight = Real(f.Value, w, 0, 100); break;
                    case "strength": r.Strength = Real(f.Value, w, 0, 2); break;
                    case "duration": r.Duration = Real(f.Value, w, 0.1f, 3); break;
                    case "balance": r.Balance = Real(f.Value, w, 0, 1); break;
                    case "support": r.Support = Real(f.Value, w, 0, 1); break;
                    case "lean":
                        if (f.Value.ValueKind != JsonValueKind.Array || f.Value.GetArrayLength() != 2)
                            throw new WeaponFileException($"{w}: [pitch, roll] in degrees");
                        r.LeanPitch = Real(f.Value[0], w, -80, 80);
                        r.LeanRoll = Real(f.Value[1], w, -80, 80);
                        break;
                    case "turn": r.Turn = Real(f.Value, w, -720, 720); break;
                    case "staggerSide": r.StaggerSide = Real(f.Value, w, -5, 5); break;
                    case "staggerBack": r.StaggerBack = Real(f.Value, w, -5, 5); break;
                    case "carry": r.Carry = Real(f.Value, w, 0, 1.5f); break;
                    case "pull": r.Pull = Real(f.Value, w, 0, 5); break;
                    case "flail": r.Flail = Real(f.Value, w, 0, 2); break;
                    case "step": r.Step = f.Value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new WeaponFileException($"{w}: true or false") }; break;
                    case "targets": r.Targets = ReadTargets(f.Value, w); break;
                    default: throw new WeaponFileException($"{w}: unknown key (known: name, when, weight, strength, duration, balance, support, lean, turn, staggerSide, staggerBack, carry, pull, flail, step, targets)");
                }
            }
            if (r.Name == "")
                throw new WeaponFileException($"{at}: a reaction needs a \"name\"");
            list.Add(r);
        }
        return list.ToArray();
    }

    private static float Real(JsonElement e, string where, float min, float max)
    {
        var v = WeaponFile.Number(e, where);
        if (!(v >= min && v <= max))
            throw new WeaponFileException($"{where}: {v} is outside {min}..{max}");
        return v;
    }

    private static void ReadWhen(DeathReaction r, JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: an object (cause, region, moving, legless)");
        foreach (var f in e.EnumerateObject())
        {
            var w = $"{where}.{f.Name}";
            switch (f.Name)
            {
                case "cause":
                    r.Causes = Names(f.Value, w, CauseNames).Select(i => (DeathCause)i).ToArray();
                    break;
                case "region":
                    // "arm" and "leg" are both sides.
                    var names = f.Value.ValueKind == JsonValueKind.Array ? f.Value.EnumerateArray().Select(x => WeaponFile.Text(x, w)).ToList() : [WeaponFile.Text(f.Value, w)];
                    var parts = new List<BloodPart>();
                    foreach (var n in names)
                    {
                        if (n == "arm")
                            parts.AddRange([BloodPart.ArmL, BloodPart.ArmR]);
                        else if (n == "leg")
                            parts.AddRange([BloodPart.LegL, BloodPart.LegR]);
                        else if (Array.IndexOf(RegionNames, n) is var i and >= 0)
                            parts.Add((BloodPart)i);
                        else
                            throw new WeaponFileException($"{w}: unknown region \"{n}\" (known: {string.Join(", ", RegionNames)}, arm, leg)");
                    }
                    r.Regions = parts.ToArray();
                    break;
                case "moving":
                    r.Moving = f.Value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new WeaponFileException($"{w}: true or false") };
                    break;
                case "legless":
                    r.Legless = f.Value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new WeaponFileException($"{w}: true or false") };
                    break;
                default:
                    throw new WeaponFileException($"{w}: unknown condition (known: cause, region, moving, legless)");
            }
        }
    }

    private static int[] Names(JsonElement e, string where, string[] known)
    {
        var names = e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => WeaponFile.Text(x, where)).ToArray() : [WeaponFile.Text(e, where)];
        return names.Select(n => Array.IndexOf(known, n) is var i and >= 0 ? i : throw new WeaponFileException($"{where}: unknown \"{n}\" (known: {string.Join(", ", known)})")).ToArray();
    }

    private static ReactionTarget[] ReadTargets(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw new WeaponFileException($"{where}: a list of {{\"part\": ..., \"at\": [x, y, z] or \"wound\" / \"face\" / \"belly\"}}");
        var list = new List<ReactionTarget>();
        var k = 0;
        foreach (var t in e.EnumerateArray())
        {
            var w = $"{where}[{k++}]";
            if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("part", out var pe) || !t.TryGetProperty("at", out var ae))
                throw new WeaponFileException($"{w}: needs \"part\" and \"at\"");
            var part = Names(pe, w + ".part", PartNames)[0];
            var anchor = ReactionAnchor.Point;
            var at = Vector3.Zero;
            if (ae.ValueKind == JsonValueKind.String)
                anchor = WeaponFile.Text(ae, w) switch
                {
                    "wound" => ReactionAnchor.Wound,
                    "face" => ReactionAnchor.Face,
                    "belly" => ReactionAnchor.Belly,
                    var other => throw new WeaponFileException($"{w}.at: \"{other}\" isn't a place (wound, face, belly or [x, y, z])"),
                };
            else if (ae.ValueKind == JsonValueKind.Array && ae.GetArrayLength() == 3)
                at = new Vector3(Real(ae[0], w + ".at", -2, 2), Real(ae[1], w + ".at", -2, 2.5f), Real(ae[2], w + ".at", -2, 2));
            else
                throw new WeaponFileException($"{w}.at: [x, y, z] (m from the hips: towards the hit side, up, back) or wound / face / belly");
            var weight = t.TryGetProperty("weight", out var we) ? Real(we, w + ".weight", 0, 1) : 1f;
            foreach (var extra in t.EnumerateObject())
                if (extra.Name is not ("part" or "at" or "weight" or "note"))
                    throw new WeaponFileException($"{w}: unknown key \"{extra.Name}\"");
            list.Add(new ReactionTarget((ReactionPart)part, anchor, at, weight));
        }
        return list.ToArray();
    }
}
