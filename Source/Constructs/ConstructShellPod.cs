using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A shell pod's numbers (its part file's <c>"shellPod"</c>; ConstructShellPod.cs).</summary>
public sealed class ShellPodDef
{
    public byte Class; // its size class (ConstructShellPod.ClassNames): it takes a shell of this class or smaller
    public float Half; // half its width each way (x and z), metres
    public float Top; // the lid's top in the part's frame (the open face; the frame rises from here along +y)
    public float Depth; // how deep it is under the lid
    public bool Flush; // its body is below its socket: recessed into the surface it is welded to
}

/// <summary>A docked shell as a pod keeps it (its settings), read out to stand it again.</summary>
public struct DockedShell
{
    public byte Frame, Owner, Ore;
    public int Channel, Health, OreUnits;
    public ShellCond Wear;
    public ulong Kit; // a byte a slot (ShellFrames.Slots order)
}

/// <summary>A pod's fold or unfold under way (server: its own; a client: as heard with <see cref="Msg.ShellPod"/>).</summary>
public struct PodJob
{
    public ushort Body, Part; // the pod
    public byte Phase; // ConstructShellPod.Dock or Wake
    public byte Frame; // the shell's frame (Shells.json)
    public byte Player; // a wake: who wakes in it
    public ushort Chair; // ...from which chair's construct
    public double Start; // ConstructConveyor.Clock when it began (a client: when it heard it, less the time already run)
    public Vector3 From; // a dock: where the shell stood when its wearer pressed the button (it slides from there into the frame)
    public float Yaw; // ...and which way it faced
}

/// <summary>
/// Shell storage pods (docs/sandbox/design/shell-pod.md; the user, 2026-11-18): a place to park a robot shell when nobody wears it.
/// A pod is a part welded onto a floor, a wall or a ceiling, bolted on or flush; as a worn shell of its size class (or a smaller one)
/// comes near, a frame rises out of it along its face's normal (drawn by ShellPodRender.cs from the players every peer knows). The
/// wearer walks into the frame and presses the use key: the server sends them back to their chair (Game/GameRobotShell.cs) and keeps
/// the shell in the pod's own hidden settings (its frame, channel, owner, wear, modules, damage and a Mining Shell's hopper load), so
/// a docked shell is no part at all: nothing can hit it or take it, and it rides saves, blueprints never (<see cref="ConstructConfig.ToJson"/>
/// leaves it out) and CBody. The frame then draws the shell in as it folds (<see cref="ShellFold"/>). A chair on its channel wakes it:
/// the frame rises, the shell unfolds, and only then does it stand as a part again and its wearer wake in it. A pod taken off, broken
/// or wrecked stands its shell beside it first (<see cref="Release"/>). Both moves are timed on the server and every peer hears them
/// (<see cref="Msg.ShellPod"/>). Game/GameShellPod.cs is the game's side.
/// </summary>
public static class ConstructShellPod
{
    public const int MaxJobs = 32;
    public const byte Dock = 1, Wake = 2;
    public const double FoldEnd = 1.8, DockSeconds = 3.5; // a dock: it folds in the frame, then the frame draws it in
    public const double RiseEnd = 1.5, WakeSeconds = 3.5; // a wake: the frame rises with it folded, then it unfolds
    public const float NearRise = 6f; // m: a worn shell this near an empty pod that takes it raises its frame (fully at half this)

    /// <summary>The size classes by a shell's standing height (the user, 2026-11-18). Large and Huge pods are for later.</summary>
    public static readonly string[] ClassNames = ["Tiny", "Small", "Medium", "Large", "Huge"];
    public static readonly float[] ClassHeights = [1, 2, 5, 15, 30];

