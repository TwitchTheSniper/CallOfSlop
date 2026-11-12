using System.Globalization;
using System.Numerics;
using Brutal.RakNetApi;
using CallOfSlop.EngineKit;

namespace CallOfSlop;

/// <summary>
/// Creative "Make an engine" (docs/vehiclekit/ENGINEKIT.md, "Callers"): a player asks for an engine by power, tech tier, layout, booster,
/// size and the bay it must fit (the inventory's Engine tab, UiSandboxEngine.cs); the host checks and clamps every field, queues an
/// Engine Kit request on the vehicle spawner (made in the generation's slices, never in one tick) and sets the engine down in front
/// of the player as a free construct. Clients never run the kit: the engine arrives as ordinary bodies. Survival gets nothing yet.
/// </summary>
public sealed partial class Game
{
    /// <summary>The most power a request may ask for (kW): a Medium W21 of tier 5 and then some; the wire's u16 holds it.</summary>
    public const int MakeEngineMaxKw = 50_000;

    /// <summary>A fit's side is 0 (any) or this many metres at least and <see cref="MakeEngineMaxFit"/> at most.</summary>
    public const float MakeEngineMinFit = 0.3f, MakeEngineMaxFit = 100f;

    /// <summary>Client (or the host): ask for an engine in front of you. <paramref name="size"/> -1: any; <paramref name="fit"/> length, width, height (m; 0: any).</summary>
    public void SandboxMakeEngine(int kw, int tier, EngineLayout layout, EngineBoost boost, int size, Vector3 fit, int seed, bool ghost = false)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.MakeEngine).U16((ushort)Math.Clamp(kw, 0, ushort.MaxValue)).U8((byte)Math.Clamp(tier, 0, 255))
            .U8((byte)layout).U8((byte)boost).U8((byte)(size < 0 ? 255 : Math.Min(size, 254)))
            .U16(FitCm(fit.X)).U16(FitCm(fit.Y)).U16(FitCm(fit.Z)).U32((uint)Math.Max(0, seed));
        if (ghost)
        {
            SpawnPrepareSend(w, $"engine of {kw} kW"); // (placed with a ghost: GameSandboxBlueprintGhost.cs)
            return;
        }
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxActGuarded(me, ref r) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    private static ushort FitCm(float m) => (ushort)Math.Clamp(MathF.Round(float.IsFinite(m) ? m * 100f : 0f), 0, ushort.MaxValue);

    /// <summary>Server: CActKind.MakeEngine (after the kind byte). Every field is checked; a refusal says why.</summary>
    private string? SandboxMakeEngineAct(Player p, ref PacketReader r)
    {
        int kw = r.U16(), tier = r.U8(), layout = r.U8(), boost = r.U8(), size = r.U8();
        float fx = r.U16() / 100f, fy = r.U16() / 100f, fz = r.U16() / 100f;
        var seed = (int)Math.Min(r.U32(), (uint)int.MaxValue);
        if (!SandboxCreativeSpawns)
            return "engines are made only in creative worlds";
        if (SandboxMenuSpawnRefusal(p) is { } no)
            return no;
        if (kw < 1)
            return "ask for some power (1 kW or more)";
        if (kw > MakeEngineMaxKw)
            return $"at most {MakeEngineMaxKw} kW";
        if (tier is < 1 or > ConstructParts.MaxTier)
            return $"a tier is 1 to {ConstructParts.MaxTier}";
        if (layout > (int)EngineLayout.Radial)
            return "no such layout";
        if (boost > (int)EngineBoost.Turbo)
            return "no such booster";
        if (size != 255 && size >= EngineKit.EngineKit.SizeWords.Length)
            return "no such size";
        if (FitBad(fx) || FitBad(fy) || FitBad(fz))
            return string.Create(CultureInfo.InvariantCulture, $"a bay's sides are 0 (any) or {MakeEngineMinFit:0.0} to {MakeEngineMaxFit:0} m");
        var ask = new EngineRequest
        {
            PowerKw = kw,
            Tier = tier,
            Layout = (EngineLayout)layout,
            Boost = (EngineBoost)boost,
            Size = size == 255 ? -1 : size,
            Fit = new Vector3(fx, fy, fz),
            Seed = seed,
        };
        var fwd = GravityFrames.FlatForward(p.Position, p.Yaw);
        var ground = ConstructWorld.Ground(World.Query, p.Position + fwd * 6f);
        var id = p.Id;
        if (VehicleSpawn.RequestEngine(ask, ground, p.Yaw, id, id, _engineMade ??= EngineMadeFor) is { } why)
            return why;
        SandboxHands.Notices.Add((id, VehicleSpawn.Pending > 1 ? $"making an engine of {kw} kW, tier {tier} ({VehicleSpawn.Pending - 1} ahead of it)" : $"making an engine of {kw} kW, tier {tier}"));
        return null;
    }

    private static bool FitBad(float m) => m != 0 && (m < MakeEngineMinFit || m > MakeEngineMaxFit);

    private VehicleSpawner.Finished? _engineMade;

    /// <summary>An engine is down (or isn't): the player who asked hears what it is, or why not.</summary>
    private void EngineMadeFor(Construct? c, string name, string why, byte who)
    {
        if (c == null)
        {
            Console.WriteLine($"[engines] {name} wasn't made: {why}");
            SandboxHands.Notices.Add((who, EngineText("no engine: " + why)));
            return;
        }
        var res = VehicleSpawn.EngineMade;
        Constructs.Events.Add($"player {who} made {name} as construct {c.Id} (Engine Kit)");
        Console.WriteLine($"[engines] made {(res != null ? res.ToString() : name)} as construct {c.Id}, {c.PartCount} parts");
        var text = res == null ? $"{name} is ready" : string.Create(CultureInfo.InvariantCulture,
            $"{name}: {res.Kw:0} kW, {res.Mass:0} kg, {res.Dims.X:0.0} x {res.Dims.Y:0.0} x {res.Dims.Z:0.0} m; it needs a fuel tank and a gearbox{(res.Note.Length > 0 ? "; " + res.Note : "")}");
        SandboxHands.Notices.Add((who, EngineText(text)));
    }

    /// <summary>Player text: ASCII only, and short enough for a notice.</summary>
    private static string EngineText(string s)
    {
        var b = new System.Text.StringBuilder(Math.Min(s.Length, 200));
        foreach (var ch in s)
        {
            if (b.Length >= 200)
                break;
            b.Append(ch is >= ' ' and <= '~' ? ch : '?');
        }
        return b.ToString();
    }
}
