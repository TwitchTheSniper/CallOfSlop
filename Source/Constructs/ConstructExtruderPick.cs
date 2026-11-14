using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What the Extruder's recipe picker sends (CActKind.ExtruderSection, protocol 284): its Profile choice, the section's width and height
/// in cm (a Round's or a Pipe's diameter is <see cref="W"/>), a Pipe's wall in cm, and a Custom section's cells.
/// </summary>
public readonly record struct ExtruderPick(byte Profile, ushort W, ushort H, ushort Wall, ulong Mask);

/// <summary>
/// The Extruder's recipe picker's rules (docs/sandbox/design/recipe-select-notes.md, option A; UiRecipePickerExtruder.cs draws it): which
/// machines it serves, the sizes this machine's die makes, why a pick is refused, and the settings a pick writes. The client clamps its
/// steppers with the same <see cref="DieCm"/> the server checks against, so a pick it lets you Set is one the server takes.
/// </summary>
public static class ExtruderPicks
{
    /// <summary>An Extruder of sections (not a Wheel, Panel, Fan or Airframe Former): its Profile, sizes and cells are the picker's.</summary>
    public static bool Pickable(ConstructPartDef d) =>
        d is { IsExtruder: true, IsWheelFormer: false } && d.CfgSlot[(int)CfgKey.Profile] >= 0 && d.CfgSlot[(int)CfgKey.Width] >= 0 && d.CfgSlot[(int)CfgKey.Height] >= 0;

    /// <summary>How many Profile choices it has (the presets, Custom, Round, Pipe and the foils).</summary>
    public static int Profiles(ConstructPartDef d) => d.Config[d.CfgSlot[(int)CfgKey.Profile]].Options.Length;

