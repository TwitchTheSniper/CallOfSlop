namespace CallOfSlop;

/// <summary>
/// Shutting a craft's reactor and thrusters down from its seat (Karl, 2026-11-21: "a way to shut down reactor, thrusters"). Each power
/// plant (a reactor, a boiler, a genset: not a Generator Head, which its engine turns) and each thruster but a solid rocket has a
/// <see cref="CfgKey.Shutdown"/> setting, on its configure panel too. Shut down, a plant spools down and feeds nothing (the batteries
/// carry the circuit while they last) and a thruster burns nothing, whatever its keys, wires or autopilot ask. The pilot's reactor and
/// thruster keys flip every one aboard at once: all off if any is running, else all back on. The setting rides saves and CBody as every
/// setting does, so a joined client's panel lamps follow it.
/// </summary>
public static class ConstructShutdown
{
    /// <summary>Does a part of this kind take the Shut down setting?</summary>
    public static bool Takes(ConstructPartDef d) =>
        d.Role == PartRole.Generator && d.PlantShaft <= 0 || d.Role == PartRole.Thruster && d.ThrustKind != ThrustKind.Solid;

    public static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        if (!Takes(d) || list.Count >= ConstructConfig.MaxSlots || list.Exists(e => e.Id == CfgKey.Shutdown))
            return;
        var plant = d.Role == PartRole.Generator;
        list.Add(new CConfigDef
        {
            Key = ConstructConfig.KeyName(CfgKey.Shutdown), Id = CfgKey.Shutdown, Type = CfgType.Toggle, Label = "Shut down", Group = plant ? "Power" : "Thrust",
            Tooltip = plant ? "On, it is shut down: it spools down and feeds nothing. A seat's reactor key shuts down or starts every plant aboard." : "On, it is shut down and burns nothing, whatever asks. A seat's thruster key shuts down or starts every thruster aboard.",
        });
    }

    /// <summary>Is this part shut down (a plant or a thruster whose Shut down setting is on)?</summary>
    public static bool Off(in CPart p) => p.D.CfgSlot[(int)CfgKey.Shutdown] >= 0 && ConstructConfig.Get(p, p.D, CfgKey.Shutdown, 0) >= 0.5f;

    /// <summary>Has the craft a power plant the reactor key works?</summary>
    public static bool HasReactor(Construct c)
    {
        for (var k = 0; k < c.PlantCount; k++)
            if (c.Plants[k] < c.PartCount && Takes(c.Parts[c.Plants[k]].D))
                return true;
        return false;
    }

    /// <summary>Are all its plants shut down (false with none)?</summary>
    public static bool ReactorOff(Construct c)
    {
        var any = false;
        for (var k = 0; k < c.PlantCount; k++)
        {
            if (c.Plants[k] >= c.PartCount || !Takes(c.Parts[c.Plants[k]].D))
                continue;
            if (!Off(c.Parts[c.Plants[k]]))
                return false;
            any = true;
        }
        return any;
    }

    /// <summary>Has the craft a thruster the thruster key works?</summary>
    public static bool HasThrusters(Construct c)
    {
        for (var k = 0; k < c.ThrusterCount; k++)
            if (c.Thrusters[k].Part < c.PartCount && Takes(c.Parts[c.Thrusters[k].Part].D))
                return true;
        return false;
    }

    /// <summary>Are all its thrusters shut down (false with none)?</summary>
    public static bool ThrustersOff(Construct c)
    {
        var any = false;
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            int part = c.Thrusters[k].Part;
            if (part >= c.PartCount || !Takes(c.Parts[part].D))
                continue;
            if (!Off(c.Parts[part]))
                return false;
            any = true;
        }
        return any;
    }

    /// <summary>Server: the seat's reactor key. Every plant aboard shut down, or all started again when all were down. A refusal's reason, or null.</summary>
    public static string? ToggleReactor(ConstructWorld world, Construct c)
    {
        if (!HasReactor(c))
            return "this craft has no reactor or power plant";
        var to = ReactorOff(c) ? 0f : 1f;
        for (var k = 0; k < c.PlantCount; k++)
            if (c.Plants[k] < c.PartCount && Takes(c.Parts[c.Plants[k]].D))
                Put(c, c.Plants[k], to);
        world.StructureDirty.Add(c.Id);
        c.Wake();
        return null;
    }

    /// <summary>Server: the seat's thruster key. Every thruster aboard shut down, or all started again when all were down. A refusal's reason, or null.</summary>
    public static string? ToggleThrusters(ConstructWorld world, Construct c)
    {
        if (!HasThrusters(c))
            return "this craft has no thrusters";
        var to = ThrustersOff(c) ? 0f : 1f;
        for (var k = 0; k < c.ThrusterCount; k++)
            if (c.Thrusters[k].Part < c.PartCount && Takes(c.Parts[c.Thrusters[k].Part].D))
                Put(c, c.Thrusters[k].Part, to);
        world.StructureDirty.Add(c.Id);
        c.Wake();
        return null;
    }

    private static void Put(Construct c, int part, float v)
    {
        ref var p = ref c.Parts[part];
        ConstructConfig.Put(ref p, p.D, CfgKey.Shutdown, v);
    }
}
