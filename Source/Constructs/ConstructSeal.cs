using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What an open box costs one kind of volume (a kind's block in Constructs/Sealing.json).</summary>
public sealed class SealRules
{
    public float OpenLoss, PourShare, PourMin, Margin, AccelMin, AccelScale = 1, BurnShare;
    public string[] Flammable = [];
    public bool[] Burns = []; // by Fluid (a tank) or material id (storage)

    /// <summary>The share of its capacity a box with these faces sealed holds.</summary>
    public float Share(byte mask) => 1f - OpenLoss * ConstructSeal.Open(mask) / 6f;
}

/// <summary>The sealing table (Constructs/Sealing.json), loaded once; the defaults when the file is missing or refused.</summary>
public sealed class SealingTable
{
    public const string Format = "call-of-slop-sealing/1";
    public float Spacing = 1f, Reach = 0.35f, Cover = 0.9f;
    public int GridMin = 3, GridMax = 6;
    public SealRules Tank = new() { OpenLoss = 0.5f, PourShare = 0.04f, PourMin = 20, Margin = 0.02f, AccelMin = 2, BurnShare = 0.02f, Flammable = ["fuel", "oil"] };
    public SealRules Storage = new() { OpenLoss = 0.1f, PourShare = 0.004f, PourMin = 10, Margin = 0.25f, AccelMin = 5, AccelScale = 0.6f, BurnShare = 0.005f, Flammable = ["wood", "planks", "cloth", "rubber"] };
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Sealing.json");

    private static SealingTable? _current;

    public static SealingTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (the self-test; null: load the file again).</summary>
    public static void Use(SealingTable? t) => _current = t;

    /// <summary>A table with no penalties: a self-test about something else, in an open box.</summary>
    public static SealingTable Off => Finish(new SealingTable { Tank = new SealRules { Margin = 100f }, Storage = new SealRules { Margin = 100f } });

    public static SealingTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ConstructPartException or InvalidOperationException)
        {
            Console.WriteLine($"[constructs] {path}: {ex.Message}: the defaults stand");
            return Finish(new SealingTable { Problem = ex.Message });
        }
    }

    public static SealingTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (ConstructParts.Opt(root, "format")?.GetString() != Format)
            throw new ConstructPartException($"\"format\" must be \"{Format}\"");
        ConstructParts.Known(root, ["format", "about", "spacing", "gridMin", "gridMax", "reach", "cover", "tank", "storage"], "Sealing.json");
        var t = new SealingTable();
        float Num(JsonElement e, string key, float now, float lo, float hi)
        {
            if (ConstructParts.Opt(e, key) is not { } v)
                return now;
            var n = v.GetSingle();
            if (!(n >= lo && n <= hi))
                throw new ConstructPartException($"\"{key}\" is {lo} to {hi}");
            return n;
        }
        t.Spacing = Num(root, "spacing", t.Spacing, 0.1f, 40);
        t.GridMin = (int)Num(root, "gridMin", t.GridMin, 1, 8);
        t.GridMax = (int)Num(root, "gridMax", t.GridMax, t.GridMin, 8);
        t.Reach = Num(root, "reach", t.Reach, 0.01f, 4);
        t.Cover = Num(root, "cover", t.Cover, 0.05f, 1);
        void Kind(string key, SealRules r)
        {
            if (ConstructParts.Opt(root, key) is not { } e)
                return;
            ConstructParts.Known(e, ["openLoss", "pourShare", "pourMin", "margin", "accelMin", "accelScale", "burnShare", "flammable"], $"Sealing.json, {key}");
            r.OpenLoss = Num(e, "openLoss", r.OpenLoss, 0, 1);
            r.PourShare = Num(e, "pourShare", r.PourShare, 0, 1);
            r.PourMin = Num(e, "pourMin", r.PourMin, 0, 100000);
            r.Margin = Num(e, "margin", r.Margin, 0, 100);
            r.AccelMin = Num(e, "accelMin", r.AccelMin, 0, 1000);
            r.AccelScale = Num(e, "accelScale", r.AccelScale, 0, 10);
            r.BurnShare = Num(e, "burnShare", r.BurnShare, 0, 1);
            if (ConstructParts.Opt(e, "flammable") is { } f)
                r.Flammable = f.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        }
        Kind("tank", t.Tank);
        Kind("storage", t.Storage);
        return Finish(t);
    }

    private static SealingTable Finish(SealingTable t)
    {
        t.Tank.Burns = new bool[ConstructFluid.Options.Length];
        foreach (var n in t.Tank.Flammable)
        {
            var k = Array.IndexOf(ConstructFluid.Options, n);
            if (k <= 0)
                throw new ConstructPartException($"\"{n}\" isn't a fluid");
            t.Tank.Burns[k - 1] = true;
        }
        var names = RecyclingTable.Current;
        t.Storage.Burns = new bool[256];
        foreach (var n in t.Storage.Flammable)
            t.Storage.Burns[names.IdOf(n)] = true;
        return t;
    }
}

