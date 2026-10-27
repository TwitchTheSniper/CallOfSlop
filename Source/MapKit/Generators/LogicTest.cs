namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Maps/Dev/LogicTest.json: "Logic Test", the map logic dev map (developer request, 2026-10-27; Maps/README.md "Map
/// logic"). Each corner shows one thing, and the map's "test" block plays them all through ("events"), so the Map
/// contract (and --check-map) checks the events fire:
/// <list type="bullet">
/// <item>West: a button opens the gate of a walled yard (it shuts again 8 seconds later; bots path through it while open).</item>
/// <item>Centre: walking into the courtyard sets off an alarm and lights its four lamps; they go dark 2 seconds after the last person leaves.</item>
/// <item>North-east: a timer sends the lift up to a deck every 6 seconds (and back down the next time); the deck has stairs too.</item>
/// <item>South: two levers feed an AND gate and a NOT gate: both levers up opens the vault and turns the green lamp on; the red lamp is on otherwise.</item>
/// <item>North: a pressure plate counts presses; every third one picks heads or tails at random.</item>
/// </list>
/// </summary>
public sealed class LogicTest : IMapGenerator
{
    public string Name => "Logic Test";
    public string Pack => "Dev";
    public int Number => 0; // Dev maps carry no number: Maps/Dev/LogicTest.json

    const string Floor = "#5a5d63", Wall = "#8a8d93", Yard = "#6d7a8a", Vault = "#7a6d8a", Post = "#3a3c40", Deck = "#8a7d6a";

    public void Build(Kit k)
    {
        k.Describe("Map logic dev map: a button opens a gate, the courtyard's lamps come on when you walk in, a timer runs the lift, "
                   + "two levers and an AND gate open the vault, and a pressure plate counts to three.", order: 987, maxPlayers: 8);
        k.Size(30, 24);
        k.Top.Set("prototypeGrid", true);
        k.Top.Set("sky", "#7d8ea6");

        k.Ground(Floor);
        k.Perimeter(4, Wall);

        // ---- West: the gate yard (walls 3.5 m; the gate is a logic door in the east wall's doorway) ----
        k.WallX(-27, -15, -6.3, 0.3, 0, 3.5, Yard);
        k.WallX(-27, -15, 6, 0.3, 0, 3.5, Yard);
        k.WallZ(-6.3, 6.3, -27.3, 0.3, 0, 3.5, Yard);
        k.WallZ(-6.3, 6.3, -15, 0.3, 0, 3.5, Yard, Kit.Door(6.3, 2.4, 2.6));
        k.Waypoint(-21, 0);

        // ---- Centre: the courtyard's lamp posts ----
        foreach (var (x, z) in new[] { (-6.0, -6.0), (6.0, -6.0), (-6.0, 6.0), (6.0, 6.0) })
            k.Block(x - 0.15, 0, z - 0.15, x + 0.15, 2.6, z + 0.15, Post);

        // ---- North-east: the lift and its deck (stairs up the deck's south side, for bots and anyone who'd rather walk) ----
        k.Block(21.6, 0, -13, 27, 3.8, -7, Deck);
        k.StairsZ(23, 25, -2.5, -1, 0, 3.8, Deck);
        k.Waypoint(24.3, -10, 3.8);
        k.Top.Set("movers", new JArray([new JObject(("type", "elevator"), ("name", "Lift"), ("at", JArray.Of(20, 0.3, -10)), ("rise", 3.5), ("speed", 1.5), ("dwell", 0),
            ("running", false), ("boxes", new JArray([new JObject(("size", JArray.Of(3, 0.3, 3)), ("color", "#4fc36b"))])))]));

        // ---- South: the lever wall and the vault behind it ----
        k.WallX(-10, 10, 15, 0.3, 0, 2.0, Wall);
        k.WallX(12, 22, 10, 0.3, 0, 3.5, Vault);
        k.WallX(12, 22, 18, 0.3, 0, 3.5, Vault);
        k.WallZ(10, 18.3, 22, 0.3, 0, 3.5, Vault);
        k.WallZ(10, 18.3, 12, 0.3, 0, 3.5, Vault, Kit.Door(4, 2.0, 2.6));
        k.Waypoint(17, 14);

        // ---- Spawns, objectives, waypoints ----
        foreach (var x in new[] { -8.0, -3.0, 3.0, 8.0 })
        {
            k.Spawn(0, x, -19);
            k.Spawn(1, x, 11);
        }
        k.Hill(0, -4, 3.5);
        k.Hill(-11, -4, 3.0);
        k.Hill(11, -4, 3.0);
        k.Flag(0, -21);
        k.Flag(0, 13);
        k.AutoWaypoints(4);

        // ---- The logic ----
        var l = k.Logic;
        l.Door("Gate", -15, 0, -1.2, -14.7, 2.6, 1.2, color: "#b0763c");
        l.Button("GateButton", -14.64, 1.3, 2.3, yaw: 90, prompt: "open the gate", cooldown: 1);
        l.Sound("GateSound", "breakMetal", -14.8, 1.3, 0, volume: 0.6, range: 30);
        l.Wire("GateButton.pressed", "Gate.open").Wire("GateButton.pressed", "Gate.close", delay: 8).Wire("Gate.opened", "GateSound.play");

        l.Zone("Courtyard", -6, 0, -6, 6, 3, 6);
        l.Sound("Alarm", "reconBeep", 0, 2, 0, range: 40);
        l.Text("AlarmText", "You set off the courtyard alarm.", toActivator: true);
        l.Wire("Courtyard.entered", "Alarm.play").Wire("Courtyard.entered", "AlarmText.show");
        var lamp = 1;
        foreach (var (x, z) in new[] { (-6.0, -6.0), (6.0, -6.0), (-6.0, 6.0), (6.0, 6.0) })
        {
            var name = $"Lamp{lamp++}";
            l.Lamp(name, x - 0.25, 2.6, z - 0.25, x + 0.25, 3.0, z + 0.25, color: "#ffd873");
            l.Wire("Courtyard.occupied", $"{name}.turnOn").Wire("Courtyard.empty", $"{name}.turnOff", delay: 2);
        }

        l.Mover("LiftControl", "Lift");
        l.Timer("LiftTimer", 6);
        l.Sound("LiftBell", "hitTick", 20, 4, -10, range: 25);
        l.Wire("LiftTimer.fired", "LiftControl.advance").Wire("LiftControl.arrived", "LiftBell.play");

        l.Lever("LeverA", -4, 1.2, 14.98, yaw: 0, prompt: "pull lever A");
        l.Lever("LeverB", 4, 1.2, 14.98, yaw: 0, prompt: "pull lever B");
        l.Gate("and", "Both");
        l.Gate("not", "Neither");
        l.Door("VaultDoor", 12, 0, 13, 12.3, 2.6, 15, color: "#8a6db0");
        l.Lamp("GreenLamp", -1.2, 2.0, 15, -0.4, 2.3, 15.3, color: "#5cff7a");
        l.Lamp("RedLamp", 0.4, 2.0, 15, 1.2, 2.3, 15.3, on: true, color: "#ff5c5c");
        l.Wire("LeverA.changed", "Both.a").Wire("LeverB.changed", "Both.b").Wire("Both.out", "Neither.in")
            .Wire("Both.true", "VaultDoor.open").Wire("Both.false", "VaultDoor.close").Wire("Both.true", "GreenLamp.turnOn").Wire("Both.false", "GreenLamp.turnOff")
            .Wire("Neither.true", "RedLamp.turnOn").Wire("Neither.false", "RedLamp.turnOff");

        l.Plate("Plate", -1, -15, 1, -13);
        l.Counter("Presses", 3, repeat: true);
        l.Random("Pick", 2);
        l.Text("Heads", "Three presses: heads!");
        l.Text("Tails", "Three presses: tails!");
        l.Wire("Plate.pressed", "Presses.add").Wire("Presses.reached", "Pick.trigger").Wire("Pick.out1", "Heads.show").Wire("Pick.out2", "Tails.show");

        // A timed challenge in the courtyard: stay in it for 3 seconds (it fails if everyone leaves); surviving pays (Zombies points: nothing here).
        l.Challenge("Trial", 3, zone: "Courtyard", reward: 250);
        l.Reward("Prize", points: 100);
        l.Text("TrialText", "Challenge survived!");
        l.Wire("Trial.survived", "Prize.giveAll").Wire("Trial.survived", "TrialText.show");

        k.Test(new JObject(("maxPlayers", 8), ("minHills", 3), ("minTeamSpawns", 4), ("walkFairness", 0.0), ("minMainNavPct", 60), ("botMatchSeconds", 6),
            ("events", new JArray([
                Step(("use", "GateButton"), ("expect", JArray.Of("GateButton.pressed", "Gate.opened")), ("on", JArray.Of("Gate"))),
                Step(("wait", 8.5), ("expect", JArray.Of("Gate.closed")), ("off", JArray.Of("Gate"))),
                Step(("enter", "Courtyard"), ("expect", JArray.Of("Courtyard.entered", "Courtyard.occupied", "Lamp1.on", "Lamp4.on")), ("on", JArray.Of("Lamp1", "Lamp2", "Lamp3", "Lamp4"))),
                Step(("leave", "Courtyard"), ("wait", 2.5), ("expect", JArray.Of("Courtyard.left", "Courtyard.empty", "Lamp1.off")), ("off", JArray.Of("Lamp1", "Lamp4"))),
                Step(("use", "LeverA"), ("wait", 0.6), ("expect", JArray.Of("LeverA.on")), ("expectNot", JArray.Of("Both.true", "VaultDoor.opened")), ("on", JArray.Of("LeverA", "RedLamp", "Neither")), ("off", JArray.Of("VaultDoor"))),
                Step(("use", "LeverB"), ("wait", 0.6), ("expect", JArray.Of("Both.true", "VaultDoor.opened", "Neither.false", "RedLamp.off", "GreenLamp.on")), ("on", JArray.Of("VaultDoor", "GreenLamp")), ("off", JArray.Of("RedLamp"))),
                Step(("use", "LeverA"), ("wait", 0.6), ("expect", JArray.Of("LeverA.off", "Both.false", "VaultDoor.closed", "RedLamp.on")), ("off", JArray.Of("VaultDoor", "GreenLamp"))),
                Step(("do", "LiftTimer.reset"), ("wait", 9), ("expect", JArray.Of("LiftTimer.fired", "LiftControl.started", "LiftControl.arrived"))),
                Step(("enter", "Plate"), ("expect", JArray.Of("Plate.pressed", "Presses.changed"))),
                Step(("leave", "Plate"), ("expect", JArray.Of("Plate.released"))),
                Step(("enter", "Plate")),
                Step(("leave", "Plate")),
                Step(("enter", "Plate"), ("expect", JArray.Of("Presses.reached"))),
                Step(("enter", "Courtyard")),
                Step(("do", "Trial.start"), ("wait", 3.5), ("expect", JArray.Of("Trial.started", "Trial.survived", "Trial.ended", "Prize.given")), ("expectNot", JArray.Of("Trial.failed")), ("off", JArray.Of("Trial"))),
                Step(("do", "Trial.start"), ("wait", 0.3), ("expect", JArray.Of("Trial.started")), ("on", JArray.Of("Trial"))),
                Step(("leave", "Courtyard"), ("expect", JArray.Of("Trial.failed", "Trial.ended")), ("expectNot", JArray.Of("Trial.survived", "Prize.given")), ("off", JArray.Of("Trial")))]))));
    }

    private static JObject Step(params (string Key, object? Value)[] items) => new(items);
}
