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
        }
    }

    /// <summary>A blueprint tile: a faction vehicle's makes a new one when "New seed" is on, anything else spawns as it is.</summary>
    private void SbSpawnBlueprintTile(Game game, int index)
    {
        var all = ConstructBuild.Blueprints;
        if (SandboxVehicleNewSeed && index >= 0 && index < all.Count && VehicleSpawnKit.FromBlueprintName(all[index].Name, out var g, out _) && g != null)
        {
            var (style, shape) = VehicleSpawnKit.Names(g);
            game.SandboxSpawnVehicle(style, shape, SandboxVehicleSeed);
            if (!SandboxPartsKeepOpen)
                CloseSandboxParts();
            return;
        }
        SandboxPartsSpawn(game, index);
    }
}
