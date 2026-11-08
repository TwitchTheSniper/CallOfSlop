# Corpses and gore that stay

Dead bodies are ragdolls (`Source/Render/Ragdolls.cs`, `Source/Physics/Ragdoll.cs`) that every PC builds from the kill
it hears. Three things make them part of the world in Sandbox:

## Bodies pile up

Bodies collide with each other, particle against particle (spheres, `Ragdoll.Others`, `Ragdoll.CollideOthers`). The
pool gives each awake body the bodies whose boxes reach it before its step (`Ragdolls.GatherOthers`, at most 8). A
sleeping body is a floor; a particle landing on it faster than `Ragdoll.WakeSpeed` (2.5 m/s) wakes it, so a heap shifts
when another body drops on it. An awake pair shares the push by mass. The cost check in "Ragdolls and knockback" covers
it (16 awake bodies, about 0.26 ms a step on the cloud box).

## Stay or fade

Settings > Video "Bodies and gore" (`Settings.GoreLasting`, `gore_lasting=` in settings.txt): **Fade** (the default)
lets bodies go after 14 s (zombies 7 s) and gibs after about 15 s, as before; **Stay** keeps them. Bodies stay until
`Ragdolls.StayKeep` (40 of the 48 slots) are lying about, then the oldest fades; gibs at rest stop ageing and the
oldest is recycled when the gib pool (320 pieces) is full. Blood splats were never timed: they leave when the decal
pool needs room.

## Walking over them

A settled body is ground for players (`Game.CorpsePlayer`, `Source/Game/GameCorpses.cs`), run after every movement
tick on the server, in the client's prediction and in its replay. Feet over a body's particles stand on top of them, a
step up of at most `World.StepHeight`; a part of a pile taller than a step is a wall the player is slid round, so a
heap is climbed from its low side. Sandbox only, and not while noclipping, riding a construct or climbing.

Only settled bodies count, and on a client only those the host has settled, because the falling bodies differ a
little between PCs. When a body falls asleep on the host its pose is rounded to the wire's precision (the first
particle exact, the rest in millimetres from it, `Ragdolls.Quantize`) and sent in `Msg.Corpse` (104 bytes, reliable).
The client takes that pose for its own body of that player or zombie, or makes one if it has none (a joiner gets
every settled body). When the host's body goes, a "gone" makes the client's fade, so the host's "Bodies and gore"
choice holds for the bodies in multiplayer; gibs stay each PC's own.

Not done: bodies are not saved with a world, and a body on a moving construct is not carried with it.