    /// <summary>The widest and tallest section this machine's die makes now (its Die Frames counted), in cm on the 5 cm step.</summary>
    public static (int W, int H) DieCm(Construct c, int part)
    {
        ConstructExtruder.Die(c, part, out _, out var size);
        static int Cm(float m) => Math.Clamp((int)MathF.Floor(m * 20 + 0.001f) * 5, Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
        return (Cm(size.X), Cm(size.Y));
    }

    /// <summary>The thickest wall a Pipe of <paramref name="across"/> cm takes (it leaves a bore of <see cref="Extrusion.MinBoreCm"/>).</summary>
    public static int MaxWallCm(int across) => Math.Max(1, Math.Min(Extrusion.MaxWallCm, (across - Extrusion.MinBoreCm) / 2));

    /// <summary>The machine's settings as a pick (sizes held to the die): what the picker opens on.</summary>
    public static ExtruderPick Current(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var (dw, dh) = DieCm(c, part);
        var profile = (int)ConstructConfig.Get(p, d, CfgKey.Profile, 0);
        static int Cm(float m) => (int)MathF.Floor(m * 20 + 0.001f) * 5;
        var w = Math.Clamp(Cm(ConstructConfig.Get(p, d, CfgKey.Width, 0.25f)), Extrusion.MinSectionCm, dw);
        var h = Math.Clamp(Cm(ConstructConfig.Get(p, d, CfgKey.Height, 0.25f)), Extrusion.MinSectionCm, dh);
        var wall = (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.Wall, 0.03f) * 100);
        var custom = (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskA, 0) | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskB, 0) << 16
            | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskC, 0) << 32 | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskD, 0) << 48;
        return new ExtruderPick((byte)Math.Clamp(profile, 0, Profiles(d) - 1), (ushort)w, (ushort)h, (ushort)Math.Clamp(wall, 1, Extrusion.MaxWallCm), custom);
    }

    /// <summary>A Profile choice's section on the grid, for drawing and for the Custom grid's start (a Round's or a Pipe's as it shows).</summary>
    public static ulong MaskOf(int profile, in ExtruderPick pick)
    {
        if (Extrusion.ShapeOf(profile) != ExShape.Section)
            return ConstructExtruder.RoundMaskOf(profile, pick.W / 100f, pick.Wall / 100f);
        if (profile < Extrusion.Presets.Length)
            return Extrusion.Presets[profile].Mask;
        var foil = profile - Extrusion.AirfoilProfile(0);
        if (foil >= 0 && foil < ExtrusionTable.Current.Airfoils.Length)
            return ExtrusionTable.Current.Airfoils[foil].Mask;
        return pick.Mask;
    }

    /// <summary>The piece a pick makes, in words: "L-angle 0.2 x 0.2 m", "Pipe 0.5 m, wall 3 cm".</summary>
    public static string Label(in ExtruderPick pick)
    {
        var shape = Extrusion.ShapeOf(pick.Profile);
        return Extrusion.Label(new ExtrusionKey(MaskOf(pick.Profile, pick), pick.W, shape == ExShape.Section ? pick.H : pick.W, 0, 0, true, shape, shape == ExShape.Pipe ? pick.Wall : (ushort)0));
    }

    /// <summary>Why machine <paramref name="part"/> can't take <paramref name="pick"/>, or null: the profile, the sizes against its die, a Pipe's wall, a Custom section's cells.</summary>
    public static string? Refusal(Construct c, int part, in ExtruderPick pick)
    {
        var d = c.DefOf(part);
        if (!Pickable(d))
            return $"a {d.Name} has no section to pick";
        if (pick.Profile >= Profiles(d))
            return $"the {d.Name} has no such profile";
        var shape = Extrusion.ShapeOf(pick.Profile);
        var foil = pick.Profile - Extrusion.AirfoilProfile(0);
        if (foil >= 0 && shape == ExShape.Section && foil >= ExtrusionTable.Current.Airfoils.Length)
            return $"the {d.Name} has no such profile";
        var (dw, dh) = DieCm(c, part);
        if (shape != ExShape.Section)
            dw = dh = Math.Min(dw, dh);
        if (pick.W % 5 != 0 || pick.H % 5 != 0 || pick.W < Extrusion.MinSectionCm || pick.H < Extrusion.MinSectionCm)
            return "sizes go in 5 cm steps from 0.1 m";
        if (pick.W > dw || shape == ExShape.Section && pick.H > dh)
            return $"the die makes at most {dw / 100f:0.##} x {dh / 100f:0.##} m: {ConstructExtruder.WideFix(d, pick.W, shape == ExShape.Section ? pick.H : pick.W)}";
        if (shape == ExShape.Pipe && (pick.Wall < 1 || pick.Wall > MaxWallCm(pick.W)))
            return $"a {pick.W / 100f:0.##} m pipe takes a wall of 1 to {MaxWallCm(pick.W)} cm";
        if (pick.Profile == Extrusion.Presets.Length && Extrusion.Refusal(pick.Mask) is { } cells)
            return cells;
        return null;
    }

    /// <summary>Writes a checked pick into the part's settings, as the configure tool would. True when anything changed.</summary>
    public static bool Apply(ref CPart p, ConstructPartDef d, in ExtruderPick pick)
    {
        var any = false;
        void Put(ref CPart q, CfgKey k, float v)
        {
            if (d.CfgSlot[(int)k] is var s and >= 0)
                any |= ConstructConfig.Set(ref q, d, s, v);
        }
        var shape = Extrusion.ShapeOf(pick.Profile);
        Put(ref p, CfgKey.Profile, pick.Profile);
        Put(ref p, CfgKey.Width, pick.W / 100f);
        Put(ref p, CfgKey.Height, (shape == ExShape.Section ? pick.H : pick.W) / 100f);
        if (shape == ExShape.Pipe)
            Put(ref p, CfgKey.Wall, pick.Wall / 100f);
        if (pick.Profile == Extrusion.Presets.Length)
        {
            Put(ref p, CfgKey.MaskA, (ushort)pick.Mask);
            Put(ref p, CfgKey.MaskB, (ushort)(pick.Mask >> 16));
            Put(ref p, CfgKey.MaskC, (ushort)(pick.Mask >> 32));
            Put(ref p, CfgKey.MaskD, (ushort)(pick.Mask >> 48));
        }
        return any;
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>
    /// Server: the Extruder's recipe picker (CActKind.ExtruderSection) sets machine <paramref name="machine"/>'s section and size at once,
    /// under a finger on its Recipe button's rule (reach, power, who may) and the die's sizes (<see cref="ExtruderPicks.Refusal"/>).
    /// </summary>
    public string? ExtruderSection(ConstructWorld world, byte player, Vector3 eye, ushort body, ushort machine, in ExtruderPick pick, string? refusal)
    {
        if (world.Get(body) is not { } c || machine >= c.PartCount)
            return "that machine is gone";
        ref var p = ref c.Parts[machine];
        var d = p.D;
        if (!ExtruderPicks.Pickable(d) || !HasRecipeButton(d))
            return $"a {d.Name} has no section to pick";
        if (Contested(c, player) is { } defended)
            return defended;
        if (refusal != null)
            return refusal;
        if (Vector3.Distance(eye, ReachPoint(c, machine, eye)) > ConstructConfig.Reach)
            return "too far away";
        if (p.Broken || !ConstructPower.On(c, machine))
            return $"the {d.Name} has no power";
        if (ExtruderPicks.Refusal(c, machine, pick) is { } why)
            return why;
        if (!ExtruderPicks.Apply(ref p, d, pick))
            return null;
        world.StructureDirty.Add(c.Id);
        c.Wake();
        Done.Add($"player {player}: section {ExtruderPicks.Label(pick)} on part {machine} of construct {c.Id}");
        return null;
    }
}