public sealed partial class Construct
{
    public ushort[] SealVolumes = []; // its Tank and Storage Volumes
    public float[] SealPour = []; // what each has on its way out (litres or units)
    public int SealCount;
    public byte SealWait; // load ticks a box just resized waits before it counts
    private float _sealJoints; // the moving joints as they stood at the last check
}

/// <summary>
/// Sealed volumes (docs/sandbox/CONSTRUCTS.md, "Sealed volumes"). Whether a Tank or Storage Volume's six faces are closed by the
/// construct's own solids is worked out when the construct changes, into <see cref="CPart.SealedFaces"/> (bit f: face f of
/// <see cref="ConstructCargo.FaceAt"/> is sealed), on the server and each client alike, so nothing goes on the wire. Every penalty
/// reads the mask, at the bins' 4 Hz. Nothing here allocates; a construct with no such volume costs one check four times a second.
/// </summary>
public static class ConstructSeal
{
    public const byte AllSealed = 63;
    private const int Every = 15; // ticks: 4 Hz
    private const float Tilted = 0.5f; // m/s2: no apparent gravity to speak of, nothing falls out

    public static int Open(byte mask) => 6 - System.Numerics.BitOperations.PopCount((uint)(mask & AllSealed));

    public static SealRules Rules(ConstructPartDef d) => d.IsTankVolume ? SealingTable.Current.Tank : SealingTable.Current.Storage;

    /// <summary>"2 faces open: holds 83%", or null when sealed.</summary>
    public static string? Note(byte mask, SealRules r)
    {
        var n = Open(mask);
        return n == 0 ? null : $"{n} {(n == 1 ? "face" : "faces")} open: holds {(int)MathF.Round(r.Share(mask) * 100)}%";
    }

    // ---- The check ----------------------------------------------------------------------------------------------------

