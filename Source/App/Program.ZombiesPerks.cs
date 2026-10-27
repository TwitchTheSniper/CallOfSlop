using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Drawing the Zombies perk machines (2026-10-27): every perk has a machine prop of its own shape
/// (Props/Zombies/PerkMachines, tools/zombies/make_perk_machines.py), all with the same face: the sign panel at 1.62 m
/// (the game paints it in the perk's colour, lit and pulsing while the machine works), a lamp at each of its top
/// corners (blinking in turn) and a glow strip under it. Without machine props (Zombies Test) the machine's prop is
/// drawn without collision; a perk without a machine of its own stands as the plain Perk Machine with its colour
/// painted on its front. Client-side only and allocation-free.
/// </summary>
public static unsafe partial class Program
{
    private static Map? _perkPropsFor;
    private static readonly List<PropDef?> PerkProps = new();
    private static readonly List<float> PerkTops = new();

    /// <summary>How tall the plain Perk Machine stands; the machines' labels go over their tops (UiZombies).</summary>
    public const float PerkClassicTop = 2.1f;

    /// <summary>The prop perk machine <paramref name="i"/> stands as (looked up once per map; null if it's missing).</summary>
    private static PropDef? PerkProp(ZombiesMap zm, int i)
    {
        if (!ReferenceEquals(_perkPropsFor, World.Map) || PerkProps.Count != zm.PerkMachines.Count)
        {
            _perkPropsFor = World.Map;
            PerkProps.Clear();
            PerkTops.Clear();
            foreach (var m in zm.PerkMachines)
            {
                var d = World.Map.PropScope.Find(m.PropName.Length > 0 ? m.PropName : ZombiesMap.MachineProp.Perk);
                PerkProps.Add(d);
                PerkTops.Add(d != null ? MathF.Max(PerkClassicTop, d.Bounds.Center.Y + d.Bounds.Half.Y) : PerkClassicTop);
            }
        }
        return i >= 0 && i < PerkProps.Count ? PerkProps[i] : null;
    }

    /// <summary>The top of perk machine <paramref name="i"/> over its spot (metres).</summary>
    public static float PerkTopY(ZombiesMap zm, int i)
    {
        PerkProp(zm, i);
        return i >= 0 && i < PerkTops.Count ? PerkTops[i] : PerkClassicTop;
    }

    private static void DrawPerkMachines(ZombiesMap zm, double now)
    {
        var t = (float)now;
        for (var i = 0; i < zm.PerkMachines.Count; i++)
        {
            var m = zm.PerkMachines[i];
            var k = _game.ZRules.PerkIndex(m.Perk);
            var c = k >= 0 ? _game.ZRules.Perks[k].Color : new Vector3(0.6f);
            var lit = !m.NeedsPower || _game.ZPower;
            var place = Matrix4x4.CreateRotationY(-m.Yaw) * Matrix4x4.CreateTranslation(m.At);
            void Part(float sx, float sy, float sz, float x, float y, float z, Vector3 col, int mode = 0) =>
                Gfx.Draw(Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateTranslation(x, y, z) * place, col, 1f, mode);
            var own = m.PropName.Length > 0 && !m.PropName.Equals(ZombiesMap.MachineProp.Perk, StringComparison.OrdinalIgnoreCase);
            var def = PerkProp(zm, i);
            if (!zm.MachineProps)
            {
                if (def != null) // (no props: the machine drawn without collision)
                    foreach (var pp in def.Parts)
                        Gfx.Draw(Matrix4x4.CreateScale(pp.Size) * Matrix4x4.CreateFromQuaternion(pp.Rotation) * Matrix4x4.CreateTranslation(pp.Offset) * place, pp.Color);
                else
                    Part(0.9f, 2.0f, 0.7f, 0, 1f, 0, c * 0.55f);
            }
            if (!own)
            {
                if (zm.MachineProps || def != null)
                    Part(0.8f, 0.97f, 0.01f, 0, 0.9f, -0.337f, c * 0.6f); // the perk's colour on the plain Perk Machine's front
                Part(0.25f, 0.4f, 0.02f, 0, 0.8f, -0.36f, new Vector3(0.1f)); // its dispenser
            }
            // The face every machine shares: the sign, its corner lamps and the glow strip under it.
            Part(0.7f, 0.5f, 0.02f, 0, 1.62f, -0.36f, lit ? c * (1.1f + 0.15f * MathF.Sin(t * 3)) : c * 0.3f, lit ? 2 : 0);
            if (!own)
                continue;
            var blink = (int)(t * 2.5f + i) & 1;
            Part(0.07f, 0.07f, 0.04f, -0.33f, 1.93f, -0.37f, lit ? (blink == 0 ? c * 1.4f + new Vector3(0.2f) : c * 0.5f) : new Vector3(0.12f), lit && blink == 0 ? 2 : 0);
            Part(0.07f, 0.07f, 0.04f, 0.33f, 1.93f, -0.37f, lit ? (blink == 1 ? c * 1.4f + new Vector3(0.2f) : c * 0.5f) : new Vector3(0.12f), lit && blink == 1 ? 2 : 0);
            Part(0.6f, 0.035f, 0.02f, 0, 1.33f, -0.365f, lit ? c * 1.2f : c * 0.25f, lit ? 2 : 0);
        }
    }
}
