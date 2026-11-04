using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>A minimal map file with <paramref name="skyKey"/> (e.g. "\"skybox\": \"dusk\",") in it, as the Sky Test map's folder.</summary>
    private static Map SkyProbe(string skyKey, string source = "Dev/SkyProbe.json") => MapFile.Load($$"""
        { "name": "Sky Probe", "halfX": 20, "halfZ": 20, {{skyKey}} "ops": [
          { "op": "Ground", "color": [0.4, 0.4, 0.4] },
          { "op": "Spawn", "x": 5, "z": 5 } ] }
        """, source);
}
