# recipe-select (feat/machine-recipe-select, wt /home/claude/wt/vehicle-weapons)

Finding: a machine's "panel" is its in-world screen (panel source "machine", MachineScreens.cs; touch buttons pressed with Hands, Program.SandboxPointer.cs). F on a machine takes its stock out, so the control is a Recipe button on that screen, plus the screen's recipe line / "No recipe set".
Wire: configure act needs the configure tool in hand (ConstructConfigure.cs:21), so the pick reuses CActKind.LineRecipe (protocol 250) with console == machine (the machine's own screen). Same bytes, no new act; server rule = a finger on that machine's button (reach, power, who may) + ProductionLines' recipe checks (tier).

| Machine | Setting | Screen | Picker list | Plan |
|---|---|---|---|---|
| Assembler T1-5, Thruster Assembler T1-5, Tailor T1-5 | recipe (CfgKey.Recipe) | machine, 8 rows, order buttons (ExtrusionParts.OrderButtons via MachineTiers) | yes (Assembler) | Recipe button |
| Smelter/Foundry/Sawmill T1-5 (refiners) | recipe (Auto + choices) | machine, 4 rows, no buttons | yes (refiner) | rows 5 + Recipe button |
| Panel Former, Medium | recipe = preset (HullLoft.PanelPresets) | machine, Preset button (orders.preset cycles) | no -> add preset tiles | Preset button opens picker |
| Die Lathe (all sizes) | recipe = die it cuts | press.bay/press.die | yes (lathe list; other sizes dim) | DONE 2026-11-12: Recipe on its die screen |
| Press (all sizes) | recipe = Pattern of the fitted die | press.die, Pattern button | yes (fitted die's patterns; other dies of its size dim) | DONE 2026-11-12: Recipe beside Pattern |
| Extruder family | profile + width/height + mask | machine | no | LEFT: section grid, not a list |
| Wheel Former | runMode (Width/Depth) + order sizes | machine | no | LEFT: not a recipe |
| Fan Former / Airframe Former | fanKind/wingKind + sizes | machine, Recipe button | yes (kinds as swatches) | DONE 2026-11-12 (ProductionLines.RecipeKey) |
| Recycler | none | - | - | n/a |

## Steps
1. survey: done
2. control: done (Recipe button on screens + Program.MachineRecipePicker; picker serves Panel Former presets)
3. verify: Assembler (host, picked Gear Set, ran), Smelter (host + client, glass on both), Esc x20 mouse captured, checks 0. Panel Former: window touch blocked by showcase parts (sign/beam in front of the screen): NOT verified in window. (2026-11-12: the sign moved to x 32.25; the real block was the board under the screen, a look box with no collider, now solid; verified: the picker opens.)
4. final push: done 9996a0e7

## Follow-ups (2026-11-12, branch claude/recipe-select-followups-2cbafe)
- Panel Former on the Showcase: verified in a window (picker opens, eight presets).
- Press, Die Lathe, Fan Former, Airframe Former: Recipe button and picker tiles (production.md "The makers adopt it"). Press verified on a
  host and a joined client (the client's pick reached the host and came back on the client's die screen after the dirty-mark fix).
- Window shots: `picker`, `picker N`, `picker @PART` (a scripted `click` doesn't reach ImGui on the Windows desktop).

## The Extruder's picker: options for the user to decide

The Extruder's "recipe" is several settings at once: Profile (14 sections and Custom), Width, Height, a Pipe's wall, and Custom's 8 x 8
cell grid (four mask settings). A pick in the picker today sets one value, so each option below says what it sends.

**A. Section tiles, sizes under them.** One tile per Profile, its picture the section itself drawn from its 8 x 8 mask (Round, Pipe and
the foils drawn as their shape); under the tiles Width and Height steppers (and Pipe wall when Pipe is picked), clamped to this die's
size, then Set. Custom opens the cell grid in the same window. Sends profile, width, height, wall and mask together: a new act and a
protocol bump. Covers everything the configure panel does, in one window; the most UI work of the three.

**B. Stock pieces as tiles (recommended).** The tiles are whole pieces: the sections and sizes the rest of the base wants, read from the
data rather than typed (each Press die's standard blanks, `PressTable` presets, the Assembling inputs that are extruded pieces), grouped
by what they feed ("Bracket blanks", "Plates", "Rods"), each drawn as its section with its size ("L-angle 0.2 m", "Plate 0.5 x 0.1").
One click sets the whole key. The value sent is the tile's place in that list, so it rides the same `CActKind.LineRecipe` with no new
bytes: the server looks the place up and writes profile, width and height (a `RecipeKey` that names a virtual setting). Custom and odd
sizes stay with the configure tool. It answers the usual question ("make me what the press needs") in one click and is the least work.

**C. A section editor.** Profile tiles on the left, the live 8 x 8 grid on the right showing the current section (click cells on or
off, as the configure panel's grid), Width and Height fields, Apply. Like A it sends several settings (new act, protocol bump), and
it is a second home for the configure panel's grid editor.

Recommendation: B now, as it keeps the picker a one-click list and needs no wire change; add A's drawn section pictures to B's tiles;
leave Custom and free sizes to the configure tool until players ask for more.

**The user's answer (2026-11-13): A, section tiles with sizes under them** (not the recommendation, B). One tile per Profile drawn
from its mask, Width and Height steppers (and Pipe wall) clamped to the die's size, Set; Custom opens the cell grid in the same
window. A new act and a protocol bump.

## Built: the Extruder's picker, option A (2026-11-13, branch claude/extruder-picker-e9eb0f, protocol 284)

- **Button.** Every Extruder (small, Medium, Large, Capital) has Recipe among its queue buttons (`ExtrusionParts.OrderButtons(recipe: true)`);
  its look line ends "Recipe on its screen: section and size". The Wheel, Panel, Fan and Airframe Formers are unchanged. A Line Console's
  Recipe button doesn't open it for an Extruder (the section is picked at the machine).
- **Window** (`UI/UiRecipePickerExtruder.cs`): one tile per Profile option (15: the nine presets, Custom, Round, Pipe, three foils), each
  drawn as its section (grid cells; Round a disc; Pipe a ring of the picked wall; the foils as NACA outlines from thickness and camber),
  names shrunk to fit by the recipe tiles' own `TileName`; the machine's own profile marked "set". Under them Width and Height steppers
  (5 cm; Diameter for Round and Pipe), Pipe wall (1 cm) for Pipe, held to `ExtruderPicks.DieCm` (the die as it is now, Die Frames
  counted), the piece in words ("L-angle 0.2 x 0.2 m"), the die's size, and why it can't be set; then Set. Custom opens the configure
  panel's own cell grid beside the steppers (`Ui.SectionCells` and `SectionCellsRefusal`, shared with `UiSandboxConfig`), starting from
  the shown section when the saved cells can't be made.
- **Wire.** `CActKind.ExtruderSection` (u16 body, u16 machine, u8 profile, u16 width, height, wall in cm, u64 mask). The server
  (`ConstructExtruderPick.cs`) takes it as a finger on the Recipe button (who may, reach, power) and refuses a profile out of range, sizes
  off the 5 cm step, wider or taller than the die (a Round or Pipe: its smaller side), a pipe wall that leaves under 5 cm of bore, or Custom
  cells `Extrusion.Refusal` refuses; then writes Profile, Width, Height, Wall (Pipe) and the four mask words (Custom) through
  `ConstructConfig.Set`, as the configure tool does: orders, bills and saves read the same settings.
- **Window shots:** `picker @Extruder#1` opens it, `picker` prints it, `picker N` picks tile N, `picker size W H [WALL]` (cm),
  `picker cell N` (flips a Custom cell; picks Custom), `picker set`.

Left: the configure panel still edits the same settings (unchanged); a console's bill lines don't use the picker; no typed sizes (steppers only).

Tried (2026-11-13, Windows, Proving Ground at 140,0,-100, `--blueprint "Extruder Line"`): as host, `picker @Extruder#1; picker 6; picker size 20 20;
picker set` and the screen said Extruding / L-angle. As a joined client, Pipe 0.3 m with a 4 cm wall: the screen said Pipe, and
reopening the picker marked Pipe "set". Then Custom with two cells flipped: the screen said Custom (then "Path too heavy", with the old
pipe stub still at the die). Not tried: the Recipe button pressed through a scripted `touch` (it reported "nothing in reach to pick up" on
this desktop, as the Press notes found; `picker @` opens it the way the button does); the server's refusals (out of reach, too big for the
die), which the client's steppers never send.
