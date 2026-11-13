using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Pacing numbers (docs/sandbox/TUTORIAL.md "Pacing"): how long the induction and the first duty take, so a change to
/// them is judged on how it plays as well as how it looks.
/// <list type="bullet">
/// <item><b>Measured.</b> When a real player finishes a step the server logs <c>[pace] induction bus: 95.3 s (Name)</c>,
/// their own time from the step's start to their finishing it (not the wait for the rest of a group), and at the end the
/// total and the slowest step. They are in callofslop.log after any play session.</item>
/// <item><b>Estimated.</b> <see cref="PaceEstimate"/> (the window script's <c>pace</c>, which the play-test tour runs)
/// prints each step's floor with no player: the reading at <see cref="PaceWordsPerSecond"/>, the way on foot between the
/// steps' places at walking and sprinting speed, the bus, and the waits. What the player does at a step comes on top,
/// so a measured step far over its floor is where people are lost or stuck.</item>
/// </list>
/// Nothing here runs per tick: a line is made when a step ends, or when the estimate is asked for.
/// </summary>
public sealed partial class Game
{
    /// <summary>Words a player reads a second while also playing (an assumption: about 180 a minute).</summary>
    public const float PaceWordsPerSecond = 3f;

    /// <summary>Seconds from a bus being called to its standing at the stop (QuarryBusWait's call).</summary>
    private const double PaceBusComes = 12;

    private const int PaceInduction = 0, PaceDuty = 1;
    private static readonly string[] PaceTracks = ["induction", "duty"];
    private readonly double[] _paceSum = new double[2 * MaxPlayers], _paceSlow = new double[2 * MaxPlayers];
    private readonly int[] _paceSteps = new int[2 * MaxPlayers];
    private readonly string[] _paceSlowId = new string[2 * MaxPlayers];
    private readonly Player?[] _paceWho = new Player?[2 * MaxPlayers]; // (whose sums a slot holds: an id is used again when a person leaves)

    /// <summary>
    /// Server: a person finished a step of a track after <paramref name="seconds"/> of their own (negative: they did not,
    /// their group moved on without them); <paramref name="last"/> ends the track with its total.
    /// </summary>
    private void PaceStep(Player p, int track, string step, double seconds, bool last)
    {
        if (p.Bot != null || p.Id >= MaxPlayers)
            return;
        var k = track * MaxPlayers + p.Id;
        if (_paceWho[k] != p)
        {
            PaceClear(k); // (another person had this id before)
            _paceWho[k] = p;
        }
        if (seconds < 0)
            Log($"[pace] {PaceTracks[track]} {step}: not finished, the group moved on ({p.Name})");
        else
        {
            _paceSum[k] += seconds;
            _paceSteps[k]++;
            if (seconds > _paceSlow[k])
            {
                _paceSlow[k] = seconds;
                _paceSlowId[k] = step;
            }
            Log($"[pace] {PaceTracks[track]} {step}: {seconds:0.0} s ({p.Name})");
        }
        if (!last)
            return;
        if (_paceSteps[k] > 0)
            Log($"[pace] {PaceTracks[track]} done: {_paceSteps[k]} steps in {PaceClock(_paceSum[k])}, the slowest {_paceSlowId[k]} at {_paceSlow[k]:0} s ({p.Name}; steps done before this session aren't counted)");
        PaceClear(k);
    }

    private void PaceClear(int k)
    {
        _paceSum[k] = _paceSlow[k] = 0;
        _paceSteps[k] = 0;
    }

    /// <summary>Server: a person starts a track again from its first step (the induction replayed): their sums start again too.</summary>
    private void PaceRestart(int id, int track)
    {
        if (id < MaxPlayers)
            PaceClear(track * MaxPlayers + id);
    }

    /// <summary>A new match: nobody's sums carry over from the last world.</summary>
    private void PaceReset()
    {
        Array.Clear(_paceSum);
        Array.Clear(_paceSlow);
        Array.Clear(_paceSteps);
        Array.Clear(_paceWho);
    }

    private static string PaceClock(double seconds) => $"{(int)(seconds / 60)} min {(int)(seconds % 60):00} s";

