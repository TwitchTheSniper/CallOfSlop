using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Old scrap counts (board item sh01, decision 31). Scrap no longer exists: a count of it read from an older inventory
/// (nothing saves the Sandbox inventory yet, so the only source is a world or inventory file from before protocol 106)
/// becomes metal in Materials Bins set down beside the player, one scrap for one metal; what no bin takes (the world is
/// full, or too much for three bins) goes into the player's carried materials. Never lost, never scrap.
/// </summary>
public static class ConstructLegacyScrap
{
    public const int MaxBins = 3;

    /// <summary>Server: turns <paramref name="scrap"/> into metal beside <paramref name="feet"/>. Returns a line for the log.</summary>
    public static string Import(ConstructWorld world, SandboxInventory inv, Vector3 feet, float yaw, int scrap)
    {
        if (scrap <= 0 || ConstructParts.Find("Materials Bin") is not { IsBin: true } bin)
            return "no legacy scrap";
        var metal = RecyclingTable.Current.IdOf("metal");
        var left = scrap;
        var bins = 0;
        var side = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        for (var k = 0; k < MaxBins && left > 0; k++)
        {
            var at = feet + World.FlatForward(yaw) * 1.8f + side * (k - 1) * 1.1f;
            if (ConstructBuild.PlaceNew(world, bin, ConstructWorld.Ground(World.Query, at.X, at.Z), yaw) is not { } c)
                break;
            c.Owner = 255; // (anyone's to use)
            left -= world.Deposit(c, 0, left, metal);
            bins++;
        }
        var carried = inv.AddMaterial(metal, left);
        return $"{scrap} legacy scrap -> {scrap - left} metal in {bins} bin(s), {carried} carried, {left - carried} lost";
    }
}