    /// <summary>Rebuilt with the caches: which parts are volumes, and each one's mask.</summary>
    public static void Build(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.IsSizedBox)
                n++;
        if (n > c.SealVolumes.Length)
        {
            c.SealVolumes = new ushort[n + 2];
            c.SealPour = new float[n + 2];
        }
        c.SealCount = 0;
        if (n == 0)
            return;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.IsSizedBox)
            {
                c.SealVolumes[c.SealCount] = (ushort)i;
                c.SealPour[c.SealCount++] = 0;
            }
        Check(c);
    }

    private static void Check(Construct c)
    {
        for (var k = 0; k < c.SealCount; k++)
            c.Parts[c.SealVolumes[k]].SealedFaces = Faces(c, c.SealVolumes[k]);
        c.SealJointsSeen();
    }

    /// <summary>Which faces of this volume's box the construct's solids close.</summary>
    public static byte Faces(Construct c, int part)
    {
        var table = SealingTable.Current;
        ref var p = ref c.Parts[part];
        ConstructCargo.Settings(p, p.D, out var size, out var at);
        ConstructCargo.BoxFrom(p, size, at, out var centre, out var half);
        Span<ulong> covered = stackalloc ulong[6];
        Span<int> nu = stackalloc int[6];
        Span<int> nv = stackalloc int[6];
        Span<int> axis = stackalloc int[6];
        Span<float> plane = stackalloc float[6];
        for (var f = 0; f < 6; f++)
        {
            var n = Rot24.Apply(p.Rot, FaceNormal(f)); // (face f in the part's frame, this axis in its segment's)
            axis[f] = MathF.Abs(n.X) > 0.5f ? 0 : MathF.Abs(n.Y) > 0.5f ? 1 : 2;
            plane[f] = centre[axis[f]] + MathF.Sign(n[axis[f]]) * half[axis[f]];
            nu[f] = Grid(table, half[(axis[f] + 1) % 3] * 2);
            nv[f] = Grid(table, half[(axis[f] + 2) % 3] * 2);
        }
        var reachBox = half + new Vector3(table.Reach);
        c.SegPose(p.Seg, out var vq, out var vt);
        var iq = Quaternion.Conjugate(vq);
        for (var i = 0; i < c.PartCount; i++)
        {
            var solids = c.SolidCount(i);
            if (solids == 0)
                continue;
            var same = c.Parts[i].Seg == p.Seg;
            Quaternion oq = default;
            Vector3 ot = default;
            if (!same)
                c.SegPose(c.Parts[i].Seg, out oq, out ot);
            for (var b = 0; b < solids; b++)
            {
                c.SolidBox(i, b, out var sc, out var sh);
                if (!same)
                {
                    // Another segment, where it stands now, in this volume's frame (a box round a turned box).
                    sc = Vector3.Transform(ot + Vector3.Transform(sc, oq) - vt, iq);
                    var m = Matrix4x4.CreateFromQuaternion(iq * oq);
                    sh = new Vector3(
                        MathF.Abs(m.M11) * sh.X + MathF.Abs(m.M21) * sh.Y + MathF.Abs(m.M31) * sh.Z,
                        MathF.Abs(m.M12) * sh.X + MathF.Abs(m.M22) * sh.Y + MathF.Abs(m.M32) * sh.Z,
                        MathF.Abs(m.M13) * sh.X + MathF.Abs(m.M23) * sh.Y + MathF.Abs(m.M33) * sh.Z);
                }
                var gap = Vector3.Abs(sc - centre) - sh - reachBox;
                if (gap.X > 0 || gap.Y > 0 || gap.Z > 0)
                    continue;
                for (var f = 0; f < 6; f++)
                    if (MathF.Abs(sc[axis[f]] - plane[f]) <= sh[axis[f]] + table.Reach)
                        covered[f] |= Covers(centre, half, sc, sh, axis[f], nu[f], nv[f]);
            }
        }
        byte mask = 0;
        for (var f = 0; f < 6; f++)
            if (System.Numerics.BitOperations.PopCount(covered[f]) >= table.Cover * nu[f] * nv[f] - 1e-3f)
                mask |= (byte)(1 << f);
        return mask;
    }

    private static Vector3 FaceNormal(int face)
    {
        var n = Vector3.Zero;
        n[face >> 1] = (face & 1) == 0 ? 1 : -1;
        return n;
    }

    private static int Grid(SealingTable t, float side) => Math.Clamp((int)MathF.Ceiling(side / t.Spacing) + 1, t.GridMin, t.GridMax);

    /// <summary>The grid points of a face that a solid covers, a bit each.</summary>
    private static ulong Covers(Vector3 centre, Vector3 half, Vector3 sc, Vector3 sh, int axis, int nu, int nv)
    {
        int u = (axis + 1) % 3, v = (axis + 2) % 3;
        ulong bits = 0;
        for (var a = 0; a < nu; a++)
        {
            var pu = centre[u] - half[u] + (a + 0.5f) / nu * half[u] * 2;
            if (MathF.Abs(pu - sc[u]) > sh[u])
                continue;
            for (var b = 0; b < nv; b++)
            {
                var pv = centre[v] - half[v] + (b + 0.5f) / nv * half[v] * 2;
                if (MathF.Abs(pv - sc[v]) <= sh[v])
                    bits |= 1UL << (a * nv + b);
            }
        }
        return bits;
    }

    // ---- Each 4 Hz tick -----------------------------------------------------------------------------------------------

    /// <summary>A joint moved a door or a wall: the masks again, on either side of the wire; the server's penalties follow.</summary>
    public static void Step(ConstructWorld world, IWorldQuery? query, ref int ticks, bool server)
    {
        if (++ticks < Every)
            return;
        ticks = 0;
        world.SealTick++;
        var bodies = world.Bodies;
        for (var n = 0; n < bodies.Count; n++)
        {
            var c = bodies[n];
            if (c.SealCount == 0 || c.Dirty)
                continue;
            if (c.MovingJoints > 0 && c.SealJointsMoved())
                Check(c);
            for (var k = 0; k < c.SealCount; k++)
                if (c.SealVolumes[k] < c.PartCount && c.Parts[c.SealVolumes[k]].D.IsVolumeStore)
                    ConstructStoreVolume.Follow(world, c, c.SealVolumes[k]);
            if (c.SealWait > 0)
            {
                c.SealWait--;
                continue;
            }
            if (!server || query == null)
                continue;
            for (var k = 0; k < c.SealCount; k++)
                Volume(world, query, c, k, Every / (float)World.TickRate);
        }
    }

    private static void Volume(ConstructWorld world, IWorldQuery query, Construct c, int k, float dt)
    {
        int part = c.SealVolumes[k];
        if (part >= c.PartCount || !c.Parts[part].D.IsSizedBox)
            return;
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (p.Broken || p.SealedFaces == AllSealed)
            return;
        var r = Rules(d);
        var st = d.IsTankVolume ? null : world.StoreOf(c, part);
        ConstructCargo.Settings(p, d, out var size, out var at);
        float amount, total;
        if (d.IsTankVolume)
        {
            amount = ConstructFluid.Litres(p, d);
            total = ConstructFluid.Capacity(p, d);
        }
        else
        {
            amount = st?.Amount ?? 0; // (parts in crates stay: only bulk runs out)
            ConstructStoreVolume.Usable(c, part, out var bmin, out var bmax);
            total = ConstructStoreVolume.UnitsFor(bmin, bmax);
        }
        if (amount <= 0)
            return;
        if (p.Burn > 0)
            Burn(world, c, part, r, total, dt);
        var over = amount - total * r.Share(p.SealedFaces);
        // Apparent gravity: what the world pulls with, less what the body is doing.
        var centreWorld = ConstructCargo.FromPart(c, part, at);
        var gravity = ConstructGravity.GravityFor(c, centreWorld);
        var accel = c.Accel.Length() >= r.AccelMin ? c.Accel * r.AccelScale : Vector3.Zero;
        var apparent = gravity - accel;
        var pull = apparent.Length();
        var down = pull > 0.01f ? apparent / pull : -Vector3.UnitY;
        var lip = centreWorld;
        var lipFound = false;
        var lipLow = float.MaxValue;
        float hmin = float.MaxValue, hmax = float.MinValue;
        for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var h = -Vector3.Dot(ConstructCargo.FromPart(c, part, at + size * 0.5f * new Vector3(x, y, z)), down);
                    hmin = MathF.Min(hmin, h);
                    hmax = MathF.Max(hmax, h);
                }
        var level = hmin + (hmax - hmin) * (Math.Clamp(amount / total, 0, 1) - r.Margin);
        for (var f = 0; f < 6; f++)
        {
            if ((p.SealedFaces >> f & 1) != 0)
                continue;
            int axis = f >> 1, u = (axis + 1) % 3, v = (axis + 2) % 3;
            var mid = ConstructCargo.FaceAt(size, at, f);
            for (var a = -1; a <= 1; a += 2)
                for (var b = -1; b <= 1; b += 2)
                {
                    var corner = mid;
                    corner[u] += a * size[u] * 0.5f;
                    corner[v] += b * size[v] * 0.5f;
                    var w = ConstructCargo.FromPart(c, part, corner);
                    var h = -Vector3.Dot(w, down);
                    if (h < lipLow)
                    {
                        lipLow = h;
                        lip = w;
                        lipFound = true;
                    }
                }
        }
        var tipped = pull >= Tilted && lipFound && lipLow < level;
        if (over <= 0 && !tipped)
        {
            c.SealPour[k] = 0;
            return;
        }
        var rate = MathF.Max(r.PourMin, total * r.PourShare) * dt;
        var want = MathF.Min(amount, tipped ? rate : MathF.Min(rate, over));
        // Clear of the box's rim and whatever stands round it, sideways as it falls.
        var side = lip - centreWorld;
        side -= down * Vector3.Dot(side, down);
        var from = lip + (side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) * 0.45f : Vector3.Zero) + down * 0.1f;
        if (d.IsTankVolume)
            ConstructFluid.Pour(world, c, part, want, from, down);
        else if (st != null)
        {
            c.SealPour[k] += want;
            ConstructScoops.PourVolume(world, query, c, part, st, from, down, ref c.SealPour[k]);
        }
        world.SpillFx(c, from);
    }

    // ---- Fire ---------------------------------------------------------------------------------------------------------

    /// <summary>Is this volume open, and is what it holds what burns?</summary>
    public static bool Exposed(ConstructWorld world, Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!d.IsSizedBox || p.SealedFaces == AllSealed || p.Broken)
            return false;
        if (d.IsTankVolume)
            return ConstructFluid.Held(p, d) is { } f && SealingTable.Current.Tank.Burns[(int)f];
        return world.StoreOf(c, part) is { Amount: > 0 } st && SealingTable.Current.Storage.Burns[st.Material];
    }

    /// <summary>Fire landed at <paramref name="at"/> on this construct: any open box with something that burns, at that spot, catches.</summary>
    public static void IgniteAt(ConstructWorld world, Construct c, Vector3 at)
    {
        for (var k = 0; k < c.SealCount; k++)
        {
            int part = c.SealVolumes[k];
            if (part >= c.PartCount || !c.Parts[part].D.IsSizedBox)
                continue;
            ConstructCargo.Settings(c.Parts[part], c.Parts[part].D, out var size, out var box);
            var local = ConstructCargo.ToPart(c, part, at) - box;
            var e = Vector3.Abs(local) - size * 0.5f - new Vector3(SealingTable.Current.Reach);
            if (e.X <= 0 && e.Y <= 0 && e.Z <= 0 && Exposed(world, c, part))
                world.Ignite(c, part);
        }
    }

    /// <summary>A fire in an open box eats what it holds, and lasts as long as that does.</summary>
    private static void Burn(ConstructWorld world, Construct c, int part, SealRules r, float total, float dt)
    {
        if (!Exposed(world, c, part))
            return;
        var eaten = MathF.Max(1f, total * r.BurnShare * dt);
        ref var p = ref c.Parts[part];
        if (p.D.IsTankVolume)
            ConstructFluid.Pour(world, c, part, eaten, null);
        else
            world.Withdraw(c, part, (int)MathF.Ceiling(eaten));
        p.Burn = (ushort)Math.Max((int)p.Burn, (int)(ConstructWeapons.BurnSeconds * World.TickRate * 0.6f));
    }
}
