using Brutal.ImGuiApi;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// The Blueprints tab's faction vehicles (docs/vehiclekit/VEHICLEKIT.md, "Dynamic spawn"): with "New seed" on, a faction
/// vehicle's tile asks the server to make one of its style and shape for a fresh seed (or the one typed), instead of the
/// committed file; the host also chooses whether the server generates at all.
/// </summary>
public sealed partial class Ui
{
    /// <summary>A faction vehicle's tile makes a new one (off: the committed seed, as before).</summary>
    public bool SandboxVehicleNewSeed = true;

    /// <summary>The seed a new one is made for (0: the server picks).</summary>
    public int SandboxVehicleSeed;

    /// <summary>What a faction tile makes with "New seed" on: 0 one of its shape (as before), 1 one by role and tier, 2 a convoy of its style (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys").</summary>
    public int SandboxVehicleBy;

    /// <summary>The role asked for (0: any the style fields, else <see cref="VehicleRoles.All"/>'s index plus one), the tier, a convoy's size.</summary>
    public int SandboxVehicleRole, SandboxVehicleTier = 2, SandboxConvoyCount = 5;

    private static readonly string[] _byNames = ["its shape", "by role", "a convoy"];

    private void SbDrawVehicleSeeds(Game game)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Checkbox("Faction vehicles: new seed"u8, ref SandboxVehicleNewSeed);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A faction vehicle's tile makes a new one of its style and shape on the server, for the seed here (0: any)."u8);
        if (SandboxVehicleNewSeed)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120 * _scale);
            if (ImGui.InputInt("Seed (0: any)##sbvseed"u8, ref SandboxVehicleSeed, 1, 100))
                SandboxVehicleSeed = Math.Clamp(SandboxVehicleSeed, 0, VehicleSpawnKit.MaxSeed);
        }
        if (game.IsServer)
        {
            ImGui.SameLine(0, 16 * _scale);
            var gen = game.VehicleSpawn.Seeds == VehicleSeeds.Generated;
            if (ImGui.Checkbox("Server generates them"u8, ref gen))
                game.VehicleSpawn.Seeds = gen ? VehicleSeeds.Generated : VehicleSeeds.Committed;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Off: every request gets a committed seed of its style and shape."u8);
            ImGui.SameLine(0, 16 * _scale);
            ImGui.Checkbox("Roles and convoys"u8, ref game.VehicleSpawn.RolesAndConvoys);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Off: faction vehicles are asked for by shape only, as before (tiles spawn their shape whatever is picked below)."u8);
        }
        if (!SandboxVehicleNewSeed)
            return;
        ImGui.AlignTextToFramePadding();
        ImGui.Text("A faction tile makes"u8);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110 * _scale);
        if (ImGui.BeginCombo("##sbvby"u8, _byNames[Math.Clamp(SandboxVehicleBy, 0, 2)]))
        {
            for (var k = 0; k < _byNames.Length; k++)
                if (ImGui.Selectable(_byNames[k], k == SandboxVehicleBy))
                    SandboxVehicleBy = k;
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Its shape: a new seed of the tile's style and shape. By role: a vehicle of the tile's style for the role and tier here. A convoy: a group of the style's vehicles, set down passing in front of you."u8);
        if (SandboxVehicleBy == 0)
            return;
        if (SandboxVehicleBy == 1)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120 * _scale);
            var roles = VehicleRoles.All;
            if (ImGui.BeginCombo("Role##sbvrole"u8, SandboxVehicleRole <= 0 || SandboxVehicleRole > roles.Length ? "any" : roles[SandboxVehicleRole - 1].Name))
            {
                if (ImGui.Selectable("any"u8, SandboxVehicleRole == 0))
                    SandboxVehicleRole = 0;
                for (var k = 0; k < roles.Length; k++)
                    if (ImGui.Selectable(roles[k].Name, SandboxVehicleRole == k + 1))
                        SandboxVehicleRole = k + 1;
                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90 * _scale);
            if (ImGui.InputInt("Vehicles##sbvcount"u8, ref SandboxConvoyCount, 1, 1))
                SandboxConvoyCount = Math.Clamp(SandboxConvoyCount, VehicleConvoys.MinCount, VehicleConvoys.MaxCount);
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90 * _scale);
        if (ImGui.InputInt("Tier##sbvtier"u8, ref SandboxVehicleTier, 1, 1))
            SandboxVehicleTier = Math.Clamp(SandboxVehicleTier, 1, VehicleRoles.MaxTier);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The threat ladder within the faction: 1 a scrappy crew's, 4 a warlord's (more guns and armour, better loot)."u8);
    }

    /// <summary>A blueprint tile: a faction vehicle's makes a new one when "New seed" is on, anything else spawns as it is.</summary>
    private void SbSpawnBlueprintTile(Game game, int index)
    {
        var all = ConstructBuild.Blueprints;
        if (SandboxVehicleNewSeed && index >= 0 && index < all.Count && VehicleSpawnKit.FromBlueprintName(all[index].Name, out var g, out _) && g != null)
        {
            var (style, shape) = VehicleSpawnKit.Names(g);
            if (SandboxVehicleBy == 0 || !game.VehicleSpawn.RolesAndConvoys && game.IsServer)
                game.SandboxSpawnVehicle(style, shape, SandboxVehicleSeed);
            else
                game.SandboxSpawnFaction(SandboxVehicleBy == 2, style, SandboxVehicleBy == 1 && SandboxVehicleRole > 0 && SandboxVehicleRole <= VehicleRoles.All.Length ? VehicleRoles.All[SandboxVehicleRole - 1].Name : "",
                    SandboxVehicleTier, SandboxConvoyCount, SandboxVehicleSeed);
            if (!SandboxPartsKeepOpen)
                CloseSandboxParts();
            return;
        }
        SandboxPartsSpawn(game, index);
    }
}
