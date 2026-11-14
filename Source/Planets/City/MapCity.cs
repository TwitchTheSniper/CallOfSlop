namespace CallOfSlop;

/// <summary>A map with a city on its planetoid (docs/planets/CITY.md): the top-level "city" object.</summary>
public sealed partial class Map
{
    /// <summary>The city's numbers (null: no city). Only a planetoid map has one.</summary>
    public CitySpec? CitySpec;

    [NotBaked] private CityPlan? _cityPlan;
    [NotBaked] private CityGeometry? _cityGeometry;
    [NotBaked] private readonly object _cityLock = new();

    /// <summary>The city's plan, worked out on first use (the same on every PC).</summary>
    public CityPlan? City
    {
        get
        {
            if (CitySpec == null || Planetoid == null)
                return null;
            lock (_cityLock)
                return _cityPlan ??= CityPlan.Make(CitySpec, Planetoid);
        }
    }

    /// <summary>The city's meshes and colliders, made on first use.</summary>
    public CityGeometry? CityGeometry
    {
        get
        {
            if (City is not { } plan)
                return null;
            lock (_cityLock)
            {
                if (_cityGeometry == null)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _cityGeometry = CallOfSlop.CityGeometry.Make(plan);
                    Console.WriteLine($"[city] {Name}: {plan.Buildings.Length} buildings, {plan.Masses.Length} masses, {plan.Streets.Length} streets, "
                        + $"{plan.Crossings.Length} crossings; {_cityGeometry.Triangles} triangles in {_cityGeometry.Chunks.Length} chunks, "
                        + $"{_cityGeometry.CollisionTriangles} solid, {sw.Elapsed.TotalMilliseconds:0} ms"
                        + $"; {plan.Pads.Length} pads" + (plan.Blocks.FirstOrDefault(b => b.Kind == 2) is { Kind: 2 } mk ? $", market ({mk.Min.X:0},{mk.Min.Y:0})-({mk.Max.X:0},{mk.Max.Y:0})" : "")
                        + (plan.Rail is { } rail ? $"; railway {rail.Length:0} m, stations at {string.Join(" ", rail.Stations.Select(st => $"({st.X:0},{st.Y:0})"))}" : ""));
                }
                return _cityGeometry;
            }
        }
    }
}
