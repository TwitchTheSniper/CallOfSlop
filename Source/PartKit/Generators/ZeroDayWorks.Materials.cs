using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The materials row (doc "Materials row"), under the annex roof, z -24 to -40: raw stock bunkers on the north row, the refiners
        // beside them, the refined bins on the south row where the extruders' chutes start. A bunker takes any stock: players tip each raw
        // bunker's own load in (the names below say which).
        public const float RawRow = -29f, BinRow = -37f;

        /// <summary>The refined bins by material: steel, metal, copper, glass, planks. Later areas chute from them.</summary>
        private readonly Dictionary<string, VPart> _bins = new();

        private VPart? _recycler;

        private void LayMaterials()
        {
            // Raw stock, west end: three ore bunkers (one ore each) and bunkers of stone and wood for the Foundry and the Sawmill.
            var iron = Raw("Ore Bunker 6 m", 5f, "iron ore");
            var copperOre = Raw("Ore Bunker 6 m", 13f, "copper ore");
            var sand = Raw("Ore Bunker 6 m", 19f, "sand");
            var stone = Raw("Ore Bunker 4 m", 25f, "stone");
            var wood = Raw("Ore Bunker 4 m", 89f, "wood");

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
            // Two more Foundries, each set to one recipe (the block Assembler's frigate pieces want concrete and rubber): stone and sand to
            // concrete at x 63, planks and sand to rubber at x 75 (a Foundry's choices: ammo, steel, concrete, laminate, rubber).
            var concrete = Refiner("Foundry", 63f);
            Config(concrete, ("recipe", 3));
            var rubber = Refiner("Foundry", 75f);
            Config(rubber, ("recipe", 5));
            Chute(stone, concrete, 6.5f);
            Chute(sand, concrete, 7f);
            Chute(_bins["planks"], rubber, 6.5f);
            Chute(sand, rubber, 7.5f);
            Chute(concrete, Bin("concrete", 63f));
            Chute(rubber, Bin("rubber", 75f));
        }

        /// <summary>A raw stock bunker on the north row at x, full of its stock when spawned in creative (Sean, 2026-11-22: a works to try at once).</summary>
        private VPart Raw(string part, float x, string stock)
        {
            var b = On(part, x, RawRow, Up, paint: Stock, finish: "concrete");
            Config(b, ("material", stock), ("fill", 100));
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
