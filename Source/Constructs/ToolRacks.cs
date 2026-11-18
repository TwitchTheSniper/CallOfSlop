using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Tool Racks (the user, 2026-11-17: "tool racks for NPCs", so crew carry portable drills to where the ore is; factory-generator-notes.md
/// "Tool racks and crew miners"). A rack is a part with hooks, each holding one hand tool (the part file's <c>"toolRack"</c>:
/// <c>{"tool": "drill", "hooks": [[x, y, z], ...]}</c>). A crew member takes a tool off a hook and hangs it back
/// (Game/GameCrewMine.cs); which hooks are empty is a hidden setting (<see cref="CfgKey.ToolsOut"/>, a bit a hook), so it rides
/// saves, blueprints and the body's structure to every peer, and the renderer draws a tool on every full hook (RouteRender.DrawToolRacks).
/// A player's own tools are always to hand (the tool wheel), so a rack is the crew's: F at it only says what it holds.
/// </summary>
public static class ToolRacks
{
    public const int MaxHooks = 8;

    /// <summary>The part file's <c>"toolRack"</c> key.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "toolRack") is not { } r)
            return;
        ConstructParts.Known(r, ["tool", "hooks"], $"{file}, toolRack");
        var tool = ConstructParts.Need(r, "tool", $"{file}, toolRack").GetString();
        d.RackTool = tool switch
        {
            "drill" => ConstructTool.GroundDrill,
            _ => throw new ConstructPartException($"{file}: toolRack.tool \"{tool}\" isn't a tool a rack holds (drill)"),
        };
        var hooks = ConstructParts.Need(r, "hooks", $"{file}, toolRack");
        if (hooks.ValueKind != JsonValueKind.Array || hooks.GetArrayLength() is < 1 or > MaxHooks)
            throw new ConstructPartException($"{file}: toolRack.hooks lists 1 to {MaxHooks} places [x, y, z]");
        var list = new Vector3[hooks.GetArrayLength()];
        var i = 0;
        foreach (var h in hooks.EnumerateArray())
            list[i] = ConstructParts.Vec(h, file, $"toolRack.hooks[{i++}]");
        d.RackHooks = list;
    }

    /// <summary>Every Tool Rack has this (ConstructConfig.Parse): which hooks are empty, hidden.</summary>
    public static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        if (!d.IsToolRack || list.Exists(e => e.Id == CfgKey.ToolsOut))
            return;
        list.Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.ToolsOut), Id = CfgKey.ToolsOut, Type = CfgType.Mask, Hidden = true, Label = "Tools out", Group = "Rack", Tooltip = "Not on the panel." });
    }

    /// <summary>The hooks whose tool is out, a bit each.</summary>
    public static int Out(in CPart p) => p.D.IsToolRack ? (int)ConstructConfig.Get(p, p.D, CfgKey.ToolsOut, 0) : 0;

    /// <summary>Tools on the rack now.</summary>
    public static int OnRack(in CPart p)
    {
        if (!p.D.IsToolRack)
            return 0;
        var o = Out(p);
        var n = 0;
        for (var k = 0; k < p.D.RackHooks.Length; k++)
            if ((o & 1 << k) == 0)
                n++;
        return n;
    }

    /// <summary>Server: a tool off the rack's first full hook (its index; -1: none there, or the part is broken). Every peer hears it with the structure.</summary>
    public static int Take(ConstructWorld world, Construct c, int part)
    {
        if ((uint)part >= (uint)c.PartCount || c.Parts[part].Broken || !c.Parts[part].D.IsToolRack)
            return -1;
        ref var p = ref c.Parts[part];
        var o = Out(p);
        for (var k = 0; k < p.D.RackHooks.Length; k++)
            if ((o & 1 << k) == 0)
            {
                ConstructConfig.Put(ref p, p.D, CfgKey.ToolsOut, o | 1 << k);
                world.StructureDirty.Add(c.Id);
                return k;
            }
        return -1;
    }

    /// <summary>Server: the tool back on hook <paramref name="hook"/> (false: the rack or the hook is gone, or it was full).</summary>
    public static bool Give(ConstructWorld world, Construct c, int part, int hook)
    {
        if ((uint)part >= (uint)c.PartCount || !c.Parts[part].D.IsToolRack || (uint)hook >= (uint)c.Parts[part].D.RackHooks.Length)
            return false;
        ref var p = ref c.Parts[part];
        var o = Out(p);
        if ((o & 1 << hook) == 0)
            return false;
        ConstructConfig.Put(ref p, p.D, CfgKey.ToolsOut, o & ~(1 << hook));
        world.StructureDirty.Add(c.Id);
        return true;
    }

    /// <summary>The look line's words for a rack.</summary>
    public static string Line(in CPart p) => $"{OnRack(p)} of {p.D.RackHooks.Length} {(p.D.RackTool == ConstructTool.GroundDrill ? "portable drills" : "tools")} on it: a crew miner takes one out to the ore and hangs it back";
}