    private static int PaceWords(string a, string b = "")
    {
        var n = 0;
        foreach (var s in (ReadOnlySpan<string>)[a, b])
        {
            var inWord = false;
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c))
                    inWord = false;
                else if (!inWord)
                {
                    inWord = true;
                    n++;
                }
            }
        }
        return n;
    }

    /// <summary>
    /// Server (the host's window script): the induction's and the first duty's floor, step by step, with no player.
    /// Distances are straight lines between the steps' places (through the gate when leaving or entering the colony), so
    /// the real way is a little longer. Returns false with the reason said when the world has no colony or quarry.
    /// </summary>
    public bool PaceEstimate(Action<string> say)
    {
        if (!IsServer)
        {
            say("[pace] no estimate: only the host can make it (this window joined a game)");
            return false;
        }
        if (_opColony == 0 || QuarrySpot(QuarrySpotKind.Colony) is not { } colonyStop)
        {
            say("[pace] no estimate: this world has no penal colony (a survival world on the story's home map has one)");
            return false;
        }
        if (QuarrySpot(QuarrySpotKind.Quarry) is not { } quarryStop)
        {
            say("[pace] no estimate: this world has no quarry works");
            return false;
        }
        say($"[pace] places: the cell block to the gate {Vector3.Distance(_opWake, _opGate):0} m, the gate to the colony's bus stop {Vector3.Distance(_opGate, colonyStop):0} m, "
            + $"the colony's stop to the quarry's {Vector3.Distance(colonyStop, quarryStop):0} m (the bus: {PaceBusComes:0} s to come, {QuarryRideSeconds:0} s ride)"
            + (QuarrySpot(QuarrySpotKind.Wall) is { } face ? $", the quarry's stop to the mine face {Vector3.Distance(quarryStop, face):0} m" : ""));
        say($"[pace] assumed: reading {PaceWordsPerSecond:0.#} words a second, walking {World.WalkSpeed:0.#} m/s, sprinting {World.SprintSpeed:0.#} m/s; what the player does at a step is not counted");

        var at = _opWake;
        var inColony = true;
        double read = 0, foot = 0, bus = 0, wait = 0, total = 0;

        // One step's floor: its words, the way to its place, its bus and its wait.
        void Step(string track, string id, int words, Vector3? place, bool placeInColony, bool ride, double seconds, string note)
        {
            var r = words / PaceWordsPerSecond;
            var line = $"[pace] estimate {track} {id}: read {words} words {r:0} s";
            double metres = 0, b = 0;
            if (place is { } to)
            {
                // Through the gate when the way crosses the colony's fence.
                metres = inColony != placeInColony ? Vector3.Distance(at, _opGate) + Vector3.Distance(_opGate, to) : Vector3.Distance(at, to);
                at = to;
                inColony = placeInColony;
            }
            var walk = metres / World.WalkSpeed;
            if (metres >= 1)
                line += $", {metres:0} m on foot {walk:0} s ({metres / World.SprintSpeed:0} s sprinting)";
            if (ride)
            {
                b = PaceBusComes + QuarryRideSeconds;
                var other = Vector3.Distance(at, colonyStop) < Vector3.Distance(at, quarryStop) ? quarryStop : colonyStop;
                at = other;
                inColony = false;
                line += $", the bus {b:0} s";
            }
            if (seconds > 0)
                line += $", a wait of {seconds:0} s";
            var floor = Math.Max(r + walk + b + seconds, TutorialMinStep);
            say($"{line}: floor {floor:0} s{note}");
            read += r;
            foot += walk;
            bus += b;
            wait += seconds;
            total += floor;
        }

        void Total(string track, int steps)
        {
            say($"[pace] estimate {track}: floor {PaceClock(total)} over {steps} steps (reading {read:0} s, on foot {foot:0} s, the bus {bus:0} s, waits {wait:0} s)");
            read = foot = bus = wait = total = 0;
        }

        if (Tutorial.Error is { } bad)
            say($"[pace] no induction: {bad}");
        else
        {
            foreach (var s in Tutorial.Steps)
            {
                var place = s.Cue == TutorialCue.Ride || s.Spot != QuarrySpotKind.None ? QuarrySpot(s.Spot == QuarrySpotKind.None ? QuarrySpotKind.Colony : s.Spot) : null;
                Step("induction", s.Id, PaceWords(s.Text, s.Warden), place, s.Spot is QuarrySpotKind.Colony or QuarrySpotKind.Stop, s.Cue == TutorialCue.Ride, s.Cue == TutorialCue.Wait ? s.Seconds : 0, "");
            }
            Total("induction", Tutorial.Steps.Length);
        }

        var duty = QuarryScript.Current;
        if (duty.Problem.Length > 0)
        {
            say($"[pace] no duty: {duty.Problem}");
            return true;
        }
        // The duty follows on where the induction ended: at the works its first step (the bus out) is passed over.
        var skipBus = duty.Steps.Length > 1 && duty.Steps[0].Cue == QuarryCue.Ride && Vector3.Distance(at, quarryStop) < QuarryAtWorks;
        var counted = 0;
        for (var k = skipBus ? 1 : 0; k < duty.Steps.Length; k++)
        {
            var s = duty.Steps[k];
            var place = s.Cue == QuarryCue.Bunk ? QuarryBunk() : s.Spot != QuarrySpotKind.None ? QuarrySpot(s.Spot) : null;
            Step("duty", s.Id, PaceWords(s.Text, s.Say), place, s.Cue == QuarryCue.Bunk || s.Spot is QuarrySpotKind.Colony or QuarrySpotKind.Stop, s.Cue == QuarryCue.Ride,
                s.Cue == QuarryCue.Wait ? s.Seconds : 0, s.Cue == QuarryCue.Bunk && place == null ? " (no free Bunk found: its walk is not counted)" : "");
            counted++;
        }
        Total("duty", counted);
        return true;
    }
}
