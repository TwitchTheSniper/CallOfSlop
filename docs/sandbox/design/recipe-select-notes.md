# recipe-select (feat/machine-recipe-select, wt /home/claude/wt/vehicle-weapons)

Finding: a machine's "panel" is its in-world screen (panel source "machine", MachineScreens.cs; touch buttons pressed with Hands, Program.SandboxPointer.cs). F on a machine takes its stock out, so the control is a Recipe button on that screen, plus the screen's recipe line / "No recipe set".
Wire: configure act needs the configure tool in hand (ConstructConfigure.cs:21), so the pick reuses CActKind.LineRecipe (protocol 250) with console == machine (the machine's own screen). Same bytes, no new act; server rule = a finger on that machine's button (reach, power, who may) + ProductionLines' recipe checks (tier).

| Machine | Setting | Screen | Picker list | Plan |
|---|---|---|---|---|
| Assembler T1-5, Thruster Assembler T1-5, Tailor T1-5 | recipe (CfgKey.Recipe) | machine, 8 rows, order buttons (ExtrusionParts.OrderButtons via MachineTiers) | yes (Assembler) | Recipe button |
| Smelter/Foundry/Sawmill T1-5 (refiners) | recipe (Auto + choices) | machine, 4 rows, no buttons | yes (refiner) | rows 5 + Recipe button |
| Panel Former, Medium | recipe = preset (HullLoft.PanelPresets) | machine, Preset button (orders.preset cycles) | no -> add preset tiles | Preset button opens picker |
| Die Lathe (all sizes) | recipe = die it cuts | press.bay/press.die (PressParts.cs: Press agent's files) | no | LEFT for Press agent |
| Extruder family | profile + width/height + mask | machine | no | LEFT: section grid, not a list |
| Wheel Former | runMode (Width/Depth) + order sizes | machine | no | LEFT: not a recipe |
| Fan Former / Airframe Former | fanKind/wingKind + sizes | machine, no buttons | no | LEFT: next phase candidate |
| Recycler | none | - | - | n/a |

## Steps
1. survey: done
2. control: done (Recipe button on screens + Program.MachineRecipePicker; picker serves Panel Former presets)
3. verify: Assembler (host, picked Gear Set, ran), Smelter (host + client, glass on both), Esc x20 mouse captured, checks 0. Panel Former: window touch blocked by showcase parts (sign/beam in front of the screen): NOT verified in window.
4. final push: done 9996a0e7
