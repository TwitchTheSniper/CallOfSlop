using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The materials row (doc "Materials row"), under the annex roof, z -24 to -40: raw stock bunkers on the north row, the refiners
        // beside them, the refined bins on the south row where the extruders' chutes start. Creative fills the raw bunkers.
        public const float RawRow = -29f, BinRow = -37f;

        /// <summary>The refined bins by material: steel, metal, copper, glass, planks. Later areas chute from them.</summary>
        private readonly Dictionary<string, VPart> _bins = new();

        private VPart? _recycler;

        private void LayMaterials()
        {
            // Raw stock, west end: three ore bunkers (one ore each) and bunkers of stone and wood for the Foundry and the Sawmill.
            var iron = Raw("Ore Bunker 6 m", "iron ore", 5f);
            var copperOre = Raw("Ore Bunker 6 m", "copper ore", 13f);
            var sand = Raw("Ore Bunker 6 m", "sand", 19f);
            var stone = Raw("Ore Bunker 4 m", "stone", 25f);
            var wood = Raw("Ore Bunker 4 m", "wood", 89f);

            // Three Smelters (iron ore to metal, copper ore to copper at tier 2, sand to glass), the Foundry (metal and stone to steel),
            // the Sawmill (wood to planks) and the Recycler, each over the bin it fills.
            var smelt = Refiner("Smelter", 33f);
            var smeltCu = Refiner("Workshop Smelter", 41f);
            var smeltGlass = Refiner("Smelter", 49f);
            var foundry = Refiner("Foundry", 57f);
            var sawmill = Refiner("Sawmill", 81f);
            _recycler = On("Recycler", 69f, RawRow, Up, paint: Refining);

            Chute(iron, smelt);
            Chute(copperOre, smeltCu);
            Chute(sand, smeltGlass);
            Chute(wood, sawmill);
            Chute(stone, foundry);
            Chute(smelt, Bin("metal", 33f));
            Chute(smeltCu, Bin("copper", 41f));
            Chute(smeltGlass, Bin("glass", 49f));
            Chute(_bins["metal"], foundry);
            Chute(foundry, Bin("steel", 57f));
            Chute(sawmill, Bin("planks", 81f));
        }

        /// <summary>A raw stock bunker on the north row at x, its material set for creative's fill.</summary>
        private VPart Raw(string part, string material, float x)
        {
            var b = On(part, x, RawRow, Up, paint: Stock, finish: "concrete");
            return b;
        }

        private VPart Refiner(string part, float x) => On(part, x, RawRow, Up, paint: Refining);

        /// <summary>A refined bin on the south row at x.</summary>
        private VPart Bin(string material, float x)
        {
            var b = On("Ore Bunker 4 m", x, BinRow, Up, paint: Stock, finish: "concrete");
            _bins[material] = b;
            return b;
        }

        private void Chute(VPart from, VPart to, float height = 6f) => Duct("chute", from, "Chute out", to, "Chute in", height, Stock);
    }
}