    /// <summary>The part files' <c>"shellPod"</c>: <c>{"class": "tiny|small|medium|large|huge", "half": m, "top": m, "depth": m, "flush": bool}</c>.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "shellPod") is not { } g)
            return;
        ConstructParts.Known(g, ["class", "half", "top", "depth", "flush"], $"{file}, shellPod");
        var cls = ConstructParts.Str(ConstructParts.Need(g, "class", $"{file}, shellPod"), file, "shellPod.class");
        var k = Array.FindIndex(ClassNames, n => n.Equals(cls, StringComparison.OrdinalIgnoreCase));
        if (k < 0)
            throw new ConstructPartException($"{file}: shellPod.class is one of {string.Join(", ", ClassNames).ToLowerInvariant()}");
        var pd = new ShellPodDef
        {
            Class = (byte)k,
            Half = ConstructParts.Num(ConstructParts.Need(g, "half", $"{file}, shellPod"), file, "shellPod.half"),
            Top = ConstructParts.Num(ConstructParts.Need(g, "top", $"{file}, shellPod"), file, "shellPod.top"),
            Depth = ConstructParts.Num(ConstructParts.Need(g, "depth", $"{file}, shellPod"), file, "shellPod.depth"),
            Flush = ConstructParts.Opt(g, "flush") is { ValueKind: JsonValueKind.True },
        };
        ConstructParts.Range(pd.Half, 0.1f, 20, file, "shellPod.half");
        ConstructParts.Range(pd.Depth, 0.1f, 20, file, "shellPod.depth");
        ConstructParts.Range(pd.Top, -20, 20, file, "shellPod.top");
        d.ShellPod = pd;
    }

    /// <summary>Every shell pod has these (ConstructConfig.Parse): what it holds, hidden (the shell's wear, damage and kit are ShellWear's keys).</summary>
    public static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        if (d.ShellPod == null)
            return;
        void Add(CfgKey k, string label, float max)
        {
            if (!list.Exists(e => e.Id == k))
                list.Add(new CConfigDef { Key = ConstructConfig.KeyName(k), Id = k, Type = CfgType.Number, Min = 0, Max = max, Step = 1, Hidden = true, Label = label, Group = "Shell pod", Tooltip = "Not on the panel." });
        }
        Add(CfgKey.PodShell, "Shell docked", 256);
        Add(CfgKey.PodChannel, "Its channel", 99);
        Add(CfgKey.PodOwner, "Its owner", 255);
        Add(CfgKey.PodOre, "Hopper material", 255);
        Add(CfgKey.PodOreUnits, "Hopper load", 100000);
    }

    // ---- What a pod holds ------------------------------------------------------------------------------------------

    /// <summary>The frame of the shell docked in this pod part, or -1 (none, or not a pod).</summary>
    public static int FrameIn(in CPart p) => p.D.ShellPod == null ? -1 : (int)ConstructConfig.Get(p, p.D, CfgKey.PodShell, 0) - 1;

    public static bool Holds(in CPart p) => FrameIn(p) >= 0;

    public static int ChannelIn(in CPart p) => (int)ConstructConfig.Get(p, p.D, CfgKey.PodChannel, 1);

    public static byte OwnerIn(in CPart p) => (byte)ConstructConfig.Get(p, p.D, CfgKey.PodOwner, 255);

    /// <summary>A frame's size class by its standing height.</summary>
    public static int ClassOf(byte frame)
    {
        var h = World.PlayerHeight * ShellFrames.ScaleOf(frame);
        for (var k = 0; k < ClassHeights.Length; k++)
            if (h <= ClassHeights[k] + 1e-3f)
                return k;
        return ClassHeights.Length;
    }

    /// <summary>Does a shell of <paramref name="frame"/> go in a pod of this kind (its class or larger: an assumption, the user's rule)?</summary>
    public static bool Fits(ShellPodDef pd, byte frame) => ClassOf(frame) <= pd.Class;

    /// <summary>Why a shell of <paramref name="frame"/> doesn't go in the pod, or null.</summary>
    public static string? FitRefusal(ConstructPartDef d, byte frame) =>
        d.ShellPod is not { } pd ? "that isn't a shell pod"
        : Fits(pd, frame) ? null
        : $"a {ShellFrames.Of(frame).Part} ({World.PlayerHeight * ShellFrames.ScaleOf(frame):0.#} m) needs a {ClassNames[Math.Min(ClassOf(frame), ClassNames.Length - 1)]} pod or larger";

    // ---- Where things are -------------------------------------------------------------------------------------------

    /// <summary>
    /// A pod's lid and frame in the world: the lid's middle, the normal the frame rises along (out of the surface), how it is mounted
    /// (0 a floor, 1 a wall, 2 a ceiling, by the normal against the gravity's up there) and the up there.
    /// </summary>
    public static void Lid(Construct c, int part, ShellPodDef pd, out Vector3 lid, out Vector3 n, out int mount, out Vector3 up, out Quaternion rot)
    {
        Freeform.PartWorld(c, part, out var pos, out rot);
        n = Vector3.Transform(Vector3.UnitY, rot);
        lid = pos + n * pd.Top;
        up = GravityFrames.UpAt(lid);
        var d = Vector3.Dot(n, up);
        mount = d > 0.7f ? 0 : d < -0.7f ? 2 : 1;
    }

    /// <summary>
    /// Where a shell of <paramref name="frame"/> stands in a pod's frame (its feet, in the world) and which way it faces: upright on
    /// the lid of a floor pod; hung under a ceiling pod with its head at the lid; in front of a wall pod, its back to the wall, its
    /// middle level with the pod's. Also the frame's length out of the lid.
    /// </summary>
    public static Vector3 StandFeet(Construct c, int part, byte frame, out Vector3 fwd, out float length)
    {
        var pd = c.DefOf(part).ShellPod!;
        Lid(c, part, pd, out var lid, out var n, out var mount, out var up, out var rot);
        var k = ShellFrames.ScaleOf(frame);
        var h = World.PlayerHeight * k;
        var r = World.PlayerRadius * k;
        var front = Vector3.Transform(-Vector3.UnitZ, rot);
        fwd = mount == 1 ? n : front - up * Vector3.Dot(front, up);
        if (fwd.LengthSquared() < 1e-4f)
            fwd = Vector3.Cross(up, Vector3.UnitX);
        fwd = Vector3.Normalize(fwd - up * Vector3.Dot(fwd, up));
        switch (mount)
        {
            case 0:
                length = h + 0.12f;
                return lid + up * 0.02f;
            case 2:
                length = h + 0.12f;
                return lid - up * (h + 0.04f);
            default:
                length = 2 * r + 0.3f;
                return lid + n * (r + 0.15f) - up * (h * 0.5f);
        }
    }

    /// <summary>The frame's yaw for a forward direction (a player's yaw: it turns -Z towards +X).</summary>
    public static float YawOf(Vector3 fwd) => MathF.Atan2(fwd.X, -fwd.Z);

    /// <summary>
    /// Is a body whose middle is at <paramref name="mid"/> in this pod's frame, as its wearer must be to dock: within the frame's column
    /// (beside a bolted-on floor pod counts, so a shell need not climb onto it) and no further out than the frame reaches.
    /// </summary>
    public static bool InFrame(Construct c, int part, byte frame, Vector3 mid)
    {
        var pd = c.DefOf(part).ShellPod!;
        Lid(c, part, pd, out var lid, out var n, out _, out _, out _);
        StandFeet(c, part, frame, out _, out var length);
        var k = ShellFrames.ScaleOf(frame);
        var rel = mid - lid;
        var along = Vector3.Dot(rel, n);
        var side = (rel - n * along).Length();
        return side <= pd.Half + World.PlayerRadius * k + 0.6f && along >= -pd.Depth - 0.3f && along <= length + 0.6f;
    }

    // ---- Moving a shell in and out ---------------------------------------------------------------------------------

    /// <summary>Server: keep a shell in pod part <paramref name="part"/> of <paramref name="c"/> (its wearer just left it there).</summary>
    public static void Store(ConstructWorld world, Construct c, int part, byte frame, int channel, byte owner, in ShellCond wear, ReadOnlySpan<byte> kit, int health, byte ore, int oreUnits)
    {
        ref var pp = ref c.Parts[part];
        var d = pp.D;
        ConstructConfig.Put(ref pp, d, CfgKey.PodShell, frame + 1);
        ConstructConfig.Put(ref pp, d, CfgKey.PodChannel, channel);
        ConstructConfig.Put(ref pp, d, CfgKey.PodOwner, owner);
        ShellWear.Write(ref pp, d, wear);
        ShellFrames.WriteKit(ref pp, d, kit);
        ShellWear.PutHealth(ref pp, d, health);
        ConstructConfig.Put(ref pp, d, CfgKey.PodOre, oreUnits > 0 ? ore : 0);
        ConstructConfig.Put(ref pp, d, CfgKey.PodOreUnits, Math.Max(0, oreUnits));
        world.StructureDirty.Add(c.Id); // (peers hear the pod holds it: CBody)
    }

    /// <summary>Server: what a pod holds, read out of its settings (so it can stand after the pod is gone).</summary>
    public static DockedShell Read(in CPart pp)
    {
        var d = pp.D;
        var s = new DockedShell { Frame = (byte)Math.Max(0, FrameIn(pp)), Channel = ChannelIn(pp), Owner = OwnerIn(pp), Health = ShellWear.HealthOf(pp, d),
            Ore = (byte)ConstructConfig.Get(pp, d, CfgKey.PodOre, 0), OreUnits = (int)ConstructConfig.Get(pp, d, CfgKey.PodOreUnits, 0) };
        ShellWear.Read(pp, d, ref s.Wear);
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        ShellFrames.ReadKit(pp, d, kit);
        for (var k = 0; k < ShellFrames.SlotCount; k++)
            s.Kit |= (ulong)kit[k] << (8 * k);
        return s;
    }

    /// <summary>
    /// Server: the docked shell stands again as its part at <paramref name="feet"/>, as it was left (channel, owner, wear, modules,
    /// damage, the hopper's load in its bin), and the pod is empty. Null: it holds none, or the world is full (it stays docked).
    /// </summary>
    public static Construct? TakeOut(ConstructWorld world, Construct c, int part, Vector3 feet, float yaw)
    {
        ref var pp = ref c.Parts[part];
        if (!Holds(pp))
            return null;
        var s = Read(pp);
        if (Stand(world, s, feet, yaw, c.DefOf(part).Name) is not { } stood)
            return null;
        Clear(ref pp, pp.D);
        world.StructureDirty.Add(c.Id);
        return stood;
    }

    /// <summary>Server: a docked shell stands as its part (its settings and load as kept). Null: its part isn't loaded or the world is full.</summary>
    public static Construct? Stand(ConstructWorld world, in DockedShell s, Vector3 feet, float yaw, string podName)
    {
        if (ConstructParts.Find(ShellFrames.Of(s.Frame).Part) is not { } sd)
        {
            world.Events.Add($"[pod] REFUSED: a {podName} holds a frame {s.Frame} shell whose part \"{ShellFrames.Of(s.Frame).Part}\" isn't loaded: it stays docked");
            return null;
        }
        if (ConstructBuild.PlaceNew(world, sd, feet, yaw) is not { } stood)
            return null;
        ref var sp = ref stood.Parts[0];
        ShellWear.Write(ref sp, sd, s.Wear);
        Span<byte> kit = stackalloc byte[ShellFrames.SlotCount];
        for (var k = 0; k < ShellFrames.SlotCount; k++)
            kit[k] = (byte)(s.Kit >> (8 * k));
        ShellFrames.WriteKit(ref sp, sd, kit);
        ShellWear.PutHealth(ref sp, sd, s.Health);
        ConstructConfig.Put(ref sp, sd, CfgKey.Channel, s.Channel);
        stood.Owner = s.Owner;
        stood.Rebuild();
        if (s.OreUnits > 0 && s.Ore != 0)
        {
            var left = s.OreUnits - world.Deposit(stood, 0, s.OreUnits, s.Ore);
            if (left > 0)
                world.Events.Add($"[pod] {left} {RecyclingTable.Current.NameOf(s.Ore)} out of a {podName} found no hopper to go into: lost");
        }
        world.StructureDirty.Add(stood.Id);
        return stood;
    }

    private static void Clear(ref CPart pp, ConstructPartDef d)
    {
        foreach (var k in (ReadOnlySpan<CfgKey>)[CfgKey.PodShell, CfgKey.PodOre, CfgKey.PodOreUnits, CfgKey.ShellWear0, CfgKey.ShellWear1, CfgKey.ShellWear2, CfgKey.ShellWear3, CfgKey.ShellDamage, CfgKey.ShellKit0, CfgKey.ShellKit1, CfgKey.ShellKit2])
            ConstructConfig.Put(ref pp, d, k, 0);
    }

    /// <summary>
    /// Server: a pod that is going (taken off, broken, wrecked) lets its shell out first, so a docked shell is never lost with its
    /// pod (ConstructWorld.RemovePart, RemoveParts, SpillAll and a break call this). The shell is read out of the pod and stands
    /// at the pod's frame on the next pod tick (<see cref="ConstructWorld.PodReleases"/>: these run inside other walks of the
    /// bodies, where a new body may not be made). Nothing when it holds none.
    /// </summary>
    public static void Release(ConstructWorld world, Construct c, int part)
    {
        if ((uint)part >= (uint)c.PartCount || !Holds(c.Parts[part]))
            return;
        ref var pp = ref c.Parts[part];
        var s = Read(pp);
        var feet = StandFeet(c, part, s.Frame, out var fwd, out _);
        var name = pp.D.Name;
        Clear(ref pp, pp.D);
        world.StructureDirty.Add(c.Id);
        if (world.PodReleaseCount >= world.PodReleases.Length)
        {
            world.Events.Add($"[pod] construct {c.Id}: its {name} went with a shell in it and too many were waiting to stand: the shell is lost");
            return;
        }
        world.PodReleases[world.PodReleaseCount++] = (s, feet, YawOf(fwd));
        world.Events.Add($"[pod] construct {c.Id}: its {name} went with a shell in it: the shell stands where its frame was");
    }

    // ---- Jobs -------------------------------------------------------------------------------------------------------

    /// <summary>The job at pod (<paramref name="body"/>, <paramref name="part"/>), or -1.</summary>
    public static int JobAt(ConstructWorld world, ushort body, int part)
    {
        for (var k = 0; k < world.PodJobCount; k++)
            if (world.PodJobs[k].Body == body && world.PodJobs[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>
    /// A pod's pose at <paramref name="t"/> seconds into a job: the frame's extension (0 shut, 1 out), the platform's lift (0 at the
    /// pod's floor, 1 at the lid), the fold (0 standing, 1 folded) and how far a docked shell has slid from where it stood into the frame.
    /// </summary>
    public static void Pose(byte phase, double t, out float extend, out float lift, out float fold, out float slide)
    {
        static float S(double x) { var f = (float)Math.Clamp(x, 0, 1); return f * f * (3 - 2 * f); }
        if (phase == Dock)
        {
            slide = S(t / 1.0);
            fold = S(t / FoldEnd);
            extend = lift = 1 - S((t - FoldEnd) / (DockSeconds - FoldEnd));
        }
        else
        {
            slide = 1;
            extend = lift = S(t / RiseEnd);
            fold = 1 - S((t - RiseEnd) / (WakeSeconds - RiseEnd));
        }
    }

    // ---- What a player sees -----------------------------------------------------------------------------------------

    /// <summary>The look line: what it holds and takes, what it is doing.</summary>
    public static string Look(ConstructWorld world, Construct c, int part, bool inShell)
    {
        ref var pp = ref c.Parts[part];
        var d = pp.D;
        var pd = d.ShellPod!;
        var j = JobAt(world, c.Id, part);
        var frame = FrameIn(pp);
        var state = j >= 0 ? (world.PodJobs[j].Phase == Dock ? "folding a shell away" : "unfolding its shell")
            : frame >= 0 ? $"holds a {ShellFrames.Of((byte)frame).Part} (channel {ChannelIn(pp)}): wake in it from a Shell Control Chair"
            : $"empty: takes a robot shell up to {ClassHeights[pd.Class]:0} m tall";
        string? problem = pp.Broken ? "wrecked" : null;
        string? act = inShell && frame < 0 && j < 0 ? SandboxKeys.Do(SandboxKeys.Use, "dock (stand in its frame)") : null;
        return SandboxKeys.Line(d.Name, state, problem, act);
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>The shell pods' folds and unfolds under way (ConstructShellPod.cs): the server's own; a client's as heard. A fixed array: nothing allocates.</summary>
    public readonly PodJob[] PodJobs = new PodJob[ConstructShellPod.MaxJobs];
    public int PodJobCount;
    public readonly (DockedShell Shell, Vector3 Feet, float Yaw)[] PodReleases = new (DockedShell, Vector3, float)[16]; // shells out of pods that went, to stand next pod tick (GameShellPod.cs)
    public int PodReleaseCount;
}

public sealed partial class Construct
{
    public ushort[] Pods = []; // its shell pods (made with the caches: ConstructConveyor.BuildRacks), for drawing their frames and the pods' tick
    public int PodCount;
}
