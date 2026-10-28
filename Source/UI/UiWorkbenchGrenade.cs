using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>The workbench's grenade slot (docs/zombies/WEAPON-BUILDER.md, "Modular grenades"): the stat bars and the part lines.</summary>
public sealed partial class Ui
{
    /// <summary>The grenade's numbers with the change a hovered or dragged part would make.</summary>
    private void DrawGrenadeStats(WeaponBuild b, GrenadeDef? cur, AssembledWeapon? trial)
    {
        var t = trial?.Grenade;
        ImGui.TextColored(Orange, "GRENADE STATS"u8);
        if (cur == null)
        {
            ImGui.TextColored(Dim, "Fit a fuse and a payload to see its numbers."u8);
            return;
        }
        ImGuiSafe.TextColored(Dim, $"Fuse: {cur.FuseText} - payload: {GrenadeDef.PayloadName(cur.Payload)}");
        WbBar("Damage", cur.Damage, t?.Damage, MathF.Sqrt(cur.Damage / 3000f), t != null ? MathF.Sqrt(t.Damage / 3000f) : null, "0", true);
        WbBar("Radius", cur.Radius, t?.Radius, cur.Radius / 16f, t != null ? t.Radius / 16f : null, "0.0 m", true);
        WbBar("Throw", cur.ThrowRange, t?.ThrowRange, cur.ThrowRange / 60f, t != null ? t.ThrowRange / 60f : null, "0 m", true);
        WbBar("Bounce", cur.Bounce, t?.Bounce, cur.Bounce / 0.9f, t != null ? t.Bounce / 0.9f : null, "0.00", true);
        if (cur.Duration > 0 || t is { Duration: > 0 })
            WbBar("Lasts", cur.Duration, t?.Duration, cur.Duration / 20f, t != null ? t.Duration / 20f : null, "0.0 s", true);
        WbBar("Bomblets", cur.Cluster, t?.Cluster, cur.Cluster / (float)GrenadeStats.MaxCluster, t != null ? t.Cluster / (float)GrenadeStats.MaxCluster : null, "0", true);
        WbBar("Aftershocks", cur.Stages, t?.Stages, cur.Stages / (float)GrenadeStats.MaxStages, t != null ? t.Stages / (float)GrenadeStats.MaxStages : null, "0", true);
        WbBar("Extra a round", cur.Extra, t?.Extra, cur.Extra / (float)GrenadeStats.MaxExtra, t != null ? t.Extra / (float)GrenadeStats.MaxExtra : null, "0", true);
        WbBar("Weight", b.Weight, trial?.Build.Weight, b.Weight / GrenadeStats.MaxWeight, trial != null ? trial.Build.Weight / GrenadeStats.MaxWeight : null, "0.00 kg", false);
        if (cur.Sticky)
            ImGui.TextColored(Dim, "Sticky: it stops where it first touches something."u8);
    }

    /// <summary>The grenade blocks of a part, in words, for its tooltip.</summary>
    private static void GrenadeSummary(PartDef p, List<string> bits)
    {
        if (p.Casing is { } c)
            bits.Add($"grenade casing: throw x{c.Throw:0.##}, bounce {c.Bounce:0.00}{(c.Sticky ? ", sticky" : "")}, payload x{c.DamageMult:0.##}, blast x{c.RadiusMult:0.##}"
                     + $"{(c.Extra > 0 ? $", +{c.Extra} grenade(s) a round" : "")}; {p.Sockets.Count(s => s.Type == "gmod")} mod slot(s)");
        if (p.Fuse is { } f)
            bits.Add(f.Kind switch
            {
                FuseKind.Timer => $"timer fuse: {f.Seconds:0.0} s{(f.Cook ? "; hold the grenade key to cook it, let go to throw" : "")}",
                FuseKind.Impact => "impact fuse: goes off the moment it touches anything",
                FuseKind.Proximity => $"proximity fuse: arms {f.Seconds:0.0} s after landing, goes off when a zombie comes within {f.Trigger:0.0} m",
                _ => $"remote fuse: press the grenade key again to set it off (it goes by itself after {f.Seconds:0} s)",
            });
        if (p.Payload is { } pl)
            bits.Add($"{GrenadeDef.PayloadName(pl.Kind)} payload: " + pl.Kind switch
            {
                PayloadKind.Incendiary => $"a {pl.Radius:0.#} m fire for {pl.Duration:0.#} s; zombies in it burn for {pl.Damage:0}",
                PayloadKind.Gas => $"a {pl.Radius:0.#} m cloud for {pl.Duration:0.#} s doing {pl.Damage:0} to each zombie inside",
                PayloadKind.Lure => $"zombies within {pl.Radius * 3:0} m come to it for {pl.Duration:0.#} s, then it goes off for {pl.Damage:0} (r {pl.Radius:0.#} m)",
                PayloadKind.Heal => $"heals players within {pl.Radius:0.#} m by {pl.Strength:0}",
                PayloadKind.Cryo => $"{pl.Damage:0} damage, freezes zombies within {pl.Radius:0.#} m for {pl.Strength:0.#} s, then slows them {pl.Duration:0.#} s",
                PayloadKind.Shock => $"{pl.Damage:0} damage, zaps and stuns zombies within {pl.Radius:0.#} m for {pl.Strength:0.#} s",
                PayloadKind.Duck => $"{pl.Damage:0} damage and launches every zombie within {pl.Radius:0.#} m",
                _ => $"{pl.Damage:0} damage within {pl.Radius:0.#} m",
            });
        if (p.GMod is { } m)
        {
            var mb = new List<string>();
            if (m.Cluster > 0) mb.Add($"splits into {m.Cluster} bomblets");
            if (m.Stages > 0) mb.Add($"goes off {m.Stages} more time(s)");
            if (m.DamageMult != 1) mb.Add($"damage x{m.DamageMult:0.##}");
            if (m.RadiusMult != 1) mb.Add($"radius x{m.RadiusMult:0.##}");
            if (m.ThrowMult != 1) mb.Add($"throw x{m.ThrowMult:0.##}");
            if (m.FuseMult != 1) mb.Add($"fuse x{m.FuseMult:0.##}");
            if (m.DurationMult != 1) mb.Add($"lasts x{m.DurationMult:0.##}");
            if (m.Bounce > 0) mb.Add($"bounce +{m.Bounce:0.##}");
            if (m.Sticky) mb.Add("sticky");
            if (m.Extra > 0) mb.Add($"+{m.Extra} grenade(s) a round");
            bits.Add("grenade mod: " + string.Join(", ", mb));
        }
    }
}
