# The Engine Kit (Source/EngineKit)

A request for an engine (power, torque, fit, mass, layout, tier, seed) and a built modular engine back: the modular engine's
own pieces (docs/sandbox/CONSTRUCTS.md "Modular engines") put together through the game's build rules (the vehicle kit's
`BlueprintScript`), with the numbers the game itself works out for it. It is in C#: the first native generator, which the
Python part generators are meant to follow.

## Why cars are slow

`--speed-test FILE... [--seconds N]` drives blueprints flat out on the drive check's flat floor (default 40 s) and prints what
they are and what tops them out (`Source/EngineKit/EngineKitSpeed.cs`). Measured 2026-11-11 on the committed blueprints:

| Vehicle | Mass kg | Engine | Peak kW | Gears x final | Wheel r m | Top m/s | 0-10 s | 0-20 s | Gear at top | rpm at top | Gearing's top m/s | Drag kW at top |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| BanditCar1 | 1653 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 24.6 | 2.0 | 6.5 | 3/4 | 5512 | 39.2 | 147 |
| ITCar1 | 1451 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 25.4 | 2.0 | 6.6 | 3/4 | 5707 | 39.2 | 143 |
| HighlandCar1 | 1500 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 24.4 | 1.3 | 4.5 | 3/4 | 5473 | 39.2 | 131 |
| BritishTechnical2 | 1439 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 26.1 | 1.4 | 4.4 | 4/4 | 4087 | 39.2 | 153 |
| ScamTechnical1 | 1595 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 24.4 | 1.5 | 4.8 | 3/4 | 5478 | 39.2 | 139 |
| BanditBoxTruck1 | 2475 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 19.3 | 1.6 | - | 3/4 | 4334 | 39.2 | 107 |
| ITBoxTruck1 | 2492 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.1 | 0.55 | 20.8 | 2.1 | 8.9 | 2/4 | 4730 | 53.7 | 134 |
| BanditWarBus1 | 3448 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 17.5 | 2.5 | - | 2/4 | 5468 | 39.2 | 111 |
| ITWarBus1 | 4401 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.6 | 0.45 | 15.2 | 3.4 | - | 2/4 | 4729 | 39.2 | 92 |
| ITBuggy1 | 1272 | V8 Engine | 218 | 4.6/3.2/2.3/1.7 x 4.1 | 0.55 | 25.7 | 1.1 | 3.1 | 3/4 | 4190 | 53.7 | 129 |

What limits them: **power against the air drag, nothing else.** No vehicle reaches its limiter (gearing would allow 39 to 54 m/s
in top gear), and no speed cap is near (`ConstructPhysics.MaxSpeed` is 60 m/s). Every shipped car, technical, truck and bus has
the same 218 kW V8 (the stock 114 kW Engine is in the styles' lists too) and the same Automatic Gearbox. The arcade air drag
(`ConstructPhysics.AirDrag`, 0.006 v^2 m/s^2, the same for every body under 20 t: `AirDragOf`) takes 0.006 v^3 W a kilogram, so a
top speed goes with the cube root of power a kilogram: twice the speed needs eight times the power. Of the V8's 218 kW, 130 to
150 kW go into drag at the top speed of a car, the rest into rolling resistance and the drive line's losses; heavier vehicles
also sit in 2nd or 3rd near the limiter, where the petrol curve has fallen.

What a top speed costs (drag only, add about half again for rolling and losses):

| Target | 1.5 t car | 2.5 t truck | 4.5 t bus |
| --- | --- | --- | --- |
| 25 m/s | 140 kW | 230 kW | 420 kW |
| 30 m/s | 245 kW | 405 kW | 730 kW |
| 35 m/s | 385 kW | 645 kW | 1160 kW |

The user (2026-11-11) is happy for today's small, low-tech vehicles to stay on the slow side: **no shipped vehicle was retuned.**
The faster, more powerful vehicles come with higher tech tiers and bigger vehicles, through the Engine Kit below. Suggested
targets for the tiers: tier 1-2 as now (cars 24-26 m/s, trucks 19-21, buses 15-18); tier 3 cars 30, trucks 24, buses 20;
tier 4-5 cars 35+, trucks 28, buses 24, which is 2.5 to 5 times today's power a kilogram.
