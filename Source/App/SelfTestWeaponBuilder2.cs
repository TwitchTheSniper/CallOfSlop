using System.Globalization;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
