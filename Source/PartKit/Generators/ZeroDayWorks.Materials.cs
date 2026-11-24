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

            // Three Smelters (iron ore to metal, copper ore to copper, sand to glass), the Foundry (metal and stone to steel), the Sawmill
            // (wood to planks) and the Recycler, each over the bin it fills. A Smelter is a Core (x1, the old tier 1), as the Foundry and
            // the Sawmill are (4 planks a second); the copper one has a Hearth Block on its +x side, a 1 x 1 furnace (x2.25, over the old Workshop Smelter's x2: production.md "Refiners as block machines").
            var smelt = Refiner("Smelter", 33f);
            var smeltCu = Refiner("Smelter", 41f);
            Paint(Onto(smeltCu, "+x", "Hearth Block", "-x"), Refining);
            var smeltGlass = Refiner("Smelter", 49f);
            var foundry = Refiner("Foundry", 57f);
            var sawmill = Refiner("Sawmill", 81f);
            _recycler = On("Recycler", 85f, RawRow, Up, paint: Refining); // (between the Sawmill and the wood bunker: x 69 is the laminate Foundry's)

            Chute(iron, smelt);
            Chute(copperOre, smeltCu);
            Chute(sand, smeltGlass);
            Chute(wood, sawmill);
            Chute(stone, foundry);
            // The metal and the steel (Sean, 2026-11-24: the refinery starved the works; a Smelter and the Foundry each make 2 units a
            // second): each refiner fills a tall Ore Silo 4 m in the hall (57,960 units, nearly five 4 m bins), which keeps its bin on the
            // row full; full of its stock, the bin too, when spawned in creative. The silos stand between the stock yard's two rows of
            // chutes (z -12 and -21), their chutes over everything at 13 to 14.5 m.
            var metalSilo = Silo(57f, "metal");
            var steelSilo = Silo(63f, "steel");
            Chute(smelt, metalSilo, 13f);
            Chute(metalSilo, Full(Bin("metal", 33f), "metal"), 13.5f);
            Chute(smeltCu, Bin("copper", 41f));
            Chute(smeltGlass, Bin("glass", 49f));
            Chute(_bins["metal"], foundry);
            Chute(foundry, steelSilo, 14f);
            Chute(steelSilo, Full(Bin("steel", 57f), "steel"), 14.5f);
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
            // A fourth Foundry on laminate at x 69 (planks and glass): the presses' Button and Board dies take laminate blanks.
            var laminate = Refiner("Foundry", 69f);
            Config(laminate, ("recipe", 4));
            Chute(_bins["planks"], laminate, 8f);
            Chute(_bins["glass"], laminate, 8f);
            Chute(laminate, Bin("laminate", 69f));
            Chute(rubber, Bin("rubber", 75f));

            // The stock yard in the generator hall (Sean, 2026-11-22: the ore bunkers were far too small for what the frigate takes; a 7 minute
            // run used most of the stone, which the steel and the concrete Foundries both draw on): eight 8 m bunkers (60,000 units each, a
            // bin's most: ConstructStorage.MaxBulk) north of the materials row, each chuted into its raw bunker, which it tops up as the
            // refiners draw it down (a chute moves bulk bin to bin while the far one has room). Two each for stone, iron ore and sand, one
            // each for copper ore and wood: with the raw bunkers, 4 to 11 times the stock the row held. Full when spawned in creative.
            var up = 8.5f;
            foreach (var (x, z, to, stock) in new[]
                     {
                         (7f, -12f, iron, "iron ore"), (7f, -21f, iron, "iron ore"), (50f, -12f, stone, "stone"), (50f, -21f, stone, "stone"),
                         (77f, -12f, sand, "sand"), (77f, -21f, sand, "sand"), (86f, -12f, copperOre, "copper ore"), (86f, -21f, wood, "wood"),
                     })
            {
                var store = On("Ore Bunker 8 m", x, z, Up, paint: Stock, finish: "concrete");
                Config(store, ("material", stock), ("fill", 100));
                Chute(store, to, up);
                up += 0.5f;
            }
        }

        /// <summary>A raw stock bunker on the north row at x, full of its stock when spawned in creative (Sean, 2026-11-22: a works to try at once).</summary>
        private VPart Raw(string part, float x, string stock)
        {
            var b = On(part, x, RawRow, Up, paint: Stock, finish: "concrete");
            Config(b, ("material", stock), ("fill", 100));
            return b;
        }

        public const float SiloZ = -17f; // (a slab's sockets are on the metres 1 to 7 past its edge: not on a multiple of 8)

        /// <summary>A tall Ore Silo 4 m in the generator hall at x, z <see cref="SiloZ"/>, full of <paramref name="stock"/> when spawned in creative.</summary>
        private VPart Silo(float x, string stock) => Full(On("Ore Silo 4 m", x, SiloZ, Up, paint: Stock, finish: "concrete"), stock);

        /// <summary>The bin, full of <paramref name="stock"/> when spawned in creative.</summary>
        private VPart Full(VPart bin, string stock)
        {
            Config(bin, ("material", stock), ("fill", 100));
            return bin;
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
