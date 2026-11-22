# The turret camera

Karl, 2026-11-22: sat at a seat that works a turret, the view is the turret's. First person looks out of a camera on the turret, third
person orbits the turret instead of the ship, and the readouts are a gunsight's.

## When it applies

`ConstructWeapons.TurretCamGun(c, seat)`: the seat aims a gun through a Turret Module (`CGun.Turret`) and at least one turning joint
(`AimJoints`), and the seat itself is on the hull (`Construct.Rigid` of its segment). A seat that rides the turret it points (an open
gun mount) keeps its own view, and a drone console's receiver keeps the drone's (Program.CameraFeeds.cs). With several Turret Modules the
camera is the module in use's (`ConstructModules.ActiveGun`), so the seat's Aim key moves the view from turret to turret.

## Where the camera is

`ConstructWeapons.TurretEye`: a Camera part bolted to the turret (on the gun's segment or a turning segment under it, so a camera on the
turntable only turns, one on the cradle also pitches), at its lens; without one, the gun's own sight just past the muzzle and above the
bore, high enough to see over its housing (`GunSight`, the drone turret's sight). The view looks along the gunner's look, not the bore:
the turret slews after it, and the bore marker shows where the gun is now.

`ConstructWeapons.SeatEye` returns the same point for these seats, so the server's aim (`GunnerPoint`), the crews and the shells'
tracking start where the picture does, and the guns converge on what the reticle is on in either view.

## On this PC

- First person: `Program.TurretCamera` (Program.TurretCamera.cs) moves the eye; the wheel steps the gunsight's zoom (1x, 2x, 4x, 8x),
  which slows the mouse by as much. The body is drawn in its chair, as at a drone console. The normal crosshair gives way to the reticle.
- Third person: `Game.TurretOrbitPoint` (GameTurretView.cs) centres the orbit a little above the gun, at a distance from its size. The
  orbit starts outside the hull, so it also stops at its own ship (Program.ThirdPerson.cs), and the crosshair's aim is taken from the
  turret's camera (`GunnerAimTarget`'s eye).
- Readouts: UI/GunnerHud.cs, the ImGui background pass. Reticle with stadia ticks (first person); the bore marker, green once laid within
  0.6 degrees of `LaidWay`, amber while it slews; range to what the reticle is on (out to 1.25x the gun's reach, "OUT OF REACH" past it)
  and the target's closing speed; bearing and elevation off the station's bow with a top-down dial; the turret (n of m), stabiliser, rounds,
  boxes, a load or reload bar and why it won't fire. The seat controls panel (UiSeatPanel) still lists the keys.

## Not done yet

- No setting to keep the seat's own view at a turret station.
- The cockpit instruments (draft PR 526) could repeat these readouts on a console screen; nothing links them yet.
