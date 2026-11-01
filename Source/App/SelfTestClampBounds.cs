using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Clamp bounds" (board item dev-crash-sweep-cs01): <c>Math.Clamp</c> throws when its lower bound is above its upper one,
    /// and the developer's crash was such a pair a hair inverted by rounding (ConstructRide.GrabOn on a narrow face). The code
    /// review found the same class elsewhere; each is driven here with the numbers that inverted: <see cref="SafeMath"/>
    /// itself, an automatic gearbox told "gear 0.5" by a wire (its top gear came out -1: a crash in the physics tick), and,
    /// kept safe the same way, a bullet mark on a face exactly its size (ImpactDecals.FitOnFace) and a radial menu ring with no options.
    /// </summary>
    private static void CheckClampBounds()
    {
        var m = SafeMath.Clamp(5f, 0.21f, 0.20999998f);
        if (!(m > 0.2099f && m < 0.2101f) || SafeMath.Clamp(2f, 0f, 1f) != 1f || SafeMath.Clamp(-2f, 0f, 1f) != 0f || SafeMath.Clamp(3, 0, -1) != 0 || SafeMath.Clamp(7, 0, 4) != 4)
            Fail("clamp bounds: SafeMath.Clamp gives the wrong value");

        // A mark the size of the face (or the face a hair narrower): min + half and max - half meet, and rounding inverted them.
        var rng = new Random(1031);
        int inverted = 0, threw = 0;
        string firstThrow = "";
        for (var i = 0; i < 4000; i++)
        {
            var size = 0.02f + (float)rng.NextDouble() * 0.6f;
            var centre = new Vector3((float)(rng.NextDouble() * 400 - 200), (float)(rng.NextDouble() * 40), (float)(rng.NextDouble() * 400 - 200));
            var box = new Box(centre, new Vector3(size * 0.5f, 0.3f, size * 0.5f));
            var pos = centre + new Vector3((float)(rng.NextDouble() - 0.5) * size, 0.3f, (float)(rng.NextDouble() - 0.5) * size);
            var s = size;
            var e = MathF.Min(box.Max.X - box.Min.X, box.Max.Z - box.Min.Z) * 0.5f; // (the function's own half size)
            if (box.Min.X + e > box.Max.X - e || box.Min.Z + e > box.Max.Z - e)
                inverted++;
            try
            {
                ImpactDecals.FitOnFace(box, ref pos, Vector3.UnitY, ref s);
            }
            catch (Exception ex)
            {
                if (threw++ == 0)
                    firstThrow = $"{ex.GetType().Name}: {ex.Message}";
            }
        }
        if (threw > 0)
            Fail($"clamp bounds: a bullet mark on a face its own size threw {threw} time(s) of 4000 ({firstThrow})");

        // An automatic gearbox with a wire telling it gear 0.5 (rounds to 0: "top gear" -1).
        var w = new ConstructWorld();
        var autoWhy = "";
        if (DriveSpawn(w, Mule("Engine", "Automatic Gearbox"), 0, 0, MathF.PI / 2, "the automatic mule") is { } car && car.Transmission >= 0)
        {
            foreach (var signal in new[] { 0.5f, 0.49f, 1.4f, 100f })
            {
                ref var tp = ref car.Parts[car.Transmission];
                tp.InCount = 1;
                tp.In = signal;
                try
                {
                    ConstructDrive.Tick(car, true, 1f, 0f, 6f, 1f / 60);
                }
                catch (Exception ex)
                {
                    autoWhy = $"signal {signal}: {ex.GetType().Name}: {ex.Message}";
                    break;
                }
            }
        }
        else
            autoWhy = "the automatic mule didn't build";
        if (autoWhy.Length > 0)
            Fail($"clamp bounds: an automatic gearbox wired to a gear signal: {autoWhy}");

        // A radial menu whose ring has no options, the pointer swept round it.
        var menu = new RadialMenu(4);
        menu.Items[0].Options = [];
        menu.Items[1].Options = [new RadialMenu.Item { Label = "a", Options = [] }];
        menu.Open();
        try
        {
            for (var k = 0; k < 400; k++)
            {
                var a = k * 0.37f;
                menu.Move(new Vector2(MathF.Sin(a), -MathF.Cos(a)) * (k % 7 * 12f), 1f / 60);
            }
        }
        catch (Exception ex)
        {
            Fail($"clamp bounds: a radial menu ring with no options: {ex.GetType().Name}: {ex.Message}");
        }
        Line($"clamp bounds: 4000 face-sized marks ({inverted} with inverted bounds), none threw; automatic gearbox wired to 0.5 and the empty radial ring fine");
    }
}
